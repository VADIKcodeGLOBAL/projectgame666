using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// "anims" (Tools > Characters > Build Animation Library): everything under Assets/Art/Animations into the
/// <see cref="AnimationLibrary"/> and the enemy controller.
///  1 test clips: humanoid clips made here from muscle curves, one per slot (Assets/Art/Animations/_Test) — the controller's
///    slots, and stand-ins for a tag until a real clip has it;
///  2 every humanoid clip in the folder (imported by AnimationImport) into the library: tag from the name, source = its folder,
///    licence from the folder's SOURCE.md; entries edited by hand (tag, weight, on/off) keep the edits; deleted clips leave;
///  3 ground speed of walks, runs and climbs measured on the reference character (CharacterRigSetup): root motion if the clip
///    travels, otherwise the feet (each pushes back one step length per cycle: speed = 2 x foot travel / cycle);
///  4 the controller (Assets/Data/Animations/Enemy.controller), when missing or out of date;
///  5 a sheet of every clip on the reference character, four moments each: Logs/shots/anims_sheet*.png.
/// Adding animations: drop humanoid FBX files into Assets/Art/Animations/&lt;Source&gt;/ (with a SOURCE.md naming the licence) and run "anims".
/// </summary>
public static class AnimationLibraryBuilder
{
    public const string LibraryPath = "Assets/Data/Animations/AnimationLibrary.asset";
    public const string ControllerPath = "Assets/Data/Animations/Enemy.controller";
    public const string TestDir = AnimationImport.Root + "/_Test";
    const string ControllerVersion = "enemy-controller-v1";
    const int Fps = 30;

    [MenuItem("Tools/Characters/Build Animation Library")]
    static void Menu() { Build(); }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path);
        return a;
    }

    public static void Build()
    {
        EditorAutomation.Log("ANIMS build");
        var character = CharacterRigSetup.Reference;
        var avatar = character != null ? CharacterRigSetup.AvatarOf(character) : null;
        if (avatar == null || !avatar.isHuman) { EditorAutomation.Log("ANIMS_FAILED: no humanoid avatar for " + (character != null ? character.name : "any character") + " (run \"chars\" first)"); return; }

        var scene = PreviewShots.Begin();
        try
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(character.asset);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            var anim = inst.GetComponent<Animator>();                        // a humanoid import has one; Unity's null is not C#'s null
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.avatar = avatar; anim.applyRootMotion = false;

            var slots = MakeTestClips(inst, avatar);
            var lib = LoadOrCreate<AnimationLibrary>(LibraryPath);
            UpdateLibrary(lib, inst, anim);
            lib.referenceHumanScale = anim.humanScale;
            var ctrl = EnsureController(slots);
            EditorUtility.SetDirty(lib); AssetDatabase.SaveAssets();

            foreach (AnimTag t in Enum.GetValues(typeof(AnimTag)))
            {
                var real = lib.clips.Count(e => e.enabled && e.clip != null && e.tag == t && !e.test);
                var used = lib.Of(t);
                if (real > 0 || used.Count > 0)
                    EditorAutomation.Log("  " + t + ": " + (real > 0 ? real + " clip(s)" : "test clip only") + " — " + string.Join(", ", used.Select(e => e.clip.name + (e.speed > 0f ? " " + e.speed.ToString("0.0") + " m/s" : "")).ToArray()));
            }
            Sheet(scene, inst, lib);
            EditorAutomation.Log("ANIMS_OK (" + lib.clips.Count(e => !e.test) + " clips, " + lib.clips.Count(e => e.test) + " test clips, controller " + Path.GetFileName(AssetDatabase.GetAssetPath(ctrl)) + ")");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>"anim-probe &lt;clip name&gt;": where the feet and hands of the reference character are (root space, m) at five moments of a clip.</summary>
    public static void Probe(string clipName)
    {
        var character = CharacterRigSetup.Reference;
        var avatar = character != null ? CharacterRigSetup.AvatarOf(character) : null;
        var lib = AssetDatabase.LoadAssetAtPath<AnimationLibrary>(LibraryPath);
        var entry = lib != null ? lib.clips.FirstOrDefault(e => e.clip != null && e.clip.name.Equals(clipName, StringComparison.OrdinalIgnoreCase)) : null;
        if (avatar == null || entry == null) { EditorAutomation.Log("PROBE_FAILED: no avatar or no clip " + clipName); return; }
        var scene = PreviewShots.Begin();
        try
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(character.asset), scene);
            var anim = inst.GetComponent<Animator>(); if (anim == null) anim = inst.AddComponent<Animator>();
            anim.avatar = avatar;
            var clip = entry.clip;
            EditorAutomation.Log("  " + clip.name + ": " + clip.length.ToString("0.00") + " s, human " + clip.humanMotion + ", loop " + clip.isLooping + ", curves " + AnimationUtility.GetCurveBindings(clip).Length
                                 + ", human scale " + anim.humanScale.ToString("0.000"));
            Func<HumanBodyBones, string> at = b => { var t = anim.GetBoneTransform(b); return t == null ? "-" : inst.transform.InverseTransformPoint(t.position).ToString("F2"); };
            for (int k = 0; k <= 4; k++)
            {
                float time = clip.length * k / 4f;
                clip.SampleAnimation(inst, time);
                EditorAutomation.Log("    t=" + time.ToString("0.00") + "  L foot " + at(HumanBodyBones.LeftFoot) + "  R foot " + at(HumanBodyBones.RightFoot)
                                     + "  L hand " + at(HumanBodyBones.LeftHand) + "  R hand " + at(HumanBodyBones.RightHand) + "  hips " + at(HumanBodyBones.Hips));
            }
            EditorAutomation.Log("PROBE_OK");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    // ------------------------------------------------------------------ 1. test clips
    /// <summary>The slot clips, in AnimTag order (Emote has none).</summary>
    public static AnimationClip[] Slots()
    {
        var names = Enum.GetNames(typeof(AnimTag));
        return names.Select(n => AssetDatabase.LoadAssetAtPath<AnimationClip>(TestDir + "/Test_" + n + ".anim")).ToArray();
    }

    static AnimationClip[] MakeTestClips(GameObject inst, Avatar avatar)
    {
        Directory.CreateDirectory(TestDir);
        // the body's height in the rest pose, in the normalised human space the clips are written in
        var handler = new HumanPoseHandler(avatar, inst.transform);
        var rest = new HumanPose(); handler.GetHumanPose(ref rest); handler.Dispose();
        float bodyY = rest.bodyPosition.y;
        var missing = new HashSet<string>();

        Func<string, float, bool, Func<float, Dictionary<string, float>>, Func<float, Vector3>, Func<float, Quaternion>, AnimationClip> make = (name, length, loop, muscles, rootT, rootQ) =>
        {
            string path = TestDir + "/Test_" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.frameRate = Fps;
            int frames = Mathf.Max(2, Mathf.RoundToInt(length * Fps));
            var curves = new Dictionary<string, AnimationCurve>();
            Action<string, float, float> key = (prop, t, v) => { AnimationCurve c; if (!curves.TryGetValue(prop, out c)) curves[prop] = c = new AnimationCurve(); c.AddKey(t, v); };
            for (int f = 0; f <= frames; f++)
            {
                float t = f / (float)Fps, u = Mathf.Min(t, length);
                foreach (var kv in muscles(u))
                {
                    if (Array.IndexOf(HumanTrait.MuscleName, kv.Key) < 0) { missing.Add(kv.Key); continue; }
                    key(kv.Key, t, kv.Value);
                }
                Vector3 p = rootT(u); Quaternion q = rootQ(u);
                key("RootT.x", t, p.x); key("RootT.y", t, p.y); key("RootT.z", t, p.z);
                key("RootQ.x", t, q.x); key("RootQ.y", t, q.y); key("RootQ.z", t, q.z); key("RootQ.w", t, q.w);
            }
            foreach (var kv in curves) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), kv.Value);
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = loop; s.keepOriginalOrientation = true; s.keepOriginalPositionY = true; s.keepOriginalPositionXZ = true;
            s.loopBlendOrientation = true; s.loopBlendPositionY = true; s.loopBlendPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            EditorUtility.SetDirty(clip);
            return clip;
        };

        Func<float, Vector3> still = t => new Vector3(0f, bodyY, 0f);
        Func<float, Quaternion> upright = t => Quaternion.identity;
        const float Tau = Mathf.PI * 2f;
        // a relaxed stance the other clips start from
        Func<Dictionary<string, float>> stance = () => new Dictionary<string, float>
        {
            { "Left Arm Down-Up", -0.55f }, { "Right Arm Down-Up", -0.55f }, { "Left Arm Front-Back", 0.1f }, { "Right Arm Front-Back", 0.1f },
            { "Left Forearm Stretch", 0.55f }, { "Right Forearm Stretch", 0.55f },
            { "Left Upper Leg Front-Back", 0f }, { "Right Upper Leg Front-Back", 0f }, { "Left Lower Leg Stretch", 0.95f }, { "Right Lower Leg Stretch", 0.95f },
            { "Spine Front-Back", 0.05f }, { "Chest Front-Back", 0f }, { "Head Nod Down-Up", 0f },
        };
        Func<Dictionary<string, float>, string, float, Dictionary<string, float>> set = (d, k, v) => { d[k] = v; return d; };

        var clips = new AnimationClip[Enum.GetValues(typeof(AnimTag)).Length];
        clips[(int)AnimTag.Idle] = make("Idle", 2.4f, true, t =>
        {
            var d = stance(); float b = Mathf.Sin(t / 2.4f * Tau);
            d["Spine Front-Back"] = 0.05f + 0.03f * b; d["Chest Front-Back"] = 0.03f * b; d["Left Arm Down-Up"] = -0.55f + 0.03f * b; d["Right Arm Down-Up"] = -0.55f + 0.03f * b;
            return d;
        }, t => new Vector3(0f, bodyY - 0.005f * (1f + Mathf.Sin(t / 2.4f * Tau)), 0f), upright);

        Func<float, float, float, float, Dictionary<string, float>> gait = (t, period, legs, arms) =>
        {
            var d = stance(); float p = t / period * Tau, s = Mathf.Sin(p), c = Mathf.Cos(p);
            d["Left Upper Leg Front-Back"] = legs * s; d["Right Upper Leg Front-Back"] = -legs * s;
            d["Left Lower Leg Stretch"] = 0.95f - legs * 3f * Mathf.Max(0f, c); d["Right Lower Leg Stretch"] = 0.95f - legs * 3f * Mathf.Max(0f, -c);
            d["Left Arm Front-Back"] = -arms * s; d["Right Arm Front-Back"] = arms * s;
            d["Spine Twist Left-Right"] = 0.12f * s; d["Spine Front-Back"] = 0.05f + legs * 0.35f;
            return d;
        };
        clips[(int)AnimTag.Walk] = make("Walk", 1.1f, true, t => gait(t, 1.1f, 0.2f, 0.3f), t => new Vector3(0f, bodyY - 0.02f * (0.5f + 0.5f * Mathf.Cos(t / 1.1f * 2f * Tau)), 0f), upright);
        clips[(int)AnimTag.Run] = make("Run", 0.7f, true, t =>
        {
            var d = gait(t, 0.7f, 0.34f, 0.55f);
            d["Left Forearm Stretch"] = -0.3f; d["Right Forearm Stretch"] = -0.3f; d["Left Arm Down-Up"] = -0.35f; d["Right Arm Down-Up"] = -0.35f;
            return d;
        }, t => new Vector3(0f, bodyY - 0.05f - 0.035f * (0.5f + 0.5f * Mathf.Cos(t / 0.7f * 2f * Tau)), 0f), upright);
        clips[(int)AnimTag.Climb] = make("Climb", 1.0f, true, t =>
        {
            var d = stance(); float p = t * Tau, s = Mathf.Sin(p), c = Mathf.Cos(p);
            d["Left Arm Down-Up"] = 0.65f + 0.3f * s; d["Right Arm Down-Up"] = 0.65f - 0.3f * s; d["Left Arm Front-Back"] = 0.7f; d["Right Arm Front-Back"] = 0.7f;
            d["Left Forearm Stretch"] = 0.3f + 0.4f * s; d["Right Forearm Stretch"] = 0.3f - 0.4f * s;
            d["Left Upper Leg Front-Back"] = 0.45f + 0.35f * s; d["Right Upper Leg Front-Back"] = 0.45f - 0.35f * s;
            d["Left Lower Leg Stretch"] = 0.1f + 0.5f * c; d["Right Lower Leg Stretch"] = 0.1f - 0.5f * c;
            d["Spine Front-Back"] = 0.45f; d["Chest Front-Back"] = 0.2f; d["Head Nod Down-Up"] = -0.4f;
            return d;
        }, t => new Vector3(0f, bodyY * 0.8f, 0f), upright);
        clips[(int)AnimTag.Attack] = make("Attack", 0.9f, false, t =>
        {
            var d = stance();
            float raise = Mathf.SmoothStep(0f, 1f, t / 0.35f), slam = Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.18f), back = Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.3f);
            d["Right Arm Down-Up"] = Mathf.Lerp(Mathf.Lerp(-0.55f, 1f, raise), -0.1f, slam) * (1f - back) + -0.55f * back;
            d["Right Arm Front-Back"] = Mathf.Lerp(0.1f, 0.9f, slam) * (1f - back) + 0.1f * back;
            d["Right Forearm Stretch"] = Mathf.Lerp(Mathf.Lerp(0.55f, -0.2f, raise), 1f, slam) * (1f - back) + 0.55f * back;
            d["Spine Front-Back"] = Mathf.Lerp(Mathf.Lerp(0.05f, -0.2f, raise), 0.45f, slam) * (1f - back) + 0.05f * back;
            d["Spine Twist Left-Right"] = Mathf.Lerp(Mathf.Lerp(0f, -0.4f, raise), 0.4f, slam) * (1f - back);
            d["Left Upper Leg Front-Back"] = 0.25f * slam * (1f - back); d["Left Lower Leg Stretch"] = 0.95f - 0.4f * slam * (1f - back);
            return d;
        }, still, upright);
        clips[(int)AnimTag.Hit] = make("Hit", 0.45f, false, t =>
        {
            var d = stance(); float k = Mathf.Sin(Mathf.Clamp01(t / 0.45f) * Mathf.PI) * (1f - Mathf.Clamp01(t / 0.45f) * 0.3f);
            d["Spine Front-Back"] = 0.05f - 0.55f * k; d["Chest Front-Back"] = -0.4f * k; d["Head Nod Down-Up"] = 0.5f * k;
            d["Left Arm Down-Up"] = -0.55f + 0.4f * k; d["Right Arm Down-Up"] = -0.55f + 0.4f * k;
            return d;
        }, still, upright);
        clips[(int)AnimTag.Stagger] = make("Stagger", 0.9f, false, t =>
        {
            var d = stance(); float k = Mathf.Sin(Mathf.Clamp01(t / 0.9f) * Mathf.PI);
            d["Spine Front-Back"] = 0.05f - 0.5f * k; d["Head Nod Down-Up"] = 0.4f * k; d["Left Arm Down-Up"] = -0.55f + 0.7f * k; d["Right Arm Down-Up"] = -0.55f + 0.6f * k;
            d["Left Upper Leg Front-Back"] = -0.2f * k; d["Right Upper Leg Front-Back"] = 0.1f * k; d["Right Lower Leg Stretch"] = 0.95f - 0.4f * k;
            return d;
        }, t => new Vector3(0f, bodyY - 0.04f * Mathf.Sin(Mathf.Clamp01(t / 0.9f) * Mathf.PI), 0f), upright);
        clips[(int)AnimTag.Death] = make("Death", 1.5f, false, t =>
        {
            var d = stance(); float k = Mathf.SmoothStep(0f, 1f, t / 1.1f);
            d["Left Arm Down-Up"] = -0.55f + 0.9f * k; d["Right Arm Down-Up"] = -0.55f + 0.8f * k; d["Left Forearm Stretch"] = 0.55f + 0.4f * k;
            d["Left Upper Leg Front-Back"] = 0.35f * k; d["Right Upper Leg Front-Back"] = 0.15f * k; d["Left Lower Leg Stretch"] = 0.95f - 0.6f * k;
            d["Spine Front-Back"] = 0.05f - 0.3f * k; d["Head Nod Down-Up"] = 0.5f * k;
            return d;
        }, t =>
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 1.1f), fall = k * k;
            return new Vector3(0f, Mathf.Lerp(bodyY, bodyY * 0.18f, fall), -bodyY * 0.55f * fall);
        }, t => Quaternion.AngleAxis(-84f * Mathf.Pow(Mathf.SmoothStep(0f, 1f, t / 1.1f), 1.6f), Vector3.right));
        if (missing.Count > 0) EditorAutomation.Log("  (test clips: no muscle " + string.Join(", ", missing.ToArray()) + ")");
        AssetDatabase.SaveAssets();
        return clips;
    }

    // ------------------------------------------------------------------ 2-3. library
    static string LicenseOf(string sourceDir)
    {
        string md = Path.Combine(sourceDir, "SOURCE.md");
        if (!File.Exists(md)) return "";
        foreach (var line in File.ReadAllLines(md))
        {
            int i = line.IndexOf("License:", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return line.Substring(i + 8).Trim();
        }
        return "";
    }

    static void UpdateLibrary(AnimationLibrary lib, GameObject inst, Animator anim)
    {
        var found = new List<KeyValuePair<AnimationClip, string>>();
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimationImport.Root }).Concat(AssetDatabase.FindAssets("t:Model", new[] { AnimationImport.Root })).Distinct())
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                if (!c.name.StartsWith("__preview__")) found.Add(new KeyValuePair<AnimationClip, string>(c, path));
        }
        int added = 0, gone = lib.clips.RemoveAll(e => e == null || e.clip == null);
        foreach (var kv in found)
        {
            var clip = kv.Key; string path = kv.Value;
            string rel = path.Substring(AnimationImport.Root.Length + 1), source = rel.Contains("/") ? rel.Substring(0, rel.IndexOf('/')) : "";
            bool test = path.StartsWith(TestDir + "/");
            if (!clip.humanMotion) { EditorAutomation.Log("  skipped (not humanoid: check its avatar in the import settings): " + path + " / " + clip.name); continue; }
            var e = lib.clips.FirstOrDefault(x => x.clip == clip);
            if (e == null)
            {
                e = new AnimationLibrary.Entry { clip = clip, test = test, weight = 1f, enabled = true };
                e.tag = test ? (AnimTag)Enum.Parse(typeof(AnimTag), clip.name.Substring(5)) : AnimTags.Guess(Path.GetFileNameWithoutExtension(path), clip.name);
                lib.clips.Add(e); added++;
            }
            e.source = test ? "project (test clip)" : source;
            e.license = test ? "made by the project" : LicenseOf(AnimationImport.Root + "/" + source);
            e.file = path; e.loop = clip.isLooping;
            if (!test && string.IsNullOrEmpty(e.license)) EditorAutomation.Log("  WARNING no licence for " + path + " (add " + AnimationImport.Root + "/" + source + "/SOURCE.md with a 'License:' line)");
            e.speed = (e.tag == AnimTag.Walk || e.tag == AnimTag.Run || e.tag == AnimTag.Climb) ? MeasureSpeed(clip, inst, anim) : 0f;
        }
        lib.clips.Sort((a, b) => a.tag != b.tag ? a.tag.CompareTo(b.tag) : a.test != b.test ? a.test.CompareTo(b.test) : string.Compare(a.clip.name, b.clip.name, StringComparison.Ordinal));
        EditorAutomation.Log("  library: " + lib.clips.Count + " entries (" + added + " new, " + gone + " gone)");
    }

    /// <summary>Ground speed the clip is made for on this character, m/s.</summary>
    public static float MeasureSpeed(AnimationClip clip, GameObject inst, Animator anim)
    {
        Vector3 avg = clip.averageSpeed;
        float rootMotion = new Vector2(avg.x, avg.z).magnitude * (clip.humanMotion ? anim.humanScale : 1f);
        if (rootMotion > 0.3f) return rootMotion;
        var foot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        if (foot == null || clip.length <= 0f) return 0f;
        float lo = float.MaxValue, hi = float.MinValue; const int n = 40;
        for (int i = 0; i <= n; i++)
        {
            clip.SampleAnimation(inst, clip.length * i / n);
            float z = inst.transform.InverseTransformPoint(foot.position).z;
            lo = Mathf.Min(lo, z); hi = Mathf.Max(hi, z);
        }
        return 2f * (hi - lo) / clip.length;
    }

    // ------------------------------------------------------------------ 4. controller
    static AnimatorStateTransition To(AnimatorState from, AnimatorState to, float duration, float exitTime = -1f)
    {
        var tr = from.AddTransition(to); tr.duration = duration; tr.hasExitTime = exitTime >= 0f; if (exitTime >= 0f) tr.exitTime = exitTime;
        return tr;
    }

    public static AnimatorController EnsureController(AnimationClip[] slots)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl != null && AssetDatabase.GetLabels(ctrl).Contains(ControllerVersion)) return ctrl;
        if (ctrl != null) AssetDatabase.DeleteAsset(ControllerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
        ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Gait", AnimatorControllerParameterType.Float);
        ctrl.AddParameter(new AnimatorControllerParameter { name = "Play", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        ctrl.AddParameter("Climb", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Stagger", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        Func<AnimTag, AnimationClip> slot = t => slots[(int)t];

        // ---- base layer: locomotion, climbing, the blow, the stagger, death
        var sm = ctrl.layers[0].stateMachine;
        BlendTree tree;
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = "Gait"; tree.useAutomaticThresholds = false;
        tree.AddChild(slot(AnimTag.Idle), 0f); tree.AddChild(slot(AnimTag.Walk), 1f); tree.AddChild(slot(AnimTag.Run), 2f);
        loco.speedParameterActive = true; loco.speedParameter = "Play";
        sm.defaultState = loco;
        var climb = sm.AddState("Climb"); climb.motion = slot(AnimTag.Climb); climb.speedParameterActive = true; climb.speedParameter = "Play";
        var attack = sm.AddState("Attack"); attack.motion = slot(AnimTag.Attack);
        var stagger = sm.AddState("Stagger"); stagger.motion = slot(AnimTag.Stagger);
        var death = sm.AddState("Death"); death.motion = slot(AnimTag.Death);
        To(loco, climb, 0.25f).AddCondition(AnimatorConditionMode.If, 0f, "Climb");
        To(climb, loco, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Climb");
        To(attack, loco, 0.15f, 0.9f);
        To(stagger, loco, 0.2f, 0.9f);
        Action<AnimatorState, string, float> fromAny = (state, trigger, duration) =>
        {
            var tr = sm.AddAnyStateTransition(state); tr.duration = duration; tr.canTransitionToSelf = false; tr.hasExitTime = false;
            if (trigger == "Dead") tr.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            else { tr.AddCondition(AnimatorConditionMode.If, 0f, trigger); tr.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead"); }
        };
        fromAny(death, "Dead", 0.15f);
        fromAny(attack, "Attack", 0.1f);
        fromAny(stagger, "Stagger", 0.1f);

        // ---- hit reactions over the upper body: the legs keep walking
        var mask = new AvatarMask { name = "UpperBody" };
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm
                                                 || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
        }
        AssetDatabase.AddObjectToAsset(mask, ctrl);
        ctrl.AddLayer("Hit");
        var layers = ctrl.layers; layers[1].defaultWeight = 1f; layers[1].avatarMask = mask; layers[1].blendingMode = AnimatorLayerBlendingMode.Override; ctrl.layers = layers;
        var hsm = ctrl.layers[1].stateMachine;
        var empty = hsm.AddState("Empty"); empty.writeDefaultValues = false; hsm.defaultState = empty;
        var hit = hsm.AddState("Hit"); hit.motion = slot(AnimTag.Hit); hit.writeDefaultValues = false;
        var toHit = To(empty, hit, 0.05f); toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit"); toHit.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
        To(hit, empty, 0.2f, 0.85f);

        AssetDatabase.SetLabels(ctrl, new[] { ControllerVersion });
        EditorUtility.SetDirty(ctrl); AssetDatabase.SaveAssets();
        EditorAutomation.Log("  controller built: locomotion blend (idle / walk / run), climb, attack, stagger, death; hit reactions on the upper body");
        return ctrl;
    }

    // ------------------------------------------------------------------ 5. sheet
    static void Sheet(UnityEngine.SceneManagement.Scene scene, GameObject inst, AnimationLibrary lib)
    {
        var entries = lib.clips.Where(e => e.enabled && e.clip != null).ToList();
        Bounds b = PreviewShots.BoundsOf(inst);
        float h = b.size.y * 1.25f; Vector3 c = new Vector3(inst.transform.position.x, h * 0.42f, inst.transform.position.z), home = inst.transform.position;
        const int perPage = 8;
        for (int page = 0; page * perPage < entries.Count; page++)
        {
            var frames = new List<PreviewShots.Frame>();
            foreach (var e in entries.Skip(page * perPage).Take(perPage))
            {
                var clip = e.clip;
                for (int k = 0; k < 4; k++)
                {
                    float t = clip.length * (clip.isLooping ? k / 4f : k / 3f);
                    // sampling applies the clip's ground travel to the root: put it back (the game does not apply it either)
                    frames.Add(new PreviewShots.Frame(c, h, new Vector3(1f, 0.08f, 0.6f), () => { clip.SampleAnimation(inst, t); inst.transform.position = home; inst.transform.rotation = Quaternion.identity; PreviewShots.Freeze(inst); }));
                }
            }
            PreviewShots.RenderGrid(scene, "anims_sheet" + (page > 0 ? "_" + (page + 1) : ""), 4, 230, 300, frames.ToArray());
        }
        EditorAutomation.Log("  sheet: " + entries.Count + " clips x 4 moments (rows in the library's order: " + string.Join(", ", entries.Select(e => e.clip.name).ToArray()) + ")");
    }
}

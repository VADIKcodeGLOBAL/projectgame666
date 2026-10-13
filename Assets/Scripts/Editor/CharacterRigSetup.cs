using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Humanoid avatars of the characters that become enemies (automation "chars", Tools > Characters > Set Up Rigs).
/// The characters are the SFW and NSFW variants exported by the Blender conversion: every
/// D:\Gmod\unarchive-models\&lt;model&gt;\export\&lt;qc&gt;\&lt;qc&gt;_&lt;outfit&gt;_(n)sfw[_skinNN].fbx, kept in the project as
/// Assets/Art/Characters/Gmod/&lt;model&gt;/&lt;qc&gt;/ (local: the folder is git-ignored). They are still being worked on: a variant
/// (and its textures) is copied in again whenever the source is newer. Without the source folder the project's copies are used.
/// Each variant's ValveBiped bones are mapped onto Unity's human bones, and the avatar's reference pose is a T-pose (arms
/// raised to the horizontal, legs straight down, whatever the rest pose is): clips from any humanoid source then retarget onto
/// it the same way. The mesh is not changed. A variant already set up (same rig version, nothing copied) is left alone.
/// Check renders of each model's first variant: Logs/shots/chars_&lt;qc&gt;_pose.png (rest pose | avatar T-pose) and _human.png
/// (muscles at zero | a test pose).
/// </summary>
public static class CharacterRigSetup
{
    public const string SourceRoot = @"D:\Gmod\unarchive-models";
    /// <summary>Stamped into a set-up model's import settings (userData): a newer version sets every model up again.</summary>
    const string RigVersion = "rig-v2";
    /// <summary>The character the clip speeds are measured on and the animation sheets are drawn with (any if it is missing).</summary>
    const string ReferenceName = "kroxigorpm_jeans_sfw";

    /// <summary>A character variant: its source FBX, its copy in the project, the model (top folder) and qc (avatar group) it
    /// belongs to, the source QC with its $jigglebone settings, and whether it is an NSFW variant.</summary>
    public class Character { public string name, model, group, source, asset, qc; public bool nsfw; }

    static readonly Regex VariantName = new Regex(@"_(n?sfw)(_|$)", RegexOptions.IgnoreCase);
    static readonly Regex LodName = new Regex(@"_LOD\d+$", RegexOptions.IgnoreCase);

    /// <summary>Whether a file name (without extension) is an SFW / NSFW variant (not a LOD mesh, not an export of something else).</summary>
    public static bool IsVariant(string stem) { return VariantName.IsMatch(stem) && !LodName.IsMatch(stem); }

    /// <summary>Every variant, from the source folder and the project (sorted: model, qc, SFW first, name).</summary>
    public static List<Character> Characters
    {
        get
        {
            var byAsset = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(SourceRoot))
                foreach (var modelDir in Directory.GetDirectories(SourceRoot))
                {
                    string export = Path.Combine(modelDir, "export");
                    if (!Directory.Exists(export)) continue;
                    foreach (var qcDir in Directory.GetDirectories(export))
                        foreach (var f in Directory.GetFiles(qcDir, "*.fbx"))
                        {
                            string stem = Path.GetFileNameWithoutExtension(f);
                            if (!IsVariant(stem)) continue;
                            string asset = GmodModelImport.Root + "/" + Path.GetFileName(modelDir) + "/" + Path.GetFileName(qcDir) + "/" + Path.GetFileName(f);
                            byAsset[asset] = Make(asset, f);
                        }
                }
            if (Directory.Exists(GmodModelImport.Root))
                foreach (var f in Directory.GetFiles(GmodModelImport.Root, "*.fbx", SearchOption.AllDirectories))
                {
                    string asset = f.Replace('\\', '/');
                    var parts = asset.Substring(GmodModelImport.Root.Length + 1).Split('/');
                    if (parts.Length != 3 || !IsVariant(Path.GetFileNameWithoutExtension(asset)) || byAsset.ContainsKey(asset)) continue;
                    byAsset[asset] = Make(asset, null);
                }
            return byAsset.Values.OrderBy(c => c.model, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.group, StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(c => c.nsfw).ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    static Character Make(string asset, string source)
    {
        var parts = asset.Substring(GmodModelImport.Root.Length + 1).Split('/');
        string stem = Path.GetFileNameWithoutExtension(asset);
        return new Character
        {
            name = stem, model = parts[0], group = parts[1], source = source, asset = asset, qc = FindQc(parts[0], parts[1]),
            nsfw = VariantName.Match(stem).Groups[1].Value.Equals("nsfw", StringComparison.OrdinalIgnoreCase),
        };
    }

    static readonly Dictionary<string, string> qcCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The decompiled QC of a qc group: under the model's own folder first (decompile*), then anywhere in the source.</summary>
    static string FindQc(string model, string group)
    {
        string key = model + "/" + group, hit;
        if (qcCache.TryGetValue(key, out hit)) return hit;
        hit = null;
        if (Directory.Exists(SourceRoot))
        {
            string own = Path.Combine(SourceRoot, model);
            var found = (Directory.Exists(own) ? Directory.GetFiles(own, group + ".qc", SearchOption.AllDirectories) : new string[0])
                        .Concat(Directory.GetFiles(SourceRoot, group + ".qc", SearchOption.AllDirectories)).ToList();
            hit = found.OrderBy(p => p.Contains(Path.DirectorySeparatorChar + "decompile" + Path.DirectorySeparatorChar) ? 0 : 1).FirstOrDefault();
        }
        qcCache[key] = hit;
        return hit;
    }

    /// <summary>The character clips are measured on and sheets drawn with.</summary>
    public static Character Reference
    {
        get
        {
            var all = Characters;
            return all.FirstOrDefault(c => c.name.Equals(ReferenceName, StringComparison.OrdinalIgnoreCase) && AvatarOf(c) != null)
                   ?? all.FirstOrDefault(c => !c.nsfw && AvatarOf(c) != null) ?? all.FirstOrDefault();
        }
    }

    const string VB = "ValveBiped.Bip01_";

    /// <summary>Unity human bone → ValveBiped bone (the spine chain is picked separately: see SpineChain).</summary>
    static readonly string[,] Map =
    {
        { "Hips", "Pelvis" }, { "Neck", "Neck1" }, { "Head", "Head1" },
        { "LeftShoulder", "L_Clavicle" }, { "LeftUpperArm", "L_UpperArm" }, { "LeftLowerArm", "L_Forearm" }, { "LeftHand", "L_Hand" },
        { "RightShoulder", "R_Clavicle" }, { "RightUpperArm", "R_UpperArm" }, { "RightLowerArm", "R_Forearm" }, { "RightHand", "R_Hand" },
        { "LeftUpperLeg", "L_Thigh" }, { "LeftLowerLeg", "L_Calf" }, { "LeftFoot", "L_Foot" }, { "LeftToes", "L_Toe0" },
        { "RightUpperLeg", "R_Thigh" }, { "RightLowerLeg", "R_Calf" }, { "RightFoot", "R_Foot" }, { "RightToes", "R_Toe0" },
        { "Left Thumb Proximal", "L_Finger0" }, { "Left Thumb Intermediate", "L_Finger01" }, { "Left Thumb Distal", "L_Finger02" },
        { "Left Index Proximal", "L_Finger1" }, { "Left Index Intermediate", "L_Finger11" }, { "Left Index Distal", "L_Finger12" },
        { "Left Middle Proximal", "L_Finger2" }, { "Left Middle Intermediate", "L_Finger21" }, { "Left Middle Distal", "L_Finger22" },
        { "Left Ring Proximal", "L_Finger3" }, { "Left Ring Intermediate", "L_Finger31" }, { "Left Ring Distal", "L_Finger32" },
        { "Left Little Proximal", "L_Finger4" }, { "Left Little Intermediate", "L_Finger41" }, { "Left Little Distal", "L_Finger42" },
        { "Right Thumb Proximal", "R_Finger0" }, { "Right Thumb Intermediate", "R_Finger01" }, { "Right Thumb Distal", "R_Finger02" },
        { "Right Index Proximal", "R_Finger1" }, { "Right Index Intermediate", "R_Finger11" }, { "Right Index Distal", "R_Finger12" },
        { "Right Middle Proximal", "R_Finger2" }, { "Right Middle Intermediate", "R_Finger21" }, { "Right Middle Distal", "R_Finger22" },
        { "Right Ring Proximal", "R_Finger3" }, { "Right Ring Intermediate", "R_Finger31" }, { "Right Ring Distal", "R_Finger32" },
        { "Right Little Proximal", "R_Finger4" }, { "Right Little Intermediate", "R_Finger41" }, { "Right Little Distal", "R_Finger42" },
    };

    [MenuItem("Tools/Characters/Set Up Rigs")]
    static void Menu() { SetUpAll(); }

    /// <summary>"chars": copies newer sources in, sets up every variant that needs it (one batch of reimports), checks them all.</summary>
    public static void SetUpAll()
    {
        var chars = Characters;
        if (chars.Count == 0) { EditorAutomation.Log("CHARS_FAILED: no SFW / NSFW variants in " + SourceRoot + " or " + GmodModelImport.Root); return; }
        var copied = Sync(chars);

        // the human descriptions first (each from its model as it is now), then all the reimports together
        var todo = new List<KeyValuePair<Character, HumanDescription>>();
        var notes = new Dictionary<Character, string>();
        int failed = 0;
        foreach (var c in chars)
        {
            var mi = AssetImporter.GetAtPath(c.asset) as ModelImporter;
            if (mi == null) { EditorAutomation.Log("  FAIL " + c.name + ": not imported (" + c.asset + ")"); failed++; continue; }
            var avatar = AvatarOf(c);
            bool current = mi.userData == RigVersion && mi.animationType == ModelImporterAnimationType.Human && avatar != null && avatar.isValid && avatar.isHuman;
            if (current && !copied.Contains(c.asset)) continue;
            try
            {
                string note; var hd = Describe(c, out note);
                notes[c] = note + (copied.Contains(c.asset) ? " (copied in from the source)" : "");
                todo.Add(new KeyValuePair<Character, HumanDescription>(c, hd));
            }
            catch (Exception e) { EditorAutomation.Log("  FAIL " + c.name + ": " + e.Message); failed++; }
        }
        if (todo.Count > 0)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in todo)
                {
                    var mi = (ModelImporter)AssetImporter.GetAtPath(kv.Key.asset);
                    mi.animationType = ModelImporterAnimationType.Human;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    mi.humanDescription = kv.Value;
                    mi.optimizeGameObjects = false;
                    mi.importAnimation = false;
                    mi.userData = RigVersion;
                    mi.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }

        int ok = 0;
        var rendered = new HashSet<string>();
        foreach (var c in chars)
        {
            var avatar = AvatarOf(c);
            bool good = avatar != null && avatar.isValid && avatar.isHuman;
            if (good) ok++;
            string note;
            if (notes.TryGetValue(c, out note) || !good)
                EditorAutomation.Log("  " + (good ? "ok   " : "FAIL ") + c.name + ": " + (note ?? "") + ", avatar " + (avatar == null ? "MISSING" : (avatar.isValid ? "valid" : "INVALID") + (avatar.isHuman ? ", human" : ", NOT human")));
            if (good && notes.ContainsKey(c) && rendered.Add(c.group)) { RenderPose(c); RenderHumanPoses(c, avatar, "chars_" + c.group.ToLowerInvariant()); }
        }
        EditorAutomation.Log("  " + chars.Count + " variants of " + chars.Select(c => c.group).Distinct().Count() + " models (" + chars.Count(c => c.nsfw) + " NSFW), "
                             + todo.Count + " set up now, " + copied.Count + " files copied in");
        EditorAutomation.Log(ok == chars.Count ? "CHARS_OK (" + ok + ")" : "CHARS_FAILED: " + (chars.Count - ok) + " of " + chars.Count);
    }

    /// <summary>The character's avatar, once set up.</summary>
    public static Avatar AvatarOf(Character c) { return AssetDatabase.LoadAllAssetsAtPath(c.asset).OfType<Avatar>().FirstOrDefault(); }

    /// <summary>Copies newer source variants and textures (the qc folder's textures/) into the project, in one import. The
    /// copies keep the source's time, so "newer" stays a plain comparison. Returns the project paths of the variants copied.</summary>
    static HashSet<string> Sync(List<Character> chars)
    {
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<KeyValuePair<string, string>>();
        foreach (var c in chars)
            if (c.source != null && File.Exists(c.source)) files.Add(new KeyValuePair<string, string>(c.source, c.asset));
        foreach (var g in chars.Where(c => c.source != null).GroupBy(c => c.model + "/" + c.group))
        {
            var c = g.First();
            string tex = Path.Combine(Path.GetDirectoryName(c.source), "textures");
            if (!Directory.Exists(tex)) continue;
            foreach (var f in Directory.GetFiles(tex, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".tga" && ext != ".jpg" && ext != ".jpeg") continue;
                files.Add(new KeyValuePair<string, string>(f, Path.GetDirectoryName(c.asset).Replace('\\', '/') + "/textures/" + f.Substring(tex.Length + 1).Replace('\\', '/')));
            }
        }
        var newer = files.Where(kv => !File.Exists(kv.Value) || File.GetLastWriteTimeUtc(kv.Key) > File.GetLastWriteTimeUtc(kv.Value)).ToList();
        if (newer.Count == 0) return copied;
        foreach (var kv in newer)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(kv.Value));
            File.Copy(kv.Key, kv.Value, true);
            File.SetLastWriteTimeUtc(kv.Value, File.GetLastWriteTimeUtc(kv.Key));
            if (kv.Value.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) copied.Add(kv.Value);
        }
        EditorAutomation.Log("  copied in from " + SourceRoot + ": " + copied.Count + " variants, " + (newer.Count - copied.Count) + " textures");
        AssetDatabase.Refresh();
        return copied;
    }

    static void Aim(Transform t, Transform child, Vector3 dir)
    {
        if (t == null || child == null) return;
        Vector3 cur = child.position - t.position;
        if (cur.sqrMagnitude < 1e-10f) return;
        t.rotation = Quaternion.FromToRotation(cur.normalized, dir) * t.rotation;
    }

    /// <summary>The bones of the hierarchy put into a T-pose: the arms out along the shoulder line, horizontal; the legs down.</summary>
    static float TPose(Func<string, Transform> bone)
    {
        Transform lu = bone("L_UpperArm"), ru = bone("R_UpperArm");
        Vector3 left = lu.position - ru.position; left.y = 0f; left.Normalize();
        float drop = Vector3.Angle(bone("L_Forearm").position - lu.position, left);
        foreach (var side in new[] { "L", "R" })
        {
            Vector3 dir = side == "L" ? left : -left;
            Aim(bone(side + "_UpperArm"), bone(side + "_Forearm"), dir);
            Aim(bone(side + "_Forearm"), bone(side + "_Hand"), dir);
            Aim(bone(side + "_Hand"), bone(side + "_Finger2") ?? bone(side + "_Finger1"), dir);
            Aim(bone(side + "_Thigh"), bone(side + "_Calf"), Vector3.down);
            Aim(bone(side + "_Calf"), bone(side + "_Foot"), Vector3.down);
        }
        return drop;
    }

    /// <summary>Spine, Chest and UpperChest from Spine, Spine1, Spine2, Spine4: the documented pick (Spine, Spine2, Spine4), dropping
    /// a bone that sits where the previous one does (a zero-length human bone breaks the avatar) and taking Spine1 in its place.</summary>
    static List<Transform> SpineChain(Func<string, Transform> bone)
    {
        var chain = new List<Transform>(); Transform prev = bone("Pelvis");
        foreach (var n in new[] { "Spine", "Spine1", "Spine2", "Spine4" })
        {
            var t = bone(n);
            if (t != null && Vector3.Distance(t.position, prev.position) > 0.02f) { chain.Add(t); prev = t; }
        }
        if (chain.Count == 4) chain.RemoveAt(1);                      // all four apart: Spine, Spine2, Spine4 as documented
        if (chain.Count > 3) chain.RemoveRange(3, chain.Count - 3);
        return chain;
    }

    /// <summary>The variant's human description: its bones mapped, its skeleton as it stands in the T-pose.</summary>
    static HumanDescription Describe(Character c, out string note)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(c.asset);
        if (model == null) throw new Exception("not imported: " + c.asset);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
            var all = inst.GetComponentsInChildren<Transform>(true);
            Func<string, Transform> bone = n => all.FirstOrDefault(t => t.name == VB + n);
            if (bone("Pelvis") == null || bone("L_UpperArm") == null || bone("R_UpperArm") == null) throw new Exception("no ValveBiped skeleton in " + c.asset);

            float drop = TPose(bone);
            var human = new List<HumanBone>();
            Action<string, Transform> add = (h, t) => { if (t != null) human.Add(new HumanBone { humanName = h, boneName = t.name, limit = new HumanLimit { useDefaultValues = true } }); };
            for (int i = 0; i < Map.GetLength(0); i++) add(Map[i, 0], bone(Map[i, 1]));
            var chain = SpineChain(bone);
            string[] slots = { "Spine", "Chest", "UpperChest" };
            for (int i = 0; i < chain.Count; i++) add(slots[i], chain[i]);
            note = human.Count + " human bones, spine " + string.Join(" / ", chain.Select(t => t.name.Replace(VB, "")).ToArray()) + ", arms raised " + drop.ToString("0") + " deg to the T-pose";
            var skeleton = all.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            return new HumanDescription
            {
                human = human.ToArray(), skeleton = skeleton,
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>The rest pose next to the avatar's T-pose.</summary>
    static void RenderPose(Character c)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(c.asset);
        var scene = PreviewShots.Begin();
        try
        {
            var rest = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            inst.transform.position = new Vector3(4f, 0f, 0f);
            var all = inst.GetComponentsInChildren<Transform>(true);
            TPose(n => all.FirstOrDefault(t => t.name == VB + n));
            Bounds b0 = PreviewShots.BoundsOf(rest), b1 = PreviewShots.BoundsOf(inst);
            float h = Mathf.Max(b0.size.y, b1.size.y) * 1.05f;
            PreviewShots.Render(scene, "chars_" + c.group.ToLowerInvariant() + "_pose", 700, 800,
                new PreviewShots.Frame(new Vector3(b0.center.x, h * 0.5f, b0.center.z), h, Vector3.forward),
                new PreviewShots.Frame(new Vector3(b1.center.x, h * 0.5f, b1.center.z), h, Vector3.forward));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>The avatar driving the bones: all muscles at zero (Unity's neutral pose), and a test pose (left arm up, right arm
    /// forward, left knee up, head turned left) from the front and the side. Shows the mapping and the sides are right.</summary>
    static void RenderHumanPoses(Character c, Avatar avatar, string tag)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(c.asset);
        var scene = PreviewShots.Begin();
        try
        {
            var a = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            var b = (GameObject)PrefabUtility.InstantiatePrefab(model, scene); b.transform.position = new Vector3(4f, 0f, 0f);
            Action<GameObject, Dictionary<string, float>> pose = (go, muscles) =>
            {
                var handler = new HumanPoseHandler(avatar, go.transform);
                var p = new HumanPose(); handler.GetHumanPose(ref p);
                for (int i = 0; i < p.muscles.Length; i++) p.muscles[i] = 0f;
                foreach (var kv in muscles)
                {
                    int i = Array.IndexOf(HumanTrait.MuscleName, kv.Key);
                    if (i >= 0) p.muscles[i] = kv.Value; else EditorAutomation.Log("  (no muscle " + kv.Key + ")");
                }
                handler.SetHumanPose(ref p);
                handler.Dispose();
            };
            pose(a, new Dictionary<string, float>());
            pose(b, new Dictionary<string, float>
            {
                { "Left Arm Down-Up", 1f }, { "Right Arm Front-Back", 1f }, { "Right Arm Down-Up", 0.4f },
                { "Left Upper Leg Front-Back", 0.8f }, { "Left Lower Leg Stretch", -0.6f }, { "Head Turn Left-Right", 0.8f },
            });
            Bounds ba = PreviewShots.BoundsOf(a), bb = PreviewShots.BoundsOf(b);
            float h = Mathf.Max(ba.size.y, bb.size.y) * 1.1f;
            PreviewShots.Render(scene, tag + "_human", 600, 800,
                new PreviewShots.Frame(new Vector3(ba.center.x, ba.center.y, ba.center.z), h, Vector3.forward),
                new PreviewShots.Frame(new Vector3(bb.center.x, bb.center.y, bb.center.z), h, Vector3.forward),
                new PreviewShots.Frame(new Vector3(bb.center.x, bb.center.y, bb.center.z), h, Vector3.right));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}

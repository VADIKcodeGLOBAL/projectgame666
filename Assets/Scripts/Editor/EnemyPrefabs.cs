using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Character enemies (automation "enemies"): a prefab per variant of CharacterRigSetup.Characters (SFW and NSFW), in
/// Assets/Art/Characters/Gmod/_Enemies (local, like the models): the box bot's EnemyBot stats and a CharacterController fitted to
/// the body, the model BodyScale times its natural size with its humanoid avatar, the enemy controller and a CharacterAnimator
/// drawing on the AnimationLibrary, lighter LOD meshes, jiggle bones from the QC, a hit box riding on the hips, the model's
/// renderers for the hit flash. Then the EnemyRoster: each model a tier by its natural height (the smallest come in the first
/// wave, the biggest in the last), hand edits of tier / weight / on-off kept. "enemies" also puts the roster into the open
/// level's wave game.
/// </summary>
public static class EnemyPrefabs
{
    public const string Dir = GmodModelImport.Root + "/_Enemies";
    public const string RosterPath = Dir + "/EnemyRoster.asset";
    const string BoxPrefab = "Assets/Prefabs/Enemies/EnemyBot_Box.prefab";
    const string LodMaterial = Dir + "/M_Character_LOD.mat";
    /// <summary>The characters are this many times their natural size (Gmod playermodels are 1.6-2.4 m: small next to the hill).</summary>
    public const float BodyScale = 1.4f;
    /// <summary>Size tiers: one per wave of the default game.</summary>
    const int Tiers = 5;

    class Built { public CharacterRigSetup.Character c; public EnemyBot prefab; public float height; }

    /// <summary>The prefabs of every variant that has a humanoid avatar (rebuilt) and the roster over them; null when there is no
    /// animation library or controller yet, or no character.</summary>
    public static EnemyRoster BuildAll()
    {
        var lib = AssetDatabase.LoadAssetAtPath<AnimationLibrary>(AnimationLibraryBuilder.LibraryPath);
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimationLibraryBuilder.ControllerPath);
        if (lib == null || ctrl == null) { EditorAutomation.Log("  enemies: no animation library or controller yet (run \"anims\")"); return null; }
        var built = new List<Built>();
        foreach (var c in CharacterRigSetup.Characters)
        {
            var avatar = CharacterRigSetup.AvatarOf(c);
            if (avatar == null || !avatar.isHuman) { EditorAutomation.Log("  enemies: " + c.name + " has no humanoid avatar (run \"chars\")"); continue; }
            try { built.Add(Build(c, avatar, lib, ctrl)); }
            catch (System.Exception e) { EditorAutomation.Log("  enemies: " + c.name + " FAILED: " + e.Message); }
        }
        // prefabs and LOD materials of characters that are no longer there (renamed, removed)
        var keep = new HashSet<string>(built.Select(b => AssetDatabase.GetAssetPath(b.prefab)));
        keep.Add(LodMaterial); keep.Add(RosterPath);
        if (Directory.Exists(Dir))
            foreach (var f in Directory.GetFiles(Dir).Where(f => f.EndsWith(".prefab") || f.EndsWith(".mat")).Select(f => f.Replace('\\', '/')))
                if (!keep.Contains(f)) { AssetDatabase.DeleteAsset(f); EditorAutomation.Log("  removed (no such character now): " + Path.GetFileName(f)); }
        if (built.Count == 0) return null;
        return UpdateRoster(built);
    }

    static EnemyRoster UpdateRoster(List<Built> built)
    {
        var roster = AssetDatabase.LoadAssetAtPath<EnemyRoster>(RosterPath);
        if (roster == null) { roster = ScriptableObject.CreateInstance<EnemyRoster>(); AssetDatabase.CreateAsset(roster, RosterPath); }
        // tiers by the models' natural height (the tallest variant of each), smallest first
        var models = built.GroupBy(b => b.c.group).Select(g => new { model = g.Key, height = g.Max(b => b.height) }).OrderBy(m => m.height).ToList();
        var tierOf = new Dictionary<string, int>();
        for (int i = 0; i < models.Count; i++) tierOf[models[i].model] = Mathf.Min(Tiers - 1, i * Tiers / Mathf.Max(1, models.Count));
        var old = roster.enemies.Where(e => e != null && !string.IsNullOrEmpty(e.variant)).GroupBy(e => e.variant).ToDictionary(g => g.Key, g => g.First());
        roster.enemies.Clear();
        foreach (var b in built.OrderBy(b => tierOf[b.c.group]).ThenBy(b => b.c.group).ThenBy(b => b.c.nsfw).ThenBy(b => b.c.name))
        {
            int auto = tierOf[b.c.group];
            EnemyRoster.Entry was;
            old.TryGetValue(b.c.name, out was);
            bool edited = was != null && was.autoTier >= 0 && was.tier != was.autoTier;
            roster.enemies.Add(new EnemyRoster.Entry
            {
                prefab = b.prefab, model = b.c.group, variant = b.c.name, height = b.height, nsfw = b.c.nsfw,
                tier = edited ? was.tier : auto, autoTier = auto,
                weight = was != null ? was.weight : 1f, enabled = was == null || was.enabled,
            });
        }
        EditorUtility.SetDirty(roster); AssetDatabase.SaveAssets();
        foreach (var m in models)
        {
            var es = roster.enemies.Where(e => e.model == m.model).ToList();
            EditorAutomation.Log("  tier " + es[0].tier + (es.Any(e => e.tier != e.autoTier) ? " (edited)" : "") + "  " + m.model + " " + m.height.ToString("0.00") + " m -> "
                                 + (m.height * BodyScale).ToString("0.00") + " m: " + es.Count(e => !e.nsfw) + " SFW + " + es.Count(e => e.nsfw) + " NSFW variants");
        }
        return roster;
    }

    static Built Build(CharacterRigSetup.Character c, Avatar avatar, AnimationLibrary lib, RuntimeAnimatorController ctrl)
    {
        KothMapGenerator.EnsureFolder(Dir);
        var box = AssetDatabase.LoadAssetAtPath<EnemyBot>(BoxPrefab);
        if (box == null) box = KothMapGenerator.MakeBotPrefab();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Enemy_" + c.name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var bot = root.AddComponent<EnemyBot>();
            bot.maxHealth = box.maxHealth; bot.speed = box.speed; bot.damage = box.damage; bot.attackInterval = box.attackInterval;
            bot.model = c.group; bot.nsfw = c.nsfw;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(c.asset), scene);
            model.transform.SetParent(root.transform, false); model.name = "Model";
            float natural = PreviewShots.BoundsOf(model).size.y;
            model.transform.localScale = Vector3.one * BodyScale;
            var rest = PreviewShots.BoundsOf(model);                   // the body as drawn, before the LOD meshes (their bounds are looser)
            float h = natural * BodyScale;
            var anim = model.GetComponent<Animator>(); if (anim == null) anim = model.AddComponent<Animator>();
            anim.avatar = avatar; anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.updateMode = AnimatorUpdateMode.Normal;
            var ca = model.AddComponent<CharacterAnimator>(); ca.library = lib; ca.slots = AnimationLibraryBuilder.Slots();
            bot.character = ca;
            string lod = AddLods(c, model, natural);
            bot.flashRenderers = model.GetComponentsInChildren<Renderer>(true);
            int springs = AddJiggle(c, model, anim);

            // the body: the box bot's capsule, as tall as the character and with its longer stride up steps, as wide as the box (the
            // torso of a BodyScale character is about the box's width: a wider capsule jams the crowd at the player: 0.63 m made the
            // CharacterController cost in a crowd fight 1.4x), and a longer reach for the bigger ones
            var boxCc = box.GetComponent<CharacterController>();
            var cc = root.GetComponent<CharacterController>();
            cc.height = Mathf.Max(boxCc.height, h * 0.9f); cc.radius = boxCc.radius; cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            cc.slopeLimit = boxCc.slopeLimit; cc.stepOffset = boxCc.stepOffset * BodyScale; cc.skinWidth = boxCc.skinWidth;
            bot.attackRange = box.attackRange * Mathf.Clamp(h / 2f, 1f, 1.6f);

            // the hit box: body-sized, standing on the ground at rest, then carried by the hips (it leans and bobs with them)
            var hips = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ValveBiped.Bip01_Pelvis");
            var hb = new GameObject("HitBox").transform;
            hb.SetParent(root.transform, false);
            hb.localPosition = new Vector3(0f, h * 0.5f, 0f);
            hb.localScale = new Vector3(Mathf.Min(rest.size.x, 0.66f * BodyScale), h * 0.97f, Mathf.Min(rest.size.z, 0.48f * BodyScale));
            if (hips != null) hb.SetParent(hips, true);
            bot.hitBox = hb;

            string path = Dir + "/Enemy_" + c.name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            EditorAutomation.Log("  enemy " + c.name + ": " + natural.ToString("0.00") + " -> " + h.ToString("0.00") + " m, reach " + bot.attackRange.ToString("0.0") + " m, "
                                 + bot.flashRenderers.Length + " renderers, " + bot.flashRenderers.Sum(r => r.sharedMaterials.Length) + " materials, " + springs + " jiggle bones, " + lod);
            return new Built { c = c, prefab = prefab.GetComponent<EnemyBot>(), height = natural };
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>Screen heights (of the LOD group's size: the model's height and a bit, before the quality LOD bias) where each
    /// next level takes over: the full model up close, the mid mesh to a few tens of metres, the far mesh beyond.</summary>
    static readonly float[] LodScreenHeights = { 0.2f, 0.045f };

    /// <summary>
    /// The lighter meshes (&lt;variant&gt;_LOD1.fbx, _LOD2.fbx... next to the model, made by Source/Blender/Scripts/character_lod.py:
    /// one mesh, one vertex-coloured material) bound to the model's own bones by name — the same animator moves them — and a
    /// LODGroup: the full model near, then each lighter mesh; the last is never culled (a far enemy stays visible) and casts no
    /// shadow. Returns a note for the log.
    /// </summary>
    static string AddLods(CharacterRigSetup.Character c, GameObject model, float natural)
    {
        string stem = c.asset.Substring(0, c.asset.Length - 4);
        var near = model.GetComponentsInChildren<Renderer>(true);
        var byName = new Dictionary<string, Transform>();
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
        var sh = Shader.Find("ProjectGame/VertexColorLit");
        var mat = KothMapGenerator.GetAsset(LodMaterial, () => new Material(sh));
        mat.shader = sh; mat.color = Color.white; EditorUtility.SetDirty(mat);
        var levels = new List<SkinnedMeshRenderer>(); var notes = new List<string>();
        for (int k = 1; ; k++)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stem + "_LOD" + k + ".fbx");
            var src = asset != null ? asset.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (src == null) break;
            var go = new GameObject("LOD" + k); go.transform.SetParent(model.transform, false);
            go.transform.localPosition = src.transform.localPosition; go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = src.sharedMesh; smr.sharedMaterial = mat;
            int missing = 0;
            smr.bones = src.bones.Select(b => { Transform t; if (b != null && byName.TryGetValue(b.name, out t)) return t; missing++; return null; }).ToArray();
            Transform root; smr.rootBone = src.rootBone != null && byName.TryGetValue(src.rootBone.name, out root) ? root : null;
            smr.localBounds = src.localBounds; smr.updateWhenOffscreen = false;
            levels.Add(smr);
            notes.Add("LOD" + k + " " + smr.sharedMesh.triangles.Length / 3 + " tris" + (missing > 0 ? " (" + missing + " bones NOT FOUND)" : ""));
        }
        if (levels.Count == 0) return "no lighter meshes (" + Path.GetFileName(stem) + "_LOD1.fbx missing: run character_lod.py)";
        levels[levels.Count - 1].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // up close the full model casts no shadow of its own: an invisible copy of the next mesh does (the shadow looks the same,
        // the shadow passes draw ~10 000 triangles instead of ~70 000 and one material instead of twenty)
        foreach (var r in near) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var shadow = Object.Instantiate(levels[0].gameObject, levels[0].transform.parent); shadow.name = "LOD0_Shadow";
        var shadowR = shadow.GetComponent<SkinnedMeshRenderer>();
        shadowR.bones = levels[0].bones; shadowR.rootBone = levels[0].rootBone;
        shadowR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        near = near.Concat(new Renderer[] { shadowR }).ToArray();
        var lods = new List<LOD> { new LOD(LodScreenHeights[0], near) };
        for (int i = 0; i < levels.Count; i++)
            lods.Add(new LOD(i == levels.Count - 1 ? 0f : LodScreenHeights[Mathf.Min(i + 1, LodScreenHeights.Length - 1)], new Renderer[] { levels[i] }));
        var lg = model.AddComponent<LODGroup>();
        lg.SetLODs(lods.ToArray()); lg.size = natural * 1.14f; lg.fadeMode = LODFadeMode.None;   // in the model's space: scaled with it
        return string.Join(", ", notes.ToArray());
    }

    /// <summary>
    /// The character's $jigglebone bones (from its QC: the source, or the copy kept next to the model) as JiggleBones springs.
    /// Source units to metres (inches, in/s²) at the model's natural size (JiggleBones scales them with the body); the bone's
    /// axis is towards its child; bones the humanoid avatar drives are left to the animation. Parents come first. Returns how many.
    /// </summary>
    static int AddJiggle(CharacterRigSetup.Character c, GameObject model, Animator anim)
    {
        string copy = Path.GetDirectoryName(c.asset).Replace('\\', '/') + "/" + c.group + ".qc.txt";
        if (!string.IsNullOrEmpty(c.qc) && File.Exists(c.qc) && (!File.Exists(copy) || File.GetLastWriteTimeUtc(c.qc) > File.GetLastWriteTimeUtc(copy)))
        {
            File.Copy(c.qc, copy, true); File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(c.qc));
        }
        if (!File.Exists(copy)) { EditorAutomation.Log("  enemies: no QC for " + c.name + " (no jiggle bones)"); return 0; }
        const float Inch = 0.0254f;
        var human = new HashSet<Transform>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) { var t = anim.GetBoneTransform((HumanBodyBones)i); if (t != null) human.Add(t); }
        var all = model.GetComponentsInChildren<Transform>(true);
        var jb = model.AddComponent<JiggleBones>();
        foreach (var e in QcJiggle.Parse(File.ReadAllText(copy)))
        {
            var t = all.FirstOrDefault(x => x.name == e.bone);
            if (t == null || human.Contains(t)) continue;
            Vector3 toTip = t.childCount > 0 ? t.GetChild(0).position - t.position : (t.parent != null ? t.position - t.parent.position : t.up);
            if (toTip.sqrMagnitude < 1e-10f) toTip = t.up;
            jb.bones.Add(new JiggleBones.Bone
            {
                bone = t, length = e.length * Inch, stiffness = (e.pitchStiffness + e.yawStiffness) * 0.5f, damping = (e.pitchDamping + e.yawDamping) * 0.5f,
                gravity = e.tipMass * Inch, angleLimit = e.angleConstraint, axis = t.InverseTransformDirection(toTip.normalized), restLocal = t.localRotation,
            });
        }
        System.Func<Transform, int> depth = x => { int d = 0; while (x != null) { d++; x = x.parent; } return d; };
        jb.bones.Sort((a, b) => depth(a.bone).CompareTo(depth(b.bone)));
        return jb.bones.Count;
    }

    /// <summary>The first safe variant of every model, smallest tier first, in the idle pose next to a 1.8 m grey capsule (a
    /// person), all panels at the same scale: Logs/shots/enemies_lineup.png.</summary>
    static void RenderLineup(EnemyRoster roster)
    {
        var models = roster.enemies.Where(e => e != null && e.prefab != null).GroupBy(e => e.model)
                               .Select(g => g.OrderBy(e => e.nsfw).First()).OrderBy(e => e.tier).ThenBy(e => e.height).ToList();
        var idle = AnimationLibraryBuilder.Slots()[(int)AnimTag.Idle];
        var scene = PreviewShots.Begin();
        try
        {
            var frames = new List<PreviewShots.Frame>();
            for (int i = 0; i < models.Count; i++)
            {
                var at = new Vector3(i * 12f, 0f, 0f);
                var bot = (GameObject)PrefabUtility.InstantiatePrefab(models[i].prefab.gameObject, scene);
                bot.transform.position = at; bot.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
                var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(person, scene);
                Object.DestroyImmediate(person.GetComponent<Collider>());
                person.transform.position = at + new Vector3(-1.5f, 0.9f, 0f); person.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
                var model = bot.GetComponent<EnemyBot>().character.gameObject;
                frames.Add(new PreviewShots.Frame(at + new Vector3(-0.45f, 2f, 0f), 4f, Vector3.forward, () =>
                {
                    if (idle != null) idle.SampleAnimation(model, 0.3f);
                    PreviewShots.Freeze(bot);
                }));
            }
            PreviewShots.RenderGrid(scene, "enemies_lineup", 7, 330, 440, frames.ToArray());
            EditorAutomation.Log("  lineup (Logs/shots/enemies_lineup.png, rows of 7 next to a 1.8 m capsule): "
                                 + string.Join(", ", models.Select(e => e.model + " t" + e.tier).ToArray()));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>"enemies": the character enemies (rebuilt) and their roster into the open level's wave game; the scene is saved.</summary>
    public static void InstallInOpenScene()
    {
        var game = Object.FindFirstObjectByType<WaveSurvivalGame>();
        if (game == null) { EditorAutomation.Log("ENEMIES_FAILED: no wave game in the open scene"); return; }
        if (EditorSceneManager.GetActiveScene().isDirty) { EditorAutomation.Log("ENEMIES_FAILED: the open scene has unsaved changes (save or discard them first)"); return; }
        // skinned crowds: the vertices are skinned on the GPU, batched (on the CPU they filled the worker threads the physics
        // and the rendering wait for)
        if (PlayerSettings.meshDeformation != MeshDeformation.GPUBatched)
        {
            EditorAutomation.Log("  mesh deformation " + PlayerSettings.meshDeformation + " -> GPUBatched");
            PlayerSettings.meshDeformation = MeshDeformation.GPUBatched;
        }
        var roster = BuildAll();
        game.roster = roster;
        EditorUtility.SetDirty(game);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        if (roster != null) RenderLineup(roster);
        EditorAutomation.Log(roster != null ? "ENEMIES_OK (" + roster.enemies.Count + " variants of " + roster.enemies.Select(e => e.model).Distinct().Count() + " models in "
                                              + scene.path + ", saved " + saved + ")" : "ENEMIES_FAILED: no character enemies built");
    }
}

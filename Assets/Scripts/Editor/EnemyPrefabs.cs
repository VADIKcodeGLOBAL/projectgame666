using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Character enemies (automation "enemies"): a prefab per character of CharacterRigSetup.Characters, in
/// Assets/Art/Characters/Gmod/_Enemies (local, like the models): the box bot's CharacterController and EnemyBot (same stats),
/// the model with its humanoid avatar, the enemy controller and a CharacterAnimator drawing on the AnimationLibrary, a hit box
/// riding on the hips, the model's renderers for the hit flash. "enemies" also puts them into the open level's wave game.
/// </summary>
public static class EnemyPrefabs
{
    public const string Dir = GmodModelImport.Root + "/_Enemies";
    const string BoxPrefab = "Assets/Prefabs/Enemies/EnemyBot_Box.prefab";

    /// <summary>The prefabs of every character that has a humanoid avatar (rebuilt); empty when there is no library or controller yet.</summary>
    public static List<EnemyBot> BuildAll()
    {
        var list = new List<EnemyBot>();
        var lib = AssetDatabase.LoadAssetAtPath<AnimationLibrary>(AnimationLibraryBuilder.LibraryPath);
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimationLibraryBuilder.ControllerPath);
        if (lib == null || ctrl == null) { EditorAutomation.Log("  enemies: no animation library or controller yet (run \"anims\")"); return list; }
        foreach (var c in CharacterRigSetup.Characters)
        {
            var avatar = CharacterRigSetup.AvatarOf(c);
            if (avatar == null || !avatar.isHuman) { EditorAutomation.Log("  enemies: " + c.name + " has no humanoid avatar (run \"chars\")"); continue; }
            list.Add(Build(c, avatar, lib, ctrl));
        }
        return list;
    }

    static EnemyBot Build(CharacterRigSetup.Character c, Avatar avatar, AnimationLibrary lib, RuntimeAnimatorController ctrl)
    {
        KothMapGenerator.EnsureFolder(Dir);
        var box = AssetDatabase.LoadAssetAtPath<EnemyBot>(BoxPrefab);
        if (box == null) box = KothMapGenerator.MakeBotPrefab();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Enemy_" + c.name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var boxCc = box.GetComponent<CharacterController>();
            var cc = root.AddComponent<CharacterController>();
            cc.height = boxCc.height; cc.radius = boxCc.radius; cc.center = boxCc.center; cc.slopeLimit = boxCc.slopeLimit; cc.stepOffset = boxCc.stepOffset; cc.skinWidth = boxCc.skinWidth;
            var bot = root.AddComponent<EnemyBot>();
            bot.maxHealth = box.maxHealth; bot.speed = box.speed; bot.damage = box.damage; bot.attackInterval = box.attackInterval; bot.attackRange = box.attackRange;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(c.asset), scene);
            model.transform.SetParent(root.transform, false); model.name = "Model";
            var anim = model.GetComponent<Animator>(); if (anim == null) anim = model.AddComponent<Animator>();
            anim.avatar = avatar; anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.updateMode = AnimatorUpdateMode.Normal;
            var ca = model.AddComponent<CharacterAnimator>(); ca.library = lib; ca.slots = AnimationLibraryBuilder.Slots();
            bot.character = ca;
            string lod = AddLods(c, model);
            bot.flashRenderers = model.GetComponentsInChildren<Renderer>(true);
            int springs = AddJiggle(c, model, anim);

            // the hit box: body-sized, standing on the ground at rest, then carried by the hips (it leans and bobs with them)
            var rest = PreviewShots.BoundsOf(model);
            float h = rest.size.y;
            var hips = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ValveBiped.Bip01_Pelvis");
            var hb = new GameObject("HitBox").transform;
            hb.SetParent(root.transform, false);
            hb.localPosition = new Vector3(0f, h * 0.5f, 0f); hb.localScale = new Vector3(Mathf.Min(rest.size.x, 0.66f), h * 0.97f, Mathf.Min(rest.size.z, 0.48f));
            if (hips != null) hb.SetParent(hips, true);
            bot.hitBox = hb;

            string path = Dir + "/Enemy_" + c.name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            EditorAutomation.Log("  enemy " + c.name + ": " + h.ToString("0.00") + " m tall, hit box " + hb.lossyScale.ToString("F2") + " on the hips, "
                                 + bot.flashRenderers.Length + " renderers, " + bot.flashRenderers.Sum(r => r.sharedMaterials.Length) + " materials, " + springs + " jiggle bones, " + lod + " -> " + path);
            return prefab.GetComponent<EnemyBot>();
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>Screen heights (of the LOD group's 2.2 m, before the quality LOD bias) where each next level takes over: the full
    /// model up close, the mid mesh to a few tens of metres, the far mesh beyond.</summary>
    static readonly float[] LodScreenHeights = { 0.2f, 0.045f };

    /// <summary>
    /// The lighter meshes (&lt;model&gt;_LOD1.fbx, _LOD2.fbx... next to the model, made by Source/Blender/Scripts/character_lod.py:
    /// one mesh, one vertex-coloured material) bound to the model's own bones by name — the same animator moves them — and a
    /// LODGroup: the full model near, then each lighter mesh; the last is never culled (a far enemy stays visible) and casts no
    /// shadow. Returns a note for the log.
    /// </summary>
    static string AddLods(CharacterRigSetup.Character c, GameObject model)
    {
        string stem = c.asset.Substring(0, c.asset.Length - 4);
        var near = model.GetComponentsInChildren<Renderer>(true);
        var byName = new Dictionary<string, Transform>();
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
        var sh = Shader.Find("ProjectGame/VertexColorLit");
        var mat = KothMapGenerator.GetAsset(Dir + "/M_" + c.name + "_LOD.mat", () => new Material(sh));
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
        if (levels.Count == 0) return "no lighter meshes (" + System.IO.Path.GetFileName(stem) + "_LOD1.fbx missing: run character_lod.py)";
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
        lg.SetLODs(lods.ToArray()); lg.size = 2.2f; lg.fadeMode = LODFadeMode.None;
        return string.Join(", ", notes.ToArray());
    }

    /// <summary>
    /// The character's $jigglebone bones (from its QC: the source, or the copy kept next to the model) as JiggleBones springs.
    /// Source units to metres (inches, in/s²); the bone's axis is towards its child; bones the humanoid avatar drives are left
    /// to the animation. Parents come first. Returns how many.
    /// </summary>
    static int AddJiggle(CharacterRigSetup.Character c, GameObject model, Animator anim)
    {
        string copy = System.IO.Path.ChangeExtension(c.asset, ".qc.txt");
        if (!string.IsNullOrEmpty(c.qc) && System.IO.File.Exists(c.qc)) System.IO.File.Copy(c.qc, copy, true);
        if (!System.IO.File.Exists(copy)) { EditorAutomation.Log("  enemies: no QC for " + c.name + " (no jiggle bones)"); return 0; }
        const float Inch = 0.0254f;
        var human = new HashSet<Transform>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) { var t = anim.GetBoneTransform((HumanBodyBones)i); if (t != null) human.Add(t); }
        var all = model.GetComponentsInChildren<Transform>(true);
        var jb = model.AddComponent<JiggleBones>();
        foreach (var e in QcJiggle.Parse(System.IO.File.ReadAllText(copy)))
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

    /// <summary>"enemies": the character enemies (rebuilt) into the open level's wave game; the scene is saved.</summary>
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
        var list = BuildAll();
        game.characterBots = list.ToArray();
        EditorUtility.SetDirty(game);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        EditorAutomation.Log(list.Count > 0 ? "ENEMIES_OK (" + list.Count + " character(s) in " + scene.path + ", saved " + saved + ")" : "ENEMIES_FAILED: no character enemies built");
    }
}

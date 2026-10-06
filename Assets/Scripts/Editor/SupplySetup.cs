using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Field supplies and the menu side of the game:
///  - pickup prefabs in Assets/Prefabs/Pickups: Magazine (ammo), Milk (heals) and EnergyDrink (speed), from the models in
///    Assets/Art/Pickups/Models made by Source/Blender/Scripts/pickup_import.py (originals in Source/Models/Pickups),
///    PBR textures in Assets/Art/Pickups/Textures, each with an upright glow in its own colour;
///  - SettingsMenu and MusicPlayer (with the music library of Assets/Audio/Music) on the Game object.
/// Called by the hill map generator; command "gameplay" applies it to the open level, "supplies-view" renders the pickups.
/// </summary>
public static class SupplySetup
{
    const string PrefabDir = "Assets/Prefabs/Pickups";
    const string ModelDir = "Assets/Art/Pickups/Models";
    const string MatDir = "Assets/Art/Pickups/Materials";
    const string TexDir = "Assets/Art/Pickups/Textures";

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        System.IO.Directory.CreateDirectory(path); AssetDatabase.Refresh();
    }

    /// <summary>
    /// PBR material for a pickup model (ProjectGame/WeaponPBR, separate metallic and roughness maps) from
    /// Textures/&lt;name&gt;_BaseColor, _Normal, _Metallic, _Roughness (any of .jpg / .png), imported at 1024 px: a pickup is small on screen.
    /// </summary>
    static Material PbrMat(string name)
    {
        Func<string, string> find = kind =>
        {
            foreach (var ext in new[] { ".jpg", ".png", ".tga" })
            {
                string p = TexDir + "/" + name + "_" + kind + ext;
                if (AssetImporter.GetAtPath(p) != null) return p;
            }
            return null;
        };
        string path = MatDir + "/M_" + name + ".mat";
        EnsureFolder(MatDir);
        var sh = Shader.Find("ProjectGame/WeaponPBR");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); } else mat.shader = sh;
        mat.SetTexture("_MainTex", WeaponSetup.Tex(find("BaseColor"), false, false, 1024));
        mat.SetTexture("_BumpMap", WeaponSetup.Tex(find("Normal"), true, true, 1024));
        mat.SetTexture("_MetallicMap", WeaponSetup.Tex(find("Metallic"), true, false, 512));
        mat.SetTexture("_RoughnessMap", WeaponSetup.Tex(find("Roughness"), true, false, 512));
        mat.SetColor("_EmissionColor", Color.black);
        if (mat.GetTexture("_MainTex") == null) EditorAutomation.Log("  pickup " + name + ": no base colour texture in " + TexDir);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>The FBX made by Source/Blender/Scripts/pickup_import.py, its one material (named like the model) remapped to the PBR one.</summary>
    static GameObject Model(string name)
    {
        string path = ModelDir + "/" + name + ".fbx";
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) throw new Exception("pickup model not found: " + path + " (run pickup_import.py)");
        mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), PbrMat(name));
        mi.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    /// <summary>Root with the pickup, a spinner holding the model (largest side = size), and two crossed glow quads.</summary>
    static SupplyPickup Prefab(string name, SupplyPickup.Kind kind, Color glow, float size)
    {
        EnsureFolder(PrefabDir);
        var model = Model(name);
        var fx = WeaponSetup.MakeFx();
        var beam = WeaponSetup.FxMat("M_Glow_" + kind, (Texture2D)fx.beam.mainTexture, glow, 0.9f, MatDir);
        var root = new GameObject(name);
        var pick = root.AddComponent<SupplyPickup>(); pick.kind = kind;
        pick.hoverHeight = size * 0.5f + 0.35f;                                       // the bottom of the model floats 35 cm above the grass
        var spin = new GameObject("Spinner").transform; spin.SetParent(root.transform, false);
        Place(spin, model, size);
        foreach (var r in spin.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.lightProbeUsage = LightProbeUsage.BlendProbes; }
        for (int k = 0; k < 2; k++)                                                   // a soft upright glow, seen over the grass
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "Glow" + k;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(root.transform, false);
            q.transform.localPosition = new Vector3(0f, -pick.hoverHeight + 1.3f, 0f); q.transform.localRotation = Quaternion.Euler(0f, k * 90f, 0f);
            q.transform.localScale = new Vector3(size * 0.75f, 2.6f, 1f);
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = beam; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        pick.spinner = spin;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
        return prefab.GetComponent<SupplyPickup>();
    }

    /// <summary>An FBX model under parent, scaled so that its largest side is size and centred on the parent.</summary>
    static void Place(Transform parent, GameObject model, float size)
    {
        var holder = new GameObject("Model").transform; holder.SetParent(parent, false);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model); inst.transform.SetParent(holder, false);
        Func<Bounds> bounds = () => { var rs = inst.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; };
        var b0 = bounds();
        holder.localScale = Vector3.one * (size / Mathf.Max(0.001f, Mathf.Max(b0.size.x, Mathf.Max(b0.size.y, b0.size.z))));
        holder.position -= bounds().center - parent.position;
    }

    // sizes: the largest side, about 1.6x the old pickups (magazine 0.4 m, medkit 0.48 m, syringe 0.42 m)
    public static SupplyPickup Ammo() { return Prefab("Magazine", SupplyPickup.Kind.Ammo, new Color(1f, 0.78f, 0.3f), 0.70f); }
    public static SupplyPickup Medkit() { return Prefab("Milk", SupplyPickup.Kind.Medkit, new Color(0.85f, 0.93f, 1f), 0.75f); }
    public static SupplyPickup Speed() { return Prefab("EnergyDrink", SupplyPickup.Kind.Speed, new Color(0.3f, 1f, 0.8f), 0.70f); }

    /// <summary>The pickups of the previous version (procedural magazine, medkit and syringe models), removed once replaced.</summary>
    static readonly string[] Obsolete = {
        PrefabDir + "/Ammo.prefab", PrefabDir + "/Medkit.prefab", PrefabDir + "/Syringe.prefab",
        ModelDir + "/Medkit.fbx", ModelDir + "/Syringe.fbx",
        MatDir + "/M_AmmoMagazine.mat", MatDir + "/M_AmmoBrass.mat",
        MatDir + "/M_Medkit_Body.mat", MatDir + "/M_Medkit_Cross.mat", MatDir + "/M_Medkit_Handle.mat",
        MatDir + "/M_Syringe_Glass.mat", MatDir + "/M_Syringe_Liquid.mat", MatDir + "/M_Syringe_Plunger.mat", MatDir + "/M_Syringe_Metal.mat" };

    /// <summary>Supplies, settings menu and music on the wave game (shared by the generator and the "gameplay" command).</summary>
    public static void AddToGame(WaveSurvivalGame game)
    {
        game.ammoPickupPrefab = Ammo(); game.medkitPrefab = Medkit(); game.speedPrefab = Speed();
        var music = game.GetComponent<MusicPlayer>();
        if (music == null) music = game.gameObject.AddComponent<MusicPlayer>();         // adds its AudioSource
        music.library = MusicLibraryUpdater.Rebuild();
        var menu = game.GetComponent<SettingsMenu>();
        if (menu == null) menu = game.gameObject.AddComponent<SettingsMenu>();
        menu.music = music;
        EditorUtility.SetDirty(game); EditorUtility.SetDirty(music); EditorUtility.SetDirty(menu);
        int removed = 0;
        foreach (var p in Obsolete) if (AssetImporter.GetAtPath(p) != null && AssetDatabase.DeleteAsset(p)) removed++;
        EditorAutomation.Log("  supplies: magazine, milk, energy drink (old pickup assets removed: " + removed + "); music tracks " + music.TrackCount + "; settings menu on " + game.name);
    }

    /// <summary>"gameplay": supplies, settings and music in the open hill level, scene saved.</summary>
    public static void InstallInOpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != KothMapGenerator.ScenePath) scene = EditorSceneManager.OpenScene(KothMapGenerator.ScenePath, OpenSceneMode.Single);
        var game = Object.FindFirstObjectByType<WaveSurvivalGame>();
        if (game == null) { EditorAutomation.Log("GAMEPLAY_FAILED: no wave game in " + scene.path); return; }
        AddToGame(game);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        EditorAutomation.Log("  scene saved " + saved);
        EditorAutomation.Log("GAMEPLAY_OK");
    }

    /// <summary>"supplies-view": the three pickups side by side, high above the level (Logs/shots/supplies.png).</summary>
    public static void ViewShots()
    {
        var names = new[] { "Magazine", "Milk", "EnergyDrink" };
        var temp = new List<GameObject>();
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            Vector3 c = new Vector3(0f, 620f, 0f);
            for (int i = 0; i < names.Length; i++)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + names[i] + ".prefab");
                if (p == null) continue;
                var g = Object.Instantiate(p, c + Vector3.right * (i - 1) * 0.9f, Quaternion.Euler(0f, 25f, 0f)); g.hideFlags = HideFlags.HideAndDontSave; temp.Add(g);
                foreach (var gl in g.GetComponentsInChildren<Renderer>()) if (gl.name.StartsWith("Glow")) gl.enabled = false;   // the models, not the beams
            }
            // the same camera as for the previous pickups, so the two pictures compare sizes
            EditorAutomation.Shot("supplies", c + new Vector3(0f, 0.35f, -2.6f), c + Vector3.up * 0.2f, 40f);
            EditorAutomation.Shot("supplies_close", c + new Vector3(0f, 0.25f, -1.7f), c + Vector3.up * 0.05f, 40f);
        }
        finally { foreach (var g in temp) Object.DestroyImmediate(g); ShaderUtil.allowAsyncCompilation = async; }
        EditorAutomation.Log("SUPPLIES_VIEW_OK");
    }
}

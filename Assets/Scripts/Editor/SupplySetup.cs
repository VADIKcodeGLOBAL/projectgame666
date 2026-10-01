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
///  - pickup prefabs in Assets/Prefabs/Pickups: Ammo (magazine), Medkit and Syringe (speed), the last two from the models
///    made by Source/Blender/Scripts/supply_models.py, each with an upright glow in its own colour;
///  - SettingsMenu and MusicPlayer (with the music library of Assets/Audio/Music) on the Game object.
/// Called by the hill map generator; command "gameplay" applies it to the open level, "supplies-view" renders the pickups.
/// </summary>
public static class SupplySetup
{
    const string PrefabDir = "Assets/Prefabs/Pickups";
    const string ModelDir = "Assets/Art/Pickups/Models";
    const string MatDir = "Assets/Art/Pickups/Materials";

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        System.IO.Directory.CreateDirectory(path); AssetDatabase.Refresh();
    }

    static Material Fade(string name, Color c, float gloss)
    {
        var m = WeaponSetup.LitMat(name, c, 0f, gloss, Color.black, MatDir);
        m.SetFloat("_Mode", 2f); m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.DisableKeyword("_EMISSION");
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }

    static GameObject Model(string file, Dictionary<string, Material> mats)
    {
        string path = ModelDir + "/" + file;
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) throw new Exception("supply model not found: " + path + " (run supply_models.py)");
        mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        foreach (var kv in mats) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
        mi.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    /// <summary>Root with the pickup, a spinner holding the visual, and two crossed glow quads.</summary>
    static SupplyPickup Prefab(string name, SupplyPickup.Kind kind, Color glow, Action<Transform> visual)
    {
        EnsureFolder(PrefabDir);
        var fx = WeaponSetup.MakeFx();
        var beam = WeaponSetup.FxMat("M_Glow_" + kind, (Texture2D)fx.beam.mainTexture, glow, 0.9f, MatDir);
        var root = new GameObject(name);
        var pick = root.AddComponent<SupplyPickup>(); pick.kind = kind;
        var spin = new GameObject("Spinner").transform; spin.SetParent(root.transform, false);
        visual(spin);
        foreach (var r in spin.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        for (int k = 0; k < 2; k++)                                                   // a soft upright glow, seen over the grass
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "Glow" + k;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(root.transform, false);
            q.transform.localPosition = new Vector3(0f, -pick.hoverHeight + 1.1f, 0f); q.transform.localRotation = Quaternion.Euler(0f, k * 90f, 0f);
            q.transform.localScale = new Vector3(0.35f, 2.2f, 1f);
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

    public static SupplyPickup Ammo()
    {
        var body = WeaponSetup.LitMat("M_AmmoMagazine", new Color(0.09f, 0.09f, 0.10f), 0.6f, 0.45f, new Color(0.10f, 0.06f, 0.01f), MatDir);
        var brass = WeaponSetup.LitMat("M_AmmoBrass", new Color(0.85f, 0.62f, 0.25f), 1f, 0.7f, new Color(0.55f, 0.35f, 0.08f), MatDir);
        return Prefab("Ammo", SupplyPickup.Kind.Ammo, new Color(1f, 0.78f, 0.3f), spin =>
        {
            var mag = new GameObject("Magazine").transform; mag.SetParent(spin, false); mag.localScale = Vector3.one * 1.6f;
            Action<string, Vector3, Vector3, Vector3, Material> part = (n, pos, rot, size, mat) =>
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n;
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(mag, false); g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(rot); g.transform.localScale = size;
                g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            };
            part("Lower", new Vector3(0f, -0.07f, 0.012f), new Vector3(14f, 0f, 0f), new Vector3(0.035f, 0.13f, 0.075f), body);   // a curved rifle magazine
            part("Upper", new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(0.035f, 0.12f, 0.075f), body);
            part("Rounds", new Vector3(0f, 0.118f, 0f), Vector3.zero, new Vector3(0.022f, 0.018f, 0.065f), brass);
        });
    }

    public static SupplyPickup Medkit()
    {
        var model = Model("Medkit.fbx", new Dictionary<string, Material> {
            { "Medkit_Body", WeaponSetup.LitMat("M_Medkit_Body", new Color(0.78f, 0.08f, 0.06f), 0.05f, 0.55f, new Color(0.18f, 0.01f, 0.0f), MatDir) },
            { "Medkit_Cross", WeaponSetup.LitMat("M_Medkit_Cross", new Color(0.95f, 0.95f, 0.95f), 0f, 0.4f, new Color(0.35f, 0.35f, 0.35f), MatDir) },
            { "Medkit_Handle", WeaponSetup.LitMat("M_Medkit_Handle", new Color(0.08f, 0.08f, 0.09f), 0.2f, 0.4f, Color.black, MatDir) } });
        var p = Prefab("Medkit", SupplyPickup.Kind.Medkit, new Color(1f, 0.32f, 0.26f), spin => Place(spin, model, 0.48f));
        return p;
    }

    public static SupplyPickup Speed()
    {
        var model = Model("Syringe.fbx", new Dictionary<string, Material> {
            { "Syringe_Glass", Fade("M_Syringe_Glass", new Color(0.85f, 0.95f, 1f, 0.32f), 0.92f) },
            { "Syringe_Liquid", WeaponSetup.LitMat("M_Syringe_Liquid", new Color(0.1f, 0.9f, 0.65f), 0f, 0.8f, new Color(0.15f, 1.4f, 1.0f), MatDir) },
            { "Syringe_Plunger", WeaponSetup.LitMat("M_Syringe_Plunger", new Color(0.92f, 0.92f, 0.95f), 0f, 0.5f, Color.black, MatDir) },
            { "Syringe_Metal", WeaponSetup.LitMat("M_Syringe_Metal", new Color(0.75f, 0.77f, 0.8f), 1f, 0.8f, Color.black, MatDir) } });
        return Prefab("Syringe", SupplyPickup.Kind.Speed, new Color(0.3f, 1f, 0.8f), spin =>
        {
            var tilt = new GameObject("Tilt").transform; tilt.SetParent(spin, false); tilt.localRotation = Quaternion.Euler(0f, 0f, 35f);
            Place(tilt, model, 0.42f);
        });
    }

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
        if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/AmmoPickup.prefab") != null)
            AssetDatabase.DeleteAsset("Assets/Prefabs/Weapons/AmmoPickup.prefab");                  // replaced by Pickups/Ammo
        EditorAutomation.Log("  supplies: ammo, medkit, syringe; music tracks " + music.TrackCount + "; settings menu on " + game.name);
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
        var names = new[] { "Ammo", "Medkit", "Syringe" };
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
            }
            EditorAutomation.Shot("supplies", c + new Vector3(0f, 0.35f, -2.6f), c + Vector3.up * 0.2f, 40f);
        }
        finally { foreach (var g in temp) Object.DestroyImmediate(g); ShaderUtil.allowAsyncCompilation = async; }
        EditorAutomation.Log("SUPPLIES_VIEW_OK");
    }
}

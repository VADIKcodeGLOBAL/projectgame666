using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The bastard sword in the player's hands: the model made by Source/Blender/Scripts/sword_model.py, steel / guard / leather
/// materials, turned so that the blade runs along +Y and its flat faces +Z (MeleeWeapon poses it that way), a trail at the tip.
/// Called by WeaponSetup.BuildPlayerWeapons (command "weapons", the map generator).
/// </summary>
public static class SwordSetup
{
    const string ModelPath = "Assets/Art/Weapons/Sword/Models/BastardSword.fbx";
    const string MatDir = "Assets/Art/Weapons/Materials";

    static Material Lit(string name, Color c, float metallic, float gloss)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_Color", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Glossiness", gloss);
        EditorUtility.SetDirty(m);
        return m;
    }

    public static MeleeWeapon Build(Transform pivot, WeaponSetup.Fx fx, StringBuilder log)
    {
        var mi = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (mi == null) throw new System.Exception("sword model not found: " + ModelPath + " (run sword_model.py)");
        mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Sword_Blade"), Lit("M_Sword_Blade", new Color(0.80f, 0.81f, 0.84f), 1f, 0.8f));
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Sword_Guard"), Lit("M_Sword_Guard", new Color(0.36f, 0.34f, 0.31f), 0.9f, 0.55f));
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Sword_Grip"), Lit("M_Sword_Grip", new Color(0.24f, 0.14f, 0.075f), 0f, 0.3f));
        mi.SaveAndReimport();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        var root = new GameObject("W_Sword");                          // built at the origin, then moved under the camera
        var holder = new GameObject("Model").transform; holder.SetParent(root.transform, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset); model.transform.SetParent(holder, false);
        var r = model.GetComponentInChildren<Renderer>();

        // whatever axes the FBX came with: the longest one is the blade, the thinnest one the normal of the flat;
        // the blade's side is the one reaching farther from the grip (the origin)
        var b = r.bounds; Vector3 size = b.size;
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        int along = 0, thin = 0;
        for (int i = 1; i < 3; i++) { if (size[i] > size[along]) along = i; if (size[i] < size[thin]) thin = i; }
        Vector3 bladeDir = axes[along] * (Mathf.Abs(b.max[along]) >= Mathf.Abs(b.min[along]) ? 1f : -1f);
        Vector3 flatDir = axes[thin];
        holder.localRotation = Quaternion.Inverse(Quaternion.LookRotation(flatDir, bladeDir));
        b = r.bounds;
        foreach (var rr in model.GetComponentsInChildren<Renderer>()) { rr.shadowCastingMode = ShadowCastingMode.Off; rr.lightProbeUsage = LightProbeUsage.BlendProbes; }

        var tip = new GameObject("Tip").transform; tip.SetParent(root.transform, false); tip.localPosition = new Vector3(0f, b.max.y - 0.03f, 0f);
        var trail = tip.gameObject.AddComponent<TrailRenderer>();
        trail.sharedMaterial = WeaponSetup.FxMat("M_SwordTrail", null, new Color(1f, 0.96f, 0.88f, 0.28f), 0.9f);
        trail.time = 0.09f; trail.minVertexDistance = 0.02f; trail.emitting = false;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.09f), new Keyframe(1f, 0f));
        trail.shadowCastingMode = ShadowCastingMode.Off; trail.receiveShadows = false;

        var sword = root.AddComponent<MeleeWeapon>();
        sword.displayName = "Bastard sword"; sword.slotName = "Sword"; sword.trail = trail;
        log.Append("  " + sword.displayName + ": " + b.size.ToString("F3") + " m, " + r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3
                   + " tris, blade along model axis " + "XYZ"[along] + (bladeDir[along] > 0f ? "+" : "-") + ", flat " + "XYZ"[thin] + "\n");
        root.transform.SetParent(pivot, false);
        sword.PutAtRest();
        return sword;
    }
}

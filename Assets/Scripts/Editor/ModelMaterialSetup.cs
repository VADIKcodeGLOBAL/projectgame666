using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Finishes the import of a model converted by Source/Blender/Scripts/glb_to_unity.py:
/// texture import settings, one material per source material (from <Name>_materials.json),
/// material remap on the FBX and a prefab in Assets/Prefabs/NPC.
/// Menu: Tools > Models > Setup Converted Model (select the model's folder in the Project window).
/// </summary>
public static class ModelMaterialSetup
{
    [Serializable]
    class MatRow
    {
        public string name, baseColor, normal, metallicSmoothness, emission;
        public float[] color, emissionColor;
        public float metallic, roughness, alpha, emissionStrength, alphaSoft, alphaMin;
        public bool alphaLinked, blend, doubleSided;
    }

    [Serializable]
    class ModelInfo
    {
        public string name;
        public float height;
        public int tris;
        public MatRow[] materials;
    }

    [MenuItem("Tools/Models/Setup Converted Model (selected folder)")]
    static void MenuSetup()
    {
        string dir = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(dir) || !AssetDatabase.IsValidFolder(dir)) { Debug.LogWarning("Select the model folder (the one with *_materials.json)."); return; }
        Setup(dir);
    }

    static Texture2D Tex(string dir, string file, Action<TextureImporter> cfg)
    {
        if (string.IsNullOrEmpty(file)) return null;
        string path = dir + "/Textures/" + file;
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { EditorAutomation.Log("missing texture: " + path); return null; }
        cfg(ti);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Color Srgb(float[] c, float a)
    {
        if (c == null || c.Length < 3) return new Color(1f, 1f, 1f, a);
        var g = new Color(c[0], c[1], c[2], 1f).gamma;      // Blender stores linear values, Unity material colours are sRGB
        g.a = a;
        return g;
    }

    static void StandardMode(Material m, bool fade)
    {
        m.SetFloat("_Mode", fade ? 2f : 3f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)(fade ? BlendMode.SrcAlpha : BlendMode.One));
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        if (fade) { m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); }
        else { m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); }
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    public static void Setup(string dir)
    {
        dir = dir.TrimEnd('/');
        string json = Directory.GetFiles(dir, "*_materials.json").FirstOrDefault();
        if (json == null) throw new Exception("no *_materials.json in " + dir);
        var info = JsonUtility.FromJson<ModelInfo>(File.ReadAllText(json));
        EditorAutomation.Log("model setup: " + info.name + ", " + info.materials.Length + " materials, " + info.tris + " tris");

        string matDir = dir + "/Materials";
        if (!AssetDatabase.IsValidFolder(matDir)) { Directory.CreateDirectory(matDir); AssetDatabase.Refresh(); }
        Shader std = Shader.Find("Standard");
        Shader two = Shader.Find("ProjectGame/CharacterTwoSided");
        if (two == null) { EditorAutomation.Log("CharacterTwoSided shader not found, using Standard"); two = std; }

        var made = new Dictionary<string, Material>();
        int nCut = 0, nBlend = 0, nOpaque = 0;
        foreach (var r in info.materials)
        {
            bool transparent = r.blend && !r.alphaLinked;                 // e.g. glass: constant alpha
            bool fade = r.blend && r.alphaLinked && r.alphaSoft <= 0.12f; // thin decals with a soft alpha
            bool cutout = r.blend && r.alphaLinked && !fade;              // hair cards: sorted correctly only with alpha test
            var baseTex = Tex(dir, r.baseColor, ti => { ti.alphaIsTransparency = r.alphaLinked; ti.anisoLevel = 4; if (cutout) { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.4f; } });
            var nrm = Tex(dir, r.normal, ti => { ti.textureType = TextureImporterType.NormalMap; });
            var ms = Tex(dir, r.metallicSmoothness, ti => { ti.sRGBTexture = false; });
            var emi = Tex(dir, r.emission, ti => { });

            string path = matDir + "/" + r.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = (transparent || fade || !r.doubleSided) ? std : two;
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;

            m.SetColor("_Color", baseTex != null ? new Color(1f, 1f, 1f, r.alpha) : Srgb(r.color, r.alpha));
            if (baseTex != null) m.SetTexture("_MainTex", baseTex);
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            if (ms != null)
            {
                m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICGLOSSMAP");
                m.SetFloat("_Metallic", 1f); m.SetFloat("_Glossiness", 1f); m.SetFloat("_GlossMapScale", 1f);
            }
            else { m.SetFloat("_Metallic", r.metallic); m.SetFloat("_Glossiness", 1f - r.roughness); }
            if (emi != null)
            {
                m.SetTexture("_EmissionMap", emi); m.SetColor("_EmissionColor", Color.white * Mathf.Max(0.5f, r.emissionStrength));
                m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else m.SetColor("_EmissionColor", Color.black);

            if (transparent) { StandardMode(m, false); nBlend++; }
            else if (fade) { StandardMode(m, true); nBlend++; }
            else if (sh == two) { m.SetFloat("_Cutoff", cutout ? 0.4f : 0f); if (cutout) nCut++; else nOpaque++; }
            else nOpaque++;
            EditorUtility.SetDirty(m);
            made[r.name] = m;
        }
        AssetDatabase.SaveAssets();

        string fbx = dir + "/Models/" + info.name + ".fbx";
        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi == null) throw new Exception("model not found: " + fbx);
        mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        foreach (var kv in made) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
        mi.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var mr = model.GetComponentInChildren<MeshRenderer>();
        int unmapped = mr.sharedMaterials.Count(x => x == null || !made.ContainsValue(x));
        EditorAutomation.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "  {0}: bounds {1}, sub-meshes {2}, not remapped {3}; materials opaque {4}, cutout {5}, blended {6}",
            info.name, mr.bounds.size, mr.sharedMaterials.Length, unmapped, nOpaque, nCut, nBlend));

        const string prefabDir = "Assets/Prefabs/NPC";
        if (!AssetDatabase.IsValidFolder(prefabDir)) { Directory.CreateDirectory(prefabDir); AssetDatabase.Refresh(); }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = info.name;
        var cap = inst.AddComponent<CapsuleCollider>();
        cap.direction = 1; cap.height = mr.bounds.size.y; cap.radius = 0.28f; cap.center = new Vector3(0f, mr.bounds.size.y * 0.5f, 0f);
        PrefabUtility.SaveAsPrefabAsset(inst, prefabDir + "/" + info.name + ".prefab");

        // check renders: the model high above the level, against the sky (nothing is left in the scene)
        inst.hideFlags = HideFlags.HideAndDontSave;
        inst.transform.position = new Vector3(0f, 600f, 0f);
        float h = mr.bounds.size.y;
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            Vector3 c = inst.transform.position + Vector3.up * h * 0.52f;
            EditorAutomation.Shot(info.name + "_a", c + new Vector3(0.9f, 0.15f, 2.5f), c, 45f);
            EditorAutomation.Shot(info.name + "_b", c + new Vector3(-0.9f, 0.15f, -2.5f), c, 45f);
            EditorAutomation.Shot(info.name + "_face", c + new Vector3(0.35f, h * 0.38f, 0.95f), c + Vector3.up * h * 0.33f, 40f);
        }
        finally { ShaderUtil.allowAsyncCompilation = async; UnityEngine.Object.DestroyImmediate(inst); }
        EditorAutomation.Log("MODEL_OK " + info.name);
    }
}

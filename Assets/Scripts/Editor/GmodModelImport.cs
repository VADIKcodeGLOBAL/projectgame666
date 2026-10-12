using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Gmod playermodels in Assets/Art/Characters/Gmod/&lt;model&gt;/&lt;qc&gt;/ (copied from the Blender conversion in
/// D:\Gmod\unarchive-models, local only: the folder is in .gitignore). Import settings, set once when a file is first imported
/// (so they can be changed by hand afterwards), and only for that folder:
///  - FBX: Source units (inches) to metres, blend shapes kept, no animation, cameras or lights, a Generic rig without an avatar
///    (the animation setup makes its own), materials from the FBX material description (albedo and normal from textures/);
///  - textures: *_n_gl / *normal* are normal maps (OpenGL green, as Unity wants), roughness and metal maps linear.
/// "gmod-check" (automation): every model's first variant — height, bones, the ValveBiped core, blend shapes, materials missing
/// their texture — and a render from three sides into Logs/shots/gmod_&lt;model&gt;.png.
/// </summary>
public class GmodModelImport : AssetPostprocessor
{
    public const string Root = "Assets/Art/Characters/Gmod";
    const float SourceUnit = 0.0254f;                               // 1 Source unit = 1 inch
    static readonly string[] CoreBones = { "Pelvis", "Spine", "Spine2", "Spine4", "Neck1", "Head1", "L_UpperArm", "L_Forearm", "L_Hand", "R_UpperArm", "R_Hand", "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Foot" };

    static bool Ours(string path) { return path.Replace('\\', '/').StartsWith(Root + "/"); }

    static bool IsNormalMap(string file)
    {
        string n = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        return n.Contains("_n_gl") || n.Contains("normal");
    }

    static bool IsData(string file)
    {
        string n = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        return n.Contains("rough") || n.Contains("metal") || n.Contains("_ao") || n.Contains("mask");
    }

    void OnPreprocessModel()
    {
        if (!Ours(assetPath) || !assetImporter.importSettingsMissing) return;
        var mi = (ModelImporter)assetImporter;
        mi.globalScale = SourceUnit; mi.useFileScale = true;
        mi.importBlendShapes = true; mi.importVisibility = false;
        mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        mi.optimizeGameObjects = false;                              // the bones stay: animation and secondary motion come later
        mi.importNormals = ModelImporterNormals.Import; mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.isReadable = false;
    }

    void OnPreprocessTexture()
    {
        if (!Ours(assetPath) || !assetImporter.importSettingsMissing) return;
        var ti = (TextureImporter)assetImporter;
        if (IsNormalMap(assetPath)) ti.textureType = TextureImporterType.NormalMap;
        else if (IsData(assetPath)) ti.sRGBTexture = false;
        ti.anisoLevel = 4;
    }

    // ------------------------------------------------------------------ check
    /// <summary>Materials of the model whose report.json gives them a base texture.</summary>
    static HashSet<string> TexturedInReport(string dir)
    {
        var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        string json = Path.Combine(dir, "report.json");
        if (!File.Exists(json)) return set;
        foreach (Match m in Regex.Matches(File.ReadAllText(json), "\"([^\"]+)\"\\s*:\\s*\"base=")) set.Add(m.Groups[1].Value);
        return set;
    }

    /// <summary>"gmod-mats &lt;part of a path&gt;": the materials of the matching models as Unity made them (shader, mode, metallic,
    /// smoothness and its source, textures).</summary>
    public static void DumpMaterials(string filter)
    {
        var fbxs = AssetDatabase.FindAssets("t:Model", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath)
                                .Where(p => p.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(p => p).Take(3).ToList();
        foreach (var p in fbxs)
        {
            EditorAutomation.Log("  " + p);
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>())
            {
                System.Func<string, string> tex = prop => m.HasProperty(prop) && m.GetTexture(prop) != null ? m.GetTexture(prop).name : "-";
                System.Func<string, string> num = prop => m.HasProperty(prop) ? m.GetFloat(prop).ToString("0.00") : "-";
                EditorAutomation.Log("    " + m.name + ": " + m.shader.name + ", mode " + num("_Mode") + ", colour " + (m.HasProperty("_Color") ? m.GetColor("_Color").ToString("F2") : "-")
                    + ", metallic " + num("_Metallic") + ", smoothness " + num("_Glossiness") + " (source " + num("_SmoothnessTextureChannel") + ", scale " + num("_GlossMapScale") + ")"
                    + ", main " + tex("_MainTex") + ", normal " + tex("_BumpMap") + " x" + num("_BumpScale") + ", metal map " + tex("_MetallicGlossMap") + ", spec map " + tex("_SpecGlossMap")
                    + ", keywords " + string.Join(" ", m.shaderKeywords));
            }
        }
        EditorAutomation.Log("GMODMATS_OK");
    }

    public static void Check()
    {
        var fbxs = AssetDatabase.FindAssets("t:Model", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToList();
        EditorAutomation.Log("gmod-check: " + fbxs.Count + " FBX under " + Root);
        if (fbxs.Count == 0) { EditorAutomation.Log("GMODCHECK_FAILED: no models"); return; }

        // a texture a material uses as its normal map, imported as a plain texture (an unusual name): fixed here
        int fixedNormals = 0;
        foreach (var p in fbxs)
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>())
            {
                var t = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                var ti = t != null ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter : null;
                if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); fixedNormals++; }
            }
        if (fixedNormals > 0) EditorAutomation.Log("  normal maps set by their use in the materials: " + fixedNormals);

        Directory.CreateDirectory(EditorAutomation.ShotDir);
        int ok = 0, groups = 0;
        foreach (var g in fbxs.GroupBy(p => Path.GetDirectoryName(p).Replace('\\', '/')))
        {
            groups++;
            string dir = g.Key, model = dir.Substring(Root.Length + 1).Replace('/', '_');
            string fbx = g.OrderBy(p => p.ToLowerInvariant().Contains("nsfw") ? 1 : 0).ThenBy(p => p).First();   // the first plain variant
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var scene = PreviewShots.Begin();
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>();
                var rends = inst.GetComponentsInChildren<Renderer>();
                Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds();
                foreach (var r in rends) b.Encapsulate(r.bounds);
                var all = inst.GetComponentsInChildren<Transform>(true);
                var pelvis = all.FirstOrDefault(t => t.name == "ValveBiped.Bip01_Pelvis");
                int bones = pelvis != null ? pelvis.GetComponentsInChildren<Transform>(true).Length : 0;
                var missingCore = CoreBones.Where(c => !all.Any(t => t.name == "ValveBiped.Bip01_" + c)).ToList();
                int shapes = smrs.Sum(s => s.sharedMesh != null ? s.sharedMesh.blendShapeCount : 0);
                var expect = TexturedInReport(dir);
                var mats = rends.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
                var noTex = mats.Where(m => m.mainTexture == null && expect.Contains(m.name)).Select(m => m.name).ToList();
                bool good = b.size.y > 1.4f && b.size.y < 2.6f && missingCore.Count == 0 && noTex.Count == 0 && smrs.Length > 0;
                if (good) ok++;
                EditorAutomation.Log("  " + (good ? "ok   " : "FAIL ") + model + " (" + g.Count() + " FBX): " + Path.GetFileName(fbx) + ", " + b.size.y.ToString("0.00") + " m tall, "
                    + bones + " bones" + (missingCore.Count > 0 ? " MISSING " + string.Join(", ", missingCore.ToArray()) : ", ValveBiped core ok") + ", " + smrs.Length + " skinned meshes, "
                    + shapes + " blend shapes, " + mats.Count + " materials" + (noTex.Count > 0 ? ", NO TEXTURE: " + string.Join(", ", noTex.ToArray()) : ""));

                // a render from the front, the side and the back
                PreviewShots.Render(scene, "gmod_" + model, 500, 900, new PreviewShots.Frame(b.center, b.size.y, Vector3.forward),
                                    new PreviewShots.Frame(b.center, b.size.y, Vector3.right), new PreviewShots.Frame(b.center, b.size.y, Vector3.back));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        EditorAutomation.Log(ok == groups ? "GMODCHECK_OK (" + groups + " models)" : "GMODCHECK_FAILED: " + (groups - ok) + " of " + groups + " models");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the player's weapons from the low-poly models in Assets/Art/Weapons/Models/LowPoly (AK-47, MP5, 10mm pistol, sniper rifle;
/// made from Models/source by Source/Blender/Scripts/weapon_decimate.py: 5000 triangles per long gun, 3000 for the pistol;
/// the portal gun and the cannon are not used yet): texture import settings, materials (ProjectGame/WeaponPBR), material remap
/// on the FBX, real-world size and barrel along +Z, the muzzle point found on the mesh, muzzle fire quad, flash light and sparks.
/// Also makes the ammo pickup prefab. Called by the hill map generator and by the editor commands
/// "weapons" (rebuild the weapons in the open level), "weapons-view" (shots from the player camera) and "weapons-info".
/// </summary>
public static class WeaponSetup
{
    public const string ModelDir = "Assets/Art/Weapons/Models";
    const string MatDir = "Assets/Art/Weapons/Materials";
    const string FxTexDir = "Assets/Art/Weapons/Textures";
    const string PickupPath = "Assets/Prefabs/Weapons/AmmoPickup.prefab";

    class MatSpec
    {
        public string source, baseColor, normal, metallic, roughness, ao, emission;
        public MatSpec(string source, string dir, string baseColor, string normal, string metallic, string roughness, string ao = null, string emission = null)
        {
            this.source = source;
            Func<string, string> p = f => f == null ? null : ModelDir + "/" + dir + "/" + f;
            this.baseColor = p(baseColor); this.normal = p(normal); this.metallic = p(metallic); this.roughness = p(roughness); this.ao = p(ao); this.emission = p(emission);
        }
    }

    class Spec
    {
        public string id, fbx, muzzleMesh, magazinePart;
        public Vector3 modelRot, viewPos, viewRot, muzzleNudge;
        public float length, fireSize, lightRange = 3.5f;
        public MatSpec[] mats;
        public Action<Weapon> stats;
    }

    // barrel directions and sizes come from "weapons-info": the AK is exported in centimetres with the barrel along -X,
    // the others are in metres with the barrel along +Z
    static readonly Spec[] Specs =
    {
        new Spec {
            id = "AK47", fbx = "LowPoly/AK47_LP.fbx", modelRot = new Vector3(0f, 90f, 0f), length = 0.88f,
            viewPos = new Vector3(0.155f, -0.165f, 0.40f), fireSize = 0.24f,
            mats = new[] {
                new MatSpec("Wood_ak-47", "source/ak-47", "Wood_ak-47_Base_Color.png", "Wood_ak-47_OpenGL.png", "Wood_ak-47_Metallic.png", "Wood_ak-47_Roughness.png", "Wood_ak-47_AO.png"),
                new MatSpec("Metall_ak-47", "source/ak-47", "Metall_ak-47_Base_Color.png", "Metall_ak-47_OpenGL.png", "Metall_ak-47_Metallic.png", "Metall_ak-47_Roughness.png", "Metall_ak-47_AO.png") },
            stats = w => { w.displayName = "AK-47"; w.mode = Weapon.FireMode.Auto; w.damage = 40f; w.roundsPerSecond = 10f; w.spread = 0.6f;
                           w.magazineSize = 30; w.startMagazines = 4; w.maxMagazines = 8; w.reloadTime = 2.4f; w.recoil = 0.35f;
                           w.kick = new Vector3(0f, 0.008f, -0.04f); w.kickPitch = 2.5f; w.shotPitch = 1f; w.shotVolume = 0.55f; w.sparksPerRound = 6; } },
        new Spec {
            id = "MP5", fbx = "LowPoly/MP5_LP.fbx", length = 0.66f, muzzleMesh = "Barrel_low", magazinePart = "Mag_low",
            viewPos = new Vector3(0.15f, -0.155f, 0.36f), fireSize = 0.17f,
            mats = new[] {
                new MatSpec("low", "source/MP5", "MP5_BaseColor.png", "MP5_Normal.png", "MP5_Metallic.png", "MP5_Roughness.png", null, "MP5_Emissive.png"),
                new MatSpec("Maglow", "source/MP5", "Low_Maglow_BaseColor.png", "Low_Maglow_Normal.png", "Low_Maglow_Metallic.png", "Low_Maglow_Roughness.png", null, "Low_Maglow_Emissive.png") },
            stats = w => { w.displayName = "MP5"; w.mode = Weapon.FireMode.Auto; w.damage = 24f; w.roundsPerSecond = 13.3f; w.spread = 0.9f;
                           w.magazineSize = 30; w.startMagazines = 5; w.maxMagazines = 10; w.reloadTime = 2.0f; w.recoil = 0.22f;
                           w.kick = new Vector3(0f, 0.005f, -0.03f); w.kickPitch = 1.8f; w.shotPitch = 1.18f; w.shotVolume = 0.45f; w.sparksPerRound = 4; } },
        new Spec {
            id = "Pistol10mm", fbx = "LowPoly/Pistol10mm_LP.fbx", length = 0.205f, magazinePart = "Magazine",
            viewPos = new Vector3(0.12f, -0.115f, 0.30f), fireSize = 0.13f, lightRange = 3f,
            mats = new[] { new MatSpec("Gun", "source/Gun 10mm/Textures", "Gun_BaseColor.png", "Gun_Normal.png", "Gun_Metallic.png", "Gun_Roughness.png") },
            stats = w => { w.displayName = "10mm"; w.mode = Weapon.FireMode.Semi; w.damage = 34f; w.roundsPerSecond = 6f; w.spread = 0.5f;
                           w.magazineSize = 15; w.infiniteReserve = true; w.maxMagazines = 0; w.reloadTime = 1.4f; w.recoil = 0.6f;
                           w.kick = new Vector3(0f, 0.012f, -0.035f); w.kickPitch = 7f; w.shotPitch = 1.3f; w.shotVolume = 0.5f; w.sparksPerRound = 4; } },
        new Spec {
            id = "Sniper", fbx = "LowPoly/Sniper_LP.fbx", length = 1.25f, magazinePart = "mag",
            viewPos = new Vector3(0.16f, -0.17f, 0.46f), fireSize = 0.34f, lightRange = 5f,
            mats = new[] { new MatSpec("sniper", "textures", "sniperColor.png", "sniperNormal.png", "sniperMetallic.png", "sniperRoughness.png", "sniperAO.png") },
            stats = w => { w.displayName = "Sniper"; w.mode = Weapon.FireMode.Semi; w.damage = 400f; w.roundsPerSecond = 0.75f; w.spread = 3f; w.scopedSpread = 0f;
                           w.magazineSize = 5; w.startMagazines = 4; w.maxMagazines = 6; w.reloadTime = 3.0f; w.recoil = 2.5f; w.hasScope = true; w.scopeFov = 12f;
                           w.kick = new Vector3(0f, 0.02f, -0.09f); w.kickPitch = 8f; w.shotPitch = 0.72f; w.shotVolume = 0.85f; w.sparksPerRound = 12; } },
    };

    static string V(Vector3 v) { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", v.x, v.y, v.z); }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Directory.CreateDirectory(path); AssetDatabase.Refresh();
    }

    // ------------------------------------------------------------------ textures and materials
    static Texture2D Tex(string path, bool linear, bool normal, int maxSize = 2048)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { EditorAutomation.Log("  missing texture: " + path); return null; }
        bool dirty = false;
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type) { ti.textureType = type; dirty = true; }
        if (!normal && ti.sRGBTexture == linear) { ti.sRGBTexture = !linear; dirty = true; }
        if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; dirty = true; }
        if (dirty) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material WeaponMat(string name, MatSpec m)
    {
        string path = MatDir + "/" + name + ".mat";
        var sh = Shader.Find("ProjectGame/WeaponPBR");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); } else mat.shader = sh;
        mat.SetTexture("_MainTex", Tex(m.baseColor, false, false));
        mat.SetTexture("_BumpMap", Tex(m.normal, true, true));
        mat.SetTexture("_MetallicMap", Tex(m.metallic, true, false));
        mat.SetTexture("_RoughnessMap", Tex(m.roughness, true, false));
        mat.SetTexture("_OcclusionMap", Tex(m.ao, true, false));
        var emi = Tex(m.emission, false, false, 512);
        mat.SetTexture("_EmissionMap", emi); mat.SetColor("_EmissionColor", emi != null ? Color.white * 0.8f : Color.black);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D GenTex(string path, int w, int h, Func<float, float, float> alpha)
    {
        if (!File.Exists(path))
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h)) * 255f));
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
        }
        return FxTex(path, 256);
    }

    static Texture2D FxTex(string path, int maxSize)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return null;
        if (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp || ti.maxTextureSize != maxSize)
        {
            ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = maxSize; ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material FxMat(string name, Texture2D tex, Color tint, float intensity)
    {
        string path = MatDir + "/" + name + ".mat";
        var sh = Shader.Find("ProjectGame/MuzzleFlash");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); } else mat.shader = sh;
        mat.SetTexture("_MainTex", tex); mat.SetColor("_Color", tint); mat.SetFloat("_Intensity", intensity);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material LitMat(string name, Color c, float metallic, float gloss, Color emission)
    {
        string path = MatDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_Color", c); mat.SetFloat("_Metallic", metallic); mat.SetFloat("_Glossiness", gloss);
        mat.SetColor("_EmissionColor", emission); mat.EnableKeyword("_EMISSION"); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    class Fx { public Material fire, spark, beam; }

    static Fx MakeFx()
    {
        EnsureFolder(MatDir);
        var fx = new Fx();
        // the flash texture is 16:9 with the fire in the middle: the material shows the centre square
        string flashPath = AssetDatabase.FindAssets("t:Texture2D", new[] { FxTexDir }).Select(AssetDatabase.GUIDToAssetPath)
                                        .FirstOrDefault(p => p.IndexOf("muzzle", StringComparison.OrdinalIgnoreCase) >= 0);
        var flash = flashPath != null ? FxTex(flashPath, 1024) : null;
        if (flash == null) EditorAutomation.Log("  muzzle flash texture not found in " + FxTexDir);
        fx.fire = FxMat("M_MuzzleFire", flash, new Color(1f, 0.82f, 0.58f, 1f), 1.7f);
        if (flash != null && flash.height > 0 && flash.width > flash.height)
        {
            float sx = (float)flash.height / flash.width;
            fx.fire.mainTextureScale = new Vector2(sx, 1f); fx.fire.mainTextureOffset = new Vector2((1f - sx) * 0.5f, 0f);
        }
        var spark = GenTex(FxTexDir + "/T_Spark.png", 64, 64, (u, v) => { float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f; return Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f); });
        fx.spark = FxMat("M_Sparks", spark, new Color(1f, 0.72f, 0.35f, 1f), 3f);
        var beam = GenTex(FxTexDir + "/T_PickupBeam.png", 16, 128, (u, v) => Mathf.Pow(1f - v, 1.6f) * Mathf.Sin(u * Mathf.PI));
        fx.beam = FxMat("M_PickupBeam", beam, new Color(1f, 0.78f, 0.3f, 1f), 0.9f);
        return fx;
    }

    // ------------------------------------------------------------------ models
    static GameObject PrepareModel(Spec sp)
    {
        string path = ModelDir + "/" + sp.fbx;
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) throw new Exception("weapon model not found: " + path);
        mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        foreach (var m in sp.mats) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.source), WeaponMat("M_" + sp.id + "_" + m.source, m));
        mi.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Centre of the front-most vertices (within 1.2 cm of the tip), in world space; only meshes named meshName if given.</summary>
    static Vector3 MuzzlePoint(GameObject model, string meshName)
    {
        var pts = new List<Vector3>();
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (!string.IsNullOrEmpty(meshName) && r.name != meshName) continue;
            var mf = r.GetComponent<MeshFilter>(); var smr = r as SkinnedMeshRenderer;
            Mesh m = mf != null ? mf.sharedMesh : smr != null ? smr.sharedMesh : null;
            if (m == null) continue;
            foreach (var v in m.vertices) pts.Add(r.transform.TransformPoint(v));
        }
        if (pts.Count == 0) { var b = WorldBounds(model); return new Vector3(b.center.x, b.center.y, b.max.z); }
        float maxZ = pts.Max(p => p.z);
        var front = pts.Where(p => p.z >= maxZ - 0.012f).ToList();
        return new Vector3(front.Average(p => p.x), front.Average(p => p.y), maxZ);
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }

    static ParticleSystem MakeSparks(Transform muzzle, Material mat)
    {
        var go = new GameObject("Sparks"); go.transform.SetParent(muzzle, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
        main.startColor = new Color(1f, 0.85f, 0.55f, 1f);
        main.gravityModifier = 0.6f; main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 14f; shape.radius = 0.004f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.2f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = mat; pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.02f; pr.lengthScale = 1.5f;
        pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
        return ps;
    }

    static Weapon BuildWeapon(Spec sp, Transform pivot, Fx fx, AudioClip[] clips, StringBuilder log)
    {
        var asset = PrepareModel(sp);
        var root = new GameObject("W_" + sp.id);                                    // built at the origin, then moved under the camera
        var holder = new GameObject("Model").transform; holder.SetParent(root.transform, false);
        holder.localRotation = Quaternion.Euler(sp.modelRot);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset); model.transform.SetParent(holder, false);
        var b = WorldBounds(model);
        holder.localScale = Vector3.one * (sp.length / Mathf.Max(0.001f, b.size.z));  // real-world length
        b = WorldBounds(model);
        holder.localPosition = -b.center;                                           // the weapon's pivot is its middle
        b = WorldBounds(model);
        foreach (var r in model.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.lightProbeUsage = LightProbeUsage.BlendProbes; }

        var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(root.transform, false);
        muzzle.localPosition = MuzzlePoint(model, sp.muzzleMesh) + sp.muzzleNudge;

        var fire = GameObject.CreatePrimitive(PrimitiveType.Quad); fire.name = "MuzzleFire";
        Object.DestroyImmediate(fire.GetComponent<Collider>());
        fire.transform.SetParent(muzzle, false); fire.transform.localPosition = new Vector3(0f, 0f, 0.01f); fire.transform.localScale = Vector3.one * sp.fireSize;
        var fr = fire.GetComponent<MeshRenderer>(); fr.sharedMaterial = fx.fire; fr.enabled = false;
        fr.shadowCastingMode = ShadowCastingMode.Off; fr.receiveShadows = false; fr.lightProbeUsage = LightProbeUsage.Off; fr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var lgo = new GameObject("FlashLight"); lgo.transform.SetParent(muzzle, false); lgo.transform.localPosition = new Vector3(0f, 0f, 0.06f);
        var light = lgo.AddComponent<Light>();
        light.type = LightType.Point; light.range = sp.lightRange; light.intensity = 2.6f; light.color = new Color(1f, 0.72f, 0.38f);
        light.shadows = LightShadows.None; light.renderMode = LightRenderMode.Auto; light.enabled = false;

        var w = root.AddComponent<Weapon>();
        sp.stats(w);
        w.muzzle = muzzle; w.muzzleFire = fr; w.muzzleFireSize = sp.fireSize; w.flashLight = light;
        w.sparks = MakeSparks(muzzle, fx.spark); w.shotClips = clips;
        w.magazine = FindDeep(model.transform, sp.magazinePart);

        log.Append("  " + w.displayName + ": " + V(b.size) + " m, muzzle " + V(muzzle.localPosition) + ", magazine part " + (w.magazine != null ? w.magazine.name : "-") + "\n");
        root.transform.SetParent(pivot, false);
        root.transform.localPosition = sp.viewPos; root.transform.localRotation = Quaternion.Euler(sp.viewRot);
        return w;
    }

    /// <summary>The weapons under the player's camera and the inventory that drives them.</summary>
    public static WeaponInventory BuildPlayerWeapons(GameObject player, Camera camera)
    {
        var fx = MakeFx();
        var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/SFX/Weapons" })
            .Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).ToArray();
        var audio = player.GetComponent<AudioSource>();
        if (audio == null) audio = player.AddComponent<AudioSource>();
        audio.playOnAwake = false; audio.spatialBlend = 0f;

        var pivot = new GameObject("Weapons").transform; pivot.SetParent(camera.transform, false);
        var log = new StringBuilder("weapons (" + clips.Length + " shot sounds):\n");
        var list = Specs.Select(sp => BuildWeapon(sp, pivot, fx, clips, log)).ToArray();
        for (int i = 0; i < list.Length; i++) list[i].gameObject.SetActive(i == 0);
        var inv = player.AddComponent<WeaponInventory>();
        inv.cam = camera; inv.weapons = list; inv.audioSource = audio;
        EditorAutomation.Log(log.ToString().TrimEnd());
        return inv;
    }

    // ------------------------------------------------------------------ ammo pickup
    public static AmmoPickup AmmoPickupPrefab()
    {
        var fx = MakeFx();
        EnsureFolder(Path.GetDirectoryName(PickupPath).Replace('\\', '/'));
        var body = LitMat("M_AmmoMagazine", new Color(0.09f, 0.09f, 0.10f), 0.6f, 0.45f, new Color(0.10f, 0.06f, 0.01f));
        var brass = LitMat("M_AmmoBrass", new Color(0.85f, 0.62f, 0.25f), 1f, 0.7f, new Color(0.55f, 0.35f, 0.08f));

        var root = new GameObject("AmmoPickup");
        var pick = root.AddComponent<AmmoPickup>();
        var spin = new GameObject("Magazine").transform; spin.SetParent(root.transform, false); spin.localScale = Vector3.one * 1.6f;
        Action<string, Vector3, Vector3, Vector3, Material> part = (name, pos, rot, size, mat) =>
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(spin, false); g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(rot); g.transform.localScale = size;
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        };
        part("Lower", new Vector3(0f, -0.07f, 0.012f), new Vector3(14f, 0f, 0f), new Vector3(0.035f, 0.13f, 0.075f), body);   // a curved rifle magazine
        part("Upper", new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(0.035f, 0.12f, 0.075f), body);
        part("Rounds", new Vector3(0f, 0.118f, 0f), Vector3.zero, new Vector3(0.022f, 0.018f, 0.065f), brass);
        for (int k = 0; k < 2; k++)                                                   // a soft upright glow, seen over the grass
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "Glow" + k;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(root.transform, false);
            q.transform.localPosition = new Vector3(0f, -pick.hoverHeight + 1.1f, 0f); q.transform.localRotation = Quaternion.Euler(0f, k * 90f, 0f);
            q.transform.localScale = new Vector3(0.35f, 2.2f, 1f);
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = fx.beam; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        pick.spinner = spin;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PickupPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<AmmoPickup>();
    }

    // ------------------------------------------------------------------ editor commands
    /// <summary>"weapons": replaces the weapons of the player in the hill level with freshly built ones and saves the scene.</summary>
    public static void InstallInOpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != KothMapGenerator.ScenePath) scene = EditorSceneManager.OpenScene(KothMapGenerator.ScenePath, OpenSceneMode.Single);
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        if (fp == null) { EditorAutomation.Log("WEAPONS_FAILED: no player in " + scene.path); return; }
        var player = fp.gameObject; var cam = player.GetComponentInChildren<Camera>();
        foreach (var n in new[] { "Gun", "Weapons" }) { var old = cam.transform.Find(n); if (old != null) Object.DestroyImmediate(old.gameObject); }
        foreach (var inv in player.GetComponents<WeaponInventory>()) Object.DestroyImmediate(inv);
        int missing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(player);         // the old PlayerGun
        var srcs = player.GetComponents<AudioSource>(); for (int i = 1; i < srcs.Length; i++) Object.DestroyImmediate(srcs[i]);

        var invNew = BuildPlayerWeapons(player, cam);
        var hud = Object.FindFirstObjectByType<SurvivalHud>(); if (hud != null) { hud.weapons = invNew; EditorUtility.SetDirty(hud); }
        var game = Object.FindFirstObjectByType<WaveSurvivalGame>(); if (game != null) { game.ammoPickupPrefab = AmmoPickupPrefab(); EditorUtility.SetDirty(game); }
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorAutomation.Log("  removed missing scripts: " + missing + ", hud " + (hud != null) + ", game " + (game != null) + ", scene saved " + saved);
        EditorAutomation.Log("WEAPONS_OK");
    }

    /// <summary>"weapons-view": each weapon from the player camera, at rest and with the muzzle fire on (Logs/shots/wpn_view_*, wpn_fire_*).</summary>
    public static void ViewShots()
    {
        var inv = Object.FindFirstObjectByType<WeaponInventory>();
        if (inv == null || inv.cam == null) { EditorAutomation.Log("WEAPONS_VIEW_FAILED: no weapons in the open scene"); return; }
        var t = inv.cam.transform;
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        var active = inv.weapons.Select(w => w.gameObject.activeSelf).ToArray();
        try
        {
            foreach (var w in inv.weapons)
            {
                foreach (var o in inv.weapons) o.gameObject.SetActive(o == w);
                EditorAutomation.Shot("wpn_view_" + w.name, t.position, t.position + t.forward, inv.cam.fieldOfView, false, inv.cam.nearClipPlane);
                w.muzzleFire.enabled = true; w.muzzleFire.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                w.flashLight.enabled = true;
                EditorAutomation.Shot("wpn_fire_" + w.name, t.position, t.position + t.forward, inv.cam.fieldOfView, false, inv.cam.nearClipPlane);
                w.muzzleFire.enabled = false; w.flashLight.enabled = false;
            }
        }
        finally
        {
            for (int i = 0; i < active.Length; i++) inv.weapons[i].gameObject.SetActive(active[i]);
            ShaderUtil.allowAsyncCompilation = async;
        }
        EditorAutomation.Log("WEAPONS_VIEW_OK");
    }

    /// <summary>"weapons-info": hierarchy, bounds, materials and shots of each weapon model.</summary>
    public static void Info()
    {
        AssetDatabase.Refresh();
        foreach (var sp in Specs)
        {
            string path = ModelDir + "/" + sp.fbx;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) { EditorAutomation.Log("weapon model missing: " + path); continue; }
            var inst = Object.Instantiate(model); inst.hideFlags = HideFlags.HideAndDontSave;
            inst.transform.position = new Vector3(0f, 600f, 0f);
            var rs = inst.GetComponentsInChildren<Renderer>();
            var b = WorldBounds(inst);
            var sb = new StringBuilder();
            sb.Append("weapon " + sp.fbx + ": renderers " + rs.Length + ", bounds centre " + V(b.center - inst.transform.position) + " size " + V(b.size) + ", root rot " + V(inst.transform.eulerAngles) + ", scale " + V(inst.transform.localScale) + "\n");
            foreach (var r in rs)
                sb.Append("    " + AnimationUtility.CalculateTransformPath(r.transform, inst.transform) + " bounds c " + V(r.bounds.center - inst.transform.position) + " s " + V(r.bounds.size)
                          + " mats [" + string.Join(", ", r.sharedMaterials.Select(x => x != null ? x.name : "null")) + "]\n");
            EditorAutomation.Log(sb.ToString().TrimEnd());
            float d = b.size.magnitude * 1.6f;
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            try
            {
                EditorAutomation.Shot("wpn_" + sp.id + "_px", b.center + Vector3.right * d, b.center, 35f);
                EditorAutomation.Shot("wpn_" + sp.id + "_pz", b.center + Vector3.forward * d, b.center, 35f);
            }
            finally { ShaderUtil.allowAsyncCompilation = async; Object.DestroyImmediate(inst); }
        }
        EditorAutomation.Log("WEAPONS_INFO_OK");
    }
}

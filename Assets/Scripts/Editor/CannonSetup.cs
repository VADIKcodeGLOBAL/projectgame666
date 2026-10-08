using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// The field cannon: material (ProjectGame/WeaponPBR from the 1001_* texture set), the rig made by Source/Blender/Scripts/cannon_import.py
/// turned so the muzzle looks along +Z and scaled to Scale, a pivot for the barrel and for each wheel, the carriage collider,
/// the gunner camera, muzzle fire / smoke / light, aiming arc and blast ring, and the dust textures and materials of the hits.
/// Prefab Assets/Prefabs/Weapons/FieldCannon.prefab; in the level one cannon on the summit plateau and the CannonballSystem.
/// Called by the hill map generator; commands "cannon" (into the open level) and "cannon-view" (pictures).
/// </summary>
public static class CannonSetup
{
    const string ModelPath = "Assets/Art/Weapons/cannon/Models/Cannon.fbx";
    const string TexDir = "Assets/Art/Weapons/cannon/textures";
    const string MatDir = "Assets/Art/Weapons/Materials";
    const string FxTexDir = "Assets/Art/Weapons/Textures";
    const string PrefabPath = "Assets/Prefabs/Weapons/FieldCannon.prefab";
    /// <summary>The model is a 4.7 m fortress gun; scaled to a field gun.</summary>
    public const float Scale = 0.75f;

    class Fx { public Material dust, smoke, trail, patch, ball, arc, ring, fire; }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Directory.CreateDirectory(path); AssetDatabase.Refresh();
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
        return null;
    }

    // ------------------------------------------------------------------ materials and textures
    static Material CannonMat()
    {
        EnsureFolder(MatDir);
        string path = MatDir + "/M_Cannon.mat";
        var sh = Shader.Find("ProjectGame/WeaponPBR");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        m.SetTexture("_MainTex", WeaponSetup.Tex(TexDir + "/1001_Base_Color.png", false, false, 2048));
        m.SetTexture("_BumpMap", WeaponSetup.Tex(TexDir + "/1001_Normal_OpenGL.png", true, true, 2048));
        m.SetTexture("_MetallicMap", WeaponSetup.Tex(TexDir + "/1001_Metallic.png", true, false, 1024));
        m.SetTexture("_RoughnessMap", WeaponSetup.Tex(TexDir + "/1001_Roughness.png", true, false, 1024));
        m.SetTexture("_OcclusionMap", WeaponSetup.Tex(TexDir + "/1001_Mixed_AO.png", true, false, 1024));
        m.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>A white texture with the given alpha (written once), clamped, with mipmaps.</summary>
    static Texture2D AlphaTex(string name, int size, Func<float, float, float> alpha, Func<float, float, float> grey = null)
    {
        EnsureFolder(FxTexDir);
        string path = FxTexDir + "/" + name + ".png";
        WeaponSetup.WriteTextureOnce(path, size, size, (u, v) =>
        {
            float g = Mathf.Clamp01(grey != null ? grey(u, v) : 1f);
            return new Color(g, g, g, Mathf.Clamp01(alpha(u, v)));
        });
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp)
        {
            ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Hermite step from 0 at e0 to 1 at e1 (GLSL smoothstep; Mathf.SmoothStep is an interpolation, not this).</summary>
    static float Step(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

    static float Noise(float u, float v, float freq, float seed)
    {
        float n = 0f, a = 0.5f;
        for (int o = 0; o < 4; o++) { n += a * Mathf.PerlinNoise(seed + u * freq, seed * 1.7f + v * freq); freq *= 2f; a *= 0.5f; }
        return n / 0.9375f;
    }

    static Material Mat(string name, Shader sh, Texture tex, Color c, Action<Material> extra = null)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        if (tex != null) m.SetTexture("_MainTex", tex);
        m.SetColor("_Color", c);
        if (extra != null) extra(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Fx MakeFx()
    {
        EnsureFolder(MatDir);
        var fx = new Fx();
        foreach (var old in new[] { "T_DustPuff", "T_DustPatch", "T_DustScorch" })   // earlier versions: too faint / square
            if (AssetImporter.GetAtPath(FxTexDir + "/" + old + ".png") != null) AssetDatabase.DeleteAsset(FxTexDir + "/" + old + ".png");
        var puff = AlphaTex("T_DustCloud", 128, (u, v) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            return Mathf.Pow(Mathf.Clamp01(1f - r), 0.8f) * Mathf.Lerp(0.6f, 1f, Noise(u, v, 4f, 3.1f));
        });
        var band = AlphaTex("T_SmokeBand", 32, (u, v) => Mathf.Pow(Mathf.Sin(v * Mathf.PI), 1.5f));
        var patch = AlphaTex("T_DustMark", 256, (u, v) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float ragged = r + (Noise(u, v, 5f, 7.3f) - 0.5f) * 0.35f;      // a torn edge
            float body = 1f - Step(0.6f, 1f, ragged);                // nothing left at the edge of the square
            float speckle = Mathf.Lerp(0.7f, 1f, Noise(u, v, 14f, 1.9f));
            return body * speckle * 0.95f;
        }, (u, v) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            // a dark scorched middle and a light dusty halo: it shows on grass and on bare ground alike
            return Mathf.Lerp(0.28f, 1f, Step(0.05f, 0.6f, r)) * Mathf.Lerp(0.85f, 1.05f, Noise(u, v, 9f, 4.4f));
        });
        var white = AlphaTex("T_White", 4, (u, v) => 1f);
        var dustSh = Shader.Find("ProjectGame/DustParticle");
        fx.dust = Mat("M_Dust", dustSh, puff, new Color(0.56f, 0.49f, 0.41f, 1f));
        fx.smoke = Mat("M_CannonSmoke", dustSh, puff, new Color(0.88f, 0.88f, 0.86f, 1f));
        fx.trail = Mat("M_CannonTrail", dustSh, band, new Color(0.85f, 0.85f, 0.83f, 1f));
        fx.patch = Mat("M_DustPatch", Shader.Find("ProjectGame/DustDecal"), patch, new Color(0.70f, 0.62f, 0.50f, 1f));
        fx.ball = Mat("M_Cannonball", Shader.Find("Standard"), null, new Color(0.07f, 0.07f, 0.075f), m => { m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Glossiness", 0.45f); });
        var flash = Shader.Find("ProjectGame/MuzzleFlash");
        fx.arc = Mat("M_AimArc", flash, white, new Color(1f, 0.92f, 0.6f, 0.35f), m => m.SetFloat("_Intensity", 1f));
        fx.ring = Mat("M_BlastRing", flash, white, new Color(1f, 0.35f, 0.2f, 0.8f), m => m.SetFloat("_Intensity", 1.4f));
        fx.fire = WeaponSetup.MakeFx().fire;
        return fx;
    }

    // ------------------------------------------------------------------ the prefab
    static LineRenderer Line(string name, Transform parent, Material m, float width, bool loop)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.loop = loop; lr.sharedMaterial = m; lr.widthMultiplier = width; lr.positionCount = 0;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.numCapVertices = 2; lr.enabled = false;
        lr.textureMode = LineTextureMode.Stretch; lr.alignment = LineAlignment.View;
        return lr;
    }

    static ParticleSystem Smoke(Transform parent, Material m)
    {
        var go = new GameObject("MuzzleSmoke"); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 1f; main.maxParticles = 60;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1f, 1f, 1f, 0.7f);
        main.gravityModifier = -0.04f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = 0.15f;
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.8f; lim.dampen = 0.15f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.2f)));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    static FieldCannon BuildPrefab(Fx fx, AudioClip[] clips)
    {
        var mat = CannonMat();
        var mi = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (mi == null) throw new Exception("cannon model not found: " + ModelPath + " (run cannon_import.py)");
        mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "cannon"), mat);
        mi.SaveAndReimport();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        var root = new GameObject("FieldCannon");
        var container = new GameObject("Model").transform; container.SetParent(root.transform, false);
        // a plain copy, not a prefab instance: the barrel and the wheels are moved onto pivots, which a prefab instance does not allow
        var model = Object.Instantiate(asset); model.name = asset.name; model.transform.SetParent(container, false);
        Transform muzzle = Find(model.transform, "Muzzle"), breech = Find(model.transform, "Breech"), barrel = Find(model.transform, "Barrel"), carriage = Find(model.transform, "Carriage");
        if (muzzle == null || breech == null || barrel == null || carriage == null) throw new Exception("cannon rig incomplete (Muzzle, Breech, Barrel, Carriage)");
        // the muzzle along +Z, the size of a field gun, the ground point between the wheels at the root
        // (measured on the wheels themselves: the FBX importer drops the position of the rig's root empty)
        Vector3 bore = muzzle.position - breech.position, h = new Vector3(bore.x, 0f, bore.z);
        container.rotation = Quaternion.FromToRotation(h.normalized, Vector3.forward);
        container.localScale = Vector3.one * Scale;
        var wheelRenderers = new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" }.Select(n => Find(model.transform, n)).Where(w => w != null).Select(w => w.GetComponent<Renderer>()).ToArray();
        if (wheelRenderers.Length != 4) throw new Exception("cannon wheels missing");
        Vector3 groundPoint = Vector3.zero; float lowest = float.MaxValue;
        foreach (var wr in wheelRenderers) { groundPoint += wr.bounds.center * 0.25f; lowest = Mathf.Min(lowest, wr.bounds.min.y); }
        groundPoint.y = lowest;
        container.position -= groundPoint;
        foreach (var r in model.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.lightProbeUsage = LightProbeUsage.BlendProbes; }

        // pivots aligned with the root: the barrel about X, each wheel about X
        var pivot = new GameObject("BarrelPivot").transform; pivot.SetParent(root.transform, false); pivot.position = barrel.position;
        barrel.SetParent(pivot, true);
        string[] wheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        var wheelPivots = new Transform[4]; var radius = new float[4];
        for (int i = 0; i < 4; i++)
        {
            var w = Find(model.transform, wheelNames[i]);
            if (w == null) throw new Exception("wheel missing: " + wheelNames[i]);
            var wp = new GameObject(wheelNames[i] + "_Pivot").transform; wp.SetParent(root.transform, false); wp.position = w.position;
            w.SetParent(wp, true);
            wheelPivots[i] = wp; radius[i] = w.GetComponent<Renderer>().bounds.extents.y;
        }

        var cannon = root.AddComponent<FieldCannon>();
        cannon.barrelPivot = pivot; cannon.muzzle = muzzle; cannon.breech = breech; cannon.wheels = wheelPivots; cannon.wheelRadius = radius;
        bore = muzzle.position - breech.position;
        cannon.restElevation = Mathf.Atan2(bore.y, new Vector2(bore.x, bore.z).magnitude) * Mathf.Rad2Deg;
        Func<int, Vector3> foot = i => new Vector3(wheelPivots[i].position.x, 0f, wheelPivots[i].position.z);
        cannon.frontContact = (foot(0) + foot(1)) * 0.5f; cannon.rearLeftContact = foot(2); cannon.rearRightContact = foot(3);

        // the carriage and the wheels block the way; the barrel above them does not
        var b = carriage.GetComponent<Renderer>().bounds;
        foreach (var wp in wheelPivots) b.Encapsulate(wp.GetComponentInChildren<Renderer>().bounds);
        var box = root.AddComponent<BoxCollider>(); box.center = b.center; box.size = b.size;
        cannon.body = box;
        var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeAll;

        // the gunner's eye: standing at the left of the breech, head above the carriage, so the barrel is off to the right
        // and the middle of the view (the crosshair) stays clear
        cannon.sightLocal = new Vector3(b.min.x - 0.05f, b.max.y + 0.95f, b.min.z + 0.35f);
        var camGo = new GameObject("GunnerCamera"); camGo.transform.SetParent(root.transform, false); camGo.transform.localPosition = cannon.sightLocal;
        var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.1f; cam.farClipPlane = 8000f; cam.fieldOfView = 75f; cam.enabled = false;
        cam.cullingMask &= ~(1 << WeaponSetup.ViewModelLayer());      // the hands, the guns and a cigarette are the view model camera's
        camGo.AddComponent<AudioListener>().enabled = false;
        var blur = camGo.AddComponent<EdgeBlurEffect>(); blur.shader = Shader.Find("Hidden/ProjectGame/EdgeBlur");
        cannon.gunnerCamera = cam;

        // muzzle fire, light and smoke; aiming arc and blast ring; sound
        var fire = GameObject.CreatePrimitive(PrimitiveType.Quad); fire.name = "MuzzleFire";
        Object.DestroyImmediate(fire.GetComponent<Collider>());
        fire.transform.SetParent(pivot, false); fire.transform.position = muzzle.position; fire.transform.localScale = Vector3.one * 1.8f;
        var fr = fire.GetComponent<MeshRenderer>(); fr.sharedMaterial = fx.fire; fr.shadowCastingMode = ShadowCastingMode.Off; fr.receiveShadows = false; fr.enabled = false;
        cannon.muzzleFire = fr;
        var lg = new GameObject("MuzzleLight"); lg.transform.SetParent(pivot, false); lg.transform.position = muzzle.position;
        var light = lg.AddComponent<Light>(); light.type = LightType.Point; light.range = 14f; light.color = new Color(1f, 0.72f, 0.4f); light.shadows = LightShadows.None; light.enabled = false;
        cannon.muzzleLight = light;
        cannon.muzzleSmoke = Smoke(root.transform, fx.smoke);
        cannon.arcLine = Line("AimArc", root.transform, fx.arc, 0.09f, false);
        cannon.blastRing = Line("BlastRing", root.transform, fx.ring, 0.22f, true);
        var audio = root.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0.5f; audio.minDistance = 12f; audio.maxDistance = 450f; audio.rolloffMode = AudioRolloffMode.Linear;
        cannon.audioSource = audio; cannon.shotClips = clips;

        EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        int tris = root.GetComponentsInChildren<MeshFilter>().Where(f => f.GetComponent<MeshRenderer>().sharedMaterial == mat).Sum(f => f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0);
        EditorAutomation.Log("  cannon rig (root space): pivot " + pivot.localPosition.ToString("F3") + ", muzzle " + muzzle.position.ToString("F3") + ", breech " + breech.position.ToString("F3")
                             + ", body centre " + b.center.ToString("F3") + ", wheels " + string.Join(" ", wheelPivots.Select(w => w.position.ToString("F2")).ToArray()) + ", sight " + cannon.sightLocal.ToString("F2"));
        EditorAutomation.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "  cannon prefab: {0} triangles, size {1:0.00} x {2:0.00} x {3:0.00} m, rest elevation {4:0.0} deg, wheel radius {5:0.00}/{6:0.00} m, ground y {7:0.000}",
            tris, b.size.x, b.size.y, b.size.z, cannon.restElevation, radius[0], radius[2], model.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y)));
        Object.DestroyImmediate(root);
        return prefab.GetComponent<FieldCannon>();
    }

    static AudioClip[] ShotClips()
    {
        return AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/SFX/Weapons" })
            .Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).ToArray();
    }

    // ------------------------------------------------------------------ into the level
    /// <summary>One cannon near the edge of the summit plateau (inside the circle, beside the player's first view, muzzle outwards) and the cannonball system.</summary>
    public static FieldCannon AddToLevel(Transform parent, WaveSurvivalGame game, Transform player)
    {
        var fx = MakeFx(); var clips = ShotClips();
        var prefab = BuildPrefab(fx, clips);
        Physics.SyncTransforms();

        var sys = Object.FindFirstObjectByType<CannonballSystem>();
        if (sys == null) { sys = new GameObject("Cannonballs").AddComponent<CannonballSystem>(); sys.transform.SetParent(parent, false); }
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere); sys.ballMesh = tmp.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(tmp);
        sys.ballMaterial = fx.ball; sys.trailMaterial = fx.trail; sys.dustMaterial = fx.dust; sys.decalMaterial = fx.patch; sys.boomClips = clips;
        EditorUtility.SetDirty(sys);

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
        inst.transform.SetParent(parent, false);
        var cannon = inst.GetComponent<FieldCannon>();
        Vector3 c = game != null && game.zone != null ? game.zone.transform.position : Vector3.zero;
        Vector3 look = player != null ? player.forward : Vector3.forward; look.y = 0f; look.Normalize();
        bool placed = false;
        for (int k = 0; k < 12 && !placed; k++)                        // beside the first view, then around the circle
        {
            Vector3 dir = Quaternion.Euler(0f, 90f + k * 30f, 0f) * look;
            Vector3 at = c + dir * 9.5f;                                // near the edge of the plateau: it can fire down the slopes
            placed = cannon.Place(at, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, true);
        }
        if (!placed) cannon.Place(c + look * 6.5f, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, false);
        var health = player != null ? player.GetComponent<PlayerHealth>() : null;
        var blur = cannon.gunnerCamera.GetComponent<EdgeBlurEffect>(); if (blur != null) blur.health = health;
        EditorUtility.SetDirty(cannon);
        EditorAutomation.Log("  cannon placed at " + cannon.transform.position.ToString("F1") + ", facing " + cannon.transform.eulerAngles.y.ToString("F0") + " deg, free spot " + placed);
        return cannon;
    }

    /// <summary>"cannon": the cannon (rebuilt) into the open hill level, the HUD canvas rebuilt for its sight, scene saved.</summary>
    public static void InstallInOpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != KothMapGenerator.ScenePath) scene = EditorSceneManager.OpenScene(KothMapGenerator.ScenePath, OpenSceneMode.Single);
        var game = Object.FindFirstObjectByType<WaveSurvivalGame>();
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        if (game == null || fp == null) { EditorAutomation.Log("CANNON_FAILED: no game or player in " + scene.path); return; }
        foreach (var old in Object.FindObjectsByType<FieldCannon>(FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
        var parent = game.transform.parent;
        AddToLevel(parent, game, fp.transform);
        var hud = Object.FindFirstObjectByType<SurvivalHud>();
        if (hud != null) HudSetup.Build(hud);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        EditorAutomation.Log("  scene saved " + saved);
        EditorAutomation.Log("CANNON_OK");
    }

    /// <summary>"cannon-view": the cannon in the level from three sides and from the gunner's seat (Logs/shots/cannon_*.png).</summary>
    public static void ViewShots()
    {
        var cannon = Object.FindFirstObjectByType<FieldCannon>();
        if (cannon == null) { EditorAutomation.Log("CANNON_VIEW_FAILED: no cannon in the open scene"); return; }
        var t = cannon.transform; Vector3 c = t.position + Vector3.up * 1.1f;
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            EditorAutomation.Shot("cannon_side", c + t.right * 6f + Vector3.up * 0.6f, c, 45f, false, 0.1f);
            EditorAutomation.Shot("cannon_front", c + t.forward * 6.5f + t.right * 2.5f + Vector3.up * 1.2f, c, 45f, false, 0.1f);
            EditorAutomation.Shot("cannon_back", c - t.forward * 5.5f - t.right * 2f + Vector3.up * 1.8f, c, 45f, false, 0.1f);
            Vector3 eye = t.TransformPoint(cannon.sightLocal);
            EditorAutomation.Shot("cannon_gunner", eye, eye + t.forward * 10f + Vector3.down * 0.9f, 75f, true, 0.1f);
        }
        finally { ShaderUtil.allowAsyncCompilation = async; }
        EditorAutomation.Log("CANNON_VIEW_OK");
    }
}

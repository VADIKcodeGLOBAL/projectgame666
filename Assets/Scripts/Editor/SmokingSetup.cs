using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// The smoking rig (Smoking) under the view model camera: a hand with a placeholder arm, the pack (a lid on a hinge, filters
/// showing inside, the one that slides out), the lighter (a lid on a hinge, chimney, wheel, flame quad and its light), the
/// cigarette (filter, paper, ember, ash), the glow of the ember, the wisp from the tip and the breathed-out smoke.
/// Sizes are real (pack 5.5 x 8.7 x 2.2 cm, lighter 3.8 x 5.6 x 1.3 cm, cigarette 8.3 cm). Everything is on the view model
/// layer except the smoke (the player camera draws it, in the world). Called by WeaponSetup.BuildPlayerWeapons.
/// </summary>
public static class SmokingSetup
{
    const string MatDir = "Assets/Art/Weapons/Materials", TexDir = "Assets/Art/Weapons/Textures";
    const float CigRadius = 0.0039f, Filter = 0.025f, Paper = 0.058f;

    public static Smoking Build(Transform viewCam, int layer, Material armMat, StringBuilder log)
    {
        var old = viewCam.Find("Smoking"); if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("Smoking").transform; root.SetParent(viewCam, false);
        var s = root.gameObject.AddComponent<Smoking>();

        var paperMat = Mat("M_CigPaper", new Color(0.93f, 0.92f, 0.89f), 0f, 0.15f);
        var filterMat = Mat("M_CigFilter", new Color(0.80f, 0.53f, 0.27f), 0f, 0.25f);
        var ashMat = Mat("M_CigAsh", new Color(0.52f, 0.51f, 0.49f), 0f, 0.05f);
        var emberMat = WeaponSetup.LitMat("M_CigEmber", new Color(0.16f, 0.12f, 0.1f), 0f, 0.1f, Color.black);
        var packMat = Mat("M_PackBody", new Color(0.72f, 0.07f, 0.06f), 0f, 0.35f);
        var labelMat = Mat("M_PackLabel", new Color(0.95f, 0.94f, 0.90f), 0f, 0.3f);
        var steelMat = Mat("M_LighterSteel", new Color(0.78f, 0.79f, 0.81f), 1f, 0.72f);
        var darkMat = Mat("M_LighterDark", new Color(0.16f, 0.16f, 0.17f), 0.8f, 0.4f);
        var flameMat = FlameMat(FlameTex());
        var glowMat = WeaponSetup.FxMat("M_CigGlow", GlowTex(), new Color(1f, 0.42f, 0.12f, 1f), 1f);
        var smokeMat = SmokeMat(SmokeTex());

        // ---- the hand and its arm (the left one: the weapons keep theirs)
        s.hand = new GameObject("Hand").transform; s.hand.SetParent(root, false);
        var arm = root.gameObject.AddComponent<ViewModelArms>();
        arm.left.grip = s.hand;
        arm.left.shoulder = new Vector3(-0.2f, -0.26f, -0.02f); arm.left.elbowHint = new Vector3(-0.8f, -1f, -0.1f);   // the elbow low: the forearm comes up from below
        arm.left.upper = WeaponSetup.ArmBox(root, "Arm_Upper", armMat); arm.left.lower = WeaponSetup.ArmBox(root, "Arm_Lower", armMat);
        s.arm = arm;

        // ---- the pack: its origin at the bottom, held 2.5 cm up
        s.pack = new GameObject("Pack").transform; s.pack.SetParent(s.hand, false); s.pack.localPosition = new Vector3(0f, -0.025f, 0f);
        Box("Body", s.pack, new Vector3(0f, 0.031f, 0f), new Vector3(0.055f, 0.062f, 0.022f), packMat);
        Box("Label", s.pack, new Vector3(0f, 0.027f, -0.0112f), new Vector3(0.0552f, 0.024f, 0.0006f), labelMat);
        Box("LabelBack", s.pack, new Vector3(0f, 0.027f, 0.0112f), new Vector3(0.0552f, 0.024f, 0.0006f), labelMat);
        s.packLid = new GameObject("LidHinge").transform; s.packLid.SetParent(s.pack, false); s.packLid.localPosition = new Vector3(0f, 0.062f, 0.011f);
        Box("Lid", s.packLid, new Vector3(0f, 0.0125f, -0.011f), new Vector3(0.0556f, 0.025f, 0.0226f), packMat);
        Box("LidBand", s.packLid, new Vector3(0f, 0.0125f, -0.0223f), new Vector3(0.0557f, 0.006f, 0.0006f), labelMat);
        for (int i = -2; i <= 2; i++)                                  // the filters of the others, under the lid
        {
            if (i == 0) continue;
            Cyl("Filter" + i, s.pack, new Vector3(i * 0.0098f, 0.072f, (i & 1) == 0 ? -0.004f : 0.004f), 0.024f, CigRadius, filterMat);
        }
        float unused;
        s.packCigarette = Cigarette("Cigarette", s.pack, paperMat, filterMat, emberMat, ashMat, out unused).root;
        s.packCigarette.localRotation = Quaternion.Euler(180f, 0f, 0f);  // the filter up, the tip down in the pack
        s.packCigarette.localPosition = new Vector3(0f, 0.084f, 0f);

        // ---- the lighter: its origin at the bottom, held 2 cm up; the lid on a hinge at the right
        s.lighter = new GameObject("Lighter").transform; s.lighter.SetParent(s.hand, false); s.lighter.localPosition = new Vector3(0f, -0.02f, 0f);
        Box("Body", s.lighter, new Vector3(0f, 0.02f, 0f), new Vector3(0.038f, 0.04f, 0.013f), steelMat);
        s.lighterLid = new GameObject("LidHinge").transform; s.lighterLid.SetParent(s.lighter, false); s.lighterLid.localPosition = new Vector3(0.019f, 0.04f, 0f);
        Box("Lid", s.lighterLid, new Vector3(-0.019f, 0.008f, 0f), new Vector3(0.038f, 0.016f, 0.0132f), steelMat);
        Box("Chimney", s.lighter, new Vector3(-0.005f, 0.046f, 0f), new Vector3(0.012f, 0.012f, 0.009f), steelMat);
        var wheel = Cyl("Wheel", s.lighter, new Vector3(0.007f, 0.049f, 0f), 0.003f, 0.004f, darkMat);
        wheel.localRotation = Quaternion.Euler(90f, 0f, 0f); wheel.localPosition = new Vector3(0.007f, 0.049f, -0.0015f);
        var fq = GameObject.CreatePrimitive(PrimitiveType.Quad); fq.name = "Flame"; Object.DestroyImmediate(fq.GetComponent<Collider>());
        fq.transform.SetParent(s.lighter, false); fq.transform.localPosition = new Vector3(-0.005f, 0.067f, 0f); fq.transform.localScale = new Vector3(0.015f, 0.03f, 1f);
        s.flame = fq.GetComponent<MeshRenderer>(); s.flame.sharedMaterial = flameMat; NoShadows(s.flame); s.flame.enabled = false;
        var lg = new GameObject("FlameLight"); lg.transform.SetParent(s.lighter, false); lg.transform.localPosition = new Vector3(-0.005f, 0.07f, 0f);
        s.flameLight = lg.AddComponent<Light>(); s.flameLight.type = LightType.Point; s.flameLight.range = 1.6f; s.flameLight.intensity = 1.2f;
        s.flameLight.color = new Color(1f, 0.68f, 0.32f); s.flameLight.shadows = LightShadows.None; s.flameLight.enabled = false;

        // ---- the smoked cigarette, its glow, the smoke
        var cig = Cigarette("SmokedCigarette", root, paperMat, filterMat, emberMat, ashMat, out unused);
        s.cigarette = cig.root; s.paper = cig.paper; s.ember = cig.ember; s.ash = cig.ash; s.emberRenderer = cig.ember.GetComponent<Renderer>();
        s.filterLength = Filter; s.paperLength = Paper;
        var gq = GameObject.CreatePrimitive(PrimitiveType.Quad); gq.name = "EmberGlow"; Object.DestroyImmediate(gq.GetComponent<Collider>());
        gq.transform.SetParent(root, false); gq.transform.localScale = Vector3.one * 0.015f;
        s.glow = gq.GetComponent<MeshRenderer>(); s.glow.sharedMaterial = glowMat; NoShadows(s.glow); s.glow.enabled = false;
        s.tipSmoke = Smoke("TipSmoke", root, smokeMat, true);
        s.breath = Smoke("BreathSmoke", root, smokeMat, false);

        var audio = root.gameObject.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0f;
        s.audioSource = audio;

        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        s.tipSmoke.gameObject.layer = 0; s.breath.gameObject.layer = 0;  // in the world: the player camera draws the smoke
        s.pack.gameObject.SetActive(false); s.lighter.gameObject.SetActive(false); s.cigarette.gameObject.SetActive(false);
        arm.left.upper.gameObject.SetActive(false); arm.left.lower.gameObject.SetActive(false);
        log.Append("  smoking: pack, lighter, cigarette " + ((Filter + Paper + 0.004f) * 100f).ToString("0.0") + " cm, key C\n");
        return s;
    }

    // ------------------------------------------------------------------ parts
    struct Cig { public Transform root, paper, ember, ash; }

    /// <summary>Filter, paper, ember and ash along +Y from the end of the filter (the origin).</summary>
    static Cig Cigarette(string name, Transform parent, Material paper, Material filter, Material ember, Material ash, out float length)
    {
        var c = new Cig();
        c.root = new GameObject(name).transform; c.root.SetParent(parent, false);
        Cyl("Filter", c.root, new Vector3(0f, Filter * 0.5f, 0f), Filter, CigRadius, filter);
        c.paper = Cyl("Paper", c.root, new Vector3(0f, Filter + Paper * 0.5f, 0f), Paper, CigRadius * 0.99f, paper);
        c.ember = Cyl("Ember", c.root, new Vector3(0f, Filter + Paper + 0.002f, 0f), 0.004f, CigRadius * 0.95f, ember);
        c.ash = Cyl("Ash", c.root, new Vector3(0f, Filter + Paper + 0.004f, 0f), 0.001f, CigRadius * 0.9f, ash);
        c.ash.gameObject.SetActive(false);
        length = Filter + Paper + 0.004f;
        return c;
    }

    static Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; NoShadows(r);
        return go.transform;
    }

    /// <summary>A cylinder along local Y, length metres long, centred on pos.</summary>
    static Transform Cyl(string name, Transform parent, Vector3 pos, float length, float radius, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; NoShadows(r);
        return go.transform;
    }

    static void NoShadows(Renderer r) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.BlendProbes; }

    static ParticleSystem Smoke(string name, Transform parent, Material m, bool wisp)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = true; main.duration = 1f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = wisp ? 60 : 90;
        main.startLifetime = wisp ? new ParticleSystem.MinMaxCurve(1.8f, 2.6f) : new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
        main.startSpeed = wisp ? new ParticleSystem.MinMaxCurve(0.01f, 0.04f) : new ParticleSystem.MinMaxCurve(0f, 0f);
        main.startSize = wisp ? new ParticleSystem.MinMaxCurve(0.012f, 0.02f) : new ParticleSystem.MinMaxCurve(0.045f, 0.075f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        main.gravityModifier = wisp ? -0.012f : -0.006f;
        var em = ps.emission; em.rateOverTime = 0f;
        var shape = ps.shape; shape.enabled = false;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, wisp ? 6f : 7f)));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        float a = wisp ? 0.32f : 0.55f;
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a, 0.08f), new GradientAlphaKey(a * 0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 50f; lv.drag = wisp ? 0.5f : 1.8f;
        var noise = ps.noise; noise.enabled = true; noise.strength = wisp ? 0.06f : 0.1f; noise.frequency = 1.6f; noise.scrollSpeed = 0.4f; noise.quality = ParticleSystemNoiseQuality.Low;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = m; r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.minParticleSize = 0f;
        return ps;
    }

    // ------------------------------------------------------------------ materials and textures
    static Material Mat(string name, Color c, float metallic, float gloss)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_Color", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Glossiness", gloss);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>The flame alpha-blended, not added: an added orange vanishes on a bright background (the white placeholder arms).</summary>
    static Material FlameMat(Texture2D tex)
    {
        string path = MatDir + "/M_LighterFlame.mat";
        var sh = Shader.Find("Unlit/Transparent");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        m.SetTexture("_MainTex", tex);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material SmokeMat(Texture2D tex)
    {
        string path = MatDir + "/M_CigSmoke.mat";
        var sh = Shader.Find("ProjectGame/DustParticle");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        m.SetTexture("_MainTex", tex); m.SetColor("_Color", new Color(0.9f, 0.9f, 0.9f, 1f)); m.SetFloat("_SunShare", 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>An RGBA texture written once (colour and alpha from the functions of u, v), clamped, with mipmaps.</summary>
    static Texture2D Tex(string name, int w, int h, Func<float, float, Color> pixel)
    {
        string path = TexDir + "/" + name + ".png";
        if (!File.Exists(path))
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
        }
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp)
        {
            ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

    /// <summary>A lighter flame: a teardrop, narrow and bluish at the base, white-yellow in the core, orange at the edges and the tip.</summary>
    static Texture2D FlameTex()
    {
        return Tex("T_LighterFlame", 64, 128, (u, v) =>
        {
            float halfW = 0.42f * Mathf.Sin(Mathf.Clamp01(v / 0.95f) * Mathf.PI) * Mathf.Lerp(0.55f, 1f, Smooth(0f, 0.3f, v)) * (1f - 0.35f * v);
            float d = Mathf.Abs(u - 0.5f) / Mathf.Max(0.001f, halfW);
            float body = 1f - Smooth(0.55f, 1f, d);
            float core = (1f - Smooth(0.1f, 0.6f, d)) * (1f - Smooth(0.15f, 0.6f, v));
            Color c = Color.Lerp(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.95f, 0.75f), core);
            c = Color.Lerp(new Color(0.35f, 0.5f, 1f), c, Smooth(0.0f, 0.12f, v));   // blue at the very bottom
            c.a = body * Smooth(0f, 0.06f, v);
            return c;
        });
    }

    static Texture2D GlowTex()
    {
        return Tex("T_SoftGlow", 64, 64, (u, v) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            return new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
        });
    }

    /// <summary>A soft, uneven puff for the cigarette smoke.</summary>
    static Texture2D SmokeTex()
    {
        return Tex("T_CigSmoke", 64, 64, (u, v) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float n = 0f, amp = 0.5f, f = 3f;
            for (int o = 0; o < 4; o++) { n += amp * Mathf.PerlinNoise(11.3f + u * f, 7.7f + v * f); f *= 2f; amp *= 0.5f; }
            n /= 0.9375f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.4f) * Mathf.Lerp(0.35f, 1f, n);
            return new Color(1f, 1f, 1f, a);
        });
    }
}

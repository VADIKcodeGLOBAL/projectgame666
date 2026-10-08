using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cannonballs and their hits, all pooled (nothing is created or destroyed while playing, no garbage per shot):
///  - a ball is a sphere renderer with a smoke trail, flying on the exact ballistic arc (pos = p0 + v0 t + g t²/2); each frame
///    one sphere sweep from the last position to the new one finds what it hits (terrain, rocks, bots, the cannon is ignored);
///    no Rigidbody, all balls are updated in this one component;
///  - a hit damages every bot (and, less, the player) within the blast radius, falling off towards the edge;
///  - and leaves a dust cloud, flying clods, a short flash of light and a dust patch laid over the ground that fades away.
/// Predict() runs the same arc with the same sweeps, so the aiming preview lands exactly where the ball will.
/// </summary>
public class CannonballSystem : MonoBehaviour
{
    [Header("Ball")]
    public float gravity = 9.81f;
    public float ballRadius = 0.16f;
    public float maxFlightTime = 14f;
    public LayerMask hitMask = Physics.AllLayers;
    public Mesh ballMesh;
    public Material ballMaterial, trailMaterial;

    [Header("Blast")]
    public float damage = 160f;
    public float radius = 7f;
    [Tooltip("Damage at the edge of the blast, share of the centre.")] [Range(0f, 1f)] public float edgeDamage = 0.3f;
    [Tooltip("Share of the damage the player takes when caught in the blast.")] [Range(0f, 1f)] public float playerDamageScale = 0.4f;

    [Header("Effects")]
    public Material dustMaterial, decalMaterial;
    public float decalLifetime = 30f;
    public AudioClip[] boomClips;
    public float boomVolume = 1f;

    public static CannonballSystem Instance { get; private set; }
    public int ActiveBalls { get; private set; }
    public int Impacts { get; private set; }
    public Vector3 LastImpact { get; private set; }
    public int LastKills { get; private set; }
    public Vector3 LastLaunchPosition { get; private set; }
    public Vector3 LastLaunchVelocity { get; private set; }

    /// <summary>Position and flight time of the ball fired last, while it flies.</summary>
    public bool TryGetLastBall(out Vector3 position, out float time)
    {
        position = Vector3.zero; time = 0f;
        if (lastBall < 0 || !balls[lastBall].active) return false;
        position = balls[lastBall].pos; time = balls[lastBall].t;
        return true;
    }

    public int DustParticles { get { int n = 0; foreach (var d in dust) if (d != null) n += d.particleCount; return n; } }

    public bool DustPatchNear(Vector3 p, float distance)
    {
        foreach (var d in decals)
            if (d != null && d.go.activeSelf && Vector2.Distance(new Vector2(d.go.transform.position.x, d.go.transform.position.z), new Vector2(p.x, p.z)) < distance) return true;
        return false;
    }

    static readonly Unity.Profiling.ProfilerMarker UpdateMarker = new Unity.Profiling.ProfilerMarker("Cannonballs.Update");
    int lastBall = -1;

    const int Balls = 8, Clouds = 6, Decals = 12, Booms = 4, DecalGrid = 13;

    class Ball { public GameObject go; public TrailRenderer trail; public Vector3 p0, v0, pos; public float t; public bool active; public Transform ignore; }
    class Decal { public GameObject go; public Mesh mesh; public Material mat; public float born = -1e6f; }

    readonly Ball[] balls = new Ball[Balls];
    readonly ParticleSystem[] dust = new ParticleSystem[Clouds], clods = new ParticleSystem[Clouds];
    readonly Light[] flashes = new Light[Clouds];
    readonly float[] flashOff = new float[Clouds];
    readonly Decal[] decals = new Decal[Decals];
    readonly AudioSource[] booms = new AudioSource[Booms];
    int nextCloud, nextDecal, nextBoom;
    readonly Vector3[] decalVerts = new Vector3[DecalGrid * DecalGrid];
    static readonly RaycastHit[] hits = new RaycastHit[16];
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    /// <summary>The system of the scene, made on first use.</summary>
    public static CannonballSystem Get()
    {
        if (Instance == null) Instance = FindFirstObjectByType<CannonballSystem>();
        if (Instance == null) Instance = new GameObject("Cannonballs").AddComponent<CannonballSystem>();
        return Instance;
    }

    void Awake() { Instance = this; Build(); }

    /// <summary>The pools. Also after scripts were reloaded in Play mode: the pools (plain C# objects) come back empty then,
    /// while the objects they held are still under this one.</summary>
    void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        ActiveBalls = 0; lastBall = -1;
        if (ballMesh == null) { var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere); ballMesh = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp); }
        for (int i = 0; i < Balls; i++)
        {
            var go = new GameObject("Ball" + i); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = ballMesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ballMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * ballRadius * 2f;
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = trailMaterial; tr.time = 0.9f; tr.minVertexDistance = 0.6f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.6f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g; tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.receiveShadows = false;
            go.SetActive(false);
            balls[i] = new Ball { go = go, trail = tr };
        }
        for (int i = 0; i < Clouds; i++)
        {
            dust[i] = MakeDust(i); clods[i] = MakeClods(i);
            var l = new GameObject("Flash" + i).AddComponent<Light>(); l.transform.SetParent(transform, false);
            l.type = LightType.Point; l.range = 16f; l.intensity = 0f; l.color = new Color(1f, 0.7f, 0.4f); l.shadows = LightShadows.None; l.enabled = false;
            flashes[i] = l;
        }
        for (int i = 0; i < Decals; i++) decals[i] = MakeDecal(i);
        for (int i = 0; i < Booms; i++)
        {
            var a = new GameObject("Boom" + i).AddComponent<AudioSource>(); a.transform.SetParent(transform, false);
            a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 15f; a.maxDistance = 450f; a.dopplerLevel = 0f;
            booms[i] = a;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (var d in decals) if (d != null) { if (d.mesh != null) Destroy(d.mesh); if (d.mat != null) Destroy(d.mat); }
    }

    // ------------------------------------------------------------------ effects (built once)
    ParticleSystem MakeDust(int i)
    {
        var go = new GameObject("Dust" + i); go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 1f; main.maxParticles = 80;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 6f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.70f, 0.60f, 0.95f), new Color(0.55f, 0.49f, 0.42f, 0.85f));
        main.gravityModifier = -0.03f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 2f;
        var vel = ps.limitVelocityOverLifetime; vel.enabled = true; vel.limit = 1.2f; vel.dampen = 0.12f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.04f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = dustMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.Distance;
        return ps;
    }

    ParticleSystem MakeClods(int i)
    {
        var go = new GameObject("Clods" + i); go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 1f; main.maxParticles = 40;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.startColor = new Color(0.32f, 0.27f, 0.21f, 1f);
        main.gravityModifier = 1.4f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.6f; sh.rotation = new Vector3(-90f, 0f, 0f);
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = dustMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    Decal MakeDecal(int i)
    {
        var go = new GameObject("DustPatch" + i); go.transform.SetParent(transform, false);
        var mesh = new Mesh { name = "DustPatch" + i };
        mesh.MarkDynamic();
        var uv = new Vector2[DecalGrid * DecalGrid]; var tri = new int[(DecalGrid - 1) * (DecalGrid - 1) * 6];
        for (int z = 0; z < DecalGrid; z++)
            for (int x = 0; x < DecalGrid; x++) uv[z * DecalGrid + x] = new Vector2(x / (DecalGrid - 1f), z / (DecalGrid - 1f));
        for (int z = 0, k = 0; z < DecalGrid - 1; z++)
            for (int x = 0; x < DecalGrid - 1; x++)
            {
                int a = z * DecalGrid + x;
                tri[k++] = a; tri[k++] = a + DecalGrid; tri[k++] = a + 1; tri[k++] = a + 1; tri[k++] = a + DecalGrid; tri[k++] = a + DecalGrid + 1;
            }
        mesh.vertices = decalVerts; mesh.uv = uv; mesh.triangles = tri;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = decalMaterial != null ? new Material(decalMaterial) : null;                 // own copy: each patch fades on its own
        mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        go.SetActive(false);
        return new Decal { go = go, mesh = mesh, mat = mat };
    }

    // ------------------------------------------------------------------ flight
    /// <summary>Fires a ball; ignore = the cannon (its colliders are never hit). False if every ball is in the air.</summary>
    public bool Launch(Vector3 position, Vector3 velocity, Transform ignore)
    {
        if (balls[0] == null) Build();                                 // scripts were reloaded in Play mode
        for (int i = 0; i < Balls; i++)
        {
            var b = balls[i];
            if (b.active) continue;
            b.p0 = position; b.v0 = velocity; b.pos = position; b.t = 0f; b.ignore = ignore; b.active = true;
            b.go.transform.position = position;
            b.go.SetActive(true); b.trail.Clear();
            ActiveBalls++; lastBall = i; LastLaunchPosition = position; LastLaunchVelocity = velocity;
            return true;
        }
        return false;
    }

    Vector3 At(Ball b, float t) { return b.p0 + b.v0 * t + Vector3.down * (0.5f * gravity * t * t); }

    /// <summary>Nearest hit of a sphere (or a thin ray) swept from a to b, ignoring triggers, the player and the given cannon.</summary>
    bool SweepSegment(Vector3 a, Vector3 b, Transform ignore, out RaycastHit hit, bool thin = false)
    {
        Vector3 d = b - a; float len = d.magnitude;
        hit = default(RaycastHit);
        if (len < 1e-5f) return false;
        int n = thin ? Physics.RaycastNonAlloc(new Ray(a, d / len), hits, len, hitMask, QueryTriggerInteraction.Ignore)
                     : Physics.SphereCastNonAlloc(a, ballRadius, d / len, hits, len, hitMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; var inv = WeaponInventory.Instance;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.distance <= 0f && h.point == Vector3.zero) continue;
            if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
            if (inv != null && h.collider.transform.IsChildOf(inv.transform)) continue;
            if (h.distance < best) { best = h.distance; hit = h; }
        }
        return best < float.MaxValue;
    }

    void Update()
    {
        if (balls[0] == null) Build();                                 // scripts were reloaded in Play mode
        using (UpdateMarker.Auto()) Tick(Time.deltaTime);
    }

    void Tick(float dt)
    {
        for (int i = 0; i < Balls; i++)
        {
            var b = balls[i];
            if (!b.active) continue;
            float t1 = b.t + dt;
            Vector3 next = At(b, t1);
            RaycastHit h;
            if (SweepSegment(b.pos, next, b.ignore, out h)) { Explode(h.point, h.normal); Retire(b); continue; }
            b.t = t1; b.pos = next; b.go.transform.position = next;
            if (b.t > maxFlightTime || next.y < -80f) Retire(b);
        }
        for (int i = 0; i < Clouds; i++)
            if (flashes[i].enabled)
            {
                float k = (flashOff[i] - Time.time) / 0.15f;
                if (k <= 0f) { flashes[i].enabled = false; continue; }
                flashes[i].intensity = 5f * k;
            }
        for (int i = 0; i < Decals; i++)
        {
            var d = decals[i];
            if (!d.go.activeSelf) continue;
            float age = Time.time - d.born, fade = 1f - Mathf.Clamp01((age - decalLifetime * 0.5f) / (decalLifetime * 0.5f));
            if (fade <= 0f) { d.go.SetActive(false); continue; }
            if (d.mat != null) d.mat.SetFloat(FadeId, fade);
        }
    }

    void Retire(Ball b) { b.active = false; b.go.SetActive(false); ActiveBalls--; }

    /// <summary>
    /// Where a ball fired with this velocity lands: the same arc and the same sweeps as in flight, in steps of step seconds.
    /// path (optional) gets the points of the arc. False if it flies longer than maxFlightTime.
    /// </summary>
    public bool Predict(Vector3 position, Vector3 velocity, Transform ignore, out Vector3 impact, out float flightTime, List<Vector3> path = null, float step = 0.05f, bool thin = false)
    {
        Vector3 prev = position; impact = position; flightTime = 0f;
        if (path != null) { path.Clear(); path.Add(position); }
        for (float t = step; t <= maxFlightTime; t += step)
        {
            Vector3 p = position + velocity * t + Vector3.down * (0.5f * gravity * t * t);
            RaycastHit h;
            if (SweepSegment(prev, p, ignore, out h, thin))
            {
                impact = h.point; flightTime = t - step + step * (h.distance / Mathf.Max(1e-4f, Vector3.Distance(prev, p)));
                if (path != null) path.Add(h.point);
                return true;
            }
            if (path != null) path.Add(p);
            prev = p;
        }
        return false;
    }

    // ------------------------------------------------------------------ the hit
    /// <summary>A hit at a point on the ground, as if a ball came down there (tests, scripted events).</summary>
    public void ExplodeAt(Vector3 at) { Explode(at, Vector3.up); }

    void Explode(Vector3 at, Vector3 normal)
    {
        Impacts++; LastImpact = at;
        int killed = 0;
        for (int i = 0; i < EnemyBot.All.Count; i++)                   // area damage: falls off towards the edge
        {
            var bot = EnemyBot.All[i];
            if (bot.IsDying) continue;
            Vector3 c = bot.body != null ? bot.body.transform.position : bot.transform.position;
            float d = Vector3.Distance(at, c);
            if (d > radius) continue;
            bot.TakeDamage(damage * Mathf.Lerp(1f, edgeDamage, d / radius));
            if (bot.IsDying) killed++;
        }
        LastKills = killed;
        var inv = WeaponInventory.Instance;
        if (inv != null && inv.Health != null)
        {
            float d = Vector3.Distance(at, inv.transform.position + Vector3.up * 0.9f);
            if (d < radius) inv.Health.TakeDamage(damage * playerDamageScale * Mathf.Lerp(1f, edgeDamage, d / radius));
        }

        int k = nextCloud; nextCloud = (nextCloud + 1) % Clouds;
        dust[k].transform.position = at; dust[k].transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(-90f, 0f, 0f);
        dust[k].Emit(48);
        clods[k].transform.position = at + normal * 0.2f; clods[k].transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        clods[k].Emit(22);
        flashes[k].transform.position = at + normal * 1.2f; flashes[k].enabled = true; flashes[k].intensity = 5f; flashOff[k] = Time.time + 0.15f;
        LayPatch(at);

        if (boomClips != null && boomClips.Length > 0)
        {
            var a = booms[nextBoom]; nextBoom = (nextBoom + 1) % Booms;
            a.transform.position = at; a.Stop(); a.clip = boomClips[Random.Range(0, boomClips.Length)];
            a.pitch = Random.Range(0.28f, 0.34f); a.volume = boomVolume * GameSettings.ShotVolume; a.Play();
        }
    }

    /// <summary>A dust patch (the blast radius, a little ragged) laid over the terrain; the oldest patch is reused.</summary>
    void LayPatch(Vector3 at)
    {
        var t = Terrain.activeTerrain;
        var d = decals[nextDecal]; nextDecal = (nextDecal + 1) % Decals;
        float size = radius * 2.1f, half = size * 0.5f;
        float baseY = at.y;
        for (int z = 0; z < DecalGrid; z++)
            for (int x = 0; x < DecalGrid; x++)
            {
                float lx = -half + size * x / (DecalGrid - 1f), lz = -half + size * z / (DecalGrid - 1f);
                Vector3 w = new Vector3(at.x + lx, baseY, at.z + lz);
                float y = t != null ? t.SampleHeight(w) + t.transform.position.y : baseY;
                decalVerts[z * DecalGrid + x] = new Vector3(lx, y - baseY + 0.06f, lz);
            }
        d.mesh.vertices = decalVerts;
        d.mesh.RecalculateNormals(); d.mesh.RecalculateBounds();
        d.go.transform.SetPositionAndRotation(new Vector3(at.x, baseY, at.z), Quaternion.identity);
        if (d.mat != null)
        {
            // one of four mirrorings of the dust texture, so neighbouring hits do not look stamped
            float sx = Random.value < 0.5f ? 1f : -1f, sz = Random.value < 0.5f ? 1f : -1f;
            d.mat.mainTextureScale = new Vector2(sx, sz); d.mat.mainTextureOffset = new Vector2(sx < 0f ? 1f : 0f, sz < 0f ? 1f : 0f);
            d.mat.SetFloat(FadeId, 1f);
        }
        d.born = Time.time; d.go.SetActive(true);
    }
}

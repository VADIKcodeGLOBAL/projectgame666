using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public enum KothRouteStyle { Serpentine, Ridge, Gully, Traverse }

/// <summary>Tools > Level > Hill Map Generator: edit the settings asset and generate the map.</summary>
public class KothMapWindow : EditorWindow
{
    KothMapSettings settings;
    Editor cached;
    Vector2 scroll;

    [MenuItem("Tools/Level/Hill Map Generator")]
    static void Open() { GetWindow<KothMapWindow>("Hill Map"); }

    void OnEnable() { settings = KothMapGenerator.LoadOrCreateSettings(); }

    void OnGUI()
    {
        settings = (KothMapSettings)EditorGUILayout.ObjectField("Settings", settings, typeof(KothMapSettings), false);
        if (settings == null) return;
        scroll = EditorGUILayout.BeginScrollView(scroll);
        Editor.CreateCachedEditor(settings, null, ref cached);
        cached.OnInspectorGUI();
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("Generate map", GUILayout.Height(32))) KothMapGenerator.Generate(settings);
        if (GUILayout.Button("New seed + generate"))
        {
            settings.seed = UnityEngine.Random.Range(1, 99999); EditorUtility.SetDirty(settings);
            KothMapGenerator.Generate(settings);
        }
        if (GUILayout.Button("Rebuild zone only (radius, look)")) KothMapGenerator.RebuildZoneInOpenScene();
        if (!string.IsNullOrEmpty(KothMapGenerator.LastReport)) EditorGUILayout.HelpBox(KothMapGenerator.LastReport, MessageType.Info);
    }
}

/// <summary>
/// Procedural "hold the hill" map for the wave survival mode. Steps (each is one method below):
///  1 PlanLayout      routes (angle + style), rock bands between them, lake
///  2 TraceRoutes     constant-grade trails traced on the analytic hill; spawns moved outward until travel times match
///  3 BuildHeights    valley + hill + rim ridge, trails and camps carved into the heightmap
///  4 PaintTerrain    grass / dry grass / forest floor / rock / dirt from slope, routes and tree density
///  5 PlantTrees      groves in the valley, thinning towards the summit
///  6 ScatterGrass    grass, tall dry grass and flowers as terrain details
///  7 PlaceRocks      cover on the summit and the routes, rock bands, scree, boulders
///  8 Water, Backdrop lake surface and the ring of distant mountains
///  9 Gameplay        the zone on the summit, player with rifle and camera effect, bot prefab, wave game + HUD
/// 10 Lighting        sun, moon and the day / night cycle with its sky
/// </summary>
public static class KothMapGenerator
{
    public const string SettingsPath = "Assets/Data/Levels/KothMapSettings.asset";
    public const string ScenePath = "Assets/Scenes/Levels/Level_KingOfTheHill.unity";
    const string GenDir = "Assets/Art/Environment/KingOfTheHill";
    const string LandTex = "Assets/Art/Environment/Landscape/Textures/";
    const string PrefabDir = "Assets/Prefabs/Level";
    static readonly string[] TreeNames = { "Tree_Variation_04", "Tree_Variation_04_B", "Tree_Variation_04_C", "Tree_Variation_04_D" };
    const float RunSpeed = 5.5f;

    public static string LastReport = "";

    class Route
    {
        public int index; public KothRouteStyle style; public float angle, gradeDeg, sectorDeg, spawnR;
        public Vector2 dir, spawn; public List<Vector2> climb, pts; public float[] h;
        public float length, time, maxGradeDeg, entryAngle;
    }
    class Cliff { public float angle, half, radius, height; }

    static KothMapSettings s;
    static System.Random rnd;
    static float ox, oz, half, baseCentre, lakeShore, waterLevel, ridgeLen;
    static Vector2 lakeC;
    static List<Route> routes;
    static List<Cliff> cliffs;
    static Terrain terrain;
    static TerrainData td;
    // route samples in a hash grid: distance-to-route queries for carving, painting and placement
    static List<Vector2> fPos; static List<float> fH; static Dictionary<long, List<int>> fGrid; const float FCell = 8f;

    // ------------------------------------------------------------------ small helpers
    static float R01() { return (float)rnd.NextDouble(); }
    static float RR(float a, float b) { return a + (b - a) * (float)rnd.NextDouble(); }
    static float Smooth(float a, float b, float x)
    {
        if (Mathf.Approximately(a, b)) return x < a ? 0f : 1f;
        float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t);
    }
    static float Fbm(float x, float z, float scale, int oct, float sx)
    {
        float v = 0f, a = 1f, f = 1f, tot = 0f;
        for (int i = 0; i < oct; i++) { v += a * (Mathf.PerlinNoise(ox + sx + x / scale * f, oz + sx * 0.37f + z / scale * f) * 2f - 1f); tot += a; a *= 0.5f; f *= 2.03f; }
        return v / tot;
    }
    static Vector2 Polar(float deg, float r) { float a = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
    static float AngleOf(Vector2 p) { return Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg; }
    static string F1(float v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

    public static KothMapSettings LoadOrCreateSettings()
    {
        var st = AssetDatabase.LoadAssetAtPath<KothMapSettings>(SettingsPath);
        if (st == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            st = ScriptableObject.CreateInstance<KothMapSettings>();
            AssetDatabase.CreateAsset(st, SettingsPath); AssetDatabase.SaveAssets();
        }
        return st;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Directory.CreateDirectory(path); AssetDatabase.Refresh();
    }

    static T GetAsset<T>(string path, Func<T> create) where T : UnityEngine.Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null) { a = create(); AssetDatabase.CreateAsset(a, path); }
        return a;
    }

    // ================================================================== 1. layout
    static float RadiusVariation(float thRad)
    {
        return 1f + 0.14f * (Mathf.PerlinNoise(ox + 3f + Mathf.Cos(thRad) * 1.2f, oz + 7f + Mathf.Sin(thRad) * 1.2f) * 2f - 1f);
    }

    static void PlanLayout()
    {
        routes = new List<Route>(); cliffs = new List<Cliff>();
        float a0 = RR(0f, 360f);
        var styles = new[] { KothRouteStyle.Serpentine, KothRouteStyle.Ridge, KothRouteStyle.Gully, KothRouteStyle.Traverse };
        for (int i = 0; i < s.routes; i++)
        {
            var r = new Route { index = i, style = styles[i % styles.Length] };
            r.angle = a0 + i * 360f / s.routes + RR(-8f, 8f);
            r.dir = Polar(r.angle, 1f); r.spawnR = s.spawnRadius;
            switch (r.style)
            {
                case KothRouteStyle.Serpentine: r.gradeDeg = 10f; r.sectorDeg = 22f; break;
                case KothRouteStyle.Ridge: r.gradeDeg = 15f; r.sectorDeg = 14f; break;
                case KothRouteStyle.Gully: r.gradeDeg = 27f; r.sectorDeg = 7f; break;
                default: r.gradeDeg = 13f; r.sectorDeg = 30f; break;
            }
            routes.Add(r);
        }
        ridgeLen = Mathf.Min(s.hillRadius * 1.75f, s.spawnRadius - 30f);
        for (int i = 0; i < routes.Count; i++)                      // a rock band in every gap between two routes
        {
            Route a = routes[i], b = routes[(i + 1) % routes.Count];
            float gap = Mathf.Repeat(b.angle - a.angle, 360f);
            float h2 = (gap - a.sectorDeg - b.sectorDeg) * 0.5f - 5f;
            if (h2 < 6f || s.cliffHeight <= 0.1f) continue;
            cliffs.Add(new Cliff { angle = a.angle + a.sectorDeg + 5f + h2, half = Mathf.Min(h2, 26f), radius = s.hillRadius * RR(0.27f, 0.33f), height = s.cliffHeight * RR(0.8f, 1.2f) });
        }
        lakeShore = s.baseLevel - 1f; waterLevel = lakeShore - 1.2f;
        float la = cliffs.Count > 0 ? cliffs[0].angle : a0 + 180f / s.routes;   // the lake lies under the first rock band: a view from the top
        lakeC = Polar(la, s.hillRadius + s.lakeRadius + 32f);
        baseCentre = s.baseLevel + 6f * Fbm(0f, 0f, 150f, 3, 0f);
    }

    // ================================================================== height functions
    /// <summary>Smooth maximum: like Max, but rounds the crease where the two surfaces meet (width k).</summary>
    static float SMax(float a, float b, float k)
    {
        float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
        return Mathf.Max(a, b) + h * h * k * 0.25f;
    }

    static float Profile(float rho, float R)
    {
        if (rho >= R) return 0f;
        float p0 = s.plateauRadius;
        if (rho <= p0) return 1f;
        return 0.5f + 0.5f * Mathf.Cos(Mathf.PI * (rho - p0) / (R - p0));      // one smooth slope from the plateau to the foot, no benches
    }

    /// <summary>Hill above the valley floor, without the small surface noise (routes are traced on this).</summary>
    static float Hill(float x, float z)
    {
        float rho = Mathf.Sqrt(x * x + z * z), th = Mathf.Atan2(z, x), thDeg = th * Mathf.Rad2Deg;
        float R = s.hillRadius * RadiusVariation(th);
        float h = s.hillHeight * Profile(rho, R);
        foreach (var c in cliffs)                                   // shelf above, rock wall below
        {
            float aw = 1f - Smooth(c.half - 5f, c.half, Mathf.Abs(Mathf.DeltaAngle(thDeg, c.angle)));
            if (aw <= 0f) continue;
            float cr = c.radius + 3f * Mathf.Sin(thDeg * 0.21f + c.angle);
            h += aw * c.height * (1f - Smooth(cr - 3f, cr + 3f, rho)) * Smooth(s.plateauRadius + 1f, cr - 6f, rho);
        }
        foreach (var r in routes)
        {
            float along = x * r.dir.x + z * r.dir.y, across = -x * r.dir.y + z * r.dir.x;
            if (r.style == KothRouteStyle.Ridge && along > 0f)      // long spur: a gentle exposed ramp up to the shoulder
            {
                float a0 = 0.47f * s.hillRadius;
                float top = 0.5f * s.hillHeight * Mathf.Clamp01(1f - (along - a0) / (ridgeLen - a0));
                h = SMax(h, top * Mathf.Exp(-across * across / (2f * 18f * 18f)), 5f);
            }
            else if (r.style == KothRouteStyle.Gully && along > 0f) // shallow trough straight up the slope
                h -= 1.0f * Mathf.Exp(-across * across / (2f * 6f * 6f)) * Smooth(s.plateauRadius + 4f, s.plateauRadius + 14f, along) * (1f - Smooth(R * 0.8f, R, along));
        }
        return Mathf.Max(0f, h);
    }

    static Vector2 HillGrad(Vector2 p)
    {
        const float e = 0.75f;
        return new Vector2(Hill(p.x + e, p.y) - Hill(p.x - e, p.y), Hill(p.x, p.y + e) - Hill(p.x, p.y - e)) / (2f * e);
    }

    static float Height(float x, float z, bool detail)
    {
        float rho = Mathf.Sqrt(x * x + z * z);
        float b = s.baseLevel + 6f * Fbm(x, z, 150f, 3, 0f) + 1.2f * Fbm(x, z, 38f, 2, 40f);
        b = Mathf.Lerp(baseCentre, b, Smooth(s.plateauRadius, s.hillRadius * 0.9f, rho));   // the summit is level
        if (s.lake)
        {
            float d = (new Vector2(x, z) - lakeC).magnitude;
            b = Mathf.Lerp(b, lakeShore, 1f - Smooth(s.lakeRadius * 1.1f, s.lakeRadius * 1.7f, d));
            b -= 6.5f * (1f - Smooth(s.lakeRadius * 0.25f, s.lakeRadius, d));
        }
        float hill = Hill(x, z);
        float h = b + hill;
        if (detail) h += 0.25f * Fbm(x, z, 30f, 2, 80f) * Mathf.Clamp01(hill / 6f) * Smooth(s.plateauRadius, s.plateauRadius + 6f, rho);
        float rim = Smooth(s.playRadius, s.playRadius + 95f, rho);
        h += s.rimHeight * rim * (0.8f + 0.35f * Fbm(x, z, 90f, 3, 120f)) + 5f * rim * Fbm(x, z, 24f, 2, 160f);
        return h;
    }

    // ================================================================== 2. routes
    static List<Vector2> Chaikin(List<Vector2> p, int passes)
    {
        for (int k = 0; k < passes; k++)
        {
            var o = new List<Vector2> { p[0] };
            for (int i = 0; i < p.Count - 1; i++) { o.Add(Vector2.Lerp(p[i], p[i + 1], 0.25f)); o.Add(Vector2.Lerp(p[i], p[i + 1], 0.75f)); }
            o.Add(p[p.Count - 1]); p = o;
        }
        return p;
    }

    static List<Vector2> Resample(List<Vector2> p, float step)
    {
        var o = new List<Vector2> { p[0] }; float acc = 0f;
        for (int i = 0; i < p.Count - 1; i++)
        {
            Vector2 a = p[i], b = p[i + 1]; float seg = (b - a).magnitude, pos = 0f;
            if (seg < 1e-5f) continue;
            while (acc + (seg - pos) >= step) { pos += step - acc; acc = 0f; o.Add(Vector2.Lerp(a, b, pos / seg)); }
            acc += seg - pos;
        }
        return o;
    }

    /// <summary>Walks uphill keeping a constant grade: on a slope steeper than the target it cuts across it, turning back at the edge of its sector.</summary>
    static List<Vector2> TraceClimb(Route r, Vector2 start)
    {
        var pts = new List<Vector2> { start };
        Vector2 p = start; float sign = r.index % 2 == 0 ? 1f : -1f;
        float tg = Mathf.Tan(r.gradeDeg * Mathf.Deg2Rad), stopR = s.plateauRadius - 1.5f;
        for (int i = 0; i < 3000; i++)
        {
            float rho = p.magnitude;
            if (rho <= stopR) break;
            Vector2 toC = -p / rho, dir = toC;
            if (rho > s.plateauRadius + 1.5f)
            {
                Vector2 g = HillGrad(p); float slope = g.magnitude;
                Vector2 up = slope > 1e-4f ? g / slope : toC;
                if (Vector2.Dot(up, toC) < 0.35f) up = (up + toC * 1.5f).normalized;
                if (slope > tg)
                {
                    float sinA = tg / slope, cosA = Mathf.Sqrt(1f - sinA * sinA);
                    Vector2 contour = new Vector2(-up.y, up.x) * sign;
                    dir = up * sinA + contour * cosA;
                    float dev = Mathf.DeltaAngle(r.angle, AngleOf(p)), dev2 = Mathf.DeltaAngle(r.angle, AngleOf(p + dir));
                    if (Mathf.Abs(dev2) > r.sectorDeg && Mathf.Abs(dev2) > Mathf.Abs(dev)) { sign = -sign; dir = up * sinA - contour * cosA; }
                }
            }
            p += dir; pts.Add(p);
        }
        pts.Add(p.normalized * s.plateauRadius * 0.45f);
        return pts;
    }

    static float RouteTime(Route r, bool fill)
    {
        float t = 0f, len = 0f, maxG = 0f;
        float prev = Height(r.pts[0].x, r.pts[0].y, false);
        for (int i = 4; i < r.pts.Count; i += 4)
        {
            float d = 0f; for (int k = i - 3; k <= i; k++) d += (r.pts[k] - r.pts[k - 1]).magnitude;
            float hh = Height(r.pts[i].x, r.pts[i].y, false), g = (hh - prev) / Mathf.Max(0.01f, d); prev = hh;
            t += d / (RunSpeed * Mathf.Clamp(1f - 1.6f * Mathf.Max(0f, g), 0.35f, 1f));
            len += d; maxG = Mathf.Max(maxG, Mathf.Abs(g));
        }
        if (fill) { r.time = t; r.length = len; r.maxGradeDeg = Mathf.Atan(maxG) * Mathf.Rad2Deg; }
        return t;
    }

    static void BuildRoute(Route r)
    {
        r.spawn = r.dir * r.spawnR;
        Vector2 foot;
        if (r.style == KothRouteStyle.Ridge) foot = r.dir * (ridgeLen - 6f);
        else
        {
            float rho = s.hillRadius * 1.4f;
            while (rho > s.plateauRadius && Hill(r.dir.x * rho, r.dir.y * rho) < 0.4f) rho -= 1f;
            foot = r.dir * (rho + 2f);
        }
        if (r.climb == null) r.climb = TraceClimb(r, foot);
        Vector2 perp = new Vector2(-r.dir.y, r.dir.x);
        float off = (r.spawnR - foot.magnitude) * 0.14f * (r.index % 2 == 0 ? 1f : -1f);
        var all = new List<Vector2>();
        for (int i = 0; i <= 12; i++)                               // approach from the camp: a slight bow, not a ruler line
        {
            float t = i / 12f;
            all.Add(Vector2.Lerp(r.spawn, foot, t) + perp * off * Mathf.Sin(t * Mathf.PI));
        }
        all.AddRange(r.climb.Skip(1));
        r.pts = Resample(Chaikin(all, 3), 1f);
        Vector2 last = r.pts[r.pts.Count - 1], entry = last;
        foreach (var q in r.pts) if (q.magnitude <= s.plateauRadius) { entry = q; break; }
        r.entryAngle = AngleOf(entry);
    }

    static void TraceRoutes()
    {
        foreach (var r in routes) { BuildRoute(r); RouteTime(r, true); }
        for (int pass = 0; pass < 3; pass++)                        // fast routes start further out, slow ones closer: equal time to the summit
        {
            float target = routes.Average(r => r.time);
            foreach (var r in routes)
            {
                r.spawnR = Mathf.Clamp(r.spawnR + (target - r.time) * RunSpeed, s.hillRadius * 1.3f, s.playRadius - 14f);
                BuildRoute(r); RouteTime(r, true);
            }
        }
        fPos = new List<Vector2>(); fH = new List<float>(); fGrid = new Dictionary<long, List<int>>();
        foreach (var r in routes)
        {
            int n = r.pts.Count; var raw = new float[n]; r.h = new float[n];
            for (int i = 0; i < n; i++) raw[i] = Height(r.pts[i].x, r.pts[i].y, true);
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int c = 0;
                for (int k = Mathf.Max(0, i - 4); k <= Mathf.Min(n - 1, i + 4); k++) { sum += raw[k]; c++; }
                r.h[i] = sum / c;
                int id = fPos.Count; fPos.Add(r.pts[i]); fH.Add(r.h[i]);
                long key = Key(r.pts[i].x, r.pts[i].y); List<int> l;
                if (!fGrid.TryGetValue(key, out l)) { l = new List<int>(); fGrid[key] = l; }
                l.Add(id);
            }
        }
    }

    static long Key(float x, float z) { return ((long)Mathf.FloorToInt(x / FCell) << 32) ^ (uint)Mathf.FloorToInt(z / FCell); }

    /// <summary>Distance to the nearest trail (exact up to FCell) and the trail height there.</summary>
    static float RouteDist(float x, float z, out float h)
    {
        float best = 1e9f; h = 0f;
        int cx = Mathf.FloorToInt(x / FCell), cz = Mathf.FloorToInt(z / FCell);
        for (int ix = cx - 1; ix <= cx + 1; ix++)
            for (int iz = cz - 1; iz <= cz + 1; iz++)
            {
                List<int> l;
                if (!fGrid.TryGetValue(((long)ix << 32) ^ (uint)iz, out l)) continue;
                foreach (int i in l)
                {
                    float dx = fPos[i].x - x, dz = fPos[i].y - z, d = dx * dx + dz * dz;
                    if (d < best) { best = d; h = fH[i]; }
                }
            }
        return best < 1e8f ? Mathf.Sqrt(best) : 1e9f;
    }

    static float SpawnDist(float x, float z, out Route nearest)
    {
        float best = 1e9f; nearest = null;
        foreach (var r in routes) { float d = (new Vector2(x, z) - r.spawn).magnitude; if (d < best) { best = d; nearest = r; } }
        return best;
    }

    // ================================================================== 3. heights
    static void BuildHeights()
    {
        int res = s.heightmapResolution; float cell = s.mapSize / (res - 1);
        var spawnH = routes.ToDictionary(r => r, r => Height(r.spawn.x, r.spawn.y, false));
        var H = new float[res, res];
        for (int zi = 0; zi < res; zi++)
        {
            float z = -half + zi * cell;
            for (int xi = 0; xi < res; xi++)
            {
                float x = -half + xi * cell, h = Height(x, z, true), hr;
                float d = RouteDist(x, z, out hr);
                if (d < s.pathHalfWidth + 4f) h = Mathf.Lerp(h, hr, 1f - Smooth(s.pathHalfWidth, s.pathHalfWidth + 3.5f, d));   // trail bench
                Route sr; float ds = SpawnDist(x, z, out sr);
                if (ds < 15f) h = Mathf.Lerp(h, spawnH[sr], 1f - Smooth(7f, 15f, ds));                                         // level camp
                H[zi, xi] = Mathf.Clamp01(h / s.maxHeight);
            }
        }
        // smooth the hill: a few passes of a 3x3 blur that fade out below its foot
        var tmp = new float[res, res];
        for (int pass = 0; pass < 4; pass++)
        {
            Array.Copy(H, tmp, H.Length);
            for (int zi = 1; zi < res - 1; zi++)
                for (int xi = 1; xi < res - 1; xi++)
                {
                    float x = -half + xi * cell, z = -half + zi * cell;
                    float w = 1f - Smooth(s.hillRadius * 1.1f, s.hillRadius * 1.35f, Mathf.Sqrt(x * x + z * z));
                    if (w <= 0f) continue;
                    float avg = (4f * tmp[zi, xi] + 2f * (tmp[zi - 1, xi] + tmp[zi + 1, xi] + tmp[zi, xi - 1] + tmp[zi, xi + 1])
                                 + tmp[zi - 1, xi - 1] + tmp[zi - 1, xi + 1] + tmp[zi + 1, xi - 1] + tmp[zi + 1, xi + 1]) / 16f;
                    H[zi, xi] = Mathf.Lerp(tmp[zi, xi], avg, w);
                }
        }
        EnsureFolder(GenDir);
        td = GetAsset(GenDir + "/KotH_TerrainData.asset", () => new TerrainData());
        td.heightmapResolution = res;
        td.size = new Vector3(s.mapSize, s.maxHeight, s.mapSize);
        td.SetHeights(0, 0, H);
        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "Terrain"; go.transform.position = new Vector3(-half, 0f, -half);
        terrain = go.GetComponent<Terrain>();
        terrain.heightmapPixelError = 4f; terrain.basemapDistance = 500f; terrain.drawInstanced = true;
        terrain.detailObjectDistance = 120f; terrain.detailObjectDensity = 1f; terrain.treeDistance = 1200f; terrain.treeBillboardDistance = 1200f;
    }

    static bool InLake(float x, float z, float y, float margin)
    {
        return s.lake && y < waterLevel + margin && (new Vector2(x, z) - lakeC).magnitude < s.lakeRadius * 1.25f;
    }

    static float GroundY(float x, float z) { return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y; }
    static float Steep(float x, float z) { return td.GetSteepness((x + half) / s.mapSize, (z + half) / s.mapSize); }
    static Vector3 Normal(float x, float z) { return td.GetInterpolatedNormal((x + half) / s.mapSize, (z + half) / s.mapSize); }

    // ================================================================== procedural textures
    static Texture2D SaveTexture(string path, Func<Texture2D> make, Action<TextureImporter> cfg)
    {
        var t = make(); File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null) { cfg(ti); ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D LoadPixels(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    static Texture2D Recolor(string src, Func<Color, float, float, Color> f)
    {
        var t = LoadPixels(src); var px = t.GetPixels(); int w = t.width, h = t.height;
        for (int i = 0; i < px.Length; i++) px[i] = f(px[i], (i % w) / (float)w, (i / w) / (float)h);
        var o = new Texture2D(w, h, TextureFormat.RGB24, false); o.SetPixels(px); o.Apply();
        UnityEngine.Object.DestroyImmediate(t);
        return o;
    }

    static float TileNoise(float x, float y, int n, float scale, int oct, float seed)
    {
        Func<float, float, float> f = (u, v) =>
        {
            float val = 0f, a = 0.5f, fr = 1f / scale;
            for (int o = 0; o < oct; o++) { val += a * Mathf.PerlinNoise(u * fr + seed + 11.3f * o, v * fr + seed * 0.7f + 5.1f * o); a *= 0.5f; fr *= 2.1f; }
            return val;
        };
        return (f(x, y) * (n - x) * (n - y) + f(x - n, y) * x * (n - y) + f(x - n, y - n) * x * y + f(x, y - n) * (n - x) * y) / (n * (float)n);
    }

    static float[,] RockHeight(int n)
    {
        var hm = new float[n, n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float a = TileNoise(x, y, n, 110f, 5, 3f);
                float c = 1f - Mathf.Abs(TileNoise(x, y, n, 60f, 4, 41f) * 2f - 0.95f) * 2.2f;      // ridged: cracks
                hm[y, x] = Mathf.Clamp01(a * 1.3f - 0.1f) * 0.7f + Mathf.Clamp01(c) * 0.3f;
            }
        return hm;
    }

    static Texture2D MakeRockAlbedo()
    {
        const int n = 512; var hm = RockHeight(n); var t = new Texture2D(n, n, TextureFormat.RGB24, false);
        Color dark = new Color(0.27f, 0.26f, 0.25f), light = new Color(0.60f, 0.58f, 0.54f), lichen = new Color(0.42f, 0.45f, 0.30f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Color c = Color.Lerp(dark, light, Mathf.Clamp01(hm[y, x] * 1.5f - 0.15f));
                c = Color.Lerp(c, lichen, Mathf.Clamp01((TileNoise(x, y, n, 45f, 3, 77f) - 0.56f) * 6f) * 0.45f);
                t.SetPixel(x, y, c);
            }
        t.Apply(); return t;
    }

    static Texture2D MakeRockNormal()
    {
        const int n = 512; var hm = RockHeight(n); var t = new Texture2D(n, n, TextureFormat.RGB24, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = hm[y, (x + 1) % n] - hm[y, (x + n - 1) % n], dy = hm[(y + 1) % n, x] - hm[(y + n - 1) % n, x];
                Vector3 nn = new Vector3(-dx * 9f, -dy * 9f, 1f).normalized;
                t.SetPixel(x, y, new Color(nn.x * 0.5f + 0.5f, nn.y * 0.5f + 0.5f, nn.z * 0.5f + 0.5f));
            }
        t.Apply(); return t;
    }

    static void Blade(Color[] px, int n, Vector2 p0, Vector2 p1, Vector2 p2, float w0, float w1, Color c0, Color c1)
    {
        for (int i = 0; i <= 220; i++)
        {
            float t = i / 220f, u = 1f - t;
            Vector2 p = u * u * p0 + 2f * u * t * p1 + t * t * p2; float rad = Mathf.Lerp(w0, w1, t) * 0.5f;
            Color c = Color.Lerp(c0, c1, t); c.a = 1f;
            for (int y = Mathf.Max(0, (int)(p.y - rad - 1)); y <= Mathf.Min(n - 1, (int)(p.y + rad + 1)); y++)
                for (int x = Mathf.Max(0, (int)(p.x - rad - 1)); x <= Mathf.Min(n - 1, (int)(p.x + rad + 1)); x++)
                    if ((new Vector2(x + 0.5f, y + 0.5f) - p).sqrMagnitude <= rad * rad + 0.3f) px[y * n + x] = c;
        }
    }

    static void Disc(Color[] px, int n, Vector2 p, float rad, Color c)
    {
        c.a = 1f;
        for (int y = Mathf.Max(0, (int)(p.y - rad - 1)); y <= Mathf.Min(n - 1, (int)(p.y + rad + 1)); y++)
            for (int x = Mathf.Max(0, (int)(p.x - rad - 1)); x <= Mathf.Min(n - 1, (int)(p.x + rad + 1)); x++)
                if ((new Vector2(x + 0.5f, y + 0.5f) - p).sqrMagnitude <= rad * rad) px[y * n + x] = c;
    }

    /// <summary>kind 0 = green tuft, 1 = tall dry grass with seed heads, 2 = flowers.</summary>
    static Texture2D MakeGrass(int kind)
    {
        const int n = 256; var r = new System.Random(100 + kind); Func<float> u = () => (float)r.NextDouble();
        Color fill = kind == 1 ? new Color(0.55f, 0.50f, 0.28f, 0f) : new Color(0.26f, 0.33f, 0.12f, 0f);   // colour under the alpha edge
        var px = new Color[n * n]; for (int i = 0; i < px.Length; i++) px[i] = fill;
        int blades = kind == 0 ? 22 : kind == 1 ? 15 : 9;
        for (int b = 0; b < blades; b++)
        {
            float bx = n * (0.22f + 0.56f * u()), tipY = n * (kind == 1 ? 0.62f + 0.36f * u() : 0.45f + 0.5f * u());
            var p0 = new Vector2(bx, 0f); var p2 = new Vector2(bx + (u() - 0.5f) * n * 0.55f, tipY);
            var p1 = new Vector2(bx + (u() - 0.5f) * n * 0.1f, tipY * 0.6f);
            Color c0, c1;
            if (kind == 1) { float k = 0.85f + 0.3f * u(); c0 = new Color(0.36f, 0.34f, 0.17f) * k; c1 = new Color(0.66f, 0.60f, 0.36f) * k; }
            else { float k = 0.8f + 0.4f * u(); c0 = new Color(0.15f, 0.21f, 0.07f) * k; c1 = new Color(0.36f, 0.45f, 0.17f) * k; }
            Blade(px, n, p0, p1, p2, kind == 2 ? 3.5f : 8f, kind == 2 ? 2.2f : 1.4f, c0, c1);
            if (kind == 1 && b % 2 == 0)
                for (int k = 0; k < 6; k++) Disc(px, n, p2 - new Vector2((u() - 0.5f) * 5f, k * 5f), 3.2f, new Color(0.66f, 0.56f, 0.32f));
            if (kind == 2)
            {
                Color petal = b % 3 == 0 ? new Color(0.98f, 0.86f, 0.25f) : b % 3 == 1 ? new Color(0.96f, 0.96f, 0.98f) : new Color(0.72f, 0.50f, 0.92f);
                for (int k = 0; k < 6; k++) { float a = k * Mathf.PI / 3f; Disc(px, n, p2 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 7f, 5.5f, petal); }
                Disc(px, n, p2, 4.5f, new Color(0.95f, 0.70f, 0.15f));
            }
        }
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false); t.SetPixels(px); t.Apply(); return t;
    }

    // ================================================================== 4. paint
    static float Grove(float x, float z) { return Smooth(-0.08f, 0.32f, Fbm(x, z, 75f, 3, 200f)); }

    static float TreeDensity(float x, float z)
    {
        float rho = Mathf.Sqrt(x * x + z * z);
        if (rho < s.plateauRadius + 5f) return 0f;
        if (s.lake && (new Vector2(x, z) - lakeC).magnitude < s.lakeRadius * 1.15f) return 0f;
        float d = Grove(x, z) * s.forestDensity * 1.7f;
        float t = rho / s.hillRadius;
        d *= Smooth(1.0f, 1.3f, t);                                // the hill itself is open: clear lines of sight from the summit
        d = Mathf.Max(d, 0.78f * Smooth(s.playRadius - 8f, s.playRadius + 22f, rho));   // the rim ridge is wooded
        return Mathf.Clamp01(d);
    }

    static float Meadow(float x, float z) { return Smooth(-0.1f, 0.35f, Fbm(x, z, 48f, 2, 300f)); }

    static void PaintTerrain()
    {
        Action<TextureImporter> tile = ti => { ti.wrapMode = TextureWrapMode.Repeat; ti.anisoLevel = 8; };
        var grassC = AssetDatabase.LoadAssetAtPath<Texture2D>(LandTex + "Poliigon_GrassPatchyGround_4585_BaseColor.jpg");
        var grassN = AssetDatabase.LoadAssetAtPath<Texture2D>(LandTex + "Poliigon_GrassPatchyGround_4585_Normal.png");
        string gsrc = LandTex + "Poliigon_GrassPatchyGround_4585_BaseColor.jpg";
        var dry = SaveTexture(GenDir + "/T_Grass_Dry.png", () => Recolor(gsrc, (c, u, v) =>
        {
            float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return new Color(Mathf.Lerp(c.r, l, 0.5f) * 1.25f + 0.05f, Mathf.Lerp(c.g, l, 0.5f) * 1.10f + 0.03f, Mathf.Lerp(c.b, l, 0.5f) * 0.75f);
        }), tile);
        var floor = SaveTexture(GenDir + "/T_Forest_Floor.png", () => Recolor(gsrc, (c, u, v) =>
        {
            return Color.Lerp(c * 0.66f, new Color(0.24f, 0.21f, 0.12f), 0.42f);
        }), tile);
        var rockC = SaveTexture(GenDir + "/T_Rock.png", MakeRockAlbedo, tile);
        var rockN = SaveTexture(GenDir + "/T_Rock_Normal.png", MakeRockNormal, ti => { ti.textureType = TextureImporterType.NormalMap; ti.wrapMode = TextureWrapMode.Repeat; });
        var dirt = AssetDatabase.LoadAssetAtPath<Texture2D>(LandTex + "Trail_Dirt.png");

        Func<string, Texture2D, Texture2D, float, TerrainLayer> layer = (name, d, nm, size) =>
        {
            var l = GetAsset(GenDir + "/TL_" + name + ".terrainlayer", () => new TerrainLayer());
            l.diffuseTexture = d; l.normalMapTexture = nm; l.tileSize = new Vector2(size, size); l.smoothness = 0f; l.metallic = 0f; l.specular = Color.black;
            EditorUtility.SetDirty(l); return l;
        };
        td.terrainLayers = new[] { layer("Grass", grassC, grassN, 7f), layer("GrassDry", dry, grassN, 8f), layer("ForestFloor", floor, grassN, 6f),
                                   layer("Rock", rockC, rockN, 9f), layer("Dirt", dirt != null ? dirt : grassC, null, 4f) };

        int n = 512; td.alphamapResolution = n; var a = new float[n, n, 5];
        for (int zi = 0; zi < n; zi++)
            for (int xi = 0; xi < n; xi++)
            {
                float x = -half + (xi + 0.5f) / n * s.mapSize, z = -half + (zi + 0.5f) / n * s.mapSize, hr;
                float rho = Mathf.Sqrt(x * x + z * z), slope = Steep(x, z), y = td.GetInterpolatedHeight((x + half) / s.mapSize, (z + half) / s.mapSize);
                float nz = Fbm(x, z, 9f, 2, 500f);
                float rock = Smooth(33f, 45f, slope + nz * 5f);
                Route sr; float ds = SpawnDist(x, z, out sr);
                float dirtW = Mathf.Max((1f - Smooth(s.pathHalfWidth * 0.7f, s.pathHalfWidth + 1.6f, RouteDist(x, z, out hr) + nz * 0.6f)) * 0.95f,
                              Mathf.Max((1f - Smooth(3f, 8f, rho + nz * 2f)) * 0.75f, (1f - Smooth(4f, 9f, ds + nz * 2f)) * 0.8f));
                if (InLake(x, z, y, 0.6f)) dirtW = Mathf.Max(dirtW, 1f - Smooth(waterLevel + 0.1f, waterLevel + 0.6f, y));   // shore
                float forest = Mathf.Clamp01(TreeDensity(x, z) * 1.2f - 0.15f) * (0.62f + 0.2f * nz);
                float hillMask = 1f - Smooth(s.hillRadius * 0.6f, s.hillRadius * 1.15f, rho);
                float dryW = Mathf.Clamp01(Meadow(x, z) * (0.25f + 0.6f * hillMask) + 0.12f * nz);
                float w0 = 1f, w1 = dryW; w0 *= 1f - dryW;
                float w2 = forest; w0 *= 1f - forest; w1 *= 1f - forest;
                float w3 = rock; w0 *= 1f - rock; w1 *= 1f - rock; w2 *= 1f - rock;
                float w4 = dirtW; w0 *= 1f - dirtW; w1 *= 1f - dirtW; w2 *= 1f - dirtW; w3 *= 1f - dirtW;
                a[zi, xi, 0] = w0; a[zi, xi, 1] = w1; a[zi, xi, 2] = w2; a[zi, xi, 3] = w3; a[zi, xi, 4] = w4;
            }
        td.SetAlphamaps(0, 0, a);
    }

    // ================================================================== 5. trees
    static void PlantTrees()
    {
        var protos = new List<TreePrototype>();
        foreach (var nme in TreeNames)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + nme + ".prefab");
            if (p != null) protos.Add(new TreePrototype { prefab = p, bendFactor = 0f });
        }
        td.treePrototypes = protos.ToArray();
        var list = new List<TreeInstance>(); var occ = new Dictionary<long, List<Vector3>>();
        if (protos.Count == 0) { td.SetTreeInstances(list.ToArray(), true); EditorAutomation.Log("  no tree prefabs found in " + PrefabDir); return; }
        const float cell = 14f;
        Func<float, float, float, bool> free = (x, z, r) =>
        {
            int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
            for (int ix = cx - 1; ix <= cx + 1; ix++)
                for (int iz = cz - 1; iz <= cz + 1; iz++)
                {
                    List<Vector3> l; if (!occ.TryGetValue(((long)ix << 32) ^ (uint)iz, out l)) continue;
                    foreach (var o in l) { float m = Mathf.Min(r, o.z); if ((o.x - x) * (o.x - x) + (o.y - z) * (o.y - z) < m * m) return false; }
                }
            return true;
        };
        Action<float, float, float, float> add = (x, z, r, scale) =>
        {
            long key = ((long)Mathf.FloorToInt(x / cell) << 32) ^ (uint)Mathf.FloorToInt(z / cell); List<Vector3> l;
            if (!occ.TryGetValue(key, out l)) { l = new List<Vector3>(); occ[key] = l; }
            l.Add(new Vector3(x, z, r));
            list.Add(new TreeInstance
            {
                position = new Vector3((x + half) / s.mapSize, 0f, (z + half) / s.mapSize), prototypeIndex = rnd.Next(protos.Count),
                widthScale = scale * RR(0.92f, 1.08f), heightScale = scale, rotation = RR(0f, Mathf.PI * 2f), color = Color.white, lightmapColor = Color.white
            });
        };
        foreach (var c in cliffs)                                   // landmark trees on the edge of the summit, above the rock bands
        {
            Vector2 p = Polar(c.angle + RR(-6f, 6f), s.plateauRadius + 3.5f);
            add(p.x, p.y, 6f, RR(1.25f, 1.5f));
        }
        float lim = half - 6f; int canopy = 0;
        for (int i = 0; i < 220000 && canopy < s.treeBudget; i++)
        {
            float x = RR(-lim, lim), z = RR(-lim, lim), hr; float dn = TreeDensity(x, z);
            if (x * x + z * z < s.hillRadius * s.hillRadius) continue;                             // the hill stays open
            if (s.lake && (new Vector2(x, z) - lakeC).magnitude < s.lakeRadius * 1.15f) continue;
            if (R01() > dn * 1.2f + 0.02f) continue;                                                // a few solitary trees in the open
            if (RouteDist(x, z, out hr) < 4.2f) continue;
            Route sr; if (SpawnDist(x, z, out sr) < 11f) continue;
            float rho = Mathf.Sqrt(x * x + z * z);
            if (Steep(x, z) > (rho > s.playRadius ? 44f : 29f)) continue;
            if (InLake(x, z, GroundY(x, z), 0.5f)) continue;
            float r = Mathf.Lerp(13f, 4.8f, dn);
            if (!free(x, z, r)) continue;
            float onHill = 1f - Smooth(s.hillRadius * 0.7f, s.hillRadius * 1.1f, rho);
            float sc = R01() < 0.08f ? RR(1.4f, 1.7f) : RR(0.85f, 1.3f);
            sc *= Mathf.Lerp(1f, 0.78f, onHill);
            add(x, z, r, sc); canopy++;
            if (R01() < 0.35f)                                      // a sapling or two beside it
            {
                Vector2 q = new Vector2(x, z) + Polar(RR(0f, 360f), RR(2.2f, 4.5f));
                if (RouteDist(q.x, q.y, out hr) > 3.5f && Steep(q.x, q.y) < 30f) add(q.x, q.y, 1.5f, RR(0.28f, 0.48f));
            }
        }
        td.SetTreeInstances(list.ToArray(), true);
        EditorAutomation.Log("  trees: " + canopy + " canopy, " + (list.Count - canopy) + " small");
    }

    // ================================================================== 6. grass
    static void ScatterGrass()
    {
        Action<TextureImporter> cfg = ti => { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.isReadable = true; ti.mipmapEnabled = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.4f; };
        var g0 = SaveTexture(GenDir + "/T_Grass_Tuft.png", () => MakeGrass(0), cfg);
        var g1 = SaveTexture(GenDir + "/T_Grass_Tall.png", () => MakeGrass(1), cfg);
        var g2 = SaveTexture(GenDir + "/T_Flowers.png", () => MakeGrass(2), cfg);
        Func<Texture2D, float, float, float, float, DetailPrototype> proto = (t, w0, w1, h0, h1) => new DetailPrototype
        {
            prototypeTexture = t, usePrototypeMesh = false, renderMode = DetailRenderMode.Grass, minWidth = w0, maxWidth = w1, minHeight = h0, maxHeight = h1,
            healthyColor = new Color(1f, 1f, 1f), dryColor = new Color(0.88f, 0.86f, 0.74f), noiseSpread = 0.2f, positionJitter = 1f
        };
        const int n = 512;
        td.SetDetailResolution(n, 16);
        td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        td.detailPrototypes = new[] { proto(g0, 0.55f, 0.95f, 0.22f, 0.45f), proto(g1, 0.5f, 0.85f, 0.5f, 0.85f), proto(g2, 0.4f, 0.65f, 0.28f, 0.45f) };
        td.wavingGrassStrength = 0.35f; td.wavingGrassSpeed = 0.35f; td.wavingGrassAmount = 0.25f; td.wavingGrassTint = new Color(0.78f, 0.80f, 0.70f);
        var m0 = new int[n, n]; var m1 = new int[n, n]; var m2 = new int[n, n];
        for (int zi = 0; zi < n; zi++)
            for (int xi = 0; xi < n; xi++)
            {
                float x = -half + (xi + 0.5f) / n * s.mapSize, z = -half + (zi + 0.5f) / n * s.mapSize, hr;
                float rho = Mathf.Sqrt(x * x + z * z);
                if (rho > s.playRadius + 60f || Steep(x, z) > 36f) continue;
                if (RouteDist(x, z, out hr) < s.pathHalfWidth + 0.4f || rho < 5f) continue;
                Route sr; if (SpawnDist(x, z, out sr) < 6f) continue;
                if (InLake(x, z, td.GetInterpolatedHeight((x + half) / s.mapSize, (z + half) / s.mapSize), 0.35f)) continue;
                float forest = TreeDensity(x, z), meadow = Meadow(x, z);
                float hillMask = 1f - Smooth(s.hillRadius * 0.6f, s.hillRadius * 1.15f, rho);
                float baseD = s.grassDensity * (1f - 0.65f * forest) * (0.55f + 0.6f * Mathf.PerlinNoise(ox + x * 0.11f, oz + z * 0.11f));
                float dryD = meadow * (0.25f + 0.6f * hillMask);
                float flower = Smooth(0.15f, 0.5f, Fbm(x, z, 26f, 2, 700f)) * (0.25f + 0.75f * hillMask) * (1f - forest);
                m0[zi, xi] = Mathf.FloorToInt(baseD * (1f - 0.6f * dryD) * 5.5f + R01());
                m1[zi, xi] = Mathf.FloorToInt(baseD * dryD * 2.2f + R01() * 0.9f);
                m2[zi, xi] = Mathf.FloorToInt(baseD * flower * 1.1f + R01() * 0.9f);
            }
        td.SetDetailLayer(0, 0, 0, m0); td.SetDetailLayer(0, 0, 1, m1); td.SetDetailLayer(0, 0, 2, m2);
    }

    // ================================================================== 7. rocks
    static Mesh MakeRockMesh(int seed)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new List<Vector3> { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
                                    new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
        var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        for (int it = 0; it < 2; it++)
        {
            var cache = new Dictionary<long, int>(); var nf = new List<int>();
            Func<int, int, int> mid = (a, b) =>
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; int id;
                if (cache.TryGetValue(key, out id)) return id;
                v.Add(((v[a] + v[b]) * 0.5f).normalized); cache[key] = v.Count - 1; return v.Count - 1;
            };
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = mid(a, b), bc = mid(b, c), ca = mid(c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = nf;
        }
        float o = seed * 13.7f;
        Vector3 squash = new Vector3(1f + 0.25f * Mathf.Sin(o), 0.72f + 0.12f * Mathf.Cos(o * 1.3f), 1f + 0.25f * Mathf.Cos(o * 0.7f));
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 p = v[i];
            float nse = (Mathf.PerlinNoise(p.x * 1.3f + o, p.y * 1.3f + 5f) + Mathf.PerlinNoise(p.y * 1.3f + o, p.z * 1.3f + 9f) + Mathf.PerlinNoise(p.z * 1.3f + o, p.x * 1.3f + 2f)) / 3f;
            float fine = Mathf.PerlinNoise(p.x * 3.7f + o, p.z * 3.7f + p.y * 2.1f);
            v[i] = Vector3.Scale(p * (0.72f + 0.55f * nse + 0.12f * fine), squash);
        }
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        for (int i = 0; i < f.Count; i += 3)                        // flat shaded, box-projected UVs
        {
            Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]], nn = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(nn, a + b + c) < 0f) { var tmp = b; b = c; c = tmp; nn = -nn; }
            foreach (var p in new[] { a, b, c })
            {
                verts.Add(p); norms.Add(nn);
                Vector3 an = new Vector3(Mathf.Abs(nn.x), Mathf.Abs(nn.y), Mathf.Abs(nn.z));
                uvs.Add((an.y >= an.x && an.y >= an.z ? new Vector2(p.x, p.z) : an.x >= an.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y)) * 0.45f);
                tris.Add(verts.Count - 1);
            }
        }
        var m = GetAsset(GenDir + "/Rock_0" + seed + ".asset", () => new Mesh());
        m.Clear(); m.name = "Rock_0" + seed;
        m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        m.RecalculateBounds(); m.RecalculateTangents(); EditorUtility.SetDirty(m);
        return m;
    }

    static void PlaceRocks(Transform parent)
    {
        var rockMat = GetAsset(GenDir + "/M_Rock.mat", () => new Material(Shader.Find("Standard")));
        rockMat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(GenDir + "/T_Rock.png"));
        var rn = AssetDatabase.LoadAssetAtPath<Texture2D>(GenDir + "/T_Rock_Normal.png");
        if (rn != null) { rockMat.SetTexture("_BumpMap", rn); rockMat.SetFloat("_BumpScale", 1.2f); rockMat.EnableKeyword("_NORMALMAP"); }
        rockMat.SetColor("_Color", new Color(0.86f, 0.85f, 0.83f)); rockMat.SetFloat("_Glossiness", 0.12f); rockMat.SetFloat("_Metallic", 0f);
        rockMat.enableInstancing = true; EditorUtility.SetDirty(rockMat);
        var prefabs = new List<GameObject>();
        for (int i = 1; i <= 5; i++)
        {
            var mesh = MakeRockMesh(i);
            var go = new GameObject("Rock_0" + i);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            prefabs.Add(PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/Rock_0" + i + ".prefab"));
            UnityEngine.Object.DestroyImmediate(go);
        }
        int count = 0;
        Action<Transform, float, float, float, float> put = (grp, x, z, scale, sink) =>
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[rnd.Next(prefabs.Count)], grp);
            Vector3 nrm = Vector3.Slerp(Vector3.up, Normal(x, z), 0.6f);
            go.transform.position = new Vector3(x, GroundY(x, z) - sink * scale, z);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, nrm) * Quaternion.Euler(RR(-12f, 12f), RR(0f, 360f), RR(-12f, 12f));
            go.transform.localScale = new Vector3(scale * RR(0.85f, 1.2f), scale * RR(0.8f, 1.15f), scale * RR(0.85f, 1.2f));
            go.isStatic = true; count++;
        };
        Func<string, Transform> group = name => { var g = new GameObject(name).transform; g.SetParent(parent, false); return g; };
        float hr;

        var gTop = group("Summit_Cover");                           // a few low stones on the rim of the plateau: cover you can shoot over
        for (int k = 0; k < 7; k++)
        {
            float a = k * 360f / 7f + RR(-9f, 9f);
            if (routes.Any(r => Mathf.Abs(Mathf.DeltaAngle(a, r.entryAngle)) < 20f)) continue;
            Vector2 p = Polar(a, s.plateauRadius + RR(-0.5f, 1.0f)); put(gTop, p.x, p.y, RR(0.8f, 1.15f), 0.35f);
        }

        var gCliff = group("Rock_Bands");
        foreach (var c in cliffs)
        {
            float step = 3.6f / c.radius * Mathf.Rad2Deg;
            for (float a = c.angle - c.half + 2f; a <= c.angle + c.half - 2f; a += step)
            {
                Vector2 p = Polar(a + RR(-0.5f, 0.5f), c.radius + RR(0.6f, 1.8f)); put(gCliff, p.x, p.y, RR(2.8f, 4.2f), 0.40f);
                Vector2 p2 = Polar(a + step * 0.5f + RR(-0.5f, 0.5f), c.radius + RR(-2.2f, -0.8f)); put(gCliff, p2.x, p2.y, RR(2.2f, 3.4f), 0.45f);
                if (R01() < 0.7f) { Vector2 q = Polar(a + RR(-2f, 2f), c.radius + RR(3.5f, 8f)); put(gCliff, q.x, q.y, RR(0.8f, 1.8f), 0.3f); }   // scree
            }
        }

        var gRoute = group("Route_Cover");
        foreach (var r in routes)
        {
            float gap = r.style == KothRouteStyle.Gully ? 7f : r.style == KothRouteStyle.Ridge ? 13f : 17f; int side = 1;
            for (int i = 12; i < r.pts.Count - 8; i += Mathf.RoundToInt(gap * RR(0.8f, 1.25f)))
            {
                if (r.pts[i].magnitude < s.hillRadius * 1.05f || R01() < 0.5f) continue;              // nothing on the open hill
                Vector2 tan = (r.pts[i + 2] - r.pts[i - 2]).normalized, nrm = new Vector2(-tan.y, tan.x) * side; side = -side;
                Vector2 p = r.pts[i] + nrm * RR(3.2f, 5f);
                if (RouteDist(p.x, p.y, out hr) < 2.8f || p.magnitude < s.plateauRadius + 3f) continue;
                put(gRoute, p.x, p.y, RR(1.1f, 1.9f), 0.3f);
            }
        }

        var gTerrace = group("Shoulder_Rocks");
        for (float a = 0f; a < 360f && s.cliffHeight > 0.1f; a += 24f)      // only with rock bands; the open hill has none
        {
            Vector2 c = Polar(a + RR(-6f, 6f), s.hillRadius * RR(0.44f, 0.52f));
            if (RouteDist(c.x, c.y, out hr) < 6f) continue;
            int nn = rnd.Next(1, 4);
            for (int k = 0; k < nn; k++) { Vector2 p = c + Polar(RR(0f, 360f), RR(0f, 3.5f)); put(gTerrace, p.x, p.y, RR(0.9f, 1.8f), 0.3f); }
        }

        var gCamp = group("Camp_Rocks");
        foreach (var r in routes)
            for (int k = 0; k < 3; k++)
            {
                Vector2 p = r.spawn + Polar(r.angle + 90f + k * 70f + RR(-15f, 15f), RR(6.5f, 9f));
                if (RouteDist(p.x, p.y, out hr) > 3f) put(gCamp, p.x, p.y, RR(0.9f, 1.5f), 0.3f);
            }

        var gField = group("Boulders");
        for (int k = 0, tries = 0; k < s.scatteredRocks && tries < 4000; tries++)
        {
            Vector2 p = Polar(RR(0f, 360f), Mathf.Sqrt(R01()) * (s.playRadius + 10f));
            if (p.magnitude < s.hillRadius || RouteDist(p.x, p.y, out hr) < 3.5f) continue;
            Route sr; if (SpawnDist(p.x, p.y, out sr) < 9f) continue;
            if (InLake(p.x, p.y, GroundY(p.x, p.y), 0.2f)) continue;
            float sc = R01() < 0.15f ? RR(1.8f, 2.6f) : RR(0.5f, 1.4f);
            put(gField, p.x, p.y, sc, 0.32f); k++;
            if (R01() < 0.4f) { Vector2 q = p + Polar(RR(0f, 360f), sc * RR(1.2f, 2f)); if (RouteDist(q.x, q.y, out hr) > 3f) put(gField, q.x, q.y, sc * RR(0.35f, 0.6f), 0.3f); }
        }
        EditorAutomation.Log("  rocks: " + count);
    }

    // ================================================================== 8. water, backdrop
    static Texture2D MakeWaterNormal()
    {
        const int n = 256; var hm = new float[n, n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) hm[y, x] = TileNoise(x, y, n, 34f, 4, 17f);
        var t = new Texture2D(n, n, TextureFormat.RGB24, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = hm[y, (x + 1) % n] - hm[y, (x + n - 1) % n], dy = hm[(y + 1) % n, x] - hm[(y + n - 1) % n, x];
                Vector3 nn = new Vector3(-dx * 7f, -dy * 7f, 1f).normalized;
                t.SetPixel(x, y, new Color(nn.x * 0.5f + 0.5f, nn.y * 0.5f + 0.5f, nn.z * 0.5f + 0.5f));
            }
        t.Apply(); return t;
    }

    static void MakeWater(Transform parent)
    {
        if (!s.lake) return;
        var nrm = SaveTexture(GenDir + "/T_Water_Normal.png", MakeWaterNormal, ti => { ti.textureType = TextureImporterType.NormalMap; ti.wrapMode = TextureWrapMode.Repeat; });
        var sh = Shader.Find("ProjectGame/Water");
        var mat = GetAsset(GenDir + "/M_Water.mat", () => new Material(sh != null ? sh : Shader.Find("Standard")));
        if (sh != null) mat.shader = sh;
        mat.SetTexture("_NormalTex", nrm); mat.renderQueue = (int)RenderQueue.Transparent; EditorUtility.SetDirty(mat);
        const int n = 48; float rad = s.lakeRadius * 1.08f;
        var v = new Vector3[n + 1]; var t = new int[n * 3]; var uv = new Vector2[n + 1];
        v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n; v[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rad; uv[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f + Vector2.one * 0.5f;
            t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % n; t[i * 3 + 2] = 1 + i;
        }
        var m = GetAsset(GenDir + "/Lake_Surface.asset", () => new Mesh());
        m.Clear(); m.name = "Lake_Surface"; m.vertices = v; m.uv = uv; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds(); EditorUtility.SetDirty(m);
        var go = new GameObject("Lake"); go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(lakeC.x, waterLevel, lakeC.y);
        go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    static float Ridged(float x, float z, float scale, int oct, float sx)
    {
        float v = 0f, a = 1f, f = 1f, tot = 0f;
        for (int i = 0; i < oct; i++) { float nn = 1f - Mathf.Abs(Mathf.PerlinNoise(ox + sx + x / scale * f, oz + sx + z / scale * f) * 2f - 1f); v += a * nn * nn; tot += a; a *= 0.5f; f *= 2.07f; }
        return v / tot;
    }

    static void MakeBackdrop(Transform parent)
    {
        if (!s.backdrop) return;
        const int nS = 180, nR = 30; float r0 = half + 12f, r1 = s.backdropRadius;
        var v = new Vector3[nS * nR]; var col = new Color[nS * nR]; var tris = new List<int>();
        for (int k = 0; k < nR; k++)
        {
            float rho = r0 * Mathf.Pow(r1 / r0, k / (float)(nR - 1));
            for (int i = 0; i < nS; i++)
            {
                float a = i * Mathf.PI * 2f / nS, x = Mathf.Cos(a) * rho, z = Mathf.Sin(a) * rho;
                float amp = s.backdropPeakHeight * Smooth(half + 40f, 1150f, rho) * (1f - 0.3f * Smooth(1900f, r1, rho));
                float y = s.baseLevel + 22f + amp * Mathf.Pow(Ridged(x, z, 720f, 5, 900f), 1.25f) + 0.12f * amp * Fbm(x, z, 180f, 3, 950f);
                v[k * nS + i] = new Vector3(x, y, z);
            }
        }
        for (int k = 0; k < nR - 1; k++)
            for (int i = 0; i < nS; i++)
            {
                int a = k * nS + i, b = k * nS + (i + 1) % nS, c = a + nS, d = b + nS;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        var m = GetAsset(GenDir + "/Backdrop_Mountains.asset", () => new Mesh());
        m.Clear(); m.name = "Backdrop_Mountains"; m.vertices = v; m.triangles = tris.ToArray(); m.RecalculateNormals();
        var nrm = m.normals;
        if (nrm.Length > 0 && nrm[nS * (nR / 2)].y < 0f) { tris.Reverse(); m.triangles = tris.ToArray(); m.RecalculateNormals(); nrm = m.normals; }
        Color forest = new Color(0.16f, 0.25f, 0.15f), rock = new Color(0.36f, 0.36f, 0.38f), snow = new Color(0.90f, 0.92f, 0.96f);
        for (int i = 0; i < v.Length; i++)
        {
            float t = (v[i].y - s.baseLevel - 22f) / s.backdropPeakHeight, nz = Fbm(v[i].x, v[i].z, 140f, 2, 990f);
            Color c = Color.Lerp(forest, rock, Smooth(0.30f, 0.55f, t + (1f - nrm[i].y) * 0.35f + nz * 0.06f));
            col[i] = Color.Lerp(c, snow, Smooth(0.68f, 0.82f, t + nz * 0.08f));
        }
        m.colors = col; m.RecalculateBounds(); EditorUtility.SetDirty(m);
        var sh = Shader.Find("ProjectGame/VertexColorLit");
        var mat = GetAsset(GenDir + "/M_Backdrop.mat", () => new Material(sh != null ? sh : Shader.Find("Standard")));
        if (sh != null) mat.shader = sh;
        var go = new GameObject("Backdrop_Mountains"); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; go.isStatic = true;
    }

    // ================================================================== 9. gameplay
    static Material ColorMat(string name, Color c, string shader)
    {
        var sh = Shader.Find(shader);
        var m = GetAsset(GenDir + "/" + name + ".mat", () => new Material(sh));
        m.shader = sh; m.SetColor("_Color", c); if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f); EditorUtility.SetDirty(m); return m;
    }

    static GameObject Banner(Transform parent, Vector3 pos, float heightM, Material cloth, Material pole, out Renderer clothR)
    {
        var root = new GameObject("Banner"); root.transform.SetParent(parent, false); root.transform.position = pos;
        var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder); p.name = "Pole"; p.transform.SetParent(root.transform, false);
        p.transform.localScale = new Vector3(0.12f, heightM * 0.5f, 0.12f); p.transform.localPosition = new Vector3(0f, heightM * 0.5f, 0f);
        p.GetComponent<Renderer>().sharedMaterial = pole; UnityEngine.Object.DestroyImmediate(p.GetComponent<Collider>());   // bullets fly through
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.name = "Flag"; c.transform.SetParent(root.transform, false);
        UnityEngine.Object.DestroyImmediate(c.GetComponent<Collider>());
        float w = heightM * 0.32f; c.transform.localScale = new Vector3(w, w * 0.62f, 0.04f); c.transform.localPosition = new Vector3(w * 0.5f + 0.06f, heightM - w * 0.36f, 0f);
        clothR = c.GetComponent<Renderer>(); clothR.sharedMaterial = cloth;
        return root;
    }

    const float ZoneWallHeight = 2.4f, ZoneRingOverhang = 0.6f;

    /// <summary>
    /// Mesh around the zone centre (local space, centre at height top) following the ground: rows[j] is the radius of row j,
    /// lift[j] its height above the ground. UV.x runs around in metres at radius R, rounded to a whole multiple of 4 so the
    /// shaders' 1, 2 and 4 m patterns close the circle; UV.y is uvY[j].
    /// </summary>
    static Mesh ZoneMesh(string name, float R, float top, float[] rows, float[] lift, float[] uvY, bool radialNormals)
    {
        int n = Mathf.CeilToInt(2f * Mathf.PI * R / 0.7f), m = rows.Length;
        float around = 4f * Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * R / 4f));
        var v = new Vector3[(n + 1) * m]; var nm = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var t = new int[n * (m - 1) * 6];
        for (int i = 0, k = 0; i <= n; i++)
        {
            float a = i * Mathf.PI * 2f / n; Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int j = 0; j < m; j++)
            {
                int q = i * m + j; Vector3 p = d * rows[j];
                v[q] = p + Vector3.up * (GroundY(p.x, p.z) - top + lift[j]);
                nm[q] = radialNormals ? d : Vector3.up; uv[q] = new Vector2(i / (float)n * around, uvY[j]);
                if (i < n && j < m - 1) { int b = q, c = q + m; t[k++] = b; t[k++] = b + 1; t[k++] = c; t[k++] = c; t[k++] = b + 1; t[k++] = c + 1; }
            }
        }
        var mesh = GetAsset(GenDir + "/" + name + ".asset", () => new Mesh());
        mesh.Clear(); mesh.name = name; mesh.indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = v; mesh.normals = nm; mesh.uv = uv; mesh.triangles = t; mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Glowing disc on the ground, a little past the boundary (UV.y = metres from the centre).</summary>
    static Mesh ZoneRingMesh(float R, float top)
    {
        float outer = R + ZoneRingOverhang; int rings = Mathf.CeilToInt(outer / 0.8f);
        var rows = new List<float>();
        for (int j = 0; j <= rings; j++) rows.Add(outer * j / rings);
        foreach (float extra in new[] { R - 0.5f, R - 0.12f, R, R + 0.12f })   // dense near the boundary: it hugs the ground under the line
            if (extra > 0f && !rows.Any(r => Mathf.Abs(r - extra) < 0.05f)) rows.Add(extra);
        rows.Sort();
        var r0 = rows.ToArray();
        return ZoneMesh("KotH_Ring", R, top, r0, r0.Select(_ => 0.07f).ToArray(), r0, false);
    }

    /// <summary>Wall standing on the boundary, from just under the ground up (UV.y = metres above the ground).</summary>
    static Mesh ZoneWallMesh(float R, float top)
    {
        return ZoneMesh("Zone_Wall", R, top, new[] { R, R }, new[] { -0.3f, ZoneWallHeight }, new[] { -0.3f, ZoneWallHeight }, true);
    }

    static EnemyBot MakeBotPrefab()
    {
        EnsureFolder("Assets/Prefabs/Enemies");
        var sh = Shader.Find("ProjectGame/EnemyGlow");
        var mat = GetAsset(GenDir + "/M_EnemyBot.mat", () => new Material(sh != null ? sh : Shader.Find("Standard")));
        if (sh != null) mat.shader = sh;
        mat.SetColor("_Color", new Color(0.05f, 0.05f, 0.07f)); mat.SetColor("_EmissionColor", new Color(1.6f, 0.55f, 0.15f)); EditorUtility.SetDirty(mat);
        var root = new GameObject("EnemyBot_Box");
        var cc = root.AddComponent<CharacterController>(); cc.height = 2f; cc.radius = 0.45f; cc.center = new Vector3(0f, 1f, 0f); cc.slopeLimit = 65f; cc.stepOffset = 0.5f;
        var bot = root.AddComponent<EnemyBot>();
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body"; body.transform.SetParent(root.transform, false);
        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.localScale = new Vector3(0.9f, 2f, 0.7f); body.transform.localPosition = new Vector3(0f, 1f, 0f);
        bot.body = body.GetComponent<Renderer>(); bot.body.sharedMaterial = mat;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Enemies/EnemyBot_Box.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return prefab.GetComponent<EnemyBot>();
    }

    /// <summary>The zone on the summit: flag, glowing floor ring and energy wall. Builds a new one, or rebuilds the visuals of an existing one.</summary>
    static HillZone BuildZone(Transform parent, HillZone zone = null)
    {
        float top = GroundY(0f, 0f), R = s.captureRadius;
        var poleMat = ColorMat("M_Pole", new Color(0.35f, 0.33f, 0.30f), "Standard");
        var flagMat = ColorMat("M_KotH_Flag", Color.white, "Standard");
        if (zone == null) { zone = new GameObject("Hill_Zone").AddComponent<HillZone>(); zone.transform.SetParent(parent, false); }
        for (int i = zone.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(zone.transform.GetChild(i).gameObject);
        var zoneGo = zone.gameObject; zoneGo.transform.position = new Vector3(0f, top, 0f);
        zone.radius = R;
        var ringMat = ColorMat("M_KotH_Ring", zone.safeColor, "ProjectGame/ZoneRing"); ringMat.SetFloat("_Radius", R);
        var wallMat = ColorMat("M_Zone_Wall", zone.safeColor, "ProjectGame/ZoneWall"); wallMat.SetFloat("_Height", ZoneWallHeight);

        Renderer flagR; Banner(zoneGo.transform, new Vector3(0f, top, 0f), 7f, flagMat, poleMat, out flagR);
        Func<string, Mesh, Material, Renderer> part = (name, mesh, mat) =>
        {
            var g = new GameObject(name); g.transform.SetParent(zoneGo.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; return r;
        };
        var ringR = part("Ring", ZoneRingMesh(R, top), ringMat);
        var wallR = part("Wall", ZoneWallMesh(R, top), wallMat);
        zone.tintRenderers = new[] { ringR, wallR, flagR };
        EditorUtility.SetDirty(zone);
        return zone;
    }

    /// <summary>"zone": rebuilds the zone of the open hill level (radius from the settings, shape from the terrain) and saves the scene.</summary>
    public static void RebuildZoneInOpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var zone = UnityEngine.Object.FindFirstObjectByType<HillZone>();
        terrain = Terrain.activeTerrain;
        if (zone == null || terrain == null) { EditorAutomation.Log("ZONE_FAILED: no zone or terrain in " + scene.path); return; }
        s = LoadOrCreateSettings();
        BuildZone(zone.transform.parent, zone);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        EditorAutomation.Log("  zone radius " + F1(zone.radius) + " m, scene saved " + saved);
        EditorAutomation.Log("ZONE_OK");
    }

    static void BuildGameplay(Transform parent)
    {
        var zone = BuildZone(parent);

        // ---- player on the hill: walker + health + rifle + camera effect
        var r0 = routes[0];
        var player = new GameObject("Player"); player.transform.SetParent(parent, false);
        Vector2 pp = r0.dir * 3.5f;
        player.transform.position = new Vector3(pp.x, GroundY(pp.x, pp.y) + 0.15f, pp.y);
        player.transform.rotation = Quaternion.LookRotation(new Vector3(r0.dir.x, 0f, r0.dir.y));
        var health = player.AddComponent<PlayerHealth>();
        var cam = new GameObject("Camera"); cam.transform.SetParent(player.transform, false); cam.transform.localPosition = new Vector3(0f, 1.62f, 0f); cam.tag = "MainCamera";
        var camera = cam.AddComponent<Camera>(); camera.nearClipPlane = 0.12f; camera.farClipPlane = 8000f; camera.fieldOfView = 70f; cam.AddComponent<AudioListener>();
        var fx = cam.AddComponent<EdgeBlurEffect>(); fx.shader = Shader.Find("Hidden/ProjectGame/EdgeBlur"); fx.health = health;
        PlayerSetup.Configure(player, cam.transform);                             // capsule + kinematic body + CharacterMotor
        player.AddComponent<SimpleFirstPersonController>().cameraPivot = cam.transform;

        var weapons = WeaponSetup.BuildPlayerWeapons(player, camera);               // AK-47, MP5, 10mm, sniper rifle

        // ---- the wave game and its HUD
        var gameGo = new GameObject("Game"); gameGo.transform.SetParent(parent, false);
        var game = gameGo.AddComponent<WaveSurvivalGame>();
        game.waves = s.waves; game.waveDuration = s.waveDuration; game.batchInterval = s.batchInterval; game.mobsPerBatch = s.mobsPerBatch;
        game.zone = zone; game.player = player.transform; game.botPrefab = MakeBotPrefab();
        game.spawnRadius = Mathf.Min(s.playRadius + 15f, half - 14f); game.mapHalfSize = half - 8f;
        var hud = gameGo.AddComponent<SurvivalHud>(); hud.game = game; hud.health = health; hud.weapons = weapons;
        HudSetup.Build(hud);                                                      // the HUD canvas, visible in the editor
        SupplySetup.AddToGame(game);                                              // magazines, milk, energy drinks; settings menu; music
        CannonSetup.AddToLevel(parent, game, player.transform);                   // the field cannon on the summit + the cannonballs
        EditorAutomation.Log("  gameplay: " + s.waves + " waves x " + F1(s.waveDuration) + " s, " + s.mobsPerBatch + " bots x wave number every " + F1(s.batchInterval) + " s, bots appear " + F1(game.spawnRadius) + " m out, weapons: " + weapons.weapons.Length);
    }

    // ================================================================== 10. lighting
    static void BuildLighting(Transform parent)
    {
        var go = new GameObject("Sun"); go.transform.SetParent(parent, false);
        var sun = go.AddComponent<Light>(); sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.82f;
        var mgo = new GameObject("Moon"); mgo.transform.SetParent(parent, false);
        var moon = mgo.AddComponent<Light>(); moon.type = LightType.Directional; moon.shadows = LightShadows.None; moon.shadowStrength = 0.6f;
        RenderSettings.sun = sun;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        EnsureFolder("Assets/Art/Environment/Skybox");
        var skySh = Shader.Find("ProjectGame/SkyDayNight");
        Material sky = null;
        if (skySh != null)
        {
            sky = GetAsset("Assets/Art/Environment/Skybox/M_Sky_DayNight.mat", () => new Material(skySh));
            sky.shader = skySh; sky.SetFloat("_CloudCoverage", 0.36f); EditorUtility.SetDirty(sky); RenderSettings.skybox = sky;
        }
        var cycle = parent.gameObject.AddComponent<DayNightCycle>();
        cycle.sun = sun; cycle.moon = moon; cycle.sky = sky; cycle.dayLength = s.dayLength; cycle.timeOfDay = 0.36f;
        cycle.azimuth = 90f - routes[0].angle + 60f;                // morning sun from the side of the player's first view
        cycle.Apply();
        QualitySettings.shadowDistance = 170f; QualitySettings.shadowCascades = 4; QualitySettings.antiAliasing = 4; QualitySettings.lodBias = 2f;
        if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.runInBackground = true;
    }

    // ================================================================== run
    public static void Generate(KothMapSettings settings)
    {
        s = settings; rnd = new System.Random(s.seed); half = s.mapSize * 0.5f;
        ox = 1000f + (float)rnd.NextDouble() * 5000f; oz = 1000f + (float)rnd.NextDouble() * 5000f;
        EditorAutomation.Log("KOTH generate: seed " + s.seed);
        try
        {
            EnsureFolder(GenDir); EnsureFolder(PrefabDir);
            EditorUtility.DisplayProgressBar("Hill map", "Layout and routes", 0.05f);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PlanLayout(); TraceRoutes();
            EditorUtility.DisplayProgressBar("Hill map", "Terrain heights", 0.15f);
            var env = new GameObject("Environment").transform;
            BuildHeights(); terrain.transform.SetParent(env, true);
            EditorUtility.DisplayProgressBar("Hill map", "Painting", 0.35f); PaintTerrain();
            EditorUtility.DisplayProgressBar("Hill map", "Trees", 0.5f); PlantTrees();
            EditorUtility.DisplayProgressBar("Hill map", "Grass", 0.6f); ScatterGrass();
            EditorUtility.DisplayProgressBar("Hill map", "Rocks", 0.7f);
            var rocks = new GameObject("Rocks").transform; rocks.SetParent(env, false); PlaceRocks(rocks);
            EditorUtility.DisplayProgressBar("Hill map", "Water and backdrop", 0.8f); MakeWater(env); MakeBackdrop(env);
            EditorUtility.DisplayProgressBar("Hill map", "Gameplay", 0.88f);
            var lighting = new GameObject("Lighting").transform; BuildLighting(lighting);
            var gp = new GameObject("Gameplay").transform; BuildGameplay(gp);
            terrain.Flush(); EditorUtility.SetDirty(td);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            float tMin = routes.Min(r => r.time), tMax = routes.Max(r => r.time);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Hill " + F1(s.hillHeight) + " m high, " + F1(s.hillRadius * 2f) + " m wide; summit at y=" + F1(GroundY(0f, 0f)) + "; zone radius " + F1(s.captureRadius) + " m");
            foreach (var r in routes)
                sb.AppendLine("Route " + r.index + " " + r.style + ": " + F1(r.length) + " m, max grade " + F1(r.maxGradeDeg) + " deg, " + F1(r.time) + " s on foot");
            sb.AppendLine("Rock bands: " + cliffs.Count + "; trees: " + td.treeInstanceCount + "; scene saved: " + saved);
            LastReport = sb.ToString().TrimEnd();
            foreach (var line in LastReport.Split('\n')) EditorAutomation.Log("  " + line.Trim());

            CheckShots();
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { sv.sceneLighting = true; sv.LookAt(new Vector3(0f, GroundY(0f, 0f), 0f), Quaternion.Euler(28f, 90f - routes[0].angle + 180f + 25f, 0f), 170f, false, true); sv.Repaint(); }
            EditorAutomation.Log("KOTH_OK");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    /// <summary>Check renders of the open scene into Logs/shots at day, dusk and night (nothing is left in the scene).</summary>
    public static void CheckShots()
    {
        var zone = UnityEngine.Object.FindFirstObjectByType<HillZone>();
        var cycle = UnityEngine.Object.FindFirstObjectByType<DayNightCycle>();
        var game = UnityEngine.Object.FindFirstObjectByType<WaveSurvivalGame>();
        var t = Terrain.activeTerrain;
        if (zone == null || t == null) { EditorAutomation.Log("shots: no hill scene open"); return; }
        Func<float, float, float, Vector3> at = (x, z, up) => new Vector3(x, t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y + up, z);
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        float keep = cycle != null ? cycle.timeOfDay : 0f;
        var temp = new List<GameObject>();
        try
        {
            Vector3 top = zone.transform.position;
            Vector3 fwd = Quaternion.Euler(0f, 35f, 0f) * Vector3.forward;
            if (game != null && game.botPrefab != null)               // a few bots in front of the summit camera, to see them in every light
                for (int k = 0; k < 5; k++)
                {
                    Vector3 p = top + Quaternion.Euler(0f, 35f + (k - 2) * 14f, 0f) * Vector3.forward * (9f + k * 2.5f);
                    var b = UnityEngine.Object.Instantiate(game.botPrefab.gameObject, at(p.x, p.z, 0f), Quaternion.LookRotation(-fwd));
                    b.hideFlags = HideFlags.HideAndDontSave; temp.Add(b);
                }
            var times = new[] { new KeyValuePair<string, float>("day", 0.40f), new KeyValuePair<string, float>("dusk", 0.742f), new KeyValuePair<string, float>("night", 0.96f) };
            foreach (var kv in times)
            {
                if (cycle != null) { cycle.timeOfDay = kv.Value; cycle.Apply(); }
                EditorAutomation.Shot("hill_summit_" + kv.Key, top + Vector3.up * 1.65f - fwd * 3f, top + fwd * 60f - Vector3.up * 3f, 70f, true);
                Vector3 far = at(top.x - fwd.x * 150f, top.z - fwd.z * 150f, 1.7f);
                EditorAutomation.Shot("hill_from_valley_" + kv.Key, far, top + Vector3.up * 2f, 62f, false);
                if (kv.Key == "day")
                {
                    EditorAutomation.Shot("hill_overview", top - fwd * 330f + Vector3.up * 150f, top - Vector3.up * 8f, 50f, false);
                    EditorAutomation.Shot("hill_lake_view", top + Vector3.up * 1.65f, new Vector3(lakeC.x, top.y - 30f, lakeC.y), 70f, true);
                }
            }
        }
        finally
        {
            foreach (var g in temp) UnityEngine.Object.DestroyImmediate(g);
            if (cycle != null) { cycle.timeOfDay = keep; cycle.Apply(); }
            ShaderUtil.allowAsyncCompilation = async;
        }
    }
}

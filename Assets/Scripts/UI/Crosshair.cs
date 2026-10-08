using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The crosshair and the hit marker, one mesh laid out in screen pixels: whole-pixel sizes, every edge on a pixel boundary
/// and the four arms mirror images of each other around the centre at any resolution. (Boxes on the HUD canvas, scaled from
/// 1080p, fall between pixels: one arm came out 1 px thicker than the other, or blurred.) A thin dark rim keeps it readable
/// against the sky and the sand.
/// The gap is honest: the inner ends of the arms sit on the spread cone of the weapon in hand (wide for the sniper from the
/// hip, tight for the pistol), plus a short kick per round fired. The hit marker is four slanted strokes with soft edges:
/// white on a hit, red and longer on a kill. Sizes are pixels at 1080p; they grow with the screen height above that.
/// SurvivalHud feeds it every frame (Tick); the mesh is rebuilt only when a whole-pixel size or the marker fade changes.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class Crosshair : MaskableGraphic
{
    [Header("Cross, pixels at 1080p")]
    [Min(1f)] public float thickness = 2f;
    [Min(1f)] public float length = 7f;
    [Tooltip("Smallest gap between the centre and the arms; a weapon with spread opens it further.")] [Min(0f)] public float gap = 4f;
    [Tooltip("Dark rim round every part, 0 = none.")] [Min(0f)] public float outline = 1f;
    public Color outlineColor = new Color(0f, 0f, 0f, 0.6f);
    public bool centerDot;
    [Tooltip("The arms jump out this far per round fired.")] public float kickPerRound = 2f;
    public float maxKick = 6f;
    [Tooltip("How fast the kick and a change of spread settle, 1/s.")] public float settle = 14f;
    [Tooltip("Alpha while the weapon cannot fire (reload, switch, empty).")] [Range(0f, 1f)] public float notReadyAlpha = 0.35f;

    [Header("Hit marker")]
    public Color hitColor = Color.white;
    public Color killColor = new Color(1f, 0.3f, 0.2f);
    [Tooltip("Inner and outer end of the strokes from the centre, pixels at 1080p.")] public Vector2 hitRadius = new Vector2(7f, 13f);
    public Vector2 killRadius = new Vector2(7f, 17f);
    [Min(1f)] public float hitThickness = 2f;
    public float hitTime = 0.25f, killTime = 0.45f;

    const int MarkSteps = 20;                                              // the marker fade rebuilds the mesh at most this often

    float cone = -1f, kick, markTime = -10f;                               // cone and kick: pixels at 1080p
    bool markKill, synced;
    int shots, hits, kills;
    Rect builtScreen; float builtScale; int builtGap = -1, builtMark = -1; // what the mesh was built for
    Vector2 origin; float unit;                                            // local position of the screen centre, local units per pixel

    protected override void OnEnable() { base.OnEnable(); synced = false; cone = -1f; }

    /// <summary>Every frame while it is shown. spread: cone of the weapon in hand, degrees; fieldOfView: vertical, of the camera;
    /// ready: the weapon can fire now; shotCount, hitCount, killCount: running totals, a rise kicks the arms or flashes the marker.</summary>
    public void Tick(float spread, float fieldOfView, bool ready, int shotCount, int hitCount, int killCount)
    {
        if (!synced) { shots = shotCount; hits = hitCount; kills = killCount; synced = true; }   // shown again: what happened meanwhile is no news
        if (shotCount > shots) kick = Mathf.Min(kick + kickPerRound * (shotCount - shots), maxKick);
        if (hitCount > hits) { markTime = Time.time; markKill = killCount > kills; }      // a kill without a hit of ours (a cannonball) shows nothing
        shots = shotCount; hits = hitCount; kills = killCount;

        float k = 1f - Mathf.Exp(-settle * Time.deltaTime);
        float target = Mathf.Tan(Mathf.Clamp(spread, 0f, 60f) * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Clamp(fieldOfView, 1f, 170f) * 0.5f * Mathf.Deg2Rad) * 540f;
        cone = cone < 0f ? target : Mathf.Lerp(cone, target, k);         // a weapon switch opens or closes the gap smoothly
        kick -= kick * k;

        float a = canvasRenderer.GetAlpha(), to = Mathf.MoveTowards(a, ready ? 1f : notReadyAlpha, Time.deltaTime * 5f);
        if (to != a) canvasRenderer.SetAlpha(to);                         // the whole graphic fades: no mesh rebuild

        var root = canvas != null ? canvas.rootCanvas : null;
        if (root == null) return;
        Rect screen = root.pixelRect;
        if (screen != builtScreen || root.scaleFactor != builtScale || GapPixels(screen.height) != builtGap || MarkStep() != builtMark) SetVerticesDirty();
    }

    static int Px(float v, float s, int min) { return Mathf.Max(min, Mathf.RoundToInt(v * s)); }

    /// <summary>Scale of the sizes: grows above 1080p, never shrinks below it (a 1 px crosshair with no rim is lost on a small screen).</summary>
    static float SizeScale(float screenHeight) { return Mathf.Max(1f, screenHeight / 1080f); }

    int RimPixels(float s) { return outline > 0f ? Px(outline, s, 1) : 0; }

    /// <summary>From the edge of the centre to the inner end of an arm, whole pixels.</summary>
    int GapPixels(float screenHeight)
    {
        float s = SizeScale(screenHeight);
        float g = Mathf.Max(gap * s, Mathf.Max(0f, cone) * screenHeight / 1080f - Px(thickness, s, 1) * 0.5f) + kick * s;   // the cone in true screen pixels
        return Mathf.Max(2 * RimPixels(s), Mathf.RoundToInt(g));           // rims of neighbouring parts must not overlap (a darker seam)
    }

    /// <summary>The hit marker fade, MarkSteps (just hit) .. 0 (gone).</summary>
    int MarkStep()
    {
        float d = markKill ? killTime : hitTime, age = Time.time - markTime;
        return age < d ? Mathf.CeilToInt((1f - age / d) * MarkSteps) : 0;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var root = canvas != null ? canvas.rootCanvas : null;
        if (root == null) return;
        Rect screen = root.pixelRect;
        float s = SizeScale(screen.height);
        builtScreen = screen; builtScale = root.scaleFactor; builtGap = GapPixels(screen.height); builtMark = MarkStep();
        unit = 1f / Mathf.Max(0.0001f, root.scaleFactor);
        origin = rectTransform.rect.center;                               // the screen centre: this rect is stretched over the canvas

        // in pixels from the screen centre: the boundaries are at whole offsets on an even-sized screen, at halves on an odd one;
        // the vertical bar starts on the boundary nearest to -thickness/2 and everything else is whole pixels away from it
        int t = Px(thickness, s, 1), len = Px(length, s, 1), o = RimPixels(s), g = builtGap;
        float fx = (Mathf.RoundToInt(screen.width) & 1) * 0.5f, fy = (Mathf.RoundToInt(screen.height) & 1) * 0.5f;
        float x0 = Mathf.Floor(-t * 0.5f - fx + 0.5f) + fx, y0 = Mathf.Floor(-t * 0.5f - fy + 0.5f) + fy;

        Color32 fill = color, rim = outlineColor;
        for (int pass = 0; pass < 2; pass++)                              // all the rims first: a fill always covers its neighbour's rim
        {
            if (pass == 0 && (o == 0 || rim.a == 0)) continue;
            float e = pass == 0 ? o : 0f; Color32 c = pass == 0 ? rim : fill;
            if (centerDot) Quad(vh, x0 - e, y0 - e, x0 + t + e, y0 + t + e, c);
            Quad(vh, x0 + t + g - e, y0 - e, x0 + t + g + len + e, y0 + t + e, c);   // right
            Quad(vh, x0 - g - len - e, y0 - e, x0 - g + e, y0 + t + e, c);           // left
            Quad(vh, x0 - e, y0 + t + g - e, x0 + t + e, y0 + t + g + len + e, c);   // up
            Quad(vh, x0 - e, y0 - g - len - e, x0 + t + e, y0 - g + e, c);           // down
        }

        // hit marker: bright for the first half of its time, then fading while it drifts a little outwards
        float f = builtMark / (float)MarkSteps;
        if (f <= 0f) return;
        float alpha = Mathf.Min(1f, f * 2f), pop = (1f - f) * 3f * s, w = Mathf.Max(1f, hitThickness * s);
        Vector2 r = (markKill ? killRadius : hitRadius) * s, mid = new Vector2(x0 + t * 0.5f, y0 + t * 0.5f);
        Color mc = markKill ? killColor : hitColor, oc = outlineColor;
        mc.a *= alpha; oc.a *= alpha;
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 0 && (o == 0 || oc.a <= 0f)) continue;
            float e = pass == 0 ? o : 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 d = new Vector2(i < 2 ? 1f : -1f, (i & 1) == 0 ? 1f : -1f) * 0.70710678f;
                Stroke(vh, mid + d * (r.x + pop - e), mid + d * (r.y + pop + e), w + 2f * e, pass == 0 ? oc : mc);
            }
        }
    }

    void Vert(VertexHelper vh, Vector2 p, Color32 c) { vh.AddVert(origin + p * unit, c, Vector4.zero); }

    /// <summary>An upright box between two corners, in pixels from the screen centre.</summary>
    void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
    {
        int i = vh.currentVertCount;
        Vert(vh, new Vector2(x0, y0), c); Vert(vh, new Vector2(x0, y1), c); Vert(vh, new Vector2(x1, y1), c); Vert(vh, new Vector2(x1, y0), c);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
    }

    /// <summary>A slanted stroke from a to b, w wide, with a soft 1 px edge all round so it does not stair-step:
    /// a core 0.5 px inside the true outline at full alpha and a rim 0.5 px outside it at none.</summary>
    void Stroke(VertexHelper vh, Vector2 a, Vector2 b, float w, Color32 c)
    {
        Vector2 u = (b - a).normalized, n = new Vector2(-u.y, u.x);
        float hi = w * 0.5f - 0.5f, ho = w * 0.5f + 0.5f;
        Vector2 ai = a + u * 0.5f, bi = b - u * 0.5f, ao = a - u * 0.5f, bo = b + u * 0.5f;
        Color32 clear = c; clear.a = 0;
        int i = vh.currentVertCount;
        Vert(vh, ai - n * hi, c); Vert(vh, ai + n * hi, c); Vert(vh, bi + n * hi, c); Vert(vh, bi - n * hi, c);
        Vert(vh, ao - n * ho, clear); Vert(vh, ao + n * ho, clear); Vert(vh, bo + n * ho, clear); Vert(vh, bo - n * ho, clear);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
        for (int k = 0; k < 4; k++)
        {
            int k1 = (k + 1) & 3;
            vh.AddTriangle(i + k, i + 4 + k, i + 4 + k1); vh.AddTriangle(i + 4 + k1, i + k1, i + k);
        }
    }
}

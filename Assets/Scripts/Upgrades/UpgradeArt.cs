using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The pictures of the upgrade cards, drawn in code once (no texture files): a rounded panel, its outline and soft glow
/// (9-sliced), a circle and a ring, a top-down fade, and one icon per ModIcon - signed distance shapes, antialiased.
/// </summary>
public static class UpgradeArt
{
    static Sprite panel, outline, glow, circle, ring, fade;
    static readonly Dictionary<ModIcon, Sprite> icons = new Dictionary<ModIcon, Sprite>();

    public static Sprite Panel { get { if (panel == null) panel = Sliced("UpPanel", 64, 22, (x, y) => Mathf.Clamp01(0.5f - RoundBox(x, y, 64, 18f))); return panel; } }
    public static Sprite Outline { get { if (outline == null) outline = Sliced("UpOutline", 64, 22, (x, y) => { float d = RoundBox(x, y, 64, 18f); return Mathf.Clamp01(0.5f - (Mathf.Abs(d + 1.6f) - 1.6f)); }); return outline; } }
    public static Sprite Glow
    {
        get
        {
            // a halo: nothing at the edge, rising softly (rounded corners) to full 110 px in - the card covers the middle
            if (glow == null) glow = Sliced("UpGlow", 256, 110, (x, y) => Mathf.Pow(Mathf.Clamp01(-RoundBox(x, y, 256, 80f) / 110f), 2.4f));
            return glow;
        }
    }
    public static Sprite Circle { get { if (circle == null) circle = Plain("UpCircle", 128, (x, y) => Mathf.Clamp01(0.5f - (Vector2.Distance(new Vector2(x, y), new Vector2(64f, 64f)) - 62f))); return circle; } }
    public static Sprite Ring { get { if (ring == null) ring = Plain("UpRing", 128, (x, y) => Mathf.Clamp01(0.5f - (Mathf.Abs(Vector2.Distance(new Vector2(x, y), new Vector2(64f, 64f)) - 59f) - 3f))); return ring; } }
    public static Sprite Fade { get { if (fade == null) fade = Plain("UpFade", 64, (x, y) => Mathf.Pow(y / 64f, 1.6f)); return fade; } }

    public static Sprite Icon(ModIcon icon)
    {
        Sprite s;
        if (icons.TryGetValue(icon, out s) && s != null) return s;
        Func<Vector2, float> sdf = Shape(icon);
        s = Plain("UpIcon_" + icon, 128, (x, y) => Mathf.Clamp01(0.5f - sdf(new Vector2(x / 64f - 1f, y / 64f - 1f)) * 64f));
        icons[icon] = s;
        return s;
    }

    // ------------------------------------------------------------------ textures
    static Texture2D Tex(string name, int size, Func<float, float, float> alpha)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f)) * 255f));
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    static Sprite Plain(string name, int size, Func<float, float, float> alpha)
    {
        var s = Sprite.Create(Tex(name, size, alpha), new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.name = name; s.hideFlags = HideFlags.DontSave; return s;
    }

    static Sprite Sliced(string name, int size, int border, Func<float, float, float> alpha)
    {
        var s = Sprite.Create(Tex(name, size, alpha), new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        s.name = name; s.hideFlags = HideFlags.DontSave; return s;
    }

    /// <summary>Distance in pixels from the edge of a rounded square filling the texture (radius r), negative inside.</summary>
    static float RoundBox(float x, float y, int size, float r)
    {
        float h = size * 0.5f;
        Vector2 q = new Vector2(Mathf.Abs(x - h) - (h - 1f - r), Mathf.Abs(y - h) - (h - 1f - r));
        return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }

    // ------------------------------------------------------------------ signed distances (p in -1..1)
    static float Circ(Vector2 p, Vector2 c, float r) { return (p - c).magnitude - r; }
    static float RingD(Vector2 p, Vector2 c, float r, float w) { return Mathf.Abs((p - c).magnitude - r) - w; }
    static float Box(Vector2 p, Vector2 c, Vector2 b, float round)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - b + Vector2.one * round;
        return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - round;
    }
    static float Seg(Vector2 p, Vector2 a, Vector2 b, float r)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - r;
    }
    static float Line(Vector2 p, float r, params Vector2[] pts)
    {
        float d = 1e9f;
        for (int i = 0; i + 1 < pts.Length; i++) d = Mathf.Min(d, Seg(p, pts[i], pts[i + 1], r));
        return d;
    }
    /// <summary>Any simple polygon (Inigo Quilez's polygon distance).</summary>
    static float Poly(Vector2 p, params Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]), s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i], w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }
    static float Star(Vector2 p, int points, float outer, float inner, float turn = 0f)
    {
        var v = new Vector2[points * 2];
        for (int i = 0; i < v.Length; i++)
        {
            float a = turn + Mathf.PI * 0.5f + i * Mathf.PI / points, r = (i & 1) == 0 ? outer : inner;
            v[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return Poly(p, v);
    }
    static float Heart(Vector2 p)
    {
        p.x = Mathf.Abs(p.x);
        if (p.y + p.x > 1f) return (p - new Vector2(0.25f, 0.75f)).magnitude - Mathf.Sqrt(2f) / 4f;
        float a = (p - new Vector2(0f, 1f)).sqrMagnitude, m = Mathf.Max(p.x + p.y, 0f) * 0.5f, b = (p - new Vector2(m, m)).sqrMagnitude;
        return Mathf.Sqrt(Mathf.Min(a, b)) * Mathf.Sign(p.x - p.y);
    }
    static float Bullet(Vector2 p, Vector2 c, float k)
    {
        p = (p - c) / k;
        float body = Box(p, new Vector2(0f, -0.25f), new Vector2(0.26f, 0.5f), 0.05f);
        float tip = Poly(p, new Vector2(-0.26f, 0.2f), new Vector2(0.26f, 0.2f), new Vector2(0.2f, 0.52f), new Vector2(0f, 0.82f), new Vector2(-0.2f, 0.52f));
        float d = Mathf.Min(body, tip);
        d = Mathf.Max(d, -Box(p, new Vector2(0f, -0.52f), new Vector2(0.4f, 0.035f), 0f));     // the groove above the rim
        return d * k;
    }
    static Vector2 Rot(Vector2 p, float deg) { float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y); }
    static Vector2 V(float x, float y) { return new Vector2(x, y); }

    static Func<Vector2, float> Shape(ModIcon icon)
    {
        switch (icon)
        {
            case ModIcon.Bullet: return p => Bullet(p, Vector2.zero, 1f);
            case ModIcon.Rate: return p => Mathf.Min(Line(p, 0.09f, V(-0.75f, 0.45f), V(-0.4f, 0f), V(-0.75f, -0.45f)),
                                           Mathf.Min(Line(p, 0.09f, V(-0.2f, 0.45f), V(0.15f, 0f), V(-0.2f, -0.45f)), Line(p, 0.09f, V(0.35f, 0.45f), V(0.7f, 0f), V(0.35f, -0.45f))));
            case ModIcon.Reload: return p =>
            {
                float a = Mathf.Atan2(p.y, p.x), arc = RingD(p, Vector2.zero, 0.58f, 0.09f);
                if (a > 0.25f && a < 1.25f) arc = 1e9f;                                       // the gap
                Vector2 e = V(Mathf.Cos(1.25f), Mathf.Sin(1.25f)) * 0.58f, n = e.normalized, t = V(-n.y, n.x);
                return Mathf.Min(arc, Poly(p, e + n * 0.24f, e + t * 0.3f, e - n * 0.24f));
            };
            case ModIcon.Speed: return p => Mathf.Min(Mathf.Min(Seg(p, V(-0.92f, 0.32f), V(-0.5f, 0.32f), 0.065f), Seg(p, V(-0.98f, 0f), V(-0.45f, 0f), 0.065f)),
                                            Mathf.Min(Seg(p, V(-0.92f, -0.32f), V(-0.5f, -0.32f), 0.065f),
                                                      Mathf.Min(Line(p, 0.1f, V(-0.25f, 0.5f), V(0.15f, 0f), V(-0.25f, -0.5f)), Line(p, 0.1f, V(0.3f, 0.5f), V(0.7f, 0f), V(0.3f, -0.5f)))));
            case ModIcon.Heart: return p => Heart(new Vector2(p.x, p.y + 0.62f) / 1.25f) * 1.25f;
            case ModIcon.Blade: return p => Mathf.Min(Mathf.Min(Poly(p, V(0f, 0.92f), V(0.11f, 0.62f), V(0.09f, -0.3f), V(-0.09f, -0.3f), V(-0.11f, 0.62f)),
                                                                Seg(p, V(-0.38f, -0.36f), V(0.38f, -0.36f), 0.07f)),
                                                      Mathf.Min(Seg(p, V(0f, -0.4f), V(0f, -0.74f), 0.065f), Circ(p, V(0f, -0.84f), 0.1f)));
            case ModIcon.Aim: return p => Mathf.Min(Mathf.Min(RingD(p, Vector2.zero, 0.55f, 0.06f), Circ(p, Vector2.zero, 0.09f)),
                                          Mathf.Min(Mathf.Min(Seg(p, V(0f, 0.38f), V(0f, 0.85f), 0.055f), Seg(p, V(0f, -0.38f), V(0f, -0.85f), 0.055f)),
                                                    Mathf.Min(Seg(p, V(0.38f, 0f), V(0.85f, 0f), 0.055f), Seg(p, V(-0.38f, 0f), V(-0.85f, 0f), 0.055f))));
            case ModIcon.Cross: return p => Mathf.Min(Box(p, Vector2.zero, V(0.22f, 0.66f), 0.06f), Box(p, Vector2.zero, V(0.66f, 0.22f), 0.06f));
            case ModIcon.Mag: return p =>
            {
                float d = Poly(p, V(-0.32f, 0.8f), V(0.32f, 0.8f), V(0.46f, -0.8f), V(-0.16f, -0.8f));
                for (int i = 0; i < 3; i++) d = Mathf.Max(d, -Box(p, V(0.06f + 0.05f * i, 0.4f - 0.4f * i), V(0.5f, 0.04f), 0f));
                return d;
            };
            case ModIcon.Shield: return p =>
            {
                float outer = Poly(p, V(-0.62f, 0.72f), V(0.62f, 0.72f), V(0.62f, 0.08f), V(0f, -0.88f), V(-0.62f, 0.08f));
                float inner = Poly(p, V(-0.44f, 0.56f), V(0.44f, 0.56f), V(0.44f, 0.1f), V(0f, -0.64f), V(-0.44f, 0.1f));
                return Mathf.Min(Mathf.Max(outer, -inner), Box(p, V(0f, 0.02f), V(0.06f, 0.42f), 0.02f));
            };
            case ModIcon.Pierce: return p => Mathf.Min(Mathf.Min(Seg(p, V(-0.9f, 0f), V(0.45f, 0f), 0.07f), Poly(p, V(0.9f, 0f), V(0.42f, 0.28f), V(0.42f, -0.28f))),
                                             RingD(p, V(-0.1f, 0f), 0.42f, 0.07f));
            case ModIcon.Drop: return p => Mathf.Min(Circ(p, V(0f, -0.28f), 0.5f), Poly(p, V(0f, 0.92f), V(0.43f, -0.08f), V(-0.43f, -0.08f)));
            case ModIcon.Pulse: return p => Line(p, 0.075f, V(-0.92f, -0.05f), V(-0.45f, -0.05f), V(-0.28f, 0.5f), V(-0.06f, -0.62f), V(0.16f, 0.32f), V(0.32f, -0.05f), V(0.92f, -0.05f));
            case ModIcon.Crate: return p =>
            {
                float outer = Box(p, V(0f, -0.08f), V(0.72f, 0.6f), 0.06f), inner = Box(p, V(0f, -0.08f), V(0.56f, 0.44f), 0.02f);
                return Mathf.Min(Mathf.Min(Mathf.Max(outer, -inner), Seg(p, V(-0.6f, -0.52f), V(0.6f, 0.36f), 0.07f)), Box(p, V(0f, 0.3f), V(0.72f, 0.06f), 0.02f));
            };
            case ModIcon.Impact: return p => Mathf.Max(Star(p, 6, 0.92f, 0.42f, 0.2f), -Circ(p, Vector2.zero, 0.16f));
            case ModIcon.Skull: return p =>
            {
                float d = Mathf.Min(Circ(p, V(0f, 0.16f), 0.62f), Box(p, V(0f, -0.44f), V(0.36f, 0.24f), 0.08f));
                d = Mathf.Max(d, -Circ(p, V(-0.24f, 0.12f), 0.17f)); d = Mathf.Max(d, -Circ(p, V(0.24f, 0.12f), 0.17f));
                d = Mathf.Max(d, -Poly(p, V(0f, -0.04f), V(0.09f, -0.22f), V(-0.09f, -0.22f)));
                for (int i = -1; i <= 1; i++) d = Mathf.Max(d, -Box(p, V(i * 0.13f, -0.56f), V(0.025f, 0.14f), 0f));
                return d;
            };
            case ModIcon.Feather: return p =>
            {
                Vector2 q = Rot(p, -35f);
                float leaf = Mathf.Max(Circ(q, V(0.55f, 0f), 0.82f), Circ(q, V(-0.55f, 0f), 0.82f));
                leaf = Mathf.Max(leaf, -Seg(q, V(0f, -0.5f), V(0f, 0.55f), 0.03f));
                return Mathf.Min(leaf, Seg(q, V(0f, -0.55f), V(0f, -0.95f), 0.05f));
            };
            case ModIcon.Burst: return p => Mathf.Min(Star(p, 8, 0.95f, 0.46f), Circ(p, Vector2.zero, 0.36f));
            case ModIcon.Bolt: return p => Poly(p, V(0.18f, 0.95f), V(-0.48f, -0.06f), V(-0.06f, -0.06f), V(-0.26f, -0.95f), V(0.48f, 0.14f), V(0.06f, 0.14f));
            case ModIcon.Hose: return p => Mathf.Min(Bullet(p, V(-0.48f, -0.05f), 0.62f), Mathf.Min(Bullet(p, V(0f, 0.05f), 0.62f), Bullet(p, V(0.48f, -0.05f), 0.62f)));
            case ModIcon.Clock: return p => Mathf.Min(Mathf.Min(RingD(p, Vector2.zero, 0.7f, 0.08f), Circ(p, Vector2.zero, 0.1f)),
                                            Mathf.Min(Seg(p, Vector2.zero, V(0f, 0.46f), 0.065f), Seg(p, Vector2.zero, V(0.34f, -0.12f), 0.065f)));
            case ModIcon.Tornado: return p => Mathf.Min(Mathf.Min(Seg(p, V(-0.8f, 0.62f), V(0.8f, 0.62f), 0.1f), Seg(p, V(-0.52f, 0.3f), V(0.6f, 0.3f), 0.09f)),
                                              Mathf.Min(Mathf.Min(Seg(p, V(-0.28f, 0f), V(0.44f, 0f), 0.085f), Seg(p, V(-0.12f, -0.3f), V(0.26f, -0.3f), 0.08f)),
                                                        Seg(p, V(0.02f, -0.6f), V(0.12f, -0.6f), 0.075f)));
            case ModIcon.Infinity: return p => Mathf.Min(RingD(p, V(-0.4f, 0f), 0.33f, 0.085f), RingD(p, V(0.4f, 0f), 0.33f, 0.085f));
        }
        return p => Circ(p, Vector2.zero, 0.5f);
    }
}

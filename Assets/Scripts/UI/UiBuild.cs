using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Building blocks of the UI made in code (the HUD canvas, the upgrade cards): a RectTransform node, one stretched over its parent,
/// a nested canvas layer, a plain box, a label in the built-in font with a drop shadow.
/// </summary>
public static class UiBuild
{
    public static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

    static Font font;
    public static Font UiFont { get { if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return font; } }

    public static RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Stretch(string name, Transform parent)
    {
        var rt = Node(name, parent, Vector2.zero, Centre, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        return rt;
    }

    /// <summary>A nested canvas: its children are batched and rebuilt on their own.</summary>
    public static RectTransform Layer(string name, Transform parent)
    {
        var rt = Stretch(name, parent);
        rt.gameObject.AddComponent<Canvas>();
        return rt;
    }

    public static Image Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
    {
        var img = Node(name, parent, anchor, pivot, pos, size).gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    public static Text Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Color c, bool bold = true, string text = "")
    {
        var t = Node(name, parent, anchor, pivot, pos, size).gameObject.AddComponent<Text>();
        t.font = UiFont; t.fontSize = fontSize; t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; t.alignment = align; t.color = c;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false; t.text = text;
        var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.7f); sh.effectDistance = new Vector2(2f, -2f);
        return t;
    }
}

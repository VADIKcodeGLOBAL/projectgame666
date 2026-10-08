using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The look of the upgrade choice (UpgradeSystem decides what is on it and when): the screen dims, a title, three cards fly in.
/// A card: a soft glow in its rarity colour, a dark rounded panel with a wash of the colour from the top, an outline, the rarity
/// on a ribbon, the icon in a circle, the name, what it does, the key that takes it. The mouse over a card lifts it; SWEG and
/// SLAYER cards have a light sweeping over them, SLAYER also burns. Taken: the card flares up and flies off, the others drop.
/// Postponed: the cards shrink into the panel on the left, which lists the choices waiting (one row each, a dot per card in its
/// colour). Everything is animated in real time: the game is paused meanwhile. Built in code, on its own overlay canvas.
/// </summary>
public class UpgradeScreen : MonoBehaviour
{
    public const int MaxCards = 4;
    static readonly Vector2 CardSize = new Vector2(330f, 470f);
    static readonly Color Body = new Color(0.07f, 0.075f, 0.09f, 0.97f), Muted = new Color(0.8f, 0.83f, 0.88f), Gold = new Color(1f, 0.85f, 0.4f);
    static readonly Vector2 PanelPos = new Vector2(160f, 70f);            // the waiting choices: from the middle of the left edge

    class Card
    {
        public RectTransform rt; public CanvasGroup group;
        public Image glow, panel, wash, outline, ribbon, iconBack, iconRing, icon, line, shine, keyBack, keyLine;
        public Text rarity, title, text, key;
        public Modifier mod; public Vector2 home; public float hover, flash;
    }

    class Row { public RectTransform rt; public Image back, edge; public Text label; public Image[] dots; public float born; }

    public bool Showing { get; private set; }
    /// <summary>An animation (in, pick, postpone) is running: input waits.</summary>
    public bool Busy { get { return mode != Mode.Idle && mode != Mode.Hidden; } }
    /// <summary>The card under the mouse, -1 for none.</summary>
    public int Hovered { get; private set; } = -1;
    /// <summary>Tests: a card to treat as hovered (-1: the mouse decides).</summary>
    public int TestHover = -1;
    public int CardCount { get { return count; } }

    enum Mode { Hidden, In, Idle, Pick, Later }
    Mode mode = Mode.Hidden;
    float t; int count, picked; Action done;

    RectTransform root, cardsRoot, panelRoot;
    CanvasGroup screenGroup, panelGroup;
    Text header, titleText, footer, panelTitle, panelHint;
    readonly Card[] cards = new Card[MaxCards];
    readonly List<Row> rows = new List<Row>();

    // ------------------------------------------------------------------ building
    void Awake() { Build(); }

    void Build()
    {
        if (root != null) return;
        var go = new GameObject("Upgrade Canvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)go.transform;

        // ---- the choice: dim, titles, cards (one group: it fades as a whole)
        var screen = Stretch("Choice", root); screenGroup = screen.gameObject.AddComponent<CanvasGroup>(); screenGroup.blocksRaycasts = false;
        var dim = Stretch("Dim", screen).gameObject.AddComponent<Image>(); dim.color = new Color(0.01f, 0.012f, 0.02f, 0.72f); dim.raycastTarget = false;
        header = Label("Header", screen, new Vector2(0f, 400f), new Vector2(1200f, 40f), 26, Gold, true);
        titleText = Label("Title", screen, new Vector2(0f, 345f), new Vector2(1400f, 70f), 54, Color.white, true);
        footer = Label("Footer", screen, new Vector2(0f, -340f), new Vector2(1400f, 40f), 24, Muted, false);
        cardsRoot = Node("Cards", screen, Vector2.zero, Vector2.zero);
        for (int i = 0; i < MaxCards; i++) cards[i] = MakeCard(i);
        screen.gameObject.SetActive(false);

        // ---- the waiting choices, on the left
        panelRoot = Node("Later", root, PanelPos, new Vector2(300f, 60f));
        panelRoot.anchorMin = panelRoot.anchorMax = new Vector2(0f, 0.5f); panelRoot.anchoredPosition = PanelPos;
        panelGroup = panelRoot.gameObject.AddComponent<CanvasGroup>(); panelGroup.blocksRaycasts = false;
        panelTitle = Label("Title", panelRoot, new Vector2(0f, 26f), new Vector2(300f, 30f), 20, Gold, true);
        panelTitle.alignment = TextAnchor.MiddleLeft; panelTitle.rectTransform.anchoredPosition = new Vector2(0f, 26f);
        panelHint = Label("Hint", panelRoot, new Vector2(0f, 0f), new Vector2(300f, 26f), 18, Muted, false);
        panelHint.alignment = TextAnchor.MiddleLeft;
        panelRoot.gameObject.SetActive(false);
    }

    Card MakeCard(int i)
    {
        var c = new Card();
        c.rt = Node("Card" + (i + 1), cardsRoot, Vector2.zero, CardSize);
        c.group = c.rt.gameObject.AddComponent<CanvasGroup>(); c.group.blocksRaycasts = false;
        c.glow = Img("Glow", c.rt, UpgradeArt.Glow, Vector2.zero, CardSize + new Vector2(180f, 180f), Color.white); c.glow.type = Image.Type.Sliced;
        c.panel = Img("Panel", c.rt, UpgradeArt.Panel, Vector2.zero, CardSize, Body); c.panel.type = Image.Type.Sliced;
        var clip = Node("Clip", c.rt, Vector2.zero, CardSize - new Vector2(6f, 6f)); clip.gameObject.AddComponent<RectMask2D>();
        c.wash = Img("Wash", clip, UpgradeArt.Fade, new Vector2(0f, CardSize.y * 0.25f), new Vector2(CardSize.x, CardSize.y * 0.5f), Color.white);
        c.shine = Img("Shine", clip, null, Vector2.zero, new Vector2(70f, CardSize.y * 1.8f), new Color(1f, 1f, 1f, 0f));
        c.shine.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -24f);
        c.outline = Img("Outline", c.rt, UpgradeArt.Outline, Vector2.zero, CardSize, Color.white); c.outline.type = Image.Type.Sliced;
        c.ribbon = Img("Ribbon", c.rt, UpgradeArt.Panel, new Vector2(0f, CardSize.y * 0.5f - 6f), new Vector2(190f, 42f), Color.white); c.ribbon.type = Image.Type.Sliced;
        c.rarity = Label("Rarity", c.ribbon.rectTransform, Vector2.zero, new Vector2(190f, 42f), 24, Color.black, true);
        c.iconBack = Img("IconBack", c.rt, UpgradeArt.Circle, new Vector2(0f, 100f), new Vector2(150f, 150f), Color.white);
        c.iconRing = Img("IconRing", c.rt, UpgradeArt.Ring, new Vector2(0f, 100f), new Vector2(150f, 150f), Color.white);
        c.icon = Img("Icon", c.rt, null, new Vector2(0f, 100f), new Vector2(104f, 104f), Color.white);
        c.title = Label("Name", c.rt, new Vector2(0f, -18f), new Vector2(300f, 70f), 31, Color.white, true);
        c.title.horizontalOverflow = HorizontalWrapMode.Wrap;
        c.line = Img("Line", c.rt, null, new Vector2(0f, -58f), new Vector2(170f, 2f), Color.white);
        c.text = Label("Text", c.rt, new Vector2(0f, -122f), new Vector2(290f, 110f), 22, Muted, false);
        c.text.horizontalOverflow = HorizontalWrapMode.Wrap; c.text.alignment = TextAnchor.UpperCenter; c.text.lineSpacing = 1.1f;
        c.keyBack = Img("KeyBack", c.rt, UpgradeArt.Panel, new Vector2(0f, -CardSize.y * 0.5f + 34f), new Vector2(54f, 42f), new Color(1f, 1f, 1f, 0.08f)); c.keyBack.type = Image.Type.Sliced;
        c.keyLine = Img("KeyLine", c.keyBack.rectTransform, UpgradeArt.Outline, Vector2.zero, new Vector2(54f, 42f), Color.white); c.keyLine.type = Image.Type.Sliced;
        c.key = Label("Key", c.keyBack.rectTransform, Vector2.zero, new Vector2(54f, 42f), 24, Color.white, true);
        c.rt.gameObject.SetActive(false);
        return c;
    }

    // the cards are laid out from their middles: UiBuild's blocks, centre-anchored
    static RectTransform Node(string name, Transform parent, Vector2 pos, Vector2 size) { return UiBuild.Node(name, parent, UiBuild.Centre, UiBuild.Centre, pos, size); }

    static RectTransform Stretch(string name, Transform parent) { return UiBuild.Stretch(name, parent); }

    static Image Img(string name, Transform parent, Sprite sprite, Vector2 pos, Vector2 size, Color c)
    {
        var img = UiBuild.Box(name, parent, UiBuild.Centre, UiBuild.Centre, pos, size, c);
        img.sprite = sprite;
        return img;
    }

    static Text Label(string name, Transform parent, Vector2 pos, Vector2 size, int fontSize, Color c, bool bold)
    {
        return UiBuild.Label(name, parent, UiBuild.Centre, UiBuild.Centre, pos, size, fontSize, TextAnchor.MiddleCenter, c, bold);
    }

    // ------------------------------------------------------------------ showing
    /// <summary>Puts the cards of an offer on the screen and plays them in.</summary>
    public void Show(IList<Modifier> mods, string headerText, string footerText)
    {
        Build();
        count = Mathf.Min(mods.Count, MaxCards);
        header.text = headerText; titleText.text = "CHOOSE AN UPGRADE"; footer.text = footerText;
        float gap = CardSize.x + 50f;
        for (int i = 0; i < MaxCards; i++)
        {
            var c = cards[i];
            c.rt.gameObject.SetActive(i < count);
            if (i >= count) continue;
            c.mod = mods[i]; c.home = new Vector2((i - (count - 1) * 0.5f) * gap, -8f); c.hover = 0f; c.flash = 0f;
            Fill(c, i);
        }
        screenGroup.gameObject.SetActive(true); screenGroup.alpha = 0f;
        Showing = true; mode = Mode.In; t = 0f; Hovered = -1;
        Animate(0f);
    }

    void Fill(Card c, int i)
    {
        Color k = ModifierCatalog.Tint(c.mod.rarity);
        bool loud = c.mod.rarity >= Rarity.Sweg;
        c.glow.color = new Color(k.r, k.g, k.b, 0.5f);
        c.wash.color = new Color(k.r, k.g, k.b, loud ? 0.42f : 0.3f);
        c.outline.color = Color.Lerp(k, Color.white, 0.15f);
        c.ribbon.color = k;
        c.rarity.text = Spaced(ModifierCatalog.Name(c.mod.rarity));
        c.rarity.color = c.mod.rarity >= Rarity.Sweg ? Color.white : new Color(0.04f, 0.05f, 0.06f, 0.92f);
        c.iconBack.color = new Color(k.r, k.g, k.b, 0.16f);
        c.iconRing.color = new Color(k.r, k.g, k.b, 0.65f);
        c.icon.sprite = UpgradeArt.Icon(c.mod.icon); c.icon.color = Color.Lerp(Color.white, k, 0.3f);
        c.title.text = c.mod.title; c.title.color = c.mod.rarity == Rarity.Slayer ? new Color(1f, 0.82f, 0.78f) : Color.white;
        c.line.color = new Color(k.r, k.g, k.b, 0.55f);
        c.text.text = c.mod.text;
        c.key.text = (i + 1).ToString(); c.keyLine.color = new Color(k.r, k.g, k.b, 0.8f);
        c.shine.color = new Color(1f, 1f, 1f, 0f);
    }

    static string Spaced(string s) { var b = new System.Text.StringBuilder(); foreach (char ch in s) { if (b.Length > 0) b.Append(' '); b.Append(ch); } return b.ToString(); }

    /// <summary>The card taken: it flares and flies off, the others drop; then done.</summary>
    public void PlayPick(int index, Action whenDone) { picked = index; mode = Mode.Pick; t = 0f; done = whenDone; cards[index].flash = 1f; }

    /// <summary>Put off: the cards shrink into the panel on the left; then done.</summary>
    public void PlayLater(Action whenDone) { mode = Mode.Later; t = 0f; done = whenDone; }

    /// <summary>The waiting choices on the left: one row each (oldest first), a dot per card in its rarity colour.</summary>
    public void SetWaiting(IList<Modifier[]> offers, IList<int> waves, string key)
    {
        Build();
        while (rows.Count < offers.Count) rows.Add(MakeRow(rows.Count));
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; bool on = i < offers.Count;
            if (r.rt.gameObject.activeSelf != on) { r.rt.gameObject.SetActive(on); if (on) r.born = Time.unscaledTime; }
            if (!on) continue;
            r.rt.anchoredPosition = new Vector2(0f, -36f - i * 58f);
            r.label.text = "UPGRADE  <size=15><color=#9AA3AE>WAVE " + waves[i] + "</color></size>";
            var mods = offers[i]; Rarity best = Rarity.Common;
            for (int d = 0; d < r.dots.Length; d++)
            {
                bool has = d < mods.Length; r.dots[d].gameObject.SetActive(has);
                if (has) { r.dots[d].color = ModifierCatalog.Tint(mods[d].rarity); if (mods[d].rarity > best) best = mods[d].rarity; }
            }
            Color k = ModifierCatalog.Tint(best);
            r.edge.color = new Color(k.r, k.g, k.b, 0.9f);
        }
        panelTitle.text = offers.Count > 1 ? "UPGRADES READY  x" + offers.Count : "UPGRADE READY";
        panelHint.text = "[" + key + "]  CHOOSE";
        panelHint.rectTransform.anchoredPosition = new Vector2(0f, -36f - offers.Count * 58f + 14f);
        panelRoot.gameObject.SetActive(offers.Count > 0);
    }

    Row MakeRow(int i)
    {
        var r = new Row();
        r.rt = Node("Row" + i, panelRoot, Vector2.zero, new Vector2(300f, 50f));
        r.back = Img("Back", r.rt, UpgradeArt.Panel, Vector2.zero, new Vector2(300f, 50f), new Color(0.06f, 0.065f, 0.08f, 0.88f)); r.back.type = Image.Type.Sliced;
        r.edge = Img("Edge", r.rt, UpgradeArt.Outline, Vector2.zero, new Vector2(300f, 50f), Color.white); r.edge.type = Image.Type.Sliced;
        r.label = Label("Label", r.rt, new Vector2(-40f, 0f), new Vector2(200f, 40f), 20, Color.white, true);
        r.label.alignment = TextAnchor.MiddleLeft; r.label.supportRichText = true;
        r.dots = new Image[MaxCards];
        for (int d = 0; d < MaxCards; d++) r.dots[d] = Img("Dot" + d, r.rt, UpgradeArt.Circle, new Vector2(88f + d * 19f, 0f), new Vector2(14f, 14f), Color.white);
        return r;
    }

    // ------------------------------------------------------------------ every frame (real time: the game is paused)
    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        t += dt;
        UpdateRows();
        if (mode == Mode.Hidden) return;

        Hovered = -1;
        if (mode == Mode.Idle || mode == Mode.In)
        {
            if (TestHover >= 0 && TestHover < count) Hovered = TestHover;
            else for (int i = 0; i < count; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(cards[i].rt, Input.mousePosition, null)) Hovered = i;
        }
        for (int i = 0; i < count; i++)
        {
            var c = cards[i];
            c.hover = Mathf.MoveTowards(c.hover, i == Hovered ? 1f : 0f, dt * 7f);
            c.flash = Mathf.MoveTowards(c.flash, 0f, dt * 3f);
        }
        Animate(dt);
    }

    void Animate(float dt)
    {
        float now = Time.unscaledTime;
        switch (mode)
        {
            case Mode.In:
                screenGroup.alpha = Mathf.Clamp01(t / 0.2f);
                if (t > 0.25f + 0.08f * count + 0.1f) mode = Mode.Idle;
                break;
            case Mode.Pick:
                if (t > 0.5f) { Finish(); return; }
                screenGroup.alpha = 1f - Ease(Mathf.Clamp01((t - 0.3f) / 0.2f));
                break;
            case Mode.Later:
                if (t > 0.45f) { Finish(); return; }
                screenGroup.alpha = 1f - Ease(Mathf.Clamp01((t - 0.1f) / 0.35f));
                break;
        }

        for (int i = 0; i < count; i++)
        {
            var c = cards[i];
            Color k = ModifierCatalog.Tint(c.mod.rarity);
            bool loud = c.mod.rarity >= Rarity.Sweg, slayer = c.mod.rarity == Rarity.Slayer;
            Vector2 pos = c.home; float scale = 1f, alpha = 1f;

            // in: one after another, up from below with a little overshoot
            float a = Mathf.Clamp01((t - 0.08f * i) / 0.32f);
            if (mode == Mode.In) { pos.y -= (1f - a) * 90f; scale = Mathf.LerpUnclamped(0.82f, 1f, Back(a)); alpha = a; }

            // hovered: lifted and a little bigger
            float h = Ease(c.hover);
            pos.y += h * 14f; scale *= 1f + 0.05f * h;

            if (mode == Mode.Pick)
            {
                if (i == picked)
                {
                    float up = Ease(Mathf.Clamp01((t - 0.18f) / 0.32f));
                   scale *= 1.06f + 0.08f * Mathf.Sin(Mathf.Clamp01(t / 0.2f) * Mathf.PI) + up * 0.1f;
                    pos.y += up * 160f; alpha = 1f - up;
                }
                else { float d = Ease(Mathf.Clamp01(t / 0.3f)); pos.y -= d * 120f; alpha = 1f - d; scale *= 1f - 0.1f * d; }
            }
            else if (mode == Mode.Later)
            {
                float k2 = Ease(Mathf.Clamp01((t - 0.03f * i) / 0.38f));
                Vector2 spot = cardsRoot.InverseTransformPoint(panelRoot.position);                    // the panel, in the cards' space scale *= Mathf.Lerp(1f, 0.16f, k2); alpha = 1f - k2 * k2;
            }

            if (slayer && mode != Mode.Later) pos += new Vector2(Mathf.PerlinNoise(now * 9f, i) - 0.5f, Mathf.PerlinNoise(i, now * 9f) - 0.5f) * 2.4f;   // it trembles
            c.rt.anchoredPosition = pos; c.rt.localScale = Vector3.one * scale; c.group.alpha = alpha;

            // the glow breathes; louder for the high rarities and under the mouse
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * (loud ? 3.4f : 2.2f) + i * 1.7f);
            bool plain = c.mod.rarity == Rarity.Common;
            float ga = (loud ? 0.62f : plain ? 0.22f : 0.42f) + (loud ? 0.25f : 0.12f) * pulse + 0.3f * h + 0.6f * c.flash;
            Color gc = slayer ? Color.Lerp(k, new Color(1f, 0.55f, 0.1f), 0.35f * pulse) : k;
            c.glow.color = new Color(gc.r, gc.g, gc.b, Mathf.Clamp01(ga));
            float gs = 1f + (loud ? 0.05f : 0.02f) * pulse + 0.04f * h;
            c.glow.rectTransform.localScale = Vector3.one * gs;
            c.outline.color = Color.Lerp(Color.Lerp(k, Color.white, 0.15f), Color.white, 0.35f * h + 0.8f * c.flash);
            c.panel.color = Color.Lerp(Body, new Color(k.r * 0.35f, k.g * 0.35f, k.b * 0.35f, 1f), 0.25f * h + 0.6f * c.flash);
            c.keyBack.color = new Color(k.r, k.g, k.b, 0.08f + 0.25f * h);

            // a light sweeps over the SWEG and SLAYER cards now and then
            if (loud)
            {
                float period = slayer ? 1.6f : 2.4f, s = Mathf.Repeat(now + i * 0.37f, period) / 0.7f;
                bool on = s < 1f;
                c.shine.color = new Color(1f, 1f, 1f, on ? 0.16f * Mathf.Sin(s * Mathf.PI) : 0f);
                c.shine.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-CardSize.x, CardSize.x, s), 0f);
            }
        }
    }

    void UpdateRows()
    {
        if (!panelRoot.gameObject.activeSelf) return;
        float now = Time.unscaledTime;
        panelGroup.alpha = Showing ? 0.25f : 1f;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; if (!r.rt.gameObject.activeSelf) continue;
            float a = Ease(Mathf.Clamp01((now - r.born) / 0.35f));
            r.rt.anchoredPosition = new Vector2(Mathf.Lerp(-300f, 0f, a), r.rt.anchoredPosition.y);
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 3f + i);
            Color e = r.edge.color; e.a = 0.55f + 0.35f * pulse; r.edge.color = e;
        }
        Color hc = panelHint.color; hc.a = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(now * 4f)); panelHint.color = hc;
    }

    void Finish()
    {
        mode = Mode.Hidden; Showing = false; Hovered = -1;
        screenGroup.gameObject.SetActive(false);
        var d = done; done = null;
        if (d != null) d();
    }

    static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    static float Back(float x) { const float c1 = 1.7f, c3 = c1 + 1f; x = Mathf.Clamp01(x) - 1f; return 1f + c3 * x * x * x + c1 * x * x; }
}

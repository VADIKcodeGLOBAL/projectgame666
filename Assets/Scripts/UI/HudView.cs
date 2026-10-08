using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD as a uGUI Canvas (Screen Space Overlay, scaled from 1920x1080): only references to its parts, filled by SurvivalHud.
/// Create() builds the whole hierarchy — the editor bakes it into the level (command "hud", the map generator), so it can be
/// seen and moved in the Scene view; SurvivalHud builds one at run time if the level has none.
/// Each corner is a nested Canvas: a changing number rebuilds only its own corner, not the whole HUD.
/// </summary>
public class HudView : MonoBehaviour
{
    [Header("Scope (sniper)")]
    public GameObject scopeRoot;
    public RawImage scope;
    public RectTransform scopeLeft, scopeRight;
    public GameObject scopeHit;

    [Header("Top")]
    public GameObject waveGroup;
    public Text wave, clock, info;
    public RectTransform progressFill;
    public Image progressFillImage;
    public GameObject prepGroup;
    public Text head, starts;

    [Header("Middle")]
    public GameObject warning;
    public CanvasGroup pickupGroup;
    public Text pickup;
    public GameObject crosshair;
    public Image[] crosshairBars;

    [Header("Bottom left")]
    public RectTransform hpFill;
    public Image hpFillImage;
    public Text hp;
    public GameObject boost;
    public RectTransform boostFill;
    public Text boostText;
    public GameObject ammo;
    public Text weaponName, rounds, magSize, mags, magTitle, status;
    public Image[] magIcons;
    public GameObject reload;
    public RectTransform reloadFill;
    public Text help;

    [Header("Bottom right")]
    public RectTransform slotsRoot;
    public Image[] slotBoxes;
    public Text[] slotTexts;
    public Text kills;

    [Header("Cannon")]
    public GameObject cannonGroup;
    public Image[] cannonBars;
    public Text cannonInfo, cannonStatus, cannonHint;
    public GameObject cannonReload;
    public RectTransform cannonReloadFill;
    public Text prompt;

    [Header("End screen")]
    public GameObject endScreen;
    public Text endTitle, endReason, endStats;

    public const int MaxMagIcons = 12;
    public static readonly Color Gold = new Color(1f, 0.85f, 0.4f), Grey = new Color(0.85f, 0.85f, 0.85f), Warn = new Color(1f, 0.3f, 0.2f);

    // ------------------------------------------------------------------ building
    static Font font;
    static Font UiFont { get { if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return font; } }

    static RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    static RectTransform Stretch(string name, Transform parent)
    {
        var rt = Node(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        return rt;
    }

    /// <summary>A nested canvas: its children are batched and rebuilt on their own.</summary>
    static RectTransform Layer(string name, Transform parent)
    {
        var rt = Stretch(name, parent);
        rt.gameObject.AddComponent<Canvas>();
        return rt;
    }

    static Image Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
    {
        var img = Node(name, parent, anchor, pivot, pos, size).gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    /// <summary>Bar background with a fill child whose right edge (anchorMax.x) is the value.</summary>
    static RectTransform Bar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color back, Color fill, float inset, out Image fillImage)
    {
        var bg = Box(name, parent, anchor, pivot, pos, size, back);
        var f = Stretch("Fill", bg.transform);
        f.offsetMin = new Vector2(inset, inset); f.offsetMax = new Vector2(-inset, -inset);
        fillImage = f.gameObject.AddComponent<Image>(); fillImage.color = fill; fillImage.raycastTarget = false;
        return f;
    }

    static Text Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Color c, bool bold = true, string text = "")
    {
        var t = Node(name, parent, anchor, pivot, pos, size).gameObject.AddComponent<Text>();
        t.font = UiFont; t.fontSize = fontSize; t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; t.alignment = align; t.color = c;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false; t.text = text;
        var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.7f); sh.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    /// <summary>The whole HUD under parent (a Screen Space Overlay canvas with a 1920x1080 scaler on a new child object).</summary>
    public static HudView Create(Transform parent)
    {
        var rootGo = new GameObject("HUD Canvas", typeof(RectTransform));
        if (parent != null) rootGo.transform.SetParent(parent, false);
        var canvas = rootGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
        var scaler = rootGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;
        var v = rootGo.AddComponent<HudView>();
        Transform root = rootGo.transform;
        Vector2 C = new Vector2(0.5f, 0.5f), TC = new Vector2(0.5f, 1f), BL = Vector2.zero, BR = new Vector2(1f, 0f), TL = new Vector2(0f, 1f);

        // ---- scope: lens texture in a square as high as the screen, black on both sides (first = drawn under everything)
        var scopeL = Layer("Scope", root); v.scopeRoot = scopeL.gameObject;
        var lens = Node("Lens", scopeL, C, C, Vector2.zero, new Vector2(1080f, 1080f));
        lens.anchorMin = new Vector2(0.5f, 0f); lens.anchorMax = new Vector2(0.5f, 1f); lens.sizeDelta = new Vector2(1080f, 0f);   // width = canvas height, set by SurvivalHud
        v.scope = lens.gameObject.AddComponent<RawImage>(); v.scope.raycastTarget = false;
        v.scopeLeft = Box("Left", scopeL, BL, BL, Vector2.zero, Vector2.zero, Color.black).rectTransform;
        v.scopeLeft.anchorMin = Vector2.zero; v.scopeLeft.anchorMax = new Vector2(0.5f, 1f);
        v.scopeRight = Box("Right", scopeL, BR, BR, Vector2.zero, Vector2.zero, Color.black).rectTransform;
        v.scopeRight.anchorMin = new Vector2(0.5f, 0f); v.scopeRight.anchorMax = Vector2.one;
        v.scopeHit = Box("Hit", scopeL, C, C, Vector2.zero, new Vector2(6f, 6f), Warn).gameObject;
        v.scopeRoot.SetActive(false);

        // ---- top: wave, timer, progress, bots / prepare and intermission
        var top = Layer("Top", root);
        var wg = Stretch("Wave", top); v.waveGroup = wg.gameObject;
        v.wave = Label("WaveNumber", wg, TC, TC, new Vector2(0f, -8f), new Vector2(600f, 28f), 22, TextAnchor.MiddleCenter, Gold, true, "WAVE 1 / 5");
        v.clock = Label("Clock", wg, TC, TC, new Vector2(0f, -34f), new Vector2(600f, 60f), 52, TextAnchor.MiddleCenter, Color.white, true, "1:30");
        v.progressFill = Bar("Progress", wg, TC, TC, new Vector2(0f, -98f), new Vector2(320f, 8f), new Color(0f, 0f, 0f, 0.5f), Gold, 2f, out v.progressFillImage);
        v.info = Label("Bots", wg, TC, TC, new Vector2(0f, -110f), new Vector2(900f, 26f), 20, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.9f), true, "bots alive: 0     next 10 bots in 30 s");
        var pg = Stretch("Prepare", top); v.prepGroup = pg.gameObject;
        v.head = Label("Head", pg, TC, TC, new Vector2(0f, -20f), new Vector2(1000f, 60f), 46, TextAnchor.MiddleCenter, Gold, true, "HOLD THE HILL");
        v.starts = Label("Starts", pg, TC, TC, new Vector2(0f, -82f), new Vector2(1000f, 30f), 22, TextAnchor.MiddleCenter, Color.white, true, "wave 1 starts in 6 s  -  stay inside the ring");
        v.prepGroup.SetActive(false);

        // ---- middle: out-of-zone warning, picked-up supply, crosshair
        var mid = Layer("Middle", root);
        var warn = Box("Warning", mid, new Vector2(0.5f, 0.76f), TC, Vector2.zero, new Vector2(0f, 70f), new Color(0.45f, 0f, 0f, 0.30f));
        warn.rectTransform.anchorMin = new Vector2(0f, 0.76f); warn.rectTransform.anchorMax = new Vector2(1f, 0.76f);
        Label("Paused", warn.transform, TC, TC, new Vector2(0f, -4f), new Vector2(1200f, 36f), 22, TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.25f), true, "PROGRESS PAUSED");
        Label("Hint", warn.transform, TC, TC, new Vector2(0f, -36f), new Vector2(1200f, 30f), 20, TextAnchor.MiddleCenter, Color.white, true, "get back into the circle - the bots keep coming");
        v.warning = warn.gameObject; v.warning.SetActive(false);
        var pk = Node("Pickup", mid, new Vector2(0.5f, 0.38f), C, Vector2.zero, new Vector2(1200f, 30f));
        v.pickupGroup = pk.gameObject.AddComponent<CanvasGroup>(); v.pickupGroup.alpha = 0f; v.pickupGroup.blocksRaycasts = false; v.pickupGroup.interactable = false;
        v.pickup = Label("Text", pk, C, C, Vector2.zero, new Vector2(1200f, 30f), 22, TextAnchor.MiddleCenter, Gold);
        var ch = Layer("Crosshair", root); v.crosshair = ch.gameObject;          // its own canvas: the hit colour flashes often
        Color cw = new Color(1f, 1f, 1f, 0.85f);
        v.crosshairBars = new[] {
            Box("L", ch, C, C, new Vector2(-6f, 0f), new Vector2(6f, 2f), cw), Box("R", ch, C, C, new Vector2(6f, 0f), new Vector2(6f, 2f), cw),
            Box("U", ch, C, C, new Vector2(0f, 6f), new Vector2(2f, 6f), cw), Box("D", ch, C, C, new Vector2(0f, -6f), new Vector2(2f, 6f), cw) };

        // ---- bottom left: health, speed boost, ammo, controls
        var bl = Layer("BottomLeft", root);
        v.hpFill = Bar("Health", bl, BL, BL, new Vector2(24f, 24f), new Vector2(284f, 26f), new Color(0f, 0f, 0f, 0.55f), new Color(0.3f, 0.9f, 0.35f), 2f, out v.hpFillImage);
        v.hp = Label("HP", v.hpFill.parent, C, C, Vector2.zero, new Vector2(284f, 26f), 20, TextAnchor.MiddleCenter, Color.white, true, "HP 100");
        Image boostFillImage;
        v.boostFill = Bar("Speed", bl, BL, BL, new Vector2(316f, 24f), new Vector2(150f, 26f), new Color(0f, 0f, 0f, 0.55f), new Color(0.25f, 0.95f, 0.8f, 0.9f), 2f, out boostFillImage);
        v.boost = v.boostFill.parent.gameObject;
        v.boostText = Label("Text", v.boost.transform, C, C, Vector2.zero, new Vector2(150f, 26f), 15, TextAnchor.MiddleCenter, Color.white, true, "SPEED 10.0 s");
        v.boost.SetActive(false);
        var am = Box("Ammo", bl, BL, BL, new Vector2(24f, 58f), new Vector2(284f, 108f), new Color(0f, 0f, 0f, 0.45f));
        v.ammo = am.gameObject; Transform a = am.transform;
        v.weaponName = Label("Weapon", a, TL, TL, new Vector2(12f, -4f), new Vector2(260f, 22f), 15, TextAnchor.MiddleLeft, Gold, true, "AK-47");
        v.rounds = Label("Rounds", a, TL, TL, new Vector2(12f, -22f), new Vector2(110f, 46f), 40, TextAnchor.MiddleLeft, Color.white, true, "30");
        v.magSize = Label("MagazineSize", a, TL, TL, new Vector2(96f, -38f), new Vector2(80f, 26f), 15, TextAnchor.MiddleLeft, Grey, true, "/ 30");
        v.magTitle = Label("MagazinesTitle", a, TL, TL, new Vector2(168f, -24f), new Vector2(110f, 22f), 15, TextAnchor.MiddleLeft, Grey, true, "MAGAZINES");
        v.mags = Label("Magazines", a, TL, TL, new Vector2(168f, -42f), new Vector2(110f, 30f), 20, TextAnchor.MiddleCenter, Color.white, true, "x 4");
        v.magIcons = new Image[MaxMagIcons];
        for (int i = 0; i < MaxMagIcons; i++) v.magIcons[i] = Box("Mag" + i, a, TL, TL, new Vector2(12f + i * 13f, -72f), new Vector2(9f, 14f), new Color(1f, 0.85f, 0.4f, 0.95f));
        Image reloadFillImage;
        v.reloadFill = Bar("Reload", a, TL, TL, new Vector2(12f, -94f), new Vector2(170f, 6f), new Color(0f, 0f, 0f, 0.6f), Gold, 0f, out reloadFillImage);
        v.reload = v.reloadFill.parent.gameObject;
        Label("Text", v.reload.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(178f, 0f), new Vector2(90f, 18f), 13, TextAnchor.MiddleLeft, Color.white, true, "RELOADING");
        v.reload.SetActive(false);
        v.status = Label("Status", a, TL, TL, new Vector2(12f, -88f), new Vector2(260f, 18f), 14, TextAnchor.MiddleLeft, new Color(1f, 0.4f, 0.3f), true, "");
        v.help = Label("Controls", bl, BL, BL, new Vector2(24f, 2f), new Vector2(1600f, 20f), 15, TextAnchor.MiddleLeft, Color.white, false,
            "LMB - capture mouse / fire / slash    R - reload    RMB - scope / heavy chop    1-5 / wheel - weapon    WASD - move    Shift - sprint    Space - jump    Esc - menu / settings");

        // ---- bottom right: weapon slots (filled by SurvivalHud) and kills
        var br = Layer("BottomRight", root);
        v.slotsRoot = Node("Slots", br, BR, BR, new Vector2(-24f, 60f), new Vector2(300f, 24f));
        v.kills = Label("Kills", br, BR, BR, new Vector2(-24f, 24f), new Vector2(300f, 28f), 20, TextAnchor.MiddleCenter, Color.white, true, "KILLS 0");

        // ---- cannon: its sight (the crosshair is where the ball lands), readouts, reload; the "use it" prompt
        var cn = Layer("Cannon", root); v.cannonGroup = cn.gameObject;
        Color cg = new Color(0.45f, 1f, 0.5f, 0.95f);
        var bars = new System.Collections.Generic.List<Image>();
        foreach (var d in new[] { new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) })
        {
            Vector2 sz = d.x != 0f ? new Vector2(16f, 3f) : new Vector2(3f, 16f);
            bars.Add(Box("Bar", cn, C, C, d * 22f, sz, cg));
            bars.Add(Box("Tick", cn, C, C, d * 44f, d.x != 0f ? new Vector2(8f, 2f) : new Vector2(2f, 8f), cg));
        }
        bars.Add(Box("Dot", cn, C, C, Vector2.zero, new Vector2(4f, 4f), cg));
        v.cannonBars = bars.ToArray();
        v.cannonStatus = Label("Status", cn, C, TC, new Vector2(0f, -62f), new Vector2(600f, 26f), 20, TextAnchor.MiddleCenter, cg, true, "READY");
        v.cannonInfo = Label("Info", cn, C, TC, new Vector2(0f, -88f), new Vector2(900f, 24f), 17, TextAnchor.MiddleCenter, Color.white, true, "RANGE 0 m     ELEV 0.0°     FLIGHT 0.0 s");
        Image cannonFillImage;
        v.cannonReloadFill = Bar("Reload", cn, C, TC, new Vector2(0f, -116f), new Vector2(220f, 6f), new Color(0f, 0f, 0f, 0.6f), Gold, 0f, out cannonFillImage);
        v.cannonReload = v.cannonReloadFill.parent.gameObject;
        v.cannonHint = Label("Hint", cn, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(1200f, 24f), 17, TextAnchor.MiddleCenter, Color.white, true,
            "LMB - fire     RMB - zoom     mouse - aim     E - leave the cannon");
        v.cannonGroup.SetActive(false);
        var pr = Layer("Prompt", root);
        v.prompt = Label("Text", pr, new Vector2(0.5f, 0.3f), C, Vector2.zero, new Vector2(1400f, 30f), 21, TextAnchor.MiddleCenter, Gold, true, "");
        v.prompt.gameObject.SetActive(false);

        // ---- end screen
        var end = Layer("EndScreen", root); v.endScreen = end.gameObject;
        var dim = end.gameObject.AddComponent<Image>(); dim.color = new Color(0f, 0f, 0f, 0.55f); dim.raycastTarget = false;
        v.endTitle = Label("Title", end, new Vector2(0.5f, 0.70f), TC, Vector2.zero, new Vector2(1600f, 80f), 64, TextAnchor.MiddleCenter, Warn, true, "GAME OVER");
        v.endReason = Label("Reason", end, new Vector2(0.5f, 0.70f), TC, new Vector2(0f, -84f), new Vector2(1600f, 30f), 20, TextAnchor.MiddleCenter, Color.white);
        v.endStats = Label("Stats", end, new Vector2(0.5f, 0.70f), TC, new Vector2(0f, -118f), new Vector2(1600f, 30f), 20, TextAnchor.MiddleCenter, Color.white);
        Label("Restart", end, new Vector2(0.5f, 0.70f), TC, new Vector2(0f, -160f), new Vector2(1600f, 30f), 20, TextAnchor.MiddleCenter, Gold, true, "R - restart");
        v.endScreen.SetActive(false);
        return v;
    }

    /// <summary>One slot per weapon ("1 AK-47"); rebuilt only when the number of weapons changes.</summary>
    public void BuildSlots(string[] names)
    {
        if (slotTexts != null && slotTexts.Length == names.Length)
        {
            for (int i = 0; i < names.Length; i++) if (slotTexts[i] != null && slotTexts[i].text != names[i]) slotTexts[i].text = names[i];
            return;
        }
        for (int i = slotsRoot.childCount - 1; i >= 0; i--)
        {
            var c = slotsRoot.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
        int n = names.Length; float cw = 300f / Mathf.Max(1, n);
        slotBoxes = new Image[n]; slotTexts = new Text[n];
        var left = Vector2.zero;
        for (int i = 0; i < n; i++)
        {
            slotBoxes[i] = Box("Slot" + (i + 1), slotsRoot, left, left, new Vector2(i * cw, 0f), new Vector2(cw - 4f, 24f), new Color(0f, 0f, 0f, 0.35f));
            slotTexts[i] = Label("Text", slotBoxes[i].transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cw - 4f, 24f), 14, TextAnchor.MiddleCenter, Color.white, true, names[i]);
        }
    }
}

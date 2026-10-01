using UnityEngine;

/// <summary>
/// HUD of the wave survival mode: wave timer, next batch, bots alive, health and the speed boost, ammo (rounds and spare
/// magazines, bottom left), weapon slots, score, supplies picked up, crosshair or the sniper scope, warnings and end screens.
/// Draws on the Repaint event only (no layout pass) and rebuilds a text only when its numbers change, so it makes no garbage per frame.
/// </summary>
public class SurvivalHud : MonoBehaviour
{
    public WaveSurvivalGame game;
    public PlayerHealth health;
    public WeaponInventory weapons;

    /// <summary>A text rebuilt only when its key (the numbers it shows) changes.</summary>
    sealed class TextSlot
    {
        long key = long.MinValue;
        public string Text = "";
        public bool Changed(long k) { if (k == key) return false; key = k; return true; }
    }

    GUIStyle mid, big, huge, small, ammoBig, ammoSmall, slot;
    SimpleFirstPersonController fp;
    Texture2D scopeTex;
    readonly TextSlot tWave = new TextSlot(), tClock = new TextSlot(), tInfo = new TextSlot(), tHead = new TextSlot(), tStarts = new TextSlot(),
                      tHp = new TextSlot(), tKills = new TextSlot(), tRounds = new TextSlot(), tMagSize = new TextSlot(), tMags = new TextSlot(),
                      tName = new TextSlot(), tBoost = new TextSlot(), tEnd = new TextSlot();
    string[] slotNames;

    const string Help = "LMB - capture mouse / fire    R - reload    RMB - scope    1-4 / wheel - weapon    WASD - move    Shift - sprint    Space - jump    Esc - menu / settings";
    static readonly Color Gold = new Color(1f, 0.85f, 0.4f), Grey = new Color(0.85f, 0.85f, 0.85f), Warn = new Color(1f, 0.3f, 0.2f);

    void Awake() { useGUILayout = false; }                       // GUI.* only: skip the Layout event

    void OnDestroy() { if (scopeTex != null) Destroy(scopeTex); }  // a new HUD comes with every restart

    static void Box(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

    /// <summary>Scope picture: black outside the lens, a darker rim inside it, thin cross hairs with thick outer posts and mil dots.</summary>
    static Texture2D MakeScope(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f, lens = n * 0.49f, px1 = n / 1024f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((d - lens) / (2f * px1));                              // outside the lens
                a = Mathf.Max(a, Mathf.Pow(Mathf.Clamp01((d - lens * 0.80f) / (lens * 0.20f)), 2.2f) * 0.85f);   // rim shadow
                float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                bool post = (ay < 5f * px1 && ax > lens * 0.55f) || (ax < 5f * px1 && ay > lens * 0.55f);
                bool hair = (ay < 1.2f * px1 && ax > 10f * px1) || (ax < 1.2f * px1 && ay > 10f * px1);
                bool dot = false;
                for (int k = 1; k <= 4 && !dot; k++)
                {
                    float m = k * lens * 0.11f, r = 3.2f * px1;
                    dot = (Mathf.Abs(ax - m) < r && ay < r) || (Mathf.Abs(ay - m) < r && ax < r);
                }
                if ((post || hair || dot) && d < lens) a = 1f;
                px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);                 // no CPU copy kept
        return tex;
    }

    static void Shadowed(Rect r, string text, GUIStyle st, Color c)
    {
        var old = st.normal.textColor;
        st.normal.textColor = new Color(0f, 0f, 0f, 0.7f); GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, st);
        st.normal.textColor = c; GUI.Label(r, text, st);
        st.normal.textColor = old;
    }

    void DrawAmmo(float h)
    {
        var w = weapons != null ? weapons.Current : null;
        if (w == null) return;
        float x = 24f, y = h - 164f;
        Box(new Rect(x, y, 284f, 108f), new Color(0f, 0f, 0f, 0.45f));
        if (tName.Changed(weapons.CurrentIndex)) tName.Text = w.displayName.ToUpperInvariant();
        Shadowed(new Rect(x + 12f, y + 4f, 260f, 22f), tName.Text, ammoSmall, Gold);

        // rounds in the magazine / magazine size
        Color rc = w.InMagazine == 0 ? Warn : w.InMagazine <= Mathf.Max(1, w.magazineSize / 4) ? new Color(1f, 0.7f, 0.3f) : Color.white;
        if (tRounds.Changed(w.InMagazine)) tRounds.Text = w.InMagazine.ToString();
        if (tMagSize.Changed(w.magazineSize)) tMagSize.Text = "/ " + w.magazineSize;
        Shadowed(new Rect(x + 12f, y + 22f, 110f, 46f), tRounds.Text, ammoBig, rc);
        Shadowed(new Rect(x + 96f, y + 38f, 80f, 26f), tMagSize.Text, ammoSmall, Grey);

        // spare magazines: one icon each, and the number
        int mags = w.Magazines;
        if (tMags.Changed(mags)) tMags.Text = mags < 0 ? "x ∞" : "x " + mags;
        Shadowed(new Rect(x + 168f, y + 24f, 110f, 22f), "MAGAZINES", ammoSmall, Grey);
        Shadowed(new Rect(x + 168f, y + 42f, 110f, 30f), tMags.Text, mid, mags == 0 ? Warn : Color.white);
        if (mags > 0)
            for (int i = 0; i < w.maxMagazines; i++)
                Box(new Rect(x + 12f + i * 13f, y + 72f, 9f, 14f), i < mags ? new Color(1f, 0.85f, 0.4f, 0.95f) : new Color(1f, 1f, 1f, 0.15f));

        if (w.IsReloading)
        {
            Box(new Rect(x + 12f, y + 94f, 170f, 6f), new Color(0f, 0f, 0f, 0.6f));
            Box(new Rect(x + 12f, y + 94f, 170f * w.ReloadProgress, 6f), Gold);
            Shadowed(new Rect(x + 190f, y + 88f, 90f, 18f), "RELOADING", small, Color.white);
        }
        else if (w.InMagazine == 0)
            Shadowed(new Rect(x + 12f, y + 88f, 260f, 18f), w.HasSpare ? "R - RELOAD" : "NO AMMO - FIND MAGAZINES", small, new Color(1f, 0.4f, 0.3f));
    }

    /// <summary>Speed syringe: a bar that runs down next to the health bar.</summary>
    void DrawBoost(float h)
    {
        if (fp == null && health != null) fp = health.GetComponent<SimpleFirstPersonController>();
        if (fp == null || fp.BoostTimeLeft <= 0f) return;
        float left = fp.BoostTimeLeft, k = left / Mathf.Max(0.01f, fp.BoostDuration);
        int tenths = Mathf.CeilToInt(left * 10f);
        if (tBoost.Changed(tenths)) tBoost.Text = "SPEED " + (tenths / 10) + "." + (tenths % 10) + " s";
        Box(new Rect(316f, h - 50f, 150f, 26f), new Color(0f, 0f, 0f, 0.55f));
        Box(new Rect(318f, h - 48f, 146f * k, 22f), new Color(0.25f, 0.95f, 0.8f, 0.9f));
        Shadowed(new Rect(316f, h - 50f, 150f, 26f), tBoost.Text, slot, Color.white);
    }

    void DrawSlots(float w, float h)
    {
        if (weapons == null || weapons.weapons == null) return;
        int n = weapons.weapons.Length;
        if (slotNames == null || slotNames.Length != n)
        {
            slotNames = new string[n];
            for (int i = 0; i < n; i++) slotNames[i] = (i + 1) + " " + (weapons.weapons[i] != null ? weapons.weapons[i].displayName : "-");
        }
        float x = w - 324f, y = h - 84f, cw = 300f / Mathf.Max(1, n);
        for (int i = 0; i < n; i++)
        {
            bool cur = i == weapons.CurrentIndex;
            Box(new Rect(x + i * cw, y, cw - 4f, 24f), cur ? new Color(1f, 0.85f, 0.4f, 0.35f) : new Color(0f, 0f, 0f, 0.35f));
            Shadowed(new Rect(x + i * cw, y, cw - 4f, 24f), slotNames[i], slot, cur ? Color.white : new Color(0.75f, 0.75f, 0.75f));
        }
    }

    void OnGUI()
    {
        if (game == null || Event.current.type != EventType.Repaint) return;   // nothing to click here: draw once per frame
        if (mid == null)
        {
            mid = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            big = new GUIStyle(mid) { fontSize = 44 };
            huge = new GUIStyle(mid) { fontSize = 64 };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            ammoBig = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            ammoSmall = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            slot = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
        float w = Screen.width, h = Screen.height, cx = w * 0.5f;

        // ---- sniper scope: under the rest of the HUD
        bool scoped = weapons != null && weapons.IsScoped && !game.IsOver;
        if (scoped)
        {
            if (scopeTex == null) scopeTex = MakeScope(1024);
            float s = h, left = cx - s * 0.5f;
            GUI.DrawTexture(new Rect(left, 0f, s, s), scopeTex);
            Box(new Rect(0f, 0f, left + 1f, h), Color.black); Box(new Rect(left + s - 1f, 0f, w - left - s + 1f, h), Color.black);
            if (Time.time - weapons.LastHitTime < 0.15f) Box(new Rect(cx - 3f, h * 0.5f - 3f, 6f, 6f), Warn);
        }

        // ---- top: wave and timer
        if (game.State == WaveSurvivalGame.GameState.Wave)
        {
            int secs = Mathf.CeilToInt(Mathf.Max(0f, game.TimeLeft));
            if (tWave.Changed(game.Wave * 1000L + game.waves)) tWave.Text = "WAVE " + game.Wave + " / " + game.waves;
            if (tClock.Changed(secs)) tClock.Text = (secs / 60) + ":" + (secs % 60).ToString("00");
            int nextIn = Mathf.CeilToInt(game.NextBatchIn);
            if (tInfo.Changed(((long)game.Alive << 40) | ((long)game.BatchSize << 20) | (uint)nextIn))
                tInfo.Text = "bots alive: " + game.Alive + "     next " + game.BatchSize + " bots in " + nextIn + " s";
            Shadowed(new Rect(cx - 300f, 8f, 600f, 28f), tWave.Text, mid, Gold);
            Color tc = !game.InZone ? new Color(0.6f, 0.6f, 0.6f) : game.TimeLeft < 10f ? new Color(0.5f, 1f, 0.5f) : Color.white;
            Shadowed(new Rect(cx - 300f, 32f, 600f, 56f), tClock.Text, big, tc);
            Box(new Rect(cx - 160f, 92f, 320f, 8f), new Color(0f, 0f, 0f, 0.5f));
            Box(new Rect(cx - 158f, 94f, 316f * game.WaveProgress, 4f), game.InZone ? Gold : new Color(0.55f, 0.55f, 0.55f));
            Shadowed(new Rect(cx - 300f, 102f, 600f, 24f), tInfo.Text, mid, new Color(0.9f, 0.9f, 0.9f));
        }
        else if (game.State == WaveSurvivalGame.GameState.Prepare || game.State == WaveSurvivalGame.GameState.Intermission)
        {
            bool prep = game.State == WaveSurvivalGame.GameState.Prepare;
            int startsIn = Mathf.CeilToInt(game.TimeLeft);
            if (tHead.Changed(prep ? -1 : game.Wave)) tHead.Text = prep ? "HOLD THE HILL" : "WAVE " + game.Wave + " SURVIVED";
            if (tStarts.Changed(game.Wave * 100000L + startsIn)) tStarts.Text = "wave " + (game.Wave + 1) + " starts in " + startsIn + " s  -  stay inside the ring";
            Shadowed(new Rect(cx - 400f, 20f, 800f, 56f), tHead.Text, big, Gold);
            Shadowed(new Rect(cx - 400f, 78f, 800f, 28f), tStarts.Text, mid, Color.white);
        }

        // ---- bottom left: health, bottom right: score
        if (health != null)
        {
            float k = Mathf.Clamp01(health.Health / Mathf.Max(1f, health.maxHealth));
            int hp = Mathf.CeilToInt(health.Health);
            if (tHp.Changed(hp)) tHp.Text = "HP " + hp;
            Box(new Rect(24f, h - 50f, 284f, 26f), new Color(0f, 0f, 0f, 0.55f));
            Box(new Rect(26f, h - 48f, 280f * k, 22f), Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.3f, 0.9f, 0.35f), k));
            Shadowed(new Rect(24f, h - 50f, 284f, 26f), tHp.Text, mid, Color.white);
        }
        DrawAmmo(h);
        DrawSlots(w, h);
        if (tKills.Changed(game.Kills)) tKills.Text = "KILLS " + game.Kills;
        Shadowed(new Rect(w - 324f, h - 52f, 300f, 28f), tKills.Text, mid, Color.white);
        GUI.Label(new Rect(24f, h - 22f, 1200f, 20f), Help, small);
        float since = Time.time - SupplyPickup.LastTime;
        if (since < 1.8f && SupplyPickup.LastMessage != null)
        {
            Color pc = SupplyPickup.LastKind == SupplyPickup.Kind.Medkit ? new Color(1f, 0.45f, 0.4f) : SupplyPickup.LastKind == SupplyPickup.Kind.Speed ? new Color(0.4f, 1f, 0.85f) : Gold;
            pc.a = 1f - Mathf.Clamp01((since - 1.3f) / 0.5f);
            Shadowed(new Rect(0f, h * 0.62f, w, 28f), SupplyPickup.LastMessage, mid, pc);
        }
        DrawBoost(h);

        // ---- crosshair and hit marker
        if (!game.IsOver && !scoped)
        {
            Color cc = weapons != null && Time.time - weapons.LastHitTime < 0.12f ? Warn : new Color(1f, 1f, 1f, 0.85f);
            Box(new Rect(cx - 9f, h * 0.5f - 1f, 6f, 2f), cc); Box(new Rect(cx + 3f, h * 0.5f - 1f, 6f, 2f), cc);
            Box(new Rect(cx - 1f, h * 0.5f - 9f, 2f, 6f), cc); Box(new Rect(cx - 1f, h * 0.5f + 3f, 2f, 6f), cc);
        }

        // ---- warnings
        if (!game.IsOver && !game.InZone && game.State == WaveSurvivalGame.GameState.Wave)
        {
            Box(new Rect(0f, h * 0.24f, w, 70f), new Color(0.45f, 0f, 0f, 0.30f));
            Shadowed(new Rect(0f, h * 0.24f + 4f, w, 36f), "PROGRESS PAUSED", mid, new Color(1f, 0.35f, 0.25f));
            Shadowed(new Rect(0f, h * 0.24f + 34f, w, 30f), "get back into the circle - the bots keep coming", mid, Color.white);
        }

        // ---- end screens
        if (game.IsOver)
        {
            bool won = game.State == WaveSurvivalGame.GameState.Victory;
            if (tEnd.Changed(game.Wave * 100000L + game.Kills)) tEnd.Text = "wave " + Mathf.Max(1, game.Wave) + " of " + game.waves + "     kills " + game.Kills;
            Box(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.55f));
            Shadowed(new Rect(0f, h * 0.30f, w, 80f), won ? "VICTORY" : "GAME OVER", huge, won ? new Color(0.5f, 1f, 0.5f) : Warn);
            Shadowed(new Rect(0f, h * 0.30f + 84f, w, 30f), game.EndReason, mid, Color.white);
            Shadowed(new Rect(0f, h * 0.30f + 118f, w, 30f), tEnd.Text, mid, Color.white);
            Shadowed(new Rect(0f, h * 0.30f + 160f, w, 30f), "R - restart", mid, Gold);
        }
    }
}

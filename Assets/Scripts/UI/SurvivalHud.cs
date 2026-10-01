using UnityEngine;

/// <summary>
/// HUD of the wave survival mode: wave timer, next batch, bots alive, health, ammo (rounds and spare magazines, bottom left),
/// weapon slots, score, crosshair or the sniper scope, warnings and end screens.
/// </summary>
public class SurvivalHud : MonoBehaviour
{
    public WaveSurvivalGame game;
    public PlayerHealth health;
    public WeaponInventory weapons;

    GUIStyle mid, big, huge, small, ammoBig, ammoSmall, slot;
    Texture2D scopeTex;

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
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    void DrawAmmo(float h)
    {
        var w = weapons != null ? weapons.Current : null;
        if (w == null) return;
        float x = 24f, y = h - 164f;
        Box(new Rect(x, y, 284f, 108f), new Color(0f, 0f, 0f, 0.45f));
        Shadowed(new Rect(x + 12f, y + 4f, 260f, 22f), w.displayName.ToUpperInvariant(), ammoSmall, new Color(1f, 0.85f, 0.4f));

        // rounds in the magazine / magazine size
        Color rc = w.InMagazine == 0 ? new Color(1f, 0.3f, 0.2f) : w.InMagazine <= Mathf.Max(1, w.magazineSize / 4) ? new Color(1f, 0.7f, 0.3f) : Color.white;
        Shadowed(new Rect(x + 12f, y + 22f, 110f, 46f), w.InMagazine.ToString(), ammoBig, rc);
        Shadowed(new Rect(x + 96f, y + 38f, 80f, 26f), "/ " + w.magazineSize, ammoSmall, new Color(0.85f, 0.85f, 0.85f));

        // spare magazines: one icon each, and the number
        int mags = w.Magazines;
        Shadowed(new Rect(x + 168f, y + 24f, 110f, 22f), "MAGAZINES", ammoSmall, new Color(0.85f, 0.85f, 0.85f));
        Shadowed(new Rect(x + 168f, y + 42f, 110f, 30f), mags < 0 ? "x ∞" : "x " + mags, mid, mags == 0 ? new Color(1f, 0.3f, 0.2f) : Color.white);
        if (mags > 0)
            for (int i = 0; i < w.maxMagazines; i++)
                Box(new Rect(x + 12f + i * 13f, y + 72f, 9f, 14f), i < mags ? new Color(1f, 0.85f, 0.4f, 0.95f) : new Color(1f, 1f, 1f, 0.15f));

        if (w.IsReloading)
        {
            Box(new Rect(x + 12f, y + 94f, 170f, 6f), new Color(0f, 0f, 0f, 0.6f));
            Box(new Rect(x + 12f, y + 94f, 170f * w.ReloadProgress, 6f), new Color(1f, 0.85f, 0.4f));
            Shadowed(new Rect(x + 190f, y + 88f, 90f, 18f), "RELOADING", small, Color.white);
        }
        else if (w.InMagazine == 0)
            Shadowed(new Rect(x + 12f, y + 88f, 260f, 18f), w.HasSpare ? "R - RELOAD" : "NO AMMO - FIND MAGAZINES", small, new Color(1f, 0.4f, 0.3f));
    }

    void DrawSlots(float w, float h)
    {
        if (weapons == null || weapons.weapons == null) return;
        float x = w - 324f, y = h - 84f, cw = 300f / Mathf.Max(1, weapons.weapons.Length);
        for (int i = 0; i < weapons.weapons.Length; i++)
        {
            bool cur = i == weapons.CurrentIndex;
            Box(new Rect(x + i * cw, y, cw - 4f, 24f), cur ? new Color(1f, 0.85f, 0.4f, 0.35f) : new Color(0f, 0f, 0f, 0.35f));
            Shadowed(new Rect(x + i * cw, y, cw - 4f, 24f), (i + 1) + " " + weapons.weapons[i].displayName, slot, cur ? Color.white : new Color(0.75f, 0.75f, 0.75f));
        }
    }

    static void Shadowed(Rect r, string text, GUIStyle st, Color c)
    {
        var old = st.normal.textColor;
        st.normal.textColor = new Color(0f, 0f, 0f, 0.7f); GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, st);
        st.normal.textColor = c; GUI.Label(r, text, st);
        st.normal.textColor = old;
    }

    static string Clock(float t) { t = Mathf.Max(0f, t); int s = Mathf.CeilToInt(t); return (s / 60) + ":" + (s % 60).ToString("00"); }

    void OnGUI()
    {
        if (game == null) return;
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
            if (Time.time - weapons.LastHitTime < 0.15f) Box(new Rect(cx - 3f, h * 0.5f - 3f, 6f, 6f), new Color(1f, 0.3f, 0.2f));
        }

        // ---- top: wave and timer
        if (game.State == WaveSurvivalGame.GameState.Wave)
        {
            Shadowed(new Rect(cx - 300f, 8f, 600f, 28f), "WAVE " + game.Wave + " / " + game.waves, mid, new Color(1f, 0.85f, 0.4f));
            Color tc = !game.InZone ? new Color(0.6f, 0.6f, 0.6f) : game.TimeLeft < 10f ? new Color(0.5f, 1f, 0.5f) : Color.white;
            Shadowed(new Rect(cx - 300f, 32f, 600f, 56f), Clock(game.TimeLeft), big, tc);
            Box(new Rect(cx - 160f, 92f, 320f, 8f), new Color(0f, 0f, 0f, 0.5f));
            Box(new Rect(cx - 158f, 94f, 316f * game.WaveProgress, 4f), game.InZone ? new Color(1f, 0.85f, 0.4f) : new Color(0.55f, 0.55f, 0.55f));
            string next = "next " + game.BatchSize + " bots in " + Mathf.CeilToInt(game.NextBatchIn) + " s";
            Shadowed(new Rect(cx - 300f, 102f, 600f, 24f), "bots alive: " + game.Alive + "     " + next, mid, new Color(0.9f, 0.9f, 0.9f));
        }
        else if (game.State == WaveSurvivalGame.GameState.Prepare || game.State == WaveSurvivalGame.GameState.Intermission)
        {
            string head = game.State == WaveSurvivalGame.GameState.Prepare ? "HOLD THE HILL" : "WAVE " + game.Wave + " SURVIVED";
            Shadowed(new Rect(cx - 400f, 20f, 800f, 56f), head, big, new Color(1f, 0.85f, 0.4f));
            Shadowed(new Rect(cx - 400f, 78f, 800f, 28f), "wave " + (game.Wave + 1) + " starts in " + Mathf.CeilToInt(game.TimeLeft) + " s  -  stay inside the ring", mid, Color.white);
        }

        // ---- bottom left: health, bottom right: score
        if (health != null)
        {
            float k = health.Health / health.maxHealth;
            Box(new Rect(24f, h - 50f, 284f, 26f), new Color(0f, 0f, 0f, 0.55f));
            Box(new Rect(26f, h - 48f, 280f * Mathf.Clamp01(k), 22f), Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.3f, 0.9f, 0.35f), k));
            Shadowed(new Rect(24f, h - 50f, 284f, 26f), "HP " + Mathf.CeilToInt(health.Health), mid, Color.white);
        }
        DrawAmmo(h);
        DrawSlots(w, h);
        Shadowed(new Rect(w - 324f, h - 52f, 300f, 28f), "KILLS " + game.Kills, mid, Color.white);
        GUI.Label(new Rect(24f, h - 22f, 1200f, 20f), "LMB - capture mouse / fire    R - reload    RMB - scope    1-4 / wheel - weapon    WASD - move    Shift - sprint    Space - jump    Esc - release mouse", small);
        if (weapons != null && Time.time - weapons.PickupTime < 1.6f)
            Shadowed(new Rect(0f, h * 0.62f, w, 28f), weapons.PickupText, mid, new Color(1f, 0.85f, 0.4f, 1f - Mathf.Clamp01((Time.time - weapons.PickupTime - 1.1f) / 0.5f)));

        // ---- crosshair and hit marker
        if (!game.IsOver && !scoped)
        {
            Color cc = weapons != null && Time.time - weapons.LastHitTime < 0.12f ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 1f, 1f, 0.85f);
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
            Box(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.55f));
            Shadowed(new Rect(0f, h * 0.30f, w, 80f), won ? "VICTORY" : "GAME OVER", huge, won ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.3f, 0.2f));
            Shadowed(new Rect(0f, h * 0.30f + 84f, w, 30f), game.EndReason, mid, Color.white);
            Shadowed(new Rect(0f, h * 0.30f + 118f, w, 30f), "wave " + Mathf.Max(1, game.Wave) + " of " + game.waves + "     kills " + game.Kills, mid, Color.white);
            Shadowed(new Rect(0f, h * 0.30f + 160f, w, 30f), "R - restart", mid, new Color(1f, 0.85f, 0.4f));
        }
    }
}

using UnityEngine;

/// <summary>HUD of the wave survival mode: wave timer, next batch, bots alive, health, score, crosshair, warnings and end screens.</summary>
public class SurvivalHud : MonoBehaviour
{
    public WaveSurvivalGame game;
    public PlayerHealth health;
    public PlayerGun gun;

    GUIStyle mid, big, huge, small;

    static void Box(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

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
        }
        float w = Screen.width, h = Screen.height, cx = w * 0.5f;

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
        Shadowed(new Rect(w - 324f, h - 52f, 300f, 28f), "KILLS " + game.Kills, mid, Color.white);
        GUI.Label(new Rect(24f, h - 22f, 900f, 20f), "LMB - capture mouse / fire    WASD - move    Shift - sprint    Space - jump    Esc - release mouse", small);

        // ---- crosshair and hit marker
        if (!game.IsOver)
        {
            Color cc = gun != null && Time.time - gun.LastHitTime < 0.12f ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 1f, 1f, 0.85f);
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

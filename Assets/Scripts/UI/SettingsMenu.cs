using UnityEngine;

/// <summary>
/// Pause menu with the settings: Esc opens and closes it. While it is open the game is paused (time scale 0)
/// and the mouse is free. Sliders: overall volume, music, shots, field of view, FOV effects, mouse sensitivity;
/// changes apply at once and are saved on close.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    public MusicPlayer music;

    public static bool IsOpen { get; private set; }

    GUIStyle title, label, value, button, note;
    float timeScaleBefore = 1f;

    void Awake() { IsOpen = false; Time.timeScale = 1f; useGUILayout = false; }   // GUI.* only: skip the Layout event
    void OnDestroy() { if (IsOpen) { IsOpen = false; Time.timeScale = 1f; } }

    void Update()
    {
        if (UpgradeSystem.IsChoosing) return;                             // the upgrade choice has the screen
        if (Input.GetKeyDown(KeyCode.Escape)) { if (IsOpen) Close(); else Open(); }
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true; timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f; Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false; Time.timeScale = timeScaleBefore;
        GameSettings.Save();
        var game = WaveSurvivalGame.Instance;
        if (game == null || !game.IsOver) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    static void Box(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

    float Slider(float y, string name, float v, float min, float max, string shown)
    {
        float x = Screen.width * 0.5f - 230f;
        GUI.Label(new Rect(x + 24f, y, 170f, 26f), name, label);
        float nv = GUI.HorizontalSlider(new Rect(x + 190f, y + 8f, 180f, 18f), v, min, max);
        GUI.Label(new Rect(x + 380f, y, 70f, 26f), shown, value);
        return nv;
    }

    static string Pct(float v) { return Mathf.RoundToInt(v * 100f) + " %"; }

    void OnGUI()
    {
        if (!IsOpen) return;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            label = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
            value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            note = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }
        GUI.depth = -10;
        float w = Screen.width, h = Screen.height, px = w * 0.5f - 230f, py = h * 0.5f - 246f;
        Box(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.55f));
        Box(new Rect(px, py, 460f, 492f), new Color(0.08f, 0.09f, 0.11f, 0.95f));
        Box(new Rect(px, py, 460f, 4f), new Color(1f, 0.85f, 0.4f));
        GUI.Label(new Rect(px, py + 14f, 460f, 40f), "SETTINGS", title);

        float y = py + 76f;
        GameSettings.MasterVolume = Slider(y, "Overall volume", GameSettings.MasterVolume, 0f, 1f, Pct(GameSettings.MasterVolume)); y += 46f;
        GameSettings.MusicVolume = Slider(y, "Music", GameSettings.MusicVolume, 0f, 1f, Pct(GameSettings.MusicVolume)); y += 46f;
        GameSettings.ShotVolume = Slider(y, "Shot sounds", GameSettings.ShotVolume, 0f, 1f, Pct(GameSettings.ShotVolume)); y += 46f;
        GameSettings.Fov = Mathf.Round(Slider(y, "Field of view", GameSettings.Fov, GameSettings.MinFov, GameSettings.MaxFov, Mathf.RoundToInt(GameSettings.Fov) + "°")); y += 46f;
        // 5 % steps, so 0 (off) and the default 100 % are easy to hit
        GameSettings.FovEffects = Mathf.Round(Slider(y, "FOV effects", GameSettings.FovEffects, 0f, GameSettings.MaxFovEffects, GameSettings.FovEffects <= 0f ? "Off" : Pct(GameSettings.FovEffects)) * 20f) / 20f; y += 46f;
        // 5 % steps, so the default 100 % is easy to hit again
        GameSettings.MouseSensitivity = Mathf.Round(Slider(y, "Mouse sensitivity", GameSettings.MouseSensitivity, GameSettings.MinMouse, GameSettings.MaxMouse, Pct(GameSettings.MouseSensitivity)) * 20f) / 20f; y += 46f;

        string tracks = music == null ? "no music player" : music.TrackCount == 0
            ? "No music yet: put tracks into Assets/Audio/Music"
            : music.TrackCount + " tracks" + (music.CurrentTrack.Length > 0 ? ", playing: " + music.CurrentTrack : "");
        GUI.Label(new Rect(px + 20f, y, 420f, 34f), tracks, note); y += 44f;

        if (GUI.Button(new Rect(px + 24f, y, 196f, 40f), "Resume (Esc)", button)) Close();
        if (GUI.Button(new Rect(px + 240f, y, 196f, 40f), "Defaults", button)) GameSettings.ResetToDefaults();
    }
}

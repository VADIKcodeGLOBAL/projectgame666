using UnityEngine;

/// <summary>
/// Player settings, kept in PlayerPrefs: overall volume, music volume, shot volume, field of view, mouse sensitivity.
/// Read by AudioListener (overall), MusicPlayer, WeaponInventory (shots and FOV), SimpleFirstPersonController and FieldCannon (mouse).
/// Changed from SettingsMenu.
/// </summary>
public static class GameSettings
{
    public const float DefaultMaster = 1f, DefaultMusic = 0.6f, DefaultShots = 1f, DefaultFov = 75f, DefaultMouse = 1f;
    public const float MinFov = 60f, MaxFov = 110f, MinMouse = 0.1f, MaxMouse = 3f;

    const string KeyMaster = "settings.volume.master", KeyMusic = "settings.volume.music", KeyShots = "settings.volume.shots", KeyFov = "settings.fov";
    const string KeyMouse = "settings.mouse.sensitivity";

    static float master = DefaultMaster, music = DefaultMusic, shots = DefaultShots, fov = DefaultFov, mouse = DefaultMouse;

    /// <summary>0..1, everything the player hears.</summary>
    public static float MasterVolume { get { return master; } set { master = Clamp(value, 0f, 1f, DefaultMaster); AudioListener.volume = master; } }
    /// <summary>0..1, on top of the overall volume.</summary>
    public static float MusicVolume { get { return music; } set { music = Clamp(value, 0f, 1f, DefaultMusic); } }
    /// <summary>0..1, weapon shots, on top of the overall volume.</summary>
    public static float ShotVolume { get { return shots; } set { shots = Clamp(value, 0f, 1f, DefaultShots); } }
    /// <summary>Vertical field of view of the player camera, degrees.</summary>
    public static float Fov { get { return fov; } set { fov = Clamp(value, MinFov, MaxFov, DefaultFov); } }
    /// <summary>Multiplier of the mouse look (1 = the controller's own speed), on top of the zoom scaling.</summary>
    public static float MouseSensitivity { get { return mouse; } set { mouse = Clamp(value, MinMouse, MaxMouse, DefaultMouse); } }

    // Mathf.Clamp lets NaN through, and a NaN volume or FOV breaks the audio or the camera
    static float Clamp(float v, float min, float max, float fallback) { return float.IsNaN(v) ? fallback : Mathf.Clamp(v, min, max); }

    /// <summary>PlayerPrefs live in the registry / a plain file and can hold anything: NaN or infinity falls back to the default.</summary>
    static float Read(string key, float fallback)
    {
        float v = PlayerPrefs.GetFloat(key, fallback);
        return float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Load()
    {
        MasterVolume = Read(KeyMaster, DefaultMaster);
        MusicVolume = Read(KeyMusic, DefaultMusic);
        ShotVolume = Read(KeyShots, DefaultShots);
        Fov = Read(KeyFov, DefaultFov);
        MouseSensitivity = Read(KeyMouse, DefaultMouse);
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeyMaster, master); PlayerPrefs.SetFloat(KeyMusic, music);
        PlayerPrefs.SetFloat(KeyShots, shots); PlayerPrefs.SetFloat(KeyFov, fov);
        PlayerPrefs.SetFloat(KeyMouse, mouse);
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        MasterVolume = DefaultMaster; MusicVolume = DefaultMusic; ShotVolume = DefaultShots; Fov = DefaultFov;
        MouseSensitivity = DefaultMouse;
    }
}

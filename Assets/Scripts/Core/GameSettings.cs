using UnityEngine;

/// <summary>
/// Player settings, kept in PlayerPrefs: overall volume, music volume, shot volume, field of view, FOV effects, mouse sensitivity,
/// NSFW character models.
/// Read by AudioListener (overall), MusicPlayer, WeaponInventory (shots, FOV and FOV effects), SimpleFirstPersonController and FieldCannon (mouse),
/// WaveSurvivalGame (NSFW models).
/// Changed from SettingsMenu.
/// </summary>
public static class GameSettings
{
    public const float DefaultMaster = 1f, DefaultMusic = 0.6f, DefaultShots = 1f, DefaultFov = 75f, DefaultMouse = 1f, DefaultFovEffects = 1f;
    public const float MinFov = 60f, MaxFov = 110f, MinMouse = 0.1f, MaxMouse = 3f, MaxFovEffects = 2f;

    const string KeyMaster = "settings.volume.master", KeyMusic = "settings.volume.music", KeyShots = "settings.volume.shots", KeyFov = "settings.fov";
    const string KeyMouse = "settings.mouse.sensitivity", KeyFovEffects = "settings.fov.effects", KeyNsfw = "settings.models.nsfw";

    static float master = DefaultMaster, music = DefaultMusic, shots = DefaultShots, fov = DefaultFov, mouse = DefaultMouse, fovEffects = DefaultFovEffects;
    static bool nsfw;

    /// <summary>0..1, everything the player hears.</summary>
    public static float MasterVolume { get { return master; } set { master = Clamp(value, 0f, 1f, DefaultMaster); AudioListener.volume = master; } }
    /// <summary>0..1, on top of the overall volume.</summary>
    public static float MusicVolume { get { return music; } set { music = Clamp(value, 0f, 1f, DefaultMusic); } }
    /// <summary>0..1, weapon shots, on top of the overall volume.</summary>
    public static float ShotVolume { get { return shots; } set { shots = Clamp(value, 0f, 1f, DefaultShots); } }
    /// <summary>Vertical field of view of the player camera, degrees.</summary>
    public static float Fov { get { return fov; } set { fov = Clamp(value, MinFov, MaxFov, DefaultFov); } }
    /// <summary>Strength of the FOV effects (the view widening while sprinting), 0 = off, 1 = as designed.</summary>
    public static float FovEffects { get { return fovEffects; } set { fovEffects = Clamp(value, 0f, MaxFovEffects, DefaultFovEffects); } }
    /// <summary>Multiplier of the mouse look (1 = the controller's own speed), on top of the zoom scaling.</summary>
    public static float MouseSensitivity { get { return mouse; } set { mouse = Clamp(value, MinMouse, MaxMouse, DefaultMouse); } }
    /// <summary>Whether the character enemies include their NSFW variants (off by default). Turned off during a game, the NSFW
    /// enemies already there are changed for safe ones (NsfwModelsChanged).</summary>
    public static bool NsfwModels { get { return nsfw; } set { if (nsfw == value) return; nsfw = value; if (NsfwModelsChanged != null) NsfwModelsChanged(); } }
    public static event System.Action NsfwModelsChanged;

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
        FovEffects = Read(KeyFovEffects, DefaultFovEffects);
        MouseSensitivity = Read(KeyMouse, DefaultMouse);
        NsfwModels = PlayerPrefs.GetInt(KeyNsfw, 0) == 1;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeyMaster, master); PlayerPrefs.SetFloat(KeyMusic, music);
        PlayerPrefs.SetFloat(KeyShots, shots); PlayerPrefs.SetFloat(KeyFov, fov);
        PlayerPrefs.SetFloat(KeyFovEffects, fovEffects); PlayerPrefs.SetFloat(KeyMouse, mouse);
        PlayerPrefs.SetInt(KeyNsfw, nsfw ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        MasterVolume = DefaultMaster; MusicVolume = DefaultMusic; ShotVolume = DefaultShots; Fov = DefaultFov;
        FovEffects = DefaultFovEffects; MouseSensitivity = DefaultMouse; NsfwModels = false;
    }
}

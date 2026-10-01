using UnityEngine;

/// <summary>
/// Player settings, kept in PlayerPrefs: overall volume, music volume, shot volume, field of view.
/// Read by AudioListener (overall), MusicPlayer, WeaponInventory (shots and FOV). Changed from SettingsMenu.
/// </summary>
public static class GameSettings
{
    public const float DefaultMaster = 1f, DefaultMusic = 0.6f, DefaultShots = 1f, DefaultFov = 75f;
    public const float MinFov = 60f, MaxFov = 110f;

    const string KeyMaster = "settings.volume.master", KeyMusic = "settings.volume.music", KeyShots = "settings.volume.shots", KeyFov = "settings.fov";

    static float master = DefaultMaster, music = DefaultMusic, shots = DefaultShots, fov = DefaultFov;

    /// <summary>0..1, everything the player hears.</summary>
    public static float MasterVolume { get { return master; } set { master = Mathf.Clamp01(value); AudioListener.volume = master; } }
    /// <summary>0..1, on top of the overall volume.</summary>
    public static float MusicVolume { get { return music; } set { music = Mathf.Clamp01(value); } }
    /// <summary>0..1, weapon shots, on top of the overall volume.</summary>
    public static float ShotVolume { get { return shots; } set { shots = Mathf.Clamp01(value); } }
    /// <summary>Vertical field of view of the player camera, degrees.</summary>
    public static float Fov { get { return fov; } set { fov = Mathf.Clamp(value, MinFov, MaxFov); } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Load()
    {
        MasterVolume = PlayerPrefs.GetFloat(KeyMaster, DefaultMaster);
        MusicVolume = PlayerPrefs.GetFloat(KeyMusic, DefaultMusic);
        ShotVolume = PlayerPrefs.GetFloat(KeyShots, DefaultShots);
        Fov = PlayerPrefs.GetFloat(KeyFov, DefaultFov);
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeyMaster, master); PlayerPrefs.SetFloat(KeyMusic, music);
        PlayerPrefs.SetFloat(KeyShots, shots); PlayerPrefs.SetFloat(KeyFov, fov);
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        MasterVolume = DefaultMaster; MusicVolume = DefaultMusic; ShotVolume = DefaultShots; Fov = DefaultFov;
    }
}

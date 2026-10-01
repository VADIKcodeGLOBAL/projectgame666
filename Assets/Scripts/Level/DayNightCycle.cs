using UnityEngine;

/// <summary>
/// Day / night cycle: moves the sun and the moon, and blends light, ambient, fog and the sky material
/// (ProjectGame/SkyDayNight) between night, sunrise / sunset and day.
/// timeOfDay: 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset. Works in the editor too (drag the slider).
/// </summary>
[ExecuteAlways]
public class DayNightCycle : MonoBehaviour
{
    public Light sun;
    public Light moon;
    public Material sky;
    [Range(0f, 1f)] public float timeOfDay = 0.36f;
    [Tooltip("Length of a full day in seconds.")] public float dayLength = 240f;
    public bool running = true;
    [Tooltip("Compass direction of sunrise, degrees.")] public float azimuth = 40f;
    [Tooltip("Tilt of the sun's path: 0 = passes straight overhead.")] public float tilt = 32f;
    public float sunIntensity = 1.3f;
    public float moonIntensity = 0.5f;
    public float dayFogDensity = 0.00075f, nightFogDensity = 0.0016f;

    /// <summary>0 at night, 1 in full daylight.</summary>
    public float DayFactor { get; private set; }

    static readonly Color DayZenith = new Color(0.17f, 0.40f, 0.86f), DayHorizon = new Color(0.70f, 0.83f, 0.96f);
    static readonly Color DuskZenith = new Color(0.20f, 0.24f, 0.48f), DuskHorizon = new Color(1.00f, 0.52f, 0.26f);
    static readonly Color NightZenith = new Color(0.010f, 0.016f, 0.050f), NightHorizon = new Color(0.045f, 0.065f, 0.14f);
    static readonly Color SunDay = new Color(1f, 0.95f, 0.84f), SunDusk = new Color(1f, 0.50f, 0.22f), MoonCol = new Color(0.55f, 0.66f, 1f);

    static Color Mix3(Color night, Color dusk, Color day, float dayF, float high)
    {
        return Color.Lerp(night, Color.Lerp(dusk, day, high), dayF);
    }

    void OnEnable()
    {
        if (Application.isPlaying && sky != null) sky = new Material(sky);      // animate a copy, not the asset
        Apply();
    }
    void OnValidate() { Apply(); }

    void Update()
    {
        if (Application.isPlaying && running && dayLength > 1f) timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime / dayLength, 1f);
        Apply();
    }

    public void Apply()
    {
        Quaternion path = Quaternion.Euler(0f, azimuth, 0f) * Quaternion.Euler(0f, 0f, tilt);
        Vector3 sunFwd = path * (Quaternion.Euler((timeOfDay - 0.25f) * 360f, 0f, 0f) * Vector3.forward);   // direction the light travels
        Vector3 toSun = -sunFwd, toMoon = sunFwd;
        float y = toSun.y;
        DayFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.14f, 0.16f, y));
        float high = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.42f, y));                           // 0 at the horizon, 1 high in the sky

        if (sun != null)
        {
            sun.transform.rotation = Quaternion.LookRotation(sunFwd);
            sun.intensity = sunIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.03f, 0.22f, y));
            sun.color = Color.Lerp(SunDusk, SunDay, high);
            sun.enabled = sun.intensity > 0.01f;
        }
        if (moon != null)
        {
            moon.transform.rotation = Quaternion.LookRotation(toMoon.y > 0.05f ? -toMoon : new Vector3(-toMoon.x, -0.35f, -toMoon.z));
            moon.intensity = moonIntensity * (1f - DayFactor);
            moon.color = MoonCol;
            moon.enabled = moon.intensity > 0.01f;
            moon.shadows = (sun == null || !sun.enabled) ? LightShadows.Soft : LightShadows.None;
        }

        Color zenith = Mix3(NightZenith, DuskZenith, DayZenith, DayFactor, high);
        Color horizon = Mix3(NightHorizon, DuskHorizon, DayHorizon, DayFactor, high);
        Color fog = Color.Lerp(horizon, zenith, 0.18f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = fog;
        RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, dayFogDensity, DayFactor);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Mix3(new Color(0.14f, 0.19f, 0.34f), new Color(0.45f, 0.40f, 0.55f), new Color(0.62f, 0.72f, 0.86f), DayFactor, high);
        RenderSettings.ambientEquatorColor = Mix3(new Color(0.10f, 0.13f, 0.22f), new Color(0.48f, 0.36f, 0.32f), new Color(0.50f, 0.56f, 0.52f), DayFactor, high);
        RenderSettings.ambientGroundColor = Mix3(new Color(0.04f, 0.05f, 0.08f), new Color(0.14f, 0.12f, 0.10f), new Color(0.20f, 0.22f, 0.16f), DayFactor, high);
        Shader.SetGlobalColor("_PG_SkyZenith", zenith);
        Shader.SetGlobalColor("_PG_SkyHorizon", horizon);

        if (sky != null)
        {
            sky.SetColor("_ZenithColor", zenith);
            sky.SetColor("_HorizonColor", horizon);
            sky.SetColor("_GroundColor", fog);
            sky.SetColor("_SunColor", Color.Lerp(SunDusk, SunDay, high) * Mathf.Lerp(1.15f, 1f, high));
            sky.SetVector("_SunDir", toSun);
            sky.SetVector("_MoonDir", toMoon);
            var mc = new Color(0.86f, 0.90f, 1f, 1f - DayFactor * 0.85f);
            sky.SetColor("_MoonColor", mc);
            sky.SetFloat("_StarIntensity", Mathf.Pow(1f - DayFactor, 2f) * 1.3f);
            sky.SetColor("_CloudColor", Mix3(new Color(0.07f, 0.09f, 0.16f), new Color(1f, 0.62f, 0.46f), new Color(1f, 1f, 1f), DayFactor, high));
            if (RenderSettings.skybox != sky) RenderSettings.skybox = sky;
        }
    }
}

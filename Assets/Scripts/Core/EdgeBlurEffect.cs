using UnityEngine;

/// <summary>
/// Camera effect (Built-in pipeline): blur and vignette growing towards the sides of the screen,
/// a little extra saturation, and a red pulse when the player takes damage.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class EdgeBlurEffect : MonoBehaviour
{
    public Shader shader;
    [Range(0f, 1.5f)] public float blurStart = 0.62f;
    [Range(0f, 2f)] public float blurEnd = 1.18f;
    [Tooltip("Blur radius at the very edge, pixels at 1080p.")] [Range(0f, 24f)] public float blurSize = 7f;
    [Tooltip("1 = round mask, lower = the effect sits on the left and right sides.")] [Range(0.3f, 1f)] public float sideBias = 0.62f;
    [Range(0f, 1f)] public float vignette = 0.30f;
    [Range(0.5f, 1.6f)] public float saturation = 1.12f;
    public Color damageColor = new Color(0.75f, 0.03f, 0.02f);
    public PlayerHealth health;

    Material mat;

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (shader == null) shader = Shader.Find("Hidden/ProjectGame/EdgeBlur");
        if (shader == null) { Graphics.Blit(src, dst); return; }
        if (mat == null) mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        float low = health != null ? 1f - Mathf.Clamp01(health.Health / Mathf.Max(1f, health.maxHealth) * 2.5f) : 0f;   // stays red below 40 % health
        mat.SetFloat("_BlurStart", blurStart); mat.SetFloat("_BlurEnd", blurEnd); mat.SetFloat("_BlurSize", blurSize);
        mat.SetFloat("_SideBias", sideBias); mat.SetFloat("_Vignette", vignette); mat.SetFloat("_Saturation", saturation);
        mat.SetColor("_DamageColor", damageColor);
        mat.SetFloat("_Damage", health != null ? Mathf.Max(health.DamagePulse, low * 0.7f) : 0f);
        Graphics.Blit(src, dst, mat);
    }

    void OnDisable()
    {
        if (mat != null) { if (Application.isPlaying) Destroy(mat); else DestroyImmediate(mat); mat = null; }
    }
}

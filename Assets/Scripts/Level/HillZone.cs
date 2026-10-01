using UnityEngine;

/// <summary>The circle on the summit: the wave timer runs only while the player is inside. The ring and the wall turn red while the player is outside.</summary>
public class HillZone : MonoBehaviour
{
    public float radius = 12f;
    public float height = 10f;
    public Renderer[] tintRenderers;
    [ColorUsage(false, true)] public Color safeColor = new Color(0.25f, 0.95f, 1f);
    [ColorUsage(false, true)] public Color dangerColor = new Color(1.6f, 0.22f, 0.08f);

    static readonly int ColorId = Shader.PropertyToID("_Color");
    MaterialPropertyBlock block;
    float danger = -1f;

    public bool Contains(Vector3 p)
    {
        Vector3 d = p - transform.position;
        return d.y > -2.5f && d.y < height && d.x * d.x + d.z * d.z <= radius * radius;
    }

    /// <summary>0 = player inside, 1 = about to lose.</summary>
    public void SetDanger(float t)
    {
        t = Mathf.Clamp01(t);
        if (Mathf.Approximately(t, danger) || tintRenderers == null) return;
        danger = t;
        if (block == null) block = new MaterialPropertyBlock();
        Color c = Color.Lerp(safeColor, dangerColor, t > 0f ? 0.5f + 0.5f * Mathf.PingPong(Time.time * 4f, 1f) : 0f);
        foreach (var r in tintRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block); block.SetColor(ColorId, c); r.SetPropertyBlock(block);
        }
        if (t > 0f) danger = -1f;                     // keep pulsing: re-evaluate next frame
    }

    void OnEnable() { danger = -1f; SetDanger(0f); }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Vector3 c = transform.position; const int N = 48;
        for (int i = 0; i < N; i++)
        {
            float a0 = i * Mathf.PI * 2f / N, a1 = (i + 1) * Mathf.PI * 2f / N;
            Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius, p1 = c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            Gizmos.DrawLine(p0, p1); Gizmos.DrawLine(p0 + Vector3.up * height, p1 + Vector3.up * height);
        }
    }
}

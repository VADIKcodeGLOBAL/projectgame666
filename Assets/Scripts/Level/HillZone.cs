using UnityEngine;

/// <summary>
/// The circle on the summit: the wave timer runs only while the player is inside. While the player is outside, the floor ring,
/// the wall and the flag fade to a pulsing red, and the ring and wall shaders brighten and run their animation faster.
/// </summary>
public class HillZone : MonoBehaviour
{
    public float radius = 16f;
    public float height = 10f;
    public Renderer[] tintRenderers;
    [ColorUsage(false, true)] public Color safeColor = new Color(0.25f, 0.95f, 1f);
    [ColorUsage(false, true)] public Color dangerColor = new Color(1.6f, 0.22f, 0.08f);
    [Tooltip("Seconds to fade between the calm and the danger look.")] public float fadeTime = 0.35f;
    [Tooltip("How much faster the ring and wall animate in danger (1 = twice as fast).")] public float dangerSpeedUp = 1.5f;

    static readonly int ColorId = Shader.PropertyToID("_Color"), DangerId = Shader.PropertyToID("_Danger"), BoostId = Shader.PropertyToID("_Boost");
    MaterialPropertyBlock block;
    float target, shown = -1f, boost;

    public bool Contains(Vector3 p)
    {
        Vector3 d = p - transform.position;
        return d.y > -2.5f && d.y < height && d.x * d.x + d.z * d.z <= radius * radius;
    }

    /// <summary>0 = player inside, 1 = about to lose.</summary>
    public void SetDanger(float t) { target = Mathf.Clamp01(t); }

    void OnEnable() { shown = -1f; }

    void Update()
    {
        float dt = Time.deltaTime;
        bool first = shown < 0f;
        if (!first && shown == 0f && target == 0f) return;         // calm: nothing changes
        shown = first ? target : Mathf.MoveTowards(shown, target, dt / Mathf.Max(0.01f, fadeTime));
        boost += dt * dangerSpeedUp * shown;                       // extra shader time: speeds the animation up without a jump
        Apply(Color.Lerp(safeColor, dangerColor, shown * (0.5f + 0.5f * Mathf.PingPong(Time.time * 4f, 1f))));
    }

    void Apply(Color c)
    {
        if (tintRenderers == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        foreach (var r in tintRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(ColorId, c); block.SetFloat(DangerId, shown); block.SetFloat(BoostId, boost);
            r.SetPropertyBlock(block);
        }
    }

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

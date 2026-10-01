using UnityEngine;

/// <summary>Player hit points with delayed regeneration. DamagePulse (0..1) drives the red flash of the camera effect.</summary>
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    [Tooltip("Seconds without damage before health starts to come back.")] public float regenDelay = 3.5f;
    public float regenPerSecond = 8f;

    public float Health { get; private set; }
    public bool IsDead { get { return Health <= 0f; } }
    public float DamagePulse { get; private set; }

    float lastDamageTime = -100f;

    void Awake() { Health = maxHealth; }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Health = Mathf.Max(0f, Health - amount);
        lastDamageTime = Time.time;
        DamagePulse = 1f;
    }

    public void Heal(float amount)
    {
        if (!IsDead && amount > 0f) Health = Mathf.Min(maxHealth, Health + amount);
    }

    void Update()
    {
        DamagePulse = Mathf.MoveTowards(DamagePulse, 0f, Time.deltaTime * 1.6f);
        if (!IsDead && Time.time - lastDamageTime > regenDelay) Health = Mathf.Min(maxHealth, Health + regenPerSecond * Time.deltaTime);
    }
}

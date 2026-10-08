using UnityEngine;

/// <summary>
/// Player hit points. No regeneration: health comes back only from medkits (SupplyPickup).
/// DamagePulse (0..1) drives the red flash of the camera effect.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;

    public float Health { get; private set; }
    public bool IsDead { get { return Health <= 0f; } }
    public bool IsFull { get { return Health >= maxHealth; } }
    public float DamagePulse { get; private set; }

    void Awake() { Health = maxHealth; }

    public void TakeDamage(float amount)
    {
        if (IsDead || !(amount > 0f)) return;                      // NaN would turn the health into NaN
        amount = UpgradeSystem.PlayerDamage(this, amount);         // armour, the last stand
        if (!(amount > 0f)) return;
        Health = Mathf.Max(0f, Health - amount);
        DamagePulse = 1f;
    }

    public void Heal(float amount)
    {
        if (!IsDead && amount > 0f) Health = Mathf.Min(maxHealth, Health + amount);
    }

    void Update()
    {
        DamagePulse = Mathf.MoveTowards(DamagePulse, 0f, Time.deltaTime * 1.6f);
    }
}

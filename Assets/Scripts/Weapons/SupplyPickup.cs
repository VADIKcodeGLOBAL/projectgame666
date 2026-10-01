using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A supply lying on the field: hovers and turns, is taken by walking into it, blinks before it disappears.
///  - Ammo: one magazine for the weapon in hand (or the first weapon with room);
///  - Medkit: health back (the only way to heal — there is no regeneration); left lying while health is full;
///  - Speed (syringe): faster movement for a while.
/// Dropped by killed bots and laid out on the summit by the wave game.
/// </summary>
public class SupplyPickup : MonoBehaviour
{
    public enum Kind { Ammo, Medkit, Speed }

    public Kind kind = Kind.Ammo;
    public float pickupRadius = 1.7f;
    public float lifetime = 45f;
    [Tooltip("Turns and bobs; the glow beam stays upright.")] public Transform spinner;
    public float hoverHeight = 0.75f;
    [Header("Effect")]
    public float heal = 35f;
    public float speedMultiplier = 1.35f, speedDuration = 10f;

    public static readonly List<SupplyPickup> All = new List<SupplyPickup>();
    /// <summary>The last thing picked up, for the HUD.</summary>
    public static string LastMessage { get; private set; }
    public static float LastTime { get; private set; } = -10f;
    public static Kind LastKind { get; private set; }
    public bool OnSummit { get; set; }

    float born, phase; Vector3 basePos; Renderer[] renderers; bool shown = true;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        born = Time.time; phase = Random.value * 10f;
        basePos = transform.position;
        renderers = GetComponentsInChildren<Renderer>();
    }

    /// <summary>Puts a supply on the ground under (x, z).</summary>
    public static SupplyPickup Spawn(SupplyPickup prefab, Vector3 at)
    {
        if (prefab == null) return null;
        var t = Terrain.activeTerrain;
        if (t != null) at.y = t.SampleHeight(at) + t.transform.position.y;
        return Instantiate(prefab, at + Vector3.up * prefab.hoverHeight, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
    }

    static void Announce(string text, Kind k) { LastMessage = text; LastTime = Time.time; LastKind = k; }

    /// <summary>Gives the supply to the player; false if it is of no use right now (it stays on the ground).</summary>
    bool Apply(WeaponInventory inv)
    {
        switch (kind)
        {
            case Kind.Ammo:
                if (!inv.GiveMagazine()) return false;
                Announce(inv.PickupText, kind); return true;
            case Kind.Medkit:
                var hp = inv.GetComponent<PlayerHealth>();
                if (hp == null || hp.IsDead || hp.IsFull) return false;
                float before = hp.Health; hp.Heal(heal);
                Announce("+" + Mathf.RoundToInt(hp.Health - before) + " HP  MEDKIT", kind); return true;
            case Kind.Speed:
                var fp = inv.GetComponent<SimpleFirstPersonController>();
                if (fp == null) return false;
                fp.ApplySpeedBoost(speedMultiplier, speedDuration);
                Announce("SPEED +" + Mathf.RoundToInt((speedMultiplier - 1f) * 100f) + " %  FOR " + Mathf.RoundToInt(speedDuration) + " S", kind); return true;
        }
        return false;
    }

    void Update()
    {
        float age = Time.time - born;
        if (age > lifetime) { Destroy(gameObject); return; }
        transform.position = basePos + Vector3.up * (Mathf.Sin((Time.time + phase) * 2.2f) * 0.08f);
        if (spinner != null) spinner.Rotate(0f, 110f * Time.deltaTime, 0f, Space.World);
        bool show = lifetime - age > 5f || Mathf.Repeat(age * 4f, 1f) < 0.6f;           // blinks in its last five seconds
        if (show != shown) { shown = show; foreach (var r in renderers) r.enabled = show; }

        var inv = WeaponInventory.Instance;
        if (inv == null) return;
        Vector3 d = inv.transform.position + Vector3.up * 0.9f - transform.position;
        if (d.sqrMagnitude < pickupRadius * pickupRadius && Apply(inv)) Destroy(gameObject);
    }
}

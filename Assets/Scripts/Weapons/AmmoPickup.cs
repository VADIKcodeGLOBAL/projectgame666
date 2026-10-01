using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A magazine lying on the ground: hovers and turns, is picked up by walking into it (one magazine for the weapon in hand,
/// or for the first weapon with room), blinks before it disappears. Dropped by killed bots and laid out on the summit by the wave game.
/// </summary>
public class AmmoPickup : MonoBehaviour
{
    public float pickupRadius = 1.7f;
    public float lifetime = 45f;
    [Tooltip("Turns and bobs; the glow beam stays upright.")] public Transform spinner;
    public float hoverHeight = 0.75f;

    public static readonly List<AmmoPickup> All = new List<AmmoPickup>();
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

    /// <summary>Puts a pickup on the ground under (x, z).</summary>
    public static AmmoPickup Spawn(AmmoPickup prefab, Vector3 at)
    {
        if (prefab == null) return null;
        var t = Terrain.activeTerrain;
        if (t != null) at.y = t.SampleHeight(at) + t.transform.position.y;
        var p = Instantiate(prefab, at + Vector3.up * prefab.hoverHeight, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        return p;
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
        if (d.sqrMagnitude < pickupRadius * pickupRadius && inv.GiveMagazine()) Destroy(gameObject);
    }
}

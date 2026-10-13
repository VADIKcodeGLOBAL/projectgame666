using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The character enemies a wave game draws from (made by the "enemies" command, next to the prefabs: local, like the models).
/// Every model has a tier from its size — the smallest models are tier 0, the biggest the last — and a wave brings the tiers up
/// to its own (wave 1: tier 0 only; each next wave one tier bigger), the newest tiers more often: the enemies grow with the waves'
/// strength. Each model gets one share however many variants (outfits, skins) it has. NSFW variants only when the player allows
/// them (GameSettings.NsfwModels). Tier, weight and on/off can be changed here by hand; "enemies" keeps those edits.
/// </summary>
[CreateAssetMenu(fileName = "EnemyRoster", menuName = "ProjectGame/Enemy Roster")]
public class EnemyRoster : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public EnemyBot prefab;
        [Tooltip("The model (one share of the spawns among its variants).")] public string model;
        [Tooltip("The variant: outfit and skin.")] public string variant;
        [Tooltip("The model's natural height, m (before the enemies' body scale).")] public float height;
        public bool nsfw;
        [Tooltip("First wave it comes in: tier + 1.")] [Min(0)] public int tier;
        [HideInInspector] public int autoTier = -1;                    // the tier "enemies" gave it: a different tier is a hand edit
        [Tooltip("Relative chance among the variants of its model.")] [Min(0f)] public float weight = 1f;
        public bool enabled = true;
    }

    public List<Entry> enemies = new List<Entry>();

    /// <summary>Whether this entry can come in this wave (1 = the first) with NSFW allowed or not.</summary>
    public static bool Allowed(Entry e, int wave, bool nsfw)
    {
        return e != null && e.enabled && e.prefab != null && e.weight > 0f && (nsfw || !e.nsfw) && e.tier <= Mathf.Max(1, wave) - 1;
    }

    readonly List<Entry> pool = new List<Entry>();
    readonly List<float> weights = new List<float>();
    readonly Dictionary<string, float> perModel = new Dictionary<string, float>();

    /// <summary>
    /// An enemy for this wave: among the allowed entries, each model's share is (1 + tier) (the newer, bigger ones more often),
    /// split over its variants by their weights. With no entry of the wave's tiers allowed, the smallest allowed ones come;
    /// null when nothing at all is allowed.
    /// </summary>
    public EnemyBot Pick(int wave, bool nsfw, Func<float> random01)
    {
        pool.Clear(); weights.Clear(); perModel.Clear();
        int lowest = int.MaxValue;
        foreach (var e in enemies) if (Allowed(e, int.MaxValue, nsfw)) lowest = Mathf.Min(lowest, e.tier);
        if (lowest == int.MaxValue) return null;
        int reach = Mathf.Max(Mathf.Max(1, wave) - 1, lowest);
        foreach (var e in enemies)
            if (Allowed(e, reach + 1, nsfw)) { pool.Add(e); float s; perModel.TryGetValue(e.model ?? "", out s); perModel[e.model ?? ""] = s + e.weight; }
        float sum = 0f;
        foreach (var e in pool) { float w = (1f + e.tier) * e.weight / perModel[e.model ?? ""]; weights.Add(w); sum += w; }
        float x = random01() * sum;
        for (int i = 0; i < pool.Count; i++) { x -= weights[i]; if (x <= 0f) return pool[i].prefab; }
        return pool[pool.Count - 1].prefab;
    }

    /// <summary>A safe-for-work stand-in for an NSFW enemy: a variant of the same model if it has one, else one of the same tier,
    /// else any allowed one.</summary>
    public EnemyBot SfwFor(string model, Func<float> random01)
    {
        int tier = -1;
        foreach (var e in enemies) if (e != null && e.model == model) { tier = e.tier; break; }
        Entry best = null; int bestRank = int.MaxValue; int seen = 0;
        foreach (var e in enemies)
        {
            if (!Allowed(e, int.MaxValue, false)) continue;
            int rank = e.model == model ? 0 : e.tier == tier ? 1 : 2;
            if (rank < bestRank) { best = e; bestRank = rank; seen = 1; }
            else if (rank == bestRank && random01() * ++seen < 1f) best = e;    // one of the equals, evenly
        }
        return best != null ? best.prefab : null;
    }

    /// <summary>The entry of a prefab (null if it is not in the roster).</summary>
    public Entry Of(EnemyBot prefab)
    {
        foreach (var e in enemies) if (e != null && e.prefab == prefab) return e;
        return null;
    }
}

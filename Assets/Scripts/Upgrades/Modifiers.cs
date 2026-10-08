using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>How strong a modifier is; also its colour on the cards.</summary>
public enum Rarity { Common, Great, Rare, Sweg, Slayer }

/// <summary>The picture on a card (drawn by UpgradeArt).</summary>
public enum ModIcon { Bullet, Rate, Reload, Speed, Heart, Blade, Aim, Cross, Mag, Shield, Pierce, Drop, Pulse, Crate, Impact, Skull, Feather, Burst, Bolt, Hose, Clock, Tornado, Infinity }

/// <summary>One upgrade the player can take after a wave. apply changes RunStats (and, through the system, the player and the weapons).</summary>
public class Modifier
{
    public string id, title, text;
    public Rarity rarity;
    public ModIcon icon;
    [Tooltip("Taken once at most (a second copy would do nothing).")] public bool unique;
    public Action<RunStats, UpgradeSystem> apply;

    public Modifier(string id, Rarity rarity, ModIcon icon, string title, string text, Action<RunStats, UpgradeSystem> apply, bool unique = false)
    {
        this.id = id; this.rarity = rarity; this.icon = icon; this.title = title; this.text = text; this.apply = apply; this.unique = unique;
    }
}

/// <summary>
/// Every modifier there is, by rarity:
///  - COMMON (grey): a small rise of one stat;
///  - GREAT (green): a good rise of one or two stats;
///  - RARE (blue): changes how something works;
///  - SWEG (purple): changes it a lot (overpowered on purpose);
///  - SLAYER (red): changes the game. The two here are stand-ins until the real ones are designed.
/// Add a modifier: one line in Build (and its hook in RunStats / UpgradeSystem if it is a new mechanic).
/// </summary>
public static class ModifierCatalog
{
    static List<Modifier> all;
    public static List<Modifier> All { get { if (all == null) all = Build(); return all; } }

    public static readonly string[] RarityNames = { "COMMON", "GREAT", "RARE", "SWEG", "SLAYER" };
    public static readonly Color[] RarityColors =
    {
        new Color(0.72f, 0.75f, 0.80f),     // grey
        new Color(0.30f, 0.92f, 0.42f),     // green
        new Color(0.26f, 0.62f, 1.00f),     // blue
        new Color(0.74f, 0.36f, 1.00f),     // purple
        new Color(1.00f, 0.20f, 0.16f),     // red
    };

    public static string Name(Rarity r) { return RarityNames[(int)r]; }
    public static Color Tint(Rarity r) { return RarityColors[(int)r]; }

    public static Modifier Find(string id) { foreach (var m in All) if (m.id == id) return m; return null; }

    static List<Modifier> Build()
    {
        return new List<Modifier>
        {
            // ---- COMMON: a little of one stat
            new Modifier("c_damage", Rarity.Common, ModIcon.Bullet, "Hollow Points", "Gun damage +8%", (s, u) => s.gunDamage *= 1.08f),
            new Modifier("c_rate", Rarity.Common, ModIcon.Rate, "Oiled Bolt", "Fire rate +7%", (s, u) => s.fireRate *= 1.07f),
            new Modifier("c_reload", Rarity.Common, ModIcon.Reload, "Quick Hands", "Reload 12% faster", (s, u) => s.reloadSpeed *= 1.12f),
            new Modifier("c_speed", Rarity.Common, ModIcon.Speed, "Light Boots", "Move speed +5%", (s, u) => s.moveSpeed *= 1.05f),
            new Modifier("c_hp", Rarity.Common, ModIcon.Heart, "Thick Skin", "Max health +10", (s, u) => u.AddMaxHealth(10f)),
            new Modifier("c_blade", Rarity.Common, ModIcon.Blade, "Whetstone", "Sword damage +10%", (s, u) => s.meleeDamage *= 1.1f),
            new Modifier("c_aim", Rarity.Common, ModIcon.Aim, "Steady Aim", "Spread -15%", (s, u) => s.spread *= 0.85f),
            new Modifier("c_heal", Rarity.Common, ModIcon.Cross, "Bandages", "Medkits heal 20% more", (s, u) => s.healing *= 1.2f),

            // ---- GREAT: a good deal more
            new Modifier("g_damage", Rarity.Great, ModIcon.Bullet, "Match Grade Ammo", "Gun damage +18%", (s, u) => s.gunDamage *= 1.18f),
            new Modifier("g_rate", Rarity.Great, ModIcon.Rate, "Hair Trigger", "Fire rate +15%", (s, u) => s.fireRate *= 1.15f),
            new Modifier("g_reload", Rarity.Great, ModIcon.Reload, "Speed Loader", "Reload 25% faster", (s, u) => s.reloadSpeed *= 1.25f),
            new Modifier("g_speed", Rarity.Great, ModIcon.Speed, "Runner", "Move speed +10%\nJumps 15% higher", (s, u) => { s.moveSpeed *= 1.1f; s.jump *= 1.15f; }),
            new Modifier("g_hp", Rarity.Great, ModIcon.Heart, "Iron Body", "Max health +25", (s, u) => u.AddMaxHealth(25f)),
            new Modifier("g_blade", Rarity.Great, ModIcon.Blade, "Balanced Blade", "Sword damage +20%\nSwings 10% faster", (s, u) => { s.meleeDamage *= 1.2f; s.meleeSpeed *= 1.1f; }),
            new Modifier("g_mags", Rarity.Great, ModIcon.Mag, "Extended Mags", "Magazines hold 25% more", (s, u) => u.ScaleMagazines(1.25f)),
            new Modifier("g_armor", Rarity.Great, ModIcon.Shield, "Kevlar", "Damage taken -12%", (s, u) => s.damageTaken *= 0.88f),

            // ---- RARE: something works differently
            new Modifier("r_pierce", Rarity.Rare, ModIcon.Pierce, "Piercing Rounds", "Rounds go through a bot\nand hit the one behind it", (s, u) => s.pierce += 1),
            new Modifier("r_vampire", Rarity.Rare, ModIcon.Drop, "Vampire", "Every kill heals 5 HP", (s, u) => s.killHeal += 5f),
            new Modifier("r_adrenaline", Rarity.Rare, ModIcon.Pulse, "Adrenaline", "Every kill: 2 s of\n+30% speed", (s, u) => s.killBoost += 2f),
            new Modifier("r_scavenger", Rarity.Rare, ModIcon.Crate, "Scavenger", "Bots drop supplies\ntwice as often", (s, u) => s.dropChance *= 2f, true),
            new Modifier("r_stopping", Rarity.Rare, ModIcon.Impact, "Stopping Power", "Gun hits knock bots back\nand stagger them", (s, u) => s.hitKnockback += 3f),
            new Modifier("r_laststand", Rarity.Rare, ModIcon.Skull, "Last Stand", "Once a wave, a killing blow\nleaves you at 1 HP,\n2 s untouchable", (s, u) => s.lastStand = true, true),
            new Modifier("r_feather", Rarity.Rare, ModIcon.Feather, "Featherweight", "The sword weighs nothing:\nfull speed, quick draw", (s, u) => s.featherweight = true, true),

            // ---- SWEG: a lot, on purpose
            new Modifier("s_explosive", Rarity.Sweg, ModIcon.Burst, "Explosive Rounds", "Gun hits burst: 45% damage\nto bots within 2.5 m", (s, u) => { s.burstRadius = Mathf.Max(s.burstRadius, 2.5f); s.burstShare += 0.45f; }),
            new Modifier("s_chain", Rarity.Sweg, ModIcon.Bolt, "Chain Lightning", "Gun hits arc to 2 more bots\nnearby for 60% damage", (s, u) => s.chainTargets += 2),
            new Modifier("s_hose", Rarity.Sweg, ModIcon.Hose, "Bullet Hose", "Fire rate +25%\nEvery other round is free", (s, u) => { s.fireRate *= 1.25f; s.freeAmmoChance = Mathf.Max(s.freeAmmoChance, 0.5f); }),
            new Modifier("s_timewarp", Rarity.Sweg, ModIcon.Clock, "Time Warp", "All bots move 30% slower", (s, u) => s.botSpeed *= 0.7f, true),
            new Modifier("s_whirlwind", Rarity.Sweg, ModIcon.Tornado, "Whirlwind", "Slashes hit all around you\nChops reach 5 m", (s, u) => s.whirlwind = true, true),

            // ---- SLAYER: stand-ins until the real ones are designed
            new Modifier("x_berserk", Rarity.Slayer, ModIcon.Skull, "BERSERK", "All your damage x2\nbut you take 50% more", (s, u) => { s.allDamage *= 2f; s.damageTaken *= 1.5f; }, true),
            new Modifier("x_deathless", Rarity.Slayer, ModIcon.Infinity, "DEATHLESS", "Kills give back 15% health\nand refill the magazine", (s, u) => { s.killHealPercent += 0.15f; s.killRefill = true; }, true),
        };
    }
}

using UnityEngine;

/// <summary>
/// What the modifiers taken so far add up to, for this run. Read where it matters: the guns (Weapon), the sword (MeleeWeapon),
/// the player (SimpleFirstPersonController, PlayerHealth), the bots (EnemyBot), the drops (WaveSurvivalGame), the medkits
/// (SupplyPickup); the kill and hit effects are UpgradeSystem's. All 1 / 0 / false at the start of a run.
/// </summary>
[System.Serializable]
public class RunStats
{
    [Header("Guns")]
    public float gunDamage = 1f;
    public float fireRate = 1f;
    public float reloadSpeed = 1f;
    public float spread = 1f;
    [Tooltip("Magazine size: applied to the guns when taken (Weapon.ScaleMagazine); kept here for the record.")] public float magazine = 1f;
    [Tooltip("Chance a round does not use ammo.")] public float freeAmmoChance;
    [Tooltip("A round goes on through this many more bots.")] public int pierce;
    [Tooltip("A gun hit throws the bot back this fast, m/s (0 = not at all).")] public float hitKnockback;
    [Tooltip("A gun hit bursts: share of its damage to the bots within burstRadius.")] public float burstRadius, burstShare;
    [Tooltip("A gun hit arcs on to this many more bots (chainShare of the damage, within chainRange).")] public int chainTargets;
    public float chainShare = 0.6f, chainRange = 6f;

    [Header("Sword")]
    public float meleeDamage = 1f;
    public float meleeSpeed = 1f;
    [Tooltip("The sword no longer slows the player or takes long to draw.")] public bool featherweight;
    [Tooltip("Slashes hit all around, chops reach 5 m.")] public bool whirlwind;

    [Header("Player")]
    public float moveSpeed = 1f;
    public float jump = 1f;
    public float damageTaken = 1f;
    [Tooltip("Medkits heal this many times as much.")] public float healing = 1f;
    [Tooltip("Every damage the player deals (guns, sword, cannon splash not included).")] public float allDamage = 1f;
    [Tooltip("Once a wave a killing blow leaves 1 HP and a moment untouchable.")] public bool lastStand;

    [Header("Kills")]
    public float killHeal;
    [Tooltip("Part of max health back per kill.")] public float killHealPercent;
    [Tooltip("Seconds of the speed boost per kill.")] public float killBoost;
    [Tooltip("A kill refills the magazine of the gun in hand.")] public bool killRefill;

    [Header("Bots and drops")]
    public float botSpeed = 1f;
    public float dropChance = 1f;

    /// <summary>What the guns hit for, all multipliers in.</summary>
    public float GunDamage { get { return gunDamage * allDamage; } }
    public float MeleeDamage { get { return meleeDamage * allDamage; } }
}

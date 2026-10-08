using UnityEngine;

/// <summary>
/// What every weapon in the player's hands is to WeaponInventory: a name for the HUD, the input of the frame, the view-model pose
/// (switching, scope, walk bob, sway) and the hooks the inventory calls. Guns (Weapon) and blades (MeleeWeapon) build on it.
/// </summary>
public abstract class HandWeapon : MonoBehaviour
{
    public string displayName = "Weapon";
    [Tooltip("Short name for the weapon slots on the HUD (empty: the display name).")] public string slotName;

    public string SlotLabel { get { return string.IsNullOrEmpty(slotName) ? displayName : slotName; } }

    [Header("Scope")]
    [Tooltip("RMB brings up a scope (guns); otherwise RMB goes to the weapon itself (the heavy blow of a blade).")] public bool hasScope;
    public float scopeFov = 12f;

    public WeaponInventory Owner { get; protected set; }
    /// <summary>How far the sway has turned the weapon from its resting pose, degrees.</summary>
    public float SwayTiltNow { get; protected set; }
    public virtual bool IsReloading { get { return false; } }
    /// <summary>Part of the normal walking / running speed the player has with this weapon in hand right now (a heavy one is slower).</summary>
    public virtual float MoveSpeedScale { get { return 1f; } }
    /// <summary>Putting this weapon away and drawing it takes this many times WeaponInventory.switchTime.</summary>
    public virtual float DrawTimeScale { get { return 1f; } }

    /// <summary>Called once by the inventory (the weapons not in hand are inactive, so Awake would come too late).</summary>
    public abstract void Init(WeaponInventory owner);

    /// <summary>
    /// The weapon in hand, every frame. held / pressed: LMB down / went down this frame; altHeld / altPressed: the same for RMB
    /// (when the weapon has no scope); ready: fully in hand (not being switched); aim: 0..1 scope.
    /// </summary>
    public abstract void Tick(bool held, bool pressed, bool altHeld, bool altPressed, bool ready, Camera cam, float aim);

    /// <summary>View-model motion. raise: 0 lowered out of sight (switching) .. 1 in hand; aim: 0..1 scope; bob: walk sway;
    /// swayTilt (degrees) and swayShift (metres): the lag behind a turning view, both in camera space.</summary>
    public abstract void UpdatePose(float raise, float aim, Vector3 bob, Vector3 swayTilt, Vector3 swayShift);

    /// <summary>R: reload if the weapon has anything to reload.</summary>
    public virtual bool StartReload() { return false; }

    /// <summary>The weapon is being put away or switched: stop what it is doing.</summary>
    public virtual void CancelReload() { }
}

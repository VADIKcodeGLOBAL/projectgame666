using UnityEngine;

/// <summary>
/// One first-person weapon. A round is registered the moment it is fired (a ray from the camera, no projectile and no tracer);
/// ammo is a magazine plus spare rounds with a timed reload; every round shows the muzzle fire (a quad with the flash texture,
/// turned to a random angle), a short flash of light and a few sparks; the view model kicks, dips for the reload and drops
/// for a weapon switch. WeaponInventory owns the input and calls Tick and UpdatePose for the weapon in hand.
/// </summary>
public class Weapon : MonoBehaviour
{
    public enum FireMode { Auto, Semi }

    [Header("Shooting")]
    public string displayName = "Rifle";
    public FireMode mode = FireMode.Auto;
    public float damage = 40f;
    public float roundsPerSecond = 10f;
    public float range = 600f;
    [Tooltip("Spread from the hip, degrees.")] public float spread = 0.5f;
    [Tooltip("Spread through the scope, degrees.")] public float scopedSpread = 0f;
    [Tooltip("Camera kick per round, degrees up.")] public float recoil = 0.35f;

    [Header("Ammo")]
    public int magazineSize = 30;
    public int startMagazines = 4;
    public int maxMagazines = 8;
    [Tooltip("Endless spare magazines (the sidearm).")] public bool infiniteReserve;
    public float reloadTime = 2.2f;

    [Header("Scope")]
    public bool hasScope;
    public float scopeFov = 12f;

    [Header("View model")]
    public Transform muzzle;
    [Tooltip("Slides down out of the gun during the reload (optional).")] public Transform magazine;
    [Tooltip("Push of the model per round (camera space), metres.")] public Vector3 kick = new Vector3(0f, 0.008f, -0.04f);
    [Tooltip("Muzzle climb of the model per round, degrees.")] public float kickPitch = 3f;

    [Header("Effects")]
    public Renderer muzzleFire;
    public float muzzleFireSize = 0.2f;
    public Light flashLight;
    public ParticleSystem sparks;
    public int sparksPerRound = 6;
    public AudioClip[] shotClips;
    public float shotVolume = 0.55f, shotPitch = 1f;

    public WeaponInventory Owner { get; private set; }
    public int InMagazine { get; private set; }
    /// <summary>Spare rounds outside the magazine.</summary>
    public int Reserve { get; private set; }
    public bool IsReloading { get; private set; }
    public float ReloadProgress { get { return IsReloading ? Mathf.Clamp01((Time.time - reloadStart) / Mathf.Max(0.01f, reloadTime)) : 0f; } }
    /// <summary>Spare magazines (a part-used one counts as one); -1 when endless.</summary>
    public int Magazines { get { return infiniteReserve ? -1 : (Reserve + magazineSize - 1) / magazineSize; } }
    public bool ReserveFull { get { return infiniteReserve || Reserve >= maxMagazines * magazineSize; } }
    public bool HasSpare { get { return infiniteReserve || Reserve > 0; } }

    static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    Vector3 restPos; Quaternion restRot; Vector3 magRest; Renderer[] modelRenderers, magRenderers;
    float nextShot, lastShot = -10f, reloadStart, effectOff, kickAmount, lightIntensity;
    bool modelVisible = true, magVisible = true;

    /// <summary>Called once by the inventory (the weapons not in hand are inactive, so Awake would come too late).</summary>
    public void Init(WeaponInventory owner)
    {
        Owner = owner;
        restPos = transform.localPosition; restRot = transform.localRotation;
        InMagazine = magazineSize; Reserve = infiniteReserve ? 0 : startMagazines * magazineSize;
        modelRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true),
            r => r != muzzleFire && !(r is ParticleSystemRenderer) && (magazine == null || !r.transform.IsChildOf(magazine)));
        if (magazine != null) { magRest = magazine.localPosition; magRenderers = magazine.GetComponentsInChildren<Renderer>(true); }
        if (muzzleFire != null) muzzleFire.enabled = false;
        if (flashLight != null) { lightIntensity = flashLight.intensity; flashLight.enabled = false; }
    }

    public bool StartReload()
    {
        if (IsReloading || InMagazine >= magazineSize || !HasSpare) return false;
        IsReloading = true; reloadStart = Time.time;
        return true;
    }

    public void CancelReload() { IsReloading = false; }

    void FinishReload()
    {
        int take = magazineSize - InMagazine;
        if (!infiniteReserve) { take = Mathf.Min(take, Reserve); Reserve -= take; }
        InMagazine += take; IsReloading = false;
    }

    /// <summary>A full magazine at once, no reload (tests and scripted events).</summary>
    public void FillMagazine() { InMagazine = magazineSize; IsReloading = false; }

    /// <summary>A picked-up magazine; false when the pouch is full.</summary>
    public bool AddMagazine()
    {
        if (ReserveFull) return false;
        Reserve = Mathf.Min(Reserve + magazineSize, maxMagazines * magazineSize);
        return true;
    }

    /// <summary>The weapon in hand, every frame. held: trigger down; pressed: went down this frame; aim: 0..1 scope.</summary>
    public void Tick(bool held, bool pressed, bool ready, Camera cam, float aim)
    {
        float now = Time.time;
        if (IsReloading && now >= reloadStart + reloadTime) FinishReload();
        if (!IsReloading && InMagazine == 0 && now > lastShot + 0.2f && HasSpare) StartReload();   // empty: reload without asking

        bool want = ready && !IsReloading && (mode == FireMode.Auto ? held : pressed);
        float interval = 1f / Mathf.Max(0.05f, roundsPerSecond);
        if (!want) { if (nextShot < now) nextShot = now; return; }
        // fixed cadence whatever the frame rate: the leftover time carries over to the next round
        for (int n = 0; now >= nextShot && n < 4; n++)
        {
            if (InMagazine <= 0) { StartReload(); break; }
            Fire(cam, Mathf.Lerp(spread, scopedSpread, aim));
            nextShot += interval;
            if (mode == FireMode.Semi) break;
        }
        if (nextShot < now) nextShot = now + interval;               // after a long hitch: no burst of make-up rounds
    }

    void Fire(Camera cam, float spreadDeg)
    {
        InMagazine--; lastShot = Time.time;
        bool hit = false;
        if (cam != null)
        {
            // registration only: the world through physics, the bots on their visible box (they are on Ignore Raycast)
            Vector2 s = Random.insideUnitCircle * spreadDeg;
            Vector3 dir = cam.transform.rotation * (Quaternion.Euler(s.y, s.x, 0f) * Vector3.forward);
            Vector3 origin = cam.transform.position;
            float dist = range;
            int n = Physics.RaycastNonAlloc(origin, dir, hitBuffer, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                // not our own body: the player's capsule is moved for queries only at the next physics step, so when
                // sprinting backwards it trails in front of the camera and used to swallow every round
                if (Owner != null && hitBuffer[i].collider.transform.IsChildOf(Owner.transform)) continue;
                if (hitBuffer[i].distance < dist) dist = hitBuffer[i].distance;
            }
            var bot = EnemyBot.RaycastBodies(origin, dir, ref dist);
            if (bot != null) { bot.TakeDamage(damage); hit = true; }
        }

        // muzzle fire: the same texture every round, turned to a random angle and a little bigger or smaller
        if (muzzleFire != null)
        {
            muzzleFire.enabled = true;
            muzzleFire.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            muzzleFire.transform.localScale = Vector3.one * (muzzleFireSize * Random.Range(0.8f, 1.2f));
        }
        if (flashLight != null) { flashLight.enabled = true; flashLight.intensity = lightIntensity * Random.Range(0.75f, 1.2f); }
        if (sparks != null) sparks.Emit(sparksPerRound);
        effectOff = Time.time + 0.045f;
        kickAmount = 1f;
        if (Owner != null) Owner.OnRoundFired(this, hit);
    }

    /// <summary>View-model motion. raise: 0 lowered out of sight (switching) .. 1 in hand; aim: 0..1 scope; bob: walk sway;
    /// swayTilt (degrees) and swayShift (metres): the lag behind a turning view, both in camera space.</summary>
    public void UpdatePose(float raise, float aim, Vector3 bob, Vector3 swayTilt, Vector3 swayShift)
    {
        float dt = Time.deltaTime;
        if (Time.time > effectOff)
        {
            if (muzzleFire != null) muzzleFire.enabled = false;
            if (flashLight != null) flashLight.enabled = false;
        }
        kickAmount *= Mathf.Exp(-dt * 16f);

        float r = ReloadProgress, dip = Mathf.Sin(r * Mathf.PI);           // down, turned towards you, and back up
        float low = 1f - raise; low *= low;
        Vector3 p = restPos + kick * kickAmount + (bob + swayShift) * (1f - aim)
                    + new Vector3(-0.03f, -0.09f, -0.03f) * dip + new Vector3(0f, -0.30f, -0.05f) * low;
        p = Vector3.Lerp(p, new Vector3(0f, -0.06f, restPos.z), aim * 0.7f);    // towards the eye while the scope comes up
        transform.localPosition = p;
        transform.localRotation = Quaternion.Euler(swayTilt * (1f - aim)) * restRot * Quaternion.Euler(-kickPitch * kickAmount + 18f * dip + 30f * low, -10f * dip, -32f * dip);

        bool show = aim < 0.85f;                                            // through the scope the gun itself is not drawn
        if (show != modelVisible) { modelVisible = show; foreach (var mr in modelRenderers) if (mr != null) mr.enabled = show; }

        if (magazine != null)                                               // the magazine drops out and comes back in
        {
            float o = r < 0.15f ? 0f : r < 0.35f ? (r - 0.15f) / 0.2f : r < 0.6f ? 1f : r < 0.85f ? 1f - (r - 0.6f) / 0.25f : 0f;
            o = o * o * (3f - 2f * o);
            magazine.localPosition = magRest + magazine.parent.InverseTransformVector(transform.TransformVector(Vector3.down * 0.22f * o));
            bool mv = show && o < 0.9f;
            if (mv != magVisible) { magVisible = mv; foreach (var mr in magRenderers) if (mr != null) mr.enabled = mv; }
        }
    }
}

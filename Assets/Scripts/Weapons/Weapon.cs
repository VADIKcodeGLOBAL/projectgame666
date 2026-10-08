using UnityEngine;

/// <summary>
/// One first-person weapon. A round is registered the moment it is fired (a ray from the camera, no projectile and no tracer);
/// ammo is a magazine plus spare rounds with a timed reload; every round shows the muzzle fire (a quad with the flash texture,
/// turned to a random angle), a short flash of light and a few sparks; the view model kicks, dips for the reload and drops
/// for a weapon switch. WeaponInventory owns the input and calls Tick and UpdatePose for the weapon in hand (HandWeapon); UpdatePose
/// ends by putting the placeholder arms (ViewModelArms) on the grips, so the hands follow every motion of the gun.
/// The recoil grows through a burst: every round fired as fast as the weapon allows raises its strength (RecoilStrength),
/// and the stronger it is the higher the camera kick, the more it wanders sideways, the harder the model is shoved back and
/// twisted and the further back it sits; once the firing stops the strength runs down to the first-round kick again.
/// </summary>
public class Weapon : HandWeapon
{
    public enum FireMode { Auto, Semi }

    [Header("Shooting")]
    public FireMode mode = FireMode.Auto;
    public float damage = 40f;
    public float roundsPerSecond = 10f;
    public float range = 600f;
    [Tooltip("Spread from the hip, degrees.")] public float spread = 0.5f;
    [Tooltip("Spread through the scope, degrees.")] public float scopedSpread = 0f;

    [Header("Recoil")]
    [Tooltip("Camera kick of the first round, degrees up.")] public float recoil = 0.35f;
    [Tooltip("Rounds fired as fast as the weapon allows until the recoil is at full strength.")] [Min(1)] public int recoilRounds = 10;
    [Tooltip("Camera kick at full strength, times the first round's.")] [Min(1f)] public float recoilGrowth = 2f;
    [Tooltip("Sideways kick at full strength, part of the upward kick; it wanders left and right through a burst.")] [Min(0f)] public float recoilSide = 0.35f;
    [Tooltip("From full strength back to the first-round kick once the firing stops, seconds.")] public float recoilCooldown = 0.6f;

    [Header("Ammo")]
    public int magazineSize = 30;
    public int startMagazines = 4;
    public int maxMagazines = 8;
    [Tooltip("Endless spare magazines (the sidearm).")] public bool infiniteReserve;
    public float reloadTime = 2.2f;

    [Header("View model")]
    public Transform muzzle;
    [Tooltip("Slides down out of the gun during the reload (optional).")] public Transform magazine;
    [Tooltip("Push of the model per round (camera space), metres.")] public Vector3 kick = new Vector3(0f, 0.005f, -0.048f);
    [Tooltip("Muzzle climb of the model per round, degrees.")] public float kickPitch = 1.8f;
    [Tooltip("Push back of a round at full recoil strength, times the first round's (the lift and the climb stay as they are).")] [Min(0f)] public float kickGrowth = 1.6f;
    [Tooltip("At full recoil strength the model sits this many rounds' push further back (not higher) while the firing lasts.")] [Min(0f)] public float kickHold = 1f;
    [Tooltip("Twist of the model per round at full recoil strength (turn towards the sideways kick, roll), degrees.")] [Min(0f)] public float kickShake = 1.2f;
    [Tooltip("How fast the model springs back to rest after a round, 1/s; lower is softer and slower.")] [Min(1f)] public float kickReturn = 20f;

    [Header("Effects")]
    public Renderer muzzleFire;
    public float muzzleFireSize = 0.2f;
    public Light flashLight;
    public ParticleSystem sparks;
    public int sparksPerRound = 6;
    public AudioClip[] shotClips;
    public float shotVolume = 0.55f, shotPitch = 1f;

    public int InMagazine { get; private set; }
    /// <summary>Spare rounds outside the magazine.</summary>
    public int Reserve { get; private set; }
    public override bool IsReloading { get { return reloading; } }
    public override bool CanAttack { get { return !reloading && InMagazine > 0; } }
    public float ReloadProgress { get { return IsReloading ? Mathf.Clamp01((Time.time - reloadStart) / Mathf.Max(0.01f, reloadTime)) : 0f; } }
    /// <summary>Spare magazines (a part-used one counts as one); -1 when endless.</summary>
    public int Magazines { get { return infiniteReserve ? -1 : (Reserve + magazineSize - 1) / magazineSize; } }
    public bool ReserveFull { get { return infiniteReserve || Reserve >= maxMagazines * magazineSize; } }
    public bool HasSpare { get { return infiniteReserve || Reserve > 0; } }
    /// <summary>0..1 how far the recoil has grown: up with every round fired as fast as the weapon allows, down once the firing stops.</summary>
    public float RecoilStrength { get { return Mathf.Clamp01(heat - Mathf.Max(0f, Time.time - coolFrom) / Mathf.Max(0.01f, recoilCooldown)); } }
    /// <summary>Camera kick of the last round, degrees: x to the right, y up.</summary>
    public Vector2 LastKick { get; private set; }

    const float SpringStep = 1f / 120f, SpringDamping = 0.6f;              // damping ratio below 1: the model overshoots a little forward
    static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    Vector3 restPos; Quaternion restRot; Vector3 magRest; Renderer[] modelRenderers, magRenderers;
    float nextShot, lastShot = -10f, reloadStart, effectOff, lightIntensity;
    float heat, coolFrom, sideDrift;                                       // recoil strength right after the last round, when it starts to cool, -1..1 side
    Vector3 kickPos, kickPosVel, kickRot, kickRotVel;                      // the model's recoil spring: offset (camera space, metres) and turn (degrees)
    ViewModelArms arms;
    bool modelVisible = true, magVisible = true, reloading;

    public override void Init(WeaponInventory owner)
    {
        Owner = owner;
        restPos = transform.localPosition; restRot = transform.localRotation;
        arms = GetComponent<ViewModelArms>();
        InMagazine = magazineSize; Reserve = infiniteReserve ? 0 : startMagazines * magazineSize;
        modelRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true),
            r => r != muzzleFire && !(r is ParticleSystemRenderer) && (magazine == null || !r.transform.IsChildOf(magazine)));
        if (magazine != null) { magRest = magazine.localPosition; magRenderers = magazine.GetComponentsInChildren<Renderer>(true); }
        if (muzzleFire != null) muzzleFire.enabled = false;
        if (flashLight != null) { lightIntensity = flashLight.intensity; flashLight.enabled = false; }
    }

    public override bool StartReload()
    {
        if (reloading || InMagazine >= magazineSize || !HasSpare) return false;
        reloading = true; reloadStart = Time.time;
        return true;
    }

    public override void CancelReload() { reloading = false; }

    // put away: the model comes back up at rest (the strength cools by the clock, also while away)
    void OnDisable() { kickPos = kickPosVel = kickRot = kickRotVel = Vector3.zero; }

    void FinishReload()
    {
        int take = magazineSize - InMagazine;
        if (!infiniteReserve) { take = Mathf.Min(take, Reserve); Reserve -= take; }
        InMagazine += take; reloading = false;
    }

    /// <summary>A full magazine at once, no reload (tests and scripted events).</summary>
    public void FillMagazine() { InMagazine = magazineSize; reloading = false; }

    /// <summary>A picked-up magazine; false when the pouch is full.</summary>
    public bool AddMagazine()
    {
        if (ReserveFull) return false;
        Reserve = Mathf.Min(Reserve + magazineSize, maxMagazines * magazineSize);
        return true;
    }

    /// <summary>The trigger is LMB; RMB is the scope (handled by the inventory), so altHeld / altPressed are not used.</summary>
    public override void Tick(bool held, bool pressed, bool altHeld, bool altPressed, bool ready, Camera cam, float aim)
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
        Kick();
        if (Owner != null) Owner.OnRoundFired(this, hit);
    }

    /// <summary>The recoil of one round at the strength the burst has reached, then the strength one round higher.</summary>
    void Kick()
    {
        float s = RecoilStrength;
        // the camera: higher with the strength, and sideways more and more; a new burst picks a side, a long one wanders
        sideDrift = s < 0.05f ? Random.Range(-1f, 1f) : Mathf.Clamp(sideDrift + Random.Range(-0.6f, 0.6f), -1f, 1f);
        float up = recoil * Mathf.Lerp(1f, recoilGrowth, s) * Random.Range(0.9f, 1.1f);
        LastKick = new Vector2(up * recoilSide * s * sideDrift, up);
        heat = Mathf.Min(1f, s + 1f / Mathf.Max(1, recoilRounds));
        coolFrom = Time.time + 1.5f / Mathf.Max(0.05f, roundsPerSecond);   // held fire (or clicking as fast as allowed) does not cool in between

        // the model: shoved back from where the burst holds it, harder at a later stage (back, not up), and twisted; a little
        // of the last shove is still in it, so the rounds of a fast weapon pile up a bit
        float g = Mathf.Lerp(1f, kickGrowth, s);
        Vector3 hold = Hold(s);
        kickPos = hold + new Vector3(kick.x * g, kick.y, kick.z * g) + (kickPos - hold) * 0.35f;
        kickRot = new Vector3(-kickPitch, kickShake * s * (0.6f * sideDrift + Random.Range(-0.4f, 0.4f)), kickShake * s * Random.Range(-1.5f, 1.5f))
                  + kickRot * 0.35f;
        kickPosVel = Vector3.zero; kickRotVel = Vector3.zero;
    }

    /// <summary>Where the model sits while a burst of this strength lasts: further back, at the same height.</summary>
    Vector3 Hold(float strength) { return new Vector3(kick.x, 0f, kick.z) * (kickHold * strength); }

    public override void UpdatePose(float raise, float aim, Vector3 bob, Vector3 swayTilt, Vector3 swayShift)
    {
        float dt = Time.deltaTime;
        if (Time.time > effectOff)
        {
            if (muzzleFire != null) muzzleFire.enabled = false;
            if (flashLight != null) flashLight.enabled = false;
        }
        // recoil spring: pulled towards where the burst holds the model, which slides home as the strength cools
        Vector3 hold = Hold(RecoilStrength);
        float w = kickReturn, damp = 2f * SpringDamping * w;
        for (float left = Mathf.Min(dt, 0.25f); left > 0f; left -= SpringStep)   // small steps: steady at any frame rate
        {
            float h = Mathf.Min(left, SpringStep);
            kickPosVel += ((hold - kickPos) * (w * w) - kickPosVel * damp) * h; kickPos += kickPosVel * h;
            kickRotVel += (-kickRot * (w * w) - kickRotVel * damp) * h; kickRot += kickRotVel * h;
        }

        float r = ReloadProgress, dip = Mathf.Sin(r * Mathf.PI);           // down, turned towards you, and back up
        float low = 1f - raise; low *= low;
        Vector3 p = restPos + kickPos + (bob + swayShift) * (1f - aim)
                    + new Vector3(-0.03f, -0.09f, -0.03f) * dip + new Vector3(0f, -0.30f, -0.05f) * low;
        p = Vector3.Lerp(p, new Vector3(0f, -0.06f, restPos.z), aim * 0.7f);    // towards the eye while the scope comes up
        transform.localPosition = p;
        Quaternion swayRot = Quaternion.Euler(swayTilt * (1f - aim));
        SwayTiltNow = Quaternion.Angle(Quaternion.identity, swayRot);
        transform.localRotation = swayRot * restRot * Quaternion.Euler(kickRot.x + 18f * dip + 30f * low, kickRot.y - 10f * dip, kickRot.z - 32f * dip);

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
        if (arms != null) arms.Solve();                                     // the hands go where the grips are now: every motion above included
    }
}

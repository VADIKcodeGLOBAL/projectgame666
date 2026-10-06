using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A field cannon standing in the level. Walk up to it: E — aim it (the view from the cannon), F — push it.
///  - Aiming: the crosshair in the middle of the screen is where the ball lands. The mouse turns the view; the carriage traverses
///    after it and the barrel takes the elevation of the ballistic arc to the point under the crosshair (the low arc; out of range:
///    the farthest the cannon reaches). The arc of the barrel as it stands and the blast circle where it will land are drawn.
///    LMB fires (CannonballSystem flies the ball), RMB zooms, E leaves. Reload between shots.
///  - Pushing: W/S push and pull, A/D or the mouse turn it, F lets go. It rolls on the ground (wheels turn, the carriage
///    follows the slope), stops at obstacles and at slopes steeper than maxPushSlope; you walk behind it.
/// While the cannon is in use the player's own controls and weapons are put away.
/// </summary>
[DisallowMultipleComponent]
public class FieldCannon : MonoBehaviour
{
    public enum Mode { Idle, Aiming, Pushing }

    [Header("Rig (set up by CannonSetup)")]
    [Tooltip("Rotates about its local X to raise the barrel; aligned with the root at rest.")] public Transform barrelPivot;
    public Transform muzzle, breech;
    public Transform[] wheels;
    public float[] wheelRadius;
    public BoxCollider body;
    public Camera gunnerCamera;
    [Tooltip("Where the gunner's eye is, in the cannon's space.")] public Vector3 sightLocal = new Vector3(0f, 2.2f, -1.2f);
    [Tooltip("Elevation of the barrel as the model was made, degrees.")] public float restElevation = 27.8f;
    [Tooltip("Ground contacts, in the cannon's space: front axle centre, rear left wheel, rear right wheel.")]
    public Vector3 frontContact = new Vector3(0f, 0f, 0.9f), rearLeftContact = new Vector3(-0.7f, 0f, -0.9f), rearRightContact = new Vector3(0.7f, 0f, -0.9f);

    [Header("Aiming")]
    public float minElevation = -12f, maxElevation = 30f;
    [Tooltip("Barrel elevation speed, degrees per second.")] public float elevationSpeed = 24f;
    [Tooltip("Carriage traverse speed, degrees per second.")] public float traverseSpeed = 50f;
    public float muzzleSpeed = 70f;
    public float reloadTime = 4f;
    public float lookSensitivity = 1.6f;
    public float zoomFov = 22f;
    public float aimRange = 900f;

    [Header("Pushing")]
    public float pushSpeed = 2.6f, pushAccel = 5f, turnSpeed = 55f;
    [Tooltip("The player walks this far behind the cannon's centre, m.")] public float handleDistance = 2.7f;
    public float maxPushSlope = 26f;

    [Header("Use")]
    [Tooltip("How close (to the carriage) the player has to be to use it, m.")] public float useDistance = 1.8f;

    [Header("Effects")]
    public Renderer muzzleFire;
    public ParticleSystem muzzleSmoke;
    public Light muzzleLight;
    public LineRenderer arcLine, blastRing;
    public AudioSource audioSource;
    public AudioClip[] shotClips;
    public float shotVolume = 1f;

    // ---- state the HUD and the tests read
    public static FieldCannon Nearby { get; private set; }
    public static FieldCannon Active { get; private set; }
    public Mode Current { get; private set; }
    public float Elevation { get; private set; }
    public float TargetElevation { get; private set; }
    public bool InRange { get; private set; }
    public bool OnTarget { get; private set; }
    public bool Loaded { get { return Time.time >= reloadEnd; } }
    public float ReloadProgress { get { return Loaded ? 1f : 1f - (reloadEnd - Time.time) / Mathf.Max(0.01f, reloadTime); } }
    public Vector3 AimPoint { get; private set; }
    public bool HasImpact { get; private set; }
    public Vector3 PredictedImpact { get; private set; }
    public float FlightTime { get; private set; }
    public float Range { get; private set; }
    public float PushSpeedNow { get { return pushVel; } }
    public bool PushBlocked { get; private set; }
    public int Shots { get; private set; }

    static readonly List<FieldCannon> all = new List<FieldCannon>();
    static readonly Unity.Profiling.ProfilerMarker AimMarker = new Unity.Profiling.ProfilerMarker("FieldCannon.Aim");
    static readonly RaycastHit[] rayHits = new RaycastHit[16];
    static readonly Collider[] overlaps = new Collider[16];
    readonly List<Vector3> arcPoints = new List<Vector3>(256);
    readonly Vector3[] ringPoints = new Vector3[48];

    Vector3 pivotLocal, muzzleLocal, boreLocal;
    float[] wheelAngle;
    Quaternion[] wheelRest;
    float yaw, camYaw, camPitch, reloadEnd, pushVel, recoil, kick, fireShown = -1f, baseFov = 70f;
    bool triggerArmed, testAim, testFireQueued; Vector3 testAimPoint; float testPush, testTurn; bool testPushActive;
    SimpleFirstPersonController user; Camera userCamera; AudioListener userListener, gunnerListener; WeaponInventory userWeapons;

    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); if (Active == this) Release(); if (Nearby == this) Nearby = null; }

    void Awake()
    {
        if (barrelPivot != null)
        {
            pivotLocal = transform.InverseTransformPoint(barrelPivot.position);
            muzzleLocal = barrelPivot.InverseTransformPoint(muzzle.position);
            boreLocal = (muzzleLocal - barrelPivot.InverseTransformPoint(breech.position)).normalized;
        }
        int n = wheels != null ? wheels.Length : 0;
        wheelAngle = new float[n]; wheelRest = new Quaternion[n];
        for (int i = 0; i < n; i++) wheelRest[i] = wheels[i].localRotation;
        yaw = transform.eulerAngles.y;
        Elevation = TargetElevation = Mathf.Clamp(8f, minElevation, maxElevation);
        ApplyElevation();
        if (gunnerCamera != null) { gunnerCamera.enabled = false; gunnerListener = gunnerCamera.GetComponent<AudioListener>(); if (gunnerListener != null) gunnerListener.enabled = false; }
        if (muzzleFire != null) muzzleFire.enabled = false;
        if (muzzleLight != null) muzzleLight.enabled = false;
        if (arcLine != null) arcLine.enabled = false;
        if (blastRing != null) blastRing.enabled = false;
    }

    // ------------------------------------------------------------------ geometry
    Quaternion PitchRot(float elevation) { return Quaternion.Euler(-(elevation - restElevation), 0f, 0f); }
    Vector3 MuzzleAt(float elevation) { return transform.TransformPoint(pivotLocal + PitchRot(elevation) * muzzleLocal); }
    Vector3 BoreAt(float elevation) { return transform.TransformDirection(PitchRot(elevation) * boreLocal); }
    public Vector3 MuzzlePosition { get { return MuzzleAt(Elevation); } }
    public Vector3 BoreDirection { get { return BoreAt(Elevation); } }
    void ApplyElevation() { if (barrelPivot != null) barrelPivot.localRotation = PitchRot(Elevation); }

    /// <summary>Puts the cannon on the ground at (x, z) facing yawDeg; false (nothing changed) if the ground there is too steep or blocked.</summary>
    public bool Place(Vector3 at, float yawDeg, bool checkBlocked)
    {
        Quaternion yawRot = Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 pF, pL, pR;
        if (!Ground(at + yawRot * frontContact, out pF) || !Ground(at + yawRot * rearLeftContact, out pL) || !Ground(at + yawRot * rearRightContact, out pR)) return false;
        Vector3 mid = (pL + pR) * 0.5f;
        Vector3 up = Vector3.Cross(pF - mid, pR - pL).normalized;
        if (up.y < 0f) up = -up;
        if (up.y < Mathf.Cos(maxPushSlope * Mathf.Deg2Rad)) return false;
        float y = pF.y - (up.x * (at.x - pF.x) + up.z * (at.z - pF.z)) / up.y;   // the ground plane under the centre
        Vector3 pos = new Vector3(at.x, y, at.z);
        Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(yawRot * Vector3.forward, up), up);
        if (checkBlocked && Blocked(pos, rot)) return false;
        transform.SetPositionAndRotation(pos, rot);
        yaw = yawDeg;
        return true;
    }

    bool Ground(Vector3 p, out Vector3 point)
    {
        point = p;
        int n = Physics.RaycastNonAlloc(new Ray(p + Vector3.up * 6f, Vector3.down), rayHits, 14f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = rayHits[i];
            if (IsOurs(h.collider) || h.collider.GetComponent<EnemyBot>() != null) continue;
            if (h.distance < best) { best = h.distance; point = h.point; }
        }
        return best < float.MaxValue;
    }

    bool IsOurs(Collider c)
    {
        if (c.transform.IsChildOf(transform)) return true;
        return user != null && c.transform.IsChildOf(user.transform) || WeaponInventory.Instance != null && c.transform.IsChildOf(WeaponInventory.Instance.transform);
    }

    /// <summary>Would the carriage (or the player walking behind it) overlap anything but the ground at this pose?</summary>
    bool Blocked(Vector3 pos, Quaternion rot)
    {
        if (body == null) return false;
        Vector3 centre = pos + rot * body.center, half = body.size * 0.5f;
        half.y *= 0.8f; centre += rot * Vector3.up * (body.size.y * 0.1f);   // not the bottom: the wheels sit on the ground
        int n = Physics.OverlapBoxNonAlloc(centre, half, overlaps, rot, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (!IsOurs(overlaps[i]) && !(overlaps[i] is TerrainCollider)) return true;
        if (user != null)
        {
            var m = user.Motor;
            Vector3 feet = pos - rot * Vector3.forward * handleDistance, g;
            if (Ground(feet, out g)) feet = g;
            Vector3 a = feet + Vector3.up * (m.Radius + 0.05f), b = feet + Vector3.up * (m.Height - m.Radius);
            n = Physics.OverlapCapsuleNonAlloc(a, b, m.Radius * 0.95f, overlaps, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!IsOurs(overlaps[i]) && !(overlaps[i] is TerrainCollider)) return true;
        }
        return false;
    }

    void TurnWheels(float distance, float yawDelta)
    {
        if (wheels == null) return;
        for (int i = 0; i < wheels.Length; i++)
        {
            Vector3 lp = transform.InverseTransformPoint(wheels[i].position);
            float d = distance - yawDelta * Mathf.Deg2Rad * lp.x;       // turning right: the left wheels roll forward, the right ones back
            float r = wheelRadius != null && i < wheelRadius.Length ? Mathf.Max(0.05f, wheelRadius[i]) : 0.3f;
            wheelAngle[i] += d / r * Mathf.Rad2Deg;
            wheels[i].localRotation = Quaternion.Euler(wheelAngle[i], 0f, 0f) * wheelRest[i];
        }
    }

    // ------------------------------------------------------------------ using it
    static bool Pressed(KeyCode k) { return Cursor.lockState == CursorLockMode.Locked && !SettingsMenu.IsOpen && Input.GetKeyDown(k); }

    void Take(SimpleFirstPersonController fp, Mode mode)
    {
        user = fp; Active = this; Current = mode;
        userWeapons = fp.GetComponent<WeaponInventory>();
        if (userWeapons != null) userWeapons.SetHolstered(true);
        fp.enabled = false;
        triggerArmed = false; pushVel = 0f;
        if (mode == Mode.Aiming)
        {
            userCamera = fp.cameraPivot != null ? fp.cameraPivot.GetComponent<Camera>() : null;
            if (userCamera != null) { baseFov = GameSettings.Fov; userCamera.enabled = false; userListener = userCamera.GetComponent<AudioListener>(); if (userListener != null) userListener.enabled = false; }
            gunnerCamera.enabled = true; if (gunnerListener != null) gunnerListener.enabled = true;
            camYaw = yaw; camPitch = 4f; recoil = 0f; kick = 0f;
        }
    }

    /// <summary>Back to walking (also when the game ends or the cannon goes away).</summary>
    public void Release()
    {
        if (Current == Mode.Aiming)
        {
            if (gunnerCamera != null) { gunnerCamera.enabled = false; if (gunnerListener != null) gunnerListener.enabled = false; }
            if (userCamera != null) { userCamera.enabled = true; if (userListener != null) userListener.enabled = true; }
            if (arcLine != null) arcLine.enabled = false;
            if (blastRing != null) blastRing.enabled = false;
        }
        if (user != null)
        {
            if (Current == Mode.Pushing) user.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            user.enabled = true;
        }
        if (userWeapons != null) userWeapons.SetHolstered(false);
        Current = Mode.Idle; testPushActive = false; testAim = false;
        if (Active == this) Active = null;
        user = null; userCamera = null; userListener = null; userWeapons = null;
    }

    public void EnterAim(SimpleFirstPersonController fp) { if (Current != Mode.Idle) Release(); if (fp != null && gunnerCamera != null) Take(fp, Mode.Aiming); }
    public void EnterPush(SimpleFirstPersonController fp) { if (Current != Mode.Idle) Release(); if (fp != null) Take(fp, Mode.Pushing); }

    /// <summary>Tests: aim at a world point instead of the crosshair; push / turn input (-1..1); fire on the next frame.</summary>
    public void SetTestAim(Vector3 point) { testAim = true; testAimPoint = point; }
    public void SetTestPush(float push, float turn) { testPushActive = true; testPush = push; testTurn = turn; }
    public void TestFire() { testFireQueued = true; }
    public void ClearReload() { reloadEnd = 0f; }

    void Update()
    {
        float dt = Time.deltaTime;
        var inv = WeaponInventory.Instance;
        var game = WaveSurvivalGame.Instance;
        bool dead = inv != null && inv.Health != null && inv.Health.IsDead;
        if (Current != Mode.Idle && (game != null && game.IsOver || dead || user == null)) { Release(); return; }

        if (Current == Mode.Idle)
        {
            // the player close to the carriage: offer it (the nearest cannon wins)
            var fp = inv != null ? inv.Controller : null;
            bool near = false;
            if (fp != null && fp.enabled && body != null && !dead && (game == null || !game.IsOver))
            {
                Vector3 p = fp.transform.position + Vector3.up * 0.9f;
                near = Vector3.Distance(body.ClosestPoint(p), p) < useDistance;
            }
            if (near && (Nearby == null || Nearby == this || !Nearby.isActiveAndEnabled)) Nearby = this;
            else if (!near && Nearby == this) Nearby = null;
            if (Nearby == this && Active == null)
            {
                if (Pressed(KeyCode.E)) EnterAim(fp);
                else if (Pressed(KeyCode.F)) EnterPush(fp);
            }
            UpdateEffects(dt);
            return;
        }
        if (Nearby == this) Nearby = null;
        if (dt <= 0f) return;                                           // paused
        if (Current == Mode.Aiming) { using (AimMarker.Auto()) UpdateAim(dt); } else UpdatePush(dt);
        UpdateEffects(dt);
    }

    void UpdateAim(float dt)
    {
        if (Pressed(KeyCode.E)) { Release(); return; }
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (!locked && Input.GetMouseButtonDown(0) && !SettingsMenu.IsOpen) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        // ---- the view
        bool zoom = locked && Input.GetMouseButton(1);
        float fovTarget = zoom ? zoomFov : GameSettings.Fov;
        gunnerCamera.fieldOfView = Mathf.MoveTowards(gunnerCamera.fieldOfView, fovTarget, 300f * dt);
        float sens = (user != null ? user.mouseSensitivity : 2f) * lookSensitivity * 0.5f * gunnerCamera.fieldOfView / Mathf.Max(1f, GameSettings.Fov);
        if (locked) { camYaw += Input.GetAxis("Mouse X") * sens; camPitch = Mathf.Clamp(camPitch - Input.GetAxis("Mouse Y") * sens, -45f, 40f); }

        // ---- the target: what is under the crosshair
        Vector3 eye = transform.position + Quaternion.Euler(0f, yaw, 0f) * sightLocal;
        if (testAim)
        {
            AimPoint = testAimPoint;                                   // and the view on it, as if the player aimed there
            Vector3 look = AimPoint - eye;
            camYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            camPitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
        }
        else
        {
            Vector3 dir = Quaternion.Euler(camPitch, camYaw, 0f) * Vector3.forward;
            int n = Physics.RaycastNonAlloc(new Ray(eye, dir), rayHits, aimRange, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            float best = aimRange; Vector3 p = eye + dir * aimRange;
            for (int i = 0; i < n; i++) if (!IsOurs(rayHits[i].collider) && rayHits[i].distance < best) { best = rayHits[i].distance; p = rayHits[i].point; }
            AimPoint = p;
        }

        // ---- traverse: the carriage turns after the target, on the ground it stands on
        Vector3 toT = AimPoint - transform.TransformPoint(pivotLocal); toT.y = 0f;
        float wantYaw = toT.sqrMagnitude > 1f ? Mathf.Atan2(toT.x, toT.z) * Mathf.Rad2Deg : yaw;
        float newYaw = Mathf.MoveTowardsAngle(yaw, wantYaw, traverseSpeed * dt);
        if (Mathf.Abs(Mathf.DeltaAngle(newYaw, yaw)) > 1e-4f)
        {
            float before = yaw;
            if (Place(transform.position, newYaw, false)) TurnWheels(0f, Mathf.DeltaAngle(before, newYaw));
        }

        // ---- elevation: the barrel angle whose arc comes down at the target's distance, over the real ground in between
        if (Time.time >= nextSolve && ((AimPoint - solvedAim).sqrMagnitude > 0.25f || Mathf.Abs(Mathf.DeltaAngle(yaw, solvedYaw)) > 0.3f || Time.time >= solveRefresh))
        {
            SolveElevation();
            nextSolve = Time.time + 0.05f; solveRefresh = Time.time + 0.5f; solvedAim = AimPoint; solvedYaw = yaw;
        }
        Elevation = Mathf.MoveTowards(Elevation, TargetElevation, elevationSpeed * dt);
        ApplyElevation();
        OnTarget = InRange && Mathf.Abs(Elevation - TargetElevation) < 0.25f && Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < 0.4f;

        // ---- where the ball goes with the barrel as it is: the arc and the blast circle
        var sys = CannonballSystem.Get();
        Vector3 impact; float tFlight;
        HasImpact = sys.Predict(MuzzlePosition, BoreDirection * muzzleSpeed, transform, out impact, out tFlight, arcPoints, 0.08f);
        PredictedImpact = impact; FlightTime = tFlight;
        Vector3 hz = impact - MuzzlePosition; hz.y = 0f; Range = hz.magnitude;
        DrawPreview(sys.radius);

        // ---- fire
        bool down = locked && Input.GetMouseButton(0);
        if (!down) triggerArmed = true;                                 // the click that brought you here is not a shot
        if ((down && triggerArmed && Input.GetMouseButtonDown(0)) || testFireQueued) { testFireQueued = false; Fire(); }

        // ---- the camera: the gunner's eye behind the breech, kicked up by a shot
        kick = Mathf.MoveTowards(kick, 0f, (2f + kick * 6f) * dt);
        gunnerCamera.transform.SetPositionAndRotation(eye, Quaternion.Euler(camPitch - kick, camYaw, 0f));
    }

    float nextSolve, solveRefresh, solvedYaw; Vector3 solvedAim = new Vector3(1e9f, 0f, 0f);

    /// <summary>Horizontal distance from the barrel's pivot to where a ball fired at this elevation comes down (far away if it never does).</summary>
    float RangeAt(float elevation, CannonballSystem sys)
    {
        Vector3 impact; float tf;
        Vector3 m = MuzzleAt(elevation), v = BoreAt(elevation) * muzzleSpeed;
        if (!sys.Predict(m, v, transform, out impact, out tf, null, 0.1f))
        {
            // flew past everything (off the map): where it comes down to the target's height, so the distance keeps growing
            float disc = v.y * v.y - 2f * sys.gravity * (AimPoint.y - m.y);
            if (disc < 0f) return 0f;                                   // never even gets that high
            float t = (v.y + Mathf.Sqrt(disc)) / sys.gravity;
            impact = m + v * t + Vector3.down * (0.5f * sys.gravity * t * t);
        }
        Vector3 d = impact - transform.TransformPoint(pivotLocal); d.y = 0f;
        return d.magnitude;
    }

    /// <summary>
    /// Bisection over the barrel's whole range with the same arc and the same ball as in flight: the landing distance grows with
    /// the elevation. A crest in front breaks that: low arcs stop on it, higher ones clear it and come down beyond. If no arc comes
    /// down at the target (it hides behind the crest and the barrel cannot lob that high) the shot is "not clear" and the barrel
    /// takes the lowest arc that clears the crest. About a dozen arcs per solve, solved again only when the target or the carriage moved.
    /// </summary>
    void SolveElevation()
    {
        var sys = CannonballSystem.Get();
        Vector3 d = AimPoint - transform.TransformPoint(pivotLocal); d.y = 0f;
        float want = d.magnitude, lo = minElevation, hi = maxElevation;
        Obstructed = false;
        if (RangeAt(hi, sys) < want) { TargetElevation = hi; InRange = false; return; }      // too far: as far as it goes
        if (RangeAt(lo, sys) > want) { TargetElevation = lo; InRange = false; return; }      // too close and too low to reach
        for (int i = 0; i < 12; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (RangeAt(mid, sys) < want) lo = mid; else hi = mid;
        }
        float got = RangeAt(hi, sys);
        TargetElevation = hi;
        InRange = Mathf.Abs(got - want) <= Mathf.Max(2.5f, want * 0.02f);
        Obstructed = !InRange;
    }

    /// <summary>The target is within reach but every arc to it is stopped by something in front (a crest, a rock).</summary>
    public bool Obstructed { get; private set; }

    /// <summary>Fires if loaded. False while reloading (or with every ball still in the air).</summary>
    public bool Fire()
    {
        if (!Loaded || barrelPivot == null) return false;
        var sys = CannonballSystem.Get();
        if (!sys.Launch(MuzzlePosition, BoreDirection * muzzleSpeed, transform)) return false;
        Shots++;
        reloadEnd = Time.time + reloadTime;
        recoil = 1f; kick = 2.5f;
        if (muzzleFire != null)
        {
            muzzleFire.enabled = true; fireShown = Time.time + 0.09f;
            muzzleFire.transform.localRotation = Quaternion.LookRotation(boreLocal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        }
        if (muzzleLight != null) { muzzleLight.enabled = true; muzzleLight.intensity = 6f; }
        if (muzzleSmoke != null) { muzzleSmoke.transform.position = MuzzlePosition; muzzleSmoke.transform.rotation = Quaternion.LookRotation(BoreDirection); muzzleSmoke.Emit(26); }
        if (audioSource != null && shotClips != null && shotClips.Length > 0)
        {
            audioSource.Stop(); audioSource.clip = shotClips[Random.Range(0, shotClips.Length)];
            audioSource.pitch = Random.Range(0.42f, 0.48f); audioSource.volume = shotVolume * GameSettings.ShotVolume; audioSource.Play();
        }
        return true;
    }

    void UpdatePush(float dt)
    {
        if (Pressed(KeyCode.F) || Pressed(KeyCode.E)) { Release(); return; }
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        float push = testPushActive ? testPush : Input.GetAxisRaw("Vertical");
        float turn = testPushActive ? testTurn : Input.GetAxisRaw("Horizontal");
        float mouseTurn = locked && !testPushActive ? Input.GetAxis("Mouse X") * user.mouseSensitivity * 0.5f : 0f;
        if (locked && !testPushActive) user.Pitch = user.Pitch - Input.GetAxis("Mouse Y") * user.mouseSensitivity;

        pushVel = Mathf.MoveTowards(pushVel, Mathf.Clamp(push, -1f, 1f) * pushSpeed * (push < 0f ? 0.6f : 1f), pushAccel * dt);
        float dYaw = Mathf.Clamp(turn, -1f, 1f) * turnSpeed * dt * (Mathf.Abs(pushVel) > 0.1f ? 1f : 0.7f) + mouseTurn;
        Vector3 fwd = Quaternion.Euler(0f, yaw + dYaw, 0f) * Vector3.forward;
        Vector3 target = transform.position + fwd * pushVel * dt;
        float before = yaw;
        PushBlocked = !Place(target, yaw + dYaw, true);
        if (PushBlocked && Mathf.Abs(dYaw) > 0f) PushBlocked = !Place(transform.position, yaw + dYaw, true);   // can it at least turn?
        if (PushBlocked) pushVel = 0f;
        else TurnWheels(pushVel * dt, Mathf.DeltaAngle(before, yaw));

        // the player walks behind the trail
        Vector3 feet = transform.position - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * handleDistance, g;
        if (Ground(feet, out g)) feet = g;
        user.Teleport(feet + Vector3.up * 0.02f);
        user.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void UpdateEffects(float dt)
    {
        if (muzzleFire != null && muzzleFire.enabled && Time.time > fireShown) muzzleFire.enabled = false;
        if (muzzleLight != null && muzzleLight.enabled)
        {
            muzzleLight.intensity = Mathf.MoveTowards(muzzleLight.intensity, 0f, 50f * dt);
            if (muzzleLight.intensity <= 0f) muzzleLight.enabled = false;
        }
        if (recoil > 0f && barrelPivot != null)                         // the barrel jumps back and slides home
        {
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 1.6f);
            float k = recoil * recoil;
            barrelPivot.localPosition = pivotLocal - boreLocal * (0.35f * k);
        }
    }

    void DrawPreview(float blast)
    {
        if (arcLine != null)
        {
            arcLine.enabled = HasImpact;
            if (HasImpact)
            {
                arcLine.positionCount = arcPoints.Count;
                for (int i = 0; i < arcPoints.Count; i++) arcLine.SetPosition(i, arcPoints[i]);
            }
        }
        if (blastRing != null)
        {
            blastRing.enabled = HasImpact;
            if (!HasImpact) return;
            var t = Terrain.activeTerrain;
            for (int i = 0; i < ringPoints.Length; i++)
            {
                float a = i * Mathf.PI * 2f / ringPoints.Length;
                Vector3 p = PredictedImpact + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * blast;
                p.y = (t != null ? t.SampleHeight(p) + t.transform.position.y : PredictedImpact.y) + 0.15f;
                ringPoints[i] = p;
            }
            blastRing.positionCount = ringPoints.Length;
            blastRing.SetPositions(ringPoints);
        }
    }
}

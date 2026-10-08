using UnityEngine;

/// <summary>
/// The player's weapons (guns and the sword, HandWeapon). With the mouse captured: LMB fire / slash, R reload,
/// RMB scope (weapons that have one) or the weapon's own second attack (the sword's chop), 1-5 or the mouse wheel to switch. Plays the shot sounds through a small pool of voices, gives picked-up magazines
/// to a weapon and keeps the numbers the HUD shows.
/// </summary>
public class WeaponInventory : MonoBehaviour
{
    public Camera cam;
    public HandWeapon[] weapons;
    public AudioSource audioSource;
    [Tooltip("Shot sounds playing at once; the oldest one is cut when a new round needs a voice.")] public int voices = 8;
    [Tooltip("Lower the old weapon + raise the new one, seconds.")] public float switchTime = 0.45f;
    public float scopeTime = 0.18f;

    [Header("Sway")]
    [Tooltip("The gun lags behind the turning view: degrees of tilt per 100 °/s of turning.")] [Min(0f)] public float swayAngle = 1.2f;
    [Tooltip("The gun slides the other way while turning: metres per 100 °/s of turning.")] [Min(0f)] public float swayShift = 0.006f;
    [Tooltip("Most the gun tilts away from the view, degrees.")] [Min(0f)] public float swayMaxAngle = 4f;
    [Tooltip("Most the gun slides away from its place, metres.")] [Min(0f)] public float swayMaxShift = 0.02f;
    [Tooltip("Side lean of the gun into a turn, part of the tilt.")] [Range(0f, 2f)] public float swayRoll = 0.6f;
    [Tooltip("How fast the gun follows the view and comes back to rest, 1/s; lower is softer and lazier.")] [Min(0.5f)] public float swaySmoothing = 9f;

    public static WeaponInventory Instance { get; private set; }
    /// <summary>The player's components, cached for the supplies.</summary>
    public PlayerHealth Health { get; private set; }
    public SimpleFirstPersonController Controller { get { return fp; } }
    public int CurrentIndex { get; private set; }
    public HandWeapon Current { get { return weapons != null && weapons.Length > 0 ? weapons[CurrentIndex] : null; } }
    /// <summary>The weapon in hand if it is a gun (null for the sword).</summary>
    public Weapon CurrentGun { get { return Current as Weapon; } }
    /// <summary>0..1 how far the scope is up.</summary>
    public float Aim { get; private set; }
    public bool IsScoped { get { return Aim > 0.85f; } }
    public bool IsSwitching { get { return pending >= 0 || raise < 1f; } }
    public int ShotsFired { get; private set; }
    public int Hits { get; private set; }
    public float LastHitTime { get; private set; } = -10f;
    public string PickupText { get; private set; }
    public float PickupTime { get; private set; } = -10f;

    SimpleFirstPersonController fp;
    float baseFov = 70f, raise = 1f, bobT, lastYaw, lastPitch;
    Vector2 turnRate;                                                      // smoothed turning of the view, °/s: x yaw (right +), y pitch (down +)
    int pending = -1;
    bool triggerArmed, prevHeld, prevAlt, testActive, testTrigger, testAim;
    AudioSource[] voicePool; int nextVoice;

    int Target { get { return pending >= 0 ? pending : CurrentIndex; } }

    void Awake()
    {
        Instance = this;
        fp = GetComponent<SimpleFirstPersonController>(); Health = GetComponent<PlayerHealth>();
        ResetSway();                                                       // no sway kick on the first frame
        if (fp != null) fp.Teleported += ResetSway;                        // a teleport is not a turn
        baseFov = GameSettings.Fov;
        if (cam != null) cam.fieldOfView = baseFov;
        weapons = weapons == null ? new HandWeapon[0] : System.Array.FindAll(weapons, x => x != null);   // a missing reference must not break the rest
        if (weapons.Length == 0) { enabled = false; return; }
        for (int i = 0; i < weapons.Length; i++) { weapons[i].Init(this); weapons[i].gameObject.SetActive(i == CurrentIndex); }
        if (audioSource != null)
        {
            // one AudioSource per voice: re-pitching a shared source would re-pitch the tails of the previous rounds too
            voicePool = new AudioSource[Mathf.Max(1, voices)];
            voicePool[0] = audioSource;
            for (int i = 1; i < voicePool.Length; i++)
            {
                var a = gameObject.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = audioSource.spatialBlend; a.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
                voicePool[i] = a;
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (fp != null) fp.Teleported -= ResetSway;
    }

    /// <summary>Drives the trigger and the scope without a mouse (automated tests). ClearTestInput gives control back.</summary>
    public void SetTestInput(bool trigger, bool aim) { testActive = true; testTrigger = trigger; testAim = aim; }
    public void ClearTestInput() { testActive = false; testTrigger = false; testAim = false; }

    /// <summary>Weapons put away (using a cannon): the one in hand is hidden, nothing fires, the view is not zoomed.</summary>
    public bool Holstered { get; private set; }
    public void SetHolstered(bool on)
    {
        if (Holstered == on) return;
        Holstered = on;
        var w = Current;
        if (w != null) { w.CancelReload(); w.gameObject.SetActive(!on); }
        Aim = 0f; triggerArmed = false; prevHeld = true; prevAlt = true;
        if (cam != null) cam.fieldOfView = baseFov;
        if (fp != null) fp.lookScale = 1f;
    }

    public void Select(int index)
    {
        if (index < 0 || index >= weapons.Length || index == Target) return;
        Current.CancelReload();
        pending = index == CurrentIndex ? -1 : index;
    }

    // LateUpdate: the mouse look of this frame is applied, so the round goes where the crosshair is
    void LateUpdate()
    {
        Vector2 turned = ViewTurned();                                     // measured every frame, so a cannon spell leaves no jump behind
        var w = Current; if (w == null || Holstered) { turnRate = Vector2.zero; return; }
        var game = WaveSurvivalGame.Instance;
        bool over = game != null && game.IsOver;
        bool held, aimHeld;
        if (testActive) { held = testTrigger; aimHeld = testAim; }
        else
        {
            // the click that captures the mouse is not a shot: the trigger arms once LMB is released with the mouse captured
            bool locked = Cursor.lockState == CursorLockMode.Locked, down = Input.GetMouseButton(0);
            if (!locked) triggerArmed = false; else if (!down) triggerArmed = true;
            held = locked && triggerArmed && down;
            aimHeld = locked && Input.GetMouseButton(1);
            if (locked && !over)
            {
                for (int i = 0; i < weapons.Length && i < 9; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Select(i);
                float wheel = Input.mouseScrollDelta.y;
                if (wheel != 0f) Select((Target + (wheel < 0f ? 1 : -1) + weapons.Length) % weapons.Length);
                if (Input.GetKeyDown(KeyCode.R)) w.StartReload();
            }
        }
        if (over) { held = false; aimHeld = false; }
        bool pressed = held && !prevHeld; prevHeld = held;
        bool altHeld = aimHeld && !w.hasScope, altPressed = altHeld && !prevAlt; prevAlt = altHeld;   // RMB for a weapon without a scope

        // switching: the old weapon goes down, the new one comes up
        float halfSwitch = Mathf.Max(0.01f, switchTime * 0.5f);
        if (pending >= 0)
        {
            raise = Mathf.MoveTowards(raise, 0f, Time.deltaTime / halfSwitch);
            if (raise <= 0f)
            {
                w.gameObject.SetActive(false);
                CurrentIndex = pending; pending = -1; w = Current;
                w.gameObject.SetActive(true);
            }
        }
        else raise = Mathf.MoveTowards(raise, 1f, Time.deltaTime / halfSwitch);

        baseFov = GameSettings.Fov;                                        // from the settings menu
        bool aiming = aimHeld && w.hasScope && !w.IsReloading && pending < 0 && raise > 0.99f;
        Aim = Mathf.MoveTowards(Aim, aiming ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, scopeTime));
        if (cam != null)
        {
            cam.fieldOfView = w.hasScope ? Mathf.Lerp(baseFov, w.scopeFov, Aim * Aim) : baseFov;      // FOV slider shows at once, also in the pause menu
            if (fp != null) fp.lookScale = cam.fieldOfView / baseFov;         // the same mouse move turns the view less when zoomed in
        }

        w.Tick(held, pressed, altHeld, altPressed, pending < 0 && raise > 0.7f, cam, Aim);

        float speed = fp != null && fp.IsGrounded ? fp.PlanarVelocity.magnitude : 0f;
        bobT += Time.deltaTime * speed * 1.35f;
        float k = Mathf.Clamp01(speed / 8.5f);

        // sway: the gun trails the turning view a little and settles back when the view stops. The turn rate is smoothed
        // first and the result limited after, so a flick sways the gun the same at 30 and at 144 fps (a clamp before
        // the smoothing would cut a one-frame flick harder the higher the frame rate)
        float dt = Time.deltaTime;
        if (dt > 0f) turnRate = Vector2.Lerp(turnRate, turned / dt, 1f - Mathf.Exp(-Mathf.Max(0.5f, swaySmoothing) * dt));
        float maxA = Mathf.Max(0f, swayMaxAngle), maxS = Mathf.Max(0f, swayMaxShift);
        Vector2 tilt = new Vector2(Mathf.Clamp(-turnRate.x * swayAngle / 100f, -maxA, maxA), Mathf.Clamp(-turnRate.y * swayAngle / 100f, -maxA, maxA));
        Vector2 slide = new Vector2(Mathf.Clamp(-turnRate.x * swayShift / 100f, -maxS, maxS), Mathf.Clamp(turnRate.y * swayShift / 100f, -maxS, maxS));
        w.UpdatePose(raise, Aim, new Vector3(Mathf.Cos(bobT) * 0.007f, -Mathf.Abs(Mathf.Sin(bobT)) * 0.009f, 0f) * k,
                     new Vector3(tilt.y, tilt.x, tilt.x * swayRoll), new Vector3(slide.x, slide.y, 0f));
    }

    /// <summary>How far the view turned since the last frame, degrees: x yaw (right +), y pitch (down +).</summary>
    Vector2 ViewTurned()
    {
        float yaw = transform.eulerAngles.y, pitch = fp != null ? fp.Pitch : 0f;
        var d = new Vector2(Mathf.DeltaAngle(lastYaw, yaw), pitch - lastPitch);
        lastYaw = yaw; lastPitch = pitch;
        return d;
    }

    /// <summary>The current view becomes the sway's resting point (after a teleport or a scripted turn: no jolt of the gun).</summary>
    public void ResetSway()
    {
        lastYaw = transform.eulerAngles.y; lastPitch = fp != null ? fp.Pitch : 0f;
        turnRate = Vector2.zero;
    }

    /// <summary>Tests: the gun's current tilt against its resting pose, degrees (0 when the view has been still a while).</summary>
    public float SwayAngleNow { get { return Current != null ? Current.SwayTiltNow : 0f; } }

    public void OnRoundFired(Weapon w, bool hit)
    {
        ShotsFired++;
        if (hit) { Hits++; LastHitTime = Time.time; }
        if (fp != null)
        {
            fp.AddRecoil(w.recoil * (1f - 0.5f * Aim));
            lastPitch = fp.Pitch;                                       // the kick is the weapon's own animation, not a turn to sway after
        }
        if (w.shotClips != null && w.shotClips.Length > 0)
            PlaySound(w.shotClips[Random.Range(0, w.shotClips.Length)], w.shotVolume, w.shotPitch * Random.Range(0.95f, 1.05f));
    }

    /// <summary>A weapon sound through the pool of voices (the oldest is cut), at the shot volume of the settings.</summary>
    public void PlaySound(AudioClip clip, float volume, float pitch)
    {
        if (voicePool == null || clip == null) return;
        var a = voicePool[nextVoice]; nextVoice = (nextVoice + 1) % voicePool.Length;
        a.Stop(); a.clip = clip; a.pitch = pitch; a.volume = volume * GameSettings.ShotVolume; a.Play();
    }

    /// <summary>A blade connected with this many bots: the hit marker, a small jolt of the view.</summary>
    public void OnMeleeHit(HandWeapon w, int bots)
    {
        Hits += bots; LastHitTime = Time.time;
        if (fp != null) { fp.AddRecoil(-0.6f); lastPitch = fp.Pitch; }    // the blade bites: the view dips a little
    }

    /// <summary>A picked-up magazine goes to the weapon in hand, or to the first one with room; false if every pouch is full.</summary>
    public bool GiveMagazine()
    {
        Weapon t = CurrentGun != null && !CurrentGun.ReserveFull ? CurrentGun : null;
        if (t == null) foreach (var x in weapons) { var g = x as Weapon; if (g != null && !g.ReserveFull) { t = g; break; } }
        if (t == null || !t.AddMagazine()) return false;
        PickupText = "+1 MAGAZINE  " + t.displayName; PickupTime = Time.time;
        return true;
    }
}

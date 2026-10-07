using UnityEngine;

/// <summary>
/// The player's weapons. With the mouse captured: LMB fire, R reload, RMB scope (weapons that have one),
/// 1-4 or the mouse wheel to switch. Plays the shot sounds through a small pool of voices, gives picked-up magazines
/// to a weapon and keeps the numbers the HUD shows.
/// </summary>
public class WeaponInventory : MonoBehaviour
{
    public Camera cam;
    public Weapon[] weapons;
    public AudioSource audioSource;
    [Tooltip("Shot sounds playing at once; the oldest one is cut when a new round needs a voice.")] public int voices = 8;
    [Tooltip("Lower the old weapon + raise the new one, seconds.")] public float switchTime = 0.45f;
    public float scopeTime = 0.18f;

    [Header("Sway")]
    [Tooltip("The gun lags behind the turning view: degrees of tilt per 100 °/s of turning.")] public float swayAngle = 1.2f;
    [Tooltip("The gun slides the other way while turning: metres per 100 °/s of turning.")] public float swayShift = 0.006f;
    [Tooltip("Most the gun tilts away from the view, degrees.")] public float swayMaxAngle = 4f;
    [Tooltip("Side lean of the gun into a turn, part of the tilt.")] public float swayRoll = 0.6f;
    [Tooltip("How fast the gun follows the view and comes back to rest, 1/s; lower is softer and lazier.")] public float swaySmoothing = 9f;

    [Header("Sprint FOV")]
    [Tooltip("The view widens this much at full sprint speed, degrees; the player's FOV effects setting multiplies it (0 = off).")] public float sprintFovKick = 7f;
    [Tooltip("How fast the widening follows the sprint, 1/s; lower is softer.")] public float sprintFovSmoothing = 6f;

    public static WeaponInventory Instance { get; private set; }
    /// <summary>The player's components, cached for the supplies.</summary>
    public PlayerHealth Health { get; private set; }
    public SimpleFirstPersonController Controller { get { return fp; } }
    public int CurrentIndex { get; private set; }
    public Weapon Current { get { return weapons != null && weapons.Length > 0 ? weapons[CurrentIndex] : null; } }
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
    float baseFov = 70f, raise = 1f, bobT, lastYaw, lastPitch, sprintFov;    // sprintFov: 0..1 how far the sprint widening is in
    Vector2 sway;                                                          // x yaw, y pitch of the gun against the view, degrees
    int pending = -1;
    bool triggerArmed, prevHeld, testActive, testTrigger, testAim;
    AudioSource[] voicePool; int nextVoice;

    int Target { get { return pending >= 0 ? pending : CurrentIndex; } }

    void Awake()
    {
        Instance = this;
        fp = GetComponent<SimpleFirstPersonController>(); Health = GetComponent<PlayerHealth>();
        lastYaw = transform.eulerAngles.y; lastPitch = fp != null ? fp.Pitch : 0f;   // no sway kick on the first frame
        baseFov = GameSettings.Fov;
        if (cam != null) cam.fieldOfView = baseFov;
        weapons = weapons == null ? new Weapon[0] : System.Array.FindAll(weapons, x => x != null);   // a missing reference must not break the rest
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

    void OnDestroy() { if (Instance == this) Instance = null; }

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
        Aim = 0f; triggerArmed = false; prevHeld = true; sprintFov = 0f;
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
        var w = Current; if (w == null || Holstered) { sway = Vector2.zero; return; }
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
        // sprint: the view widens a little with the speed above walking; pressing into a wall widens nothing
        float run = fp != null && fp.IsSprinting ? Mathf.InverseLerp(fp.walkSpeed, fp.sprintSpeed, fp.PlanarVelocity.magnitude) : 0f;
        sprintFov = Mathf.Lerp(sprintFov, run, 1f - Mathf.Exp(-sprintFovSmoothing * Time.deltaTime));
        if (cam != null)
        {
            float fov = w.hasScope ? Mathf.Lerp(baseFov, w.scopeFov, Aim * Aim) : baseFov;
            if (fp != null) fp.lookScale = fov / baseFov;                     // the same mouse move turns the view less when zoomed in; the sprint widening leaves it alone
            cam.fieldOfView = fov + sprintFov * (1f - Aim) * sprintFovKick * GameSettings.FovEffects;  // FOV sliders show at once, also in the pause menu; the scope takes the widening away
        }

        w.Tick(held, pressed, pending < 0 && raise > 0.7f, cam, Aim);

        float speed = fp != null && fp.IsGrounded ? fp.PlanarVelocity.magnitude : 0f;
        bobT += Time.deltaTime * speed * 1.35f;
        float k = Mathf.Clamp01(speed / 8.5f);

        // sway: the gun trails the turning view a little and settles back when the view stops
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            float perDegPerSec = swayAngle / 100f / dt;
            Vector2 target = new Vector2(Mathf.Clamp(-turned.x * perDegPerSec, -swayMaxAngle, swayMaxAngle),
                                         Mathf.Clamp(-turned.y * perDegPerSec, -swayMaxAngle, swayMaxAngle));
            sway = Vector2.Lerp(sway, target, 1f - Mathf.Exp(-swaySmoothing * dt));
        }
        float shift = swayAngle > 0.0001f ? swayShift / swayAngle : 0f;
        w.UpdatePose(raise, Aim, new Vector3(Mathf.Cos(bobT) * 0.007f, -Mathf.Abs(Mathf.Sin(bobT)) * 0.009f, 0f) * k,
                     new Vector3(sway.y, sway.x, sway.x * swayRoll), new Vector3(sway.x, -sway.y, 0f) * shift);
    }

    /// <summary>How far the view turned since the last frame, degrees: x yaw (right +), y pitch (down +).</summary>
    Vector2 ViewTurned()
    {
        float yaw = transform.eulerAngles.y, pitch = fp != null ? fp.Pitch : 0f;
        var d = new Vector2(Mathf.DeltaAngle(lastYaw, yaw), pitch - lastPitch);
        lastYaw = yaw; lastPitch = pitch;
        return d;
    }

    public void OnRoundFired(Weapon w, bool hit)
    {
        ShotsFired++;
        if (hit) { Hits++; LastHitTime = Time.time; }
        if (fp != null) fp.AddRecoil(w.recoil * (1f - 0.5f * Aim));
        if (voicePool != null && w.shotClips != null && w.shotClips.Length > 0)
        {
            var a = voicePool[nextVoice]; nextVoice = (nextVoice + 1) % voicePool.Length;
            a.Stop(); a.clip = w.shotClips[Random.Range(0, w.shotClips.Length)];
            a.pitch = w.shotPitch * Random.Range(0.95f, 1.05f); a.volume = w.shotVolume * GameSettings.ShotVolume; a.Play();
        }
    }

    /// <summary>A picked-up magazine goes to the weapon in hand, or to the first one with room; false if every pouch is full.</summary>
    public bool GiveMagazine()
    {
        Weapon t = Current != null && !Current.ReserveFull ? Current : null;
        if (t == null) foreach (var x in weapons) if (!x.ReserveFull) { t = x; break; }
        if (t == null || !t.AddMagazine()) return false;
        PickupText = "+1 MAGAZINE  " + t.displayName; PickupTime = Time.time;
        return true;
    }
}

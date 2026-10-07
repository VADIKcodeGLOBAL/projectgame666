using UnityEngine;

/// <summary>
/// First-person walker: WASD, mouse look, Shift to sprint (either Shift, as long as it is held — also through jumps),
/// Space to jump. Input, speeds and the jump rules live here; the movement itself (slopes, steps, walls, gravity) is CharacterMotor.
/// The same speed uphill, downhill, across a slope and on the flat; standing still on a slope stays still.
/// A jump still counts for a moment after leaving an edge (coyote time) and a press just before landing is kept (jump buffer).
/// Keeps its momentum in the air. Click the Game view to capture the mouse; Esc opens the settings menu.
/// The weapon kick reaches the view here: lifted over a few frames, and part of the climb the player did not pull down comes back after the firing.
/// </summary>
[RequireComponent(typeof(CharacterMotor))]
public class SimpleFirstPersonController : MonoBehaviour
{
    public Transform cameraPivot;
    public float walkSpeed = 5.5f;
    public float sprintSpeed = 8.5f;
    public float jumpHeight = 1.1f;
    [Tooltip("How fast the speed follows the input on the ground / in the air, m/s².")] public float groundAccel = 45f, airAccel = 10f;
    [Tooltip("Base mouse look speed; the player's setting (GameSettings.MouseSensitivity) multiplies it.")] public float mouseSensitivity = 2.2f;
    [Tooltip("A jump still works this long after the ground was lost, s.")] public float coyoteTime = 0.12f;
    [Tooltip("A jump pressed this long before landing is done on landing, s.")] public float jumpBuffer = 0.12f;
    [Tooltip("Falling below this height puts the player back where they started.")] public float killHeight = -30f;

    [Tooltip("Multiplier of the mouse look, set by the weapons (lower while zoomed in).")] public float lookScale = 1f;

    [Header("Weapon recoil")]
    [Tooltip("How fast a round's kick lifts the view, 1/s; higher is snappier (the kick takes a few frames, not one jump).")] public float recoilSnap = 30f;
    [Tooltip("Part of the climb the player did not pull down that comes back once the firing stops, 0..1.")] [Range(0f, 1f)] public float recoilRecovery = 0.5f;
    [Tooltip("How fast the view comes back down after the firing, 1/s.")] public float recoilRecoverySpeed = 6f;
    [Tooltip("Pause after a round before the view starts coming back, s; longer than the gap between the rounds of an automatic weapon.")] public float recoilRecoveryDelay = 0.15f;

    /// <summary>Mouse look speed with the player's setting from the settings menu, without the zoom scaling.</summary>
    public float LookSensitivity { get { return mouseSensitivity * GameSettings.MouseSensitivity; } }

    public CharacterMotor Motor { get { if (motor == null) motor = GetComponent<CharacterMotor>(); return motor; } }
    public Vector3 PlanarVelocity { get { return Motor.PlanarVelocity; } }
    public float VerticalVelocity { get { return Motor.VerticalVelocity; } }
    public bool IsGrounded { get { return Motor.IsGrounded; } }
    public bool IsSprinting { get; private set; }
    /// <summary>Camera pitch, degrees (negative = up). Setting it puts the view there on purpose: recoil still on its way
    /// and the climb waiting to come back are dropped.</summary>
    public float Pitch { get { return pitch; } set { ClearRecoil(); SetPitch(value); } }
    /// <summary>Weapon recoil: lifts the view (and turns it, + right) over the next few frames.</summary>
    public void AddRecoil(float degreesUp, float degreesRight = 0f) { recoilPending += new Vector2(degreesRight, degreesUp); lastRecoilTime = Time.time; }
    /// <summary>Forgets the kick still on its way and the climb waiting to come back.</summary>
    public void ClearRecoil() { recoilPending = Vector2.zero; recoilDebt = 0f; }

    /// <summary>Speed supply: multiplier and seconds left (0 when none).</summary>
    public float SpeedMultiplier { get { return Time.time < boostEnd ? boostMultiplier : 1f; } }
    public float BoostTimeLeft { get { return Mathf.Max(0f, boostEnd - Time.time); } }
    public float BoostDuration { get; private set; }
    /// <summary>Takes the stronger multiplier and restarts the timer.</summary>
    public void ApplySpeedBoost(float multiplier, float seconds)
    {
        boostMultiplier = Time.time < boostEnd ? Mathf.Max(boostMultiplier, multiplier) : multiplier;
        boostEnd = Time.time + seconds; BoostDuration = seconds;
    }
    public void ClearSpeedBoost() { boostEnd = 0f; }

    CharacterMotor motor;
    float pitch, lastGroundedTime = -10f, jumpPressedTime = -10f, boostEnd, boostMultiplier = 1f;
    Vector2 recoilPending;                                               // kick not yet in the view, degrees: x right, y up
    float recoilDebt, lastRecoilTime = -10f;                             // climb from the kicks the player has not pulled down, degrees
    Vector3 startPosition;
    bool testActive, testSprint, testJump; Vector3 testMove;

    void Awake()
    {
        motor = GetComponent<CharacterMotor>();
        if (motor.smoothedChild == null) motor.smoothedChild = cameraPivot;
        startPosition = transform.position;
    }

    /// <summary>Drives the controller without a keyboard (automated tests). ClearTestInput gives control back.</summary>
    public void SetTestInput(Vector3 move, bool sprint, bool jump) { testActive = true; testMove = move; testSprint = sprint; testJump |= jump; }
    public void ClearTestInput() { testActive = false; testJump = false; }

    void Update()
    {
        var game = WaveSurvivalGame.Instance;
        bool over = game != null && game.IsOver;
        if (Input.GetMouseButtonDown(0) && !SettingsMenu.IsOpen && !over) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float sens = LookSensitivity * lookScale;
            transform.Rotate(0f, Input.GetAxis("Mouse X") * sens, 0f);
            float down = -Input.GetAxis("Mouse Y") * sens;
            if (down > 0f) recoilDebt = Mathf.Max(0f, recoilDebt - down);  // pulling the gun down pays the climb off
            SetPitch(pitch + down);
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;                                            // paused (settings menu)
        UpdateRecoil(dt);
        Vector3 input = testActive ? testMove : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        bool sprintKey = testActive ? testSprint : (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        bool jumpKey = testActive ? testJump : Input.GetKeyDown(KeyCode.Space);
        testJump = false;
        if (over) { input = Vector3.zero; jumpKey = false; }
        if (input.sqrMagnitude > 1f) input.Normalize();
        if (jumpKey) jumpPressedTime = Time.time;

        IsSprinting = sprintKey && input.sqrMagnitude > 0.01f;          // Shift held = sprint, whatever happened before
        Vector3 heading = transform.TransformDirection(input); heading.y = 0f;
        float speed = (IsSprinting ? sprintSpeed : walkSpeed) * SpeedMultiplier;

        var m = Motor;
        if (m.IsGrounded) lastGroundedTime = Time.time;
        Vector3 planar = m.PlanarVelocity;
        if (m.IsGrounded) planar = Vector3.MoveTowards(planar, heading * speed, groundAccel * dt);
        else
        {
            // in the air: keep the momentum and steer a little; with Shift held the target stays the sprint speed
            Vector3 target = heading.sqrMagnitude > 0.01f ? heading * speed : planar;
            planar = Vector3.MoveTowards(planar, target, airAccel * dt);
        }
        m.PlanarVelocity = planar;

        if (Time.time - jumpPressedTime <= jumpBuffer && Time.time - lastGroundedTime <= coyoteTime && m.VerticalVelocity <= 0f)
        {
            m.Jump(Mathf.Sqrt(2f * m.gravity * jumpHeight));
            jumpPressedTime = -10f; lastGroundedTime = -10f;
        }

        m.Move(dt);
        if (m.IsGrounded) lastGroundedTime = Time.time;
        if (transform.position.y < killHeight) Respawn();
    }

    void SetPitch(float value) { pitch = Mathf.Clamp(value, -85f, 85f); if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f); }

    /// <summary>The kicks of the rounds fired reach the view over a few frames; a moment after the last one, part of the climb
    /// the player has not pulled down comes back by itself.</summary>
    void UpdateRecoil(float dt)
    {
        if (recoilPending != Vector2.zero)
        {
            Vector2 step = recoilPending.sqrMagnitude < 1e-4f ? recoilPending : recoilPending * (1f - Mathf.Exp(-recoilSnap * dt));
            recoilPending -= step;
            transform.Rotate(0f, step.x, 0f);
            float before = pitch;
            SetPitch(pitch - step.y);
            recoilDebt += before - pitch;                                // only what the view really climbed (it stops at the top)
        }
        if (recoilDebt > 0f && Time.time > lastRecoilTime + recoilRecoveryDelay)
        {
            float back = recoilDebt < 0.01f ? recoilDebt : recoilDebt * (1f - Mathf.Exp(-recoilRecoverySpeed * dt));
            recoilDebt -= back;
            SetPitch(pitch + back * recoilRecovery);
        }
    }

    /// <summary>Puts the player at p (feet) standing still, landed on the ground just below.</summary>
    public void Teleport(Vector3 p) { Motor.Teleport(p); }

    public void Respawn() { Teleport(startPosition + Vector3.up * 0.2f); }
}

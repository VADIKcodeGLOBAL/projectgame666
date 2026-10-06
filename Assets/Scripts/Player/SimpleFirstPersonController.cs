using UnityEngine;

/// <summary>
/// First-person walker: WASD, mouse look, Shift to sprint (either Shift, as long as it is held — also through jumps),
/// Space to jump. Input, speeds and the jump rules live here; the movement itself (slopes, steps, walls, gravity) is CharacterMotor.
/// The same speed uphill, downhill, across a slope and on the flat; standing still on a slope stays still.
/// A jump still counts for a moment after leaving an edge (coyote time) and a press just before landing is kept (jump buffer).
/// Keeps its momentum in the air. Click the Game view to capture the mouse; Esc opens the settings menu.
/// </summary>
[RequireComponent(typeof(CharacterMotor))]
public class SimpleFirstPersonController : MonoBehaviour
{
    public Transform cameraPivot;
    public float walkSpeed = 5.5f;
    public float sprintSpeed = 8.5f;
    public float jumpHeight = 1.1f;
    [Tooltip("How fast the speed follows the input on the ground / in the air, m/s².")] public float groundAccel = 45f, airAccel = 10f;
    public float mouseSensitivity = 2.2f;
    [Tooltip("A jump still works this long after the ground was lost, s.")] public float coyoteTime = 0.12f;
    [Tooltip("A jump pressed this long before landing is done on landing, s.")] public float jumpBuffer = 0.12f;
    [Tooltip("Falling below this height puts the player back where they started.")] public float killHeight = -30f;

    [Tooltip("Multiplier of the mouse look, set by the weapons (lower while zoomed in).")] public float lookScale = 1f;

    public CharacterMotor Motor { get { if (motor == null) motor = GetComponent<CharacterMotor>(); return motor; } }
    public Vector3 PlanarVelocity { get { return Motor.PlanarVelocity; } }
    public float VerticalVelocity { get { return Motor.VerticalVelocity; } }
    public bool IsGrounded { get { return Motor.IsGrounded; } }
    public bool IsSprinting { get; private set; }
    /// <summary>Camera pitch, degrees (negative = up).</summary>
    public float Pitch { get { return pitch; } set { pitch = Mathf.Clamp(value, -85f, 85f); if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f); } }
    /// <summary>Weapon recoil: lifts the view.</summary>
    public void AddRecoil(float degreesUp) { Pitch = pitch - degreesUp; }

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
            float sens = mouseSensitivity * lookScale;
            transform.Rotate(0f, Input.GetAxis("Mouse X") * sens, 0f);
            Pitch = pitch - Input.GetAxis("Mouse Y") * sens;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;                                            // paused (settings menu)
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

    /// <summary>Puts the player at p (feet) standing still, landed on the ground just below.</summary>
    public void Teleport(Vector3 p) { Motor.Teleport(p); }

    public void Respawn() { Teleport(startPosition + Vector3.up * 0.2f); }
}

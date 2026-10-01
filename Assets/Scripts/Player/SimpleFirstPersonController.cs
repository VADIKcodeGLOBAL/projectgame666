using UnityEngine;

/// <summary>
/// First-person walker: WASD, mouse look, Shift to sprint (either Shift, as long as it is held — also through jumps),
/// Space to jump. The same speed uphill, downhill and on the flat: on the ground the move follows the slope and the walker
/// stays snapped to it, so running downhill neither turns into a fall nor blocks the jump. A jump still counts for a moment
/// after leaving an edge (coyote time) and a press just before landing is kept (jump buffer). Keeps its momentum in the air.
/// Click the Game view to capture the mouse; Esc opens the settings menu.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SimpleFirstPersonController : MonoBehaviour
{
    public Transform cameraPivot;
    public float walkSpeed = 5.5f;
    public float sprintSpeed = 8.5f;
    public float jumpHeight = 1.1f;
    public float gravity = 22f;
    [Tooltip("How fast the speed follows the input on the ground / in the air, m/s².")] public float groundAccel = 45f, airAccel = 10f;
    public float mouseSensitivity = 2.2f;
    [Tooltip("Ground below the feet within this distance pulls the walker down onto it (running down a slope, over a crest), m.")] public float snapDistance = 0.6f;
    [Tooltip("A jump still works this long after the ground was lost, s.")] public float coyoteTime = 0.12f;
    [Tooltip("A jump pressed this long before landing is done on landing, s.")] public float jumpBuffer = 0.12f;
    [Tooltip("Falling below this height puts the player back where they started.")] public float killHeight = -30f;

    [Tooltip("Multiplier of the mouse look, set by the weapons (lower while zoomed in).")] public float lookScale = 1f;

    public Vector3 PlanarVelocity { get; private set; }
    public float VerticalVelocity { get { return verticalVelocity; } }
    /// <summary>Camera pitch, degrees (negative = up).</summary>
    public float Pitch { get { return pitch; } set { pitch = Mathf.Clamp(value, -85f, 85f); if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f); } }
    /// <summary>Weapon recoil: lifts the view.</summary>
    public void AddRecoil(float degreesUp) { Pitch = pitch - degreesUp; }
    public bool IsSprinting { get; private set; }
    public bool IsGrounded { get { return grounded; } }

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

    CharacterController cc;
    float pitch, verticalVelocity, lastGroundedTime = -10f, jumpPressedTime = -10f, boostEnd, boostMultiplier = 1f;
    bool grounded;
    Vector3 startPosition, groundNormal = Vector3.up;
    bool testActive, testSprint, testJump; Vector3 testMove;
    static readonly RaycastHit[] hits = new RaycastHit[8];

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        startPosition = transform.position;
    }

    /// <summary>Drives the controller without a keyboard (automated tests). ClearTestInput gives control back.</summary>
    public void SetTestInput(Vector3 move, bool sprint, bool jump) { testActive = true; testMove = move; testSprint = sprint; testJump |= jump; }
    public void ClearTestInput() { testActive = false; testJump = false; }

    /// <summary>
    /// Walkable ground under the feet, ignoring our own capsule (its query shape trails a physics step behind the transform).
    /// </summary>
    bool FindGround(float maxDistance, out RaycastHit ground)
    {
        float r = cc.radius * 0.9f;
        Vector3 origin = transform.position + cc.center + Vector3.down * (cc.height * 0.5f - cc.radius);   // centre of the lower sphere
        int n = Physics.SphereCastNonAlloc(origin, r, Vector3.down, hits, cc.radius - r + cc.skinWidth + maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        ground = default(RaycastHit); float best = float.MaxValue; float minUp = Mathf.Cos(cc.slopeLimit * Mathf.Deg2Rad);
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.collider == cc || h.distance <= 0f || h.normal.y < minUp) continue;
            if (h.distance < best) { best = h.distance; ground = h; }
        }
        return best < float.MaxValue;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && !SettingsMenu.IsOpen && (WaveSurvivalGame.Instance == null || !WaveSurvivalGame.Instance.IsOver))
        { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float sens = mouseSensitivity * lookScale;
            transform.Rotate(0f, Input.GetAxis("Mouse X") * sens, 0f);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sens, -85f, 85f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;                                            // paused (settings menu)
        Vector3 input = testActive ? testMove : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        bool sprintKey = testActive ? testSprint : (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        bool jumpKey = testActive ? testJump : Input.GetKeyDown(KeyCode.Space);
        testJump = false;
        var game = WaveSurvivalGame.Instance;
        if (game != null && game.IsOver) { input = Vector3.zero; jumpKey = false; }
        if (input.sqrMagnitude > 1f) input.Normalize();
        if (jumpKey) jumpPressedTime = Time.time;

        IsSprinting = sprintKey && input.sqrMagnitude > 0.01f;          // Shift held = sprint, whatever happened before
        Vector3 heading = transform.TransformDirection(input);
        float speed = (IsSprinting ? sprintSpeed : walkSpeed) * SpeedMultiplier;

        if (grounded)
        {
            RaycastHit g;
            groundNormal = FindGround(0.3f, out g) ? g.normal : Vector3.up;
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, heading * speed, groundAccel * dt);
            verticalVelocity = 0f;
        }
        else
        {
            // in the air: keep the momentum and steer a little; with Shift held the target stays the sprint speed
            Vector3 target = heading.sqrMagnitude > 0.01f ? heading * speed : PlanarVelocity;
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, target, airAccel * dt);
            verticalVelocity -= gravity * dt;
        }

        bool jumped = false;
        if (Time.time - jumpPressedTime <= jumpBuffer && Time.time - lastGroundedTime <= coyoteTime && verticalVelocity <= 0f)
        {
            verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            jumpPressedTime = -10f; lastGroundedTime = -10f; jumped = true;
        }

        Vector3 move;
        if (grounded && !jumped)
        {
            // along the slope with the same horizontal speed up and down; a light press into the surface (along its normal,
            // so that it does not push down the slope) keeps the feet on bumps
            float rise = -Vector3.Dot(PlanarVelocity, groundNormal) / Mathf.Max(0.2f, groundNormal.y);
            move = PlanarVelocity + Vector3.up * rise - groundNormal;
        }
        else move = PlanarVelocity + Vector3.up * verticalVelocity;

        bool wasGrounded = grounded;
        var flags = cc.Move(move * dt);
        grounded = (flags & CollisionFlags.Below) != 0;
        if (!grounded && wasGrounded && !jumped)
        {
            // the ground dropped away faster than we walked (downhill, over a crest): step down onto it instead of falling
            RaycastHit g;
            if (FindGround(snapDistance, out g))
            {
                float down = g.distance - (cc.radius - cc.radius * 0.9f) - cc.skinWidth;
                if (down > 0f) cc.Move(Vector3.down * down);
                grounded = true; groundNormal = g.normal;
            }
        }
        if (grounded) { lastGroundedTime = Time.time; if (!jumped) verticalVelocity = 0f; }
        if (transform.position.y < killHeight) Respawn();
    }

    public void Respawn()
    {
        cc.enabled = false;
        transform.position = startPosition + Vector3.up * 0.2f;
        verticalVelocity = 0f; PlanarVelocity = Vector3.zero;
        cc.enabled = true;
    }
}

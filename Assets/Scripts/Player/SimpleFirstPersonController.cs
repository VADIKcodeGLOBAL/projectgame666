using UnityEngine;

/// <summary>
/// First-person walker: WASD, mouse look, Shift to sprint (either Shift, as long as it is held — also through jumps),
/// Space to jump. Keeps its horizontal momentum in the air; running uphill slows you down.
/// Click the Game view to capture the mouse, Esc to release it.
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
    [Tooltip("Speed loss per unit of uphill grade; the map generator balances routes with the same value.")] public float slopeSlowdown = 1.6f;
    [Tooltip("Falling below this height puts the player back where they started.")] public float killHeight = -30f;

    [Tooltip("Multiplier of the mouse look, set by the weapons (lower while zoomed in).")] public float lookScale = 1f;

    public Vector3 PlanarVelocity { get; private set; }
    /// <summary>Camera pitch, degrees (negative = up).</summary>
    public float Pitch { get { return pitch; } set { pitch = Mathf.Clamp(value, -85f, 85f); if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f); } }
    /// <summary>Weapon recoil: lifts the view.</summary>
    public void AddRecoil(float degreesUp) { Pitch = pitch - degreesUp; }
    public bool IsSprinting { get; private set; }
    public bool IsGrounded { get { return cc != null && cc.isGrounded; } }

    CharacterController cc;
    float pitch, verticalVelocity;
    Vector3 startPosition;
    bool testActive, testSprint, testJump; Vector3 testMove;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        startPosition = transform.position;
    }

    /// <summary>Drives the controller without a keyboard (automated tests). ClearTestInput gives control back.</summary>
    public void SetTestInput(Vector3 move, bool sprint, bool jump) { testActive = true; testMove = move; testSprint = sprint; testJump |= jump; }
    public void ClearTestInput() { testActive = false; testJump = false; }

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float sens = mouseSensitivity * lookScale;
            transform.Rotate(0f, Input.GetAxis("Mouse X") * sens, 0f);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sens, -85f, 85f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        Vector3 input = testActive ? testMove : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        bool sprintKey = testActive ? testSprint : (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        bool jumpKey = testActive ? testJump : Input.GetKeyDown(KeyCode.Space);
        testJump = false;
        var game = WaveSurvivalGame.Instance;
        if (game != null && game.IsOver) { input = Vector3.zero; jumpKey = false; }
        if (input.sqrMagnitude > 1f) input.Normalize();

        IsSprinting = sprintKey && input.sqrMagnitude > 0.01f;          // Shift held = sprint, whatever happened before
        Vector3 heading = transform.TransformDirection(input);
        float speed = IsSprinting ? sprintSpeed : walkSpeed;

        if (cc.isGrounded)
        {
            // ground normal straight under the feet — not the last collision, which can be a rock or a bot hit in the air
            RaycastHit hit; Vector3 n = Vector3.up;
            if (Physics.Raycast(transform.position + Vector3.up * 0.4f, Vector3.down, out hit, 1.2f, ~0, QueryTriggerInteraction.Ignore)) n = hit.normal;
            if (heading.sqrMagnitude > 0.01f)
            {
                float grade = -Vector3.Dot(new Vector3(n.x, 0f, n.z), heading.normalized) / Mathf.Max(0.2f, n.y);
                speed *= Mathf.Clamp(1f - slopeSlowdown * Mathf.Max(0f, grade), 0.35f, 1f);
            }
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, heading * speed, groundAccel * Time.deltaTime);
            verticalVelocity = -2f;
            if (jumpKey) verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
        }
        else
        {
            // in the air: keep the momentum and steer a little; with Shift held the target stays the sprint speed
            Vector3 target = heading.sqrMagnitude > 0.01f ? heading * speed : PlanarVelocity;
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, target, airAccel * Time.deltaTime);
            verticalVelocity -= gravity * Time.deltaTime;
        }

        cc.Move((PlanarVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
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

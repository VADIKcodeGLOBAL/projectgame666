using UnityEngine;

/// <summary>
/// Kinematic capsule motor (collide and slide), used instead of Unity's CharacterController so that every rule is ours:
///  - on walkable ground (up to maxSlope) the move follows the ground with the same horizontal speed uphill, downhill and across;
///    nothing pushes the character sideways, so standing still on a slope stays still;
///  - the character keeps a small gap (skin) to everything and is pulled down onto the ground below within snapDistance
///    (crests, steps down), so it never hops off a slope while running;
///  - steeper surfaces are walls: they stop the walk (no climbing them) and in the air you slide down them;
///  - ledges up to stepHeight are stepped onto; the camera (smoothedChild) is eased over the step;
///  - in the air: exact ballistic arc (jump height does not depend on the frame rate), ceilings stop the rise,
///    walls clip the velocity (sliding along them, stopping in corners); walkable ground lands the character with its
///    horizontal speed kept, also while still rising (a slope that rises faster than the arc);
///  - a jump while running uphill keeps the climb: as high over the slope as on the flat;
///  - overlaps (something moved into the capsule) are resolved first, along the shortest way out.
/// The owner sets PlanarVelocity (and calls Jump) and then Move(dt) once per frame.
/// Collides with everything in collisionMask except triggers and its own collider; the bots are on the Ignore Raycast layer
/// and are included by default. The capsule collider is the shape; a kinematic Rigidbody keeps moving it cheap for PhysX.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody))]
public class CharacterMotor : MonoBehaviour
{
    [Tooltip("Steepest walkable ground, degrees. Steeper surfaces are walls: they stop the walk and you slide down them.")]
    [Range(1f, 89f)] public float maxSlope = 50f;
    [Tooltip("Highest ledge (rock edge, box) walked onto without jumping, m.")] public float stepHeight = 0.4f;
    [Tooltip("Ground below within this distance pulls the character down onto it while walking (crests, steps down), m.")] public float snapDistance = 0.5f;
    public float gravity = 22f;
    [Tooltip("Gap kept between the capsule and everything it touches, m.")] public float skin = 0.01f;
    public LayerMask collisionMask = Physics.AllLayers;
    [Tooltip("Child eased over steps instead of jumping with them (the camera pivot).")] public Transform smoothedChild;
    [Tooltip("How fast the eased child catches up after a step, m/s.")] public float smoothSpeed = 2.5f;

    /// <summary>Horizontal velocity (y is always 0). Set it before Move; collisions reduce it.</summary>
    public Vector3 PlanarVelocity { get { return planar; } set { planar = new Vector3(value.x, 0f, value.z); } }
    public float VerticalVelocity { get { return vertical; } }
    public Vector3 Velocity { get { return planar + Vector3.up * vertical; } }
    public bool IsGrounded { get { return grounded; } }
    public Vector3 GroundNormal { get { return groundNormal; } }
    public Collider GroundCollider { get { return groundCollider; } }
    /// <summary>Height climbed by a step in the last Move (0 if none).</summary>
    public float LastStep { get; private set; }
    public float Radius { get { return Capsule.radius; } }
    public float Height { get { return Capsule.height; } }

    CapsuleCollider capsule;
    Rigidbody body;
    Vector3 planar, groundNormal = Vector3.up, smoothBase;
    float vertical, smoothOffset;
    bool grounded, jumpRequested, smoothReady;
    Collider groundCollider;

    const int MaxSlides = 5;
    static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    static readonly Collider[] overlapBuffer = new Collider[16];
    readonly Vector3[] planes = new Vector3[MaxSlides];

    CapsuleCollider Capsule { get { if (capsule == null) capsule = GetComponent<CapsuleCollider>(); return capsule; } }
    float MinWalkableY { get { return Mathf.Cos(maxSlope * Mathf.Deg2Rad); } }

    void Awake()
    {
        capsule = GetComponent<CapsuleCollider>();
        capsule.direction = 1;                                       // upright
        body = GetComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None;
        body.constraints = RigidbodyConstraints.FreezeAll;
        if (smoothedChild != null) { smoothBase = smoothedChild.localPosition; smoothReady = true; }
    }

    /// <summary>
    /// Starts a jump (vertical speed in m/s) on the next Move, also from the air (the caller decides when that is allowed).
    /// Running uphill, the climb is kept on top of it, so the jump is as high over the slope as on the flat (from rest it would
    /// barely clear a steep slope); downhill it starts from rest, a longer flight down the slope.
    /// </summary>
    public void Jump(float upSpeed)
    {
        if (grounded) upSpeed += Mathf.Max(0f, AlongSlope(planar, groundNormal).y);
        vertical = upSpeed; jumpRequested = true; grounded = false;
    }

    /// <summary>Puts the character at p (feet) with no velocity and lands it on the ground just below, if there is any.</summary>
    public void Teleport(Vector3 p)
    {
        planar = Vector3.zero; vertical = 0f; jumpRequested = false; grounded = false; groundCollider = null;
        p = Depenetrate(p);
        transform.position = p;
        if (body != null) body.position = p;
        RaycastHit g; Vector3 n;
        if (ProbeGround(p, snapDistance, out g, out n)) { p.y -= g.distance - skin; transform.position = p; Land(g, n); }
        smoothOffset = 0f; ApplySmoothing(0f);
    }

    // ------------------------------------------------------------------ queries (never against our own collider)
    void CapsulePoints(Vector3 pos, out Vector3 bottom, out Vector3 top, out float radius)
    {
        var c = Capsule;
        radius = c.radius;
        Vector3 centre = pos + transform.rotation * c.center;
        float half = Mathf.Max(0f, c.height * 0.5f - radius);
        bottom = centre + Vector3.down * half; top = centre + Vector3.up * half;
    }

    /// <summary>Nearest hit of the capsule swept from pos along dir (unit) within dist; ignores hits it starts inside of.</summary>
    bool Sweep(Vector3 pos, Vector3 dir, float dist, out RaycastHit hit)
    {
        Vector3 p0, p1; float r;
        CapsulePoints(pos, out p0, out p1, out r);
        int n = Physics.CapsuleCastNonAlloc(p0, p1, r, dir, hitBuffer, dist, collisionMask, QueryTriggerInteraction.Ignore);
        hit = default(RaycastHit); float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = hitBuffer[i];
            if (h.collider == capsule || h.collider.attachedRigidbody == body) continue;
            if (h.distance <= 0f && h.point == Vector3.zero) continue;  // started inside: Depenetrate deals with it
            if (h.distance < best) { best = h.distance; hit = h; }
        }
        return best < float.MaxValue;
    }

    /// <summary>
    /// Normal of the face under a ground contact. A capsule touching an edge (a step lip) reports a slanted contact normal;
    /// the face below the contact says whether it is something to stand on.
    /// </summary>
    Vector3 FaceNormal(RaycastHit h, Vector3 pos)
    {
        // a ray straight down just past the contact, on the side of the obstacle (away from our axis): the top of a step or
        // the face of a slope; started inside a wall it finds nothing and the contact normal stays
        Vector3 away = h.point - pos; away.y = 0f;
        Vector3 origin = h.point + Vector3.up * 0.05f + (away.sqrMagnitude > 1e-8f ? away.normalized * 0.002f : Vector3.zero);
        RaycastHit f;
        if (h.collider.Raycast(new Ray(origin, Vector3.down), out f, 0.1f)) return f.normal;
        return h.normal;
    }

    bool ProbeGround(Vector3 pos, float distance, out RaycastHit hit, out Vector3 normal)
    {
        normal = Vector3.up;
        if (!Sweep(pos, Vector3.down, distance + skin, out hit)) return false;
        normal = FaceNormal(hit, pos);                               // standing on an edge: the face under the feet decides
        return normal.y >= MinWalkableY;
    }

    /// <summary>Pushes the capsule out of whatever overlaps it (shortest way), a few colliders at most.</summary>
    Vector3 Depenetrate(Vector3 pos)
    {
        Vector3 p0, p1; float r;
        for (int pass = 0; pass < 3; pass++)
        {
            CapsulePoints(pos, out p0, out p1, out r);
            int n = Physics.OverlapCapsuleNonAlloc(p0, p1, r, overlapBuffer, collisionMask, QueryTriggerInteraction.Ignore);
            bool moved = false;
            for (int i = 0; i < n; i++)
            {
                var other = overlapBuffer[i];
                if (other == capsule || other.attachedRigidbody == body) continue;
                Vector3 dir; float d;
                if (Physics.ComputePenetration(capsule, pos, transform.rotation, other, other.transform.position, other.transform.rotation, out dir, out d))
                {
                    if (d > 1e-5f) { pos += dir * (d + skin * 0.5f); moved = true; }
                }
                else
                {
                    var terrain = other as TerrainCollider;                 // fallback: stand on the terrain surface
                    if (terrain != null)
                    {
                        var t = terrain.GetComponent<Terrain>();
                        float ground = t != null ? t.SampleHeight(pos) + t.transform.position.y : pos.y;
                        if (pos.y < ground + skin) { pos.y = ground + skin; moved = true; }
                    }
                }
            }
            if (!moved) break;
        }
        return pos;
    }

    // ------------------------------------------------------------------ the move
    /// <summary>The same horizontal motion, raised or lowered to run along a plane with normal n.</summary>
    static Vector3 AlongSlope(Vector3 horizontal, Vector3 n)
    {
        horizontal.y = 0f;
        float y = -(horizontal.x * n.x + horizontal.z * n.z) / Mathf.Max(0.05f, n.y);
        return new Vector3(horizontal.x, y, horizontal.z);
    }

    static Vector3 Clip(Vector3 v, Vector3 n)
    {
        float d = Vector3.Dot(v, n);
        return d < 0f ? v - n * d : v;
    }

    /// <summary>Wall normal for walking: steep faces block horizontally only, so they can neither be climbed nor push you up.</summary>
    static Vector3 Horizontal(Vector3 n)
    {
        n.y = 0f;
        return n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.zero;
    }

    /// <summary>Up by stepHeight, forward by the rest of the move, down onto a walkable top; false (nothing moved) if that fails.</summary>
    bool TryStep(ref Vector3 pos, Vector3 horizontal, float feetY)
    {
        float hd = new Vector2(horizontal.x, horizontal.z).magnitude;
        if (hd < 1e-4f) return false;
        Vector3 dir = new Vector3(horizontal.x, 0f, horizontal.z) / hd;
        RaycastHit h;
        float up = stepHeight;
        if (Sweep(pos, Vector3.up, stepHeight + skin, out h)) up = Mathf.Max(0f, h.distance - skin);
        if (up < 0.02f) return false;
        Vector3 p = pos + Vector3.up * up;
        float fwd = Mathf.Max(hd, skin * 4f);
        if (Sweep(p, dir, fwd + skin, out h)) fwd = Mathf.Max(0f, h.distance - skin);
        if (fwd < 0.002f) return false;
        p += dir * fwd;
        Vector3 n;
        if (!ProbeGround(p, up + 0.01f, out h, out n)) return false;
        p.y -= h.distance - skin;
        // the ledge itself (not the feet: they may rest on its rounded-off edge, a little higher) decides what counts as a step
        float ledge = h.point.y - feetY;
        if (p.y - feetY < 0.003f || ledge < 0.003f || ledge > stepHeight + 0.01f) return false;
        pos = p; groundNormal = n; groundCollider = h.collider;
        return true;
    }

    /// <summary>
    /// Moves by delta, sliding along what it hits. Ground mode: the motion follows walkable ground (horizontal part kept),
    /// steep faces are walls. Air mode: velocity and motion are clipped by every surface; touching walkable ground while
    /// falling or moving into it lands the character (ground mode for the rest of the move, the horizontal speed kept as it was).
    /// </summary>
    Vector3 SlideMove(Vector3 pos, Vector3 delta, ref bool groundMode, ref bool climbed)
    {
        int planeCount = 0;
        bool stepped = false;
        for (int iter = 0; iter < MaxSlides; iter++)
        {
            float dist = delta.magnitude;
            if (dist < 1e-5f) break;
            Vector3 dir = delta / dist;
            RaycastHit hit;
            if (!Sweep(pos, dir, dist + skin, out hit)) { pos += delta; break; }
            float travel = Mathf.Max(0f, hit.distance - skin);
            pos += dir * travel;
            delta = dir * (dist - travel);

            // the contact normal: a sloped face, or the rounded-off direction to an edge (low lips are rolled over like a ramp)
            Vector3 n = hit.normal;
            bool walkable = n.y >= MinWalkableY;

            // landed during the move: falling onto walkable ground, or running into ground that rises faster than we do (a slope
            // steeper than the arc) — clipping against it instead would eat the horizontal speed and keep us "airborne" on it
            if (!groundMode && walkable && (vertical <= 0f || Vector3.Dot(planar + Vector3.up * vertical, n) < 0f))
            {
                groundMode = true; vertical = 0f;
                groundNormal = n; groundCollider = hit.collider;
                delta = AlongSlope(delta, groundNormal);
                planeCount = 0;
                continue;
            }
            if (groundMode)
            {
                if (walkable)
                {
                    groundNormal = n; groundCollider = hit.collider;
                    delta = AlongSlope(delta, groundNormal);           // a change of slope: follow it
                    continue;
                }
                float ledge = hit.point.y - pos.y;
                float lowCentre = pos.y + Capsule.center.y - Mathf.Max(0f, Capsule.height * 0.5f - Capsule.radius);   // centre of the lower sphere
                if (stepHeight > 0f && ledge <= stepHeight + 0.01f && hit.point.y < lowCentre - 0.01f && n.y > 0.05f
                    && FaceNormal(hit, pos).y >= MinWalkableY)
                {
                    // the edge of something low with a flat top, below the round bottom of the capsule: roll over it
                    // (move along the contact tangent, horizontal speed kept), no pop; the camera is eased afterwards
                    delta = AlongSlope(delta, n);
                    climbed = true;
                    continue;
                }
                if (!stepped && stepHeight > 0f && ledge <= stepHeight + 0.01f)
                {
                    Vector3 stepPos = pos;                             // a ledge at the height of the capsule's side: step up onto it
                    if (TryStep(ref stepPos, delta, pos.y))
                    {
                        pos = stepPos; stepped = true; climbed = true;
                        break;                                         // the step used up the rest of the move
                    }
                }
                n = Horizontal(n);
                if (n == Vector3.zero) break;                          // a ceiling while walking: nothing to slide along
                Vector3 h = Clip(new Vector3(delta.x, 0f, delta.z), n);
                planar = Clip(planar, n);
                for (int k = 0; k < planeCount; k++)                   // two walls: slide along the corner line (vertical: stop)
                    if (Vector3.Dot(h, planes[k]) < -1e-6f) { h = Vector3.zero; planar = Vector3.zero; break; }
                delta = AlongSlope(h, groundNormal);
            }
            else
            {
                Vector3 v = planar + Vector3.up * vertical, vBefore = v, dBefore = delta;
                v = Clip(v, n); delta = Clip(delta, n);
                if (n.y > 0f && n.y < MinWalkableY)                    // a steep face: slide down it, never gain height on it
                {
                    v.y = Mathf.Min(v.y, Mathf.Max(vBefore.y, 0f));
                    delta.y = Mathf.Min(delta.y, Mathf.Max(dBefore.y, 0f));
                }
                for (int k = 0; k < planeCount; k++)
                    if (Vector3.Dot(delta, planes[k]) < -1e-6f)
                    {
                        Vector3 crease = Vector3.Cross(planes[k], n);
                        if (crease.sqrMagnitude < 1e-8f) { delta = Vector3.zero; v = Vector3.zero; break; }
                        crease.Normalize();
                        delta = crease * Vector3.Dot(delta, crease); v = crease * Vector3.Dot(v, crease);
                    }
                planar = new Vector3(v.x, 0f, v.z); vertical = v.y;
            }
            if (planeCount < planes.Length) planes[planeCount++] = n;
        }
        return pos;
    }

    void Land(RaycastHit g, Vector3 n)
    {
        grounded = true; vertical = 0f; groundNormal = n; groundCollider = g.collider;
    }

    /// <summary>One frame of movement with the current velocity.</summary>
    public void Move(float dt)
    {
        if (dt <= 0f) return;
        dt = Mathf.Min(dt, 0.1f);                                     // after a hitch: no huge single step
        LastStep = 0f;
        Vector3 pos = Depenetrate(transform.position);
        bool walking = grounded && !jumpRequested;
        jumpRequested = false;

        Vector3 delta;
        if (walking)
        {
            vertical = 0f;
            delta = AlongSlope(planar * dt, groundNormal);
        }
        else
        {
            delta = planar * dt + Vector3.up * (vertical * dt - 0.5f * gravity * dt * dt);   // exact under constant gravity
            vertical -= gravity * dt;
        }
        bool groundMode = walking, climbed = false;
        float expectedY = pos.y + delta.y;
        pos = SlideMove(pos, delta, ref groundMode, ref climbed);
        if (walking && climbed && pos.y > expectedY + 0.005f) LastStep = pos.y - expectedY;   // height gained on a step / an edge

        // ground: pulled down onto it while walking; when falling, only what is right under the feet
        RaycastHit g; Vector3 n;
        float probe = walking || groundMode ? snapDistance : (vertical <= 0f ? skin * 2f + 0.02f : 0f);
        if (probe > 0f && ProbeGround(pos, probe, out g, out n))
        {
            float gap = g.distance - skin;
            pos.y -= gap;
            Land(g, n);
        }
        else
        {
            grounded = false; groundCollider = null;
            if (walking) vertical = 0f;                                // walked off an edge: start falling from rest
        }

        if (walking && grounded)
        {
            // height changes that are not the ground under the feet being followed — a step up or down, an edge rolled over
            // (up or down, over a few frames), a drop the snap pulled us onto — go to the eased camera, both ways
            float excess = pos.y - expectedY;
            if (Mathf.Abs(excess) > 0.02f) smoothOffset -= excess;
        }
        transform.position = pos;
        ApplySmoothing(dt);
    }

    void ApplySmoothing(float dt)
    {
        if (smoothedChild == null) return;
        if (!smoothReady) { smoothBase = smoothedChild.localPosition; smoothReady = true; }
        smoothOffset = Mathf.Clamp(smoothOffset, -0.6f, 0.6f);
        smoothOffset = Mathf.MoveTowards(smoothOffset, 0f, Mathf.Max(smoothSpeed, Mathf.Abs(smoothOffset) * 10f) * dt);
        smoothedChild.localPosition = smoothBase + Vector3.up * smoothOffset;
    }

    void OnDrawGizmosSelected()
    {
        if (!grounded) return;
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, groundNormal);
    }
}

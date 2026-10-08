using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A big, heavy hand-and-a-half sword in first person (1.6 m).
///  - LMB: a slash; held (or pressed again late in a slash) it chains into the next one the other way round,
///    right-to-left, left-to-right. It cuts every bot inside its arc and reach.
///  - RMB: the two-handed chop from above: a long heave up, a narrow arc, more reach, much more damage and a harder knock.
/// The weight: the blade gathers speed through the strike and carries on past its end before it is brought back, it slows for a
/// moment where it bites, a landed blow jolts the view; the player walks slower with it in hand and slower still while swinging,
/// it takes longer to draw, it lags further behind a turning view and bobs more.
/// A blow is registered while the blade sweeps (no physics on the model): a bot counts once per swing when the swept part of the arc
/// passes its direction, its body box is within reach and nothing solid stands between it and the eye. Hit bots are thrown back and
/// staggered (EnemyBot.Knockback). The view model is posed in camera space along the swing, both placeholder hands on the grip
/// (ViewModelArms); the swish and the hit are synthesised.
/// </summary>
public class MeleeWeapon : HandWeapon
{
    public enum Swing { None, SlashRightToLeft, SlashLeftToRight, Chop }

    [Header("Slash (LMB)")]
    public float slashDamage = 65f;
    public float slashReach = 3.0f;
    [Tooltip("Width of the slash in front of you, degrees.")] public float slashArc = 140f;
    public float slashWindup = 0.22f, slashStrike = 0.18f, slashRecover = 0.42f;
    public float slashKnockback = 5f, slashStagger = 0.5f;

    [Header("Chop (RMB, two hands)")]
    public float chopDamage = 190f;
    public float chopReach = 3.4f;
    public float chopArc = 60f;
    public float chopWindup = 0.55f, chopStrike = 0.17f, chopRecover = 0.65f;
    public float chopKnockback = 9f, chopStagger = 1.1f;

    [Tooltip("The next blow can start this long after the strike of the current one has ended, seconds (a chain of slashes).")]
    public float chainDelay = 0.04f;

    [Header("Weight")]
    [Tooltip("Walking / running speed with the sword in hand, part of the normal speed.")] [Range(0.3f, 1f)] public float carrySpeed = 0.88f;
    [Tooltip("Speed while a slash / the chop is swung, part of the normal speed.")] [Range(0.2f, 1f)] public float slashMoveSpeed = 0.72f, chopMoveSpeed = 0.5f;
    [Tooltip("Putting the sword away and drawing it takes this many times the normal switch time.")] [Min(0.2f)] public float drawTime = 1.6f;
    [Tooltip("The blade carries on past the end of the strike by this part of the swing before it is brought back (the chop half of it).")]
    [Range(0f, 0.3f)] public float followThrough = 0.12f;
    [Tooltip("The swing slows to a quarter this long where the blade bites into a bot, seconds (slash / chop).")] public float slashHitStop = 0.05f, chopHitStop = 0.09f;
    [Tooltip("The view is jolted down this far when a blow lands, degrees (slash / chop); it springs back.")] public float slashPunch = 0.9f, chopPunch = 2.4f;
    [Tooltip("The sword lags behind a turning view and bobs on the walk this many times as much as a gun.")] [Min(0f)] public float swayWeight = 1.6f, bobWeight = 1.7f;
    [Tooltip("How fast the sword catches up with the sway, 1/s; lower is heavier.")] [Min(0.5f)] public float swayFollow = 7f;

    [Header("View model")]
    [Tooltip("At the tip of the blade; drawn only while the blade strikes.")] public TrailRenderer trail;

    public Swing State { get; private set; }
    public int SwingsStarted { get; private set; }
    public int BotsHit { get; private set; }
    /// <summary>0..1 how far the current strike has swept (0 outside the strike).</summary>
    public float StrikeProgress { get; private set; }
    public override float MoveSpeedScale { get { return moveScale; } }
    public override float DrawTimeScale { get { return drawTime; } }
    public override bool CanAttack { get { return State == Swing.None; } }

    static AudioClip swish, impact;
    static readonly List<EnemyBot> hitThisSwing = new List<EnemyBot>(16);
    static readonly RaycastHit[] losHits = new RaycastHit[16];

    Swing queued = Swing.None;
    float t, hitStopEnd = -1f, moveScale = 1f;
    bool nextFromRight = true, hitSounded;
    Vector3 fromPos, heavyTilt, heavyShift; Quaternion fromRot;
    ViewModelArms arms;

    // ------------------------------------------------------------------ poses (camera space: x right, y up, z forward)
    static readonly Vector3 RestHand = new Vector3(0.25f, -0.33f, 0.46f);

    /// <summary>The sword's rotation for a blade direction (model +Y) and the side its flat faces (model +Z).</summary>
    static Quaternion Orient(Vector3 blade, Vector3 flat)
    {
        blade.Normalize();
        flat = Vector3.ProjectOnPlane(flat, blade);
        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.ProjectOnPlane(Vector3.forward, blade);
        return Quaternion.LookRotation(flat.normalized, blade);
    }

    // held up at the right shoulder, leaning out to the right: the big blade runs up the side of the view and leaves the middle free;
    // the flat of the blade towards you
    static Quaternion RestRot { get { return Orient(new Vector3(0.3f, 0.9f, 0.12f), new Vector3(-0.5f, 0.05f, -1f)); } }

    /// <summary>The blade's direction across a slash, degrees right of the view (+ right). fromRight: right to left; s past 1 is the follow-through.</summary>
    float SlashYaw(float s, bool fromRight) { float half = slashArc * 0.5f; return fromRight ? Mathf.LerpUnclamped(half, -half, s) : Mathf.LerpUnclamped(-half, half, s); }

    void SlashPose(float s, bool fromRight, out Vector3 pos, out Quaternion rot)
    {
        float phi = SlashYaw(s, fromRight) * Mathf.Deg2Rad;
        float y = fromRight ? Mathf.LerpUnclamped(0.55f, -0.45f, s) : Mathf.LerpUnclamped(-0.40f, 0.50f, s);   // a diagonal cut: down from the right, up from the left
        var blade = new Vector3(Mathf.Sin(phi), y, Mathf.Cos(phi));
        pos = new Vector3(0.06f + 0.26f * Mathf.Sin(phi), -0.27f + 0.06f * y, 0.30f + 0.10f * Mathf.Cos(phi));
        rot = Orient(blade, Vector3.up);                                // the edge leads, the flat faces up
    }

    void ChopPose(float s, out Vector3 pos, out Quaternion rot)        // 0: raised over the head, 1: struck down in front
    {
        float a = Mathf.LerpUnclamped(-35f, 125f, s) * Mathf.Deg2Rad;   // from up-and-back, over the top, to down-and-forward
        var blade = new Vector3(0.04f, Mathf.Cos(a), Mathf.Sin(a));
        // raised: the hands well in front of the face (with the arms on them, closer would fill the view)
        pos = new Vector3(Mathf.LerpUnclamped(0.12f, 0.06f, s), Mathf.LerpUnclamped(0.10f, -0.34f, s), Mathf.LerpUnclamped(0.34f, 0.50f, s));
        rot = Orient(blade, Vector3.right);
    }

    static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }
    /// <summary>A heavy blade through the strike: starts slowly, gathers speed and is still moving at the end (0..1 -> 0..1).</summary>
    static float StrikeCurve(float x) { x = Mathf.Clamp01(x); return x * x * (2.2f - 1.2f * x); }

    void Timing(Swing s, out float windup, out float strike, out float recover)
    {
        if (s == Swing.Chop) { windup = chopWindup; strike = chopStrike; recover = chopRecover; }
        else { windup = slashWindup; strike = slashStrike; recover = slashRecover; }
        windup = Mathf.Max(0.01f, windup); strike = Mathf.Max(0.01f, strike); recover = Mathf.Max(0.01f, recover);
    }

    void StrikePose(Swing s, float k, out Vector3 pos, out Quaternion rot)
    {
        if (s == Swing.Chop) ChopPose(k, out pos, out rot);
        else SlashPose(k, s == Swing.SlashRightToLeft, out pos, out rot);
    }

    // ------------------------------------------------------------------ the weapon
    public override void Init(WeaponInventory owner)
    {
        Owner = owner;
        hasScope = false;                                              // RMB is the chop
        arms = GetComponent<ViewModelArms>();
        if (trail != null) { trail.emitting = false; trail.Clear(); }
        if (swish == null) swish = MakeSwish();
        if (impact == null) impact = MakeImpact();
        State = Swing.None; moveScale = carrySpeed;
        PutAtRest();
    }

    /// <summary>The resting pose in the hand (also used by the editor, so the sword sits right in the scene before Play).</summary>
    public void PutAtRest() { transform.localPosition = RestHand; transform.localRotation = RestRot; }

    public override void CancelReload()                                // put away or switched: the swing is dropped
    {
        State = Swing.None; queued = Swing.None; StrikeProgress = 0f; moveScale = carrySpeed;
        if (trail != null) trail.emitting = false;
    }

    void Begin(Swing s)
    {
        State = s; t = 0f; queued = Swing.None; hitSounded = false; hitStopEnd = -1f;
        fromPos = transform.localPosition - extraOffsets; fromRot = transform.localRotation;
        hitThisSwing.Clear();
        SwingsStarted++;
        if (s != Swing.Chop) nextFromRight = s != Swing.SlashRightToLeft;
        else if (Owner != null) Owner.PunchView(-0.7f);                 // heaving it up lifts the view a little
    }

    Swing NextSlash() { return nextFromRight ? Swing.SlashRightToLeft : Swing.SlashLeftToRight; }

    public override void Tick(bool held, bool pressed, bool altHeld, bool altPressed, bool ready, Camera cam, float aim)
    {
        float dt = Time.deltaTime;
        if (State == Swing.None)
        {
            if (ready && altPressed) Begin(Swing.Chop);
            else if (ready && (pressed || held)) Begin(NextSlash());
        }
        if (State == Swing.None) { moveScale = Mathf.Lerp(moveScale, carrySpeed, 1f - Mathf.Exp(-8f * dt)); return; }

        float windup, strike, recover;
        Timing(State, out windup, out strike, out recover);
        if (ready && altPressed && t > windup) queued = Swing.Chop;     // asked for during this swing: follows it
        else if (ready && (pressed || held) && t > windup && queued == Swing.None) queued = NextSlash();

        // slower on foot from the wind-up to halfway through the recovery
        float slow = t < windup + strike + recover * 0.5f ? (State == Swing.Chop ? chopMoveSpeed : slashMoveSpeed) : carrySpeed;
        moveScale = Mathf.Lerp(moveScale, Mathf.Min(slow, carrySpeed), 1f - Mathf.Exp(-8f * dt));

        float prevT = t;
        t += dt * (Time.time < hitStopEnd ? 0.25f : 1f);               // where the blade bites it slows for a moment
        if (prevT < windup && t >= windup)                             // the blade starts moving: the swish
        {
            if (Owner != null) Owner.PlaySound(swish, State == Swing.Chop ? 0.8f : 0.65f, State == Swing.Chop ? Random.Range(0.6f, 0.66f) : Random.Range(0.78f, 0.88f));
        }
        float k = StrikeCurve((t - windup) / strike);
        StrikeProgress = t >= windup && t < windup + strike ? k : 0f;
        if (t >= windup && prevT < windup + strike) Sweep(cam, k);
        if (trail != null) trail.emitting = t >= windup && t < windup + strike;
        if (State == Swing.Chop && prevT < windup + strike && t >= windup + strike && !hitSounded && Owner != null)
            Owner.PunchView(chopPunch * 0.35f);                         // a miss: the weight still pulls the view down

        if (queued != Swing.None && t >= windup + strike + chainDelay && ready) { Begin(queued); return; }
        if (t >= windup + strike + recover) { State = Swing.None; StrikeProgress = 0f; }
    }

    /// <summary>The part of the swing swept so far (0..s) cuts every bot it passes once.</summary>
    void Sweep(Camera cam, float s)
    {
        if (cam == null) return;
        var eye = cam.transform;
        bool chop = State == Swing.Chop;
        float reach = chop ? chopReach : slashReach;
        float yawA = chop ? -chopArc * 0.5f : Mathf.Min(SlashYaw(0f, State == Swing.SlashRightToLeft), SlashYaw(s, State == Swing.SlashRightToLeft));
        float yawB = chop ? chopArc * 0.5f : Mathf.Max(SlashYaw(0f, State == Swing.SlashRightToLeft), SlashYaw(s, State == Swing.SlashRightToLeft));
        if (chop && s < 0.25f) return;                                 // the chop only bites once it comes over the top
        int hits = 0;
        for (int i = 0; i < EnemyBot.All.Count; i++)
        {
            var bot = EnemyBot.All[i];
            if (bot.IsDying || bot.body == null || hitThisSwing.Contains(bot)) continue;
            Vector3 near = bot.body.bounds.ClosestPoint(eye.position), centre = bot.body.bounds.center;
            float dist = Vector3.Distance(eye.position, near);
            if (dist > reach) continue;
            Vector3 local = eye.InverseTransformDirection(centre - eye.position);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            bool close = dist < 0.6f && Mathf.Abs(yaw) < 100f;           // pressed against you: any swing catches it
            if (!close && (yaw < yawA || yaw > yawB || pitch < -70f || pitch > (chop ? 50f : 45f))) continue;
            if (Blocked(eye.position, centre)) continue;
            hitThisSwing.Add(bot);
            Vector3 push = centre - eye.position; push.y = 0f;
            bot.TakeDamage(chop ? chopDamage : slashDamage);
            bot.Knockback(push.normalized * (chop ? chopKnockback : slashKnockback), chop ? chopStagger : slashStagger);
            hits++;
        }
        if (hits > 0)
        {
            BotsHit += hits;
            if (!hitSounded)                                           // the first bite of this swing: it slows, the view jolts
            {
                hitSounded = true;
                hitStopEnd = Time.time + (chop ? chopHitStop : slashHitStop);
                if (Owner != null) Owner.PlaySound(impact, chop ? 1f : 0.8f, chop ? Random.Range(0.72f, 0.8f) : Random.Range(0.88f, 0.98f));
            }
            if (Owner != null) Owner.OnMeleeHit(this, hits, chop ? chopPunch : slashPunch);
        }
    }

    /// <summary>Something solid (terrain, a rock, the cannon) between the eye and the bot: the blade cannot reach it.</summary>
    bool Blocked(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; float len = d.magnitude;
        if (len < 0.05f) return false;
        int n = Physics.RaycastNonAlloc(from, d / len, losHits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (Owner != null && losHits[i].collider.transform.IsChildOf(Owner.transform)) continue;   // our own body
            if (losHits[i].distance < len - 0.3f) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ the view model
    Vector3 extraOffsets;

    public override void UpdatePose(float raise, float aim, Vector3 bob, Vector3 swayTilt, Vector3 swayShift)
    {
        Vector3 pos; Quaternion rot;
        if (State == Swing.None)                                       // at rest: the weight breathes a little
        {
            float now = Time.time;
            pos = RestHand + new Vector3(0f, Mathf.Sin(now * 1.7f) * 0.004f, 0f);
            rot = RestRot * Quaternion.Euler(0f, 0f, Mathf.Sin(now * 1.1f) * 0.6f);
        }
        else
        {
            float windup, strike, recover;
            Timing(State, out windup, out strike, out recover);
            Vector3 p0, p1; Quaternion r0, r1;
            float over = followThrough * (State == Swing.Chop ? 0.5f : 1f);
            if (t < windup)                                            // from wherever the sword was to the start of the strike
            {
                StrikePose(State, 0f, out p1, out r1);
                float k = Ease(t / windup);
                pos = Vector3.Lerp(fromPos, p1, k); rot = Quaternion.Slerp(fromRot, r1, k);
            }
            else if (t < windup + strike) StrikePose(State, StrikeCurve((t - windup) / strike), out pos, out rot);
            else
            {
                const float carryOn = 0.3f;                            // the first part of the recovery: the blade carries on past the end
                float r = (t - windup - strike) / recover;
                if (r < carryOn) StrikePose(State, 1f + over * EaseOut(r / carryOn), out pos, out rot);
                else                                                   // then it is brought back to rest
                {
                    StrikePose(State, 1f + over, out p0, out r0);
                    float k = Ease((r - carryOn) / (1f - carryOn));
                    pos = Vector3.Lerp(p0, RestHand, k); rot = Quaternion.Slerp(r0, RestRot, k);
                }
            }
        }

        // heavy: the sword follows the sway of a light gun late and further, and bobs more on the walk
        float follow = 1f - Mathf.Exp(-Mathf.Max(0.5f, swayFollow) * Time.deltaTime);
        heavyTilt = Vector3.Lerp(heavyTilt, swayTilt * swayWeight, follow);
        heavyShift = Vector3.Lerp(heavyShift, swayShift * swayWeight, follow);

        float low = 1f - raise; low *= low;                            // switching: down out of sight
        extraOffsets = bob * bobWeight + heavyShift + new Vector3(0f, -0.6f, -0.1f) * low;
        transform.localPosition = pos + extraOffsets;
        Quaternion swayRot = Quaternion.Euler(heavyTilt);
        SwayTiltNow = Quaternion.Angle(Quaternion.identity, swayRot);
        transform.localRotation = swayRot * Quaternion.Euler(55f * low, 0f, 0f) * rot;
        if (arms != null) arms.Solve();                                 // the hands stay on the grip through every swing
    }

    // ------------------------------------------------------------------ sounds (made once, no files)
    static AudioClip MakeSwish()
    {
        const int rate = 44100; int n = (int)(rate * 0.42f);
        var d = new float[n]; var rnd = new System.Random(7);
        float lp = 0f, lp2 = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float x = i / (float)n, noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
            float cut = Mathf.Lerp(0.012f, 0.2f, Mathf.Sin(x * Mathf.PI));   // band-passed noise rising and falling: air pushed by a broad blade
            lp += cut * (noise - lp); lp2 += cut * (lp - lp2);
            float env = Mathf.Sin(x * Mathf.PI); env *= env;
            d[i] = (lp - lp2 * 0.5f) * env; peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        }
        for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
        var c = AudioClip.Create("SwordSwish", n, 1, rate, false); c.SetData(d, 0);
        return c;
    }

    static AudioClip MakeImpact()
    {
        const int rate = 44100; const float len = 0.3f; int n = (int)(rate * len);
        var d = new float[n]; var rnd = new System.Random(11);
        float phase = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float s = i / (float)rate, f = Mathf.Lerp(130f, 42f, s / len);   // a heavy, dull thump with a short crack on top
            phase += 2f * Mathf.PI * f / rate;
            float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
            d[i] = Mathf.Sin(phase) * Mathf.Exp(-s * 11f) + noise * Mathf.Exp(-s * 50f) * 0.5f;
            peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        }
        for (int i = 0; i < n; i++) d[i] *= 0.9f / peak;
        var c = AudioClip.Create("SwordHit", n, 1, rate, false); c.SetData(d, 0);
        return c;
    }
}

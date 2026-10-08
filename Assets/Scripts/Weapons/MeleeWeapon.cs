using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A hand-and-a-half sword in first person.
///  - LMB: a quick one-handed slash; held (or pressed again late in a slash) it chains into the next one the other way round,
///    right-to-left, left-to-right. It cuts every bot inside its arc and reach.
///  - RMB: the two-handed chop from above: a long wind-up, a narrow arc, more reach, much more damage and a harder knock.
/// A blow is registered while the blade sweeps (no physics on the model): a bot counts once per swing when the swept part of the arc
/// passes its direction, its body box is within reach and nothing solid stands between it and the eye. Hit bots are thrown back and
/// staggered (EnemyBot.Knockback). The view model is posed in camera space along the swing; the swish and the hit are synthesised.
/// </summary>
public class MeleeWeapon : HandWeapon
{
    public enum Swing { None, SlashRightToLeft, SlashLeftToRight, Chop }

    [Header("Slash (LMB, one hand)")]
    public float slashDamage = 45f;
    public float slashReach = 2.6f;
    [Tooltip("Width of the slash in front of you, degrees.")] public float slashArc = 130f;
    public float slashWindup = 0.10f, slashStrike = 0.13f, slashRecover = 0.25f;
    public float slashKnockback = 3.5f, slashStagger = 0.35f;

    [Header("Chop (RMB, two hands)")]
    public float chopDamage = 130f;
    public float chopReach = 3.0f;
    public float chopArc = 50f;
    public float chopWindup = 0.36f, chopStrike = 0.15f, chopRecover = 0.42f;
    public float chopKnockback = 7f, chopStagger = 0.8f;

    [Tooltip("The next blow can start this long after the strike of the current one has ended, seconds (a chain of slashes).")]
    public float chainDelay = 0.04f;

    [Header("View model")]
    [Tooltip("At the tip of the blade; drawn only while the blade strikes.")] public TrailRenderer trail;

    public Swing State { get; private set; }
    public int SwingsStarted { get; private set; }
    public int BotsHit { get; private set; }
    /// <summary>0..1 how far the current strike has swept (0 outside the strike).</summary>
    public float StrikeProgress { get; private set; }

    static AudioClip swish, impact;
    static readonly List<EnemyBot> hitThisSwing = new List<EnemyBot>(16);
    static readonly RaycastHit[] losHits = new RaycastHit[16];

    Swing queued = Swing.None;
    float t, lowered;
    bool nextFromRight = true, hitSounded;
    Vector3 fromPos; Quaternion fromRot;
    Renderer[] renderers;

    // ------------------------------------------------------------------ poses (camera space: x right, y up, z forward)
    static readonly Vector3 RestHand = new Vector3(0.27f, -0.34f, 0.46f);

    /// <summary>The sword's rotation for a blade direction (model +Y) and the side its flat faces (model +Z).</summary>
    static Quaternion Orient(Vector3 blade, Vector3 flat)
    {
        blade.Normalize();
        flat = Vector3.ProjectOnPlane(flat, blade);
        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.ProjectOnPlane(Vector3.forward, blade);
        return Quaternion.LookRotation(flat.normalized, blade);
    }

    static Quaternion RestRot { get { return Orient(new Vector3(-0.28f, 0.78f, 0.56f), new Vector3(-0.15f, 0.1f, -1f)); } }   // the flat of the blade towards you

    /// <summary>The blade's direction across a slash, degrees right of the view (+ right). fromRight: right to left.</summary>
    float SlashYaw(float s, bool fromRight) { float half = slashArc * 0.5f; return fromRight ? Mathf.Lerp(half, -half, s) : Mathf.Lerp(-half, half, s); }

    void SlashPose(float s, bool fromRight, out Vector3 pos, out Quaternion rot)
    {
        float phi = SlashYaw(s, fromRight) * Mathf.Deg2Rad;
        float y = fromRight ? Mathf.Lerp(0.55f, -0.45f, s) : Mathf.Lerp(-0.40f, 0.50f, s);   // a diagonal cut: down from the right, up from the left
        var blade = new Vector3(Mathf.Sin(phi), y, Mathf.Cos(phi));
        pos = new Vector3(0.06f + 0.26f * Mathf.Sin(phi), -0.27f + 0.06f * y, 0.30f + 0.10f * Mathf.Cos(phi));
        rot = Orient(blade, Vector3.up);                                // the edge leads, the flat faces up
    }

    void ChopPose(float s, out Vector3 pos, out Quaternion rot)        // 0: raised over the head, 1: struck down in front
    {
        float a = Mathf.Lerp(-35f, 125f, s) * Mathf.Deg2Rad;            // from up-and-back, over the top, to down-and-forward
        var blade = new Vector3(0.04f, Mathf.Cos(a), Mathf.Sin(a));
        pos = new Vector3(Mathf.Lerp(0.14f, 0.06f, s), Mathf.Lerp(0.06f, -0.34f, s), Mathf.Lerp(0.20f, 0.50f, s));
        rot = Orient(blade, Vector3.right);
    }

    static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

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
        renderers = GetComponentsInChildren<Renderer>(true);
        if (trail != null) { trail.emitting = false; trail.Clear(); }
        if (swish == null) swish = MakeSwish();
        if (impact == null) impact = MakeImpact();
        State = Swing.None;
        PutAtRest();
    }

    /// <summary>The resting pose in the hand (also used by the editor, so the sword sits right in the scene before Play).</summary>
    public void PutAtRest() { transform.localPosition = RestHand; transform.localRotation = RestRot; }

    public override void CancelReload()                                // put away or switched: the swing is dropped
    {
        State = Swing.None; queued = Swing.None; StrikeProgress = 0f;
        if (trail != null) trail.emitting = false;
    }

    void Begin(Swing s)
    {
        State = s; t = 0f; queued = Swing.None; hitSounded = false;
        fromPos = transform.localPosition - Offsets; fromRot = transform.localRotation;
        hitThisSwing.Clear();
        SwingsStarted++;
        if (s != Swing.Chop) nextFromRight = s != Swing.SlashRightToLeft;
    }

    Swing NextSlash() { return nextFromRight ? Swing.SlashRightToLeft : Swing.SlashLeftToRight; }

    public override void Tick(bool held, bool pressed, bool altHeld, bool altPressed, bool ready, Camera cam, float aim)
    {
        float dt = Time.deltaTime;
        if (State == Swing.None)
        {
            if (ready && altPressed) Begin(Swing.Chop);
            else if (ready && (pressed || held)) Begin(NextSlash());
            return;
        }

        float windup, strike, recover;
        Timing(State, out windup, out strike, out recover);
        if (ready && altPressed && t > windup) queued = Swing.Chop;     // asked for during this swing: follows it
        else if (ready && (pressed || held) && t > windup && queued == Swing.None) queued = NextSlash();

        float prevT = t;
        t += dt;
        if (prevT < windup && t >= windup)                             // the blade starts moving: the swish
        {
            if (Owner != null) Owner.PlaySound(swish, State == Swing.Chop ? 0.75f : 0.6f, State == Swing.Chop ? Random.Range(0.72f, 0.8f) : Random.Range(0.95f, 1.12f));
        }
        StrikeProgress = t >= windup && t < windup + strike ? (t - windup) / strike : 0f;
        if (t >= windup && prevT < windup + strike) Sweep(cam, Mathf.Clamp01((t - windup) / strike));
        if (trail != null) trail.emitting = t >= windup && t < windup + strike;

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
            if (Owner != null)
            {
                Owner.OnMeleeHit(this, hits);
                if (!hitSounded) { hitSounded = true; Owner.PlaySound(impact, chop ? 0.9f : 0.7f, chop ? Random.Range(0.8f, 0.9f) : Random.Range(1f, 1.15f)); }
            }
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
    Vector3 Offsets { get { return extraOffsets; } }

    public override void UpdatePose(float raise, float aim, Vector3 bob, Vector3 swayTilt, Vector3 swayShift)
    {
        Vector3 pos; Quaternion rot;
        if (State == Swing.None) { pos = RestHand; rot = RestRot; }
        else
        {
            float windup, strike, recover;
            Timing(State, out windup, out strike, out recover);
            Vector3 p0, p1; Quaternion r0, r1;
            if (t < windup)                                            // from wherever the sword was to the start of the strike
            {
                StrikePose(State, 0f, out p1, out r1);
                float k = Ease(t / windup);
                pos = Vector3.Lerp(fromPos, p1, k); rot = Quaternion.Slerp(fromRot, r1, k);
            }
            else if (t < windup + strike) StrikePose(State, Ease((t - windup) / strike), out pos, out rot);
            else                                                       // back to rest
            {
                StrikePose(State, 1f, out p0, out r0);
                float k = Ease((t - windup - strike) / recover);
                pos = Vector3.Lerp(p0, RestHand, k); rot = Quaternion.Slerp(r0, RestRot, k);
            }
        }

        float low = 1f - raise; low *= low;                            // switching: down out of sight
        extraOffsets = bob + swayShift + new Vector3(0f, -0.35f, -0.05f) * low;
        transform.localPosition = pos + extraOffsets;
        Quaternion swayRot = Quaternion.Euler(swayTilt);
        SwayTiltNow = Quaternion.Angle(Quaternion.identity, swayRot);
        transform.localRotation = swayRot * Quaternion.Euler(30f * low, 0f, 0f) * rot;
    }

    // ------------------------------------------------------------------ sounds (made once, no files)
    static AudioClip MakeSwish()
    {
        const int rate = 44100; int n = (int)(rate * 0.32f);
        var d = new float[n]; var rnd = new System.Random(7);
        float lp = 0f, lp2 = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float x = i / (float)n, noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
            float cut = Mathf.Lerp(0.02f, 0.28f, Mathf.Sin(x * Mathf.PI));   // band-passed noise rising and falling: air cut by a blade
            lp += cut * (noise - lp); lp2 += cut * (lp - lp2);
            float env = Mathf.Sin(x * Mathf.PI); env *= env;
            d[i] = (lp - lp2 * 0.6f) * env; peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        }
        for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
        var c = AudioClip.Create("SwordSwish", n, 1, rate, false); c.SetData(d, 0);
        return c;
    }

    static AudioClip MakeImpact()
    {
        const int rate = 44100; int n = (int)(rate * 0.22f);
        var d = new float[n]; var rnd = new System.Random(11);
        float phase = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float s = i / (float)rate, f = Mathf.Lerp(160f, 55f, s / 0.22f);   // a dull thump with a short crack on top
            phase += 2f * Mathf.PI * f / rate;
            float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
            d[i] = Mathf.Sin(phase) * Mathf.Exp(-s * 16f) + noise * Mathf.Exp(-s * 55f) * 0.55f;
            peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        }
        for (int i = 0; i < n; i++) d[i] *= 0.85f / peak;
        var c = AudioClip.Create("SwordHit", n, 1, rate, false); c.SetData(d, 0);
        return c;
    }
}

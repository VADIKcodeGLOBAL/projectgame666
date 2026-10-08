using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Box bot: walks over the terrain straight at the player, keeps a little distance from its neighbours,
/// sidesteps when it gets stuck on a rock or a tree, and hits the player at arm's length.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class EnemyBot : MonoBehaviour
{
    public float maxHealth = 40f;
    public float speed = 6.5f;
    public float damage = 8f;
    public float attackInterval = 1.0f;
    public float attackRange = 1.9f;
    public Renderer body;

    public float Health { get; private set; }
    public bool IsDying { get; private set; }

    public static readonly List<EnemyBot> All = new List<EnemyBot>();
    static readonly Unity.Profiling.ProfilerMarker SepMarker = new Unity.Profiling.ProfilerMarker("EnemyBot.Separation"),
                                                   MoveMarker = new Unity.Profiling.ProfilerMarker("EnemyBot.Move");

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), FlashId = Shader.PropertyToID("_Flash");
    CharacterController cc;
    MaterialPropertyBlock block;
    Transform target; PlayerHealth targetHealth;
    Color glow = new Color(1f, 0.35f, 0.1f);
    float vy, nextAttack, flash, stuckCheck, sidestepUntil, sideSign = 1f, dieTime, pendingDt;
    Vector3 lastPos, baseScale;
    int gridIndex = -1, lodSlot;
    /// <summary>Within FullRateDistance of the player a bot is updated every frame, up to ThirdRateDistance every third frame,
    /// farther every sixth (with the time saved up): the bots walk in from 250 m, most of them are far most of the time.</summary>
    const float FullRateDistance = 40f, ThirdRateDistance = 100f;
    static readonly Unity.Profiling.ProfilerMarker TickMarker = new Unity.Profiling.ProfilerMarker("EnemyBot.TickAll");
    static BotDirector director;

    // Ignore Raycast: the capsule only moves the bot, bullets test the visible box (RaycastBodies)
    void Awake() { gameObject.layer = 2; }
    void OnEnable()
    {
        All.Add(this);
        if (director == null && Application.isPlaying) director = new GameObject("BotDirector").AddComponent<BotDirector>();
    }
    void OnDisable() { All.Remove(this); }

    /// <summary>Every bot's frame, from BotDirector's one Update: 200 MonoBehaviour Updates would cost a native call each.</summary>
    public static void TickAll(float dt)
    {
        if (dt <= 0f) return;                                          // paused: no zero moves, no blow landing on the frozen clock
        using (TickMarker.Auto())
        {
            if (gridFrame != Time.frameCount) BuildGrid();
            for (int i = All.Count - 1; i >= 0; i--)                     // Destroy is deferred: the list does not change meanwhile
            {
                var b = All[i];
                if (b != null) b.Tick(dt);
            }
        }
    }

    /// <summary>
    /// Nearest living bot whose body box the ray (unit dir) crosses closer than maxDist; maxDist becomes the distance to it.
    /// The box is the rendered cube, so a round that visibly touches a corner hits, one that passes beside the box misses.
    /// </summary>
    public static EnemyBot RaycastBodies(Vector3 origin, Vector3 dir, ref float maxDist) { return RaycastBodies(origin, dir, ref maxDist, -1f); }

    /// <summary>The same among the bots whose box the ray enters further than after: the next one behind a hit bot (piercing rounds).</summary>
    public static EnemyBot RaycastBodies(Vector3 origin, Vector3 dir, ref float maxDist, float after)
    {
        EnemyBot best = null;
        for (int i = 0; i < All.Count; i++)
        {
            var b = All[i]; float t;
            if (b.RayHitsBody(origin, dir, maxDist, out t) && t > after) { best = b; maxDist = t; }
        }
        return best;
    }

    /// <summary>Whether the ray (unit dir) crosses this bot's body box closer than maxDist; t = the distance to it.</summary>
    public bool RayHitsBody(Vector3 origin, Vector3 dir, float maxDist, out float t)
    {
        t = 0f;
        if (IsDying || body == null) return false;
        Transform bt = body.transform;
        Vector3 v = bt.position - origin;
        float along = Vector3.Dot(v, dir), r = 0.5f * bt.lossyScale.magnitude;      // bounding sphere first: most bots are far off the line
        if (along < -r || along > maxDist + r || v.sqrMagnitude - along * along > r * r) return false;

        Matrix4x4 m = bt.worldToLocalMatrix;                                  // slab test in the cube's space, where it spans -0.5..0.5
        Vector3 lo = m.MultiplyPoint3x4(origin), ld = m.MultiplyVector(dir);  // the ray parameter stays the world distance
        float t0 = 0f, t1 = maxDist;
        for (int k = 0; k < 3; k++)
        {
            if (Mathf.Abs(ld[k]) < 1e-8f) { if (lo[k] < -0.5f || lo[k] > 0.5f) return false; continue; }
            float inv = 1f / ld[k], a = (-0.5f - lo[k]) * inv, c = (0.5f - lo[k]) * inv;
            if (a > c) { float s = a; a = c; c = s; }
            if (a > t0) t0 = a;
            if (c < t1) t1 = c;
            if (t0 > t1) return false;
        }
        t = t0;
        return true;
    }

    public void Init(Transform player, float health, float moveSpeed, float hitDamage, float size, Color glowColor)
    {
        cc = GetComponent<CharacterController>();
        target = player; targetHealth = player != null ? player.GetComponent<PlayerHealth>() : null;
        maxHealth = health; Health = health; speed = moveSpeed; damage = hitDamage; glow = glowColor;
        transform.localScale = Vector3.one * size; baseScale = transform.localScale;
        lastPos = transform.position; stuckCheck = Time.time + 1f; sideSign = Random.value < 0.5f ? -1f : 1f;
        lodSlot = Random.Range(0, 6); pendingDt = 0f;                  // spread the far bots over the frames they skip
        ApplyLook();
    }

    void ApplyLook()
    {
        if (body == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        body.GetPropertyBlock(block); block.SetColor(EmissionId, glow); block.SetFloat(FlashId, flash); body.SetPropertyBlock(block);
    }

    public void TakeDamage(float amount)
    {
        if (IsDying || !(amount > 0f)) return;                          // no healing through negative or NaN damage
        Health -= amount; flash = 1f; ApplyLook();
        if (Health <= 0f) Die(true);
    }

    const float KnockDeceleration = 14f;
    Vector3 knock; float staggerUntil;
    public bool IsStaggered { get { return Time.time < staggerUntil; } }

    /// <summary>
    /// A blow throws the bot back (horizontal velocity, m/s, slowing down) and puts it off balance for stagger seconds:
    /// it neither walks nor strikes until it has caught itself (at least as long as the slide lasts).
    /// </summary>
    public void Knockback(Vector3 velocity, float stagger)
    {
        if (IsDying) return;
        velocity.y = 0f;
        knock = velocity;
        float until = Time.time + Mathf.Max(stagger, velocity.magnitude / KnockDeceleration);
        if (until > staggerUntil) staggerUntil = until;
        if (nextAttack < staggerUntil) nextAttack = staggerUntil;
    }

    public void Die(bool killedByPlayer)
    {
        if (IsDying) return;
        IsDying = true; dieTime = Time.time;
        if (cc != null) cc.enabled = false;
        if (killedByPlayer && WaveSurvivalGame.Instance != null) WaveSurvivalGame.Instance.OnBotKilled(this);
    }

    // ---- neighbours: one spatial hash per frame instead of every bot reading every other bot's transform (O(n²))
    const float SeparationRadius2 = 2.6f;                       // squared distance at which two bots push apart
    const float CellSize = 1.62f;                                // >= the separation radius: neighbours are in the 3x3 cells around
    static int gridFrame = -1;
    static Vector3[] gridPos = new Vector3[256];
    static int[] gridNext = new int[256];
    static readonly Dictionary<long, int> gridHead = new Dictionary<long, int>(512);

    static long CellKey(int x, int z) { return ((long)x << 32) ^ (uint)z; }

    static void BuildGrid()
    {
        gridFrame = Time.frameCount;
        int n = All.Count;
        if (gridPos.Length < n) { gridPos = new Vector3[n * 2]; gridNext = new int[n * 2]; }
        gridHead.Clear();
        for (int i = 0; i < n; i++)
        {
            var b = All[i]; b.gridIndex = i;
            Vector3 p = b.transform.position; gridPos[i] = p;
            if (b.IsDying) { gridNext[i] = -1; continue; }
            long k = CellKey(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
            int head; gridNext[i] = gridHead.TryGetValue(k, out head) ? head : -1;
            gridHead[k] = i;
        }
    }

    Vector3 Separation(Vector3 pos)
    {
        if (gridFrame != Time.frameCount) BuildGrid();
        Vector3 sep = Vector3.zero;
        int cx = Mathf.FloorToInt(pos.x / CellSize), cz = Mathf.FloorToInt(pos.z / CellSize);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int i; if (!gridHead.TryGetValue(CellKey(cx + dx, cz + dz), out i)) continue;
                for (; i >= 0; i = gridNext[i])
                {
                    if (i == gridIndex) continue;
                    Vector3 d = pos - gridPos[i]; d.y = 0f; float m = d.sqrMagnitude;
                    if (m < SeparationRadius2 && m > 0.0001f) sep += d / m;
                }
            }
        return sep;
    }

    void Tick(float dt)
    {
        if (flash > 0f) { flash = Mathf.MoveTowards(flash, 0f, dt * 7f); ApplyLook(); }
        if (IsDying)
        {
            float k = 1f - (Time.time - dieTime) / 0.18f;
            if (k <= 0f) { Destroy(gameObject); return; }
            transform.localScale = new Vector3(baseScale.x * (1.4f - 0.4f * k), baseScale.y * k, baseScale.z * (1.4f - 0.4f * k));
            return;
        }
        if (target == null || cc == null) return;

        Vector3 pos = transform.position, to = target.position - pos; to.y = 0f;
        float dist = to.magnitude;
        // far from the player a bot thinks and moves every third / sixth frame, with the time saved up: a third / a sixth of the
        // CharacterController cost; a far step may be longer (it is seen from far away), a near one stays short after a hitch
        pendingDt += dt;
        int every = dist <= FullRateDistance ? 1 : dist <= ThirdRateDistance ? 3 : 6;
        if (every > 1 && (Time.frameCount + lodSlot) % every != 0) return;
        dt = Mathf.Min(pendingDt, every == 1 ? 0.1f : 0.25f); pendingDt = 0f;

        if (knock.sqrMagnitude > 1e-4f)                                 // thrown back by a blow: slides, decelerating
        {
            vy = cc.isGrounded ? -2f : vy - 25f * dt;
            cc.Move((knock + Vector3.up * vy) * dt);
            knock = Vector3.MoveTowards(knock, Vector3.zero, KnockDeceleration * dt);
        }
        if (Time.time < staggerUntil) return;                           // off balance: no step and no blow

        Vector3 dir = dist > 0.01f ? to / dist : transform.forward;
        if (dist <= attackRange)
        {
            if (Time.time >= nextAttack && targetHealth != null && !targetHealth.IsDead && Mathf.Abs(target.position.y - pos.y) < 2.5f)
            {
                nextAttack = Time.time + attackInterval; targetHealth.TakeDamage(damage);
            }
        }
        else
        {
            Vector3 sep;                                              // keep the crowd from collapsing into one point
            using (SepMarker.Auto()) sep = Separation(pos);
            if (Time.time < sidestepUntil) dir = (dir * 0.35f + new Vector3(-dir.z, 0f, dir.x) * sideSign).normalized;
            Vector3 move = (dir + sep * 0.9f); move.y = 0f;
            if (move.sqrMagnitude > 1f) move.Normalize();
            vy = cc.isGrounded ? -2f : vy - 25f * dt;
            float pace = speed * UpgradeSystem.Stats.botSpeed;                 // Time Warp slows them all
            using (MoveMarker.Auto()) cc.Move((move * pace + Vector3.up * vy) * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Mathf.Min(1f, dt * 8f));

            if (Time.time >= stuckCheck)                              // blocked by a rock, a tree or a wall of rocks: go around
            {
                Vector3 moved = transform.position - lastPos; moved.y = 0f;
                if (moved.magnitude < pace * 0.25f) { sidestepUntil = Time.time + Random.Range(1.2f, 2.2f); sideSign = -sideSign; }
                lastPos = transform.position; stuckCheck = Time.time + 1f;
            }
        }
        if (pos.y < -60f) Destroy(gameObject);
    }
}

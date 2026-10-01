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

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), FlashId = Shader.PropertyToID("_Flash");
    CharacterController cc;
    MaterialPropertyBlock block;
    Transform target; PlayerHealth targetHealth;
    Color glow = new Color(1f, 0.35f, 0.1f);
    float vy, nextAttack, flash, stuckCheck, sidestepUntil, sideSign = 1f, dieTime;
    Vector3 lastPos, baseScale;

    // Ignore Raycast: the capsule only moves the bot, bullets test the visible box (RaycastBodies)
    void Awake() { gameObject.layer = 2; }
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// <summary>
    /// Nearest living bot whose body box the ray (unit dir) crosses closer than maxDist; maxDist becomes the distance to it.
    /// The box is the rendered cube, so a round that visibly touches a corner hits, one that passes beside the box misses.
    /// </summary>
    public static EnemyBot RaycastBodies(Vector3 origin, Vector3 dir, ref float maxDist)
    {
        EnemyBot best = null;
        for (int i = 0; i < All.Count; i++)
        {
            var b = All[i]; if (b.IsDying || b.body == null) continue;
            Transform bt = b.body.transform;
            Vector3 v = bt.position - origin;
            float along = Vector3.Dot(v, dir), r = 0.5f * bt.lossyScale.magnitude;      // bounding sphere first: most bots are far off the line
            if (along < -r || along > maxDist + r || v.sqrMagnitude - along * along > r * r) continue;

            Matrix4x4 m = bt.worldToLocalMatrix;                                  // slab test in the cube's space, where it spans -0.5..0.5
            Vector3 lo = m.MultiplyPoint3x4(origin), ld = m.MultiplyVector(dir);  // the ray parameter stays the world distance
            float t0 = 0f, t1 = maxDist; bool miss = false;
            for (int k = 0; k < 3 && !miss; k++)
            {
                if (Mathf.Abs(ld[k]) < 1e-8f) { miss = lo[k] < -0.5f || lo[k] > 0.5f; continue; }
                float inv = 1f / ld[k], a = (-0.5f - lo[k]) * inv, c = (0.5f - lo[k]) * inv;
                if (a > c) { float s = a; a = c; c = s; }
                if (a > t0) t0 = a;
                if (c < t1) t1 = c;
                miss = t0 > t1;
            }
            if (!miss) { best = b; maxDist = t0; }
        }
        return best;
    }

    public void Init(Transform player, float health, float moveSpeed, float hitDamage, float size, Color glowColor)
    {
        cc = GetComponent<CharacterController>();
        target = player; targetHealth = player != null ? player.GetComponent<PlayerHealth>() : null;
        maxHealth = health; Health = health; speed = moveSpeed; damage = hitDamage; glow = glowColor;
        transform.localScale = Vector3.one * size; baseScale = transform.localScale;
        lastPos = transform.position; stuckCheck = Time.time + 1f; sideSign = Random.value < 0.5f ? -1f : 1f;
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
        if (IsDying) return;
        Health -= amount; flash = 1f; ApplyLook();
        if (Health <= 0f) Die(true);
    }

    public void Die(bool killedByPlayer)
    {
        if (IsDying) return;
        IsDying = true; dieTime = Time.time;
        if (cc != null) cc.enabled = false;
        if (killedByPlayer && WaveSurvivalGame.Instance != null) WaveSurvivalGame.Instance.OnBotKilled(this);
    }

    void Update()
    {
        float dt = Time.deltaTime;
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
            Vector3 sep = Vector3.zero;                               // keep the crowd from collapsing into one point
            for (int i = 0; i < All.Count; i++)
            {
                var o = All[i]; if (o == this || o.IsDying) continue;
                Vector3 d = pos - o.transform.position; d.y = 0f; float m = d.sqrMagnitude;
                if (m < 2.6f && m > 0.0001f) sep += d / m;
            }
            if (Time.time < sidestepUntil) dir = (dir * 0.35f + new Vector3(-dir.z, 0f, dir.x) * sideSign).normalized;
            Vector3 move = (dir + sep * 0.9f); move.y = 0f;
            if (move.sqrMagnitude > 1f) move.Normalize();
            vy = cc.isGrounded ? -2f : vy - 25f * dt;
            cc.Move((move * speed + Vector3.up * vy) * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), dt * 8f);

            if (Time.time >= stuckCheck)                              // blocked by a rock, a tree or a wall of rocks: go around
            {
                Vector3 moved = transform.position - lastPos; moved.y = 0f;
                if (moved.magnitude < speed * 0.25f) { sidestepUntil = Time.time + Random.Range(1.2f, 2.2f); sideSign = -sideSign; }
                lastPos = transform.position; stuckCheck = Time.time + 1f;
            }
        }
        if (pos.y < -60f) Destroy(gameObject);
    }
}

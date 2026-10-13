using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Secondary motion of loose parts (tails, hair, ears, chest) after the animation, from the Source models' $jigglebone data
/// (is_flexible; set up by EnemyPrefabs from the QC). Each bone's tip is a point on a spring: pulled towards where the
/// animation puts it (stiffness, damping), weighed down a little (tip mass), never more than angleLimit away from it; the bone
/// turns to point at it. Bones are done parent first, so a chain (a tail) swings as a whole.
/// The bones the humanoid animation does not move keep their rest pose under the spring; each frame starts from it.
/// Fixed 60 Hz steps (the same at any frame rate); only on the characters near the camera (distance and a count per frame) —
/// the others hold the animated pose. Lengths and the pull down are at the model's natural size and grow with its scale (a
/// bigger body swings the same way).
/// </summary>
public class JiggleBones : MonoBehaviour
{
    [Serializable]
    public class Bone
    {
        public Transform bone;
        [Tooltip("Distance to the simulated tip, m at the model's natural size (Source: length).")] public float length = 0.5f;
        [Tooltip("Pull of the tip back to the animated pose, 1/s² (Source: pitch / yaw stiffness).")] public float stiffness = 100f;
        [Tooltip("Velocity damping, 1/s (Source: pitch / yaw damping).")] public float damping = 9f;
        [Tooltip("Downward pull on the tip, m/s² at the model's natural size (Source: tip_mass).")] public float gravity;
        [Tooltip("Furthest the tip strays from the animated pose, degrees (Source: angle_constraint).")] public float angleLimit = 45f;
        [Tooltip("The bone's axis towards its tip, in its own space at rest.")] public Vector3 axis = Vector3.up;
        public Quaternion restLocal = Quaternion.identity;
        [NonSerialized] public Vector3 tip, velocity;
    }

    public List<Bone> bones = new List<Bone>();
    [Tooltip("Springs run only within this distance of the camera, m.")] public float activeDistance = 28f;

    /// <summary>At most this many characters run their springs in a frame (the nearest come first in practice: far ones are cut by
    /// distance). Characters have up to ~50 springs: 16 of them cost ~1 ms in a crowd fight.</summary>
    public static int MaxActivePerFrame = 16;
    const float Step = 1f / 60f;
    static int countFrame = -1, activeCount;

    float pending;
    bool live;                                                        // the tips are being simulated (false: reset on the next start)

    /// <summary>Whether the springs ran this frame (for the tests).</summary>
    public bool Active { get; private set; }
    /// <summary>The largest angle between a tip and where the animation puts it, this frame, degrees (for the tests).</summary>
    public float MaxDeflection { get; private set; }

    void OnEnable() { live = false; pending = 0f; }

    /// <summary>Puts every tip back where the pose has it, at rest (after a teleport or a spawn).</summary>
    public void ResetTips() { live = false; }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        Active = false; MaxDeflection = 0f;
        if (dt <= 0f || bones.Count == 0) return;
        var cam = Camera.main;
        bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude <= activeDistance * activeDistance;
        if (near && countFrame != Time.frameCount) { countFrame = Time.frameCount; activeCount = 0; }
        if (!near || activeCount >= MaxActivePerFrame)
        {
            if (live) foreach (var b in bones) if (b.bone != null) b.bone.localRotation = b.restLocal;   // once: back to the pose, then left alone
            live = false; return;
        }
        activeCount++; Active = true;
        foreach (var b in bones) if (b.bone != null) b.bone.localRotation = b.restLocal;     // the pose the animation left, before the springs

        float scale = transform.lossyScale.y;
        if (!live)
        {
            foreach (var b in bones) if (b.bone != null) { b.tip = b.bone.position + b.bone.TransformDirection(b.axis) * (b.length * scale); b.velocity = Vector3.zero; }
            live = true; pending = 0f;
        }
        pending = Mathf.Min(pending + dt, Step * 4f);                 // after a hitch: at most four steps, then on
        int steps = 0;
        while (pending >= Step) { pending -= Step; steps++; }
        foreach (var b in bones)
        {
            if (b.bone == null) continue;
            float length = b.length * scale;
            Vector3 root = b.bone.position, goalDir = b.bone.TransformDirection(b.axis).normalized, goal = root + goalDir * length;
            if ((b.tip - goal).sqrMagnitude > length * length * 9f) { b.tip = goal; b.velocity = Vector3.zero; }   // teleported
            for (int s = 0; s < steps; s++)
            {
                Vector3 acc = (goal - b.tip) * b.stiffness - b.velocity * b.damping + Vector3.down * (b.gravity * scale);
                b.velocity += acc * Step; b.tip += b.velocity * Step;
            }
            Vector3 dir = b.tip - root;
            if (dir.sqrMagnitude < 1e-8f) dir = goalDir; else dir.Normalize();
            float angle = Vector3.Angle(goalDir, dir);
            if (angle > b.angleLimit)                                     // past the limit: back onto it, the speed outwards lost
            {
                dir = Vector3.Slerp(goalDir, dir, b.angleLimit / angle).normalized;
                Vector3 onLimit = root + dir * length;
                b.velocity -= Vector3.Project(b.velocity, b.tip - onLimit);
                b.tip = onLimit;
            }
            else b.tip = root + dir * length;                           // the tip stays at the bone's length
            MaxDeflection = Mathf.Max(MaxDeflection, Mathf.Min(angle, b.angleLimit));
            b.bone.rotation = Quaternion.FromToRotation(goalDir, dir) * b.bone.rotation;
        }
    }
}

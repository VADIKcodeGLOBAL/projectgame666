using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The animation of a character enemy, driven by its <see cref="EnemyBot"/>: ground speed, climbing, a blow, a hit, a stagger,
/// death. The controller (Assets/Data/Animations/EnemyCharacter.controller, made by the "anims" command) has one slot clip per
/// <see cref="AnimTag"/>; each character gets one of a few sets of clips picked from the <see cref="AnimationLibrary"/> (an
/// AnimatorOverrideController per set, shared), so a crowd does not move in step.
/// The Animator runs as usual (Unity updates them all together on worker threads; off screen the bones are left alone);
/// <see cref="Step"/> feeds it what the body did at the bot's own (level of detail) rate. Root motion is not applied — the bot
/// moves the body. Locomotion is a 1D blend idle / walk / run on a "gait" value worked out
/// from the speed and the natural speeds of the chosen walk and run; faster than the run, the run plays faster (up to maxPlayback).
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimator : MonoBehaviour
{
    public AnimationLibrary library;
    [Tooltip("The controller's slot clips, in AnimTag order (the clips it was built with).")] public AnimationClip[] slots = new AnimationClip[0];
    [Tooltip("Different clip sets shared by all characters of this controller.")] [Min(1)] public int sets = 6;
    [Tooltip("Fastest the run clip is played, x its natural speed.")] public float maxPlayback = 2.2f;
    [Tooltip("Smoothing of the gait value, s.")] public float gaitDamping = 0.12f;

    public static readonly int GaitId = Animator.StringToHash("Gait"), PlayId = Animator.StringToHash("Play"), ClimbId = Animator.StringToHash("Climb"),
                               AttackId = Animator.StringToHash("Attack"), HitId = Animator.StringToHash("Hit"), StaggerId = Animator.StringToHash("Stagger"),
                               DeadId = Animator.StringToHash("Dead");

    /// <summary>One set of clips over the controller, with the natural speeds of its walk and run.</summary>
    class ClipSet { public AnimatorOverrideController controller; public float walk, run, climb; public string names; }
    static readonly Dictionary<RuntimeAnimatorController, List<ClipSet>> setsByController = new Dictionary<RuntimeAnimatorController, List<ClipSet>>();

    Animator animator;
    ClipSet set;
    float scale = 1f;

    public Animator Animator { get { return animator; } }
    /// <summary>Names of the clips this character plays (for the log and the tests).</summary>
    public string ClipNames { get { return set != null ? set.names : ""; } }
    public float WalkSpeed { get { return set != null ? set.walk * scale : 1.5f; } }
    public float RunSpeed { get { return set != null ? set.run * scale : 4f; } }

    void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.enabled = true;                                         // Unity updates all animators together, on worker threads
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // off screen: the state runs on, the bones are not posed
    }

    /// <summary>Picks this character's clip set. The clips' natural speeds (measured on the library's reference character) grow
    /// with this character's own build and its scale in the world: call it once the body has its size.</summary>
    public void Init(System.Random rnd)
    {
        if (animator == null) Awake();
        float build = library != null && library.referenceHumanScale > 0f && animator.isHuman ? animator.humanScale / library.referenceHumanScale : 1f;
        scale = Mathf.Max(0.1f, build * transform.lossyScale.y);
        var sets = SetsFor(animator.runtimeAnimatorController);
        if (sets.Count > 0) { set = sets[rnd.Next(sets.Count)]; animator.runtimeAnimatorController = set.controller; }
        animator.Rebind();
        animator.Update(0f);
    }

    List<ClipSet> SetsFor(RuntimeAnimatorController baseController)
    {
        if (baseController is AnimatorOverrideController aoc) baseController = aoc.runtimeAnimatorController;
        if (baseController == null) return new List<ClipSet>();
        List<ClipSet> list;
        if (setsByController.TryGetValue(baseController, out list)) return list;
        list = new List<ClipSet>();
        if (library != null && slots != null && slots.Length > 0)
        {
            var rnd = new System.Random(1234);
            for (int s = 0; s < sets; s++)
            {
                var o = new AnimatorOverrideController(baseController) { name = baseController.name + " set " + s };
                var cs = new ClipSet { controller = o, walk = 1.5f, run = 4f, climb = 1f };
                var names = new List<string>();
                for (int t = 0; t < slots.Length; t++)
                {
                    if (slots[t] == null) continue;
                    var e = library.Pick((AnimTag)t, rnd);
                    if (e == null) continue;
                    o[slots[t]] = e.clip;
                    names.Add(e.clip.name);
                    if ((AnimTag)t == AnimTag.Walk && e.speed > 0.1f) cs.walk = e.speed;
                    if ((AnimTag)t == AnimTag.Run && e.speed > 0.1f) cs.run = e.speed;
                    if ((AnimTag)t == AnimTag.Climb && e.speed > 0.1f) cs.climb = e.speed;
                }
                if (cs.run < cs.walk * 1.2f) cs.run = cs.walk * 1.2f;
                cs.names = string.Join(", ", names.ToArray());
                list.Add(cs);
            }
        }
        setsByController[baseController] = list;
        return list;
    }

    /// <summary>What the body did over the last dt: moving over the ground at speed m/s (climbing: up a steep slope). Sets the
    /// animator's parameters; the animator itself runs every frame with Unity's others.</summary>
    public void Step(float dt, float speed, bool climbing)
    {
        if (dt <= 0f) return;
        float walk = WalkSpeed, run = RunSpeed, gait, play = 1f;
        if (climbing) { gait = 0f; play = Mathf.Clamp(speed / Mathf.Max(0.2f, (set != null ? set.climb : 1f) * scale), 0.6f, maxPlayback); }
        else if (speed < 0.05f) gait = 0f;
        else if (speed <= walk) gait = speed / walk;
        else if (speed <= run) gait = 1f + (speed - walk) / (run - walk);
        else { gait = 2f; play = Mathf.Min(maxPlayback, speed / run); }
        animator.SetFloat(GaitId, gait, gaitDamping, dt);
        animator.SetFloat(PlayId, play);
        animator.SetBool(ClimbId, climbing);
    }

    public void Attack() { animator.SetTrigger(AttackId); }
    public void Hit() { animator.SetTrigger(HitId); }
    public void Stagger() { animator.SetTrigger(StaggerId); }
    public void Die() { animator.SetBool(DeadId, true); }
}

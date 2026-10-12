using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EditorAutomation
{
    /// <summary>
    /// "animtest": the character enemies in Play mode (hill level, waves off). The prefab (animator, library, slots, hit box,
    /// jiggle bones); a bot running in from 30 m (gait, both feet on the ground and off it, hips not drifting off the root,
    /// the tail swinging), its blow at the player, a hit reaction, the hit box on the body and a round hitting it, death (the
    /// clip, then gone into the ground); the tail's swing the same at 30 and 144 fps.
    /// </summary>
    static IEnumerator AnimTests()
    {
        var game = WaveSurvivalGame.Instance;
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        var t = Terrain.activeTerrain;
        if (game == null || fp == null || t == null || game.zone == null) throw new Exception("game, player, terrain or zone missing");
        game.enabled = false;
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
        var prefab = game.characterBots != null ? game.characterBots.FirstOrDefault(b => b != null) : null;
        if (prefab == null) throw new Exception("no character enemy in the level (run \"chars\", \"anims\", \"enemies\")");
        Func<Vector3, float> ground = p => t.SampleHeight(p) + t.transform.position.y;
        Func<Vector3, Vector3> onGround = p => { p.y = ground(p); return p; };
        Vector3 c = onGround(game.zone.transform.position);
        fp.Teleport(c + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.LookRotation(Vector3.forward); fp.Pitch = 0f;
        var hp = fp.GetComponent<PlayerHealth>();
        yield return Wait(0.3f);

        // ---- the prefab
        var ca = prefab.character; var jb = ca != null ? ca.GetComponent<JiggleBones>() : null;
        var tags = new[] { AnimTag.Idle, AnimTag.Walk, AnimTag.Run, AnimTag.Climb, AnimTag.Attack, AnimTag.Hit, AnimTag.Stagger, AnimTag.Death };
        bool slotsOk = ca != null && ca.library != null && ca.slots != null && ca.slots.Length >= tags.Length && tags.All(g => ca.slots[(int)g] != null);
        Check(slotsOk && prefab.hitBox != null && tags.All(g => ca.library.Of(g).Count > 0) && jb != null && jb.bones.Count > 0,
              "prefab " + prefab.name + ": library " + (ca != null && ca.library != null ? ca.library.clips.Count + " clips" : "MISSING") + ", slots " + (slotsOk ? "ok" : "MISSING")
              + ", hit box " + (prefab.hitBox != null) + ", jiggle bones " + (jb != null ? jb.bones.Count : 0)
              + "; clips per slot: " + (ca != null && ca.library != null ? string.Join(", ", tags.Select(g => g + " " + ca.library.Of(g).Count).ToArray()) : "-"));

        // ---- a bot running in from 30 m
        Vector3 from = onGround(c + Vector3.forward * 30f);
        var bot = Object.Instantiate(prefab, from + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.back));
        bot.Init(fp.transform, 1e6f, game.baseSpeed, 0f, 1f, Color.white); moveTemp.Add(bot.gameObject);
        var anim = bot.character.Animator; var bjb = bot.character.GetComponent<JiggleBones>();
        Transform lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot), rf = anim.GetBoneTransform(HumanBodyBones.RightFoot), hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        float gaitMax = 0f, lLo = 9f, lHi = -9f, rLo = 9f, rHi = -9f, drift = 0f, swing = 0f; bool finite = true;
        float t0 = Time.time;
        while (Time.time < t0 + 3f && Horiz(bot.transform.position, c) > 3.5f)
        {
            yield return null;
            if (Time.time < t0 + 0.6f) continue;                           // up to speed
            gaitMax = Mathf.Max(gaitMax, anim.GetFloat(CharacterAnimator.GaitId));
            float l = lf.position.y - ground(lf.position), r = rf.position.y - ground(rf.position);
            lLo = Mathf.Min(lLo, l); lHi = Mathf.Max(lHi, l); rLo = Mathf.Min(rLo, r); rHi = Mathf.Max(rHi, r);
            drift = Mathf.Max(drift, Horiz(hips.position, bot.transform.position));
            if (bjb != null && bjb.Active) swing = Mathf.Max(swing, bjb.MaxDeflection);
            finite &= !float.IsNaN(hips.position.x) && bjb.bones.All(b => b.bone == null || !float.IsNaN(b.bone.rotation.x));
        }
        Check(gaitMax > 1.5f && lLo < 0.15f && rLo < 0.15f && lHi > 0.12f && rHi > 0.12f && drift < 0.4f && finite,
              "running in at " + F(game.baseSpeed) + " m/s (" + bot.character.ClipNames + "): gait up to " + F3(gaitMax) + ", left foot " + F3(lLo) + ".." + F3(lHi)
              + " m over the ground, right " + F3(rLo) + ".." + F3(rHi) + " m, hips off the root " + F3(drift) + " m");
        Check(swing > 2f && swing <= jb.bones.Max(b => b.angleLimit) + 0.5f, "loose parts swing while running: up to " + F3(swing) + " deg off the pose (limits " + string.Join("/", jb.bones.Select(b => b.angleLimit.ToString("0")).Distinct().ToArray()) + " deg)");

        // ---- its blow at the player (no damage), a hit reaction, the hit box
        bool struck = false; t0 = Time.time;
        while (Time.time < t0 + 5f && !struck)
        {
            yield return null;
            struck = anim.GetCurrentAnimatorStateInfo(0).IsName("Attack") || anim.GetNextAnimatorStateInfo(0).IsName("Attack");
        }
        Check(struck, "at the player it strikes: the Attack state after " + F3(Time.time - t0) + " s, " + F(Horiz(bot.transform.position, fp.transform.position)) + " m away");
        yield return Wait(1.2f);
        bot.TakeDamage(1f);
        bool reacted = false; t0 = Time.time;
        while (Time.time < t0 + 0.6f && !reacted)
        {
            yield return null;
            reacted = anim.GetCurrentAnimatorStateInfo(1).IsName("Hit") || anim.GetNextAnimatorStateInfo(1).IsName("Hit");
        }
        Bounds body = new Bounds(); bool first = true;
        foreach (var r in bot.flashRenderers) if (r is SkinnedMeshRenderer) { if (first) { body = r.bounds; first = false; } else body.Encapsulate(r.bounds); }
        Vector3 hbc = bot.BodyBounds.center, eye = fp.cameraPivot.position;
        float dist = 600f; var shot = EnemyBot.RaycastBodies(eye, (hbc - eye).normalized, ref dist);
        Check(reacted && body.Contains(hbc) && shot == bot, "a hit plays the reaction on the upper body " + reacted + "; hit box centre inside the body " + body.Contains(hbc) + "; a round at it hits " + (shot == bot ? "the bot" : "NOTHING") + " at " + F(dist) + " m");

        // ---- death: the clip, a moment lying there, then into the ground
        bot.Die(true);
        bool dying = false; t0 = Time.time; float lowest = bot.transform.position.y;
        while (bot != null && Time.time < t0 + bot.deathLinger + 2f)
        {
            yield return null;
            if (bot == null) break;
            dying |= anim != null && (anim.GetCurrentAnimatorStateInfo(0).IsName("Death") || anim.GetNextAnimatorStateInfo(0).IsName("Death"));
        }
        float gone = Time.time - t0; float d2 = 600f;
        Check(dying && bot == null && gone > prefab.deathLinger && EnemyBot.RaycastBodies(eye, (hbc - eye).normalized, ref d2) == null,
              "death: the Death state " + dying + ", gone after " + F3(gone) + " s (lies " + F(prefab.deathLinger) + " s, then sinks), not hit meanwhile");

        // ---- the swing does not depend on the frame rate
        var swings = new float[2]; int[] rates = { 30, 144 };
        for (int k = 0; k < 2; k++)
        {
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = rates[k];
            var b2 = Object.Instantiate(prefab, onGround(c + Vector3.forward * 26f) + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.back));
            b2.Init(fp.transform, 1e6f, game.baseSpeed, 0f, 1f, Color.white); moveTemp.Add(b2.gameObject);
            var j2 = b2.character.GetComponent<JiggleBones>(); float sum = 0f; int n = 0; t0 = Time.time;
            while (Time.time < t0 + 2.2f)
            {
                yield return null;
                if (Time.time > t0 + 0.8f && j2.Active) { sum += j2.MaxDeflection; n++; }
            }
            swings[k] = n > 0 ? sum / n : 0f;
            b2.Die(false); yield return Wait(0.2f);
        }
        Application.targetFrameRate = savedFrameRate; QualitySettings.vSyncCount = savedVSync;
        Check(swings[0] > 1f && Mathf.Abs(swings[0] - swings[1]) <= Mathf.Max(swings[0], swings[1]) * 0.35f,
              "swing while running in at 30 / 144 fps: " + F3(swings[0]) + " / " + F3(swings[1]) + " deg on average");
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EditorAutomation
{
    /// <summary>
    /// "animtest": the character enemies in Play mode (hill level, waves off). The roster (every variant, tiers growing with the
    /// models' size, the waves' picks, NSFW only when allowed, NSFW enemies changed for safe ones when it is turned off); the
    /// prefab (animator, library, slots, hit box, jiggle bones, BodyScale times the natural size); a bot running in from 30 m (gait, both feet on the ground and off it, hips not drifting off the root,
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
        var roster = game.roster;
        var entries = roster != null ? roster.enemies.Where(e => e != null && e.prefab != null).ToList() : new List<EnemyRoster.Entry>();
        if (entries.Count == 0) throw new Exception("no character enemy in the level (run \"chars\", \"anims\", \"enemies\")");
        // the reference character (a tail and hair: jiggle bones), else the first safe one
        var refEntry = entries.FirstOrDefault(e => e.variant == "kroxigorpm_jeans_sfw") ?? entries.FirstOrDefault(e => !e.nsfw) ?? entries[0];
        var prefab = refEntry.prefab;
        Func<Vector3, float> ground = p => t.SampleHeight(p) + t.transform.position.y;
        Func<Vector3, Vector3> onGround = p => { p.y = ground(p); return p; };
        Vector3 c = onGround(game.zone.transform.position);
        fp.Teleport(c + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.LookRotation(Vector3.forward); fp.Pitch = 0f;
        var hp = fp.GetComponent<PlayerHealth>();
        yield return Wait(0.3f);

        // ---- the roster: every variant, the tiers by size, what the waves bring
        var models = entries.GroupBy(e => e.model).Select(g => new { model = g.Key, height = g.Max(e => e.height), tier = g.Min(e => e.tier), edited = g.Any(e => e.tier != e.autoTier) })
                            .OrderBy(m => m.height).ToList();
        bool tiersBySize = true;
        for (int i = 1; i < models.Count; i++) if (!models[i].edited && !models[i - 1].edited && models[i].tier < models[i - 1].tier) tiersBySize = false;
        int lastTier = entries.Max(e => e.tier);
        Check(entries.Count == roster.enemies.Count && tiersBySize && models[0].tier == 0 && lastTier > 0,
              "roster: " + entries.Count + " variants (" + entries.Count(e => e.nsfw) + " NSFW) of " + models.Count + " models; tiers by size: "
              + string.Join(", ", models.Select(m => m.model + " " + F(m.height) + " m t" + m.tier).ToArray()));
        var rnd = new System.Random(7); Func<float> r01 = () => (float)rnd.NextDouble();
        Func<int, bool, List<EnemyRoster.Entry>> picks = (wave, nsfw) => Enumerable.Range(0, 600).Select(i => roster.Of(roster.Pick(wave, nsfw, r01))).ToList();
        var w1 = picks(1, false); var wl = picks(lastTier + 1, true); var wls = picks(lastTier + 1, false);
        Func<List<EnemyRoster.Entry>, int, int> ofTier = (l, k) => l.Count(e => e != null && e.tier == k);
        bool anyNsfw = entries.Any(e => e.nsfw && e.enabled);
        Check(w1.All(e => e != null && e.tier == 0 && !e.nsfw) && wls.All(e => e != null && !e.nsfw) && (!anyNsfw || wl.Any(e => e.nsfw))
              && ofTier(wl, lastTier) > ofTier(wl, 0) && w1.Select(e => e.model).Distinct().Count() == models.Count(m => m.tier == 0),
              "wave 1 brings tier 0 only (" + string.Join(", ", w1.Select(e => e.model).Distinct().ToArray()) + "), safe ones only; wave " + (lastTier + 1) + ": tier 0 x" + ofTier(wl, 0)
              + " .. tier " + lastTier + " x" + ofTier(wl, lastTier) + " of 600, NSFW x" + wl.Count(e => e.nsfw) + " when allowed, x" + wls.Count(e => e.nsfw) + " when not");

        // ---- the prefab
        var ca = prefab.character; var jb = ca != null ? ca.GetComponent<JiggleBones>() : null;
        var tags = new[] { AnimTag.Idle, AnimTag.Walk, AnimTag.Run, AnimTag.Climb, AnimTag.Attack, AnimTag.Hit, AnimTag.Stagger, AnimTag.Death };
        bool slotsOk = ca != null && ca.library != null && ca.slots != null && ca.slots.Length >= tags.Length && tags.All(g => ca.slots[(int)g] != null);
        var prefabCtrl = ca != null ? ca.GetComponent<Animator>().runtimeAnimatorController : null;
        Check(slotsOk && prefabCtrl != null && prefab.hitBox != null && tags.All(g => ca.library.Of(g).Count > 0) && jb != null && jb.bones.Count > 0,
              "prefab " + prefab.name + ": controller " + (prefabCtrl != null ? prefabCtrl.name : "MISSING") + ", library " + (ca != null && ca.library != null ? ca.library.clips.Count + " clips" : "MISSING") + ", slots " + (slotsOk ? "ok" : "MISSING")
              + ", hit box " + (prefab.hitBox != null) + ", jiggle bones " + (jb != null ? jb.bones.Count : 0)
              + "; clips per slot: " + (ca != null && ca.library != null ? string.Join(", ", tags.Select(g => g + " " + ca.library.Of(g).Count).ToArray()) : "-"));

        // ---- a bot running in from 30 m, BodyScale times its natural size
        Vector3 from = onGround(c + Vector3.forward * 30f);
        var bot = Object.Instantiate(prefab, from + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.back));
        bot.Init(fp.transform, 1e6f, game.baseSpeed, 0f, 1f, Color.white); moveTemp.Add(bot.gameObject);
        float scale = bot.character.transform.lossyScale.y, tall = 0f;
        foreach (var r in bot.flashRenderers) if (r is SkinnedMeshRenderer && r.enabled) tall = Mathf.Max(tall, r.bounds.size.y);
        Check(Mathf.Abs(scale - EnemyPrefabs.BodyScale) < 0.01f && tall > refEntry.height * scale * 0.85f && tall < refEntry.height * scale * 1.3f,
              "the body: " + F(scale) + " x its natural " + F(refEntry.height) + " m, drawn " + F(tall) + " m tall (bounds, arms and tail included)");
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
        Check(gaitMax > 1.5f && lLo < 0.15f * scale && rLo < 0.15f * scale && lHi > 0.12f && rHi > 0.12f && drift < 0.4f * scale && finite,
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

        // ---- NSFW turned off in the middle of a game: the NSFW enemies there become safe ones, in place, with their health
        var nsfwEntry = entries.FirstOrDefault(e => e.nsfw && e.enabled);
        if (nsfwEntry == null) { Log("  (no NSFW variant in the roster: the swap is not tested)"); yield break; }
        bool savedNsfw = GameSettings.NsfwModels;
        try
        {
            GameSettings.NsfwModels = true;
            Vector3 at = onGround(c + Vector3.forward * 12f);
            var nb = Object.Instantiate(nsfwEntry.prefab, at + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.back));
            nb.Init(fp.transform, 500f, game.baseSpeed, 0f, 1.08f, Color.white); nb.TakeDamage(120f);
            yield return null;
            Vector3 was = nb.transform.position;
            game.enabled = true;                                         // listening to the setting (one synchronous call, no frame)
            GameSettings.NsfwModels = false;
            game.enabled = false;
            var now = EnemyBot.All.Where(b => b != null && !b.IsDying && Horiz(b.transform.position, was) < 0.05f).ToList();
            var swapped = now.FirstOrDefault();
            if (swapped != null) moveTemp.Add(swapped.gameObject);
            Check(now.Count == 1 && !swapped.nsfw && swapped.model == nsfwEntry.model && Mathf.Abs(swapped.Health - 380f) < 0.01f && Mathf.Abs(swapped.transform.localScale.x - 1.08f) < 0.001f
                  && !EnemyBot.All.Any(b => b != null && b.nsfw),
                  "NSFW off: " + nsfwEntry.variant + " became " + (swapped != null ? swapped.name.Replace("(Clone)", "") + " (" + swapped.model + ", health " + F(swapped.Health) + ")" : "NOTHING")
                  + ", " + EnemyBot.All.Count(b => b != null && b.nsfw) + " NSFW enemies left");
        }
        finally { GameSettings.NsfwModels = savedNsfw; }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EditorAutomation
{
    /// <summary>
    /// "smoketest": a cigarette in Play mode with the waves off (two drags, short pauses). The whole sequence runs in order: the pack
    /// (lid open, one shaken out), the lighter (flame with its light), lit by the first drag, the drags with the smoke breathed out
    /// and the wisp from the tip, burnt down, the butt flicked into the world. The gun keeps firing meanwhile (the hands are not
    /// needed); C during the drags throws it away early. Screenshots of the pack, the flame, the smoke.
    /// </summary>
    static IEnumerator SmokeTests()
    {
        var game = WaveSurvivalGame.Instance;
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        var t = Terrain.activeTerrain;
        var s = Object.FindFirstObjectByType<Smoking>();
        if (game == null || fp == null || t == null) throw new Exception("game, player or terrain missing");
        var inv = fp.GetComponent<WeaponInventory>();
        game.enabled = false;
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
        Vector3 c = game.zone.transform.position; c.y = t.SampleHeight(c) + t.transform.position.y;
        fp.Teleport(c + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.identity; fp.Pitch = 0f; inv.ResetSway();
        yield return Wait(0.3f);

        int vm = LayerMask.NameToLayer(WeaponSetup.ViewModelLayerName);
        Check(s != null && s.hand != null && s.pack != null && s.lighter != null && s.cigarette != null && s.gameObject.layer == vm
              && s.tipSmoke != null && s.tipSmoke.gameObject.layer == 0 && s.breath.gameObject.layer == 0 && !s.IsSmoking,
              "rig: hand, pack, lighter, cigarette on the view model layer, the smoke in the world");
        if (s == null) yield break;
        s.drags = 2; s.pause = new Vector2(0.6f, 0.8f);

        // ---- one cigarette from the pack to the butt; the gun fires meanwhile
        var seq = new List<Smoking.Phase>();
        bool lidOpen = false, slidOut = false, flame = false, light = false, glowLit = false, wisp = false, firedWhileLighting = false;
        float minLeft = 1f; int shots0 = inv.ShotsFired, weapon0 = inv.CurrentIndex;
        bool shotPack = false, shotFlame = false, shotSmoke = false, shotLips = false, shotFlick = false, fireDone = false;
        s.Toggle();
        float t0 = Time.time;
        while ((s.IsSmoking || seq.Count == 0) && Time.time < t0 + 40f)
        {
            if (seq.Count == 0 || seq[seq.Count - 1] != s.Current) seq.Add(s.Current);
            if (Quaternion.Angle(Quaternion.identity, s.packLid.localRotation) > 90f) lidOpen = true;
            if (s.packCigarette.gameObject.activeInHierarchy && s.packCigarette.localPosition.y > 0.084f + 0.02f) slidOut = true;
            if (s.FlameOn && s.flame.enabled) { flame = true; light |= s.flameLight.enabled && s.flameLight.intensity > 0.5f; }
            if (s.Lit && s.glow.enabled) glowLit = true;
            if (s.Current == Smoking.Phase.Smoking && !s.IsDrawing && s.tipSmoke.particleCount > 3) wisp = true;
            minLeft = Mathf.Min(minLeft, s.Left);

            // pictures (a stalled frame does not matter here), then a burst into the sky while the lighter is out
            if (!shotPack && lidOpen && slidOut && s.Current == Smoking.Phase.Pack) { ScreenCapture.CaptureScreenshot(ShotDir + "/smoke_pack.png"); shotPack = true; }
            if (!shotFlame && s.Current == Smoking.Phase.Lighting) { ScreenCapture.CaptureScreenshot(ShotDir + "/smoke_light.png"); shotFlame = true; }
            if (!shotSmoke && s.IsExhaling && s.breath.particleCount > 12) { ScreenCapture.CaptureScreenshot(ShotDir + "/smoke_breath.png"); shotSmoke = true; }
            if (!shotLips && s.Current == Smoking.Phase.Pack && s.cigarette.gameObject.activeSelf) { ScreenCapture.CaptureScreenshot(ShotDir + "/smoke_lips.png"); shotLips = true; }
            if (!shotFlick && s.Current == Smoking.Phase.Finishing && s.cigarette.parent == s.hand) { ScreenCapture.CaptureScreenshot(ShotDir + "/smoke_flick.png"); shotFlick = true; }
            if (!fireDone && s.Current == Smoking.Phase.Lighter && s.FlameOn)
            {
                fireDone = true;
                int before = inv.ShotsFired; fp.Pitch = -70f;
                inv.SetTestInput(true, false); yield return Wait(0.4f); inv.SetTestInput(false, false);
                firedWhileLighting = inv.ShotsFired > before; fp.Pitch = 0f;
                continue;
            }
            yield return null;
        }
        inv.ClearTestInput();
        if (seq.Count == 0 || seq[seq.Count - 1] != s.Current) seq.Add(s.Current);
        string order = string.Join(" > ", seq.Select(p => p.ToString()).ToArray());
        var want = new[] { Smoking.Phase.Pack, Smoking.Phase.Lighter, Smoking.Phase.Lighting, Smoking.Phase.Smoking, Smoking.Phase.Finishing, Smoking.Phase.None };
        Check(seq.SequenceEqual(want) && Time.time < t0 + 40f, "sequence: " + order + " in " + F3(Time.time - t0) + " s");
        Check(lidOpen && slidOut, "the pack: lid open " + lidOpen + ", one shaken out " + slidOut);
        Check(flame && light && glowLit, "the lighter: flame " + flame + ", its light " + light + "; the ember glows " + glowLit);
        Check(s.DragsDone == 2 && s.BreathPuffs > 40 && wisp, "drags " + s.DragsDone + " of 2, smoke breathed out " + s.BreathPuffs + " puffs (first one and after each drag), a wisp from the tip " + wisp);
        Check(minLeft < 0.2f, "burnt down: " + F3((1f - minLeft) * 100f) + " % of the tobacco");
        Check(firedWhileLighting && inv.CurrentIndex == weapon0 && inv.ShotsFired > shots0, "the gun fires while the lighter is out (" + (inv.ShotsFired - shots0) + " rounds), still in hand");

        // ---- the butt flies off into the world and lands
        var butt = s.LastButt;
        Vector3 eye = fp.cameraPivot.position;
        Vector3 b0 = butt != null ? butt.transform.position : Vector3.zero;
        yield return Wait(1.5f);
        bool flew = butt != null && butt.GetComponent<Rigidbody>() != null && Vector3.Distance(butt.transform.position, eye) > 1f && butt.layer == 2;
        Check(flew && !s.cigarette.gameObject.activeSelf, "the butt flicked away: " + (butt != null ? F3(Vector3.Distance(butt.transform.position, eye)) + " m off, fell " + F3(b0.y - butt.transform.position.y) + " m" : "MISSING") + ", none left in the mouth");

        // ---- C during the drags: thrown away early
        s.drags = 20; s.pause = new Vector2(3f, 3f);
        int finished = s.Finished; s.Toggle();
        t0 = Time.time;
        while (s.Current != Smoking.Phase.Smoking && Time.time < t0 + 20f) yield return null;
        yield return Wait(0.5f);
        float left = s.Left; s.Toggle(); float t1 = Time.time;
        while (s.IsSmoking && Time.time < t1 + 6f) yield return null;
        Check(!s.IsSmoking && s.Finished == finished + 1 && left > 0.6f && Time.time - t1 < 4f,
              "C again while smoking: thrown away after " + F3(Time.time - t1) + " s with " + F3(left * 100f) + " % left");
        yield return Wait(0.5f);
    }
}

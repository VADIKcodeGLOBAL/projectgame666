using System;
using System.Collections;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EditorAutomation
{
    /// <summary>
    /// "cannontest": the field cannon in Play mode with the waves off. Standing behind it offers it; aiming takes the view and puts
    /// the weapons away; a point 140 m off to the side is aimed at (traverse + ballistic elevation) and hit where the sight said;
    /// the ball follows the exact arc; bots inside the blast die, bots outside live; dust cloud and dust patch appear; reload; out of range;
    /// leaving gives everything back; pushing rolls it on the ground with the player behind, turns it, stops at an obstacle;
    /// five balls in the air: time of the cannonball update and garbage per frame.
    /// </summary>
    static IEnumerator CannonTests()
    {
        var game = WaveSurvivalGame.Instance;
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        var cannon = Object.FindFirstObjectByType<FieldCannon>();
        var t = Terrain.activeTerrain;
        if (game == null || fp == null || cannon == null || t == null) throw new Exception("game, player, cannon or terrain missing");
        var inv = fp.GetComponent<WeaponInventory>();
        var playerCam = fp.cameraPivot.GetComponent<Camera>();
        var sys = CannonballSystem.Get();
        game.enabled = false;
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
        Func<Vector3, Vector3> onGround = p => { p.y = t.SampleHeight(p) + t.transform.position.y; return p; };
        Vector3 home = cannon.transform.position; float homeYaw = cannon.transform.eulerAngles.y;
        yield return Wait(0.3f);

        // ---- standing behind it offers it
        fp.Teleport(onGround(cannon.transform.position - cannon.transform.forward * cannon.handleDistance) + Vector3.up * 0.3f);
        yield return Wait(0.3f);
        Check(FieldCannon.Nearby == cannon, "standing behind the cannon: offered (E aim / F push)");

        // ---- aiming takes the view and puts the weapons away
        cannon.EnterAim(fp); yield return null;
        Check(cannon.Current == FieldCannon.Mode.Aiming && cannon.gunnerCamera.enabled && !playerCam.enabled && !fp.enabled && inv.Holstered
              && (inv.viewCam == null || !inv.viewCam.enabled),
              "aiming: gunner camera on, player and view model cameras off, walking off, weapons away");

        Vector3 fwd = cannon.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 target = onGround(cannon.transform.position + Quaternion.Euler(0f, 25f, 0f) * fwd * 190f);   // the valley floor: a clear arc
        var near = new System.Collections.Generic.List<EnemyBot>(); var far = new System.Collections.Generic.List<EnemyBot>();
        for (int i = 0; i < 5; i++)
        {
            bool inside = i < 3;
            Vector3 p = onGround(target + Quaternion.Euler(0f, i * 72f, 0f) * Vector3.forward * (inside ? 2.5f : 11f)) + Vector3.up * 0.05f;
            var b = Object.Instantiate(game.botPrefab, p, Quaternion.identity);
            b.Init(fp.transform, 60f, 0f, 0f, 1f, Color.white);
            (inside ? near : far).Add(b); moveTemp.Add(b.gameObject);
        }
        cannon.SetTestAim(target);
        var recAim = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "FieldCannon.Aim");
        float t0 = Time.time; double aimMs = 0, aimMax = 0; int aimFrames = 0;
        while (Time.time < t0 + 6f && !cannon.OnTarget)
        {
            yield return null;
            if (recAim.Valid) { double ms = recAim.LastValue / 1e6; aimMs += ms; aimMax = Math.Max(aimMax, ms); aimFrames++; }
        }
        float settle = Time.time - t0;
        recAim.Dispose();
        Log("  aiming cost: " + (aimMs / Math.Max(1, aimFrames)).ToString("0.000") + " ms/frame on average, " + aimMax.ToString("0.000") + " ms at most (re-solves included)");
        yield return Wait(0.1f);
        ScreenCapture.CaptureScreenshot(ShotDir + "/cannon_aim.png");
        yield return null;
        Vector3 predicted = cannon.PredictedImpact;
        Check(cannon.OnTarget && cannon.InRange, "aim at a point 190 m away, 25 deg to the side: on target after " + F3(settle) + " s, elevation " + cannon.Elevation.ToString("0.0") + " deg, flight " + F3(cannon.FlightTime) + " s");
        Check(Vector3.Distance(predicted, target) < 2.5f, "sight: predicted landing " + F3(Vector3.Distance(predicted, target)) + " m from the point aimed at");

        // ---- the shot
        int impacts = sys.Impacts;
        cannon.TestFire(); yield return null;
        Check(cannon.Shots == 1 && !cannon.Loaded && sys.ActiveBalls >= 1, "fired: ball in the air, reloading");
        Vector3 p0 = sys.LastLaunchPosition, v0 = sys.LastLaunchVelocity; float worst = 0f; int samples = 0;
        t0 = Time.time;
        while (sys.Impacts == impacts && Time.time < t0 + 12f)
        {
            Vector3 pos; float tf;
            if (sys.TryGetLastBall(out pos, out tf))
            {
                Vector3 exact = p0 + v0 * tf + Vector3.down * (0.5f * sys.gravity * tf * tf);
                worst = Mathf.Max(worst, Vector3.Distance(pos, exact)); samples++;
            }
            yield return null;
        }
        float flew = Time.time - t0;
        Check(samples > 20 && worst < 0.001f, "ball on the exact arc: " + samples + " samples, largest deviation " + worst.ToString("0.000000") + " m, flew " + F3(flew) + " s");
        Check(sys.Impacts == impacts + 1 && Vector3.Distance(sys.LastImpact, predicted) < 1f, "landed " + F3(Vector3.Distance(sys.LastImpact, predicted)) + " m from where the sight said");
        yield return Wait(0.3f);
        ScreenCapture.CaptureScreenshot(ShotDir + "/cannon_hit.png");
        Vector3 hitAt = sys.LastImpact, side = Vector3.Cross(Vector3.up, (hitAt - cannon.transform.position).normalized);
        Shot("cannon_dust_cloud", hitAt - (hitAt - cannon.transform.position).normalized * 22f + side * 6f + Vector3.up * 7f, hitAt + Vector3.up * 1.5f, 50f, false, 0.2f);
        int deadNear = 0, aliveFar = 0;
        foreach (var b in near) if (b == null || b.IsDying) deadNear++;
        foreach (var b in far) if (b != null && !b.IsDying) aliveFar++;
        Check(deadNear == near.Count && aliveFar == far.Count, "blast (radius " + sys.radius + " m): " + deadNear + "/" + near.Count + " bots at 2.5 m dead, " + aliveFar + "/" + far.Count + " at 11 m alive");
        Check(sys.DustParticles > 10 && sys.DustPatchNear(sys.LastImpact, 1f), "dust: " + sys.DustParticles + " particles in the cloud, dust patch on the ground " + sys.DustPatchNear(sys.LastImpact, 1f));

        // ---- reload, out of range
        Check(!cannon.Fire(), "a second shot at once is refused (reloading " + F3(cannon.ReloadProgress) + ")");
        yield return Wait(3.5f);
        Shot("cannon_dust_patch", hitAt - (hitAt - cannon.transform.position).normalized * 14f + side * 4f + Vector3.up * 9f, hitAt, 50f, false, 0.2f);
        yield return Wait(cannon.reloadTime - 3.4f);
        Check(cannon.Loaded, "loaded again after " + cannon.reloadTime + " s");
        cannon.SetTestAim(cannon.transform.position + fwd * 2400f + Vector3.up * 50f);
        yield return Wait(1.6f);
        Check(!cannon.InRange && !cannon.Obstructed && cannon.Elevation > cannon.maxElevation - 0.5f, "a point 2.4 km away: out of range, barrel at " + cannon.Elevation.ToString("0.0") + " deg (max " + cannon.maxElevation + "), longest shot " + F3(cannon.Range) + " m");
        Vector3 hidden = onGround(cannon.transform.position + fwd * 30f);
        cannon.SetTestAim(hidden); yield return Wait(1.2f);
        Log("  a point 30 m away down the slope: in range " + cannon.InRange + ", not clear " + cannon.Obstructed + ", barrel " + cannon.Elevation.ToString("0.0") + " deg, lands " + F3(Vector3.Distance(cannon.PredictedImpact, hidden)) + " m from it");

        // ---- leaving gives everything back
        cannon.Release(); yield return null;
        Check(cannon.Current == FieldCannon.Mode.Idle && fp.enabled && !inv.Holstered && playerCam.enabled && !cannon.gunnerCamera.enabled && FieldCannon.Active == null
              && (inv.viewCam == null || inv.viewCam.enabled),
              "left the cannon: walking, weapons, player and view model cameras back");

        // ---- pushing
        cannon.Place(home, homeYaw, false);
        cannon.EnterPush(fp);
        cannon.SetTestPush(1f, 0f);
        Vector3 c0 = cannon.transform.position; t0 = Time.time;
        float worstGround = 0f, worstHandle = 0f;
        yield return Run(1.5f, () =>
        {
            Vector3 cp = cannon.transform.position;
            worstGround = Mathf.Max(worstGround, Mathf.Abs(cp.y - onGround(cp).y));
            worstHandle = Mathf.Max(worstHandle, Mathf.Abs(Horiz(fp.transform.position, cp) - cannon.handleDistance));
        });
        float moved = Horiz(cannon.transform.position, c0), want = cannon.pushSpeed * (Time.time - t0) - cannon.pushSpeed * cannon.pushSpeed / (2f * cannon.pushAccel);
        Check(Mathf.Abs(moved - want) < want * 0.15f && worstGround < 0.25f && worstHandle < 0.3f,
              "pushed 1.5 s: " + F3(moved) + " m (expected ~" + F3(want) + "), on the ground within " + F3(worstGround) + " m, player behind within " + F3(worstHandle) + " m");
        cannon.SetTestPush(0f, 0f); yield return Wait(0.8f);             // rolled to a stop first
        float y0 = cannon.transform.eulerAngles.y; t0 = Time.time;
        cannon.SetTestPush(0f, 1f);
        yield return Wait(1f);                                          // a slow editor tick can make it longer: the rate is what counts
        float secs = Time.time - t0, turned = Mathf.DeltaAngle(y0, cannon.transform.eulerAngles.y), wantTurn = cannon.turnSpeed * 0.7f * secs;
        Check(Mathf.Abs(turned - wantTurn) < wantTurn * 0.25f, "turned on the spot " + F3(secs) + " s: " + turned.ToString("0.0") + " deg (expected ~" + wantTurn.ToString("0.0") + ")");
        cannon.SetTestPush(0f, 0f); yield return Wait(0.2f);
        Vector3 f2 = cannon.transform.forward; f2.y = 0f; f2.Normalize();
        float frontReach = cannon.body.center.z + cannon.body.size.z * 0.5f;
        Vector3 wallAt = onGround(cannon.transform.position + f2 * (frontReach + 1.6f));
        var wall = Block("CannonWall", wallAt + Vector3.up * 1f, new Vector3(4f, 2f, 0.4f), Quaternion.LookRotation(f2));
        cannon.SetTestPush(1f, 0f);
        yield return Wait(2f);
        float gap = Vector3.Dot(wallAt - cannon.transform.position, f2) - 0.2f - frontReach;
        Check(cannon.PushBlocked && gap > -0.01f && gap < 0.8f, "obstacle ahead: stopped " + F3(gap) + " m before it, blocked " + cannon.PushBlocked);
        cannon.SetTestPush(0f, 0f); Object.Destroy(wall);
        cannon.Release(); yield return null;
        Check(fp.enabled && !inv.Holstered, "let go of the cannon: walking again");
        cannon.Place(home, homeYaw, false);
        yield return Wait(0.2f);

        // ---- five balls in the air: cost of the update, garbage
        var recUpd = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Cannonballs.Update");
        var recGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        double gcBase = 0; int nb = 0;
        yield return Run(0.5f, () => { if (recGc.Valid) { gcBase += recGc.LastValue; nb++; } });
        for (int i = 0; i < 5; i++) { cannon.ClearReload(); cannon.Fire(); yield return Wait(0.12f); }
        double upd = 0, gc = 0; int nf = 0, maxBalls = 0;
        yield return Run(1.2f, () => { if (recUpd.Valid) upd += recUpd.LastValue / 1e6; if (recGc.Valid) gc += recGc.LastValue; nf++; maxBalls = Mathf.Max(maxBalls, sys.ActiveBalls); });
        recUpd.Dispose(); recGc.Dispose();
        double extraKb = (gc / Math.Max(1, nf) - gcBase / Math.Max(1, nb)) / 1024.0;
        Check(maxBalls >= 4 && upd / Math.Max(1, nf) < 0.5, "5 balls fired: " + maxBalls + " in the air at once, cannonball update " + (upd / Math.Max(1, nf)).ToString("0.000") + " ms/frame, garbage vs before " + extraKb.ToString("0.0") + " KB/frame (editor)");
        yield return Wait(4f);

        // ---- a hit on the open summit, seen from close: the dust cloud, then the dust patch left on the ground
        Vector3 zc = onGround(game.zone.transform.position), boomAt = onGround(zc + (zc - cannon.transform.position).normalized * 5f);
        fp.Teleport(onGround(zc - (zc - cannon.transform.position).normalized * 6f) + Vector3.up * 0.3f);
        var hp = fp.GetComponent<PlayerHealth>(); hp.Heal(hp.maxHealth);
        sys.ExplodeAt(boomAt);
        Vector3 eyeAt = boomAt + (cannon.transform.position - boomAt).normalized * 16f + Vector3.up * 4f;
        yield return Wait(0.5f);
        Shot("cannon_boom", eyeAt, boomAt + Vector3.up * 1.5f, 55f, false, 0.2f);
        yield return Wait(4.5f);
        Shot("cannon_patch", eyeAt + Vector3.up * 4f, boomAt, 55f, false, 0.2f);
        Log("  close hit: dust particles " + sys.DustParticles + " after 5 s, patch " + sys.DustPatchNear(boomAt, 1f));
        fp.Teleport(onGround(game.zone.transform.position) + Vector3.up * 0.3f);
    }
}

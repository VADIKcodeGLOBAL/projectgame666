using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EditorAutomation
{
    static Stack<IEnumerator> moveTestRunner;
    static int moveFailures, moveChecks;
    static readonly List<GameObject> moveTemp = new List<GameObject>();
    static int savedFrameRate, savedVSync;

    /// <summary>
    /// "movetest": the movement suite in Play mode, with the wave game switched off. Slopes of several steepness levels
    /// (standing still: no drift), walking across a slope (no drift up or down), sprinting up and down (same speed, always on the ground),
    /// jump height at 30 / 60 / 144 fps, a running jump downhill, a wall at 45 degrees and a corner, a 0.3 m step (walked onto, camera eased)
    /// and a 0.6 m block (stops), a low ceiling, a bot in the way, ramps of 30 degrees (walkable) and 60 degrees (slides, cannot be climbed).
    /// Test geometry is built on the summit plateau and disappears with the Play session.
    /// </summary>
    static void MoveTick() { SuiteTick(MoveTests, "MOVETEST"); }

    static string suiteName = "MOVETEST";

    /// <summary>Runs a test suite written as an iterator (yield null = next frame; yield an IEnumerator = run it first).</summary>
    static void SuiteTick(Func<IEnumerator> suite, string name)
    {
        if (!EditorApplication.isPlaying || Time.timeSinceLevelLoad < 1.5f) return;
        if (moveTestRunner == null)
        {
            suiteName = name;
            moveTestRunner = new Stack<IEnumerator>(); moveTestRunner.Push(suite());
            moveFailures = 0; moveChecks = 0; moveTemp.Clear();
            savedFrameRate = Application.targetFrameRate; savedVSync = QualitySettings.vSyncCount;
        }
        if (moveTestRunner.Count == 0) return;
        try
        {
            var top = moveTestRunner.Peek();
            if (top.MoveNext()) { var sub = top.Current as IEnumerator; if (sub != null) moveTestRunner.Push(sub); }
            else moveTestRunner.Pop();
            if (moveTestRunner.Count == 0) FinishMoveTest(null);
        }
        catch (Exception e) { FinishMoveTest(e.ToString()); }
    }

    static void FinishMoveTest(string error)
    {
        Application.targetFrameRate = savedFrameRate; QualitySettings.vSyncCount = savedVSync;
        foreach (var g in moveTemp) if (g != null) Object.Destroy(g);
        moveTemp.Clear();
        if (moveTestRunner != null) moveTestRunner.Clear();
        if (error != null) Log(suiteName + "_FAILED: " + error);
        else Log(moveFailures == 0 ? suiteName + "_OK (" + moveChecks + " checks)" : suiteName + "_FAILED: " + moveFailures + " of " + moveChecks + " checks");
        Stop();
    }

    static void Check(bool ok, string what)
    {
        moveChecks++;
        if (!ok) moveFailures++;
        Log("  " + (ok ? "ok   " : "FAIL ") + what);
    }

    static IEnumerator Wait(float seconds) { float end = Time.time + seconds; while (Time.time < end) yield return null; }

    /// <summary>Calls perTick every tick for the given time (after the first tick).</summary>
    static IEnumerator Run(float seconds, Action perTick) { float end = Time.time + seconds; while (Time.time < end) { yield return null; perTick(); } }

    static string F3(float v) { return v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture); }
    static float Horiz(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    static GameObject Block(string name, Vector3 centre, Vector3 size, Quaternion rot)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "MoveTest_" + name;
        g.transform.SetPositionAndRotation(centre, rot); g.transform.localScale = size;
        moveTemp.Add(g); Physics.SyncTransforms();
        return g;
    }

    struct SlopeSpot { public Vector3 point, downhill; public float angle; }

    /// <summary>Clean terrain slopes near the target angles: no rock within 3 m, an even slope around, and (for runs) 12 m free uphill and downhill.</summary>
    static List<SlopeSpot> FindSlopes(Terrain t, Vector3 centre, float[] targets, bool needRunway)
    {
        var td = t.terrainData; Vector3 tp = t.transform.position;
        var best = new SlopeSpot[targets.Length]; var bestErr = new float[targets.Length];
        for (int i = 0; i < targets.Length; i++) bestErr[i] = 3f;
        Func<Vector3, float> steep = p => td.GetSteepness((p.x - tp.x) / td.size.x, (p.z - tp.z) / td.size.z);
        var near = new Collider[8];
        for (float r = 16f; r < 170f; r += 3f)
            for (float a = 0f; a < 360f; a += 360f / Mathf.Max(12f, r * 0.6f))
            {
                Vector3 p = centre + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                float nx = (p.x - tp.x) / td.size.x, nz = (p.z - tp.z) / td.size.z;
                if (nx < 0.05f || nx > 0.95f || nz < 0.05f || nz > 0.95f) continue;
                p.y = t.SampleHeight(p) + tp.y;
                float s = steep(p);
                int k = -1; float err = 99f;
                for (int i = 0; i < targets.Length; i++) { float e = Mathf.Abs(s - targets[i]); if (e < bestErr[i] && e < err) { err = e; k = i; } }
                if (k < 0) continue;
                Vector3 n = td.GetInterpolatedNormal(nx, nz);
                Vector3 down = new Vector3(n.x, 0f, n.z).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, down);
                bool even = true;                                      // the same slope 1.5 m around
                foreach (var o in new[] { down, -down, side, -side }) if (Mathf.Abs(steep(p + o * 1.5f) - s) > 4f) { even = false; break; }
                if (!even) continue;
                int c = Physics.OverlapSphereNonAlloc(p + Vector3.up * 1f, 3f, near, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                bool clear = true;
                for (int i = 0; i < c; i++) if (!(near[i] is TerrainCollider)) { clear = false; break; }
                if (!clear) continue;
                if (needRunway)
                {
                    bool free = true;
                    foreach (var dir in new[] { down, -down })
                    {
                        RaycastHit[] hs = Physics.SphereCastAll(p + Vector3.up * 1.2f, 0.5f, dir, 12f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                        foreach (var h in hs) if (!(h.collider is TerrainCollider)) { free = false; break; }
                    }
                    if (!free) continue;
                }
                bestErr[k] = err; best[k] = new SlopeSpot { point = p, downhill = down, angle = s };
            }
        var list = new List<SlopeSpot>();
        for (int i = 0; i < targets.Length; i++) if (bestErr[i] < 3f) list.Add(best[i]);
        return list;
    }

    static IEnumerator MoveTests()
    {
        var game = WaveSurvivalGame.Instance;
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        var t = Terrain.activeTerrain;
        if (game == null || fp == null || t == null || game.zone == null) throw new Exception("game, player, terrain or zone missing");
        var motor = fp.Motor;
        game.enabled = false;                                          // no waves: only the movement
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
        Vector3 c = game.zone.transform.position; c.y = t.SampleHeight(c) + t.transform.position.y;
        Log("  movetest: motor max slope " + motor.maxSlope + " deg, step " + motor.stepHeight + " m, snap " + motor.snapDistance + " m, walk " + fp.walkSpeed + ", sprint " + fp.sprintSpeed + ", jump " + fp.jumpHeight + " m");
        yield return Wait(0.3f);

        // ---- 1. standing still on slopes
        var spots = FindSlopes(t, c, new[] { 8f, 16f, 24f, 32f, 40f, 46f }, false);
        Check(spots.Count >= 3, "slope spots found: " + spots.Count + " (" + string.Join(", ", spots.ConvertAll(s => s.angle.ToString("0.0") + " deg").ToArray()) + ")");
        foreach (var s in spots)
        {
            fp.ClearTestInput(); fp.Teleport(s.point + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.LookRotation(s.downhill);
            yield return Wait(0.6f);
            Vector3 p0 = fp.transform.position; int ticks = 0, onGround = 0;
            yield return Run(3f, () => { ticks++; if (fp.IsGrounded) onGround++; });
            Vector3 p1 = fp.transform.position;
            float h = Horiz(p0, p1), v = Mathf.Abs(p1.y - p0.y);
            Check(h < 0.005f && v < 0.005f && onGround == ticks, "stand still 3 s on " + s.angle.ToString("0.0") + " deg: drift " + F3(h) + " m sideways, " + F3(v) + " m up/down, on the ground " + onGround + "/" + ticks);
        }

        // ---- 2-3. across, up and down a slope
        var runSpots = FindSlopes(t, c, new[] { 18f, 26f }, true);
        if (runSpots.Count == 0) Check(false, "no clean slope with a 12 m runway for the walking tests");
        foreach (var s in runSpots)
        {
            Vector3 across = Vector3.Cross(Vector3.up, s.downhill);
            fp.Teleport(s.point + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.LookRotation(across);
            yield return Wait(0.4f);
            fp.SetTestInput(Vector3.forward, false, false);
            yield return Wait(0.3f);                                   // up to speed
            Vector3 a0 = fp.transform.position; float ta = Time.time; int ticks = 0, onGround = 0;
            yield return Run(1.2f, () => { ticks++; if (fp.IsGrounded) onGround++; });
            Vector3 d = fp.transform.position - a0; d.y = 0f; float secs = Time.time - ta;
            float drift = Vector3.Dot(d, s.downhill), speed = d.magnitude / secs;
            Check(Mathf.Abs(drift) < 0.02f && Mathf.Abs(speed - fp.walkSpeed) < fp.walkSpeed * 0.03f && onGround == ticks,
                  "walk across " + s.angle.ToString("0.0") + " deg: drift " + F3(drift) + " m downhill over " + d.magnitude.ToString("0.0") + " m, speed " + F3(speed) + " (walk " + fp.walkSpeed + "), on the ground " + onGround + "/" + ticks);

            foreach (var up in new[] { true, false })
            {
                fp.ClearTestInput(); fp.Teleport(s.point + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.LookRotation(up ? -s.downhill : s.downhill);
                yield return Wait(0.4f);
                fp.SetTestInput(Vector3.forward, true, false);
                yield return Wait(0.3f);
                a0 = fp.transform.position; ta = Time.time; ticks = 0; onGround = 0;
                yield return Run(1.0f, () => { ticks++; if (fp.IsGrounded) onGround++; });
                Vector3 e = fp.transform.position - a0; float hd = new Vector2(e.x, e.z).magnitude; secs = Time.time - ta;
                speed = hd / secs;
                Check(Mathf.Abs(speed - fp.sprintSpeed) < fp.sprintSpeed * 0.03f && onGround == ticks,
                      "sprint " + (up ? "uphill" : "downhill") + " " + s.angle.ToString("0.0") + " deg: " + F3(speed) + " m/s over the ground (sprint " + fp.sprintSpeed + "), climbed " + F3(e.y) + " m, on the ground " + onGround + "/" + ticks);
            }

            // running jump downhill: leaves the ground, lands, keeps the speed
            fp.ClearTestInput(); fp.Teleport(s.point - s.downhill * 4f + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.LookRotation(s.downhill);
            yield return Wait(0.4f);
            fp.SetTestInput(Vector3.forward, true, false);
            yield return Wait(0.5f);
            fp.SetTestInput(Vector3.forward, true, true);
            bool left = false, landed = false; float tj = Time.time;
            while (Time.time < tj + 2f && !landed)
            {
                yield return null;
                if (!fp.IsGrounded && fp.VerticalVelocity > 1f) left = true;
                if (left && fp.IsGrounded) landed = true;
            }
            yield return Wait(0.2f);
            float after = fp.PlanarVelocity.magnitude;
            Check(left && landed && after > fp.sprintSpeed * 0.9f, "running jump downhill on " + s.angle.ToString("0.0") + " deg: took off " + left + ", landed " + landed + " after " + F3(Time.time - tj - 0.2f) + " s, speed after " + F3(after));
            fp.ClearTestInput();
        }

        // ---- 4. jump height at several frame rates (flat summit)
        foreach (int fps in new[] { 30, 60, 144 })
        {
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps;
            fp.ClearTestInput(); fp.Teleport(c + Vector3.up * 0.3f);
            yield return Wait(0.6f);
            float y0 = fp.transform.position.y, top = y0; int frames = Time.frameCount; float tj = Time.time;
            fp.SetTestInput(Vector3.zero, false, true);
            bool left = false, landed = false;
            while (Time.time < tj + 2f && !landed)
            {
                yield return null;
                top = Mathf.Max(top, fp.transform.position.y);
                if (!fp.IsGrounded) left = true; else if (left) landed = true;
            }
            float measured = (Time.frameCount - frames) / Mathf.Max(0.01f, Time.time - tj), apex = top - y0;
            Check(landed && Mathf.Abs(apex - fp.jumpHeight) < fp.jumpHeight * 0.03f && Mathf.Abs(fp.transform.position.y - y0) < 0.01f,
                  "jump at " + measured.ToString("0") + " fps (asked " + fps + "): apex " + F3(apex) + " m (jump " + fp.jumpHeight + "), back on the ground " + F3(fp.transform.position.y - y0));
        }
        Application.targetFrameRate = savedFrameRate; QualitySettings.vSyncCount = savedVSync;
        fp.ClearTestInput();

        // the summit plateau as a test area: x/z axes of the world, ground height at the centre
        float gy = c.y;
        Vector3 R = Vector3.right, Fw = Vector3.forward;
        float rad = motor.Radius;

        // ---- 5. a wall at 45 degrees: slide along it, never into it; a corner: stop without jitter
        var wall = Block("Wall", c + R * 3f + Fw * 0f + Vector3.up * (gy - c.y + 1.5f), new Vector3(0.4f, 3f, 8f), Quaternion.identity);
        float face = c.x + 3f - 0.2f;
        fp.Teleport(new Vector3(c.x + 1f, gy + 0.3f, c.z - 3f)); fp.transform.rotation = Quaternion.LookRotation((R + Fw).normalized);
        yield return Wait(0.4f);
        fp.SetTestInput(Vector3.forward, false, false);
        float worst = 0f; Vector3 s0 = Vector3.zero; float ts = 0f;
        float tw = Time.time;
        yield return Run(1.6f, () =>
        {
            worst = Mathf.Max(worst, fp.transform.position.x + rad - face);
            if (s0 == Vector3.zero && Time.time > tw + 0.8f) { s0 = fp.transform.position; ts = Time.time; }
        });
        float along = (fp.transform.position.z - s0.z) / Mathf.Max(0.01f, Time.time - ts), wantAlong = fp.walkSpeed * Mathf.Cos(45f * Mathf.Deg2Rad);
        Check(worst <= 0.002f && Mathf.Abs(along - wantAlong) < wantAlong * 0.1f,
              "wall at 45 deg: deepest into the wall " + F3(worst) + " m, slides along at " + F3(along) + " m/s (expected " + F3(wantAlong) + ")");
        var wall2 = Block("Wall2", new Vector3(c.x, gy + 1.5f, c.z + 4.2f), new Vector3(8f, 3f, 0.4f), Quaternion.identity);
        yield return Wait(1.6f);                                       // into the corner
        Vector3 k0 = fp.transform.position;
        yield return Wait(0.5f);
        float jitter = Vector3.Distance(fp.transform.position, k0);
        Check(jitter < 0.005f && fp.transform.position.z + rad <= c.z + 4.0f + 0.002f && fp.transform.position.x + rad <= face + 0.002f,
              "corner: moves " + F3(jitter) + " m in 0.5 s while pushing into it, gaps " + F3(face - fp.transform.position.x - rad) + " / " + F3(c.z + 4f - fp.transform.position.z - rad) + " m");
        fp.ClearTestInput(); Object.Destroy(wall); Object.Destroy(wall2);
        yield return Wait(0.1f); Physics.SyncTransforms();

        // ---- 6. 0.3 and 0.38 m steps are walked onto, the camera eased over them; a 0.6 m block stops you
        foreach (float hgt in new[] { 0.3f, 0.38f, 0.6f })                 // rolled over / stepped up (above the round bottom) / a wall
        {
            var box = Block("Step" + hgt, new Vector3(c.x, gy + hgt * 0.5f - 0.05f, c.z + 3f), new Vector3(3f, hgt + 0.1f, 2f), Quaternion.identity);
            float topY = gy + hgt;
            fp.Teleport(new Vector3(c.x, gy + 0.3f, c.z - 1f)); fp.transform.rotation = Quaternion.LookRotation(Fw);
            yield return Wait(0.4f);
            fp.SetTestInput(Vector3.forward, false, false);
            // the camera rises at a limited speed (m/s) instead of jumping with the step: measured as speed, so a slow frame
            // (a longer move at the same speed) does not count as a jump
            float camJump = 0f, lastCam = fp.cameraPivot.position.y, lastCamT = Time.time, stepped = 0f;
            yield return Run(0.9f, () =>
            {
                float cy = fp.cameraPivot.position.y, ct = Time.time;
                if (ct > lastCamT + 1e-4f) { camJump = Mathf.Max(camJump, Mathf.Abs(cy - lastCam) / (ct - lastCamT)); lastCam = cy; lastCamT = ct; }
                stepped += fp.Motor.LastStep;
            });
            Vector3 p = fp.transform.position;
            if (hgt < motor.stepHeight)
                Check(Mathf.Abs(p.y - topY) < 0.03f && p.z > c.z + 2f && fp.IsGrounded && camJump < 6f,
                      "step " + hgt + " m: on top " + F3(p.y - topY) + " m, stepped " + F3(stepped) + " m, fastest camera rise or fall " + F3(camJump) + " m/s (moving with the step: ~" + F3(hgt * 144f) + ")");
            else
                Check(Mathf.Abs(p.y - gy) < 0.03f && p.z + rad <= c.z + 2f + 0.002f && p.z + rad > c.z + 2f - 0.05f && fp.IsGrounded,
                      "block " + hgt + " m: stopped " + F3(c.z + 2f - p.z - rad) + " m before it, feet at " + F3(p.y - gy) + " m");
            fp.ClearTestInput(); Object.Destroy(box);
            yield return Wait(0.1f); Physics.SyncTransforms();
        }

        // ---- 7. a low ceiling cuts the jump
        var roof = Block("Ceiling", new Vector3(c.x, gy + 2.3f + 0.15f, c.z), new Vector3(4f, 0.3f, 4f), Quaternion.identity);
        fp.Teleport(new Vector3(c.x, gy + 0.3f, c.z)); yield return Wait(0.4f);
        float headTop = 0f; bool airborne = false, back = false; float tc = Time.time;
        fp.SetTestInput(Vector3.zero, false, true);
        while (Time.time < tc + 2f && !back)
        {
            yield return null;
            headTop = Mathf.Max(headTop, fp.transform.position.y + motor.Height);
            if (!fp.IsGrounded) airborne = true; else if (airborne) back = true;
        }
        Check(airborne && back && headTop <= gy + 2.3f + 0.002f, "ceiling at 2.3 m: head reached " + F3(headTop - gy) + " m, landed " + back);
        fp.ClearTestInput(); Object.Destroy(roof); yield return Wait(0.1f); Physics.SyncTransforms();

        // ---- 8. a bot in the way is not walked through
        var bot = Object.Instantiate(game.botPrefab, new Vector3(c.x, gy + 0.05f, c.z + 3f), Quaternion.LookRotation(-Fw));
        bot.Init(fp.transform, 1e7f, 0f, 0f, 1f, Color.white); moveTemp.Add(bot.gameObject);
        fp.Teleport(new Vector3(c.x + 0.1f, gy + 0.3f, c.z - 1f)); fp.transform.rotation = Quaternion.LookRotation(Fw);
        yield return Wait(0.5f);
        fp.SetTestInput(Vector3.forward, false, false);
        float closest = 99f;
        var bcc = bot.GetComponent<CharacterController>();
        yield return Run(1.5f, () => closest = Mathf.Min(closest, Horiz(fp.transform.position, bot.transform.position)));
        // a CharacterController's solid shape is its radius minus its skin width (the skin is where other controllers stop)
        float minGap = rad + (bcc.radius - bcc.skinWidth) * bot.transform.localScale.x;
        Check(closest >= minGap - 0.03f, "bot in the way: closest " + F3(closest) + " m between the axes (solid radii " + F3(minGap) + ")");
        fp.ClearTestInput(); bot.Die(false); yield return Wait(0.4f);

        // ---- 9. ramps: 30 deg is walked up, 60 deg cannot be climbed and slides you down
        foreach (float ang in new[] { 30f, 60f })
        {
            float L = 6f, th = 0.3f;
            Quaternion q = Quaternion.LookRotation(Fw) * Quaternion.Euler(-ang, 0f, 0f);
            Vector3 nearTop = new Vector3(c.x, gy - 0.02f, c.z + 1.5f);
            Vector3 centre = nearTop - q * (Vector3.up * th * 0.5f - Vector3.forward * L * 0.5f);
            var ramp = Block("Ramp" + ang, centre, new Vector3(3f, th, L), q);
            fp.Teleport(new Vector3(c.x, gy + 0.3f, c.z - 0.5f)); fp.transform.rotation = Quaternion.LookRotation(Fw);
            yield return Wait(0.4f);
            fp.SetTestInput(Vector3.forward, false, false);
            float rise = 0f; int rTicks = 0, rGround = 0;
            yield return Run(1.1f, () => { rise = Mathf.Max(rise, fp.transform.position.y - gy); rTicks++; if (fp.IsGrounded) rGround++; });   // the top is 5.2 m away
            if (ang < motor.maxSlope)
                Check(rise > 1.5f && rGround == rTicks, "ramp " + ang + " deg: walked up " + F3(rise) + " m, on the ground " + rGround + "/" + rTicks);
            else
            {
                Check(rise < 0.05f, "ramp " + ang + " deg: cannot be climbed, highest " + F3(rise) + " m");
                fp.ClearTestInput();
                Vector3 onRamp = centre + q * (Vector3.up * (th * 0.5f + 0.4f));   // above the middle of its surface
                fp.Teleport(onRamp); yield return Wait(0.05f);
                float y0 = fp.transform.position.y; int ticks = 0, onGround = 0;
                yield return Run(0.6f, () => { if (fp.transform.position.y > gy + 0.6f) { ticks++; if (fp.IsGrounded) onGround++; } });   // while on the ramp
                float slid = y0 - fp.transform.position.y;
                Check(slid > 0.5f && onGround == 0 && ticks > 10, "ramp " + ang + " deg: placed on it, slid down " + F3(slid) + " m in 0.6 s, 'on the ground' while on the ramp " + onGround + "/" + ticks);
            }
            fp.ClearTestInput(); Object.Destroy(ramp); yield return Wait(0.2f); Physics.SyncTransforms();
        }

        // ---- 10. standing still on the steepest slope at 30 fps
        if (spots.Count > 0)
        {
            var s = spots[spots.Count - 1];
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30;
            fp.Teleport(s.point + Vector3.up * 0.3f); yield return Wait(0.6f);
            Vector3 p0 = fp.transform.position;
            yield return Wait(2f);
            float drift = Vector3.Distance(p0, fp.transform.position);
            Check(drift < 0.005f, "stand still 2 s on " + s.angle.ToString("0.0") + " deg at 30 fps: drift " + F3(drift) + " m");
            Application.targetFrameRate = savedFrameRate; QualitySettings.vSyncCount = savedVSync;
        }
        // ---- 11. weapon sway: trails a turning view within its limit, settles back, the same at 30 and 144 fps;
        //          recoil and teleports do not swing it
        var inv = fp.GetComponent<WeaponInventory>();
        if (inv != null && inv.Current != null)
        {
            fp.ClearTestInput(); fp.Teleport(c + Vector3.up * 0.3f); fp.Pitch = 0f; inv.ResetSway();
            float limit = Mathf.Sqrt(2f) * inv.swayMaxAngle * Mathf.Sqrt(1f + inv.swayRoll * inv.swayRoll) + 0.1f;   // yaw + roll (+ pitch)
            var peaks = new float[2];
            int[] rates = { 30, 144 };
            for (int r = 0; r < 2; r++)
            {
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = rates[r];
                yield return Wait(0.8f);
                float rest = inv.SwayAngleNow, peak = 0f, lastT = Time.time;
                yield return Run(0.5f, () =>                               // turn right at 180 °/s
                {
                    float now = Time.time; fp.transform.Rotate(0f, 180f * (now - lastT), 0f); lastT = now;
                    peak = Mathf.Max(peak, inv.SwayAngleNow);
                });
                yield return Wait(1.2f);
                float settled = inv.SwayAngleNow;
                peaks[r] = peak;
                Check(rest < 0.05f && peak > 0.5f && peak <= limit && settled < 0.05f,
                      "sway at " + rates[r] + " fps: turning 180 deg/s tilts the gun " + F3(peak) + " deg (limit " + F3(limit) + "), back to " + F3(settled) + " deg after 1.2 s");
            }
            Application.targetFrameRate = savedFrameRate; QualitySettings.vSyncCount = savedVSync;
            Check(Mathf.Abs(peaks[0] - peaks[1]) <= Mathf.Max(peaks[0], peaks[1]) * 0.2f, "sway the same at 30 and 144 fps: " + F3(peaks[0]) + " / " + F3(peaks[1]) + " deg");

            yield return Wait(0.6f);
            fp.Teleport(c + Vector3.up * 0.3f + Vector3.right * 2f); fp.transform.Rotate(0f, 0f, 0f);
            float afterTp = 0f;
            yield return Run(0.3f, () => afterTp = Mathf.Max(afterTp, inv.SwayAngleNow));
            Check(afterTp < 0.05f, "teleport: the gun does not jolt (" + F3(afterTp) + " deg)");

            fp.Pitch = -60f; inv.ResetSway(); yield return Wait(0.5f);      // a burst into the sky: recoil lifts the view, the gun must not sway after it
            inv.SetTestInput(true, false);
            float recoilSway = 0f;
            yield return Run(0.6f, () => recoilSway = Mathf.Max(recoilSway, inv.SwayAngleNow));
            inv.ClearTestInput(); fp.Pitch = 0f; inv.ResetSway();
            Check(recoilSway < 0.05f, "recoil (" + inv.Current.displayName + " burst) does not sway the gun: " + F3(recoilSway) + " deg");
        }
        fp.Teleport(c + Vector3.up * 0.3f);
    }
}

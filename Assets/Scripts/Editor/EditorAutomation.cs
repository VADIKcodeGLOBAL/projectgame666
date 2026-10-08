using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Small remote control for the open editor: write one command into Logs/EditorAutomation.trigger
/// (refresh | koth | shots | playtest | perftest | weapons | weapons-view | weapons-side | weapons-info | gameplay | supplies-view | model &lt;Assets folder&gt;)
/// and the editor runs it. Progress goes to Logs/EditorAutomation.log. A command written during Play mode is dropped (only "stop" works there),
/// so nothing runs by surprise later. Off switch: Tools > Automation > Accept Trigger File Commands.
/// Also holds the shared Log and Shot helpers of the editor tools.
/// </summary>
[InitializeOnLoad]
public static partial class EditorAutomation
{
    const string LogPath = "Logs/EditorAutomation.log";
    const string TriggerPath = "Logs/EditorAutomation.trigger";
    public const string ShotDir = "Logs/shots";
    static double nextPoll;

    const string PlayKey = "EditorAutomation.playtest", PlayAtKey = "EditorAutomation.playtestAt", ModeKey = "EditorAutomation.mode";
    static int playPhase; static float playT;

    static EditorAutomation()
    {
        EditorApplication.update += Tick;
        if (SessionState.GetBool(PlayKey, false))
        {
            // only for the Play session started by the "playtest" command within the last two minutes, never for a session of the user
            double at; bool fresh = double.TryParse(SessionState.GetString(PlayAtKey, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out at)
                                    && (DateTime.UtcNow - DateTime.FromOADate(at)).TotalSeconds < 120.0;
            if (fresh) EditorApplication.update += PlayTick; else SessionState.SetBool(PlayKey, false);
        }
        EditorApplication.playModeStateChanged += st => { if (st == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(PlayKey, false); };
    }

    static void ArmPlaytest(string mode = "playtest")
    {
        SessionState.SetString(ModeKey, mode);
        SessionState.SetBool(PlayKey, true);
        SessionState.SetString(PlayAtKey, DateTime.UtcNow.ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        PlayerSettings.runInBackground = true;
        EditorApplication.isPlaying = true;
    }

    // playtest state
    static Vector3 runDir; static float tA, tB, landT, vRun, vAir, tl0, tl1, hp0, w1Health, w1Speed; static bool wasAir; static int spawnedAtWave2, moveIdx, ownBlocked, ownTicks, moveTicks, groundTicks; static float speedSum; static bool jumpedOk; static Vector3 startPos, endPos; static float[] hitRates = new float[3];

    static bool playTakeUpgrade;                                          // the playtest brings the upgrade put off back and takes it

    static string F(float v) { return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }

    /// <summary>The player 0.3 m above the terrain at (x, z), landed by the motor.</summary>
    static void Teleport(SimpleFirstPersonController fp, Vector3 p)
    {
        var t = Terrain.activeTerrain; if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y + 0.3f;
        fp.Teleport(p);
    }

    /// <summary>
    /// Automatic test in Play mode (short timings): sprint survives a jump, progress stops outside the circle and resumes inside,
    /// bots reach and hurt the player, wave 2 brings twice the batch and stronger bots. Saves Game view screenshots.
    /// </summary>
    static void PlayTick()
    {
        if (!EditorApplication.isPlaying) return;
        string mode = SessionState.GetString(ModeKey, "playtest");
        if (mode == "perftest") { PerfTick(); return; }
        if (mode == "movetest") { MoveTick(); return; }
        if (mode == "cannontest") { SuiteTick(CannonTests, "CANNONTEST"); return; }
        if (mode == "swordtest") { SuiteTick(SwordTests, "SWORDTEST"); return; }
        if (mode == "smoketest") { SuiteTick(SmokeTests, "SMOKETEST"); return; }
        if (mode == "upgradetest") { SuiteTick(UpgradeTests, "UPGRADETEST"); return; }
        float t = Time.timeSinceLevelLoad;
        if (t < 1.5f) return;
        var game = WaveSurvivalGame.Instance;
        var fp = UnityEngine.Object.FindFirstObjectByType<SimpleFirstPersonController>();
        if (game == null || fp == null || game.zone == null) { Log("PLAYTEST_FAILED: game, player or zone missing"); Stop(); return; }
        var hp = fp.GetComponent<PlayerHealth>();
        Vector3 c = game.zone.transform.position;
        if (t > 120f && playPhase != 9 && playPhase != 10) { Log("PLAYTEST_FAILED: timeout in phase " + playPhase); playPhase = 9; playT = t; return; }

        // a wave held brings the upgrade choice and pauses the game: put off at first (a random upgrade would change the
        // speeds, rates and reloads measured meanwhile), taken at the end
        var ups = UpgradeSystem.Instance;
        if (ups != null && ups.Current != null)
        {
            if (ups.Busy) return;
            var cards = new System.Text.StringBuilder();
            foreach (var m in ups.Current.cards) cards.Append((cards.Length > 0 ? ", " : "") + m.title + " (" + ModifierCatalog.Name(m.rarity) + ")");
            if (!playTakeUpgrade)
            {
                Log("  playtest upgrade after wave " + ups.Current.wave + ": " + cards + "; paused " + (Time.timeScale == 0f) + ", cursor " + Cursor.lockState + "; put off");
                if (Time.timeScale != 0f || Cursor.lockState == CursorLockMode.Locked) { Log("PLAYTEST_FAILED: the upgrade choice does not pause the game"); playPhase = 9; playT = t; }
                ups.Later();
            }
            else { Log("  playtest upgrade put off brought back (" + cards + "): takes the first"); ups.Choose(0); playTakeUpgrade = false; }
            return;
        }

        switch (playPhase)
        {
            case 0:
                game.waveDuration = 16f; game.batchInterval = 6f; game.intermission = 3f; playTakeUpgrade = false;
                runDir = Vector3.right;
                Teleport(fp, c - runDir * 10f); fp.transform.rotation = Quaternion.LookRotation(runDir);
                fp.SetTestInput(Vector3.forward, true, false);
                Log("  playtest: short timings (wave 16 s, batch every 6 s); sprint test starts");
                playPhase = 1; tA = t; break;
            case 1:
                if (t < tA + 0.8f) break;
                vRun = fp.PlanarVelocity.magnitude;
                {
                    var inv = fp.GetComponent<WeaponInventory>();
                    float want = GameSettings.Fov + inv.sprintFovKick * GameSettings.FovEffects;
                    Log("  playtest sprint fov: " + F(inv.cam.fieldOfView) + " (base " + F(GameSettings.Fov) + ", want " + F(want) + ", effects " + F(GameSettings.FovEffects) + ")");
                    if (Mathf.Abs(inv.cam.fieldOfView - want) > 0.5f) { Log("PLAYTEST_FAILED: the view does not widen while sprinting"); playPhase = 9; playT = t; break; }
                }
                fp.SetTestInput(Vector3.forward, true, true);                       // jump with Shift still held
                playPhase = 2; tB = t; wasAir = false; landT = -1f; vAir = 0f; break;
            case 2:
                if (!fp.IsGrounded && t > tB + 0.05f) { wasAir = true; vAir = Mathf.Max(vAir, fp.PlanarVelocity.magnitude); }
                if (wasAir && fp.IsGrounded && landT < 0f) landT = t;
                if (landT > 0f && t > landT + 0.35f)
                {
                    float vAfter = fp.PlanarVelocity.magnitude, need = fp.sprintSpeed * 0.9f;
                    Log("  playtest sprint: run " + F(vRun) + " m/s, in the air " + F(vAir) + ", after landing " + F(vAfter) + " (sprint " + F(fp.sprintSpeed) + ")");
                    if (vRun < need || vAir < need || vAfter < need) { Log("PLAYTEST_FAILED: sprint is lost around the jump"); playPhase = 9; playT = t; break; }
                    fp.ClearTestInput(); Teleport(fp, c + new Vector3(0.5f, 0f, 0.5f));
                    playPhase = 3;
                }
                else if (t > tB + 3f) { Log("PLAYTEST_FAILED: the jump did not land"); playPhase = 9; playT = t; }
                break;
            case 3:
                if (game.State != WaveSurvivalGame.GameState.Wave || game.TimeLeft > game.waveDuration - 2f) break;
                tl0 = game.TimeLeft; Teleport(fp, c + runDir * 40f); landT = 1f;
                playPhase = 4; playT = t; break;
            case 4:
                if (t > playT + 2.5f && landT >= 0f) { ScreenCapture.CaptureScreenshot(ShotDir + "/play_paused.png"); landT = -1f; }   // one frame before the check
                if (t < playT + 3f) break;
                tl1 = game.TimeLeft;
                Log("  playtest outside the circle for 3 s: timer " + F(tl0) + " -> " + F(tl1) + ", in zone " + game.InZone + ", bots alive " + game.Alive + ", spawned " + game.Spawned);
                if (game.InZone || Mathf.Abs(tl1 - tl0) > 0.05f) { Log("PLAYTEST_FAILED: progress did not stop outside the circle"); playPhase = 9; playT = t; break; }
                Teleport(fp, c + new Vector3(0.5f, 0f, 0.5f));
                playPhase = 5; playT = t; break;
            case 5:
                if (t < playT + 2f) break;
                Log("  playtest back in the circle for 2 s: timer " + F(tl1) + " -> " + F(game.TimeLeft));
                if (game.TimeLeft > tl1 - 1.5f && game.State == WaveSurvivalGame.GameState.Wave) { Log("PLAYTEST_FAILED: progress did not resume"); playPhase = 9; playT = t; break; }
                if (EnemyBot.All.Count > 0) { w1Health = EnemyBot.All[0].maxHealth; w1Speed = EnemyBot.All[0].speed; }
                int moved = 0;
                foreach (var b in EnemyBot.All)
                {
                    if (moved >= 3) break;
                    var bcc = b.GetComponent<CharacterController>(); bcc.enabled = false;
                    b.transform.position = fp.transform.position + Quaternion.Euler(0f, moved * 120f, 0f) * Vector3.forward * 6f + Vector3.up * 1.5f;
                    bcc.enabled = true; moved++;
                }
                hp0 = hp.Health; landT = -1f; playPhase = 6; playT = t; break;
            case 6:
                if (t > playT + 4.5f && landT < 0f) { ScreenCapture.CaptureScreenshot(ShotDir + "/play_fight.png"); landT = 1f; }
                if (t < playT + 5f) break;
                float near = 1e9f; foreach (var b in EnemyBot.All) near = Mathf.Min(near, Vector3.Distance(b.transform.position, fp.transform.position));
                var pg = fp.GetComponent<WeaponInventory>();
                Log("  playtest melee: nearest bot " + F(near) + " m, hp " + F(hp0) + " -> " + F(hp.Health) + ", kills " + game.Kills + ", rounds fired " + (pg != null ? pg.ShotsFired : -1) + ", cursor " + Cursor.lockState);
                if (hp.Health >= hp0 && hp.Health >= hp.maxHealth) { Log("PLAYTEST_FAILED: bots did not hurt the player"); playPhase = 9; playT = t; break; }
                for (int i = EnemyBot.All.Count - 1; i >= 0; i--)           // clear the melee test bots so the player survives to wave 2
                    if (Vector3.Distance(EnemyBot.All[i].transform.position, fp.transform.position) < 12f) EnemyBot.All[i].TakeDamage(1e6f);
                playPhase = 11; break;
            case 11:                                                       // weapons: the visible box is the hitbox, the rate of fire holds
            {
                var inv = fp.GetComponent<WeaponInventory>();
                EnemyBot tb = null; foreach (var b in EnemyBot.All) if (!b.IsDying && b.body != null) { tb = b; break; }
                if (inv == null || inv.weapons.Length < 4 || tb == null) { Log("PLAYTEST_FAILED: no weapons or no bot for the weapon test"); playPhase = 9; playT = t; break; }
                var bt = tb.body.transform; Vector3 fwd = bt.forward;
                Vector3 corner = bt.TransformPoint(new Vector3(0.45f, 0.45f, 0f)) - fwd * 12f, beside = bt.TransformPoint(new Vector3(0.56f, 0.45f, 0f)) - fwd * 12f;
                float d1 = 600f, d2 = 600f;
                // this bot only: with 20 bots around, another one can stand on the same line
                bool hitCorner = tb.RayHitsBody(corner, fwd, d1, out d1), hitBeside = tb.RayHitsBody(beside, fwd, d2, out d2);
                bool capsuleCorner = Physics.Raycast(corner, fwd, 30f, 1 << 2, QueryTriggerInteraction.Ignore);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 2000; i++) { float d = 600f; EnemyBot.RaycastBodies(fp.transform.position + Vector3.up * 1.6f, UnityEngine.Random.onUnitSphere, ref d); }
                double us = sw.Elapsed.TotalMilliseconds * 1000.0 / 2000.0;
                Log("  playtest gun hitbox: top corner of the box " + (hitCorner ? "hit" : "MISSED") + " (old capsule: " + (capsuleCorner ? "hit" : "missed") + "), 6 cm beside the box " + (hitBeside ? "HIT" : "missed") + "; bot test " + us.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " us per round with " + EnemyBot.All.Count + " bots");
                if (!hitCorner || hitBeside) { Log("PLAYTEST_FAILED: rounds do not follow the visible box"); playPhase = 9; playT = t; break; }
                fp.Pitch = -80f;                                           // into the sky: the bursts hurt nobody
                tl0 = inv.ShotsFired; tl1 = inv.CurrentGun.InMagazine; inv.SetTestInput(true, false);
                playPhase = 12; playT = t; landT = Time.frameCount; break;
            }
            case 12:                                                       // AK-47 held for 2 s: 20 rounds, 20 fewer in the magazine
            {
                if (t < playT + 2f) break;
                var inv = fp.GetComponent<WeaponInventory>(); var ak = inv.CurrentGun;
                float fired = inv.ShotsFired - tl0, secs = t - playT, fps = (Time.frameCount - landT) / secs, want = ak.roundsPerSecond * secs;
                Log("  playtest " + ak.displayName + " rate: " + fired + " rounds in " + F(secs) + " s at " + F(fps) + " fps (want " + F(want) + "), magazine " + tl1 + " -> " + ak.InMagazine);
                if (Mathf.Abs(fired - want) > 1.5f || tl1 - ak.InMagazine != fired) { Log("PLAYTEST_FAILED: rate of fire or ammo count is off"); playPhase = 9; playT = t; break; }
                Log("  playtest recoil after " + fired + " rounds: strength " + F(ak.RecoilStrength) + ", kick " + F(ak.LastKick.y) + " deg up and " + F(ak.LastKick.x) + " aside (first round " + F(ak.recoil) + ", full x" + F(ak.recoilGrowth) + ")");
                if (ak.RecoilStrength < 0.99f || ak.LastKick.y < ak.recoil * (1f + (ak.recoilGrowth - 1f) * 0.6f)) { Log("PLAYTEST_FAILED: the recoil does not grow through a burst"); playPhase = 9; playT = t; break; }
                // view model: own layer and camera, the player camera does not draw it, the hands stay on the grips of a kicking gun
                int vm = LayerMask.NameToLayer(WeaponSetup.ViewModelLayerName); var arms = ak.GetComponent<ViewModelArms>();
                float gap = arms != null ? arms.GripGap() : -1f;
                Log("  playtest view model: layer " + vm + ", camera " + (inv.viewCam != null ? "fov " + F(inv.viewCam.fieldOfView) + " near " + F(inv.viewCam.nearClipPlane) : "MISSING")
                    + ", player camera draws it " + (vm >= 0 && (inv.cam.cullingMask & (1 << vm)) != 0) + ", hands off the grips " + F(gap * 1000f) + " mm while firing");
                if (vm < 0 || inv.viewCam == null || (inv.cam.cullingMask & (1 << vm)) != 0 || (inv.viewCam.cullingMask & (1 << vm)) == 0 || ak.gameObject.layer != vm
                    || arms == null || gap < 0f || gap > 0.001f) { Log("PLAYTEST_FAILED: the weapon is not a view model with the hands on it"); playPhase = 9; playT = t; break; }
                tl1 = ak.Reserve; playPhase = 13; playT = t; break;        // keep holding: the magazine runs dry and reloads by itself
            }
            case 13:
            {
                var inv = fp.GetComponent<WeaponInventory>(); var ak = inv.CurrentGun;
                if (ak.IsReloading) { inv.SetTestInput(false, false); hp0 = t; playPhase = 14; break; }
                if (t > playT + 3f) { Log("PLAYTEST_FAILED: an empty magazine did not start a reload (in magazine " + ak.InMagazine + ")"); playPhase = 9; playT = t; }
                break;
            }
            case 14:
            {
                var inv = fp.GetComponent<WeaponInventory>(); var ak = inv.CurrentGun;
                if (ak.IsReloading && ak.ReloadProgress > 0.45f && landT >= 0f) { fp.Pitch = 0f; ScreenCapture.CaptureScreenshot(ShotDir + "/play_reload.png"); landT = -1f; }   // the gun turned over, the hands on it
                if (ak.IsReloading) { if (t > hp0 + ak.reloadTime + 1f) { Log("PLAYTEST_FAILED: the reload never ends"); playPhase = 9; playT = t; } break; }
                Log("  playtest reload: " + F(t - hp0) + " s (set " + F(ak.reloadTime) + "), magazine " + ak.InMagazine + "/" + ak.magazineSize + ", spare rounds " + tl1 + " -> " + ak.Reserve + " (magazines " + ak.Magazines + ")");
                if (ak.InMagazine != ak.magazineSize || tl1 - ak.Reserve != ak.magazineSize) { Log("PLAYTEST_FAILED: the reload did not move one magazine"); playPhase = 9; playT = t; break; }
                if (ak.RecoilStrength > 0f) { Log("PLAYTEST_FAILED: the recoil did not settle during the reload (strength " + F(ak.RecoilStrength) + ")"); playPhase = 9; playT = t; break; }
                tl1 = ak.Magazines;
                SupplyPickup.Spawn(game.ammoPickupPrefab, fp.transform.position + fp.transform.forward * 0.5f);
                playPhase = 15; playT = t; break;
            }
            case 15:                                                       // walk into a magazine, then a medkit and a syringe
            {
                if (t < playT + 0.6f) break;
                var inv = fp.GetComponent<WeaponInventory>(); var ak = inv.CurrentGun;
                Log("  playtest pickup: magazines " + tl1 + " -> " + ak.Magazines + " (" + SupplyPickup.LastMessage + "), pickup prefab " + (game.ammoPickupPrefab != null));
                if (ak.Magazines != tl1 + 1) { Log("PLAYTEST_FAILED: the magazine was not picked up"); playPhase = 9; playT = t; break; }
                if (game.medkitPrefab == null || game.speedPrefab == null) { Log("PLAYTEST_FAILED: medkit or syringe prefab missing"); playPhase = 9; playT = t; break; }
                if (hp.Health > 60f) hp.TakeDamage(hp.Health - 50f);           // room for the medkit
                hp0 = hp.Health; fp.ClearSpeedBoost();
                SupplyPickup.Spawn(game.medkitPrefab, fp.transform.position + fp.transform.right * 0.5f);
                SupplyPickup.Spawn(game.speedPrefab, fp.transform.position - fp.transform.right * 0.5f);
                playPhase = 24; playT = t; break;
            }
            case 24:
            {
                if (t < playT + 0.6f) break;
                var inv = fp.GetComponent<WeaponInventory>();
                float want = Mathf.Min(hp.maxHealth, hp0 + game.medkitPrefab.heal);
                Log("  playtest medkit: hp " + F(hp0) + " -> " + F(hp.Health) + " (want " + F(want) + "); syringe: speed x" + F(fp.SpeedMultiplier) + " for " + F(fp.BoostTimeLeft) + " s more");
                if (Mathf.Abs(hp.Health - want) > 0.5f || fp.SpeedMultiplier <= 1f || fp.BoostTimeLeft <= 0f) { Log("PLAYTEST_FAILED: medkit or syringe did nothing"); playPhase = 9; playT = t; break; }
                fp.ClearSpeedBoost(); hp.Heal(hp.maxHealth);                   // the rest of the test at full health and normal speed
                inv.Select(3); playPhase = 16; playT = t; break;
            }
            case 16:                                                       // sniper: the scope comes up and zooms in
            {
                var inv = fp.GetComponent<WeaponInventory>();
                if (inv.IsSwitching) { if (t > playT + 2f) { Log("PLAYTEST_FAILED: weapon switch hangs"); playPhase = 9; playT = t; } break; }
                fp.Pitch = 0f; inv.SetTestInput(false, true); playPhase = 17; playT = t; landT = -1f; break;
            }
            case 17:
            {
                var inv = fp.GetComponent<WeaponInventory>();
                if (t > playT + 0.5f && landT < 0f) { ScreenCapture.CaptureScreenshot(ShotDir + "/play_scope.png"); landT = 1f; }
                if (t < playT + 0.6f) break;
                Log("  playtest scope: " + inv.Current.displayName + ", scoped " + inv.IsScoped + ", fov " + F(inv.cam.fieldOfView) + " (scope " + F(inv.Current.scopeFov) + "), look scale " + F(fp.lookScale));
                if (!inv.IsScoped || Mathf.Abs(inv.cam.fieldOfView - inv.Current.scopeFov) > 0.5f) { Log("PLAYTEST_FAILED: no zoom through the scope"); playPhase = 9; playT = t; break; }
                inv.SetTestInput(false, false); inv.Select(0); playPhase = 18; playT = t; break;
            }
            case 18:                                                       // AK-47 again: a burst at the horizon for the muzzle fire picture
            {
                var inv = fp.GetComponent<WeaponInventory>();
                if (inv.IsSwitching || inv.Aim > 0f) { if (t > playT + 2f) { Log("PLAYTEST_FAILED: switch back hangs"); playPhase = 9; playT = t; } break; }
                Log("  playtest switch back: fov " + F(inv.cam.fieldOfView) + ", weapon " + inv.Current.displayName);
                inv.SetTestInput(true, false); playPhase = 19; playT = t; landT = -1f; break;
            }
            case 19:
            {
                var inv = fp.GetComponent<WeaponInventory>();
                if (t > playT + 0.25f && landT < 0f) { ScreenCapture.CaptureScreenshot(ShotDir + "/play_fire.png"); landT = 1f; }
                if (t < playT + 0.4f) break;
                inv.ClearTestInput(); fp.Pitch = 0f;
                // a target bot that stands still 14 m ahead, for hits while standing, sprinting backwards and walking backwards
                Teleport(fp, c - runDir * 2f); fp.transform.rotation = Quaternion.LookRotation(runDir);
                var tgt = UnityEngine.Object.Instantiate(game.botPrefab, c + runDir * 12f, Quaternion.LookRotation(-runDir));
                tgt.Init(fp.transform, 1e7f, 0f, 0f, 1f, Color.white); tgt.name = "TestTarget";
                moveIdx = 0; landT = -1f; playPhase = 20; playT = t; break;
            }
            case 20:                                                       // aim at the target every tick; 1.5 s per movement
            {
                var inv = fp.GetComponent<WeaponInventory>();
                var tgt = GameObject.Find("TestTarget"); EnemyBot tb = tgt != null ? tgt.GetComponent<EnemyBot>() : null;
                if (tb == null) { Log("PLAYTEST_FAILED: test target lost"); playPhase = 9; playT = t; break; }
                Vector3 d = tb.body.transform.position - inv.cam.transform.position;
                fp.transform.rotation = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z));
                fp.Pitch = -Mathf.Asin(d.y / d.magnitude) * Mathf.Rad2Deg;
                if (t < playT + 0.3f) break;                               // settle, then measure
                if (landT < 0f)
                {
                    Vector3[] moves = { Vector3.zero, Vector3.back, Vector3.back };
                    bool[] sprint = { false, true, false };
                    fp.SetTestInput(moves[moveIdx], sprint[moveIdx], false); inv.SetTestInput(true, false);
                    inv.CurrentGun.FillMagazine();                            // 30 rounds: no reload inside the 1.5 s run
                    tl0 = inv.ShotsFired; tl1 = inv.Hits; landT = t; break;
                }
                if (t < landT + 1.5f)
                {
                    RaycastHit own;                                        // how often the player's own capsule is in front of the camera
                    ownTicks++;
                    if (Physics.Raycast(inv.cam.transform.position, d.normalized, out own, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && own.collider.gameObject == fp.gameObject) ownBlocked++;
                    break;
                }
                string[] names = { "standing", "sprinting backwards", "walking backwards" };
                float shots = inv.ShotsFired - tl0, hits = inv.Hits - tl1;
                Log("  playtest hits " + names[moveIdx] + ": " + hits + " / " + shots + " at " + F(fp.PlanarVelocity.magnitude) + " m/s, target " + F(d.magnitude) + " m; own capsule in front of the camera " + ownBlocked + " of " + ownTicks + " ticks");
                ownBlocked = 0; ownTicks = 0;
                hitRates[moveIdx] = shots > 0 ? hits / shots : 0f;
                inv.SetTestInput(false, false); fp.ClearTestInput(); landT = -1f;
                if (++moveIdx < 3) { Teleport(fp, c - runDir * 2f); playT = t; break; }
                tb.Die(false); inv.ClearTestInput(); fp.Pitch = 0f; Teleport(fp, c + new Vector3(0.5f, 0f, 0.5f));
                if (Mathf.Min(hitRates[0], Mathf.Min(hitRates[1], hitRates[2])) < 0.9f) { Log("PLAYTEST_FAILED: rounds miss a target in the crosshair while moving"); playPhase = 9; playT = t; break; }
                playPhase = 21; break;
            }
            case 21:                                                       // movement: sprint down the hill side, then up it
            {
                fp.ClearSpeedBoost();
                for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);   // a bot in the path would block a run at random
                Teleport(fp, c + runDir * 16f); fp.transform.rotation = Quaternion.LookRotation(runDir);
                fp.SetTestInput(Vector3.forward, true, false);
                moveTicks = 0; groundTicks = 0; speedSum = 0f; jumpedOk = false; startPos = fp.transform.position;
                playPhase = 22; playT = t; landT = -1f; break;
            }
            case 22:
            {
                if (landT < 0f && t > playT + 0.3f)
                {
                    if (moveTicks == 0) { startPos = fp.transform.position; tl1 = t; }  // measured after the speed-up
                    moveTicks++; if (fp.IsGrounded) groundTicks++;
                }
                if (landT < 0f && t > playT + 1.3f)                        // jump on the way down
                {
                    endPos = fp.transform.position; speedSum = t - tl1;
                    fp.SetTestInput(Vector3.forward, true, true); landT = t; break;
                }
                if (landT > 0f && !fp.IsGrounded && fp.VerticalVelocity > 1f) jumpedOk = true;
                if (landT < 0f || t < landT + 0.6f) break;
                float horiz = Vector2.Distance(new Vector2(startPos.x, startPos.z), new Vector2(endPos.x, endPos.z));
                float grade = (startPos.y - endPos.y) / Mathf.Max(0.1f, horiz) * 100f, onGround = groundTicks / (float)Mathf.Max(1, moveTicks), v = horiz / Mathf.Max(0.01f, speedSum);
                Log("  playtest downhill: grade ~" + F(grade) + " %, on the ground " + F(onGround * 100f) + " % of ticks, speed over the ground " + F(v) + " m/s (sprint " + F(fp.sprintSpeed) + "), jump " + (jumpedOk ? "done" : "MISSING"));
                if (onGround < 0.95f || v < fp.sprintSpeed * 0.93f || v > fp.sprintSpeed * 1.05f || !jumpedOk) { Log("PLAYTEST_FAILED: running downhill is not the same as on the flat"); playPhase = 9; playT = t; break; }
                Teleport(fp, c + runDir * 34f); fp.transform.rotation = Quaternion.LookRotation(-runDir);
                fp.SetTestInput(Vector3.forward, true, false);
                moveTicks = 0; speedSum = 0f; startPos = fp.transform.position;
                playPhase = 23; playT = t; break;
            }
            case 23:
            {
                if (t > playT + 0.3f && moveTicks == 0) { startPos = fp.transform.position; tl1 = t; moveTicks = 1; }
                if (t < playT + 1.3f) break;
                float horiz = Vector2.Distance(new Vector2(startPos.x, startPos.z), new Vector2(fp.transform.position.x, fp.transform.position.z));
                float v = horiz / Mathf.Max(0.01f, t - tl1), grade = (fp.transform.position.y - startPos.y) / Mathf.Max(0.1f, horiz) * 100f;
                Log("  playtest uphill: grade ~" + F(grade) + " %, speed over the ground " + F(v) + " m/s (" + F(horiz) + " m in " + F(t - tl1) + " s), planar velocity " + F(fp.PlanarVelocity.magnitude));
                fp.ClearTestInput(); Teleport(fp, c + new Vector3(0.5f, 0f, 0.5f));
                if (v < fp.sprintSpeed * 0.93f) { Log("PLAYTEST_FAILED: running uphill is slower than on the flat"); playPhase = 9; playT = t; break; }
                playPhase = 7; break;
            }
            case 7:
                if (game.Wave < 2 || game.State != WaveSurvivalGame.GameState.Wave) break;
                spawnedAtWave2 = game.Spawned; playPhase = 8; playT = t; break;
            case 8:
                if (game.Spawned < spawnedAtWave2 + 3) break;
                var nb = EnemyBot.All[EnemyBot.All.Count - 1];
                Log("  playtest wave 2: batch " + game.BatchSize + " (wave 1: " + game.mobsPerBatch + "), new bot hp " + F(nb.maxHealth) + " (was " + F(w1Health) + "), speed " + F(nb.speed) + " (was " + F(w1Speed) + "), kills " + game.Kills + ", player hp " + F(hp.Health));
                ScreenCapture.CaptureScreenshot(ShotDir + "/play_wave2.png");
                if (!(game.BatchSize == game.mobsPerBatch * 2 && nb.maxHealth > w1Health && nb.speed > w1Speed)) { Log("PLAYTEST_FAILED: wave 2 is not stronger"); playPhase = 9; playT = t; break; }
                if (ups == null || ups.Offered < 1 || ups.Waiting.Count < 1) { Log("PLAYTEST_FAILED: no upgrade was offered after wave 1 (or it was not put off)"); playPhase = 9; playT = t; break; }
                playTakeUpgrade = true; ups.OpenWaiting(); playPhase = 27; break;
            case 27:                                                       // the upgrade put off: brought back and taken
            {
                if (UpgradeSystem.IsChoosing || ups == null || ups.taken.Count < 1) break;
                Log("  playtest upgrade taken: " + ups.taken[0] + ", the game runs again (time scale " + Time.timeScale + ")");
                var sm = UnityEngine.Object.FindFirstObjectByType<SettingsMenu>();
                if (sm == null) { Log("PLAYTEST_FAILED: no settings menu"); playPhase = 9; playT = t; break; }
                sm.Open(); playPhase = 25; playT = Time.realtimeSinceStartup; break;
            }
            case 25:                                                      // settings menu: pauses the game, shows, closes
            {
                if (Time.realtimeSinceStartup < playT + 0.5f) break;
                ScreenCapture.CaptureScreenshot(ShotDir + "/play_settings.png");   // taken at the end of this frame, with the menu up
                playPhase = 26; playT = Time.realtimeSinceStartup; break;
            }
            case 26:
            {
                if (Time.realtimeSinceStartup < playT + 0.4f) break;
                var menu = UnityEngine.Object.FindFirstObjectByType<SettingsMenu>();
                bool paused = Time.timeScale == 0f && SettingsMenu.IsOpen && Cursor.lockState != CursorLockMode.Locked;
                menu.Close();
                Log("  playtest settings menu: paused " + paused + ", resumed " + (Time.timeScale == 1f && !SettingsMenu.IsOpen) + ", fov " + F(GameSettings.Fov) + ", fov effects " + F(GameSettings.FovEffects) + ", mouse " + F(GameSettings.MouseSensitivity) + ", volumes " + F(GameSettings.MasterVolume) + "/" + F(GameSettings.MusicVolume) + "/" + F(GameSettings.ShotVolume) + ", music tracks " + menu.music.TrackCount);
                Log(paused && Time.timeScale == 1f ? "PLAYTEST_OK" : "PLAYTEST_FAILED: the settings menu does not pause and resume");
                playPhase = 9; playT = t; break;
            }
            case 9:
                if (t > playT + 1.5f) { playPhase = 10; Stop(); }
                break;
        }
    }

    static void Stop()
    {
        SessionState.SetBool(PlayKey, false);
        EditorApplication.update -= PlayTick;
        EditorApplication.isPlaying = false;
    }

    public static void Log(string s)
    {
        try
        {
            Directory.CreateDirectory("Logs");
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > MaxLogBytes) { File.Copy(LogPath, LogPath + ".old", true); File.Delete(LogPath); }   // keep the log from growing forever
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss ") + s + "\n");
        }
        catch { }
        Debug.Log("[Tools] " + s);
    }

    // ---- the trigger file is a local back door into the editor: it can be switched off, and what it says is checked
    const string EnabledPref = "EditorAutomation.enabled", MenuPath = "Tools/Automation/Accept Trigger File Commands";
    const int MaxTriggerBytes = 512;
    const long MaxLogBytes = 2 * 1024 * 1024;

    static bool Enabled { get { return EditorPrefs.GetBool(EnabledPref, true); } }

    [MenuItem(MenuPath)]
    static void ToggleEnabled() { EditorPrefs.SetBool(EnabledPref, !Enabled); Debug.Log("[Tools] trigger file commands " + (Enabled ? "on" : "off")); }
    [MenuItem(MenuPath, true)]
    static bool ToggleEnabledCheck() { Menu.SetChecked(MenuPath, Enabled); return true; }

    /// <summary>The command in the trigger file (deleted after reading); null if there is none, it is too big or it cannot be read yet.</summary>
    static string ReadTrigger()
    {
        try
        {
            var fi = new FileInfo(TriggerPath);
            if (!fi.Exists) return null;
            if (fi.Length > MaxTriggerBytes) { File.Delete(TriggerPath); Log("trigger ignored: " + fi.Length + " bytes"); return null; }
            string cmd = File.ReadAllText(TriggerPath).Trim();
            File.Delete(TriggerPath);
            return cmd;
        }
        catch (IOException) { return null; }                         // still being written: next poll
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Printable ASCII only, at most 120 characters: the command goes into the log as it is.</summary>
    static string Clean(string s)
    {
        var sb = new System.Text.StringBuilder(Math.Min(s.Length, 120));
        foreach (char ch in s) { if (sb.Length >= 120) break; sb.Append(ch >= ' ' && ch < 127 ? ch : '?'); }
        return sb.ToString();
    }

    /// <summary>"model" takes an existing folder inside Assets only: no absolute paths, no "..".</summary>
    static bool IsAssetsFolder(string dir)
    {
        dir = dir.Replace('\\', '/').TrimEnd('/');
        return dir.StartsWith("Assets/") && !dir.Contains("..") && !dir.Contains(":") && AssetDatabase.IsValidFolder(dir);
    }

    static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < nextPoll) return;
        nextPoll = now + 0.5;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !Enabled) return;
        if (!File.Exists(TriggerPath)) return;
        string cmd = ReadTrigger();
        if (cmd == null) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)            // in Play mode only "stop" is accepted
        {
            if (cmd == "stop") { Log("command: stop"); Stop(); } else Log("ignored in Play mode: " + Clean(cmd));
            return;
        }
        Run(cmd);
    }

    static void Run(string cmd)
    {
        Log("command: " + Clean(cmd));
        if (cmd.StartsWith("model ") && !IsAssetsFolder(cmd.Substring(6).Trim())) { Log("ERROR model: the folder must exist inside Assets/"); return; }
        try
        {
            if (cmd == "refresh") { AssetDatabase.Refresh(); Log("REFRESH_OK"); }
            else if (cmd == "koth") KothMapGenerator.Generate(KothMapGenerator.LoadOrCreateSettings());
            else if (cmd == "shots") { KothMapGenerator.CheckShots(); Log("SHOTS_OK"); }
            else if (cmd == "playtest") { playPhase = 0; ArmPlaytest(); }
            else if (cmd == "perftest") { perfPhase = 0; ArmPlaytest("perftest"); }
            else if (cmd == "weapons-info") WeaponSetup.Info();
            else if (cmd == "weapons") WeaponSetup.InstallInOpenScene();
            else if (cmd == "weapons-view") WeaponSetup.ViewShots();
            else if (cmd == "weapons-side") WeaponSetup.SideShots();
            else if (cmd == "gameplay") SupplySetup.InstallInOpenScene();
            else if (cmd == "supplies-view") SupplySetup.ViewShots();
            else if (cmd == "hud") HudSetup.InstallInOpenScene();
            else if (cmd == "player") PlayerSetup.InstallInOpenScene();
            else if (cmd == "movetest") { moveTestRunner = null; ArmPlaytest("movetest"); }
            else if (cmd == "cannontest") { moveTestRunner = null; ArmPlaytest("cannontest"); }
            else if (cmd == "swordtest") { moveTestRunner = null; ArmPlaytest("swordtest"); }
            else if (cmd == "smoketest") { moveTestRunner = null; ArmPlaytest("smoketest"); }
            else if (cmd == "upgradetest") { moveTestRunner = null; ArmPlaytest("upgradetest"); }
            else if (cmd == "cannon") CannonSetup.InstallInOpenScene();
            else if (cmd == "cannon-view") CannonSetup.ViewShots();
            else if (cmd == "zone") KothMapGenerator.RebuildZoneInOpenScene();
            else if (cmd.StartsWith("model ")) ModelMaterialSetup.Setup(cmd.Substring(6).Trim());
            else Log("unknown command");
        }
        catch (Exception e) { Log("ERROR " + e); EditorUtility.ClearProgressBar(); }
    }

    /// <summary>-executeMethod entry: queue the map generation, it starts once the editor is idle.</summary>
    public static void GenerateKothFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText(TriggerPath, "koth");
    }

    /// <summary>Renders the open scene from a point into Logs/shots/name.png (used to check results without touching the scene).</summary>
    public static void Shot(string name, Vector3 pos, Vector3 target, float fov, bool edgeBlur = false, float near = 0.2f)
    {
        const int W = 1600, H = 900;
        var go = new GameObject("TMP_ShotCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.nearClipPlane = near; cam.farClipPlane = 8000f; cam.fieldOfView = fov;
        if (edgeBlur) go.AddComponent<EdgeBlurEffect>().shader = Shader.Find("Hidden/ProjectGame/EdgeBlur");
        go.transform.position = pos;
        Vector3 dir = (target - pos).normalized;
        go.transform.rotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.98f ? Vector3.forward : Vector3.up);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        Directory.CreateDirectory(ShotDir);
        File.WriteAllBytes(ShotDir + "/" + name + ".png", tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
    }
}

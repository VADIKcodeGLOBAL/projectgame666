using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Small remote control for the open editor: write one command into Logs/EditorAutomation.trigger
/// (refresh | koth | shots | playtest | model &lt;folder&gt;) and the editor runs it. Progress goes to Logs/EditorAutomation.log.
/// Also holds the shared Log and Shot helpers of the editor tools.
/// </summary>
[InitializeOnLoad]
public static class EditorAutomation
{
    const string LogPath = "Logs/EditorAutomation.log";
    const string TriggerPath = "Logs/EditorAutomation.trigger";
    public const string ShotDir = "Logs/shots";
    static double nextPoll;

    const string PlayKey = "EditorAutomation.playtest", PlayAtKey = "EditorAutomation.playtestAt";
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

    static void ArmPlaytest()
    {
        SessionState.SetBool(PlayKey, true);
        SessionState.SetString(PlayAtKey, DateTime.UtcNow.ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        PlayerSettings.runInBackground = true;
        EditorApplication.isPlaying = true;
    }

    // playtest state
    static Vector3 runDir; static float tA, tB, landT, vRun, vAir, tl0, tl1, hp0, w1Health, w1Speed; static bool wasAir; static int spawnedAtWave2, moveIdx, ownBlocked, ownTicks; static float[] hitRates = new float[3];

    static string F(float v) { return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }

    static void Teleport(CharacterController cc, Vector3 p)
    {
        var t = Terrain.activeTerrain; if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y + 0.3f;
        cc.enabled = false; cc.transform.position = p; cc.enabled = true;
    }

    /// <summary>
    /// Automatic test in Play mode (short timings): sprint survives a jump, progress stops outside the circle and resumes inside,
    /// bots reach and hurt the player, wave 2 brings twice the batch and stronger bots. Saves Game view screenshots.
    /// </summary>
    static void PlayTick()
    {
        if (!EditorApplication.isPlaying) return;
        float t = Time.timeSinceLevelLoad;
        if (t < 1.5f) return;
        var game = WaveSurvivalGame.Instance;
        var fp = UnityEngine.Object.FindFirstObjectByType<SimpleFirstPersonController>();
        if (game == null || fp == null || game.zone == null) { Log("PLAYTEST_FAILED: game, player or zone missing"); Stop(); return; }
        var cc = fp.GetComponent<CharacterController>(); var hp = fp.GetComponent<PlayerHealth>();
        Vector3 c = game.zone.transform.position;
        if (t > 120f && playPhase != 9 && playPhase != 10) { Log("PLAYTEST_FAILED: timeout in phase " + playPhase); playPhase = 9; playT = t; return; }

        switch (playPhase)
        {
            case 0:
                game.waveDuration = 16f; game.batchInterval = 6f; game.intermission = 3f;
                runDir = Vector3.right;
                Teleport(cc, c - runDir * 10f); fp.transform.rotation = Quaternion.LookRotation(runDir);
                fp.SetTestInput(Vector3.forward, true, false);
                Log("  playtest: short timings (wave 16 s, batch every 6 s); sprint test starts");
                playPhase = 1; tA = t; break;
            case 1:
                if (t < tA + 0.8f) break;
                vRun = fp.PlanarVelocity.magnitude;
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
                    fp.ClearTestInput(); Teleport(cc, c + new Vector3(0.5f, 0f, 0.5f));
                    playPhase = 3;
                }
                else if (t > tB + 3f) { Log("PLAYTEST_FAILED: the jump did not land"); playPhase = 9; playT = t; }
                break;
            case 3:
                if (game.State != WaveSurvivalGame.GameState.Wave || game.TimeLeft > game.waveDuration - 2f) break;
                tl0 = game.TimeLeft; Teleport(cc, c + runDir * 40f); landT = 1f;
                playPhase = 4; playT = t; break;
            case 4:
                if (t > playT + 2.5f && landT >= 0f) { ScreenCapture.CaptureScreenshot(ShotDir + "/play_paused.png"); landT = -1f; }   // one frame before the check
                if (t < playT + 3f) break;
                tl1 = game.TimeLeft;
                Log("  playtest outside the circle for 3 s: timer " + F(tl0) + " -> " + F(tl1) + ", in zone " + game.InZone + ", bots alive " + game.Alive + ", spawned " + game.Spawned);
                if (game.InZone || Mathf.Abs(tl1 - tl0) > 0.05f) { Log("PLAYTEST_FAILED: progress did not stop outside the circle"); playPhase = 9; playT = t; break; }
                Teleport(cc, c + new Vector3(0.5f, 0f, 0.5f));
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
                bool hitCorner = EnemyBot.RaycastBodies(corner, fwd, ref d1) == tb, hitBeside = EnemyBot.RaycastBodies(beside, fwd, ref d2) == tb;
                bool capsuleCorner = Physics.Raycast(corner, fwd, 30f, 1 << 2, QueryTriggerInteraction.Ignore);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 2000; i++) { float d = 600f; EnemyBot.RaycastBodies(fp.transform.position + Vector3.up * 1.6f, UnityEngine.Random.onUnitSphere, ref d); }
                double us = sw.Elapsed.TotalMilliseconds * 1000.0 / 2000.0;
                Log("  playtest gun hitbox: top corner of the box " + (hitCorner ? "hit" : "MISSED") + " (old capsule: " + (capsuleCorner ? "hit" : "missed") + "), 6 cm beside the box " + (hitBeside ? "HIT" : "missed") + "; bot test " + us.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " us per round with " + EnemyBot.All.Count + " bots");
                if (!hitCorner || hitBeside) { Log("PLAYTEST_FAILED: rounds do not follow the visible box"); playPhase = 9; playT = t; break; }
                fp.Pitch = -80f;                                           // into the sky: the bursts hurt nobody
                tl0 = inv.ShotsFired; tl1 = inv.Current.InMagazine; inv.SetTestInput(true, false);
                playPhase = 12; playT = t; landT = Time.frameCount; break;
            }
            case 12:                                                       // AK-47 held for 2 s: 20 rounds, 20 fewer in the magazine
            {
                if (t < playT + 2f) break;
                var inv = fp.GetComponent<WeaponInventory>(); var ak = inv.Current;
                float fired = inv.ShotsFired - tl0, secs = t - playT, fps = (Time.frameCount - landT) / secs, want = ak.roundsPerSecond * secs;
                Log("  playtest " + ak.displayName + " rate: " + fired + " rounds in " + F(secs) + " s at " + F(fps) + " fps (want " + F(want) + "), magazine " + tl1 + " -> " + ak.InMagazine);
                if (Mathf.Abs(fired - want) > 1.5f || tl1 - ak.InMagazine != fired) { Log("PLAYTEST_FAILED: rate of fire or ammo count is off"); playPhase = 9; playT = t; break; }
                tl1 = ak.Reserve; playPhase = 13; playT = t; break;        // keep holding: the magazine runs dry and reloads by itself
            }
            case 13:
            {
                var inv = fp.GetComponent<WeaponInventory>(); var ak = inv.Current;
                if (ak.IsReloading) { inv.SetTestInput(false, false); hp0 = t; playPhase = 14; break; }
                if (t > playT + 3f) { Log("PLAYTEST_FAILED: an empty magazine did not start a reload (in magazine " + ak.InMagazine + ")"); playPhase = 9; playT = t; }
                break;
            }
            case 14:
            {
                var inv = fp.GetComponent<WeaponInventory>(); var ak = inv.Current;
                if (ak.IsReloading) { if (t > hp0 + ak.reloadTime + 1f) { Log("PLAYTEST_FAILED: the reload never ends"); playPhase = 9; playT = t; } break; }
                Log("  playtest reload: " + F(t - hp0) + " s (set " + F(ak.reloadTime) + "), magazine " + ak.InMagazine + "/" + ak.magazineSize + ", spare rounds " + tl1 + " -> " + ak.Reserve + " (magazines " + ak.Magazines + ")");
                if (ak.InMagazine != ak.magazineSize || tl1 - ak.Reserve != ak.magazineSize) { Log("PLAYTEST_FAILED: the reload did not move one magazine"); playPhase = 9; playT = t; break; }
                tl1 = ak.Magazines;
                AmmoPickup.Spawn(game.ammoPickupPrefab, fp.transform.position + fp.transform.forward * 0.5f);
                playPhase = 15; playT = t; break;
            }
            case 15:                                                       // walk into a magazine
            {
                if (t < playT + 0.6f) break;
                var inv = fp.GetComponent<WeaponInventory>(); var ak = inv.Current;
                Log("  playtest pickup: magazines " + tl1 + " -> " + ak.Magazines + " (" + inv.PickupText + "), pickup prefab " + (game.ammoPickupPrefab != null));
                if (ak.Magazines != tl1 + 1) { Log("PLAYTEST_FAILED: the magazine was not picked up"); playPhase = 9; playT = t; break; }
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
                Teleport(cc, c - runDir * 2f); fp.transform.rotation = Quaternion.LookRotation(runDir);
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
                    inv.Current.FillMagazine();                            // 30 rounds: no reload inside the 1.5 s run
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
                if (++moveIdx < 3) { Teleport(cc, c - runDir * 2f); playT = t; break; }
                tb.Die(false); inv.ClearTestInput(); fp.Pitch = 0f; Teleport(cc, c + new Vector3(0.5f, 0f, 0.5f));
                if (Mathf.Min(hitRates[0], Mathf.Min(hitRates[1], hitRates[2])) < 0.9f) { Log("PLAYTEST_FAILED: rounds miss a target in the crosshair while moving"); playPhase = 9; playT = t; break; }
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
                Log(game.BatchSize == game.mobsPerBatch * 2 && nb.maxHealth > w1Health && nb.speed > w1Speed ? "PLAYTEST_OK" : "PLAYTEST_FAILED: wave 2 is not stronger");
                playPhase = 9; playT = t; break;
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
        try { Directory.CreateDirectory("Logs"); File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); } catch { }
        Debug.Log("[Tools] " + s);
    }

    static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < nextPoll) return;
        nextPoll = now + 0.5;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(TriggerPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)            // in Play mode only "stop" is accepted
        {
            if (File.ReadAllText(TriggerPath).Trim() == "stop") { File.Delete(TriggerPath); Log("command: stop"); Stop(); }
            return;
        }
        string cmd = File.ReadAllText(TriggerPath).Trim();
        File.Delete(TriggerPath);
        Run(cmd);
    }

    static void Run(string cmd)
    {
        Log("command: " + cmd);
        try
        {
            if (cmd == "refresh") { AssetDatabase.Refresh(); Log("REFRESH_OK"); }
            else if (cmd == "koth") KothMapGenerator.Generate(KothMapGenerator.LoadOrCreateSettings());
            else if (cmd == "shots") { KothMapGenerator.CheckShots(); Log("SHOTS_OK"); }
            else if (cmd == "playtest") { playPhase = 0; ArmPlaytest(); }
            else if (cmd == "weapons-info") WeaponSetup.Info();
            else if (cmd == "weapons") WeaponSetup.InstallInOpenScene();
            else if (cmd == "weapons-view") WeaponSetup.ViewShots();
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

using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static partial class EditorAutomation
{
    static int perfPhase, perfFrames, perfLastFrame;
    static float perfT;
    static double perfMain, perfMainMax, perfGc, perfScripts, perfLate, perfGui;
    static ProfilerRecorder recMain, recGc, recScripts, recLate, recGui, recSep, recMove, recCanvas, recOverlay;
    static double perfSep, perfMove, perfCanvas, perfOverlay;

    /// <summary>A recorder for a marker found by name in any category ("PlayerLoop", "UI Render" have no ProfilerCategory constant).</summary>
    static ProfilerRecorder Any(string marker)
    {
        var handles = new System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
        Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var h in handles)
            if (Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h).Name == marker)
            {
                var r = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);
                r.Start();
                return r;
            }
        return default(ProfilerRecorder);
    }

    static double Ms(ProfilerRecorder r) { return r.Valid ? r.LastValue / 1e6 : -1.0; }

    /// <summary>
    /// "perftest": 200 extra bots in a ring 45-75 m around the summit walk in; for 3 s (before they arrive) the frame time,
    /// script time (Update, LateUpdate, OnGUI) and garbage per frame are averaged from the profiler counters.
    /// Editor numbers: compare runs with each other, not with a build.
    /// </summary>
    static void PerfTick()
    {
        float t = Time.timeSinceLevelLoad;
        if (t < 1.5f) return;
        var game = WaveSurvivalGame.Instance;
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        if (game == null || fp == null || game.zone == null || game.botPrefab == null) { Log("PERFTEST_FAILED: game, player, zone or bot prefab missing"); Stop(); return; }
        Vector3 c = game.zone.transform.position;
        switch (perfPhase)
        {
            case 0:
            {
                Teleport(fp, c);
                game.maxAlive = 1000;
                var terrain = Terrain.activeTerrain;
                for (int i = 0; i < 200; i++)
                {
                    float a = i * 137.508f * Mathf.Deg2Rad, r = 45f + (i % 31) * 1f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (terrain != null) p.y = terrain.SampleHeight(p) + terrain.transform.position.y + 0.2f;
                    var b = Object.Instantiate(game.botPrefab, p, Quaternion.LookRotation(new Vector3(c.x - p.x, 0f, c.z - p.z)));
                    b.Init(fp.transform, 40f, 7.5f, 4f, 1f, new Color(1.6f, 0.9f, 0.2f));
                }
                Log("  perftest: " + EnemyBot.All.Count + " bots alive");
                perfPhase = 1; perfT = t; break;
            }
            case 1:
                if (t < perfT + 1f) break;
                recMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
                recGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                recScripts = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BehaviourUpdate");
                recLate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LateBehaviourUpdate");
                recGui = ProfilerRecorder.StartNew(ProfilerCategory.Gui, "GUI.Repaint");
                recSep = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "EnemyBot.Separation");
                recMove = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "EnemyBot.Move");
                recCanvas = Any("PostLateUpdate.PlayerUpdateCanvases"); recOverlay = Any("UI.RenderOverlays");
                if (!recCanvas.Valid || !recOverlay.Valid)                // list what the profiler offers for the canvases
                {
                    var handles = new System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
                    Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
                    var sb = new System.Text.StringBuilder("  perftest canvas markers:");
                    foreach (var hnd in handles)
                    {
                        var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(hnd);
                        if (d.Name.IndexOf("Canvas", System.StringComparison.OrdinalIgnoreCase) >= 0 || d.Name.StartsWith("UGUI") || d.Name.StartsWith("UI."))
                            sb.Append(" [" + d.Category.Name + "] " + d.Name + ";");
                    }
                    Log(sb.ToString());
                }
                perfFrames = 0; perfMain = perfMainMax = perfGc = perfScripts = perfLate = perfGui = perfSep = perfMove = perfCanvas = perfOverlay = 0; perfLastFrame = Time.frameCount;
                perfPhase = 2; perfT = t; break;
            case 2:
                if (Time.frameCount != perfLastFrame && recMain.Valid && recMain.LastValue > 0)
                {
                    perfLastFrame = Time.frameCount; perfFrames++;
                    double m = Ms(recMain); perfMain += m; if (m > perfMainMax) perfMainMax = m;
                    perfGc += recGc.Valid ? recGc.LastValue : 0; perfScripts += Ms(recScripts); perfLate += Ms(recLate); perfGui += Ms(recGui);
                    perfSep += Ms(recSep); perfMove += Ms(recMove); perfCanvas += Ms(recCanvas); perfOverlay += Ms(recOverlay);
                }
                if (t < perfT + 3f) break;
                int n = Mathf.Max(1, perfFrames);
                System.Func<double, string> f2 = v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                Log("  perftest " + EnemyBot.All.Count + " bots, " + perfFrames + " frames: main thread " + f2(perfMain / n) + " ms (max " + f2(perfMainMax) + "), "
                    + "Update " + (recScripts.Valid ? f2(perfScripts / n) + " ms" : "n/a") + ", LateUpdate " + (recLate.Valid ? f2(perfLate / n) + " ms" : "n/a")
                    + " (bots: separation " + (recSep.Valid ? f2(perfSep / n) + " ms" : "n/a") + ", CharacterController.Move " + (recMove.Valid ? f2(perfMove / n) + " ms" : "n/a") + ")"
                    + ", OnGUI repaint " + (recGui.Valid ? f2(perfGui / n) + " ms" : "n/a")
                    + ", canvas update " + (recCanvas.Valid ? f2(perfCanvas / n) + " ms" : "n/a") + ", canvas draw " + (recOverlay.Valid ? f2(perfOverlay / n) + " ms" : "n/a") + ", garbage " + (recGc.Valid ? (perfGc / n / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB/frame" : "n/a"));
                recMain.Dispose(); recGc.Dispose(); recScripts.Dispose(); recLate.Dispose(); recGui.Dispose(); recSep.Dispose(); recMove.Dispose(); recCanvas.Dispose(); recOverlay.Dispose();
                Log("PERFTEST_OK");
                perfPhase = 3; Stop(); break;
        }
    }
}

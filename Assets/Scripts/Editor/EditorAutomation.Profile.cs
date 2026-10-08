using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EditorAutomation
{
    /// <summary>
    /// "profile": where the frame goes, from the Unity profiler. Two scenes of 150 frames each: 200 bots walking in on the summit,
    /// then the AK firing into them (hits, deaths, drops). For each, the main thread's samples are summed over the frames by
    /// name (self time, the editor's own loop left out) and the garbage is put on the sample that made it; the top ones are logged.
    /// </summary>
    static IEnumerator ProfileRun()
    {
        var game = WaveSurvivalGame.Instance;
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        if (game == null || fp == null || game.botPrefab == null) throw new Exception("game, player or bot prefab missing");
        var inv = fp.GetComponent<WeaponInventory>();
        var terrain = Terrain.activeTerrain;
        Vector3 c = game.zone.transform.position;
        game.maxAlive = 1000;
        Teleport(fp, c);
        yield return Wait(1f);
        Action<float, float> ring = (near, span) =>
        {
            for (int i = 0; i < 200; i++)
            {
                float a = i * 137.508f * Mathf.Deg2Rad, r = near + (i % 31) / 30f * span;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (terrain != null) p.y = terrain.SampleHeight(p) + terrain.transform.position.y + 0.2f;
                var b = Object.Instantiate(game.botPrefab, p, Quaternion.LookRotation(new Vector3(c.x - p.x, 0f, c.z - p.z)));
                b.Init(fp.transform, 40f, 7.5f, 4f, 1f, new Color(1.6f, 0.9f, 0.2f)); moveTemp.Add(b.gameObject);
            }
        };

        // where they come from: over the ridge, 160-240 m out
        ring(160f, 80f);
        yield return Wait(1f);
        yield return Capture("far", 120, null);
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
        yield return Wait(0.5f);

        // close: 45-75 m, walking in
        ring(45f, 30f);
        yield return Wait(1f);
        yield return Capture("walk", 150, null);

        // the AK into the crowd: a full magazine every frame, the view on the nearest bots
        fp.transform.rotation = Quaternion.LookRotation(Vector3.forward); fp.Pitch = 4f;
        inv.SetTestInput(true, false);
        yield return Capture("fire", 150, () =>
        {
            if (inv.CurrentGun != null) inv.CurrentGun.FillMagazine();
            fp.transform.Rotate(0f, 40f * Time.deltaTime, 0f);
        });
        inv.ClearTestInput();
        Check(true, "profiled: " + EnemyBot.All.Count + " bots left, " + game.Kills + " killed");
    }

    static bool profiling;

    /// <summary>Records frames frames of the profiler and analyses just those: what the Profiler window held before is left alone.</summary>
    static IEnumerator Capture(string label, int frames, Action perFrame)
    {
        int from = ProfilerDriver.lastFrameIndex + 1;
        profiling = true; ProfilerDriver.enabled = true;
        int start = Time.frameCount; float t0 = Time.realtimeSinceStartup;
        while (Time.frameCount < start + frames) { if (perFrame != null) perFrame(); yield return null; }
        float secs = Time.realtimeSinceStartup - t0;
        StopProfiling();
        Analyze(label, secs, from);
    }

    /// <summary>Recording off (also when Play mode ends in the middle of a capture).</summary>
    static void StopProfiling()
    {
        if (!profiling) return;
        profiling = false; ProfilerDriver.enabled = false;
    }

    static void Analyze(string label, float secs, int from)
    {
        var self = new Dictionary<string, double>(); var gc = new Dictionary<string, double>(); var calls = new Dictionary<string, double>();
        int first = Mathf.Max(from, ProfilerDriver.firstFrameIndex), last = ProfilerDriver.lastFrameIndex, n = 0;
        double frameMs = 0, editor = 0;
        for (int f = first; f <= last && f >= 0; f++)
        {
            using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
            {
                if (v == null || !v.valid) continue;
                n++; frameMs += v.frameTimeMs;
                Walk(v, v.GetRootItemID(), "(root)", 0, self, gc, calls, ref editor);
            }
        }
        if (n == 0) { Log("  profile " + label + ": no frames captured"); return; }
        Func<double, string> f2 = x => x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        sb.Append("  profile " + label + ": " + n + " frames, " + f2(frameMs / n) + " ms/frame (" + f2(secs / Mathf.Max(1, n) * 1000.0) + " ms real), editor loop " + f2(editor / n) + " ms; self time ms/frame:\n");
        foreach (var kv in self.OrderByDescending(x => x.Value).Take(32))
            sb.Append("    " + f2(kv.Value / n) + "  " + kv.Key + "  (" + (calls.ContainsKey(kv.Key) ? (calls[kv.Key] / n).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "-") + " calls)\n");
        sb.Append("  garbage KB/frame by the sample that made it:\n");
        foreach (var kv in gc.OrderByDescending(x => x.Value).Take(20))
            sb.Append("    " + (kv.Value / n / 1024.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "  " + kv.Key + "\n");
        Log(sb.ToString().TrimEnd());
    }

    static readonly List<List<int>> kidLists = new List<List<int>>();          // one list per depth, reused

    static void Walk(HierarchyFrameDataView v, int id, string parent, int depth, Dictionary<string, double> self, Dictionary<string, double> gc, Dictionary<string, double> calls, ref double editor)
    {
        while (kidLists.Count <= depth) kidLists.Add(new List<int>());
        var kids = kidLists[depth];
        v.GetItemChildren(id, kids);
        for (int i = 0; i < kids.Count; i++)
        {
            int k = kids[i];
            string name = v.GetItemName(k);
            if (name == "EditorLoop") { editor += v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime); continue; }
            if (name == "GC.Alloc")
            {
                double b = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnGcMemory);
                double old; gc.TryGetValue(parent, out old); gc[parent] = old + b;
                continue;
            }
            double s = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnSelfTime), cnt = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnCalls);
            double o; self.TryGetValue(name, out o); self[name] = o + s;
            calls.TryGetValue(name, out o); calls[name] = o + cnt;
            Walk(v, k, name, depth + 1, self, gc, calls, ref editor);
        }
    }
}
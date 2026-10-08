using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EditorAutomation
{
    /// <summary>
    /// "swordtest": the bastard sword in Play mode with the waves off. It is slot 5 and shows its blows on the HUD; a slash cuts the bots
    /// in its arc and reach (in front, 45 degrees left) and spares the one behind and the far one, lands during the strike (not on the
    /// click), throws them back and staggers them; held LMB chains slashes both ways; the RMB chop hits harder in a narrow arc and does
    /// not zoom; a wall between saves the bot; a weak bot dies; the guns still work after it.
    /// </summary>
    static IEnumerator SwordTests()
    {
        var game = WaveSurvivalGame.Instance;
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        var t = Terrain.activeTerrain;
        if (game == null || fp == null || t == null) throw new Exception("game, player or terrain missing");
        var inv = fp.GetComponent<WeaponInventory>();
        var sword = inv.weapons.OfType<MeleeWeapon>().FirstOrDefault();
        game.enabled = false;
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
        Func<Vector3, Vector3> onGround = p => { p.y = t.SampleHeight(p) + t.transform.position.y; return p; };
        Vector3 c = onGround(game.zone.transform.position);
        yield return Wait(0.3f);

        Check(sword != null && inv.weapons.Length == 5 && Array.IndexOf(inv.weapons, sword) == 4, "the sword is weapon slot 5 (" + inv.weapons.Length + " slots)");
        if (sword == null) yield break;
        fp.Teleport(c + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.identity; fp.Pitch = 0f; inv.ResetSway();
        inv.Select(4);
        float t0 = Time.time;
        while (inv.IsSwitching && Time.time < t0 + 2f) yield return null;
        yield return Wait(0.2f);
        var hud = Object.FindFirstObjectByType<HudView>();
        Check(inv.Current == sword && hud != null && hud.status.text.Contains("SLASH") && !hud.rounds.gameObject.activeSelf,
              "sword in hand, HUD shows its blows (" + (hud != null ? hud.status.text : "no HUD") + ")");

        var bots = new List<EnemyBot>();
        Func<Vector3, float, EnemyBot> spawn = (offset, hp) =>
        {
            var b = Object.Instantiate(game.botPrefab, onGround(c + offset) + Vector3.up * 0.05f, Quaternion.LookRotation(-offset.normalized + Vector3.forward * 0.001f));
            b.Init(fp.transform, hp, 0f, 0f, 1f, Color.white); moveTemp.Add(b.gameObject); bots.Add(b);
            return b;
        };
        Action clear = () => { foreach (var b in bots) if (b != null) Object.Destroy(b.gameObject); bots.Clear(); };

        // ---- one slash: the arc and the reach, during the strike, thrown back
        var front = spawn(new Vector3(0f, 0f, 1.8f), 1000f); var left = spawn(new Vector3(-1.27f, 0f, 1.27f), 1000f);
        var behind = spawn(new Vector3(0f, 0f, -1.8f), 1000f); var far = spawn(new Vector3(0f, 0f, 4.5f), 1000f);
        yield return Wait(0.4f);
        Vector3 frontAt = front.transform.position;
        int swings = sword.SwingsStarted;
        inv.SetTestInput(true, false); float click = Time.time; yield return null; inv.SetTestInput(false, false);
        float firstHit = -1f;
        yield return Run(0.7f, () => { if (firstHit < 0f && front.Health < 1000f) firstHit = Time.time - click; });
        float pushed = Horiz(front.transform.position, frontAt);
        Check(front.Health == 1000f - sword.slashDamage && left.Health == 1000f - sword.slashDamage && behind.Health == 1000f && far.Health == 1000f,
              "slash: front " + (1000f - front.Health) + ", 45 deg left " + (1000f - left.Health) + ", behind " + (1000f - behind.Health) + ", 4.5 m away " + (1000f - far.Health) + " damage");
        Check(firstHit >= sword.slashWindup - 0.02f && firstHit <= sword.slashWindup + sword.slashStrike + 0.05f,
              "the cut lands during the strike: " + F3(firstHit) + " s after the click (strike " + F3(sword.slashWindup) + "-" + F3(sword.slashWindup + sword.slashStrike) + " s)");
        Check(pushed > 0.3f && sword.SwingsStarted == swings + 1, "thrown back " + F3(pushed) + " m, one swing");
        clear(); yield return Wait(0.3f);

        // ---- held LMB: a chain of slashes, both ways
        var seen = new List<MeleeWeapon.Swing>();
        swings = sword.SwingsStarted;
        inv.SetTestInput(true, false);
        yield return Run(1.4f, () => { if (sword.State != MeleeWeapon.Swing.None && (seen.Count == 0 || seen[seen.Count - 1] != sword.State)) seen.Add(sword.State); });
        inv.SetTestInput(false, false);
        int chained = sword.SwingsStarted - swings;
        bool alternates = seen.Count >= 2 && seen.Zip(seen.Skip(1), (a, b) => a != b).All(x => x);
        Check(chained >= 3 && alternates && seen.All(s => s != MeleeWeapon.Swing.Chop), "held LMB for 1.4 s: " + chained + " slashes, " + string.Join(" > ", seen.Select(s => s == MeleeWeapon.Swing.SlashRightToLeft ? "R-L" : "L-R").ToArray()));
        yield return Wait(0.6f);

        // ---- RMB: the chop, narrow and heavy, no zoom
        front = spawn(new Vector3(0f, 0f, 2.2f), 1000f); left = spawn(new Vector3(-1.27f, 0f, 1.27f), 1000f);
        yield return Wait(0.4f);
        frontAt = front.transform.position; float fov0 = inv.cam.fieldOfView, fovMax = 0f;
        inv.SetTestInput(false, true); yield return null; inv.SetTestInput(false, false);
        yield return Run(1.2f, () => fovMax = Mathf.Max(fovMax, Mathf.Abs(inv.cam.fieldOfView - fov0)));
        Check(front.Health == 1000f - sword.chopDamage && left.Health == 1000f, "chop: front (2.2 m) " + (1000f - front.Health) + " damage, 45 deg left " + (1000f - left.Health));
        Check(Horiz(front.transform.position, frontAt) > 1f && fovMax < 0.01f, "chop throws back " + F3(Horiz(front.transform.position, frontAt)) + " m; RMB does not zoom (fov change " + F3(fovMax) + ")");
        clear(); yield return Wait(0.3f);

        // ---- a wall between: no cut; a weak bot: dies
        var guarded = spawn(new Vector3(0f, 0f, 1.9f), 1000f);
        var wall = Block("SwordWall", onGround(c + new Vector3(0f, 0f, 1.05f)) + Vector3.up * 1.2f, new Vector3(2.5f, 2.4f, 0.2f), Quaternion.identity);
        yield return Wait(0.3f);
        inv.SetTestInput(true, false); yield return null; inv.SetTestInput(false, false);
        yield return Wait(0.7f);
        Check(guarded.Health == 1000f, "a wall between: the bot behind it is not cut (" + (1000f - guarded.Health) + " damage)");
        Object.Destroy(wall); clear(); yield return Wait(0.3f); Physics.SyncTransforms();
        int kills = game.Kills;
        var weak = spawn(new Vector3(0.3f, 0f, 1.7f), 40f);
        yield return Wait(0.3f);
        inv.SetTestInput(true, false); yield return null; inv.SetTestInput(false, false);
        yield return Wait(0.7f);
        Check((weak == null || weak.IsDying) && game.Kills == kills + 1, "a 40 HP bot dies from one slash, kills " + kills + " -> " + game.Kills);
        clear();

        // ---- pictures (a screenshot stalls a frame, so none of them is taken during a timed check)
        ScreenCapture.CaptureScreenshot(ShotDir + "/sword_rest.png");
        yield return Wait(0.3f);
        inv.SetTestInput(true, false); yield return null; inv.SetTestInput(false, false);
        while (sword.StrikeProgress < 0.4f && sword.State != MeleeWeapon.Swing.None) yield return null;
        ScreenCapture.CaptureScreenshot(ShotDir + "/sword_slash.png");
        yield return Wait(0.8f);
        inv.SetTestInput(false, true); yield return null; inv.SetTestInput(false, false);
        yield return Wait(sword.chopWindup * 0.8f);
        ScreenCapture.CaptureScreenshot(ShotDir + "/sword_chop_raised.png");
        while (sword.StrikeProgress < 0.55f && sword.State != MeleeWeapon.Swing.None) yield return null;
        ScreenCapture.CaptureScreenshot(ShotDir + "/sword_chop_down.png");
        yield return Wait(1f);

        // ---- the guns after it
        inv.Select(0); t0 = Time.time;
        while (inv.IsSwitching && Time.time < t0 + 2f) yield return null;
        int shots = inv.ShotsFired; fp.Pitch = -70f;
        inv.SetTestInput(true, false); yield return Wait(0.5f); inv.SetTestInput(false, false); inv.ClearTestInput();
        fp.Pitch = 0f;
        Check(inv.CurrentGun != null && inv.ShotsFired > shots + 2 && hud.rounds.gameObject.activeSelf, "back to the " + (inv.Current != null ? inv.Current.displayName : "-") + ": " + (inv.ShotsFired - shots) + " rounds, ammo on the HUD again");
    }
}

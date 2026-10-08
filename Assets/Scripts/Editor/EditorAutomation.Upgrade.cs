using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EditorAutomation
{
    static IEnumerator RealWait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }

    /// <summary>
    /// "upgradetest": the upgrades in Play mode with the waves off. The catalog (every rarity there, ids unique), the odds (better
    /// rarities later, mixed offers), an offer pauses the game and frees the mouse, TAB-later sends it to the panel on the left and
    /// the game runs on, two can wait, the oldest comes back first, a card taken is applied; then the effects on bots and the
    /// player, one modifier at a time. Screenshots of the cards (every rarity), a lifted card, the waiting panel.
    /// </summary>
    static IEnumerator UpgradeTests()
    {
        var game = WaveSurvivalGame.Instance;
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        var t = Terrain.activeTerrain;
        var ups = UpgradeSystem.Instance;
        if (game == null || fp == null || t == null) throw new Exception("game, player or terrain missing");
        var inv = fp.GetComponent<WeaponInventory>(); var hp = fp.GetComponent<PlayerHealth>();
        game.enabled = false;
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
        Func<Vector3, Vector3> onGround = p => { p.y = t.SampleHeight(p) + t.transform.position.y; return p; };
        Vector3 c = onGround(game.zone.transform.position);
        fp.Teleport(c + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.identity; fp.Pitch = 0f; inv.ResetSway();
        yield return Wait(0.3f);
        Check(ups != null && ups.Screen != null && ups.gameObject == game.gameObject, "upgrade system on the game object, its screen built");
        if (ups == null) yield break;

        // ---- the catalog
        var all = ModifierCatalog.All;
        var counts = new int[5]; foreach (var m in all) counts[(int)m.rarity]++;
        bool idsUnique = all.Select(m => m.id).Distinct().Count() == all.Count, filled = all.All(m => m.apply != null && m.title.Length > 0 && m.text.Length > 0);
        Check(idsUnique && filled && counts.All(n => n >= 2), "catalog: " + all.Count + " modifiers - " + string.Join(", ", Enumerable.Range(0, 5).Select(r => counts[r] + " " + ModifierCatalog.RarityNames[r]).ToArray()));

        // ---- the odds: later waves bring the rarer cards; an offer mixes rarities and never repeats a card
        Func<int, float[]> shares = w =>
        {
            var n = new float[5]; int cards = 0;
            for (int k = 0; k < 600; k++) foreach (var m in ups.Roll(w).cards) { n[(int)m.rarity]++; cards++; }
            for (int r = 0; r < 5; r++) n[r] /= cards;
            return n;
        };
        float[] s1 = shares(1), s5 = shares(5);
        int mixed = 0, repeats = 0;
        for (int k = 0; k < 400; k++) { var o = ups.Roll(3); if (o.cards.Select(m => m.rarity).Distinct().Count() > 1) mixed++; if (o.cards.Distinct().Count() < o.cards.Length) repeats++; }
        Func<float[], string> pct = a => string.Join("/", a.Select(x => Mathf.RoundToInt(x * 100f).ToString()).ToArray());
        Check(s5[2] + s5[3] + s5[4] > (s1[2] + s1[3] + s1[4]) * 1.5f && s1[0] > s5[0] && s5[4] > 0f && mixed > 200 && repeats == 0,
              "odds % (COMMON/GREAT/RARE/SWEG/SLAYER): after wave 1 " + pct(s1) + ", after wave 5 " + pct(s5) + "; mixed offers " + mixed + "/400, repeats " + repeats);

        // ---- a wave held: the offer comes up by itself and pauses the game
        game.waveDuration = 1.5f; game.intermission = 30f; game.batchInterval = 1000f; game.enabled = true;
        float w0 = Time.realtimeSinceStartup;
        while (!UpgradeSystem.IsChoosing && Time.realtimeSinceStartup < w0 + 15f) yield return null;
        yield return RealWait(0.9f);
        var real = ups.Current;
        Check(real != null && real.wave == 1 && Time.timeScale == 0f && Cursor.lockState != CursorLockMode.Locked && game.State == WaveSurvivalGame.GameState.Intermission,
              "wave 1 held: the offer comes up by itself (" + (real != null ? string.Join(", ", real.cards.Select(m => m.title + " " + ModifierCatalog.Name(m.rarity)).ToArray()) : "none")
              + "), paused " + (Time.timeScale == 0f) + ", mouse " + Cursor.lockState);
        if (real != null) { ups.Later(); yield return RealWait(0.8f); }
        game.enabled = false; ups.Waiting.Clear();
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
        foreach (var p in SupplyPickup.All.ToArray()) Object.Destroy(p.gameObject);   // the wave laid medkits on the summit: they would heal
        yield return Wait(0.3f);

        // ---- an offer: paused, the mouse free, the cards on the screen
        var screen = ups.Screen;
        ups.OpenOffer(new UpgradeSystem.Offer { wave = 2, cards = new[] { ModifierCatalog.Find("c_damage"), ModifierCatalog.Find("g_mags"), ModifierCatalog.Find("r_pierce") } });
        yield return RealWait(0.9f);
        Check(Time.timeScale == 0f && UpgradeSystem.IsChoosing && Cursor.lockState != CursorLockMode.Locked && screen.CardCount == 3 && !ups.Busy,
              "an offer: paused (time scale " + Time.timeScale + "), mouse " + Cursor.lockState + ", " + screen.CardCount + " cards");
        ScreenCapture.CaptureScreenshot(ShotDir + "/upgrade_cards_a.png"); yield return RealWait(0.2f);
        screen.TestHover = 1; yield return RealWait(0.45f);
        ScreenCapture.CaptureScreenshot(ShotDir + "/upgrade_hover.png"); yield return RealWait(0.2f);
        screen.TestHover = -1;

        // ---- later: to the panel, the game runs on; a second one waits too; the oldest comes back first
        ups.Later(); yield return RealWait(0.8f);
        bool firstWaits = !UpgradeSystem.IsChoosing && Time.timeScale == 1f && ups.Waiting.Count == 1;
        yield return Wait(0.3f);
        ScreenCapture.CaptureScreenshot(ShotDir + "/upgrade_waiting.png"); yield return Wait(0.2f);
        ups.OpenOffer(new UpgradeSystem.Offer { wave = 3, cards = new[] { ModifierCatalog.Find("s_chain"), ModifierCatalog.Find("x_berserk"), ModifierCatalog.Find("s_timewarp") } });
        yield return RealWait(0.9f);
        ScreenCapture.CaptureScreenshot(ShotDir + "/upgrade_cards_b.png"); yield return RealWait(0.3f);
        ups.Later(); yield return RealWait(0.8f);
        Check(firstWaits && ups.Waiting.Count == 2 && Time.timeScale == 1f, "TAB later: the game runs on, " + ups.Waiting.Count + " choices wait on the left");
        yield return Wait(0.3f);
        ScreenCapture.CaptureScreenshot(ShotDir + "/upgrade_waiting2.png"); yield return Wait(0.2f);
        ups.OpenWaiting(); yield return RealWait(0.9f);
        bool oldest = UpgradeSystem.IsChoosing && ups.Current != null && ups.Current.wave == 2 && Time.timeScale == 0f;
        int before = ups.taken.Count;
        ups.Choose(2); yield return RealWait(0.9f);                    // Piercing Rounds
        Check(oldest && !UpgradeSystem.IsChoosing && Time.timeScale == 1f && ups.taken.Count == before + 1 && ups.taken.Last() == "r_pierce" && UpgradeSystem.Stats.pierce == 1 && ups.Waiting.Count == 1,
              "back with TAB: the oldest (wave 2) first; card 3 taken: " + ups.taken.Last() + ", pierce " + UpgradeSystem.Stats.pierce + ", " + ups.Waiting.Count + " still waiting");
        ups.OpenWaiting(); yield return RealWait(0.9f);
        ups.Choose(0); yield return RealWait(0.9f);
        Check(ups.Waiting.Count == 0 && !UpgradeSystem.IsChoosing && ups.taken.Last() == "s_chain", "the second one taken too, nothing waits");

        // ---- the effects, one at a time on fresh stats
        var bots = new List<EnemyBot>();
        Func<Vector3, float, float, EnemyBot> spawn = (offset, health, speed) =>
        {
            var b = Object.Instantiate(game.botPrefab, onGround(c + offset) + Vector3.up * 0.05f, Quaternion.LookRotation(-offset.normalized + Vector3.forward * 0.001f));
            b.Init(fp.transform, health, speed, 0f, 1f, Color.white); moveTemp.Add(b.gameObject); bots.Add(b);
            return b;
        };
        Action clear = () => { foreach (var b in bots) if (b != null) Object.Destroy(b.gameObject); bots.Clear(); };
        var ak = inv.CurrentGun;
        Func<EnemyBot, IEnumerator> shootAt = target => ShootOnce(fp, inv, target);

        // gun damage
        ups.ResetStats(); ups.ApplyById("g_damage");
        var a = spawn(new Vector3(0f, 0f, 12f), 1000f, 0f); yield return Wait(0.3f);
        yield return shootAt(a);
        Check(Mathf.Abs(1000f - a.Health - ak.damage * 1.18f) < 0.01f, "Match Grade Ammo: a round takes " + F3(1000f - a.Health) + " (base " + ak.damage + ")");
        clear(); yield return Wait(0.2f);

        // piercing: the bot behind is hit too
        ups.ResetStats(); ups.ApplyById("r_pierce");
        a = spawn(new Vector3(0f, 0f, 10f), 1000f, 0f); var behind = spawn(new Vector3(0f, 0f, 14f), 1000f, 0f); yield return Wait(0.3f);
        yield return shootAt(a);
        Check(a.Health < 1000f && behind.Health < 1000f, "Piercing Rounds: front " + F3(1000f - a.Health) + ", the one behind " + F3(1000f - behind.Health));
        clear(); yield return Wait(0.2f);

        // chain lightning: two bots near the one hit take 60 %
        ups.ResetStats(); ups.ApplyById("s_chain");
        // the arc hops from bot to bot (each hop within 6 m): a chain 2.5 m apart, one more 9 m on stays out of it
        a = spawn(new Vector3(0f, 0f, 12f), 1000f, 0f); var n1 = spawn(new Vector3(2.5f, 0f, 12f), 1000f, 0f); var n2 = spawn(new Vector3(5f, 0f, 12f), 1000f, 0f);
        var far = spawn(new Vector3(14f, 0f, 12f), 1000f, 0f); yield return Wait(0.3f);
        yield return ShootOnce(fp, inv, a, "upgrade_chain");
        float want = ak.damage * 0.6f;
        Check(Mathf.Abs(1000f - n1.Health - want) < 0.01f && Mathf.Abs(1000f - n2.Health - want) < 0.01f && far.Health == 1000f,
              "Chain Lightning: the next two in the chain take " + F3(1000f - n1.Health) + " / " + F3(1000f - n2.Health) + " (want " + F3(want) + "), 14 m off " + F3(1000f - far.Health));
        clear(); yield return Wait(0.2f);

        // explosive rounds: 45 % within 2.5 m
        ups.ResetStats(); ups.ApplyById("s_explosive");
        a = spawn(new Vector3(0f, 0f, 12f), 1000f, 0f); n1 = spawn(new Vector3(1.6f, 0f, 12.5f), 1000f, 0f); far = spawn(new Vector3(-5f, 0f, 12f), 1000f, 0f); yield return Wait(0.3f);
        yield return shootAt(a);
        Check(Mathf.Abs(1000f - n1.Health - ak.damage * 0.45f) < 0.01f && far.Health == 1000f, "Explosive Rounds: 1.6 m off " + F3(1000f - n1.Health) + ", 5 m off " + F3(1000f - far.Health));
        clear(); yield return Wait(0.2f);

        // stopping power: thrown back
        ups.ResetStats(); ups.ApplyById("r_stopping");
        a = spawn(new Vector3(0f, 0f, 12f), 1000f, 0f); yield return Wait(0.3f);
        Vector3 at = a.transform.position;
        yield return shootAt(a); yield return Wait(0.4f);
        Check(Horiz(a.transform.position, at) > 0.15f, "Stopping Power: thrown back " + F3(Horiz(a.transform.position, at)) + " m");
        clear(); yield return Wait(0.2f);

        // vampire: a kill heals 5
        ups.ResetStats(); ups.ApplyById("r_vampire");
        hp.TakeDamage(30f); float h0 = hp.Health;
        a = spawn(new Vector3(0f, 0f, 12f), 10f, 0f); yield return Wait(0.3f);
        yield return shootAt(a);
        Check(a.IsDying && Mathf.Abs(hp.Health - h0 - 5f) < 0.01f, "Vampire: a kill, health " + F3(h0) + " -> " + F3(hp.Health));
        hp.Heal(1000f); clear(); yield return Wait(0.2f);

        // last stand: a killing blow leaves 1 HP, then 2 s untouchable
        ups.ResetStats(); ups.ApplyById("r_laststand");
        hp.TakeDamage(1000f); float afterBlow = hp.Health;
        hp.TakeDamage(50f); float untouched = hp.Health;
        yield return Wait(2.2f);
        hp.TakeDamage(0.5f);
        Check(afterBlow == 1f && untouched == 1f && Mathf.Abs(hp.Health - 0.5f) < 0.001f && !hp.IsDead, "Last Stand: a killing blow leaves " + F3(afterBlow) + " HP, the next one " + F3(untouched) + ", after 2 s it hurts again (" + F3(hp.Health) + ")");
        hp.Heal(1000f);

        // max health, armour and berserk
        ups.ResetStats(); float max0 = hp.maxHealth; ups.ApplyById("c_hp");
        bool hpUp = hp.maxHealth == max0 + 10f && hp.Health == hp.maxHealth;
        ups.ResetStats(); ups.ApplyById("x_berserk");
        float b0 = hp.Health; hp.TakeDamage(10f); float took = b0 - hp.Health; hp.Heal(100f);
        a = spawn(new Vector3(0f, 0f, 12f), 1000f, 0f); yield return Wait(0.3f);
        yield return shootAt(a);
        Check(hpUp && Mathf.Abs(took - 15f) < 0.01f && Mathf.Abs(1000f - a.Health - ak.damage * 2f) < 0.01f,
              "Thick Skin: max health " + F3(max0) + " -> " + F3(hp.maxHealth) + "; BERSERK: 10 damage takes " + F3(took) + ", a round " + F3(1000f - a.Health));
        clear(); yield return Wait(0.2f);

        // speed, bots slowed
        ups.ResetStats(); ups.ApplyById("g_speed");
        fp.SetTestInput(Vector3.forward, false, false); yield return Wait(0.6f);
        float walk = fp.PlanarVelocity.magnitude; fp.ClearTestInput();
        fp.Teleport(c + Vector3.up * 0.3f); fp.transform.rotation = Quaternion.identity; yield return Wait(0.3f);
        ups.ResetStats();
        a = spawn(new Vector3(0f, 0f, 30f), 1000f, 6f); yield return Wait(0.5f);
        Vector3 p0 = a.transform.position; yield return Wait(1f); float free = Horiz(a.transform.position, p0);
        ups.ApplyById("s_timewarp");
        p0 = a.transform.position; yield return Wait(1f); float warped = Horiz(a.transform.position, p0);
        Check(Mathf.Abs(walk - fp.walkSpeed * 1.1f) < 0.1f && warped / Mathf.Max(0.01f, free) > 0.58f && warped / Mathf.Max(0.01f, free) < 0.82f,
              "Runner: walks " + F3(walk) + " m/s (" + fp.walkSpeed + " x1.1); Time Warp: a bot " + F3(free) + " -> " + F3(warped) + " m/s");
        clear(); yield return Wait(0.2f);

        // bullet hose: faster, every other round free; extended mags
        ups.ResetStats(); ups.ApplyById("s_hose");
        ak.FillMagazine(); int mag0 = ak.InMagazine, shots0 = inv.ShotsFired; fp.Pitch = -75f;
        inv.SetTestInput(true, false); yield return Wait(1.6f); inv.SetTestInput(false, false);
        int fired = inv.ShotsFired - shots0, used = mag0 - ak.InMagazine; fp.Pitch = 0f;
        ups.ResetStats(); ak.FillMagazine(); int size0 = ak.magazineSize; ups.ApplyById("g_mags");
        Check(fired > ak.roundsPerSecond * 1.6f * 1.1f && used < fired * 0.8f && used > fired * 0.2f && ak.magazineSize == Mathf.RoundToInt(size0 * 1.25f) && ak.InMagazine == ak.magazineSize,
              "Bullet Hose: " + fired + " rounds in 1.6 s used " + used + " from the magazine; Extended Mags: " + size0 + " -> " + ak.magazineSize + ", full");
        yield return Wait(0.3f);

        // the sword: whirlwind cuts behind, featherweight weighs nothing
        ups.ResetStats(); ups.ApplyById("s_whirlwind"); ups.ApplyById("r_feather");
        fp.transform.rotation = Quaternion.identity; inv.Select(4);
        float t0 = Time.time; while (inv.IsSwitching && Time.time < t0 + 3f) yield return null;
        var sword = inv.Current as MeleeWeapon;
        var back = spawn(new Vector3(0f, 0f, -1.8f), 1000f, 0f); yield return Wait(0.3f);
        inv.SetTestInput(true, false); yield return null; inv.SetTestInput(false, false);
        yield return Wait(0.9f);
        Check(sword != null && back.Health < 1000f && sword.MoveSpeedScale == 1f && sword.DrawTimeScale == 1f,
              "Whirlwind: the bot behind takes " + F3(1000f - back.Health) + "; Featherweight: speed x" + (sword != null ? F3(sword.MoveSpeedScale) : "-") + ", draw x" + (sword != null ? F3(sword.DrawTimeScale) : "-"));
        clear(); inv.Select(0); ups.ResetStats();
        yield return Wait(1f);
    }

    /// <summary>Turns the view on the bot's body and fires one round of the gun in hand.</summary>
    static IEnumerator ShootOnce(SimpleFirstPersonController fp, WeaponInventory inv, EnemyBot target, string shot = null)
    {
        Vector3 eye = fp.cameraPivot.position, aim = target.body.bounds.center, d = aim - eye;
        fp.transform.rotation = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z)); inv.ResetSway();
        fp.Pitch = -Mathf.Asin(d.y / d.magnitude) * Mathf.Rad2Deg;
        if (inv.CurrentGun != null) inv.CurrentGun.FillMagazine();
        yield return Wait(0.15f);
        inv.SetTestInput(true, false); yield return null; inv.SetTestInput(false, false);
        if (shot != null) ScreenCapture.CaptureScreenshot(ShotDir + "/" + shot + ".png");   // the round's effects still on
        yield return Wait(0.2f);
        fp.Pitch = 0f;
    }
}

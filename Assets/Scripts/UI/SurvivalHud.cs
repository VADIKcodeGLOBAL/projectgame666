using UnityEngine;

/// <summary>
/// HUD of the wave survival mode on a uGUI Canvas (HudView): wave timer, next batch, bots alive, health and the speed boost,
/// ammo (rounds and spare magazines, bottom left), weapon slots, score, supplies picked up, crosshair or the sniper scope,
/// warnings and end screens. Touches a UI element only when what it shows changes, so the canvases are rebuilt rarely.
/// </summary>
public class SurvivalHud : MonoBehaviour
{
    public WaveSurvivalGame game;
    public PlayerHealth health;
    public WeaponInventory weapons;
    [Tooltip("The HUD canvas in the level (built by the editor command \"hud\"); made at run time when empty.")] public HudView view;

    /// <summary>A text rebuilt only when its key (the numbers it shows) changes.</summary>
    sealed class TextSlot
    {
        long key = long.MinValue;
        public bool Changed(long k) { if (k == key) return false; key = k; return true; }
    }

    readonly TextSlot tWave = new TextSlot(), tClock = new TextSlot(), tInfo = new TextSlot(), tHead = new TextSlot(), tStarts = new TextSlot(),
                      tHp = new TextSlot(), tKills = new TextSlot(), tRounds = new TextSlot(), tMagSize = new TextSlot(), tMags = new TextSlot(),
                      tName = new TextSlot(), tBoost = new TextSlot(), tPickup = new TextSlot(), tStatus = new TextSlot(),
                      tIcons = new TextSlot(), tSlot = new TextSlot(), tEndState = new TextSlot();
    SimpleFirstPersonController fp;
    Texture2D scopeTex;
    float scopeHalf = -1f;
    int hitState = -1;

    void Awake()
    {
        if (view == null) view = HudView.Create(transform);
        if (fp == null && health != null) fp = health.GetComponent<SimpleFirstPersonController>();
        if (weapons != null && weapons.weapons != null)
        {
            var names = new string[weapons.weapons.Length];
            for (int i = 0; i < names.Length; i++) names[i] = (i + 1) + " " + (weapons.weapons[i] != null ? weapons.weapons[i].displayName : "-");
            view.BuildSlots(names);
        }
    }

    void OnDestroy() { if (scopeTex != null) Destroy(scopeTex); }     // a new HUD comes with every restart

    // ------------------------------------------------------------------ helpers: change the UI only when needed
    static void Show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }

    /// <summary>Right edge of a bar fill, in 1/256 steps (a value that creeps every frame would rebuild its canvas every frame).</summary>
    static void Fill(RectTransform fill, float k)
    {
        k = Mathf.Round(Mathf.Clamp01(k) * 256f) / 256f;
        var m = fill.anchorMax;
        if (m.x != k) { m.x = k; fill.anchorMax = m; }
    }

    /// <summary>Scope picture: black outside the lens, a darker rim inside it, thin cross hairs with thick outer posts and mil dots.</summary>
    static Texture2D MakeScope(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f, lens = n * 0.49f, px1 = n / 1024f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((d - lens) / (2f * px1));                              // outside the lens
                a = Mathf.Max(a, Mathf.Pow(Mathf.Clamp01((d - lens * 0.80f) / (lens * 0.20f)), 2.2f) * 0.85f);   // rim shadow
                float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                bool post = (ay < 5f * px1 && ax > lens * 0.55f) || (ax < 5f * px1 && ay > lens * 0.55f);
                bool hair = (ay < 1.2f * px1 && ax > 10f * px1) || (ax < 1.2f * px1 && ay > 10f * px1);
                bool dot = false;
                for (int k = 1; k <= 4 && !dot; k++)
                {
                    float m = k * lens * 0.11f, r = 3.2f * px1;
                    dot = (Mathf.Abs(ax - m) < r && ay < r) || (Mathf.Abs(ay - m) < r && ax < r);
                }
                if ((post || hair || dot) && d < lens) a = 1f;
                px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);                 // no CPU copy kept
        return tex;
    }

    // ------------------------------------------------------------------ per frame
    void LateUpdate()
    {
        if (game == null || view == null) return;
        bool over = game.IsOver;

        // ---- sniper scope
        bool scoped = weapons != null && weapons.IsScoped && !over;
        Show(view.scopeRoot, scoped);
        if (scoped)
        {
            if (scopeTex == null) { scopeTex = MakeScope(1024); view.scope.texture = scopeTex; }
            float half = ((RectTransform)view.transform).rect.height * 0.5f;   // a square lens as high as the screen; black beside it
            if (half != scopeHalf)
            {
                scopeHalf = half;
                view.scope.rectTransform.sizeDelta = new Vector2(half * 2f, 0f);
                view.scopeLeft.offsetMax = new Vector2(1f - half, 0f); view.scopeRight.offsetMin = new Vector2(half - 1f, 0f);
            }
            Show(view.scopeHit, Time.time - weapons.LastHitTime < 0.15f);
        }

        // ---- top: wave and timer / prepare and intermission
        bool inWave = game.State == WaveSurvivalGame.GameState.Wave;
        bool prep = game.State == WaveSurvivalGame.GameState.Prepare || game.State == WaveSurvivalGame.GameState.Intermission;
        Show(view.waveGroup, inWave); Show(view.prepGroup, prep);
        if (inWave)
        {
            int secs = Mathf.CeilToInt(Mathf.Max(0f, game.TimeLeft)), nextIn = Mathf.CeilToInt(game.NextBatchIn);
            if (tWave.Changed(game.Wave * 1000L + game.waves)) view.wave.text = "WAVE " + game.Wave + " / " + game.waves;
            if (tClock.Changed(secs)) view.clock.text = (secs / 60) + ":" + (secs % 60).ToString("00");
            if (tInfo.Changed(((long)game.Alive << 40) | ((long)game.BatchSize << 20) | (uint)nextIn))
                view.info.text = "bots alive: " + game.Alive + "     next " + game.BatchSize + " bots in " + nextIn + " s";
            view.clock.color = !game.InZone ? new Color(0.6f, 0.6f, 0.6f) : game.TimeLeft < 10f ? new Color(0.5f, 1f, 0.5f) : Color.white;   // Graphic skips equal colours
            view.progressFillImage.color = game.InZone ? HudView.Gold : new Color(0.55f, 0.55f, 0.55f);
            Fill(view.progressFill, game.WaveProgress);
        }
        else if (prep)
        {
            bool first = game.State == WaveSurvivalGame.GameState.Prepare;
            int startsIn = Mathf.CeilToInt(game.TimeLeft);
            if (tHead.Changed(first ? -1 : game.Wave)) view.head.text = first ? "HOLD THE HILL" : "WAVE " + game.Wave + " SURVIVED";
            if (tStarts.Changed(game.Wave * 100000L + startsIn)) view.starts.text = "wave " + (game.Wave + 1) + " starts in " + startsIn + " s  -  stay inside the ring";
        }

        // ---- middle: warning, picked-up supply, crosshair
        Show(view.warning, !over && !game.InZone && inWave);
        float since = Time.time - SupplyPickup.LastTime;
        float alpha = since < 1.8f && SupplyPickup.LastMessage != null ? 1f - Mathf.Clamp01((since - 1.3f) / 0.5f) : 0f;
        if (view.pickupGroup.alpha != alpha) view.pickupGroup.alpha = alpha;   // a CanvasGroup fade does not rebuild the text
        if (alpha > 0f && tPickup.Changed((long)(SupplyPickup.LastTime * 1000f)))
        {
            view.pickup.text = SupplyPickup.LastMessage;
            view.pickup.color = SupplyPickup.LastKind == SupplyPickup.Kind.Medkit ? new Color(1f, 0.45f, 0.4f)
                              : SupplyPickup.LastKind == SupplyPickup.Kind.Speed ? new Color(0.4f, 1f, 0.85f) : HudView.Gold;
        }
        Show(view.crosshair, !over && !scoped);
        int hit = weapons != null && Time.time - weapons.LastHitTime < 0.12f ? 1 : 0;
        if (hit != hitState)
        {
            hitState = hit;
            Color cc = hit == 1 ? HudView.Warn : new Color(1f, 1f, 1f, 0.85f);
            foreach (var b in view.crosshairBars) if (b != null) b.color = cc;
        }

        // ---- bottom left: health, speed boost, ammo
        if (health != null)
        {
            float k = Mathf.Clamp01(health.Health / Mathf.Max(1f, health.maxHealth));
            int hp = Mathf.CeilToInt(health.Health);
            if (tHp.Changed(hp))
            {
                view.hp.text = "HP " + hp;
                view.hpFillImage.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.3f, 0.9f, 0.35f), k);
            }
            Fill(view.hpFill, k);
        }
        bool boosted = fp != null && fp.BoostTimeLeft > 0f;
        Show(view.boost, boosted);
        if (boosted)
        {
            int tenths = Mathf.CeilToInt(fp.BoostTimeLeft * 10f);
            if (tBoost.Changed(tenths)) view.boostText.text = "SPEED " + (tenths / 10) + "." + (tenths % 10) + " s";
            Fill(view.boostFill, fp.BoostTimeLeft / Mathf.Max(0.01f, fp.BoostDuration));
        }
        UpdateAmmo();

        // ---- bottom right: slots and kills
        if (weapons != null && view.slotBoxes != null && tSlot.Changed(weapons.CurrentIndex))
            for (int i = 0; i < view.slotBoxes.Length; i++)
            {
                bool cur = i == weapons.CurrentIndex;
                view.slotBoxes[i].color = cur ? new Color(1f, 0.85f, 0.4f, 0.35f) : new Color(0f, 0f, 0f, 0.35f);
                view.slotTexts[i].color = cur ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            }
        if (tKills.Changed(game.Kills)) view.kills.text = "KILLS " + game.Kills;

        // ---- end screen
        Show(view.endScreen, over);
        if (over && tEndState.Changed((int)game.State * 1000000L + game.Kills * 100L + game.Wave))
        {
            bool won = game.State == WaveSurvivalGame.GameState.Victory;
            view.endTitle.text = won ? "VICTORY" : "GAME OVER";
            view.endTitle.color = won ? new Color(0.5f, 1f, 0.5f) : HudView.Warn;
            view.endReason.text = game.EndReason;
            view.endStats.text = "wave " + Mathf.Max(1, game.Wave) + " of " + game.waves + "     kills " + game.Kills;
        }
    }

    void UpdateAmmo()
    {
        var w = weapons != null ? weapons.Current : null;
        Show(view.ammo, w != null);
        if (w == null) return;
        if (tName.Changed(weapons.CurrentIndex)) view.weaponName.text = w.displayName.ToUpperInvariant();
        if (tRounds.Changed(w.InMagazine)) view.rounds.text = w.InMagazine.ToString();
        if (tMagSize.Changed(w.magazineSize)) view.magSize.text = "/ " + w.magazineSize;
        view.rounds.color = w.InMagazine == 0 ? HudView.Warn : w.InMagazine <= Mathf.Max(1, w.magazineSize / 4) ? new Color(1f, 0.7f, 0.3f) : Color.white;

        int mags = w.Magazines;
        if (tMags.Changed(mags))
        {
            view.mags.text = mags < 0 ? "x ∞" : "x " + mags;
            view.mags.color = mags == 0 ? HudView.Warn : Color.white;
        }
        if (tIcons.Changed(((long)mags << 16) | (uint)w.maxMagazines))  // one icon per magazine the pouch can hold, lit ones are there
            for (int i = 0; i < view.magIcons.Length; i++)
            {
                bool on = mags > 0 && i < w.maxMagazines;
                Show(view.magIcons[i].gameObject, on);
                if (on) view.magIcons[i].color = i < mags ? new Color(1f, 0.85f, 0.4f, 0.95f) : new Color(1f, 1f, 1f, 0.15f);
            }

        Show(view.reload, w.IsReloading);
        if (w.IsReloading) Fill(view.reloadFill, w.ReloadProgress);
        int state = w.IsReloading ? 0 : w.InMagazine > 0 ? 1 : w.HasSpare ? 2 : 3;
        if (tStatus.Changed(state)) view.status.text = state == 2 ? "R - RELOAD" : state == 3 ? "NO AMMO - FIND MAGAZINES" : "";
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Upgrades between the waves. When a wave is held (the game goes to its break), an offer of cards comes up - each card a modifier
/// of a rarity rolled on its own, so one offer can mix them; the later the wave, the better the odds of the rare ones. The game
/// pauses while the player chooses (1-3 or a click); TAB puts the choice off: it goes to the panel on the left and TAB brings it
/// back whenever the player likes (the oldest first). What the taken modifiers add up to is RunStats (Stats), read by the guns,
/// the sword, the player and the bots; the effects of kills and gun hits (healing, speed, bursts, lightning, knockback) and the
/// last stand are here. Made by WaveSurvivalGame when the level has none; the look is UpgradeScreen.
/// </summary>
public class UpgradeSystem : MonoBehaviour
{
    public class Offer { public Modifier[] cards; public int wave; }

    [Header("Offers")]
    [Range(1, UpgradeScreen.MaxCards)] public int cardsPerOffer = 3;
    [Tooltip("Weight of each rarity in the first break: COMMON, GREAT, RARE, SWEG, SLAYER.")]
    public float[] baseWeights = { 55f, 28f, 11f, 4.5f, 1.5f };
    [Tooltip("Added to each weight for every wave after the first (the odds move up the rarities).")]
    public float[] weightPerWave = { -7f, 0f, 3f, 2f, 1f };
    [Tooltip("Puts the choice off / brings a choice put off back.")] public KeyCode laterKey = KeyCode.Tab;
    [Tooltip("Offers go straight to the panel on the left without pausing the game (scripted runs, tests that measure the game).")]
    public bool deferOffers;

    [Header("This run")]
    [Tooltip("What the modifiers taken so far add up to.")] public RunStats stats;
    public List<string> taken = new List<string>();

    public static UpgradeSystem Instance { get; private set; }
    /// <summary>The run's stats (all neutral when there is no system).</summary>
    public static RunStats Stats { get; private set; } = new RunStats();
    /// <summary>An offer is on the screen (the game is paused).</summary>
    public static bool IsChoosing { get { return Instance != null && Instance.current != null; } }
    public Offer Current { get { return current; } }
    public List<Offer> Waiting { get { return waiting; } }
    public UpgradeScreen Screen { get { return screen; } }
    public bool Busy { get { return screen != null && screen.Busy; } }
    public int Offered { get; private set; }

    Offer current;
    readonly List<Offer> waiting = new List<Offer>();
    UpgradeScreen screen;
    WaveSurvivalGame game;
    WaveSurvivalGame.GameState lastState;
    PlayerHealth health; WeaponInventory inv; SimpleFirstPersonController fp;
    float timeScaleBefore = 1f, untouchableUntil = -1f;
    bool lastStandUsed, overShown;

    void Awake()
    {
        Instance = this;
        Stats = new RunStats(); stats = Stats;
        var go = new GameObject("Upgrade Screen"); go.transform.SetParent(transform, false);
        screen = go.AddComponent<UpgradeScreen>();
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        if (current != null) Time.timeScale = timeScaleBefore;
        Instance = null; Stats = new RunStats();
    }

    void Start()
    {
        game = GetComponent<WaveSurvivalGame>(); if (game == null) game = WaveSurvivalGame.Instance;
        if (game != null) { lastState = game.State; FindPlayer(game.player); }
    }

    void FindPlayer(Transform p)
    {
        if (p == null) return;
        health = p.GetComponent<PlayerHealth>(); inv = p.GetComponent<WeaponInventory>(); fp = p.GetComponent<SimpleFirstPersonController>();
    }

    void Update()
    {
        if (game != null)
        {
            var st = game.State;
            if (lastState == WaveSurvivalGame.GameState.Wave && st == WaveSurvivalGame.GameState.Intermission) OfferAfterWave(game.Wave);   // a wave held
            if (st == WaveSurvivalGame.GameState.Wave && lastState != WaveSurvivalGame.GameState.Wave) lastStandUsed = false;
            lastState = st;
        }

        if (current != null)
        {
            if (Busy) return;
            for (int i = 0; i < current.cards.Length && i < 9; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { Choose(i); return; }
            if (Input.GetMouseButtonDown(0) && screen.Hovered >= 0) { Choose(screen.Hovered); return; }
            if (Input.GetKeyDown(laterKey)) Later();
            return;
        }
        bool over = game != null && game.IsOver;
        if (over != overShown) { overShown = over; RefreshWaiting(); }     // the game is over: the waiting choices are of no use now
        if (waiting.Count > 0 && !over && Input.GetKeyDown(laterKey) && Cursor.lockState == CursorLockMode.Locked && !SettingsMenu.IsOpen) OpenWaiting();
    }

    // ------------------------------------------------------------------ offers
    /// <summary>A new offer after this wave: on the screen now (or waiting, if one is being chosen).</summary>
    public void OfferAfterWave(int wave)
    {
        var o = Roll(wave); Offered++;
        if (current != null || deferOffers) { waiting.Add(o); RefreshWaiting(); } else OpenOffer(o);
    }

    /// <summary>The cards of an offer after this wave (no side effects).</summary>
    public Offer Roll(int wave)
    {
        var o = new Offer { wave = wave, cards = new Modifier[Mathf.Clamp(cardsPerOffer, 1, UpgradeScreen.MaxCards)] };
        for (int i = 0; i < o.cards.Length; i++) o.cards[i] = Draw(RollRarity(wave), o.cards, i);
        return o;
    }

    public float Weight(Rarity r, int wave)
    {
        int k = (int)r;
        float b = baseWeights != null && k < baseWeights.Length ? baseWeights[k] : 0f, w = weightPerWave != null && k < weightPerWave.Length ? weightPerWave[k] : 0f;
        return Mathf.Max(0f, b + w * Mathf.Max(0, wave - 1));
    }

    Rarity RollRarity(int wave)
    {
        float total = 0f;
        for (int r = 0; r <= (int)Rarity.Slayer; r++) total += Weight((Rarity)r, wave);
        float x = Random.value * total;
        for (int r = 0; r <= (int)Rarity.Slayer; r++) { x -= Weight((Rarity)r, wave); if (x < 0f) return (Rarity)r; }
        return Rarity.Common;
    }

    static readonly List<Modifier> pool = new List<Modifier>();

    /// <summary>A modifier of rarity r not already on this offer (and not a unique one already taken); a step down, then up, if none is left.</summary>
    Modifier Draw(Rarity r, Modifier[] already, int count)
    {
        for (int step = 0; step <= 8; step++)
        {
            int k = (int)r + (step <= (int)r ? -step : step - (int)r);
            if (k < 0 || k > (int)Rarity.Slayer) continue;
            pool.Clear();
            foreach (var m in ModifierCatalog.All)
            {
                if ((int)m.rarity != k || (m.unique && taken.Contains(m.id))) continue;
                bool dup = false; for (int i = 0; i < count; i++) if (already[i] == m) dup = true;
                if (!dup) pool.Add(m);
            }
            if (pool.Count > 0) return pool[Random.Range(0, pool.Count)];
        }
        return ModifierCatalog.All[0];
    }

    /// <summary>Puts this offer on the screen now and pauses the game (tests can show any cards).</summary>
    public void OpenOffer(Offer o)
    {
        current = o;
        timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f; Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        string key = laterKey.ToString().ToUpperInvariant();
        string footer = "[1-" + o.cards.Length + "] or click  -  take          [" + key + "]  -  later";
        screen.Show(o.cards, "WAVE " + o.wave + " HELD", footer);
        RefreshWaiting();
    }

    /// <summary>TAB: the oldest choice put off comes back.</summary>
    public void OpenWaiting()
    {
        if (current != null || waiting.Count == 0) return;
        var o = waiting[0]; waiting.RemoveAt(0);
        OpenOffer(o);
    }

    /// <summary>Takes card i of the offer on the screen.</summary>
    public void Choose(int i)
    {
        if (current == null || Busy || i < 0 || i >= current.cards.Length) return;
        Apply(current.cards[i]);
        screen.PlayPick(i, Close);
    }

    /// <summary>TAB: the offer goes to the panel on the left, to be chosen later.</summary>
    public void Later()
    {
        if (current == null || Busy) return;
        waiting.Add(current);
        screen.PlayLater(() => { Close(); RefreshWaiting(); });
    }

    void Close()
    {
        current = null;
        Time.timeScale = timeScaleBefore;
        bool over = game != null && game.IsOver;
        if (!over && !SettingsMenu.IsOpen) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        RefreshWaiting();
    }

    void RefreshWaiting()
    {
        var mods = new List<Modifier[]>(); var waves = new List<int>();
        if (!overShown) foreach (var o in waiting) { mods.Add(o.cards); waves.Add(o.wave); }
        screen.SetWaiting(mods, waves, laterKey.ToString().ToUpperInvariant());
    }

    // ------------------------------------------------------------------ taking a modifier
    public void Apply(Modifier m)
    {
        if (m == null) return;
        if (health == null && game != null) FindPlayer(game.player);
        m.apply(Stats, this);
        taken.Add(m.id);
    }

    /// <summary>Tests: takes a modifier by id at once (no screen).</summary>
    public bool ApplyById(string id) { var m = ModifierCatalog.Find(id); if (m == null) return false; Apply(m); return true; }

    /// <summary>Tests: back to a fresh run's stats (what was done to the player and the guns stays).</summary>
    public void ResetStats() { Stats = new RunStats(); stats = Stats; taken.Clear(); lastStandUsed = false; untouchableUntil = -1f; }

    public void AddMaxHealth(float hp)
    {
        if (health == null) return;
        health.maxHealth += hp; health.Heal(hp);
    }

    public void ScaleMagazines(float k)
    {
        Stats.magazine *= k;
        if (inv == null || inv.weapons == null) return;
        foreach (var w in inv.weapons) { var g = w as Weapon; if (g != null) g.ScaleMagazine(k); }
    }

    // ------------------------------------------------------------------ hooks
    /// <summary>PlayerHealth: what a blow really takes (armour, the last stand, a moment untouchable after it).</summary>
    public static float PlayerDamage(PlayerHealth h, float amount)
    {
        var s = Instance;
        if (s == null) return amount;
        if (Time.time < s.untouchableUntil) return 0f;
        amount *= Stats.damageTaken;
        if (Stats.lastStand && !s.lastStandUsed && amount >= h.Health && h.Health > 0f)
        {
            s.lastStandUsed = true; s.untouchableUntil = Time.time + 2f;
            return Mathf.Max(0f, h.Health - 1f);
        }
        return amount;
    }

    public bool LastStandUsed { get { return lastStandUsed; } }

    /// <summary>WaveSurvivalGame: a bot killed by the player (or the cannon).</summary>
    public void OnKill(EnemyBot bot)
    {
        if (health == null && game != null) FindPlayer(game.player);
        float heal = Stats.killHeal + (health != null ? Stats.killHealPercent * health.maxHealth : 0f);
        if (heal > 0f && health != null) health.Heal(heal);
        if (Stats.killBoost > 0f && fp != null) fp.ApplySpeedBoost(1.3f, Mathf.Max(fp.BoostTimeLeft, Stats.killBoost));
        if (Stats.killRefill && inv != null && inv.CurrentGun != null) inv.CurrentGun.FillMagazine();
    }

    static readonly List<EnemyBot> struck = new List<EnemyBot>(8);

    /// <summary>Weapon: a round hit this bot at point (going dir) for damage: knockback, burst, lightning.</summary>
    public static void OnGunHit(EnemyBot bot, Vector3 point, Vector3 dir, float damage)
    {
        var s = Instance; var st = Stats;
        if (st.hitKnockback > 0f && !bot.IsDying) { Vector3 d = dir; d.y = 0f; bot.Knockback(d.normalized * st.hitKnockback, 0.25f); }
        if (st.burstShare > 0f && st.burstRadius > 0f)
        {
            float r2 = st.burstRadius * st.burstRadius;
            for (int i = 0; i < EnemyBot.All.Count; i++)
            {
                var b = EnemyBot.All[i];
                if (b == bot || b.IsDying || b.body == null) continue;
                if ((b.body.bounds.ClosestPoint(point) - point).sqrMagnitude <= r2) b.TakeDamage(damage * st.burstShare);
            }
            if (s != null) s.Burst(point, st.burstRadius);
        }
        if (st.chainTargets > 0)
        {
            struck.Clear(); struck.Add(bot);
            Vector3 from = bot.body != null ? bot.body.bounds.center : point;
            for (int n = 0; n < st.chainTargets; n++)
            {
                EnemyBot next = null; float best = st.chainRange * st.chainRange;
                for (int i = 0; i < EnemyBot.All.Count; i++)
                {
                    var b = EnemyBot.All[i];
                    if (b.IsDying || b.body == null || struck.Contains(b)) continue;
                    float d2 = (b.body.bounds.center - from).sqrMagnitude;
                    if (d2 < best) { best = d2; next = b; }
                }
                if (next == null) break;
                Vector3 to = next.body.bounds.center;
                if (s != null) s.Arc(from, to);
                struck.Add(next); next.TakeDamage(damage * st.chainShare);
                from = to;
            }
        }
    }

    // ------------------------------------------------------------------ effects (pooled, in the world)
    class Fx { public GameObject go; public Renderer r; public LineRenderer line; public Light light; public float start, life, size; public bool on; }
    readonly List<Fx> arcs = new List<Fx>(), bursts = new List<Fx>();
    Material arcMat, burstMat;
    bool fxLive;
    MaterialPropertyBlock block;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    Material FxMaterial(Color c, float intensity, Texture2D tex)
    {
        var sh = Shader.Find("ProjectGame/MuzzleFlash");
        if (sh == null) return null;
        var m = new Material(sh) { hideFlags = HideFlags.DontSave };
        m.SetColor(ColorId, c); m.SetFloat(IntensityId, intensity); if (tex != null) m.SetTexture("_MainTex", tex);
        return m;
    }

    Fx Take(List<Fx> list, bool line)
    {
        foreach (var f in list) if (!f.on) return f;
        if (list.Count >= 8) return list[0];
        var fx = new Fx { go = new GameObject(line ? "UpgradeArc" : "UpgradeBurst") };
        fx.go.transform.SetParent(transform, false);
        if (line)
        {
            if (arcMat == null) arcMat = FxMaterial(new Color(0.55f, 0.8f, 1f, 1f), 2.6f, null);
            fx.line = fx.go.AddComponent<LineRenderer>(); fx.line.sharedMaterial = arcMat; fx.line.positionCount = 9; fx.line.useWorldSpace = true;
            fx.line.widthMultiplier = 0.07f; fx.line.numCapVertices = 2; fx.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fx.r = fx.line;
        }
        else
        {
            if (burstMat == null) burstMat = FxMaterial(new Color(1f, 0.6f, 0.25f, 1f), 2.2f, UpgradeArt.SoftDot);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(fx.go.transform, false);
            fx.r = q.GetComponent<MeshRenderer>(); fx.r.sharedMaterial = burstMat; fx.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fx.r.receiveShadows = false;
            fx.light = fx.go.AddComponent<Light>(); fx.light.type = LightType.Point; fx.light.color = new Color(1f, 0.6f, 0.3f); fx.light.shadows = LightShadows.None;
        }
        list.Add(fx);
        return fx;
    }

    void Arc(Vector3 a, Vector3 b)
    {
        var f = Take(arcs, true); f.on = true; fxLive = true; f.start = Time.time; f.life = 0.16f; f.go.SetActive(true);
        Vector3 d = b - a, side = Vector3.Cross(d, Vector3.up).normalized; if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        for (int i = 0; i < 9; i++)
        {
            float k = i / 8f, j = (i == 0 || i == 8) ? 0f : Random.Range(-0.35f, 0.35f);
            f.line.SetPosition(i, a + d * k + side * j + Vector3.up * Random.Range(-0.25f, 0.25f) * (i == 0 || i == 8 ? 0f : 1f));
        }
    }

    void Burst(Vector3 at, float radius)
    {
        var f = Take(bursts, false); f.on = true; fxLive = true; f.start = Time.time; f.life = 0.2f; f.size = radius * 2f; f.go.SetActive(true);
        f.go.transform.position = at; f.light.range = radius * 3f;
    }

    void LateUpdate()
    {
        if (!fxLive) return;                                           // nothing flashing: nothing to do
        fxLive = false;
        if (block == null) block = new MaterialPropertyBlock();
        var cam = inv != null && inv.cam != null ? inv.cam : Camera.main;
        for (int i = 0; i < arcs.Count; i++)
        {
            var f = arcs[i]; if (!f.on) continue;
            float k = (Time.time - f.start) / f.life;
            if (k >= 1f) { f.on = false; f.go.SetActive(false); continue; }
            Color c = new Color(1f, 1f, 1f, 1f - k); f.line.startColor = c; f.line.endColor = c; fxLive = true;
        }
        for (int i = 0; i < bursts.Count; i++)
        {
            var f = bursts[i]; if (!f.on) continue;
            float k = (Time.time - f.start) / f.life;
            if (k >= 1f) { f.on = false; f.go.SetActive(false); continue; }
            var q = f.r.transform;
            q.localScale = Vector3.one * f.size * (0.4f + 0.6f * Mathf.Sqrt(k));
            if (cam != null) q.rotation = cam.transform.rotation;
            f.r.GetPropertyBlock(block); block.SetFloat(IntensityId, 2.4f * (1f - k)); f.r.SetPropertyBlock(block);
            f.light.intensity = 3f * (1f - k); fxLive = true;
        }
    }
}

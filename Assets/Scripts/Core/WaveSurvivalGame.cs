using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Arcade mode: hold the hill through a few timed waves of bots.
///  - the wave timer (your progress) only runs while you stand inside the circle on the summit;
///    outside, the waves go on — bots keep coming every batchInterval seconds — but your progress is frozen;
///  - every batchInterval seconds a batch of bots comes over the ridge: mobsPerBatch * wave number;
///  - each wave the bots get more health, speed, damage and size; bots left over carry on into the next wave;
///  - the game is lost when the player dies, won when the last wave's timer runs out.
/// </summary>
public class WaveSurvivalGame : MonoBehaviour
{
    public enum GameState { Prepare, Wave, Intermission, GameOver, Victory }

    [Header("Waves")]
    public int waves = 5;
    public float waveDuration = 90f;
    public float batchInterval = 30f;
    public int mobsPerBatch = 10;
    public float intermission = 10f;
    public float startDelay = 6f;
    [Tooltip("No new bots while this many are alive (performance guard).")] public int maxAlive = 220;

    [Header("Bots")]
    public EnemyBot botPrefab;
    [Tooltip("Distance from the hill at which bots appear (up on the boundary ridge).")] public float spawnRadius = 250f;
    public float baseHealth = 40f, healthPerWave = 0.2f;
    public float baseSpeed = 7.5f, speedPerWave = 0.35f;
    public float baseDamage = 4f, damagePerWave = 1.5f;
    public Color[] waveColors = { new Color(1f, 0.55f, 0.1f), new Color(1f, 0.25f, 0.1f), new Color(1f, 0.1f, 0.35f), new Color(0.8f, 0.15f, 1f), new Color(0.3f, 0.5f, 1.4f) };

    [Header("Rules")]
    public HillZone zone;
    public Transform player;
    [Tooltip("Health the player gets back for every bot killed.")] public float killHeal = 3f;
    public float mapHalfSize = 292f;

    [Header("Ammo")]
    public AmmoPickup ammoPickupPrefab;
    [Tooltip("Chance that a killed bot drops a magazine.")] [Range(0f, 1f)] public float dropChance = 0.3f;
    [Tooltip("Magazines kept lying inside the circle during a wave.")] public int summitMagazines = 2;
    [Tooltip("Seconds between new magazines inside the circle.")] public float summitMagazineInterval = 20f;

    public static WaveSurvivalGame Instance { get; private set; }
    public GameState State { get; private set; }
    public int Wave { get; private set; }
    public float TimeLeft { get; private set; }
    public float NextBatchIn { get; private set; }
    /// <summary>Seconds the player has been outside the circle (0 inside).</summary>
    public float OutOfZone { get; private set; }
    public bool InZone { get; private set; } = true;
    public int Kills { get; private set; }
    public int Spawned { get; private set; }
    public string EndReason { get; private set; }
    public bool IsOver { get { return State == GameState.GameOver || State == GameState.Victory; } }
    public int Alive { get { return EnemyBot.All.Count; } }
    public int BatchSize { get { return mobsPerBatch * Mathf.Max(1, Wave); } }
    /// <summary>0..1 progress of the current wave.</summary>
    public float WaveProgress { get { return State == GameState.Wave ? Mathf.Clamp01(1f - TimeLeft / Mathf.Max(1f, waveDuration)) : 0f; } }

    PlayerHealth health;
    float batchTimer; int toSpawn; float spawnTick; float batchAngle; float summitAmmoTimer;

    void Awake()
    {
        Instance = this;
        State = GameState.Prepare; TimeLeft = startDelay; Wave = 0;
        if (player != null) health = player.GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (IsOver)
        {
            if (Input.GetKeyDown(KeyCode.R)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }
        float dt = Time.deltaTime;

        InZone = zone == null || player == null || zone.Contains(player.position);
        OutOfZone = InZone ? 0f : OutOfZone + dt;
        if (zone != null) zone.SetDanger(InZone ? 0f : 1f);
        if (health != null && health.IsDead) { End(false, "You were overrun"); return; }

        switch (State)
        {
            case GameState.Prepare:
            case GameState.Intermission:
                TimeLeft -= dt;
                if (TimeLeft <= 0f) StartWave(Wave + 1);
                break;
            case GameState.Wave:
                batchTimer -= dt; NextBatchIn = Mathf.Max(0f, batchTimer);              // the bots do not wait for you
                if (batchTimer <= 0f) { StartBatch(); batchTimer += batchInterval; }
                if (InZone) TimeLeft -= dt;                                              // progress only on the hill
                summitAmmoTimer -= dt;
                if (summitAmmoTimer <= 0f) { summitAmmoTimer = summitMagazineInterval; SpawnSummitMagazine(); }
                if (TimeLeft <= 0f)
                {
                    if (Wave >= waves) { End(true, "The hill is yours"); ClearBots(); }
                    else { State = GameState.Intermission; TimeLeft = intermission; }
                }
                break;
        }
        if (toSpawn > 0 && Time.time >= spawnTick && Alive < maxAlive && !IsOver) { SpawnBot(); toSpawn--; spawnTick = Time.time + 0.25f; }
    }

    void StartWave(int wave)
    {
        Wave = wave; State = GameState.Wave; TimeLeft = waveDuration; batchTimer = 0f;
    }

    void StartBatch()
    {
        toSpawn += BatchSize;
        batchAngle = Random.Range(0f, 360f);                           // each batch arrives from three directions
    }

    void SpawnBot()
    {
        if (botPrefab == null) return;
        float a = (batchAngle + (Spawned % 3) * 120f + Random.Range(-28f, 28f)) * Mathf.Deg2Rad;
        Vector3 centre = zone != null ? zone.transform.position : Vector3.zero;
        Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (spawnRadius + Random.Range(-10f, 8f));
        p.x = Mathf.Clamp(p.x, -mapHalfSize, mapHalfSize); p.z = Mathf.Clamp(p.z, -mapHalfSize, mapHalfSize);
        var t = Terrain.activeTerrain;
        p.y = (t != null ? t.SampleHeight(p) + t.transform.position.y : centre.y) + 0.2f;
        var bot = Instantiate(botPrefab, p, Quaternion.LookRotation(new Vector3(centre.x - p.x, 0f, centre.z - p.z)));
        int w = Mathf.Max(1, Wave) - 1;
        Color c = waveColors != null && waveColors.Length > 0 ? waveColors[Mathf.Min(w, waveColors.Length - 1)] : Color.red;
        bot.Init(player, baseHealth * (1f + healthPerWave * w), baseSpeed + speedPerWave * w, baseDamage + damagePerWave * w, 1f + 0.08f * w, c * 1.6f);
        Spawned++;
    }

    void ClearBots()
    {
        toSpawn = 0;
        for (int i = EnemyBot.All.Count - 1; i >= 0; i--) EnemyBot.All[i].Die(false);
    }

    public void OnBotKilled(EnemyBot bot)
    {
        Kills++;
        if (health != null) health.Heal(killHeal);
        if (ammoPickupPrefab != null && bot != null && Random.value < dropChance) AmmoPickup.Spawn(ammoPickupPrefab, bot.transform.position);
    }

    void SpawnSummitMagazine()
    {
        if (ammoPickupPrefab == null || zone == null) return;
        int onSummit = 0;
        foreach (var p in AmmoPickup.All) if (p.OnSummit) onSummit++;
        if (onSummit >= summitMagazines) return;
        Vector2 r = Random.insideUnitCircle * zone.radius * 0.75f;
        var pick = AmmoPickup.Spawn(ammoPickupPrefab, zone.transform.position + new Vector3(r.x, 0f, r.y));
        if (pick != null) { pick.OnSummit = true; pick.lifetime = 1e6f; }      // the summit ones wait for you
    }

    void End(bool won, string reason)
    {
        State = won ? GameState.Victory : GameState.GameOver; EndReason = reason; toSpawn = 0;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
}

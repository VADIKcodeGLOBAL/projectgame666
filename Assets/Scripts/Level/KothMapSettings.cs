using UnityEngine;

/// <summary>
/// Parameters of the hill map and of the wave survival mode played on it. Edit the asset (Assets/Data/Levels/KothMapSettings.asset)
/// and press Generate in Tools > Level > King of the Hill Generator.
/// </summary>
[CreateAssetMenu(fileName = "KothMapSettings", menuName = "ProjectGame/KotH Map Settings")]
public class KothMapSettings : ScriptableObject
{
    [Header("Map")]
    public int seed = 7;
    [Tooltip("Side of the square terrain, metres.")] public float mapSize = 600f;
    [Tooltip("Radius of the playable valley; the rim ridge starts here.")] public float playRadius = 235f;
    [Tooltip("Terrain height range, metres.")] public float maxHeight = 130f;
    public int heightmapResolution = 513;
    [Tooltip("Height of the valley floor.")] public float baseLevel = 14f;
    [Tooltip("How high the boundary ridge rises above the valley.")] public float rimHeight = 52f;

    [Header("Hill")]
    public float hillRadius = 110f;
    public float hillHeight = 34f;
    [Tooltip("Flat top of the hill, metres. The capture zone sits here.")] public float plateauRadius = 13f;
    [Tooltip("Height of the rock bands between the routes; 0 = no rock bands (open, smooth hill).")] public float cliffHeight = 0f;

    [Header("Routes and spawns")]
    [Range(2, 6)] public int routes = 4;
    [Tooltip("Nominal distance from the hill centre to a spawn; the generator moves spawns outward to equalise travel time.")]
    public float spawnRadius = 190f;
    public float pathHalfWidth = 1.8f;

    [Header("Zone and waves")]
    [Tooltip("Radius of the zone on the summit the player has to stay in.")] public float captureRadius = 12f;
    [Range(1, 12)] public int waves = 5;
    [Tooltip("Length of a wave, seconds.")] public float waveDuration = 90f;
    [Tooltip("A batch of bots arrives every this many seconds.")] public float batchInterval = 30f;
    [Tooltip("Bots per batch in wave 1; wave N gets N times as many.")] public int mobsPerBatch = 10;
    [Tooltip("Length of a full day / night cycle, seconds.")] public float dayLength = 240f;

    [Header("Nature")]
    [Range(0f, 1f)] public float forestDensity = 0.5f;
    public int treeBudget = 5200;
    public int scatteredRocks = 70;
    [Range(0f, 1f)] public float grassDensity = 0.8f;
    public bool lake = true;
    public float lakeRadius = 34f;

    [Header("Backdrop")]
    public bool backdrop = true;
    public float backdropPeakHeight = 420f;
    public float backdropRadius = 3000f;
}

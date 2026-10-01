using UnityEngine;

/// <summary>
/// Automatic hitscan rifle: hold LMB (with the mouse captured). Tracer, muzzle flash and a random shot sound per round.
/// Bots are hit on their visible box, not on the movement capsule; the world (terrain, rocks) is hit through physics.
/// </summary>
public class PlayerGun : MonoBehaviour
{
    public Camera cam;
    public Transform muzzle;
    public float damage = 40f;
    public float shotsPerSecond = 10f;
    public float range = 600f;
    [Tooltip("Random spread, degrees.")] public float spread = 0.3f;
    public AudioSource audioSource;
    public AudioClip[] shotClips;
    [Tooltip("Shot sounds playing at once; the oldest one is cut when a new round needs a voice.")] public int voices = 6;
    public LineRenderer tracer;
    public Light flash;

    public int ShotsFired { get; private set; }
    public int Hits { get; private set; }
    public float LastHitTime { get; private set; }

    float nextShot, effectOff;
    bool triggerArmed, testActive, testHeld;
    Vector3 restPos;
    AudioSource[] voicePool; int nextVoice;

    void Awake()
    {
        LastHitTime = -10f;
        if (muzzle != null && muzzle.parent != null) restPos = muzzle.parent.localPosition;
        if (tracer != null) tracer.enabled = false;
        if (flash != null)
        {
            // a pixel point light adds a whole extra pass to the terrain, grass and every bot in range; per-vertex it is free
            flash.enabled = false; flash.shadows = LightShadows.None; flash.renderMode = LightRenderMode.ForceVertex;
        }
        if (audioSource != null)
        {
            // one AudioSource per voice: changing the pitch of a shared source re-pitches the tails of the previous rounds too
            voicePool = new AudioSource[Mathf.Max(1, voices)];
            voicePool[0] = audioSource;
            for (int i = 1; i < voicePool.Length; i++)
            {
                var a = gameObject.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = audioSource.spatialBlend; a.outputAudioMixerGroup = audioSource.outputAudioMixerGroup; a.priority = audioSource.priority;
                voicePool[i] = a;
            }
        }
    }

    /// <summary>Holds the trigger without a mouse (automated tests). ClearTestTrigger gives control back.</summary>
    public void SetTestTrigger(bool held) { testActive = true; testHeld = held; }
    public void ClearTestTrigger() { testActive = false; testHeld = false; }

    // LateUpdate: the mouse look of this frame is already applied, so the round goes where the crosshair is
    // and the tracer starts at the barrel where it is drawn this frame
    void LateUpdate()
    {
        if (Time.time > effectOff)
        {
            if (tracer != null) tracer.enabled = false;
            if (flash != null) flash.enabled = false;
        }
        if (muzzle != null && muzzle.parent != null)                 // the gun settles back after the kick
            muzzle.parent.localPosition = Vector3.Lerp(muzzle.parent.localPosition, restPos, Time.deltaTime * 14f);

        bool held;
        if (testActive) held = testHeld;
        else
        {
            // the click that captures the mouse is not a shot: the trigger arms once LMB is released with the mouse captured
            bool locked = Cursor.lockState == CursorLockMode.Locked, down = Input.GetMouseButton(0);
            if (!locked) triggerArmed = false; else if (!down) triggerArmed = true;
            held = locked && triggerArmed && down;
        }
        var game = WaveSurvivalGame.Instance;
        if (game != null && game.IsOver) held = false;

        float now = Time.time, interval = 1f / Mathf.Max(0.1f, shotsPerSecond);
        if (!held) { if (nextShot < now) nextShot = now; return; }
        // fixed cadence independent of the frame rate: the leftover time carries over to the next round
        // (counting from Time.time each shot rounded every interval up to whole frames: 8.6 rounds/s instead of 10 at 60 fps)
        for (int n = 0; now >= nextShot && n < 4; n++) { Fire(); nextShot += interval; }
        if (nextShot < now) nextShot = now + interval;               // after a long hitch: no burst of make-up rounds
    }

    /// <summary>One round along the camera's forward. Public so tests and scripted events can fire it.</summary>
    public void Fire()
    {
        if (cam == null) return;
        ShotsFired++;
        Vector3 dir = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * Vector3.forward;
        dir = cam.transform.TransformDirection(dir);
        Vector3 origin = cam.transform.position;
        float dist = range;
        RaycastHit hit;
        // bots sit on the Ignore Raycast layer, so this hits only the world
        if (Physics.Raycast(origin, dir, out hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) dist = hit.distance;
        var bot = EnemyBot.RaycastBodies(origin, dir, ref dist);
        if (bot != null) { bot.TakeDamage(damage); Hits++; LastHitTime = Time.time; }
        Vector3 end = origin + dir * dist;

        Vector3 from = muzzle != null ? muzzle.position : origin;
        if (tracer != null) { tracer.enabled = true; tracer.SetPosition(0, from); tracer.SetPosition(1, end); }
        if (flash != null) flash.enabled = true;
        effectOff = Time.time + 0.035f;
        if (muzzle != null && muzzle.parent != null) muzzle.parent.localPosition = restPos + new Vector3(0f, 0.004f, -0.035f);
        if (voicePool != null && shotClips != null && shotClips.Length > 0)
        {
            var a = voicePool[nextVoice]; nextVoice = (nextVoice + 1) % voicePool.Length;
            a.Stop(); a.clip = shotClips[Random.Range(0, shotClips.Length)];
            a.pitch = Random.Range(0.94f, 1.06f); a.volume = 0.55f; a.Play();
        }
    }
}

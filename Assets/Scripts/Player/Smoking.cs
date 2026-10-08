using System.Collections;
using UnityEngine;

/// <summary>
/// A cigarette, whatever the hands hold (C). A hand of its own (a placeholder arm on the view model, like the weapons' arms)
/// comes up with the pack, flips the lid, shakes one out and brings it to the lips, which take it; then the lighter: the lid
/// clicks open, the flint is struck, the flame (with its light) goes to the tip and the first drag lights it. Then drags until
/// it is burnt down: while drawing the ember glows bright and the paper burns faster, the smoke is breathed out after each drag,
/// and a thin wisp rises from the tip all the time. Burnt down to the filter, the hand takes the butt and flicks it away
/// (it falls into the world and goes out). C during the drags flicks it away early.
/// The weapons are not touched: they fire, reload and switch as usual meanwhile.
/// Poses are in the space of the view model camera (this object); the smoke is in the world, drawn by the player camera.
/// Built by SmokingSetup (part of WeaponSetup.BuildPlayerWeapons); the sounds are synthesised.
/// </summary>
public class Smoking : MonoBehaviour
{
    public enum Phase { None, Pack, Lighter, Lighting, Smoking, Finishing }

    [Header("Rig (SmokingSetup)")]
    [Tooltip("The smoking hand: the pack and the lighter are its children; the arm holds on to it.")] public Transform hand;
    public ViewModelArms arm;
    public Transform pack, packLid;
    [Tooltip("The cigarette that slides out of the pack (a copy of the smoked one, never lit).")] public Transform packCigarette;
    public Transform lighter, lighterLid;
    public Renderer flame;
    public Light flameLight;
    [Tooltip("The smoked cigarette: its origin is the end of the filter, +Y runs to the tip.")] public Transform cigarette;
    public Transform paper, ember, ash;
    public Renderer emberRenderer, glow;
    public ParticleSystem tipSmoke, breath;
    public AudioSource audioSource;

    [Header("Cigarette")]
    public float filterLength = 0.025f;
    [Tooltip("Tobacco of a new cigarette, metres.")] public float paperLength = 0.058f;
    [Tooltip("Tobacco left when it is thrown away, metres.")] public float stubLength = 0.007f;
    [Tooltip("Drags until it is burnt down.")] [Min(1)] public int drags = 8;
    [Tooltip("Pause between drags, seconds (random between the two).")] public Vector2 pause = new Vector2(2.5f, 4.5f);
    public float inhaleTime = 1.1f, holdTime = 0.4f, exhaleTime = 1.6f;
    [Tooltip("Part of the burning done between the drags; the rest goes while drawing.")] [Range(0f, 1f)] public float idleBurnShare = 0.25f;
    public float volume = 0.7f;

    public Phase Current { get; private set; }
    public bool IsSmoking { get { return Current != Phase.None; } }
    public int DragsDone { get; private set; }
    public bool IsDrawing { get; private set; }
    public bool IsExhaling { get; private set; }
    public bool FlameOn { get; private set; }
    public bool Lit { get; private set; }
    /// <summary>0..1 of the tobacco still there.</summary>
    public float Left { get { return Mathf.Clamp01((paperLength - burnt) / Mathf.Max(0.001f, paperLength)); } }
    public int Finished { get; private set; }
    public int BreathPuffs { get; private set; }
    /// <summary>The last butt flicked away (tests).</summary>
    public GameObject LastButt { get; private set; }

    // the mouth: where the filter sits, and the way the cigarette points from it (camera space: x right, y up, z forward)
    static readonly Vector3 Mouth = new Vector3(0.006f, -0.076f, 0.074f);   // a little high and forward: the cigarette shows
    static readonly Vector3 CigDir = new Vector3(0.14f, -0.17f, 1f).normalized;
    static readonly Vector3 HandDown = new Vector3(-0.17f, -0.62f, 0.22f);
    const float PackHold = 0.025f, LighterHold = 0.02f;              // the hand holds the pack / the lighter this far above its bottom
    const float PackTop = 0.084f, CigOut = 0.032f;                   // filter tops in the pack; how far the shaken one comes out
    static readonly Vector3 FlameTip = new Vector3(-0.005f, 0.074f, 0f);   // lighter space: the hot top of the flame
    const float EmberLength = 0.004f;

    static AudioClip click, strike, flare, inhale, exhale, flick;
    float burnt, ashLength, glowNow, glowWant, idleRate, flameT;
    bool finishEarly, handShown;
    Coroutine run;
    MaterialPropertyBlock block;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), IntensityId = Shader.PropertyToID("_Intensity");
    static readonly Color EmberColor = new Color(1f, 0.32f, 0.06f);

    void Awake()
    {
        block = new MaterialPropertyBlock();
        if (click == null) MakeSounds();
        ResetCigarette(); ShowHand(false); Show(pack, false); Show(lighter, false); Show(cigarette, false);
        SetFlame(false); SetEmissions(0f);
        if (tipSmoke != null) tipSmoke.Play();                         // both run all the time with nothing to emit:
        if (breath != null) breath.Play();                             // the wisp gets a rate, the breath single puffs
    }

    void OnDisable()                                                   // cut short: everything back in the pocket
    {
        if (run != null) { StopCoroutine(run); run = null; }
        Current = Phase.None;
        if (hand == null || cigarette == null) return;
        ShowHand(false); Show(pack, false); Show(lighter, false); SetFlame(false);
        Show(cigarette, false); ResetCigarette();
    }

    /// <summary>C: light one up when not smoking; while smoking (the drags), throw it away now.</summary>
    public void Toggle()
    {
        if (Current == Phase.None) run = StartCoroutine(Run());
        else if (Current == Phase.Smoking) finishEarly = true;
    }

    void Update()
    {
        var game = WaveSurvivalGame.Instance;
        bool over = game != null && game.IsOver;
        if (Cursor.lockState == CursorLockMode.Locked && !SettingsMenu.IsOpen && !over && Input.GetKeyDown(KeyCode.C)) Toggle();

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (Current == Phase.None && glowNow <= 0f && !FlameOn) return;  // nothing in hand, nothing glowing: nothing to do
        if (Lit)
        {
            float rate = IsDrawing ? (paperLength - stubLength) * (1f - idleBurnShare) / Mathf.Max(1, drags) / Mathf.Max(0.1f, inhaleTime) : idleRate;
            Burn(rate * dt);
            glowWant = IsDrawing ? 1f : 0.32f + 0.05f * Mathf.Sin(Time.time * 7f) * Mathf.Sin(Time.time * 2.3f);
        }
        glowNow = Mathf.MoveTowards(glowNow, Lit ? glowWant : 0f, dt * (glowWant > glowNow ? 2.2f : 0.8f));
        SetEmissions(glowNow);
        UpdateSmoke(dt);
        if (FlameOn) UpdateFlame(dt);
    }

    void LateUpdate()
    {
        if (handShown && arm != null) arm.Solve();                     // after the hand was posed this frame
        if (glow != null && glow.enabled) { glow.transform.position = TipWorld(); glow.transform.rotation = transform.rotation; }
    }

    // ------------------------------------------------------------------ the sequence
    IEnumerator Run()
    {
        finishEarly = false; DragsDone = 0; BreathPuffs = 0;
        ResetCigarette();

        // ---- the pack: up, the lid, two shakes, to the lips
        Current = Phase.Pack;
        packCigarette.localPosition = new Vector3(0f, PackTop, 0f); Show(packCigarette, true);
        packLid.localRotation = Quaternion.identity;
        Show(pack, true); ShowHand(true);
        Pose packFront = HandFor(new Vector3(-0.11f, -0.155f, 0.30f), Quaternion.Euler(-16f, 24f, 6f), PackHold);   // left of the gun, in sight
        yield return MoveHand(new Pose(HandDown, packFront.rotation * Quaternion.Euler(40f, 0f, 0f)), packFront, 0.45f);
        Play(click, 0.8f, 1.15f);
        yield return Tween(0.22f, k => packLid.localRotation = Quaternion.Euler(118f * k, 0f, 0f));
        for (int i = 0; i < 2; i++)                                    // a flick of the wrist: one comes out a little each time
        {
            Pose low = new Pose(packFront.position + new Vector3(0f, -0.03f, 0f), packFront.rotation * Quaternion.Euler(-12f, 0f, 0f));
            yield return MoveHand(packFront, low, 0.08f);
            float from = packCigarette.localPosition.y, to = PackTop + CigOut * (i + 1) / 2f;
            yield return MoveHand(low, packFront, 0.12f, k => packCigarette.localPosition = new Vector3(0f, Mathf.Lerp(from, to, k), 0f));
        }
        // the filter of the one sticking out goes to the lips: the pack points its top at the mouth
        Quaternion packToMouth = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.down, -CigDir), -CigDir);
        Pose atMouth = HandFor(transform.TransformPoint(Mouth) - transform.TransformDirection(-CigDir) * (PackTop + CigOut), transform.rotation * packToMouth, PackHold, true);
        yield return MoveHand(CurrentHand(), atMouth, 0.42f);
        // the lips take it: from the pack into the mouth
        Show(packCigarette, false);
        cigarette.SetPositionAndRotation(packCigarette.position, packCigarette.rotation);
        Show(cigarette, true);
        Vector3 cp = cigarette.localPosition; Quaternion cr = cigarette.localRotation;
        yield return Tween(0.16f, k => { cigarette.localPosition = Vector3.Lerp(cp, Mouth, k); cigarette.localRotation = Quaternion.Slerp(cr, MouthRot, k); });
        Pose away = new Pose(packFront.position + new Vector3(-0.04f, -0.08f, 0f), packFront.rotation);
        yield return MoveHand(atMouth, away, 0.3f, k => packLid.localRotation = Quaternion.Euler(118f * (1f - k), 0f, 0f));
        Play(click, 0.5f, 0.9f);
        yield return MoveHand(away, new Pose(HandDown, away.rotation), 0.3f);
        Show(pack, false);

        // ---- the lighter: up, the lid, the flint, the flame to the tip, the first drag lights it
        Current = Phase.Lighter;
        lighterLid.localRotation = Quaternion.identity; Show(lighter, true);
        Pose lighterFront = HandFor(new Vector3(-0.08f, -0.16f, 0.28f), Quaternion.Euler(-8f, -14f, 0f), LighterHold);
        yield return MoveHand(new Pose(HandDown, lighterFront.rotation), lighterFront, 0.4f);
        Play(click, 0.9f, 1f);
        yield return Tween(0.12f, k => lighterLid.localRotation = Quaternion.Euler(0f, 0f, -112f * k));
        yield return Wait(0.12f);
        Play(strike, 0.8f, Random.Range(0.95f, 1.08f));
        yield return Wait(0.05f);
        SetFlame(true); Play(flare, 0.45f, 1f);
        yield return Wait(0.25f);
        Quaternion upright = transform.rotation * Quaternion.Euler(-6f, -10f, 0f);
        Vector3 lighterAt = TipWorld() - upright * FlameTip + transform.TransformDirection(new Vector3(0f, 0.004f, 0f));
        Pose atTip = HandFor(lighterAt, upright, LighterHold, true);   // the hot top of the flame just over the tip
        yield return MoveHand(CurrentHand(), atTip, 0.38f);

        Current = Phase.Lighting;
        Play(inhale, 0.75f, 1f);
        IsDrawing = true;
        yield return Wait(0.35f);
        Lit = true; glowWant = 1f;                                     // it catches
        yield return Wait(Mathf.Max(0.2f, inhaleTime - 0.35f));
        IsDrawing = false;
        Pose off = new Pose(lighterFront.position, lighterFront.rotation);
        yield return MoveHand(atTip, off, 0.22f);
        Play(click, 0.8f, 0.85f);
        SetFlame(false);
        yield return Tween(0.1f, k => lighterLid.localRotation = Quaternion.Euler(0f, 0f, -112f * (1f - k)));
        yield return MoveHand(off, new Pose(HandDown, off.rotation), 0.32f);
        Show(lighter, false); ShowHand(false);
        yield return Exhale();

        // ---- the drags, until it is burnt down
        Current = Phase.Smoking;
        float cycle = (pause.x + pause.y) * 0.5f + holdTime + exhaleTime;
        idleRate = (paperLength - stubLength) * idleBurnShare / Mathf.Max(1, drags) / Mathf.Max(0.5f, cycle);
        while (DragsDone < drags && !finishEarly)
        {
            float wait = Random.Range(pause.x, pause.y);
            for (float t = 0f; t < wait && !finishEarly; t += Time.deltaTime) yield return null;
            if (finishEarly) break;
            Play(inhale, 0.65f, Random.Range(0.94f, 1.06f));
            IsDrawing = true;
            yield return Tween(inhaleTime, k => cigarette.localPosition = Mouth + new Vector3(0f, 0.004f, -0.002f) * Mathf.Sin(k * Mathf.PI));
            IsDrawing = false; DragsDone++;
            yield return Wait(holdTime);
            yield return Exhale();
        }

        // ---- the butt: taken, flicked away
        Current = Phase.Finishing;
        Show(pack, false); Show(lighter, false); ShowHand(true);
        Vector3 mid = cigarette.TransformPoint(new Vector3(0f, filterLength + (paperLength - burnt) * 0.5f, 0f));   // the fingers away from the face
        Pose take = HandFor(mid, transform.rotation * Quaternion.Euler(-30f, 25f, -20f), 0f, true);
        yield return MoveHand(new Pose(HandDown, take.rotation), take, 0.36f);
        cigarette.SetParent(hand, true);
        Pose wind = HandFor(new Vector3(0.02f, -0.2f, 0.26f), Quaternion.Euler(-40f, 30f, -10f), 0f);
        yield return MoveHand(take, wind, 0.22f);
        Pose flickTo = HandFor(new Vector3(0.12f, -0.16f, 0.42f), Quaternion.Euler(-70f, 20f, -10f), 0f);
        yield return MoveHand(wind, flickTo, 0.08f);
        Play(flick, 0.7f, Random.Range(0.95f, 1.1f));
        ThrowButt();
        yield return MoveHand(flickTo, new Pose(HandDown, flickTo.rotation), 0.3f);
        ShowHand(false);
        Finished++; Current = Phase.None; run = null;
    }

    IEnumerator Exhale()
    {
        Play(exhale, 0.6f, Random.Range(0.94f, 1.06f));
        IsExhaling = true; float acc = 0f;
        for (float t = 0f; t < exhaleTime; t += Time.deltaTime)
        {
            float x = t / exhaleTime, rate = 34f * Mathf.Pow(1f - x, 1.4f) * Mathf.Clamp01(x * 8f);   // a quick start, then thinner
            acc += rate * Time.deltaTime;
            while (acc >= 1f) { acc -= 1f; Breathe(1f - x); }
            yield return null;
        }
        IsExhaling = false;
    }

    void Breathe(float strength)
    {
        if (breath == null) return;
        Vector3 at = transform.TransformPoint(Mouth + new Vector3(0f, -0.005f, 0.075f));
        Vector3 v = transform.TransformDirection(new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(-0.2f, 0.02f), 1f)) * Mathf.Lerp(0.35f, 1.1f, strength);
        var p = new ParticleSystem.EmitParams { position = at + Random.insideUnitSphere * 0.01f, velocity = v, applyShapeToPosition = false };
        breath.Emit(p, 1); BreathPuffs++;
    }

    // ------------------------------------------------------------------ the hand
    struct Pose { public Vector3 position; public Quaternion rotation; public Pose(Vector3 p, Quaternion r) { position = p; rotation = r; } }

    Pose CurrentHand() { return new Pose(hand.localPosition, hand.localRotation); }

    /// <summary>The hand pose (local to this) for a prop held hold metres above its bottom, the prop's bottom at p and turned to r.
    /// world: p and r are in world space (else in this object's space).</summary>
    Pose HandFor(Vector3 p, Quaternion r, float hold, bool world = false)
    {
        if (world) { p = transform.InverseTransformPoint(p); r = Quaternion.Inverse(transform.rotation) * r; }
        return new Pose(p + r * new Vector3(0f, hold, 0f), r);
    }

    IEnumerator MoveHand(Pose a, Pose b, float time, System.Action<float> also = null)
    {
        for (float t = 0f; ; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, time)), e = k * k * (3f - 2f * k);
            hand.localPosition = Vector3.Lerp(a.position, b.position, e); hand.localRotation = Quaternion.Slerp(a.rotation, b.rotation, e);
            if (also != null) also(e);
            if (k >= 1f) yield break;
            yield return null;
        }
    }

    static IEnumerator Tween(float time, System.Action<float> step)
    {
        for (float t = 0f; ; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, time));
            step(k * k * (3f - 2f * k));
            if (k >= 1f) yield break;
            yield return null;
        }
    }

    static IEnumerator Wait(float time) { for (float t = 0f; t < time; t += Time.deltaTime) yield return null; }

    void ShowHand(bool on)
    {
        handShown = on;
        if (arm != null) foreach (var a in new[] { arm.left, arm.right }) { Show(a.upper, on); Show(a.lower, on); }
        if (!on) hand.localPosition = HandDown;
    }

    static void Show(Transform t, bool on) { if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on); }

    // ------------------------------------------------------------------ the cigarette, the ember, the flame, the smoke
    static Quaternion MouthRot { get { return Quaternion.FromToRotation(Vector3.up, CigDir); } }

    void ResetCigarette()
    {
        if (cigarette.parent != transform) cigarette.SetParent(transform, false);
        cigarette.localPosition = Mouth; cigarette.localRotation = MouthRot;
        burnt = 0f; ashLength = 0f; Lit = false; glowNow = 0f; glowWant = 0f; IsDrawing = IsExhaling = false;
        Burn(0f);
    }

    /// <summary>Shortens the tobacco by d metres: the paper shrinks, the ember and the ash move down the cigarette.</summary>
    void Burn(float d)
    {
        burnt = Mathf.Min(burnt + d, paperLength - stubLength);
        if (Lit) ashLength += d * 0.6f;
        if (ashLength > 0.009f) ashLength = 0.002f;                    // a bit of ash drops off
        float l = paperLength - burnt;
        paper.localPosition = new Vector3(0f, filterLength + l * 0.5f, 0f); paper.localScale = new Vector3(paper.localScale.x, l * 0.5f, paper.localScale.z);
        ember.localPosition = new Vector3(0f, filterLength + l + EmberLength * 0.5f, 0f);
        Show(ash, ashLength > 0.0005f);
        ash.localPosition = new Vector3(0f, filterLength + l + EmberLength + ashLength * 0.5f, 0f); ash.localScale = new Vector3(ash.localScale.x, ashLength * 0.5f, ash.localScale.z);
    }

    Vector3 TipWorld() { return cigarette.TransformPoint(new Vector3(0f, filterLength + paperLength - burnt + EmberLength, 0f)); }

    void SetEmissions(float k)
    {
        if (emberRenderer != null)
        {
            emberRenderer.GetPropertyBlock(block); block.SetColor(EmissionId, EmberColor * (k * 3.2f)); emberRenderer.SetPropertyBlock(block);
        }
        if (glow != null)
        {
            bool on = k > 0.01f && cigarette.gameObject.activeSelf;
            if (glow.enabled != on) glow.enabled = on;
            if (on) { glow.GetPropertyBlock(block); block.SetFloat(IntensityId, 0.4f + 1.6f * k); glow.SetPropertyBlock(block); glow.transform.localScale = Vector3.one * (0.012f + 0.01f * k); }
        }
    }

    void SetFlame(bool on)
    {
        FlameOn = on;
        if (flame != null) flame.enabled = on;
        if (flameLight != null) flameLight.enabled = on;
    }

    void UpdateFlame(float dt)
    {
        flameT += dt;
        float n = Mathf.PerlinNoise(flameT * 9f, 0.37f), m = Mathf.PerlinNoise(0.71f, flameT * 13f);
        var ft = flame.transform;
        Vector3 up = lighter.up, at = lighter.TransformPoint(new Vector3(-0.005f, 0.052f, 0f));
        ft.localScale = new Vector3(0.015f * (0.9f + 0.2f * m), 0.03f * (0.85f + 0.3f * n), 1f);
        ft.position = at + up * (ft.localScale.y * 0.5f);              // the quad grows from the chimney
        Vector3 toCam = Vector3.ProjectOnPlane(ft.position - transform.position, up);
        if (toCam.sqrMagnitude > 1e-8f) ft.rotation = Quaternion.LookRotation(toCam, up);
        if (flameLight != null) { flameLight.transform.position = at + up * 0.02f; flameLight.intensity = 1.1f + 0.5f * n; }
    }

    void UpdateSmoke(float dt)
    {
        if (tipSmoke == null) return;
        bool wisp = Lit && cigarette.gameObject.activeSelf && !IsDrawing;
        tipSmoke.transform.position = TipWorld();
        var em = tipSmoke.emission;
        float rate = wisp ? 7f : 0f;
        if (em.rateOverTime.constant != rate) em.rateOverTime = rate;
        if (wisp && !tipSmoke.isPlaying) tipSmoke.Play();
    }

    void ThrowButt()
    {
        var butt = Instantiate(cigarette.gameObject, cigarette.position, cigarette.rotation);
        butt.name = "CigaretteButt";
        foreach (var t in butt.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;   // in the world, out of the rounds' way
        var body = butt.AddComponent<Rigidbody>(); body.mass = 0.01f; body.linearDamping = 0.3f; body.angularDamping = 0.5f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var col = butt.AddComponent<CapsuleCollider>(); col.direction = 1; col.radius = 0.004f;
        col.height = filterLength + paperLength - burnt; col.center = new Vector3(0f, col.height * 0.5f, 0f);
        var owner = GetComponentInParent<SimpleFirstPersonController>();
        if (owner != null) foreach (var c in owner.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c);
        Vector3 v = transform.TransformDirection(new Vector3(0.35f, 0.45f, 1f)) * 3.2f;
        if (owner != null) v += owner.PlanarVelocity;
        body.linearVelocity = v; body.angularVelocity = Random.onUnitSphere * 25f;
        LastButt = butt;
        StartCoroutine(GoOut(butt));
        Destroy(butt, 12f);
        Show(cigarette, false);
        ResetCigarette();
    }

    /// <summary>The thrown butt keeps glowing a moment and goes out.</summary>
    IEnumerator GoOut(GameObject butt)
    {
        Renderer r = null;
        foreach (var x in butt.GetComponentsInChildren<Renderer>(true)) if (x.name == ember.name) r = x;
        var b = new MaterialPropertyBlock();
        for (float t = 0f; t < 4f && butt != null && r != null; t += Time.deltaTime)
        {
            r.GetPropertyBlock(b); b.SetColor(EmissionId, EmberColor * (1.1f * (1f - t / 4f))); r.SetPropertyBlock(b);
            yield return null;
        }
    }

    void Play(AudioClip clip, float v, float pitch)
    {
        if (audioSource == null || clip == null) return;
        audioSource.pitch = pitch; audioSource.PlayOneShot(clip, v * volume);
    }

    // ------------------------------------------------------------------ sounds (made once, no files)
    static AudioClip Clip(string name, float seconds, System.Func<float, System.Random, float> sample)
    {
        const int rate = 44100; int n = (int)(rate * seconds);
        var d = new float[n]; var rnd = new System.Random(name.GetHashCode()); float peak = 1e-4f;
        for (int i = 0; i < n; i++) { d[i] = sample(i / (float)rate, rnd); peak = Mathf.Max(peak, Mathf.Abs(d[i])); }
        for (int i = 0; i < n; i++) d[i] *= 0.85f / peak;
        var c = AudioClip.Create(name, n, 1, rate, false); c.SetData(d, 0);
        return c;
    }

    static float Noise(System.Random r) { return (float)(r.NextDouble() * 2.0 - 1.0); }

    static void MakeSounds()
    {
        // a metal lid: a sharp tick with a short ring
        click = Clip("LighterClick", 0.09f, (t, r) => Noise(r) * Mathf.Exp(-t * 180f) + Mathf.Sin(t * 2f * Mathf.PI * 3100f) * Mathf.Exp(-t * 60f) * 0.5f);
        // the flint wheel: a scratchy burst
        float lp = 0f;
        strike = Clip("LighterStrike", 0.14f, (t, r) => { float x = Noise(r); lp += 0.5f * (x - lp); return (x - lp) * Mathf.Exp(-t * 28f) * (r.NextDouble() < 0.08 ? 2.5f : 1f); });
        // the wick catching: a soft low whoosh
        float a = 0f, b = 0f;
        flare = Clip("LighterFlare", 0.35f, (t, r) => { a += 0.04f * (Noise(r) - a); b += 0.04f * (a - b); return b * Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI); });
        // drawing on it: a breath in with the tobacco crackling
        float c0 = 0f, c1 = 0f;
        inhale = Clip("CigInhale", 1.1f, (t, r) =>
        {
            c0 += 0.08f * (Noise(r) - c0); c1 += 0.08f * (c0 - c1);
            float env = Mathf.Sin(Mathf.Clamp01(t / 1.1f) * Mathf.PI);
            float crackle = r.NextDouble() < 0.0016 ? Noise(r) * 6f : 0f;
            return (c0 - c1) * 0.6f * env + crackle * env;
        });
        // breathing the smoke out: a long, falling breath
        float e0 = 0f, e1 = 0f;
        exhale = Clip("CigExhale", 1.6f, (t, r) =>
        {
            float x = t / 1.6f, cut = Mathf.Lerp(0.16f, 0.05f, x);
            e0 += cut * (Noise(r) - e0); e1 += cut * (e0 - e1);
            return (e0 - e1 * 0.7f) * Mathf.Clamp01(x * 12f) * Mathf.Pow(1f - x, 1.3f);
        });
        // the butt flicked off the fingers
        flick = Clip("CigFlick", 0.06f, (t, r) => Noise(r) * Mathf.Exp(-t * 120f));
    }
}

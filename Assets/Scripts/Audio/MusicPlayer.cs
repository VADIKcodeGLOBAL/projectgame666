using UnityEngine;

/// <summary>
/// Background music: plays the tracks of the MusicLibrary one after another in a shuffled order, with the music volume
/// from the settings. Keeps playing while the game is paused. Does nothing while the library is empty.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    public MusicLibrary library;
    [Tooltip("Seconds of silence between two tracks.")] public float gap = 2f;

    public int TrackCount { get { return library != null && library.tracks != null ? library.tracks.Length : 0; } }
    public string CurrentTrack { get { return source != null && source.clip != null ? source.clip.name : ""; } }

    AudioSource source;
    int[] order; int next; float waitUntil;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f; source.ignoreListenerPause = true;
        source.priority = 0;                                     // never stolen by the shots
    }

    void Shuffle()
    {
        int n = TrackCount; order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        for (int i = n - 1; i > 0; i--) { int j = Random.Range(0, i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }
        next = 0;
    }

    void Update()
    {
        source.volume = GameSettings.MusicVolume;
        if (TrackCount == 0 || source.isPlaying) { waitUntil = Time.unscaledTime + gap; return; }
        if (Time.unscaledTime < waitUntil) return;
        if (order == null || order.Length != TrackCount || next >= order.Length) Shuffle();
        var clip = library.tracks[order[next++]];
        if (clip == null) return;
        source.clip = clip; source.Play();
    }
}

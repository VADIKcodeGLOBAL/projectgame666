using UnityEngine;

/// <summary>
/// The music tracks of the game. Filled automatically in the editor from everything in Assets/Audio/Music
/// (MusicLibraryUpdater), so dropping a file into that folder is all it takes.
/// </summary>
[CreateAssetMenu(menuName = "Project Game/Music Library")]
public class MusicLibrary : ScriptableObject
{
    public AudioClip[] tracks = new AudioClip[0];
}

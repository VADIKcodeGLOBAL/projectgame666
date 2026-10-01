using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Assets/Data/Audio/MusicLibrary.asset in step with the files in Assets/Audio/Music: a track dropped into the folder
/// is streamed (Vorbis) and added to the library, a deleted one is removed. Nothing else has to be set up.
/// </summary>
public class MusicLibraryUpdater : AssetPostprocessor
{
    public const string MusicDir = "Assets/Audio/Music";
    public const string LibraryPath = "Assets/Data/Audio/MusicLibrary.asset";

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(MusicDir + "/")) return;
        var ai = (AudioImporter)assetImporter;
        var s = ai.defaultSampleSettings;
        s.loadType = AudioClipLoadType.Streaming; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.7f;
        ai.defaultSampleSettings = s; ai.forceToMono = false; ai.loadInBackground = true;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(p => p.StartsWith(MusicDir + "/")))
            EditorApplication.delayCall += () => Rebuild();
    }

    /// <summary>The library asset, created if missing, with every AudioClip of the music folder (sorted by name).</summary>
    public static MusicLibrary Rebuild()
    {
        var lib = AssetDatabase.LoadAssetAtPath<MusicLibrary>(LibraryPath);
        if (lib == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            AssetDatabase.Refresh();
            lib = ScriptableObject.CreateInstance<MusicLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }
        lib.tracks = AssetDatabase.FindAssets("t:AudioClip", new[] { MusicDir })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c != null).ToArray();
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssets();
        return lib;
    }
}

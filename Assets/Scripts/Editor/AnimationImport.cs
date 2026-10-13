using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import of the clips in Assets/Art/Animations/&lt;source&gt;/ (Mixamo "FBX for Unity, without skin", Quaternius, converted mocap...),
/// set when a file is first imported (changes by hand stay):
///  - Humanoid with an avatar made from the file's own skeleton (Unity maps the usual rigs by itself): Mecanim moves the motion
///    onto any humanoid character;
///  - no materials, cameras, lights or blend shapes;
///  - a clip per take, named after the file (Mixamo calls every take "mixamo.com") or, with several takes, after the take;
///  - looping for idles, walks, runs and climbs (the tag: <see cref="AnimTags.Guess"/>);
///  - the root's turning and height baked into the pose (falls and spins play as authored); ground travel stays root motion,
///    which the game does not apply: it moves the character itself.
/// </summary>
public class AnimationImport : AssetPostprocessor
{
    public const string Root = "Assets/Art/Animations";

    static bool Ours(string path) { return path.Replace('\\', '/').StartsWith(Root + "/"); }

    void OnPreprocessModel()
    {
        if (!Ours(assetPath) || !assetImporter.importSettingsMissing) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true; mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.importVisibility = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.animationCompression = ModelImporterAnimationCompression.Optimal;
    }

    void OnPreprocessAnimation()
    {
        if (!Ours(assetPath) || !assetImporter.importSettingsMissing) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        string file = Path.GetFileNameWithoutExtension(assetPath);
        foreach (var c in clips)
        {
            if (clips.Length == 1) c.name = file;
            var tag = AnimTags.Guess(file, c.takeName, c.name);
            c.loopTime = AnimTags.Loops(tag);
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = false;
        }
        mi.clipAnimations = clips;
    }
}

/// <summary>The tag of a clip from its name: a "&lt;Tag&gt;_" prefix decides, otherwise the words in it.</summary>
public static class AnimTags
{
    public static bool Loops(AnimTag t) { return t == AnimTag.Idle || t == AnimTag.Walk || t == AnimTag.Run || t == AnimTag.Climb; }

    static bool Has(string s, params string[] words) { return words.Any(w => s.Contains(w)); }

    public static AnimTag Guess(params string[] names)
    {
        foreach (var n in names)                                        // an explicit prefix: "Walk_Zombie", "Death-02"
        {
            if (string.IsNullOrEmpty(n)) continue;
            int cut = n.IndexOfAny(new[] { '_', '-', ' ' });
            if (cut > 0 && System.Enum.TryParse(n.Substring(0, cut), true, out AnimTag t)) return t;
        }
        string s = string.Join(" ", names.Where(x => !string.IsNullOrEmpty(x)).ToArray()).ToLowerInvariant();
        if (Has(s, "death", "dying", "die", "dead")) return AnimTag.Death;
        if (Has(s, "stumbl", "stagger", "knockback", "knock back", "knocked")) return AnimTag.Stagger;
        if (Has(s, "react", "hurt", "damage", "impact", "flinch")) return AnimTag.Hit;
        if (Has(s, "attack", "punch", "slash", "strike", "swipe", "bite", "kick", "stab", "swing", "jab", "hook", "smash")) return AnimTag.Attack;
        if (Has(s, "hit")) return AnimTag.Hit;
        // sideways steps, turns on the spot, jumps and dances are not the forward locomotion the enemies blend
        if (Has(s, "strafe", "turn", "jump", "dance", "dancing", "backward")) return AnimTag.Emote;
        if (Has(s, "climb", "crawl")) return AnimTag.Climb;
        if (Has(s, "run", "jog", "sprint")) return AnimTag.Run;
        if (Has(s, "walk")) return AnimTag.Walk;
        if (Has(s, "idle", "breath", "stand")) return AnimTag.Idle;
        return AnimTag.Emote;
    }
}

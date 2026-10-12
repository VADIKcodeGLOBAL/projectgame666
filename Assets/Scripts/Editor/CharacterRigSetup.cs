using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Humanoid avatars of the characters that become enemies (automation "chars", Tools > Characters > Set Up Rigs).
/// Each entry of <see cref="Characters"/> is a source FBX outside the project (still being worked on: copied in again whenever the
/// source is newer) and its place in the project. Its ValveBiped bones are mapped onto Unity's human bones, and the avatar's
/// reference pose is a T-pose (arms raised to the horizontal, legs straight down, whatever the rest pose is): clips from any
/// humanoid source then retarget onto it the same way. The mesh is not changed.
/// Check renders: Logs/shots/chars_&lt;name&gt;_pose.png (rest pose | avatar T-pose) and _human.png (muscles at zero | a test pose).
/// </summary>
public static class CharacterRigSetup
{
    /// <summary>A character: its source FBX, its copy in the project, and the source QC with its $jigglebone settings.</summary>
    public class Character { public string name, source, asset, qc; }

    public static readonly Character[] Characters =
    {
        new Character
        {
            name = "Kroxigor",
            source = @"D:\Gmod\unarchive-models\kroxigor\export\kroxigorpm\kroxigorpm-my-export.fbx",
            asset = GmodModelImport.Root + "/kroxigor/kroxigorpm/kroxigorpm-my-export.fbx",
            qc = @"D:\Gmod\unarchive-models\kroxigor\decompile\kroxigorpm.qc",
        },
    };

    const string VB = "ValveBiped.Bip01_";

    /// <summary>Unity human bone → ValveBiped bone (the spine chain is picked separately: see SpineChain).</summary>
    static readonly string[,] Map =
    {
        { "Hips", "Pelvis" }, { "Neck", "Neck1" }, { "Head", "Head1" },
        { "LeftShoulder", "L_Clavicle" }, { "LeftUpperArm", "L_UpperArm" }, { "LeftLowerArm", "L_Forearm" }, { "LeftHand", "L_Hand" },
        { "RightShoulder", "R_Clavicle" }, { "RightUpperArm", "R_UpperArm" }, { "RightLowerArm", "R_Forearm" }, { "RightHand", "R_Hand" },
        { "LeftUpperLeg", "L_Thigh" }, { "LeftLowerLeg", "L_Calf" }, { "LeftFoot", "L_Foot" }, { "LeftToes", "L_Toe0" },
        { "RightUpperLeg", "R_Thigh" }, { "RightLowerLeg", "R_Calf" }, { "RightFoot", "R_Foot" }, { "RightToes", "R_Toe0" },
        { "Left Thumb Proximal", "L_Finger0" }, { "Left Thumb Intermediate", "L_Finger01" }, { "Left Thumb Distal", "L_Finger02" },
        { "Left Index Proximal", "L_Finger1" }, { "Left Index Intermediate", "L_Finger11" }, { "Left Index Distal", "L_Finger12" },
        { "Left Middle Proximal", "L_Finger2" }, { "Left Middle Intermediate", "L_Finger21" }, { "Left Middle Distal", "L_Finger22" },
        { "Left Ring Proximal", "L_Finger3" }, { "Left Ring Intermediate", "L_Finger31" }, { "Left Ring Distal", "L_Finger32" },
        { "Left Little Proximal", "L_Finger4" }, { "Left Little Intermediate", "L_Finger41" }, { "Left Little Distal", "L_Finger42" },
        { "Right Thumb Proximal", "R_Finger0" }, { "Right Thumb Intermediate", "R_Finger01" }, { "Right Thumb Distal", "R_Finger02" },
        { "Right Index Proximal", "R_Finger1" }, { "Right Index Intermediate", "R_Finger11" }, { "Right Index Distal", "R_Finger12" },
        { "Right Middle Proximal", "R_Finger2" }, { "Right Middle Intermediate", "R_Finger21" }, { "Right Middle Distal", "R_Finger22" },
        { "Right Ring Proximal", "R_Finger3" }, { "Right Ring Intermediate", "R_Finger31" }, { "Right Ring Distal", "R_Finger32" },
        { "Right Little Proximal", "R_Finger4" }, { "Right Little Intermediate", "R_Finger41" }, { "Right Little Distal", "R_Finger42" },
    };

    [MenuItem("Tools/Characters/Set Up Rigs")]
    static void Menu() { SetUpAll(); }

    public static void SetUpAll()
    {
        int ok = 0;
        foreach (var c in Characters)
        {
            try { if (SetUp(c)) ok++; }
            catch (Exception e) { EditorAutomation.Log("  " + c.name + ": ERROR " + e.Message); }
        }
        EditorAutomation.Log(ok == Characters.Length ? "CHARS_OK (" + ok + ")" : "CHARS_FAILED: " + (Characters.Length - ok) + " of " + Characters.Length);
    }

    /// <summary>The character's avatar, once set up.</summary>
    public static Avatar AvatarOf(Character c) { return AssetDatabase.LoadAllAssetsAtPath(c.asset).OfType<Avatar>().FirstOrDefault(); }

    /// <summary>Copies the source in when the project has none or an older one. True if it did.</summary>
    static bool Sync(Character c)
    {
        bool have = File.Exists(c.asset), src = File.Exists(c.source);
        if (!src) { if (!have) throw new Exception("neither " + c.source + " nor " + c.asset + " exists"); return false; }
        if (have && File.GetLastWriteTimeUtc(c.source) <= File.GetLastWriteTimeUtc(c.asset)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(c.asset));
        File.Copy(c.source, c.asset, true);
        File.SetLastWriteTimeUtc(c.asset, File.GetLastWriteTimeUtc(c.source));
        AssetDatabase.ImportAsset(c.asset, ImportAssetOptions.ForceUpdate);
        return true;
    }

    static void Aim(Transform t, Transform child, Vector3 dir)
    {
        if (t == null || child == null) return;
        Vector3 cur = child.position - t.position;
        if (cur.sqrMagnitude < 1e-10f) return;
        t.rotation = Quaternion.FromToRotation(cur.normalized, dir) * t.rotation;
    }

    /// <summary>The bones of the hierarchy put into a T-pose: the arms out along the shoulder line, horizontal; the legs down.</summary>
    static float TPose(Func<string, Transform> bone)
    {
        Transform lu = bone("L_UpperArm"), ru = bone("R_UpperArm");
        Vector3 left = lu.position - ru.position; left.y = 0f; left.Normalize();
        float drop = Vector3.Angle(bone("L_Forearm").position - lu.position, left);
        foreach (var side in new[] { "L", "R" })
        {
            Vector3 dir = side == "L" ? left : -left;
            Aim(bone(side + "_UpperArm"), bone(side + "_Forearm"), dir);
            Aim(bone(side + "_Forearm"), bone(side + "_Hand"), dir);
            Aim(bone(side + "_Hand"), bone(side + "_Finger2") ?? bone(side + "_Finger1"), dir);
            Aim(bone(side + "_Thigh"), bone(side + "_Calf"), Vector3.down);
            Aim(bone(side + "_Calf"), bone(side + "_Foot"), Vector3.down);
        }
        return drop;
    }

    /// <summary>Spine, Chest and UpperChest from Spine, Spine1, Spine2, Spine4: the documented pick (Spine, Spine2, Spine4), dropping
    /// a bone that sits where the previous one does (a zero-length human bone breaks the avatar) and taking Spine1 in its place.</summary>
    static List<Transform> SpineChain(Func<string, Transform> bone)
    {
        var chain = new List<Transform>(); Transform prev = bone("Pelvis");
        foreach (var n in new[] { "Spine", "Spine1", "Spine2", "Spine4" })
        {
            var t = bone(n);
            if (t != null && Vector3.Distance(t.position, prev.position) > 0.02f) { chain.Add(t); prev = t; }
        }
        if (chain.Count == 4) chain.RemoveAt(1);                      // all four apart: Spine, Spine2, Spine4 as documented
        if (chain.Count > 3) chain.RemoveRange(3, chain.Count - 3);
        return chain;
    }

    public static bool SetUp(Character c)
    {
        bool copied = Sync(c);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(c.asset);
        if (model == null) throw new Exception("not imported: " + c.asset);
        var mi = (ModelImporter)AssetImporter.GetAtPath(c.asset);
        string tag = "chars_" + c.name.ToLowerInvariant();

        HumanDescription hd; float drop; int mapped; string spine;
        var scene = PreviewShots.Begin();
        try
        {
            var rest = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            inst.transform.position = new Vector3(4f, 0f, 0f);
            var all = inst.GetComponentsInChildren<Transform>(true);
            Func<string, Transform> bone = n => all.FirstOrDefault(t => t.name == VB + n);
            if (bone("Pelvis") == null || bone("L_UpperArm") == null || bone("R_UpperArm") == null) throw new Exception("no ValveBiped skeleton in " + c.asset);

            drop = TPose(bone);
            var human = new List<HumanBone>();
            Action<string, Transform> add = (h, t) => { if (t != null) human.Add(new HumanBone { humanName = h, boneName = t.name, limit = new HumanLimit { useDefaultValues = true } }); };
            for (int i = 0; i < Map.GetLength(0); i++) add(Map[i, 0], bone(Map[i, 1]));
            var chain = SpineChain(bone);
            string[] slots = { "Spine", "Chest", "UpperChest" };
            for (int i = 0; i < chain.Count; i++) add(slots[i], chain[i]);
            mapped = human.Count; spine = string.Join(" / ", chain.Select(t => t.name.Replace(VB, "")).ToArray());

            // the skeleton as it stands in the T-pose: the instance itself (root first), its offset taken off
            inst.transform.position = Vector3.zero;
            var skeleton = inst.GetComponentsInChildren<Transform>(true)
                               .Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            inst.transform.position = new Vector3(4f, 0f, 0f);
            hd = new HumanDescription
            {
                human = human.ToArray(), skeleton = skeleton,
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };

            Bounds b0 = PreviewShots.BoundsOf(rest), b1 = PreviewShots.BoundsOf(inst);
            float h = Mathf.Max(b0.size.y, b1.size.y) * 1.05f;
            PreviewShots.Render(scene, tag + "_pose", 700, 800,
                new PreviewShots.Frame(new Vector3(b0.center.x, h * 0.5f, b0.center.z), h, Vector3.forward),
                new PreviewShots.Frame(new Vector3(b1.center.x, h * 0.5f, b1.center.z), h, Vector3.forward));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.humanDescription = hd;
        mi.optimizeGameObjects = false;
        mi.importAnimation = false;
        mi.SaveAndReimport();

        var avatar = AvatarOf(c);
        bool good = avatar != null && avatar.isValid && avatar.isHuman;
        EditorAutomation.Log("  " + (good ? "ok   " : "FAIL ") + c.name + (copied ? " (copied in from the source)" : "") + ": " + mapped + " human bones, spine " + spine
                             + ", arms raised " + drop.ToString("0") + " deg to the T-pose, avatar " + (avatar == null ? "MISSING" : (avatar.isValid ? "valid" : "INVALID") + (avatar.isHuman ? ", human" : ", NOT human")));
        if (good) RenderHumanPoses(c, avatar, tag);
        return good;
    }

    /// <summary>The avatar driving the bones: all muscles at zero (Unity's neutral pose), and a test pose (left arm up, right arm
    /// forward, left knee up, head turned left) from the front and the side. Shows the mapping and the sides are right.</summary>
    static void RenderHumanPoses(Character c, Avatar avatar, string tag)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(c.asset);
        var scene = PreviewShots.Begin();
        try
        {
            var a = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            var b = (GameObject)PrefabUtility.InstantiatePrefab(model, scene); b.transform.position = new Vector3(4f, 0f, 0f);
            Action<GameObject, Dictionary<string, float>> pose = (go, muscles) =>
            {
                var handler = new HumanPoseHandler(avatar, go.transform);
                var p = new HumanPose(); handler.GetHumanPose(ref p);
                for (int i = 0; i < p.muscles.Length; i++) p.muscles[i] = 0f;
                foreach (var kv in muscles)
                {
                    int i = Array.IndexOf(HumanTrait.MuscleName, kv.Key);
                    if (i >= 0) p.muscles[i] = kv.Value; else EditorAutomation.Log("  (no muscle " + kv.Key + ")");
                }
                handler.SetHumanPose(ref p);
                handler.Dispose();
            };
            pose(a, new Dictionary<string, float>());
            pose(b, new Dictionary<string, float>
            {
                { "Left Arm Down-Up", 1f }, { "Right Arm Front-Back", 1f }, { "Right Arm Down-Up", 0.4f },
                { "Left Upper Leg Front-Back", 0.8f }, { "Left Lower Leg Stretch", -0.6f }, { "Head Turn Left-Right", 0.8f },
            });
            Bounds ba = PreviewShots.BoundsOf(a), bb = PreviewShots.BoundsOf(b);
            float h = Mathf.Max(ba.size.y, bb.size.y) * 1.1f;
            PreviewShots.Render(scene, tag + "_human", 600, 800,
                new PreviewShots.Frame(new Vector3(ba.center.x, ba.center.y, ba.center.z), h, Vector3.forward),
                new PreviewShots.Frame(new Vector3(bb.center.x, bb.center.y, bb.center.z), h, Vector3.forward),
                new PreviewShots.Frame(new Vector3(bb.center.x, bb.center.y, bb.center.z), h, Vector3.right));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}

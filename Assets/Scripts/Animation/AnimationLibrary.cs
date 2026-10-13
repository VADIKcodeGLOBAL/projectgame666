using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a clip is for: the slots of the enemy's animator controller.</summary>
public enum AnimTag { Idle, Walk, Run, Climb, Attack, Hit, Stagger, Death, Emote }

/// <summary>
/// The humanoid clips the characters can play, filled by the "anims" command from Assets/Art/Animations/&lt;source&gt;/ (any humanoid
/// FBX: Mixamo, Quaternius, converted mocap...). Each entry knows its tag, where it came from and under which licence, whether it
/// loops and, for the moving ones, the ground speed it is made for (measured on the reference character). Entries can be switched
/// off or weighted by hand; "anims" keeps those edits. Clips made by the project itself for testing (test) are used for a tag only
/// while no real clip has that tag.
/// </summary>
[CreateAssetMenu(fileName = "AnimationLibrary", menuName = "ProjectGame/Animation Library")]
public class AnimationLibrary : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public AnimationClip clip;
        public AnimTag tag;
        [Tooltip("Folder under Assets/Art/Animations (Mixamo, Quaternius...).")] public string source;
        [Tooltip("From the folder's SOURCE.md.")] public string license;
        [Tooltip("The file the clip comes from.")] public string file;
        public bool loop;
        [Tooltip("Ground speed the clip is made for, m/s on the reference character (0 for clips that stand still).")] public float speed;
        [Tooltip("Relative chance among the clips of the same tag.")] [Min(0f)] public float weight = 1f;
        public bool enabled = true;
        [Tooltip("Made by the project for testing: used only while no real clip has this tag.")] public bool test;
    }

    public List<Entry> clips = new List<Entry>();
    [Tooltip("Human scale (hips height) of the reference character the speeds were measured on: a character plays them scaled by its own.")]
    public float referenceHumanScale = 1f;

    /// <summary>The clips that serve a tag: the real ones if there are any, otherwise the test ones.</summary>
    public List<Entry> Of(AnimTag tag)
    {
        var real = new List<Entry>(); var test = new List<Entry>();
        foreach (var e in clips)
            if (e != null && e.enabled && e.clip != null && e.tag == tag && e.weight > 0f) (e.test ? test : real).Add(e);
        return real.Count > 0 ? real : test;
    }

    /// <summary>One clip of the tag, by weight; null if the tag has none.</summary>
    public Entry Pick(AnimTag tag, System.Random rnd)
    {
        var list = Of(tag);
        if (list.Count == 0) return null;
        float sum = 0f; foreach (var e in list) sum += e.weight;
        float x = (float)rnd.NextDouble() * sum;
        foreach (var e in list) { x -= e.weight; if (x <= 0f) return e; }
        return list[list.Count - 1];
    }
}

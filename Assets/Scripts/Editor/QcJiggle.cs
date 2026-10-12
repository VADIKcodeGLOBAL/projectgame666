using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// The $jigglebone blocks of a Source QC: bone name and its is_flexible settings (Source units: inches, 1/s², 1/s, degrees).
/// is_rigid and has_base_spring blocks are read past; a bone without is_flexible is left out.
/// </summary>
public static class QcJiggle
{
    public class Entry
    {
        public string bone;
        public float length = 10f, tipMass, pitchStiffness = 100f, yawStiffness = 100f, pitchDamping = 5f, yawDamping = 5f, angleConstraint = 90f;
    }

    /// <summary>The text of the block that opens at text[open] ('{'), without its braces.</summary>
    static string Block(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open + 1, i - open - 1);
        }
        return text.Substring(open + 1);
    }

    public static List<Entry> Parse(string qc)
    {
        var list = new List<Entry>();
        foreach (Match m in Regex.Matches(qc, "\\$jigglebone\\s+\"([^\"]+)\"\\s*\\{"))
        {
            string body = Block(qc, m.Index + m.Length - 1);
            var flex = Regex.Match(body, "is_flexible\\s*\\{");
            if (!flex.Success) continue;
            string settings = Block(body, flex.Index + flex.Length - 1);
            var e = new Entry { bone = m.Groups[1].Value };
            foreach (Match kv in Regex.Matches(settings, "([a-z_]+)\\s+(-?[0-9.]+)"))
            {
                float v = float.Parse(kv.Groups[2].Value, CultureInfo.InvariantCulture);
                switch (kv.Groups[1].Value)
                {
                    case "length": e.length = v; break;
                    case "tip_mass": e.tipMass = v; break;
                    case "pitch_stiffness": e.pitchStiffness = v; break;
                    case "yaw_stiffness": e.yawStiffness = v; break;
                    case "pitch_damping": e.pitchDamping = v; break;
                    case "yaw_damping": e.yawDamping = v; break;
                    case "angle_constraint": e.angleConstraint = v; break;
                }
            }
            list.Add(e);
        }
        return list;
    }
}

using UnityEngine;

/// <summary>
/// Placeholder arms of the view model: two boxes per arm, the upper arm and the forearm. Each hand is held to a grip point on
/// the weapon (a child of it, so every kick, sway, reload dip and switch moves the hands exactly with the gun); the shoulders
/// stay put in the view and the elbows bend in between (a two-bone solve, the elbow pushed towards a hint direction).
/// Weapon.UpdatePose calls Solve once it has posed the gun. The boxes are children of the weapon: they are drawn by the view
/// model camera with it and hide with it (scope, switch).
/// </summary>
public class ViewModelArms : MonoBehaviour
{
    [System.Serializable]
    public class Arm
    {
        [Tooltip("Where the hand holds the weapon: the forearm ends here. A child of the weapon.")] public Transform grip;
        [Tooltip("Shoulder joint in the space of the weapon's parent (the view), metres.")] public Vector3 shoulder;
        [Tooltip("Which way the elbow points, in the space of the weapon's parent.")] public Vector3 elbowHint = Vector3.down;
        public Transform upper, lower;
    }

    public Arm right = new Arm(), left = new Arm();
    [Tooltip("Shoulder to elbow, metres.")] public float upperLength = 0.3f;
    [Tooltip("Elbow to the grip point (forearm and half the hand), metres.")] public float lowerLength = 0.33f;
    [Tooltip("Thickness of the upper arm, metres.")] public float upperWidth = 0.08f;
    [Tooltip("Thickness of the forearm, metres.")] public float lowerWidth = 0.06f;
    [Tooltip("The forearm goes this far past the grip point (the fist round the grip), metres.")] public float fist = 0.035f;

    /// <summary>Puts the boxes between the shoulders and the grips as the weapon is posed now.</summary>
    public void Solve()
    {
        var view = transform.parent;
        if (view == null) return;
        SolveArm(right, view); SolveArm(left, view);
    }

    /// <summary>How far the end of a forearm (less the fist) is from its grip point, metres: 0 when the hands are on the weapon.</summary>
    public float GripGap()
    {
        float gap = 0f;
        foreach (var a in new[] { right, left })
        {
            if (a.grip == null || a.lower == null) return -1f;
            Vector3 hand = a.lower.position + a.lower.forward * (a.lower.localScale.z * 0.5f - fist);
            gap = Mathf.Max(gap, Vector3.Distance(hand, a.grip.position));
        }
        return gap;
    }

    void SolveArm(Arm a, Transform view)
    {
        if (a.grip == null || a.upper == null || a.lower == null) return;
        Vector3 s = view.TransformPoint(a.shoulder), h = a.grip.position;
        float l1 = upperLength, l2 = lowerLength;
        Vector3 d = h - s; float dist = d.magnitude;
        Vector3 dir = dist > 1e-5f ? d / dist : view.forward;
        float reach = (l1 + l2) * 0.999f;
        if (dist > reach) { s = h - dir * reach; dist = reach; }           // out of reach: the shoulder comes along, the hand stays on the gun
        dist = Mathf.Max(dist, Mathf.Abs(l1 - l2) + 0.001f);
        float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);       // from the shoulder to the foot of the elbow on the shoulder-hand line
        float side = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(view.TransformDirection(a.elbowHint), dir);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(-view.up, dir);
        bend.Normalize();
        Vector3 e = s + dir * along + bend * side;
        Place(a.upper, s, e, bend, upperWidth, 0f, lowerWidth * 0.5f);        // a little past the elbow: no gap at the joint
        Place(a.lower, e, h, bend, lowerWidth, lowerWidth * 0.3f, fist);
    }

    /// <summary>A unit cube stretched from a to b (plus back before a and front past b), its top towards up.</summary>
    static void Place(Transform box, Vector3 a, Vector3 b, Vector3 up, float width, float back, float front)
    {
        Vector3 d = b - a; float len = d.magnitude;
        if (len < 1e-5f) return;
        Vector3 f = d / len;
        box.SetPositionAndRotation(a + f * ((len + front - back) * 0.5f), Quaternion.LookRotation(f, up));
        box.localScale = new Vector3(width, width, len + front + back);     // the weapon and the view are not scaled
    }
}

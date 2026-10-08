using UnityEngine;

/// <summary>
/// This camera renders with no shadows. The built-in pipeline has no per-camera switch, so the shadow distance is 0 while it renders
/// and back after. For the view model camera: it draws only the weapons and the arms, which neither cast nor receive shadows, yet
/// with shadows on it still made a depth texture and collected the screen-space shadows for them every frame.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ShadowlessCamera : MonoBehaviour
{
    float saved = -1f;

    // saved only while nothing is saved: a cull that never reached OnPostRender must not make 0 the distance to come back to
    void OnPreCull() { if (saved < 0f) saved = QualitySettings.shadowDistance; QualitySettings.shadowDistance = 0f; }

    void OnPostRender() { if (saved >= 0f) { QualitySettings.shadowDistance = saved; saved = -1f; } }

    void OnDisable() { if (saved >= 0f) { QualitySettings.shadowDistance = saved; saved = -1f; } }
}

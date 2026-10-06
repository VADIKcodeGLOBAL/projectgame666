using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The player's body: CapsuleCollider (1.8 x 0.35 m) + kinematic Rigidbody + CharacterMotor, driven by SimpleFirstPersonController.
/// Called by the hill map generator; command "player" converts the player of the open level (it used to be a CharacterController).
/// </summary>
public static class PlayerSetup
{
    public const float Height = 1.8f, Radius = 0.35f;

    public static CharacterMotor Configure(GameObject player, Transform cameraPivot)
    {
        foreach (var cc in player.GetComponents<CharacterController>()) Object.DestroyImmediate(cc, true);
        var cap = player.GetComponent<CapsuleCollider>();
        if (cap == null) cap = player.AddComponent<CapsuleCollider>();
        cap.direction = 1; cap.height = Height; cap.radius = Radius; cap.center = new Vector3(0f, Height * 0.5f, 0f); cap.isTrigger = false;
        var rb = player.GetComponent<Rigidbody>();
        if (rb == null) rb = player.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None; rb.constraints = RigidbodyConstraints.FreezeAll;
        var motor = player.GetComponent<CharacterMotor>();
        if (motor == null) motor = player.AddComponent<CharacterMotor>();
        motor.maxSlope = 50f; motor.stepHeight = 0.4f; motor.snapDistance = 0.5f; motor.gravity = 22f; motor.skin = 0.01f;
        motor.collisionMask = Physics.AllLayers; motor.smoothedChild = cameraPivot;
        EditorUtility.SetDirty(player);
        return motor;
    }

    /// <summary>"player": the player of the open hill level on the CharacterMotor, scene saved.</summary>
    public static void InstallInOpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != KothMapGenerator.ScenePath) scene = EditorSceneManager.OpenScene(KothMapGenerator.ScenePath, OpenSceneMode.Single);
        var fp = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        if (fp == null) { EditorAutomation.Log("PLAYER_FAILED: no player in " + scene.path); return; }
        var motor = Configure(fp.gameObject, fp.cameraPivot);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        EditorAutomation.Log("  player: CharacterMotor, capsule " + Height + " x " + Radius + " m, max slope " + motor.maxSlope + " deg, step " + motor.stepHeight
                             + " m, character controllers left " + fp.GetComponents<CharacterController>().Length + "; scene saved " + saved);
        EditorAutomation.Log("PLAYER_OK");
    }
}

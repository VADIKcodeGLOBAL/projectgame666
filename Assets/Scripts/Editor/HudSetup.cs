using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes the HUD canvas (HudView) into the level so it can be seen and adjusted in the editor.
/// Called by the hill map generator; command "hud" rebuilds it in the open level (edits made by hand to the old one are lost).
/// </summary>
public static class HudSetup
{
    public static HudView Build(SurvivalHud hud)
    {
        if (hud.view != null) Object.DestroyImmediate(hud.view.gameObject);
        foreach (var old in hud.GetComponentsInChildren<HudView>(true)) Object.DestroyImmediate(old.gameObject);
        var v = HudView.Create(hud.transform);
        if (hud.weapons != null && hud.weapons.weapons != null)
        {
            var names = new string[hud.weapons.weapons.Length];
            for (int i = 0; i < names.Length; i++) names[i] = (i + 1) + " " + (hud.weapons.weapons[i] != null ? hud.weapons.weapons[i].SlotLabel : "-");
            v.BuildSlots(names);
        }
        hud.view = v;
        EditorUtility.SetDirty(hud);
        return v;
    }

    /// <summary>"hud": a fresh HUD canvas in the open hill level, scene saved.</summary>
    public static void InstallInOpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != KothMapGenerator.ScenePath) scene = EditorSceneManager.OpenScene(KothMapGenerator.ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<SurvivalHud>();
        if (hud == null) { EditorAutomation.Log("HUD_FAILED: no SurvivalHud in " + scene.path); return; }
        var v = Build(hud);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        EditorAutomation.Log("  hud canvas: " + v.GetComponentsInChildren<Canvas>(true).Length + " canvases, " + v.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length
                             + " graphics, " + (v.slotTexts != null ? v.slotTexts.Length : 0) + " weapon slots; scene saved " + saved);
        EditorAutomation.Log("HUD_OK");
    }
}

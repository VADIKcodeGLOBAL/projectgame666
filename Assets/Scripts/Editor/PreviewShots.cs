using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders of objects in a preview scene (nothing touches the open scene): a key and a fill light, a grey background,
/// several views side by side in one picture under Logs/shots.
/// </summary>
public static class PreviewShots
{
    /// <summary>A new preview scene with the two lights; close it with EditorSceneManager.ClosePreviewScene.</summary>
    public static Scene Begin()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var key = Hidden<Light>(scene, "Key"); key.type = LightType.Directional; key.intensity = 1.15f; key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
        var fill = Hidden<Light>(scene, "Fill"); fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.75f, 0.82f, 1f); fill.transform.rotation = Quaternion.Euler(20f, -40f, 0f);
        return scene;
    }

    public static T Hidden<T>(Scene scene, string name) where T : Component
    {
        var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, typeof(T));
        SceneManager.MoveGameObjectToScene(go, scene);
        return go.GetComponent<T>();
    }

    /// <summary>Bounds of all renderers under root.</summary>
    public static Bounds BoundsOf(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(root.transform.position, Vector3.one);
        foreach (var r in rends) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>
    /// Panels side by side, one per entry of frames: each frame is (centre, height to fit, view direction from the centre
    /// towards the camera). Saved as Logs/shots/&lt;name&gt;.png.
    /// </summary>
    public static void Render(Scene scene, string name, int panelW, int h, params Frame[] frames) { RenderGrid(scene, name, frames.Length, panelW, h, frames); }

    /// <summary>The same in rows of cols panels (first row at the top).</summary>
    public static void RenderGrid(Scene scene, string name, int cols, int panelW, int panelH, params Frame[] frames)
    {
        var cam = Hidden<Camera>(scene, "Cam"); cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.32f, 0.34f, 0.37f);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
        int rows = (frames.Length + cols - 1) / cols, w = panelW * cols, h = panelH * rows;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        RenderTexture.active = rt; GL.Clear(true, true, cam.backgroundColor); RenderTexture.active = null;
        for (int k = 0; k < frames.Length; k++)
        {
            var f = frames[k];
            if (f.before != null) f.before();
            int col = k % cols, row = k / cols;
            cam.rect = new Rect(col / (float)cols, 1f - (row + 1) / (float)rows, 1f / cols, 1f / rows);
            cam.aspect = panelW / (float)panelH;
            float dist = f.height * 0.5f / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.12f;
            cam.transform.position = f.centre + f.view.normalized * dist; cam.transform.rotation = Quaternion.LookRotation(-f.view.normalized);
            cam.Render();
        }
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        Directory.CreateDirectory(EditorAutomation.ShotDir);
        File.WriteAllBytes(EditorAutomation.ShotDir + "/" + name + ".png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(cam.gameObject);
        Thaw();
    }

    static readonly System.Collections.Generic.List<GameObject> frozen = new System.Collections.Generic.List<GameObject>();
    static readonly System.Collections.Generic.List<SkinnedMeshRenderer> hidden = new System.Collections.Generic.List<SkinnedMeshRenderer>();

    /// <summary>
    /// Skinned meshes are skinned once per editor frame, not per Camera.Render: a pose set between two renders of the same frame
    /// would not show. This bakes the skinned meshes under root as they are posed now into plain meshes (shown instead of them
    /// until the render ends).
    /// </summary>
    public static void Freeze(GameObject root)
    {
        Thaw();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            var mesh = new Mesh { name = smr.name + " (frozen)" }; smr.BakeMesh(mesh, false);   // world size, in the renderer's position and turn
            var go = EditorUtility.CreateGameObjectWithHideFlags("Frozen", HideFlags.HideAndDontSave, typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(go, root.scene);
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
            smr.forceRenderingOff = true; hidden.Add(smr); frozen.Add(go);
        }
    }

    /// <summary>Back to the live skinned meshes.</summary>
    public static void Thaw()
    {
        foreach (var go in frozen) if (go != null) { Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(go); }
        foreach (var smr in hidden) if (smr != null) smr.forceRenderingOff = false;
        frozen.Clear(); hidden.Clear();
    }

    public struct Frame
    {
        public Vector3 centre, view; public float height; public System.Action before;
        public Frame(Vector3 centre, float height, Vector3 view, System.Action before = null) { this.centre = centre; this.height = height; this.view = view; this.before = before; }
    }
}

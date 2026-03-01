using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SRValidationDebugTools
{
    private const string ValidationScenePath = "Assets/SRExtractionValidation/Scenes/SR-Extract-Validation.unity";

    public static void FixValidationCameraAndRenderers()
    {
        var scene = EnsureValidationSceneOpen();
        if (!scene.IsValid())
        {
            Debug.LogError("[FxLab Debug] Validation scene is not valid.");
            return;
        }

        var model = GameObject.Find("MODEL_Target");
        if (model == null)
        {
            Debug.LogError("[FxLab Debug] MODEL_Target not found.");
            return;
        }

        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var r in renderers)
        {
            if (r == null)
            {
                continue;
            }
            r.enabled = true;
            r.forceRenderingOff = false;
            r.updateWhenOffscreen = true;
            EditorUtility.SetDirty(r);
        }

        var cameraGo = GameObject.Find("CAM_Main");
        if (cameraGo != null)
        {
            var cam = cameraGo.GetComponent<Camera>();
            if (cam != null)
            {
                var bounds = CalculateBounds(renderers, model.transform.position);
                var center = bounds.center;
                var radius = Mathf.Max(0.5f, bounds.extents.magnitude);
                var distance = Mathf.Max(2.0f, radius * 2.6f);

                var pos = center + new Vector3(0f, radius * 0.35f, -distance);
                cameraGo.transform.position = pos;
                cameraGo.transform.LookAt(center + new Vector3(0f, radius * 0.1f, 0f));

                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 500f;
                cam.fieldOfView = 40f;
                EditorUtility.SetDirty(cam);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[FxLab Debug] Fixed validation scene. Renderers={renderers.Length}");
    }

    public static void LogValidationDiagnostics()
    {
        var scene = EnsureValidationSceneOpen();
        if (!scene.IsValid())
        {
            Debug.LogError("[FxLab Debug] Validation scene is not valid.");
            return;
        }

        var model = GameObject.Find("MODEL_Target");
        var camGo = GameObject.Find("CAM_Main");
        var cam = camGo != null ? camGo.GetComponent<Camera>() : null;

        Debug.Log($"[FxLab Debug] Model found: {model != null}, Cam found: {cam != null}");
        if (model != null)
        {
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = CalculateBounds(renderers, model.transform.position);
            Debug.Log($"[FxLab Debug] Skinned renderers={renderers.Length}, boundsCenter={bounds.center}, boundsSize={bounds.size}");
            foreach (var r in renderers)
            {
                Debug.Log($"[FxLab Debug] Renderer={r.name}, enabled={r.enabled}, forceOff={r.forceRenderingOff}, updateWhenOffscreen={r.updateWhenOffscreen}, mesh={(r.sharedMesh != null ? r.sharedMesh.name : "null")}");
            }
        }

        if (cam != null)
        {
            Debug.Log($"[FxLab Debug] Camera pos={cam.transform.position}, rot={cam.transform.eulerAngles}, fov={cam.fieldOfView}, near={cam.nearClipPlane}, far={cam.farClipPlane}, cullingMask={cam.cullingMask}");
        }
    }

    private static Scene EnsureValidationSceneOpen()
    {
        var active = SceneManager.GetActiveScene();
        if (active.IsValid() && active.path == ValidationScenePath)
        {
            return active;
        }
        return EditorSceneManager.OpenScene(ValidationScenePath, OpenSceneMode.Single);
    }

    private static Bounds CalculateBounds(SkinnedMeshRenderer[] renderers, Vector3 fallbackCenter)
    {
        if (renderers == null || renderers.Length == 0)
        {
            return new Bounds(fallbackCenter, Vector3.one);
        }

        var valid = false;
        var b = new Bounds(fallbackCenter, Vector3.one);
        foreach (var r in renderers)
        {
            if (r == null || r.sharedMesh == null)
            {
                continue;
            }
            if (!valid)
            {
                b = r.bounds;
                valid = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }

        return valid ? b : new Bounds(fallbackCenter, Vector3.one);
    }
}



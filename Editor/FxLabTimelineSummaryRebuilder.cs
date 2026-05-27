using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

public static class FxLabTimelineSummaryRebuilder
{
    private const string LogPrefix = "[SRTimelineRebuilder]";
    private const string GeneratedRoot = "Assets/SRExtractionValidation/Generated/SRTimelineRecovered";
    private const string SummaryFolderPrefKey = "SRExtractionValidation.SR.DefaultTimelineSummaryFolder";
    private const string LegacySummaryFolderPrefKey = "AllEffectsLab.SR.DefaultTimelineSummaryFolder";
    private const string ValidationScenePath = "Assets/SRExtractionValidation/Scenes/SR-Extract-Validation.unity";
    private const string DebugMaterialPath = "Assets/SRExtractionValidation/Generated/SRExtractValidation/SR_Debug_Opaque.mat";
    private const string DiagnosticsOutputPath = "Assets/SRExtractionValidation/Screenshots/sr_renderer_diag.txt";
    private static string ProbeOutputPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "sr_mcp_probe.txt");

    [Serializable]
    private class TimelineSummary
    {
        public string Name;
        public string Source;
        public string Container;
        public string MonoClass;
        public string MonoNamespace;
        public string MonoAssembly;
        public int RefCount;
    }

    public static void RebuildFromSummaryFolder()
    {
        var folder = EditorUtility.OpenFolderPanel("Select timeline summary folder", "", "");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }
        RebuildFromFolder(folder);
    }

    public static void WriteMcpProbe()
    {
        try
        {
            var content =
                "utc=" + DateTime.UtcNow.ToString("o") + Environment.NewLine +
                "dataPath=" + Application.dataPath + Environment.NewLine +
                "company=" + Application.companyName + Environment.NewLine +
                "product=" + Application.productName + Environment.NewLine;
            File.WriteAllText(ProbeOutputPath, content);
            Debug.Log($"{LogPrefix} Probe written: {ProbeOutputPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"{LogPrefix} Probe write failed: {e.Message}");
        }
    }

    public static void RebuildFromDefaultSummary()
    {
        var summaryFolder = ResolveDefaultSummaryFolder();
        if (string.IsNullOrEmpty(summaryFolder) || !Directory.Exists(summaryFolder))
        {
            Debug.LogError($"{LogPrefix} Default summary folder not found. Current='{summaryFolder}'.");
            return;
        }

        RebuildFromFolder(summaryFolder);
    }

    public static void SetDefaultSummaryFolder()
    {
        var folder = EditorUtility.OpenFolderPanel("Select default SR timeline summary folder", "", "");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Debug.LogWarning($"{LogPrefix} Folder selection canceled.");
            return;
        }

        EditorPrefs.SetString(SummaryFolderPrefKey, folder);
        Debug.Log($"{LogPrefix} Default summary folder set: {folder}");
    }

    public static void AutoSetDefaultSummaryFolderLatest()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var root = Path.Combine(desktop, "SR_Workspace", "_codex_workspace");
        if (!Directory.Exists(root))
        {
            Debug.LogError($"{LogPrefix} Codex workspace not found: {root}");
            return;
        }

        var candidates = Directory.GetDirectories(root, "timeline_extract_*", SearchOption.TopDirectoryOnly)
            .Select(dir =>
            {
                var monoDir = Path.Combine(dir, "MonoBehaviour");
                var summaryCount = Directory.Exists(monoDir)
                    ? Directory.GetFiles(monoDir, "*__timeline_summary.json", SearchOption.AllDirectories).Length
                    : 0;
                var mtime = new DirectoryInfo(dir).LastWriteTime;
                return new { dir, monoDir, summaryCount, mtime };
            })
            .Where(x => Directory.Exists(x.monoDir))
            .OrderByDescending(x => x.summaryCount)
            .ThenByDescending(x => x.mtime)
            .ToList();

        var pick = candidates.FirstOrDefault();
        if (pick == null)
        {
            Debug.LogError($"{LogPrefix} No timeline_extract_* MonoBehaviour folder found.");
            return;
        }

        EditorPrefs.SetString(SummaryFolderPrefKey, pick.monoDir);
        Debug.Log($"{LogPrefix} Auto set default summary folder: {pick.monoDir} (summaryCount={pick.summaryCount})");
    }

    public static void AutoSetDefaultSummaryFolderLatestAlias()
    {
        AutoSetDefaultSummaryFolderLatest();
    }

    public static void RebuildAndApplyToValidationScene()
    {
        RebuildFromDefaultSummary();
        ApplyRecoveredToValidationScene();
    }

    public static void RebuildAndApplyToValidationSceneForRole(string roleKey)
    {
        var summaryFolder = ResolveDefaultSummaryFolder();
        if (string.IsNullOrEmpty(summaryFolder) || !Directory.Exists(summaryFolder))
        {
            Debug.LogError($"{LogPrefix} Default summary folder not found. Current='{summaryFolder}'.");
            return;
        }

        RebuildFromFolder(summaryFolder, roleKey);
        ApplyRecoveredToValidationScene();
    }

    public static void ApplyRecoveredToValidationScene()
    {
        ApplyRecoveredTimelineToValidationScene();
    }

    private sealed class ClipSearchIndex
    {
        public readonly List<ClipEntry> All = new List<ClipEntry>();
        public readonly Dictionary<string, List<int>> TokenToIndices = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ClipEntry
    {
        public AnimationClip Clip;
        public string NormalizedName;
        public string[] Tokens;
    }

    public static void ConfigureDirectorForPreview(PlayableDirector director, bool autoPlay)
    {
        if (director == null)
        {
            return;
        }

        AutoBindTimeline(director);
        director.time = 0d;
        director.Evaluate();
        if (autoPlay)
        {
            director.Play();
        }
    }

    public static void FixValidationCameraAndRenderers()
    {
        var lines = new List<string>();
        if (!SceneAssetExists(ValidationScenePath))
        {
            Debug.LogError($"[Timeline Rebuilder] Validation scene not found: {ValidationScenePath}");
            return;
        }

        var scene = EnsureValidationSceneOpen();
        if (!scene.IsValid())
        {
            Debug.LogError("[Timeline Rebuilder] Validation scene is not available.");
            return;
        }

        var model = GameObject.Find("MODEL_Target");
        if (model == null)
        {
            Debug.LogWarning("[Timeline Rebuilder] MODEL_Target not found.");
            return;
        }

        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Bounds bounds = new Bounds(model.transform.position, Vector3.one);
        var hasBounds = false;
        foreach (var r in renderers)
        {
            if (r == null)
            {
                continue;
            }
            r.enabled = true;
            r.forceRenderingOff = false;
            r.updateWhenOffscreen = true;
            r.localBounds = new Bounds(new Vector3(0f, 1.0f, 0f), new Vector3(8f, 12f, 8f));
            if (r.sharedMesh != null)
            {
                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            EditorUtility.SetDirty(r);
        }

        var camGo = GameObject.Find("CAM_Main");
        var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
        if (cam != null)
        {
            var center = hasBounds ? bounds.center : model.transform.position + new Vector3(0f, 1.0f, 0f);
            var radius = hasBounds ? Mathf.Max(0.6f, bounds.extents.magnitude) : 1.0f;
            var distance = Mathf.Max(2.5f, radius * 2.4f);
            camGo.transform.position = center + new Vector3(0f, radius * 0.25f, -distance);
            camGo.transform.LookAt(center + new Vector3(0f, radius * 0.1f, 0f));
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 500f;
            cam.fieldOfView = 40f;
            EditorUtility.SetDirty(cam);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Timeline Rebuilder] Fixed validation camera/renderers. Skinned renderers: {renderers.Length}");
    }

    public static void ApplyDebugOpaqueMaterial()
    {
        var lines = new List<string>();
        if (!SceneAssetExists(ValidationScenePath))
        {
            Debug.LogError($"[Timeline Rebuilder] Validation scene not found: {ValidationScenePath}");
            return;
        }

        var scene = GetActiveValidationScene();
        if (!scene.IsValid())
        {
            Debug.LogError("[Timeline Rebuilder] Validation scene is not active.");
            return;
        }

        EnsureFolder("Assets/SRExtractionValidation/Generated/SRExtractValidation");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(DebugMaterialPath);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                Debug.LogError("[Timeline Rebuilder] No suitable shader found for debug material.");
                return;
            }
            mat = new Material(shader);
            mat.name = "SR_Debug_Opaque";
            mat.SetColor("_BaseColor", new Color(0.78f, 0.83f, 0.92f, 1.0f));
            AssetDatabase.CreateAsset(mat, DebugMaterialPath);
        }

        var model = GameObject.Find("MODEL_Target");
        if (model == null)
        {
            Debug.LogWarning("[Timeline Rebuilder] MODEL_Target not found.");
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
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                r.sharedMaterial = mat;
            }
            else
            {
                for (var i = 0; i < mats.Length; i++)
                {
                    mats[i] = mat;
                }
                r.sharedMaterials = mats;
            }
            EditorUtility.SetDirty(r);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Timeline Rebuilder] Applied debug opaque material to {renderers.Length} skinned renderers.");
    }

    public static void LogModelRendererDiagnostics()
    {
        if (!SceneAssetExists(ValidationScenePath))
        {
            Debug.LogError($"[Timeline Rebuilder] Validation scene not found: {ValidationScenePath}");
            return;
        }

        var scene = GetActiveValidationScene();
        if (!scene.IsValid())
        {
            Debug.LogError("[Timeline Rebuilder] Validation scene is not active.");
            return;
        }

        var lines = new List<string>();
        var model = GameObject.Find("MODEL_Target");
        lines.Add($"modelFound={model != null}");
        if (model == null)
        {
            WriteDiagnostics(lines);
            return;
        }

        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        lines.Add($"skinnedRendererCount={renderers.Length}");
        foreach (var r in renderers)
        {
            if (r == null)
            {
                continue;
            }

            var mesh = r.sharedMesh;
            var meshName = mesh != null ? mesh.name : "null";
            var bindPoseCount = mesh != null ? mesh.bindposes.Length : 0;
            var bonesCount = r.bones != null ? r.bones.Length : 0;
            var rootBoneName = r.rootBone != null ? r.rootBone.name : "null";
            lines.Add($"renderer={r.name}, enabled={r.enabled}, forceOff={r.forceRenderingOff}, boundsCenter={r.bounds.center}, boundsSize={r.bounds.size}, localBoundsCenter={r.localBounds.center}, localBoundsSize={r.localBounds.size}, lossyScale={r.transform.lossyScale}, mesh={meshName}, bindPoses={bindPoseCount}, bones={bonesCount}, rootBone={rootBoneName}");
        }

        var camGo = GameObject.Find("CAM_Main");
        var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
        if (cam != null)
        {
            lines.Add($"camPos={cam.transform.position}, camEuler={cam.transform.eulerAngles}, fov={cam.fieldOfView}, near={cam.nearClipPlane}, far={cam.farClipPlane}");
        }

        WriteDiagnostics(lines);
    }

    public static void SwapModelToPrimaryRoleAsset()
    {
        if (!SceneAssetExists(ValidationScenePath))
        {
            Debug.LogError($"[Timeline Rebuilder] Validation scene not found: {ValidationScenePath}");
            return;
        }

        var scene = GetActiveValidationScene();
        if (!scene.IsValid())
        {
            Debug.LogError("[Timeline Rebuilder] Validation scene is not active.");
            return;
        }

        var modelRoot = GameObject.Find("MODEL_Root");
        if (modelRoot == null)
        {
            Debug.LogError("[Timeline Rebuilder] MODEL_Root not found.");
            return;
        }

        var prefab = FindPreferredRoleModelPrefab();
        if (prefab == null)
        {
            Debug.LogError($"{LogPrefix} No suitable role model prefab/fbx found.");
            return;
        }

        for (var i = modelRoot.transform.childCount - 1; i >= 0; i--)
        {
            var child = modelRoot.transform.GetChild(i);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
        {
            Debug.LogError($"{LogPrefix} Failed to instantiate role model asset.");
            return;
        }

        instance.name = "MODEL_Target";
        instance.transform.SetParent(modelRoot.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"{LogPrefix} Swapped model to {prefab.name}.");
    }

    public static void DeleteDebugCube()
    {
        var cube = GameObject.Find("Cube");
        if (cube == null)
        {
            Debug.Log("[Timeline Rebuilder] Debug cube not found.");
            return;
        }
        Object.DestroyImmediate(cube);
        var scene = GetActiveValidationScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log("[Timeline Rebuilder] Debug cube deleted.");
    }

    private static void WriteDiagnostics(List<string> lines)
    {
        try
        {
            var abs = Path.GetFullPath(DiagnosticsOutputPath);
            var folder = Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            File.WriteAllLines(abs, lines ?? new List<string>());
            AssetDatabase.Refresh();
            Debug.Log($"[Timeline Rebuilder] Wrote diagnostics: {DiagnosticsOutputPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Timeline Rebuilder] Failed to write diagnostics: {e.Message}");
        }
    }

    private static void ApplyRecoveredTimelineToValidationScene()
    {

        if (!SceneAssetExists(ValidationScenePath))
        {
            Debug.LogError($"[Timeline Rebuilder] Validation scene not found: {ValidationScenePath}");
            return;
        }

        var scene = GetActiveValidationScene();
        if (!scene.IsValid())
        {
            Debug.LogError("[Timeline Rebuilder] Validation scene is not active.");
            return;
        }

        var timeline = LoadPreferredOrBestTimeline();
        if (timeline == null)
        {
            Debug.LogWarning("[Timeline Rebuilder] No recovered timeline asset found to apply.");
            return;
        }

        var director = Object.FindObjectOfType<PlayableDirector>();
        if (director == null)
        {
            var go = new GameObject("Timeline_Director");
            director = go.AddComponent<PlayableDirector>();
        }

        director.playableAsset = timeline;
        ConfigureDirectorForPreview(director, true);

        EditorUtility.SetDirty(director);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Timeline Rebuilder] Applied timeline to scene: {timeline.name}");
    }

    private static TimelineAsset LoadPreferredOrBestTimeline()
    {
        return FindBestRecoveredTimeline();
    }

    private static GameObject FindPreferredRoleModelPrefab()
    {
        const string rolesRoot = "Assets/SRExtractionValidation/Imported/SR/Roles";
        if (!AssetDatabase.IsValidFolder(rolesRoot))
        {
            return null;
        }

        var candidates = AssetDatabase.FindAssets("t:GameObject", new[] { rolesRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var preferredPath = candidates.FirstOrDefault(path =>
            Path.GetFileNameWithoutExtension(path).StartsWith("Art_", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(preferredPath))
        {
            preferredPath = candidates.FirstOrDefault();
        }

        return string.IsNullOrEmpty(preferredPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(preferredPath);
    }

    private static TimelineAsset FindBestRecoveredTimeline()
    {
        var guids = AssetDatabase.FindAssets("t:TimelineAsset", new[] { GeneratedRoot });
        if (guids == null || guids.Length == 0)
        {
            return null;
        }

        // Prefer character timelines, then deterministic alphabetical fallback.
        var candidates = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(path => AssetDatabase.LoadAssetAtPath<TimelineAsset>(path))
            .Where(x => x != null)
            .OrderByDescending(x => x.name.IndexOf("Character_", StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.FirstOrDefault();
    }

    private static void AutoBindTimeline(PlayableDirector director)
    {
        if (director == null || !(director.playableAsset is TimelineAsset timeline))
        {
            return;
        }

        var animator = Object.FindObjectOfType<Animator>();
        var modelGo = ResolveModelTargetForBinding();
        var camera = Camera.main;
        var timelineRoot = GameObject.Find("TIMELINE_Root") ?? new GameObject("TIMELINE_Root");

        foreach (var output in timeline.outputs)
        {
            var source = output.sourceObject;
            if (source == null)
            {
                continue;
            }

            if (source is AnimationTrack)
            {
                if (animator != null)
                {
                    director.SetGenericBinding(source, animator);
                }
                continue;
            }

            if (source is ActivationTrack || source is ControlTrack)
            {
                if (modelGo != null)
                {
                    director.SetGenericBinding(source, modelGo);
                }

                if (source is ControlTrack controlTrack)
                {
                    BindControlTrackClipObjects(director, controlTrack, timelineRoot.transform, modelGo);
                }
                continue;
            }

            // Generic fallback for unknown/custom tracks.
            if (modelGo != null)
            {
                director.SetGenericBinding(source, modelGo);
            }
            else if (camera != null)
            {
                director.SetGenericBinding(source, camera.gameObject);
            }
        }
    }

    private static GameObject ResolveModelTargetForBinding()
    {
        var direct = GameObject.Find("MODEL_Target");
        if (direct != null)
        {
            return direct;
        }

        var modelRoot = GameObject.Find("MODEL_Root");
        if (modelRoot == null || modelRoot.transform.childCount == 0)
        {
            return null;
        }

        for (var i = 0; i < modelRoot.transform.childCount; i++)
        {
            var child = modelRoot.transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (child.name.StartsWith("MODEL_Target", StringComparison.OrdinalIgnoreCase))
            {
                return child.gameObject;
            }
        }

        return modelRoot.transform.GetChild(0).gameObject;
    }

    private static void BindControlTrackClipObjects(PlayableDirector director, ControlTrack track, Transform timelineRoot, GameObject fallback)
    {
        if (director == null || track == null || timelineRoot == null)
        {
            return;
        }

        var trackAnchorName = string.IsNullOrWhiteSpace(track.name) ? "ControlTrack" : SanitizeName(track.name);
        var trackAnchor = timelineRoot.Find(trackAnchorName);
        if (trackAnchor == null)
        {
            var go = new GameObject(trackAnchorName);
            go.transform.SetParent(timelineRoot, false);
            trackAnchor = go.transform;
        }

        var clipIndex = 0;
        foreach (var clip in track.GetClips())
        {
            clipIndex++;
            if (!(clip.asset is ControlPlayableAsset controlAsset))
            {
                continue;
            }

            var clipAnchorName = BuildClipAnchorName(clip, clipIndex);
            var clipAnchor = trackAnchor.Find(clipAnchorName);
            if (clipAnchor == null)
            {
                var go = new GameObject(clipAnchorName);
                go.transform.SetParent(trackAnchor, false);
                clipAnchor = go.transform;
            }

            var sourceRef = controlAsset.sourceGameObject;
            if (sourceRef.exposedName == default)
            {
                sourceRef.exposedName = UnityEditor.GUID.Generate().ToString();
            }
            controlAsset.sourceGameObject = sourceRef;
            director.SetReferenceValue(sourceRef.exposedName, clipAnchor.gameObject);

            // Never point ControlPlayableAsset.prefabGameObject to the live preview model.
            // That causes recursive self-instantiation in Unity Timeline runtime.
            if (controlAsset.prefabGameObject != null)
            {
                if (fallback != null && (controlAsset.prefabGameObject == fallback || IsSameOrChild(controlAsset.prefabGameObject.transform, fallback.transform)))
                {
                    controlAsset.prefabGameObject = null;
                    EditorUtility.SetDirty(controlAsset);
                }
            }
        }
    }

    private static string BuildClipAnchorName(TimelineClip clip, int index)
    {
        var name = clip != null ? (clip.displayName ?? string.Empty) : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Clip_" + index;
        }
        return SanitizeName(name);
    }

    private static string SanitizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unnamed";
        }

        var chars = raw.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray();
        if (chars.Length == 0)
        {
            return "unnamed";
        }
        return new string(chars);
    }

    private static bool IsSameOrChild(Transform node, Transform root)
    {
        if (node == null || root == null)
        {
            return false;
        }
        var t = node;
        while (t != null)
        {
            if (t == root)
            {
                return true;
            }
            t = t.parent;
        }
        return false;
    }

    private static void RebuildFromFolder(string folder)
    {
        RebuildFromFolder(folder, null);
    }

    private static void RebuildFromFolder(string folder, string roleKey)
    {
        var summaries = LoadSummaries(folder);
        if (summaries.Count == 0)
        {
            Debug.LogWarning("[Timeline Rebuilder] No valid summary json found.");
            return;
        }

        EnsureFolder(GeneratedRoot);

        var groups = summaries
            .Where(x => !string.IsNullOrEmpty(x.Container))
            .GroupBy(x => x.Container, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groups.Count == 0)
        {
            Debug.LogWarning("[Timeline Rebuilder] No container groups found.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(roleKey))
        {
            var roleAliases = BuildRoleAliases(roleKey);
            groups = groups
                .Where(g => GroupMatchesRole(g, roleAliases))
                .ToList();
            Debug.Log($"{LogPrefix} Role filter='{roleKey}', aliases=[{string.Join(", ", roleAliases)}], matched groups={groups.Count}");
        }

        var hardMaxGroups = string.IsNullOrWhiteSpace(roleKey) ? 256 : 2048;
        if (groups.Count > hardMaxGroups)
        {
            groups = groups.Take(hardMaxGroups).ToList();
            Debug.LogWarning($"{LogPrefix} Group count trimmed to {hardMaxGroups} for stability.");
        }

        var rebuilt = 0;
        var clipIndex = BuildClipSearchIndex();
        foreach (var group in groups)
        {
            if (!TryBuildTimelineAsset(group.Key, group.ToList(), clipIndex, out var assetPath))
            {
                continue;
            }
            rebuilt++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Timeline Rebuilder] Done. Rebuilt {rebuilt} timeline asset(s).");
    }

    private static bool GroupMatchesRole(IGrouping<string, TimelineSummary> group, List<string> normalizedRoles)
    {
        if (group == null || normalizedRoles == null || normalizedRoles.Count == 0)
        {
            return false;
        }

        var groupKey = Normalize(group.Key);
        if (normalizedRoles.Any(role => groupKey.Contains(role)))
        {
            return true;
        }

        foreach (var e in group)
        {
            if (e == null)
            {
                continue;
            }

            var name = Normalize(e.Name);
            var source = Normalize(e.Source);
            var container = Normalize(e.Container);
            if (normalizedRoles.Any(role =>
                    name.Contains(role) ||
                    source.Contains(role) ||
                    container.Contains(role)))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> BuildRoleAliases(string roleKey)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string value)
        {
            var n = Normalize(value);
            if (!string.IsNullOrEmpty(n))
            {
                aliases.Add(n);
            }
        }

        Add(roleKey);
        if (string.IsNullOrEmpty(roleKey))
        {
            return aliases.ToList();
        }

        if (roleKey.Equals("Sparxie", StringComparison.OrdinalIgnoreCase) ||
            roleKey.Equals("Sparkle", StringComparison.OrdinalIgnoreCase) ||
            roleKey.Equals("Hanabi", StringComparison.OrdinalIgnoreCase))
        {
            Add("Sparxie");
            Add("Sparkle");
            Add("Hanabi");
        }

        return aliases.ToList();
    }

    private static bool TryBuildTimelineAsset(string container, List<TimelineSummary> entries, ClipSearchIndex clipIndex, out string assetPath)
    {
        assetPath = string.Empty;
        if (entries == null || entries.Count == 0)
        {
            return false;
        }

        var nonTimelineEntries = entries
            .Where(x => x != null && !string.IsNullOrEmpty(x.MonoClass))
            .Where(x => !string.Equals(x.MonoClass, "TimelineAsset", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var timelineEntry = entries.FirstOrDefault(x =>
            string.Equals(x.MonoClass, "TimelineAsset", StringComparison.OrdinalIgnoreCase));
        var timelineName = timelineEntry != null && !string.IsNullOrEmpty(timelineEntry.Name)
            ? timelineEntry.Name
            : Path.GetFileNameWithoutExtension(container);
        if (string.IsNullOrEmpty(timelineName))
        {
            timelineName = "RecoveredTimeline";
        }

        var safeName = SanitizeFileName(timelineName);
        assetPath = $"{GeneratedRoot}/{safeName}.playable";

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(assetPath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, assetPath);
        }

        foreach (var track in timeline.GetOutputTracks().ToArray())
        {
            timeline.DeleteTrack(track);
        }

        var clipCandidates = FindCandidateClips(timelineName, container, clipIndex).ToList();
        BuildAnimationTrack(timeline, clipCandidates);
        BuildPlaceholderTracks(timeline, entries);

        EditorUtility.SetDirty(timeline);
        return true;
    }

    private static void BuildAnimationTrack(TimelineAsset timeline, List<AnimationClip> clips)
    {
        var track = timeline.CreateTrack<AnimationTrack>(null, "Recovered Animation");
        if (clips == null || clips.Count == 0)
        {
            var empty = track.CreateDefaultClip();
            empty.displayName = "No matched AnimationClip";
            empty.start = 0.0;
            empty.duration = 1.0;
            return;
        }

        var t = 0.0;
        foreach (var clip in clips.Take(12))
        {
            if (clip == null)
            {
                continue;
            }

            var timelineClip = track.CreateDefaultClip();
            timelineClip.displayName = clip.name;
            timelineClip.start = t;
            timelineClip.duration = Mathf.Max(0.05f, clip.length);
            var asset = timelineClip.asset as AnimationPlayableAsset;
            if (asset != null)
            {
                asset.clip = clip;
            }
            t += timelineClip.duration + 0.05;
        }
    }

    private static void BuildPlaceholderTracks(TimelineAsset timeline, List<TimelineSummary> entries)
    {
        var groupTrack = timeline.CreateTrack<GroupTrack>(null, "Recovered Metadata");
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackIndex = 1;

        foreach (var entry in entries)
        {
            if (entry == null)
            {
                continue;
            }
            if (string.Equals(entry.MonoClass, "TimelineAsset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rawName = !string.IsNullOrEmpty(entry.Name) ? entry.Name : $"Track_{fallbackIndex++}";
            var trackName = EnsureUnique(rawName, usedNames);
            var monoClass = entry.MonoClass ?? string.Empty;

            if (monoClass.IndexOf("ActivationTrack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var activationTrack = timeline.CreateTrack<ActivationTrack>(groupTrack, trackName);
                var activationClip = activationTrack.CreateDefaultClip();
                activationClip.displayName = $"{trackName} (placeholder)";
                activationClip.start = 0.0;
                activationClip.duration = 1.0;
                continue;
            }

            if (monoClass.IndexOf("ControlTrack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var controlTrack = timeline.CreateTrack<ControlTrack>(groupTrack, trackName);
                var controlClip = controlTrack.CreateDefaultClip();
                controlClip.displayName = $"{trackName} (placeholder)";
                controlClip.start = 0.0;
                controlClip.duration = 1.0;
                EnsureControlClipReference(controlClip, trackName, 1);
                continue;
            }

            var fallbackTrack = timeline.CreateTrack<ControlTrack>(groupTrack, trackName);
            var clip = fallbackTrack.CreateDefaultClip();
            clip.displayName = $"{trackName} (marker-placeholder)";
            clip.start = 0.0;
            clip.duration = 0.1;
            EnsureControlClipReference(clip, trackName, 1);
        }
    }

    private static void EnsureControlClipReference(TimelineClip clip, string trackName, int index)
    {
        if (!(clip?.asset is ControlPlayableAsset controlAsset))
        {
            return;
        }

        var sourceRef = controlAsset.sourceGameObject;
        if (sourceRef.exposedName == default)
        {
            var seed = $"{SanitizeName(trackName)}_{index}_{Guid.NewGuid():N}";
            sourceRef.exposedName = new PropertyName(seed);
            controlAsset.sourceGameObject = sourceRef;
            EditorUtility.SetDirty(controlAsset);
        }
    }

    private static IEnumerable<AnimationClip> FindCandidateClips(string timelineName, string container, ClipSearchIndex index)
    {
        if (index == null || index.All.Count == 0)
        {
            return Enumerable.Empty<AnimationClip>();
        }

        var keywords = ExtractKeywords(timelineName)
            .Concat(ExtractKeywords(container))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var candidateIndices = new HashSet<int>();
        foreach (var keyword in keywords)
        {
            var k = Normalize(keyword);
            if (string.IsNullOrEmpty(k) || k.Length < 3)
            {
                continue;
            }

            if (index.TokenToIndices.TryGetValue(k, out var list))
            {
                foreach (var idx in list)
                {
                    candidateIndices.Add(idx);
                }
            }
        }

        if (candidateIndices.Count == 0)
        {
            return Enumerable.Empty<AnimationClip>();
        }

        var scored = new List<(AnimationClip clip, int score)>(candidateIndices.Count);
        foreach (var idx in candidateIndices)
        {
            if (idx < 0 || idx >= index.All.Count)
            {
                continue;
            }

            var entry = index.All[idx];
            if (entry == null || entry.Clip == null)
            {
                continue;
            }

            var score = ScoreClip(entry.NormalizedName, keywords, true);
            if (score <= 0)
            {
                continue;
            }
            scored.Add((entry.Clip, score));
        }

        return scored
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.clip.name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.clip)
            .Distinct()
            .Take(24);
    }

    private static int ScoreClip(string clipNameOrNormalized, string[] keywords, bool clipNameAlreadyNormalized = false)
    {
        if (string.IsNullOrEmpty(clipNameOrNormalized) || keywords == null || keywords.Length == 0)
        {
            return 0;
        }

        var normalized = clipNameAlreadyNormalized ? clipNameOrNormalized : Normalize(clipNameOrNormalized);
        var score = 0;
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrEmpty(keyword) || keyword.Length < 3)
            {
                continue;
            }
            if (normalized.Contains(Normalize(keyword)))
            {
                score += 10;
            }
        }

        if (normalized.Contains("camera"))
        {
            score -= 3;
        }

        return score;
    }

    private static ClipSearchIndex BuildClipSearchIndex()
    {
        var index = new ClipSearchIndex();
        var clipFolders = new[] { "Assets/SRExtractionValidation/Imported/SR/Roles", "Assets/SRExtractionValidation" }
            .Where(AssetDatabase.IsValidFolder)
            .Distinct()
            .ToArray();
        if (clipFolders.Length == 0)
        {
            return index;
        }

        var guids = AssetDatabase.FindAssets("t:AnimationClip", clipFolders);
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                continue;
            }

            var normalized = Normalize(clip.name ?? string.Empty);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            var tokens = normalized
                .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var entry = new ClipEntry
            {
                Clip = clip,
                NormalizedName = normalized,
                Tokens = tokens
            };

            var entryIndex = index.All.Count;
            index.All.Add(entry);

            foreach (var token in tokens)
            {
                if (!index.TokenToIndices.TryGetValue(token, out var list))
                {
                    list = new List<int>();
                    index.TokenToIndices[token] = list;
                }
                list.Add(entryIndex);
            }
        }

        Debug.Log($"{LogPrefix} Clip index built. clips={index.All.Count}, tokens={index.TokenToIndices.Count}");
        return index;
    }

    private static IEnumerable<string> ExtractKeywords(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "assets", "asbres", "camera", "timeline", "track", "playable",
            "character", "avatar", "skill", "passiveskill", "mono", "behaviour"
        };

        var tokens = text
            .Replace("\\", "/")
            .Split(new[] { '/', '_', '-', '.', '(', ')', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var t = token.Trim();
            if (t.Length < 3)
            {
                continue;
            }
            if (stopWords.Contains(t))
            {
                continue;
            }
            yield return t;
        }
    }

    private static List<TimelineSummary> LoadSummaries(string folder)
    {
        var result = new List<TimelineSummary>();
        var files = Directory.EnumerateFiles(folder, "*__timeline_summary.json", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }
                var text = File.ReadAllText(file);
                var item = JsonUtility.FromJson<TimelineSummary>(text);
                if (item == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(item.Container))
                {
                    continue;
                }
                result.Add(item);
            }
            catch (Exception e)
            {
                if (e is FileNotFoundException || e is DirectoryNotFoundException)
                {
                    continue;
                }
                Debug.LogWarning($"[Timeline Rebuilder] Failed to parse summary: {file}\n{e.Message}");
            }
        }
        return result;
    }

    private static string EnsureUnique(string baseName, HashSet<string> usedNames)
    {
        var name = string.IsNullOrEmpty(baseName) ? "Track" : baseName;
        if (usedNames.Add(name))
        {
            return name;
        }

        var i = 2;
        while (true)
        {
            var test = $"{name} ({i})";
            if (usedNames.Add(test))
            {
                return test;
            }
            i++;
        }
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "RecoveredTimeline";
        }
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(ch, '_');
        }
        return input.Trim();
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        return new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static bool SceneAssetExists(string sceneAssetPath)
    {
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(sceneAssetPath) != null;
    }

    private static string ResolveDefaultSummaryFolder()
    {
        var configured = EditorPrefs.GetString(SummaryFolderPrefKey, string.Empty);
        if (string.IsNullOrEmpty(configured))
        {
            configured = EditorPrefs.GetString(LegacySummaryFolderPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
            {
                EditorPrefs.SetString(SummaryFolderPrefKey, configured);
            }
        }
        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
        {
            return configured;
        }
        var autoDetected = TryFindLatestSummaryFolderUnderCodexWorkspace();
        return string.IsNullOrEmpty(autoDetected) ? configured : autoDetected;
    }

    private static string TryFindLatestSummaryFolderUnderCodexWorkspace()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var root = Path.Combine(desktop, "SR_Workspace", "_codex_workspace");
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        var candidates = Directory.GetDirectories(root, "timeline_extract_*", SearchOption.TopDirectoryOnly)
            .Select(dir =>
            {
                var monoDir = Path.Combine(dir, "MonoBehaviour");
                var summaryCount = Directory.Exists(monoDir)
                    ? Directory.GetFiles(monoDir, "*__timeline_summary.json", SearchOption.AllDirectories).Length
                    : 0;
                var mtime = new DirectoryInfo(dir).LastWriteTime;
                return new { monoDir, summaryCount, mtime };
            })
            .Where(x => Directory.Exists(x.monoDir))
            .OrderByDescending(x => x.summaryCount)
            .ThenByDescending(x => x.mtime)
            .FirstOrDefault();

        return candidates?.monoDir ?? string.Empty;
    }

    private static Scene EnsureValidationSceneOpen()
    {
        var current = SceneManager.GetActiveScene();
        if (current.IsValid() && string.Equals(current.path, ValidationScenePath, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (!SceneAssetExists(ValidationScenePath))
        {
            return default;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return default;
        }

        return EditorSceneManager.OpenScene(ValidationScenePath, OpenSceneMode.Single);
    }

    private static Scene GetActiveValidationScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return default;
        }
        if (!string.Equals(scene.path, ValidationScenePath, StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }
        return scene;
    }
}


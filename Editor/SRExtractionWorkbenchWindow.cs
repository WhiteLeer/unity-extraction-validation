using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public class SRExtractionWorkbenchWindow : EditorWindow
{
    private const string ValidationScenePath = "Assets/unity-sr-extraction-validation/Scenes/SR-Extract-Validation.unity";
    private const string RolesRoot = "Assets/unity-sr-extraction-validation/Imported/SR/Roles";
    private const string TimelineRoot = "Assets/unity-sr-extraction-validation/Generated/SRTimelineRecovered";
    private const string TempControllerPath = "Assets/unity-sr-extraction-validation/Generated/SRExtractValidation/SR_Workbench_Preview.controller";

    private static readonly List<RoleEntry> Roles = new List<RoleEntry>();
    private static readonly List<AnimationClip> Clips = new List<AnimationClip>();
    private static readonly List<TimelineEntry> Timelines = new List<TimelineEntry>();

    private static int _roleIndex;
    private static int _clipIndex;
    private static int _timelineIndex;
    private static Queue<PipelineStep> _pipelineQueue;
    private static bool _pipelineRunning;
    private static bool _timelineRebuildRunning;

    [MenuItem("Tools/SR Extraction Panel")]
    public static void Open()
    {
        var win = GetWindow<SRExtractionWorkbenchWindow>("SR提取工作台");
        win.minSize = new Vector2(620f, 420f);
        win.Show();
    }

    private void OnEnable()
    {
        RefreshData();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("SR提取工作台", EditorStyles.boldLabel);

        DrawInitSection();
        DrawRoleSection();
        DrawClipSection();
        DrawTimelineSection();
    }

    private static void DrawInitSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("初始化", EditorStyles.boldLabel);
        if (GUILayout.Button("一键构建/清理/初始化预览场景", GUILayout.Height(34f)))
        {
            StartInitPipeline();
        }

        if (GUILayout.Button("刷新角色/动作/Timeline列表", GUILayout.Height(26f)))
        {
            RefreshData();
        }
        EditorGUILayout.EndVertical();
    }

    private static void DrawRoleSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("角色", EditorStyles.boldLabel);

        if (Roles.Count == 0)
        {
            EditorGUILayout.HelpBox("未发现角色目录: Assets/unity-sr-extraction-validation/Imported/SR/Roles", MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        _roleIndex = Mathf.Clamp(_roleIndex, 0, Roles.Count - 1);
        var names = Roles.Select(r => r.DisplayName).ToArray();
        var newIdx = EditorGUILayout.Popup("角色列表", _roleIndex, names);
        if (newIdx != _roleIndex)
        {
            _roleIndex = newIdx;
            RefreshSelectionData();
        }

        if (GUILayout.Button("应用该角色到预览场景", GUILayout.Height(26f)))
        {
            ApplySelectedRole();
        }
        EditorGUILayout.EndVertical();
    }

    private static void DrawClipSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("动作", EditorStyles.boldLabel);

        if (Clips.Count == 0)
        {
            EditorGUILayout.LabelField("当前角色无动作。", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            return;
        }

        _clipIndex = Mathf.Clamp(_clipIndex, 0, Clips.Count - 1);
        _clipIndex = EditorGUILayout.Popup("动作列表", _clipIndex, Clips.Select(c => c.name).ToArray());

        if (GUILayout.Button("应用该动作预览", GUILayout.Height(26f)))
        {
            ApplySelectedClip();
        }
        EditorGUILayout.EndVertical();
    }

    private static void DrawTimelineSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Timeline构建", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(_timelineRebuildRunning || _pipelineRunning))
        {
            if (GUILayout.Button("一键重建并导出全部Timeline诊断", GUILayout.Height(28f)))
            {
                RebuildTimelineAndExportAllDiagnostics();
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Timeline预览列表", EditorStyles.boldLabel);
        if (Timelines.Count == 0)
        {
            EditorGUILayout.LabelField("当前角色无匹配Timeline。", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            return;
        }

        _timelineIndex = Mathf.Clamp(_timelineIndex, 0, Timelines.Count - 1);
        _timelineIndex = EditorGUILayout.Popup("Timeline列表", _timelineIndex, Timelines.Select(t => t.DisplayName).ToArray());

        if (GUILayout.Button("应用该Timeline预览", GUILayout.Height(26f)))
        {
            ApplySelectedTimeline();
        }
        EditorGUILayout.EndVertical();
    }

    private static void RefreshData()
    {
        Roles.Clear();
        if (AssetDatabase.IsValidFolder(RolesRoot))
        {
            foreach (var folder in AssetDatabase.GetSubFolders(RolesRoot))
            {
                var name = Path.GetFileName(folder);
                if (!name.StartsWith("Avatar_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Roles.Add(new RoleEntry
                {
                    DisplayName = name,
                    RoleFolder = folder,
                    RoleKey = name.Split('_').Length >= 2 ? name.Split('_')[1] : name
                });
            }
        }

        Roles.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        _roleIndex = Mathf.Clamp(_roleIndex, 0, Mathf.Max(0, Roles.Count - 1));
        RefreshSelectionData();
    }

    private static void RefreshSelectionData()
    {
        Clips.Clear();
        Timelines.Clear();
        _clipIndex = 0;
        _timelineIndex = 0;

        if (Roles.Count == 0)
        {
            return;
        }

        var role = Roles[_roleIndex];

        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { role.RoleFolder }))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip != null)
            {
                Clips.Add(clip);
            }
        }
        Clips.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

        if (AssetDatabase.IsValidFolder(TimelineRoot))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                if (TimelineMatchesRole(path, name, role.RoleKey) && IsTimelineUsable(path))
                {
                    Timelines.Add(new TimelineEntry { DisplayName = name, AssetPath = path });
                }
            }
            Timelines.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void ApplySelectedRole()
    {
        if (Roles.Count == 0)
        {
            return;
        }

        if (!OpenValidationScene())
        {
            return;
        }

        var role = Roles[_roleIndex];
        var modelPath = FindModelPath(role.RoleFolder);
        if (string.IsNullOrEmpty(modelPath))
        {
            Debug.LogError("[SR工作台] 未找到角色模型: " + role.RoleFolder);
            return;
        }

        var modelRoot = GameObject.Find("MODEL_Root") ?? new GameObject("MODEL_Root");
        var modelTarget = EnsureMutableModelTarget(modelRoot.transform);
        if (modelTarget == null)
        {
            Debug.LogError("[SR工作台] 无法创建或获取可编辑的 MODEL_Target。");
            return;
        }

        ClearChildrenSafely(modelTarget.transform);
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        instance.name = modelAsset.name;
        instance.transform.SetParent(modelTarget.transform, false);

        var animator = instance.GetComponentInChildren<Animator>(true) ?? instance.AddComponent<Animator>();
        EnsureFaceDirectionBinder(instance);
        var ctl = UnityEngine.Object.FindObjectOfType<FxLabExtractValidationController>();
        if (ctl != null)
        {
            ctl.targetAnimator = animator;
            EditorUtility.SetDirty(ctl);
        }

        // Keep preview behavior consistent: apply extracted textures and refresh camera/renderer state.
        if (!SRRoleMaterialAutoBinder.BindRoleTextures(role.RoleFolder, modelTarget))
        {
            throw new InvalidOperationException("[SR工作台] 材质绑定失败，请检查角色贴图命名与路径。");
        }
        FxLabTimelineSummaryRebuilder.FixValidationCameraAndRenderers();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[SR工作台] 角色已应用: " + role.DisplayName);
    }

    private static void EnsureFaceDirectionBinder(GameObject root)
    {
        if (root == null) return;
        var binder = root.GetComponent<NprFaceDirectionBinder>();
        if (binder == null)
        {
            binder = root.AddComponent<NprFaceDirectionBinder>();
        }

        binder.AutoFindHeadBone = true;
        if (binder.HeadBone == null)
        {
            binder.HeadBone = FindHeadBone(root.transform);
        }
        EditorUtility.SetDirty(binder);
    }

    private static Transform FindHeadBone(Transform root)
    {
        if (root == null) return null;
        var all = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < all.Length; i++)
        {
            var n = all[i].name;
            if (n.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Bip001", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return all[i];
            }
        }
        return null;
    }

    private static void ApplySelectedClip()
    {
        if (Clips.Count == 0)
        {
            return;
        }

        EnsureFolder("Assets/unity-sr-extraction-validation/Generated/SRExtractValidation");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(TempControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(TempControllerPath);
        }

        var sm = controller.layers[0].stateMachine;
        foreach (var st in sm.states.ToArray())
        {
            sm.RemoveState(st.state);
        }

        var clip = Clips[_clipIndex];
        var state = sm.AddState("Preview_" + clip.name);
        state.motion = clip;
        sm.defaultState = state;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        var ctl = UnityEngine.Object.FindObjectOfType<FxLabExtractValidationController>();
        if (ctl == null || ctl.targetAnimator == null)
        {
            Debug.LogError("[SR工作台] Animator不存在，请先应用角色。");
            return;
        }

        ctl.targetAnimator.runtimeAnimatorController = controller;
        ctl.targetAnimator.Rebind();
        ctl.targetAnimator.Update(0f);
        EditorUtility.SetDirty(ctl.targetAnimator);
        Debug.Log("[SR工作台] 动作已应用: " + clip.name);
    }

    private static void ApplySelectedTimeline()
    {
        if (Timelines.Count == 0)
        {
            return;
        }

        var asset = AssetDatabase.LoadAssetAtPath<PlayableAsset>(Timelines[_timelineIndex].AssetPath);
        var ctl = UnityEngine.Object.FindObjectOfType<FxLabExtractValidationController>();
        if (ctl == null || ctl.targetDirector == null)
        {
            Debug.LogError("[SR工作台] PlayableDirector不存在，请先初始化场景。");
            return;
        }

        ctl.targetDirector.playableAsset = asset;
        FxLabTimelineSummaryRebuilder.ConfigureDirectorForPreview(ctl.targetDirector, true);
        EditorUtility.SetDirty(ctl.targetDirector);
        Debug.Log("[SR工作台] Timeline已应用: " + Timelines[_timelineIndex].DisplayName);
    }

    private static bool OpenValidationScene()
    {
        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ValidationScenePath);
        if (sceneAsset == null)
        {
            Debug.LogError("[SR工作台] 场景不存在: " + ValidationScenePath);
            return false;
        }

        var active = EditorSceneManager.GetActiveScene();
        if (active.IsValid() && string.Equals(active.path, ValidationScenePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!EditorSceneManager.SaveOpenScenes())
        {
            return false;
        }

        EditorSceneManager.OpenScene(ValidationScenePath);
        return true;
    }

    private static string FindModelPath(string roleFolder)
    {
        var paths = AssetDatabase.FindAssets("t:GameObject", new[] { roleFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var art = paths.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).StartsWith("Art_", StringComparison.OrdinalIgnoreCase));
        return art ?? paths.FirstOrDefault();
    }

    private static void StartInitPipeline()
    {
        if (_pipelineRunning)
        {
            Debug.LogWarning("[SR工作台] 一键流程已在执行中。");
            return;
        }

        _pipelineQueue = new Queue<PipelineStep>(new[]
        {
            PipelineStep.BuildScene,
            PipelineStep.BindTextures,
            PipelineStep.FixCamera,
            PipelineStep.DeleteCube,
            PipelineStep.ApplyRecoveredTimeline
        });

        _pipelineRunning = true;
        Debug.Log("[SR工作台] 一键流程开始。");
        EditorApplication.delayCall += RunNextPipelineStep;
    }

    private static void RunNextPipelineStep()
    {
        if (_pipelineQueue == null || _pipelineQueue.Count == 0)
        {
            NormalizeModelTargetHierarchy();
            _pipelineRunning = false;
            _pipelineQueue = null;
            Debug.Log("[SR工作台] 一键流程完成。");
            return;
        }

        var step = _pipelineQueue.Dequeue();
        try
        {
            switch (step)
            {
                case PipelineStep.BuildScene:
                    FxLabExtractValidationSceneBuilder.BuildScene();
                    break;
                case PipelineStep.BindTextures:
                    if (Roles.Count > 0)
                    {
                        var currentRole = Roles[Mathf.Clamp(_roleIndex, 0, Roles.Count - 1)];
                        var currentTarget = GameObject.Find("MODEL_Target");
                        if (!SRRoleMaterialAutoBinder.BindRoleTextures(currentRole.RoleFolder, currentTarget))
                        {
                            throw new InvalidOperationException("[SR工作台] 一键流程材质绑定失败。");
                        }
                        break;
                    }
                    throw new InvalidOperationException("[SR工作台] 一键流程缺少角色列表，无法绑定贴图。");
                case PipelineStep.FixCamera:
                    FxLabTimelineSummaryRebuilder.FixValidationCameraAndRenderers();
                    break;
                case PipelineStep.DeleteCube:
                    FxLabTimelineSummaryRebuilder.DeleteDebugCube();
                    break;
                case PipelineStep.ApplyRecoveredTimeline:
                    FxLabTimelineSummaryRebuilder.ApplyRecoveredToValidationScene();
                    break;
            }
        }
        catch (Exception e)
        {
            _pipelineRunning = false;
            _pipelineQueue = null;
            Debug.LogError("[SR工作台] 一键流程中断，失败步骤: " + step + "\n" + e.Message);
            return;
        }

        Debug.Log("[SR工作台] 完成步骤: " + step);
        EditorApplication.delayCall += RunNextPipelineStep;
    }

    private static void NormalizeModelTargetHierarchy()
    {
        if (!OpenValidationScene())
        {
            return;
        }

        var modelRoot = GameObject.Find("MODEL_Root");
        if (modelRoot == null)
        {
            return;
        }

        var modelTarget = GameObject.Find("MODEL_Target");
        if (modelTarget == null)
        {
            modelTarget = new GameObject("MODEL_Target");
            modelTarget.transform.SetParent(modelRoot.transform, false);
        }

        for (var i = modelRoot.transform.childCount - 1; i >= 0; i--)
        {
            var child = modelRoot.transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            var name = child.name;
            if (string.Equals(name, "MODEL_Target", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!name.StartsWith("MODEL_Target", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            // Avoid reparenting nested prefab instance children, which can freeze editor on invalid operations.
            UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
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

    private static void ClearChildrenSafely(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        var children = new List<GameObject>();
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child != null)
            {
                children.Add(child.gameObject);
            }
        }

        foreach (var go in children)
        {
            if (go == null)
            {
                continue;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (outer != null)
                {
                    try
                    {
                        PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    }
                    catch
                    {
                        // If unpack fails, fall through and try direct destroy of top-level child.
                    }
                }
            }

            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static GameObject EnsureMutableModelTarget(Transform modelRoot)
    {
        if (modelRoot == null)
        {
            return null;
        }

        var modelTarget = GameObject.Find("MODEL_Target");
        if (modelTarget == null)
        {
            modelTarget = new GameObject("MODEL_Target");
            modelTarget.transform.SetParent(modelRoot, false);
            return modelTarget;
        }

        if (modelTarget.transform.parent != modelRoot)
        {
            modelTarget.transform.SetParent(modelRoot, false);
        }

        if (PrefabUtility.IsPartOfPrefabInstance(modelTarget))
        {
            var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(modelTarget);
            if (outer != null)
            {
                PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                modelTarget = GameObject.Find("MODEL_Target");
                if (modelTarget == null)
                {
                    modelTarget = new GameObject("MODEL_Target");
                    modelTarget.transform.SetParent(modelRoot, false);
                }
            }
        }

        return modelTarget;
    }

    [Serializable]
    private class RoleEntry
    {
        public string DisplayName;
        public string RoleFolder;
        public string RoleKey;
    }

    [Serializable]
    private class TimelineEntry
    {
        public string DisplayName;
        public string AssetPath;
    }

    private enum PipelineStep
    {
        BuildScene,
        BindTextures,
        FixCamera,
        DeleteCube,
        ApplyRecoveredTimeline
    }

    private static void RebuildTimelineAndApply()
    {
        if (_timelineRebuildRunning)
        {
            Debug.LogWarning("[SR工作台] Timeline 重建已在执行中。");
            return;
        }

        _timelineRebuildRunning = true;
        try
        {
            Debug.Log("[SR工作台] 开始重建 Timeline（耗时步骤）...");
            FxLabTimelineSummaryRebuilder.AutoSetDefaultSummaryFolderLatest();
            var roleKey = Roles.Count > 0 ? (Roles[_roleIndex].RoleKey ?? string.Empty) : string.Empty;
            FxLabTimelineSummaryRebuilder.RebuildAndApplyToValidationSceneForRole(roleKey);
            Debug.Log("[SR工作台] Timeline 重建并应用完成。");
        }
        catch (Exception e)
        {
            Debug.LogError("[SR工作台] Timeline 重建失败: " + e.Message);
        }
        finally
        {
            _timelineRebuildRunning = false;
            RefreshSelectionData();
        }
    }

    private static void RebuildTimelineAndExportAllDiagnostics()
    {
        RebuildTimelineAndApply();
        var roleFolder = Roles.Count > 0 ? Roles[_roleIndex].RoleFolder : string.Empty;
        var roleKey = Roles.Count > 0 ? Roles[_roleIndex].RoleKey : string.Empty;
        SRTimelineAuditTool.AuditRecoveredTimelinesWithBindings();
        SRTimelineAuditTool.AuditControlTrackResourceLookup(roleFolder);
        SRTimelineAuditTool.AuditTimelineRebuildCoverage(roleKey);
        Debug.Log("[SR工作台] Timeline 一键流程完成：重建+引用审计+控制轨反查+覆盖率自检。");
    }

    private static bool TimelineMatchesRole(string assetPath, string timelineName, string roleKey)
    {
        if (string.IsNullOrEmpty(roleKey))
        {
            return false;
        }

        var roleAliases = BuildRoleAliases(roleKey);
        bool ContainsAlias(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return roleAliases.Any(alias => text.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (ContainsAlias(timelineName))
        {
            return true;
        }

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(assetPath);
        if (timeline == null)
        {
            return false;
        }

        foreach (var track in timeline.GetOutputTracks())
        {
            if (track == null)
            {
                continue;
            }

            foreach (var clip in track.GetClips())
            {
                var displayName = clip.displayName ?? string.Empty;
                if (ContainsAlias(displayName))
                {
                    return true;
                }

                if (clip.asset is AnimationPlayableAsset animAsset && animAsset.clip != null)
                {
                    var clipName = animAsset.clip.name ?? string.Empty;
                    if (ContainsAlias(clipName))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsTimelineUsable(string assetPath)
    {
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(assetPath);
        if (timeline == null)
        {
            return false;
        }

        var tracks = timeline.GetOutputTracks().ToList();
        if (tracks.Count == 0)
        {
            return false;
        }

        var hasAnyClip = false;
        foreach (var track in tracks)
        {
            if (track == null)
            {
                continue;
            }

            foreach (var clip in track.GetClips())
            {
                var dn = (clip.displayName ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(dn))
                {
                    continue;
                }

                if (dn.Equals("No matched AnimationClip", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hasAnyClip = true;
            }
        }

        return hasAnyClip;
    }

    private static List<string> BuildRoleAliases(string roleKey)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            roleKey
        };

        if (roleKey.Equals("Sparxie", StringComparison.OrdinalIgnoreCase) ||
            roleKey.Equals("Sparkle", StringComparison.OrdinalIgnoreCase) ||
            roleKey.Equals("Hanabi", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("Sparxie");
            aliases.Add("Sparkle");
            aliases.Add("Hanabi");
        }

        return aliases.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }
}



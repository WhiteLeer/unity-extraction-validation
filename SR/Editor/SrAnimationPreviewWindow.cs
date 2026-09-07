using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal sealed class SrAnimationPreviewWindow : EditorWindow
{
    // The preview object is temporary and is removed when the window closes.
    private const string MenuPath = "Tools/动画预览";
    private const string PreviewObjectPrefix = "__SR_AnimationPreview_";

    private GameObject modelAsset;
    private DefaultAsset animationFolder;
    private string animationFolderPath;
    private readonly List<AnimationClipEntry> clips = new();
    private string[] clipLabels = Array.Empty<string>();
    private int selectedClipIndex = -1;
    private AnimationClip selectedClip;
    private GameObject previewInstance;
    private GameObject instantiatedAsset;
    private Animator previewAnimator;
    private float previewTime;
    private float playbackSpeed = 1f;
    private bool loop = true;
    private bool isPlaying;
    private double lastUpdateTime;
    private string filter = string.Empty;
    private Vector2 scrollPosition;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        var window = GetWindow<SrAnimationPreviewWindow>();
        window.titleContent = new GUIContent("动画预览");
        window.minSize = new Vector2(420, 360);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += UpdatePlayback;
    }

    private void OnDisable()
    {
        EditorApplication.update -= UpdatePlayback;
        CleanupPreviewInstance();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.LabelField("编辑器动画预览", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "选择角色 Prefab 或 FBX，再选择动画文件夹。扫描后会在当前场景创建一个临时实例，动画直接在编辑器模式采样播放。",
            MessageType.Info);

        var newModelAsset = (GameObject)EditorGUILayout.ObjectField(
            "Prefab / FBX",
            modelAsset,
            typeof(GameObject),
            false);
        if (newModelAsset != modelAsset)
        {
            modelAsset = newModelAsset;
            if (previewInstance != null && instantiatedAsset != modelAsset)
                CleanupPreviewInstance();
        }

        var newAnimationFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "动画文件夹",
            animationFolder,
            typeof(DefaultAsset),
            false);
        if (newAnimationFolder != animationFolder)
        {
            animationFolder = newAnimationFolder;
            animationFolderPath = GetFolderPath(animationFolder);
            clips.Clear();
            clipLabels = Array.Empty<string>();
            selectedClip = null;
            selectedClipIndex = -1;
        }

        var folderIsValid = !string.IsNullOrEmpty(animationFolderPath) &&
                            AssetDatabase.IsValidFolder(animationFolderPath);
        if (animationFolder != null && !folderIsValid)
            EditorGUILayout.HelpBox("动画文件夹字段必须指向 Project 窗口中的文件夹。", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(modelAsset == null || !folderIsValid))
            {
                if (GUILayout.Button("检查并加载动画", GUILayout.Height(28)))
                    ScanAndPreparePreview();
            }

            if (GUILayout.Button("清理预览实例", GUILayout.Height(28)))
                CleanupPreviewInstance();
        }

        if (clips.Count > 0)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"动画数量: {clips.Count}", EditorStyles.boldLabel);
            filter = EditorGUILayout.TextField("筛选", filter);

            var filtered = GetFilteredClipIndices();
            var filteredLabels = filtered.Select(index => clipLabels[index]).ToArray();
            if (filtered.Length == 0)
            {
                EditorGUILayout.HelpBox("没有匹配的动画。", MessageType.Info);
            }
            else
            {
                var currentFilteredIndex = Math.Max(0, Array.IndexOf(filtered, selectedClipIndex));
                var nextFilteredIndex = EditorGUILayout.Popup("动画", currentFilteredIndex, filteredLabels);
                var nextClipIndex = filtered[Mathf.Clamp(nextFilteredIndex, 0, filtered.Length - 1)];
                if (nextClipIndex != selectedClipIndex)
                    SelectClip(nextClipIndex);

                EditorGUILayout.LabelField("当前 Clip", selectedClip != null ? selectedClip.name : "无");
                loop = EditorGUILayout.Toggle("循环", loop);
                playbackSpeed = EditorGUILayout.Slider("速度", playbackSpeed, 0f, 3f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(isPlaying ? "暂停" : "播放"))
                    {
                        isPlaying = !isPlaying;
                        lastUpdateTime = EditorApplication.timeSinceStartup;
                    }

                    if (GUILayout.Button("归零"))
                    {
                        isPlaying = false;
                        previewTime = 0f;
                        SampleSelectedClip();
                    }
                }

                if (selectedClip != null)
                {
                    var length = Mathf.Max(0.0001f, selectedClip.length);
                    var nextTime = EditorGUILayout.Slider("时间", previewTime, 0f, length);
                    if (!Mathf.Approximately(nextTime, previewTime))
                    {
                        previewTime = nextTime;
                        isPlaying = false;
                        SampleSelectedClip();
                    }

                    EditorGUILayout.LabelField($"{previewTime:0.000} / {selectedClip.length:0.000} 秒");
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("尚未加载动画。", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    private void ScanAndPreparePreview()
    {
        ScanAnimations();
        EnsurePreviewInstance();
        if (clips.Count > 0)
            SelectClip(0);
    }

    private void ScanAnimations()
    {
        clips.Clear();
        selectedClip = null;
        selectedClipIndex = -1;

        if (string.IsNullOrEmpty(animationFolderPath) || !AssetDatabase.IsValidFolder(animationFolderPath))
            return;

        var paths = AssetDatabase.FindAssets("t:AnimationClip", new[] { animationFolderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
                continue;

            var relativePath = path.StartsWith(animationFolderPath + "/", StringComparison.OrdinalIgnoreCase)
                ? path.Substring(animationFolderPath.Length + 1)
                : path;
            clips.Add(new AnimationClipEntry(path, clip, relativePath));
        }

        clipLabels = clips.Select(clip => clip.DisplayName).ToArray();
        Repaint();
    }

    private void EnsurePreviewInstance()
    {
        if (modelAsset == null)
            return;

        if (previewInstance != null && instantiatedAsset == modelAsset)
            return;

        CleanupPreviewInstance();

        var assetPath = AssetDatabase.GetAssetPath(modelAsset);
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog("动画预览", "Prefab / FBX 必须来自 Project 窗口，不能使用场景中的普通对象。", "确定");
            return;
        }

        previewInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
        if (previewInstance == null)
            previewInstance = Instantiate(modelAsset);
        if (previewInstance == null)
            return;

        previewInstance.name = PreviewObjectPrefix + modelAsset.name;
        previewInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        previewInstance.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        instantiatedAsset = modelAsset;

        var animators = previewInstance.GetComponentsInChildren<Animator>(true);
        previewAnimator = animators.FirstOrDefault();
        foreach (var animator in animators)
            animator.enabled = false;

        Selection.activeGameObject = previewInstance;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    private void SelectClip(int index)
    {
        if (index < 0 || index >= clips.Count)
            return;

        EnsurePreviewInstance();
        selectedClipIndex = index;
        selectedClip = clips[index].Clip;
        previewTime = 0f;
        isPlaying = true;
        lastUpdateTime = EditorApplication.timeSinceStartup;
        SampleSelectedClip();
        Repaint();
    }

    private void UpdatePlayback()
    {
        if (!isPlaying || selectedClip == null || previewInstance == null)
            return;

        var now = EditorApplication.timeSinceStartup;
        var deltaTime = (float)(now - lastUpdateTime) * playbackSpeed;
        lastUpdateTime = now;
        if (deltaTime <= 0f)
            return;

        previewTime += deltaTime;
        if (previewTime >= selectedClip.length)
        {
            if (loop && selectedClip.length > 0f)
                previewTime %= selectedClip.length;
            else
            {
                previewTime = Mathf.Max(0f, selectedClip.length);
                isPlaying = false;
            }
        }

        SampleSelectedClip();
        Repaint();
    }

    private void SampleSelectedClip()
    {
        if (previewInstance == null || selectedClip == null)
            return;

        if (!AnimationMode.InAnimationMode())
            AnimationMode.StartAnimationMode();

        AnimationMode.BeginSampling();
        try
        {
            AnimationMode.SampleAnimationClip(previewInstance, selectedClip, previewTime);
        }
        finally
        {
            AnimationMode.EndSampling();
        }

        SceneView.RepaintAll();
    }

    private void CleanupPreviewInstance()
    {
        isPlaying = false;
        selectedClip = null;
        selectedClipIndex = -1;
        previewAnimator = null;
        instantiatedAsset = null;

        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }

        if (AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode();
    }

    private int[] GetFilteredClipIndices()
    {
        if (string.IsNullOrWhiteSpace(filter))
            return Enumerable.Range(0, clips.Count).ToArray();

        return clips
            .Select((clip, index) => new { clip, index })
            .Where(item => item.clip.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          item.clip.Clip.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(item => item.index)
            .ToArray();
    }

    private static string GetFolderPath(DefaultAsset folder)
    {
        if (folder == null)
            return null;

        var path = AssetDatabase.GetAssetPath(folder);
        return AssetDatabase.IsValidFolder(path) ? path : null;
    }

    private sealed class AnimationClipEntry
    {
        public AnimationClipEntry(string path, AnimationClip clip, string relativePath)
        {
            Path = path;
            Clip = clip;
            DisplayName = relativePath.Replace('\\', '/');
        }

        public string Path { get; }
        public AnimationClip Clip { get; }
        public string DisplayName { get; }
    }
}

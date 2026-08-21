using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SrCharacterPreviewBuilder
{
    private const string MenuPath = "Tools/SR/Character Preview Builder";

    [MenuItem(MenuPath)]
    private static void OpenWindow()
    {
        var window = EditorWindow.GetWindow<SrCharacterPreviewBuilderWindow>();
        window.titleContent = new GUIContent("SR角色预览构建");
        window.minSize = new Vector2(480, 420);
        window.Show();
    }

    // Supports unattended generation without embedding a character-specific path in the tool.
    public static void BuildFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var characterPath = GetArgument(args, "--sr-character-model");
        var weaponPath = TryGetArgument(args, "--sr-weapon-rig");
        var animationPath = GetArgument(args, "--sr-animation");
        var outputRoot = GetArgument(args, "--sr-output-root");
        var previewName = GetArgument(args, "--sr-preview-name");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var character = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
        var weapon = string.IsNullOrEmpty(weaponPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
        var animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationPath);
        if (character == null || animation == null)
            throw new FileNotFoundException("SR preview command inputs were not imported.");

        Build(character, weapon, animation, outputRoot, previewName);
        EditorApplication.Exit(0);
    }

    public static void InspectModelFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var modelPath = GetArgument(args, "--sr-model");
        var reportPath = GetArgument(args, "--sr-report");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
            throw new FileNotFoundException("SR model was not imported: " + modelPath);

        var lines = new List<string> { "Model: " + modelPath };
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var meshRenderer = renderer as SkinnedMeshRenderer;
            var meshFilter = renderer.GetComponent<MeshFilter>();
            var mesh = meshRenderer != null ? meshRenderer.sharedMesh : meshFilter != null ? meshFilter.sharedMesh : null;
            lines.Add(string.Format(
                "Renderer: {0} | Type={1} | Mesh={2} | MeshBounds={3} | LocalBounds={4} | LocalScale={5} | WorldScale={6} | Bones={7} | RootBone={8} | Materials={9}",
                GetTransformPath(renderer.transform, model.transform),
                renderer.GetType().Name,
                mesh != null ? mesh.name : "<none>",
                mesh != null ? FormatBounds(mesh.bounds) : "<none>",
                meshRenderer != null ? FormatBounds(meshRenderer.localBounds) : "<none>",
                renderer.transform.localScale,
                renderer.transform.lossyScale,
                meshRenderer != null && meshRenderer.bones != null ? meshRenderer.bones.Length.ToString() : "0",
                meshRenderer != null && meshRenderer.rootBone != null ? meshRenderer.rootBone.name : "<none>",
                string.Join(",", renderer.sharedMaterials.Where(material => material != null).Select(material => material.name))));
        }
        File.WriteAllLines(reportPath, lines);
        Debug.Log("Wrote SR model inspection: " + reportPath);
        EditorApplication.Exit(0);
    }

    public static void Build(
        GameObject characterAsset,
        GameObject weaponRigAsset,
        AnimationClip animationClip,
        string outputRoot,
        string previewName)
    {
        if (characterAsset == null || animationClip == null)
            throw new ArgumentNullException("Preview inputs cannot be null.");
        if (string.IsNullOrWhiteSpace(outputRoot) || !outputRoot.StartsWith("Assets/", StringComparison.Ordinal))
            throw new ArgumentException("Preview output must be a Unity asset path under Assets/.", nameof(outputRoot));
        if (string.IsNullOrWhiteSpace(previewName))
            throw new ArgumentException("Preview name cannot be empty.", nameof(previewName));

        EnsureFolder(outputRoot);
        EnsureFolder(outputRoot + "/Prefabs");
        EnsureFolder(outputRoot + "/Controllers");
        EnsureFolder(outputRoot + "/Generated");
        EnsureFolder(outputRoot + "/Scenes");

        var prefabPath = outputRoot + "/Prefabs/" + previewName + ".prefab";
        var controllerPath = outputRoot + "/Controllers/" + previewName + ".controller";
        var meshPath = outputRoot + "/Generated/" + previewName + "_WeaponMesh.asset";
        var scenePath = outputRoot + "/Scenes/" + previewName + ".unity";

        var root = Object.Instantiate(characterAsset);
        root.name = previewName;
        RemoveLegacyAnimation(root);

        var characterBones = BuildBoneMap(root);
        var weaponSkin = weaponRigAsset == null ? FindWeaponSkin(root, false) : null;
        var sourceRig = weaponRigAsset != null ? Object.Instantiate(weaponRigAsset) : null;
        if (sourceRig != null)
        {
            sourceRig.name = "__SRWeaponRigSource";
            weaponSkin = FindWeaponSkin(sourceRig, true);
            if (weaponSkin == null || weaponSkin.sharedMesh == null)
                throw new InvalidDataException("The weapon FBX does not contain a SkinnedMeshRenderer with a mesh.");

            var sourceRootBoneName = weaponSkin.rootBone != null ? weaponSkin.rootBone.name : null;
            weaponSkin.transform.SetParent(root.transform, true);
            weaponSkin.gameObject.name = previewName + "_Weapon_Skinned";
            var remappedBones = RemapBones(weaponSkin, characterBones);
            var remappedMesh = Object.Instantiate(weaponSkin.sharedMesh);
            remappedMesh.name = previewName + "_WeaponMesh";
            var bindposes = new Matrix4x4[remappedBones.Length];
            for (var i = 0; i < remappedBones.Length; i++)
            {
                if (remappedBones[i] == null)
                    continue;
                bindposes[i] = remappedBones[i].worldToLocalMatrix * weaponSkin.transform.localToWorldMatrix;
            }
            remappedMesh.bindposes = bindposes;

            DeleteAssetIfExists(meshPath);
            AssetDatabase.CreateAsset(remappedMesh, meshPath);
            AssetDatabase.SaveAssets();
            remappedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            weaponSkin.bones = remappedBones;
            weaponSkin.sharedMesh = remappedMesh;
            if (!string.IsNullOrEmpty(sourceRootBoneName) && characterBones.TryGetValue(sourceRootBoneName, out var rootBone))
                weaponSkin.rootBone = rootBone;
            else
                weaponSkin.rootBone = remappedBones.FirstOrDefault(bone => bone != null);
        }
        else if (weaponSkin != null)
        {
            weaponSkin.gameObject.name = previewName + "_Weapon_Skinned";
        }

        if (sourceRig != null)
            Object.DestroyImmediate(sourceRig);
        AddAnimator(root, controllerPath, animationClip);

        DeleteAssetIfExists(prefabPath);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        if (prefab == null)
            throw new InvalidDataException("Unity did not create the preview prefab.");
        BuildPreviewScene(prefab, scenePath, previewName);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("Built SR character preview: " + prefabPath);
    }

    private static SkinnedMeshRenderer FindWeaponSkin(GameObject sourceRig, bool allowFallback)
    {
        var renderers = sourceRig.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(renderer => renderer.sharedMesh != null && renderer.bones != null && renderer.bones.Length > 0)
            .ToArray();
        return renderers.FirstOrDefault(IsWeaponRenderer) ?? (allowFallback ? renderers.FirstOrDefault() : null);
    }

    private static bool IsWeaponRenderer(SkinnedMeshRenderer renderer)
    {
        var text = renderer.name + "/" + renderer.sharedMesh.name + "/" +
                   string.Join("/", renderer.sharedMaterials.Where(material => material != null).Select(material => material.name));
        return text.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Dictionary<string, Transform> BuildBoneMap(GameObject root)
    {
        var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (var bone in root.GetComponentsInChildren<Transform>(true))
        {
            if (!result.ContainsKey(bone.name))
                result.Add(bone.name, bone);
        }
        return result;
    }

    private static Transform[] RemapBones(SkinnedMeshRenderer sourceSkin, Dictionary<string, Transform> characterBones)
    {
        var remapped = new Transform[sourceSkin.bones.Length];
        for (var i = 0; i < sourceSkin.bones.Length; i++)
        {
            var sourceBone = sourceSkin.bones[i];
            if (sourceBone == null)
                continue;
            if (!characterBones.TryGetValue(sourceBone.name, out remapped[i]))
                throw new InvalidDataException("Weapon skin bone was not found on the character: " + sourceBone.name);
        }
        return remapped;
    }

    private static void AddAnimator(GameObject root, string controllerPath, AnimationClip animationClip)
    {
        DeleteAssetIfExists(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var stateMachine = controller.layers[0].stateMachine;
        var state = stateMachine.AddState(animationClip.name);
        state.motion = animationClip;
        stateMachine.defaultState = state;

        var animator = root.GetComponent<Animator>();
        if (animator == null)
            animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private static void BuildPreviewScene(GameObject prefab, string scenePath, string previewName)
    {
        // Batchmode starts with an untitled scene, which cannot be used as the
        // parent of an additive scene. The generated preview is self-contained.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
        if (instance == null)
            throw new InvalidDataException("Could not instantiate the preview prefab in the scene.");

        var bounds = CalculateBounds(instance);
        var center = bounds.center;
        var radius = Mathf.Max(bounds.extents.magnitude, 1f);

        var cameraObject = new GameObject(previewName + "_Camera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.transform.position = center + new Vector3(0f, radius * 0.15f, radius * 2.8f);
        cameraObject.transform.LookAt(center);
        camera.fieldOfView = 35f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = Mathf.Max(100f, radius * 10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
        camera.tag = "MainCamera";

        var lightObject = new GameObject(previewName + "_KeyLight");
        SceneManager.MoveGameObjectToScene(lightObject, scene);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightObject.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

        EditorSceneManager.SaveScene(scene, scenePath);
        EditorSceneManager.CloseScene(scene, true);
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one);
        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void RemoveLegacyAnimation(GameObject root)
    {
        var legacy = root.GetComponent<Animation>();
        if (legacy != null)
            Object.DestroyImmediate(legacy);
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;
        var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        var name = Path.GetFileName(assetPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new InvalidDataException("Invalid Unity output folder: " + assetPath);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void DeleteAssetIfExists(string assetPath)
    {
        if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            AssetDatabase.DeleteAsset(assetPath);
    }

    private static string GetArgument(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            var candidate = args[i].TrimStart('-');
            var expected = name.TrimStart('-');
            if (string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        throw new ArgumentException("Missing command argument: " + name);
    }

    private static string TryGetArgument(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            var candidate = args[i].TrimStart('-');
            if (string.Equals(candidate, name.TrimStart('-'), StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static string FormatBounds(Bounds bounds)
    {
        return string.Format(
            "center=({0:R},{1:R},{2:R}), extents=({3:R},{4:R},{5:R})",
            bounds.center.x, bounds.center.y, bounds.center.z,
            bounds.extents.x, bounds.extents.y, bounds.extents.z);
    }

    private static string GetTransformPath(Transform transform, Transform root)
    {
        var names = new List<string>();
        for (var current = transform; current != null && current != root; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return names.Count == 0 ? root.name : root.name + "/" + string.Join("/", names);
    }

    private sealed class SrCharacterPreviewBuilderWindow : EditorWindow
    {
        private GameObject characterModel;
        private GameObject weaponRig;
        private AnimationClip animationClip;
        private DefaultAsset outputFolder;
        private string previewName = "CharacterWeaponPreview";

        private void OnGUI()
        {
            EditorGUILayout.LabelField("SR角色武器预览构建", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("输入角色 FBX 和一个示例动画；如果武器是独立 FBX，可以额外指定，工具会按骨骼名称重绑定。", MessageType.Info);
            characterModel = (GameObject)EditorGUILayout.ObjectField("角色 FBX", characterModel, typeof(GameObject), false);
            weaponRig = (GameObject)EditorGUILayout.ObjectField("武器 FBX", weaponRig, typeof(GameObject), false);
            animationClip = (AnimationClip)EditorGUILayout.ObjectField("示例动画", animationClip, typeof(AnimationClip), false);
            outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Preview 文件夹", outputFolder, typeof(DefaultAsset), false);
            previewName = EditorGUILayout.TextField("预览名称", previewName);

            using (new EditorGUI.DisabledScope(characterModel == null || animationClip == null || outputFolder == null))
            {
                if (GUILayout.Button("构建 Prefab、Controller 和 Scene", GUILayout.Height(30)))
                {
                    var outputPath = AssetDatabase.GetAssetPath(outputFolder);
                    Build(characterModel, weaponRig, animationClip, outputPath, previewName);
                }
            }
        }
    }
}

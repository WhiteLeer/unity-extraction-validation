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
        var animationFolderPath = TryGetArgument(args, "--sr-animation-folder");
        var outputRoot = GetArgument(args, "--sr-output-root");
        var previewName = GetArgument(args, "--sr-preview-name");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var character = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
        var weapon = string.IsNullOrEmpty(weaponPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
        var animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationPath);
        if (character == null || animation == null)
            throw new FileNotFoundException("SR preview command inputs were not imported.");

        var animations = string.IsNullOrEmpty(animationFolderPath)
            ? new[] { animation }
            : FindAnimationClips(animationFolderPath, animation);
        Build(character, weapon, animations, outputRoot, previewName);
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

    public static void ValidatePreviewFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var prefabPath = GetArgument(args, "--sr-preview-prefab");
        var animationPath = GetArgument(args, "--sr-validation-animation");
        var reportPath = GetArgument(args, "--sr-validation-report");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationPath);
        if (prefab == null || clip == null)
            throw new FileNotFoundException("SR preview validation inputs were not imported.");

        var instance = Object.Instantiate(prefab);
        var animator = instance.GetComponentInChildren<Animator>(true);
        var weapon = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(IsWeaponRenderer);
        var curvePaths = AnimationUtility.GetCurveBindings(clip)
            .Where(binding => binding.type == typeof(Transform))
            .Select(binding => binding.path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var targets = curvePaths
            .Select(path => new
            {
                Path = path,
                Transform = string.IsNullOrEmpty(path) ? instance.transform : instance.transform.Find(path)
            })
            .Where(item => item.Transform != null)
            .ToArray();
        var lines = new List<string>
        {
            "Prefab: " + prefabPath,
            "Clip: " + clip.name + " length=" + clip.length.ToString("R"),
            "Animator: " + (animator == null ? "<none>" : "present"),
            "Avatar: " + (animator == null || animator.avatar == null ? "<none>" : animator.avatar.name + " valid=" + animator.avatar.isValid),
            "TransformCurvePaths: " + curvePaths.Length,
            "ResolvedTransformCurvePaths: " + targets.Length,
            "WeaponRenderer: " + (weapon == null ? "<none>" : weapon.name + " mesh=" + (weapon.sharedMesh == null ? "<none>" : weapon.sharedMesh.name) + " bounds=" + FormatBounds(weapon.localBounds))
        };

        if (animator != null && targets.Length > 0)
        {
            var animatorStart = targets.ToDictionary(item => item.Path, item => item.Transform.localRotation, StringComparer.Ordinal);
            animator.Rebind();
            animator.Play(clip.name, 0, 0f);
            animator.Update(0.001f);
            animator.Play(clip.name, 0, 0.5f);
            animator.Update(0.001f);
            var animatorChanged = targets
                .Where(item => Quaternion.Angle(animatorStart[item.Path], item.Transform.localRotation) > 0.001f)
                .Select(item => item.Path)
                .ToArray();
            lines.Add("AnimatorChangedPaths: " + animatorChanged.Length);
            lines.Add("AnimatorChangedBodyPaths: " + animatorChanged.Count(path => !path.Contains("skirt", StringComparison.OrdinalIgnoreCase)));
            lines.Add("AnimatorChangedSkirtPaths: " + animatorChanged.Count(path => path.Contains("skirt", StringComparison.OrdinalIgnoreCase)));

            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(instance, clip, clip.length * 0.5f);
            AnimationMode.EndSampling();
            AnimationMode.StopAnimationMode();
            var sampledChanged = targets
                .Where(item => Quaternion.Angle(animatorStart[item.Path], item.Transform.localRotation) > 0.001f)
                .Select(item => item.Path)
                .ToArray();
            lines.Add("DirectSampleChangedPaths: " + sampledChanged.Length);
            lines.Add("DirectSampleChangedBodyPaths: " + sampledChanged.Count(path => !path.Contains("skirt", StringComparison.OrdinalIgnoreCase)));
            lines.Add("DirectSampleChangedSkirtPaths: " + sampledChanged.Count(path => path.Contains("skirt", StringComparison.OrdinalIgnoreCase)));
        }

        File.WriteAllLines(reportPath, lines);
        Object.DestroyImmediate(instance);
        EditorApplication.Exit(0);
    }

    public static void Build(
        GameObject characterAsset,
        GameObject weaponRigAsset,
        AnimationClip animationClip,
        string outputRoot,
        string previewName)
    {
        Build(characterAsset, weaponRigAsset, new[] { animationClip }, outputRoot, previewName);
    }

    public static void Build(
        GameObject characterAsset,
        GameObject weaponRigAsset,
        IReadOnlyList<AnimationClip> animationClips,
        string outputRoot,
        string previewName)
    {
        if (characterAsset == null || animationClips == null || animationClips.Count == 0 ||
            animationClips.Any(clip => clip == null))
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
        var avatarPath = outputRoot + "/Generated/" + previewName + "_GenericAvatar.asset";
        var scenePath = outputRoot + "/Scenes/" + previewName + ".unity";

        var root = Object.Instantiate(characterAsset);
        root.name = previewName;
        RemoveLegacyAnimation(root);

        var characterBones = BuildBoneMap(root);
        var characterBonesByPath = BuildBonePathMap(root);
        var weaponSkin = weaponRigAsset == null ? FindWeaponSkin(root, false) : null;
        var sourceRig = weaponRigAsset != null ? Object.Instantiate(weaponRigAsset) : null;
        if (sourceRig != null)
        {
            sourceRig.name = "__SRWeaponRigSource";
            weaponSkin = FindWeaponSkin(sourceRig, true);
            if (weaponSkin == null || weaponSkin.sharedMesh == null)
                throw new InvalidDataException("The weapon FBX does not contain a SkinnedMeshRenderer with a mesh.");

            var sourceLocalPosition = weaponSkin.transform.localPosition;
            var sourceLocalRotation = weaponSkin.transform.localRotation;
            var sourceLocalScale = weaponSkin.transform.localScale;
            // Keep the renderer in the source prefab's coordinate space. The source
            // mesh bindposes already describe the relationship to its weapon bones;
            // parenting the renderer to the mount and rebuilding bindposes loses that
            // coordinate space and makes the weapon drift during animation.
            weaponSkin.transform.SetParent(root.transform, false);
            weaponSkin.transform.localPosition = sourceLocalPosition;
            weaponSkin.transform.localRotation = sourceLocalRotation;
            weaponSkin.transform.localScale = sourceLocalScale;
            weaponSkin.gameObject.name = previewName + "_Weapon_Skinned";
            var remappedBones = RemapBones(
                weaponSkin,
                sourceRig.transform,
                characterBones,
                characterBonesByPath);
            var remappedMesh = Object.Instantiate(weaponSkin.sharedMesh);
            remappedMesh.name = previewName + "_WeaponMesh";

            DeleteAssetIfExists(meshPath);
            AssetDatabase.CreateAsset(remappedMesh, meshPath);
            AssetDatabase.SaveAssets();
            remappedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            weaponSkin.bones = remappedBones;
            weaponSkin.sharedMesh = remappedMesh;
            // The source renderer can carry a tiny runtime-calculated bounds box.
            // Keep the weapon from being culled after it is moved into the preview.
            weaponSkin.localBounds = remappedMesh.bounds;
            weaponSkin.updateWhenOffscreen = true;
            var sourceRootBone = weaponSkin.rootBone;
            var rootBone = FindMatchingBone(
                sourceRootBone,
                sourceRig.transform,
                characterBones,
                characterBonesByPath);
            if (rootBone != null)
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
        AddAnimator(root, characterAsset, controllerPath, animationClips, avatarPath);
        ValidateAnimationBindings(root, animationClips);

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
        return root.GetComponentsInChildren<Transform>(true)
            .GroupBy(bone => bone.name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
    }

    private static Dictionary<string, Transform> BuildBonePathMap(GameObject root)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .ToDictionary(
                bone => GetRelativeTransformPath(bone, root.transform),
                bone => bone,
                StringComparer.Ordinal);
    }

    private static Transform[] RemapBones(
        SkinnedMeshRenderer sourceSkin,
        Transform sourceRoot,
        Dictionary<string, Transform> characterBones,
        Dictionary<string, Transform> characterBonesByPath)
    {
        var remapped = new Transform[sourceSkin.bones.Length];
        for (var i = 0; i < sourceSkin.bones.Length; i++)
        {
            var sourceBone = sourceSkin.bones[i];
            if (sourceBone == null)
                continue;
            remapped[i] = FindMatchingBone(
                sourceBone,
                sourceRoot,
                characterBones,
                characterBonesByPath);
            if (remapped[i] == null)
                throw new InvalidDataException("Weapon skin bone was not found on the character: " + sourceBone.name);
        }
        return remapped;
    }

    private static Transform FindMatchingBone(
        Transform sourceBone,
        Transform sourceRoot,
        Dictionary<string, Transform> characterBones,
        Dictionary<string, Transform> characterBonesByPath)
    {
        if (sourceBone == null)
            return null;

        var sourcePath = GetRelativeTransformPath(sourceBone, sourceRoot);
        if (characterBonesByPath.TryGetValue(sourcePath, out var pathMatch))
            return pathMatch;

        return characterBones.TryGetValue(sourceBone.name, out var nameMatch) ? nameMatch : null;
    }

    private static void AddAnimator(
        GameObject root,
        GameObject sourceAsset,
        string controllerPath,
        IReadOnlyList<AnimationClip> animationClips,
        string avatarPath)
    {
        DeleteAssetIfExists(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var stateMachine = controller.layers[0].stateMachine;
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < animationClips.Count; i++)
        {
            var clip = animationClips[i];
            var stateName = clip.name;
            if (!usedNames.Add(stateName))
                stateName = stateName + "_" + i;

            var state = stateMachine.AddState(stateName);
            state.motion = clip;
            if (i == 0)
                stateMachine.defaultState = state;
        }

        var animator = root.GetComponent<Animator>();
        if (animator == null)
            animator = root.AddComponent<Animator>();
        var sourceAnimator = sourceAsset.GetComponentsInChildren<Animator>(true).FirstOrDefault();
        var avatar = sourceAnimator != null ? sourceAnimator.avatar : null;
        if (avatar == null)
        {
            var sourcePath = AssetDatabase.GetAssetPath(sourceAsset);
            avatar = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Avatar>().FirstOrDefault();
        }
        if (avatar == null)
        {
            avatar = BuildGenericAvatar(root, avatarPath);
        }
        if (avatar != null)
            animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private static Avatar BuildGenericAvatar(GameObject root, string avatarPath)
    {
        var rootMotion = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => string.Equals(transform.name, "Root_M", StringComparison.Ordinal));
        var avatar = AvatarBuilder.BuildGenericAvatar(root, rootMotion != null ? rootMotion.name : string.Empty);
        if (avatar == null || !avatar.isValid)
        {
            if (avatar != null)
                Object.DestroyImmediate(avatar);
            Debug.LogWarning("SR could not build a valid Generic Avatar for " + root.name, root);
            return null;
        }

        DeleteAssetIfExists(avatarPath);
        avatar.name = Path.GetFileNameWithoutExtension(avatarPath);
        AssetDatabase.CreateAsset(avatar, avatarPath);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
    }

    private static void ValidateAnimationBindings(GameObject root, IReadOnlyList<AnimationClip> animationClips)
    {
        var transformPaths = new HashSet<string>(
            root.GetComponentsInChildren<Transform>(true)
                .Select(transform => GetRelativeTransformPath(transform, root.transform)),
            StringComparer.Ordinal);

        foreach (var clip in animationClips)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip)
                .Where(binding => binding.type == typeof(Transform))
                .Select(binding => binding.path)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var missing = bindings.Where(path => !transformPaths.Contains(path)).ToArray();
            if (missing.Length == 0)
                continue;
            Debug.LogWarning(
                string.Format(
                    "SR animation binding mismatch: {0}, missing {1}/{2} transform paths. First missing: {3}",
                    clip.name, missing.Length, bindings.Length, string.Join(", ", missing.Take(8))),
                root);
        }
    }

    private static AnimationClip[] FindAnimationClips(string folderPath, AnimationClip fallback)
    {
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            return new[] { fallback };

        var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => AssetDatabase.LoadAssetAtPath<AnimationClip>(path))
            .Where(clip => clip != null)
            .OrderBy(clip => clip.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        clips.Remove(fallback);
        clips.Insert(0, fallback);
        return clips.ToArray();
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

    private static string GetRelativeTransformPath(Transform transform, Transform root)
    {
        var names = new List<string>();
        for (var current = transform; current != null && current != root; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
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

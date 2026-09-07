using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SrRuntimeEffectTools;

namespace SrEffectPrefabTools
{
    public static class SrCerydraRuntimeEffectPreviewBuilder
    {
        private const string CharacterPrefabPath =
            "Assets/unity-extraction-validation/SR/characters/cerydra/Preview/Prefabs/Cerydra_00_CharacterPreview.prefab";
        private const string EffectPrefabPath =
            "Assets/unity-extraction-validation/SR/effects/cerydra/Eff_Avatar_Cerydra_00_MazeSkill_01/Eff_Avatar_Cerydra_00_MazeSkill_01.prefab";
        private const string ScenePath =
            "Assets/unity-extraction-validation/SR/effects/cerydra/Preview/Eff_Avatar_Cerydra_00_MazeSkill_01_RuntimePreview.unity";

        [MenuItem("Tools/SR/Build Cerydra Runtime Effect Preview")]
        public static void BuildCerydraRuntimePreview()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            var effectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPrefabPath);
            if (characterPrefab == null || effectPrefab == null)
                throw new MissingReferenceException($"Missing Cerydra preview assets: {CharacterPrefabPath} or {EffectPrefabPath}");

            EnsureAssetFolder("Assets/unity-extraction-validation/SR/effects/cerydra/Preview");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var character = PrefabUtility.InstantiatePrefab(characterPrefab, scene) as GameObject;
            var effect = PrefabUtility.InstantiatePrefab(effectPrefab, scene) as GameObject;
            character.name = "CerydraCharacter_Runtime";
            effect.name = "Eff_Avatar_Cerydra_00_MazeSkill_01_Runtime";

            var previewRoot = new GameObject("SR_RuntimeEffectPreview");
            SceneManager.MoveGameObjectToScene(previewRoot, scene);
            var controller = previewRoot.AddComponent<SrEffectRuntimePreviewController>();
            SetPrivateField(controller, "characterRoot", character.transform);
            SetPrivateField(controller, "effectRoot", effect.transform);

            CreateCamera(scene, CalculateBounds(character, effect));
            CreateLight(scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = previewRoot;
            Debug.Log($"Built Cerydra runtime effect preview scene: {ScenePath}");
        }

        private static void EnsureAssetFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            var parent = System.IO.Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            var name = System.IO.Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"Invalid Unity asset folder path: {assetFolder}");

            EnsureAssetFolder(parent);
            if (!AssetDatabase.IsValidFolder(assetFolder))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static Bounds CalculateBounds(params GameObject[] roots)
        {
            var renderers = roots
                .Where(root => root != null)
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .ToArray();
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static void CreateCamera(Scene scene, Bounds bounds)
        {
            var cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 45f;
            camera.aspect = 1f;
            var halfFovRadians = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            var radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            var distance = Mathf.Max(4f, radius / Mathf.Tan(halfFovRadians) * 1.15f);
            var target = bounds.center;
            camera.transform.position = target + new Vector3(0f, 0f, -distance);
            camera.transform.LookAt(target);
            camera.nearClipPlane = Mathf.Max(0.01f, distance * 0.001f);
            camera.farClipPlane = Mathf.Max(1000f, distance * 4f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.06f, 0.075f, 1f);
        }

        private static void CreateLight(Scene scene)
        {
            var lightObject = new GameObject("Key Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
        }

        private static void SetPrivateField(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }
    }
}

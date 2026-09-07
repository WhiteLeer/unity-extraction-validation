using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SrRuntimeEffectTools;

namespace SrEffectPrefabTools
{
    public static class SrRuntimeEffectPreviewBuilder
    {
        private const string CharacterPrefabPath =
            "Assets/unity-extraction-validation/SR/characters/herta/Preview/Herta_SplitWeaponPreview.prefab";
        private const string EffectPrefabPath =
            "Assets/unity-extraction-validation/SR/effects/herta/Eff_Avatar_Herta_00_Skill03_Start02/Eff_Avatar_Herta_00_Skill03_Start02.prefab";
        private const string SourceManifestPath =
            "D:/Unpack_Workspace/Games/SR_4.4/Validation/herta_complete_prefab_stage_20260813/Eff_Avatar_Herta_00_Skill03_Start02.srprefab";
        private const string ScenePath =
            "Assets/unity-extraction-validation/SR/effects/herta/Preview/Eff_Avatar_Herta_00_Skill03_Start02_RuntimePreview.unity";
        private const string RdcTextureSlotsShaderPath =
            "Assets/unity-extraction-validation/SR/Shader/SR_RdcTextureSlots.shader";
        private const string HertaGlowTexturePath =
            "Assets/unity-extraction-validation/SR/effects/herta/Eff_Avatar_Herta_00_Skill03_Start02/Textures/CAB-4d31a2ca93825616128ab0614d37dd51_-6695804635681089760_Eff_Glow_40.png";
        private const string HertaGlowMaterialPath =
            "Assets/unity-extraction-validation/SR/effects/herta/Eff_Avatar_Herta_00_Skill03_Start02/Materials/Eff_Glow_40_Rdc_Action436.mat";
        private const string HertaStart02PrefabPath =
            "Assets/unity-extraction-validation/SR/effects/herta/Eff_Avatar_Herta_00_Skill03_Start02/Eff_Avatar_Herta_00_Skill03_Start02.prefab";

        [MenuItem("Tools/SR/Build Herta Runtime Effect Preview")]
        public static void BuildHertaRuntimePreview()
        {
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            var effectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPrefabPath);
            if (characterPrefab == null || effectPrefab == null)
                throw new MissingReferenceException($"Missing preview assets: {CharacterPrefabPath} or {EffectPrefabPath}");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var character = PrefabUtility.InstantiatePrefab(characterPrefab, scene) as GameObject;
            var effect = PrefabUtility.InstantiatePrefab(effectPrefab, scene) as GameObject;
            character.name = "HertaCharacter_Runtime";
            effect.name = "Eff_Avatar_Herta_00_Skill03_Start02_Runtime";

            var previewRoot = new GameObject("SR_RuntimeEffectPreview");
            SceneManager.MoveGameObjectToScene(previewRoot, scene);
            var controller = previewRoot.AddComponent<SrEffectRuntimePreviewController>();
            SetPrivateField(controller, "characterRoot", character.transform);
            SetPrivateField(controller, "effectRoot", effect.transform);

            CreateCamera(scene, character.transform);
            CreateLight(scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = previewRoot;
            Debug.Log($"Built SR runtime effect preview scene: {ScenePath}");
        }

        [MenuItem("Tools/SR/Reimport And Build Herta Runtime Effect Preview")]
        public static void ReimportAndBuildHertaRuntimePreview()
        {
            SrEffectPrefabImporter.Import(SourceManifestPath, EffectPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            BuildHertaRuntimePreview();
        }

        [MenuItem("Tools/SR/Apply RDC Action 436 Material To Herta Start02 Prefab")]
        public static void ApplyRdcAction436MaterialToHertaStart02Prefab()
        {
            AssetDatabase.Refresh();

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(RdcTextureSlotsShaderPath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(HertaGlowTexturePath);
            if (shader == null)
                throw new MissingReferenceException($"Missing RDC shader: {RdcTextureSlotsShaderPath}");
            if (texture == null)
                throw new MissingReferenceException($"Missing Herta Glow texture: {HertaGlowTexturePath}");

            EnsureAssetFolder("Assets/unity-extraction-validation/SR/effects/herta/Eff_Avatar_Herta_00_Skill03_Start02/Materials");
            var material = AssetDatabase.LoadAssetAtPath<Material>(HertaGlowMaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "Eff_Glow_40_Rdc_Action436"
                };
                AssetDatabase.CreateAsset(material, HertaGlowMaterialPath);
            }
            else
                material.shader = shader;

            ConfigureHertaGlowRdcMaterial(material, texture);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            var prefabContents = PrefabUtility.LoadPrefabContents(HertaStart02PrefabPath);
            try
            {
                var renderers = prefabContents.GetComponentsInChildren<ParticleSystemRenderer>(true);
                var targetCount = 0;
                foreach (var renderer in renderers)
                {
                    if (renderer.gameObject.name != "SubEmitter01")
                        continue;

                    renderer.sharedMaterials = new[] { material };
                    renderer.enabled = true;
                    EditorUtility.SetDirty(renderer);
                    targetCount++;
                }

                if (targetCount == 0)
                    throw new MissingReferenceException("Could not find ParticleSystemRenderer on 'SubEmitter01'.");

                PrefabUtility.SaveAsPrefabAsset(prefabContents, HertaStart02PrefabPath);
                Debug.Log($"Applied RDC Action 436 material to {targetCount} Herta Start02 renderer(s): {HertaGlowMaterialPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(HertaStart02PrefabPath);
        }

        private static void ConfigureHertaGlowRdcMaterial(Material material, Texture2D texture)
        {
            material.SetTexture("_MainTex0", texture);
            material.SetColor("_VertexColorTint", new Color(0.7490196f, 0.7490196f, 0.7490196f, 0.1019608f));
            material.SetVector("_UVScaleOffset", new Vector4(1f, 1f, 0f, 0f));
            material.SetFloat("_SlotAdd0", 2.1757169f);
            material.SetFloat("_SlotAdd1", -0.2925967f);
            material.SetFloat("_SlotAdd2", 0.1878809f);
            material.SetFloat("_SlotAdd3", 1f);
            material.SetFloat("_SlotAdd4", 2.1772728f);
            material.SetFloat("_SlotAdd5", -0.2925967f);
            material.SetFloat("_SlotAdd6", 0.1878809f);
            material.SetFloat("_SlotAdd7", 0f);
            material.SetFloat("_SrcBlend", 5f);
            material.SetFloat("_DstBlend", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
        }

        private static void EnsureAssetFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            var parent = System.IO.Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            var name = System.IO.Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new System.InvalidOperationException($"Invalid Unity asset folder path: {assetFolder}");

            EnsureAssetFolder(parent);
            if (!AssetDatabase.IsValidFolder(assetFolder))
                AssetDatabase.CreateFolder(parent, name);
        }

        public static void ValidateHertaRuntimePreviewLoop()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
            var start = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckLoop;

            void CheckLoop()
            {
                if (EditorApplication.timeSinceStartup - start < 7.5)
                    return;

                var proxies = Object.FindObjectsOfType<SrEffectRuntimeProxy>(true);
                foreach (var proxy in proxies)
                    Debug.Log($"SR runtime loop validation: {proxy.name} cycles={proxy.PlaybackCycleCount}, attach='{proxy.AttachPoint}'.");
                EditorApplication.update -= CheckLoop;
                EditorApplication.isPlaying = false;
                EditorApplication.Exit(0);
            }
        }

        private static void CreateCamera(Scene scene, Transform target)
        {
            var cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 1.25f, -4.5f);
            camera.transform.LookAt(target.position + Vector3.up * 1.05f);
            camera.fieldOfView = 35f;
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

        private static void SetPrivateField(Object target, string fieldName, Object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }
    }
}

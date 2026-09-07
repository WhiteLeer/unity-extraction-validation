using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace SrEffectPrefabTools
{
    /// <summary>
    /// Compares an extracted SR effect manifest with the generated Unity prefab.
    /// This is intentionally separate from the importer: a regression report must
    /// not change the asset it is checking.
    /// </summary>
    public static class SrEffectPrefabAudit
    {
        private static readonly string[] ModuleNames =
        {
            "InitialModule", "ShapeModule", "EmissionModule", "SizeModule",
            "RotationModule", "ColorModule", "UVModule", "VelocityModule",
            "InheritVelocityModule", "ForceModule", "ExternalForcesModule",
            "ClampVelocityModule", "NoiseModule", "SizeBySpeedModule",
            "RotationBySpeedModule", "ColorBySpeedModule", "CollisionModule",
            "TriggerModule", "SubModule", "LightsModule", "TrailModule",
            "CustomDataModule"
        };

        private const string CerydraManifestPath =
            @"C:\Users\wepie\AppData\Local\Temp\cerydra_manifest_inspect_dea976a5b1264633b41c70735e860f1c\manifest.json";
        private const string CerydraPrefabPath =
            "Assets/unity-extraction-validation/SR/effects/cerydra/Eff_Avatar_Cerydra_00_MazeSkill_01/Eff_Avatar_Cerydra_00_MazeSkill_01.prefab";

        [MenuItem("Tools/SR/Audit Effect Prefab From Manifest...")]
        public static void AuditFromDialog()
        {
            var manifestPath = EditorUtility.OpenFilePanel(
                "Select SR effect manifest", string.Empty, "json");
            if (string.IsNullOrEmpty(manifestPath))
                return;

            var prefabPath = EditorUtility.OpenFilePanel(
                "Select generated SR prefab", Application.dataPath, "prefab");
            if (string.IsNullOrEmpty(prefabPath))
                return;

            var reportPath = Path.Combine(
                Application.dataPath, "../Temp/SR_EffectAudits",
                Path.GetFileNameWithoutExtension(prefabPath) + ".json");
            var report = Run(manifestPath, ToAssetPath(prefabPath));
            WriteReport(report, reportPath);
            LogResult(report, reportPath);
        }

        [MenuItem("Tools/SR/Audit Cerydra Prefab")]
        public static void AuditCerydraPrefab()
        {
            var reportPath = Path.Combine(
                Application.dataPath, "unity-extraction-validation/SR/effects/cerydra/Diagnostics",
                "Eff_Avatar_Cerydra_00_MazeSkill_01.audit.json");
            var report = Run(CerydraManifestPath, CerydraPrefabPath);
            WriteReport(report, reportPath);
            LogResult(report, reportPath);
        }

        public static void RunFromCommandLine()
        {
            try
            {
                var arguments = Environment.GetCommandLineArgs();
                var manifestPath = ReadArgument(arguments, "-srManifest");
                var prefabPath = ReadArgument(arguments, "-srPrefab");
                var reportPath = ReadOptionalArgument(arguments, "-srAuditOutput");
                if (string.IsNullOrEmpty(reportPath))
                {
                    reportPath = Path.Combine(
                        Application.dataPath, "../Temp/SR_EffectAudits",
                        Path.GetFileNameWithoutExtension(prefabPath) + ".json");
                }

                var report = Run(manifestPath, prefabPath);
                WriteReport(report, reportPath);
                LogResult(report, reportPath);
                if (Application.isBatchMode)
                    EditorApplication.Exit(report.Passed ? 0 : 2);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(3);
            }
        }

        public static AuditReport Run(string manifestPath, string prefabPath)
        {
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("SR manifest was not found.", manifestPath);

            var absolutePrefabPath = prefabPath;
            if (!Path.IsPathRooted(absolutePrefabPath))
                absolutePrefabPath = Path.GetFullPath(Path.Combine(".", prefabPath));
            var assetPrefabPath = absolutePrefabPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase)
                ? ToAssetPath(absolutePrefabPath)
                : prefabPath.Replace('\\', '/');
            if (!File.Exists(absolutePrefabPath) && !File.Exists(ToAbsolutePath(assetPrefabPath)))
                throw new FileNotFoundException("SR prefab was not found.", prefabPath);

            var manifest = JsonConvert.DeserializeObject<SourceManifest>(
                File.ReadAllText(manifestPath));
            if (manifest == null)
                throw new InvalidDataException("SR manifest is empty or invalid.");

            var report = new AuditReport
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                ManifestPath = manifestPath,
                PrefabPath = assetPrefabPath,
                SourceNodeCount = manifest.Nodes?.Length ?? 0
            };

            var sourceDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            var sourceParticles = new List<SourceParticle>();
            var sourceRenderers = new List<SourceRenderer>();
            var sourceLightCount = 0;
            foreach (var node in manifest.Nodes ?? Array.Empty<SourceNode>())
            {
                foreach (var component in node.Components ?? Array.Empty<SourceComponent>())
                {
                    if (component.Type == "ParticleSystem")
                    {
                        var parameters = ReadParameters(sourceDirectory, component.ParametersFile);
                        sourceParticles.Add(new SourceParticle(node, component, parameters.Json, parameters.Available));
                        if (!parameters.Available)
                        {
                            report.SourceParameterMissingCount++;
                            AddIssue(report, "source-parameters", node.Path,
                                string.IsNullOrEmpty(component.ParametersFile)
                                    ? "ParticleSystem has no ParametersFile"
                                    : $"cannot read {component.ParametersFile}");
                        }
                    }
                    else if (component.Type == "ParticleSystemRenderer")
                    {
                        var parameters = ReadParameters(sourceDirectory, component.ParametersFile);
                        sourceRenderers.Add(new SourceRenderer(
                            node, component, parameters.Json, parameters.Available));
                        if (!parameters.Available)
                        {
                            report.SourceParameterMissingCount++;
                            AddIssue(report, "source-parameters", node.Path,
                                string.IsNullOrEmpty(component.ParametersFile)
                                    ? "ParticleSystemRenderer has no ParametersFile"
                                    : $"cannot read {component.ParametersFile}");
                        }
                    }
                    else if (component.Type == "Light")
                    {
                        sourceLightCount++;
                    }
                }
            }

            report.SourceParticleSystemCount = sourceParticles.Count;
            report.SourceRendererCount = sourceRenderers.Count;
            report.SourceLightCount = sourceLightCount;

            var prefabRoot = PrefabUtility.LoadPrefabContents(assetPrefabPath);
            try
            {
                report.PrefabNodeCount = prefabRoot.GetComponentsInChildren<Transform>(true).Length;
                var prefabParticles = prefabRoot.GetComponentsInChildren<ParticleSystem>(true);
                var prefabRenderers = prefabRoot.GetComponentsInChildren<ParticleSystemRenderer>(true);
                report.PrefabParticleSystemCount = prefabParticles.Length;
                report.PrefabRendererCount = prefabRenderers.Length;
                report.PrefabLightCount = prefabRoot.GetComponentsInChildren<Light>(true).Length;

                CompareCount(report, "nodes", report.SourceNodeCount, report.PrefabNodeCount);
                CompareCount(report, "particleSystems", report.SourceParticleSystemCount, report.PrefabParticleSystemCount);
                CompareCount(report, "particleSystemRenderers", report.SourceRendererCount, report.PrefabRendererCount);
                CompareCount(report, "lights", report.SourceLightCount, report.PrefabLightCount);

                foreach (var sourceParticle in sourceParticles)
                    AuditParticle(report, prefabRoot.transform, sourceParticle);
                foreach (var sourceRenderer in sourceRenderers)
                    AuditRenderer(report, prefabRoot.transform, sourceRenderer);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            report.Passed = report.Issues.Count == 0;
            return report;
        }

        private static void AuditParticle(
            AuditReport report,
            Transform prefabRoot,
            SourceParticle sourceParticle)
        {
            if (!sourceParticle.ParametersAvailable)
                return;

            var targetTransform = FindTransform(prefabRoot, sourceParticle.Node.Path);
            if (targetTransform == null)
            {
                AddIssue(report, "missing-node", sourceParticle.Node.Path,
                    "source node exists but no matching prefab transform was found");
                return;
            }

            var target = targetTransform.GetComponent<ParticleSystem>();
            if (target == null)
            {
                AddIssue(report, "missing-component", sourceParticle.Node.Path,
                    "source ParticleSystem has no matching prefab component");
                return;
            }

            var sourceJson = sourceParticle.Parameters;
            var serialized = new SerializedObject(target);
            foreach (var moduleName in ModuleNames)
            {
                var sourceModule = sourceJson[moduleName] as JObject;
                if (sourceModule == null)
                    continue;

                var sourceEnabled = sourceModule.Value<bool?>("enabled") ?? false;
                var sourceCurves = CountNonEmptyCurveArrays(sourceModule);
                var targetModule = serialized.FindProperty(moduleName);
                var targetEnabled = false;
                var targetCurves = 0;
                var targetAlphaKeys = new List<int>();
                if (targetModule != null)
                {
                    targetEnabled = targetModule.FindPropertyRelative("enabled")?.boolValue ?? false;
                    targetCurves = CountSerializedCurveArrays(targetModule);
                    CollectSerializedArraySizes(targetModule, "m_Alpha", targetAlphaKeys);
                }

                var metric = GetModuleMetric(report, moduleName);
                if (sourceEnabled)
                    metric.SourceEnabled++;
                if (targetEnabled)
                    metric.PrefabEnabled++;
                metric.SourceCurveArrays += sourceCurves;
                metric.PrefabCurveArrays += targetCurves;
                metric.SourceAlphaKeyCounts.AddRange(
                    GetArraySizes(sourceModule, "m_Alpha"));
                metric.PrefabAlphaKeyCounts.AddRange(targetAlphaKeys);

                if (moduleName == "ColorModule" &&
                    !HistogramEquals(metric.SourceAlphaKeyCounts, metric.PrefabAlphaKeyCounts))
                {
                    AddIssue(report, "gradient-alpha-keys", sourceParticle.Node.Path,
                        $"source=[{FormatHistogram(metric.SourceAlphaKeyCounts)}], " +
                        $"prefab=[{FormatHistogram(metric.PrefabAlphaKeyCounts)}]");
                }

                if (sourceEnabled != targetEnabled)
                {
                    AddIssue(report, "module-enabled", sourceParticle.Node.Path + "/" + moduleName,
                        $"source={sourceEnabled}, prefab={targetEnabled}");
                }
                if (sourceCurves != targetCurves)
                {
                    AddIssue(report, "curve-arrays", sourceParticle.Node.Path + "/" + moduleName,
                        $"source={sourceCurves}, prefab={targetCurves}");
                }
            }

            AuditParticleModuleOptions(report, sourceParticle.Node.Path, sourceJson, target);
        }

        private static void AuditParticleModuleOptions(
            AuditReport report,
            string path,
            JObject source,
            ParticleSystem target)
        {
            if (source["ClampVelocityModule"] is JObject clampSource)
            {
                var clamp = target.limitVelocityOverLifetime;
                var sourceWorld = clampSource.Value<bool?>("inWorldSpace") ?? false;
                if ((clamp.space == ParticleSystemSimulationSpace.World) != sourceWorld)
                    AddIssue(report, "module-option", path + "/ClampVelocityModule/space",
                        $"sourceWorld={sourceWorld}, prefab={clamp.space}");

                CompareBoolOption(report, path + "/ClampVelocityModule/multiplyDragByParticleSize",
                    clampSource, "multiplyDragByParticleSize", clamp.multiplyDragByParticleSize);
                CompareBoolOption(report, path + "/ClampVelocityModule/multiplyDragByParticleVelocity",
                    clampSource, "multiplyDragByParticleVelocity", clamp.multiplyDragByParticleVelocity);
            }

            if (source["UVModule"] is JObject uvSource && uvSource["speedRange"] is JObject speedRange)
            {
                var expected = new Vector2(
                    speedRange.Value<float?>("x") ?? speedRange.Value<float?>("X") ?? 0f,
                    speedRange.Value<float?>("y") ?? speedRange.Value<float?>("Y") ?? 0f);
                var actual = target.textureSheetAnimation.speedRange;
                if ((expected - actual).sqrMagnitude > 0.0000001f)
                    AddIssue(report, "module-option", path + "/UVModule/speedRange",
                        $"source={expected}, prefab={actual}");
            }

            if (source["ShapeModule"] is JObject shapeSource)
            {
                var shape = target.shape;
                var expectedScale = ReadVector3(shapeSource["m_Scale"], shape.scale);
                if ((expectedScale - shape.scale).sqrMagnitude > 0.0000001f)
                    AddIssue(report, "module-option", path + "/ShapeModule/m_Scale",
                        $"source={expectedScale}, prefab={shape.scale}");

                var isMeshShape = shape.shapeType == ParticleSystemShapeType.Mesh ||
                                  shape.shapeType == ParticleSystemShapeType.MeshRenderer ||
                                  shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer;
                if (shapeSource.Value<bool?>("enabled") == true && isMeshShape)
                {
                    var expectedMeshShapeType = Mathf.Clamp(
                        shapeSource.Value<int?>("placementMode") ?? (int)shape.meshShapeType, 0, 2);
                    if ((int)shape.meshShapeType != expectedMeshShapeType)
                        AddIssue(report, "module-option", path + "/ShapeModule/placementMode",
                            $"source={expectedMeshShapeType}, prefab={(int)shape.meshShapeType}");
                }
            }

            if (source["InitialModule"] is JObject initialSource &&
                initialSource.Value<bool?>("rotation3D") == true)
            {
                var initial = target.main;
                if (!initial.startRotation3D)
                    AddIssue(report, "module-option", path + "/InitialModule/rotation3D",
                        "source=true, prefab=false");
            }
        }

        private static void CompareBoolOption(
            AuditReport report,
            string path,
            JObject source,
            string name,
            bool actual)
        {
            var expected = source.Value<bool?>(name);
            if (expected.HasValue && expected.Value != actual)
                AddIssue(report, "module-option", path,
                    $"source={expected.Value}, prefab={actual}");
        }

        private static Vector3 ReadVector3(JToken token, Vector3 fallback)
        {
            if (!(token is JObject value))
                return fallback;
            return new Vector3(
                value.Value<float?>("x") ?? value.Value<float?>("X") ?? fallback.x,
                value.Value<float?>("y") ?? value.Value<float?>("Y") ?? fallback.y,
                value.Value<float?>("z") ?? value.Value<float?>("Z") ?? fallback.z);
        }

        private static void AuditRenderer(
            AuditReport report,
            Transform prefabRoot,
            SourceRenderer sourceRenderer)
        {
            var targetTransform = FindTransform(prefabRoot, sourceRenderer.Node.Path);
            if (targetTransform == null)
            {
                AddIssue(report, "missing-node", sourceRenderer.Node.Path,
                    "source renderer node has no matching prefab transform");
                return;
            }

            var target = targetTransform.GetComponent<ParticleSystemRenderer>();
            if (target == null)
            {
                AddIssue(report, "missing-component", sourceRenderer.Node.Path,
                    "source ParticleSystemRenderer has no matching prefab component");
                return;
            }

            var sourceMaterialSlots = ReadPointerSlots(sourceRenderer.Parameters, "m_Materials");
            var expectedMaterials = sourceMaterialSlots.Count;
            var expectedNullMaterials = sourceMaterialSlots.Count(slot => !slot);
            var actualMaterials = target.sharedMaterials?.Length ?? 0;
            var nullMaterials = target.sharedMaterials?.Count(material => material == null) ?? 0;
            report.RendererSlots.Add(new RendererSlotMetric
            {
                Path = sourceRenderer.Node.Path,
                SourceMaterialSlots = expectedMaterials,
                SourceNullMaterialSlots = expectedNullMaterials,
                PrefabMaterialSlots = actualMaterials,
                PrefabNullMaterialSlots = nullMaterials,
                SourceMeshSlots = CountSourceMeshSlots(sourceRenderer.Parameters),
                PrefabMeshSlots = CountPrefabMeshSlots(target)
            });

            if (expectedMaterials != actualMaterials || expectedNullMaterials != nullMaterials)
            {
                AddIssue(report, "material-slots", sourceRenderer.Node.Path,
                    $"source={expectedMaterials} (null={expectedNullMaterials}), " +
                    $"prefab={actualMaterials} (null={nullMaterials})");
            }

            var expectedMeshes = CountSourceMeshSlots(sourceRenderer.Parameters);
            var actualMeshes = CountPrefabMeshSlots(target);
            if (expectedMeshes != actualMeshes)
            {
                AddIssue(report, "mesh-slots", sourceRenderer.Node.Path,
                    $"source={expectedMeshes}, prefab={actualMeshes}");
            }

            AuditRendererParameters(report, sourceRenderer.Node.Path, sourceRenderer.Parameters, target);
        }

        private static void AuditRendererParameters(
            AuditReport report,
            string path,
            JObject source,
            ParticleSystemRenderer target)
        {
            if (source == null)
                return;

            var serialized = new SerializedObject(target);
            CompareSerializedBool(report, path, source, "m_Enabled", serialized.FindProperty("m_Enabled"), "enabled");
            CompareSerializedInt(report, path, source, "m_RenderMode", serialized.FindProperty("m_RenderMode"), "renderMode");
            CompareSerializedInt(report, path, source, "m_SortMode", serialized.FindProperty("m_SortMode"), "sortMode");
            CompareSerializedFloat(report, path, source, "m_MinParticleSize", serialized.FindProperty("m_MinParticleSize"), "minParticleSize");
            CompareSerializedFloat(report, path, source, "m_MaxParticleSize", serialized.FindProperty("m_MaxParticleSize"), "maxParticleSize");
            CompareSerializedFloat(report, path, source, "m_CameraVelocityScale", serialized.FindProperty("m_CameraVelocityScale"), "cameraVelocityScale");
            CompareSerializedFloat(report, path, source, "m_VelocityScale", serialized.FindProperty("m_VelocityScale"), "velocityScale");
            CompareSerializedFloat(report, path, source, "m_LengthScale", serialized.FindProperty("m_LengthScale"), "lengthScale");
            CompareSerializedFloat(report, path, source, "m_SortingFudge", serialized.FindProperty("m_SortingFudge"), "sortingFudge");
            CompareSerializedFloat(report, path, source, "m_NormalDirection", serialized.FindProperty("m_NormalDirection"), "normalDirection");
            CompareSerializedFloat(report, path, source, "m_ShadowBias", serialized.FindProperty("m_ShadowBias"), "shadowBias");
            CompareSerializedInt(report, path, source, "m_RenderAlignment", serialized.FindProperty("m_RenderAlignment"), "renderAlignment");
            CompareSerializedVector3(report, path, source, "m_Pivot", serialized.FindProperty("m_Pivot"), "pivot");
            CompareSerializedVector3(report, path, source, "m_Flip", serialized.FindProperty("m_Flip"), "flip");
            CompareSerializedBool(report, path, source, "m_UseCustomVertexStreams", serialized.FindProperty("m_UseCustomVertexStreams"), "useCustomVertexStreams");
            CompareSerializedBool(report, path, source, "m_EnableGPUInstancing", serialized.FindProperty("m_EnableGPUInstancing"), "enableGPUInstancing");
            CompareSerializedBool(report, path, source, "m_EnableAdvancedGPUInstancing", serialized.FindProperty("m_EnableAdvancedGPUInstancing"), "enableAdvancedGPUInstancing");
            CompareSerializedBool(report, path, source, "m_ApplyActiveColorSpace", serialized.FindProperty("m_ApplyActiveColorSpace"), "applyActiveColorSpace");
            CompareSerializedBool(report, path, source, "m_AllowRoll", serialized.FindProperty("m_AllowRoll"), "allowRoll");
            CompareSerializedBool(report, path, source, "m_UseOctagonShape", serialized.FindProperty("m_UseOctagonShape"), "useOctagonShape");
            CompareSerializedBool(report, path, source, "m_LockAxis", serialized.FindProperty("m_LockAxis"), "lockAxis");
            CompareSerializedBool(report, path, source, "m_RotationXAxis", serialized.FindProperty("m_RotationXAxis"), "rotationXAxis");
            CompareSerializedBool(report, path, source, "m_RotationYAxis", serialized.FindProperty("m_RotationYAxis"), "rotationYAxis");
            CompareSerializedBool(report, path, source, "m_RotationZAxis", serialized.FindProperty("m_RotationZAxis"), "rotationZAxis");
            CompareSerializedBool(report, path, source, "m_FollowRotation", serialized.FindProperty("m_FollowRotation"), "followRotation");

            var sourceStreams = source["m_VertexStreams"] as JArray;
            var useCustomStreams = serialized.FindProperty("m_UseCustomVertexStreams")?.boolValue ?? false;
            if (sourceStreams != null && useCustomStreams)
            {
                var streamProperty = serialized.FindProperty("m_VertexStreams");
                var actualStreams = streamProperty == null
                    ? Array.Empty<int>()
                    : Enumerable.Range(0, streamProperty.arraySize)
                        .Select(index => streamProperty.GetArrayElementAtIndex(index).intValue)
                        .ToArray();
                var expectedStreams = sourceStreams.Values<int>().ToArray();
                if (!expectedStreams.SequenceEqual(actualStreams))
                {
                    AddIssue(report, "vertex-streams", path,
                        $"source=[{string.Join(",", expectedStreams)}], prefab=[{string.Join(",", actualStreams)}]");
                }
            }

            foreach (var material in target.sharedMaterials ?? Array.Empty<Material>())
            {
                if (material == null)
                    continue;
                if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                {
                    AddIssue(report, "invalid-shader", path,
                        $"material '{material.name}' has no usable shader");
                }
            }
        }

        private static void CompareSerializedBool(
            AuditReport report, string path, JObject source, string sourceName,
            SerializedProperty property, string label)
        {
            var expected = source.Value<bool?>(sourceName);
            if (expected.HasValue && property != null && expected.Value != property.boolValue)
                AddIssue(report, "renderer-parameter", path + "/" + label,
                    $"source={expected.Value}, prefab={property.boolValue}");
        }

        private static void CompareSerializedInt(
            AuditReport report, string path, JObject source, string sourceName,
            SerializedProperty property, string label)
        {
            var expected = source.Value<int?>(sourceName);
            if (expected.HasValue && property != null && expected.Value != property.intValue)
                AddIssue(report, "renderer-parameter", path + "/" + label,
                    $"source={expected.Value}, prefab={property.intValue}");
        }

        private static void CompareSerializedFloat(
            AuditReport report, string path, JObject source, string sourceName,
            SerializedProperty property, string label)
        {
            var expected = source.Value<float?>(sourceName);
            if (expected.HasValue && property != null && Mathf.Abs(expected.Value - property.floatValue) > 0.0001f)
                AddIssue(report, "renderer-parameter", path + "/" + label,
                    $"source={expected.Value:R}, prefab={property.floatValue:R}");
        }

        private static void CompareSerializedVector3(
            AuditReport report, string path, JObject source, string sourceName,
            SerializedProperty property, string label)
        {
            if (!(source[sourceName] is JObject value))
                return;
            var expected = new Vector3(
                value.Value<float?>("x") ?? 0f,
                value.Value<float?>("y") ?? 0f,
                value.Value<float?>("z") ?? 0f);
            if (property != null && (expected - property.vector3Value).sqrMagnitude > 0.0000001f)
                AddIssue(report, "renderer-parameter", path + "/" + label,
                    $"source={expected}, prefab={property.vector3Value}");
        }

        private static int CountPrefabMeshSlots(ParticleSystemRenderer renderer)
        {
            var serialized = new SerializedObject(renderer);
            var count = 0;
            foreach (var name in new[] { "m_Mesh", "m_Mesh1", "m_Mesh2", "m_Mesh3" })
            {
                var property = serialized.FindProperty(name);
                if (property != null && property.objectReferenceValue != null)
                    count++;
            }
            return count;
        }

        private static int CountSourceMeshSlots(JObject rendererJson)
        {
            return new[] { "m_Mesh", "m_Mesh1", "m_Mesh2", "m_Mesh3" }
                .Count(name => HasPointer(rendererJson?[name]));
        }

        private static List<bool> ReadPointerSlots(JObject root, string propertyName)
        {
            var result = new List<bool>();
            if (!(root?[propertyName] is JArray array))
                return result;

            foreach (var item in array)
                result.Add(HasPointer(item));
            return result;
        }

        private static bool HasPointer(JToken token)
        {
            if (!(token is JObject pointer))
                return false;
            var fileId = pointer.Value<long?>("m_FileID") ?? pointer.Value<long?>("FileID") ?? 0;
            var pathId = pointer.Value<long?>("m_PathID") ?? pointer.Value<long?>("PathID") ?? 0;
            return fileId != 0 || pathId != 0;
        }

        private static Transform FindTransform(Transform root, string sourcePath)
        {
            var normalized = (sourcePath ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.Equals(normalized, root.name, StringComparison.Ordinal))
                return root;
            var prefix = root.name + "/";
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                normalized = normalized.Substring(prefix.Length);
            return string.IsNullOrEmpty(normalized) ? root : root.Find(normalized);
        }

        private static ParameterReadResult ReadParameters(string sourceDirectory, string parametersFile)
        {
            if (string.IsNullOrEmpty(parametersFile))
                return new ParameterReadResult(new JObject(), false);
            var path = parametersFile;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(sourceDirectory, parametersFile);
            if (!File.Exists(path))
                return new ParameterReadResult(new JObject(), false);
            try
            {
                return new ParameterReadResult(JObject.Parse(File.ReadAllText(path)), true);
            }
            catch (JsonException)
            {
                return new ParameterReadResult(new JObject(), false);
            }
        }

        private static int CountNonEmptyCurveArrays(JToken token)
        {
            var count = 0;
            if (token is JObject objectToken)
            {
                foreach (var property in objectToken.Properties())
                {
                    if (property.Name == "m_Curve" && property.Value is JArray curve && curve.Count > 0)
                        count++;
                    count += CountNonEmptyCurveArrays(property.Value);
                }
            }
            else if (token is JArray arrayToken)
            {
                foreach (var item in arrayToken)
                    count += CountNonEmptyCurveArrays(item);
            }
            return count;
        }

        private static int CountSerializedCurveArrays(SerializedProperty root)
        {
            var count = 0;
            var iterator = root.Copy();
            var end = root.GetEndProperty();
            var enterChildren = true;
            var guard = 0;
            while (iterator.Next(enterChildren) && !SerializedProperty.EqualContents(iterator, end) && guard++ < 10000)
            {
                enterChildren = true;
                if (iterator.name == "m_Curve" && iterator.isArray && iterator.arraySize > 0)
                    count++;
            }
            return count;
        }

        private static void CollectSerializedArraySizes(
            SerializedProperty root,
            string propertyName,
            List<int> result)
        {
            var iterator = root.Copy();
            var end = root.GetEndProperty();
            var enterChildren = true;
            var guard = 0;
            while (iterator.Next(enterChildren) && !SerializedProperty.EqualContents(iterator, end) && guard++ < 10000)
            {
                enterChildren = true;
                if (iterator.name == propertyName && iterator.isArray)
                    result.Add(iterator.arraySize);
            }
        }

        private static IEnumerable<int> GetArraySizes(JToken root, string propertyName)
        {
            if (root is JObject objectToken)
            {
                foreach (var property in objectToken.Properties())
                {
                    if (property.Name == propertyName && property.Value is JArray array)
                        yield return array.Count;
                    foreach (var size in GetArraySizes(property.Value, propertyName))
                        yield return size;
                }
            }
            else if (root is JArray arrayToken)
            {
                foreach (var item in arrayToken)
                    foreach (var size in GetArraySizes(item, propertyName))
                        yield return size;
            }
        }

        private static ModuleMetric GetModuleMetric(AuditReport report, string moduleName)
        {
            if (!report.Modules.TryGetValue(moduleName, out var metric))
            {
                metric = new ModuleMetric();
                report.Modules.Add(moduleName, metric);
            }
            return metric;
        }

        private static void CompareCount(AuditReport report, string name, int source, int prefab)
        {
            if (source != prefab)
                AddIssue(report, "count", name, $"source={source}, prefab={prefab}");
        }

        private static void AddIssue(AuditReport report, string category, string path, string detail)
        {
            report.Issues.Add(new AuditIssue { Category = category, Path = path, Detail = detail });
        }

        private static bool HistogramEquals(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            if (left.Count != right.Count)
                return false;
            return left.OrderBy(value => value).SequenceEqual(right.OrderBy(value => value));
        }

        private static string FormatHistogram(IEnumerable<int> values)
        {
            return string.Join(",", values.OrderBy(value => value));
        }

        private static void WriteReport(AuditReport report, string reportPath)
        {
            var absolutePath = Path.IsPathRooted(reportPath)
                ? reportPath
                : Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? ".");
            File.WriteAllText(absolutePath,
                JsonConvert.SerializeObject(report, Formatting.Indented));
        }

        private static void LogResult(AuditReport report, string reportPath)
        {
            var message = $"SR effect audit {(report.Passed ? "PASS" : "FAIL")}: " +
                          $"{report.Issues.Count} issues. Report: {reportPath}";
            if (report.Passed)
                Debug.Log(message);
            else
                Debug.LogError(message);
        }

        private static string ToAssetPath(string absolutePath)
        {
            var normalized = absolutePath.Replace('\\', '/');
            var dataPath = Application.dataPath.Replace('\\', '/');
            return normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + normalized.Substring(dataPath.Length)
                : absolutePath;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static string ReadArgument(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            if (index < 0 || index + 1 >= arguments.Length)
                throw new ArgumentException($"Missing required command-line argument {name}.");
            return arguments[index + 1];
        }

        private static string ReadOptionalArgument(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : string.Empty;
        }

        [Serializable]
        public sealed class AuditReport
        {
            public string GeneratedAtUtc;
            public string ManifestPath;
            public string PrefabPath;
            public bool Passed;
            public int SourceNodeCount;
            public int PrefabNodeCount;
            public int SourceParticleSystemCount;
            public int PrefabParticleSystemCount;
            public int SourceRendererCount;
            public int PrefabRendererCount;
            public int SourceLightCount;
            public int PrefabLightCount;
            public int SourceParameterMissingCount;
            public Dictionary<string, ModuleMetric> Modules = new Dictionary<string, ModuleMetric>();
            public List<RendererSlotMetric> RendererSlots = new List<RendererSlotMetric>();
            public List<AuditIssue> Issues = new List<AuditIssue>();
        }

        [Serializable]
        public sealed class ModuleMetric
        {
            public int SourceEnabled;
            public int PrefabEnabled;
            public int SourceCurveArrays;
            public int PrefabCurveArrays;
            public List<int> SourceAlphaKeyCounts = new List<int>();
            public List<int> PrefabAlphaKeyCounts = new List<int>();
        }

        [Serializable]
        public sealed class RendererSlotMetric
        {
            public string Path;
            public int SourceMaterialSlots;
            public int SourceNullMaterialSlots;
            public int PrefabMaterialSlots;
            public int PrefabNullMaterialSlots;
            public int SourceMeshSlots;
            public int PrefabMeshSlots;
        }

        [Serializable]
        public sealed class AuditIssue
        {
            public string Category;
            public string Path;
            public string Detail;
        }

        private sealed class SourceManifest
        {
            public SourceNode[] Nodes;
        }

        private sealed class SourceNode
        {
            public string Name;
            public string Path;
            public SourceComponent[] Components;
        }

        private sealed class SourceComponent
        {
            public string Type;
            public string ParametersFile;
            public SourceReference[] References;
        }

        private sealed class SourceReference
        {
            public string Type;
        }

        private sealed class SourceParticle
        {
            public SourceNode Node;
            public SourceComponent Component;
            public JObject Parameters;
            public bool ParametersAvailable;

            public SourceParticle(
                SourceNode node,
                SourceComponent component,
                JObject parameters,
                bool parametersAvailable)
            {
                Node = node;
                Component = component;
                Parameters = parameters;
                ParametersAvailable = parametersAvailable;
            }
        }

        private sealed class ParameterReadResult
        {
            public JObject Json { get; }
            public bool Available { get; }

            public ParameterReadResult(JObject json, bool available)
            {
                Json = json;
                Available = available;
            }
        }

        private sealed class SourceRenderer
        {
            public SourceNode Node;
            public SourceComponent Component;
            public JObject Parameters;
            public bool ParametersAvailable;

            public SourceRenderer(
                SourceNode node,
                SourceComponent component,
                JObject parameters,
                bool parametersAvailable)
            {
                Node = node;
                Component = component;
                Parameters = parameters;
                ParametersAvailable = parametersAvailable;
            }
        }
    }
}

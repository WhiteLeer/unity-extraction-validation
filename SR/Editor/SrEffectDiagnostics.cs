using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace SrEffectPrefabTools
{
    public enum SrEffectDiagnosticMode
    {
        All,
        Group,
        ParticleSystem
    }

    [ExecuteAlways]
    [AddComponentMenu("SR/Effect Diagnostics Controller")]
    public sealed class SrEffectDiagnosticsController : MonoBehaviour
    {
        [Tooltip("All, Group, or ParticleSystem")]
        public SrEffectDiagnosticMode isolationMode = SrEffectDiagnosticMode.All;

        [Tooltip("Group path or ParticleSystem relative path")]
        public string targetPath = string.Empty;

        [Tooltip("Reapply isolation when serialized fields change")]
        public bool applyOnValidate = true;

        private readonly Dictionary<int, bool> originalRendererStates = new();
        private readonly Dictionary<int, bool> originalLightStates = new();
        private readonly Dictionary<int, bool> originalEmissionStates = new();

        private void OnEnable()
        {
            if (applyOnValidate)
                ApplyIsolation();
        }

        private void OnValidate()
        {
            if (applyOnValidate)
                ApplyIsolation();
        }

        [ContextMenu("Apply Isolation")]
        public void ApplyIsolation()
        {
            var systems = GetComponentsInChildren<ParticleSystem>(true);
            var root = transform;
            var systemByPath = systems
                .GroupBy(system => RelativePath(root, system.transform), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var selectedSystems = new HashSet<ParticleSystem>();

            if (isolationMode == SrEffectDiagnosticMode.All)
            {
                foreach (var system in systems)
                    selectedSystems.Add(system);
            }
            else if (isolationMode == SrEffectDiagnosticMode.Group)
            {
                foreach (var system in systems)
                {
                    if (string.Equals(GroupPath(root, system.transform), targetPath, StringComparison.Ordinal))
                        selectedSystems.Add(system);
                }
            }
            else if (systemByPath.TryGetValue(targetPath ?? string.Empty, out var selected))
            {
                foreach (var system in selected)
                    AddWithSubEmitters(system, selectedSystems);
            }

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var id = renderer.GetInstanceID();
                if (!originalRendererStates.ContainsKey(id))
                    originalRendererStates[id] = renderer.enabled;

                var enabled = isolationMode == SrEffectDiagnosticMode.All ||
                              (isolationMode == SrEffectDiagnosticMode.Group &&
                               string.Equals(SrEffectDiagnosticsController.GroupPath(root, renderer.transform),
                                   targetPath, StringComparison.Ordinal)) ||
                              (isolationMode == SrEffectDiagnosticMode.ParticleSystem &&
                               selectedSystems.Any(system => system.transform == renderer.transform ||
                                                             system.transform.IsChildOf(renderer.transform) ||
                                                             renderer.transform.IsChildOf(system.transform)));
                renderer.enabled = enabled && originalRendererStates[id];
            }

            foreach (var light in GetComponentsInChildren<Light>(true))
            {
                var id = light.GetInstanceID();
                if (!originalLightStates.ContainsKey(id))
                    originalLightStates[id] = light.enabled;
                light.enabled = isolationMode == SrEffectDiagnosticMode.All ||
                                (isolationMode == SrEffectDiagnosticMode.Group &&
                                 string.Equals(SrEffectDiagnosticsController.GroupPath(root, light.transform),
                                     targetPath, StringComparison.Ordinal)) ||
                                (isolationMode == SrEffectDiagnosticMode.ParticleSystem &&
                                 selectedSystems.Any(system => light.transform == system.transform ||
                                                               light.transform.IsChildOf(system.transform) ||
                                                               system.transform.IsChildOf(light.transform)));
                light.enabled &= originalLightStates[id];
            }

            foreach (var system in systems)
            {
                var id = system.GetInstanceID();
                var emission = system.emission;
                if (!originalEmissionStates.ContainsKey(id))
                    originalEmissionStates[id] = emission.enabled;

                var enabled = selectedSystems.Contains(system);
                emission.enabled = enabled && originalEmissionStates[id];
                if (!enabled)
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                else if (Application.isPlaying)
                    system.Play(true);
            }

            if (!Application.isPlaying)
                EditorUtility.SetDirty(this);
        }

        private static void AddWithSubEmitters(
            ParticleSystem system,
            HashSet<ParticleSystem> result)
        {
            if (system == null || !result.Add(system))
                return;

            var subEmitters = system.subEmitters;
            for (var index = 0; index < subEmitters.subEmittersCount; index++)
                AddWithSubEmitters(subEmitters.GetSubEmitterSystem(index), result);
        }

        internal static string RelativePath(Transform root, Transform target)
        {
            if (target == root)
                return string.Empty;

            var parts = new List<string>();
            for (var current = target; current != null && current != root; current = current.parent)
                parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        internal static string GroupPath(Transform root, Transform target)
        {
            var path = RelativePath(root, target);
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return string.Empty;
            return parts.Length > 1 && string.Equals(parts[0], "internal", StringComparison.OrdinalIgnoreCase)
                ? $"{parts[0]}/{parts[1]}"
                : parts[0];
        }
    }

    public static class SrEffectDiagnostics
    {
        private const string CerydraPrefabPath =
            "Assets/unity-extraction-validation/SR/effects/cerydra/Eff_Avatar_Cerydra_00_MazeSkill_01/Eff_Avatar_Cerydra_00_MazeSkill_01.prefab";
        private const string CerydraManifestPath =
            @"C:\Users\wepie\AppData\Local\Temp\cerydra_manifest_inspect_dea976a5b1264633b41c70735e860f1c\manifest.json";
        private const string DefaultReportPath =
            "Assets/unity-extraction-validation/SR/effects/cerydra/Diagnostics/Eff_Avatar_Cerydra_00_MazeSkill_01.diagnostics.json";
        private const string DefaultSampleReportPath =
            "Assets/unity-extraction-validation/SR/effects/cerydra/Diagnostics/Eff_Avatar_Cerydra_00_MazeSkill_01.samples.json";
        private const string DefaultParticleSampleReportPath =
            "Assets/unity-extraction-validation/SR/effects/cerydra/Diagnostics/Eff_Avatar_Cerydra_00_MazeSkill_01.particles.json";
        private const string CloneName = "__SR_EffectDiagnostics__";

        [MenuItem("Tools/SR/Effect Diagnostics/Scan Cerydra")]
        public static void ScanCerydra()
        {
            ScanPrefab(CerydraPrefabPath, DefaultReportPath);
            Debug.Log($"SR effect diagnostic report written to {DefaultReportPath}.");
        }

        [MenuItem("Tools/SR/Effect Diagnostics/Rebuild Cerydra Prefab")]
        public static void RebuildCerydraPrefab()
        {
            var prefab = SrEffectPrefabImporter.Import(CerydraManifestPath, CerydraPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"SR Cerydra prefab rebuilt: {prefab.name}");
        }

        [MenuItem("Tools/SR/Effect Diagnostics/Create Cerydra Diagnostic Instance")]
        public static void CreateCerydraInstance()
        {
            CreateDiagnosticInstance(CerydraPrefabPath);
        }

        [MenuItem("Tools/SR/Effect Diagnostics/Sample Cerydra Groups")]
        public static void SampleCerydraGroups()
        {
            var report = SamplePrefab(CerydraPrefabPath);
            var absolute = ToAbsolutePath(DefaultSampleReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, JsonConvert.SerializeObject(report, Formatting.Indented));
            AssetDatabase.ImportAsset(DefaultSampleReportPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"SR effect group sample written to {DefaultSampleReportPath}: {report.Groups.Length} groups.");
        }

        [MenuItem("Tools/SR/Effect Diagnostics/Sample Cerydra Particle Systems")]
        public static void SampleCerydraParticleSystems()
        {
            var report = SampleParticleSystems(CerydraPrefabPath);
            var absolute = ToAbsolutePath(DefaultParticleSampleReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, JsonConvert.SerializeObject(report, Formatting.Indented));
            AssetDatabase.ImportAsset(DefaultParticleSampleReportPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"SR particle-system sample written to {DefaultParticleSampleReportPath}: {report.ParticleSystems.Length} systems.");
        }

        [MenuItem("Tools/SR/Effect Diagnostics/Remove Diagnostic Instance")]
        public static void RemoveDiagnosticInstance()
        {
            var existing = GameObject.Find(CloneName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);
        }

        [MenuItem("Tools/SR/Effect Diagnostics/Simulate Diagnostic Group")]
        public static void SimulateDiagnosticGroup()
        {
            var instance = GameObject.Find(CloneName);
            if (instance == null)
                throw new InvalidOperationException("Create the Cerydra diagnostic instance first.");

            var controller = instance.GetComponent<SrEffectDiagnosticsController>();
            if (controller == null || controller.isolationMode != SrEffectDiagnosticMode.Group ||
                string.IsNullOrWhiteSpace(controller.targetPath))
                throw new InvalidOperationException("Set the diagnostic controller to Group mode and choose targetPath first.");

            controller.ApplyIsolation();
            // Do not let ExecuteAlways/OnValidate immediately undo the temporary preview state.
            controller.applyOnValidate = false;
            var systems = instance.GetComponentsInChildren<ParticleSystem>(true)
                .Where(system => string.Equals(
                    SrEffectDiagnosticsController.GroupPath(instance.transform, system.transform),
                    controller.targetPath, StringComparison.Ordinal))
                .ToArray();
            if (systems.Length == 0)
                throw new InvalidOperationException($"No ParticleSystems found for group '{controller.targetPath}'.");

            // Delayed qizi groups need a sample after their one-second start delay.
            var sampledTimes = new List<float>();
            foreach (var system in systems)
            {
                // Some SR effects leave emission disabled until their runtime MonoBehaviour starts them.
                // This is a diagnostic-only clone, so force the selected systems on before simulating.
                var emission = system.emission;
                emission.enabled = true;
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(true);
                var sampleTime = GetVisualSampleTime(system);
                system.Simulate(sampleTime, true, true, false);
                sampledTimes.Add(sampleTime);
            }

            foreach (var renderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (string.Equals(
                        SrEffectDiagnosticsController.GroupPath(instance.transform, renderer.transform),
                        controller.targetPath, StringComparison.Ordinal))
                {
                    renderer.enabled = true;
                    renderer.forceRenderingOff = false;
                }
            }

            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            SceneView.RepaintAll();
            Debug.Log($"SR diagnostic group simulated: {controller.targetPath}, systems={systems.Length}, time={sampledTimes.Min():F2}-{sampledTimes.Max():F2}s.");
        }

        private static float GetVisualSampleTime(ParticleSystem system)
        {
            var main = system.main;
            var delay = Mathf.Max(0f, main.startDelay.constantMax);
            var lifetime = Mathf.Max(0.1f, main.startLifetime.constantMax);
            var activeWindow = Mathf.Min(Mathf.Max(0.1f, main.duration), lifetime);
            return Mathf.Clamp(delay + activeWindow * 0.5f, 0.05f, 3f);
        }

        public static DiagnosticReport ScanPrefab(string prefabPath, string reportPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new FileNotFoundException($"SR effect prefab was not found: {prefabPath}");

            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            var entries = systems
                .Select(system => BuildEntry(prefab.transform, system))
                .OrderBy(entry => entry.Group, StringComparer.Ordinal)
                .ThenBy(entry => entry.Path, StringComparer.Ordinal)
                .ToArray();

            var report = new DiagnosticReport
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                PrefabPath = prefabPath,
                NodeCount = prefab.GetComponentsInChildren<Transform>(true).Length,
                ParticleSystemCount = entries.Length,
                Groups = entries.Select(entry => entry.Group).Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
                ParticleSystems = entries
            };

            var absolute = ToAbsolutePath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, JsonConvert.SerializeObject(report, Formatting.Indented));
            AssetDatabase.ImportAsset(reportPath.Replace('\\', '/'), ImportAssetOptions.ForceSynchronousImport);
            return report;
        }

        public static GameObject CreateDiagnosticInstance(string prefabPath)
        {
            RemoveDiagnosticInstance();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new FileNotFoundException($"SR effect prefab was not found: {prefabPath}");

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Could not instantiate SR effect prefab: {prefabPath}");

            instance.name = CloneName;
            var controller = instance.AddComponent<SrEffectDiagnosticsController>();
            controller.isolationMode = SrEffectDiagnosticMode.All;
            controller.targetPath = string.Empty;
            controller.ApplyIsolation();
            Undo.RegisterCreatedObjectUndo(instance, "Create SR effect diagnostic instance");
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            return instance;
        }

        public static SampleReport SamplePrefab(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new FileNotFoundException($"SR effect prefab was not found: {prefabPath}");

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Could not instantiate SR effect prefab: {prefabPath}");

            try
            {
                instance.name = CloneName + "_Sample";
                var controller = instance.AddComponent<SrEffectDiagnosticsController>();
                var groups = instance.GetComponentsInChildren<ParticleSystem>(true)
                    .Select(system => SrEffectDiagnosticsController.GroupPath(instance.transform, system.transform))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(group => group, StringComparer.Ordinal)
                    .ToArray();
                var groupReports = new List<GroupSample>();
                foreach (var group in groups)
                {
                    controller.isolationMode = SrEffectDiagnosticMode.Group;
                    controller.targetPath = group;
                    controller.ApplyIsolation();
                    groupReports.Add(SampleGroup(instance.transform, group));
                }

                return new SampleReport
                {
                    PrefabPath = prefabPath,
                    GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                    Groups = groupReports.ToArray()
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static GroupSample SampleGroup(Transform root, string group)
        {
            var systems = root.GetComponentsInChildren<ParticleSystem>(true)
                .Where(system => string.Equals(
                    SrEffectDiagnosticsController.GroupPath(root, system.transform),
                    group, StringComparison.Ordinal))
                .OrderBy(system => SrEffectDiagnosticsController.RelativePath(root, system.transform), StringComparer.Ordinal)
                .ToArray();
            var samples = new List<SystemSample>();
            // Include delayed one-shot effects instead of only sampling absolute times.
            foreach (var time in new[] { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 1.05f, 1.1f, 1.25f, 1.5f, 2f, 3f })
            {
                foreach (var system in systems)
                {
                    system.Simulate(time, true, true);
                    var renderer = system.GetComponent<ParticleSystemRenderer>();
                    var materials = renderer?.sharedMaterials ?? Array.Empty<Material>();
                    var validMaterials = materials.Count(material =>
                        material != null && material.shader != null &&
                        material.shader.name != "Hidden/InternalErrorShader");
                    var bounds = renderer?.bounds ?? new Bounds(system.transform.position, Vector3.zero);
                    samples.Add(new SystemSample
                    {
                        Time = time,
                        Path = SrEffectDiagnosticsController.RelativePath(root, system.transform),
                        ParticleCount = system.particleCount,
                        RendererEnabled = renderer != null && renderer.enabled,
                        MaterialCount = materials.Length,
                        ValidMaterialCount = validMaterials,
                        BoundsX = bounds.size.x,
                        BoundsY = bounds.size.y,
                        BoundsZ = bounds.size.z
                    });
                }
            }

            return new GroupSample { Group = group, Systems = samples.ToArray() };
        }

        private static ParticleSampleReport SampleParticleSystems(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new FileNotFoundException($"SR effect prefab was not found: {prefabPath}");

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Could not instantiate SR effect prefab: {prefabPath}");

            try
            {
                instance.name = CloneName + "_ParticleSample";
                var controller = instance.AddComponent<SrEffectDiagnosticsController>();
                var systems = instance.GetComponentsInChildren<ParticleSystem>(true)
                    .OrderBy(system => SrEffectDiagnosticsController.RelativePath(instance.transform, system.transform), StringComparer.Ordinal)
                    .Select(system =>
                    {
                        var path = SrEffectDiagnosticsController.RelativePath(instance.transform, system.transform);
                        controller.isolationMode = SrEffectDiagnosticMode.ParticleSystem;
                        controller.targetPath = path;
                        controller.ApplyIsolation();
                        return new ParticleSystemSample
                        {
                            Path = path,
                            Group = SrEffectDiagnosticsController.GroupPath(instance.transform, system.transform),
                            Samples = SampleSystem(instance.transform, system)
                        };
                    })
                    .ToArray();

                return new ParticleSampleReport
                {
                    PrefabPath = prefabPath,
                    GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                    ParticleSystems = systems
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static SystemSample[] SampleSystem(Transform root, ParticleSystem system)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            var materials = renderer?.sharedMaterials ?? Array.Empty<Material>();
            var validMaterials = materials.Count(material =>
                material != null && material.shader != null &&
                material.shader.name != "Hidden/InternalErrorShader");
            var main = system.main;
            var samples = new List<SystemSample>();
            // Include delayed one-shot effects instead of only sampling absolute times.
            foreach (var time in new[] { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 1.05f, 1.1f, 1.25f, 1.5f, 2f, 3f })
            {
                system.Simulate(time, true, true);
                var bounds = renderer?.bounds ?? new Bounds(system.transform.position, Vector3.zero);
                samples.Add(new SystemSample
                {
                    Time = time,
                    Path = SrEffectDiagnosticsController.RelativePath(root, system.transform),
                    ParticleCount = system.particleCount,
                    RendererEnabled = renderer != null && renderer.enabled,
                    MaterialCount = materials.Length,
                    ValidMaterialCount = validMaterials,
                    BoundsX = bounds.size.x,
                    BoundsY = bounds.size.y,
                    BoundsZ = bounds.size.z,
                    RenderMode = renderer == null ? string.Empty : renderer.renderMode.ToString(),
                    ShapeType = system.shape.shapeType.ToString(),
                    MeshScaleX = system.shape.scale.x,
                    MeshScaleY = system.shape.scale.y,
                    MeshScaleZ = system.shape.scale.z,
                    Looping = main.loop,
                    PlayOnAwake = main.playOnAwake,
                    Duration = main.duration,
                    StartLifetimeMax = main.startLifetime.constantMax,
                    StartSizeMax = main.startSize.constantMax
                });
            }

            return samples.ToArray();
        }

        private static DiagnosticEntry BuildEntry(Transform root, ParticleSystem system)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            var materialNames = renderer == null
                ? Array.Empty<string>()
                : renderer.sharedMaterials.Select(material => material == null ? "<null>" : material.name).ToArray();
            var shaderNames = renderer == null
                ? Array.Empty<string>()
                : renderer.sharedMaterials
                    .Where(material => material != null && material.shader != null)
                    .Select(material => material.shader.name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

            var subEmitterPaths = new List<string>();
            var subEmitters = system.subEmitters;
            for (var index = 0; index < subEmitters.subEmittersCount; index++)
            {
                var child = subEmitters.GetSubEmitterSystem(index);
                if (child != null)
                    subEmitterPaths.Add(SrEffectDiagnosticsController.RelativePath(root, child.transform));
            }

            var main = system.main;
            var emission = system.emission;
            var size = system.sizeOverLifetime;
            var color = system.colorOverLifetime;
            var trails = system.trails;
            var clamp = system.limitVelocityOverLifetime;
            var textureSheet = system.textureSheetAnimation;
            return new DiagnosticEntry
            {
                Path = SrEffectDiagnosticsController.RelativePath(root, system.transform),
                Group = SrEffectDiagnosticsController.GroupPath(root, system.transform),
                Active = system.gameObject.activeSelf,
                ParticleSystemEnabled = system.gameObject.activeSelf,
                RendererEnabled = renderer != null && renderer.enabled,
                RenderMode = renderer == null ? string.Empty : renderer.renderMode.ToString(),
                MaterialNames = materialNames,
                ShaderNames = shaderNames,
                SimulationSpace = main.simulationSpace.ToString(),
                ScalingMode = main.scalingMode.ToString(),
                Looping = main.loop,
                PlayOnAwake = main.playOnAwake,
                Duration = main.duration,
                StartLifetime = main.startLifetime.constant,
                StartSize = main.startSize.constant,
                EmissionEnabled = emission.enabled,
                EmissionRate = emission.rateOverTime.constant,
                SizeOverLifetimeEnabled = size.enabled,
                ColorOverLifetimeEnabled = color.enabled,
                TrailsEnabled = trails.enabled,
                LimitVelocityEnabled = clamp.enabled,
                LimitVelocitySpace = clamp.space.ToString(),
                MultiplyDragByParticleSize = clamp.multiplyDragByParticleSize,
                MultiplyDragByParticleVelocity = clamp.multiplyDragByParticleVelocity,
                TextureSheetEnabled = textureSheet.enabled,
                TextureSheetSpeedMin = textureSheet.speedRange.x,
                TextureSheetSpeedMax = textureSheet.speedRange.y,
                SubEmitterPaths = subEmitterPaths.ToArray()
            };
        }

        private static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        [Serializable]
        public sealed class DiagnosticReport
        {
            public string GeneratedAtUtc;
            public string PrefabPath;
            public int NodeCount;
            public int ParticleSystemCount;
            public string[] Groups;
            public DiagnosticEntry[] ParticleSystems;
        }

        [Serializable]
        public sealed class DiagnosticEntry
        {
            public string Path;
            public string Group;
            public bool Active;
            public bool ParticleSystemEnabled;
            public bool RendererEnabled;
            public string RenderMode;
            public string[] MaterialNames;
            public string[] ShaderNames;
            public string SimulationSpace;
            public string ScalingMode;
            public bool Looping;
            public bool PlayOnAwake;
            public float Duration;
            public float StartLifetime;
            public float StartSize;
            public bool EmissionEnabled;
            public float EmissionRate;
            public bool SizeOverLifetimeEnabled;
            public bool ColorOverLifetimeEnabled;
            public bool TrailsEnabled;
            public bool LimitVelocityEnabled;
            public string LimitVelocitySpace;
            public bool MultiplyDragByParticleSize;
            public bool MultiplyDragByParticleVelocity;
            public bool TextureSheetEnabled;
            public float TextureSheetSpeedMin;
            public float TextureSheetSpeedMax;
            public string[] SubEmitterPaths;
        }

        [Serializable]
        public sealed class SampleReport
        {
            public string GeneratedAtUtc;
            public string PrefabPath;
            public GroupSample[] Groups;
        }

        [Serializable]
        public sealed class ParticleSampleReport
        {
            public string GeneratedAtUtc;
            public string PrefabPath;
            public ParticleSystemSample[] ParticleSystems;
        }

        [Serializable]
        public sealed class ParticleSystemSample
        {
            public string Path;
            public string Group;
            public SystemSample[] Samples;
        }

        [Serializable]
        public sealed class GroupSample
        {
            public string Group;
            public SystemSample[] Systems;
        }

        [Serializable]
        public sealed class SystemSample
        {
            public float Time;
            public string Path;
            public int ParticleCount;
            public bool RendererEnabled;
            public int MaterialCount;
            public int ValidMaterialCount;
            public float BoundsX;
            public float BoundsY;
            public float BoundsZ;
            public string RenderMode;
            public string ShapeType;
            public float MeshScaleX;
            public float MeshScaleY;
            public float MeshScaleZ;
            public bool Looping;
            public bool PlayOnAwake;
            public float Duration;
            public float StartLifetimeMax;
            public float StartSizeMax;
        }
    }

    public sealed class SrEffectDiagnosticsWindow : EditorWindow
    {
        private GameObject prefab;
        private SrEffectDiagnostics.DiagnosticReport report;
        private Vector2 scroll;
        private string filter = string.Empty;

        [MenuItem("Tools/SR/Effect Diagnostics/Window")]
        public static void Open()
        {
            var window = GetWindow<SrEffectDiagnosticsWindow>("SR Effect Diagnostics");
            window.minSize = new Vector2(620f, 420f);
            window.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/unity-extraction-validation/SR/effects/cerydra/Eff_Avatar_Cerydra_00_MazeSkill_01/Eff_Avatar_Cerydra_00_MazeSkill_01.prefab");
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("SR Effect Diagnostics", EditorStyles.boldLabel);
            prefab = (GameObject)EditorGUILayout.ObjectField("Effect Prefab", prefab, typeof(GameObject), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan"))
                    Scan();
                if (GUILayout.Button("Create Diagnostic Instance"))
                {
                    if (prefab != null)
                        SrEffectDiagnostics.CreateDiagnosticInstance(AssetDatabase.GetAssetPath(prefab));
                }
            }

            if (report == null)
                return;

            EditorGUILayout.LabelField($"Nodes: {report.NodeCount}    ParticleSystems: {report.ParticleSystemCount}");
            filter = EditorGUILayout.TextField("Filter", filter);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var entry in report.ParticleSystems.Where(MatchesFilter))
            {
                EditorGUILayout.LabelField(entry.Group + " / " + entry.Path, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"Renderer={entry.RenderMode}, Material={string.Join(", ", entry.MaterialNames)}, " +
                    $"Shader={string.Join(", ", entry.ShaderNames)}");
                EditorGUILayout.LabelField(
                    $"Emission={entry.EmissionEnabled}:{entry.EmissionRate:0.###}, " +
                    $"Lifetime={entry.StartLifetime:0.###}, Size={entry.StartSize:0.###}, " +
                    $"SubEmitters={entry.SubEmitterPaths.Length}, " +
                    $"Clamp={entry.LimitVelocityEnabled}:{entry.LimitVelocitySpace}, " +
                    $"UVSheet={entry.TextureSheetEnabled}:[{entry.TextureSheetSpeedMin:0.###},{entry.TextureSheetSpeedMax:0.###}]");
            }
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            if (prefab == null)
                return;
            var reportPath = "Assets/unity-extraction-validation/SR/effects/cerydra/Diagnostics/" +
                             prefab.name + ".diagnostics.json";
            report = SrEffectDiagnostics.ScanPrefab(AssetDatabase.GetAssetPath(prefab), reportPath);
        }

        private bool MatchesFilter(SrEffectDiagnostics.DiagnosticEntry entry)
        {
            return string.IsNullOrWhiteSpace(filter) ||
                   entry.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.Group.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

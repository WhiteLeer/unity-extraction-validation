using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrRuntimeEffectTools;
using UnityEditor;
using UnityEngine;

namespace SrEffectPrefabTools
{
    public static class SrEffectPrefabImporter
    {
        private const string DefaultOutputFolder = "Assets/unity-extraction-validation/SR/ReconstructedPrefabs";

        [MenuItem("Tools/SR/Rebuild Effect Prefab From Manifest...")]
        public static void ImportFromDialog()
        {
            var manifestPath = EditorUtility.OpenFilePanel("Select SR prefab package", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(manifestPath))
                return;

            Directory.CreateDirectory(ToAbsolutePath(DefaultOutputFolder));
            using var source = new ImportSource(manifestPath);
            var manifest = source.Manifest;
            if (!HasVisualComponents(manifest))
            {
                Debug.LogWarning($"Skipped transform-only SR package '{manifest.Name}'; it has no renderer, particle system, or light.");
                return;
            }
            var prefabFolder = $"{DefaultOutputFolder}/{SanitizeFileName(manifest.Name)}";
            EnsureAssetFolder(prefabFolder);
            var outputPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{prefabFolder}/{SanitizeFileName(manifest.Name)}.prefab");
            Import(manifestPath, outputPath);
        }

        public static void ImportFromCommandLine()
        {
            var arguments = Environment.GetCommandLineArgs();
            var manifestPath = ReadArgument(arguments, "-srManifest");
            var outputPath = ReadArgument(arguments, "-srOutput");
            Import(manifestPath, outputPath);
        }

        public static void ImportDirectoryFromCommandLine()
        {
            var arguments = Environment.GetCommandLineArgs();
            var manifestDirectory = ReadArgument(arguments, "-srManifestDir");
            var outputRoot = ReadArgument(arguments, "-srOutputRoot").Replace('\\', '/').TrimEnd('/');
            if (!outputRoot.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("The output root must be under Assets.", nameof(outputRoot));

            var packages = Directory.GetFiles(manifestDirectory, "*.srprefab", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (packages.Length == 0)
                throw new InvalidDataException($"No .srprefab packages found under '{manifestDirectory}'.");

            EnsureAssetFolder(outputRoot);
            var usedPrefabFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages)
            {
                using var source = new ImportSource(package);
                if (!HasVisualComponents(source.Manifest))
                {
                    Debug.LogWarning($"Skipped transform-only SR package '{source.Manifest.Name}'; it has no renderer, particle system, or light.");
                    continue;
                }
                var prefabFolderName = SanitizeFileName(source.Manifest.Name);
                if (!usedPrefabFolders.Add(prefabFolderName))
                {
                    var duplicateIndex = 2;
                    var baseName = prefabFolderName;
                    do
                    {
                        prefabFolderName = $"{baseName}__{duplicateIndex++}";
                    }
                    while (!usedPrefabFolders.Add(prefabFolderName));
                }

                // A package name is not globally unique in SR. Keep each package's
                // derived assets beside its own prefab so a later import cannot
                // delete materials or meshes referenced by an earlier duplicate.
                var prefabFolder = $"{outputRoot}/{prefabFolderName}";
                EnsureAssetFolder(prefabFolder);
                var outputPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{prefabFolder}/{SanitizeFileName(source.Manifest.Name)}.prefab");
                Import(package, outputPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Converted {packages.Length} SR prefab packages to native Unity assets under '{outputRoot}'.");
        }

        public static GameObject Import(string manifestPath, string outputAssetPath)
        {
            using var source = new ImportSource(manifestPath);
            var manifest = source.Manifest;
            if (manifest.Nodes == null || manifest.Nodes.Length == 0)
                throw new InvalidDataException("The manifest contains no nodes.");
            if (!outputAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("The output path must be under Assets/.", nameof(outputAssetPath));

            var nodes = manifest.Nodes.OrderBy(node => PathDepth(node.Path)).ToArray();
            var objects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            var primaryObjectsByPath = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            var warnings = new List<string>();
            var derivedRoot = GetDerivedRoot(outputAssetPath, manifest.Name);
            ClearGeneratedAssetFolders(derivedRoot);
            var meshes = CreateMeshes(manifest, derivedRoot);
            var materials = CreateMaterials(source, manifest, outputAssetPath, warnings);
            var lightCount = 0;
            var particleSystemCount = 0;
            var particleRendererCount = 0;
            var animatorCount = 0;
            var particleSystemsByPathId = new Dictionary<long, ParticleSystem>();
            var pendingParticleBindings = new List<PendingParticleBinding>();

            foreach (var node in nodes)
            {
                var gameObject = new GameObject(node.Name, GetNativeComponentTypes(node));
                var nodeKey = GetNodeKey(node);
                objects.Add(nodeKey, gameObject);
                if (!primaryObjectsByPath.ContainsKey(node.Path))
                    primaryObjectsByPath.Add(node.Path, gameObject);
                var parentPath = ParentPath(node.Path);
                if (!string.IsNullOrEmpty(parentPath))
                {
                    if (!primaryObjectsByPath.TryGetValue(parentPath, out var parent))
                        throw new InvalidDataException($"Missing parent node '{parentPath}' for '{node.Path}'.");
                    gameObject.transform.SetParent(parent.transform, false);
                }

                foreach (var component in node.Components ?? Array.Empty<ManifestComponent>())
                {
                    if (component.Type == "Transform" && !string.IsNullOrEmpty(component.ParametersFile))
                        ApplyTransform(gameObject.transform, source.Read<TransformData>(component.ParametersFile));
                    else if (component.Type == "MeshFilter" && !string.IsNullOrEmpty(component.ParametersFile))
                    {
                        var meshFilter = gameObject.GetComponent<MeshFilter>() ??
                                         throw new InvalidOperationException($"Failed to create MeshFilter on '{node.Path}'.");
                        var meshData = JObject.Parse(source.ReadText(component.ParametersFile));
                        meshFilter.sharedMesh = FindMesh(meshes, ReadPointer(meshData["m_Mesh"]));
                    }
                    else if (component.Type == "MeshRenderer")
                    {
                        var meshRenderer = gameObject.GetComponent<MeshRenderer>() ??
                                           throw new InvalidOperationException($"Failed to create MeshRenderer on '{node.Path}'.");
                        if (!string.IsNullOrEmpty(component.ParametersFile))
                            ApplyMeshRenderer(meshRenderer, source.ReadText(component.ParametersFile), materials, derivedRoot);
                        else
                        {
                            meshRenderer.sharedMaterials = new[] { GetFallbackMaterial(derivedRoot) };
                            warnings.Add($"{node.Path}: MeshRenderer parameters were unavailable; bound ExtractedMeshFallback.");
                        }
                    }
                    else if (component.Type == "Light" && !string.IsNullOrEmpty(component.ParametersFile))
                    {
                        ApplyLight(gameObject.GetComponent<Light>() ?? gameObject.AddComponent<Light>(), source.Read<LightData>(component.ParametersFile));
                        lightCount++;
                    }
                    else if (component.Type == "ParticleSystem" && !string.IsNullOrEmpty(component.ParametersFile))
                    {
                        var particleSystem = gameObject.GetComponent<ParticleSystem>() ??
                                             throw new InvalidOperationException($"Failed to create ParticleSystem on '{node.Path}'.");
                        if (component.ParametersStatus?.StartsWith("external-type-tree-", StringComparison.Ordinal) == true &&
                            !string.IsNullOrEmpty(component.ParametersFile))
                        {
                            var particleJson = source.ReadText(component.ParametersFile);
                            ApplyNativeSerializedData(particleSystem, particleJson, node.Path, warnings);
                            ApplyParticleSystem(particleSystem, DeserializeParticleSystemData(particleJson));
                            ApplySerializedColorLifetimeFallback(particleSystem, particleJson);
                        }
                        else
                        {
                            var particleJson = source.ReadText(component.ParametersFile);
                            ApplyParticleSystem(particleSystem, DeserializeParticleSystemData(particleJson));
                            ApplySerializedColorLifetimeFallback(particleSystem, particleJson);
                        }
                        var serializedParticleJson = source.ReadText(component.ParametersFile);
                        ApplyParticleShapeMesh(particleSystem, component, meshes, node.Path, warnings);
                        particleSystemsByPathId[component.PathID] = particleSystem;
                        pendingParticleBindings.Add(new PendingParticleBinding(particleSystem, component, serializedParticleJson, node.Path));
                        EnsureParticleSystemDefaults(particleSystem);
                        particleSystemCount++;
                    }
                    else if (component.Type == "ParticleSystemRenderer")
                    {
                        var particleSystem = gameObject.GetComponent<ParticleSystem>() ??
                                             throw new InvalidOperationException($"Failed to create ParticleSystemRenderer on '{node.Path}'.");
                        var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                        string rendererJson = null;
                        if (!string.IsNullOrEmpty(component.ParametersFile))
                        {
                            rendererJson = source.ReadText(component.ParametersFile);
                            if (component.ParametersStatus == "external-type-tree-exported")
                            {
                                ApplyNativeSerializedData(renderer, rendererJson, node.Path, warnings);
                                ApplyParticleRendererVertexStreams(renderer, rendererJson);
                            }
                        }
                        if (component.ParticleRenderer != null)
                        {
                            renderer.enabled = component.ParticleRenderer.Enabled;
                            var materialPointers = component.ParticleRenderer.MaterialPointers ?? Array.Empty<PointerInfo>();
                            var resolvedMaterials = ResolveParticleMaterialSlots(
                                materialPointers,
                                rendererJson,
                                materials,
                                out var materialPointerSlots,
                                out var hasUnresolvedMaterials);
                            var fallbackMaterial = hasUnresolvedMaterials ? GetFallbackParticleMaterial(derivedRoot) : null;
                            // Keep the authored renderer state even when the source has no material slots.
                            // An empty material binding is source data, not a reason to mutate m_Enabled.
                            var hasResolvedMaterial = resolvedMaterials.Any(material => material != null);
                            var materialAssignments = resolvedMaterials
                                .Select((material, index) => material ??
                                    (index < materialPointerSlots.Length && materialPointerSlots[index] ? fallbackMaterial : null))
                                .ToArray();
                            renderer.sharedMaterials = materialAssignments;
                            ApplyParticleMaterialSlots(renderer, materialAssignments);
                            if (hasUnresolvedMaterials)
                                warnings.Add($"{node.Path}: one or more particle materials were unresolved; bound ExtractedParticleFallback.");
                            else if (!hasResolvedMaterial)
                                warnings.Add($"{node.Path}: ParticleSystemRenderer has no serialized material pointers; preserved as non-rendering.");
                            ApplyParticleRenderer(renderer, component.ParticleRenderer, meshes, rendererJson);
                        }
                        else
                        {
                            renderer.enabled = true;
                            renderer.sharedMaterials = new[] { GetFallbackParticleMaterial(derivedRoot) };
                            warnings.Add($"{node.Path}: ParticleSystemRenderer parameters were unavailable; bound ExtractedParticleFallback.");
                        }
                        particleRendererCount++;
                    }
                    else if (component.Type == "Animator" && component.ParametersStatus != "not-required")
                    {
                        var animator = gameObject.GetComponent<Animator>() ??
                                       throw new InvalidOperationException($"Failed to create Animator on '{node.Path}'.");
                        if (component.ParametersStatus?.StartsWith("external-type-tree-", StringComparison.Ordinal) == true &&
                            !string.IsNullOrEmpty(component.ParametersFile))
                            ApplyNativeSerializedData(animator, source.ReadText(component.ParametersFile), node.Path, warnings);
                        animatorCount++;
                    }
                    else if (component.MonoBehaviour != null &&
                             component.MonoBehaviour.ClassName is "MonoEffect" or "MonoEffectPluginFollow" &&
                             !string.IsNullOrEmpty(component.ParametersFile))
                    {
                        var proxy = gameObject.GetComponent<SrEffectRuntimeProxy>() ??
                                    gameObject.AddComponent<SrEffectRuntimeProxy>();
                        ApplyRuntimeEffectMetadata(proxy, component.MonoBehaviour.ClassName,
                            source.ReadText(component.ParametersFile));
                    }
                    else if (component.MonoBehaviour != null && component.MonoBehaviour.ClassName == "CustomAdditionalLightData")
                        warnings.Add($"{node.Path}: CustomAdditionalLightData is preserved in {component.ParametersFile}, but its SR runtime behavior is not reconstructed.");
                }
            }

            ApplyParticleSubEmitters(pendingParticleBindings, particleSystemsByPathId, warnings);
            NormalizeParticleLights(objects, warnings);

            var root = primaryObjectsByPath[nodes[0].Path];
            var outputDirectory = Path.GetDirectoryName(outputAssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(outputDirectory))
                EnsureAssetFolder(outputDirectory);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, outputAssetPath);
            UnityEngine.Object.DestroyImmediate(root);

            AssetDatabase.Refresh();
            Selection.activeObject = prefab;
            Debug.Log($"Rebuilt SR effect prefab '{outputAssetPath}': {nodes.Length} nodes, {particleSystemCount} ParticleSystems, " +
                      $"{particleRendererCount} ParticleSystemRenderers, {animatorCount} Animators, {lightCount} Lights. " +
                      $"Preserved SR extension warnings: {warnings.Count}.");
            return prefab;
        }

        private static void ApplyParticleShapeMesh(
            ParticleSystem particleSystem,
            ManifestComponent component,
            Dictionary<string, Mesh> meshes,
            string nodePath,
            List<string> warnings)
        {
            var meshReference = (component.References ?? Array.Empty<ManifestReference>())
                .FirstOrDefault(reference => string.Equals(reference.Type, "Mesh", StringComparison.Ordinal) && reference.PathID != 0);
            if (meshReference == null)
                return;

            var mesh = FindMesh(meshes, meshReference.SourceCAB, meshReference.PathID);
            if (mesh == null)
            {
                warnings.Add($"{nodePath}: ParticleSystem Shape Mesh reference {meshReference.SourceCAB}:{meshReference.PathID} was unresolved.");
                return;
            }

            var shape = particleSystem.shape;
            shape.mesh = mesh;
        }

        private static void ApplyParticleSubEmitters(
            IEnumerable<PendingParticleBinding> bindings,
            IReadOnlyDictionary<long, ParticleSystem> particleSystemsByPathId,
            List<string> warnings)
        {
            foreach (var binding in bindings)
            {
                var root = JObject.Parse(binding.Json);
                if (root["SubModule"] is not JObject subModule || subModule["subEmitters"] is not JArray emitters)
                    continue;

                var module = binding.ParticleSystem.subEmitters;
                for (var index = 0; index < emitters.Count; index++)
                {
                    if (emitters[index] is not JObject emitter || emitter["emitter"] is not JObject pointer)
                        continue;
                    var pathId = pointer.Value<long?>("m_PathID") ?? 0;
                    if (pathId == 0 || !particleSystemsByPathId.TryGetValue(pathId, out var target))
                        continue;
                    var type = (ParticleSystemSubEmitterType)Mathf.Clamp(emitter.Value<int?>("type") ?? 0, 0, 2);
                    var properties = (ParticleSystemSubEmitterProperties)Mathf.Clamp(emitter.Value<int?>("properties") ?? 0, 0, 3);
                    var probability = Mathf.Clamp01(emitter.Value<float?>("emitProbability") ?? 1f);
                    if (index >= module.subEmittersCount)
                    {
                        if (index != module.subEmittersCount)
                        {
                            warnings.Add($"{binding.NodePath}: SubEmitter slot {index} targets {pathId}, but Unity did not create the serialized slot.");
                            continue;
                        }
                        module.AddSubEmitter(target, type, properties, probability);
                        continue;
                    }

                    module.SetSubEmitterSystem(index, target);
                    module.SetSubEmitterType(index, type);
                    module.SetSubEmitterProperties(index, properties);
                    module.SetSubEmitterEmitProbability(index, probability);
                }
            }
        }

        private static void NormalizeParticleLights(
            Dictionary<string, GameObject> objects,
            List<string> warnings)
        {
            foreach (var gameObject in objects.Values)
            {
                var particleSystem = gameObject.GetComponent<ParticleSystem>();
                if (particleSystem == null)
                    continue;

                var lights = particleSystem.lights;
                if (!lights.enabled || lights.light != null)
                    continue;

                var childLight = gameObject.GetComponentsInChildren<Light>(true).FirstOrDefault();
                if (childLight != null)
                {
                    lights.light = childLight;
                    warnings.Add($"{gameObject.name}: ParticleSystem LightsModule was rebound to child Light '{childLight.name}'.");
                }
                else
                {
                    lights.enabled = false;
                    warnings.Add($"{gameObject.name}: ParticleSystem LightsModule had no Light reference; disabled to avoid invalid native state.");
                }
            }
        }

        private static void ApplyNativeSerializedData(UnityEngine.Object target, string json, string nodePath, List<string> warnings)
        {
            try
            {
                var serialized = new SerializedObject(target);
                var root = JObject.Parse(json);
                var applied = 0;
                foreach (var field in root.Properties())
                {
                    // ParticleSystemRenderer owns these fields through its public API.
                    // Writing the old TypeTree representation first and then calling
                    // SetActiveVertexStreams/enableGPUInstancing can leave native
                    // renderer state inconsistent during URP culling.
                    if (target is ParticleSystemRenderer &&
                        (field.Name == "m_UseCustomVertexStreams" ||
                         field.Name == "m_VertexStreams" ||
                         field.Name == "m_EnableGPUInstancing"))
                        continue;

                    var property = serialized.FindProperty(field.Name);
                    if (property != null)
                        applied += ApplySerializedValue(property, field.Value);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(target);
                if (applied == 0)
                    warnings.Add($"{nodePath}: native {target.GetType().Name} data contained no compatible serialized fields.");
            }
            catch (Exception exception)
            {
                warnings.Add($"{nodePath}: native {target.GetType().Name} data could not be applied: {exception.Message}");
            }
        }

        private static int ApplySerializedValue(SerializedProperty property, JToken value)
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference)
                return 0;

            if (property.isArray && property.propertyType != SerializedPropertyType.String && value is JArray array)
            {
                property.arraySize = array.Count;
                var applied = 1;
                for (var index = 0; index < array.Count; index++)
                    applied += ApplySerializedValue(property.GetArrayElementAtIndex(index), array[index]);
                return applied;
            }

            if (value is JObject objectValue)
            {
                var applied = 0;
                foreach (var field in objectValue.Properties())
                {
                    var child = property.FindPropertyRelative(field.Name);
                    if (child != null)
                        applied += ApplySerializedValue(child, field.Value);
                }
                return applied;
            }

            if (value is not JValue scalar || scalar.Value == null)
                return 0;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    property.boolValue = scalar.Value<bool>();
                    return 1;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.LayerMask:
                    property.longValue = scalar.Value<long>();
                    return 1;
                case SerializedPropertyType.Float:
                    property.doubleValue = scalar.Value<double>();
                    return 1;
                case SerializedPropertyType.String:
                    property.stringValue = scalar.Value<string>();
                    return 1;
                default:
                    return 0;
            }
        }

        private static void ApplyParticleRendererVertexStreams(ParticleSystemRenderer renderer, string json)
        {
            var root = JObject.Parse(json);
            if (root.Value<bool>("m_UseCustomVertexStreams") == false || root["m_VertexStreams"] is not JArray streams)
                return;

            renderer.SetActiveVertexStreams(streams
                .Values<int>()
                .Select(value => (ParticleSystemVertexStream)value)
                .ToList());
        }

        private static Dictionary<string, Mesh> CreateMeshes(Manifest manifest, string derivedRoot)
        {
            var result = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
            if (manifest.Meshes == null || manifest.Meshes.Length == 0)
                return result;
            var meshFolder = $"{derivedRoot}/Meshes";
            EnsureAssetFolder(meshFolder);
            foreach (var info in manifest.Meshes)
            {
                var mesh = new Mesh { name = info.Name };
                if (info.VertexCount > ushort.MaxValue)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = ToVector3Array(info.Vertices, info.VertexCount);
                if (info.Normals?.Length >= info.VertexCount * 3)
                    mesh.normals = ToVector3Array(info.Normals, info.VertexCount);
                if (info.Tangents?.Length >= info.VertexCount * 4)
                    mesh.tangents = ToVector4Array(info.Tangents, info.VertexCount);
                if (info.Colors?.Length >= info.VertexCount * 3)
                    mesh.colors = ToColorArray(info.Colors, info.VertexCount);
                if (info.UV0?.Length >= info.VertexCount * 2)
                    mesh.uv = ToVector2Array(info.UV0, info.VertexCount);
                SetAdditionalUV(mesh, 1, info.UV1, info.VertexCount);
                SetAdditionalUV(mesh, 2, info.UV2, info.VertexCount);
                SetAdditionalUV(mesh, 3, info.UV3, info.VertexCount);
                SetAdditionalUV(mesh, 4, info.UV4, info.VertexCount);
                SetAdditionalUV(mesh, 5, info.UV5, info.VertexCount);
                SetAdditionalUV(mesh, 6, info.UV6, info.VertexCount);
                SetAdditionalUV(mesh, 7, info.UV7, info.VertexCount);
                mesh.subMeshCount = info.SubMeshes?.Length ?? 0;
                for (var index = 0; index < mesh.subMeshCount; index++)
                    mesh.SetTriangles((info.SubMeshes[index].Indices ?? Array.Empty<uint>()).Select(value => (int)value).ToArray(), index, false);
                if (mesh.normals == null || mesh.normals.Length == 0)
                    mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var meshPath = $"{meshFolder}/{SanitizeFileName(info.Name)}_{info.PathID}.asset";
                AssetDatabase.CreateAsset(mesh, meshPath);
                result[MaterialKey(info.SourceCAB, info.PathID)] = mesh;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        private static void ApplyParticleRenderer(
            ParticleSystemRenderer renderer,
            ParticleRendererInfo info,
            Dictionary<string, Mesh> meshes,
            string rendererJson)
        {
            if (info.PrefixParsed)
            {
                if (info.RenderMode >= 0 && info.RenderMode <= (int)ParticleSystemRenderMode.None)
                    renderer.renderMode = (ParticleSystemRenderMode)info.RenderMode;
                if (info.SortMode >= 0 && info.SortMode <= (int)ParticleSystemSortMode.OldestInFront)
                    renderer.sortMode = (ParticleSystemSortMode)info.SortMode;
                // The SR 4.4 partial renderer prefix can contain zero-filled or
                // misaligned values. Do not overwrite Unity's safe defaults with
                // an invalid normalized size; maxParticleSize == 0 makes the
                // billboard renderer effectively invisible.
                SetNormalizedRendererValue(info.MinParticleSize, value => renderer.minParticleSize = value, allowZero: true);
                SetNormalizedRendererValue(info.MaxParticleSize, value => renderer.maxParticleSize = value, allowZero: false);
                SetFinite(info.CameraVelocityScale, value => renderer.cameraVelocityScale = value);
                SetFinite(info.VelocityScale, value => renderer.velocityScale = value);
                SetFinite(info.LengthScale, value => renderer.lengthScale = value);
                SetFinite(info.SortingFudge, value => renderer.sortingFudge = value);
                SetNormalizedRendererValue(info.NormalDirection, value => renderer.normalDirection = value, allowZero: true);
                SetFinite(info.ShadowBias, value => renderer.shadowBias = value);
                if (Enum.IsDefined(typeof(ParticleSystemRenderSpace), info.RenderAlignment))
                    renderer.alignment = (ParticleSystemRenderSpace)info.RenderAlignment;
                renderer.pivot = SanitizeRendererVector(info.Pivot.ToVector3());
                renderer.flip = SanitizeRendererVector(info.Flip.ToVector3());
                if (info.UseCustomVertexStreams && info.VertexStreams != null && info.VertexStreams.Length > 0)
                {
                    var streams = info.VertexStreams
                        .Where(value => Enum.IsDefined(typeof(ParticleSystemVertexStream), value))
                        .Select(value => (ParticleSystemVertexStream)value)
                        .ToList();
                    if (streams.Count > 0)
                        renderer.SetActiveVertexStreams(streams);
                }
                renderer.enableGPUInstancing = info.EnableGPUInstancing;
                renderer.allowRoll = info.AllowRoll;
                var serialized = new SerializedObject(renderer);
                SetBoolean(serialized, "m_ApplyActiveColorSpace", info.ApplyActiveColorSpace);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var resolvedMeshes = ResolveParticleMeshSlots(info.MeshPointers, rendererJson, meshes);
            ApplyParticleMeshSlots(renderer, resolvedMeshes);

            // Older SR4.4 exports can preserve the particle mesh pointer while
            // failing to decode the renderer prefix. Unity defaults that case
            // to Billboard, so the mesh is bound but never drawn as a mesh.
            // The presence of m_Mesh is authoritative for this fallback.
            if (!info.PrefixParsed && resolvedMeshes.FirstOrDefault() != null)
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
        }

        private static Material[] ResolveParticleMaterialSlots(
            PointerInfo[] pointers,
            string rendererJson,
            Dictionary<string, Material> materials,
            out bool[] materialPointerSlots,
            out bool hasUnresolvedMaterials)
        {
            pointers ??= Array.Empty<PointerInfo>();
            var slots = ReadSerializedPointerSlots(rendererJson, "m_Materials");
            if (slots.Count == 0)
            {
                materialPointerSlots = Enumerable.Repeat(true, pointers.Length).ToArray();
                hasUnresolvedMaterials = pointers.Any(pointer => FindMaterial(materials, pointer) == null);
                return pointers.Select(pointer => FindMaterial(materials, pointer)).ToArray();
            }

            materialPointerSlots = slots.ToArray();
            var resolved = new Material[slots.Count];
            var pointerIndex = 0;
            hasUnresolvedMaterials = false;
            foreach (var slot in slots.Select((hasPointer, index) => (hasPointer, index)))
            {
                if (!slot.hasPointer)
                    continue;
                var material = pointerIndex < pointers.Length
                    ? FindMaterial(materials, pointers[pointerIndex])
                    : null;
                resolved[slot.index] = material;
                if (material == null)
                    hasUnresolvedMaterials = true;
                pointerIndex++;
            }

            if (pointerIndex < pointers.Length)
                hasUnresolvedMaterials = true;
            return resolved;
        }

        private static void ApplyParticleMaterialSlots(
            ParticleSystemRenderer renderer,
            IReadOnlyList<Material> materials)
        {
            var serialized = new SerializedObject(renderer);
            var property = serialized.FindProperty("m_Materials");
            if (property == null || !property.isArray)
                return;

            property.arraySize = materials.Count;
            for (var index = 0; index < materials.Count; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = materials[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Mesh[] ResolveParticleMeshSlots(
            PointerInfo[] pointers,
            string rendererJson,
            Dictionary<string, Mesh> meshes)
        {
            pointers ??= Array.Empty<PointerInfo>();
            var slots = ReadSerializedPointerSlots(rendererJson, "m_Mesh", "m_Mesh1", "m_Mesh2", "m_Mesh3");
            if (slots.Count == 0)
                return pointers.Select(pointer => FindMesh(meshes, pointer)).ToArray();

            var resolved = new Mesh[slots.Count];
            var pointerIndex = 0;
            foreach (var slot in slots.Select((hasPointer, index) => (hasPointer, index)))
            {
                if (!slot.hasPointer)
                    continue;
                if (pointerIndex < pointers.Length)
                    resolved[slot.index] = FindMesh(meshes, pointers[pointerIndex]);
                pointerIndex++;
            }
            return resolved;
        }

        private static List<bool> ReadSerializedPointerSlots(string json, params string[] propertyNames)
        {
            if (string.IsNullOrEmpty(json))
                return new List<bool>();
            var data = JObject.Parse(json);
            if (propertyNames.Length == 1 && data[propertyNames[0]] is JArray array)
                return array.Select(token => ReadSerializedPointerPathId(token) != 0).ToList();

            return propertyNames
                .Select(name => ReadSerializedPointerPathId(data[name]) != 0)
                .ToList();
        }

        private static long ReadSerializedPointerPathId(JToken token) =>
            token is JObject value ? value.Value<long?>("m_PathID") ?? 0 : 0;

        private static void ApplyParticleMeshSlots(ParticleSystemRenderer renderer, IReadOnlyList<Mesh> meshes)
        {
            renderer.SetMeshes(meshes.Take(4).ToArray());
        }

        private static Mesh FindMesh(Dictionary<string, Mesh> meshes, PointerInfo pointer)
        {
            if (pointer == null || pointer.PathID == 0)
                return null;
            return FindMesh(meshes, pointer.SourceCAB, pointer.PathID);
        }

        private static Mesh FindMesh(Dictionary<string, Mesh> meshes, string sourceCab, long pathId)
        {
            if (pathId == 0)
                return null;
            if (meshes.TryGetValue(MaterialKey(sourceCab, pathId), out var exact))
                return exact;
            return meshes.FirstOrDefault(pair => pair.Key.EndsWith($":{pathId}", StringComparison.Ordinal)).Value;
        }

        private static Vector3[] ToVector3Array(float[] values, int count)
        {
            var stride = values.Length / count;
            return Enumerable.Range(0, count).Select(index => new Vector3(values[index * stride], values[index * stride + 1], values[index * stride + 2])).ToArray();
        }

        private static Vector4[] ToVector4Array(float[] values, int count) => Enumerable.Range(0, count)
            .Select(index => new Vector4(values[index * 4], values[index * 4 + 1], values[index * 4 + 2], values[index * 4 + 3])).ToArray();

        private static Vector2[] ToVector2Array(float[] values, int count)
        {
            var stride = values.Length / count;
            return Enumerable.Range(0, count).Select(index => new Vector2(values[index * stride], values[index * stride + 1])).ToArray();
        }

        private static void SetAdditionalUV(Mesh mesh, int channel, float[] values, int count)
        {
            if (values == null || count <= 0 || values.Length < count * 2)
                return;
            var stride = values.Length / count;
            var data = Enumerable.Range(0, count)
                .Select(index =>
                {
                    var offset = index * stride;
                    return new Vector4(
                        values[offset],
                        values[offset + 1],
                        stride > 2 ? values[offset + 2] : 0f,
                        stride > 3 ? values[offset + 3] : 0f);
                })
                .ToList();
            mesh.SetUVs(channel, data);
        }

        private static Color[] ToColorArray(float[] values, int count)
        {
            var stride = values.Length / count;
            return Enumerable.Range(0, count).Select(index => new Color(values[index * stride], values[index * stride + 1], values[index * stride + 2], stride > 3 ? values[index * stride + 3] : 1f)).ToArray();
        }

        private static Dictionary<string, Material> CreateMaterials(ImportSource source, Manifest manifest, string outputAssetPath, List<string> warnings)
        {
            var result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            if (manifest.Materials == null || manifest.Materials.Length == 0)
                return result;

            var root = GetDerivedRoot(outputAssetPath, manifest.Name);
            var shaderFolder = $"{root}/Shaders";
            var textureFolder = $"{root}/Textures";
            var materialFolder = $"{root}/Materials";
            EnsureAssetFolder(shaderFolder);
            EnsureAssetFolder(textureFolder);
            EnsureAssetFolder(materialFolder);

            // A texture can be shared by several material slots. Resolve its import
            // color-space policy before importing it, instead of letting the first
            // slot encountered decide. Main textures are color data; mask/noise/
            // dissolve slots are scalar data unless the same texture is also used
            // as a main texture.
            var textureUsesColorSpace = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in manifest.Materials)
            {
                foreach (var property in info.Textures ?? Array.Empty<TextureProperty>())
                {
                    if (string.IsNullOrEmpty(property.PackageEntry))
                        continue;
                    var isColorTexture = string.Equals(property.Name, "_MainTex", StringComparison.OrdinalIgnoreCase);
                    if (textureUsesColorSpace.TryGetValue(property.PackageEntry, out var existing))
                        textureUsesColorSpace[property.PackageEntry] = existing || isColorTexture;
                    else
                        textureUsesColorSpace[property.PackageEntry] = isColorTexture;
                }
            }

            var shaderByFamily = new Dictionary<ShaderFamily, Shader>();
            foreach (var family in manifest.Materials.Select(ClassifyShaderFamily).Distinct())
            {
                var shaderPath = $"{shaderFolder}/SR_{family}.shader";
                File.WriteAllText(ToAbsolutePath(shaderPath), BuildShaderSource(family));
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader == null)
                    throw new InvalidOperationException($"Failed to create reconstructed SR shader family '{family}'.");
                shaderByFamily[family] = shader;
            }

            var textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in manifest.Materials)
            {
                var family = ClassifyShaderFamily(info);
                var material = new Material(shaderByFamily[family]) { name = info.Name, enableInstancing = info.EnableInstancing };
                if (info.RenderQueue >= 0)
                    material.renderQueue = info.RenderQueue;
                foreach (var property in info.Floats ?? Array.Empty<FloatProperty>())
                    if (material.HasProperty(property.Name))
                        material.SetFloat(property.Name, property.Value);
                foreach (var property in info.Colors ?? Array.Empty<ColorProperty>())
                    if (material.HasProperty(property.Name))
                        material.SetColor(property.Name, property.Value.ToColor());
                foreach (var property in info.Textures ?? Array.Empty<TextureProperty>())
                {
                    if (string.IsNullOrEmpty(property.PackageEntry))
                        continue;
                    var targetProperty = property.Name;
                    if (string.Equals(property.Name, "_DissolveMap", StringComparison.OrdinalIgnoreCase))
                        targetProperty = "_DisTex";
                    if (!material.HasProperty(targetProperty))
                        continue;
                    if (!textures.TryGetValue(property.PackageEntry, out var texture))
                    {
                        var texturePath = $"{textureFolder}/{SanitizeFileName(Path.GetFileName(property.PackageEntry))}";
                        File.WriteAllBytes(ToAbsolutePath(texturePath), source.ReadBytes(property.PackageEntry));
                        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                        if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer)
                        {
                            importer.sRGBTexture = textureUsesColorSpace.TryGetValue(property.PackageEntry, out var useColorSpace) && useColorSpace;
                            importer.wrapMode = TextureWrapMode.Repeat;
                            importer.filterMode = FilterMode.Bilinear;
                            importer.mipmapEnabled = true;
                            importer.SaveAndReimport();
                        }
                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                        textures[property.PackageEntry] = texture;
                    }
                    material.SetTexture(targetProperty, texture);
                    material.SetTextureScale(targetProperty, property.Scale.ToVector2());
                    material.SetTextureOffset(targetProperty, property.Offset.ToVector2());
                }
                // DecalClip falls back to the main packed channel when the
                // source material has no mask slot. Sampling Unity's default
                // white texture would make every missing-mask quad pass clip.
                if (material.HasProperty("_DecalHasMask"))
                    material.SetFloat("_DecalHasMask", HasTexture(info, "_MaskTex") ? 1f : 0f);
                // The Cerydra chess draw (EIDs 6026/6028/6030/6032/6034) uses
                // the original TEXCOORD0.xy for the main texture. Its vertex
                // shader derives the mask coordinates separately from particle
                // custom data, so do not reinterpret TEXCOORD1.xy as main UV.
                if (material.HasProperty("_PackedUV"))
                    material.SetFloat("_PackedUV", UsesPackedChessUV(info) ? 1f : 0f);
                if (material.HasProperty("_RdcColorChannelMode"))
                    material.SetFloat("_RdcColorChannelMode", UsesPackedChessUV(info) ? 1f : 0f);
                ApplySourceKeywords(material, info, warnings);
                var materialPath = $"{materialFolder}/{SanitizeFileName(info.Name)}_{info.PathID}.mat";
                AssetDatabase.CreateAsset(material, materialPath);
                result[MaterialKey(info.SourceCAB, info.PathID)] = material;
                if (string.IsNullOrEmpty(info.ShaderName))
                    warnings.Add($"{info.Name}: original shader name unresolved ({info.ShaderSourceCAB}:{info.ShaderPathID}); reconstructed shader applied.");
                if (HasTexture(info, "_ParallaxTex") || HasFloat(info, "_OutlineWidth") ||
                    ContainsKeyword(info, "_OUTLINENORMALFROM_TANGENT") || ContainsKeyword(info, "_USE_PARALLAX_MAP"))
                    warnings.Add($"{info.Name}: surface/parallax/outline terms are preserved as material evidence but not reconstructed; provide a matching RDC draw before implementing this family.");
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        private enum ShaderFamily
        {
            Generic,
            OneChannel,
            UvMove,
            DissolveSwirl,
            Decal,
            Distortion,
            Fresnel
        }

        private static ShaderFamily ClassifyShaderFamily(ManifestMaterial info)
        {
            if (info == null)
                return ShaderFamily.Generic;

            // Distortion materials do not carry a normal _MainTex. Their
            // source shader samples a dedicated distortion map, so treating
            // them as a regular particle shader renders the default white
            // texture as opaque geometry.
            if (HasTexture(info, "_DistortionTex") || HasFloat(info, "_Distortion"))
                return ShaderFamily.Distortion;

            // Decal properties own the clip/mask path. Keep this ahead of the
            // UV and CL checks because decal materials also carry those flags.
            if (HasFloat(info, "_DecalClip") || HasFloat(info, "_DECALMASK") ||
                HasFloat(info, "_DECALNOISE") || HasFloat(info, "_TurnOnAnnularUV") ||
                HasFloat(info, "_IsPerParticle"))
                return ShaderFamily.Decal;

            if (HasTexture(info, "_DisTex") || HasFloat(info, "_DisTexG") ||
                HasFloat(info, "_Mid") || HasFloat(info, "_EnableClip") ||
                HasVector(info, "_DisGSpeed") || HasVector(info, "_DisRSpeed") ||
                HasColor(info, "_InsideColor") || HasColor(info, "_OutSideColor"))
                return ShaderFamily.DissolveSwirl;

            // Keep surface-style materials out of Generic. These fields are
            // evidence that the material needs an edge/surface term.
            var hasSurfaceTerms = HasTexture(info, "_ParallaxTex") || HasFloat(info, "_OutlineWidth") ||
                                  ContainsKeyword(info, "_OUTLINENORMALFROM_TANGENT") ||
                                  ContainsKeyword(info, "_USE_PARALLAX_MAP");
            if (!hasSurfaceTerms &&
                (HasColor(info, "_Fresnel") || HasColor(info, "_FresnelColor") ||
                 HasFloat(info, "_FresnelColorStrength") || ContainsKeyword(info, "_FresnelON")))
                return ShaderFamily.Fresnel;

            // A material can animate its UVs and still be a OneChannel
            // material. The explicit family in the authored name is stronger
            // evidence than the presence of a speed vector; otherwise packed
            // custom-stream particles are sent through the UvMove layout.
            if (HasFloat(info, "_CL") || HasVector(info, "_MainChannel") ||
                (info.Name?.IndexOf("OneChannel", StringComparison.OrdinalIgnoreCase) >= 0))
                return ShaderFamily.OneChannel;

            if (HasVector(info, "_MainSpeed") || HasVector(info, "_MaskSpeed") ||
                HasVector(info, "_NoiseSpeed") || HasVector(info, "_CustomUV") ||
                (info.Name?.IndexOf("UVMove", StringComparison.OrdinalIgnoreCase) >= 0))
                return ShaderFamily.UvMove;

            return ShaderFamily.Generic;
        }

        private static bool HasFloat(ManifestMaterial info, string name)
        {
            return (info.Floats ?? Array.Empty<FloatProperty>())
                .Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                 Math.Abs(property.Value) > 0.000001f);
        }

        private static bool HasVector(ManifestMaterial info, string name)
        {
            return (info.Colors ?? Array.Empty<ColorProperty>())
                .Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                 (Math.Abs(property.Value.r) > 0.000001f ||
                                  Math.Abs(property.Value.g) > 0.000001f ||
                                  Math.Abs(property.Value.b) > 0.000001f ||
                                  Math.Abs(property.Value.a) > 0.000001f));
        }

        private static bool HasColor(ManifestMaterial info, string name)
        {
            return HasVector(info, name);
        }

        private static bool HasTexture(ManifestMaterial info, string name)
        {
            return (info.Textures ?? Array.Empty<TextureProperty>())
                .Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                 !string.IsNullOrEmpty(property.PackageEntry));
        }

        private static bool UsesPackedChessUV(ManifestMaterial info)
        {
            return info != null &&
                !string.IsNullOrEmpty(info.Name) &&
                info.Name.IndexOf("Mz_DissolveM_22", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildShaderSource(ShaderFamily family)
        {
            var source = ReconstructedParticleShader.Replace(
                "Shader \"SR/Reconstructed Particle\"",
                $"Shader \"SR/Reconstructed Particle/{family}\"");

            if (family == ShaderFamily.OneChannel)
            {
                // _MainChannel and _MainChannelRGB are channel selectors, not output
                // color masks. The RDC OneChannel variant uses R-only selectors for
                // omitted properties, including the CL=2 path.
                source = source.Replace(
                    "_MainChannel (\"Main Channel\", Vector) = (1,0,0,0)",
                    "_MainChannel (\"Main Channel\", Vector) = (1,0,0,0)");
                source = source.Replace(
                    "_MainChannelRGB (\"Main Channel RGB\", Vector) = (1,1,1,0)",
                    "_MainChannelRGB (\"Main Channel RGB\", Vector) = (1,0,0,0)");
            }

            if (family == ShaderFamily.DissolveSwirl)
            {
                // The dissolve family carries a two-point transition in
                // _SmoothStep. The shared shader only used .x, which made the
                // second edge value inert and produced a hard clip.
                source = source.Replace(
                    "if (_EnableClip > 0.5) clip(saturate(dissolve + noise * _SmoothStep.x) - _Mid);",
                    "float dissolveControl = dissolve + noise * _SmoothStep.x;\n                float dissolveEdge = smoothstep(_SmoothStep.x, max(_SmoothStep.y, _SmoothStep.x + 0.0001), dissolveControl);\n                if (_EnableClip > 0.5) clip(dissolveEdge - _Mid);");
            }

            if (family == ShaderFamily.Decal)
            {
                // Decal variants have two independent source switches. The
                // decal clip trims the mask carrier, while fragment clip uses
                // the dissolve/noise map. They must not be collapsed into one
                // mask test or packed textures turn into opaque rectangles.
                source = source.Replace(
                    "if (_EnableClip > 0.5) clip(saturate(dissolve + noise * _SmoothStep.x) - _Mid);",
                    "float decalMaskValue = dot(maskSample, _MaskChannel);\n                float decalClipValue = lerp(mainChannel, decalMaskValue, step(0.5, _DecalHasMask));\n#if defined(_ENABLE_DECAL_CLIP)\n                if (_DecalClip > 0.0)\n                    clip(decalClipValue - _DecalClip);\n#else\n                if (_EnableClip > 0.5)\n                    clip(decalClipValue - _DecalClip);\n#endif\n#if defined(_FRAGMENT_CLIP)\n                if (_FragmentClip > 0.5)\n                    clip(saturate(dissolve + noise * _SmoothStep.x) - _Mid);\n#endif");
            }

            if (family == ShaderFamily.Distortion)
            {
                // URP cannot reproduce SR's screen-color grab path in this
                // plain particle shader. Sample the source distortion map and
                // keep its carrier transparent rather than drawing the
                // default white _MainTex as a solid mesh.
                source = source.Replace(
                    "fixed4 mainSample = tex2D(_MainTex, mainUv);",
                    "fixed4 mainSample = tex2D(_MainTex, mainUv);\n                fixed4 distortionSample = tex2D(_DistortionTex, mainUv);\n#if defined(DISTORTION)\n                mainSample = fixed4(distortionSample.rgb, saturate(length(distortionSample.rg - 0.5) * 0.35));\n#endif");
                source = source.Replace(
                    "float4 _MainTex_ST, _MaskTex_ST, _NoiseTex_ST, _DisTex_ST;",
                    "float4 _MainTex_ST, _MaskTex_ST, _NoiseTex_ST, _DisTex_ST, _DistortionTex_ST;");
            }

            if (family == ShaderFamily.Fresnel)
            {
                // This is a material-family reconstruction, not the original
                // game shader. It only adds the edge term evidenced by the
                // extracted Fresnel fields and the RDC sample.
                source = source.Replace(
                    "color.a *= _Opacity;",
                    "color.a *= _Opacity;\n                float3 fresnelNormal = normalize(input.worldNormal);\n                float3 fresnelView = normalize(_WorldSpaceCameraPos - input.worldPosition);\n                float fresnelTerm = pow(1.0 - saturate(abs(dot(fresnelNormal, fresnelView))), 2.0);\n                float fresnelStrength = max(_FresnelColorStrength, _FresnelColor.a);\n                color.rgb += _FresnelColor.rgb * fresnelTerm * max(fresnelStrength, 0.0);");
            }
            else
            {
                // Cerydra dissolve materials enable the same Fresnel path by
                // keyword even though their family is not classified as Fresnel.
                source = source.Replace(
                    "color.a *= _Opacity;",
                    "color.a *= _Opacity;\n#if defined(_FresnelON)\n                float3 fresnelNormal = normalize(input.worldNormal);\n                float3 fresnelView = normalize(_WorldSpaceCameraPos - input.worldPosition);\n                float fresnelTerm = pow(1.0 - saturate(abs(dot(fresnelNormal, fresnelView))), 2.0);\n                float fresnelStrength = max(_FresnelColorStrength, _FresnelColor.a);\n                color.rgb += _FresnelColor.rgb * fresnelTerm * max(fresnelStrength, 0.0);\n#endif");
            }

            source = HideSharedInspectorProperties(source, family);
            if (family == ShaderFamily.UvMove)
                source = HideUvMoveInspectorProperties(source);

            return source;
        }

        private static string HideUvMoveInspectorProperties(string source)
        {
            // UvMove only consumes the UV, channel, mask, dissolve and blend
            // properties used by its fragment path. Keep the other shared
            // fields serialized for compatibility, but remove them from the
            // Inspector so they are not mistaken for active controls.
            var hiddenProperties = new[]
            {
                "_DissolveMap", "_DistortionTex", "_Fresnel", "_Fresnel2",
                "_FresnelColor2", "_ParallaxScale", "_OutlineWidth",
                "_InsideColor", "_OutSideColor", "_AlwaysOnTop", "_CL2", "_CL3",
                "_EffectUVSet", "_MainTexUvSet", "_ScrPosScale", "_ScrPosTex",
                "_ScrPosXYOffset", "_CustomData", "_CustomDstBlend", "_CustomSrcBlend",
                "_DecalClip", "_DECALNOISE", "_DissolveDistortionIntensity",
                "_DissolveOutlineOffset", "_DissolveOutlineSize1", "_DissolveOutlineSize2",
                "_DissolveType", "_FragmentClip", "_RenderingMode", "_SoftFar",
                "_Stencil", "_StencilComp", "_TurnOnAnnularUV", "_NoiseSpeed2",
                "_NoiseSpeedG", "_DisStep", "_CustomUV", "_DissolveOutlineColor1",
                "_DissolveOutlineColor2", "_DissolveOutlineSmoothStep", "_MidColor"
            };

            foreach (var property in hiddenProperties)
                source = source.Replace($"        {property} (", $"        [HideInInspector] {property} (");
            return source;
        }

        private static string HideSharedInspectorProperties(string source, ShaderFamily family)
        {
            // These fields are retained in the generated shader for material
            // compatibility, but the reconstructed fragment paths do not
            // consume them. Hide them for every shader family instead of
            // exposing a misleading superset of controls.
            var hiddenProperties = new HashSet<string>(StringComparer.Ordinal)
            {
                "_DissolveMap", "_Fresnel", "_Fresnel2", "_FresnelColor2",
                "_ParallaxScale", "_OutlineWidth", "_InsideColor", "_OutSideColor",
                "_AlwaysOnTop", "_CL2", "_CL3", "_EffectUVSet", "_MainTexUvSet",
                "_ScrPosScale", "_ScrPosTex", "_ScrPosXYOffset", "_CustomData",
                "_CustomDstBlend", "_CustomSrcBlend", "_RenderingMode", "_Stencil",
                "_StencilComp", "_TurnOnAnnularUV", "_NoiseSpeed2", "_NoiseSpeedG",
                "_DisStep", "_CustomUV", "_DissolveDistortionIntensity",
                "_DissolveOutlineOffset", "_DissolveOutlineSize1", "_DissolveOutlineSize2",
                "_DissolveType", "_DissolveOutlineColor1", "_DissolveOutlineColor2",
                "_DissolveOutlineSmoothStep", "_MidColor"
            };

            if (family != ShaderFamily.Distortion)
                hiddenProperties.Add("_DistortionTex");

            foreach (var property in hiddenProperties)
                source = source.Replace($"        {property} (", $"        [HideInInspector] {property} (");
            return source;
        }

        private static bool ContainsKeyword(ManifestMaterial info, string keyword)
        {
            return SplitKeywords(info?.ShaderKeywords)
                .Any(value => string.Equals(value, keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplySourceKeywords(Material material, ManifestMaterial info, List<string> warnings)
        {
            var sourceKeywords = SplitKeywords(info?.ShaderKeywords);
            // Keep the source variant selection on the reconstructed material.
            // Unknown keywords remain visible as invalid Unity keywords instead
            // of being silently discarded during export.
            material.shaderKeywords = Array.Empty<string>();
            foreach (var keyword in sourceKeywords)
            {
                material.EnableKeyword(keyword);
                if (!IsReconstructedKeyword(keyword))
                    warnings.Add($"{info.Name}: source shader keyword '{keyword}' is preserved but not reconstructed by family '{ClassifyShaderFamily(info)}'.");
            }
            ApplyGeneratedFeatureKeywords(material, info);
        }

        private static void ApplyGeneratedFeatureKeywords(Material material, ManifestMaterial info)
        {
            if (material == null || info == null)
                return;

            var maskChannel = ReadFloat(info, "_MASKCHANEL");
            var maskEnabled = ReadFloat(info, "_MaskON") > 0.5f ||
                              maskChannel > 0.5f ||
                              ReadFloat(info, "_DECALMASK") > 0.5f;

            // These keywords are generated from serialized source values. The
            // shader can therefore compile out boolean paths instead of
            // evaluating the same feature for every fragment.
            SetGeneratedKeyword(material, "SR_MASK_R", maskChannel > 0.5f && maskChannel <= 1.5f);
            SetGeneratedKeyword(material, "SR_MASK_RG", maskChannel > 1.5f);
            SetGeneratedKeyword(material, "SR_MASK_ON", maskEnabled);
            SetGeneratedKeyword(material, "SR_NOISE_ON", ReadFloat(info, "_NoiseSwitch") > 0.5f);
            SetGeneratedKeyword(material, "SR_DISSOLVE_G", ReadFloat(info, "_DisTexG") > 0.5f);
            SetGeneratedKeyword(material, "SR_PER_PARTICLE", ReadFloat(info, "_IsPerParticle") > 0.5f);
            SetGeneratedKeyword(material, "SR_IGNORE_MAIN_ALPHA", ReadFloat(info, "_IgnoreMainTexAlpha") > 0.5f);
            // Do not assume particle vertex color is enabled.  SR materials
            // that omit this property use the shader's fallback color; forcing
            // the keyword makes zero/default particle colors black out effects.
            SetGeneratedKeyword(material, "SR_VERTEX_COLOR", ReadFloat(info, "_VertexColor") > 0.5f);
            SetGeneratedKeyword(material, "SR_SATURATE", ReadFloat(info, "_Saturate") > 0.5f);
            SetGeneratedKeyword(material, "SR_CLIP", ReadFloat(info, "_EnableClip") > 0.5f);
            // SR can serialize a depth-alpha control without the legacy
            // _SoftParticle keyword. Preserve that signal for depth fade.
            SetGeneratedKeyword(material, "SR_SOFT_DEPTH",
                ReadFloat(info, "_AdditionalDepthAlpha") > 0.0001f ||
                ReadFloat(info, "_SoftFar") > ReadFloat(info, "_SoftNear"));
        }

        private static void SetGeneratedKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        private static float ReadFloat(ManifestMaterial info, string name, float fallback = 0f)
        {
            return (info?.Floats ?? Array.Empty<FloatProperty>())
                .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;
        }

        private static bool IsReconstructedKeyword(string keyword) => keyword switch
        {
            "_CL2_X" or "_CL2_RG" or "_CL2_RGB" or
            "_CL3_OFF" or "_CL3_TEX" or "_CL3_VER" or
            "_FresnelON" or "_DECALNOISE_RG" or "_SoftParticle" or
            "_ENABLE_DECAL_CLIP" or "_FRAGMENT_CLIP" or
            "SR_MASK_R" or "SR_MASK_RG" or "SR_MASK_ON" or "SR_CL4" or
            "SR_NOISE_ON" or "SR_DISSOLVE_G" or "SR_PER_PARTICLE" or
             "SR_IGNORE_MAIN_ALPHA" or "SR_VERTEX_COLOR" or "SR_SATURATE" or
             "SR_CLIP" or "SR_SOFT_DEPTH" => true,
            _ => false
        };

        private static Material FindMaterial(Dictionary<string, Material> materials, PointerInfo pointer)
        {
            if (pointer == null || pointer.PathID == 0)
                return null;
            if (materials.TryGetValue(MaterialKey(pointer.SourceCAB, pointer.PathID), out var exact))
                return exact;
            return materials.FirstOrDefault(pair => pair.Key.EndsWith($":{pointer.PathID}", StringComparison.Ordinal)).Value;
        }

        private static string MaterialKey(string cab, long pathId)
        {
            var separator = cab?.IndexOf('.') ?? -1;
            return $"{(separator < 0 ? cab : cab.Substring(0, separator))}:{pathId}";
        }

        private static string GetDerivedRoot(string outputAssetPath, string prefabName)
        {
            // All derived assets belong beside the target prefab. The directory
            // may contain a duplicate suffix such as "__2", so deriving it from
            // the manifest name would create a second, incorrect nesting level.
            return Path.GetDirectoryName(outputAssetPath)?.Replace('\\', '/') ?? string.Empty;
        }

        private static void ClearGeneratedAssetFolders(string prefabRoot)
        {
            if (!AssetDatabase.IsValidFolder(prefabRoot))
                return;

            foreach (var folderName in new[] { "Meshes", "Materials", "Textures", "Shaders" })
            {
                var folder = $"{prefabRoot}/{folderName}";
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
            }
        }

        private static string[] SplitKeywords(string value) => (value ?? string.Empty)
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private static Type[] GetNativeComponentTypes(ManifestNode node)
        {
            var componentTypes = new List<Type>();
            var components = node.Components ?? Array.Empty<ManifestComponent>();
            if (components.Any(component => component.Type == "MeshFilter"))
                componentTypes.Add(typeof(MeshFilter));
            if (components.Any(component => component.Type == "MeshRenderer"))
                componentTypes.Add(typeof(MeshRenderer));
            if (components.Any(component => component.Type == "ParticleSystem" || component.Type == "ParticleSystemRenderer"))
                componentTypes.Add(typeof(ParticleSystem));
            if (components.Any(component => component.Type == "Animator" && component.ParametersStatus != "not-required"))
                componentTypes.Add(typeof(Animator));
            if (components.Any(component => component.MonoBehaviour != null &&
                component.MonoBehaviour.ClassName is "MonoEffect" or "MonoEffectPluginFollow"))
                componentTypes.Add(typeof(SrEffectRuntimeProxy));
            if (components.Any(component => component.Type == "Light"))
                componentTypes.Add(typeof(Light));
            return componentTypes.ToArray();
        }

        private static void ApplyRuntimeEffectMetadata(
            SrEffectRuntimeProxy proxy,
            string className,
            string json)
        {
            var data = JObject.Parse(json);
            if (className == "MonoEffect")
            {
                proxy.SetMonoEffect(
                    ReadFloat(data, "AdaptScale", 1f),
                    ReadFloat(data, "Delay"),
                    ReadVector3(data, "LocalOffset"),
                    ReadVector3(data, "LocalRotationOffset"),
                    ReadVector3(data, "LocalScale"),
                    ReadFloat(data, "MaxLifeTime", -1f),
                    data.Value<string>("AttachPoint") ?? string.Empty);
            }
            else if (className == "MonoEffectPluginFollow")
            {
                proxy.SetFollowPlugin(
                    ReadInt(data, "PositionOption"),
                    ReadInt(data, "RotationOption"),
                    ReadInt(data, "ScaleOption"),
                    ReadVector3(data, "BaseFollowScale", Vector3.one),
                    ReadVector3(data, "MaxFollowScale", new Vector3(-1f, -1f, -1f)));
            }
        }

        private static Vector3 ReadVector3(JObject data, string name, Vector3 fallback = default)
        {
            var value = data[name] as JObject;
            return value == null
                ? fallback
                : new Vector3(
                    value.Value<float?>("X") ?? fallback.x,
                    value.Value<float?>("Y") ?? fallback.y,
                    value.Value<float?>("Z") ?? fallback.z);
        }

        private static Vector2 ReadVector2(JObject data, string name, Vector2 fallback = default)
        {
            var value = data[name] as JObject;
            return value == null
                ? fallback
                : new Vector2(
                    value.Value<float?>("x") ?? value.Value<float?>("X") ?? fallback.x,
                    value.Value<float?>("y") ?? value.Value<float?>("Y") ?? fallback.y);
        }

        private static float ReadFloat(JObject data, string name, float fallback = 0f) =>
            data.Value<float?>(name) ?? fallback;

        private static int ReadInt(JObject data, string name, int fallback = 0) =>
            data.Value<int?>(name) ?? fallback;

        private static void ApplyMeshRenderer(
            MeshRenderer renderer,
            string json,
            Dictionary<string, Material> materials,
            string derivedRoot)
        {
            var data = JObject.Parse(json);
            renderer.enabled = data.Value<bool?>("m_Enabled") ?? true;

            var resolved = (data["m_Materials"] as JArray ?? new JArray())
                .Select(ReadPointer)
                .Select(pointer => FindMaterial(materials, pointer))
                .Where(material => material != null)
                .ToArray();

            renderer.sharedMaterials = resolved.Length > 0
                ? resolved
                : new[] { GetFallbackMaterial(derivedRoot) };
            if (resolved.Length == 0)
                Debug.LogWarning($"SR mesh renderer has no serialized material pointer; bound ExtractedMeshFallback on '{renderer.name}'.");
        }

        private static PointerInfo ReadPointer(JToken token)
        {
            if (token is not JObject value)
                return null;
            return new PointerInfo
            {
                PathID = value.Value<long>("m_PathID"),
                SourceCAB = value.Value<string>("SourceCAB")
            };
        }

        private static Material GetFallbackMaterial(string derivedRoot)
        {
            var folder = $"{derivedRoot}/Materials";
            EnsureAssetFolder(folder);
            const string fileName = "ExtractedMeshFallback.mat";
            var path = $"{folder}/{fileName}";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "ExtractedMeshFallback" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material GetFallbackParticleMaterial(string derivedRoot)
        {
            var folder = $"{derivedRoot}/Materials";
            EnsureAssetFolder(folder);
            const string fileName = "ExtractedParticleFallback.mat";
            var path = $"{folder}/{fileName}";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                         Shader.Find("Particles/Standard Unlit") ??
                         Shader.Find("Unlit/Transparent") ??
                         Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                throw new InvalidOperationException("No particle fallback shader is available in the current Unity project.");

            material = new Material(shader) { name = "ExtractedParticleFallback" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static bool HasVisualComponents(Manifest manifest)
        {
            return (manifest.Nodes ?? Array.Empty<ManifestNode>())
                .SelectMany(node => node.Components ?? Array.Empty<ManifestComponent>())
                .Any(component => component.Type is "MeshFilter" or "MeshRenderer" or "SkinnedMeshRenderer" or
                    "ParticleSystem" or "ParticleSystemRenderer" or "Light");
        }

        private static void ApplyTransform(Transform transform, TransformData data)
        {
            transform.localPosition = data.LocalPosition.ToVector3();
            transform.localRotation = data.LocalRotation.ToQuaternion();
            var scale = data.LocalScale.ToVector3();
            if (!IsFinite(scale) ||
                (Mathf.Abs(scale.x) <= 0.000001f &&
                 Mathf.Abs(scale.y) <= 0.000001f &&
                 Mathf.Abs(scale.z) <= 0.000001f))
                scale = Vector3.one;
            transform.localScale = scale;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static void ApplyLight(Light light, LightData data)
        {
            light.enabled = data.Enabled;
            if (data.Type >= (int)LightType.Spot && data.Type <= (int)LightType.Disc)
                light.type = (LightType)data.Type;
            if (IsFinite(data.Color))
                light.color = new Color(data.Color.r, data.Color.g, data.Color.b, data.Color.a);
            SetFinite(data.Intensity, value => light.intensity = value);
            SetFinite(data.Range, value => light.range = value);
            SetFinite(data.SpotAngle, value => light.spotAngle = value);
            SetFinite(data.InnerSpotAngle, value => light.innerSpotAngle = value);
            SetFinite(data.CookieSize, value => light.cookieSize = value);

            if (data.Shadows != null)
            {
                if (data.Shadows.Type >= (int)LightShadows.None && data.Shadows.Type <= (int)LightShadows.Soft)
                    light.shadows = (LightShadows)data.Shadows.Type;
                SetFinite(data.Shadows.Strength, value => light.shadowStrength = value);
                SetFinite(data.Shadows.Bias, value => light.shadowBias = value);
                SetFinite(data.Shadows.NormalBias, value => light.shadowNormalBias = value);
                SetFinite(data.Shadows.NearPlane, value => light.shadowNearPlane = value);
                if (data.Shadows.CustomResolution > 0)
                    light.shadowCustomResolution = data.Shadows.CustomResolution;
            }
        }

        private static bool IsFinite(ColorData value) =>
            IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) && IsFinite(value.a);

        private static void ApplyParticleSystem(ParticleSystem particleSystem, ParticleSystemData data)
        {
            var serialized = new SerializedObject(particleSystem);
            SetFloat(serialized, "lengthInSec", Math.Max(0.05f, data.LengthInSec));
            SetFloat(serialized, "simulationSpeed", data.SimulationSpeed);
            SetInteger(serialized, "stopAction", data.StopAction);
            SetInteger(serialized, "cullingMode", data.CullingMode);
            SetInteger(serialized, "ringBufferMode", data.RingBufferMode);
            SetVector2(serialized, "ringBufferLoopRange", data.RingBufferLoopRange.ToVector2());
            SetBoolean(serialized, "looping", data.Looping);
            SetBoolean(serialized, "prewarm", data.Prewarm && data.Looping);
            SetBoolean(serialized, "playOnAwake", data.PlayOnAwake);
            SetBoolean(serialized, "useUnscaledTime", data.UseUnscaledTime);
            SetBoolean(serialized, "autoRandomSeed", data.AutoRandomSeed);
            SetBoolean(serialized, "useRigidbodyForVelocity", data.UseRigidbodyForVelocity);
            SetMinMaxCurve(serialized, "startDelay", data.StartDelay);
            if (data.InitialModule != null)
            {
                SetMinMaxCurve(serialized, "InitialModule.startLifetime", data.InitialModule.StartLifetime);
                SetMinMaxCurve(serialized, "InitialModule.startSpeed", data.InitialModule.StartSpeed);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var main = particleSystem.main;
            if (data.SimulationSpace >= (int)ParticleSystemSimulationSpace.Local &&
                data.SimulationSpace <= (int)ParticleSystemSimulationSpace.Custom)
                main.simulationSpace = (ParticleSystemSimulationSpace)data.SimulationSpace;
            if (data.ScalingMode >= (int)ParticleSystemScalingMode.Hierarchy &&
                data.ScalingMode <= (int)ParticleSystemScalingMode.Shape)
                main.scalingMode = (ParticleSystemScalingMode)data.ScalingMode;
            if (data.InitialModule != null)
            {
                if (data.InitialModule.StartLifetime != null)
                    main.startLifetime = data.InitialModule.StartLifetime.ToMinMaxCurve();
                if (data.InitialModule.StartSpeed != null)
                    main.startSpeed = data.InitialModule.StartSpeed.ToMinMaxCurve();
                if (data.InitialModule.StartRotation != null)
                    main.startRotation = data.InitialModule.StartRotation.ToMinMaxCurve();
                if (data.Rotation3D)
                {
                    main.startRotation3D = true;
                    if (data.InitialModule.StartRotationX != null)
                        main.startRotationX = data.InitialModule.StartRotationX.ToMinMaxCurve();
                    if (data.InitialModule.StartRotationY != null)
                        main.startRotationY = data.InitialModule.StartRotationY.ToMinMaxCurve();
                    if (data.InitialModule.StartRotation != null)
                        main.startRotationZ = data.InitialModule.StartRotation.ToMinMaxCurve();
                }
                main.flipRotation = Mathf.Clamp01(data.InitialModule.RandomizeRotationDirection);
            }
            if (data.RandomSeed != 0 && !Application.isPlaying)
                particleSystem.randomSeed = unchecked((uint)data.RandomSeed);
            if (data.StartColor != null)
            {
                main.startColor = data.StartColor.ToMinMaxGradient();
            }
            else
            {
                // SR4.4's partial ParticleSystem type tree omits StartColor on
                // some effects. Unity can then retain its native zero color,
                // producing particles with RGBA(0,0,0,0) even though the
                // particle count and renderer are valid.
                main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
                var colorSerialized = new SerializedObject(particleSystem);
                SetInteger(colorSerialized, "InitialModule.startColor.minMaxState", 0);
                SetColor(colorSerialized, "InitialModule.startColor.minColor", Color.white);
                SetColor(colorSerialized, "InitialModule.startColor.maxColor", Color.white);
                colorSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
            if (data.StartSize != null)
                main.startSize = data.StartSize.ToMinMaxCurve();
            else
                // The partial SR type tree does not expose StartSize. A zero
                // default is not renderable, so retain Unity's normal particle
                // size instead of serializing an invisible system.
                main.startSize = new ParticleSystem.MinMaxCurve(1f);
            if (!main.startSize3D &&
                main.startSize.mode == ParticleSystemCurveMode.Constant &&
                main.startSize.constant <= 0f)
            {
                // Re-assert the serialized curve as well. Unity can preserve
                // the zero scalar from the partially decoded native block when
                // the public MainModule value is assigned during reconstruction.
                var sizeSerialized = new SerializedObject(particleSystem);
                SetInteger(sizeSerialized, "InitialModule.startSize.minMaxState", 0);
                SetFloat(sizeSerialized, "InitialModule.startSize.scalar", 1f);
                SetFloat(sizeSerialized, "InitialModule.startSize.minScalar", 1f);
                sizeSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
            if (data.Size3D)
            {
                main.startSize3D = true;
                // SR4.4 serializes the X axis under StartSize. When Size3D is
                // enabled Unity no longer uses the legacy startSize field.
                // Leaving X untouched makes Unity retain its native 0..1
                // default, which changes the particle footprint.
                var startSizeX = data.StartSizeX ?? data.StartSize;
                if (startSizeX != null)
                    main.startSizeX = startSizeX.ToMinMaxCurve();
                if (data.StartSizeY != null)
                    main.startSizeY = data.StartSizeY.ToMinMaxCurve();
                if (data.StartSizeZ != null)
                    main.startSizeZ = data.StartSizeZ.ToMinMaxCurve();
            }

            // Re-apply the SR curve values after the public MainModule setters
            // so they survive prefab save. State 3 is Unity's TwoConstants.
            var sizeAxesSerialized = new SerializedObject(particleSystem);
            if (data.StartSize != null)
                SetMinMaxCurve(sizeAxesSerialized, "InitialModule.startSize", data.StartSize);
            if (data.Size3D)
            {
                if (data.StartSizeY != null)
                    SetMinMaxCurve(sizeAxesSerialized, "InitialModule.startSizeY", data.StartSizeY);
                if (data.StartSizeZ != null)
                    SetMinMaxCurve(sizeAxesSerialized, "InitialModule.startSizeZ", data.StartSizeZ);
            }
            sizeAxesSerialized.ApplyModifiedPropertiesWithoutUndo();
            if (data.MaxNumParticles > 0)
                main.maxParticles = data.MaxNumParticles;
            if (data.GravityModifier != null)
                main.gravityModifier = data.GravityModifier.ToMinMaxCurve();
            if (data.ColorOverLifetime != null)
            {
                var colorOverLifetime = particleSystem.colorOverLifetime;
                // SR4.4's partial particle data can preserve the gradient while
                // losing the module-enabled bit. If the stored alpha curve is
                // genuinely non-constant, disabling the module makes the
                // reconstructed particle remain opaque for its whole lifetime.
                colorOverLifetime.enabled = data.ColorOverLifetimeEnabled ||
                                             HasAnimatedAlpha(data.ColorOverLifetime);
                colorOverLifetime.color = data.ColorOverLifetime.ToMinMaxGradient();
            }

            ApplyParticleModules(particleSystem, data.Raw);
        }

        private static ParticleSystemData DeserializeParticleSystemData(string json)
        {
            // Exported SR fields use lowerCamelCase while the importer model
            // intentionally uses PascalCase. JsonUtility is case-sensitive and
            // silently turns simulationSpeed/lengthInSec into zero defaults.
            var root = JObject.Parse(json);
            var data = root.ToObject<ParticleSystemData>() ?? new ParticleSystemData();
            data.Raw = root;
            return data;
        }

        private static void ApplyParticleModules(ParticleSystem particleSystem, JObject root)
        {
            if (root == null)
                return;

            // The SR exporter names Unity's Color over Lifetime module
            // `ColorModule`. Apply its enabled bit explicitly after native
            // serialized data so a partial TypeTree cannot leave the module
            // disabled while its gradient is present.
            if (root["ColorModule"] is JObject colorData)
                ApplyParticleColorModule(particleSystem, colorData);

            var emissionData = root["EmissionModule"] as JObject;
            if (emissionData != null)
            {
                var emission = particleSystem.emission;
                emission.enabled = ReadBool(emissionData, "enabled");
                if (ReadCurve(emissionData["rateOverTime"]) is { } rateOverTime)
                    emission.rateOverTime = rateOverTime;
                if (ReadCurve(emissionData["rateOverDistance"]) is { } rateOverDistance)
                    emission.rateOverDistance = rateOverDistance;

                var burstTokens = emissionData["m_Bursts"] as JArray;
                if (burstTokens != null)
                {
                    var bursts = burstTokens
                        .OfType<JObject>()
                        .Select(ReadBurst)
                        .ToArray();
                    emission.SetBursts(bursts);
                }

                // These are SR/Unity-version-specific emission fields. Unity
                // 2022 may not expose them, so only write a serialized property
                // when the active editor actually has that field.
                var emissionSerialized = new SerializedObject(particleSystem);
                if (emissionData["m_EnableFallOff"] != null)
                    SetBooleanIfPresent(emissionSerialized, "EmissionModule.m_EnableFallOff",
                        ReadBool(emissionData, "m_EnableFallOff"));
                if (emissionData["m_EnableEmissionLevel"] != null)
                    SetBooleanIfPresent(emissionSerialized, "EmissionModule.m_EnableEmissionLevel",
                        ReadBool(emissionData, "m_EnableEmissionLevel"));
                if (emissionData["m_EnableEmissionCallback"] != null)
                    SetBooleanIfPresent(emissionSerialized, "EmissionModule.m_EnableEmissionCallback",
                        ReadBool(emissionData, "m_EnableEmissionCallback"));
                if (emissionData["m_EmissionFalloffStart"] != null)
                    SetFloatIfPresent(emissionSerialized, "EmissionModule.m_EmissionFalloffStart",
                        ReadFloat(emissionData, "m_EmissionFalloffStart"));
                if (emissionData["m_EmissionFalloffEnd"] != null)
                    SetFloatIfPresent(emissionSerialized, "EmissionModule.m_EmissionFalloffEnd",
                        ReadFloat(emissionData, "m_EmissionFalloffEnd"));
                if (ReadVector4(emissionData["m_EmissionLevel"]) is { } emissionLevel)
                    SetVector4IfPresent(emissionSerialized, "EmissionModule.m_EmissionLevel", emissionLevel);
                if (emissionData["m_MinLODParticles"] != null)
                    SetIntegerIfPresent(emissionSerialized, "EmissionModule.m_MinLODParticles",
                        ReadInt(emissionData, "m_MinLODParticles"));
                emissionSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var sizeData = root["SizeModule"] as JObject;
            if (sizeData != null)
            {
                var size = particleSystem.sizeOverLifetime;
                size.enabled = ReadBool(sizeData, "enabled");
                size.separateAxes = ReadBool(sizeData, "separateAxes");
                if (ReadCurve(sizeData["curve"]) is { } curve)
                    size.size = curve;
                if (ReadCurve(sizeData["x"]) is { } x)
                    size.x = x;
                if (ReadCurve(sizeData["y"]) is { } y)
                    size.y = y;
                if (ReadCurve(sizeData["z"]) is { } z)
                    size.z = z;
            }

            var rotationData = root["RotationModule"] as JObject;
            if (rotationData != null)
            {
                var rotation = particleSystem.rotationOverLifetime;
                rotation.enabled = ReadBool(rotationData, "enabled");
                rotation.separateAxes = ReadBool(rotationData, "separateAxes");
                if (ReadCurve(rotationData["curve"]) is { } angularVelocity)
                    rotation.z = angularVelocity;
                if (ReadCurve(rotationData["x"]) is { } x)
                    rotation.x = x;
                if (ReadCurve(rotationData["y"]) is { } y)
                    rotation.y = y;
                if (ReadCurve(rotationData["z"]) is { } z)
                    rotation.z = z;
            }

            var velocityData = root["VelocityModule"] as JObject;
            if (velocityData != null)
            {
                var velocity = particleSystem.velocityOverLifetime;
                velocity.enabled = ReadBool(velocityData, "enabled");
                velocity.space = ReadEnum(velocityData, "inWorldSpace", false)
                    ? ParticleSystemSimulationSpace.World
                    : ParticleSystemSimulationSpace.Local;
                if (ReadCurve(velocityData["x"]) is { } x)
                    velocity.x = x;
                if (ReadCurve(velocityData["y"]) is { } y)
                    velocity.y = y;
                if (ReadCurve(velocityData["z"]) is { } z)
                    velocity.z = z;
                if (ReadCurve(velocityData["orbitalX"]) is { } orbitalX)
                    velocity.orbitalX = orbitalX;
                if (ReadCurve(velocityData["orbitalY"]) is { } orbitalY)
                    velocity.orbitalY = orbitalY;
                if (ReadCurve(velocityData["orbitalZ"]) is { } orbitalZ)
                    velocity.orbitalZ = orbitalZ;
                if (ReadCurve(velocityData["orbitalOffsetX"]) is { } orbitalOffsetX)
                    velocity.orbitalOffsetX = orbitalOffsetX;
                if (ReadCurve(velocityData["orbitalOffsetY"]) is { } orbitalOffsetY)
                    velocity.orbitalOffsetY = orbitalOffsetY;
                if (ReadCurve(velocityData["orbitalOffsetZ"]) is { } orbitalOffsetZ)
                    velocity.orbitalOffsetZ = orbitalOffsetZ;
                if (ReadCurve(velocityData["radial"]) is { } radial)
                    velocity.radial = radial;
                if (ReadCurve(velocityData["speedModifier"]) is { } speedModifier)
                    velocity.speedModifier = speedModifier;
            }

            var shapeData = root["ShapeModule"] as JObject;
            if (shapeData != null)
            {
                var shape = particleSystem.shape;
                shape.enabled = ReadBool(shapeData, "enabled");
                shape.shapeType = (ParticleSystemShapeType)Mathf.Clamp(ReadInt(shapeData, "type"), 0, 23);
                shape.angle = ReadFloat(shapeData, "angle", shape.angle);
                shape.length = ReadFloat(shapeData, "length", shape.length);
                shape.radius = ReadScalar(shapeData["radius"], shape.radius);
                shape.radiusThickness = ReadFloat(shapeData, "radiusThickness", shape.radiusThickness);
                shape.scale = ReadVector3(shapeData["boxThickness"], shape.scale);
                shape.donutRadius = ReadFloat(shapeData, "donutRadius", shape.donutRadius);
                shape.position = ReadVector3(shapeData["m_Position"], shape.position);
                shape.rotation = ReadVector3(shapeData["m_Rotation"], shape.rotation);
                var authoredShapeScale = ReadVector3(shapeData["m_Scale"], shape.scale);
                shape.scale = authoredShapeScale;
                if (shape.shapeType == ParticleSystemShapeType.Mesh ||
                    shape.shapeType == ParticleSystemShapeType.MeshRenderer ||
                    shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer)
                {
                    shape.meshShapeType = (ParticleSystemMeshShapeType)Mathf.Clamp(
                        ReadInt(shapeData, "placementMode", (int)shape.meshShapeType), 0, 2);
                }
                shape.alignToDirection = ReadBool(shapeData, "alignToDirection");
                shape.randomDirectionAmount = ReadFloat(shapeData, "randomDirectionAmount", shape.randomDirectionAmount);
                shape.sphericalDirectionAmount = ReadFloat(shapeData, "sphericalDirectionAmount", shape.sphericalDirectionAmount);
                shape.randomPositionAmount = ReadFloat(shapeData, "randomPositionAmount", shape.randomPositionAmount);
                shape.useMeshMaterialIndex = ReadBool(shapeData, "m_UseMeshMaterialIndex");
                shape.meshMaterialIndex = ReadInt(shapeData, "m_MeshMaterialIndex", shape.meshMaterialIndex);
                shape.useMeshColors = ReadBool(shapeData, "m_UseMeshColors", shape.useMeshColors);
                shape.normalOffset = ReadFloat(shapeData, "m_MeshNormalOffset", shape.normalOffset);
                shape.textureClipChannel = (ParticleSystemShapeTextureChannel)Mathf.Clamp(
                    ReadInt(shapeData, "m_TextureClipChannel", (int)shape.textureClipChannel), 0, 3);
                shape.textureUVChannel = Mathf.Clamp(
                    ReadInt(shapeData, "m_TextureUVChannel", shape.textureUVChannel), 0, 1);
                shape.textureColorAffectsParticles = ReadBool(
                    shapeData, "m_TextureColorAffectsParticles", shape.textureColorAffectsParticles);
                shape.textureAlphaAffectsParticles = ReadBool(
                    shapeData, "m_TextureAlphaAffectsParticles", shape.textureAlphaAffectsParticles);
                shape.textureBilinearFiltering = ReadBool(
                    shapeData, "m_TextureBilinearFiltering", shape.textureBilinearFiltering);
                if (shapeData["m_MeshSpawn"] is JObject meshSpawnData)
                {
                    shape.meshSpawnMode = (ParticleSystemShapeMultiModeValue)Mathf.Clamp(ReadInt(meshSpawnData, "mode", (int)shape.meshSpawnMode), 0, 2);
                    shape.meshSpawnSpread = ReadFloat(meshSpawnData, "spread", shape.meshSpawnSpread);
                    if (ReadCurve(meshSpawnData["speed"]) is { } meshSpawnSpeed)
                        shape.meshSpawnSpeed = meshSpawnSpeed;
                }
                if (shapeData["radius"] is JObject radiusData)
                {
                    shape.radiusMode = (ParticleSystemShapeMultiModeValue)Mathf.Clamp(ReadInt(radiusData, "mode", (int)shape.radiusMode), 0, 2);
                    shape.radiusSpread = ReadFloat(radiusData, "spread", shape.radiusSpread);
                    if (ReadCurve(radiusData["speed"]) is { } radiusSpeed)
                        shape.radiusSpeed = radiusSpeed;
                }
                shape.arc = ReadScalar(shapeData["arc"], shape.arc);
                if (shapeData["arc"] is JObject arcData)
                {
                    shape.arcMode = (ParticleSystemShapeMultiModeValue)Mathf.Clamp(ReadInt(arcData, "mode", (int)shape.arcMode), 0, 2);
                    shape.arcSpread = ReadFloat(arcData, "spread", shape.arcSpread);
                    if (ReadCurve(arcData["speed"]) is { } arcSpeed)
                        shape.arcSpeed = arcSpeed;
                }
            }

            var clampData = root["ClampVelocityModule"] as JObject;
            if (clampData != null)
            {
                var clamp = particleSystem.limitVelocityOverLifetime;
                clamp.enabled = ReadBool(clampData, "enabled");
                clamp.separateAxes = ReadBool(clampData, "separateAxis");
                clamp.space = ReadBool(clampData, "inWorldSpace", false)
                    ? ParticleSystemSimulationSpace.World
                    : ParticleSystemSimulationSpace.Local;
                clamp.multiplyDragByParticleSize = ReadBool(
                    clampData, "multiplyDragByParticleSize", clamp.multiplyDragByParticleSize);
                clamp.multiplyDragByParticleVelocity = ReadBool(
                    clampData, "multiplyDragByParticleVelocity", clamp.multiplyDragByParticleVelocity);
                if (ReadCurve(clampData["magnitude"]) is { } magnitude)
                    clamp.limit = magnitude;
                if (ReadCurve(clampData["x"]) is { } x)
                    clamp.limitX = x;
                if (ReadCurve(clampData["y"]) is { } y)
                    clamp.limitY = y;
                if (ReadCurve(clampData["z"]) is { } z)
                    clamp.limitZ = z;
                clamp.dampen = ReadFloat(clampData, "dampen", clamp.dampen);
            }

            var customData = root["CustomDataModule"] as JObject;
            if (customData != null)
            {
                var module = particleSystem.customData;
                module.enabled = ReadBool(customData, "enabled");
                ApplyCustomDataStream(module, ParticleSystemCustomData.Custom1, customData, "mode0", "vectorComponentCount0", "vector0_", "color0");
                ApplyCustomDataStream(module, ParticleSystemCustomData.Custom2, customData, "mode1", "vectorComponentCount1", "vector1_", "color1");
            }

            var noiseData = root["NoiseModule"] as JObject;
            if (noiseData != null)
            {
                var noise = particleSystem.noise;
                noise.enabled = ReadBool(noiseData, "enabled");
                noise.separateAxes = ReadBool(noiseData, "separateAxes", noise.separateAxes);
                if (ReadCurve(noiseData["strength"]) is { } strength)
                    noise.strength = strength;
                if (ReadCurve(noiseData["strengthY"]) is { } strengthY)
                    noise.strengthY = strengthY;
                if (ReadCurve(noiseData["strengthZ"]) is { } strengthZ)
                    noise.strengthZ = strengthZ;
                noise.frequency = ReadFloat(noiseData, "frequency", noise.frequency);
                noise.damping = ReadBool(noiseData, "damping", noise.damping);
                noise.octaveCount = Mathf.Clamp(ReadInt(noiseData, "octaves", noise.octaveCount), 1, 4);
                noise.octaveMultiplier = ReadFloat(noiseData, "octaveMultiplier", noise.octaveMultiplier);
                noise.octaveScale = ReadFloat(noiseData, "octaveScale", noise.octaveScale);
                noise.quality = (ParticleSystemNoiseQuality)Mathf.Clamp(ReadInt(noiseData, "quality", (int)noise.quality), 0, 2);
                if (ReadCurve(noiseData["scrollSpeed"]) is { } scrollSpeed)
                    noise.scrollSpeed = scrollSpeed;
                noise.remapEnabled = ReadBool(noiseData, "remapEnabled", noise.remapEnabled);
                if (ReadCurve(noiseData["remap"]) is { } remap)
                    noise.remap = remap;
                if (ReadCurve(noiseData["remapY"]) is { } remapY)
                    noise.remapY = remapY;
                if (ReadCurve(noiseData["remapZ"]) is { } remapZ)
                    noise.remapZ = remapZ;
                if (ReadCurve(noiseData["positionAmount"]) is { } positionAmount)
                    noise.positionAmount = positionAmount;
                if (ReadCurve(noiseData["rotationAmount"]) is { } rotationAmount)
                    noise.rotationAmount = rotationAmount;
                if (ReadCurve(noiseData["sizeAmount"]) is { } sizeAmount)
                    noise.sizeAmount = sizeAmount;
            }

            var uvData = root["UVModule"] as JObject;
            if (uvData != null)
            {
                var textureSheet = particleSystem.textureSheetAnimation;
                textureSheet.enabled = ReadBool(uvData, "enabled");
                textureSheet.mode = (ParticleSystemAnimationMode)Mathf.Clamp(ReadInt(uvData, "mode", (int)textureSheet.mode), 0, 1);
                textureSheet.timeMode = (ParticleSystemAnimationTimeMode)Mathf.Clamp(ReadInt(uvData, "timeMode", (int)textureSheet.timeMode), 0, 2);
                textureSheet.fps = ReadFloat(uvData, "fps", textureSheet.fps);
                textureSheet.numTilesX = Mathf.Max(1, ReadInt(uvData, "tilesX", textureSheet.numTilesX));
                textureSheet.numTilesY = Mathf.Max(1, ReadInt(uvData, "tilesY", textureSheet.numTilesY));
                textureSheet.animation = (ParticleSystemAnimationType)Mathf.Clamp(ReadInt(uvData, "animationType", (int)textureSheet.animation), 0, 1);
                textureSheet.rowMode = (ParticleSystemAnimationRowMode)Mathf.Clamp(ReadInt(uvData, "rowMode", (int)textureSheet.rowMode), 0, 1);
                textureSheet.rowIndex = Mathf.Max(0, ReadInt(uvData, "rowIndex", textureSheet.rowIndex));
                textureSheet.cycleCount = Mathf.Max(1, ReadInt(uvData, "cycles", textureSheet.cycleCount));
                textureSheet.speedRange = ReadVector2(uvData, "speedRange", textureSheet.speedRange);
                textureSheet.uvChannelMask = (UnityEngine.Rendering.UVChannelFlags)ReadInt(uvData, "uvChannelMask", (int)textureSheet.uvChannelMask);
                if (uvData["rowMode"] == null && uvData["useRandomRow"] != null)
                {
                    textureSheet.rowMode = ReadBool(uvData, "useRandomRow")
                        ? ParticleSystemAnimationRowMode.Random
                        : ParticleSystemAnimationRowMode.Custom;
                }
                var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    var flip = renderer.flip;
                    flip.x = Mathf.Clamp01(ReadFloat(uvData, "flipU", flip.x));
                    flip.y = Mathf.Clamp01(ReadFloat(uvData, "flipV", flip.y));
                    renderer.flip = flip;
                }
                if (ReadCurve(uvData["frameOverTime"]) is { } frameOverTime)
                    textureSheet.frameOverTime = frameOverTime;
                if (ReadCurve(uvData["startFrame"]) is { } startFrame)
                    textureSheet.startFrame = startFrame;
            }

            var trailSettings = root["TrailModule"] as JObject;
            if (trailSettings != null)
            {
                var trails = particleSystem.trails;
                trails.enabled = ReadBool(trailSettings, "enabled");
                trails.mode = (ParticleSystemTrailMode)Mathf.Clamp(ReadInt(trailSettings, "mode", (int)trails.mode), 0, 1);
                trails.ratio = Mathf.Clamp01(ReadFloat(trailSettings, "ratio", trails.ratio));
                if (ReadCurve(trailSettings["lifetime"]) is { } lifetime)
                    trails.lifetime = lifetime;
                trails.minVertexDistance = Mathf.Max(0f, ReadFloat(trailSettings, "minVertexDistance", trails.minVertexDistance));
                trails.textureMode = (ParticleSystemTrailTextureMode)Mathf.Clamp(ReadInt(trailSettings, "textureMode", (int)trails.textureMode), 0, 1);
                trails.worldSpace = ReadBool(trailSettings, "worldSpace", trails.worldSpace);
                trails.dieWithParticles = ReadBool(trailSettings, "dieWithParticles", trails.dieWithParticles);
                trails.sizeAffectsWidth = ReadBool(trailSettings, "sizeAffectsWidth", trails.sizeAffectsWidth);
                trails.sizeAffectsLifetime = ReadBool(trailSettings, "sizeAffectsLifetime", trails.sizeAffectsLifetime);
                trails.inheritParticleColor = ReadBool(trailSettings, "inheritParticleColor", trails.inheritParticleColor);
                trails.generateLightingData = ReadBool(trailSettings, "generateLightingData", trails.generateLightingData);
                trails.ribbonCount = Mathf.Max(1, ReadInt(trailSettings, "ribbonCount", trails.ribbonCount));
                trails.shadowBias = Mathf.Clamp01(ReadFloat(trailSettings, "shadowBias", trails.shadowBias));
                trails.splitSubEmitterRibbons = ReadBool(trailSettings, "splitSubEmitterRibbons", trails.splitSubEmitterRibbons);
                trails.attachRibbonsToTransform = ReadBool(trailSettings, "attachRibbonsToTransform", trails.attachRibbonsToTransform);
                if (ReadCurve(trailSettings["widthOverTrail"]) is { } widthOverTrail)
                    trails.widthOverTrail = widthOverTrail;
                if (trailSettings["colorOverLifetime"] is JObject trailLifetime && TryReadPackedMinMaxGradient(trailLifetime, out var trailColorLifetime))
                    trails.colorOverLifetime = trailColorLifetime;
                if (trailSettings["colorOverTrail"] is JObject trailOverTrail && TryReadPackedMinMaxGradient(trailOverTrail, out var trailColorOverTrail))
                    trails.colorOverTrail = trailColorOverTrail;
            }

            var lightsData = root["LightsModule"] as JObject;
            if (lightsData != null)
            {
                var lights = particleSystem.lights;
                lights.enabled = ReadBool(lightsData, "enabled");
                lights.ratio = Mathf.Clamp01(ReadFloat(lightsData, "ratio", lights.ratio));
                lights.useRandomDistribution = ReadBool(lightsData, "randomDistribution", lights.useRandomDistribution);
                lights.useParticleColor = ReadBool(lightsData, "color", lights.useParticleColor);
                lights.sizeAffectsRange = ReadBool(lightsData, "range", lights.sizeAffectsRange);
                lights.alphaAffectsIntensity = ReadBool(lightsData, "intensity", lights.alphaAffectsIntensity);
                lights.maxLights = Mathf.Max(0, ReadInt(lightsData, "maxLights", lights.maxLights));
                if (ReadCurve(lightsData["rangeCurve"]) is { } range)
                    lights.range = range;
                if (ReadCurve(lightsData["intensityCurve"]) is { } intensity)
                    lights.intensity = intensity;
            }
        }

        private static void ApplyCustomDataStream(
            ParticleSystem.CustomDataModule module,
            ParticleSystemCustomData stream,
            JObject data,
            string modeName,
            string componentCountName,
            string vectorPrefix,
            string colorName)
        {
            var mode = ReadInt(data, modeName);
            if (mode == (int)ParticleSystemCustomDataMode.Vector)
            {
                module.SetMode(stream, ParticleSystemCustomDataMode.Vector);
                var count = Mathf.Clamp(ReadInt(data, componentCountName, 4), 1, 4);
                module.SetVectorComponentCount(stream, count);
                for (var component = 0; component < count; component++)
                {
                    if (ReadCurve(data[$"{vectorPrefix}{component}"]) is { } curve)
                        module.SetVector(stream, component, curve);
                }
            }
            else if (mode == (int)ParticleSystemCustomDataMode.Color)
            {
                module.SetMode(stream, ParticleSystemCustomDataMode.Color);
                if (data[colorName] is JObject colorData)
                {
                    var gradient = colorData.ToObject<MinMaxGradientData>();
                    if (gradient != null)
                        module.SetColor(stream, gradient.ToMinMaxGradient());
                }
            }
            else
            {
                module.SetMode(stream, ParticleSystemCustomDataMode.Disabled);
            }
        }

        private static ParticleSystem.Burst ReadBurst(JObject token)
        {
            var count = token["countCurve"] as JObject;
            var min = count == null ? 1 : Mathf.Clamp(Mathf.RoundToInt(ReadFloat(count, "minScalar", 1f)), 0, short.MaxValue);
            var max = count == null ? min : Mathf.Clamp(Mathf.RoundToInt(ReadFloat(count, "scalar", min)), min, short.MaxValue);
            var burst = new ParticleSystem.Burst(
                ReadFloat(token, "time", 0f),
                (short)min,
                (short)max,
                Math.Max(1, ReadInt(token, "cycleCount")),
                Mathf.Max(0f, ReadFloat(token, "repeatInterval", 0f)));
            burst.probability = Mathf.Clamp01(ReadFloat(token, "probability", 1f));
            return burst;
        }

        private static ParticleSystem.MinMaxCurve? ReadCurve(JToken token)
        {
            return token is JObject objectToken
                ? objectToken.ToObject<MinMaxCurveData>()?.ToMinMaxCurve()
                : null;
        }

        private static void ApplyParticleColorModule(ParticleSystem particleSystem, JObject colorModule)
        {
            var colorOverLifetime = particleSystem.colorOverLifetime;
            colorOverLifetime.enabled = ReadBool(colorModule, "enabled");
            if (colorModule["gradient"] is JObject gradientData &&
                TryReadPackedMinMaxGradient(gradientData, out var gradient))
                colorOverLifetime.color = gradient;
        }

        private static bool TryReadPackedMinMaxGradient(
            JObject data,
            out ParticleSystem.MinMaxGradient gradient)
        {
            gradient = default;
            var state = ReadInt(data, "minMaxState", -1);
            if (state < 0)
                return false;

            switch (state)
            {
                case 1 when data["maxGradient"] is JObject maxGradient:
                    gradient = new ParticleSystem.MinMaxGradient(ReadPackedGradient(maxGradient));
                    return true;
                case 3 when data["minGradient"] is JObject minGradient &&
                            data["maxGradient"] is JObject maxGradient:
                    gradient = new ParticleSystem.MinMaxGradient(
                        ReadPackedGradient(minGradient),
                        ReadPackedGradient(maxGradient));
                    return true;
                case 2:
                    gradient = new ParticleSystem.MinMaxGradient(
                        ReadPackedColor(data["minColor"] as JObject, Color.white),
                        ReadPackedColor(data["maxColor"] as JObject, Color.white));
                    return true;
                default:
                    gradient = new ParticleSystem.MinMaxGradient(
                        ReadPackedColor(data["minColor"] as JObject, Color.white));
                    return true;
            }
        }

        private static Gradient ReadPackedGradient(JObject data)
        {
            var colorCount = Mathf.Clamp(ReadInt(data, "m_NumColorKeys", 2), 2, 8);
            var alphaCount = Mathf.Clamp(ReadInt(data, "m_NumAlphaKeys", 2), 2, 8);
            var colors = Enumerable.Range(0, colorCount)
                .Select(index => new GradientColorKey(
                    ReadPackedColor(data[$"key{index}"] as JObject, Color.white),
                    ReadPackedTime(data, "ctime", index)))
                .ToArray();
            var alphas = Enumerable.Range(0, alphaCount)
                .Select(index => new GradientAlphaKey(
                    ReadFloat(data[$"key{index}"] as JObject, "a", 1f),
                    ReadPackedTime(data, "atime", index)))
                .ToArray();
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            var mode = ReadInt(data, "m_Mode", 0);
            if (Enum.IsDefined(typeof(GradientMode), mode))
                gradient.mode = (GradientMode)mode;
            return gradient;
        }

        private static Color ReadPackedColor(JObject data, Color fallback)
        {
            if (data == null)
                return fallback;
            return new Color(
                ReadFloat(data, "r", fallback.r),
                ReadFloat(data, "g", fallback.g),
                ReadFloat(data, "b", fallback.b),
                ReadFloat(data, "a", fallback.a));
        }

        private static float ReadPackedTime(JObject data, string prefix, int index)
        {
            var value = ReadFloat(data, $"{prefix}{index}", 0f);
            return Mathf.Clamp01(value / 65535f);
        }

        private static bool ReadBool(JObject token, string name, bool fallback = false)
        {
            return token[name]?.Type == JTokenType.Boolean ? token[name].Value<bool>() : fallback;
        }

        private static Vector3 ReadVector3(JToken token, Vector3 fallback)
        {
            if (!(token is JObject value))
                return fallback;
            return new Vector3(
                ReadFloat(value, "x", fallback.x),
                ReadFloat(value, "y", fallback.y),
                ReadFloat(value, "z", fallback.z));
        }

        private static Vector4? ReadVector4(JToken token)
        {
            if (!(token is JObject value))
                return null;
            return new Vector4(
                ReadFloat(value, "x"),
                ReadFloat(value, "y"),
                ReadFloat(value, "z"),
                ReadFloat(value, "w"));
        }

        private static float ReadScalar(JToken token, float fallback)
        {
            if (token == null)
                return fallback;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<float>();
            if (token is JObject value)
                return ReadFloat(value, "value", fallback);
            return fallback;
        }

        private static bool ReadEnum(JObject token, string name, bool fallback)
        {
            return token[name]?.Type switch
            {
                JTokenType.Boolean => token[name].Value<bool>(),
                JTokenType.Integer => token[name].Value<int>() != 0,
                _ => fallback,
            };
        }

        private static bool HasAnimatedAlpha(MinMaxGradientData data)
        {
            if (data == null)
                return false;

            return HasAnimatedAlpha(data.MinGradient) || HasAnimatedAlpha(data.MaxGradient);
        }

        private static bool HasAnimatedAlpha(GradientData data)
        {
            var keys = data?.AlphaKeys;
            if (keys == null || keys.Length < 2)
                return false;

            var first = Mathf.Clamp01(keys[0].Alpha);
            return keys.Any(key => Mathf.Abs(Mathf.Clamp01(key.Alpha) - first) > 0.0001f);
        }

        private static void ApplySerializedColorLifetimeFallback(ParticleSystem particleSystem, string json)
        {
            var root = JObject.Parse(json);
            if (root["ColorModule"] is JObject colorModule)
            {
                ApplyParticleColorModule(particleSystem, colorModule);
                return;
            }
            if (root.Value<bool>("ColorOverLifetimeEnabled"))
                return;

            var alphaKeys = root["ColorOverLifetime"]?["MaxGradient"]?["AlphaKeys"] as JArray;
            if (alphaKeys == null || alphaKeys.Count < 2)
                return;

            var first = alphaKeys[0]["Alpha"].Value<float>();
            var hasAnimatedAlpha = alphaKeys
                .Skip(1)
                .Any(key => Mathf.Abs(Mathf.Clamp01(key["Alpha"].Value<float>()) - Mathf.Clamp01(first)) > 0.0001f);
            if (hasAnimatedAlpha)
            {
                var colorOverLifetime = particleSystem.colorOverLifetime;
                colorOverLifetime.enabled = true;
            }
        }

        private static void EnsureParticleSystemDefaults(ParticleSystem particleSystem)
        {
            var main = particleSystem.main;
            if (!main.startSize3D &&
                (!IsFinite(main.startSize.constantMin) || !IsFinite(main.startSize.constantMax) ||
                 (main.startSize.constantMin <= 0.000001f && main.startSize.constantMax <= 0.000001f)))
            {
                // Missing SR4.4 type-tree fields can deserialize a particle size as
                // zero. Do not overwrite authored non-zero curves; only repair the
                // unusable zero/non-finite sentinel.
                main.startSize = new ParticleSystem.MinMaxCurve(1f);
            }

            if (main.startColor.color.a <= 0.001f)
            {
                // External SR4.4 type-tree application can leave the native
                // start color at RGBA(0,0,0,0). Keep the particle renderable
                // until a decoded color module is available.
                main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
                var colorSerialized = new SerializedObject(particleSystem);
                SetInteger(colorSerialized, "InitialModule.startColor.minMaxState", 0);
                SetColor(colorSerialized, "InitialModule.startColor.minColor", Color.white);
                SetColor(colorSerialized, "InitialModule.startColor.maxColor", Color.white);
                colorSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

        }

        private static void SetMinMaxCurve(SerializedObject target, string path, MinMaxCurveData curve)
        {
            if (curve == null)
                return;
            SetInteger(target, $"{path}.minMaxState", curve.MinMaxState);
            SetFloat(target, $"{path}.scalar", curve.Scalar);
            SetFloat(target, $"{path}.minScalar", curve.MinScalar);
        }

        private static void SetFloat(SerializedObject target, string name, float value)
        {
            if (IsFinite(value) && target.FindProperty(name) is { } property)
                property.floatValue = value;
        }

        private static void SetColor(SerializedObject target, string name, Color value)
        {
            if (target.FindProperty(name) is { } property)
                property.colorValue = value;
        }

        private static void SetInteger(SerializedObject target, string name, int value)
        {
            if (target.FindProperty(name) is { } property)
                property.intValue = value;
        }

        private static void SetBoolean(SerializedObject target, string name, bool value)
        {
            if (target.FindProperty(name) is { } property)
                property.boolValue = value;
        }

        private static void SetBooleanIfPresent(SerializedObject target, string name, bool value)
        {
            if (target.FindProperty(name) is { propertyType: SerializedPropertyType.Boolean } property)
                property.boolValue = value;
        }

        private static void SetIntegerIfPresent(SerializedObject target, string name, int value)
        {
            if (target.FindProperty(name) is { propertyType: SerializedPropertyType.Integer } property)
                property.intValue = value;
        }

        private static void SetFloatIfPresent(SerializedObject target, string name, float value)
        {
            if (IsFinite(value) && target.FindProperty(name) is { propertyType: SerializedPropertyType.Float } property)
                property.floatValue = value;
        }

        private static void SetVector4IfPresent(SerializedObject target, string name, Vector4 value)
        {
            if (target.FindProperty(name) is { propertyType: SerializedPropertyType.Vector4 } property)
                property.vector4Value = value;
        }

        private static void SetVector2(SerializedObject target, string name, Vector2 value)
        {
            if (target.FindProperty(name) is { } property)
                property.vector2Value = value;
        }

        private static void SetFinite(float value, Action<float> setter)
        {
            if (IsFinite(value))
                setter(value);
        }

        private static void SetNormalizedRendererValue(float value, Action<float> setter, bool allowZero)
        {
            if (IsFinite(value) && value >= 0f && value <= 1f && (allowZero || value > 0f))
                setter(value);
        }

        private static Vector3 SanitizeRendererVector(Vector3 value)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z) || value.sqrMagnitude > 100f)
                return Vector3.zero;
            return new Vector3(
                Mathf.Abs(value.x) < 0.0001f ? 0f : value.x,
                Mathf.Abs(value.y) < 0.0001f ? 0f : value.y,
                Mathf.Abs(value.z) < 0.0001f ? 0f : value.z);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static int PathDepth(string path) => path.Count(character => character == '/');

        private static string GetNodeKey(ManifestNode node)
        {
            return $"{node.Path}|{node.PathID}";
        }

        private static string ParentPath(string path)
        {
            var separator = path.LastIndexOf('/');
            return separator < 0 ? string.Empty : path.Substring(0, separator);
        }

        private static string SanitizeFileName(string value)
        {
            return Path.GetInvalidFileNameChars().Aggregate(value, (current, invalid) => current.Replace(invalid, '_'));
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
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

        [Serializable]
        private sealed class Manifest
        {
            public string Name;
            public string UnityVersion;
            public ManifestNode[] Nodes;
            public ManifestMaterial[] Materials;
            public ManifestMesh[] Meshes;
        }

        [Serializable]
        private sealed class ManifestNode
        {
            public string Name;
            public string Path;
            public long PathID;
            public ManifestComponent[] Components;
        }

        [Serializable]
        private sealed class ManifestComponent
        {
            public string Type;
            public int FileID;
            public long PathID;
            public string SourceCAB;
            public string ParametersStatus;
            public string ParametersFile;
            public ManifestReference[] References;
            public MonoBehaviourInfo MonoBehaviour;
            public ParticleRendererInfo ParticleRenderer;
        }

        [Serializable]
        private sealed class ManifestReference
        {
            public string Type;
            public string Name;
            public int FileID;
            public long PathID;
            public string SourceCAB;
        }

        private sealed class PendingParticleBinding
        {
            public PendingParticleBinding(
                ParticleSystem particleSystem,
                ManifestComponent component,
                string json,
                string nodePath)
            {
                ParticleSystem = particleSystem;
                Component = component;
                Json = json;
                NodePath = nodePath;
            }

            public ParticleSystem ParticleSystem { get; }
            public ManifestComponent Component { get; }
            public string Json { get; }
            public string NodePath { get; }
        }

        [Serializable]
        private sealed class ParticleRendererInfo
        {
            public bool Enabled;
            public bool PrefixParsed;
            public int RenderMode; public int SortMode; public float MinParticleSize; public float MaxParticleSize;
            public float CameraVelocityScale; public float VelocityScale; public float LengthScale; public float SortingFudge;
            public float NormalDirection; public float ShadowBias; public int RenderAlignment;
            public Vector3Data Pivot; public Vector3Data Flip;
            public bool UseCustomVertexStreams; public int[] VertexStreams;
            public bool EnableGPUInstancing; public bool ApplyActiveColorSpace; public bool AllowRoll;
            public PointerInfo[] MaterialPointers;
            public PointerInfo[] MeshPointers;
        }

        [Serializable] private sealed class PointerInfo { public long PathID; public string SourceCAB; }
        [Serializable] private sealed class ManifestMaterial
        {
            public string SourceCAB; public long PathID; public string Name; public string ShaderName;
            public string ShaderSourceCAB; public long ShaderPathID; public string ShaderKeywords;
            public int RenderQueue; public bool EnableInstancing;
            public FloatProperty[] Floats; public ColorProperty[] Colors; public TextureProperty[] Textures;
        }
        [Serializable] private sealed class FloatProperty { public string Name; public float Value; }
        [Serializable] private sealed class ColorProperty { public string Name; public ColorData Value; }
        [Serializable] private sealed class TextureProperty
        {
            public string Name; public string PackageEntry; public Vector2Data Scale; public Vector2Data Offset;
        }
        [Serializable] private sealed class ManifestMesh
        {
            public string SourceCAB; public long PathID; public string Name; public int VertexCount;
            public float[] Vertices; public float[] Normals; public float[] Tangents; public float[] Colors;
            public float[] UV0; public float[] UV1; public float[] UV2; public float[] UV3;
            public float[] UV4; public float[] UV5; public float[] UV6; public float[] UV7;
            public ManifestSubMesh[] SubMeshes;
        }
        [Serializable] private sealed class ManifestSubMesh { public uint[] Indices; }

        [Serializable]
        private sealed class MonoBehaviourInfo
        {
            public string ClassName;
        }

        [Serializable]
        private sealed class TransformData
        {
            public Vector3Data LocalPosition;
            public QuaternionData LocalRotation;
            public Vector3Data LocalScale;
        }

        [Serializable]
        private struct Vector3Data
        {
            public float X;
            public float Y;
            public float Z;
            public Vector3 ToVector3() => new Vector3(X, Y, Z);
        }

        [Serializable]
        private struct Vector2Data
        {
            public float X;
            public float Y;
            public Vector2 ToVector2() => new Vector2(X, Y);
        }

        [Serializable]
        private struct QuaternionData
        {
            public float X;
            public float Y;
            public float Z;
            public float W;
            public Quaternion ToQuaternion() => new Quaternion(X, Y, Z, W);
        }

        [Serializable]
        private struct ColorData
        {
            public float r;
            public float g;
            public float b;
            public float a;
            public Color ToColor() => new Color(r, g, b, a);
        }

        [Serializable]
        private sealed class LightData
        {
            public bool Enabled;
            public int Type;
            public ColorData Color;
            public float Intensity;
            public float Range;
            public float SpotAngle;
            public float InnerSpotAngle;
            public float CookieSize;
            public ShadowData Shadows;
        }

        [Serializable]
        private sealed class ShadowData
        {
            public int Type;
            public int CustomResolution;
            public float Strength;
            public float Bias;
            public float NormalBias;
            public float NearPlane;
        }

        [Serializable]
        private sealed class ParticleSystemData
        {
            [JsonIgnore]
            public JObject Raw;
            public float LengthInSec;
            public float SimulationSpeed;
            public int StopAction;
            public int CullingMode;
            public int RingBufferMode;
            public Vector2Data RingBufferLoopRange;
            public bool Looping;
            public bool Prewarm;
            public bool PlayOnAwake;
            public bool UseUnscaledTime;
            public bool AutoRandomSeed;
            public bool UseRigidbodyForVelocity;
            public MinMaxCurveData StartDelay;
            public int SimulationSpace;
            public int ScalingMode;
            // Unity serializes this field as a signed 64-bit value in the
            // extracted JSON even though ParticleSystem.randomSeed is uint.
            public long RandomSeed;
            public InitialModuleData InitialModule;
            public MinMaxGradientData StartColor;
            public bool ColorOverLifetimeEnabled;
            public MinMaxGradientData ColorOverLifetime;
            public MinMaxCurveData StartSize;
            public MinMaxCurveData StartSizeX;
            public MinMaxCurveData StartSizeY;
            public MinMaxCurveData StartSizeZ;
            public bool Size3D;
            public bool Rotation3D;
            public int MaxNumParticles;
            public MinMaxCurveData GravityModifier;
        }

        [Serializable]
        private sealed class InitialModuleData
        {
            public bool Enabled;
            public int SrExtension0;
            public int SrExtension1;
            public MinMaxCurveData StartLifetime;
            public MinMaxCurveData StartSpeed;
            public MinMaxCurveData StartRotation;
            public MinMaxCurveData StartRotationX;
            public MinMaxCurveData StartRotationY;
            public float RandomizeRotationDirection;
        }

        [Serializable]
        private sealed class MinMaxGradientData
        {
            public int MinMaxState;
            public ColorData MinColor;
            public ColorData MaxColor;
            public GradientData MinGradient;
            public GradientData MaxGradient;

            public ParticleSystem.MinMaxGradient ToMinMaxGradient()
            {
                return MinMaxState switch
                {
                    1 when MaxGradient != null => new ParticleSystem.MinMaxGradient(MaxGradient.ToGradient()),
                    3 when MinGradient != null && MaxGradient != null => new ParticleSystem.MinMaxGradient(MinGradient.ToGradient(), MaxGradient.ToGradient()),
                    2 => new ParticleSystem.MinMaxGradient(MinColor.ToColor(), MaxColor.ToColor()),
                    _ => new ParticleSystem.MinMaxGradient(MinColor.ToColor()),
                };
            }
        }

        [Serializable]
        private sealed class GradientData
        {
            public int Mode;
            public GradientColorKeyData[] ColorKeys;
            public GradientAlphaKeyData[] AlphaKeys;

            public Gradient ToGradient()
            {
                var gradient = new Gradient();
                var colors = (ColorKeys ?? Array.Empty<GradientColorKeyData>())
                    .Select(key => key.ToColorKey())
                    .ToArray();
                var alphas = (AlphaKeys ?? Array.Empty<GradientAlphaKeyData>())
                    .Select(key => key.ToAlphaKey())
                    .ToArray();
                if (colors.Length < 2)
                    colors = new[]
                    {
                        new GradientColorKey(UnityEngine.Color.white, 0f),
                        new GradientColorKey(UnityEngine.Color.white, 1f),
                    };
                if (alphas.Length < 2)
                    alphas = new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f),
                    };
                gradient.SetKeys(colors, alphas);
                if (Enum.IsDefined(typeof(GradientMode), Mode))
                    gradient.mode = (GradientMode)Mode;
                return gradient;
            }
        }

        [Serializable]
        private sealed class GradientColorKeyData
        {
            public ColorData Color;
            public float Time;

            public GradientColorKey ToColorKey() => new GradientColorKey(Color.ToColor(), Mathf.Clamp01(Time));
        }

        [Serializable]
        private sealed class GradientAlphaKeyData
        {
            public float Alpha;
            public float Time;

            public GradientAlphaKey ToAlphaKey() => new GradientAlphaKey(Mathf.Clamp01(Alpha), Mathf.Clamp01(Time));
        }

        [Serializable]
        private sealed class MinMaxCurveData
        {
            public int MinMaxState;
            public float Scalar;
            public float MinScalar;
            public AnimationCurveData MaxCurve;
            public AnimationCurveData MinCurve;

            public ParticleSystem.MinMaxCurve ToMinMaxCurve()
            {
                // SR4.4's partial schema uses state 3 for two constants when
                // both curve payloads are empty. Unity's enum uses state 3 for
                // two curves, so passing it through unchanged corrupts values
                // such as star (1)'s lifetime 0.4..0.9 and size 0.15..0.33.
                if (MinMaxState == 3 && !HasKeys(MaxCurve) && !HasKeys(MinCurve))
                    return new ParticleSystem.MinMaxCurve(MinScalar, Scalar);

                return (ParticleSystemCurveMode)MinMaxState switch
                {
                    ParticleSystemCurveMode.Curve => new ParticleSystem.MinMaxCurve(Scalar, MaxCurve?.ToAnimationCurve()),
                    ParticleSystemCurveMode.TwoCurves => new ParticleSystem.MinMaxCurve(Scalar, MinCurve?.ToAnimationCurve(), MaxCurve?.ToAnimationCurve()),
                    ParticleSystemCurveMode.TwoConstants => new ParticleSystem.MinMaxCurve(MinScalar, Scalar),
                    _ => new ParticleSystem.MinMaxCurve(Scalar),
                };
            }

            private static bool HasKeys(AnimationCurveData curve) =>
                curve?.Keys is { Length: > 0 } || curve?.SerializedKeys is { Length: > 0 };
        }

        [Serializable]
        private sealed class AnimationCurveData
        {
            public CurveKeyData[] Keys;
            [JsonProperty("m_Curve")] public CurveKeyData[] SerializedKeys;
            public int PreInfinity;
            public int PostInfinity;
            [JsonProperty("m_PreInfinity")] public int SerializedPreInfinity;
            [JsonProperty("m_PostInfinity")] public int SerializedPostInfinity;

            public AnimationCurve ToAnimationCurve()
            {
                var keys = Keys is { Length: > 0 } ? Keys : SerializedKeys;
                var curve = new AnimationCurve((keys ?? Array.Empty<CurveKeyData>()).Select(key => key.ToKeyframe()).ToArray());
                var preInfinity = Keys is { Length: > 0 } ? PreInfinity : SerializedPreInfinity;
                var postInfinity = Keys is { Length: > 0 } ? PostInfinity : SerializedPostInfinity;
                if (Enum.IsDefined(typeof(WrapMode), preInfinity))
                    curve.preWrapMode = (WrapMode)preInfinity;
                if (Enum.IsDefined(typeof(WrapMode), postInfinity))
                    curve.postWrapMode = (WrapMode)postInfinity;
                return curve;
            }
        }

        [Serializable]
        private sealed class CurveKeyData
        {
            public float Time;
            public float Value;
            public float InSlope;
            public float OutSlope;
            public int WeightedMode;
            public float InWeight;
            public float OutWeight;

            public Keyframe ToKeyframe()
            {
                return new Keyframe(Time, Value, InSlope, OutSlope, InWeight, OutWeight)
                {
                    weightedMode = (WeightedMode)Mathf.Clamp(WeightedMode, 0, 3),
                };
            }
        }

        private sealed class ImportSource : IDisposable
        {
            private readonly string directory;
            private readonly ZipArchive archive;

            public Manifest Manifest { get; }

            public ImportSource(string path)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("SR prefab package was not found.", path);

                if (string.Equals(Path.GetExtension(path), ".srprefab", StringComparison.OrdinalIgnoreCase))
                {
                    archive = ZipFile.OpenRead(path);
                    Manifest = ReadEntry<Manifest>("manifest.json");
                }
                else
                {
                    directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
                    Manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                }
            }

            public T Read<T>(string relativePath)
            {
                return JsonUtility.FromJson<T>(ReadText(relativePath));
            }

            public string ReadText(string relativePath)
            {
                if (archive != null)
                {
                    var entry = archive.GetEntry(relativePath.Replace('\\', '/')) ??
                                throw new InvalidDataException($"Package entry '{relativePath}' was not found.");
                    using var reader = new StreamReader(entry.Open());
                    return reader.ReadToEnd();
                }

                var path = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    throw new FileNotFoundException("SR prefab component data was not found.", path);
                return File.ReadAllText(path);
            }

            public byte[] ReadBytes(string relativePath)
            {
                if (archive != null)
                {
                    var entry = archive.GetEntry(relativePath.Replace('\\', '/')) ??
                                throw new InvalidDataException($"Package entry '{relativePath}' was not found.");
                    using var input = entry.Open();
                    using var output = new MemoryStream();
                    input.CopyTo(output);
                    return output.ToArray();
                }
                return File.ReadAllBytes(Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            public void Dispose() => archive?.Dispose();

            private T ReadEntry<T>(string relativePath)
            {
                var entry = archive.GetEntry(relativePath.Replace('\\', '/')) ??
                            throw new InvalidDataException($"Package entry '{relativePath}' was not found.");
                using var reader = new StreamReader(entry.Open());
                return JsonUtility.FromJson<T>(reader.ReadToEnd());
            }
        }

        private const string ReconstructedParticleShader = @"Shader ""SR/Reconstructed Particle""
{
    Properties
    {
        _MainTex (""Main Texture"", 2D) = ""white"" {}
        _MaskTex (""Mask Texture"", 2D) = ""white"" {}
        _NoiseTex (""Noise Texture"", 2D) = ""gray"" {}
        _DisTex (""Dissolve Texture"", 2D) = ""white"" {}
        _DissolveMap (""Dissolve Map Alias"", 2D) = ""white"" {}
        _DistortionTex (""Distortion Texture"", 2D) = ""gray"" {}
        _MainColor (""Main Color"", Color) = (1,1,1,1)
        [HideInInspector] _RdcColorChannelMode (""RDC Color Channel Mode"", Float) = 0
        _Fresnel (""Fresnel Parameters"", Vector) = (0,0,0,0)
        _Fresnel2 (""Fresnel Parameters 2"", Vector) = (0,0,0,0)
        _FresnelColor (""Fresnel Color"", Color) = (0,0,0,0)
        _FresnelColor2 (""Fresnel Color 2"", Color) = (0,0,0,0)
        _FresnelColorStrength (""Fresnel Color Strength"", Float) = 0
        _ParallaxScale (""Parallax Scale"", Float) = 0
        _OutlineWidth (""Outline Width"", Float) = 0
        _Opacity (""Opacity"", Range(0,1)) = 1
        _IgnoreMainTexAlpha (""Ignore MainTex Alpha"", Float) = 0
        _VertexColor (""Vertex Color"", Float) = 1
        _VertexColorFallback (""Vertex Color Fallback"", Color) = (1,1,1,1)
        _InsideColor (""Inside Color"", Color) = (1,1,1,1)
        _OutSideColor (""Outside Color"", Color) = (1,1,1,1)
        _MainColorScale (""Main Color Scale"", Float) = 1
        _EmissionIntensity (""Emission Intensity"", Float) = 1
        _AlwaysOnTop (""Always On Top"", Float) = 0
        _CL (""CL"", Float) = 0
        _CL2 (""CL2"", Float) = 0
        _CL3 (""CL3"", Float) = 0
        _CL4 (""CL4"", Float) = 0
        _EffectUVSet (""Effect UV Set"", Float) = 0
        _MainTexUvSet (""Main Texture UV Set"", Float) = 0
        [HideInInspector] _PackedUV (""Packed Main/Mask UV"", Float) = 0
        _ScrPosScale (""Screen Position Scale"", Float) = 0
        _ScrPosTex (""Screen Position Texture"", Float) = 0
        _ScrPosXYOffset (""Screen Position Offset"", Float) = 0
        _CustomData (""Custom Data"", Float) = 0
        _CustomDstBlend (""Custom Dst Blend"", Float) = 0
        _CustomSrcBlend (""Custom Src Blend"", Float) = 0
        _DecalClip (""Decal Clip"", Float) = 0
        [HideInInspector] _DecalHasMask (""Decal Has Mask"", Float) = 0
        _DECALMASK (""Decal Mask"", Float) = 0
        _DECALNOISE (""Decal Noise"", Float) = 0
        _DissolveDistortionIntensity (""Dissolve Distortion Intensity"", Float) = 0
        _DissolveOutlineOffset (""Dissolve Outline Offset"", Float) = 0
        _DissolveOutlineSize1 (""Dissolve Outline Size 1"", Float) = 0
        _DissolveOutlineSize2 (""Dissolve Outline Size 2"", Float) = 0
        _DissolveType (""Dissolve Type"", Float) = 0
        _DisTexG (""Dissolve Texture G"", Float) = 0
        _FragmentClip (""Fragment Clip"", Float) = 0
        _IsPerParticle (""Per Particle"", Float) = 0
        _MASKCHANEL (""Mask Channel Legacy"", Float) = 0
        _MaskON (""Mask Enabled"", Float) = 0
        _NoiseSwitch (""Noise Switch"", Float) = 0
        _RenderingMode (""Rendering Mode"", Float) = 0
        _Saturate (""Saturate"", Float) = 0
         _SoftNear (""Soft Particle Near"", Float) = 0
         _SoftFar (""Soft Particle Far"", Float) = 0
         [HideInInspector] _AdditionalDepthAlpha (""Additional Depth Alpha"", Range(0,1)) = 0
        _Stencil (""Stencil"", Float) = 0
        _StencilComp (""Stencil Comparison"", Float) = 8
        _TurnOnAnnularUV (""Annular UV"", Float) = 0
        _MainSpeed (""Main Speed"", Vector) = (0,0,0,0)
        _MaskSpeed (""Mask Speed"", Vector) = (0,0,0,0)
        _NoiseSpeed (""Noise Speed"", Vector) = (0,0,0,0)
        _NoiseSpeed2 (""Noise Speed 2"", Vector) = (0,0,0,0)
        _NoiseSpeedG (""Noise Speed G"", Vector) = (0,0,0,0)
        _DisGSpeed (""Dissolve G Speed"", Vector) = (0,0,0,0)
        _DisRSpeed (""Dissolve R Speed"", Vector) = (0,0,0,0)
        _DisStep (""Dissolve Step"", Vector) = (0,0,0,0)
        _DissolveUVSpeed (""Dissolve UV Speed"", Vector) = (0,0,0,0)
        _CustomUV (""Custom UV"", Vector) = (0,0,0,0)
        _MainChannel (""Main Channel"", Vector) = (1,0,0,0)
        _MainChannelRGB (""Main Channel RGB"", Vector) = (1,1,1,0)
        _MaskChannel (""Mask Channel"", Vector) = (1,0,0,0)
        _MaskUVoffset (""Mask UV Offset"", Vector) = (0,0,0,0)
        _NoiseMask (""Noise Mask"", Vector) = (1,0,1,1)
        _DissolveOutlineColor1 (""Dissolve Outline Color 1"", Color) = (1,1,1,1)
        _DissolveOutlineColor2 (""Dissolve Outline Color 2"", Color) = (1,1,1,1)
        _DissolveOutlineSmoothStep (""Dissolve Outline Smooth Step"", Vector) = (0.1,0,0,0)
        _MidColor (""Mid Color"", Color) = (1,1,1,1)
        _Mid (""Dissolve Mid"", Range(0,1)) = 0
        _SmoothStep (""Dissolve Smoothness"", Vector) = (0.1,0,0,0)
        _EnableClip (""Enable Clip"", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend (""Src Blend"", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend (""Dst Blend"", Float) = 10
        [Enum(Off,0,On,1)] _ZWrite (""ZWrite"", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull (""Cull"", Float) = 0
    }
    SubShader
    {
        Tags { ""Queue""=""Transparent"" ""RenderType""=""Transparent"" ""RenderPipeline""=""UniversalPipeline"" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]
        Pass
        {
            Name ""SRReconstructedParticle""
            Tags { ""LightMode""=""UniversalForward"" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _CL2_X _CL2_RG _CL2_RGB
            #pragma shader_feature_local_fragment _CL3_OFF _CL3_TEX _CL3_VER
            #pragma shader_feature_local_fragment _FresnelON
            #pragma shader_feature_local_fragment _CL2_OFF
            #pragma shader_feature_local_fragment _SoftParticle
            #pragma shader_feature_local_fragment _DisTexGON
            #pragma shader_feature_local_fragment _Noise
            #pragma shader_feature_local_fragment CUSTOMDATA
            #pragma shader_feature_local_fragment DISTORTION _TEXTURE_TOP
            #pragma shader_feature_local_fragment ISPARTICLE_ON
            #pragma shader_feature_local_fragment _DECALNOISE_RG
            #pragma shader_feature_local_fragment _ENABLE_DECAL_CLIP
            #pragma shader_feature_local_fragment _FRAGMENT_CLIP
            #pragma shader_feature_local_fragment PER_PARTICLE_ON
            #pragma shader_feature_local_fragment _USINGDITHERALPAH
            #pragma shader_feature_local_fragment SR_MASK_R SR_MASK_RG
            #pragma shader_feature_local_fragment SR_MASK_ON
            #pragma shader_feature_local_fragment SR_CL4
            #pragma shader_feature_local_fragment SR_NOISE_ON
            #pragma shader_feature_local_fragment SR_DISSOLVE_G
            #pragma shader_feature_local_fragment SR_PER_PARTICLE
            #pragma shader_feature_local_fragment SR_IGNORE_MAIN_ALPHA
            #pragma shader_feature_local_fragment SR_VERTEX_COLOR
            #pragma shader_feature_local_fragment SR_SATURATE
             #pragma shader_feature_local_fragment SR_CLIP
             #pragma shader_feature_local_fragment SR_SOFT_DEPTH
            #include ""UnityCG.cginc""
            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;
                float4 packedUvOrCustom0 : TEXCOORD1;
                float3 normal : NORMAL;
                float4 custom1 : TEXCOORD2;
                float3 custom2 : TEXCOORD3;
            };
            struct v2f
            {
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;
                float4 packedUvOrCustom0 : TEXCOORD1;
                float4 custom1 : TEXCOORD2;
                float3 custom2 : TEXCOORD3;
                float2 customUv : TEXCOORD4;
                float4 position : SV_POSITION;
                float3 worldNormal : TEXCOORD5;
                float3 worldPosition : TEXCOORD6;
                float4 screenPosition : TEXCOORD7;
            };
            sampler2D _MainTex, _MaskTex, _NoiseTex, _DisTex, _DistortionTex;
            float4 _MainTex_ST, _MaskTex_ST, _NoiseTex_ST, _DisTex_ST;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _MainColor, _VertexColorFallback, _InsideColor, _OutSideColor;
            float4 _Fresnel, _FresnelColor;
            float4 _MainSpeed, _MaskSpeed, _NoiseSpeed, _NoiseSpeed2, _NoiseSpeedG;
            float4 _DisGSpeed, _DisRSpeed, _DisStep, _DissolveUVSpeed, _CustomUV, _MainChannel, _MainChannelRGB;
            float4 _MaskChannel, _MaskUVoffset, _NoiseMask, _SmoothStep;
            float4 _DissolveOutlineColor1, _DissolveOutlineColor2, _DissolveOutlineSmoothStep;
            float _MainColorScale, _EmissionIntensity, _Opacity, _IgnoreMainTexAlpha, _VertexColor, _MaskON, _NoiseSwitch, _Saturate, _PackedUV, _RdcColorChannelMode;
            float _DisTexG, _IsPerParticle, _CL, _CL4, _MASKCHANEL, _DECALMASK, _Mid, _EnableClip, _DecalClip, _DecalHasMask;
            float _DissolveDistortionIntensity, _DissolveOutlineOffset, _DissolveOutlineSize1, _DissolveOutlineSize2, _DissolveType, _FragmentClip;
             float _FresnelColorStrength, _ParallaxScale, _OutlineWidth, _SoftNear, _SoftFar, _AdditionalDepthAlpha;
            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.screenPosition = ComputeScreenPos(output.position);
                output.screenPosition.z = -UnityObjectToViewPos(input.vertex).z;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                // Match the captured SR vertex layout: original VS output
                // Location(1).xy is sourced from input UV.xy, while its .zw
                // is a separate mask/custom coordinate.
                output.uv = float4(input.uv.xy, input.packedUvOrCustom0.zw);
                output.color = input.color;
                output.packedUvOrCustom0 = input.packedUvOrCustom0;
                output.customUv = input.uv.xy;
                output.custom1 = input.custom1;
                output.custom2 = input.custom2;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                float packedUvEnabled = step(0.5, _PackedUV);
                // RenderDoc EID 6026/6028/6030/6032/6034 shows that the chess
                // pixel shader samples its main texture from the vertex shader's
                // output.xy. For this draw that output is sourced from the
                // original TEXCOORD0.xy, not TEXCOORD1.xy. TEXCOORD1 is a
                // separate particle/custom stream and must not replace main UV.
                float2 mainBaseUv = input.uv.xy;
                // The original vertex shader derives mask.zw from particle
                // custom data. Keep the raw TEXCOORD1 fallback only for masks;
                // the chess material currently disables that branch.
                float2 maskBaseUv = lerp(input.uv.zw, input.packedUvOrCustom0.zw, packedUvEnabled);
                float2 mainUv = TRANSFORM_TEX(mainBaseUv, _MainTex) + _MainSpeed.xy * _Time.y;
                float2 maskUv = TRANSFORM_TEX(maskBaseUv, _MaskTex) + _MaskUVoffset.xy + _MaskSpeed.xy * _Time.y;
                float2 noiseUv = TRANSFORM_TEX(input.uv, _NoiseTex) + _NoiseSpeed.xy * _Time.y;
                float2 dissolveUv = TRANSFORM_TEX(input.uv, _DisTex) + _DissolveUVSpeed.xy * _Time.y;
                fixed4 mainSample = tex2D(_MainTex, mainUv);
                fixed4 maskSample = tex2D(_MaskTex, maskUv);
                fixed4 noiseSample = tex2D(_NoiseTex, noiseUv);
                fixed4 dissolveSample = tex2D(_DisTex, dissolveUv);

                // _MainChannel and _MainChannelRGB are independent selectors for
                // the alpha and RGB paths in SR's packed effect textures.
                float channelMode = clamp(round(_CL), 0.0, 2.0);
                float useChannel = step(0.5, channelMode);
                float rgbChannel = dot(mainSample, _MainChannelRGB);
                float mainChannel = dot(mainSample, _MainChannel);
                float cl4Enabled = step(0.5, _CL4);
                float3 mainRgb = lerp(mainSample.rgb, rgbChannel.xxx, useChannel);
                mainRgb = lerp(mainRgb, mainChannel.xxx, cl4Enabled);
#if defined(SR_CL4)
                // CL4 source variants use the packed channel as their visible
                // intensity. Keep this isolated from the legacy _CL path.
                mainRgb = mainChannel.xxx;
#endif
                // EIDs 6026-6034 use the captured SR path where the sampled
                // RGB is lifted by a runtime scalar and opacity is derived
                // from a material channel vector. This is opt-in because the
                // same reconstructed family is also used by other effects.
                if (_RdcColorChannelMode > 0.5)
                {
                    // The CL4 chess variant keeps the sampled RGB intact;
                    // only its opacity comes from the packed channel vector.
                    mainRgb = mainSample.rgb;
                    mainChannel = dot(mainSample.rgb, _MainChannel.rgb);
                }
                // OneChannel variants store the visible shape in the selected
                // texture channel. The source alpha is commonly constant 1 for
                // packed SR effect textures, so using it as opacity draws the
                // whole quad and exposes the packed RGB background.
                float channelAlpha = lerp(mainSample.a, mainChannel, useChannel);
                // The captured chess PS keeps sampled RGB intact and derives
                // opacity from its material channel vector. _CL2_X is not a
                // red-channel alpha mask for this variant.
                // CL4 materials use the packed main texture as an opacity mask
                // even when the legacy _CL selector is disabled. Their source
                // textures commonly have constant alpha and carry the shape in
                // the channels selected by _MainChannel.
                channelAlpha = lerp(channelAlpha, saturate(mainChannel), cl4Enabled);
                // Match the RDC path: a populated material channel vector also
                // drives opacity when the legacy _CL selector is disabled.
                float hasMainChannel = step(0.0001, dot(abs(_MainChannel), float4(1, 1, 1, 1)));
                channelAlpha = lerp(channelAlpha, saturate(mainChannel), hasMainChannel);
#if defined(SR_CL4)
                    channelAlpha = saturate(mainChannel);
#endif
                float mask = 1.0;
#if defined(SR_MASK_ON)
                mask = dot(maskSample, _MaskChannel);
#if defined(SR_MASK_RG)
                    mask = max(maskSample.r, maskSample.g);
#elif defined(SR_MASK_R)
                    mask = maskSample.r;
#endif
#endif
                float noise = 1.0;
#if defined(SR_NOISE_ON)
                noise = dot(noiseSample, _NoiseMask);
#if defined(_DECALNOISE_RG)
                noise = max(noiseSample.r, noiseSample.g);
#endif
#endif
                float dissolveR = dissolveSample.r + _DisRSpeed.x * _Time.y;
                float dissolveG = dissolveSample.g + _DisGSpeed.x * _Time.y;
                float dissolve = dissolveR;
#if defined(SR_DISSOLVE_G)
                dissolve = dissolveG;
#endif
                float particleScale = 1.0;
#if defined(SR_PER_PARTICLE)
                particleScale = max(input.packedUvOrCustom0.x, 0.0);
#endif
                float mainAlpha = channelAlpha;
#if defined(SR_IGNORE_MAIN_ALPHA)
                mainAlpha = 1.0;
#endif
                // SR stores this choice as a material scalar, not as a shader keyword.
                // Keep the particle-system color/alpha when the source enables it.
                fixed4 vertexColor = lerp(_VertexColorFallback, input.color, step(0.5, _VertexColor));
                if (_RdcColorChannelMode > 0.5)
                    vertexColor = fixed4(1, 1, 1, 1);
                // RenderDoc EID 6026 shows the source VS output color as
                // input COLOR * (2, 2, 2, _MainColor.a * 10). The alpha is
                // not a second direct multiplication by _MainColor.a.
                float vertexRgbScale = max(_MainColorScale + _EmissionIntensity, 1.0);
                float vertexAlphaScale = 1.0;
                if (_MainColor.a > 0.0001 && _MainColor.a < 0.9)
                    vertexAlphaScale = _MainColor.a * 10.0;
                vertexColor.rgb *= vertexRgbScale;
                vertexColor.a *= vertexAlphaScale;
                fixed4 color = fixed4(mainRgb, mainAlpha) * vertexColor;
                color.rgb *= _MainColor.rgb;
                color.a *= _Opacity;
#if defined(_SoftParticle) || defined(SR_SOFT_DEPTH)
                // SR soft-particle variants fade against the scene depth. Only
                // use the path when the source supplied a valid range; several
                // older materials carry the keyword but no distance values.
                if (_SoftFar > _SoftNear)
                {
                    float sceneDepth = LinearEyeDepth(
                        SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(input.screenPosition)));
                    float softRange = max(_SoftFar - _SoftNear, 0.0001);
                    float depthFade = saturate((sceneDepth - input.screenPosition.z - _SoftNear) / softRange);
                    color.a *= lerp(1.0, depthFade, saturate(_AdditionalDepthAlpha));
                }
#endif
#if defined(SR_SATURATE)
                color.rgb = saturate(color.rgb);
#endif
                color.rgb *= particleScale * max(_MainColorScale * _EmissionIntensity, 0.0001);
                color.a *= mask;
#if defined(SR_CLIP)
#if defined(_ENABLE_DECAL_CLIP)
                float decalMaskValue = dot(maskSample, _MaskChannel);
                float decalClipValue = lerp(mainChannel, decalMaskValue, step(0.5, _DecalHasMask));
                clip(decalClipValue - _DecalClip);
#endif
#if defined(_FRAGMENT_CLIP)
                clip(saturate(dissolve + noise * _SmoothStep.x) - _Mid);
#elif !defined(_ENABLE_DECAL_CLIP)
                clip(saturate(dissolve + noise * _SmoothStep.x) - _Mid);
#endif
#endif
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name ""DepthOnly""
            Tags
            {
                ""LightMode"" = ""DepthOnly""
            }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl""
            #include ""Assets/unity-shadertoy-validation/Common/Shaders/ShadertoyDepthOnlyPass.hlsl""
            ENDHLSL
        }
    }
}";
    }
}

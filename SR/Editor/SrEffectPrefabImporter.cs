using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
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
            var outputPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{DefaultOutputFolder}/{SanitizeFileName(manifest.Name)}.prefab");
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
            foreach (var package in packages)
            {
                using var source = new ImportSource(package);
                var outputPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{outputRoot}/{SanitizeFileName(source.Manifest.Name)}.prefab");
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
            var warnings = new List<string>();
            var derivedRoot = GetDerivedRoot(outputAssetPath, manifest.Name);
            if (AssetDatabase.IsValidFolder(derivedRoot))
                AssetDatabase.DeleteAsset(derivedRoot);
            var meshes = CreateMeshes(manifest, derivedRoot);
            var materials = CreateMaterials(source, manifest, outputAssetPath, warnings);
            var lightCount = 0;
            var particleSystemCount = 0;
            var particleRendererCount = 0;
            var animatorCount = 0;

            foreach (var node in nodes)
            {
                var gameObject = new GameObject(node.Name, GetNativeComponentTypes(node));
                objects.Add(node.Path, gameObject);
                var parentPath = ParentPath(node.Path);
                if (!string.IsNullOrEmpty(parentPath))
                {
                    if (!objects.TryGetValue(parentPath, out var parent))
                        throw new InvalidDataException($"Missing parent node '{parentPath}' for '{node.Path}'.");
                    gameObject.transform.SetParent(parent.transform, false);
                }

                foreach (var component in node.Components ?? Array.Empty<ManifestComponent>())
                {
                    if (component.Type == "Transform" && !string.IsNullOrEmpty(component.ParametersFile))
                        ApplyTransform(gameObject.transform, source.Read<TransformData>(component.ParametersFile));
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
                            ApplyNativeSerializedData(particleSystem, source.ReadText(component.ParametersFile), node.Path, warnings);
                        else
                        {
                            var particleJson = source.ReadText(component.ParametersFile);
                            ApplyParticleSystem(particleSystem, JsonUtility.FromJson<ParticleSystemData>(particleJson));
                            ApplySerializedColorLifetimeFallback(particleSystem, particleJson);
                        }
                        EnsureParticleSystemDefaults(particleSystem);
                        particleSystemCount++;
                    }
                    else if (component.Type == "ParticleSystemRenderer")
                    {
                        var particleSystem = gameObject.GetComponent<ParticleSystem>() ??
                                             throw new InvalidOperationException($"Failed to create ParticleSystemRenderer on '{node.Path}'.");
                        var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                        if (component.ParametersStatus == "external-type-tree-exported" && !string.IsNullOrEmpty(component.ParametersFile))
                        {
                            var rendererJson = source.ReadText(component.ParametersFile);
                            ApplyNativeSerializedData(renderer, rendererJson, node.Path, warnings);
                            ApplyParticleRendererVertexStreams(renderer, rendererJson);
                        }
                        if (component.ParticleRenderer != null)
                        {
                            renderer.enabled = component.ParticleRenderer.Enabled;
                            var rendererMaterials = (component.ParticleRenderer.MaterialPointers ?? Array.Empty<PointerInfo>())
                                .Select(pointer => FindMaterial(materials, pointer))
                                .ToArray();
                            renderer.sharedMaterials = rendererMaterials;
                            if (rendererMaterials.Length == 0 || rendererMaterials.All(material => material == null))
                                renderer.enabled = false;
                            ApplyParticleRenderer(renderer, component.ParticleRenderer, meshes);
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
                    else if (component.MonoBehaviour != null && component.MonoBehaviour.ClassName == "CustomAdditionalLightData")
                        warnings.Add($"{node.Path}: CustomAdditionalLightData is preserved in {component.ParametersFile}, but its SR runtime behavior is not reconstructed.");
                }
            }

            NormalizeParticleLights(objects, warnings);

            var root = objects[nodes[0].Path];
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

        private static void ApplyParticleRenderer(ParticleSystemRenderer renderer, ParticleRendererInfo info, Dictionary<string, Mesh> meshes)
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
                if (info.RenderAlignment >= 0 && info.RenderAlignment <= (int)ParticleSystemRenderSpace.World)
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
            renderer.mesh = (info.MeshPointers ?? Array.Empty<PointerInfo>())
                .Select(pointer => FindMesh(meshes, pointer))
                .FirstOrDefault(mesh => mesh != null);
        }

        private static Mesh FindMesh(Dictionary<string, Mesh> meshes, PointerInfo pointer)
        {
            if (pointer == null || pointer.PathID == 0)
                return null;
            if (meshes.TryGetValue(MaterialKey(pointer.SourceCAB, pointer.PathID), out var exact))
                return exact;
            return meshes.FirstOrDefault(pair => pair.Key.EndsWith($":{pointer.PathID}", StringComparison.Ordinal)).Value;
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
                    if (string.IsNullOrEmpty(property.PackageEntry) || !material.HasProperty(property.Name))
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
                    material.SetTexture(property.Name, texture);
                    material.SetTextureScale(property.Name, property.Scale.ToVector2());
                    material.SetTextureOffset(property.Name, property.Offset.ToVector2());
                }
                material.shaderKeywords = SplitKeywords(info.ShaderKeywords);
                var materialPath = $"{materialFolder}/{SanitizeFileName(info.Name)}_{info.PathID}.mat";
                AssetDatabase.CreateAsset(material, materialPath);
                result[MaterialKey(info.SourceCAB, info.PathID)] = material;
                if (string.IsNullOrEmpty(info.ShaderName))
                    warnings.Add($"{info.Name}: original shader name unresolved ({info.ShaderSourceCAB}:{info.ShaderPathID}); reconstructed shader applied.");
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
            Decal
        }

        private static ShaderFamily ClassifyShaderFamily(ManifestMaterial info)
        {
            if (info == null)
                return ShaderFamily.Generic;

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

            if (HasVector(info, "_MainSpeed") || HasVector(info, "_MaskSpeed") ||
                HasVector(info, "_NoiseSpeed") || HasVector(info, "_CustomUV") ||
                (info.Name?.IndexOf("UVMove", StringComparison.OrdinalIgnoreCase) >= 0))
                return ShaderFamily.UvMove;

            if (HasFloat(info, "_CL") || HasVector(info, "_MainChannel") ||
                (info.Name?.IndexOf("OneChannel", StringComparison.OrdinalIgnoreCase) >= 0))
                return ShaderFamily.OneChannel;

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

        private static string BuildShaderSource(ShaderFamily family)
        {
            var source = ReconstructedParticleShader.Replace(
                "Shader \"SR/Reconstructed Particle\"",
                $"Shader \"SR/Reconstructed Particle/{family}\"");

            if (family == ShaderFamily.OneChannel)
            {
                // SR declares _CL as KeywordEnum(RGBA, R, RA), not a boolean.
                // The original OneChannel shader defaults are _MainChannel=(1,0,0,1)
                // and _MainChannelRGB=(1,0,0,0). Preserve them when a material omits
                // either property.
                source = source.Replace(
                    "_MainChannel (\"Main Channel\", Vector) = (1,0,0,0)",
                    "_MainChannel (\"Main Channel\", Vector) = (1,0,0,1)");
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
                // Decal materials use the mask texture as the clip source;
                // _DecalClip was previously only serialized, not consumed.
                source = source.Replace(
                    "if (_EnableClip > 0.5) clip(saturate(dissolve + noise * _SmoothStep.x) - _Mid);",
                    "if (_EnableClip > 0.5)\n                {\n                    float decalClipValue = dot(maskSample, _MaskChannel);\n                    clip(decalClipValue - _DecalClip);\n                }");
            }

            return source;
        }

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

        private static string GetDerivedRoot(string outputAssetPath, string prefabName) =>
            $"{Path.GetDirectoryName(outputAssetPath)?.Replace('\\', '/')}/{SanitizeFileName(prefabName)}_Assets";

        private static string[] SplitKeywords(string value) => (value ?? string.Empty)
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private static Type[] GetNativeComponentTypes(ManifestNode node)
        {
            var componentTypes = new List<Type>();
            var components = node.Components ?? Array.Empty<ManifestComponent>();
            if (components.Any(component => component.Type == "ParticleSystem" || component.Type == "ParticleSystemRenderer"))
                componentTypes.Add(typeof(ParticleSystem));
            if (components.Any(component => component.Type == "Animator" && component.ParametersStatus != "not-required"))
                componentTypes.Add(typeof(Animator));
            if (components.Any(component => component.Type == "Light"))
                componentTypes.Add(typeof(Light));
            return componentTypes.ToArray();
        }

        private static void ApplyTransform(Transform transform, TransformData data)
        {
            transform.localPosition = data.LocalPosition.ToVector3();
            transform.localRotation = data.LocalRotation.ToQuaternion();
            transform.localScale = data.LocalScale.ToVector3();
        }

        private static void ApplyLight(Light light, LightData data)
        {
            light.enabled = data.Enabled;
            if (data.Type >= (int)LightType.Spot && data.Type <= (int)LightType.Disc)
                light.type = (LightType)data.Type;
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
            if (data.RandomSeed != 0)
                particleSystem.randomSeed = data.RandomSeed;
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
            public ManifestComponent[] Components;
        }

        [Serializable]
        private sealed class ManifestComponent
        {
            public string Type;
            public string ParametersStatus;
            public string ParametersFile;
            public MonoBehaviourInfo MonoBehaviour;
            public ParticleRendererInfo ParticleRenderer;
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
            public float[] Vertices; public float[] Normals; public float[] Tangents; public float[] Colors; public float[] UV0;
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
            public uint RandomSeed;
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
                if (MinMaxState == 3 &&
                    (MaxCurve?.Keys == null || MaxCurve.Keys.Length == 0) &&
                    (MinCurve?.Keys == null || MinCurve.Keys.Length == 0))
                    return new ParticleSystem.MinMaxCurve(MinScalar, Scalar);

                return (ParticleSystemCurveMode)MinMaxState switch
                {
                    ParticleSystemCurveMode.Curve => new ParticleSystem.MinMaxCurve(Scalar, MaxCurve?.ToAnimationCurve()),
                    ParticleSystemCurveMode.TwoCurves => new ParticleSystem.MinMaxCurve(Scalar, MinCurve?.ToAnimationCurve(), MaxCurve?.ToAnimationCurve()),
                    ParticleSystemCurveMode.TwoConstants => new ParticleSystem.MinMaxCurve(MinScalar, Scalar),
                    _ => new ParticleSystem.MinMaxCurve(Scalar),
                };
            }
        }

        [Serializable]
        private sealed class AnimationCurveData
        {
            public CurveKeyData[] Keys;
            public int PreInfinity;
            public int PostInfinity;

            public AnimationCurve ToAnimationCurve()
            {
                var curve = new AnimationCurve((Keys ?? Array.Empty<CurveKeyData>()).Select(key => key.ToKeyframe()).ToArray());
                if (Enum.IsDefined(typeof(WrapMode), PreInfinity))
                    curve.preWrapMode = (WrapMode)PreInfinity;
                if (Enum.IsDefined(typeof(WrapMode), PostInfinity))
                    curve.postWrapMode = (WrapMode)PostInfinity;
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
        _MainColor (""Main Color"", Color) = (1,1,1,1)
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
        _CustomData (""Custom Data"", Float) = 0
        _CustomDstBlend (""Custom Dst Blend"", Float) = 0
        _CustomSrcBlend (""Custom Src Blend"", Float) = 0
        _DecalClip (""Decal Clip"", Float) = 0
        _DECALMASK (""Decal Mask"", Float) = 0
        _DECALNOISE (""Decal Noise"", Float) = 0
        _DisTexG (""Dissolve Texture G"", Float) = 0
        _IsPerParticle (""Per Particle"", Float) = 0
        _MASKCHANEL (""Mask Channel Legacy"", Float) = 0
        _MaskON (""Mask Enabled"", Float) = 0
        _NoiseSwitch (""Noise Switch"", Float) = 0
        _RenderingMode (""Rendering Mode"", Float) = 0
        _Saturate (""Saturate"", Float) = 0
        _SoftFar (""Soft Particle Far"", Float) = 0
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
        _CustomUV (""Custom UV"", Vector) = (0,0,0,0)
        _MainChannel (""Main Channel"", Vector) = (1,0,0,0)
        _MainChannelRGB (""Main Channel RGB"", Vector) = (1,1,1,0)
        _MaskChannel (""Mask Channel"", Vector) = (1,0,0,0)
        _MaskUVoffset (""Mask UV Offset"", Vector) = (0,0,0,0)
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
            #include ""UnityCG.cginc""
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float4 custom0 : TEXCOORD1;
                float4 custom1 : TEXCOORD2;
                float3 custom2 : TEXCOORD3;
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float4 custom0 : TEXCOORD1;
                float4 custom1 : TEXCOORD2;
                float3 custom2 : TEXCOORD3;
            };
            sampler2D _MainTex, _MaskTex, _NoiseTex, _DisTex;
            float4 _MainTex_ST, _MaskTex_ST, _NoiseTex_ST, _DisTex_ST;
            float4 _MainColor, _VertexColorFallback, _InsideColor, _OutSideColor;
            float4 _MainSpeed, _MaskSpeed, _NoiseSpeed, _NoiseSpeed2, _NoiseSpeedG;
            float4 _DisGSpeed, _DisRSpeed, _DisStep, _CustomUV, _MainChannel, _MainChannelRGB;
            float4 _MaskChannel, _MaskUVoffset, _SmoothStep;
            float _MainColorScale, _EmissionIntensity, _Opacity, _IgnoreMainTexAlpha, _VertexColor, _MaskON, _NoiseSwitch, _Saturate;
            float _DisTexG, _IsPerParticle, _CL, _Mid, _EnableClip, _DecalClip;
            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                // Keep the interpolated coordinate in the mesh's original UV space.
                // Each texture must apply its own ST transform in the fragment stage;
                // applying _MainTex_ST here would make the mask/noise/dissolve scales
                // inherit the main texture's scale and offset as well.
                output.uv = input.uv;
                output.color = input.color;
                output.custom0 = input.custom0;
                output.custom1 = input.custom1;
                output.custom2 = input.custom2;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                float2 mainUv = TRANSFORM_TEX(input.uv, _MainTex) + _MainSpeed.xy * _Time.y;
                float2 maskUv = TRANSFORM_TEX(input.uv, _MaskTex) + _MaskUVoffset.xy + _MaskSpeed.xy * _Time.y;
                float2 noiseUv = TRANSFORM_TEX(input.uv, _NoiseTex) + _NoiseSpeed.xy * _Time.y;
                float2 dissolveUv = TRANSFORM_TEX(input.uv, _DisTex);
                fixed4 mainSample = tex2D(_MainTex, mainUv);
                fixed4 maskSample = tex2D(_MaskTex, maskUv);
                fixed4 noiseSample = tex2D(_NoiseTex, noiseUv);
                fixed4 dissolveSample = tex2D(_DisTex, dissolveUv);

                // _CL is SR's Channel Mapping enum: RGBA=0, R=1, RA=2.
                // It is shared by the particle shader families, so keep the
                // mapping in the common fragment path instead of one family branch.
                float channelMode = clamp(round(_CL), 0.0, 2.0);
                float useChannel = step(0.5, channelMode);
                float isRedOnly = useChannel * (1.0 - step(1.5, channelMode));
                float mainChannel = dot(mainSample, _MainChannel);
                float3 mainRgb = lerp(mainSample.rgb, mainChannel.xxx, useChannel);
                float hasRgbRemap = step(0.001, dot(abs(_MainChannelRGB.rgb), 1.0.xxx));
                float3 rgbRemap = lerp(1.0.xxx, _MainChannelRGB.rgb, hasRgbRemap);
                mainRgb *= lerp(1.0.xxx, rgbRemap, useChannel);
                float mask = dot(maskSample, _MaskChannel);
                mask = lerp(1.0, mask, step(0.5, _MaskON));
                float noise = dot(noiseSample, _MaskChannel);
                noise = lerp(1.0, noise, step(0.5, _NoiseSwitch));
                float dissolveR = dissolveSample.r + _DisRSpeed.x * _Time.y;
                float dissolveG = dissolveSample.g + _DisGSpeed.x * _Time.y;
                float dissolve = lerp(dissolveR, dissolveG, step(0.5, _DisTexG));
                float particleScale = lerp(1.0, max(input.custom0.x, 0.0), step(0.5, _IsPerParticle));
                float channelAlpha = lerp(mainSample.a, 1.0, isRedOnly);
                float mainAlpha = lerp(channelAlpha, 1.0, step(0.5, _IgnoreMainTexAlpha));
                fixed4 vertexColor = lerp(_VertexColorFallback, input.color, step(0.5, _VertexColor));
                fixed4 color = fixed4(mainRgb, mainAlpha) * vertexColor * _MainColor;
                color.a *= _Opacity;
                if (_Saturate > 0.5) color.rgb = saturate(color.rgb);
                color.rgb *= particleScale * max(_MainColorScale * _EmissionIntensity, 0.0001);
                color.a *= mask;
                if (_EnableClip > 0.5) clip(saturate(dissolve + noise * _SmoothStep.x) - _Mid);
                return color;
            }
            ENDHLSL
        }
    }
}";
    }
}

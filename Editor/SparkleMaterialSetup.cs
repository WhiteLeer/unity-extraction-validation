using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SparkleMaterialSetup
{
    private const string CharacterRoot = "Assets/unity-extraction-validation/SR/characters/sparkle";
    private const string ModelsRoot = CharacterRoot + "/models";
    private const string TexturesRoot = CharacterRoot + "/textures";
    private const string MaterialsRoot = CharacterRoot + "/materials";
    private const string ShaderName = "HoyoToon/Star Rail/Character";

    [MenuItem("Tools/Codex/Setup Sparkle Materials")]
    public static void Setup()
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"SparkleMaterialSetup: shader not found: {ShaderName}");
            return;
        }

        EnsureFolder("Assets/unity-extraction-validation/SR");
        EnsureFolder("Assets/unity-extraction-validation/SR/characters");
        EnsureFolder(CharacterRoot);
        EnsureFolder(MaterialsRoot);

        var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToDictionary(Path.GetFileNameWithoutExtension, path => AssetDatabase.LoadAssetAtPath<Texture2D>(path), StringComparer.OrdinalIgnoreCase);

        var profiles = BuildProfiles(textures);
        var materialsByKey = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in profiles)
        {
            materialsByKey[pair.Key] = CreateOrUpdateMaterial(pair.Key, shader, pair.Value);
        }

        var fbxPaths = AssetDatabase.FindAssets("t:GameObject", new[] { ModelsRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var fbxPath in fbxPaths)
        {
            RemapModelMaterials(fbxPath, materialsByKey);
        }

        CreateCharacterPrefab(materialsByKey);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"SparkleMaterialSetup: created {materialsByKey.Count} materials and remapped {fbxPaths.Length} FBX assets.");
    }

    private static Dictionary<string, MaterialProfile> BuildProfiles(IReadOnlyDictionary<string, Texture2D> textures)
    {
        return new Dictionary<string, MaterialProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Body"] = new MaterialProfile
            {
                MaterialType = MaterialKind.Base,
                MainTex = GetTexture(textures, "Avatar_Sparkle_00_Body_Color_L"),
                LightMap = GetTexture(textures, "Avatar_Sparkle_00_Body_LightMap_L"),
                WarmRamp = GetTexture(textures, "Avatar_Sparkle_00_Body_Warm_Ramp"),
                CoolRamp = GetTexture(textures, "Avatar_Sparkle_00_Body_Cool_Ramp"),
                MaterialLut = GetTexture(textures, "MaterialIDValuesLUTSparkle_00_Mat_Body"),
            },
            ["Hair"] = new MaterialProfile
            {
                MaterialType = MaterialKind.Hair,
                MainTex = GetTexture(textures, "Avatar_Sparkle_00_Hair_Color"),
                LightMap = GetTexture(textures, "Avatar_Sparkle_00_Hair_LightMap"),
                WarmRamp = GetTexture(textures, "Avatar_Sparkle_00_Hair_Warm_Ramp"),
                CoolRamp = GetTexture(textures, "Avatar_Sparkle_00_Hair_Cool_Ramp"),
                MaterialLut = GetTexture(textures, "MaterialIDValuesLUTSparkle_00_Mat_Body"),
            },
            ["Face"] = new MaterialProfile
            {
                MaterialType = MaterialKind.Face,
                MainTex = GetTexture(textures, "Avatar_Sparkle_00_Face_Color"),
            },
            ["FaceMask"] = new MaterialProfile
            {
                MaterialType = MaterialKind.Base,
                MainTex = GetTexture(textures, "Avatar_Sparkle_00_Face_Color"),
            },
            ["Effect"] = new MaterialProfile
            {
                MaterialType = MaterialKind.Base,
                MainTex = GetTexture(textures, "Avatar_Sparkle_00_Effect_Color"),
                LightMap = GetTexture(textures, "Avatar_Sparkle_00_Effect_LightMap"),
                MaterialLut = GetTexture(textures, "MaterialIDValuesLUTSparkle_00_Mat_Effect"),
            },
            ["Kendama"] = new MaterialProfile
            {
                MaterialType = MaterialKind.Base,
                MainTex = GetTexture(textures, "Avatar_Sparkle_00_Kendama_Color"),
                LightMap = GetTexture(textures, "Avatar_Sparkle_00_Kendama_LightMap"),
                MaterialLut = GetTexture(textures, "MaterialIDValuesLUTSparkle_00_Mat_Kendama"),
                WarmRamp = GetTexture(textures, "Avatar_Sparkle_00_Body_Warm_Ramp"),
                CoolRamp = GetTexture(textures, "Avatar_Sparkle_00_Body_Cool_Ramp"),
            }
        };
    }

    private static Material CreateOrUpdateMaterial(string key, Shader shader, MaterialProfile profile)
    {
        var path = $"{MaterialsRoot}/Sparkle_{key}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        SetMaterialType(material, profile.MaterialType);
        SetTexture(material, "_MainTex", profile.MainTex);
        SetTexture(material, "_LightMap", profile.LightMap);
        SetTexture(material, "_DiffuseRampMultiTex", profile.WarmRamp);
        SetTexture(material, "_DiffuseCoolRampMultiTex", profile.CoolRamp);
        SetTexture(material, "_MaterialValuesPackLUT", profile.MaterialLut);
        SetFloat(material, "_UseMaterialValuesLUT", profile.MaterialLut != null ? 1f : 0f);
        SetColor(material, "_Color", Color.white);
        SetColor(material, "_BackColor", Color.white);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void RemapModelMaterials(string fbxPath, IReadOnlyDictionary<string, Material> materialsByKey)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (prefab == null)
        {
            return;
        }

        var sourceNames = prefab.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Select(material => material.name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var sourceName in sourceNames)
        {
            var key = ResolveMaterialKey(sourceName, fbxPath);
            if (!materialsByKey.TryGetValue(key, out var material))
            {
                material = materialsByKey["Body"];
            }

            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceName), material);
        }

        importer.SaveAndReimport();
        Debug.Log($"SparkleMaterialSetup: remapped {sourceNames.Length} source materials for {fbxPath}");
    }

    private static void CreateCharacterPrefab(IReadOnlyDictionary<string, Material> materialsByKey)
    {
        const string sourceModel = ModelsRoot + "/Avatar_Sparkle_00_Model_Chara/Avatar_Sparkle_00_Model_Chara.fbx";
        const string prefabPath = ModelsRoot + "/Sparkle.prefab";

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourceModel);
        if (source == null)
        {
            Debug.LogWarning($"SparkleMaterialSetup: source model not found: {sourceModel}");
            return;
        }

        var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (instance == null)
        {
            Debug.LogWarning("SparkleMaterialSetup: failed to instantiate source model.");
            return;
        }

        try
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var key = ResolveMaterialKey(renderer.gameObject.name, prefabPath);
                if (!materialsByKey.TryGetValue(key, out var material))
                {
                    material = materialsByKey["Body"];
                }

                var assigned = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                renderer.sharedMaterials = assigned;
            }

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Debug.Log($"SparkleMaterialSetup: created prefab {prefabPath}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static string ResolveMaterialKey(string sourceName, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return ResolveMaterialKeyFromPath(assetPath);
        }

        var normalized = sourceName.Replace(" ", string.Empty);
        if (normalized.IndexOf("Kendama", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Kendama";
        }

        if (normalized.IndexOf("Face_Mask", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("FaceMask", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "FaceMask";
        }

        if (normalized.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Face";
        }

        if (normalized.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Hair";
        }

        if (normalized.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Effect";
        }

        if (string.Equals(normalized, "Lit", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveMaterialKeyFromPath(assetPath);
        }

        return ResolveMaterialKeyFromPath(assetPath);
    }

    private static string ResolveMaterialKeyFromPath(string assetPath)
    {
        var normalizedPath = assetPath.Replace("\\", "/");
        if (normalizedPath.IndexOf("/Face_Mask/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "FaceMask";
        }

        if (normalizedPath.IndexOf("/Face/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Face";
        }

        if (normalizedPath.IndexOf("/Hair/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Hair";
        }

        if (normalizedPath.IndexOf("Kendama", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Kendama";
        }

        if (normalizedPath.IndexOf("/Effect/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalizedPath.IndexOf("Model_Effect", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Effect";
        }

        return "Body";
    }

    private static void SetMaterialType(Material material, MaterialKind kind)
    {
        SetFloat(material, "variant_selector", kind == MaterialKind.Face ? 1f : kind == MaterialKind.Hair ? 3f : 0f);
        SetFloat(material, "_BaseMaterial", kind == MaterialKind.Base ? 1f : 0f);
        SetFloat(material, "_FaceMaterial", kind == MaterialKind.Face ? 1f : 0f);
        SetFloat(material, "_EyeShadowMat", 0f);
        SetFloat(material, "_HairMaterial", kind == MaterialKind.Hair ? 1f : 0f);
        SetFloat(material, "_EnableShadow", 1f);
        SetFloat(material, "_EnableRimLight", 1f);
        SetFloat(material, "_EnableSpecular", 1f);
        SetFloat(material, "_EnableOutline", 1f);
        SetFloat(material, "_HideNPCParts", 1f);
        SetFloat(material, "_MultiLight", 1f);
        SetFloat(material, "_FilterLight", 1f);
        SetFloat(material, "_backfdceuv2", 1f);
        SetFloat(material, "_VertexColor", 1f);

        switch (kind)
        {
            case MaterialKind.Face:
                ApplyVariantState(material, cullMode: 2, srcBlend: 1, dstBlend: 0, sdwRef: 20, sdwPass: 2, sdwComp: 7,
                    sdwColorMask: 0, sdwSrc: 1, sdwDst: 0, sdwZWrite: 1, sdwZTest: 2,
                    stencilPassA: 0, stencilPassB: 2, stencilCompA: 5, stencilCompB: 5, stencilRefA: 100, stencilRefB: 100,
                    renderQueue: 2010);
                break;
            case MaterialKind.Hair:
                ApplyVariantState(material, cullMode: 0, srcBlend: 5, dstBlend: 10, sdwRef: 0, sdwPass: 0, sdwComp: 8,
                    sdwColorMask: 16, sdwSrc: 1, sdwDst: 1, sdwZWrite: 1, sdwZTest: 2,
                    stencilPassA: 2, stencilPassB: 0, stencilCompA: 0, stencilCompB: 0, stencilRefA: 0, stencilRefB: 0,
                    renderQueue: 2000);
                break;
            default:
                ApplyVariantState(material, cullMode: 0, srcBlend: 5, dstBlend: 10, sdwRef: 0, sdwPass: 0, sdwComp: 8,
                    sdwColorMask: 16, sdwSrc: 1, sdwDst: 1, sdwZWrite: 1, sdwZTest: 2,
                    stencilPassA: 2, stencilPassB: 0, stencilCompA: 0, stencilCompB: 0, stencilRefA: 0, stencilRefB: 0,
                    renderQueue: 2000);
                break;
        }
    }

    private static void ApplyVariantState(
        Material material,
        float cullMode,
        float srcBlend,
        float dstBlend,
        float sdwRef,
        float sdwPass,
        float sdwComp,
        float sdwColorMask,
        float sdwSrc,
        float sdwDst,
        float sdwZWrite,
        float sdwZTest,
        float stencilPassA,
        float stencilPassB,
        float stencilCompA,
        float stencilCompB,
        float stencilRefA,
        float stencilRefB,
        int renderQueue)
    {
        SetFloat(material, "_CullMode", cullMode);
        SetFloat(material, "_SrcBlend", srcBlend);
        SetFloat(material, "_DstBlend", dstBlend);
        SetFloat(material, "_sdwRef", sdwRef);
        SetFloat(material, "_sdwPass", sdwPass);
        SetFloat(material, "_sdwComp", sdwComp);
        SetFloat(material, "_sdwColorMask", sdwColorMask);
        SetFloat(material, "_sdwSrc", sdwSrc);
        SetFloat(material, "_sdwDst", sdwDst);
        SetFloat(material, "_sdwZWrite", sdwZWrite);
        SetFloat(material, "_sdwZTest", sdwZTest);
        SetFloat(material, "_StencilPassA", stencilPassA);
        SetFloat(material, "_StencilPassB", stencilPassB);
        SetFloat(material, "_StencilCompA", stencilCompA);
        SetFloat(material, "_StencilCompB", stencilCompB);
        SetFloat(material, "_StencilRefA", stencilRefA);
        SetFloat(material, "_StencilRefB", stencilRefB);
        material.renderQueue = renderQueue;
    }

    private static void SetTexture(Material material, string propertyName, Texture texture)
    {
        if (texture != null && material.HasProperty(propertyName))
        {
            material.SetTexture(propertyName, texture);
        }
    }

    private static void SetFloat(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static void SetColor(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, value);
        }
    }

    private static Texture2D GetTexture(IReadOnlyDictionary<string, Texture2D> textures, string name)
    {
        textures.TryGetValue(name, out var texture);
        return texture;
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        var name = Path.GetFileName(assetPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException($"Invalid folder path: {assetPath}");
        }

        AssetDatabase.CreateFolder(parent, name);
    }

    private sealed class MaterialProfile
    {
        public MaterialKind MaterialType;
        public Texture2D MainTex;
        public Texture2D LightMap;
        public Texture2D WarmRamp;
        public Texture2D CoolRamp;
        public Texture2D MaterialLut;
    }

    private enum MaterialKind
    {
        Base,
        Face,
        Hair
    }
}

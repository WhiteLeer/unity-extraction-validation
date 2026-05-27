using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SRRoleMaterialAutoBinder
{
    private const string LogPrefix = "[SRMaterialBinder]";
    private static readonly string[] ShaderCandidates =
    {
        "Custom/NPR-3/HSRURP",
        "Custom/NPR-3/CharacterAdvancedURP",
        "Universal Render Pipeline/Lit",
        "Standard"
    };

    private static readonly string[] SharedTextureFolders =
    {
        "Assets/SRExtractionValidation/NPR/NPR-Core/Textures/StyleMaps/HSR",
        "Assets/SRExtractionValidation/NPR/NPR-Core/Textures",
        "Assets/SRExtractionValidation/Imported/SR/Shared/Textures"
    };

    private static readonly string[] SemanticBase = { "Color", "BaseMap", "Albedo" };
    private static readonly string[] SemanticLightMap = { "LightMap" };
    private static readonly string[] SemanticFaceShadow = { "FaceMap", "FaceShadow" };
    private static readonly string[] SemanticOutline = { "Outline", "OutlineWidth" };
    private static readonly string[] SemanticWarmRamp = { "Warm_Ramp", "Ramp" };
    private static readonly string[] SemanticCoolRamp = { "Cool_Ramp", "Ramp" };

    private enum RolePart
    {
        Body,
        Face,
        Hair,
        Trans
    }

    private struct HsrParams
    {
        public float FaceOrientationStrength;
        public float HairSpecStrength;
        public float HairSpecExponent1;
        public float HairSpecExponent2;
        public float OutlineWidth;
        public float OutlineWidthMapStrength;
        public float ShadowStrength;
        public float ShadowThreshold;
    }

    public static bool BindRoleTextures(string roleRoot, GameObject modelTarget)
    {
        if (string.IsNullOrEmpty(roleRoot) || !AssetDatabase.IsValidFolder(roleRoot))
        {
            Debug.LogError($"{LogPrefix} Invalid role root: {roleRoot}");
            return false;
        }

        if (modelTarget == null)
        {
            Debug.LogError($"{LogPrefix} MODEL_Target is null. Role={roleRoot}");
            return false;
        }

        var shader = ResolveShader();
        if (shader == null)
        {
            Debug.LogError($"{LogPrefix} No suitable shader found.");
            return false;
        }

        var textureDir = $"{roleRoot}/Textures";
        var materialDir = $"{roleRoot}/Art/Materials";
        EnsureFolder(materialDir);

        var roleName = Path.GetFileName(roleRoot);
        var bodyMat = CreateOrUpdatePartMaterial(roleName, RolePart.Body, shader, materialDir, textureDir);
        var faceMat = CreateOrUpdatePartMaterial(roleName, RolePart.Face, shader, materialDir, textureDir);
        var hairMat = CreateOrUpdatePartMaterial(roleName, RolePart.Hair, shader, materialDir, textureDir);
        var transMat = CreateOrUpdatePartMaterial(roleName, RolePart.Trans, shader, materialDir, textureDir);

        var boundCount = 0;
        foreach (var renderer in modelTarget.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var part = ClassifyPart(renderer.name);
            var targetMaterial = part == RolePart.Face ? faceMat : part == RolePart.Hair ? hairMat : part == RolePart.Trans ? transMat : bodyMat;
            ApplyMaterialToRenderer(renderer, targetMaterial);
            boundCount++;
        }

        AssetDatabase.SaveAssets();
        if (EditorSceneManager.GetActiveScene().IsValid())
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }

        Debug.Log($"{LogPrefix} Bound materials to {boundCount} renderers for role {roleName}.");
        return true;
    }

    private static Shader ResolveShader()
    {
        foreach (var name in ShaderCandidates)
        {
            var shader = Shader.Find(name);
            if (shader != null)
            {
                return shader;
            }
        }

        return null;
    }

    private static Material CreateOrUpdatePartMaterial(string roleName, RolePart part, Shader shader, string materialDir, string textureDir)
    {
        var partName = part.ToString();
        var path = $"{materialDir}/{roleName}_{partName}_Auto.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = $"{roleName}_{partName}_Auto" };
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        BindTextures(material, textureDir, part);
        ApplyHsrLockedParams(material, part);
        ApplyExtractedMaterialParams(material, materialDir, part);
        ApplyRoleShadingKeywords(material, part);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ApplyRoleShadingKeywords(Material material, RolePart part)
    {
        const string bodyKw = "_ROLESHADINGMODE_BODY";
        const string faceKw = "_ROLESHADINGMODE_FACE";
        const string hairKw = "_ROLESHADINGMODE_HAIR";
        const string transKw = "_ROLESHADINGMODE_TRANS";
        material.DisableKeyword(bodyKw);
        material.DisableKeyword(faceKw);
        material.DisableKeyword(hairKw);
        material.DisableKeyword(transKw);

        switch (part)
        {
            case RolePart.Face:
                material.EnableKeyword(faceKw);
                SetFloat(material, "_RoleShadingMode", 1f);
                break;
            case RolePart.Hair:
                material.EnableKeyword(hairKw);
                SetFloat(material, "_RoleShadingMode", 2f);
                break;
            case RolePart.Trans:
                material.EnableKeyword(transKw);
                SetFloat(material, "_RoleShadingMode", 3f);
                break;
            default:
                material.EnableKeyword(bodyKw);
                SetFloat(material, "_RoleShadingMode", 0f);
                break;
        }
    }

    private static void BindTextures(Material material, string textureDir, RolePart part)
    {
        var baseTex = ResolveTexture(textureDir, part, SemanticBase);
        var lightMap = ResolveTexture(textureDir, part, SemanticLightMap);
        var faceShadow = ResolveTexture(textureDir, part, SemanticFaceShadow);
        var outline = ResolveTexture(textureDir, part, SemanticOutline);
        var warmRamp = ResolveTexture(textureDir, part, SemanticWarmRamp);
        var coolRamp = ResolveTexture(textureDir, part, SemanticCoolRamp) ?? warmRamp;

        SetTexture(material, "_BaseMap", baseTex);
        SetTexture(material, "_MainTex", baseTex);
        SetTexture(material, "_LightMap", lightMap);
        SetTexture(material, "_FaceShadowMap", faceShadow);
        SetTexture(material, "_OutlineWidthMap", outline);
        SetTexture(material, "_RampMap", warmRamp);
        SetTexture(material, "_DiffuseRampMultiTex", warmRamp);
        SetTexture(material, "_DiffuseCoolRampMultiTex", coolRamp);
    }

    private static void ApplyHsrLockedParams(Material material, RolePart part)
    {
        // Align with README_NPR3_LockedParams.md (HSR profile baseline).
        SetFloat(material, "_ColorSaturation", 1.48f);
        SetFloat(material, "_ExposureCompensation", 1.04f);
        SetFloat(material, "_ToonContrast", 1.00f);
        SetFloat(material, "_ShadowStrength", 0.83f);
        SetFloat(material, "_AmbientStrength", 0.19f);
        SetFloat(material, "_RampContrast", 1.16f);
        SetFloat(material, "_RampBands", 3.00f);
        SetFloat(material, "_SpecThreshold", 0.987f);
        SetFloat(material, "_SpecSoftness", 0.03f);
        SetFloat(material, "_OutlineUseVertexColorNormal", 1.00f);
        SetFloat(material, "_PackedMapRule", 1.00f); // HSR: AO=R, Spec=B, ID=A
        SetFloat(material, "_UseExtractedSpecParams", 1.00f);
        SetFloat(material, "_FaceShadowThresholdBias", 0.00f);
        SetFloat(material, "_FaceShadowNdLInfluence", 0.35f);
        SetFloat(material, "_FaceShadowMirrorLR", 1.00f);

        var p = GetPartParams(part);
        SetFloat(material, "_FaceOrientationStrength", p.FaceOrientationStrength);
        SetFloat(material, "_HairSpecStrength", p.HairSpecStrength);
        SetFloat(material, "_HairSpecExponent1", p.HairSpecExponent1);
        SetFloat(material, "_HairSpecExponent2", p.HairSpecExponent2);
        SetFloat(material, "_OutlineWidth", p.OutlineWidth);
        SetFloat(material, "_OutlineWidthMapStrength", p.OutlineWidthMapStrength);
        SetFloat(material, "_ShadowStrength", p.ShadowStrength);
        SetFloat(material, "_ShadowThreshold", p.ShadowThreshold);
    }

    private static void ApplyExtractedMaterialParams(Material material, string materialDir, RolePart part)
    {
        var jsonPath = FindExtractedMaterialJson(materialDir, part);
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
        {
            return;
        }

        var json = File.ReadAllText(jsonPath);
        ApplyFloatBlock(material, json);
        ApplyColorBlock(material, json);
    }

    private static string FindExtractedMaterialJson(string materialDir, RolePart part)
    {
        if (!Directory.Exists(materialDir))
        {
            return null;
        }

        var jsonFiles = Directory.GetFiles(materialDir, "*.json", SearchOption.TopDirectoryOnly);
        if (jsonFiles.Length == 0)
        {
            return null;
        }

        string[] keywords;
        switch (part)
        {
            case RolePart.Face:
                keywords = new[] { "Mat_Face" };
                break;
            case RolePart.Hair:
                keywords = new[] { "Mat_Hair" };
                break;
            case RolePart.Trans:
                keywords = new[] { "Mat_Body_Trans", "Mat_Trans" };
                break;
            default:
                keywords = new[] { "Mat_Body_D", "Mat_Body_S", "Mat_Body_Trans", "Mat_Body" };
                break;
        }

        foreach (var key in keywords)
        {
            var match = jsonFiles.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrEmpty(match))
            {
                return match;
            }
        }

        return null;
    }

    private static void ApplyFloatBlock(Material material, string json)
    {
        var block = ExtractJsonObjectBlock(json, "\"m_Floats\"");
        if (string.IsNullOrEmpty(block))
        {
            return;
        }

        var rx = new Regex("\"(?<k>[^\"]+)\"\\s*:\\s*(?<v>-?\\d+(?:\\.\\d+)?)", RegexOptions.Compiled);
        var ms = rx.Matches(block);
        foreach (Match m in ms)
        {
            var key = m.Groups["k"].Value;
            if (!float.TryParse(m.Groups["v"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                continue;
            }

            if (material.HasProperty(key))
            {
                material.SetFloat(key, val);
            }
        }
    }

    private static void ApplyColorBlock(Material material, string json)
    {
        var block = ExtractJsonObjectBlock(json, "\"m_Colors\"");
        if (string.IsNullOrEmpty(block))
        {
            return;
        }

        var colorEntry = new Regex("\"(?<k>[^\"]+)\"\\s*:\\s*\\{\\s*\"r\"\\s*:\\s*(?<r>-?\\d+(?:\\.\\d+)?)\\s*,\\s*\"g\"\\s*:\\s*(?<g>-?\\d+(?:\\.\\d+)?)\\s*,\\s*\"b\"\\s*:\\s*(?<b>-?\\d+(?:\\.\\d+)?)\\s*,\\s*\"a\"\\s*:\\s*(?<a>-?\\d+(?:\\.\\d+)?)\\s*\\}", RegexOptions.Compiled);
        var ms = colorEntry.Matches(block);
        foreach (Match m in ms)
        {
            var key = m.Groups["k"].Value;
            if (!material.HasProperty(key))
            {
                continue;
            }

            if (!TryParseInvariantFloat(m.Groups["r"].Value, out var r) ||
                !TryParseInvariantFloat(m.Groups["g"].Value, out var g) ||
                !TryParseInvariantFloat(m.Groups["b"].Value, out var b) ||
                !TryParseInvariantFloat(m.Groups["a"].Value, out var a))
            {
                continue;
            }

            material.SetColor(key, new Color(r, g, b, a));
        }
    }

    private static string ExtractJsonObjectBlock(string json, string marker)
    {
        var idx = json.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var braceStart = json.IndexOf('{', idx);
        if (braceStart < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = braceStart; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return json.Substring(braceStart, i - braceStart + 1);
                }
            }
        }

        return null;
    }

    private static bool TryParseInvariantFloat(string text, out float value)
    {
        return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static HsrParams GetPartParams(RolePart part)
    {
        switch (part)
        {
            case RolePart.Face:
                return new HsrParams
                {
                    FaceOrientationStrength = 0.30f,
                    HairSpecStrength = 0.00f,
                    HairSpecExponent1 = 72.00f,
                    HairSpecExponent2 = 18.00f,
                    OutlineWidth = 1.75f,
                    OutlineWidthMapStrength = 0.72f
                };
            case RolePart.Hair:
                return new HsrParams
                {
                    FaceOrientationStrength = 0.00f,
                    HairSpecStrength = 0.85f,
                    HairSpecExponent1 = 96.00f,
                    HairSpecExponent2 = 24.00f,
                    OutlineWidth = 2.05f,
                    OutlineWidthMapStrength = 0.60f,
                    ShadowStrength = 0.83f,
                    ShadowThreshold = 0.50f
                };
            case RolePart.Trans:
                return new HsrParams
                {
                    FaceOrientationStrength = 0.00f,
                    HairSpecStrength = 0.20f,
                    HairSpecExponent1 = 64.00f,
                    HairSpecExponent2 = 20.00f,
                    OutlineWidth = 1.60f,
                    OutlineWidthMapStrength = 0.45f,
                    ShadowStrength = 0.68f,
                    ShadowThreshold = 0.58f
                };
            default:
                return new HsrParams
                {
                    FaceOrientationStrength = 0.00f,
                    HairSpecStrength = 0.00f,
                    HairSpecExponent1 = 72.00f,
                    HairSpecExponent2 = 18.00f,
                    OutlineWidth = 2.05f,
                    OutlineWidthMapStrength = 0.60f,
                    ShadowStrength = 0.83f,
                    ShadowThreshold = 0.50f
                };
        }
    }

    private static Texture2D ResolveTexture(string roleTextureDir, RolePart part, IReadOnlyList<string> semantics)
    {
        var texture = FindTextureByTokens(roleTextureDir, PartTokens(part), semantics);
        if (texture != null)
        {
            return texture;
        }

        var partTokens = PartTokens(part);
        foreach (var folder in SharedTextureFolders)
        {
            // Prefer part-matched shared maps first (Face/Body/Hair variants).
            texture = FindTextureByTokens(folder, partTokens, semantics);
            if (texture != null)
            {
                return texture;
            }
        }

        // Fallback to generic shared maps (e.g. default ramp).
        foreach (var folder in SharedTextureFolders)
        {
            texture = FindTextureByTokens(folder, Array.Empty<string>(), semantics);
            if (texture != null)
            {
                return texture;
            }
        }

        return null;
    }

    private static Texture2D FindTextureByTokens(string folder, IReadOnlyList<string> allRequiredAnyPart, IReadOnlyList<string> anySemantic)
    {
        if (!AssetDatabase.IsValidFolder(folder))
        {
            return null;
        }

        var path = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(p =>
            {
                var name = Path.GetFileNameWithoutExtension(p) ?? string.Empty;
                var partOk = allRequiredAnyPart.Count == 0 || allRequiredAnyPart.Any(t => Contains(name, t));
                var semanticOk = anySemantic.Any(t => Contains(name, t));
                return partOk && semanticOk;
            });

        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static string[] PartTokens(RolePart part)
    {
        switch (part)
        {
            case RolePart.Face:
                return new[] { "_Face_" };
            case RolePart.Hair:
                return new[] { "_Hair_" };
            case RolePart.Trans:
                return new[] { "_Trans_", "_Body_" };
            default:
                return new[] { "_Body_", "_Weapon_", "_Parts_" };
        }
    }

    private static bool Contains(string source, string token)
    {
        return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static RolePart ClassifyPart(string rendererName)
    {
        if (!string.IsNullOrEmpty(rendererName))
        {
            if (Contains(rendererName, "Face")) return RolePart.Face;
            if (Contains(rendererName, "Hair")) return RolePart.Hair;
            if (Contains(rendererName, "Trans") || Contains(rendererName, "Glass") || Contains(rendererName, "Stockings")) return RolePart.Trans;
        }

        return RolePart.Body;
    }

    private static void ApplyMaterialToRenderer(Renderer renderer, Material material)
    {
        var mats = renderer.sharedMaterials;
        if (mats == null || mats.Length == 0)
        {
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);
            return;
        }

        for (var i = 0; i < mats.Length; i++)
        {
            mats[i] = material;
        }
        renderer.sharedMaterials = mats;
        EditorUtility.SetDirty(renderer);
    }

    private static void SetTexture(Material material, string property, Texture value)
    {
        if (value != null && material.HasProperty(property))
        {
            material.SetTexture(property, value);
        }
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}

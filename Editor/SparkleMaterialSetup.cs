using System;
using System.Collections.Generic;
using UnityEditor;
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
        SRCharacterNormalizationPipeline.Normalize(BuildConfig());
    }

    private static SRCharacterNormalizationPipeline.CharacterNormalizationConfig BuildConfig()
    {
        return new SRCharacterNormalizationPipeline.CharacterNormalizationConfig
        {
            CharacterRoot = CharacterRoot,
            ModelsRoot = ModelsRoot,
            TexturesRoot = TexturesRoot,
            MaterialsRoot = MaterialsRoot,
            ShaderName = ShaderName,
            SourceModelPath = ModelsRoot + "/Avatar_Sparkle_00_Model_Chara/Avatar_Sparkle_00_Model_Chara.fbx",
            PrefabPath = ModelsRoot + "/Sparkle.prefab",
            MaterialDefinitions = new List<SRCharacterNormalizationPipeline.MaterialDefinition>
            {
                new SRCharacterNormalizationPipeline.MaterialDefinition
                {
                    Key = "Body",
                    Kind = SRCharacterNormalizationPipeline.MaterialKind.Base,
                    MainTex = "Avatar_Sparkle_00_Body_Color_L",
                    LightMap = "Avatar_Sparkle_00_Body_LightMap_L",
                    WarmRamp = "Avatar_Sparkle_00_Body_Warm_Ramp",
                    CoolRamp = "Avatar_Sparkle_00_Body_Cool_Ramp",
                    MaterialLut = "MaterialIDValuesLUTSparkle_00_Mat_Body"
                },
                new SRCharacterNormalizationPipeline.MaterialDefinition
                {
                    Key = "Hair",
                    Kind = SRCharacterNormalizationPipeline.MaterialKind.Hair,
                    MainTex = "Avatar_Sparkle_00_Hair_Color",
                    LightMap = "Avatar_Sparkle_00_Hair_LightMap",
                    WarmRamp = "Avatar_Sparkle_00_Hair_Warm_Ramp",
                    CoolRamp = "Avatar_Sparkle_00_Hair_Cool_Ramp",
                    MaterialLut = "MaterialIDValuesLUTSparkle_00_Mat_Body"
                },
                new SRCharacterNormalizationPipeline.MaterialDefinition
                {
                    Key = "Face",
                    Kind = SRCharacterNormalizationPipeline.MaterialKind.Face,
                    MainTex = "Avatar_Sparkle_00_Face_Color"
                },
                new SRCharacterNormalizationPipeline.MaterialDefinition
                {
                    Key = "FaceMask",
                    Kind = SRCharacterNormalizationPipeline.MaterialKind.Base,
                    MainTex = "Avatar_Sparkle_00_Face_Color"
                },
                new SRCharacterNormalizationPipeline.MaterialDefinition
                {
                    Key = "Effect",
                    Kind = SRCharacterNormalizationPipeline.MaterialKind.Base,
                    MainTex = "Avatar_Sparkle_00_Effect_Color",
                    LightMap = "Avatar_Sparkle_00_Effect_LightMap",
                    MaterialLut = "MaterialIDValuesLUTSparkle_00_Mat_Effect"
                },
                new SRCharacterNormalizationPipeline.MaterialDefinition
                {
                    Key = "Kendama",
                    Kind = SRCharacterNormalizationPipeline.MaterialKind.Base,
                    MainTex = "Avatar_Sparkle_00_Kendama_Color",
                    LightMap = "Avatar_Sparkle_00_Kendama_LightMap",
                    WarmRamp = "Avatar_Sparkle_00_Body_Warm_Ramp",
                    CoolRamp = "Avatar_Sparkle_00_Body_Cool_Ramp",
                    MaterialLut = "MaterialIDValuesLUTSparkle_00_Mat_Kendama"
                }
            }
        };
    }
}

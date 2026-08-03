Shader "SR"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _DiffuseCoolRampMultiTex ("_DiffuseCoolRampMultiTex", 2D) = "black" {}
        _DiffuseRampMultiTex ("_DiffuseRampMultiTex", 2D) = "black" {}
        _FaceExpression ("_FaceExpression", 2D) = "black" {}
        _FaceMap ("_FaceMap", 2D) = "black" {}
        _LightMap ("_LightMap", 2D) = "black" {}
        _MaskTex ("_MaskTex", 2D) = "black" {}
        _MaterialValuesPackLUT ("_MaterialValuesPackLUT", 2D) = "black" {}
        _AddLightStrengthen ("_AddLightStrengthen", Float) = 0
        _AlphaTestThreshold ("_AlphaTestThreshold", Float) = 0
        _CullMode ("_CullMode", Float) = 0
        _CullModeIn ("_CullModeIn", Float) = 0
        _DstBlend ("_DstBlend", Float) = 0
        _EdgeWidth ("_EdgeWidth", Float) = 0
        _EdgeWidthout ("_EdgeWidthout", Float) = 0
        _EmissionIntensity ("_EmissionIntensity", Float) = 0
        _EmissionThreshold ("_EmissionThreshold", Float) = 0
        _EnableAlphaCutoff ("_EnableAlphaCutoff", Float) = 0
        _ExMapThreshold ("_ExMapThreshold", Float) = 0
        _ExSpecularIntensity ("_ExSpecularIntensity", Float) = 0
        _FillAmount0 ("_FillAmount0", Float) = 0
        _FillAmount1 ("_FillAmount1", Float) = 0
        _FillAmount2 ("_FillAmount2", Float) = 0
        _FoamWidth ("_FoamWidth", Float) = 0
        _GlassFrsnIn ("_GlassFrsnIn", Float) = 0
        _HairBlendOffset ("_HairBlendOffset", Float) = 0
        _HairBlendSilhouette ("_HairBlendSilhouette", Float) = 0
        _HairBlendWeight ("_HairBlendWeight", Float) = 0
        _HideCharaParts ("_HideCharaParts", Float) = 0
        _LipLineFixMax ("_LipLineFixMax", Float) = 0
        _LipLineFixStart ("_LipLineFixStart", Float) = 0
        _LipLineFixThrd ("_LipLineFixThrd", Float) = 0
        _LiquidOpaqueness ("_LiquidOpaqueness", Float) = 0
        _NoseLinePower ("_NoseLinePower", Float) = 0
        _Opaqueness ("_Opaqueness", Float) = 0
        _OutlineFixRange1 ("_OutlineFixRange1", Float) = 0
        _OutlineFixRange2 ("_OutlineFixRange2", Float) = 0
        _OutlineFixRange3 ("_OutlineFixRange3", Float) = 0
        _OutlineFixRange4 ("_OutlineFixRange4", Float) = 0
        _OutlineNormalFrom ("_OutlineNormalFrom", Float) = 0
        _OutlineOffset ("_OutlineOffset", Float) = 0
        _OutlineWidth ("_OutlineWidth", Float) = 0
        _ReceiveShadows ("_ReceiveShadows", Float) = 0
        _RimDark5 ("_RimDark5", Float) = 0
        _RimDark7 ("_RimDark7", Float) = 0
        _RimEdgeSoftness0 ("_RimEdgeSoftness0", Float) = 0
        _RimEdgeSoftness1 ("_RimEdgeSoftness1", Float) = 0
        _RimEdgeSoftness2 ("_RimEdgeSoftness2", Float) = 0
        _RimEdgeSoftness3 ("_RimEdgeSoftness3", Float) = 0
        _RimEdgeSoftness4 ("_RimEdgeSoftness4", Float) = 0
        _RimEdgeSoftness5 ("_RimEdgeSoftness5", Float) = 0
        _RimEdgeSoftness6 ("_RimEdgeSoftness6", Float) = 0
        _RimEdgeSoftness7 ("_RimEdgeSoftness7", Float) = 0
        _RimPower ("_RimPower", Float) = 0
        _RimShadowFeather0 ("_RimShadowFeather0", Float) = 0
        _RimShadowFeather2 ("_RimShadowFeather2", Float) = 0
        _RimShadowFeather5 ("_RimShadowFeather5", Float) = 0
        _RimShadowFeather7 ("_RimShadowFeather7", Float) = 0
        _RimShadowWidth0 ("_RimShadowWidth0", Float) = 0
        _RimShadowWidth1 ("_RimShadowWidth1", Float) = 0
        _RimShadowWidth2 ("_RimShadowWidth2", Float) = 0
        _RimShadowWidth3 ("_RimShadowWidth3", Float) = 0
        _RimShadowWidth4 ("_RimShadowWidth4", Float) = 0
        _RimShadowWidth5 ("_RimShadowWidth5", Float) = 0
        _RimShadowWidth6 ("_RimShadowWidth6", Float) = 0
        _RimShadowWidth7 ("_RimShadowWidth7", Float) = 0
        _RimType7 ("_RimType7", Float) = 0
        _RimWidth ("_RimWidth", Float) = 0
        _RimWidth0 ("_RimWidth0", Float) = 0
        _Rimintensity ("_Rimintensity", Float) = 0
        _ShadowThreshold ("_ShadowThreshold", Float) = 0
        _ShowPartID ("_ShowPartID", Float) = 0
        _SpecularIntensity ("_SpecularIntensity", Float) = 0
        _SpecularIntensity5 ("_SpecularIntensity5", Float) = 0
        _SpecularIntensity7 ("_SpecularIntensity7", Float) = 0
        _SpecularPow ("_SpecularPow", Float) = 0
        _SpecularRoughness ("_SpecularRoughness", Float) = 0
        _SpecularRoughness2 ("_SpecularRoughness2", Float) = 0
        _SpecularRoughness3 ("_SpecularRoughness3", Float) = 0
        _SpecularRoughness4 ("_SpecularRoughness4", Float) = 0
        _SpecularRoughness5 ("_SpecularRoughness5", Float) = 0
        _SpecularRoughness6 ("_SpecularRoughness6", Float) = 0
        _SpecularRoughness7 ("_SpecularRoughness7", Float) = 0
        _SpecularShadowOffset ("_SpecularShadowOffset", Float) = 0
        _SpecularShininess ("_SpecularShininess", Float) = 0
        _SpecularShininess0 ("_SpecularShininess0", Float) = 0
        _SpecularShininess2 ("_SpecularShininess2", Float) = 0
        _SpecularShininess4 ("_SpecularShininess4", Float) = 0
        _SpecularShininess6 ("_SpecularShininess6", Float) = 0
        _SpecularShininess7 ("_SpecularShininess7", Float) = 0
        _SpecularThreshold ("_SpecularThreshold", Float) = 0
        _SrcBlend ("_SrcBlend", Float) = 0
        _StencilFace ("_StencilFace", Float) = 0
        _StencilRef ("_StencilRef", Float) = 0
        _StencilRefIn ("_StencilRefIn", Float) = 0
        _StencilRefOut ("_StencilRefOut", Float) = 0
        _SurfaceLighted ("_SurfaceLighted", Float) = 0
        _UseMaterialValuesLUT ("_UseMaterialValuesLUT", Float) = 0
        _mBloomIntensity0 ("_mBloomIntensity0", Float) = 0
        _mBloomIntensity1 ("_mBloomIntensity1", Float) = 0
        _mBloomIntensity2 ("_mBloomIntensity2", Float) = 0
        _mBloomIntensity3 ("_mBloomIntensity3", Float) = 0
        _mBloomIntensity4 ("_mBloomIntensity4", Float) = 0
        _mBloomIntensity5 ("_mBloomIntensity5", Float) = 0
        _mBloomIntensity6 ("_mBloomIntensity6", Float) = 0
        _mBloomIntensity7 ("_mBloomIntensity7", Float) = 0
        _BackColor ("_BackColor", Color) = (1,1,1,1)
        _BrightColor ("_BrightColor", Color) = (1,1,1,1)
        _DissolveComponent ("_DissolveComponent", Color) = (1,1,1,1)
        _ExCheekColor ("_ExCheekColor", Color) = (1,1,1,1)
        _ExEyeColor ("_ExEyeColor", Color) = (1,1,1,1)
        _ExShadowColor ("_ExShadowColor", Color) = (1,1,1,1)
        _ExShyColor ("_ExShyColor", Color) = (1,1,1,1)
        _EyeShadowColor ("_EyeShadowColor", Color) = (1,1,1,1)
        _GlassColorA ("_GlassColorA", Color) = (1,1,1,1)
        _GlassColorI ("_GlassColorI", Color) = (1,1,1,1)
        _GlassColorU ("_GlassColorU", Color) = (1,1,1,1)
        _LipLinefixColor ("_LipLinefixColor", Color) = (1,1,1,1)
        _MainTexSpeed ("_MainTexSpeed", Color) = (1,1,1,1)
        _NoseLineColor ("_NoseLineColor", Color) = (1,1,1,1)
        _OutlineColor ("_OutlineColor", Color) = (1,1,1,1)
        _OutlineColor0 ("_OutlineColor0", Color) = (1,1,1,1)
        _OutlineColor1 ("_OutlineColor1", Color) = (1,1,1,1)
        _OutlineColor2 ("_OutlineColor2", Color) = (1,1,1,1)
        _OutlineColor3 ("_OutlineColor3", Color) = (1,1,1,1)
        _OutlineColor4 ("_OutlineColor4", Color) = (1,1,1,1)
        _OutlineColor5 ("_OutlineColor5", Color) = (1,1,1,1)
        _OutlineColor6 ("_OutlineColor6", Color) = (1,1,1,1)
        _OutlineColor7 ("_OutlineColor7", Color) = (1,1,1,1)
        _RimColor ("_RimColor", Color) = (1,1,1,1)
        _RimColor0 ("_RimColor0", Color) = (1,1,1,1)
        _RimColor1 ("_RimColor1", Color) = (1,1,1,1)
        _RimColor2 ("_RimColor2", Color) = (1,1,1,1)
        _RimColor3 ("_RimColor3", Color) = (1,1,1,1)
        _RimColor4 ("_RimColor4", Color) = (1,1,1,1)
        _RimColor5 ("_RimColor5", Color) = (1,1,1,1)
        _RimColor6 ("_RimColor6", Color) = (1,1,1,1)
        _RimColor7 ("_RimColor7", Color) = (1,1,1,1)
        _RimShadowColor2 ("_RimShadowColor2", Color) = (1,1,1,1)
        _RimShadowColor4 ("_RimShadowColor4", Color) = (1,1,1,1)
        _RimShadowColor5 ("_RimShadowColor5", Color) = (1,1,1,1)
        _RimShadowColor6 ("_RimShadowColor6", Color) = (1,1,1,1)
        _RimShadowColor7 ("_RimShadowColor7", Color) = (1,1,1,1)
        _RimShadowOffset ("_RimShadowOffset", Color) = (1,1,1,1)
        _SPDir ("_SPDir", Color) = (1,1,1,1)
        _ShadowColor ("_ShadowColor", Color) = (1,1,1,1)
        _SpecularColor0 ("_SpecularColor0", Color) = (1,1,1,1)
        _SpecularColor1 ("_SpecularColor1", Color) = (1,1,1,1)
        _SpecularColor2 ("_SpecularColor2", Color) = (1,1,1,1)
        _SpecularColor3 ("_SpecularColor3", Color) = (1,1,1,1)
        _SpecularColor4 ("_SpecularColor4", Color) = (1,1,1,1)
        _SpecularColor5 ("_SpecularColor5", Color) = (1,1,1,1)
        _SpecularColor6 ("_SpecularColor6", Color) = (1,1,1,1)
        _SpecularColor7 ("_SpecularColor7", Color) = (1,1,1,1)
        _SurfaceColor ("_SurfaceColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float4 _Color;
            CBUFFER_END
            Varyings vert(Attributes input) { Varyings o; o.positionHCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=TRANSFORM_TEX(input.uv,_MainTex); return o; }
            half4 frag(Varyings input) : SV_Target { return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv) * _Color; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Assets/unity-shadertoy-validation/Common/Shaders/ShadertoyDepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
}

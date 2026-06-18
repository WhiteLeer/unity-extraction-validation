#ifndef HOYOTOON_STAR_RAIL_URP_INCLUDED
#define HOYOTOON_STAR_RAIL_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
TEXTURE2D(_LightMap);
SAMPLER(sampler_LightMap);
TEXTURE2D(_FaceMap);
SAMPLER(sampler_FaceMap);
TEXTURE2D(_DiffuseRampMultiTex);
SAMPLER(sampler_DiffuseRampMultiTex);
TEXTURE2D(_DiffuseCoolRampMultiTex);
SAMPLER(sampler_DiffuseCoolRampMultiTex);
TEXTURE2D(_EmissionTex);
SAMPLER(sampler_EmissionTex);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _LightMap_ST;
    float4 _FaceMap_ST;
    half4 _Color;
    half4 _BackColor;
    half4 _ShadowColor;
    half4 _RimColor0;
    half4 _ES_SPColor;
    half4 _EmissionTintColor;
    half _ShadowRamp;
    half _ShadowSoftness;
    half _EnableShadow;
    half _EnableRimLight;
    half _RimWidth;
    half _Rimintensity;
    half _ES_Rimintensity;
    half _EnableSpecular;
    half _SpecularShininess0;
    half _SpecularIntensity0;
    half _ES_SPIntensity;
    half _EnableEmission;
    half _EmissionThreshold;
    half _EmissionIntensity;
    half _EmissionToggle;
    half _BaseMaterial;
    half _FaceMaterial;
    half _HairMaterial;
    half _IsTransparent;
    half _Opacity;
    half _EnableAlphaCutoff;
    half _AlphaTestThreshold;
CBUFFER_END

struct HoyoAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 uv2 : TEXCOORD1;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct HoyoVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float2 uv2 : TEXCOORD3;
    half3 viewDirWS : TEXCOORD4;
    float4 shadowCoord : TEXCOORD5;
    half4 fogFactorAndVertexLight : TEXCOORD6;
    FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

inline half HoyoRegionToRampV(half region)
{
    half regionIndex = floor(saturate(region) * 7.0h + 0.5h);
    return (regionIndex + 0.5h) / 8.0h;
}

inline half3 HoyoSampleRamp(float x, half region, bool useCoolRamp)
{
    float2 uv = float2(saturate(x), HoyoRegionToRampV(region));
    return useCoolRamp
        ? SAMPLE_TEXTURE2D(_DiffuseCoolRampMultiTex, sampler_DiffuseCoolRampMultiTex, uv).rgb
        : SAMPLE_TEXTURE2D(_DiffuseRampMultiTex, sampler_DiffuseRampMultiTex, uv).rgb;
}

inline Light HoyoResolveMainLight(float4 shadowCoord)
{
    Light mainLight = GetMainLight(shadowCoord);
    half colorEnergy = max(mainLight.color.r, max(mainLight.color.g, mainLight.color.b));
    if (colorEnergy <= 0.0001h)
    {
        mainLight.direction = normalize(half3(0.35h, 0.8h, 0.4h));
        mainLight.color = half3(1.0h, 1.0h, 1.0h);
        mainLight.distanceAttenuation = 1.0h;
        mainLight.shadowAttenuation = 1.0h;
    }

    return mainLight;
}

inline half3 HoyoEvaluateToon(half3 albedo, half3 normalWS, half3 viewDirWS, half4 lightMap, float2 uv, Light mainLight)
{
    half attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
    half ndl = saturate(dot(normalWS, mainLight.direction));
    half shadowSoftness = max(_ShadowSoftness, 0.02h);
    half shadowThreshold = saturate(_ShadowRamp);
    half litBand = smoothstep(shadowThreshold - shadowSoftness, shadowThreshold + shadowSoftness, ndl * attenuation);
    half3 warmRamp = HoyoSampleRamp(ndl, lightMap.a, false);
    half3 coolRamp = HoyoSampleRamp(ndl, lightMap.a, true);
    half3 rampColor = lerp(coolRamp, warmRamp, litBand);
    half shadowEnabled = step(0.5h, _EnableShadow);
    half3 toonColor = lerp(albedo, albedo * lerp(_ShadowColor.rgb, rampColor, litBand), shadowEnabled);

    half specEnabled = step(0.5h, _EnableSpecular);
    half3 halfDir = SafeNormalize(mainLight.direction + viewDirWS);
    half specPower = lerp(8.0h, 96.0h, saturate(_SpecularShininess0 / 128.0h));
    half spec = pow(saturate(dot(normalWS, halfDir)), specPower) * lightMap.b;
    toonColor += specEnabled * spec * _SpecularIntensity0 * max(_ES_SPIntensity, 0.001h) * _ES_SPColor.rgb * attenuation;

    half rimEnabled = step(0.5h, _EnableRimLight);
    half rimBase = pow(1.0h - saturate(dot(normalWS, viewDirWS)), lerp(2.0h, 8.0h, saturate(_RimWidth)));
    toonColor += rimEnabled * rimBase * lightMap.r * _Rimintensity * max(_ES_Rimintensity, 0.001h) * _RimColor0.rgb;

    half emissionEnabled = max(step(0.5h, _EmissionToggle), step(0.5h, _EnableEmission));
    if (emissionEnabled > 0.0h)
    {
        half3 emissionTex = SAMPLE_TEXTURE2D(_EmissionTex, sampler_EmissionTex, uv).rgb;
        half emissionMask = step(_EmissionThreshold, lightMap.g);
        toonColor += emissionEnabled * emissionMask * lerp(albedo, emissionTex, step(1.5h, _EnableEmission)) * _EmissionTintColor.rgb * _EmissionIntensity;
    }

    return toonColor * mainLight.color;
}

HoyoVaryings HoyoUrpVert(HoyoAttributes input)
{
    HoyoVaryings output = (HoyoVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = positionInputs.positionCS;
    output.positionWS = positionInputs.positionWS;
    output.normalWS = normalize(normalInputs.normalWS);
    output.viewDirWS = GetWorldSpaceNormalizeViewDir(positionInputs.positionWS);
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    output.uv2 = TRANSFORM_TEX(input.uv2, _LightMap);
    output.shadowCoord = GetShadowCoord(positionInputs);
    output.fogFactorAndVertexLight = half4(ComputeFogFactor(positionInputs.positionCS.z), VertexLighting(positionInputs.positionWS, output.normalWS));
    return output;
}

half4 HoyoUrpFrag(HoyoVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half4 albedoSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
    if (_EnableAlphaCutoff > 0.5h)
    {
        clip(albedoSample.a - _AlphaTestThreshold);
    }

    half4 lightMap = SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, input.uv2);
    half facing = IS_FRONT_VFACE(input.frontFace, 1.0h, 0.0h);
    half3 tint = lerp(_BackColor.rgb, _Color.rgb, facing);
    half3 albedo = albedoSample.rgb * tint;

    if (_FaceMaterial > 0.5h)
    {
        half3 faceMap = SAMPLE_TEXTURE2D(_FaceMap, sampler_FaceMap, input.uv).rgb;
        albedo = lerp(albedo, albedo * faceMap, step(0.001h, dot(faceMap, half3(0.2126h, 0.7152h, 0.0722h))));
        lightMap.a = 0.0h;
        lightMap.b = 0.0h;
    }

    Light mainLight = HoyoResolveMainLight(input.shadowCoord);
    half3 color = HoyoEvaluateToon(albedo, normalize(input.normalWS), SafeNormalize(input.viewDirWS), lightMap, input.uv, mainLight);

    #if defined(_ADDITIONAL_LIGHTS)
    uint additionalLightsCount = GetAdditionalLightsCount();
    for (uint lightIndex = 0u; lightIndex < additionalLightsCount; ++lightIndex)
    {
        Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS);
        color += 0.25h * HoyoEvaluateToon(albedo, normalize(input.normalWS), SafeNormalize(input.viewDirWS), lightMap, input.uv, additionalLight);
    }
    #endif

    color += albedo * input.fogFactorAndVertexLight.yzw;
    color = MixFog(color, input.fogFactorAndVertexLight.x);

    half finalAlpha = lerp(1.0h, albedoSample.a, step(0.5h, _IsTransparent)) * max(_Opacity, 0.0h);
    return half4(saturate(color), saturate(finalAlpha));
}

#endif

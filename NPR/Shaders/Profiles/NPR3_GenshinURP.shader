Shader "Custom/NPR-3/GenshinURP"
{
    Properties
    {
        [MainTexture]_BaseMap("Base Map", 2D) = "white" {}
        [MainColor]_BaseColor("Base Color", Color) = (1,1,1,1)
        [NoScaleOffset]_LightMap("Packed Light Map", 2D) = "white" {}
        _LightMapAOStrength("LightMap AO Strength", Range(0,2)) = 1.0
        _LightMapSpecStrength("LightMap Spec Strength", Range(0,2)) = 1.0
        _LightMapSpecPower("LightMap Spec Power", Range(0.25,8)) = 1.0

        [NoScaleOffset]_RampMap("Ramp Map", 2D) = "gray" {}
        _RampOffset("Ramp Offset", Range(-1,1)) = 0
        _RampContrast("Ramp Contrast", Range(0.1,3)) = 1.2
        _RampStrength("Ramp Strength", Range(0,2)) = 1.05
        _RampBands("Ramp Bands", Range(1,8)) = 4
        _ShadowStrength("Shadow Strength", Range(0,1)) = 0.9
        _AmbientStrength("Ambient Strength", Range(0,1)) = 0.14
        _ShadowTintColor("Shadow Tint Color", Color) = (0.75,0.83,1,1)
        _ShadowTintStrength("Shadow Tint Strength", Range(0,1)) = 0.35
        [Header(Shadow Stylization)]
        _ShadowCoolColor("Shadow Cool Color", Color) = (0.72,0.82,1,1)
        _ShadowWarmColor("Shadow Warm Color", Color) = (1,0.86,0.74,1)
        _ShadowStylizeStrength("Shadow Stylize Strength", Range(0,1)) = 0.0
        _ShadowTerminatorWidth("Shadow Terminator Width", Range(0.02,1.0)) = 0.28
        _ShadowTerminatorSoftness("Shadow Terminator Softness", Range(0.02,1.0)) = 0.22

        _SpecColor("Spec Color", Color) = (0.38,0.38,0.38,1)
        _SpecThreshold("Spec Threshold", Range(0,1)) = 0.994
        _SpecSoftness("Spec Softness", Range(0.001,0.2)) = 0.005
        _HairSpecStrength("Hair Spec Strength", Range(0,2)) = 0.0
        _HairSpecShift("Hair Spec Shift", Range(-1,1)) = 0.10
        _HairSpecExponent1("Hair Spec Exponent 1", Range(1,256)) = 64
        _HairSpecExponent2("Hair Spec Exponent 2", Range(1,256)) = 20
        _HairSpecSecondaryStrength("Hair Spec Secondary Strength", Range(0,1)) = 0.45
        _HairAnisoStabilize("Hair Aniso Stabilize", Range(0,1)) = 0.7
        _HairSpecViewFade("Hair Spec View Fade", Range(0,4)) = 1.2

        _RimColor("Rim Color", Color) = (1,1,1,1)
        _RimPower("Rim Power", Range(0.5,8)) = 3.8
        _RimStrength("Rim Strength", Range(0,2)) = 0.08
        _AdditionalLightStrength("Additional Light Strength", Range(0,2)) = 0.10
        _ColorSaturation("Color Saturation", Range(0,2.5)) = 1.3
        _ExposureCompensation("Exposure Compensation", Range(0.25,2.0)) = 1.0
        _ToonContrast("Toon Contrast", Range(0.5,2.0)) = 1.0
        [Header(Face Tuning)]
        _FaceRegionWeight("Face Region Weight", Range(0,1)) = 0
        _FaceShadowLift("Face Shadow Lift", Range(0,1)) = 0
        _FaceForwardWrap("Face Forward Wrap", Range(0,1)) = 0
        _FaceSpecBoost("Face Spec Boost", Range(0,2)) = 0
        _FaceRimSuppress("Face Rim Suppress", Range(0,1)) = 0
        _HeadForwardWS("Head Forward WS", Vector) = (0,0,1,0)
        _HeadRightWS("Head Right WS", Vector) = (1,0,0,0)
        _FaceShadowSoftness("Face Shadow Softness", Range(0.01,0.5)) = 0.08
        _FaceShadowHorizontalBias("Face Shadow Horizontal Bias", Range(-1,1)) = 0
        _FaceOrientationStrength("Face Orientation Strength", Range(0,1)) = 0
        [NoScaleOffset]_FaceShadowMap("Face Shadow Map", 2D) = "white" {}
        _UseFaceShadowMap("Use Face Shadow Map", Range(0,1)) = 0
        _FaceShadowMapStrength("Face Shadow Map Strength", Range(0,2)) = 1.0

        [Header(Outline)]
        _OutlineColor("Outline Color", Color) = (0.08,0.11,0.16,1)
        _OutlineWidth("Outline Width", Range(0,10)) = 3.0
        _OutlineMinScale("Outline Min Scale", Range(0.1,1.0)) = 0.55
        _OutlineDistanceStart("Outline Distance Start", Range(0.0,20.0)) = 2.0
        _OutlineDistanceEnd("Outline Distance End", Range(0.1,60.0)) = 10.0
        _OutlineSilhouetteBoost("Outline Silhouette Boost", Range(0.0,2.0)) = 0.35
        _OutlineInnerSuppress("Outline Inner Suppress", Range(0.0,0.95)) = 0.10
        _OutlineZOffset("Outline Z Offset", Range(0.0,0.02)) = 0.001
        _OutlineUseVertexColorNormal("Outline Use Vertex Color Normal", Range(0,1)) = 0
        [NoScaleOffset]_OutlineWidthMap("Outline Width Map", 2D) = "white" {}
        _OutlineWidthMapStrength("Outline Width Map Strength", Range(0,2)) = 1.0

        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5

        [NoScaleOffset]_NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(0,2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 300

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float _LightMapAOStrength;
            float _LightMapSpecStrength;
            float _LightMapSpecPower;
            float _RampOffset;
            float _RampContrast;
            float _RampStrength;
            float _RampBands;
            float _ShadowStrength;
            float _AmbientStrength;
            float4 _ShadowTintColor;
            float _ShadowTintStrength;
            float4 _ShadowCoolColor;
            float4 _ShadowWarmColor;
            float _ShadowStylizeStrength;
            float _ShadowTerminatorWidth;
            float _ShadowTerminatorSoftness;
            float4 _SpecColor;
            float _SpecThreshold;
            float _SpecSoftness;
            float _HairSpecStrength;
            float _HairSpecShift;
            float _HairSpecExponent1;
            float _HairSpecExponent2;
            float _HairSpecSecondaryStrength;
            float _HairAnisoStabilize;
            float _HairSpecViewFade;
            float4 _RimColor;
            float _RimPower;
            float _RimStrength;
            float _AdditionalLightStrength;
            float _ColorSaturation;
            float _ExposureCompensation;
            float _ToonContrast;
            float _FaceRegionWeight;
            float _FaceShadowLift;
            float _FaceForwardWrap;
            float _FaceSpecBoost;
            float _FaceRimSuppress;
            float4 _HeadForwardWS;
            float4 _HeadRightWS;
            float _FaceShadowSoftness;
            float _FaceShadowHorizontalBias;
            float _FaceOrientationStrength;
            float _UseFaceShadowMap;
            float _FaceShadowMapStrength;
            float _Cutoff;
            float _NormalScale;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_LightMap); SAMPLER(sampler_LightMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_RampMap); SAMPLER(sampler_RampMap);
            TEXTURE2D(_FaceShadowMap); SAMPLER(sampler_FaceShadowMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 tangentWS : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vni = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS = NormalizeNormalPerVertex(vni.normalWS);
                OUT.tangentWS = NormalizeNormalPerVertex(vni.tangentWS);
                OUT.bitangentWS = NormalizeNormalPerVertex(vni.bitangentWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(vpi);
                return OUT;
            }

            float3 GetNormalWS(Varyings IN)
            {
                float3 n = normalize(IN.normalWS);
                float3 t = normalize(IN.tangentWS);
                float3 b = normalize(IN.bitangentWS);
                float3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv), _NormalScale);
                return normalize(nTS.x * t + nTS.y * b + nTS.z * n);
            }

            float EvalBand(float ndotl)
            {
                float rampU = saturate(ndotl * 0.5 + 0.5 + _RampOffset);
                rampU = saturate((rampU - 0.5) * _RampContrast + 0.5);
                float bands = max(_RampBands, 1.0);
                float quant = max(bands - 1.0, 1.0);
                return floor(rampU * quant + 0.5) / quant;
            }

            float3 EvalRamp(float ndotl)
            {
                float rampU = EvalBand(ndotl);
                float3 ramp = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(rampU, 0.5)).rgb;
                return lerp(1.0.xxx, ramp, _RampStrength);
            }

            float3 ApplySaturation(float3 color, float saturation)
            {
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                return lerp(luma.xxx, color, saturation);
            }

            float EvalAnisoLobe(float3 tangentWS, float3 normalWS, float3 halfDirWS, float shift, float exponent)
            {
                float3 tShift = normalize(tangentWS + normalWS * shift);
                float tDotH = saturate(abs(dot(tShift, halfDirWS)));
                return pow(1.0 - tDotH, max(exponent, 1.0));
            }

            float3 DecodePackedLightMap(float4 mapSample)
            {
                // Genshin profile: G=AO, max(R,B)=SpecMask, A=MaterialId
                float ao = mapSample.g;
                float specMask = max(mapSample.r, mapSample.b);
                float materialId = mapSample.a;
                return float3(ao, specMask, materialId);
            }

            float EvalFaceOrientationMask(float2 uv, float3 lightDirWS)
            {
                float3 headF = normalize(_HeadForwardWS.xyz);
                float3 headR = normalize(_HeadRightWS.xyz);
                float2 lightFR = float2(dot(lightDirWS, headR), dot(lightDirWS, headF));
                lightFR /= max(1e-4, length(lightFR));
                float pivot = saturate(lightFR.x * 0.5 + 0.5 + _FaceShadowHorizontalBias);
                float soft = max(0.01, _FaceShadowSoftness);
                return smoothstep(pivot - soft, pivot + soft, uv.x);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                float4 packedMap = SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, IN.uv);
                float3 packedDecoded = DecodePackedLightMap(packedMap);
                float aoFromMap = saturate(lerp(1.0, packedDecoded.x, _LightMapAOStrength));
                float specMaskFromMap = pow(saturate(packedDecoded.y), _LightMapSpecPower) * _LightMapSpecStrength;
                float materialId = packedDecoded.z;
                #if defined(_ALPHATEST_ON)
                clip(baseSample.a - _Cutoff);
                #endif

                float3 normalWS = GetNormalWS(IN);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight(IN.shadowCoord);
                float ndotlRaw = saturate(dot(normalWS, mainLight.direction));
                float ndotlWrapped = saturate(ndotlRaw * (1.0 - _FaceForwardWrap) + _FaceForwardWrap);
                float ndotl = lerp(ndotlRaw, ndotlWrapped, _FaceRegionWeight);
                float shadow = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
                shadow = lerp(shadow, saturate(shadow + _FaceShadowLift), _FaceRegionWeight);
                float faceShadowMask = SAMPLE_TEXTURE2D(_FaceShadowMap, sampler_FaceShadowMap, IN.uv).r;
                float faceShadowBlend = saturate(_UseFaceShadowMap * _FaceShadowMapStrength * _FaceRegionWeight);
                float faceOrientationMask = EvalFaceOrientationMask(IN.uv, mainLight.direction);
                float orientedFaceMask = lerp(faceShadowMask, saturate(faceShadowMask * 0.6 + faceOrientationMask * 0.4), _FaceOrientationStrength);
                shadow *= lerp(1.0, orientedFaceMask, faceShadowBlend);
                float3 rampMain = EvalRamp(ndotl) * shadow;
                float3 ambient = SampleSH(normalWS) * _AmbientStrength * aoFromMap;

                float shadowMask = 1.0 - ndotl * shadow;
                float termCenter = saturate(1.0 - ndotl / max(_ShadowTerminatorWidth, 0.02));
                float termBand = smoothstep(0.0, max(_ShadowTerminatorSoftness, 0.02), termCenter);
                float3 stylizedShadowTint = lerp(_ShadowCoolColor.rgb, _ShadowWarmColor.rgb, termBand);
                float3 finalShadowTint = lerp(_ShadowTintColor.rgb, stylizedShadowTint, _ShadowStylizeStrength);
                float shadowTintWeight = saturate(shadowMask * _ShadowTintStrength);
                float3 shadedBase = lerp(baseSample.rgb, baseSample.rgb * finalShadowTint, shadowTintWeight);

                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float ndoth = saturate(dot(normalWS, halfDir));
                float spec = smoothstep(_SpecThreshold - _SpecSoftness, _SpecThreshold + _SpecSoftness, ndoth) * specMaskFromMap;
                float3 tangentWS = normalize(IN.tangentWS);
                float hairPrimary = EvalAnisoLobe(tangentWS, normalWS, halfDir, _HairSpecShift, _HairSpecExponent1);
                float hairSecondary = EvalAnisoLobe(tangentWS, normalWS, halfDir, -_HairSpecShift, _HairSpecExponent2);
                float hairSpec = (hairPrimary + hairSecondary * _HairSpecSecondaryStrength) * _HairSpecStrength * specMaskFromMap;
                float tangentValid = saturate((length(IN.tangentWS) - 0.05) * 8.0);
                float viewN = saturate(abs(dot(normalWS, viewDirWS)));
                float viewFade = saturate(pow(viewN, max(0.01, _HairSpecViewFade)));
                float anisoStable = lerp(1.0, viewFade, _HairAnisoStabilize);
                hairSpec *= tangentValid * anisoStable;
                spec *= (1.0 + _FaceSpecBoost * _FaceRegionWeight);

                float rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimStrength;
                rim *= (1.0 - _FaceRimSuppress * _FaceRegionWeight);

                float3 litColor = shadedBase * (rampMain * mainLight.color + ambient);
                litColor *= lerp(1.0, 1.08, saturate(materialId));
                litColor += _SpecColor.rgb * spec * mainLight.color;
                litColor += _SpecColor.rgb * hairSpec * mainLight.color;
                litColor += _RimColor.rgb * rim;

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint i = 0; i < lightCount; i++)
                {
                    Light l = GetAdditionalLight(i, IN.positionWS);
                    float nDotLAdd = saturate(dot(normalWS, l.direction));
                    float3 rampAdd = EvalRamp(nDotLAdd) * l.distanceAttenuation * l.shadowAttenuation;
                    litColor += baseSample.rgb * rampAdd * l.color * _AdditionalLightStrength;
                }
                #endif

                litColor *= _ExposureCompensation;
                litColor = (litColor - 0.5) * _ToonContrast + 0.5;
                litColor = ApplySaturation(litColor, _ColorSaturation);
                return half4(saturate(litColor), baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertOutline
            #pragma fragment FragOutline
            #pragma multi_compile_fragment _ _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineMinScale;
            float _OutlineDistanceStart;
            float _OutlineDistanceEnd;
            float _OutlineSilhouetteBoost;
            float _OutlineInnerSuppress;
            float _OutlineZOffset;
            float _OutlineUseVertexColorNormal;
            float _OutlineWidthMapStrength;
            float _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OutlineWidthMap); SAMPLER(sampler_OutlineWidthMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings VertOutline(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                float3 normalOS = normalize(IN.normalOS);
                float3 colorNormalOS = normalize(IN.color.xyz * 2.0 - 1.0);
                normalOS = normalize(lerp(normalOS, colorNormalOS, saturate(_OutlineUseVertexColorNormal)));
                VertexNormalInputs vni = GetVertexNormalInputs(normalOS);
                float3 normalVS = normalize(TransformWorldToViewDir(normalize(vni.normalWS), true));

                float depthVS = abs(vpi.positionVS.z);
                float distDenom = max(0.001, _OutlineDistanceEnd - _OutlineDistanceStart);
                float distT = saturate((depthVS - _OutlineDistanceStart) / distDenom);
                float distScale = lerp(1.0, _OutlineMinScale, distT);

                float silhouette = 1.0 - saturate(abs(normalVS.z));
                float silhouetteScale = 1.0 + silhouette * _OutlineSilhouetteBoost;
                float innerSuppress = saturate((silhouette - _OutlineInnerSuppress) / max(0.001, 1.0 - _OutlineInnerSuppress));
                float outlineWidthMask = SAMPLE_TEXTURE2D_LOD(_OutlineWidthMap, sampler_OutlineWidthMap, IN.uv, 0).r;
                float widthMapScale = lerp(1.0, outlineWidthMask, saturate(_OutlineWidthMapStrength));

                float aspect = max(0.001, _ScreenParams.x / _ScreenParams.y);
                float2 dir = normalize(float2(normalVS.x / aspect, normalVS.y));
                float outlineScale = _OutlineWidth * 0.004 * distScale * silhouetteScale * widthMapScale * innerSuppress;
                float2 offset = dir * outlineScale;
                float4 positionCS = vpi.positionCS;
                positionCS.xy += offset * positionCS.w;
                positionCS.z -= _OutlineZOffset * positionCS.w;
                OUT.positionCS = positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 FragOutline(Varyings IN) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                #endif
                return _OutlineColor;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
}

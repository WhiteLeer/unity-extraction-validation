Shader "SR/RDC/Texture Slots"
{
    Properties
    {
        _MainTex0 ("Texture Slot 0", 2D) = "white" {}
        _MainTex1 ("Texture Slot 1", 2D) = "white" {}
        _MainTex2 ("Texture Slot 2", 2D) = "white" {}
        _MainTex3 ("Texture Slot 3", 2D) = "white" {}
        _MainTex4 ("Texture Slot 4", 2D) = "white" {}
        _MainTex5 ("Texture Slot 5", 2D) = "white" {}
        _MainTex6 ("Texture Slot 6", 2D) = "white" {}
        _MainTex7 ("Texture Slot 7", 2D) = "white" {}
        _SlotAdd0 ("Slot 0 Red Bias", Float) = 0
        _SlotAdd1 ("Slot 1 Red Bias", Float) = 0
        _SlotAdd2 ("Slot 2 Red Bias", Float) = 0
        _SlotAdd3 ("Slot 3 Red Bias", Float) = 0
        _SlotAdd4 ("Slot 4 Red Bias", Float) = 0
        _SlotAdd5 ("Slot 5 Red Bias", Float) = 0
        _SlotAdd6 ("Slot 6 Red Bias", Float) = 0
        _SlotAdd7 ("Slot 7 Red Bias", Float) = 0
        _UVScaleOffset ("UV Scale Offset", Vector) = (1,1,0,0)
        _VertexColorTint ("Vertex Color Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]
        Pass
        {
            Name "RdcTextureSlots"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 slotData : TEXCOORD1;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                nointerpolation uint slot : TEXCOORD1;
            };

            sampler2D _MainTex0, _MainTex1, _MainTex2, _MainTex3;
            sampler2D _MainTex4, _MainTex5, _MainTex6, _MainTex7;
            float _SlotAdd0, _SlotAdd1, _SlotAdd2, _SlotAdd3;
            float _SlotAdd4, _SlotAdd5, _SlotAdd6, _SlotAdd7;
            float4 _UVScaleOffset, _VertexColorTint;

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _VertexColorTint;
                output.uv = input.uv * _UVScaleOffset.xy + _UVScaleOffset.zw;
                output.slot = (uint)clamp((int)(input.slotData.x + 0.5), 0, 7);
                return output;
            }

            fixed4 SampleSlot(uint slot, float2 uv)
            {
                if (slot == 0) return tex2D(_MainTex0, uv);
                if (slot == 1) return tex2D(_MainTex1, uv);
                if (slot == 2) return tex2D(_MainTex2, uv);
                if (slot == 3) return tex2D(_MainTex3, uv);
                if (slot == 4) return tex2D(_MainTex4, uv);
                if (slot == 5) return tex2D(_MainTex5, uv);
                if (slot == 6) return tex2D(_MainTex6, uv);
                return tex2D(_MainTex7, uv);
            }

            float SlotRedBias(uint slot)
            {
                if (slot == 0) return _SlotAdd0;
                if (slot == 1) return _SlotAdd1;
                if (slot == 2) return _SlotAdd2;
                if (slot == 3) return _SlotAdd3;
                if (slot == 4) return _SlotAdd4;
                if (slot == 5) return _SlotAdd5;
                if (slot == 6) return _SlotAdd6;
                return _SlotAdd7;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = SampleSlot(input.slot, input.uv);
                color.r += SlotRedBias(input.slot);
                return saturate(color * input.color);
            }
            ENDHLSL
        }
    }
}

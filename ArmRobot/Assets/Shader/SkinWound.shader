Shader "Custom/SimpleSkinWound"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)

        _WoundMask ("Wound Mask (RT)", 2D) = "black" {}   // filled by script
        _RedColor ("Red Color", Color) = (1,0.25,0.25,1)
        _RedIntensity ("Red Intensity", Range(0,1)) = 0.75
        _Darken ("Darken Amount", Range(0,1)) = 0.5
        _BleedSpread ("Mask Power", Range(0.5,3)) = 1.2   // >1 = more concentrated
        _Depth01 ("Penetration 0..1", Range(0,1)) = 0     // fed by script
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalRenderPipeline" }
        LOD 200

        Pass
        {
            Name "Forward"
            Tags{ "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS: POSITION; float2 uv:TEXCOORD0; };
            struct Varyings  { float4 positionCS: SV_POSITION; float2 uv:TEXCOORD0; };

            TEXTURE2D(_BaseMap);       SAMPLER(sampler_BaseMap);
            TEXTURE2D(_WoundMask);     SAMPLER(sampler_WoundMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RedColor;
                float   _RedIntensity;
                float   _Darken;
                float   _BleedSpread;
                float   _Depth01;
            CBUFFER_END

            Varyings vert (Attributes v){
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i):SV_Target
            {
                float3 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;

                // wound mask (0..1), boosted by power and penetration
                float m = SAMPLE_TEXTURE2D(_WoundMask, sampler_WoundMask, i.uv).r;
                m = pow(saturate(m), _BleedSpread) * _Depth01;

                // darken then add red tint
                float3 darkened = lerp(baseCol, baseCol*(1.0 - _Darken), m);
                float3 reddened = lerp(darkened, _RedColor.rgb, m * _RedIntensity);

                return half4(reddened, 1);
            }
            ENDHLSL
        }
    }
}

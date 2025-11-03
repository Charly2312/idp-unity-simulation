Shader "Custom/SkinWoundSimple"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        _WoundMask ("Wound Mask (R)", 2D) = "black" {}
        _WoundColor ("Wound Color", Color) = (1,0,0,1)
        _WoundIntensity ("Wound Intensity", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            { 
                float4 pos:SV_POSITION;
                float2 uvMain:TEXCOORD0; 
                float2 uvMask:TEXCOORD1;
            };

            sampler2D _MainTex, _WoundMask;
            float4 _MainTex_ST;
            fixed4 _Color, _WoundColor;
            float _WoundIntensity;

            v2f vert(appdata v){
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvMain = TRANSFORM_TEX(v.uv, _MainTex); // base uses its own tiling
                o.uvMask = v.uv;                          // mask uses raw mesh UV (no tiling)
                return o;
            }

            fixed4 frag(v2f i):SV_Target
            {
                fixed4 baseC = tex2D(_MainTex, i.uvMain) * _Color;
                float m = tex2D(_WoundMask, i.uvMask).r;  // decoupled from _MainTex tiling
                float w = saturate(m * _WoundIntensity);
                baseC.rgb = lerp(baseC.rgb, _WoundColor.rgb, w);
                return baseC;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
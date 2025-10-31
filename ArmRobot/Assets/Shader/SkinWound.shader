Shader "Custom/SimpleSkinWoundBasic"
{
    Properties
    {
        _MainTex ("Skin Texture", 2D) = "white" {}
        _SkinColor ("Skin Color", Color) = (1, 0.627451, 0.4784314, 1)
        _WoundMask ("Wound Mask", 2D) = "black" {}
        _WoundColor ("Wound Color", Color) = (0.8, 0.1, 0.1, 1)
        _WoundIntensity ("Wound Intensity", Range(0, 2)) = 1.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

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
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _WoundMask;
            fixed4 _SkinColor;
            fixed4 _WoundColor;
            float _WoundIntensity;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample base texture
                fixed4 skin = tex2D(_MainTex, i.uv);
                
                // Apply skin color tint
                skin *= _SkinColor;
                
                // Sample wound mask
                fixed wound = tex2D(_WoundMask, i.uv).r;
                wound *= _WoundIntensity;
                
                // Blend between skin and wound color
                fixed3 finalColor = lerp(skin.rgb, _WoundColor.rgb, wound);
                
                return fixed4(finalColor, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
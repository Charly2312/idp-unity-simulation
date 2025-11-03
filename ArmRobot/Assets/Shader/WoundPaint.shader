Shader "Hidden/WoundPaint"
{
    Properties
    {
        _MainTex ("Base", 2D) = "black" {}
        _BrushPos ("Brush Position", Vector) = (0.5, 0.5, 0, 0)
        _BrushRadius ("Brush Radius", Float) = 0.02
        _Strength ("Strength", Range(0,1)) = 1
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        
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
            float4 _BrushPos;
            float _BrushRadius;
            float _Strength;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample existing mask
                float src = tex2D(_MainTex, i.uv).r;

                // Calculate distance from brush center
                float2 delta = i.uv - _BrushPos.xy;
                float dist = length(delta);
                float normalizedDist = dist / max(0.000001, _BrushRadius);

                // Soft circular brush with smoothstep falloff
                float brush = saturate(1.0 - smoothstep(0.0, 1.0, normalizedDist));
                brush *= _Strength;

                // Accumulate (max so overlapping stamps don't reduce intensity)
                float result = max(src, brush);

                return float4(result, result, result, 1);
            }
            ENDCG
        }
    }
}
Shader "Hidden/WoundPaint"
{
    Properties { _MainTex("Base",2D)="black"{} _BrushPos("BrushPos",Vector)=(0,0,0,0) _BrushRadius("Radius",Float)=0.02 _Strength("Strength",Range(0,1))=1 }
    SubShader
    {
        Tags{ "RenderType"="Opaque" "Queue"="Overlay" }
        Pass
        {
            ZWrite Off Cull Off ZTest Always
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(uint id:SV_VertexID){ v2f o; o.uv=float2((id<<1)&2,id&2); o.pos=float4(o.uv*2-1,0,1); return o; }
            sampler2D _MainTex; float4 _BrushPos; float _BrushRadius; float _Strength;
            fixed4 frag(v2f i):SV_Target
            {
                float2 uv=i.uv;
                float cur = tex2D(_MainTex, uv).r;
                float d = distance(uv, _BrushPos.xy);
                float s = saturate(1 - smoothstep(_BrushRadius*0.6, _BrushRadius, d));
                // max blend so wounds accumulate
                float outv = max(cur, s * _Strength);
                return fixed4(outv,outv,outv,1);
            }
            ENDHLSL
        }
    }
}

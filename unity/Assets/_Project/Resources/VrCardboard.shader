Shader "FlyingGame/VR Cardboard"
{
    Properties { _MainTex ("Eye", 2D) = "black" {} }
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            struct Input { float4 vertex:POSITION; float2 r:TEXCOORD0; float2 g:TEXCOORD1; float2 b:TEXCOORD2; };
            struct Output { float4 vertex:SV_POSITION; float2 r:TEXCOORD0; float2 g:TEXCOORD1; float2 b:TEXCOORD2; };
            float2 orient(float2 uv) {
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) uv.y = 1 - uv.y;
                #endif
                return uv;
            }
            Output vert(Input i) {
                Output o; o.vertex=UnityObjectToClipPos(i.vertex);
                o.r=orient(i.r); o.g=orient(i.g); o.b=orient(i.b); return o;
            }
            float valid(float2 uv) { return step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1); }
            fixed4 frag(Output i):SV_Target {
                return fixed4(tex2D(_MainTex,i.r).r*valid(i.r),tex2D(_MainTex,i.g).g*valid(i.g),tex2D(_MainTex,i.b).b*valid(i.b),1);
            }
            ENDCG
        }
    }
}

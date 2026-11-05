Shader "FlyingGame/VR HUD Text" {
    Properties { _MainTex ("Font", 2D) = "white" {} }
    SubShader {
        Tags { "Queue"="Overlay+1" "RenderType"="Transparent" }
        Pass {
            Cull Off ZTest Always ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            Output vert(Input i) {
                Output o; o.vertex=UnityObjectToClipPos(i.vertex);
                o.uv=i.uv; o.color=i.color; return o;
            }
            fixed4 frag(Output i):SV_Target {
                return fixed4(i.color.rgb, i.color.a * tex2D(_MainTex,i.uv).a);
            }
            ENDCG
        }
    }
}

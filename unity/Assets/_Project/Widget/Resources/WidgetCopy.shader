// Aero Widget: copies the scene texture into its part of the frame exactly (no blending — keeps the alpha as rendered).
Shader "FlyingGame/WidgetCopy"
{
    Properties { _MainTex ("Scene", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" }
        Blend Off
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag (v2f i) : SV_Target { return tex2D(_MainTex, i.uv); }
            ENDCG
        }
    }
}

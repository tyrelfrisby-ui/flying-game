// Water spray particles: soft radial alpha from the quad UV × vertex colour (particle colour/alpha).
Shader "FlyingGame/Spray"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            fixed4 _Color;
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2.0;
                float a = saturate(1.0 - r * r);
                return fixed4(i.color.rgb, i.color.a * a * a);
            }
            ENDCG
        }
    }
}

// A spinning propeller's blur disc (owner 2026-10-04: "make the props look like they are actually spinning"): an unlit,
// two-sided, alpha-blended textured quad — the texture carries the blade smears, _Color its tint and overall alpha.
// Force-included in the player via BuildScript.EnsureAlwaysIncludedShaders.
Shader "FlyingGame/PropBlur"
{
    Properties
    {
        _MainTex ("Blur", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (float4 vertex : POSITION, float2 uv : TEXCOORD0) { v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; return o; }
            fixed4 frag (v2f i) : SV_Target { return tex2D(_MainTex, i.uv) * _Color; }
            ENDCG
        }
    }
}

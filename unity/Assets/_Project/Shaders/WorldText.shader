// World-space signs (race gate numbers, rooftop ring numbers, labels): the font's alpha × the text's vertex colour, DEPTH-TESTED
// and BACK-FACE CULLED (owner 2026-10-06: "the numbers for the gates are double sided … a 4 becomes a weird looking house on
// two posts"). Unity's GUI text shaders draw through everything from both sides, so each digit showed its mirror image through
// the plate. Force-included in the player via BuildScript.EnsureAlwaysIncludedShaders.
Shader "FlyingGame/WorldText"
{
    Properties { _MainTex ("Font Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; o.uv = v.uv; return o; }
            fixed4 frag (v2f i) : SV_Target { fixed4 c = i.color; c.a *= tex2D(_MainTex, i.uv).a; return c; }
            ENDCG
        }
    }
}

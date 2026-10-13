// Airshow smoke puffs (SmokeTrail): a soft round puff from the quad UV × vertex alpha.
// The widget's frame is STRAIGHT alpha over a cleared (0,0,0,0) background (Syphon / NDI / ProRes 4444), so an ordinary
// blend would leave half-covered smoke GREY (white × a in the colour, a in the alpha). Three passes:
//  1. colour, the ordinary "over" — right over the aircraft, the grid, the vectors;
//  2. colour, where nothing was drawn yet (1 − destination alpha): the smoke's own white, so empty sky gets white smoke;
//  3. alpha, "over" — the smoke's coverage adds to what is there.
Shader "FlyingGame/Smoke"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+4" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        CGINCLUDE
        #include "UnityCG.cginc"
        struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
        struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
        fixed4 _Color;
        v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv; return o; }
        // (coverage clamped to 0…1 and a non-finite fragment discarded: one bad puff blended a column of the airframe to black,
        //  Ty 2026-10-13 — a NaN or negative alpha poisons "over" even where its weight should be zero)
        float Cover (v2f i) { float r = length(i.uv - 0.5) * 2.0; float a = saturate(1.0 - r * r); return saturate(i.color.a) * a * a; }
        void Guard (v2f i) { if (any(isnan(i.color)) || any(isinf(i.color)) || any(isnan(i.uv))) discard; }
        fixed4 fragOver (v2f i) : SV_Target { Guard(i); return fixed4(saturate(i.color.rgb), Cover(i)); }
        fixed4 fragFill (v2f i) : SV_Target { Guard(i); return fixed4(saturate(i.color.rgb) * step(0.002, Cover(i)), 0); }
        ENDCG
        Pass { ColorMask RGB  Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOver
            ENDCG }
        Pass { ColorMask RGB  Blend OneMinusDstAlpha One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragFill
            ENDCG }
        Pass { ColorMask A  Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOver
            ENDCG }
    }
}

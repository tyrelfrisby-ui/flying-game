// A livery marking laid on an airframe (Livery.cs / AirframeVisual.BuildDecals).
//
// ALPHA COMES FROM THE IMAGE'S OWN LUMINANCE, not from its alpha channel. The Talk Flying mark is authored as
// brushed silver on a solid black field; it carries an alpha channel but that channel is opaque, so drawing it
// with an ordinary transparent shader put a black square on the tail (2026-10-21). Taking luminance as alpha
// makes the black field vanish and the silver strokes stay, which is what a decal of a monochrome mark wants,
// and it means the SAME artwork serves a light tail and a dark one -- _Color tints it, so the mark goes on
// black over a silver 737 and silver over a dark fin, with no second file to keep in step.
//
// Built-in transparent shaders are stripped from the iOS player, so this is force-included via BuildScript
// alongside FlyingGame/UnlitTransparent.
Shader "FlyingGame/Decal"
{
    Properties
    {
        _MainTex ("Mark", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Luminance cutoff", Range(0, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Offset -1, -1          // sit proud of the skin it is painted on, instead of z-fighting with it
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _Cutoff;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.uv = TRANSFORM_TEX(uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                // the brightest channel: a grey mark on black gives its own coverage
                fixed l = max(c.r, max(c.g, c.b));
                // the field is never perfectly black once compressed, so ignore the bottom of the range and
                // stretch what is left back to full, or the whole tail picks up a faint wash
                l = saturate((l - _Cutoff) / max(1e-4, 1.0 - _Cutoff));
                return fixed4(_Color.rgb, l * _Color.a * c.a);
            }
            ENDCG
        }
    }
}

// The airframe's silhouette, seen from the sun, written into GroundShadow's water-shadow mask.
//
// This is a REPLACEMENT shader (Camera.RenderWithShader): the mask camera is orthographic and looks along the
// sun's own direction, so what it draws IS the shadow's outline — the same parallel projection GroundShadow has
// always used for the ground, taken from the other end. Everything that draws becomes white; the camera clears to
// black; Water.shader reads the one channel and darkens only what the sunlight was going to reach.
//
// No lighting, no texture, no vertex colour: a shadow has no colour of its own. Ty, 2026-11-11: "the reflection
// should show color etc where the shadow is just darkness".
Shader "FlyingGame/WaterShadowCaster"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off            // a wing seen edge-on from the sun still blocks the light
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert (float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag () : SV_Target { return 1; }
            ENDCG
        }
    }
}

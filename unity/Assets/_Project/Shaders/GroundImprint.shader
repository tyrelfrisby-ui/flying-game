// A mark in the ground: a rut, a skid, a ski track, a gouge or a wreck's scar (docs/GROUND-IMPRINTS.md,
// Core/GroundImprintMesh.cs, Bridge/GroundImprintVisual.cs). Ty 2026-11-10: "if it is penetrating it should leave
// imprints behind as it slides and rolls displacing the dirt or snow etc".
//
// A mark is NOT a hole in the geometry -- it is a flat sheet lying on the ground, and its depth is in its ALPHA: the
// record says a Cub's rut on a dirt strip is 3.3 mm, and 3.3 mm of displaced earth is flattened, bruised ground and
// not a ditch (GroundImprintDrawing.DepthScaleM carries that whole argument). Colour and alpha both come in on the
// vertices, from Core, so every platform's marks agree and nothing is tuned here.
//
// LIGHT is the island terrain's own day/sunset model, verbatim, because a mark has to stay a consistent shade darker
// than the ground it lies on whether that ground is in the sun or in a mountain's shadow. The terrain's aerial
// perspective is NOT repeated: a mark is a few centimetres across and is gone from the picture long before haze
// would matter.
//
// Built-in transparent shaders are stripped from the iOS player, so this is force-included via BuildScript alongside
// FlyingGame/UnlitTransparent and FlyingGame/Decal.
Shader "FlyingGame/GroundImprint"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // Two-sided: a mark under an overhang or seen from below a cliff edge is still a mark, and the sheet is flat
        // so there is no inside to see.
        Cull Off
        // Sit proud of the ground it is painted on instead of z-fighting it (the same trick FlyingGame/Decal uses on
        // an airframe's skin). GroundImprintDrawing.SurfaceLiftM lifts it 2 cm as well: the lift covers the gap
        // between the analytic ground the marks are recorded on and the 25 m mesh that is drawn, and this covers the
        // depth precision.
        Offset -2, -2
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _BubbleSunDir, _ReflectionClip;
            float _SunsetOn, _ReflectionRendering;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float4 c : COLOR; float clipDistance : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.clipDistance = dot(mul(unity_ObjectToWorld, v.vertex), _ReflectionClip);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.c = v.color;
                return o;
            }

            // No VFACE here, although the pass is two-sided: a mark is a flat sheet lying on the ground, so the only
            // way to see its back is from under an overhang, and lighting it from above there is invisibly wrong.
            // Mixing VFACE with the single-pass-instanced stereo macros is a combination nothing else in this project
            // uses, and the Quest is not the place to find out.
            fixed4 frag (v2f i) : SV_Target
            {
                if (_ReflectionRendering > .5) clip(i.clipDistance);
                float3 n = normalize(i.n);
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > .001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(.4, .8, -.4));
                float d = saturate(dot(n, l));
                float3 sky = lerp(float3(.30, .36, .43), float3(.50, .57, .64), n.y * .5 + .5);
                float3 dayLight = sky + float3(.54, .50, .42) * d;
                float3 col = i.c.rgb * _Color.rgb * lerp(dayLight, float3(.19, .22, .30) + float3(.81, .53, .29) * d, _SunsetOn);
                return fixed4(col, i.c.a * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}

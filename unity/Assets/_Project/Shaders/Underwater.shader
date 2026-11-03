// THE EYE UNDER WATER (P2d, UnderwaterRender): one camera-centred inverted sphere, drawn after the opaque world and
// before the water surface, that turns everything behind it into what an eye in a lake sees.
//   fog       Beer-Lambert on the UNDERWATER part of each pixel's ray only (from the eye to the surface, if the ray leaves
//             the water): f = 1 - exp(-sigma * d_water), sigma = 3.912 / visibility (Koschmieder, Core UnderwaterViewMode).
//             The distance comes from the camera depth texture (WaterReflection already turns it on for the main camera,
//             so this costs no extra depth pass); the sky (depth = far) goes to the water's own colour.
//   caustics  a cheap procedural pattern of moving bright ridges on whatever the pixel's ray hits under the surface,
//             fading with its depth under the surface and with the fog — no texture, a handful of sin() per pixel.
//   blend     everything scaled by _UwBlend (0..1 over 0.25 s at the boundary).
// Built-in pipeline, no keywords; single-pass-instanced / multiview macros as XrOverlay has them. Premultiplied output
// (Blend One OneMinusSrcAlpha): rgb = fog colour * f + caustic light, a = f. Force-included by BuildScript.
Shader "FlyingGame/Underwater"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Front
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float _UwBlend, _UwSurfaceY, _UwSigma;
            float4 _UwColor;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float4 sp : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.sp = ComputeScreenPos(o.pos);
                return o;
            }

            float Caustic(float2 p, float t)
            {
                float2 q = p * 0.9;
                float a = sin(q.x * 1.7 + t * 1.3 + sin(q.y * 2.3 + t * 0.7));
                float b = sin(q.y * 1.9 - t * 1.1 + sin(q.x * 2.9 - t * 0.9));
                float c = 1.0 - abs(a + b) * 0.5;
                return pow(saturate(c), 6.0);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_UwBlend <= 0.001) return fixed4(0, 0, 0, 0);
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.wp - ro);
                float eyeZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.sp)));
                float3 fwd = -UNITY_MATRIX_V[2].xyz;
                float dist = eyeZ / max(1e-3, dot(rd, fwd));
                // Only the part of the ray that is in the water.
                float wDist = dist;
                if (ro.y < _UwSurfaceY) { if (rd.y > 1e-4) wDist = min(dist, (_UwSurfaceY - ro.y) / rd.y); }
                else { wDist = rd.y < -1e-4 ? max(0.0, dist - (_UwSurfaceY - ro.y) / rd.y) : 0.0; }
                float keep = exp(-_UwSigma * wDist);
                float f = (1.0 - keep) * _UwBlend;
                float3 p = ro + rd * min(dist, 200.0);
                float below = _UwSurfaceY - p.y;
                float c = below > 0.0 && dist < 60.0 ? Caustic(p.xz, _Time.y) * exp(-below * 0.35) * keep : 0.0;
                float3 rgb = _UwColor.rgb * f + c * 0.22 * float3(0.80, 0.95, 1.0) * _UwBlend;
                return fixed4(rgb, f);
            }
            ENDCG
        }
    }
}

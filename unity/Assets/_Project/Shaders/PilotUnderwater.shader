// The pilot's view under water (Ty t922u; Core UnderwaterView has the rule and its tests). Drawn only by the seat camera,
// only while UnderwaterView.ModeFor says so (PilotUnderwater.cs enables the renderer for that camera's render and no
// other), on the inside of a small box around the eye: every pixel of the opaque scene is reached once, before the
// transparent queue, so glass, spray, the water surface and the HUD are drawn over it untouched.
//
// Per pixel: the opaque point from _CameraDepthTexture, the length of the eye→point ray under the plane
// y = _PilotWaterY (UnderwaterView.SubmergedLength), and Beer–Lambert murk along it — pass 1 absorbs (red first),
// pass 2 adds the water's own scattered colour. The waterline on screen is where the real surface crosses each part.
Shader "Hidden/FlyingGame/PilotUnderwater"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Front
        CGINCLUDE
            #include "UnityCG.cginc"
            #include "PilotUnderwater.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
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
            // The opaque point behind this pixel, as a segment from the eye; then PilotPath.
            float3 Path(v2f i)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 ray = i.wp - _WorldSpaceCameraPos;
                float boxZ = -mul(UNITY_MATRIX_V, float4(i.wp, 1)).z;
                float sceneZ = min(LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.sp))), _ProjectionParams.z);
                return PilotPath(_WorldSpaceCameraPos, ray * (sceneZ / max(1e-4, boxZ)));
            }
        ENDCG
        Pass
        {
            // Absorption: water takes red first, then green; the dry panel behind keeps its shapes, not its colours.
            Blend Zero SrcColor
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            fixed4 frag (v2f i) : SV_Target
            {
                float3 p = Path(i);
                return fixed4(exp(-p.x * _PilotWaterK * float3(1.7, .85, .7)), 1);
            }
            ENDCG
        }
        Pass
        {
            // In-scatter: the lake's own colour, brighter toward the daylight, darker the deeper the point.
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            fixed4 frag (v2f i) : SV_Target
            {
                float3 p = Path(i);
                return fixed4(_PilotWaterColor.rgb * p.z * p.y, 1);
            }
            ENDCG
        }
    }
}

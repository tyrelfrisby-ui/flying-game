// Minimal unlit shader that SUPPORTS GPU INSTANCING, for BubbleField's Graphics.DrawMeshInstanced.
// The built-in "Unlit/Color" shader has no instancing variant, so DrawMeshInstanced draws nothing with
// it — that was why the air bubbles never appeared on any platform. This one declares
// multi_compile_instancing + UNITY_SETUP_INSTANCE_ID so the per-instance transforms actually apply.
Shader "FlyingGame/BubbleInstanced"
{
    Properties
    {
        _Color ("Color", Color) = (0.85, 0.92, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}

// Plain unlit translucent colour (canopy glass, propeller discs). Built-in transparent shaders are
// stripped from the iOS player, so this tiny one is force-included via BuildScript.
Shader "FlyingGame/UnlitTransparent"
{
    Properties { _Color ("Color", Color) = (0.2, 0.3, 0.4, 0.5) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color;
            // Single-pass-instanced / multiview ready (the Quest renders both eyes in one pass; without the stereo
            // macros a translucent part drew in the left eye only). The same colour on a flat screen.
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}

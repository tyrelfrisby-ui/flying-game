// Thin polycarbonate sheet. One pass, no texture/depth sampling or extra camera.
// Force-included by BuildScript.EnsureAlwaysIncludedShaders.
Shader "FlyingGame/Lexan"
{
    Properties { _Color ("Smoke tint / opacity", Color) = (.588235, .627451, .647059, .18) }
    SubShader
    {
        // Queue 3006: AFTER everything a pilot sees THROUGH the floor - water (3000), waterfall (3001), the aeroplane's
        // own planar ground shadow (3002), smoke (3004), spray (3005) - so they are tinted and reflected by the sheet
        // instead of being drawn over it. (Transparent-10 put them all on top of the pane.) Still before decals and
        // airport glow (3010+). Seen from outside, through the canopy from above, it now draws after the canopy glass
        // (3000): at alpha .18 that ordering error is invisible, the cockpit view is what matters.
        Tags { "Queue"="Transparent+6" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float3 n = normalize(i.wn);
                // Schlick for polycarbonate IOR 1.586 (F0 = .0514), identical on both sides.
                float f = .0514 + .9486 * pow(1 - saturate(abs(dot(n, v))), 5);
                float3 r = reflect(-v, n);
                float3 sky = lerp(float3(.65,.72,.78), float3(.16,.36,.62), saturate(r.y));
                // Express absorption + reflection in ordinary alpha blending, retaining ground detail.
                float a = _Color.a * (1 - f) + f;
                float3 col = (_Color.rgb * _Color.a * (1 - f) + sky * f) / max(a, .001);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}

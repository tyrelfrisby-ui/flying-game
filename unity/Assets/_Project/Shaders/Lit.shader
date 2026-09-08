// Cheap directional shading for the airframe (one dot product, no shadow maps): flat unlit colour made
// thin surfaces vanish edge-on (the glider's stab from the chase view). Two-sided: generated lofts have
// arbitrary winding, so back faces are drawn with the normal flipped. Sun direction comes from the
// _BubbleSunDir global BubbleField sets each frame (fallback if unset).
Shader "FlyingGame/Lit"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
            };

            fixed4 _Color;
            float4 _BubbleSunDir;

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.n) * (facing >= 0 ? 1.0 : -1.0);
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float d = saturate(dot(n, l));
                return fixed4(_Color.rgb * (0.58 + 0.42 * d), 1.0);
            }
            ENDCG
        }
    }
}

// FlyingGame/Lit with per-vertex colour: big static scenery (the seaside city, the Golden Gate, Avalon, the pirate wreck)
// is batched into a few meshes whose vertices carry their colour — one draw call per landmark instead of one per box.
// Same cheap one-dot-product sun shading as Lit; two-sided.
Shader "FlyingGame/LitVC"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float4 c : COLOR; UNITY_FOG_COORDS(1) };

            fixed4 _Color;
            float4 _BubbleSunDir;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.c = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.n) * (facing >= 0 ? 1.0 : -1.0);
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float d = saturate(dot(n, l));
                fixed4 c = fixed4(i.c.rgb * _Color.rgb * (0.58 + 0.42 * d), 1.0);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}

// Terrain: vertex colour (grass/rock/dry by height+slope, computed on the CPU) × directional shading,
// plus farm-field grid lines on flat ground (vertex alpha = flatness) so the valley floor keeps the
// original 200 m field pattern. Two-sided not needed (closed surface).
Shader "FlyingGame/Terrain"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float4 c : COLOR; };
            fixed4 _Color; float4 _BubbleSunDir;
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; o.c = v.color; return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float d = saturate(dot(normalize(i.n), l));
                float3 col = i.c.rgb * (0.55 + 0.45 * d);
                // Field boundaries every 200 m on flat ground (alpha carries flatness).
                float2 g = abs(frac(i.wp.xz / 200.0) - 0.5);
                float fieldLine = 1.0 - smoothstep(0.47, 0.5, max(g.x, g.y));
                col *= 1.0 - 0.35 * fieldLine * i.c.a;
                return fixed4(col * _Color.rgb, 1);
            }
            ENDCG
        }
    }
}

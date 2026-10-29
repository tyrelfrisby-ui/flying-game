// FlyingGame/Lit with per-vertex colour: big static scenery (the seaside city, the Golden Gate, Avalon, the pirate wreck)
// is batched into a few meshes whose vertices carry their colour — one draw call per landmark instead of one per box.
// Same cheap one-dot-product sun shading as Lit; two-sided.
Shader "FlyingGame/LitVC"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) _Emission ("Self-lit fixtures", Range(0,1)) = 0 }
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
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float4 c : COLOR; float clipDistance : TEXCOORD2; UNITY_FOG_COORDS(1) };

            fixed4 _Color;
            float4 _BubbleSunDir;
            float _SunsetOn, _ReflectionRendering, _CityNight, _Emission, _UrbanNightFill;
            float4 _ReflectionClip;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.clipDistance = dot(mul(unity_ObjectToWorld, v.vertex), _ReflectionClip);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.c = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                if (_ReflectionRendering > .5) clip(i.clipDistance);
                float3 n = normalize(i.n) * (facing >= 0 ? 1.0 : -1.0);
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float d = saturate(dot(n, l));
                fixed4 c = fixed4(i.c.rgb * _Color.rgb * lerp(float3(.58,.58,.58) + .42*d, lerp(float3(.53,.51,.54), float3(.64,.65,.73), n.y * .5 + .5) + float3(.65,.43,.24)*d, _SunsetOn), 1.0);
                // Only the night fixture enables this. Alpha zero marks city lamp/beacon heads;
                // ordinary vertex colours retain alpha one and the day expression above is unchanged.
                float emission = 1-step(.01,i.c.a);
                c.rgb = lerp(c.rgb, c.rgb*lerp(float3(.20,.20,.20),float3(.23,.28,.38),_UrbanNightFill) + i.c.rgb*emission*1.6, _CityNight);
                c.rgb = lerp(c.rgb, i.c.rgb * _Color.rgb, _Emission);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}

// The generated airframe's skin. Replaces FlyingGame/Lit, which was one lambert term on a flat colour —
// `_Color.rgb * (0.58 + 0.42 * NdotL)` — with no texture, no specular and no surface detail. That, far more
// than polygon count, is what made the generated fleet look like a toy next to a downloaded model: a Cub mesh
// of 45k faces reads as real because it carries a normal map and a specular response, not because of its
// triangles.
//
// What this adds, in the order it matters:
//   * a SPECULAR highlight, so metal reads as metal and doped fabric does not. A highlight that moves with the
//     eye is most of what "material" means to a viewer.
//   * a tiling DETAIL NORMAL map — panel lines and rivets. On a real model these are not geometry either; they
//     are a normal map, which is why a good model stays cheap. Generated in code (AirframeDetail.cs) and
//     scaled in METRES, so a rivet is the same size on a Cub as on a 737.
//   * FRESNEL rim, which is what makes a curved painted surface look curved and painted.
//
// Deliberately Blinn-Phong rather than full PBR: this ships to a phone, the fleet is flat-coloured paint, and
// the expensive half of PBR (image-based lighting) buys little without an environment probe.
//
// Two-sided like FlyingGame/Lit — the generated meshes have arbitrary winding, so back faces are drawn with the
// normal flipped. Sun direction comes from the global _BubbleSunDir that BubbleField/WorldBuilder set.
Shader "FlyingGame/Airframe"
{
    Properties
    {
        _WaterMetal ("Float aluminium environment", Float) = 0
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _DetailNormal ("Panel lines + rivets (normal)", 2D) = "bump" {}
        _DetailTile ("Detail tiling (per metre)", Float) = 1.0
        _DetailDepth ("Detail depth", Range(0, 2)) = 1.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _SpecTint ("Specular tint", Color) = (1, 1, 1, 1)
        _SpecStrength ("Specular strength", Range(0, 2)) = 0.35
        _FresnelStrength ("Fresnel", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "SunsetSky.cginc"

            fixed4 _Color, _SpecTint;
            sampler2D _DetailNormal;
            float _DetailTile, _DetailDepth, _Smoothness, _SpecStrength, _FresnelStrength;
            float4 _BubbleSunDir;
            float _ReflectionRendering;
            float _WaterMetal;
            float4 _ReflectionClip;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
                float3 t : TEXCOORD1;
                float3 b : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float3 wpos : TEXCOORD4;
                float clipDistance : TEXCOORD5;
            };

            v2f vert (float4 vertex : POSITION, float3 normal : NORMAL, float4 tangent : TANGENT, float2 uv : TEXCOORD0)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.clipDistance = dot(mul(unity_ObjectToWorld, vertex), _ReflectionClip);
                o.n = UnityObjectToWorldNormal(normal);
                o.t = UnityObjectToWorldDir(tangent.xyz);
                // tangent.w carries the bitangent sign; unity_WorldTransformParams.w handles a mirrored scale
                o.b = cross(o.n, o.t) * tangent.w * unity_WorldTransformParams.w;
                o.uv = uv * _DetailTile;
                o.wpos = mul(unity_ObjectToWorld, vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                if (_ReflectionRendering > .5) clip(i.clipDistance);
                float3 n = normalize(i.n) * (facing >= 0 ? 1.0 : -1.0);

                // surface detail: panel lines and rivets, in tangent space
                float3 tn = UnpackNormal(tex2D(_DetailNormal, i.uv));
                tn.xy *= _DetailDepth * lerp(.25, .025, _SunsetOn);
                float3 t = normalize(i.t), b = normalize(i.b);
                n = normalize(t * tn.x + b * tn.y + n * tn.z);

                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001
                           ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 h = normalize(l + v);

                float d = saturate(dot(n, l));
                // the same 0.58 ambient floor FlyingGame/Lit used, so nothing in the fleet changes brightness
                float3 col = _Color.rgb * lerp(float3(.58,.58,.58) + .42*d, float3(.24,.27,.34) + SunsetEnvironment(n)*.60 + float3(.60,.37,.18)*d, _SunsetOn);

                // Broad sky reflection gives curved paint a cool-to-warm response even
                // away from the tiny sun. Analytic environment, no cubemap or extra pass.
                float3 env = SunsetEnvironment(reflect(-v,n));
                col += _SunsetOn * env * (.10 + .18 * _Smoothness) *
                    (.35 + .65 * pow(1-saturate(dot(n,v)), 2));

                // Aluminium floats trade diffuse plastic fill for directional sky response.
                if (_WaterMetal > .5)
                {
                float3 metalEnv = lerp(float3(.13,.20,.28),float3(.62,.74,.85),saturate(n.y*.5+.5));
                if (_SunsetOn > .5) metalEnv = SunsetEnvironment(reflect(-v,n))
                    * lerp(.22,1.10,saturate(n.y*.65+.40));
                col = lerp(col, col*.22 + metalEnv*_Color.rgb*.95, .85);
                }

                // Blinn-Phong: smoothness drives the exponent, so a polished cowl is tight and fabric is broad
                float power = exp2(_Smoothness * 10.0) + 1.0;
                float spec = pow(saturate(dot(n, h)), power) * _SpecStrength * d;
                col += _SpecTint.rgb * spec * lerp(float3(1,1,1),float3(1,.66,.38),_SunsetOn);

                // a painted curve catches the sky at its edge
                float fres = pow(1.0 - saturate(dot(n, v)), 4.0) * _FresnelStrength;
                col += fres * lerp(float3(1,1,1), float3(1,.77,.53), _SunsetOn);

                col = SunsetAirframeResponse(col, normalize(_WorldSpaceCameraPos - i.wpos), _ReflectionRendering);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback "FlyingGame/Lit"
}

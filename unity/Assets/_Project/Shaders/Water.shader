// Glassy water: fresnel sky reflection + planar reflection of the aircraft (rendered by WaterReflection
// into _ReflectionTex) + sun glint + faint animated ripples. Reflection texture is only valid for the
// water plane at _ReflPlaneY (the lake under the aircraft); other lakes fall back to the sky term.
Shader "FlyingGame/Water"
{
    Properties
    {
        _Color ("Deep colour", Color) = (0.08, 0.28, 0.5, 0.92)
        _SkyColor ("Sky", Color) = (0.45, 0.66, 0.95, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float4 sp : TEXCOORD1; };
            fixed4 _Color, _SkyColor;
            sampler2D _ReflectionTex;
            float _ReflPlaneY, _ReflOn;
            float4 _BubbleSunDir;
            // Wake waves (WakeWaves.cs): height field over the active lake, its world rect and texel, and that lake's surface.
            sampler2D _WakeTex;
            float4 _WakeRect, _WakeTexel;
            float _WakeOn, _WakeY;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.sp = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                // Faint ripples: two crossing wave trains perturb the normal.
                float2 p = i.wp.xz;
                float a = sin(p.x * 0.35 + p.y * 0.2 + t * 1.1) + 0.6 * sin(p.x * 0.9 - p.y * 0.7 + t * 1.7);
                float b = cos(p.x * 0.25 - p.y * 0.4 + t * 0.9) + 0.6 * cos(p.x * 0.6 + p.y * 1.1 + t * 1.4);
                float3 n = normalize(float3(a * 0.02, 1.0, b * 0.02));
                float foam = 0;
                if (_WakeOn > 0.5 && abs(i.wp.y - _WakeY) < 1.5)
                {
                    float2 wuv = (i.wp.xz - _WakeRect.xy) * _WakeRect.zw;
                    if (all(wuv > 0) && all(wuv < 1))
                    {
                        // Slope of the wake field (central differences, metres per metre) tilts the normal.
                        float hx = tex2D(_WakeTex, wuv + float2(_WakeTexel.x, 0)).r - tex2D(_WakeTex, wuv - float2(_WakeTexel.x, 0)).r;
                        float hz = tex2D(_WakeTex, wuv + float2(0, _WakeTexel.y)).r - tex2D(_WakeTex, wuv - float2(0, _WakeTexel.y)).r;
                        float2 slope = float2(hx, hz) / (2.0 * _WakeTexel.z);
                        n = normalize(float3(n.x - slope.x * 2.0, 1.0, n.z - slope.y * 2.0));
                        foam = saturate((length(slope) - 0.12) * 4.0) * 0.4;   // only the steepest crests froth
                    }
                }
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float ndv = saturate(dot(n, v));
                float fresnel = 0.06 + 0.9 * pow(1.0 - ndv, 3.0);

                float3 refl = _SkyColor.rgb;
                if (_ReflOn > 0.5 && abs(i.wp.y - _ReflPlaneY) < 1.5)
                {
                    float4 uv = i.sp; uv.xy += n.xz * 0.6 * uv.w;   // ripple distortion
                    fixed4 r = tex2Dproj(_ReflectionTex, UNITY_PROJ_COORD(uv));
                    refl = lerp(refl, r.rgb, r.a);
                }
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), 220.0) * 1.2;
                float3 col = lerp(_Color.rgb, refl, fresnel) + spec;
                col = lerp(col, float3(0.92, 0.95, 0.97), foam);
                return fixed4(col, _Color.a);
            }
            ENDCG
        }
    }
}

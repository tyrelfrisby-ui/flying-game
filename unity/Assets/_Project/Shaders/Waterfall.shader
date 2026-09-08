// Flowing water for the plunge waterfalls: a translucent curtain with streaks racing down it (v scrolls
// with time at the fall speed), whitening with distance fallen (aeration), plus a MIST mode (_Mist = 1)
// for the plunge-pool cloud: a soft radial cloud with slow drifting noise. Unlit, iOS-safe (no textures).
Shader "FlyingGame/Waterfall"
{
    Properties
    {
        _Color ("Water", Color) = (0.80, 0.90, 1.0, 0.82)
        _Speed ("Streak speed (uv/s)", Float) = 0.9
        _Mist ("Mist mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };
            fixed4 _Color; float _Speed, _Mist;

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash(i), b = hash(i + float2(1, 0)), c = hash(i + float2(0, 1)), d = hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                if (_Mist > 0.5)
                {
                    // Plunge-pool mist: radial soft cloud, drifting/boiling noise, fading with height (uv.y).
                    float2 c = i.uv - 0.5;
                    float r = length(c * float2(1.0, 1.4)) * 2.0;
                    float n = vnoise(i.uv * 5.0 + float2(t * 0.15, -t * 0.35)) * 0.6 + vnoise(i.uv * 11.0 - float2(0, t * 0.6)) * 0.4;
                    float a = saturate(1.0 - r) * saturate(1.0 - i.uv.y * 0.9) * (0.45 + 0.55 * n);
                    return fixed4(0.93, 0.96, 1.0, a * _Color.a);
                }
                // Curtain: u across, v down the fall. Streaks = stretched noise scrolling down; two layers at
                // different rates read as sheets of water sliding over each other. Water speeds up as it
                // falls, so lower streaks scroll faster (v * (1 + v)).
                float v = i.uv.y, u = i.uv.x;
                float s1 = vnoise(float2(u * 28.0, v * 6.0 - t * _Speed * (1.0 + v)));
                float s2 = vnoise(float2(u * 60.0 + 3.1, v * 14.0 - t * _Speed * 1.7 * (1.0 + v)));
                float streak = smoothstep(0.35, 0.8, s1 * 0.65 + s2 * 0.35);
                // Aeration: the sheet breaks into white water with distance fallen.
                float foam = saturate(v * 1.3) * (0.5 + 0.5 * s2);
                float3 col = lerp(_Color.rgb, float3(1, 1, 1), 0.35 * streak + 0.6 * foam);
                // Edges of the curtain are ragged/thinner.
                float edge = smoothstep(0.0, 0.08, u) * smoothstep(0.0, 0.08, 1.0 - u);
                float a = _Color.a * (0.55 + 0.45 * streak) * edge * (0.85 + 0.15 * foam);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}

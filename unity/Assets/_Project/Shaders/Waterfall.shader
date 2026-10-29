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
            #pragma target 3.0
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
                if (_Mist > 2.5)
                {
                    // Fragmented rising spray, soft on every edge; no solid rectangular cards.
                    float across = pow(saturate(1-abs(i.uv.x*2-1)),2);
                    float travel = frac(i.uv.y - t*.65 + hash(floor(i.wp.xz*.1)));
                    float drops = smoothstep(.50,.76,vnoise(float2(i.uv.x*8,travel*18)));
                    float alpha = across * sin(i.uv.y*3.14159) * drops * _Color.a;
                    return fixed4(_Color.rgb,alpha);
                }
                if (_Mist > 1.5)
                {
                    float2 p = (i.uv-.5)*2;
                    float r = length(p);
                    float2 flow = p / max(.12,r) * t*.22;
                    float n = vnoise(p*9-flow)*.65 + vnoise(p*23-flow*2)*.35;
                    float core = 1-smoothstep(.12,.65,r);
                    float lace = smoothstep(.40,.68,n);
                    float coverage = (core*.82+lace*.65)*(1-smoothstep(.55,1,r));
                    return fixed4(lerp(float3(.47,.66,.64),_Color.rgb,.65+lace*.35),coverage*_Color.a);
                }
                if (_Mist > 0.5)
                {
                    // Plunge-pool mist: radial soft cloud, drifting/boiling noise, fading with height (uv.y).
                    float2 c = i.uv - 0.5;
                    float r = length(c * float2(1.0, 1.4)) * 2.0;
                    float n = vnoise(i.uv * 5.0 + float2(t * 0.15, -t * 0.35)) * 0.6 + vnoise(i.uv * 11.0 - float2(0, t * 0.6)) * 0.4;
                    float a = pow(saturate(1.0 - r), 1.4) * saturate(i.uv.y*12) * (1-smoothstep(.35,1,i.uv.y)) * (.3 + .7*n);
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
                float foam = smoothstep(.08,.75,v) * (.55 + .45 * s2);
                float3 col = lerp(float3(.30,.49,.48),float3(.94,.97,.96),saturate(.25 + .48*streak + .65*foam));
                // Edges of the curtain are ragged/thinner.
                float ragged = .025 + .035*vnoise(float2(v*32-t*2,u*5));
                float edge = smoothstep(0.0, ragged, u) * smoothstep(0.0, ragged, 1.0 - u);
                float holes = lerp(1,smoothstep(.20,.42,s1*.6+s2*.4),v*.65);
                float a = _Color.a * (.45 + .4*streak + .15*foam) * edge * holes;
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}

// Glassy water: directional sky + tiered planar reflection of aircraft and scenery (WaterReflection
// writes _ReflectionTex), depth absorption, sun glint and ambient ripples. Reflection is valid for the
// water plane at _ReflPlaneY (the lake under the aircraft); other lakes fall back to the sky term.
Shader "FlyingGame/Water"
{
    Properties
    {
        _Color ("Deep colour", Color) = (0.08, 0.28, 0.5, 0.92)
        _SceneryWater ("Scenery depth palette", Float) = 0
        _RiverFlow ("River flow", Float) = 0
        _FlowDirection ("Flow direction", Vector) = (1,0,0,0)
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
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "SunsetSky.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            struct appdata { float4 vertex : POSITION; float2 depth : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float4 sp : TEXCOORD1; float2 depth : TEXCOORD2; };
            fixed4 _Color, _SkyColor;
            sampler2D _ReflectionTex;
            float _ReflPlaneY, _ReflOn;
            float _SceneryWater, _RiverFlow; float4 _FlowDirection;
            float4 _BubbleSunDir;
            // Wake waves (WakeWaves.cs): height field over the active lake, its world rect and texel, and that lake's surface.
            sampler2D _WakeTex, _WakeFoamTex;
            float4 _WakeRect, _WakeTexel;
            float _WakeOn, _WakeY;
            float4 _FoamOrigins[2], _FoamMotion[2];

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 q = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(hash(q),hash(q+float2(1,0)),f.x),lerp(hash(q+float2(0,1)),hash(q+1),f.x),f.y);
            }
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.sp = ComputeScreenPos(o.pos); o.depth = v.depth;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                // Faint ripples: two crossing wave trains perturb the normal.
                float2 p = i.wp.xz;
                float a = sin(p.x * 0.35 + p.y * 0.2 + t * 1.1) + 0.6 * sin(p.x * 0.9 - p.y * 0.7 + t * 1.7);
                float b = cos(p.x * 0.25 - p.y * 0.4 + t * 0.9) + 0.6 * cos(p.x * 0.6 + p.y * 1.1 + t * 1.4);
                float scenery = _SceneryWater * (1 - _SunsetOn);
                // Scenery uses irregular capillary patches, avoiding two visible crossing sine grids.
                if (scenery > .5)
                {
                    float warp = noise(p*.017 + t*.025)*5;
                    a = noise(p*.19 + float2(warp,-t*.24))-.5;
                    b = noise(p*.27 + float2(t*.18,warp)+31.7)-.5;
                }
                float ripple = lerp(lerp(.012,.009,scenery), .0007, _WaterCalm);
                float3 n = normalize(float3(a * ripple, 1.0, b * ripple));
                float foam = 0;
                float taxiGlint = 0;
                if (_RiverFlow > .5 && scenery > .5)
                {
                    float2 along = _FlowDirection.xy;
                    float2 across = float2(-along.y, along.x);
                    float2 flow = float2(dot(p, across), dot(p, along) - t * 3.4);
                    float coarse = noise(flow * float2(.20, .045));
                    float fine = noise(flow * float2(.65, .13));
                    n = normalize(float3(n.x + (coarse-.5)*.065, 1, n.z + (fine-.5)*.045));
                    // Only sloping reaches break; flat water stays clear. Motion is cosmetic.
                    float slope = length(float2(ddx(i.wp.y),ddy(i.wp.y))) /
                        max(.01, length(ddx(p)) + length(ddy(p)));
                    float rapid = smoothstep(.025,.13,slope);
                    foam = rapid * smoothstep(.48,.75,coarse*.6+fine*.4) * .72;
                }
                if (_WakeOn > 0.5 && abs(i.wp.y - _WakeY) < 1.5)
                {
                    float2 wuv = (i.wp.xz - _WakeRect.xy) * _WakeRect.zw;
                    if (all(wuv > 0) && all(wuv < 1))
                    {
                        // Slope of the wake field (central differences, metres per metre) tilts the normal.
                        float hx = tex2D(_WakeTex, wuv + float2(_WakeTexel.x, 0)).r - tex2D(_WakeTex, wuv - float2(_WakeTexel.x, 0)).r;
                        float hz = tex2D(_WakeTex, wuv + float2(0, _WakeTexel.y)).r - tex2D(_WakeTex, wuv - float2(0, _WakeTexel.y)).r;
                        float2 slope = float2(hx, hz) / (2.0 * _WakeTexel.z);
                        // Keep the solver intact; limit only optical slope amplification. The old
                        // 2x slope made every radial forcing pulse read as a circular ring.
                        n = normalize(float3(n.x - slope.x * .25, 1.0, n.z - slope.y * .25));
                        // World-space foam rafts break up the crest history.
                        float history = max(tex2D(_WakeFoamTex, wuv).r, saturate((length(slope) - .12) * 4) * .4);
                        float lace = noise(p * 1.7 + float2(t * .025, 0)) * .65 + noise(p * 5.3) * .35;
                        float coverage = smoothstep(.48, .78, lace);
                        foam = coverage * saturate(history) * .16;
                        // Cosmetic Kelvin envelope, anchored to each wet step and its measured speed/load.
                        // It supplements the coarse solver grid; no height/forcing constants are changed.
                        for (int f = 0; f < 2; f++)
                        {
                            float4 origin = _FoamOrigins[f], motion = _FoamMotion[f];
                            // Wet displacement taxi: normal-only bow shoulders and a short
                            // diverging ripple train. No planing whitewater at walking pace.
                            if (origin.z > 0 && motion.z > .05 && motion.z < 4)
                            {
                                float2 taxiDelta = p - origin.xy;
                                float2 across = float2(motion.y, -motion.x);
                                float taxiAft = -dot(taxiDelta, motion.xy);
                                float taxiSide = dot(taxiDelta, across);
                                float spread = origin.z * .55 + max(0,taxiAft) * .42;
                                float edge = abs(taxiSide) - spread;
                                float envelope = exp(-edge*edge/.22) * exp(-max(0,taxiAft)/9)
                                    * smoothstep(-.8,.6,taxiAft);
                                float oscillation = sin(edge*9-t*.9);
                                taxiGlint = max(taxiGlint, envelope * pow(saturate(oscillation),6)
                                    * .035 * saturate(motion.z/2.5));
                                float bow = exp(-taxiSide*taxiSide/max(.1,origin.z*origin.z)
                                    -(taxiAft+2)*(taxiAft+2)*2);
                                float tilt = (envelope*oscillation*.022 + bow*.018)
                                    * saturate(motion.z/2.5);
                                n = normalize(float3(n.x + across.x*sign(taxiSide)*tilt,
                                    n.y, n.z + across.y*sign(taxiSide)*tilt));
                            }
                            if (origin.w <= .001) continue;
                            float2 delta = p - origin.xy;
                            float aft = -dot(delta, motion.xy);
                            float signedSide = dot(delta, float2(motion.y, -motion.x));
                            float side = abs(signedSide);
                            float age = max(0, aft) / max(1, motion.z);
                            float width = max(.28, origin.z * .65) + max(0, aft) * .032;
                            // The outer arm of each float forms the fleet's visible V. Inner
                            // arms interfere and dissipate; retaining both produced four rails.
                            float outward = motion.w == 0 ? 1 : smoothstep(-.15, .15, signedSide * motion.w);
                            float wander = (noise(float2(aft * .38, f * 7.3)) - .5) * width;
                            float armDistance = abs(side - max(0, aft) * .354 - origin.z * .35 + wander);
                            float ragged = noise(float2(aft * .65, signedSide * 3.1));
                            float arms = 1 - smoothstep(width * .12, width * (.65 + ragged * .7), armDistance);
                            float fade = exp(-age * .8) * smoothstep(0, 1.2, aft);
                            // Connected young cusp line, becoming elongated, broken rafts downstream.
                            float streak = noise(float2(aft * .19, signedSide * 4.5) + f * 13.1);
                            float breakup = lerp(.9, smoothstep(.36, .68, streak), saturate(age / 1.4));
                            float turbulentWidth = origin.z * .7 + max(0,aft) * .07;
                            float stepFoam = exp(-side * side / max(.2, turbulentWidth * turbulentWidth)) * exp(-max(0,aft) / 6);
                            stepFoam *= .65 + .35 * smoothstep(.25, .7, noise(float2(aft * 1.7, signedSide * 3.2)));
                            float transverse = pow(saturate(cos(aft * 1.8 / max(1, motion.z / 8) + side * side / max(1,aft))), 12);
                            float inside = 1 - smoothstep(max(0,aft)*.30, max(0,aft)*.354 + .1, side);
                            float active = origin.w * stepFoam * 1.25 + origin.w * fade * (arms * outward * breakup * .8 + inside * transverse * .045);
                            foam = max(foam, active * step(0, aft));
                        }
                        n = normalize(lerp(n, float3(0,1,0), foam * .65));
                    }
                }
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float ndv = saturate(dot(n, v));
                float fresnel = .0204 + .9796 * pow(1.0 - ndv, 5.0);

                float3 refl = DaySky(reflect(-v,n));
                if (_SunsetOn > .5) refl = SunsetSky(reflect(-v,n));
                if (_ReflOn > 0.5 && abs(i.wp.y - _ReflPlaneY) < 1.5)
                {
                    float4 uv = i.sp; uv.xy += n.xz * 0.025 * uv.w;   // ripple distortion
                    fixed4 r = tex2Dproj(_ReflectionTex, UNITY_PROJ_COORD(uv));
                    refl = lerp(refl, r.rgb, r.a);
                }
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), 220.0) * 1.2 * lerp(1.0, .22, _WaterCalm * _SunsetOn);
                // Screen depth is measured along the view ray; vertical depth removes the grazing-angle bias.
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.sp)));
                float surfaceZ = -mul(UNITY_MATRIX_V, float4(i.wp,1)).z;
                float depth = max(0, sceneZ - surfaceZ) * max(.05, v.y);
                float3 deep = lerp(_Color.rgb, float3(.025,.045,.06), _SunsetOn);
                float3 shallow = lerp(_Color.rgb * 1.3, float3(.16,.14,.105), _SunsetOn);
                if (scenery > .5)
                {
                    if (i.depth.y > .5) depth = max(0,i.depth.x);
                    shallow = float3(.24,.30,.24);
                    deep = _Color.rgb * float3(.78,.90,.80);
                }
                float3 body = lerp(shallow, deep, 1-exp(-depth*.22));
                float3 sunTint = lerp(float3(1,1,1),float3(1,.68,.36),_SunsetOn);
                // A calm sunset lake reflects the luminous deck well into the foreground.
                // Keep silhouettes and sky on the same reflection weight; no additive sky wash.
                float mirror = lerp(fresnel, max(fresnel, .91 + .07 * pow(1-ndv, 2)), _WaterCalm * _SunsetOn);
                float3 col = lerp(body, refl, mirror) + spec * sunTint * (1 - foam);
                col += refl * taxiGlint;
                col = lerp(col, float3(0.92, 0.95, 0.97), foam);
                // Sub-metre edge blends the existing mesh into the damp bank rather than a blue cutout.
                float alpha = scenery > .5 && i.depth.y > .5 ? smoothstep(0,1.2,depth) : 1;
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}

// Water volume: directional sky + tiered planar reflection of aircraft and scenery (WaterReflection
// writes _ReflectionTex), depth absorption, sun glint and ambient ripples. Reflection is valid for the
// water plane at _ReflPlaneY (the lake under the aircraft); other lakes fall back to the sky term.
Shader "FlyingGame/Water"
{
    Properties
    {
        _Color ("Deep colour", Color) = (0.08, 0.28, 0.5, 0.92)
        _SceneryWater ("Scenery depth palette", Float) = 0
        _PowellWater ("Powell shallow tint", Float) = 0
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
            // The pilot's head in or by the lake (PilotUnderwater.cs; _PilotWaterMode is 0 for every other camera and view).
            #include "PilotUnderwater.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            struct appdata { float4 vertex : POSITION; float2 depth : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float4 sp : TEXCOORD1; float2 depth : TEXCOORD2; };
            fixed4 _Color, _SkyColor;
            sampler2D _ReflectionTex;
            float4 _ReflectionTex_TexelSize;
            float _ReflPlaneY, _ReflOn;
            float _SceneryWater, _RiverFlow, _PowellWater; float4 _FlowDirection;
            float4 _BubbleSunDir;
            // Wake waves (WakeWaves.cs): height field over the active lake, its world rect and texel, and that lake's surface.
            sampler2D _WakeTex, _WakeFoamTex;
            float4 _WakeRect, _WakeTexel;
            float _WakeOn, _WakeY;
            // Headset only (QuestRig / QuestRecorder; 0 everywhere else, so nothing changes on a phone or the desktop):
            // _WaterDepthErrK ≈ the depth buffer's error in metres per metre² of distance, so the depth term below lets
            // go where it is noise instead of flickering; _WaterSceneDepthOff = 1 while a camera with no depth
            // texture of its own (the clip recorder's) renders, which would otherwise read another camera's.
            float _WaterDepthErrK, _WaterSceneDepthOff;
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
                                // Curved, short wave packets rather than a continuous
                                // ridge along a straight V. Taxi strength stays normal-only.
                                float downstream = max(0,taxiAft);
                                float spread = origin.z * .55 + downstream * .34 + .12*sqrt(downstream);
                                float edge = abs(taxiSide) - spread;
                                float envelope = exp(-edge*edge/.48) * exp(-downstream/2.8)
                                    * smoothstep(-.8,.6,taxiAft) * (1-smoothstep(5,10,taxiAft));
                                float phase = taxiAft*3.5 + edge*4.0 - t*1.7;
                                float packet = pow(saturate(sin(phase)),3);
                                float oscillation = sin(phase);
                                taxiGlint = max(taxiGlint, envelope * packet
                                    * .055 * saturate(motion.z/2.5));
                                float bow = exp(-taxiSide*taxiSide/max(.1,origin.z*origin.z)
                                    -(taxiAft+2)*(taxiAft+2)*2);
                                // Compact radial packets around each step: bounded to a few metres.
                                float radius = length(taxiDelta);
                                float ringFade = exp(-radius/1.9) * (1-smoothstep(2.8,4.5,radius));
                                float ringPhase = radius*8.5-t*2.4;
                                float ring = sin(ringPhase)*ringFade;
                                taxiGlint = max(taxiGlint, (.11*envelope*packet
                                    + .075*ringFade*pow(saturate(cos(ringPhase)),8))
                                    * saturate(motion.z/2.5));
                                float2 radial = taxiDelta/max(.15,radius);
                                n = normalize(float3(n.x + radial.x*ring*.055, 1, n.z + radial.y*ring*.055));
                                float tilt = (envelope*oscillation*.055 + bow*.075)
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
                if (_PilotWaterMode > 1.5 && _WorldSpaceCameraPos.y < i.wp.y - .01)
                {
                    // Seen from under the surface by the pilot: Snell's window (the sky, inside 48.6 deg of straight up,
                    // already murked behind this by PilotUnderwater) and, outside it, total internal reflection of the
                    // lit water. Without this the underside drew the sky mirror at fresnel 1: a pale opaque band across
                    // the flooded cockpit (Ty's t922u screenshot).
                    float window = smoothstep(.62, .70, saturate(dot(n, -v)));
                    float3 seen = PilotPath(_WorldSpaceCameraPos, i.wp - _WorldSpaceCameraPos);
                    float3 lit = _PilotWaterColor.rgb * seen.z;
                    return fixed4(lerp(lit * 1.25, lit, seen.y), lerp(1, .06 + .9 * seen.y, window));
                }
                float3 refl;
                if (_SunsetOn > .5) refl = SunsetSky(reflect(-v,n));
                else refl = DaySkyRough(reflect(-v,n), .85 * (1-fresnel));
                if (_ReflOn > 0.5 && abs(i.wp.y - _ReflPlaneY) < 1.5)
                {
                    float4 uv = i.sp; uv.xy += n.xz * 0.025 * uv.w;   // ripple distortion
                    float2 mirrorUV = uv.xy / uv.w;
                    // A small fixed five-tap footprint softens the finite mirror target.
                    // No second camera, full-screen colour copy, or temporal image history.
                    float2 blur = _ReflectionTex_TexelSize.xy * lerp(1.8, .65, _WaterCalm);
                    fixed4 r = tex2D(_ReflectionTex, mirrorUV) * .4;
                    r += tex2D(_ReflectionTex, mirrorUV + float2(blur.x, 0)) * .15;
                    r += tex2D(_ReflectionTex, mirrorUV - float2(blur.x, 0)) * .15;
                    r += tex2D(_ReflectionTex, mirrorUV + float2(0, blur.y)) * .15;
                    r += tex2D(_ReflectionTex, mirrorUV - float2(0, blur.y)) * .15;
                    refl = lerp(refl, r.rgb, r.a);
                }
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), 220.0) * 1.2 * lerp(1.0, .22, _WaterCalm * _SunsetOn);
                // Screen depth is measured along the view ray; vertical depth removes the grazing-angle bias.
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.sp)));
                float surfaceZ = -mul(UNITY_MATRIX_V, float4(i.wp,1)).z;
                float path = max(0, sceneZ - surfaceZ);
                float depth = path * max(.05, v.y);
                // Out where the buffer cannot resolve the seabed from the surface, read it as deep water, smoothly.
                depth = lerp(depth, 12, saturate(surfaceZ * surfaceZ * _WaterDepthErrK - .25));
                if (_WaterSceneDepthOff > .5) depth = 12;
                float3 deep = lerp(_Color.rgb * float3(.50,.72,.64), float3(.025,.045,.06), _SunsetOn);
                float3 shallow = lerp(_Color.rgb * float3(.72,1.0,.80), float3(.16,.14,.105), _SunsetOn);
                if (scenery > .5)
                {
                    // Authored bed depth is a fallback/cap; closer submerged hulls still win.
                    if (i.depth.y > .5) depth = min(depth, max(0,i.depth.x));
                    shallow = lerp(float3(.24,.30,.24),float3(.13,.43,.43),_PowellWater);
                    deep = _Color.rgb * float3(.78,.90,.80);
                }
                float3 body = lerp(shallow, deep, 1-exp(-depth*.22));
                float3 sunTint = lerp(float3(1,1,1),float3(1,.68,.36),_SunsetOn);
                // Beer-Lambert transmission along the ray, with Fresnel reflection at the surface.
                // The opaque scene is already behind this transparent pass: alpha blending preserves
                // submerged floats and the bank without a costly mobile GrabPass. Calm changes the
                // normals/roughness, never the refractive index (the old sunset override forced 91%).
                if (_WaterSceneDepthOff > .5) path = 18;
                float uncertain = saturate(surfaceZ * surfaceZ * _WaterDepthErrK - .25);
                path = lerp(path, 18, uncertain);
                if (scenery > .5 && i.depth.y > .5) path = min(path, max(0,i.depth.x) / max(.1,v.y));
                float transmission = exp(-min(path, 80) * .28);
                float alpha = 1 - (1-fresnel) * transmission;
                float3 col = (body * (1-fresnel) * (1-transmission) + refl * fresnel)
                    / max(.001, alpha);
                col += spec * sunTint * (1 - foam);
                // Small reflective crests, no aerated planing foam.
                col += lerp(float3(.38,.53,.66),refl,.65) * taxiGlint;
                col = lerp(col, float3(0.92, 0.95, 0.97), foam);
                alpha = lerp(alpha, 1, foam);
                // Sub-metre edge blends the existing mesh into the damp bank rather than a blue cutout.
                if (scenery > .5 && i.depth.y > .5) alpha *= smoothstep(0,.18,depth);
                if (_PilotWaterMode > .5)
                {
                    // Another surface seen through the pilot's lake (the sea below a hill lake, the far shore's water):
                    // this pass draws after PilotUnderwater, so it takes the same murk itself. A surface the eye looks
                    // down on through air has no submerged path and is untouched.
                    float3 seen = PilotPath(_WorldSpaceCameraPos, i.wp - _WorldSpaceCameraPos);
                    col = lerp(col, _PilotWaterColor.rgb * seen.z, seen.y);
                    alpha = lerp(alpha, 1, seen.y);
                }
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}

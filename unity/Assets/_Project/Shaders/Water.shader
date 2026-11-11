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
            // The wake surface behind each float's step (WakeWaves.Sources <- FlyingGame.Core.PlaningWake, which is
            // Savitsky and Morabito 2010 read as a free-surface contour): x the cavity's depth at the step, y how
            // far astern it closes, z where the rooster crest stands, w how high the crest is above still water.
            float4 _FoamWake[2];
            // Wind-driven sea state (SeaSurface.cs <- FlyingGame.Core.SeaState). See the long note in frag().
            float4 _SeaWind, _SeaGrowth, _SeaFetch;
            // The aeroplane's shadow on the water (GroundShadow.CastOntoWater): a silhouette mask rendered from the
            // sun's direction, the world->mask matrix, and (on, how much of the daylight is the direct beam, -, -).
            sampler2D _WaterShadowTex;
            float4x4 _WaterShadowMatrix;
            float4 _WaterShadow;

            /// Schlick's approximation for an air/water interface: F0 = 0.0204 from n = 1.333.
            float SchlickWater(float cosTheta)
            {
                float f = 1 - cosTheta, f2 = f * f;
                return .0204 + .9796 * f2 * f2 * f;
            }

            /// How much of a wave of this length the screen can draw here, 0..1: Nyquist says two samples per
            /// wavelength, but a MOVING surface needs several times that before it stops reading as a moire
            /// pattern — two gave a plaid across the whole middle distance. Below about five pixels per wave the
            /// band is handed to the statistics (unresolvedMss) instead of being drawn, where it widens the
            /// Fresnel average and the sun's glitter path: the slope is still there, it has just stopped
            /// pretending to be a picture of a wave.
            ///
            /// Raising this to 12..30 does clean up the last of the far-field hatch, and it also flattens the
            /// middle distance into a sheet. 5..14 is the setting Ty approved on 2026-11-11, so it is the setting
            /// that ships; the residual hatch beyond about fifteen pixels a wave is a known and recorded one.
            float Resolved(float wavelengthM, float footprintM)
            {
                return smoothstep(5, 14, wavelengthM / max(footprintM, 1e-4));
            }

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
                float2 p = i.wp.xz;
                float scenery = _SceneryWater * (1 - _SunsetOn);

                // ---- THE SEA STATE: what the wind has done to this patch of water ------------------------------
                // Ty, 2026-11-11: "water texture should change with wind. No wind glassy wind [makes] texture first
                // then waves stack up with distance etc use real physics". The physics is FlyingGame.Core.SeaState
                // (JONSWAP fetch-limited growth, the Pierson-Moskowitz fully developed cap, Cox and Munk's measured
                // mean square slope, and the capillary floor that makes a dead calm a mirror); SeaSurface.cs fills
                // these three vectors once a frame and this shader only evaluates them. It replaces a BINARY
                // _WaterCalm that was set by a lighting preset and had nothing to do with the wind at all.
                //   _SeaWind   = (downwind.x, downwind.z, wind m/s, capillary texture 0..1)
                //   _SeaGrowth = (kH, kL, Hs cap m, lambda cap m):  Hs = kH*sqrt(F), lambda = kL*F^0.66
                //   _SeaFetch  = (where the water starts along the wind m, longest fetch m, Cox-Munk mss, -)
                // All zero -- no driver, or a dead calm -- is a mirror, which is the right thing to default to.
                // A scene with no SeaSurfaceDriver leaves all three of these at zero, which has to come out as a
                // dead calm and not as a divide by zero: hence the unit fallback here and the floors below.
                float2 downwind = dot(_SeaWind.xy, _SeaWind.xy) > 1e-6 ? normalize(_SeaWind.xy) : float2(0, 1);
                // FETCH, and this is the "waves stack up with distance" term: how far the wind has blown over open
                // water to reach THIS fragment, not the lake's average. A straight upwind shoreline standing in for
                // a curved one; see SeaSurface.cs for why that is the right trade here.
                float fetch = clamp(dot(p, downwind) - _SeaFetch.x, 0, _SeaFetch.y);
                float hs = min(_SeaGrowth.z, _SeaGrowth.x * sqrt(fetch));
                float lambda = clamp(_SeaGrowth.y * pow(max(fetch, 1), .66), .08, max(_SeaGrowth.w, .08));
                // The peak slope of a sinusoid of height Hs and wavelength lambda (SeaState.PeakWaveSlope), and the
                // rms slope Cox and Munk's optical measurement leaves over for the short waves riding on it. A
                // sinusoid of peak slope s has slope VARIANCE s^2/2, which is what is taken out of the budget.
                float peakSlope = 3.14159265 * hs / lambda;
                float shortSlope = sqrt(max(0, _SeaFetch.z - .5 * peakSlope * peakSlope));

                // A SUM OF COMPONENTS, NOT TWO SINE TRAINS. Two crossing trains of the same wavelength make a
                // perfectly regular diamond lattice — corrugated iron, which is what the first version of this
                // looked like. Real wind sea has a directional spread of tens of degrees and a spectrum a decade
                // wide, so the five components below are spread in direction and given wavenumber ratios with no
                // common factor (1 : 1.31 for the long pair, 1 : 1.63 : 2.41 for the short) so nothing repeats.
                // Each travels at ITS OWN speed from the dispersion relation, omega^2 = g·k + (sigma/rho)·k^3 —
                // which is why the long waves overtake the short ones, and why the capillary term is kept: it is
                // what makes a 2 cm ripple outrun the gravity wave it would otherwise be.
                float2 across = float2(-downwind.y, downwind.x);
                float2 g1d = normalize(downwind + across * .57), g2d = normalize(downwind - across * .33);
                float2 s1d = normalize(downwind + across * .95), s2d = normalize(downwind - across * .62);
                float2 s3d = normalize(downwind + across * .14);
                // How much of each band the SCREEN can resolve here. A wave shorter than a few pixels cannot be
                // drawn as geometry without aliasing into a shimmer; its slope is carried statistically instead,
                // in unresolvedMss below, which is what widens the Fresnel and the sun's glitter path at range.
                float footprint = max(length(ddx(p)), length(ddy(p)));
                float lambdaS = max(.22, lambda * .14);
                // EVERY COMPONENT IS GATED BY ITS OWN WAVELENGTH, not by its band's longest member. Gating the
                // short group on lambdaS alone left its 2.41x harmonic being drawn at four pixels a wave, and that
                // one component put a regular cross-hatch over the whole foreground.
                float l1 = lambda, l2 = lambda / 1.31;
                float l3 = lambdaS, l4 = lambdaS / 1.63, l5 = lambdaS / 2.41;
                float r1 = Resolved(l1, footprint), r2 = Resolved(l2, footprint);
                float r3 = Resolved(l3, footprint), r4 = Resolved(l4, footprint), r5 = Resolved(l5, footprint);
                #define SEA_OMEGA(k) sqrt(9.80665 * (k) + 7.294e-5 * (k) * (k) * (k))
                #define SEA_K(lam) (6.2831853 / (lam))
                // N components of equal amplitude A carry a slope variance N·A^2/2, so A = sqrt(2V/N): the long
                // pair shares the peak train's variance (peakSlope^2/2) and the short three share the rest.
                float ga = peakSlope * .70711, sa = shortSlope * .81650;
                // GROUPS AND WANDERING PHASE. A real sea has a continuum of directions and random phases; five
                // discrete components have neither, and two crossing trains of similar wavelength draw a perfectly
                // regular diamond lattice — a weave across the whole middle distance, which is what the first two
                // attempts at this looked like. One noise field fixes both: its value modulates the long pair's
                // amplitude (the sets a real sea arrives in, mean 1 so no energy moves) and shifts each
                // component's phase by up to a few radians over a scale of about sixteen wavelengths, so crest
                // lines bend and drift apart instead of ruling a grid. That is what a narrow directional spread
                // does to a real wave train, for the cost of two noise lookups.
                // The noise cell is about four wavelengths across, not sixteen: a wind wave's crest is a few
                // wavelengths long and then it is a different crest. At sixteen the lines stayed ruler-straight
                // for ten metres, which is what the foreground looked like — drafting hatch, not water. At four
                // the same field also patches the amplitude, and a patchy short-wave amplitude at low wind IS the
                // thing everybody has seen on a lake: cat's paws, with glass between them.
                float2 nq = p * (.25 / max(lambda, .3)) + float2(t * .06, -t * .04);
                float nA = noise(nq), nB = noise(nq.yx + 31.7);
                float sets = .35 + 1.3 * nA;
                float j1 = (nA - .5) * 7.0, j2 = (nB - .5) * 7.0;
                // The COHERENT part of the surface: the long waves. A mirror image is DISPLACED by features bigger
                // than itself and BLURRED by features smaller, and this is the first kind — so this is the slope
                // the reflection's UV shift uses. Feeding the short waves into it as well tore the aeroplane's
                // reflection into confetti, which is not what a lake does to one.
                float2 coherentSlope = sets * (
                      r1 * ga * cos(SEA_K(l1) * dot(p, g1d) - SEA_OMEGA(SEA_K(l1)) * t + j1) * g1d
                    + r2 * ga * cos(SEA_K(l2) * dot(p, g2d) - SEA_OMEGA(SEA_K(l2)) * t + j2 + 1.7) * g2d);
                float2 slope = coherentSlope
                    + r3 * sa * cos(SEA_K(l3) * dot(p, s1d) - SEA_OMEGA(SEA_K(l3)) * t + j2) * s1d
                    + r4 * sa * cos(SEA_K(l4) * dot(p, s2d) - SEA_OMEGA(SEA_K(l4)) * t + j1 + 2.3) * s2d
                    + r5 * sa * cos(SEA_K(l5) * dot(p, s3d) - SEA_OMEGA(SEA_K(l5)) * t - j2 + 4.1) * s3d;
                // The slope variance the geometry above did NOT draw. Variance scales as amplitude squared, so a
                // component faded to r carries r^2 of its variance and leaves the rest here, where it widens the
                // Fresnel average and the sun's glitter path instead of shimmering.
                float unresolvedMss = .5 * ga * ga * (2 - r1 * r1 - r2 * r2)
                                    + .5 * sa * sa * (3 - r3 * r3 - r4 * r4 - r5 * r5);
                float3 n = normalize(float3(slope.x, 1.0, slope.y));
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

                            // ---- THE CAVITY AND THE ROOSTER TAIL --------------------------------------------
                            // Ty, 2026-11-11, from his own J-3's cockpit: "The keel pushes down into the water and
                            // creates a 'rooster tail' of spray that is not modeled it should be. At plow the water
                            // is significantly displaced downward and a 'cavity' is created behind the airplane that
                            // is visible". Both are one curve, and it was already in this repository:
                            // FloatHydro.WakeHeightBeams is Savitsky and Morabito (2010), used until now only to
                            // find where the afterbody meets the water. Referred to the undisturbed surface instead
                            // of to the extended keel line (FlyingGame.Core.PlaningWake) it reads:
                            //   z(x) = −depth + (depth + crest)·sin((π/2)·(x/x_crest)^1.5)
                            // which is −depth at the step (the hollow is exactly as deep as the keel is immersed,
                            // for the reason he gives), zero where the cavity closes, and +crest at the rooster.
                            // The sine is held past the crest, as the formula's own cap does.
                            // NOTHING IS DARKENED BY HAND: the hollow reads dark because its slope points the
                            // reflection somewhere else, which is what a hollow in water does.
                            float4 wk = _FoamWake[f];
                            if (wk.x > .001 && aft > 0)
                            {
                                float xr = max(wk.z, .05);
                                float u = saturate(aft / xr);
                                float arg = 1.5707963 * pow(u, 1.5);
                                // Across-track the trough is the float's own beam, with the chines as its edges.
                                // The aerated wake is wider than the float: the chine spray lands outboard of it
                                // and the hollow's shoulders stand outside the keel line, so 1.6 beams across.
                                float lateral = exp(-signedSide * signedSide / max(.02, origin.z * origin.z * 2.6));
                                // The wake decays astern; the solver's own height field carries the far wake.
                                float decay = exp(-max(0, aft - xr) / (3 * xr));
                                // d/dx of the contour above, exactly: the chain rule on that sine.
                                float dz = (wk.x + wk.w) * cos(arg) * 1.5707963 * 1.5 * sqrt(max(aft, 1e-4))
                                    / pow(xr, 1.5) * lateral * decay;
                                // aft runs OPPOSITE to motion.xy, so the world gradient is -motion·dz, and a height
                                // field's normal is (-grad.x, 1, -grad.y).
                                n = normalize(float3(n.x + motion.x * dz, 1, n.z + motion.y * dz));
                                // Aerated white water where the flow climbs out of the hollow and breaks over the
                                // crest. origin.w already carries PlaningWake.WhiteWater01, so this is zero below
                                // Savitsky's planing onset: a displacement taxi gets no rooster at all.
                                foam = max(foam, origin.w * lateral * decay * smoothstep(.30, 1.0, u) * .85);
                            }
                        }
                        n = normalize(lerp(n, float3(0,1,0), foam * .65));
                    }
                }
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float ndv = saturate(dot(n, v));
                // ---- A BOLDER REFLECTION, BY BEING ROUGHER AND NOT BY BEING FUDGED ---------------------------
                // Ty, 2026-11-11: "The reflection needs to be a little bolder". Evaluating Fresnel at the mean
                // normal is only right for a surface that is flat between one pixel and the next, and water never
                // is: what comes back to the eye is the AVERAGE of Fresnel over the surface's slope distribution,
                // and Schlick's (1-cos)^5 is so convex near grazing that the average is much larger than the value
                // at the average angle. That is why a real lake hands you a bold reflection of an aeroplane at an
                // angle where a perfect mirror would give you 2 %, and it is the whole of the change here -- there
                // is no multiplier on the Fresnel term anywhere in this shader.
                // Two-point Gauss-Hermite over a Gaussian slope distribution of variance unresolvedMss: its nodes
                // are the mean angle plus and minus one rms slope, with equal weights. cos(theta +- s) is expanded
                // rather than taken through acos, which costs two multiplies instead of a transcendental.
                float su = sqrt(unresolvedMss);
                float sinv = sqrt(saturate(1 - ndv * ndv));
                float cs = 1 - .5 * su * su;
                float fresnel = .5 * (SchlickWater(saturate(ndv * cs - sinv * su))
                                    + SchlickWater(saturate(ndv * cs + sinv * su)));
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
                // The sky's own blur in the reflection is the SAME unresolved roughness: glassy water gives back
                // sharp-edged cumulus, a rippled surface gives back a smear. It used to be .85*(1-fresnel), which
                // made the reflection sharper exactly where it was strongest -- backwards.
                if (_SunsetOn > .5) refl = SunsetSky(reflect(-v,n));
                else refl = DaySkyRough(reflect(-v,n), saturate(.10 + 5 * su));
                if (_ReflOn > 0.5 && abs(i.wp.y - _ReflPlaneY) < 1.5)
                {
                    // Ripple distortion of the mirror. A facet tilted by a slope s deflects the reflected ray by
                    // 2s, and a ray deflected by an angle phi lands phi*P11/2 of a UV away (P11 = 1/tan(fovY/2)),
                    // so the shift is the slope itself times P11. Capped at a tenth of the screen: past that the
                    // mirror is sampling somewhere unrelated and the aeroplane's reflection comes apart.
                    // (n.xz - slope) is whatever the wake, the river and the taxi rings added to the normal after
                    // the sea state built it: those are metre-scale features and they displace the mirror too.
                    float2 shift = clamp((coherentSlope + n.xz - slope) * unity_CameraProjection._m11, -.06, .06);
                    float4 uv = i.sp; uv.xy += shift * uv.w;
                    float2 mirrorUV = uv.xy / uv.w;
                    // A small fixed five-tap footprint softens the finite mirror target.
                    // No second camera, full-screen colour copy, or temporal image history.
                    // Its radius is the unresolved roughness through the same P11, floored at one texel so a
                    // glassy lake still gets the target's own softening and nothing more.
                    float2 blur = max(_ReflectionTex_TexelSize.xy * .65, su * unity_CameraProjection._m11);
                    fixed4 r = tex2D(_ReflectionTex, mirrorUV) * .4;
                    r += tex2D(_ReflectionTex, mirrorUV + float2(blur.x, 0)) * .15;
                    r += tex2D(_ReflectionTex, mirrorUV - float2(blur.x, 0)) * .15;
                    r += tex2D(_ReflectionTex, mirrorUV + float2(0, blur.y)) * .15;
                    r += tex2D(_ReflectionTex, mirrorUV - float2(0, blur.y)) * .15;
                    refl = lerp(refl, r.rgb, r.a);
                }
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float3 h = normalize(l + v);
                // THE SUN'S GLITTER PATH comes out of the same roughness rather than a fixed exponent. A Gaussian
                // slope distribution of variance m is a Blinn lobe of exponent 2/m - 2, so a mirror gets a hard
                // point of sun and a breeze spreads it into the long glitter path you see down-sun on any lake.
                // Spreading it must not brighten it: the peak of a normalised lobe goes as its exponent, so the
                // whole thing is scaled by exponent/220 and the glassy case keeps exactly the brightness it had.
                float specPower = clamp(2.0 / max(unresolvedMss, 9.1e-3) - 2.0, 6.0, 220.0);
                float spec = pow(saturate(dot(n, h)), specPower) * 1.2 * (specPower / 220.0)
                    * lerp(1.0, .22, _WaterCalm * _SunsetOn);

                // ---- THE AEROPLANE'S SHADOW ON THE WATER ------------------------------------------------------
                // Ty, 2026-11-11: "we should also see a shadow on the water so both a shadow and a reflection. The
                // reflection should show color etc where the shadow is just darkness." Those are two different
                // things happening at two different places, and that is why the shadow has to be applied HERE and
                // not painted over the water as a dark cutout (which is what was rejected before -- it reads as a
                // second aeroplane beside the mirror). The shadow blocks the sun on its way INTO the water, so it
                // takes away the sunlight coming back out of the body and the glint off the surface; the sky
                // reflecting off that same patch is untouched, because the aeroplane is not between the patch and
                // the sky. GroundShadow.CastOntoWater renders the mask, from the sun's own direction, with its
                // penumbra carried by the mask's resolution; this is one matrix multiply and one tap.
                float sunlit = 1;
                if (_WaterShadow.x > .5)
                {
                    float4 sp = mul(_WaterShadowMatrix, float4(i.wp, 1));
                    // GL.GetGPUProjectionMatrix(..., renderIntoTexture: true) has already put the platform's own
                    // render-target Y convention into this matrix, so there is nothing to flip here.
                    float2 suv = sp.xy / sp.w * .5 + .5;
                    if (sp.w > 0 && all(abs(suv - .5) < .5))
                        sunlit = 1 - tex2D(_WaterShadowTex, suv).r * _WaterShadow.y;
                }
                spec *= sunlit;
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
                // The shadow, on the submerged term only. What is left in it is the skylight the aeroplane does
                // not block (_WaterShadow.y is the direct beam's share), which is why a real shadow on a lake is
                // dark and a cold blue rather than black.
                body *= sunlit;
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
                // THE n-SQUARED LAW OF RADIANCE, and the second half of "the reflection needs to be a little
                // bolder". What leaves the water from below is not the upwelling radiance just under the surface:
                // radiance scales as 1/n^2 across a refracting interface, so the water-leaving radiance is
                // t/n^2 = 0.98/1.333^2 = 0.55 of it (Mobley, Light and Water, the n^2 law; t/n^2 ~ 0.54 is the
                // standard ocean-colour factor). This shader used (1 - fresnel) in its place, which is the
                // EXTERNAL Fresnel at the view angle -- 0.98 looking down, 0.88 at a grazing angle -- so the
                // submerged term was being drawn at nearly twice the radiance it can physically leave with, and
                // that is what a 12 % reflection was being asked to show up against. Nothing on the reflection
                // side is touched: the mirror keeps exactly the Fresnel it earns.
                float3 col = (body * .55 * (1-transmission) + refl * fresnel)
                    / max(.001, alpha);
                col += spec * sunTint * (1 - foam);
                // Small reflective crests, no aerated planing foam.
                col += lerp(float3(.38,.53,.66),refl,.65) * taxiGlint;
                col = lerp(col, float3(0.92, 0.95, 0.97) * sunlit, foam);   // foam is lit by the sun like anything else
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

// The island's ground (2026-11-02, lane/claude-island-terrain). Every material decision is per pixel, from
// the world position, the interpolated ANALYTIC normal and the generated surface channels on the vertices:
//   colour = (glacier ice, debris/moraine, wetness, forest cover)
//   uv0    = (local ELA m, red-rock country, gravel surface, managed turf)
// Nothing is a per-cell colour, so there are no facets or colour steps at the 25 m lattice.
// Climate (docs/pending/ISLAND-TERRAIN-CLAUDE.md): the ELA is the climatic one generated from the orographic
// precipitation; the snow shown is EARLY SUMMER cover, 220 m below the local ELA, lower on shaded faces
// (north = +Unity Z), shed by slopes steeper than ~45 deg, lingering as patches in wet hollows.
Shader "FlyingGame/IslandTerrain"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UrbanGlow.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float4 uv0 : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float4 c : COLOR; float4 a : TEXCOORD2; };
            fixed4 _Color; float4 _BubbleSunDir, _DayAtmosphere, _ReflectionClip, _ReflectionEye;
            float _SunsetOn, _ReflectionRendering;

            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; o.c = v.color; o.a = v.uv0; return o;
            }
            // Simplex gradient noise (no lattice-aligned cell borders in its derivative), polynomial hash
            // (no large-argument sine on mobile Metal). Same family as the legacy terrain's mountainNoise.
            float2 grad2(float2 cell)
            {
                float3 h = frac(float3(cell.x, cell.y, cell.x) * .1031); h += dot(h, h.yzx + 33.33);
                float2 g = frac((h.xx + h.yz) * h.zy) * 2 - 1; return g * rsqrt(max(.01, dot(g, g)));
            }
            float simplex(float2 p)
            {
                float2 cell = floor(p + (p.x + p.y) * .366025404);
                float2 a = p - cell + (cell.x + cell.y) * .211324865;
                float2 corner = a.x > a.y ? float2(1, 0) : float2(0, 1);
                float2 b = a - corner + .211324865, c = a - 1 + .422649730;
                float3 w = max(.5 - float3(dot(a, a), dot(b, b), dot(c, c)), 0); w *= w; w *= w;
                float3 v = float3(dot(grad2(cell), a), dot(grad2(cell + corner), b), dot(grad2(cell + 1), c));
                return saturate(.5 + 35 * dot(w, v));
            }
            // Fade a noise's contrast to its mean before its features fall under ~4 pixels (no shimmer).
            float filtered(float2 p) { float f = max(length(ddx(p)), length(ddy(p))); return lerp(.5, simplex(p), 1 - smoothstep(.12, .25, f)); }
            // Same, with the footprint (lattice cells per pixel) supplied: safe inside a branch, where screen-space
            // derivatives are undefined on Metal.
            float filteredAt(float2 p, float footprint) { return lerp(.5, simplex(p), 1 - smoothstep(.12, .25, footprint)); }

            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                if (_ReflectionRendering > .5)
                {
                    clip(dot(float4(i.wp, 1), _ReflectionClip));
                    float3 fn = cross(ddx(i.wp), ddy(i.wp)); fn *= dot(fn, i.n) < 0 ? -1 : 1;
                    clip(dot(fn, _ReflectionEye.xyz - i.wp));
                }
                else clip(facing);
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > .001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(.4, .8, -.4));
                float3 n = normalize(i.n);
                float h = i.wp.y, ice = i.c.r, debris = i.c.g, wet = i.c.b, forest = i.c.a;
                // a.y: + red-rock country (Grand Canyon area), - the leeward tropical coast (Dillingham area).
                float ela = i.a.x, redrock = saturate(i.a.y), tropical = saturate(-i.a.y), gravel = i.a.z, turf = i.a.w;
                float2 xz = i.wp.xz;
                float pixelM = length(fwidth(i.wp));

                // ---- variation at three scales -------------------------------------------------------
                float broad = simplex(xz * (1.0 / 1100));            // hillside-scale tone
                float mid = filtered(xz * (1.0 / 170));              // stands, ledges, patches
                float fine = filtered(xz * (1.0 / 26) + broad * 3);  // grain

                // ---- slope, aspect, dryness ------------------------------------------------------------
                float ny = n.y, horiz = max(1e-4, length(n.xz));
                float tanSlope = horiz / max(ny, .05);
                float north = n.z / horiz * saturate(tanSlope / .35);  // +1 a steep shaded face, -1 a sunny one
                float dry = saturate((ela - 2250) / 500);              // rain shadow: high ELA = little precipitation
                float treeLine = 1460 - 130 * north + (broad - .5) * 140;

                // ---- vegetation and soil -----------------------------------------------------------------
                // Colours are median surface reflectances of each land-cover class in the six real places
                // (Sentinel-2 L2A, sRGB-encoded; tools/island/topo/shader_palette.py, palette.json).
                float3 lush = lerp(float3(.20, .26, .15), float3(.31, .30, .26), mid);       // alder/willow scrub .. meadow (Knik, Sitka, Johnson Creek)
                float3 dryGrass = lerp(float3(.38, .32, .27), float3(.44, .38, .31), mid);   // dry grass/shrub (Grand Canyon rim, Dillingham)
                lush = lerp(lush, lerp(float3(.29, .26, .20), float3(.34, .30, .24), mid), tropical); // Mokuleia pasture
                float3 meadow = lerp(lush, dryGrass, saturate(dry * 1.2 + (1 - wet) * .15));
                float alpine = smoothstep(treeLine, treeLine + 260, h);  // heath and fellfield above the trees
                float3 heath = lerp(float3(.27, .28, .25), float3(.33, .33, .29), fine);     // alpine meadow/heath (Fairweather)
                float3 albedo = lerp(meadow, heath, alpine);
                // Riparian strips: wetter, greener valley floors.
                albedo = lerp(albedo, float3(.22, .34, .16), smoothstep(.55, .9, wet) * (1 - alpine) * .5 * (1 - dry));
                // Conifer canopy where the forest function puts trees (the same cover the tree streamer uses),
                // so at altitude, where no trees are drawn, the forest still reads as forest.
                float canopy = smoothstep(.12, .75, forest);
                // Wet coastal spruce-hemlock (Sitka, Fairweather) to dry interior fir/pine (Johnson Creek);
                // the Mokuleia slopes carry broadleaf (kiawe, koa, guava) instead.
                float3 conifer = lerp(float3(.12, .17, .11), float3(.21, .23, .18), saturate((ela - 1300) / 900));
                conifer = lerp(conifer, float3(.18, .25, .15), tropical);
                conifer *= (.80 + mid * .35 + (broad - .5) * .2);
                albedo = lerp(albedo, conifer, canopy * .92);

                // ---- rock: steep ground everywhere, and more of it the higher above the treeline ---------
                // Bare rock where it is too steep to hold soil: ~50 deg under forest/meadow, ~40 deg in the alpine.
                float cliff = 1 - smoothstep(lerp(.55, .70, alpine), lerp(.70, .82, alpine), ny);
                float exposed = smoothstep(treeLine + 250, treeLine + 800, h) * smoothstep(.40, .70, mid + broad * .3);
                float rock = saturate(max(cliff, exposed * (1 - smoothstep(.93, .99, ny) * .6)));
                rock *= 1 - turf;
                float3 granite = lerp(float3(.30, .29, .28), float3(.42, .41, .39), broad) * (.88 + fine * .24); // Fairweather/Knik bare rock p10..p90
                float3 stone = granite;
                if (redrock > .001)   // red-rock country only (coherent across whole regions: cheap to branch)
                {
                    float bed = h * .011 + (broad - .5) * .6 + xz.x * .00009;
                    // Grand Canyon: red_rock median (.48,.41,.35) with its R/G ratio kept, darkened for the
                    // Redwall/Supai bands, and the pale Coconino/Kaibab caprock (bare_rock p90).
                    float3 strata = lerp(float3(.40, .31, .25), float3(.50, .42, .35), smoothstep(.3, .7, simplex(float2(bed, 7.3))));
                    strata = lerp(strata, float3(.55, .51, .44), smoothstep(.6, .8, simplex(float2(bed * 3.1, 2.1))) * .6);
                    stone = lerp(granite, strata * (.92 + fine * .16), redrock);
                }
                // Near-vertical faces: projected onto the wall itself, vertical joints and sub-horizontal
                // bedding, so a cliff reads as fractured rock rather than a smooth grey plane.
                float wall = 1 - smoothstep(.25, .5, ny);
                if (wall > .001)
                {
                    float2 face = float2(dot(xz, normalize(float2(-n.z, n.x) + 1e-4)), i.wp.y);
                    float joints = filteredAt(face * float2(1.0 / 9, 1.0 / 40), pixelM / 9);
                    float beds = filteredAt(face * float2(1.0 / 60, 1.0 / 7) + 11.3, pixelM / 7);
                    stone *= lerp(1, (.78 + .3 * joints) * (.9 + .16 * beds), wall);
                }
                // Talus / scree: debris (moraines) and the moderate slopes below cliffs above the treeline.
                float scree = saturate(debris + smoothstep(.70, .82, ny) * (1 - smoothstep(.86, .95, ny)) * alpine * .7);
                float3 screeColour = lerp(float3(.38, .37, .35), float3(.50, .43, .36), redrock) * (.9 + fine * .2);
                albedo = lerp(albedo, screeColour, scree * (1 - canopy) * .85);
                albedo = lerp(albedo, stone, rock * (1 - canopy * .8));
                // Desert country: sparse grass on bare sandstone soil.
                albedo = lerp(albedo, lerp(float3(.44, .36, .29), float3(.49, .43, .36), mid), redrock * (1 - rock) * (1 - canopy) * .75);

                // ---- made and washed surfaces -------------------------------------------------------------
                float beach = (1 - smoothstep(2.5, 6, h)) * smoothstep(.85, .97, ny) * (1 - canopy);
                float3 sand = lerp(float3(.55, .51, .45), float3(.64, .60, .53), smoothstep(.6, 2.2, h)) * (.94 + fine * .12); // Mokuleia beach
                albedo = lerp(albedo, sand, beach);
                albedo = lerp(albedo, float3(.40, .39, .37) * (.9 + fine * .2), gravel * .9);   // Knik braid bars
                albedo = lerp(albedo, lerp(float3(.36, .47, .22), float3(.50, .50, .30), dry), turf * smoothstep(.95, .99, ny));

                // ---- snow: early-summer cover by elevation and aspect, shed by steep ground ---------------
                float snowLine = ela - 160 * north - 220 + (broad - .5) * 170 + (mid - .5) * 70;
                float retention = smoothstep(.58, .74, ny + (fine - .5) * .08);  // nothing stays on ~>50 deg
                float cover = smoothstep(snowLine - 40, snowLine + 90, h) * retention;
                // Patches below the line: shaded, wet hollows hold drifts longest.
                float hollow = saturate(wet * 1.6 + max(0, north) * .6 - .35);
                float patch = smoothstep(snowLine - 420, snowLine - 60, h) * smoothstep(.62, .78, mid * .7 + hollow * .6) * smoothstep(.45, .7, ny);
                float snow = saturate(max(cover, patch)) * (1 - turf) * (1 - beach);
                float3 snowColour = float3(.90, .92, .92) * (.97 + fine * .04);
                albedo = lerp(albedo, snowColour, snow);

                // ---- glacier ice --------------------------------------------------------------------------
                float steepIce = 1 - smoothstep(.85, .95, ny);
                float crevasse = 0;
                if (ice > .15)        // glaciers only
                    crevasse = (1 - smoothstep(.04, .12, abs(filteredAt(float2(dot(xz, n.xz) * .045, dot(xz, float2(-n.z, n.x)) * .006), pixelM * .045) - .5)))
                               * steepIce * smoothstep(.5, .8, ice) * (1 - smoothstep(1.5, 5, pixelM));
                float3 iceColour = lerp(float3(.62, .66, .68), float3(.70, .75, .78), fine);   // bare summer ice (Fairweather, Knik)
                iceColour = lerp(iceColour, float3(.32, .45, .55), crevasse * .45);
                iceColour = lerp(iceColour, float3(.36, .35, .33) * (.85 + fine * .3), saturate(debris * 1.3));
                albedo = lerp(albedo, iceColour, smoothstep(.15, .6, ice));
                float whiteness = max(snow, smoothstep(.15, .6, ice) * (1 - debris));

                // ---- optical detail: shading-only bumps, fading out with distance --------------------------
                float detail = (1 - smoothstep(6, 40, pixelM));
                float relief = detail * ((fine - .5) * (.35 + rock * 2.4) * (1 - whiteness * .7) + (mid - .5) * 3 * rock);
                float3 dpdx = ddx(i.wp), dpdy = ddy(i.wp);
                float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx); float det = dot(dpdx, r1);
                float3 gradient = (ddx(relief) * r1 + ddy(relief) * r2) * sign(det) / max(abs(det), 1e-5);
                gradient *= min(1, .35 / max(.001, length(gradient)));
                n = normalize(n - gradient);

                // ---- light ---------------------------------------------------------------------------------
                float d = saturate(dot(n, l));
                float3 sky = lerp(float3(.30, .36, .43), float3(.50, .57, .64), n.y * .5 + .5);
                float3 dayLight = sky + float3(.54, .50, .42) * d;
                dayLight = lerp(dayLight, float3(.66, .70, .76) + float3(.30, .28, .24) * d, whiteness); // bright, blue-shadowed snow
                float3 col = albedo * lerp(dayLight, float3(.19, .22, .30) + float3(.81, .53, .29) * d, _SunsetOn);
                if (_SunsetOn < .5)
                {
                    // Aerial perspective: optical depth of an exponential atmosphere along the view segment,
                    // the same model as the legacy terrain (densities from SunsetLighting.ConfigureDayAtmosphere).
                    float dist = length(i.wp - _WorldSpaceCameraPos);
                    float path = max(0, dist - _DayAtmosphere.y);
                    float lowAlt = max(0, min(i.wp.y, _WorldSpaceCameraPos.y));
                    float span = abs(i.wp.y - _WorldSpaceCameraPos.y) * .0002;
                    float mean = exp(-lowAlt * .0002) * (1 - exp(-max(.001, span))) / max(.001, span);
                    float haze = 1 - exp(-path * _DayAtmosphere.x * mean * 1.3);
                    col = lerp(col, float3(.40, .53, .68), haze);
                }
                return fixed4(urbanHaze(col * _Color.rgb, i.wp), 1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}

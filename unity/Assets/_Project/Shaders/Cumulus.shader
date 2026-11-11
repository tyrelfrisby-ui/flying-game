// Fair-weather cumulus (owner 2026-10-03: "an accurate cloud over each thermal"). A cloud is one mesh of overlapping
// puff spheres (Cumulus.cs). What makes it read as a real thermal cumulus:
//   * FLAT BASE — every puff is cut off at the condensation level (_BaseY, world y): the base of a cumulus is the
//     height where the rising air cools to its dew point, the same for the whole cloud, so it is dead flat.
//   * GREY UNDERSIDE, WHITE TOP — the base is in the cloud's own shadow (blue-grey), brightening to sunlit white
//     over the lower third; sunward faces are brightest (wrapped Lambert, clouds scatter light all round).
//   * SOFT EDGES — the silhouette thins out (fresnel) through an ordered dither, so the cloud draws OPAQUE (depth-
//     correct against the bubbles, aircraft and other clouds, no transparent sorting) yet has a feathered edge.
//   * NO CIRCLES — the puffs are not spheres any more (Ty 2026-11-11, "a bunch of circles glued together"): their
//     surface is displaced by noise and their normals rebuilt, at build time, in Cumulus.cs. This shader's share of
//     that is the vertex colour it bakes: .r = how exposed the vertex is (1 = clear air, 0 = buried in a neighbour),
//     which shades the valleys where puffs meet, and .g = a fine mottle so a sunlit face is not flat white.
Shader "FlyingGame/Cumulus"
{
    Properties
    {
        _BaseY ("Cloud base (world y)", Float) = 1000
        _TopY ("Cloud top (world y)", Float) = 2000
        _SunDir ("Toward the sun (world)", Vector) = (0.3, 0.8, 0.4, 0)
        _Lit ("Sunlit colour", Color) = (1, 1, 1, 1)
        _Shade ("Shaded colour", Color) = (0.68, 0.72, 0.8, 1)
        _Base ("Base colour", Color) = (0.55, 0.58, 0.65, 1)
        _Fade ("Fade (0 hidden .. 1 solid)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Cull Off      // back faces = the inside of a puff seen through its cut-off bottom: drawn as the flat grey base
        ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wn : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float4 sp : TEXCOORD2;
                fixed2 cre : TEXCOORD4;   // .x exposure (crease shading), .y mottle — baked in Cumulus.cs
                UNITY_FOG_COORDS(3)
            };

            float _BaseY, _TopY, _Fade;
            float4 _SunDir;
            fixed4 _Lit, _Shade, _Base;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.sp = ComputeScreenPos(o.pos);
                o.cre = v.color.rg;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // Interleaved-gradient noise (Jimenez 2014): a stable per-pixel threshold 0..1 for the dither.
            float Dither(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                clip(i.wp.y - _BaseY);                       // the flat condensation-level base
                if (facing < 0)
                {
                    // Looking up through the cut: the cloud's flat, shadowed underside.
                    float2 pb = i.sp.xy / max(i.sp.w, 1e-5) * _ScreenParams.xy;
                    clip(_Fade - Dither(pb));
                    // The flat base, shaded by how enclosed it is: the middle of a cumulus base, with cloud all
                    // round it, is markedly darker than its edge, where light gets in from the side.
                    fixed4 b = fixed4(_Base.rgb * lerp(0.74, 0.96, i.cre.x), 1);
                    UNITY_APPLY_FOG(i.fogCoord, b);
                    return b;
                }
                float3 n = normalize(i.wn);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float ndv = saturate(dot(n, v));
                float2 px = i.sp.xy / max(i.sp.w, 1e-5) * _ScreenParams.xy;
                // Feathered silhouette + fade-in/out, both through the dither. The mottle widens and narrows the
                // feathered band, so the rim dissolves in CLUMPS a few metres across rather than as even sand: an
                // evenly dithered rim is still a smooth outline, only a fuzzy one, and the smooth outline is the
                // thing being fixed. It scales the BAND and not the opacity, so a surface facing the eye stays
                // solid — scaling the opacity put the stipple over the whole cloud.
                float edge = smoothstep(0.0, 0.30 + i.cre.y * 0.40, ndv);
                clip(edge * _Fade - Dither(px));

                float h = saturate((i.wp.y - _BaseY) / max(1.0, (_TopY - _BaseY)));
                float sun = saturate(dot(n, normalize(_SunDir.xyz)) * 0.5 + 0.5);   // wrapped Lambert
                float3 col = lerp(_Shade.rgb, _Lit.rgb, sun);
                col = lerp(_Base.rgb, col, smoothstep(0.0, 0.35, h));               // shadowed flat underside
                // The valleys between puffs. The light reaching the bottom of one has been through cloud twice, so
                // it is grey while the shoulders either side of it are white — that contrast is what reads as a
                // cauliflower crown rather than as a heap of balls, and it is the same cue the eye uses on a real
                // one. The fine mottle on top of it keeps a sunlit face from being a flat sheet of white.
                col *= lerp(0.58, 1.0, i.cre.x) * (0.96 + i.cre.y * 0.08);
                col += (1.0 - ndv) * 0.08;                                          // bright silver lining at the rim
                fixed4 c = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}

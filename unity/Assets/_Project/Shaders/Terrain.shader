// Terrain: vertex colour (grass/rock/dry by height+slope, computed on the CPU) × directional shading,
// plus farm-field grid lines on flat ground (vertex alpha = flatness) so the valley floor keeps the
// original 200 m field pattern. Two-sided not needed (closed surface).
Shader "FlyingGame/Terrain"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float4 c : COLOR; };
            fixed4 _Color; float4 _BubbleSunDir;
            float _SunsetOn, _ReflectionRendering;
            float4 _ReflectionClip;
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; o.c = v.color; return o;
            }
            float hashTerrain(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noiseTerrain(float2 p)
            {
                float2 q=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hashTerrain(q),hashTerrain(q+float2(1,0)),f.x),
                            lerp(hashTerrain(q+float2(0,1)),hashTerrain(q+1),f.x),f.y);
            }
            fixed4 frag (v2f i) : SV_Target
            {
                // Explicit world-space half-space rejects submerged terrain even at grazing angles.
                if (_ReflectionRendering > .5)
                {
                    clip(dot(float4(i.wp, 1), _ReflectionClip));
                    // Rasterizer winding handles visibility. Smooth vertex normals do
                    // not describe a cliff triangle's facing and must not clip its pixels.
                }
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float3 n=normalize(i.n);
                float3 albedo=i.c.rgb;
                float forest=saturate(-i.c.a);
                // All new detail is day-only: the sunset comparison keeps its existing response.
                if (_SunsetOn < .5)
                {
                    float broad=noiseTerrain(i.wp.xz*.006);
                    float grain=noiseTerrain(i.wp.xz*.17+i.wp.y*.021);
                    float cliff=1-smoothstep(.42,.86,n.y);
                    float snow=smoothstep(.70,.86,min(albedo.r,min(albedo.g,albedo.b)));
                    // World-space strata with irregular bed thickness; no repeating texture tile.
                    float bed=i.wp.y*.13 + broad*2.8 + i.wp.x*.0018;
                    float band=(sin(bed)+.35*sin(bed*2.37+grain)) / (1+fwidth(bed)*2);
                    float joints=noiseTerrain(float2(i.wp.x+i.wp.z*.73,i.wp.y*.21)*.045);
                    albedo*=1+(broad-.5)*.18+(grain-.5)*.15;
                    albedo*=1+cliff*(1-snow)*(band*.13+(joints-.5)*.28);
                    // Darker crevices through snow, preserving the authored altitude/slope snow mask.
                    albedo*=1-snow*cliff*smoothstep(.58,.80,joints)*.16;
                    // Canopy mottling survives beyond the streamed tree radius; tied to actual cover.
                    float crowns=lerp(.5,noiseTerrain(i.wp.xz*.095),saturate(1-length(fwidth(i.wp.xz))*.025));
                    albedo*=1+forest*((crowns-.5)*.60+(broad-.5)*.24);
                }
                float d = saturate(dot(n, l));
                float3 dayFill=lerp(float3(.32,.38,.44),float3(.51,.57,.62),n.y*.5+.5);
                float3 col = albedo * lerp(dayFill + float3(.52,.48,.40)*d,
                    float3(.19,.22,.30) + float3(.81,.53,.29)*d, _SunsetOn);
                // Thin dark boundaries, not bright graph paper, and none beneath forest canopy.
                float2 g = abs(frac(i.wp.xz / 200.0) - 0.5);
                float fieldInterior = 1.0 - smoothstep(0.488, 0.5, max(g.x, g.y));
                if (_SunsetOn > .5)
                {
                    float oldLine=1.0-smoothstep(.47,.5,max(g.x,g.y));
                    col*=1-.35*oldLine*saturate(i.c.a);
                }
                else
                {
                    col*=1-.12*(1-fieldInterior)*saturate(i.c.a);
                    // Height-dependent optical depth: distant ridges lose contrast and become blue.
                    float distanceM=length(i.wp-_WorldSpaceCameraPos);
                    float haze=1-exp(-distanceM*.000055*exp(-max(0,i.wp.y)*.00014));
                    col=lerp(col,float3(.67,.77,.86),haze);
                }
                return fixed4(col * _Color.rgb, 1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}

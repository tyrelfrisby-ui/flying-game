// Built-in terrain: CPU biome/cover, snow provenance and curvature; day-only procedural
// rock/drainage surface-gradient normals and slope/aspect snow. No geometric displacement.
// Vertex alpha retains forest coverage / flat farm masks; sunset and mirrored facing retained.
Shader "FlyingGame/Terrain"
{
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            // Reflection view has negative determinant; determine physical facing
            // from the triangle below, independently of backend winding conversion.
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 geology : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float4 c : COLOR; float2 geology : TEXCOORD2; };
            fixed4 _Color; float4 _BubbleSunDir;
            float _SunsetOn, _ReflectionRendering;
            float4 _ReflectionClip;
            float4 _ReflectionEye;
            float4 _DayAtmosphere;
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; o.c = v.color; o.geology = v.geology; return o;
            }
            float hashTerrain(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noiseTerrain(float2 p)
            {
                float2 q=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hashTerrain(q),hashTerrain(q+float2(1,0)),f.x),
                            lerp(hashTerrain(q+float2(0,1)),hashTerrain(q+1),f.x),f.y);
            }
            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                // Explicit world-space half-space rejects submerged terrain even at grazing angles.
                if (_ReflectionRendering > .5)
                {
                    clip(dot(float4(i.wp, 1), _ReflectionClip));
                    // Use a true face normal, not the smoothed cliff vertex normal.
                    // Orient the derivative cross product with the mesh's outward normal;
                    // screen Y / Metal render-target flips cannot change this test.
                    float3 faceNormal = cross(ddx(i.wp), ddy(i.wp));
                    faceNormal *= dot(faceNormal, i.n) < 0 ? -1 : 1;
                    clip(dot(faceNormal, _ReflectionEye.xyz - i.wp));
                }
                else clip(facing);
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float3 n=normalize(i.n);
                float3 albedo=i.c.rgb;
                float forest=saturate(-i.c.a);
                // All new detail is day-only: the sunset comparison keeps its existing response.
                if (_SunsetOn < .5)
                {
                    float3 macroNormal=n;
                    float2 ground=float2(i.wp.x*.81+i.wp.z*.59,i.wp.z*.81-i.wp.x*.59);
                    float broad=noiseTerrain(ground*.0047);
                    float cliff=1-smoothstep(.42,.86,n.y);
                    float redrock=smoothstep(.08,.22,albedo.r-albedo.g);
                    float hollow=saturate(i.geology.y*2.5);
                    float ridge=saturate(-i.geology.y*2);
                    // World-anchored triplanar joints. Vertical projections preserve scale on cliffs;
                    // unlike an xz-only texture they do not stretch across near-vertical faces.
                    float3 weights=pow(abs(n),float3(4,4,4));
                    weights/=max(.001,weights.x+weights.y+weights.z);
                    float rockGrain=noiseTerrain(i.wp.xz*.075)*weights.y;
                    rockGrain+=noiseTerrain(i.wp.zy*float2(.065,.095))*weights.x;
                    rockGrain+=noiseTerrain(i.wp.xy*float2(.065,.095))*weights.z;
                    // Side projections follow gravity on cliff faces. Broad troughs rather than
                    // contours of isotropic noise avoid the closed ink-loop artefact of M2.
                    float warp=noiseTerrain(ground*.006);
                    float2 sideWeight=abs(macroNormal.xz);
                    sideWeight/=max(.001,sideWeight.x+sideWeight.y);
                    float drainageX=noiseTerrain(float2(i.wp.z*.022+warp*.65,i.wp.y*.0023));
                    float drainageZ=noiseTerrain(float2(i.wp.x*.022-warp*.65,i.wp.y*.0023+17));
                    float drainage=drainageX*sideWeight.x+drainageZ*sideWeight.y;
                    float channel=1-smoothstep(.24,.53,drainage);
                    float fine=rockGrain;
                    if (_DayAtmosphere.z > .5)
                        fine=noiseTerrain(ground*.061+float2(warp*.6,i.wp.y*.003));
                    float tributary=1-smoothstep(.28,.57,fine);
                    float gully=saturate(channel*.7+tributary*channel*.3+hollow*.25);
                    // Recover the substrate hidden by the old vertex snow, then deposit snow by
                    // altitude, aspect and concavity. Ice metadata bypasses this reconstruction.
                    float oldSnow=saturate(i.geology.x);
                    float3 substrate=saturate((albedo-float3(.9,.9,.92)*oldSnow)/max(.20,1-oldSnow));
                    substrate=lerp(substrate,float3(.47,.42,.37),smoothstep(.45,.85,oldSnow));
                    if(i.geology.x >= 0) albedo=substrate;
                    float aspect=macroNormal.z*.5+.5; // Unity north: sheltered north-facing slopes.
                    float snowAltitude=smoothstep(2440-aspect*180,3050-aspect*180,i.wp.y+(broad-.5)*160);
                    // Very compressed peaks need discontinuous snow tongues on their flanks;
                    // shallow caps also retain exposed wind-scoured patches instead of white rings.
                    float retention=smoothstep(.28,.74,macroNormal.y);
                    float pocket=gully*smoothstep(.19,.43,macroNormal.y)*.62;
                    float accumulation=snowAltitude*(retention*.78+pocket)*(1-ridge*.55);
                    float breakup=(broad-.5)*.60+(rockGrain-.5)*.24+(fine-.5)*.24;
                    float snow=smoothstep(.35,.75,accumulation+breakup)*(1-redrock);
                    snow*=smoothstep(.16,.29,macroNormal.y); // no snow on near-vertical rock
                    float talus=hollow*smoothstep(.60,.82,macroNormal.y)
                        *(1-smoothstep(.88,.98,macroNormal.y))*(1-forest);
                    albedo=lerp(albedo,lerp(float3(.44,.43,.40),float3(.57,.43,.32),redrock),talus*.65);
                    float bed=i.wp.y*.048+broad*1.4+ground.x*.0007;
                    float band=sin(bed)/(1+fwidth(bed)*2);
                    albedo*=1+(broad-.5)*.18+(rockGrain-.5)*.25;
                    albedo*=1+cliff*band*lerp(.045,.19,redrock);
                    albedo*=1-cliff*(gully*.12+hollow*.14); // crease occlusion, not baked sunlight
                    if(i.geology.x >= 0)
                        albedo=lerp(albedo,float3(.87,.90,.94)*(1+(rockGrain-.5)*.045),snow);
                    // Surface-gradient bump in world space. These are shading heights only:
                    // NO vertex movement, depth/parallax change, or collision displacement.
                    float detailFade=1-smoothstep(8,38,length(fwidth(i.wp)));
                    float relief=detailFade*cliff*(1-snow*.85)*(1-forest)*
                        ((rockGrain-.5)*1.8-gully*2.4);
                    float3 dpdx=ddx(i.wp), dpdy=ddy(i.wp);
                    float3 r1=cross(dpdy,n), r2=cross(n,dpdx);
                    float determinant=dot(dpdx,r1);
                    float3 gradient=(ddx(relief)*r1+ddy(relief)*r2)
                        *sign(determinant)/max(abs(determinant),.00001);
                    // Bound the perturbation; avoid grazing-angle derivative explosions.
                    gradient*=min(1,.65/max(.001,length(gradient)));
                    n=normalize(n-gradient);
                    float crowns=lerp(.5,noiseTerrain(ground*.14),saturate(1-length(fwidth(ground))*.025));
                    float canopy=smoothstep(.15,.8,forest);
                    albedo=lerp(albedo,float3(.115,.205,.13)*(.8+crowns*.65+(broad-.5)*.16),canopy*.85);

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
                    // Optical depth along the viewing segment through an exponential
                    // blue atmosphere. Vertical top-down paths should not fog like
                    // equally long horizontal valley paths.
                    float distanceM=length(i.wp-_WorldSpaceCameraPos);
                    float path=max(0,distanceM-_DayAtmosphere.y);
                    float lowAltitude=max(0,min(i.wp.y,_WorldSpaceCameraPos.y));
                    float altitudeSpan=abs(i.wp.y-_WorldSpaceCameraPos.y);
                    float scaledSpan=altitudeSpan*.00032;
                    float meanDensity=exp(-lowAltitude*.00032)
                        *(1-exp(-max(.001,scaledSpan)))/max(.001,scaledSpan);
                    float optical=path*_DayAtmosphere.x*meanDensity;
                    float haze=1-exp(-optical);
                    col=lerp(col,float3(.32,.49,.68),haze);
                }
                return fixed4(col * _Color.rgb, 1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}

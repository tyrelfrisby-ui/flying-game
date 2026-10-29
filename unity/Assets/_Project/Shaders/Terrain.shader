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
            #include "UrbanGlow.cginc"
            #include "PowellSandstone.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 geology : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float4 c : COLOR; float2 geology : TEXCOORD2; };
            fixed4 _Color; float4 _BubbleSunDir;
            float _SunsetOn, _ReflectionRendering, _AirportGrassPreviewOff;
            float4 _ReflectionClip;
            float4 _ReflectionEye;
            float4 _AirportGrass[4];
            float4 _AirportPasture[4], _AirportPastureHeights;
            float4 _MountainAlpine, _MountainAlaska;
            float4 _GlacierAxes[3], _GlacierWidths;
            float mountainRegion(float2 p, float4 bounds)
            {
                float2 inside=min(p-bounds.xy,bounds.zw-p);
                return smoothstep(0,400,min(inside.x,inside.y))*step(1,bounds.z-bounds.x);
            }

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
            // Value noise has detail within each lattice cell. Remove its contrast
            // before a cell falls below four pixels, for colour AND optical normals.
            float filteredTerrain(float2 p)
            {
                float footprint=max(length(ddx(p)),length(ddy(p)));
                return lerp(.5,noiseTerrain(p),1-smoothstep(.125,.25,footprint));
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
                float alpine=mountainRegion(i.wp.xz,_MountainAlpine);
                float alaska=mountainRegion(i.wp.xz,_MountainAlaska);
                float mountain=max(alpine,alaska);
                alpine*=1-alaska; // Granite takes priority in the overlapping footprint skirts.

                // Local maintained turf takes precedence over shoreline sandstone.
                // Height/slope gates leave the gorge walls and lower banks untouched.
                float pasture=0;
                for(int field=0;field<4;field++)
                {
                    float4 bounds=_AirportPasture[field];
                    float2 outside=abs(i.wp.xz-bounds.xy)-bounds.zw;
                    float edge=max(outside.x,outside.y);
                    float level=1-smoothstep(5,15,abs(i.wp.y-_AirportPastureHeights[field]));
                    float flat=smoothstep(.90,.98,n.y);
                    pasture=max(pasture,(1-smoothstep(0,60,edge))*level*flat*step(1,bounds.z));
                }
                float powellMask=saturate(-i.geology.x-2)*(1-pasture);
                // All new detail is day-only: the sunset comparison keeps its existing response.
                if (_SunsetOn < .5 && powellMask < .999 && pasture < .999)
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
                    float rockGrain=filteredTerrain(i.wp.xz*.075)*weights.y;
                    rockGrain+=filteredTerrain(i.wp.zy*float2(.065,.095))*weights.x;
                    rockGrain+=filteredTerrain(i.wp.xy*float2(.065,.095))*weights.z;
                    // Fixed world projections: rotating a world-position dot product by
                    // the varying slope normal made coordinates race across every face.
                    // Blend stationary side fields instead. 42 m cross-slope cells and
                    // 375 m vertical cells stretch the previous ribs a further 3:1.
                    float sideWeight=weights.x/max(.001,weights.x+weights.z);
                    float drainage=lerp(filteredTerrain(i.wp.xy*float2(.024,.0027)+13.7),
                        filteredTerrain(i.wp.zy*float2(.024,.0027)+31.2),sideWeight);
                    float fine=rockGrain;
                    if (_DayAtmosphere.z > .5)
                        fine=lerp(filteredTerrain(i.wp.xy*float2(.045,.005)+7.1),
                            filteredTerrain(i.wp.zy*float2(.045,.005)+23.6),sideWeight);
                    // Broad soft channels, no absolute-value/height contour functions.
                    float channel=1-smoothstep(.22,.72,drainage);
                    float tributary=1-smoothstep(.22,.72,fine);
                    float gully=saturate(channel*.84+tributary*channel*.16+hollow*.16);
                    // Recover the substrate hidden by the old vertex snow, then deposit snow by
                    // altitude, aspect and concavity. Ice metadata bypasses this reconstruction.
                    float oldSnow=saturate(i.geology.x);
                    float3 substrate=saturate((albedo-float3(.9,.9,.92)*oldSnow)/max(.20,1-oldSnow));
                    substrate=lerp(substrate,float3(.47,.42,.37),smoothstep(.45,.85,oldSnow));
                    if(i.geology.x >= 0) albedo=substrate;
                    // North is +Unity Z: positive normal.z faces the shaded north.
                    float aspect=macroNormal.z*.5+.5;
                    float treeLine=lerp(2180,1720,alaska)+(broad-.5)*170-aspect*100;
                    float bareAltitude=smoothstep(treeLine+80,treeLine+520,i.wp.y);
                    float exposed=(1-smoothstep(.60,.819,macroNormal.y));
                    float mountainRock=mountain*max(exposed,bareAltitude)*(1-redrock);
                    // Cover stays tied to the existing forest registry. Never add trees
                    // to strips, authored clearings, or slopes that have no sim timber.
                    // Registered canopy already has sim altitude/aspect/slope thinning.
                    // Do not erase that thin edge a second time with the rock mask.
                    float3 meadow=lerp(float3(.34,.41,.22),float3(.49,.47,.32),
                        saturate((i.wp.y-treeLine)/500));
                    albedo=lerp(albedo,meadow,mountain*(1-exposed)*(1-forest));
                    float snowLine=lerp(3170,1770,alaska)-aspect*lerp(150,240,alaska);
                    float raggedHeight=i.wp.y+(broad-.5)*260+(rockGrain-.5)*75;
                    float snowAltitude=smoothstep(snowLine-160,snowLine+320,raggedHeight);
                    // 35-40 degree snow retention, with sheltered hollows extending
                    // below the regional line. Near-vertical faces remain bare.
                    // Compressed Alaska relief reaches 70+ degrees: retain bonded ice
                    // to 74 degrees, leaving the sheer couloir walls as granite.
                    float retention=smoothstep(lerp(.75,.13,alaska),lerp(.84,.28,alaska),
                        macroNormal.y+(broad-.5)*.06);
                    float cap=snowAltitude*retention*(1-ridge*lerp(.38,.12,alaska));
                    float sheltered=smoothstep(.48,.78,gully+hollow*.30);
                    float patchAltitude=smoothstep(snowLine-430,snowLine+100,raggedHeight);
                    float streak=sheltered*patchAltitude*smoothstep(.42,.76,macroNormal.y)
                        *(1-retention)*(.35+aspect*.45)*(1-ridge*.65);
                    float alaskaIce=snowAltitude*smoothstep(.08,.23,macroNormal.y)
                        *smoothstep(.30,.65,gully+hollow*.45)*.92;
                    float snow=saturate(max(cap+streak,alaskaIce*alaska))*(1-redrock);
                    // Preserve the established non-range snow response on front walls.
                    float legacyAltitude=smoothstep(2440-aspect*180,3050-aspect*180,i.wp.y+(broad-.5)*160);
                    float legacyRetention=smoothstep(.64,.88,macroNormal.y);
                    float legacyStreak=smoothstep(.72,.92,gully)*legacyAltitude
                        *smoothstep(.18,.35,macroNormal.y)*(1-legacyRetention)*(1-ridge*.6);
                    float legacySnow=saturate(legacyAltitude*legacyRetention*(.94-ridge*.24)+legacyStreak*.32)*(1-redrock);
                    snow=lerp(legacySnow,snow,mountain);
                    float talus=hollow*smoothstep(.60,.82,macroNormal.y)
                        *(1-smoothstep(.88,.98,macroNormal.y))*(1-forest);
                    albedo=lerp(albedo,lerp(float3(.44,.43,.40),float3(.57,.43,.32),redrock),talus*.65);
                    albedo*=1+(drainage-.5)*.072+(rockGrain-.5)*.072;
                    if(redrock>.001)
                    {
                        // Existing desert beds: do not spend these three samples on
                        // grey mountain rock that has a different geological package.
                        float bed=i.wp.y*.0065+(broad-.5)*.32+ground.x*.00012;
                        float package=filteredTerrain(float2(bed,7.3));
                        float thinBeds=filteredTerrain(float2(bed*3.3+19.1,4.8));
                        float lamina=filteredTerrain(float2(bed*5.7+37.2,2.6));
                        float3 strata=lerp(float3(.55,.32,.22),float3(.76,.65,.48),
                            smoothstep(.25,.72,package));
                        strata=lerp(strata,float3(.47,.46,.43),smoothstep(.53,.76,thinBeds)*.65);
                        strata=lerp(strata,float3(.82,.72,.55),smoothstep(.62,.80,lamina)*.52);
                        strata*=1-cliff*(1-smoothstep(.23,.39,thinBeds))*.16;
                        albedo=lerp(albedo,strata*(.94+rockGrain*.12),redrock*.82);
                        float fan=talus*(.65+gully*.20);
                        albedo=lerp(albedo,float3(.73,.63,.49)*(.97+broad*.06),fan*redrock*.82);
                    }
                    albedo*=1-cliff*(1-talus*.8)*(gully*.064+hollow*.032);
                    albedo*=1+cliff*(1-talus*.8)*(1-gully)*.024;
                    if(i.geology.x >= 0)
                        albedo=lerp(albedo,float3(.87,.90,.94)*(1+(rockGrain-.5)*.045),snow*(1-mountain));
                    float beddingRelief=0;
                    if(mountain>.001)
                    {
                        // Limestone packages dip gently across both stationary side
                        // projections; triplanar grain above supplies face-scale rock.
                        // Unequal 18/53/137 m beds avoid a repeated ruler or moire.
                        float bedHeight=i.wp.y+i.wp.x*.31-i.wp.z*.19
                            +(noiseTerrain(i.wp.xz*.0016)-.5)*170+(broad-.5)*48;
                        float bedFace=(1-smoothstep(.766,.866,macroNormal.y))
                            *smoothstep(.20,.55,drainage)*(1-hollow*.65);
                        float bedLarge=filteredTerrain(float2(bedHeight/137,11.7));
                        float bedMid=filteredTerrain(float2(bedHeight/53+23.1,3.4));
                        float bedSmall=.5;
                        if(_DayAtmosphere.z>.5)
                            bedSmall=filteredTerrain(float2(bedHeight/18+41.3,8.2));
                        float3 limestone=lerp(float3(.43,.45,.46),float3(.73,.70,.62),
                            smoothstep(.25,.75,bedLarge*.55+bedMid*.45));
                        limestone*=1+(bedSmall-.5)*.24;
                        float darkBed=(1-smoothstep(.28,.44,bedMid))*.28;
                        limestone*=1-darkBed;
                        limestone=lerp(float3(.59,.59,.55),limestone,bedFace*.60);
                        beddingRelief=(bedMid-.5)*alpine*bedFace*.42;
                        // Alaska is massive granite: no sedimentary bands.
                        float3 granite=lerp(float3(.62,.64,.65),float3(.76,.76,.73),broad);
                        float fracture=1-smoothstep(.08,.16,abs(drainage-.48));
                        granite*=1-fracture*.13*(1-smoothstep(.40,.62,rockGrain));
                        granite*=1+(rockGrain-.5)*.14;
                        float3 mountainStone=lerp(limestone,granite,alaska);
                        mountainStone*=1-gully*.09-hollow*.08;
                        albedo=lerp(albedo,mountainStone,mountainRock);
                        // Concave toes and channel mouths broaden into smooth debris
                        // aprons as the slope shallows. No geometry or depth offset.
                        float toe=smoothstep(.58,.83,macroNormal.y);
                        float debrisHeight=smoothstep(treeLine-100,treeLine+420,i.wp.y);
                        float fanWidth=lerp(.76,.40,toe);
                        float fanChannel=smoothstep(fanWidth,fanWidth+.22,gully);
                        float debris=saturate(hollow*3.4+fanChannel*.85)*toe
                            *debrisHeight*(1-forest)*(1-snow);
                        float3 scree=lerp(float3(.66,.65,.60),float3(.59,.62,.64),alaska);
                        scree*=.97+rockGrain*.06;
                        albedo=lerp(albedo,scree,debris*mountain*.88);
                        if(i.geology.x>=0)
                            albedo=lerp(albedo,float3(.96,.975,.99),snow*mountain);
                        talus=max(talus,debris*mountain);
                    }
                    // Surface-gradient bump in world space. These are shading heights only:
                    // NO vertex movement, depth/parallax change, or collision displacement.
                    float detailFade=1-smoothstep(8,38,length(fwidth(i.wp)));
                    float relief=detailFade*cliff*(1-snow*.85)*(1-forest)*
                        ((rockGrain-.5)*1.2-gully*lerp(4.0,2.5,mountain)+beddingRelief)*(1-talus*.8);
                    float3 dpdx=ddx(i.wp), dpdy=ddy(i.wp);
                    float3 r1=cross(dpdy,n), r2=cross(n,dpdx);
                    float determinant=dot(dpdx,r1);
                    float3 gradient=(ddx(relief)*r1+ddy(relief)*r2)
                        *sign(determinant)/max(abs(determinant),.00001);
                    // Bound the perturbation; avoid grazing-angle derivative explosions.
                    gradient*=min(1,.35/max(.001,length(gradient)));
                    n=normalize(n-gradient);
                    if(alaska>.001)
                    {
                        float glacier=0, iceAcross=0, iceAlong=0, iceWidth=1, iceLength=1;
                        float2 iceDown=float2(0,1);
                        for(int gi=0;gi<3;gi++)
                        {
                            float4 axis=_GlacierAxes[gi];
                            float2 delta=axis.zw-axis.xy;
                            float len=max(1,length(delta));
                            float2 down=delta/len, right=float2(-down.y,down.x);
                            float2 local=i.wp.zx-axis.xy;
                            float along=dot(local,down), across=dot(local,right);
                            float width=_GlacierWidths[gi];
                            float edge=1-smoothstep(width*.88,width,abs(across));
                            float ends=smoothstep(0,65,along)*(1-smoothstep(len-90,len,along));
                            float claim=edge*ends*smoothstep(.65,.90,macroNormal.y);
                            if(claim>glacier)
                            {
                                glacier=claim; iceAcross=across; iceAlong=along;
                                iceWidth=width; iceLength=len; iceDown=down;
                            }
                        }
                        // One centred 800 x 80 m snow runway per corridor, 65% downstream.
                        // 60 m safety feather outside the clear rectangle; no actual flattening.
                        float lz=(1-smoothstep(400,460,abs(iceAlong-iceLength*.65)))
                            *(1-smoothstep(40,100,abs(iceAcross)));
                        float edgeZone=smoothstep(.45,.86,abs(iceAcross)/iceWidth);
                        float icefall=(1-smoothstep(.30,.55,iceAlong/iceLength))
                            +saturate((.98-macroNormal.y)*12);
                        float warp=filteredTerrain(float2(iceAlong*.006,iceAcross*.008));
                        float phase=(iceAlong+abs(iceAcross)*.32+(warp-.5)*38)/31;
                        float footprint=max(abs(ddx(phase)),abs(ddy(phase)));
                        float fissure=1-smoothstep(.035,.105,abs(frac(phase)-.5));
                        fissure*=1-smoothstep(.12,.28,footprint);
                        float cracks=fissure*saturate(edgeZone+icefall*.8)*(1-lz);
                        float drift=(warp-.5)*20;
                        float iceAA=max(.5,max(length(ddx(i.wp.xz)),length(ddy(i.wp.xz))));
                        float medial=(1-smoothstep(max(0,9-iceAA),22+iceAA,abs(iceAcross-iceWidth*.30-drift)))
                            *smoothstep(.20,.45,iceAlong/iceLength);
                        float lateral=smoothstep(.82,.97,abs(iceAcross)/iceWidth);
                        float moraine=max(medial,lateral)*(1-lz);
                        float3 iceColour=lerp(float3(.95,.975,.99),float3(.19,.39,.51),cracks*.85);
                        iceColour=lerp(iceColour,float3(.27,.29,.30)*( .9+rockGrain*.2),moraine*.82);
                        albedo=lerp(albedo,iceColour,glacier);
                        float opticalIce=-cracks*.8;
                        float3 iceGradient=(ddx(opticalIce)*r1+ddy(opticalIce)*r2)
                            *sign(determinant)/max(abs(determinant),.00001);
                        iceGradient*=min(1,.20/max(.001,length(iceGradient)));
                        n=normalize(n-iceGradient*glacier);
                        // Smooth shading at the centre, exact terrain depth/silhouette retained.
                        float grade=dot(macroNormal.xz,float2(iceDown.y,iceDown.x));
                        float3 iceNormal=normalize(float3(iceDown.y*grade,macroNormal.y,iceDown.x*grade));
                        n=normalize(lerp(n,iceNormal,glacier*(1-edgeZone)*(1-cracks)*lerp(.85,1,lz)));
                        forest*=1-glacier;
                    }
                    float crowns=lerp(.5,noiseTerrain(ground*.14),saturate(1-length(fwidth(ground))*.025));
                    float canopy=smoothstep(.15,.8,forest);
                    albedo=lerp(albedo,float3(.115,.205,.13)*(.8+crowns*.65+(broad-.5)*.16),canopy*.85);

                }
                if(powellMask>.001)
                {
                    float3 sandstone;
                    powellSurface(i.wp,n,sandstone);
                    albedo=lerp(albedo,sandstone,powellMask);
                }
                // The authored grass strip is narrower than one coarse terrain cell.
                // Paint its exact footprint on the visible terrain instead of a coplanar overlay.
                // No field-grid mask, depth bias, displacement or world-wide tiling lines.
                float grassAA=max(.12,max(length(ddx(i.wp.xz)),length(ddy(i.wp.xz))));
                if(pasture>.001)
                {
                    // Same green/olive altitude palette as the airfield vertex grass.
                    // Fine, filtered low-contrast variation; no thresholded dirt spots.
                    float3 turf=lerp(float3(.42,.55,.28),float3(.62,.56,.36),saturate(i.wp.y/2800)*.9);
                    float grain=filteredTerrain(i.wp.xz*.32)-.5;
                    float mow=sin(i.wp.z*.785398)*(1-smoothstep(.6,2.5,grassAA));
                    turf*=1+grain*.035+mow*.018;
                    albedo=lerp(albedo,turf,pasture);
                    n=normalize(lerp(n,normalize(i.n),pasture));
                }
                for (int airport=0; airport<4; airport++)
                {
                    float4 bounds=_AirportGrass[airport];
                    float2 local=i.wp.xz-bounds.xy;
                    float edge=max(abs(local.x)-bounds.z,abs(local.y)-bounds.w);
                    float aa=grassAA;
                    // Feather the cut grass into its surrounding pasture, not a dark rectangle.
                    float mask=(1-smoothstep(-1.5-aa,aa,edge))*step(1,bounds.z)*(1-_AirportGrassPreviewOff);
                    if(mask>.001)
                    {
                        float variation=lerp(.5,noiseTerrain(local*.08),1-smoothstep(1.5,3.0,aa));
                        float mow=.5+.5*sin(local.x*1.05)*(1-smoothstep(.6,2.5,aa));
                        float tracks=exp(-pow((abs(local.x)-1.8)/.65,2))
                            *(1-smoothstep(.6,2.5,aa));
                        // Match the local vertex pasture colour (and its altitude palette).
                        // The old fixed olive replacement created the long dark band.
                        float3 turf=i.c.rgb*(.98+.04*mow+.025*(variation-.5));
                        turf=lerp(turf,turf*float3(.95,.86,.73),tracks*(.18+.08*variation));
                        albedo=lerp(albedo,turf,mask);
                    }
                }
                float d = saturate(dot(n, l));
                float3 dayFill=lerp(float3(.32,.38,.44),float3(.51,.57,.62),n.y*.5+.5);
                float3 col = albedo * lerp(dayFill + float3(.52,.48,.40)*d,
                    float3(.19,.22,.30) + float3(.81,.53,.29)*d, _SunsetOn);
                if(powellMask>.001)
                {
                    float3 desertFill=lerp(float3(.28,.34,.43),float3(.43,.48,.54),n.y*.5+.5);
                    float3 desertLight=lerp(desertFill+float3(.68,.57,.43)*d,
                        float3(.32,.30,.34)+float3(.81,.53,.29)*d,_SunsetOn);
                    col=lerp(col,albedo*desertLight,powellMask);
                }
                // Airport pasture has no synthetic section-line grid, including at dusk.
                if (_SunsetOn < .5)
                {
                    // No synthetic 200 m farm grid in the daytime airport infields.
                    // Optical depth along the viewing segment through an exponential
                    // blue atmosphere. Vertical top-down paths should not fog like
                    // equally long horizontal valley paths.
                    float distanceM=length(i.wp-_WorldSpaceCameraPos);
                    float path=max(0,distanceM-_DayAtmosphere.y);
                    float lowAltitude=max(0,min(i.wp.y,_WorldSpaceCameraPos.y));
                    float altitudeSpan=abs(i.wp.y-_WorldSpaceCameraPos.y);
                    float scaledSpan=altitudeSpan*.00020;
                    float meanDensity=exp(-lowAltitude*.00020)
                        *(1-exp(-max(.001,scaledSpan)))/max(.001,scaledSpan);
                    // Terrain-only depth separation: retain the clear near field,
                    // progressively blue/desaturated ranges, no change to the sky.
                    float rangeHaze=1+mountain*.85;
                    float optical=path*_DayAtmosphere.x*meanDensity*lerp(1.65,.8,powellMask)*rangeHaze;
                    float haze=1-exp(-optical);
                    float3 hazeColour=lerp(float3(.32,.49,.68),float3(.43,.57,.70),mountain);
                    col=lerp(col,hazeColour,haze);
                }
                return fixed4(urbanHaze(col * _Color.rgb,i.wp), 1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}

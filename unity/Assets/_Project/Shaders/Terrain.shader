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
            // Triangular gradient noise for mountain surfaces: no flat Cartesian cell
            // borders in its derivative (the old value-noise normals exposed a brick grid).
            // Polynomial hash avoids large-argument sine precision on mobile Metal.
            float2 mountainGradient(float2 cell)
            {
                float3 h=frac(float3(cell.x,cell.y,cell.x)*.1031);
                h+=dot(h,h.yzx+33.33);
                float2 g=frac((h.xx+h.yz)*h.zy)*2-1;
                return g*rsqrt(max(.01,dot(g,g)));
            }
            float mountainNoise(float2 p)
            {
                float2 cell=floor(p+(p.x+p.y)*.366025404);
                float2 a=p-cell+(cell.x+cell.y)*.211324865;
                float2 corner=a.x>a.y ? float2(1,0) : float2(0,1);
                float2 b=a-corner+.211324865;
                float2 c=a-1+.422649730;
                float3 w=max(float3(.5,.5,.5)-float3(dot(a,a),dot(b,b),dot(c,c)),0);
                w*=w; w*=w;
                float3 v=float3(dot(mountainGradient(cell),a),
                    dot(mountainGradient(cell+corner),b),dot(mountainGradient(cell+1),c));
                return saturate(.5+35*dot(w,v));
            }
            float filteredMountain(float2 p)
            {
                float footprint=max(length(ddx(p)),length(ddy(p)));
                return lerp(.5,mountainNoise(p),1-smoothstep(.125,.25,footprint));
            }
            // Stationary vertical-plane structures: x is cross-fall-line, y elevation.
            // Uneven centres persist for kilometres vertically; no thresholded 2D blobs.
            float3 graniteRibs(float2 p)
            {
                float bend=(filteredMountain(float2(p.x*.00065,p.y*.00045))-.5)*22;
                float crossM=p.x+bend;
                float cell=floor(crossM/110);
                float distanceM=1000, widthM=4;
                for(int ri=-1;ri<=1;ri++)
                {
                    float id=cell+ri;
                    float seed=frac((id*.1031+17.17)*(id*.1031+51.73));
                    float centre=(id+.18+.64*seed)*110;
                    float dist=abs(crossM-centre);
                    if(dist<distanceM) { distanceM=dist; widthM=3+seed*6; }
                }
                float aa=max(.35,fwidth(crossM)*.65);
                float couloir=1-smoothstep(max(0,widthM-aa),widthM+aa,distanceM);
                // Optical relief describes the broad rock ribs between the snow chutes.
                float rib=1-exp(-distanceM*.055);
                if(_DayAtmosphere.z<.5) return float3(0,couloir,rib);
                float jointCoordinate=p.x+(filteredMountain(p*.0009+37)-.5)*9;
                float jc=floor(jointCoordinate/23);
                float jointDistance=100;
                for(int ji=-1;ji<=1;ji++)
                {
                    float id=jc+ji;
                    float seed=frac((id*.107+7.13)*(id*.107+31.91));
                    jointDistance=min(jointDistance,abs(jointCoordinate-(id+.15+.7*seed)*23));
                }
                float jaa=max(.20,fwidth(jointCoordinate)*.65);
                float joint=(1-smoothstep(max(0,.45-jaa),.85+jaa,jointDistance))
                    *(1-smoothstep(2,7,jaa));
                return float3(joint,couloir,rib);
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
                    if(mountain>.001) broad=filteredMountain(ground*.0029);
                    float cliff=1-smoothstep(.42,.86,n.y);
                    float redrock=smoothstep(.08,.22,albedo.r-albedo.g);
                    float hollow=saturate(i.geology.y*2.5);
                    float ridge=saturate(-i.geology.y*2);
                    // World-anchored triplanar joints. Vertical projections preserve scale on cliffs;
                    // unlike an xz-only texture they do not stretch across near-vertical faces.
                    float3 weights=pow(abs(n),float3(4,4,4));
                    weights/=max(.001,weights.x+weights.y+weights.z);
                    float rockGrain=.5, drainage=.5, fine=.5;
                    float sideWeight=weights.x/max(.001,weights.x+weights.z);
                    float mountainJoint=0, buttress=0, couloir=0;
                    if(mountain>.001)
                    {
                        // Warped, oblique stationary projections; elongated joints drift
                        // down the face rather than making axis-aligned rectangular cells.
                        float2 warp=float2(broad-.5,filteredMountain(ground*.0061+71.3)-.5);
                        float3 q=i.wp+float3(warp.x*47,warp.y*23,warp.y*53);
                        rockGrain=filteredMountain(q.xz*.061)*weights.y
                            +filteredMountain(float2(q.z*.057+q.y*.013,q.y*.081-q.z*.019))*weights.x
                            +filteredMountain(float2(q.x*.063-q.y*.017,q.y*.087+q.x*.021))*weights.z;
                        if(alaska<.999)
                        {
                            drainage=lerp(filteredMountain(float2(q.x*.018+q.y*.0017,q.y*.0039-q.x*.0021)+13.7),
                                filteredMountain(float2(q.z*.019-q.y*.0013,q.y*.0041+q.z*.0019)+31.2),sideWeight);
                            fine=rockGrain;
                            if(_DayAtmosphere.z>.5)
                                fine=lerp(filteredMountain(float2(q.x*.051+q.y*.007,q.y*.011)+7.1),
                                    filteredMountain(float2(q.z*.047-q.y*.006,q.y*.013)+23.6),sideWeight);
                            mountainJoint=(1-smoothstep(.018,.068,abs(drainage-.46)))
                                *smoothstep(.36,.65,rockGrain);
                            buttress=smoothstep(.49,.76,drainage)*(.65+.35*broad);
                        }
                        if(alaska>.001)
                        {
                            // Vertical planes intersect a slope along its projected gravity
                            // (fall line). Blend fixed X/Y and Z/Y planes by the macro normal;
                            // never rotate position coordinates with a varying normal.
                            float3 ribsX=graniteRibs(i.wp.xy);
                            float3 ribsZ=graniteRibs(i.wp.zy+float2(371,0));
                            float3 ribs=lerp(ribsX,ribsZ,sideWeight);
                            mountainJoint=lerp(mountainJoint,ribs.x,alaska);
                            couloir=ribs.y;
                            buttress=lerp(buttress,ribs.z,alaska);
                            drainage=lerp(drainage,ribs.z,alaska);
                            fine=lerp(fine,ribs.z,alaska);
                        }
                    }
                    else
                    {
                        rockGrain=filteredTerrain(i.wp.xz*.075)*weights.y;
                        rockGrain+=filteredTerrain(i.wp.zy*float2(.065,.095))*weights.x;
                        rockGrain+=filteredTerrain(i.wp.xy*float2(.065,.095))*weights.z;
                        // Fixed world projections: rotating a world-position dot product by
                        // the varying slope normal made coordinates race across every face.
                        // Blend stationary side fields instead. 42 m cross-slope cells and
                        // 375 m vertical cells stretch the previous ribs a further 3:1.
                        drainage=lerp(filteredTerrain(i.wp.xy*float2(.024,.0027)+13.7),
                            filteredTerrain(i.wp.zy*float2(.024,.0027)+31.2),sideWeight);
                        fine=rockGrain;
                        if (_DayAtmosphere.z > .5)
                            fine=lerp(filteredTerrain(i.wp.xy*float2(.045,.005)+7.1),
                                filteredTerrain(i.wp.zy*float2(.045,.005)+23.6),sideWeight);
                    }
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
                    mountainRock*=1-alpine*smoothstep(.78,.94,macroNormal.y)
                        *(1-smoothstep(2920,3140,i.wp.y))*.88;
                    // Cover stays tied to the existing forest registry. Never add trees
                    // to strips, authored clearings, or slopes that have no sim timber.
                    // Registered canopy already has sim altitude/aspect/slope thinning.
                    // Do not erase that thin edge a second time with the rock mask.
                    float3 meadow=lerp(float3(.34,.41,.22),float3(.49,.47,.32),
                        saturate((i.wp.y-treeLine)/500));
                    albedo=lerp(albedo,meadow,mountain*(1-exposed)*(1-forest));
                    // Alpine upper ridges recover winter cover; low lake meadows retain
                    // their high sunny snowline. Aspect lowers the shaded-face line.
                    float snowLine=lerp(3000,1770,alaska)-aspect*lerp(300,240,alaska);
                    float raggedHeight=i.wp.y+(broad-.5)*lerp(70,130,alaska);
                    float snowAltitude=smoothstep(snowLine-lerp(100,160,alaska),
                        snowLine+lerp(190,320,alaska),raggedHeight);
                    // 35-40 degree snow retention, with sheltered hollows extending
                    // below the regional line. Near-vertical faces remain bare.
                    float retention=smoothstep(.75,.84,macroNormal.y+(broad-.5)*.06);
                    float cap=snowAltitude*retention*(1-ridge*.38);
                    float sheltered=smoothstep(.48,.78,gully+hollow*.30);
                    float patchAltitude=smoothstep(snowLine-430,snowLine+100,raggedHeight);
                    float streak=sheltered*patchAltitude*smoothstep(.42,.76,macroNormal.y)
                        *(1-retention)*(.35+aspect*.45)*(1-ridge*.65);
                    float snow=saturate(cap+streak)*(1-redrock);
                    // Alaska: clean benches/crests, predominantly bare >55-60 degree
                    // granite faces, with narrow continuous chutes between the rock ribs.
                    float bench=smoothstep(.52,.76,macroNormal.y);
                    float chute=couloir*smoothstep(.09,.22,macroNormal.y);
                    float alaskaSnow=snowAltitude*max(bench,chute*.95);
                    snow=lerp(snow,alaskaSnow,alaska)*(1-redrock);
                    // An absolute local snowline gate prevents sunny lake meadows from
                    // inheriting upper-ridge deposition through aspect/noise alone.
                    float meadowExclusion=smoothstep(2920,3140,i.wp.y);
                    snow*=lerp(1,meadowExclusion,alpine);
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
                        // Broken inclined seams, not a stack of height contours.
                        // Same warped triangular field as the joints: sparse, discontinuous.
                        float3 limestone=float3(.59,.59,.55);
                        if(alpine>.001)
                        {
                            float2 bedWarp=float2(broad-.5,drainage-.5)*1.7;
                            float bedLarge=filteredMountain(ground*.008+bedWarp);
                            float bedMid=rockGrain;
                            if(_DayAtmosphere.z>.5)
                                bedMid=lerp(filteredMountain(float2(i.wp.x*.009+i.wp.y*.014,
                                    i.wp.y*.026-i.wp.x*.004)+bedWarp),
                                    filteredMountain(float2(i.wp.z*.011-i.wp.y*.013,
                                    i.wp.y*.029+i.wp.z*.005)+bedWarp+41.3),sideWeight);
                            float bedFace=(1-smoothstep(.74,.88,macroNormal.y))*(1-hollow*.5);
                            limestone=lerp(float3(.49,.50,.49),float3(.68,.66,.60),bedLarge);
                            limestone*=1-mountainJoint*.23-(1-smoothstep(.25,.35,bedMid))*bedFace*.08;
                            beddingRelief=(bedMid-.5)*alpine*bedFace*.55;
                        }
                        beddingRelief-=mountainJoint*.65;
                        float graniteTone=filteredMountain(ground*.00045);
                        float3 granite=lerp(float3(.58,.60,.63),float3(.66,.64,.60),graniteTone);
                        granite*=1-mountainJoint*.48;
                        granite*=1+(rockGrain-.5)*.055+buttress*.065;
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
                        {
                            float shade=1-smoothstep(-.12,.65,dot(macroNormal,l));
                            float3 snowColour=lerp(float3(.98,.98,.96),float3(.66,.79,.94),shade*alaska*.45);
                            snowColour*=1+(rockGrain-.5)*.035+ridge*alaska*.025;
                            albedo=lerp(albedo,snowColour,snow*mountain);
                        }
                        talus=max(talus,debris*mountain);
                    }
                    // Hanging ice on concave upper benches: fractured light/shadow
                    // inside the bonded snow, with no new floating sheet or geometry.
                    float benchIce=alaska*snow*snowAltitude*smoothstep(.25,.60,macroNormal.y)
                        *(1-smoothstep(.85,.97,macroNormal.y))*saturate(hollow+gully*.45);
                    // Surface-gradient bump in world space. These are shading heights only:
                    // NO vertex movement, depth/parallax change, or collision displacement.
                    float detailFade=1-smoothstep(8,38,length(fwidth(i.wp)));
                    float relief=detailFade*cliff*(1-snow*lerp(.85,.40,alaska))*(1-forest)*
                        ((rockGrain-.5)*lerp(1.2,.18,alaska)-gully*lerp(4.0,2.5,mountain)
                            +beddingRelief+alaska*(buttress*6-mountainJoint*.45))*(1-talus*.8)
                        +detailFade*benchIce*((rockGrain-.5)*2.2-mountainJoint);
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
                        float glacier=0, iceAcross=0, iceAlong=0, iceWidth=1, iceLength=1, marginDistance=1000;
                        float surroundings=0;
                        float2 iceDown=float2(0,1);
                        float worldAA=max(.5,max(length(ddx(i.wp.xz)),length(ddy(i.wp.xz))));
                        for(int gi=0;gi<3;gi++)
                        {
                            float4 axis=_GlacierAxes[gi];
                            float2 delta=axis.zw-axis.xy;
                            float len=max(1,length(delta));
                            float2 down=delta/len, right=float2(-down.y,down.x);
                            float2 local=i.wp.zx-axis.xy;
                            float along=dot(local,down), across=dot(local,right);
                            float width=_GlacierWidths[gi];
                            float marginNoise=filteredMountain(float2(along*.008,across*.005)+gi*17.3);
                            // Signed-distance edge with ~1.5 pixel transition, no 65 m smear.
                            // Smooth stationary axis noise avoids slope/triangle sawteeth.
                            float boundary=width+(marginNoise-.5)*36;
                            float edgeDistance=abs(across)-boundary;
                            float edgeAA=max(.5,fwidth(edgeDistance)*.75);
                            float edge=1-smoothstep(-edgeAA,edgeAA,edgeDistance);
                            float endAA=max(.5,fwidth(along)*.75);
                            float ends=smoothstep(-endAA,endAA,along)
                                *(1-smoothstep(len-endAA,len+endAA,along));
                            float clearLz=(1-smoothstep(400,460,abs(along-len*.65)))
                                *(1-smoothstep(40,100,abs(across)));
                            float claim=max(edge*ends,clearLz);
                            float setting=(1-smoothstep(width+700,width+1300,abs(across)))
                                *smoothstep(-700,-100,along)*(1-smoothstep(len+650,len+1200,along));
                            surroundings=max(surroundings,setting);
                            if(claim>glacier)
                            {
                                glacier=claim; iceAcross=across; iceAlong=along;
                                iceWidth=width; iceLength=len; iceDown=down; marginDistance=edgeDistance;
                            }
                        }
                        if(surroundings>.001 || glacier>.001)
                        {
                            // Local glacial foreland overrides green plateau vegetation.
                            // Feather to tundra outside the corridor; no global snowline lowering.
                            float localSnow=smoothstep(.20,.77,macroNormal.y)*(.42+.40*broad);
                            float3 foreland=lerp(float3(.49,.49,.46)*( .88+rockGrain*.24),
                                float3(.88,.92,.96),localSnow);
                            albedo=lerp(albedo,foreland,surroundings*alaska);
                            forest*=1-surroundings;
                            if(glacier>.001)
                            {
                                // One centred 800 x 80 m snow runway per corridor, 65% downstream.
                                // 60 m safety feather outside the clear rectangle; no actual flattening.
                                float lz=(1-smoothstep(400,460,abs(iceAlong-iceLength*.65)))
                                    *(1-smoothstep(40,100,abs(iceAcross)));
                                float normalizedAcross=abs(iceAcross)/iceWidth;
                                float edgeZone=smoothstep(.65,.88,normalizedAcross);
                                float upper=1-smoothstep(.22,.35,iceAlong/iceLength);
                                // Only genuinely steep ice adds central icefall cracks; normal
                                // valley grade no longer activates fissures across the full width.
                                float steepIce=1-smoothstep(.86,.96,macroNormal.y);
                                float warp=filteredMountain(float2(iceAlong*.007,iceAcross*.009));
                                float curve=iceAcross*iceAcross/max(1,iceWidth)*.32;
                                float phaseM=iceAlong+curve+(warp-.5)*26;
                                float cell=floor(phaseM/38);
                                float side=iceAcross<0 ? 13.7 : 39.1;
                                float fissure=0;
                                // Jittered 38 m cells give nominal 16-60 m spacing. Each side
                                // has independent offset/length; broken arcs cannot span the valley.
                                for(int ci=-1;ci<=1;ci++)
                                {
                                    float id=cell+ci;
                                    float seed=hashTerrain(float2(id,side));
                                    float centre=(id+.5)*38+(seed-.5)*22;
                                    float distanceToCrack=abs(phaseM-centre);
                                    float crackWidth=lerp(.6,2.2,hashTerrain(float2(id+17,side)));
                                    float aa=max(.35,fwidth(phaseM)*.75);
                                    float crackLine=1-smoothstep(max(0,crackWidth-aa),crackWidth+aa,distanceToCrack);
                                    crackLine*=1-smoothstep(2,7,aa);
                                    float broken=smoothstep(.29,.58,warp+seed*.2);
                                    if(_DayAtmosphere.z>.5)
                                        broken=smoothstep(.33,.60,filteredMountain(
                                            float2(iceAcross*.024+id*.37,iceAlong*.013+side)));
                                    float reach=smoothstep(lerp(.55,.79,seed),.94,normalizedAcross);
                                    fissure=max(fissure,crackLine*broken*max(reach,max(upper,steepIce)*.75));
                                }
                                float cracks=fissure*max(edgeZone,max(upper,steepIce))*(1-lz);
                                float drift=(warp-.5)*48;
                                float medial=0;
                                for(int mi=0;mi<3;mi++)
                                {
                                    if(mi==2 && _DayAtmosphere.z<.5) continue;
                                    float bandSide=mi==0 ? -.43 : (mi==1 ? .17 : .49);
                                    float wander=filteredMountain(float2(iceAlong*.0035+mi*31.7,mi*17.1));
                                    float bandCentre=iceWidth*bandSide+(wander-.5)*75+drift*.35;
                                    float bandWidth=lerp(4,15,wander);
                                    float band=1-smoothstep(max(0,bandWidth-worldAA),bandWidth+12+worldAA,
                                        abs(iceAcross-bandCentre));
                                    float junction=smoothstep(.12+mi*.10,.32+mi*.12,iceAlong/iceLength);
                                    medial=max(medial,band*junction*lerp(.40,.88,wander));
                                }
                                float edgeAA=max(.5,fwidth(marginDistance)*.75);
                                float lateral=smoothstep(-18-edgeAA,-5+edgeAA,marginDistance)
                                    *(.65+.35*warp);
                                float debrisGrain=filteredMountain(float2(iceAlong*.09,iceAcross*.12));
                                float moraine=max(medial,lateral)*(1-lz)*(.50+.50*debrisGrain);
                                float3 iceColour=lerp(float3(.95,.975,.99),float3(.22,.43,.59),cracks*.80);
                                iceColour=lerp(iceColour,float3(.32,.31,.29)*( .75+debrisGrain*.5),moraine*.84);
                                iceColour*=1-(1-smoothstep(0,8+edgeAA,abs(marginDistance)))*.12*(1-lz);
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
                        }
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
                // Readable blue-grey skylight on shaded Alaska granite, including Ruth.
                dayFill=lerp(dayFill,max(dayFill,float3(.46,.53,.63)),alaska*(1-_SunsetOn));
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

// Shared by the skybox and water: the same direction always yields the same sky.
#ifndef AERO_SUNSET_SKY
#define AERO_SUNSET_SKY
float _SunsetOn, _WaterCalm;
float4 _SunsetSunDir;
// x: optical depth / metre, y: clear near range, z: cloud detail (0 low, 1 high).
float4 _DayAtmosphere;
float skyHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float skyNoise(float2 p) {
 float2 q=floor(p), f=frac(p); f=f*f*(3-2*f);
 return lerp(lerp(skyHash(q),skyHash(q+float2(1,0)),f.x),lerp(skyHash(q+float2(0,1)),skyHash(q+1),f.x),f.y);
}
// Low-frequency environment for rough painted surfaces: avoid evaluating cloud
// octaves on every scenery/aircraft fragment, especially on Balanced devices.
float3 SunsetEnvironment(float3 ray) {
 ray=normalize(ray);
 float y=max(0,ray.y);
 float3 fill=lerp(float3(.94,.65,.40),float3(.57,.59,.73),smoothstep(.08,.8,y));
 fill=lerp(float3(.19,.23,.32),fill,smoothstep(-.55,.10,ray.y));
 float sun=pow(saturate(dot(ray,normalize(_SunsetSunDir.xyz))),6);
 return fill+float3(.27,.16,.06)*sun;
}
// Analytic built-in atmosphere; shared sky and water directions keep the horizon coherent.
float3 DaySkyRough(float3 ray, float roughness) {
 ray=normalize(ray);
 float y=saturate(ray.y);
 float3 sky=lerp(float3(.48,.65,.80),float3(.075,.26,.52),pow(y,.42));
 float3 sunDir=normalize(float3(.4,.8,-.4));
 float sun=saturate(dot(ray,sunDir));
 sky+=float3(.14,.12,.075)*pow(sun,32);
 sky=lerp(sky,float3(1,.98,.88),smoothstep(.99994,.99998,sun));
 // Separate rounded cumulus lobes. Value-noise thresholding alone made
 // enormous connected, melted patches; cellular envelopes keep clear gaps.
 // Project onto a horizontal deck: distant banks shrink toward the horizon.
 // The old bounded denominator kept identical large stickers at every elevation.
 float2 p=ray.xz/max(.12,y+.08)*1.3;
 float pixelWidth=max(.06,length(fwidth(p))*.8) + roughness*.42;
 // Four nearest cell centres suffice: maximum lobe reach plus jitter < 1 cell.
 float2 cell=floor(p-.5);
 float field=-2;
 float crownLight=0;
 for (int cy=0;cy<2;cy++)
 for (int cx=0;cx<2;cx++)
 {
     float2 id=cell+float2(cx,cy);
     float seed=skyHash(id);
     float2 center=id+.5+float2(seed-.5,skyHash(id+41.7)-.5)*.44;
     float2 delta=p-center;
     float radius=lerp(.17,.42,skyHash(id+17.3));
     // Leave entire cells clear, rather than putting one identical cloud in each.
     float occupied=smoothstep(.25,.43,seed);
     // One broad base with a smaller sunlit turret, no textures or geometry.
     float baseShape=1-dot(delta/float2(radius*1.28,radius*.73),
                           delta/float2(radius*1.28,radius*.73));
     float2 turret=delta-float2(-radius*.28,radius*.30);
     float topShape=1-dot(turret/(radius*.78),turret/(radius*.78));
     float shape=max(baseShape,topShape)-(1-occupied)*4;
     if (shape>field)
     {
         field=shape;
         // Rounded lit interior with shaded lower/side edges. A bright rim
         // all around the old silhouette made each cloud read as a cut-out.
         crownLight=saturate(.32+sqrt(saturate(shape))*.55
             +delta.y/max(.1,radius)*.24-delta.x/max(.1,radius)*.18);
     }
 }
 float erosion=skyNoise(p*7.7+13.2)-.5;
 if (_DayAtmosphere.z > .5) erosion+=(skyNoise(p*23.1+7.1)-.5)*.35;
 field+=erosion*.85;
 float cloud=smoothstep(-.18-pixelWidth,.22+pixelWidth,field)*smoothstep(.005,.055,y);
 float light=saturate(crownLight+erosion*.30);
 float3 cloudColor=lerp(float3(.38,.48,.62),float3(1,.99,.95),light);

 // Unresolved capillary slopes spread cloud radiance and lower its contrast.
 return lerp(sky,cloudColor,cloud*.94*(1-roughness*.88));
}
float3 DaySky(float3 ray) { return DaySkyRough(ray,0); }
// Backlighting uses the horizontal view so main and submerged mirror eyes
// agree about the sun being behind the subject. Applied only by airframe materials.
float3 SunsetAirframeResponse(float3 color, float3 view, float reflected) {
 float2 horizontalView=view.xz/max(.001,length(view.xz));
 float2 horizontalSun=_SunsetSunDir.xz/max(.001,length(_SunsetSunDir.xz));
 float backlit=smoothstep(.15,.75,-dot(horizontalView,horizontalSun))*_SunsetOn;
 float luminance=dot(color,float3(.2126,.7152,.0722));
 float3 silhouette=luminance*float3(.48,.53,.63);
 color=lerp(color,color*.78,backlit);
 return lerp(color,silhouette,backlit*saturate(reflected)*.86);
}
float3 SunsetSky(float3 ray) {
 ray=normalize(ray); float y=max(ray.y,0);
 float3 sunDir=normalize(_SunsetSunDir.xyz);
 float toward=pow(saturate(dot(ray,sunDir)),4);
 // Broad peach/gold lower sky, rose middle, cool blue-grey above. Warmth is
 // directional, so the lake gets the same gold path from the reflected ray.
 float3 sky=lerp(float3(1,.84,.57),float3(.92,.65,.66),smoothstep(.025,.26,y));
 sky=lerp(sky,float3(.43,.50,.65),smoothstep(.18,.62,y));
 float aureole=toward*exp(-y*7);
 sky=lerp(sky,float3(1,.91,.64),aureole*.85);
 // A world-space deck of small, softly lit cloudlets, broken by broader gaps.
 // Continuous density and multiscale erosion avoid hard, flat camouflage patches.
 float2 p=ray.xz/max(.12,ray.y+.10)*2.4;
 float broad=skyNoise(p*.8);
 float2 warp=float2(skyNoise(p*2.1),skyNoise(p*2.1+31.7));
 float cells=skyNoise(p*6.5+warp*1.4);
 float detail=skyNoise(p*17+warp);
 float fine=skyNoise(p*41);
 float density=cells*.48+detail*.27+fine*.1+broad*.15;
 // Clear low band, broad gaps toward the sun, denser cool deck overhead.
 float breakup=smoothstep(.25,.60,broad);
 float cloud=smoothstep(.34,.73,density)*smoothstep(.055,.25,y)
     *lerp(.40+breakup*.60,1,smoothstep(.18,.55,y))
     *(1-toward*exp(-y*4)*.40);
 float edge=smoothstep(.34,.76,density);
 float3 shadow=lerp(float3(.84,.51,.47),float3(.43,.43,.56),smoothstep(.14,.60,y));
 float3 lit=lerp(float3(1,.84,.65),float3(.76,.69,.77),smoothstep(.14,.64,y));
 float3 cloudColor=lerp(lit,shadow,edge*.8);
 cloudColor=lerp(cloudColor,float3(1,.85,.64),toward*exp(-y*5)*.82);
 sky=lerp(sky,cloudColor,cloud*.68);
 float sun=dot(ray,normalize(_SunsetSunDir.xyz));
 sky+=float3(1,.66,.28)*pow(saturate(sun),350)*.55;
 sky=lerp(sky,float3(1,.91,.69),smoothstep(.99994,.99998,sun));
 return sky;
}
#endif

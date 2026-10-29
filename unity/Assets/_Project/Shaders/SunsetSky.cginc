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
float3 DaySky(float3 ray) {
 ray=normalize(ray);
 float y=saturate(ray.y);
 float3 sky=lerp(float3(.48,.65,.80),float3(.075,.26,.52),pow(y,.42));
 float3 sunDir=normalize(float3(.4,.8,-.4));
 float sun=saturate(dot(ray,sunDir));
 sky+=float3(.14,.12,.075)*pow(sun,32);
 sky=lerp(sky,float3(1,.98,.88),smoothstep(.99994,.99998,sun));
 // Separated cumulus banks, rather than a continuous sheet of tiny white cells.
 // Stereographic dome keeps cloud lobes round in low horizon views.
 float2 p=ray.xz/(.65+y)*3.8;
 float broad=skyNoise(p);
 float detail=skyNoise(p*3.7+13.2);
 float fine=detail;
 if (_DayAtmosphere.z > .5) fine=skyNoise(p*11.3+7.1);
 float density=broad*.66+detail*.20+fine*.14;
 float cloud=smoothstep(.565,.625,density)*smoothstep(.025,.11,y);
 // Offset density approximates a sun-facing bright crown and a cooler dense base.
 float crown=skyNoise(p+sunDir.xz*.09);
 float light=saturate(.52+(broad-crown)*5.0+(detail-.5)*1.2);
 // Dense interiors keep a shaded base; eroded edges receive the brighter rim.
 light=lerp(light,.98,(1-smoothstep(.57,.69,density))*.45);
 float3 cloudColor=lerp(float3(.40,.51,.65),float3(1,.99,.95),light);

 return lerp(sky,cloudColor,cloud*.94);
}
float3 SunsetSky(float3 ray) {
 ray=normalize(ray); float y=max(ray.y,0);
 float3 sunDir=normalize(_SunsetSunDir.xyz);
 float toward=pow(saturate(dot(ray,sunDir)),4);
 // Broad peach/gold lower sky, rose middle, cool blue-grey above. Warmth is
 // directional, so the lake gets the same gold path from the reflected ray.
 float3 sky=lerp(float3(1,.84,.57),float3(.92,.65,.66),smoothstep(.025,.38,y));
 sky=lerp(sky,float3(.48,.49,.64),smoothstep(.25,.90,y));
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
 float3 shadow=lerp(float3(.84,.51,.47),float3(.43,.43,.56),smoothstep(.18,.78,y));
 float3 lit=lerp(float3(1,.84,.65),float3(.76,.69,.77),smoothstep(.18,.82,y));
 float3 cloudColor=lerp(lit,shadow,edge*.8);
 cloudColor=lerp(cloudColor,float3(1,.85,.64),toward*exp(-y*5)*.82);
 sky=lerp(sky,cloudColor,cloud*.68);
 float sun=dot(ray,normalize(_SunsetSunDir.xyz));
 sky+=float3(1,.66,.28)*pow(saturate(sun),350)*.55;
 sky=lerp(sky,float3(1,.91,.69),smoothstep(.99994,.99998,sun));
 return sky;
}
#endif

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
 float y=saturate(ray.y*.5+.5);
 float3 fill=lerp(float3(.47,.39,.34),float3(.66,.73,.86),y);
 float sun=pow(saturate(dot(normalize(ray),normalize(_SunsetSunDir.xyz))),8);
 return fill+float3(.40,.23,.09)*sun;
}
// Analytic built-in atmosphere; shared sky and water directions keep the horizon coherent.
float3 DaySky(float3 ray) {
 ray=normalize(ray);
 float y=saturate(ray.y);
 float3 sky=lerp(float3(.48,.65,.80),float3(.075,.26,.52),pow(y,.48));
 float3 sunDir=normalize(float3(.4,.8,-.4));
 float sun=saturate(dot(ray,sunDir));
 sky+=float3(.14,.12,.075)*pow(sun,32);
 sky=lerp(sky,float3(1,.98,.88),smoothstep(.99994,.99998,sun));
 // Separated cumulus banks, rather than a continuous sheet of tiny white cells.
 float2 p=ray.xz/max(.045,y)*.85;
 float broad=skyNoise(p);
 float detail=skyNoise(p*3.7+13.2);
 float fine=detail;
 if (_DayAtmosphere.z > .5) fine=skyNoise(p*11.3+7.1);
 float density=broad*.72+detail*.23+fine*.05;
 float cloud=smoothstep(.59,.69,density)*smoothstep(.055,.18,y);
 // Offset density approximates a sun-facing bright crown and a cooler dense base.
 float crown=skyNoise(p+sunDir.xz*.18);
 float light=saturate(.52+(broad-crown)*4.5+(detail-.5)*.6);
 float3 cloudColor=lerp(float3(.40,.51,.65),float3(1,.99,.95),light);

 return lerp(sky,cloudColor,cloud*.94);
}
float3 SunsetSky(float3 ray) {
 ray=normalize(ray); float y=max(ray.y,0);
 float3 sunDir=normalize(_SunsetSunDir.xyz);
 float toward=pow(saturate(dot(ray,sunDir)),8);
 // Broad peach/gold lower sky, rose middle, cool blue-grey above. Warmth is
 // directional, so the lake gets the same gold path from the reflected ray.
 float3 sky=lerp(float3(.95,.71,.43),float3(.87,.57,.56),smoothstep(.015,.44,y));
 sky=lerp(sky,float3(.32,.43,.59),smoothstep(.30,.92,y));
 float aureole=toward*exp(-y*2.7);
 sky=lerp(sky,float3(1,.87,.61),aureole*.68);
 // A world-space deck of small, softly lit cloudlets, broken by broader gaps.
 // Continuous density and multiscale erosion avoid hard, flat camouflage patches.
 float2 p=ray.xz/max(.12,ray.y+.10)*2.4;
 float broad=skyNoise(p*.8);
 float2 warp=float2(skyNoise(p*2.1),skyNoise(p*2.1+31.7));
 float cells=skyNoise(p*6.5+warp*1.4);
 float detail=skyNoise(p*17+warp);
 float fine=skyNoise(p*41);
 float density=cells*.48+detail*.27+fine*.1+broad*.15;
 float cloud=smoothstep(.34,.73,density)*smoothstep(.035,.23,y);
 float edge=smoothstep(.34,.76,density);
 float3 shadow=lerp(float3(.72,.49,.49),float3(.34,.41,.54),smoothstep(.15,.85,y));
 float3 lit=lerp(float3(1,.78,.59),float3(.66,.69,.77),smoothstep(.18,.85,y));
 float3 cloudColor=lerp(lit,shadow,edge*.8);
 cloudColor=lerp(cloudColor,float3(1,.85,.64),toward*exp(-y*1.3)*.82);
 sky=lerp(sky,cloudColor,cloud*.68);
 float sun=dot(ray,normalize(_SunsetSunDir.xyz));
 sky+=float3(1,.66,.28)*pow(saturate(sun),350)*.55;
 sky=lerp(sky,float3(1,.91,.69),smoothstep(.99994,.99998,sun));
 return sky;
}
#endif

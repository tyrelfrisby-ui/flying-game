// Shared by the skybox and water: the same direction always yields the same sky.
#ifndef AERO_SUNSET_SKY
#define AERO_SUNSET_SKY
float _SunsetOn, _WaterCalm;
float4 _SunsetSunDir;
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
float3 SunsetSky(float3 ray) {
 ray=normalize(ray); float y=max(ray.y,0);
 float3 sky=lerp(float3(1.0,.78,.47),float3(.77,.61,.69),smoothstep(0,.20,y));
 sky=lerp(sky,float3(.35,.45,.64),smoothstep(.12,.80,y));
 float toward=pow(saturate(dot(ray,normalize(_SunsetSunDir.xyz))),12);
 sky+=float3(.14,.08,.025)*toward*exp(-y*7);
 // A world-space deck of small, softly lit cloudlets, broken by broader gaps.
 // Continuous density and multiscale erosion avoid hard, flat camouflage patches.
 float2 p=ray.xz/max(.12,ray.y+.10)*2.4;
 float broad=skyNoise(p*.8);
 float2 warp=float2(skyNoise(p*2.1),skyNoise(p*2.1+31.7));
 float cells=skyNoise(p*6.5+warp*1.4);
 float detail=skyNoise(p*17+warp);
 float fine=skyNoise(p*41);
 float density=cells*.48+detail*.27+fine*.1+broad*.15;
 float cloud=smoothstep(.30,.77,density)*smoothstep(.025,.16,y);
 float edge=smoothstep(.34,.76,density);
 float3 shadow=lerp(float3(.74,.57,.64),float3(.39,.46,.61),smoothstep(.08,.7,y));
 float3 lit=lerp(float3(1,.77,.66),float3(.68,.69,.78),smoothstep(.08,.7,y));
 float3 cloudColor=lerp(lit,shadow,edge*.8);
 sky=lerp(sky,cloudColor,cloud*.68);
 float sun=dot(ray,normalize(_SunsetSunDir.xyz));
 sky+=float3(1,.66,.28)*pow(saturate(sun),350)*.24;
 sky=lerp(sky,float3(1,.91,.69),smoothstep(.99994,.99998,sun));
 return sky;
}
#endif

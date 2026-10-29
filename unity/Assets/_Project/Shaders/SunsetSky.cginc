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
float3 SunsetSky(float3 ray) {
 ray=normalize(ray); float y=max(ray.y,0);
 float3 sky=lerp(float3(1.0,.69,.34),float3(.72,.48,.62),smoothstep(0,.19,y));
 sky=lerp(sky,float3(.24,.30,.49),smoothstep(.12,.85,y));
 float toward=pow(saturate(dot(ray,normalize(_SunsetSunDir.xyz))),12);
 sky+=float3(.25,.13,.035)*toward*exp(-y*7);
 // A fixed cloud deck in world direction: no screen-space texture or mirrored cloud mismatch.
 float2 p=ray.xz/max(.12,ray.y+.10)*2.4;
 float broad=skyNoise(p*.8);
 float cells=skyNoise(p*7+skyNoise(p*1.7)*2);
 float detail=skyNoise(p*17);
 float cloud=smoothstep(.37,.70,cells*.6+broad*.28+detail*.12);
 cloud*=smoothstep(.025,.16,y);
 float3 cloudColor=lerp(float3(.94,.57,.49),float3(.44,.43,.58),smoothstep(.08,.7,y));
 sky=lerp(sky,cloudColor,cloud*.72);
 float sun=dot(ray,normalize(_SunsetSunDir.xyz));
 sky+=float3(1,.66,.28)*pow(saturate(sun),350)*.24;
 sky=lerp(sky,float3(1,.91,.69),smoothstep(.99994,.99998,sun));
 return sky;
}
#endif

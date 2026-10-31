// Built-in analytic urban aerosol: integrate a bounded ellipsoid along the visible ray.
// No textures, transparent shells, depth sampling or extra render pass.
#ifndef FLYINGGAME_URBAN_GLOW
#define FLYINGGAME_URBAN_GLOW
float4 _UrbanGlow0, _UrbanGlow1, _UrbanGlow2, _UrbanGlow3;
float4 _UrbanGlow4, _UrbanGlow5, _UrbanGlow6, _UrbanGlow7;
float _UrbanNightFill;
float urbanHazeCity(float3 endPoint,float4 city)
{
 if(city.w<.5) return 0;
 float3 scale=float3(1850,620,1850);
 float3 origin=(_WorldSpaceCameraPos-city.xyz-float3(0,220,0))/scale;
 float3 segment=(endPoint-_WorldSpaceCameraPos)/scale;
 float dd=max(.00001,dot(segment,segment));
 float closest=saturate(-dot(origin,segment)/dd);
 float3 radial=origin+segment*closest;
 // Smooth Gaussian volume, finite visible segment prevents glow through foreground terrain.
 float density=exp(-dot(radial,radial)*2.5);
 float integral=min(1.0,length(segment)*.45)*density;
 return integral*city.w;
}
float3 urbanHaze(float3 colour,float3 endPoint)
{
 if(_UrbanNightFill<.5) return colour;
 float haze=urbanHazeCity(endPoint,_UrbanGlow0)+urbanHazeCity(endPoint,_UrbanGlow1)
 +urbanHazeCity(endPoint,_UrbanGlow2)+urbanHazeCity(endPoint,_UrbanGlow3)
 +urbanHazeCity(endPoint,_UrbanGlow4)+urbanHazeCity(endPoint,_UrbanGlow5)
 +urbanHazeCity(endPoint,_UrbanGlow6)+urbanHazeCity(endPoint,_UrbanGlow7);
 return colour+float3(.17,.075,.022)*min(.85,haze);
}
#endif

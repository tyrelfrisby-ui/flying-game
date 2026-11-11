// Shared by the skybox and water: the same direction always yields the same sky.
#ifndef AERO_SUNSET_SKY
#define AERO_SUNSET_SKY
float _SunsetOn, _WaterCalm;
float4 _SunsetSunDir;
// x: optical depth / metre, y: clear near range, z: detail (0 low, 1 high). Nothing in THIS file reads it
// any more (the painted deck used z); it stays declared because the shaders that include this one do read it.
float4 _DayAtmosphere;
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
// There used to be a painted cumulus deck here, drawn into the skybox at infinity: cellular lobes with multiscale
// erosion, parameterised by azimuth and elevation, with a cross-fade round atan2's branch cut to hide the seam it
// put down the -x axis. Ty, 2026-11-11: "Delete painted sky deck I don't like that one". It is gone, and with it
// the branch-cut machinery, the pixel-footprint dissolve and the cloud use of _DayAtmosphere.z (that uniform is
// still set and still read by Terrain/Powell/UrbanFacade for their own detail switches — this file no longer
// reads it). What is left is the sky itself, which is load-bearing: Water.shader reflects through DaySkyRough and
// SunsetSky, and the airframes and airport surfaces light through SunsetEnvironment.
// Clouds now come only from geometry — the cloud playground's banks and patches, the thermal cumulus that is
// built from the same impostors, and the wisp sheets.
float3 DaySkyRough(float3 ray, float roughness) {
 ray=normalize(ray);
 float y=saturate(ray.y);
 float3 sky=lerp(float3(.48,.65,.80),float3(.075,.26,.52),pow(y,.42));
 // Elevated downward views see below the true horizon beyond the finite world.
 // Continue into a softly graded haze hemisphere rather than clamping to blue.
 sky=lerp(sky,float3(.34,.46,.58),smoothstep(0,.70,-ray.y)*.75);
 float3 sunDir=normalize(float3(.4,.8,-.4));
 float sun=saturate(dot(ray,sunDir));
 sky+=float3(.14,.12,.075)*pow(sun,32);
 sky=lerp(sky,float3(1,.98,.88),smoothstep(.99994,.99998,sun));
 // roughness is kept in the signature because Water.shader passes the surface's own roughness to it; with the
 // painted deck gone there is nothing left in this sky whose contrast it would have lowered.
 return sky;
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
 // The sunset's own painted cloudlet deck went with the daytime one (Ty, 2026-11-11). The geometry clouds are
 // lit by the scene's sun, so they take the sunset with them; a second, painted set at infinity did not.
 float sun=dot(ray,normalize(_SunsetSunDir.xyz));
 sky+=float3(1,.66,.28)*pow(saturate(sun),350)*.55;
 sky=lerp(sky,float3(1,.91,.69),smoothstep(.99994,.99998,sun));
 return sky;
}
#endif

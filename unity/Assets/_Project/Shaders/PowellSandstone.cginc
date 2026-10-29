// Original procedural Navajo sandstone, shared only by Powell terrain and lake rocks.
// World Y contacts; stationary side projections; no geometry/depth displacement.
#ifndef FLYING_POWELL_INCLUDED
#define FLYING_POWELL_INCLUDED
float _PowellWaterLevel;
float4 _DayAtmosphere;
// Sine-free hash avoids large-angle precision patches in Metal world coordinates.
float powellHash(float2 p)
{
    float3 q=frac(float3(p.x,p.y,p.x)*.1031);
    q+=dot(q,q.yzx+33.33);
    return frac((q.x+q.y)*q.z);
}
float powellNoise(float2 p)
{
    float2 q=floor(p), f=frac(p); f=f*f*f*(f*(f*6-15)+10);
    float v=lerp(lerp(powellHash(q),powellHash(q+float2(1,0)),f.x),
        lerp(powellHash(q+float2(0,1)),powellHash(q+1),f.x),f.y);
    return lerp(.5,v,1-smoothstep(.125,.3,max(length(ddx(p)),length(ddy(p)))));
}
void powellSurface(float3 p, inout float3 n, out float3 rock)
{
    float steep=1-smoothstep(.45,.82,abs(n.y));
    float side=abs(n.x)/max(.001,abs(n.x)+abs(n.z));
    float h=p.y-_PowellWaterLevel;
    // Unequal beds from a monotonically increasing world-height coordinate.
    // Local thickness spans roughly .5–6 metres, without tilted face-UV bands.
    float bed=p.y*.34+.30*sin(p.y*.63)+.14*sin(p.y*1.47);
    float cell=floor(bed), phase=frac(bed);
    float aa=max(.015,fwidth(bed));
    float bedTone=lerp(.5,powellHash(float2(cell,17)),1-smoothstep(.15,.6,aa));
    float contact=(1-smoothstep(.035,.035+aa,min(phase,1-phase)))*(1-smoothstep(.15,.65,aa));
    float grain=.5;
    // Low/iOS default omits fine grain, cross bedding and cap speckles.
    if(_DayAtmosphere.z>.5)
        grain=lerp(powellNoise(p.xy*float2(.65,.32)),powellNoise(p.zy*float2(.65,.32)),side);
    float streak=lerp(powellNoise(p.xy*float2(.24,.03)+13),powellNoise(p.zy*float2(.24,.03)+13),side);
    float joints=lerp(powellNoise(p.xy*float2(.095,.006)),powellNoise(p.zy*float2(.095,.006)),side);
    float fracture=1-smoothstep(.12,.25,joints);
    // Blend stationary samples, never positions multiplied by a varying normal.
    float2 crossPhase=p.y*8+p.xz*.65+grain*2;
    float2 crossFine=sin(crossPhase)*(1-smoothstep(.6,2.0,fwidth(crossPhase)));
    float crossBed=step(.5,_DayAtmosphere.z)*lerp(crossFine.x,crossFine.y,side);
    rock=lerp(float3(.710,.345,.180),float3(.851,.541,.306),.36+bedTone*.40);
    rock*=1+(grain-.5)*.10+crossBed*.025-contact*.14*steep;
    float cap=smoothstep(44,65,h)*(.70-steep*.60)+(1-steep)*.16;
    rock=lerp(rock,float3(.910,.788,.627),saturate(cap));
    float varnish=smoothstep(.49,.73,streak)*steep*(.42+.58*powellNoise(float2(p.y*.035,7)));
    rock=lerp(rock,float3(.17,.105,.075),varnish*.64);
    rock*=1-fracture*steep*.25;
    // Continuous 26 m calcite ring; sharp, horizontal former high-water mark.
    float ring=(1-smoothstep(26-max(.15,fwidth(h)),26+max(.15,fwidth(h)),h))*smoothstep(-1,1,h);
    rock=lerp(rock,float3(.91,.865,.735)*(.94+streak*.08),ring*.90);
    rock*=lerp(.70,1,smoothstep(0,1.3,h));
    // Optical talus apron at the existing wall foot: no added obstacle geometry.
    float talus=(1-smoothstep(3,11,h))*(1-steep)*smoothstep(-2,1,h);
    rock=lerp(rock,float3(.76,.64,.48)*(.88+grain*.24),talus*.65);
    // Sparse dry shrubs / rubble flecks on cap surfaces, filtered before subpixel.
    float topMark=0;
    if(_DayAtmosphere.z>.5) topMark=smoothstep(.74,.86,powellNoise(p.xz*.6))*(1-steep);
    rock=lerp(rock,float3(.34,.32,.19),topMark*.55);
    float relief=steep*((grain-.5)*.12-fracture*.35-contact*.06);
    float3 dx=ddx(p), dy=ddy(p), r1=cross(dy,n), r2=cross(n,dx);
    float det=dot(dx,r1);
    float3 grad=(ddx(relief)*r1+ddy(relief)*r2)*sign(det)/max(.00001,abs(det));
    grad*=min(1,.24/max(.001,length(grad)));
    n=normalize(n-grad);
}
#endif

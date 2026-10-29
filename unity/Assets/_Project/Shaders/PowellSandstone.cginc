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
sampler2D _PowellRimHeights;
float4 _PowellRimBounds;
// Large clean faces separate runoff catchments. Two stationary world projections
// use 90/170 m cells, independent of height so clusters follow the whole wall.
float powellCatchment(float u, float depth)
{
    float broad=powellNoise(float2(u/90,depth/170)+float2(31,67));
    return smoothstep(.59,.73,broad);
}
// Runoff is widest/darkest at its lip, narrowing to irregular 10–80 m tails.
float powellRunnel(float u, float y, float rim, float offset, float cluster)
{
    float warped=u+(powellNoise(float2(u*.012,offset))-.5)*18;
    float column=floor(warped/13);
    float seed=powellHash(float2(column,offset));
    float centre=(column+.15+.7*powellHash(float2(column+71,offset)))*13;
    float drop=max(0,rim-y);
    float lengthM=lerp(10,80,powellHash(float2(column+39,offset)));
    float progress=saturate(drop/lengthM);
    float width=lerp(.8,4.8,seed)*lerp(1,.12,progress);
    float wander=(powellNoise(float2(column+offset,y*.06))-.5)*lerp(.3,2.4,progress);
    float aa=max(.12,fwidth(u));
    float thin=1-smoothstep(width*.45,width+aa,abs(warped-centre+wander));
    float tail=(1-smoothstep(.25,1,progress));
    // Outside catchments only a few isolated columns can carry a faint stain.
    float density=max(cluster,smoothstep(.975,.995,seed)*.24);
    float ragged=lerp(1,.55+.45*powellNoise(float2(u*.45,y*.24)),progress);
    return thin*tail*density*ragged*(.45+.55*seed)
        *(1-smoothstep(3,10,fwidth(u)))*step(y,rim+.3);
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
    float joints=lerp(powellNoise(p.xy*float2(.09,.035)),powellNoise(p.zy*float2(.09,.035)),side);
    float fracture=1-smoothstep(.13,.23,joints);
    // Blend stationary samples, never positions multiplied by a varying normal.
    float2 crossPhase=p.y*8+p.xz*.65+grain*2;
    float2 crossFine=sin(crossPhase)*(1-smoothstep(.6,2.0,fwidth(crossPhase)));
    float crossBed=step(.5,_DayAtmosphere.z)*lerp(crossFine.x,crossFine.y,side);
    rock=lerp(float3(.710,.345,.180),float3(.851,.541,.306),.36+bedTone*.40);
    rock*=1+(grain-.5)*.10+crossBed*.025-contact*.14*steep;
    float cap=smoothstep(44,65,h)*(.70-steep*.60)+(1-steep)*.16;
    rock=lerp(rock,float3(.910,.788,.627),saturate(cap));
    float rim=tex2D(_PowellRimHeights,(p.xz-_PowellRimBounds.xy)*_PowellRimBounds.zw).r;
    float clusterX=powellCatchment(p.x,p.z);
    float clusterZ=powellCatchment(p.z,p.x);
    float runX=powellRunnel(p.x,p.y,rim,7,clusterX);
    float runZ=powellRunnel(p.z,p.y,rim,19,clusterZ);
    if(_DayAtmosphere.z>.5)
    {
        runX=max(runX,powellRunnel(p.x+4.1,p.y,rim,43,clusterX)*.60);
        runZ=max(runZ,powellRunnel(p.z+4.1,p.y,rim,61,clusterZ)*.60);
    }
    float varnish=lerp(runX,runZ,side)*steep*smoothstep(26,29,h);
    float3 stain=lerp(float3(.105,.075,.060),float3(.125,.145,.165),streak);
    rock=lerp(rock,stain,varnish*.82);
    rock*=1-fracture*steep*.035;
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
    float relief=steep*((grain-.5)*.20-fracture*.045-contact*.06);
    float3 dx=ddx(p), dy=ddy(p), r1=cross(dy,n), r2=cross(n,dx);
    float det=dot(dx,r1);
    float3 grad=(ddx(relief)*r1+ddy(relief)*r2)*sign(det)/max(.00001,abs(det));
    grad*=min(1,.24/max(.001,length(grad)));
    n=normalize(n-grad);
}
#endif

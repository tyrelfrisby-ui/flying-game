// The thin wispy layers' density before any wake (lane A, 2026-11-05): ONE function for the wake-reactive patch
// (CloudWispAdvect) and the far sheet (CloudWispFar), so they meet without a seam. Codex's stretched two-octave wisps,
// now inside a large-scale coverage mask so a layer is broken patches a few km across with clear sky between, not an
// overcast. The mask is analytic (sines, no hash) so a C# tool can evaluate the same thing (CloudPlayground.WispCover).
// world = Unity X/Z metres minus the layer's wind drift. fine01 = how much of the fine octave the pixel can resolve.
float WispHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float WispNoise(float2 p) { float2 a=floor(p), f=frac(p); f=f*f*(3-2*f); return lerp(lerp(WispHash(a),WispHash(a+float2(1,0)),f.x),lerp(WispHash(a+float2(0,1)),WispHash(a+1),f.x),f.y); }
float WispCover(float2 world)
{
    float a = sin(world.x*.00061 + 1.3*sin(world.y*.00043)) * sin(world.y*.00057 + 1.1*sin(world.x*.00037+.7));
    return smoothstep(.05,.45,a);
}
float WispDetail(float2 world, float coarse01, float fine01)
{
    float2 w = world*.016;
    float n = lerp(.5, WispNoise(w*float2(.45,3)), coarse01) + lerp(.5, WispNoise(w*float2(1.5,8)), fine01)*.35;
    return smoothstep(.45,.92,n)*.58;
}
float WispSeed(float2 world, float coarse01, float fine01) { return WispDetail(world,coarse01,fine01)*WispCover(world); }
// A low patch (CloudSites.Low, lane A 2026-11-05): one ragged round patch instead of the layer's coverage.
// mask = centre X/Z, radius (m). Fixed over the ground like the banks; its wisps stream through it with the wind.
float WispPatchCover(float2 world, float3 mask)
{
    float2 d = world - mask.xy;
    float ragged = (WispNoise(world*.006+mask.xy*.001)-.5)*.5 + (WispNoise(world*.02)-.5)*.2;
    return smoothstep(0,.35,1-length(d)/mask.z+ragged);
}

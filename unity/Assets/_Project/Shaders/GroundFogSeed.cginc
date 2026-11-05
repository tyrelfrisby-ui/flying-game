// Ground fog before any wake (lane A, 2026-11-05; docs/CLOUDS-WAKE-VISUALS.md "Ground fog"). ONE function for the
// wake-stirred patch (GroundFogAdvect seeds and relaxes toward it) and for the rest of the volume (GroundFog draws it
// directly), so the patch meets the unstirred fog without a seam.
// Volume frame: world X/Z metres, h01 = height above the surface over the sim box height (the fog's own top is near
// h01 = 0.5, so a vortex can lift fog into the clear air above it). Site = an oriented box (a strip) or an ellipse
// (a lake): _FogSite = centre X/Z, cos/sin of the long axis's heading; _FogHalf = half length, half width, lake (0/1).
#include "CloudWispSeed.cginc"
float4 _FogSite, _FogHalf;
float4 _FogDrift;   // the fog's wind drift so far (world X/Z m)

// 0 outside the site, 1 well inside; a soft, noisy edge ~60 m wide.
float FogSiteMask(float2 world)
{
    float2 d = world - _FogSite.xy;
    // heading h: along = (sin h, cos h) in (east, north); _FogSite.zw = (sin h, cos h)
    float a = dot(d, _FogSite.zw), c = dot(d, float2(_FogSite.w, -_FogSite.z));
    float edge = (WispNoise(world * .012 + 3.1) - .5) * 70;
    float inside;
    if (_FogHalf.z > .5)
    {
        float2 q = float2(a / _FogHalf.x, c / _FogHalf.y);
        float r = length(q), rad = min(_FogHalf.x, _FogHalf.y);
        inside = (1 - r) * rad;                         // metres inside the shore, roughly
    }
    else inside = min(_FogHalf.x - abs(a), _FogHalf.y - abs(c));
    return smoothstep(0, 60, inside + edge);
}

// The unstirred fog's column at a world X/Z: x = its top (h01), y = its density below the top (site mask included).
float2 FogColumn(float2 world)
{
    float2 w = world - _FogDrift.xy;
    float n1 = WispNoise(w * .009), n2 = WispNoise(w * .031 + 7.7), n3 = WispNoise(w * float2(.08, .05) + 2.3);
    // The top undulates (0.30-0.60 of the box); thick and thin banks and clear gaps across the field; fine streaks.
    float top = .30 + .26 * n1 + .06 * n3;
    // A fine octave (~8 m) so a stirred patch has texture to carry and the eye can see it move.
    float n4 = WispNoise(w * float2(.13, .17) + 5.1);
    float body = saturate(.40 + 1.1 * (n2 - .5) + .6 * (n1 - .5)) * (.6 + .25 * n3 + .3 * n4);
    return float2(top, body * FogSiteMask(world));
}
float FogProfile(float top, float h01) { return 1 - smoothstep(top * .55, top, h01); }
// Fog density 0..1 at a world X/Z and h01, unstirred.
float FogSeed(float2 world, float h01)
{
    float2 c = FogColumn(world);
    return FogProfile(c.x, h01) * c.y;
}
// The unstirred fog above h01 (density x h01 units), for its self-shadow.
float FogSeedAbove(float2 c, float h01)
{
    float sum = 0;
    for (int k = 1; k <= 4; k++) sum += FogProfile(c.x, h01 + (c.x - h01) * (k - .5) / 4);
    return c.y * sum / 4 * max(0, c.x - h01);
}

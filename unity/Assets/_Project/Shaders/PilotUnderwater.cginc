// Shared by Hidden/FlyingGame/PilotUnderwater and FlyingGame/Water: the pilot's view under water (Ty t922u; the rule,
// the constants and their tests are Core UnderwaterView). Globals are set by PilotUnderwater.cs for the seat camera's
// own render and cleared after it; _PilotWaterMode is 0 (off) for every other camera and view.
#ifndef FLYINGGAME_PILOT_UNDERWATER
#define FLYINGGAME_PILOT_UNDERWATER
float _PilotWaterY, _PilotWaterMode, _PilotWaterK;
float4 _PilotWaterColor, _PilotWaterNear;

// UnderwaterView.SubmergedLength: of the ray from an eye e metres above the water (negative = under it), going up at
// `up` (the unit direction's y), out to len metres, how much is under the surface.
float PilotSubmerged(float e, float up, float len)
{
    if (e < 0) return up <= 0 ? len : min(len, -e / up);
    if (up >= 0) return 0;
    return max(0, len - e / -up);
}

// x = submerged path (m), y = murk (0..1, UnderwaterView.Murk), z = light (the in-scattered colour's brightness).
float3 PilotPath(float3 eye, float3 seg)
{
    float len = length(seg);
    float3 dir = seg / max(1e-4, len);
    float s = PilotSubmerged(eye.y - _PilotWaterY, dir.y, len);
    // Eye above the water (UnderwaterView.Mode.NearField): the airframe only; the lake keeps its own look.
    float near = _PilotWaterMode < 1.5 ? 1 - smoothstep(_PilotWaterNear.x, _PilotWaterNear.y, len) : 1;
    float murk = (1 - exp(-s * _PilotWaterK)) * near;
    // Brighter looking up toward the daylight, darker the deeper the point it ends at.
    float depth = max(0, _PilotWaterY - (eye.y + seg.y));
    float light = lerp(1, .45, saturate(depth / 12)) * (.8 + .6 * saturate(dir.y));
    return float3(s * near, murk, light);
}
#endif

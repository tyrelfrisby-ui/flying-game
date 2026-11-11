// Ground fog, wake-stirred patch (lane A, 2026-11-05; docs/CLOUDS-WAKE-VISUALS.md "Ground fog"). The fog is a passive
// tracer in the real air: eight horizontal slices from the surface up (two ARGBHalf targets, four slices each), moved
// through Aero.Wake.WakeField's velocity (vortex lines from the aeroplanes' actual lift, slipstream from their actual
// thrust; WakeField.WriteVolumeToTexture, a 4 x 2 atlas of the same eight heights) plus the surface wind.
// Nothing here knows what a vortex is: the rolls, the trench under the track and the curls lifted beside it are the
// tracer following the air. Below the surface nothing comes in (the fog lies on it); above the box, clear air.
// MacCormack: a semi-Lagrangian step alone smears a tracer by about sqrt(u t dx) (7 m after 17 s of a 2 m/s vortex at
// 1.5 m texels), which erased the curls; passes 0-1 predict, passes 2-3 trace the prediction forward again, correct by
// half the round-trip error and clamp to the predicted neighbourhood. The stirred fog relaxes back toward the
// unstirred fog (GroundFogSeed.cginc) over _Relax seconds: fog re-forms. Force-included via
// BuildScript.EnsureAlwaysIncludedShaders.
Shader "Hidden/FlyingGame/GroundFogAdvect"
{
 Properties { _FogA("Slices 0-3",2D)="black" {} _FogB("Slices 4-7",2D)="black" {} _HatA("Predicted 0-3",2D)="black" {} _HatB("Predicted 4-7",2D)="black" {} _WakeAtlas("Wake 4x2",2D)="black" {} }
 SubShader { Cull Off ZWrite Off ZTest Always
 CGINCLUDE
 #include "UnityCG.cginc"
 #include "GroundFogSeed.cginc"
 sampler2D _FogA, _FogB, _HatA, _HatB, _WakeAtlas;
 float4 _FogPatch;      // origin X/Z (m), size (m), box height H (m)
 float4 _FogShift;      // old-to-new UV offset, dt (s), initialise (0/1)
 float4 _FogWind;       // surface wind X/Z (m/s), wake texel inset (uv), relax time (s)
 float4 _FogTexel;      // 1 / resolution
 float Slice(float4 a, float4 b, float s)
 {
     // Density at fractional slice s (0..7 centres); below slice 0 the fog lies on the surface, above 7 clear air.
     float4 wa = saturate(1 - abs(s - float4(0,1,2,3)));
     float4 wb = saturate(1 - abs(s - float4(4,5,6,7)));
     float v = dot(a, wa) + dot(b, wb);
     return s < 0 ? a.x : v;
 }
 // The displacement over one step at slice k (uv units horizontally, slice units vertically).
 float3 Step(float2 uv, int k)
 {
     float2 tile = float2(k % 4, k / 4);
     float2 inset = clamp(uv, _FogWind.z, 1 - _FogWind.z);
     float4 wake = tex2Dlod(_WakeAtlas, float4((tile + inset) / float2(4, 2), 0, 0));
     float dt = _FogShift.z;
     return float3((wake.rg + _FogWind.xy) * dt / _FogPatch.z, wake.a * dt / _FogPatch.w * 8);
 }
 float Outside(float2 uv) { return step(uv.x, 0) + step(1, uv.x) + step(uv.y, 0) + step(1, uv.y); }
 // Prediction: back along the air from this texel, in the OLD patch (shifted).
 float Predict(float2 uv, int k)
 {
     float3 d = Step(uv, k);
     float2 back = uv + _FogShift.xy - d.xy;
     float s = k - d.z;
     float dens = Slice(tex2Dlod(_FogA, float4(back, 0, 0)), tex2Dlod(_FogB, float4(back, 0, 0)), s);
     float seed = FogSeed(_FogPatch.xy + (back - _FogShift.xy) * _FogPatch.z, (s + .5) / 8);
     return lerp(dens, seed, saturate(Outside(back) + _FogShift.w));
 }
 // Correction: the prediction traced forward again should give back the old fog here; half the error is the
 // prediction's own, removed; the result is held within the predicted fog around the departure point.
 float Correct(float2 uv, int k)
 {
     float3 d = Step(uv, k);
     float4 ha = tex2Dlod(_HatA, float4(uv, 0, 0)), hb = tex2Dlod(_HatB, float4(uv, 0, 0));
     float hat = Slice(ha, hb, k);
     float2 fwd = uv + d.xy;
     float s = k + d.z;
     float there = Slice(tex2Dlod(_HatA, float4(fwd, 0, 0)), tex2Dlod(_HatB, float4(fwd, 0, 0)), s);
     float2 old = uv + _FogShift.xy;
     float now = Slice(tex2Dlod(_FogA, float4(old, 0, 0)), tex2Dlod(_FogB, float4(old, 0, 0)), k);
     float fixedUp = hat + .5 * (now - there);
     // Limiter: the predicted fog at the four texels round this one.
     float2 e = _FogTexel.xy;
     float n1 = Slice(tex2Dlod(_HatA, float4(uv + float2(e.x, 0), 0, 0)), tex2Dlod(_HatB, float4(uv + float2(e.x, 0), 0, 0)), k);
     float n2 = Slice(tex2Dlod(_HatA, float4(uv - float2(e.x, 0), 0, 0)), tex2Dlod(_HatB, float4(uv - float2(e.x, 0), 0, 0)), k);
     float n3 = Slice(tex2Dlod(_HatA, float4(uv + float2(0, e.y), 0, 0)), tex2Dlod(_HatB, float4(uv + float2(0, e.y), 0, 0)), k);
     float n4 = Slice(tex2Dlod(_HatA, float4(uv - float2(0, e.y), 0, 0)), tex2Dlod(_HatB, float4(uv - float2(0, e.y), 0, 0)), k);
     float lo = min(hat, min(min(n1, n2), min(n3, n4))), hi = max(hat, max(max(n1, n2), max(n3, n4)));
     float dens = (Outside(old) + _FogShift.w) > 0 ? hat : clamp(fixedUp, lo, hi);
     // Re-forming: back toward the unstirred fog at this texel.
     float here = FogSeed(_FogPatch.xy + uv * _FogPatch.z, (k + .5) / 8);
     return lerp(dens, here, 1 - exp(-_FogShift.z / _FogWind.w));
 }
 ENDCG
 Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 float4 frag(v2f_img i):SV_Target { return float4(Predict(i.uv,0), Predict(i.uv,1), Predict(i.uv,2), Predict(i.uv,3)); }
 ENDCG
 }
 Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 float4 frag(v2f_img i):SV_Target { return float4(Predict(i.uv,4), Predict(i.uv,5), Predict(i.uv,6), Predict(i.uv,7)); }
 ENDCG
 }
 Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 float4 frag(v2f_img i):SV_Target { return float4(Correct(i.uv,0), Correct(i.uv,1), Correct(i.uv,2), Correct(i.uv,3)); }
 ENDCG
 }
 Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 float4 frag(v2f_img i):SV_Target { return float4(Correct(i.uv,4), Correct(i.uv,5), Correct(i.uv,6), Correct(i.uv,7)); }
 ENDCG
 }
 }
}

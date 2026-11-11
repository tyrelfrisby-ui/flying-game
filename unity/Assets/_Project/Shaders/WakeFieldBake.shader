// Aero.Wake.WakeField.WriteToTexture: the wake's air on one horizontal sheet, evaluated per texel on the GPU from the
// segments the CPU picked for the region (docs/WAKE-VORTEX.md). The same regularised Biot-Savart law and
// Burnham-Hallock core as FlyingGame.Core.Aero.Wake.WakeVortex.SegmentVelocity, in Unity's axes (the CPU passes
// gamma already negated for the left-handed frame), positions relative to the region centre for float precision.
// WriteVolumeToTexture: the same for several sheets at once, tiled in one target (_WakeVolume).
// Output (lane B's CloudWispAdvect packing): R,G = world X/Z velocity m/s, B = vorticity magnitude 1/s, A = world Y
// velocity m/s. Linear ARGBHalf. Force-included via BuildScript.EnsureAlwaysIncludedShaders.
Shader "Hidden/FlyingGame/WakeFieldBake"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_SEG 64
            #define MAX_JET 16
            float4 _WakeSegA[MAX_SEG];   // a.xyz (relative to the region centre), gamma (m^2/s, Unity-handed)
            float4 _WakeSegB[MAX_SEG];   // b.xyz, core radius (m)
            float4 _WakeJetA[MAX_JET];   // a.xyz, excess speed (m/s)
            float4 _WakeJetB[MAX_JET];   // b.xyz, half-width (m)
            float4 _WakeJetD[MAX_JET];   // blow direction (unit, Unity), swirl at the half-width (m/s, + clockwise from behind)
            int _WakeSegCount;
            int _WakeJetCount;
            float4 _WakeRegion;          // half-size x, half-size z, 0, 0
            float4 _WakeVolume;          // tiles x, tiles y (0: one sheet), lowest slice y, slice spacing (m, rel. centre)

            float4 frag (v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float y = 0;
                if (_WakeVolume.x > 0)
                {
                    // WriteVolumeToTexture: an atlas of horizontal sheets, slice k = tile (k % tiles x, k / tiles x).
                    float2 cell = floor(uv * _WakeVolume.xy);
                    uv = uv * _WakeVolume.xy - cell;
                    y = _WakeVolume.z + (cell.y * _WakeVolume.x + cell.x) * _WakeVolume.w;
                }
                float3 p = float3((uv.x * 2 - 1) * _WakeRegion.x, y, (uv.y * 2 - 1) * _WakeRegion.y);
                float3 vel = 0;
                float3 vort = 0;
                for (int k = 0; k < MAX_SEG; k++)
                {
                    if (k >= _WakeSegCount) break;
                    float3 a = _WakeSegA[k].xyz, b = _WakeSegB[k].xyz;
                    float gam = _WakeSegA[k].w, rc = _WakeSegB[k].w;
                    float3 r0 = b - a, r1 = p - a, r2 = p - b;
                    float l1 = length(r1), l2 = length(r2), l0sq = dot(r0, r0);
                    float3 c = cross(r1, r2);
                    float den = dot(c, c) + rc * rc * l0sq + 1e-6;
                    float kk = gam / (4 * UNITY_PI) * dot(r0, r1 / max(l1, 1e-4) - r2 / max(l2, 1e-4)) / den;
                    vel += c * kk;
                    // Vorticity: gamma/pi * rc^2/(h^2+rc^2)^2 along the segment, times the share it subtends.
                    float len = sqrt(l0sq) + 1e-6;
                    float3 t = r0 / len;
                    float s = dot(r1, t);
                    float h2 = max(dot(r1, r1) - s * s, 0);
                    float d = h2 + rc * rc;
                    float share = 0.5 * (s / sqrt(s * s + d) - (s - len) / sqrt((s - len) * (s - len) + d));
                    vort += t * (gam / UNITY_PI * rc * rc / (d * d) * share);
                }
                for (int j = 0; j < MAX_JET; j++)
                {
                    if (j >= _WakeJetCount) break;
                    float3 a = _WakeJetA[j].xyz, b = _WakeJetB[j].xyz, dir = _WakeJetD[j].xyz;
                    float3 ab = b - a;
                    float l2 = dot(ab, ab) + 1e-6;
                    float u = dot(p - a, ab) / l2;
                    if (u < 0 || u >= 1) continue;
                    float3 off = p - (a + ab * u);
                    float r = max(_WakeJetB[j].w, 1e-3);
                    float rho2 = dot(off, off) / (r * r);
                    if (rho2 > 9) continue;
                    vel += dir * (_WakeJetA[j].w * exp(-rho2));
                    float rho = sqrt(rho2);
                    // Unity is left-handed: the prop's clockwise-from-behind turn is cross(off, -dir) here.
                    float3 tang = cross(off, -dir) / max(rho * r, 1e-4);
                    vel += tang * (_WakeJetD[j].w * rho * exp(0.5 * (1 - rho2)));
                }
                return float4(vel.x, vel.z, length(vort), vel.y);
            }
            ENDCG
        }
    }
}

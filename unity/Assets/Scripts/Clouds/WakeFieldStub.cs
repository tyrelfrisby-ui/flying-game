// Integration: remove this file OR define CLOUDS_WAKEFIELD_LIVE when lane A supplies Aero.Wake.
#if !CLOUDS_WAKEFIELD_LIVE
using System.Collections.Generic;
using UnityEngine;
namespace Aero.Wake
{
    public struct WakeSegment { public Vector3 a, b; public float gamma, coreRadius, age; }
    public static class WakeField
    {
        public static Vector3 SampleVelocity(Vector3 worldPos) => Vector3.zero;
        public static float SampleVorticityMagnitude(Vector3 worldPos) => 0f;
        public static int GetActiveSegments(List<WakeSegment> output) { output.Clear(); return 0; }
        // RG = signed world X/Z velocity (m/s), B = vorticity magnitude (1/s), A = Y velocity.
        public static void WriteToTexture(RenderTexture rt, Bounds region)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }
    }
}
#endif

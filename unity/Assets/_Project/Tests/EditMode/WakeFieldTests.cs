using System.Collections.Generic;
using Aero.Wake;
using FlyingGame.Core;
using FlyingGame.Core.Aero.Wake;
using FlyingGame.Core.MathTypes;
using NUnit.Framework;
using UnityEngine;

namespace FlyingGame.EditModeTests
{
    /// <summary>
    /// The Unity side of the wake (docs/WAKE-VORTEX.md): Aero.Wake.WakeField answers in Unity's left-handed y-up frame
    /// what the shared Core model computes in the sim's right-handed NED frame. The physics itself is tested headless
    /// (tools/FlightTests/WakeVortexTests); these hold the frame change: a level pair's downwash points DOWN in Unity,
    /// the segments' gamma (negated for the handedness) reproduce the sampled velocity with Vector3.Cross, and no lift
    /// leaves still air. Run: Unity -batchmode -runTests -testPlatform EditMode.
    /// </summary>
    public sealed class WakeFieldTests
    {
        private const double Alt = 1000.0;

        [TearDown] public void Clean() { VortexWake.Clear(); }

        /// <summary>A 900 kg, 11 m aeroplane flown north, level, at 30 m/s and load factor <paramref name="nz"/> for 6 s.</summary>
        private static Vec3 Fly(double nz)
        {
            VortexWake.Clear();
            VortexWake.GroundOverride = (_, _) => 0.0;
            WakeSource s = VortexWake.CreateSource("t", 11.0);
            Vec3 p = new(0, 0, -Alt);
            for (int i = 0; i < 300; i++)
            {
                p += new Vec3(30 * 0.02, 0, 0);
                s.ShedFromLoadFactor(0.02, p, Quat.Identity, new Vec3(30, 0, 0), nz, 900 * Atmosphere.GravityMs2, 1.11);
                VortexWake.Advance(0.02);
            }
            return p;
        }

        [Test]
        public void LevelPairPushesTheAirDownBetweenItsLinesInUnity()
        {
            Vec3 p = Fly(1.0);
            Vector3 between = FlyingGame.Bridge.CoordinateMap.ToUnity(p + new Vec3(-90, 0, 0));
            Vector3 v = WakeField.SampleVelocity(between);
            Assert.Less(v.y, -0.5f, $"downwash between the lines should point down in Unity: {v}");
            // Outboard of the right line (Unity +x is east, the right wing flying north): upwash.
            Vector3 outboard = FlyingGame.Bridge.CoordinateMap.ToUnity(p + new Vec3(-90, 7.5, 0));
            Assert.Greater(WakeField.SampleVelocity(outboard).y, 0.1f);
            Assert.Greater(WakeField.SampleVorticityMagnitude(FlyingGame.Bridge.CoordinateMap.ToUnity(p + new Vec3(-90, 4.32, 0))), 1f);
        }

        [Test]
        public void SegmentsReproduceTheFieldWithUnitysCrossProduct()
        {
            Vec3 p = Fly(1.0);
            var segs = new List<WakeSegment>();
            Assert.Greater(WakeField.GetActiveSegments(segs), 10);
            Vector3 q = FlyingGame.Bridge.CoordinateMap.ToUnity(p + new Vec3(-60, 2.0, -1.0));
            Vector3 sum = Vector3.zero;
            foreach (WakeSegment s in segs)
            {
                Vector3 r0 = s.b - s.a, r1 = q - s.a, r2 = q - s.b;
                Vector3 c = Vector3.Cross(r1, r2);
                float den = c.sqrMagnitude + s.coreRadius * s.coreRadius * r0.sqrMagnitude;
                if (den < 1e-9f || r1.magnitude < 1e-6f || r2.magnitude < 1e-6f) continue;
                sum += c * (s.gamma / (4f * Mathf.PI) * Vector3.Dot(r0, r1 / r1.magnitude - r2 / r2.magnitude) / den);
            }
            Vector3 sampled = WakeField.SampleVelocity(q);
            Assert.Less((sum - sampled).magnitude, 0.02f * Mathf.Max(0.1f, sampled.magnitude) + 0.01f, $"Unity Biot-Savart {sum} vs field {sampled}");
        }

        [Test]
        public void NoLiftLeavesStillAir()
        {
            Vec3 p = Fly(0.0);
            Assert.AreEqual(0, WakeField.GetActiveSegments(new List<WakeSegment>()));
            Assert.AreEqual(0f, WakeField.SampleVelocity(FlyingGame.Bridge.CoordinateMap.ToUnity(p + new Vec3(-90, 0, 0))).magnitude, 1e-6f);
        }
    }
}

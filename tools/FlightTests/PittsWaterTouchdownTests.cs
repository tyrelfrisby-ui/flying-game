using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 08:18 (iPad mini, 2026.1105.0804): "I crashed the Pitts on the water, it did not hydroplane at all but I
/// had some sink rate." Nothing in the model was wrong; these hold what it does, measured on 2026-11-05 with the same
/// pilot as <see cref="WaterSkiTests"/> (a held attitude, idle, deep flat water). The Pitts' 21.4 psi tyres plane above
/// V_p = 41.6 kt (r 0.170 m, b 0.129 m, 3,243 N static on each main).
///
/// Sweep, held 6°, 6 s: 70 and 80 kt ski at every sink rate from 0.5 to 3 m/s (peak decel 0.2-1.7 g, tyres 3-22 cm
/// deep, nothing broken); 60 kt skis at 0.5-1 m/s, at 2 m/s it skis for 5 s, slows through the dig-in speed and goes
/// over at 8 m/s, and at 3 m/s the tyres go in 1.6 m (to the wings), it goes over in 0.45 s and breaks the nose and the
/// right wing on WATER LOADS over their ultimates (28.0 kN against 26.1; 38.9 kN·m against 34.7). 50 kt goes over at
/// every sink rate. With the stick left where the glide trim put it (no flare), even 70-80 kt at 0.5 m/s skips off,
/// comes down nose-level at ~59 kt with ~2 m/s of sink and goes over within 0.3 s. That is the real case for a
/// fixed-gear landplane in water (AC 61-21/ditching advice: expect to nose over), and a re-touch with sink and the nose
/// down is the likeliest reading of Ty's report. Whether the tyres plane is decided by WheelHydro alone; nothing here
/// knows the aircraft type.
/// </summary>
[Collection("WorldTerrainActive")]
public class PittsWaterTouchdownTests
{
    private readonly ITestOutputHelper _out;
    public PittsWaterTouchdownTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Pitts() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));

    /// <summary>Flared to 6° at 70 kt, a firm 3 m/s arrival still skis: the tyres stay near the surface and nothing breaks.</summary>
    [Fact]
    public void AFlaredTouchAt70KnotsSkisEvenWithThreeMetresASecondOfSink()
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            var r = WheelWaterTouch.Run(Pitts(), 3.0, 6.0, speed: 70 * 0.514444, holdPitchDeg: 6, startAtHoldPitch: true);
            _out.WriteLine(r.ToString());
            Assert.True(r.NoseOverAt < 0, $"over at {r.NoseOverAt:F2} s");
            Assert.True(r.PeakDecelG < 2.5, $"peak decel {r.PeakDecelG:F2} g");
            Assert.True(r.PeakImmersionM.Take(2).All(d => d < 0.34), "a main tyre went past its own diameter: " + string.Join("/", r.PeakImmersionM));
            Assert.True(r.Lost.Count == 0 && r.Damage.Count == 0, string.Join("; ", r.Damage));
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>At 60 kt (1.44 V_p) the same 3 m/s drives the tyres under: it goes over at once, and whatever breaks
    /// breaks on a logged water load over its ultimate.</summary>
    [Fact]
    public void ThreeMetresASecondAt60KnotsDigsInAndGoesOver()
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            var r = WheelWaterTouch.Run(Pitts(), 3.0, 6.0, speed: 60 * 0.514444, holdPitchDeg: 6, startAtHoldPitch: true);
            _out.WriteLine(r.ToString());
            Assert.InRange(r.NoseOverAt, 0.0, 1.5);
            Assert.True(r.PeakImmersionM.Take(2).Max() > 0.34, "the mains should be under: " + string.Join("/", r.PeakImmersionM));
            foreach (string d in r.Damage.Where(d => d.Contains("broken off")))
                Assert.Matches(@"\d+\.\d kN(·m)? .*against \d+\.\d", d);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The launch argument <c>-crashtest water</c> (CrashScenario.PittsWaterStart) puts the Pitts over the real
    /// island's first float lane, tyres 10 cm up, and the first second of the flight puts them in it.</summary>
    [Fact]
    public void TheWaterCrashTestStartsOverTheLaneAndTouchesItsWater()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig c = Pitts();
            RigidBodyState s0 = CrashScenario.PittsWaterStart(c);
            double? w = WorldTerrain.Active?.WaterSurfaceAt(s0.Position.X, s0.Position.Y, -s0.Position.Z);
            Assert.True(w.HasValue, "no water under the scenario's start");
            _out.WriteLine($"start ({s0.Position.X:F0}, {s0.Position.Y:F0}), CG {-s0.Position.Z:F2} m, water {w:F2} m, ground {WorldTerrain.GroundHeightAt(s0.Position.X, s0.Position.Y):F2} m");
            Assert.True(w.Value - WorldTerrain.GroundHeightAt(s0.Position.X, s0.Position.Y) > 2.0, "the lake is too shallow there");
            Assert.Equal("water", CrashScenario.FromArgs(new[] { "app", "-crashtest", "water" }));
            Assert.Equal("runway", CrashScenario.FromArgs(new[] { "app", "-crashtest" }));
            var ac = new FlyingGame.Sim.Aircraft(c, s0, FlyingGame.Core.Aero.ControlDeflections.Neutral);
            var sim = new FlyingGame.Sim.SimLoop(ac);
            bool wet = false;
            for (int i = 0; i < 20 && !wet; i++)
            {
                sim.RunFor(0.05, new FlyingGame.Sim.ControlInputs(0, 0, 0, 1.0));
                foreach (ContactPoint cp in ac.ContactPoints) if (cp.SurfaceCode == ContactPoint.SurfaceWater && cp.DepthM > 0) wet = true;
                foreach (GearConfig g in c.Gear)
                {
                    var p = ac.State.Position + ac.State.Attitude.Rotate(g.PosVec() - c.Mass.CgVec());
                    if (-p.Z < w.Value) wet = true;
                }
            }
            Assert.True(wet, "never reached the water in 1 s");
        }
        finally { WorldTerrain.Active = null; }
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04 20:28: the J-3's water rudder, retracted and deployed, from the blade's own wetted area, angle and the
/// water it actually sits in, the float's wake included, never a factor per state. Retracted (hung on the hook) the
/// blade stands up behind the float's heel: only its lowest few centimetres stay wet, and those are in the hull's
/// boundary layer, so it still steers, weakly. Lowered, most of it hangs below that layer and it has full authority.
///
/// The wake is the hull's turbulent boundary layer shed off the heel: δ = 0.37·L·Re_L^−0.2 and u/U = (d/δ)^(1/7)
/// (Schlichting, Boundary-Layer Theory ch. 21). The literals are worked by hand from those two formulas.
/// </summary>
[Collection("WorldTerrainActive")]
public class J3WaterRudderWakeTests
{
    private const string Id = "j3-cub-floats-like";
    private readonly ITestOutputHelper _out;
    public J3WaterRudderWakeTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", Id + ".json"));

    private static Aircraft Afloat(AircraftConfig cfg)
    {
        var state = new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
        new SimLoop(ac).RunFor(15.0, new ControlInputs(0, 0, 0, 1.0));
        return ac;
    }

    private static RigidBodyState Moving(RigidBodyState rest, Vec3 vBody) => new(rest.Position, rest.Attitude, vBody, Vec3.Zero);

    /// <summary>Full-left to full-right pedal: the swing in yaw moment (body z, N·m) the blade gives at this pose.</summary>
    private static double Swing(AircraftConfig cfg, RigidBodyState m, double up01)
    {
        (_, Vec3 mL) = FloatHydro.Compute(cfg, m, -1.0, up01);
        (_, Vec3 mR) = FloatHydro.Compute(cfg, m, 1.0, up01);
        return m.Attitude.Conjugate().Rotate(mR - mL).Z;
    }

    /// <summary>The two formulas, against numbers worked by hand: a 4 m hull at 3 m/s, Re = 1.2e7,
    /// δ = 0.37·4·(1.2e7)^−0.2 = 0.05681 m; 1 cm from the skin u/U = (0.01/0.05681)^(1/7) = 0.7802, 3 cm 0.9128.</summary>
    [Fact]
    public void TheBoundaryLayerIsTheTurbulentFlatPlates()
    {
        double d = FloatHydro.HullBoundaryLayerM(4.0, 3.0);
        Assert.Equal(0.05681, d, 5);
        Assert.Equal(0.7802, FloatHydro.WakeSpeedRatio(0.01, d), 4);
        Assert.Equal(0.9128, FloatHydro.WakeSpeedRatio(0.03, d), 4);
        Assert.Equal(1.0, FloatHydro.WakeSpeedRatio(0.06, d));
        Assert.Equal(0.0, FloatHydro.WakeSpeedRatio(0.0, d));
    }

    /// <summary>At rest attitude, 3 m/s straight ahead. Hooked, the wet tip is behind the heel in slower water than any
    /// part of the lowered blade sees; it still steers, a small fraction of the lowered blade's authority. Lowered it is
    /// nearly in open water.</summary>
    [Fact]
    public void HookedTheWetTipIsInTheHeelsWakeAndStillSteersWeakly()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            Aircraft ac = Afloat(cfg);
            RigidBodyState m = Moving(ac.State, new Vec3(3.0, 0, 0));
            FloatHydro.Compute(cfg, m, 1.0, 0.0); FloatHydro.WaterRudderReport down = FloatHydro.LastWaterRudder;
            FloatHydro.Compute(cfg, m, 1.0, 1.0); FloatHydro.WaterRudderReport up = FloatHydro.LastWaterRudder;
            double sDown = Swing(cfg, m, 0.0), sUp = Swing(cfg, m, 1.0);
            foreach ((string what, FloatHydro.WaterRudderReport r) in new[] { ("DOWN", down), ("UP", up) })
                _out.WriteLine($"{what}: wet {r.ImmersedAreaM2:F5} m², δ {r.BoundaryLayerM * 100:F1} cm, mean u/U {r.WakeSpeedRatio:F3}, q̄ {r.DynamicPressurePa:F0} Pa of ½ρV² {r.FreeStreamPressurePa:F0}, lift {r.LiftN:F1} N");
            _out.WriteLine($"pedal swing of yaw moment: down {sDown:F0} N·m, up {sUp:F0} N·m ({sUp / sDown * 100:F1} %)");

            Assert.InRange(down.BoundaryLayerM, 0.03, 0.08);                    // a 4 m hull at 3 m/s: ~6 cm
            Assert.True(up.WakeSpeedRatio < down.WakeSpeedRatio, $"up u/U {up.WakeSpeedRatio:F3}, down {down.WakeSpeedRatio:F3}");
            Assert.True(up.DynamicPressurePa / up.FreeStreamPressurePa < down.DynamicPressurePa / down.FreeStreamPressurePa);
            Assert.True(up.WakeSpeedRatio < 0.95, $"hooked, the wet tip sits behind the heel: u/U {up.WakeSpeedRatio:F3}");
            Assert.True(down.WakeSpeedRatio > 0.95, $"lowered, most of the blade is below the layer: u/U {down.WakeSpeedRatio:F3}");
            Assert.True(sUp > 0.01 * sDown, $"hooked it still steers: {sUp:F0} N·m against {sDown:F0}");
            Assert.True(sUp < 0.25 * sDown, $"but weakly: {sUp:F0} N·m against {sDown:F0}");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The handle's travel between the stops (the 0.8 s slew, or a VR hand holding it part way) is a geometry,
    /// not a blend of two states: the wetted area and the pedal authority run from the lowered blade's down to the hooked
    /// blade's, and never outside them.</summary>
    [Fact]
    public void BetweenTheStopsItIsTheBladesGeometryNotABlend()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            Aircraft ac = Afloat(cfg);
            RigidBodyState m = Moving(ac.State, new Vec3(3.0, 0, 0));
            double[] ups = { 0.0, 0.25, 0.5, 0.75, 1.0 };
            var wet = new double[ups.Length]; var sw = new double[ups.Length];
            for (int i = 0; i < ups.Length; i++)
            {
                FloatHydro.Compute(cfg, m, 1.0, ups[i]); wet[i] = FloatHydro.LastWaterRudder.ImmersedAreaM2;
                sw[i] = Swing(cfg, m, ups[i]);
                _out.WriteLine($"up {ups[i]:F2}: wet {wet[i]:F5} m², u/U {FloatHydro.LastWaterRudder.WakeSpeedRatio:F3}, swing {sw[i]:F0} N·m");
            }
            for (int i = 1; i < ups.Length; i++) Assert.True(wet[i] <= wet[i - 1] + 1e-9, $"wet area rose from {wet[i - 1]:F5} to {wet[i]:F5} at {ups[i]}");
            for (int i = 1; i < ups.Length - 1; i++) Assert.InRange(sw[i], sw[^1], sw[0]);
            // Not linear between the stops: the wet area follows the blade swinging through the surface.
            double mid = 0.5 * (sw[0] + sw[^1]);
            Assert.True(System.Math.Abs(sw[2] - mid) > 0.05 * (sw[0] - sw[^1]), "the half-way blade is exactly the average of the two stops");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The shadow is cast along the stream past the heel, not along the float: drifting beam-on or going astern
    /// the blade is out of the heel's wake (u/U = 1). Full right pedal, hooked: the blade swung across the heel's V bottom.</summary>
    [Fact]
    public void TheWakeLiesDownstreamOfTheHeel()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            Aircraft ac = Afloat(cfg);
            FloatHydro.Compute(cfg, Moving(ac.State, new Vec3(3.0, 0, 0)), 1.0, 1.0);
            double ahead = FloatHydro.LastWaterRudder.WakeSpeedRatio;
            FloatHydro.Compute(cfg, Moving(ac.State, new Vec3(0.0, 1.5, 0)), 1.0, 1.0);
            double beam = FloatHydro.LastWaterRudder.WakeSpeedRatio;
            FloatHydro.Compute(cfg, Moving(ac.State, new Vec3(-1.5, 0, 0)), 1.0, 1.0);
            double astern = FloatHydro.LastWaterRudder.WakeSpeedRatio;
            _out.WriteLine($"hooked, mean u/U: ahead 3 m/s {ahead:F3}, beam-on drift {beam:F3}, astern {astern:F3}");
            Assert.True(ahead < 0.95);
            Assert.Equal(1.0, beam, 9);
            Assert.Equal(1.0, astern, 9);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}

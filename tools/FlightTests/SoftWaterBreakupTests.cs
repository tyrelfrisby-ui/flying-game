using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 13:56: "Cub water-skied to a very soft nose-over; ailerons fell off — not nearly hard enough for that."
/// 13:57: "in general water-based damage is too severe — water has much more give than ground, especially at low
/// speed; soft water impacts should stay intact."
///
/// What did it: not the nose-over. Seconds after it, the J-3 floating inverted at 0.2–0.5 m/s and rocking at up to
/// 43°/s, a wing rolling flat onto the water was charged the whole von Kármán slam of its ω × r by
/// WaterSlam.ElementForce (the joint and panel loads), while HydroBody.Correct gave the ROTATION no added mass at all —
/// the airframe never felt the slam its wing was loaded with. One step read 67 kN·m on the 28 kN·m wing root, and the
/// wing, its outer panel and both ailerons came off. HydroBody now carries the entrained water's inertia in the rotations
/// too (as FloatHydro always has for the floats), so the slam that loads the wing also slows the roll that drove it.
///
/// Before (acaf8ed2) → after, J-3 42 kt water-ski, 0.5 m/s touch, idle-to-30 % power, 15 s: 2° held at 20 % power lost
/// WingRightOuter, AileronRight, WingLeft (67.0 kN·m against 28.0), WingLeftOuter, AileronLeft → nothing. J-3 arriving at
/// 15 m/s, 1.5 m/s sink, 20° bank lost both wings and ailerons (WingLeft 124.8 kN·m against 28.0) → nothing. Hard arrivals
/// still break: Extra 40 m/s / 6 m/s / 30° still loses the right outer wing (WaterBreakupTests), the J-3 at 25 m/s /
/// 4 m/s / 30° still loses its right wing.
/// </summary>
[Collection("WorldTerrainActive")]
public class SoftWaterBreakupTests
{
    private readonly ITestOutputHelper _o;
    public SoftWaterBreakupTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static readonly AirframeComponent[] Structure =
    {
        AirframeComponent.WingLeft, AirframeComponent.WingRight, AirframeComponent.WingLeftInner, AirframeComponent.WingRightInner,
        AirframeComponent.WingLeftOuter, AirframeComponent.WingRightOuter, AirframeComponent.AileronLeft, AirframeComponent.AileronRight,
        AirframeComponent.WingTipLeft, AirframeComponent.WingTipRight, AirframeComponent.TailBoom, AirframeComponent.Nose,
    };

    /// <summary>Ty's case and its neighbours: the J-3 on wheels water-skiing at 42 kt, held at an attitude, at a power,
    /// until it slows, digs in and goes over (or settles on its nose). Nothing of the structure may come off.</summary>
    [Theory]
    [InlineData(2.0, 0.2)]
    [InlineData(2.0, 0.0)]
    [InlineData(10.0, 0.0)]
    [InlineData(5.0, 0.1)]
    [InlineData(0.0, 0.3)]
    public void AJ3WaterSkiNoseOverKeepsItsWingsAndAilerons(double holdPitchDeg, double throttle01)
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            var r = WheelWaterTouch.Run(Load("j3-cub-like"), 0.5, 15.0, speed: 42 * 0.514444, holdPitchDeg: holdPitchDeg, throttle01: throttle01);
            _o.WriteLine(r.ToString());
            // It is the scenario: it went nose-down onto the water, prop in, and came to a stop.
            Assert.True(r.MinPitchDeg < -15, $"pitch only went to {r.MinPitchDeg:F1}");
            Assert.True(r.PropWaterDepthM > 0.5, $"prop {r.PropWaterDepthM:F2} m in the water");
            Assert.True(r.MinSpeed < 1.0, $"Vmin {r.MinSpeed:F1}");
            foreach (AirframeComponent c in Structure) Assert.DoesNotContain(c, r.Lost);
            Assert.DoesNotContain(r.Damage, d => d.Contains("broken off in the water"));
            Assert.False(r.WingsFailed);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    private static Aircraft Arrive(AircraftConfig c, double v, double sinkMs, double bankDeg)
    {
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 10, flapFraction: 0.0, idleProp: true);
        double alpha = t.Converged ? t.AlphaRad : 4 / 57.3, gam = Math.Asin(sinkMs / v), th = alpha - gam, ph = bankDeg * Math.PI / 180;
        Quat q = Quat.Multiply(new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2)), new Quat(Math.Sin(ph / 2), 0, 0, Math.Cos(ph / 2)));
        Vec3 cg = c.Mass.CgVec();
        double low = double.NegativeInfinity;
        foreach (GearConfig g in c.Gear) low = Math.Max(low, q.Rotate(g.PosVec() - cg).Z);
        foreach (ContactPoint p in AirframeContact.BuildPoints(c)) if (!p.PropDisc) low = Math.Max(low, q.Rotate(p.Body - cg).Z);
        Vec3 vw = new(v * Math.Cos(gam), 0, v * Math.Sin(gam));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(3.0 + low + 0.03)), q, q.Conjugate().Rotate(vw), Vec3.Zero),
            new ControlDeflections(0, t.Converged ? t.ElevatorRad : 0, 0, 0, 1.0));
        var idle = new ControlInputs(0, 0, 0, 1);
        for (int i = 0; i < 2000; i++) ac.Step(idle, 0.005);   // 10 s: the arrival, the roll-over and the settling
        return ac;
    }

    private string Report(Aircraft ac) =>
        $"lost [{string.Join(",", ac.LostComponents)}]; {string.Join("; ", ac.Hydro.DamageLog.Where(d => d.Contains("broken off")))}";

    /// <summary>Soft, low-speed arrivals on water — a J-3 stalled on at 29 kt, level or a wing low, sinking 1–2 m/s: it
    /// may nose over and float upside down, but nothing of the structure comes off.</summary>
    [Theory]
    [InlineData(15.0, 1.0, 0.0)]
    [InlineData(15.0, 2.0, 0.0)]
    [InlineData(15.0, 1.5, 20.0)]
    [InlineData(20.0, 2.0, 15.0)]
    public void ASoftLowSpeedArrivalOnWaterStaysIntact(double v, double sinkMs, double bankDeg)
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            Aircraft ac = Arrive(Load("j3-cub-like"), v, sinkMs, bankDeg);
            _o.WriteLine(Report(ac));
            foreach (AirframeComponent c in Structure) Assert.False(ac.IsLost(c), $"{c}: {Report(ac)}");
            Assert.False(ac.Structure.WingsFailed);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>And a hard one still breaks: the J-3 at 49 kt, 4 m/s of sink and 30° of bank puts its right wing into
    /// the water and loses it, through the water's logged load over the joint's ultimate (never because water was touched).</summary>
    [Fact]
    public void AHardWingFirstArrivalOnWaterStillBreaksTheWing()
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            Aircraft ac = Arrive(Load("j3-cub-like"), 25.0, 4.0, 30.0);
            _o.WriteLine(Report(ac));
            Assert.True(ac.IsLost(AirframeComponent.WingRight) || ac.IsLost(AirframeComponent.WingRightOuter), Report(ac));
            Assert.Contains(ac.Hydro.DamageLog, d => d.StartsWith("WingRight") && d.Contains("broken off in the water"));
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The mechanism, by hand: a body of 10 kg·m² turning at 1 rad/s that takes up 10 kg·m² of entrained water in
    /// one step shares its angular momentum with it, 10·1 / (10 + 10) = 0.5 rad/s; water already moving with it changes
    /// nothing; water shed keeps its momentum (the body keeps its own rate); with none, the old update exactly.</summary>
    [Fact]
    public void TheRotationsCarryTheEntrainedWater()
    {
        var s = new RigidBodyState(Vec3.Zero, new Quat(0, 0, 0, 1), Vec3.Zero, new Vec3(1, 0, 0));
        WaterCoeffs none = WaterCoeffs.None, wet = new(new Vec3(1, 1, 1), Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, new Vec3(10, 10, 10));
        var inertia = new Vec3(10, 10, 10);
        Assert.Equal(0.5, HydroBody.Correct(s, s, 100, inertia, none, wet, 0.005).Rates.X, 12);
        Assert.Equal(1.0, HydroBody.Correct(s, s, 100, inertia, wet, wet, 0.005).Rates.X, 12);
        Assert.Equal(1.0, HydroBody.Correct(s, s, 100, inertia, wet, none, 0.005).Rates.X, 12);
        var damped = new WaterCoeffs(new Vec3(1, 1, 1), Vec3.Zero, new Vec3(200, 0, 0));
        Assert.Equal(10.0 / (10 + 0.005 * 200), HydroBody.Correct(s, s, 100, inertia, damped, damped, 0.005).Rates.X, 12);
    }
}

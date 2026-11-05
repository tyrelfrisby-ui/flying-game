using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A propeller in the water (Ty 2026-11-05 8:58: "a water strike does not stop the prop it should"; 9:00: "model the prop
/// water strikes accurately they don't have to stop the engine but they can stop the engine i like that and a bent prop
/// is visible and comes with reduced thrust and a vibration"). The J-3 on floats, 0.915 m blades, held with its lowest tip
/// a set depth into flat water 3 m deep. Literals worked by hand (python, 2026-11-05) from the documented formulas, not the code:
///  - flat engine torque P_max/ω_max = 63,400 / 269.65 = 235.1 N·m; the J-3's static rpm 700 + 1875·0.82 = 2,237.5;
///  - steady rotor where 235.1 = 235.1·(ω/234.3)² + Q_water(ω): 5 mm → 1,868 rpm, 4 cm → 682, 10 cm → 359, 15 cm → 268
///    (stops: below half of idle, 350 rpm); at idle (17.4 N·m) 5 mm → 558, 1 cm → 432, 2 cm → 296 (stops);
///  - blade section at 0.75 R: c 0.1098 m, t 8.78 mm, 2025-T6 at 255 MPa, Z = 0.075·c·t² → yield 162.0 N·m; the depth
///    whose tip load reaches it: 0.60 mm at 2,237 rpm, 6.2 mm at idle.
/// </summary>
[Collection("WorldTerrainActive")]
public class PropWaterStrikeTests
{
    private readonly ITestOutputHelper _out;
    public PropWaterStrikeTests(ITestOutputHelper o) { _out = o; }
    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    [Fact]
    public void TheBladeAndTheEngineAreTheHandWorkedOnes()
    {
        Assert.Equal(162.0, Aircraft.BladeYieldMomentNm(0.915), 1);
        Assert.Equal(205.2, Aircraft.BladeYieldMomentNm(0.99), 1);
        Assert.Equal(0.60e-3, Aircraft.WaterBladeYieldDepth(0.915, 2237.5 * Math.PI / 30), 4);
        Assert.Equal(6.2e-3, Aircraft.WaterBladeYieldDepth(0.915, 700 * Math.PI / 30), 4);
    }

    /// <summary>The J-3 held in the air (no water) except for the prop: its lowest tip <paramref name="depth"/> into
    /// water at 0 m, at a throttle, for <paramref name="hold"/> seconds, then lifted clear for 0.3 s.</summary>
    private (Aircraft ac, List<ContactEvent> ev, double rpmDuring) Dip(double depth, double hold, double throttle, double? phaseTimeS = null)
    {
        AircraftConfig c = Load("j3-cub-floats-like");
        WorldTerrain.Active = null;
        double low = AirframeContact.BuildPoints(c).Where(p => p.PropDisc).Max(p => p.Body.Z - c.Mass.CgVec().Z);
        var att = new Quat(0, 0, 0, 1);
        const double surface = 3.0;   // water 3 m up over flat ground at 0: a lake with a bed under it
        var pos = new Vec3(0, 0, -(surface + low - depth));
        var ac = new Aircraft(c, new RigidBodyState(pos + new Vec3(0, 0, -5), att, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
        var sim = new SimLoop(ac);
        var lever = new ControlInputs(0, 0, 0, 1 - 2 * throttle);
        FloatHydro.FlatWaterOverride = surface;
        double rpm = 0;
        try
        {
            ac.State = new RigidBodyState(new Vec3(0, 0, -100), att, Vec3.Zero, Vec3.Zero);
            sim.RunFor(0.5, lever);   // the engine up to its rpm, nowhere near the water
            if (phaseTimeS is { } ts) Atmosphere.SimTimeSec = ts;
            for (double t = 0; t < hold - 1e-9; t += 0.005)
            {
                ac.State = new RigidBodyState(pos, att, Vec3.Zero, Vec3.Zero);
                sim.RunFor(0.005, lever);
                rpm = ac.EngineRpm;
            }
            ac.State = new RigidBodyState(new Vec3(0, 0, -100), att, Vec3.Zero, Vec3.Zero);
            sim.RunFor(0.3, lever);
        }
        finally { FloatHydro.FlatWaterOverride = null; Atmosphere.SimTimeSec = 0; }
        _out.WriteLine($"dip {depth * 100:F1} cm {hold:F3} s throttle {throttle:F1}: rpm during {rpm:F0}, after {ac.EngineRpm:F0}, stopped {ac.EngineStopped}; " +
                       $"blades {ac.PropBladeRadiusM(0, 0):F4} / {ac.PropBladeRadiusM(0, 1):F4} m, thrust x{ac.PropThrustScale():F4}, vibration {ac.PropVibrationG():F3} g at {ac.PropVibrationHz:F1} Hz; " +
                       string.Join(" | ", ev.Where(e => e.Kind is ContactEventKind.PropStrike or ContactEventKind.PropStoppage).Select(e => e.Kind)));
        return (ac, ev, rpm);
    }

    /// <summary>A tip brushing the water for one step (5 ms, a fifth of a turn) at full power, at a dozen different
    /// instants: whichever blade sweeps through the wet arc folds back by about the depth past its 0.6 mm yield
    /// (5 → 4.4 mm), the other stays straight (a fifth of a turn cannot pass both), the engine always runs on, the thrust
    /// is down by the mean of the blades' (R/R₀)^(2/3), and the unequal blades shake the airframe once a revolution.</summary>
    [Fact]
    public void ALightTipStrikeBendsTheBladeAndTheEngineRunsOnRoughly()
    {
        int bentOne = 0;
        for (int i = 0; i < 12; i++)
        {
            (Aircraft ac, List<ContactEvent> ev, _) = Dip(0.005, 0.005, 1.0, 0.5 + i * 0.001);
            Assert.Contains(ev, e => e.Kind == ContactEventKind.PropStrike);
            Assert.DoesNotContain(ev, e => e.Kind == ContactEventKind.PropStoppage);
            Assert.False(ac.EngineStopped);
            Assert.True(ac.EngineRpm > 2000);
            double b0 = 0.915 - ac.PropBladeRadiusM(0, 0), b1 = 0.915 - ac.PropBladeRadiusM(0, 1);
            Assert.Equal(0.0, Math.Min(b0, b1), 6);
            Assert.Equal(0.915, ac.PropRadiusM(), 6);   // the straight blade still sweeps the whole disc
            if (Math.Max(b0, b1) <= 0) continue;
            bentOne++;
            Assert.InRange(Math.Max(b0, b1), 0.0035, 0.0050);
            double expect = 0.5 * (1 + Math.Pow((0.915 - Math.Max(b0, b1)) / 0.915, 2.0 / 3.0));
            Assert.Equal(expect, ac.PropThrustScale(), 9);
            Assert.True(ac.PropThrustScale() < 1.0);
            Assert.True(ac.PropVibrationG() > 0.005, $"vibration {ac.PropVibrationG():F4} g");
            Assert.True(ac.PropBend01() > 0);
        }
        Assert.True(bentOne >= 2, $"only {bentOne} of 12 brushes met a blade");
    }

    /// <summary>A 4 cm dip held at full power: the water's drag slows the rotor to the hand-worked 682 rpm and the engine
    /// keeps turning it (slows, does not stop); both blades fold back.</summary>
    [Fact]
    public void AHeldDipSlowsTheRotorWithoutStoppingIt()
    {
        (Aircraft ac, _, double rpm) = Dip(0.04, 2.0, 1.0);
        Assert.False(ac.EngineStopped);
        Assert.InRange(rpm, 640, 730);
        Assert.True(ac.PropBladeRadiusM(0, 0) < 0.915 - 0.03 && ac.PropBladeRadiusM(0, 1) < 0.915 - 0.03);
        Assert.True(ac.PropThrustScale() < 0.98);
    }

    /// <summary>A deep strike: 15 cm at full power, or 2 cm at idle — the water's torque beats the engine's below half of
    /// idle and the engine stops; 5 mm at idle runs on (558 rpm).</summary>
    [Theory]
    [InlineData(0.15, 1.0, true)]
    [InlineData(0.02, 0.0, true)]
    [InlineData(0.005, 0.0, false)]
    public void ADeepEnoughStrikeStopsTheEngineAndAShallowOneDoesNot(double depth, double throttle, bool stops)
    {
        (Aircraft ac, List<ContactEvent> ev, _) = Dip(depth, 10.0, throttle);   // at idle the rotor settles over ~4 s (2.4 kg·m² on ~0.6 N·m per rad/s)
        Assert.Equal(stops, ac.EngineStopped);
        Assert.Equal(stops, ev.Any(e => e.Kind == ContactEventKind.PropStoppage));
        if (stops) Assert.Equal(0.0, ac.EngineRpm);
        else Assert.True(ac.EngineRpm > 0);
    }
}

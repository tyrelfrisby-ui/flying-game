using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE RPM IS SOLVED, NOT ASSUMED (Phase 1b). A propeller demands the torque its geometry demands; the engine makes
/// the torque its manifold pressure and rpm allow; the rpm settles where they are equal. These are the behaviours
/// that follow, and every one of them is a thing a pilot can check against the aeroplane.
/// </summary>
public class PistonEngineTests
{
    private readonly ITestOutputHelper _out;
    public PistonEngineTests(ITestOutputHelper o) { _out = o; }

    // THE REAL J-3, read from its own config rather than invented. It is the 85 hp C-85 variant (Ty 2026-11-09:
    // "Our j3 is 85hp"), 2575 rpm, 1.83 m, on a Sensenich W72 laminated birch two-blade — all of which the config
    // already carried while an earlier version of this test guessed 65 hp, 2300 rpm and 1.78 m. Read the aeroplane.
    private static FlyingGame.Core.DataContracts.AircraftConfig Cub
    {
        get
        {
            var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
            return FlyingGame.Core.DataContracts.AircraftConfigLoader.LoadFromFile(
                System.IO.Path.Combine(d.FullName, "configs", "aircraft", "j3-cub-like.json"));
        }
    }
    private static BladeElement.Geometry CubProp => new(Cub.Propulsion!.PropBladeCount, 0.5 * Cub.Propulsion.PropDiameterM, 16.0, Cub.Propulsion.ActivityFactor);
    private static double CubPowerW => Cub.Propulsion!.MaxPowerW;
    private static double CubRatedRpm => Cub.Propulsion!.MaxRpm;
    private static double CubIdle => Cub.Propulsion!.IdleRpm;
    /// <summary>The Cub's ENGINE as the torque law needs it — the real C-85's 188 in^3 and 6.3:1, out of the config,
    /// not the fleet fallback. Friction and pumping are mean effective pressures and only become torques through a
    /// swept volume (PistonMep), so a test that invented the displacement would be testing a different engine.</summary>
    private static PistonEngine.Spec CubEngine => PistonEngine.Spec.For(Cub.Propulsion!);

    /// <summary>
    /// A FIXED-PITCH AEROPLANE CANNOT MAKE RATED RPM STANDING STILL. With no forward speed the blade's angle of
    /// attack is high, it absorbs more torque than the engine can give at that rpm, and the engine is dragged down:
    /// real static rpm sits a couple of hundred below redline and climbs as the aeroplane accelerates. That is why a
    /// static run-up is a check rather than a formality, and the old thrust = eta*P/V model could not express it at all.
    /// </summary>
    /// Measured: 2131 rpm standing still against 2300 rated — 93 %, where a J-3's static run-up really sits — rising
    /// to the rated figure as it accelerates. There is a small DIP between 0 and 10 m/s, and it is not a glitch: with
    /// a constant geometric pitch the blade angle at a fifth of the radius is about 47 deg while the inflow angle
    /// there is 23, so the inboard blade is stalled at 24 deg of incidence when the aeroplane is standing still. A
    /// stalled root absorbs LESS torque, so the engine turns slightly faster at rest than at ten knots. Published
    /// torque-coefficient curves show the same kink at very low advance ratio, so the test asserts the real property
    /// — below rated at rest, climbing over the working range — and not the idealised one it first assumed.
    [Fact]
    public void StaticRpmIsBelowRedlineAndClimbsWithSpeed()
    {
        double at0 = 0, prev = 0;
        foreach (double v in new[] { 0.0, 10.0, 20.0, 30.0, 40.0 })
        {
            double rpm = PistonEngine.BalancedRpm(CubProp, CubEngine, CubIdle, 1.0, 1.0, v, 1.225);
            var r = BladeElement.Solve(CubProp, v, 0.0, rpm, 1.225);
            _out.WriteLine($"full throttle at {v,4:F0} m/s: {rpm,6:F0} rpm, {r.ThrustN,6:F0} N, {r.PowerW / 745.7,5:F0} hp absorbed");
            if (v == 0) at0 = rpm;
            else Assert.True(rpm > prev - 1, $"rpm must rise with airspeed once the root is flying: {prev:F0} -> {rpm:F0} at {v:F0} m/s");
            if (v > 0) prev = rpm;
        }
        Assert.InRange(at0, 0.80 * CubRatedRpm, 0.99 * CubRatedRpm);
        double at40 = PistonEngine.BalancedRpm(CubProp, CubEngine, CubIdle, 1.0, 1.0, 40.0, 1.225);
        Assert.True(at40 > at0, $"rpm must be higher in the cruise than standing still: {at0:F0} -> {at40:F0}");
    }

    /// <summary>Closing the throttle drops the rpm; opening it raises it. The engine's torque falls with manifold
    /// pressure, so the balance moves down the propeller's demand curve.</summary>
    [Fact]
    public void ThrottleMovesTheBalance()
    {
        double prev = 0;
        foreach (double t in new[] { 0.2, 0.4, 0.6, 0.8, 1.0 })
        {
            double rpm = PistonEngine.BalancedRpm(CubProp, CubEngine, CubIdle, t, 1.0, 20.0, 1.225);
            _out.WriteLine($"throttle {t:F1}: {rpm:F0} rpm");
            Assert.True(rpm > prev, "more throttle must mean more rpm");
            prev = rpm;
        }
    }

    /// <summary>Altitude costs power through the air the engine can draw, with no separate lapse constant: manifold
    /// pressure is ambient pressure throttled, so a naturally aspirated engine simply gets less of it up high.</summary>
    [Fact]
    public void ItLosesPowerWithAltitudeBecauseItSwallowsLessAir()
    {
        double sea = PistonEngine.PowerW(CubEngine, CubRatedRpm, 1.0, 1.0);
        double tenK = PistonEngine.PowerW(CubEngine, CubRatedRpm, 1.0, 0.688);   // 10 000 ft pressure ratio
        _out.WriteLine($"sea level {sea / 745.7:F0} hp, 10 000 ft {tenK / 745.7:F0} hp ({100 * tenK / sea:F0} %)");
        Assert.InRange(tenK / sea, 0.50, 0.70);     // the classic "about 60 % at ten thousand feet"
    }

    /// <summary>
    /// A CONSTANT-SPEED PROPELLER HOLDS ITS RPM — until it runs out of blade. Between the stops the governor finds a
    /// pitch and the rpm is exactly what was asked for; on a stop it cannot, and the rpm goes where the torque
    /// balance puts it. That is what the stops are for, and it is why they are derived from the owner's two rules
    /// (BladeElement.GovernorStops) rather than picked.
    /// </summary>
    [Fact]
    public void TheGovernorHoldsRpmUntilItRunsOutOfBlade()
    {
        // The Pitts' Hartzell: 260 hp, 2700 rpm, 82 in two-blade, Vne 94.8 m/s.
        const int blades = 2; const double r = 1.0414, af = 110.0, hub = 0.15;
        const double powerW = 194000, rated = 2700, idle = 700, vne = 94.8;
        var stops = BladeElement.GovernorStops(blades, r, af, hub, powerW, rated, vne);
        // The real AEIO-540: 541.5 in^3, six cylinders, 8.7:1 — the engine, not a rating with fleet defaults behind it.
        var engine = new PistonEngine.Spec(powerW, rated, 541.5 * PistonCompression.CubicInchM3, 6, 8.7, -1.0);
        _out.WriteLine($"stops: fine {stops.FineDeg:F1} deg, coarse {stops.CoarseDeg:F1} deg");
        bool heldSomewhere = false, hitAStop = false;
        foreach (double v in new[] { 0.0, 20.0, 40.0, 60.0, 80.0, 104.0, 130.0 })
        {
            var g = PistonEngine.Govern(blades, r, af, hub, stops, rated, engine, idle, 1.0, 1.0, v, 1.225);
            string where = g.OnFineStop ? "FINE stop" : g.OnCoarseStop ? "COARSE stop" : $"governing at {g.PitchDeg:F1} deg";
            _out.WriteLine($"  {v,5:F0} m/s full power: {g.Rpm,6:F0} rpm   {where}");
            if (!g.OnFineStop && !g.OnCoarseStop) { heldSomewhere = true; Assert.Equal(rated, g.Rpm, 3); }
            else hitAStop = true;
        }
        Assert.True(heldSomewhere, "the governor must hold the commanded rpm somewhere in the range");
        Assert.True(hitAStop, "and must run out of blade somewhere in the range");
    }
}

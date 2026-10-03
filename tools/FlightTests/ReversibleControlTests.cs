using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Reversible controls (owner 2026-10-02): released, a cable-controlled surface is FREE and trails to where its hinge
/// moment balances — air load vs. centering spring vs. trim — instead of snapping to neutral. Trace tests: hands-off at
/// the trimmed speed the aircraft holds trim; a pull-and-release returns toward the trimmed speed (stick-free stable);
/// a released rudder in a sideslip trails WITH the flow, less so with a centering spring.
/// </summary>
public class ReversibleControlTests
{
    private readonly ITestOutputHelper _out;
    public ReversibleControlTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static (Aircraft ac, double trimStick, double v) Spawn(AircraftConfig cfg, double alt = 1000)
    {
        double v = cfg.SpawnIasMs > 1 ? cfg.SpawnIasMs : 25;
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(cfg, v, alt);
        Assert.True(t.Converged);
        double h = t.ThetaRad / 2;
        var q = new Quat(0, Math.Sin(h), 0, Math.Cos(h));
        var s = new RigidBodyState(new Vec3(0, 0, -alt), q, new Vec3(v * Math.Cos(t.AlphaRad), 0, v * Math.Sin(t.AlphaRad)), Vec3.Zero);
        return (new Aircraft(cfg, s, new ControlDeflections(0, t.ElevatorRad, 0, 0)), Aircraft.StickForDeflection(t.ElevatorRad, cfg.Controls.Elevator), v);
    }

    private static void MakeReversible(AircraftConfig cfg, double rudderSpringKt)
    {
        foreach (ControlAxisConfig a in new[] { cfg.Controls.Aileron, cfg.Controls.Elevator, cfg.Controls.Rudder }) a.Reversible = true;
        cfg.Controls.Rudder.CenteringSpringKt = rudderSpringKt;
    }

    [Theory]
    [InlineData("glider-2-33-like")]
    [InlineData("glider-swift-s1-like")]
    [InlineData("pa18-cub-like")]
    [InlineData("c172-like")]
    [InlineData("pitts-s2b-like")]
    [InlineData("p51d-like")]
    [InlineData("dc3-like")]
    [InlineData("seminole-like")]
    public void HandsOffHoldsTrimAndReturnsAfterAPull(string id)
    {
        // Each type with its own reversible / spring / trim settings, against the same type stick-fixed.
        (double band, double early, double late) free = Fly(id, true), fixedRun = Fly(id, false);
        _out.WriteLine($"{id}: hands-off speed band {free.band:F2} m/s; after a pull, free {free.early:F1} -> {free.late:F1} m/s, stick-fixed {fixedRun.early:F1} -> {fixedRun.late:F1}");
        AircraftConfig cfg = Load(id);
        Assert.True(free.band < 0.03 * cfg.SpawnIasMs + 0.5, "hands-off at trim should hold the trimmed speed");
        // Gliders: the phugoid is nearly undamped (ζ ≈ 1/(√2·L/D) ≈ 0.03 at L/D 23 — the 2-33 stick-FIXED barely damps), so
        // stick-free it may grow slowly but must not run away. Powered: no worse than stick-fixed beyond a margin.
        if (cfg.Propulsion == null) Assert.True(free.late < 2.0 * free.early, "glider: stick-free phugoid must not run away");
        else Assert.True(free.late <= 1.3 * fixedRun.late + 1.0, "stick-free should settle about as well as stick-fixed");
    }

    private (double band, double early, double late) Fly(string id, bool reversible)
    {
        AircraftConfig cfg = Load(id);
        if (!reversible) foreach (ControlAxisConfig a in new[] { cfg.Controls.Aileron, cfg.Controls.Elevator, cfg.Controls.Rudder }) a.Reversible = false;
        (Aircraft ac, double trim, double v0) = Spawn(cfg, 3000);
        double lever = cfg.Propulsion == null ? -1.0 : 1.0, h = SimLoop.DefaultFixedDtSec;
        double pull = 0.3 * Math.Min(1.0, Math.Pow(25.0 / v0, 2));   // about the same pitch impulse at any trim speed (∝ 1/q)
        double vMin = 1e9, vMax = 0, late = 0, early = 0;
        for (double t = 0; t < 90; t += h)
        {
            bool pulling = t >= 30 && t < 31;
            // Hands off except a 1 s pull at t = 30 s; the wings are held level by the pilot (aileron held).
            (double roll, _, _) = FormationPilot.Euler(ac.State.Attitude);
            double ail = Math.Clamp(-roll * 2.0 - ac.State.Rates.X * 0.3, -1, 1);
            ac.Step(pulling ? new ControlInputs(ail, trim - pull, 0, lever, false, false, true)
                            : new ControlInputs(ail, trim, 0, lever, false, true, true), h);
            double vv = ac.State.Velocity.Length;
            if (t > 5 && t < 30) { vMin = Math.Min(vMin, vv); vMax = Math.Max(vMax, vv); }
            if (t > 32 && t < 50) early = Math.Max(early, Math.Abs(vv - v0));
            if (t > 75) late = Math.Max(late, Math.Abs(vv - v0));
            if (reversible && (int)(t / h) % 1200 == 0)
                _out.WriteLine($"  t={t,4:F0}s V {vv,5:F1} m/s  elevator {ac.CurrentDeflections.ElevatorRad * 57.3,6:F2}°  pitch {FormationPilot.Euler(ac.State.Attitude).pitch * 57.3,6:F1}°{(pulling ? "  (pulling)" : "")}");
        }
        return (vMax - vMin, early, late);
    }

    [Fact]
    public void ReleasedRudderTrailsWithTheFlowLessWithASpring()
    {
        double Float(double springKt)
        {
            AircraftConfig cfg = Load("pa18-cub-like");
            MakeReversible(cfg, springKt);
            (Aircraft ac, double trim, _) = Spawn(cfg);
            double h = SimLoop.DefaultFixedDtSec, maxFloat = 0, sum = 0; int n = 0;
            for (double t = 0; t < 6; t += h)
            {
                bool kick = t < 1.5;
                var inp = kick ? new ControlInputs(0, trim, 0.8, 1.0, false, false, false) : new ControlInputs(0, trim, 0, 1.0, false, false, true);
                ac.Step(inp, h);
                if (!kick && t < 2.5)
                {
                    // Right after release the yaw it built carries on: the fin sees sideslip and the free rudder trails with it.
                    double beta = Math.Atan2(ac.State.Velocity.Y, ac.State.Velocity.X);
                    if (t > 1.8) { sum += Math.Abs(ac.CurrentDeflections.RudderRad); n++; }
                    if ((int)(t / h) % 24 == 0) _out.WriteLine($"spring {springKt} kt  t={t:F2}s β {beta * 57.3,6:F2}°  rudder {ac.CurrentDeflections.RudderRad * 57.3,6:F2}°");
                }
            }
            return sum / Math.Max(1, n);
        }
        double free = Float(0), sprung = Float(70);
        _out.WriteLine($"mean rudder float 0.3-1 s after release: no spring {free * 57.3:F2}°, 70 kt spring {sprung * 57.3:F2}°");
        Assert.True(Math.Abs(free) > 0.5 * Math.PI / 180, "a free rudder should visibly trail in the sideslip");
        Assert.True(Math.Abs(sprung) < Math.Abs(free), "the centering spring should hold it nearer neutral");
    }

    [Fact]
    public void IrreversibleAxisIgnoresRelease()
    {
        AircraftConfig cfg = Load("pa18-cub-like");
        foreach (ControlAxisConfig a in new[] { cfg.Controls.Aileron, cfg.Controls.Elevator, cfg.Controls.Rudder }) a.Reversible = false;   // released = the trim position, as before
        (Aircraft a1, double trim, _) = Spawn(cfg);
        (Aircraft a2, _, _) = Spawn(cfg);
        for (int i = 0; i < 2400; i++)
        {
            a1.Step(new ControlInputs(0, trim, 0, 1.0), SimLoop.DefaultFixedDtSec);
            a2.Step(new ControlInputs(0, trim, 0, 1.0, true, true, true), SimLoop.DefaultFixedDtSec);
        }
        Assert.Equal(a1.State.Position.X, a2.State.Position.X, 6);
    }
    /// <summary>Owner (CFI) 2026-10-02: in a TAILSLIDE the relative wind comes from behind, so a free surface is pushed AWAY
    /// from neutral (its hinge is now downstream) and slams to the stop; the rudder may be held back a little by its
    /// centering springs but still deflects a lot.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("pa18-cub-like")]
    [InlineData("extra-300-like")]
    public void TailslideSlamsFreeControlsToTheStops(string id)
    {
        AircraftConfig cfg = Load(id);
        double h = Math.PI / 4;   // straight up: pitch +90 deg
        var q = new Quat(0, Math.Sin(h), 0, Math.Cos(h));
        // A touch of sideslip / pitch off the vertical (no real tailslide is perfectly clean) and a hair of deflection.
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -1500), q, new Vec3(12, 0.3, 0.4), Vec3.Zero),
            new ControlDeflections(0.01, 0.01, 0.01, 0));
        double lever = cfg.Propulsion == null ? -1.0 : 1.0, step = SimLoop.DefaultFixedDtSec;
        double ail = 0, ele = 0, rud = 0, maxBack = 0;
        for (double t = 0; t < 5; t += step)
        {
            ac.Step(new ControlInputs(0, 0, 0, lever, true, true, true), step);
            double u = ac.State.Velocity.X;   // body-axis: negative = flying backwards
            maxBack = Math.Max(maxBack, -u);
            if (u < -6)
            {
                ControlDeflections d = ac.CurrentDeflections;
                ail = Math.Max(ail, Math.Abs(d.AileronRad) / cfg.Controls.Aileron.MaxDeflRad);
                ele = Math.Max(ele, Math.Abs(d.ElevatorRad) / cfg.Controls.Elevator.MaxDeflRad);
                rud = Math.Max(rud, Math.Abs(d.RudderRad) / cfg.Controls.Rudder.MaxDeflRad);
            }
            if ((int)(t / step) % 60 == 0)
                _out.WriteLine($"t={t:F1}s u {u,6:F1} m/s  ail {ac.CurrentDeflections.AileronRad * 57.3,6:F1}°  ele {ac.CurrentDeflections.ElevatorRad * 57.3,6:F1}°  rud {ac.CurrentDeflections.RudderRad * 57.3,6:F1}°");
        }
        _out.WriteLine($"{id}: slid back to {maxBack:F1} m/s; free surfaces reached aileron {ail:P0}, elevator {ele:P0}, rudder {rud:P0} of travel");
        Assert.True(maxBack > 6, "the tailslide should actually slide back");
        Assert.True(ele > 0.9, "free elevator should slam to the stop");
        Assert.True(ail > 0.9, "free ailerons should slam to the stop");
        Assert.True(rud > 0.5, "free rudder should deflect a lot (springs may hold it back a little)");
    }
}

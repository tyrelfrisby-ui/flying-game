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
// FLIES an aeroplane, so it READS the global ground and weather (WorldTerrain.Active, Atmosphere.*,
// FloatHydro) through the sim even though it sets none of them. The collection was protecting the
// classes that WRITE that state from each other and leaving their victims outside it, so this class ran
// in parallel with 68 that install worlds and weather: StructuralTests went 39 of 40 NaN in one run and
// passed clean in the next on identical code (2026-11-01). A reader needs the same serialisation as a
// writer.
[Collection("WorldTerrainActive")]
public class ReversibleControlTests
{
    private readonly ITestOutputHelper _out;
    public ReversibleControlTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static (Aircraft ac, double trimStick, double v) Spawn(AircraftConfig cfg, double alt = 1000)
    {
        double v = cfg.SpawnIasMs > 1 ? cfg.SpawnIasMs : 25;
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(cfg, v, alt, idleProp: true);   // flown at idle: the idling prop's drag is in the trim
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
    [InlineData("jn4-jenny-like")]   // 1917: cables everywhere, no trim tab, no spring, and nothing balanced
    public void HandsOffHoldsTrimAndReturnsAfterAPull(string id)
    {
        // Each type with its own reversible / spring / trim settings, against the same type stick-fixed.
        Run free = Fly(id, true), held = Fly(id, false);
        _out.WriteLine($"{id}: hands-off speed band {free.Band:F2} m/s; after a pull the longitudinal oscillation grows "
            + $"{free.PerCycle:F3} per cycle stick-FREE ({free.PeriodS:F1} s) against {held.PerCycle:F3} stick-FIXED "
            + $"({held.PeriodS:F1} s); peak excursion {free.Peak:F1} m/s free, {held.Peak:F1} fixed");
        AircraftConfig cfg = Load(id);
        _out.WriteLine($"    peak excursion after the pull: free {free.Early:F1} -> {free.Late:F1} m/s, "
            + $"stick-fixed {held.Early:F1} -> {held.Late:F1}");
        Assert.True(free.Band < 0.03 * cfg.SpawnIasMs + 0.5, "hands-off at trim should hold the trimmed speed");
        if (cfg.Propulsion != null)
        {
            // POWERED: unchanged, and deliberately so. These types' modes are long and heavily damped — the DC-3, the
            // P-51, the Pitts and the Seminole give fewer than four extrema in the 58 s after the pull, so a per-cycle
            // growth fitted to them is a fit to two points and would make the gate vacuous rather than strict. The
            // P-51 is RED here on a pristine tip (free 1.7 against 1.3 x 0.5 + 1.0 = 1.65) and this measurement does
            // not touch that.
            Assert.True(free.Late <= 1.3 * held.Late + 1.0, "stick-free should settle about as well as stick-fixed");
            return;
        }
        // NO GROWTH, hands off. This gate used to permit a glider to grow to 2x per cycle, deriving the licence from
        // ζ ≈ 1/(√2·L/D) ≈ 0.03 — but that derivation gives a DAMPED phugoid (0.03 of critical is 0.83 per cycle), so
        // it never licensed divergence at all, and the 2x it was written as hid the Swift growing 1.35 per cycle until
        // it was flying 54 m/s of amplitude on a 26 m/s trim. Ty flew the 2-33 on 2026-11-10 and reported exactly this
        // as wrong — "The 2-33 has negative dynamic stability about the lateral axis... Ie in pitch". A 2-33 is a
        // docile trainer with a large tail on a long arm; hands off it does not wind itself up.
        Assert.True(free.PerCycle <= 1.0, "hands off, the longitudinal oscillation must not grow");
        // AND THE INSTRUMENT THAT SAYS WHOSE FAULT IT IS: the same aeroplane with the stick held. A free surface may
        // make the mode heavier or lighter, but a surface that pumps a mode the held stick damps is the surface's
        // fault and nothing else's — no coefficient outside the hinge model can tell those two runs apart.
        // FreeElevatorPhugoidProbe has the numbers: on the Swift and the EB29R the floating elevator is the thing
        // HOLDING this mode together (ζ +0.028 and +0.051 free, against −0.000 and −0.013 held), so it is only the
        // 2-33 that fails this way.
        Assert.True(free.PerCycle <= held.PerCycle + 0.02,
            "a free surface must not pump a longitudinal mode that the held stick damps");
    }

    /// <summary>What one hands-off flight measured. PerCycle is the growth of the longitudinal oscillation's
    /// amplitude per cycle: 1.0 is neutral, above 1.0 it is winding up.</summary>
    private readonly record struct Run(double Band, double PerCycle, double PeriodS, double Peak,
                                      double Early, double Late);

    private Run Fly(string id, bool reversible)
    {
        AircraftConfig cfg = Load(id);
        if (!reversible) foreach (ControlAxisConfig a in new[] { cfg.Controls.Aileron, cfg.Controls.Elevator, cfg.Controls.Rudder }) a.Reversible = false;
        (Aircraft ac, double trim, double v0) = Spawn(cfg, 3000);
        double lever = cfg.Propulsion == null ? -1.0 : 1.0, h = SimLoop.DefaultFixedDtSec;
        double pull = 0.3 * Math.Min(1.0, Math.Pow(25.0 / v0, 2));   // about the same pitch impulse at any trim speed (∝ 1/q)
        double vMin = 1e9, vMax = 0, peak = 0, late = 0, early = 0;
        double v0e = v0 * Math.Sqrt(Atmosphere.DensityAtAltitude(3000) / Atmosphere.DensityAtAltitude(0));
        // The amplitude is HALF THE SWING BETWEEN CONSECUTIVE EXTREMA, never |V − V_trim| at an extremum: a glide
        // drifts in equivalent airspeed across a 90 s window and that drift lands in the amplitude. Measured the
        // other way the Cub read 1.24 per cycle on an oscillation of 0.47 m/s whose damping matches the drag theory
        // to 3 %, and the Swift's genuine 1.35 runaway read 0.94 because its first and last peaks straddled the
        // saturation. (FreeElevatorPhugoidProbe, 2026-11-11.)
        var amps = new List<(double t, double amp)>();
        double sPrev = 0, sPrev2 = 0, tPrev = 0; double? lastExt = null;
        for (double t = 0; t < 90; t += h)
        {
            bool pulling = t >= 30 && t < 31;
            // Hands off except a 1 s pull at t = 30 s; the wings are held level by the pilot (aileron held).
            (double roll, _, _) = FormationPilot.Euler(ac.State.Attitude);
            double ail = Math.Clamp(-roll * 2.0 - ac.State.Rates.X * 0.3, -1, 1);
            ac.Step(pulling ? new ControlInputs(ail, trim - pull, 0, lever, false, false, true)
                            : new ControlInputs(ail, trim, 0, lever, false, true, true), h);
            // EQUIVALENT airspeed: trim holds dynamic pressure, not true airspeed — a steep idle glide (the P-51 at −17°)
            // descends into denser air and its TAS falls ~4 % over the window at constant trim.
            double vv = ac.State.Velocity.Length * Math.Sqrt(Atmosphere.DensityAtAltitude(-ac.State.Position.Z) / Atmosphere.DensityAtAltitude(0));
            if (t > 5 && t < 30) { vMin = Math.Min(vMin, vv); vMax = Math.Max(vMax, vv); }
            double sig = vv - v0e;
            if (t > 31.5)
            {
                if (sPrev2 != 0 && ((sPrev > sPrev2 && sPrev > sig) || (sPrev < sPrev2 && sPrev < sig)))
                {
                    if (lastExt is { } le) amps.Add((tPrev, 0.5 * Math.Abs(sPrev - le)));
                    lastExt = sPrev;
                }
                peak = Math.Max(peak, Math.Abs(sig));
            }
            if (t > 32 && t < 50) early = Math.Max(early, Math.Abs(sig));
            if (t > 75) late = Math.Max(late, Math.Abs(sig));
            sPrev2 = sPrev; sPrev = sig; tPrev = t;
            if (reversible && (int)(t / h) % 1200 == 0)
                _out.WriteLine($"  t={t,4:F0}s V {vv,5:F1} m/s  elevator {ac.CurrentDeflections.ElevatorRad * 57.3,6:F2}°  pitch {FormationPilot.Euler(ac.State.Attitude).pitch * 57.3,6:F1}°{(pulling ? "  (pulling)" : "")}");
        }
        // Least squares of ln(amplitude) on time over the half-cycle amplitudes, converted to a per-cycle ratio with
        // the measured period. Fewer than four extrema means there was no oscillation to grow: call it neutral.
        double perCycle = 1.0, period = 0.0;
        if (amps.Count >= 4)
        {
            period = 2.0 * (amps[^1].t - amps[0].t) / (amps.Count - 1);
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < amps.Count; i++)
            {
                double x = amps[i].t - amps[0].t, y = Math.Log(Math.Max(1e-9, amps[i].amp));
                sx += x; sy += y; sxx += x * x; sxy += x * y;
            }
            double den = amps.Count * sxx - sx * sx;
            if (Math.Abs(den) > 1e-12) perCycle = Math.Exp((amps.Count * sxy - sx * sy) / den * period);
        }
        return new Run(vMax - vMin, perCycle, period, peak, early, late);
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

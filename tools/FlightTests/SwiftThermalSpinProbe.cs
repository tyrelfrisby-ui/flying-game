using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (SWIFT_SPIN=1): WILL THE SWIFT SPIN, and if not, which gap stops it? (Ty, Talk Flying Ep4,
/// 2026-10-19 — he wanted a spin out of a thermal filmed, and the Swift as shipped would not hold one.)
/// Three questions, one theory each: does it spin at all on the widget's own recipe; which of the two candidate
/// gaps is responsible; and does the rotation SETTLE or merely thrash. Nothing here sets an attitude or a rate —
/// the only inputs are the stick, the rudder and the air.
///
/// What it measured, with the elevator class corrected (saturation 5.2 -> 16.8 deg) and no rotary table:
///   the Swift departs and reaches alpha 33 +-17 but leaves the stall 11 % of the time and the rate swings
///   +-33 deg/s — an oscillatory departure, not a developed spin. With the TR-379 monoplane table as well it is
///   100 % stalled at alpha 44 +-13, 4.4 s and 397 ft a turn. The Extra, for scale, holds alpha 50 +-0.
/// </summary>
[Collection("WorldTerrainActive")]   // writes the global Atmosphere
public class SwiftThermalSpinProbe
{
    private readonly ITestOutputHelper _out;
    public SwiftThermalSpinProbe(ITestOutputHelper o) { _out = o; }

    /// <summary>Angle of attack from the AIR-relative velocity. Reading it off the ground-relative velocity is
    /// wrong by atan(w/V) inside a thermal — 11 deg in a 6 m/s core at 30 m/s, which hid the stall completely.</summary>
    private static double AlphaDeg(RigidBodyState s)
    {
        Vec3 air = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
        return Math.Atan2(air.Z, air.X) * 57.3;
    }

    private static double Air(RigidBodyState s)
        => (s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position))).Length;

    private static double OmVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }

    /// <summary>The widget's own spin recipe (WidgetPresets "spin-entry"): wings level, power off, full aft stick to the
    /// stall, then full rudder, held. If a type will not hold a spin HERE it will not hold one in the widget either.</summary>
    [Theory]
    [InlineData("glider-swift-s1-like")]
    [InlineData("glider-2-33-like")]
    [InlineData("extra-300-like")]
    public void TheWidgetsOwnSpinRecipe(string id)
    {
        if (System.Environment.GetEnvironmentVariable("SWIFT_SPIN") == null) return;
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        double stallDeg = StallAuthorityTests.StallDeg(cfg);
        double v = ThermalSpawn.MinSinkSpeed(cfg, 1500);
        TrimSolver.Result t0 = TrimSolver.SolveGliderTrim(cfg, v, 1500, idleProp: true);
        var att = new Quat(0, Math.Sin(t0.ThetaRad / 2), 0, Math.Cos(t0.ThetaRad / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -1500), att,
            new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad)), Vec3.Zero),
            new FlyingGame.Core.Aero.ControlDeflections(0, t0.ElevatorRad, 0, 0));
        double h = SimLoop.DefaultFixedDtSec, lever = cfg.Propulsion == null ? -1.0 : 1.0;
        _out.WriteLine($"== {id}: stall {stallDeg:F1} deg, entry {v:F1} m/s");
        // stall it wings level
        double tS = 0;
        for (; tS < 20; tS += h)
        {
            ac.Step(new ControlInputs(0, -1.0, 0, lever), h);
            if (AlphaDeg(ac.State) > stallDeg + 2) break;
        }
        _out.WriteLine($"   stalled after {tS:F1} s at alpha {AlphaDeg(ac.State):F0}, {Air(ac.State):F1} m/s");
        // then full rudder, held, with the widget's altitude wrap so it cannot hit the ground
        double net = 0, stalled = 0, n = 0, aSum = 0, pSum = 0, lost = 0;
        var pro = new ControlInputs(0, -1.0, -1.0, lever);
        for (double t = 0; t < 30; t += h)
        {
            double z0 = ac.State.Position.Z;
            ac.Step(pro, h);
            RigidBodyState s = ac.State;
            lost += s.Position.Z - z0;
            net += OmVert(s) * h;
            double alpha = AlphaDeg(s);
            (_, double pitch, _) = FormationPilot.Euler(s.Attitude);
            if (alpha > 16) stalled++;
            aSum += alpha; pSum += pitch * 57.3; n++;
            if (-s.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 1500), s.Attitude, s.Velocity, s.Rates);
            if (t % 3.0 < h)
                _out.WriteLine($"   t{t,5:F1}  turns {net / (2 * Math.PI),6:F2}  alpha {alpha,5:F0}  pitch {pitch * 57.3,5:F0}  " +
                               $"{Air(s),5:F1} m/s  r {s.Rates.Z * 57.3,5:F0} deg/s");
        }
        double turns = Math.Abs(net) / (2 * Math.PI);
        _out.WriteLine($"   >> {turns:F2} turns in 30 s = {30 / Math.Max(0.01, turns):F1} s/turn, " +
                       $"{lost * 3.2808 / Math.Max(0.01, turns):F0} ft/turn, stalled {stalled / n:P0}, " +
                       $"mean alpha {aSum / n:F0}, mean pitch {pSum / n:F0}");
    }

    /// <summary>WHY the Swift will not spin, as four cases: the two candidate gaps on and off. The rotary table is the
    /// published TR-379 one already in the model (docs/SPIN-ROTARY-DATA.md), wired only to the Extra and the Pitts; the
    /// elevator margin is StallAuthorityTests.TargetMarginDeg, +6 deg for aerobatic types and -0.5 for everyone else.</summary>
    [Theory]
    [InlineData("glider-swift-s1-like", false, false)]
    [InlineData("glider-swift-s1-like", true, false)]
    [InlineData("glider-swift-s1-like", false, true)]
    [InlineData("glider-swift-s1-like", true, true)]
    public void WhichGapStopsTheSpin(string id, bool rotary, bool aerobaticElevator)
    {
        if (System.Environment.GetEnvironmentVariable("SWIFT_SPIN") == null) return;
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        double stallDeg = StallAuthorityTests.StallDeg(cfg);
        if (rotary) cfg.SpinAero = new FlyingGame.Core.DataContracts.SpinAeroConfig { RollDueToSideslip = "monoplane" };
        if (aerobaticElevator)
        {
            // the calibration's own bisection, to the aerobatic target (stall + 6 deg)
            double target = stallDeg + 6.0, lo = 3, hi = 120;
            cfg.Controls.Elevator.SaturationDeg = hi;
            if (StallAuthorityTests.FullAftBalanceDeg(cfg) >= target)
                for (int i = 0; i < 30; i++) { double mid = 0.5 * (lo + hi); cfg.Controls.Elevator.SaturationDeg = mid; if (StallAuthorityTests.FullAftBalanceDeg(cfg) >= target) hi = mid; else lo = mid; }
            cfg.Controls.Elevator.SaturationDeg = Math.Round(hi, 1);
        }
        _out.WriteLine($"== rotary {(rotary ? "ON " : "off")}  aerobatic elevator {(aerobaticElevator ? "ON " : "off")}  " +
                       $"(saturation {cfg.Controls.Elevator.SaturationDeg:F1} deg, full-aft balance {StallAuthorityTests.FullAftBalanceDeg(cfg):F1} vs stall {stallDeg:F1})");
        double v = ThermalSpawn.MinSinkSpeed(cfg, 1500);
        TrimSolver.Result t0 = TrimSolver.SolveGliderTrim(cfg, v, 1500, idleProp: true);
        var att = new Quat(0, Math.Sin(t0.ThetaRad / 2), 0, Math.Cos(t0.ThetaRad / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -1500), att,
            new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad)), Vec3.Zero),
            new FlyingGame.Core.Aero.ControlDeflections(0, t0.ElevatorRad, 0, 0));
        double h = SimLoop.DefaultFixedDtSec, lever = -1.0;
        for (double t = 0; t < 20; t += h) { ac.Step(new ControlInputs(0, -1.0, 0, lever), h); if (AlphaDeg(ac.State) > stallDeg + 2) break; }
        double net = 0, stalled = 0, n = 0, aSum = 0, pSum = 0, lost = 0, vMax = 0;
        var pro = new ControlInputs(0, -1.0, -1.0, lever);
        for (double t = 0; t < 30; t += h)
        {
            double z0 = ac.State.Position.Z;
            ac.Step(pro, h);
            RigidBodyState s = ac.State;
            lost += s.Position.Z - z0;
            net += OmVert(s) * h;
            double alpha = AlphaDeg(s);
            (_, double pitch, _) = FormationPilot.Euler(s.Attitude);
            if (alpha > 16) stalled++;
            aSum += alpha; pSum += pitch * 57.3; n++; vMax = Math.Max(vMax, Air(s));
            if (-s.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 1500), s.Attitude, s.Velocity, s.Rates);
        }
        double turns = Math.Abs(net) / (2 * Math.PI);
        _out.WriteLine($"   >> {turns:F2} turns in 30 s = {30 / Math.Max(0.01, turns):F1} s/turn, " +
                       $"{lost * 3.2808 / Math.Max(0.01, turns):F0} ft/turn, stalled {stalled / n:P0}, " +
                       $"mean alpha {aSum / n:F0}, mean pitch {pSum / n:F0}, fastest {vMax:F0} m/s");
    }

    /// <summary>Does the rotation SETTLE? A developed spin holds a roughly steady angle of attack, bank and rate; an
    /// oscillatory departure cycles through them and keeps leaving the stall. Reported over the LAST 20 s of a 45 s
    /// hold, so the incipient phase is excluded, with and without the rotary table.</summary>
    [Theory]
    [InlineData("glider-swift-s1-like", false)]
    [InlineData("glider-swift-s1-like", true)]
    [InlineData("extra-300-like", false)]
    [InlineData("glider-2-33-like", false)]
    public void DoesTheRotationSettle(string id, bool rotary)
    {
        if (System.Environment.GetEnvironmentVariable("SWIFT_SPIN") == null) return;
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        if (rotary) cfg.SpinAero = new FlyingGame.Core.DataContracts.SpinAeroConfig { RollDueToSideslip = "monoplane" };
        double stallDeg = StallAuthorityTests.StallDeg(cfg);
        double v = ThermalSpawn.MinSinkSpeed(cfg, 1500);
        TrimSolver.Result t0 = TrimSolver.SolveGliderTrim(cfg, v, 1500, idleProp: true);
        var att = new Quat(0, Math.Sin(t0.ThetaRad / 2), 0, Math.Cos(t0.ThetaRad / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -1500), att,
            new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad)), Vec3.Zero),
            new FlyingGame.Core.Aero.ControlDeflections(0, t0.ElevatorRad, 0, 0));
        double h = SimLoop.DefaultFixedDtSec, lever = cfg.Propulsion == null ? -1.0 : 1.0;
        for (double t = 0; t < 20; t += h) { ac.Step(new ControlInputs(0, -1.0, 0, lever), h); if (AlphaDeg(ac.State) > stallDeg + 2) break; }
        var pro = new ControlInputs(0, -1.0, -1.0, lever);
        var a = new List<double>(); var pit = new List<double>(); var om = new List<double>();
        double net = 0, lost = 0;
        for (double t = 0; t < 45; t += h)
        {
            double z0 = ac.State.Position.Z;
            ac.Step(pro, h);
            RigidBodyState s = ac.State;
            if (t >= 25)
            {
                a.Add(AlphaDeg(s));
                pit.Add(FormationPilot.Euler(s.Attitude).Item2 * 57.3);
                om.Add(OmVert(s) * 57.3);
                net += OmVert(s) * h;
                lost += s.Position.Z - z0;
            }
            if (-s.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 1500), s.Attitude, s.Velocity, s.Rates);
        }
        double Sd(List<double> xs) { double m = xs.Average(); return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / xs.Count); }
        double turns = Math.Abs(net) / (2 * Math.PI);
        _out.WriteLine($"== {id}{(rotary ? " + rotary" : "")}: last 20 s of a 45 s hold");
        _out.WriteLine($"   alpha {a.Average(),5:F0} +-{Sd(a),4:F0}   pitch {pit.Average(),5:F0} +-{Sd(pit),4:F0}   " +
                       $"rate {om.Average(),5:F0} +-{Sd(om),4:F0} deg/s   {turns:F2} turns = {20 / Math.Max(0.01, turns):F1} s/turn, " +
                       $"{lost * 3.2808 / Math.Max(0.01, turns):F0} ft/turn");
        _out.WriteLine($"   stalled {a.Count(x => x > 16) / (double)a.Count:P0}, left the stall {a.Count(x => x < 10) / (double)a.Count:P0} of the time");
    }
}

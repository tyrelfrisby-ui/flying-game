using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (STALL_PROBE=1): the Extra's stall break, BEFORE vs AFTER the derived wing sections
/// (docs/aircraft/extra-300-airfoil-DERIVATION.md). STALL_CFG_BEFORE=&lt;path&gt; is the config to compare against (the
/// 2026-09-07 hand-made `extra-sym` feel table, kept as tools/aircraft_builder/data/extra-sym-f8dc538.json); the
/// AFTER is the repo's extra-300-like. Every manoeuvre is flown open-loop past the break: the pilot model
/// below only sets the entry up and then FREEZES the controls, so whatever the aeroplane does after the break is its own.
///   static   aircraft CL(alpha) (elevator neutral, no rates) and the wing alone, 0-30 deg;
///   Vs       PracticeScenario.EstimateVso (trimmed, idle) and the 1 kt/s deceleration's minimum EAS;
///   1 kt/s   idle, wings held level by aileron and the speed bled 1 kt/s by elevator until the effective CL
///            (n W / q S) has peaked and fallen 10 %; then controls frozen 3 s. Variants: feet flat; 0.1 rudder held
///            from the start (a small slip); a 0.05 aileron step at the break;
///   accel    from an idle glide trim at 1.8 Vs, stick ramped to full aft in 1 s and held, ailerons held level until the
///            break, then frozen;
///   spin     the 1 kt/s entry, then full aft and full left rudder at the break; time to 90 % of the developed vertical
///            rotation (mean 15-25 s) and that rate;
///   snap     level at 90 KIAS, full power, full aft and full right rudder together; time to 180 deg/s of roll, peak
///            roll rate, time to 360 deg of bank.
/// </summary>
[Collection("WorldTerrainActive")]
public class StallCharacterProbe
{
    private readonly ITestOutputHelper _out;
    public StallCharacterProbe(ITestOutputHelper o) { _out = o; }

    private const double Kt = 0.514444, R2D = 57.29578;
    private static string Repo { get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; } }

    [Fact]
    public void BeforeAndAfter()
    {
        if (Environment.GetEnvironmentVariable("STALL_PROBE") == null) return;
        var cases = new List<(string Name, string Path)>();
        string? before = Environment.GetEnvironmentVariable("STALL_CFG_BEFORE");
        if (before != null) cases.Add(("before", before));
        cases.Add(("after", Path.Combine(Repo, "configs", "aircraft", "extra-300-like.json")));
        foreach (var (name, path) in cases)
        {
            AircraftConfig Cfg() => AircraftConfigLoader.LoadFromFile(path);
            _out.WriteLine($"==================== {name}: {path}");
            Static(Cfg());
            double vso = PracticeScenario.EstimateVso(Cfg(), 0.0);
            _out.WriteLine($"Vs (EstimateVso, trimmed idle, sea level, {Cfg().Mass.MassKg} kg): {vso / Kt:F1} KEAS");
            Report("1 kt/s feet flat", Decel(Cfg(), 0, 0, false));
            Report("1 kt/s rudder 0.1", Decel(Cfg(), 0.1, 0, false));
            Report("1 kt/s ail 0.05 at break", Decel(Cfg(), 0, 0.05, false));
            Report("accelerated", Accel(Cfg()));
            SpinEntry(Cfg());
            Snap(Cfg());
            // FOUR MORE CASES WERE DELIBERATELY LEFT BEHIND when this probe was extracted (2026-11-10). On the
            // originating lane this loop also printed a pitch-step response, a FleetSpinSweep spin with PARE/BM
            // recovery, a hands-off let-go and a roll-rate validation, by calling InertiaTailCaseProbe and
            // RollValidationProbe. Those two probes belong to that lane's OTHER work -- the mass properties built up
            // from components, the tail past the stall -- which Ty ruled out of this extraction:
            //   "Only the Extra wing section" (2026-11-10, off docs/pending/ORPHANED-LANES-TRIAGE-2026-11-10.md).
            // Pulling them in to keep four print lines would have dragged ~700 lines of the rejected work across the
            // boundary, so the calls are gone instead of the probes being vendored. Everything above this line is
            // self-contained and is the part that answers the wing-section question: the static lift curve, Vs, the
            // 1 kt/s break in three configurations, the accelerated break, the spin entry and the snap.
        }
    }

    private void Static(AircraftConfig c)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double s = c.Surfaces.Where(x => x.Id.StartsWith("wing")).SelectMany(x => x.Strips).Sum(x => x.Area), v = 30, rho = 1.225, q = 0.5 * rho * v * v;
        var wingMask = c.Surfaces.Select(x => x.Id.StartsWith("wing")).ToArray();
        _out.WriteLine($"static CL (S {s:F2} m2): alpha | aircraft de=0 | wing alone | Cm aircraft");
        double prevW = double.NaN, peakW = 0, peakA = 0;
        var line = new System.Text.StringBuilder();
        for (double a = 0; a <= 30.01; a += 1)
        {
            double ar = a / R2D;
            Vec3 vel = new(v * Math.Cos(ar), 0, v * Math.Sin(ar)), liftDir = new(Math.Sin(ar), 0, -Math.Cos(ar));
            var (f, m) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral);
            var (fw, _) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral, surfaceMask: wingMask);
            double cl = Vec3.Dot(f, liftDir) / (q * s), clw = Vec3.Dot(fw, liftDir) / (q * s), cm = m.Y / (q * s * 1.404);
            if (clw > peakW) { peakW = clw; peakA = a; }
            line.Append($"  {a,2:F0}:{cl:F3}/{clw:F3}/{cm:+0.000;-0.000}");
            if (((int)a + 1) % 6 == 0) { _out.WriteLine(line.ToString()); line.Clear(); }
            prevW = clw;
        }
        if (line.Length > 0) _out.WriteLine(line.ToString());
        _out.WriteLine($"wing-alone static CLmax {peakW:F3} at {peakA:F0} deg");
        foreach (bool wingOnly in new[] { true, false })
        {
            double Cl(double a)
            {
                double ar = a / R2D; Vec3 vel = new(v * Math.Cos(ar), 0, v * Math.Sin(ar)), ld = new(Math.Sin(ar), 0, -Math.Cos(ar));
                var (ff, _) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral, surfaceMask: wingOnly ? wingMask : null);
                return Vec3.Dot(ff, ld) / (q * s);
            }
            double am = 0, cm = 0;
            for (double a = 5; a <= 30; a += 0.05) { double x = Cl(a); if (x > cm) { cm = x; am = a; } }
            double c1 = Cl(am + 1), c3 = Cl(am + 3), c5 = Cl(am + 5);
            _out.WriteLine($"{(wingOnly ? "wing" : "aircraft de=0")} static peak {cm:F3} at {am:F2} deg; +1 deg {c1:F3} ({c1 / cm - 1:P0}), +3 {c3:F3} ({c3 / cm - 1:P0}), +5 {c5:F3} ({c5 / cm - 1:P0}); " +
                           $"steepest 0.05-deg drop past the peak {Enumerable.Range(1, 100).Select(k => (Cl(am + 0.05 * (k - 1)) - Cl(am + 0.05 * k)) / 0.05).Max():F2}/deg");
        }
    }

    public sealed class Break
    {
        public double Drop025, VsKeas, ClMax, AlphaAtMax, NzAtMax, DropCl, DropDeg, DropSec, Bank2s, PMax2s, Theta2s, Sep2s, Alpha2s;
        public bool Found;
    }

    private void Report(string name, Break b)
    {
        if (!b.Found) { _out.WriteLine($"{name}: no break found"); return; }
        _out.WriteLine($"{name}: break at {b.VsKeas:F1} KEAS (1-g equivalent {b.VsKeas / Math.Sqrt(Math.Max(0.1, b.NzAtMax)):F1}), n {b.NzAtMax:F2}, CLeff max {b.ClMax:F3} at alpha {b.AlphaAtMax:F1}; " +
                       $"CL drop {b.DropCl:F3} ({b.DropCl / b.ClMax:P0}) in {b.DropSec:F2} s = {b.DropCl / Math.Max(0.01, b.DropSec):F2}/s, first 0.25 s after the peak {b.Drop025:F3}; " +
                       $"+2 s: bank {b.Bank2s:+0;-0} deg, |p|max {b.PMax2s:F0} deg/s, dtheta {b.Theta2s:+0;-0} deg, alpha {b.Alpha2s:F0}, wing separated {b.Sep2s:P0}");
    }

    private static double Alpha(RigidBodyState s) => Math.Atan2(s.Velocity.Z, s.Velocity.X) * R2D;
    private static double Eas(RigidBodyState s) => s.Velocity.Length * Math.Sqrt(Atmosphere.DensityAtAltitude(-s.Position.Z) / Atmosphere.SeaLevelDensityKgM3);
    private static double ClEff(Aircraft ac, double sRef) => ac.LoadFactorZ * ac.Config.Mass.MassKg * 9.80665 / (0.5 * Atmosphere.SeaLevelDensityKgM3 * Math.Pow(Eas(ac.State), 2) * sRef);
    private static double SRef(AircraftConfig c) => c.Surfaces.Where(x => x.Id.StartsWith("wing")).SelectMany(x => x.Strips).Sum(x => x.Area);

    private static Aircraft Trimmed(AircraftConfig cfg, double eas, double alt)
    {
        double rho = Atmosphere.DensityAtAltitude(alt), tas = eas / Math.Sqrt(rho / Atmosphere.SeaLevelDensityKgM3);
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(cfg, eas, alt, idleProp: true);
        var att = new Quat(0, Math.Sin(t.ThetaRad / 2), 0, Math.Cos(t.ThetaRad / 2));
        return new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -alt), att, new Vec3(tas * Math.Cos(t.AlphaRad), 0, tas * Math.Sin(t.AlphaRad)), Vec3.Zero),
            new ControlDeflections(0, t.ElevatorRad, 0, 0));
    }

    /// <summary>Tracks the effective CL through the break, then flies 2 s more on frozen controls.</summary>
    private static Break Track(Aircraft ac, Func<double, bool, ControlInputs> pilot, double tMax)
    {
        double h = SimLoop.DefaultFixedDtSec, sRef = SRef(ac.Config);
        var b = new Break();
        double clMax = 0, tMax_ = 0, aMax = 0, nzMax = 0, vMax = 0;
        bool broke = false; double tBreak = 0; ControlInputs frozen = default;
        var hist = new List<(double T, double Cl)>();
        double bank0 = 0, th0 = 0, clMin = double.MaxValue, aMin = 0, tMin = 0;
        for (double t = 0; t < tMax; t += h)
        {
            ControlInputs ci = broke ? frozen : pilot(t, false);
            ac.Step(ci, h);
            var s = ac.State;
            double cl = ClEff(ac, sRef), a = Alpha(s);
            hist.Add((t, cl));
            var (roll, pitch, _) = FormationPilot.Euler(s.Attitude);
            if (!broke)
            {
                if (cl > clMax) { clMax = cl; tMax_ = t; aMax = a; nzMax = ac.LoadFactorZ; vMax = Eas(s); }
                if (clMax > 0.5 && cl < 0.9 * clMax && a > aMax)
                {
                    broke = true; tBreak = t; frozen = pilot(t, true); bank0 = roll; th0 = pitch;
                    b.Found = true; b.ClMax = clMax; b.AlphaAtMax = aMax; b.NzAtMax = nzMax; b.VsKeas = vMax / Kt;
                }
            }
            else
            {
                if (t - tBreak <= 1.5 && cl < clMin) { clMin = cl; aMin = a; tMin = t; }
                b.PMax2s = Math.Max(b.PMax2s, Math.Abs(s.Rates.X) * R2D);
                if (t - tBreak >= 2.0)
                {
                    b.Bank2s = (roll - bank0) * R2D; b.Theta2s = (pitch - th0) * R2D; b.Alpha2s = a; b.Sep2s = ac.StalledFraction;
                    b.DropCl = clMax - clMin; b.DropDeg = aMin - aMax; b.DropSec = tMin - tMax_;
                    b.Drop025 = clMax - hist.First(x => x.T >= tMax_ + 0.25).Cl;
                    return b;
                }
            }
        }
        return b;
    }

    /// <summary>The 1 kt/s stall: elevator bleeds EAS at 1 kt/s (PI on the speed error, q damping), ailerons hold the
    /// wings level, idle. At the break the controls freeze (plus the optional aileron step).</summary>
    public static Break Decel(AircraftConfig cfg, double rudder, double ailStepAtBreak, bool fullPower)
    {
        double vs = PracticeScenario.EstimateVso(cfg, 1500.0), v0 = 1.25 * vs;
        Aircraft ac = Trimmed(cfg, v0, 1500.0);
        double h = SimLoop.DefaultFixedDtSec, integ = 0, stick0 = -0.0, stick = 0, ail = 0;
        // the trimmed stick (the trim solver holds the elevator; the stick is relative to centre): start from the
        // deflection's stick and let the integrator take over.
        stick0 = Aircraft.StickForDeflection(ac.CurrentDeflections.ElevatorRad, cfg.Controls.Elevator);
        ControlInputs Pilot(double t, bool atBreak)
        {
            if (atBreak) return new ControlInputs(ail + ailStepAtBreak, stick, rudder, fullPower ? -1.0 : 1.0);
            var s = ac.State;
            double err = Eas(s) - (v0 - Kt * t);   // + = too fast: pull
            integ += err * h;
            stick = Math.Clamp(stick0 - 0.08 * err - 0.02 * integ + 0.3 * s.Rates.Y, -1, 1);
            var (roll, _, _) = FormationPilot.Euler(s.Attitude);
            ail = Math.Clamp(-1.5 * roll - 0.3 * s.Rates.X, -1, 1);
            return new ControlInputs(ail, stick, rudder, fullPower ? -1.0 : 1.0);
        }
        return Track(ac, Pilot, 120.0);
    }

    public static Break Accel(AircraftConfig cfg)
    {
        double vs = PracticeScenario.EstimateVso(cfg, 1500.0);
        Aircraft ac = Trimmed(cfg, 1.8 * vs, 1500.0);
        double ail = 0;
        ControlInputs Pilot(double t, bool atBreak)
        {
            if (!atBreak)
            {
                var (roll, _, _) = FormationPilot.Euler(ac.State.Attitude);
                ail = Math.Clamp(-1.5 * roll - 0.3 * ac.State.Rates.X, -1, 1);
            }
            return new ControlInputs(ail, -Math.Min(1.0, t / 1.0), 0, 1.0);
        }
        return Track(ac, Pilot, 10.0);
    }

    private void SpinEntry(AircraftConfig cfg)
    {
        double vs = PracticeScenario.EstimateVso(cfg, 2500.0), v0 = 1.25 * vs;
        Aircraft ac = Trimmed(cfg, v0, 2500.0);
        double h = SimLoop.DefaultFixedDtSec, integ = 0, sRef = SRef(cfg), clMax = 0, aMax = 0;
        double stick0 = Aircraft.StickForDeflection(ac.CurrentDeflections.ElevatorRad, cfg.Controls.Elevator);
        double t = 0;
        for (; t < 120; t += h)
        {
            var s = ac.State;
            double err = Eas(s) - (v0 - Kt * t); integ += err * h;
            double stick = Math.Clamp(stick0 - 0.08 * err - 0.02 * integ + 0.3 * s.Rates.Y, -1, 1);
            var (roll, _, _) = FormationPilot.Euler(s.Attitude);
            ac.Step(new ControlInputs(Math.Clamp(-1.5 * roll - 0.3 * s.Rates.X, -1, 1), stick, 0, 1.0), h);
            double cl = ClEff(ac, sRef), a = Alpha(ac.State);
            if (cl > clMax) { clMax = cl; aMax = a; }
            if (clMax > 0.5 && cl < 0.9 * clMax && a > aMax) break;
        }
        var om = new List<double>();
        for (double u = 0; u < 25; u += h)
        {
            ac.Step(new ControlInputs(0, -1.0, -1.0, 1.0), h);
            Vec3 zB = ac.State.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
            om.Add(Vec3.Dot(ac.State.Rates, zB));
            if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(ac.State.Position - new Vec3(0, 0, 2500), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
        }
        double dev = om.Skip((int)(15 / h)).Average();
        int k = om.FindIndex(x => Math.Abs(x) >= 0.9 * Math.Abs(dev) && Math.Sign(x) == Math.Sign(dev));
        _out.WriteLine($"spin entry from the 1 kt/s break (full aft + full left rudder): developed {Math.Abs(dev) * R2D:F0} deg/s; 90 % reached {(k < 0 ? double.NaN : (k + 1) * h):F2} s after the input");
    }

    private void Snap(AircraftConfig cfg)
    {
        double eas = 90 * Kt, alt = 1500;
        double rho = Atmosphere.DensityAtAltitude(alt), tas = eas / Math.Sqrt(rho / Atmosphere.SeaLevelDensityKgM3);
        TrimSolver.Result tr = TrimSolver.SolveGliderTrim(cfg, eas, alt, idleProp: true);
        var st = new RigidBodyState(new Vec3(0, 0, -alt), new Quat(0, Math.Sin(tr.AlphaRad / 2), 0, Math.Cos(tr.AlphaRad / 2)),
            new Vec3(tas * Math.Cos(tr.AlphaRad), 0, tas * Math.Sin(tr.AlphaRad)), Vec3.Zero);
        var ac = new Aircraft(cfg, st, new ControlDeflections(0, tr.ElevatorRad, 0, 0));
        double h = SimLoop.DefaultFixedDtSec, bank = 0, pMax = 0, t180 = double.NaN, t360 = double.NaN, aPk = 0, nPk = 0;
        for (double t = 0; t < 4.0; t += h)
        {
            ac.Step(new ControlInputs(0, -1.0, 1.0, -1.0), h);
            double p = ac.State.Rates.X * R2D;
            bank += p * h;
            pMax = Math.Max(pMax, Math.Abs(p)); aPk = Math.Max(aPk, Alpha(ac.State)); nPk = Math.Max(nPk, ac.LoadFactorZ);
            if (double.IsNaN(t180) && Math.Abs(p) >= 180) t180 = t;
            if (double.IsNaN(t360) && Math.Abs(bank) >= 360) t360 = t;
        }
        _out.WriteLine($"snap at 90 KEAS full power (full aft + full right rudder): |p| 180 deg/s at {t180:F2} s, peak |p| {pMax:F0} deg/s, 360 deg of roll at {t360:F2} s, peak alpha {aPk:F0}, peak n {nPk:F1}");
    }
}

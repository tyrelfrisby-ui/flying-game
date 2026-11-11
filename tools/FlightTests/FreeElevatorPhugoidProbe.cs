using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// FREE_PHUG=1 / FREE_PHUG_SWEEP=1 / FREE_PHUG_BASE=1 / FREE_PHUG_TRACE=1.
///
/// Ty's own report, 2026-11-10, flying the 2-33 — "The 2-33 has negative dynamic stability about the lateral axis...
/// Ie in pitch", and "It has had negative dynamic stability for quite some time now."
///
/// WHAT WAS ALREADY RULED OUT, elsewhere, and is not re-done here: the flat-plate tail conversion is not the cause
/// (all twelve flat-plate types sit −2.8°…+2.1° about their own trim, never reaching the plate table's 9° branch, and
/// the pre-conversion naca0012-like tail differs by under 0.3 %), and the propeller double-drive bug cannot reach a
/// glider, which has no propeller.
///
/// WHAT THIS PROBE MEASURED (2026-11-11). zeta, from the per-cycle growth of the longitudinal oscillation after one
/// 1 s pull, against the constrained-phugoid damping zeta = (1/sqrt 2)(C_D/C_L) = 1/(sqrt2 * L/D), which comes from
/// DRAG and is independent of the tail to first order:
///
///   type          L/D    zeta theory   zeta stick-FIXED   zeta elevator-FREE   what the free elevator does
///   2-33         19.2       0.0369        +0.0055              −0.0132          −0.0187  DESTABILISES
///   Swift S-1    34.9       0.0202        −0.0001              +0.0281          +0.0282  stabilises
///   EB29R        58.4       0.0121        −0.0130              +0.0509          +0.0639  stabilises
///   PA-18 Cub     6.7       0.1055        +0.1229              +0.1428          +0.0199  stabilises
///   C-172         7.3       0.0968        +0.1349              +0.1334          −0.0015  neutral
///
/// 1. IT IS THE ELEVATOR AND ONLY THE ELEVATOR. A free RUDDER is bit-identical to stick-fixed on every glider; a free
///    aileron likewise. <c>FREE_PHUG=1</c> prints all four combinations.
/// 2. ON THE 2-33 THE FREE ELEVATOR COSTS zeta 0.019 AND THAT IS ENOUGH TO INVERT THE SIGN — but it is only half the
///    story, because the 2-33 stick-FIXED is already 7x short of the drag theory (+0.0055 against 0.0369). There is
///    0.0055 of damping to spend and the elevator spends 0.019.
/// 3. THE OTHER TWO GLIDERS ARE DIVERGENT STICK-FIXED, WITH THE STICK HELD, NO FREE SURFACE ANYWHERE: the EB29R at
///    zeta −0.0130 and the Swift at −0.0001. Both are then DAMPED stick-free, +0.051 and +0.028: on a high-L/D
///    sailplane this model's floating elevator is the thing holding the mode together. A gate on the stick-free case
///    alone passes them for the wrong reason. The glider stick-fixed longitudinal mode is a fault of its own, it is
///    NOT the free surface, and it is not this lane's.
/// 4. THE FREE-FLOAT MODEL ITSELF MEASURES RIGHT. The float is delta_f -> −(Ch_alpha/Ch_delta)*alpha_tail with
///    Ch_alpha/Ch_delta = 0.444 (a plain unbalanced surface is 0.45–0.55), and the elevator commands tau_e = 0.636 of
///    the tail's own alpha-response (measured from the strip table: 0.840 m2 hinged at gain 1.0 plus 1.261 m2 fixed at
///    gain 0.393, over 2.101 m2). So stick-free the tail's alpha and q response falls to 1 − 0.636*0.444 = 72 % of
///    stick-fixed — which is the textbook stick-free reduction, in both form and size. There is no coefficient here
///    to correct without inventing one.
/// 5. WHICH TERM PUMPS IT (<c>FREE_PHUG_SWEEP=1</c>, 2-33, elevator free only): Ch_alpha. Baseline 1.090 per cycle;
///    Ch_alpha = 0 (the float blind to the flow) 0.963; Ch_alpha x0.5 1.020; x2 1.505. The linkage lag is NOT it —
///    tau 0.12 s gives 1.090 and tau 0.001 s still gives 1.079, so the destabilisation survives into the quasi-static
///    limit and is a STIFFNESS effect, not a phase lag. Adding hinge damping cannot fix it: a damper only makes tau
///    larger, and tau 0.5 s reads 1.311, tau 2.0 s reads 1.360. The centering spring DILUTES the alpha-float and so
///    helps — 0 kt 1.152, 40 kt (as configured) 1.090, 80 kt 1.010.
///
/// TWO MEASUREMENT TRAPS THIS PROBE WAS WRONG ABOUT FIRST, both worth keeping:
///   (a) AMPLITUDE MEASURED ABOUT A FIXED V_trim IS NOT AMPLITUDE. A glide drifts in equivalent airspeed over a 260 s
///       window, and |V_eas − V_trim| at each extremum adds that drift. Measured that way EVERY type in the fleet
///       "diverged" at small amplitude, the Cub at 1.242 per cycle on an oscillation of 0.47 m/s whose large-amplitude
///       damping matches the drag theory to 3 %. Half the swing to the PREVIOUS extremum is immune to any offset that
///       moves slowly against the mode, and with it the answer stops depending on the impulse (pull 0.03 and pull 0.3
///       now agree to 0.001 in zeta on all five types).
///   (b) AN EIGENVALUE IS ONLY AN EIGENVALUE AT SMALL AMPLITUDE. ReversibleControlTests' own 0.3 of stick puts the
///       2-33 through alpha 3°…10° and V 13.6…24.5 m/s on a 19 m/s trim; that holds neither alpha nor the drag polar
///       constant, so it is not a phugoid and the drag theory does not describe it. At 0.3 the Swift reads zeta
///       −0.048 stick-free and at 0.03 it reads +0.028 — opposite signs, same aeroplane. FREE_PHUG_PULL sets it;
///       state which you measured.
///
/// And the trap the whole repo shares: <c>ControlInputs.ThrottleLever</c> is INVERTED. −1 is FULL POWER, +1 is IDLE,
/// and Neutral's 0 is HALF. Everything here runs gliders at −1 (which is also brakes SHUT, since a glider reads the
/// aft half of that lever as speed brake) and powered types at +1, i.e. at IDLE, matching ReversibleControlTests.
/// </summary>
[Collection("WorldTerrainActive")]
public class FreeElevatorPhugoidProbe
{
    private readonly ITestOutputHelper _o;
    public FreeElevatorPhugoidProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;
    private double? _lastExt;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static IDisposable StillAir()
    {
        var field = Atmosphere.Field; var wind = Atmosphere.SteadyWind; var turb = Atmosphere.ActiveTurbulence;
        var terrain = WorldTerrain.Active; var ridge = Atmosphere.ActiveRidge; var thermals = Atmosphere.Thermals.ToList();
        Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null;
        WorldTerrain.Active = null; Atmosphere.ActiveRidge = null; Atmosphere.Thermals.Clear();
        return new Restore(() =>
        {
            Atmosphere.Field = field; Atmosphere.SteadyWind = wind; Atmosphere.ActiveTurbulence = turb;
            WorldTerrain.Active = terrain; Atmosphere.ActiveRidge = ridge; Atmosphere.Thermals.AddRange(thermals);
        });
    }
    private sealed class Restore : IDisposable { private readonly Action _a; public Restore(Action a) { _a = a; } public void Dispose() => _a(); }

    /// <summary>One flight: trim at 3,000 m as ReversibleControlTests does, one 1 s pull, then hands off.</summary>
    private sealed class Flight
    {
        public double PerCycle;             // geometric mean growth per cycle of |V − V_trim| peaks
        public double PeriodS;
        public List<(double t, double amp)> Peaks = new();
        public double MaxAmp;
        public string Note = "";
    }

    private Flight Run(string id, bool eleFree, bool rudFree, bool ailFree, double seconds,
                       Action<AircraftConfig>? tweak = null, bool trace = false, double traceEvery = 0.5)
    {
        AircraftConfig cfg = Load(id);
        tweak?.Invoke(cfg);
        const double alt = 3000;
        double v = cfg.SpawnIasMs > 1 ? cfg.SpawnIasMs : 25;
        TrimSolver.Result t0 = TrimSolver.SolveGliderTrim(cfg, v, alt, idleProp: true);
        double hh = t0.ThetaRad / 2;
        var q0 = new Quat(0, Math.Sin(hh), 0, Math.Cos(hh));
        var st0 = new RigidBodyState(new Vec3(0, 0, -alt), q0,
            new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad)), Vec3.Zero);
        var ac = new Aircraft(cfg, st0, new ControlDeflections(0, t0.ElevatorRad, 0, 0));
        double trim = Aircraft.StickForDeflection(t0.ElevatorRad, cfg.Controls.Elevator);
        double lever = cfg.Propulsion == null ? -1.0 : 1.0;   // glider: lever −1 keeps the brakes SHUT. Powered: +1 is IDLE.
        double h = SimLoop.DefaultFixedDtSec;
        // THE IMPULSE SIZE IS A KNOB (FREE_PHUG_PULL), because the eigenvalue is only the eigenvalue at small
        // amplitude. The test's own 0.3 of stick puts the 2-33 through alpha 3 deg to 10 deg and V 13.6 to 24.5 m/s on a
        // 19 m/s trim: that is not a phugoid (which holds alpha nearly constant) and the drag theory does not describe
        // it. Measure the mode at 0.03 and the forced motion at 0.3, and say which you measured.
        double pullScale = double.TryParse(Environment.GetEnvironmentVariable("FREE_PHUG_PULL"), out double ps) ? ps : 0.3;
        double pull = pullScale * Math.Min(1.0, Math.Pow(25.0 / v, 2));
        double rho0 = Atmosphere.DensityAtAltitude(0);
        double v0e = v * Math.Sqrt(Atmosphere.DensityAtAltitude(alt) / rho0);
        var f = new Flight();
        // peak tracking on the signal (V_eas − V_trim)
        double prev = 0, prev2 = 0, prevT = 0;
        _lastExt = null;
        if (trace)
            _o.WriteLine("     t    Veas   alpha   theta       q     ele    eleTgt     A(flow)     B(defl)      K(spr)   rud");
        for (double t = 0; t < seconds; t += h)
        {
            bool pulling = t >= 30 && t < 31;
            (double roll, _, _) = FormationPilot.Euler(ac.State.Attitude);
            double ail = Math.Clamp(-roll * 2.0 - ac.State.Rates.X * 0.3, -1, 1);
            ac.Step(pulling ? new ControlInputs(ail, trim - pull, 0, lever, ailFree, false, rudFree)
                            : new ControlInputs(ail, trim, 0, lever, ailFree, eleFree, rudFree), h);
            RigidBodyState s = ac.State;
            double vv = s.Velocity.Length * Math.Sqrt(Atmosphere.DensityAtAltitude(-s.Position.Z) / rho0);
            double sig = vv - v0e;
            if (t > 31.5)
            {
                // A local extremum. THE AMPLITUDE IS HALF THE SWING TO THE PREVIOUS EXTREMUM, not |signal| at the
                // extremum: a glide drifts in equivalent airspeed over a 260 s window, and |signal| measured about a
                // fixed V_trim adds that drift to the amplitude. Measured that way every type in the fleet "diverged"
                // at small amplitude — the Cub at 1.242 per cycle on an oscillation of 0.47 m/s whose large-amplitude
                // damping matches the drag theory to 3 %. Peak-to-peak is immune to any offset that moves slowly
                // compared with the mode.
                if (prev2 != 0 && ((prev > prev2 && prev > sig) || (prev < prev2 && prev < sig)))
                {
                    if (_lastExt is { } le) f.Peaks.Add((prevT, 0.5 * Math.Abs(prev - le)));
                    _lastExt = prev;
                }
                f.MaxAmp = Math.Max(f.MaxAmp, Math.Abs(sig));
            }
            prev2 = prev; prev = sig; prevT = t;
            if (trace && (int)Math.Round(t / h) % Math.Max(1, (int)Math.Round(traceEvery / h)) == 0)
            {
                (double _, double pitch, double _) = FormationPilot.Euler(s.Attitude);
                double alpha = Math.Atan2(s.Velocity.Z, s.Velocity.X) * D;
                (double A, double B, double K, double Den, double _, double _) = ac.HingeDebug("elevator", cfg.Controls.Elevator);
                double tgt = ac.FreeFloat("elevator", cfg.Controls.Elevator, t0.ElevatorRad, ac.CurrentDeflections.ElevatorRad);
                _o.WriteLine($"{t,6:F1} {vv,7:F2} {alpha,7:F3} {pitch * D,7:F2} {s.Rates.Y * D,7:F3} "
                    + $"{ac.CurrentDeflections.ElevatorRad * D,7:F3} {tgt * D,7:F3} {A,11:F2} {B,11:F2} {K,11:F2} {ac.CurrentDeflections.RudderRad * D,6:F2}");
            }
        }
        // Per-cycle growth from the LINEAR regime only. A diverging phugoid saturates (the Swift reaches 54 m/s of
        // amplitude on a 26 m/s trim and then stalls), and first-peak-to-last-peak across the saturation reads as
        // DAMPED — 0.94 on an aeroplane whose first eight peaks grow monotonically. So: least squares of ln(amplitude)
        // on time over the first `fit` half-cycle peaks, converted to a per-cycle ratio with the measured period.
        if (f.Peaks.Count >= 4)
        {
            var p = f.Peaks;
            int fit = Math.Min(p.Count, 8);
            f.PeriodS = 2.0 * (p[fit - 1].t - p[0].t) / (fit - 1);
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < fit; i++)
            {
                double x = p[i].t - p[0].t, y = Math.Log(Math.Max(1e-9, p[i].amp));
                sx += x; sy += y; sxx += x * x; sxy += x * y;
            }
            double den = fit * sxx - sx * sx;
            double slope = Math.Abs(den) < 1e-12 ? 0.0 : (fit * sxy - sx * sy) / den;
            f.PerCycle = Math.Exp(slope * f.PeriodS);
        }
        return f;
    }

    private void Line(string label, Flight f)
        => _o.WriteLine($"  {label,-44} per cycle {f.PerCycle,6:F3}   period {f.PeriodS,5:F1} s   peak amp {f.MaxAmp,6:F2} m/s   "
            + $"({f.Peaks.Count} half-cycles: {string.Join(" ", f.Peaks.Select(x => x.amp.ToString("F2")).Take(10))})");

    private static readonly string[] DefaultIds = { "glider-2-33-like", "glider-swift-s1-like" };

    /// <summary>FREE_PHUG=1: the per-cycle growth, stick-free against stick-fixed, on each type.</summary>
    [Fact]
    public void TheGrowthPerCycle()
    {
        if (Environment.GetEnvironmentVariable("FREE_PHUG") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("FREE_PHUG_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        if (ids.Length == 0) ids = DefaultIds;
        double sec = double.TryParse(Environment.GetEnvironmentVariable("FREE_PHUG_SEC"), out double s) ? s : 150.0;
        using var still = StillAir();
        foreach (string id in ids)
        {
            _o.WriteLine($"=== {id}");
            Line("stick FIXED (nothing reversible)", Run(id, false, false, false, sec,
                c => { foreach (ControlAxisConfig a in All(c)) a.Reversible = false; }));
            Line("stick FREE (elevator + rudder, as the test)", Run(id, true, true, false, sec));
            Line("elevator FREE only", Run(id, true, false, false, sec));
            Line("rudder FREE only", Run(id, false, true, false, sec));
        }
    }

    private static ControlAxisConfig[] All(AircraftConfig c)
        => new[] { c.Controls.Aileron, c.Controls.Elevator, c.Controls.Rudder };

    /// <summary>FREE_PHUG_SWEEP=1: which input to the float pumps it. Elevator free only, one coefficient at a time.</summary>
    [Fact]
    public void WhichTermPumpsIt()
    {
        if (Environment.GetEnvironmentVariable("FREE_PHUG_SWEEP") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("FREE_PHUG_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        if (ids.Length == 0) ids = DefaultIds;
        double sec = double.TryParse(Environment.GetEnvironmentVariable("FREE_PHUG_SEC"), out double s) ? s : 150.0;
        using var still = StillAir();
        foreach (string id in ids)
        {
            _o.WriteLine($"=== {id}, ELEVATOR FREE ONLY");
            Line("baseline", Run(id, true, false, false, sec));
            Line("ChAlpha = 0 (float blind to the flow)", Run(id, true, false, false, sec,
                c => c.Controls.Elevator.HingeChAlpha = 0));
            Line("ChAlpha x0.5", Run(id, true, false, false, sec, c => c.Controls.Elevator.HingeChAlpha *= 0.5));
            Line("ChAlpha x2", Run(id, true, false, false, sec, c => c.Controls.Elevator.HingeChAlpha *= 2.0));
            Line("tau = 0.001 s (no linkage lag)", Run(id, true, false, false, sec, c => c.Controls.Elevator.FreeTauS = 0.001));
            Line("tau = 0.5 s", Run(id, true, false, false, sec, c => c.Controls.Elevator.FreeTauS = 0.5));
            Line("tau = 2.0 s", Run(id, true, false, false, sec, c => c.Controls.Elevator.FreeTauS = 2.0));
            Line("trim tab instead of spring", Run(id, true, false, false, sec,
                c => { c.Controls.Elevator.TrimType = "tab"; c.Controls.Elevator.CenteringSpringKt = 0; }));
            Line("spring 0 kt, keep spring trim", Run(id, true, false, false, sec,
                c => c.Controls.Elevator.CenteringSpringKt = 0));
            Line("spring 80 kt", Run(id, true, false, false, sec, c => c.Controls.Elevator.CenteringSpringKt = 80));
        }
    }

    /// <summary>
    /// FREE_PHUG_BASE=1: the STICK-FIXED phugoid against the drag theory, because that is the floor the free surface
    /// stands on. The constrained-phugoid damping is ζ = (1/√2)·(C_D/C_L) = 1/(√2·L/D) — it comes from DRAG and is
    /// independent of the tail to first order, so a stick-fixed phugoid far below it is a fault of its own and the
    /// free elevator only has to remove the rest. Prints the glide's measured L/D at the trim point beside it.
    /// </summary>
    [Fact]
    public void TheStickFixedFloor()
    {
        if (Environment.GetEnvironmentVariable("FREE_PHUG_BASE") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("FREE_PHUG_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        if (ids.Length == 0) ids = DefaultIds;
        double sec = double.TryParse(Environment.GetEnvironmentVariable("FREE_PHUG_SEC"), out double s) ? s : 150.0;
        using var still = StillAir();
        foreach (string id in ids)
        {
            AircraftConfig c0 = Load(id);
            const double alt = 3000;
            double v = c0.SpawnIasMs > 1 ? c0.SpawnIasMs : 25;
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c0, v, alt, idleProp: true);
            double ld = Math.Abs(1.0 / Math.Tan(t.ThetaRad - t.AlphaRad));
            double zTheory = 1.0 / (Math.Sqrt(2.0) * ld);
            _o.WriteLine($"=== {id}: trim V {v:F1} m/s TAS at {alt:F0} m, gamma {(t.ThetaRad - t.AlphaRad) * D:F2} deg -> L/D {ld:F1}");
            _o.WriteLine($"    constrained-phugoid theory: zeta {zTheory:F4}, per cycle {Math.Exp(-2 * Math.PI * zTheory):F3}, "
                // omega_n = sqrt(2)*g/V, so T = 2*pi*V/(sqrt(2)*g). NOT 2*pi*sqrt(2)*V/g, which is 2x too long and
                // made the glider periods read SHORT of classical when in fact they are half again too LONG.
                + $"period {2 * Math.PI * v / (Math.Sqrt(2.0) * 9.81):F1} s");
            void Z(string label, Flight f)
            {
                double zeta = f.PerCycle > 0 ? -Math.Log(f.PerCycle) / (2 * Math.PI) : double.NaN;
                _o.WriteLine($"    {label,-40} per cycle {f.PerCycle,6:F3}  zeta {zeta,8:F4}  period {f.PeriodS,5:F1} s  peak {f.MaxAmp,6:F2} m/s");
            }
            Z("stick FIXED", Run(id, false, false, false, sec, x => { foreach (ControlAxisConfig a in All(x)) a.Reversible = false; }));
            Z("stick FIXED, unsteady lag OFF", Run(id, false, false, false, sec,
                x => { foreach (ControlAxisConfig a in All(x)) a.Reversible = false; x.StallDynamics.UnsteadyLagEnabled = false; }));
            Z("elevator FREE", Run(id, true, false, false, sec));
            Z("elevator FREE, unsteady lag OFF", Run(id, true, false, false, sec,
                x => x.StallDynamics.UnsteadyLagEnabled = false));
        }
    }

    /// <summary>FREE_PHUG_TRACE=1: the state and the hinge balance, frame by frame, on one type.</summary>
    [Fact]
    public void TraceTheBalance()
    {
        if (Environment.GetEnvironmentVariable("FREE_PHUG_TRACE") == null) return;
        string id = Environment.GetEnvironmentVariable("FREE_PHUG_IDS") ?? "glider-2-33-like";
        double sec = double.TryParse(Environment.GetEnvironmentVariable("FREE_PHUG_SEC"), out double s) ? s : 90.0;
        double every = double.TryParse(Environment.GetEnvironmentVariable("FREE_PHUG_EVERY"), out double e) ? e : 0.5;
        using var still = StillAir();
        _o.WriteLine($"=== {id} elevator FREE only");
        Flight f = Run(id, true, false, false, sec, null, trace: true, traceEvery: every);
        Line("elevator FREE only", f);
    }
}

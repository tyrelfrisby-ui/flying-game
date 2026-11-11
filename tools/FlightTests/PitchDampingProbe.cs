using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// PITCH_DAMP=1: IS THE 2-33 DYNAMICALLY UNSTABLE IN PITCH, AND IS THE FLAT-PLATE TAIL WHY?
///
/// Ty, 2026-11-10, flying the current build: <i>"The 2-33 has negative dynamic stability about the lateral
/// axis"</i>, <i>"Ie in pitch"</i> — and then, decisively, <i>"It has had negative dynamic stability for quite
/// some time now"</i>. The flat-plate conversion landed THAT DAY (b7396cd0), so "quite some time" rules the plate
/// out as the origin before any measurement, however good the mechanism looks on paper. (And the mechanism does
/// look good: the plate's table peaks at Cl 0.900 at α 9° and declines monotonically past 50°, so a tail working
/// on the declining side contributes pitch damping with the WRONG SIGN — a nose-up rate raises the tail's local
/// α, which REDUCES tail lift, which pitches the nose further up.)
///
/// So this probe answers three separable questions rather than confirming one story:
///
///  1. WHERE DOES THE 2-33'S TAIL ACTUALLY WORK? The mechanism above only bites if the tail is past 9° of its
///     OWN local angle in ordinary flight. The probe prints the tail's local α at trim and over the α sweep, so
///     "a post-stall curiosity" and "the aeroplane's everyday pitch damping" can be told apart by measurement.
///  2. dM/dq ABOUT THE TRIMMED STATE, not at a round α with the controls neutral, over the band the type really
///     flies — and the same for all twelve plate-tailed types, so a fleet-wide fault and one aeroplane's bug can
///     be told apart.
///  3. DOES IT DIVERGE WHEN FLOWN? dM/dq &gt; 0 is not divergence on its own: the pitch modes depend on Cm_α, Cm_q
///     and Iyy together, and a glider's PHUGOID is nearly undamped for a reason that is real physics and not a
///     defect — ζ ≈ 1/(√2·L/D), which is 0.03 at L/D 23. `ReversibleControlTests` already says in its own comment
///     that "the 2-33 stick-FIXED barely damps". So the aeroplane is flown here: trimmed, pulled for a second,
///     then hands-off, and the successive speed excursions are reported as a GROWTH RATIO per cycle. Above 1 it
///     is divergent; just under 1 it is the glider phugoid Ty would also feel as "negative dynamic stability".
///
/// Each question is asked with the plate tail AND with the `naca0012-like` tail it had before b7396cd0, and with
/// the VERTICAL tail masked out, because the coordinator asked for the fin and the spoiler to be ruled out rather
/// than assumed. (The spoiler needs no run: it is gated on a deflected control and dM/dq is controls-neutral,
/// measured bit-identical at k 0 and 0.6 on all thirteen configs in `SpoilerFleetProbe`; and the 2-33 carried
/// k 0 until today in any case.)
/// </summary>
[Collection("WorldTerrainActive")]
public class PitchDampingProbe
{
    private readonly ITestOutputHelper _o;
    public PitchDampingProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;

    private static string Root()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return d.FullName;
    }
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(Root(), "configs", "aircraft", id + ".json"));

    /// <summary>Put the horizontal tail back on the section it flew before b7396cd0. The 0012 table is not in
    /// every config's `airfoilTables` any more, so it is taken from the C172, which still carries it.</summary>
    private static bool To0012(AircraftConfig c)
    {
        if (!c.AirfoilTables.ContainsKey("naca0012-like"))
        {
            var src = Load("c172-like");
            if (!src.AirfoilTables.TryGetValue("naca0012-like", out AirfoilTableData? t)) return false;
            c.AirfoilTables["naca0012-like"] = t;
        }
        bool any = false;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (sf.Id != "hStab" && sf.Id != "elevator") continue;
            foreach (StripConfig st in sf.Strips) { if (st.Airfoil == "flat-plate") { st.Airfoil = "naca0012-like"; any = true; } }
        }
        return any;
    }

    private static bool[] MaskWithoutFin(AircraftConfig c)
    {
        var m = new bool[c.Surfaces.Count];
        for (int i = 0; i < m.Length; i++)
        {
            string id = c.Surfaces[i].Id;
            m[i] = !(id.Contains("vstab", StringComparison.OrdinalIgnoreCase)
                     || id.StartsWith("rudder", StringComparison.OrdinalIgnoreCase));
        }
        return m;
    }

    /// <summary>dM/dq and dM/dα at one body alpha, with the elevator held where TRIM put it (not at neutral —
    /// damping about the trimmed state is the quantity that decides the pitch modes), both per Iyy so the number
    /// is a rate in 1/s and types of different size are comparable. Positive dM/dq = ANTI-damping.</summary>
    private static (double mq, double ma) Derivs(AircraftConfig c, IReadOnlyDictionary<string, AirfoilTable> t,
                                                 double vMs, double alphaRad, double elevRad, bool[]? mask)
    {
        double iyy = c.Mass.Inertia.Iyy;
        Vec3 v = new(vMs * Math.Cos(alphaRad), 0, vMs * Math.Sin(alphaRad));
        var ctl = new ControlDeflections(0, elevRad, 0, 0);
        double M(Vec3 vv, Vec3 om) => AeroModel.Compute(c, t, vv, om, Vec3.Zero, 1.225, ctl,
                                                        surfaceMask: mask).Moment.Y;
        double mq = (M(v, new Vec3(0, 0.05, 0)) - M(v, new Vec3(0, -0.05, 0))) / 0.1 / iyy;
        double h = 0.25 / D;
        Vec3 va = new(vMs * Math.Cos(alphaRad + h), 0, vMs * Math.Sin(alphaRad + h));
        Vec3 vb = new(vMs * Math.Cos(alphaRad - h), 0, vMs * Math.Sin(alphaRad - h));
        double ma = (M(va, Vec3.Zero) - M(vb, Vec3.Zero)) / (2 * h) / iyy;
        return (mq, ma);
    }

    /// <summary>The horizontal tail's own local angle and dynamic-pressure ratio, from the gated flow
    /// diagnostic — the number that says whether the plate's declining branch is even reached.</summary>
    private static (double alphaT, double eps, double etaT) TailFlow(AircraftConfig c,
        IReadOnlyDictionary<string, AirfoilTable> t, double vMs, double alphaRad, double elevRad)
    {
        Vec3 v = new(vMs * Math.Cos(alphaRad), 0, vMs * Math.Sin(alphaRad));
        var cap = new List<ForceSample>(512);
        ForceDebug.FlowDiagnostics = true; ForceDebug.Samples = cap;
        try { AeroModel.Compute(c, t, v, Vec3.Zero, Vec3.Zero, 1.225, new ControlDeflections(0, elevRad, 0, 0)); }
        finally { ForceDebug.Samples = null; ForceDebug.FlowDiagnostics = false; }
        double a = 0, e = 0, q = 0; int n = 0;
        foreach (ForceSample fs in cap)
        {
            if (fs.Kind != "tailflow" || fs.PosBody.X > -1.0 || Math.Abs(fs.PosBody.Y) <= 0.03) continue;
            a += fs.MomentBody.X; e += fs.MomentBody.Y; q += fs.MomentBody.Z; n++;
        }
        return n > 0 ? (a / n * D, e / n * D, q / n) : (double.NaN, double.NaN, double.NaN);
    }

    /// <summary>Fly it: trimmed, hands off, one second of pull at t = 30 s, then hands off again. Returns the
    /// successive peak speed excursions from trim so the mode can be read as growing or decaying, and the growth
    /// ratio between the last two. `stickFixed` holds the surfaces where the stick puts them instead of letting
    /// the reversible elevator float, which separates the airframe's mode from the free-control one.</summary>
    private (List<double> peaks, double ratio) Fly(AircraftConfig cfg, bool stickFixed)
    {
        if (stickFixed) foreach (ControlAxisConfig a in new[] { cfg.Controls.Aileron, cfg.Controls.Elevator, cfg.Controls.Rudder }) a.Reversible = false;
        double v = cfg.SpawnIasMs > 1 ? cfg.SpawnIasMs : 25.0;
        const double alt = 3000;
        TrimSolver.Result tr = TrimSolver.SolveGliderTrim(cfg, v, alt, idleProp: true);
        double th = tr.ThetaRad / 2;
        var st = new RigidBodyState(new Vec3(0, 0, -alt), new Quat(0, Math.Sin(th), 0, Math.Cos(th)),
                                    new Vec3(v * Math.Cos(tr.AlphaRad), 0, v * Math.Sin(tr.AlphaRad)), Vec3.Zero);
        var ac = new Aircraft(cfg, st, new ControlDeflections(0, tr.ElevatorRad, 0, 0));
        double trim = Aircraft.StickForDeflection(tr.ElevatorRad, cfg.Controls.Elevator);
        double lever = cfg.Propulsion == null ? -1.0 : 1.0, h = SimLoop.DefaultFixedDtSec;
        double v0e = v * Math.Sqrt(Atmosphere.DensityAtAltitude(alt) / Atmosphere.DensityAtAltitude(0));
        double pull = 0.3 * Math.Min(1.0, Math.Pow(25.0 / v, 2));

        var peaks = new List<double>();
        double lastErr = 0, prevErr = 0;
        for (double t = 0; t < 110; t += h)
        {
            bool pulling = t >= 30 && t < 31;
            (double roll, _, _) = FormationPilot.Euler(ac.State.Attitude);
            double ail = Math.Clamp(-roll * 2.0 - ac.State.Rates.X * 0.3, -1, 1);
            ac.Step(pulling ? new ControlInputs(ail, trim - pull, 0, lever, false, false, true)
                            : new ControlInputs(ail, trim, 0, lever, false, !stickFixed, true), h);
            double ve = ac.State.Velocity.Length * Math.Sqrt(Atmosphere.DensityAtAltitude(-ac.State.Position.Z) / Atmosphere.DensityAtAltitude(0));
            double err = ve - v0e;
            if (t > 31)
            {
                // A turning point in the speed trace is one half-cycle of the phugoid; record its size.
                if (Math.Sign(err - lastErr) != Math.Sign(lastErr - prevErr) && Math.Abs(lastErr) > 0.05)
                    peaks.Add(Math.Abs(lastErr));
            }
            prevErr = lastErr; lastErr = err;
            if (peaks.Count >= 12) break;
        }
        double ratio = peaks.Count >= 4 ? peaks[^1] / Math.Max(1e-6, peaks[^3]) : double.NaN;   // one full cycle = two turning points
        return (peaks, ratio);
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("PITCH_DAMP") == null) return;
        bool clampWas = AeroModel.ClampNormalForcePastStall;
        AeroModel.ClampNormalForcePastStall = false;
        try
        {
            // ---- 1 & 2: where the tail works, and dM/dq about trim, on the twelve plate-tailed types -------
            _o.WriteLine("dM/dq AND dM/dalpha ABOUT THE TRIMMED STATE, per Iyy (1/s and 1/s^2). Positive dM/dq = " +
                         "ANTI-damping. 'tail a' is the horizontal tail's OWN local angle at trim: the plate's " +
                         "table peaks at 9 deg and declines past it, so only a tail PAST 9 can be on the " +
                         "wrong-signed branch.\n");
            _o.WriteLine($"{"type",-22} {"V",5} {"alpha",6} {"elev",6} | {"tail a",7} {"eps",6} {"eta_t",6} | " +
                         $"{"dM/dq",8} {"dM/da",9} | {"0012: dM/dq",12} {"dM/da",9} | {"no fin: dM/dq",13}");
            foreach (string id in new[]
            {
                "glider-2-33-like", "great-lakes-like", "decathlon-8kcab-like", "j3-cub-like", "j3-cub-floats-like",
                "pa18-cub-like", "pa18-bush-like", "pa18-floats-like", "stearman-pt17-like", "jn4-jenny-like",
                "pa25-pawnee-like", "pitts-s2b-like",
            })
            {
                var c = Load(id);
                var t = Aircraft.BuildAirfoilTables(c);
                double v = c.SpawnIasMs > 1 ? c.SpawnIasMs : 25.0;
                TrimSolver.Result tr = TrimSolver.SolveGliderTrim(c, v, 0, idleProp: true);
                var (at, eps, eta) = TailFlow(c, t, v, tr.AlphaRad, tr.ElevatorRad);
                var now = Derivs(c, t, v, tr.AlphaRad, tr.ElevatorRad, null);
                var noFin = Derivs(c, t, v, tr.AlphaRad, tr.ElevatorRad, MaskWithoutFin(c));
                var c2 = Load(id); bool swapped = To0012(c2);
                var t2 = Aircraft.BuildAirfoilTables(c2);
                TrimSolver.Result tr2 = TrimSolver.SolveGliderTrim(c2, v, 0, idleProp: true);
                var old = swapped ? Derivs(c2, t2, v, tr2.AlphaRad, tr2.ElevatorRad, null) : (double.NaN, double.NaN);
                _o.WriteLine($"{id,-22} {v,5:F1} {tr.AlphaRad * D,6:F2} {tr.ElevatorRad * D,6:F2} | {at,7:F2} {eps,6:F2} {eta,6:F3} | " +
                             $"{now.mq,8:F4} {now.ma,9:F4} | {old.Item1,12:F4} {old.Item2,9:F4} | {noFin.mq,13:F4}");
            }

            // ---- the 2-33's own band, since that is the aeroplane he felt it in ------------------------------
            {
                var c = Load("glider-2-33-like");
                var t = Aircraft.BuildAirfoilTables(c);
                var c2 = Load("glider-2-33-like"); To0012(c2);
                var t2 = Aircraft.BuildAirfoilTables(c2);
                double v = c.SpawnIasMs;
                TrimSolver.Result tr = TrimSolver.SolveGliderTrim(c, v, 0, idleProp: true);
                _o.WriteLine($"\nTHE 2-33 ACROSS ITS OWN BAND at {v:F0} m/s with the elevator held at its trim " +
                             $"({tr.ElevatorRad * D:F2} deg). Its hStab is rigged at {c.Surfaces.Find(s => s.Id == "hStab")!.Strips[0].IncidenceRad * D:F1} deg, " +
                             "which is why its tail sits so far below the body alpha.");
                _o.WriteLine($"  {"body a",7} | {"tail a",7} {"eps",6} | {"plate dM/dq",12} {"dM/da",9} | {"0012 dM/dq",11} {"dM/da",9}");
                for (double a = -4; a <= 20.0001; a += 2)
                {
                    double ar = a / D;
                    var (at, eps, _) = TailFlow(c, t, v, ar, tr.ElevatorRad);
                    var p = Derivs(c, t, v, ar, tr.ElevatorRad, null);
                    var o = Derivs(c2, t2, v, ar, tr.ElevatorRad, null);
                    _o.WriteLine($"  {a,7:F1} | {at,7:F2} {eps,6:F2} | {p.mq,12:F4} {p.ma,9:F4} | {o.mq,11:F4} {o.ma,9:F4}" +
                                 $"{(p.mq > 0 ? "   PLATE ANTI-DAMPED" : "")}{(o.mq > 0 ? "   0012 ANTI-DAMPED" : "")}");
                }
            }

            // ---- 3: flown ------------------------------------------------------------------------------------
            _o.WriteLine("\nFLOWN: trimmed, hands off, a 1 s pull at t = 30 s, then hands off. The speed trace's " +
                         "turning points are the phugoid's half-cycles; 'ratio' is the last peak over the one a " +
                         "FULL cycle earlier, so > 1 is divergent and < 1 is damped. A glider's phugoid is nearly " +
                         "undamped by real physics (zeta ~ 1/(sqrt2*L/D) = 0.03 at L/D 23), so a ratio a little " +
                         "under 1 is the aeroplane and not a defect.");
            // The 2-33 is the aeroplane he felt it in; the Swift is the control — a composite glider at L/D 36
            // whose tail was never converted, so if the phugoid is nearly undamped there too it is the glider
            // and not the plate. (Each run is 110 s of flight at the fixed step, so the matrix is kept small:
            // PITCH_DAMP_TYPES overrides the list, comma-separated, for anyone who wants more.)
            string[] flyIds = (Environment.GetEnvironmentVariable("PITCH_DAMP_TYPES")
                               ?? "glider-2-33-like,glider-swift-s1-like").Split(',');
            foreach (string id in flyIds)
                foreach (bool fixedStick in new[] { false, true })
                    foreach (bool plate in new[] { true, false })
                    {
                        var c = Load(id);
                        if (!plate && !To0012(c)) continue;
                        var (peaks, ratio) = Fly(c, fixedStick);
                        string tail = plate ? "plate" : "0012 ";
                        _o.WriteLine($"  {id,-22} {(fixedStick ? "stick-fixed" : "stick-free ")} {tail} " +
                                     $"ratio/cycle {ratio,6:F3}{(ratio > 1.0 ? "  DIVERGENT" : "")}  peaks m/s: " +
                                     string.Join(" ", peaks.ConvertAll(p => p.ToString("F2"))));
                    }
        }
        finally { AeroModel.ClampNormalForcePastStall = clampWas; }
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (run with SPINBAL=1, SPINBAL_ID=pitts-s2b-like by default). The idle upright spin and what holds it there,
/// measured the same way on any branch: the developed spin (α, pitch, rates, s/turn, ft/turn), PARE recovery in turns, the
/// tail's dynamic-pressure ratio and the elevator's and rudder's moment authority at α 30/45/60/75, and the pitch budget in
/// the developed spin (aerodynamic M against the inertial (I_zz − I_xx)·p·r). Owner 2026-11-03: "the pitts is still
/// spinning too flat … the elevator control is also weak at high aoa".
/// </summary>
public class SpinBalanceProbe
{
    private readonly ITestOutputHelper _out;
    public SpinBalanceProbe(ITestOutputHelper o) { _out = o; }

    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load(string id)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    private static double Alpha(RigidBodyState s)
    {
        Vec3 air = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
        return Math.Atan2(air.Z, air.X) * D;
    }

    private static double OmegaVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }

    /// <summary>Stall, then full aft stick and full rudder at idle; returns the aircraft after <paramref name="spinS"/> s.</summary>
    public static Aircraft EnterSpin(AircraftConfig cfg, double rudder, double lever, double spinS,
                                     Action<double, Aircraft>? each = null)
    {
        double v = ThermalSpawn.MinSinkSpeed(cfg, 2500);
        var t0 = TrimSolver.SolveGliderTrim(cfg, v, 2500, idleProp: true);
        var att = new Quat(0, Math.Sin(t0.ThetaRad / 2), 0, Math.Cos(t0.ThetaRad / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad)), Vec3.Zero),
                              new ControlDeflections(0, t0.ElevatorRad, 0, 0));
        double stallDeg = StallAuthorityTests.StallDeg(cfg), h = SimLoop.DefaultFixedDtSec;
        for (double t = 0; t < 25; t += h) { ac.Step(new ControlInputs(0, -1, 0, lever), h); if (Alpha(ac.State) > stallDeg + 2) break; }
        var pro = new ControlInputs(0, -1, rudder, lever);
        for (double t = 0; t < spinS; t += h)
        {
            ac.Step(pro, h);
            each?.Invoke(t, ac);
            Relift(ac);
        }
        return ac;
    }

    private static void Relift(Aircraft ac)
    {
        var s = ac.State;
        if (-s.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 2500), s.Attitude, s.Velocity, s.Rates);
    }

    public record SpinStats(double Alpha, double Pitch, double P, double Q, double R, double OmegaVertDeg, double SecPerTurn,
                            double FtPerTurn, double Tas, double Beta, double AlphaMin, double AlphaMax, double PitchMin, double PitchMax);

    /// <summary>Developed spin: 45 s of pro-spin controls, the last 20 measured.</summary>
    public static (SpinStats stats, Aircraft ac) Developed(AircraftConfig cfg, double rudder, double lever = 1.0)
    {
        double net = 0, lost = 0, sa = 0, sp = 0, p = 0, q = 0, r = 0, tas = 0, beta = 0, amin = 999, amax = -999, pmin = 999, pmax = -999; int n = 0;
        double h = SimLoop.DefaultFixedDtSec;
        double zPrev = double.NaN;
        var ac = EnterSpin(cfg, rudder, lever, 45, (t, a) =>
        {
            var s = a.State;
            if (t >= 25)
            {
                double al = Alpha(s), th = FormationPilot.Euler(s.Attitude).Item2 * D; sa += al; sp += th; amin = Math.Min(amin, al); amax = Math.Max(amax, al); pmin = Math.Min(pmin, th); pmax = Math.Max(pmax, th);
                p += s.Rates.X * D; q += s.Rates.Y * D; r += s.Rates.Z * D; tas += s.Velocity.Length;
                beta += Math.Asin(Math.Clamp(s.Velocity.Y / Math.Max(1e-6, s.Velocity.Length), -1, 1)) * D;
                net += OmegaVert(s) * h;
                if (!double.IsNaN(zPrev) && Math.Abs(s.Position.Z - zPrev) < 100) lost += s.Position.Z - zPrev;
                n++;
            }
            zPrev = s.Position.Z;
        });
        double turns = Math.Abs(net) / (2 * Math.PI), dur = n * h;
        return (new SpinStats(sa / n, sp / n, p / n, q / n, r / n, Math.Abs(net) / dur * D, dur / turns, lost * 3.2808 / turns, tas / n, beta / n, amin, amax, pmin, pmax), ac);
    }

    /// <summary>
    /// PARE from the developed spin: power idle, ailerons neutral, full opposite rudder; <paramref name="eleDelayS"/> later the
    /// stick goes to <paramref name="eleTo"/> (0 neutral, +1 full forward). Turns from the rudder input until the vertical
    /// rotation falls below 20 deg/s (or NaN if it has not within 30 s).
    /// </summary>
    /// <summary>Turns to recover: until the vertical rotation is below 20 deg/s, or the wing has unstalled (α under 10°) —
    /// with full forward stick and the rudder still held a Pitts goes straight on into an inverted spin, as the real one does.</summary>
    public static (double turns, double seconds) Recover(AircraftConfig cfg, double rudder, double eleTo, double eleDelayS = 0.5, double lever = 1.0,
                                                         System.Text.StringBuilder? trace = null)
    {
        var (_, ac) = Developed(cfg, rudder, lever);
        double h = SimLoop.DefaultFixedDtSec, net = 0, nextTrace = 0;
        for (double t = 0; t < 30; t += h)
        {
            double ele = t < eleDelayS ? -1 : eleTo;
            ac.Step(new ControlInputs(0, ele, -rudder, lever), h);
            Relift(ac);
            double om = OmegaVert(ac.State);
            net += Math.Abs(om) * h;
            if (trace is not null && t >= nextTrace)
            {
                trace.Append($"[{t:F1}s α{Alpha(ac.State):F0} θ{FormationPilot.Euler(ac.State.Attitude).Item2 * D:F0} Ω{om * D:F0}] ");
                nextTrace += 0.5;
            }
            if (t > 0.3 && (Math.Abs(om) * D < 20 || Alpha(ac.State) < 10)) return (net / (2 * Math.PI), t);
        }
        return (double.NaN, double.NaN);
    }

    private static double RefArea(AircraftConfig c)
    {
        double s = 0; foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing")) foreach (var st in sf.Strips) s += st.Area;
        return s;
    }

    private static double RefChord(AircraftConfig c)
    {
        double s = 0, sc = 0; foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing")) foreach (var st in sf.Strips) { s += st.Area; sc += st.Area * st.Chord; }
        return sc / s;
    }

    /// <summary>Aero moment (body, about the CG) at one state, no flow memory (instantaneous separation).</summary>
    private static Vec3 Moment(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables, Vec3 v, Vec3 w, double ele, double rud, bool[]? mask = null)
        => AeroModel.Compute(cfg, tables, v, w, Vec3.Zero, 1.0, new ControlDeflections(0, ele, rud, 0), surfaceMask: mask).Moment;

    /// <summary>Area-weighted η = q_local/q∞ of the horizontal tail strips, from AeroModel's own tailflow record (stab, then elevator).</summary>
    private static (double stab, double elev) TailEta(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables, Vec3 v, Vec3 w)
    {
        ForceDebug.Samples = new List<ForceSample>();
        try { AeroModel.Compute(cfg, tables, v, w, Vec3.Zero, 1.0, ControlDeflections.Neutral); }
        finally { }
        var tf = ForceDebug.Samples.Where(s => s.Kind == "tailflow").ToList();
        ForceDebug.Samples = null;
        int k = 0; double sa = 0, sq = 0, ea = 0, eq = 0;
        foreach (var sf in cfg.Surfaces)
        {
            bool tail = !sf.Id.Contains("wing") && !TailWakeVertical(sf.Id);
            foreach (var st in sf.Strips)
            {
                if (!tail || st.Pos[0] >= -1.0) continue;
                double eta = tf[k++].MomentBody.Z;
                if (sf.Id == "hStab") { sa += st.Area; sq += st.Area * eta; } else if (sf.Id == "elevator") { ea += st.Area; eq += st.Area * eta; }
            }
        }
        return (sq / sa, eq / ea);
    }

    private static bool TailWakeVertical(string id) => id.Contains("vstab", StringComparison.OrdinalIgnoreCase) || id.Contains("vertical", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fin and rudder q ratio: their side force with the wings and stab present over that with them removed.</summary>
    private static (double fin, double rud) FinRatio(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables, Vec3 v, Vec3 w, double rudRad)
    {
        double Side(bool[]? mask, string id)
        {
            var only = new bool[cfg.Surfaces.Count];
            for (int i = 0; i < only.Length; i++) only[i] = cfg.Surfaces[i].Id == id || (mask is null && (cfg.Surfaces[i].Id.Contains("wing") || cfg.Surfaces[i].Id is "hStab" or "elevator"));
            return AeroModel.Compute(cfg, tables, v, w, Vec3.Zero, 1.0, new ControlDeflections(0, 0, rudRad, 0), surfaceMask: only).Force.Y
                 - (mask is null ? AeroModel.Compute(cfg, tables, v, w, Vec3.Zero, 1.0, new ControlDeflections(0, 0, rudRad, 0), surfaceMask: OnlyWingsAndStab(cfg)).Force.Y : 0);
        }
        double fin = Side(null, "vStab") / Side(new bool[0], "vStab");
        double rud = Side(null, "rudder-vstab") / Side(new bool[0], "rudder-vstab");
        return (fin, rud);
    }

    private static bool[] OnlyWingsAndStab(AircraftConfig cfg)
    {
        var m = new bool[cfg.Surfaces.Count];
        for (int i = 0; i < m.Length; i++) m[i] = cfg.Surfaces[i].Id.Contains("wing") || cfg.Surfaces[i].Id is "hStab" or "elevator";
        return m;
    }

    [Fact]
    public void Run()
    {
        if (Environment.GetEnvironmentVariable("SPINBAL") == null) return;
        string id = Environment.GetEnvironmentVariable("SPINBAL_ID") ?? "pitts-s2b-like";
        string parts = Environment.GetEnvironmentVariable("SPINBAL_PARTS") ?? "spin,recover,table,budget";
        AircraftConfig cfg = Load(id);
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double S = RefArea(cfg), c = RefChord(cfg), eMax = cfg.Controls.Elevator.MaxDeflRad, rMax = cfg.Controls.Rudder.MaxDeflRad;
        _out.WriteLine($"{id}: S {S:F2} m², c {c:F3} m, elevator ±{eMax * D:F1}°, rudder ±{rMax * D:F1}°, I {cfg.Mass.Inertia.Ixx}/{cfg.Mass.Inertia.Iyy}/{cfg.Mass.Inertia.Izz}");

        if (parts.Contains("spin"))
            foreach (double rud in new[] { -1.0, 1.0 })
            {
                var (st, _) = Developed(cfg, rud);
                _out.WriteLine($"SPIN rud {rud,2}: α {st.Alpha,5:F1}  pitch {st.Pitch,6:F1}  β {st.Beta,5:F1}  p/q/r {st.P,5:F0}/{st.Q,4:F0}/{st.R,5:F0} deg/s  Ωvert {st.OmegaVertDeg,4:F0} deg/s  {st.SecPerTurn:F2} s/turn  {st.FtPerTurn:F0} ft/turn  TAS {st.Tas:F1} m/s  α {st.AlphaMin:F0}..{st.AlphaMax:F0} θ {st.PitchMin:F0}..{st.PitchMax:F0}");
            }

        if (parts.Contains("power"))
            foreach (double lever in new[] { 0.2, -1.0 })
                foreach (double rud in new[] { -1.0, 1.0 })
                {
                    var (st, _) = Developed(cfg, rud, lever);
                    _out.WriteLine($"POWER lever {lever,4} rud {rud,2}: α {st.Alpha,5:F1}  pitch {st.Pitch,6:F1}  Ωvert {st.OmegaVertDeg,4:F0} deg/s  {st.SecPerTurn:F2} s/turn  {st.FtPerTurn:F0} ft/turn  α {st.AlphaMin:F0}..{st.AlphaMax:F0}");
                }

        if (parts.Contains("recover"))
            foreach (double rud in new[] { -1.0, 1.0 })
                foreach (double eleTo in new[] { 0.0, 1.0 })
                {
                    var tr = new System.Text.StringBuilder();
                    var (turns, secs) = Recover(cfg, rud, eleTo, trace: tr);
                    _out.WriteLine($"PARE rud {rud,2} stick→{(eleTo == 0 ? "neutral" : "full fwd")}: {turns:F2} turns, {secs:F1} s");
                    if (Environment.GetEnvironmentVariable("SPINBAL_TRACE") != null) _out.WriteLine("   " + tr.ToString(0, Math.Min(tr.Length, 900)));
                }

        if (parts.Contains("table"))
        {
            // Elevator sign: whichever deflection gives the more nose-up moment at α 0 is "up".
            double V = 30;
            Vec3 v0 = new(V, 0, 0);
            double sUp = Moment(cfg, tables, v0, Vec3.Zero, -eMax, 0).Y > Moment(cfg, tables, v0, Vec3.Zero, eMax, 0).Y ? -1 : 1;
            double qS = 0.5 * 1.0 * V * V * S;
            double CmDe0 = (Moment(cfg, tables, v0, Vec3.Zero, sUp * eMax, 0).Y - Moment(cfg, tables, v0, Vec3.Zero, -sUp * eMax, 0).Y) / (qS * c);
            double CnDr0 = (Moment(cfg, tables, v0, Vec3.Zero, 0, rMax).Z - Moment(cfg, tables, v0, Vec3.Zero, 0, -rMax).Z) / (qS * Span(cfg));
            _out.WriteLine($"α 0: ΔCm(full up − full down) {CmDe0:F3}   ΔCn(rudder ±) {CnDr0:F4}");
            _out.WriteLine("  α | η stab | η elev | q fin | q rud | Cm neutral | Cm full up | ΔCm elev (×α0) | ΔCn rud (×α0)");
            foreach (double aDeg in new[] { 0.0, 15, 30, 45, 60, 75 })
            {
                double a = aDeg / D;
                Vec3 v = new(V * Math.Cos(a), 0, V * Math.Sin(a));
                var (es, ee) = TailEta(cfg, tables, v, Vec3.Zero);
                var (qf, qr) = FinRatio(cfg, tables, v, Vec3.Zero, rMax);
                double cmN = Moment(cfg, tables, v, Vec3.Zero, 0, 0).Y / (qS * c);
                double cmU = Moment(cfg, tables, v, Vec3.Zero, sUp * eMax, 0).Y / (qS * c);
                double dCm = cmU - Moment(cfg, tables, v, Vec3.Zero, -sUp * eMax, 0).Y / (qS * c);
                double dCn = (Moment(cfg, tables, v, Vec3.Zero, 0, rMax).Z - Moment(cfg, tables, v, Vec3.Zero, 0, -rMax).Z) / (0.5 * V * V * S * Span(cfg));
                _out.WriteLine($" {aDeg,2:F0} | {es,6:F3} | {ee,6:F3} | {qf,5:F3} | {qr,5:F3} | {cmN,10:F3} | {cmU,10:F3} | {dCm,7:F3} ({dCm / CmDe0,4:F2}) | {dCn,7:F4} ({dCn / CnDr0,4:F2})");
            }
        }

        if (parts.Contains("budget"))
            foreach (double rud in new[] { -1.0, 1.0 })
            {
                // The developed spin's own state, sampled over 2 s: aero pitching moment at the three stick positions, the
                // inertial pitch couple, and the tail's η there.
                                double mN = 0, mU = 0, mD = 0, inert = 0, ixz = 0, etaS = 0, etaE = 0, nPro = 0, nAnti = 0, qbar = 0, mTailOff = 0; int n = 0;
                var mI = cfg.Mass.Inertia;
                var tailOff = new bool[cfg.Surfaces.Count];
                for (int i = 0; i < tailOff.Length; i++) tailOff[i] = !(cfg.Surfaces[i].Id is "hStab" or "elevator");
                EnterSpin(cfg, rud, 1.0, 47, (t, a) =>
                {
                    if (t < 45) return;
                    Vec3 w = a.State.Rates, v = a.State.Velocity;
                    mU += Moment(cfg, tables, v, w, sUpOf(cfg, tables) * eMax, -rud * 0 + rud * rMax * RudSign(cfg, tables)).Y;
                    mN += Moment(cfg, tables, v, w, 0, rud * rMax * RudSign(cfg, tables)).Y;
                    mD += Moment(cfg, tables, v, w, -sUpOf(cfg, tables) * eMax, rud * rMax * RudSign(cfg, tables)).Y;
                    mTailOff += Moment(cfg, tables, v, w, sUpOf(cfg, tables) * eMax, rud * rMax * RudSign(cfg, tables), tailOff).Y;
                    nPro += Moment(cfg, tables, v, w, sUpOf(cfg, tables) * eMax, rud * rMax * RudSign(cfg, tables)).Z;
                    nAnti += Moment(cfg, tables, v, w, sUpOf(cfg, tables) * eMax, -rud * rMax * RudSign(cfg, tables)).Z;
                    inert += (mI.Izz - mI.Ixx) * w.X * w.Z;     // pitch acceleration term the aero must balance: (Izz−Ixx)pr
                    ixz += mI.Ixz * (w.Z * w.Z - w.X * w.X);
                    var (es, ee) = TailEta(cfg, tables, v, w); etaS += es; etaE += ee;
                    qbar += 0.5 * v.Length * v.Length;
                    n++;
                });
                double qSc = qbar / n * S * c;
                _out.WriteLine($"BUDGET rud {rud,2}: aero M (N·m, Cm) stick full aft {mU / n,7:F0} ({mU / n / qSc:F3})  neutral {mN / n,7:F0} ({mN / n / qSc:F3})  full fwd {mD / n,7:F0} ({mD / n / qSc:F3})  no h-tail {mTailOff / n,7:F0}");
                _out.WriteLine($"              inertial (Izz−Ixx)·p·r {inert / n,7:F0}  Ixz(r²−p²) {ixz / n,6:F0}   η stab {etaS / n:F3} elev {etaE / n:F3}   N rudder pro {nPro / n,6:F0} anti {nAnti / n,6:F0}");
            }
    }

    private static double Span(AircraftConfig c)
    {
        double lo = 0, hi = 0; foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing")) foreach (var st in sf.Strips) { lo = Math.Min(lo, st.Pos[1]); hi = Math.Max(hi, st.Pos[1]); }
        return hi - lo;
    }

    private static double sUpOf(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables)
    {
        double e = cfg.Controls.Elevator.MaxDeflRad; Vec3 v0 = new(30, 0, 0);
        return Moment(cfg, tables, v0, Vec3.Zero, -e, 0).Y > Moment(cfg, tables, v0, Vec3.Zero, e, 0).Y ? -1 : 1;
    }

    /// <summary>Rudder deflection sign that a positive pedal (right) produces: + if +δr yaws the nose right (+N).</summary>
    private static double RudSign(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables)
    {
        double r = cfg.Controls.Rudder.MaxDeflRad; Vec3 v0 = new(30, 0, 0);
        return Moment(cfg, tables, v0, Vec3.Zero, 0, r).Z > 0 ? 1 : -1;
    }
}

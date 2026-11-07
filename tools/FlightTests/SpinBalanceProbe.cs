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
                                     Action<double, Aircraft>? each = null, bool inverted = false, double aileron = 0)
    {
        double v = ThermalSpawn.MinSinkSpeed(cfg, 2500);
        var t0 = TrimSolver.SolveGliderTrim(cfg, v, 2500, idleProp: true);
        var att = new Quat(0, Math.Sin(t0.ThetaRad / 2), 0, Math.Cos(t0.ThetaRad / 2));
        Vec3 vb = new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad));
        if (inverted)
        {
            // Inverted, level, at the same speed: rolled 180° about the body x axis, flying at the mirror of the trim α.
            att = Quat.Multiply(att, new Quat(1, 0, 0, 0));
            vb = new Vec3(vb.X, 0, -vb.Z);
        }
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, vb, Vec3.Zero),
                              new ControlDeflections(0, inverted ? 0 : t0.ElevatorRad, 0, 0));
        double stallDeg = StallAuthorityTests.StallDeg(cfg), h = SimLoop.DefaultFixedDtSec, stick = inverted ? 1 : -1;
        for (double t = 0; t < 25; t += h) { ac.Step(new ControlInputs(0, stick, 0, lever), h); if (stick * Alpha(ac.State) < -(stallDeg + 2)) break; }
        var pro = new ControlInputs(aileron, stick, rudder, lever);
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
    public static (SpinStats stats, Aircraft ac) Developed(AircraftConfig cfg, double rudder, double lever = 1.0, bool inverted = false, double aileron = 0, System.Text.StringBuilder? trace = null)
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
            if (trace is not null && Math.Abs(t / 1.0 - Math.Round(t / 1.0)) < h / 2)
            {
                var e = FormationPilot.Euler(s.Attitude);
                trace.Append($"[{t:F0}s α{Alpha(s):F0} φ{e.Item1 * D:F0} θ{e.Item2 * D:F0} Ω{OmegaVert(s) * D:F0}] ");
            }
        }, inverted, aileron);
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
    private static Vec3 Moment(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables, Vec3 v, Vec3 w, double ele, double rud, bool[]? mask = null, double ail = 0)
        => AeroModel.Compute(cfg, tables, v, w, Vec3.Zero, 1.0, new ControlDeflections(ail, ele, rud, 0), surfaceMask: mask).Moment;

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
        if (Environment.GetEnvironmentVariable("SPINBAL_NOGYRO") != null) cfg.Propulsion!.PropInertia = 0;
        if (Environment.GetEnvironmentVariable("SPINBAL_IP") is string ip) cfg.Propulsion!.PropInertia = double.Parse(ip);
        if (Environment.GetEnvironmentVariable("SPINBAL_SLIPK") is string slipK) cfg.Propulsion!.SlipstreamK = double.Parse(slipK);
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

        if (parts.Contains("grid"))
            foreach (bool inv in (Environment.GetEnvironmentVariable("SPINBAL_INV") ?? "0,1").Split(',').Select(x => x == "1"))
                foreach (double lever in new[] { 1.0, 0.2, -1.0 })
                    foreach (double rud in new[] { -1.0, 1.0 })
                    {
                        var (st, _) = Developed(cfg, rud, lever, inv);
                        _out.WriteLine($"GRID {(inv ? "inverted" : "upright "),-8} {(lever == 1.0 ? "idle" : lever == 0.2 ? "40%" : "full"),-4} {(rud < 0 ? "left " : "right")}: α {st.Alpha,6:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})  pitch {st.Pitch,6:F1}  Ωvert {st.OmegaVertDeg,4:F0}  {st.SecPerTurn:F2} s/turn  {st.FtPerTurn:F0} ft/turn  TAS {st.Tas:F1}");
                    }

        if (parts.Contains("tybug"))
        {
            // Owner 2026-11-03: inverted, stick full forward, full RIGHT rudder, full RIGHT aileron, full power — "rolls out of
            // the inverted flat into a knife-edge spin or tumbles".
            foreach (double ail in new[] { 0.0, 1.0, -1.0 })
            {
                var tr = new System.Text.StringBuilder();
                var (st, _) = Developed(cfg, 1.0, -1.0, true, ail, tr);
                _out.WriteLine($"TYBUG inverted full right rudder, aileron {ail,2}: α {st.Alpha,6:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})  pitch {st.Pitch:F1} ({st.PitchMin:F0}..{st.PitchMax:F0})  Ωvert {st.OmegaVertDeg:F0}  {st.SecPerTurn:F2} s/turn");
                _out.WriteLine("   " + tr.ToString(0, Math.Min(tr.Length, 1400)));
            }
        }

        if (parts.Contains("rollbudget"))
        {
            // The rolling moment AS FLOWN (the aircraft's own force capture, last RK4 stage of each step): wing lift/drag by
            // wing, the tail, the separated-wing sideslip roll (a pure X moment at the CG), fuselage and its damping, thrust
            // (torque) and the gyro (a breakdown of thrust's moment, not added again).
            double ail = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_AIL") ?? "0");
            double rud = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_RUD") ?? "1");
            double lev = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_LEVER") ?? "-1");
            bool inv = (Environment.GetEnvironmentVariable("SPINBAL_INVB") ?? "1") == "1";
            double t0 = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_T0") ?? "4"), t1 = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_T1") ?? "20");
            var mI = cfg.Mass.Inertia; double next = t0; Vec3 cg = cfg.Mass.CgVec();
            _out.WriteLine($"ROLLBUDGET inv {inv} rud {rud} lever {lev} ail {ail}: Mx in N·m (+ = right wing down, body)");
            _out.WriteLine("   t     α     β     φ     p     r |  wingU-L wingU-R wingL-L wingL-R  tail | slipRoll damp  fus | thrust (gyro) | aeroΣ  inertial");
            EnterSpin(cfg, rud, lev, t1, (t, a) =>
            {
                a.CaptureForces = true;
                if (t < next) return;
                next += 0.5;
                var s = a.State; Vec3 v = s.Velocity, w = s.Rates;
                double uL = 0, uR = 0, lL = 0, lR = 0, tail = 0, slip = 0, damp = 0, fus = 0, thr = 0, gy = 0;
                double nW = 0, nH = 0, nV = 0, nF = 0, nMom = 0, nThr = 0, nGy = 0, mGy = 0;
                foreach (var f in a.LastForces)
                {
                    Vec3 r = f.PosBody - cg;
                    double mx = r.Y * f.ForceBody.Z - r.Z * f.ForceBody.Y + f.MomentBody.X;
                    double nz = r.X * f.ForceBody.Y - r.Y * f.ForceBody.X + f.MomentBody.Z;
                    if (f.Kind is "lift" or "drag")
                    {
                        if (f.PosBody.X >= -1.5) nW += nz;
                        else if (Math.Abs(f.ForceBody.Y) > Math.Abs(f.ForceBody.Z)) nV += nz; else nH += nz;
                    }
                    else if (f.Kind == "fuselage") nF += nz;
                    else if (f.Kind == "moment") nMom += nz;
                    else if (f.Kind == "thrust") nThr += nz;
                    else if (f.Kind == "gyro") { nGy += nz; mGy += f.MomentBody.Y; }
                    switch (f.Kind)
                    {
                        case "lift": case "drag":
                            if (f.PosBody.X < -1.5) tail += mx;
                            else if (f.PosBody.Z < -0.2) { if (f.PosBody.Y < 0) uL += mx; else uR += mx; }
                            else { if (f.PosBody.Y < 0) lL += mx; else lR += mx; }
                            break;
                        case "moment":
                            if (f.MomentBody.X != 0 && f.MomentBody.Y == 0 && f.MomentBody.Z == 0) slip += mx; else if (f.MomentBody.X != 0) damp += mx;
                            break;
                        case "fuselage": fus += mx; break;
                        case "thrust": thr += mx; break;
                        case "gyro": gy += mx; break;
                    }
                }
                double aero = uL + uR + lL + lR + tail + slip + damp + fus;
                double inertial = (mI.Iyy - mI.Izz) * w.Y * w.Z + mI.Ixz * w.X * w.Y;
                double beta = Math.Asin(Math.Clamp(v.Y / v.Length, -1, 1)) * D;
                var e = FormationPilot.Euler(s.Attitude);
                _out.WriteLine($" {t,4:F1} {Alpha(s),5:F0} {beta,5:F0} {e.Item1 * D,5:F0} {w.X * D,5:F0} {w.Z * D,5:F0} | {uL,7:F0} {uR,7:F0} {lL,7:F0} {lR,7:F0} {tail,5:F0} | {slip,8:F0} {damp,4:F0} {fus,4:F0} | {thr,6:F0} ({gy,4:F0}) | {aero,5:F0} {inertial,7:F0}");
                double nInert = (mI.Ixx - mI.Iyy) * w.X * w.Y;
                _out.WriteLine($"        q {w.Y * D,5:F0}  YAW wings {nW,6:F0} htail {nH,5:F0} fin+rud {nV,6:F0} fus {nF,6:F0} moments {nMom,5:F0} thrust {nThr,6:F0} (gyro yaw {nGy,6:F0}, gyro pitch {mGy,6:F0})  inertial (Ixx−Iyy)pq {nInert,6:F0}");
            }, inv, ail);
        }

        if (parts.Contains("flatgrid"))
        {
            // Run #4 (owner 2026-11-03, build 1622): "the inverted flat isn't quite flat enough now and full opposite aileron
            // actually reduces the spin rate and lowers the nose". Each spin with ailerons neutral, then with full aileron
            // AGAINST the developed roll rate (out-spin: right stick in an upright left spin, and in an inverted spin the stick
            // toward the rudder, since there p and r have opposite signs), then with it.
            string levers = Environment.GetEnvironmentVariable("SPINBAL_LEVERS") ?? "1,0.2,-1";
            foreach (bool inv in (Environment.GetEnvironmentVariable("SPINBAL_INV") ?? "0,1").Split(',').Select(x => x == "1"))
                foreach (double lever in levers.Split(',').Select(double.Parse))
                    foreach (double rud in new[] { -1.0, 1.0 })
                    {
                        var (st, _) = Developed(cfg, rud, lever, inv);
                        double opp = inv ? rud : -rud;   // out-spin: against the roll (upright against the rudder, inverted with it)
                        string L(string tag, SpinStats x) => $"{tag}: α {x.Alpha,6:F1} ({x.AlphaMin,4:F0}..{x.AlphaMax,4:F0}) θ {x.Pitch,6:F1} Ω {x.OmegaVertDeg,4:F0} {x.SecPerTurn,4:F2} s/t p/r {x.P,4:F0}/{x.R,4:F0}";
                        string line = $"FLAT {(inv ? "inv" : "up "),-3} {(lever == 1.0 ? "idle" : lever == 0.2 ? "40%" : "full"),-4} {(rud < 0 ? "L" : "R")} | " + L("ail 0", st);
                        var (so, _) = Developed(cfg, rud, lever, inv, opp);
                        line += " | " + L($"opp({opp,2})", so);
                        if (Environment.GetEnvironmentVariable("SPINBAL_WITH") != null) { var (sw, _) = Developed(cfg, rud, lever, inv, -opp); line += " | " + L("with", sw); }
                        _out.WriteLine(line);
                    }
        }

        if (parts.Contains("mirror"))
        {
            // Run #4. THE MIRROR TEST. Turn an aeroplane over (180° about its own x axis) and every term of a top/bottom
            // symmetric airframe maps (α, β, p, q, r) → (−α, −β, p, −q, −r) and (L, M, N) → (L, −M, −N): inertia coupling,
            // the prop's gyro (h along +x is unchanged by the flip), its torque and P-factor, every symmetric section. The
            // mirror of an upright LEFT spin (stick aft, left rudder, aileron δa) is an inverted RIGHT spin (stick forward,
            // right rudder, the same δa). So take a developed upright spin, hold both it and its mirror state for 1 s (flow
            // memory and rpm settle), and compare each part's moment: whatever does not mirror is a real asymmetry of the
            // airframe (fin on top, wings above the CG, a cambered section, body camber) or a sign error.
            double ailU = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_AIL") ?? "0");
            double rudU = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_RUD") ?? "-1");
            var mI = cfg.Mass.Inertia; Vec3 cg = cfg.Mass.CgVec(); double h = SimLoop.DefaultFixedDtSec;
            var cfgP = cfg.Fuselage.Crossflow;
            foreach (double lever in (Environment.GetEnvironmentVariable("SPINBAL_LEVERS") ?? "-1,1").Split(',').Select(double.Parse))
            {
                bool srcInv = Environment.GetEnvironmentVariable("SPINBAL_FROMINV") != null;   // source spin inverted (rudder as given)
                var (stU, acU) = Developed(cfg, rudU, lever, srcInv, ailU);
                var sU = acU.State;
                var sI = new RigidBodyState(sU.Position, Quat.Multiply(sU.Attitude, new Quat(1, 0, 0, 0)),
                                            new Vec3(sU.Velocity.X, -sU.Velocity.Y, -sU.Velocity.Z), new Vec3(sU.Rates.X, -sU.Rates.Y, -sU.Rates.Z));
                var dU = acU.CurrentDeflections;
                var pU = Parts(cfg, sU, new ControlInputs(ailU, srcInv ? 1 : -1, rudU, lever), dU, acU);
                var pI = Parts(cfg, sI, new ControlInputs(ailU, srcInv ? -1 : 1, -rudU, lever), new ControlDeflections(dU.AileronRad, -dU.ElevatorRad, -dU.RudderRad, 0), acU, true);
                _out.WriteLine($"MIRROR {(srcInv ? "SOURCE INVERTED (columns: inv | its upright mirror)" : "upright")} rud {rudU} ail {ailU} lever {lever}: α {stU.Alpha:F1} θ {stU.Pitch:F1} Ω {stU.OmegaVertDeg:F0} {stU.SecPerTurn:F2} s/t; state p/q/r {sU.Rates.X * D:F0}/{sU.Rates.Y * D:F0}/{sU.Rates.Z * D:F0} β {Math.Asin(sU.Velocity.Y / sU.Velocity.Length) * D:F1}");
                _out.WriteLine("   part                 |  L up   L inv  ΔL  |  M up  −M inv  ΔM  |  N up  −N inv  ΔN    (Δ = asymmetry, N·m; + M = nose up in the UPRIGHT sense)");
                Vec3 sumU = Vec3.Zero, sumI = Vec3.Zero;
                foreach (var key in pU.Keys.Union(pI.Keys))
                {
                    Vec3 u = pU.GetValueOrDefault(key), i = pI.GetValueOrDefault(key);
                    if (!key.StartsWith("  ")) { sumU += u; sumI += i; }
                    _out.WriteLine($"   {key,-20} | {u.X,6:F0} {i.X,6:F0} {i.X - u.X,5:F0} | {u.Y,6:F0} {-i.Y,6:F0} {-i.Y - u.Y,5:F0} | {u.Z,6:F0} {-i.Z,6:F0} {-i.Z - u.Z,5:F0}");
                }
                _out.WriteLine($"   {"SUM (aero+prop+inert)",-20} | {sumU.X,6:F0} {sumI.X,6:F0} {sumI.X - sumU.X,5:F0} | {sumU.Y,6:F0} {-sumI.Y,6:F0} {-sumI.Y - sumU.Y,5:F0} | {sumU.Z,6:F0} {-sumI.Z,6:F0} {-sumI.Z - sumU.Z,5:F0}");
            }
        }

        if (parts.Contains("mirrorfly"))
        {
            // Run #4: start AT the mirror of the developed upright spin (stick forward, opposite rudder, same aileron) and fly
            // it on: does the inverted flat hold once it is there (stability), or is it only the entry that never reaches it?
            double ailU = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_AIL") ?? "0");
            double rudU = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_RUD") ?? "-1");
            double h = SimLoop.DefaultFixedDtSec;
            foreach (double lever in (Environment.GetEnvironmentVariable("SPINBAL_LEVERS") ?? "-1").Split(',').Select(double.Parse))
                foreach (double ailI in new[] { ailU, 1.0, -1.0 })
                {
                    bool srcInv = Environment.GetEnvironmentVariable("SPINBAL_FROMINV") != null;
                    var (stU, acU) = Developed(cfg, rudU, lever, srcInv, ailU);
                    var sU = acU.State; var dU = acU.CurrentDeflections;
                    foreach (bool flip in new[] { false, true })
                    {
                        bool inv = flip != srcInv;
                        var st = flip ? new RigidBodyState(sU.Position, Quat.Multiply(sU.Attitude, new Quat(1, 0, 0, 0)),
                                                          new Vec3(sU.Velocity.X, -sU.Velocity.Y, -sU.Velocity.Z), new Vec3(sU.Rates.X, -sU.Rates.Y, -sU.Rates.Z)) : sU;
                        var a = new Aircraft(cfg, st, flip ? new ControlDeflections(dU.AileronRad, -dU.ElevatorRad, -dU.RudderRad, 0) : dU);
                        double ail = ailI;                                          // δa is unchanged by the flip
                        var ci = new ControlInputs(ail, inv ? 1 : -1, flip ? -rudU : rudU, lever);
                        var tr = new System.Text.StringBuilder(); double sa = 0, sw = 0; int n = 0;
                        for (double t = 0; t < 30; t += h)
                        {
                            a.Step(ci, h); Relift(a);
                            if (t > 15) { sa += Alpha(a.State); sw += Math.Abs(OmegaVert(a.State)) * D; n++; }
                            if (Math.Abs(t - Math.Round(t / 2) * 2) < h / 2)
                                tr.Append($"[{t:F0} α{Alpha(a.State):F0} θ{FormationPilot.Euler(a.State.Attitude).Item2 * D:F0} Ω{OmegaVert(a.State) * D:F0}] ");
                        }
                        _out.WriteLine($"MFLY lever {lever} {(inv ? "INVERTED" : "upright ")} ail {ail,2}: 15–30 s α {sa / n:F1} Ω {sw / n:F0}   {tr}");
                    }
                    if (ailI == ailU && ailU != 0) break;
                }
        }

        if (parts.Contains("aildelta"))
        {
            // Run #4: what full aileron does at the developed spin's own state, part by part: Δ(L, M, N) of full out-spin
            // aileron (upright: against the rudder; inverted: with it, i.e. against the roll) over ailerons neutral.
            foreach (bool inv in (Environment.GetEnvironmentVariable("SPINBAL_INV") ?? "0,1").Split(',').Select(x => x == "1"))
                foreach (double lever in (Environment.GetEnvironmentVariable("SPINBAL_LEVERS") ?? "1,0.2").Split(',').Select(double.Parse))
                {
                    double rud = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_RUD") ?? (inv ? "1" : "-1"));
                    var (st0, ac) = Developed(cfg, rud, lever, inv);
                    double opp = inv ? rud : -rud;
                    var d0 = ac.CurrentDeflections; double el = inv ? 1 : -1;
                    var p0 = Parts(cfg, ac.State, new ControlInputs(0, el, rud, lever), d0, ac);
                    var p1 = Parts(cfg, ac.State, new ControlInputs(opp, el, rud, lever), new ControlDeflections(opp * cfg.Controls.Aileron.MaxDeflRad, d0.ElevatorRad, d0.RudderRad, 0), ac);
                    var w = ac.State.Rates;
                    _out.WriteLine($"AILDELTA {(inv ? "inv" : "up ")} lever {lever} rud {rud} ail {opp}: α {st0.Alpha:F1} θ {st0.Pitch:F1} Ω {st0.OmegaVertDeg:F0}; p/q/r {w.X * D:F0}/{w.Y * D:F0}/{w.Z * D:F0}  (pro-spin roll sign {Math.Sign(w.X)}, pro yaw {Math.Sign(w.Z)})");
                    foreach (var key in p0.Keys.Union(p1.Keys))
                    {
                        Vec3 d = p1.GetValueOrDefault(key) - p0.GetValueOrDefault(key);
                        if (d.Length > 5) _out.WriteLine($"   {key,-20} ΔL {d.X,6:F0}  ΔM {d.Y,6:F0}  ΔN {d.Z,6:F0}");
                    }
                }
        }

        if (parts.Contains("ailbal"))
        {
            // Run #4: the developed spin with ailerons neutral and with full out-spin aileron HELD (two equilibria), each part's
            // moment at each and the change: how the new balance is reached (inertial pq yaw, sideslip roll, rate).
            foreach (bool inv in (Environment.GetEnvironmentVariable("SPINBAL_INV") ?? "0,1").Split(',').Select(x => x == "1"))
                foreach (double lever in (Environment.GetEnvironmentVariable("SPINBAL_LEVERS") ?? "1").Split(',').Select(double.Parse))
                {
                    double rud = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_RUD") ?? (inv ? "1" : "-1"));
                    double opp = inv ? rud : -rud, el = inv ? 1 : -1;
                    var (s0, a0) = Developed(cfg, rud, lever, inv);
                    var (s1, a1) = Developed(cfg, rud, lever, inv, opp);
                    var p0 = Parts(cfg, a0.State, new ControlInputs(0, el, rud, lever), a0.CurrentDeflections, a0);
                    var p1 = Parts(cfg, a1.State, new ControlInputs(opp, el, rud, lever), a1.CurrentDeflections, a1);
                    _out.WriteLine($"AILBAL {(inv ? "inv" : "up ")} lever {lever} rud {rud}: ail 0 α {s0.Alpha:F1} θ {s0.Pitch:F1} β {s0.Beta:F1} p/q/r {s0.P:F0}/{s0.Q:F0}/{s0.R:F0} Ω {s0.OmegaVertDeg:F0}  →  ail {opp}: α {s1.Alpha:F1} θ {s1.Pitch:F1} β {s1.Beta:F1} p/q/r {s1.P:F0}/{s1.Q:F0}/{s1.R:F0} Ω {s1.OmegaVertDeg:F0}");
                    foreach (var key in p0.Keys.Union(p1.Keys))
                    {
                        Vec3 u = p0.GetValueOrDefault(key), v = p1.GetValueOrDefault(key);
                        _out.WriteLine($"   {key,-20} L {u.X,6:F0} → {v.X,6:F0}   M {u.Y,6:F0} → {v.Y,6:F0}   N {u.Z,6:F0} → {v.Z,6:F0}");
                    }
                }
        }

        if (parts.Contains("exp"))
            foreach (var (inv, rud, ail) in new[] { (true, 1.0, 1.0), (true, 1.0, 0.0), (false, 1.0, 0.0), (false, -1.0, 0.0) })
            {
                var (st, _) = Developed(cfg, rud, -1.0, inv, ail);
                _out.WriteLine($"EXP {(inv ? "inv" : "up "),-3} full {(rud < 0 ? "L" : "R")} ail {ail,2}: α {st.Alpha,6:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})  pitch {st.Pitch:F0}  {st.SecPerTurn:F2} s/turn");
            }

        if (parts.Contains("ailsym"))
        {
            // Owner 2026-11-03: "look for asymmetries in how aileron power is calculated with positive and negative angle of
            // attack". Rolling moment of full aileron (half the +/− difference) against α, static, β 0, no rotation; the mirror
            // column is the same at −α. For a symmetric section the two should match about the zero-lift line.
            double am = cfg.Controls.Aileron.MaxDeflRad, V = 40;
            int nStrips = cfg.Surfaces.Sum(x => x.Strips.Count);
            int[] pair = (Environment.GetEnvironmentVariable("SPINBAL_PAIR") ?? "8,17").Split(',').Select(int.Parse).ToArray();
            var pairMask = new bool[nStrips]; foreach (int k in pair) pairMask[k] = true;
            double L(double aDeg, double da, bool[]? sm, bool deep)
            {
                double a = aDeg / D; Vec3 v = new(V * Math.Cos(a), 0, V * Math.Sin(a));
                StripFlowState? fs = null;
                if (deep) { fs = new StripFlowState(); fs.EnsureSize(nStrips); for (int i = 0; i < nStrips; i++) fs.Separation[i] = Math.Abs(aDeg) > 20 ? 1 : 0; }
                return AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.0, new ControlDeflections(da, 0, 0, 0), flowState: fs, stripMask: sm).Moment.X;
            }
            double qSb = 0.5 * V * V * S * Span(cfg);
            _out.WriteLine("AILSYM ΔCl = (L(+δa) − L(−δa))/2 / (q S b)   [pair = one aileron section, strips " + string.Join("+", pair) + "]");
            _out.WriteLine("    α |  whole(α)  whole(−α) | pair(α)   pair(−α) | whole deep(α) deep(−α) | pair deep(α) deep(−α)");
            for (int aDeg = 0; aDeg <= 120; aDeg += 5)
            {
                double W(double x, bool deep) => (L(x, am, null, deep) - L(x, -am, null, deep)) / 2 / qSb;
                double P(double x, bool deep) => (L(x, am, pairMask, deep) - L(x, -am, pairMask, deep)) / 2 / qSb;
                _out.WriteLine($"  {aDeg,3} | {W(aDeg, false),8:F5} {W(-aDeg, false),9:F5} | {P(aDeg, false),8:F5} {P(-aDeg, false),9:F5} | {W(aDeg, true),9:F5} {W(-aDeg, true),9:F5} | {P(aDeg, true),9:F5} {P(-aDeg, true),9:F5}");
            }
        }

        if (parts.Contains("ailpower"))
            foreach (bool inv in new[] { false, true })
                foreach (double lever in new[] { 1.0, -1.0 })
                    foreach (double rud in new[] { -1.0, 1.0 })
                    {
                        // Aileron power in the developed spin: the rolling moment (and yaw) of full aileron either way at the spin's
                        // own state, no flow memory; and the spin it leads to when held.
                        var (st0, ac) = Developed(cfg, rud, lever, inv);
                        var s = ac.State; double am = cfg.Controls.Aileron.MaxDeflRad;
                        Vec3 m0 = Moment(cfg, tables, s.Velocity, s.Rates, 0, 0, null, 0);
                        Vec3 mP = Moment(cfg, tables, s.Velocity, s.Rates, 0, 0, null, am), mN = Moment(cfg, tables, s.Velocity, s.Rates, 0, 0, null, -am);
                        double qSb = 0.5 * s.Velocity.Length * s.Velocity.Length * S * Span(cfg);
                        double qSb0 = 0.5 * 50.0 * 50.0 * S * Span(cfg);
                        Vec3 c0P = Moment(cfg, tables, new Vec3(50, 0, 0), Vec3.Zero, 0, 0, null, am), c0N = Moment(cfg, tables, new Vec3(50, 0, 0), Vec3.Zero, 0, 0, null, -am);
                        double clSpin = (mP.X - mN.X) / qSb, cl0 = (c0P.X - c0N.X) / qSb0;
                        string line = $"AIL {(inv ? "inv" : "up "),-3} {(lever == 1.0 ? "idle" : "full"),-4} {(rud < 0 ? "L" : "R")}: α {st0.Alpha,6:F1}  ΔCl(+δa − −δa) spin {clSpin:F4} vs α0 {cl0:F4} ({clSpin / cl0:F2})  ΔCn {(mP.Z - mN.Z) / qSb:F4}";
                        foreach (double ail in new[] { 1.0, -1.0 })
                        {
                            var (sa, _) = Developed(cfg, rud, lever, inv, ail);
                            line += $" | ail {ail,2}: α {sa.Alpha:F0} ({sa.AlphaMin:F0}..{sa.AlphaMax:F0}) {sa.SecPerTurn:F2} s/t";
                        }
                        _out.WriteLine(line);
                    }

        if (parts.Contains("abin"))
        {
            // Q007 (owner 2026-11-05): "it should not go more than 95 or 5 nose up and it should not gyrate or roll out of inverted
            // flat right right spin". The flown spin (not a held snapshot), each part's moment averaged over the last 20 s in bins
            // of |α|: what pushes |α| past 90 and what pulls it back, in the spin as flown.
            double ail = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_AIL") ?? "1");
            double rud = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_RUD") ?? "-1");
            double lev = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_LEVER") ?? "-1");
            bool inv = (Environment.GetEnvironmentVariable("SPINBAL_INVB") ?? "0") == "1";
            double[] edges = { 0, 30, 45, 60, 80, 90, 95, 100, 110, 130, 181 };
            double abinT0 = double.Parse(Environment.GetEnvironmentVariable("SPINBAL_ABIN_T0") ?? "25");   // 0: the entry too
            var sums = new Dictionary<string, Vec3>[edges.Length - 1]; var cnt = new int[edges.Length - 1];
            for (int i = 0; i < sums.Length; i++) sums[i] = new();
            var tr = new System.Text.StringBuilder();
            EnterSpin(cfg, rud, lev, 45, (t, a) =>
            {
                a.CaptureForces = true;
                if (t < abinT0 || a.LastForces is null) return;
                double al = Math.Abs(Alpha(a.State)); int b = 0; while (b < cnt.Length - 1 && al >= edges[b + 1]) b++;
                cnt[b]++;
                foreach (var kv in Classify(cfg, a.LastForces, a.State.Rates)) sums[b][kv.Key] = (sums[b].TryGetValue(kv.Key, out var o) ? o : Vec3.Zero) + kv.Value;
                if (Math.Abs(t * 2 - Math.Round(t * 2)) < SimLoop.DefaultFixedDtSec)
                {
                    var e = FormationPilot.Euler(a.State.Attitude); var w = a.State.Rates;
                    tr.Append($"[{t:F1} α{Alpha(a.State):F0} φ{e.Item1 * D:F0} θ{e.Item2 * D:F0} p{w.X * D:F0} q{w.Y * D:F0} r{w.Z * D:F0}] ");
                }
            }, inv, ail);
            _out.WriteLine($"ABIN inv {inv} rud {rud} lever {lev} ail {ail}: mean moments (N·m, body) per |α| bin over {abinT0:F0}..45 s");
            _out.WriteLine("   " + tr.ToString());
            for (int b = 0; b < cnt.Length; b++)
            {
                if (cnt[b] == 0) continue;
                _out.WriteLine($" |α| {edges[b]:F0}..{edges[b + 1]:F0}: {cnt[b] * SimLoop.DefaultFixedDtSec:F1} s");
                foreach (var kv in sums[b].OrderBy(k => k.Key))
                {
                    Vec3 m = kv.Value * (1.0 / cnt[b]);
                    _out.WriteLine($"   {kv.Key,-20} L {m.X,7:F0}  M {m.Y,7:F0}  N {m.Z,7:F0}");
                }
            }
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

    /// <summary>Each part's moment about the CG (body, N·m) at one held state: the aircraft is held there 1 s so flow memory and rpm
    /// settle, then one step is captured. Lift/drag/section moments by wing, h-tail and fin; fuselage terms; prop (as applied, a
    /// moment) with the gyro broken out; and the inertial couples of the rigid-body equations.</summary>
    public static Dictionary<string, Vec3> Parts(AircraftConfig cfg, RigidBodyState st, ControlInputs ci, ControlDeflections d0,
                                                 Aircraft? memFrom = null, bool mirrored = false)
    {
        var mI = cfg.Mass.Inertia; Vec3 cg = cfg.Mass.CgVec(); double h = SimLoop.DefaultFixedDtSec; var cfgP = cfg.Fuselage.Crossflow;
        var a = new Aircraft(cfg, st, d0);
        if (memFrom is not null)
        {
            // The per-strip separation memory is hysteretic (holds between 11° and 16°), so a fresh aircraft would start every
            // strip in that band attached: copy the flown aircraft's, left and right swapped when the state is flipped.
            a.State = st; a.Step(ci, h);
            double[] src = Mem(memFrom).Separation, dst = Mem(a).Separation;
            int[] map = mirrored ? MirrorStrips(cfg) : Enumerable.Range(0, src.Length).ToArray();
            for (int i = 0; i < dst.Length && i < map.Length; i++) dst[i] = src[map[i]];
        }
        for (double t = 0; t < 1.0; t += h) { a.State = st; a.Step(ci, h); }
        a.State = st; a.CaptureForces = true; a.Step(ci, h);
        return Classify(cfg, a.LastForces, st.Rates);
    }

    /// <summary>The captured forces of one step, by part, as moments about the CG (body, N·m), with the rigid-body inertial couples.</summary>
    public static Dictionary<string, Vec3> Classify(AircraftConfig cfg, IEnumerable<ForceSample> forces, Vec3 w)
    {
        var mI = cfg.Mass.Inertia; Vec3 cg = cfg.Mass.CgVec(); var cfgP = cfg.Fuselage.Crossflow;
        var m = new Dictionary<string, Vec3>();
        void Add(string k, Vec3 v) => m[k] = (m.TryGetValue(k, out var o) ? o : Vec3.Zero) + v;
        foreach (var f in forces)
        {
            Vec3 mm = Vec3.Cross(f.PosBody - cg, f.ForceBody) + f.MomentBody;
            switch (f.Kind)
            {
                case "lift": case "drag": case "moment" when (f.PosBody - cg).Length > 1e-9:
                    string k = f.PosBody.X < -1.5 ? (Math.Abs(f.PosBody.Z + 0.071) > 1e-3 ? (f.PosBody.X < -3.2 ? "rudder" : "fin") : f.PosBody.X < -3.2 ? "elevator" : "stab")
                             : f.PosBody.Z < 0 ? "wing upper" : "wing lower";
                    Add(k, mm); break;
                case "moment": Add(f.MomentBody.Y != 0 && f.MomentBody.X == 0 ? "fus Munk" : f.MomentBody.X != 0 && f.MomentBody.Y == 0 && f.MomentBody.Z == 0 ? "sep-wing slip roll" : "damping", mm); break;
                case "fuselage":
                    bool camber = Math.Abs(f.PosBody.X - cfgP.SectionCamberCenterX) < 1e-6 || Math.Abs(f.PosBody.X - cfgP.CowlCamberCenterX) < 1e-6;
                    Add(camber ? "fus camber Cm(β)" : "fus crossflow", mm); break;
                case "thrust": Add("prop (all)", f.MomentBody); break;          // applied as a moment about the CG
                case "gyro": Add("  of which gyro", f.MomentBody); break;
                case "tailflow": case "weight": break;                            // overlay records, not loads
                default: Add(f.Kind, mm); break;
            }
        }
        m["inertial"] = new Vec3((mI.Iyy - mI.Izz) * w.Y * w.Z, (mI.Izz - mI.Ixx) * w.X * w.Z, (mI.Ixx - mI.Iyy) * w.X * w.Y);
        m["inertial Ixz"] = new Vec3(mI.Ixz * w.X * w.Y, -mI.Ixz * (w.X * w.X - w.Z * w.Z), -mI.Ixz * w.Y * w.Z);
        return m;
    }

    private static StripFlowState Mem(Aircraft a) =>
        (StripFlowState)typeof(Aircraft).GetField("_flowState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(a)!;

    /// <summary>For each strip, the index of its lateral mirror (same surface, y → −y).</summary>
    private static int[] MirrorStrips(AircraftConfig cfg)
    {
        var map = new List<int>(); int b = 0;
        foreach (var sf in cfg.Surfaces)
        {
            for (int i = 0; i < sf.Strips.Count; i++)
            {
                int best = i; double bd = double.MaxValue;
                for (int j = 0; j < sf.Strips.Count; j++)
                {
                    double d = Math.Abs(sf.Strips[j].Pos[0] - sf.Strips[i].Pos[0]) + Math.Abs(sf.Strips[j].Pos[1] + sf.Strips[i].Pos[1]);
                    if (d < bd) { bd = d; best = j; }
                }
                map.Add(b + best);
            }
            b += sf.Strips.Count;
        }
        return map.ToArray();
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

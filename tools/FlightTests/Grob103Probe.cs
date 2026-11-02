using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (GROB_PROBE=1): everything the Twin II Acro's validation table needs, printed -- the polar at the two
/// published masses, stall speeds clean and brakes-out, the airbrake glide, the 45-degree brakes-out dive, a 45-45
/// roll reversal, and spins at both ends of the CG range. Grob103Tests asserts the bands; this shows the numbers.
/// </summary>
[Collection("WorldTerrainActive")]
public class Grob103Probe
{
    private readonly ITestOutputHelper _out;
    public Grob103Probe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Everything()
    {
        if (Environment.GetEnvironmentVariable("GROB_PROBE") == null) return;
        foreach ((string name, double front, double rear) in new[] { ("solo 450", 70.0, 0.0), ("solo 470 (default)", 90.0, 0.0), ("dual 580", 100.0, 100.0) })
        {
            AircraftConfig c = Grob103.Loaded(front, rear);
            _out.WriteLine($"== {name}: {c.Mass.MassKg:F0} kg, CG {-c.Mass.Cg[0] * 1000:F0} mm aft of datum");
            var p = Grob103.Polar(c, 0.0);
            _out.WriteLine($"   best L/D {p.BestLd:F1} at {p.BestLdV * 3.6:F1} km/h; min sink {p.MinSink:F3} m/s at {p.MinSinkV * 3.6:F1} km/h");
            foreach (double v in new[] { 70.0, 80, 90, 100, 110, 120, 140, 160, 180, 200 })
            {
                TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v / 3.6, 0.0);
                _out.WriteLine($"     {v,4:F0} km/h  L/D {t.GlideRatio,5:F1}  sink {v / 3.6 / t.GlideRatio,5:F2}  alpha {t.AlphaRad * 57.3,5:F1}  elev {t.ElevatorRad * 57.3,5:F1}  conv {t.Converged}");
            }
            double vs = Grob103.StallSpeed(c, 0.0), vsb = Grob103.StallSpeed(c, 1.0);
            _out.WriteLine($"   stall clean {vs * 3.6:F1} km/h, brakes {vsb * 3.6:F1} km/h");
            var pb = Grob103.Polar(c, 1.0);
            TrimSolver.Result ap = TrimSolver.SolveGliderTrim(c, 95 / 3.6, 0.0, spoilerFraction: 1.0);
            _out.WriteLine($"   full brakes: best L/D {pb.BestLd:F1} at {pb.BestLdV * 3.6:F0} km/h; at 95 km/h L/D {ap.GlideRatio:F1}");
            _out.WriteLine($"   45 deg dive, full brakes: terminal {Grob103.DiveTerminal(c, 45, 1.0) * 3.6:F0} km/h; clean {Grob103.DiveTerminal(c, 45, 0.0) * 3.6:F0} km/h");
            double t45 = Grob103.RollReversal(c, 1.4 * vs, _out);
            _out.WriteLine($"   45-45 reversal at 1.4 Vs ({1.4 * vs * 3.6:F0} km/h): {t45:F2} s");
        }
        foreach ((string name, double front, double rear) in new[] { ("AFT CG solo 70 kg", 70.0, 0.0), ("FWD CG solo 110 kg", 110.0, 0.0), ("dual 100/100", 100.0, 100.0) })
        {
            AircraftConfig c = Grob103.Loaded(front, rear);
            var s = Grob103.Spin(c, +1.0, 12.0, 6.0);
            _out.WriteLine($"== spin {name} (CG {-c.Mass.Cg[0] * 1000:F0} mm): {s.Turns:F2} turns in 12 s, {s.SecPerTurn:F1} s/turn, " +
                           $"{s.HeightPerTurn:F0} m/turn, stalled {s.Stalled:P0}, recovered after {s.RecoveryTurns:F2} turns ({s.RecoverySec:F1} s, recovered {s.Recovered})");
        }
    }
}

/// <summary>Shared Twin II Acro helpers for the probe and the tests.</summary>
public static class Grob103
{
    public const string Id = "glider-grob103-acro-like";

    public static AircraftConfig Load() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", Id + ".json"));

    /// <summary>The config loaded with these seat masses, its mass block rewritten the way the load sheet does it.</summary>
    public static AircraftConfig Loaded(double frontKg, double rearKg)
    {
        AircraftConfig c = Load();
        var load = new AircraftLoad(c);
        load.Set("front", frontKg); load.Set("rear", rearKg);
        MassProperties mp = load.Properties; Vec3 cg = load.Cg;
        c.Mass.MassKg = mp.MassKg; c.Mass.Cg = new[] { cg.X, cg.Y, cg.Z };
        c.Mass.Inertia = new InertiaConfig { Ixx = mp.Ixx, Iyy = mp.Iyy, Izz = mp.Izz, Ixz = mp.Ixz };
        return c;
    }

    public readonly record struct PolarResult(double BestLd, double BestLdV, double MinSink, double MinSinkV);

    /// <summary>Sea-level ISA polar from the trim solver, 0.25 m/s steps.</summary>
    public static PolarResult Polar(AircraftConfig c, double spoiler)
    {
        double best = 0, bestV = 0, minS = 99, minV = 0;
        for (double v = 18; v <= 60; v += 0.25)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 0.0, spoilerFraction: spoiler);
            if (!t.Converged || !(t.GlideRatio > 0)) continue;
            if (t.AlphaRad > StallAlphaBody(c)) continue;
            double sink = v / Math.Sqrt(1 + t.GlideRatio * t.GlideRatio);
            if (t.GlideRatio > best) { best = t.GlideRatio; bestV = v; }
            if (sink < minS) { minS = sink; minV = v; }
        }
        return new PolarResult(best, bestV, minS, minV);
    }

    /// <summary>Body alpha at which the root strips reach the wing table's CLmax.</summary>
    public static double StallAlphaBody(AircraftConfig c)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        StripConfig root = c.Surfaces.Find(s => s.Id == "wing")!.Strips.OrderBy(s => Math.Abs(s.Pos[1])).First();
        return tables[root.Airfoil].AlphaClMaxRad - root.IncidenceRad;
    }

    /// <summary>1-g stall speed (IAS = TAS at sea level): the slowest speed the glider still trims in a steady glide
    /// below its wing's stalling angle, 0.05 m/s resolution.</summary>
    public static double StallSpeed(AircraftConfig c, double spoiler)
    {
        double cap = StallAlphaBody(c);
        double last = double.NaN;
        for (double v = 30; v >= 10; v -= 0.05)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 0.0, spoilerFraction: spoiler);
            if (!t.Converged || !(t.GlideRatio > 0) || t.AlphaRad >= cap) break;
            last = v;
        }
        return last;
    }

    /// <summary>Steady speed in a straight dive at this flight-path angle (deg): weight along the path equals drag at
    /// the trimmed lift W cos(gamma). Sea level.</summary>
    public static double DiveTerminal(AircraftConfig c, double gammaDeg, double spoiler)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double W = c.Mass.MassKg * Atmosphere.GravityMs2, g = gammaDeg * Math.PI / 180.0, rho = Atmosphere.SeaLevelDensityKgM3;
        for (double v = 30; v < 150; v += 0.25)
        {
            // find alpha for lift = W cos g at elevator 0 (moment ignored: the tail's trim drag is small at these speeds)
            double lo = -0.2, hi = 0.3, a = 0;
            for (int i = 0; i < 50; i++)
            {
                a = 0.5 * (lo + hi);
                Vec3 vel = new(v * Math.Cos(a), 0, v * Math.Sin(a));
                (Vec3 f, _) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, spoiler));
                double lift = f.X * Math.Sin(a) - f.Z * Math.Cos(a);
                if (lift > W * Math.Cos(g)) hi = a; else lo = a;
            }
            Vec3 vv = new(v * Math.Cos(a), 0, v * Math.Sin(a));
            (Vec3 ff, _) = AeroModel.Compute(c, tables, vv, Vec3.Zero, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, spoiler));
            double drag = -(ff.X * Math.Cos(a) + ff.Z * Math.Sin(a));
            if (drag >= W * Math.Sin(g)) return v;
        }
        return double.NaN;
    }

    /// <summary>Bank angle from the attitude quaternion (rad, + right wing down).</summary>
    public static double Bank(RigidBodyState s)
    {
        Quat q = s.Attitude;
        return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
    }

    /// <summary>The standard sailplane roll test: established in a 45-degree bank one way at speed V, full opposite
    /// aileron with rudder coordinating (here: rudder held to null the sideslip, the way the test is flown), time to
    /// 45 degrees the other way. Elevator held at the 1-g trim. Sea level.</summary>
    public static double RollReversal(AircraftConfig c, double v, ITestOutputHelper? log = null)
    {
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 300.0);
        double phi0 = -45.0 * Math.PI / 180.0;
        // attitude: bank phi0 about the velocity's own pitch attitude
        Quat pitch = new(0, Math.Sin(t.ThetaRad / 2), 0, Math.Cos(t.ThetaRad / 2));
        Quat roll = new(Math.Sin(phi0 / 2), 0, 0, Math.Cos(phi0 / 2));
        // a steady, coordinated 45-degree turn: the turn rate g tan(phi)/V about the vertical, in body axes
        double psiDot = Atmosphere.GravityMs2 * Math.Tan(phi0) / v;
        Vec3 rates = new(-psiDot * Math.Sin(t.ThetaRad), psiDot * Math.Sin(phi0) * Math.Cos(t.ThetaRad), psiDot * Math.Cos(phi0) * Math.Cos(t.ThetaRad));
        var state = new RigidBodyState(new Vec3(0, 0, -300), Quat.Multiply(pitch, roll),
            new Vec3(v * Math.Cos(t.AlphaRad), 0, v * Math.Sin(t.AlphaRad)), rates);
        var ac = new Aircraft(c, state, new ControlDeflections(0, t.ElevatorRad, 0, 0));
        var sim = new SimLoop(ac);
        double elevCmd = ElevatorCommand(c, t.ElevatorRad);
        double time = 0, dt = 0.02, rud = 0;
        while (time < 15)
        {
            // the pilot's feet: rudder toward the side the air is coming from (beta > 0 = air from the right ->
            // right rudder), integrating, which is how a reversal is coordinated by feel on the yaw string
            double beta = Math.Atan2(ac.State.Velocity.Y, ac.State.Velocity.X);
            rud = Math.Clamp(rud + 8.0 * beta * dt, -1, 1);
            sim.RunFor(dt, new ControlInputs(1.0, elevCmd, rud, 0.0));
            time += dt;
            if (Bank(ac.State) >= 45.0 * Math.PI / 180.0) return time;
        }
        return double.NaN;
    }

    /// <summary>The stick input (-1..1, aft negative) that holds this elevator deflection, ignoring expo near neutral.</summary>
    public static double ElevatorCommand(AircraftConfig c, double elevRad)
    {
        ControlAxisConfig e = c.Controls.Elevator;
        double lim = e.LimitFor(Math.Sign(elevRad));
        return lim > 0 ? Math.Clamp(elevRad / lim, -1, 1) : 0.0;
    }

    public readonly record struct SpinResult(double Turns, double SecPerTurn, double HeightPerTurn, double Stalled,
                                             double RecoveryTurns, double RecoverySec, bool Recovered);

    /// <summary>Spin entry from a trimmed glide: full aft stick and full rudder held for `holdS`, then standard recovery
    /// (full opposite rudder, stick to neutral -- the [FM] recovery: "opposite rudder, stick neutral") until the
    /// rotation stops, up to `recoverMaxS`.</summary>
    public static SpinResult Spin(AircraftConfig c, double rudder, double holdS, double recoverMaxS)
    {
        double v0 = 22.0;
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v0, 600.0);
        double half = t.ThetaRad / 2.0;
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -600), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
            new Vec3(v0 * Math.Cos(t.AlphaRad), 0, v0 * Math.Sin(t.AlphaRad)), Vec3.Zero), new ControlDeflections(0, t.ElevatorRad, 0, 0));
        var sim = new SimLoop(ac);
        double net = 0, dt = 0.05, time = 0; int stalled = 0, n = 0;
        double h0 = -ac.State.Position.Z, hAtSpin = double.NaN, netAtSpin = 0, tAtSpin = 0;
        while (time < holdS)
        {
            sim.RunFor(dt, new ControlInputs(0, -1.0, rudder, 0.0));
            time += dt;
            double om = OmVert(ac.State);
            net += om * dt / (2 * Math.PI);
            double a = Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 180 / Math.PI;
            if (Math.Abs(net) >= 1.0 && double.IsNaN(hAtSpin)) { hAtSpin = -ac.State.Position.Z; netAtSpin = net; tAtSpin = time; }
            if (time > 3) { n++; if (a > 14) stalled++; }
        }
        double turnsDeveloped = Math.Abs(net - netAtSpin);
        double secPerTurn = turnsDeveloped > 0.2 ? (time - tAtSpin) / turnsDeveloped : double.NaN;
        double hPerTurn = turnsDeveloped > 0.2 ? (hAtSpin - (-ac.State.Position.Z)) / turnsDeveloped : double.NaN;
        double dir = Math.Sign(OmVert(ac.State));
        double recNet = 0, recT = 0; bool rec = false;
        while (recT < recoverMaxS)
        {
            sim.RunFor(dt, new ControlInputs(0, 0.0, -dir * Math.Abs(rudder), 0.0));
            recT += dt;
            double om = OmVert(ac.State);
            recNet += om * dt / (2 * Math.PI);
            double a = Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 180 / Math.PI;
            if (Math.Abs(om) < 0.3 && a < 12) { rec = true; break; }
        }
        return new SpinResult(net, secPerTurn, hPerTurn, n > 0 ? (double)stalled / n : 0, Math.Abs(recNet), recT, rec);
    }

    public static double OmVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }
}

[Collection("WorldTerrainActive")]
public class Grob103SpinTrace
{
    private readonly ITestOutputHelper _out;
    public Grob103SpinTrace(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Trace()
    {
        if (Environment.GetEnvironmentVariable("GROB_SPIN") == null) return;
        AircraftConfig c = Grob103.Loaded(70, 0);
        double v0 = 22.0;
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v0, 600.0);
        double half = t.ThetaRad / 2.0;
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -600), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
            new Vec3(v0 * Math.Cos(t.AlphaRad), 0, v0 * Math.Sin(t.AlphaRad)), Vec3.Zero), new ControlDeflections(0, t.ElevatorRad, 0, 0));
        var sim = new SimLoop(ac);
        double rec = Environment.GetEnvironmentVariable("GROB_ELEV") is string e ? double.Parse(e) : 0.0;
        for (int i = 0; i < 400; i++)
        {
            bool recover = i >= 240;
            sim.RunFor(0.05, recover ? new ControlInputs(0, rec, -1.0, 0) : new ControlInputs(0, -1.0, 1.0, 0));
            if (i % 10 == 0)
            {
                RigidBodyState s = ac.State;
                double a = Math.Atan2(s.Velocity.Z, s.Velocity.X) * 57.3, b = Math.Asin(s.Velocity.Y / s.Velocity.Length) * 57.3;
                ControlDeflections d = ac.CurrentDeflections;
                _out.WriteLine($"t {i * 0.05,5:F1} {(recover ? "REC" : "PRO")} V {s.Velocity.Length,5:F1} a {a,6:F1} b {b,6:F1} p {s.Rates.X,5:F2} q {s.Rates.Y,5:F2} r {s.Rates.Z,5:F2} om {Grob103.OmVert(s),5:F2} bank {Grob103.Bank(s) * 57.3,6:F0} rud {d.RudderRad * 57.3,5:F1} elev {d.ElevatorRad * 57.3,5:F1} h {-s.Position.Z,5:F0}");
            }
        }
    }
}

[Collection("WorldTerrainActive")]
public class Grob103Rigging
{
    private readonly ITestOutputHelper _out;
    public Grob103Rigging(ITestOutputHelper o) { _out = o; }

    /// <summary>GROB_RIG=1: the trimmed elevator at the mid-envelope CG (360 mm) and 580 kg, at the published best-glide
    /// speed (105 km/h, CL ~0.6) -- the condition the stabiliser incidence is rigged for.</summary>
    [Fact]
    public void ElevatorAtMidCg()
    {
        if (Environment.GetEnvironmentVariable("GROB_RIG") == null) return;
        AircraftConfig c = Grob103.Load();
        c.Mass.MassKg = 580; c.Mass.Cg = new[] { -0.360, 0, c.Mass.Cg[2] };
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, 105 / 3.6, 0.0);
        _out.WriteLine($"RIG elevator {t.ElevatorRad * 57.2958:F3} deg at alpha {t.AlphaRad * 57.2958:F2}, L/D {t.GlideRatio:F2}");
    }
}

[Collection("WorldTerrainActive")]
public class Grob103RollTrace
{
    private readonly ITestOutputHelper _out;
    public Grob103RollTrace(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Trace()
    {
        if (Environment.GetEnvironmentVariable("GROB_ROLL") == null) return;
        foreach (double front in new[] { 70.0, 90.0 })
        {
            AircraftConfig c = Grob103.Loaded(front, 0);
            double v = 1.4 * Grob103.StallSpeed(c, 0);
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 300.0);
            double phi0 = -45.0 * Math.PI / 180.0;
            Quat pitch = new(0, Math.Sin(t.ThetaRad / 2), 0, Math.Cos(t.ThetaRad / 2));
            Quat roll = new(Math.Sin(phi0 / 2), 0, 0, Math.Cos(phi0 / 2));
            double psiDot = Atmosphere.GravityMs2 * Math.Tan(phi0) / v;
            Vec3 rates = new(-psiDot * Math.Sin(t.ThetaRad), psiDot * Math.Sin(phi0) * Math.Cos(t.ThetaRad), psiDot * Math.Cos(phi0) * Math.Cos(t.ThetaRad));
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -300), Quat.Multiply(pitch, roll),
                new Vec3(v * Math.Cos(t.AlphaRad), 0, v * Math.Sin(t.AlphaRad)), rates), new ControlDeflections(0, t.ElevatorRad, 0, 0));
            var sim = new SimLoop(ac);
            double elevCmd = Grob103.ElevatorCommand(c, t.ElevatorRad), rud = 0;
            _out.WriteLine($"== front {front} kg, V {v * 3.6:F1} km/h, trim elev {t.ElevatorRad * 57.3:F2} -> cmd {elevCmd:F3}");
            for (int i = 0; i < 350; i++)
            {
                double beta = Math.Atan2(ac.State.Velocity.Y, ac.State.Velocity.X);
                rud = Math.Clamp(rud + 8.0 * beta * 0.02, -1, 1);
                sim.RunFor(0.02, new ControlInputs(1.0, elevCmd, rud, 0.0));
                if (i % 25 == 0)
                {
                    RigidBodyState s = ac.State;
                    _out.WriteLine($"t {i * 0.02,4:F2} bank {Grob103.Bank(s) * 57.3,6:F1} p {s.Rates.X * 57.3,6:F1} r {s.Rates.Z * 57.3,6:F1} beta {beta * 57.3,6:F1} rud {rud,5:F2} V {s.Velocity.Length,5:F1} a {Math.Atan2(s.Velocity.Z, s.Velocity.X) * 57.3,5:F1} ail {ac.CurrentDeflections.AileronRad * 57.3,5:F1}");
                }
            }
        }
    }
}

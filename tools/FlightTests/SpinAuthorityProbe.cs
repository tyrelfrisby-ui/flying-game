using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// SPIN_AUTH=1 — elevator and rudder authority IN THE DEVELOPED SPIN, which is what the owner is reporting
/// ("the elevator and rudder authority in the spin are both significantly weaker than the real thing").
///
/// Every authority number measured before this was STATIC: AeroModel.Compute at a frozen α with zero body
/// rates. That is the wrong condition. In a flat spin at 4.5 rad/s the tail sits 4 m aft, so ω×r is about
/// 18 m/s of SIDEWAYS flow against roughly 12 m/s of descent — the horizontal tail is not broadside to its
/// local flow at all, and its local flow barely resembles the free stream. So this flies the aeroplane into
/// its developed spin and perturbs the controls THERE, with the real velocity and the real rates, and reports
/// the answer as an angular acceleration in deg/s² — the thing a pilot actually feels.
/// </summary>
public class SpinAuthorityProbe
{
    private readonly ITestOutputHelper _out;
    public SpinAuthorityProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("SPIN_AUTH") == null) return;
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string id = Environment.GetEnvironmentVariable("SPIN_AUTH_ID") ?? "pitts-s2b-like";
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, -System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        sim.RunFor(10, new ControlInputs(0, -1, -1, 1.0));
        var ci = new ControlInputs(1, -1, -1, -1.0);                       // power-on flat: out-spin aileron, aft, left rudder
        for (int i = 0; i < 500; i++) { sim.RunFor(0.1, ci); Wrap(ac); }

        var st = ac.State; var v = st.Velocity; var om = st.Rates;
        var I = ac.MassProperties; var cg = cfg.Mass.CgVec();
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double e = cfg.Controls.Elevator.MaxDeflRad, r = cfg.Controls.Rudder.MaxDeflRad;
        double alpha = System.Math.Atan2(v.Z, v.X) * 57.3;

        // WHY it is what it is: AeroModel records a "tailflow" sample per stab strip carrying the strip's own
        // local flow vector and (α_t, ε, η_t = q_local / q_freestream). η_t is the number that matters — a
        // tail in a dead wake can have all the section and all the deflection in the world and still make
        // nothing, because it has no dynamic pressure to work with.
        // On only across the capture, and off again before anything else sums LastForces: see
        // ForceDebug.FlowDiagnostics. A diagnostic left on is a force to whoever adds up next.
        ForceDebug.FlowDiagnostics = true;
        try
        {
            ac.CaptureForces = true;
            sim.RunFor(0.02, ci);
            ac.CaptureForces = false;
        }
        finally { ForceDebug.FlowDiagnostics = false; }
        double qInf = 0.5 * Density(st) * v.LengthSquared;
        var seen = new HashSet<string>();
        _out.WriteLine($"   control travel: elevator ±{e * 57.3:0.0}°, rudder ±{r * 57.3:0.0}°");
        _out.WriteLine("   surface   in-plane  geometric   BLANKETING   η_t      (η_t = geometric × blanketing)");
        foreach (var f in ac.LastForces)
            if (f.Kind == "tailq")
            {
                string key = $"{f.PosBody.X:0.00}/{f.PosBody.Y:0.00}/{f.PosBody.Z:0.00}";
                if (!seen.Add(key)) continue;                       // the capture holds one RK4 stage; strips repeat
                double inPlane = f.ForceBody.X, qf = f.ForceBody.Y, eta = f.ForceBody.Z, vInf = f.MomentBody.Y;
                string where = f.MomentBody.X > 0.5 ? "FIN " : "stab";
                _out.WriteLine($"   {where} z {f.PosBody.Z:+0.00;-0.00}  {inPlane,7:0.0} m/s"
                    + $"   {inPlane * inPlane / (vInf * vInf),8:0.000}   {qf,10:0.000}   {eta,6:0.000}");
            }
        _out.WriteLine($"   (free stream {v.Length:0.0} m/s, q∞ {qInf:0} Pa; geometric is the flow in the strip's OWN plane,"
            + " so a fin at α 90 is edge-on for reasons that are not blanketing)");

        // AUTHORITY AGAINST α, AT THE SPIN'S OWN RATES. At exactly α 90 an elevator has no pitch authority
        // and cannot have any: the tail is broadside, and deflecting the flap up or down turns it the same
        // number of degrees away from the flow either way, so the two are symmetric. The real question is
        // therefore not "how much at α 91" but "where does it come BACK" — because Beggs-Mueller recovers on
        // the RUDDER once the nose drops, not on the elevator at α 91. If the rudder is healthy by α 60–70
        // then the control model is sound and the blocker is the nose not dropping; if it is still nothing at
        // α 50, the control model is broken. Speed and rates held at the spin's, α swept by turning the
        // relative wind — quasi-static, so read the trend, not the third digit.
        _out.WriteLine($"\n   {cfg.Id}: authority against α at the spin's own rates ({om.Length * 57.3:0} °/s, {v.Length * 1.943844:0} kt)");
        _out.WriteLine("     α      elevator °/s²   rudder °/s²");
        for (int deg = 90; deg >= 30; deg -= 10)
        {
            double a = deg * Math.PI / 180;
            Vec3 vs = new(v.Length * Math.Cos(a), 0, v.Length * Math.Sin(a));
            Vec3 M(ControlDeflections c) => AeroModel.Compute(cfg, tables, vs, om, Vec3.Zero, Density(st), c).Moment;
            double dE = Math.Abs(M(new ControlDeflections(0, -e, 0, 0)).Y - M(new ControlDeflections(0, e, 0, 0)).Y);
            double dR = Math.Abs(M(new ControlDeflections(0, 0, -r, 0)).Z - M(new ControlDeflections(0, 0, r, 0)).Z);
            _out.WriteLine($"    {deg,3}      {dE / I.Iyy * 57.3,10:0.0}   {dR / I.Izz * 57.3,10:0.0}");
        }

        // WHERE THE RUDDER'S MOMENT GOES, strip by strip. The whole-aircraft number says the rudder is light;
        // this says which factor is light. For each vertical strip, full left against full right: the side force
        // it changes, its own q, its area and its yaw arm — so a missing moment is attributable to one of them
        // rather than to "the rudder".
        foreach (int deg in new[] { 91, 60, 30 })
        {
            double a = deg * Math.PI / 180;
            Vec3 vs = new(v.Length * Math.Cos(a), 0, v.Length * Math.Sin(a));
            var side = new Dictionary<string, (double fy, double arm)>();
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var cap = new List<ForceSample>(256);
                ForceDebug.Samples = cap;
                AeroModel.Compute(cfg, tables, vs, om, Vec3.Zero, Density(st), new ControlDeflections(0, 0, sign * r, 0));
                ForceDebug.Samples = null;
                foreach (var fs in cap)
                {
                    if (fs.Kind != "lift" && fs.Kind != "drag") continue;
                    if (Math.Abs(fs.PosBody.Y) > 0.03) continue;               // vertical tail sits on the centreline
                    if (fs.PosBody.X > -1.0) continue;
                    string k = $"{fs.PosBody.X:0.00}/{fs.PosBody.Z:+0.00;-0.00}";
                    double arm = fs.PosBody.X - cg.X;
                    side.TryGetValue(k, out var acc);
                    side[k] = (acc.fy + sign * fs.ForceBody.Y, arm);
                }
            }
            double totFy = 0, totM = 0;
            foreach (var (k, val) in side) { totFy += Math.Abs(val.fy); totM += Math.Abs(val.fy * val.arm); }
            // The implied COEFFICIENT, which is the only way to tell a weak control model from a surface that
            // simply has no air: ΔCy = ΔFy / (q_local × area). Area and q from the vertical strips themselves.
            double area = 0, qSum = 0, armSum = 0; int n = 0;
            {
                var cap = new List<ForceSample>(256);
                ForceDebug.FlowDiagnostics = true;
                ForceDebug.Samples = cap;
                try { AeroModel.Compute(cfg, tables, vs, om, Vec3.Zero, Density(st), new ControlDeflections(0, 0, 0, 0)); }
                finally { ForceDebug.Samples = null; ForceDebug.FlowDiagnostics = false; }
                double qInfHere = 0.5 * Density(st) * vs.LengthSquared;
                foreach (var fs in cap)
                    if (fs.Kind == "tailq" && fs.MomentBody.X > 0.5)
                    { qSum += qInfHere * fs.ForceBody.Z; n++; }
                foreach (var sf in cfg.Surfaces)
                    if (sf.Id.StartsWith("vStab") || sf.Id.StartsWith("rudder"))
                        foreach (var stp in sf.Strips) { area += stp.Area; armSum += Math.Abs(stp.PosVec().X - cg.X); }
            }
            double qMean = n > 0 ? qSum / n : 0;
            if (deg == 60 || deg == 91)
                foreach (var (k, val) in side)
                    _out.WriteLine($"        strip {k}  ΔFy {val.fy,8:+0.0;-0.0} N  arm {val.arm:0.00} m");
            _out.WriteLine($"   α {deg,3}: ΔFy {totFy,6:0} N   ΔMz {totM,6:0} N·m  →  {totM / I.Izz * 57.3,5:0.0} °/s²"
                + $"   | vertical area {area:0.00} m²  q_local {qMean,5:0} Pa (q∞ {0.5 * Density(st) * vs.LengthSquared:0})"
                + $"  →  implied ΔCy {(qMean * area > 1 ? totFy / (qMean * area) : 0),6:0.000}");
        }

        // The same perturbation, with the spin's own rates and then with the rates zeroed: the gap between
        // them is exactly the error in every static measurement taken before this probe existed.
        foreach (var (what, rates) in new[] { ("in the spin (real rates)", om), ("static (rates zeroed)", Vec3.Zero) })
        {
            Vec3 M(ControlDeflections c) => AeroModel.Compute(cfg, tables, v, rates, Vec3.Zero, Density(st), c).Moment;
            double dEle = System.Math.Abs(M(new ControlDeflections(0, -e, 0, 0)).Y - M(new ControlDeflections(0, e, 0, 0)).Y);
            double dRud = System.Math.Abs(M(new ControlDeflections(0, 0, -r, 0)).Z - M(new ControlDeflections(0, 0, r, 0)).Z);
            _out.WriteLine($"{cfg.Id}  α {alpha:0.0}  {om.Length * 57.3:0} °/s  {v.Length * 1.943844:0} kt   {what}"
                + $"\n     elevator full up↔down: {dEle:0} N·m  →  {dEle / I.Iyy * 57.3:0.0} °/s² of pitch"
                + $"\n     rudder   full L↔R:     {dRud:0} N·m  →  {dRud / I.Izz * 57.3:0.0} °/s² of yaw");
        }
    }

    private static double Density(RigidBodyState s) => Atmosphere.DensityAtAltitude(-s.Position.Z);

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}

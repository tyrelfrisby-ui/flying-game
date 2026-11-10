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
        var I = ac.MassProperties;
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double e = cfg.Controls.Elevator.MaxDeflRad, r = cfg.Controls.Rudder.MaxDeflRad;
        double alpha = System.Math.Atan2(v.Z, v.X) * 57.3;

        // WHY it is what it is: AeroModel records a "tailflow" sample per stab strip carrying the strip's own
        // local flow vector and (α_t, ε, η_t = q_local / q_freestream). η_t is the number that matters — a
        // tail in a dead wake can have all the section and all the deflection in the world and still make
        // nothing, because it has no dynamic pressure to work with.
        ac.CaptureForces = true;
        sim.RunFor(0.02, ci);
        ac.CaptureForces = false;
        double qInf = 0.5 * Density(st) * v.LengthSquared;
        foreach (var f in ac.LastForces)
            if (f.Kind == "tailflow")
                _out.WriteLine($"   tail strip x {f.PosBody.X:0.00} y {f.PosBody.Y:+0.00;-0.00}:  local flow {f.ForceBody.Length:0.0} m/s"
                    + $"  α_t {f.MomentBody.X * 57.3:+0;-0}°  ε {f.MomentBody.Y * 57.3:+0.0;-0.0}°  η_t {f.MomentBody.Z:0.000}"
                    + $"  (free stream {v.Length:0.0} m/s, q∞ {qInf:0} Pa → q_local {qInf * f.MomentBody.Z:0} Pa)");

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

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic: lateral-directional derivatives at trim and a hold-full-rudder time history.</summary>
public class LateralProbeTests
{
    private readonly ITestOutputHelper _out;
    public LateralProbeTests(ITestOutputHelper o) { _out = o; }

    private static readonly AircraftConfig Config = TestAircraftConfig.Load();
    private static readonly Dictionary<string, AirfoilTable> Tables = Aircraft.BuildAirfoilTables(Config);
    private const double Rho = Atmosphere.SeaLevelDensityKgM3;

    [Fact]
    public void Probe()
    {
        const double V = 22.0, S = 20.4, b = 15.8;
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(Config, V, 600);
        double a = trim.AlphaRad;
        double q = 0.5 * Rho * V * V;
        _out.WriteLine($"trim alpha {a*57.3:F2} deg, elev {trim.ElevatorRad*57.3:F2} deg, CL~{Config.Mass.MassKg*9.81/(q*S):F2}");

        Vec3 Vel(double beta) => new(V * Math.Cos(a) * Math.Cos(beta), V * Math.Sin(beta), V * Math.Sin(a) * Math.Cos(beta));
        (double Cl, double Cn) Coef(Vec3 vel, Vec3 rates, double dr)
        {
            var (_, m) = AeroModel.Compute(Config, Tables, vel, rates, Vec3.Zero, Rho, new ControlDeflections(0, trim.ElevatorRad, dr, 0));
            // stability axes (rotate by -alpha about y)
            double sa = Math.Sin(a), ca = Math.Cos(a);
            double L = m.X * ca + m.Z * sa, N = -m.X * sa + m.Z * ca;
            return (L / (q * S * b), N / (q * S * b));
        }
        var c0 = Coef(Vel(0), Vec3.Zero, 0);
        double beta = 5 * Math.PI / 180;
        var cb = Coef(Vel(beta), Vec3.Zero, 0);
        _out.WriteLine($"Cl_beta = {(cb.Cl - c0.Cl) / beta:F4} /rad   Cn_beta = {(cb.Cn - c0.Cn) / beta:F4} /rad   (typical glider: Clb ~ -0.08..-0.15, Cnb ~ +0.05..0.1)");
        double r = 0.2; double rhat = r * b / (2 * V);
        var cr = Coef(Vel(0), new Vec3(-r * Math.Sin(a), 0, r * Math.Cos(a)), 0); // stability-axis yaw rate
        _out.WriteLine($"Cl_r = {(cr.Cl - c0.Cl) / rhat:F4}   Cn_r = {(cr.Cn - c0.Cn) / rhat:F4}   (typical: Clr ~ +0.15..0.3 (~CL/4), Cnr ~ -0.1..-0.2)");
        double p = 0.2; double phat = p * b / (2 * V);
        var cp = Coef(Vel(0), new Vec3(p * Math.Cos(a), 0, p * Math.Sin(a)), 0); // stability-axis roll rate
        _out.WriteLine($"Cl_p = {(cp.Cl - c0.Cl) / phat:F4}   Cn_p = {(cp.Cn - c0.Cn) / phat:F4}   (typical: Clp ~ -0.4..-0.6)");
        double dr = 0.5;
        var cd = Coef(Vel(0), Vec3.Zero, dr);
        _out.WriteLine($"Cl_dr = {(cd.Cl - c0.Cl) / dr:F4}   Cn_dr = {(cd.Cn - c0.Cn) / dr:F4}  (per rad rudder; typical Cndr ~ -0.05..-0.1, Cldr ~ +0.005..0.02 i.e. small, OPPOSITE the yaw)");

        // Time history: trimmed glide, then full right rudder held, stick neutral (elevator at trim).
        double half = trim.ThetaRad / 2.0;
        var att = new Quat(0, Math.Sin(half), 0, Math.Cos(half));
        var state = new RigidBodyState(new Vec3(0, 0, -600), att, new Vec3(V * Math.Cos(a), 0, V * Math.Sin(a)), Vec3.Zero);
        var ac = new Aircraft(Config, state, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        var sim = new SimLoop(ac);
        sim.RunFor(2.0, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        _out.WriteLine("t     bank    hdg    beta    p       r      V");
        foreach (double rudFrac in new[] { 1.0, 0.36 })
        {
        _out.WriteLine($"--- rudder {rudFrac*100:F0}% of max ({rudFrac*Config.Controls.Rudder.MaxDeflRad*57.3:F0} deg){(rudFrac < 1 ? "  = a HALF-stick pad input after the config dead zone/expo (pad shaping removed)" : "")}");
        ac = new Aircraft(Config, state, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        sim = new SimLoop(ac);
        sim.RunFor(2.0, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        var hold = new ControlDeflections(0, trim.ElevatorRad, rudFrac * Config.Controls.Rudder.MaxDeflRad, 0);
        for (double t = 0; t <= 8.0001; t += 1.0)
        {
            RigidBodyState s = ac.State;
            Quat qq = s.Attitude;
            double phi = Math.Atan2(2 * (qq.W * qq.X + qq.Y * qq.Z), 1 - 2 * (qq.X * qq.X + qq.Y * qq.Y)) * 57.3;
            double psi = Math.Atan2(2 * (qq.W * qq.Z + qq.X * qq.Y), 1 - 2 * (qq.Y * qq.Y + qq.Z * qq.Z)) * 57.3;
            double bet = Math.Asin(s.Velocity.Y / s.Velocity.Length) * 57.3;
            _out.WriteLine($"{t,4:F1}  {phi,6:F1}  {psi,6:F1}  {bet,6:F2}  {s.Rates.X*57.3,6:F1}  {s.Rates.Z*57.3,6:F1}  {s.Velocity.Length,5:F1}");
            sim.RunFor(1.0, hold);
        }
        }
    }
}

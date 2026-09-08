using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Extra 300 tumble ingredients (owner/CFI): (1) propeller gyroscopics must be applied at full strength
/// with the right sign — right-hand prop, pitch UP → nose yaws RIGHT; (2) fuselage cross-section camber:
/// sideslip in EITHER direction pitches the nose DOWN.
/// </summary>
public class ExtraCouplingTests
{
    private readonly ITestOutputHelper _out;
    public ExtraCouplingTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "extra-300-like.json"));

    [Fact]
    public void PitchUpAtFullPowerYawsRightByPropAngularMomentum()
    {
        AircraftConfig cfg = Load();
        PropulsionConfig p = cfg.Propulsion!;
        double q = 1.0; // rad/s pitch-up
        var vel = new Vec3(50, 0, 2);
        (_, Vec3 m0) = PropModel.Compute(p, 1.0, vel, Vec3.Zero, Atmosphere.SeaLevelDensityKgM3);
        (_, Vec3 m1) = PropModel.Compute(p, 1.0, vel, new Vec3(0, q, 0), Atmosphere.SeaLevelDensityKgM3);
        double yaw = m1.Z - m0.Z;
        double h = p.PropInertia * p.MaxRpm * 2 * Math.PI / 60.0;
        _out.WriteLine($"h = {h:F0} N·m·s, gyro yaw moment at q=1 rad/s: {yaw:F0} N·m (expected {h:F0}); Izz={cfg.Mass.Inertia.Izz} → {yaw / cfg.Mass.Inertia.Izz:F2} rad/s²");
        Assert.True(yaw > 0, "Right-hand prop + pitch up must yaw the nose right.");
        Assert.InRange(yaw, h * 0.95, h * 1.05);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(-20)]
    public void SideslipPitchesNoseDown(double betaDeg)
    {
        AircraftConfig cfg = Load();
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double a = 3 * Math.PI / 180, b = betaDeg * Math.PI / 180, V = 50;
        Vec3 Vel(double beta) => new(V * Math.Cos(a) * Math.Cos(beta), V * Math.Sin(beta), V * Math.Sin(a) * Math.Cos(beta));
        (_, Vec3 m0) = AeroModel.Compute(cfg, tables, Vel(0), Vec3.Zero, Vec3.Zero, Atmosphere.SeaLevelDensityKgM3, ControlDeflections.Neutral);
        (_, Vec3 m1) = AeroModel.Compute(cfg, tables, Vel(b), Vec3.Zero, Vec3.Zero, Atmosphere.SeaLevelDensityKgM3, ControlDeflections.Neutral);
        double dPitch = m1.Y - m0.Y;
        double qSc = 0.5 * Atmosphere.SeaLevelDensityKgM3 * V * V * 10.4 * 1.3;
        _out.WriteLine($"beta {betaDeg} deg: ΔM_pitch = {dPitch:F0} N·m  (ΔCm = {dPitch / qSc:F4}; ≈ {dPitch / qSc / 0.5 * 57.3:F1} deg of equivalent alpha at Cm_alpha -0.5)");
        Assert.True(dPitch < -300, $"Sideslip must pitch the Extra nose-down (got {dPitch:F0} N·m).");
    }
}

public class ExtraGyroProbe
{
    private readonly ITestOutputHelper _out;
    public ExtraGyroProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void SnapPullAtFullPower()
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "extra-300-like.json"));
        const double V = 55;
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(cfg, V, 600);
        double a = trim.AlphaRad, half = trim.ThetaRad / 2;
        var state = new RigidBodyState(new Vec3(0, 0, -600), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
            new Vec3(V * Math.Cos(a), 0, V * Math.Sin(a)), Vec3.Zero);
        var ac = new Aircraft(cfg, state, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        var sim = new SimLoop(ac);
        sim.RunFor(1.0, new ControlInputs(0, 0, 0, -1.0)); // settle at full power
        _out.WriteLine("t    q(deg/s)  r(deg/s)  beta(deg)  alpha(deg)  V");
        var pull = new ControlInputs(0, -1.0, 0, -1.0); // full aft stick, full power, no rudder
        for (double t = 0; t <= 2.0001; t += 0.25)
        {
            RigidBodyState s = ac.State;
            double alpha = Math.Atan2(s.Velocity.Z, s.Velocity.X) * 57.3;
            double beta = Math.Asin(s.Velocity.Y / s.Velocity.Length) * 57.3;
            _out.WriteLine($"{t,4:F2}  {s.Rates.Y * 57.3,7:F1}  {s.Rates.Z * 57.3,7:F1}  {beta,8:F1}  {alpha,8:F1}  {s.Velocity.Length,5:F1}");
            sim.RunFor(0.25, pull);
        }
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Every type spawns at its own cruise-ish airspeed (owner: the Pitts / P-51 / Sabre stalled instantly at
/// glider speed) and, with the pitch-trim slider preset to the solved trim, holds level flight hands-off.
/// </summary>
public class SpawnSpeedTests
{
    private readonly ITestOutputHelper _out;
    public SpawnSpeedTests(ITestOutputHelper o) { _out = o; }

    public static IEnumerable<object[]> Ids() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json")
            .Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [Theory]
    [MemberData(nameof(Ids))]
    public void SpawnsTrimmedAndHoldsLevel(string id)
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        Assert.True(cfg.SpawnIasMs > 0, $"{id}: spawnIasMs missing.");
        double V = cfg.SpawnIasMs;
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(cfg, V, 600);
        Assert.True(trim.Converged, $"{id}: trim did not converge at {V} m/s.");
        double alpha0 = trim.AlphaRad * 57.3;
        Assert.True(alpha0 < 10.0, $"{id}: spawn trim alpha {alpha0:F1} deg — too close to stall at {V} m/s.");

        // Static pitch stability at the spawn point: Cm must FALL with alpha (nose-down restoring).
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double qSc = 0.5 * Atmosphere.SeaLevelDensityKgM3 * V * V;
        double CmAt(double aRad)
        {
            (_, Vec3 m) = AeroModel.Compute(cfg, tables, new Vec3(V * Math.Cos(aRad), 0, V * Math.Sin(aRad)), Vec3.Zero, Vec3.Zero,
                Atmosphere.SeaLevelDensityKgM3, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
            return m.Y / qSc;
        }
        double slope = (CmAt(trim.AlphaRad + 0.035) - CmAt(trim.AlphaRad - 0.035)) / 0.07;
        Assert.True(slope < 0, $"{id}: statically UNSTABLE in pitch at spawn (dM/dα = {slope:F2} N·m per rad per Pa).");

        double stick = Aircraft.StickForDeflection(trim.ElevatorRad, cfg.Controls.Elevator);
        double half = trim.ThetaRad / 2.0;
        var state = new RigidBodyState(new Vec3(0, 0, -600), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
            new Vec3(V * Math.Cos(trim.AlphaRad), 0, V * Math.Sin(trim.AlphaRad)), Vec3.Zero);
        var ac = new Aircraft(cfg, state, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        var sim = new SimLoop(ac);
        // Hands-off: stick neutral + trim preset; throttle lever at neutral (half power) like the pad at rest.
        var hold = new ControlInputs(0, stick, 0, 0);
        double maxAlpha = 0, minV = V, maxV = V;
        for (int i = 0; i < 40; i++)
        {
            RigidBodyState s = sim.RunFor(0.1, hold);
            double a = Math.Atan2(s.Velocity.Z, s.Velocity.X) * 57.3;
            maxAlpha = Math.Max(maxAlpha, a);
            minV = Math.Min(minV, s.Velocity.Length); maxV = Math.Max(maxV, s.Velocity.Length);
        }
        _out.WriteLine($"{id,-22} V {V,5:F0} m/s  trim α {alpha0,5:F1}°  elev {trim.ElevatorRad * 57.3,6:F1}°  stick {stick,6:F2}  dM/dα {slope,7:F2}  4 s hands-off: α_max {maxAlpha,5:F1}°  V {minV:F0}..{maxV:F0}");
        Assert.True(maxAlpha < 13.0, $"{id}: alpha reached {maxAlpha:F1} deg hands-off — stalling after spawn.");
        Assert.True(minV > 0.7 * V, $"{id}: speed decayed to {minV:F0} m/s within 4 s of spawn.");
    }
}

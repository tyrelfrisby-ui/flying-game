using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// REAL structural g limits (owner request 2026-09-08): limit load from AircraftConfig.Limits (GMax/GMin),
/// ultimate = 1.5 × limit (FAR 23/25). Between them the airframe groans (severity 0..1); past ultimate for
/// a short continuous dwell the WINGS SEPARATE and the sim keeps flying the fuselage + tail + engine so the
/// pilot still has something as it drops.
/// </summary>
public class StructuralTests
{
    private readonly ITestOutputHelper _out;
    public StructuralTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Spawn at speed V (m/s) with pitch attitude theta (rad, negative = nose down), wings level, at 1500 m.</summary>
    private static (Aircraft, SimLoop) Spawn(AircraftConfig cfg, double V, double thetaRad, double elevatorRad = 0.0)
    {
        double half = thetaRad / 2;
        var state = new RigidBodyState(new Vec3(0, 0, -1500), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
            new Vec3(V, 0, 0), Vec3.Zero);
        var ac = new Aircraft(cfg, state, new ControlDeflections(0, elevatorRad, 0, 0));
        return (ac, new SimLoop(ac));
    }

    private static (Aircraft, SimLoop) SpawnTrimmedGlider()
    {
        AircraftConfig config = TestAircraftConfig.Load();
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, 18.0, 600.0);
        double half = trim.ThetaRad / 2.0;
        var aircraft = new Aircraft(config,
            new RigidBodyState(new Vec3(0, 0, -600), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
                new Vec3(18 * Math.Cos(trim.AlphaRad), 0, 18 * Math.Sin(trim.AlphaRad)), Vec3.Zero),
            new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        return (aircraft, new SimLoop(aircraft));
    }

    [Fact]
    public void LimitsComeFromConfigWithUltimateAtOnePointFive()
    {
        var (ac, _) = Spawn(Load("glider-2-33-like"), 30, 0);
        // SGS 2-33 flight manual: limit load factors +4.67 / -2.33 g (utility category).
        Assert.Equal(4.67, ac.Structure.LimitPosG, 6);
        Assert.Equal(-2.33, ac.Structure.LimitNegG, 6);
        Assert.Equal(4.67 * 1.5, ac.Structure.UltimatePosG, 6);
        Assert.Equal(-2.33 * 1.5, ac.Structure.UltimateNegG, 6);
        Assert.Equal(0.0, ac.Structure.SeverityFor(1.0));
        Assert.Equal(0.0, ac.Structure.SeverityFor(4.67));
        Assert.InRange(ac.Structure.SeverityFor(4.67 * 1.25), 0.49, 0.51);   // halfway limit→ultimate
        Assert.Equal(1.0, ac.Structure.SeverityFor(9.0));
        Assert.InRange(ac.Structure.SeverityFor(-2.33 * 1.25), 0.49, 0.51);  // negative side, by sign
    }

    [Fact]
    public void PullBetweenLimitAndUltimateGroansButWingsHold()
    {
        // (a) 2-33 in a shallow dive at 45 m/s, half aft stick: peaks just over the 4.7 g limit, well
        // under the 7.05 g ultimate — the structure groans and holds.
        var (ac, sim) = Spawn(Load("glider-2-33-like"), 45, -0.3);
        var pull = new ControlInputs(0, -0.5, 0, 0);
        double maxN = 0, maxSev = 0;
        for (int i = 0; i < 30; i++)
        {
            sim.RunFor(0.05, pull);
            maxN = Math.Max(maxN, ac.LoadFactorZ);
            maxSev = Math.Max(maxSev, ac.Structure.OverLimitSeverity);
            Assert.False(ac.Structure.WingsFailed, $"Wings failed at n={ac.LoadFactorZ:F2} (ultimate {ac.Structure.UltimatePosG:F2}).");
        }
        _out.WriteLine($"peak n={maxN:F2}, peak severity={maxSev:F2}");
        Assert.True(maxN > ac.Structure.LimitPosG && maxN < ac.Structure.UltimatePosG, $"Pull must land between limit and ultimate; peak n={maxN:F2}.");
        Assert.True(maxSev > 0.0 && maxSev < 1.0, $"Severity must be in (0,1); got {maxSev:F2}.");
        Assert.Equal(ac.Config.Mass.MassKg, ac.MassProperties.MassKg);
    }

    [Fact]
    public void SustainedPullPastUltimateFailsTheWings()
    {
        // (b) Extra 300 diving at Vne (130 m/s), full aft stick: n runs past 15 g and the wings let go.
        var (ac, sim) = Spawn(Load("extra-300-like"), 130, -0.5);
        int fired = 0;
        ac.Structure.OnWingsFailed += () => fired++;
        var pull = new ControlInputs(0, -1.0, 0, -1.0);
        double maxN = 0;
        for (int i = 0; i < 40 && !ac.Structure.WingsFailed; i++)
        {
            sim.RunFor(0.025, pull);
            maxN = Math.Max(maxN, ac.LoadFactorZ);
        }
        _out.WriteLine($"peak n={maxN:F2}, failed={ac.Structure.WingsFailed}, mass {ac.Config.Mass.MassKg} → {ac.MassProperties.MassKg:F0} kg");
        Assert.True(maxN > ac.Structure.UltimatePosG, $"Pull never exceeded ultimate ({maxN:F2} g).");
        Assert.True(ac.Structure.WingsFailed, "Sustained load past ultimate must separate the wings.");
        Assert.Equal(1, fired);
        Assert.InRange(ac.MassProperties.MassKg / ac.Config.Mass.MassKg, 0.7, 0.8);   // ~12 % per wing gone
        Assert.True(ac.MassProperties.Ixx < 0.5 * ac.Config.Mass.Inertia.Ixx, "Roll inertia must drop with the wings.");
        Assert.Equal(0.0, ac.Structure.OverLimitSeverity);   // nothing left to groan

        // Wing surfaces (and their aileron rows) are masked; tail surfaces stay.
        for (int i = 0; i < ac.Config.Surfaces.Count; i++)
        {
            bool wing = ac.Config.Surfaces[i].Id.Contains("wing", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(!wing, ac.IsSurfaceActive(i));
        }
        // Config untouched (other systems index it).
        Assert.Equal(6, ac.Config.Surfaces.Count);
    }

    [Fact]
    public void WinglessFuselageFallsButEngineAndTailStillWork()
    {
        // (c) Failed Extra at 60 m/s: it falls (sink grows), full throttle still accelerates it forward vs
        // idle, and the rudder still yaws it (tail + engine work, wings do not).
        AircraftConfig cfg = Load("extra-300-like");

        var (ac, sim) = Spawn(cfg, 60, 0);
        ac.FailWings();
        double Sink(Aircraft a) => a.State.Attitude.Rotate(a.State.Velocity).Z;   // world +z is down
        sim.RunFor(0.5, new ControlInputs(0, 0, 0, 0));
        double sink0 = Sink(ac);
        double alt0 = -ac.State.Position.Z;
        sim.RunFor(2.5, new ControlInputs(0, 0, 0, 0));
        double sink1 = Sink(ac);
        _out.WriteLine($"sink {sink0:F1} → {sink1:F1} m/s, altitude {alt0:F0} → {-ac.State.Position.Z:F0} m");
        Assert.True(sink1 > sink0 + 10.0, $"Wingless fuselage must be falling faster and faster: sink {sink0:F1} → {sink1:F1} m/s.");
        Assert.True(-ac.State.Position.Z < alt0 - 20.0, "Altitude must be dropping.");
        Assert.False(double.IsNaN(ac.State.Velocity.Length));

        // Engine: full power vs idle from the same wingless state — forward speed must differ.
        var (full, simFull) = Spawn(cfg, 60, 0); full.FailWings();
        var (idle, simIdle) = Spawn(cfg, 60, 0); idle.FailWings();
        simFull.RunFor(2.0, new ControlInputs(0, 0, 0, -1.0));
        simIdle.RunFor(2.0, new ControlInputs(0, 0, 0, +1.0));
        _out.WriteLine($"after 2 s: u full={full.State.Velocity.X:F1} m/s, u idle={idle.State.Velocity.X:F1} m/s, throttle {full.Throttle01:F2}/{idle.Throttle01:F2}");
        Assert.True(full.Throttle01 > 0.99 && idle.Throttle01 < 0.01);
        Assert.True(full.State.Velocity.X > idle.State.Velocity.X + 3.0,
            $"Full throttle must still push the fuselage forward: u full={full.State.Velocity.X:F1}, idle={idle.State.Velocity.X:F1}.");

        // Rudder: right vs left pedal — the fin/rudder still yaw the body, mirror-symmetric.
        double YawAfter(double rudder)
        {
            var (a, s) = Spawn(cfg, 60, 0); a.FailWings();
            double yaw = 0;
            for (int i = 0; i < 10; i++) { s.RunFor(0.05, new ControlInputs(0, 0, rudder, 0)); yaw += a.State.Rates.Z * 0.05; }
            return yaw;
        }
        double right = YawAfter(+1.0), left = YawAfter(-1.0);
        _out.WriteLine($"yaw over 0.5 s: right rudder {right * 57.3:F1} deg, left rudder {left * 57.3:F1} deg");
        Assert.True(right > 0.05, $"Right rudder must still yaw right (got {right * 57.3:F1} deg).");
        Assert.True(left < -0.05, $"Left rudder must still yaw left (got {left * 57.3:F1} deg).");
    }

    [Fact]
    public void LevelFlightAtOneGNeverReportsOverLimit()
    {
        // (d) Trimmed glide: n ≈ 1 the whole way, severity stays 0, nothing accumulates.
        var (ac, sim) = SpawnTrimmedGlider();
        for (int i = 0; i < 100; i++)
        {
            sim.RunFor(0.1, ControlInputs.Neutral);
            Assert.Equal(0.0, ac.Structure.OverLimitSeverity);
            Assert.Equal(0.0, ac.Structure.OverUltimateDwellSec);
            Assert.False(ac.Structure.WingsFailed);
            Assert.InRange(ac.LoadFactorZ, 0.5, 1.5);
        }
    }

    [Fact]
    public void FreshAircraftIsIntactAfterAFailure()
    {
        // (3) Repair path: ResetFlight builds a new Aircraft — no static state carries the failure over.
        AircraftConfig cfg = Load("extra-300-like");
        var (broken, simB) = Spawn(cfg, 60, 0);
        broken.FailWings();
        simB.RunFor(1.0, ControlInputs.Neutral);
        var (fresh, simF) = Spawn(cfg, 60, 0);
        Assert.False(fresh.Structure.WingsFailed);
        Assert.Equal(cfg.Mass.MassKg, fresh.MassProperties.MassKg);
        for (int i = 0; i < cfg.Surfaces.Count; i++) Assert.True(fresh.IsSurfaceActive(i));
        // Wings flying again: from the same state, a pull loads the fresh airframe far more than the wingless one.
        var (bare, simBare) = Spawn(cfg, 60, 0);
        bare.FailWings();
        simF.RunFor(0.3, new ControlInputs(0, -0.5, 0, 0));
        simBare.RunFor(0.3, new ControlInputs(0, -0.5, 0, 0));
        Assert.True(fresh.LoadFactorZ > bare.LoadFactorZ + 1.0, $"Fresh n={fresh.LoadFactorZ:F2} vs wingless n={bare.LoadFactorZ:F2}.");
    }

    public static IEnumerable<object[]> AllTypes()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json"))
        {
            yield return new object[] { Path.GetFileNameWithoutExtension(f) };
        }
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void WingIdentificationAndWinglessAeroHoldForEveryType(string id)
    {
        // (6) Every config: the main wing set is non-empty, takes every aileron row and biplane plane with
        // it, never a tail surface; and the masked strip loop stays consistent (no NaN) with the flow-state
        // arrays after failure, with engine and controls in use.
        AircraftConfig cfg = Load(id);
        bool[] wings = Aircraft.MainWingSurfaces(cfg);
        int wingCount = 0;
        for (int i = 0; i < cfg.Surfaces.Count; i++)
        {
            string sid = cfg.Surfaces[i].Id.ToLowerInvariant();
            bool tail = sid.Contains("stab") || sid.Contains("elevator") || sid.Contains("rudder");
            if (tail) Assert.False(wings[i], $"{id}: tail surface '{sid}' must not be treated as wing.");
            if (sid.Contains("aileron")) Assert.True(wings[i], $"{id}: aileron row '{sid}' must leave with the wing.");
            if (wings[i]) wingCount++;
        }
        Assert.True(wingCount >= 2, $"{id}: expected wing + aileron row(s); found {wingCount}.");
        if (id.Contains("pitts") || id.Contains("stearman")) Assert.True(wingCount >= 3, $"{id}: both biplane planes must go.");

        double V = cfg.SpawnIasMs > 0 ? cfg.SpawnIasMs : 25;
        var (ac, sim) = Spawn(cfg, V, 0);
        sim.RunFor(0.5, new ControlInputs(0, 0, 0, -1.0));
        ac.FailWings();
        for (int i = 0; i < 20; i++)
        {
            sim.RunFor(0.1, new ControlInputs(0.5, -0.3, 0.5, -1.0));
            RigidBodyState s = ac.State;
            Assert.False(double.IsNaN(s.Velocity.Length) || double.IsNaN(s.Rates.Length) || double.IsNaN(s.Position.Length), $"{id}: NaN after wing loss.");
        }
        Assert.True(ac.Structure.WingsFailed);
        Assert.True(-ac.State.Position.Z < 1500.0 - 5.0, $"{id}: a wingless airframe must be descending.");
    }
}

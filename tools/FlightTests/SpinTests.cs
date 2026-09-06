using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Spin physics guards (owner flight-test findings, 2026-09-06). Autorotation must be EMERGENT:
/// (1) statically, a post-stall AoA band must exist where a rolling wing generates a pro-roll moment
///     (down-going wing loses lift faster than drag resists — needs the deep-stall lift valley);
/// (2) dynamically, a full pro-spin input from a slow trim must develop and SUSTAIN rotation in the
///     commanded direction with the wing stalled — not damp out after a quarter turn.
/// </summary>
public class SpinTests
{
    [Fact]
    public void AutorotativeBandExistsJustPastStall()
    {
        AircraftConfig config = TestAircraftConfig.Load();
        var tables = Aircraft.BuildAirfoilTables(config);
        double rho = Atmosphere.DensityAtAltitude(600);

        bool anyAutorotative = false;
        for (int aDeg = 15; aDeg <= 26; aDeg++)
        {
            double a = aDeg * Math.PI / 180.0;
            Vec3 vel = new(18 * Math.Cos(a), 0, 18 * Math.Sin(a));
            (_, Vec3 m) = AeroModel.Compute(config, tables, vel, new Vec3(0.35, 0, 0), Vec3.Zero, rho, ControlDeflections.Neutral);
            if (m.X > 0)
            {
                anyAutorotative = true;
            }
        }

        Assert.True(anyAutorotative, "No autorotative AoA band 15-26 deg: a rolling stalled wing damps everywhere, so spins cannot develop.");

        // And attached flow must still damp rolling — sanity that we didn't break normal flight.
        Vec3 attachedVel = new(18 * Math.Cos(0.09), 0, 18 * Math.Sin(0.09));
        (_, Vec3 attachedM) = AeroModel.Compute(config, tables, attachedVel, new Vec3(0.35, 0, 0), Vec3.Zero, rho, ControlDeflections.Neutral);
        Assert.True(attachedM.X < 0, "Roll damping must be stable at attached-flow AoA.");
    }

    [Fact]
    public void ProSpinInputSustainsAutorotation()
    {
        AircraftConfig config = TestAircraftConfig.Load();
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, 18.0, 600.0);
        double half = trim.ThetaRad / 2.0;
        var aircraft = new Aircraft(config,
            new RigidBodyState(new Vec3(0, 0, -600),
                new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
                new Vec3(18 * Math.Cos(trim.AlphaRad), 0, 18 * Math.Sin(trim.AlphaRad)), Vec3.Zero),
            new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        var sim = new SimLoop(aircraft);

        var proSpin = new ControlInputs(0, -1.0, 1.0, 0); // full aft stick + full right rudder
        sim.RunFor(7.0, proSpin); // entry + incipient phase

        // Developed phase: rotation must persist in the commanded (right, positive) direction
        // with the wing stalled, sampled over several seconds.
        double minOmega = double.MaxValue, minAlpha = double.MaxValue;
        for (int i = 0; i < 10; i++)
        {
            sim.RunFor(0.5, proSpin);
            RigidBodyState s = aircraft.State;
            double yawRollMag = Math.Sqrt(s.Rates.X * s.Rates.X + s.Rates.Z * s.Rates.Z);
            double dirSign = s.Rates.X + s.Rates.Z; // both should stay positive (right spin)
            double alphaDeg = Math.Atan2(s.Velocity.Z, s.Velocity.X) * 180 / Math.PI;
            minOmega = Math.Min(minOmega, yawRollMag);
            minAlpha = Math.Min(minAlpha, alphaDeg);
            Assert.True(dirSign > 0, $"Rotation reversed against held pro-spin controls at sample {i} (p={s.Rates.X:F2}, r={s.Rates.Z:F2}).");
        }

        Assert.True(minOmega > 0.5, $"Autorotation decayed (min rotation rate {minOmega:F2} rad/s) — spin does not sustain.");
        Assert.True(minAlpha > 16.0, $"Wing un-stalled during the 'spin' (min alpha {minAlpha:F1} deg) — this is a spiral, not autorotation.");
    }
}

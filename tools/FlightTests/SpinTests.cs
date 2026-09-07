using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Spin physics guards (owner flight-test findings, 2026-09-06). Autorotation must be EMERGENT:
/// (1) statically, a post-stall AoA band must exist where a rolling wing generates a pro-roll moment;
/// (2) dynamically, held pro-spin controls must produce a genuine spin: multiple net turns in the
///     commanded direction with the wing stalled most of the time, at a credible rotation rate.
/// The developed spin is OSCILLATORY (real 2-33 trait): alpha and rates pump, and the sim may cycle
/// through a flatten/re-entry — so the test grades net rotation, not instant-by-instant smoothness.
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

        Vec3 attachedVel = new(18 * Math.Cos(0.09), 0, 18 * Math.Sin(0.09));
        (_, Vec3 attachedM) = AeroModel.Compute(config, tables, attachedVel, new Vec3(0.35, 0, 0), Vec3.Zero, rho, ControlDeflections.Neutral);
        Assert.True(attachedM.X < 0, "Roll damping must be stable at attached-flow AoA.");
    }

    [Fact]
    public void ProSpinInputProducesNetAutorotation()
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

        var proSpin = new ControlInputs(0, -1.0, 1.0, 0); // full aft stick + full right rudder, held

        double headingPrev = 0, netHeading = 0;
        int stalledSamples = 0, samples = 0;
        double fastestTurnSec = double.MaxValue;
        for (int i = 0; i < 140; i++)
        {
            sim.RunFor(0.1, proSpin);
            RigidBodyState s = aircraft.State;
            Quat q = s.Attitude;
            double heading = Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
            if (i > 0)
            {
                double d = heading - headingPrev;
                if (d > Math.PI) d -= 2 * Math.PI;
                if (d < -Math.PI) d += 2 * Math.PI;
                netHeading += d;
            }
            headingPrev = heading;

            double alphaDeg = Math.Atan2(s.Velocity.Z, s.Velocity.X) * 180 / Math.PI;
            samples++;
            if (alphaDeg > 16) stalledSamples++;

            double omega = Math.Sqrt(s.Rates.X * s.Rates.X + s.Rates.Z * s.Rates.Z);
            if (omega > 0.3) fastestTurnSec = Math.Min(fastestTurnSec, 2 * Math.PI / omega);
            Assert.False(double.IsNaN(omega), "Sim went NaN during spin.");
        }

        double netTurns = netHeading / (2 * Math.PI);
        double stalledFrac = (double)stalledSamples / samples;
        Assert.True(netTurns > 1.5, $"Held pro-spin controls produced only {netTurns:F2} net turns right in 14 s — autorotation not developing.");
        Assert.True(stalledFrac > 0.5, $"Wing stalled only {stalledFrac:P0} of the time — this is a spiral, not a spin.");
        Assert.True(fastestTurnSec < 5.0, $"Peak rotation only {fastestTurnSec:F1} s/turn — spin rate not credible (owner target ~3).");
    }
}

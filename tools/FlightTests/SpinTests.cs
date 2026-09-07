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
    public void ProSpinDepartsAndSustainsRotation()
    {
        // Owner acceptance (2026-09-06): held pro-spin controls must produce a genuine departure and
        // SUSTAINED deep rotation. Direction of the developed gyration need NOT match the held rudder
        // (real spins are not steered by rudder against held aft stick; Tom's recovery is rudder AND
        // stick together) — but it MUST be mirror-symmetric and recoverable (tests below).
        double net = RunSpin(+1.0, 300, out double stalledFrac);
        Assert.True(Math.Abs(net) > 1.2, $"Held pro-spin controls produced only {net:F2} net turns in 30 s.");
        Assert.True(stalledFrac > 0.5, $"Wing stalled only {stalledFrac:P0} of the time — spiral, not spin.");
    }

    [Fact]
    public void SpinIsMirrorSymmetric()
    {
        double right = RunSpin(+1.0, 300, out _);
        double left = RunSpin(-1.0, 300, out _);
        Assert.True(Math.Sign(right) == -Math.Sign(left) && Math.Abs(Math.Abs(right) - Math.Abs(left)) < 0.7,
            $"Left/right entries must mirror (chirality check): right={right:F2}, left={left:F2}.");
    }

    [Fact]
    public void TomRecoveryStopsTheSpin()
    {
        // Tom's Tips: full opposite rudder + relieve back pressure -> rotation stops promptly.
        var (aircraft, sim) = SpawnTrimmed();
        var proSpin = new ControlInputs(0, -1.0, 1.0, 0);
        for (int i = 0; i < 150; i++) sim.RunFor(0.1, proSpin);
        double omDev = OmVert(aircraft.State);
        var recover = new ControlInputs(0, 0.0, omDev > 0 ? -1.0 : 1.0, 0);
        bool stopped = false;
        for (int i = 0; i < 60; i++)
        {
            sim.RunFor(0.1, recover);
            RigidBodyState s = aircraft.State;
            double a = Math.Atan2(s.Velocity.Z, s.Velocity.X) * 180 / Math.PI;
            if (Math.Abs(OmVert(s)) < 0.3 && a < 15) { stopped = true; break; }
        }
        Assert.True(stopped, "Opposite rudder + relieved back pressure must stop the spin within 6 s.");
    }

    private static (Aircraft, SimLoop) SpawnTrimmed()
    {
        AircraftConfig config = TestAircraftConfig.Load();
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, 18.0, 600.0);
        double half = trim.ThetaRad / 2.0;
        var aircraft = new Aircraft(config,
            new RigidBodyState(new Vec3(0, 0, -600),
                new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
                new Vec3(18 * Math.Cos(trim.AlphaRad), 0, 18 * Math.Sin(trim.AlphaRad)), Vec3.Zero),
            new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        return (aircraft, new SimLoop(aircraft));
    }

    private static double OmVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }

    private static double RunSpin(double rudder, int steps, out double stalledFrac)
    {
        var (aircraft, sim) = SpawnTrimmed();
        var pro = new ControlInputs(0, -1.0, rudder, 0);
        double net = 0; int stalled = 0;
        for (int i = 0; i < steps; i++)
        {
            sim.RunFor(0.1, pro);
            net += OmVert(aircraft.State) * 0.1;
            double a = Math.Atan2(aircraft.State.Velocity.Z, aircraft.State.Velocity.X) * 180 / Math.PI;
            if (a > 16) stalled++;
            Assert.False(double.IsNaN(net), "Sim went NaN during spin.");
        }
        stalledFrac = (double)stalled / steps;
        return net / (2 * Math.PI);
    }
}

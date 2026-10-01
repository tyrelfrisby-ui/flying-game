using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// In-game replay (owner 2026-10-01): the recorder must reproduce the flight it saw, interpolate smoothly between
/// samples, keep only the newest window, and leaving a replay must resume the live flight exactly where it was.
/// </summary>
public class ReplayTests
{
    private static (Aircraft ac, SimLoop sim, ControlInputs hold) Cub()
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        double V = cfg.SpawnIasMs;
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(cfg, V, 600);
        double half = trim.ThetaRad / 2.0;
        var state = new RigidBodyState(new Vec3(0, 0, -600), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
            new Vec3(V * Math.Cos(trim.AlphaRad), 0, V * Math.Sin(trim.AlphaRad)), Vec3.Zero);
        var ac = new Aircraft(cfg, state, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        double stick = Aircraft.StickForDeflection(trim.ElevatorRad, cfg.Controls.Elevator);
        return (ac, new SimLoop(ac), new ControlInputs(0.3, stick, 0, 0));   // rolling right so the attitude changes
    }

    [Fact]
    public void RecorderReproducesAndInterpolatesTheFlight()
    {
        Atmosphere.SteadyWind = Vec3.Zero;
        var (ac, sim, hold) = Cub();
        var rec = new FlightRecorder(30, 60);
        double t = 0;
        var truth = new List<(double t, Vec3 pos)>();
        for (int i = 0; i < 600; i++)   // 10 s at 60 fps
        {
            sim.RunFor(1.0 / 60, hold);
            t += 1.0 / 60;
            rec.Record(ReplayFrame.Capture(t, ac, Vec3.Zero));
            truth.Add((t, ac.State.Position));
        }
        Assert.InRange(rec.Count, 295, 305);   // 30 Hz
        Assert.InRange(rec.Duration, 9.8, 10.0);
        // Every display frame (including the ones between samples) is within a few cm of the real flight.
        foreach (var (tt, p) in truth)
        {
            if (tt < rec.StartTime || tt > rec.EndTime) continue;
            Vec3 r = rec.Sample(tt).State.Position;
            Assert.True((r - p).Length < 0.1, $"t={tt:F3}: replay {(r - p).Length:F3} m off the flight");
        }
        // Attitude interpolation stays a unit quaternion.
        Quat q = rec.Sample(rec.StartTime + 3.0123).State.Attitude;
        Assert.InRange(q.LengthSquared, 0.999, 1.001);
    }

    [Fact]
    public void RecorderKeepsOnlyTheNewestWindow()
    {
        Atmosphere.SteadyWind = Vec3.Zero;
        var (ac, _, _) = Cub();
        var rec = new FlightRecorder(10, 5);
        for (int i = 0; i <= 200; i++) rec.Record(ReplayFrame.Capture(i * 0.1, ac, Vec3.Zero));
        Assert.InRange(rec.Duration, 4.9, 5.0);
        Assert.Equal(20.0, rec.EndTime, 6);
        // A clock that goes backwards (a reset) starts a new recording.
        rec.Record(ReplayFrame.Capture(0.0, ac, Vec3.Zero));
        Assert.Equal(1, rec.Count);
    }

    [Fact]
    public void LeavingReplayResumesTheLiveFlightExactly()
    {
        Atmosphere.SteadyWind = Vec3.Zero;
        var (a1, s1, hold) = Cub();
        var (a2, s2, _) = Cub();
        var rec = new FlightRecorder(30, 60);
        for (int i = 0; i < 300; i++)
        {
            s1.RunFor(1.0 / 60, hold); s2.RunFor(1.0 / 60, hold);
            rec.Record(ReplayFrame.Capture(i / 60.0, a2, Vec3.Zero));
        }
        // a2 goes into replay: scrub around in the recording, then restore the live pose it had on entry.
        ReplayFrame live = ReplayFrame.Capture(5.0, a2, Vec3.Zero);
        for (double t = 0; t < 5; t += 0.37) rec.Sample(t).ApplyTo(a2);
        live.ApplyTo(a2);
        for (int i = 0; i < 300; i++) { s1.RunFor(1.0 / 60, hold); s2.RunFor(1.0 / 60, hold); }
        Assert.True((a1.State.Position - a2.State.Position).Length < 1e-9, "resumed flight diverged from the uninterrupted one");
    }
}

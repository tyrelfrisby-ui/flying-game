using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The replayed propeller (Ty 2026-11-07: "the prop is not spinning"). The in-flight replay poses the aeroplane but
/// never runs its engine, and <see cref="Aircraft.EngineStopped"/> latches on a lost nose — so after a crash that tore
/// the nose off, the aeroplane reported 0 rpm for ever and the prop stood still through the whole replay, including the
/// minutes of flight BEFORE the crash. The rpm is now recorded with the frame and handed back while replaying.
/// </summary>
public class ReplayEngineRpmTests
{
    private static AircraftConfig CubConfig() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    private static Aircraft Cub() => new Aircraft(CubConfig(), new RigidBodyState());

    [Fact]
    public void AFrameCarriesTheRpmItWasFlownAtAndSamplingInterpolatesIt()
    {
        var rec = new FlightRecorder(30.0, 600.0);
        Aircraft a = Cub();
        // Two frames a second apart at known rpm, written straight in so the test pins the recorder, not the engine.
        rec.Record(new ReplayFrame(0.0, a.State, a.CurrentDeflections, 0, 0, 0, 1, Vec3.Zero, default, 1000.0));
        rec.Record(new ReplayFrame(1.0, a.State, a.CurrentDeflections, 0, 0, 0, 1, Vec3.Zero, default, 2000.0));

        Assert.Equal(1000.0, rec.Sample(0.0).EngineRpm, 6);
        Assert.Equal(2000.0, rec.Sample(1.0).EngineRpm, 6);
        Assert.Equal(1500.0, rec.Sample(0.5).EngineRpm, 6);   // halfway between the two
    }

    [Fact]
    public void ARecordedFrameTakesTheRpmFromTheAeroplaneThatFlewIt()
    {
        Aircraft a = Cub();
        ReplayFrame f = ReplayFrame.Capture(3.0, a, Vec3.Zero);
        Assert.Equal(a.EngineRpm, f.EngineRpm, 6);
    }

    [Fact]
    public void TheReplayedPropTurnsEvenAfterTheCrashTookTheNoseOff()
    {
        Aircraft a = Cub();
        a.LoseComponent(AirframeComponent.Nose);
        // This is the bug: the live aeroplane is a wreck, so it insists the engine is stopped at 0 rpm...
        Assert.True(a.EngineStopped);
        Assert.Equal(0.0, a.EngineRpm, 6);

        // ...but the replay is showing a moment from BEFORE the crash, when it was turning 2200.
        a.ReplayEngineRpm = 2200.0;
        Assert.False(a.EngineStopped);
        Assert.Equal(2200.0, a.EngineRpm, 6);

        // Leaving the replay hands the engine back to the wreck it really is.
        a.ReplayEngineRpm = null;
        Assert.True(a.EngineStopped);
        Assert.Equal(0.0, a.EngineRpm, 6);
    }

    [Fact]
    public void AReplayedZeroRpmStillReadsAsAStoppedEngine()
    {
        Aircraft a = Cub();
        a.ReplayEngineRpm = 0.0;   // the recording caught it with the engine genuinely stopped
        Assert.True(a.EngineStopped);
        Assert.Equal(0.0, a.EngineRpm, 6);
    }

    /// <summary>WHETHER IT WAS MAKING POWER SURVIVES SAMPLING. EngineRunning was left out of FlightRecorder.Lerp
    /// altogether, so it took the constructor's default — true — and Sample() returns an interpolated frame for every
    /// head strictly inside the recording. A replay of a flight that ended with the engine stopped therefore reported
    /// a RUNNING engine for all of it but its two end frames, which is the engine sound and the thrust vector the
    /// 11-08 repair set out to fix. It steps, like the smoke switch: a frame's state holds until the next.</summary>
    [Fact]
    public void WhetherTheEngineWasRunningSurvivesInterpolation()
    {
        var rec = new FlightRecorder(30.0, 600.0);
        Aircraft a = Cub();
        rec.Record(new ReplayFrame(0.0, a.State, a.CurrentDeflections, 0, 0, 0, 1, Vec3.Zero, default, 0.0, false));
        rec.Record(new ReplayFrame(1.0, a.State, a.CurrentDeflections, 0, 0, 0, 1, Vec3.Zero, default, 0.0, false));

        Assert.False(rec.Sample(0.0).EngineRunning);   // the frame itself: always was right
        Assert.False(rec.Sample(1.0).EngineRunning);
        Assert.False(rec.Sample(0.5).EngineRunning);   // the defect: this said TRUE, for every head between frames

        // And a frame where it WAS running still says so, at the frame and between frames.
        var run = new FlightRecorder(30.0, 600.0);
        run.Record(new ReplayFrame(0.0, a.State, a.CurrentDeflections, 0, 0, 0, 1, Vec3.Zero, default, 2200.0, true));
        run.Record(new ReplayFrame(1.0, a.State, a.CurrentDeflections, 0, 0, 0, 1, Vec3.Zero, default, 2200.0, true));
        Assert.True(run.Sample(0.5).EngineRunning);
    }
}

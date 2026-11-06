using System;
using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Smoke in the replay (Ty t912u, 2026-11-06: "flew with smoke ON; the replay showed none; the recording of the replay
/// showed none"). The cause: the replay recorder kept no smoke at all, and SmokeController stopped drawing the moment a
/// replay began ("FlightReplay redraws" — it never did). Now every replay frame carries the smoke switch and the flow the
/// oil made or would have made (<see cref="ReplaySmoke"/>), and the trail is rebuilt at the play head from the recording
/// (<see cref="ReplaySmokeView.Window"/>) in the mode the replay bar shows: AS FLOWN (the default), ON, OFF.
/// </summary>
[Collection("WorldTerrainActive")]
public class ReplaySmokeTests : IDisposable
{
    public void Dispose()
    {
        Atmosphere.SteadyWind = Vec3.Zero;
        Atmosphere.ActiveTurbulence = null;
        Atmosphere.Field = null;
    }

    private static Aircraft Cub(out AircraftConfig cfg)
    {
        cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        var state = new RigidBodyState(new Vec3(0, 0, -600), Quat.Identity, new Vec3(cfg.SpawnIasMs, 0, 0), Vec3.Zero);
        return new Aircraft(cfg, state, new ControlDeflections(0, 0, 0, 0));
    }

    /// <summary>30 s at 30 Hz, flying east at 40 m/s; the smoke switch on from 5 s to 15 s (and the flow the oil makes, 0.8).</summary>
    private static FlightRecorder Flight(Func<double, ReplaySmoke> smoke)
    {
        var rec = new FlightRecorder(30, 600);
        for (int i = 0; i <= 900; i++)
        {
            double t = i / 30.0;
            var st = new RigidBodyState(new Vec3(40 * t, 0, -600), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero);
            rec.Record(new ReplayFrame(t, st, new ControlDeflections(0, 0, 0, 0), 0.6, 0, 1, 1, Vec3.Zero, smoke(t)));
        }
        return rec;
    }

    private static ReplaySmoke OnBetween(double t, double from, double to) =>
        new(t >= from - 1e-9 && t < to - 1e-9, 0.8f, 30, 1.1, 40 * t, 2400);

    [Fact]
    public void AsFlownIsTheDefaultAndKeepsTheSwitchTiming()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        var pts = new List<ReplaySmokePoint>();
        Assert.True(ReplaySmokeView.Window(rec, 20, default, false, 90, pts));
        Assert.Equal(SmokeReplayMode.AsFlown, default(SmokeReplayMode));
        Assert.Equal(20.0, pts[0].T, 9);   // the head itself first: the trail starts at the aircraft
        int smoking = 0;
        foreach (var p in pts)
        {
            bool shouldSmoke = p.T >= 5 - 1e-9 && p.T < 15 - 1e-9;
            Assert.True(shouldSmoke ? Math.Abs(p.Flow - 0.8f) < 1e-6 : p.Flow == 0f, $"t {p.T:0.000}: flow {p.Flow}");
            if (p.Flow > 0) smoking++;
            Assert.True(p.T <= 20 + 1e-9 && 20 - p.T <= 90 + 1e-9);
        }
        Assert.Equal(300, smoking);   // 10 s at 30 Hz, the switch's own frames
        // The jet, the crank and the rpm come through as recorded: the pulse tubes are timed by them.
        var on = pts.Find(p => Math.Abs(p.T - 10) < 1e-6);
        Assert.Equal(30.0, on.JetU0, 9); Assert.Equal(400.0, on.Crank, 6); Assert.Equal(2400f, on.Rpm);
    }

    [Fact]
    public void OnAddsSmokeToAFlightFlownWithoutItAndOffMutesIt()
    {
        var never = Flight(t => new ReplaySmoke(false, 0.7f, 30, 1.1, 0, 2400));
        var pts = new List<ReplaySmokePoint>();
        Assert.False(ReplaySmokeView.Window(never, 20, SmokeReplayMode.AsFlown, false, 90, pts));
        Assert.Empty(pts);
        Assert.True(ReplaySmokeView.Window(never, 20, SmokeReplayMode.On, false, 90, pts));
        Assert.All(pts, p => Assert.Equal(0.7f, p.Flow));
        Assert.Equal(601, pts.Count);   // the head and 20 s of frames before it

        var flown = Flight(t => OnBetween(t, 5, 15));
        Assert.False(ReplaySmokeView.Window(flown, 12, SmokeReplayMode.Off, false, 90, pts));
        Assert.Empty(pts);
    }

    /// <summary>"Viewing overrides must not alter stored flight telemetry": every frame, field by field, after every mode
    /// has been drawn at many heads.</summary>
    [Fact]
    public void AModeNeverChangesTheRecording()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        var before = new ReplayFrame[rec.Count];
        for (int i = 0; i < rec.Count; i++) before[i] = rec.At(i);
        var pts = new List<ReplaySmokePoint>();
        foreach (var m in new[] { SmokeReplayMode.On, SmokeReplayMode.Off, SmokeReplayMode.AsFlown, SmokeReplayMode.On })
            for (double h = 0; h <= 30; h += 0.37) ReplaySmokeView.Window(rec, h, m, false, 90, pts);
        Assert.Equal(before.Length, rec.Count);
        for (int i = 0; i < rec.Count; i++)
        {
            ReplayFrame a = before[i], b = rec.At(i);
            Assert.Equal(a.Time, b.Time); Assert.Equal(a.State.Position, b.State.Position); Assert.Equal(a.Throttle01, b.Throttle01);
            Assert.Equal(a.Smoke.Recorded, b.Smoke.Recorded); Assert.Equal(a.Smoke.On, b.Smoke.On); Assert.Equal(a.Smoke.FlowIfOn, b.Smoke.FlowIfOn);
            Assert.Equal(a.Smoke.JetU0, b.Smoke.JetU0); Assert.Equal(a.Smoke.Crank, b.Smoke.Crank); Assert.Equal(a.Smoke.Rpm, b.Smoke.Rpm);
        }
    }

    /// <summary>Seeking and rewinding: the trail at a head is the same whatever head was shown before (no particle carried
    /// over), and nothing newer than the head is ever drawn.</summary>
    [Fact]
    public void SeekingAndRewindingRedrawTheSameTrail()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        var fresh = new List<ReplaySmokePoint>(); ReplaySmokeView.Window(rec, 13.3, SmokeReplayMode.AsFlown, false, 90, fresh);
        var pts = new List<ReplaySmokePoint>();
        foreach (double h in new[] { 29.0, 2.0, 14.9, 0.0, 30.0, 6.5 }) ReplaySmokeView.Window(rec, h, SmokeReplayMode.On, false, 90, pts);
        ReplaySmokeView.Window(rec, 13.3, SmokeReplayMode.AsFlown, false, 90, pts);
        Assert.Equal(fresh.Count, pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Assert.Equal(fresh[i].T, pts[i].T); Assert.Equal(fresh[i].Pos, pts[i].Pos); Assert.Equal(fresh[i].Flow, pts[i].Flow);
            Assert.True(pts[i].T <= 13.3 + 1e-9);
            if (i > 0) Assert.True(pts[i].T < pts[i - 1].T, "newest first, no repeats");
        }
        // Rewound to before the smoke went on: nothing as flown.
        Assert.False(ReplaySmokeView.Window(rec, 4.9, SmokeReplayMode.AsFlown, false, 90, pts));
        // The interpolated head steps the switch: between the 15 s frame (off) and the one before it (on) the head is on.
        ReplayFrame mid = rec.Sample(15 - 1 / 60.0);
        Assert.True(mid.Smoke.On);
        Assert.False(rec.Sample(15 + 1 / 60.0).Smoke.On);
    }

    /// <summary>An older recording (frames made before the channels existed): as flown shows none — that is all it knows —
    /// ON estimates the flow from the throttle, as live does from power (a glider's canisters: full), and nothing throws.</summary>
    [Fact]
    public void AnOlderRecordingWithoutSmokeChannelsDegradesGracefully()
    {
        var ac = Cub(out _);
        var rec = new FlightRecorder(30, 60);
        for (int i = 0; i <= 120; i++) rec.Record(ReplayFrame.Capture(i / 30.0, ac, Vec3.Zero));   // no channel
        Assert.False(ReplaySmokeView.AnyRecorded(rec));
        var pts = new List<ReplaySmokePoint>();
        Assert.False(ReplaySmokeView.Window(rec, 3, SmokeReplayMode.AsFlown, false, 90, pts));
        Assert.True(ReplaySmokeView.Window(rec, 3, SmokeReplayMode.On, false, 90, pts));
        float expect = (float)(0.25 + 0.75 * Math.Clamp(ac.Throttle01, 0, 1));
        Assert.All(pts, p => { Assert.Equal(expect, p.Flow, 5); Assert.Equal(0.0, p.JetU0); Assert.Equal(0f, p.Rpm); });
        Assert.True(ReplaySmokeView.Window(rec, 3, SmokeReplayMode.On, true, 90, pts));
        Assert.All(pts, p => Assert.Equal(1f, p.Flow));
        // An empty recorder, a head outside the recording, a zero window: no throw.
        Assert.False(ReplaySmokeView.Window(new FlightRecorder(30, 60), 1, SmokeReplayMode.On, false, 90, pts));
        Assert.True(ReplaySmokeView.Window(rec, 1e9, SmokeReplayMode.On, false, 0, pts));
        Assert.Single(pts);
        // Mixed: older frames and then frames with the channel (the recorder outlived an update) keep each its own.
        var mixed = new FlightRecorder(30, 60);
        for (int i = 0; i <= 120; i++)
            mixed.Record(i < 60 ? ReplayFrame.Capture(i / 30.0, ac, Vec3.Zero) : ReplayFrame.Capture(i / 30.0, ac, Vec3.Zero, new ReplaySmoke(true, 0.5f, 20, 1.1, i, 2000)));
        Assert.True(ReplaySmokeView.AnyRecorded(mixed));
        ReplaySmokeView.Window(mixed, 4, SmokeReplayMode.AsFlown, false, 90, pts);
        Assert.All(pts, p => Assert.Equal(p.T >= 2 - 1e-9 ? 0.5f : 0f, p.Flow));
    }

    /// <summary>The recorder keeps the live trail's own law: on with the engine running, 0.25 + 0.75 × power; none with the
    /// engine stopped or the bus dead (the pump is electric); a glider full. The switch is recorded beside it, not in it.</summary>
    [Fact]
    public void TheRecordedFlowIsTheLiveLaw()
    {
        var ac = Cub(out var cfg);
        var sim = new SimLoop(ac);
        var hold = new ControlInputs(0, 0, 0, -0.6);
        sim.RunFor(2.0, hold);
        Assert.True(ac.EngineRunning);
        ReplaySmoke ch = ReplaySmoke.Capture(ac, false, 12.5);
        Assert.True(ch.Recorded); Assert.False(ch.On); Assert.Equal(0f, ch.AsFlown);
        Assert.Equal((float)(0.25 + 0.75 * Math.Clamp(ac.ShaftPower01, 0, 1)), ch.FlowIfOn, 5);
        Assert.Equal(12.5, ch.Crank);
        Assert.True(ch.JetU0 > 1, $"jet {ch.JetU0}");
        Assert.Equal(ch.FlowIfOn, ReplaySmoke.Capture(ac, true, 0).AsFlown);
        ac.Electrics.Master = false;
        for (int i = 0; i < 400 && ac.Electrics.BusLive; i++) sim.RunFor(0.05, hold);
        if (!ac.Electrics.BusLive) Assert.Equal(0f, ReplaySmoke.FlowIfOnNow(ac));
        var glider = new Aircraft(AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "glider-2-33-like.json")),
            new RigidBodyState(new Vec3(0, 0, -600), Quat.Identity, new Vec3(25, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
        Assert.Equal(1f, ReplaySmoke.FlowIfOnNow(glider));
        Assert.Equal(0f, ReplaySmoke.FlowIfOnNow(null));
    }
}

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
/// (<see cref="ReplaySmokeView.Window"/>). Ty t982u (2026-11-07) rejected the single AS FLOWN / ON / OFF cycle: the replay
/// bar now has the mode — AS FLOWN, the default, or MANUAL — and, in manual, a separate SMOKE switch beside it that
/// toggles at each press like the live one (<see cref="ReplaySmokeControl"/>).
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

    private static List<ReplaySmokePoint> Win(FlightRecorder rec, double head, ReplaySmokeControl ctl, bool glider = false)
    {
        var pts = new List<ReplaySmokePoint>();
        ReplaySmokeView.Window(rec, head, ctl, glider, 90, pts);
        return pts;
    }

    private static void SameTrail(List<ReplaySmokePoint> a, List<ReplaySmokePoint> b, string what)
    {
        Assert.True(a.Count == b.Count, $"{what}: {a.Count} vs {b.Count} samples");
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].T, b[i].T); Assert.Equal(a[i].Pos, b[i].Pos);
            Assert.True(a[i].Flow == b[i].Flow, $"{what}: t {a[i].T:0.000} flow {a[i].Flow} vs {b[i].Flow}");
        }
    }

    [Fact]
    public void AsFlownIsTheDefaultAndKeepsTheSwitchTiming()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        var ctl = new ReplaySmokeControl();
        Assert.False(ctl.Manual);
        var pts = Win(rec, 20, ctl);
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
        var on = pts.Find(p => Math.Abs(p.T - 10) < 1e-6);
        Assert.Equal(30.0, on.JetU0, 9); Assert.Equal(400.0, on.Crank, 6); Assert.Equal(2400f, on.Rpm);
    }

    /// <summary>Ty t982u: "preserve the visible smoke state when entering manual". Taking manual control changes nothing on
    /// screen — at a head where the smoke was flowing, where it was off, and where it was off with a trail still hanging —
    /// and the manual switch starts as the flown one is there.</summary>
    [Fact]
    public void EnteringManualChangesNothingOnScreen()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        foreach (double head in new[] { 10.0, 3.0, 20.0, 15.0, 5.0 })
        {
            var asFlown = Win(rec, head, new ReplaySmokeControl());
            var ctl = new ReplaySmokeControl();
            ctl.EnterManual(rec, head);
            Assert.True(ctl.Manual);
            Assert.Equal(ReplaySmokeControl.FlownAt(rec, head), ctl.OnAt(rec, head));
            SameTrail(asFlown, Win(rec, head, ctl), $"enter manual at {head}");
        }
    }

    /// <summary>The adjacent SMOKE switch toggles at each press, like the live one: from the play head on. The trail already
    /// laid stays exactly as flown; what follows the press is on or off by the switch, whatever the recording did.</summary>
    [Fact]
    public void TheManualSwitchTogglesFromThePlayHeadOnLikeTheLiveOne()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        var ctl = new ReplaySmokeControl();
        ctl.EnterManual(rec, 10);              // flowing there: manual starts ON
        ctl.Toggle(rec, 10);                   // press: OFF at 0:10
        Assert.False(ctl.OnAt(rec, 10));
        var pts = Win(rec, 20, ctl);
        foreach (var p in pts)
        {
            float expect = p.T >= 5 - 1e-9 && p.T < 10 - 1e-9 ? 0.8f : 0f;   // as flown before the press, off after
            Assert.True(p.Flow == expect, $"t {p.T:0.000}: {p.Flow} not {expect}");
        }
        ctl.Toggle(rec, 18);                   // press again at 0:18: ON from there, past where the pilot switched off (0:15)
        Assert.True(ctl.OnAt(rec, 18));
        pts = Win(rec, 25, ctl);
        Assert.All(pts, p => Assert.Equal(p.T >= 18 - 1e-9 ? 0.8f : p.T >= 5 - 1e-9 && p.T < 10 - 1e-9 ? 0.8f : 0f, p.Flow));
        // Each press flips; it is a toggle, not a cycle through modes.
        bool before = ctl.OnAt(rec, 25);
        ctl.Toggle(rec, 25); Assert.Equal(!before, ctl.OnAt(rec, 25));
        ctl.Toggle(rec, 25); Assert.Equal(before, ctl.OnAt(rec, 25));
        Assert.True(ctl.Manual);
    }

    /// <summary>Turning smoke on after the fact for a flight flown without it, and muting one flown with it.</summary>
    [Fact]
    public void ManualAddsSmokeToAFlightFlownWithoutItAndMutesOneFlownWithIt()
    {
        var never = Flight(t => new ReplaySmoke(false, 0.7f, 30, 1.1, 0, 2400));
        var ctl = new ReplaySmokeControl();
        var pts = new List<ReplaySmokePoint>();
        Assert.False(ReplaySmokeView.Window(never, 20, ctl, false, 90, pts));
        ctl.EnterManual(never, 0);
        Assert.False(ctl.OnAt(never, 0));
        ctl.Toggle(never, 0);
        Assert.True(ReplaySmokeView.Window(never, 20, ctl, false, 90, pts));
        Assert.All(pts, p => Assert.Equal(0.7f, p.Flow));
        Assert.Equal(601, pts.Count);

        var flown = Flight(_ => new ReplaySmoke(true, 0.8f, 30, 1.1, 0, 2400));
        var mute = new ReplaySmokeControl();
        mute.Toggle(flown, 0);   // a press in AS FLOWN takes manual first, then flips: off from the start
        Assert.True(mute.Manual);
        Assert.False(ReplaySmokeView.Window(flown, 12, mute, false, 90, pts));
    }

    /// <summary>Back to AS FLOWN in one press, without passing through the manual switch: the recorded timeline at once,
    /// exactly as if manual had never been touched — and the next manual entry starts from the flown state again.</summary>
    [Fact]
    public void ReturningToAsFlownRestoresTheRecordedTimelineInOnePress()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        var fresh = Win(rec, 20, new ReplaySmokeControl());
        var ctl = new ReplaySmokeControl();
        ctl.EnterManual(rec, 7); ctl.Toggle(rec, 7); ctl.Toggle(rec, 12); ctl.Toggle(rec, 16);
        Assert.NotEqual(fresh.FindAll(p => p.Flow > 0).Count, Win(rec, 20, ctl).FindAll(p => p.Flow > 0).Count);
        ctl.ReturnToAsFlown();
        Assert.False(ctl.Manual);
        Assert.Empty(ctl.Settings);
        SameTrail(fresh, Win(rec, 20, ctl), "returned to as flown");
        ctl.EnterManual(rec, 20);
        Assert.Single(ctl.Settings);
        Assert.False(ctl.OnAt(rec, 20));   // flown off at 0:20: starts off, not whatever the last manual setting was
    }

    /// <summary>"Viewing overrides must not alter stored flight telemetry": every frame, field by field, after every control
    /// has been used at many heads.</summary>
    [Fact]
    public void TheControlsNeverChangeTheRecording()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        var before = new ReplayFrame[rec.Count];
        for (int i = 0; i < rec.Count; i++) before[i] = rec.At(i);
        var ctl = new ReplaySmokeControl();
        var pts = new List<ReplaySmokePoint>();
        for (double h = 0; h <= 30; h += 0.37)
        {
            ReplaySmokeView.Window(rec, h, ctl, false, 90, pts);
            if ((int)(h * 10) % 3 == 0) ctl.Toggle(rec, h);
            if ((int)(h * 10) % 7 == 0) ctl.ReturnToAsFlown();
            if ((int)(h * 10) % 5 == 0) ctl.EnterManual(rec, h);
        }
        Assert.Equal(before.Length, rec.Count);
        for (int i = 0; i < rec.Count; i++)
        {
            ReplayFrame a = before[i], b = rec.At(i);
            Assert.Equal(a.Time, b.Time); Assert.Equal(a.State.Position, b.State.Position); Assert.Equal(a.Throttle01, b.Throttle01);
            Assert.Equal(a.Smoke.Recorded, b.Smoke.Recorded); Assert.Equal(a.Smoke.On, b.Smoke.On); Assert.Equal(a.Smoke.FlowIfOn, b.Smoke.FlowIfOn);
            Assert.Equal(a.Smoke.JetU0, b.Smoke.JetU0); Assert.Equal(a.Smoke.Crank, b.Smoke.Crank); Assert.Equal(a.Smoke.Rpm, b.Smoke.Rpm);
        }
    }

    /// <summary>Seeking and rewinding: the trail at a head is the same whatever head was shown before, in either mode. Behind
    /// the first manual setting the recording shows as flown; a press after seeking back replaces what followed (a punch-in).
    /// A clip of the replay records the screen, so it follows the same function.</summary>
    [Fact]
    public void SeekingAndRewindingHonourTheControls()
    {
        var rec = Flight(t => OnBetween(t, 5, 15));
        var ctl = new ReplaySmokeControl();
        ctl.EnterManual(rec, 12); ctl.Toggle(rec, 12);   // off from 0:12
        var at20 = Win(rec, 20, ctl);
        foreach (double h in new[] { 29.0, 2.0, 14.9, 0.0, 30.0, 6.5 }) Win(rec, h, ctl);
        SameTrail(at20, Win(rec, 20, ctl), "seek away and back");
        // Rewound behind the manual setting: as flown there.
        SameTrail(Win(rec, 11, new ReplaySmokeControl()), Win(rec, 11, ctl), "behind the first manual setting");
        Assert.All(at20, p => Assert.True(p.T <= 20 + 1e-9));
        for (int i = 1; i < at20.Count; i++) Assert.True(at20[i].T < at20[i - 1].T, "newest first, no repeats");
        // Punch-in: seek back to 0:8 and press — it was on there (flown), so off from 0:8, and the 0:12 setting is replaced.
        ctl.Toggle(rec, 8);
        Assert.Single(ctl.Settings);
        Assert.Equal((8.0, false), ctl.Settings[0]);
        Assert.All(Win(rec, 20, ctl), p => Assert.Equal(p.T >= 5 - 1e-9 && p.T < 8 - 1e-9 ? 0.8f : 0f, p.Flow));
    }

    /// <summary>An older recording (frames made before the channels existed): as flown shows none — that is all it knows —
    /// manual ON estimates the flow from the throttle, as live does from power (a glider's canisters: full), and nothing
    /// throws.</summary>
    [Fact]
    public void AnOlderRecordingWithoutSmokeChannelsDegradesGracefully()
    {
        var ac = Cub(out _);
        var rec = new FlightRecorder(30, 60);
        for (int i = 0; i <= 120; i++) rec.Record(ReplayFrame.Capture(i / 30.0, ac, Vec3.Zero));   // no channel
        Assert.False(ReplaySmokeView.AnyRecorded(rec));
        var pts = new List<ReplaySmokePoint>();
        var ctl = new ReplaySmokeControl();
        Assert.False(ReplaySmokeView.Window(rec, 3, ctl, false, 90, pts));
        ctl.EnterManual(rec, 0);
        Assert.False(ctl.OnAt(rec, 0));
        ctl.Toggle(rec, 0);
        Assert.True(ReplaySmokeView.Window(rec, 3, ctl, false, 90, pts));
        float expect = (float)(0.25 + 0.75 * Math.Clamp(ac.Throttle01, 0, 1));
        Assert.All(pts, p => { Assert.Equal(expect, p.Flow, 5); Assert.Equal(0.0, p.JetU0); Assert.Equal(0f, p.Rpm); });
        Assert.True(ReplaySmokeView.Window(rec, 3, ctl, true, 90, pts));
        Assert.All(pts, p => Assert.Equal(1f, p.Flow));
        // An empty recorder, a head outside the recording, a zero window: no throw.
        var empty = new FlightRecorder(30, 60);
        var c2 = new ReplaySmokeControl(); c2.Toggle(empty, 1); c2.EnterManual(empty, 1); c2.ReturnToAsFlown();
        Assert.False(ReplaySmokeView.Window(empty, 1, c2, false, 90, pts));
        Assert.True(ReplaySmokeView.Window(rec, 1e9, ctl, false, 0, pts));
        Assert.Single(pts);
        // Mixed: older frames and then frames with the channel keep each its own.
        var mixed = new FlightRecorder(30, 60);
        for (int i = 0; i <= 120; i++)
            mixed.Record(i < 60 ? ReplayFrame.Capture(i / 30.0, ac, Vec3.Zero) : ReplayFrame.Capture(i / 30.0, ac, Vec3.Zero, new ReplaySmoke(true, 0.5f, 20, 1.1, i, 2000)));
        Assert.True(ReplaySmokeView.AnyRecorded(mixed));
        ReplaySmokeView.Window(mixed, 4, new ReplaySmokeControl(), false, 90, pts);
        Assert.All(pts, p => Assert.Equal(p.T >= 2 - 1e-9 ? 0.5f : 0f, p.Flow));
    }

    /// <summary>Live flight is never touched by the replay's smoke controls: the control is the replay's own object, and
    /// nothing on the replay's side (FlightReplay, the replay bar, the replay's trail) calls the live switch. A guard on the
    /// code, as StreamingAssetsTests is: the live switch is SmokeController.SetOn / Toggle / On.</summary>
    [Fact]
    public void TheReplayControlsNeverReachTheLiveSmokeSwitch()
    {
        string bridge = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "unity", "Assets", "_Project", "Scripts", "Bridge"));
        foreach (string f in new[] { "FlightReplay.cs", "ViewPanel.cs" })
        {
            string src = File.ReadAllText(Path.Combine(bridge, f));
            Assert.DoesNotMatch(@"SmokeController\b[^;]*\.(SetOn|Toggle)\(|_smoke\.(SetOn|Toggle)\(|\.On\s*=", src);
        }
        // SmokeController's replay path reads the replay's control and the recording; it never writes On.
        string sc = File.ReadAllText(Path.Combine(bridge, "SmokeController.cs"));
        int i = sc.IndexOf("private void BuildReplay()", StringComparison.Ordinal);
        Assert.True(i > 0);
        string body = sc.Substring(i, sc.IndexOf("\n        }\n", i, StringComparison.Ordinal) - i);
        Assert.DoesNotContain("On =", body);
        Assert.DoesNotContain("SetOn(", body);
        Assert.DoesNotContain("Toggle(", body);
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

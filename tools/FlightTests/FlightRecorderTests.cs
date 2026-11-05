using System.Linq;
using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Flight recorder (Ty 2026-11-04): recordings saved to a library, played back as recorded ships that can be flown
/// with and shot down, and MULTITRACK sessions layered one ship at a time. docs/FLIGHT-RECORDER.md.
/// </summary>
[Collection("WorldTerrainActive")]   // the Gunnery and the wrecks read WorldTerrain; the Cub's sim reads the Atmosphere
public class FlightRecorderTests : IDisposable
{
    public void Dispose()
    {
        Atmosphere.SteadyWind = Vec3.Zero;
        Atmosphere.ActiveTurbulence = null;
        Atmosphere.Field = null;
    }

    private const double Step = SimLoop.DefaultFixedDtSec;

    private static AircraftConfig CubConfig() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    /// <summary>A Cub trimmed at 600 m going north, and the stick that holds a gentle right roll.</summary>
    private static (Aircraft ac, SimLoop sim, ControlInputs hold) Cub(Vec3? at = null, Quat? att = null)
    {
        AircraftConfig cfg = CubConfig();
        double V = cfg.SpawnIasMs;
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(cfg, V, 600);
        double half = trim.ThetaRad / 2.0;
        Quat q = att ?? new Quat(0, Math.Sin(half), 0, Math.Cos(half));
        var state = new RigidBodyState(at ?? new Vec3(0, 0, -600), q,
            new Vec3(V * Math.Cos(trim.AlphaRad), 0, V * Math.Sin(trim.AlphaRad)), Vec3.Zero);
        var ac = new Aircraft(cfg, state, new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        double stick = Aircraft.StickForDeflection(trim.ElevatorRad, cfg.Controls.Elevator);
        return (ac, new SimLoop(ac), new ControlInputs(0.3, stick, 0, 0));
    }

    /// <summary>A synthetic straight-and-level track: <paramref name="v"/> m/s along heading <paramref name="psi"/> from p0.</summary>
    private static FlightTrack Straight(Vec3 p0, double v, double seconds, double psi = 0.0, double startSec = 0.0, string ac = "pa18-cub-like")
    {
        var t = new FlightTrack { AircraftId = ac, StartSec = startSec };
        var q = new Quat(0, 0, Math.Sin(psi / 2), Math.Cos(psi / 2));
        var dir = new Vec3(Math.Cos(psi), Math.Sin(psi), 0);
        int n = (int)Math.Round(seconds * t.SampleHz) + 1;
        for (int k = 0; k < n; k++)
        {
            double tt = k / t.SampleHz;
            var s = new RigidBodyState(p0 + dir * (v * tt), q, new Vec3(v, 0, 0), Vec3.Zero);
            t.Append(new TrackSample(s, ControlDeflections.Neutral, 0.6, 1.0, 2300, 1.0, Vec3.Zero, RecordFlags.Smoke));
        }
        return t;
    }

    private static ReplayDirector Director() { AircraftConfig cub = CubConfig(); return new ReplayDirector(id => id == "pa18-cub-like" ? cub : null); }

    // ---------------------------------------------------------------- the frame and the track

    [Fact]
    public void APackedFrameKeepsEveryChannelToItsStep()
    {
        var origin = new Vec3(41000, -23000, -1500);
        var q = new Quat(0.1, -0.7, 0.2, 0.67).Normalized();
        var s = new TrackSample(new RigidBodyState(origin + new Vec3(812.3456, -95.4321, -33.21), q, new Vec3(71.234, -3.21, 4.56), new Vec3(5.4321, -0.123, 0.987)),
            new ControlDeflections(0.31234, -0.2222, 0.4321, 0.5, 0.75, 0.25), 0.8, 0.4, 2534, 4.567, new Vec3(-6.78, 3.21, 0.12),
            RecordFlags.Smoke | RecordFlags.PilotOut);
        TrackSample r = PackedFrame.Pack(s, origin).Unpack(origin);
        Assert.True((r.State.Position - s.State.Position).Length < 1e-3, "position");
        double dq = Math.Abs(r.State.Attitude.X * q.X + r.State.Attitude.Y * q.Y + r.State.Attitude.Z * q.Z + r.State.Attitude.W * q.W);
        Assert.True(2 * Math.Acos(Math.Min(1, dq)) < 1e-4, "attitude within 0.1 mrad");
        Assert.True((r.State.Velocity - s.State.Velocity).Length < 0.02, "velocity");
        Assert.True((r.State.Rates - s.State.Rates).Length < 0.001, "rates");
        Assert.Equal(s.Deflections.AileronRad, r.Deflections.AileronRad, 4);
        Assert.Equal(s.Deflections.RudderRad, r.Deflections.RudderRad, 4);
        Assert.Equal(0.75, r.Deflections.FlapFraction, 2);
        Assert.Equal(0.8, r.Throttle01, 2);
        Assert.Equal(2534, r.EngineRpm);
        Assert.Equal(4.567, r.LoadFactorZ, 3);
        Assert.True((r.WindWorld - s.WindWorld).Length < 0.01);
        Assert.Equal(RecordFlags.Smoke | RecordFlags.PilotOut, r.Flags);
    }

    [Fact]
    public void TheRecorderWritesAFixedGridWhateverTheDisplayRate()
    {
        // The same flight fed at a jittery Quest 72 Hz and at a struggling 13 Hz comes out on the same 30 Hz grid, and
        // both agree with the flight itself at every grid instant. (Display intervals are whole sim steps: RunFor rounds
        // to whole 5 ms steps, so any other interval would put the sim's clock and the test's apart.)
        foreach (int[] steps in new[] { new[] { 2, 3, 3, 3 }, new[] { 14, 15, 16 } })
        {
            double hz = 200.0 / steps.Average();
            var (ac, sim, hold) = Cub();
            var track = new FlightTrack { AircraftId = "pa18-cub-like" };
            var rec = new TrackRecorder(track);
            var truth = new List<(double t, Vec3 p)>();
            double t = 0; var rng = new Random(7);
            rec.Feed(t, ac, Vec3.Zero, RecordFlags.None, 0xFFFFFF);
            truth.Add((0, ac.State.Position));
            while (t < 10.0)
            {
                int n = steps[rng.Next(steps.Length)];
                // Truth at every sim step between display frames.
                for (int i = 0; i < n; i++) { sim.RunFor(Step, hold); t += Step; truth.Add((t, ac.State.Position)); }
                rec.Feed(t, ac, Vec3.Zero, RecordFlags.None, 0xFFFFFF);
            }
            Assert.Equal((int)Math.Floor(t * 30 + 1e-9) + 1, track.FrameCount);
            double worst = 0;
            foreach (var (tt, p) in truth)
            {
                double f = tt * 30;
                if (Math.Abs(f - Math.Round(f)) > 1e-6 || Math.Round(f) >= track.FrameCount) continue;   // every third frame lies on a step
                worst = Math.Max(worst, (track.Frame((int)Math.Round(f)).State.Position - p).Length);
            }
            // Grid instants falling between two display frames are Hermite-filled from the two frames around them.
            Assert.True(worst < (hz > 30 ? 0.005 : 0.05), $"{hz:F0} Hz feed: grid frame {worst:F4} m off the flight");
        }
    }

    [Fact]
    public void BetweenFramesTheHermiteFollowsATurnTheChordCuts()
    {
        // A rolling, turning Cub: between 30 Hz frames the curve through both ends' velocities stays on the flight
        // where a straight line between the two frames cuts inside it.
        var (ac, sim, hold) = Cub();
        var track = new FlightTrack { AircraftId = "pa18-cub-like" };
        var rec = new TrackRecorder(track);
        var truth = new List<(double t, Vec3 p)>();
        double t = 0;
        rec.Feed(0, ac, Vec3.Zero, RecordFlags.None, 0xFFFFFF);
        for (int i = 0; i < 2000; i++) { sim.RunFor(Step, hold); t += Step; rec.Feed(t, ac, Vec3.Zero, RecordFlags.None, 0xFFFFFF); truth.Add((t, ac.State.Position)); }
        double hermite = 0, chord = 0;
        foreach (var (tt, p) in truth)
        {
            double f = tt * 30; int k = (int)Math.Floor(f); double u = f - k;
            if (k + 1 >= track.FrameCount || u < 0.2 || u > 0.8) continue;
            hermite = Math.Max(hermite, (track.SampleLocal(tt).State.Position - p).Length);
            Vec3 a = track.Frame(k).State.Position, b = track.Frame(k + 1).State.Position;
            chord = Math.Max(chord, (a + (b - a) * u - p).Length);
        }
        double grid = 0;
        foreach (var (tt, p) in truth) { double f = tt * 30; if (Math.Abs(f - Math.Round(f)) < 1e-6 && Math.Round(f) < track.FrameCount) grid = Math.Max(grid, (track.Frame((int)Math.Round(f)).State.Position - p).Length); }
        Assert.True(hermite < 0.02, $"Hermite {hermite:F4} m off the flight between frames (chord {chord:F4}, grid {grid:F4})");
        Assert.True(hermite < chord, $"Hermite {hermite:F4} m vs chord {chord:F4} m");
    }

    [Fact]
    public void ALostPartAndASmokeColourChangeAreRecordedAsEvents()
    {
        var (ac, sim, hold) = Cub();
        var track = new FlightTrack { AircraftId = "pa18-cub-like" };
        var rec = new TrackRecorder(track);
        double t = 0;
        for (int i = 0; i < 150; i++)
        {
            if (i == 50) ac.LoseComponent(AirframeComponent.Rudder);
            sim.RunFor(1.0 / 50, hold); t += 1.0 / 50;
            rec.Feed(t, ac, Vec3.Zero, RecordFlags.Smoke, i < 100 ? 0xFFFFFF : 0xFF0000);
        }
        Assert.Contains(track.Events, e => e.Kind == TrackEventKind.PartLost && e.Arg == (int)AirframeComponent.Rudder && Math.Abs(e.Frame - 30) <= 1);
        Assert.Contains(track.Events, e => e.Kind == TrackEventKind.SmokeColour && e.Arg == 0xFF0000 && Math.Abs(e.Frame - 60) <= 1);
        Assert.Equal(0xFFFFFF, track.SmokeRgbAt(30));
        Assert.Equal(0xFF0000, track.SmokeRgbAt(track.FrameCount - 1));
    }

    // ---------------------------------------------------------------- the file and the library

    private static FlightSession TwoShipSession()
    {
        var s = new FlightSession { Name = "Diamond practice", WorldId = "island" };
        FlightTrack a = Straight(new Vec3(1000, 2000, -800), 50, 20);
        a.Name = "Lead"; a.LiveryId = "red-sunburst"; a.SmokeRgb = 0xFF2000;
        a.Events.Add(new TrackEvent(100, TrackEventKind.SmokeColour, 0x0000FF));
        FlightTrack b = Straight(new Vec3(980, 2020, -800), 50, 15, startSec: 2.0);
        b.Name = "Right wing"; b.NudgeSec = -0.25; b.Muted = true;
        s.Tracks.Add(a); s.Tracks.Add(b);
        return s;
    }

    [Fact]
    public void ASessionFileReadsBackByteForByte()
    {
        FlightSession s = TwoShipSession();
        byte[] bytes = RecordingCodec.ToBytes(s);
        FlightSession r = RecordingCodec.FromBytes(bytes);
        Assert.Equal(bytes, RecordingCodec.ToBytes(r));
        Assert.Equal("Diamond practice", r.Name);
        Assert.Equal(2, r.Tracks.Count);
        Assert.Equal("red-sunburst", r.Tracks[0].LiveryId);
        Assert.Null(r.Tracks[1].LiveryId);
        Assert.Equal(-0.25, r.Tracks[1].NudgeSec);
        Assert.True(r.Tracks[1].Muted);
        Assert.Single(r.Tracks[0].Events);
        // The frames themselves, not only the bytes: the same samples at an instant between frames.
        TrackSample x = s.Tracks[0].SampleLocal(7.123), y = r.Tracks[0].SampleLocal(7.123);
        Assert.Equal(x.State.Position.X, y.State.Position.X);
        Assert.Equal(x.State.Position.Y, y.State.Position.Y);
        Assert.Equal(x.Deflections.AileronRad, y.Deflections.AileronRad);
    }

    [Fact]
    public void AMinuteOfOneShipIsAboutNinetySevenKilobytes()
    {
        // The size estimate in the design doc, as arithmetic on the format: 54 bytes a frame at 30 Hz is 1,620 B/s.
        var s = new FlightSession { Name = "x", WorldId = "island" };
        s.Tracks.Add(Straight(new Vec3(0, 0, -500), 50, 60));
        int bytes = RecordingCodec.ToBytes(s).Length;
        Assert.Equal(54, PackedFrame.Bytes);
        Assert.Equal(1801 * 54, bytes - Overhead(s));
        Assert.InRange(bytes, 97_000, 97_500);
    }

    private static int Overhead(FlightSession s)
    {
        var empty = new FlightSession { Id = s.Id, Name = s.Name, WorldId = s.WorldId, CreatedUtcTicks = s.CreatedUtcTicks };
        foreach (FlightTrack t in s.Tracks) empty.Tracks.Add(t.CloneHeader());
        return RecordingCodec.ToBytes(empty).Length;
    }

    [Fact]
    public void ATruncatedOrForeignFileIsRefused()
    {
        byte[] bytes = RecordingCodec.ToBytes(TwoShipSession());
        Assert.ThrowsAny<Exception>(() => RecordingCodec.FromBytes(bytes[..(bytes.Length - 100)]));
        Assert.ThrowsAny<Exception>(() => RecordingCodec.FromBytes(bytes[..(bytes.Length - 1)]));
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0 };
        Assert.Throws<InvalidDataException>(() => RecordingCodec.FromBytes(png));
    }

    [Fact]
    public void TheLibrarySavesListsRenamesAndDeletes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fgrec-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lib = new RecordingLibrary(dir);
            Assert.Equal("Recording 1", lib.NextName());
            FlightSession a = TwoShipSession(); a.Name = "Recording 1"; a.CreatedUtcTicks = 100;
            var b = new FlightSession { Name = "Loop practice", CreatedUtcTicks = 200 };
            b.Tracks.Add(Straight(new Vec3(0, 0, -500), 40, 5));
            lib.Save(a); lib.Save(b);
            File.WriteAllText(Path.Combine(dir, "junk" + RecordingLibrary.Extension), "not a recording");
            var bad = new List<string>();
            List<RecordingSummary> list = lib.List(bad);
            Assert.Equal(new[] { "Loop practice", "Recording 1" }, list.ConvertAll(x => x.Name));
            Assert.Equal(new[] { "junk.fgrec" }, bad);
            Assert.Equal(2, list[1].TrackCount);
            Assert.Equal(20.0, list[1].DurationSec, 6);   // the lead's 20 s; the 15 s wingman ends at 16.75 s
            Assert.Equal("Recording 2", lib.NextName());
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));

            Assert.True(lib.Rename(a.Id, "  Diamond  "));
            Assert.Equal("Diamond", lib.Load(a.Id).Name);
            Assert.Equal(a.Tracks[0].FrameCount, lib.Load(a.Id).Tracks[0].FrameCount);
            Assert.False(lib.Rename(a.Id, "   "));
            Assert.True(lib.Delete(b.Id));
            Assert.False(lib.Delete(b.Id));
            Assert.Single(lib.List());
            Assert.False(RecordingLibrary.IsValidId("../escape"));
            Assert.Throws<ArgumentException>(() => lib.Load("../escape"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    // ---------------------------------------------------------------- the session timeline

    [Fact]
    public void MuteAndSoloWorkLikeAMixingDesk()
    {
        FlightSession s = TwoShipSession();
        FlightTrack a = s.Tracks[0], b = s.Tracks[1];
        Assert.True(s.Plays(a)); Assert.False(s.Plays(b));            // b is muted
        b.Muted = false;
        Assert.True(s.Plays(a)); Assert.True(s.Plays(b));
        b.Solo = true;
        Assert.False(s.Plays(a)); Assert.True(s.Plays(b));            // solo wins over everything not soloed
        Assert.Equal(2.0 - 0.25 + 15.0, s.Duration, 9);
        a.Solo = true;
        Assert.Equal(20.0, s.Duration, 9);
    }

    [Fact]
    public void ANudgeSlidesOneTrackAgainstTheOthers()
    {
        FlightTrack t = Straight(new Vec3(0, 0, -500), 50, 10, startSec: 3.0);
        Assert.True(t.TrySampleSession(3.0, out TrackSample s0));
        Assert.Equal(0.0, s0.State.Position.X, 3);
        t.NudgeSec = 0.5;
        Assert.False(t.TrySampleSession(3.2, out _));
        Assert.True(t.TrySampleSession(4.5, out TrackSample s1));
        Assert.Equal(50.0, s1.State.Position.X, 2);
    }

    // ---------------------------------------------------------------- playing it back

    [Fact]
    public void ARecordedShipFlysItsTrackExactly()
    {
        var s = new FlightSession();
        s.Tracks.Add(Straight(new Vec3(100, 200, -700), 50, 10, psi: 0.7, startSec: 1.0));
        ReplayDirector d = Director();
        SessionPlayback p = d.Play(s, new PlaybackOptions());
        ReplayActor a = Assert.Single(p.Actors);
        Assert.Equal(ActorMode.Absent, a.Mode);                       // before its track begins
        Assert.Empty(d.GunTargets());
        for (int i = 0; i < 4 * 60; i++) d.Tick(1.0 / 60);
        Assert.Equal(ActorMode.Kinematic, a.Mode);
        s.Tracks[0].TrySampleSession(p.Clock, out TrackSample want);
        Assert.True((a.Aircraft.State.Position - want.State.Position).Length < 1e-9);
        Assert.Single(d.GunTargets());
        for (int i = 0; i < 10 * 60; i++) d.Tick(1.0 / 60);
        Assert.True(p.Finished);
        Assert.Equal(ActorMode.Absent, a.Mode);                       // after its track ends, without loop
    }

    [Fact]
    public void AShipThatIsShotDownFallsWithItsRecordedMomentum()
    {
        var s = new FlightSession();
        s.Tracks.Add(Straight(new Vec3(0, 0, -800), 50, 30));
        ReplayDirector d = Director();
        SessionPlayback p = d.Play(s, new PlaybackOptions());
        ReplayActor a = p.Actors[0];
        for (int i = 0; i < 120; i++) d.Tick(1.0 / 60);
        Vec3 vWorld = a.Aircraft.State.Attitude.Rotate(a.Aircraft.State.Velocity);
        for (int i = 0; i < DamageState.HitsToLose; i++) a.Aircraft.Damage.RegisterHit(AirframeComponent.TailBoom);
        d.Tick(1.0 / 60);
        Assert.Equal(ActorMode.Wreck, a.Mode);
        Assert.True(a.ShotDown);
        Assert.Equal(1, p.Kills);
        Assert.True((a.Aircraft.State.Attitude.Rotate(a.Aircraft.State.Velocity) - vWorld).Length < 1.0, "released with its recorded velocity");
        double z0 = a.Aircraft.State.Position.Z;
        for (int i = 0; i < 180; i++) d.Tick(1.0 / 60);
        s.Tracks[0].TrySampleSession(p.Clock, out TrackSample onTrack);
        Assert.True(a.Aircraft.State.Position.Z > z0 + 5, "the wreck is falling");
        Assert.True((a.Aircraft.State.Position - onTrack.State.Position).Length > 5, "the wreck has left its track");
    }

    [Fact]
    public void GunneryHitsARecordedShipAndTheDirectorScoresIt()
    {
        var s = new FlightSession();
        s.Tracks.Add(Straight(new Vec3(0, 0, -800), 50, 30));
        ReplayDirector d = Director();
        SessionPlayback p = d.Play(s, new PlaybackOptions());
        AircraftConfig cfg = CubConfig();
        var guns = Armament.For(cfg);
        var gun = new Gunnery();
        double h = SimLoop.DefaultFixedDtSec;
        int hits = 0;
        for (int i = 0; i < 400; i++)
        {
            d.Tick(h);
            ReplayActor a = p.Actors[0];
            if (a.Mode != ActorMode.Kinematic) break;
            // The shooter sits 120 m dead astern at the same speed, its guns' convergence put on the target.
            RigidBodyState t = a.Aircraft.State;
            var me = new RigidBodyState(t.Position + new Vec3(-120, 0, -0.4), t.Attitude, t.Velocity, Vec3.Zero);
            gun.Fire(0, guns, cfg, me, h);
            gun.Step(h, d.GunTargets());
            foreach (HitEvent e in gun.Hits) if (d.OnHit(e) != null) hits++;
        }
        Assert.True(hits > 0, "no round hit the recorded ship");
        Assert.Equal(hits, p.Hits);
        Assert.True(p.Actors[0].Aircraft.Damage.TotalHits >= hits);
    }

    [Fact]
    public void ALoopBringsAShotDownShipBackWhole()
    {
        var s = new FlightSession();
        s.Tracks.Add(Straight(new Vec3(0, 0, -800), 50, 5));
        ReplayDirector d = Director();
        SessionPlayback p = d.Play(s, new PlaybackOptions { Loop = true });
        ReplayActor a = p.Actors[0];
        d.Tick(0.5);
        a.Aircraft.Damage.RegisterHit(AirframeComponent.Rudder);
        for (int i = 0; i < DamageState.HitsToLose; i++) a.Aircraft.Damage.RegisterHit(AirframeComponent.WingLeftInner);
        d.Tick(0.02);
        Assert.Equal(ActorMode.Wreck, a.Mode);
        int gen = a.Generation;
        for (int i = 0; i < 300; i++) d.Tick(1.0 / 60);
        Assert.Equal(1, p.Loops);
        Assert.Equal(ActorMode.Kinematic, a.Mode);
        Assert.True(a.Generation > gen);
        Assert.Empty(a.Aircraft.LostComponents);
    }

    [Fact]
    public void ARecordedCrashShedsItsPartsAndASeekBackPutsThemBack()
    {
        var s = new FlightSession();
        FlightTrack t = Straight(new Vec3(0, 0, -800), 50, 10);
        t.Events.Add(new TrackEvent(150, TrackEventKind.PartLost, (int)AirframeComponent.Rudder));
        s.Tracks.Add(t);
        ReplayDirector d = Director();
        SessionPlayback p = d.Play(s, new PlaybackOptions());
        ReplayActor a = p.Actors[0];
        d.Tick(4.0);
        Assert.False(a.Aircraft.IsLost(AirframeComponent.Rudder));
        d.Tick(2.0);
        Assert.True(a.Aircraft.IsLost(AirframeComponent.Rudder));
        Assert.Equal(ActorMode.Kinematic, a.Mode);                    // a lost rudder is not a kill: it flies its track on
        p.Seek(1.0); d.Tick(0);
        Assert.False(a.Aircraft.IsLost(AirframeComponent.Rudder));
    }

    [Fact]
    public void AtMyPositionPutsTheLeadAheadOnMyHeadingAndKeepsTheShape()
    {
        FlightSession s = TwoShipSession();
        s.Tracks[1].Muted = false; s.Tracks[1].NudgeSec = 0; s.Tracks[1].StartSec = 0;
        // The pilot is 30 km away heading east (psi = 90 deg) at 1,200 m.
        double psi = Math.PI / 2;
        var live = new RigidBodyState(new Vec3(-30000, 5000, -1200), new Quat(0, 0, Math.Sin(psi / 2), Math.Cos(psi / 2)), new Vec3(50, 0, 0), Vec3.Zero);
        ReplayDirector d = Director();
        SessionPlayback p = d.Play(s, new PlaybackOptions { Placement = PlacementMode.AtMyPosition, AheadM = 150 }, live);
        ReplayActor lead = p.Actors[0], wing = p.Actors[1];
        Vec3 rel = lead.Aircraft.State.Position - live.Position;
        Assert.Equal(0.0, rel.X, 6); Assert.Equal(150.0, rel.Y, 6); Assert.Equal(0.0, rel.Z, 6);
        Assert.Equal(psi, FormationPilot.Euler(lead.Aircraft.State.Attitude).psi, 6);
        double recorded = (s.Tracks[0].Frame(0).State.Position - s.Tracks[1].Frame(0).State.Position).Length;
        Assert.Equal(recorded, (lead.Aircraft.State.Position - wing.Aircraft.State.Position).Length, 6);
        // And it flies east: a second later the lead is 50 m further along +y.
        d.Tick(1.0);
        Assert.Equal(200.0, (lead.Aircraft.State.Position - live.Position).Y, 3);
    }

    [Fact]
    public void SeveralSessionsPlayAtOnceWithTheirOwnClocks()
    {
        ReplayDirector d = Director();
        var s1 = new FlightSession(); s1.Tracks.Add(Straight(new Vec3(0, 0, -500), 50, 10));
        var s2 = new FlightSession(); s2.Tracks.Add(Straight(new Vec3(0, 500, -500), 40, 10));
        SessionPlayback p1 = d.Play(s1, new PlaybackOptions());
        SessionPlayback p2 = d.Play(s2, new PlaybackOptions { StartOffsetSec = 4.0 });
        d.Tick(1.0);
        Assert.Equal(1.0, p1.Clock, 9); Assert.Equal(5.0, p2.Clock, 9);
        var ids = new HashSet<int>(); foreach (GunTarget g in d.GunTargets()) ids.Add(g.Id);
        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.True(id >= ReplayDirector.FirstActorId));
    }

    [Fact]
    public void AShipOfAnAeroplaneThisBuildDoesNotHaveIsLeftOutAndTheRestPlay()
    {
        var s = new FlightSession();
        s.Tracks.Add(Straight(new Vec3(0, 0, -500), 50, 10, ac: "retired-type"));
        s.Tracks.Add(Straight(new Vec3(0, 50, -500), 50, 10));
        SessionPlayback p = Director().Play(s, new PlaybackOptions());
        Assert.Single(p.Actors);
        Assert.Equal("pa18-cub-like", p.Actors[0].Track.AircraftId);
    }

    [Fact]
    public void TheLiveShipCollidesWithRecordedShipsButRecordedShipsDoNotByDefault()
    {
        var s = new FlightSession();
        s.Tracks.Add(Straight(new Vec3(0, 0, -500), 50, 10));
        s.Tracks.Add(Straight(new Vec3(0, 0.5, -500), 50, 10));      // flown separately, half a metre apart
        ReplayDirector d = Director();
        d.Play(s, new PlaybackOptions());
        d.Tick(1.0);
        Assert.Empty(d.Collisions(null));
        s.CollideRecordedShips = true;
        Assert.NotEmpty(d.Collisions(null));
        s.CollideRecordedShips = false;
        // The live Cub where ship 1 is.
        var (live, _, _) = Cub(at: new Vec3(50, 0, -500), att: Quat.Identity);
        List<ReplayCollision> c = d.Collisions(live);
        Assert.NotEmpty(c);
        Assert.All(c, x => Assert.Null(x.Other));
        var (far, _, _) = Cub(at: new Vec3(50, 300, -500), att: Quat.Identity);
        Assert.Empty(d.Collisions(far));
    }

    // ---------------------------------------------------------------- multitrack takes

    [Fact]
    public void LayeringASecondShipStartsItInSlotOnTheLeadAfterTheCountIn()
    {
        var session = new FlightSession { CountdownSec = 3.0 };
        // Track 1: no count-in, recorded wherever the pilot is.
        RecordingTake t1 = RecordingTake.NewTrack(session, "pa18-cub-like", null, 0xFFFFFF, 0.0, FlightSession.DefaultSlot(11));
        Assert.Null(t1.StartPose);
        var (lead, leadSim, roll) = Cub();
        var hold = new ControlInputs(0, roll.Elevator, 0, 0);          // straight: two ships in a turn do not keep a slot on the same stick
        for (int i = 0; i < 501; i++) { if (t1.State == TakeState.Recording) leadSim.RunFor(1.0 / 50, hold); t1.Tick(1.0 / 50, lead, Vec3.Zero, RecordFlags.Smoke, 0xFFFFFF); }
        Assert.Equal(TakeState.Recording, t1.State);
        FlightTrack? one = t1.Commit();
        Assert.NotNull(one);
        Assert.Single(session.Tracks);
        Assert.InRange(one!.Duration, 9.9, 10.0);

        // Track 2: the count-in holds the session clock at the punch-in, then it runs with the take.
        Vec3 slot = new(-15, 15, 0);
        RecordingTake t2 = RecordingTake.NewTrack(session, "pa18-cub-like", null, 0x00FF00, 0.0, slot);
        RigidBodyState start = t2.StartPose!.Value;
        RigidBodyState l0 = one.Frame(0).State;
        Assert.True((start.Position - (l0.Position + l0.Attitude.Rotate(slot))).Length < 1e-9);
        var (wing, wingSim, _) = Cub(at: start.Position, att: start.Attitude);
        for (int i = 0; i < 120; i++) t2.Tick(1.0 / 60, wing, Vec3.Zero, RecordFlags.None, 0x00FF00);
        Assert.Equal(TakeState.Countdown, t2.State);
        Assert.Equal(0.0, t2.Clock);
        for (int i = 0; i < 600; i++) { if (t2.State == TakeState.Recording) wingSim.RunFor(1.0 / 50, hold); t2.Tick(1.0 / 50, wing, Vec3.Zero, RecordFlags.None, 0x00FF00); }
        Assert.Equal(TakeState.Recording, t2.State);
        FlightTrack two = t2.Commit()!;
        Assert.Equal(2, session.Tracks.Count);
        Assert.Equal(0.0, two.StartSec);
        Assert.Equal(0x00FF00, two.SmokeRgb);
        Assert.True((two.Frame(0).State.Position - start.Position).Length < 1e-2);
        // Played together, the second ship sits in its slot: same aeroplane, same stick, so the world offset it started
        // with holds (measured in the world: the lead pitches through its phugoid, which turns its own axes).
        one.TrySampleSession(0.0, out TrackSample a0); two.TrySampleSession(0.0, out TrackSample b0);
        one.TrySampleSession(5.0, out TrackSample a5); two.TrySampleSession(5.0, out TrackSample b5);
        Vec3 drift = (b5.State.Position - a5.State.Position) - (b0.State.Position - a0.State.Position);
        Assert.True(drift.Length < 0.05, $"slot drifted {drift.Length:F3} m in 5 s");
    }

    [Fact]
    public void AnOverdubPunchInKeepsWhatCameBeforeAndACancelChangesNothing()
    {
        var session = new FlightSession { CountdownSec = 1.0 };
        FlightTrack orig = Straight(new Vec3(0, 0, -500), 50, 10);
        session.Tracks.Add(orig);
        byte[] before = RecordingCodec.ToBytes(session);

        RecordingTake cancel = RecordingTake.Overdub(session, orig, 4.0);
        var (ac0, _, _) = Cub(at: new Vec3(200, 0, -500), att: Quat.Identity);
        for (int i = 0; i < 200; i++) cancel.Tick(1.0 / 60, ac0, Vec3.Zero, RecordFlags.None, 0xFFFFFF);
        cancel.Cancel();
        Assert.Null(cancel.Commit());
        Assert.Equal(before, RecordingCodec.ToBytes(session));

        RecordingTake take = RecordingTake.Overdub(session, orig, 4.0);
        RigidBodyState start = take.StartPose!.Value;
        Assert.Equal(200.0, start.Position.X, 2);                     // where the track was at 4 s
        var (ac, sim, hold) = Cub(at: start.Position, att: start.Attitude);
        for (int i = 0; i < 201; i++) { if (take.State == TakeState.Recording) sim.RunFor(1.0 / 50, hold); take.Tick(1.0 / 50, ac, Vec3.Zero, RecordFlags.None, 0xFFFFFF); }   // 1 s count-in, 3 s flown
        FlightTrack re = take.Commit()!;
        Assert.Same(re, session.Tracks[0]);
        Assert.Single(session.Tracks);
        for (int k = 0; k < 120; k++) Assert.Equal(orig.Frames[k].Px, re.Frames[k].Px);   // 0 .. 4 s untouched
        Assert.InRange(re.EndSec, 6.9, 7.05);                        // re-flown from 4 s for 3 s
        Assert.True((re.SampleLocal(3.99).State.Position - re.SampleLocal(4.01).State.Position).Length < 2.0, "the splice jumps");
        Assert.NotEqual(orig.Frame(200).State.Rates.X, re.Frame(200).State.Rates.X);
    }

    [Fact]
    public void ATakeStoppedInItsCountInRecordsNothing()
    {
        var session = new FlightSession { CountdownSec = 5.0 };
        session.Tracks.Add(Straight(new Vec3(0, 0, -500), 50, 10));
        RecordingTake t = RecordingTake.NewTrack(session, "pa18-cub-like", null, 0xFFFFFF, 0.0, FlightSession.DefaultSlot(11));
        var (ac, _, _) = Cub();
        for (int i = 0; i < 60; i++) t.Tick(1.0 / 60, ac, Vec3.Zero, RecordFlags.None, 0xFFFFFF);
        Assert.Null(t.Commit());
        Assert.Single(session.Tracks);
    }

    [Fact]
    public void ThePlaybackDuringATakeLeavesOutTheTrackBeingReflown()
    {
        FlightSession s = TwoShipSession();
        s.Tracks[1].Muted = false;
        RecordingTake take = RecordingTake.Overdub(s, s.Tracks[1], 5.0);
        SessionPlayback p = Director().Play(s, new PlaybackOptions { StartOffsetSec = take.PunchInSec }, exclude: take.Replacing);
        Assert.Single(p.Actors);
        Assert.Same(s.Tracks[0], p.Actors[0].Track);
    }

    [Fact]
    public void TheOtherShipsPlayOnTheTakesClockThroughTheCountInAndAfterGo()
    {
        var session = new FlightSession { CountdownSec = 1.0 };
        FlightTrack lead = Straight(new Vec3(0, 0, -500), 50, 10);
        session.Tracks.Add(lead);
        RecordingTake take = RecordingTake.NewTrack(session, "pa18-cub-like", null, 0xFFFFFF, 2.0, new Vec3(-15, 15, 0));
        ReplayDirector d = Director();
        SessionPlayback p = d.PlayAlong(take);
        Assert.Same(take, p.Take);
        ReplayActor ship = Assert.Single(p.Actors);
        var (live, sim, hold) = Cub(at: take.StartPose!.Value.Position, att: take.StartPose!.Value.Attitude);
        lead.TrySampleSession(2.0, out TrackSample atCue);
        // The count-in: the lead waits at the punch-in, however long the frames take.
        for (int i = 0; i < 30; i++)
        {
            take.Tick(1.0 / 60, live, Vec3.Zero, RecordFlags.None, 0xFFFFFF);
            d.Tick(1.0 / 60);
            Assert.Equal(TakeState.Countdown, take.State);
            Assert.True((ship.Current.State.Position - atCue.State.Position).Length < 1e-9, "the lead moved during the count-in");
        }
        // After GO the lead runs on the take's clock, frame for frame, at a ragged display rate.
        double[] dts = { 1.0 / 72, 1.0 / 60, 1.0 / 40, 1.0 / 90 };
        for (int i = 0; i < 200; i++)
        {
            double dt = dts[i % dts.Length];
            if (take.State == TakeState.Recording) sim.RunFor(dt, hold);
            take.Tick(dt, live, Vec3.Zero, RecordFlags.None, 0xFFFFFF);
            d.Tick(dt);
            Assert.Equal(take.Clock, p.Clock);
            lead.TrySampleSession(take.Clock, out TrackSample want);
            Assert.True((ship.Current.State.Position - want.State.Position).Length < 1e-9);
        }
        Assert.Equal(TakeState.Recording, take.State);
        Assert.InRange(take.Clock, 4.5, 5.5);
        // The new ship and the lead share one clock: the take's frame 0 is at the punch-in, on the session's grid.
        FlightTrack two = take.Commit()!;
        Assert.Equal(2.0, two.BeginSec);
        Assert.True((two.Frame(0).State.Position - take.StartPose!.Value.Position).Length < 1e-2);
    }

    [Fact]
    public void EditingASessionWhileItPlaysAddsAndDropsItsShips()
    {
        FlightSession s = TwoShipSession();
        s.Tracks[1].Muted = false;
        s.CountdownSec = 1.0;
        ReplayDirector d = Director();
        SessionPlayback p = d.Play(s, new PlaybackOptions());
        Assert.Equal(2, p.Actors.Count);

        // Overdub ship 2 while ship 1 plays: the playback leaves it out during the take and then has the new take in its place.
        FlightTrack old = s.Tracks[1];
        d.Stop(p);
        RecordingTake take = RecordingTake.Overdub(s, old, 5.0);
        p = d.PlayAlong(take);
        Assert.Same(s.Tracks[0], Assert.Single(p.Actors).Track);
        RigidBodyState st = take.StartPose!.Value;
        var (live, sim, hold) = Cub(at: st.Position, att: st.Attitude);
        for (int i = 0; i < 151; i++)
        {
            if (take.State == TakeState.Recording) sim.RunFor(1.0 / 50, hold);
            take.Tick(1.0 / 50, live, Vec3.Zero, RecordFlags.None, 0xFFFFFF);
            d.Tick(1.0 / 50);
        }
        Assert.Equal(TakeState.Recording, take.State);
        FlightTrack re = take.Commit()!;
        Assert.NotNull(re);
        p.ReleaseTake();
        d.Tick(0);
        Assert.Null(p.Take);
        Assert.Equal(take.Clock, p.Clock, 9);
        Assert.Equal(2, p.Actors.Count);
        Assert.Same(re, p.Actors[1].Track);
        Assert.DoesNotContain(p.Actors, a => a.Track == old);

        // Delete a ship from the mixer while it plays: it goes. Add one: it comes.
        s.DeleteTrack(re);
        d.Tick(0.1);
        Assert.Same(s.Tracks[0], Assert.Single(p.Actors).Track);
        s.Tracks.Add(Straight(new Vec3(1000, 2040, -800), 50, 15));
        s.Touch();
        d.Tick(0.1);
        Assert.Equal(2, p.Actors.Count);
        Assert.Equal(2, p.Present().Count());
    }

    [Fact]
    public void AJoiningPilotStartsInASlotWhereTheSessionIsDrawn()
    {
        var s = new FlightSession();
        s.Tracks.Add(Straight(new Vec3(0, 0, -500), 50, 30));             // recorded going north
        var (live, _, _) = Cub(at: new Vec3(5000, 7000, -900), att: new Quat(0, 0, Math.Sin(Math.PI / 4), Math.Cos(Math.PI / 4)));   // flying east
        ReplayDirector d = Director();
        SessionPlayback p = d.Play(s, new PlaybackOptions { Placement = PlacementMode.AtMyPosition, StartOffsetSec = 3.0 }, live.State);
        ReplayActor lead = Assert.Single(p.Actors);
        Vec3 slot = new(-15, 15, 0);
        RigidBodyState join = p.JoinPose(slot)!.Value;
        RigidBodyState l = lead.Current.State;
        // The slot is on the lead as it is DRAWN (ahead of me, turned east), not where the recording was flown.
        Assert.True((join.Position - (l.Position + l.Attitude.Rotate(slot))).Length < 1e-9);
        Assert.True((l.Position - new Vec3(5000, 7150, -900)).Length < 1.0, $"lead at {l.Position}");
        Assert.True((join.Attitude.Rotate(new Vec3(1, 0, 0)) - new Vec3(0, 1, 0)).Length < 1e-3, "joining ship not on the lead's heading");
        // The joined ship is the live one: it can hit the recorded ships; in its slot it does not.
        var (inSlot, _, _) = Cub(at: join.Position, att: join.Attitude);
        Assert.Empty(d.Collisions(inSlot));
        var (onLead, _, _) = Cub(at: l.Position, att: l.Attitude);
        Assert.NotEmpty(d.Collisions(onLead));
        // A session that has no ship flying at the play head has no slot to join.
        p.Seek(40.0); d.Tick(0);
        Assert.Null(p.JoinPose(slot));
    }

    [Fact]
    public void SixShipsLayeredOneAtATimePlayTogetherInTheirSlots()
    {
        var session = new FlightSession { CountdownSec = 0.5 };
        ReplayDirector d = Director();
        var slots = new List<Vec3> { Vec3.Zero };
        for (int k = 1; k < 6; k++) slots.Add(new Vec3(-12.0 * ((k + 1) / 2), 12.0 * ((k + 1) / 2) * (k % 2 == 1 ? 1 : -1), 0));   // a vic of six
        for (int k = 0; k < 6; k++)
        {
            RecordingTake take = RecordingTake.NewTrack(session, "pa18-cub-like", null, 0x100000 * (k + 1), 0.0, slots[k]);
            SessionPlayback? along = k == 0 ? null : d.PlayAlong(take);
            if (along != null) Assert.Equal(k, along.Actors.Count);                // every ship already layered plays beside this take
            var (ac, sim, roll) = k == 0 ? Cub() : Cub(at: take.StartPose!.Value.Position, att: take.StartPose!.Value.Attitude);
            var hold = new ControlInputs(0, roll.Elevator, 0, 0);
            for (int i = 0; i < 226; i++)
            {
                if (take.State == TakeState.Recording) sim.RunFor(1.0 / 50, hold);
                take.Tick(1.0 / 50, ac, Vec3.Zero, RecordFlags.Smoke, 0x100000 * (k + 1));
                d.Tick(1.0 / 50);
            }
            Assert.NotNull(take.Commit());
            if (along != null) d.Stop(along);
        }
        Assert.Equal(6, session.Tracks.Count);
        FlightSession back = RecordingCodec.FromBytes(RecordingCodec.ToBytes(session));
        Assert.Equal(6, back.Tracks.Count);
        SessionPlayback p = d.Play(back, new PlaybackOptions { StartOffsetSec = 3.0 });
        Assert.Equal(6, p.Present().Count());
        Assert.Empty(d.Collisions(null));
        FlightTrack lead = back.Tracks[0];
        lead.TrySampleSession(0.0, out TrackSample l0); lead.TrySampleSession(3.0, out TrackSample l3);
        for (int k = 1; k < 6; k++)
        {
            back.Tracks[k].TrySampleSession(0.0, out TrackSample s0);
            Vec3 drift = (p.Actors[k].Current.State.Position - l3.State.Position) - (s0.State.Position - l0.State.Position);
            Assert.True(drift.Length < 0.05, $"ship {k + 1} drifted {drift.Length:F3} m from its slot");
            Assert.Equal(0x100000 * (k + 1), back.Tracks[k].SmokeRgb);
        }
    }
}

using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

public enum TakeState { Countdown, Recording, Done, Cancelled }

/// <summary>
/// One pass at recording a track into a session while the others play (Ty 2026-11-04: "like a solo musician layering
/// tracks"). A take counts in from its punch-in point with the session clock held there — the other ships wait in
/// their punch-in poses and the live aircraft waits at <see cref="StartPose"/> — then runs the clock and records the
/// live aircraft on it. Nothing in the session changes until <see cref="Commit"/>: a cancelled take leaves it as it was.
/// <list type="bullet">
/// <item><see cref="NewTrack"/> adds a ship: track 1 (an empty session) starts wherever the pilot is; a later one starts
/// in a slot on whichever ship is flying at the punch-in.</item>
/// <item><see cref="Overdub"/> re-flies an existing track from the punch-in on, keeping what it had before that point.
/// The pilot starts where that track was at the punch-in.</item>
/// </list>
/// </summary>
public sealed class RecordingTake
{
    public readonly FlightSession Session;
    /// <summary>The track this take is writing (a working copy when overdubbing).</summary>
    public readonly FlightTrack Track;
    /// <summary>The session track an overdub will replace on commit; null for a new track.</summary>
    public readonly FlightTrack? Replacing;
    public readonly double PunchInSec;
    /// <summary>Where the live aircraft should be put for the count-in (null: leave it where it is).</summary>
    public readonly RigidBodyState? StartPose;
    public TakeState State { get; private set; }
    public double CountdownLeft { get; private set; }
    /// <summary>Session time now: the playback's clock follows it.</summary>
    public double Clock { get; private set; }
    private readonly TrackRecorder _rec;

    private RecordingTake(FlightSession session, FlightTrack track, FlightTrack? replacing, double punchInSec, RigidBodyState? start, double countdownSec)
    {
        Session = session; Track = track; Replacing = replacing; PunchInSec = punchInSec; StartPose = start;
        Clock = punchInSec;
        CountdownLeft = System.Math.Max(0.0, countdownSec);
        State = TakeState.Countdown;
        _rec = new TrackRecorder(track);
    }

    /// <summary>A take that adds a new ship of type <paramref name="aircraftId"/> at session time <paramref name="punchInSec"/>,
    /// starting in <paramref name="slotBody"/> on the lead (see <see cref="FlightSession.JoinPose"/>).</summary>
    public static RecordingTake NewTrack(FlightSession session, string aircraftId, string? liveryId, int smokeRgb, double punchInSec, Vec3 slotBody)
    {
        var t = new FlightTrack
        {
            Name = $"Ship {session.Tracks.Count + 1}", AircraftId = aircraftId, LiveryId = liveryId, SmokeRgb = smokeRgb,
            StartSec = punchInSec, RecordedUtcTicks = System.DateTime.UtcNow.Ticks,
        };
        RigidBodyState? start = session.JoinPose(punchInSec, slotBody);
        double countdown = session.Tracks.Count == 0 ? 0.0 : session.CountdownSec;
        return new RecordingTake(session, t, null, punchInSec, start, countdown);
    }

    /// <summary>A take that re-flies <paramref name="track"/> from <paramref name="punchInSec"/> on.</summary>
    public static RecordingTake Overdub(FlightSession session, FlightTrack track, double punchInSec)
    {
        if (!session.Tracks.Contains(track)) throw new System.ArgumentException("track is not in this session");
        FlightTrack work = track.CloneHeader();
        work.Origin = track.Origin;
        work.Frames.AddRange(track.Frames);
        work.Events.AddRange(track.Events);
        RigidBodyState? start = null;
        if (punchInSec > track.BeginSec && track.FrameCount > 0)
        {
            track.TrySampleSession(punchInSec, out TrackSample s);
            start = s.State;
        }
        work.TruncateAtSession(punchInSec);
        if (work.FrameCount == 0) { work.StartSec = punchInSec; work.NudgeSec = 0; }
        if (start == null)
        {
            // Re-flying from its very start: begin where it began (or in a slot if it never had a frame).
            if (track.FrameCount > 0 && punchInSec <= track.BeginSec) start = track.Frame(0).State;
        }
        return new RecordingTake(session, work, track, punchInSec, start, session.CountdownSec);
    }

    /// <summary>One display frame. Count-in first (clock held), then the clock runs and the live aircraft is recorded.</summary>
    public void Tick(double dt, Aircraft live, Vec3 windWorld, RecordFlags flags, int smokeRgb)
    {
        if (State == TakeState.Countdown)
        {
            CountdownLeft -= dt;
            if (CountdownLeft > 0) return;
            CountdownLeft = 0;
            State = TakeState.Recording;
            _rec.Feed(Clock, live, windWorld, flags, smokeRgb);   // the frame at the punch-in itself
            return;
        }
        if (State != TakeState.Recording) return;
        Clock += dt;
        _rec.Feed(Clock, live, windWorld, flags, smokeRgb);
    }

    /// <summary>Stop and put the take into the session (replacing the overdubbed track in its place). Returns the track,
    /// or null if nothing was recorded (a take stopped during its count-in changes nothing).</summary>
    public FlightTrack? Commit()
    {
        if (State is TakeState.Done or TakeState.Cancelled) return null;
        bool recorded = State == TakeState.Recording && Track.FrameCount >= 2;
        State = TakeState.Done;
        if (!recorded) return null;
        Track.RecordedUtcTicks = System.DateTime.UtcNow.Ticks;
        if (Replacing != null)
        {
            int i = Session.Tracks.IndexOf(Replacing);
            if (i >= 0) Session.Tracks[i] = Track; else Session.Tracks.Add(Track);
        }
        else Session.Tracks.Add(Track);
        Session.Touch();
        return Track;
    }

    public void Cancel() { if (State is TakeState.Countdown or TakeState.Recording) State = TakeState.Cancelled; }
}

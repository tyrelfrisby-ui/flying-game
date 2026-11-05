using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// A recording as the pilot thinks of it (Ty 2026-11-04): a SESSION is a set of tracks on one shared timeline, like a
/// solo musician layering parts. Track 1 is usually the lead; each further track was flown while the others played
/// back from the same cue. A single recorded flight is simply a session with one track. Session time 0 is the cue.
/// </summary>
public sealed class FlightSession
{
    /// <summary>Stable id (file name in the library); never changes on rename.</summary>
    public string Id = System.Guid.NewGuid().ToString("N");
    public string Name = "Recording";
    /// <summary>The world it was flown in ("island", "legacy", …). Original-location playback only makes sense there.</summary>
    public string WorldId = "";
    public long CreatedUtcTicks = System.DateTime.UtcNow.Ticks;
    /// <summary>The count-in before a take (s): the other tracks wait at the punch-in pose while it runs down.</summary>
    public double CountdownSec = 5.0;
    /// <summary>Recorded ships collide with each other. Off by default: the tracks were flown separately, so two ships
    /// that overlapped by a wingspan in the playback never actually touched.</summary>
    public bool CollideRecordedShips;

    public readonly List<FlightTrack> Tracks = new();

    public bool AnySolo { get { foreach (FlightTrack t in Tracks) if (t.Solo) return true; return false; } }

    /// <summary>Mute / solo as on a mixing desk: with any track soloed only the soloed tracks play; otherwise every
    /// track that is not muted plays.</summary>
    public bool Plays(FlightTrack t) => AnySolo ? t.Solo : !t.Muted;

    /// <summary>The end of the last playing track (s).</summary>
    public double Duration
    {
        get
        {
            double end = 0;
            foreach (FlightTrack t in Tracks) if (Plays(t) && t.FrameCount > 0) end = System.Math.Max(end, t.EndSec);
            return end;
        }
    }

    /// <summary>The first playing track that is in the air at <paramref name="sessionSec"/> (the formation's reference),
    /// skipping <paramref name="except"/>.</summary>
    public FlightTrack? LeadAt(double sessionSec, FlightTrack? except = null)
    {
        foreach (FlightTrack t in Tracks)
        {
            if (t == except || !Plays(t) || t.FrameCount == 0) continue;
            if (sessionSec >= t.BeginSec - 1e-9 && sessionSec <= t.EndSec + 1e-9) return t;
        }
        return null;
    }

    /// <summary>A new, empty track for the next ship, starting at session time <paramref name="startSec"/>.</summary>
    public FlightTrack AddTrack(string aircraftId, string? liveryId, double startSec = 0.0)
    {
        var t = new FlightTrack
        {
            Name = $"Ship {Tracks.Count + 1}", AircraftId = aircraftId, LiveryId = liveryId, StartSec = startSec,
            RecordedUtcTicks = System.DateTime.UtcNow.Ticks,
        };
        Tracks.Add(t);
        return t;
    }

    public bool DeleteTrack(FlightTrack t) => Tracks.Remove(t);

    /// <summary>Where a live pilot should start to fly slot <paramref name="slotBody"/> (lead body axes: x forward, y
    /// right, z down, m) on the lead at <paramref name="sessionSec"/>: the lead's attitude and velocity, offset into the
    /// slot. Null when no track is flying then (the first track starts wherever the pilot is).</summary>
    public RigidBodyState? JoinPose(double sessionSec, Vec3 slotBody, FlightTrack? except = null)
    {
        FlightTrack? lead = LeadAt(sessionSec, except);
        if (lead == null) return null;
        lead.TrySampleSession(sessionSec, out TrackSample s);
        RigidBodyState l = s.State;
        return new RigidBodyState(l.Position + l.Attitude.Rotate(slotBody), l.Attitude, l.Velocity, l.Rates);
    }

    /// <summary>Echelon right, one span back and one span out: a safe first slot for a new ship.</summary>
    public static Vec3 DefaultSlot(double spanM) => new(-spanM, spanM, 0.0);
}

using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// Writes a live flight into a <see cref="FlightTrack"/> on the track's fixed grid. The game feeds it once per display
/// frame at whatever rate the display runs (72 Hz on the Quest, 60 or 120 on an iPhone, a hitch now and then); every
/// grid instant that frame passed is filled by interpolating between this feed and the last, so the file is truly
/// fixed-rate and its timing does not depend on the device that flew it.
/// </summary>
public sealed class TrackRecorder
{
    public readonly FlightTrack Track;
    private bool _havePrev;
    private double _prevT;
    private TrackSample _prev;
    private readonly HashSet<AirframeComponent> _lostSeen = new();
    private int _smokeRgb;

    /// <summary>Record into <paramref name="track"/>, continuing its grid where its frames end (a fresh track starts at
    /// its <see cref="FlightTrack.BeginSec"/>; a truncated one for a punch-in carries on from the cut).</summary>
    public TrackRecorder(FlightTrack track)
    {
        Track = track;
        _smokeRgb = track.SmokeRgbAt(int.MaxValue);
    }

    /// <summary>Session time of the next grid frame to be written.</summary>
    public double NextFrameSec => Track.BeginSec + Track.FrameCount / Track.SampleHz;

    /// <summary>Feed the live state at session time <paramref name="sessionSec"/>. Returns frames written.</summary>
    public int Feed(double sessionSec, in TrackSample s)
    {
        int written = 0;
        while (NextFrameSec <= sessionSec + 1e-9)
        {
            double tg = NextFrameSec;
            TrackSample f;
            if (!_havePrev || sessionSec - _prevT < 1e-9) f = s;
            else
            {
                double span = sessionSec - _prevT;
                double u = System.Math.Clamp((tg - _prevT) / span, 0.0, 1.0);
                f = TrackSample.Interpolate(_prev, s, u, span);
            }
            Track.Append(f);
            written++;
            if (written > 100000) break;   // a clock gone mad must not fill the heap
        }
        _prev = s; _prevT = sessionSec; _havePrev = true;
        return written;
    }

    /// <summary>Feed the live aircraft, also logging any part it has lost since the last feed and a smoke colour change.</summary>
    public int Feed(double sessionSec, Aircraft a, Vec3 windWorld, RecordFlags flags, int smokeRgb)
    {
        int n = Feed(sessionSec, TrackSample.Capture(a, windWorld, flags));
        int frame = System.Math.Max(0, Track.FrameCount - 1);
        foreach (AirframeComponent c in a.LostComponents)
            if (_lostSeen.Add(c)) Track.Events.Add(new TrackEvent(frame, TrackEventKind.PartLost, (int)c));
        if (smokeRgb != _smokeRgb)
        {
            _smokeRgb = smokeRgb;
            if (Track.FrameCount <= 1) Track.SmokeRgb = smokeRgb;
            else Track.Events.Add(new TrackEvent(frame, TrackEventKind.SmokeColour, smokeRgb));
        }
        return n;
    }
}

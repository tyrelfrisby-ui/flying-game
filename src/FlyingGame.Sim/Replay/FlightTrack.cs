using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

/// <summary>Switches recorded with each frame. Stepped, never interpolated: a frame's flags hold until the next frame.</summary>
[System.Flags]
public enum RecordFlags : byte
{
    None = 0,
    Smoke = 1 << 0,
    PilotOut = 1 << 1,
    NavLights = 1 << 2,
    Strobes = 1 << 3,
    LandingLight = 1 << 4,
    GunsFiring = 1 << 5,
    OnGround = 1 << 6,
}

/// <summary>One recorded instant at full precision: what a playback actor is put into and what the visual reads.
/// Position is world NED (m), attitude body→world, velocity and rates in BODY axes, as <see cref="RigidBodyState"/>.</summary>
public readonly struct TrackSample
{
    public readonly RigidBodyState State;
    public readonly ControlDeflections Deflections;
    public readonly double Throttle01, GearExtension, EngineRpm, LoadFactorZ;
    public readonly Vec3 WindWorld;
    public readonly RecordFlags Flags;

    public TrackSample(RigidBodyState state, ControlDeflections deflections, double throttle01, double gearExtension,
        double engineRpm, double loadFactorZ, Vec3 windWorld, RecordFlags flags)
    {
        State = state; Deflections = deflections; Throttle01 = throttle01; GearExtension = gearExtension;
        EngineRpm = engineRpm; LoadFactorZ = loadFactorZ; WindWorld = windWorld; Flags = flags;
    }

    /// <summary>What the live aircraft is doing now. Flap and slat travel ride in the deflections (the ones the visual
    /// reads), so a flap the cockpit set is recorded even though <see cref="Aircraft.CurrentDeflections"/> omits it.</summary>
    public static TrackSample Capture(Aircraft a, Vec3 windWorld, RecordFlags flags)
    {
        ControlDeflections d = a.CurrentDeflections;
        var full = new ControlDeflections(d.AileronRad, d.ElevatorRad, d.RudderRad, d.SpoilerFraction, a.FlapFraction, d.SlatFraction);
        double rpm = a.Config.Propulsion is null ? 0.0 : a.EngineRpm;
        return new TrackSample(a.State, full, a.Throttle01, a.GearExtension, rpm, a.LoadFactorZ, windWorld, flags);
    }

    public bool Has(RecordFlags f) => (Flags & f) != 0;

    /// <summary>Velocity in world NED (m/s).</summary>
    public Vec3 WorldVelocity => State.Attitude.Rotate(State.Velocity);

    /// <summary>Between two samples <paramref name="dt"/> s apart. Position is a cubic Hermite through both ends' WORLD
    /// velocities (a 30 Hz linear chord cuts a 4 g turn by centimetres; the Hermite follows it), attitude is slerped, the
    /// rest is linear. Flags step: they are <paramref name="a"/>'s until u reaches 1.</summary>
    public static TrackSample Interpolate(in TrackSample a, in TrackSample b, double u, double dt)
    {
        if (u <= 0) return a;
        if (u >= 1) return b;
        Vec3 p = Hermite(a.State.Position, a.WorldVelocity * dt, b.State.Position, b.WorldVelocity * dt, u);
        var s = new RigidBodyState(p, FlightRecorder.Slerp(a.State.Attitude, b.State.Attitude, u),
            L(a.State.Velocity, b.State.Velocity, u), L(a.State.Rates, b.State.Rates, u));
        ControlDeflections da = a.Deflections, db = b.Deflections;
        var d = new ControlDeflections(L(da.AileronRad, db.AileronRad, u), L(da.ElevatorRad, db.ElevatorRad, u),
            L(da.RudderRad, db.RudderRad, u), L(da.SpoilerFraction, db.SpoilerFraction, u), L(da.FlapFraction, db.FlapFraction, u),
            L(da.SlatFraction, db.SlatFraction, u));
        return new TrackSample(s, d, L(a.Throttle01, b.Throttle01, u), L(a.GearExtension, b.GearExtension, u),
            L(a.EngineRpm, b.EngineRpm, u), L(a.LoadFactorZ, b.LoadFactorZ, u), L(a.WindWorld, b.WindWorld, u), a.Flags);
    }

    /// <summary>Cubic Hermite: p0 → p1 with end tangents m0, m1 (already scaled by the interval).</summary>
    public static Vec3 Hermite(Vec3 p0, Vec3 m0, Vec3 p1, Vec3 m1, double u)
    {
        double u2 = u * u, u3 = u2 * u;
        double h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
        return p0 * h00 + m0 * h10 + p1 * h01 + m1 * h11;
    }

    private static double L(double a, double b, double u) => a + (b - a) * u;
    private static Vec3 L(Vec3 a, Vec3 b, double u) => new(L(a.X, b.X, u), L(a.Y, b.Y, u), L(a.Z, b.Z, u));
}

/// <summary>
/// One frame as it is stored, in memory and on disk alike: 54 bytes, quantised once when it is recorded, so a track
/// played straight after recording and the same track loaded from a file next week are the same numbers. Position is
/// a float offset from the track's double <see cref="FlightTrack.Origin"/> (4 mm at 50 km); everything else is a fixed
/// step chosen against the fleet's envelope (see docs/FLIGHT-RECORDER.md, "The frame").
/// </summary>
public struct PackedFrame
{
    public const int Bytes = 54;
    public const double QuatStep = 1.0 / 32767.0, VelStep = 0.02, RateStep = 0.001, SurfaceStep = 1e-4, GStep = 1e-3, WindStep = 0.01;

    public float Px, Py, Pz;
    public short Qx, Qy, Qz, Qw;
    public short Ux, Uy, Uz;
    public short P, Q, R;
    public short Aileron, Elevator, Rudder;
    public byte Spoiler, Flap, Slat, Throttle, Gear;
    public ushort Rpm;
    public short Nz;
    public short Wx, Wy, Wz;
    public byte Flags;

    public static PackedFrame Pack(in TrackSample s, Vec3 origin)
    {
        Quat q = s.State.Attitude.Normalized();
        if (q.W < 0) q = new Quat(-q.X, -q.Y, -q.Z, -q.W);   // one hemisphere: the slerp takes the short way regardless
        Vec3 rel = s.State.Position - origin;
        ControlDeflections d = s.Deflections;
        return new PackedFrame
        {
            Px = (float)rel.X, Py = (float)rel.Y, Pz = (float)rel.Z,
            Qx = S16(q.X / QuatStep), Qy = S16(q.Y / QuatStep), Qz = S16(q.Z / QuatStep), Qw = S16(q.W / QuatStep),
            Ux = S16(s.State.Velocity.X / VelStep), Uy = S16(s.State.Velocity.Y / VelStep), Uz = S16(s.State.Velocity.Z / VelStep),
            P = S16(s.State.Rates.X / RateStep), Q = S16(s.State.Rates.Y / RateStep), R = S16(s.State.Rates.Z / RateStep),
            Aileron = S16(d.AileronRad / SurfaceStep), Elevator = S16(d.ElevatorRad / SurfaceStep), Rudder = S16(d.RudderRad / SurfaceStep),
            Spoiler = U8(d.SpoilerFraction), Flap = U8(d.FlapFraction), Slat = U8(d.SlatFraction), Throttle = U8(s.Throttle01), Gear = U8(s.GearExtension),
            Rpm = (ushort)System.Math.Clamp(System.Math.Round(s.EngineRpm), 0, ushort.MaxValue),
            Nz = S16(s.LoadFactorZ / GStep),
            Wx = S16(s.WindWorld.X / WindStep), Wy = S16(s.WindWorld.Y / WindStep), Wz = S16(s.WindWorld.Z / WindStep),
            Flags = (byte)s.Flags,
        };
    }

    public TrackSample Unpack(Vec3 origin)
    {
        var q = new Quat(Qx * QuatStep, Qy * QuatStep, Qz * QuatStep, Qw * QuatStep).Normalized();
        var state = new RigidBodyState(origin + new Vec3(Px, Py, Pz), q,
            new Vec3(Ux * VelStep, Uy * VelStep, Uz * VelStep), new Vec3(P * RateStep, Q * RateStep, R * RateStep));
        var d = new ControlDeflections(Aileron * SurfaceStep, Elevator * SurfaceStep, Rudder * SurfaceStep, Spoiler / 255.0, Flap / 255.0, Slat / 255.0);
        return new TrackSample(state, d, Throttle / 255.0, Gear / 255.0, Rpm, Nz * GStep,
            new Vec3(Wx * WindStep, Wy * WindStep, Wz * WindStep), (RecordFlags)Flags);
    }

    private static short S16(double v) => (short)System.Math.Clamp(System.Math.Round(v), short.MinValue, short.MaxValue);
    private static byte U8(double f) => (byte)System.Math.Clamp(System.Math.Round(f * 255.0), 0, 255);
}

public enum TrackEventKind : byte
{
    /// <summary>A part came off (Arg = <see cref="AirframeComponent"/>). Played back so a recorded crash or shoot-down
    /// sheds the same parts at the same moment.</summary>
    PartLost = 1,
    /// <summary>The smoke colour changed (Arg = 0xRRGGBB).</summary>
    SmokeColour = 2,
}

/// <summary>Something that happens once, at a frame, rather than every frame.</summary>
public readonly struct TrackEvent
{
    public readonly int Frame; public readonly TrackEventKind Kind; public readonly int Arg;
    public TrackEvent(int frame, TrackEventKind kind, int arg) { Frame = frame; Kind = kind; Arg = arg; }
}

/// <summary>
/// One aircraft's recorded flight: who flew what, where it sits on its session's timeline, and its frames at a fixed
/// rate (frame k is at <see cref="StartSec"/> + <see cref="NudgeSec"/> + k / <see cref="SampleHz"/> session seconds).
/// </summary>
public sealed class FlightTrack
{
    public const double DefaultSampleHz = 30.0;

    public string Name = "Track";
    public string AircraftId = "";
    /// <summary>The livery id the pilot flew (null = the type's own). Applied on playback when that aircraft still offers it.</summary>
    public string? LiveryId;
    /// <summary>Smoke colour at the start of the track, 0xRRGGBB (later changes are <see cref="TrackEventKind.SmokeColour"/> events).</summary>
    public int SmokeRgb = 0xFFFFFF;
    public double SampleHz = DefaultSampleHz;
    /// <summary>Where the track's float positions are measured from (world NED). Set from the first frame.</summary>
    public Vec3 Origin;
    /// <summary>Session time of frame 0 (s). A punch-in take starts later than 0.</summary>
    public double StartSec;
    /// <summary>The time-offset nudge (s): the pilot slides one track against the others without re-flying it.</summary>
    public double NudgeSec;
    public bool Muted, Solo;
    /// <summary>When the track was flown (UTC ticks), for the library's list.</summary>
    public long RecordedUtcTicks;

    public readonly List<PackedFrame> Frames = new();
    public readonly List<TrackEvent> Events = new();

    public int FrameCount => Frames.Count;
    public double Duration => Frames.Count < 2 ? 0 : (Frames.Count - 1) / SampleHz;
    /// <summary>Session time of the first frame, nudge included.</summary>
    public double BeginSec => StartSec + NudgeSec;
    public double EndSec => BeginSec + Duration;

    public TrackSample Frame(int k) => Frames[k].Unpack(Origin);

    /// <summary>Append a frame. The first one fixes the origin (rounded to the metre so the file reads cleanly).</summary>
    public void Append(in TrackSample s)
    {
        if (Frames.Count == 0)
        {
            Vec3 p = s.State.Position;
            Origin = new Vec3(System.Math.Round(p.X), System.Math.Round(p.Y), System.Math.Round(p.Z));
        }
        Frames.Add(PackedFrame.Pack(s, Origin));
    }

    /// <summary>The track at <paramref name="localSec"/> seconds after its own frame 0, clamped to the recording.</summary>
    public TrackSample SampleLocal(double localSec)
    {
        if (Frames.Count == 0) throw new System.InvalidOperationException("empty track");
        double f = localSec * SampleHz;
        if (f <= 0) return Frame(0);
        int last = Frames.Count - 1;
        if (f >= last) return Frame(last);
        int k = (int)System.Math.Floor(f);
        TrackSample a = Frame(k), b = Frame(k + 1);
        return TrackSample.Interpolate(a, b, f - k, 1.0 / SampleHz);
    }

    /// <summary>The track at session time <paramref name="sessionSec"/>; false (and frame 0 or the last frame) outside it.</summary>
    public bool TrySampleSession(double sessionSec, out TrackSample s)
    {
        double local = sessionSec - BeginSec;
        if (Frames.Count == 0) { s = default; return false; }
        s = SampleLocal(local);
        return local >= 0 && local <= Duration;
    }

    /// <summary>The frame index the play head is on at local time <paramref name="localSec"/> (for events).</summary>
    public int FrameAt(double localSec) => (int)System.Math.Clamp(System.Math.Floor(localSec * SampleHz + 1e-9), 0, System.Math.Max(0, Frames.Count - 1));

    /// <summary>The smoke colour in force at frame <paramref name="frame"/>.</summary>
    public int SmokeRgbAt(int frame)
    {
        int rgb = SmokeRgb;
        foreach (TrackEvent e in Events) { if (e.Frame > frame) break; if (e.Kind == TrackEventKind.SmokeColour) rgb = e.Arg; }
        return rgb;
    }

    /// <summary>Keep frames before session time <paramref name="sessionSec"/> and drop the rest (and their events): the
    /// first half of a punch-in.</summary>
    public void TruncateAtSession(double sessionSec)
    {
        int keep = (int)System.Math.Clamp(System.Math.Ceiling((sessionSec - BeginSec) * SampleHz - 1e-9), 0, Frames.Count);
        Frames.RemoveRange(keep, Frames.Count - keep);
        Events.RemoveAll(e => e.Frame >= keep);
    }

    /// <summary>Copy of everything but the frames and events (a take's new track starts from its target's settings).</summary>
    public FlightTrack CloneHeader() => new()
    {
        Name = Name, AircraftId = AircraftId, LiveryId = LiveryId, SmokeRgb = SmokeRgb, SampleHz = SampleHz,
        StartSec = StartSec, NudgeSec = NudgeSec, Muted = Muted, Solo = Solo, RecordedUtcTicks = RecordedUtcTicks,
    };
}

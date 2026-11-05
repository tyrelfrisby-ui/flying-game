using System.IO;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// The session file (.fgrec): little-endian binary, versioned, ending in a trailer so a file cut short by a full disk or
/// a killed app is refused rather than half-played. Layout in docs/FLIGHT-RECORDER.md, "The file". BinaryWriter and
/// BinaryReader only: no JSON library (System.Text.Json is not in Unity) and no compression stream, so the same bytes
/// are read on iOS, the Mac, the Quest and the test suite.
/// </summary>
public static class RecordingCodec
{
    public const uint Magic = 0x43524746;     // "FGRC"
    public const uint Trailer = 0x21444E45;   // "END!"
    public const ushort Version = 1;
    public const int MaxTracks = 64;
    public const int MaxFrames = 30 * 3600 * 6;   // six hours at 30 Hz per track
    public const int MaxEvents = 100000;

    public static void Write(Stream s, FlightSession session)
    {
        using var w = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(session.Id); w.Write(session.Name); w.Write(session.WorldId);
        w.Write(session.CreatedUtcTicks); w.Write(session.CountdownSec); w.Write(session.CollideRecordedShips);
        w.Write(session.Tracks.Count);
        foreach (FlightTrack t in session.Tracks)
        {
            w.Write(t.Name); w.Write(t.AircraftId);
            w.Write(t.LiveryId != null); if (t.LiveryId != null) w.Write(t.LiveryId);
            w.Write(t.SmokeRgb); w.Write(t.SampleHz);
            w.Write(t.Origin.X); w.Write(t.Origin.Y); w.Write(t.Origin.Z);
            w.Write(t.StartSec); w.Write(t.NudgeSec); w.Write(t.Muted); w.Write(t.Solo); w.Write(t.RecordedUtcTicks);
            w.Write(t.Frames.Count);
            foreach (PackedFrame f in t.Frames) WriteFrame(w, f);
            w.Write(t.Events.Count);
            foreach (TrackEvent e in t.Events) { w.Write(e.Frame); w.Write((byte)e.Kind); w.Write(e.Arg); }
        }
        w.Write(Trailer);
    }

    public static FlightSession Read(Stream s)
    {
        using var r = new BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a flight recording");
        ushort v = r.ReadUInt16();
        if (v > Version) throw new InvalidDataException($"recording version {v} is newer than this game ({Version})");
        var session = new FlightSession
        {
            Id = r.ReadString(), Name = r.ReadString(), WorldId = r.ReadString(),
            CreatedUtcTicks = r.ReadInt64(), CountdownSec = r.ReadDouble(), CollideRecordedShips = r.ReadBoolean(),
        };
        int nt = r.ReadInt32();
        if (nt < 0 || nt > MaxTracks) throw new InvalidDataException($"bad track count {nt}");
        for (int i = 0; i < nt; i++)
        {
            var t = new FlightTrack { Name = r.ReadString(), AircraftId = r.ReadString() };
            if (r.ReadBoolean()) t.LiveryId = r.ReadString();
            t.SmokeRgb = r.ReadInt32(); t.SampleHz = r.ReadDouble();
            if (!(t.SampleHz > 0 && t.SampleHz <= 1000)) throw new InvalidDataException($"bad sample rate {t.SampleHz}");
            t.Origin = new Vec3(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
            t.StartSec = r.ReadDouble(); t.NudgeSec = r.ReadDouble(); t.Muted = r.ReadBoolean(); t.Solo = r.ReadBoolean();
            t.RecordedUtcTicks = r.ReadInt64();
            int nf = r.ReadInt32();
            if (nf < 0 || nf > MaxFrames) throw new InvalidDataException($"bad frame count {nf}");
            t.Frames.Capacity = nf;
            for (int k = 0; k < nf; k++) t.Frames.Add(ReadFrame(r));
            int ne = r.ReadInt32();
            if (ne < 0 || ne > MaxEvents) throw new InvalidDataException($"bad event count {ne}");
            for (int k = 0; k < ne; k++) t.Events.Add(new TrackEvent(r.ReadInt32(), (TrackEventKind)r.ReadByte(), r.ReadInt32()));
            session.Tracks.Add(t);
        }
        if (r.ReadUInt32() != Trailer) throw new InvalidDataException("recording is truncated");
        return session;
    }

    /// <summary>The library list's line for a file, without reading its frames (seeks past them).</summary>
    public static RecordingSummary ReadSummary(Stream s)
    {
        using var r = new BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a flight recording");
        ushort v = r.ReadUInt16();
        if (v > Version) throw new InvalidDataException($"recording version {v} is newer than this game ({Version})");
        var sum = new RecordingSummary
        {
            Id = r.ReadString(), Name = r.ReadString(), WorldId = r.ReadString(), CreatedUtcTicks = r.ReadInt64(),
        };
        r.ReadDouble(); r.ReadBoolean();
        int nt = r.ReadInt32();
        if (nt < 0 || nt > MaxTracks) throw new InvalidDataException($"bad track count {nt}");
        for (int i = 0; i < nt; i++)
        {
            r.ReadString(); string ac = r.ReadString();
            if (r.ReadBoolean()) r.ReadString();
            r.ReadInt32(); double hz = r.ReadDouble();
            r.ReadDouble(); r.ReadDouble(); r.ReadDouble();
            double start = r.ReadDouble(), nudge = r.ReadDouble(); r.ReadBoolean(); r.ReadBoolean(); r.ReadInt64();
            int nf = r.ReadInt32();
            if (nf < 0 || nf > MaxFrames || !(hz > 0)) throw new InvalidDataException("bad track header");
            s.Seek((long)nf * PackedFrame.Bytes, SeekOrigin.Current);
            int ne = r.ReadInt32();
            if (ne < 0 || ne > MaxEvents) throw new InvalidDataException($"bad event count {ne}");
            s.Seek((long)ne * 9, SeekOrigin.Current);
            sum.AircraftIds.Add(ac);
            if (nf > 1) sum.DurationSec = System.Math.Max(sum.DurationSec, start + nudge + (nf - 1) / hz);
        }
        if (r.ReadUInt32() != Trailer) throw new InvalidDataException("recording is truncated");
        return sum;
    }

    public static byte[] ToBytes(FlightSession session)
    {
        using var ms = new MemoryStream();
        Write(ms, session);
        return ms.ToArray();
    }

    public static FlightSession FromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return Read(ms);
    }

    private static void WriteFrame(BinaryWriter w, in PackedFrame f)
    {
        w.Write(f.Px); w.Write(f.Py); w.Write(f.Pz);
        w.Write(f.Qx); w.Write(f.Qy); w.Write(f.Qz); w.Write(f.Qw);
        w.Write(f.Ux); w.Write(f.Uy); w.Write(f.Uz);
        w.Write(f.P); w.Write(f.Q); w.Write(f.R);
        w.Write(f.Aileron); w.Write(f.Elevator); w.Write(f.Rudder);
        w.Write(f.Spoiler); w.Write(f.Flap); w.Write(f.Slat); w.Write(f.Throttle); w.Write(f.Gear);
        w.Write(f.Rpm); w.Write(f.Nz);
        w.Write(f.Wx); w.Write(f.Wy); w.Write(f.Wz);
        w.Write(f.Flags);
    }

    private static PackedFrame ReadFrame(BinaryReader r) => new()
    {
        Px = r.ReadSingle(), Py = r.ReadSingle(), Pz = r.ReadSingle(),
        Qx = r.ReadInt16(), Qy = r.ReadInt16(), Qz = r.ReadInt16(), Qw = r.ReadInt16(),
        Ux = r.ReadInt16(), Uy = r.ReadInt16(), Uz = r.ReadInt16(),
        P = r.ReadInt16(), Q = r.ReadInt16(), R = r.ReadInt16(),
        Aileron = r.ReadInt16(), Elevator = r.ReadInt16(), Rudder = r.ReadInt16(),
        Spoiler = r.ReadByte(), Flap = r.ReadByte(), Slat = r.ReadByte(), Throttle = r.ReadByte(), Gear = r.ReadByte(),
        Rpm = r.ReadUInt16(), Nz = r.ReadInt16(),
        Wx = r.ReadInt16(), Wy = r.ReadInt16(), Wz = r.ReadInt16(),
        Flags = r.ReadByte(),
    };
}

/// <summary>One line of the recordings list.</summary>
public sealed class RecordingSummary
{
    public string Id = "", Name = "", WorldId = "";
    public long CreatedUtcTicks;
    public double DurationSec;
    public readonly List<string> AircraftIds = new();
    public int TrackCount => AircraftIds.Count;
}

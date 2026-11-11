using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Recording;
using FlyingGame.Sim;
using FlyingGame.Sim.Recording;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// RECORD A REAL FLIGHT, THEN READ IT BACK THE WAY A HUMAN WOULD. The recorder and the converter are two halves of
/// one deliverable: a recording nobody can convert is not data, it is a file. This flies a Pitts, writes the
/// binary, parses it with the same logic recdump uses, and checks the numbers that come out are the numbers that
/// went in — including that a burst sample is labelled as one.
/// </summary>
public class RecDumpRoundTripTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static int IndexOfBytes(byte[] hay, string s)
    {
        byte[] n = Encoding.UTF8.GetBytes(s);
        for (int i = 0; i <= hay.Length - n.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < n.Length; j++) if (hay[i + j] != n[j]) { hit = false; break; }
            if (hit) return i;
        }
        return -1;
    }

    [Fact]
    public void AFlightRecordsAndReadsBackWithItsChannelsNamedAndItsBurstLabelled()
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(
            Path.Combine(RepoRoot(), "configs", "aircraft", "pitts-s2b-like.json"));
        var a = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -300), Quat.Identity, new Vec3(60, 0, 0), Vec3.Zero),
                             ControlDeflections.Neutral);
        var rec = new FlightTestRecorder(capacityBytes: 32 * 1024 * 1024);
        var inputs = new ControlInputs(0.1, -0.05, 0.0, -0.9);
        const double dt = 1.0 / 200.0;

        for (int i = 0; i < 400; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 190.0, 0); }
        rec.Mark(FlightRecording.Event.PilotMark);
        for (int i = 0; i < 400; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 190.0, 0); }

        byte[] file = rec.BuildFile(new[]
        {
            new KeyValuePair<string, string>("aircraftFlown", cfg.Id),
            new KeyValuePair<string, string>("modelDrawn", cfg.Id),
            new KeyValuePair<string, string>("build", "2026.1111.test"),
        });

        // ---- parse it exactly as recdump does -------------------------------------------------
        int headerEnd = IndexOfBytes(file, FlightRecording.EndHeader) + FlightRecording.EndHeader.Length;
        int channelsEnd = IndexOfBytes(file, FlightRecording.EndChannels);
        int bodyAt = channelsEnd + FlightRecording.EndChannels.Length;
        Assert.True(headerEnd > 0 && channelsEnd > headerEnd);

        string table = Encoding.UTF8.GetString(file, headerEnd, channelsEnd - headerEnd);
        var names = new List<string>();
        var units = new List<string>();
        foreach (string line in table.Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith("channels:", StringComparison.Ordinal)) continue;
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            names.Add(line.Substring(0, tab));
            units.Add(line.Substring(tab + 1).TrimEnd('\r'));
        }
        Assert.Equal(FlightTestChannels.Count, names.Count);

        int recBytes = FlightRecording.RecordBytes(names.Count);
        int records = (file.Length - bodyAt) / recBytes;
        Assert.Equal(0, (file.Length - bodyAt) % recBytes);
        Assert.Equal(rec.Ring.Count, records);

        // ---- the numbers mean what the column says ----------------------------------------------
        int altCol = names.IndexOf("altitude");
        int iasCol = names.IndexOf("ias");
        int leverCol = names.IndexOf("inThrottleLever");
        Assert.True(altCol >= 0 && iasCol >= 0 && leverCol >= 0);
        Assert.Equal("m", units[altCol]);
        Assert.Equal("m/s", units[iasCol]);

        int bursts = 0, marks = 0;
        float firstT = BitConverter.ToSingle(file, bodyAt);
        float lastT = BitConverter.ToSingle(file, bodyAt + (records - 1) * recBytes);
        for (int r = 0; r < records; r++)
        {
            int at = bodyAt + r * recBytes;
            if (file[at + 4] == FlightRecording.RateBurst) bursts++;
            if ((file[at + 6] | (file[at + 7] << 8)) != 0) marks++;

            float alt = BitConverter.ToSingle(file, at + FlightRecording.PreambleBytes + 4 * altCol);
            float ias = BitConverter.ToSingle(file, at + FlightRecording.PreambleBytes + 4 * iasCol);
            float lever = BitConverter.ToSingle(file, at + FlightRecording.PreambleBytes + 4 * leverCol);
            Assert.InRange(alt, 100f, 500f);                 // it was put at 300 m and flown for four seconds
            Assert.InRange(ias, 1f, 200f);
            Assert.Equal(-0.9f, lever, 3);                   // the raw inverted lever, recorded as it was given
        }

        Assert.True(lastT > firstT);
        Assert.True(bursts >= 300, $"only {bursts} records were labelled as burst samples");
        Assert.True(marks > 50, $"only {marks} records carried the event — the run-up should be marked too");

        // ---- and the header says what was flown, both identities --------------------------------
        string header = Encoding.UTF8.GetString(file, 0, headerEnd);
        Assert.Contains("aircraftFlown: pitts-s2b-like", header);
        Assert.Contains("modelDrawn: pitts-s2b-like", header);
        Assert.Contains("build: 2026.1111.test", header);
    }

    /// <summary>A file cut short — a phone killed mid-save — must convert the whole records it has rather than
    /// refusing or, worse, reading one record half out of alignment.</summary>
    [Fact]
    public void ATruncatedFileStillYieldsItsWholeRecords()
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(
            Path.Combine(RepoRoot(), "configs", "aircraft", "j3-cub-like.json"));
        var a = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -200), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero),
                             ControlDeflections.Neutral);
        var rec = new FlightTestRecorder(capacityBytes: 8 * 1024 * 1024);
        var inputs = new ControlInputs(0, 0, 0, -0.5);
        for (int i = 0; i < 400; i++) { a.Step(inputs, 1.0 / 200.0); rec.Step(a, inputs, 1.0 / 200.0, Vec3.Zero, 0, 0); }

        byte[] file = rec.BuildFile(new[] { new KeyValuePair<string, string>("aircraftFlown", cfg.Id) });
        var cut = new byte[file.Length - 37];                 // a partial final record
        Buffer.BlockCopy(file, 0, cut, 0, cut.Length);

        int bodyAt = IndexOfBytes(cut, FlightRecording.EndChannels) + FlightRecording.EndChannels.Length;
        int recBytes = FlightRecording.RecordBytes(FlightTestChannels.Count);
        int whole = (cut.Length - bodyAt) / recBytes;
        Assert.Equal(rec.Ring.Count - 1, whole);
        Assert.NotEqual(0, (cut.Length - bodyAt) % recBytes);  // and the remainder is detectable, not silently eaten
    }
}

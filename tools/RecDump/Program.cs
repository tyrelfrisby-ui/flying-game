using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FlyingGame.Core.Recording;

namespace FlyingGame.Tools.RecDump;

/// <summary>
/// TURNS A FLIGHT-TEST RECORDING INTO CSV, so the data can be read in anything.
///
///     dotnet run --project tools/RecDump -- flight.aerorec            # -> flight.csv, prints the header
///     dotnet run --project tools/RecDump -- flight.aerorec --header   # the header only, no CSV
///     dotnet run --project tools/RecDump -- flight.aerorec -o out.csv --events
///
/// The recording is binary so that nothing formats a string inside the physics step; this is the other half of
/// that bargain, and it lives in tools/ rather than in the game because converting is something you do on a
/// computer with the file in front of you.
///
/// It reads the channel table out of the FILE rather than from the game's current schema. That matters: a
/// recording made last month must still convert after a channel has been added, and a converter that assumed
/// today's column list would silently mislabel every column in an older file.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: recdump <file.aerorec> [-o out.csv] [--header] [--events]");
            return 2;
        }

        string path = args[0];
        string? outPath = null;
        bool headerOnly = false, eventsOnly = false;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o": if (++i < args.Length) outPath = args[i]; break;
                case "--header": headerOnly = true; break;
                case "--events": eventsOnly = true; break;
                default: Console.Error.WriteLine("unknown argument: " + args[i]); return 2;
            }
        }

        if (!File.Exists(path)) { Console.Error.WriteLine("no such file: " + path); return 1; }
        byte[] file = File.ReadAllBytes(path);

        if (!StartsWith(file, FlightRecording.Magic))
        {
            Console.Error.WriteLine("not a flight-test recording (bad magic): " + path);
            return 1;
        }

        int headerEnd = IndexOf(file, FlightRecording.EndHeader);
        int channelsEnd = IndexOf(file, FlightRecording.EndChannels);
        if (headerEnd < 0 || channelsEnd < 0) { Console.Error.WriteLine("truncated: no header or channel table"); return 1; }
        headerEnd += FlightRecording.EndHeader.Length;
        int bodyAt = channelsEnd + FlightRecording.EndChannels.Length;

        string header = Encoding.UTF8.GetString(file, 0, headerEnd);
        Console.Write(header);
        if (headerOnly) return 0;

        // The channel table, read from the file itself.
        string tableText = Encoding.UTF8.GetString(file, headerEnd, channelsEnd - headerEnd);
        var names = new List<string>();
        var units = new List<string>();
        foreach (string line in tableText.Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith("channels:", StringComparison.Ordinal)) continue;
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            names.Add(line.Substring(0, tab));
            units.Add(line.Substring(tab + 1).TrimEnd('\r'));
        }

        int recBytes = FlightRecording.RecordBytes(names.Count);
        int bodyBytes = file.Length - bodyAt;
        int records = bodyBytes / recBytes;
        if (records == 0) { Console.Error.WriteLine("no records in " + path); return 1; }
        if (bodyBytes % recBytes != 0)
            Console.Error.WriteLine($"warning: {bodyBytes % recBytes} trailing bytes — the file was cut short; converting the {records} whole records.");

        outPath ??= Path.ChangeExtension(path, ".csv");
        using var w = new StreamWriter(outPath, false, new UTF8Encoding(false));

        // Column titles carry their unit, so a plot axis is labelled without anyone going back to the header.
        w.Write("time_s,rate_hz,events");
        for (int i = 0; i < names.Count; i++)
        {
            w.Write(',');
            w.Write(units[i].Length > 0 ? names[i] + "_" + units[i].Replace(",", "") : names[i]);
        }
        w.Write('\n');

        var sb = new StringBuilder(names.Count * 12);
        int written = 0;
        for (int r = 0; r < records; r++)
        {
            int at = bodyAt + r * recBytes;
            float t = BitConverter.ToSingle(file, at);
            byte rate = file[at + 4];
            int bits = file[at + 6] | (file[at + 7] << 8);
            if (eventsOnly && bits == 0) continue;

            sb.Clear();
            sb.Append(t.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(rate == FlightRecording.RateBurst ? FlightRecording.BurstHz : FlightRecording.BaselineHz).Append(',');
            sb.Append(FlightRecording.EventsToText(bits));
            for (int i = 0; i < names.Count; i++)
            {
                float v = BitConverter.ToSingle(file, at + FlightRecording.PreambleBytes + 4 * i);
                sb.Append(',');
                // A NaN is written as an empty cell: a spreadsheet reads it as a gap, which is what it is, rather
                // than as the word NaN turning the whole column into text.
                if (!float.IsNaN(v)) sb.Append(v.ToString("R", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
            w.Write(sb.ToString());
            written++;
        }

        int bursts = 0, marked = 0;
        for (int r = 0; r < records; r++)
        {
            int at = bodyAt + r * recBytes;
            if (file[at + 4] == FlightRecording.RateBurst) bursts++;
            if ((file[at + 6] | (file[at + 7] << 8)) != 0) marked++;
        }
        float t0 = BitConverter.ToSingle(file, bodyAt);
        float t1 = BitConverter.ToSingle(file, bodyAt + (records - 1) * recBytes);
        Console.WriteLine($"{records} records, {names.Count} channels, {t1 - t0:F1} s "
                        + $"({bursts} at {FlightRecording.BurstHz} Hz, {marked} carrying events) -> {outPath} ({written} rows)");
        return 0;
    }

    private static bool StartsWith(byte[] hay, string s)
    {
        byte[] n = Encoding.UTF8.GetBytes(s);
        if (hay.Length < n.Length) return false;
        for (int i = 0; i < n.Length; i++) if (hay[i] != n[i]) return false;
        return true;
    }

    /// <summary>Found in the BYTES, never in a decoded string: the records are not text, and decoding them shifts
    /// every index past the first non-UTF-8 byte.</summary>
    private static int IndexOf(byte[] hay, string s)
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
}

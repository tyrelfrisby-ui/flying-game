using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlyingGame.Core.Recording;

/// <summary>
/// THE FLIGHT TEST RECORDING FORMAT. Ty, 2026-11-11: "Add a flight test data recorder too with all of the data
/// tracked for review and analysis."
///
/// He flies real flight test, and so do his beta pilots: they fly a card, land, and want to know what the aeroplane
/// ACTUALLY did rather than what it felt like. Everything about this format follows from two things being true —
/// the data has to be TRUSTWORTHY and it has to get OFF THE PHONE.
///
/// A FILE IS: a text header (what was flown, including every non-default Developer knob), then a channel table
/// (name and unit per column), then fixed-width binary records. The header and the table are text so that a file is
/// self-describing — you can read what it is in any editor, years later, without this code — and the records are
/// binary because formatting 150 floats into a string 50 times a second in the physics step is exactly the kind of
/// cost that makes the data describe the recorder instead of the aeroplane.
///
/// THE RATE IS IN EVERY RECORD, not inferred from timestamps. The recorder runs at
/// <see cref="BaselineHz"/> and bursts to <see cref="BurstHz"/> around events, so a reader must be able to see which
/// samples are burst samples; guessing from timestamp spacing is the kind of inference that is wrong exactly once,
/// at the sample that mattered.
/// </summary>
public static class FlightRecording
{
    /// <summary>First bytes of the file; the trailing digit is the format version.</summary>
    public const string Magic = "AERORECORD1\n";

    public const string EndHeader = "--END-HEADER--\n";
    public const string EndChannels = "--END-CHANNELS--\n";

    /// <summary>
    /// THE BASELINE RATE, 50 Hz. The sim steps at 200; recording every step would be 72 MB for ten minutes of the
    /// ~150 channels, which is more than a phone should be asked to keep and more than anyone will read. 50 Hz
    /// resolves every rigid-body mode on this fleet with a wide margin — the fastest thing a pilot flies here is a
    /// roll rate of a few hundred degrees a second, and a spin turns in about 3.8 s.
    /// </summary>
    public const int BaselineHz = 50;

    /// <summary>
    /// THE BURST RATE, the sim's own 200 Hz, held for <see cref="BurstSeconds"/> either side of an event.
    ///
    /// WHY IT IS NOT OPTIONAL: the events are precisely the things the baseline cannot see. A water-landing peak at
    /// 4.6 g, a gear strike, a blade leaving at 25 % overspeed are sub-100-millisecond transients, and at 50 Hz you
    /// get three or four samples across the whole event with no reason to believe the peak is one of them. Ty will
    /// read a number off this file and believe it, so the file must not quietly understate a peak. It is close to
    /// free because events are rare: four seconds of burst costs about 365 kB against 18 MB for ten minutes.
    /// </summary>
    public const int BurstHz = 200;

    /// <summary>How long the burst runs either side of an event. The leading half is why the ring exists at all:
    /// the recorder cannot know an event is coming, so the samples BEFORE it are retro-flagged out of the buffer the
    /// recorder was already keeping.</summary>
    public const double BurstSeconds = 2.0;

    public const byte RateBaseline = 0;
    public const byte RateBurst = 1;

    /// <summary>Bytes of fixed preamble on every record: time, the rate it was taken at, and the event bits.</summary>
    public const int PreambleBytes = 8;

    public static int RecordBytes(int channelCount) => PreambleBytes + 4 * channelCount;

    /// <summary>
    /// DISCRETE EVENTS, one bit each, carried on every record and set at the FULL sim rate even between baseline
    /// samples — so an event that happens between two 50 Hz samples is still on the next one. Without these a
    /// twenty-minute file is something you scrub by hand looking for the moment.
    /// </summary>
    [Flags]
    public enum Event
    {
        None = 0,
        Stall = 1 << 0,
        SpinEntry = 1 << 1,
        SpinExit = 1 << 2,
        GroundContact = 1 << 3,
        WaterContact = 1 << 4,
        Damage = 1 << 5,
        ComponentLoss = 1 << 6,
        Overspeed = 1 << 7,
        OverG = 1 << 8,
        EngineStop = 1 << 9,
        Touchdown = 1 << 10,
        PilotMark = 1 << 11,
    }

    public static readonly string[] EventNames =
    {
        "stall", "spinEntry", "spinExit", "groundContact", "waterContact", "damage",
        "componentLoss", "overspeed", "overG", "engineStop", "touchdown", "pilotMark",
    };

    /// <summary>Render the event bits as names, for the CSV converter and for a human reading the file.</summary>
    public static string EventsToText(int bits)
    {
        if (bits == 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < EventNames.Length; i++)
            if ((bits & (1 << i)) != 0) { if (sb.Length > 0) sb.Append('|'); sb.Append(EventNames[i]); }
        return sb.ToString();
    }

    /// <summary>
    /// THE HEADER: what was flown. Written once, off the hot path.
    ///
    /// The knob block is the point of it. A recording whose tuning state is unknown is worth nothing, and the
    /// Developer page makes that a live risk rather than a theoretical one — so the same
    /// <c>TuningSession.DeltaReport</c> that a beta tester pastes into a bug report heads the file. One function,
    /// two callers, exactly so the two can never disagree.
    /// </summary>
    public static string BuildHeader(IEnumerable<KeyValuePair<string, string>> fields)
    {
        var sb = new StringBuilder();
        sb.Append(Magic);
        foreach (KeyValuePair<string, string> f in fields)
        {
            // A value may be multi-line (the knob report is): indent its continuations so the header stays parseable
            // as "key: value" lines and a reader never mistakes a knob for a field.
            string v = (f.Value ?? "").Replace("\r", "").Replace("\n", "\n    ");
            sb.Append(f.Key).Append(": ").Append(v).Append('\n');
        }
        sb.Append(EndHeader);
        return sb.ToString();
    }

    /// <summary>The channel table: one "name\tunit" line per float column, in record order.</summary>
    public static string BuildChannelTable(string[] names, string[] units)
    {
        if (names == null) throw new ArgumentNullException(nameof(names));
        if (units == null || units.Length != names.Length)
            throw new ArgumentException("every channel needs a unit; a column whose unit nobody recorded is a column nobody can use", nameof(units));
        var sb = new StringBuilder();
        sb.Append("channels: ").Append(names.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
        for (int i = 0; i < names.Length; i++) sb.Append(names[i]).Append('\t').Append(units[i]).Append('\n');
        sb.Append(EndChannels);
        return sb.ToString();
    }
}

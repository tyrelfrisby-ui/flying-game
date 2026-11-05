using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FlyingGame.Core.Weather;

public enum StationKind { Asos, Awos3 }

/// <summary>One airport's automated weather station (Ty 2026-11-05 08:42: "each airport should have a metar station
/// with a frequency on the map").</summary>
public sealed class WeatherStation
{
    /// <summary>The airport's identifier, used as the report's station id.</summary>
    public string Id { get; }
    public string Name { get; }
    /// <summary>The sensor site, sim metres (north, east): the airport's first runway's centre.</summary>
    public double North { get; }
    public double East { get; }
    /// <summary>The field elevation, m MSL.</summary>
    public double ElevationM { get; }
    public StationKind Kind { get; }
    /// <summary>The broadcast frequency, MHz (25 kHz channel, unique on the island).</summary>
    public double FreqMhz { get; }
    public WeatherStation(string id, string name, double north, double east, double elevationM, StationKind kind, double freqMhz)
    { Id = id; Name = name; North = north; East = east; ElevationM = elevationM; Kind = kind; FreqMhz = freqMhz; }
}

/// <summary>One METAR-style observation, already rounded the way the report states it.</summary>
public sealed class MetarReport
{
    public WeatherStation Station { get; set; } = null!;
    public DateTime TimeUtc { get; set; }
    /// <summary>Wind FROM, degrees true rounded to 10 (0 when calm), speed and gust, kt (gust 0 = none reported).</summary>
    public int WindDirDeg { get; set; }
    public int WindKt { get; set; }
    public int GustKt { get; set; }
    /// <summary>Visibility, statute miles (one of the reportable values; 10 means "10 or more").</summary>
    public double VisibilitySm { get; set; }
    /// <summary>Present weather groups, e.g. "-RA", "BR", "FG".</summary>
    public List<string> Weather { get; } = new();
    /// <summary>Sky groups, e.g. "FEW035", "BKN080", "VV002", or "CLR".</summary>
    public List<string> Sky { get; } = new();
    public int TempC { get; set; }
    public int DewpointC { get; set; }
    /// <summary>Altimeter setting, hundredths of inHg (2992 = 29.92).</summary>
    public int AltimeterHundredths { get; set; }
    /// <summary>Precipitation rate at the station, mm/h (0 none).</summary>
    public double PrecipMmH { get; set; }

    /// <summary>The raw METAR text, e.g. "METAR KMYL 051453Z AUTO 27008G18KT 10SM FEW040 12/04 A3001 RMK AO2".</summary>
    public string Raw
    {
        get
        {
            var sb = new StringBuilder("METAR ");
            sb.Append(Station.Id.ToUpperInvariant()).Append(' ');
            sb.Append(TimeUtc.ToString("ddHHmm", CultureInfo.InvariantCulture)).Append("Z AUTO ");
            sb.Append(WeatherStations.WindGroup(WindDirDeg, WindKt, GustKt)).Append(' ');
            sb.Append(WeatherStations.VisibilityGroup(VisibilitySm));
            foreach (string w in Weather) sb.Append(' ').Append(w);
            foreach (string s in Sky) sb.Append(' ').Append(s);
            sb.Append(' ').Append(WeatherStations.TempGroup(TempC)).Append('/').Append(WeatherStations.TempGroup(DewpointC));
            sb.Append(" A").Append(AltimeterHundredths.ToString("0000", CultureInfo.InvariantCulture));
            sb.Append(Station.Kind == StationKind.Asos ? " RMK AO2" : " RMK AO1");
            return sb.ToString();
        }
    }

    /// <summary>What the station says on the frequency (the AWOS/ASOS voice, as text for the display and the speech).</summary>
    public string Spoken
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Station.Name).Append(" automated weather observation, ");
            sb.Append(WeatherStations.Digits(TimeUtc.ToString("HHmm", CultureInfo.InvariantCulture))).Append(" zulu. ");
            if (WindKt == 0) sb.Append("Wind calm. ");
            else
            {
                sb.Append("Wind ").Append(WeatherStations.Digits(WindDirDeg.ToString("000", CultureInfo.InvariantCulture)))
                  .Append(" at ").Append(WeatherStations.Digits(WindKt.ToString(CultureInfo.InvariantCulture)));
                if (GustKt > 0) sb.Append(", gusts ").Append(WeatherStations.Digits(GustKt.ToString(CultureInfo.InvariantCulture)));
                sb.Append(". ");
            }
            sb.Append("Visibility ").Append(VisibilitySm >= 10 ? "one zero" : WeatherStations.SpokenFraction(VisibilitySm)).Append(". ");
            foreach (string w in Weather) sb.Append(WeatherStations.SpokenWeather(w)).Append(". ");
            sb.Append("Sky condition ");
            sb.Append(string.Join(", ", Sky.Select(WeatherStations.SpokenSky))).Append(". ");
            sb.Append("Temperature ").Append(WeatherStations.SpokenTemp(TempC)).Append(", dewpoint ").Append(WeatherStations.SpokenTemp(DewpointC)).Append(". ");
            sb.Append("Altimeter ").Append(WeatherStations.Digits(AltimeterHundredths.ToString("0000", CultureInfo.InvariantCulture))).Append('.');
            return sb.ToString();
        }
    }
}

/// <summary>
/// The island's automated weather stations and their reports (docs/weather-system.md "Airport weather stations").
/// One station per airport in the island data, each on its own 25 kHz channel. A report is SAMPLED from the island
/// weather at the station's point (<see cref="IslandWeather.SampleSurface"/>), so it says what the pilot will find
/// there, then rounded and formatted the way an ASOS/AWOS states it (FAA AC 00-45H; FMH-1).
/// </summary>
public static class WeatherStations
{
    public const double KtPerMs = 1 / 0.514444, InHgPerHpa = 0.0295299830714, MPerSm = 1609.344, FtPerM = 1 / 0.3048;
    /// <summary>The band: 118.000-135.975 MHz on 25 kHz channels.</summary>
    public const int FirstChannelKhz = 118000, LastChannelKhz = 135975, ChannelKhz = 25;
    /// <summary>Gusts are reported when the peak exceeds the mean by at least this, kt; winds below CalmKt are calm.</summary>
    public const int GustReportKt = 10, CalmKt = 3;

    /// <summary>Channels a weather broadcast must not use: 121.5 guard and its neighbours, the ground-control block,
    /// the flight-service block (122.0-122.675), the CTAF/UNICOM/MULTICOM block (122.8, the game's default COM, among
    /// them) and 123.45 air-to-air.</summary>
    public static bool Reserved(int khz) =>
        (khz >= 121400 && khz <= 121975) || (khz >= 122000 && khz <= 123100) || khz == 123450;

    private static List<WeatherStation>? _all;
    /// <summary>Every station, in the island data's airport order.</summary>
    public static IReadOnlyList<WeatherStation> All => _all ??= Build();

    private static List<WeatherStation> Build()
    {
        var mapped = new HashSet<string>(WorldTerrain.Airports.Select(a => a.Name));
        var items = IslandTerrain.World["airports"].Items;
        var ids = items.Select(a => a.S("id")).ToList();
        Dictionary<string, int> freq = AssignChannels(ids);
        var list = new List<WeatherStation>();
        foreach (IslandValue a in items)
        {
            string id = a.S("id");
            IslandPoint c = a["runways"][0]["centre"].Point;
            list.Add(new WeatherStation(id, a.S("name", id), c.X, c.Y, a.N("elevation_ft") * IslandTerrain.Feet,
                mapped.Contains(id) ? StationKind.Asos : StationKind.Awos3, freq[id] / 1000.0));
        }
        return list;
    }

    /// <summary>One channel per id: each id hashes to a start channel and takes the next free one, ids in ordinal
    /// order, so the assignment depends only on the set of ids (not on the list's order) and never repeats.</summary>
    public static Dictionary<string, int> AssignChannels(IEnumerable<string> ids)
    {
        int n = (LastChannelKhz - FirstChannelKhz) / ChannelKhz + 1;
        var used = new HashSet<int>();
        var o = new Dictionary<string, int>();
        foreach (string id in ids.Distinct().OrderBy(s => s, StringComparer.Ordinal))
        {
            int k = (int)(StableHash(id) % (uint)n);
            for (int tries = 0; tries < n; tries++, k = (k + 1) % n)
            {
                int khz = FirstChannelKhz + k * ChannelKhz;
                if (Reserved(khz) || used.Contains(khz)) continue;
                used.Add(khz); o[id] = khz; break;
            }
        }
        return o;
    }

    private static uint StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            h ^= h >> 16; h *= 0x85ebca6bu; h ^= h >> 13;
            return h;
        }
    }

    public static WeatherStation? ById(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    /// <summary>The station on a tuned frequency (within half a channel), or null.</summary>
    public static WeatherStation? ByFrequency(double mhz) => All.FirstOrDefault(s => Math.Abs(s.FreqMhz - mhz) < 0.0125);

    /// <summary>The station's report at sim time t, sampled from <paramref name="weather"/> at the station's point.</summary>
    public static MetarReport Observe(WeatherStation s, IslandWeather weather, double simTimeSec)
    {
        WeatherPoint w = weather.SampleSurface(s.North, s.East, simTimeSec);
        var r = new MetarReport { Station = s };
        DateTime utc = weather.UtcAt(simTimeSec);
        // Reports are taken at the top of a minute.
        r.TimeUtc = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0);
        int kt = (int)Math.Round(w.WindSpeedMs * KtPerMs, MidpointRounding.AwayFromZero);
        if (kt < CalmKt) { r.WindDirDeg = 0; r.WindKt = 0; }
        else
        {
            int dir = (int)(Math.Round(w.WindFromDeg / 10, MidpointRounding.AwayFromZero) * 10) % 360;
            r.WindDirDeg = dir == 0 ? 360 : dir; r.WindKt = kt;
            int g = (int)Math.Round(w.PeakGustMs * KtPerMs, MidpointRounding.AwayFromZero);
            r.GustKt = g - kt >= GustReportKt ? g : 0;
        }
        r.VisibilitySm = ReportableVisibility(w.VisibilityM / MPerSm);
        r.PrecipMmH = w.PrecipMmH;
        if (w.Precip != PrecipKind.None)
        {
            string code = w.Precip == PrecipKind.Snow ? "SN" : "RA";
            string intensity = w.PrecipMmH < 2.5 ? "-" : w.PrecipMmH < 7.6 ? "" : "+";
            r.Weather.Add(intensity + code);
        }
        // Obscurations (FMH-1 §8.5.2): fog under 5/8 SM, mist from 5/8 to 6 SM with the air near saturation.
        if (w.VisibilityM < 0.625 * MPerSm && w.FogDensity > 0) r.Weather.Add("FG");
        else if (w.VisibilityM < 7 * MPerSm && w.SpreadK <= 2.5 && w.Precip == PrecipKind.None) r.Weather.Add("BR");

        // Sky: a fog the station sits in, so deep the sky is hidden, is a vertical visibility; otherwise the layers
        // above the field, in the automated station's 12,000 ft range.
        if (w.FogDensity >= 0.5 && w.VisibilityM < 0.25 * MPerSm)
            r.Sky.Add("VV" + Math.Max(1, Hundreds(w.FogTopAglM)).ToString("000", CultureInfo.InvariantCulture));
        else
        {
            int cum = 0;
            foreach (SkyLayer l in weather.Layers(simTimeSec, s.ElevationM))
            {
                double agl = l.BaseMslM - s.ElevationM;
                if (agl < 30 || agl * FtPerM > 12000) continue;
                cum = Math.Max(cum, l.Oktas);   // a summation layer: an upper layer is at least as covered as what is below
                r.Sky.Add(CoverCode(cum) + Hundreds(agl).ToString("000", CultureInfo.InvariantCulture));
            }
            if (r.Sky.Count == 0) r.Sky.Add("CLR");
        }
        r.TempC = (int)Math.Round(w.TemperatureK - 273.15, MidpointRounding.AwayFromZero);
        r.DewpointC = (int)Math.Round(w.DewpointK - 273.15, MidpointRounding.AwayFromZero);
        // The altimeter setting is QNH, rounded DOWN to the hundredth (as an ASOS does).
        r.AltimeterHundredths = (int)Math.Floor(w.QnhPa / 100 * InHgPerHpa * 100 + 1e-9);
        return r;
    }

    // ---- the reporting rules ------------------------------------------------------------------------------------
    /// <summary>The automated station's reportable visibilities, SM (FMH-1 Table 6-1, ASOS).</summary>
    public static readonly double[] ReportableSm = { 0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5, 6, 7, 8, 9, 10 };

    /// <summary>The largest reportable value not above the visibility (an observation never claims more than it saw).</summary>
    public static double ReportableVisibility(double sm)
    {
        double o = 0;
        foreach (double v in ReportableSm) if (v <= sm + 1e-9) o = v;
        return o;
    }

    /// <summary>Cloud heights in hundreds of feet: to the nearest 100 ft up to 5,000 ft, 500 ft to 10,000, 1,000 above.</summary>
    public static int Hundreds(double aglM)
    {
        double ft = aglM * FtPerM;
        double step = ft <= 5000 ? 100 : ft <= 10000 ? 500 : 1000;
        return (int)(Math.Round(ft / step, MidpointRounding.AwayFromZero) * step / 100);
    }

    public static string CoverCode(int oktas) => oktas <= 2 ? "FEW" : oktas <= 4 ? "SCT" : oktas <= 7 ? "BKN" : "OVC";

    public static string WindGroup(int dir, int kt, int gust) =>
        kt == 0 ? "00000KT" : dir.ToString("000", CultureInfo.InvariantCulture) + kt.ToString("00", CultureInfo.InvariantCulture)
            + (gust > 0 ? "G" + gust.ToString("00", CultureInfo.InvariantCulture) : "") + "KT";

    public static string VisibilityGroup(double sm)
    {
        if (sm <= 0) return "M1/4SM";
        int whole = (int)Math.Floor(sm + 1e-9);
        double frac = sm - whole;
        string f = frac < 0.125 ? "" : frac < 0.375 ? "1/4" : frac < 0.625 ? "1/2" : "3/4";
        if (whole == 0) return f + "SM";
        return whole.ToString(CultureInfo.InvariantCulture) + (f.Length > 0 ? " " + f : "") + "SM";
    }

    public static string TempGroup(int c) => (c < 0 ? "M" : "") + Math.Abs(c).ToString("00", CultureInfo.InvariantCulture);

    // ---- the voice ----------------------------------------------------------------------------------------------
    private static readonly string[] DigitWords = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "niner" };
    public static string Digits(string s) => string.Join(" ", s.Where(char.IsDigit).Select(c => DigitWords[c - '0']));
    public static string SpokenTemp(int c) => (c < 0 ? "minus " : "") + Digits(Math.Abs(c).ToString(CultureInfo.InvariantCulture));
    public static string SpokenFraction(double sm)
    {
        string g = VisibilityGroup(sm).Replace("SM", "");
        if (g.StartsWith("M")) return "less than one quarter";
        return string.Join(" and ", g.Split(' ').Select(p => p switch
        {
            "1/4" => "one quarter", "1/2" => "one half", "3/4" => "three quarters", _ => Digits(p),
        }));
    }
    public static string SpokenWeather(string w) => w switch
    {
        "-RA" => "Light rain", "RA" => "Rain", "+RA" => "Heavy rain", "-SN" => "Light snow", "SN" => "Snow", "+SN" => "Heavy snow",
        "BR" => "Mist", "FG" => "Fog", _ => w,
    };
    public static string SpokenSky(string s)
    {
        if (s == "CLR") return "clear below one two thousand";
        string code = s.Substring(0, s.StartsWith("VV") ? 2 : 3);
        int h = int.Parse(s.Substring(code.Length), CultureInfo.InvariantCulture) * 100;
        string height = Height(h);
        return code switch
        {
            "VV" => "indefinite ceiling, vertical visibility " + height,
            "FEW" => "few clouds at " + height, "SCT" => height + " scattered", "BKN" => "ceiling " + height + " broken",
            _ => "ceiling " + height + " overcast",
        };
    }
    private static string Height(int ft)
    {
        int th = ft / 1000, hu = ft % 1000 / 100;
        var parts = new List<string>();
        if (th > 0) parts.Add(Digits(th.ToString(CultureInfo.InvariantCulture)) + " thousand");
        if (hu > 0) parts.Add(Digits(hu.ToString(CultureInfo.InvariantCulture)) + " hundred");
        return parts.Count == 0 ? "zero" : string.Join(" ", parts);
    }

    /// <summary>Test hook: rebuild the list (the island data changed).</summary>
    public static void Reset() => _all = null;
}

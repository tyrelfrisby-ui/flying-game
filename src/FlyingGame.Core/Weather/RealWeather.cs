using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FlyingGame.Core.Weather;

/// <summary>One hourly real-world observation the bias is made from. Any field may be missing (null): the island's
/// own draw is used for it.</summary>
public sealed class RealWeatherObservation
{
    /// <summary>Hours since the session date's local midnight.</summary>
    public double Hour { get; set; }
    public double? QnhHpa { get; set; }
    public double? WindFromDeg { get; set; }
    public double? WindMs { get; set; }
    public double? TempC { get; set; }
    public double? DewpointC { get; set; }
    /// <summary>Total cloud cover, 0..1.</summary>
    public double? Cover { get; set; }
}

/// <summary>What a real-weather provider is asked: a place and a date. The answer is hourly observations or nothing.</summary>
public readonly struct RealWeatherRequest
{
    public readonly double LatitudeDeg, LongitudeDeg;
    public readonly DateTime Date;
    public RealWeatherRequest(double lat, double lon, DateTime date) { LatitudeDeg = lat; LongitudeDeg = lon; Date = date.Date; }
}

/// <summary>
/// A source of real weather (docs/weather-system.md "Real-weather seeding"). Only the HOST (or the server) asks it;
/// clients get the bias inside the seed. Implementations must never log a key, a token or a full request URL.
/// </summary>
public interface IRealWeatherProvider
{
    /// <summary>Hourly observations for the request, or false (offline, no key, no data). Must not throw.</summary>
    bool TryGet(RealWeatherRequest request, out IReadOnlyList<RealWeatherObservation> hourly);
}

/// <summary>The fallback, and for now the only provider: no network, never any data. The island's own climatology
/// stands alone. (Which live source to use is Ty's call: AGENT-LOG NEEDS YOU.)</summary>
public sealed class OfflineWeatherProvider : IRealWeatherProvider
{
    public bool TryGet(RealWeatherRequest request, out IReadOnlyList<RealWeatherObservation> hourly)
    {
        hourly = Array.Empty<RealWeatherObservation>();
        return false;
    }
}

/// <summary>
/// A LOOSE pull of the island's air mass toward a real observation (Ty 08:41: "loosely based on the real weather ...
/// enough independence to keep it cohesive"). The draw moves only part of the way (<see cref="Weight"/>), never further
/// than a per-quantity clamp from the island's own draw, and the observations are rate-limited hour to hour before use,
/// so a bad report, a station thousands of miles away or a front in the feed cannot break the island's coherence: the
/// result is always an island air mass (dewpoint ≤ temperature, cover in 0..1, pressure in a sane range).
/// </summary>
public sealed class RealWeatherBias
{
    /// <summary>How far toward the observation the draw moves.</summary>
    public const double Weight = 0.6;
    /// <summary>Most the bias may move each quantity from the island's own draw.</summary>
    public const double MaxQnhHpa = 12, MaxTempK = 8, MaxWindMs = 6, MaxSpreadK = 4, MaxCover = 0.5;
    /// <summary>Most an observation may change from the hour before (applied before use), per hour.</summary>
    public const double RateQnhHpaPerH = 2, RateTempKPerH = 3, RateWindMsPerH = 3, RateCoverPerH = 0.25;
    public const double MinQnhHpa = 975, MaxQnhHpaAbs = 1045, MaxBiasWindMs = 15;

    private readonly List<RealWeatherObservation> _obs;
    public IReadOnlyList<RealWeatherObservation> Observations => _obs;

    public RealWeatherBias(IEnumerable<RealWeatherObservation> hourly)
    {
        _obs = hourly.OrderBy(o => o.Hour).Select(Copy).ToList();
        for (int i = 1; i < _obs.Count; i++) RateLimit(_obs[i - 1], _obs[i]);
        foreach (var o in _obs) if (o.TempC is { } t && o.DewpointC is { } d && d > t) o.DewpointC = t;
        var temps = _obs.Where(o => o.TempC.HasValue).Select(o => o.TempC!.Value).ToList();
        if (temps.Count > 0) MeanTempC = temps.Average();
        var spreads = _obs.Where(o => o.TempC.HasValue && o.DewpointC.HasValue).Select(o => o.TempC!.Value - o.DewpointC!.Value).ToList();
        if (spreads.Count > 0) MinSpreadK = spreads.Min();
    }

    /// <summary>The day's mean real temperature (compared with the island's daily mean, which is what a draw
    /// describes) and its smallest spread (compared with the island's dawn spread). Null when the feed had none.</summary>
    public double? MeanTempC { get; }
    public double? MinSpreadK { get; }

    private static RealWeatherObservation Copy(RealWeatherObservation o) => new()
    { Hour = o.Hour, QnhHpa = o.QnhHpa, WindFromDeg = o.WindFromDeg, WindMs = o.WindMs, TempC = o.TempC, DewpointC = o.DewpointC, Cover = o.Cover };

    private static double? Limit(double? prev, double? cur, double perH, double dh) =>
        prev is { } p && cur is { } c ? Math.Clamp(c, p - perH * dh, p + perH * dh) : cur;

    private static void RateLimit(RealWeatherObservation a, RealWeatherObservation b)
    {
        double dh = Math.Max(b.Hour - a.Hour, 0);
        b.QnhHpa = Limit(a.QnhHpa, b.QnhHpa, RateQnhHpaPerH, dh);
        b.TempC = Limit(a.TempC, b.TempC, RateTempKPerH, dh);
        b.DewpointC = Limit(a.DewpointC, b.DewpointC, RateTempKPerH, dh);
        b.WindMs = Limit(a.WindMs, b.WindMs, RateWindMsPerH, dh);
        b.Cover = Limit(a.Cover, b.Cover, RateCoverPerH, dh);
    }

    /// <summary>The observation at an hour of the session's day, interpolated (held flat past the ends); null if none.</summary>
    public RealWeatherObservation? At(double hourOfDay)
    {
        if (_obs.Count == 0) return null;
        if (hourOfDay <= _obs[0].Hour) return _obs[0];
        if (hourOfDay >= _obs[^1].Hour) return _obs[^1];
        int i = 1; while (_obs[i].Hour < hourOfDay) i++;
        RealWeatherObservation a = _obs[i - 1], b = _obs[i];
        double f = (hourOfDay - a.Hour) / Math.Max(b.Hour - a.Hour, 1e-9);
        double? L(double? x, double? y) => x is { } p && y is { } q ? p + (q - p) * f : (f < 0.5 ? x : y);
        double? dir = null;
        if (a.WindFromDeg is { } da && b.WindFromDeg is { } db) { double d = ((db - da + 540) % 360) - 180; dir = (da + d * f + 360) % 360; }
        else dir = f < 0.5 ? a.WindFromDeg : b.WindFromDeg;
        return new RealWeatherObservation { Hour = hourOfDay, QnhHpa = L(a.QnhHpa, b.QnhHpa), WindFromDeg = dir, WindMs = L(a.WindMs, b.WindMs),
            TempC = L(a.TempC, b.TempC), DewpointC = L(a.DewpointC, b.DewpointC), Cover = L(a.Cover, b.Cover) };
    }

    private static double Pull(double own, double? real, double max) =>
        real is { } r ? own + Math.Clamp(Weight * (r - own), -max, max) : own;

    /// <summary>The island's draw pulled toward the observation at <paramref name="absoluteHour"/> (local hours since
    /// 2000-01-01; only the hour of day is used). Temperature and spread are compared by their daily mean and minimum,
    /// so the island's own diurnal curve is not pulled twice.</summary>
    public IslandWeather.AirMass Apply(IslandWeather.AirMass own, double absoluteHour)
    {
        double hod = absoluteHour % 24; if (hod < 0) hod += 24;
        RealWeatherObservation? o = At(hod);
        if (o == null) return own;
        double qnh = Math.Clamp(Pull(own.QnhHpa, o.QnhHpa, MaxQnhHpa), MinQnhHpa, MaxQnhHpaAbs);
        double tMean = Pull(own.MeanSeaLevelTempK, MeanTempC + 273.15, MaxTempK);
        double spread = Math.Max(0, Pull(own.DawnSpreadK, MinSpreadK, MaxSpreadK));
        double ws = Math.Clamp(Pull(own.WindMs, o.WindMs, MaxWindMs), 0, MaxBiasWindMs);
        double from = own.WindFromDeg;
        if (o.WindFromDeg is { } rd)
        {
            double d = ((rd - from + 540) % 360) - 180;
            from = (from + Weight * d + 360) % 360;
        }
        double cover = Math.Clamp(Pull(own.Cover, o.Cover, MaxCover), 0, 1);
        return new IslandWeather.AirMass(qnh, from, ws, own.GustFactor, tMean, spread, cover, own.UpperCover, own.UpperBaseMslM, own.RainDraw);
    }

    // ---- wire form: hour,qnh,dir,ms,t,td,cover per observation, '|' between; empty field = missing ----------------
    public string Encode() => string.Join("|", _obs.Select(o => string.Join(",",
        F(o.Hour), F(o.QnhHpa), F(o.WindFromDeg), F(o.WindMs), F(o.TempC), F(o.DewpointC), F(o.Cover))));

    private static string F(double? v) => v is { } x ? Math.Round(x, 2).ToString("0.##", CultureInfo.InvariantCulture) : "";

    public static RealWeatherBias Decode(string s)
    {
        var list = new List<RealWeatherObservation>();
        foreach (string row in s.Split('|'))
        {
            if (row.Length == 0) continue;
            string[] c = row.Split(',');
            double? P(int i) => i < c.Length && c[i].Length > 0 ? double.Parse(c[i], CultureInfo.InvariantCulture) : null;
            list.Add(new RealWeatherObservation { Hour = P(0) ?? 0, QnhHpa = P(1), WindFromDeg = P(2), WindMs = P(3), TempC = P(4), DewpointC = P(5), Cover = P(6) });
        }
        return new RealWeatherBias(list);
    }

    /// <summary>The host's step: ask the provider, and make a bias of what came back (null = offline: climatology).</summary>
    public static RealWeatherBias? From(IRealWeatherProvider provider, RealWeatherRequest request)
    {
        try { return provider.TryGet(request, out var hourly) && hourly.Count > 0 ? new RealWeatherBias(hourly) : null; }
        catch { return null; }   // a provider fault is an offline session, never a crash or a log line with its details
    }
}

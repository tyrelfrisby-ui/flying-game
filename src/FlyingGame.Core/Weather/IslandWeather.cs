using System;
using System.Collections.Generic;
using System.Globalization;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Weather;

/// <summary>
/// What one session's island weather is made from (docs/weather-system.md "The seed"). Everything a client needs to
/// rebuild the identical weather: the host sends exactly this (<see cref="Encode"/>), never a player's own real weather.
/// </summary>
public sealed class WeatherSeed
{
    public const int DefaultSeed = 1105;
    /// <summary>The draw seed.</summary>
    public int Seed { get; }
    /// <summary>The island's local calendar date of the session.</summary>
    public DateTime Date { get; }
    /// <summary>Local solar hour at sim time 0 (7.0 = 07:00, a morning, so a fog that formed overnight can be seen
    /// to burn off).</summary>
    public double StartHour { get; }
    /// <summary>The loose real-weather bias, if the host had one (null = the island's own climatology only).</summary>
    public RealWeatherBias? Bias { get; }

    public WeatherSeed(int seed, DateTime date, double startHour = 7.0, RealWeatherBias? bias = null)
    {
        Seed = seed; Date = date.Date; StartHour = startHour; Bias = bias;
    }

    /// <summary>The compact form the host broadcasts ("seed;yyyyMMdd;hour[;bias]"). Contains no key, URL or player
    /// location: only numbers the island's weather is built from.</summary>
    public string Encode() => string.Join(";", Seed.ToString(CultureInfo.InvariantCulture), Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        StartHour.ToString("0.###", CultureInfo.InvariantCulture)) + (Bias != null ? ";" + Bias.Encode() : "");

    public static WeatherSeed Decode(string s)
    {
        string[] p = s.Split(new[] { ';' }, 4);
        int seed = int.Parse(p[0], CultureInfo.InvariantCulture);
        DateTime date = DateTime.ParseExact(p[1], "yyyyMMdd", CultureInfo.InvariantCulture);
        double hour = double.Parse(p[2], CultureInfo.InvariantCulture);
        return new WeatherSeed(seed, date, hour, p.Length > 3 ? RealWeatherBias.Decode(p[3]) : null);
    }
}

/// <summary>Where the island notionally is: the sun's path, the season and the UTC offset follow from it. The default
/// (45 N, 116 W, UTC-8) is a placeholder until Ty names the place (AGENT-LOG NEEDS YOU).</summary>
public readonly struct IslandLocation
{
    public readonly double LatitudeDeg, LongitudeDeg, UtcOffsetHours;
    public IslandLocation(double lat, double lon, double utcOffsetHours) { LatitudeDeg = lat; LongitudeDeg = lon; UtcOffsetHours = utcOffsetHours; }
    public static IslandLocation Default => new(45.0, -116.0, -8.0);
}

/// <summary>One cloud layer: cover in oktas (1-8) and its base, m MSL.</summary>
public readonly struct SkyLayer
{
    public readonly int Oktas;
    public readonly double BaseMslM;
    public SkyLayer(int oktas, double baseMslM) { Oktas = oktas; BaseMslM = baseMslM; }
}

public enum PrecipKind { None, Rain, Snow }

/// <summary>Everything the island weather says about one point at one time.</summary>
public readonly struct WeatherPoint
{
    public readonly double TemperatureK, DewpointK, PressurePa, QnhPa;
    /// <summary>Mean wind, sim world frame (x north, y east, z down), m/s, and the peak gust speed, m/s (≥ the mean).</summary>
    public readonly Vec3 MeanWind;
    public readonly double PeakGustMs;
    /// <summary>Horizontal visibility here, m.</summary>
    public readonly double VisibilityM;
    /// <summary>Fog density here, 0..1 (1 ≈ 100 m visibility), and the fog's top, m above the ground.</summary>
    public readonly double FogDensity, FogTopAglM;
    public readonly PrecipKind Precip;
    /// <summary>Precipitation rate, mm/h.</summary>
    public readonly double PrecipMmH;
    public readonly double SunElevationDeg, LocalHour;
    public WeatherPoint(double tK, double tdK, double pPa, double qnhPa, Vec3 wind, double gust, double vis, double fog, double fogTop,
        PrecipKind precip, double precipMmH, double sunDeg, double hour)
    {
        TemperatureK = tK; DewpointK = tdK; PressurePa = pPa; QnhPa = qnhPa; MeanWind = wind; PeakGustMs = gust; VisibilityM = vis;
        FogDensity = fog; FogTopAglM = fogTop; Precip = precip; PrecipMmH = precipMmH; SunElevationDeg = sunDeg; LocalHour = hour;
    }
    public double SpreadK => TemperatureK - DewpointK;
    public double WindSpeedMs => Math.Sqrt(MeanWind.X * MeanWind.X + MeanWind.Y * MeanWind.Y);
    /// <summary>Direction the wind blows FROM, degrees true, 0..360 (0 when calm).</summary>
    public double WindFromDeg
    {
        get
        {
            if (WindSpeedMs < 1e-6) return 0;
            double d = Math.Atan2(-MeanWind.Y, -MeanWind.X) * 180 / Math.PI;
            return d <= 0 ? d + 360 : d;
        }
    }
}

/// <summary>
/// THE ISLAND WEATHER (Ty 2026-11-05 08:41, docs/weather-system.md): one coherent, island-wide state built
/// deterministically from a <see cref="WeatherSeed"/>. Pressure, wind, temperature and dewpoint, cloud layers,
/// visibility, precipitation and fog are pure functions of (seed, position, sim time) and the island's terrain, so every
/// device in a session computes the same weather without exchanging anything but the seed.
///
/// Air mass: six-hourly draws (QNH, 10 m wind, temperature anomaly, the dawn dewpoint spread, cloud cover, rain) blended
/// on a smoothstep, so the weather evolves through a session but never jumps. A real-weather bias, when the host has
/// one, pulls those draws loosely toward an observation (<see cref="RealWeatherBias"/>) and is clamped so the result is
/// still a self-consistent island air mass. Temperature follows a diurnal curve (minimum at sunrise, maximum at 15:00,
/// amplitude cut by cloud); the dewpoint holds, so the spread is smallest at dawn. On the ground the terrain acts:
/// clear, calm nights pool cold air in valleys (<see cref="FogModel"/>), which is what puts fog there.
///
/// Wind: with an <see cref="AtmosphereField"/> attached (the regional air the aeroplane flies), the wind IS that field's
/// wind, so a station reports what the aircraft feels; otherwise the state's own synoptic wind on a power-law profile.
/// </summary>
public sealed class IslandWeather
{
    public WeatherSeed Seed { get; }
    public IslandLocation Location { get; }
    /// <summary>The regional air the flight model reads; when set, the wind comes from it.</summary>
    public AtmosphereField? Field { get; set; }
    /// <summary>Ground height (m MSL) at sim (north, east). Default: the active world terrain.</summary>
    public Func<double, double, double> Ground { get; set; } = WorldTerrain.GroundHeightAt;
    /// <summary>Water surface (m MSL) at sim (north, east), null over land. Default: the island's sea and lakes.</summary>
    public Func<double, double, double?> Water { get; set; } = (x, y) => IslandTerrain.WaterAt(x, y);

    /// <summary>A session override of the 10 m wind (the landing page's choice): from, degrees true, and m/s.</summary>
    public (double fromDeg, double speedMs)? WindOverride { get; set; }
    /// <summary>A session override of the day's mean ISA deviation (the landing page's hot/cold day), K.</summary>
    public double? IsaDeviationOverrideK { get; set; }

    /// <summary>The air mass draws are this many hours apart.</summary>
    public const double KeyHours = 6;
    /// <summary>Lapse rates of the ENVIRONMENT's temperature and dewpoint, K/m. The dewpoint falls a little faster than
    /// the temperature, so the spread grows 1 K/km and the highlands are slightly drier. (A rising parcel's dewpoint falls
    /// only ~1.8 K/km, which is what <see cref="EspyMPerK"/> uses for the cloud base; applying THAT to the environment
    /// would saturate every station above ~400 m.)</summary>
    public const double LapseK = 0.0065, DewLapseK = 0.0075;
    /// <summary>Espy: a rising parcel's base is this far above where it started per kelvin of spread, m/K.</summary>
    public const double EspyMPerK = 125;
    /// <summary>A lake's water is this much cooler than the day's mean air over it, K.</summary>
    public const double LakeBelowMeanAirK = 1.0;

    public IslandWeather(WeatherSeed seed, IslandLocation? location = null)
    {
        Seed = seed; Location = location ?? IslandLocation.Default;
    }

    /// <summary>The air mass at one time, before any terrain: what one six-hour key (or a blend of two) says.</summary>
    public readonly struct AirMass
    {
        public readonly double QnhHpa, WindFromDeg, WindMs, GustFactor, MeanSeaLevelTempK, DawnSpreadK, Cover, UpperCover, UpperBaseMslM, RainDraw;
        public AirMass(double qnh, double from, double ms, double gust, double tMean, double spread, double cover, double upper, double upperBase, double rain)
        {
            QnhHpa = qnh; WindFromDeg = from; WindMs = ms; GustFactor = gust; MeanSeaLevelTempK = tMean; DawnSpreadK = spread;
            Cover = cover; UpperCover = upper; UpperBaseMslM = upperBase; RainDraw = rain;
        }
    }

    // ---- time ---------------------------------------------------------------------------------------------------
    /// <summary>Local hours since 2000-01-01 00:00 at sim time t.</summary>
    public double AbsoluteHour(double simTimeSec) =>
        (Seed.Date - new DateTime(2000, 1, 1)).TotalDays * 24 + Seed.StartHour + simTimeSec / 3600.0;
    public double LocalHour(double simTimeSec) { double h = AbsoluteHour(simTimeSec) % 24; return h < 0 ? h + 24 : h; }
    public int DayOfYear(double simTimeSec) => new DateTime(2000, 1, 1).AddHours(Math.Floor(AbsoluteHour(simTimeSec))).DayOfYear;
    /// <summary>UTC date and time of sim time t (for the METAR's time group).</summary>
    public DateTime UtcAt(double simTimeSec) => new DateTime(2000, 1, 1).AddHours(AbsoluteHour(simTimeSec) - Location.UtcOffsetHours);

    /// <summary>Solar declination, rad, for a day of the year (Cooper 1969).</summary>
    public static double Declination(int doy) => 23.44 * Math.PI / 180 * Math.Sin(2 * Math.PI * (284 + doy) / 365.0);
    /// <summary>The sun's elevation, degrees, at a local solar hour.</summary>
    public double SunElevationDeg(double simTimeSec)
    {
        double lat = Location.LatitudeDeg * Math.PI / 180, dec = Declination(DayOfYear(simTimeSec));
        double ha = (LocalHour(simTimeSec) - 12) * 15 * Math.PI / 180;
        return Math.Asin(Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(ha)) * 180 / Math.PI;
    }
    /// <summary>The sun's elevation at local noon, degrees.</summary>
    public double NoonElevationDeg(double simTimeSec) =>
        90 - Math.Abs(Location.LatitudeDeg - Declination(DayOfYear(simTimeSec)) * 180 / Math.PI);
    /// <summary>Sunrise and sunset, local solar hours (polar day/night clamp to 0/24 or 12/12).</summary>
    public (double rise, double set) SunTimes(double simTimeSec)
    {
        double lat = Location.LatitudeDeg * Math.PI / 180, dec = Declination(DayOfYear(simTimeSec));
        double c = Math.Clamp(-Math.Tan(lat) * Math.Tan(dec), -1, 1);
        double half = Math.Acos(c) * 180 / Math.PI / 15;
        return (12 - half, 12 + half);
    }

    // ---- the air mass -------------------------------------------------------------------------------------------
    /// <summary>Stable [0, 1) draw keyed by the seed, a key index and a channel (FNV-1a + murmur3 fmix; no
    /// <c>System.Random</c>, whose sequence is not specified across runtimes).</summary>
    public static double Draw(int seed, long key, int channel)
    {
        unchecked
        {
            uint h = 2166136261u;
            void Mix(uint v) { for (int i = 0; i < 4; i++) { h ^= (v >> (8 * i)) & 0xFF; h *= 16777619u; } }
            Mix((uint)seed); Mix((uint)key); Mix((uint)(key >> 32)); Mix((uint)channel);
            h ^= h >> 16; h *= 0x85ebca6bu; h ^= h >> 13; h *= 0xc2b2ae35u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>The island's climatological daily mean sea-level temperature, K, for a day of the year: a mid-latitude
    /// maritime curve, warmest near day 205.</summary>
    public double ClimateMeanTempK(int doy)
    {
        double lat = Location.LatitudeDeg;
        double annual = 288.15 + 0.35 * (45 - Math.Abs(lat));              // warmer toward the equator
        double swing = Math.Clamp(0.18 * Math.Abs(lat), 1, 10);           // and a smaller season
        double sign = lat >= 0 ? 1 : -1;
        return annual + sign * swing * Math.Cos(2 * Math.PI * (doy - 205) / 365.0);
    }

    /// <summary>The sea's surface temperature, K: the same season lagging a month, with half the swing.</summary>
    public double SeaTempK(int doy)
    {
        double lat = Location.LatitudeDeg, sign = lat >= 0 ? 1 : -1;
        return 286.15 + 0.35 * (45 - Math.Abs(lat)) + sign * 0.5 * Math.Clamp(0.18 * Math.Abs(lat), 1, 10) * Math.Cos(2 * Math.PI * (doy - 235) / 365.0);
    }

    /// <summary>The raw draws of one six-hour key, before any bias.</summary>
    public AirMass KeyDraw(long key)
    {
        int s = Seed.Seed;
        double u(int ch) => Draw(s, key, ch);
        // QNH ~ 1013 ± 12 hPa, a normal-ish sum of three draws.
        double qnh = 1013.25 + 12 * ((u(0) + u(1) + u(2)) / 1.5 - 1);
        // Wind: light most of the time, now and then fresh: 0.5 + 9 u² m/s.
        double ws = 0.5 + 9 * u(3) * u(3);
        double from = 360 * u(4);
        double gust = u(5);
        int doy = new DateTime(2000, 1, 1).AddHours(key * KeyHours).DayOfYear;
        double tMean = ClimateMeanTempK(doy) + 6 * (u(6) - 0.5);
        // The spread at dawn: 1-10 K, weighted toward humid mornings.
        double spread = 1 + 9 * u(7) * u(7);
        // Cloud: more of it in humid air.
        double cover = Math.Clamp(u(8) * (1.15 - spread / 10), 0, 1);
        double upper = u(9) < 0.5 ? 0 : (u(9) - 0.5) * 2;
        double upperBase = 2600 + 2400 * u(10);
        return new AirMass(qnh, from, ws, gust, tMean, spread, cover, upper, upperBase, u(11));
    }

    private readonly Dictionary<long, AirMass> _keys = new();
    private AirMass KeyAt(long key)
    {
        if (_keys.TryGetValue(key, out AirMass m)) return m;
        m = KeyDraw(key);
        if (Seed.Bias != null) m = Seed.Bias.Apply(m, key * KeyHours);
        // Quantise: the state every device shares is a few decimals, not whatever the last ulp of a Sin said.
        m = new AirMass(Math.Round(m.QnhHpa, 1), Math.Round(m.WindFromDeg, 1), Math.Round(m.WindMs, 2), Math.Round(m.GustFactor, 3),
            Math.Round(m.MeanSeaLevelTempK, 2), Math.Round(m.DawnSpreadK, 2), Math.Round(m.Cover, 3), Math.Round(m.UpperCover, 3),
            Math.Round(m.UpperBaseMslM), Math.Round(m.RainDraw, 3));
        if (_keys.Count > 64) _keys.Clear();
        _keys[key] = m;
        return m;
    }

    /// <summary>The air mass at sim time t: two six-hour keys on a smoothstep (wind blended as a vector).</summary>
    public AirMass AirMassAt(double simTimeSec)
    {
        double h = AbsoluteHour(simTimeSec) / KeyHours;
        long k = (long)Math.Floor(h);
        double f = h - k; f = f * f * (3 - 2 * f);
        AirMass a = KeyAt(k), b = KeyAt(k + 1);
        double L(double x, double y) => x + (y - x) * f;
        double ar = a.WindFromDeg * Math.PI / 180, br = b.WindFromDeg * Math.PI / 180;
        double wx = L(a.WindMs * Math.Cos(ar), b.WindMs * Math.Cos(br)), wy = L(a.WindMs * Math.Sin(ar), b.WindMs * Math.Sin(br));
        double ws = Math.Sqrt(wx * wx + wy * wy), from = Math.Atan2(wy, wx) * 180 / Math.PI;
        if (from < 0) from += 360;
        if ((WindOverride) is { } o) { from = o.fromDeg; ws = o.speedMs; }
        double tMean = L(a.MeanSeaLevelTempK, b.MeanSeaLevelTempK);
        if (IsaDeviationOverrideK is { } dev) tMean = Atmosphere.SeaLevelTempK + dev;
        return new AirMass(L(a.QnhHpa, b.QnhHpa), from, ws, L(a.GustFactor, b.GustFactor), tMean, L(a.DawnSpreadK, b.DawnSpreadK),
            L(a.Cover, b.Cover), L(a.UpperCover, b.UpperCover), L(a.UpperBaseMslM, b.UpperBaseMslM), L(a.RainDraw, b.RainDraw));
    }

    /// <summary>Diurnal shape: −1 at sunrise, +1 at 15:00, cosine between, back to −1 at the next sunrise.</summary>
    public static double DiurnalShape(double hour, double sunrise)
    {
        const double peak = 15;
        double h = hour < sunrise ? hour + 24 : hour;
        if (h <= peak) return -Math.Cos(Math.PI * (h - sunrise) / (peak - sunrise));
        return Math.Cos(Math.PI * (h - peak) / (sunrise + 24 - peak));
    }

    /// <summary>Diurnal half-range, K: 5 K under a clear sky, 1.5 K under overcast.</summary>
    public static double DiurnalAmplitudeK(double cover) => 5.0 - 3.5 * cover;

    /// <summary>Sea-level temperature and dewpoint (K) of the air mass at t, before any terrain.</summary>
    public (double tK, double tdK) SeaLevelThermo(double simTimeSec, in AirMass m)
    {
        double a = DiurnalAmplitudeK(m.Cover);
        double t = m.MeanSeaLevelTempK + a * DiurnalShape(LocalHour(simTimeSec), SunTimes(simTimeSec).rise);
        double td = m.MeanSeaLevelTempK - a - m.DawnSpreadK;
        return (t, Math.Min(td, t));
    }

    /// <summary>The low layer's base over ground at <paramref name="groundM"/>, m MSL: Espy's 125 m per kelvin of the
    /// spread at that ground (a cumulus base follows the terrain it grows from), never under 120 m above it.</summary>
    public double LowBaseMslM(double simTimeSec, double groundM = 0)
    {
        AirMass m = AirMassAt(simTimeSec);
        var (t, td) = SeaLevelThermo(simTimeSec, m);
        double g = Math.Max(groundM, 0);
        double spread = Math.Max(0, (t - LapseK * g) - (td - DewLapseK * g));
        return g + Math.Max(120, EspyMPerK * spread);
    }

    /// <summary>The cloud layers over ground at <paramref name="groundM"/> at t, lowest first.</summary>
    public List<SkyLayer> Layers(double simTimeSec, double groundM = 0)
    {
        AirMass m = AirMassAt(simTimeSec);
        var list = new List<SkyLayer>();
        int low = (int)Math.Round(m.Cover * 8);
        if (low > 0) list.Add(new SkyLayer(low, LowBaseMslM(simTimeSec, groundM)));
        int up = (int)Math.Round(m.UpperCover * 8);
        if (up > 0) list.Add(new SkyLayer(up, Math.Max(m.UpperBaseMslM, (list.Count > 0 ? list[0].BaseMslM : 0) + 600)));
        return list;
    }

    /// <summary>Rain (or snow) rate at t, mm/h, island-wide: only under a near-overcast, humid sky.</summary>
    public double PrecipRateMmH(double simTimeSec)
    {
        AirMass m = AirMassAt(simTimeSec);
        if (m.Cover < 0.85 || m.DawnSpreadK > 3 || m.RainDraw < 0.55) return 0;
        return Math.Round(0.4 + 8 * (m.RainDraw - 0.55) / 0.45 * (m.Cover - 0.85) / 0.15, 2);
    }

    // ---- terrain caches -----------------------------------------------------------------------------------------
    private readonly Dictionary<long, double> _valley = new();
    /// <summary><see cref="FogModel.ValleyDepth"/> on a 250 m grid (exact at the grid nodes, which is where stations and
    /// fog sites are asked about: they are asked once and kept).</summary>
    public double ValleyDepthAt(double north, double east)
    {
        long ix = (long)Math.Round(north / 250), iy = (long)Math.Round(east / 250);
        long key = (ix << 32) ^ (iy & 0xFFFFFFFFL);
        if (_valley.TryGetValue(key, out double v)) return v;
        v = FogModel.ValleyDepth(Ground, ix * 250.0, iy * 250.0);
        _valley[key] = v;
        return v;
    }

    // ---- sampling -----------------------------------------------------------------------------------------------
    /// <summary>The weather at the ground (2 m for temperature and visibility, 10 m for wind) at sim (north, east).</summary>
    public WeatherPoint SampleSurface(double north, double east, double simTimeSec)
    {
        double? water = Water(north, east);
        double ground = water ?? Ground(north, east);
        return Sample(north, east, ground + 2, simTimeSec, ground, water.HasValue);
    }

    /// <summary>The weather at sim (north, east) and altitude (m MSL).</summary>
    public WeatherPoint Sample(double north, double east, double altMslM, double simTimeSec)
    {
        double? water = Water(north, east);
        return Sample(north, east, altMslM, simTimeSec, water ?? Ground(north, east), water.HasValue);
    }

    private WeatherPoint Sample(double north, double east, double alt, double t, double ground, bool overWater)
    {
        AirMass m = AirMassAt(t);
        var (t0, td0) = SeaLevelThermo(t, m);
        double agl = Math.Max(0, alt - ground);
        double tK = t0 - LapseK * alt, tdK = td0 - DewLapseK * alt;
        // Wind: the regional field when the aeroplane flies one, else the air mass's 10 m wind on a 1/7 power law.
        Vec3 wind; double gust;
        if (Field != null)
        {
            AtmosphereSample s = Field.Sample(new Vec3(north, east, -Math.Max(alt, ground + 10)), t);
            wind = new Vec3(s.MeanWind.X, s.MeanWind.Y, 0);
            double ws = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y);
            gust = ws + Math.Max(s.GustIncrementMs, 0);
        }
        else
        {
            double prof = Math.Pow(Math.Clamp(Math.Max(agl, 10) / 10, 1, 60), 1 / 7.0);
            double ws = m.WindMs * prof, r = m.WindFromDeg * Math.PI / 180;
            wind = new Vec3(-ws * Math.Cos(r), -ws * Math.Sin(r), 0);
            // A gusty day: peaks up to 1.7 × the mean, once the wind is fresh enough to be mechanically turbulent.
            gust = ws * (1 + 0.7 * m.GustFactor * FogModel.Smoothstep(4, 9, ws));
        }
        double windMs = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y);
        double sun = SunElevationDeg(t), hour = LocalHour(t);
        var (rise, set) = SunTimes(t);
        double night = FogModel.NightCooling(hour, rise, set, FogModel.BurnOffHours(NoonElevationDeg(t)));

        // Near the ground the terrain acts: clear, calm nights drain cold air into the valleys; lakes moisten the air
        // over them. Both fade with height above the surface (a radiative layer is tens of metres deep).
        double fog = 0, fogTop = 0;
        double near = 1 - FogModel.Smoothstep(20, 150, agl);
        double valley = overWater ? 0 : ValleyDepthAt(north, east);
        double pool = FogModel.ColdPoolK(valley, windMs, m.Cover, night) * near;
        tK -= pool;
        if (overWater) tdK += FogModel.LakeMoistK * near * night;
        tdK = Math.Min(tdK, tK);
        double spread = tK - tdK;
        double rad = FogModel.RadiationDensity(spread, windMs, m.Cover, night);
        double adv = 0;
        if (overWater)
        {
            int doy = DayOfYear(t);
            double waterK = ground > 1 ? m.MeanSeaLevelTempK - LapseK * ground - LakeBelowMeanAirK : SeaTempK(doy);
            adv = FogModel.AdvectionDensity(tdK, waterK, windMs);
        }
        fog = Math.Max(rad, adv);
        if (fog > 0) fogTop = rad >= adv ? FogModel.RadiationTopM(rad, valley) : FogModel.AdvectionTopM(adv);
        // Inside the fog only below its top; inside a cloud layer of BKN or more ("hill fog" on a high station).
        if (agl > fogTop) fog = 0;
        double baseMsl = LowBaseMslM(t, ground);
        if (m.Cover >= 5 / 8.0 && alt >= baseMsl && alt <= baseMsl + 400) { fog = Math.Max(fog, 0.9); fogTop = Math.Max(fogTop, baseMsl + 400 - ground); }

        double precip = PrecipRateMmH(t);
        PrecipKind kind = precip <= 0 ? PrecipKind.None : tK < 274.15 ? PrecipKind.Snow : PrecipKind.Rain;
        double vis = FogModel.VisibilityM(fog, spread, precip, kind == PrecipKind.Snow);

        double qnh = m.QnhHpa * 100;
        double p = qnh * Math.Pow(1 - LapseK * alt / Atmosphere.SeaLevelTempK, Atmosphere.GravityMs2 / (Atmosphere.GasConstantAir * LapseK));
        return new WeatherPoint(tK, tdK, p, qnh, wind, Math.Max(gust, windMs), vis, fog, fogTop, kind, precip, sun, hour);
    }

    /// <summary>Fog density and top at a fog site's centre (the Bridge's GroundFog asks this, every few seconds).</summary>
    public (double density, double topAglM) FogAt(double north, double east, double simTimeSec)
    {
        WeatherPoint w = SampleSurface(north, east, simTimeSec);
        return (w.FogDensity, w.FogTopAglM);
    }

    /// <summary>The session's weather; the Bridge sets it when a session starts (SessionSettings.ApplyWeather).</summary>
    public static IslandWeather Current { get; set; } = new(new WeatherSeed(WeatherSeed.DefaultSeed, new DateTime(2026, 11, 5)));
}

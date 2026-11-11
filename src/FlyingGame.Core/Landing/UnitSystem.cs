namespace FlyingGame.Core.Landing;

/// <summary>How numbers are SHOWN to the pilot (Ty 2026-11-05 10:56: "give page two a standard or metric option; most
/// users will be American/standard"). Display only: the sim, the configs, the store and every view-model field stay SI
/// (kg, m, m/s). <see cref="Standard"/> is US customary and is the default when nothing is saved.</summary>
public enum UnitSystem { Standard = 0, Metric = 1 }

/// <summary>
/// The one units preference and its formatting. <see cref="PrefKey"/> is the single key for the whole game: the landing
/// page writes it, and any HUD or instrument label that grows a units choice must read the same key (through
/// <see cref="Parse"/>) rather than add a second setting. Stored values: "standard" | "metric".
/// </summary>
public static class DisplayUnits
{
    public const string PrefKey = "landing.units";
    public const UnitSystem Default = UnitSystem.Standard;

    /// <summary>International avoirdupois pound (exact).</summary>
    public const double KgPerLb = 0.45359237;
    /// <summary>International foot (exact).</summary>
    public const double MPerFt = 0.3048;
    /// <summary>International nautical mile per hour, in km/h (exact).</summary>
    public const double KmhPerKt = 1.852;

    public static string ToStored(UnitSystem u) => u == UnitSystem.Metric ? "metric" : "standard";

    /// <summary>A stored value back to the system; anything absent or unknown is <see cref="Default"/>.</summary>
    public static UnitSystem Parse(string? stored) =>
        string.Equals(stored?.Trim(), "metric", StringComparison.OrdinalIgnoreCase) ? UnitSystem.Metric : Default;

    public static string Name(UnitSystem u) => u == UnitSystem.Metric ? "Metric" : "Standard";
    public static string Blurb(UnitSystem u) => u == UnitSystem.Metric ? "kg · m · km/h" : "lb · ft · kt";

    // ---- mass ----

    public static string MassUnit(UnitSystem u) => u == UnitSystem.Metric ? "kg" : "lb";
    /// <summary>A mass in the system's unit, rounded to the whole unit, no suffix.</summary>
    public static double MassValue(double kg, UnitSystem u) => u == UnitSystem.Metric ? kg : kg / KgPerLb;
    public static string MassNumber(double kg, UnitSystem u) => MassValue(kg, u).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    public static string Mass(double kg, UnitSystem u) => $"{MassNumber(kg, u)} {MassUnit(u)}";
    /// <summary>One tap of a − / + weight button: 10 lb or 5 kg, returned in kg (what the flow is set in).</summary>
    public static double MassStepKg(UnitSystem u) => u == UnitSystem.Metric ? 5.0 : 10.0 * KgPerLb;

    // ---- height, speed ----

    public static string Elevation(double m, UnitSystem u) =>
        u == UnitSystem.Metric ? $"{m.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} m"
                               : $"{(m / MPerFt).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} ft";

    /// <summary>A wind given in knots (how the presets are authored), shown in kt or km/h.</summary>
    public static string WindFromKt(double kt, UnitSystem u) =>
        u == UnitSystem.Metric ? $"{(kt * KmhPerKt).ToString("0", System.Globalization.CultureInfo.InvariantCulture)} km/h"
                               : $"{kt.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} kt";
}

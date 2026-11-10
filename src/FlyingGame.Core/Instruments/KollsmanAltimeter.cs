using System;

namespace FlyingGame.Core.Instruments
{
    /// <summary>The setting range an altimeter's knob can reach: it stops there, like the real one.</summary>
    public readonly struct KollsmanRange
    {
        public readonly double MinInHg, MaxInHg;
        public readonly string Why;
        public KollsmanRange(double min, double max, string why) { MinInHg = min; MaxInHg = max; Why = why; }
        public bool Contains(double inHg) => inHg >= MinInHg - 1e-9 && inHg <= MaxInHg + 1e-9;
        public double Clamp(double inHg) => Math.Clamp(inHg, MinInHg, MaxInHg);
    }

    /// <summary>
    /// THE SENSITIVE ALTIMETER AND ITS KOLLSMAN WINDOW (Ty 2026-11-05: "Every altimeter has a knob and a Kollsman window.
    /// Touch/click anywhere on the instrument and a popup lets you type or scroll the setting; reject absurd or
    /// out-of-limit values like a real altimeter would. Aerobatic pilots set field elevation to zero, so give enough range
    /// for that: up to ~6000 ft at least in the aerobatic aircraft.").
    ///
    /// An altimeter is an aneroid barometer: it reads the STATIC PRESSURE and shows the height, in the standard
    /// atmosphere, at which that pressure lies below a surface where the pressure is the SETTING:
    ///     indicated ft = 145 366.45 · (1 − (Ps / Pset)^0.190 263)
    /// (the NOAA/FAA pressure-altitude formula, ISA troposphere; it IS the instrument's calibration, so it is exact for
    /// the instrument even on a non-standard day). Set it to the field's QNH and it reads field elevation on the ground;
    /// set it to the field's own pressure (QFE) and it reads zero there.
    ///
    /// THE LIMITS (per type, <see cref="RangeFor"/>). A standard sensitive altimeter's window runs 28.10-31.00 inHg (the
    /// common panel instrument; that is the range most GA altimeters are built to). Zeroing a field needs a setting equal
    /// to the field's own pressure: 6,000 ft on a standard day is 23.98 inHg, below that window. So the aerobatic types get
    /// an extended window, 22.00-31.00 inHg — chosen here, not copied from an instrument's data sheet — which zeroes a
    /// field up to 8,260 ft on a standard day (7,480 ft on a day 30 hPa low). The standard window zeroes only up to
    /// 1,730 ft. A typed value outside the window is refused
    /// with the reason; the knob simply stops at the ends. Values typed in hPa (900-1050) are accepted and converted.
    /// </summary>
    public static class KollsmanAltimeter
    {
        public const double PaPerInHg = 3386.389;
        public const double StandardInHg = 29.92;
        public const double FtPerUnit = 145366.45, Exponent = 0.190263;

        public static readonly KollsmanRange Standard = new(28.10, 31.00, "standard sensitive altimeter window");
        public static readonly KollsmanRange Aerobatic = new(22.00, 31.00, "extended window: zeroes a field up to ~8,260 ft on a standard day (aerobatic type)");

        /// <summary>The knob's smallest step (the window's last digit).</summary>
        public const double StepInHg = 0.01;

        /// <summary>Aerobatic types: their pilots zero the altimeter on the field before a contest flight.</summary>
        public static bool IsAerobatic(string aircraftId)
        {
            string id = aircraftId ?? "";
            return id.StartsWith("pitts") || id.StartsWith("extra") || id.StartsWith("great-lakes") || id.StartsWith("decathlon")
                || id.StartsWith("glider-grob103-acro") || id.StartsWith("glider-swift") || id.StartsWith("stearman")
                || id.StartsWith("cassutt") || id.StartsWith("jn4");
        }

        public static KollsmanRange RangeFor(string aircraftId) => IsAerobatic(aircraftId) ? Aerobatic : Standard;

        /// <summary>What the needles show for static pressure <paramref name="staticPa"/> with the window at
        /// <paramref name="settingInHg"/>.</summary>
        public static double IndicatedFt(double staticPa, double settingInHg)
        {
            double pset = settingInHg * PaPerInHg;
            if (!(staticPa > 0) || !(pset > 0)) return 0.0;
            return FtPerUnit * (1.0 - Math.Pow(staticPa / pset, Exponent));
        }

        /// <summary>The setting that makes the altimeter read <paramref name="indicatedFt"/> at static pressure
        /// <paramref name="staticPa"/>: QNH for field elevation, QFE for zero.</summary>
        public static double SettingFor(double staticPa, double indicatedFt)
        {
            double ratio = 1.0 - indicatedFt / FtPerUnit;
            if (!(staticPa > 0) || !(ratio > 0)) return StandardInHg;
            return staticPa / Math.Pow(ratio, 1.0 / Exponent) / PaPerInHg;
        }

        /// <summary>The window shows hundredths: what the knob can actually be set to.</summary>
        public static double Round(double inHg) => Math.Round(inHg / StepInHg) * StepInHg;

        /// <summary>
        /// A value the pilot typed. Accepts "29.92", "29,92", "2992" (the window's four digits), "1013" / "1013.2" (hPa)
        /// and "29.92 inHg" / "1013 hPa". Refuses anything else, and anything outside <paramref name="range"/>, with the
        /// reason in <paramref name="why"/>.
        /// </summary>
        public static bool TryParse(string text, KollsmanRange range, out double inHg, out string why)
        {
            inHg = double.NaN; why = "";
            string s = (text ?? "").Trim().ToLowerInvariant().Replace(',', '.');
            bool hpa = false;
            if (s.EndsWith("hpa") || s.EndsWith("mb")) { hpa = true; s = s.Replace("hpa", "").Replace("mb", "").Trim(); }
            else if (s.EndsWith("inhg") || s.EndsWith("in") || s.EndsWith("\"")) s = s.Replace("inhg", "").Replace("in", "").Replace("\"", "").Trim();
            if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
                || double.IsNaN(v) || double.IsInfinity(v))
            { why = "not a number"; return false; }
            if (!hpa && v >= 2000 && v <= 3300 && !s.Contains('.')) v /= 100.0;          // "2992": the window's digits
            else if (hpa || v >= 700 && v <= 1100) { v = v * 100.0 / PaPerInHg; hpa = true; }
            v = Round(v);
            if (!range.Contains(v))
            {
                why = $"{v:F2} inHg is outside this altimeter's window, {range.MinInHg:F2}-{range.MaxInHg:F2} inHg";
                return false;
            }
            inHg = v;
            return true;
        }

        /// <summary>The window's text: "29.92".</summary>
        public static string Window(double inHg) => inHg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The same setting in hectopascals (for the popup's second line).</summary>
        public static double Hpa(double inHg) => inHg * PaPerInHg / 100.0;
    }

    /// <summary>One aeroplane's altimeter setting: the knob. Every altimeter in the cockpit reads through it (both seats'
    /// share a static line and are set together — one knob each in the real aeroplane, one value here).</summary>
    public sealed class AltimeterSetting
    {
        public KollsmanRange Range { get; private set; } = KollsmanAltimeter.Standard;
        public double InHg { get; private set; } = KollsmanAltimeter.StandardInHg;
        public int Version { get; private set; }

        public void ForType(string aircraftId) { Range = KollsmanAltimeter.RangeFor(aircraftId); InHg = Range.Clamp(InHg); Version++; }

        /// <summary>The knob turned to <paramref name="inHg"/>: it stops at the ends of the window.</summary>
        public void Turn(double inHg) { double v = KollsmanAltimeter.Round(Range.Clamp(inHg)); if (v != InHg) { InHg = v; Version++; } }

        /// <summary>Click by click (scroll wheel, the headset's stick, the knob's detents).</summary>
        public void Step(int clicks) => Turn(InHg + clicks * KollsmanAltimeter.StepInHg);

        /// <summary>A typed value: refused (and nothing changes) outside the window.</summary>
        public bool TrySet(string text, out string why)
        {
            if (!KollsmanAltimeter.TryParse(text, Range, out double v, out why)) return false;
            Turn(v);
            return true;
        }

        /// <summary>Set so the altimeter reads <paramref name="indicatedFt"/> where the static pressure is
        /// <paramref name="staticPa"/> (field elevation → QNH; 0 → QFE). False when the window cannot reach it (it then
        /// stops at the nearest end, as the knob would).</summary>
        public bool SetToRead(double staticPa, double indicatedFt)
        {
            double want = KollsmanAltimeter.SettingFor(staticPa, indicatedFt);
            Turn(want);
            return Range.Contains(KollsmanAltimeter.Round(want));
        }

        public double IndicatedFt(double staticPa) => KollsmanAltimeter.IndicatedFt(staticPa, InHg);
    }
}

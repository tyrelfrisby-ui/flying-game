using System.Globalization;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core.Landing;

/// <summary>
/// The weight-and-balance plot on page 2 (Ty 2026-11-05 addendum: "Plot the weight and balance but don't prevent someone
/// from flying even out of balance or over gross just give them a warning"; Ty 2026-11-06 t880u: "W&amp;B graph needs
/// limits in cg and weight plotted so user can see where they fall"). Read straight off <see cref="AircraftLoad"/>, which
/// already computes the loaded mass and CG and the CG limits at a weight; this adds the plot's frame, the envelope drawn
/// in it, the zero-fuel point (where the CG goes as the fuel burns), the axes' ticks in the chosen units and the float
/// placard (<see cref="LandingCatalog.FloatGrossFactor"/>).
///
/// Laid out like a handbook's chart: CG across, weight up. x runs 0 → 1 from FORWARD (left, as on a paper chart) to
/// AFT, over the envelope's widest limits and both points, padded by <see cref="Margin"/> each side; y runs 0 → 1 from
/// the empty weight to 10 % above the gross weight (or above the loaded weight, whichever is more). The envelope's
/// bottom edge sits on the plot's floor: a certificate's lowest weight is "or less", not a limit.
///
/// Only verified limits are drawn. A type with no authored envelope has <see cref="HasEnvelope"/> false: its CG axis is
/// centred on the loaded CG ±0.25 m, the points are drawn, no CG limit is (<see cref="NoEnvelopeLabel"/>), and nothing is
/// invented to fill the gap. A type with no gross draws no gross line.
///
/// <see cref="Warnings"/> are factual condition labels and nothing else (Ty t881u: "Over gross", "Aft c.g.",
/// "Forward c.g."). <see cref="CanLaunch"/> is always true: out of limits, LAUNCH asks first
/// (<see cref="LandingFlow.RequestLaunch"/>) and flies if told to.
/// </summary>
public sealed class WeightBalance
{
    public const double Margin = 0.12;

    public const string OverGrossLabel = "Over gross";
    public const string ForwardCgLabel = "Forward c.g.";
    public const string AftCgLabel = "Aft c.g.";
    public const string ForwardCgZeroFuelLabel = "Forward c.g. at zero fuel";
    public const string AftCgZeroFuelLabel = "Aft c.g. at zero fuel";
    /// <summary>The envelope's edges, for labelling the lines themselves.</summary>
    public const string ForwardLimitLineLabel = "Forward c.g. limit";
    public const string AftLimitLineLabel = "Aft c.g. limit";
    public const string MaxGrossLineLabel = "Max gross";
    public const string NoEnvelopeLabel = "No envelope authored";
    public const string TakeoffPointLabel = "Takeoff";
    public const string ZeroFuelPointLabel = "Zero fuel";

    public double EmptyKg, TotalKg, ZeroFuelKg, FuelKg;
    /// <summary>The placard gross used for the warning, kg (0 = none authored).</summary>
    public double MaxGrossKg;
    /// <summary>The CG, body x (m, +forward), loaded and with every tank empty.</summary>
    public double CgM, ZeroFuelCgM;
    /// <summary>The CG limits at the loaded weight and at the zero-fuel weight, body x (m, +forward).</summary>
    public double FwdLimitM, AftLimitM, ZeroFuelFwdLimitM, ZeroFuelAftLimitM;
    public bool HasEnvelope, HasGross;
    public bool OverGross, CgForward, CgAft, ZeroFuelCgForward, ZeroFuelCgAft;
    /// <summary>Which conditions are out, as factual labels (the constants above), in a fixed order.</summary>
    public readonly List<string> Warnings = new();
    /// <summary>The same conditions with how far out each is, in the chosen units ("Over gross by 88 lb").</summary>
    public readonly List<string> Details = new();
    public bool CanLaunch => true;
    public bool InLimits => Warnings.Count == 0;
    public bool TakeoffInside => !OverGross && !CgForward && !CgAft;
    public bool ZeroFuelInside => !ZeroFuelCgForward && !ZeroFuelCgAft;

    /// <summary>Plot coordinates (0..1 each way, see class notes).</summary>
    public double PointX, PointY, ZeroFuelX, ZeroFuelY;
    /// <summary>The envelope outline in plot coordinates, closed (first point repeated), or empty with no envelope.</summary>
    public readonly List<(double x, double y)> Envelope = new();
    /// <summary>The same outline in the aeroplane's terms: (CG body x m, weight kg), closed.</summary>
    public readonly List<(double cgM, double kg)> EnvelopeCg = new();
    /// <summary>The envelope's limit lines, each named, in plot coordinates (forward, aft, then max gross when there is one).</summary>
    public readonly List<LimitLine> Limits = new();

    public UnitSystem Units;
    /// <summary>True when the CG axis is measured from the type's own datum (as its handbook does), false when from the
    /// body reference because no datum has been located for the type.</summary>
    public bool MeasuredFromDatum;
    public double DatumM;
    public string XAxisTitle = "", YAxisTitle = "";
    public readonly List<Tick> XTicks = new(), YTicks = new();

    public sealed class LimitLine
    {
        public string Label = "";
        public readonly List<(double x, double y)> Points = new();
    }

    public readonly struct Tick
    {
        /// <summary>Position along the axis, 0..1 in plot coordinates.</summary>
        public readonly double At;
        public readonly double Value;
        public readonly string Label;
        public Tick(double at, double value, string label) { At = at; Value = value; Label = label; }
    }

    private double _xFwd, _xAft, _yLo, _yHi;

    public double PlotX(double cgM) => (_xFwd - cgM) / (_xFwd - _xAft);
    public double PlotY(double kg) => (kg - _yLo) / (_yHi - _yLo);

    /// <summary>A CG as the chart's axis reads it: inches (Standard) or millimetres (Metric) aft of the datum.</summary>
    public double ArmValue(double cgM) => (DatumM - cgM) * (Units == UnitSystem.Metric ? 1000.0 : 1.0 / 0.0254);
    public string ArmUnit => Units == UnitSystem.Metric ? "mm" : "in";
    public string CgText(double cgM) => ArmValue(cgM).ToString(Units == UnitSystem.Metric ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + ArmUnit;
    public string TakeoffText => $"{DisplayUnits.Mass(TotalKg, Units)} at {CgText(CgM)}";
    public string ZeroFuelText => $"{DisplayUnits.Mass(ZeroFuelKg, Units)} at {CgText(ZeroFuelCgM)}";

    /// <param name="grossOverrideKg">A placard to use instead of the config's own (the float rule); 0 = the config's.</param>
    /// <param name="units">How the axes, ticks and details read (every field stays kg and metres).</param>
    public static WeightBalance From(AircraftLoad load, double grossOverrideKg = 0.0, UnitSystem units = DisplayUnits.Default)
    {
        var wb = new WeightBalance
        {
            EmptyKg = load.EmptyMassKg, TotalKg = load.TotalMassKg, FuelKg = load.FuelKg, CgM = load.Cg.X,
            HasEnvelope = load.HasCgEnvelope, Units = units, MeasuredFromDatum = load.HasCgDatum, DatumM = load.HasCgDatum ? load.CgDatumM : 0.0,
        };
        wb.MaxGrossKg = grossOverrideKg > 0 ? grossOverrideKg : load.PlacardGrossKg;
        wb.HasGross = wb.MaxGrossKg > 0;

        // Zero fuel: drain every tank, read, put it back exactly.
        var saved = new List<(string id, double kg)>();
        foreach (var s in load.Slots) if (s.Kind == AircraftLoad.StationKind.Fuel) saved.Add((s.Id, s.Kg));
        foreach (var (id, _) in saved) load.Set(id, 0.0);
        wb.ZeroFuelKg = load.TotalMassKg; wb.ZeroFuelCgM = load.Cg.X;
        foreach (var (id, kg) in saved) load.Set(id, kg);

        // ---- the frame
        double xHi, xLo;   // forward-most and aft-most CG the plot must show
        IReadOnlyList<CgLimitConfig> rows = load.CgLimitRows;
        if (wb.HasEnvelope)
        {
            (wb.FwdLimitM, wb.AftLimitM) = load.CgLimitsAt(wb.TotalKg);
            (wb.ZeroFuelFwdLimitM, wb.ZeroFuelAftLimitM) = load.CgLimitsAt(wb.ZeroFuelKg);
            xHi = double.MinValue; xLo = double.MaxValue;
            foreach (CgLimitConfig r in rows) { xHi = Math.Max(xHi, r.ForwardM); xLo = Math.Min(xLo, r.AftM); }
        }
        else
        {
            xHi = wb.CgM + 0.25; xLo = wb.CgM - 0.25;
            wb.FwdLimitM = wb.ZeroFuelFwdLimitM = xHi; wb.AftLimitM = wb.ZeroFuelAftLimitM = xLo;
        }
        xHi = Math.Max(xHi, Math.Max(wb.CgM, wb.ZeroFuelCgM));
        xLo = Math.Min(xLo, Math.Min(wb.CgM, wb.ZeroFuelCgM));
        double span = Math.Max(xHi - xLo, 1e-3);
        wb._xFwd = xHi + span * Margin; wb._xAft = xLo - span * Margin;
        wb._yLo = Math.Min(wb.EmptyKg, wb.ZeroFuelKg);
        wb._yHi = Math.Max(wb.HasGross ? wb.MaxGrossKg : wb.TotalKg, wb.TotalKg) * 1.10;
        if (wb._yHi - wb._yLo < 1.0) wb._yHi = wb._yLo + 1.0;

        wb.PointX = wb.PlotX(wb.CgM); wb.PointY = wb.PlotY(wb.TotalKg);
        wb.ZeroFuelX = wb.PlotX(wb.ZeroFuelCgM); wb.ZeroFuelY = wb.PlotY(wb.ZeroFuelKg);

        // ---- the envelope: up the forward limit from the floor to the gross, across, and down the aft limit
        if (wb.HasEnvelope)
        {
            double floor = wb._yLo, top = wb.HasGross ? wb.MaxGrossKg : wb._yHi;
            var weights = new List<double> { floor };
            foreach (CgLimitConfig r in rows) if (r.MassKg > floor + 1e-9 && r.MassKg < top - 1e-9) weights.Add(r.MassKg);
            weights.Add(top);
            var fwd = new LimitLine { Label = ForwardLimitLineLabel };
            var aft = new LimitLine { Label = AftLimitLineLabel };
            foreach (double w in weights)
            {
                double f = load.CgLimitsAt(w).forwardM;
                wb.EnvelopeCg.Add((f, w));
                fwd.Points.Add((wb.PlotX(f), wb.PlotY(w)));
            }
            for (int i = weights.Count - 1; i >= 0; i--) wb.EnvelopeCg.Add((load.CgLimitsAt(weights[i]).aftM, weights[i]));
            foreach (double w in weights) aft.Points.Add((wb.PlotX(load.CgLimitsAt(w).aftM), wb.PlotY(w)));
            wb.EnvelopeCg.Add(wb.EnvelopeCg[0]);
            foreach (var (cg, kg) in wb.EnvelopeCg) wb.Envelope.Add((wb.PlotX(cg), wb.PlotY(kg)));
            wb.Limits.Add(fwd);
            wb.Limits.Add(aft);
            if (wb.HasGross)
            {
                (double f, double a) = load.CgLimitsAt(top);
                var g = new LimitLine { Label = MaxGrossLineLabel };
                g.Points.Add((wb.PlotX(f), wb.PlotY(top)));
                g.Points.Add((wb.PlotX(a), wb.PlotY(top)));
                wb.Limits.Add(g);
            }
        }
        else if (wb.HasGross)
        {
            // A gross with no CG limits: the gross is still a verified line, drawn across the whole plot.
            var g = new LimitLine { Label = MaxGrossLineLabel };
            g.Points.Add((0.0, wb.PlotY(wb.MaxGrossKg)));
            g.Points.Add((1.0, wb.PlotY(wb.MaxGrossKg)));
            wb.Limits.Add(g);
        }

        // ---- the conditions
        wb.OverGross = wb.HasGross && wb.TotalKg > wb.MaxGrossKg + 1e-9;
        wb.CgForward = wb.HasEnvelope && wb.CgM > wb.FwdLimitM + 1e-9;
        wb.CgAft = wb.HasEnvelope && wb.CgM < wb.AftLimitM - 1e-9;
        wb.ZeroFuelCgForward = wb.HasEnvelope && wb.ZeroFuelCgM > wb.ZeroFuelFwdLimitM + 1e-9;
        wb.ZeroFuelCgAft = wb.HasEnvelope && wb.ZeroFuelCgM < wb.ZeroFuelAftLimitM - 1e-9;
        string By(double m) => (m * (units == UnitSystem.Metric ? 1000.0 : 1.0 / 0.0254)).ToString(units == UnitSystem.Metric ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + wb.ArmUnit;
        if (wb.OverGross) { wb.Warnings.Add(OverGrossLabel); wb.Details.Add($"{OverGrossLabel} by {DisplayUnits.Mass(wb.TotalKg - wb.MaxGrossKg, units)}"); }
        if (wb.CgForward) { wb.Warnings.Add(ForwardCgLabel); wb.Details.Add($"{ForwardCgLabel} by {By(wb.CgM - wb.FwdLimitM)}"); }
        if (wb.CgAft) { wb.Warnings.Add(AftCgLabel); wb.Details.Add($"{AftCgLabel} by {By(wb.AftLimitM - wb.CgM)}"); }
        if (wb.ZeroFuelCgForward) { wb.Warnings.Add(ForwardCgZeroFuelLabel); wb.Details.Add($"{ForwardCgZeroFuelLabel} by {By(wb.ZeroFuelCgM - wb.ZeroFuelFwdLimitM)}"); }
        if (wb.ZeroFuelCgAft) { wb.Warnings.Add(AftCgZeroFuelLabel); wb.Details.Add($"{AftCgZeroFuelLabel} by {By(wb.ZeroFuelAftLimitM - wb.ZeroFuelCgM)}"); }

        // ---- the axes
        string from = wb.MeasuredFromDatum ? "datum" : "reference";
        wb.XAxisTitle = $"C.G. ({wb.ArmUnit} aft of {from})";
        wb.YAxisTitle = $"Weight ({DisplayUnits.MassUnit(units)})";
        double a0 = wb.ArmValue(wb._xFwd), a1 = wb.ArmValue(wb._xAft);
        AddTicks(wb.XTicks, a0, a1, 5, "0.#");
        AddTicks(wb.YTicks, DisplayUnits.MassValue(wb._yLo, units), DisplayUnits.MassValue(wb._yHi, units), 5, "#,0");
        return wb;
    }

    /// <summary>Round-number ticks (1, 2, 2.5 or 5 × 10ⁿ apart) across lo..hi, about <paramref name="target"/> of them.</summary>
    private static void AddTicks(List<Tick> into, double lo, double hi, int target, string format)
    {
        if (!(hi > lo)) return;
        double step = NiceStep((hi - lo) / target);
        for (double v = Math.Ceiling(lo / step - 1e-9) * step; v <= hi + 1e-9 * step; v += step)
        {
            double r = Math.Round(v / step) * step;   // no 0.30000000000000004
            into.Add(new Tick((r - lo) / (hi - lo), r, r.ToString(format, CultureInfo.InvariantCulture)));
        }
    }

    public static double NiceStep(double raw)
    {
        if (!(raw > 0)) return 1.0;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / mag;
        double m = n <= 1.0 ? 1.0 : n <= 2.0 ? 2.0 : n <= 2.5 ? 2.5 : n <= 5.0 ? 5.0 : 10.0;
        return m * mag;
    }
}

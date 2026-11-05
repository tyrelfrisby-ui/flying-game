namespace FlyingGame.Core.Landing;

/// <summary>
/// The weight-and-balance plot on page 2 (Ty 2026-11-05 addendum: "Plot the weight and balance but don't prevent someone
/// from flying even out of balance or over gross just give them a warning"). Read straight off
/// <see cref="AircraftLoad"/>, which already computes the loaded mass and CG; this only adds the plot's frame, the
/// zero-fuel point (where the CG goes as the fuel burns) and the float placard (<see cref="LandingCatalog.FloatGrossFactor"/>).
///
/// Plot frame: x runs 0 → 1 from the FORWARD limit to the AFT limit (so the nose is on the left, as on a paper W&amp;B
/// chart), padded by <see cref="Margin"/> each side; y runs 0 → 1 from the empty weight to 10 % above the gross weight
/// (or above the loaded weight, whichever is more). Without an authored envelope the CG axis is centred on the
/// aeroplane's empty CG ±0.25 m and <see cref="HasEnvelope"/> is false: the point is drawn, the box is not.
///
/// <see cref="CanLaunch"/> is always true. The warnings are the whole consequence.
/// </summary>
public sealed class WeightBalance
{
    public const double Margin = 0.12;

    public double EmptyKg, TotalKg, ZeroFuelKg, FuelKg;
    /// <summary>The placard gross used for the warning, kg (0 = none authored).</summary>
    public double MaxGrossKg;
    /// <summary>The CG, body x (m, +forward), loaded and with every tank empty.</summary>
    public double CgM, ZeroFuelCgM;
    public double FwdLimitM, AftLimitM;
    public bool HasEnvelope, HasGross;
    public bool OverGross, CgForward, CgAft;
    public readonly List<string> Warnings = new();
    public bool CanLaunch => true;
    public bool InLimits => Warnings.Count == 0;

    /// <summary>Plot coordinates (0..1 each way, see class notes).</summary>
    public double PointX, PointY, ZeroFuelX, ZeroFuelY;
    /// <summary>The envelope outline in plot coordinates, closed (first point repeated), or empty with no envelope.</summary>
    public readonly List<(double x, double y)> Envelope = new();

    private double _xFwd, _xAft, _yLo, _yHi;

    public double PlotX(double cgM) => (_xFwd - cgM) / (_xFwd - _xAft);
    public double PlotY(double kg) => (kg - _yLo) / (_yHi - _yLo);

    /// <param name="grossOverrideKg">A placard to use instead of the config's own (the float rule); 0 = the config's.</param>
    public static WeightBalance From(AircraftLoad load, double grossOverrideKg = 0.0)
    {
        var wb = new WeightBalance
        {
            EmptyKg = load.EmptyMassKg, TotalKg = load.TotalMassKg, FuelKg = load.FuelKg, CgM = load.Cg.X,
            HasEnvelope = load.HasCgEnvelope,
        };
        wb.MaxGrossKg = grossOverrideKg > 0 ? grossOverrideKg : load.PlacardGrossKg;
        wb.HasGross = wb.MaxGrossKg > 0;

        // Zero fuel: drain every tank, read, put it back exactly.
        var saved = new List<(string id, double kg)>();
        foreach (var s in load.Slots) if (s.Kind == AircraftLoad.StationKind.Fuel) saved.Add((s.Id, s.Kg));
        foreach (var (id, _) in saved) load.Set(id, 0.0);
        wb.ZeroFuelKg = load.TotalMassKg; wb.ZeroFuelCgM = load.Cg.X;
        foreach (var (id, kg) in saved) load.Set(id, kg);

        if (wb.HasEnvelope)
        {
            wb.FwdLimitM = load.CgForwardLimitM;
            wb.AftLimitM = load.CgAftLimitM;
        }
        else { wb.FwdLimitM = wb.CgM + 0.25; wb.AftLimitM = wb.CgM - 0.25; }

        double span = wb.FwdLimitM - wb.AftLimitM;
        wb._xFwd = wb.FwdLimitM + span * Margin; wb._xAft = wb.AftLimitM - span * Margin;
        wb._yLo = Math.Min(wb.EmptyKg, wb.ZeroFuelKg);
        wb._yHi = Math.Max(wb.HasGross ? wb.MaxGrossKg : wb.TotalKg, wb.TotalKg) * 1.10;
        if (wb._yHi - wb._yLo < 1.0) wb._yHi = wb._yLo + 1.0;

        wb.PointX = wb.PlotX(wb.CgM); wb.PointY = wb.PlotY(wb.TotalKg);
        wb.ZeroFuelX = wb.PlotX(wb.ZeroFuelCgM); wb.ZeroFuelY = wb.PlotY(wb.ZeroFuelKg);
        if (wb.HasEnvelope)
        {
            double top = wb.HasGross ? wb.MaxGrossKg : wb._yHi;
            double x0 = wb.PlotX(wb.FwdLimitM), x1 = wb.PlotX(wb.AftLimitM), y0 = wb.PlotY(wb.EmptyKg), y1 = wb.PlotY(top);
            wb.Envelope.AddRange(new[] { (x0, y0), (x0, y1), (x1, y1), (x1, y0), (x0, y0) });
        }

        wb.OverGross = wb.HasGross && wb.TotalKg > wb.MaxGrossKg + 1e-9;
        wb.CgForward = wb.HasEnvelope && wb.CgM > wb.FwdLimitM + 1e-9;
        wb.CgAft = wb.HasEnvelope && wb.CgM < wb.AftLimitM - 1e-9;
        if (wb.OverGross) wb.Warnings.Add($"Over gross by {wb.TotalKg - wb.MaxGrossKg:F0} kg: it will fly like it");
        if (wb.CgForward) wb.Warnings.Add("CG forward of the limit: heavy nose, short of elevator to flare");
        if (wb.CgAft) wb.Warnings.Add("CG aft of the limit: light, twitchy pitch, hard to recover from a spin");
        return wb;
    }
}

namespace FlyingGame.Core.Aero;

/// <summary>
/// Per-strip separation state (two-branch stall hysteresis). Each strip carries its own
/// attached↔separated memory: Separation[i] ∈ [0,1] blends the ATTACHED branch (linear lift carried
/// briefly past the static stall — dynamic-stall overshoot) with the SEPARATED branch (the table's
/// post-stall values). This is what creates the real spanwise differential in a spin: the outer wing
/// rides the attached branch at high lift while the inner wing, at the same |α| history, is committed
/// to the separated branch — a lift gap far larger than any single curve's slope can produce.
/// Owned and time-advanced by the Sim layer (Aircraft); AeroModel reads Separation and reports
/// LocalAlphaRad for the update law.
/// </summary>
public sealed class StripFlowState
{
    public double[] Separation = System.Array.Empty<double>();
    public double[] LocalAlphaRad = System.Array.Empty<double>();

    // Unsteady-aero lag (proposal 3): instantaneous coefficients recorded by AeroModel,
    // first-order-lagged copies advanced by Aircraft (tau ~ 3 chords / V).
    public double[] InstCl = System.Array.Empty<double>();
    public double[] InstCd = System.Array.Empty<double>();
    public double[] InstCm = System.Array.Empty<double>();
    public double[] LagCl = System.Array.Empty<double>();
    public double[] LagCd = System.Array.Empty<double>();
    public double[] LagCm = System.Array.Empty<double>();
    public double[] ChordM = System.Array.Empty<double>();
    public bool LagPrimed;

    // Downwash transport lag (proposal 1): eps lagged by tail-arm/V (the Cm-alphadot term).
    public double DownwashEpsLagged;

    public void EnsureSize(int stripCount)
    {
        if (Separation.Length != stripCount)
        {
            Separation = new double[stripCount];
            LocalAlphaRad = new double[stripCount];
            InstCl = new double[stripCount];
            InstCd = new double[stripCount];
            InstCm = new double[stripCount];
            LagCl = new double[stripCount];
            LagCd = new double[stripCount];
            LagCm = new double[stripCount];
            ChordM = new double[stripCount];
            LagPrimed = false;
        }
    }
}

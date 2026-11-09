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
    /// <summary>Each strip's own stall angles from its airfoil table: StallPosRad > 0 (upright),
    /// StallNegRad &lt; 0 (inverted). Recorded by AeroModel so the separation law can measure its
    /// hysteresis band from whichever stall the strip is actually approaching — on a cambered section
    /// those are not mirror images.</summary>
    public double[] StallPosRad = System.Array.Empty<double>();
    public double[] StallNegRad = System.Array.Empty<double>();

    /// <summary>
    /// IS THIS STRIP'S LEADING EDGE STALLED? Not a fraction and not a blend: leading-edge stall is abrupt, and it is
    /// HYSTERETIC — the flow reattaches at a LOWER angle than it separated at, so over a band of alpha the state
    /// depends on which way the aeroplane arrived. That band is the whole point. Ty, 2026-11-09, on his own Pitts:
    /// let go of the stick in an upright spin and it stays fully aft, because a leading-edge-stalled stabiliser has
    /// "no attached chord wise flow over the upper surface to provide a counter to the airflow from the lower
    /// surface"; then idle and full opposite rudder, "the turn rate slows and the nose drops correspondingly and then
    /// the leading edge flow reattaches on the stab and the stick lurches forward briskly due to the new attached flow
    /// over the top breaking the stall with no pilot input completely hands off".
    ///
    /// A smooth blend on local flow angle cannot produce that: it drifts the elevator forward as alpha falls. The
    /// LURCH is the hysteresis letting go.
    /// </summary>
    public bool[] LeStalled = System.Array.Empty<bool>();
    public bool LagPrimed;

    // Reversible controls (owner 2026-10-02): what a FREE hinged surface feels — the flow angle over it (before its own
    // deflection; reversed in tail-first flow) and its dynamic pressure. Recorded by AeroModel for control strips only;
    // HingeQ = 0 when the strip was skipped (no airflow / broken off).
    public double[] HingeAlphaRad = System.Array.Empty<double>();
    public double[] HingeQ = System.Array.Empty<double>();
    /// <summary>Chordwise flow direction over the hinged strip: +1 normal, 0 crossflow, −1 flow from the trailing edge (tailslide).</summary>
    public double[] HingeFlowDir = System.Array.Empty<double>();

    // Structural damage (WingStructure, Ty 2026-11-03), null while the airframe is whole. Set by Aircraft, never sized
    // by EnsureSize. DeflectionOverride: a control row's deflection in its AXIS's units, NaN = follow the stick (a slaved
    // aileron driven through its strut, a disconnected one floating, a row whose aileron has gone at 0). BendRad: extra
    // dihedral of a bent panel's row; BendOffset: how far the bend has moved the row's load point (body, m).
    public double[]? DeflectionOverride;
    public double[]? BendRad;
    public MathTypes.Vec3[]? BendOffset;
    /// <summary>Crash damage (Ty 2026-11-04), null while the airframe is whole. IncidenceDeltaRad: a bent tail boom turns its
    /// tail surfaces with it (FuselageStructure). LiftFactor / ExtraCd: a strip whose leading edge has been dented or whose
    /// fabric has been torn by branches (BranchStrike) keeps LiftFactor of its lift and adds ExtraCd of profile drag.</summary>
    public double[]? IncidenceDeltaRad;
    public double[]? LiftFactor;
    public double[]? ExtraCd;

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
            StallPosRad = new double[stripCount];
            StallNegRad = new double[stripCount];
            LeStalled = new bool[stripCount];
            HingeAlphaRad = new double[stripCount];
            HingeQ = new double[stripCount];
            HingeFlowDir = new double[stripCount];
            LagPrimed = false;
        }
    }
}

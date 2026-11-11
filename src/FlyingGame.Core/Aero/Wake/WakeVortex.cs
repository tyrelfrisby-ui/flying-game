using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero.Wake;

/// <summary>
/// The physics of one trailing vortex, as plain functions (docs/WAKE-VORTEX.md). Everything a wake does to another
/// aeroplane comes out of <see cref="SegmentVelocity"/>: the air it moves at a strip, which that strip's own lift then
/// answers to. There is no "wake roll" anywhere.
///
/// Sources and their standing:
/// - Γ₀ = L / (ρ V b₀), b₀ = π/4 · b: Kutta–Joukowski on an elliptically loaded wing whose sheet has rolled up into two
///   lines (Prandtl). Textbook, exact for the ellipse.
/// - Core: Burnham–Hallock profile v = Γ/2π · h / (h² + r_c²) (Burnham &amp; Hallock 1982, the FAA's fit to tower-flyby
///   data). Applied to a FINITE straight segment as the regularised Biot–Savart law of vortex-lattice codes, which is
///   exactly that profile for an infinite line. Initial r_c = 0.05 b: the B–H fits put the rolled-up core at a few per
///   cent of span; 0.05 is this model's choice within that.
/// - Core growth: Lamb–Oseen r_c² = r_c0² + 4 α ν_eff t (α = 1.25643) with Squire's eddy viscosity ν_eff = ν + a₁ Γ₀,
///   a₁ = 2×10⁻⁴ (Squire 1965). This is the slow, diffusive phase: total circulation is kept but what a follower a span
///   away meets weakens as the core spreads.
/// - Demise: the two-phase picture of every LIDAR campaign (Holzäpfel 2003, P2P): Γ holds, then collapses quickly once
///   ambient turbulence has linked the pair (Crow instability). The linking time is of the Crow–Bate / Sarpkaya form
///   T* = c η^(−3/4) with η = (ε b₀)^(1/3) / w₀ and t* = t / t₀, t₀ = b₀ / w₀. c = <see cref="DemiseCoefficient"/>, the
///   ceiling <see cref="DemiseMaxTStar"/> (near-calm air, where stratification rather than turbulence ends a wake) and
///   the collapse time <see cref="RapidDecayTStar"/> are this model's JUDGEMENT against the published rule that wakes
///   live about 2–8 t₀ and die within about one t₀ of linking; they are not fitted to a data set.
/// - Descent is NOT a formula here: every node moves with the velocity the OTHER line induces on it (and their ground
///   images), so w ≈ Γ/(2π b₀) appears by itself, slows as Γ decays and turns into spreading near the ground.
/// - Prop slipstream: a round turbulent jet carried in the wake, Gaussian in excess speed with the momentum-theory tube's
///   volume flux, spreading at dσ/dt = S·ΔU (S = 0.094, the round jet's spreading rate, Pope, Turbulent Flows §5.1),
///   momentum excess kept (ΔU σ² const) and angular momentum kept (swirl · σ³ const).
/// </summary>
public static class WakeVortex
{
    public const double SpanFactor = Math.PI / 4.0;
    public const double CoreRadiusPerSpan = 0.05;
    public const double SquireA1 = 2e-4;
    public const double LambOseenAlpha = 1.25643;
    public const double AirKinematicViscosity = 1.5e-5;
    /// <summary>ε in quiet air with no turbulence model running, m²/s³ (a calm, near-neutral morning boundary layer).</summary>
    public const double CalmEddyDissipation = 1e-4;
    public const double DemiseCoefficient = 0.8;
    public const double DemiseMaxTStar = 6.0;
    public const double RapidDecayTStar = 0.6;
    public const double JetSpreadRate = 0.094;
    /// <summary>A pair whose circulation has fallen below this share of its start is dropped.</summary>
    public const double DeadFraction = 0.02;
    /// <summary>A segment further than this many b₀ from a point is not evaluated there (its pair's far field, falling
    /// as 1/r², is under 2 % of w₀ beyond it).</summary>
    public const double InfluenceSpans = 8.0;

    /// <summary>Γ₀ (m²/s) of the trailing pair of a wing of span <paramref name="spanM"/> carrying <paramref name="liftN"/>
    /// at <paramref name="speedMs"/> through air of density <paramref name="rho"/>. Zero lift, zero vortex.</summary>
    public static double Circulation(double liftN, double rho, double speedMs, double spanM)
    {
        double b0 = SpanFactor * spanM;
        if (speedMs < 1e-3 || rho < 1e-6 || b0 < 1e-6) return 0.0;
        return Math.Abs(liftN) / (rho * speedMs * b0);
    }

    /// <summary>Velocity (m/s) induced at <paramref name="p"/> by a straight vortex segment from <paramref name="a"/> to
    /// <paramref name="b"/> of circulation <paramref name="gamma"/> (right-hand rule along a→b) with a Burnham–Hallock
    /// core of radius <paramref name="rc"/>.</summary>
    public static Vec3 SegmentVelocity(Vec3 p, Vec3 a, Vec3 b, double gamma, double rc)
    {
        Vec3 r0 = b - a, r1 = p - a, r2 = p - b;
        double l1 = r1.Length, l2 = r2.Length, l0sq = r0.LengthSquared;
        if (l1 < 1e-9 || l2 < 1e-9 || l0sq < 1e-12) return Vec3.Zero;
        Vec3 c = Vec3.Cross(r1, r2);
        double den = c.LengthSquared + rc * rc * l0sq;
        if (den < 1e-18) return Vec3.Zero;
        double k = gamma / (4.0 * Math.PI) * Vec3.Dot(r0, r1 / l1 - r2 / l2) / den;
        return c * k;
    }

    /// <summary>Vorticity (1/s, vector) at <paramref name="p"/> from the same segment: ω = Γ/π · r_c²/(h² + r_c²)² along
    /// the segment, weighted by the share of the line the segment subtends.</summary>
    public static Vec3 SegmentVorticity(Vec3 p, Vec3 a, Vec3 b, double gamma, double rc)
    {
        Vec3 r0 = b - a;
        double len = r0.Length;
        if (len < 1e-9 || rc < 1e-6) return Vec3.Zero;
        Vec3 t = r0 / len;
        double s = Vec3.Dot(p - a, t);
        Vec3 perp = p - a - t * s;
        double h2 = perp.LengthSquared;
        // How much of an infinite line's effect the finite piece gives: (cos θ₁ − cos θ₂)/2.
        double share = 0.5 * (s / Math.Sqrt(s * s + h2 + rc * rc) - (s - len) / Math.Sqrt((s - len) * (s - len) + h2 + rc * rc));
        double d = h2 + rc * rc;
        return t * (gamma / Math.PI * rc * rc / (d * d) * share);
    }

    /// <summary>Linking (demise) time in units of t₀ for normalised turbulence η.</summary>
    public static double DemiseTStar(double eta) =>
        eta <= 1e-6 ? DemiseMaxTStar : Math.Min(DemiseMaxTStar, DemiseCoefficient * Math.Pow(eta, -0.75));

    /// <summary>Γ/Γ₀ at normalised age t* for a pair whose demise is at <paramref name="demiseTStar"/>.</summary>
    public static double CirculationFraction(double tStar, double demiseTStar) =>
        tStar <= demiseTStar ? 1.0 : Math.Exp(-(tStar - demiseTStar) / RapidDecayTStar);

    /// <summary>Core radius at <paramref name="ageSec"/> (Lamb–Oseen growth, Squire eddy viscosity).</summary>
    public static double CoreRadius(double rc0, double gamma0, double ageSec)
    {
        double nu = AirKinematicViscosity + SquireA1 * gamma0;
        return Math.Sqrt(rc0 * rc0 + 4.0 * LambOseenAlpha * nu * Math.Max(0.0, ageSec));
    }

    /// <summary>The ambient eddy dissipation rate (m²/s³) a turbulence model of RMS <paramref name="rmsMs"/> and
    /// integral length <paramref name="lengthM"/> implies: ε ≈ σ³/L (Kolmogorov / Taylor scaling, coefficient 1).</summary>
    public static double EddyDissipation(double rmsMs, double lengthM) =>
        lengthM > 1e-3 ? Math.Max(CalmEddyDissipation, rmsMs * rmsMs * rmsMs / lengthM) : CalmEddyDissipation;

    /// <summary>Jet half-width after <paramref name="ageSec"/>: σ³ = σ₀³ + 3 S ΔU₀ σ₀² t (dσ/dt = S ΔU, ΔU σ² const).</summary>
    public static double JetRadius(double r0, double du0, double ageSec) =>
        r0 <= 0 ? 0 : Math.Cbrt(r0 * r0 * r0 + 3.0 * JetSpreadRate * Math.Abs(du0) * r0 * r0 * Math.Max(0.0, ageSec));
}

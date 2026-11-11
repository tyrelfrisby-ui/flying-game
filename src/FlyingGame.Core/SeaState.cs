namespace FlyingGame.Core;

/// <summary>
/// What the wind does to the LOOK of the water: how rough the surface is, how big the waves are, and how both of
/// those grow with the distance the wind has blown over open water.
///
/// WHY THIS EXISTS. Ty, 2026-11-11, verbatim: "water texture should change with wind. No wind glassy wind [makes]
/// texture first then waves stack up with distance etc use real physics". Until this class the water shader had a
/// single BINARY <c>_WaterCalm</c>, set from a <c>calm</c> flag in a lighting preset and connected to the wind model
/// by nothing at all: the lake looked the same in a dead calm as in 20 knots, and the only way to change it was to
/// pass a different flag to a lighting call. What Ty described is not a stylistic preference — it is the textbook
/// fetch-limited wave spectrum, three regimes deep, and all three are published with numbers.
///
/// THE THREE REGIMES, and the source for each.
///
/// 1. NO WIND — A MIRROR. There is a minimum wind below which the wind CANNOT generate a wave, and it is not a
///    modelling convenience: a gravity-capillary wave on clean water has a minimum phase speed
///    c_min = (4gσ/ρ)^¼ = 0.231 m/s at λ_min = 2π√(σ/ρg) = 1.71 cm (σ = 0.0728 N/m, clean water at 20 °C). Wind
///    slower than the slowest wave it could resonate with raises nothing, and the surface stays flat. The sea
///    criterion of Beaufort force 0 (&lt; 1 kt = 0.5 m/s) is the observational form of the same statement, in the
///    WMO's own words: "Sea like a mirror."
/// 2. A LITTLE WIND — TEXTURE FIRST, and that is what kills the mirror. Beaufort force 1 (1-3 kt, 0.5-1.5 m/s) is
///    "Ripples with the appearance of scales are formed, but without foam crests"; force 2 (4-6 kt, 1.6-3.3 m/s) is
///    "Small wavelets, still short but more pronounced". Those are centimetre-scale CAPILLARY waves — cat's paws —
///    and their wave HEIGHT is nothing (force 1's probable height is 0.1 m), but their SLOPE is large, which is the
///    only thing an optical surface cares about. <see cref="Ripple01"/> is this band, and it is why the reflection
///    goes from a mirror to a shimmer before any wave you could see in profile exists.
/// 3. MORE WIND AND MORE FETCH — GRAVITY WAVES THAT GROW DOWNWIND. <see cref="SignificantHeightM"/> and
///    <see cref="PeakWavelengthM"/> are the JONSWAP fetch-limited growth laws (Hasselmann et al. 1973, "Measurements
///    of wind-wave growth and swell decay during the Joint North Sea Wave Project", Deutsche Hydrographische
///    Zeitschrift A8(12)), capped at the fully developed Pierson-Moskowitz limit (Pierson and Moskowitz 1964,
///    J. Geophys. Res. 69(24) 5181). Height goes as √fetch and wavelength as fetch^0.66, so the waves really do
///    "stack up with distance": at a given wind the water near the upwind shore is smoother than the water a
///    kilometre downwind of it, which is what you see standing on any lake shore.
///
/// AND THE ONE THAT TIES IT TO WHAT THE EYE SEES: Cox and Munk 1954 (J. Opt. Soc. Am. 44(11) 838) photographed the
/// sun's glitter pattern on the Pacific and fitted the MEAN SQUARE SLOPE of the surface directly against the wind:
/// σ² = 0.003 + 5.12e-3·U (clean sea, U in m/s at 12.5 m, ±0.004). That is the single most useful number here,
/// because a reflection is decided by the distribution of surface NORMALS and by nothing else — not by wave height,
/// which is what a wave spectrum is usually wanted for. <see cref="RmsSlope"/> is √σ², and it is what the water
/// shader uses to build its normals, so the surface the game reflects in is as rough as the real one is measured
/// to be.
///
/// NOT MODELLED, deliberately.
///  - SHALLOW WATER. The growth laws above are deep-water laws (depth ≫ λ/2). The game's lakes are a few metres
///    deep and the peak wavelength at full fetch here is ~5 m, so the deep-water assumption is marginal on the
///    lakes and fine at sea. Shallow water would LOWER the height and SHORTEN the wave, so this errs toward rougher
///    water than the lake would really have; the error is below what the eye can judge on a 25 cm wave.
///  - SWELL. Every wave here is locally generated wind sea. There is no swell arriving from outside the world —
///    the world is 8.6 x 14.9 NM, so there is no "outside" to arrive from.
///  - WIND REFERENCE HEIGHT. JONSWAP is written for U10 and Cox-Munk measured U12.5; the log profile puts those
///    within ~2 % of one another, which is well inside Cox-Munk's own ±0.004 scatter. One wind speed is used.
///  - THE CAPILLARY CUTOFF AT HIGH WIND. Capillary ripples ride on the gravity waves rather than disappearing;
///    <see cref="Ripple01"/> saturates at 1 and stays there.
/// </summary>
public static class SeaState
{
    /// <summary>Standard gravity, m/s².</summary>
    public const double G = 9.80665;

    // ---- the capillary floor: why a dead calm is a mirror -----------------------------------------------------
    /// <summary>Surface tension of clean water at 20 °C, N/m, and its density, kg/m³.</summary>
    public const double SurfaceTensionNPerM = 0.0728, DensityKgPerM3 = 998.2;

    /// <summary>The slowest wave that can exist on clean water, m/s: c_min = (4gσ/ρ)^¼ = 0.231 m/s. A wind below
    /// this has no wave to resonate with, which is the physical reason a dead calm is a mirror.</summary>
    public static double MinimumPhaseSpeedMs => Math.Pow(4.0 * G * SurfaceTensionNPerM / DensityKgPerM3, 0.25);

    /// <summary>The wavelength that travels at <see cref="MinimumPhaseSpeedMs"/>: λ_min = 2π√(σ/ρg) = 1.71 cm.
    /// Everything shorter is held together by surface tension, everything longer by gravity.</summary>
    public static double MinimumWavelengthM => 2.0 * Math.PI * Math.Sqrt(SurfaceTensionNPerM / (DensityKgPerM3 * G));

    /// <summary>Beaufort force 0/1 boundary (1 kt) and force 2/3 boundary (7 kt), m/s: the band over which the WMO
    /// sea criteria go from "sea like a mirror" to "large wavelets, crests begin to break".</summary>
    public const double MirrorWindMs = 0.51, RippledWindMs = 3.6;

    /// <summary>How much capillary TEXTURE the surface carries, 0..1 — 0 is a mirror, 1 is fully rippled. This is
    /// the thing that changes FIRST as the wind comes up, long before any wave has a height worth drawing, and it
    /// is what Ty means by "wind [makes] texture first". Smoothstep across the Beaufort 0-to-3 band.</summary>
    public static double Ripple01(double windMs) => Smoothstep(MirrorWindMs, RippledWindMs, windMs);

    /// <summary>1 in a dead calm, 0 once the surface is fully rippled: the continuous replacement for the old binary
    /// <c>_WaterCalm</c> flag.</summary>
    public static double Glassy01(double windMs) => 1.0 - Ripple01(windMs);

    // ---- Cox and Munk 1954: the surface slope the eye actually sees -------------------------------------------
    /// <summary>Cox and Munk's clean-sea fit: σ² = 0.003 + 5.12e-3·U. The 0.003 intercept is a fit to data that
    /// never went near zero wind, not a measurement of a calm sea; a calm sea is flat. So the intercept is faded
    /// out with <see cref="Ripple01"/>, which is the same Beaufort band the WMO uses to say the same thing.</summary>
    public const double SlopeInterceptCleanSea = 0.003, SlopePerWindMs = 5.12e-3;

    /// <summary>Mean square surface slope (up- plus cross-wind), dimensionless. 0.029 at 5 m/s, 0.054 at 10 m/s,
    /// which is an rms slope of 9.7° and 13.2°.</summary>
    public static double MeanSquareSlope(double windMs)
    {
        if (!(windMs > 0)) return 0.0;
        double ripple = Ripple01(windMs);
        return ripple * (SlopeInterceptCleanSea + SlopePerWindMs * windMs);
    }

    /// <summary>Rms surface slope (radians, for small slopes): √<see cref="MeanSquareSlope"/>. This is the number
    /// the water shader builds its normals to, and the one that decides how bold a reflection is — a reflection is
    /// decided by the distribution of surface normals and by nothing else.</summary>
    public static double RmsSlope(double windMs) => Math.Sqrt(MeanSquareSlope(windMs));

    // ---- JONSWAP fetch-limited growth, Pierson-Moskowitz cap --------------------------------------------------
    /// <summary>JONSWAP's dimensionless variance law ε = 1.6e-7·x̂ becomes, through Hs = 4√E,
    /// gHs/U² = 1.6e-3·√(gX/U²). This is that coefficient.</summary>
    public const double JonswapHeightCoeff = 1.6e-3;

    /// <summary>JONSWAP's peak frequency law: f_p·U/g = 3.5·(gX/U²)^-0.33.</summary>
    public const double JonswapPeakCoeff = 3.5, JonswapPeakExponent = 0.33;

    /// <summary>The fully developed (Pierson-Moskowitz) limits the fetch laws are capped at: gHs/U² = 0.2413
    /// (from ε∞ = 3.64e-3 through Hs = 4√E) and f_p·U/g = 0.13. At 10 m/s that is Hs = 2.46 m with a 7.8 s, 96 m
    /// peak wave — the published fully developed sea for a fresh breeze.</summary>
    public const double FullyDevelopedHeight = 0.2413, FullyDevelopedPeak = 0.13;

    /// <summary>Significant wave height, m, for a wind of <paramref name="windMs"/> that has blown
    /// <paramref name="fetchM"/> metres over open water. Grows as √fetch, capped at the fully developed sea.</summary>
    public static double SignificantHeightM(double windMs, double fetchM)
    {
        if (!(windMs > 0) || !(fetchM > 0)) return 0.0;
        // Hs = 1.6e-3·U·√(X/g), which is gHs/U² = 1.6e-3·√(gX/U²) rearranged so the wind appears once.
        double hs = JonswapHeightCoeff * windMs * Math.Sqrt(fetchM / G);
        // The capillary band carries no height of its own, and below it there is no wave at all.
        return Math.Min(hs, FullyDevelopedHeightM(windMs)) * Ripple01(windMs);
    }

    /// <summary>The fully developed significant height for this wind, m — the cap above: 2.46 m at 10 m/s.</summary>
    public static double FullyDevelopedHeightM(double windMs) =>
        windMs > 0 ? FullyDevelopedHeight * windMs * windMs / G : 0.0;

    /// <summary>Peak period, s: 1/f_p from JONSWAP, floored at the fully developed peak.</summary>
    public static double PeakPeriodS(double windMs, double fetchM)
    {
        if (!(windMs > 0) || !(fetchM > 0)) return 0.0;
        double xHat = G * fetchM / (windMs * windMs);
        double fHat = Math.Max(FullyDevelopedPeak, JonswapPeakCoeff * Math.Pow(xHat, -JonswapPeakExponent));
        return windMs / (fHat * G);
    }

    /// <summary>Peak wavelength, m, from the deep-water dispersion relation λ = gT²/2π. Grows as fetch^0.66: this
    /// is the half of "waves stack up with distance" that you see as the waves getting LONGER downwind, not only
    /// taller.</summary>
    public static double PeakWavelengthM(double windMs, double fetchM)
    {
        double t = PeakPeriodS(windMs, fetchM);
        return G * t * t / (2.0 * Math.PI);
    }

    /// <summary>The fetch, m, at which this wind's sea is fully developed and stops growing: 232 km at 10 m/s.
    /// Every body of water in this world is three orders of magnitude short of it, which is exactly why the FETCH
    /// term matters here and the fully developed limit never bites.</summary>
    public static double FullyDevelopedFetchM(double windMs)
    {
        if (!(windMs > 0)) return 0.0;
        double xHat = FullyDevelopedHeight / JonswapHeightCoeff;
        return xHat * xHat * windMs * windMs / G;
    }

    /// <summary>
    /// The characteristic SLOPE of the peak wave train, rad: the maximum slope of a sinusoid of height Hs and
    /// wavelength λ is πHs/λ. This is the LONG-WAVE part of the surface's roughness, and comparing it with
    /// <see cref="RmsSlope"/> — which is the whole surface, measured optically — says how to split the slope
    /// budget between a wave train a shader can draw and the short waves it cannot:
    ///
    ///   5 m/s over 1 km of fetch: Hs = 8.1 cm at λ = 1.71 m, so πHs/λ = 0.149 against Cox and Munk's 0.169.
    ///   The long waves carry 88 % of the slope and the capillaries riding on them carry the rest.
    ///   2 kt (1 m/s): πHs/λ = 0.006 against 0.023. The long waves carry a QUARTER — the mirror is broken almost
    ///   entirely by texture, which is precisely what Ty described and what Beaufort force 1 says.
    ///
    /// Two independent measurement programmes, twenty years apart, neither interested in the other's quantity.
    /// <see cref="RemainingSlope"/> is what is left for the short waves.
    /// </summary>
    public static double PeakWaveSlope(double windMs, double fetchM)
    {
        double lambda = PeakWavelengthM(windMs, fetchM);
        return lambda > 0 ? Math.PI * SignificantHeightM(windMs, fetchM) / lambda : 0.0;
    }

    /// <summary>The rms slope left over for the capillaries and short waves once the peak train has taken its
    /// share: √(σ² − s_peak²), never negative. At low wind this is nearly the whole budget; by 10 m/s it is about
    /// half of it in rms. The water shader draws the peak train geometrically and spends this on texture.</summary>
    public static double RemainingSlope(double windMs, double fetchM)
    {
        double total = MeanSquareSlope(windMs), peak = PeakWaveSlope(windMs, fetchM);
        return Math.Sqrt(Math.Max(0.0, total - peak * peak));
    }

    // ---- fetch: the distance upwind to the shore --------------------------------------------------------------
    /// <summary>How far upwind of (<paramref name="x"/>, <paramref name="y"/>) there is still open water, m,
    /// stepping <paramref name="stepM"/> at a time until <paramref name="isWater"/> says no or
    /// <paramref name="maxFetchM"/> is reached. <paramref name="windFromRad"/> is the compass direction the wind
    /// blows FROM, in the sim frame (0 = from the north, +x); upwind is therefore that direction.
    ///
    /// A march, not a closed form, because the shoreline is whatever the world says it is: an ellipse on the
    /// lakes, a rectangle on a water lane, and an arbitrary coast at sea. The caller controls the cost — one call
    /// per frame at maxFetch/step samples, which at 3 km and 25 m is 120 cheap height-field queries.</summary>
    public static double UpwindFetchM(Func<double, double, bool> isWater, double x, double y,
                                      double windFromRad, double maxFetchM, double stepM = 25.0)
    {
        if (!(maxFetchM > 0) || !(stepM > 0)) return 0.0;
        double ux = Math.Cos(windFromRad), uy = Math.Sin(windFromRad);
        double travelled = 0.0;
        while (travelled + stepM <= maxFetchM)
        {
            double next = travelled + stepM;
            if (!isWater(x + ux * next, y + uy * next)) break;
            travelled = next;
        }
        return travelled;
    }

    /// <summary>Hermite smoothstep, as used throughout <see cref="Weather.FogModel"/>.</summary>
    public static double Smoothstep(double e0, double e1, double v)
    {
        double t = Math.Clamp((v - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

namespace FlyingGame.Core;

/// <summary>
/// What a pneumatic tyre does under load (Ty 2026-11-04, "proper tundra tires": real physics, no fudge). Every
/// tyre in <see cref="TireCatalog"/> goes through the same equations; the only thing that differs between an
/// 8.00-6 at 18 psi and a 35-inch Bushwheel at 8 is the catalog's numbers.
///
/// The equations are NACA TN 4110 / NASA TR R-64 (Smiley &amp; Horne 1958/1960, "Mechanical properties of pneumatic
/// tires with special reference to modern aircraft tires", ntrs 19930084870), with p the inflation pressure, p_r the
/// RATED pressure, w the section width, d the outside diameter, δ the vertical deflection:
///
/// - VERTICAL (eq. 24): F = (p + 0.08·p_r)·w·√(w·d)·2.4·[δ/w − c_z·(1 − e^(−0.6·(δ/w)/c_z))], c_z = 0.03 for Type III.
///   The 0.08·p_r is the carcass: a tyre at zero pressure still carries something. Its slope at the first touch is
///   0.96·(p + 0.08 p_r)·√(wd), rising to 2.4·(...) — a tyre stiffens as it flattens. Checked against Goodyear's
///   static loaded radius for the 8.00-6 (2,050 lb at 35 psi: 2.20 in by its static loaded radius, 2.05 in by this).
/// - FOOTPRINT: length 2h = 1.7·√(dδ − δ²) (eq. 5), width b = 2w·√(δ/w − (δ/w)²) up to w (eq. 8), gross area
///   A = 2.3·δ·√(wd) (eq. 12, modern tyres). The ground pressure F/A comes out at about the inflation pressure plus the
///   carcass — which is why a soft tyre floats.
/// - FORE-AND-AFT (eq. 48, Type III): K_x = 0.53·d·(p + 4·p_r)·∛(δ/d), and at small slip F_x = r·K_x·s (eq. 90).
///   That is the slip stiffness, and it sets where the longitudinal force peaks (<see cref="SlipRatioScale"/>).
/// - ROLLING RADIUS r_e = r − δ/3 (eq. 66, Whitbread).
///
/// The tyre is in SERIES with the leg (<see cref="Junction"/>), so a soft tyre takes the first of a touchdown and
/// the load builds more slowly.
/// </summary>
public static class TireModel
{
    public const double Psi = 6894.757;
    /// <summary>TR R-64 eq. 24's constants (Type III).</summary>
    public const double VerticalConstant = 2.4, CarcassPressureFraction = 0.08, Cz = 0.03;
    /// <summary>TR R-64 eq. 5: footprint length = 1.7·√(dδ − δ²).</summary>
    public const double FootprintLengthConstant = 1.7;
    /// <summary>TR R-64 eq. 12: gross footprint area = 2.3·δ·√(wd).</summary>
    public const double FootprintAreaConstant = 2.3;
    /// <summary>TR R-64 eq. 48: fore-and-aft spring K_x = 0.53·d·(p + 4 p_r)·∛(δ/d) (Type III).</summary>
    public const double ForeAftConstant = 0.53, ForeAftRatedFactor = 4.0;
    /// <summary>Where the rim flange reaches the ground through the tyre: the section height less the flange and the
    /// rubber under it (ESTIMATE: 0.85 of the section height). Past it the tyre is a wheel on the ground.</summary>
    public const double BottomingFraction = 0.85;
    /// <summary>How much stiffer the tyre gets once bottomed (rim and two folds of sidewall on the ground; ESTIMATE).</summary>
    public const double BottomedStiffnessFactor = 20.0;
    /// <summary>Damping ratio of the tyre's own hysteresis against the unsprung mass (ESTIMATE: rubber's loss at a few
    /// hertz gives 0.03–0.1). Tyres are bouncy, which is why a tundra-tyred Cub bounces.</summary>
    public const double TireDampingRatio = 0.05;

    /// <summary>(p + 0.08·p_r)·w·√(wd) for this tyre at <paramref name="psi"/>, all the leg's tyres together (N).</summary>
    private static double LoadScale(TireSpec t, double psi)
    {
        double p = (System.Math.Max(0, psi) + CarcassPressureFraction * t.MaxPsi) * Psi;
        double w = t.SectionWidthM;
        return t.Count * p * w * System.Math.Sqrt(w * t.OdM);
    }

    /// <summary>The tyre's spring force at deflection δ, bottoming included (N): TR R-64 eq. 24.</summary>
    public static double SpringForce(TireSpec t, double psi, double delta)
    {
        if (delta <= 0) return 0;
        double s = LoadScale(t, psi), w = t.SectionWidthM, u = delta / w;
        double f = s * VerticalConstant * (u - Cz * (1 - System.Math.Exp(-0.6 * u / Cz)));
        double b = BottomDeflectionM(t);
        if (delta > b) f += (BottomedStiffnessFactor - 1) * StiffnessNPerM(t, psi) * (delta - b);
        return f;
    }

    /// <summary>dF/dδ (N/m).</summary>
    public static double SpringSlope(TireSpec t, double psi, double delta)
    {
        double s = LoadScale(t, psi), w = t.SectionWidthM, u = System.Math.Max(0, delta) / w;
        double k = s * VerticalConstant / w * (1 - 0.6 * System.Math.Exp(-0.6 * u / Cz));
        if (delta > BottomDeflectionM(t)) k += (BottomedStiffnessFactor - 1) * StiffnessNPerM(t, psi);
        return k;
    }

    /// <summary>The tyre's large-deflection stiffness, 2.4·(p + 0.08 p_r)·√(wd) (N/m): the slope eq. 24 tends to.</summary>
    public static double StiffnessNPerM(TireSpec t, double psi) => LoadScale(t, psi) * VerticalConstant / t.SectionWidthM;

    /// <summary>Deflection at which the rim bottoms through the tyre.</summary>
    public static double BottomDeflectionM(TireSpec t) => BottomingFraction * t.SectionHeightM;

    /// <summary>The tyre's damping against the wheel's mass (N·s/m).</summary>
    public static double DampingNsPerM(TireSpec t, double stiffness, double rotatingMassKg)
        => 2 * TireDampingRatio * System.Math.Sqrt(System.Math.Max(0, stiffness) * System.Math.Max(0.5, rotatingMassKg));

    /// <summary>
    /// The leg and the tyre in series, joined at a massless axle. The leg is a spring k_l with a one-sided damper c_l
    /// (it damps compression only, as the gear always has); the tyre is eq. 24 with its bottoming and a small damper
    /// c_t. With no mass at the axle the two forces balance:
    ///
    ///     k_l·(x − δ) + c_l·max(0, ẋ − δ̇) = F_t(δ) + c_t·δ̇
    ///
    /// — a first-order equation for the tyre's deflection δ. It is advanced from <paramref name="delta0"/> over
    /// <paramref name="tau"/> seconds, implicitly (Newton on the backward-Euler residual, so it cannot ring), with the
    /// total compression x and its rate ẋ held; τ = 0 gives the force now. The leg can not extend past its top (δ ≤ x)
    /// and the tyre can not pull (δ ≥ 0). Returns δ; <paramref name="force"/> is the load on the ground, ≥ 0.
    /// (The unsprung mass — wheel, axle, lower leg — is not modelled: its period is a few ms, below the 5 ms step.)
    /// </summary>
    public static double Junction(TireSpec t, double psi, double delta0, double x, double xdot, double tau,
                                  double kl, double cl, double ct, out double force)
    {
        force = 0;
        if (x <= 0) return 0;
        if (!double.IsFinite(delta0) || delta0 < 0) delta0 = 0;
        if (!double.IsFinite(xdot)) xdot = 0;
        double d, rate;
        if (kl <= 0) { d = x; rate = xdot; }
        else
        {
            // Case A: the leg is compressing, so its damper is in the balance.
            d = Solve(t, psi, delta0, x, xdot, tau, kl, cl, cl + ct, out rate);
            if (!(xdot - rate > 0) && cl > 0)
                d = Solve(t, psi, delta0, x, xdot, tau, kl, 0, System.Math.Max(ct, 1e-3), out rate);   // B: leg extending, damper off
        }
        if (d > x) { d = x; rate = xdot; }
        if (d < 0) { d = 0; rate = 0; }
        force = System.Math.Max(0.0, SpringForce(t, psi, d) + ct * rate);
        return d;
    }

    // Backward Euler: C·(δ − δ0) = τ·[k_l(x − δ) + c_l·ẋ − F_t(δ)]; at τ = 0, δ = δ0 and the rate is the equation's.
    private static double Solve(TireSpec t, double psi, double d0, double x, double v, double tau, double kl, double cl, double C, out double rate)
    {
        if (tau <= 0)
        {
            rate = (kl * (x - d0) + cl * v - SpringForce(t, psi, d0)) / C;
            return d0;
        }
        double d = d0;
        for (int it = 0; it < 8; it++)
        {
            double r = C * (d - d0) - tau * (kl * (x - d) + cl * v - SpringForce(t, psi, d));
            double dr = C + tau * (kl + SpringSlope(t, psi, d));
            double step = r / dr;
            d -= step;
            if (d < 0) d = 0;
            if (System.Math.Abs(step) < 1e-9) break;
        }
        rate = (d - d0) / tau;
        return d;
    }

    /// <summary>The same two springs with no state (a direct call with no wheel kept): the static split of x, and the
    /// leg's damper on the share of the motion that is the leg's.</summary>
    public static double QuasiStatic(TireSpec t, double psi, double x, double xdot, double kl, double cl, out double delta)
    {
        delta = 0;
        if (x <= 0) return 0;
        if (kl <= 0) { delta = x; return SpringForce(t, psi, x); }
        // k_l·(x − δ) = F_t(δ), by bisection (monotone).
        double lo = 0, hi = x;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (SpringForce(t, psi, mid) > kl * (x - mid)) hi = mid; else lo = mid;
        }
        delta = 0.5 * (lo + hi);
        double kt = SpringSlope(t, psi, delta);
        double legShare = kt / (kl + kt);
        return System.Math.Max(0, kl * (x - delta) + cl * legShare * System.Math.Max(0, xdot));
    }

    /// <summary>Deflection of the tyre alone under a static load at a pressure.</summary>
    public static double StaticDeflectionM(TireSpec t, double psi, double loadN)
    {
        if (loadN <= 0) return 0;
        double lo = 0, hi = t.SectionHeightM * 2;
        for (int i = 0; i < 50; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (SpringForce(t, psi, mid) > loadN) hi = mid; else lo = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>The radius the wheel rolls on at deflection δ (TR R-64: R − δ/3).</summary>
    public static double RollingRadiusM(TireSpec t, double delta)
        => t.RadiusM - System.Math.Clamp(delta, 0, BottomDeflectionM(t)) / 3.0;

    /// <summary>Footprint length (m) at deflection δ.</summary>
    public static double FootprintLengthM(TireSpec t, double delta)
    {
        double d = t.OdM, del = System.Math.Clamp(delta, 0, BottomDeflectionM(t));
        return FootprintLengthConstant * System.Math.Sqrt(System.Math.Max(0, d * del - del * del));
    }

    /// <summary>Footprint width (m): TR R-64 eq. 8, b = 2w·√(δ/w − (δ/w)²), at most w.</summary>
    public static double FootprintWidthM(TireSpec t, double delta)
    {
        double w = t.SectionWidthM, u = System.Math.Clamp(delta / w, 0, 0.5);
        return System.Math.Min(w, 2 * w * System.Math.Sqrt(u - u * u));
    }

    /// <summary>Gross footprint area (m²), all the leg's tyres: TR R-64 eq. 12, 2.3·δ·√(wd).</summary>
    public static double FootprintAreaM2(TireSpec t, double delta)
        => t.Count * FootprintAreaConstant * System.Math.Clamp(delta, 0, BottomDeflectionM(t)) * System.Math.Sqrt(t.SectionWidthM * t.OdM);

    /// <summary>Mean ground pressure under the tyre (Pa): load over footprint. Floors the area at 1 cm² so a grazing
    /// touch is not an infinite pressure.</summary>
    public static double GroundPressurePa(TireSpec t, double loadN, double delta)
        => System.Math.Max(0, loadN) / System.Math.Max(1e-4, FootprintAreaM2(t, delta));

    // ---- longitudinal slip ----------------------------------------------------------------------------------
    /// <summary>
    /// The slip ratio κ0 at which the tyre's longitudinal force reaches tanh(1) of μN (<see cref="WheelSpin.Longitudinal"/>):
    /// the model's initial slope μN/κ0 is the tyre's slip stiffness r_e·K_x (TR R-64 eqs. 48, 90), so
    /// κ0 = μN / (r_e·K_x). Held to [<see cref="MinSlipRatioScale"/>, <see cref="MaxSlipRatioScale"/>] so a grazing
    /// touch (N and K_x both → 0) stays finite.
    ///
    /// What it says, plainly: K_x is mostly CARCASS (4·p_r against p), and it grows with the footprint (∛δ) and the
    /// diameter. So at the same load a lower pressure moves the peak only a little — and a big tyre peaks at a SMALLER
    /// slip than a small one, not a larger. A balloon tyre's late, soft peak on soft ground is the ground shearing,
    /// which this pavement-tyre relation does not contain.
    /// </summary>
    public static double SlipRatioScale(TireSpec? t, double psi, double muN, double delta)
    {
        if (t == null) return WheelSpin.SlipRatioScale;
        double kx = ForeAftStiffnessNPerM(t, psi, delta);
        double re = RollingRadiusM(t, delta);
        if (kx <= 0 || re <= 0 || muN <= 0) return MinSlipRatioScale;
        return System.Math.Clamp(muN / (re * kx), MinSlipRatioScale, MaxSlipRatioScale);
    }
    public const double MinSlipRatioScale = 0.02, MaxSlipRatioScale = 0.3;

    /// <summary>TR R-64 eq. 48 (Type III): K_x = 0.53·d·(p + 4 p_r)·∛(δ/d), N/m, all the leg's tyres.</summary>
    public static double ForeAftStiffnessNPerM(TireSpec t, double psi, double delta)
    {
        double d = t.OdM, rel = System.Math.Max(0, delta) / d;
        return t.Count * ForeAftConstant * d * (System.Math.Max(0, psi) + ForeAftRatedFactor * t.MaxPsi) * Psi * System.Math.Cbrt(rel);
    }

    // ---- rolling resistance -------------------------------------------------------------------------------------
    /// <summary>
    /// Rolling resistance of this tyre on this surface. The surface table (<see cref="WorldTerrain.RollingCoefficient"/>)
    /// is the light-aircraft literature's, which is for a light-aircraft tyre: the reference here, a 6.00-6 at 30 psi
    /// carrying 4 kN. Two parts:
    /// - the tyre's own HYSTERESIS, the whole of it on pavement (0.025 for the reference): the normal force sits ahead of
    ///   the axle by a fixed fraction of the half footprint, so the coefficient is ∝ l/r ∝ √(δ/d);
    /// - the GROUND's share, the rest of the table's number on soft ground: Bekker's compaction resistance (Wong, Theory
    ///   of Ground Vehicles) is R/W = z/((n+1)·l) with sinkage z = (p_g/k)^(1/n), so against the reference tyre the
    ///   share goes as (p_g/p_ref)^(1/n)·(l_ref/l) — the ground pressure and the footprint length. A big soft tyre
    ///   presses less and over a longer patch. n is Wong's for the nearest soil he tabulates (<see cref="SinkageExponent"/>);
    ///   the soil's own k and the k_c/b width term cancel against the reference (the width term is ~10 % and dropped).
    /// Snow on a packed zone keeps its own model (<see cref="LandingGear.SnowSinkageRolling(AircraftConfig, WorldTerrain.Surface, double)"/>).
    /// </summary>
    public static double RollingCoefficient(TireSpec? t, double psi, WorldTerrain.Surface s, double loadN, double delta)
    {
        double table = WorldTerrain.RollingCoefficient(s);
        if (t == null || loadN <= 0) return table;
        double hard = WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Paved);
        Reference(out double refRel, out double refPg, out double refL);
        double rel = System.Math.Max(1e-4, delta / t.OdM);
        double hyst = hard * System.Math.Sqrt(rel / refRel);
        double ground = System.Math.Max(0, table - hard);
        if (ground <= 0) return hyst;
        double l = System.Math.Max(0.01, FootprintLengthM(t, delta));
        double pg = GroundPressurePa(t, loadN, delta);
        return hyst + ground * System.Math.Pow(pg / refPg, 1.0 / SinkageExponent(s)) * (refL / l);
    }

    /// <summary>Bekker's sinkage exponent n (Wong, Theory of Ground Vehicles, via NASA/TM-20250006958 Table 1) for the
    /// soil each surface is nearest: snow 1.6 (US snow); sand 1.1 (dry sand); gravel 1.1 (granular, taken as dry sand —
    /// ESTIMATE); grass, dirt and rough 0.7 (sandy loam, 15 % moisture).</summary>
    public static double SinkageExponent(WorldTerrain.Surface s) => s switch
    {
        WorldTerrain.Surface.Snow or WorldTerrain.Surface.PackedGlacierSnow => 1.6,
        WorldTerrain.Surface.Sand or WorldTerrain.Surface.Gravel => 1.1,
        _ => 0.7,
    };

    public const string ReferenceTireId = "6.00-6";
    public const double ReferencePsi = 30, ReferenceLoadN = 4000;
    private static bool _refDone;
    private static double _refRel, _refPg, _refL;
    private static void Reference(out double rel, out double pg, out double l)
    {
        if (!_refDone)
        {
            TireSpec r = TireCatalog.Get(ReferenceTireId)!;
            double d = StaticDeflectionM(r, ReferencePsi, ReferenceLoadN);
            _refRel = d / r.OdM; _refPg = GroundPressurePa(r, ReferenceLoadN, d); _refL = FootprintLengthM(r, d);
            _refDone = true;
        }
        rel = _refRel; pg = _refPg; l = _refL;
    }

    /// <summary>The reference tyre's ground pressure (Pa), for the snow crust and the tests.</summary>
    public static double ReferenceGroundPressurePa { get { Reference(out _, out double p, out _); return p; } }
}

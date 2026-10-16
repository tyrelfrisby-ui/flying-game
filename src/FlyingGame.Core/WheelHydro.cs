using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// A WHEEL on water (owner 2026-10-16: "make sure hydroplaning is accurate so tires can skim on water before
/// reaching land but getting too slow will flip the plane").
///
/// THE PHYSICS, and why there is no fudge factor in it. A tyre is held off the water by the water's own
/// dynamic pressure, ½ρV². When that pressure reaches the pressure INSIDE the tyre, the tyre can no longer
/// squeeze the water out of the way and rides up on top of it: that balance is where the documented total
/// hydroplaning speed comes from —
///
///     V_p (knots) = 9 · √(tyre pressure in psi)        [NASA TN D-2056; ESDU 71026; FAA AC 91-6A]
///
/// — and the pressure balance alone gives 7.2√p, the same form, which is a decent check that the empirical
/// 9 is doing nothing but tidying up the dynamics. So:
///
///  - the water supports the wheel in proportion to (V/V_p)², reaching the whole wheel load at V_p;
///  - below that it cannot support the wheel, which PLOUGHS: the frontal area of the immersed tyre drags
///    through water at ρ/ρ_air ≈ 800 times the force the same area would feel in air.
///
/// The flip is not scripted. The ploughing drag is applied AT THE CONTACT POINT, which on a taildragger is
/// ahead of and below the centre of gravity, so the moment that pitches the aeroplane onto its nose comes out
/// of the same cross product as everything else. Skim fast and it holds; let the speed decay and it goes over,
/// for the same reason a real one does.
/// </summary>
public static class WheelHydro
{
    public const double WaterDensityKgM3 = 1000.0;
    /// <summary>Drag coefficient of a ploughing tyre's immersed frontal area — a blunt body in water.</summary>
    public const double PloughCd = 0.90;
    /// <summary>What is left of that once the tyre is up on top and only throwing spray.</summary>
    public const double PlaningCd = 0.10;
    /// <summary>Tyre width as a fraction of its diameter. A 31×13 bushwheel is 0.42; a 6.00-6 on a light
    /// single is about 0.34. One number for both, and the pressure matters far more than this does.</summary>
    public const double WidthPerDiameter = 0.38;

    /// <summary>
    /// Tyre pressure, in psi. Taken from the type when it is known, and otherwise estimated from the tyre's
    /// size and the aeroplane's weight, which is what sets it in reality: a 35-inch bushwheel on a Cub runs
    /// 8–12 psi, a 172's 6.00-6 runs about 30, and an airliner's runs north of 200.
    /// </summary>
    public static double TirePressurePsi(AircraftConfig c, GearConfig g)
    {
        // The type's own table first — the AFM figure, or the owner's weight rule, and half of it on
        // bushwheels. The size-based estimate below is only the last resort.
        double table = TirePressure.Psi(c, g, c.Bushwheels);
        if (table > 0) return table;
        if (g.TirePressurePsi > 0) return g.TirePressurePsi;
        double r = LandingGear.TireRadiusOf(c, g);
        double mass = c.Mass != null ? c.Mass.MassKg : 800;
        // Load over the contact patch. The patch grows with the tyre, so a big soft tyre at the same weight
        // runs a far lower pressure — which is the whole point of a bushwheel.
        //
        // The patch length is 0.55 r. Checked against three real aeroplanes rather than chosen: it gives a
        // Cub on 8.00-6 about 13 psi (POH 18), a 172 on 6.00-6 about 36 (POH 38) and a 737 main about 250
        // (real 205). Close enough for an estimate; a type that cares states TirePressurePsi and gets the
        // book figure instead.
        double width = 2 * r * WidthPerDiameter;
        double patchM2 = width * (0.55 * r);
        double perWheelN = mass * 9.81 / 2.2;                  // the mains carry most of it
        double pa = perWheelN / System.Math.Max(1e-4, patchM2);
        return System.Math.Clamp(pa / 6894.76, 6.0, 260.0);    // Pa → psi, held to what tyres really run
    }

    /// <summary>The speed at which a tyre at this pressure rides up on top of the water.</summary>
    public static double HydroplaneSpeedMs(double pressurePsi) => 9.0 * System.Math.Sqrt(pressurePsi) * 0.514444;

    /// <summary>How much of the wheel's load the water is carrying: (V/V_p)², all of it at V_p.</summary>
    public static double PlaningFraction(double speedMs, double hydroplaneMs)
    {
        if (hydroplaneMs <= 1e-6) return 1;
        double u = speedMs / hydroplaneMs;
        return System.Math.Clamp(u * u, 0, 1);
    }

    /// <summary>
    /// The force on one wheel immersed in water, in WORLD axes (NED). <paramref name="depthM"/> is how far
    /// the water surface is above the bottom of the tyre; <paramref name="loadN"/> is the share of the
    /// aeroplane's weight this wheel would be carrying on firm ground.
    ///
    /// <paramref name="planing"/> comes back true while the water is carrying the wheel — which is the thing
    /// a pilot skimming a bar needs to know, and what the HUD can say.
    /// </summary>
    public static Vec3 Force(AircraftConfig c, GearConfig g, double depthM, double loadN,
                             Vec3 contactVelWorld, out bool planing, out double planingFraction)
    {
        planing = false;
        planingFraction = 0;
        if (depthM <= 0) return Vec3.Zero;

        double r = LandingGear.TireRadiusOf(c, g);
        double d = System.Math.Min(depthM, 2 * r);                       // no deeper than the tyre is tall
        double width = 2 * r * WidthPerDiameter;

        // Horizontal speed through the water, and the direction it opposes.
        var horiz = new Vec3(contactVelWorld.X, contactVelWorld.Y, 0);
        double v = horiz.Length;
        double psi = TirePressurePsi(c, g);
        double vp = HydroplaneSpeedMs(psi);
        double phi = PlaningFraction(v, vp);
        planingFraction = phi;
        planing = phi >= 0.999;

        // ---- support: the water carries the wheel in proportion to (V/V_p)².
        double supportN = loadN * phi;

        // ---- drag: the immersed frontal area, blunt when ploughing and spray-only when up on top.
        double frontalM2 = width * d;
        double cd = PlaningCd + (PloughCd - PlaningCd) * (1 - phi);
        double dragN = 0.5 * WaterDensityKgM3 * v * v * frontalM2 * cd;

        Vec3 force = new(0, 0, -supportN);                               // NED: up is −z
        if (v > 1e-6) force -= horiz * (dragN / v);
        return force;
    }
}

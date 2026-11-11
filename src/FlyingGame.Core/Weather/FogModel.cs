using System;

namespace FlyingGame.Core.Weather;

/// <summary>
/// Where fog forms and when it goes (Ty 2026-11-05 08:41: "fog tends to sit in valleys when present";
/// docs/weather-system.md "Fog"). Heuristics with the physics in the right places, pure functions so the tests can hold
/// each one alone:
///
/// RADIATION FOG forms when the ground radiates to a clear sky through a calm night and the air near it cools to its
/// dewpoint. So it needs a small spread (≤ <see cref="RadiationSpreadMaxK"/>), light wind (mixing stirs the cooled
/// layer into the air above), a clear sky (cloud sends the heat back) and the night's cooling (<see cref="NightCooling"/>:
/// builds over the hours after sunset, decays over the hours after sunrise as the sun warms the ground: the burn-off).
/// Cold air is dense and drains downhill, so on such a night the valleys pool colder air (<see cref="ColdPoolK"/>,
/// scaled by how far the ground lies below its surroundings, <see cref="ValleyDepth"/>): the spread closes there first
/// and the fog sits in the valleys, over low ground and on lakes, while the ridges stay clear.
///
/// ADVECTION FOG forms when moist air moves over water colder than its dewpoint (<see cref="AdvectionDensity"/>). Wind
/// does not stop it, it feeds it, up to the point where the mixing lifts it into low stratus.
/// </summary>
public static class FogModel
{
    /// <summary>Radiation fog needs the local spread under this, K (it is dense below ~0.5 K).</summary>
    public const double RadiationSpreadMaxK = 2.5, RadiationSpreadDenseK = 0.5;
    /// <summary>Light wind lets radiation fog form; by this much (m/s, 10 m) the mixing has removed it.</summary>
    public const double CalmWindMs = 1.5, MixedOutWindMs = 4.5;
    /// <summary>A valley this deep (m below its surroundings) pools the full <see cref="ColdPoolMaxK"/>.</summary>
    public const double ValleyFullDepthM = 220, ColdPoolMaxK = 6.0, ThermalBeltMaxK = 3.0;
    /// <summary>A lake's evaporation adds this much to the dewpoint just over it on a cooling night, K.</summary>
    public const double LakeMoistK = 1.0;
    /// <summary>The surroundings a point is compared with: rings this far out, m.</summary>
    public const double RingInnerM = 1500, RingOuterM = 3000;
    /// <summary>The night's cooling builds over this many hours after sunset and burns off over this many after sunrise.</summary>
    public const double CoolingBuildH = 3.0, BurnOffH = 2.5;

    public static double Smoothstep(double e0, double e1, double x)
    {
        double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>How far the ground at (north, east) lies below the mean of its surroundings (two rings of 16), m.
    /// Positive in a valley or a basin, negative on a ridge or a summit.</summary>
    public static double ValleyDepth(Func<double, double, double> ground, double north, double east)
    {
        double sum = 0; int n = 0;
        foreach (double r in new[] { RingInnerM, RingOuterM })
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8;
                sum += ground(north + r * Math.Cos(a), east + r * Math.Sin(a)); n++;
            }
        return sum / n - ground(north, east);
    }

    /// <summary>The night's radiative cooling, 0..1: 0 in the day, building over <see cref="CoolingBuildH"/> after
    /// sunset to 1, holding to sunrise, then burning off over <paramref name="burnOffH"/> (default
    /// <see cref="BurnOffH"/>; see <see cref="BurnOffHours"/>).</summary>
    public static double NightCooling(double hour, double sunrise, double sunset, double burnOffH = BurnOffH)
    {
        if (hour >= sunrise && hour < sunset)
            return 1 - Smoothstep(0, burnOffH, hour - sunrise);
        double since = hour >= sunset ? hour - sunset : hour + 24 - sunset;
        return Smoothstep(0, CoolingBuildH, since);
    }

    /// <summary>How long the morning takes to burn the fog off, h: <see cref="BurnOffH"/> under a high sun (noon
    /// elevation 60°), longer as the sun is weaker (a November valley fog at 45 N lasts to late morning).</summary>
    public static double BurnOffHours(double noonElevationDeg) =>
        BurnOffH * Math.Sin(60 * Math.PI / 180) / Math.Max(Math.Sin(noonElevationDeg * Math.PI / 180), 0.2);

    /// <summary>The cold pool a valley holds on a clear, calm, cooling night, K below the free air at its height; NEGATIVE
    /// on a ridge or a summit, which the cold air drains off (the slopes' "thermal belt" stays warmer than the free air,
    /// up to <see cref="ThermalBeltMaxK"/>).</summary>
    public static double ColdPoolK(double valleyDepthM, double windMs, double cover, double nightCooling)
    {
        double shape = valleyDepthM >= 0 ? ColdPoolMaxK * Smoothstep(0, ValleyFullDepthM, valleyDepthM)
                                         : -ThermalBeltMaxK * Smoothstep(0, ValleyFullDepthM, -valleyDepthM);
        return shape * Calm(windMs) * Clear(cover) * nightCooling;
    }

    /// <summary>1 in calm air, 0 once mixing has taken over.</summary>
    public static double Calm(double windMs) => 1 - Smoothstep(CalmWindMs, MixedOutWindMs, windMs);
    /// <summary>1 under a clear sky, 0 under broken-to-overcast cloud (it radiates the heat back).</summary>
    public static double Clear(double cover) => 1 - Smoothstep(0.4, 0.85, cover);

    /// <summary>Radiation fog density, 0..1, from the LOCAL spread (after any cold pool), the wind, the cloud and the
    /// night's cooling.</summary>
    public static double RadiationDensity(double spreadK, double windMs, double cover, double nightCooling) =>
        (1 - Smoothstep(RadiationSpreadDenseK, RadiationSpreadMaxK, spreadK)) * Calm(windMs) * Clear(cover) * Smoothstep(0.05, 0.6, nightCooling);

    /// <summary>Advection fog density over water, 0..1: the air's dewpoint at or above the water's temperature, a
    /// breeze to bring the moist air (1-3 m/s), and not so much wind that it lifts into stratus (10-14 m/s).</summary>
    public static double AdvectionDensity(double dewpointK, double waterK, double windMs) =>
        Smoothstep(-0.5, 1.0, dewpointK - waterK) * Smoothstep(1, 3, windMs) * (1 - Smoothstep(10, 14, windMs));

    /// <summary>A radiation fog's depth, m: thin on open ground, deeper where a valley holds it.</summary>
    public static double RadiationTopM(double density, double valleyDepthM) => 4 + 10 * density + 0.15 * Math.Max(0, valleyDepthM);
    /// <summary>An advection fog's depth, m (a sea fog bank is tens to hundreds of metres).</summary>
    public static double AdvectionTopM(double density) => 30 + 120 * density;

    /// <summary>Visibility, m: fog (density 1 ≈ 100 m, Koschmieder 3.912/σ), humid haze (spread), rain or snow.</summary>
    public static double VisibilityM(double fogDensity, double spreadK, double precipMmH, bool snow)
    {
        double clear = 15000 + 45000 * Smoothstep(1, 8, spreadK);
        if (precipMmH > 0) clear = Math.Min(clear, (snow ? 2500 : 11000) * Math.Pow(precipMmH, snow ? -0.7 : -0.67));
        if (fogDensity <= 0) return clear;
        double sigma = 3.912 / 100.0 * fogDensity + 3.912 / clear;
        return 3.912 / sigma;
    }
}

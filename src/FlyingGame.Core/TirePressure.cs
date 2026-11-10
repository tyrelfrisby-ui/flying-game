using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// What a leg's tyre is blown up to. That one number sets the tyre's spring and footprint (<see cref="TireModel"/>), how
/// far it sinks into soft ground, and when it hydroplanes (<see cref="WheelHydro"/>: V_p in knots = 9√p).
///
/// Since 2026-11-04 every config states it on the leg (<see cref="GearConfig.TirePressurePsi"/>), next to the catalog
/// tyre it applies to. The book figures this class used to hold moved into the configs as they were (PA-18 POH 18,
/// 172S 38/29, Archer 24/18, 737 205/190, DHC-2 30/38, the bush Super Cub 8), and every other type got the value the
/// owner's 2026-10-16 rule gave it — gross lb / 100, 0.8 of that off the mains — so nothing moved. The bushwheel
/// rule (gross / 200) is gone: the option now fits a real tundra tyre at its own pressure (<see cref="TireFit"/>).
///
/// What is left here is the fall-back for a leg that states none: the catalog tyre's typical pressure, and for a
/// code-built leg with no tyre the owner's rule.
/// </summary>
public static class TirePressure
{
    public const double PoundsPerPsiStandard = 100.0;

    /// <summary>Is this leg a main wheel? (The others — nose and tail — often run softer.)</summary>
    private static bool IsMain(GearConfig g) => !g.IsTailwheel && System.Math.Abs(g.Pos[1]) > 0.3;

    /// <summary>Tyre pressure for one leg, in psi.</summary>
    public static double Psi(AircraftConfig c, GearConfig g)
    {
        if (g.TirePressurePsi > 0) return g.TirePressurePsi;
        TireSpec? t = TireCatalog.Of(g);
        if (t != null && t.TypicalPsi > 0) return t.TypicalPsi;
        double grossLb = (c.Mass != null ? c.Mass.MassKg : 800) * 2.20462;
        double psi = grossLb / PoundsPerPsiStandard;
        if (!IsMain(g)) psi *= 0.8;
        return System.Math.Clamp(psi, 5.0, 260.0);
    }

    /// <summary>The planing speed that pressure gives, in m/s — the number a pilot skimming a bar lives by.</summary>
    public static double HydroplaneSpeedMs(AircraftConfig c, GearConfig g) => WheelHydro.HydroplaneSpeedMs(Psi(c, g));
}

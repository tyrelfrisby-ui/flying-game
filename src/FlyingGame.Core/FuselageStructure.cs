using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>How a fuselage is built (<see cref="AircraftConfig.StructureType"/>).</summary>
public enum StructureType { Untagged, SteelTube, AluminumMonocoque, Composite, Wood }

/// <summary>
/// The fuselage in a crash (Ty 2026-11-04, after flying build 2026.1104.0427: "The fuselage is really strong so rarer than
/// having it break apart have it tend to bend and crush more. Engine, wings, tails and canopy can break off but for all
/// steel tube airplanes like Pitts, Extra, Cub, Great Lakes etc. the fuselage doesn't break apart other than at the
/// firewall").
///
/// Until this, a fuselage section (nose, cabin, tail boom) came off the first time one of its hard points ARRIVED faster than
/// its break speed (AirframeContact: tail cone 5.5 m/s, nose 6, cabin 8, × impact strength) — the cabin taking the nose and
/// the tail with it. Now every section first CRUSHES. A section struck at v_n above its break speed v_b crushes
///
///     δ = δ₀ · (v_n² / v_b² − 1),   δ₀ = <see cref="CrushTravelM"/> = 0.15 m,
///
/// which is the energy rule the tree strike already uses: the structure crushes at the force F_c = ½·m_eff·v_b²/δ₀ that stops
/// a v_b arrival in δ₀, so the energy above that arrival, ½·m_eff·(v_n² − v_b²), buys δ = that / F_c of crush. Crush
/// accumulates over strikes, and a section BREAKS only when its total crush passes its capacity (<see cref="CapacityM"/>):
///
///  • Steel tube (welded 4130 truss): the truss yields and folds, it does not part — the cabin and the tail boom have no
///    capacity limit (they bend; the crush is capped for the geometry at <see cref="MaxGeometricCrushM"/>). The ENGINE MOUNT is
///    the exception Ty named: it bends 0.20 m and then the engine leaves at the firewall.
///  • Aluminium semi-monocoque: skin buckles and stringers fold progressively before the section parts: 0.15 m of crush in
///    every section, i.e. it breaks at v_b·√2 (the cabin at 11.3 m/s instead of 8). Belly crush in gear-up and hard
///    landings of metal singles is routine, and fuselages that part do so in arrivals well above the cabin speed.
///  • Composite shells absorb little before they crack (0.05 m: break at 1.15 v_b); wood splinters after a little more
///    (0.08 m: 1.24 v_b).
///  • Untagged: capacity 0 — the first overload breaks the section, exactly the old rule.
///
/// The crush is geometry too: the struck hard point moves into the airframe by it (the aeroplane sits lower on a crushed
/// belly), and a crushed tail boom or engine mount BENDS about its station by atan(crush / arm), up to
/// <see cref="MaxBendRad"/>.
/// </summary>
public static class FuselageStructure
{
    /// <summary>The crush that stops an arrival at the break speed, m (the same as the tree strike's).</summary>
    public const double CrushTravelM = 0.15;
    /// <summary>Most a section is drawn and modelled crushed in, m (the rest of the energy is absorbed without moving the points further).</summary>
    public const double MaxGeometricCrushM = 0.30;
    /// <summary>Most a tail boom or engine mount bends, rad (30°).</summary>
    public const double MaxBendRad = 30.0 * System.Math.PI / 180.0;

    public static StructureType Parse(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "steeltube" or "steel-tube" or "steel_tube" => StructureType.SteelTube,
        "aluminummonocoque" or "aluminiummonocoque" or "aluminum" or "aluminium" => StructureType.AluminumMonocoque,
        "composite" => StructureType.Composite,
        "wood" or "wooden" => StructureType.Wood,
        _ => StructureType.Untagged,
    };

    public static StructureType Of(AircraftConfig c) => Parse(c.StructureType);

    /// <summary>The fuselage sections this rule governs.</summary>
    public static bool IsSection(AirframeComponent c) => c is AirframeComponent.Nose or AirframeComponent.Cabin or AirframeComponent.TailBoom;

    /// <summary>Crush a section takes before it breaks, m (+∞ = it never breaks).</summary>
    public static double CapacityM(StructureType t, AirframeComponent section) => t switch
    {
        StructureType.SteelTube => section == AirframeComponent.Nose ? 0.20 : double.PositiveInfinity,
        StructureType.AluminumMonocoque => 0.15,
        StructureType.Composite => 0.05,
        StructureType.Wood => 0.08,
        _ => 0.0,
    };

    /// <summary>Crush from one arrival at <paramref name="closingMs"/> on a section that breaks at <paramref name="breakMs"/>, m.</summary>
    public static double CrushFrom(double closingMs, double breakMs)
    {
        if (breakMs <= 0 || closingMs <= breakMs) return 0.0;
        return CrushTravelM * (closingMs * closingMs / (breakMs * breakMs) - 1.0);
    }

    /// <summary>The closing speed at which an intact section breaks on one arrival.</summary>
    public static double SingleBlowBreakMs(StructureType t, AirframeComponent section, double breakMs)
    {
        double cap = CapacityM(t, section);
        return double.IsPositiveInfinity(cap) ? double.PositiveInfinity : breakMs * System.Math.Sqrt(1.0 + cap / CrushTravelM);
    }
}

/// <summary>One aeroplane's fuselage crush state: accumulated crush per section, the bends, and the energy absorbed.</summary>
public sealed class FuselageCrush
{
    public StructureType Type { get; }
    public double NoseCrushM { get; private set; }
    public double CabinCrushM { get; private set; }
    public double TailCrushM { get; private set; }
    /// <summary>Energy the crush has absorbed, J (½·m_eff·(v_n² − v_b²) summed).</summary>
    public double AbsorbedJ { get; private set; }
    /// <summary>Bend of the tail boom about the tail station, rad: + = tail bent UP (pitch), and sideways (+ = to the right).</summary>
    public double TailBendPitchRad { get; private set; }
    public double TailBendYawRad { get; private set; }
    /// <summary>Bend of the engine mount, rad (+ = nose bent down).</summary>
    public double NoseBendRad { get; private set; }
    /// <summary>The airframe's bend bookkeeping (Sim's Aircraft) records the bends here.</summary>
    public void SetBends(double tailPitchRad, double tailYawRad, double noseRad) { TailBendPitchRad = tailPitchRad; TailBendYawRad = tailYawRad; NoseBendRad = noseRad; }

    public FuselageCrush(StructureType type) { Type = type; }

    public double CrushOf(AirframeComponent section) => section switch
    {
        AirframeComponent.Nose => NoseCrushM,
        AirframeComponent.Cabin => CabinCrushM,
        AirframeComponent.TailBoom => TailCrushM,
        _ => 0.0,
    };

    /// <summary>
    /// A hard point of <paramref name="section"/> arrived at <paramref name="closingMs"/> (above its break speed
    /// <paramref name="breakMs"/>) with effective mass <paramref name="mEffKg"/>. Adds the crush; returns true when the section
    /// now BREAKS (its crush has passed its capacity). <paramref name="crushM"/> is this arrival's crush.
    /// </summary>
    public bool Absorb(AirframeComponent section, double closingMs, double breakMs, double mEffKg, out double crushM)
    {
        crushM = FuselageStructure.CrushFrom(closingMs, breakMs);
        if (!FuselageStructure.IsSection(section)) return true;
        double cap = FuselageStructure.CapacityM(Type, section);
        double total = CrushOf(section) + crushM;
        if (mEffKg > 0 && closingMs > breakMs) AbsorbedJ += 0.5 * mEffKg * (closingMs * closingMs - breakMs * breakMs);
        switch (section)
        {
            case AirframeComponent.Nose: NoseCrushM = total; break;
            case AirframeComponent.Cabin: CabinCrushM = total; break;
            case AirframeComponent.TailBoom: TailCrushM = total; break;
        }
        return total > cap;
    }

    /// <summary>A load-ruled overload (the water joints): no arrival speed; steel tube still holds the cabin and tail.</summary>
    public bool BreaksUnderLoad(AirframeComponent section) =>
        !FuselageStructure.IsSection(section) || !double.IsPositiveInfinity(FuselageStructure.CapacityM(Type, section));
}

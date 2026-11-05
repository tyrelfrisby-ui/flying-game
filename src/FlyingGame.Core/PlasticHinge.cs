using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>How a wing is built, which decides how its panels bend and how far before they let go
/// (<see cref="AircraftConfig.WingSpar"/>; Ty 2026-11-05: "we need a lot more bends before it breaks").</summary>
public enum WingConstruction
{
    /// <summary>Two spruce spars, ribs and fabric (J-3, Pitts, Great Lakes, Stearman, Decathlon, Cassutt, Jenny).</summary>
    WoodFabric,
    /// <summary>Two aluminium spars, ribs and fabric (PA-18, PA-25).</summary>
    AluminumFabric,
    /// <summary>Aluminium spar and riveted stressed skin (C172, PA-28, Beaver, 2-33, the metal twins and jets).</summary>
    AluminumSkin,
    /// <summary>Glass or carbon spar caps in a sandwich shell (SR22, Extra 300, the composite gliders).</summary>
    Composite,
}

/// <summary>
/// A member's moment–rotation BACKBONE beyond yield, in plastic rotation κ (rad, accumulated in either sense):
/// yield at κ = 0 rising to the ultimate at <see cref="PeakRad"/> (strain hardening up to the first crack or local
/// buckle), falling to <see cref="ResidualRatio"/> × ultimate at <see cref="ResidualRad"/> (the cracked spar / buckled
/// flange shedding its load onto the rest of the structure), holding that to <see cref="RuptureRad"/>, where the last
/// of it tears (separation).
/// </summary>
public readonly struct HingeLaw
{
    public readonly double PeakRad, ResidualRatio, ResidualRad, RuptureRad;
    public HingeLaw(double peakRad, double residualRatio, double residualRad, double ruptureRad)
    {
        PeakRad = peakRad; ResidualRatio = residualRatio; ResidualRad = residualRad; RuptureRad = ruptureRad;
    }
}

/// <summary>
/// The hinge laws of the fleet's wing constructions (Ty 2026-11-05). A permanent bend is a PLASTIC HINGE at the panel's
/// station: the rotation it takes is the plastic strain at the extreme fibre over a hinge length L_p,
///
///     θ_p = ε_p · L_p / (h/2)  ≈  2·ε_p     with L_p ≈ h (the depth of the section, the usual plastic-hinge length),
///
/// so each rotation below is twice a strain read off the material. The yield and ultimate MOMENTS are the panel's own
/// (<see cref="WingStructure.EnsureStrength"/>: limit = yield, 14 CFR 23.305; ultimate = limit × the ultimate factor).
///
///  • Wood/fabric. Sitka spruce (ANC-18, 15 % MC): fibre stress at proportional limit 6,200 psi, modulus of rupture 9,400
///    psi, E 1.3·10⁶ psi → ε_PL 0.48 %, ε at MOR about 0.83 % (the curve bends over above the PL) → peak at
///    2 × 0.35 % = 0.007 rad. Past it the spar cracks across; a two-spar wing then carries on its other spar, its drag
///    truss and its leading edge, about 35 % of its strength, by 0.05 rad. The last attachment is the fabric and the
///    ribs: doped cotton / polyester covering stretches about 20 % before it tears → 2 × 20 % = 0.40 rad.
///  • Aluminium/fabric and aluminium skin. 2024-T3 (MMPDS): F_ty 47 ksi, F_tu 64 ksi, E 10.5·10⁶ psi, elongation 15 %+.
///    A thin spar flange or skin buckles locally near 1.5 % strain → peak at 2 × (1.5 − 0.45) % ≈ 0.02 rad; a buckled
///    thin-walled beam then folds at about half its peak moment (the mean folding moment of a thin-walled section,
///    Wierzbicki &amp; Abramowicz) by 0.10 rad, and holds that until the tension flange tears at its elongation:
///    2 × 18 % = 0.36 rad.
///  • Composite. Glass/carbon laminates are linear to failure at 1.5–2.5 % strain: the peak comes almost at once
///    (0.005 rad), the shell shatters to about 15 % (the fibres still bridging) by 0.02 rad, and the bridging fibres pull
///    out by 2 × 5 % = 0.10 rad.
/// </summary>
public static class MemberMaterials
{
    public static HingeLaw Law(WingConstruction w) => w switch
    {
        WingConstruction.WoodFabric => new HingeLaw(0.007, 0.35, 0.05, 0.40),
        WingConstruction.AluminumFabric => new HingeLaw(0.02, 0.50, 0.10, 0.36),
        WingConstruction.AluminumSkin => new HingeLaw(0.02, 0.50, 0.10, 0.36),
        _ => new HingeLaw(0.005, 0.15, 0.02, 0.10),
    };

    public static WingConstruction? Parse(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "wood" or "woodfabric" or "wood-fabric" => WingConstruction.WoodFabric,
        "aluminumfabric" or "aluminiumfabric" or "aluminum-fabric" => WingConstruction.AluminumFabric,
        "aluminum" or "aluminium" or "aluminumskin" or "aluminiumskin" => WingConstruction.AluminumSkin,
        "composite" => WingConstruction.Composite,
        _ => null,
    };

    /// <summary>The wing's construction: the config's <see cref="AircraftConfig.WingSpar"/>, else what its fuselage
    /// implies — a steel-tube fuselage in this fleet carries a wood-and-fabric wing unless the config says otherwise.</summary>
    public static WingConstruction Of(AircraftConfig c) => Parse(c.WingSpar) ?? FuselageStructure.Of(c) switch
    {
        StructureType.SteelTube or StructureType.Wood => WingConstruction.WoodFabric,
        StructureType.Composite => WingConstruction.Composite,
        _ => WingConstruction.AluminumSkin,
    };

    /// <summary>
    /// Crush a struck MEMBER takes before it breaks, m (Ty 2026-11-05) — the tail surfaces, the rudder, a wing's mid-span,
    /// a nacelle and a gear leg, under the same energy rule as a fuselage section (<see cref="FuselageStructure"/>: an
    /// arrival at v_n above its break speed v_b crushes δ₀·(v_n²/v_b² − 1), δ₀ = 0.15 m, and it parts only when the crush
    /// adds up past this). Until then any of them came off at the first arrival over v_b.
    ///  • Tail surfaces and the rudder are built like the fuselage: welded steel tube bends a long way (0.20 m, as the engine
    ///    mount), riveted aluminium folds (0.15 m), wood splinters (0.08 m), composite cracks (0.05 m).
    ///  • A wing's mid-span by its construction: aluminium 0.15 m, wood 0.08 m, composite 0.05 m.
    ///  • A nacelle is riveted aluminium (0.15 m); a gear leg is a steel or aluminium spring, tube or oleo, which bends
    ///    up to the airframe before it tears out (0.15 m).
    /// An untagged airframe keeps 0: the first overload breaks it, the old rule exactly.
    /// </summary>
    public static double CrushCapacityM(AirframeComponent comp, AircraftConfig c)
    {
        StructureType t = FuselageStructure.Of(c);
        if (t == StructureType.Untagged) return 0.0;
        switch (comp)
        {
            case AirframeComponent.TailVertical: case AirframeComponent.TailHorizontal: case AirframeComponent.Rudder:
            case AirframeComponent.ElevatorLeft: case AirframeComponent.ElevatorRight:
                return t switch { StructureType.SteelTube => 0.20, StructureType.AluminumMonocoque => 0.15, StructureType.Wood => 0.08, _ => 0.05 };
            case AirframeComponent.WingLeftInner: case AirframeComponent.WingRightInner:
            case AirframeComponent.WingLeftOuter: case AirframeComponent.WingRightOuter:
                return Of(c) switch { WingConstruction.WoodFabric => 0.08, WingConstruction.Composite => 0.05, _ => 0.15 };
            case AirframeComponent.NacelleLeft: case AirframeComponent.NacelleRight:
            case AirframeComponent.GearLeft: case AirframeComponent.GearRight: case AirframeComponent.GearNose: case AirframeComponent.GearTail:
                return 0.15;
            default:
                return 0.0;
        }
    }

    /// <summary>A covering that carries no shear (fabric): the panel resists twist only through its two spars bending in
    /// opposite senses.</summary>
    public static bool TwoSparFabric(WingConstruction w) => w is WingConstruction.WoodFabric or WingConstruction.AluminumFabric;

    /// <summary>Chordwise place of the spars (fraction of chord aft of the leading edge) and of the flexural axis between
    /// them: typical of light-aircraft two-spar wings (front 0.15–0.25 c, rear 0.60–0.70 c; flexural axis 0.33–0.40 c).</summary>
    public const double FrontSparX = 0.20, RearSparX = 0.65, FlexuralAxisX = 0.35;

    /// <summary>
    /// The panel's ultimate TORQUE about its flexural axis, from its ultimate bending moment and its construction:
    ///  • fabric over two spars: a torque T at the panel puts a couple ±T/b on spars b apart, which each carry it over the
    ///    panel's length L as a bending moment T·L/b at the station; with each spar good for half the panel's moment,
    ///    T_u = M_u · b / (2L);
    ///  • a closed stressed-skin cell: the same skin carries the bending as flanges (M ≈ σ·t·b·h) and the torque as shear
    ///    flow (T = 2·A·t·τ = 2·b·h·t·τ), so T_u / M_u = 2·F_su/F_tu = 2 × 41/64 = 1.28 for 2024-T3 (MMPDS).
    /// </summary>
    public static double UltimateTorqueNm(WingConstruction w, double ultimateNm, double chordM, double lengthM)
    {
        if (TwoSparFabric(w))
        {
            double b = (RearSparX - FrontSparX) * chordM;
            return lengthM > 1e-6 ? ultimateNm * b / (2.0 * lengthM) : ultimateNm;
        }
        return 1.28 * ultimateNm;
    }
}

/// <summary>
/// One plastic hinge: a member's rotation about one axis beyond its elastic range (Ty 2026-11-05). Below the backbone
/// (<see cref="Capacity"/>) the member is elastic and keeps no set; a demand above it turns the hinge until the demand,
/// relieved by whatever the turn moves out of the way (a wingtip rising out of the ground), comes back down to the
/// backbone. The rotation stays (<see cref="SetRad"/>), every rotation in either sense adds to <see cref="PathRad"/>, and
/// the backbone is read at the accumulated PathRad: past the peak, each new yield leaves the member weaker than the
/// last, so repeated overloads walk it down to its residual and then off (<see cref="Ruptured"/>). The work the hinge
/// has absorbed is <see cref="WorkJ"/>.
/// </summary>
public sealed class PlasticHinge
{
    public double YieldNm, UltimateNm;
    public HingeLaw Law;
    /// <summary>Permanent rotation, signed, rad.</summary>
    public double SetRad;
    /// <summary>Plastic rotation accumulated in either sense, rad.</summary>
    public double PathRad;
    /// <summary>Plastic work absorbed, J.</summary>
    public double WorkJ;
    public bool Ruptured;

    public PlasticHinge(double yieldNm, double ultimateNm, HingeLaw law)
    {
        UltimateNm = System.Math.Max(0.0, ultimateNm);
        YieldNm = System.Math.Min(System.Math.Max(0.0, yieldNm), UltimateNm);
        Law = law;
    }

    /// <summary>The backbone's moment at accumulated plastic rotation <paramref name="kappa"/>, N·m (0 once ruptured).</summary>
    public double CapacityAt(double kappa)
    {
        HingeLaw l = Law;
        if (kappa >= l.RuptureRad) return 0.0;
        if (kappa <= l.PeakRad) return l.PeakRad > 0 ? YieldNm + (UltimateNm - YieldNm) * kappa / l.PeakRad : UltimateNm;
        double res = l.ResidualRatio * UltimateNm;
        if (kappa <= l.ResidualRad) return UltimateNm - (UltimateNm - res) * (kappa - l.PeakRad) / (l.ResidualRad - l.PeakRad);
        return res;
    }

    /// <summary>What the member can carry now before it yields again, N·m.</summary>
    public double Capacity => Ruptured ? 0.0 : CapacityAt(PathRad);

    /// <summary>Past the peak: cracked or buckled, and weaker than new.</summary>
    public bool Damaged => PathRad > Law.PeakRad;

    /// <summary>
    /// Load the hinge with <paramref name="demandNm"/> (signed). <paramref name="reliefNmPerRad"/> is how much of the
    /// demand each radian of turn takes away (the stiffness of whatever is pushing: 0 for a load that does not let go,
    /// like the air). Returns the plastic rotation of this load, signed (0 within the backbone). At most
    /// <paramref name="maxStepRad"/> per call: a load the backbone cannot meet (past the peak with nothing to relieve it)
    /// keeps turning it on the next call, until it ruptures.
    /// </summary>
    public double Load(double demandNm, double reliefNmPerRad, double maxStepRad = 0.1)
    {
        if (Ruptured || double.IsNaN(demandNm)) return 0.0;
        double m = System.Math.Abs(demandNm), sgn = System.Math.Sign(demandNm);
        double cap = CapacityAt(PathRad);
        if (m <= cap) return 0.0;
        double k = System.Math.Max(0.0, reliefNmPerRad), kappa = PathRad, turned = 0.0;
        // Walk the piecewise-linear backbone: in each segment of slope H the demand meets it after (m − C)/(k + H).
        while (turned < maxStepRad)
        {
            double end = kappa < Law.PeakRad ? Law.PeakRad : kappa < Law.ResidualRad ? Law.ResidualRad : Law.RuptureRad;
            double cEnd = end >= Law.RuptureRad ? Law.ResidualRatio * UltimateNm : CapacityAt(end);
            double c0 = CapacityAt(kappa), seg = end - kappa;
            double h = seg > 1e-12 ? (cEnd - c0) / seg : 0.0;
            double excess = m - k * turned - c0;
            if (excess <= 0) break;
            double d = k + h > 1e-9 ? excess / (k + h) : double.PositiveInfinity;
            d = System.Math.Min(d, maxStepRad - turned);
            if (d < seg) { WorkJ += 0.5 * (c0 + CapacityAt(kappa + d)) * d; kappa += d; turned += d; break; }
            WorkJ += 0.5 * (c0 + cEnd) * seg; kappa = end; turned += seg;
            if (kappa >= Law.RuptureRad) { Ruptured = true; break; }
        }
        PathRad = kappa;
        SetRad += sgn * turned;
        return sgn * turned;
    }

    /// <summary>Put a set of <paramref name="rad"/> into the hinge as a strike would have (scenarios, tests): the path to
    /// it is added, so the hinge is as weakened as a member bent that far.</summary>
    public void Impose(double rad)
    {
        double d = System.Math.Abs(rad - SetRad);
        if (d <= 0) return;
        double k0 = PathRad;
        PathRad += d;
        WorkJ += 0.5 * (CapacityAt(k0) + CapacityAt(PathRad)) * d;
        SetRad = rad;
        if (PathRad >= Law.RuptureRad) Ruptured = true;
    }
}

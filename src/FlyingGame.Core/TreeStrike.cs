using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// A tree as a STRUCTURE (Ty 2026-11-03: "i flew into the tree tops at high speed and it just broke apart and stopped …
/// if a wing hits just that wing breaks off and everything should retain its momentum").
///
/// Until this, the forest was a <see cref="WorldSolids"/> shape like a building: a solid cylinder of the CROWN's radius
/// (2.4 × (0.22 + H/24) m — 4.7 m for a 42 m fir) standing from the ground to the very top, pushed back with the
/// airframe's hard-ground spring, and every hard point that met it broke by closing speed. A treetop was a 9 m wide
/// concrete pillar. Now a tree is what it is: a tapering wooden STEM that bends and snaps, inside a cone of BRANCHES
/// that the airframe pushes its way through.
///
///  • Stem: a cone, diameter DBH at the ground falling to nothing at the top, DBH = H / <see cref="Slenderness"/>
///    (stand-grown conifers run H/D 50–100; 60 is a sound Douglas-fir stand — Wang, Titus &amp; Lieffers 1998, Can. J. For.
///    Res. 28:1222; a 42 m tree is then 0.70 m through, a 20 m one 0.33 m).
///  • Strength: green Douglas-fir, modulus of rupture 53 MPa (USDA Wood Handbook FPL-GTR-282, Table 5-3a: 7,700 psi
///    green), density 600 kg/m³ green (specific gravity 0.45 with ~35 % moisture). Struck fast, a stem fails in bending
///    within a couple of diameters of the blow — the rest of it is held by its own inertia, not by the root — so the force
///    it can take is M_rupture / (2d) = MOR·π·d³/32 / (2d) = MOR·π·d²/64: 6.5 kN at 5 cm, 26 kN at 10 cm, 230 kN at 30 cm.
///  • Snapping it costs the airframe the momentum of the top it throws (m_top/4: a free rod struck at its end moves with
///    a quarter of its mass, Den Hartog) and the work of cutting through it (F·d over the time the edge takes to cross d).
///  • Crown: the three skirts the game draws (<see cref="Skirts"/>), from 22 % of the height to the top, of bulk density
///    <see cref="CrownBulkDensityKgM3"/> — needles and branchwood. What goes through it pushes that much wood out of its
///    way at its own speed, F = ρ_c·A·v² over the band it sweeps (its thickness plus a branch's spray,
///    <see cref="FoliageSprayM"/>), and throws the outer part of every branch it cuts (<see cref="ThrownBranchKg"/>).
/// </summary>
public static class TreeStructure
{
    /// <summary>Height over stem diameter at the ground (stand-grown conifer).</summary>
    public const double Slenderness = 60.0;
    /// <summary>Green Douglas-fir modulus of rupture, Pa (USDA Wood Handbook Table 5-3a, 7,700 psi).</summary>
    public const double ModulusOfRupturePa = 53.0e6;
    /// <summary>Green wood, kg/m³.</summary>
    public const double WoodDensityKgM3 = 600.0;
    /// <summary>Where a struck stem breaks: this many diameters from the blow (the lever arm of the failing section).</summary>
    public const double FailureArmDiameters = 2.0;
    /// <summary>Crown base as a fraction of the height: the lowest skirt's (<see cref="Skirts"/>).</summary>
    public const double CrownBaseFraction = 0.22;
    /// <summary>Needles, twigs and branchwood per crown volume, kg/m³. Fire science's canopy bulk density (fuel finer than
    /// 6 mm) is 0.05–0.3 kg/m³ for western conifers (Scott &amp; Reinhardt 2001, RMRS-RP-29); with the branchwood a crown
    /// holds roughly five times that.</summary>
    public const double CrownBulkDensityKgM3 = 1.5;

    public static double BaseDiameterM(in Forest.Tree t) => t.HeightM / Slenderness;

    /// <summary>Stem diameter at height <paramref name="up"/> (m AMSL); 0 outside the stem.</summary>
    public static double DiameterAt(in Forest.Tree t, double up)
    {
        double h = up - t.GroundM;
        if (h < 0 || h > t.HeightM) return 0;
        return BaseDiameterM(t) * (1.0 - h / t.HeightM);
    }

    /// <summary>The force a stem of diameter <paramref name="d"/> takes before it snaps, N.</summary>
    public static double BreakForceN(double d) => ModulusOfRupturePa * System.Math.PI * d * d / (32.0 * FailureArmDiameters);

    /// <summary>Mass of the stem above height <paramref name="up"/> (a cone), kg.</summary>
    public static double MassAboveKg(in Forest.Tree t, double up)
    {
        double d = DiameterAt(t, up), above = t.TopM - up;
        if (d <= 0 || above <= 0) return 0;
        return WoodDensityKgM3 * System.Math.PI / 12.0 * d * d * above;
    }

    // ---- the crown is the crown the pilot sees (Ty 2026-11-05 13:49: "wing passing through tree branches ... has too little
    // drag"). The crown was a single cone from 20 % of the height to the top, and the game draws three stacked skirts
    // (Bridge ForestBuilder): near the top the drawn crown was twice the cone's width, so a wing could go through drawn
    // foliage with the physics a metre clear of it — measured on the base e3e754bc (TreeBranchPassTests' table), a J-3's
    // right wing through the top 4 m of a 25 m fir changed nothing at all. Now one table is both: each skirt is a
    // four-sided spire (base height, rise, radius, as fractions of the tree's height and crown radius), and the crown at
    // a height is the widest skirt there.

    /// <summary>The drawn crown, lowest skirt first: (base, rise) as fractions of the height, radius as a fraction of
    /// <see cref="Forest.Tree.CrownRadiusM"/>. Bridge ForestBuilder draws exactly these (its close-up trees).</summary>
    public static readonly (double Base, double Rise, double Radius)[] Skirts = { (0.22, 0.42, 1.00), (0.52, 0.34, 0.72), (0.78, 0.24, 0.42) };
    /// <summary>A skirt's four sides stand out to 1.414 and 1.174 of its radius at alternate corners (ForestBuilder.Spire):
    /// a circle of the same area has 1.03 of it.</summary>
    public const double SkirtAreaRadius = 1.03;

    /// <summary>Crown radius at height <paramref name="up"/> (the drawn skirts, <see cref="Skirts"/>); 0 outside.</summary>
    public static double CrownRadiusAt(in Forest.Tree t, double up)
    {
        double h = up - t.GroundM, H = t.HeightM, best = 0;
        if (h < 0 || h > H) return 0;
        for (int i = 0; i < Skirts.Length; i++)
        {
            double b = Skirts[i].Base * H, rise = Skirts[i].Rise * H;
            if (h < b || h > b + rise) continue;
            best = System.Math.Max(best, SkirtAreaRadius * Skirts[i].Radius * t.CrownRadiusM * (1.0 - (h - b) / rise));
        }
        return best;
    }

    /// <summary>Needles and branchwood above height <paramref name="up"/>, kg: what a snapped top throws besides its stem.</summary>
    public static double CrownMassAboveKg(in Forest.Tree t, double up)
    {
        double top = t.TopM;
        if (up >= top) return 0;
        const int n = 12;
        double dz = (top - up) / n, v = 0;
        for (int i = 0; i < n; i++) { double r = CrownRadiusAt(t, up + (i + 0.5) * dz); v += System.Math.PI * r * r * dz; }
        return CrownBulkDensityKgM3 * v;
    }

    /// <summary>How far a conifer branch's spray of twigs and needles stands off it, m, across both sides together. A part
    /// going through a crown meets the foliage of every branch whose spray it cuts, so the band it sweeps is its own
    /// thickness plus one spray (Douglas-fir sprays are flat fans 0.3–0.5 m across; 0.4 is a modelling choice, not a fit).
    /// It replaces a fixed 0.3 m band for every part.</summary>
    public const double FoliageSprayM = 0.4;

    /// <summary>A branch struck <paramref name="rho"/> from the stem is cut there and its outer part thrown with the part that
    /// struck it: the mass that moves, kg — the crown's own mass per metre of branch (ρ_c / λ, needles with it) over the
    /// outer length, a quarter of it (a free rod struck at its end, Den Hartog, as for the snapped top).</summary>
    public static double ThrownBranchKg(in Forest.Tree t, double up, double rho)
    {
        double rc = CrownRadiusAt(t, up), lambda = BranchLengthDensity(t, up);
        if (rc <= 0 || lambda <= 0 || rho >= rc) return 0;
        return 0.25 * CrownBulkDensityKgM3 / lambda * (rc - rho);
    }

    // ---- branches (Ty 2026-11-04: "Tree hits were too forgiving: it made a sound with no damage until I got deep toward
    // the trunks"). Until this the crown was only a drag, ρ_c·A·v²: nothing in it could harm the airframe, and the only
    // thing that could was the stem, which near the top of the tree is a few centimetres thick and snaps for free.
    //
    // A conifer's branches stand out from the stem in whorls. At height h a branch's diameter where it leaves the stem is
    // about a fifth of the stem's there (Douglas-fir branches are a few centimetres through at the stem on 30–40 m trees,
    // more in the lower crown — the order of the crown-structure literature; the 0.2 is a modelling choice, not a fit), and it tapers to a twig at the
    // crown's edge: d_b(ρ) = d_twig + 0.2·d_stem·(1 − ρ/r_c). So the outer canopy is twigs, half way in
    // the branches are a few centimetres, and next to the stem they are limbs.
    // Five branches a whorl and a whorl every half metre put 10·r_c metres of branch in every metre of crown height, over
    // π·r_c² of crown: λ = 10 / (π·r_c) metres of branch per cubic metre. A straight edge of length ℓ moving at v across
    // itself through randomly oriented branches crosses (2/π)·λ·ℓ·v of them a second.
    // Each crossing has to break the branch where it is struck (its snapping force, <see cref="BreakForceN"/>) and throw
    // the struck wood out of the way at the edge's speed (ρ_w·d·v² per metre of edge in contact, over the branch's own
    // width: ρ_w·d²·v²): F = F_s(d) + ρ_w·d²·v². That force on a leading edge, a control surface or a propeller blade is
    // what damages it; the force times the branch's width is the work it costs the airframe.

    /// <summary>A branch's diameter where it leaves the stem, as a fraction of the stem's diameter there.</summary>
    public const double BranchBaseFraction = 0.2;
    /// <summary>The finest wood that matters at the crown's edge (twigs), m.</summary>
    public const double TwigDiameterM = 0.005;
    /// <summary>Branches per whorl and the spacing of the whorls, m.</summary>
    public const double WhorlBranches = 5.0, WhorlSpacingM = 0.5;

    /// <summary>Branch diameter met at height <paramref name="up"/> and horizontal distance <paramref name="rho"/> from the
    /// stem, m; 0 outside the crown.</summary>
    public static double BranchDiameterAt(in Forest.Tree t, double up, double rho)
    {
        double rc = CrownRadiusAt(t, up);
        if (rc <= 0 || rho >= rc) return 0;
        return TwigDiameterM + BranchBaseFraction * DiameterAt(t, up) * (1.0 - rho / rc);
    }

    /// <summary>Metres of branch per cubic metre of crown at height <paramref name="up"/>; 0 outside the crown.</summary>
    public static double BranchLengthDensity(in Forest.Tree t, double up)
    {
        double rc = CrownRadiusAt(t, up);
        if (rc <= 0) return 0;
        return WhorlBranches / WhorlSpacingM / (System.Math.PI * System.Math.Max(rc, 0.5));
    }

    /// <summary>Branches an edge of length <paramref name="lengthM"/> moving across itself at <paramref name="vAcrossMs"/>
    /// crosses per second where the branch density is <paramref name="lambda"/>.</summary>
    public static double CrossingsPerS(double lambda, double lengthM, double vAcrossMs) => 2.0 / System.Math.PI * lambda * lengthM * vAcrossMs;

    /// <summary>The force of one branch of diameter <paramref name="d"/> struck at <paramref name="v"/>, N.</summary>
    public static double BranchStrikeForceN(double d, double v) => BreakForceN(d) + WoodDensityKgM3 * d * d * v * v;
}

/// <summary>Where the trees are, for the airframe's strike test, and which of them have been snapped.</summary>
public interface ITreeSource
{
    /// <summary>Every tree whose stem stands inside the rectangle (world x, y).</summary>
    void Near(double x0, double y0, double x1, double y1, List<Forest.Tree> into);
    /// <summary>The top of what is left of the tree, m AMSL (its own top until it is snapped).</summary>
    double TopOf(in Forest.Tree t);
    /// <summary>The tree has been snapped at height <paramref name="up"/>: everything above it is gone.</summary>
    void BreakAt(in Forest.Tree t, double up);
}

/// <summary>A fixed list of trees (tests, scenarios).</summary>
public sealed class FixedTrees : ITreeSource
{
    public readonly List<Forest.Tree> Trees = new();
    private readonly Dictionary<int, double> _top = new();
    public FixedTrees(params Forest.Tree[] trees) { Trees.AddRange(trees); }
    public void Near(double x0, double y0, double x1, double y1, List<Forest.Tree> into)
    {
        foreach (Forest.Tree t in Trees) if (t.X >= x0 && t.X <= x1 && t.Y >= y0 && t.Y <= y1) into.Add(t);
    }
    private int Key(in Forest.Tree t) { for (int i = 0; i < Trees.Count; i++) if (Trees[i].X == t.X && Trees[i].Y == t.Y) return i; return -1; }
    public double TopOf(in Forest.Tree t) => _top.TryGetValue(Key(t), out double h) ? h : t.TopM;
    public void BreakAt(in Forest.Tree t, double up) { int k = Key(t); _top[k] = System.Math.Min(TopOf(t), up); }
}

/// <summary>A straight edge of the airframe that can meet a tree: a wing's leading edge inboard or outboard of its
/// structural station, a fuselage section, a tailplane's or fin's leading edge. Body frame (config origin).</summary>
public sealed class StrikeEdge
{
    public Vec3 A, B;
    public AirframeComponent Component;
    /// <summary>The <see cref="WingStructure"/> panel this edge belongs to (-1 = none).</summary>
    public int Panel = -1;
    /// <summary>Half the part's thickness, m: how close to the stem's surface the edge's centreline comes when it touches.</summary>
    public double RadiusM;
    /// <summary>For a wing edge: the station the edge's moment is taken about (|y|) and the ultimate moments (vertical,
    /// in-plane), N·m. For a fuselage or tail edge: <see cref="CrushVBreakMs"/> instead.</summary>
    public double StationY, UltimateNm, InPlaneUltimateNm;
    public double CrushVBreakMs;
    public int Side;
    public string Name = "";
    /// <summary>Components whose loss also takes this edge.</summary>
    public AirframeComponent[] GoneWith = System.Array.Empty<AirframeComponent>();
    /// <summary>The aero strips behind each third of the edge (AeroModel strip order), and the control surface among them
    /// (null = none): what branches met by that third damage (<see cref="BranchStrike"/>).</summary>
    public int[][] ThirdStrips = { System.Array.Empty<int>(), System.Array.Empty<int>(), System.Array.Empty<int>() };
    public AirframeComponent?[] ThirdControl = new AirframeComponent?[3];
    /// <summary>The face the part shows the flow when it moves along its own length (a fuselage's nose, a float's bow), m:
    /// width and height. 0 = none (a wing or a leg moves across itself, and its thickness is <see cref="RadiusM"/>).</summary>
    public double FrontWidthM, FrontHeightM;
    /// <summary>A landing-gear leg (Ty 2026-11-05 13:50: "gear/float strikes — drag with moment ... and a velocity
    /// penalty"): it meets trees only while it is down and on.</summary>
    public bool Gear;
    /// <summary>Nothing the model can break off (a float): a stem it meets snaps, or stops it.</summary>
    public bool Unbreakable;
}

/// <summary>
/// What branches do to an airframe going through them (Ty 2026-11-04; the branch model is in <see cref="TreeStructure"/>).
/// Each branch crossing a leading edge pushes on it with F = F_s(d) + ρ_w·d²·v². A leading edge dents, and the fabric or
/// skin behind it tears, when that force passes <see cref="LeadingEdgeDentN"/>; a control surface is damaged past
/// <see cref="ControlSurfaceN"/>; a propeller blade nicks on anything thicker than <see cref="PropNickM"/>, its tips being
/// chewed back by a share of the wood's width, and stops on a limb of <see cref="PropStopM"/>. A limb of
/// <see cref="LimbM"/> or more is struck as a stem would be: it snaps, or it bends or breaks the wing panel it met.
///
/// Damage accumulates as a fraction 0..1 per third of each edge: the share of that third's length that is dented. Each
/// crossing over the threshold dents <see cref="DentWidthM"/>·F/F₀ of it (a branch just at the threshold leaves a 5 cm
/// dent, one at twice it 10 cm). A strip at damage D keeps 1 − <see cref="LiftLossAtFull"/>·D of its lift and adds
/// <see cref="ExtraCdAtFull"/>·D of profile drag (a crushed leading edge stalls early and separates; torn fabric
/// balloons). A control surface's own damage reaching 1 takes it off.
/// </summary>
public static class BranchStrike
{
    /// <summary>Force a leading edge takes from a branch before it dents, N: 0.025–0.032 in aluminium or a plywood/fabric
    /// D-nose over its ribs dents under a 2–3 cm rod at a kilonewton or two.</summary>
    public const double LeadingEdgeDentN = 1500.0;
    /// <summary>Force on a control surface (light ribs, thin skin or fabric) before it is damaged, N.</summary>
    public const double ControlSurfaceN = 2500.0;
    /// <summary>A branch at least this thick nicks a propeller blade, m; this thick, it stops the engine (the blade breaks
    /// or the crankshaft is shock-loaded: sudden stoppage).</summary>
    public const double PropNickM = 0.01, PropStopM = 0.06;
    /// <summary>Blade tip lost per crossing, as a share of the branch's diameter.</summary>
    public const double PropChewFraction = 0.05;
    /// <summary>A branch this thick is struck as a limb (snap / bend / break), m.</summary>
    public const double LimbM = 0.05;
    /// <summary>After a limb strike, the next one on the same edge and tree comes when the limbs are this much thicker.</summary>
    public const double LimbStepRatio = 1.33;
    /// <summary>Width of leading edge one branch at the threshold dents, m.</summary>
    public const double DentWidthM = 0.05;
    public const double LiftLossAtFull = 0.4, ExtraCdAtFull = 0.06;
    /// <summary>
    /// Ty's setting, not physics (Ty 2026-11-05 13:49–13:52: a wing through branches "has too little drag; should yaw the
    /// airplane more toward the struck wing", and "can fly through every treetop with little/no slowdown"). The crown's
    /// push on each part — its foliage ρ_c·A·v², the work of the branches it breaks and the branches it throws — is
    /// multiplied by this. At 1 (the model as derived, crown matched to the drawn skirts) a J-3 at 32 m/s with its right
    /// wing's outer metre through a 42 m fir's crown 8 m below the top turns 0.8° toward it (peak yaw rate 9.8°/s) and loses 0.19 m/s;
    /// at 3, 7.0° (28.5°/s) and 0.53 m/s (TreeBranchPassTests). 4 turned 9.2° but slowed a Pitts deep in a 40 m fir's lower
    /// crown so much before its limbs that none could break the wing (CrashTuningTests.AHighEnergyLimbHitCanBreakAWing). The crown's mass is already generous (a 42 m fir's crown here weighs
    /// ~900 kg, against ~300 kg of foliage and live branch in the Douglas-fir biomass tables), so nothing in the wood
    /// itself justifies the factor: it is the feel Ty asked for. Tune it here.
    /// </summary>
    public const double CrownGrip = 3.0;

    /// <summary>Damage one crossing of force <paramref name="forceN"/> adds to an edge of length <paramref name="lengthM"/>
    /// against <paramref name="thresholdN"/>: the share of its length dented.</summary>
    public static double DamageFrom(double forceN, double thresholdN, double lengthM) =>
        forceN > thresholdN && lengthM > 1e-6 ? DentWidthM * forceN / thresholdN / lengthM : 0.0;
}

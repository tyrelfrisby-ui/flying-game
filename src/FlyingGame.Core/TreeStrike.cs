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
///  • Crown: a cone of branches from 20 % of the height to the top, widest at its base (<see cref="Forest.CrownRatio"/>),
///    of bulk density <see cref="CrownBulkDensityKgM3"/> — needles and branchwood. What goes through it pushes that much
///    wood out of its way at its own speed: F = ρ_c·A·v².
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
    /// <summary>Crown base as a fraction of the height.</summary>
    public const double CrownBaseFraction = 0.2;
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

    /// <summary>Crown radius at height <paramref name="up"/>: widest at the crown base, nothing at the top; 0 outside.</summary>
    public static double CrownRadiusAt(in Forest.Tree t, double up)
    {
        double h = up - t.GroundM;
        if (h < CrownBaseFraction * t.HeightM || h > t.HeightM) return 0;
        return t.CrownRadiusM * (t.HeightM - h) / ((1.0 - CrownBaseFraction) * t.HeightM);
    }
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
}

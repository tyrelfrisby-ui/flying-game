using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// How far one SIDE's wing has come apart under flight load (Ty 2026-11-10). The stages are not a severity dial: which
/// of them a wing can reach depends on how it is held up (<see cref="WingPanel.Braced"/>).
/// </summary>
public enum WingSideStage
{
    /// <summary>Whole. It may be permanently bent and weakened; it is all still there.</summary>
    Intact,
    /// <summary>STRUT-BRACED only: the sections outboard of the structural station have gone and the braced inner bay
    /// is still flying. The wing is shorter, and what is left of it still lifts.</summary>
    OutboardGone,
    /// <summary>CANTILEVER only: the wing has FOLDED UP about its root and is still attached, still making whatever
    /// force its folded shape makes.</summary>
    Folded,
    /// <summary>The whole wing on this side has left the aeroplane.</summary>
    Departed,
}

/// <summary>
/// STAGGERED, PROGRESSIVE WING FAILURE — Ty 2026-11-10, verbatim:
///
///   "Create a staggered failure where outboard sections break off first unless the overstress is severe (rapid onset
///    1 g over breaking point for outboards) and account for aileron load so one tip could break off but not the other
///    in some situations. This for strut braced. For cantilever have the wings fold up 1 g before departing using same
///    logic and continue to account for their aerodynamic force and treat them independently based on aileron etc so
///    one wing could fold while the other doesn't or one wing could fold while the other departs. We just need more
///    progressive stagger to the failures."
///
/// Before this, the wings were ONE <see cref="OverstressMember"/> on the whole aeroplane's load factor, and they left in
/// one symmetric event at 2.0 × the limit. The two things that make a stagger possible are both here:
///
///  • EACH SIDE IS ITS OWN MEMBER, on its own load. In a rolling pull the two wings do not carry the same thing: the
///    down-going aileron's wing carries more. The side's load factor is the symmetric part plus half the rolling
///    moment over the side's lift arm (Aircraft.WingSideLoadFactor) — which is exact rather than fitted, because an
///    aero roll moment IS the difference between the two wings' lift, and half of any rolling moment reaches each wing
///    root whatever spanwise station makes it. So one tip reaches its breaking point while the other does not.
///
///  • THE LADDER ABOVE THE MEMBER. The member still breaks at <see cref="OverstressMember.BreakRatio"/>; what that
///    costs the aeroplane now depends on the wing and on how fast the load arrived (<see cref="Severe"/>, <see cref="FoldRatio"/>).
///
/// WHY A BRACED WING SHEDS ITS TIPS AND A CANTILEVER WING FOLDS. A strut-braced wing's inner bay is a beam on two
/// supports — the root fitting and the lift-strut attach — so its moment is small (the q·L²/8 of
/// <see cref="WingStructure.EnsureStrength"/>'s braced branch), while everything outboard of the strut is a full
/// cantilever off that attach. The critical station is the OUTBOARD one, and that is where it comes apart first: Ty's
/// rule and the structure's own. A cantilever wing has no strut, its moment peaks at the root, and a root that goes
/// over does not shed a tip — it folds the wing up. The same member therefore means a different station by type, and
/// <see cref="WingPanel.Braced"/> (already authored per type) is the switch.
/// </summary>
public static class WingFailureLaw
{
    /// <summary>
    /// THE PROGRESSIVE STAGE, one g before the break, in the member's own units (multiples of its limit): where a
    /// strut-braced wing sheds its outboard sections and where a cantilever wing folds up. One rung for both, because
    /// his two sentences describe one ladder — "outboard sections break off first" and "fold up 1 g before departing
    /// using same logic".
    ///
    /// Never inside the ultimate ratio, because below that the structure is still elastic and neither a shed panel nor
    /// a fold is elastic: an aeroplane whose limit is so low that one g spans its whole overstress band stages at the
    /// ultimate load rather than at the limit.
    ///
    /// A READING WORTH FLAGGING. "unless the overstress is severe (rapid onset 1 g over breaking point for outboards)"
    /// can be read two ways, and they put this rung in different places:
    ///  (A) the outboards go AT the breaking point and the whole wing only on a snatch one g BEYOND it. The braced
    ///      ladder is then not the cantilever one, "1 g" means something different in his two sentences, and 2.0 × the
    ///      limit stops taking the wings off most of the fleet (every strut-braced and biplane type) — which contradicts
    ///      his own 2026-11-05 pattern, "20 degrees at 2.0 where they would snap and depart".
    ///  (B) the outboards' breaking point is one g BELOW the wing's, so they go first at 2.0 − 1 g and the wing still
    ///      departs at 2.0, and a rapid onset runs past the outboard rung before it can act.
    /// (B) is what is built: it makes one ladder of both sentences, it uses his "1 g" the same way in each, and it
    /// leaves every existing threshold where it was. If he meant (A), this method and <see cref="Severe"/> are the two
    /// lines to change and nothing else.
    /// </summary>
    public static double StageRatio(double oneGRatio) =>
        System.Math.Max(OverstressMember.UltimateRatio, OverstressMember.BreakRatio - oneGRatio);

    /// <summary>Where a CANTILEVER wing folds — the same rung (<see cref="StageRatio"/>), named for his sentence.</summary>
    public static double FoldRatio(double oneGRatio) => StageRatio(oneGRatio);

    /// <summary>
    /// A SEVERE overstress: "rapid onset 1 g over breaking point for outboards" — the load has run one g past the
    /// outboards' breaking point (<see cref="StageRatio"/>, so: to the WING's breaking point) before the structure has
    /// caught up with the outboards'. The two halves of his parenthesis are one measurement, because the member feels
    /// the load through a first-order lag (<see cref="OverstressMember.ResponseTauSec"/>, 50 ms) and on a ramp of R
    /// ratios per second what it feels sits exactly R·τ behind what the air is making. At the instant the felt load
    /// reaches the outboard rung,
    ///
    ///   commanded − felt  ≥  one g   ⟺   R·τ ≥ one g   ⟺   R ≥ one g / 50 ms,
    ///
    /// so "the air is already demanding one g beyond the outboards' break" and "the load is arriving at least one g per
    /// 50 ms" are the same test. A pull that creeps up to the rung is demanding barely more than the rung when it gets
    /// there and is not severe, and the outboards go first; a snatch straight through is demanding a great deal more,
    /// the outboard rung is skipped, and the wing departs whole at its own breaking point as it always did.
    ///
    /// <paramref name="oneGRatio"/> is one g in the member's own units: 1/limit at the rated gross, and more when the
    /// aeroplane is light — a light aeroplane's structure has further to go per g, the same weighting the member's load
    /// already carries.
    /// </summary>
    /// <summary>
    /// The tolerance on <see cref="Severe"/>'s comparison. A pull to EXACTLY the breaking point has to read as severe,
    /// because that is the case Ty wrote down on 2026-11-05 ("a single pull to 2.0 at gross weight would permanently
    /// bend the wings … 10 degrees") and a wing that staged on the way there would have shed its tips instead of
    /// bending. In binary 7.6/3.8 is 1.9999999999999998, not 2, so the comparison needs a hair of room; a billionth of
    /// a limit load is far below anything the structure can tell apart.
    /// </summary>
    public const double SevereTolerance = 1e-9;

    public static bool Severe(double commandedEffectiveRatio, double oneGRatio) =>
        commandedEffectiveRatio >= StageRatio(oneGRatio) + oneGRatio - SevereTolerance;

    /// <summary>The same for a member being stepped at what the air is making
    /// (<see cref="OverstressMember.CommandedEffectiveRatio"/>).</summary>
    public static bool Severe(OverstressMember m, double oneGRatio) => Severe(m.CommandedEffectiveRatio, oneGRatio);

    /// <summary>
    /// HOW FAR A FOLDED WING FOLDS, rad — and the answer is not a chosen angle, it is where the fold stops.
    ///
    /// Once the root has gone over, what holds the wing up is the hinge's RESIDUAL capacity: the cracked spar, the drag
    /// truss and the covering that still bridge the break, <see cref="HingeLaw.ResidualRatio"/> × the ultimate moment
    /// (0.35 for wood and fabric, 0.50 for aluminium, 0.15 for composite — <see cref="MemberMaterials"/>, measured from
    /// the materials, not invented here). The air's moment about the fold falls as the panel rotates out of the flow:
    /// the lift turns with the wing, so the part of it that still bends the root up goes as cos φ. The wing therefore
    /// folds until
    ///
    ///     M_demand · cos φ  =  residual · M_ultimate
    ///
    /// and stops. With the demand at the breaking point (2.0 × limit = 1.33 × ultimate at the usual 1.5 ultimate
    /// factor) that is 75° for a wood-and-fabric wing, 68° for aluminium and 84° for composite: a composite wing goes
    /// right over, an aluminium one folds less far, which is what the residual strengths say and what photographs of
    /// each show. Past 90° there is nothing left to fold — the lift makes no fold-up moment at all — so it is capped
    /// there.
    ///
    /// The fold is PLASTIC: it never comes back as the load comes off (the caller keeps the largest it has reached),
    /// and a bigger demand later folds it further. And because folding kills that wing's lift, the fold RELIEVES its own
    /// load — which is why a folded wing can stay on the aeroplane instead of departing on the next step, and why Ty
    /// asked that the folded wing keep making its aerodynamic force.
    /// </summary>
    /// <param name="effectiveRatio">The load the member is feeling, multiples of its own limit.</param>
    /// <param name="ultimateFactor">Ultimate over limit (<see cref="LimitsConfig.UltimateFactor"/>, usually 1.5).</param>
    /// <param name="residualRatio">What still bridges the fold, as a share of the ultimate moment.</param>
    public static double FoldRad(double effectiveRatio, double ultimateFactor, double residualRatio)
    {
        double uf = ultimateFactor > 1.0 ? ultimateFactor : 1.5;
        double demandOverUltimate = System.Math.Abs(effectiveRatio) / uf;
        if (demandOverUltimate <= residualRatio) return 0.0;      // the residual holds it: no fold at all
        return System.Math.Acos(System.Math.Clamp(residualRatio / demandOverUltimate, 0.0, 1.0));
    }
}

/// <summary>
/// One SIDE's wing as a failing structure (<see cref="WingFailureLaw"/>): its own overstress member on its own load, and
/// how far it has come apart. Per SIDE, not per wing: a biplane's two planes on one side are one braced cellule — the
/// interplane struts tie the upper and lower outboard panels into a single bay, and the bay that lets go takes both.
/// </summary>
public sealed class WingSideStructure
{
    /// <summary>−1 left, +1 right.</summary>
    public int Side { get; }
    /// <summary>Held up by struts or by the other plane of a biplane cellule (<see cref="WingPanel.Braced"/>): its
    /// critical station is the outboard one and it sheds its tips. A cantilever wing folds at the root instead.</summary>
    public bool Braced { get; set; }
    /// <summary>The flight-overstress member at this side's critical station: flex, permanent set and weakening,
    /// exactly as the one symmetric member did, on the load THIS wing carries.</summary>
    public OverstressMember Member { get; }
    public WingSideStage Stage { get; set; } = WingSideStage.Intact;
    /// <summary>Has this wing anything to shed or fold? False for a type with no wing panels at all — a weight-shift
    /// wing or a paraglider canopy (<see cref="WingStructure.Build"/> gives those none) — and such a wing keeps the
    /// one symmetric event it always had, because there is no outboard section to lose and no root to fold about.</summary>
    public bool Stages { get; set; } = true;
    /// <summary>How far this wing has folded, rad (0 unless it has; <see cref="WingFailureLaw.FoldRad"/>). Plastic:
    /// it only ever grows.</summary>
    public double FoldRad { get; set; }
    /// <summary>The load factor this wing is carrying (Aircraft.WingSideLoadFactor), for probes and tests.</summary>
    public double LoadFactor { get; set; } = 1.0;

    public WingSideStructure(int side)
    {
        Side = side;
        Member = new OverstressMember(side < 0 ? "left wing" : "right wing");
    }

    public bool Gone => Stage == WingSideStage.Departed;
}

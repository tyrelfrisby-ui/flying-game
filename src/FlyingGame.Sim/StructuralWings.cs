using FlyingGame.Core;

namespace FlyingGame.Sim;

/// <summary>
/// The wings' half of <see cref="StructuralState"/>: the STAGGERED, PROGRESSIVE FAILURE of Ty 2026-11-10
/// (<see cref="WingFailureLaw"/> has his words and the derivations). Each side is its own member on its own load, and
/// above the member sits a ladder of stages instead of the one symmetric event the wings used to have:
///
///   STRUT-BRACED            CANTILEVER
///   ────────────            ──────────
///   break        → outboard sections depart,     fold ratio (break − 1 g) → folds up about the root, still attached
///                  the braced inner bay flies on                            and still making its own force
///   break, SEVERE  → the whole wing departs      break                    → the whole wing departs
///   (rapid onset, 1 g beyond the break)
///   break again  → the whole wing departs
///
/// Three things are deliberately NOT asserted here, because the aeroplane measures them for itself on the next step:
///  • how much the load falls once a side has shed its tips or folded — that is the lift those surfaces no longer make,
///    which reaches the member through <see cref="Aircraft.WingSideLoadFactor"/> like any other lift;
///  • which side gets the bigger share of a rolling pull — the aero roll moment says;
///  • whether a folded wing survives — it does if the relief it bought itself is enough, and does not if it is not.
/// That is what makes the stagger emergent rather than scripted: the only numbers in the law are Ty's one g and the
/// materials' residual strengths.
/// </summary>
public sealed partial class StructuralState
{
    /// <summary>Both wings, for the loops that treat them alike. Left first, so a tie goes to the left wing as the
    /// single-member reading always did.</summary>
    internal WingSideStructure[] Wings2 => _wings2 ??= new[] { LeftWing, RightWing };
    private WingSideStructure[]? _wings2;

    /// <summary>
    /// ONE G IN THE WINGS' OWN UNITS: multiples of their limit. The member's load is the load factor over the published
    /// limit, weighted by how heavy the aeroplane is against the gross it was rated at
    /// (<see cref="LimitsConfig.WingWeightRatio"/>), so one g is that same weighting over that same limit. A J-3 at
    /// gross (3.8 g limit) has one g = 0.263 of its limit, so its outboard panels depart at 7.6 g and the whole wing
    /// only on a snatch to 8.6 g; an Extra 300 at gross (10 g) has one g = 0.100, and its wings fold at 19 g and depart
    /// at 20. A LIGHT aeroplane's one g is a bigger step, which is right: it has further to go per g.
    /// </summary>
    private double OneGRatio(double wingWeightRatio, double sign) =>
        wingWeightRatio / (sign >= 0 ? _publishedPos : _publishedNeg);

    /// <summary>Ultimate over limit, for the fold angle (<see cref="WingFailureLaw.FoldRad"/>).</summary>
    private double UltimateFactor => _limits.UltimateFactor > 1.0 ? _limits.UltimateFactor : 1.5;

    /// <summary>
    /// What still bridges a folded root, as a share of the ultimate moment: the wing construction's residual capacity
    /// (<see cref="MemberMaterials.Law"/>). Set from the config when the aeroplane is built; 0.35 (wood and fabric,
    /// the fleet's commonest) for a <see cref="StructuralState"/> built with no config at all.
    /// </summary>
    internal double FoldResidualRatio { get; set; } = MemberMaterials.Law(WingConstruction.WoodFabric).ResidualRatio;

    /// <summary>
    /// Advance both wings one step and return what each of them did. The member still does all the structural work —
    /// flex, permanent set, weakening, the break — exactly as the single symmetric member did; this only decides what
    /// the break COSTS, and when a cantilever wing folds on the way to it.
    /// </summary>
    private StructuralBreak StepWings(double nLeft, double nRight, double wingWeightRatio, double dt)
    {
        var broke = StructuralBreak.None;
        for (int k = 0; k < Wings2.Length; k++)
        {
            WingSideStructure w = Wings2[k];
            if (w.Gone) continue;
            bool left = w.Side < 0;
            double n = left ? nLeft : nRight;
            w.LoadFactor = n;
            double r = Ratio(n) * wingWeightRatio;
            double oneG = OneGRatio(wingWeightRatio, r);
            bool snapped = w.Member.Step(r, dt);

            // THE PROGRESSIVE RUNG, one g before the break (WingFailureLaw.StageRatio): a strut-braced wing sheds the
            // sections outboard of its station, a cantilever wing folds up about its root. Skipped when the overstress
            // is SEVERE — a rapid onset runs past this rung before it can act, and the wing departs whole at its own
            // breaking point as it always did, which is what "unless the overstress is severe" buys.
            //
            // Severity is asked HERE and nowhere else, on the step the rung is reached, because that is the only
            // instant at which the gap between what the air is demanding and what the structure has felt IS the onset
            // rate. Asked every step, a slow pull that ended up a long way past the break would read as severe for the
            // rest of the manoeuvre and "rapid onset" would mean nothing at all.
            //
            // BOTH the felt load and the air's demand have to be at the rung. The felt load alone passes through it
            // twice — once on the way up and once on the way DOWN — and a wing that came off while the pilot was
            // unloading would be a wing that came off because he stopped pulling. (Measured: without the second
            // condition, Ty's own 2.0 × limit pull from 2026-11-05 shed its tips on the recovery.)
            double stage = WingFailureLaw.StageRatio(oneG);
            if (w.Stages && w.Stage == WingSideStage.Intact && w.Member.EffectiveRatio >= stage && w.Member.CommandedEffectiveRatio >= stage
                && !WingFailureLaw.Severe(w.Member, oneG))
            {
                if (w.Braced)
                {
                    w.Stage = WingSideStage.OutboardGone;
                    broke |= left ? StructuralBreak.OutboardLeftGone : StructuralBreak.OutboardRightGone;
                }
                else
                {
                    w.Stage = WingSideStage.Folded;
                    broke |= left ? StructuralBreak.FoldedLeft : StructuralBreak.FoldedRight;
                }
            }

            // A FOLD DEEPENS with the load, and never comes back: it is a plastic collapse, so the largest it has
            // reached is the shape the wing flies.
            if (w.Stage == WingSideStage.Folded)
            {
                double fold = WingFailureLaw.FoldRad(w.Member.EffectiveRatio, UltimateFactor, FoldResidualRatio)
                            * (w.Member.LoadRatio >= 0 ? 1.0 : -1.0);     // a pull folds it UP, a push folds it down
                if (Math.Abs(fold) > Math.Abs(w.FoldRad)) w.FoldRad = fold;
            }

            if (!snapped) continue;

            // THE BREAK. Whatever stage it had reached, the wing has now left the aeroplane — a braced wing that shed
            // its tips earlier in this same pull cannot break again inside it (one break per overstress,
            // OverstressMember), so getting here means the load went on past the rung in a later pull or in one go.
            w.Stage = WingSideStage.Departed;
            broke |= left ? StructuralBreak.WingLeftGone : StructuralBreak.WingRightGone;
        }
        // "The wings are gone" is now the SECOND side departing. For a symmetric pull the two sides see the same load
        // and break on the same step, so this is the one bit the old model raised, at the moment it always raised it.
        if (!WingsFailed && LeftWing.Gone && RightWing.Gone) broke |= StructuralBreak.Wings;
        return broke;
    }
}

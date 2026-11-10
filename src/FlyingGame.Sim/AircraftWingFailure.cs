using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// The aeroplane's side of the STAGGERED, PROGRESSIVE WING FAILURE (Ty 2026-11-10; <see cref="WingFailureLaw"/> holds his
/// words, the derivations and the law, <see cref="StructuralState"/> the ladder). Here: what each wing is carrying, and
/// what the aeroplane does when one of them sheds its tips, folds, or leaves.
/// </summary>
public sealed partial class Aircraft
{
    private double _sideLiftArmM = -1.0;
    private readonly double[] _wingStrengthPerSide = { 1.0, 1.0 };

    /// <summary>Which ladder each wing is on and what would hold a folded one up — from the panels, which
    /// InitContactDamage has already built.</summary>
    private void InitWingFailure()
    {
        // Nothing to shed and nothing to fold: a weight-shift wing or a paraglider canopy has no wing panels, and it
        // keeps the one symmetric event it always had.
        Structure.LeftWing.Stages = Structure.RightWing.Stages = _panels.Count > 0;
        if (_panels.Count == 0) return;
        // A side is braced if any of its planes is: a biplane's cellule, a strut-braced wing's lift struts.
        foreach (WingPanel p in _panels) if (p.Braced) Structure.Wing(p.Side).Braced = true;
        Structure.FoldResidualRatio = MemberMaterials.Law(MemberMaterials.Of(Config)).ResidualRatio;
    }

    /// <summary>
    /// WHAT ONE WING IS CARRYING, as a load factor (Ty 2026-11-10: "account for aileron load so one tip could break off
    /// but not the other in some situations"; "treat them independently based on aileron etc").
    ///
    /// The aero normal force is the two wings' lift together, n·W. An aero ROLLING moment is the difference between
    /// them about the centreline, L = y_c·(F_left − F_right) with y_c the side's lift arm
    /// (<see cref="WingStructure.SideLiftArmM"/>), because a rolling moment is nothing else: whatever spanwise station
    /// makes it, half of it reaches each wing root. Solving the two together,
    ///
    ///     n_side  =  n  −  side · L / (y_c · W)          (side = −1 left, +1 right)
    ///
    /// which is exact rather than fitted, and carries every cause of asymmetry the aero model knows about: aileron
    /// deflection, roll damping in a rolling pull, the dihedral effect in a slip, and — once a side has shed its tips or
    /// folded up — the lift it no longer makes. That last one is what relieves a damaged wing and lets the failure
    /// stagger instead of running away: nothing here asserts the relief, the aeroplane measures it.
    ///
    /// A type with no wing panels (a paraglider, a weight-shift wing) has no side arm and both wings read the symmetric
    /// load, exactly as before.
    /// </summary>
    public double WingSideLoadFactor(int side)
    {
        if (_panels.Count == 0) return AeroLoadFactorZ;   // no wing structure to tell apart (weight shift, a canopy)
        if (_sideLiftArmM < 0) _sideLiftArmM = WingStructure.SideLiftArmM(Config);
        double weightN = MassProperties.MassKg * Atmosphere.GravityMs2;
        if (_sideLiftArmM <= 1e-6 || weightN <= 1e-9) return AeroLoadFactorZ;
        return AeroLoadFactorZ - Math.Sign(side) * AeroRollMomentNm / (_sideLiftArmM * weightN);
    }

    /// <summary>
    /// THE SECTIONS OUTBOARD OF ONE SIDE'S STATION HAVE GONE (strut-braced, at the breaking point): the panels on that
    /// side depart through the same path a wingtip strike that broke them uses — <see cref="LoseComponent"/>, which
    /// masks their rows, cuts the aileron at the station, disconnects the slave strut, detaches their volumes and sheds
    /// their mass with the inertia and CG shift that follow. The braced inner bay is untouched and still flying.
    /// </summary>
    private void ShedOutboardPanels(int side)
    {
        for (int k = 0; k < _panels.Count; k++)
        {
            WingPanel p = _panels[k];
            if (p.Side != side || p.State == PanelState.Broken) continue;
            // A wing that is not there carries no load: the flex comes off and the plastic set stays, because that is
            // the shape the piece fell in (the same rule FailWings keeps).
            p.FlightFlexRad = 0.0; p.ElasticRad = 0.0;
            LoseComponent(p.Component);
        }
    }


    /// <summary>
    /// ONE SIDE'S WHOLE WING HAS LEFT. Not <see cref="FailWings"/>: that is the symmetric event, and it scales the mass
    /// and the inertia for BOTH wings at once. A single wing goes through <see cref="LoseComponent"/>, which already
    /// knows how to take one side off — it masks every wing row on that side, drops the panels and their ailerons and
    /// slave struts, detaches that side's volumes so CrashRelay can throw them as a wreck piece, and sheds exactly
    /// their mass, with the parallel-axis share of the inertia and the CG shift that an asymmetric loss really makes.
    /// (The symmetric path's hand-scaled tensor is left alone, so nothing already tuned moves.)
    /// </summary>
    private void FailWingSide(int side)
    {
        AirframeComponent comp = side < 0 ? AirframeComponent.WingLeft : AirframeComponent.WingRight;
        foreach (WingPanel p in _panels)
            if (p.Side == side) { p.FlightFlexRad = 0.0; p.ElasticRad = 0.0; }
        if (!IsLost(comp)) LoseComponent(comp);
    }

    /// <summary>Act on what the structure did to the wings this step.</summary>
    private void ApplyWingBreaks(StructuralBreak broke)
    {
        if ((broke & StructuralBreak.OutboardLeftGone) != 0) ShedOutboardPanels(-1);
        if ((broke & StructuralBreak.OutboardRightGone) != 0) ShedOutboardPanels(1);
        // Both sides on the same step is the symmetric failure, and it keeps its own path (mass, inertia, the
        // OnWingsFailed event and CrashRelay's two wreck pieces) exactly as it was.
        bool l = (broke & StructuralBreak.WingLeftGone) != 0, r = (broke & StructuralBreak.WingRightGone) != 0;
        if ((broke & StructuralBreak.Wings) != 0 && l && r) { FailWings(); return; }
        if (l) FailWingSide(-1);
        if (r) FailWingSide(1);
        if ((broke & StructuralBreak.Wings) != 0 && !Structure.WingsFailed)
        {
            // The second side has gone on its own step: the aeroplane has no wings, so everything that reads
            // WingsFailed (the HUD, the grading, the practice scenarios, CrashRelay) must see it — but the mass and the
            // volumes left with the two sides, so none of FailWings' symmetric bookkeeping is wanted here.
            Structure.WingsFailed = true;
            Structure.OverLimitSeverity = 0.0;
            Structure.FailureTimeSec = Atmosphere.SimTimeSec;
            Structure.RaiseFailed();
        }
    }

    /// <summary>
    /// A FOLDED WING'S SHAPE, and with it its aerodynamic force (Ty 2026-11-10: "continue to account for their
    /// aerodynamic force"). The fold is a KINK at the wing's ROOT — the structure there has gone over — so every row of
    /// that wing turns rigidly about the root by the fold angle, and the aero model flies the folded geometry: the rows'
    /// load points, their dihedral and the direction their lift acts in all move with it. Nothing is masked and nothing
    /// is made inert; a wing folded to 70° simply makes very little lift and a great deal of drag, which is why folding
    /// relieves its own load.
    /// </summary>
    private void ApplyWingFold()
    {
        _flowState.BendRad ??= new double[_stripTotal];
        _flowState.BendOffset ??= new Vec3[_stripTotal];
        foreach (WingPanel p in _panels)
        {
            double fold = Structure.Wing(p.Side).FoldRad;
            if (fold == 0.0 || p.FoldRad == fold || p.State == PanelState.Broken) continue;
            p.FoldRad = fold;
            ShapeWingSide(p);
        }
    }

    /// <summary>
    /// Write the whole of one wing side's shape into the flow state: the fold at its root, and outboard of the station
    /// whatever the panel's own bend and twist add (a strike's kink, the overstress set, the flex under load). EVERY row
    /// of the wing and side, because a fold at the root moves the inboard rows too — which is why it cannot go through
    /// <see cref="WingPanel.Deform"/> alone (that is about the station, and rightly: a strike's bend is a kink there).
    /// </summary>
    private void ShapeWingSide(WingPanel p)
    {
        int k = _panels.IndexOf(p);
        _flowState.BendRad ??= new double[_stripTotal];
        _flowState.BendOffset ??= new Vec3[_stripTotal];
        foreach (int i in p.SideStrips) WriteStripShape(p, i);
        foreach (ContactPoint cp in _contacts)
            if (cp.Panel == k && _straightBody.TryGetValue(cp, out Vec3 b0)) cp.Body = p.Shape(b0);
    }

    /// <summary>One row's shape under its panel: into <see cref="StripFlowState.BendRad"/> (the local rotation the aero
    /// flies the section at) and <see cref="StripFlowState.BendOffset"/> (where its load point has moved to).</summary>
    private void WriteStripShape(WingPanel p, int i)
    {
        if (_stripAt[i] is not { } st) return;
        Vec3 pos = st.PosVec();
        _flowState.BendRad![i] = LocalBendRad(p, Math.Abs(pos.Y));
        _flowState.BendOffset![i] = p.Shape(pos, p.FlightFlexRad) - pos;
    }

    /// <summary>The rotation the section at |y| is flying at: the fold everywhere on the wing, plus the panel's kink
    /// outboard of its station.</summary>
    private static double LocalBendRad(WingPanel p, double absY) =>
        p.FoldRad + (absY > p.BreakY ? p.BendRad + p.FlightFlexRad : 0.0);
}

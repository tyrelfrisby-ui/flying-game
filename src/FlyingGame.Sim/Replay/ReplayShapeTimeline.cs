using System.Collections.Generic;
using FlyingGame.Core;

namespace FlyingGame.Sim.Replay;

/// <summary>One wing panel's PERMANENT shape: the plastic set the airframe has kept, not the flex it happens to be
/// flying under.</summary>
public readonly struct PanelSet
{
    public readonly AirframeComponent Component;
    public readonly double BendRad, TwistRad;
    /// <summary>How far this wing had FOLDED UP about its root (WingPanel.FoldRad, Ty 2026-11-10's staggered failure).
    /// Recorded with the set because it is permanent too, and because a replay that left it out would show a folded
    /// wing flying straight — the same lie as a bent tip on an aeroplane with no wings.</summary>
    public readonly double FoldRad;
    public readonly PanelState State;

    /// <summary>Half the angle the renderer can see (WingStructure.VisibleSetRad is 0.5°): below this a change is not
    /// worth a new entry in the timeline.</summary>
    public const double TolRad = 0.25 * System.Math.PI / 180.0;

    public PanelSet(AirframeComponent component, double bendRad, double twistRad, PanelState state, double foldRad = 0.0)
    {
        Component = component; BendRad = bendRad; TwistRad = twistRad; State = state; FoldRad = foldRad;
    }

    public bool SameAs(in PanelSet o) =>
        Component == o.Component && State == o.State
        && System.Math.Abs(BendRad - o.BendRad) < TolRad && System.Math.Abs(TwistRad - o.TwistRad) < TolRad
        && System.Math.Abs(FoldRad - o.FoldRad) < TolRad;
}

/// <summary>
/// One propeller blade's PERMANENT damage (Ty 2026-11-10: "Replay shows damaged propeller that is not turning ...
/// prior to the crash"). The same four readings <c>AirframeVisual.DamageBlades</c> draws a blade from
/// (Aircraft.PropBladeRadiusM / PropBladeAffectedM / PropBladeCurl / PropBladeBroken), so a replay can hand it the
/// blade of the moment being watched instead of the one that came home.
/// </summary>
public readonly struct PropBladeShape
{
    /// <summary>The straight span the air still works on, m (a broken blade ends here).</summary>
    public readonly double AeroRadiusM;
    /// <summary>The span the strike affected, m: a metal blade's cosmetic curl, or the piece a brittle one lost.</summary>
    public readonly double AffectedM;
    /// <summary>Which way a metal curl went: +1 forward, −1 aft, 0 straight or broken.</summary>
    public readonly int Curl;
    /// <summary>Whether the affected span broke off (wood, composite).</summary>
    public readonly bool Broken;

    /// <summary>A millimetre. DamageBlades redraws at 1e-4 m, so anything below this cannot change the drawn blade and
    /// is not worth a new entry in the timeline.</summary>
    public const double TolM = 0.001;

    public PropBladeShape(double aeroRadiusM, double affectedM, int curl, bool broken)
    {
        AeroRadiusM = aeroRadiusM; AffectedM = affectedM; Curl = curl; Broken = broken;
    }

    public bool SameAs(in PropBladeShape o) =>
        Curl == o.Curl && Broken == o.Broken
        && System.Math.Abs(AeroRadiusM - o.AeroRadiusM) < TolM && System.Math.Abs(AffectedM - o.AffectedM) < TolM;
}

/// <summary>
/// The shape of the airframe at one instant: every panel's set, the tail's bend, the fuselage's crush, the engine
/// mount's droop and every propeller blade's damage. Enough to draw the aeroplane as it was, and nothing else.
/// </summary>
public readonly struct AirframeShape
{
    public readonly double Time;
    public readonly PanelSet[] Panels;
    public readonly double TailBendRad, NoseCrushM, TailCrushM, NoseBendRad, EngineDeflectionRad;
    /// <summary>The tail boom's own crush bend — the two angles AirframeVisual actually turns the boom by.</summary>
    public readonly double TailBendPitchRad, TailBendYawRad;
    /// <summary>Whether the engine mount had gone far enough to be DRAWN drooping (StructuralDamage's own test: past
    /// an event, or keeping a visible set). Recorded rather than recomputed because it latches.</summary>
    public readonly bool EngineDrawn;
    /// <summary>Every blade of every propeller, flattened [prop * <see cref="BladesPerProp"/> + blade] exactly as the
    /// sim keeps them. Empty on a jet or a glider.</summary>
    public readonly PropBladeShape[] Blades;
    /// <summary>How many blades each propeller has (so <see cref="Blade"/> can index back out).</summary>
    public readonly int BladesPerProp;
    /// <summary>Whether the PROPELLER had been lost by now — the prop strike that stops the engine and folds the blade
    /// tips back (AirframeVisual.ShowPropBend). Latches, so it is recorded rather than recomputed.</summary>
    public readonly bool PropellerLost;

    public AirframeShape(double time, PanelSet[] panels, double tailBendRad, double noseCrushM, double tailCrushM,
        double noseBendRad, double engineDeflectionRad, bool engineDrawn, double tailBendPitchRad, double tailBendYawRad,
        PropBladeShape[] blades, int bladesPerProp, bool propellerLost)
    {
        Time = time; Panels = panels; TailBendRad = tailBendRad; NoseCrushM = noseCrushM; TailCrushM = tailCrushM;
        NoseBendRad = noseBendRad; EngineDeflectionRad = engineDeflectionRad; EngineDrawn = engineDrawn;
        TailBendPitchRad = tailBendPitchRad; TailBendYawRad = tailBendYawRad;
        Blades = blades; BladesPerProp = bladesPerProp; PropellerLost = propellerLost;
    }

    /// <summary>The same shape at another moment. Every field travels, so a field added here cannot be dropped by the
    /// one caller that rebuilds a shape (<see cref="ReplayShapeTimeline.TrimBefore"/>).</summary>
    public AirframeShape At(double time) => new AirframeShape(time, Panels, TailBendRad, NoseCrushM, TailCrushM,
        NoseBendRad, EngineDeflectionRad, EngineDrawn, TailBendPitchRad, TailBendYawRad, Blades, BladesPerProp,
        PropellerLost);

    /// <summary>Blade <paramref name="blade"/> of propeller <paramref name="prop"/>, or a straight blade of zero span
    /// when this recording has none (a jet, or a shape taken before the blades were recorded).</summary>
    public PropBladeShape Blade(int prop, int blade)
    {
        if (Blades == null || BladesPerProp <= 0 || blade < 0 || blade >= BladesPerProp) return default;
        int i = prop * BladesPerProp + blade;
        return i >= 0 && i < Blades.Length ? Blades[i] : default;
    }

    /// <summary>How many propellers this shape holds blades for.</summary>
    public int PropCount => Blades != null && BladesPerProp > 0 ? Blades.Length / BladesPerProp : 0;

    public static AirframeShape Capture(double time, Aircraft a)
    {
        IReadOnlyList<WingPanel> panels = a.Panels;
        var sets = new PanelSet[panels.Count];
        for (int i = 0; i < panels.Count; i++)
        {
            WingPanel p = panels[i];
            sets[i] = new PanelSet(p.Component, p.BendRad, p.TwistRad, p.State, p.FoldRad);
        }
        OverstressMember em = a.Structure.EngineMount;
        bool drawn = em != null && (em.Overstressed || System.Math.Abs(em.SetRad) >= WingStructure.VisibleSetRad);
        // The propellers. One per engine, and one for a single-engine type, whose Engines list is empty — the same count
        // AircraftContactDamage flattens its blades by, and the same index AirframeVisual's hubs carry (PropHub.Engine).
        bool airscrew = a.Config.Propulsion != null && a.Config.Propulsion.PropDiameterM > 0.0;
        int props = airscrew ? System.Math.Max(1, a.Config.Engines?.Count ?? 1) : 0;
        int n = props > 0 ? a.PropBladeCount : 0;
        PropBladeShape[] blades = props > 0 ? new PropBladeShape[props * n] : System.Array.Empty<PropBladeShape>();
        for (int i = 0; i < props; i++)
            for (int k = 0; k < n; k++)
                blades[i * n + k] = new PropBladeShape(a.PropBladeRadiusM(i, k), a.PropBladeAffectedM(i, k),
                    a.PropBladeCurl(i, k), a.PropBladeBroken(i, k));
        return new AirframeShape(time, sets, a.TailBendRad, a.Crush.NoseCrushM, a.Crush.TailCrushM,
            a.Crush.NoseBendRad, em?.DeflectionRad ?? 0.0, drawn, a.Crush.TailBendPitchRad, a.Crush.TailBendYawRad,
            blades, n, a.IsLost(AirframeComponent.Propeller));
    }

    /// <summary>The engine's droop as StructuralDamage draws it: the mount's deflection once it is drawn at all, plus
    /// whatever the nose has been crushed back by. One formula, in one place.</summary>
    public double EngineSagRad => (EngineDrawn ? EngineDeflectionRad : 0.0) + NoseBendRad;

    /// <summary>Same shape, ignoring the clock — what decides whether a new instant is worth recording.</summary>
    public bool SameShapeAs(in AirframeShape o)
    {
        if (Panels.Length != o.Panels.Length || EngineDrawn != o.EngineDrawn) return false;
        for (int i = 0; i < Panels.Length; i++) if (!Panels[i].SameAs(o.Panels[i])) return false;
        if (PropellerLost != o.PropellerLost || BladesPerProp != o.BladesPerProp
            || (Blades?.Length ?? 0) != (o.Blades?.Length ?? 0)) return false;
        for (int i = 0; i < (Blades?.Length ?? 0); i++) if (!Blades![i].SameAs(o.Blades![i])) return false;
        return System.Math.Abs(TailBendRad - o.TailBendRad) < PanelSet.TolRad
            && System.Math.Abs(TailBendPitchRad - o.TailBendPitchRad) < PanelSet.TolRad
            && System.Math.Abs(TailBendYawRad - o.TailBendYawRad) < PanelSet.TolRad
            && System.Math.Abs(NoseBendRad - o.NoseBendRad) < PanelSet.TolRad
            && System.Math.Abs(EngineDeflectionRad - o.EngineDeflectionRad) < PanelSet.TolRad
            && System.Math.Abs(NoseCrushM - o.NoseCrushM) < 0.005
            && System.Math.Abs(TailCrushM - o.TailCrushM) < 0.005;
    }
}

/// <summary>
/// WHEN the airframe took each shape, so a replay can draw the aeroplane as it was at the moment being watched rather
/// than as it ended up (Ty 2026-11-08: "The wing tip was still bent up on replay after a wing strike so that is not
/// fixed yet"; Ty 2026-11-10: "Replay shows damaged propeller that is not turning and bent wings prior to the crash").
///
/// The PROPELLER is in here for the same reason and was the second half of the 11-10 report: its blades were drawn from
/// the live per-blade damage (Core PropBladeDamage), so a replay opened on the curled or shattered prop the crash left
/// and the hub it was folded onto no longer turned. Blade damage is permanent, changes only at a strike, and is four
/// numbers a blade — the same four the renderer draws from — so it belongs with the bends rather than in a channel of
/// its own.
///
/// This is the other half of the family that <see cref="ReplayDamageTimeline"/> and the part tracks belong to. The
/// pieces that came OFF are posed as transforms; the damage left in the aeroplane's own meshes is a continuous shape —
/// a bend angle, a twist, a crush depth — and a replay that does not restore it draws a wing that was broken by an
/// impact the viewer has not reached yet.
///
/// Only PLASTIC deformation is kept: the set the airframe has taken and will not give back. The elastic flex under g
/// belongs to the load of the moment, is a few milliradians, and is not recoverable from a recorded pose; a replay
/// draws the airframe unflexed and nobody can see the difference.
///
/// Shape changes only at a damage event, so the timeline holds one entry per NEW shape rather than one per frame — a
/// whole flight that never touches anything is a single entry.
/// </summary>
public sealed class ReplayShapeTimeline
{
    private readonly List<AirframeShape> _shapes = new();

    public int Count => _shapes.Count;
    public AirframeShape At(int i) => _shapes[i];

    /// <summary>Note the airframe's shape at <paramref name="t"/>. Returns true if this was a NEW shape and was
    /// therefore kept.</summary>
    public bool Record(double t, Aircraft a)
    {
        AirframeShape now = AirframeShape.Capture(t, a);
        if (_shapes.Count > 0 && _shapes[_shapes.Count - 1].SameShapeAs(now)) return false;
        _shapes.Add(now);
        return true;
    }

    /// <summary>The shape the airframe was in at <paramref name="t"/>: the last one it took at or before then. Before
    /// the first entry it is the earliest shape known, as <see cref="FlightRecorder.Sample"/> clamps to its nearest
    /// end. False only when nothing has been recorded at all.</summary>
    public bool TryShapeAt(double t, out AirframeShape shape)
    {
        if (_shapes.Count == 0) { shape = default; return false; }
        int lo = 0, hi = _shapes.Count - 1, found = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_shapes[mid].Time <= t) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        shape = _shapes[found];
        return true;
    }

    /// <summary>The recording's ring has dropped everything before <paramref name="startT"/>. The shape in force at
    /// that moment must survive — drop it and the replay draws an aeroplane that was mended minutes ago — so the last
    /// entry at or before it is carried forward to the new start and the rest go.</summary>
    public void TrimBefore(double startT)
    {
        int keep = -1;
        for (int i = 0; i < _shapes.Count; i++) if (_shapes[i].Time <= startT) keep = i; else break;
        if (keep < 0) return;
        AirframeShape carried = _shapes[keep];
        _shapes.RemoveRange(0, keep);
        _shapes[0] = carried.At(startT);
    }

    /// <summary>A fresh aeroplane: no shape yet.</summary>
    public void Clear() => _shapes.Clear();
}

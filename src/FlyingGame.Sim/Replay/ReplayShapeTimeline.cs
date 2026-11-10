using System.Collections.Generic;
using FlyingGame.Core;

namespace FlyingGame.Sim.Replay;

/// <summary>One wing panel's PERMANENT shape: the plastic set the airframe has kept, not the flex it happens to be
/// flying under.</summary>
public readonly struct PanelSet
{
    public readonly AirframeComponent Component;
    public readonly double BendRad, TwistRad;
    public readonly PanelState State;

    /// <summary>Half the angle the renderer can see (WingStructure.VisibleSetRad is 0.5°): below this a change is not
    /// worth a new entry in the timeline.</summary>
    public const double TolRad = 0.25 * System.Math.PI / 180.0;

    public PanelSet(AirframeComponent component, double bendRad, double twistRad, PanelState state)
    {
        Component = component; BendRad = bendRad; TwistRad = twistRad; State = state;
    }

    public bool SameAs(in PanelSet o) =>
        Component == o.Component && State == o.State
        && System.Math.Abs(BendRad - o.BendRad) < TolRad && System.Math.Abs(TwistRad - o.TwistRad) < TolRad;
}

/// <summary>
/// The shape of the airframe at one instant: every panel's set, the tail's bend, the fuselage's crush and the engine
/// mount's droop. Enough to draw the aeroplane as it was, and nothing else.
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

    public AirframeShape(double time, PanelSet[] panels, double tailBendRad, double noseCrushM, double tailCrushM,
        double noseBendRad, double engineDeflectionRad, bool engineDrawn, double tailBendPitchRad, double tailBendYawRad)
    {
        Time = time; Panels = panels; TailBendRad = tailBendRad; NoseCrushM = noseCrushM; TailCrushM = tailCrushM;
        NoseBendRad = noseBendRad; EngineDeflectionRad = engineDeflectionRad; EngineDrawn = engineDrawn;
        TailBendPitchRad = tailBendPitchRad; TailBendYawRad = tailBendYawRad;
    }

    public static AirframeShape Capture(double time, Aircraft a)
    {
        IReadOnlyList<WingPanel> panels = a.Panels;
        var sets = new PanelSet[panels.Count];
        for (int i = 0; i < panels.Count; i++)
        {
            WingPanel p = panels[i];
            sets[i] = new PanelSet(p.Component, p.BendRad, p.TwistRad, p.State);
        }
        OverstressMember em = a.Structure.EngineMount;
        bool drawn = em != null && (em.Overstressed || System.Math.Abs(em.SetRad) >= WingStructure.VisibleSetRad);
        return new AirframeShape(time, sets, a.TailBendRad, a.Crush.NoseCrushM, a.Crush.TailCrushM,
            a.Crush.NoseBendRad, em?.DeflectionRad ?? 0.0, drawn, a.Crush.TailBendPitchRad, a.Crush.TailBendYawRad);
    }

    /// <summary>The engine's droop as StructuralDamage draws it: the mount's deflection once it is drawn at all, plus
    /// whatever the nose has been crushed back by. One formula, in one place.</summary>
    public double EngineSagRad => (EngineDrawn ? EngineDeflectionRad : 0.0) + NoseBendRad;

    /// <summary>Same shape, ignoring the clock — what decides whether a new instant is worth recording.</summary>
    public bool SameShapeAs(in AirframeShape o)
    {
        if (Panels.Length != o.Panels.Length || EngineDrawn != o.EngineDrawn) return false;
        for (int i = 0; i < Panels.Length; i++) if (!Panels[i].SameAs(o.Panels[i])) return false;
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
/// fixed yet").
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
        _shapes[0] = new AirframeShape(startT, carried.Panels, carried.TailBendRad, carried.NoseCrushM,
            carried.TailCrushM, carried.NoseBendRad, carried.EngineDeflectionRad, carried.EngineDrawn,
            carried.TailBendPitchRad, carried.TailBendYawRad);
    }

    /// <summary>A fresh aeroplane: no shape yet.</summary>
    public void Clear() => _shapes.Clear();
}

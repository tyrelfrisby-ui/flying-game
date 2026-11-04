using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>
/// The water's hold on the aeroplane itself — what is still attached of it. Replaces the old ditching contact (a
/// spring at a tenth of the ground's stiffness and three times its damping, which made an aeroplane in the sea sit
/// at a fixed depth for ever) with buoyancy from the airframe's own volumes, water drag and added mass, and
/// flooding through its openings, so an intact ditching floats and then slowly sinks as it fills.
///
/// Aircraft drives it: <see cref="Prepare"/> before the RK4 step (one water query at the CG; inactive unless the
/// airframe is within reach of a water surface), <see cref="Forces"/> inside it, <see cref="AfterStep"/> after it.
/// Gear legs are excluded from the forces (WheelHydro carries a wheel in water); a flying boat's fuselage is
/// excluded (its hull is FloatHydro's).
///
/// Mass: when a part breaks off, the aeroplane's rigid body sheds exactly the elements <see cref="Detach"/> hands to
/// the wreck, and its CG moves to what is left (Aircraft.LoseComponent; decision for Ty in AGENT-LOG NEEDS YOU).
/// </summary>
public sealed class AirframeFlooding
{
    public AirframeHydroModel Model { get; }
    private readonly List<HydroElement> _attached;
    private readonly List<HydroElement> _forceEls = new();
    private readonly Dictionary<AirframeComponent, List<HydroElement>> _detached = new();
    private readonly Vec3 _cg;
    private readonly bool _hullIsFloat;
    private double _reach;
    private WaterCoeffs _old = WaterCoeffs.None;

    /// <summary>Surface height under the CG at the last <see cref="Prepare"/>, m up; NaN if none.</summary>
    public double WaterUp { get; private set; } = double.NaN;
    /// <summary>True when the airframe is within reach of the water this step.</summary>
    public bool Active { get; private set; }
    /// <summary>The airframe has touched water at some point (it has a flood state worth reporting).</summary>
    public bool EverWet { get; private set; }
    public IReadOnlyList<HydroElement> Attached => _attached;

    public AirframeFlooding(AircraftConfig c, AircraftLoad? load = null)
    {
        Model = AirframeHydroModel.Build(c, load);
        _attached = new List<HydroElement>(Model.Elements);
        _cg = c.Mass.CgVec();
        _hullIsFloat = c.Floats is { Count: 1 };
        RebuildForceSet();
    }

    private void RebuildForceSet()
    {
        _forceEls.Clear();
        _reach = 0;
        foreach (HydroElement e in _attached)
        {
            if (WreckAtoms.IsGear(e.Atom)) continue;
            if (_hullIsFloat && e.Atom is WreckAtom.Cabin or WreckAtom.Nose or WreckAtom.TailCone) continue;
            _forceEls.Add(e);
            _reach = System.Math.Max(_reach, (e.Centre - _cg).Length + e.Half.Length);
        }
    }

    /// <summary>Arms the step if any element could be wet, from the water surface under the CG (null = none).</summary>
    public void Prepare(RigidBodyState s, double? w)
    {
        WaterUp = w ?? double.NaN;
        Active = w.HasValue && (-s.Position.Z) - w.Value < _reach + 0.5;
        if (Active) EverWet = true;
        _old = Active ? HydroBody.Coefficients(_forceEls, _cg, s, WaterUp, airDrag: false) : WaterCoeffs.None;
    }

    /// <summary>Buoyancy and floodwater weight, body frame, about the CG (zero when inactive).</summary>
    public (Vec3 Force, Vec3 Moment) Forces(RigidBodyState s) =>
        Active ? HydroBody.Hydrostatics(_forceEls, _cg, s, WaterUp) : (Vec3.Zero, Vec3.Zero);

    /// <summary>Implicit water drag and added mass on the step just integrated, then crash damage (skin, glazing, doors,
    /// and joints that the water has loaded past their strength — <see cref="TakeBreaks"/>), then flooding.</summary>
    public RigidBodyState AfterStep(RigidBodyState before, RigidBodyState after, MassProperties mp, double dt)
    {
        RigidBodyState s = after;
        if (Active)
        {
            WaterCoeffs now = HydroBody.Coefficients(_forceEls, _cg, after, WaterUp, airDrag: false);
            s = HydroBody.Correct(before, after, mp.MassKg, new Vec3(mp.Ixx, mp.Iyy, mp.Izz), _old, now, dt);
            List<Panel>? broke = WaterSlam.Damage(_forceEls, _cg, s, WaterUp);
            if (broke != null) foreach (Panel p in broke) DamageLog.Add(p.Why);
            CheckJoints(before, s, dt);
        }
        if (EverWet)
            foreach (HydroElement e in _attached) e.Flood(_cg, s, Active ? WaterUp : double.NaN, dt);
        return s;
    }

    /// <summary>What has broken open so far, in order (one entry per break or growth step).</summary>
    public readonly List<string> DamageLog = new();
    private readonly List<AirframeComponent> _breaks = new();

    /// <summary>Components whose joint the water has broken since the last call (the aircraft breaks them off).</summary>
    public List<AirframeComponent> TakeBreaks()
    {
        var l = new List<AirframeComponent>(_breaks);
        _breaks.Clear();
        return l;
    }

    /// <summary>
    /// The water break-up rule (decision 1 in AGENT-LOG): on a step where any of the airframe is in the water, each
    /// joint carries what it must to make the parts beyond it move as they did — Σ (r_e − r_joint) × [m_e·(a_e − g) −
    /// F_water,e], a_e each element's inertial acceleration over the step (finite difference of its velocity plus
    /// ω × v) and F_water,e its own water force (<see cref="WaterSlam.ElementForce"/>). When the bending part (about
    /// the axes across the joint; for a wing only the flapwise moment, about x) exceeds the joint's ultimate moment, or
    /// the force along a fuselage joint exceeds its ultimate axial load (<see cref="Joint"/>), the component breaks off.
    /// </summary>
    private void CheckJoints(RigidBodyState s0, RigidBodyState s1, double dt)
    {
        if (Model.Joints.Count == 0 || dt <= 0) return;
        bool wet = false;
        foreach (HydroElement e in _forceEls)
        {
            double depth = s1.Position.Z + s1.Attitude.Rotate(e.Centre - _cg).Z + WaterUp;
            if (HydroElement.SubmergedFraction(depth, e.VerticalHalf(s1.Attitude)) > 0) { wet = true; break; }
        }
        if (!wet) return;
        Vec3 gB = s1.Attitude.Conjugate().Rotate(new Vec3(0, 0, WaterPhysics.G));
        var force = new HashSet<HydroElement>(_forceEls);
        foreach (Joint j in Model.Joints)
        {
            if (_breaks.Contains(j.Component)) continue;
            Vec3 mom = Vec3.Zero;
            double axial = 0;
            bool any = false;
            foreach (HydroElement e in _attached)
            {
                if (!j.Atoms.Contains(e.Atom)) continue;
                any = true;
                Vec3 rb = e.Centre - _cg;
                Vec3 v0 = s0.Velocity + Vec3.Cross(s0.Rates, rb), v1 = s1.Velocity + Vec3.Cross(s1.Rates, rb);
                Vec3 a = (v1 - v0) / dt + Vec3.Cross(s1.Rates, v1);
                Vec3 fw = force.Contains(e) ? WaterSlam.ElementForce(e, _cg, s0, s1, WaterUp, dt) : Vec3.Zero;
                Vec3 fj = (a - gB) * e.MassKg - fw;
                mom += Vec3.Cross(e.Centre - j.At, fj);
                axial += Vec3.Dot(fj, j.Axis);
            }
            if (!any) continue;
            double bend = j.Axis.Y > 0.5 ? System.Math.Abs(mom.X) : System.Math.Sqrt(mom.Y * mom.Y + mom.Z * mom.Z);
            if (bend > j.PeakNm) j.PeakNm = bend;
            if (System.Math.Abs(axial) > j.PeakAxialN) j.PeakAxialN = System.Math.Abs(axial);
            if (bend > j.UltimateNm)
            {
                _breaks.Add(j.Component);
                DamageLog.Add($"{j.Component} broken off in the water: {bend / 1000:F1} kN·m against {j.UltimateNm / 1000:F1}");
            }
            else if (j.UltimateAxialN > 0 && System.Math.Abs(axial) > j.UltimateAxialN)
            {
                _breaks.Add(j.Component);
                DamageLog.Add($"{j.Component} broken off in the water: {System.Math.Abs(axial) / 1000:F1} kN along the fuselage against {j.UltimateAxialN / 1000:F1}");
            }
        }
    }

    /// <summary>Area the crash has broken open on the attached airframe, m^2.</summary>
    public double CrashOpeningM2 => WaterSlam.BrokenAreaM2(_attached);

    /// <summary>
    /// A component has broken off: its atoms that are still attached leave (returned, and kept for
    /// <see cref="TakeDetached"/>), and every joint torn by it opens on both sides.
    /// </summary>
    public List<HydroElement> Detach(AirframeComponent comp)
    {
        var claimed = new List<HydroElement>();
        var atoms = new HashSet<WreckAtom>(WreckAtoms.Of(comp));
        if (atoms.Count == 0) return claimed;
        for (int i = _attached.Count - 1; i >= 0; i--)
            if (atoms.Contains(_attached[i].Atom)) { claimed.Insert(0, _attached[i]); _attached.RemoveAt(i); }
        if (claimed.Count == 0) return claimed;
        var claimedAtoms = new HashSet<WreckAtom>(claimed.Select(e => e.Atom));
        var attachedAtoms = new HashSet<WreckAtom>(_attached.Select(e => e.Atom));
        foreach (Cut cut in Model.Cuts)
        {
            bool childGoes = claimedAtoms.Contains(cut.Child) && attachedAtoms.Contains(cut.Parent);
            bool parentGoes = claimedAtoms.Contains(cut.Parent) && attachedAtoms.Contains(cut.Child);
            if (!childGoes && !parentGoes) continue;
            List<HydroElement> childSide = childGoes ? claimed : _attached, parentSide = childGoes ? _attached : claimed;
            HydroElement? ce = cut.ChildElement != null ? childSide.FirstOrDefault(e => e.Name == cut.ChildElement) : Nearest(childSide, cut.Child, cut.At);
            if (ce != null) Open(ce, cut);
            if (!cut.OneSided) { HydroElement? pe = Nearest(parentSide, cut.Parent, cut.At); if (pe != null) Open(pe, cut); }
        }
        _detached[comp] = claimed;
        RebuildForceSet();
        return claimed;
    }

    private static void Open(HydroElement e, Cut cut)
    {
        if (cut.HalfHeight <= 0) { e.Openings.Add(new Opening(cut.At, cut.AreaM2, cut.Why)); return; }
        e.Openings.Add(new Opening(new Vec3(cut.At.X, cut.At.Y, cut.At.Z - cut.HalfHeight), cut.AreaM2 * 0.5, cut.Why));
        e.Openings.Add(new Opening(new Vec3(cut.At.X, cut.At.Y, cut.At.Z + cut.HalfHeight), cut.AreaM2 * 0.5, cut.Why));
    }

    private static HydroElement? Nearest(List<HydroElement> els, WreckAtom atom, Vec3 at)
    {
        HydroElement? best = null; double bd = double.MaxValue;
        foreach (HydroElement e in els)
        {
            if (e.Atom != atom || e.Name.StartsWith("tank-") || e.Name.StartsWith("engine")) continue;
            double d = (e.Centre - at).LengthSquared;
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    /// <summary>Elements cut out of the airframe by something other than <see cref="Detach"/> (a wing panel outboard of
    /// its structural station, whose share of the wing's mass Aircraft.ShedStrips carves out): kept for
    /// <see cref="TakeDetached"/>, so the panel becomes a wreck piece of its own like any other part.</summary>
    public void AddDetached(AirframeComponent comp, List<HydroElement> els)
    {
        if (els.Count == 0) return;
        if (_detached.TryGetValue(comp, out List<HydroElement>? had)) had.AddRange(els);
        else _detached[comp] = new List<HydroElement>(els);
    }

    /// <summary>The elements a component took with it when it broke off (once), or null.</summary>
    public List<HydroElement>? TakeDetached(AirframeComponent comp)
    {
        if (!_detached.TryGetValue(comp, out List<HydroElement>? els)) return null;
        _detached.Remove(comp);
        return els;
    }

    /// <summary>Damage from outside (a scenario, a lesson): a hole of <paramref name="areaM2"/> spanning the largest
    /// element of the atom.</summary>
    public bool Damage(WreckAtom atom, double areaM2, string why)
    {
        HydroElement? e = _attached.Where(x => x.Atom == atom && !x.Name.StartsWith("engine")).OrderByDescending(x => x.VoidM3).FirstOrDefault();
        if (e == null) return false;
        e.AddSpanningOpening(e.Centre, areaM2, why);
        return true;
    }

    public double TakeEscapedFreeM3() { double s = 0; foreach (HydroElement e in _attached) s += e.TakeEscaped(); return s; }
    public double TrappedAirM3 { get { double s = 0; foreach (HydroElement e in _forceEls) s += e.AirM3; return s; } }
    public double TrappedAirFreeM3 { get { double s = 0; foreach (HydroElement e in _forceEls) s += e.AirFreeM3; return s; } }
    public double WaterInsideM3 { get { double s = 0; foreach (HydroElement e in _forceEls) s += e.WaterM3; return s; } }
    /// <summary>Envelope less water inside, m^3: what could hold the aeroplane up if it all went under.</summary>
    public double ReserveVolumeM3 { get { double v = 0; foreach (HydroElement e in _forceEls) v += e.EnvelopeM3 - e.WaterM3; return v; } }
    /// <summary>Current buoyancy of the attached airframe, N.</summary>
    public double BuoyancyN(RigidBodyState s)
    {
        if (double.IsNaN(WaterUp)) return 0;
        double b = 0;
        foreach (HydroElement e in _forceEls)
        {
            double depth = s.Position.Z + s.Attitude.Rotate(e.Centre - _cg).Z + WaterUp;
            b += WaterPhysics.RhoWater * WaterPhysics.G * e.EnvelopeM3 * HydroElement.SubmergedFraction(depth, e.VerticalHalf(s.Attitude));
        }
        return b;
    }
}

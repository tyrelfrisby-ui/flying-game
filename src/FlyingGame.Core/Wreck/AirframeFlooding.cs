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
/// Mass: the aeroplane's rigid body keeps its full mass when parts break off (owner's call, Aircraft.LoseComponent),
/// so the remainder floats lower than its own parts would. Logged in AGENT-LOG.md NEEDS YOU.
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

    /// <summary>Implicit water drag and added mass on the step just integrated, then flooding.</summary>
    public RigidBodyState AfterStep(RigidBodyState before, RigidBodyState after, MassProperties mp, double dt)
    {
        RigidBodyState s = after;
        if (Active)
        {
            WaterCoeffs now = HydroBody.Coefficients(_forceEls, _cg, after, WaterUp, airDrag: false);
            s = HydroBody.Correct(before, after, mp.MassKg, new Vec3(mp.Ixx, mp.Iyy, mp.Izz), _old, now, dt);
        }
        if (EverWet)
            foreach (HydroElement e in _attached) e.Flood(_cg, s, Active ? WaterUp : double.NaN, dt);
        return s;
    }

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

using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// The fuselage crushes before it breaks (Ty 2026-11-04; the law is <see cref="FuselageStructure"/>). Every crash source that
/// can take a fuselage section off — a hard point arriving on the ground or a solid, a tree trunk, a water joint — goes
/// through here first. A section that holds is CRUSHED instead: its struck hard point moves into the airframe by the crush
/// (capped at <see cref="FuselageStructure.MaxGeometricCrushM"/>), and a tail boom or engine mount BENDS about its station,
/// the tail surfaces turning with it (their incidence follows the bend, <see cref="Core.Aero.StripFlowState.IncidenceDeltaRad"/>).
/// </summary>
public sealed partial class Aircraft
{
    private FuselageCrush? _crush;
    /// <summary>The fuselage's crush state (sections crushed, bends, energy absorbed).</summary>
    public FuselageCrush Crush => _crush ??= new FuselageCrush(FuselageStructure.Of(Config));
    private readonly Dictionary<ContactPoint, Vec3> _uncrushedBody = new();
    private Quat _tailBend = Quat.Identity, _noseBend = Quat.Identity;

    /// <summary>Crush each struck member (tail surface, mid-span, nacelle, gear leg) has taken, m, and the energy it absorbed.</summary>
    private readonly Dictionary<AirframeComponent, double> _memberCrushM = new();
    /// <summary>Crush taken so far by a member that is not a fuselage section (MemberMaterials.CrushCapacityM), m.</summary>
    public double MemberCrushM(AirframeComponent c) => _memberCrushM.TryGetValue(c, out double d) ? d : 0.0;

    /// <summary>Drop from <see cref="_pendingBreaks"/> every fuselage section, and every other member, that crushes instead
    /// of breaking this step.</summary>
    private void FilterFuselageBreaks()
    {
        for (int i = _pendingBreaks.Count - 1; i >= 0; i--)
        {
            AirframeComponent c = _pendingBreaks[i];
            if (_lost.Contains(c)) continue;
            if (FuselageStructure.IsSection(c) ? !SectionBreaksOnArrival(c) : !MemberBreaksOnArrival(c)) _pendingBreaks.RemoveAt(i);
        }
        for (int i = 0; i < _contacts.Count; i++) _contacts[i].ArrivalPending = false;
        foreach (WheelState w in _wheels) { w.ArrivalMs = 0; w.ArrivalBreakMs = 0; }
    }

    /// <summary>A member that is not a fuselage section, struck over its break speed: it crushes by the fuselage's energy
    /// rule and breaks only when its crush adds up past its capacity. A break with no arrival to go on (the water joints,
    /// a tree, combat) is left as it was.</summary>
    private bool MemberBreaksOnArrival(AirframeComponent c)
    {
        double cap = MemberMaterials.CrushCapacityM(c, Config);
        if (cap <= 0) return true;
        double arrive = 0, breakMs = 0; ContactPoint? hit = null;
        for (int i = 0; i < _contacts.Count; i++)
        {
            ContactPoint p = _contacts[i];
            if (p.ArrivalPending && p.Component == c && p.ArrivalMs > arrive) { arrive = p.ArrivalMs; breakMs = p.BreakSpeedMs; hit = p; }
        }
        if (c is AirframeComponent.GearLeft or AirframeComponent.GearRight or AirframeComponent.GearNose or AirframeComponent.GearTail)
            for (int g = 0; g < _wheels.Length && g < Config.Gear.Count; g++)
                if (_wheels[g].ArrivalBreakMs > 0 && AirframeContact.GearComponent(Config.Gear[g]) == c && _wheels[g].ArrivalMs > arrive)
                { arrive = _wheels[g].ArrivalMs; breakMs = _wheels[g].ArrivalBreakMs; hit = null; }
        if (arrive <= 0 || breakMs <= 0) return true;
        double total = MemberCrushM(c) + FuselageStructure.CrushFrom(arrive, breakMs);
        _memberCrushM[c] = total;
        if (total > cap) return true;
        Vec3 pos = hit?.WorldPos ?? State.Position, vel = hit?.WorldVel ?? State.Attitude.Rotate(State.Velocity);
        Contact?.Invoke(new ContactEvent(ContactEventKind.MemberBent, hit?.Name ?? c.ToString(), c, hit?.SurfaceCode ?? ContactPoint.SurfaceNone, arrive / breakMs, total, pos, vel));
        return false;
    }

    private bool SectionBreaksOnArrival(AirframeComponent c)
    {
        ContactPoint? hit = null;
        for (int i = 0; i < _contacts.Count; i++)
        {
            ContactPoint p = _contacts[i];
            if (p.ArrivalPending && p.Component == c && (hit is null || p.ArrivalMs > hit.ArrivalMs)) hit = p;
        }
        // No arrival: a load-ruled break (the water joints). A steel-tube cabin or tail boom does not part under it either.
        if (hit is null) return Crush.BreaksUnderLoad(c);
        Vec3 r = hit.Body - Config.Mass.CgVec();
        double mEff = EffectiveMass(r, hit.ArrivalNormalBody);
        if (Crush.Absorb(c, hit.ArrivalMs, hit.BreakSpeedMs, mEff, out double crush)) return true;
        CrushAt(c, hit, hit.ArrivalNormalBody, crush, mEff, hit.ArrivalMs, hit.BreakSpeedMs);
        return false;
    }

    /// <summary>Effective mass of the airframe at body point r (from P) along body direction n.</summary>
    private double EffectiveMass(Vec3 r, Vec3 n)
    {
        MassProperties mp = MassProperties;
        double inv = 1.0 / mp.MassKg + Vec3.Dot(Vec3.Cross(mp.Solve(Vec3.Cross(r, n)), r), n);
        return inv > 0 ? 1.0 / inv : mp.MassKg;
    }

    /// <summary>
    /// A section has crushed by <paramref name="crushM"/> where <paramref name="at"/> (a hard point, or null for a tree edge)
    /// was struck along <paramref name="normalBody"/> (the outward normal of what it struck, body frame): move the point in, bend
    /// a tail boom or an engine mount, and tell the listeners.
    /// </summary>
    private void CrushAt(AirframeComponent section, ContactPoint? at, Vec3 normalBody, double crushM, double mEff, double closingMs, double breakMs)
    {
        if (crushM <= 0) return;
        Vec3 n = normalBody.LengthSquared > 1e-12 ? normalBody.Normalized() : new Vec3(0, 0, -1);
        // The cabin's struck point moves in; a tail boom or an engine mount bends instead (its points turn with it).
        if (at is not null && section == AirframeComponent.Cabin)
        {
            if (!_uncrushedBody.TryGetValue(at, out Vec3 orig)) _uncrushedBody[at] = orig = at.Body;
            Vec3 moved = at.Body + n * crushM - orig;
            double len = moved.Length;
            if (len > FuselageStructure.MaxGeometricCrushM) moved *= FuselageStructure.MaxGeometricCrushM / len;
            at.Body = orig + moved;
        }
        (double noseCut, double tailCut) = AirframeContact.FuselageStations(Config);
        if (section == AirframeComponent.TailBoom) Bend(ref _tailBend, new Vec3(tailCut, 0, 0), TailEnd(), n, crushM, IsTailPoint);
        else if (section == AirframeComponent.Nose) Bend(ref _noseBend, new Vec3(noseCut, 0, 0), NoseEnd(), n, crushM, p => p.Component == AirframeComponent.Nose);
        Vec3 pos = at?.WorldPos ?? State.Position, vel = at?.WorldVel ?? State.Attitude.Rotate(State.Velocity);
        Contact?.Invoke(new ContactEvent(ContactEventKind.FuselageCrushed, at?.Name ?? section.ToString(), section, at?.SurfaceCode ?? ContactPoint.SurfaceNone,
            0.5 * mEff * Math.Max(0.0, closingMs * closingMs - breakMs * breakMs), crushM, pos, vel));
    }

    private static bool IsTailPoint(ContactPoint p) => p.Component is AirframeComponent.TailBoom or AirframeComponent.TailHorizontal or AirframeComponent.TailVertical
        or AirframeComponent.Rudder or AirframeComponent.ElevatorLeft or AirframeComponent.ElevatorRight;

    private Vec3 TailEnd()
    {
        foreach (ContactPoint p in _contacts) if (p.Name == "tail-cone") return _uncrushedBody.TryGetValue(p, out Vec3 o) ? o : p.Body;
        (_, double tailCut) = AirframeContact.FuselageStations(Config);
        return new Vec3(tailCut - 2.0, 0, 0);
    }

    private Vec3 NoseEnd()
    {
        foreach (ContactPoint p in _contacts) if (p.Name == "nose") return _uncrushedBody.TryGetValue(p, out Vec3 o) ? o : p.Body;
        (double noseCut, _) = AirframeContact.FuselageStations(Config);
        return new Vec3(noseCut + 1.0, 0, 0);
    }

    /// <summary>Turn the section about its station so its end moves <paramref name="crushM"/> along <paramref name="push"/>
    /// (the part of it across the arm), capped at <see cref="FuselageStructure.MaxBendRad"/> in all.</summary>
    private void Bend(ref Quat total, Vec3 pivot, Vec3 end, Vec3 push, double crushM, Func<ContactPoint, bool> moves)
    {
        Vec3 arm = end - pivot;
        double L = arm.Length;
        if (L < 0.2) return;
        Vec3 u = arm / L;
        Vec3 across = push - u * Vec3.Dot(push, u);
        if (across.Length < 1e-6) return;
        Vec3 axis = Vec3.Cross(u, across).Normalized();
        double angle = Math.Atan(crushM * across.Length / L);
        Quat step = new Quat(axis.X * Math.Sin(angle / 2), axis.Y * Math.Sin(angle / 2), axis.Z * Math.Sin(angle / 2), Math.Cos(angle / 2));
        Quat next = Quat.Multiply(step, total).Normalized();
        double turned = 2 * Math.Acos(Math.Clamp(Math.Abs(next.W), 0.0, 1.0));
        if (turned > FuselageStructure.MaxBendRad) return;   // folded as far as it goes: the rest is crush, not more bend
        total = next;
        foreach (ContactPoint p in _contacts)
        {
            if (!moves(p)) continue;
            p.Body = pivot + step.Rotate(p.Body - pivot);
        }
        UpdateBendState();
    }

    private void UpdateBendState()
    {
        Vec3 tailDir = _tailBend.Rotate(new Vec3(-1, 0, 0)), noseDir = _noseBend.Rotate(new Vec3(1, 0, 0));
        Crush.SetBends(Math.Atan2(-tailDir.Z, -tailDir.X), Math.Atan2(tailDir.Y, -tailDir.X), Math.Atan2(noseDir.Z, noseDir.X));
        // The tail surfaces turn with the boom: the stab's chord (forward = body +x turned by the bend) points LE-down when
        // the tail is bent up — less incidence; the fin's trailing edge goes the way the tail went (+ = TE right).
        Vec3 chord = _tailBend.Rotate(new Vec3(1, 0, 0));
        double dStab = Math.Atan2(-chord.Z, chord.X), dFin = Math.Atan2(-chord.Y, chord.X);
        if (Math.Abs(dStab) < 1e-9 && Math.Abs(dFin) < 1e-9) return;
        int total = 0; foreach (Core.DataContracts.SurfaceConfig sf in Config.Surfaces) total += sf.Strips.Count;
        double[] inc = _flowState.IncidenceDeltaRad is { } a && a.Length == total ? a : (_flowState.IncidenceDeltaRad = new double[total]);
        int idx = 0;
        foreach (Core.DataContracts.SurfaceConfig sf in Config.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool hTail = id == "hstab" || id == "elevator", vTail = id.Contains("vstab") || id.StartsWith("rudder") || id.Contains("fin");
            // Only the tail's rows: a bent wing panel's twist is in the same array (AircraftContactDamage.SetBend).
            foreach (Core.DataContracts.StripConfig _ in sf.Strips) { if (hTail) inc[idx] = dStab; else if (vTail) inc[idx] = dFin; idx++; }
        }
    }

    /// <summary>A tree edge on a fuselage section has crushed it (AircraftTreeStrike).</summary>
    private void CrushFromTree(AirframeComponent section, Vec3 normalBody, double crushM, double mEff, double closingMs, double breakMs) =>
        CrushAt(section, null, normalBody, crushM, mEff, closingMs, breakMs);
}

using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>
/// What a water impact does to the structure, on top of what it does to the motion (HydroBody):
///
///  - <see cref="Damage"/>: every <see cref="Panel"/> (skin face, windshield, canopy, door) that is wet and pressed by
///    more than it can carry breaks open, and the hole joins the element's openings — the orifice flooding takes it
///    from there. The pressure is DNV's slam pressure ½ρ·Cs·v_n² while the face is going in and the stagnation
///    pressure ½ρ·v_n² once it is under (<see cref="WaterPhysics.FacePressurePa"/>), v_n the face's own speed along
///    its outward normal ("going in" = its element is not yet all under). A skin face bursts over the fraction of it
///    that is wet while the pressure is above its strength: a face meeting the water at an angle tears as far as it
///    has gone in before the pressure falls below its strength — the faster the arrival, the bigger the hole — and a
///    face that hits flat goes all at once. A hatch opens whole.
///  - <see cref="ElementForce"/>: the water's force on one element over a step — von Kármán's momentum theory per
///    element, F = −d(m_a·v)/dt, plus its drag and its hydrostatics — for the joint loads (AirframeFlooding).
/// </summary>
public static class WaterSlam
{
    /// <summary>Depth (+ under) of a body-frame point.</summary>
    private static double Depth(Vec3 body, Vec3 cg, RigidBodyState s, double waterUp) =>
        (s.Position + s.Attitude.Rotate(body - cg)).Z + waterUp;

    /// <summary>Fraction of a panel under water (by the vertical extent of its four corners: 0 dry, 1 all under).</summary>
    public static double WetFraction(Panel p, Vec3 cg, RigidBodyState s, double waterUp)
    {
        double dMin = double.PositiveInfinity, dMax = double.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vec3 c = p.At + ((i & 1) == 0 ? p.U : -1.0 * p.U) + ((i & 2) == 0 ? p.V : -1.0 * p.V);
            double d = Depth(c, cg, s, waterUp);
            dMin = System.Math.Min(dMin, d); dMax = System.Math.Max(dMax, d);
        }
        if (dMax <= 0) return 0;
        if (dMin >= 0) return 1;
        return dMax / (dMax - dMin);
    }

    /// <summary>
    /// Break open whatever the water pressure is breaking this step. Returns the panels that broke (or grew) — for
    /// the probe; the flooding sees the holes through the elements' openings.
    /// </summary>
    public static List<Panel>? Damage(IEnumerable<HydroElement> els, Vec3 cg, RigidBodyState s, double waterUp)
        => Damage(els, cg, s, waterUp, WaterCurrent.Local.None);

    /// <param name="flow">The water's own motion (<see cref="WaterCurrent"/>): a face's speed is its speed through the water.</param>
    public static List<Panel>? Damage(IEnumerable<HydroElement> els, Vec3 cg, RigidBodyState s, double waterUp, in WaterCurrent.Local flow)
    {
        if (double.IsNaN(waterUp)) return null;
        List<Panel>? broke = null;
        foreach (HydroElement e in els)
        {
            if (e.Panels.Count == 0) continue;
            // The element is still going in (slam) until it is all under; then the water flows past it (stagnation).
            bool entering = HydroElement.SubmergedFraction(Depth(e.Centre, cg, s, waterUp), e.VerticalHalf(s.Attitude)) < 1;
            foreach (Panel p in e.Panels)
            {
                if (!p.Skin && p.Broken >= 1) continue;
                double wet = WetFraction(p, cg, s, waterUp);
                if (wet <= 0 || (p.Skin && wet <= p.Broken)) continue;
                Vec3 v = s.Velocity + Vec3.Cross(s.Rates, p.At - cg);
                if (flow.Moving) v -= s.Attitude.Conjugate().Rotate(flow.At(s.Position + s.Attitude.Rotate(p.At - cg), System.Math.Max(0.0, Depth(p.At, cg, s, waterUp))));
                double pa = WaterPhysics.FacePressurePa(Vec3.Dot(v, p.Normal), entering);
                if (pa > p.PeakPa) p.PeakPa = pa;
                if (pa <= p.FailPa) continue;
                p.Broken = p.Skin ? wet : 1.0;
                Open(e, p);
                (broke ??= new List<Panel>()).Add(p);
                foreach (Panel q in p.Linked)
                    if (q.Broken < 1 && q.Owner != null) { q.Broken = 1; Open(q.Owner, q); broke.Add(q); }
            }
        }
        return broke;
    }

    private static void Open(HydroElement e, Panel p)
    {
        if (p.Holes.Count == 0)
        {
            IReadOnlyList<Vec3> at = p.HoleAt.Count > 0 ? p.HoleAt : new[] { p.At };
            foreach (Vec3 a in at) { var o = new Opening(a, 0, p.Why); p.Holes.Add(o); e.Openings.Add(o); }
        }
        double each = p.AreaM2 * p.Broken / p.Holes.Count;
        foreach (Opening o in p.Holes) o.AreaM2 = each;
    }

    /// <summary>Area broken open on these elements, m^2.</summary>
    public static double BrokenAreaM2(IEnumerable<HydroElement> els)
    {
        double a = 0;
        foreach (HydroElement e in els) foreach (Panel p in e.Panels) a += p.AreaM2 * p.Broken;
        return a;
    }

    /// <summary>
    /// The water's force on one element over the step from <paramref name="s0"/> to <paramref name="s1"/>, body frame
    /// (of <paramref name="s1"/>), N: −[m_a,1·(v1 − v0) + max(0, m_a,1 − m_a,0)·v0]/dt per body axis (von Kármán: the
    /// momentum handed to the water it sets moving; none is handed back as it leaves, as in HydroBody.Correct), minus
    /// its quadratic drag, plus its buoyancy less the weight of the water inside it.
    /// </summary>
    public static Vec3 ElementForce(HydroElement e, Vec3 cg, RigidBodyState s0, RigidBodyState s1, double waterUp, double dt)
        => ElementForce(e, cg, s0, s1, waterUp, dt, WaterCurrent.Local.None);

    /// <param name="flow">The water's own motion: the momentum and the drag are the element's relative to the water.</param>
    public static Vec3 ElementForce(HydroElement e, Vec3 cg, RigidBodyState s0, RigidBodyState s1, double waterUp, double dt, in WaterCurrent.Local flow)
    {
        if (double.IsNaN(waterUp) || e.NoDrag) return Vec3.Zero;
        Vec3 rb = e.Centre - cg;
        double Sub(RigidBodyState s) => HydroElement.SubmergedFraction(Depth(e.Centre, cg, s, waterUp), e.VerticalHalf(s.Attitude));
        double sub0 = Sub(s0), sub1 = Sub(s1);
        if (sub1 <= 0 && sub0 <= 0) return Vec3.Zero;
        Vec3 v0 = s0.Velocity + Vec3.Cross(s0.Rates, rb), v1 = s1.Velocity + Vec3.Cross(s1.Rates, rb);
        if (flow.Moving)
        {
            v0 -= s0.Attitude.Conjugate().Rotate(flow.At(s0.Position + s0.Attitude.Rotate(rb), System.Math.Max(0.0, Depth(e.Centre, cg, s0, waterUp))));
            v1 -= s1.Attitude.Conjugate().Rotate(flow.At(s1.Position + s1.Attitude.Rotate(rb), System.Math.Max(0.0, Depth(e.Centre, cg, s1, waterUp))));
        }
        double Axis(int k, double a0, double a1)
        {
            double ma0 = e.AddedMass(k) * sub0, ma1 = e.AddedMass(k) * sub1;
            double dmv = ma1 * (a1 - a0) + System.Math.Max(0.0, ma1 - ma0) * a0;
            return -dmv / dt - e.DragConstant(k, WaterPhysics.RhoWater) * sub1 * System.Math.Abs(a1) * a1;
        }
        var f = new Vec3(Axis(0, v0.X, v1.X), Axis(1, v0.Y, v1.Y), Axis(2, v0.Z, v1.Z));
        // Hydrostatics (world up = −z): buoyancy of the submerged envelope less the weight of the water inside.
        double up = (e.BuoyancyElsewhere ? 0 : WaterPhysics.RhoWater * WaterPhysics.G * e.EnvelopeM3 * sub1)
                    - WaterPhysics.RhoWater * WaterPhysics.G * e.WaterM3;
        f += s1.Attitude.Conjugate().Rotate(new Vec3(0, 0, -up));
        return f;
    }
}

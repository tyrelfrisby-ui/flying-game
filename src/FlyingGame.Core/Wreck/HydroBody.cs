using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>Water's per-axis added mass and implicit damping for one body at one instant (body axes).</summary>
public readonly struct WaterCoeffs
{
    /// <summary>Added mass along body x, y, z, kg.</summary>
    public readonly Vec3 AddedMass;
    /// <summary>Linearised quadratic drag along body x, y, z: F = −C·v, N per m/s (C = Σ 0.5ρCdA·|v_element|).</summary>
    public readonly Vec3 Damping;
    /// <summary>Linearised rotational drag about body x, y, z: M = −D·ω, N·m per rad/s.</summary>
    public readonly Vec3 RotDamping;
    /// <summary>What the moving water does to a body at rest over the ground: Σ C_e·v_water,e along body x, y, z, N
    /// (<see cref="WaterCurrent"/>). With it the drag is F = −C·v + FlowForce, i.e. −C·(v − v_water).</summary>
    public readonly Vec3 FlowForce;
    /// <summary>The moment of the water's SHEAR about the CG, N·m, body axes: the drag of each element's current less the
    /// current at the CG, at its own lever. A uniform current turns nothing (the still-water model, moved along with
    /// the water); a current faster at one end than the other turns the body.</summary>
    public readonly Vec3 FlowMoment;
    /// <summary>Σ m_a,e·v_water,e along body x, y, z, kg·m/s: the momentum the entrained water had before the body took it up.</summary>
    public readonly Vec3 AddedMassFlow;
    /// <summary>Added moment of inertia about body x, y, z, kg·m²: each element's added mass at its lever from the CG
    /// (the same levers as <see cref="RotDamping"/>).</summary>
    public readonly Vec3 AddedInertia;
    public WaterCoeffs(Vec3 addedMass, Vec3 damping, Vec3 rotDamping) : this(addedMass, damping, rotDamping, Vec3.Zero, Vec3.Zero, Vec3.Zero) { }
    public WaterCoeffs(Vec3 addedMass, Vec3 damping, Vec3 rotDamping, Vec3 flowForce, Vec3 flowMoment, Vec3 addedMassFlow)
        : this(addedMass, damping, rotDamping, flowForce, flowMoment, addedMassFlow, Vec3.Zero) { }
    public WaterCoeffs(Vec3 addedMass, Vec3 damping, Vec3 rotDamping, Vec3 flowForce, Vec3 flowMoment, Vec3 addedMassFlow, Vec3 addedInertia)
    {
        AddedMass = addedMass; Damping = damping; RotDamping = rotDamping;
        FlowForce = flowForce; FlowMoment = flowMoment; AddedMassFlow = addedMassFlow; AddedInertia = addedInertia;
    }
    public static readonly WaterCoeffs None = new(Vec3.Zero, Vec3.Zero, Vec3.Zero);
}

/// <summary>
/// The water's forces on a rigid body made of <see cref="HydroElement"/>s.
///
/// Split by stiffness, because the project integrates with RK4 at 200 Hz and water drag on a light piece has a time
/// constant of a millisecond (an aileron at 20 m/s: m/(½ρCdA·v) ≈ 0.8 ms):
///  - HYDROSTATICS (buoyancy, the weight of water that has got in) are smooth and go into the RK4 force sum
///    (<see cref="Hydrostatics"/>).
///  - DRAG and ADDED MASS are applied after the RK4 step as an implicit velocity update (<see cref="Correct"/>):
///        (m + m_a,new + dt·C)·v_new = (m + m_a,old)·v_old + m·(v_rk4 − v_old)
///    per body axis, which is unconditionally stable for the drag, divides the step's other accelerations by the
///    added mass the way (m + m_a)·a = F says, and — when m_a grows because the body is entering the water — is
///    exactly von Kármán's (1929) momentum theory of water impact: the body's momentum is shared with the water it
///    sets moving, and that is the slam. When m_a shrinks (leaving the water) the water's momentum is not handed
///    back, so the old m_a is used. The rates are updated the same way about each body axis with the added inertia
///    Σ m_a·r² (<see cref="WaterCoeffs.AddedInertia"/>, since 2026-11-05): a wing slapping the water because the body
///    is rolling shares the roll's momentum with the water, just as one slapping it because the body is sinking does.
///
/// MOVING WATER (<see cref="WaterCurrent"/>): every velocity above is the element's velocity THROUGH THE WATER. Drag is
/// −C·(v − v_w), so a body left in a river is carried at the river's speed; water newly entrained starts at the current's
/// speed (the slam is the change from the current's speed to the body's); and in a uniform current the whole scheme is
/// exactly the still-water one seen from a frame moving with the water. Where the current differs over the body (a
/// river's shear) each element feels its own, and the difference turns the body.
/// </summary>
public static class HydroBody
{
    /// <summary>Buoyancy and floodwater weight, body frame, about <paramref name="cgBody"/>. Zero with no water.</summary>
    public static (Vec3 Force, Vec3 Moment) Hydrostatics(IReadOnlyList<HydroElement> els, Vec3 cgBody, RigidBodyState s, double waterUp)
    {
        if (double.IsNaN(waterUp)) return (Vec3.Zero, Vec3.Zero);
        double rho = WaterPhysics.RhoWater, g = WaterPhysics.G;
        Vec3 fW = Vec3.Zero, mW = Vec3.Zero;   // world frame
        foreach (HydroElement e in els)
        {
            Vec3 r = s.Attitude.Rotate(e.Centre - cgBody);
            double halfH = e.VerticalHalf(s.Attitude);
            double depth = s.Position.Z + r.Z + waterUp;
            double sub = HydroElement.SubmergedFraction(depth, halfH);
            if (!e.BuoyancyElsewhere && sub > 0)
            {
                // Archimedes on the submerged slab of the box, acting at its centroid.
                Vec3 f = new(0, 0, -rho * g * e.EnvelopeM3 * sub);
                Vec3 at = r + new Vec3(0, 0, halfH * (1 - sub));
                fW += f; mW += Vec3.Cross(at, f);
            }
            double w = e.WaterM3;
            if (w > 1e-9 && e.VoidM3 > 1e-9)
            {
                // Water inside settles to the bottom of the box: its centroid is half its own depth up from the floor.
                double fill = w / e.VoidM3;
                Vec3 f = new(0, 0, rho * g * w);
                Vec3 at = r + new Vec3(0, 0, halfH * (1 - fill));
                fW += f; mW += Vec3.Cross(at, f);
            }
        }
        Quat inv = s.Attitude.Conjugate();
        return (inv.Rotate(fW), inv.Rotate(mW));
    }

    /// <summary>
    /// Added mass and linearised drag for the current state. The submerged part of each element feels water, the
    /// rest air (only when <paramref name="airDrag"/> — a free piece; the aeroplane's air is AeroModel's).
    ///
    /// Rotation: an element of half-length h whose centre is r from the axis has points at r ± h; its drag moment
    /// about the axis is approximated with the speed at its tip (|v_c| + |ω|·h) and the second moment r² + h²/3 of a
    /// uniform strip, so a piece spinning about its own centre is still damped.
    /// </summary>
    public static WaterCoeffs Coefficients(IReadOnlyList<HydroElement> els, Vec3 cgBody, RigidBodyState s, double waterUp, bool airDrag)
        => Coefficients(els, cgBody, s, waterUp, airDrag, WaterCurrent.Local.None);

    /// <param name="flow">The water's own motion over the body (<see cref="WaterCurrent.Around"/>); None = still water.</param>
    public static WaterCoeffs Coefficients(IReadOnlyList<HydroElement> els, Vec3 cgBody, RigidBodyState s, double waterUp, bool airDrag, in WaterCurrent.Local flow)
    {
        bool water = !double.IsNaN(waterUp);
        double ma0 = 0, ma1 = 0, ma2 = 0, c0 = 0, c1 = 0, c2 = 0, d0 = 0, d1 = 0, d2 = 0, j0 = 0, j1 = 0, j2 = 0;
        double wMag = s.Rates.Length;
        bool moving = water && flow.Moving;
        Quat qi = s.Attitude.Conjugate();
        Vec3 vRef = moving ? qi.Rotate(flow.At(s.Position, System.Math.Max(0.0, s.Position.Z + waterUp))) : Vec3.Zero;
        Vec3 flowF = Vec3.Zero, flowM = Vec3.Zero, amFlow = Vec3.Zero;
        foreach (HydroElement e in els)
        {
            if (e.NoDrag) continue;
            Vec3 rb = e.Centre - cgBody;
            double sub = 0, depth = 0;
            if (water)
            {
                depth = s.Position.Z + s.Attitude.Rotate(rb).Z + waterUp;
                sub = HydroElement.SubmergedFraction(depth, e.VerticalHalf(s.Attitude));
            }
            if (sub <= 0 && !airDrag) continue;
            double air = airDrag ? 1 - sub : 0;
            // The wet share against the water, the dry share against the air (still, as before: a piece's air is minor).
            double kwx = e.DragConstant(0, WaterPhysics.RhoWater) * sub, kwy = e.DragConstant(1, WaterPhysics.RhoWater) * sub, kwz = e.DragConstant(2, WaterPhysics.RhoWater) * sub;
            double kax = e.DragConstant(0, WaterPhysics.RhoAirSeaLevel) * air, kay = e.DragConstant(1, WaterPhysics.RhoAirSeaLevel) * air, kaz = e.DragConstant(2, WaterPhysics.RhoAirSeaLevel) * air;
            double kx = kwx + kax, ky = kwy + kay, kz = kwz + kaz;
            Vec3 v = s.Velocity + Vec3.Cross(s.Rates, rb);
            Vec3 vc = moving && sub > 0 ? qi.Rotate(flow.At(s.Position + s.Attitude.Rotate(rb), System.Math.Max(0.0, depth))) : Vec3.Zero;
            double vx = System.Math.Abs(v.X), vy = System.Math.Abs(v.Y), vz = System.Math.Abs(v.Z);
            // Linearised per axis: C = k_w |v − v_w| + k_a |v|. In still water this is (k_w + k_a)|v|, as it always was.
            double ax = kwx * System.Math.Abs(v.X - vc.X) + kax * vx, ay = kwy * System.Math.Abs(v.Y - vc.Y) + kay * vy, az = kwz * System.Math.Abs(v.Z - vc.Z) + kaz * vz;
            c0 += ax; c1 += ay; c2 += az;
            if (moving && sub > 0)
            {
                double wx = kwx * System.Math.Abs(v.X - vc.X), wy = kwy * System.Math.Abs(v.Y - vc.Y), wz = kwz * System.Math.Abs(v.Z - vc.Z);
                flowF += new Vec3(wx * vc.X, wy * vc.Y, wz * vc.Z);
                flowM += Vec3.Cross(rb, new Vec3(wx * (vc.X - vRef.X), wy * (vc.Y - vRef.Y), wz * (vc.Z - vRef.Z)));
                amFlow += new Vec3(e.AddedMass(0) * sub * vc.X, e.AddedMass(1) * sub * vc.Y, e.AddedMass(2) * sub * vc.Z);
            }
            double mx = e.AddedMass(0) * sub, my = e.AddedMass(1) * sub, mz = e.AddedMass(2) * sub;
            ma0 += mx; ma1 += my; ma2 += mz;
            double hx = e.Half.X, hy = e.Half.Y, hz = e.Half.Z;
            double lx = rb.X * rb.X + hx * hx / 3, ly = rb.Y * rb.Y + hy * hy / 3, lz = rb.Z * rb.Z + hz * hz / 3;
            j0 += my * lz + mz * ly; j1 += mx * lz + mz * lx; j2 += mx * ly + my * lx;
            // About x: points move along y (lever z) and along z (lever y).
            d0 += (ay + ky * wMag * hz) * (rb.Z * rb.Z + hz * hz / 3) + (az + kz * wMag * hy) * (rb.Y * rb.Y + hy * hy / 3);
            d1 += (ax + kx * wMag * hz) * (rb.Z * rb.Z + hz * hz / 3) + (az + kz * wMag * hx) * (rb.X * rb.X + hx * hx / 3);
            d2 += (ax + kx * wMag * hy) * (rb.Y * rb.Y + hy * hy / 3) + (ay + ky * wMag * hx) * (rb.X * rb.X + hx * hx / 3);
        }
        return new WaterCoeffs(new Vec3(ma0, ma1, ma2), new Vec3(c0, c1, c2), new Vec3(d0, d1, d2), flowF, flowM, amFlow, new Vec3(j0, j1, j2));
    }

    /// <summary>The implicit drag / added-mass update after an RK4 step (see the class summary). Position is
    /// re-advanced with the corrected velocity so the two stay consistent.</summary>
    public static RigidBodyState Correct(RigidBodyState before, RigidBodyState after, double massKg, Vec3 inertia, WaterCoeffs oldC, WaterCoeffs newC, double dt)
    {
        if (newC.AddedMass.LengthSquared + newC.Damping.LengthSquared + newC.RotDamping.LengthSquared < 1e-18
            && oldC.AddedMass.LengthSquared < 1e-18) return after;
        double Axis(double vOld, double vRk, double maOld, double maNew, double c, double flowF, double amFlow)
        {
            double mOld = massKg + maOld, mNew = massKg + maNew;
            double p = (mNew >= mOld ? mOld : mNew) * vOld + massKg * (vRk - vOld);
            // Water newly taken up brings the current's momentum with it (zero in still water); the current's drag.
            if (maNew > maOld && maNew > 1e-12) p += (maNew - maOld) * amFlow / maNew;
            p += dt * flowF;
            return p / (mNew + dt * c);
        }
        Vec3 vo = before.Velocity, vr = after.Velocity;
        var v = new Vec3(
            Axis(vo.X, vr.X, oldC.AddedMass.X, newC.AddedMass.X, newC.Damping.X, newC.FlowForce.X, newC.AddedMassFlow.X),
            Axis(vo.Y, vr.Y, oldC.AddedMass.Y, newC.AddedMass.Y, newC.Damping.Y, newC.FlowForce.Y, newC.AddedMassFlow.Y),
            Axis(vo.Z, vr.Z, oldC.AddedMass.Z, newC.AddedMass.Z, newC.Damping.Z, newC.FlowForce.Z, newC.AddedMassFlow.Z));
        // The rotations carry their entrained water too (the rule FloatHydro.CorrectForAddedMass applies to the floats).
        // Until 2026-11-05 they did not: a wing slapping the water because the airframe was ROLLING handed no momentum to
        // the water here, yet WaterSlam.ElementForce, which loads the joints and the panels, charged that wing the whole
        // slam of its ω × r. A J-3 floating inverted at 0.2 m/s and rocking 43°/s read 67 kN·m on its 28 kN·m wing root
        // with the airframe barely slowed (Ty 2026-11-05 13:56, the ailerons lost after a soft nose-over). Now the slam
        // that loads the wing also slows the roll that drove it.
        double Spin(double wOld, double wRk, double i, double jOld, double jNew, double d, double flowM)
        {
            double iOld = i + jOld, iNew = i + jNew;
            double l = (iNew >= iOld ? iOld : iNew) * wOld + i * (wRk - wOld) + dt * flowM;
            return l / (iNew + dt * d);
        }
        Vec3 wo = before.Rates, wr = after.Rates;
        var w = new Vec3(
            Spin(wo.X, wr.X, inertia.X, oldC.AddedInertia.X, newC.AddedInertia.X, newC.RotDamping.X, newC.FlowMoment.X),
            Spin(wo.Y, wr.Y, inertia.Y, oldC.AddedInertia.Y, newC.AddedInertia.Y, newC.RotDamping.Y, newC.FlowMoment.Y),
            Spin(wo.Z, wr.Z, inertia.Z, oldC.AddedInertia.Z, newC.AddedInertia.Z, newC.RotDamping.Z, newC.FlowMoment.Z));
        Vec3 pos = after.Position + after.Attitude.Rotate(v - vr) * dt;
        return new RigidBodyState(pos, after.Attitude, v, w);
    }

    /// <summary>Reserve buoyancy if the whole body went under now with the air it has: ρ_w·(Σ envelope − Σ water
    /// inside) − mass, kg. Negative = it cannot stay up any more (the FloodState "Sinking" flag).</summary>
    public static double ReserveKg(IReadOnlyList<HydroElement> els, double massKg)
    {
        double v = 0;
        foreach (HydroElement e in els) { if (!e.BuoyancyElsewhere) v += e.EnvelopeM3; v -= e.WaterM3; }
        return WaterPhysics.RhoWater * v - massKg;
    }
}

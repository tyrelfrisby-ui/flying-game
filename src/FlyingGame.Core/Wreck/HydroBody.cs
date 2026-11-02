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
    public WaterCoeffs(Vec3 addedMass, Vec3 damping, Vec3 rotDamping) { AddedMass = addedMass; Damping = damping; RotDamping = rotDamping; }
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
///    back, so the old m_a is used.
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
    {
        bool water = !double.IsNaN(waterUp);
        double ma0 = 0, ma1 = 0, ma2 = 0, c0 = 0, c1 = 0, c2 = 0, d0 = 0, d1 = 0, d2 = 0;
        double wMag = s.Rates.Length;
        foreach (HydroElement e in els)
        {
            if (e.NoDrag) continue;
            Vec3 rb = e.Centre - cgBody;
            double sub = 0;
            if (water)
            {
                double depth = s.Position.Z + s.Attitude.Rotate(rb).Z + waterUp;
                sub = HydroElement.SubmergedFraction(depth, e.VerticalHalf(s.Attitude));
            }
            if (sub <= 0 && !airDrag) continue;
            double air = airDrag ? 1 - sub : 0;
            double rhoMix(int axis) => e.DragConstant(axis, WaterPhysics.RhoWater) * sub + e.DragConstant(axis, WaterPhysics.RhoAirSeaLevel) * air;
            double kx = rhoMix(0), ky = rhoMix(1), kz = rhoMix(2);
            Vec3 v = s.Velocity + Vec3.Cross(s.Rates, rb);
            double vx = System.Math.Abs(v.X), vy = System.Math.Abs(v.Y), vz = System.Math.Abs(v.Z);
            c0 += kx * vx; c1 += ky * vy; c2 += kz * vz;
            ma0 += e.AddedMass(0) * sub; ma1 += e.AddedMass(1) * sub; ma2 += e.AddedMass(2) * sub;
            double hx = e.Half.X, hy = e.Half.Y, hz = e.Half.Z;
            // About x: points move along y (lever z) and along z (lever y).
            d0 += ky * (vy + wMag * hz) * (rb.Z * rb.Z + hz * hz / 3) + kz * (vz + wMag * hy) * (rb.Y * rb.Y + hy * hy / 3);
            d1 += kx * (vx + wMag * hz) * (rb.Z * rb.Z + hz * hz / 3) + kz * (vz + wMag * hx) * (rb.X * rb.X + hx * hx / 3);
            d2 += kx * (vx + wMag * hy) * (rb.Y * rb.Y + hy * hy / 3) + ky * (vy + wMag * hx) * (rb.X * rb.X + hx * hx / 3);
        }
        return new WaterCoeffs(new Vec3(ma0, ma1, ma2), new Vec3(c0, c1, c2), new Vec3(d0, d1, d2));
    }

    /// <summary>The implicit drag / added-mass update after an RK4 step (see the class summary). Position is
    /// re-advanced with the corrected velocity so the two stay consistent.</summary>
    public static RigidBodyState Correct(RigidBodyState before, RigidBodyState after, double massKg, Vec3 inertia, WaterCoeffs oldC, WaterCoeffs newC, double dt)
    {
        if (newC.AddedMass.LengthSquared + newC.Damping.LengthSquared + newC.RotDamping.LengthSquared < 1e-18
            && oldC.AddedMass.LengthSquared < 1e-18) return after;
        double Axis(double vOld, double vRk, double maOld, double maNew, double c)
        {
            double mOld = massKg + maOld, mNew = massKg + maNew;
            double p = (mNew >= mOld ? mOld : mNew) * vOld + massKg * (vRk - vOld);
            return p / (mNew + dt * c);
        }
        Vec3 vo = before.Velocity, vr = after.Velocity;
        var v = new Vec3(
            Axis(vo.X, vr.X, oldC.AddedMass.X, newC.AddedMass.X, newC.Damping.X),
            Axis(vo.Y, vr.Y, oldC.AddedMass.Y, newC.AddedMass.Y, newC.Damping.Y),
            Axis(vo.Z, vr.Z, oldC.AddedMass.Z, newC.AddedMass.Z, newC.Damping.Z));
        Vec3 wr = after.Rates;
        var w = new Vec3(
            wr.X * inertia.X / (inertia.X + dt * newC.RotDamping.X),
            wr.Y * inertia.Y / (inertia.Y + dt * newC.RotDamping.Y),
            wr.Z * inertia.Z / (inertia.Z + dt * newC.RotDamping.Z));
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

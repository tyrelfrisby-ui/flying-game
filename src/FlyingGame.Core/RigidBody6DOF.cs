using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>Mass properties about the CG. Ixz kept full; Ixy and Iyz are zero for a symmetric aeroplane and are carried
/// only once it is not — an airframe that has lost a wing panel on one side (Aircraft.ShedMass), a broken-off piece. The
/// tensor is [[Ixx, −Ixy, −Ixz], [−Ixy, Iyy, −Iyz], [−Ixz, −Iyz, Izz]] with Ixy = Σm·x·y etc.</summary>
public readonly struct MassProperties
{
    public readonly double MassKg;
    public readonly double Ixx;
    public readonly double Iyy;
    public readonly double Izz;
    public readonly double Ixz;
    public readonly double Ixy;
    public readonly double Iyz;

    public MassProperties(double massKg, double ixx, double iyy, double izz, double ixz)
        : this(massKg, ixx, iyy, izz, ixz, 0.0, 0.0) { }

    public MassProperties(double massKg, double ixx, double iyy, double izz, double ixz, double ixy, double iyz)
    {
        MassKg = massKg;
        Ixx = ixx;
        Iyy = iyy;
        Izz = izz;
        Ixz = ixz;
        Ixy = ixy;
        Iyz = iyz;
    }

    /// <summary>I·ω, body axes.</summary>
    public Vec3 Times(Vec3 w) => new(
        Ixx * w.X - Ixy * w.Y - Ixz * w.Z,
        -Ixy * w.X + Iyy * w.Y - Iyz * w.Z,
        -Ixz * w.X - Iyz * w.Y + Izz * w.Z);

    /// <summary>I⁻¹·v, body axes (the full symmetric 3×3 inverse).</summary>
    public Vec3 Solve(Vec3 v)
    {
        double a = Ixx, b = -Ixy, c = -Ixz, d = Iyy, e = -Iyz, f = Izz;
        double A = d * f - e * e, B = c * e - b * f, C = b * e - c * d, D = a * f - c * c, E = b * c - a * e, F = a * d - b * b;
        double det = a * A + b * B + c * C;
        return new Vec3(A * v.X + B * v.Y + C * v.Z, B * v.X + D * v.Y + E * v.Z, C * v.X + E * v.Y + F * v.Z) / det;
    }
}

/// <summary>Full 6DOF state: world position/attitude plus body-frame velocity (u,v,w) and body rates (p,q,r).</summary>
public readonly struct RigidBodyState
{
    public readonly Vec3 Position;
    public readonly Quat Attitude;
    public readonly Vec3 Velocity;
    public readonly Vec3 Rates;

    public RigidBodyState(Vec3 position, Quat attitude, Vec3 velocity, Vec3 rates)
    {
        Position = position;
        Attitude = attitude;
        Velocity = velocity;
        Rates = rates;
    }

    public static RigidBodyState operator +(RigidBodyState s, RigidBodyDerivative d) => new(
        s.Position + d.PositionDot,
        new Quat(s.Attitude.X + d.AttitudeDot.X, s.Attitude.Y + d.AttitudeDot.Y, s.Attitude.Z + d.AttitudeDot.Z, s.Attitude.W + d.AttitudeDot.W),
        s.Velocity + d.VelocityDot,
        s.Rates + d.RatesDot);

    public RigidBodyState WithNormalizedAttitude() => new(Position, Attitude.Normalized(), Velocity, Rates);
}

/// <summary>Time derivative of a RigidBodyState. AttitudeDot is a raw (non-unit) quaternion derivative.</summary>
public readonly struct RigidBodyDerivative
{
    public readonly Vec3 PositionDot;
    public readonly Quat AttitudeDot;
    public readonly Vec3 VelocityDot;
    public readonly Vec3 RatesDot;

    public RigidBodyDerivative(Vec3 positionDot, Quat attitudeDot, Vec3 velocityDot, Vec3 ratesDot)
    {
        PositionDot = positionDot;
        AttitudeDot = attitudeDot;
        VelocityDot = velocityDot;
        RatesDot = ratesDot;
    }

    public static RigidBodyDerivative operator *(RigidBodyDerivative d, double s) => new(
        d.PositionDot * s, d.AttitudeDot * s, d.VelocityDot * s, d.RatesDot * s);

    public static RigidBodyDerivative operator +(RigidBodyDerivative a, RigidBodyDerivative b) => new(
        a.PositionDot + b.PositionDot, a.AttitudeDot + b.AttitudeDot, a.VelocityDot + b.VelocityDot, a.RatesDot + b.RatesDot);
}

/// <summary>
/// Rigid-body equations of motion in body axes (x forward, y right, z down), full inertia tensor
/// including the Ixz product term (couples roll and yaw — required for credible spin dynamics per
/// ARCHITECTURE.md). Integrator: fixed-step RK4, quaternion renormalized once per step.
/// </summary>
public static class RigidBody6DOF
{
    /// <summary>
    /// Computes the state derivative given total body-frame force/moment about the CG (aero + gravity +
    /// propulsion, already summed by the caller).
    /// </summary>
    public static RigidBodyDerivative ComputeDerivative(RigidBodyState s, Vec3 forceBody, Vec3 momentBody, MassProperties mp)
    {
        Vec3 velocityDot = forceBody / mp.MassKg - Vec3.Cross(s.Rates, s.Velocity);

        if (mp.Ixy != 0.0 || mp.Iyz != 0.0)
        {
            // An asymmetric body: Euler's equations with the whole tensor, I·ω̇ = M − ω × I·ω.
            Vec3 wd = mp.Solve(momentBody - Vec3.Cross(s.Rates, mp.Times(s.Rates)));
            return new RigidBodyDerivative(s.Attitude.Rotate(s.Velocity), Quat.Derivative(s.Attitude, s.Rates), velocityDot, wd);
        }

        double p = s.Rates.X, q = s.Rates.Y, r = s.Rates.Z;
        double l = momentBody.X, m = momentBody.Y, n = momentBody.Z;
        double ixx = mp.Ixx, iyy = mp.Iyy, izz = mp.Izz, ixz = mp.Ixz;

        double gamma = ixx * izz - ixz * ixz;

        double pDot = (ixz * (ixx - iyy + izz) * p * q
                       - (izz * (izz - iyy) + ixz * ixz) * q * r
                       + izz * l + ixz * n) / gamma;

        double rDot = ((ixx * (ixx - iyy) + ixz * ixz) * p * q
                       - ixz * (ixx - iyy + izz) * q * r
                       + ixz * l + ixx * n) / gamma;

        double qDot = ((izz - ixx) * p * r - ixz * (p * p - r * r) + m) / iyy;

        Vec3 ratesDot = new(pDot, qDot, rDot);
        Vec3 positionDot = s.Attitude.Rotate(s.Velocity);
        Quat attitudeDot = Quat.Derivative(s.Attitude, s.Rates);

        return new RigidBodyDerivative(positionDot, attitudeDot, velocityDot, ratesDot);
    }

    /// <summary>
    /// Advances the state by dt using classic 4th-order Runge-Kutta, re-evaluating forces/moments at
    /// each stage via <paramref name="forceMomentFunc"/> (aero depends on the intermediate velocity/rates).
    /// </summary>
    public static RigidBodyState IntegrateRK4(
        RigidBodyState s0,
        double dt,
        MassProperties mp,
        System.Func<RigidBodyState, (Vec3 Force, Vec3 Moment)> forceMomentFunc)
    {
        RigidBodyDerivative Deriv(RigidBodyState s)
        {
            var (force, moment) = forceMomentFunc(s);
            return ComputeDerivative(s, force, moment, mp);
        }

        RigidBodyDerivative k1 = Deriv(s0);
        RigidBodyDerivative k2 = Deriv(s0 + k1 * (dt / 2.0));
        RigidBodyDerivative k3 = Deriv(s0 + k2 * (dt / 2.0));
        RigidBodyDerivative k4 = Deriv(s0 + k3 * dt);

        RigidBodyDerivative combined = (k1 + k2 * 2.0 + k3 * 2.0 + k4) * (dt / 6.0);
        RigidBodyState result = s0 + combined;
        return result.WithNormalizedAttitude();
    }
}

namespace FlyingGame.Core.MathTypes;

/// <summary>
/// Double-precision quaternion, Hamilton convention. Represents a body-to-world rotation:
/// Rotate(vBody) returns the same vector expressed in world axes.
/// </summary>
public readonly struct Quat
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;
    public readonly double W;

    public static readonly Quat Identity = new(0, 0, 0, 1);

    public Quat(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Pure (vector-only) quaternion, used to carry body rates for the kinematics equation.</summary>
    public static Quat FromVector(Vec3 v) => new(v.X, v.Y, v.Z, 0);

    public static Quat operator +(Quat a, Quat b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Quat operator *(Quat a, double s) => new(a.X * s, a.Y * s, a.Z * s, a.W * s);
    public static Quat operator *(double s, Quat a) => new(a.X * s, a.Y * s, a.Z * s, a.W * s);

    /// <summary>Hamilton product a ⊗ b.</summary>
    public static Quat Multiply(Quat a, Quat b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public double LengthSquared => X * X + Y * Y + Z * Z + W * W;

    public Quat Normalized()
    {
        double len = System.Math.Sqrt(LengthSquared);
        return len > 1e-12 ? new Quat(X / len, Y / len, Z / len, W / len) : Identity;
    }

    public Quat Conjugate() => new(-X, -Y, -Z, W);

    /// <summary>Rotates a body-frame vector into world axes: v_world = q ⊗ (0,v) ⊗ q*.</summary>
    public Vec3 Rotate(Vec3 v)
    {
        Quat p = FromVector(v);
        Quat r = Multiply(Multiply(this, p), Conjugate());
        return new Vec3(r.X, r.Y, r.Z);
    }

    /// <summary>
    /// Quaternion kinematics derivative for body rates (p,q,r): qDot = 0.5 * q ⊗ (0,p,q,r).
    /// Caller is responsible for renormalizing after integrating (RK4 stages accumulate raw components).
    /// </summary>
    public static Quat Derivative(Quat attitude, Vec3 bodyRates) =>
        Multiply(attitude, FromVector(bodyRates)) * 0.5;

    public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4}, {W:F4})";
}

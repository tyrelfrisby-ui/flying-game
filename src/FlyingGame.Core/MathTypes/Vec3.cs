namespace FlyingGame.Core.MathTypes;

/// <summary>
/// Double-precision 3D vector. Body axes convention throughout Core/Sim: x forward, y right, z down.
/// Double precision (not System.Numerics' float Vector3) so post-stall/spin integration stays quiet — see
/// ARCHITECTURE.md "6DOF numerical stability".
/// </summary>
public readonly struct Vec3
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public static readonly Vec3 Zero = new(0, 0, 0);

    public Vec3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;

    public static double Dot(Vec3 a, Vec3 b) => a.Dot(b);

    public Vec3 Cross(Vec3 b) => new(
        Y * b.Z - Z * b.Y,
        Z * b.X - X * b.Z,
        X * b.Y - Y * b.X);

    public static Vec3 Cross(Vec3 a, Vec3 b) => a.Cross(b);

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Length => System.Math.Sqrt(LengthSquared);

    public Vec3 Normalized()
    {
        double len = Length;
        return len > 1e-12 ? this / len : Zero;
    }

    public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
}

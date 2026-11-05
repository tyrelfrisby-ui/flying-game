using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// What the panel's instruments can sense, gathered once a step. Nothing here is an instrument reading: it is
/// the aeroplane's own state, in the sim's frames, and every instrument works out what IT would show from it.
///
/// Frames: world is x north, y east, z down (metres); body is x forward, y right, z down. <see cref="Attitude"/>
/// rotates body vectors into world.
/// </summary>
public struct PanelInputs
{
    /// <summary>Body-to-world attitude.</summary>
    public Quat Attitude;
    /// <summary>Body rates p, q, r, rad/s.</summary>
    public Vec3 RatesBody;
    /// <summary>
    /// Specific force in body axes, m/s²: the non-gravitational force per unit mass, which is what an
    /// accelerometer, a pendulum, a ball in a tube and a compass float all respond to. Level unaccelerated
    /// flight is (0, 0, −9.81): the wing pushes UP.
    /// </summary>
    public Vec3 SpecificForceBody;
    /// <summary>World position, metres (z down, so altitude is −Z).</summary>
    public Vec3 PositionWorld;
    /// <summary>Crankshaft rpm. The vacuum pump is geared to it.</summary>
    public double EngineRpm;
    /// <summary>Static pressure and temperature outside, Pa and K. The vacuum system breathes this air.</summary>
    public double AmbientPressurePa, AmbientTempK;
    /// <summary>Main bus voltage. A 1970s 172 is a 14 V system: ~14.2 V on the alternator, ~12.4 V on the battery alone.</summary>
    public double BusVolts;

    public double AltitudeM => -PositionWorld.Z;

    /// <summary>Straight and level, at rest in the air at <paramref name="altM"/>, heading <paramref name="headingDeg"/> — the
    /// state the tests and an air start begin from.</summary>
    public static PanelInputs Level(double headingDeg, double altM = 1000.0, double rpm = 2300.0)
    {
        double h = headingDeg * Math.PI / 180.0;
        return new PanelInputs
        {
            Attitude = new Quat(0, 0, Math.Sin(h / 2), Math.Cos(h / 2)),
            RatesBody = Vec3.Zero,
            SpecificForceBody = new Vec3(0, 0, -Earth.G),
            PositionWorld = new Vec3(0, 0, -altM),
            EngineRpm = rpm,
            AmbientPressurePa = Atmosphere.PressureAtAltitude(altM),
            AmbientTempK = 288.15 - 0.0065 * Math.Clamp(altM, 0, 11000),
            BusVolts = 14.2,
        };
    }
}

/// <summary>The planet the gyros sit on: its rotation and its magnetic field.</summary>
public static class Earth
{
    /// <summary>Sidereal rotation rate, rad/s (15.041 °/h).</summary>
    public const double OmegaRadS = 7.2921159e-5;
    public const double G = 9.80665;
    /// <summary>Equatorial surface strength of a centred dipole fitted to the present field, tesla (IGRF g₁⁰ ≈ −29.4 µT;
    /// the full first-degree term, with g₁¹ and h₁¹, is 30 µT).</summary>
    public const double DipoleB0Tesla = 30.0e-6;

    /// <summary>The Earth's rotation expressed in local north-east-down axes, rad/s: Ω (cos φ, 0, −sin φ).
    /// A gyro that holds still in space appears, to the ground, to turn the other way at this rate.</summary>
    public static Vec3 RateNed(double latitudeDeg)
    {
        double p = latitudeDeg * Math.PI / 180.0;
        return new Vec3(OmegaRadS * Math.Cos(p), 0, -OmegaRadS * Math.Sin(p));
    }

    /// <summary>
    /// The geomagnetic field in north-east-down axes, tesla, for a centred dipole whose axis is the spin axis
    /// (this world's variation is zero by decision — <see cref="Nav.Navaids.MagneticVariationDeg"/> — so
    /// magnetic and geographic latitude are the same thing here). Horizontal B₀ cos λ, vertical 2 B₀ sin λ,
    /// so the dip obeys tan I = 2 tan λ: 63.4° at 45° N, which is what a compass in the northern United
    /// States actually feels.
    /// </summary>
    public static Vec3 MagneticFieldNed(double latitudeDeg)
    {
        double p = latitudeDeg * Math.PI / 180.0;
        return new Vec3(DipoleB0Tesla * Math.Cos(p), 0, 2.0 * DipoleB0Tesla * Math.Sin(p));
    }
}

/// <summary>Small vector helpers the gyros share.</summary>
internal static class V
{
    public const double Deg = Math.PI / 180.0;

    /// <summary>Rodrigues: <paramref name="v"/> turned by <paramref name="angle"/> rad about the unit <paramref name="axis"/>.</summary>
    public static Vec3 Rotate(Vec3 v, Vec3 axis, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return v * c + axis.Cross(v) * s + axis * (axis.Dot(v) * (1 - c));
    }

    /// <summary>The unit vector, or <paramref name="fallback"/> if there is nothing to normalise.</summary>
    public static Vec3 Unit(Vec3 v, Vec3 fallback)
    {
        double l = v.Length;
        return l > 1e-12 ? v / l : fallback;
    }

    /// <summary>Angle between two unit vectors, rad, safe at 0 and π.</summary>
    public static double Angle(Vec3 a, Vec3 b) => Math.Atan2(a.Cross(b).Length, a.Dot(b));

    public static double Wrap360(double deg)
    {
        double d = deg % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    /// <summary>
    /// Move unit vector <paramref name="s"/> toward unit <paramref name="target"/> by at most <paramref name="maxStep"/> rad,
    /// along the great circle. Never overshoots.
    /// </summary>
    public static Vec3 Toward(Vec3 s, Vec3 target, double maxStep)
    {
        double ang = Angle(s, target);
        if (ang < 1e-12 || maxStep <= 0) return s;
        Vec3 axis = Unit(s.Cross(target), Vec3.Zero);
        if (axis.LengthSquared < 0.5) return s;       // exactly opposite: no defined direction, so no move
        return Rotate(s, axis, Math.Min(ang, maxStep)).Normalized();
    }
}

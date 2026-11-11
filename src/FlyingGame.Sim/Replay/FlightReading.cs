using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// The numbers an instructor reads off a flight, taken from a RECORDED frame rather than from a live aeroplane.
///
/// Why this exists beside <c>FlightSignals</c> (the challenge grader's equivalent): that one takes an
/// <see cref="Aircraft"/>, and a debrief has no aeroplane — it has <see cref="FlightRecorder"/>, whose frames are a
/// pose, a set of deflections and a few channels. One place turns a frame into the quantities the debrief talks about,
/// so the suite tests the same arithmetic the debrief prints and nothing computes a sideslip twice.
///
/// Sim conventions throughout: body x forward, y right, z down; world NED, so −Position.Z is height and +Z of a world
/// velocity is sink. Angles in radians unless a name says degrees.
/// </summary>
public static class FlightReading
{
    public const double R2D = 180.0 / System.Math.PI;
    /// <summary>Knots per m/s, as the rest of the repo spells it.</summary>
    public const double KtPerMs = 1.943844;
    public const double FtPerM = 3.28084;

    /// <summary>
    /// Indicated airspeed from true, m/s: TAS × √(ρ/ρ0). The debrief quotes IAS because every limit it compares
    /// against is an indicated one — Vne, the stall, the book glide speed — and because that is the needle the pilot
    /// was looking at.
    /// </summary>
    public static double IasFromTas(double tasMs, double altitudeM) =>
        tasMs * System.Math.Sqrt(Atmosphere.DensityAtAltitude(altitudeM) / Atmosphere.SeaLevelDensityKgM3);

    /// <summary>Height above sea level, m.</summary>
    public static double AltitudeM(in RigidBodyState s) => -s.Position.Z;
    public static double AltitudeM(in ReplayFrame f) => -f.State.Position.Z;

    /// <summary>True airspeed, m/s (the body velocity's magnitude: the recording holds no separate air-relative
    /// velocity, and the wind it holds is the mean at the aeroplane).</summary>
    public static double TasMs(in ReplayFrame f) => f.State.Velocity.Length;

    /// <summary>Indicated airspeed, m/s.</summary>
    public static double IasMs(in ReplayFrame f) => IasFromTas(f.State.Velocity.Length, AltitudeM(f));

    /// <summary>Sink, m/s positive DOWN (the world-frame vertical velocity).</summary>
    public static double SinkMs(in ReplayFrame f) => f.State.Attitude.Rotate(f.State.Velocity).Z;

    /// <summary>Angle of attack, rad (the body-axis flow angle, as FlightSignals' "alphaRad").</summary>
    public static double AlphaRad(in ReplayFrame f) => System.Math.Atan2(f.State.Velocity.Z, f.State.Velocity.X);

    /// <summary>Sideslip, degrees: + = the air coming from the right. At a touchdown this is the CRAB — how far the
    /// wheels are from rolling the way the aeroplane is going, which is the number an instructor asks for.</summary>
    public static double SideslipDeg(in RigidBodyState s)
    {
        double v = s.Velocity.Length;
        return v > 1e-3 ? System.Math.Asin(System.Math.Clamp(s.Velocity.Y / v, -1, 1)) * R2D : 0.0;
    }
    public static double SideslipDeg(in ReplayFrame f) => SideslipDeg(f.State);

    /// <summary>Pitch, degrees.</summary>
    public static double PitchDeg(in RigidBodyState s)
    {
        Quat q = s.Attitude;
        return System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * R2D;
    }
    public static double PitchDeg(in ReplayFrame f) => PitchDeg(f.State);

    /// <summary>Bank, degrees (+ = right wing down).</summary>
    public static double BankDeg(in ReplayFrame f)
    {
        Quat q = f.State.Attitude;
        return System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * R2D;
    }

    /// <summary>Magnetic-less heading, degrees 0..360 (0 = north, + clockwise).</summary>
    public static double HeadingDeg(in ReplayFrame f)
    {
        Quat q = f.State.Attitude;
        double h = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * R2D;
        return (h + 360.0) % 360.0;
    }

    /// <summary>
    /// mm:ss of a flight clock, the way a debrief line opens ("4:12"). Seconds are whole: an instructor says "about
    /// seven seconds", not "7.03", and the latencies that need a tenth print their own tenth.
    /// </summary>
    public static string Clock(double sec)
    {
        if (sec < 0) sec = 0;
        int whole = (int)System.Math.Round(sec);
        return $"{whole / 60}:{whole % 60:00}";
    }

    /// <summary>Knots, rounded, as a string.</summary>
    public static string Kt(double ms) => $"{ms * KtPerMs:0} kt";
    /// <summary>Feet, rounded to 10, as a string — a debrief does not claim a foot.</summary>
    public static string Ft(double m) => $"{System.Math.Round(m * FtPerM / 10.0) * 10:0} ft";
    /// <summary>Feet per minute, rounded to 10.</summary>
    public static string Fpm(double ms) => $"{System.Math.Round(ms * FtPerM * 60.0 / 10.0) * 10:0} fpm";
}

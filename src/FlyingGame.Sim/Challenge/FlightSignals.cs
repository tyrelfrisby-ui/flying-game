using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Challenge;

/// <summary>
/// Extracts the graded flight signals from an Aircraft's state. One place maps signal names to values,
/// so a new challenge signal is one line here plus its name in a JSON tolerance. Angles in radians,
/// speeds m/s, altitude m — the challenge JSON uses the same SI units.
/// </summary>
public static class FlightSignals
{
    public static double Read(string signal, Aircraft aircraft)
    {
        RigidBodyState s = aircraft.State;
        Quat q = s.Attitude;
        Vec3 v = s.Velocity;
        double speed = v.Length;

        return signal switch
        {
            "headingRad" => Heading(q),
            "bankRad" => Bank(q),
            "pitchRad" => Pitch(q),
            "iasMs" => speed,
            "altitudeM" => -s.Position.Z,
            "glideRatio" => GlideRatio(aircraft),
            "alphaRad" => System.Math.Atan2(v.Z, v.X),
            "betaRad" => speed > 1e-3 ? System.Math.Asin(System.Math.Clamp(v.Y / speed, -1, 1)) : 0,
            "rollRate" => s.Rates.X,
            "pitchRate" => s.Rates.Y,
            "yawRate" => s.Rates.Z,
            "gLoad" => GLoad(aircraft),
            "verticalSpeedMs" => q.Rotate(v).Z, // world +z down: sink positive
            _ => throw new System.ArgumentException($"Unknown challenge signal '{signal}'"),
        };
    }

    private static double Heading(Quat q) =>
        System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));

    private static double Bank(Quat q) =>
        System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));

    private static double Pitch(Quat q) =>
        System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));

    private static double GlideRatio(Aircraft aircraft)
    {
        Vec3 worldVel = aircraft.State.Attitude.Rotate(aircraft.State.Velocity);
        double horiz = System.Math.Sqrt(worldVel.X * worldVel.X + worldVel.Y * worldVel.Y);
        double sink = worldVel.Z; // +down
        return sink > 0.05 ? horiz / sink : 999;
    }

    private static double GLoad(Aircraft aircraft)
    {
        // Body-z specific force / g, approximated from aero+prop normal load. Cheap proxy: lift/weight
        // via vertical accel is not stored, so use 1 + (pitch-rate * speed / g) as a live estimate.
        double speed = aircraft.State.Velocity.Length;
        return 1.0 + aircraft.State.Rates.Y * speed / Atmosphere.GravityMs2;
    }
}

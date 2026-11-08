namespace FlyingGame.Core;

/// <summary>
/// Flying by tilting the phone (Ty 2026-11-08, for a beta tester: "Can we add control by phone tilt? Aileron of roll
/// rudder is yaw and elevator is pitch"). Tilt drives ONLY those three; the throttle, flaps, gear, spoiler and water
/// rudder stay on the screen where they are.
///
/// This half is the mapping, and it is pure: an angle away from how the phone was held when the pilot centred it, in
/// degrees, becomes a stick command in -1..1. The reading of the sensors and the choice of reference belong to the
/// Unity side; everything here can be tested without a phone.
///
/// Roll and pitch are honest angles: gravity tells the phone which way is down, so they are absolute and they do not
/// drift. YAW IS NOT THE SAME KIND OF NUMBER — gravity says nothing about rotation about itself, so yaw can only come
/// from integrating the gyro, and it wanders over minutes however good the sensor is. That is why the rudder is its
/// own switch and why re-centring exists.
/// </summary>
public static class TiltControls
{
    /// <summary>Tilt (deg) that commands full deflection at the middle of the sensitivity range.</summary>
    public const double DefaultFullScaleDeg = 30.0;
    /// <summary>Tilt (deg) ignored around centre, so a hand that is merely not perfectly still does not fly.</summary>
    public const double DefaultDeadzoneDeg = 2.0;
    /// <summary>The ends of the sensitivity slider, as the tilt needed for full deflection. SMALLER IS MORE SENSITIVE:
    /// 10° means a flick of the wrist is full aileron, 60° means you have to turn the phone on its side.</summary>
    public const double MinFullScaleDeg = 10.0, MaxFullScaleDeg = 60.0;
    /// <summary>Yaw wanders, so its deadzone is wider: a rudder that creeps while the phone is still is worse than a
    /// rudder that needs a deliberate push.</summary>
    public const double DefaultYawDeadzoneDeg = 4.0;

    /// <summary>
    /// A tilt angle as a stick command. Zero inside the deadzone, ±1 at <paramref name="fullScaleDeg"/> and beyond, and
    /// in between a straight line from the edge of the deadzone — so the first degree past the deadzone commands a
    /// little, not a lurch.
    /// </summary>
    public static double Axis(double deg, double fullScaleDeg, double deadzoneDeg = DefaultDeadzoneDeg, double expo = 0.0)
    {
        if (double.IsNaN(deg)) return 0.0;
        double full = System.Math.Clamp(fullScaleDeg, 1.0, 180.0);
        double dz = System.Math.Clamp(deadzoneDeg, 0.0, full * 0.9);
        double a = deg < 0 ? -deg : deg;
        if (a <= dz) return 0.0;
        double x = System.Math.Clamp((a - dz) / (full - dz), 0.0, 1.0);
        if (expo > 0.0)
        {
            double e = System.Math.Clamp(expo, 0.0, 1.0);
            x = (1.0 - e) * x + e * x * x * x;   // softer around centre, same full deflection at the stop
        }
        return deg < 0.0 ? -x : x;
    }

    /// <summary>The sensitivity slider (0 least sensitive .. 1 most) as the full-scale tilt it means.</summary>
    public static double FullScaleForSensitivity(double sensitivity01)
    {
        double t = System.Math.Clamp(sensitivity01, 0.0, 1.0);
        return MaxFullScaleDeg + (MinFullScaleDeg - MaxFullScaleDeg) * t;
    }

    /// <summary>Where the slider sits for a given full-scale tilt (the inverse, for drawing it).</summary>
    public static double SensitivityForFullScale(double fullScaleDeg)
    {
        double span = MinFullScaleDeg - MaxFullScaleDeg;
        if (span == 0.0) return 0.5;
        return System.Math.Clamp((System.Math.Clamp(fullScaleDeg, MinFullScaleDeg, MaxFullScaleDeg) - MaxFullScaleDeg) / span, 0.0, 1.0);
    }
}

namespace FlyingGame.Core;

/// <summary>
/// THE STICK EXPO (Ty, 2026-11-04: "hand flying needs some expo"). RC-style exponential on the three flight
/// controls, the one curve every input path reaches the aeroplane through: touch pads, keyboard ramps, a
/// calibrated joystick and its rudder pedals, a gamepad, and the Quest's controllers all hand raw stick positions
/// to <c>Aircraft.ShapeAxis</c>, which takes its dead zone and then calls <see cref="Apply"/> — once, on every
/// platform. Nothing upstream of it adds a curve of its own (the gamepad's used to: <see cref="GamepadShaping.DefaultExpo"/>).
///
///   out = (1 − e)·x + e·x³,   x clamped to ±1
///
/// At x = ±1 that is ±1 for every e, so full stick is full travel; it is odd, so left and right are mirror images;
/// and for e in 0…1 the slope (1 − e) + 3e·x² is never negative, so more stick never gives less control. e = 0 is
/// linear.
///
/// The setting is ONE number, the pitch-and-roll expo ("Stick expo", 0…70 %). The rudder takes the same curve,
/// scaled by <see cref="YawPerPitchRoll"/> (0.20 / 0.30), so the default 30 % gives 20 % on the pedals.
/// </summary>
public static class InputExpo
{
    /// <summary>Pitch and roll expo out of the box.</summary>
    public const double DefaultPitchRoll = 0.30;
    /// <summary>Yaw (rudder) expo out of the box.</summary>
    public const double DefaultYaw = 0.20;
    /// <summary>The top of the "Stick expo" slider.</summary>
    public const double MaxStickExpo = 0.70;
    /// <summary>The rudder's expo as a fraction of the pitch-and-roll setting.</summary>
    public const double YawPerPitchRoll = DefaultYaw / DefaultPitchRoll;

    /// <summary>The cubic blend (1 − e)·x + e·x³, with x clamped to ±1 and e to 0…1.</summary>
    public static double Apply(double x, double expo)
    {
        double e = Math.Clamp(expo, 0.0, 1.0), c = Math.Clamp(x, -1.0, 1.0);
        return (1.0 - e) * c + e * c * c * c;
    }

    /// <summary>The pitch-and-roll expo for a "Stick expo" setting, clamped to the slider's 0…<see cref="MaxStickExpo"/>.</summary>
    public static double PitchRoll(double stickExpo) => Math.Clamp(stickExpo, 0.0, MaxStickExpo);

    /// <summary>The rudder's expo for a "Stick expo" setting: the same, scaled by <see cref="YawPerPitchRoll"/>.</summary>
    public static double Yaw(double stickExpo) => PitchRoll(stickExpo) * YawPerPitchRoll;
}

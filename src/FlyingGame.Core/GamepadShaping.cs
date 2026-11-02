namespace FlyingGame.Core;

/// <summary>
/// A thumb-stick gamepad's sticks and triggers, shaped into stick, pedal and lever positions (Ty, 2026-11-02: an
/// 8BitDo Pro 2 on the iPhone, "nothing works").
///
/// Pure arithmetic, no Unity: the Unity side reads the pad and hands the raw numbers here. A thumb stick travels a
/// centimetre where a real stick travels a hand's width, so it gets a dead zone of its own (pads rest a few percent
/// off centre and drift) and HEAVY expo, so the middle of that short travel is fine control and the stops are
/// still full deflection.
///
///  • DEAD ZONE: everything inside it is exactly zero, and what is left is rescaled so the edge of the zone is 0 and
///    the stop is 1 — no step at the edge, and nothing lost at the end. The two sticks use a RADIAL zone, measured on
///    the stick's distance from centre, so a small diagonal is not cut to a pure axis the way two separate axis zones
///    would cut it.
///  • EXPO: out = (1 − e)·x + e·x³. At x = ±1 that is ±1 for every e, so full deflection is exactly full; for e in
///    0…1 the slope (1 − e) + 3e·x² is never negative, so more stick never gives less control.
///
/// Sign conventions are the PAD'S: stick right and stick up (pushed away from you) are positive, triggers 0…1.
/// The aeroplane's own senses (push = nose down) are applied by the caller, which owns <c>InvertElevator</c>.
/// </summary>
public static class GamepadShaping
{
    /// <summary>The sticks' dead zone, as a fraction of full travel.</summary>
    public const double DefaultStickDeadZone = 0.07;
    /// <summary>The triggers' dead zone: they rest at zero and do not drift, so they need less.</summary>
    public const double DefaultTriggerDeadZone = 0.05;
    /// <summary>The expo blend, 0 = linear … 1 = pure cube.</summary>
    public const double DefaultExpo = 0.65;

    /// <summary>One axis through a symmetric dead zone, rescaled so the stop is still ±1.</summary>
    public static double DeadZone(double x, double deadZone)
    {
        double dz = Math.Clamp(deadZone, 0.0, 0.95);
        double c = Math.Clamp(x, -1.0, 1.0), m = Math.Abs(c);
        if (m <= dz) return 0.0;
        return Math.Sign(c) * Math.Clamp((m - dz) / (1.0 - dz), 0.0, 1.0);
    }

    /// <summary>A stick's two axes through a RADIAL dead zone: the distance from centre is zoned and rescaled and the
    /// direction is kept. Each axis is then clamped to ±1, so a pad whose corners read past the unit circle still
    /// reaches full roll and full pitch together.</summary>
    public static (double X, double Y) RadialDeadZone(double x, double y, double deadZone)
    {
        double dz = Math.Clamp(deadZone, 0.0, 0.95);
        double m = Math.Sqrt(x * x + y * y);
        if (m <= dz) return (0.0, 0.0);
        double k = (m - dz) / (1.0 - dz) / m;
        return (Math.Clamp(x * k, -1.0, 1.0), Math.Clamp(y * k, -1.0, 1.0));
    }

    /// <summary>The cubic expo blend (1 − e)·x + e·x³, with e clamped to 0…1 and x to ±1.</summary>
    public static double Expo(double x, double expo)
    {
        double e = Math.Clamp(expo, 0.0, 1.0), c = Math.Clamp(x, -1.0, 1.0);
        return (1.0 - e) * c + e * c * c * c;
    }

    /// <summary>A stick, dead zone then expo per axis: (roll, pitch) or (rudder, —).</summary>
    public static (double X, double Y) Stick(double x, double y,
        double deadZone = DefaultStickDeadZone, double expo = DefaultExpo)
    {
        var (zx, zy) = RadialDeadZone(x, y, deadZone);
        return (Expo(zx, expo), Expo(zy, expo));
    }

    /// <summary>
    /// The rudder: right trigger is right pedal, left trigger is left pedal (RT − LT), and the right stick's X is a
    /// second way to the same pedals. Each is shaped on its own and the two are summed and clamped, so either one
    /// alone reaches full rudder and both together cannot exceed it.
    /// </summary>
    public static double Rudder(double leftTrigger, double rightTrigger, double rightStickX,
        double triggerDeadZone = DefaultTriggerDeadZone, double stickDeadZone = DefaultStickDeadZone, double expo = DefaultExpo)
    {
        double t = DeadZone(Math.Clamp(rightTrigger, 0.0, 1.0), triggerDeadZone) - DeadZone(Math.Clamp(leftTrigger, 0.0, 1.0), triggerDeadZone);
        double s = DeadZone(rightStickX, stickDeadZone);
        return Math.Clamp(Expo(t, expo) + Expo(s, expo), -1.0, 1.0);
    }

    /// <summary>
    /// Undo a dead zone that will be applied AGAIN downstream. The aeroplane already zones every control once
    /// (<c>Aircraft.ShapeAxis</c>, 2 to 5 percent from its config) because a touch pad hands it raw finger positions.
    /// A pad output that has already been zoned here would lose a second band to that, and the two together leave a
    /// numb patch round centre twice the size anyone asked for. So a non-zero output is lifted past the downstream
    /// zone by exactly the amount that zone will take off: downstream then hands back the value that went in.
    /// </summary>
    public static double PreCompensateDeadZone(double shaped, double downstreamDeadZone)
    {
        double dz = Math.Clamp(downstreamDeadZone, 0.0, 0.95), c = Math.Clamp(shaped, -1.0, 1.0);
        if (c == 0.0) return 0.0;
        return Math.Sign(c) * (dz + Math.Abs(c) * (1.0 - dz));
    }

    /// <summary>A lever driven by two held buttons: moves at <paramref name="ratePerSec"/> toward whichever is held
    /// (both or neither: stays), and stops at the given ends.</summary>
    public static double Ramp(double current, bool up, bool down, double ratePerSec, double dt, double min, double max)
    {
        double dir = (up ? 1.0 : 0.0) - (down ? 1.0 : 0.0);
        if (dir == 0.0) return current;
        return Math.Clamp(current + dir * Math.Abs(ratePerSec) * Math.Max(0.0, dt), min, max);
    }
}

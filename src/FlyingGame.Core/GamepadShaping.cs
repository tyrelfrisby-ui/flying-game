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
/// The mapping (Ty, 2026-11-02, "the right hand on the stick"): RIGHT stick = roll and pitch, LEFT stick X = rudder
/// (triggers as a second rudder), LEFT stick Y = throttle as a rate (<see cref="ThrottleRate"/>). Expo on the three
/// flight controls only. This file is shared by every platform (iOS, Mac, Quest).
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

    /// <summary>The flying stick (the RIGHT stick), dead zone then expo per axis: (roll, pitch).</summary>
    public static (double X, double Y) Stick(double x, double y,
        double deadZone = DefaultStickDeadZone, double expo = DefaultExpo)
    {
        var (zx, zy) = RadialDeadZone(x, y, deadZone);
        return (Expo(zx, expo), Expo(zy, expo));
    }

    /// <summary>
    /// The rudder: the LEFT stick's X, and the triggers as a second way to the same pedals (right trigger = right
    /// pedal, left = left: RT − LT). Each is shaped on its own and the two are summed and clamped, so either one alone
    /// reaches full rudder and both together cannot exceed it.
    /// </summary>
    public static double Rudder(double leftStickX, double leftTrigger, double rightTrigger,
        double stickDeadZone = DefaultStickDeadZone, double triggerDeadZone = DefaultTriggerDeadZone, double expo = DefaultExpo)
    {
        double s = DeadZone(leftStickX, stickDeadZone);
        double t = DeadZone(Math.Clamp(rightTrigger, 0.0, 1.0), triggerDeadZone) - DeadZone(Math.Clamp(leftTrigger, 0.0, 1.0), triggerDeadZone);
        return Math.Clamp(Expo(s, expo) + Expo(t, expo), -1.0, 1.0);
    }

    /// <summary>
    /// The throttle from the LEFT stick's Y, as a RATE, not a position. A gamepad stick springs back to centre the
    /// moment the thumb lifts; read as a position, that spring would chop the power to half every time the pilot
    /// let go to press a button. Read as a rate it behaves like a throttle: held up, the power comes up; held down,
    /// it comes off; let go, it stays where it was put. Linear with a dead zone and NO expo — this is how fast the
    /// lever moves, not a control surface, and a deflection of half should move it at half speed.
    /// Returns −1…1, a fraction of the full rate; the shoulder buttons add a fixed rate of their own.
    /// </summary>
    public static double ThrottleRate(double leftStickY, double deadZone = DefaultStickDeadZone) => DeadZone(leftStickY, deadZone);

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

    /// <summary>The throttle lever moved for one frame: the stick's rate (fraction of <paramref name="stickRatePerSec"/>)
    /// plus the shoulder buttons' (up − down at <paramref name="buttonRatePerSec"/>), then clamped to the lever's ends.
    /// Nothing asking for a change leaves the lever exactly where it was — including outside the ends, so a lever
    /// the touch pad has put in the brake band below idle is not dragged back to idle by a stick at rest.</summary>
    public static double Lever(double current, double stickRate, bool up, bool down,
        double stickRatePerSec, double buttonRatePerSec, double dt, double min, double max)
    {
        double rate = Math.Clamp(stickRate, -1.0, 1.0) * Math.Abs(stickRatePerSec)
            + ((up ? 1.0 : 0.0) - (down ? 1.0 : 0.0)) * Math.Abs(buttonRatePerSec);
        if (rate == 0.0) return current;
        return Math.Clamp(current + rate * Math.Max(0.0, dt), min, max);
    }
}

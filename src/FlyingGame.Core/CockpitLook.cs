using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Where the pilot is looking from his seat (owner 2026-10-31: "build the code necessary for looking around either by
/// buttons like a hat control on a stick or by goggles").
///
/// Pure arithmetic, no Unity: the camera reads <see cref="YawDeg"/> and <see cref="PitchDeg"/> and applies them to the
/// seat's own view point. Three ways in, and they compose rather than fight:
///
///  • A HAT, held — <see cref="Hat"/> — pans at <see cref="RateDegPerSec"/> while a direction is held, which is what a
///    four-way hat on a stick or four buttons on glass actually feel like.
///  • A HAT, tapped — <see cref="Snap"/> — jumps to a named direction, because looking over your shoulder on base is
///    one tap, not a two-second pan.
///  • GOGGLES — <see cref="FromDevice"/> — the head follows the device's own attitude, measured against wherever it
///    was when <see cref="Recentre"/> was last called, so the pilot can sit however he likes and zero it there.
///
/// The limits are a neck's, not a gimbal's: a pilot can look a long way over his shoulder, further up than down,
/// and he cannot look through the seat back.
/// </summary>
public sealed class CockpitLook
{
    /// <summary>How far round a pilot can look. 160 degrees is over the shoulder with the straps on — enough to keep
    /// the runway in sight on base — without letting the head spin round backwards through the seat.</summary>
    public const double MaxYawDeg = 160.0;
    /// <summary>Up is easy, down is blocked by your own lap and the seat pan.</summary>
    public const double MaxPitchUpDeg = 75.0;
    public const double MaxPitchDownDeg = 60.0;

    /// <summary>Degrees per second while a hat direction is held.</summary>
    public double RateDegPerSec = 140.0;

    /// <summary>How fast the view returns when nothing is asking for it and <see cref="SpringCentre"/> is on,
    /// degrees per second. A hat that self-centres is the normal flight-sim behaviour; goggles never do.</summary>
    public double CentreRateDegPerSec = 220.0;

    /// <summary>With a hat, let go and the view drifts back to straight ahead. Off by default: a pilot who looks at
    /// his passenger should be able to leave it there.</summary>
    public bool SpringCentre;

    /// <summary>Right is positive, matching a positive yaw to the right in the body frame.</summary>
    public double YawDeg { get; private set; }
    /// <summary>Up is positive.</summary>
    public double PitchDeg { get; private set; }

    /// <summary>True while the view is anywhere but straight ahead — for the HUD to say so.</summary>
    public bool LookingAway => System.Math.Abs(YawDeg) > 0.5 || System.Math.Abs(PitchDeg) > 0.5;

    private Quat? _reference;      // device attitude at the last Recentre; null until goggles are first zeroed

    public void Centre()
    {
        YawDeg = 0.0;
        PitchDeg = 0.0;
    }

    /// <summary>Pan while held. <paramref name="x"/> and <paramref name="y"/> are -1, 0 or +1: right and up positive.
    /// Called every frame with 0,0 when nothing is held, which is what lets <see cref="SpringCentre"/> work.</summary>
    public void Hat(int x, int y, double dt)
    {
        if (dt <= 0.0) return;
        if (x == 0 && y == 0)
        {
            if (!SpringCentre) return;
            double back = CentreRateDegPerSec * dt;
            YawDeg = Decay(YawDeg, back);
            PitchDeg = Decay(PitchDeg, back);
            return;
        }

        double step = RateDegPerSec * dt;
        // A diagonal must not pan faster than a straight one.
        if (x != 0 && y != 0) step *= 0.70710678;
        Set(YawDeg + System.Math.Sign(x) * step, PitchDeg + System.Math.Sign(y) * step);
    }

    private static double Decay(double v, double by) =>
        System.Math.Abs(v) <= by ? 0.0 : v - System.Math.Sign(v) * by;

    /// <summary>The eight directions a tapped hat jumps to, plus centre. Yaw and pitch in degrees.</summary>
    public enum Direction { Centre, Left, Right, Up, Down, UpLeft, UpRight, OverLeftShoulder, OverRightShoulder }

    public static (double yawDeg, double pitchDeg) Of(Direction d) => d switch
    {
        Direction.Left => (-90.0, 0.0),
        Direction.Right => (90.0, 0.0),
        Direction.Up => (0.0, 45.0),
        Direction.Down => (0.0, -35.0),
        Direction.UpLeft => (-60.0, 35.0),
        Direction.UpRight => (60.0, 35.0),
        Direction.OverLeftShoulder => (-MaxYawDeg, -10.0),
        Direction.OverRightShoulder => (MaxYawDeg, -10.0),
        _ => (0.0, 0.0),
    };

    /// <summary>Jump straight to a direction — a tapped hat, or a button on glass.</summary>
    public void Snap(Direction d)
    {
        (double y, double p) = Of(d);
        Set(y, p);
    }

    /// <summary>Zero the goggles wherever the device is now: the pilot's "straight ahead" is here. Called when he
    /// asks for it, and whenever goggles are switched on, so he can hold the device however he is sitting.</summary>
    public void Recentre(Quat deviceAttitude)
    {
        _reference = deviceAttitude.Normalized();
        Centre();
    }

    /// <summary>True once the goggles have a zero to measure against.</summary>
    public bool HasReference => _reference.HasValue;

    /// <summary>Follow the device. The attitude is whatever the platform reports; only the CHANGE since
    /// <see cref="Recentre"/> is used, so the convention of the absolute frame does not matter — which is what makes
    /// this testable without a phone, and robust to a platform that reports a different zero.</summary>
    public void FromDevice(Quat deviceAttitude)
    {
        if (!_reference.HasValue) { Recentre(deviceAttitude); return; }
        Quat delta = Quat.Multiply(_reference.Value.Conjugate(), deviceAttitude.Normalized());
        (double yaw, double pitch) = YawPitchOf(delta);
        Set(yaw, pitch);
    }

    /// <summary>Yaw (right positive) and pitch (up positive) of a rotation, in degrees. Standard aircraft extraction:
    /// yaw about z, pitch about y, with the pitch clamped before the asin so a gimbal-locked device reports a
    /// straight-up look rather than a NaN.</summary>
    public static (double yawDeg, double pitchDeg) YawPitchOf(Quat q)
    {
        double sinPitch = 2.0 * (q.W * q.Y - q.Z * q.X);
        double pitch = System.Math.Asin(System.Math.Clamp(sinPitch, -1.0, 1.0));
        double yaw = System.Math.Atan2(2.0 * (q.W * q.Z + q.X * q.Y), 1.0 - 2.0 * (q.Y * q.Y + q.Z * q.Z));
        const double toDeg = 180.0 / System.Math.PI;
        return (yaw * toDeg, pitch * toDeg);
    }

    /// <summary>Set the look directly, clamped to the neck. Everything above goes through here, so no path can
    /// produce a look a pilot could not hold.</summary>
    public void Set(double yawDeg, double pitchDeg)
    {
        YawDeg = double.IsFinite(yawDeg) ? System.Math.Clamp(yawDeg, -MaxYawDeg, MaxYawDeg) : 0.0;
        PitchDeg = double.IsFinite(pitchDeg) ? System.Math.Clamp(pitchDeg, -MaxPitchDownDeg, MaxPitchUpDeg) : 0.0;
    }
}

using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr;

/// <summary>
/// The pilot's head in VR: yaw, pitch AND roll from the phone's own attitude, one-for-one, measured against the
/// pose it was recentred at.
///
/// WHY IT BORROWS <see cref="CockpitLook"/> FOR TWO OF THE THREE. The mono look-around was shipped broken once
/// by reading angles straight off <c>reference⁻¹ · current</c> — a delta in the PHONE's own axes, so which
/// component a movement landed in depended on how the phone was held (CLAUDE.md, "A delta in the sensor's own
/// frame is not a measurement of the world"). The fix was to follow where the phone is AIMED and resolve that
/// against world vertical, and it is tested from awkward reference poses. Rather than re-derive the same signs
/// here and get them wrong a third time, VR calls that same function for yaw and pitch and only adds roll.
///
/// ROLL is the part mono never needed. The phone is in a headset in LANDSCAPE-LEFT — long edge horizontal, the
/// charge port on the pilot's right — so the top of the picture is the device's own +x axis. Roll is how far
/// that has turned about the view axis since the recentre, positive with the pilot's right ear toward his right
/// shoulder.
///
/// Gain is ALWAYS 1. The mono look-around has an adjustable gain because it is a control; VR is a window, and a
/// window that moves faster than your head is what makes people ill.
/// </summary>
public sealed class VrHead
{
    /// <summary>The neck's own limits, shared with the mono look so the two cannot disagree.</summary>
    public const double MaxYawDeg = CockpitLook.MaxYawDeg;
    public const double MaxPitchUpDeg = CockpitLook.MaxPitchUpDeg;
    public const double MaxPitchDownDeg = CockpitLook.MaxPitchDownDeg;

    /// <summary>Lateral neck flexion runs to about 45° in a healthy adult; this stops a little inside that.
    /// It is a backstop, not a working limit — in VR the view SHOULD roll with the head, and clamping it at all
    /// is a judgement call (AGENT-LOG NEEDS YOU). Nothing a seated pilot does in a headset reaches it.</summary>
    public const double MaxRollDeg = 40.0;

    public double YawDeg { get; private set; }
    public double PitchDeg { get; private set; }
    public double RollDeg { get; private set; }

    public bool HasReference => _reference.HasValue;

    private Quat? _reference;
    private double _referenceRollRad;

    /// <summary>Straight ahead and level is wherever the headset is pointing now. Called on entering VR, on the
    /// recentre gesture and from the menu.</summary>
    public void Recentre(Quat deviceAttitude)
    {
        _reference = deviceAttitude.Normalized();
        _referenceRollRad = ScreenUpRollRad(_reference.Value);
        YawDeg = PitchDeg = RollDeg = 0.0;
    }

    public void Forget()
    {
        _reference = null;
        YawDeg = PitchDeg = RollDeg = 0.0;
    }

    /// <summary>Follow the headset. The first reading with no reference becomes the reference rather than
    /// swinging the world by however the phone happened to be lying.</summary>
    public void FromDevice(Quat deviceAttitude)
    {
        Quat now = deviceAttitude.Normalized();
        if (!_reference.HasValue) { Recentre(now); return; }

        (double yaw, double pitch, bool yawUsable) = CockpitLook.ViewDeltaOf(_reference.Value, now);
        if (yawUsable && double.IsFinite(yaw)) YawDeg = System.Math.Clamp(yaw, -MaxYawDeg, MaxYawDeg);
        if (double.IsFinite(pitch)) PitchDeg = System.Math.Clamp(pitch, -MaxPitchDownDeg, MaxPitchUpDeg);

        double roll = ScreenUpRollRad(now);
        if (double.IsFinite(roll))
        {
            // Recentring while aimed at the zenith or the floor leaves no roll reference to measure against.
            // Adopt the first usable one instead of subtracting a NaN forever: without this the pilot's roll
            // never recovers, and it is silent (VrStereoTests.RecentringAtTheZenithStillGivesRollLater).
            if (!double.IsFinite(_referenceRollRad)) _referenceRollRad = roll;
            double d = Wrap(roll - _referenceRollRad) * (180.0 / System.Math.PI);
            RollDeg = System.Math.Clamp(d, -MaxRollDeg, MaxRollDeg);
        }
    }

    /// <summary>
    /// How far the TOP OF THE PICTURE has rotated about the view axis, radians, measured against world up.
    ///
    /// In the attitude frame the phone's own −z is where it is aimed and +z is vertical (CockpitLook.BackOf).
    /// Held landscape-left in a headset, the top of the picture is the device's +x. Project world up into the
    /// plane across the view axis, and the roll is the angle from that to the picture's up.
    /// </summary>
    public static double ScreenUpRollRad(Quat attitude)
    {
        Vec3 aim = CockpitLook.BackOf(attitude);
        double len = aim.Length;
        if (len < 1e-9) return 0.0;
        aim /= len;
        Vec3 screenUp = attitude.Rotate(new Vec3(1.0, 0.0, 0.0));     // landscape-left: picture up is device +x
        var worldUp = new Vec3(0.0, 0.0, 1.0);
        Vec3 levelUp = worldUp - aim * worldUp.Dot(aim);
        double lu = levelUp.Length;
        // Aimed at the zenith or the floor there is no "level up" to measure against; hold the last roll.
        if (lu < 0.02) return double.NaN;
        levelUp /= lu;
        // aim x up, NOT up x aim. Measured, not reasoned: the first version had these the other way round and
        // every roll came back with the right magnitude and the wrong sign (VrStereoTests.RollingTheHeadIsPureRoll).
        Vec3 levelRight = aim.Cross(levelUp);
        return System.Math.Atan2(screenUp.Dot(levelRight), screenUp.Dot(levelUp));
    }

    private static double Wrap(double rad)
    {
        const double twoPi = 2.0 * System.Math.PI;
        rad %= twoPi;
        if (rad > System.Math.PI) rad -= twoPi;
        if (rad <= -System.Math.PI) rad += twoPi;
        return rad;
    }
}

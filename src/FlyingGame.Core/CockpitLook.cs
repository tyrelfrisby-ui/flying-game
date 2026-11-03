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
///  • THE DEVICE ITSELF — <see cref="FromDevice"/> — the head follows the device's own attitude, measured against
///    wherever it was when <see cref="Recentre"/> was last called, so the pilot can sit however he likes and zero
///    it there, at <see cref="DeviceGain"/> degrees of view per degree of device.
///
/// They COMPOSE rather than exclude each other (owner Ty, 2026-11-01: "keep it working alongside the existing
/// touch look"). The hat and the snaps move a MANUAL offset; the device moves a separate one; the view is the sum,
/// clamped once at the end. So a pilot can snap over his shoulder and then lean the last few degrees with the
/// device, and letting go of the hat does not throw away where he is holding the phone.
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

    /// <summary>Degrees of view per degree of device (owner Ty, 2026-11-01: "an adjustable gain from 1x … up to
    /// 10x, continuous"). 1 is one-for-one and the default, which is the only setting that reads as a window
    /// rather than a control; above that the pilot trades fidelity for not having to turn his whole body.
    /// Continuous, and clamped — a gain below 1 would make the device less useful than no device, and above 10 a
    /// hand tremor reaches the stops.</summary>
    public const double MinDeviceGain = 1.0, MaxDeviceGain = 10.0;

    /// <summary>What the gain is before anybody touches the slider. Ty flew the phone at 1.0, 7.0 and 5.0 and
    /// asked for "3 to 4" (2026-11-01), so 3.5 is the middle of what he asked for.
    ///
    /// This is deliberately NOT <see cref="MinDeviceGain"/>, which is only the bottom of the slider's travel.
    /// Every default used to read the floor, so the feature shipped at 1.0 — one degree of view per degree of
    /// phone, a window rather than a control, which is honest but means turning your whole body to look at the
    /// wingtip. The two numbers answer different questions and now have different names.</summary>
    public const double DefaultDeviceGain = 3.5;

    private double _deviceGain = DefaultDeviceGain;

    public double DeviceGain
    {
        get => _deviceGain;
        // A value that is not a number is not a setting: fall back to the DEFAULT rather than to the floor,
        // which is what "we have nothing usable" means everywhere else.
        set => _deviceGain = double.IsFinite(value)
            ? System.Math.Clamp(value, MinDeviceGain, MaxDeviceGain) : DefaultDeviceGain;
    }

    // The two contributions are kept apart so they can compose: the hat and the snaps own the first, the device
    // owns the second, and only the SUM is clamped. Clamping them separately would let a pilot looking over his
    // shoulder lose the device's last few degrees silently.
    private double _manualYaw, _manualPitch;
    private double _deviceYaw, _devicePitch;

    /// <summary>Right is positive, matching a positive yaw to the right in the body frame.</summary>
    public double YawDeg => System.Math.Clamp(_manualYaw + _deviceYaw, -MaxYawDeg, MaxYawDeg);
    /// <summary>Up is positive.</summary>
    public double PitchDeg => System.Math.Clamp(_manualPitch + _devicePitch, -MaxPitchDownDeg, MaxPitchUpDeg);

    /// <summary>What the hat and the snaps have asked for, before the device is added. For the view panel, which
    /// has to know whether letting go would leave the pilot looking anywhere.</summary>
    public double ManualYawDeg => _manualYaw;
    public double ManualPitchDeg => _manualPitch;
    /// <summary>What the device is contributing right now, gain already applied.</summary>
    public double DeviceYawDeg => _deviceYaw;
    public double DevicePitchDeg => _devicePitch;

    /// <summary>True while the view is anywhere but straight ahead — for the HUD to say so.</summary>
    public bool LookingAway => System.Math.Abs(YawDeg) > 0.5 || System.Math.Abs(PitchDeg) > 0.5;

    private Quat? _reference;      // device attitude at the last Recentre; null until the device is first zeroed

    /// <summary>Let go of the hat: the MANUAL half goes back to straight ahead. The device's half is left alone on
    /// purpose — the phone is still being held where it is being held, and snatching the view back from it would
    /// be a camera moving while the pilot's hands are still. <see cref="Recentre"/> is what zeroes both.</summary>
    public void Centre()
    {
        _manualYaw = 0.0;
        _manualPitch = 0.0;
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
            _manualYaw = Decay(_manualYaw, back);
            _manualPitch = Decay(_manualPitch, back);
            return;
        }

        double step = RateDegPerSec * dt;
        // A diagonal must not pan faster than a straight one.
        if (x != 0 && y != 0) step *= 0.70710678;
        Set(_manualYaw + System.Math.Sign(x) * step, _manualPitch + System.Math.Sign(y) * step);
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

    /// <summary>RECENTRE: straight ahead is wherever the device is being held right now, and the hat's offset goes
    /// with it. This is the one call that zeroes everything, which is why the button on glass is wired to it.
    /// Called when the pilot asks, and whenever device look is switched on, so he can hold the device however he
    /// happens to be sitting.</summary>
    public void Recentre(Quat deviceAttitude)
    {
        // An attitude the device has not actually measured yet is no zero at all (see IsUnsampled): leave the
        // reference empty so the FIRST REAL reading becomes straight ahead, instead of "lying flat on a table".
        _reference = IsUnsampled(deviceAttitude) ? null : deviceAttitude.Normalized();
        _deviceYaw = 0.0;
        _devicePitch = 0.0;
        Centre();
    }

    /// <summary>Forget the device's contribution without a new attitude to zero against — for switching device look
    /// off, where leaving its last offset in would hold the view off-centre with nothing driving it.</summary>
    public void ForgetDevice()
    {
        _reference = null;
        _deviceYaw = 0.0;
        _devicePitch = 0.0;
    }

    /// <summary>
    /// TRUE WHEN THE DEVICE HAS NOT MEASURED ANYTHING YET (Ty, 2026-11-02, iPhone, build 2026.1102.1603: "cockpit view
    /// is all sky"). Unity's <c>Input.gyro.attitude</c> reads EXACTLY the identity -- the phone lying flat, screen up --
    /// from the moment the gyro is switched on until CoreMotion delivers its first sample, a frame or more later. The
    /// view used to zero against that placeholder at start-up, so a phone then held up in landscape read as its back
    /// having swung from straight down to level: about +90 degrees of pitch, times the gain (Ty's 4.9), clamped at
    /// <see cref="MaxPitchUpDeg"/> -- the view parked looking up. A real attitude is never bit-exact identity (sensor
    /// noise alone moves the last digits), and a zero-length or non-finite one is no attitude either.
    /// </summary>
    public static bool IsUnsampled(Quat q)
    {
        if (!double.IsFinite(q.X) || !double.IsFinite(q.Y) || !double.IsFinite(q.Z) || !double.IsFinite(q.W)) return true;
        double n = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
        if (n < 1e-12) return true;
        return q.X == 0.0 && q.Y == 0.0 && q.Z == 0.0;
    }

    /// <summary>True once the device has a zero to measure against.</summary>
    public bool HasReference => _reference.HasValue;

    /// <summary>Follow the device, at <see cref="DeviceGain"/> degrees of view per degree of device, kept as the
    /// device's OWN offset so whatever the hat has asked for still counts. The first call with no reference zeroes
    /// itself rather than swinging the view by however the phone happened to be lying.</summary>
    public void FromDevice(Quat deviceAttitude)
    {
        // No measurement yet: nothing to follow, and above all nothing to zero against (see IsUnsampled).
        if (IsUnsampled(deviceAttitude)) return;
        Quat now = deviceAttitude.Normalized();
        if (!_reference.HasValue)
        {
            // Adopt this reading as the zero, but do NOT disturb the hat: an implicit first frame (the gyro coming
            // up a moment after the option was switched on) must not throw away a look the pilot asked for. Only an
            // explicit Recentre clears both.
            _reference = now;
            _deviceYaw = 0.0;
            _devicePitch = 0.0;
            return;
        }
        (double yaw, double pitch, bool yawUsable) = ViewDeltaOf(_reference.Value, now);
        if (!double.IsFinite(pitch)) return;
        // Clamped to the neck here as well as in the sum: at 10x a quarter turn of the wrist would otherwise carry
        // hundreds of degrees of offset that the pilot then has to unwind before the view moves at all.
        if (yawUsable && double.IsFinite(yaw))
            _deviceYaw = System.Math.Clamp(yaw * _deviceGain, -MaxYawDeg, MaxYawDeg);
        _devicePitch = System.Math.Clamp(pitch * _deviceGain, -MaxPitchDownDeg, MaxPitchUpDeg);
    }

    /// <summary>
    /// Where the BACK of the device points, in the attitude frame's own axes. The device's local -z: with the
    /// phone lying flat and screen up — which is the attitude frame's identity — the back faces straight down.
    ///
    /// This one vector is the whole fix. The view follows where the phone is AIMED, like looking through a window,
    /// and aiming is a direction in the world, not a rotation in the phone's own axes.
    /// </summary>
    public static Vec3 BackOf(Quat deviceAttitude) => deviceAttitude.Rotate(new Vec3(0.0, 0.0, -1.0));

    /// <summary>
    /// How far the device has been turned and tilted since the reference pose, as a view yaw (right positive) and
    /// pitch (up positive) in degrees.
    ///
    /// WHY IT IS DONE THIS WAY (the bug Ty flew on build 2026.1101.0549). The old code took
    /// <c>reference⁻¹ · current</c> and read yaw and pitch straight off that quaternion's components. That delta is
    /// expressed in the DEVICE'S OWN axes, so which component a given movement landed in depended entirely on how
    /// the phone happened to be held when it was zeroed: for any natural "phone held up at an angle" pose one
    /// physical rotation spilled into two components at once (every movement came out diagonal), two different
    /// rotations landed on overlapping components (turning and rolling felt the same), and the third landed on the
    /// roll component, which was discarded (tilting did nothing). No amount of axis-swapping fixes that, because
    /// the frame it measures in moves with the phone.
    ///
    /// Resolving the AIM against world vertical instead makes the three independent by construction:
    ///  • yaw   = change in compass bearing of the aim, about world up (+z in the attitude frame)
    ///  • pitch = change in its elevation above the horizon
    ///  • roll  = rotating the phone in its own plane does not move where it is aimed, so it does nothing at all,
    ///            which is what the owner asked for and is also why PORTRAIT AND LANDSCAPE NEED NO SPECIAL CASE:
    ///            holding the phone the other way up is exactly a roll about the aim.
    ///
    /// <paramref name="yawUsable"/> is false within a few degrees of the phone lying flat or pointing at the sky,
    /// where the aim is vertical and its bearing is meaningless. The caller holds the last good yaw rather than
    /// letting the view spin while the phone is on the pilot's knee.
    /// </summary>
    public static (double yawDeg, double pitchDeg, bool yawUsable) ViewDeltaOf(Quat reference, Quat current)
    {
        Vec3 from = BackOf(reference), to = BackOf(current);
        const double toDeg = 180.0 / System.Math.PI;
        double pitch = (Elevation(to) - Elevation(from)) * toDeg;

        // Within this much of straight up or down the aim has no bearing to speak of: 0.08 is about 4.6 degrees.
        const double flat = 0.08;
        double hFrom = System.Math.Sqrt(from.X * from.X + from.Y * from.Y);
        double hTo = System.Math.Sqrt(to.X * to.X + to.Y * to.Y);
        if (hFrom < flat || hTo < flat) return (0.0, pitch, false);

        // Turning the phone to the pilot's LEFT is a positive rotation about world up, and the view should go
        // left, which is a negative yaw — hence the sign.
        double bearing = System.Math.Atan2(to.Y, to.X) - System.Math.Atan2(from.Y, from.X);
        return (-Wrap(bearing) * toDeg, pitch, true);
    }

    /// <summary>Elevation of a direction above the horizontal, radians, flat-safe for a zero vector.</summary>
    private static double Elevation(Vec3 v)
    {
        double len = System.Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-9 ? 0.0 : System.Math.Asin(System.Math.Clamp(v.Z / len, -1.0, 1.0));
    }

    /// <summary>An angle difference into (-pi, pi], so turning past due north is not a 359 degree swing.</summary>
    private static double Wrap(double rad)
    {
        const double twoPi = 2.0 * System.Math.PI;
        rad %= twoPi;
        if (rad > System.Math.PI) rad -= twoPi;
        if (rad <= -System.Math.PI) rad += twoPi;
        return rad;
    }

    /// <summary>Set the MANUAL look directly, clamped to the neck. The hat and the snaps go through here, so no
    /// path can ask for a look a pilot could not hold; the device adds its own offset on top and the sum is
    /// clamped again by <see cref="YawDeg"/> / <see cref="PitchDeg"/>.</summary>
    public void Set(double yawDeg, double pitchDeg)
    {
        _manualYaw = double.IsFinite(yawDeg) ? System.Math.Clamp(yawDeg, -MaxYawDeg, MaxYawDeg) : 0.0;
        _manualPitch = double.IsFinite(pitchDeg) ? System.Math.Clamp(pitchDeg, -MaxPitchDownDeg, MaxPitchUpDeg) : 0.0;
    }
}

using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Side-by-side stereo for a phone in a Cardboard headset: the rig, the head, the lens.
///
/// The head-tracking half is deliberately built on <see cref="CockpitLook.ViewDeltaOf"/>, which was written
/// after the mono look-around shipped broken, so the yaw/pitch signs are already pinned by
/// <c>CockpitLookTests</c>. What is new here is ROLL, and the tests that matter most are the ones that take the
/// reference from an awkward pose — the lesson from that bug was that a measurement relative to a reference is
/// only proved by references that are not tidy.
/// </summary>
public sealed class VrStereoTests
{
    // ---- synthetic device attitudes, as CoreMotion / Input.gyro.attitude reports them ------------------
    // Identity is the phone FLAT, SCREEN UP: +x across the screen, +y to the top of the screen, +z out of it
    // and therefore vertical. The back of the phone — where it is aimed — is -z.

    private static Quat About(double ax, double ay, double az, double deg)
    {
        double h = deg * System.Math.PI / 360.0, s = System.Math.Sin(h);
        return new Quat(ax * s, ay * s, az * s, System.Math.Cos(h));
    }

    /// <summary>A headset aimed somewhere, held LANDSCAPE-LEFT. <paramref name="turnRightDeg"/> and
    /// <paramref name="tiltUpDeg"/> aim it; <paramref name="rollRightDeg"/> tips the pilot's head toward his
    /// right shoulder. The 90° about x stands the phone up; the −90° about its own z is the landscape-left
    /// grip, which puts the top of the picture on the device's +x.</summary>
    private static Quat Head(double turnRightDeg, double tiltUpDeg, double rollRightDeg) =>
        Quat.Multiply(
            Quat.Multiply(About(0, 0, 1, -turnRightDeg), Quat.Multiply(About(1, 0, 0, tiltUpDeg), About(1, 0, 0, 90))),
            Quat.Multiply(About(0, 0, 1, -90), About(0, 0, 1, -rollRightDeg)));

    private static VrHead Tracking(double t = 0, double p = 0, double r = 0)
    {
        var h = new VrHead();
        h.Recentre(Head(t, p, r));
        return h;
    }

    // ---- the head: three axes, each on its own ----

    [Fact]
    public void RecentreZeroesAllThreeAxes()
    {
        VrHead h = Tracking(37, 23, 19);
        Assert.True(h.HasReference);
        Assert.Equal(0.0, h.YawDeg, 6);
        Assert.Equal(0.0, h.PitchDeg, 6);
        Assert.Equal(0.0, h.RollDeg, 6);
    }

    [Theory]
    [InlineData(20.0)]
    [InlineData(-35.0)]
    public void TurningTheHeadIsPureYaw(double turn)
    {
        VrHead h = Tracking();
        h.FromDevice(Head(turn, 0, 0));
        Assert.Equal(turn, h.YawDeg, 3);
        Assert.Equal(0.0, h.PitchDeg, 4);
        Assert.Equal(0.0, h.RollDeg, 4);
    }

    [Theory]
    [InlineData(25.0)]
    [InlineData(-25.0)]
    public void TiltingTheHeadIsPurePitch(double tilt)
    {
        VrHead h = Tracking();
        h.FromDevice(Head(0, tilt, 0));
        Assert.Equal(tilt, h.PitchDeg, 3);
        Assert.Equal(0.0, h.YawDeg, 4);
        Assert.Equal(0.0, h.RollDeg, 4);
    }

    /// <summary>The axis mono never had. Rolling the head must roll the view and move nothing else — in mono
    /// this rotation was thrown away entirely.</summary>
    [Theory]
    [InlineData(15.0)]
    [InlineData(-15.0)]
    [InlineData(30.0)]
    public void RollingTheHeadIsPureRoll(double roll)
    {
        VrHead h = Tracking();
        h.FromDevice(Head(0, 0, roll));
        Assert.Equal(roll, h.RollDeg, 3);
        Assert.Equal(0.0, h.YawDeg, 4);
        Assert.Equal(0.0, h.PitchDeg, 4);
    }

    /// <summary>THE ONE THAT WOULD CATCH IT. Recentre from a pose nobody would call tidy and every axis must
    /// still come out on its own. The mono bug hid behind tests that all zeroed from square-on.</summary>
    [Theory]
    [InlineData(41.0, 17.0, 23.0, 18.0, 0.0, 0.0)]
    [InlineData(41.0, 17.0, 23.0, 0.0, 14.0, 0.0)]
    [InlineData(41.0, 17.0, 23.0, 0.0, 0.0, 12.0)]
    [InlineData(-96.0, -28.0, -31.0, -22.0, 0.0, 0.0)]
    [InlineData(-96.0, -28.0, -31.0, 0.0, 0.0, -16.0)]
    public void FromAnyHeadPoseEachAxisIsStillPure(double t0, double p0, double r0, double dt, double dp, double dr)
    {
        VrHead h = Tracking(t0, p0, r0);
        h.FromDevice(Head(t0 + dt, p0 + dp, r0 + dr));
        Assert.Equal(dt, h.YawDeg, 2);
        Assert.Equal(dp, h.PitchDeg, 2);
        Assert.Equal(dr, h.RollDeg, 2);
    }

    /// <summary>Recentre with the headset aimed straight up — a pilot pulling it on while lying back, or
    /// setting it down on its face — and there is no level-up to measure roll against, so the reference roll
    /// comes out NaN. It must be adopted from the first usable frame instead of being subtracted forever: the
    /// failure is silent and permanent, because NaN survives every clamp.</summary>
    [Theory]
    [InlineData(90.0)]
    [InlineData(-90.0)]
    public void RecentringAtTheZenithStillGivesRollLater(double straightUpOrDown)
    {
        var h = new VrHead();
        h.Recentre(Head(0, straightUpOrDown, 0));
        Assert.True(double.IsNaN(VrHead.ScreenUpRollRad(Head(0, straightUpOrDown, 0))),
                    "the pose this test is about must be one with no level-up");

        h.FromDevice(Head(0, 0, 0));              // the first frame a pilot looks ahead again
        Assert.False(double.IsNaN(h.RollDeg));
        h.FromDevice(Head(0, 0, 20));
        Assert.False(double.IsNaN(h.RollDeg));
        Assert.Equal(20.0, h.RollDeg, 2);
    }

    [Fact]
    public void TheNeckStillLimitsTheHead()
    {
        VrHead h = Tracking();
        h.FromDevice(Head(179, 0, 0));
        Assert.InRange(h.YawDeg, -VrHead.MaxYawDeg, VrHead.MaxYawDeg);
        h.FromDevice(Head(0, 89, 0));
        Assert.InRange(h.PitchDeg, -VrHead.MaxPitchDownDeg, VrHead.MaxPitchUpDeg);
        h.FromDevice(Head(0, 0, 85));
        Assert.InRange(h.RollDeg, -VrHead.MaxRollDeg, VrHead.MaxRollDeg);
    }

    /// <summary>VR is a window, not a control: it never applies the mono look-around's 1–10× gain. Twenty
    /// degrees of head is twenty degrees of view whatever that slider says.</summary>
    [Fact]
    public void VrIsAlwaysOneForOneWhateverTheMonoGainIs()
    {
        var mono = new CockpitLook { DeviceGain = 10.0 };
        mono.Recentre(Head(0, 0, 0));
        mono.FromDevice(Head(20, 0, 0));
        Assert.Equal(CockpitLook.MaxYawDeg, mono.YawDeg, 4);      // the control runs to its stop

        VrHead vr = Tracking();
        vr.FromDevice(Head(20, 0, 0));
        Assert.Equal(20.0, vr.YawDeg, 3);                          // the window does not
    }

    // ---- the rig ----

    [Theory]
    [InlineData(0.063)]
    [InlineData(0.055)]
    [InlineData(0.075)]
    public void TheEyesAreExactlyAnIpdApartAndStraddleTheSeatEye(double ipd)
    {
        var seat = new Vec3(0.5, 0.0, -0.3);
        (Vec3 l, Vec3 r) = VrStereoRig.EyePositions(seat, new Vec3(0, 1, 0), ipd);
        Assert.Equal(ipd, VrStereoRig.Separation(l, r), 9);
        Vec3 mid = VrStereoRig.Midpoint(l, r);
        Assert.Equal(seat.X, mid.X, 9);
        Assert.Equal(seat.Y, mid.Y, 9);
        Assert.Equal(seat.Z, mid.Z, 9);
    }

    /// <summary>Change seat and BOTH eyes move: they hang off whichever seat is active, never off the front and
    /// rear eye points, which are two different pilots a metre apart.</summary>
    [Fact]
    public void ChangingSeatMovesBothEyes()
    {
        var right = new Vec3(0, 1, 0);
        (Vec3 l1, Vec3 r1) = VrStereoRig.EyePositions(new Vec3(-1.960, 0, -0.305), right, 0.063);
        (Vec3 l2, Vec3 r2) = VrStereoRig.EyePositions(new Vec3(-1.193, 0, -0.293), right, 0.063);
        Assert.True((l2 - l1).Length > 0.7 && (r2 - r1).Length > 0.7);
        Assert.Equal(0.063, VrStereoRig.Separation(l2, r2), 9);
    }

    /// <summary>Head roll tilts the BASELINE. If the two eyes stayed level while the head rolled, the stereo
    /// would shear and stop fusing — this is the test that says the baseline rides the head.</summary>
    [Fact]
    public void HeadRollTiltsTheBaseline()
    {
        var seat = new Vec3(0, 0, 0);
        (Vec3 l0, Vec3 r0) = VrStereoRig.EyePositions(seat, new Vec3(0, 1, 0), 0.063);
        double rollRad = 30.0 * System.Math.PI / 180.0;
        var tilted = new Vec3(0, System.Math.Cos(rollRad), -System.Math.Sin(rollRad));
        (Vec3 l1, Vec3 r1) = VrStereoRig.EyePositions(seat, tilted, 0.063);

        Assert.Equal(0.0, (r0 - l0).Z, 9);                                  // level head: a level baseline
        Assert.True(System.Math.Abs((r1 - l1).Z) > 0.01, "the baseline did not tilt with the head");
        Assert.Equal(0.063, VrStereoRig.Separation(l1, r1), 9);             // ...and it is still an IPD long
    }

    // ---- the lens ----

    /// <summary>The Cardboard v2 profile, as literals. Google publishes 39.3 mm / 63.9 mm / 35 mm and
    /// k1 0.33582564 / k2 0.55348791; the brief's 0.039 / 0.064 / 0.34 / 0.55 are those rounded.
    /// https://support.google.com/cardboard/manufacturers/answer/6321873</summary>
    [Fact]
    public void TheShippedProfileIsGenericCardboardV2()
    {
        string path = Path.Combine(RepoRoot(), "configs", "vr-cardboard.json");
        Assert.True(File.Exists(path), path + " is missing");
        VrLensConfig c = VrLensConfig.LoadFromFile(path);
        Assert.Equal(0.0639, c.InterLensDistanceM, 9);
        Assert.Equal(0.0393, c.ScreenToLensDistanceM, 9);
        Assert.Equal(0.035, c.TrayToLensDistanceM, 9);
        Assert.Equal("BOTTOM", c.VerticalAlignment);
        Assert.Equal(0.33582564, c.K1, 9);
        Assert.Equal(0.55348791, c.K2, 9);
        Assert.Equal(60.0, c.FovLeftDeg, 9);
        Assert.Equal(60.0, c.FovRightDeg, 9);
        Assert.Equal(60.0, c.FovBottomDeg, 9);
        Assert.Equal(60.0, c.FovTopDeg, 9);
        Assert.Equal(0.063, c.IpdM, 9);
    }

    [Fact]
    public void AMissingOrSillyProfileStillGivesAUsableHeadset()
    {
        VrLensConfig c = VrLensConfig.LoadFromJson("{}").Sane();
        Assert.Equal(0.0639, c.InterLensDistanceM, 9);
        var mad = new VrLensConfig { IpdM = 9.0, RenderScale = 40.0, ScreenToLensDistanceM = -1.0 }.Sane();
        Assert.InRange(mad.IpdM, VrLensConfig.MinIpdM, VrLensConfig.MaxIpdM);
        Assert.InRange(mad.RenderScale, 0.5, 1.0);
        Assert.True(mad.ScreenToLensDistanceM > 0.0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.4)]
    [InlineData(0.9)]
    [InlineData(1.3)]
    public void DistortionAndItsInverseRoundTrip(double r)
    {
        const double k1 = 0.33582564, k2 = 0.55348791;
        double there = VrOptics.Distort(r, k1, k2);
        double back = VrOptics.Undistort(there, k1, k2);
        Assert.Equal(r, back, 7);
        if (r > 0) Assert.True(there > r, "a barrel lens magnifies: r' must exceed r");
    }

    [Fact]
    public void DistortionIsOddAndFixesTheAxis()
    {
        const double k1 = 0.33582564, k2 = 0.55348791;
        Assert.Equal(0.0, VrOptics.Distort(0.0, k1, k2), 12);
        Assert.Equal(0.0, VrOptics.Undistort(0.0, k1, k2), 12);
        Assert.Equal(-VrOptics.Distort(0.5, k1, k2), VrOptics.Distort(-0.5, k1, k2), 12);
        Assert.Equal(-0.3, VrOptics.Undistort(VrOptics.Distort(-0.3, k1, k2), k1, k2), 7);
    }

    /// <summary>A phone's screen, through the v2 lens: every edge has a real angle, the lens's own 60° is never
    /// exceeded, and the view is wider OUTBOARD than inboard, because the lens axis sits outboard of the centre
    /// of its own half of the screen.</summary>
    [Fact]
    public void TheScreenThroughTheLensGivesAnAsymmetricFrustum()
    {
        VrLensConfig c = new VrLensConfig().Sane();
        const double wM = 0.1430, hM = 0.0645;              // about an iPhone 17 Pro in landscape
        var (l, r, b, t) = VrOptics.EyeFovDeg(c, wM, hM);
        foreach (double a in new[] { l, r, b, t })
        {
            Assert.True(double.IsFinite(a), "a non-finite frustum edge");
            Assert.InRange(a, 1.0, 60.0);                    // never beyond what the lens can show
        }
        Assert.True(l > r, $"outboard {l:F1}° should see more than inboard {r:F1}°");
    }

    [Fact]
    public void TheLensSitsWhereTheAlignmentSaysItDoes()
    {
        const double hM = 0.0645;
        Assert.Equal(0.035, VrOptics.VerticalOffsetM(new VrLensConfig { VerticalAlignment = "BOTTOM" }, hM), 9);
        Assert.Equal(hM * 0.5, VrOptics.VerticalOffsetM(new VrLensConfig { VerticalAlignment = "CENTER" }, hM), 9);
        Assert.Equal(hM - 0.035, VrOptics.VerticalOffsetM(new VrLensConfig { VerticalAlignment = "TOP" }, hM), 9);
    }

    [Theory]
    [InlineData(1.0, 1133, 1488)]
    [InlineData(0.85, 963, 1265)]
    [InlineData(0.5, 567, 744)]
    public void TheEyeTargetFollowsTheRenderScale(double scale, int w, int h)
    {
        var c = new VrLensConfig { RenderScale = scale };
        (int gotW, int gotH) = VrOptics.EyeTargetPixels(c, 2266, 1488);    // iPad mini landscape
        Assert.Equal(w, gotW);
        Assert.Equal(h, gotH);
    }

    [Fact]
    public void EachEyeGetsItsOwnHalfOfTheScreen()
    {
        var (lx, _, lw, lh) = VrOptics.EyeViewportPixels(false, 2266, 1488);
        var (rx, _, rw, _) = VrOptics.EyeViewportPixels(true, 2266, 1488);
        Assert.Equal(0, lx);
        Assert.Equal(1133, rx);
        Assert.Equal(1133, lw);
        Assert.Equal(1133, rw);
        Assert.Equal(1488, lh);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs"))) d = d.Parent;
        Assert.True(d != null, "could not find the repo root above " + AppContext.BaseDirectory);
        return d!.FullName;
    }
}

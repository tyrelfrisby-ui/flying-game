using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlightTests;

/// <summary>
/// The Pitts S-2B canopy v2 (Ty 2026-11-05): a black lever in each cockpit on one cable; the canopy's frame stopped by the
/// rubber-covered tab on the right upper wing; the jettison freeing the track so it runs past the tab and leaves.
/// Nothing here sets a stop angle: where the canopy stops comes out of the frame and the tab (CanopyStopTab).
/// </summary>
public class PittsCanopyTabTests
{
    private const double Rho0 = 1.225;
    private static readonly Vec3 Level1G = new(0, -9.80665, 0);
    private static double Q(double kt) { double v = kt * 0.514444; return 0.5 * Rho0 * v * v; }
    private static CanopyDynamics Pitts() => new(CanopySpec.For("pitts-s2b-like"));

    private static CanopyEvents Run(CanopyDynamics c, double q, bool locked, double seconds, Vec3 hand = default)
    {
        CanopyEvents all = CanopyEvents.None;
        for (int i = 0; i < (int)(seconds / 0.005); i++) all |= c.Step(0.005, q, Level1G, locked, hand, Vec3.Zero);
        return all;
    }

    /// <summary>A hand on the free edge swinging it steadily from 0 to <paramref name="toDeg"/> over the run.</summary>
    private static CanopyEvents Swing(CanopyDynamics c, double toDeg, double seconds, double z)
    {
        CanopyEvents all = CanopyEvents.None;
        int n = (int)(seconds / 0.005);
        for (int i = 0; i < n; i++)
        {
            double a = toDeg * System.Math.Min(1.0, (i + 1.0) / (0.75 * n)) * System.Math.PI / 180.0;
            var hand = new Vec3(-0.716 * System.Math.Cos(a), 0.716 * System.Math.Sin(a), z);
            all |= c.Step(0.005, 0.0, Level1G, false, hand, Vec3.Zero);
        }
        return all;
    }

    [Fact]
    public void TheNumbersTyGave()
    {
        // 3/8 in tube, 12 mm of rubber on the 1/4 in stock (Codex's model), 4 in normal slide, 2 in more jettisoned (E).
        Assert.Equal(0.0047625, CanopyStopTab.TubeRadiusM, 9);
        Assert.Equal(0.006, CanopyStopTab.TabRadiusDefaultM, 9);
        CanopySpec s = CanopySpec.For("pitts-s2b-like");
        Assert.Equal(0.1016, s.SlideM, 9);
        Assert.Equal(0.1524, s.TrackM, 9);
        Assert.NotNull(s.StopTab);
        Assert.Null(CanopySpec.For("extra-300-like").StopTab);   // the Extra keeps its hinge stop
    }

    [Fact]
    public void AfterTheNormalSlideTheFrameMeetsTheTabAndNothingElseStopsIt()
    {
        CanopyStopTab t = CanopySpec.For("pitts-s2b-like").StopTab!;
        // Slid 4 in, the front hoop comes over onto the rubber at 96.8° (Codex placed the rubber for 100° against the
        // drawn hoop; the frame here follows the hoop's measured centre-line).
        Assert.InRange(t.FirstTouchDeg(0.1016), 96.6, 97.0);
        // The rubber is 28 mm long fore-aft: the hoop only finds it between about 3.5 and 4.75 in of slide. At the
        // jettison's 6 in it has passed behind it: no touch all the way to 180°. Not slid, it is ahead of it.
        Assert.True(double.IsNaN(t.FirstTouchDeg(0.1270)));
        Assert.True(double.IsNaN(t.FirstTouchDeg(0.1524)));
        Assert.True(double.IsNaN(t.FirstTouchDeg(0.0)));
    }

    [Fact]
    public void UnlatchedAt60KiasItFlingsOpenAndRestsOnTheTab()
    {
        CanopyDynamics c = Pitts();
        CanopyEvents ev = Run(c, Q(60), locked: false, 3.0);
        Assert.True((ev & CanopyEvents.FlippedOpen) != 0);
        Assert.True((ev & CanopyEvents.HitStop) != 0);
        Assert.False(c.Departed);
        Assert.Equal(0.1016, c.SlideM, 6);
        Assert.InRange(c.AngleDeg, 96.6, 97.6);
        Assert.True(c.TabForceN > 50.0, $"resting on the tab with {c.TabForceN:F0} N");
        Assert.True(c.PeakTabForceN > 5000.0, $"the slam: {c.PeakTabForceN:F0} N");
        Assert.True(c.PeakFittingLoadN < c.Spec.FittingStrengthN);
    }

    [Fact]
    public void OnTheGroundAHandOpensItOntoTheTabAndNoFurther()
    {
        // Slid 4 in by hand, then swung over by a hand that keeps pushing (it aims at 175°): the tab holds it.
        CanopyDynamics c = Pitts();
        var edge = new Vec3(-0.716, 0.0, 0.0);
        c.Grab(edge, Vec3.Zero);
        c.Step(0.005, 0.0, Level1G, false, edge + new Vec3(0, 0, -0.2), Vec3.Zero);   // a hand pulled 20 cm aft
        Assert.Equal(0.1016, c.SlideM, 6);                                            // the track's normal stop
        Swing(c, 160.0, 3.0, -0.2);                                                   // aimed well past the tab
        Assert.False(c.Departed);
        Assert.InRange(c.AngleDeg, 96.6, 98.0);
        Assert.True(c.TabForceN > 0.0);
    }

    [Fact]
    public void JettisonedOnTheGroundItSlidesFurtherByHandMissesTheTabAndComesOff()
    {
        CanopyDynamics c = Pitts();
        c.ReleaseHingePins();
        var edge = new Vec3(-0.716, 0.0, 0.0);
        c.Grab(edge, Vec3.Zero);
        c.Step(0.005, 0.0, Level1G, false, edge + new Vec3(0, 0, -0.5), Vec3.Zero);   // pulled right back
        Assert.Equal(0.1524, c.SlideM, 6);                                            // the whole freed track
        CanopyEvents ev = Swing(c, 160.0, 3.0, -0.5);
        Assert.True(c.Departed);
        Assert.Equal(CanopyDeparture.Jettisoned, c.DepartedBy);
        Assert.Equal(0.0, c.PeakTabForceN, 9);
        Assert.True((ev & CanopyEvents.Departed) != 0);
    }

    [Fact]
    public void TheLeverAndTheCanopyTogether()
    {
        // The Bridge's order: latch first against the canopy's state from the last frame, then the canopy.
        var l = new PittsCanopyLatch();
        CanopyDynamics c = Pitts();
        for (int i = 0; i < 100; i++) { l.Update(c.Shut); c.Step(0.005, Q(60), Level1G, l.Locked, Vec3.Zero, Vec3.Zero); }
        Assert.True(l.Locked);
        Assert.Equal(0.0, c.AngleDeg, 9);
        // The rear pilot pulls his lever back: the hook comes out for both, and at 60 KIAS it goes onto the tab.
        l.Grab(PittsLatchLever.Rear);
        l.Follow(PittsLatchLever.Rear, 90.0);
        for (int i = 0; i < 600; i++) { l.Update(c.Shut); c.Step(0.005, Q(60), Level1G, l.Locked, Vec3.Zero, Vec3.Zero); }
        Assert.False(l.Locked);
        Assert.InRange(c.AngleDeg, 96.6, 97.6);
        // Lever forward again with the canopy open: the hook swings in free air, nothing is locked.
        l.Follow(PittsLatchLever.Rear, 0.0);
        l.Update(c.Shut);
        Assert.True(l.HookHome);
        Assert.False(l.Locked);
    }

    [Fact]
    public void SegmentDistanceIsExact()
    {
        // Two crossing segments 1 cm apart, and parallel ones.
        CanopyStopTab.ClosestSegSeg(new Vec3(-1, 0, 0), new Vec3(1, 0, 0), new Vec3(0.3, 0.01, -1), new Vec3(0.3, 0.01, 1),
                                    out Vec3 a, out Vec3 b);
        Assert.Equal(0.01, (a - b).Length, 9);
        Assert.Equal(0.3, a.X, 9);
        CanopyStopTab.ClosestSegSeg(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(2, 0.5, 0), new Vec3(3, 0.5, 0),
                                    out a, out b);
        Assert.Equal(new Vec3(1, 0, 0).X, a.X, 9);
        Assert.Equal(2.0, b.X, 9);
    }

    [Fact]
    public void TheScriptedCanopyRestsWhereThePhysicalOneDoes()
    {
        // iOS / Mac (no hands): the 0..1 command slides the first fifth, then swings to the tab's angle.
        CanopySpec p = CanopySpec.For("pitts-s2b-like");
        (double s0, double o0) = CanopyDynamics.ScriptedRig(p, 0.1);
        Assert.Equal(0.0508 / 0.1524, s0, 9);
        Assert.Equal(0.0, o0, 9);
        (double s1, double o1) = CanopyDynamics.ScriptedRig(p, 1.0);
        Assert.Equal(0.1016 / 0.1524, s1, 9);
        Assert.InRange(o1 * 175.0, 96.6, 97.0);
        // The Extra: the command is the swing.
        (double sx, double ox) = CanopyDynamics.ScriptedRig(CanopySpec.For("extra-300-like"), 0.6);
        Assert.Equal(0.0, sx, 9);
        Assert.Equal(0.6, ox, 9);
    }
}

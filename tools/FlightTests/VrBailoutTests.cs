using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Phase 2 of the headset cockpit: the hands-on bail-out (src/FlyingGame.Core/Vr: CanopyHinge, Harness, HeadsetCord,
/// Egress, Ripcord). Every expected value is a literal worked by hand from the equation in the comment beside it
/// (CLAUDE.md: "A test that reads the file it is checking proves nothing"). Cockpit frame: +x right, +y up, +z nose.
/// </summary>
public sealed class VrBailoutTests
{
    private static readonly Vec3 Level1G = new(0, -9.80665, 0);
    private static readonly Vec3 Hinge = new(0.375, 0.0, 0.0);   // Extra: right sill, w = 0.75 to the free edge
    private const double Rho0 = 1.225;
    private static double Q(double kt) { double v = kt * 0.514444; return 0.5 * Rho0 * v * v; }

    // ---- the canopy: moments and the flip-open threshold ----

    // Extra: w 0.75, L 2.0, A = 0.8·0.75·2.0 = 1.2 m², m 14 kg.
    //   M_aero / q = (w/2)·ΔCp·A = 0.375·0.5·1.2 = 0.225 m³
    //   M_g (shut, level 1 g) = −(w/2)·m·g = −0.375·14·9.80665 = −51.4849 N·m
    //   flips open alone at q = 51.4849 / 0.225 = 228.82 Pa (19.33 m/s, 37.6 kt)
    //   held by one hand (200 N at the edge, 150 N·m): q = 201.4849 / 0.225 = 895.49 Pa (38.24 m/s, 74.3 kt)
    [Fact]
    public void ExtraCanopyMomentsAndFlipOpenThreshold()
    {
        CanopySpec s = CanopySpec.For("extra-300-like");
        Assert.Equal(1.2, s.AreaM2, 6);
        Assert.Equal(0.225 * 1000.0, CanopyDynamics.AeroMomentNm(s, 1000.0), 6);
        Assert.Equal(-51.4849, CanopyDynamics.GravityMomentNm(s, Level1G, 0.0), 3);
        Assert.Equal(228.82, CanopyDynamics.FlipOpenQ(s, 0.0), 1);
        Assert.Equal(895.49, CanopyDynamics.FlipOpenQ(s, 200.0), 1);
    }

    // Pitts: w 0.70, L 1.5, A = 0.84 m², m 9: alone q = 30.8909 / 0.147 = 210.14 Pa; held: 170.8909 / 0.147 = 1162.52 Pa.
    [Fact]
    public void PittsFlipOpenThreshold()
    {
        CanopySpec s = CanopySpec.For("pitts-s2b-like");
        Assert.Equal(0.84, s.AreaM2, 6);
        Assert.Equal(210.14, CanopyDynamics.FlipOpenQ(s, 0.0), 1);
        Assert.Equal(1162.52, CanopyDynamics.FlipOpenQ(s, 200.0), 1);
    }

    private static CanopyEvents Run(CanopyDynamics c, double q, bool locked, double seconds, Vec3 hand = default)
    {
        CanopyEvents all = CanopyEvents.None;
        for (int i = 0; i < (int)(seconds / 0.005); i++) all |= c.Step(0.005, q, Level1G, locked, hand, Hinge);
        return all;
    }

    [Fact]
    public void UnlatchedExtraCanopyFlipsOpenAt100KiasAndStaysShutAt30()
    {
        // 100 KIAS: q = 1621 Pa, seven times the 229 Pa it takes. 30 kt: q = 145.9 Pa, under it.
        var fast = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        CanopyEvents ev = Run(fast, Q(100), locked: false, 1.0);
        Assert.Equal(100.0, fast.AngleDeg, 0);
        Assert.True((ev & CanopyEvents.FlippedOpen) != 0);
        Assert.Equal(1.0, fast.Open01, 6);

        var slow = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Run(slow, Q(30), locked: false, 2.0);
        Assert.Equal(0.0, slow.AngleDeg, 6);
    }

    [Fact]
    public void LatchedCanopyNeverMoves()
    {
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Run(c, Q(200), locked: true, 1.0);
        Assert.Equal(0.0, c.AngleDeg, 9);
    }

    [Fact]
    public void AHandHoldsItAt60KtAndLosesItAt100()
    {
        // The hand on the free edge, shut: 0.75 m inboard of the hinge, level with it.
        var edge = new Vec3(Hinge.X - 0.75, 0.0, 0.0);
        // 60 kt: q = 583.6, M_aero = 131.3 − 51.5 = 79.8 N·m to hold, under the hand's 150.
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        c.Grab(edge, Hinge);
        CanopyEvents ev = Run(c, Q(60), locked: false, 1.0, edge);
        Assert.True(c.Held);
        Assert.True(c.AngleDeg < 2.0, $"held at {c.AngleDeg:F1}°");
        Assert.True((ev & CanopyEvents.TornFromHand) == 0);
        // 100 kt: M_aero = 364.7 − 51.5 = 313 N·m, twice what the hand has: it is torn out and flips open.
        var d = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        d.Grab(edge, Hinge);
        ev = Run(d, Q(100), locked: false, 1.0, edge);
        Assert.False(d.Held);
        Assert.True((ev & CanopyEvents.TornFromHand) != 0);
        Assert.Equal(100.0, d.AngleDeg, 0);
    }

    [Fact]
    public void PittsMustSlideAftBeforeItCanHinge()
    {
        var c = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        var hand = new Vec3(-0.35, 0.0, 0.0);
        // Unlatched at 100 kt but still forward on its track: the mushroom buttons hold it down.
        Run(c, Q(100), locked: false, 0.5, hand);
        Assert.Equal(0.0, c.AngleDeg, 9);
        // Hand on the rail, 5.08 cm aft: half the 10.16 cm slide → canopy_open 0.2·0.5 = 0.1.
        c.Grab(hand, Hinge);
        c.Step(0.005, 0.0, Level1G, false, new Vec3(-0.35, 0.0, -0.0508), Hinge);
        Assert.Equal(0.0508, c.SlideM, 6);
        Assert.Equal(0.1, c.Open01, 6);
        // All the way aft, let go at 100 kt: now it flips (canopy_open 0.2 + 0.8·100/100 = 1).
        c.Step(0.005, 0.0, Level1G, false, new Vec3(-0.35, 0.0, -0.12), Hinge);
        Assert.True(c.SlidComplete);
        c.Release();
        Run(c, Q(100), locked: false, 1.0);
        Assert.Equal(100.0, c.AngleDeg, 0);
        Assert.Equal(1.0, c.Open01, 6);
    }

    [Fact]
    public void OpenCanopyDragArea()
    {
        // ΔCdA = 0.20 · min(1, β/60°): 30° → 0.10 m², 90° → 0.20 m²; at 100 kt (1621 Pa) that is 162 N and 324 N.
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Assert.Equal(0.0, c.DragAreaM2, 9);
        Run(c, Q(100), locked: false, 1.0);
        Assert.Equal(0.20, c.DragAreaM2, 6);
    }

    // ---- the harness ----

    [Fact]
    public void ExtraHasTwoReleasesAndThePilotStaysAttachedUntilBoth()
    {
        SeatHarness h = SeatHarness.For("extra-300-like");
        Assert.Equal(2, h.Releases.Count);
        Assert.True(h.Latched);
        HarnessRelease top = h.Releases[0], lap = h.Releases[1];
        // Lift axis (0, 0.6, 0.8): 3.9 cm along it is not enough, 4.1 cm is.
        top.Grab(Vec3.Zero, Quat.Identity);
        Assert.False(top.Follow(new Vec3(0, 0.6, 0.8) * 0.039, Quat.Identity));
        Assert.Equal(0.975, top.Travel01, 6);
        Assert.True(top.Follow(new Vec3(0, 0.6, 0.8) * 0.041, Quat.Identity));
        Assert.True(h.Latched);   // the lap belt still holds him
        // Sideways 10 cm moves a lever nothing.
        lap.Grab(Vec3.Zero, Quat.Identity);
        Assert.False(lap.Follow(new Vec3(0.10, 0, 0), Quat.Identity));
        Assert.True(lap.Follow(new Vec3(0, 0.6, 0.8) * 0.05, Quat.Identity));
        Assert.False(h.Latched);
        Assert.Equal(1.0, h.Harness01, 9);
    }

    private static Quat AxisAngle(Vec3 axis, double deg)
    {
        double h = deg * Math.PI / 360.0;
        Vec3 a = axis.Normalized() * Math.Sin(h);
        return new Quat(a.X, a.Y, a.Z, Math.Cos(h));
    }

    [Fact]
    public void RotaryReleaseTurnsOnTwistNotSwing()
    {
        var r = new HarnessRelease("main", HarnessReleaseKind.RotaryTurn, new Vec3(0, 0, 1));
        r.Grab(Vec3.Zero, Quat.Identity);
        Assert.False(r.Follow(Vec3.Zero, AxisAngle(new Vec3(1, 0, 0), 80.0)));   // a swing about x: no twist about z
        Assert.Equal(0.0, r.Travel01, 6);
        Assert.False(r.Follow(Vec3.Zero, AxisAngle(new Vec3(0, 0, 1), 59.0)));
        Assert.True(r.Follow(Vec3.Zero, AxisAngle(new Vec3(0, 0, -1), 61.0)));   // either way round
        Assert.Equal(61.0, Math.Abs(HarnessRelease.TwistAboutDeg(Quat.Identity, AxisAngle(new Vec3(0, 0, 1), 61.0), new Vec3(0, 0, 1))), 6);
    }

    [Fact]
    public void LetGoShortOfReleaseSpringsBack()
    {
        var r = new HarnessRelease("lap", HarnessReleaseKind.LiftLever, new Vec3(0, 1, 0));
        r.Grab(Vec3.Zero, Quat.Identity);
        r.Follow(new Vec3(0, 0.03, 0), Quat.Identity);
        r.Release();
        Assert.False(r.Released);
        Assert.Equal(0.0, r.Travel01, 9);
    }

    // ---- the headset cord ----

    [Fact]
    public void CordTensionIsZeroSlackAndStiffnessTimesStretchTaut()
    {
        // T = 2000 N/m · (d − 1.5 m): 1.49 → 0; 1.51 → 20 N.
        Assert.Equal(0.0, HeadsetCord.Tension(1.49, 1.5), 9);
        Assert.Equal(20.0, HeadsetCord.Tension(1.51, 1.5), 6);
    }

    [Fact]
    public void LeavingPluggedInPullsThePlugOutAtItsRetentionForce()
    {
        var c = new HeadsetCord();
        Assert.Equal(CordEvents.None, c.Step(1.0));
        Assert.Equal(CordEvents.Taut, c.Step(1.505));                 // 10 N: taut, holding
        Assert.Equal(10.0, c.TensionN, 6);
        CordEvents ev = c.Step(1.52);                                 // 40 N asked, the plugs give at 25
        Assert.True((ev & CordEvents.PulledOut) != 0);
        Assert.Equal(CordState.Unplugged, c.State);
        Assert.Equal(25.0, c.PeakTensionN, 6);
        Assert.Equal(0.0, (c.Step(3.0) == CordEvents.None ? c.TensionN : -1), 9);   // nothing to pull on now
    }

    [Fact]
    public void ALockingPlugTakesTheHeadsetOffInstead()
    {
        var c = new HeadsetCord(plugN: 200.0);   // a LEMO that does not let go: the 40 N headset goes first
        CordEvents ev = c.Step(1.6);
        Assert.True((ev & CordEvents.HeadsetPulledOff) != 0);
        Assert.Equal(CordState.HeadsetOff, c.State);
        Assert.Equal(40.0, c.PeakTensionN, 6);
    }

    [Fact]
    public void TheYankOnTheHeadIsBounded()
    {
        // offset = T / 1000 N/m, capped at 4 cm: 25 N → 2.5 cm; 100 N → 4 cm.
        Assert.Equal(0.025, HeadsetCord.YankM(25.0), 9);
        Assert.Equal(0.04, HeadsetCord.YankM(100.0), 9);
        Assert.Equal(0.0, HeadsetCord.YankM(-5.0), 9);
    }

    [Fact]
    public void UnplugByHandIsThreeCentimetresAlongTheJackAxis()
    {
        var c = new HeadsetCord();
        var axis = new Vec3(-1, 0, 0);
        c.Grab(Vec3.Zero, axis);
        Assert.False(c.Follow(new Vec3(0, 0.05, 0)));   // across the jack: nothing
        Assert.False(c.Follow(new Vec3(-0.029, 0, 0)));
        Assert.True(c.Follow(new Vec3(-0.031, 0, 0)));
        Assert.Equal(CordState.Unplugged, c.State);
    }

    [Fact]
    public void RopeKeepsItsLength()
    {
        var r = new VerletRope(16, 1.5, Vec3.Zero, new Vec3(0, -1, 0));
        for (int i = 0; i < 400; i++) r.Step(1.0 / 72.0, Level1G, Vec3.Zero, new Vec3(1.0, 0, 0), true);
        Assert.InRange(r.Length, 1.47, 1.53);
        Assert.Equal(1.0, r.Points[^1].X, 9);
        Assert.True(r.Points[8].Y < -0.5, "a 1.5 m cord between points 1 m apart sags");
    }

    // ---- freefall ----

    [Fact]
    public void BellyToEarthTerminalIs53Point6AtSeaLevel()
    {
        // CdA = 2·105·9.80665 / (1.225·53.6²) = 0.585160 m² (back-solved from 120 mph); v_t = √(2mg/(ρ CdA)).
        Assert.Equal(0.585160, SkydiverAero.CdABelly, 5);
        Assert.Equal(53.6, JumperAero.TerminalSpeed(SkydiverAero.CdABelly, 105.0, Rho0), 3);
        // Tumbling 0.7 m²: √(2059.40 / 0.8575) = 49.01 m/s. ISA 3000 m (ρ 0.9093) belly: 62.21 m/s.
        Assert.Equal(49.01, JumperAero.TerminalSpeed(JumperAero.TumblingCdA, 105.0, Rho0), 2);
        Assert.Equal(62.21, JumperAero.TerminalSpeed(SkydiverAero.CdABelly, 105.0, 0.9093), 2);
        Assert.Equal(0.7, JumperAero.CdA(1.0), 9);
        Assert.Equal(0.585160, JumperAero.CdA(5.0), 5);
    }

    [Fact]
    public void AnExitAt100KtSettlesToTerminal()
    {
        // Leave horizontally at 51.4 m/s; after 30 s the speed is the belly terminal 53.6 within 0.5 %.
        double vx = 51.4, vy = 0.0, t = 0.0, dt = 0.002;
        while (t < 30.0)
        {
            double sp = Math.Sqrt(vx * vx + vy * vy), k = 0.5 * Rho0 * JumperAero.CdA(t) / 105.0;
            vx -= k * sp * vx * dt;
            vy += (-9.80665 - k * sp * vy) * dt;
            t += dt;
        }
        Assert.InRange(Math.Sqrt(vx * vx + vy * vy), 53.6 * 0.995, 53.6 * 1.005);
    }

    [Fact]
    public void ExitVelocityIsTheAeroplanesPlusThePush()
    {
        // 51.4 m/s ahead, rolling right at 1 rad/s (ω along the nose: +z), pilot 0.5 m above the reference:
        // ω × r = (0,0,1) × (0,0.5,0) = (−0.5, 0, 0). Left exit: push −1 m/s on x, +0.5 up.
        Vec3 v = JumperAero.ExitVelocity(new Vec3(0, 0, 51.4), new Vec3(0, 0, 1), new Vec3(0, 0.5, 0),
            new Vec3(1, 0, 0), new Vec3(0, 1, 0), ExitSide.Left);
        Assert.Equal(-1.5, v.X, 9);
        Assert.Equal(0.5, v.Y, 9);
        Assert.Equal(51.4, v.Z, 9);
    }

    // ---- climbing out ----

    [Fact]
    public void StrappedInYouCannotRise()
    {
        var c = new ClimbOut();
        c.Grab(new Vec3(-0.35, -0.4, 0), ExitSide.Left);
        ClimbEvents ev = c.Step(new Vec3(-0.35, -0.7, 0), strapped: true, canopyClear: true, q: 0);
        Assert.Equal(0.02, c.RiseM, 9);
        Assert.True((ev & ClimbEvents.Blocked) != 0);
        c.Step(new Vec3(-0.35, -0.7, 0), strapped: false, canopyClear: false, q: 0);
        Assert.Equal(0.08, c.RiseM, 9);   // head against the shut canopy
    }

    [Fact]
    public void OnTheGroundYouClimbOverAndLetGo()
    {
        var c = new ClimbOut();
        c.Grab(new Vec3(-0.35, -0.4, 0), ExitSide.Left);
        c.Step(new Vec3(-0.35, -0.95, 0), strapped: false, canopyClear: true, q: 0);
        Assert.Equal(0.55, c.RiseM, 9);
        Assert.True(c.Release());
        Assert.True(c.Out);
        Assert.Equal(ExitSide.Left, c.Side);
    }

    [Fact]
    public void At100KtTheSlipstreamTakesYouOffTheSill()
    {
        // F = q·0.35·(rise − 0.15)/0.45: at 0.30 m, 1621·0.35/3 = 189.1 N (holds, < 200); at 0.32 m, 214.3 N (out).
        Assert.Equal(189.12, ClimbOut.SlipstreamN(Q(100), 0.30), 1);
        Assert.Equal(214.33, ClimbOut.SlipstreamN(Q(100), 0.32), 1);
        // First exposure is pulled out above √(2·200 / (1.225·0.35/3)) = 52.90 m/s.
        Assert.Equal(52.90, ClimbOut.PullOutSpeedMs(Rho0), 2);
        var c = new ClimbOut();
        c.Grab(Vec3.Zero, ExitSide.Left);
        Assert.False(c.Out);
        c.Step(new Vec3(0, -0.30, 0), false, true, Q(100));
        Assert.False(c.Out);
        ClimbEvents ev = c.Step(new Vec3(0, -0.32, 0), false, true, Q(100));
        Assert.True(c.Out && c.PulledOut);
        Assert.True((ev & ClimbEvents.PulledOut) != 0);
    }

    // ---- the tail ----

    private static AircraftConfig Pitts() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));

    [Fact]
    public void ABodyAtTheTailplaneHitsIt()
    {
        EgressClearance c = EgressClearance.From(Pitts());
        // The Pitts' hStab inboard strip is at x −2.81, y ±0.22, z −0.07 (its config): a body there hits the tail.
        Assert.Contains(c.Hit(new Vec3(-3.0, 0.6, -0.07)), new[] { "hStab", "elevator" });
        // The fin: x −2.73 .. −3.03 at y 0, z −0.24 .. −0.76.
        Assert.Contains(c.Hit(new Vec3(-2.9, 0.0, -0.5)), new[] { "vStab", "rudder-vstab" });
        // Two metres to the left of the cockpit: clear.
        Assert.Null(c.Hit(new Vec3(-1.3, -2.0 - 3.5, -1.0)));
        Assert.Null(c.Hit(new Vec3(-1.3, 0.0, -2.0)));
    }

    // ---- the ripcord ----

    [Fact]
    public void RipcordNeedsTenCentimetresDown()
    {
        Ripcord r = Ripcord.BackRig();
        r.Grab(new Vec3(-0.1, -0.4, 0.15));
        Assert.False(r.Follow(new Vec3(-0.1, -0.4 + 0.10, 0.15)));     // up: nothing
        Assert.False(r.Follow(new Vec3(0.2, -0.4, 0.15)));            // across: nothing
        Assert.False(r.Follow(new Vec3(-0.1, -0.4 - 0.099, 0.15)));
        r.Release();
        // A partial pull stays: grab again and only the last 1 mm is needed... but 1.1 mm more to be sure.
        r.Grab(new Vec3(0, 0, 0));
        Assert.True(r.Follow(new Vec3(0, -0.0011, 0)));
        Assert.True(r.Pulled);
        Assert.Equal(97.9, Ripcord.RealPullForceMaxN, 1);   // 22 lbf
    }

    // ---- the canopy opening ----

    [Fact]
    public void FillTimeIsNDiametersAtLineStretchSpeed()
    {
        // t_f = 8 · 7.925 / V: 53.6 m/s → 1.18284 s; 30 m/s → 2.11333 s.
        Assert.Equal(1.18284, CanopyOpening.FillTime(53.6), 5);
        Assert.Equal(2.11333, CanopyOpening.FillTime(30.0), 5);
    }

    [Fact]
    public void SteadyDescentUnderThe26FootRound()
    {
        // CdS0 = 0.80·π·7.925²/4 = 39.4619 m²; v = √(2·105·9.80665 / (1.225·39.9619)) = 6.486 m/s; at 2000 m ISA
        // (ρ 1.0066) 7.155 m/s.
        Assert.Equal(39.4619, CanopyOpening.FullCdS, 4);
        Assert.Equal(6.486, CanopyOpening.SteadySinkMs(105.0, Rho0), 3);
        Assert.Equal(7.155, CanopyOpening.SteadySinkMs(105.0, 1.0066), 3);
    }

    [Fact]
    public void OpeningAtTerminalMatchesTheManualAndGivesASixToNineGShock()
    {
        // From 53.6 m/s, sea level, 105 kg, belly 0.585 m²: the integration gives 6.7 g, 3.2 s, 64 m.
        // Strong's manual: "opening times are from 2 to 3 seconds and the distance fallen … 150 feet to 300 feet"
        // (45.7–91.4 m). The shock band asserted is 6–9 g.
        CanopyOpening.Result r = CanopyOpening.Simulate(53.6, Rho0, 105.0, 0.585160);
        Assert.InRange(r.PeakG, 6.0, 9.0);
        Assert.InRange(r.LineStretchSec, 0.15, 0.40);
        Assert.InRange(r.FillTimeSec, 1.10, 1.30);
        Assert.InRange(r.SteadySec, 2.0, 4.0);
        Assert.InRange(r.LostHeightM, 45.7, 91.4);
        Assert.InRange(r.FinalSinkMs, 6.3, 6.7);
    }

    [Fact]
    public void ASlowOpeningIsSofterAndLonger()
    {
        CanopyOpening.Result fast = CanopyOpening.Simulate(53.6, Rho0, 105.0, 0.585160);
        CanopyOpening.Result slow = CanopyOpening.Simulate(25.0, Rho0, 105.0, 0.585160);
        Assert.True(slow.PeakG < fast.PeakG * 0.6, $"slow {slow.PeakG:F1} g vs fast {fast.PeakG:F1} g");
        Assert.True(slow.FillTimeSec > fast.FillTimeSec);
    }
}

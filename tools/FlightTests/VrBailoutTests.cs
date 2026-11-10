using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using FlyingGame.Sim;
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
    private static readonly Vec3 Hinge = new(0.343, 0.0, 0.0);   // Extra: right sill, 0.733 m to the free edge
    private const double Rho0 = 1.225;
    private static double Q(double kt) { double v = kt * 0.514444; return 0.5 * Rho0 * v * v; }

    // ---- the canopy in the airflow: suction from the bubble's own curvature (P2c-2) ----
    //
    // Independent check: a Python sketch of the same potential-flow integral (Lamb's α₀ by quadrature, Cp = 1 − C²(1 − n_a²)
    // averaged over the plan, the surface-mass centroid and inertia on a 400 × 400 grid), copied here by hand:
    //   Extra (2.12 × 0.78 × 0.48 m, hinge 0.343 m right):  α₀ 0.27571  C 1.15990  ΔCp 0.22721  y_c 0.25065  I 3.57864 (14 kg)
    //   Pitts (1.48 × 0.716 × 0.406 m, hinge 0.36 m right): α₀ 0.35798  C 1.21801  ΔCp 0.30751  y_c 0.21247  I 2.10843 (9 kg)
    //   plan areas π a b: Extra π·1.06·0.39 = 1.29873 m²; Pitts π·0.74·0.358 = 0.83227 m².
    [Fact]
    public void BubbleSuctionComesFromItsCurvature()
    {
        var x = new CanopyDynamics(CanopySpec.For("extra-300-like")).Bubble;
        Assert.Equal(0.27571, x.Alpha0, 4);
        Assert.Equal(1.15990, x.SpeedRatio, 4);
        Assert.Equal(1.29873, x.PlanAreaM2, 4);
        Assert.Equal(0.2272, x.NetCp, 3);
        Assert.Equal(0.2506, x.ComHeightM, 3);
        Assert.Equal(3.579, x.InertiaKgM2, 2);
        var p = new CanopyDynamics(CanopySpec.For("pitts-s2b-like")).Bubble;
        Assert.Equal(0.35798, p.Alpha0, 4);
        Assert.Equal(1.21801, p.SpeedRatio, 4);
        Assert.Equal(0.83227, p.PlanAreaM2, 4);
        Assert.Equal(0.3075, p.NetCp, 3);
        Assert.Equal(0.2125, p.ComHeightM, 3);
        Assert.Equal(2.108, p.InertiaKgM2, 2);
        // Sign: net OUTWARD (suction outside, static inside). Shape: the shorter, taller Pitts bubble sucks harder.
        Assert.True(x.NetCp > 0.0 && p.NetCp > x.NetCp);
        // A sphere's classical result: α₀ = 2/3, C = 1.5.
        Assert.Equal(2.0 / 3.0, CanopyBubble.LambAlpha(1.0, 1.0, 1.0), 3);
    }

    [Fact]
    public void AirloadScalesWithDynamicPressure()
    {
        // L = ΔCp q A: Extra 0.22719 × 1.29873 = 0.29506 m² → 295.1 N per kPa; the moment on the 0.343 m lever.
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Assert.Equal(295.1, c.AeroNormalN(1000.0), 0);
        Assert.Equal(2.0 * c.AeroNormalN(500.0), c.AeroNormalN(1000.0), 9);
        Assert.Equal(0.343 * 295.06, c.AeroMomentNm(1000.0), 0);
        Assert.Equal(-47.0915, c.GravityMomentNm(Level1G, 0.0), 3);   // −m g d = −14 · 9.80665 · 0.343
        // On its back, past 90° the centre of mass is over the hinge's far side: gravity helps open it.
        Assert.True(c.GravityMomentNm(Level1G, 100.0) > 0.0);
    }

    [Fact]
    public void FlipOpenAndHandHoldSpeeds()
    {
        // Lifts off alone at q = m g d / (d ΔCp A): Extra 47.0915 / 0.101204 = 465.3 Pa (53.6 kt); Pitts 31.7735 / 0.092133
        // = 344.9 Pa (46.1 kt). One hand at the free edge (200 N on 0.733 / 0.718 m) holds it to 1913.9 / 1903.5 Pa (108.7 / 108.4 kt).
        var x = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Assert.Equal(465.3, x.FlipOpenQ(0.0), 1);
        Assert.Equal(1913.9, x.FlipOpenQ(200.0), 1);
        var p = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        Assert.Equal(344.9, p.FlipOpenQ(0.0), 1);
        Assert.Equal(1903.5, p.FlipOpenQ(200.0), 1);
    }

    private static CanopyEvents Run(CanopyDynamics c, double q, bool locked, double seconds, Vec3 hand = default)
    {
        CanopyEvents all = CanopyEvents.None;
        for (int i = 0; i < (int)(seconds / 0.005); i++) all |= c.Step(0.005, q, Level1G, locked, hand, Hinge);
        return all;
    }

    [Fact]
    public void UnlatchedInFlightItFlingsOpenAndStaysOnBelowTheFailureSpeed()
    {
        // 60 KIAS (q 583.6, above both lift-off q's): flung to the 100° stop, slammed, still on its hinges.
        var x = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        CanopyEvents ev = Run(x, Q(60), locked: false, 2.0);
        Assert.True((ev & CanopyEvents.FlippedOpen) != 0 && (ev & CanopyEvents.HitStop) != 0);
        Assert.False(x.Departed);
        Assert.InRange(x.AngleDeg, 99.0, 102.0);
        // The Pitts first runs aft 4 in on its track in the airload, then flings.
        var p = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        ev = Run(p, Q(60), locked: false, 2.0);
        Assert.Equal(0.1016, p.SlideM, 6);
        Assert.True((ev & CanopyEvents.FlippedOpen) != 0);
        Assert.False(p.Departed);
        // Below the lift-off speed nothing moves: Extra at 45 kt (q 310 < 465).
        var slow = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Run(slow, Q(45), locked: false, 2.0);
        Assert.Equal(0.0, slow.AngleDeg, 6);
    }

    [Fact]
    public void LatchedCanopyNeverOpensByHandOrAirload()
    {
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        var edge = new Vec3(Hinge.X - 0.733, 0.0, 0.0);
        c.Grab(edge, Hinge);
        Run(c, Q(200), locked: true, 1.0, new Vec3(edge.X, 0.5, 0.0));   // hand heaving up 0.5 m, 200 kt
        Assert.Equal(0.0, c.AngleDeg, 9);
        Assert.False(c.Departed);
    }

    [Fact]
    public void PinsInItStaysShutPinsOutItOpens()
    {
        // The latch and the canopy together, as the Bridge steps them: latch first, against the canopy's state.
        static (ExtraCanopyLatch, CanopyDynamics) Fly(double squeeze, double kt)
        {
            var l = new ExtraCanopyLatch();
            var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
            var h = new ExtraLatchHands { PairHeld = true, PairSqueeze = squeeze };
            for (int i = 0; i < 300; i++)
            {
                l.Step(0.005, h, c.Shut, c.AngleDeg);
                c.Step(0.005, Q(kt), Level1G, l.Locked, Vec3.Zero, Hinge);
            }
            return (l, c);
        }
        (ExtraCanopyLatch l1, CanopyDynamics c1) = Fly(0.75, 70);   // 13.5 mm drawn: pins still in
        Assert.True(l1.Locked);
        Assert.Equal(0.0, c1.AngleDeg, 9);
        (ExtraCanopyLatch l2, CanopyDynamics c2) = Fly(1.0, 70);    // pins out at 70 KIAS: flung open
        Assert.False(l2.Locked);
        Assert.True(c2.AngleDeg > 95.0);
    }

    [Fact]
    public void ExtraHoldsAt75KiasAndTearsAwayAt85()
    {
        var hold = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Run(hold, Q(75), locked: false, 3.0);
        Assert.False(hold.Departed);
        var go = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        CanopyEvents ev = Run(go, Q(85), locked: false, 3.0);
        Assert.True(go.Departed);
        Assert.Equal(CanopyDeparture.HingesFailed, go.DepartedBy);
        Assert.True((ev & CanopyEvents.Departed) != 0);
        Assert.True(go.ClearToLeave);
    }

    [Fact]
    public void PittsHoldsAt65KiasAndTearsAwayAt75()
    {
        var hold = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        Run(hold, Q(65), locked: false, 3.0);
        Assert.False(hold.Departed);
        var go = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        Run(go, Q(75), locked: false, 3.0);
        Assert.True(go.Departed);
        Assert.Equal(CanopyDeparture.HingesFailed, go.DepartedBy);
    }

    [Fact]
    public void FittingStrengthIsThePeakStopLoadAtTysSpeeds()
    {
        // The calibration, re-run: the peak load at the stop lug when flung open at 80 KIAS (Extra) and 80 mph = 69.5 KIAS
        // (Pitts) is the strength the fittings were given, to within the 100 N it was rounded to.
        Assert.Equal(15100.0, CanopySpec.For("extra-300-like").FittingStrengthN, 6);
        Assert.Equal(11500.0, CanopySpec.For("pitts-s2b-like").FittingStrengthN, 6);
        Assert.InRange(CanopyDynamics.PeakStopLoadN(CanopySpec.For("extra-300-like"), Q(80)), 15050.0, 15150.0);
        // The Pitts' is the peak hinge reaction as its frame slams onto the stop tab (v2): 11,481 N at 69.5 KIAS.
        Assert.InRange(CanopyDynamics.PeakStopLoadN(CanopySpec.For("pitts-s2b-like"), Q(69.5)), 11450.0, 11550.0);
    }

    [Fact]
    public void AHandHoldsItShutAt100KtAndIsTornOffItAt120()
    {
        // The hand on the free edge, shut. 100 kt: q 1621 < the 1914 one hand can hold. 120 kt: q 2334, torn out of the
        // hand, flung open, and at 120 kt it does not stop at the stop.
        var edge = new Vec3(Hinge.X - 0.733, 0.0, 0.0);
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        c.Grab(edge, Hinge);
        CanopyEvents ev = Run(c, Q(100), locked: false, 1.0, edge);
        Assert.True(c.Held);
        Assert.True(c.AngleDeg < 2.0, $"held at {c.AngleDeg:F1}°");
        var d = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        d.Grab(edge, Hinge);
        ev = Run(d, Q(120), locked: false, 2.0, edge);
        Assert.True((ev & CanopyEvents.TornFromHand) != 0);
        Assert.True(d.Departed);
    }

    [Fact]
    public void PittsMustSlideAftBeforeItCanHinge()
    {
        var c = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        var hand = new Vec3(-0.35, 0.0, 0.0);
        // Locked: the latch holds it forward on its track at any speed.
        Run(c, Q(100), locked: true, 0.5, hand);
        Assert.Equal(0.0, c.SlideM, 9);
        Assert.Equal(0.0, c.AngleDeg, 9);
        // Unlatched on the ground, hand on the rail, 5.08 cm aft: half the 10.16 cm slide. v2 rig: canopy_slide is the
        // slide over the whole 15.24 cm track (0.0508 / 0.1524 = 0.333333), canopy_open the swing alone (still 0).
        c.Grab(hand, Hinge);
        c.Step(0.005, 0.0, Level1G, false, new Vec3(-0.35, 0.0, -0.0508), Hinge);
        Assert.Equal(0.0508, c.SlideM, 6);
        Assert.Equal(0.333333, c.Slide01, 6);
        Assert.Equal(0.0, c.Open01, 9);
        Assert.Equal(0.0, c.AngleDeg, 9);
        c.Step(0.005, 0.0, Level1G, false, new Vec3(-0.35, 0.0, -0.12), Hinge);
        Assert.True(c.SlidComplete);
    }

    [Fact]
    public void GripsRideTheCanopy()
    {
        // A grip on the free edge (0.733 m inboard of the hinge, level with it, 0.15 m ahead): at 90° it is straight above
        // the hinge; at 100°, 0.733·(−cos 100°, sin 100°) = (+0.1273, 0.7219) from it. The Pitts' slide carries it aft.
        var grip = new Vec3(Hinge.X - 0.733, Hinge.Y, 0.15);
        Vec3 at90 = CanopyDynamics.PointOnCanopy(grip, Hinge, 0.0, 90.0);
        Assert.Equal(Hinge.X, at90.X, 6);
        Assert.Equal(0.733, at90.Y, 6);
        Assert.Equal(0.15, at90.Z, 9);
        Vec3 at100 = CanopyDynamics.PointOnCanopy(grip, Hinge, 0.1016, 100.0);
        Assert.Equal(Hinge.X + 0.12729, at100.X, 4);
        Assert.Equal(0.72186, at100.Y, 4);
        Assert.Equal(0.15 - 0.1016, at100.Z, 9);
        // A point on the shut canopy goes nowhere.
        Vec3 shut = CanopyDynamics.PointOnCanopy(new Vec3(-0.2, 0.3, 0.4), Hinge, 0.0, 0.0);
        Assert.Equal(-0.2, shut.X, 9); Assert.Equal(0.3, shut.Y, 9); Assert.Equal(0.4, shut.Z, 9);
    }

    [Fact]
    public void OpenCanopyAndOpenCockpitDragAreas()
    {
        // Attached and fully open: open cockpit 0.15 + shell 0.4 × π·0.39·0.48/2 (0.29405) = 0.26762 m² (Extra).
        // Departed: the open cockpit alone, 0.15. Shut: nothing (the faired canopy is in the airframe's drag).
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Assert.Equal(0.0, c.DragAreaM2, 9);
        Run(c, Q(60), locked: false, 2.0);
        Assert.Equal(0.2676, c.DragAreaM2, 3);
        var gone = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Run(gone, Q(90), locked: false, 2.0);
        Assert.True(gone.Departed);
        Assert.Equal(0.15, gone.DragAreaM2, 9);
    }

    [Fact]
    public void OpenCanopyDragSlowsTheAeroplane()
    {
        static Aircraft At()
        {
            AircraftConfig cfg = Pitts();
            return new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -1000), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero),
                                new ControlDeflections(0, 0, 0, 0));
        }
        Aircraft clean = At(), open = At();
        open.ExtraDragAreaM2 = 0.2113;   // the Pitts' canopy fully open and attached: 0.12 + 0.4 × 0.22831
        for (int i = 0; i < 50; i++) { clean.StepWithDeflectionTargets(ControlDeflections.Neutral, 0.01); open.StepWithDeflectionTargets(ControlDeflections.Neutral, 0.01); }
        // ½ρV²·0.2113 at 40 m/s ≈ 207 N on 737 kg ≈ 0.28 m/s² more deceleration: about 0.14 m/s in 0.5 s.
        double dv = clean.State.Velocity.X - open.State.Velocity.X;
        Assert.InRange(dv, 0.10, 0.18);
    }

    [Fact]
    public void LosingTheCanopyTakesItsMassAndMovesTheCg()
    {
        // Pitts 737 kg, CG (0.045, 0, 0.2). A 9 kg canopy at (−0.5, 0, −0.4): r − cg = (−0.545, 0, −0.6).
        //   mass 728; CG shift = −9·r/728 = (+0.006738, 0, +0.007418); Iyy loses its parallel-axis share,
        //   9·(0.545² + 0.6²) = 5.913225 kg·m².
        //
        // THE PITCH INERTIA IS READ FROM THE AEROPLANE, NOT WRITTEN DOWN HERE. This used to assert 1115.0868, which is
        // 1121 − 5.9132 — and 1121 was the Pitts' Iyy when this test was written on 2026-11-02. The owner had it
        // retuned on 2026-11-09 ("reduce the inertia of the pitts a bit it might be overstated": the part-by-part
        // build-up's 1048, trimmed to 1000 against Roskam's band — see the config's inertiaSource), and from that day
        // the test was asserting a number about the Pitts rather than about ShedMass, and failing for it. What it is
        // for is the SHARE that leaves, so that is what it measures, and the next retune cannot rot it.
        Aircraft a = new(Pitts(), new RigidBodyState(new Vec3(0, 0, -1000), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero),
                         new ControlDeflections(0, 0, 0, 0));
        Assert.Equal(737.0, a.MassProperties.MassKg, 6);
        double iyy0 = a.MassProperties.Iyy, ixx0 = a.MassProperties.Ixx;
        a.ShedMass(9.0, new Vec3(-0.5, 0, -0.4));
        Assert.Equal(728.0, a.MassProperties.MassKg, 6);
        Assert.Equal(0.006738, a.CgShiftBody.X, 6);
        Assert.Equal(0.007418, a.CgShiftBody.Z, 6);
        Assert.Equal(iyy0 - 9.0 * (0.545 * 0.545 + 0.6 * 0.6), a.MassProperties.Iyy, 6);
        // And ROLL loses only the part of the arm that is off the roll axis: the canopy is on the centreline, so it is
        // the 0.6 m of height alone (9 × 0.36 = 3.24), which is a different number from pitch's and pins the axes apart.
        Assert.Equal(ixx0 - 9.0 * 0.6 * 0.6, a.MassProperties.Ixx, 6);
        // A re-load (fuel burn, the loading page) keeps it gone.
        a.ApplyLoad();
        Assert.Equal(728.0, a.MassProperties.MassKg, 6);
    }

    [Fact]
    public void ADepartedCanopyFallsAndDrags()
    {
        // Let go at 80 KIAS (41.16 m/s) level in still air, crown up, tumbling: it loses speed fast (a broadside shell is
        // Cd 1.2 on 1.30 m² for 14 kg) and falls.
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        var b = new CanopyFreeBody(c, new Vec3(0, 1000, 0), new Vec3(0, 0, 41.16), new Vec3(0, 1, 0), new Vec3(0, 0, 1), new Vec3(3, 0, 0));
        b.Step(1.0, 1.225, Vec3.Zero, Level1G);
        Assert.True(b.Velocity.Z < 30.0, $"still {b.Velocity.Z:F1} m/s forward");
        Assert.True(b.Velocity.Z > 0.0);
        b.Step(2.0, 1.225, Vec3.Zero, Level1G);
        Assert.True(b.Position.Y < 995.0, $"at {b.Position.Y:F1} m");
        Assert.True(b.Velocity.Y < -5.0);
        // Edge-on vs broadside drag area: 0.4 × 0.29405 = 0.11762 vs 1.2 × 1.29873 = 1.55848 m².
        var flat = new CanopyFreeBody(c, Vec3.Zero, Vec3.Zero, new Vec3(0, 1, 0), new Vec3(0, 0, 1), Vec3.Zero);
        Assert.Equal(0.11762, flat.DragAreaM2(new Vec3(0, 0, 1)), 4);
        Assert.Equal(1.55848, flat.DragAreaM2(new Vec3(0, 1, 0)), 4);
    }

    [Fact]
    public void WindNoiseGrowsWithQAndWithTheCockpitOpen()
    {
        // A = (q / 1620.8) · (0.12 + 0.88 · exposure): open at 100 kt = 1; shut at 100 kt = 0.12; open at 50 kt = 0.25.
        Assert.Equal(1.0, CockpitWind.Amplitude(1620.8, 1.0), 9);
        Assert.Equal(0.12, CockpitWind.Amplitude(1620.8, 0.0), 9);
        Assert.Equal(0.25, CockpitWind.Amplitude(405.2, 1.0), 9);
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        double shut = CockpitWind.Amplitude(Q(70), c.Exposure01);
        Run(c, Q(70), locked: false, 2.0);
        double open = CockpitWind.Amplitude(Q(70), c.Exposure01);
        var gone = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Run(gone, Q(90), locked: false, 2.0);
        double none = CockpitWind.Amplitude(Q(70), gone.Exposure01);
        Assert.True(shut < open && open < none);
        Assert.Equal(0.8, c.Exposure01, 9);
        Assert.Equal(1.0, gone.Exposure01, 9);
    }

    [Fact]
    public void ATornAwayCanopyLeavesOutboardAwayFromThePilot()
    {
        // At the moment the fittings let go it is past 100° and still swinging over: its centre of mass is moving to the
        // RIGHT (away from the cockpit) and down. The tail check then follows it relative to the aeroplane.
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        Run(c, Q(85), locked: false, 3.0);
        Assert.True(c.Departed);
        Vec3 v = c.ComVelocityLocal();
        Assert.True(v.X > 0.5, $"outboard {v.X:F2} m/s");
        CanopyTailCheck.Result r = CanopyTailCheck.Predict(c, new Vec3(-0.5, 0.3, -0.9), new Vec3(v.Z, v.X, -v.Y),
                                                           new Vec3(0, 0.98, 0.17), 85 * 0.514444, 1.225, null);
        Assert.True(r.AtTail.Y > 0.3, "it passes the tail station to the right of where it left");
    }

    // ---- the jettison (P2c-3) ----

    [Fact]
    public void JettisonKnobBelowItsThresholdDoesNothingAndFiringIsOneWay()
    {
        // Pitts knob 38 mm; 8 mm breakout; fires at 90 % = 34.2 mm, i.e. 42.2 mm of hand travel.
        JettisonPull k = JettisonPull.For("pitts-s2b-like", modelHasPull: false)!;
        var axis = new Vec3(-1, 0, 0);
        k.Grab(Vec3.Zero, axis);
        Assert.False(k.Follow(new Vec3(-0.006, 0, 0)));   // inside the breakout: nothing moves
        Assert.Equal(0.0, k.PositionM, 9);
        Assert.False(k.Follow(new Vec3(-0.040, 0, 0)));   // 32 mm out: 84 %, short of firing
        Assert.Equal(0.032, k.PositionM, 9);
        k.Release();                                       // let go: it springs home
        Assert.Equal(0.0, k.PositionM, 9);
        Assert.False(k.Fired);
        k.Grab(Vec3.Zero, axis);
        Assert.True(k.Follow(new Vec3(-0.043, 0, 0)));    // 35 mm: fires, snaps to its stop
        Assert.True(k.Fired);
        Assert.Equal(1.0, k.Pulled01, 9);
        k.Release();
        k.Grab(Vec3.Zero, axis);
        Assert.False(k.Follow(new Vec3(0.05, 0, 0)));     // pushed back in: it stays out
        Assert.Equal(1.0, k.Pulled01, 9);
        Assert.True(k.Fired);
    }

    [Fact]
    public void JettisonedAndUnlatchedThePittsCanopyRunsOffTheTabAndLeaves()
    {
        // v2 (Ty 2026-11-05): the pull frees the track; the canopy's own drag runs it the whole 6 in aft, its front hoop
        // passes the tab, nothing stops the swing, and past 150° the hinges let go. 70 and 120 kt: gone within 1.1 s.
        foreach (double kt in new[] { 70.0, 120.0 })
        {
            var c = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
            c.ReleaseHingePins();
            CanopyEvents ev = Run(c, Q(kt), locked: false, 1.1);
            Assert.True(c.Departed, $"{kt} kt");
            Assert.Equal(CanopyDeparture.Jettisoned, c.DepartedBy);
            Assert.True((ev & CanopyEvents.Departed) != 0);
            Assert.Equal(0.1524, c.SlideM, 6);
            Assert.Equal(0.0, c.PeakTabForceN, 9);   // it never touched the tab
        }
        // 40 kt is below the suction's lift-off (46 kt): it runs back to the end of the track and stays down there.
        var slow = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        slow.ReleaseHingePins();
        Run(slow, Q(40), locked: false, 3.0);
        Assert.False(slow.Departed);
        Assert.Equal(0.1524, slow.SlideM, 6);
        Assert.Equal(0.0, slow.AngleDeg, 9);
    }

    [Fact]
    public void JettisonWithTheLatchStillLockedKeepsItOnUntilTheLatchOpens()
    {
        var c = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        c.ReleaseHingePins();
        Run(c, Q(120), locked: true, 1.0);
        Assert.False(c.Departed);
        Assert.Equal(0.0, c.AngleDeg, 9);
        Assert.Equal(0.0, c.SlideM, 9);
        Run(c, Q(120), locked: false, 1.0);
        Assert.True(c.Departed);
        // On the ground nothing moves it.
        var g = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        g.ReleaseHingePins();
        Run(g, 0.0, locked: false, 1.0);
        Assert.False(g.Departed);
        Assert.Equal(0.0, g.SlideM, 9);
    }

    [Fact]
    public void PittsUnlatchedHoldsAt60OnItsTabAndDepartsAt75()
    {
        var hold = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        Run(hold, Q(60), locked: false, 3.0);
        Assert.False(hold.Departed);
        Assert.InRange(hold.AngleDeg, 96.6, 97.6);   // flung open, resting on the stop tab
        Assert.True(hold.TabForceN > 0.0);
        var go = new CanopyDynamics(CanopySpec.For("pitts-s2b-like"));
        Run(go, Q(75), locked: false, 3.0);
        Assert.True(go.Departed);
        Assert.Equal(CanopyDeparture.HingesFailed, go.DepartedBy);
    }

    [Fact]
    public void OnlyThePittsAndAnExtraWhoseModelHasThePullHaveAJettison()
    {
        Assert.NotNull(JettisonPull.For("pitts-s2b-like", modelHasPull: false));
        Assert.Null(JettisonPull.For("extra-300-like", modelHasPull: false));
        Assert.NotNull(JettisonPull.For("extra-300-like", modelHasPull: true));
        Assert.Equal(0.040, JettisonPull.For("extra-300-like", modelHasPull: true)!.TravelM, 9);
        Assert.Null(JettisonPull.For("great-lakes-2t1a2-like", modelHasPull: true));
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

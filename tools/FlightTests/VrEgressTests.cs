using System;
using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// P2d — "you can always get out" (Ty, 2026-11-02 13:28 / 14:30 / 14:31): belt + exit control → the automatic exit by
/// situation (in flight, on the ground, under water), the parachute water landing, the underwater view and the pilot's
/// water sounds. Core: src/FlyingGame.Core/Vr/{ExitTable, DoorWaterLoad, GroundEgress, PilotSwim, UnderwaterView,
/// EgressWater}.cs. Expected values are literals: hand arithmetic in the comment beside each, or copied from an
/// independent Python sketch of the same equations (not this code), as CLAUDE.md asks. World frame y up.
/// </summary>
public sealed class VrEgressTests
{
    private const double G = 9.80665;
    private static readonly FlatPilotWater Lake = new(0.0);

    // ---- in flight ----

    [Fact]
    public void InFlightBeltReleasedAndCanopyReleasedBailsOut()
    {
        var x = new AutoExit();
        Assert.Equal(ExitPath.Bailout, x.Step(beltLatched: false, exitWorked: true, EgressSituation.InFlight, 60.0, doorHeld: false));
        Assert.True(x.Started);
        // One-way: it stays started whatever the inputs do next.
        Assert.Equal(ExitPath.Bailout, x.Step(true, false, EgressSituation.OnGround, 0.0, false));
    }

    [Fact]
    public void BeltLatchedNoExitWhateverTheCanopyDoes()
    {
        var x = new AutoExit();
        foreach (EgressSituation s in Enum.GetValues(typeof(EgressSituation)))
        {
            Assert.Equal(ExitPath.None, x.Step(beltLatched: true, exitWorked: true, s, 0.0, doorHeld: false));
            Assert.Equal("belt", x.Waiting);
        }
        // Belt open but nothing worked yet: still nothing.
        Assert.Equal(ExitPath.None, x.Step(false, false, EgressSituation.InFlight, 60.0, false));
        Assert.Equal("exit control", x.Waiting);
        // On the ground but still rolling faster than a walk (1.5 m/s): wait; stopped: climb out. A ground exit that finds
        // water where he steps down is redirected into it (a shore, a floatplane); a Redirect before the start does nothing.
        Assert.Equal(ExitPath.None, x.Step(false, true, EgressSituation.OnGround, 2.0, false));
        Assert.Equal("still rolling", x.Waiting);
        x.Redirect(ExitPath.IntoWater);
        Assert.False(x.Started);
        Assert.Equal(ExitPath.ClimbOutGround, x.Step(false, true, EgressSituation.OnGround, 0.5, false));
        x.Redirect(ExitPath.IntoWater);
        Assert.Equal(ExitPath.IntoWater, x.Path);
    }

    [Fact]
    public void PittsJettisonCountsAsTheCanopyRelease()
    {
        // The knob pulled through its 8 mm breakout and its 90 % firing point (8 + 38 × 0.9 = 42.2 mm of hand; 50 mm is past it), the ball
        // latch still LOCKED: the exit control has still been worked, and the bail-out starts.
        JettisonPull j = JettisonPull.For("pitts-s2b-like", modelHasPull: true)!;
        var axis = new Vec3(-1, 0, 0);
        j.Grab(Vec3.Zero, axis);
        bool fired = false;
        for (int i = 1; i <= 50 && !fired; i++) fired = j.Follow(axis * (i * 0.001));
        Assert.True(fired && j.Fired);
        bool worked = AutoExit.CanopyWorked(latchOpen: false, jettisonFired: j.Fired, canopyGone: false);
        Assert.True(worked);
        var x = new AutoExit();
        Assert.Equal(ExitPath.Bailout, x.Step(false, worked, EgressSituation.InFlight, 40.0, false));
        // And nothing at all worked: no.
        Assert.False(AutoExit.CanopyWorked(false, false, false));
        Assert.True(AutoExit.CanopyWorked(false, false, canopyGone: true));
    }

    [Fact]
    public void ClassifiesTheSituation()
    {
        var w = new LineageCockpitWater(Lake);
        Assert.Equal(EgressSituation.InFlight, AutoExit.Classify(false, 300.0, null, w));
        Assert.Equal(EgressSituation.OnGround, AutoExit.Classify(true, 2.0, null, w));
        w.SetForTesting(CockpitWaterPhase.Floating, -0.5, 0.6, -0.4);
        Assert.Equal(EgressSituation.Afloat, AutoExit.Classify(true, 0.4, 0.0, w));
        Assert.Equal(EgressSituation.Submerged, AutoExit.Classify(true, -0.3, 0.0, w));   // eye under
        w.SetForTesting(CockpitWaterPhase.Sinking, -2.7, -1.5, -1.53);                      // 97 % full
        Assert.Equal(EgressSituation.Submerged, AutoExit.Classify(false, -2.0, 0.0, w));
    }

    // ---- the exit table ----

    [Fact]
    public void ExitTableMapsTheFleet()
    {
        ExitSpec pitts = ExitTable.For("pitts-s2b-like", "rear");
        Assert.Equal(ExitControl.Canopy, pitts.Control);
        Assert.Equal(ExitSide.Left, pitts.Side);
        Assert.Equal(ExitControl.Canopy, ExitTable.For("extra-300-like", "front").Control);
        ExitSpec cub = ExitTable.For("j3-cub-like", "rear");
        Assert.Equal(ExitControl.Door, cub.Control);
        Assert.Equal(ExitSide.Right, cub.Side);
        Assert.Contains("DoorLower_R", cub.HandleNodes);
        Assert.Contains("DoorUpper_R", cub.HandleNodes);
        Assert.Equal(ExitSide.Left, ExitTable.For("c172-like", "pilot").Side);
        Assert.Equal(ExitSide.Right, ExitTable.For("c172-like", "copilot").Side);
        Assert.Equal(ExitSide.Right, ExitTable.For("seminole-like", "pilot").Side);
        Assert.Equal(ExitControl.BeltOnly, ExitTable.For("great-lakes-like", "rear").Control);
        Assert.Equal(ExitControl.Canopy, ExitTable.For("glider-2-33-like", "front").Control);
        Assert.Equal(ExitControl.None, ExitTable.For("hang-glider-like", null).Control);
    }

    // ---- on the ground ----

    private static Vec3 Level(Vec3 eye, Vec3 local) => eye + local;   // aeroplane level, nose +z

    [Fact]
    public void ClimbOutOnTheGroundEndsStandingOnTheGroundOnTheRightSide()
    {
        // Extra: out to the LEFT (−x), over the 0.80 m wing walk, onto ground at 0.30 m; seated eye 1.95 m up.
        var eye = new Vec3(10.0, 1.95, 20.0);
        ExitSpec extra = ExitTable.For("extra-300-like", "rear");
        ClimbPath p = ClimbPath.For(extra, l => Level(eye, l), (x, z) => 0.30);
        Vec3 end = p.End;
        Assert.Equal(10.0 - 1.45, end.X, 6);              // GroundOutM 1.45 to the left
        Assert.Equal(20.0 - 0.90, end.Z, 6);              // off the trailing edge root, 0.90 aft
        Assert.Equal(0.30 + 1.641, end.Y, 6);             // standing: the soles exactly on the ground
        // Every leg on the left of the eye after the stand-up, and the wing-walk point at 0.30 + 0.80 + 1.641.
        Assert.Equal(5, p.Points.Count);
        Assert.Equal(0.30 + 0.80 + 1.641, p.Points[3].Y, 6);
        for (int i = 2; i < p.Points.Count; i++) Assert.True(p.Points[i].X < eye.X);
        // Smooth: it starts and ends at rest, and finishes at its own total.
        Vec3 a = p.EyeAt(0.0), b = p.EyeAt(0.01);
        Assert.True((b - a).Length < 1e-4);
        Vec3 e = p.Step(100.0);
        Assert.True(p.Done);
        Assert.Equal(end.Y, e.Y, 9);

        // Cub: the door is on the RIGHT.
        ClimbPath c = ClimbPath.For(ExitTable.For("j3-cub-like", "rear"), l => Level(eye, l), (x, z) => 0.30);
        Assert.True(c.End.X > eye.X);
        Assert.Equal(0.30 + 1.641, c.End.Y, 6);
    }

    [Fact]
    public void WalkAndRunSpeedsAreSmoothed()
    {
        var w = new GroundWalker(new Vec3(0, 0, 0), 0.0);
        // Full stick forward, head facing +z: 4 m/s² for 0.1 s = 0.4 m/s, then the 1.4 m/s walk.
        w.Step(0.1, 0.0, 1.0, false, 0.0, 0.0, TurnOnFoot.Snap, (x, z) => 0.0, null);
        Assert.Equal(0.4, w.Velocity.Length, 6);
        for (int i = 0; i < 30; i++) w.Step(0.1, 0.0, 1.0, false, 0.0, 0.0, TurnOnFoot.Snap, (x, z) => 0.0, null);
        Assert.Equal(1.4, w.Velocity.Length, 6);
        Assert.Equal(1.4, w.Velocity.Z, 6);
        for (int i = 0; i < 30; i++) w.Step(0.1, 0.0, 1.0, true, 0.0, 0.0, TurnOnFoot.Snap, (x, z) => 0.0, null);
        Assert.Equal(3.5, w.Velocity.Length, 6);
        // Facing east (90°): forward is +x. Never turned by the stick.
        var e = new GroundWalker(Vec3.Zero, 0.0);
        for (int i = 0; i < 30; i++) e.Step(0.1, 0.0, 1.0, false, 90.0, 0.0, TurnOnFoot.Snap, (x, z) => 0.0, null);
        Assert.Equal(1.4, e.Velocity.X, 6);
        Assert.Equal(0.0, e.YawDeg, 9);
        // Terrain following: on a 10 % slope the soles stay on it.
        var s = new GroundWalker(Vec3.Zero, 0.0);
        for (int i = 0; i < 50; i++) s.Step(0.1, 0.0, 1.0, false, 0.0, 0.0, TurnOnFoot.Snap, (x, z) => 0.1 * z, null);
        Assert.Equal(0.1 * s.Feet.Z, s.Feet.Y, 9);
        Assert.Equal(ClimbPath.StandingEyeM + s.Feet.Y, s.Eye.Y, 9);
    }

    [Fact]
    public void TurningIsOnlyWhatThePlayerAsks()
    {
        var w = new GroundWalker(Vec3.Zero, 0.0);
        w.Step(0.1, 0, 0, false, 0, turnX: 1.0, TurnOnFoot.Snap, (x, z) => 0, null);
        Assert.Equal(30.0, w.YawDeg, 9);
        w.Step(0.1, 0, 0, false, 0, turnX: 1.0, TurnOnFoot.Snap, (x, z) => 0, null);   // held: no repeat
        Assert.Equal(30.0, w.YawDeg, 9);
        w.Step(0.1, 0, 0, false, 0, turnX: 0.0, TurnOnFoot.Snap, (x, z) => 0, null);   // re-armed
        w.Step(0.1, 0, 0, false, 0, turnX: -1.0, TurnOnFoot.Snap, (x, z) => 0, null);
        Assert.Equal(0.0, w.YawDeg, 9);
        w.Step(0.5, 0, 0, false, 0, turnX: 1.0, TurnOnFoot.Smooth, (x, z) => 0, null);  // 90°/s × 0.5 s
        Assert.Equal(45.0, w.YawDeg, 9);
        w.Step(0.5, 0, 0, false, 0, turnX: 1.0, TurnOnFoot.Off, (x, z) => 0, null);
        Assert.Equal(45.0, w.YawDeg, 9);
    }

    [Fact]
    public void CannotWalkThroughTheAirframe()
    {
        // A wing-sized box ahead: centre z 3, ±2 m across, 0.4 to 1.0 m up, 0.5 m deep. Walking straight at it he stops
        // at its face less his 0.25 m radius: z ≤ 3 − 0.5 − 0.25 = 2.25.
        var wing = KeepOutBox.Level("wing", new Vec3(0, 0.7, 3.0), 0.0, new Vec3(2.0, 0.3, 0.5));
        var box = new List<KeepOutBox> { wing };
        var w = new GroundWalker(Vec3.Zero, 0.0);
        for (int i = 0; i < 100; i++) w.Step(0.05, 0.0, 1.0, false, 0.0, 0.0, TurnOnFoot.Snap, (x, z) => 0.0, box);
        Assert.Equal(2.25, w.Feet.Z, 3);
        Assert.Equal("wing", w.BlockedBy);
        // A high wing (2.0 to 2.2 m up) is over his head (1.75 m): he walks under it.
        var high = new List<KeepOutBox> { KeepOutBox.Level("high wing", new Vec3(0, 2.1, 3.0), 0.0, new Vec3(2.0, 0.1, 0.5)) };
        var u = new GroundWalker(Vec3.Zero, 0.0);
        for (int i = 0; i < 100; i++) u.Step(0.05, 0.0, 1.0, false, 0.0, 0.0, TurnOnFoot.Snap, (x, z) => 0.0, high);
        Assert.True(u.Feet.Z > 4.0);
    }

    // ---- under water ----

    [Fact]
    public void SunkCockpitDoorIsGatedByTheWaterUntilFlooded()
    {
        // 172 door, 0.90 × 1.00 m, forward hinge, handle at 0.85 W; push available 400 N. Roof still out (vented): the
        // door opens once s_o − s_i ≤ 400·0.85/(1000·g·0.90·0.5) = 0.07705 m.
        var o = ExitTable.For("c172-like", "pilot").Opening;
        double centre = -0.6;   // door spans −1.1 … −0.1 m, the surface at 0, the roof above it
        Assert.False(DoorWaterLoad.CanOpen(o, centre, 0.0, double.NaN, 1000.0));   // dry inside: 0.5 m of water on it
        // Dry inside: Δp(z) = ρg(0 − z) over z ∈ [−1.1, −0.1]: F = ρ g W ∫ −z dz = 9806.65·0.9·0.6 = 5295.6 N; M = F·W/2.
        var (f, m) = DoorWaterLoad.Load(o, centre, 0.0, double.NaN, 1000.0);
        Assert.Equal(5295.6, f, 0);
        Assert.Equal(5295.6 * 0.45, m, 0);
        Assert.False(DoorWaterLoad.CanOpen(o, centre, 0.0, -0.10, 1000.0));
        Assert.True(DoorWaterLoad.CanOpen(o, centre, 0.0, -0.076, 1000.0));
        Assert.Equal(400.0, DoorWaterLoad.PushNeededN(o, centre, 0.0, -0.07705, 1000.0), 0);

        // Fully under (roof at −1.5, the door −2.7 … −1.7): the air bubbles out at the roof, so what holds the door is the
        // air gap under the roof, z_top − s_i, not the 1.7 m of depth. The stub fills toward the roof with τ 20 s from the
        // floor at −2.7: gap(t) = 1.2 e^(−t/20) ≤ 0.07705 at t = 20 ln(1.2/0.07705) = 54.9 s.
        var cw = new LineageCockpitWater(Lake);
        double door = -2.2, t = 0.0, opened = double.NaN;
        for (int i = 0; i < 1200 && double.IsNaN(opened); i++)
        {
            cw.Step(0.1, 0, 0, floorUp: -2.7, topUp: -1.5);
            t += 0.1;
            if (DoorWaterLoad.CanOpen(o, door, 0.0, cw.InteriorWaterUp, cw.WaterDensity, cw.TopUp)) opened = t;
        }
        Assert.Equal(54.9, opened, 0);
        Assert.Equal(CockpitWaterPhase.Sinking, cw.Phase);
        // The automatic exit waits on the water, then goes.
        var x = new AutoExit();
        Assert.Equal(ExitPath.None, x.Step(false, true, EgressSituation.Submerged, 0.0, doorHeld: true));
        Assert.Equal("water pressure", x.Waiting);
        Assert.Equal(ExitPath.SwimUp, x.Step(false, true, EgressSituation.Submerged, 0.0, doorHeld: false));
    }

    [Fact]
    public void CanopyReleaseIsNotGatedButLiftingItIs()
    {
        // Extra canopy under 1 m of water, dry inside, roof out of nothing (vented): A = 0.78 × 2.12 m, Δp = ρ g·1 m.
        var o = ExitTable.For("extra-300-like", "rear").Opening;
        var (f, m) = DoorWaterLoad.Load(o, -1.0, 0.0, double.NaN, 1000.0);
        Assert.Equal(9806.65 * 0.78 * 2.12, f, 0);
        Assert.Equal(f * 0.343, m, 0);
        Assert.False(DoorWaterLoad.CanOpen(o, -1.0, 0.0, double.NaN, 1000.0));
        // The release itself is a latch / pin, unloaded in shear by a canopy pressed onto its sill: the exit control
        // counts as worked either way (CanopyWorked takes no water term).
        Assert.True(AutoExit.CanopyWorked(latchOpen: true, jettisonFired: false, canopyGone: false));
    }

    [Fact]
    public void SwimUpReachesTheSurfaceInTheDerivedTime()
    {
        // From rest, eye 3 m (and 5 m) down, suit + boots (no parachute), a held breath of 5.0 L, 50 N of stroke against
        // ½ρ·0.12 m²·v² — independent Python sketch, 2 ms steps: 4.126 s and 6.438 s.
        foreach ((double depth, double want) in new[] { (3.0, 4.126), (5.0, 6.438) })
        {
            var s = new PilotSwim(Lake, new Vec3(0, -depth, 0), Vec3.Zero, SwimPhase.SwimUp, PilotSwim.KitFor(false, false));
            for (int i = 0; i < 3000 && double.IsNaN(s.SurfacedSec); i++) s.Step(0.01);
            Assert.Equal(want, s.SurfacedSec, 1);
            Assert.Equal(SwimPhase.Tread, s.Phase);
        }
    }

    [Fact]
    public void TreadsWaterWithTheHeadHeldSteady()
    {
        var s = new PilotSwim(Lake, new Vec3(0, -3.0, 0), Vec3.Zero, SwimPhase.SwimUp, PilotSwim.KitFor(false, false));
        for (int i = 0; i < 2000; i++) s.Step(0.01);   // 20 s: up, through the surface, settled
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < 1000; i++)
        {
            s.Step(0.01);
            lo = Math.Min(lo, s.EyeAboveSurfaceM); hi = Math.Max(hi, s.EyeAboveSurfaceM);
            Assert.True(s.EyeAboveSurfaceM + SwimmerBody.ChinM > 0.0);   // the chin clear: he breathes
        }
        Assert.True(hi - lo < 0.03, $"bob {hi - lo:F3} m");              // gentle: well under 3 cm peak to peak
        Assert.Equal(SwimPhase.Tread, s.Phase);
    }

    // ---- the parachute water landing ----

    [Fact]
    public void PlungeDepthGrowsWithEntrySpeed()
    {
        // Feet first from standing height, 0.4 s before the limbs spread (CdA 0.12 → 0.40) and he strokes up; suit, boots,
        // opened harness, a 5.0 L breath. Independent Python sketch, deepest EYE below the surface:
        // 3.0 m/s 0.5146, 5.0 m/s 0.8084, 6.49 m/s (the canopy's steady sink) 1.0108, 8.0 m/s 1.1995 m.
        foreach ((double v0, double want) in new[] { (3.0, 0.5146), (5.0, 0.8084), (6.49, 1.0108), (8.0, 1.1995) })
        {
            var s = new PilotSwim(Lake, new Vec3(0, ClimbPath.StandingEyeM, 0), new Vec3(0, -v0, 0), SwimPhase.Plunge, PilotSwim.KitFor(true, true));
            for (int i = 0; i < 600; i++) s.Step(0.01);
            Assert.Equal(want, s.DeepestEyeM, 2);
        }
    }

    [Fact]
    public void SettlesToTheTopOfTheChestTreadingWater()
    {
        var s = new PilotSwim(Lake, new Vec3(0, ClimbPath.StandingEyeM, 0), new Vec3(1.5, -6.49, 0), SwimPhase.Plunge, PilotSwim.KitFor(true, true));
        double settled = 0.0;
        var tail = new List<double>();
        for (int i = 0; i < 3000; i++)
        {
            s.Step(0.01);
            if (s.TimeSec > 20.0) tail.Add(s.EyeAboveSurfaceM);
        }
        double mean = 0.0; foreach (double e in tail) mean += e; mean /= tail.Count;
        // Sketch: mean 0.2006 m from 20–30 s, ±1 cm of bob.
        Assert.Equal(0.2006, mean, 2);
        // Settle time: replay and find the last moment more than 3 cm from that mean — sketch: 5.10 s.
        var r = new PilotSwim(Lake, new Vec3(0, ClimbPath.StandingEyeM, 0), new Vec3(0, -6.49, 0), SwimPhase.Plunge, PilotSwim.KitFor(true, true));
        for (int i = 0; i < 3000; i++) { r.Step(0.01); if (Math.Abs(r.EyeAboveSurfaceM - mean) > 0.03) settled = r.TimeSec; }
        Assert.InRange(settled, 4.9, 5.3);
        // The static equilibrium (sketch): with 60 N of tread, eye 0.2099 m clear (fresh suit air) → the waterline at
        // −0.21 m, just below the suprasternal notch (−0.20): the top of the chest. Without treading, 0.0896 m UNDER.
        var still = new PilotSwim(Lake, new Vec3(0, 0.2, 0), Vec3.Zero, SwimPhase.Tread, PilotSwim.KitFor(true, true));
        still.Step(0.001);
        double eq = still.EquilibriumEyeAboveM(PilotSwim.TreadN);
        Assert.Equal(0.2099, eq, 3);
        Assert.True(-eq < SwimmerBody.NotchM && -eq > SwimmerBody.ChestBottomM);
        Assert.Equal(-0.0896, still.EquilibriumEyeAboveM(0.0), 3);
        // Once the suit's air has leaked out (τ 90 s; 10 min): 0.1237 m clear, the water at the neck; no tread: he sinks.
        still.Step(600.0);
        Assert.Equal(0.1237, still.EquilibriumEyeAboveM(PilotSwim.TreadN), 3);
        Assert.True(double.IsNaN(still.EquilibriumEyeAboveM(0.0)));
    }

    [Fact]
    public void EyeStaysAboveTheSurfaceWhileTreading()
    {
        var s = new PilotSwim(Lake, new Vec3(0, ClimbPath.StandingEyeM, 0), new Vec3(0, -6.49, 0), SwimPhase.Plunge, PilotSwim.KitFor(true, true));
        bool wentUnder = false;
        for (int i = 0; i < 4000; i++)
        {
            s.Step(0.01);
            if (s.EyeAboveSurfaceM < 0.0) wentUnder = true;
            if (s.TimeSec > 6.0) Assert.True(s.EyeAboveSurfaceM > 0.15, $"t {s.TimeSec:F1}: eye {s.EyeAboveSurfaceM:F3}");
        }
        Assert.True(wentUnder);   // the plunge did take the eye under (and the view with it)
    }

    [Fact]
    public void UnderwaterCameraModeFollowsTheEye()
    {
        var m = new UnderwaterViewMode();
        m.Step(-0.5, 0.0, 0.125);
        Assert.True(m.Under);
        Assert.Equal(0.5, m.Blend01, 9);   // half of the 0.25 s blend
        m.Step(-0.5, 0.0, 1.0);
        Assert.Equal(1.0, m.Blend01, 9);
        m.Step(-0.005, 0.0, 0.01);         // inside the 1 cm hysteresis: still under
        Assert.True(m.Under);
        m.Step(0.5, 0.0, 0.05);
        Assert.False(m.Under);
        Assert.Equal(0.8, m.Blend01, 9);
        m.Step(-3.0, null, 1.0);           // no water: never under
        Assert.False(m.Under);
        Assert.Equal(0.0, m.Blend01, 9);
        // Koschmieder at 6 m: σ = 3.912/6 = 0.652 /m; at 0.5 m e^(−0.326) = 0.7218 of the contrast is left.
        Assert.Equal(0.652, UnderwaterViewMode.FogSigmaPerM, 3);
        Assert.Equal(0.7218, UnderwaterViewMode.ContrastLeft(0.5), 4);

        // Through the plunge: on while the eye is down, off again once he has come up.
        var s = new PilotSwim(Lake, new Vec3(0, ClimbPath.StandingEyeM, 0), new Vec3(0, -6.49, 0), SwimPhase.Plunge, PilotSwim.KitFor(true, true));
        var v = new UnderwaterViewMode();
        bool wasOn = false;
        for (int i = 0; i < 1000; i++) { s.Step(0.01); v.Step(s.Eye.Y, 0.0, 0.01); wasOn |= v.Under; }
        Assert.True(wasOn);
        Assert.False(v.Under);
        Assert.Equal(0.0, v.Blend01, 9);
    }

    [Fact]
    public void CanopyCollapsesDownwindAndSettlesOnTheWater()
    {
        // Fall: v = √(2·3.6·g/(1.225·12)) = 2.19163 m/s over 7.6 m → 3.46773 s. Rest: wind × 3.46773 + 0.3 m, ≤ 11.5625 m.
        Assert.Equal(2.19163, CanopyOnWater.FallSpeedMs(), 4);
        Assert.Equal(3.46773, CanopyOnWater.FallSec(), 4);
        Assert.Equal(11.5625, CanopyOnWater.MaxReachM, 4);
        Assert.Equal(1.14268, CanopyOnWater.CoverWindMs(), 4);    // 3.9625 / 3.46773
        var pilot = new Vec3(5, 0, 5);
        var apex = new Vec3(5, 9.7, 5);
        var east3 = new CanopyOnWater(pilot, new Vec3(3, 0, 0), new Vec3(0, 0, 1), apex);
        Assert.Equal(3 * 3.46773 + 0.3, east3.DistanceM, 4);       // 10.703 m east
        Assert.Equal(5 + 10.7032, east3.Centre.X, 3);
        Assert.Equal(5.0, east3.Centre.Z, 9);
        Assert.False(east3.CoversPilot);
        var gale = new CanopyOnWater(pilot, new Vec3(0, 0, -8), new Vec3(0, 0, 1), apex);
        Assert.Equal(11.5625, gale.DistanceM, 4);                  // lines straight
        Assert.Equal(5 - 11.5625, gale.Centre.Z, 4);
        var light = new CanopyOnWater(pilot, new Vec3(0.8, 0, 0), new Vec3(0, 0, 1), apex);
        Assert.Equal(0.8 * 3.46773 + 0.3, light.DistanceM, 4);     // 3.074 m < 3.9625: over his head
        Assert.True(light.CoversPilot);
        var calm = new CanopyOnWater(pilot, new Vec3(0.1, 0, 0), new Vec3(0, 0, 1), apex);
        Assert.Equal(0.3, calm.DistanceM, 9);                      // calm: its own heading, 0.3 m
        Assert.Equal(5.3, calm.Centre.Z, 9);
        Assert.True(calm.CoversPilot);
        // Settles: halfway at half the fall time, on the water after it, floating for 45 s, then sinking at
        // √(2·4.3356/(1000·2)) = 0.065845 m/s; it pulls on him only once it hangs from him on taut lines, at a depth of
        // √(11.5625² − d²): east3 (d 10.7032: √(133.691 − 114.558)) at 4.3741 m, i.e. 66.4 s of sinking; light (d 3.074) at 11.146 m.
        Assert.Equal(0.5, east3.Settle01(3.46773 / 2), 4);
        Assert.Equal(0.0, east3.DepthM(3.46773 + 44.0, pilot), 9);
        Assert.Equal(0.065845 * 10.0, light.DepthM(3.46773 + 55.0, pilot), 3);
        Assert.Equal(4.3741, east3.HangDepthM(pilot), 3);
        Assert.Equal(4.3741, east3.DepthM(3.46773 + 45.0 + 100.0, pilot), 3);
        Assert.Equal(0.0, east3.LoadOnPilotN(3.46773 + 45.0 + 60.0, pilot), 9);
        Assert.Equal(4.3356, east3.LoadOnPilotN(3.46773 + 45.0 + 70.0, pilot), 3);
        Assert.Equal(4.3356, light.LoadOnPilotN(3.46773 + 45.0 + 200.0, pilot), 3);
    }

    [Fact]
    public void PilotInWaterAudioIsOnlySplashAndLapping()
    {
        var entry = PilotWaterAudio.Select(inWater: true, eyeUnder: false, enteredThisFrame: true, entrySpeedMs: 6.49);
        Assert.Equal(WaterCue.Splash | WaterCue.Lapping, entry.Cues);
        Assert.Equal(0.35 * 6.49 / 6.5, entry.SplashGain, 6);       // light: under the 0.35 cap
        Assert.Equal(0.0, entry.BubbleGain, 9);
        var surface = PilotWaterAudio.Select(true, false, false, 0.0);
        Assert.Equal(WaterCue.Lapping, surface.Cues);
        Assert.Equal(0.25, surface.LappingGain, 9);
        Assert.Equal(22000.0, surface.LowPassHz, 6);
        var under = PilotWaterAudio.Select(true, true, false, 0.0);
        Assert.Equal(WaterCue.Lapping, under.Cues);                  // the SAME ambience, no bubble layer
        Assert.Equal(0.25 * 0.40, under.LappingGain, 9);
        Assert.Equal(500.0, under.LowPassHz, 6);
        Assert.Equal(0.0, under.BubbleGain, 9);
        var dry = PilotWaterAudio.Select(false, false, false, 0.0);
        Assert.Equal(WaterCue.None, dry.Cues);
        Assert.Equal(1.0, dry.BubbleGain, 9);
        // A step into the water off the sill, 1 m/s: still a splash, quieter (floor 0.15 of the cap).
        Assert.Equal(0.35 * 0.15, PilotWaterAudio.Select(true, false, true, 0.5).SplashGain, 9);
        // The gate other audio reads.
        PilotWaterAudioGate.PilotInWater = true;
        Assert.Equal(0.0, PilotWaterAudioGate.BubbleGainForListener, 9);
        PilotWaterAudioGate.PilotInWater = false;
        Assert.Equal(1.0, PilotWaterAudioGate.BubbleGainForListener, 9);
    }
}

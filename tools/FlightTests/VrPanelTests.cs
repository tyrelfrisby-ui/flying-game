using System;
using System.Collections.Generic;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Phase 3, the switches and knobs (Codex's Ctl_* controls on the Pitts, Extra and Great Lakes): toggles by poke and by
/// hand, rotary knobs by twist with detents and the magneto's spring-loaded START, push-pull knobs, momentary buttons,
/// and the rig table's duplicate rows dropped on load. Specs are literals copied by hand from
/// docs/models/vr-cockpit-phase3a.md (CLAUDE.md: a test that reads the file it is checking proves nothing).
/// </summary>
public sealed class VrPanelTests
{
    private const int P = 6;

    private static PanelControlSpec Toggle() => new() { Node = "Ctl_Toggle_Smoke", Param = "smoke", Kind = PanelKind.Toggle, Lo = 0, Hi = 1, Detents = new double[] { 0, 1 } };

    // Magneto: 130°, OFF/R/L/BOTH/START = 0..4 at 0/30/60/90/130°; START springs to BOTH.
    private static PanelControlSpec Magneto() => new()
    {
        Node = "Ctl_Rotary_Magneto_Front", Param = "magneto", Kind = PanelKind.Rotary, Lo = 0, Hi = 4,
        Detents = new double[] { 0, 1, 2, 3, 4 }, SpringFrom = 4, SpringTo = 3, Seat = "Front",
    };

    private static Quat About(Vec3 axis, double deg)
    {
        double h = deg * Math.PI / 360.0, s = Math.Sin(h);
        return new Quat(axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(h));
    }

    [Fact]
    public void TheShortNameDropsKindAndSeat()
    {
        Assert.Equal("Magneto", Magneto().Short);
        Assert.Equal("Smoke", Toggle().Short);
        Assert.Equal("XpdrDigit3", new PanelControlSpec { Node = "Ctl_Rotary_XpdrDigit3" }.Short);
    }

    // ---- toggles ----

    [Fact]
    public void APokeFlipsAToggleOnceAndRearmsWhenTheFingerLeaves()
    {
        var c = new PanelControl(Toggle(), 0);
        Assert.Equal(PanelEvent.None, c.Poke(0.05));
        Assert.True((c.Poke(0.012) & PanelEvent.Changed) != 0);
        Assert.Equal(1.0, c.Value);
        for (int i = 0; i < 30; i++) c.Poke(0.010);          // resting on it: nothing more
        Assert.Equal(1.0, c.Value);
        c.Poke(0.020);                                        // backed off, but not out of the 3 cm: still not re-armed
        c.Poke(0.012);
        Assert.Equal(1.0, c.Value);
        c.Poke(0.035);                                        // out
        c.Poke(0.010);                                        // and in again: off
        Assert.Equal(0.0, c.Value);
    }

    [Fact]
    public void AToggleGrabbedFlipsAsItPassesHalfWayAndStaysWhereItIsLeft()
    {
        var c = new PanelControl(Toggle(), 0);
        c.Grab(Quat.Identity);
        Assert.Equal(PanelEvent.None, c.FollowTravel(0.3));
        Assert.Equal(0.0, c.Value);
        Assert.True((c.FollowTravel(0.6) & PanelEvent.Changed) != 0);
        Assert.Equal(1.0, c.Value);
        c.Release();
        Assert.Equal(1.0, c.Value);
        Assert.Equal(PanelEvent.None, c.Poke(0.0));          // a held-then-released toggle is not poked by the same hand's fingers still there
    }

    // ---- rotary knobs ----

    [Fact]
    public void TwistingTheMagnetoClicksThroughItsPositions()
    {
        // Shaft along +z. 130° of travel for 4 units: 32.5° a unit. From BOTH (3), twist −65° → 3 − 2 = 1 (R).
        var c = new PanelControl(Magneto(), 3, rotaryTravelDeg: 130);
        Vec3 shaft = new(0, 0, 1);
        c.Grab(Quat.Identity);
        Assert.Equal(PanelEvent.None, c.FollowTwist(About(shaft, -10), shaft));   // 3 − 0.31 → still 3
        Assert.True((c.FollowTwist(About(shaft, -40), shaft) & PanelEvent.Detent) != 0);
        Assert.Equal(2.0, c.Value);                                                // 3 − 1.23 → L (2)
        c.FollowTwist(About(shaft, -65), shaft);
        Assert.Equal(1.0, c.Value);
        c.FollowTwist(About(shaft, -170), shaft);                                  // past OFF: stays OFF
        Assert.Equal(0.0, c.Value);
    }

    [Fact]
    public void StartSpringsBackToBothAndOffStaysOff()
    {
        var c = new PanelControl(Magneto(), 3, rotaryTravelDeg: 130);
        Vec3 shaft = new(0, 0, 1);
        c.Grab(Quat.Identity);
        c.FollowTwist(About(shaft, 35), shaft);    // 3 + 1.08 → START (4)
        Assert.Equal(4.0, c.Value);
        c.Release();
        Assert.Equal(3.0, c.Value);                 // springs to BOTH
        c.Grab(Quat.Identity);
        c.FollowTwist(About(shaft, -100), shaft);  // 3 − 3.08 → OFF
        c.Release();
        Assert.Equal(0.0, c.Value);                 // latched
    }

    [Fact]
    public void TwistIsMeasuredAboutTheShaftOnly()
    {
        Vec3 shaft = new(0, 0, 1);
        // A 30° roll about the shaft, then a 40° swing about x on top: the twist is still 30°.
        Quat q = Quat.Multiply(About(new Vec3(1, 0, 0), 40), About(shaft, 30));
        Assert.Equal(30.0, PanelControl.Twist(About(shaft, 30), shaft), 6);
        Assert.InRange(PanelControl.Twist(q, shaft), 22.0, 30.0);   // swing-twist: the swing tilts the shaft, so a little less
        Assert.Equal(0.0, PanelControl.Twist(About(new Vec3(1, 0, 0), 40), shaft), 6);
        Assert.Equal(-30.0, PanelControl.Twist(About(shaft, -30), shaft), 6);
    }

    [Fact]
    public void AContinuousKnobFollowsTheTwistExactly()
    {
        // The trim knob: 300° for −1..+1. A 75° twist from neutral is +0.5.
        var spec = new PanelControlSpec { Node = "Ctl_Rotary_Trim", Param = "trim", Kind = PanelKind.Rotary, Lo = -1, Hi = 1 };
        var c = new PanelControl(spec, 0, rotaryTravelDeg: 300);
        Vec3 shaft = new(1, 0, 0);
        c.Grab(About(shaft, 10));                   // the hand was already turned a little: only the change counts
        c.FollowTwist(About(shaft, 85), shaft);
        Assert.Equal(0.5, c.Value, P);
        c.Release();
        Assert.Equal(0.5, c.Value, P);
    }

    [Fact]
    public void TheSimMovesAKnobNobodyHolds()
    {
        var spec = new PanelControlSpec { Node = "Ctl_Rotary_Trim", Param = "trim", Kind = PanelKind.Rotary, Lo = -1, Hi = 1 };
        var c = new PanelControl(spec, 0, 300);
        c.SetFromSim(-0.4);
        Assert.Equal(-0.4, c.Value, P);
        c.Grab(Quat.Identity);
        c.SetFromSim(0.9);                          // held: the hand wins
        Assert.Equal(-0.4, c.Value, P);
    }

    // ---- push-pull and momentary ----

    [Fact]
    public void APushPullKnobFollowsAndSnapsToItsStops()
    {
        // Alternate air: 35 mm, two stops 0 / 1.
        var spec = new PanelControlSpec { Node = "Ctl_PushPull_AlternateAir", Param = "alternate_air", Kind = PanelKind.PushPull, Lo = 0, Hi = 1, Detents = new double[] { 0, 1 } };
        var c = new PanelControl(spec, 0);
        c.Grab(Quat.Identity);
        c.FollowTravel(0.4);
        Assert.Equal(0.0, c.Value);
        c.FollowTravel(0.7);
        Assert.Equal(1.0, c.Value);
        // Mixture: continuous.
        var mix = new PanelControl(new PanelControlSpec { Node = "Ctl_PushPull_Mixture", Param = "mixture", Kind = PanelKind.PushPull }, 1);
        mix.Grab(Quat.Identity);
        mix.FollowTravel(0.35);
        Assert.Equal(0.35, mix.Value, P);
    }

    [Fact]
    public void AMomentaryButtonIsInWhileTheFingerIsAndPopsOut()
    {
        var spec = new PanelControlSpec { Node = "Ctl_PushPull_ComSwap", Param = "com_swap", Kind = PanelKind.PushPull, Lo = 0, Hi = 1, Detents = new double[] { 0, 1 }, SpringFrom = 1, SpringTo = 0 };
        var c = new PanelControl(spec, 0);
        Assert.True((c.Poke(0.010) & PanelEvent.Pressed) != 0);
        Assert.Equal(1.0, c.Value);
        Assert.Equal(PanelEvent.None, c.Poke(0.010));
        Assert.Equal(PanelEvent.None, c.Poke(0.020));   // inside the 3 cm: still in
        Assert.True((c.Poke(0.040) & PanelEvent.Changed) != 0);
        Assert.Equal(0.0, c.Value);
        // Grabbed and pulled in, let go: it springs out.
        c.Grab(Quat.Identity);
        c.FollowTravel(1.0);
        Assert.Equal(1.0, c.Value);
        c.Release();
        Assert.Equal(0.0, c.Value);
    }

    // ---- the duplicated rig rows ----

    [Fact]
    public void DuplicateRowsAreDroppedAndTheFirstKept()
    {
        // The Extra's table: 414 rows for 126 nodes. Here: three nodes, A three times (one of them different), B twice.
        var rows = new List<(string Node, int Map)> { ("A", 1), ("B", 2), ("A", 1), ("C", 3), ("B", 2), ("A", 9) };
        List<(string Node, int Map)> kept = RigRows.FirstOfEach<(string Node, int Map)>(rows, r => r.Node, (a, b) => a.Map == b.Map, out int dropped, out List<string> differing);
        Assert.Equal(3, kept.Count);
        Assert.Equal(new[] { "A", "B", "C" }, kept.ConvertAll(r => r.Node).ToArray());
        Assert.Equal(1, kept[0].Map);           // the first A, not the odd one
        Assert.Equal(3, dropped);
        Assert.Equal(new[] { "A" }, differing.ToArray());
    }
}

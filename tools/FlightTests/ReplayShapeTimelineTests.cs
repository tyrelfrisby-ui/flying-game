using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHEN the airframe took each shape (Ty 2026-11-08: "The wing tip was still bent up on replay after a wing strike so
/// that is not fixed yet").
///
/// A replay restores the pose and nothing else, so the damage visuals read the LIVE airframe — which during a playback
/// is whatever the aeroplane ended up as. Watching a flight that ended in a wing strike, the wing was bent from the
/// first frame, minutes before the impact that bent it. These pin the recording that fixes it.
/// </summary>
public class ReplayShapeTimelineTests
{
    private static Aircraft Build() =>
        new Aircraft(AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json")),
            new RigidBodyState(new Vec3(0, 0, -600), Quat.Identity, new Vec3(55, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    [Fact]
    public void AFlightThatTouchesNothingIsOneEntry()
    {
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        Assert.True(line.Record(0.0, a));                      // the aeroplane as it took off
        for (double t = 0.1; t < 60; t += 0.1) line.Record(t, a);
        Assert.Equal(1, line.Count);
    }

    [Fact]
    public void ABentPanelIsANewShape()
    {
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        line.Record(0.0, a);
        a.Panels[0].BendRad = 0.20;                            // the strike
        Assert.True(line.Record(5.0, a));
        Assert.Equal(2, line.Count);
    }

    [Fact]
    public void TheWingIsNOTBentBeforeTheStrikeThatBentIt()
    {
        // The defect itself.
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        line.Record(0.0, a);
        a.Panels[0].BendRad = 0.20;
        line.Record(5.0, a);

        Assert.True(line.TryShapeAt(2.0, out AirframeShape before));
        Assert.Equal(0.0, before.Panels[0].BendRad, 1e-9);

        Assert.True(line.TryShapeAt(7.0, out AirframeShape after));
        Assert.Equal(0.20, after.Panels[0].BendRad, 1e-9);
    }

    [Fact]
    public void ScrubbingBackAndForthGivesTheShapeOfEachMoment()
    {
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        line.Record(0.0, a);
        a.Panels[0].BendRad = 0.10; line.Record(10.0, a);
        a.Panels[0].BendRad = 0.30; line.Record(20.0, a);
        foreach ((double t, double expected) in new[] { (0.0, 0.0), (9.9, 0.0), (10.0, 0.10), (19.9, 0.10), (20.0, 0.30), (999.0, 0.30) })
        {
            Assert.True(line.TryShapeAt(t, out AirframeShape s));
            Assert.Equal(expected, s.Panels[0].BendRad, 1e-9);
        }
    }

    [Fact]
    public void AskingBeforeTheRecordingBeganGivesItsEarliestShape()
    {
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        a.Panels[0].BendRad = 0.05;
        line.Record(4.0, a);
        Assert.True(line.TryShapeAt(-10.0, out AirframeShape s));
        Assert.Equal(0.05, s.Panels[0].BendRad, 1e-9);
    }

    [Fact]
    public void WithNothingRecordedThereIsNoShapeToDraw()
    {
        Assert.False(new ReplayShapeTimeline().TryShapeAt(0.0, out _));
    }

    [Fact]
    public void TrimmingKeepsTheShapeThatWasInForceAtTheNewStart()
    {
        // The ring buffer drops the old frames; the shape they were flying in must not go with them, or the replay
        // draws an aeroplane that mended itself.
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        line.Record(0.0, a);
        a.Panels[0].BendRad = 0.25; line.Record(10.0, a);
        a.Panels[0].BendRad = 0.40; line.Record(300.0, a);

        line.TrimBefore(100.0);
        Assert.True(line.TryShapeAt(100.0, out AirframeShape s));
        Assert.Equal(0.25, s.Panels[0].BendRad, 1e-9);         // the 10 s bend survived its frames
        Assert.True(line.TryShapeAt(350.0, out AirframeShape later));
        Assert.Equal(0.40, later.Panels[0].BendRad, 1e-9);
    }

    /// <summary>A panel that BENT and then BROKE OFF (what a wing strike actually does, and the case the 11-08 repair did
    /// not reach — Ty 2026-11-10: "bent wings prior to the crash bug is back"). Its bent geometry rides away on the
    /// debris, which the replay carries back onto the aeroplane for every frame before the break, so the recording has to
    /// hold both the STRAIGHT shape from before the bend and the bend the panel had AT the break — the one the piece is
    /// drawn at from then on.</summary>
    [Fact]
    public void APanelThatBentAndThenBrokeIsRecordedStraightBentAndBroken()
    {
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        line.Record(0.0, a);                                   // straight
        a.Panels[0].BendRad = 0.18; a.Panels[0].State = PanelState.Bent;
        line.Record(10.0, a);                                  // the strike
        a.Panels[0].State = PanelState.Broken;
        Assert.True(line.Record(10.5, a));                     // it lets go, keeping the set it had
        Assert.Equal(3, line.Count);

        Assert.True(line.TryShapeAt(5.0, out AirframeShape before));
        Assert.Equal(PanelState.Intact, before.Panels[0].State);
        Assert.Equal(0.0, before.Panels[0].BendRad, 1e-9);     // nothing to draw bent: the piece goes back straight

        Assert.True(line.TryShapeAt(10.2, out AirframeShape bent));
        Assert.Equal(PanelState.Bent, bent.Panels[0].State);
        Assert.Equal(0.18, bent.Panels[0].BendRad, 1e-9);

        Assert.True(line.TryShapeAt(60.0, out AirframeShape gone));
        Assert.Equal(PanelState.Broken, gone.Panels[0].State);
        Assert.Equal(0.18, gone.Panels[0].BendRad, 1e-9);      // the shape the piece fell in
    }

    [Fact]
    public void ATinyChangeIsNotWorthAnEntry()
    {
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        line.Record(0.0, a);
        a.Panels[0].BendRad = 0.0001;                          // far below anything the renderer can show
        Assert.False(line.Record(1.0, a));
        Assert.Equal(1, line.Count);
    }

    [Fact]
    public void AnUndroopedEngineContributesOnlyTheNoseBend()
    {
        Aircraft a = Build();
        var line = new ReplayShapeTimeline();
        line.Record(0.0, a);
        Assert.True(line.TryShapeAt(0.0, out AirframeShape s));
        Assert.False(s.EngineDrawn);
        Assert.Equal(s.NoseBendRad, s.EngineSagRad, 1e-12);
    }
}

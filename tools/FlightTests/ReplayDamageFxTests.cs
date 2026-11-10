using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHEN the aeroplane was alight and WHEN it was streaming fuel (Ty 2026-11-10: "Add the smoke fire fuel leak fixes
/// then fold it in" — the last of the three damage visuals that read the LIVE airframe through a replay, after the bent
/// wing and the propeller).
///
/// A replay restores the pose and nothing else, so DamageFx was handed the live DamageState — which during a playback
/// is whatever the aeroplane ended up as. A flight that ended alight burned from the first frame, before the round that
/// lit it. These pin the recording that fixes it.
/// </summary>
[Collection("WorldTerrainActive")]
public class ReplayDamageFxTests : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or fail: a part loss is sim state, and nothing of it is left
    /// standing for the next test (CombatTests' own rule).</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private static AircraftConfig Cub() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    private static Aircraft Level() =>
        new Aircraft(Cub(), new RigidBodyState(new Vec3(0, 0, -500), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero),
            ControlDeflections.Neutral);

    // ---- the defect itself --------------------------------------------------------------------------------------------

    /// <summary>The whole report, in one: an aeroplane that ends the flight alight is NOT burning before the round that
    /// lit it, and is burning after.</summary>
    [Fact]
    public void TheWingIsNOTOnFireBeforeTheRoundThatLitIt()
    {
        Aircraft a = Level();
        var line = new ReplayDamageFxTimeline();
        Assert.True(line.Record(0.0, a));                                          // whole, dry
        Assert.Equal(DamageEvent.FuelLeak, a.Damage.RegisterHit(AirframeComponent.WingRightInner));
        Assert.True(line.Record(10.0, a));                                         // the tank is holed
        Assert.Equal(DamageEvent.Fire, a.Damage.RegisterHit(AirframeComponent.WingRightInner));
        Assert.True(line.Record(20.0, a));                                         // and it catches
        Assert.Equal(3, line.Count);

        Assert.True(line.TryStateAt(5.0, out DamageFxState dry));
        Assert.False(dry.Any);                                                     // nothing drawn at all

        Assert.True(line.TryStateAt(15.0, out DamageFxState leaking));
        Assert.True(leaking.LeakDrawnRight);
        Assert.False(leaking.FireRight);

        Assert.True(line.TryStateAt(25.0, out DamageFxState alight));
        Assert.True(alight.FireRight);
        Assert.False(alight.LeakDrawnRight);                                       // the same hole feeds the flame now
    }

    /// <summary>The one rule that moved out of the renderer, so the suite can reach it: a holed tank STREAMS until it
    /// catches, and after that there is flame and black smoke and no stream.</summary>
    [Fact]
    public void AStreamIsDrawnOnlyUntilItCatches()
    {
        Assert.False(new DamageFxState(false, false, false, false).LeakDrawnLeft);
        Assert.True(new DamageFxState(true, false, false, false).LeakDrawnLeft);    // holed, not alight: a stream
        Assert.False(new DamageFxState(true, false, true, false).LeakDrawnLeft);    // alight: no stream
        Assert.True(new DamageFxState(true, false, true, false).FireLeft);
        // And the sides do not borrow from each other.
        var left = new DamageFxState(true, false, true, false);
        Assert.False(left.LeakDrawnRight);
        Assert.False(left.FireRight);
    }

    /// <summary>WHY THIS IS ITS OWN TIMELINE AND NOT A PREFIX. A burning inner panel fails after
    /// DamageState.FireBurnSec and takes its own fire and leak with it, so the flags go back OFF partway through the
    /// flight. ReplayDamageTimeline's model — the damage at any moment is a PREFIX of a list, because nothing ever
    /// repairs itself — cannot say that. Scrubbing past the burn-through must put the fire out; scrubbing back into it
    /// must light it again.</summary>
    [Fact]
    public void AFireThatBurnedTheWingOffGoesOutAndScrubbingBackLightsItAgain()
    {
        Aircraft a = Level();
        var line = new ReplayDamageFxTimeline();
        line.Record(0.0, a);
        a.Damage.RegisterHit(AirframeComponent.WingRightInner);
        a.Damage.RegisterHit(AirframeComponent.WingRightInner);
        Assert.True(a.Damage.FireRight);
        line.Record(30.0, a);
        for (int i = 0; i < 120; i++) a.Damage.Update(0.1);                        // 12 s of burning: past the 10 s
        Assert.True(a.IsLost(AirframeComponent.WingRightInner));
        Assert.False(a.Damage.FireRight);                                          // the panel took its fire with it
        Assert.True(line.Record(42.0, a));
        Assert.Equal(3, line.Count);

        Assert.True(line.TryStateAt(35.0, out DamageFxState burning));
        Assert.True(burning.FireRight);
        Assert.True(line.TryStateAt(60.0, out DamageFxState after));
        Assert.False(after.Any, "the wing is gone: there is nothing left to burn");
        Assert.True(line.TryStateAt(31.0, out DamageFxState again));               // scrub back into the fire
        Assert.True(again.FireRight);
    }

    /// <summary>Each wing keeps its own fire: one streaming and the other alight is one state, and the replay shows
    /// both as they were.</summary>
    [Fact]
    public void TheTwoWingsAreRecordedApart()
    {
        Aircraft a = Level();
        var line = new ReplayDamageFxTimeline();
        line.Record(0.0, a);
        a.Damage.RegisterHit(AirframeComponent.WingLeftInner);                     // left: holed
        a.Damage.RegisterHit(AirframeComponent.WingRightInner);                    // right: holed
        a.Damage.RegisterHit(AirframeComponent.WingRightInner);                    // right: alight
        line.Record(8.0, a);

        Assert.True(line.TryStateAt(8.0, out DamageFxState s));
        Assert.True(s.LeakDrawnLeft);
        Assert.False(s.FireLeft);
        Assert.True(s.FireRight);
        Assert.False(s.LeakDrawnRight);
    }

    // ---- the timeline's own bookkeeping ------------------------------------------------------------------------------

    /// <summary>A flight nobody shot at is one entry, however many frames it ran for — the same rule the airframe's
    /// shape keeps.</summary>
    [Fact]
    public void AFlightThatIsNeverHitIsOneEntry()
    {
        Aircraft a = Level();
        var line = new ReplayDamageFxTimeline();
        Assert.True(line.Record(0.0, a));
        for (double t = 1.0 / 30; t < 60; t += 1.0 / 30) line.Record(t, a);
        Assert.Equal(1, line.Count);
        Assert.True(line.TryStateAt(42.0, out DamageFxState s));
        Assert.False(s.Any);
    }

    /// <summary>The ring buffer drops the old frames; the fire they were flying in must not go with them, or the replay
    /// puts out a fire that is still burning.</summary>
    [Fact]
    public void TrimmingKeepsTheFireThatWasBurningAtTheNewStart()
    {
        var line = new ReplayDamageFxTimeline();
        line.Record(0.0, new DamageFxState(false, false, false, false));
        line.Record(10.0, new DamageFxState(false, true, false, true));            // the right wing alight at 10 s
        line.Record(300.0, new DamageFxState(false, false, false, false));         // it burns off at 300 s

        line.TrimBefore(100.0);
        Assert.Equal(100.0, line.TimeAt(0), 12);
        Assert.True(line.TryStateAt(100.0, out DamageFxState atStart));
        Assert.True(atStart.FireRight, "the 10 s fire survived its frames");
        Assert.True(line.TryStateAt(350.0, out DamageFxState later));
        Assert.False(later.Any);
    }

    /// <summary>Asking before the recording began gives its earliest state, as FlightRecorder.Sample clamps to its
    /// nearest end — and with nothing recorded at all there is no state, so the renderer keeps reading the live
    /// aeroplane.</summary>
    [Fact]
    public void BeforeTheRecordingBeganIsItsEarliestStateAndAnEmptyTimelineHasNone()
    {
        var line = new ReplayDamageFxTimeline();
        Assert.False(line.TryStateAt(0.0, out _));
        line.Record(4.0, new DamageFxState(true, false, false, false));
        Assert.True(line.TryStateAt(-10.0, out DamageFxState s));
        Assert.True(s.LeakDrawnLeft);
    }

    /// <summary>A fresh aeroplane: nothing alight, nothing leaking, nothing remembered.</summary>
    [Fact]
    public void ClearingLeavesNothingBurning()
    {
        var line = new ReplayDamageFxTimeline();
        line.Record(0.0, new DamageFxState(true, true, true, true));
        Assert.Equal(1, line.Count);
        line.Clear();
        Assert.Equal(0, line.Count);
        Assert.False(line.TryStateAt(0.0, out _));
    }

    /// <summary>A caller whose clock stepped backwards never puts the timeline out of order (ReplayDamageTimeline's own
    /// rule): the entry is clamped to the one before it.</summary>
    [Fact]
    public void AnOutOfOrderReportIsClamped()
    {
        var line = new ReplayDamageFxTimeline();
        line.Record(10.0, new DamageFxState(false, false, false, false));
        line.Record(5.0, new DamageFxState(true, false, false, false));
        Assert.Equal(2, line.Count);
        Assert.Equal(10.0, line.TimeAt(1), 12);
    }
}

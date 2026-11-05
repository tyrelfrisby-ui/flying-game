using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.LocalMp;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// LOCAL 2-PLAYER (Ty 2026-11-05: "start local multi player on same device for iOS and quest"). The engine-free half:
/// the world clock advances once per tick however many aeroplanes are stepped, the pads bind to seats and stay bound,
/// the split layout, the formation start, and the second player's pad pilot. Expected values are literals.
/// </summary>
[Collection("WorldTerrainActive")]   // writes the global Atmosphere clock and its owner
public sealed class LocalMultiplayerTests
{
    private readonly ITestOutputHelper _o;
    public LocalMultiplayerTests(ITestOutputHelper o) => _o = o;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Aircraft AirStartAt(AircraftConfig c, double x, double y)
    {
        AirStart.Plan plan = AirStart.Compute(c, x, y, 1200, 0.0, c.SpawnIasMs > 0 ? c.SpawnIasMs : 30.0);
        return AirStart.Build(c, plan);
    }

    /// <summary>Clock, owner and the still air they need, restored in the SAME synchronous scope (CLAUDE.md: a global
    /// set in a test is cleared on the thread that set it).</summary>
    private static void WithCleanClock(Action body)
    {
        object? owner = Atmosphere.ClockOwner; double t = Atmosphere.SimTimeSec;
        var field = Atmosphere.Field; var wind = Atmosphere.SteadyWind; var turb = Atmosphere.ActiveTurbulence; var terrain = WorldTerrain.Active;
        try
        {
            Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; WorldTerrain.Active = null;
            Atmosphere.ClockOwner = null; Atmosphere.SimTimeSec = 0;
            body();
        }
        finally
        {
            Atmosphere.ClockOwner = owner; Atmosphere.SimTimeSec = t;
            Atmosphere.Field = field; Atmosphere.SteadyWind = wind; Atmosphere.ActiveTurbulence = turb; WorldTerrain.Active = terrain;
        }
    }

    /// <summary>One second of the game's frame loop: P1 and P2 at 200 Hz through their drivers' accumulators, three AI
    /// drones at 50 Hz the way CombatController steps them (a fresh SimLoop per AI step).</summary>
    private static void FlyOneSecond(SimLoop p1, SimLoop? p2, IList<Aircraft> ai)
    {
        double acc1 = 0, acc2 = 0, aiAcc = 0;
        var u = new ControlInputs(0, 0, 0, -0.5);
        for (int f = 0; f < 60; f++)
        {
            const double dt = 1.0 / 60.0;
            p1.Advance(dt, u, ref acc1);
            p2?.Advance(dt, u, ref acc2);
            aiAcc += dt;
            while (aiAcc >= SimLoop.AiFixedDtSec - 1e-12)
            {
                foreach (Aircraft a in ai) new SimLoop(a, SimLoop.AiFixedDtSec).RunFor(SimLoop.AiFixedDtSec, u);
                aiAcc -= SimLoop.AiFixedDtSec;
            }
        }
    }

    [Fact]
    public void WithoutAnOwnerEveryAircraftAdvancedTheClock_TheBugThisFixes()
    {
        // Documents the old rule (still the rule with no owner, for the headless tools): P1 + three drones = 4 s a second.
        WithCleanClock(() =>
        {
            AircraftConfig c = Load("j3-cub-like");
            var p1 = new SimLoop(AirStartAt(c, 0, 0));
            var ai = new List<Aircraft> { AirStartAt(c, 500, 0), AirStartAt(c, 1000, 0), AirStartAt(c, 1500, 0) };
            FlyOneSecond(p1, null, ai);
            _o.WriteLine($"no owner: clock {Atmosphere.SimTimeSec:F4} s after 1 s");
            Assert.InRange(Atmosphere.SimTimeSec, 3.95, 4.05);
        });
    }

    [Fact]
    public void TheWorldClockAdvancesOncePerTickWithTwoPlayersAndAi()
    {
        WithCleanClock(() =>
        {
            AircraftConfig c = Load("j3-cub-like");
            var p1 = new SimLoop(AirStartAt(c, 0, 0));
            var p2 = new SimLoop(AirStartAt(c, 0, 40));
            var ai = new List<Aircraft> { AirStartAt(c, 500, 0), AirStartAt(c, 1000, 0), AirStartAt(c, 1500, 0) };
            Atmosphere.ClockOwner = p1.Aircraft;   // what FlightSimDriver does for P1
            FlyOneSecond(p1, p2, ai);
            _o.WriteLine($"owner P1: clock {Atmosphere.SimTimeSec:F6} s after 1 s with P2 + 3 AI");
            // 60 frames of 1/60 s at 200 Hz: 200 steps minus at most one carried in the accumulator.
            Assert.InRange(Atmosphere.SimTimeSec, 0.995 - 1e-9, 1.0 + 1e-9);
        });
    }

    [Fact]
    public void OnlyTheOwnersStepMovesTheClock()
    {
        WithCleanClock(() =>
        {
            object owner = new object(), other = new object();
            Atmosphere.ClockOwner = owner;
            Atmosphere.AdvanceTime(other, 0.5);
            Assert.Equal(0.0, Atmosphere.SimTimeSec);
            Atmosphere.AdvanceTime(owner, 0.25);
            Assert.Equal(0.25, Atmosphere.SimTimeSec);
            Atmosphere.ClockOwner = null;
            Atmosphere.AdvanceTime(other, 0.5);
            Assert.Equal(0.75, Atmosphere.SimTimeSec);
        });
    }

    [Fact]
    public void OnePlayerFliesBitForBitTheSameWithTheOwnerSet()
    {
        // The single-player game with nothing else flying: owner set (the new game) and no owner (the old game) must be
        // the same flight to the bit, clock included.
        RigidBodyState RunOnce(bool owned, out double clock)
        {
            RigidBodyState end = default; double t = 0;
            WithCleanClock(() =>
            {
                AircraftConfig c = Load("pitts-s2b-like");
                var p1 = new SimLoop(AirStartAt(c, 0, 0));
                if (owned) Atmosphere.ClockOwner = p1.Aircraft;
                double acc = 0;
                for (int f = 0; f < 300; f++) p1.Advance(1.0 / 60.0, new ControlInputs(0.2, -0.1, 0.05, -0.6), ref acc);
                end = p1.Aircraft.State; t = Atmosphere.SimTimeSec;
            });
            clock = t;
            return end;
        }
        RigidBodyState a = RunOnce(false, out double ta), b = RunOnce(true, out double tb);
        Assert.Equal(ta, tb);
        Assert.Equal(a.Position.X, b.Position.X); Assert.Equal(a.Position.Y, b.Position.Y); Assert.Equal(a.Position.Z, b.Position.Z);
        Assert.Equal(a.Attitude.W, b.Attitude.W); Assert.Equal(a.Attitude.X, b.Attitude.X);
        Assert.Equal(a.Velocity.X, b.Velocity.X); Assert.Equal(a.Rates.X, b.Rates.X);
    }

    // ---- pads ------------------------------------------------------------------------------------------------

    [Fact]
    public void FirstPadIsP1SecondIsP2()
    {
        var b = new PadBinding(2);
        Assert.True(b.Update(new[] { 7 }));
        Assert.Equal(7, b.PadFor(0)); Assert.Equal(PadBinding.NoPad, b.PadFor(1));
        Assert.True(b.Update(new[] { 7, 12 }));
        Assert.Equal(7, b.PadFor(0)); Assert.Equal(12, b.PadFor(1));
        Assert.False(b.Update(new[] { 12, 7 }));   // order of the list (who spoke last) changes nothing
        Assert.Equal(7, b.PadFor(0)); Assert.Equal(12, b.PadFor(1));
    }

    [Fact]
    public void TwoPadsAtOnceBindLowestIdFirst()
    {
        var b = new PadBinding(2);
        b.Update(new[] { 21, 9 });
        Assert.Equal(9, b.PadFor(0)); Assert.Equal(21, b.PadFor(1));
    }

    [Fact]
    public void P1DroppingOutDoesNotMoveP2AndComesBackToItsSeat()
    {
        var b = new PadBinding(2);
        b.Update(new[] { 7, 12 });
        b.Update(new[] { 12 });                     // P1's pad goes flat
        Assert.Equal(PadBinding.NoPad, b.PadFor(0)); Assert.Equal(12, b.PadFor(1));
        b.Update(new[] { 12, 7 });                  // and reconnects with its id
        Assert.Equal(7, b.PadFor(0)); Assert.Equal(12, b.PadFor(1));
    }

    [Fact]
    public void P2DroppingOutLeavesP1AloneAndANewPadTakesTheEmptySeat()
    {
        var b = new PadBinding(2);
        b.Update(new[] { 7, 12 });
        b.Update(new[] { 7 });
        Assert.Equal(7, b.PadFor(0)); Assert.Equal(PadBinding.NoPad, b.PadFor(1));
        Assert.Equal(-1, b.SeatOf(12));
        b.Update(new[] { 7, 30 });                  // a different pad turns up while 12 is away: it is P2 now
        Assert.Equal(30, b.PadFor(1));
        b.Update(new[] { 7, 30, 12 });              // 12 returns to a full house: it flies nothing
        Assert.Equal(7, b.PadFor(0)); Assert.Equal(30, b.PadFor(1)); Assert.Equal(-1, b.SeatOf(12));
    }

    [Fact]
    public void ASeatsOldPadIsNotTakenByTheOtherEmptySeat()
    {
        var b = new PadBinding(2);
        b.Update(new[] { 7, 12 });
        b.Update(Array.Empty<int>());               // both pads off (the app went to the background)
        b.Update(new[] { 12 });                     // P2's pad comes back first: it is still P2, not promoted to P1
        Assert.Equal(PadBinding.NoPad, b.PadFor(0)); Assert.Equal(12, b.PadFor(1));
        b.Update(new[] { 12, 7 });
        Assert.Equal(7, b.PadFor(0)); Assert.Equal(12, b.PadFor(1));
    }

    // ---- layout ----------------------------------------------------------------------------------------------

    [Fact]
    public void LandscapeSplitsSideBySideP1Left()
    {
        var l = SplitLayout.Viewport(0, 2, 2556, 1179); var r = SplitLayout.Viewport(1, 2, 2556, 1179);
        Assert.Equal(0.0, l.X); Assert.Equal(0.0, l.Y); Assert.Equal(0.498, l.W, 9); Assert.Equal(1.0, l.H);
        Assert.Equal(0.502, r.X, 9); Assert.Equal(0.498, r.W, 9); Assert.Equal(1.0, r.H);
        Assert.True(l.X + l.W < r.X, "a gap between the halves");
    }

    [Fact]
    public void PortraitSplitsTopAndBottomP1OnTop()
    {
        var t = SplitLayout.Viewport(0, 2, 1179, 2556); var b = SplitLayout.Viewport(1, 2, 1179, 2556);
        Assert.Equal(0.502, t.Y, 9); Assert.Equal(0.498, t.H, 9); Assert.Equal(1.0, t.W);
        Assert.Equal(0.0, b.Y); Assert.Equal(0.498, b.H, 9);
    }

    [Fact]
    public void OnePlayerIsTheWholeScreen()
    {
        var v = SplitLayout.Viewport(0, 1, 2556, 1179);
        Assert.Equal(0.0, v.X); Assert.Equal(0.0, v.Y); Assert.Equal(1.0, v.W); Assert.Equal(1.0, v.H);
    }

    // ---- formation start -------------------------------------------------------------------------------------

    [Fact]
    public void AirStartIsAbreastOnTheRightThirtyMetresTipToTip()
    {
        var o = FormationSlot.For(1, 10.7, 6.8, onSurface: false);   // a J-3: 35 ft 3 in span
        Assert.Equal(0.0, o.ForwardM); Assert.Equal(40.7, o.RightM, 9);
        var (dxN, dyN) = FormationSlot.World(o, 0.0);                 // heading north: right is east (+y)
        Assert.Equal(0.0, dxN, 9); Assert.Equal(40.7, dyN, 9);
        var (dxE, dyE) = FormationSlot.World(o, Math.PI / 2);         // heading east: right is south (−x)
        Assert.Equal(-40.7, dxE, 9); Assert.Equal(0.0, dyE, 9);
    }

    [Fact]
    public void SurfaceStartIsInLineAheadOnTheCentreline()
    {
        var o = FormationSlot.For(1, 10.7, 6.8, onSurface: true);   // ahead: behind the threshold is off the runway
        Assert.Equal(66.8, o.ForwardM, 9); Assert.Equal(0.0, o.RightM);
        var (dx, dy) = FormationSlot.World(o, 0.0);
        Assert.Equal(66.8, dx, 9); Assert.Equal(0.0, dy, 9);
        Assert.Equal(0.0, FormationSlot.For(0, 10.7, 6.8, false).RightM);   // P1 is where the game puts it
    }

    // ---- the second player's pad -----------------------------------------------------------------------------

    [Fact]
    public void ParkedP2FliesItsTrimmedStartHandsOff()
    {
        var pilot = new LocalPadPilot();
        pilot.Preset(powered: true, trimStick: -0.12, idleStart: false, startSpoiler01: 0);
        var r = pilot.Step(new LocalPadPilot.Pad { Connected = false }, 1.0 / 60, null);
        Assert.Equal(0.0, r.Inputs.Aileron); Assert.Equal(-0.12, r.Inputs.Elevator, 12); Assert.Equal(0.0, r.Inputs.Rudder);
        Assert.Equal(1.0 - 2.0 * 0.75, r.Inputs.ThrottleLever, 12);   // the rig power, 75 %
        Assert.False(r.Inputs.AileronFree); Assert.False(r.Inputs.RudderFree);   // held, as a pad at rest holds them
        Assert.Equal(0.0, r.Brake01);
    }

    [Fact]
    public void ThrottleIsARateIdleToFullInTwoSeconds()
    {
        var pilot = new LocalPadPilot();
        pilot.Preset(true, 0, idleStart: true, 0);
        var pad = new LocalPadPilot.Pad { Connected = true, Throttle = 1.0 };
        for (int i = 0; i < 60; i++) pilot.Step(pad, 1.0 / 60, null);
        Assert.Equal(0.5, pilot.Lever01, 9);                         // 1 s of full stick: half way
        pad.Throttle = 0;
        for (int i = 0; i < 60; i++) pilot.Step(pad, 1.0 / 60, null);
        Assert.Equal(0.5, pilot.Lever01, 9);                         // let go: it stays
        pad.ThrottleUp = true;
        for (int i = 0; i < 120; i++) pilot.Step(pad, 1.0 / 60, null);
        Assert.Equal(1.0, pilot.Lever01, 9);                         // shoulders, clamped at full
    }

    [Fact]
    public void GliderLeverIsTheSpoilerStickDownOpensThem()
    {
        var pilot = new LocalPadPilot();
        pilot.Preset(powered: false, 0, idleStart: false, 0);
        var pad = new LocalPadPilot.Pad { Connected = true, Throttle = -1.0 };
        for (int i = 0; i < 60; i++) pilot.Step(pad, 1.0 / 60, null);
        var r = pilot.Step(new LocalPadPilot.Pad { Connected = true }, 1.0 / 60, null);
        Assert.Equal(0.5, r.Inputs.ThrottleLever, 9);                 // the sim reads a glider's lever as the spoiler fraction
    }

    [Fact]
    public void SticksLiftPastTheAeroplanesDeadZoneAndTrimRuns()
    {
        var axis = new ControlAxisConfig { DeadZone = 0.05 };
        var controls = new ControlsConfig { Aileron = axis, Elevator = axis, Rudder = axis };
        var pilot = new LocalPadPilot();
        pilot.Preset(true, 0, false, 0);
        var r = pilot.Step(new LocalPadPilot.Pad { Connected = true, Roll = 0.5, Pitch = -0.5, TrimNoseDown = true }, 0.5, controls);
        Assert.Equal(0.05 + 0.5 * 0.95, r.Inputs.Aileron, 12);
        // trim: 0.5 s at 0.5 slider/s = 0.25 slider = 0.15 stick, added to −0.525.
        Assert.Equal(-(0.05 + 0.5 * 0.95) + 0.25 * 0.6, r.Inputs.Elevator, 12);
    }

    [Fact]
    public void FlapsStepFromWhereTheyAre()
    {
        Assert.Equal(0.5, LocalPadPilot.NextFlap(0.0, 1)); Assert.Equal(1.0, LocalPadPilot.NextFlap(0.5, 1));
        Assert.Equal(1.0, LocalPadPilot.NextFlap(1.0, 1)); Assert.Equal(0.5, LocalPadPilot.NextFlap(1.0, -1));
        Assert.Equal(0.0, LocalPadPilot.NextFlap(0.3, -1)); Assert.Equal(0.3, LocalPadPilot.NextFlap(0.3, 0));
    }

    // ---- LAN / room spawn spread -------------------------------------------------------------------------------

    private static SpawnSpread.Other O(string id, double x, double y, double z, double? age) => new(id, x, y, z, age);

    [Fact]
    public void NewcomerOnTopOfAPilotAlreadyFlyingMoves_ThePilotFlyingDoesNot()
    {
        double clear = SpawnSpread.ClearRadiusM(10.7);   // 25.7 m
        Assert.Equal(25.7, clear, 9);
        var flying = new[] { O("aaaa", 3, 2, -1200, 120.0) };
        Assert.True(SpawnSpread.ShouldMove("zzzz", 0.5, 0, 0, -1200, flying, clear));
        // ...and the one flying, seeing the newcomer (age 0.5) beside him, never moves: he is past his window.
        Assert.False(SpawnSpread.ShouldMove("aaaa", 120.0, 3, 2, -1200, new[] { O("zzzz", 0, 0, -1200, 0.5) }, clear));
    }

    [Fact]
    public void TwoNewcomersAgreeThatExactlyOneMoves()
    {
        double clear = SpawnSpread.ClearRadiusM(10.7);
        bool a = SpawnSpread.ShouldMove("4f2a91c0", 1.0, 0, 0, -1200, new[] { O("b7e01d33", 0, 0, -1200, 1.2) }, clear);
        bool b = SpawnSpread.ShouldMove("b7e01d33", 1.2, 0, 0, -1200, new[] { O("4f2a91c0", 0, 0, -1200, 1.0) }, clear);
        Assert.False(a); Assert.True(b);
    }

    [Fact]
    public void AnOlderBuildWithNoAgeCountsAsFlyingAlready()
    {
        Assert.True(SpawnSpread.ShouldMove("0000", 0.2, 0, 0, -800, new[] { O("ffff", 0, 0, -800, null) }, 25.7));
    }

    [Fact]
    public void FarApartOrPastTheWindowNobodyMoves()
    {
        Assert.False(SpawnSpread.ShouldMove("zzzz", 0.5, 0, 0, -1200, new[] { O("aaaa", 30, 0, -1200, 120.0) }, 25.7));   // 30 m clear
        Assert.False(SpawnSpread.ShouldMove("zzzz", 0.5, 0, 0, -1200, new[] { O("aaaa", 0, 0, -1240, 120.0) }, 25.7));   // 40 m above
        Assert.False(SpawnSpread.ShouldMove("zzzz", 4.5, 0, 0, -1200, new[] { O("aaaa", 0, 0, -1200, 120.0) }, 25.7));   // window gone
    }

    [Fact]
    public void TheMoverTakesTheFirstFreeSlotAbreast()
    {
        double clear = SpawnSpread.ClearRadiusM(10.7);
        // Heading north; one pilot at the start, one already in slot 1 (40.7 m east): slot 2 is 81.4 m east.
        var others = new[] { O("a", 0, 0, -1200, 60.0), O("b", 0, 40.7, -1200, 60.0) };
        Assert.Equal(2, SpawnSpread.PickSlot(others, 0, 0, -1200, 0.0, 10.7, 6.8, onSurface: false, clear));
        Assert.Equal(1, SpawnSpread.PickSlot(new[] { O("a", 0, 0, -1200, 60.0) }, 0, 0, -1200, 0.0, 10.7, 6.8, false, clear));
    }

    [Fact]
    public void OnTheRunwayTheMoverLinesUpAhead()
    {
        double clear = SpawnSpread.ClearRadiusM(10.7);
        int k = SpawnSpread.PickSlot(new[] { O("a", 0, 0, -450, 10.0) }, 0, 0, -450, 0.0, 10.7, 6.8, onSurface: true, clear);
        Assert.Equal(1, k);
        var (dx, dy) = FormationSlot.World(FormationSlot.For(k, 10.7, 6.8, true), 0.0);
        Assert.Equal(66.8, dx, 9); Assert.Equal(0.0, dy, 9);
    }
}

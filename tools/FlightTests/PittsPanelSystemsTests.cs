using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Pitts' cockpit systems (Ty 2026-11-04 19:22): the rear panel's four switches MASTER, ALTERNATOR, FUEL PUMP and
/// SMOKE on a real bus (ElectricalSystem, stepped by Aircraft.Step), and the PROP lever, front and rear, as the governor's
/// rpm (Aircraft.PropLever01). Expected numbers are worked by hand from the constants in the class comments, not read
/// back from the code (CLAUDE.md: a test that reads the file it is checking proves nothing).
/// </summary>
public sealed class PittsPanelSystemsTests
{
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Aircraft Pitts(double throttle01, AircraftConfig? cfg = null)
    {
        cfg ??= Load("pitts-s2b-like");
        var state = new RigidBodyState(new Vec3(0, 0, -600), new Quat(0, 0, 0, 1), new Vec3(60, 0, 2), Vec3.Zero);
        var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
        ac.SetReplayPose(state, ControlDeflections.Neutral, throttle01, 0.0, 1.0, 1.0);
        return ac;
    }

    // ---- the bus ----

    [Fact]
    public void WithTheEngineRunningTheAlternatorCarriesTheBus()
    {
        var e = new ElectricalSystem();
        e.Step(1.0, 2400);
        Assert.True(e.Charging);
        Assert.Equal(14.2, e.BusVolts, 6);
        Assert.Equal(0.0, e.Amps, 6);   // a full battery takes no charge
        Assert.True(e.BusLive);
    }

    [Fact]
    public void MasterOffKillsTheBusAndTheAmmeterButNotTheEngine()
    {
        var e = new ElectricalSystem { Master = false, FuelPump = true, SmokePump = true };
        e.Step(1.0, 2400);
        Assert.False(e.BusLive);
        Assert.Equal(0.0, e.BusVolts, 9);
        Assert.Equal(0.0, e.Amps, 9);
        Assert.Equal(1.0, e.Charge01, 9);   // nothing drawn through an open contactor
    }

    [Fact]
    public void AlternatorOffTheBatteryCarriesTheLoadAndRunsFlat()
    {
        // 2 A base + 3 A pump + 6 A smoke = 11 A from a 15 Ah battery: flat in 15/11 h = 4909 s.
        var e = new ElectricalSystem { Alternator = false, FuelPump = true, SmokePump = true };
        e.Step(1.0, 2400);
        Assert.False(e.Charging);
        Assert.Equal(-11.0, e.Amps, 6);
        Assert.Equal(12.6, e.BusVolts, 2);   // the battery's own voltage, nearly full
        for (int i = 0; i < 4800; i++) e.Step(1.0, 2400);
        Assert.True(e.BusLive);
        for (int i = 0; i < 200; i++) e.Step(1.0, 2400);
        Assert.False(e.BusLive);
        Assert.Equal(0.0, e.Charge01, 9);
    }

    [Fact]
    public void TheAlternatorIsOfflineBelowItsCutInRpm()
    {
        var e = new ElectricalSystem();
        e.Step(1.0, 700);   // the Pitts' idle
        Assert.False(e.Charging);
        Assert.Equal(-2.0, e.Amps, 6);
    }

    [Fact]
    public void ADownBatteryChargesWhenTheAlternatorComesBack()
    {
        var e = new ElectricalSystem();
        e.SetCharge(0.5);
        e.Step(1.0, 2400);
        Assert.Equal(5.0, e.Amps, 6);   // 10 A x (1 - 0.5)
        Assert.Equal(14.2, e.BusVolts, 6);
    }

    [Fact]
    public void EveryAircraftStartsWithMasterAndAlternatorOnAndThePumpOff()
    {
        Aircraft ac = Pitts(0.7);
        Assert.True(ac.Electrics.Master);
        Assert.True(ac.Electrics.Alternator);
        Assert.False(ac.Electrics.FuelPump);
        ac.Step(new ControlInputs(0, 0, 0, -0.4), 0.01);
        Assert.True(ac.Electrics.Charging);   // the sim steps the bus with the engine's own rpm
    }

    // ---- the prop lever ----

    [Fact]
    public void ThePittsGovernorRunsFrom2000To2700()
    {
        // Ty-checkable: (E) 2000 low stop, the config's governedRpm 2500 and maxRpm 2700 are the S-2B's AEIO-360 + Hartzell.
        PropulsionConfig p = Load("pitts-s2b-like").Propulsion!;
        Assert.Equal(2000.0, p.GovernedMinRpm, 9);
        Assert.Equal(2500.0, p.GovernedRpm, 9);
        Assert.Equal(2700.0, p.MaxRpm, 9);
    }

    [Fact]
    public void LeverForwardIsTheAeroplaneAsItAlwaysFlew()
    {
        Aircraft ac = Pitts(0.7);
        Assert.Equal(1.0, ac.PropLever01, 9);
        Assert.Equal(2500.0, ac.EngineRpm, 6);   // governing at cruise power
        Assert.Equal(1.0, ac.PropPowerScale, 12);
    }

    [Fact]
    public void LeverBackTheGovernorHoldsFewerRpmAndThePowerFallsWithThem()
    {
        Aircraft ac = Pitts(0.7);
        ac.PropLever01 = 0.0;
        Assert.Equal(2000.0, ac.EngineRpm, 6);
        Assert.Equal(0.8, ac.PropPowerScale, 9);          // 2000 / 2500 at the same manifold pressure
        ac.PropLever01 = 0.5;                              // 2000 + 0.5 x 700 = 2350
        Assert.Equal(2350.0, ac.EngineRpm, 6);
        Assert.Equal(0.94, ac.PropPowerScale, 9);
        double fullFlow = Pitts(0.7).FuelFlowKgPerSec;
        Assert.Equal(0.94 * fullFlow, ac.FuelFlowKgPerSec, 12);
    }

    [Fact]
    public void OnTheFinePitchStopTheLeverChangesNothing()
    {
        Aircraft ac = Pitts(0.1);
        double rpm = ac.EngineRpm;
        ac.PropLever01 = 0.0;
        Assert.Equal(rpm, ac.EngineRpm, 9);
        Assert.Equal(1.0, ac.PropPowerScale, 12);
    }

    [Fact]
    public void ATypeWithNoGovernorRangeIgnoresTheLever()
    {
        // The Extra is constant-speed too, but nobody has given it a low stop: the lever must not move it.
        AircraftConfig c = Load("extra-300-like");
        Assert.True(c.Propulsion!.ConstantSpeed);
        Aircraft ac = Pitts(0.7, c);
        double rpm = ac.EngineRpm;
        ac.PropLever01 = 0.0;
        Assert.Equal(rpm, ac.EngineRpm, 9);
        Assert.Equal(1.0, ac.PropPowerScale, 12);
    }

    [Fact]
    public void LeverBackClimbsLessAtFullThrottle()
    {
        static double SpeedAfter(double lever)
        {
            Aircraft ac = Pitts(1.0);
            ac.PropLever01 = lever;
            var loop = new SimLoop(ac, 1.0 / 200.0);
            for (int i = 0; i < 400; i++) loop.RunFor(1.0 / 200.0, new ControlInputs(0, 0, 0, -1.0));
            return ac.State.Velocity.Length;
        }
        Assert.True(SpeedAfter(0.0) < SpeedAfter(1.0) - 0.2);
    }

    // ---- the rig and the panel ----

    [Fact]
    public void TheRigCarriesTheNewSwitchesAndTheParkedCockpitHasTheAlternatorOn()
    {
        RigInputs r = RigInputs.Parked();
        Assert.True(r.TryGet("alternator", out double alt));
        Assert.Equal(1.0, alt);
        Assert.True(r.TryGet("fuel_pump", out double pump));
        Assert.Equal(0.0, pump);
        Assert.True(r.TryGet("master", out double master));
        Assert.Equal(1.0, master);
    }

    [Fact]
    public void ALeverFollowsItsArcAndNeverFlips()
    {
        var spec = new PanelControlSpec { Node = "PropLever_Front", Param = "prop", Seat = "Front", Kind = PanelKind.Lever, Lo = 0, Hi = 1 };
        var c = new PanelControl(spec, 1.0);
        Assert.Equal("PropLever", spec.Short);
        c.Grab(new Quat(0, 0, 0, 1));
        c.FollowTravel(0.3);
        Assert.Equal(0.3, c.Value, 9);
        c.FollowTravel(0.7);
        Assert.Equal(0.7, c.Value, 9);
        c.Release();
        Assert.Equal(PanelEvent.None, c.Poke(0.0));   // nothing to poke on a lever
        Assert.Equal(0.7, c.Value, 9);
    }
}

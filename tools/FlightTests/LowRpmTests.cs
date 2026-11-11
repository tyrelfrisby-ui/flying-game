using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE BOTTOM OF THE RPM RANGE. Ty 2026-11-11:
///
///   "Sometimes lower like a tail slide where the reversed direction works against the prop and can actually stop the
///    engine if rpm falls below around 300 rpm. Model impulse couplings that make a snapping sound and retard
///    ignition to TDC at rpm below 400. Once on[,] the impulse coupling[,] if rpm drops below 300 the friction should
///    be too high for the engine to overcome static and the engine simply winds down and stops"
///
/// 300 and 400 are HIS numbers. Everything else here is the model's own machinery doing what they say.
/// </summary>
[Collection("WorldTerrainActive")]
public class LowRpmTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public LowRpmTests(ITestOutputHelper o) { _o = o; Calm(); }
    public void Dispose() => Calm();

    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
    }

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    private static RigidBodyState Held(double vx)
        => new(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(vx, 0, 0), Vec3.Zero);

    /// <summary>
    /// IT STOPS, AND IT STAYS STOPPED — which is the end of the compression ratchet, with Ty's number rather than
    /// one of mine. The clamp at zero in StepShaft discards the ripple's negative half and keeps its positive one, so
    /// a stopped propeller used to TWITCH for ever: measured on the commit before this, mixture cut at 25 m/s and
    /// held, the Pitts reached 0.00 rpm at 2.0 s and then read 1.55, 0.00, 1.24, 0.32, 1.22 rpm second after second
    /// for the full minute, the J-3 the same with 2.7 rpm flicks. Crumbs rather than the ~200 rpm it was first
    /// recorded as, but crumbs that never stop — and stopped-propeller drag cannot be measured honestly against a
    /// propeller that will not stand still.
    ///
    /// Static friction latches it. Now: 0.00 rpm from 2.0 s to 60 s, not one twitch.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    [InlineData("extra-300-like")]
    public void ADeadEngineWindsDownAndStaysStopped(string id)
    {
        var ac = new Aircraft(Load(id), Held(25.0), ControlDeflections.Neutral);
        ac.MixtureLever = 0.0;                                  // idle cut-off
        double twitched = 0.0, stoppedAt = double.NaN, t = 0.0;
        for (int i = 0; i < 6000; i++)                          // 30 s
        {
            ac.Step(new ControlInputs(0, 0, 0, 1.0), SimLoop.DefaultFixedDtSec);
            ac.State = Held(25.0);
            t += SimLoop.DefaultFixedDtSec;
            if (double.IsNaN(stoppedAt) && ac.ShaftRpm <= 0.0) stoppedAt = t;
            else if (!double.IsNaN(stoppedAt) && ac.ShaftRpm > twitched) twitched = ac.ShaftRpm;
        }
        _o.WriteLine($"{id}: stopped at {stoppedAt:F1} s, worst twitch afterwards {twitched:F3} rpm, final {ac.ShaftRpm:F3}");
        Assert.False(double.IsNaN(stoppedAt), $"{id}: it has to stop");
        Assert.True(stoppedAt < 10.0, $"{id}: and stop promptly ({stoppedAt:F1} s)");
        Assert.Equal(0.0, twitched, 9);                         // not one twitch after it stops
        Assert.Equal(0.0, ac.ShaftRpm, 9);
        Assert.True(ac.ShaftHeldByStaticFriction, $"{id}: held by static friction, not merely passing through zero");
    }

    /// <summary>The floor catches a DECELERATING shaft only. A relight has to be able to take the same engine back up
    /// through 300 rpm, or the rule would make every stopped engine unstartable.</summary>
    [Fact]
    public void ARelightTakesItBackUpThroughTheFloor()
    {
        var ac = new Aircraft(Load("j3-cub-like"), Held(25.0), ControlDeflections.Neutral);
        ac.MixtureLever = 0.0;
        var sim = new SimLoop(ac);
        for (int i = 0; i < 4000; i++) { ac.Step(new ControlInputs(0, 0, 0, 1.0), SimLoop.DefaultFixedDtSec); ac.State = Held(25.0); }
        Assert.Equal(0.0, ac.ShaftRpm, 9);
        Assert.True(ac.ShaftHeldByStaticFriction);

        ac.MixtureLever = 1.0;                                   // mixture back in, throttle open
        for (int i = 0; i < 4000; i++) { ac.Step(new ControlInputs(0, 0, 0, -1.0), SimLoop.DefaultFixedDtSec); ac.State = Held(25.0); }
        _o.WriteLine($"J-3 after 20 s with the mixture back in and the throttle open: {ac.ShaftRpm:F0} rpm, running {ac.EngineRunning}");
        Assert.True(ac.ShaftRpm > ImpulseCoupling.StaticFrictionStopRpm,
                    $"the floor must not trap a restart ({ac.ShaftRpm:F0} rpm)");
        Assert.False(ac.ShaftHeldByStaticFriction);
    }

    // ---- the impulse coupling ---------------------------------------------------------------------------------

    /// <summary>Engaged below 400, out above it, with the spark retarded to TDC while it is in. Ty's 400.</summary>
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(100.0, true)]
    [InlineData(399.0, true)]
    [InlineData(400.0, false)]
    [InlineData(700.0, false)]
    [InlineData(2700.0, false)]
    public void TheImpulseCouplingLetsGoBelowFourHundred(double rpm, bool engaged)
    {
        Assert.Equal(engaged, ImpulseCoupling.Engaged(rpm));
        Assert.Equal(engaged ? ImpulseCoupling.RetardedAdvanceDeg : ImpulseCoupling.NormalAdvanceDeg,
                     ImpulseCoupling.AdvanceDeg(rpm));
        if (!engaged) Assert.Equal(0.0, ImpulseCoupling.SnapsPerSecond(rpm, 4));
    }

    /// <summary>The snap rate is the firing rate, because it is the same event — one per cylinder per two crank
    /// revolutions. At 200 rpm a four cylinder snaps 6.7 times a second, which is the sound of a prop coming round.</summary>
    [Fact]
    public void TheSnapRateIsTheFiringRate()
    {
        Assert.Equal(200.0 / 60.0 * 4 * 0.5, ImpulseCoupling.SnapsPerSecond(200.0, 4), 9);
        Assert.Equal(200.0 / 60.0 * 6 * 0.5, ImpulseCoupling.SnapsPerSecond(200.0, 6), 9);
        Assert.Equal(0.0, ImpulseCoupling.SnapsPerSecond(500.0, 6));     // out of engagement: nothing to release
    }

    /// <summary>And the aeroplane reports it, so the cockpit and the audio read the same number the physics does.</summary>
    [Fact]
    public void TheAeroplaneReportsTheCouplingWhileItWindsDown()
    {
        var ac = new Aircraft(Load("j3-cub-like"), Held(25.0), ControlDeflections.Neutral);
        Assert.False(ac.ImpulseCouplingEngaged);                 // running: well above cut-out
        Assert.Equal(ImpulseCoupling.NormalAdvanceDeg, ac.IgnitionAdvanceDeg);

        ac.MixtureLever = 0.0;
        bool sawEngaged = false;
        double maxSnaps = 0.0;
        for (int i = 0; i < 4000; i++)
        {
            ac.Step(new ControlInputs(0, 0, 0, 1.0), SimLoop.DefaultFixedDtSec);
            ac.State = Held(25.0);
            if (ac.ImpulseCouplingEngaged)
            {
                sawEngaged = true;
                maxSnaps = Math.Max(maxSnaps, ac.ImpulseSnapsPerSecond);
                Assert.Equal(ImpulseCoupling.RetardedAdvanceDeg, ac.IgnitionAdvanceDeg);
            }
        }
        _o.WriteLine($"J-3 winding down: coupling engaged at some point {sawEngaged}, fastest snap rate {maxSnaps:F1}/s");
        Assert.True(sawEngaged, "winding down from 1,274 rpm to a stop has to pass through the coupling's range");
        Assert.True(maxSnaps > 0.0);
        Assert.Equal(0.0, ac.ImpulseSnapsPerSecond);             // and stopped, it snaps no more
    }
}

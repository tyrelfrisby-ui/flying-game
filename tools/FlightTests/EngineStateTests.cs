using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Engine;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The engine is more than its throttle (Ty 2026-11-04, Quest 3S build 2026.1104.1656):
///  - Extra: after a prop strike "the prop was completely stopped but if i had the throttle up it would still yaw with
///    rudder". The tail's slipstream came from the throttle alone (Aircraft.TailSlipstream), so a prop that had gone
///    still blew over the rudder at full power.
///  - Great Lakes: "the prop control lever moves but doesn't change rpm, and the mixture is stuck at full rich". The sim
///    had neither a governor lever nor a mixture.
/// Expected numbers are literals worked by hand from the model's stated constants (MixtureModel, the configs), or, for
/// the running engine, copied from the base commit 73d7702b's output so "unchanged" is a measurement.
/// </summary>
[Collection("WorldTerrainActive")]
public class EngineStateTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public EngineStateTests(ITestOutputHelper o) { _out = o; Calm(); }
    public void Dispose() => Calm();

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
    }

    private const double Rho0 = 1.225;
    private const double FullThrottle = -1.0;   // the lever: −1 full power, +1 idle

    private static double Heading(Aircraft ac) { var q = ac.State.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)); }

    private static Aircraft OnTheGround(AircraftConfig c) => new(c, LandingGear.RestingState(c, 0, 0, 0), ControlDeflections.Neutral);

    private static Aircraft Flying(AircraftConfig c, double speedMs, double altitudeM = 1500) =>
        new(c, new RigidBodyState(new Vec3(0, 0, -altitudeM), Quat.Identity, new Vec3(speedMs, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    // ---- 4. prop blast over the tail ------------------------------------------------------------------------------

    [Fact]
    public void ExtraStoppedPropFullThrottleBlowsNothingOverTheTail()
    {
        var ac = OnTheGround(Load("extra-300-like"));
        ac.LoseComponent(AirframeComponent.Propeller);   // the strike's own outcome for a single (Aircraft.PropStrike)
        var sim = new SimLoop(ac);
        sim.RunFor(0.5, new ControlInputs(0, 0, 1.0, FullThrottle));
        _out.WriteLine($"rpm {ac.EngineRpm:F0} running {ac.EngineRunning} slip {ac.LastTailSlipstream}");
        Assert.Equal(1.0, ac.Throttle01);
        Assert.False(ac.EngineRunning);
        Assert.Equal(0.0, ac.EngineRpm);
        Assert.Equal((0.0, 0.0, 0.0), ac.LastTailSlipstream);
        Assert.Equal(0.0, ac.ShaftPower01);
    }

    [Fact]
    public void ExtraStoppedPropFullThrottleFullRudderDoesNotYawOnTheGround()
    {
        var c = Load("extra-300-like");
        // Running, for contrast: the same three seconds blow the tail round (and roll it forward).
        var live = OnTheGround(c);
        double h0 = Heading(live);
        new SimLoop(live).RunFor(3.0, new ControlInputs(0, 0, 1.0, FullThrottle));
        double liveYawDeg = Math.Abs(Heading(live) - h0) * 180 / Math.PI;

        // Stopped prop, full right rudder: full throttle and closed throttle must be the same aeroplane (Ty's report was
        // exactly that the throttle still mattered). The gear settling and the tailwheel's steering link move it a
        // hair either way; the throttle must add nothing to that.
        double DeadYaw(double lever)
        {
            var dead = OnTheGround(Load("extra-300-like"));
            dead.LoseComponent(AirframeComponent.Propeller);
            double d0 = Heading(dead);
            new SimLoop(dead).RunFor(3.0, new ControlInputs(0, 0, 1.0, lever));
            return (Heading(dead) - d0) * 180 / Math.PI;
        }
        double full = DeadYaw(FullThrottle), idle = DeadYaw(1.0);
        _out.WriteLine($"3 s full right rudder: running engine full throttle {liveYawDeg:F2}° of heading; stopped prop {full:F6}° at full throttle, {idle:F6}° closed");
        Assert.True(liveYawDeg > 2.0, $"running engine: the blast should yaw it ({liveYawDeg:F2}°)");
        Assert.Equal(idle, full, 9);
        Assert.True(Math.Abs(full) < 0.2);
    }

    [Fact]
    public void DeadEngineFullThrottleMakesNoBlastTorqueOrFuelFlow()
    {
        var ac = OnTheGround(Load("extra-300-like"));
        ac.MixtureLever = 0.0;   // idle cut-off
        var sim = new SimLoop(ac);
        sim.RunFor(0.5, new ControlInputs(0, 0, 0, FullThrottle));
        Assert.False(ac.EngineRunning);
        Assert.True(ac.EngineStarved);
        Assert.False(ac.EngineStopped);              // starved, not damaged
        Assert.Equal(0.0, ac.EngineRpm);             // standing still: nothing turns the prop
        Assert.Equal((0.0, 0.0, 0.0), ac.LastTailSlipstream);
        Assert.Equal(0.0, ac.FuelFlowKgPerSec);
        (Vec3 f, Vec3 m) = Aircraft.PropulsionForces(ac.Config, 1.0, Vec3.Zero, Vec3.Zero, Rho0, false, default, null, ac.Drive);
        Assert.Equal(0.0, f.X);
        Assert.Equal(Vec3.Zero, m);
    }

    [Fact]
    public void WindmillingPropIsASmallDragAndAWakeDeficitNeverABlast()
    {
        var c = Load("extra-300-like");
        // 65 m/s, AND THE SPEED IS PART OF THE TEST. It was 50, which is no longer a windmilling condition at all: with
        // the dry boundary friction solved against Ty's stall anchor (PistonEngine.BoundaryExcessFor, 2026-11-10) the
        // Extra's COARSE oil-loss stop cannot be driven at 50 m/s at 1,500 m - the propeller is simply stopped, and
        // the stopped case is pinned just below. This test is about what a TURNING one does to the tail, so it is
        // asked at a speed where it turns.
        var ac = Flying(c, 65.0);
        ac.MixtureLever = 0.0;
        new SimLoop(ac).RunFor(0.05, new ControlInputs(0, 0, 0, FullThrottle));
        double v = ac.State.Velocity.Length;
        // IT IS NO LONGER A FLAT J = 1 GUESS. Until 2026-11-10 a dead propeller was assumed to turn at one advance
        // ratio whatever its blades were doing, which could not tell a fine stop from a coarse one - and the oil-loss
        // rule is entirely about which stop it is on. Now the rpm is where the blade's own torque balances the
        // engine's cranking resistance (PropModel.Windmilling), which for the Extra's COARSE oil-loss stop up here is
        // around 1,100 rather than 1,900. Checked against the model's own solve so the two cannot drift, and bounded
        // so a change of mechanism still has to come past this test.
        double solved = PropModel.WindmillRpm(c.Propulsion!, v, Atmosphere.DensityAtAltitude(-ac.State.Position.Z));
        Assert.Equal(solved, ac.EngineRpm, 6);
        Assert.InRange(ac.EngineRpm, 600.0, 1200.0);
        (double du, _, double swirl) = ac.LastTailSlipstream;
        _out.WriteLine($"windmilling at {v:F1} m/s: {ac.EngineRpm:F0} rpm, tail du {du:F3} m/s swirl {swirl:F3}");
        // Momentum theory with the windmill's OWN drag: V_slip = V·√(1 − C_D), 90 % developed. The coefficient is no
        // longer the authored 0.15 — a dead constant-speed propeller's drag is solved at the stop its oil loss left it
        // on (PropModel.Windmilling), and the Extra's coarse stop up here is a third of that. Derived from the same
        // solve rather than written down, so the two cannot drift apart.
        double discM2 = Math.PI * 0.25 * c.Propulsion!.PropDiameterM * c.Propulsion.PropDiameterM;
        double rhoHere = Atmosphere.DensityAtAltitude(-ac.State.Position.Z);
        double cdDisc = PropModel.Windmilling(c.Propulsion, ac.State.Velocity.X, rhoHere).DragN
                        / (0.5 * rhoHere * ac.State.Velocity.X * ac.State.Velocity.X * discM2);
        Assert.Equal(0.9 * (Math.Sqrt(1 - cdDisc) - 1) * ac.State.Velocity.X, du, 1);
        Assert.True(du < 0.0 && du > -0.08 * v, $"a small deficit, not a blast ({du:F3})");
        Assert.True(swirl < 0.0, "a windmill swirls the other way");

        // AND A COARSE BLADE THE AIR CANNOT DRIVE READS ZERO, not a guess. PropModel.Windmilling answers NaN both for a
        // type this does not apply to and for a blade that is stopped, and until 2026-11-10 the tachometer fell through
        // to the flat J = 1 assumption in both cases: a standing propeller read 1,498 rpm. On a type whose stops are
        // fitted, NaN is an answer — it is stopped.
        var slow = Flying(c, 50.0);
        slow.MixtureLever = 0.0;
        new SimLoop(slow).RunFor(0.05, new ControlInputs(0, 0, 0, FullThrottle));
        double rhoSlow = Atmosphere.DensityAtAltitude(-slow.State.Position.Z);
        Assert.True(double.IsNaN(PropModel.WindmillRpm(c.Propulsion!, slow.State.Velocity.Length, rhoSlow)),
                    "the Extra's coarse oil-loss stop is not driveable at 50 m/s up here");
        Assert.Equal(0.0, slow.EngineRpm);
        (Vec3 f, Vec3 m) = PropModel.Compute(c.Propulsion!, 1.0, new Vec3(50, 0, 0), new Vec3(0, 0.5, 0), Rho0, PropModel.PropDrive.Dead(1500), out PropModel.Parts parts);
        Assert.True(f.X < 0.0, "drag");
        Assert.Equal(0.0, parts.TorqueRoll);
        Assert.Equal(0.0, parts.SlipYaw);
        Assert.Equal(0.0, parts.PFactorY);
        // Gyro: h = Ip·ω = 2.1 kg m² × 1500 rpm × 2π/60; a 0.5 rad/s pitch rate gives N = q·h.
        // 2.1 is extra-300-like's own propInertia, hand-copied as the tripwire this is meant to be. It was 2.5
        // until the Extra went onto its derived figure (2026-11-09) and this literal was not followed through,
        // so the test had been failing on a stale number rather than on anything the model does.
        Assert.Equal(0.5 * 2.1 * 1500 * 2 * Math.PI / 60, parts.Gyro.Z, 6);
    }

    [Fact]
    public void StoppedPropHasNoTorquePFactorSpiralSlipstreamOrGyro()
    {
        var p = Load("extra-300-like").Propulsion!;
        var alphaBeta = new Vec3(30, 4, 6);   // α ≈ 11°, β ≈ 7°: where P-factor would show
        (_, Vec3 m) = PropModel.Compute(p, 1.0, alphaBeta, new Vec3(0, 0.5, 0.5), Rho0, PropModel.PropDrive.Dead(0.0), out PropModel.Parts parts);
        Assert.Equal(0.0, parts.TorqueRoll);
        Assert.Equal(0.0, parts.PFactorY);
        Assert.Equal(0.0, parts.PFactorZ);
        Assert.Equal(0.0, parts.SlipYaw);
        Assert.Equal(Vec3.Zero, parts.Gyro);
        Assert.Equal(0.0, m.X);
        Assert.Equal(0.0, m.Z);
    }

    [Fact]
    public void RunningEngineBlastIsUnchanged()
    {
        // Copied from base 73d7702b (Aircraft.TailSlipstream, the same call): Extra, full throttle, standing still, sea level.
        var c = Load("extra-300-like");
        (double du, double r, double sw) = Aircraft.TailSlipstream(c, 1.0, Vec3.Zero, Vec3.Zero, Rho0);
        _out.WriteLine($"du {du:R} r {r:R} swirl {sw:R}");
        Assert.Equal(BaseStaticDu, du, 9);
        Assert.Equal(0.7, r, 12);
        (double du2, _, _) = Aircraft.TailSlipstream(c, 1.0, Vec3.Zero, Vec3.Zero, Rho0, PropModel.PropDrive.Running, propGone: false);
        Assert.Equal(du, du2);
        // And through a live aircraft at full rich, high rpm.
        var ac = OnTheGround(c);
        new SimLoop(ac).RunFor(0.005, new ControlInputs(0, 0, 0, FullThrottle));
        Assert.True(ac.EngineRunning);
        Assert.Equal(1.0, ac.PowerScale);
        Assert.Equal(BaseStaticDu, ac.LastTailSlipstream.DeltaU, 1);
    }

    /// <summary>Extra 300, full throttle, V = 0, ρ 1.225: base 73d7702b printed this.</summary>
    private const double BaseStaticDu = 42.886981849718921;

    // ---- 5. mixture ------------------------------------------------------------------------------------------------

    [Fact]
    public void MixtureModelNumbers()
    {
        // φ at full rich, sea level and σ 0.786 (≈ 8,000 ft).
        Assert.Equal(1.25, MixtureModel.Phi(1.0, 1.0), 12);
        Assert.Equal(1.25 / 0.786, MixtureModel.Phi(1.0, 0.786), 12);
        // Power relative to full rich: best power (φ 1.13) at sea level +0.73 %; at σ 0.786 +11.85 %.
        Assert.Equal(1.0, MixtureModel.PowerFactor(1.0, 1.0), 12);
        Assert.Equal(1.0 / (1 - 0.5 * 0.12 * 0.12), MixtureModel.PowerFactor(0.05 + 0.95 * 1.13 / 1.25, 1.0), 9);
        double rich8k = 1 - 0.5 * Math.Pow(1.25 / 0.786 - 1.13, 2);
        Assert.Equal(1.0 / rich8k, MixtureModel.PowerFactor(0.05 + 0.95 * 1.13 * 0.786 / 1.25, 0.786), 9);
        Assert.InRange(1.0 / rich8k, 1.118, 1.119);
        // Lean of peak: φ 0.7 makes 67.6 % of full-rich power and runs a third of the way to quitting.
        double lean = 0.05 + 0.95 * 0.7 / 1.25;
        Assert.Equal((1 - 1.78 * 0.43 * 0.43) / (1 - 0.5 * 0.12 * 0.12), MixtureModel.PowerFactor(lean, 1.0), 9);
        Assert.Equal(1.0 / 3.0, MixtureModel.Roughness(0.7), 9);
        // Will not fire below φ 0.6, nor at cut-off.
        Assert.False(MixtureModel.Fires(0.05 + 0.95 * 0.59 / 1.25, 1.0));
        Assert.True(MixtureModel.Fires(0.05 + 0.95 * 0.61 / 1.25, 1.0));
        Assert.False(MixtureModel.Fires(0.05, 1.0));
        Assert.Equal(0.0, MixtureModel.FuelFraction(0.0));
        // EGT: peak 1,450 °F at full power, 1,000 + 450·√0.75 − 650 × 0.25 full rich at 75 %.
        Assert.Equal(1450.0, MixtureModel.EgtF(1.0, 1.0), 9);
        Assert.Equal(1000 + 450 * Math.Sqrt(0.75) - 162.5, MixtureModel.EgtF(1.25, 0.75), 9);
    }

    [Fact]
    public void IdleCutoffStopsTheEngineAndPushingItInRestartsIt()
    {
        var ac = OnTheGround(Load("great-lakes-like"));
        var sim = new SimLoop(ac);
        sim.RunFor(0.2, new ControlInputs(0, 0, 0, 1.0));   // idle
        Assert.True(ac.EngineRunning);
        Assert.Equal(700.0, ac.EngineRpm, 6);
        ac.MixtureLever = 0.0;
        sim.RunFor(0.05, new ControlInputs(0, 0, 0, 1.0));
        Assert.False(ac.EngineRunning);
        Assert.Equal(0.0, ac.EngineRpm);
        ac.MixtureLever = 1.0;
        sim.RunFor(0.5, new ControlInputs(0, 0, 0, 1.0));
        Assert.False(ac.EngineRunning);                 // not yet: it takes RelightSec (1 s) to catch
        sim.RunFor(0.6, new ControlInputs(0, 0, 0, 1.0));
        Assert.True(ac.EngineRunning);
        Assert.Equal(700.0, ac.EngineRpm, 6);
    }

    [Fact]
    public void LeaningAFixedPitchEngineChangesRpmAndEgt()
    {
        var ac = Flying(Load("c172-like"), 50.0, altitudeM: 100.0);   // near sea level: full rich is already near best power
        var sim = new SimLoop(ac);
        var cruise = new ControlInputs(0, 0, 0, 1.0 - 2.0 * 0.75);   // 75 % throttle
        sim.RunFor(0.01, cruise);
        double rpmRich = ac.EngineRpm, egtRich = ac.EgtF, sigma = ac.MixturePhi > 0 ? 1.25 / ac.MixturePhi : 1;
        // Lean to peak EGT (φ 1.0): power × 0.97/0.9928, so rpm − idle × that^0.8, EGT up to the peak.
        ac.MixtureLever = 0.05 + 0.95 * 1.0 * sigma / 1.25;
        sim.RunFor(0.01, cruise);
        double rpmPeak = ac.EngineRpm, egtPeak = ac.EgtF;
        _out.WriteLine($"σ {sigma:F3}: full rich {rpmRich:F0} rpm EGT {egtRich:F0} °F; peak EGT {rpmPeak:F0} rpm EGT {egtPeak:F0} °F");
        Assert.True(egtPeak > egtRich + 100.0, "leaning to peak raises EGT");
        Assert.True(rpmPeak < rpmRich, "a little past best power: a little less rpm");
        Assert.True(rpmRich - rpmPeak < 60.0);
        // Far too lean: rough, then dead, the prop windmilling.
        ac.MixtureLever = 0.05 + 0.95 * 0.7 * sigma / 1.25;
        sim.RunFor(0.01, cruise);
        Assert.True(ac.EngineRoughness > 0.3);
        Assert.True(ac.EngineRpm < rpmPeak - 150.0);
        ac.MixtureLever = 0.2;
        sim.RunFor(0.01, cruise);
        Assert.False(ac.EngineRunning);
        Assert.Equal(60.0 * ac.State.Velocity.Length / 1.9, ac.EngineRpm, 6);   // windmilling, D 1.9 m
    }

    // ---- 5. prop lever -----------------------------------------------------------------------------------------------

    [Fact]
    public void GreatLakesPropLeverSetsTheGovernedRpm()
    {
        var ac = Flying(Load("great-lakes-like"), 50.0);
        Assert.True(ac.HasPropLever);
        var sim = new SimLoop(ac);
        var cruise = new ControlInputs(0, 0, 0, 1.0 - 2.0 * 0.75);
        sim.RunFor(0.01, cruise);
        Assert.Equal(2700.0, ac.EngineRpm, 6);            // governedRpm 2700, lever full forward
        Assert.Equal(1.0, ac.PowerScale, 12);
        ac.PropLever = 0.0;
        sim.RunFor(0.01, cruise);
        Assert.Equal(PropModel.FleetLowGovernedRpm, ac.EngineRpm, 6);   // the governor's low stop: 1,200 fleet-wide
        Assert.Equal(1200.0 / 2700.0, ac.PowerScale, 9);  // same manifold pressure, so power goes with the rpm
        ac.PropLever = 0.5;
        sim.RunFor(0.01, cruise);
        // Half lever is half way from the low stop to governedRpm: (1,200 + 2,700) / 2. It was 2,295 when the low stop
        // was 0.7 x MaxRpm.
        Assert.Equal(0.5 * (PropModel.FleetLowGovernedRpm + 2700.0), ac.EngineRpm, 6);
    }

    [Fact]
    public void ExtraPropLeverSetsTheGovernedRpm()
    {
        var ac = Flying(Load("extra-300-like"), 50.0);
        var sim = new SimLoop(ac);
        var cruise = new ControlInputs(0, 0, 0, 1.0 - 2.0 * 0.75);
        sim.RunFor(0.01, cruise);
        Assert.Equal(2500.0, ac.EngineRpm, 6);
        ac.PropLever = 0.0;
        sim.RunFor(0.01, cruise);
        // 1,200, fleet-wide: Ty 2026-11-10, "Governor low stops 1600 across the board", then "Actually 1200 rpm
        // across the board". It was 0.7 x MaxRpm = 1,890, which made a big engine's governor bottom higher than a
        // small one's for no reason anybody could point at - a governor's low stop is set by the unit and its spring.
        Assert.Equal(PropModel.FleetLowGovernedRpm, ac.EngineRpm, 6);
    }

    [Fact]
    public void FixedPitchTypesHaveNoFunctionalPropLever()
    {
        foreach (string id in new[] { "c172-like", "j3-cub-like", "pa18-cub-like", "decathlon-8kcab-like", "stearman-pt17-like" })
        {
            var ac = Flying(Load(id), 45.0);
            Assert.False(ac.HasPropLever, id);
            var sim = new SimLoop(ac);
            var cruise = new ControlInputs(0, 0, 0, 1.0 - 2.0 * 0.75);
            sim.RunFor(0.01, cruise);
            double rpm = ac.EngineRpm, scale = ac.PowerScale;
            ac.PropLever = 0.0;
            sim.RunFor(0.0, cruise);
            Assert.Equal(rpm, ac.EngineRpm);
            Assert.Equal(scale, ac.PowerScale);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHERE THE BLADES GO WITH NO OIL PRESSURE. Ty 2026-11-10: "aerobatic aircraft with a constant speed propeller
/// default to high pitch with loss of oil pressure like twins do (Seminole beech 18 etc) and standard single engines
/// default to low pitch with no oil pressure".
///
/// The direction is a plumbing fact about each installation, so it is authored per type and pinned here; the
/// consequence — less drag and a slower-turning propeller on a coarse blade — is measured rather than asserted, and
/// the numbers in these tests are what OilLossPitchProbe printed.
/// </summary>
public class OilLossPitchTests
{
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));

    private static PropulsionConfig Prop(string id) => Load(id).Propulsion!;

    [Theory]
    // The aerobatic singles and every twin: the oil holds them fine, so losing it drives them COARSE.
    [InlineData("pitts-s2b-like", true)]
    [InlineData("extra-300-like", true)]
    [InlineData("great-lakes-like", true)]
    [InlineData("seminole-like", true)]
    [InlineData("dc3-like", true)]
    [InlineData("hughes-h4-like", true)]
    // The standard singles: the oil holds them coarse, so losing it springs them FINE.
    [InlineData("cirrus-sr22-like", false)]
    [InlineData("p51d-like", false)]
    [InlineData("glasair3-like", false)]
    [InlineData("dhc2-beaver-like", false)]
    public void EachConstantSpeedTypeIsPlumbedTheWayItsInstallationIs(string id, bool coarse)
    {
        PropulsionConfig p = Prop(id);
        Assert.True(p.ConstantSpeed, id + " is not constant speed");
        Assert.Equal(coarse, p.CoarseOnOilLoss);
    }

    [Fact]
    public void TheBladesGoToTheStopThatPlumbingTakesThemTo()
    {
        PropulsionConfig pitts = Prop("pitts-s2b-like");
        Assert.Equal(pitts.CoarseStopDeg, PropModel.OilLossPitchDeg(pitts), 6);   // aerobatic: coarse

        // Same aeroplane plumbed as a standard single springs the other way, with nothing else changed.
        PropulsionConfig standard = Prop("pitts-s2b-like");
        standard.CoarseOnOilLoss = false;
        Assert.Equal(standard.FineStopDeg, PropModel.OilLossPitchDeg(standard), 6);
    }

    [Fact]
    public void AFixedPitchPropAndAnUnfittedGovernorHaveNoOilLossPitchAtAll()
    {
        Assert.Equal(0.0, PropModel.OilLossPitchDeg(Prop("j3-cub-like")));        // fixed pitch
        Assert.Equal(0.0, PropModel.OilLossPitchDeg(Prop("seminole-like")));      // constant speed, stops not fitted
    }

    [Fact]
    public void ADeadEngineIsNotAFreeShaft_itTakesRealTorqueToTurnOver()
    {
        // Ty 2026-11-10: "You are not accounting for the cranking resistance of the engine". The number is the power
        // curve's own friction intercept, not a new constant: a closed throttle draws 0.28 of ambient = 8.4 inHg, below
        // the 10 inHg intercept, so the engine absorbs about 8 % of rated at rated rpm.
        PropulsionConfig p = Prop("pitts-s2b-like");
        double q = PistonEngine.MotoringTorqueNm(p.MaxPowerW, p.MaxRpm, p.MaxRpm, 0.0, 1.0, 1.0);
        Assert.True(q < 0.0, "it must OPPOSE whatever turns it");
        Assert.InRange(-q, 40.0, 75.0);                         // ~56 N·m for a 540 cubic inch engine at 2,700
        // Open the throttle and it is making power, which is not this function's business.
        Assert.Equal(0.0, PistonEngine.MotoringTorqueNm(p.MaxPowerW, p.MaxRpm, p.MaxRpm, 1.0, 1.0, 1.0));
        // It is NOT folded into the power path, where IdleDragCd already carries the same drag against measured glide
        // ratios. Letting it in there would count it twice.
        Assert.True(PistonEngine.TorqueNm(p.MaxPowerW, p.MaxRpm, p.MaxRpm, 0.0, 1.0) >= 0.0);
    }

    [Fact]
    public void TheCrankingResistanceIsWhatPutsTheWindmillingDragWhereTheCalibrationSaysItIs()
    {
        // The authored coefficient's own note: a fine-pitch prop driven by the air is "C_D ~ 0.15-0.25 on disc area".
        // Solved against a FREE shaft the blade element said 0.02-0.07 and I would not have used it. Against the
        // engine's cranking resistance it says 0.19 at approach speed, which is the band. Two independent routes to
        // the same number is the only evidence worth having.
        PropulsionConfig p = Prop("pitts-s2b-like");
        double disc = Math.PI * 0.25 * p.PropDiameterM * p.PropDiameterM;
        var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, p.FineStopDeg, p.ActivityFactor);
        double EngineQ(double r) => PistonEngine.MotoringTorqueNm(p.MaxPowerW, p.MaxRpm, r, 0.0, 1.0, 1.0);

        BladeElement.Windmilling free = BladeElement.Windmill(g, 30.0, 1.225, p.MaxRpm);
        BladeElement.Windmilling cranked = BladeElement.Windmill(g, 30.0, 1.225, p.MaxRpm, EngineQ);
        Assert.InRange(cranked.DragN / (0.5 * 1.225 * 30.0 * 30.0 * disc), 0.15, 0.25);
        Assert.True(free.DragN / (0.5 * 1.225 * 30.0 * 30.0 * disc) < 0.12, "the free shaft is well below the band");
        Assert.True(cranked.Rpm < free.Rpm, "cranking the engine holds the rpm DOWN");
    }

    [Fact]
    public void ACoarseDeadPropIsNearlyFeathered_whichIsWhatTyAskedFor()
    {
        // Told the two stops differed by only a quarter he said: "Much more - nearer feather". The solve with cranking
        // resistance gives it without being asked: the coarse blade can barely turn the engine over, so it turns slowly
        // and takes almost nothing out of the air.
        PropulsionConfig coarse = Prop("pitts-s2b-like");
        PropulsionConfig fine = Prop("pitts-s2b-like");
        fine.CoarseOnOilLoss = false;
        foreach (double v in new[] { 30.0, 45.0, 60.0, 80.0 })
        {
            double dc = PropModel.Windmilling(coarse, v, 1.225).DragN;
            double df = PropModel.Windmilling(fine, v, 1.225).DragN;
            Assert.True(dc > 0.0 && df > 0.0);
            Assert.True(dc < 0.35 * df, $"V={v}: coarse {dc:0} N against fine {df:0} N is not near feather");
        }
    }

    [Fact]
    public void ADeadCoarsePropMakesFarLessDragThroughComputeThanTheSameDeadPropLeftFine()
    {
        PropulsionConfig coarsePlumbed = Prop("pitts-s2b-like");
        PropulsionConfig finePlumbed = Prop("pitts-s2b-like");
        finePlumbed.CoarseOnOilLoss = false;

        var v = new Vec3(60.0, 0.0, 0.0);
        var dead = PropModel.PropDrive.Dead(900.0);
        double tCoarse = PropModel.Compute(coarsePlumbed, 0.0, v, Vec3.Zero, 1.225, dead, out _).Force.X;
        double tFine = PropModel.Compute(finePlumbed, 0.0, v, Vec3.Zero, 1.225, dead, out _).Force.X;

        Assert.True(tCoarse < 0.0 && tFine < 0.0, "a dead propeller drags");
        Assert.True(tCoarse > 0.4 * tFine, $"coarse should drag FAR less: coarse {tCoarse:0.0} N, fine {tFine:0.0} N");
        // A running engine is untouched by any of this - the governor sits on the fine stop, where IdleDragCd was fitted.
        double running = PropModel.Compute(coarsePlumbed, 0.0, v, Vec3.Zero, 1.225, PropModel.PropDrive.Running, out _).Force.X;
        Assert.Equal(PropModel.Compute(finePlumbed, 0.0, v, Vec3.Zero, 1.225, PropModel.PropDrive.Running, out _).Force.X, running, 9);
    }

    [Fact]
    public void TheAirTurnsACoarseDeadPropFarSlowerThanAFineOne()
    {
        PropulsionConfig coarsePlumbed = Prop("pitts-s2b-like");
        PropulsionConfig finePlumbed = Prop("pitts-s2b-like");
        finePlumbed.CoarseOnOilLoss = false;

        foreach (double v in new[] { 30.0, 45.0, 60.0 })
        {
            double rc = PropModel.WindmillRpm(coarsePlumbed, v, 1.225);
            double rf = PropModel.WindmillRpm(finePlumbed, v, 1.225);
            Assert.True(rc > 0.0 && rf > 0.0);
            Assert.True(rc < 0.5 * rf, $"V={v}: coarse {rc:0} rpm against fine {rf:0}");
            // Not quite one advance ratio per stop any more: the engine's cranking torque is nearly constant while the
            // blade's grows with speed, so the balance point creeps toward the free-shaft kinematic value as it goes
            // faster. Coarse: J 2.3 at 30 m/s easing to 1.7 at 80. The ordering is what matters and it never inverts.
            double j = v / (rc / 60.0 * coarsePlumbed.PropDiameterM);
            Assert.InRange(j, 1.65, 2.40);
            Assert.InRange(v / (rf / 60.0 * finePlumbed.PropDiameterM), 0.52, 0.70);
        }

        // A fine blade in a dive still runs away past the red line — the cranking resistance slows it (4,414 free
        // against 4,294 cranked at 80 m/s) but nowhere near enough to save it, which is the other half of why the
        // aerobatic installation is plumbed coarse.
        Assert.True(PropModel.WindmillRpm(finePlumbed, 80.0, 1.225) > finePlumbed.MaxRpm);
        Assert.True(PropModel.WindmillRpm(coarsePlumbed, 80.0, 1.225) < coarsePlumbed.MaxRpm);
    }

    [Fact]
    public void ATypeWithNoFittedStopsKeepsTheFlatWindmillAssumptionItAlwaysHad()
    {
        PropulsionConfig cub = Prop("j3-cub-like");
        Assert.True(double.IsNaN(PropModel.WindmillRpm(cub, 45.0, 1.225)));
        Assert.True(MixtureModel.WindmillRpm(45.0, cub.PropDiameterM, cub.MaxRpm) > 0.0);
    }
}

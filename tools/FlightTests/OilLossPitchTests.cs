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
    public void TheIdlingPropIsUNCHANGED_itsGovernorSitsOnTheFineStopWhereThatCoefficientWasCalibrated()
    {
        PropulsionConfig pitts = Prop("pitts-s2b-like");
        Assert.Equal(1.0, PropModel.DeadPropPitchScale(pitts, pitts.FineStopDeg), 9);
        Assert.Equal(1.0, PropModel.DeadPropPitchScale(Prop("j3-cub-like"), 0.0), 9);
    }

    [Fact]
    public void ACoarseBladeDragsLessThanAFineOneAndAFeatheredOneNotAtAll()
    {
        PropulsionConfig pitts = Prop("pitts-s2b-like");
        double coarse = PropModel.DeadPropPitchScale(pitts, pitts.CoarseStopDeg);
        Assert.InRange(coarse, 0.70, 0.76);                                        // 0.73 measured: cos²33 / cos²11
        Assert.True(coarse < PropModel.DeadPropPitchScale(pitts, pitts.FineStopDeg));
        Assert.Equal(0.0, PropModel.DeadPropPitchScale(pitts, 90.0), 6);            // feathered: edge on, nothing left
        // Monotone the whole way, so there is no angle where coarsening the blade costs MORE drag.
        double prev = double.MaxValue;
        for (double d = pitts.FineStopDeg; d <= 90.0; d += 2.5)
        {
            double k = PropModel.DeadPropPitchScale(pitts, d);
            Assert.True(k <= prev + 1e-12, $"{d:0.0} deg scaled UP to {k:0.000}");
            prev = k;
        }
    }

    [Fact]
    public void ADeadCoarsePropMakesLessDragThroughComputeThanTheSameDeadPropLeftFine()
    {
        PropulsionConfig coarsePlumbed = Prop("pitts-s2b-like");
        PropulsionConfig finePlumbed = Prop("pitts-s2b-like");
        finePlumbed.CoarseOnOilLoss = false;

        var v = new Vec3(60.0, 0.0, 0.0);
        var dead = PropModel.PropDrive.Dead(900.0);
        double tCoarse = PropModel.Compute(coarsePlumbed, 0.0, v, Vec3.Zero, 1.225, dead, out _).Force.X;
        double tFine = PropModel.Compute(finePlumbed, 0.0, v, Vec3.Zero, 1.225, dead, out _).Force.X;

        Assert.True(tCoarse < 0.0 && tFine < 0.0, "a dead propeller drags");
        Assert.True(tCoarse > tFine, $"coarse should drag LESS: coarse {tCoarse:0.0} N, fine {tFine:0.0} N");
        Assert.Equal(0.73, tCoarse / tFine, 2);
        // A running engine is untouched by any of this.
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
            // Zero torque is a KINEMATIC condition, so each is linear in airspeed: one advance ratio per stop.
            Assert.InRange(v / (rc / 60.0 * coarsePlumbed.PropDiameterM), 1.60, 1.72);
            Assert.InRange(v / (rf / 60.0 * finePlumbed.PropDiameterM), 0.52, 0.58);
        }

        // A fine blade in a dive runs away past the red line, with no engine to stop it — the other half of why the
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

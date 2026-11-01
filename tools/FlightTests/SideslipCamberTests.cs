using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Sideslip pitches the nose DOWN on the aerobatic monoplanes and biplanes (owner Ty, 2026-11-01):
///
///   "The extra has a strong yaw pitch coupling with side slip right or left causing a nose down moment due to
///    airflow over the turtleneck being relatively low pressure compared to under the fuselage and air under the
///    cowling being relatively low pressure compared to above." … "The Pitts is similar but less pronounced."
///
/// Two separate forces, both EVEN in beta, so either direction pitches down:
///  - the turtledeck, AFT of the CG, is pulled UP   (CrossflowConfig.SectionCamber*)
///  - the cowl,       AHEAD of the CG, is pulled DOWN (CrossflowConfig.CowlCamber*)
///
/// What this guards. The Extra had the aft half authored and the forward half authored with a ZERO coefficient,
/// and <c>AeroModel</c> gates on <c>&gt; 0.0</c> — so half of the mechanism was in the file, looked present in a
/// diff, and never acted. The Pitts had no camber keys at all, so the effect was absent rather than reduced.
/// Every test below isolates the camber by recomputing with both coefficients zeroed, which is the only way to
/// tell "authored" from "authored and reaching the aeroplane".
/// </summary>
public sealed class SideslipCamberTests
{
    private readonly ITestOutputHelper _out;
    public SideslipCamberTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private const double V = 50.0, AlphaRad = 3.0 * System.Math.PI / 180.0;

    private static Vec3 Vel(double v, double betaRad) =>
        new(v * System.Math.Cos(AlphaRad) * System.Math.Cos(betaRad), v * System.Math.Sin(betaRad),
            v * System.Math.Sin(AlphaRad) * System.Math.Cos(betaRad));

    /// <summary>The pitching moment the CAMBER terms contribute at this sideslip: everything else the aeroplane
    /// does in a slip is subtracted off by recomputing with the two coefficients zeroed.</summary>
    private static double CamberPitch(AircraftConfig cfg, double betaDeg, double v = V)
    {
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double beta = betaDeg * System.Math.PI / 180.0;
        (_, Vec3 withCamber) = AeroModel.Compute(cfg, tables, Vel(v, beta), Vec3.Zero, Vec3.Zero,
                                                 Atmosphere.SeaLevelDensityKgM3, ControlDeflections.Neutral);
        AircraftConfig bare = Load(cfg.Id);
        bare.Fuselage.Crossflow.SectionCamberCl = 0.0;
        bare.Fuselage.Crossflow.CowlCamberCl = 0.0;
        (_, Vec3 without) = AeroModel.Compute(bare, Aircraft.BuildAirfoilTables(bare), Vel(v, beta), Vec3.Zero,
                                              Vec3.Zero, Atmosphere.SeaLevelDensityKgM3, ControlDeflections.Neutral);
        return withCamber.Y - without.Y;
    }

    /// <summary>Both types, both directions: the camber pitches the nose down, and by a recognisable amount.</summary>
    [Theory]
    [InlineData("extra-300-like", 15, -800, -450)]
    [InlineData("extra-300-like", -15, -800, -450)]
    [InlineData("pitts-s2b-like", 15, -330, -120)]
    [InlineData("pitts-s2b-like", -15, -330, -120)]
    public void SideslipPitchesTheNoseDown(string id, double betaDeg, double min, double max)
    {
        AircraftConfig cfg = Load(id);
        double m = CamberPitch(cfg, betaDeg);
        _out.WriteLine($"{id} at beta {betaDeg} deg, {V} m/s: camber pitching moment {m:F0} N.m "
                       + $"({m / cfg.Mass.Inertia.Iyy:F3} rad/s^2 at Iyy {cfg.Mass.Inertia.Iyy})");
        Assert.InRange(m, min, max);
    }

    /// <summary>"side slip right or left": the force goes as the square of the crossflow, so the pitch-down is
    /// the same either way. A term that was odd in beta would pitch UP in one direction, which is the bug this
    /// catches and which no single-direction test can see.</summary>
    [Theory]
    [InlineData("extra-300-like")]
    [InlineData("pitts-s2b-like")]
    public void ItIsTheSameSlippingLeftOrRight(string id)
    {
        AircraftConfig cfg = Load(id);
        double right = CamberPitch(cfg, 15), left = CamberPitch(cfg, -15);
        Assert.True(System.Math.Abs(right - left) <= 0.02 * System.Math.Abs(right),
            $"{id}: {right:F0} N.m slipping right against {left:F0} slipping left — the coupling is not even in beta");
    }

    /// <summary>Each half of the mechanism must actually reach the aeroplane. This is the test that would have
    /// caught `cowlCamberCl: 0.0` sitting in the Extra's config looking like a feature.</summary>
    [Theory]
    [InlineData("extra-300-like")]
    [InlineData("pitts-s2b-like")]
    public void BothTheTurtledeckAndTheCowlAct(string id)
    {
        AircraftConfig cfg = Load(id);
        CrossflowConfig x = cfg.Fuselage.Crossflow;
        Assert.True(x.SectionCamberCl > 0.0 && x.SectionCamberArea > 0.0, $"{id}: no turtledeck camber authored");
        Assert.True(x.CowlCamberCl > 0.0 && x.CowlCamberArea > 0.0, $"{id}: no cowl camber authored");
        Assert.True(x.SectionCamberCenterX < cfg.Mass.CgVec().X, $"{id}: the turtledeck term must act AFT of the CG");
        Assert.True(x.CowlCamberCenterX > cfg.Mass.CgVec().X, $"{id}: the cowl term must act AHEAD of the CG");

        double both = CamberPitch(cfg, 15);
        AircraftConfig noCowl = Load(id); noCowl.Fuselage.Crossflow.CowlCamberCl = 0.0;
        AircraftConfig noDeck = Load(id); noDeck.Fuselage.Crossflow.SectionCamberCl = 0.0;
        double deckOnly = CamberPitch(noCowl, 15), cowlOnly = CamberPitch(noDeck, 15);
        _out.WriteLine($"{id}: turtledeck {deckOnly:F0} N.m + cowl {cowlOnly:F0} = {both:F0}");
        Assert.True(deckOnly < -1.0, $"{id}: the turtledeck contributes {deckOnly:F1} N.m");
        Assert.True(cowlOnly < -1.0, $"{id}: the cowl contributes {cowlOnly:F1} N.m — authored but not acting?");
        Assert.Equal(both, deckOnly + cowlOnly, 6);
    }

    /// <summary>"The Pitts is similar but less pronounced." Compared as angular acceleration, because that is what
    /// the pilot feels — a moment on its own says nothing without the inertia it works against.</summary>
    [Fact]
    public void ThePittsIsTheSameEffectButSmallerThanTheExtras()
    {
        AircraftConfig extra = Load("extra-300-like"), pitts = Load("pitts-s2b-like");
        double e = CamberPitch(extra, 15) / extra.Mass.Inertia.Iyy;
        double p = CamberPitch(pitts, 15) / pitts.Mass.Inertia.Iyy;
        _out.WriteLine($"pitch-down at beta 15 deg, {V} m/s: Extra {e:F3} rad/s^2, Pitts {p:F3} rad/s^2, ratio {e / p:F2}");
        Assert.True(p < 0.0 && e < 0.0, "both must pitch down");
        Assert.True(e < p, $"the Extra's coupling must be the stronger one (Extra {e:F3}, Pitts {p:F3} rad/s^2)");
        Assert.InRange(e / p, 2.0, 5.0);
    }

    /// <summary>A dynamic pressure on an area: double the speed and it quadruples. Anything else means the term is
    /// being fed an angle rather than a velocity.</summary>
    [Theory]
    [InlineData("extra-300-like")]
    [InlineData("pitts-s2b-like")]
    public void ItGrowsWithTheSquareOfTheSpeed(string id)
    {
        AircraftConfig cfg = Load(id);
        double slow = CamberPitch(cfg, 15, 40), fast = CamberPitch(cfg, 15, 80);
        Assert.True(System.Math.Abs(fast / slow - 4.0) < 0.02, $"{id}: {slow:F0} -> {fast:F0} N.m doubling the speed");
    }

    /// <summary>Nothing else in the fleet grew a coupling it was never authored to have.</summary>
    [Theory]
    [InlineData("c172-like")]
    [InlineData("j3-cub-like")]
    [InlineData("great-lakes-like")]
    public void ATypeWithNoCamberAuthoredStillHasNone(string id)
    {
        AircraftConfig cfg = Load(id);
        Assert.Equal(0.0, CamberPitch(cfg, 15), 9);
    }
}

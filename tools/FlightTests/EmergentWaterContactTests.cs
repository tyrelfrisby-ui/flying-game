using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04 20:24 PT: "Swift skim / water contact: no predetermined crash on water touch; emergent forces, damage only
/// by load", and 20:27 (Q024): "SR22/Glasair prop strike ok but must be emergent (prop geometry hits water), not a touch
/// trigger". Two touch rules were found and removed: a wet blade tip stopped the engine outright
/// (AircraftContactDamage.PropStrike, `if (water || …)`), and any water impact of the airframe above 0.5 m/s latched the
/// chase camera's crash view for the rest of the flight (ChaseCamera.CrashImpact). The load path stays:
/// WaterBreakupTests.AWingDugIntoTheWaterLosesOnlyWhatWasOverloaded… (a 40 m/s, 6 m/s-sink wing strike breaks the
/// outer wing on its logged joint load) and AGentleWheelTouchOnWater… (a skim loses nothing).
/// </summary>
[Collection("WorldTerrainActive")]
public class EmergentWaterContactTests
{
    private readonly ITestOutputHelper _o;
    public EmergentWaterContactTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    /// <summary>The water's brake on a blade is hydrodynamic and grows with how deep it dips: an SR22's 0.99 m blades at
    /// 2,700 rpm (282.7 rad/s) — a 5 mm brush ~200 N·m, a 2 cm dip ~1,560 N·m (worked by hand from
    /// Q = n·(arc/π)·½ρ(ωR)²·c·(⅔d)·C·(R−⅓d), n 2, c 0.12 R, C 0.2) — and nothing at all out of the water.</summary>
    [Fact]
    public void TheWaterBrakesAPropByBladeDragNotByTouch()
    {
        double w = 2700 * Math.PI / 30;
        Assert.Equal(0.0, Aircraft.WaterBladeTorque(0.99, 0.0, w));
        Assert.InRange(Aircraft.WaterBladeTorque(0.99, 0.005, w), 150, 250);
        Assert.InRange(Aircraft.WaterBladeTorque(0.99, 0.02, w), 1300, 1800);
        Assert.True(Aircraft.WaterBladeTorque(0.99, 0.02, 700 * Math.PI / 30) < Aircraft.WaterBladeTorque(0.99, 0.02, w));
    }

    /// <summary>The chase camera's crash view opens only for a piece that broke off arriving in the water, never for the
    /// intact airframe touching it, however hard (a hard arrival that breaks something makes a piece, by load).</summary>
    [Fact]
    public void TouchingWaterIsNotACrashOnlyABrokenOffPieceIs()
    {
        var at = new Vec3(0, 0, -3);
        Assert.False(CrashEvents.OpensWaterCrashView(new CrashImpact(ImpactSurface.Water, at, Vec3.Zero, 0.6, 1100, 0.05, -1)));
        Assert.False(CrashEvents.OpensWaterCrashView(new CrashImpact(ImpactSurface.Water, at, Vec3.Zero, 25, 1100, 1.0, -1)));
        Assert.True(CrashEvents.OpensWaterCrashView(new CrashImpact(ImpactSurface.Water, at, Vec3.Zero, 12, 80, 0.7, 3)));
        Assert.False(CrashEvents.OpensWaterCrashView(new CrashImpact(ImpactSurface.Land, at, Vec3.Zero, 12, 80, 0.7, 3)));
    }

    /// <summary>A gentle wheels-first touch on water (0.5 m/s of sink at the skim speed, idle) loses nothing on the 172
    /// and the Cub, and no part of the code path decides "crash" from the water alone.</summary>
    [Theory]
    [InlineData("c172-like")]
    [InlineData("pa18-cub-like")]
    public void AGentleWaterTouchIsNotACrash(string id)
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            WheelWaterTouch.Result r = WheelWaterTouch.Run(Load(id), 0.5, 3.0);
            _o.WriteLine(r.ToString());
            Assert.True(r.TouchAt > 0, "never touched");
            Assert.Empty(r.Lost);
            Assert.False(r.WingsFailed);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>No code decides a crash because the surface is water: the prop's stoppage is the rotor's speed alone, and
    /// the camera's crash view goes through <see cref="CrashFraming.OpensCrashView"/> (a broken-off piece, land or water).</summary>
    [Fact]
    public void NoTouchRuleIsLeft()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        string prop = File.ReadAllText(Path.Combine(d!.FullName, "src", "FlyingGame.Sim", "AircraftContactDamage.cs"));
        Assert.DoesNotContain("if (water ||", prop);
        Assert.Contains("WaterBladeTorque(", prop);
        string cam = File.ReadAllText(Path.Combine(d.FullName, "unity", "Assets", "_Project", "Scripts", "Bridge", "ChaseCamera.cs"));
        Assert.Contains("CrashFraming.OpensCrashView(hit)", cam);
    }
}

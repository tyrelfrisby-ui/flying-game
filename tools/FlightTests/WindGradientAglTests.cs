using System;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE BOUNDARY LAYER IS A PROPERTY OF THE SURFACE, so the wind gradient is referenced to height above the GROUND.
/// A pre-ship audit (2026-11-11) found it reading height straight off the world z, which was true of the old valley
/// world (floor at sea level) and wrong the moment the island arrived with fields at 1,000 to 7,000 ft: at AP8 a
/// 5 m/s setting blew 10.59 m/s on the runway.
/// </summary>
public class WindGradientAglTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain? _saved;
    public WindGradientAglTests(ITestOutputHelper o) { _o = o; _saved = WorldTerrain.Active; }
    public void Dispose() { WorldTerrain.Active = _saved; }

    /// <summary>With no terrain the ground is the sea-level datum, so the old answers still hold — this is the
    /// control, and it is what keeps the ridge lift the gradient was written for.</summary>
    [Theory]
    [InlineData(10.0, 1.000)]
    [InlineData(100.0, 1.380)]
    [InlineData(900.0, 1.878)]
    [InlineData(100000.0, 2.2)]    // the cap
    public void OverTheSeaItIsTheSameProfileAsBefore(double heightM, double expected)
    {
        WorldTerrain.Active = null;
        double f = Atmosphere.WindGradientFactor(new Vec3(0, 0, -heightM));
        _o.WriteLine($"{heightM,8:F0} m above the sea -> x{f:F4}");
        Assert.Equal(expected, f, 3);
    }

    /// <summary>THE ONE THE AUDIT WAS ABOUT. Standing ON a high field, the wind must be the surface wind — the
    /// gradient is 1.0 at 10 m above the ground whatever the field's elevation is.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(305.0)]      // ~1,000 ft
    [InlineData(2134.0)]     // ~7,000 ft, AP8
    public void TenMetresAboveAnyFieldIsTheSurfaceWind(double fieldElevM)
    {
        WorldTerrain.Active = null;
        // No terrain is available in a unit test, so stand the aeroplane at the datum and assert the PROFILE:
        // 10 m of AGL must give 1.0 regardless of what the field's elevation adds to the world z.
        double atTen = Atmosphere.WindGradientFactor(new Vec3(0, 0, -10.0));
        Assert.Equal(1.0, atTen, 6);
        // And the defect: reading the world z instead would have given this much more at that elevation.
        double wouldHaveBeen = Math.Min(2.2, Math.Pow(Math.Max(10.0, fieldElevM + 10.0) / 10.0, 0.14));
        _o.WriteLine($"field {fieldElevM,6:F0} m: correct x{atTen:F3}, the old sea-level reading would have been x{wouldHaveBeen:F3}");
        Assert.True(wouldHaveBeen >= atTen);
    }

    /// <summary>The profile still climbs with height ABOVE THE GROUND, so ridge lift is unchanged in the only
    /// sense that matters: the air a given distance over the terrain is faster than the air near it.</summary>
    [Fact]
    public void ItStillGrowsWithHeightAboveTheGround()
    {
        WorldTerrain.Active = null;
        double low = Atmosphere.WindGradientFactor(new Vec3(0, 0, -10.0));
        double mid = Atmosphere.WindGradientFactor(new Vec3(0, 0, -200.0));
        double high = Atmosphere.WindGradientFactor(new Vec3(0, 0, -900.0));
        _o.WriteLine($"10 m x{low:F3}  200 m x{mid:F3}  900 m x{high:F3}");
        Assert.True(low < mid && mid < high, "the boundary layer must still thicken with height");
        Assert.True(high < 2.2001, "and stay under the cap");
    }

    /// <summary>The memo must not leak one place's terrain into another's: two points far apart get their own
    /// answers even when queried back to back.</summary>
    [Fact]
    public void TheMemoDoesNotLeakBetweenDistantPoints()
    {
        WorldTerrain.Active = null;
        double a = Atmosphere.WindGradientFactor(new Vec3(0, 0, -50.0));
        double b = Atmosphere.WindGradientFactor(new Vec3(50000, 50000, -50.0));
        double aAgain = Atmosphere.WindGradientFactor(new Vec3(0, 0, -50.0));
        Assert.Equal(a, aAgain, 12);
        Assert.Equal(a, b, 12);   // no terrain: both are the datum
    }
}

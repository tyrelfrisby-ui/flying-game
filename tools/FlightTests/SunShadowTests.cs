using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The shadow geometry behind the 2026-11-09 report ("the shadow stays the same size at 50 ft and at 5,000 ft").
/// <see cref="SunShadow"/> is the whole of it; GroundShadow (Unity) only scales its projection by Grow and fades
/// its material by Opacity01, so these are the numbers Ty will see on the ground.
/// </summary>
public sealed class SunShadowTests
{
    private readonly ITestOutputHelper _out;
    public SunShadowTests(ITestOutputHelper o) { _out = o; }

    /// <summary>The Pitts S-2B's upper span from its config is 6.0 m, so half of it is 3.0 m.</summary>
    private const double PittsHalfSpanM = 3.0;
    private const double Feet = 0.3048;
    /// <summary>Sun at 45°, so the slant range to the ground is √2 × the height.</summary>
    private const double Slant45 = 1.41421356;

    [Fact]
    public void OnTheGroundTheShadowIsTheAeroplaneSOwnSilhouette()
    {
        (double grow, double op) = SunShadow.Soften(0.0, PittsHalfSpanM);
        Assert.Equal(1.0, grow, 9);
        Assert.Equal(1.0, op, 9);
    }

    /// <summary>The number the whole model turns on: an edge's transition band is slant × θ wide, so its half-width
    /// is 4.65 mm per metre of slant range.</summary>
    [Fact]
    public void ThePenumbraGrowsAtHalfTheSunSAngularDiameter()
    {
        Assert.Equal(0.004654, SunShadow.PenumbraHalfWidthM(1.0), 6);
        Assert.Equal(4.654, SunShadow.PenumbraHalfWidthM(1000.0), 3);
    }

    /// <summary>A 6 m aeroplane has no fully dark shadow beyond 645 m of slant range — about 1,500 ft up with the
    /// sun at 45°. Above that every shadow in the game is a smear, and that is correct.</summary>
    [Fact]
    public void ThePittsUmbraIsGoneAt645MetresOfSlantRange()
    {
        double d = SunShadow.UmbraVanishesAtM(PittsHalfSpanM);
        Assert.Equal(644.6, d, 1);
        Assert.Equal(PittsHalfSpanM, SunShadow.PenumbraHalfWidthM(d), 6);
        // At that range the patch is exactly twice the sharp silhouette and a quarter of its darkness.
        (double grow, double op) = SunShadow.Soften(d, PittsHalfSpanM);
        Assert.Equal(2.0, grow, 6);
        Assert.Equal(0.25, op, 6);
        _out.WriteLine($"  Pitts umbra gone at slant {d:F0} m = {d / Slant45 / Feet:F0} ft AGL with the sun at 45 deg");
    }

    /// <summary>The table in the handback: what Ty sees at each height with the sun at 45°.</summary>
    [Theory]
    [InlineData(50, 1.034, 0.936)]
    [InlineData(500, 1.334, 0.562)]
    [InlineData(1000, 1.669, 0.359)]
    [InlineData(2000, 2.339, 0.183)]
    [InlineData(5000, 4.344, 0.0530)]
    public void SizeAndDarknessAtHeight(double ft, double expectGrow, double expectOpacity)
    {
        (double grow, double op) = SunShadow.Soften(ft * Feet * Slant45, PittsHalfSpanM);
        _out.WriteLine($"  {ft,6:F0} ft: shadow x{grow:F3} the aeroplane, {op * 100:F1} % of a sharp shadow's darkness");
        Assert.Equal(expectGrow, grow, 2);
        Assert.Equal(expectOpacity, op, 3);
    }

    /// <summary>A low sun casts a longer ray to the ground, so the same height gives a bigger, fainter shadow — the
    /// evening shadow that is both stretched and washed out, which falls straight out of the slant range.</summary>
    [Fact]
    public void ALowSunSoftensTheShadowMoreThanAHighOneAtTheSameHeight()
    {
        double h = 100.0;
        (double g45, double o45) = SunShadow.Soften(h / System.Math.Sin(45 * System.Math.PI / 180), PittsHalfSpanM);
        (double g15, double o15) = SunShadow.Soften(h / System.Math.Sin(15 * System.Math.PI / 180), PittsHalfSpanM);
        Assert.True(g15 > g45);
        Assert.True(o15 < o45);
        _out.WriteLine($"  100 m AGL: sun 45 deg -> x{g45:F2} at {o45 * 100:F0} %; sun 15 deg -> x{g15:F2} at {o15 * 100:F0} %");
    }

    /// <summary>Monotone both ways, and never darker or smaller than the sharp shadow: a shadow that grew back or
    /// darkened with height would be worse than the fixed blob it replaces.</summary>
    [Fact]
    public void ItOnlyEverGrowsAndOnlyEverFades()
    {
        double lastG = 0, lastO = 2;
        for (double d = 0; d <= 3000; d += 25)
        {
            (double g, double o) = SunShadow.Soften(d, PittsHalfSpanM);
            Assert.True(g >= lastG && g >= 1.0);
            Assert.True(o <= lastO && o <= 1.0 && o > 0.0);
            lastG = g; lastO = o;
        }
    }

    /// <summary>A bigger aeroplane keeps a sharp shadow higher up: the same penumbra band is a smaller share of it.
    /// A DC-3's 29 m span against a Pitts' 6 m.</summary>
    [Fact]
    public void ABiggerAeroplaneKeepsItsShadowLonger()
    {
        (double gPitts, double oPitts) = SunShadow.Soften(1000.0, 3.0);
        (double gDc3, double oDc3) = SunShadow.Soften(1000.0, 14.5);
        Assert.True(gDc3 < gPitts);
        Assert.True(oDc3 > oPitts);
        _out.WriteLine($"  1 km slant: Pitts x{gPitts:F2} at {oPitts * 100:F0} %, DC-3 x{gDc3:F2} at {oDc3 * 100:F0} %");
    }
}

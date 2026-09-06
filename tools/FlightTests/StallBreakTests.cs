using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Verifies the wing airfoil table's ±180° polar has a genuine stall break — lift rises with angle of
/// attack, peaks, and then drops — rather than the interpolation smoothing a cliff into a plateau.
/// </summary>
public class StallBreakTests
{
    private static readonly AirfoilTable WingTable = Aircraft.BuildAirfoilTables(TestAircraftConfig.Load())["clarkY-like"];

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;

    [Fact]
    public void LiftRisesThenBreaksPastStall()
    {
        double clPreStall = WingTable.Sample(DegToRad(10)).Cl;
        double clAtStallPeak = WingTable.Sample(DegToRad(15)).Cl;
        double clJustPastStall = WingTable.Sample(DegToRad(18)).Cl;
        double clWellPastStall = WingTable.Sample(DegToRad(22)).Cl;

        Assert.True(clPreStall < clAtStallPeak, $"Cl should still be rising before stall: Cl(10)={clPreStall:F3}, Cl(15)={clAtStallPeak:F3}.");
        Assert.True(clAtStallPeak > clJustPastStall, $"Expected a break right after stall: Cl(15)={clAtStallPeak:F3}, Cl(18)={clJustPastStall:F3}.");
        Assert.True(clJustPastStall > clWellPastStall, $"Cl should keep declining further past stall: Cl(18)={clJustPastStall:F3}, Cl(22)={clWellPastStall:F3}.");

        double dropFraction = (clAtStallPeak - clWellPastStall) / clAtStallPeak;
        Assert.True(dropFraction > 0.20, $"Stall break is too shallow ({dropFraction:P0} drop from peak to 22deg) to be a credible stall.");
    }

    [Fact]
    public void DragKeepsRisingThroughTheStall()
    {
        double cdPreStall = WingTable.Sample(DegToRad(10)).Cd;
        double cdAtStallPeak = WingTable.Sample(DegToRad(15)).Cd;
        double cdWellPastStall = WingTable.Sample(DegToRad(22)).Cd;

        Assert.True(cdPreStall < cdAtStallPeak);
        Assert.True(cdAtStallPeak < cdWellPastStall, "Drag should keep climbing even as lift breaks down post-stall.");
    }
}

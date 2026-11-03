using FlyingGame.Core;
using Xunit;
namespace FlyingGame.FlightTests;
public sealed class HudInstrumentLayoutTests
{
    [Theory]
    [InlineData(false, 150, 110, 700, .37, 20)]
    [InlineData(true, 185, 135, 820, .43, 24)]
    public void PhoneBandMeetsMinimumSizesAndBottomBudget(bool controller, double flight, double engine, double width, double limit, double numeral)
    {
        const double s = 1206, toolbar = 96.48, gap = 14.472;
        Assert.Equal(flight, 2 * HudInstrumentLayout.DialRadius(s, controller), 3);
        Assert.Equal(engine, 2 * HudInstrumentLayout.EngineRadius(s, controller), 3);
        Assert.Equal(numeral, HudInstrumentLayout.NumeralText(s, controller), 3);
        Assert.Equal(width, HudInstrumentLayout.RowWidth(s, controller ? 2206 : 938, controller), 3);
        Assert.Equal(12, HudInstrumentLayout.ToolbarGap(s), 3);
        Assert.Equal(34.65, HudInstrumentLayout.ValueHeight(s), 3);
        Assert.True(width / 4 >= flight + 20);
        double engineTop = toolbar + 12 + flight + 34.65 + 6;
        Assert.Equal(engineTop, HudInstrumentLayout.EngineTop(toolbar, gap, s, controller), 3);
        Assert.Equal(engineTop + engine + 34.65, HudInstrumentLayout.Bottom(toolbar, gap, s, controller, true), 3);
        Assert.True(HudInstrumentLayout.Bottom(toolbar, gap, s, controller, true) < s * limit);
        Assert.Equal(toolbar + 12 + flight + 34.65, HudInstrumentLayout.Bottom(toolbar, gap, s, controller, false), 3);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1206)]
    [InlineData(1809)]
    public void FacesAndNumeralsScaleWithShortEdge(double s)
    {
        foreach (bool controller in new[] { false, true })
        {
            Assert.True(2 * HudInstrumentLayout.DialRadius(s, controller) >= s / 1206 * (controller ? 185 : 150));
            Assert.True(2 * HudInstrumentLayout.EngineRadius(s, controller) >= s / 1206 * (controller ? 135 : 110));
            Assert.Equal(s / 1206 * (controller ? 24 : 20), HudInstrumentLayout.NumeralText(s, controller), 6);
            Assert.True(HudInstrumentLayout.Bottom(s * .08, s * .012, s, controller, true) < s * (controller ? .43 : .37));
            Assert.True(HudInstrumentLayout.Bottom(282.48, 14.472, 1206, controller, true) < 2622 * (controller ? .43 : .37));
        }
    }

    [Fact]
    public void AvgasMassConvertsToUsGallons()
    {
        Assert.Equal(1, HudInstrumentLayout.FuelGallons(2.7252), 8);
        Assert.Equal(10, HudInstrumentLayout.FuelGallons(27.252), 8);
        Assert.Equal(0, HudInstrumentLayout.FuelGallons(0));
    }

    [Fact]
    public void EngineBlockIsDialPlusValueLine()
    {
        Assert.Equal(2 * HudInstrumentLayout.EngineRadius(1206) + HudInstrumentLayout.ValueHeight(1206), HudInstrumentLayout.EngineBlockHeight(1206), 6);
        Assert.True(HudInstrumentLayout.EngineBlockHeight(1206, true) > HudInstrumentLayout.EngineBlockHeight(1206));   // controller dials are larger
    }

    [Fact]
    public void EngineRowCentresButStaysInsideItsLimits()
    {
        Assert.Equal(901, HudInstrumentLayout.CentredRowLeft(1311, 820, 0, 2622), 6);    // centred
        Assert.Equal(1000, HudInstrumentLayout.CentredRowLeft(1311, 820, 1000, 2622), 6);  // pushed right of a button column
        Assert.Equal(780, HudInstrumentLayout.CentredRowLeft(1311, 820, 0, 1600), 6);    // pushed left of a pad
    }

    [Fact]
    public void NarrowEngineCellsPutTheUnitOnASecondLine()
    {
        Assert.Equal(1, HudInstrumentLayout.EngineValueLines(175, 33));   // iPhone touch band: 700/4 px at 33 px text
        Assert.Equal(2, HudInstrumentLayout.EngineValueLines(146, 41));   // iPad mini touch band
        Assert.Equal(HudInstrumentLayout.EngineBlockHeight(1488) + HudInstrumentLayout.ValueHeight(1488), HudInstrumentLayout.EngineBlockHeight(1488, false, 2), 6);
    }
}

using FlyingGame.Core;
using Xunit;
namespace FlyingGame.FlightTests;
public sealed class HudInstrumentLayoutTests
{
    [Theory]
    [InlineData(false, 54, 938)]
    [InlineData(true, 60, 1963.468)]
    public void PhoneRowsHaveSeparateFacesValuesAndCaptions(bool controller, double radius, double width)
    {
        const double s = 1206, toolbar = 96.48, gap = 14.472;
        Assert.Equal(radius, HudInstrumentLayout.DialRadius(s, controller), 3);
        Assert.Equal(width, HudInstrumentLayout.RowWidth(s, controller ? 1963.468 : 938, controller), 3);
        Assert.Equal(12, HudInstrumentLayout.ToolbarGap(s), 3);
        Assert.Equal(39.6, HudInstrumentLayout.ValueHeight(s), 3);
        double caption = controller ? 36 : 72;
        Assert.Equal(caption, HudInstrumentLayout.CaptionHeight(s, controller), 3);
        double faceTop = toolbar + 12;
        double valueTop = faceTop + 2 * radius;
        double captionTop = valueTop + 39.6;
        double engineTop = HudInstrumentLayout.EngineTop(toolbar, gap, s, controller);
        Assert.Equal(captionTop + caption + gap, engineTop, 3);
        Assert.True(width / 4 > radius * 2 + 24);
        if (controller) Assert.True(engineTop + radius * 1.4 + 39.6 + caption <= 1206 * .4);
    }

    [Fact]
    public void ControllerUsesFreedWidthAndLargerGauges()
    {
        Assert.True(HudInstrumentLayout.DialRadius(1206, true) > HudInstrumentLayout.DialRadius(1206));
        Assert.True(HudInstrumentLayout.RowWidth(1206, 2206, true) > 2 * 938);
    }

    [Fact]
    public void GliderReservesOnlyFlightRowAndPortraitScalesFromShortEdge()
    {
        Assert.Equal(328.08, HudInstrumentLayout.Bottom(96.48, 14.472, 1206, false, false), 3);
        Assert.Equal(478.152, HudInstrumentLayout.Bottom(96.48, 14.472, 1206, true, true), 3);
        Assert.Equal(1206 * .84, HudInstrumentLayout.RowWidth(1206, 1163), 3);
        Assert.True(HudInstrumentLayout.Bottom(282.48, 14.472, 1206, true, true) < 2622 * .4);
    }

    [Fact]
    public void AvgasMassConvertsToUsGallons()
    {
        Assert.Equal(1, HudInstrumentLayout.FuelGallons(2.7252), 8);
        Assert.Equal(10, HudInstrumentLayout.FuelGallons(27.252), 8);
        Assert.Equal(0, HudInstrumentLayout.FuelGallons(0));
    }
}

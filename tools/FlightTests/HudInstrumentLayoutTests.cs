using FlyingGame.Core;
using Xunit;
namespace FlyingGame.FlightTests;
public sealed class HudInstrumentLayoutTests
{
    [Fact]
    public void PhoneTouchKeepsFourReadableDialsInBand()
    {
        Assert.Equal(104.922, HudInstrumentLayout.DialRadius(1206), 3);
        Assert.Equal(938, HudInstrumentLayout.RowWidth(1206, 938), 3);
        Assert.True(938 / 4.0 - 209.844 > 24);
        Assert.Equal(411.944, HudInstrumentLayout.EngineTop(96.5, 14.4, 1206), 3);
    }
    [Fact]
    public void GamepadAndPortraitUseSameDialSizeWithWiderSpacing()
    {
        Assert.Equal(1013.04, HudInstrumentLayout.RowWidth(1206, 2206), 2);
        Assert.Equal(1013.04, HudInstrumentLayout.RowWidth(1206, 1163), 2);
        Assert.True(HudInstrumentLayout.EngineTop(96.5, 14.4, 1206) + 92.4 < 603);
    }
}

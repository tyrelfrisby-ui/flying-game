using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Ty 2026-11-06 (t906u): "Use the Great Lakes as a tow plane for the 2-33 until we get the super cub built."
/// Ids copied here by hand, not read from TowPlanes.</summary>
public sealed class TowPlanesTests
{
    [Theory]
    [InlineData("pa25-pawnee-like")]
    [InlineData("pa18-cub-like")]
    [InlineData(null)]
    [InlineData("")]
    public void The233IsTowedByTheGreatLakesWhateverThePickerSays(string? chosen)
    {
        Assert.Equal("great-lakes-like", TowPlanes.TugFor("glider-2-33-like", chosen));
        Assert.True(TowPlanes.IsInterim("glider-2-33-like"));
    }

    [Theory]
    [InlineData("glider-swift-s1-like", "pa18-cub-like", "pa18-cub-like")]
    [InlineData("glider-eb29r-like", "pa25-pawnee-like", "pa25-pawnee-like")]
    [InlineData("glider-swift-s1-like", null, "pa25-pawnee-like")]
    public void EveryOtherGliderKeepsThePicker(string glider, string? chosen, string tug)
    {
        Assert.Equal(tug, TowPlanes.TugFor(glider, chosen));
        Assert.False(TowPlanes.IsInterim(glider));
    }

    [Fact]
    public void TheInterimTugIsAShippedConfig()
    {
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "great-lakes-like.json"));
        Assert.Equal("great-lakes-like", cfg.Id);
        Assert.NotNull(cfg.Propulsion);
        // The source of truth is configs/aircraft, NOT unity/Assets/StreamingAssets/aircraft: that mirror is generated
        // and untracked since 2026-11-10, so it is simply absent in a fresh checkout and this used to fail there.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        Assert.NotNull(d);
        Assert.True(File.Exists(Path.Combine(d!.FullName, "configs", "aircraft", "great-lakes-like.json")));
    }
}

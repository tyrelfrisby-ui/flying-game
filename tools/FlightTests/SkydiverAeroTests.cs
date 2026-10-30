using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The falling body, held to the speeds it was derived from. The drag areas in
/// <see cref="SkydiverAero"/> are back-solved from published terminal velocities, so the one thing worth
/// checking is that the round trip closes: put the areas back into the terminal-speed equation and the
/// published speeds must come out.</summary>
public class SkydiverAeroTests
{
    private readonly ITestOutputHelper _out;
    public SkydiverAeroTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void TheDragAreasGiveBackThePublishedTerminalSpeeds()
    {
        double rho = SkydiverAero.ReferenceDensity, m = SkydiverAero.ReferenceMassKg;
        foreach ((string what, double pos, double want) in new[]
                 { ("belly", 0.0, SkydiverAero.BellyTerminalMs),
                   ("track", 0.5, SkydiverAero.TrackTerminalMs),
                   ("head down", 1.0, SkydiverAero.HeadDownTerminalMs) })
        {
            double got = SkydiverAero.TerminalSpeed(pos, m, rho);
            _out.WriteLine($"{what,10}: CdA {SkydiverAero.CdA(pos):0.000} m2 -> {got:0.0} m/s " +
                           $"({got * 2.23694:0} mph), published {want:0.0} ({want * 2.23694:0} mph), " +
                           $"glide {SkydiverAero.GlideRatio(pos):0.00}");
            Assert.Equal(want, got, 1);
        }
    }

    /// <summary>A body falls FASTER the more it stands on its head -- monotonically, with no dip in the
    /// middle. A drag area interpolated carelessly through three points can easily break this.</summary>
    [Fact]
    public void StandingOnYourHeadOnlyEverMakesYouFaster()
    {
        double last = 0;
        for (double p = 0; p <= 1.0001; p += 0.02)
        {
            double v = SkydiverAero.TerminalSpeed(p, SkydiverAero.ReferenceMassKg, SkydiverAero.ReferenceDensity);
            Assert.True(v >= last - 1e-9, $"body position {p:0.00} falls slower ({v:0.0}) than {last:0.0} m/s");
            last = v;
        }
        _out.WriteLine($"belly {SkydiverAero.TerminalSpeed(0, 105, 1.225):0.0} -> head down " +
                       $"{SkydiverAero.TerminalSpeed(1, 105, 1.225):0.0} m/s, monotone throughout");
    }

    /// <summary>Tracking is a skill: there is one best body angle and being either side of it costs you.
    /// A model where flat or head-down tracked just as well would make the whole position pointless.</summary>
    [Fact]
    public void ThereIsOneBestBodyAngleToTrackAt()
    {
        double best = SkydiverAero.GlideRatio(0.5);
        Assert.Equal(SkydiverAero.TrackBestGlide + 0.04, best, 3);
        Assert.True(SkydiverAero.GlideRatio(0.0) < 0.25 * best, "a flat stable body should barely track");
        Assert.True(SkydiverAero.GlideRatio(1.0) < 0.25 * best, "a head-down body should barely track");
        _out.WriteLine($"glide: flat {SkydiverAero.GlideRatio(0):0.00}, best {best:0.00} at the track " +
                       $"position, head down {SkydiverAero.GlideRatio(1):0.00}");
    }
}

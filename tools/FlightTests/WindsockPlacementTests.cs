using System.Collections.Generic;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Three windsocks per runway, and never one on a runway (Ty 2026-11-08).
/// </summary>
public class WindsockPlacementTests
{
    private static IReadOnlyList<WorldTerrain.Strip> Strips => WorldTerrain.AirportStrips;

    [Fact]
    public void EveryRunwayGetsThree()
    {
        var spots = WindsockPlacement.For(Strips, 1);
        foreach (WorldTerrain.Strip s in Strips)
            Assert.Equal(3, spots.FindAll(p => p.Strip == s.Kind).Count);
    }

    [Fact]
    public void NoneOfThemStandsOnAnyRunway()
    {
        // The rule he underlined. Checked against EVERY strip, not just its own, and over many random side draws.
        for (int seed = 1; seed <= 50; seed++)
            foreach (SockSpot p in WindsockPlacement.For(Strips, seed))
                Assert.False(WindsockPlacement.OnAnyStrip(Strips, p.X, p.Y),
                    $"seed {seed}: a {p.Strip} sock at ({p.X:F0},{p.Y:F0}) is on a surface");
    }

    [Fact]
    public void TheEndOnesAre1000FeetInOrAFifthWhicheverIsShorter()
    {
        // The main is 2400 m: a fifth is 480 m, so 1000 ft (304.8) wins. A 600 m strip: a fifth is 120 m, which wins.
        Assert.Equal(304.8, WindsockPlacement.EndOffsetFor(2400), 3);
        Assert.Equal(120.0, WindsockPlacement.EndOffsetFor(600), 3);
        Assert.Equal(150.0, WindsockPlacement.EndOffsetFor(750), 3);
    }

    [Fact]
    public void TheEndOnesSitThatFarInFromEachThreshold()
    {
        var spots = WindsockPlacement.For(Strips, 4);
        WorldTerrain.Strip main = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "paved");
        double want = main.Length / 2.0 - WindsockPlacement.EndOffsetFor(main.Length);
        foreach (SockSpot p in spots.FindAll(p => p.Strip == "paved" && p.Index < 2))
            Assert.Equal(want, System.Math.Abs(p.X - main.Dx), 1);
    }

    [Fact]
    public void TheThirdIsMidfieldWhenNothingIsInTheWay()
    {
        // The main's own midfield is clear: the crosswind runway crosses it well off centre.
        var spots = WindsockPlacement.For(Strips, 4);
        WorldTerrain.Strip main = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "paved");
        SockSpot mid = spots.Find(p => p.Strip == "paved" && p.Index == 2);
        Assert.Equal(main.Dx, mid.X, 1);
    }

    [Fact]
    public void ACrossingRunwayMovesTheMiddleOneAsideButKeepsItNear()
    {
        // The crosswind runway's midfield IS its crossing with the main, so its third sock cannot stand there.
        WorldTerrain.Strip x = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "paved-xwind");
        for (int seed = 1; seed <= 20; seed++)
        {
            SockSpot mid = WindsockPlacement.For(Strips, seed).Find(p => p.Strip == "paved-xwind" && p.Index == 2);
            if (mid.Strip == null) continue;
            Assert.False(WindsockPlacement.OnAnyStrip(Strips, mid.X, mid.Y));
            double alongFromCentre = System.Math.Abs(mid.Y - x.Dy);          // the xwind runs along y
            Assert.True(alongFromCentre <= WindsockPlacement.MidfieldSearchM,
                $"seed {seed}: it wandered {alongFromCentre:F0} m from the intersection");
        }
    }

    [Fact]
    public void TheSideIsNotAlwaysTheSame()
    {
        // "randomly placed as far as which side they are on"
        bool sawLeft = false, sawRight = false;
        WorldTerrain.Strip main = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "paved");
        for (int seed = 1; seed <= 40; seed++)
            foreach (SockSpot p in WindsockPlacement.For(Strips, seed).FindAll(p => p.Strip == "paved"))
            {
                if (p.Y > main.Dy) sawLeft = true; else sawRight = true;
            }
        Assert.True(sawLeft && sawRight, "every sock ended up on the same side");
    }

    [Fact]
    public void TheSameSeedPlacesThemTheSameWayTwice()
    {
        // An airport that rearranged itself every time you looked would be worse than a fixed one.
        var a = WindsockPlacement.For(Strips, 9);
        var b = WindsockPlacement.For(Strips, 9);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].X, b[i].X, 6); Assert.Equal(a[i].Y, b[i].Y, 6); }
    }
}

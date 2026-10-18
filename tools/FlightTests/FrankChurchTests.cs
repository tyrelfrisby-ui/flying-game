using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The backcountry strips (owner 2026-10-16). What matters about a backcountry strip is not that it exists but
/// that it is flat where it should be, sloped where it should be, has something at the end that makes it
/// one-way, and is close enough to the next one to go and look. All measured here.
/// </summary>
public class FrankChurchTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t;

    public FrankChurchTests(ITestOutputHelper o)
    {
        _o = o;
        _t = new WorldTerrain();
        WorldTerrain.Active = _t;
    }

    public void Dispose() => WorldTerrain.Active = null;

    [Fact]
    public void EveryStripIsWithinTenMinutesOfEveryOther()
    {
        // The owner's compactness rule, in the unit that matters: minutes in a Cub.
        double worst = FrankChurch.WorstCrossingMinutes();
        _o.WriteLine($"farthest pair: {worst:0.0} minutes at 90 kt");
        Assert.True(worst < 10, $"the farthest pair is {worst:0.0} minutes apart");
        Assert.True(worst > 1.5, "they should not all be on top of each other either");
        Assert.Equal(10, FrankChurch.Strips.Length);
    }

    [Fact]
    public void TheStripsAreTheLengthsTheRealOnesAre()
    {
        // The famous ones run from about 1,200 ft (Dewey Moore) to about 4,000 ft (Thomas Creek).
        double shortest = double.MaxValue, longest = 0;
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            Assert.InRange(s.LengthFt, 1150, 4100);
            shortest = Math.Min(shortest, s.LengthFt);
            longest = Math.Max(longest, s.LengthFt);
            _o.WriteLine($"{s.Name,-14} {s.LengthFt,5:0} ft  {s.ElevationM * 3.28084,5:0} ft MSL  {s.SlopePercent,4:0.0}%  {(s.OneWay ? "ONE WAY" : "two-way")}");
        }
        Assert.True(shortest < 1300, "one of them has to be properly short");
        Assert.True(longest > 3800, "and one of them long");
    }

    [Fact]
    public void EveryStripIsFlatAcrossAndAtItsOwnSlopeAlong()
    {
        // A strip you cannot roll straight down is not a strip. Flat across to the edges, and the slope along
        // it is the slope it says it has.
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            double h = s.HeadingDeg * Math.PI / 180;
            for (double f = -0.45; f <= 0.45; f += 0.15)
            {
                double along = f * s.LengthM;
                double cx = s.X + Math.Cos(h) * along, cy = s.Y + Math.Sin(h) * along;
                double mid = _t.HeightAt(cx, cy);
                Assert.Equal(s.ElevationM + along * s.SlopePercent / 100.0, mid, 1);
                // Across: the same height at both edges.
                for (int k = -1; k <= 1; k += 2)
                {
                    double ex = cx - Math.Sin(h) * k * s.WidthM * 0.45;
                    double ey = cy + Math.Cos(h) * k * s.WidthM * 0.45;
                    Assert.Equal(mid, _t.HeightAt(ex, ey), 1);
                }
            }
            // And the ends are where the slope says they are.
            Assert.Equal(s.HighEndM - s.LowEndM, s.LengthM * s.SlopePercent / 100.0, 3);
        }
    }

    [Fact]
    public void TheStripsAreSurfacesTheWheelsUnderstand()
    {
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            WorldTerrain.Surface surf = WorldTerrain.SurfaceAt(s.X, s.Y);
            Assert.True(surf is WorldTerrain.Surface.Grass or WorldTerrain.Surface.Gravel,
                        $"{s.Name} is {surf} — nothing lands on that");
            // Off the side it is mountain again.
            double h = s.HeadingDeg * Math.PI / 180;
            double ox = s.X - Math.Sin(h) * (s.WidthM * 0.5 + 60), oy = s.Y + Math.Cos(h) * (s.WidthM * 0.5 + 60);
            Assert.NotEqual(WorldTerrain.Surface.Grass, WorldTerrain.SurfaceAt(ox, oy));
        }
    }

    [Fact]
    public void AOneWayStripHasSomethingAtTheFarEndAndAirAtTheNear()
    {
        // That is what makes it one-way: you land toward the hill, and there is no going around.
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            if (!s.OneWay) continue;
            double h = s.OneWayInHeadingDeg!.Value * Math.PI / 180;
            double halfL = s.LengthM * 0.5;
            // Beyond the far end the ground keeps climbing above the strip.
            double far = 0;
            for (double d = halfL + 90; d < halfL + 500; d += 40)
                far = Math.Max(far, _t.HeightAt(s.X + Math.Cos(h) * d, s.Y + Math.Sin(h) * d));
            double high = s.HighEndM;
            _o.WriteLine($"{s.Name,-14} far end: ground rises to {far - high:+0;-0} m above the strip");
            Assert.True(far > high + 25, $"{s.Name}: the far end is open — it is not one-way at all ({far - high:+0;-0} m)");
            // And behind the approach end the ground falls well away, so the approach is over nothing.
            double behind = double.MaxValue;
            for (double d = halfL + 90; d < halfL + 400; d += 40)
                behind = Math.Min(behind, _t.HeightAt(s.X - Math.Cos(h) * d, s.Y - Math.Sin(h) * d));
            _o.WriteLine($"{s.Name,-14} approach: ground {behind - s.LowEndM:+0;-0} m relative to the threshold");
            Assert.True(behind < s.LowEndM - 15, $"{s.Name}: there is ground under the approach, so a go-around exists");
        }
    }

    [Fact]
    public void TheTwoWayStripsCanBeLeftInEitherDirection()
    {
        // Two-way in the backcountry does not mean an open field — Johnson Creek has timber off both ends and
        // the canyon walls are never far. It means neither end is a wall you cannot out-climb, and at least
        // one end gives you somewhere to go.
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            if (s.OneWay) continue;
            double h = s.HeadingDeg * Math.PI / 180, halfL = s.LengthM * 0.5;
            double best = double.MaxValue;
            foreach (int k in new[] { -1, 1 })
            {
                double worst = 0;
                for (double d = halfL + 80; d < halfL + 400; d += 40)
                    worst = Math.Max(worst, _t.HeightAt(s.X + k * Math.Cos(h) * d, s.Y + k * Math.Sin(h) * d));
                double rel = worst - (k > 0 ? s.HighEndM : s.LowEndM);
                _o.WriteLine($"{s.Name,-14} end {(k > 0 ? "+" : "-")}: {rel:+0;-0} m");
                Assert.True(rel < 220, $"{s.Name}: the {(k > 0 ? "far" : "near")} end is walled in by {rel:0} m");
                best = Math.Min(best, rel);
            }
            Assert.True(best < 60, $"{s.Name}: both ends climb ({best:0} m at best) — that is a one-way strip, not a two-way one");
        }
    }

    [Fact]
    public void TheCanyonIsAKilometreDeepAndHasARiverInIt()
    {
        // The country, not the strips: a canyon cut into the plateau with water in the bottom.
        int looked = 0;
        for (double x = FrankChurch.X0 + 400; x < FrankChurch.X1 - 400; x += 300)
        {
            double cy = FrankChurch.CanyonCentreY(x);
            // Not where a strip's bench has been laid over the canyon bottom — there the ground is the strip.
            if (FrankChurch.OnWorkedGround(x, cy)) continue;
            looked++;
            double floor = _t.HeightAt(x, cy);
            double rim = _t.HeightAt(x, cy + FrankChurch.CanyonHalfWidthM + 500);
            Assert.True(rim - floor > 500, $"at x={x:0} the canyon is only {rim - floor:0} m deep");
            double? w = _t.WaterSurfaceAt(x, cy, floor + 1);
            Assert.True(w.HasValue, $"no river at x={x:0}");
            Assert.True(w!.Value > floor - 0.01, $"at x={x:0} the water ({w.Value:0.0}) is below the bed ({floor:0.0})");
        }
        Assert.True(looked > 8, $"only {looked} places along the canyon were open river");
    }

    [Fact]
    public void TheStripsDoNotSitOnTopOfEachOtherOrInTheRiver()
    {
        FrankChurch.Strip[] st = FrankChurch.Strips;
        for (int i = 0; i < st.Length; i++)
        {
            // Not in the water.
            Assert.Null(FrankChurch.WaterSurfaceAt(st[i].X, st[i].Y));
            for (int j = i + 1; j < st.Length; j++)
            {
                // What matters is that the two strips plus their aprons do not overlap on the ground — not
                // how far apart their middles are. Two parallel strips 300 m apart are fine; two crossing
                // ones are not. Walk each centreline and check it stays off the other's apron.
                for (double f = -0.5; f <= 0.5; f += 0.02)
                {
                    double h = st[i].HeadingDeg * Math.PI / 180;
                    double px = st[i].X + Math.Cos(h) * f * st[i].LengthM;
                    double py = st[i].Y + Math.Sin(h) * f * st[i].LengthM;
                    Assert.False(st[j].On(px, py, FrankChurch.ApronM),
                                 $"{st[i].Name} runs across {st[j].Name}'s apron");
                }
            }
        }
    }

    [Fact]
    public void TheBackcountryLeavesTheRestOfTheWorldAlone()
    {
        // It is cut into its own block, and the Cascades next door are untouched. The canyon's cut now dies
        // away over a head at each end, so the ground it touches reaches further in x than the block itself
        // (2026-10-17) — but only as far as the heads, and the east one still stops 400 m short of the range.
        Assert.True(FrankChurch.X1 < AlpineRange.X0, "the two areas should be side by side, not on top of each other");
        Assert.True(FrankChurch.HeadEastX < AlpineRange.X0, "the canyon's head reaches into the Cascades");
        foreach (AlpineRange.Lake l in AlpineRange.Lakes)
            Assert.False(FrankChurch.InArea(l.X, l.Y), $"{l.Name} is inside the backcountry block");
        // Beyond either head the ground is the plateau, to the metre, because there is no cut left to apply.
        Assert.Equal(FrankChurch.RimM, _t.HeightAt(FrankChurch.HeadWestX - 100, -12800), 0);
        Assert.Equal(0.0, FrankChurch.EndTaper(FrankChurch.HeadWestX), 6);
        Assert.Equal(0.0, FrankChurch.EndTaper(FrankChurch.HeadEastX), 6);
        Assert.Equal(1.0, FrankChurch.EndTaper(0), 6);
    }

    /// <summary>
    /// The canyon is 636 m deep at its head and 756 m at the other end, and until 2026-10-17 its ground
    /// simply stopped: a 639 m vertical step across the east mouth and 759 m across the west one, into which
    /// you could fly at any height. Nothing caught it because the test that walks a block's margins walked
    /// only the north and south edges, and this canyon runs east–west, out between the two of them.
    ///
    /// So: walk the floor from end to end, and walk across both heads at every y, and hold that the ground
    /// never steps more than a steep hillside's worth. 25 m in 5 m is a 1 : 0.2 face — far steeper than
    /// anything here is meant to be, and a twenty-fifth of what was there.
    /// </summary>
    [Fact]
    public void TheCanyonDoesNotEndInAWall()
    {
        double worst = 0; string where = "nowhere";
        double prev = _t.HeightAt(FrankChurch.HeadWestX - 300, FrankChurch.CanyonCentreY(FrankChurch.HeadWestX - 300));
        for (double x = FrankChurch.HeadWestX - 295; x <= FrankChurch.HeadEastX + 300; x += 5)
        {
            double h = _t.HeightAt(x, FrankChurch.CanyonCentreY(x));
            if ((h < FrankChurch.RimM - 20 || prev < FrankChurch.RimM - 20) && System.Math.Abs(h - prev) > worst)
            { worst = System.Math.Abs(h - prev); where = $"on the floor at x {x:F0}"; }
            prev = h;
        }
        foreach (int side in new[] { -1, 1 })
        {
            double from = side < 0 ? FrankChurch.X0 + 100 : FrankChurch.WorkedEastX - 100;
            double to = side < 0 ? FrankChurch.HeadWestX - 300 : FrankChurch.HeadEastX + 300;
            // Only the canyon's own country: a band either side of the centreline a little wider than the
            // canyon ever is. Beyond it the ground belongs to whatever else is there — the alpine range's
            // footprint ends in a 98 m step of its own 600 m west of its block, which is not this test's.
            for (double y = FrankChurch.Y0 - 500; y <= FrankChurch.Y1 + 500; y += 10)
            {
                double p = _t.HeightAt(from, y);
                for (double x = from + side * 5; side < 0 ? x >= to : x <= to; x += side * 5)
                {
                    double h = _t.HeightAt(x, y);
                    // The canyon's own country, rim to rim and a little past. Beyond it the ground belongs
                    // to whatever else is there: the alpine range's footprint ends in a 98 m step of its own
                    // 600 m west of its block, which is not this test's business.
                    bool inCanyon = System.Math.Abs(y - FrankChurch.CanyonCentreY(x)) < FrankChurch.CanyonHalfWidthAt(x) + 60;
                    if (inCanyon && System.Math.Abs(h - p) > worst)
                    { worst = System.Math.Abs(h - p); where = $"across the {(side < 0 ? "west" : "east")} head at ({x:F0}, {y:F0})"; }
                    p = h;
                }
            }
        }
        _o.WriteLine($"worst step through the canyon's two ends: {worst:F1} m over 5 m — {where}");
        Assert.True(worst < 25, $"the canyon still ends in a wall: {worst:F0} m in 5 m {where}");
    }

    /// <summary>
    /// Where two strips' blend zones overlap the ground has to agree with itself. It did not: the nearest
    /// strip owned the ground outright, so 260 m off the end of Dewey Moore — whose bench is 276 m down on
    /// the canyon wall — ownership passed to Sulphur Creek's, up on the rim, and the ground teleported 234 m
    /// at the halfway line. Walk every point of worked ground against its neighbour 5 m away.
    /// </summary>
    [Fact]
    public void NoStripsBlendIntoACliffWhereTheyMeet()
    {
        double worst = 0; string where = "nowhere";
        for (double x = FrankChurch.X0; x <= FrankChurch.X1; x += 25)
        {
            double p = _t.HeightAt(x, FrankChurch.Y0 - 500);
            for (double y = FrankChurch.Y0 - 495; y <= FrankChurch.Y1 + 500; y += 5)
            {
                double h = _t.HeightAt(x, y);
                if (FrankChurch.OnWorkedGround(x, y) && System.Math.Abs(h - p) > worst)
                { worst = System.Math.Abs(h - p); where = $"at ({x:F0}, {y:F0})"; }
                p = h;
            }
        }
        _o.WriteLine($"worst step on the ground the strips have shaped: {worst:F1} m over 5 m — {where}");
        // 25 m in 5 m: these benches are cut into canyon walls, so the ground beside one is allowed to be a
        // steep hillside. It is not allowed to be a cliff.
        Assert.True(worst < 25, $"a cliff where two strips' blends meet: {worst:F0} m in 5 m {where}");
    }
}

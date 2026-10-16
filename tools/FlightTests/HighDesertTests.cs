using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Reno high desert (owner 2026-10-16). These tests are the whole point of the place: a hilltop that
/// LOOKS landable from the air and is not is the worst thing this game could ship, so every top is measured
/// rather than eyeballed — flat, clear, reachable, and far enough from everything else that nothing has
/// carved it.
/// </summary>
[Collection("WorldTerrainActive")]
public class HighDesertTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public HighDesertTests(ITestOutputHelper o) { _o = o; }
    public void Dispose() { WorldTerrain.Active = null; }

    private static WorldTerrain Terrain() { var t = new WorldTerrain(); WorldTerrain.Active = t; return t; }

    [Fact]
    public void TheBlockSitsOnTheMesaPlateauAndNowhereNearAnythingElse()
    {
        // The Alaska plain was laid on top of the Cascades, then across the tunnel maze. Twice. So: the block
        // is inside the Mesa plateau's own y band, and it overlaps no other region's footprint.
        const double mesaSouthEdge = WorldTerrain.FirstEdgeY - WorldTerrain.StepSpacingM * 2;   // -7650
        const double mesaNorthEdge = WorldTerrain.FirstEdgeY - WorldTerrain.StepSpacingM;       // -4350
        Assert.True(HighDesert.Y0 > mesaSouthEdge, $"the block's south edge {HighDesert.Y0} is off the Mesa plateau");
        Assert.True(HighDesert.Y1 < mesaNorthEdge, $"the block's north edge {HighDesert.Y1} is off the Mesa plateau");

        bool Overlaps(double ax0, double ax1, double ay0, double ay1)
            => ax0 < HighDesert.X1 + 600 && ax1 > HighDesert.X0 - 600
            && ay0 < HighDesert.Y1 + 600 && ay1 > HighDesert.Y0 - 600;
        Assert.False(Overlaps(AlaskaRange.X0, AlaskaRange.X1, AlaskaRange.Y0, AlaskaRange.Y1), "it overlaps the Alaska range");
        Assert.False(Overlaps(AlpineRange.X0, AlpineRange.X1, AlpineRange.Y0, AlpineRange.Y1), "it overlaps the Cascades");
        Assert.False(Overlaps(FrankChurch.X0, FrankChurch.X1, FrankChurch.Y0, FrankChurch.Y1), "it overlaps the backcountry");
        Assert.False(Overlaps(BraidedRiver.X0, BraidedRiver.X1, BraidedRiver.Y0, BraidedRiver.Y1), "it overlaps the braided plain");

        // And the thing the region tests above do NOT cover: every plateau carries the same standard
        // landscape — airport, town, lake, arch, tower, race course — and the Mesa plateau's sits at the
        // east end of this very block's y band. The desert must not touch any of it.
        WorldTerrain.Airport mesa = Array.Find(WorldTerrain.Airports, a => a.Name == "Mesa");
        // THE CITY. Every plateau carries a copy of the Valley landscape, and the Mesa copy's city grid sits
        // in this very block's y band. A height probe cannot see it — a city does not move the ground — so it
        // is checked as a rectangle, on every plateau, which is how a render came to show tower blocks
        // standing up the side of a mesa.
        for (int pl = 0; pl < WorldTerrain.PlateauCount; pl++)
        {
            var (cx0, cy0, cx1, cy1) = ValleyCity.At(pl).Extent(120);
            Assert.False(Overlaps(cx0, cx1, cy0, cy1),
                         $"the block overlaps plateau {pl}'s city (x {cx0:0}..{cx1:0}, y {cy0:0}..{cy1:0})");
        }

        // From the airport pad's west edge eastward — everything the Mesa plateau's own landscape occupies.
        // (The desert does own the ground inside its block AND its 600 m margin, so the check starts at the
        // pad, not at some arbitrary radius: the margin reaching x -1400 is the desert's to blend in.)
        const double probe = 1234.5;
        double westmost = mesa.X - WorldTerrain.PadHalfX;
        for (double x = westmost; x <= mesa.X + 3000; x += 100)
            for (double y = mesa.Y - 1600; y <= mesa.Y + 1600; y += 100)
                Assert.Equal(probe, HighDesert.HeightAt(x, y, probe), 6);
        Assert.True(westmost > HighDesert.X1 + 600 + 300,
                    $"the desert's margin reaches x {HighDesert.X1 + 600:0} and the Mesa pad starts at {westmost:0} — too close");
        var t = Terrain();
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(mesa.X, mesa.Y));
        _o.WriteLine($"the Mesa airport is at x {mesa.X:0}, y {mesa.Y:0}; the block's east edge is {HighDesert.X1:0} — " +
                     $"{westmost - (HighDesert.X1 + 600):0} m between the desert's margin and the airport pad");
    }

    [Fact]
    public void TheBlockOwnsItsOwnGround()
    {
        // Sample the whole block against the bare plateau: inside it, the desert is what decides the height,
        // and the only thing allowed to be built here is the desert. Also the reverse — just outside it, the
        // plateau is untouched, so nothing has leaked.
        var t = Terrain();
        int inside = 0;
        double worstOutside = 0;
        for (double x = HighDesert.X0; x <= HighDesert.X1; x += 100)
            for (double y = HighDesert.Y0; y <= HighDesert.Y1; y += 100)
            {
                double h = t.HeightAt(x, y);
                Assert.False(double.IsNaN(h), $"NaN ground at {x:0},{y:0}");
                Assert.True(h > HighDesert.BaseM - 60 && h < 2500,
                            $"the ground at {x:0},{y:0} is {h:0} m — outside anything the desert builds");
                inside++;
            }
        // 900 m clear of the block in every direction the desert says nothing at all. This asks the DESERT,
        // not the terrain: 900 m north of the block is over the escarpment on the Bench plateau, where the
        // rolling grass hills quite properly differ from the bare plateau and have nothing to do with us.
        double leak = 0, lx = 0, ly = 0;
        for (double x = HighDesert.X0 - 1200; x <= HighDesert.X1 + 1200; x += 100)
            for (double y = HighDesert.Y0 - 1200; y <= HighDesert.Y1 + 1200; y += 100)
            {
                bool outside = x < HighDesert.X0 - 900 || x > HighDesert.X1 + 900
                            || y < HighDesert.Y0 - 900 || y > HighDesert.Y1 + 900;
                if (!outside) continue;
                const double probe = 1234.5;
                double d = Math.Abs(HighDesert.HeightAt(x, y, probe) - probe);
                if (d > leak) { leak = d; lx = x; ly = y; }
            }
        worstOutside = leak;
        _o.WriteLine($"{inside} points inside the block; 900 m outside it the desert moves the ground by {leak:0.000} m (worst at {lx:0},{ly:0})");
        Assert.True(worstOutside < 3.0, $"the desert leaks {worstOutside:0.0} m of ground 900 m outside its own block");
    }

    [Fact]
    public void NoHillCarvesAnother()
    {
        // The Cascades lesson: two bowls a kilometre apart with 1.3 km radii carve each other's rims. Here the
        // test is direct — every landable top must be at its own plane everywhere on it, whatever else is near.
        var t = Terrain();
        foreach (HighDesert.Hill hl in HighDesert.Hills)
        {
            if (!hl.Landable) continue;
            double worst = 0; double wx = 0, wy = 0;
            for (double a = -hl.TopLengthM * 0.5; a <= hl.TopLengthM * 0.5; a += 10)
                for (double c = -hl.TopWidthM * 0.5; c <= hl.TopWidthM * 0.5; c += 10)
                {
                    double hrad = hl.TopHeadingDeg * Math.PI / 180;
                    double px = hl.X + Math.Cos(hrad) * a - Math.Sin(hrad) * c;
                    double py = hl.Y + Math.Sin(hrad) * a + Math.Cos(hrad) * c;
                    double d = Math.Abs(t.HeightAt(px, py) - hl.TopSurfaceAt(px, py));
                    if (d > worst) { worst = d; wx = px; wy = py; }
                }
            _o.WriteLine($"{hl.Name,-16} top {hl.TopFt,5:0} ft, {hl.TopLengthFt,4:0} ft long, relief {hl.ReliefM * 3.28084,4:0} ft — " +
                         $"worst departure from its own plane {worst:0.00} m");
            Assert.True(worst < 0.5, $"{hl.Name}: the top is {worst:0.0} m off its own plane at {wx:0},{wy:0} — something carved it");
        }
    }

    [Fact]
    public void EveryLandableTopIsFlatEnoughToLandOn()
    {
        foreach (HighDesert.Hill hl in HighDesert.Hills)
        {
            if (!hl.Landable) continue;
            Assert.True(hl.TopSlopeDeg <= HighDesert.MaxTopSlopeDeg,
                        $"{hl.Name} slopes {hl.TopSlopeDeg}°, past the {HighDesert.MaxTopSlopeDeg}° a top is allowed");
            Assert.True(hl.TopLengthM >= 500, $"{hl.Name} is only {hl.TopLengthFt:0} ft long");
            Assert.True(hl.TopWidthM >= HighDesert.CleanLaneHalfWidthM * 2 + 40,
                        $"{hl.Name} is only {hl.TopWidthM:0} m wide — no room for the clean lane and a margin");
        }
    }

    [Fact]
    public void EveryLandableTopHasAnUtterlyClearLane()
    {
        // The promise: a lane down the middle of every top with nothing in it at all.
        var hazards = HighDesert.Hazards();
        foreach (HighDesert.Hill hl in HighDesert.Hills)
        {
            if (!hl.Landable) continue;
            foreach (HighDesert.Hazard hz in hazards)
                Assert.False(hl.InCleanLane(hz.X, hz.Y),
                             $"a {hz.Kind} sits in {hl.Name}'s clean lane at {hz.X:0},{hz.Y:0}");
        }
        _o.WriteLine($"{hazards.Count} hazards scattered, none of them in any clean lane");
    }

    [Fact]
    public void TheMadeStripsAreGradedAndEmpty()
    {
        var t = Terrain();
        var hazards = HighDesert.Hazards();
        foreach (HighDesert.DesertStrip s in HighDesert.Strips)
        {
            double worst = 0;
            for (double a = -s.LengthM * 0.5; a <= s.LengthM * 0.5; a += 10)
                for (double c = -s.WidthM * 0.5; c <= s.WidthM * 0.5; c += 5)
                {
                    var (px, py) = s.At(a / s.LengthM, c);
                    worst = Math.Max(worst, Math.Abs(t.HeightAt(px, py) - HighDesert.StripSurfaceAt(s, px, py)));
                }
            Assert.True(worst < 0.3, $"{s.Name} is {worst:0.00} m off its graded plane — it is supposed to have been bladed");
            foreach (HighDesert.Hazard hz in hazards)
                Assert.False(s.On(hz.X, hz.Y, 20), $"a {hz.Kind} is on {s.Name}");
            Assert.Equal(WorldTerrain.Surface.Gravel, WorldTerrain.SurfaceAt(s.X, s.Y));
            _o.WriteLine($"{s.Name,-16} {s.LengthFt,4:0} x {s.WidthFt:0} ft on {(s.OnHill ?? "the flats"),-14} heading {s.HeadingDeg:000}°, " +
                         $"graded to {worst:0.00} m");
        }
    }

    [Fact]
    public void EveryThresholdIsMarkedByFourRocksAHundredFeetApart()
    {
        // The owner's figure, checked as a figure: two piles at each end, 100 ft apart across the strip.
        foreach (HighDesert.DesertStrip s in HighDesert.Strips)
        {
            var piles = new List<(double x, double y, double r, double h)>(HighDesert.ThresholdPiles(s));
            Assert.Equal(4, piles.Count);
            foreach (int end in new[] { 0, 2 })
            {
                var a = piles[end]; var b = piles[end + 1];
                double gap = Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
                Assert.Equal(30.48, gap, 1);
                // And the pair straddles the centreline at the very end of the strip.
                var (ca, _) = s.Local((a.x + b.x) * 0.5, (a.y + b.y) * 0.5);
                Assert.Equal(s.LengthM * 0.5, Math.Abs(ca), 0);
            }
            _o.WriteLine($"{s.Name,-16} thresholds marked by 4 rock piles, {30.48 * 3.28084:0} ft apart at each end");
        }
    }

    [Fact]
    public void ThePlayaIsDeadLevelAndCompletelyClear()
    {
        // The one surface in the desert you can trust.
        var t = Terrain();
        double lo = double.MaxValue, hi = double.MinValue;
        for (double x = HighDesert.PlayaX - HighDesert.PlayaRx; x <= HighDesert.PlayaX + HighDesert.PlayaRx; x += 25)
            for (double y = HighDesert.PlayaY - HighDesert.PlayaRy; y <= HighDesert.PlayaY + HighDesert.PlayaRy; y += 25)
            {
                if (!HighDesert.OnPlaya(x, y)) continue;
                double h = t.HeightAt(x, y);
                lo = Math.Min(lo, h); hi = Math.Max(hi, h);
            }
        _o.WriteLine($"the playa: {HighDesert.PlayaRx * 2 * 3.28084:0} x {HighDesert.PlayaRy * 2 * 3.28084:0} ft, " +
                     $"height spread {(hi - lo):0.00} m");
        Assert.True(hi - lo < 0.4, $"the playa varies by {(hi - lo):0.0} m — a dry lake is level");
        foreach (HighDesert.Hazard hz in HighDesert.Hazards())
            Assert.False(HighDesert.OnPlaya(hz.X, hz.Y), $"a {hz.Kind} is out on the playa");
    }

    [Fact]
    public void AHilltopCanBeLandedOnAndLeftAgain()
    {
        // Measured, not asserted: from the lane's threshold, how much room is there, and how far is the drop
        // off each end? A mesa top is only worth having if going off the end is a real consequence.
        var t = Terrain();
        foreach (HighDesert.Hill hl in HighDesert.Hills)
        {
            if (!hl.Landable) continue;
            double hrad = hl.TopHeadingDeg * Math.PI / 180;
            double Drop(int end)
            {
                double a = end * (hl.TopLengthM * 0.5 + 120);
                double px = hl.X + Math.Cos(hrad) * a, py = hl.Y + Math.Sin(hrad) * a;
                return hl.TopSurfaceAt(hl.X + Math.Cos(hrad) * end * hl.TopLengthM * 0.5,
                                       hl.Y + Math.Sin(hrad) * end * hl.TopLengthM * 0.5) - t.HeightAt(px, py);
            }
            double dropLow = Drop(1), dropHigh = Drop(-1);
            _o.WriteLine($"{hl.Name,-16} land uphill on {hl.LandingHeadingDeg:000}°, {hl.TopLengthFt:0} ft of top, " +
                         $"{hl.TopSlopeDeg:0.0}° up; 120 m past the ends the ground is {dropHigh:0} m and {dropLow:0} m below");
            Assert.True(dropLow > 25, $"{hl.Name}: only {dropLow:0} m of drop off the low end — that is not a mesa");
        }
    }

    [Fact]
    public void TheHazardsAreTheSameEveryTime()
    {
        // A hazard you cannot learn is a dice roll. Two runs, same answer, down to the metre.
        var a = HighDesert.Hazards();
        var b = HighDesert.Hazards();
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].X, b[i].X, 6);
            Assert.Equal(a[i].Y, b[i].Y, 6);
            Assert.Equal(a[i].Kind, b[i].Kind);
        }
        int sage = a.FindAll(h => h.Kind == HighDesert.Scrub.Sagebrush).Count;
        int rock = a.FindAll(h => h.Kind == HighDesert.Scrub.Rock).Count;
        int juniper = a.FindAll(h => h.Kind == HighDesert.Scrub.Juniper).Count;
        _o.WriteLine($"{a.Count} hazards: {sage} sage you can mow, {rock} rocks and {juniper} junipers you cannot");
        Assert.True(a.Count > 200, $"only {a.Count} hazards over 8 x 3 km — the desert should feel populated");
        Assert.True(sage > rock + juniper, "most of the high desert is sagebrush");
    }

    [Fact]
    public void TheSurfaceUnderfootIsWhatItLooksLike()
    {
        // Gravel where a wheel is welcome, rough sage everywhere else — and nothing claimed outside the block.
        foreach (HighDesert.Hill hl in HighDesert.Hills)
            if (hl.Landable)
                Assert.Equal(WorldTerrain.Surface.Gravel, WorldTerrain.SurfaceAt(hl.X, hl.Y));
        Assert.Equal(WorldTerrain.Surface.Gravel, WorldTerrain.SurfaceAt(HighDesert.PlayaX, HighDesert.PlayaY));
        // A point in the open desert, well away from everything made.
        Assert.Equal(WorldTerrain.Surface.Rough, WorldTerrain.SurfaceAt(HighDesert.X0 + 40, HighDesert.Y0 + 40));
        // And 200 m outside the block the desert has no opinion at all.
        Assert.Null(HighDesert.SurfaceAt(HighDesert.X0 - 200, HighDesert.Y0 + 500));
        Assert.Null(HighDesert.SurfaceAt(HighDesert.X1 + 200, HighDesert.Y1 - 500));
    }
}

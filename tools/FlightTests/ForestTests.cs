using System.Collections.Generic;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlightTests;

/// <summary>
/// The timber (owner 2026-10-16: "Forests and a treeline — timber across the world, not just the six alpine
/// lakes: conifers up to a treeline that falls with latitude and rises on sun-facing slopes, solid where you
/// can hit them. Changes how every low approach feels.").
///
/// The forest is a FUNCTION of the cell a tree stands in, not a list, so what these tests check is the shape
/// of that function: that the line is where it should be, that nothing grows where somebody has built or
/// where nothing could, that there are meadows as well as timber, and that a tree you can see is a tree you
/// can hit — and that the sky above the tallest tree costs nothing to ask about, because the collision asks
/// about it sixty times a second.
/// </summary>
[Collection("WorldTerrainActive")]
public class ForestTests : System.IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t;
    public ForestTests(ITestOutputHelper o)
    {
        _o = o; _t = new WorldTerrain(); WorldTerrain.Active = _t;
        // WorldSolids is global. Another test in this collection leaves its buildings and bridge decks in
        // the box list, and "nothing up here" then finds a tower — this passed alone and failed in the suite.
        WorldSolids.Boxes.Clear(); WorldSolids.Shapes.Clear();
    }
    public void Dispose() { WorldTerrain.Active = null; WorldSolids.Boxes.Clear(); WorldSolids.Shapes.Clear(); }

    [Fact]
    public void TheTreeLineFallsGoingNorthAndRisesWhereTheGroundFacesTheSun()
    {
        // Latitude: 16 km of northing is 900 m of treeline, which is the whole reason to fly the length of
        // the world. Measured on flat ground down the middle so only x is changing.
        double south = Forest.TreeLineAt(-6500, -5000, 0), north = Forest.TreeLineAt(8500, -5000, 0);
        _o.WriteLine($"treeline: {south:0} m ({south * 3.28084:0} ft) at the south end, {north:0} m ({north * 3.28084:0} ft) at the north");
        Assert.True(south - north > 600, $"the line only falls {south - north:0} m over the world");
        for (double x = -6500; x <= 8500; x += 1500)
            Assert.True(Forest.TreeLineAt(x, -5000, 0) > Forest.TreeLineAt(x + 1200, -5000, 0),
                        $"the line should keep falling northward, and does not at x {x:0}");

        // Aspect: the same spot, once with the ground falling away south and once north. dh/dx > 0 means the
        // ground rises to the north, so the face looks south, into the sun, and carries trees higher.
        double sunny = Forest.TreeLineAt(0, -5000, 0.8), shady = Forest.TreeLineAt(0, -5000, -0.8);
        _o.WriteLine($"same spot: {sunny:0} m on the sun-facing side, {shady:0} m on the shaded side — {sunny - shady:0} m apart");
        Assert.True(sunny - shady > 200, $"aspect is worth only {sunny - shady:0} m");
    }

    [Fact]
    public void NobodysRunwayApronOrStripHasTreesOnIt()
    {
        // Every made surface in the world, walked. Timber is allowed on ROUGH ground and nowhere else, which
        // is what keeps it off the pavement, the gravel, the groomed grass and the glaciers — but a rule is
        // only worth what the walk says it is.
        int checkedPoints = 0;
        foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            foreach (WorldTerrain.Strip st in WorldTerrain.AirportStrips)
            {
                double h = st.HeadingDeg * System.Math.PI / 180, c = System.Math.Cos(h), s = System.Math.Sin(h);
                for (double along = -st.Length / 2; along <= st.Length / 2; along += 20)
                    for (double across = -st.Width / 2 - 8; across <= st.Width / 2 + 8; across += 8)
                    {
                        double x = a.X + st.Dx + along * c - across * s, y = a.Y + st.Dy + along * s + across * c;
                        Assert.False(Forest.Timbered(x, y), $"timber on {a.Name}'s {st.Kind} strip at {x:0},{y:0}");
                        checkedPoints++;
                    }
            }
        // The backcountry strips, which are the ones that matter most: trees at the end of a 1,200 ft meadow
        // strip in a canyon are the difference between a landing and an accident, so they are off the strip
        // itself — and the test that they are CLOSE BY is in the test below.
        foreach (FrankChurch.Strip fs in FrankChurch.Strips)
        {
            // (X, Y) is the middle of the strip — Strip.On() measures |along| against half the length.
            double hh = fs.HeadingDeg * System.Math.PI / 180;
            for (double along = -fs.LengthM / 2; along <= fs.LengthM / 2; along += 20)
            {
                double x = fs.X + along * System.Math.Cos(hh), y = fs.Y + along * System.Math.Sin(hh);
                Assert.False(Forest.Timbered(x, y), $"timber on {fs.Name} at {x:0},{y:0}");
                checkedPoints++;
            }
        }
        // And the pylon courses: a gate you cannot see past is not a gate.
        foreach (int p in WorldTerrain.PlateausWith(WorldTerrain.Feature.RaceCourse))
            foreach (RaceElement e in RaceCourse.ElementsFor(p))
            {
                Assert.False(Forest.Timbered(e.X, e.Y), $"timber on a race element at {e.X:0},{e.Y:0}");
                foreach (Forest.Tree tr in Forest.InRect(e.X - 90, e.Y - 90, e.X + 90, e.Y + 90))
                    Assert.Fail($"a {tr.HeightM:0} m tree {System.Math.Sqrt((tr.X - e.X) * (tr.X - e.X) + (tr.Y - e.Y) * (tr.Y - e.Y)):0} m from a race element on plateau {p}");
                checkedPoints++;
            }
        _o.WriteLine($"{checkedPoints:N0} points walked on made surfaces, none of them timbered");
    }

    [Fact]
    public void TheBackcountryStripsAreTimberedRightUpToTheirEdges()
    {
        // The point of the whole exercise, in the owner's words: "changes how every low approach feels". A
        // strip with the trees cleared back 300 m is a runway; a strip with timber to the edge is a strip.
        var near = new List<string>();
        foreach (FrankChurch.Strip fs in FrankChurch.Strips)
        {
            double cx = fs.X, cy = fs.Y;
            double closest = double.MaxValue;
            foreach (Forest.Tree tr in Forest.InRect(cx - 700, cy - 700, cx + 700, cy + 700))
                closest = System.Math.Min(closest, System.Math.Sqrt((tr.X - cx) * (tr.X - cx) + (tr.Y - cy) * (tr.Y - cy)));
            _o.WriteLine($"{fs.Name}: nearest tree {(closest == double.MaxValue ? "none within 700 m" : $"{closest:0} m from the middle of the strip")}");
            if (closest < 400) near.Add(fs.Name);
        }
        Assert.True(near.Count >= 3, $"only {near.Count} of {FrankChurch.Strips.Length} backcountry strips have timber within 400 m");
    }

    [Fact]
    public void NothingGrowsInTheWaterOnAGlacierOrInTheDryCountry()
    {
        foreach (WorldTerrain.Lake l in WorldTerrain.Lakes)
            for (double a = 0; a < 6.28; a += 0.4)
                for (double f = 0; f <= 0.8; f += 0.4)
                {
                    double x = l.Cx + System.Math.Cos(a) * l.Rx * f, y = l.Cy + System.Math.Sin(a) * l.Ry * f;
                    Assert.Equal(0, Forest.CoverAt(x, y));
                }
        // The river, down its whole length.
        for (double y = -12000; y < 3000; y += 250)
            Assert.Equal(0, Forest.CoverAt(WorldTerrain.RiverCentreX(y), y));
        // The glaciers and the high desert.
        foreach (AlaskaRange.Glacier g in AlaskaRange.Glaciers)
            Assert.Equal(0, Forest.CoverAt((g.HeadX + g.TermX) / 2, (g.HeadY + g.TermY) / 2));
        for (double x = HighDesert.X0 + 100; x < HighDesert.X1; x += 400)
            for (double y = HighDesert.Y0 + 100; y < HighDesert.Y1; y += 400)
                Assert.Equal(0, Forest.CoverAt(x, y));
        _o.WriteLine("no timber on any lake, down the river, on either glacier, or anywhere in the high desert");
    }

    [Fact]
    public void ItIsStandsAndMeadowsAndAQuarterOfTheWorld()
    {
        // A forest that is an even speckle of trees everywhere is not a forest and leaves nowhere to land. So:
        // measure how much of the world carries timber, and how long the gaps are.
        int n = 0, timbered = 0, solid = 0;
        double sum = 0;
        for (double x = -6800; x < 8800; x += 160)
            for (double y = -14800; y < 12300; y += 160)
            {
                double c = Forest.CoverAt(x, y);
                n++; sum += c;
                if (c > 0.05) timbered++;
                if (c > 0.75) solid++;
            }
        double pct = timbered * 100.0 / n;
        _o.WriteLine($"{pct:0.0} % of the world carries timber, {solid * 100.0 / n:0.0} % of it heavy, mean cover {sum / n:0.00}");
        Assert.InRange(pct, 10, 45);
        Assert.True(solid * 100.0 / n > 3, "nowhere is the forest heavy");

        // The gaps: walk a long transect through forested country and find the biggest clearing on it.
        double longest = 0, run = 0;
        for (double y = -9000; y < 2000; y += 20)
        {
            if (Forest.CoverAt(1200, y) < 0.05) { run += 20; longest = System.Math.Max(longest, run); }
            else run = 0;
        }
        _o.WriteLine($"down an 11 km transect at x 1,200 the biggest clearing is {longest:0} m");
        Assert.True(longest > 300, $"the biggest gap in 11 km is {longest:0} m — there is nowhere to put it down");
    }

    [Fact]
    public void ATreeStandsOnTheGroundAndTheOnesNearTheLineAreStunted()
    {
        int n = 0, tall = 0, stunted = 0;
        // Two blocks: 16 km² of valley floor for the big timber, and the third mountain front — the one that
        // carries the country up to 7,000 ft — where the ground climbs through the line and the last trees
        // standing on it are waist-high.
        foreach (Forest.Tree tr in Forest.InRect(-2000, -6000, 2000, -2000).Concat(Forest.InRect(-3000, -8400, 0, -7400)))
        {
            Assert.Equal(_t.HeightAt(tr.X, tr.Y), tr.GroundM, 6);        // on the ground, not above or in it
            Assert.InRange(tr.HeightM, 2.0, Forest.MaxHeightM);
            Assert.True(tr.CrownRadiusM > 0.5 && tr.CrownRadiusM < 7, $"a crown {tr.CrownRadiusM:0.0} m across");
            double below = Forest.TreeLineAt(tr.X, tr.Y) - tr.GroundM;
            if (below > 900) tall++;
            if (below < 60) { stunted++; Assert.True(tr.HeightM < 12, $"a {tr.HeightM:0} m tree {below:0} m under the line"); }
            n++;
        }
        _o.WriteLine($"{n:N0} trees: {tall:N0} of them well below the line, {stunted} of them in the krummholz within 60 m of it");
        Assert.True(n > 500, $"only {n} trees in 16 km² of valley");
        Assert.True(tall > 0, "no full-grown timber anywhere in the valley");
        Assert.True(stunted > 0, "the line is a wall — nothing is stunted approaching it");
    }

    [Fact]
    public void ATreeYouCanSeeIsATreeYouCanHit()
    {
        Forest.RegisterSolids();
        int tested = 0;
        foreach (Forest.Tree tr in Forest.InRect(-1500, -5200, 1500, -4000).Take(400))
        {
            double mid = tr.GroundM + tr.HeightM * 0.5;
            // In the trunk: solid.
            var hit = WorldSolids.Penetration(tr.X, tr.Y, mid);
            Assert.NotNull(hit);
            // Just outside the crown: nothing. (Unless a neighbour's crown reaches here, which is a forest
            // doing its job — so only assert where no other stem is within reach.)
            double out1 = tr.CrownRadiusM + 4;
            bool lonely = !Forest.InRect(tr.X - 20, tr.Y - 20, tr.X + 20, tr.Y + 20)
                                 .Any(o => (o.X != tr.X || o.Y != tr.Y) &&
                                           System.Math.Sqrt((o.X - tr.X) * (o.X - tr.X) + (o.Y - tr.Y) * (o.Y - tr.Y)) < out1 + o.CrownRadiusM + 1);
            if (lonely) Assert.Null(WorldSolids.Penetration(tr.X + out1, tr.Y, mid));
            // Over the top: nothing.
            Assert.Null(WorldSolids.Penetration(tr.X, tr.Y, tr.TopM + 1.0));
            tested++;
        }
        _o.WriteLine($"{tested} stems: solid in the trunk, empty past the crown and over the top");
        Assert.True(tested > 100);
    }

    [Fact]
    public void AboveTheTimberTheSkyCostsOneHeightQuery()
    {
        // The whole design rests on this: at any normal height the forest answers "nothing here" without
        // looking at a single cell. If this ever stops being true, the sim gets slower everywhere.
        Forest.RegisterSolids();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int probes = 0;
        for (double x = -5000; x < 8000; x += 97)
            for (double y = -14000; y < 11000; y += 97)
            {
                Assert.Null(WorldSolids.Penetration(x, y, _t.HeightAt(x, y) + Forest.SkyClearM + 1));
                probes++;
            }
        sw.Stop();
        _o.WriteLine($"{probes:N0} probes at {Forest.SkyClearM:0} m AGL: {sw.Elapsed.TotalMilliseconds / probes * 1000:0} µs each, none of them in anything");
    }

    [Fact]
    public void TheSameCellAlwaysAnswersTheSameWay()
    {
        // Chunks are built and thrown away as the camera moves, so a cell asked an hour later has to come
        // back identical or the forest crawls.
        for (int i = -40; i < 40; i++)
            for (int j = -40; j < 40; j++)
            {
                bool a = Forest.TryTreeInCell(i, j, out Forest.Tree ta);
                bool b = Forest.TryTreeInCell(i, j, out Forest.Tree tb);
                Assert.Equal(a, b);
                if (a) { Assert.Equal(ta.X, tb.X); Assert.Equal(ta.HeightM, tb.HeightM); }
            }
        // And a rectangle built in two halves holds the same trees as the whole.
        var whole = Forest.InRect(0, -5000, 400, -4600).Select(t => (t.X, t.Y)).ToList();
        var halves = Forest.InRect(0, -5000, 200, -4600).Select(t => (t.X, t.Y))
                     .Concat(Forest.InRect(200, -5000, 400, -4600).Select(t => (t.X, t.Y))).ToList();
        Assert.Equal(whole.OrderBy(p => p.X).ThenBy(p => p.Y), halves.OrderBy(p => p.X).ThenBy(p => p.Y));
        _o.WriteLine($"{whole.Count} trees in a 400 m square, the same however the square is cut up");
    }
}

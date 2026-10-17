using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The mountain fronts (owner 2026-10-16: "reshape the risers as mountain fronts rather than flat shelves").
/// A riser used to be a 183 m ramp with a flat top; it is now embayed, with spurs reaching out onto the lower
/// plateau, summits above the crest and saddles between them. These tests hold the four things that make it
/// one rather than a staircase — that it still climbs exactly one step, that it varies, that there is always a
/// way over it, and that it has not grown into anything that was already standing there.
/// </summary>
[Collection("WorldTerrainActive")]
public class MountainFrontTests
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t = new();
    public MountainFrontTests(ITestOutputHelper o) { _o = o; }

    private const double XMin = -6800, XMax = 8800;

    [Fact]
    public void EveryFrontStillClimbsExactlyOneStep()
    {
        for (int i = 0; i < WorldTerrain.StepCount; i++)
        {
            double lower = WorldTerrain.DatumM + WorldTerrain.StepHeightM * i;
            double upper = lower + WorldTerrain.StepHeightM;
            for (double x = XMin; x <= XMax; x += 97)
            {
                if (System.Math.Abs(x - _t.Waterfalls[i].X) < 600) continue;      // the water gap is a hole in the front
                double crest = WorldTerrain.CrestY(i, x), run = _t.FrontWidthAt(i, x);
                // Clear of the debris at the toe: the lower plateau (the gorge and the hills aside, which
                // BaseHeightAt does not carry).
                Assert.InRange(_t.BaseHeightAt(x, crest + run + WorldTerrain.FanRunM + 60), lower - 8, lower + 8);
                // And ON the debris, 60 m out from the toe: above the plateau, because that is what a fan is,
                // but never by more than the fan is thick.
                Assert.InRange(_t.BaseHeightAt(x, crest + run + 60), lower - 8, lower + WorldTerrain.FanHeightM + 2);
                // Past the back slope: the upper plateau, exactly.
                var (_, back) = _t.CrestAt(i, x);
                Assert.InRange(_t.BaseHeightAt(x, crest - back - 120), upper - 8, upper + 8);
            }
        }
    }

    [Fact]
    public void ItIsEmbayedAndNotAWall()
    {
        for (int i = 0; i < WorldTerrain.StepCount; i++)
        {
            double minW = double.MaxValue, maxW = 0, minToe = double.MaxValue, maxToe = double.MinValue, maxRelief = 0;
            for (double x = XMin; x <= XMax; x += 25)
            {
                double w = _t.FrontWidthAt(i, x), toe = WorldTerrain.CrestY(i, x) + w;
                minW = System.Math.Min(minW, w); maxW = System.Math.Max(maxW, w);
                minToe = System.Math.Min(minToe, toe); maxToe = System.Math.Max(maxToe, toe);
                maxRelief = System.Math.Max(maxRelief, _t.CrestAt(i, x).reliefM);
            }
            _o.WriteLine($"front {i}: run {minW:F0}–{maxW:F0} m, toe wanders {maxToe - minToe:F0} m, summits to {maxRelief:F0} m");
            Assert.Equal(WorldTerrain.EscarpmentWidthM, minW, 1);          // the old cliff survives in the bays
            Assert.True(maxW > 900, $"front {i}: no spur reaches out ({maxW:F0} m)");
            Assert.True(maxToe - minToe > 800, $"front {i}: the toe hardly wanders ({maxToe - minToe:F0} m)");
            Assert.True(maxRelief > 300, $"front {i}: no summit stands over the plateau ({maxRelief:F0} m)");
        }
    }

    /// <summary>
    /// A front you cannot get over without climbing to its summits is a wall with scenery on it. The saddles
    /// between the summits are the passes, and this holds that there is one within 3 km wherever you meet the
    /// front — and that a pass really is at plateau level, not a dip in a mountain.
    /// </summary>
    [Fact]
    public void ThereIsAPassWithinThreeKilometresAnywhereAlongEveryFront()
    {
        const double window = 3000;
        for (int i = 0; i < WorldTerrain.StepCount; i++)
        {
            double upper = WorldTerrain.DatumM + WorldTerrain.StepHeightM * (i + 1);
            int found = 0;
            for (double x0 = XMin; x0 + window <= XMax; x0 += 500)
            {
                double best = double.MaxValue, bestX = 0;
                for (double x = x0; x <= x0 + window; x += 25)
                {
                    double r = _t.CrestAt(i, x).reliefM;
                    // The water gap is the lowest pass of all, and the river found it first — but its floor
                    // is the LOWER plateau (that is what an amphitheater is), so it is no use for the height
                    // check below. Count it as a pass; measure a dry one.
                    if (System.Math.Abs(x - _t.Waterfalls[i].X) < 1.6 * WorldTerrain.FallNotchHalfSpanM) continue;
                    if (r < best) { best = r; bestX = x; }
                }
                Assert.True(best < 60, $"front {i}: no pass in x {x0:F0}..{x0 + window:F0} (lowest saddle {best:F0} m)");
                // And the ground at the saddle is the plateau, give or take the rock texture on the face.
                double hCrest = _t.BaseHeightAt(bestX, WorldTerrain.CrestY(i, bestX) - 20);
                Assert.InRange(hCrest, upper - 15, upper + 75);
                found++;
            }
            _o.WriteLine($"front {i}: a pass in every one of {found} windows of 3 km");
        }
    }

    /// <summary>
    /// The aerodromes sit in bays in the range — which is the usual reason an aerodrome is where it is. All
    /// four share an x, so every front keeps the old cliff across the middle, and the spurs are out to the
    /// sides where there is room for them.
    /// </summary>
    [Fact]
    public void TheAerodromesSitInBaysInTheRange()
    {
        for (int i = 0; i < WorldTerrain.StepCount; i++)
        {
            WorldTerrain.Airport below = WorldTerrain.Airports[i];
            for (double x = below.X - WorldTerrain.PadSouthX; x <= below.X + WorldTerrain.PadHalfX; x += 50)
            {
                double toe = WorldTerrain.CrestY(i, x) + _t.FrontWidthAt(i, x);
                Assert.True(toe < below.Y + WorldTerrain.PadHalfY + 300 - 100,
                            $"front {i} reaches the {below.Name} pad: toe y {toe:F0}");
                Assert.Equal(WorldTerrain.EscarpmentWidthM, _t.FrontWidthAt(i, x), 1);
            }
            // And the pad is still exactly flat at field elevation.
            Assert.Equal(below.ElevationM, _t.HeightAt(below.X, below.Y), 3);
        }
    }

    /// <summary>
    /// The crest is where the old wall's top was, which is why the three plunge falls, their amphitheaters and
    /// the tunnel maze behind the first one did not move a metre when the fronts went in.
    /// </summary>
    [Fact]
    public void TheWaterfallsAndTheMazeDidNotMove()
    {
        foreach (WorldTerrain.Waterfall f in _t.Waterfalls)
        {
            Assert.Equal(WorldTerrain.CrestY(f.Step, f.X), f.LipY, 0);
            Assert.Equal(WorldTerrain.EscarpmentWidthM, _t.FrontWidthAt(f.Step, f.X), 1);   // no spur across a water gap
            Assert.Equal(0.0, _t.CrestAt(f.Step, f.X).reliefM, 6);                          // and no summit over it
            Assert.Equal(WorldTerrain.StepHeightM, f.UpperM - f.LowerM, 6);
        }
    }

    /// <summary>
    /// The thing a height probe inside a block can never see: a cliff along its boundary, because inside it
    /// the ground is exactly what it should be. The first wall's wander took its crest up to 138 m past the
    /// high desert's northern edge, and the desert's floor stood 560 m above the half-built escarpment under
    /// it for three kilometres. Walk the edge of every block that FLATTENS its ground and compare the two
    /// sides of the line.
    /// </summary>
    [Fact]
    public void NoBlockThatFlattensItsGroundEndsInACliff()
    {
        // The old version of this test walked the line on the map and found nothing, because the step is not
        // at the line: each of these blocks lays its own ground down for a few hundred metres OUTSIDE its
        // declared bounds and then stops. On three sides of the high desert the plateau is at the same height
        // and nothing shows; on the fourth the third mountain front climbs, so the desert's floor was laid
        // across its face and ended in a 900 m wall 560 m south of its own Y0 (found 2026-10-16, by probing a
        // render that showed a flat green wall where a mountainside should have been).
        //
        // So: walk right across each margin, at 5 m — fine enough that a real escarpment, which is 3.3 : 1 by
        // design and may be in here, cannot be mistaken for a discontinuity.
        (string name, double x0, double y0, double x1, double y1, double pad)[] blocks =
        {
            ("braided plain", BraidedRiver.X0, BraidedRiver.Y0, BraidedRiver.X1, BraidedRiver.Y1, BraidedRiver.FootprintPadM),
            ("high desert", HighDesert.X0, HighDesert.Y0, HighDesert.X1, HighDesert.Y1, HighDesert.FootprintPadM),
            ("Frank Church", FrankChurch.X0, FrankChurch.Y0, FrankChurch.X1, FrankChurch.Y1, FrankChurch.FootprintPadM),
        };
        double worst = 0; string where = "nowhere";
        foreach (var b in blocks)
            for (double x = b.x0; x <= b.x1; x += 40)
            {
                if (System.Math.Abs(x - WorldTerrain.RiverCentreX(b.y0)) < 500) continue;   // the gorge crosses it
                foreach (int side in new[] { -1, 1 })
                {
                    double edge = side < 0 ? b.y0 : b.y1;
                    double from = edge + side * -120, to = edge + side * (b.pad + 260);
                    double prev = _t.HeightAt(x, from);
                    for (double y = from + side * 5; side < 0 ? y >= to : y <= to; y += side * 5)
                    {
                        double h = _t.HeightAt(x, y);
                        double d = System.Math.Abs(h - prev);
                        prev = h;
                        if (d > worst) { worst = d; where = $"{b.name} at x {x:F0}, y {y:F0}"; }
                    }
                }
            }
        _o.WriteLine($"worst step anywhere across a block's margin: {worst:F1} m over 5 m — {where}");
        // 150 m in 5 m, not 60: where a front is squeezed to its minimum width the ESCARPMENT ITSELF is
        // 87 m in 5 m — 500 m of climb in 82 m of run, plus a summit's back slope ramping in on top of it —
        // and that cliff is there on purpose (owner 2026-10-16: "leave the cliff"). What this is looking for
        // is a block's ground stopping dead, which is hundreds of metres in nothing at all.
        Assert.True(worst < 150, $"a wall where a block's ground stops: {worst:F0} m in 5 m — {where}");
    }
}

using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>The solids broadphase (WorldSolids.Nearby, iOS 2026-11-02): the island's ~1,500 solids were tested for every
/// hard point at every RK4 stage. The broadphase must give exactly the answers of the full scan, and gather rarely.</summary>
[Collection("WorldTerrainActive")]
public sealed class SolidsBroadphaseTests
{
    [Fact]
    public void TheBroadphaseAnswersExactlyAsTheFullScanAcrossTheIsland()
    {
        IslandGeometry.Build();
        Assert.True(WorldSolids.Shapes.Count > 1000, $"island solids: {WorldSolids.Shapes.Count}");
        var rnd = new System.Random(7);
        int hits = 0, checkedPts = 0;
        // Around every 7th solid's own position (so plenty of points ARE inside something), and at random.
        var centres = new System.Collections.Generic.List<(double x, double y, double up)>();
        foreach (var b in IslandGeometry.Blocks) centres.Add((b.X, b.Y, (b.Bottom + b.Top) / 2));
        foreach (var c in IslandGeometry.Cables) centres.Add(((c.A.X + c.B.X) / 2, (c.A.Y + c.B.Y) / 2, -(c.A.Z + c.B.Z) / 2));
        for (int i = 0; i < 300; i++) centres.Add((rnd.NextDouble() * 40000 - 20000, rnd.NextDouble() * 40000 - 20000, rnd.NextDouble() * 400));
        var nb = new WorldSolids.Nearby();
        for (int i = 0; i < centres.Count; i += 3)
        {
            var (cx, cy, cu) = centres[i];
            const double reach = 8;
            nb.Update(cx - reach, cy - reach, cu - reach, cx + reach, cy + reach, cu + reach);
            for (int k = 0; k < 20; k++)
            {
                double x = cx + (rnd.NextDouble() * 2 - 1) * reach, y = cy + (rnd.NextDouble() * 2 - 1) * reach, u = cu + (rnd.NextDouble() * 2 - 1) * reach;
                var full = WorldSolids.Penetration(x, y, u);
                var near = nb.Penetration(x, y, u);
                Assert.Equal(full.HasValue, near.HasValue);
                if (full.HasValue) { hits++; Assert.Equal(full.Value.depth, near!.Value.depth); Assert.Equal(full.Value.normalNed, near.Value.normalNed); }
                checkedPts++;
            }
        }
        Assert.True(hits > 200, $"only {hits} of {checkedPts} points were inside a solid; the test checks too little");
    }

    [Fact]
    public void TheForestStandInAnswersExactlyAsTheWholeForest()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            IslandGeometry.Build();
            var rnd = new System.Random(11);
            var trees = new System.Collections.Generic.List<Forest.Tree>();
            for (double x = -20000; x < 20000 && trees.Count < 120; x += 997)
                for (double y = -20000; y < 20000 && trees.Count < 120; y += 1013)
                    foreach (var t in Forest.InRect(x, y, x + 60, y + 60)) { trees.Add(t); break; }
            Assert.True(trees.Count > 30, $"only {trees.Count} trees found");
            int hits = 0;
            // Since 2026-11-03 the forest is frangible (TreeStructure): the airframe's hard-point broadphase leaves it out and
            // the aeroplane meets trees through its own strike test, so the stand-in is checked against the whole forest
            // shape directly — the same Within() the broadphase used to call.
            var forest = WorldSolids.Shapes.OfType<Forest.Solids>().First();
            Assert.IsAssignableFrom<WorldSolids.IFrangible>(forest);
            foreach (var t in trees)
            {
                double cu = t.GroundM + t.HeightM * 0.7;
                const double reach = 8;
                WorldSolids.IShape nb = forest.Within(t.X - reach - WorldSolids.Nearby.RegionMarginM, t.Y - reach - WorldSolids.Nearby.RegionMarginM, cu - reach - WorldSolids.Nearby.RegionMarginM,
                                                      t.X + reach + WorldSolids.Nearby.RegionMarginM, t.Y + reach + WorldSolids.Nearby.RegionMarginM, cu + reach + WorldSolids.Nearby.RegionMarginM);
                for (int k = 0; k < 40; k++)
                {
                    // Mostly inside the region; a few outside it, where the stand-in must fall back to the whole forest.
                    double span = k % 10 == 0 ? 120 : reach;
                    double x = t.X + (rnd.NextDouble() * 2 - 1) * span, y = t.Y + (rnd.NextDouble() * 2 - 1) * span;
                    double u = t.GroundM + rnd.NextDouble() * (t.HeightM + 4);
                    bool fullHit = forest.Penetrate(x, y, u, out Vec3 fn, out double fd);
                    bool nearHit = nb.Penetrate(x, y, u, out Vec3 nn, out double nd);
                    Assert.Equal(fullHit, nearHit);
                    if (fullHit) { hits++; Assert.Equal(fd, nd); Assert.Equal(fn, nn); }
                }
            }
            Assert.True(hits > 100, $"only {hits} points hit a tree");
        }
        finally { WorldTerrain.Active = null; }
    }

    [Fact]
    public void AnAirframeCruisingRegathersRarelyAndAfterARebuild()
    {
        IslandGeometry.Build();
        var nb = new WorldSolids.Nearby();
        for (int i = 0; i < 1000; i++)   // 5 s at 200 Hz, 60 m/s
        {
            double x = i * 0.3;
            nb.Update(x - 6, -6, 94, x + 6, 6, 106);
        }
        Assert.InRange(nb.Gathers, 1, 5);
        int before = nb.Gathers;
        IslandGeometry.Build();
        nb.Update(294, -6, 94, 306, 6, 106);
        Assert.Equal(before + 1, nb.Gathers);
    }
}

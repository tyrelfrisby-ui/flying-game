using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Quest 3S, build 8e7862eb, 2026-11-02: a 2-33 sitting on the ground at (5854, 3825) ran at 27–30 fps because every
/// hard point asked all 1,475 of the island's solids and 128 decks about itself at each RK4 stage, and the forest's
/// solid worked out the trees round it from scratch every time. The fix files the solids and decks by footprint
/// (<see cref="FootprintIndex{T}"/>) and remembers each forest cell's tree. Neither may change an answer: these hold
/// the indexed answers to the full walk, the footprints to their shapes, the remembered trees to fresh ones, and
/// whole flights, on and off, to the same bits.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class WorldIndexTests : IDisposable
{
    public WorldIndexTests()
    {
        WorldTerrain.Active = new WorldTerrain();
        IslandGeometry.Build();
    }

    public void Dispose()
    {
        WorldSolids.IndexEnabled = true;
        WorldTerrain.Active = null;
        WorldSolids.Boxes.Clear(); WorldSolids.Shapes.Clear(); WorldDecks.All.Clear();
    }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>The island registers what the headset log implies: well over a thousand solids, most of them filed by footprint.</summary>
    [Fact]
    public void TheIslandsSolidsAreMostlyFiledByFootprint()
    {
        Assert.True(WorldSolids.Shapes.Count > 1000, $"{WorldSolids.Shapes.Count} shapes");
        Assert.True(WorldSolids.ShapeIndex.FootprintCount > WorldSolids.Shapes.Count * 9 / 10,
            $"only {WorldSolids.ShapeIndex.FootprintCount} of {WorldSolids.Shapes.Count} shapes have a footprint");
        Assert.True(WorldDecks.Index.FootprintCount >= WorldDecks.All.Count - 2, $"{WorldDecks.Index.FootprintCount} of {WorldDecks.All.Count} decks");
    }

    /// <summary>Every footprint holds its shape: points just outside each rectangle, at every height a shape has, hit nothing.</summary>
    [Fact]
    public void NothingOutsideAFootprintAnswers()
    {
        var rng = new Random(11);
        int tested = 0;
        void Check(object item)
        {
            if (item is not WorldSolids.IFootprint f) return;
            f.Footprint(out double x0, out double y0, out double x1, out double y1);
            for (int k = 0; k < 64; k++)
            {
                // A point on a band 0–3 m outside the rectangle, on a random side.
                double t = rng.NextDouble(), out_ = rng.NextDouble() * 3 + 1e-6;
                (double x, double y) = rng.Next(4) switch
                {
                    0 => (x0 - out_, y0 + t * (y1 - y0)),
                    1 => (x1 + out_, y0 + t * (y1 - y0)),
                    2 => (x0 + t * (x1 - x0), y0 - out_),
                    _ => (x0 + t * (x1 - x0), y1 + out_),
                };
                double g = WorldTerrain.GroundHeightAt(x, y);
                foreach (double up in new[] { g - 5, g, g + 1, g + 10, g + 40, g + 150, g + 600 })
                {
                    if (item is WorldSolids.IShape sh) Assert.False(sh.Penetrate(x, y, up, out _, out _), $"{item.GetType().Name} answers outside its footprint at ({x:F2}, {y:F2}, {up:F1})");
                    tested++;
                }
                if (item is WorldDecks.IDeck d) Assert.False(d.TopAt(x, y, out _), $"{item.GetType().Name} deck outside its footprint at ({x:F2}, {y:F2})");
            }
        }
        foreach (var sh in WorldSolids.Shapes) Check(sh);
        foreach (var d in WorldDecks.All) Check(d);
        Assert.True(tested > 100000, $"{tested}");
    }

    /// <summary>The indexed answers are the full walk's, to the bit: across the island at random, and densely round
    /// and inside every solid and deck that has a footprint (where the answers are not all "nothing").</summary>
    [Fact]
    public void IndexedAnswersEqualTheFullWalk()
    {
        var rng = new Random(5);
        var pts = new List<(double x, double y, double up)>();
        for (int i = 0; i < 20000; i++)
        {
            double x = rng.NextDouble() * 24000 - 2000, y = rng.NextDouble() * 24000 - 2000;
            pts.Add((x, y, WorldTerrain.GroundHeightAt(x, y) + rng.NextDouble() * 80 - 5));
        }
        foreach (object item in WorldSolids.Shapes.Cast<object>().Concat(WorldDecks.All))
        {
            if (item is not WorldSolids.IFootprint f) continue;
            f.Footprint(out double x0, out double y0, out double x1, out double y1);
            for (int k = 0; k < 12; k++)
            {
                double x = x0 + rng.NextDouble() * (x1 - x0), y = y0 + rng.NextDouble() * (y1 - y0);
                double g = WorldTerrain.GroundHeightAt(x, y);
                pts.Add((x, y, g + rng.NextDouble() * 120 - 5));
                if (item is WorldDecks.IDeck d && d.TopAt(x, y, out double top)) pts.Add((x, y, top + rng.NextDouble() * 3 - 1));
            }
        }
        int hits = 0, decks = 0;
        foreach (var (x, y, up) in pts)
        {
            WorldSolids.IndexEnabled = false;
            var solidAll = WorldSolids.Penetration(x, y, up);
            bool deckAll = WorldDecks.DeckUnder(x, y, up, out double topAll);
            Vec3 velAll = WorldDecks.SurfaceVelocity(x, y, up);
            WorldSolids.IndexEnabled = true;
            var solid = WorldSolids.Penetration(x, y, up);
            bool deck = WorldDecks.DeckUnder(x, y, up, out double top);
            Vec3 vel = WorldDecks.SurfaceVelocity(x, y, up);
            Assert.Equal(solidAll.HasValue, solid.HasValue);
            if (solid.HasValue)
            {
                hits++;
                Assert.Equal(solidAll!.Value.depth, solid.Value.depth);
                Assert.Equal(solidAll.Value.normalNed.X, solid.Value.normalNed.X);
                Assert.Equal(solidAll.Value.normalNed.Y, solid.Value.normalNed.Y);
                Assert.Equal(solidAll.Value.normalNed.Z, solid.Value.normalNed.Z);
            }
            Assert.Equal(deckAll, deck);
            Assert.Equal(topAll, top);
            if (deck) decks++;
            Assert.Equal(velAll.X, vel.X); Assert.Equal(velAll.Y, vel.Y); Assert.Equal(velAll.Z, vel.Z);
        }
        Assert.True(hits > 1000, $"only {hits} of {pts.Count} points were inside a solid: the test proves little");
        Assert.True(decks > 200, $"only {decks} points on a deck");
    }

    /// <summary>A remembered forest cell is the same tree as one worked out afresh, over the forest the 2-33 sat in and
    /// far from it (so the cache has wrapped and been refilled).</summary>
    [Fact]
    public void RememberedTreesAreTheSameTrees()
    {
        int trees = 0;
        foreach ((double cx, double cy) in new[] { (5854.0, 3825.0), (6188.0, 5183.0), (12000.0, 9000.0), (5854.0, 3825.0) })
        {
            int ci = Forest.CellOf(cx), cj = Forest.CellOf(cy);
            for (int i = ci - 40; i <= ci + 40; i++)
                for (int j = cj - 40; j <= cj + 40; j++)
                {
                    bool fresh = Forest.TreeInCellUncached(i, j, out Forest.Tree a);
                    bool cached = Forest.TryTreeInCell(i, j, out Forest.Tree b);
                    bool again = Forest.TryTreeInCell(i, j, out Forest.Tree c);
                    Assert.Equal(fresh, cached); Assert.Equal(fresh, again);
                    if (!fresh) continue;
                    trees++;
                    foreach (Forest.Tree t in new[] { b, c })
                    {
                        Assert.Equal(a.X, t.X); Assert.Equal(a.Y, t.Y); Assert.Equal(a.GroundM, t.GroundM); Assert.Equal(a.HeightM, t.HeightM);
                        Assert.Equal(a.RadiusM, t.RadiusM); Assert.Equal(a.LeanDeg, t.LeanDeg); Assert.Equal(a.TwistDeg, t.TwistDeg);
                    }
                }
        }
        Assert.True(trees > 200, $"only {trees} trees: the spots are not in the forest");
    }

    public static IEnumerable<object[]> Flights()
    {
        foreach (string ac in new[] { "glider-2-33-like", "pitts-s2b-like" })
            foreach (string kind in new[] { "rest", "crash", "takeoff" })
                yield return new object[] { ac, kind };
    }

    /// <summary>The headset's case, flown with the index and the tree cache and without: on the ground at rest, into the
    /// ground and broken up, a takeoff run — at (5854, 3825), among the trees. Same bits at the end, fewer queries.</summary>
    [Theory]
    [MemberData(nameof(Flights))]
    public void FlightsAmongTheSolidsAreBitIdentical(string id, string kind)
    {
        (RigidBodyState s, string trace, long queries) Fly(bool index)
        {
            WorldSolids.IndexEnabled = index;
            var c = Load(id);
            const double x = 5854, y = 3825;
            double surface = Math.Max(WorldTerrain.GroundHeightAt(x, y), FloatHydro.WaterSurfaceAt(x, y) ?? double.NegativeInfinity);
            RigidBodyState s0 = kind switch
            {
                "rest" => LandingGear.RestingState(c, x, y, surface, 0.3),
                "takeoff" => LandingGear.RestingState(c, x, y, surface, 2.1),
                _ => new RigidBodyState(new Vec3(x, y, -(surface + 40)), new Quat(Math.Sin(0.15), Math.Sin(-0.12), 0, Math.Cos(0.19)).Normalized(), new Vec3(40, 0, 10), new Vec3(0.3, 0, 0.1)),
            };
            var input = kind == "takeoff" ? new ControlInputs(0, -0.3, 0.1, 1.0) : new ControlInputs(0.1, 0, -0.2, -1.0);
            var ac = new Aircraft(c, s0, ControlDeflections.Neutral);
            var loop = new SimLoop(ac);
            double acc = 0;
            var trace = new System.Text.StringBuilder();
            SimProfile.Reset();
            for (int f = 0; f < 72 * 5; f++)
            {
                SimProfile.Enabled = true;
                loop.Advance(1.0 / 72.0, input, ref acc);
                SimProfile.Enabled = false;
                if (f % 24 == 0)
                    trace.Append(ac.State.Position.X.ToString("R")).Append(',').Append(ac.State.Position.Z.ToString("R")).Append(',')
                         .Append(ac.State.Velocity.X.ToString("R")).Append(',').Append(ac.State.Rates.Y.ToString("R")).Append(';');
            }
            foreach (ContactPoint p in ac.ContactPoints) trace.Append(p.Name).Append(p.Touching ? '+' : '-');
            trace.Append('|').Append(string.Join(",", ac.LostComponents));
            return (ac.State, trace.ToString(), SimProfile.HeightQueries);
        }
        var (off, offTrace, offQ) = Fly(false);
        var (on, onTrace, onQ) = Fly(true);
        Assert.Equal(offTrace, onTrace);
        Assert.Equal(off.Position.X, on.Position.X); Assert.Equal(off.Position.Y, on.Position.Y); Assert.Equal(off.Position.Z, on.Position.Z);
        Assert.Equal(off.Velocity.X, on.Velocity.X); Assert.Equal(off.Velocity.Y, on.Velocity.Y); Assert.Equal(off.Velocity.Z, on.Velocity.Z);
        Assert.Equal(off.Attitude.W, on.Attitude.W); Assert.Equal(off.Attitude.X, on.Attitude.X);
        Assert.Equal(off.Rates.X, on.Rates.X); Assert.Equal(off.Rates.Z, on.Rates.Z);
        // The tree cache is on in both runs (it cannot be switched off), so the queries it saves do not show here;
        // the index's own saving is in the solids it no longer asks, which make height queries of their own.
        Assert.True(onQ <= offQ, $"{onQ} height queries with the index against {offQ} without");
    }
}

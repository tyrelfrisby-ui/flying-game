using System.Reflection;
using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A test that installs the island must take it away again. <c>WorldTerrain.Active</c> is process-wide and the suite
/// runs every class in one process, so a class that leaves the island installed moves the ground under every class
/// that happens to run after it: IslandStartupCostTests did, and in the B1 qualification run (2026-11-06, t971u) the
/// 40 StructuralTests names that start at 600-1,500 m over "flat" ground began 2,460 m underground and went NaN. They
/// only failed when xUnit's class order happened to put StructuralTests after it, which is why the same source passed
/// on the branch point.
///
/// These run each IslandStartupCostTests test the way xUnit does (constructor, method, Dispose) and require the
/// terrain to be exactly what it was before, and the ground at the origin flat again. A test added to that class later
/// is covered without touching this file. The same holds for the island's solids and decks (IslandWorldScope), which
/// IosLowAltitudeProbe, SolidsBroadphaseTests, UrbanLowRiseTests and ApproachSpawnTests left registered.
/// </summary>
[Collection("WorldTerrainActive")]   // swaps the global terrain: never in parallel with the legacy-world tests
public sealed class TerrainLeakTests
{
    private sealed class Quiet : ITestOutputHelper
    {
        public void WriteLine(string message) { }
        public void WriteLine(string format, params object[] args) { }
    }

    public static IEnumerable<object[]> IslandStartupCostTestNames() =>
        typeof(IslandStartupCostTests).GetMethods()
            .Where(m => m.GetCustomAttribute<FactAttribute>() is { Skip: null })
            .Select(m => new object[] { m.Name })
            .OrderBy(r => (string)r[0], StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(IslandStartupCostTestNames))]
    public void IslandStartupCostTestsPutTheTerrainBack(string method)
    {
        WorldTerrain? before = WorldTerrain.Active;
        try
        {
            WorldTerrain.Active = null;
            var test = new IslandStartupCostTests(new Quiet());
            try { typeof(IslandStartupCostTests).GetMethod(method)!.Invoke(test, null); }
            finally { if ((object)test is IDisposable d) d.Dispose(); }   // as xUnit does

            Assert.Null(WorldTerrain.Active);
            Assert.Equal(0.0, WorldTerrain.GroundHeightAt(0, 0));
        }
        finally { WorldTerrain.Active = before; }
    }

    /// <summary>Dispose restores the terrain that was there, not "no terrain": a caller that had its own world keeps it.</summary>
    [Fact]
    public void IslandStartupCostTestsRestoreAnEarlierTerrainRatherThanClearingIt()
    {
        WorldTerrain? before = WorldTerrain.Active;
        try
        {
            var mine = new WorldTerrain();
            WorldTerrain.Active = mine;
            var test = new IslandStartupCostTests(new Quiet());
            WorldTerrain.Active = new WorldTerrain();   // what a test body may install in its place
            if ((object)test is IDisposable d) d.Dispose();
            Assert.Same(mine, WorldTerrain.Active);
        }
        finally { WorldTerrain.Active = before; }
    }

    public static IEnumerable<object[]> SolidsBroadphaseTestNames() =>
        typeof(SolidsBroadphaseTests).GetMethods()
            .Where(m => m.GetCustomAttribute<FactAttribute>() is { Skip: null })
            .Select(m => new object[] { m.Name })
            .OrderBy(r => (string)r[0], StringComparer.Ordinal);

    /// <summary>SolidsBroadphaseTests builds the island's ~1,500 solids and 128 decks; after each of its tests the world
    /// must be empty again, as it was (t971u: left behind, they nearly doubled a flying aeroplane's step cost in
    /// IslandCombatTests and pushed its wreck/air cost ratio across the line).</summary>
    [Theory]
    [MemberData(nameof(SolidsBroadphaseTestNames))]
    public void SolidsBroadphaseTestsPutTheIslandsSolidsBack(string method)
    {
        using var outer = new IslandWorldScope();
        Empty();
        var test = new SolidsBroadphaseTests();
        try { typeof(SolidsBroadphaseTests).GetMethod(method)!.Invoke(test, null); }
        finally { if ((object)test is IDisposable d) d.Dispose(); }   // as xUnit does
        AssertEmpty();
    }

    /// <summary>The same for IosLowAltitudeProbe, whose one test builds the island and then times 21 flights (about
    /// 30 s): this does what its body does to the world, IslandGeometry.Build(), between its constructor and Dispose.</summary>
    [Fact]
    public void IosLowAltitudeProbePutsTheIslandsSolidsBack()
    {
        using var outer = new IslandWorldScope();
        Empty();
        var probe = new IosLowAltitudeProbe(new Quiet());
        WorldTerrain.Active = new WorldTerrain();
        IslandGeometry.Build();
        Assert.True(WorldSolids.Shapes.Count > 1000, $"island solids: {WorldSolids.Shapes.Count}");
        if ((object)probe is IDisposable d) d.Dispose();
        AssertEmpty();
    }

    /// <summary>The scope restores the world that was there (not an empty one), and moves WorldSolids.Version on so the
    /// broadphase cannot mistake the restored lists for the ones it last gathered.</summary>
    [Fact]
    public void IslandWorldScopeRestoresTheEarlierWorld()
    {
        using var outer = new IslandWorldScope();
        Empty();
        var box = new WorldSolids.Box(1, 2, 3, 4, 5, 6);
        WorldSolids.Boxes.Add(box);
        WorldSolids.IndexEnabled = false;
        var scope = new IslandWorldScope();
        IslandGeometry.Build();
        WorldSolids.IndexEnabled = true;
        int version = WorldSolids.Version;
        scope.Dispose();
        Assert.Equal(new[] { box }, WorldSolids.Boxes);
        Assert.Empty(WorldSolids.Shapes);
        Assert.Empty(WorldDecks.All);
        Assert.False(WorldSolids.IndexEnabled);
        Assert.NotEqual(version, WorldSolids.Version);
    }

    public static IEnumerable<object[]> UrbanLowRiseTestNames() =>
        typeof(UrbanLowRiseTests).GetMethods()
            .Where(m => m.GetCustomAttribute<FactAttribute>() is { Skip: null })
            .Select(m => new object[] { m.Name })
            .OrderBy(r => (string)r[0], StringComparer.Ordinal);

    /// <summary>UrbanLowRiseTests installs the island and builds into the world's solids (t971u: left behind, they put
    /// StructuralTests 2,460 m underground). After each of its tests the world must be as it was.</summary>
    [Theory]
    [MemberData(nameof(UrbanLowRiseTestNames))]
    public void UrbanLowRiseTestsPutTheWorldBack(string method)
    {
        using var outer = new IslandWorldScope();
        Empty();
        var test = new UrbanLowRiseTests(new Quiet());
        try { typeof(UrbanLowRiseTests).GetMethod(method)!.Invoke(test, null); }
        finally { if ((object)test is IDisposable d) d.Dispose(); }   // as xUnit does
        AssertEmpty();
    }

    /// <summary>ApproachSpawnTests' corridor check registers the landmarks (Landmarks.RegisterSolids) and cleared only
    /// the boxes: 44 solids and 4 decks stayed for every class after it.</summary>
    [Fact]
    public void ApproachSpawnCorridorCheckPutsTheWorldBack()
    {
        using var outer = new IslandWorldScope();
        Empty();
        var test = new ApproachSpawnTests(new Quiet());
        try { test.FinalApproachCorridorIsClearOfSolids(); }
        finally { if ((object)test is IDisposable d) d.Dispose(); }   // as xUnit does
        AssertEmpty();
    }


    private static void Empty()
    {
        WorldTerrain.Active = null;
        WorldSolids.Boxes.Clear(); WorldSolids.Shapes.Clear(); WorldDecks.All.Clear();
        WorldSolids.IndexEnabled = true;
    }

    private static void AssertEmpty()
    {
        Assert.Null(WorldTerrain.Active);
        Assert.Empty(WorldSolids.Boxes);
        Assert.Empty(WorldSolids.Shapes);
        Assert.Empty(WorldDecks.All);
        Assert.True(WorldSolids.IndexEnabled);
    }
}
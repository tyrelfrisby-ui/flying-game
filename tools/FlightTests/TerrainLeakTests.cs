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
/// is covered without touching this file.
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
            finally { if (test is IDisposable d) d.Dispose(); }   // as xUnit does

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
            if (test is IDisposable d) d.Dispose();
            Assert.Same(mine, WorldTerrain.Active);
        }
        finally { WorldTerrain.Active = before; }
    }
}

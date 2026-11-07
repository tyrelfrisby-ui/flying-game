using FlyingGame.Core;

namespace FlyingGame.FlightTests;

/// <summary>
/// Saves the process-wide world a test is about to replace and puts it back on <see cref="Dispose"/>: the terrain,
/// the solids and decks <c>IslandGeometry.Build()</c> registers, and the footprint-index switch. A test class that
/// builds the island holds one from its constructor and disposes it from its own Dispose, which xUnit calls after
/// every test, pass or fail.
///
/// Why it exists (B1 qualification, t971u, 2026-11-06): IosLowAltitudeProbe and SolidsBroadphaseTests built the
/// island's 1,481 solids and 128 decks and never removed them. Whatever ran next flew among them: the island combat
/// test that compares a wreck's step cost with a flying aeroplane's measured a ratio of 1.43-1.58 instead of 1.72-1.77
/// (the flying aeroplane's cost nearly doubled), against its 1.5 threshold, and failed or passed on timing alone.
/// Clearing only the solids after either class restored 1.72-1.77.
/// </summary>
internal sealed class IslandWorldScope : IDisposable
{
    private readonly WorldTerrain? _terrain = WorldTerrain.Active;
    private readonly WorldSolids.Box[] _boxes = WorldSolids.Boxes.ToArray();
    private readonly WorldSolids.IShape[] _shapes = WorldSolids.Shapes.ToArray();
    private readonly WorldDecks.IDeck[] _decks = WorldDecks.All.ToArray();
    private readonly bool _indexEnabled = WorldSolids.IndexEnabled;

    public void Dispose()
    {
        WorldTerrain.Active = _terrain;
        WorldSolids.Boxes.Clear(); WorldSolids.Boxes.AddRange(_boxes);
        WorldSolids.Shapes.Clear(); foreach (var s in _shapes) WorldSolids.Shapes.Add(s);
        WorldDecks.All.Clear(); foreach (var d in _decks) WorldDecks.All.Add(d);
        WorldSolids.IndexEnabled = _indexEnabled;
        WorldSolids.Version++;   // the broadphase caches on counts and Version: never let a restored list look unchanged
    }
}

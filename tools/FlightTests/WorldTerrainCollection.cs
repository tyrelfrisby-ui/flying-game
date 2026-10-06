using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Tests that switch the real terrain on (<c>WorldTerrain.Active</c>, a global) run alone, after the parallel ones —
/// a drone or formation flight running beside them otherwise saw the ground change under it mid-flight (2026-10-05: with
/// the 1,000 ft tableland the AI step-rate comparison diverged).</summary>
[CollectionDefinition("WorldTerrainActive", DisableParallelization = true)]
public sealed class WorldTerrainActiveCollection { }

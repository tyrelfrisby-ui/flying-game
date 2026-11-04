using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// StripFinish: one decision for what an island landing surface looks like and rolls like (Ty 2026-11-03: the Frank
/// Church strips were drawn as paved runways, centreline dashes and all). Runway paint is for pavement; dirt, turf,
/// grass, gravel and sand get none, and what the wheels feel is the surface that is drawn.
/// </summary>
[Collection("WorldTerrainActive")]
public class StripFinishTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public StripFinishTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = new WorldTerrain(); WorldTerrain.SurfaceOverride = null; }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }

    // surface string, finish, painted, rolls like: copied by hand, not read back from the code.
    public static readonly (string surface, StripFinish.Kind kind, bool painted, WorldTerrain.Surface rolls)[] Table =
    {
        ("asphalt", StripFinish.Kind.Paved, true, WorldTerrain.Surface.Paved),
        ("paved", StripFinish.Kind.Paved, true, WorldTerrain.Surface.Paved),
        ("gravel", StripFinish.Kind.Gravel, false, WorldTerrain.Surface.Gravel),
        ("dirt", StripFinish.Kind.Dirt, false, WorldTerrain.Surface.Gravel),
        ("turf", StripFinish.Kind.Turf, false, WorldTerrain.Surface.Grass),
        ("grass", StripFinish.Kind.Turf, false, WorldTerrain.Surface.Grass),
        ("hard sand", StripFinish.Kind.Sand, false, WorldTerrain.Surface.Gravel),
        ("water", StripFinish.Kind.Water, false, WorldTerrain.Surface.Gravel),
    };

    [Fact]
    public void OnlyPavementIsPainted()
    {
        foreach (var t in Table)
        {
            Assert.Equal(t.kind, StripFinish.Of(t.surface));
            Assert.Equal(t.painted, StripFinish.Painted(t.surface));
            Assert.Equal(t.rolls, StripFinish.Rolls(t.surface));
        }
    }

    [Fact]
    public void EveryIslandFieldIsPaintedOnlyIfPavedAndRollsLikeItIsDrawn()
    {
        int painted = 0, bare = 0;
        foreach (var f in IslandTerrain.Fields)
        {
            Assert.Equal(f.Surface == "asphalt", StripFinish.Painted(f.Surface));
            if (StripFinish.Painted(f.Surface)) painted++; else bare++;
            if (f.Surface == "water") continue;
            // The middle of the strip, unless another field's pad overlaps it there (crossing runways).
            var c = f.Centre;
            if (IslandTerrain.Fields.Any(g => g != f && g.Contains(c))) continue;
            Assert.Equal(StripFinish.Rolls(f.Surface), WorldTerrain.SurfaceAt(c.N * 1000, c.E * 1000));
        }
        _o.WriteLine($"{painted} paved fields painted, {bare} unpaved fields bare");
        Assert.True(painted > 0, "the paved airports still have their markings");
    }
}

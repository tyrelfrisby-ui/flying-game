using System;
using System.Linq;
using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// EVERY WATER A FLOATPLANE CAN LAND ON HAS A WAKE FIELD.
///
/// Ty 2026-11-11, twice: "The water wake and spray work seem to have not been [shown]", then "The water spray and
/// wake don't seem to have been done". They were done, and WaterRender's fixtures prove them frame by frame — which
/// is exactly how it hid. The fixture builds its own <see cref="WorldTerrain.Lake"/> out of the ISLAND's water lane
/// ("Editor fixtures can use the current island's water bounds without changing legacy lake tables") while
/// WakeWaves.Update searched <see cref="WorldTerrain.Lakes"/>, the LEGACY table, and needed a hit inside 1.3 radii.
/// PALH's 1,384 m seaplane lane is 9.64 radii from the nearest legacy lake and AP8's 600 m lane 5.08, so the height
/// field, the foam history, the Kelvin V and the bow shoulders had never once allocated anywhere he could land.
///
/// This is the pin that would have caught it: it asks the question the GAME asks.
/// </summary>
public class WakeFieldCoverageProbe
{
    private readonly ITestOutputHelper _out;
    public WakeFieldCoverageProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void EveryWaterLaneTheGameOffersHasAWakeField()
    {
        var water = WorldTerrain.WaterLanes;
        Assert.NotEmpty(water);
        foreach (var f in water)
        {
            // The lane centre, and a point a wingspan outside each end and each side — a floatplane does not stay
            // between the buoys, and the wake must not vanish when it taxis off the marked lane.
            foreach ((string where, double x, double y) in new[]
            {
                ("centre", f.Centre.X, f.Centre.Y),
                ("off the end", f.Centre.X + f.U.N * (f.Length / 2 + 60), f.Centre.Y + f.U.E * (f.Length / 2 + 60)),
                ("off the side", f.Centre.X + f.V.N * (f.Width / 2 + 60), f.Centre.Y + f.V.E * (f.Width / 2 + 60)),
            })
            {
                WorldTerrain.Lake? body = WorldTerrain.WakeBodyAt(x, y);
                _out.WriteLine($"  {f.Field,-8} {where,-12} ({x:0},{y:0}) -> " +
                               (body is null ? "NO WAKE FIELD" : $"field r={body.Value.Rx:0} at surface {body.Value.SurfaceM:0.0}"));
                Assert.True(body.HasValue, $"{f.Field} {where}: a floatplane can be here and there is no wake field");
                Assert.True(body!.Value.Rx >= 300.0, $"{f.Field} {where}: the wake patch is too small to be a bay");
            }
        }
        // And the legacy bodies still work, unchanged: the harbour and the first airport lake.
        foreach (var l in new[] { WorldTerrain.Harbor, WorldTerrain.Lakes[0] })
            Assert.True(WorldTerrain.WakeBodyAt(l.Cx, l.Cy).HasValue, "a legacy lake lost its wake field");
        // Dry land, well away from any of it, still gets nothing.
        Assert.Null(WorldTerrain.WakeBodyAt(WorldTerrain.Lakes[4].Cx, WorldTerrain.Lakes[4].Cy + 40000));
    }
}

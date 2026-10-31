using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

// INSTALLS a world into the global WorldTerrain.Active, so it must not run in parallel with anything that
// steps an aeroplane: LandingGear, AirframeContact and FloatHydro all read the ground and the water through
// that static, so a class holding a world here silently changes the ground under 29 other test classes.
[Collection("WorldTerrainActive")]
public sealed class IslandWaterProbe : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method
    /// does not run when the test throws, so a failing test used to leave its world installed in the global
    /// WorldTerrain.Active for the whole rest of the run — which silently changed the ground under every
    /// class that came after it, whether or not it was serialised.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _o;
    public IslandWaterProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void WhereDoesTheFloatplaneStartPutUs()
    {
        WorldTerrain.Active = new WorldTerrain();
        _o.WriteLine("airport   legacy lake centre      surface   island ground   water there?");
        for (int i = 0; i < WorldTerrain.Airports.Length; i++)
        {
            WorldTerrain.Lake lk = WorldTerrain.Lakes[i];
            double g = WorldTerrain.GroundHeightAt(lk.Cx, lk.Cy);
            double? w = FloatHydro.WaterSurfaceAt(lk.Cx, lk.Cy, lk.SurfaceM);
            _o.WriteLine($"{WorldTerrain.Airports[i].Name,-8} ({lk.Cx,8:F0},{lk.Cy,8:F0}) {lk.SurfaceM,8:F1} {g,13:F1} {(w.HasValue ? w.Value.ToString("F1") : "NO WATER"),14}   gap {lk.SurfaceM - g,8:F1}");
        }
        WorldTerrain.Lake h = WorldTerrain.Harbor;
        _o.WriteLine($"{"HARBOR",-8} ({h.Cx,8:F0},{h.Cy,8:F0}) {h.SurfaceM,8:F1} {WorldTerrain.GroundHeightAt(h.Cx, h.Cy),13:F1} {(FloatHydro.WaterSurfaceAt(h.Cx, h.Cy, h.SurfaceM)?.ToString("F1") ?? "NO WATER"),14}");

        _o.WriteLine("");
        _o.WriteLine("the island's OWN water lanes (fields with surface=water):");
        foreach (IslandField f in IslandSites.Fields)
            if (f.Surface == "water")
                _o.WriteLine($"  {f.Field,-8} {f.Id,-16} centre=({f.Centre.X,8:F0},{f.Centre.Y,8:F0}) hdg={f.Heading,5:F0} len={f.Length,6:F0} wid={f.Width,5:F0} elev={f.Elevation,7:F1} water={(FloatHydro.WaterSurfaceAt(f.Centre.X, f.Centre.Y, f.Elevation)?.ToString("F1") ?? "NO WATER")}");

        _o.WriteLine("");
        _o.WriteLine("the island's OWN water bodies (world[\"water\"]):");
        foreach (IslandValue q in IslandTerrain.World["water"].Items)
        {
            IslandPoint c = q["centre"].Point;
            _o.WriteLine($"  {q.S("id"),-14} centre=({c.X,8:F0},{c.Y,8:F0}) radii_km=[{q["radii_km"][0].Number():F2},{q["radii_km"][1].Number():F2}] elev_ft={q.N("elevation_ft"),7:F0} depth={q.N("depth_m"),5:F0} water={(FloatHydro.WaterSurfaceAt(c.X, c.Y, q.N("elevation_ft") * IslandTerrain.Feet)?.ToString("F1") ?? "NO WATER")}");
        }

        _o.WriteLine("");
        _o.WriteLine("what IslandRunways.Water picks instead:");
        foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            foreach ((string what, double min) in new[] { ("floatplane", 600.0), ("flying boat", 2200.0) })
            {
                WorldTerrain.Lake? lk = IslandRunways.Water(a, min);
                if (lk is null) { _o.WriteLine($"  {a.Name,-6} {what,-12} NOTHING FOUND"); continue; }
                double g = WorldTerrain.GroundHeightAt(lk.Value.Cx, lk.Value.Cy);
                double? w = FloatHydro.WaterSurfaceAt(lk.Value.Cx, lk.Value.Cy, lk.Value.SurfaceM);
                double km = Math.Sqrt((lk.Value.Cx - a.X) * (lk.Value.Cx - a.X) + (lk.Value.Cy - a.Y) * (lk.Value.Cy - a.Y)) / 1000;
                _o.WriteLine($"  {a.Name,-6} {what,-12} ({lk.Value.Cx,8:F0},{lk.Value.Cy,8:F0}) {km,5:F1} km away  surface {lk.Value.SurfaceM,7:F1}  bed {g,8:F1}  water {(w.HasValue ? w.Value.ToString("F1") : "NONE"),8}  {2 * lk.Value.Rx,6:F0} x {2 * lk.Value.Ry,5:F0} m");
            }
        WorldTerrain.Active = null;
    }
}

using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Not an assertion — the table I want in front of me when a start goes wrong on the device: where the
/// runway choice puts the aeroplane at every airport, against the ground that is actually there, and every runway
/// and authored start the world holds. <see cref="IslandStartTests"/> is what holds the line.</summary>
// INSTALLS a world into the global WorldTerrain.Active, so it must not run in parallel with anything that
// steps an aeroplane: LandingGear, AirframeContact and FloatHydro all read the ground and the water through
// that static, so a class holding a world here silently changes the ground under 29 other test classes.
[Collection("WorldTerrainActive")]
public sealed class IslandSpawnProbe : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method
    /// does not run when the test throws, so a failing test used to leave its world installed in the global
    /// WorldTerrain.Active for the whole rest of the run — which silently changed the ground under every
    /// class that came after it, whether or not it was serialised.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _out;
    public IslandSpawnProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void WhereDoesEveryStartActuallyPutUs()
    {
        WorldTerrain.Active = new WorldTerrain();
        _out.WriteLine("airport           ap.X       ap.Y    apElev  |  chosen start X     Y    hdg   groundThere   offPad");
        foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
        {
            WorldTerrain.RunwayEnd rw = WorldTerrain.ChooseRunway(a, 0, true, 1.0);
            (double sx, double sy) = rw.Start;
            double ground = WorldTerrain.GroundHeightAt(sx, sy);
            _out.WriteLine($"{a.Name,-12} {a.X,10:F0} {a.Y,10:F0} {a.ElevationM,8:F1}  | {sx,10:F0} {sy,10:F0} {rw.HeadingRad * 180 / System.Math.PI,5:F0} {ground,12:F1} {ground - a.ElevationM,10:F1}");
        }
        _out.WriteLine("");
        _out.WriteLine("the island's OWN landable fields:");
        foreach (IslandField f in IslandSites.Fields)
            _out.WriteLine($"  {f.Field,-8} {f.Id,-14} surface={f.Surface,-8} centre=({f.Centre.X,9:F0},{f.Centre.Y,9:F0}) hdg={f.Heading,5:F0} len={f.Length,7:F0} wid={f.Width,5:F0} elev={f.Elevation,8:F1} slope={f.Slope,5:F2} numbers={f.Numbers}");
        _out.WriteLine("");
        _out.WriteLine("the island's OWN starts:");
        foreach (IslandSites.Start s in IslandSites.Starts)
        {
            double ground = WorldTerrain.GroundHeightAt(s.X, s.Y);
            _out.WriteLine($"  {s.Id,-5} mode={s.Mode,-14} ap={s.Airport,-10} group={s.Group,-10} at=({s.X,9:F0},{s.Y,9:F0}) alt={s.AltitudeM,8:F1} ground={ground,8:F1} agl={s.AltitudeM - ground,8:F1} hdg={s.HeadingTrueDeg,5:F0}");
        }
        WorldTerrain.Active = null;
    }
}

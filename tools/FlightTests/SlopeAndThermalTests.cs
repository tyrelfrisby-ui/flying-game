using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Air over terrain: wind onto a wall rises up the windward face (strongest just in front of and above
/// the crest), sinks on the lee side, and a thermal is a rising core ringed by sink.</summary>
public class SlopeAndThermalTests
{
    private readonly ITestOutputHelper _out;
    public SlopeAndThermalTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void WindOntoTheFirstWallRisesOnTheFaceAndSinksInTheLee()
    {
        var t = new WorldTerrain();
        Vec3 wind = new(0, -8, 0);   // from the east (090), blowing west onto the first escarpment
        double edge = WorldTerrain.EdgeMeanY(0) + WorldTerrain.EdgeWander(0, 0);   // toe of the wall at x = 0
        double crestY = edge - WorldTerrain.EscarpmentWidthM;
        double Up(double y, double agl) => -SlopeLift.WindAt(t, new Vec3(0, y, -(t.HeightAt(0, y) + agl)), wind).Z;
        double face = Up(edge - WorldTerrain.EscarpmentWidthM * 0.5, 30), crest = Up(crestY, 40), lee = Up(crestY - 400, 40), valley = Up(edge + 2000, 40);
        _out.WriteLine($"up: mid-face {face:F1}  crest {crest:F1}  lee {lee:F1}  valley {valley:F1} m/s");
        Assert.True(face > 2.0, "lift on the windward face");
        // Blocking: the lift starts well out in front of the wall — 250 m upwind of the toe at 60 m AGL is already soarable.
        double ahead = Up(edge + 250, 60);
        _out.WriteLine($"up 250 m in front of the wall: {ahead:F1} m/s");
        Assert.True(ahead > 1.0, "lift band should reach ~300 m out in front of the wall");
        Assert.True(crest > 1.0, "lift carries over the crest");
        Assert.True(lee <= 0.05, "no lift (sink or nothing) in the lee");
        Assert.True(System.Math.Abs(valley) < 0.05, "flat valley: no slope lift");
        // Reverse the wind: the face becomes the lee (sink).
        double faceLee = -SlopeLift.WindAt(t, new Vec3(0, edge - WorldTerrain.EscarpmentWidthM * 0.5, -(t.HeightAt(0, edge - WorldTerrain.EscarpmentWidthM * 0.5) + 30)), new Vec3(0, 8, 0)).Z;
        Assert.True(faceLee < -1.0, $"wind off the plateau sinks down the face ({faceLee:F1})");
    }

    [Fact]
    public void ThermalHasRisingCoreAndSinkRing()
    {
        var th = new Thermal(new Vec3(0, 0, 0), 100, 4.0, 2000);
        double W(double r, double alt) => -th.WindAt(new Vec3(r, 0, -alt)).Z;
        // At 1000 m the column has widened to R = 140 m: edge ≈ R, sink ring ≈ 1.7 R, gone by ~4 R.
        double core = W(0, 1000), edge = W(140, 1000), ring = W(238, 1000), far = W(620, 1000);
        _out.WriteLine($"w: core {core:F2}  edge {edge:F2}  ring {ring:F2}  far {far:F2} m/s");
        Assert.InRange(core, 3.5, 4.0);
        Assert.True(ring < -1.0 && ring > -2.0, "sink ring about a third of the core, just outside it");
        Assert.True(System.Math.Abs(far) < 0.2, "gone a few radii out");
        Assert.True(W(0, 1500) < core, "weaker near the top");
    }

    [Fact]
    public void WindStrengthensWithHeightAndSoDoesTheRidgeLift()
    {
        var t = new WorldTerrain(); WorldTerrain.Active = t;
        Atmosphere.SteadyWind = new Vec3(0, -5, 0); Atmosphere.SlopeLiftEnabled = true; Atmosphere.Thermals.Clear(); Atmosphere.ActiveTurbulence = null;
        try
        {
            double f10 = Atmosphere.WindGradientFactor(new Vec3(0, 0, -10)), f900 = Atmosphere.WindGradientFactor(new Vec3(0, 0, -900));
            Assert.InRange(f10, 0.99, 1.01); Assert.InRange(f900, 1.7, 2.0);
            double edge = WorldTerrain.EdgeMeanY(0) + WorldTerrain.EdgeWander(0, 0);
            double Up(double y, double agl) => -Atmosphere.WindAtPosition(new Vec3(0, y, -(t.HeightAt(0, y) + agl))).Z;
            double low = Up(edge - WorldTerrain.EscarpmentWidthM * 0.15, 40), high = Up(edge - WorldTerrain.EscarpmentWidthM * 0.85, 40);
            _out.WriteLine($"lift low on the face {low:F1} m/s, high on the face {high:F1} m/s; band e-fold {SlopeLift.DecayHeightM} m");
            Assert.True(high > low, "lift must be stronger higher up the ridge");
            Assert.True(SlopeLift.DecayHeightM >= 500, "wider lift band");
        }
        finally { WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.SlopeLiftEnabled = false; }
    }
}

using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The shipped regional atmosphere must load against the shipped world (Ty 2026-11-08: "i have show lift on and no
/// lift is shown … live not replay").
///
/// What happened: the config's two airport circles still named the OLD four-airport world's fields, "Bench" and
/// "Mesa". The island world has none, so AtmosphereField's constructor threw on the first unresolvable anchor —
/// and the caller caught it, logged a warning no one on a phone can see, and flew uniform air instead. One stale
/// name in one region took the WHOLE field down: no thermals, no slope lift, no lee, no wave, anywhere in the
/// world. The lift bubbles draw columns where the air is rising, so they had nothing to draw, for about a week.
///
/// These two tests are the gate that was missing: every anchor must resolve, and the air the config describes must
/// actually move somewhere.
/// </summary>
[Collection("WorldTerrainActive")]
public class RegionalAtmosphereAnchorTests
{
    private static AtmosphereFieldConfig ShippedConfig() =>
        AtmosphereFieldConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "atmosphere", "regional-atmosphere.json"));

    [Fact]
    public void EveryAnchorInTheShippedConfigResolvesAgainstTheShippedWorld()
    {
        var terrain = new WorldTerrain();
        WorldTerrain.Active = terrain;
        try
        {
            // Constructing the field resolves every anchor; an unknown one throws ArgumentException.
            var field = new AtmosphereField(ShippedConfig());
            Assert.NotNull(field);
        }
        finally { WorldTerrain.Active = null; }
    }

    [Fact]
    public void TheShippedAirRisesSomewhereStronglyEnoughToBeWorthDrawing()
    {
        var terrain = new WorldTerrain();
        WorldTerrain.Active = terrain;
        try
        {
            var field = new AtmosphereField(ShippedConfig()) { ConvectionScale = 1.0 };
            // LiftBubbles.ThresholdMs: rising air slower than 0.25 m/s is not shown, so that is the bar.
            const double shown = 0.25;
            double bestConvective = 0, bestSlope = 0, bestVertical = 0;
            for (double x = -8000; x <= 8000; x += 500)
                for (double y = -12000; y <= 8000; y += 500)
                {
                    double ground = terrain.HeightAt(x, y);
                    var s = field.Sample(new Vec3(x, y, -(ground + 600)), 120.0);
                    bestConvective = System.Math.Max(bestConvective, s.ConvectiveMs);
                    bestSlope = System.Math.Max(bestSlope, s.SlopeLiftMs);
                    bestVertical = System.Math.Max(bestVertical, -s.MeanWind.Z);
                }
            Assert.True(bestConvective >= shown, $"no thermal anywhere reached {shown} m/s (best {bestConvective:F2})");
            Assert.True(bestSlope >= shown, $"no slope lift anywhere reached {shown} m/s (best {bestSlope:F2})");
            Assert.True(bestVertical >= shown, $"the air never rises at {shown} m/s anywhere (best {bestVertical:F2})");
        }
        finally { WorldTerrain.Active = null; }
    }
}

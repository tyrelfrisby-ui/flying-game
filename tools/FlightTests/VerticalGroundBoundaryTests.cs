using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// No through-flow at the ground (Ty 2026-11-08, on final at 3U2 in a J-3: the lift markers "begin mid field … they
/// were so strong that my j3 rose several hundred feet from nearly touching down. Air cannot come out of the earth
/// so a real thermal or ridge etc has to come from air moving along the surface then up not straight up from the
/// surface").
///
/// He is right, and the field was breaking it in two places. The background vertical wind had no height dependence
/// at all — a constant updraught applied at every altitude including ground level — and the convective updraught
/// used Lenschow's mixed-layer profile all the way down, whose cube root still carries more than half the peak
/// 10 m above the grass. So an aeroplane in the flare sat in air rising almost as fast as it would 1000 ft up.
///
/// Every vertical component is now faded to nothing at the surface across <see cref="AtmosphereFieldConfig.SurfaceLayerM"/>.
/// </summary>
[Collection("WorldTerrainActive")]
public class VerticalGroundBoundaryTests
{
    private static AtmosphereFieldConfig ShippedConfig() =>
        AtmosphereFieldConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "atmosphere", "regional-atmosphere.json"));

    /// <summary>Vertical air speed (m/s, up positive) at a height above the ground under the given point.</summary>
    private static double RisingAt(AtmosphereField f, double x, double y, double agl)
    {
        double ground = WorldTerrain.GroundHeightAt(x, y);
        var pos = new Vec3(x, y, -(ground + agl));
        return -f.Sample(pos, 0.0).MeanWind.Z;
    }

    private static void InTheWorld(Action<AtmosphereField> body)
    {
        var terrain = new WorldTerrain();
        WorldTerrain.Active = terrain;
        try { body(new AtmosphereField(ShippedConfig())); }
        finally { WorldTerrain.Active = null; }
    }

    // ---- the shape of the taper itself -----------------------------------------------------------

    [Fact]
    public void AtTheSurfaceNothingIsMoving()
    {
        Assert.Equal(0.0, AtmosphereField.VerticalGroundTaper(0.0, 50.0));
    }

    [Fact]
    public void AboveTheSurfaceLayerTheWindIsItsFullSelf()
    {
        Assert.Equal(1.0, AtmosphereField.VerticalGroundTaper(50.0, 50.0));
        Assert.Equal(1.0, AtmosphereField.VerticalGroundTaper(500.0, 50.0));
    }

    [Fact]
    public void AFlareHeightKeepsAlmostNoneOfItAndASoaringHeightKeepsMost()
    {
        Assert.InRange(AtmosphereField.VerticalGroundTaper(10.0, 50.0), 0.0, 0.10);   // in the flare: gone
        Assert.InRange(AtmosphereField.VerticalGroundTaper(30.0, 50.0), 0.60, 0.80);  // ridge soaring: still there
    }

    [Fact]
    public void ItOnlyEverGrowsWithHeight()
    {
        double previous = -1.0;
        for (double agl = 0; agl <= 80; agl += 2.0)
        {
            double now = AtmosphereField.VerticalGroundTaper(agl, 50.0);
            Assert.True(now >= previous, $"the taper went backwards at {agl} m");
            previous = now;
        }
    }

    // ---- and what it means in the world the game ships ------------------------------------------

    [Fact]
    public void NowhereInTheWORLDDoesAirComeOutOfTheGround()
    {
        // The whole island, at the surface: nothing vertical, up or down, anywhere.
        InTheWorld(f =>
        {
            double worst = 0.0, worstX = 0, worstY = 0;
            for (double x = -8000; x <= 8000; x += 500)
                for (double y = -12000; y <= 8000; y += 500)
                {
                    double w = Math.Abs(RisingAt(f, x, y, 0.0));
                    if (w > worst) { worst = w; worstX = x; worstY = y; }
                }
            Assert.True(worst < 0.01, $"air was moving {worst:F3} m/s vertically AT the ground near ({worstX:F0}, {worstY:F0})");
        });
    }

    [Fact]
    public void AnAEROPLANEInTheFlareIsNotLiftedByAThermal()
    {
        // The report itself: on final, a few metres up, there must be nothing strong enough even to draw a marker.
        InTheWorld(f =>
        {
            double worst = 0.0;
            for (double x = -8000; x <= 8000; x += 500)
                for (double y = -12000; y <= 8000; y += 500)
                    worst = Math.Max(worst, Math.Abs(RisingAt(f, x, y, 3.0)));
            Assert.True(worst < LiftBubblesThresholdMs,
                $"{worst:F3} m/s of vertical air 3 m above the ground — a J-3 in the flare would feel that");
        });
    }

    [Fact]
    public void TheAirStillRisesWhereAGliderWouldLookForIt()
    {
        // The taper must not have switched the thermals off again: well clear of the ground, the lift is still there.
        InTheWorld(f =>
        {
            double best = 0.0;
            for (double x = -8000; x <= 8000; x += 500)
                for (double y = -12000; y <= 8000; y += 500)
                    best = Math.Max(best, RisingAt(f, x, y, 600.0));
            Assert.True(best >= LiftBubblesThresholdMs,
                $"the strongest lift in the world at 600 m was {best:F3} m/s — worth drawing is {LiftBubblesThresholdMs}");
        });
    }

    [Fact]
    public void ClimbingAwayFromTheGroundTheLiftComesIn()
    {
        // Pick the best column in the world at soaring height, then walk up it from the grass: the lift must arrive
        // gradually with height rather than be there from the first metre.
        InTheWorld(f =>
        {
            double bestX = 0, bestY = 0, best = 0;
            for (double x = -8000; x <= 8000; x += 500)
                for (double y = -12000; y <= 8000; y += 500)
                {
                    double w = RisingAt(f, x, y, 600.0);
                    if (w > best) { best = w; bestX = x; bestY = y; }
                }
            Assert.True(best > 0, "no lift anywhere to walk up");
            double atGround = RisingAt(f, bestX, bestY, 0.0);
            double at10 = RisingAt(f, bestX, bestY, 10.0);
            double at50 = RisingAt(f, bestX, bestY, 50.0);
            Assert.True(Math.Abs(atGround) < 0.01, $"{atGround:F3} m/s at the surface of the best column");
            Assert.True(Math.Abs(at10) < Math.Abs(at50), "the lift must build with height through the surface layer");
        });
    }

    // ---- the wind the game actually flies, not just the field's own --------------------------------

    /// <summary>
    /// The miss, written down. The first fix faded the FIELD's own slope and convective lift and nothing else — but
    /// Atmosphere.WindFromSample adds the AUTHORED thermal columns straight on top of it (SoaringScenery: 10 to 14 m/s
    /// cores, one set per plateau), and their profile is a fraction of ABSOLUTE altitude, so over a plateau nearly
    /// 5000 ft up a column is already half developed at the wheels.
    ///
    /// Ty 2026-11-08, after flying the first fix: "I flew 3u2 again and the bubbles were still coming out of the
    /// ground and rising straight up at the surface my cub was climbing at over 300 fpm at idle."
    ///
    /// So this one goes through Atmosphere, where every contribution is finally added up, which is where the condition
    /// now lives. A test of the field alone could not have caught it, and did not.
    /// </summary>
    [Fact]
    public void AnAuthoredThermalCannotBlowOutOfAPlateauAirfield()
    {
        var terrain = new WorldTerrain();
        WorldTerrain.Active = terrain;
        AtmosphereField? keepField = Atmosphere.Field;
        try
        {
            Atmosphere.Field = new AtmosphereField(ShippedConfig());
            Atmosphere.Thermals.Clear();
            // The strong authored column, over the highest plateau airfield, exactly as SoaringScenery builds it.
            WorldTerrain.Airport field = WorldTerrain.Airports[0];
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports) if (a.ElevationM > field.ElevationM) field = a;
            double ground = WorldTerrain.GroundHeightAt(field.X, field.Y);
            Atmosphere.Thermals.Add(new Thermal(new Vec3(field.X, field.Y, -ground), 300.0, 13.0, ground + 2000.0));

            double atTheWheels = -Atmosphere.MeanWindAtPosition(new Vec3(field.X, field.Y, -(ground + 1.0))).Z;
            double inTheFlare = -Atmosphere.MeanWindAtPosition(new Vec3(field.X, field.Y, -(ground + 3.0))).Z;
            double soaring = -Atmosphere.MeanWindAtPosition(new Vec3(field.X, field.Y, -(ground + 600.0))).Z;

            Assert.True(System.Math.Abs(atTheWheels) < 0.02, $"{atTheWheels:F2} m/s coming out of the runway at {field.Name}");
            Assert.True(inTheFlare < LiftBubblesThresholdMs,
                $"{inTheFlare:F2} m/s of lift 3 m over {field.Name} — a J-3 in the flare would be carried off");
            Assert.True(soaring > 1.0, $"and the column must still be a thermal where a glider would use it: {soaring:F2} m/s");
        }
        finally
        {
            Atmosphere.Thermals.Clear();
            Atmosphere.Field = keepField;
            WorldTerrain.Active = null;
        }
    }

    private const double LiftBubblesThresholdMs = 0.25;   // LiftBubbles.ThresholdMs: below this nothing is drawn
}

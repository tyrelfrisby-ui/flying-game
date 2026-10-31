using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Owner 2026-09-10: choose the headwind (into-wind 09/27) or crosswind (main) runway at launch.</summary>
[Collection("WorldTerrainActive")]
public class RunwayChoiceTests
{
    private static AircraftConfig Cub() => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    /// <summary>The four-airport world's template: the same two crossing runways at every field. No world installed
    /// (the widget, and the flat-ground tests) still gets this, through ChooseTemplateRunway — so it is asserted with
    /// Active explicitly null rather than left to whichever test in the collection ran last.</summary>
    [Fact]
    public void EastWindHeadwindIsRunway09AndCrosswindIsTheMain()
    {
        WorldTerrain.Active = null;
        try
        {
            var a = WorldTerrain.Airports[0];
            double from090 = 90 * System.Math.PI / 180;
            WorldTerrain.RunwayEnd hw = WorldTerrain.ChooseRunway(a, from090, headwind: true);
            WorldTerrain.RunwayEnd xw = WorldTerrain.ChooseRunway(a, from090, headwind: false);
            Assert.Equal("paved-xwind", hw.Strip.Kind);
            Assert.Equal(90.0, hw.HeadingRad * 180 / System.Math.PI, 3);        // facing east, into the wind
            Assert.Equal("paved", xw.Strip.Kind);
            Assert.True(xw.HeadwindFactor(from090) > -1e-6);
            // Start point sits in from the west threshold of the 09/27, on its centreline.
            (double sx, double sy) = hw.Start;
            Assert.Equal(a.X + WorldTerrain.XwindRunwayDx, sx, 3);
            Assert.Equal(a.Y - hw.LengthM / 2 + WorldTerrain.ThresholdSetbackM, sy, 3);
            // North wind: the main is the headwind runway, heading north; crosswind = the 09/27.
            WorldTerrain.RunwayEnd n = WorldTerrain.ChooseRunway(a, 0, headwind: true);
            Assert.Equal("paved", n.Strip.Kind); Assert.Equal(0.0, n.HeadingRad, 6);
            Assert.Equal("paved-xwind", WorldTerrain.ChooseRunway(a, 0, headwind: false).Strip.Kind);
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>With the island installed the choice comes from the island's own runways instead (IslandRunways), so
    /// an arbitrary true heading is handled and the crosswind pick is a runway on a different axis. KMEV has two
    /// (17/35 and 13/31, 45 degrees apart); KSNA's 2L and 2R are parallel and are not a crosswind choice.</summary>
    [Fact]
    public void OnTheIslandTheCrosswindRunwayIsADifferentAxisNotTheOtherEnd()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            WorldTerrain.Airport kmev = System.Array.Find(WorldTerrain.Airports, x => x.Name == "KMEV");
            double from180 = System.Math.PI;                                    // wind from the south
            WorldTerrain.RunwayEnd hw = WorldTerrain.ChooseRunway(kmev, from180, headwind: true, windSpeedMs: 5);
            WorldTerrain.RunwayEnd xw = WorldTerrain.ChooseRunway(kmev, from180, headwind: false, windSpeedMs: 5);
            Assert.Equal(180.0, (hw.HeadingRad * 180 / System.Math.PI % 360 + 360) % 360, 1);   // 17, into a south wind
            double axis = (xw.HeadingRad * 180 / System.Math.PI % 180 + 180) % 180;
            Assert.InRange(axis, 134.0, 136.0);                                 // 13/31, not the other end of 17/35
            Assert.NotEqual(hw.Strip.Kind, xw.Strip.Kind);                      // the numbers ride in the kind

            // KSNA's two runways are parallel, so there is no crosswind runway: the choice is the other end.
            WorldTerrain.Airport ksna = System.Array.Find(WorldTerrain.Airports, x => x.Name == "KSNA");
            WorldTerrain.RunwayEnd k1 = WorldTerrain.ChooseRunway(ksna, from180, headwind: true, windSpeedMs: 5);
            WorldTerrain.RunwayEnd k2 = WorldTerrain.ChooseRunway(ksna, from180, headwind: false, windSpeedMs: 5);
            Assert.True(System.Math.Abs(k1.HeadwindFactor(from180) - k2.HeadwindFactor(from180)) > 1.0,
                "the only other choice at KSNA is the reciprocal, which is a tailwind");
        }
        finally { WorldTerrain.Active = null; }
    }

    [Fact]
    public void OnFinalForTheIntoWindRunwayHeadsEastToItsThreshold()
    {
        var c = Cub();
        var a = WorldTerrain.Airports[0];
        WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        try
        {
            WorldTerrain.RunwayEnd hw = WorldTerrain.ChooseRunway(a, 90 * System.Math.PI / 180, headwind: true);
            var (state, g, _) = ApproachSpawn.Compute(c, a, hw);
            Quat q = state.Attitude;
            double psi = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
            Assert.Equal(90.0, psi * 180 / System.Math.PI, 1);
            (double tx, double ty) = hw.Threshold;
            Assert.Equal(tx, state.Position.X, 1);                           // on the extended centreline (x = runway x)
            Assert.True(state.Position.Y < ty, "west of the threshold, flying east toward it");
            Assert.InRange(-state.Position.Z, a.ElevationM + 90, a.ElevationM + 93);
        }
        finally { Atmosphere.SteadyWind = Vec3.Zero; }
    }

    [Fact]
    public void TugPilotRunwayFrameRotatesPositionAndHeading()
    {
        // A tug sitting 100 m down the 09/27 from its start, heading east, is at local (100, 0) heading 0.
        var pilot = new TugPilot { OriginX = 400, OriginY = -500, RunwayHeadingRad = System.Math.PI / 2 };
        var q = new Quat(0, 0, System.Math.Sin(System.Math.PI / 4), System.Math.Cos(System.Math.PI / 4));   // yaw 90°
        var world = new RigidBodyState(new Vec3(400, -400, -1), q, new Vec3(20, 0, 0), Vec3.Zero);
        var local = pilot.LocalizeForTest(world);
        Assert.Equal(100.0, local.Position.X, 3);
        Assert.Equal(0.0, local.Position.Y, 3);
        Quat lq = local.Attitude;
        double psi = System.Math.Atan2(2 * (lq.W * lq.Z + lq.X * lq.Y), 1 - 2 * (lq.Y * lq.Y + lq.Z * lq.Z));
        Assert.Equal(0.0, psi, 3);
    }

    [Fact]
    public void FloatplaneOnFinalIsAimedAtTheLake()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-floats-like.json"));
        WorldTerrain.Active = null; Atmosphere.SteadyWind = new Vec3(3, 0, 0);   // wind blowing north
        var lake = WorldTerrain.Lakes[0];
        var (st, glide, aimX) = ApproachSpawn.ComputeToLake(c, lake);
        Assert.InRange(-st.Position.Z - lake.SurfaceM, 90, 93);                     // 300 ft over the water
        Assert.True(lake.Inside(aimX, lake.Cy) < 1, "aim point is on the lake");
        // Heading south into the northerly wind, so the touchdown zone lies on the lake's near half.
        var q = st.Attitude; double hdg = Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        Assert.InRange(Math.Abs(hdg), 3.0, 3.3);
        Assert.True(lake.Inside(aimX, lake.Cy) < 0.6);
        Atmosphere.SteadyWind = Vec3.Zero;
    }
}

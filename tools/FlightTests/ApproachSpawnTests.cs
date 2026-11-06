using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>"On final" start: 300 ft on the centreline at best glide, idle, trimmed — hands off it should ride
/// its own glide path down to the aim point.</summary>
[Collection("WorldTerrainActive")]
public class ApproachSpawnTests : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method does
    /// not run when the test throws, so a failing test left its world installed for the whole rest of the run.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _out;
    public ApproachSpawnTests(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData("glider-2-33-like", 19, 27, 15, 30)]
    [InlineData("pa18-cub-like", 24, 34, 6, 13)]
    [InlineData("c172-like", 28, 40, 7, 14)]
    [InlineData("extra-300-like", 35, 55, 5, 12)]   // 5.7:1 with the full windmilling-prop drag (owner 2026-10-05: no type overrides it lower)
    [InlineData("glider-eb29r-like", 22, 34, 40, 70)]
    [InlineData("glider-swift-s1-like", 22, 34, 20, 38)]
    public void BestGlideIsPlausible(string id, double vMin, double vMax, double ldMin, double ldMax)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var g = ApproachSpawn.FindBestGlide(c, 100);
        _out.WriteLine($"{id}: best glide {g.SpeedMs:F1} m/s ({g.SpeedMs * 1.944:F0} kt), L/D {g.GlideRatio:F1}, γ {g.GammaRad * 57.3:F1}°, α {g.AlphaRad * 57.3:F1}°");
        Assert.InRange(g.SpeedMs, vMin, vMax);
        Assert.InRange(g.GlideRatio, ldMin, ldMax);
    }

    [Theory]
    [InlineData("glider-2-33-like")]
    [InlineData("pa18-cub-like")]
    public void HandsOffFromFinalRidesTheGlidePathToTheAimPoint(string id)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var t = new WorldTerrain(); WorldTerrain.Active = t; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.SlopeLiftEnabled = false; Atmosphere.Thermals.Clear();
        var a = WorldTerrain.Airports[0];
        var (state, g, aimX) = ApproachSpawn.Compute(c, a);
        var ac = new Aircraft(c, state, new ControlDeflections(0, g.ElevatorRad, 0, g.SpoilerFraction));
        var sim = new SimLoop(ac);
        double stick = Aircraft.StickForDeflection(g.ElevatorRad, c.Controls.Elevator);
        double idle = c.Propulsion != null ? 1.0 : g.SpoilerFraction;   // lever +1 = idle power; glider: lever = spoiler fraction (half out on final)
        if (c.Propulsion == null) { Assert.Equal(0.5, g.SpoilerFraction); Assert.True(g.GammaRad > 5 * System.Math.PI / 180, $"half spoiler must steepen the 2-33's final (γ {g.GammaRad * 57.3:F1}°)"); }
        _out.WriteLine($"{id}: approach {g.SpeedMs * 1.944:F0} kt, γ {g.GammaRad * 57.3:F1}°, spoiler {g.SpoilerFraction:P0}, start {-(state.Position.X - aimX):F0} m before the aim point");
        double agl0 = -state.Position.Z - a.ElevationM, x0 = state.Position.X;
        Assert.InRange(agl0, 91, 92);
        double maxDev = 0;
        for (double tt = 0; tt < 120; tt += 0.05)
        {
            // Pilot keeps the wings level (the 2-33's spiral mode wanders off hands-off over a 2 km final); pitch is
            // left on the trim — that is what proves the trimmed best-glide state.
            var q = ac.State.Attitude;
            double roll = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
            double ail = System.Math.Clamp(-roll * 1.5 - ac.State.Rates.X * 0.4, -0.5, 0.5);
            sim.RunFor(0.05, new ControlInputs(ail, stick, 0, idle));
            var s = ac.State;
            double agl = -s.Position.Z - a.ElevationM;
            double expected = agl0 - (s.Position.X - x0) * System.Math.Tan(g.GammaRad);
            maxDev = System.Math.Max(maxDev, System.Math.Abs(agl - expected));
            if (agl < 3) { _out.WriteLine($"{id}: touched at x = {s.Position.X - aimX:+0;-0} m from the aim point, max path deviation {maxDev:F1} m"); Assert.InRange(s.Position.X - aimX, -250, 250); Assert.True(maxDev < 25, $"path deviation {maxDev:F1} m"); WorldTerrain.Active = null; return; }
        }
        WorldTerrain.Active = null;
        Assert.Fail("never reached the runway");
    }

    [Fact]
    public void FinalApproachCorridorIsClearOfSolids()
    {
        var t = new WorldTerrain(); WorldTerrain.Active = t;
        Landmarks.RegisterSolids(t);
        var a = WorldTerrain.Airports[0];
        double thr = a.X + WorldTerrain.MainRunwayDx - WorldTerrain.RunwayLengthM / 2;
        // From 3 km out to the threshold, ±300 m either side, up to 400 ft: no building, leg or shelf.
        foreach (var b in WorldSolids.Boxes)
        {
            bool inX = b.Cx + b.Hx > thr - 3000 && b.Cx - b.Hx < thr + 100;
            bool inY = b.Cy + b.Hy > a.Y - 300 && b.Cy - b.Hy < a.Y + 300;
            Assert.False(inX && inY && b.Bottom < a.ElevationM + 122, $"solid at ({b.Cx:F0},{b.Cy:F0}) top {b.Top:F0} m sits in the final approach corridor");
        }
        WorldTerrain.Active = null; WorldSolids.Boxes.Clear();
    }

    // ---- Crab into the crosswind on final (Ty 2026-11-05 Q129) ----

    private static (RigidBodyState S, ApproachSpawn.BestGlide G, ApproachSpawn.Crab C, Vec3 Ground, Vec3 Wind) FinalIn(string id, Vec3 wind, double headingDeg = 0)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        Vec3 saved = Atmosphere.SteadyWind; var savedTurb = Atmosphere.ActiveTurbulence; var savedField = Atmosphere.Field;
        try
        {
            Atmosphere.SteadyWind = wind; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
            var (s, g, _, crab) = ApproachSpawn.ComputeWithCrab(c, 1000, 2000, 50, headingDeg * System.Math.PI / 180);
            Vec3 ground = s.Attitude.Rotate(s.Velocity);
            Vec3 w = Atmosphere.WindAtPosition(s.Position);   // the steady wind with the gradient at 300 ft
            Assert.Equal(g.SpeedMs, (ground - w).Length, 6);   // airspeed preserved whatever the wind
            return (s, g, crab, ground, w);
        }
        finally { Atmosphere.SteadyWind = saved; Atmosphere.ActiveTurbulence = savedTurb; Atmosphere.Field = savedField; }
    }

    private static double CrossRight(Vec3 v, double headingDeg) { double h = headingDeg * System.Math.PI / 180; return -v.X * System.Math.Sin(h) + v.Y * System.Math.Cos(h); }
    private static double Along(Vec3 v, double headingDeg) { double h = headingDeg * System.Math.PI / 180; return v.X * System.Math.Cos(h) + v.Y * System.Math.Sin(h); }
    private static double YawDeg(Quat q) => System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 180 / System.Math.PI;

    [Theory]
    [InlineData("pa18-cub-like", 0)]
    [InlineData("c172-like", 90)]
    [InlineData("glider-2-33-like", 230)]
    public void CalmFinalIsUnchangedNoseAndTrackDownTheRunway(string id, double hdg)
    {
        var (s, g, crab, ground, _) = FinalIn(id, Vec3.Zero, hdg);
        Assert.Equal(0, crab.CrabRad, 12);
        Assert.True(crab.Attainable);
        double yaw = YawDeg(s.Attitude); Assert.Equal(0, System.Math.IEEERemainder(yaw - hdg, 360), 6);
        Assert.Equal(0, CrossRight(ground, hdg), 6);
        Assert.Equal(0, s.Velocity.Y, 9);   // no sideslip
        // Same body velocity as the old nose-on-centreline calm start.
        Assert.Equal(g.SpeedMs * System.Math.Cos(g.AlphaRad), s.Velocity.X, 9);
        Assert.Equal(g.SpeedMs * System.Math.Sin(g.AlphaRad), s.Velocity.Z, 9);
    }

    [Theory]
    [InlineData("pa18-cub-like", 0, 0, 6, +1)]     // wind blowing toward the east (from the LEFT on a north final): nose left
    [InlineData("pa18-cub-like", 0, 0, -6, -1)]    // from the RIGHT: nose right
    [InlineData("c172-like", 90, 5, 0, -1)]        // heading east, wind blowing north drifts it LEFT: nose right
    [InlineData("glider-2-33-like", 0, -4, 5, +1)] // headwind plus a left crosswind
    [InlineData("glider-2-33-like", 0, 4, -5, -1)] // tailwind plus a right crosswind
    public void CrosswindFinalTracksTheCentrelineCrabbedIntoWind(string id, double hdg, double wn, double we, int driftSign)
    {
        var (s, g, crab, ground, wind) = FinalIn(id, new Vec3(wn, we, 0), hdg);
        double wCross = CrossRight(wind, hdg);
        Assert.Equal(driftSign, System.Math.Sign(wCross));
        Assert.True(crab.Attainable);
        Assert.Equal(0, CrossRight(ground, hdg), 6);   // ground track ON the centreline
        Assert.True(Along(ground, hdg) > 0);
        Assert.Equal(0, crab.ResidualCrossMs, 9);
        // Nose into the wind: a wind drifting the aeroplane right (+) wants the nose LEFT (crab < 0).
        Assert.Equal(-driftSign, System.Math.Sign(crab.CrabRad));
        double yaw = YawDeg(s.Attitude);
        Assert.Equal(crab.CrabRad * 180 / System.Math.PI, System.Math.IEEERemainder(yaw - hdg, 360), 6);
        // Balanced: the AIR-relative velocity has no side component in the body (no frame-zero sideslip),
        // and pitch is the calm-air glide's.
        Vec3 airBody = s.Velocity - s.Attitude.Conjugate().Rotate(wind);
        Assert.Equal(0, airBody.Y, 9);
        Assert.Equal(System.Math.Atan2(airBody.Z, airBody.X), g.AlphaRad, 9);
        double pitch = System.Math.Asin(System.Math.Clamp(2 * (s.Attitude.W * s.Attitude.Y - s.Attitude.Z * s.Attitude.X), -1, 1));
        Assert.Equal(g.ThetaRad, pitch, 9);
    }

    [Theory]
    [InlineData(-8, 0)]   // headwind (blowing south on a north final)
    [InlineData(8, 0)]    // tailwind
    public void HeadAndTailwindNeedNoCrabAndChangeOnlyGroundSpeed(double wn, double we)
    {
        var (_, g, crab, ground, _) = FinalIn("pa18-cub-like", new Vec3(wn, we, 0));
        Assert.Equal(0, crab.CrabRad, 12);
        Assert.Equal(0, ground.Y, 9);
        Assert.Equal(crab.AlongGroundMs, ground.X, 6);
        Assert.True(wn < 0 ? ground.X < g.SpeedMs : ground.X > g.SpeedMs * 0.99);
    }

    [Theory]
    [InlineData(0, 25)]    // a 25 m/s crosswind on a ~28 m/s Cub: past the 45° cap
    [InlineData(0, -60)]   // faster across than the aeroplane flies: no heading holds the line at all
    [InlineData(-40, 40)]  // gale from the north-east quarter
    public void UnattainableCrosswindClampsTheCrabAndReportsTheDrift(double wn, double we)
    {
        var (s, _, crab, ground, wind) = FinalIn("pa18-cub-like", new Vec3(wn, we, 0));
        Assert.False(crab.Attainable);
        Assert.Equal(ApproachSpawn.MaxCrabRad, System.Math.Abs(crab.CrabRad), 12);
        Assert.Equal(-System.Math.Sign(we), System.Math.Sign(crab.CrabRad));
        foreach (double v in new[] { s.Velocity.X, s.Velocity.Y, s.Velocity.Z, s.Attitude.W, s.Attitude.Z, crab.ResidualCrossMs, crab.AlongGroundMs })
            Assert.False(double.IsNaN(v) || double.IsInfinity(v));
        Assert.Equal(crab.ResidualCrossMs, ground.Y, 6);   // the drift is reported, not hidden
        Assert.Equal(System.Math.Sign(we), System.Math.Sign(crab.ResidualCrossMs));   // still crabbed INTO it: drift less than the wind's
        Assert.True(System.Math.Abs(crab.ResidualCrossMs) < System.Math.Abs(wind.Y));
    }

    [Fact]
    public void CrabForWithNoAirspeedIsFinite()
    {
        var c = ApproachSpawn.CrabFor(0, new Vec3(0, 5, 0), 0);
        Assert.Equal(0, c.CrabRad); Assert.Equal(5, c.ResidualCrossMs); Assert.False(c.Attainable);
        var n = ApproachSpawn.CrabFor(0, new Vec3(0, 5, 0), double.NaN);
        Assert.False(double.IsNaN(n.CrabRad));
    }

    [Fact]
    public void GustyWindCrabsAgainstTheSameSampleTheVelocityUses()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        Vec3 saved = Atmosphere.SteadyWind; var savedTurb = Atmosphere.ActiveTurbulence; var savedField = Atmosphere.Field; double savedT = Atmosphere.SimTimeSec;
        try
        {
            Atmosphere.Field = null; Atmosphere.SteadyWind = new Vec3(0, 7, 0);
            Atmosphere.ActiveTurbulence = new Turbulence(2.5, 150, 7);
            Atmosphere.SimTimeSec = 123.4;
            var (s, _, _, crab) = ApproachSpawn.ComputeWithCrab(c, 0, 0, 0, 0);
            Vec3 ground = s.Attitude.Rotate(s.Velocity);
            Assert.Equal(0, ground.Y, 6);
            Assert.True(crab.Attainable);
            Assert.True(crab.CrabRad < 0);
        }
        finally { Atmosphere.SteadyWind = saved; Atmosphere.ActiveTurbulence = savedTurb; Atmosphere.Field = savedField; Atmosphere.SimTimeSec = savedT; }
    }

    /// <summary>Flown, not only initialised: the old start (nose on the centreline, wind added) was blown off the line
    /// from frame zero. Wings held level, rudder free, over flat ground with a 6 m/s crosswind from the right.</summary>
    [Theory]
    [InlineData("pa18-cub-like")]
    [InlineData("glider-2-33-like")]
    public void CrabbedFinalStaysOnTheCentrelineHandsOff(string id)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        Vec3 saved = Atmosphere.SteadyWind; var savedTurb = Atmosphere.ActiveTurbulence; var savedField = Atmosphere.Field;
        try
        {
            WorldTerrain.Active = null; Atmosphere.Field = null; Atmosphere.ActiveTurbulence = null;
            Atmosphere.SteadyWind = new Vec3(0, -6, 0);
            Atmosphere.SlopeLiftEnabled = false; Atmosphere.Thermals.Clear();
            var (state, g, _, crab) = ApproachSpawn.ComputeWithCrab(c, 0, 0, 0, 0);
            var ac = new Aircraft(c, state, new ControlDeflections(0, g.ElevatorRad, 0, g.SpoilerFraction));
            var sim = new SimLoop(ac);
            double stick = Aircraft.StickForDeflection(g.ElevatorRad, c.Controls.Elevator);
            double lever = c.Propulsion != null ? 1.0 : g.SpoilerFraction;
            double y0 = state.Position.Y, maxOff = 0;
            for (double tt = 0; tt < 10; tt += 0.05)
            {
                var q = ac.State.Attitude;
                double roll = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
                double ail = System.Math.Clamp(-roll * 1.5 - ac.State.Rates.X * 0.4, -0.5, 0.5);
                sim.RunFor(0.05, new ControlInputs(ail, stick, 0, lever));
                maxOff = System.Math.Max(maxOff, System.Math.Abs(ac.State.Position.Y - y0));
            }
            _out.WriteLine($"{id}: crab {crab.CrabRad * 57.3:+0.0;-0.0}°, max {maxOff:F1} m off the centreline in 10 s (uncrabbed would drift ~{6 * 10 * Atmosphere.WindGradientFactor(state.Position):F0} m)");
            Assert.True(crab.CrabRad > 0, "wind from the right: nose right");
            Assert.True(maxOff < 15, $"{maxOff:F1} m off the centreline in 10 s");
        }
        finally { Atmosphere.SteadyWind = saved; Atmosphere.ActiveTurbulence = savedTurb; Atmosphere.Field = savedField; WorldTerrain.Active = null; }
    }
}

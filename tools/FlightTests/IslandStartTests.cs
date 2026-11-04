using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The island's starts, and the defect they had: the four-airport world's runway template — one 2,400 m strip
/// along +x at every field — was still being applied to the island's airport centres. At PHDH that put the
/// start 1,570 m south of the field at pad elevation and 785 m inside a hillside; at KMYL, 1,137 m in the air.
///
/// An aeroplane spawned inside terrain takes LandingGear.Compute to NaN on its first step
/// (Math.Sign(NaN) throws), so the sim throws every frame: the airframe never moves, the controls do nothing,
/// and the chase camera — which may not go below ground — climbs to the surface above and looks down at an
/// aeroplane it cannot reach. One cause, both of the symptoms Ty saw on the device.
///
/// These tests mirror what FlightSimDriver.Spawn does, because that lives in Unity and cannot be reached from
/// here. If the spawn path changes shape, change them together.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class IslandStartTests : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method
    /// does not run when the test throws, so a failing test used to leave its world installed in the global
    /// WorldTerrain.Active for the whole rest of the run — which silently changed the ground under every
    /// class that came after it, whether or not it was serialised.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _out;
    public IslandStartTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static readonly double[] WindsFrom = { 0, 45, 90, 135, 180, 225, 270, 315 };

    [Fact]
    public void EveryAirportStartsOnItsOwnRunway()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            {
                List<IslandField> fields = IslandSites.Fields.Where(f => f.Field == a.Name && IslandRunways.IsRunway(f)).ToList();
                Assert.True(fields.Count > 0, $"{a.Name} has no runway in the world");
                foreach (double from in WindsFrom)
                    foreach (bool headwind in new[] { true, false })
                        foreach (double speed in new[] { 0.0, 7.7 })
                        {
                            WorldTerrain.RunwayEnd rw = WorldTerrain.ChooseRunway(a, from * Math.PI / 180, headwind, speed);
                            (double sx, double sy) = rw.Start;
                            var p = new IslandPoint(sy / 1000, sx / 1000);
                            IslandField? on = fields.FirstOrDefault(f => f.Contains(p, 30));
                            Assert.True(on is not null,
                                $"{a.Name} wind {from:F0}/{speed:F1} {(headwind ? "headwind" : "crosswind")}: start ({sx:F0},{sy:F0}) is not on any runway of this field");
                            double ground = WorldTerrain.GroundHeightAt(sx, sy);
                            Assert.InRange(ground - on!.Plane(p), -1.0, 1.0);
                            Assert.NotEqual(WorldTerrain.Surface.Rough, WorldTerrain.SurfaceAt(sx, sy));
                        }
            }
        }
        finally { WorldTerrain.Active = null; }
    }

    [Fact]
    public void TheRunwayChoiceAgreesWithTheStartTheWorldAuthored()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            int checked_ = 0;
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            {
                IslandSites.Start? authored = IslandRunways.Authored(a.Name, "OnTheRunway");
                if (authored is null) continue;
                // Calm: no wind argues for one end over the other, so the end the world starts from is the answer.
                WorldTerrain.RunwayEnd rw = WorldTerrain.ChooseRunway(a, 0, headwind: true, windSpeedMs: 0.0);
                (double sx, double sy) = rw.Start;
                _out.WriteLine($"{a.Name,-6} authored ({authored.Value.X,8:F0},{authored.Value.Y,8:F0}) hdg {authored.Value.HeadingTrueDeg,5:F0}   chosen ({sx,8:F0},{sy,8:F0}) hdg {rw.HeadingRad * 180 / Math.PI % 360,5:F0}");
                Assert.Equal(authored.Value.X, sx, 1);
                Assert.Equal(authored.Value.Y, sy, 1);
                double hdg = (rw.HeadingRad * 180 / Math.PI % 360 + 360) % 360;
                Assert.Equal(authored.Value.HeadingTrueDeg, hdg, 1);
                checked_++;
            }
            Assert.Equal(8, checked_);
        }
        finally { WorldTerrain.Active = null; }
    }

    [Theory]
    [InlineData("c172-like")]
    [InlineData("pa18-cub-like")]
    public void NoStartPutsAnAeroplaneInsideTheGround(string id)
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig config = Load(id);
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            {
                // On the runway, exactly as FlightSimDriver.Spawn builds it.
                WorldTerrain.RunwayEnd rw = WorldTerrain.ChooseRunway(a, 90 * Math.PI / 180, headwind: true, windSpeedMs: 5.0);
                (double sx, double sy) = rw.Start;
                double pad = WorldTerrain.GroundHeightAt(sx, sy);
                RigidBodyState ground = LandingGear.RestingState(config, sx, sy, pad, rw.HeadingRad);
                double cg = -ground.Position.Z - pad;
                _out.WriteLine($"{a.Name,-6} {id,-14} runway start: pad {pad,8:F1} CG {cg,6:F2} m above it");
                Assert.InRange(cg, 0.2, 6.0);   // sitting on its wheels, not buried and not hovering

                // On final, and the authored airborne start.
                var (final, _, _) = ApproachSpawn.Compute(config, a, rw);
                double fAgl = -final.Position.Z - WorldTerrain.GroundHeightAt(final.Position.X, final.Position.Y);
                Assert.True(fAgl > 15, $"{a.Name} on final is {fAgl:F0} m above the ground");

                IslandSites.Start? air = IslandRunways.Authored(a.Name, "InTheAir");
                Assert.True(air.HasValue, $"{a.Name} has no authored InTheAir start");
                double aAgl = air.Value.AltitudeM - WorldTerrain.GroundHeightAt(air.Value.X, air.Value.Y);
                Assert.True(aAgl > 50, $"{a.Name} in the air is {aAgl:F0} m above the ground");

                // The rest of the VR menu's START list: free flight takes the world's authored start nearest the
                // field (FlightSimDriver.Spawn → IslandRunways.AuthoredNearest), so every one of them must be in
                // the air, not in a hillside — Ty's first Quest flight looked like exactly that (2026-11-02).
                foreach (string mode in new[] { "InAeroBox", "InCombatZone", "InThermal" })
                {
                    IslandSites.Start? s = IslandRunways.AuthoredNearest(mode, a.X, a.Y);
                    Assert.True(s.HasValue, $"{a.Name}: no authored {mode} start anywhere");
                    double agl = s.Value.AltitudeM - WorldTerrain.GroundHeightAt(s.Value.X, s.Value.Y);
                    _out.WriteLine($"{a.Name,-6} {mode,-12} start {s.Value.AltitudeM,7:F0} m over ground {s.Value.AltitudeM - agl,7:F0} m: AGL {agl,6:F0} m");
                    Assert.True(agl > 50, $"{a.Name} {mode} start is {agl:F0} m above the ground");
                }
            }
        }
        finally { WorldTerrain.Active = null; }
    }

    [Fact]
    public void TheControlsMoveTheAeroplaneAtEveryStart()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig config = Load("c172-like");
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            {
                WorldTerrain.RunwayEnd rw = WorldTerrain.ChooseRunway(a, 90 * Math.PI / 180, headwind: true, windSpeedMs: 5.0);
                (double sx, double sy) = rw.Start;
                double pad = WorldTerrain.GroundHeightAt(sx, sy);
                var loop = new SimLoop(new Aircraft(config, LandingGear.RestingState(config, sx, sy, pad, rw.HeadingRad), ControlDeflections.Neutral));
                // ThrottleLever is -1 full FORWARD .. +1 full aft, so full power is -1.
                var takeoff = new ControlInputs(0, -0.3, 0, -1);
                double dt = SimLoop.DefaultFixedDtSec;
                for (int i = 0; i < (int)(15 / dt); i++) loop.RunFor(dt, takeoff);
                RigidBodyState s = loop.Aircraft.State;
                double moved = Math.Sqrt((s.Position.X - sx) * (s.Position.X - sx) + (s.Position.Y - sy) * (s.Position.Y - sy));
                double speed = s.Velocity.Length;
                _out.WriteLine($"{a.Name,-6} 15 s at full throttle: {moved,7:F0} m down the runway, {speed,6:F1} m/s, {-s.Position.Z - pad,6:F1} m above the pad");
                Assert.False(double.IsNaN(s.Position.Z) || double.IsNaN(speed), $"{a.Name}: the sim went NaN — the controls are dead");
                Assert.True(moved > 100, $"{a.Name}: full throttle for 15 s moved the aeroplane {moved:F0} m");
                Assert.True(speed > 15, $"{a.Name}: full throttle for 15 s reached {speed:F1} m/s");
            }
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>The seaplane starts, which are the half Ty found by flying: the J-3 on floats launched on the iPhone
    /// at 12 ft and 10 kt with alpha 180 and beta 90 and nothing on the pads moving it. Those are not flight values —
    /// they are the spawn, frozen. 12 ft is PHDH's legacy lake surface (2.3 m) plus the Cub's float keel less its
    /// resting draft; 10 kt is the 5 m/s session wind; beta 90 is that wind from the east on an aeroplane heading
    /// north. The state never advanced, because the legacy lake at PHDH is 448 m inside a hill with no water in it.</summary>
    [Theory]
    [InlineData("j3-cub-floats-like", 600.0)]
    [InlineData("pa18-floats-like", 600.0)]
    [InlineData("dhc2-beaver-floats-like", 600.0)]
    [InlineData("hughes-h4-like", 2200.0)]
    public void EverySeaplaneStartsOnWaterDeepEnoughToFloat(string id, double minLengthM)
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig config = Load(id);
            Assert.NotNull(config.Floats);
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            {
                WorldTerrain.Lake? lake = IslandRunways.Water(a, minLengthM);
                Assert.True(lake.HasValue, $"{a.Name}: no water for {id}");
                WorldTerrain.Lake lk = lake.Value;

                double? surface = FloatHydro.WaterSurfaceAt(lk.Cx, lk.Cy, lk.SurfaceM);
                Assert.True(surface.HasValue, $"{a.Name}: {id} would start where there is no water at all");
                double bed = WorldTerrain.GroundHeightAt(lk.Cx, lk.Cy);
                Assert.True(surface.Value - bed > 1.0, $"{a.Name}: {id} would start in {surface.Value - bed:F1} m of water");
                Assert.True(2 * Math.Max(lk.Rx, lk.Ry) >= minLengthM, $"{a.Name}: {id} has only {2 * Math.Max(lk.Rx, lk.Ry):F0} m of water");

                // At rest on it, as FlightSimDriver.Spawn builds it: the keel in the water, the CG above it.
                var wpos = new Vec3(lk.Cx - lk.Rx * (config.Floats.Count == 1 ? 0.85 : 0.5), lk.Cy,
                                    -(lk.SurfaceM + config.Floats.KeelZ - 0.25));
                double? here = FloatHydro.WaterSurfaceAt(wpos.X, wpos.Y, -wpos.Z);
                Assert.True(here.HasValue, $"{a.Name}: {id} sits off the edge of its own water");
                Assert.True(-wpos.Z > WorldTerrain.GroundHeightAt(wpos.X, wpos.Y),
                    $"{a.Name}: {id} is under the bed at its start point");
                _out.WriteLine($"{a.Name,-6} {id,-22} water at ({lk.Cx,8:F0},{lk.Cy,8:F0}) surface {lk.SurfaceM,7:F1} depth {surface.Value - bed,6:F1} m  run {2 * Math.Max(lk.Rx, lk.Ry),6:F0} m");
            }
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>A landplane that ditches has to stop at the water. Ty's Pitts "froze" about thirty seconds into a
    /// flight out of the aerobatic box; the device watchdog showed what really happened — it went into the sea at
    /// t=25 s and kept going: alt -25, -174, -534, -1046, -1654, -2303 m and still falling, with the camera pinned at
    /// the surface because it may not follow below ground. Every contact point over water returned no force at all,
    /// so there was nothing to stop it, and the frame cost rose to 45 ms because once under the ground the altitude
    /// gate opens and all fifteen points are tested every step of every stage.</summary>
    [Theory]
    [InlineData("c172-like")]
    [InlineData("pitts-s2b-like")]
    public void AnAeroplaneThatCannotFloatStopsAtTheWaterInsteadOfFallingThroughTheWorld(string id)
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig config = Load(id);
            Assert.Null(config.Floats);
            // Over the open sea off the aerobatic box, where the Pitts went in: sea at 0, bed at -30.
            double x = 7499, y = 6193;
            Assert.True(FloatHydro.WaterSurfaceAt(x, y, 0).HasValue, "this spot should be open water");
            var state = new RigidBodyState(new Vec3(x, y, -60), new Quat(0, 0, 0, 1), new Vec3(30, 0, 25), Vec3.Zero);
            var loop = new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
            double dt = SimLoop.DefaultFixedDtSec, lowest = 0;
            for (int i = 0; i < (int)(20 / dt); i++)
            {
                loop.RunFor(dt, ControlInputs.Neutral);
                double alt = -loop.Aircraft.State.Position.Z;
                if (alt < lowest) lowest = alt;
                Assert.False(double.IsNaN(alt), $"{id}: NaN after ditching");
            }
            double rest = -loop.Aircraft.State.Position.Z;
            // One that is sinking gets up to another 40 s to reach the bottom (a 30 m fall through water).
            for (int i = 0; i < (int)(40 / dt) && rest < -10 && loop.Aircraft.State.Velocity.Length >= 0.1; i++)
            {
                loop.RunFor(dt, ControlInputs.Neutral);
                lowest = Math.Min(lowest, -loop.Aircraft.State.Position.Z);
                rest = -loop.Aircraft.State.Position.Z;
            }
            double speed = loop.Aircraft.State.Velocity.Length;
            _out.WriteLine($"{id,-16} ditched at 25 m/s: lowest {lowest,8:F1} m, at rest {rest,8:F1} m (20-60 s), speed {speed:F2} m/s, lost {string.Join(",", loop.Aircraft.LostComponents)}");
            Assert.True(lowest > -30, $"{id} went through the sea bed: {lowest:F0} m");
            // Since 2026-11-02 (lane/claude-crash-water) the water holds an aeroplane up by its own volumes and floods it
            // through its openings, so a 25 m/s vertical arrival that breaks it up may sink it: it must then lie ON the
            // bed (-30 m here), stopped — afloat or on the bottom, never through it and never still falling.
            Assert.True(rest > -10 || (rest > -30 && speed < 0.5), $"{id} neither afloat nor at rest on the bed: {rest:F0} m, {speed:F1} m/s");
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>The thermal starts name a lift feature; S3's start and its lift centre disagree by 1.2 km.
    /// Codex's decision (2026-10-31) is to keep the lift centre at (E 5.7, N −4.6) and move the start, which is
    /// a world-data change and so is Codex's to make — logged as a HANDOFF. This holds the line meanwhile: one
    /// known mismatch, and it is S3. It passes once the start moves, and fails if a second one appears.</summary>
    [Fact]
    public void TheOnlyThermalStartAwayFromItsLiftIsS3()
    {
        var mismatched = new List<string>();
        foreach (IslandValue lift in IslandTerrain.World["lift_terrain"].Items)
        {
            string id = lift.S("id");
            IslandSites.Start? start = IslandSites.Starts.Where(s => s.Id == id).Select(s => (IslandSites.Start?)s).FirstOrDefault();
            if (start is null) continue;
            IslandPoint c = lift["centre"].Point;
            double d = Math.Sqrt((c.X - start.Value.X) * (c.X - start.Value.X) + (c.Y - start.Value.Y) * (c.Y - start.Value.Y));
            _out.WriteLine($"{id,-4} start ({start.Value.X,8:F0},{start.Value.Y,8:F0})  lift centre ({c.X,8:F0},{c.Y,8:F0})  {d,8:F0} m apart");
            if (d > 300) mismatched.Add($"{id} ({d:F0} m)");
        }
        Assert.True(mismatched.Count <= 1, $"more than one thermal start is away from its lift: {string.Join(", ", mismatched)}");
        if (mismatched.Count == 1) Assert.StartsWith("S3", mismatched[0]);
    }
}

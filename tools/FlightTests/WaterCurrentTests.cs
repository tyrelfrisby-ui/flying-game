using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Moving water (Ty 2026-11-04: "What happens if a plane crashes in a river? It should be carried by the current; model it
/// correctly, and model current for floatplanes accurately too"). Ty 19:35: a take-off UPSTREAM is longer — at lift-off
/// the floats are moving through the water at airspeed + current — and DOWNSTREAM shorter; a strong downstream wind can
/// turn that round. Ty 19:36: "NO special-case logic for upstream/downstream at all — just let the physics do the work …
/// Tests only VERIFY the emergent outcome." Nothing in src/ knows which way is upstream: these tests set a current and a
/// wind, fly the same pilot every time, and look at what happens.
///
/// Literals are worked by hand (python, 2026-11-04), not read from the code under test (CLAUDE.md).
/// </summary>
// Mutates WorldTerrain.Active, Atmosphere.SteadyWind and FloatHydro.FlatWaterOverride: the serialised collection.
[Collection("WorldTerrainActive")]
public sealed class WaterCurrentTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public WaterCurrentTests(ITestOutputHelper o) { _out = o; WorldTerrain.Active = null; }
    public void Dispose() { WorldTerrain.Active = null; }

    /// <summary>Flat water at <paramref name="up"/> and (optionally) a current, set and cleared on the test's own thread
    /// (both hooks are thread-static).</summary>
    private sealed class Water : IDisposable
    {
        private readonly Vec3 _wind;
        public Water(double up, Func<double, double, double, Vec3>? current = null, Vec3 wind = default)
        {
            FloatHydro.FlatWaterOverride = up; WaterCurrent.Override = current;
            _wind = Atmosphere.SteadyWind; Atmosphere.SteadyWind = wind;
        }
        public void Dispose() { FloatHydro.FlatWaterOverride = null; WaterCurrent.Override = null; Atmosphere.SteadyWind = _wind; }
    }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.2958; }
    private static double Roll(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.2958; }
    private static double Yaw(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 57.2958; }
    private static double Lever(double throttle01) => 1.0 - 2.0 * throttle01;
    private static Vec3 Ground(RigidBodyState s) => s.Attitude.Rotate(s.Velocity);

    // ---- the field's equations, against hand-worked numbers ----------------------------------------------------------

    [Fact]
    public void JarrettAndTheLogLawMatchHandWorkedNumbers()
    {
        // Jarrett 1984 (SI): V = R^(2/3) S^(1/2) / (0.32 S^0.38 R^-0.16) = R^0.82667 S^0.12 / 0.32. R 2 m, S 0.002:
        // 2.62921 m/s (5.1 kt); S 0.04: 3.76659 (7.3 kt). R 0.15 m (the drawn sheet), S 0.002: 0.30894 m/s (0.6 kt).
        Assert.Equal(2.62921, WaterCurrent.JarrettVelocity(2.0, 0.002), 4);
        Assert.Equal(3.76659, WaterCurrent.JarrettVelocity(2.0, 0.04), 4);
        Assert.Equal(0.30894, WaterCurrent.JarrettVelocity(0.15, 0.002), 4);
        // Outside the fitted slopes the equation is held at its ends.
        Assert.Equal(WaterCurrent.JarrettVelocity(2.0, 0.04), WaterCurrent.JarrettVelocity(2.0, 0.3), 12);
        // Log law, 2 m column, z0 0.012 m: ln(2/0.012) = 5.11600; surface 5.11600/4.11600 = 1.2429546; 1 m down
        // ln(1/0.012)/4.11600 = 4.42285/4.11600 = 1.0745513; on the bed 0.
        Assert.Equal(1.2429546, WaterCurrent.LogProfile(0.0, 2.0, 0.012), 6);
        Assert.Equal(1.0745513, WaterCurrent.LogProfile(1.0, 2.0, 0.012), 6);
        Assert.Equal(0.0, WaterCurrent.LogProfile(2.0, 2.0, 0.012), 12);
    }

    [Fact]
    public void TheTideIsSlackAtTheStartAndRunsBothWaysOverAnM2Period()
    {
        const double T = 44714.16;   // M2: 12.4206012 h
        Assert.Equal(0.0, WaterCurrent.TidalFactor(0.0), 9);
        Assert.Equal(1.0, WaterCurrent.TidalFactor(T / 4), 4);
        Assert.Equal(0.0, WaterCurrent.TidalFactor(T / 2), 4);
        Assert.Equal(-1.0, WaterCurrent.TidalFactor(3 * T / 4), 4);
    }

    // ---- the island's water ------------------------------------------------------------------------------------------

    private static IEnumerable<(string id, IslandPoint[] pts)> Streams() =>
        IslandTerrain.World["canyons"].Items.Select(q => (q.S("id"), q["path"].Points));

    [Fact]
    public void EveryIslandStreamCarriesAFloatDownhillAtAMountainRiversSpeed()
    {
        WorldTerrain.Active = new WorldTerrain();
        int checkedStreams = 0;
        foreach ((string id, IslandPoint[] pts) in Streams())
        {
            // A tracer dropped on the centreline a fifth of the way along the first segment, carried by the surface current
            // for 60 s in 0.5 s steps: it must go somewhere, toward the stream's LOW end. (Not "downhill at every metre":
            // the canyon floors have strips and bumps on them a metre high, and a river runs on over those.)
            IslandPoint a = pts[0], b = pts[1], p0 = a + (b - a) * 0.2;
            double x = p0.X, y = p0.Y;
            if (FloatHydro.WaterSurfaceAt(x, y) is null) continue;
            WaterCurrent.Column c0 = WaterCurrent.ColumnAt(x, y);
            double speed = Math.Sqrt(c0.North * c0.North + c0.East * c0.East);
            double h0 = WorldTerrain.GroundHeightAt(x, y);
            IslandPoint lo = WorldTerrain.GroundHeightAt(pts[0].X, pts[0].Y) >= WorldTerrain.GroundHeightAt(pts[^1].X, pts[^1].Y) ? pts[^1] : pts[0];
            for (int i = 0; i < 120; i++) { Vec3 v = WaterCurrent.SurfaceAt(x, y); x += v.X * 0.5; y += v.Y * 0.5; }
            double moved = Math.Sqrt((x - p0.X) * (x - p0.X) + (y - p0.Y) * (y - p0.Y)), h1 = WorldTerrain.GroundHeightAt(x, y);
            _out.WriteLine($"{id,-8} centreline {speed:F2} m/s ({speed / 0.514444:F1} kt), tracer {moved:F0} m in 60 s, ground {h0:F1} -> {h1:F1} m");
            Assert.InRange(speed, 2.0 * 0.514444 - 1e-9, 8.0 * 0.514444 + 1e-9);   // the brief's 2-8 kt
            Assert.True(moved > 30, $"{id}: the tracer only moved {moved:F1} m");
            double before = Math.Sqrt((lo.X - p0.X) * (lo.X - p0.X) + (lo.Y - p0.Y) * (lo.Y - p0.Y)), after = Math.Sqrt((lo.X - x) * (lo.X - x) + (lo.Y - y) * (lo.Y - y));
            Assert.True(after < before - 0.8 * moved, $"{id}: carried {moved:F0} m but only {before - after:F0} m toward the low end");
            checkedStreams++;
        }
        Assert.True(checkedStreams >= 15, $"only {checkedStreams} streams had water where the tracer started");
    }

    [Fact]
    public void AStreamRunsFastestMidChannelSlowerNearTheBanksAndNotAtAllPastThem()
    {
        WorldTerrain.Active = new WorldTerrain();
        // Middle Fork, reach 3 (C2-MF3): straight, 10 m half-width.
        IslandPoint[] pts = Streams().First(s => s.id == "C2-MF3").pts;
        IslandPoint a = pts[0], b = pts[1], mid = a + (b - a) * 0.5, t = (b - a) * (1 / (b - a).Length), n = new IslandPoint(t.N, -t.E);
        // The depth-averaged speed (the surface's adds the log law's factor for the local depth, which is shallower off the centreline).
        double Speed(double acrossM) { IslandPoint p = mid + n * (acrossM / 1000); WaterCurrent.Column k = WaterCurrent.ColumnAt(p.X, p.Y); return Math.Sqrt(k.North * k.North + k.East * k.East); }
        double c = Speed(0), half = Speed(5), near = Speed(9), past = Speed(14);
        _out.WriteLine($"MF3 depth-averaged speed: centre {c:F2}, 5 m off {half:F2}, 9 m off {near:F2}, 14 m off {past:F2} m/s");
        // u(s) ∝ (1 − s²)^0.82667: s 0.5 → 0.78835, s 0.9 → 0.25338.
        Assert.Equal(0.78835, half / c, 2);
        Assert.Equal(0.25338, near / c, 2);
        Assert.Equal(0.0, past, 12);
        Assert.Equal(half, Speed(-5), 2);   // symmetric
    }

    [Fact]
    public void LakesAndPondsAreStillAndTheSeaCarriesTheTide()
    {
        WorldTerrain.Active = new WorldTerrain();
        foreach (IslandValue q in IslandTerrain.World["water"].Items)
        {
            IslandPoint c = q["centre"].Point;
            if (FloatHydro.WaterSurfaceAt(c.X, c.Y) is null) continue;
            WaterCurrent.Column col = WaterCurrent.ColumnAt(c.X, c.Y);
            Assert.True(col.North == 0 && col.East == 0, $"{q.S("id")} is moving");
        }
        foreach (IslandField f in IslandSites.Fields.Where(f => f.Surface == "water"))
        {
            WaterCurrent.Column col = WaterCurrent.ColumnAt(f.Centre.X, f.Centre.Y);
            Assert.True(col.North == 0 && col.East == 0, $"{f.Id} is moving");
        }
        // The sea at the mouth of C3 (ground ≈ −30 m): at full flood 0.3 m/s toward 045°, i.e. 0.21213 north and east.
        WaterCurrent.Column sea = WaterCurrent.ColumnAt(-9600, -1540);
        Assert.True(sea.Tidal);
        Assert.Equal(0.21213, sea.North, 4);
        Assert.Equal(0.21213, sea.East, 4);
        Assert.True(sea.DepthM > 20, $"sea {sea.DepthM:F1} m deep");
    }

    [Fact]
    public void BelowASharpBendTheWaterByTheInnerBankRunsBackUpstream()
    {
        // A 10 m half-width channel running north at S 0.01 with a 90° bend to the left above this reach: the inner bank is
        // the left (s > 0). Jarrett at D_max 2.0 m, S 0.01: 2^0.82667·0.01^0.12/0.32 = 3.18934 m/s on the centreline.
        // 1.5 widths (30 m) below the bend the eddy's weight is 1 at s 0.7: u = −0.2 · 3.18934 = −0.63787 m/s, back upstream;
        // the outer bank at s −0.7 keeps (1 − 0.49)^0.82667 = 0.57301 of the centreline: 1.82793 m/s.
        WaterCurrent.Column inner = WaterCurrent.Channel(1, 0, 0.7, 10, 0.01, Math.PI / 2, 30);
        WaterCurrent.Column outer = WaterCurrent.Channel(1, 0, -0.7, 10, 0.01, Math.PI / 2, 30);
        Assert.Equal(-0.63787, inner.North, 4);
        Assert.Equal(1.82793, outer.North, 4);
        // Above the bend, and past the eddy's length (3 widths), the inner bank is the ordinary profile again.
        Assert.Equal(outer.North, WaterCurrent.Channel(1, 0, 0.7, 10, 0.01, Math.PI / 2, 61).North, 9);
        Assert.Equal(outer.North, WaterCurrent.Channel(1, 0, 0.7, 10, 0.01, Math.PI / 2, -1).North, 9);
        // A gentle bend only slows it: 25° is 0.41667 of the full eddy.
        double gentle = WaterCurrent.Channel(1, 0, 0.7, 10, 0.01, 25 * Math.PI / 180, 30).North;
        Assert.InRange(gentle, 0, outer.North * 0.6);
    }

    [Fact]
    public void BelowTheIslandsBendsTheInnerBankRunsSlowerThanTheOuter()
    {
        WorldTerrain.Active = new WorldTerrain();
        int seen = 0;
        foreach ((string id, IslandPoint[] pts) in Streams())
        {
            if (pts.Length < 3) continue;
            bool down = WorldTerrain.GroundHeightAt(pts[0].X, pts[0].Y) >= WorldTerrain.GroundHeightAt(pts[^1].X, pts[^1].Y);
            double half = id == "C3" ? 18 : 10;
            for (int k = 1; k + 1 < pts.Length; k++)
            {
                IslandPoint d0 = pts[k] - pts[k - 1], d1 = pts[k + 1] - pts[k];
                double turn = Math.Atan2(d0.E * d1.N - d0.N * d1.E, d0.Dot(d1));
                if (Math.Abs(turn) < 10 * Math.PI / 180) continue;
                // 1.5 channel widths below the bend, 70 % of the way to each bank.
                IslandPoint seg = down ? d1 : d0 * -1.0, u = seg * (1 / seg.Length);
                IslandPoint pathDir = (down ? d1 : d0) * (1 / (down ? d1 : d0).Length);
                IslandPoint left = new(-pathDir.N, pathDir.E);
                IslandPoint pi = pts[k] + u * (1.5 * 2 * half / 1000) + left * (Math.Sign(turn) * 0.7 * half / 1000);
                IslandPoint po = pts[k] + u * (1.5 * 2 * half / 1000) - left * (Math.Sign(turn) * 0.7 * half / 1000);
                if (FloatHydro.WaterSurfaceAt(pi.X, pi.Y) is null || FloatHydro.WaterSurfaceAt(po.X, po.Y) is null) continue;
                WaterCurrent.Column ci = WaterCurrent.ColumnAt(pi.X, pi.Y), co = WaterCurrent.ColumnAt(po.X, po.Y);
                double alongI = ci.North * u.N + ci.East * u.E, alongO = co.North * u.N + co.East * u.E;
                _out.WriteLine($"{id} bend {k} ({turn * 57.3:F0}°): inner bank {alongI:F2} m/s along the river, outer bank {alongO:F2}");
                Assert.True(alongO > 0, $"{id} bend {k}: the outer bank runs {alongO:F2}");
                Assert.True(alongI < 0.6 * alongO, $"{id} bend {k}: inner {alongI:F2} vs outer {alongO:F2}");
                seen++;
            }
        }
        Assert.True(seen >= 1, "no bend with water to check");
    }

    // ---- wrecks ------------------------------------------------------------------------------------------------------

    /// <summary>A C172 wing (long, light, floats a long time on its trapped air), laid on still water 30 m deep and left to
    /// settle for 20 s.</summary>
    private static (WreckSim sim, WreckPiece piece, FlatWreckWorld world) FloatingWing(AirframeComponent comp = AirframeComponent.WingLeft, double waterUp = 0.0)
    {
        AircraftConfig c = Load("c172-like");
        var state = new RigidBodyState(new Vec3(0, 0, -1.0), Quat.Identity, Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(c, state);
        List<HydroElement> els = ac.Hydro.Detach(comp);
        var world = new FlatWreckWorld(waterUp, -30.0);
        var sim = new WreckSim(world);
        WreckPiece p = sim.Add(comp.ToString(), comp, els, c.Mass.CgVec(), state);
        for (int i = 0; i < (int)(20 / sim.Dt); i++) sim.StepOnce();
        return (sim, p, world);
    }

    [Fact]
    public void AWreckInARiverIsCarriedDownstreamAtTheRiversSpeed()
    {
        (WreckSim sim, WreckPiece wing, FlatWreckWorld world) = FloatingWing();
        Assert.True(wing.Phase is WreckPhase.Floating or WreckPhase.Plunging, $"{wing.Phase}");
        // A 5 kt river running north-east.
        world.Current = WaterCurrent.Uniform(1.8, 1.8);
        Vec3 x0 = wing.State.Position;
        for (int i = 0; i < (int)(120 / sim.Dt); i++) sim.StepOnce();
        Vec3 v = wing.WorldVelocity, moved = wing.State.Position - x0;
        _out.WriteLine($"after 120 s: {v.X:F3}, {v.Y:F3} m/s (river 1.800, 1.800); moved {moved.X:F0}, {moved.Y:F0} m; {wing.Phase}");
        Assert.InRange(v.X, 1.8 * 0.95, 1.8 * 1.05);
        Assert.InRange(v.Y, 1.8 * 0.95, 1.8 * 1.05);
        Assert.True(moved.X > 150 && moved.Y > 150, "it should have gone ~200 m downstream");
    }

    [Fact]
    public void InStillWaterAZeroCurrentChangesNothing()
    {
        (WreckSim a, WreckPiece pa, _) = FloatingWing();
        (WreckSim b, WreckPiece pb, FlatWreckWorld wb) = FloatingWing();
        wb.Current = WaterCurrent.Uniform(0, 0);   // the moving-water path, with the water not moving
        for (int i = 0; i < (int)(30 / a.Dt); i++) { a.StepOnce(); b.StepOnce(); }
        Assert.Equal(pa.State.Position.X, pb.State.Position.X, 12);
        Assert.Equal(pa.State.Position.Y, pb.State.Position.Y, 12);
        Assert.Equal(pa.State.Position.Z, pb.State.Position.Z, 12);
        Assert.Equal(pa.State.Rates.Z, pb.State.Rates.Z, 12);
    }

    [Fact]
    public void AWreckAcrossARiversShearIsTurnedAndInAUniformCurrentIsNot()
    {
        // Where the wing's span points over the water (its own y axis, projected on the horizontal): a floating wing pitches
        // and rolls on the swell of its own settling, so Euler yaw is the wrong measure of being turned.
        static double Span(RigidBodyState s) { Vec3 e = s.Attitude.Rotate(new Vec3(0, 1, 0)); return Math.Atan2(e.Y, e.X) * 180 / Math.PI; }
        double TurnedBy(Func<double, double, double, Vec3>? current)
        {
            (WreckSim sim, WreckPiece wing, FlatWreckWorld world) = FloatingWing();
            double a0 = Span(wing.State), worst = 0;
            world.Current = current;
            for (int i = 0; i < (int)(60 / sim.Dt); i++)
            {
                sim.StepOnce();
                double d = Span(wing.State) - a0;
                d = d > 180 ? d - 360 : d < -180 ? d + 360 : d;
                worst = Math.Max(worst, Math.Abs(d));
            }
            return worst;
        }
        double still = TurnedBy(null), uniform = TurnedBy(WaterCurrent.Uniform(2.0, 0));
        // The same 2 m/s at y = 0, faster toward +y (east): 0.2 m/s per metre across, so 1 m/s more at one tip than the other.
        double shear = TurnedBy((x, y, d) => new Vec3(2.0 + 0.2 * y, 0, 0));
        _out.WriteLine($"span turned in 60 s: still water {still:F1}°, uniform current {uniform:F1}°, sheared {shear:F1}°");
        Assert.True(Math.Abs(uniform - still) < 3, $"a uniform current turned it {uniform:F1}° (still water {still:F1}°)");
        Assert.True(shear > still + 20, $"the shear only turned it {shear:F1}° (still water {still:F1}°)");
    }

    [Fact]
    public void AHeavyPieceInTheShallowsGroundsAndStaysPut()
    {
        // The engine (heavy, sinks at once) in a 0.15 m sheet of a 3 kt river: it is on the gravel, and the water's push on
        // the little of it that is wet is well short of what the bed's friction holds.
        AircraftConfig c = Load("c172-like");
        var state = new RigidBodyState(new Vec3(0, 0, -0.6), Quat.Identity, Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(c, state);
        var world = new FlatWreckWorld(0.15, 0.0) { Current = WaterCurrent.Uniform(1.5, 0) };
        var sim = new WreckSim(world);
        WreckPiece engine = sim.Add("nose", AirframeComponent.Nose, ac.Hydro.Detach(AirframeComponent.Nose), c.Mass.CgVec(), state);
        for (int i = 0; i < (int)(10 / sim.Dt); i++) sim.StepOnce();   // the drop onto the gravel and its slide
        Vec3 x0 = engine.State.Position;
        for (int i = 0; i < (int)(20 / sim.Dt); i++) sim.StepOnce();
        double creep = (engine.State.Position - x0).X / 20;
        _out.WriteLine($"nose piece {engine.MassKg:F0} kg in 0.15 m of 1.5 m/s water: creeping {creep:F3} m/s, {engine.Phase}; buoyancy {engine.BuoyancyN:F0} N of {engine.MassKg * 9.80665:F0}");
        // Half its weight is borne by the water it displaces, the rest on the gravel at the wet bed's μ 0.3: the river's
        // push on the wet 0.15 m of it nearly balances that, so it creeps — at a few percent of the river's speed.
        Assert.True(creep < 0.1 * 1.5, $"it moves at {creep:F2} m/s, {creep / 1.5:P0} of the river");
        Assert.True(creep >= 0, "pushed upstream?");
        Assert.True(engine.Phase is WreckPhase.OnBottom, $"{engine.Phase}");
    }

    [Fact]
    public void AnIntactDitchedAeroplaneDriftsWithTheCurrent()
    {
        // A C172 set down on the water and left (the propeller strikes and stops). In still water it bobs and wanders a
        // little on its own; in a 3 kt current it goes with the water, a little behind it: it floats high, and still air
        // is a 1.5 m/s wind to something drifting at 1.5 m/s (leeway — its own windage holding it back).
        Vec3 Drift(double current)
        {
            AircraftConfig c = Load("c172-like");
            using var w = new Water(10.0, current == 0 ? null : WaterCurrent.Uniform(0, current));
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(10.0 + 1.0)), Quat.Identity, Vec3.Zero, Vec3.Zero));
            var loop = new SimLoop(ac);
            loop.RunFor(60, new ControlInputs(0, 0, 0, 1.0));
            Vec3 p0 = ac.State.Position;
            loop.RunFor(60, new ControlInputs(0, 0, 0, 1.0));
            Vec3 d = (ac.State.Position - p0) / 60;
            _out.WriteLine($"C172 in {current:F1} m/s water, 60-120 s: mean drift {d.X:F3}, {d.Y:F3} m/s; lost {string.Join(",", ac.LostComponents)}");
            return d;
        }
        Vec3 still = Drift(0), moving = Drift(1.5);
        double carried = moving.Y - still.Y;
        Assert.InRange(carried, 1.5 * 0.7, 1.5 * 1.02);
        Assert.InRange(still.Y, -0.05, 0.05);
    }

    // ---- floatplanes -------------------------------------------------------------------------------------------------

    [Fact]
    public void AFloatplaneWithItsEngineStoppedDriftsWithTheCurrent()
    {
        AircraftConfig c = Load("j3-cub-floats-like");
        using var w = new Water(0.0, WaterCurrent.Uniform(0, 1.5));   // 3 kt, across the nose
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(c.Floats!.KeelZ - 0.25)), Quat.Identity, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        ac.LoseComponent(AirframeComponent.Propeller);   // engine stopped
        var sim = new SimLoop(ac);
        sim.RunFor(120, new ControlInputs(0, 0, 0, Lever(0)));
        RigidBodyState s = ac.State;
        Vec3 ground = Ground(s), water = ground - WaterCurrent.SurfaceAt(s.Position.X, s.Position.Y);
        double airspeed = (ground - Atmosphere.WindAtPosition(s.Position)).Length;
        _out.WriteLine($"J-3 floats, engine stopped, 120 s in a 1.5 m/s current: over the ground {ground.X:F3}, {ground.Y:F3}; "
                       + $"through the water {water.Length:F3} m/s; airspeed {airspeed:F2} m/s; heading {Yaw(s):F0}°");
        // Carried with the water, a little behind it: at 0.2 m/s through the water the floats' only resistance along the
        // keel is skin friction (well under 10 N), so even the airframe's drag in the 1.3 m/s of still air it is drifting
        // through holds it back (leeway). Measured 2026-11-04: 1.25 m/s over the ground, 0.25 through the water.
        Assert.InRange(ground.Y, 1.5 * 0.75, 1.5 * 1.02);
        Assert.True(water.Length < 0.4, $"still moving {water.Length:F2} m/s through the water");
        // The three speeds a pilot could name are all different: over the ground ≈ the river's, through the water ≈ 0, and
        // the AIRSPEED is the air's — in calm air, the drift itself.
        Assert.Equal(ground.Length, airspeed, 3);
        Assert.True(water.Length < 0.3 * ground.Length);
    }

    /// <summary>The J-3 on floats, nose along +x, sat 15 s at idle in water moving at <paramref name="currentX"/> (m/s along
    /// x) and wind <paramref name="windX"/> (the air's velocity, m/s along x), then full power with the HydroJ3 pilot (stick
    /// back to the hump, then a pitch schedule on AIRSPEED, rudder and aileron holding the heading and wings level). Returns
    /// the take-off run from full power to unstick: time, distance through the WATER, distance over the GROUND, and the
    /// floats' speed through the water at unstick. The pilot is the same function in every case.</summary>
    private (double t, double waterM, double groundM, double waterSpeed, double airspeed) TakeOff(double currentX, double windX, bool hookAlways = false)
    {
        AircraftConfig c = Load("j3-cub-floats-like");
        double weight = c.Mass.MassKg * Atmosphere.GravityMs2;
        using var w = new Water(0.0, currentX == 0 && !hookAlways ? null : WaterCurrent.Uniform(currentX, 0), new Vec3(windX, 0, 0));
        var st = new RigidBodyState(new Vec3(0, 0, -(c.Floats!.KeelZ - 0.25)), Quat.Identity, new Vec3(currentX, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, st, ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        ControlInputs Hold(RigidBodyState s, double stick, double lever)
        {
            double rud = Math.Clamp(-0.08 * Yaw(s) - 0.6 * s.Rates.Z, -1, 1), ail = Math.Clamp(-0.05 * Roll(s) - 0.3 * s.Rates.X, -1, 1);
            return new ControlInputs(ail, stick, rud, lever);
        }
        for (double t = 0; t < 15; t += 0.05) sim.RunFor(0.05, Hold(ac.State, 0, Lever(0)));
        double x0 = ac.State.Position.X, waterRun = 0, integ = 0, peak = -99;
        bool eased = false;
        for (double t = 0; t < 60; t += 0.05)
        {
            RigidBodyState s = ac.State;
            Vec3 g = Ground(s);
            double air = (g - Atmosphere.WindAtPosition(s.Position)).Length, pitch = Pitch(s), q = s.Rates.Y * 57.3, stick;
            if (!eased) { stick = -1; if (pitch > peak) peak = pitch; else if (peak > 6 && pitch < peak - 1) eased = true; }
            else { double target = air < 18 ? 6.0 : 10.0; integ = Math.Clamp(integ + (target - pitch) * 0.05, -20, 20); stick = Math.Clamp(-0.15 - 0.10 * (target - pitch) - 0.03 * integ + 0.04 * q, -1, 1); }
            sim.RunFor(0.05, Hold(s, stick, Lever(1.0)));
            RigidBodyState n = ac.State;
            waterRun += (Ground(n).X - WaterCurrent.SurfaceAt(n.Position.X, n.Position.Y).X) * 0.05;
            FloatHydro.Report r = FloatHydro.LastReport;
            Assert.False(double.IsNaN(n.Position.Z), $"NaN at t={t:F1}");
            if (eased && r.BuoyancyN + r.PlaningLiftN < 0.02 * weight)
            {
                Vec3 gn = Ground(n);
                return (t + 0.05, waterRun, n.Position.X - x0, gn.X - currentX, (gn - Atmosphere.WindAtPosition(n.Position)).Length);
            }
        }
        return (double.NaN, waterRun, ac.State.Position.X - x0, double.NaN, double.NaN);
    }

    [Fact]
    public void TakingOffUpstreamTakesLongerThanDownstreamAndAStrongDownstreamWindTurnsThatRound()
    {
        const double current = 2.0;       // ~4 kt
        const double wind = 7.7;          // 15 kt, blowing DOWNSTREAM in the windy pair
        var calmNone = TakeOff(0, 0);
        var calmUp = TakeOff(-current, 0);    // the water runs toward −x: the nose (+x) is upstream
        var calmDown = TakeOff(+current, 0);
        var windUp = TakeOff(-current, -wind);   // upstream and into the wind
        var windDown = TakeOff(+current, +wind); // downstream with the wind behind
        void Say(string what, (double t, double waterM, double groundM, double waterSpeed, double airspeed) r) =>
            _out.WriteLine($"{what,-28} unstick after {r.t,5:F1} s; {r.waterM,6:F0} m through the water, {r.groundM,6:F0} m over the ground; "
                           + $"at unstick {r.airspeed,5:F1} m/s airspeed, {r.waterSpeed,5:F1} m/s through the water");
        Say("no current, calm", calmNone);
        Say("upstream, calm", calmUp);
        Say("downstream, calm", calmDown);
        Say("upstream, into 15 kt wind", windUp);
        Say("downstream, 15 kt tailwind", windDown);
        foreach (var r in new[] { calmNone, calmUp, calmDown, windUp, windDown }) Assert.False(double.IsNaN(r.t), "did not get off");

        // Calm air: the same airspeed at unstick is airspeed + 2 m/s through the water upstream, − 2 m/s downstream.
        Assert.Equal(calmUp.airspeed + current, calmUp.waterSpeed, 0);
        Assert.Equal(calmDown.airspeed - current, calmDown.waterSpeed, 0);
        // ... so the upstream run is substantially longer, in time and through the water, and the downstream one shorter.
        Assert.True(calmUp.t > 1.15 * calmDown.t, $"upstream {calmUp.t:F1} s vs downstream {calmDown.t:F1} s");
        Assert.True(calmUp.waterM > 1.15 * calmDown.waterM, $"upstream {calmUp.waterM:F0} m vs downstream {calmDown.waterM:F0} m through the water");
        Assert.True(calmUp.t > calmNone.t && calmNone.t > calmDown.t, "no current should lie between");
        // A 15 kt wind blowing downstream: upstream is now into it, and the comparison turns round.
        double calmRatio = calmUp.t / calmDown.t, windRatio = windUp.t / windDown.t;
        Assert.True(windRatio < calmRatio, $"ratio up/down {calmRatio:F2} calm, {windRatio:F2} with the wind");
        Assert.True(windUp.t < windDown.t, $"into wind upstream {windUp.t:F1} s vs downwind downstream {windDown.t:F1} s");
    }

    [Fact]
    public void WithNoCurrentTheTakeOffIsExactlyTheStillWaterOne()
    {
        var still = TakeOff(0, 0);
        var hooked = TakeOff(0, 0, hookAlways: true);   // the moving-water path throughout, the water not moving
        Assert.Equal(still.t, hooked.t, 12);
        Assert.Equal(still.groundM, hooked.groundM, 9);
        Assert.Equal(still.waterM, hooked.waterM, 9);
    }

    [Fact]
    public void TaxiingUpstreamTakesPower()
    {
        // Nose upstream into a 4 kt river, calm air. At idle the floats move forward THROUGH THE WATER and still go
        // backwards over the ground; it takes power to make way up the river.
        AircraftConfig c = Load("j3-cub-floats-like");
        using var w = new Water(0.0, WaterCurrent.Uniform(-2.0, 0));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(c.Floats!.KeelZ - 0.25)), Quat.Identity, new Vec3(-2.0, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        ControlInputs Taxi(double throttle) { RigidBodyState s = ac.State; return new ControlInputs(0, -0.3, Math.Clamp(-0.08 * Yaw(s) - 0.6 * s.Rates.Z, -1, 1), Lever(throttle)); }
        for (double t = 0; t < 60; t += 0.05) sim.RunFor(0.05, Taxi(0));
        double idleGround = Ground(ac.State).X, idleWater = idleGround + 2.0, yawIdle = Yaw(ac.State);
        for (double t = 0; t < 60; t += 0.05) sim.RunFor(0.05, Taxi(0.3));
        double powerGround = Ground(ac.State).X, powerWater = powerGround + 2.0;
        _out.WriteLine($"idle: {idleWater:F2} m/s through the water, {idleGround:F2} over the ground (heading {yawIdle:F0}°); "
                       + $"30 % power: {powerWater:F2} through the water, {powerGround:F2} over the ground");
        Assert.True(idleWater > 0, "at idle it should still be moving forward through the water");
        Assert.True(idleGround < 0, "at idle the river should be carrying it backwards");
        Assert.True(powerGround > 0, "with power it should make way upstream");
    }
}

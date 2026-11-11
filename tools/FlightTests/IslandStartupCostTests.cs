using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE 2026-10-30 STARTUP FAILURE: the app opened on Ty's iPhone and iPad and would not play. It was neither a crash
/// nor a hang — FLY worked, the aeroplane spawned, the controls answered and the simulation ran, at 5 fps with
/// 220 ms of CPU main-thread time a frame against 4 ms of GPU. One island height query costs about 70 us, because
/// the height field evaluates the whole world description per sample, and AirframeContact asked for one per contact
/// point per RK4 stage at 200 Hz: about 12,000 a second for ONE aeroplane.
///
/// The timings below are REPORTED, not asserted: a frame budget belongs to the machine, and a timing assertion on a
/// shared runner is a flake waiting to happen. What IS asserted is the safety property the fix rests on — that
/// skipping the contact loop at altitude never skips a real contact.
///
/// Every test here installs the island (<c>WorldTerrain.Active ??= …</c>), and until 2026-11-06 none of them took it
/// away again: the island stayed under every class that ran after this one, so the ground at (0, 0) was 2,460 m and
/// StructuralTests, which flies from 600-1,500 m over what it takes to be flat ground, started underground and went
/// NaN (B1 qualification, t971u: 40 names, reproduced by running this class before it). <see cref="Dispose"/> puts back
/// whatever terrain was there before, after every test, pass or fail.
/// </summary>
// FLIES an aeroplane, so it READS the global ground and weather (WorldTerrain.Active, Atmosphere.*,
// FloatHydro) through the sim even though it sets none of them. The collection was protecting the
// classes that WRITE that state from each other and leaving their victims outside it, so this class ran
// in parallel with 68 that install worlds and weather: StructuralTests went 39 of 40 NaN in one run and
// passed clean in the next on identical code (2026-11-01). A reader needs the same serialisation as a
// writer. It swaps the global terrain itself too, so it is never in parallel with the legacy-world tests.
[Collection("WorldTerrainActive")]
public class IslandStartupCostTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain? _previousTerrain;
    public IslandStartupCostTests(ITestOutputHelper o) { _o = o; _previousTerrain = WorldTerrain.Active; }
    public void Dispose() => WorldTerrain.Active = _previousTerrain;

    [Fact]
    public void HowExpensiveIsAGroundSample()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        // Warm up the generated tables and any lazy caches.
        double warm = 0;
        for (int i = 0; i < 1000; i++) warm += WorldTerrain.GroundHeightAt(i * 3.0, i * 7.0);
        _o.WriteLine($"warm {warm:F0}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int n = 20000;
        double s = 0;
        var rng = new Random(7);
        for (int i = 0; i < n; i++)
        {
            double x = rng.NextDouble() * 20000 - 10000, y = rng.NextDouble() * 20000 - 10000;
            s += WorldTerrain.GroundHeightAt(x, y);
        }
        sw.Stop();
        double us = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
        _o.WriteLine($"GroundHeightAt: {us:F1} us per call over {n} random samples (sum {s:F0})");

        // And a realistic case: samples clustered near one aircraft, as a flight actually makes them.
        sw.Restart();
        s = 0;
        for (int i = 0; i < n; i++)
        {
            double x = 1000 + (i % 37) * 2.0, y = -8000 + (i % 53) * 2.0;
            s += WorldTerrain.GroundHeightAt(x, y);
        }
        sw.Stop();
        _o.WriteLine($"clustered: {sw.Elapsed.TotalMilliseconds * 1000.0 / n:F1} us per call (sum {s:F0})");
    }

    /// <summary>What does ONE aircraft-second of simulation cost on the island world, near the ground and high up?</summary>
    [Fact]
    public void WhatDoesOneAircraftSecondCost()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json"));

        foreach ((string where, double x, double y, double aglM) in new[]
                 {
                     ("on the runway at KSNA", -8060.0, 960.0, 2.0),
                     ("500 ft over KSNA", -8060.0, 960.0, 152.0),
                     ("5000 ft over mid-island", 0.0, 0.0, 1524.0),
                 })
        {
            double ground = WorldTerrain.GroundHeightAt(x, y);
            var st = new RigidBodyState(new Vec3(x, y, -(ground + aglM)), new Quat(0, 0, 0, 1), new Vec3(40, 0, 0), Vec3.Zero);
            var air = new Aircraft(cfg, st, ControlDeflections.Neutral);
            var loop = new SimLoop(air);
            var ci = new ControlInputs(0, 0, 0, 0.6);
            loop.RunFor(0.1, ci);                                  // settle and warm every lazy path
            var sw = System.Diagnostics.Stopwatch.StartNew();
            loop.RunFor(1.0, ci);
            sw.Stop();
            _o.WriteLine($"{where,-26} 1.0 s of sim took {sw.Elapsed.TotalMilliseconds,7:F1} ms of CPU "
                         + $"(ground {ground:F0} m, {(sw.Elapsed.TotalMilliseconds > 1000 ? "SLOWER THAN REAL TIME" : $"{1000 / sw.Elapsed.TotalMilliseconds:F1}x real time")})");
        }
    }

    /// <summary>How many contact points does a step sample the ground under?</summary>
    [Fact]
    public void HowManyGroundSamplesDoesAStepMake()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        foreach (string id in new[] { "c172-like", "p51d-like", "dc3-like" })
        {
            var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            int pts = AirframeContact.BuildPoints(cfg).Count;
            _o.WriteLine($"{id,-12} {pts,3} contact points + {cfg.Gear.Count} gear legs "
                         + $"-> about {pts * 2 + cfg.Gear.Count * 3} terrain/water queries per step");
        }
    }

    /// <summary>
    /// Isolate the terrain cost: the same aircraft-second, once moving (every height query is a fresh coordinate and
    /// misses the memo cache) and once stationary (every query repeats and hits it). The difference IS the height field.
    /// </summary>
    [Fact]
    public void HowMuchOfTheStepCostIsTheHeightField()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json"));
        double ground = WorldTerrain.GroundHeightAt(0, 0);

        double Run(double speed)
        {
            var st = new RigidBodyState(new Vec3(0, 0, -(ground + 1524)), new Quat(0, 0, 0, 1), new Vec3(speed, 0, 0), Vec3.Zero);
            var air = new Aircraft(cfg, st, ControlDeflections.Neutral);
            var loop = new SimLoop(air);
            var ci = new ControlInputs(0, 0, 0, 0.6);
            loop.RunFor(0.1, ci);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            loop.RunFor(1.0, ci);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        double moving = Run(40), frozen = Run(0);
        _o.WriteLine($"moving at 40 m/s : {moving,7:F1} ms of CPU per second of flight");
        _o.WriteLine($"stationary       : {frozen,7:F1} ms  (same code, but every height query repeats and hits the memo cache)");
        _o.WriteLine($"=> the island height field is {(moving - frozen) / moving * 100:F0} % of the simulation cost");
    }

    /// <summary>Decompose one force/moment evaluation: the aero pass against the contact pass (which is what queries terrain).</summary>
    [Fact]
    public void WhereDoesTheStepTimeActuallyGo()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json"));
        var tables = Aircraft.BuildAirfoilTables(cfg);
        var pts = AirframeContact.BuildPoints(cfg);
        double ground = WorldTerrain.GroundHeightAt(0, 0);
        var vb = new Vec3(40, 0, 2);
        var ctl = ControlDeflections.Neutral;
        const int n = 2000;

        // aero only
        AeroModel.Compute(cfg, tables, vb, Vec3.Zero, Vec3.Zero, 1.0, ctl);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < n; i++) AeroModel.Compute(cfg, tables, vb, Vec3.Zero, Vec3.Zero, 1.0, ctl);
        sw.Stop();
        double aeroUs = sw.Elapsed.TotalMilliseconds * 1000 / n;

        // contact only, at a fresh position each time so the terrain queries are real misses
        var cg = cfg.Mass.CgVec();
        AirframeContact.Compute(pts, cg, new RigidBodyState(new Vec3(0, 0, -(ground + 1524)), new Quat(0, 0, 0, 1), vb, Vec3.Zero), null);
        sw.Restart();
        for (int i = 0; i < n; i++)
        {
            var st = new RigidBodyState(new Vec3(i * 0.21, i * 0.17, -(ground + 1524)), new Quat(0, 0, 0, 1), vb, Vec3.Zero);
            AirframeContact.Compute(pts, cg, st, null);
        }
        sw.Stop();
        double contactUs = sw.Elapsed.TotalMilliseconds * 1000 / n;

        _o.WriteLine($"AeroModel.Compute      {aeroUs,8:F1} us per call");
        _o.WriteLine($"AirframeContact.Compute{contactUs,8:F1} us per call ({pts.Count} points, terrain queried per point)");
        // RK4 re-evaluates forces four times per step, and the default step is 200 Hz.
        double perSec = 4 * 200.0;
        _o.WriteLine($"at {perSec:F0} evaluations per second of flight: aero {aeroUs * perSec / 1000,7:F0} ms/s, contact {contactUs * perSec / 1000,7:F0} ms/s");
    }

    /// <summary>
    /// The one that must never regress. The footprint pre-test may only skip the per-point loop where the exact test
    /// would have found nothing touching, so sweep altitude down through the gate's own threshold — on a flat field,
    /// on Drake's deliberately vertical north cliff, over a canyon and over the sea, upright, banked and inverted —
    /// and the gated forces must equal the ungated ones every time. A shortcut that misses a contact is a far worse
    /// bug than a slow frame.
    /// </summary>
    [Fact]
    public void SkippingContactAtAltitudeNeverSkipsARealContact()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json"));
        var pts = AirframeContact.BuildPoints(cfg);
        var cg = cfg.Mass.CgVec();

        // SIM metres: x is north, y is east (world E,N km x 1000 with the axes swapped).
        (string where, double x, double y)[] places =
        {
            ("KSNA", -8060, 960), ("Drake north cliff", -4180, -4900), ("mid-island", 0, 0),
            ("Powell canyon", -5150, 4580), ("over the sea", -18000, 0),
        };
        int states = 0;
        foreach ((string where, double x, double y) in places)
        {
            double ground = WorldTerrain.GroundHeightAt(x, y);
            foreach (double agl in new[] { -5.0, 0.0, 0.5, 2.0, 5.0, 20.0, 55.0, 59.0, 61.0, 70.0, 200.0, 1000.0 })
                foreach (double rollDeg in new[] { 0.0, 45.0, 180.0 })
                {
                    double hr = rollDeg * Math.PI / 360.0;
                    var att = new Quat(Math.Sin(hr), 0, 0, Math.Cos(hr));
                    var st = new RigidBodyState(new Vec3(x, y, -(ground + agl)), att, new Vec3(30, 0, 0), Vec3.Zero);

                    (Vec3 gf, Vec3 gm) = AirframeContact.Compute(pts, cg, st, null);
                    (Vec3 ef, Vec3 em) = AirframeContact.ComputeEveryPoint(pts, cg, st, null);
                    states++;
                    Assert.Equal(ef.X, gf.X, 6);
                    Assert.Equal(ef.Y, gf.Y, 6);
                    Assert.Equal(ef.Z, gf.Z, 6);
                    Assert.Equal(em.X, gm.X, 6);
                    Assert.Equal(em.Y, gm.Y, 6);
                    Assert.Equal(em.Z, gm.Z, 6);
                }
        }
        _o.WriteLine($"{states} states across {places.Length} places, upright/banked/inverted: "
                     + "gated contact forces identical to the exact per-point test");
    }
}

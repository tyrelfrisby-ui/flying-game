using System.Diagnostics;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Ty on the Quest 3S, build a222bda, 2026-11-02: "slows to a crawl after a couple of minutes". The
/// device log says it is not time but HEIGHT: 72 fps / 7 ms main thread above ~100 m, ~20 fps / ~50 ms below
/// ~60 m, 8 fps / 115 ms standing on the ground at (6188, 5183). This times the sim at the positions in that log,
/// high and low, and each ground query the contact code makes. Timing is printed, not asserted.</summary>
[Collection("WorldTerrainActive")]
public sealed class QuestLowAltitudeProbe : IDisposable
{
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _o;
    public QuestLowAltitudeProbe(ITestOutputHelper o) { _o = o; }

    /// <summary>(x, y, ground-relative note) from /tmp/questlogs/perf-1410: where the frame rate fell.</summary>
    private static readonly (double x, double y, string where)[] Spots =
    {
        (5941, 2618, "spawn, fine at 437 m agl"),
        (5882, 4390, "on the ground, 18 fps"),
        (6371, 5303, "on the ground, 20 fps"),
        (6188, 5183, "on the ground, 8 fps"),
        (8544, 2673, "on the water, 62 fps"),
    };

    /// <summary>µs per call: warmed up, then the best of three runs of <paramref name="n"/> (other lanes' suites share this Mac).</summary>
    private static double Time(int n, Action<int> body)
    {
        for (int i = 0; i < n / 4; i++) body(i + 7 * n);
        double best = double.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) body(i + run * n);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000.0 / n);
        }
        return best;
    }

    [Fact]
    public void GroundQueryCostAtTheLoggedSpots()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            foreach (var (x, y, where) in Spots)
            {
                double g = WorldTerrain.GroundHeightAt(x, y), up = g + 1.0;
                // Fresh coordinates every call (as a moving contact point is): the exact-coordinate memo never hits.
                double jit = 0;
                const int n = 2000;
                double X(int i) => x + i * 1.7e-4;
                double h = Time(n, i => jit += WorldTerrain.GroundHeightAt(X(i), y));
                double wheel = Time(n, i => jit += WorldTerrain.WheelGroundHeightAt(X(i), y, up));
                double surf = Time(n, i => jit += (int)WorldTerrain.SurfaceAt(X(i), y));
                double surf3 = Time(n, i => jit += (int)WorldTerrain.SurfaceAt(X(i), y, up));
                double bump = Time(n, i => jit += WorldTerrain.MicroBumpAt(X(i), y));
                double water = Time(n, i => jit += FloatHydro.WaterSurfaceAt(X(i), y, up) ?? 0);
                double cav = Time(n, i => jit += IslandCavities.TryFloor(X(i), y, up, out _) ? 1 : 0);
                double deck = Time(n, i => jit += WorldDecks.DeckUnder(X(i), y, up, out _) ? 1 : 0);
                double solid = Time(n, i => jit += WorldSolids.Penetration(X(i), y, up)?.depth ?? 0);
                var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));
                var pts = AirframeContact.BuildPoints(cfg);
                RigidBodyState rest = LandingGear.RestingState(cfg, x, y, g);
                double every = Time(200, i => jit += AirframeContact.ComputeEveryPoint(pts, cfg.Mass.CgVec(),
                    new RigidBodyState(rest.Position + new Vec3(i * 1e-3, 0, 0), rest.Attitude, rest.Velocity, rest.Rates), null).Force.Z);
                _o.WriteLine($"  {pts.Count} hard points: every-point test {every:F1} µs ({every / pts.Count:F1} µs a point)");
                _o.WriteLine($"({x},{y}) {where}: ground {g:F0} m | height {h:F1} µs, wheelGround {wheel:F1}, surface {surf:F1}, surface@up {surf3:F1}, microBump {bump:F1}, " +
                             $"water {water:F1}, cavity {cav:F1}, decks {deck:F1}, solids {solid:F1} µs  ({jit:E1})");
            }
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>The Pitts flown at 500 m, 30 m and sitting on the ground at each logged spot, driven at 72 Hz the way
    /// FlightSimDriver drives it.</summary>
    [Fact]
    public void PittsSimSecondHighLowAndOnTheGround()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            var config = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));
            foreach (var (x, y, where) in Spots)
            {
                double g = WorldTerrain.GroundHeightAt(x, y);
                double water = FloatHydro.WaterSurfaceAt(x, y) ?? double.NegativeInfinity;
                double surface = Math.Max(g, water);
                foreach (double agl in new[] { 500.0, 100.0, 50.0, 30.0, 0.0 })
                {
                    RigidBodyState st = agl > 0
                        ? new RigidBodyState(new Vec3(x, y, -(surface + agl)), Quat.Identity, new Vec3(45, 0, 0), Vec3.Zero)
                        : LandingGear.RestingState(config, x, y, surface);
                    var loop = new SimLoop(new Aircraft(config, st, ControlDeflections.Neutral));
                    var input = agl > 0 ? new ControlInputs(0, -0.05, 0, -0.6) : new ControlInputs(0, 0, 0, -1.0);
                    double acc = 0;
                    for (int f = 0; f < 36; f++) loop.Advance(1.0 / 72.0, input, ref acc);
                    const int frames = 72 * 2;
                    var sw = Stopwatch.StartNew();
                    for (int f = 0; f < frames; f++) loop.Advance(1.0 / 72.0, input, ref acc);
                    double ms = sw.Elapsed.TotalMilliseconds / 2.0;
                    SimProfile.Reset(); SimProfile.Enabled = true;
                    for (int f = 0; f < frames; f++) loop.Advance(1.0 / 72.0, input, ref acc);
                    SimProfile.Enabled = false;
                    _o.WriteLine($"({x},{y}) {where}: agl {agl,4:F0} → {ms,7:F1} ms CPU per sim-second ({ms / 72.0:F2} ms per 72 Hz frame) | {SimProfile.Top(6, frames)}");
                }
            }
        }
        finally { WorldTerrain.Active = null; }
    }
}

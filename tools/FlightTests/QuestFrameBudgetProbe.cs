using System.Diagnostics;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Ty on the Quest 3S, 2026-11-02: "after a few minutes the frame rate gets really slow, catches up,
/// then slows again". The sim runs 200 steps a second on the render thread's CPU, so what a sim-second costs —
/// and above all what it ALLOCATES, which is what the headset's garbage collector has to come back for — is the
/// floor under every frame. This measures both for the aeroplane he was flying, on the island, and pins the
/// allocation: Unity's collector is non-generational, so steady garbage at 200 Hz turns into a collection every
/// few seconds that the incremental slices cannot hide inside a 13.9 ms frame.
///
/// Timing is printed, not asserted (it depends on the machine and on what else is running). Allocation is
/// deterministic and is asserted.</summary>
[Collection("WorldTerrainActive")]
public sealed class QuestFrameBudgetProbe : IDisposable
{
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _o;
    public QuestFrameBudgetProbe(ITestOutputHelper o) { _o = o; }

    /// <summary>Managed bytes per sim-second of ordinary flight, above which the probe fails: a guard against the
    /// sim getting worse, not a target. Measured 2026-11-02: 145.9 KiB (about 730 B a step — the RK4 force closure
    /// and its delegate, a LINQ Sum, the aero model; the island's ground, water and air queries allocate nothing).
    /// The Unity side was the bigger producer and was cut instead (docs/quest-performance.md).</summary>
    public const long MaxBytesPerSimSecond = 160 * 1024;

    /// <summary>Uniform air: the regional atmosphere config does not load against the island world here ("unknown
    /// airport anchor 'airport:Bench'"), so this measures the sim without the field's four samples a step.</summary>
    [Fact]
    public void PittsIslandSimSecondCostAndGarbage()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            var config = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));
            WorldTerrain.Airport ap = WorldTerrain.Airports[WorldTerrain.DefaultAirportIndex];
            IslandSites.Start? s = IslandRunways.Authored(ap.Name, "InTheAir");
            Assert.True(s.HasValue);
            double hdg = s.Value.HeadingTrueDeg * Math.PI / 180;
            var att = new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2));
            var state = new RigidBodyState(new Vec3(s.Value.X, s.Value.Y, -s.Value.AltitudeM), att,
                                           new Vec3(config.SpawnIasMs > 1 ? config.SpawnIasMs : 40, 0, 0), Vec3.Zero);
            var loop = new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
            var cruise = new ControlInputs(0, -0.05, 0, -0.6);
            double acc = 0;
            // Warm up (JIT, the atmosphere's first tiles), then measure the way FlightSimDriver drives it: Advance()
            // at the headset's 72 Hz.
            for (int f = 0; f < 72 * 5; f++) loop.Advance(1.0 / 72.0, cruise, ref acc);
            const int seconds = 20;
            var sw = Stopwatch.StartNew();
            long bytes0 = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0);
            for (int f = 0; f < 72 * seconds; f++) loop.Advance(1.0 / 72.0, cruise, ref acc);
            sw.Stop();
            long perSec = (GC.GetAllocatedBytesForCurrentThread() - bytes0) / seconds;
            double msPerSec = sw.Elapsed.TotalMilliseconds / seconds;
            _o.WriteLine($"pitts island: {msPerSec:F1} ms CPU per sim-second ({msPerSec / 72.0:F2} ms per 72 Hz frame), " +
                         $"{perSec / 1024.0:F1} KiB allocated per sim-second ({perSec / 72.0:F0} B per frame), gen0 GCs {GC.CollectionCount(0) - gen0}");
            Assert.False(double.IsNaN(loop.Aircraft.State.Position.Z));
            Assert.True(perSec <= MaxBytesPerSimSecond,
                $"the sim allocates {perSec / 1024.0:F1} KiB per sim-second (limit {MaxBytesPerSimSecond / 1024} KiB): on the Quest that is a garbage collection every few seconds");
        }
        finally { WorldTerrain.Active = null; }
    }
}

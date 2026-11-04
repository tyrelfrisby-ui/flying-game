using System.Diagnostics;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Ty, iOS, 2026-11-02 (t256u): "it still slows down when it gets close to the ground and it makes it hard to
/// land". The player's sim timed engine-free, the way FlightSimDriver drives it on the phone (one SimLoop.Advance per
/// 60 Hz frame, 200 Hz steps), from 500 m down to standing on the ground, for three types at the spots where the
/// Quest log showed the slowdown. Timing is printed, not asserted; it uses nothing newer than the island-nav lane, so
/// the same file times the code before and after the ground-contact fix.</summary>
[Collection("WorldTerrainActive")]
public sealed class IosLowAltitudeProbe : IDisposable
{
    public void Dispose() => WorldTerrain.Active = null;
    private readonly ITestOutputHelper _o;
    public IosLowAltitudeProbe(ITestOutputHelper o) { _o = o; }

    private static readonly (double x, double y)[] Spots = { (5941, 2618), (5882, 4390), (6188, 5183) };
    private static readonly string[] Types = { "pitts-s2b-like.json", "glider-2-33-like.json", "c172-like.json" };
    private static readonly double[] Agls = { 500, 100, 60, 30, 10, 3, 0 };

    [Fact]
    public void SimMsPerFrameAgainstHeight()
    {
        WorldTerrain.Active = new WorldTerrain();
        IslandGeometry.Build();   // the island's ~1,500 solids, as IslandWorldBuilder registers them on the device
        try
        {
            foreach (string type in Types)
            {
                var config = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", type));
                double speed = type.Contains("glider") ? 28 : 40;
                var row = new System.Text.StringBuilder($"{type,-24}");
                foreach (double agl in Agls)
                {
                    double worst = 0, sum = 0;
                    foreach (var (x, y) in Spots)
                    {
                        double g = WorldTerrain.GroundHeightAt(x, y);
                        double surface = Math.Max(g, FloatHydro.WaterSurfaceAt(x, y) ?? double.NegativeInfinity);
                        double best = double.MaxValue;
                        for (int run = 0; run < 3; run++)
                        {
                            RigidBodyState st = agl > 0
                                ? new RigidBodyState(new Vec3(x, y, -(surface + agl)), Quat.Identity, new Vec3(speed, 0, 0), Vec3.Zero)
                                : LandingGear.RestingState(config, x, y, surface);
                            var loop = new SimLoop(new Aircraft(config, st, ControlDeflections.Neutral));
                            var input = agl > 0 ? new ControlInputs(0, -0.05, 0, -0.6) : new ControlInputs(0, 0, 0, -1.0);
                            double acc = 0;
                            for (int f = 0; f < 20; f++) loop.Advance(1.0 / 60.0, input, ref acc);
                            const int frames = 30;   // half a second: low runs stay low
                            var sw = Stopwatch.StartNew();
                            for (int f = 0; f < frames; f++) loop.Advance(1.0 / 60.0, input, ref acc);
                            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / frames);
                        }
                        worst = Math.Max(worst, best); sum += best;
                    }
                    row.Append($" | {agl,3:F0} m {sum / Spots.Length,5:F2} ({worst:F2})");
                }
                _o.WriteLine(row.ToString());
            }
            _o.WriteLine("ms of sim CPU per 60 Hz frame, mean over the spots (worst spot), Mac.");
        }
        finally { WorldTerrain.Active = null; }
    }
}

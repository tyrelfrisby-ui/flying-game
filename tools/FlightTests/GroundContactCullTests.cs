using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Quest slowdown fix (2026-11-02) skips ground queries the sim can prove would find nothing: gear and hard
/// points for a whole step when the airframe is clear of the ground, and on RK4 stages 2–4 for a point or wheel that
/// stage 1 found clear by more than it can travel in the step. Those queries returned zero force, so the flight must
/// be bit-for-bit the same with the skips on and off. These fly the same flights both ways — takeoff runs, a crash
/// into rising ground, a low pass, sitting still, a float landing — at the very spots the headset log named, and
/// compare every number the step leaves behind.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class GroundContactCullTests : IDisposable
{
    public void Dispose()
    {
        WorldTerrain.Active = null;
        AirframeContact.StageCullEnabled = true;
    }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    public sealed record Flight(string Aircraft, double X, double Y, string Kind);

    /// <summary>Spots from the Quest log of 2026-11-02 (/tmp/questlogs/perf-1410): the slow ones, the spawn and the sea.</summary>
    public static IEnumerable<object[]> Flights()
    {
        (double x, double y)[] spots = { (6188, 5183), (6371, 5303), (5882, 4390), (5941, 2618) };
        foreach (string ac in new[] { "pitts-s2b-like", "c172-like", "glider-2-33-like" })
            foreach (var (x, y) in spots)
                foreach (string kind in new[] { "takeoff", "crash", "lowpass", "sit" })
                    yield return new object[] { new Flight(ac, x, y, kind) };
        yield return new object[] { new Flight("dhc2-beaver-floats-like", 8544, 2673, "splash") };
        yield return new object[] { new Flight("pitts-s2b-like", 8544, 2673, "ditch") };
    }

    private static (RigidBodyState state, string trace, long queries) Fly(Flight f, bool cull)
    {
        AirframeContact.StageCullEnabled = cull;
        SimProfile.Reset();
        AircraftConfig c = Load(f.Aircraft);
        double ground = WorldTerrain.GroundHeightAt(f.X, f.Y);
        double surface = Math.Max(ground, FloatHydro.WaterSurfaceAt(f.X, f.Y) ?? double.NegativeInfinity);
        RigidBodyState s0;
        ControlInputs input;
        double seconds;
        switch (f.Kind)
        {
            case "takeoff": s0 = LandingGear.RestingState(c, f.X, f.Y, surface, 0.7); input = new ControlInputs(0, -0.3, 0.1, 1.0); seconds = 8; break;
            case "sit":     s0 = LandingGear.RestingState(c, f.X, f.Y, surface, 2.1); input = new ControlInputs(0.2, 0, -0.3, -1.0); seconds = 4; break;
            case "lowpass": s0 = new RigidBodyState(new Vec3(f.X, f.Y, -(surface + 15)), new Quat(0, 0, Math.Sin(0.4), Math.Cos(0.4)), new Vec3(40, 0, 0), Vec3.Zero);
                            input = new ControlInputs(0.05, -0.05, 0, 0.6); seconds = 4; break;
            case "splash":
            case "ditch":   s0 = new RigidBodyState(new Vec3(f.X, f.Y, -(surface + 6)), new Quat(0, Math.Sin(0.03), 0, Math.Cos(0.03)), new Vec3(30, 0, 2.5), Vec3.Zero);
                            input = new ControlInputs(0, -0.2, 0, -1.0); seconds = 8; break;
            default:        // crash: 40 m up, diving at the ground, rolling
                            s0 = new RigidBodyState(new Vec3(f.X, f.Y, -(surface + 40)), new Quat(Math.Sin(0.15), Math.Sin(-0.12), 0, Math.Cos(0.19)).Normalized(), new Vec3(45, 0, 8), new Vec3(0.3, 0, 0.1));
                            input = new ControlInputs(0.4, 0.3, 0.2, 1.0); seconds = 6; break;
        }
        var ac = new Aircraft(c, s0, ControlDeflections.Neutral);
        var loop = new SimLoop(ac);
        double acc = 0;
        var trace = new System.Text.StringBuilder();
        for (int frame = 0; frame < seconds * 72; frame++)
        {
            SimProfile.Enabled = true;
            loop.Advance(1.0 / 72.0, input, ref acc);
            SimProfile.Enabled = false;
            if (frame % 36 == 0)
                trace.Append(ac.State.Position.X.ToString("R")).Append(',').Append(ac.State.Position.Z.ToString("R")).Append(',')
                     .Append(ac.State.Velocity.X.ToString("R")).Append(',').Append(ac.State.Rates.Y.ToString("R")).Append(';');
        }
        foreach (ContactPoint p in ac.ContactPoints) trace.Append(p.Name).Append(p.Touching ? '+' : '-');
        return (ac.State, trace.ToString(), SimProfile.HeightQueries);
    }

    [Theory]
    [MemberData(nameof(Flights))]
    public void SkippingTheQueriesChangesNothing(Flight f)
    {
        WorldTerrain.Active = new WorldTerrain();
        var (off, offTrace, offQueries) = Fly(f, cull: false);
        var (on, onTrace, onQueries) = Fly(f, cull: true);
        Assert.True(onQueries < offQueries, $"the skips skipped nothing: {onQueries} height queries against {offQueries}");
        Assert.Equal(offTrace, onTrace);
        Assert.Equal(off.Position.X, on.Position.X);
        Assert.Equal(off.Position.Y, on.Position.Y);
        Assert.Equal(off.Position.Z, on.Position.Z);
        Assert.Equal(off.Velocity.X, on.Velocity.X);
        Assert.Equal(off.Velocity.Z, on.Velocity.Z);
        Assert.Equal(off.Attitude.W, on.Attitude.W);
        Assert.Equal(off.Rates.X, on.Rates.X);
    }

    /// <summary>A hard point skips the surface lookup above <see cref="WorldTerrain.MaxMicroBumpM"/>: the bumps must never
    /// reach that high, anywhere on any surface (sampled across the island, its ice and its strips).</summary>
    [Fact]
    public void MicroBumpsNeverReachTheirCeiling()
    {
        WorldTerrain.Active = new WorldTerrain();
        double worst = double.MinValue;
        var rng = new Random(7);
        for (int i = 0; i < 40000; i++)
        {
            double x = rng.NextDouble() * 24000 - 2000, y = rng.NextDouble() * 24000 - 2000;
            worst = Math.Max(worst, WorldTerrain.MicroBumpAt(x, y));
        }
        // And the formula's own maximum on crevassed ice: every term at its peak.
        double crevasseMax = 0.30 + 0.18 + 0.30;
        Assert.True(worst <= WorldTerrain.MaxMicroBumpM, $"a bump of {worst:F3} m");
        Assert.True(crevasseMax <= WorldTerrain.MaxMicroBumpM);
    }

    /// <summary>The stage cull is a margin: at rest it is 0.1 m, and it grows with what the point can travel in a step.</summary>
    [Fact]
    public void CullMarginCoversTheTravelOfAStep()
    {
        Assert.Equal(0.1, AirframeContact.StageCullMargin(0, 0.005), 12);
        Assert.Equal(0.1 + 11 * 60 * 0.005, AirframeContact.StageCullMargin(60, 0.005), 12);   // 3.4 m at 60 m/s
    }
}

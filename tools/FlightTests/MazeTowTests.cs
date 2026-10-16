using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The tow through the maze, FLOWN. An autopilot that steers a tug down a tunnel is the kind of thing that
/// looks right in the code and puts a wing into the rock at the first bend, so these tests fly it with the
/// real flight model and measure the clearance — which is the only way to know without a device.
/// </summary>
public class MazeTowTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public MazeTowTests(ITestOutputHelper o)
    {
        _o = o;
        _t = new WorldTerrain();
        WorldTerrain.Active = _t;
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
        _t.RegisterWaterfallSolids();
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    public void Dispose()
    {
        WorldTerrain.Active = null;
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    private readonly WorldTerrain _t;

    private static AircraftConfig Cub() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    private static int IndexOf(WaterfallTunnel.Leg[] legs, string id) => Array.FindIndex(legs, l => l.Id == id);

    /// <summary>Fly the tug from the start of a leg, with the rope held wherever the glider pilot is holding
    /// it, and report what it did. No rope physics here: this is the tug's own flying, which is the half that
    /// can put it into a wall.</summary>
    private (MazeTugPilot pilot, double worstClearance, string worstLeg, bool hitRock, double seconds)
        Fly(string startLegId, double ropeLateral, double seconds = 340)
    {
        WorldTerrain.Waterfall f = WaterfallTunnel.Fall(_t);
        WaterfallTunnel.Leg[] legs = WaterfallTunnel.Legs(f);
        int start = IndexOf(legs, startLegId);
        WaterfallTunnel.Leg l0 = legs[start];

        // On the floor at the start of the leg, lined up, at rest — as it would be after the hook-up.
        double hdg = Math.Atan2(l0.Y1 - l0.Y0, l0.X1 - l0.X0);
        var state = LandingGear.RestingState(Cub(), l0.X0 + (l0.X1 - l0.X0) * 0.02, l0.Y0 + (l0.Y1 - l0.Y0) * 0.02,
                                             l0.Z0, hdg);
        var tug = new Aircraft(Cub(), state, new ControlDeflections(0, 0, 0, 0));
        var pilot = new MazeTugPilot(legs, start) { RopeLateralBody = ropeLateral };
        var loop = new SimLoop(tug);

        double worst = double.MaxValue, t = 0;
        string worstLeg = "";
        bool hit = false;
        const double dt = SimLoop.DefaultFixedDtSec;
        while (t < seconds && !pilot.Done)
        {
            ControlInputs ci = pilot.Update(tug, dt);
            loop.RunFor(dt, ci);
            t += dt;
            Vec3 p = tug.State.Position;
            if (double.IsNaN(p.X)) { hit = true; worstLeg = "NaN"; break; }
            if (!pilot.Airborne) continue;
            // Clear of the floor before clearance means anything: during the rotation a wing tip is a metre
            // off the ground by design, and that is the gear's business, not the arch's.
            if (-p.Z < WaterfallTunnel.FloorAt(f, p.X, p.Y) + 6) continue;

            // Clearance: how far the wing tips are from the arch, measured through the maze's own geometry.
            double span = 0;
            foreach (SurfaceConfig sf in tug.Config.Surfaces)
                if (sf.Id == "wing")
                    foreach (StripConfig stp in sf.Strips) span = Math.Max(span, Math.Abs(stp.Pos[1]));
            for (int k = -1; k <= 1; k += 2)
            {
                Vec3 tip = p + tug.State.Attitude.Rotate(new Vec3(0, k * span, 0));
                double ceil = WaterfallTunnel.CeilingAt(f, tip.X, tip.Y);
                double floor = WaterfallTunnel.FloorAt(f, tip.X, tip.Y);
                double upTip = -tip.Z;
                double clear = Math.Min(ceil - upTip, upTip - floor);
                if (!WaterfallTunnel.InFootprint(f, tip.X, tip.Y)) clear = -1;    // outside the bore entirely
                if (clear < worst) { worst = clear; worstLeg = pilot.Took[^1]; }
                if (clear < 0) hit = true;
            }
        }
        _o.WriteLine($"rope {ropeLateral,5:0.00}: {t,6:0.0} s, {pilot.Took.Count,2} legs, worst clearance {worst,6:0.0} m on {worstLeg}, done={pilot.Done}");
        _o.WriteLine($"  route: {string.Join(" → ", pilot.Took)}");
        return (pilot, worst, worstLeg, hit, t);
    }

    [Fact]
    public void TheTugTakesOffInsideTheTunnel()
    {
        // The first thing it has to do is get airborne in a bore 32 m tall, from a standing start, without
        // putting a wing into the springing of the arch.
        var (pilot, worst, worstLeg, hit, t) = Fly("S0.glider", 0.0, 40);
        Assert.True(pilot.Airborne, $"the tug never got airborne (status '{pilot.Status}')");
        Assert.False(hit, $"it hit the rock on {worstLeg}");
        Assert.True(worst > 4, $"only {worst:0.0} m of clearance on {worstLeg}");
        _ = t;
    }

    [Fact]
    public void ACentredGliderIsTowedRoundWithoutTouchingTheRock()
    {
        // The whole point: a glider pilot who does nothing gets taken round and out.
        var (pilot, worst, worstLeg, hit, _) = Fly("S0.glider", 0.0);
        Assert.False(hit, $"the tow hit the rock on {worstLeg} (clearance {worst:0.0} m)");
        Assert.True(worst > 4, $"only {worst:0.0} m of clearance on {worstLeg}");
        Assert.DoesNotContain("D.slit", pilot.Took);
        Assert.True(pilot.Took.Count > 40, $"it only flew {pilot.Took.Count} legs — it did not get round");
        Assert.Contains("exit.bore", pilot.Took);          // out through the mouth, which is the whole point
    }

    [Fact]
    public void TheGliderPilotCanAskForADifferentBranch()
    {
        // Holding the rope out to one side has to actually change where the tow goes — and the other way
        // round has to change it the other way.
        var left = Fly("S0.glider", +0.5, 120).pilot;
        var right = Fly("S0.glider", -0.5, 120).pilot;
        Assert.NotEqual(left.Took[1], right.Took[1]);
    }

    [Fact]
    public void TheTowNeverFliesTheSlitHoweverTheRopeIsHeld()
    {
        // The one refusal (owner's call): no tow pair can fly a knife edge.
        foreach (double rope in new[] { -0.8, -0.3, 0.0, 0.3, 0.8 })
        {
            var pilot = Fly("B2.west", rope, 120).pilot;
            Assert.DoesNotContain("D.slit", pilot.Took);
        }
    }
}

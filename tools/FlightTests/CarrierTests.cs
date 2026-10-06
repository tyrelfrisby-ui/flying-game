using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The aircraft carrier (owner 2026-10-03): steaming offshore at 18 kt on a racetrack, a moving deck that
/// carries what lands on it, the hull and island solid.</summary>
[Collection("WorldTerrainActive")]
public class CarrierTests
{
    private readonly ITestOutputHelper _o;
    public CarrierTests(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void SteamsAt18KnotsSmoothlyRoundItsTrackInDeepWaterOffshore()
    {
        var t = new WorldTerrain();
        double lap = Carrier.LapM / Carrier.SpeedMs, minClear = double.MaxValue, maxFloor = double.MinValue;
        var prev = Carrier.PoseAt(0);
        for (double s = 1; s <= lap + 1; s += 1)
        {
            var p = Carrier.PoseAt(s);
            double step = System.Math.Sqrt((p.x - prev.x) * (p.x - prev.x) + (p.y - prev.y) * (p.y - prev.y));
            Assert.InRange(step, Carrier.SpeedMs * 0.97, Carrier.SpeedMs * 1.001);   // no jumps (a chord in the turns)
            prev = p;
            // The whole footprint (deck corners and the hull's sides) over deep water, clear of the shore.
            foreach (double u in new[] { -Carrier.HalfLengthM, 0, Carrier.HalfLengthM })
                foreach (double v in new[] { -Carrier.DeckPortM, Carrier.DeckStarboardM })
                {
                    double c = System.Math.Cos(p.heading), sn = System.Math.Sin(p.heading);
                    double x = p.x + u * c - v * sn, y = p.y + u * sn + v * c;
                    minClear = System.Math.Min(minClear, y - Coast.ShoreY(x));
                    maxFloor = System.Math.Max(maxFloor, t.HeightAt(x, y));
                }
        }
        _o.WriteLine($"lap {Carrier.LapM:F0} m = {lap / 60:F1} min; nearest the shore {minClear:F0} m; shallowest {-maxFloor:F0} m");
        Assert.True(minClear > 200, $"stays offshore ({minClear:F0} m)");
        Assert.True(maxFloor < -Carrier.DraftM - 3, $"deep water under the keel ({maxFloor:F1} m)");
        Assert.Equal(18.0, Carrier.SpeedMs / 0.514444, 6);
    }

    [Fact]
    public void TheDeckIsWhereTheShipIsAndMovesWithIt()
    {
        try
        {
            WorldDecks.All.Clear(); WorldSolids.Shapes.Clear();
            Carrier.Register();
            foreach (double time in new[] { 30.0, Carrier.LapM / Carrier.SpeedMs * 0.3, 700.0 })
            {
                WorldClock.TimeS = time;
                var p = Carrier.PoseAt(time);
                Assert.True(WorldDecks.DeckUnder(p.x, p.y, Carrier.DeckTopM + 1, out double top));
                Assert.Equal(Carrier.DeckTopM, top, 6);
                Assert.False(WorldDecks.DeckUnder(p.x, p.y, Carrier.DeckTopM - 10, out _));   // below the deck isn't on it
                Vec3 v = WorldDecks.SurfaceVelocity(p.x, p.y, Carrier.DeckTopM);
                Assert.Equal(Carrier.SpeedMs, v.Length, 6);
                Assert.Equal(p.heading, System.Math.Atan2(v.Y, v.X) + (System.Math.Atan2(v.Y, v.X) < -1e-9 ? 2 * System.Math.PI : 0), 6);
                // Solid: the hull at the waterline, the island; not the air above the deck.
                Assert.NotNull(WorldSolids.Penetration(p.x, p.y, 5));
                double iu = (Carrier.IslandU0 + Carrier.IslandU1) / 2, iv = (Carrier.IslandV0 + Carrier.IslandV1) / 2, c = System.Math.Cos(p.heading), sn = System.Math.Sin(p.heading);
                Assert.NotNull(WorldSolids.Penetration(p.x + iu * c - iv * sn, p.y + iu * sn + iv * c, Carrier.DeckTopM + 20));
                Assert.Null(WorldSolids.Penetration(p.x, p.y, Carrier.DeckTopM + 5));
            }
        }
        finally { WorldDecks.All.Clear(); WorldSolids.Shapes.Clear(); WorldClock.TimeS = 0; }
    }

    [Theory]
    [InlineData(20.0)]                                  // a straight
    [InlineData(-1.0)]                                  // across a turn (start just before one)
    public void AnAircraftParkedOnTheDeckIsCarriedAlong(double startS)
    {
        if (startS < 0) startS = (Carrier.TrackX1 - Carrier.TrackX0) / Carrier.SpeedMs - 10;
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        var t = new WorldTerrain();
        try
        {
            WorldTerrain.Active = t; WorldDecks.All.Clear(); WorldSolids.Shapes.Clear();
            Carrier.Register();
            WorldClock.TimeS = startS;
            var p = Carrier.PoseAt(startS);
            // Three-point on the deck centre, nose along the ship's heading, moving with the deck.
            var mains = c.Gear.FindAll(g => !g.IsTailwheel); var tw = c.Gear.Find(g => g.IsTailwheel)!;
            double pitch = System.Math.Atan((mains[0].Pos[2] - tw.Pos[2]) / (mains[0].Pos[0] - tw.Pos[0]));
            Quat yaw = new(0, 0, System.Math.Sin(p.heading / 2), System.Math.Cos(p.heading / 2)), pq = new(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
            Quat att = Quat.Multiply(yaw, pq);
            double maxWz = -999; foreach (var g in c.Gear) maxWz = System.Math.Max(maxWz, att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
            Vec3 vDeck = Carrier.DeckVelocityAt(p.x, p.y, startS);
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(p.x, p.y, -(Carrier.DeckTopM + maxWz + 0.01)), att, att.Conjugate().Rotate(vDeck), Vec3.Zero), ControlDeflections.Neutral);
            ac.BrakeInput = 1.0;
            var sim = new SimLoop(ac);
            double dur = 30, maxSlip = 0;
            for (double tt = 0; tt < dur; tt += 0.02)
            {
                WorldClock.TimeS = startS + tt + 0.02;
                sim.RunFor(0.02, new ControlInputs(0, -0.4, 0, 1.0));   // idle (lever full aft), brakes set
                var (u, v) = Carrier.ToLocal(ac.State.Position.X, ac.State.Position.Y, WorldClock.TimeS);
                maxSlip = System.Math.Max(maxSlip, System.Math.Sqrt(u * u + v * v));
                if (System.Environment.GetEnvironmentVariable("CVTRACE") != null && ((int)System.Math.Round(tt / 0.02)) % 25 == 0) _o.WriteLine($"t={tt:F1} u={u:F1} v={v:F1} h={-ac.State.Position.Z:F2} vrel={(ac.State.Attitude.Rotate(ac.State.Velocity) - Carrier.DeckVelocityAt(ac.State.Position.X, ac.State.Position.Y, WorldClock.TimeS)).Length:F2}");
            }
            var q = Carrier.PoseAt(startS + dur);
            double moved = System.Math.Sqrt((ac.State.Position.X - p.x) * (ac.State.Position.X - p.x) + (ac.State.Position.Y - p.y) * (ac.State.Position.Y - p.y));
            _o.WriteLine($"start {startS:F0} s: carried {moved:F0} m in {dur} s (ship {Carrier.SpeedMs * dur:F0} m along its track); furthest from the deck centre {maxSlip:F1} m; height {-ac.State.Position.Z:F2} m; lost [{string.Join(',', ac.LostComponents)}]");
            Assert.True(maxSlip < 15, $"stays put on the deck (drifted {maxSlip:F1} m)");
            Assert.InRange(-ac.State.Position.Z, Carrier.DeckTopM, Carrier.DeckTopM + 3);
            Assert.Empty(ac.LostComponents);
        }
        finally { WorldTerrain.Active = null; WorldDecks.All.Clear(); WorldSolids.Shapes.Clear(); WorldClock.TimeS = 0; }
    }
}

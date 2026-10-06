using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The airframe itself (not just the wheels) contacts the ground: an inverted aircraft rests on its
/// fin and wing tips; a hard wing-tip or tail strike snaps that component off, its aero disappears, the rest
/// keeps flying with unchanged mass/inertia.</summary>
[Collection("WorldTerrainActive")]
public class AirframeContactTests
{
    private readonly ITestOutputHelper _out;
    public AirframeContactTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Cub() => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    [Fact]
    public void InvertedAircraftRestsOnFinAndWingTipsNotItsTyres()
    {
        var c = Cub();
        WorldTerrain.Active = null;   // flat ground at 0
        // Upside down in its resting attitude (fin top and wing skin level), lowest point 5 cm up: a gentle settle.
        var roll180 = new Quat(1, 0, 0, 0);
        Quat att = roll180; double best = double.MaxValue;
        foreach (double th in new[] { -0.2, -0.15, -0.1, -0.05, 0.0, 0.05, 0.1, 0.15, 0.2 })
        {
            var cand = Quat.Multiply(roll180, new Quat(0, System.Math.Sin(th / 2), 0, System.Math.Cos(th / 2)));
            double lowest = double.MaxValue, second = double.MaxValue;
            var pts = AirframeContact.BuildPoints(c);
            foreach (var p in pts) { double up = -cand.Rotate(p.Body - c.Mass.CgVec()).Z; if (up < lowest) { second = lowest; lowest = up; } else if (up < second) second = up; }
            // Prefer the attitude where the two lowest points are level (a stable rest on wing + fin).
            double spread = second - lowest;
            if (spread < best) { best = spread; att = cand; }
        }
        double lowestUp = double.MaxValue;
        foreach (var p in AirframeContact.BuildPoints(c)) lowestUp = System.Math.Min(lowestUp, -att.Rotate(p.Body - c.Mass.CgVec()).Z);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, lowestUp - 0.05), att, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        double minPointClearance = 9;
        for (double t = 0; t < 12; t += 0.05)
        {
            sim.RunFor(0.05, new ControlInputs(0, 0, 0, 1.0));
            foreach (var p in ac.ContactPoints)
            {
                Vec3 w = ac.State.Position + ac.State.Attitude.Rotate(p.Body - c.Mass.CgVec());
                minPointClearance = System.Math.Min(minPointClearance, -w.Z);
            }
        }
        double cgHeight = -ac.State.Position.Z;
        _out.WriteLine($"inverted rest: CG {cgHeight:F2} m above ground, deepest point {minPointClearance:F2} m, speed {ac.State.Velocity.Length:F2}");
        Assert.True(minPointClearance > -0.25, $"airframe pushed into the ground by {-minPointClearance:F2} m");
        Assert.InRange(cgHeight, 0.6, 2.0);   // resting on the cabin top / fin, wheels in the air
        Assert.True(ac.State.Velocity.Length < 0.5, "should come to rest");
        _out.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
        Assert.Empty(ac.LostComponents);        // a gentle settle breaks nothing
    }

    [Fact]
    public void HardWingTipStrikeSnapsTheWingOffAndAeroFollows()
    {
        var c = Cub();
        WorldTerrain.Active = null;
        var tables = Aircraft.BuildAirfoilTables(c);
        // Banked 60° right, dropping at 6 m/s from 2.5 m: the right tip hits first, hard.
        double roll = 60 * System.Math.PI / 180;
        var att = new Quat(System.Math.Sin(roll / 2), 0, 0, System.Math.Cos(roll / 2));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -2.5), att, att.Conjugate().Rotate(new Vec3(0, 0, 6.0)), Vec3.Zero), ControlDeflections.Neutral);
        var lost = new List<AirframeComponent>(); ac.ComponentLost += lost.Add;
        var sim = new SimLoop(ac);
        for (double t = 0; t < 3 && lost.Count == 0; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
        _out.WriteLine("lost: " + string.Join(", ", lost));
        // A tip strike takes the OUTER panel only (owner 2026-09-10: two panels per wing); the whole wing stays.
        Assert.Contains(AirframeComponent.WingRightOuter, lost);
        Assert.True(ac.IsLost(AirframeComponent.WingRightOuter));
        Assert.False(ac.IsLost(AirframeComponent.WingRightInner));
        Assert.False(ac.IsLost(AirframeComponent.WingRight));
        Assert.False(ac.IsLost(AirframeComponent.WingLeftOuter));
        // Aero with the mask: level flight at 30 m/s now rolls hard LEFT (only the left wing lifts) — the
        // remaining half still produces lift (the aircraft can be flown on), mass is untouched.
        var vel = new Vec3(30 * System.Math.Cos(0.07), 0, 30 * System.Math.Sin(0.07));
        (Vec3 fIntact, Vec3 mIntact) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral);
        (Vec3 fHalf, Vec3 mHalf) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral, -1.0, null, 0, 0, null, ac.StripMask);
        _out.WriteLine($"lift intact {-fIntact.Z:F0} N, half {-fHalf.Z:F0} N; roll moment intact {mIntact.X:F0}, half {mHalf.X:F0} N·m");
        // Only the LEFT wing lifts → it rises → the aircraft rolls RIGHT, toward the missing wing (+L in NED).
        Assert.True(mHalf.X > 1500, $"losing the right wing must roll it toward the stub (L = {mHalf.X:F0})");
        Assert.InRange(-fHalf.Z / -fIntact.Z, 0.6, 0.95);   // outer panel only (owner: two panels per wing)
        Assert.Equal(c.Mass.MassKg, ac.MassProperties.MassKg, 3);
    }

    [Fact]
    public void LosingTheTailRemovesPitchStability()
    {
        var c = Cub();
        var tables = Aircraft.BuildAirfoilTables(c);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -500), new Quat(0, 0, 0, 1), new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        ac.LoseComponent(AirframeComponent.TailHorizontal);
        double Cm(double a, bool[]? mask)
        {
            var vel = new Vec3(30 * System.Math.Cos(a), 0, 30 * System.Math.Sin(a));
            (_, Vec3 m) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral, -1.0, null, 0, 0, null, mask);
            return m.Y;
        }
        double dMdaIntact = (Cm(0.1, null) - Cm(0.0, null)) / 0.1, dMdaNoTail = (Cm(0.1, ac.StripMask) - Cm(0.0, ac.StripMask)) / 0.1;
        _out.WriteLine($"dM/dα intact {dMdaIntact:F0}, without the stabiliser {dMdaNoTail:F0} N·m/rad");
        Assert.True(dMdaIntact < 0);
        Assert.True(dMdaNoTail > dMdaIntact + 0.5 * System.Math.Abs(dMdaIntact), "the stabiliser's restoring moment must be gone");
    }

    [Fact]
    public void GentleFlightAndNormalLandingsBreakNothing()
    {
        var c = Cub();
        var t = new WorldTerrain(); WorldTerrain.Active = t;
        var a = WorldTerrain.Airports[0];
        var mains = c.Gear.FindAll(g => !g.IsTailwheel); var tw = c.Gear.Find(g => g.IsTailwheel)!;
        double pitch = System.Math.Atan((mains[0].Pos[2] - tw.Pos[2]) / (mains[0].Pos[0] - tw.Pos[0]));
        var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
        double maxWz = -999; foreach (var g in c.Gear) maxWz = System.Math.Max(maxWz, att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
        // Three-point touchdown at 15 m/s sinking 1.5 m/s on the runway.
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(a.X - 500, a.Y, -(a.ElevationM + maxWz + 0.3)), att, att.Conjugate().Rotate(new Vec3(15, 0, 1.5)), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        for (double tt = 0; tt < 8; tt += 0.02) sim.RunFor(0.02, new ControlInputs(0, -0.3, 0, 1.0));
        WorldTerrain.Active = null;
        Assert.Empty(ac.LostComponents);
        Assert.True(-ac.State.Position.Z - a.ElevationM < 2.0);
    }

    [Fact]
    public void PropStrikeStopsTheEngine()
    {
        var c = Cub();
        WorldTerrain.Active = null;
        // Nose-over: pitched 25° nose-down on the ground rolling at 8 m/s with power on — the prop bites the runway.
        double pitch = -25 * System.Math.PI / 180;
        var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1.2), att, new Vec3(8, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var lost = new List<AirframeComponent>(); ac.ComponentLost += lost.Add;
        var sim = new SimLoop(ac);
        for (double t = 0; t < 2 && !ac.EngineStopped; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, 0, 0, -1.0));
        Assert.Contains(AirframeComponent.Propeller, lost);
        Assert.True(ac.EngineStopped);
        Assert.Equal(0.0, ac.EngineRpm);
        sim.RunFor(0.5, new ControlInputs(0, 0, 0, -1.0));
        Assert.True(ac.State.Velocity.Length < 8.5, "no thrust after the strike");
    }
}

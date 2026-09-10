using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Owner 2026-09-09: a hard touchdown tears off the gear leg that took it (individually), and the fuselage
/// is three sections — a tail-cone strike snaps the tail boom, a mid-fuselage slam breaks both ends off.</summary>
public class BreakupTests
{
    private readonly ITestOutputHelper _out;
    public BreakupTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Cub() => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    /// <summary>Drop the aircraft from rest at the given attitude with the lowest hard point/wheel <paramref name="clearance"/> m up and a vertical speed.</summary>
    private static Aircraft Drop(AircraftConfig c, Quat att, double sinkMs, double clearance, double forwardMs = 0, double rollRateRad = 0)
    {
        WorldTerrain.Active = null;
        double lowest = double.MaxValue;
        foreach (var p in AirframeContact.BuildPoints(c)) lowest = System.Math.Min(lowest, -att.Rotate(p.Body - c.Mass.CgVec()).Z);
        foreach (GearConfig g in c.Gear) lowest = System.Math.Min(lowest, -att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
        var vBody = att.Conjugate().Rotate(new Vec3(forwardMs, 0, sinkMs));
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, lowest - clearance), att, vBody, new Vec3(rollRateRad, 0, 0)), ControlDeflections.Neutral);
    }

    private static void Run(Aircraft ac, double sec)
    {
        var sim = new SimLoop(ac);
        for (double t = 0; t < sec; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
    }

    [Fact]
    public void GentleTouchdownKeepsEveryLeg()
    {
        var c = Cub();
        var ac = Drop(c, new Quat(0, 0, 0, 1), 1.5, 0.05);
        Run(ac, 3);
        _out.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
        Assert.Empty(ac.LostComponents);
    }

    [Fact]
    public void HardDropOnOneMainTearsThatLegOffOnly()
    {
        var c = Cub();
        // Level, sinking at 2.8 m/s but rolling LEFT at 2.5 rad/s: the left main arrives at ~5 m/s (past the 4 m/s
        // the leg can take), the right main at ~0.6 m/s — one leg goes, the other stays.
        var ac = Drop(c, new Quat(0, 0, 0, 1), 2.8, 0.05, rollRateRad: -2.5);
        Run(ac, 2.5);
        _out.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
        Assert.Contains(AirframeComponent.GearLeft, ac.LostComponents);
        Assert.DoesNotContain(AirframeComponent.GearRight, ac.LostComponents);
        Assert.DoesNotContain(AirframeComponent.TailBoom, ac.LostComponents);
        // The aircraft still comes to rest on the stub, not through the ground.
        Assert.True(-ac.State.Position.Z > 0.2, $"CG at {-ac.State.Position.Z:F2} m");
    }

    [Fact]
    public void TailConeStrikeSnapsTheTailBoomAndItsSurfaces()
    {
        var c = Cub();
        // Nose 25° up, tail-first at 7 m/s: the tail cone is the first thing to hit.
        double pitch = 25 * System.Math.PI / 180;
        var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
        var ac = Drop(c, att, 7.0, 0.05);
        Run(ac, 1.0);
        _out.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
        Assert.Contains(AirframeComponent.TailBoom, ac.LostComponents);
        Assert.Contains(AirframeComponent.GearTail, ac.LostComponents);     // the tailwheel goes with the boom
        Assert.DoesNotContain(AirframeComponent.Nose, ac.LostComponents);
        // Tail aero is gone: every hstab/vstab strip masked; wing strips kept.
        bool[]? mask = ac.StripMask; Assert.NotNull(mask);
        int idx = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool tail = id == "hstab" || id == "elevator" || id.Contains("vstab") || id.StartsWith("rudder") || id.Contains("fin");
            foreach (StripConfig _ in sf.Strips) { if (tail) Assert.False(mask![idx], $"{sf.Id} strip {idx} should be gone"); else if (id.Contains("wing")) Assert.True(mask![idx]); idx++; }
        }
        Assert.DoesNotContain(ac.ContactPoints, p => p.Component is AirframeComponent.TailHorizontal or AirframeComponent.TailVertical or AirframeComponent.TailBoom);
        Assert.Contains(ac.ContactPoints, p => p.Name == "boom-stub");
    }

    [Fact]
    public void BellySlamBreaksTheFuselageIntoThree()
    {
        var c = Cub();
        // Flat 14 m/s slam: the legs tear off, then the belly/wing roots hit past 8 m/s — the fuselage breaks up.
        var ac = Drop(c, new Quat(0, 0, 0, 1), 14.0, 0.05);
        Run(ac, 1.5);
        _out.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
        Assert.Contains(AirframeComponent.Cabin, ac.LostComponents);
        Assert.Contains(AirframeComponent.Nose, ac.LostComponents);
        Assert.Contains(AirframeComponent.TailBoom, ac.LostComponents);
        Assert.True(ac.EngineStopped);
        Assert.Contains(ac.ContactPoints, p => p.Name == "belly");     // the cabin keeps its own hard points
    }

    [Fact]
    public void HardImpactIsReportedForTheCrashSound()
    {
        var c = Cub();
        var ac = Drop(c, new Quat(0, 0, 0, 1), 3.0, 0.05);
        double reported = 0; string point = "";
        ac.HardImpact += (v, p) => { if (v > reported) { reported = v; point = p; } };
        Run(ac, 1.0);
        _out.WriteLine($"impact {reported:F2} m/s at {point}");
        Assert.True(reported >= 2.5, $"the 3 m/s arrival should be reported (got {reported:F2})");
        Assert.Empty(ac.LostComponents);   // 3 m/s on the mains, the tail dropping through after: nothing breaks
    }

    private static AircraftConfig Load(string file) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", file));

    [Theory]
    [InlineData("pa18-cub-like.json")]
    [InlineData("p51d-like.json")]
    [InlineData("pa25-pawnee-like.json")]
    public void SpawnOnTheRunwayRestsOnAllWheelsAndBreaksNothing(string file)
    {
        // Owner: the P-51 spawned level dropped its tail and broke it off. Spawn in the resting stance instead.
        var c = Load(file);
        WorldTerrain.Active = null;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, rest, ControlDeflections.Neutral);
        double pitch0 = System.Math.Asin(System.Math.Clamp(2 * (rest.Attitude.W * rest.Attitude.Y - rest.Attitude.Z * rest.Attitude.X), -1, 1));
        Run(ac, 3);
        var q = ac.State.Attitude;
        double pitch = System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
        _out.WriteLine($"{file}: stance pitch {pitch0 * 57.3:F1}°, settled {pitch * 57.3:F1}°, CG {-ac.State.Position.Z:F2} m, lost: {string.Join(", ", ac.LostComponents)}");
        Assert.Empty(ac.LostComponents);
        Assert.InRange(pitch, pitch0 - 3 * System.Math.PI / 180, pitch0 + 3 * System.Math.PI / 180);   // it was already sitting on its wheels
        Assert.True(ac.State.Velocity.Length < 0.3, "at rest");
    }

    [Fact]
    public void TougherAirframeTakesHarderHits()
    {
        var p51 = Load("p51d-like.json");
        Assert.InRange(p51.ImpactStrength, 1.5, 2.0);
        double cub = 0, fighter = 0;
        foreach (var p in AirframeContact.BuildPoints(Cub())) if (p.Name == "tail-cone") cub = p.BreakSpeedMs;
        foreach (var p in AirframeContact.BuildPoints(p51)) if (p.Name == "tail-cone") fighter = p.BreakSpeedMs;
        Assert.Equal(cub * p51.ImpactStrength, fighter, 6);
    }

    [Fact]
    public void CrashCrumplesInsteadOfBouncing()
    {
        // Owner: a crash bounced 100 ft. A flat 15 m/s slam must not come back up more than a couple of metres.
        var c = Cub();
        var ac = Drop(c, new Quat(0, 0, 0, 1), 15.0, 0.05, forwardMs: 25);
        var sim = new SimLoop(ac);
        double lowest = 0, reboundPeak = 0; bool hit = false;
        for (double t = 0; t < 6; t += 0.02)
        {
            sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
            double up = -ac.State.Position.Z;
            if (!hit && ac.State.Velocity.Length < 14.5) hit = true;
            if (hit) { lowest = System.Math.Min(lowest, up); reboundPeak = System.Math.Max(reboundPeak, up); }
        }
        _out.WriteLine($"after the slam: lowest CG {lowest:F2} m, highest CG {reboundPeak:F2} m, lost: {string.Join(", ", ac.LostComponents)}");
        Assert.True(reboundPeak < 2.5, $"bounced to {reboundPeak:F1} m");
    }

    [Fact]
    public void FuselageStationsBracketTheWingRoot()
    {
        var c = Cub();
        (double nose, double tail) = AirframeContact.FuselageStations(c);
        Assert.True(nose > tail + 1.5, $"nose cut {nose:F2} should be well ahead of tail cut {tail:F2}");
        foreach (GearConfig g in c.Gear) Assert.Equal(g.IsTailwheel ? AirframeComponent.GearTail : g.Pos[1] < 0 ? AirframeComponent.GearLeft : AirframeComponent.GearRight, AirframeContact.GearComponent(g));
    }
}

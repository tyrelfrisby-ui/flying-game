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
public class AirframeContactTests : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method does
    /// not run when the test throws, so a failing test left its world installed for the whole rest of the run.</summary>
    public void Dispose() => WorldTerrain.Active = null;

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
            // The propeller disc holds nothing up (a blade cuts or curls): the rest is on the hard points.
            var pts = AirframeContact.BuildPoints(c).Where(q => !q.PropDisc).ToList();
            foreach (var p in pts) { double up = -cand.Rotate(p.Body - c.Mass.CgVec()).Z; if (up < lowest) { second = lowest; lowest = up; } else if (up < second) second = up; }
            // Prefer the attitude where the two lowest points are level (a stable rest on wing + fin).
            double spread = second - lowest;
            if (spread < best) { best = spread; att = cand; }
        }
        double lowestUp = double.MaxValue;
        foreach (var p in AirframeContact.BuildPoints(c).Where(q => !q.PropDisc)) lowestUp = System.Math.Min(lowestUp, -att.Rotate(p.Body - c.Mass.CgVec()).Z);
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
        _out.WriteLine("lost: " + string.Join(", ", ac.LostComponents) + "; tip panels: " + string.Join(", ", ac.Panels.Select(p => $"{p.Component} {p.State} peak {p.PeakContactNm:F0} N·m (limit {p.LimitNm:F0}, ultimate {p.UltimateNm:F0})")));
        // The fuselage, tail and gear take it. Two things do not (2026-11-03): upside down the propeller's whole disc is on
        // the ground, and at idle the blades stop in it — a prop strike; and a PA-18 that tips off its fin arrives on its
        // wingtips at ~3 m/s, which takes each tip panel past its limit moment (7.0 kN·m against 3.8 limit, 7.6 ultimate):
        // bent, the panel holds only the 2 g moment Ty set for a damaged panel, and the aeroplane's own weight on it is
        // more than that. Reported in AGENT-LOG for Ty.
        Assert.DoesNotContain(ac.LostComponents, x => x is not (AirframeComponent.Propeller
            or AirframeComponent.WingTipLeft or AirframeComponent.WingTipRight or AirframeComponent.AileronLeft or AirframeComponent.AileronRight));
    }

    [Fact]
    public void HardWingTipStrikeSnapsTheWingOffAndAeroFollows()
    {
        var c = Cub();
        WorldTerrain.Active = null;
        var tables = Aircraft.BuildAirfoilTables(c);
        // Banked 40° right at 35 m/s, sinking 10 m/s from 2.5 m: the right tip hits first, hard. (Until 2026-11-05 a 6 m/s
        // drop at 60° with no way on took it; now that pivots the aeroplane about its tip onto its gear and the panel
        // keeps a set — Ty: "bends before it breaks", BendDamageTests. It takes a strike that folds it through its
        // aluminium spar's rupture rotation.)
        double roll = 40 * System.Math.PI / 180;
        var att = new Quat(System.Math.Sin(roll / 2), 0, 0, System.Math.Cos(roll / 2));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -2.5), att, att.Conjugate().Rotate(new Vec3(35, 0, 10.0)), Vec3.Zero), ControlDeflections.Neutral);
        var lost = new List<AirframeComponent>(); ac.ComponentLost += lost.Add;
        var sim = new SimLoop(ac);
        for (double t = 0; t < 3 && lost.Count == 0; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
        _out.WriteLine("lost: " + string.Join(", ", lost));
        // A tip strike takes the panel outboard of the lift-strut attach (Ty 2026-11-03: the panel beyond the wing's
        // structural station; for a strut-braced wing the lift strut), with its aileron; the inner wing stays.
        Assert.Contains(AirframeComponent.WingTipRight, lost);
        Assert.True(ac.IsLost(AirframeComponent.AileronRight));
        Assert.False(ac.IsLost(AirframeComponent.WingRightInner));
        Assert.False(ac.IsLost(AirframeComponent.WingRight));
        Assert.False(ac.IsLost(AirframeComponent.WingTipLeft));
        // Aero with the mask: level flight at 30 m/s now rolls hard LEFT (only the left wing lifts) — the
        // remaining half still produces lift (the aircraft can be flown on), mass is untouched.
        var vel = new Vec3(30 * System.Math.Cos(0.07), 0, 30 * System.Math.Sin(0.07));
        (Vec3 fIntact, Vec3 mIntact) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral);
        (Vec3 fHalf, Vec3 mHalf) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral, -1.0, null, 0, 0, null, ac.StripMask);
        _out.WriteLine($"lift intact {-fIntact.Z:F0} N, half {-fHalf.Z:F0} N; roll moment intact {mIntact.X:F0}, half {mHalf.X:F0} N·m");
        // Only the LEFT wing lifts → it rises → the aircraft rolls RIGHT, toward the missing wing (+L in NED).
        Assert.True(mHalf.X > 1500, $"losing the right wing must roll it toward the stub (L = {mHalf.X:F0})");
        Assert.InRange(-fHalf.Z / -fIntact.Z, 0.6, 0.95);   // outer panel only (owner: two panels per wing)
        // Its mass left with it (WingStructure panel mass, Raymer's buildup): the rest of the aeroplane is lighter.
        Assert.InRange(c.Mass.MassKg - ac.MassProperties.MassKg, 1.0, 0.1 * c.Mass.MassKg);
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
        // On a real island runway (2026-11-03: this used to start at Airports[0].ElevationM, 4.3 m, 500 m short of the
        // field — 650 m inside the hill there; the closing-speed break rule never noticed a start that deep).
        WorldTerrain.Airport a = WorldTerrain.Airports.First(ap => IslandRunways.Ends(ap).Count > 0);
        WorldTerrain.RunwayEnd rw = IslandRunways.Ends(a)[0];
        double x0 = rw.CentreX - rw.AlongX * 0.25 * rw.LengthM, y0 = rw.CentreY - rw.AlongY * 0.25 * rw.LengthM;
        double ground = WorldTerrain.GroundHeightAt(x0, y0);
        var mains = c.Gear.FindAll(g => !g.IsTailwheel); var tw = c.Gear.Find(g => g.IsTailwheel)!;
        double pitch = System.Math.Atan((mains[0].Pos[2] - tw.Pos[2]) / (mains[0].Pos[0] - tw.Pos[0]));
        var yaw = new Quat(0, 0, System.Math.Sin(rw.HeadingRad / 2), System.Math.Cos(rw.HeadingRad / 2));
        var att = Quat.Multiply(yaw, new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2)));
        double maxWz = -999; foreach (var g in c.Gear) maxWz = System.Math.Max(maxWz, att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
        // Three-point touchdown at 15 m/s sinking 1.5 m/s on the runway.
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(x0, y0, -(ground + maxWz + 0.3)), att, att.Conjugate().Rotate(new Vec3(15 * rw.AlongX, 15 * rw.AlongY, 1.5)), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        var events = new List<ContactEvent>(); ac.Contact += events.Add;
        for (double tt = 0; tt < 8; tt += 0.02) sim.RunFor(0.02, new ControlInputs(0, -0.3, 0, 1.0));
        _out.WriteLine($"{a.Name} runway {rw.Strip.Kind}, ground {ground:F1} m; lost: " + string.Join(", ", ac.LostComponents) + "; events: " + string.Join(" | ", events));
        double agl = -ac.State.Position.Z - WorldTerrain.GroundHeightAt(ac.State.Position.X, ac.State.Position.Y);
        WorldTerrain.Active = null;
        Assert.Empty(ac.LostComponents);
        Assert.True(agl < 2.0);
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

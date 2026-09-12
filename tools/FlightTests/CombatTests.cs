using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Owner 2026-09-10: combat damage (wing panels, control surfaces, fuel and fire) and gunnery.</summary>
public class CombatTests
{
    private readonly ITestOutputHelper _out;
    public CombatTests(ITestOutputHelper o) { _out = o; }
    private static AircraftConfig Load(string f) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));
    private static Aircraft Level(AircraftConfig c, double alt = 500, double v = 60) =>
        new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -alt), new Quat(0, 0, 0, 1), new Vec3(v, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    private static int MaskedCount(Aircraft ac, Func<SurfaceConfig, StripConfig, bool> pick)
    {
        int idx = 0, gone = 0;
        foreach (SurfaceConfig sf in ac.Config.Surfaces) foreach (StripConfig st in sf.Strips) { if (pick(sf, st) && ac.StripMask != null && !ac.StripMask[idx]) gone++; idx++; }
        return gone;
    }

    [Fact]
    public void TipStrikeTakesOnlyTheOuterPanel()
    {
        var c = Load("pa18-cub-like.json");
        WorldTerrain.Active = null;
        // Rolled 60° left, dropping so the left tip hits at ~6 m/s.
        double roll = -60 * Math.PI / 180;
        var att = new Quat(Math.Sin(roll / 2), 0, 0, Math.Cos(roll / 2));
        double lowest = double.MaxValue;
        foreach (var p in AirframeContact.BuildPoints(c)) lowest = Math.Min(lowest, -att.Rotate(p.Body - c.Mass.CgVec()).Z);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, lowest - 0.05), att, att.Conjugate().Rotate(new Vec3(0, 0, 6)), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        for (double t = 0; t < 0.3; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1));
        _out.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
        Assert.True(ac.IsLost(AirframeComponent.WingLeftOuter));
        Assert.False(ac.IsLost(AirframeComponent.WingLeftInner));
        Assert.False(ac.IsLost(AirframeComponent.WingLeft));
        double semi = WingPanels.Semispan(c);
        Assert.True(MaskedCount(ac, (sf, st) => sf.Id == "wing" && st.Pos[1] < -WingPanels.OuterFraction * semi) > 0, "outer wing strips masked");
        Assert.Equal(0, MaskedCount(ac, (sf, st) => sf.Id == "wing" && st.Pos[1] > -WingPanels.OuterFraction * semi && st.Pos[1] < -0.3));
    }

    [Fact]
    public void ThreeHitsDropAPartAndOneHitDropsAControlSurface()
    {
        var ac = Level(Load("pa18-cub-like.json"));
        var events = new List<(AirframeComponent, DamageEvent)>();
        ac.Damage.Changed += (p, e) => events.Add((p, e));
        Assert.Null(ac.Damage.RegisterHit(AirframeComponent.TailBoom));
        Assert.Null(ac.Damage.RegisterHit(AirframeComponent.TailBoom));
        Assert.Equal(DamageEvent.PartLost, ac.Damage.RegisterHit(AirframeComponent.TailBoom));
        Assert.True(ac.IsLost(AirframeComponent.TailBoom));
        var ac2 = Level(Load("pa18-cub-like.json"));
        Assert.Equal(DamageEvent.PartLost, ac2.Damage.RegisterHit(AirframeComponent.AileronRight));
        Assert.True(ac2.IsLost(AirframeComponent.AileronRight));
        Assert.False(ac2.IsLost(AirframeComponent.AileronLeft));
        Assert.True(MaskedCount(ac2, (sf, st) => sf.Id.Contains("aileron") && st.Pos[1] > 0) > 0, "right aileron strips masked");
        Assert.Equal(0, MaskedCount(ac2, (sf, st) => sf.Id.Contains("aileron") && st.Pos[1] < 0));
        // Rudder gone: rudder strips masked, fin kept.
        Assert.Equal(DamageEvent.PartLost, ac2.Damage.RegisterHit(AirframeComponent.Rudder));
        Assert.True(MaskedCount(ac2, (sf, st) => sf.Id.StartsWith("rudder")) > 0);
        Assert.Equal(0, MaskedCount(ac2, (sf, st) => sf.Id == "vStab"));
    }

    [Fact]
    public void InnerPanelLeaksThenBurnsThenFails()
    {
        var ac = Level(Load("pa18-cub-like.json"));
        Assert.Equal(DamageEvent.FuelLeak, ac.Damage.RegisterHit(AirframeComponent.WingRightInner));
        Assert.True(ac.Damage.FuelLeakRight); Assert.False(ac.Damage.FireRight);
        Assert.Equal(DamageEvent.Fire, ac.Damage.RegisterHit(AirframeComponent.WingRightInner));
        Assert.True(ac.Damage.FireRight);
        Assert.False(ac.IsLost(AirframeComponent.WingRightInner));
        for (int i = 0; i < 120; i++) ac.Damage.Update(0.1);   // 12 s of burning
        Assert.True(ac.IsLost(AirframeComponent.WingRightInner), "burning panel fails after 10 s");
        Assert.True(ac.IsLost(AirframeComponent.WingRightOuter), "the outer panel goes with it");
        Assert.True(ac.IsLost(AirframeComponent.AileronRight));
        Assert.False(ac.Damage.FireRight);
    }

    [Fact]
    public void HitVolumesResolveASegmentThroughTheOuterWing()
    {
        var c = Load("p51d-like.json");
        var vols = HitVolumes.Build(c);
        var s = new RigidBodyState(new Vec3(1000, 2000, -800), new Quat(0, 0, 0, 1), new Vec3(100, 0, 0), Vec3.Zero);
        double semi = WingPanels.Semispan(c);
        // A vertical shot through the right wing at 0.8 semispan → outer panel; at 0.35 → inner panel.
        var hitOuter = HitVolumes.Test(vols, c, s, new Vec3(1000.2, 2000 + 0.8 * semi, -830), new Vec3(1000.2, 2000 + 0.8 * semi, -770));
        var hitInner = HitVolumes.Test(vols, c, s, new Vec3(1000.2, 2000 + 0.35 * semi, -830), new Vec3(1000.2, 2000 + 0.35 * semi, -770));
        var miss = HitVolumes.Test(vols, c, s, new Vec3(1000, 2000 + 2 * semi, -830), new Vec3(1000, 2000 + 2 * semi, -770));
        _out.WriteLine($"outer {hitOuter} inner {hitInner} miss {miss}; volumes {vols.Count}");
        Assert.Equal(AirframeComponent.WingRightOuter, hitOuter);
        Assert.Equal(AirframeComponent.WingRightInner, hitInner);
        Assert.Null(miss);
        // From the front along the fuselage: the nose is first.
        var nose = HitVolumes.Test(vols, c, s, new Vec3(1020, 2000, -800), new Vec3(990, 2000, -800));
        Assert.Equal(AirframeComponent.Nose, nose);
    }

    [Fact]
    public void ArmamentMatchesTheTypeAndBulletsConvergeAndDrop()
    {
        var p51 = Load("p51d-like.json"); var f86 = Load("f86-sabre-like.json"); var cub = Load("pa18-cub-like.json");
        Assert.Equal(6, Armament.For(p51).Guns.Count); Assert.Equal(1880, Armament.For(p51).RoundsTotal);
        Assert.Equal(6, Armament.For(f86).Guns.Count); Assert.Equal(1602, Armament.For(f86).RoundsTotal);
        Assert.Single(Armament.For(cub).Guns); Assert.Equal(0.0, Armament.For(cub).Guns[0].Muzzle.Y, 6);
        WorldTerrain.Active = null;
        var ac = Level(p51, 30, 120);   // low, so a level burst reaches the ground inside a bullet's 3 s life
        var g = new Gunnery();
        int n = g.Fire(1, ac.Guns, p51, ac.State, 0.1);   // 6 guns × 800 rpm = 80 rounds/s → 8 rounds
        Assert.InRange(n, 7, 9);
        Assert.Equal(1880 - n, ac.Guns.RoundsLeft);
        // Fly the bullets: they converge toward the centreline ahead and drop to the ground within a few seconds.
        double minSpread = double.MaxValue;
        for (int i = 0; i < 60; i++)
        {
            g.Step(0.02, Array.Empty<GunTarget>());
            foreach (Bullet b in g.Bullets) if (b.Pos.X - ac.State.Position.X > 250 && b.Pos.X - ac.State.Position.X < 300) minSpread = Math.Min(minSpread, Math.Abs(b.Pos.Y));
        }
        _out.WriteLine($"spread near convergence {minSpread:F2} m; bullets left {g.Bullets.Count}");
        Assert.True(minSpread < 0.6, "bullets converge near 275 m");
        int groundHits = 0;
        for (int i = 0; i < 300; i++) { g.Step(0.02, Array.Empty<GunTarget>()); foreach (HitEvent h in g.Hits) if (h.Target == -1) groundHits++; }
        Assert.Empty(g.Bullets);
        Assert.True(groundHits > 0, "bullets end on the ground");
    }

    [Fact]
    public void BulletsHitADroneAheadAndDamageIt()
    {
        var p51 = Load("p51d-like.json"); var cub = Load("pa18-cub-like.json");
        WorldTerrain.Active = null;
        var shooter = Level(p51, 500, 120);
        var drone = new Aircraft(cub, new RigidBodyState(new Vec3(260, 0, -500), new Quat(0, 0, 0, 1), new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var target = new GunTarget { Id = 2, Config = cub, Volumes = HitVolumes.Build(cub), State = () => drone.State, Lost = drone.IsLost, Damage = drone.Damage };
        var g = new Gunnery();
        int hits = 0;
        for (int i = 0; i < 40; i++)
        {
            g.Fire(1, shooter.Guns, p51, shooter.State, 0.02);
            g.Step(0.02, new[] { target });
            foreach (HitEvent h in g.Hits) if (h.Target == 2) hits++;
        }
        _out.WriteLine($"hits on the drone: {hits}; total damage hits {drone.Damage.TotalHits}; lost: {string.Join(", ", drone.LostComponents)}");
        Assert.True(hits >= 3, "a burst at a drone dead ahead lands hits");
        Assert.True(drone.LostComponents.Count > 0 || drone.Damage.TotalHits >= 3);
    }

    [Fact]
    public void CombatZoneIsABoxFromTheSurfaceUp()
    {
        WorldTerrain.Active = null;
        Assert.True(CombatZone.Inside(new Vec3(5000, 4000, -500)));
        Assert.True(CombatZone.Inside(new Vec3(5000, 4000, -1)));
        Assert.False(CombatZone.Inside(new Vec3(5000, 4000, -3200)));
        Assert.False(CombatZone.Inside(new Vec3(3000, 4000, -500)));
        Assert.True(CombatZone.BuildGroundTargets().Count >= 4);
        foreach (GroundTarget t in CombatZone.BuildGroundTargets()) Assert.True(CombatZone.Inside(new Vec3(t.X, t.Y, -1)));
    }
}

public class DronePilotTests
{
    private readonly ITestOutputHelper _out;
    public DronePilotTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void SteadyDroneOrbitsInsideTheZoneForAMinute()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "dc3-like.json"));
        WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        var pilot = new DronePilot(7, aerobatic: false, cruiseMs: 70);
        var drone = new Aircraft(c, new RigidBodyState(new Vec3(CombatZone.CentreX, CombatZone.CentreY, -500), new Quat(0, 0, 0, 1), new Vec3(70, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(drone);
        double minAgl = double.MaxValue; int outside = 0;
        for (double t = 0; t < 60; t += 0.02)
        {
            sim.RunFor(0.02, pilot.Update(drone, 0.02));
            minAgl = Math.Min(minAgl, -drone.State.Position.Z);
            if (!CombatZone.Inside(drone.State.Position)) outside++;
        }
        Assert.True(minAgl > 150, $"drone stayed up (min {minAgl:F0} m)");
        Assert.True(outside < 50, $"drone stayed in the zone (outside {outside} steps)");
        Assert.Empty(drone.LostComponents);
    }

    [Fact]
    public void AerobaticDroneFliesVariedManoeuvresAndSurvives()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "target-drone-like.json"));
        WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        var pilot = new DronePilot(11, aerobatic: true, cruiseMs: 65);
        var drone = new Aircraft(c, new RigidBodyState(new Vec3(CombatZone.CentreX, CombatZone.CentreY, -700), new Quat(0, 0, 0, 1), new Vec3(65, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(drone);
        double minAgl = double.MaxValue, maxRoll = 0, maxPitch = 0; int outside = 0;
        for (double t = 0; t < 180; t += 0.02)
        {
            sim.RunFor(0.02, pilot.Update(drone, 0.02));
            var q = drone.State.Attitude;
            double roll = Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
            double pitch = Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
            maxRoll = Math.Max(maxRoll, Math.Abs(roll)); maxPitch = Math.Max(maxPitch, Math.Abs(pitch));
            minAgl = Math.Min(minAgl, -drone.State.Position.Z);
            if (!CombatZone.Inside(drone.State.Position)) outside++;
        }
        _out.WriteLine($"manoeuvres flown {pilot.ManoeuvresFlown} distinct {string.Join(",", pilot.Flown)}; max roll {maxRoll * 57.3:F0}° max pitch {maxPitch * 57.3:F0}°; min alt {minAgl:F0} m; outside {outside} steps");
        Assert.True(minAgl > 120, $"drone never went below 120 m (min {minAgl:F0})");
        Assert.Empty(drone.LostComponents);
        Assert.True(pilot.ManoeuvresFlown >= 5, "several manoeuvres in three minutes");
        Assert.True(pilot.Flown.Count >= 3, "varied manoeuvres");
        Assert.True(maxRoll > 2.0, "went inverted at some point");
        Assert.True(maxPitch > 0.9, "went steeply nose-up at some point");
        Assert.True(outside < 900, "mostly inside the zone");
    }

    [Fact]
    public void P51GunsSitAtTheWingLeadingEdge()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "p51d-like.json"));
        var a = Armament.For(c);
        (double le, _) = Armament.WingLeadingEdge(c, 2.3);
        foreach (GunConfig g in a.Guns) Assert.True(g.Muzzle.X > le, $"muzzle x {g.Muzzle.X:F2} ahead of the leading edge {le:F2}");
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Combat-zone formations (owner 2026-10-01): a 4-ship of P-51s and a 3-ship of DC-3s must hold their slots behind a
/// leader flying the zone's waypoints (turns included) without hitting each other or the ground; a P-51 that fights back
/// at MEDIUM skill must get into range, fire, and land some hits on a straight-flying target — but nowhere near all.
/// </summary>
public class FormationTests
{
    private readonly ITestOutputHelper _out;
    public FormationTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    public static readonly Vec3[] P51Slots = { new(-35, -35, 3), new(-35, 35, 3), new(-70, 70, 6) };   // finger four
    public static readonly Vec3[] Dc3Slots = { new(-45, -45, 4), new(-45, 45, 4) };                    // vic

    [Theory]
    [InlineData("p51d-like", 85.0, 3)]
    [InlineData("dc3-like", 62.0, 2)]
    public void FormationHoldsItsSlotsThroughTurns(string id, double cruise, int wingmen)
    {
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig cfg = Load(id);
        Vec3[] slots = id.StartsWith("p51") ? P51Slots : Dc3Slots;
        var leadPilot = new DronePilot(7, aerobatic: false, cruiseMs: cruise) { MaxBankDeg = 20, CaptureRadiusM = 1200 };
        Vec3 wp = leadPilot.Waypoints[leadPilot.Next];
        var lead = new Aircraft(cfg, new RigidBodyState(new Vec3(wp.X - 1500, wp.Y, -600), new Quat(0, 0, 0, 1), new Vec3(cruise, 0, 0), Vec3.Zero));
        if (cfg.RetractableGear) lead.SetGear(false, immediate: true);
        var wings = new List<(Aircraft ac, FormationPilot fp)>();
        for (int i = 0; i < wingmen; i++)
        {
            var fp = new FormationPilot(slots[i]);
            Vec3 p = fp.SlotPosition(lead.State);
            var ac = new Aircraft(cfg, new RigidBodyState(p, new Quat(0, 0, 0, 1), new Vec3(cruise, 0, 0), Vec3.Zero));
            if (cfg.RetractableGear) ac.SetGear(false, immediate: true);
            wings.Add((ac, fp));
        }
        double h = SimLoop.DefaultFixedDtSec, t = 0;
        var errSq = new double[wingmen]; var maxErr = new double[wingmen]; int n = 0;
        double minSep = double.MaxValue, minAlt = double.MaxValue, maxBank = 0;
        while (t < 150)
        {
            ControlInputs li = leadPilot.Update(lead, h);
            lead.Step(li, h);
            foreach (var (ac, fp) in wings) ac.Step(fp.Update(ac, lead, h), h);
            t += h;
            if (t < 25) continue;   // settle into the slots
            var all = new List<Aircraft> { lead }; all.AddRange(wings.Select(w => w.ac));
            for (int a = 0; a < all.Count; a++)
            {
                minAlt = Math.Min(minAlt, -all[a].State.Position.Z);
                for (int b = a + 1; b < all.Count; b++) minSep = Math.Min(minSep, (all[a].State.Position - all[b].State.Position).Length);
            }
            maxBank = Math.Max(maxBank, Math.Abs(FormationPilot.Euler(lead.State.Attitude).roll));
            for (int i = 0; i < wingmen; i++)
            {
                Vec3 ev = wings[i].fp.SlotPosition(lead.State) - wings[i].ac.State.Position;
                double e = ev.Length;
                if (n % 4000 == 0)
                {
                    Vec3 vl = lead.State.Attitude.Rotate(lead.State.Velocity); double ps = Math.Atan2(vl.Y, vl.X);
                    _out.WriteLine($"t={t:F0} #{i + 2} ahead {ev.X * Math.Cos(ps) + ev.Y * Math.Sin(ps):F1} right {-ev.X * Math.Sin(ps) + ev.Y * Math.Cos(ps):F1} up {-ev.Z:F1}  (err = slot − me)");
                }
                errSq[i] += e * e; maxErr[i] = Math.Max(maxErr[i], e);
            }
            n++;
        }
        for (int i = 0; i < wingmen; i++)
            _out.WriteLine($"{id} #{i + 2}: slot error RMS {Math.Sqrt(errSq[i] / n):F1} m, max {maxErr[i]:F1} m");
        _out.WriteLine($"min separation {minSep:F1} m, min altitude {minAlt:F0} m, leader max bank {maxBank * 57.3:F0}°, leader turned through {leadPilot.Next} waypoints");
        for (int i = 0; i < wingmen; i++)
        {
            Assert.True(Math.Sqrt(errSq[i] / n) < 25, $"#{i + 2} RMS slot error {Math.Sqrt(errSq[i] / n):F1} m");
            Assert.True(maxErr[i] < 70, $"#{i + 2} max slot error {maxErr[i]:F1} m");
        }
        Assert.True(minSep > 20, $"aircraft came within {minSep:F1} m of each other");
        Assert.True(minAlt > 300, $"formation sank to {minAlt:F0} m");
        Assert.True(maxBank > 10 * Math.PI / 180, "leader never turned — the test did not exercise turns");
    }

    [Fact]
    public void MediumP51GetsSomeHitsButIsNoAce()
    {
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig p51 = Load("p51d-like"), tgtCfg = Load("c172-like");
        var me = new Aircraft(p51, new RigidBodyState(new Vec3(5000 - 1500, 4500, -1000), new Quat(0, 0, 0, 1), new Vec3(95, 0, 0), Vec3.Zero));
        me.SetGear(false, immediate: true);
        var pilot = new AttackPilot(3);
        // The target: straight and level at 55 m/s, 800 m up (kinematic — this tests the shooter, not the target).
        Vec3 tPos = new(5000, 4500, -800), tVel = new(55, 0, 0);
        RigidBodyState TState() => new(tPos, new Quat(0, 0, 0, 1), tVel, Vec3.Zero);
        var target = new GunTarget { Id = 1, Config = tgtCfg, Volumes = HitVolumes.Build(tgtCfg), State = TState };
        var gun = new Gunnery();
        double h = SimLoop.DefaultFixedDtSec, minRange = double.MaxValue, minAgl = double.MaxValue;
        int hits = 0;
        for (double t = 0; t < 90; t += h)
        {
            ControlInputs ci = pilot.Update(me, TState(), h);
            me.Step(ci, h);
            if (pilot.Firing) gun.Fire(200, me.Guns!, p51, me.State, h);
            gun.Step(h, new[] { target });
            hits += gun.Hits.Count(x => x.Target == 1);
            tPos = tPos + tVel * h;
            minRange = Math.Min(minRange, (tPos - me.State.Position).Length);
            minAgl = Math.Min(minAgl, -me.State.Position.Z);
            if ((int)(t / h) % 400 == 0)
            {
                var (r, p, _) = FormationPilot.Euler(me.State.Attitude);
                _out.WriteLine($"t={t:F0} {pilot.LastMode} range {pilot.LastRangeM:F0} off {pilot.LastOffNoseRad * 57.3:F1}° roll {r * 57.3:F0} pitch {p * 57.3:F0} V {me.State.Velocity.Length:F0} alt {-me.State.Position.Z:F0} fire {pilot.Firing}");
            }
        }
        double ratio = gun.Fired > 0 ? hits / (double)gun.Fired : 0;
        _out.WriteLine($"bursts {pilot.Bursts}, rounds {gun.Fired}, hits {hits} ({ratio:P1}), closest {minRange:F0} m, lowest {minAgl:F0} m");
        Assert.True(minRange < 500, "never got into gun range");
        Assert.True(pilot.Bursts >= 2, "never fired");
        Assert.True(hits >= 1, "never hit a straight-flying target");
        Assert.True(ratio < 0.35, $"too accurate for medium skill: {ratio:P0} of rounds hit");
        Assert.True(minAgl > 150, $"flew down to {minAgl:F0} m");
    }
}

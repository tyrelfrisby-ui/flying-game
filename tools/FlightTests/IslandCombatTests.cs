using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The owner's 2026-10-31 report: "the game freezes after a while but what is weird is that I hear crashing sounds
/// when it freezes ... one is crashing while I'm flying the other one. Once it crashes my airplane freezes up."
/// He was right that there was a second aeroplane; there were fifteen. These hold each fix in place.
/// </summary>
[Collection("WorldTerrainActive")]   // swaps the global terrain: never in parallel with the legacy-world tests
public sealed class IslandCombatTests
{
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // ---- the zone geometry: no start is inside one, so no FLY begins with drones in the air ----

    [Fact]
    public void TheIslandAuthorsFourCombatZonesAndEveryOneHasItsOwnDronesAndBullseyes()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            IslandCombat.Zone[] zones = IslandCombat.Zones;
            Assert.Equal(4, zones.Length);
            foreach (IslandCombat.Zone z in zones)
            {
                Assert.True(z.Enabled, $"{z.Id} is not enabled");
                Assert.Equal(3, z.DroneCount);                             // "3 drones per zone"
                Assert.True(z.GroundTargets.Length >= 2, $"{z.Id} has {z.GroundTargets.Length} bullseyes");
                Assert.True(z.CeilingM > z.FloorM, $"{z.Id} ceiling {z.CeilingM} is not above floor {z.FloorM}");
                Assert.True(z.OrbitAltM > z.FloorM && z.OrbitAltM < z.CeilingM, $"{z.Id} orbits outside its own volume");
            }
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>The whole bug in one assertion: a zone's drones appear on entry, so if a start were inside a zone the
    /// pilot would be in a gunfight at the moment of FLY. The legacy box held S4's start and came within 575 m of
    /// 3U2's; the authored zones hold none of them.</summary>
    [Fact]
    public void NoStartOnTheIslandIsInsideACombatZone()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            foreach (IslandSites.Start s in IslandSites.Starts)
            {
                if (s.Mode == "InCombatZone") continue;                    // these are meant to be in one
                var pos = new Vec3(s.X, s.Y, -s.AltitudeM);
                IslandCombat.Zone? z = IslandCombat.ZoneAt(pos);
                Assert.True(z == null, $"start {s.Id} at ({s.X:F0},{s.Y:F0}) alt {s.AltitudeM:F0} is inside {z?.Id}");
            }
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>The authored InCombatZone starts are the entry points, so each one must actually be in its own zone —
    /// this is what proves the annular-sector and polygon arithmetic reads the world the way the world meant it.</summary>
    [Fact]
    public void EveryAuthoredCombatZoneStartIsInsideTheZoneItIsNamedFor()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            int checked_ = 0;
            foreach (IslandSites.Start s in IslandSites.Starts)
            {
                if (s.Mode != "InCombatZone") continue;
                IslandCombat.Zone? z = IslandCombat.ZoneAt(new Vec3(s.X, s.Y, -s.AltitudeM));
                Assert.True(z != null, $"start {s.Id} at ({s.X:F0},{s.Y:F0}) alt {s.AltitudeM:F0} is in no zone at all");
                Assert.Equal(s.Id, z!.Value.Id);
                checked_++;
            }
            Assert.True(checked_ >= 4, $"only {checked_} combat-zone starts found");
        }
        finally { WorldTerrain.Active = null; }
    }

    // ---- the cliff: NOT fixed, and the test that would hold it is not here ----
    //
    // Flying into a cliff is modelled wrongly: the terrain's contact normal is always straight up, so an aeroplane
    // going horizontally into rock at 74 m/s has a closing speed of nearly zero. Nothing registers an impact, nothing
    // breaks, and the only force available launches it skyward — 594 m/s, six times a DC-3's never-exceed speed.
    // That is what detonated the AI traffic over the island, and it is a real bug the player can hit too.
    //
    // It is deliberately NOT fixed here, and no test asserts a bound on it. Three attempts are recorded in AGENT-LOG
    // with their numbers: a penetration clamp on the spring, a saturation on the total contact force, and a real
    // surface normal from the terrain gradient past 45 degrees. Together they got it to 210 m/s and made the airframe
    // break, but the force saturation drove a tow through the waterfall tunnel to NaN — a tug on the bore floor
    // legitimately asks for more than twenty g — and the cliff test itself proved order-dependent, because
    // WorldSolids and WorldDecks are global registries that another test class can leave populated. The honest
    // position is that this wants a swept contact test rather than a deeper penalty force, which is its own piece of
    // work with its own proof. Ty's reported freeze is fixed without touching contact physics at all.

    // ---- the drones: they stay in the air over the island ----

    /// <summary>The legacy world must be exactly as it was: its fifteen orbit its own box and none of them falls out
    /// of the sky. This is the no-regression half of the report.</summary>
    [Fact]
    public void TheLegacyBoxTrafficStillStaysInTheAirForThreeMinutes()
    {
        WorldTerrain.Active = null;
        AircraftConfig cassutt = Load("target-drone-like"), dc3 = Load("dc3-like");
        var down = new List<string>();
        var fleet = new List<(string kind, Aircraft ac, DronePilot p)>();
        for (int i = 0; i < 8; i++)
        {
            bool aero = i < 6;
            var pilot = new DronePilot(12345 + i * 7919, aero, aero ? 65.0 : 70.0);
            Vec3 wp = pilot.Waypoints[(pilot.Next + 3) % pilot.Waypoints.Count];
            var start = new Vec3(wp.X, wp.Y, -(WorldTerrain.GroundHeightAt(wp.X, wp.Y) + wp.Z));
            var ac = new Aircraft(aero ? cassutt : dc3,
                new RigidBodyState(start, new Quat(0, 0, 0, 1), new Vec3(pilot.CruiseMs, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            if (ac.Config.RetractableGear) ac.SetGear(false, immediate: true);
            fleet.Add((aero ? "CASSUTT" : "DC-3", ac, pilot));
        }
        Fly(fleet, down, 180);
        Assert.True(down.Count == 0, string.Join("; ", down));
    }

    /// <summary>What the island actually flies: three Cassutts on each authored zone's own orbit, at its own centre
    /// and height, for three minutes. The legacy box's waypoints are not used on the island at all any more — they are
    /// what put five of fifteen into mountainsides — so this, not that, is the path that has to hold.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryAuthoredZoneOrbitKeepsItsDronesInTheAir(int zoneIndex)
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            IslandCombat.Zone z = IslandCombat.Zones[zoneIndex];
            AircraftConfig cfg = Load("target-drone-like");
            double ground = WorldTerrain.GroundHeightAt(z.OrbitX, z.OrbitY);
            double heightAgl = Math.Max(120.0, z.OrbitAltM - ground);
            var fleet = new List<(string kind, Aircraft ac, DronePilot p)>();
            for (int i = 0; i < z.DroneCount; i++)
            {
                var pilot = new DronePilot(999 + i * 7919, true, 65.0, z.OrbitX, z.OrbitY, z.OrbitRadiusM, heightAgl);
                Vec3 wp = pilot.Waypoints[(pilot.Next + 3) % pilot.Waypoints.Count];
                var start = new Vec3(wp.X, wp.Y, -(WorldTerrain.GroundHeightAt(wp.X, wp.Y) + wp.Z));
                var ac = new Aircraft(cfg, new RigidBodyState(start, new Quat(0, 0, 0, 1), new Vec3(65, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
                fleet.Add(($"{z.Id} drone {i}", ac, pilot));
            }
            var down = new List<string>();
            Fly(fleet, down, 180);
            Assert.True(down.Count == 0, string.Join("; ", down));
            // And they are still where the world put them, not halfway across the island.
            foreach ((string kind, Aircraft ac, DronePilot _) in fleet)
                Assert.True(z.DistanceToOrbitM(ac.State.Position.X, ac.State.Position.Y) < 4000,
                            $"{kind} has wandered {z.DistanceToOrbitM(ac.State.Position.X, ac.State.Position.Y) / 1000:F1} km from its orbit");
        }
        finally { WorldTerrain.Active = null; }
    }

    private static void Fly(List<(string kind, Aircraft ac, DronePilot p)> fleet, List<string> down, double seconds)
    {
        const double ha = 1.0 / 50.0;
        for (double t = 0; t < seconds; t += ha)
            foreach ((string kind, Aircraft ac, DronePilot p) in fleet)
            {
                ControlInputs ci = p.Update(ac, ha);
                new SimLoop(ac, ha).RunFor(ha, ci);
                RigidBodyState st = ac.State;
                if (!double.IsFinite(st.Position.Z) || !double.IsFinite(st.Velocity.X))
                { down.Add($"{kind} went non-finite at t={t:F1} s"); return; }
                double agl = -st.Position.Z - WorldTerrain.GroundHeightAt(st.Position.X, st.Position.Y);
                if (agl < 2 && st.Velocity.Length > 5)
                { down.Add($"{kind} flew into the ground at t={t:F1} s, ({st.Position.X:F0},{st.Position.Y:F0}) agl {agl:F0}"); return; }
            }
    }

    /// <summary>The authored orbit is one a drone can actually fly: the world asks for 150 m, which is a 70 degree bank
    /// at 65 m/s, so the pilot widens it to its own 35 degree bank while keeping the authored centre and height.</summary>
    [Fact]
    public void AnAuthoredZoneOrbitIsWidenedToSomethingTheDroneCanHold()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            IslandCombat.Zone z = IslandCombat.Zones[0];
            var pilot = new DronePilot(7, true, 65.0, z.OrbitX, z.OrbitY, z.OrbitRadiusM, 274);
            Assert.Equal(6, pilot.Waypoints.Count);
            foreach (Vec3 wp in pilot.Waypoints)
            {
                double r = Math.Sqrt((wp.X - z.OrbitX) * (wp.X - z.OrbitX) + (wp.Y - z.OrbitY) * (wp.Y - z.OrbitY));
                Assert.True(r >= z.OrbitRadiusM, $"waypoint is {r:F0} m out, inside the authored {z.OrbitRadiusM:F0} m");
                Assert.True(r < 1200, $"waypoint is {r:F0} m out - the orbit has stopped being an orbit");
                Assert.Equal(274, wp.Z, 1);
            }
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>What the freeze cost: a wrecked aeroplane left on the island is far dearer per step than a flying one,
    /// which is why CombatController stops stepping one once it is down. If this ratio ever falls to 1 the island's
    /// ground and water queries have been made cheap and the Settled guard has stopped mattering.</summary>
    [Fact]
    public void AWreckOnTheGroundCostsMorePerStepThanAnAeroplaneInTheAir()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig cfg = Load("dc3-like");
            double gx = 2850, gy = 1900, g = WorldTerrain.GroundHeightAt(gx, gy);
            var air = new Aircraft(cfg, new RigidBodyState(new Vec3(gx, gy, -(g + 400)), new Quat(0, 0, 0, 1), new Vec3(62, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            air.SetGear(false, immediate: true);
            var wreck = new Aircraft(cfg, LandingGear.RestingState(cfg, gx, gy, g, 0), ControlDeflections.Neutral);
            Assert.True(Cost(wreck) > Cost(air) * 1.5, "a wreck no longer costs appreciably more than a flying aeroplane");
        }
        finally { WorldTerrain.Active = null; }

        static double Cost(Aircraft a)
        {
            var ci = new ControlInputs(0, 0, 0, 1.0);
            for (int i = 0; i < 100; i++) new SimLoop(a, 1.0 / 50.0).RunFor(1.0 / 50.0, ci);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 500; i++) new SimLoop(a, 1.0 / 50.0).RunFor(1.0 / 50.0, ci);
            return sw.Elapsed.TotalMilliseconds;
        }
    }
}

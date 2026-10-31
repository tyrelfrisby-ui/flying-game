using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Not an assertion — where the legacy COMBAT ZONE box lands on the island, and what the fifteen AI
/// aeroplanes that orbit it actually do when they are flown there. The player hears their crashes.</summary>
[Collection("WorldTerrainActive")]   // swaps the global terrain: never in parallel with the legacy-world tests
public sealed class IslandDroneProbe : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method
    /// does not run when the test throws, so a failing test used to leave its world installed in the global
    /// WorldTerrain.Active for the whole rest of the run — which silently changed the ground under every
    /// class that came after it, whether or not it was serialised.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _out;
    public IslandDroneProbe(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    [Fact]
    public void WhereIsTheCombatZoneOnTheIsland()
    {
        WorldTerrain.Active = new WorldTerrain();
        _out.WriteLine($"zone box  X {CombatZone.X0}..{CombatZone.X1}   Y {CombatZone.Y0}..{CombatZone.Y1}   centre ({CombatZone.CentreX},{CombatZone.CentreY})");
        double lo = double.MaxValue, hi = double.MinValue, sea = 0; int n = 0;
        for (double x = CombatZone.X0; x <= CombatZone.X1; x += 100)
            for (double y = CombatZone.Y0; y <= CombatZone.Y1; y += 100)
            {
                double g = WorldTerrain.GroundHeightAt(x, y);
                lo = Math.Min(lo, g); hi = Math.Max(hi, g); if (g < 0) sea++; n++;
                }
        _out.WriteLine($"terrain under the zone: min {lo:F0} m  max {hi:F0} m  relief {hi - lo:F0} m   below sea level {100.0 * sea / n:F0}% of {n} samples");
        _out.WriteLine("");
        _out.WriteLine("how close is each island start to the zone (active inside 3000 m)?");
        foreach (IslandSites.Start s in IslandSites.Starts)
        {
            double dx = Math.Max(0, Math.Max(CombatZone.X0 - s.X, s.X - CombatZone.X1));
            double dy = Math.Max(0, Math.Max(CombatZone.Y0 - s.Y, s.Y - CombatZone.Y1));
            double d = Math.Sqrt(dx * dx + dy * dy);
            _out.WriteLine($"  {s.Id,-5} {s.Mode,-14} ap={s.Airport,-10} at=({s.X,8:F0},{s.Y,8:F0})  distance to zone {d,8:F0} m   {(d < 3000 ? "<<< DRONES ACTIVE" : "")}");
        }
        WorldTerrain.Active = null;
    }

    /// <summary>The full fifteen: six aerobatic Cassutts, two DC-3 singles, a four-ship of P-51s and a three-ship of
    /// DC-3s, flown exactly as CombatController flies them (50 Hz, formation members on FormationPilot). Reports every
    /// crash, every non-finite state, and whether the thing ever comes back.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DoTheDronesStayInTheAirOverTheIsland(bool island)
    {
        WorldTerrain.Active = island ? new WorldTerrain() : null;
        int seedBase = 12345;
        var all = new List<D>();
        AircraftConfig cassutt = Load("target-drone-like"), dc3 = Load("dc3-like"), p51 = Load("p51d-like");
        for (int i = 0; i < 8; i++)
        {
            bool aero = i < 6;
            var pilot = new DronePilot(seedBase + i * 7919, aero, aero ? 65.0 : 70.0);
            all.Add(Make(aero ? "CASSUTT" : "DC-3", aero ? cassutt : dc3, pilot, pilot.CruiseMs, null, null));
        }
        AddFlight(all, "P-51", p51, 85.0, new Vec3[] { new(-35, -35, 3), new(-35, 35, 3), new(-70, 70, 6) }, seedBase + 104729);
        AddFlight(all, "DC-3f", dc3, 62.0, new Vec3[] { new(-45, -45, 4), new(-45, 45, 4) }, seedBase + 209458);

        const double ha = 1.0 / 50.0;
        int crashes = 0, bad = 0;
        var seen = new HashSet<string>();
        for (double t = 0; t < 180; t += ha)
        {
            foreach (D d in all)
            {
                if (d.Bad) continue;
                ControlInputs ci;
                if (d.Dead) ci = new ControlInputs(0, 0, 0, 1.0);
                else if (d.Fp != null && d.Lead != null && d.Lead != d) ci = d.Fp.Update(d.Ac, d.Lead.Ac, ha);
                else ci = d.Pilot.Update(d.Ac, ha);
                try { new SimLoop(d.Ac, ha).RunFor(ha, ci); }
                catch (Exception e) { d.Bad = true; bad++; _out.WriteLine($"  t={t,6:F1}  {d.Kind} #{d.Id} THREW {e.GetType().Name}"); continue; }
                RigidBodyState st = d.Ac.State;
                if (!Finite(st))
                {
                    d.Bad = true; bad++;
                    _out.WriteLine($"  t={t,6:F1}  {d.Kind} #{d.Id} STATE WENT NON-FINITE{(d.Fp != null ? "  (formation member - its flight never respawns)" : "")}");
                    continue;
                }
                double g = WorldTerrain.GroundHeightAt(st.Position.X, st.Position.Y);
                double agl = -st.Position.Z - g;
                if (!d.Dead && agl < 2 && st.Velocity.Length > 5)
                {
                    d.Dead = true; d.DeadT = 0; crashes++;
                    _out.WriteLine($"  t={t,6:F1}  {d.Kind} #{d.Id} FLEW INTO THE GROUND at ({st.Position.X:F0},{st.Position.Y:F0}) ground {g:F0} m at {st.Velocity.Length:F0} m/s -> EXPLOSION heard by the player");
                    seen.Add(d.Kind);
                }
                if (d.Dead) d.DeadT += ha;
            }
        }
        _out.WriteLine("");
        int finite = 0; foreach (D d in all) if (!d.Bad) finite++;
        _out.WriteLine($"world = {(island ? "ISLAND" : "legacy template")}:  {all.Count} AI aeroplanes, {crashes} flew into the ground, {bad} left a non-finite state behind, {finite} still simulable after 180 s");
        WorldTerrain.Active = null;
    }

    /// <summary>The crash itself, step by step: a DC-3 single over the island from t=30 s, so the birth of the NaN is
    /// on the record rather than inferred.</summary>
    [Fact]
    public void WhereIsTheNanBorn()
    {
        WorldTerrain.Active = new WorldTerrain();
        var pilot = new DronePilot(12345 + 7 * 7919, false, 70.0);
        D d = Make("DC-3", Load("dc3-like"), pilot, 70.0, null, null);
        const double ha = 1.0 / 50.0;
        for (double t = 0; t < 38; t += ha)
        {
            ControlInputs ci = pilot.Update(d.Ac, ha);
            new SimLoop(d.Ac, ha).RunFor(ha, ci);
            RigidBodyState st = d.Ac.State;
            bool near = t > 34.0;
            if (near && (int)(t * 50) % 5 == 0 || !Finite(st))
            {
                double g = Finite(st) ? WorldTerrain.GroundHeightAt(st.Position.X, st.Position.Y) : double.NaN;
                _out.WriteLine($"  t={t,6:F2}  pos=({st.Position.X,8:F0},{st.Position.Y,8:F0},{st.Position.Z,9:F0})  ground {g,7:F0}  agl {-st.Position.Z - g,8:F0}  |V| {st.Velocity.Length,8:F1}  rates=({st.Rates.X,7:F1},{st.Rates.Y,7:F1},{st.Rates.Z,7:F1})");
            }
            if (!Finite(st)) { _out.WriteLine($"  -> non-finite at t={t:F2}"); break; }
        }
        WorldTerrain.Active = null;
    }

    /// <summary>What one AI aeroplane costs per 50 Hz step on the island, flying versus wrecked on the ground. The
    /// wreck is the one that matters: CombatController keeps stepping dead drones for the rest of the session.</summary>
    [Fact]
    public void WhatDoesAWreckedDroneCostPerStep()
    {
        WorldTerrain.Active = new WorldTerrain();
        AircraftConfig cfg = Load("dc3-like");
        const double ha = 1.0 / 50.0;

        // In the air over the zone, 400 m AGL.
        double gx = 2850, gy = 1900, g = WorldTerrain.GroundHeightAt(gx, gy);
        var air = new Aircraft(cfg, new RigidBodyState(new Vec3(gx, gy, -(g + 400)), new Quat(0, 0, 0, 1), new Vec3(62, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        air.SetGear(false, immediate: true);
        // Resting on the ground at the same place, gear down, throttle closed - a wreck.
        var wreck = new Aircraft(cfg, LandingGear.RestingState(cfg, gx, gy, g, 0), ControlDeflections.Neutral);

        foreach ((string what, Aircraft a) in new[] { ("flying at 400 m AGL", air), ("wrecked on the ground", wreck) })
        {
            var ci = new ControlInputs(0, 0, 0, 1.0);
            for (int i = 0; i < 200; i++) new SimLoop(a, ha).RunFor(ha, ci);   // warm
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int n = 2000;
            for (int i = 0; i < n; i++) new SimLoop(a, ha).RunFor(ha, ci);
            sw.Stop();
            double us = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
            _out.WriteLine($"  {what,-24} {us,8:F1} us per step   -> {us * 50 / 1000.0,6:F2} ms of every second, per aeroplane");
        }
        WorldTerrain.Active = null;
    }

    private static bool Finite(RigidBodyState s) =>
        double.IsFinite(s.Position.X) && double.IsFinite(s.Position.Y) && double.IsFinite(s.Position.Z) &&
        double.IsFinite(s.Velocity.X) && double.IsFinite(s.Velocity.Y) && double.IsFinite(s.Velocity.Z) &&
        double.IsFinite(s.Rates.X) && double.IsFinite(s.Rates.Y) && double.IsFinite(s.Rates.Z) &&
        double.IsFinite(s.Attitude.W) && double.IsFinite(s.Attitude.X) && double.IsFinite(s.Attitude.Y) && double.IsFinite(s.Attitude.Z);

    private sealed class D
    {
        public string Kind = ""; public int Id; public Aircraft Ac = null!; public DronePilot Pilot = null!;
        public FormationPilot? Fp; public D? Lead; public bool Dead, Bad; public double DeadT;
    }

    private static int _next;

    private static D Make(string kind, AircraftConfig cfg, DronePilot pilot, double speed, FormationPilot? fp, D? lead)
    {
        Vec3 wp = pilot.Waypoints[(pilot.Next + 3) % pilot.Waypoints.Count];
        double ground = WorldTerrain.GroundHeightAt(wp.X, wp.Y);
        var start = new Vec3(wp.X, wp.Y, -(ground + wp.Z));
        var ac = new Aircraft(cfg, new RigidBodyState(start, new Quat(0, 0, 0, 1), new Vec3(speed, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        if (cfg.RetractableGear) ac.SetGear(false, immediate: true);
        return new D { Kind = kind, Id = _next++, Ac = ac, Pilot = pilot, Fp = fp, Lead = lead };
    }

    private static void AddFlight(List<D> all, string kind, AircraftConfig cfg, double cruise, Vec3[] slots, int seed)
    {
        var route = new DronePilot(seed, false, cruise);
        Vec3 wp = route.Waypoints[(route.Next + 3) % route.Waypoints.Count];
        double ground = WorldTerrain.GroundHeightAt(wp.X, wp.Y);
        var start = new Vec3(wp.X, wp.Y, -(ground + Math.Max(450, wp.Z)));
        var leadState = new RigidBodyState(start, new Quat(0, 0, 0, 1), new Vec3(cruise, 0, 0), Vec3.Zero);
        D? lead = null;
        for (int k = 0; k <= slots.Length; k++)
        {
            var pilot = new DronePilot(seed, false, cruise) { MaxBankDeg = 20, CaptureRadiusM = 1200 };
            var fp = new FormationPilot(k == 0 ? Vec3.Zero : slots[k - 1]);
            Vec3 pos = k == 0 ? start : fp.SlotPosition(leadState);
            var ac = new Aircraft(cfg, new RigidBodyState(pos, new Quat(0, 0, 0, 1), new Vec3(cruise, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            if (cfg.RetractableGear) ac.SetGear(false, immediate: true);
            var d = new D { Kind = kind, Id = _next++, Ac = ac, Pilot = pilot, Fp = fp };
            if (k == 0) lead = d;
            d.Lead = lead;
            all.Add(d);
        }
    }

}

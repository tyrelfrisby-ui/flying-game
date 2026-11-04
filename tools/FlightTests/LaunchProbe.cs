using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// PROBES behind lane/claude-rig-trim's launch-variation investigation (2026-11-02), env-gated, nothing asserted:
///   LAUNCH_PROBE=1  the default airport's authored in-air start (PHDH, 461 m = 1,514 ft), through AirStart, in the air
///                   the island actually flies (the regional field does not load on the island, so uniform air: calm, the
///                   menu's 5 m/s from 090, and that plus light turbulence), with the sim clock started at different values
///                   — stick-free ON and OFF. Stops at 100 m above the ground and says so (a probe flight that hits a
///                   hill is not a left turn).
///   LAUNCH_PROBE=2  rig point and 1,514 ft, an initial bank of ±0.01–0.1°: how fast a perturbation grows.
///   LAUNCH_PROBE=3  what a stale first frame, a leftover trim or a drifting pad does to the direction.
/// LAUNCH_ID picks the type (default the Pitts).
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class LaunchProbe
{
    private readonly ITestOutputHelper _o;
    public LaunchProbe(ITestOutputHelper o) => _o = o;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("LAUNCH_PROBE") != "1") return;
        var island = new WorldTerrain(); WorldTerrain.Active = island;
        try
        {
            var ap = WorldTerrain.DefaultAirport;
            IslandSites.Start st = IslandRunways.Authored(ap.Name, "InTheAir")!.Value;
            _o.WriteLine($"{ap.Name} in-air start alt {st.AltitudeM:F0} m = {st.AltitudeM / 0.3048:F0} ft, ground {WorldTerrain.GroundHeightAt(st.X, st.Y):F0} m");
            string id = Environment.GetEnvironmentVariable("LAUNCH_ID") ?? "pitts-s2b-like";
            foreach (string air in new[] { "calm", "wind", "light" })
            foreach (bool free in new[] { false, true })
            foreach (double t0 in new[] { 0.0, 3.7, 61.3 })
            {
                Atmosphere.Field = null;
                Atmosphere.SteadyWind = air == "calm" ? Vec3.Zero : new Vec3(0, -5, 0);   // FROM 090 at 5 m/s
                Atmosphere.ActiveTurbulence = air == "light" ? Turbulence.Light() : null;
                Atmosphere.SimTimeSec = t0;
                AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
                AirStart.Plan plan = AirStart.Compute(c, st.X, st.Y, st.AltitudeM, st.HeadingTrueDeg * Math.PI / 180, c.SpawnIasMs);
                Aircraft ac = AirStart.Build(c, plan);
                var inp = new ControlInputs(0, plan.TrimStick, 0, plan.Lever, free, free, free);
                double psi0 = Hd(plan.State.Attitude), tEnd = 60; var sb = new System.Text.StringBuilder();
                for (int i = 1; i <= 60 * 200; i++)
                {
                    ac.Step(inp, 1.0 / 200);
                    if (i % 2000 == 0) sb.Append($" {Bk(ac.State.Attitude) * 57.2958,6:F1}");
                    if (-ac.State.Position.Z - WorldTerrain.GroundHeightAt(ac.State.Position.X, ac.State.Position.Y) < 100) { tEnd = i / 200.0; sb.Append(" [terrain]"); break; }
                }
                double dpsi = Hd(ac.State.Attitude) - psi0; dpsi = Math.Atan2(Math.Sin(dpsi), Math.Cos(dpsi));
                _o.WriteLine($"{air,-6} free={free,-5} clock {t0,5:F1} s: bank @10 s steps{sb}; Δψ {dpsi * 57.2958,6:F1}° in {tEnd:F1} s");
            }
        }
        finally { WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; }
    }
    private static double Hd(Quat q) { Vec3 x = q.Rotate(new Vec3(1, 0, 0)); return Math.Atan2(x.Y, x.X); }
    private static double Bk(Quat q) { Vec3 y = q.Rotate(new Vec3(0, 1, 0)); return Math.Asin(Math.Clamp(y.Z, -1, 1)); }
}

[Collection("WorldTerrainActive")]
public sealed class LaunchSensitivityProbe
{
    private readonly ITestOutputHelper _o;
    public LaunchSensitivityProbe(ITestOutputHelper o) => _o = o;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("LAUNCH_PROBE") != "2") return;
        Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; WorldTerrain.Active = null;
        string id = Environment.GetEnvironmentVariable("LAUNCH_ID") ?? "pitts-s2b-like";
        foreach (double altFt in new[] { 4000.0, 1514.0 })
        foreach (bool free in new[] { false, true })
        foreach (double dPhiDeg in new[] { -0.1, -0.01, 0.0, 0.01, 0.1 })
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            var r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, altFt * 0.3048);
            Quat att = RigSolver.Attitude(r, 0.0);
            double hb = dPhiDeg * Math.PI / 360; att = Quat.Multiply(att, new Quat(Math.Sin(hb), 0, 0, Math.Cos(hb)));
            var vb = new Vec3(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -r.AltitudeM), att, vb, Vec3.Zero), new ControlDeflections(0, r.ElevatorRad, 0, 0));
            double stick = Aircraft.StickForDeflection(r.ElevatorRad, c.Controls.Elevator);
            var inp = new ControlInputs(0, stick, 0, 1 - 2 * RigSolver.RigThrottle, free, free, free);
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= 60 * 200; i++) { ac.Step(inp, 1.0 / 200); if (i % 2000 == 0) sb.Append($" {Bk(ac.State.Attitude) * 57.2958,6:F2}"); }
            _o.WriteLine($"{id} {altFt,5} ft free={free,-5} φ0+{dPhiDeg,5}°: bank @10..60 s{sb}");
        }
    }
    private static double Bk(Quat q) { Vec3 y = q.Rotate(new Vec3(0, 1, 0)); return Math.Asin(Math.Clamp(y.Z, -1, 1)); }
}

[Collection("WorldTerrainActive")]
public sealed class LaunchPerturbationProbe
{
    private readonly ITestOutputHelper _o;
    public LaunchPerturbationProbe(ITestOutputHelper o) => _o = o;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("LAUNCH_PROBE") != "3") return;
        Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; WorldTerrain.Active = null;
        string id = Environment.GetEnvironmentVariable("LAUNCH_ID") ?? "pitts-s2b-like";
        AircraftConfig c0 = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        double drift = GamepadShaping.PreCompensateDeadZone(GamepadShaping.Stick(0.10, 0).X, c0.Controls.Aileron.DeadZone);
        _o.WriteLine($"aileron DZ {c0.Controls.Aileron.DeadZone}, gamepad 0.10 drift -> stick {drift:F4}");
        foreach (double altFt in new[] { 1514.0, 4000.0 })
        foreach (bool free in new[] { false, true })
        foreach (string pert in new[] { "none", "stale-neutral-10", "full-left-ail-10", "full-right-ail-10", "ail-trim-left-0.05", "pad-drift-left-0.10" })
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            var r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, altFt * 0.3048);
            var vb = new Vec3(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -r.AltitudeM), RigSolver.Attitude(r, 0.0), vb, Vec3.Zero), new ControlDeflections(0, r.ElevatorRad, 0, 0));
            double stick = Aircraft.StickForDeflection(r.ElevatorRad, c.Controls.Elevator), lever = 1 - 2 * RigSolver.RigThrottle;
            var hands = new ControlInputs(0, stick, 0, lever, free, free, free);
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= 60 * 200; i++)
            {
                ControlInputs u = hands;
                if (i <= 10 && pert == "stale-neutral-10") u = ControlInputs.Neutral;
                if (i <= 10 && pert == "full-left-ail-10") u = new ControlInputs(-1, stick, 0, lever);
                if (i <= 10 && pert == "full-right-ail-10") u = new ControlInputs(1, stick, 0, lever);
                if (pert == "ail-trim-left-0.05") u = new ControlInputs(-0.05, stick, 0, lever, free, free, free);
                if (pert == "pad-drift-left-0.10") u = new ControlInputs(-drift, stick, 0, lever, false, false, free);
                ac.Step(u, 1.0 / 200);
                if (i % 2000 == 0) sb.Append($" {Bk(ac.State.Attitude) * 57.2958,7:F2}");
            }
            _o.WriteLine($"{altFt,5} ft free={free,-5} {pert,-20}: bank @10..60 s{sb}");
        }
    }
    private static double Bk(Quat q) { Vec3 y = q.Rotate(new Vec3(0, 1, 0)); return Math.Asin(Math.Clamp(y.Z, -1, 1)); }
}

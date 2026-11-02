using System.IO;
using System.Text;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Not an assertion: the crash-in-water probe (lane/claude-crash-water). The same C172 dropped onto LAND and onto WATER
/// at a crash speed that breaks it up, one intact ditching, and a floatplane with undamaged and with damaged floats;
/// per body: t, depth, vertical speed, trapped air, buoyancy, state — every 0.25 s for 5 s, then every 5 s to the
/// bottom or 30 min. Runs only with CRASH_WATER_PROBE=1 (it takes minutes and writes
/// ~/Developer/agent-lanes/aero-playground/crash-water/probe.txt and probe.csv).
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class CrashWaterProbe : IDisposable
{
    private readonly ITestOutputHelper _out;
    public CrashWaterProbe(ITestOutputHelper o) { _out = o; CrashEvents.ClearForTests(); WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = null; }
    public void Dispose() { CrashEvents.ClearForTests(); WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = null; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
    private static Quat Pitch(double deg) { double h = deg * System.Math.PI / 360; return new Quat(0, System.Math.Sin(h), 0, System.Math.Cos(h)); }
    private static double PitchDeg(Quat q) => System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 180 / System.Math.PI;
    private static double RollDeg(Quat q) => System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 180 / System.Math.PI;

    private readonly StringBuilder _txt = new(), _csv = new();
    private const double WaterUp = 10.0;      // water surface 10 m up over ground at 0: a 10 m deep lake

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("CRASH_WATER_PROBE") != "1") { _out.WriteLine("set CRASH_WATER_PROBE=1 to run"); return; }
        _csv.AppendLine("scenario,body,t_s,depth_m,vz_ms,trapped_air_m3,buoyancy_N,state,extra");
        AircraftConfig c172 = Load("c172-like");
        _txt.AppendLine("CRASH-WATER PROBE  (lane/claude-crash-water, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
        _txt.AppendLine("depth = metres of the body CENTRE below the water surface (+ = under). On land: height of the lowest point above the ground.");
        _txt.AppendLine("Water: a 10 m deep lake. Severity01 = (v_n / 8 m/s)^2 clamped (WreckSim.Severity).");
        _txt.AppendLine();
        _txt.AppendLine("C172 HYDRO MODEL (Core/Wreck/AirframeHydroModel, reference loading)");
        _txt.Append(AirframeHydroModel.Build(c172).Describe());
        _txt.AppendLine();

        try { RunAll(c172); } finally { FloatHydro.FlatWaterOverride = null; }   // thread-static: clear it on this thread

        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Developer", "agent-lanes", "aero-playground", "crash-water");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "probe.txt"), _txt.ToString());
        File.WriteAllText(Path.Combine(dir, "probe.csv"), _csv.ToString());
        _out.WriteLine(_txt.ToString());
    }

    private void RunAll(AircraftConfig c172)
    {
        BreakUp("LAND break-up", c172, water: false);
        BreakUp("WATER break-up", c172, water: true);
        Ditch("INTACT ditching", c172, 12, 0.5, 8);
        Ditch("FAST ditching, gear down", c172, 24, 0.8, 6);
        Floatplane("FLOATPLANE undamaged", false);
        Floatplane("FLOATPLANE left float holed (4 compartments, RockStrike)", true);
    }

    private static bool Sample(double t, ref double next)
    {
        if (t + 1e-9 < next) return false;
        next = t < 5.0 - 1e-9 ? next + 0.25 : next + 5.0;
        return true;
    }

    private sealed class Track
    {
        public string Name = ""; public double MaxDepth = double.MinValue, Resurface = double.NaN, Sink = double.NaN, Bottom = double.NaN, FirstUnder = double.NaN;
        public string Impact = "";
    }

    private void Line(string scen, string body, double t, double depth, double vz, double air, double buoy, string state, string extra = "")
    {
        _txt.AppendLine($"  {t,8:F2} {body,-16} {depth,8:F2} {vz,7:F2} {air,9:F4} {buoy,10:F0}  {state,-9} {extra}");
        _csv.AppendLine($"{scen},{body},{t:F2},{depth:F3},{vz:F3},{air:F5},{buoy:F1},{state},{extra}");
    }

    private void Header(string title)
    {
        _txt.AppendLine("==== " + title);
        _txt.AppendLine("       t  body                depth      vz   air m3   buoy N   state");
    }

    private void BreakUp(string title, AircraftConfig c, bool water)
    {
        CrashEvents.ClearForTests();
        FloatHydro.FlatWaterOverride = water ? WaterUp : null;
        double surface = water ? WaterUp : 0.0;
        var impacts = new List<CrashImpact>();
        CrashEvents.Impact += impacts.Add;
        // 30 m/s (58 kt) along, 12 m/s down, 15 deg nose-down: past every break speed in AirframeContact.
        var att = Pitch(-15);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(surface + 2.5)), att, att.Conjugate().Rotate(new Vec3(30, 0, 12)), Vec3.Zero));
        var relay = new CrashRelay(ac);
        var loop = new SimLoop(ac);
        double dt = SimLoop.DefaultFixedDtSec, next = 0;
        Header(title);
        var tracks = new Dictionary<int, Track>();
        Track airT = new() { Name = "airframe" };
        double tEnd = 1800;
        for (int i = 0; ; i++)
        {
            double t = i * dt;
            if (i > 0) { loop.RunFor(dt, ControlInputs.Neutral); relay.Step(dt); }
            foreach (WreckPiece p in relay.Wreck.Pieces)
            {
                if (!tracks.TryGetValue(p.Id, out Track? tr)) tracks[p.Id] = tr = new Track { Name = p.Name };
                if (water) Follow(tr, t, p.DepthM, p.WorldVelocity.Z, p.Phase == WreckPhase.OnBottom, p.Sinking);
            }
            double airDepth = ac.State.Position.Z + surface;
            if (water) Follow(airT, t, airDepth, ac.State.Attitude.Rotate(ac.State.Velocity).Z, false, relay.LastAirframeFlood?.Sinking ?? false);
            if (Sample(t, ref next) || i == 0)
            {
                string airState = water ? (airDepth > 1.5 ? "under" : "afloat") : "on land";
                Line(title, "airframe", t, water ? airDepth : -ac.State.Position.Z, ac.State.Attitude.Rotate(ac.State.Velocity).Z,
                    ac.Hydro.TrappedAirM3, ac.Hydro.BuoyancyN(ac.State), airState, $"lost: {string.Join("+", ac.LostComponents)}");
                foreach (WreckPiece p in relay.Wreck.Pieces)
                    Line(title, p.Name, t, water ? p.DepthM : p.ClearanceM, p.WorldVelocity.Z, p.TrappedAirM3, p.BuoyancyN, p.Phase.ToString(),
                        $"m {p.MassKg:F0} kg reserve {p.ReserveKg:F0} kg esc {p.AirEscapeM3s * 1000:F2} L/s");
            }
            bool done = t > 10 && relay.Wreck.Pieces.All(p => p.Asleep || p.Phase is WreckPhase.OnBottom or WreckPhase.OnLand) && (!water || airDepth > WaterUp - 3);
            if (t >= tEnd || (done && t > 30) || (!water && t > 30)) break;
        }
        _txt.AppendLine("  pieces:");
        foreach (WreckPiece p in relay.Wreck.Pieces)
        {
            _txt.AppendLine($"    #{p.Id} {p.Name,-14} {p.MassKg,6:F0} kg  elements: {string.Join(", ", p.Elements.Select(e => $"{e.Name}({e.MassKg:F0} kg, env {e.EnvelopeM3:F3}, void {e.VoidM3:F3} m3, open {e.TotalOpeningM2 * 1e4:F0} cm2)"))}");
            if (water && tracks.TryGetValue(p.Id, out Track? tr))
                _txt.AppendLine($"       max plunge {tr.MaxDepth:F2} m; resurfaced {Fmt(tr.Resurface)}; reserve<0 (sinking) {Fmt(tr.Sink)}; on the bed {Fmt(tr.Bottom)}");
        }
        if (water) _txt.AppendLine($"    airframe: max depth {airT.MaxDepth:F2} m; resurfaced {Fmt(airT.Resurface)}; sinking {Fmt(airT.Sink)}");
        _txt.AppendLine("  impacts raised:");
        foreach (CrashImpact e in impacts)
            _txt.AppendLine($"    {e.Surface,-5} piece {e.PieceId,2}  v_n {e.NormalSpeedMs,5:F1} m/s  mass {e.MassKg,6:F0} kg  severity {e.Severity01:F2}");
        _txt.AppendLine();
        relay.Detach();
    }

    private static string Fmt(double t) => double.IsNaN(t) ? "never" : $"{t:F1} s";

    private static void Follow(Track tr, double t, double depth, double vz, bool bottom, bool sinking)
    {
        if (double.IsNaN(depth)) return;
        if (depth > tr.MaxDepth && t < 20) tr.MaxDepth = depth;
        if (double.IsNaN(tr.FirstUnder) && depth > 0.2) tr.FirstUnder = t;
        if (!double.IsNaN(tr.FirstUnder) && double.IsNaN(tr.Resurface) && depth < 0.05 && vz <= 0 && t > tr.FirstUnder) tr.Resurface = t;
        if (double.IsNaN(tr.Sink) && sinking) tr.Sink = t;
        if (double.IsNaN(tr.Bottom) && bottom) tr.Bottom = t;
    }

    private void Ditch(string title, AircraftConfig c, double fwd, double down, double pitchDeg)
    {
        CrashEvents.ClearForTests();
        FloatHydro.FlatWaterOverride = WaterUp;
        var impacts = new List<CrashImpact>();
        CrashEvents.Impact += impacts.Add;
        var att = Pitch(pitchDeg);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(WaterUp + 1.6)), att, att.Conjugate().Rotate(new Vec3(fwd, 0, down)), Vec3.Zero));
        var relay = new CrashRelay(ac);
        var loop = new SimLoop(ac);
        double dt = SimLoop.DefaultFixedDtSec, next = 0, sink = double.NaN, under = double.NaN;
        Header(title + $" ({fwd} m/s along, {down} m/s down, {pitchDeg} deg nose-up)");
        for (int i = 0; ; i++)
        {
            double t = i * dt;
            if (i > 0) { loop.RunFor(dt, new ControlInputs(0, 0, 0, 1.0)); relay.Step(dt); }
            double depth = ac.State.Position.Z + WaterUp;
            bool sinking = relay.LastAirframeFlood?.Sinking ?? false;
            if (double.IsNaN(sink) && sinking) sink = t;
            if (double.IsNaN(under) && depth > 3.0) under = t;
            if (Sample(t, ref next) || i == 0)
                Line(title, "airframe", t, depth, ac.State.Attitude.Rotate(ac.State.Velocity).Z, ac.Hydro.TrappedAirM3, ac.Hydro.BuoyancyN(ac.State),
                    sinking ? "sinking" : depth > 1.5 ? "under" : "afloat",
                    $"water in {ac.Hydro.WaterInsideM3:F3} m3 esc {(relay.LastAirframeFlood?.AirEscapeM3s ?? 0) * 1000:F2} L/s pitch {PitchDeg(ac.State.Attitude):F0} roll {RollDeg(ac.State.Attitude):F0} lost {string.Join("+", ac.LostComponents)}");
            if (t >= 1800 || (!double.IsNaN(under) && t > under + 10)) break;
        }
        _txt.AppendLine($"  reserve<0 (sinking) at {Fmt(sink)}; CG 3 m under at {Fmt(under)}");
        _txt.AppendLine("  per element at the end:");
        foreach (HydroElement e in ac.Hydro.Attached)
            _txt.AppendLine($"    {e.Name,-16} void {e.VoidM3,7:F3} water {e.WaterM3,7:F3} air {e.AirM3,7:F3} m3  openings {e.TotalOpeningM2 * 1e4,7:F1} cm2");
        _txt.AppendLine("  impacts raised:");
        foreach (CrashImpact e in impacts) _txt.AppendLine($"    {e.Surface,-5} piece {e.PieceId,2}  v_n {e.NormalSpeedMs,5:F1} m/s  severity {e.Severity01:F2}");
        _txt.AppendLine();
        relay.Detach();
    }

    private void Floatplane(string title, bool damaged)
    {
        CrashEvents.ClearForTests();
        FloatHydro.FlatWaterOverride = WaterUp;
        AircraftConfig c = Load("pa18-floats-like");
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(WaterUp + c.Floats!.KeelZ - 0.25)), Quat.Identity, Vec3.Zero, Vec3.Zero));
        var relay = new CrashRelay(ac);
        var loop = new SimLoop(ac);
        double dt = SimLoop.DefaultFixedDtSec;
        for (int i = 0; i < (int)(20 / dt); i++) { loop.RunFor(dt, new ControlInputs(0, 0, 0, 1.0)); relay.Step(dt); }   // settle
        if (damaged) for (int k = 0; k < 4; k++) ac.PunctureFloat(FloatPuncture.RockStrike(0, k));
        Header(title + " (t from the moment of damage; depth = CG; extra: per-float water)");
        double next = 0;
        for (int i = 0; ; i++)
        {
            double t = i * dt;
            if (i > 0) { loop.RunFor(dt, new ControlInputs(0, 0, 0, 1.0)); relay.Step(dt); }
            if (Sample(t, ref next) || i == 0)
                Line(title, "floatplane", t, ac.State.Position.Z + WaterUp, ac.State.Attitude.Rotate(ac.State.Velocity).Z,
                    ac.FloatFlood!.TrappedAirM3, FloatHydro.Floats[0].BuoyancyN + FloatHydro.Floats[1].BuoyancyN + ac.Hydro.BuoyancyN(ac.State), "",
                    $"roll {RollDeg(ac.State.Attitude),6:F1} deg  water L {ac.FloatFlood.WaterKgIn(0),6:F0} kg R {ac.FloatFlood.WaterKgIn(1),6:F0} kg");
            if (t >= 1800) break;
        }
        _txt.AppendLine();
        relay.Detach();
    }
}

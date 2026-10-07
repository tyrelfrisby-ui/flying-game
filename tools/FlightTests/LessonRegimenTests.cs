using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Overnight regimen (owner 2026-10-06: "put the aircraft through a test regimen overnight, test every lesson in the
/// syllabus"): every fleet type × every lesson × every wind and flap choice the menu offers × three pilots (ace = the
/// game's own law on the user's axes with firm braking; hands-off = controls released, no brakes; student = the law a
/// third of a second late at 70 % gain, heavy on the brakes). Writes one CSV row per run; tools/regimen/report.py flags
/// the anomalies. Opt-in: REGIMEN=1, REGIMEN_AIRCRAFT=id[,id…], REGIMEN_OUT=file.csv.
/// </summary>
[Collection("WorldTerrainActive")]
public class LessonRegimenTests
{
    private readonly ITestOutputHelper _out;
    public LessonRegimenTests(ITestOutputHelper o) { _out = o; }

    private static readonly PracticeKind[] Landing = { PracticeKind.LandingRudder, PracticeKind.LandingAileron, PracticeKind.Flare, PracticeKind.FlareSideView, PracticeKind.ApproachSideView };
    private static readonly PracticeWind[] Cross = { PracticeWind.Steady, PracticeWind.Gusty, PracticeWind.Shifting };
    private static readonly PracticeWind[] Along = { PracticeWind.Calm, PracticeWind.Headwind, PracticeWind.HeadwindGusty, PracticeWind.Tailwind, PracticeWind.TailwindGusty };

    public static IEnumerable<(PracticeKind kind, PracticeWind wind, double flaps)> Syllabus(bool hasFlaps)
    {
        double[] flaps = hasFlaps ? new[] { 0.0, 0.5, 1.0 } : new[] { 0.0 };
        foreach (PracticeKind k in Enum.GetValues<PracticeKind>())
        {
            bool xw = k is PracticeKind.CrosswindRudder or PracticeKind.CrosswindAileron or PracticeKind.LandingRudder or PracticeKind.LandingAileron;
            PracticeWind[] winds = xw ? Cross : (Landing.Contains(k) || k is PracticeKind.GlideRear or PracticeKind.GlideSide) ? Along : new[] { PracticeWind.Calm };
            foreach (var w in winds)
                foreach (var f in Landing.Contains(k) ? flaps : new[] { 0.0 })
                    yield return (k, w, f);
        }
    }

    [Fact]
    public void FlyTheWholeSyllabus()
    {
        if (Environment.GetEnvironmentVariable("REGIMEN") != "1") return;
        string outPath = Environment.GetEnvironmentVariable("REGIMEN_OUT") ?? Path.Combine(Path.GetTempPath(), "regimen.csv");
        string[] ids = (Environment.GetEnvironmentVariable("REGIMEN_AIRCRAFT") ?? "c172-like").Split(',', StringSplitOptions.RemoveEmptyEntries);
        string[] pilots = (Environment.GetEnvironmentVariable("REGIMEN_PILOTS") ?? "ace,handsoff,student").Split(',');
        bool header = !File.Exists(outPath);
        using var csv = new StreamWriter(outPath, append: true) { AutoFlush = true };
        if (header) csv.WriteLine(Row.Header);
        foreach (string id in ids)
        {
            var probe = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            bool hasFlaps = probe.Surfaces.Any(s => s.Strips.Any(st => st.Flap != null));
            string? only = Environment.GetEnvironmentVariable("REGIMEN_KINDS");
            foreach (var (kind, wind, flaps) in Syllabus(hasFlaps).Where(x => only == null || only.Split(',').Contains(x.kind.ToString())))
                foreach (string pilot in pilots)
                {
                    var r = Fly(id, kind, wind, flaps, pilot);
                    csv.WriteLine(r.ToCsv());
                    _out.WriteLine(r.ToCsv());
                }
        }
    }

    public sealed class Row
    {
        public string Id = "", Kind = "", Wind = "", Pilot = "", Phase = "", End = "", Verdict = "", Braking = "", Error = "";
        public double Flaps, SimSec, WallSec, Score, InBand, TdSink, TdAlign, TdOff, TdAlongM, X50M = double.NaN, FloatM = double.NaN, RollM = double.NaN, AvgDecelG, PeakDecelG, MaxG, MaxSwing, TdKt, V50Kt;
        public int Bounces, NoseFirst, Lost; public bool Touched, WingsFailed, TailLifted, NaN;
        public const string Header = "aircraft,lesson,wind,flaps,pilot,phase,end,score,inband,touched,td_sink_ms,td_align_deg,td_off_m,td_along_m,v50_kt,td_kt,float_m,roll_m,bounces,nose_first,braking,avg_decel_g,peak_decel_g,tail_lifted,max_swing_deg,max_g,wings_failed,lost,nan,sim_s,wall_s,verdict,error";
        private static string Q(string s) => "\"" + s.Replace("\"", "'").Replace("\n", " ") + "\"";
        private static string F(double d) => double.IsNaN(d) ? "" : d.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        public string ToCsv() => string.Join(",", Id, Kind, Wind, F(Flaps), Pilot, Phase, Q(End), F(Score), F(InBand), Touched, F(TdSink), F(TdAlign), F(TdOff), F(TdAlongM), F(V50Kt), F(TdKt), F(FloatM), F(RollM), Bounces, NoseFirst, Braking, F(AvgDecelG), F(PeakDecelG), TailLifted, F(MaxSwing), F(MaxG), WingsFailed, Lost, NaN, F(SimSec), F(WallSec), Q(Verdict), Q(Error));
    }

    private static Row Fly(string id, PracticeKind kind, PracticeWind wind, double flaps, string pilot)
    {
        var r = new Row { Id = id, Kind = kind.ToString(), Wind = wind.ToString(), Flaps = flaps, Pilot = pilot };
        var sw = Stopwatch.StartNew();
        try
        {
            var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            WorldTerrain.Active = null;
            Atmosphere.ThermalStrengthScale = 0.0; Atmosphere.SlopeLiftEnabled = false;   // the lessons fly in still air
            var sc = new PracticeScenario(kind, wind, c, PracticeScenarioTests.TestRunway(), 0.0, seed: 7, flapFraction: flaps);
            var ac = sc.Spawn(); var sim = new SimLoop(ac); sc.SkipBriefing();
            double maxSec = kind switch
            {
                PracticeKind.ClimbLevelDescend => 600, PracticeKind.GlideRear or PracticeKind.GlideSide or PracticeKind.ClimbVyRear or PracticeKind.ClimbVxSide => 450,
                PracticeKind.STurns or PracticeKind.StallSideView or PracticeKind.StallRudder or PracticeKind.StallElevator or PracticeKind.Straight => 120,   // endless: two minutes
                _ => 300,
            };
            const double dt = 0.02, delay = 0.34;
            var lag = new Queue<ControlInputs>();
            ControlInputs user = ControlInputs.Neutral; double? heldLever = null; double tTd = -1;
            double kts = 1.943844, prevVz = 0; double maxSwing = 0, maxDrop = 0, stallT = 0; int stallPhase = 0;
            double t = 0;
            for (; t < maxSec && sc.Phase != PracticePhase.Finished; t += dt)
            {
                var inputs = sc.Step(ac, user, dt);
                if (sc.TouchedDown && tTd < 0) { tTd = t; r.TdAlongM = sc.AlongM; r.TdKt = sc.AirspeedMs * kts; }
                // brakes: the game's own on the lessons that keep them; the user's on the idle-power landings
                double userBrake = !sc.UserBrakes || tTd < 0 || t < tTd + 1.0 ? 0.0 : pilot switch { "ace" => sc.Taildragger ? 0.25 : 0.4, "student" => sc.Taildragger ? 0.6 : 0.9, _ => 0.0 };
                ac.BrakeInput = Math.Max(sc.GameBrake, userBrake); ac.BrakeBias = sc.GameBrakeBias;
                sim.RunFor(dt, inputs);
                sc.ConstrainLongitudinal(ac);
                var auto = sc.Autopilot;
                heldLever ??= auto.ThrottleLever;
                // The stall lessons: the user's job is to stall it and recover (the game's own law never stalls it) —
                // pull past the stall, hold, push to recover, again; the rudder lesson holds the wing drop with rudder.
                if (sc.Stall && sc.UserHasControl && pilot != "handsoff")
                {
                    double g = pilot == "ace" ? 1.0 : 0.6;
                    // State machine, the way it's taught: pull to the break, hold a second, release and lower the nose until it
                    // flies again (1.25 Vso), level off for a few seconds, again. (A blind 8 s pull / 4 s push spun them.)
                    stallT += dt;
                    switch (stallPhase)
                    {
                        case 0: if (sc.Stalled) { stallPhase = 1; stallT = 0; } break;                                  // pulling
                        case 1: if (stallT > (pilot == "ace" ? 1.0 : 2.0)) { stallPhase = 2; stallT = 0; } break;       // at the break
                        case 2: if (!sc.Stalled && sc.AirspeedMs > 1.25 * sc.VsoMs) { stallPhase = 3; stallT = 0; } break; // recovering
                        case 3: if (stallT > 4.0) { stallPhase = 0; stallT = 0; } break;                                 // level off
                    }
                    // Fly attitudes (stick + = nose down): raise the nose to 12° and hold it while the speed bleeds into the stall,
                    // more back stick at the break, release to lower the nose (no steeper than −10°), level off at +3°.
                    var aq = ac.State.Attitude; double pitchDeg = Math.Asin(Math.Clamp(2 * (aq.W * aq.Y - aq.Z * aq.X), -1, 1)) * 57.3, qd = ac.State.Rates.Y;
                    double Hold(double thetaDeg) => Math.Clamp(sc.TrimStick - 0.08 * (thetaDeg - pitchDeg) + 0.6 * qd, -0.8, 0.5);
                    double ele = kind == PracticeKind.StallRudder ? auto.Elevator : stallPhase switch
                    {
                        0 => Hold(12), 1 => Math.Min(Hold(14), -0.5),
                        2 => Math.Max(sc.TrimStick + 0.15, Hold(-10)),
                        _ => Hold(3),
                    };
                    double rud = kind == PracticeKind.StallRudder ? Math.Clamp(g * (-0.8 * sc.BankDeg / 57.3 - 0.3 * sc.RollRateDegS / 57.3), -1, 1) : auto.Rudder;
                    user = new ControlInputs(auto.Aileron, ele, rud, auto.ThrottleLever);
                    maxDrop = Math.Max(maxDrop, Math.Abs(sc.BankDeg));
                    if (Environment.GetEnvironmentVariable("REGIMEN_TRACE") != null && Math.Abs(t * 2 - Math.Round(t * 2)) < dt) Console.WriteLine($"TR t {t,5:F1} ph {stallPhase} agl {sc.AglM,5:F0} ias {sc.AirspeedMs * 1.944,4:F0} stalled {sc.Stalled} alpha {sc.AlphaDeg,5:F1} bank {sc.BankDeg,5:F0} ele {ele,5:F2} g {ac.LoadFactorZ,5:F1}");
                }
                else switch (pilot)
                {
                    case "ace": user = auto; break;
                    case "handsoff": user = new ControlInputs(0, sc.TrimStick, 0, heldLever.Value, true, true, true); break;
                    default:
                        lag.Enqueue(auto);
                        if (lag.Count > (int)(delay / dt)) { var a = lag.Dequeue(); user = new ControlInputs(0.7 * a.Aileron, sc.TrimStick + 0.7 * (a.Elevator - sc.TrimStick), 0.7 * a.Rudder, a.ThrottleLever); }
                        break;
                }
                if ((sc.FlareExercise || sc.Approach) && double.IsNaN(r.X50M) && sc.MainsAglM <= PracticeScenario.FiftyFtM) { r.X50M = sc.AlongM; r.V50Kt = sc.AirspeedMs * kts; }
                double vz = ac.State.Velocity.Z, nz = 1 + (vz - prevVz) / dt / 9.81; prevVz = vz;   // crude vertical-load proxy (spikes on touchdown)
                if (t > 1) r.MaxG = Math.Max(r.MaxG, Math.Abs(nz));
                if (sc.TouchedDown) maxSwing = Math.Max(maxSwing, Math.Abs(sc.AlignmentDeg));
                if (double.IsNaN(ac.State.Position.X) || double.IsNaN(sc.AirspeedMs)) { r.NaN = true; break; }
            }
            Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null;
            r.SimSec = t; r.Phase = sc.Phase.ToString(); r.End = sc.EndReason; r.Verdict = sc.Verdict; r.Score = sc.Score; r.InBand = sc.InBandFraction;
            if (sc.Stall) r.Verdict = $"stalls {sc.StallCount}, max wing drop {sc.MaxWingDropDeg:F0}° (any {maxDrop:F0}°), max sink {sc.MaxSinkMs:F1} m/s, agl {sc.AglM:F0} m; {sc.Verdict}";
            r.Touched = sc.TouchedDown; r.TdSink = sc.TouchdownSinkMs; r.TdAlign = sc.TouchdownAlignDeg; r.TdOff = sc.TouchdownOffCentreM;
            if (!double.IsNaN(r.X50M) && sc.TouchedDown) r.FloatM = r.TdAlongM - r.X50M;
            if (sc.TouchedDown) r.RollM = sc.AlongM - r.TdAlongM;
            r.Bounces = sc.BounceHeightsM.Count(h => h > 0.15); r.NoseFirst = sc.NoseFirstTouches;
            var bm = sc.Judge.Moments.FirstOrDefault(m => m.name == LessonJudge.Std.Braking.Name);
            r.Braking = bm.name == null ? "" : bm.grade.ToString();
            r.AvgDecelG = sc.RolloutAvgDecelG; r.PeakDecelG = sc.RolloutPeakDecelG; r.TailLifted = sc.RolloutTailLifted;
            r.MaxSwing = Math.Max(maxSwing, sc.MaxRolloutSwingDeg); r.WingsFailed = ac.Structure.WingsFailed; r.Lost = ac.LostComponents.Count;
        }
        catch (Exception e) { r.Error = e.GetType().Name + ": " + e.Message; r.Phase = "Exception"; }
        finally { Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.ThermalStrengthScale = 1.0; }
        r.WallSec = sw.Elapsed.TotalSeconds;
        return r;
    }
}

[Collection("WorldTerrainActive")]
public class LessonTraceTests
{
    private readonly ITestOutputHelper _out;
    public LessonTraceTests(ITestOutputHelper o) { _out = o; }

    /// <summary>TRACE_LESSON=aircraft,kind,wind,flaps — the roll-out yaw loop at 20 Hz (rudder asked vs surface vs yaw rate).</summary>
    [Fact]
    public void TraceRollout()
    {
        string? spec = Environment.GetEnvironmentVariable("TRACE_LESSON"); if (spec == null) return;
        var p = spec.Split(',');
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", p[0] + ".json"));
        WorldTerrain.Active = null; Atmosphere.ThermalStrengthScale = 0;
        var sc = new PracticeScenario(Enum.Parse<PracticeKind>(p[1]), Enum.Parse<PracticeWind>(p[2]), c, PracticeScenarioTests.TestRunway(), 0.0, seed: 7, flapFraction: double.Parse(p[3]));
        var ac = sc.Spawn(); var sim = new SimLoop(ac); sc.SkipBriefing();
        ControlInputs user = ControlInputs.Neutral; double fdt = p.Length > 4 ? double.Parse(p[4]) : 0.02; double? tTd = null;
        for (double t = 0; t < 200 && sc.Phase != PracticePhase.Finished; t += fdt)
        {
            var inputs = sc.Step(ac, user, fdt);
            ac.BrakeInput = sc.GameBrake; ac.BrakeBias = sc.GameBrakeBias;
            if (Environment.GetEnvironmentVariable("TRACE_BRAKE") is string tb && sc.TouchedDown) { tTd ??= t; if (t > tTd + 1.0) { ac.BrakeInput = double.Parse(tb); ac.BrakeBias = 0; } }
            sim.RunFor(fdt, inputs); sc.ConstrainLongitudinal(ac);
            user = sc.Autopilot;
            if (Environment.GetEnvironmentVariable("TRACE_ALL") != null ? Math.Abs(t * 4 - Math.Round(t * 4)) < fdt * 2 : (sc.MainsAglM < 1.0 || sc.TouchedDown) && Math.Abs(t * 10 - Math.Round(t * 10)) < fdt * 5)
                _out.WriteLine($"t {t,6:F2} int {sc.ElevIntDbg,5:F2} herr {sc.HErrDbg,5:F2} hdT {sc.HdotTDbg,5:F2} mains {sc.MainsAglM,6:F2} sink {sc.SinkMs,5:F2} pitch {Math.Asin(Math.Clamp(2 * (ac.State.Attitude.W * ac.State.Attitude.Y - ac.State.Attitude.Z * ac.State.Attitude.X), -1, 1)) * 57.3,5:F1} agl {sc.AglM,6:F0} stalled {(sc.Stalled ? 1 : 0)} ele {inputs.Elevator,5:F2} lev {inputs.ThrottleLever,5:F2} phase {sc.Phase} gnd {(sc.OnGround ? 1 : 0)} ias {sc.AirspeedMs * 1.944,4:F0} align {sc.AlignmentDeg,6:F1} off {sc.OffCentreM,5:F1} r {ac.State.Rates.Z * 57.3,6:F1}°/s rudCmd {inputs.Rudder,5:F2} rudSurf {ac.CurrentDeflections.RudderRad * 57.3,6:F1}° brake {ac.BrakeInput:F2}/{ac.BrakeBias:F2}");
        }
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.ThermalStrengthScale = 1;
        _out.WriteLine($"{sc.EndReason} swing {sc.MaxRolloutSwingDeg:F0}° verdict {sc.Verdict} lost [{string.Join(",", ac.LostComponents)}] moments: {string.Join("; ", sc.Judge.Moments.Select(m => m.name + " " + m.grade + " " + m.detail))}");
    }
}

public class TrimProbeTests
{
    private readonly ITestOutputHelper _out;
    public TrimProbeTests(ITestOutputHelper o) { _out = o; }
    /// <summary>TRIM_PROBE=aircraft,flaps — the glide trim across speeds (converged? alpha, elevator, L/D).</summary>
    [Fact]
    public void Probe()
    {
        string? spec = Environment.GetEnvironmentVariable("TRIM_PROBE"); if (spec == null) return;
        var p = spec.Split(',');
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", p[0] + ".json"));
        for (double v = 40; v <= 120; v += 10)
        {
            var t = TrimSolver.SolveGliderTrim(c, v, 300, flapFraction: double.Parse(p[1]));
            _out.WriteLine($"v {v * 1.944,4:F0} kt conv {t.Converged} alpha {t.AlphaRad * 57.3,6:F1} elev {t.ElevatorRad * 57.3,6:F1} (max {c.Controls.Elevator.MaxDeflRad * 57.3:F0}) L/D {t.GlideRatio:F1}");
        }
    }
}

[Collection("WorldTerrainActive")]
public class WaterLaneLessonTests
{
    private readonly ITestOutputHelper _out;
    public WaterLaneLessonTests(ITestOutputHelper o) { _out = o; }

    /// <summary>Owner 2026-10-07: the seaplanes' landing lessons fly onto a buoyed water lane. Flat water at 0 (the test
    /// world has no lakes): the game's own law lands it on the water, no wheel brakes, and the run ends off the step.</summary>
    [Theory]
    [InlineData("pa18-floats-like", PracticeKind.Flare)]
    [InlineData("dhc2-beaver-floats-like", PracticeKind.FlareSideView)]
    [InlineData("pa18-floats-like", PracticeKind.LandingAileron)]
    [InlineData("hughes-h4-like", PracticeKind.Flare)]
    public void SeaplanesLandOnTheWaterLane(string id, PracticeKind kind)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            var lane = new WorldTerrain.RunwayEnd(new WorldTerrain.Strip("water", 0, 0, 2400, 60, 0), 0, 0, 0);
            var sc = new PracticeScenario(kind, kind == PracticeKind.LandingAileron ? PracticeWind.Steady : PracticeWind.Calm, c, lane, 0.0, flapFraction: 1.0);
            Assert.True(sc.WaterLane); Assert.False(sc.UserBrakes);
            var ac = sc.Spawn(); var sim = new SimLoop(ac); sc.SkipBriefing();
            ControlInputs user = ControlInputs.Neutral;
            for (double t = 0; t < 200 && sc.Phase != PracticePhase.Finished; t += 0.02)
            {
                var inputs = sc.Step(ac, user, 0.02);
                ac.BrakeInput = sc.GameBrake; ac.BrakeBias = sc.GameBrakeBias;
                sim.RunFor(0.02, inputs); sc.ConstrainLongitudinal(ac);
                user = sc.Autopilot;
            }
            _out.WriteLine($"{id} {kind}: {sc.EndReason}, touchdown sink {sc.TouchdownSinkMs:F1} m/s at {sc.AlongM:F0} m, lost [{string.Join(",", ac.LostComponents)}] — {sc.Verdict} {sc.Score:F0}");
            Assert.True(sc.TouchedDown, "never reached the water");
            Assert.Equal(PracticePhase.Finished, sc.Phase);
            Assert.Equal(0.0, sc.GameBrake);
            Assert.Empty(ac.LostComponents);
        }
        finally { FloatHydro.FlatWaterOverride = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; }
    }
}

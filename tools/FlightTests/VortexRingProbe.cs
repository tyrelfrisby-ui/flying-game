using System;
using System.Globalization;
using System.IO;
using System.Text;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE WHOLE AXIAL RANGE OF THE DISC, MEASURED (VRS=1). Ty 2026-11-11:
///
///   "Sometimes lower like a tail slide where the reversed direction works against the prop and can actually stop the
///    engine if rpm falls below around 300 rpm."
///
/// This is the before-and-after table for that. It sweeps the axial velocity from strong forward flight, through
/// zero, into strongly reversed flow — the four rotor working states in one pass: normal working, vortex ring,
/// turbulent wake, windmill brake — at several rpm and blade angles, and records thrust, torque, the disc-mean
/// induced velocity and the state the disc is in. <c>VRS=1</c> runs it; <c>VRS_OUT=&lt;dir&gt;</c> writes the tables
/// to files so a before and an after can be diffed instead of eyeballed.
///
/// It is a PROBE, not a test: it asserts nothing about the numbers. <see cref="VortexRingTests"/> holds the
/// assertions.
/// </summary>
public class VortexRingProbe
{
    private readonly ITestOutputHelper _o;
    public VortexRingProbe(ITestOutputHelper o) { _o = o; }

    private static string ConfigDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return Path.Combine(d.FullName, "configs", "aircraft");
    }
    internal static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(ConfigDir(), id + ".json"));

    /// <summary>The fleet this sweep covers: the two Ty flew the defect on, plus a constant-speed type and a type
    /// with a different blade count, so a change that only suits one propeller shows up.</summary>
    internal static readonly string[] Fleet = { "pitts-s2b-like", "j3-cub-like", "great-lakes-like", "c172-like" };

    /// <summary>Forward flight, through zero, into the tailslide. Negative is the aeroplane going backwards.</summary>
    internal static readonly double[] Sweep =
    {
        60.0, 50.0, 40.0, 30.0, 25.0, 20.0, 15.0, 10.0, 5.0, 2.0, 0.5, 0.0,
        -0.5, -2.0, -5.0, -8.0, -12.0, -16.0, -20.0, -25.0, -30.0, -40.0
    };

    private void Emit(string name, StringBuilder sb)
    {
        _o.WriteLine(sb.ToString());
        string? dir = Environment.GetEnvironmentVariable("VRS_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), sb.ToString());
    }

    /// <summary>
    /// ITEM 1. The disc swept through the whole axial range, at the blade angle and the rpm a tailslide actually
    /// happens at (power off, idle) and at a cruise setting, so what moves in reversed flow can be read against what
    /// must not move in forward flight.
    /// </summary>
    [Fact]
    public void TheDiscThroughEveryWorkingState()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        var sb = new StringBuilder();
        sb.AppendLine("# blade element swept through the four working states, sea level");
        sb.AppendLine("# aircraft,pitch_deg,rpm,vaxial_ms,thrust_N,torque_Nm,induced_ms,vbar");
        foreach (string id in Fleet)
        {
            AircraftConfig c = Load(id);
            PropulsionConfig? p = c.Propulsion;
            if (p is null || p.PropDiameterM <= 0.0) continue;
            double pitch = PistonCompression.FinePitchDeg(p);
            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
            foreach (double rpm in new[] { 500.0, 700.0, 1000.0, 1500.0, 2300.0 })
                foreach (double v in Sweep)
                {
                    BladeElement.Result r = BladeElement.Solve(g, v, 0.0, rpm, rho);
                    // vh from the disc loading is what the universal curve is drawn against: V/vh is the state.
                    double vh = Math.Sqrt(Math.Abs(r.ThrustN) / Math.Max(1e-9, 2.0 * rho * Math.PI * g.RadiusM * g.RadiusM));
                    double vbar = vh > 1e-6 ? v / vh : double.NaN;
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"{id},{pitch:F2},{rpm:F0},{v:F1},{r.ThrustN:F2},{r.TorqueNm:F3},{r.InducedMs:F3},{vbar:F3}"));
                }
        }
        Emit("disc-sweep.csv", sb);
    }

    /// <summary>
    /// ITEM 2. THE TORQUE BALANCE AT IDLE, which is what actually decides whether the rpm rises or falls. The shaft
    /// accelerates when the engine's idle torque beats the propeller's; so at each reversed speed, print both.
    /// A positive margin is a propeller the engine can keep spinning; a negative one is a propeller dragging the
    /// engine down toward the 300 rpm stop.
    /// </summary>
    [Fact]
    public void TheIdleTorqueMarginInReversedFlow()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        var sb = new StringBuilder();
        sb.AppendLine("# idle torque margin, sea level: engine torque at idle throttle minus the propeller's");
        sb.AppendLine("# aircraft,rpm,vaxial_ms,q_prop_Nm,q_engine_Nm,margin_Nm");
        foreach (string id in Fleet)
        {
            AircraftConfig c = Load(id);
            PropulsionConfig? p = c.Propulsion;
            if (p is null || p.PropDiameterM <= 0.0) continue;
            double pitch = PistonCompression.FinePitchDeg(p);
            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
            double idleThrottle = PistonEngine.IdleBleedThrottle(g, p, 1.0, rho);
            foreach (double rpm in new[] { 350.0, 500.0, 700.0, 900.0 })
                foreach (double v in Sweep)
                {
                    double qProp = BladeElement.Solve(g, v, 0.0, rpm, rho).TorqueNm;
                    double qEng = PistonEngine.TorqueNm(p, rpm, idleThrottle, 1.0);
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"{id},{rpm:F0},{v:F1},{qProp:F3},{qEng:F3},{qEng - qProp:F3}"));
                }
        }
        Emit("idle-margin.csv", sb);
    }

    /// <summary>
    /// ITEM 3. THE TAILSLIDE ITSELF: the shaft integrated, throttle closed, the aeroplane held going backwards, and
    /// the rpm logged. This is the acceptance condition in Ty's own terms — does it fall, and does it reach the
    /// 300 rpm static-friction stop (<see cref="ImpulseCoupling.StaticFrictionStopRpm"/>), and how long does it take.
    /// </summary>
    [Fact]
    public void ATailslideOnTheIntegratedShaft()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        var sb = new StringBuilder();
        sb.AppendLine("# throttle closed, held at a reversed airspeed, the shaft integrated at 200 Hz");
        sb.AppendLine("# aircraft,vaxial_ms,t_s,rpm,stopped");
        foreach (string id in new[] { "pitts-s2b-like", "j3-cub-like" })
            foreach (double v in new[] { -15.0, -20.0, -25.0, -30.0, -40.0 })
            {
                var run = Tailslide(id, v, 12.0);
                foreach ((double t, double rpm, bool stopped) in run.Log)
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{id},{v:F1},{t:F2},{rpm:F1},{(stopped ? 1 : 0)}"));
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"# {id} at {v:F0} m/s: start {run.StartRpm:F0} rpm, end {run.EndRpm:F0} rpm, " +
                    $"{(double.IsNaN(run.StopSeconds) ? "NEVER STOPPED" : $"stopped at {run.StopSeconds:F2} s")}"));
            }
        Emit("tailslide.csv", sb);
    }


    /// <summary>
    /// ITEM 4. INSIDE THE DISC, element by element, at the conditions that matter: standing still, and flown
    /// backwards. What a reversed free stream does to the angle of attack of each station is the whole story, and it
    /// is not something to reason about from the totals.
    /// </summary>
    [Fact]
    public void InsideTheDiscElementByElement()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        var sb = new StringBuilder();
        sb.AppendLine("# aircraft,vaxial_ms,x,theta_deg,phi_deg,alpha_deg,cn,ct,vi_ms,dT_N,dQ_Nm");
        var tr = new double[24 * BladeElement.TraceStride];
        foreach (string id in new[] { "pitts-s2b-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double pitch = PistonCompression.FinePitchDeg(p);
            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
            foreach (double v in new[] { 0.0, -8.0, -12.0, -16.0, -20.0, -30.0 })
            {
                BladeElement.ElementTrace = tr;
                BladeElement.Result r = BladeElement.Solve(g, v, 0.0, 700.0, rho);
                BladeElement.ElementTrace = null;
                sb.AppendLine($"# {id} at {v:F0} m/s, 700 rpm, pitch {pitch:F2} deg: thrust {r.ThrustN:F1} N, torque {r.TorqueNm:F2} N.m");
                for (int i = 0; i < 24; i++)
                {
                    int o = i * BladeElement.TraceStride;
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"{id},{v:F1},{tr[o]:F3},{tr[o + 1]:F2},{tr[o + 2]:F2},{tr[o + 3]:F2},{tr[o + 4]:F3},{tr[o + 5]:F4},{tr[o + 6]:F3},{tr[o + 7]:F2},{tr[o + 8]:F3}"));
                }
            }
        }
        Emit("inside-disc.csv", sb);
    }


    /// <summary>
    /// ITEM 5. WHAT THE POST-STALL CHANGE COSTS THE FLEET'S CALIBRATION, measured both ways in one binary through
    /// <see cref="BladeElement.LegacyPostStall"/>. Only five types fly the blade-element model at all — the two J-3s,
    /// which carry a fitted bladePitch75Deg, and the three constant-speed types, whose governor holds the rpm — so
    /// this is the whole of the forward-flight blast radius, not a sample of it.
    ///
    /// The J-3's anchors are both Ty's: 2,150 rpm static at full power ("just use 2150 static on both the wood and
    /// metal"), and the 2,575 rpm redline reached at 84 mph at full throttle, which he confirmed.
    /// </summary>
    [Fact]
    public void WhatThePlateSectionCostsTheCalibration()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        var sb = new StringBuilder();
        try
        {
            foreach (bool legacy in new[] { true, false })
            {
                BladeElement.LegacyPostStall = legacy;
                sb.AppendLine($"\n### post-stall branch: {(legacy ? "the 2026-11-10 decay (before)" : "Viterna plate (after)")}");
                foreach (string id in new[] { "j3-cub-like", "j3-cub-floats-like" })
                {
                    AircraftConfig c = Load(id);
                    PropulsionConfig p = c.Propulsion!;
                    double fitted = p.BladePitch75Deg;
                    var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, fitted, p.ActivityFactor);
                    double staticRpm = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, 0.0, rho);
                    // 84 mph = 37.55 m/s, full throttle: the rpm it reaches there.
                    double rpm84 = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, 37.55, rho);
                    double refit = PistonEngine.FitPitchForStaticRpm(p.PropBladeCount, 0.5 * p.PropDiameterM, p.ActivityFactor, 0.15,
                                                                     2150.0, PistonEngine.Spec.For(p), p.IdleRpm);
                    sb.AppendLine($"  {id,-22} pitch {fitted:F2} deg: static {staticRpm,7:F1} rpm (anchor 2150), " +
                                  $"at 84 mph {rpm84,7:F1} rpm (redline {p.MaxRpm:F0}); refit would want {refit:F2} deg");
                }
                foreach (string id in new[] { "pitts-s2b-like", "great-lakes-like", "extra-300-like" })
                {
                    AircraftConfig c = Load(id);
                    PropulsionConfig p = c.Propulsion!;
                    var stops = new BladeElement.Stops(p.FineStopDeg, p.CoarseStopDeg);
                    var fine = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, p.FineStopDeg, p.ActivityFactor);
                    BladeElement.Result st = BladeElement.Solve(fine, 0.0, 0.0, 2300.0, rho);
                    sb.AppendLine($"  {id,-22} fine stop {p.FineStopDeg:F2} deg, static at 2300 rpm: " +
                                  $"thrust {st.ThrustN,8:F1} N, torque {st.TorqueNm,7:F2} N.m; stops {stops.FineDeg:F2}/{stops.CoarseDeg:F2}");
                }
            }
        }
        finally { BladeElement.LegacyPostStall = false; }
        Emit("calibration-cost.txt", sb);
    }


    /// <summary>
    /// ITEM 6. RE-DERIVING `PropModel.StoppedDragFraction` THE WAY IT WAS DERIVED. That constant's own provenance is
    /// a blade check: 0.5 was kept because the J-3's authored coefficient gave Cd 0.075 on disc area against the
    /// blade's 0.073, "two independent routes to two per cent". The route is the method; the corrected post-stall
    /// section gives it a different answer, so this runs the same derivation again and prints where the blade puts
    /// the fraction — legacy and plate side by side, so the move is visible rather than asserted.
    ///
    /// It also prints the stopped drag of every modelled type at both stops, and the stopped-against-windmilling
    /// comparison, because those are the pins that depend on it.
    /// </summary>
    [Fact]
    public void RederiveTheStoppedDragFractionFromTheBlade()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3, v = 40.0;
        var sb = new StringBuilder();
        try
        {
            foreach (bool legacy in new[] { true, false })
            {
                BladeElement.LegacyPostStall = legacy;
                sb.AppendLine($"\n### post-stall branch: {(legacy ? "the 2026-11-10 decay (before)" : "Viterna plate (after)")}");

                // THE DERIVATION, on the one type that has both a fitted blade and the authored coefficient.
                AircraftConfig j3 = Load("j3-cub-like");
                PropulsionConfig jp = j3.Propulsion!;
                double jr = 0.5 * jp.PropDiameterM, jdisc = Math.PI * jr * jr, jq = 0.5 * rho * v * v * jdisc;
                double jBlade = BladeElement.StoppedDragN(new BladeElement.Geometry(jp.PropBladeCount, jr, jp.BladePitch75Deg, jp.ActivityFactor), v, rho);
                double jCd = jBlade / jq;
                sb.AppendLine($"  J-3 fitted blade {jp.BladePitch75Deg:F2} deg, stopped at {v:F0} m/s: {jBlade:F1} N, Cd {jCd:F4} on disc area");
                sb.AppendLine($"  its IdleDragCd is {jp.IdleDragCd:F4}, so the fraction the blade asks for is {jCd / jp.IdleDragCd:F4}");
                sb.AppendLine($"  (the constant is {PropModel.StoppedDragFraction:F4}, giving {jp.IdleDragCd * PropModel.StoppedDragFraction:F4})");

                // The coarse-stop figures quoted in the constant's own documentation.
                foreach (string id in new[] { "pitts-s2b-like", "great-lakes-like", "extra-300-like" })
                {
                    AircraftConfig c = Load(id);
                    PropulsionConfig p = c.Propulsion!;
                    double r = 0.5 * p.PropDiameterM, disc = Math.PI * r * r, q = 0.5 * rho * v * v * disc;
                    double coarse = BladeElement.StoppedDragN(new BladeElement.Geometry(p.PropBladeCount, r, p.CoarseStopDeg, p.ActivityFactor), v, rho);
                    double fine = BladeElement.StoppedDragN(new BladeElement.Geometry(p.PropBladeCount, r, p.FineStopDeg, p.ActivityFactor), v, rho);
                    sb.AppendLine($"  {id,-20} {p.PropBladeCount} blades: coarse {coarse,7:F1} N (Cd {coarse / q:F4}), fine {fine,7:F1} N (Cd {fine / q:F4}), coarse/fine {coarse / fine:F3}");
                }

                // The stopped-against-windmilling comparison, at the same blade, where the margin pins live.
                foreach (string id in new[] { "extra-300-like", "great-lakes-like", "j3-cub-like" })
                {
                    AircraftConfig c = Load(id);
                    PropulsionConfig p = c.Propulsion!;
                    double vv = 1.3 * FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(c, 0.0, 0.0);
                    double pitch = PistonCompression.FinePitchDeg(p);
                    var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
                    double EngineQ(double r) => PistonEngine.MotoringTorqueNm(p, r, 0.0, 1.0, 1.0);
                    double stopped = BladeElement.StoppedDragN(g, vv, rho);
                    BladeElement.Windmilling w = BladeElement.Windmill(g, vv, rho, p.MaxRpm, EngineQ);
                    double disc = Math.PI * 0.25 * p.PropDiameterM * p.PropDiameterM, q = 0.5 * rho * vv * vv * disc;
                    sb.AppendLine($"  {id,-20} at {pitch:F2} deg, {vv:F0} m/s: stopped {stopped,6:F1} N (Cd {stopped / q:F4}) vs windmilling " +
                                  $"{w.Rpm,6:F0} rpm {w.DragN,6:F1} N (Cd {w.DragN / q:F4}); ratio {stopped / w.DragN:F3}" +
                                  $"; per blade Cd {stopped / q / p.PropBladeCount:F4}");
                }
            }
        }
        finally { BladeElement.LegacyPostStall = false; }
        Emit("stopped-drag-derivation.txt", sb);
    }


    /// <summary>
    /// ITEM 7. WHICH HALF OF THIS LANE BROKE THE ADAPTIVE AZIMUTH COUNT. HotPathCostTests pins the cheap path (four
    /// azimuths, approximate Prandtl, warm start) against the exact one (sixteen, exact, cold) to 1e-3 of itself,
    /// and after this lane the worst thrust disagreement is 7.9e-2. Both sides of that pin run the NEW model, so it
    /// is not a before-and-after: it says the azimuthal integrand got harder. The inflow cannot be the cause —
    /// vAxial is the same at every azimuth and that grid never goes below zero, so every azimuth takes the
    /// normal-working branch, which is the old expression — which leaves the section. This runs the same grid with
    /// the post-stall branch forced both ways and prints the worst case for each, so the attribution is measured.
    /// </summary>
    [Fact]
    public void WhichHalfOfTheLaneBrokeTheAzimuthPin()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        var sb = new StringBuilder();
        (string Name, int Nb, double R, double Pitch)[] blades =
        {
            ("j3", 2, 0.915, 15.49), ("pitts", 2, 0.99, 11.0), ("gl", 2, 0.9398, 12.5),
            ("extra", 3, 1.0, 12.72), ("c172", 2, 0.95, 15.0),
        };
        try
        {
            foreach (bool legacy in new[] { true, false })
            {
                BladeElement.LegacyPostStall = legacy;
                double worstT = 0; string where = "";
                foreach ((string name, int nb, double radius, double pitch) in blades)
                {
                    var g = new BladeElement.Geometry(nb, radius, pitch);
                    foreach (double va in new[] { 0.0, 10.0, 25.0, 40.0, 60.0, 90.0 })
                        foreach (double vc in new[] { 0.001, 0.05, 0.3, 1.0, 3.0, 6.0, 12.0, 25.0, 40.0 })
                            foreach (double rpm in new[] { 500.0, 900.0, 1500.0, 2100.0, 2700.0 })
                                foreach (double rho in new[] { 1.225, 0.9093 })
                                {
                                    BladeElement.AzimuthOverride = 16; BladeElement.ExactPrandtlLoss = true; BladeElement.LegacyInnerLoop = true;
                                    BladeElement.Result was = BladeElement.Solve(g, va, vc, rpm, rho);
                                    BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
                                    BladeElement.Result now = BladeElement.Solve(g, va, vc, rpm, rho);
                                    double sT = Math.Max(Math.Abs(was.ThrustN), 1.0);
                                    double dT = Math.Abs(now.ThrustN - was.ThrustN) / sT;
                                    if (dT > worstT) { worstT = dT; where = $"{name} at {va:F0} m/s axial, {vc:F2} crossflow, {rpm:F0} rpm, rho {rho:F3} ({was.ThrustN:F1} N vs {now.ThrustN:F1} N)"; }
                                }
                }
                sb.AppendLine($"{(legacy ? "OLD post-stall decay" : "Viterna plate      ")}: worst thrust {worstT:E2}  at {where}");
            }
        }
        finally
        {
            BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false;
            BladeElement.LegacyInnerLoop = false; BladeElement.LegacyPostStall = false;
        }
        Emit("azimuth-attribution.txt", sb);
    }


    /// <summary>
    /// ITEM 8. THE AZIMUTH PIN VARIES THREE THINGS AT ONCE, so "the azimuth count" is not what its worst case is
    /// measuring. Its exact side sets AzimuthOverride = 16, ExactPrandtlLoss = true AND LegacyInnerLoop = true
    /// (cold start, 1e-6 tolerance) against a cheap side that has none of them — and the worst case after this lane
    /// sits at 0.001 m/s of crossflow, where every azimuth of the disc is identical and the count cannot matter.
    /// This takes that one condition and turns the three flags one at a time.
    /// </summary>
    [Fact]
    public void WhichFlagMovesTheWindmillingDisc()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        var sb = new StringBuilder();
        var g = new BladeElement.Geometry(2, 0.9398, 12.5);       // the Great Lakes' fine stop, the worst case
        const double va = 60.0, vc = 0.001, rpm = 1500.0, rho = 0.9093;
        try
        {
            foreach (bool legacySection in new[] { true, false })
            {
                BladeElement.LegacyPostStall = legacySection;
                sb.AppendLine($"\n### post-stall: {(legacySection ? "old decay" : "Viterna plate")}  (GL blade, {va:F0} m/s, {vc} crossflow, {rpm:F0} rpm, rho {rho})");
                foreach ((string name, int az, bool exact, bool legacyLoop) in new[]
                {
                    ("exact: 16 az, exact Prandtl, cold start 1e-6", 16, true, true),
                    ("  only 4 azimuths                           ",  4, true, true),
                    ("  only approximate Prandtl                  ", 16, false, true),
                    ("  only warm start + 1e-4 tolerance          ", 16, true, false),
                    ("cheap: adaptive az, approx, warm 1e-4       ",  0, false, false),
                })
                {
                    BladeElement.AzimuthOverride = az; BladeElement.ExactPrandtlLoss = exact; BladeElement.LegacyInnerLoop = legacyLoop;
                    BladeElement.Result r = BladeElement.Solve(g, va, vc, rpm, rho);
                    sb.AppendLine($"  {name}  thrust {r.ThrustN,10:F2} N  torque {r.TorqueNm,9:F2} N.m");
                }
            }
        }
        finally
        {
            BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false;
            BladeElement.LegacyInnerLoop = false; BladeElement.LegacyPostStall = false;
        }
        Emit("flag-attribution.txt", sb);
    }


    /// <summary>
    /// ITEM 9. THE COMBINED REFIT, and the test of whether the composed model is right. Two lanes refitted the J-3's
    /// blade against the SAME anchor on the same day for different reasons — 15.49 here, for the corrected
    /// post-stall section, and 15.62 on lane/claude-fmep-pmep, for FMEP and PMEP replacing the friction intercept —
    /// and neither is correct once both land. This fits one pitch with both in place.
    ///
    /// AND IT IS A TEST, NOT JUST A FIT. Ty gives two figures: 2,150 rpm static at full power, which he measured,
    /// and the 2,575 rpm redline reached at 84 mph full throttle, which he confirmed. They are independent — the
    /// static point has a stalled root and the 84 mph point does not — so one blade angle holding BOTH is a check on
    /// the composed model, and one that cannot hold both is a finding about it. Printed as both columns across a
    /// pitch sweep, so where they each land is visible rather than asserted.
    /// </summary>
    [Fact]
    public void TheCombinedRefitAndWhetherOnePitchHoldsBothAnchors()
    {
        if (Environment.GetEnvironmentVariable("VRS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        var sb = new StringBuilder();
        foreach (string id in new[] { "j3-cub-like", "j3-cub-floats-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            sb.AppendLine($"\n### {id}, config now at {p.BladePitch75Deg:F2} deg");
            sb.AppendLine("  pitch | static rpm (anchor 2150) | at 84 mph (redline 2575)");
            foreach (double pitch in new[] { 15.33, 15.49, 15.62, 15.70, 15.78, 15.85, 16.00 })
            {
                var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
                double st = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, 0.0, rho);
                double r84 = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, 37.55, rho);
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {pitch,5:F2} | {st,8:F1} ({st - 2150.0,+6:F1}) | {r84,8:F1} ({r84 - 2575.0,+6:F1})"));
            }
            // WHOSE CHANGE MOVED THE STATIC POINT. Both lanes raised the static rpm at a fixed pitch while leaving
            // the 84 mph point alone, which is what makes the two anchors disagree; this splits the move between
            // them by forcing the section back to the old branch under the NEW engine.
            try
            {
                var g33 = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, 15.33, p.ActivityFactor);
                BladeElement.LegacyPostStall = true;
                double sOld = PistonEngine.BalancedRpm(g33, p, 1.0, 1.0, 0.0, rho);
                double oOld = PistonEngine.BalancedRpm(g33, p, 1.0, 1.0, 37.55, rho);
                BladeElement.LegacyPostStall = false;
                double sNew = PistonEngine.BalancedRpm(g33, p, 1.0, 1.0, 0.0, rho);
                double oNew = PistonEngine.BalancedRpm(g33, p, 1.0, 1.0, 37.55, rho);
                sb.AppendLine($"  at 15.33 deg with the NEW engine: old section static {sOld:F1} / 84 mph {oOld:F1}; " +
                              $"plate section static {sNew:F1} / 84 mph {oNew:F1}  (the section's own share: {sNew - sOld:+0.0;-0.0} static)");
            }
            finally { BladeElement.LegacyPostStall = false; }

            double fit = PistonEngine.FitPitchForStaticRpm(p.PropBladeCount, 0.5 * p.PropDiameterM, p.ActivityFactor, 0.15,
                                                           2150.0, PistonEngine.Spec.For(p), p.IdleRpm);
            var fg = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, fit, p.ActivityFactor);
            double fs = PistonEngine.BalancedRpm(fg, p, 1.0, 1.0, 0.0, rho);
            double f84 = PistonEngine.BalancedRpm(fg, p, 1.0, 1.0, 37.55, rho);
            sb.AppendLine($"  FIT to the 2,150 static anchor: {fit:F2} deg -> static {fs:F1}, at 84 mph {f84:F1} ({f84 - 2575.0:+0.0;-0.0} of the redline)");
        }
        Emit("combined-refit.txt", sb);
    }

    internal readonly struct Run
    {
        public readonly double StartRpm, EndRpm, StopSeconds;
        public readonly System.Collections.Generic.List<(double T, double Rpm, bool Stopped)> Log;
        public Run(double start, double end, double stop, System.Collections.Generic.List<(double T, double Rpm, bool Stopped)> log)
        { StartRpm = start; EndRpm = end; StopSeconds = stop; Log = log; }
    }

    internal static RigidBodyState Held(double vx)
        => new(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(vx, 0, 0), Vec3.Zero);

    /// <summary>
    /// Hold an aeroplane at a fixed axial airspeed with the throttle at the idle stop and step it. The body velocity
    /// is imposed rather than flown, because a real tailslide lasts a second or two before the nose falls through,
    /// and the question "does the rpm fall while it is reversed" needs the reversed condition held long enough to
    /// answer. The shaft settles at idle in forward flight first, so the run starts from the rpm the aeroplane
    /// actually carries into the manoeuvre. Returns the rpm trace and when, if ever, static friction latched it.
    /// </summary>
    internal static Run Tailslide(string id, double vAxialMs, double seconds, double entrySpeedMs = 25.0)
    {
        AircraftConfig c = Load(id);
        var ac = new Aircraft(c, Held(entrySpeedMs), ControlDeflections.Neutral);
        double dt = SimLoop.DefaultFixedDtSec;
        for (int i = 0; i < (int)(6.0 / dt); i++) { ac.Step(new ControlInputs(0, 0, 0, 1.0), dt); ac.State = Held(entrySpeedMs); }
        double start = ac.ShaftRpm;
        var log = new System.Collections.Generic.List<(double T, double Rpm, bool Stopped)>();
        double stopAt = double.NaN;
        int steps = (int)(seconds / dt), every = (int)(0.1 / dt);
        for (int i = 1; i <= steps; i++)
        {
            ac.Step(new ControlInputs(0, 0, 0, 1.0), dt);
            ac.State = Held(vAxialMs);
            double t = i * dt;
            if (double.IsNaN(stopAt) && ac.ShaftHeldByStaticFriction) stopAt = t;
            if (i % every == 0) log.Add((t, ac.ShaftRpm, ac.ShaftHeldByStaticFriction));
            if (!double.IsNaN(stopAt) && t > stopAt + 0.5) break;
        }
        return new Run(start, ac.ShaftRpm, stopAt, log);
    }
}

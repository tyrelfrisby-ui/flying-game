using System.Text.Json.Nodes;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Full-aft elevator authority (owner 2026-10-03: a stalled Skyhawk dropped "from way up high" and spun): with the
/// stick fully back the static pitch balance must sit just past the wing's stall for trainers (they MUSH, the nose bobs)
/// and well past it for aerobatic types (they snap and spin). The elevator's plain-flap saturation (SaturationDeg) is
/// calibrated per type — CALIBRATE_STALL=1 solves it and writes the configs. Part of the new-airplane protocol.</summary>
public class StallAuthorityTests
{
    private readonly ITestOutputHelper _o;
    public StallAuthorityTests(ITestOutputHelper o) { _o = o; }

    private static string Repo { get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; } }

    /// <summary>Types whose longitudinal design has been verified through the protocol's stall check. The rest are listed
    /// as PENDING (their elevators were too weak to stall, or the CG/tail is off) — see docs/NEW-AIRPLANE-PROTOCOL.md.</summary>
    public static readonly HashSet<string> Verified = new() { "c172-like", "pa28-archer-like", "cirrus-sr22-like", "pa18-cub-like", "pa18-bush-like", "glasair3-like", "glider-swift-s1-like", "f86-sabre-like" };

    /// <summary>Types whose elevator is set by hand, not by the calibration (2026-10-06): it wanted the Pitts' elevator chord ×3.6
    /// and the Cub-on-floats' ×1.4 (geometry problems — too much stability — to fix with data), gave the Cassutt and drone a
    /// non-physical 7.8° saturation, and the 2-33 is set from its spin (20°).</summary>
    public static readonly HashSet<string> ManualElevator = new() { "pitts-s2b-like", "pa18-floats-like", "cassutt-f1-like", "target-drone-like", "glider-2-33-like", "stearman-pt17-like" };

    /// <summary>Margin past the wing stall that full aft should hold (deg).</summary>
    public static double TargetMarginDeg(string id) => id switch
    {
        // The Swift S-1 is an aerobatic sailplane, cleared for spins — it belongs with the aerobatic types, not with
        // the trainers whose full aft stick is calibrated to stop just short of the stall (owner, 2026-10-19: with it
        // in the trainer bucket the Swift could not be spun at all — it stalled, dropped its nose and flew out of it
        // with the stick on the aft stop and full rudder held. 0.10 turns in 30 s against the Extra's 3.60).
        "extra-300-like" or "pitts-s2b-like" or "decathlon-8kcab-like" or "glider-swift-s1-like" => 6.0,
        "stearman-pt17-like" => 4.0,
        _ => -0.5,   // trainers: balance just UNDER the stall — full aft bobs across it and MUSHES (~1,300 fpm, 172), wings rockable
    };

    /// <summary>
    /// The wing's own stall angle, from the table its biggest lifting surface uses.
    ///
    /// This used to look for a surface named exactly "wing" and fall back to a hard-coded 15 deg. Every BIPLANE
    /// names its wings "wing-upper"/"wing-lower", so all three of them — the Pitts, the Stearman and now the Jenny —
    /// were silently targeted at 15 deg instead of at their own wings (2026-10-20). It mattered most for the Pitts,
    /// whose `pitts-wing-upper` table peaks at **12 deg**: the target was 21 deg when it should have been 18, and
    /// "the Pitts cannot be held past the stall, alpha pins at 13" was measured against a stall angle 3 deg too
    /// high. alpha 13 is PAST a 12 deg stall, not short of it. See docs/SPIN-ROTARY-DATA.md.
    /// </summary>
    public static double StallDeg(AircraftConfig c)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        SurfaceConfig? main = null;
        double best = 0;
        foreach (var sf in c.Surfaces)
        {
            if (!sf.Id.StartsWith("wing") || sf.Id.EndsWith("aileron") || sf.Strips.Count == 0) continue;
            double area = sf.Strips.Sum(st => st.Area);
            if (area > best) { best = area; main = sf; }
        }
        return main is null ? 15 : tables[main.Strips[0].Airfoil].AlphaClMaxRad * 57.2958;
    }

    /// <summary>Wing AoA where full-aft elevator's pitching moment crosses zero (static, no rates), deg.</summary>
    public static double FullAftBalanceDeg(AircraftConfig c)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double up = -c.Controls.Elevator.MaxDeflRad, prev = double.NaN;
        for (double a = 2; a <= 45; a += 0.25)
        {
            double ar = a / 57.2958, v = 30;
            var (_, m) = AeroModel.Compute(c, tables, new Vec3(v * Math.Cos(ar), 0, v * Math.Sin(ar)), Vec3.Zero, Vec3.Zero, 1.2, new ControlDeflections(0, up, 0, 0));
            if (!double.IsNaN(prev) && prev > 0 && m.Y <= 0) return a;
            prev = m.Y;
        }
        return 45;
    }

    [Fact]
    public void FullAftHoldsJustPastTheStallForEveryAircraft()
    {
        bool calibrateAny = Environment.GetEnvironmentVariable("CALIBRATE_STALL") != null;
        // CALIBRATE_ONLY=<id> scopes the write to ONE type. Without it a calibration run rewrites every config it
        // touches, and on 2026-10-19 that silently rewrote two unrelated J-3 configs (one grew its elevator by
        // x1.41) while the intended type was being worked on. Calibrating one aeroplane should change one file.
        string? only = Environment.GetEnvironmentVariable("CALIBRATE_ONLY");
        var bad = new List<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(Repo, "configs", "aircraft"), "*.json").Order())
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(file);
            bool calibrate = calibrateAny && (only is null || c.Id == only);
            double stall = StallDeg(c), target = stall + TargetMarginDeg(c.Id);
            if (calibrate && c.Id == "pa28-archer-like")
            {
                // All-moving stabilator: no flap saturation — its TRAVEL sets full-aft authority (bisection on the up-travel).
                double lo = 2, hi = 20;
                for (int i = 0; i < 30; i++) { double mid = 0.5 * (lo + hi); c.Controls.Elevator.MaxDeflRad = mid * Math.PI / 180; if (FullAftBalanceDeg(c) >= target) hi = mid; else lo = mid; }
                double travel = Math.Round(hi, 2);
                var node = JsonNode.Parse(File.ReadAllText(file))!;
                node["controls"]!["elevator"]!["maxDeflRad"] = Math.Round(travel * Math.PI / 180, 4);
                File.WriteAllText(file, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                File.Copy(file, Path.Combine(Repo, "unity", "Assets", "StreamingAssets", "aircraft", Path.GetFileName(file)), true);
                c.Controls.Elevator.MaxDeflRad = travel * Math.PI / 180;
                _o.WriteLine($"CALIBRATED {c.Id}: stabilator travel {travel:F1}°");
            }
            else if (calibrate && c.Id != "pa28-archer-like" && !ManualElevator.Contains(c.Id))   // every type (2026-10-06: with real pitch stability the unverified types pulled far past the stall)   // (the Archer and SR22 are set by their builder: the Archer by stabilator travel; the SR22 auto-calibrates to a non-physical 4.7°, so its tail is due a protocol rework)
            {
                // Bisection on the saturation (balance rises with it); 120° ≈ no saturation.
                double lo = 3, hi = 120;
                c.Controls.Elevator.SaturationDeg = hi;
                if (FullAftBalanceDeg(c) >= target)
                {
                    for (int i = 0; i < 30; i++) { double mid = 0.5 * (lo + hi); c.Controls.Elevator.SaturationDeg = mid; if (FullAftBalanceDeg(c) >= target) hi = mid; else lo = mid; }
                    double sat = Math.Round(hi, 1);
                    var node = JsonNode.Parse(File.ReadAllText(file))!;
                    node["controls"]!["elevator"]!["saturationDeg"] = sat;
                    File.WriteAllText(file, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    File.Copy(file, Path.Combine(Repo, "unity", "Assets", "StreamingAssets", "aircraft", Path.GetFileName(file)), true);
                    c.Controls.Elevator.SaturationDeg = sat;
                }
                else
                {
                    // The elevator itself is too small for the stall: grow its strips' chord (area with it) until full aft
                    // reaches the target, unsaturated; then the same saturation solve.
                    double kLo = 1, kHi = 4;
                    AircraftConfig Scaled(double k)
                    {
                        AircraftConfig cc = AircraftConfigLoader.LoadFromFile(file);
                        cc.Controls.Elevator.SaturationDeg = 120;
                        foreach (var sf in cc.Surfaces) if (sf.Id == "elevator") foreach (var st in sf.Strips) { st.Chord *= k; st.Area *= k; }
                        return cc;
                    }
                    if (FullAftBalanceDeg(Scaled(kHi)) < target) { _o.WriteLine($"  {c.Id}: cannot reach {target:F1}° even with a 4x elevator"); }
                    else
                    {
                        for (int i = 0; i < 25; i++) { double mid = 0.5 * (kLo + kHi); if (FullAftBalanceDeg(Scaled(mid)) >= target + 0.5) kHi = mid; else kLo = mid; }
                        double k = Math.Round(kHi, 3);
                        var node = JsonNode.Parse(File.ReadAllText(file))!;
                        foreach (var sfn in node["surfaces"]!.AsArray())
                            if ((string?)sfn!["id"] == "elevator")
                                foreach (var stn in sfn["strips"]!.AsArray()) { stn!["chord"] = Math.Round((double)stn["chord"]! * k, 4); stn["area"] = Math.Round((double)stn["area"]! * k, 4); }
                        File.WriteAllText(file, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        c = AircraftConfigLoader.LoadFromFile(file);
                        double lo2 = 3, hi2 = 120;
                        for (int i = 0; i < 30; i++) { double mid = 0.5 * (lo2 + hi2); c.Controls.Elevator.SaturationDeg = mid; if (FullAftBalanceDeg(c) >= target) hi2 = mid; else lo2 = mid; }
                        node = JsonNode.Parse(File.ReadAllText(file))!;
                        node["controls"]!["elevator"]!["saturationDeg"] = Math.Round(hi2, 1);
                        File.WriteAllText(file, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        File.Copy(file, Path.Combine(Repo, "unity", "Assets", "StreamingAssets", "aircraft", Path.GetFileName(file)), true);
                        c = AircraftConfigLoader.LoadFromFile(file);
                        _o.WriteLine($"  {c.Id}: elevator chord x{k:F2} (it was too small to stall the wing)");
                    }
                }
            }
            double bal = FullAftBalanceDeg(c);
            _o.WriteLine($"{c.Id,-26} stall {stall,4:F1}°  full-aft balance {bal,4:F1}°  (target {target:F1}°)  saturation {c.Controls.Elevator.SaturationDeg:F1}°");
            bool ok = bal >= Math.Min(stall, target) - 1.0 && bal <= Math.Max(stall, target) + 2.0;
            if (!ok && Verified.Contains(c.Id)) bad.Add($"{c.Id}: {bal:F1}° vs stall {stall:F1}° (target {target:F1}°)");
            else if (!ok) _o.WriteLine($"   PENDING protocol rework: {c.Id} (full aft {bal:F1}° vs stall {stall:F1}°)");
        }
        Assert.True(bad.Count == 0, "full-aft authority off: " + string.Join("; ", bad) + " — run CALIBRATE_STALL=1");
    }
}

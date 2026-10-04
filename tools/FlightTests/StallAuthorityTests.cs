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

    /// <summary>Margin past the wing stall that full aft should hold (deg).</summary>
    public static double TargetMarginDeg(string id) => id switch
    {
        "extra-300-like" or "pitts-s2b-like" or "decathlon-8kcab-like" => 6.0,
        "stearman-pt17-like" => 4.0,
        _ => -0.5,   // trainers: balance just UNDER the stall — full aft bobs across it and MUSHES (~1,300 fpm, 172), wings rockable
    };

    public static double StallDeg(AircraftConfig c)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        foreach (var sf in c.Surfaces) if (sf.Id == "wing") return tables[sf.Strips[0].Airfoil].AlphaClMaxRad * 57.2958;
        return 15;
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
        bool calibrate = Environment.GetEnvironmentVariable("CALIBRATE_STALL") != null;
        var bad = new List<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(Repo, "configs", "aircraft"), "*.json").Order())
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(file);
            double stall = StallDeg(c), target = stall + TargetMarginDeg(c.Id);
            if (calibrate && Verified.Contains(c.Id) && c.Id is not ("pa28-archer-like" or "cirrus-sr22-like"))   // (the Archer and SR22 are set by their builder: the Archer by stabilator travel; the SR22 auto-calibrates to a non-physical 4.7°, so its tail is due a protocol rework)
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

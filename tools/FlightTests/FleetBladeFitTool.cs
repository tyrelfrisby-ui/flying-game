using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// FIT EVERY FIXED-PITCH BLADE (Phase 1, the fleet fit). BLADE_WRITE=1 writes the result into the configs; without
/// it this only reports, so it is safe to run.
///
/// The anchor is the rpm at full throttle standing still, which is published in the TCDS for any certified type —
/// Ty 2026-11-09: "the static rpm is in the tcds so any fixed pitch prop airplane will be easy to tune in this
/// manner... just use the middle of the tcds range for static at sea level."
///
/// Only one type here has a MEASURED figure: his own Cub at 2150. Every other type is fitted to 0.835 of its red
/// line, which is that Cub's ratio, and is marked PROVISIONAL in its own bladePitchSource so a real TCDS number can
/// replace it one aeroplane at a time without anyone having to guess which were real.
/// </summary>
public class FleetBladeFitTool
{
    private readonly ITestOutputHelper _o;
    public FleetBladeFitTool(ITestOutputHelper o) { _o = o; }

    /// <summary>His Cub's static over its red line, the only measured ratio we have.</summary>
    private const double ProvisionalStaticFraction = 2150.0 / 2575.0;

    /// <summary>Static rpm that is MEASURED rather than scaled. Add TCDS figures here as they are read.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, double> Measured = new()
    {
        ["j3-cub-like"] = 2150.0,          // Ty's own, metal prop, applied to the wooden blade at his direction
        ["j3-cub-floats-like"] = 2150.0,
    };

    [Fact]
    public void Fit()
    {
        bool write = System.Environment.GetEnvironmentVariable("BLADE_WRITE") == "1";
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string dir = System.IO.Path.Combine(d.FullName, "configs", "aircraft");
        _o.WriteLine($"{(write ? "WRITING" : "dry run")} — fixed-pitch blades fitted to full-power static rpm\n");
        _o.WriteLine("type                      hp   dia   b  redline | static target | fitted pitch | source");
        int done = 0;
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            var c = AircraftConfigLoader.LoadFromFile(f);
            var p = c.Propulsion;
            if (p is null || p.PropDiameterM <= 0 || p.ConstantSpeed || c.Engines.Count > 0 || p.MaxPowerW <= 0) continue;
            bool measured = Measured.TryGetValue(c.Id, out double target);
            if (!measured) target = ProvisionalStaticFraction * p.MaxRpm;

            double pitch = PistonEngine.FitPitchForStaticRpm(p.PropBladeCount, 0.5 * p.PropDiameterM, p.ActivityFactor, 0.15,
                                                             target, PistonEngine.Spec.For(p), p.IdleRpm);
            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
            double got = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, 0.0, 1.225, 1.0);
            bool pinned = pitch <= 8.05 || pitch >= 17.95;
            _o.WriteLine($"{c.Id,-22}{p.MaxPowerW / 745.7,6:F0} {p.PropDiameterM,5:F2} {p.PropBladeCount,3}  {p.MaxRpm,6:F0} | {target,13:F0} | {pitch,8:F2}{(pinned ? " PIN" : "   ")}  | {(measured ? "MEASURED" : "provisional")} (got {got:F0})");
            if (write) Write(f, pitch, c.Id, target, measured, got);
            done++;
        }
        _o.WriteLine($"\n{done} fixed-pitch types. PIN = the fit hit a bound, so that blade's activity factor is wrong, not its pitch.");
    }

    private static void Write(string path, double pitch, string id, double target, bool measured, double got)
    {
        string t = System.IO.File.ReadAllText(path);
        string note = measured
            ? $"Fitted to a MEASURED static rpm of {target:F0} at full power, sea level (Ty's own aeroplane). Blade-element against the engine's torque; PistonEngine.FitPitchForStaticRpm."
            : $"PROVISIONAL, 2026-11-09: fitted to {target:F0} rpm static, which is 0.835 of this type's red line — the ratio of Ty's own Cub, the one aeroplane with a measured figure. The TCDS publishes a static rpm range for any certified type; take the middle of it at sea level and refit. Blade-element against the engine's torque.";
        string json = System.Text.Json.JsonSerializer.Serialize(note);
        var m = System.Text.RegularExpressions.Regex.Match(t, "\"propDiameterM\"\\s*:\\s*[-0-9.eE]+");
        if (!m.Success) return;
        if (t.Contains("\"bladePitch75Deg\""))
        {
            t = System.Text.RegularExpressions.Regex.Replace(t, "\"bladePitch75Deg\"\\s*:\\s*[-0-9.eE]+", $"\"bladePitch75Deg\": {pitch:F2}");
            t = System.Text.RegularExpressions.Regex.Replace(t, "\"bladePitchSource\"\\s*:\\s*\"(\\\\.|[^\"\\\\])*\"", "\"bladePitchSource\": " + json.Replace("$", "$$"));
        }
        else t = t[..m.Index] + m.Value + $",\n    \"bladePitch75Deg\": {pitch:F2},\n    \"bladePitchSource\": {json}" + t[(m.Index + m.Length)..];
        System.Text.Json.JsonDocument.Parse(t);
        System.IO.File.WriteAllText(path, t);
    }
}

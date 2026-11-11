using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE SIX THAT SHIP, finished (Ty 2026-11-09: "keep moving on the 6 shipping airplanes and we'll add the others
/// later"). PROP_WRITE=1 writes; without it this reports only.
///
/// The 2-33 is a glider and has no propeller. Both Cubs are fixed pitch and already fitted to his measured 2150
/// static. The Pitts, the Extra and the Great Lakes are CONSTANT SPEED, so a single fitted pitch means nothing to
/// them in flight — what they need is their pair of stops, derived from his two rules
/// (BladeElement.GovernorStops) and cached in the config because the governor reads them every step.
/// </summary>
public class ShippingPropFitTool
{
    private readonly ITestOutputHelper _o;
    public ShippingPropFitTool(ITestOutputHelper o) { _o = o; }

    private static readonly string[] Shipping =
        { "glider-2-33-like", "j3-cub-like", "j3-cub-floats-like", "great-lakes-like", "pitts-s2b-like", "extra-300-like" };

    [Fact]
    public void Fit()
    {
        bool write = System.Environment.GetEnvironmentVariable("PROP_WRITE") == "1";
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string dir = System.IO.Path.Combine(d.FullName, "configs", "aircraft");
        _o.WriteLine($"{(write ? "WRITING" : "dry run")} — the six that ship\n");
        foreach (string id in Shipping)
        {
            string path = System.IO.Path.Combine(dir, id + ".json");
            var c = AircraftConfigLoader.LoadFromFile(path);
            var p = c.Propulsion;
            if (p is null || p.PropDiameterM <= 0) { _o.WriteLine($"{id,-22} no propeller — nothing to fit"); continue; }
            if (!p.ConstantSpeed)
            {
                _o.WriteLine($"{id,-22} fixed pitch, blade {p.BladePitch75Deg:F2} deg (fitted to {2150:F0} static)");
                continue;
            }
            double radius = 0.5 * p.PropDiameterM, vne = c.Limits?.VneMs ?? 100.0;
            var st = BladeElement.GovernorStops(p.PropBladeCount, radius, p.ActivityFactor, 0.15, p.MaxPowerW, p.MaxRpm, vne);
            // What the governor can hold, as a check: rated rpm at full power from rest up to Vd, AT THE CONDITION
            // THE COARSE STOP IS WRITTEN AT (10,000 ft density altitude, Vd as CAS) rather than at sea level — the
            // sea-level sweep said "holds to past Vd" for every type and hid the fact that the altitude dive did not.
            double rho = Atmosphere.DensityAtAltitude(BladeElement.CoarseStopDensityAltitudeM);
            double delta = PropModel.IsaPressureRatioForDensity(rho), tasPerCas = System.Math.Sqrt(1.225 / rho);
            double heldTo = 0;
            for (double v = 0; v <= 1.25 * vne; v += 2)
            {
                var g = PistonEngine.Govern(p.PropBladeCount, radius, p.ActivityFactor, 0.15, st, p.MaxRpm,
                                            p.MaxPowerW, p.MaxRpm, p.IdleRpm, 1.0, delta, v * tasPerCas, rho);
                if (g.OnFineStop || g.OnCoarseStop) break;
                heldTo = v;
            }
            _o.WriteLine($"{id,-22} constant speed: fine {st.FineDeg:F2} deg, coarse {st.CoarseDeg:F2} deg; holds {p.MaxRpm:F0} rpm to {heldTo * 1.94384:F0} kt CAS at 10,000 ft DA (Vd {1.1 * vne * 1.94384:F0} kt)");
            if (write) Write(path, st, vne, p);
        }
    }

    /// <summary>
    /// A TYPE THAT ALREADY HAS STOPS KEEPS ITS FINE ONE, and that is deliberate rather than an oversight. Two of the
    /// three constant-speed types carry a PUBLISHED low stop off their TCDS (Pitts 11.0, Great Lakes 12.5), and each
    /// one's <see cref="PropulsionConfig.DryBoundaryExcess"/> was then SOLVED against it — against Ty's anchor that
    /// the propeller stops at the type's own stall speed, which is measured with the blade at that stop. Re-deriving
    /// the fine stop here would silently invalidate a constant in another file. The coarse stop has no such tie and
    /// is the one Ty's 2026-11-11 rule is about, so that is the one this writes.
    ///
    /// A type with NO stops yet still gets both, because there is nothing to invalidate.
    /// </summary>
    private static void Write(string path, BladeElement.Stops st, double vne, PropulsionConfig p)
    {
        string t = System.IO.File.ReadAllText(path);
        string note = $"Derived from Ty's two rules, not chosen: the FINE stop is the blade angle that lets the engine make rated rpm standing still on 26 inHg at SEA LEVEL (the run-up check), and the COARSE stop is the angle needed to hold rated rpm at Vd = 1.1 x Vne ({1.1 * vne * 1.94384:F0} kt CAS) at full power at 10,000 ft DENSITY ALTITUDE (Ty 2026-11-11), so the governor runs out of blade exactly at Vd there and over-revs past it. Power at 26 inHg is the naturally aspirated line through its friction intercept, (26-10)/(29.92-10) = 0.80 of rated; power at 10,000 ft is Gagg and Ferrar's (sigma-0.117)/0.883. Computed by BladeElement.GovernorStops and cached here because the governor reads them every step.";
        string json = System.Text.Json.JsonSerializer.Serialize(note);
        var m = System.Text.RegularExpressions.Regex.Match(t, "\"propDiameterM\"\\s*:\\s*[-0-9.eE]+");
        if (!m.Success) return;
        string ins = $",\n    \"fineStopDeg\": {st.FineDeg:F2},\n    \"coarseStopDeg\": {st.CoarseDeg:F2},\n    \"pitchStopSource\": {json}";
        if (t.Contains("\"fineStopDeg\""))
        {
            t = System.Text.RegularExpressions.Regex.Replace(t, "\"coarseStopDeg\"\\s*:\\s*[-0-9.eE]+", $"\"coarseStopDeg\": {st.CoarseDeg:F2}");
            t = System.Text.RegularExpressions.Regex.Replace(t, "\"pitchStopSource\"\\s*:\\s*\"(\\\\.|[^\"\\\\])*\"", "\"pitchStopSource\": " + json.Replace("$", "$$"));
        }
        else t = t[..(m.Index + m.Length)] + ins + t[(m.Index + m.Length)..];
        System.Text.Json.JsonDocument.Parse(t);
        System.IO.File.WriteAllText(path, t);
    }
}

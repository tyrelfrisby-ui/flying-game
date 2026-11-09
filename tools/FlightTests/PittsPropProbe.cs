using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The Pitts' two real propellers (Ty 2026-11-09): the Hartzell two-blade, 82 in, and the MT three-blade,
/// 203 cm. 260 hp, 2700 rpm, Vne 184 kt so Vd 203 kt.</summary>
public class PittsPropProbe
{
    private readonly ITestOutputHelper _o;
    public PittsPropProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        // The owner's own numbers, 2026-11-09. Each row: rated power, rpm, Vne, and the REAL propeller.
        foreach (var (type, powerW, rpm, vne, name, dia, blades, af, cfgDia) in new (string, double, double, double, string, double, int, double, double)[]
        {
            ("Pitts S-2B",   194000, 2700, 94.8,  "Hartzell 2-blade 82 in", 2.0828, 2, 110.0, 1.98),
            ("Pitts S-2B",   194000, 2700, 94.8,  "MT 3-blade 203 cm",      2.03,   3, 100.0, 1.98),
            ("Extra 300",    224000, 2700, 113.2, "205 cm",                 2.05,   3, 100.0, 2.00),
            ("Extra 300",    224000, 2700, 113.2, "205 cm, 2 blade",        2.05,   2, 110.0, 2.00),
            ("Great Lakes",  134226, 2700, 68.4,  "76 in",                  1.9304, 2, 100.0, 1.8796),
        })
        {
            double r = 0.5 * dia;
            var st = BladeElement.GovernorStops(blades, r, af, 0.15, powerW, rpm, vne);
            var statc = BladeElement.Solve(new BladeElement.Geometry(blades, r, st.FineDeg, af, 0.15), 0.0, 0.0, rpm, 1.225);
            double q = powerW / (rpm * 2 * System.Math.PI / 60.0);
            double vCruise = 0.55 * vne;
            double pc = BladeElement.PitchForTorqueDeg(blades, r, af, 0.15, q, rpm, vCruise, 1.225);
            var cruise = BladeElement.Solve(new BladeElement.Geometry(blades, r, pc, af, 0.15), vCruise, 0.0, rpm, 1.225);
            // What the config's (wrong) diameter would have given, same blade otherwise.
            var stCfg = BladeElement.GovernorStops(blades, 0.5 * cfgDia, af, 0.15, powerW, rpm, vne);
            _o.WriteLine($"{type,-13}{name,-24}{dia,6:F3} ({blades}b AF{af:F0}) | fine {st.FineDeg,5:F1}  coarse {st.CoarseDeg,5:F1} | static {statc.ThrustN,6:F0} N ({statc.ThrustN * 0.2248 / (powerW / 745.7),4:F1} lb/hp) | eta {cruise.Efficiency,5:F3} at {vCruise,4:F0} m/s | config dia {cfgDia:F3} would give fine {stCfg.FineDeg:F1}");
        }
    }
}

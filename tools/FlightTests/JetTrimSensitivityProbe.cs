using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (JETSENS=1): how sharply the traced jet's loads move with alpha, bank and yaw rate about a type's
/// rig point. A trim that cannot be held hands off shows up here as a steep or jumpy lateral moment.</summary>
public class JetTrimSensitivityProbe
{
    private readonly ITestOutputHelper _out;
    public JetTrimSensitivityProbe(ITestOutputHelper o) { _out = o; }
    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load(string id)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    [Fact]
    public void Run()
    {
        if (Environment.GetEnvironmentVariable("JETSENS") == null) return;
        foreach (string id in (Environment.GetEnvironmentVariable("JETSENS_ID") ?? "pitts-s2b-like,j3-cub-like,extra-300-like").Split(','))
        {
            AircraftConfig c = Load(id);
            var tables = Aircraft.BuildAirfoilTables(c);
            RigConfig rig = c.Rig;
            double rho = Atmosphere.DensityAtAltitude(rig.AltitudeM);
            var ctl = new ControlDeflections(0, rig.ElevatorTrimRad, 0, 0);
            _out.WriteLine($"=== {id}  alpha {rig.AlphaRad * D:F2} deg  V {rig.TasMs:F1} m/s  jetK {c.Propulsion!.JetK}  shadowK {c.SpinAero?.ShadowK ?? 0}");
            foreach (string what in new[] { "jet 1", "jet 0", "shadow 0", "both 0" })
            {
                c.Propulsion.JetK = what is "jet 0" or "both 0" ? 0.0 : 1.0;
                if (c.SpinAero is not null) c.SpinAero.ShadowK = what is "shadow 0" or "both 0" ? 0.0 : 0.5;
                var line = new System.Text.StringBuilder($"  {what,-9}");
                double prevL = double.NaN, prevN = double.NaN;
                foreach (double dA in new[] { -0.5, -0.25, 0.0, 0.25, 0.5 })
                {
                    double a = rig.AlphaRad + dA / D;
                    Vec3 vb = new Vec3(rig.TasMs * Math.Cos(a), 0, rig.TasMs * Math.Sin(a));
                    // Through RigSolver, which is what hands AeroModel the slipstream: calling Compute directly
                    // passes no jet at all, and then every variant reads the same.
                    Vec3 m = RigSolver.SteadyForces(c, tables, RigSolver.RigThrottle, vb, rho, ctl).Moment;
                    line.Append($" | dα{dA,+5:F2}: L {m.X,8:F1} N {m.Z,8:F1}");
                    if (!double.IsNaN(prevL)) line.Append($" (ΔL {m.X - prevL,+7:F1} ΔN {m.Z - prevN,+7:F1})");
                    prevL = m.X; prevN = m.Z;
                }
                _out.WriteLine(line.ToString());
            }
            c.Propulsion.JetK = 1.0;
            if (c.SpinAero is not null) c.SpinAero.ShadowK = 0.5;
        }
    }
}

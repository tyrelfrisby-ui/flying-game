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

/// <summary>
/// DOES FORWARD ELEVATOR BLANKET MORE RUDDER? Ty, 2026-11-09, correcting me: "it would actually be the opposite, a
/// forward elevator in an upright spin would blank more rudder which is why you have to use rudder before moving the
/// elevator forward." In an upright spin the air comes from BELOW; elevator UP turns the trailing edge away from it
/// and sheds less, elevator FORWARD turns it into the flow and sheds more.
/// </summary>
public class ElevatorShadowProbe
{
    private readonly ITestOutputHelper _o;
    public ElevatorShadowProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("ELE_SHADOW") == null) return;
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like" })
        {
            var c = Load(id);
            var tables = Aircraft.BuildAirfoilTables(c);
            _o.WriteLine($"\n=== {id}: q on the RUDDER strips, upright spin (alpha 45, yaw 2.6 rad/s)");
            _o.WriteLine("  elevator |  mean q on rudder   (1.0 = clean air)");
            double a = 45 * Math.PI / 180;
            var v = new Vec3(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
            var rates = new Vec3(0.3, 0.2, 2.6);
            foreach (double deg in new[] { -25.0, -15.0, 0.0, 15.0, 25.0 })
            {
                double de = deg * Math.PI / 180;
                double[] q = TailWake.StripQFactors(c, tables, v, rates, new ControlDeflections(0, de, 0, 0), null, 0.0);
                // rudder strips only
                int idx = 0; double sum = 0; int n = 0;
                foreach (SurfaceConfig sf in c.Surfaces)
                    foreach (StripConfig st in sf.Strips)
                    {
                        if (sf.Id.Contains("rudder", StringComparison.OrdinalIgnoreCase) && idx < q.Length) { sum += q[idx]; n++; }
                        idx++;
                    }
                _o.WriteLine($"  {deg,7:F0} deg | {(n > 0 ? sum / n : double.NaN),8:F4}   ({n} strips)");
            }
        }
    }
}

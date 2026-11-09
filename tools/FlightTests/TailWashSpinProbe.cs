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
/// WHAT THE TAIL IS BREATHING IN A SPIN (TAILWASH=1). Ty 2026-11-09, on his own Pitts: "on the pitts flat spin in the
/// real airplane when i pull power out and rpm decreases the nose drops back down.. in the game the nose stays up."
/// If the stabiliser has no propeller wash to begin with, taking the power away cannot change anything there - so
/// print what each tail strip actually gets, at full power and at idle, in a developed spin.
/// </summary>
public class TailWashSpinProbe
{
    private readonly ITestOutputHelper _o;
    public TailWashSpinProbe(ITestOutputHelper o) { _o = o; }
    private const double Rho = 1.1;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("TAILWASH") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));

        // A developed upright flat spin: alpha ~85, yaw rate 4 rad/s left, slow.
        var vel = new Vec3(25 * Math.Cos(85 * Math.PI / 180), 1.0, 25 * Math.Sin(85 * Math.PI / 180));
        var rates = new Vec3(0.3, 0.2, -4.0);
        _o.WriteLine($"Pitts, developed flat spin: alpha 85, yaw -4.0 rad/s, TAS 25 m/s, hub x {AirframeContact.PropHubsBody(c)[0].X:F3}");
        _o.WriteLine("");
        _o.WriteLine("  throttle | thrust N | du m/s | tube m | swirl m/s |  wash on each hStab/elevator strip (m/s)");
        foreach ((string name, double thr) in new[] { ("FULL", 1.0), ("idle", 0.0) })
        {
            PropModel.PropDrive drive = PropModel.SteadyDrive(c.Propulsion, thr, vel.X, Rho);
            (double du, double r, double swirl) = Aircraft.TailSlipstream(c, thr, vel, rates, Rho, drive, false);
            PropModel.Compute(c.Propulsion!, thr, vel, rates, Rho, drive, out PropModel.Parts parts);
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (SurfaceConfig sf in c.Surfaces)
            {
                if (!sf.Id.Contains("stab", StringComparison.OrdinalIgnoreCase) && !sf.Id.Contains("elev", StringComparison.OrdinalIgnoreCase)) continue;
                if (sf.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (StripConfig st in sf.Strips)
                {
                    Vec3 w = PropJet.RelWindAt(c, vel, rates, c.Mass.CgVec(), du, r, swirl, st.PosVec(), true);
                    if (n++ < 8) sb.Append($" {w.Length,5:F2}");
                }
            }
            _o.WriteLine($"  {name,-8} | {parts.Thrust,8:F0} | {du,6:F2} | {r,6:F3} | {swirl,9:F2} | {sb}");
        }
    }
}

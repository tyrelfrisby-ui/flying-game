using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHERE THE SWIRL ACTUALLY IS, read out of the blade instead of assumed. Prints the far-wake tangential speed per
/// annulus beside the two laws that were the candidates, each scaled to carry the SAME total angular momentum so the
/// comparison is about shape alone.
/// </summary>
public class SwirlProfileProbe
{
    private readonly ITestOutputHelper _o;
    public SwirlProfileProbe(ITestOutputHelper o) { _o = o; }
    private const double Rho0 = 1.225, KtMs = 0.514444;

    [Fact]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("SWIRL_PROFILE") == null) return;
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string dir = System.IO.Path.Combine(d.FullName, "configs", "aircraft");

        foreach ((string id, double rigKt) in new[] { ("j3-cub-like", 60.1), ("pitts-s2b-like", 128.3) })
        foreach ((string what, double tasMs, double thr) in new[] { ("rig 75%", rigKt * KtMs, 0.75), ("static full", 0.0, 1.0) })
        {
            var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(dir, id + ".json"));
            var p = c.Propulsion!;
            PropModel.PropDrive drive = PropModel.SteadyDrive(p, thr, tasMs, Rho0);
            double pitch = drive.BladePitchDeg > 0 ? drive.BladePitchDeg : p.BladePitch75Deg;
            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
            var rr = new double[BladeElement.ProfileCount];
            var sw = new double[BladeElement.ProfileCount];
            var res = BladeElement.Solve(g, tasMs, 0.0, drive.ShaftRpm, Rho0, rr, sw);

            // Total angular momentum flux the measured profile carries, so the two laws can be matched to it.
            double vi = 0.5 * (System.Math.Sqrt(tasMs * tasMs + 2.0 * res.ThrustN / (Rho0 * System.Math.PI * g.RadiusM * g.RadiusM)) - tasMs);
            double vTube = tasMs + 2.0 * vi;
            double rs = rr[rr.Length - 1];
            double q = res.TorqueNm;
            double omega = 2.0 * q / (Rho0 * System.Math.PI * rs * rs * rs * rs * vTube);   // solid body: v = omega*r
            double kFree = q / (System.Math.PI * Rho0 * vTube * rs * rs);                   // free vortex: v = k/r

            _o.WriteLine($"\n=== {id}, {what} — rpm {drive.ShaftRpm:F0}, pitch {pitch:F1} deg, thrust {res.ThrustN:F0} N, torque {q:F0} N.m, tube {rs:F3} m");
            _o.WriteLine("   wake r |  r/Rs | MEASURED | solid body | free vortex");
            for (int i = 0; i < rr.Length; i += 2)
                _o.WriteLine($"  {rr[i],7:F3} | {rr[i] / rs,5:F3} | {sw[i],8:F2} | {omega * rr[i],10:F2} | {kFree / rr[i],11:F2}");
        }
    }
}

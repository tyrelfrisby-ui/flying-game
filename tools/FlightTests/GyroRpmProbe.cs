using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT TURNS THE GYRO, old law against new (GYRO_RPM=1). The gyroscopic couple is I*Omega*rate, so if the governed
/// shaft rpm the blade-element path now reports is lower than the throttle law it replaced, every gyroscopic effect
/// got lighter the day the propeller was solved. Ty 2026-11-09, flying it: the Extra "is coming up way short on side
/// slip angle (beta) when yawed left and pushed forward... like the gyroscopics are too light".
/// </summary>
public class GyroRpmProbe
{
    private readonly ITestOutputHelper _o;
    public GyroRpmProbe(ITestOutputHelper o) { _o = o; }
    private const double Rho0 = 1.225, KtMs = 0.514444;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("GYRO_RPM") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        foreach (string id in new[] { "extra-300-like", "pitts-s2b-like" })
        {
            var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
            var p = c.Propulsion!;
            _o.WriteLine($"\n=== {id}   I_prop {p.PropInertia} kg.m2, governed {p.GovernedRpm}, max {p.MaxRpm}");
            _o.WriteLine("   kt |  thr | OLD law rpm | NEW shaft rpm |  pitch deg |  gyro Mz at q=1 rad/s (N.m)");
            foreach (double kt in new[] { 60.0, 120.0, 160.0, 200.0 })
                foreach (double thr in new[] { 1.0, 0.6 })
                {
                    double v = kt * KtMs;
                    double oldRpm = PropModel.RunningRpm(p, thr, 1.0);
                    var drive = PropModel.SteadyDrive(p, thr, v, Rho0);
                    double newRpm = drive.ShaftRpm > 0 ? drive.ShaftRpm : oldRpm;
                    // Mz from a 1 rad/s pitch rate: the term that turns forward stick into yaw.
                    // M = -omega x h with h = I*Omega*sign on +x; a 1 rad/s pitch rate gives Mz = I*Omega*sign.
                    double gOld = p.PropInertia * (oldRpm * 2 * Math.PI / 60) * p.RotationSign;
                    double gNew = p.PropInertia * (newRpm * 2 * Math.PI / 60) * p.RotationSign;
                    _o.WriteLine($" {kt,4:F0} | {thr,4:F2} | {oldRpm,11:F0} | {newRpm,13:F0} | {drive.BladePitchDeg,10:F2} |  old {gOld,7:F0}   new {gNew,7:F0}   ({(oldRpm > 1 ? gNew / gOld * 100 : 100),5:F0}%)");
                }
        }
    }
}

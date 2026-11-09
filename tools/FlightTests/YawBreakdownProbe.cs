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
/// WHAT MAKES AND UNMAKES BETA (YAW_BD=1). Ty 2026-11-09, flying it: the Extra "is coming up way short on side slip
/// angle (beta) when yawed left and pushed forward. It is like the gyroscopics are too light." Beta is a balance —
/// what drives it (gyro from the pitch rate, the disc's in-plane force ahead of the CG, the rudder) against what
/// kills it (the fin's weathercock and the yaw damping). A shortfall can come from either side, so print both.
/// </summary>
public class YawBreakdownProbe
{
    private readonly ITestOutputHelper _o;
    public YawBreakdownProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI, Rho = 1.0, KtMs = 0.514444;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("YAW_BD") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "extra-300-like.json"));
        var tables = Aircraft.BuildAirfoilTables(c);
        var p = c.Propulsion!;

        _o.WriteLine("Extra 300, 120 kt, full power, full LEFT rudder, pushing forward (q negative).");
        _o.WriteLine("Yaw moments, N.m. Positive Mz = nose RIGHT. Beta grows while the total is NEGATIVE (nose left).");
        _o.WriteLine("");
        _o.WriteLine("  beta |    q    |  gyro Mz | inplane Mz | P-fac Mz |   AERO Mz  |   TOTAL");
        double v = 120 * KtMs;
        foreach (double betaDeg in new[] { 0.0, 10.0, 20.0, 30.0, 40.0 })
            foreach (double q in new[] { 0.0, -1.5 })
            {
                double b = betaDeg * Math.PI / 180;
                // Nose yawed LEFT of the path: the velocity in body axes points to the RIGHT (+y).
                var vel = new Vec3(v * Math.Cos(b), v * Math.Sin(b), 0);
                var rates = new Vec3(0, q, 0);
                var drive = PropModel.SteadyDrive(p, 1.0, vel.X, Rho);
                PropModel.Compute(p, 1.0, vel, rates, Rho, drive, out PropModel.Parts parts);
                Vec3 propArm = new Vec3(1.73, 0, 0);   // hub ahead of the CG
                double inplaneMz = Vec3.Cross(propArm, parts.InPlane).Z;
                double pfacMz = -parts.Thrust * parts.PFactorY;
                var ctl = new ControlDeflections(0, 0, -c.Controls.Rudder.MaxDeflRad, 0, 0);
                (Vec3 _, Vec3 am) = AeroModel.Compute(c, tables, vel, rates, Vec3.Zero, Rho, ctl);
                double tot = parts.Gyro.Z + inplaneMz + pfacMz + am.Z;
                _o.WriteLine($" {betaDeg,5:F0} | {q,7:F1} | {parts.Gyro.Z,8:F0} | {inplaneMz,10:F0} | {pfacMz,8:F0} | {am.Z,10:F0} | {tot,9:F0}"
                    + $"   [thrust {parts.Thrust,6:F0}  inPlane ({parts.InPlane.Y,6:F0},{parts.InPlane.Z,6:F0})  pfY {parts.PFactorY,7:F4}  arm {propArm.X,5:F2}  rpm {drive.ShaftRpm,5:F0}]");
            }
    }
}

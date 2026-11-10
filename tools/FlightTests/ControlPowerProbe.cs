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
/// ELEVATOR AND RUDDER POWER (CTL_POWER=1). Ty 2026-11-09, flying the Pitts: "look at elevator and rudder power and
/// stabilizer effectiveness... i'm thinking the stabilizer is about right based on the right hand spins but the
/// elevator and rudder effectiveness are way too low." Moment per degree of deflection, at cruise and at spin
/// attitudes, with the elevator's tanh saturation shown separately so its share is visible.
/// </summary>
public class ControlPowerProbe
{
    private readonly ITestOutputHelper _o;
    public ControlPowerProbe(ITestOutputHelper o) { _o = o; }
    private const double Rho = 1.225;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("CTL_POWER") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
        var tables = Aircraft.BuildAirfoilTables(c);
        double eMax = c.Controls.Elevator.MaxDeflRad, rMax = c.Controls.Rudder.MaxDeflRad;

        _o.WriteLine("Pitts S-2B. Moment per DEGREE of deflection, N.m/deg, at 60 kt unless noted.");
        _o.WriteLine("");
        _o.WriteLine("  alpha |  ELEVATOR dM/ddeg  (full = N.m) |  RUDDER dN/ddeg  (full = N.m)");
        double v = 60 * 0.514444;
        foreach (double aDeg in new[] { 0.0, 10.0, 30.0, 45.0, 60.0, 80.0 })
        {
            double a = aDeg * Math.PI / 180;
            var vel = new Vec3(v * Math.Cos(a), 0, v * Math.Sin(a));
            double M(double e) => AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, Rho, new ControlDeflections(0, e, 0, 0)).Moment.Y;
            double N(double r) => AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, Rho, new ControlDeflections(0, 0, r, 0)).Moment.Z;
            double dM = (M(eMax) - M(-eMax)) / (2 * eMax * 57.2958);
            double dN = (N(rMax) - N(-rMax)) / (2 * rMax * 57.2958);
            _o.WriteLine($"  {aDeg,5:F0} | {dM,10:F1}   ({M(eMax) - M(0),9:F0}) | {dN,10:F1}   ({N(rMax) - N(0),9:F0})");
        }
        _o.WriteLine("");
        _o.WriteLine($"  rudder travel   {rMax * 57.2958:F1} deg -> no saturation");
    }
}

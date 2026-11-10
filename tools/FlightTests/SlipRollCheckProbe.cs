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
/// DOES THE PER-STRIP SLIP ROLL ADD UP TO TR-379? (SLIPROLL=1). The table is a WHOLE-WING rolling-moment coefficient
/// C_l = L/(q S b) at 20 deg of yaw, read off NACA TR-379's published curves; the model applies it strip by strip and
/// scales by k = b / y_mean to put it back on the whole wing. If that conversion over-counts, our rolling moment
/// exceeds the moment the report actually measured - and Ty, flying 2026.1109.1442: "there is a ton of roll with yaw
/// (dihedral effect) on the pitts... it is way too much in spin roll".
/// </summary>
public class SlipRollCheckProbe
{
    private readonly ITestOutputHelper _o;
    public SlipRollCheckProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("SLIPROLL") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like" })
        {
            var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
            var tables = Aircraft.BuildAirfoilTables(c);
            string kind = c.SpinAero?.RollDueToSideslip ?? "";
            double b = AeroModel.WingSpan(c);
            double S = 0; foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) foreach (var st in sf.Strips) S += st.Area;
            double v = 50.0, rho = 1.225, q = 0.5 * rho * v * v;
            _o.WriteLine($"\n=== {id}  kind '{kind}'  b {b:F2} m  S {S:F2} m2   (20 deg yaw, 50 m/s)");
            _o.WriteLine("  alpha | TR-379 Cl | published L | model L (slip-roll only) | ratio");
            foreach (double aDeg in new[] { 20.0, 30.0, 40.0, 50.0, 60.0 })
            {
                double a = aDeg * Math.PI / 180, beta = 20 * Math.PI / 180;
                var vel = new Vec3(v * Math.Cos(a) * Math.Cos(beta), v * Math.Sin(beta), v * Math.Sin(a) * Math.Cos(beta));
                var velNoBeta = new Vec3(v * Math.Cos(a), 0, v * Math.Sin(a));
                double Lb = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral).Moment.X;
                double L0 = AeroModel.Compute(c, tables, velNoBeta, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral).Moment.X;
                double model = Lb - L0;
                double cl = AeroModel.SlipRollCl20(kind, aDeg);
                double pub = cl * q * S * b;
                _o.WriteLine($"  {aDeg,5:F0} | {cl,9:F4} | {pub,11:F0} | {model,24:F0} | {(Math.Abs(pub) > 1 ? model / pub : 0),5:F2}");
            }
        }
    }
}

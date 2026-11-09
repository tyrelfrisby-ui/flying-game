using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE CRUISE SPREAD TRADE, both laws at the same condition. The flat law is what ships today: 0.045 m of radius per
/// metre of path, which works out to k = 0.10 of radius per propeller diameter at EVERY airspeed. The shear law scales
/// the still-air rate by the mixing layer's shear parameter, k = 0.25 * du/(2V+du), so it spreads hard standing still
/// and hardly at all in cruise. They disagree most exactly where the fin is.
/// </summary>
public class SpreadTradeProbe
{
    private readonly ITestOutputHelper _o;
    public SpreadTradeProbe(ITestOutputHelper o) { _o = o; }
    private const double Rho0 = 1.225, MphMs = 2.23694;

    [Fact]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("SPREAD_TRADE") == null) return;
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", "j3-cub-like.json"));
        double arm = PropJet.TailArm(c), dia = c.Propulsion!.PropDiameterM;

        _o.WriteLine("J-3, full throttle. Fin strips are heights above the thrust line; the fin reaches 1.34 m.");
        _o.WriteLine("");
        _o.WriteLine("  mph |  du  | lambda ||        FLAT (today, k=0.10)        ||      SHEAR (k = 0.25*lambda)");
        _o.WriteLine("      |      |        || k_eff  tube   .69   1.01   1.34    || k_eff  tube   .69   1.01   1.34");
        foreach (double mph in new[] { 0.0, 20.0, 40.0, 60.0, 70.0, 84.0 })
        {
            double v = mph / MphMs;
            var vv = new Vec3(v, 0, 0);
            PropModel.PropDrive drive = PropModel.SteadyDrive(c.Propulsion, 1.0, v, Rho0);
            (double du, double r, double swirl) = Aircraft.TailSlipstream(c, 1.0, vv, Vec3.Zero, Rho0, drive, false);
            double lambda = du / (2.0 * v + du);
            var line = new System.Text.StringBuilder($" {mph,4:F0} | {du,4:F1} | {lambda,6:F3} |");
            foreach (double k in new[] { 0.10, 0.25 * lambda })
            {
                PropJet.SpreadOverride = k;
                line.Append($"| {k,5:F3} {r * (1.0 + k * arm / dia),5:F2}");
                foreach (double z in new[] { -0.69, -1.01, -1.34 })
                {
                    Vec3 w = PropJet.RelWindAt(c, vv, Vec3.Zero, c.Mass.CgVec(), du, r, swirl, new Vec3(-5.06, 0, z), true);
                    line.Append($" {w.Length,5:F2}");
                }
                line.Append("   ");
            }
            PropJet.SpreadOverride = -1.0;
            _o.WriteLine(line.ToString());
        }
    }
}

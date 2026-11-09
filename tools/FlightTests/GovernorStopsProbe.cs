using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Ty's two rules for a constant-speed propeller's pitch stops, applied to the fleet's constant-speed types.</summary>
public class GovernorStopsProbe
{
    private readonly ITestOutputHelper _o;
    public GovernorStopsProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        _o.WriteLine("type                  rated hp  dia m  rpm   Vne kt  Vd kt | fine deg  coarse deg | run-up rpm check");
        foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(d.FullName, "configs", "aircraft"), "*.json"))
        {
            var c = AircraftConfigLoader.LoadFromFile(f);
            var p = c.Propulsion;
            if (p is null || !p.ConstantSpeed || p.PropDiameterM <= 0) continue;
            double r = 0.5 * p.PropDiameterM, af = 100.0, hub = 0.15;
            int blades = p.MaxPowerW > 400000 ? 4 : 2;
            double vne = c.Limits?.VneMs ?? 100.0;
            var st = BladeElement.GovernorStops(blades, r, af, hub, p.MaxPowerW, p.MaxRpm, vne);

            // The rule, checked back: at the FINE stop, standing still, on 26 inHg, does it make rated rpm? That is
            // true when the propeller's torque there equals the torque 26 inHg can supply at rated rpm.
            double omega = p.MaxRpm * 2 * System.Math.PI / 60.0;
            double qAvail = BladeElement.PowerFractionAtManifold(26.0) * p.MaxPowerW / omega;
            double qFine = BladeElement.Solve(new BladeElement.Geometry(blades, r, st.FineDeg, af, hub), 0.0, 0.0, p.MaxRpm, 1.225).TorqueNm;
            // And at the COARSE stop at Vd on full power, the propeller must absorb at least rated torque.
            double qCoarse = BladeElement.Solve(new BladeElement.Geometry(blades, r, st.CoarseDeg, af, hub), 1.1 * vne, 0.0, p.MaxRpm, 1.225).TorqueNm;
            double qRated = p.MaxPowerW / omega;
            _o.WriteLine($"{c.Id,-22}{p.MaxPowerW / 745.7,7:F0}  {p.PropDiameterM,5:F2}  {p.MaxRpm,4:F0}  {vne * 1.94384,6:F0}  {1.1 * vne * 1.94384,5:F0} | {st.FineDeg,7:F1}  {st.CoarseDeg,9:F1} | fine Q {qFine,6:F0} vs avail {qAvail,6:F0};  coarse Q {qCoarse,6:F0} vs rated {qRated,6:F0}");
        }
    }
}

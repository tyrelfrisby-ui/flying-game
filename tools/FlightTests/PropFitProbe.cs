using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// FITTING EACH TYPE'S BLADE TO ITS OWN CRUISE (Phase 1, the per-type fit). A blade-element propeller needs geometry
/// no config carries, and rather than invent chord and twist per type the plan is a standard blade family fitted to
/// something already trusted. The anchor is each type's RIG CONDITION — rig.tasMs at rig.throttleFraction and
/// rig.altitudeM — which is the cruise point its whole trim and rig were solved at. The aeroplane's DRAG there is
/// known from the aero model, so the blade is simply the one whose thrust equals it.
///
/// That needs no new published numbers, it cannot disagree with the aeroplane's own performance, and for a
/// fixed-pitch type it also reports the rpm that falls out, which is the number a pilot would recognise.
/// </summary>
public class PropFitProbe
{
    private readonly ITestOutputHelper _o;
    public PropFitProbe(ITestOutputHelper o) { _o = o; }

    /// <summary>Drag at the rig condition: the body-x force with the propeller contributing nothing.</summary>
    private static double DragAtRig(AircraftConfig c, double rho, Vec3 vBody)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        var (f, _) = AeroModel.Compute(c, tables, vBody, Vec3.Zero, Vec3.Zero, rho,
            new ControlDeflections(0, c.Rig.ElevatorTrimRad, 0, 0));
        return -f.X;      // body x is forward, so drag is the negative of it
    }

    [Fact]
    public void Probe()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        // Each aeroplane's speeds in ITS OWN units (Ty 2026-11-09: mph on the J-3, 2-33, Great Lakes and Pitts;
        // knots on the Extra). The ASI itself still reads knots everywhere — that is noted and not yet built — but
        // there is no reason to report a Cub's cruise in knots in the meantime.
        _o.WriteLine("type                   dia  b   rig cruise   alt ft  thr | drag N | fitted pitch | rpm there | eta");
        foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(d.FullName, "configs", "aircraft"), "*.json"))
        {
            var c = AircraftConfigLoader.LoadFromFile(f);
            var p = c.Propulsion;
            if (p is null || p.PropDiameterM <= 0 || c.Engines.Count > 0 || c.Rig is null || c.Rig.TasMs <= 0) continue;
            if (p.ConstantSpeed) continue;     // a governed blade is free: its stops are what matter, not one pitch
            double alt = c.Rig.AltitudeM, rho = Atmosphere.DensityAtAltitude(alt);
            double a = c.Rig.AlphaRad;
            Vec3 v = new(c.Rig.TasMs * System.Math.Cos(a), 0, c.Rig.TasMs * System.Math.Sin(a));
            double drag = DragAtRig(c, rho, v);
            if (!(drag > 1)) continue;
            double rad = 0.5 * p.PropDiameterM; int blades = p.PropBladeCount; double af = p.ActivityFactor;
            // The pitch whose thrust at the balanced rpm equals the drag. Thrust rises with pitch here, so bisect.
            double lo = 8, hi = 40, bestRpm = 0, bestEta = 0;
            for (int i = 0; i < 26; i++)
            {
                double mid = 0.5 * (lo + hi);
                var g = new BladeElement.Geometry(blades, rad, mid, af);
                double rpm = PistonEngine.BalancedRpm(g, p.MaxPowerW, p.MaxRpm, p.IdleRpm, c.Rig.ThrottleFraction, rho / 1.225, c.Rig.TasMs, rho);
                var res = BladeElement.Solve(g, c.Rig.TasMs, 0.0, rpm, rho);
                bestRpm = rpm; bestEta = res.Efficiency;
                if (res.ThrustN > drag) hi = mid; else lo = mid;
            }
            double pitch = 0.5 * (lo + hi);
            bool mph = c.Id.Contains("j3-cub") || c.Id.Contains("2-33") || c.Id.Contains("great-lakes") || c.Id.Contains("pitts");
            string speed = mph ? $"{c.Rig.TasMs * 2.23694:F0} mph" : $"{c.Rig.TasMs * 1.94384:F0} kt";
            _o.WriteLine($"{c.Id,-22}{p.PropDiameterM,5:F2} {blades,2}  {speed,10} {alt * 3.28084,7:F0} {c.Rig.ThrottleFraction,5:F2} | {drag,6:F0} | {pitch,12:F1} | {bestRpm,9:F0} | {bestEta,5:F3}");
        }
    }
}

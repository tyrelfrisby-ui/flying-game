using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE FLOAT CUB AT ITS OWNER'S CRUISE (Ty 2026-11-09): "i don't cruise at full throttle. i set 2200 rpm in cruise in
/// my j3 on floats and it goes about 75 mph."
///
/// This is a stronger datum than a run-up, because it tests the AIRFRAME as well as the propeller: at 75 mph in level
/// flight the thrust at 2200 rpm must equal the aeroplane's drag. If the two cannot be reconciled by a sensible blade
/// then one of the two is wrong, and the direction of the error says which.
/// </summary>
public class FloatCubCruiseProbe
{
    private readonly ITestOutputHelper _o;
    public FloatCubCruiseProbe(ITestOutputHelper o) { _o = o; }
    private const double MphPerMs = 2.23694;

    private static AircraftConfig Load(string id)
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    /// <summary>Level flight at this speed: the alpha where lift carries the weight, and the drag there.</summary>
    private (double alphaDeg, double dragN, double liftN) Level(AircraftConfig c, double vMs, double rho)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double w = c.Mass.MassKg * 9.80665;
        double lo = -4 * System.Math.PI / 180, hi = 16 * System.Math.PI / 180;
        for (int i = 0; i < 40; i++)
        {
            double a = 0.5 * (lo + hi);
            Vec3 v = new(vMs * System.Math.Cos(a), 0, vMs * System.Math.Sin(a));
            var (f, _) = AeroModel.Compute(c, tables, v, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral);
            double lift = -(f.Z * System.Math.Cos(a) - f.X * System.Math.Sin(a));
            if (lift < w) lo = a; else hi = a;
        }
        double alpha = 0.5 * (lo + hi);
        Vec3 vv = new(vMs * System.Math.Cos(alpha), 0, vMs * System.Math.Sin(alpha));
        var (ff, _) = AeroModel.Compute(c, tables, vv, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral);
        double drag = -(ff.X * System.Math.Cos(alpha) + ff.Z * System.Math.Sin(alpha));
        double liftOut = -(ff.Z * System.Math.Cos(alpha) - ff.X * System.Math.Sin(alpha));
        return (alpha * 180 / System.Math.PI, drag, liftOut);
    }

    [Fact]
    public void Probe()
    {
        // Ty 2026-11-09: "the float drag is probably too high tune it down until you get cruise at 75 mph at 2200
        // rpm". The blade is fitted (15.33 deg from 2150 static), so the thrust at 2200 rpm and 75 mph is already
        // decided: the drag has to equal it.
        var fl = Load("j3-cub-floats-like");
        var p = fl.Propulsion!;
        double v75 = 75.0 / MphPerMs;
        var blade = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, p.BladePitch75Deg, p.ActivityFactor);
        var thr = BladeElement.Solve(blade, v75, 0.0, 2200.0, 1.225);
        _o.WriteLine($"blade {p.BladePitch75Deg:F2} deg at 2200 rpm, 75 mph: THRUST {thr.ThrustN:F0} N ({thr.ThrustN * 0.2248:F0} lb), absorbing {thr.PowerW / 745.7:F1} hp");
        var wheels = Load("j3-cub-like");
        _o.WriteLine($"for reference, the WHEELED Cub's drag at 75 mph is {Level(wheels, v75, 1.225).dragN:F0} N (cd0Area {wheels.Fuselage!.Cd0Area:F2})\n");

        double lo = 0.40, hi = 1.40;
        for (int k = 0; k < 40; k++)
        {
            double mid = 0.5 * (lo + hi);
            fl.Fuselage!.Cd0Area = mid;
            if (Level(fl, v75, 1.225).dragN > thr.ThrustN) hi = mid; else lo = mid;
        }
        double fit = 0.5 * (lo + hi);
        fl.Fuselage!.Cd0Area = fit;
        var lv = Level(fl, v75, 1.225);
        _o.WriteLine($">>> float cd0Area {fit:F3} gives drag {lv.dragN:F0} N at 75 mph = the thrust at 2200 rpm (was 1.25)");
        _o.WriteLine($"    shaft power there: {thr.PowerW / 745.7:F1} hp of {p.MaxPowerW / 745.7:F0}, and the engine can make {p.MaxPowerW / 745.7 * 2200 / p.MaxRpm:F1} hp at 2200 rpm wide open");
        _o.WriteLine($"    so that cruise is {100 * thr.PowerW / (p.MaxPowerW * 2200 / p.MaxRpm):F0} % of what is available at 2200 rpm");
    }
}

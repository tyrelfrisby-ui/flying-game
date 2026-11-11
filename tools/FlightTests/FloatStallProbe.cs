using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Where the floats' lift actually shows: the stall. Ty 2026-11-09, correcting himself — they carry their
/// own weight plus some AT STALL, not at cruise, and "stall speed wing floats is roughly 2-5% lower with floats
/// depending on their rigged incidence".</summary>
public class FloatStallProbe
{
    private readonly ITestOutputHelper _o;
    public FloatStallProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id)
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    /// <summary>Slowest speed at which wing + floats still carry the weight, searching alpha at each speed.</summary>
    private static double StallMs(AircraftConfig c, bool floatLift)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double w = c.Mass.MassKg * 9.80665;
        for (double v = 50; v > 8; v -= 0.05)
        {
            double best = 0;
            for (double aDeg = 0; aDeg <= 22; aDeg += 0.25)
            {
                double a = aDeg * System.Math.PI / 180.0;
                Vec3 vb = new(v * System.Math.Cos(a), 0, v * System.Math.Sin(a));
                var (f, _) = AeroModel.Compute(c, tables, vb, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral);
                if (floatLift) f += FloatAero.Compute(c, vb, 1.225).Force;
                double lift = -(f.Z * System.Math.Cos(a) - f.X * System.Math.Sin(a));
                if (lift > best) best = lift;
            }
            if (best < w) return v + 0.05;
        }
        return double.NaN;
    }

    [Fact]
    public void Probe()
    {
        var c = Load("j3-cub-floats-like");
        double bare = StallMs(c, false);
        _o.WriteLine($"float Cub, {c.Mass.MassKg:F0} kg: stall WITHOUT float lift {bare * 2.23694:F1} mph");
        _o.WriteLine("  rigged at | stall mph | reduction | floats carry at the stall (share of the seaplane exchange weight)");
        foreach (double inc in new[] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0 })
        {
            c.Floats!.IncidenceDeg = inc;
            double s = StallMs(c, true);
            double aStall = 15.0 * System.Math.PI / 180.0;
            Vec3 vb = new(s * System.Math.Cos(aStall), 0, s * System.Math.Sin(aStall));
            var ff = FloatAero.Compute(c, vb, 1.225).Force;
            double lift = -(ff.Z * System.Math.Cos(aStall) - ff.X * System.Math.Sin(aStall));
            // What the float installation costs, read from the two configs rather than from a literal 553.0 that went
            // stale the moment the J-3's authored mass moved (2026-11-11, the empty-weight lane). Note the exchange:
            // the owner, "floats use an exchange weight removing landing gear and adding floats", so this delta is floats-on
            // MINUS gear-off (125 lb), not what the floats alone weigh (Edo 1320s: 171-182 lb installed).
            double wFloat = (c.Mass.MassKg - Load("j3-cub-like").Mass.MassKg) * 9.80665;
            _o.WriteLine($"  {inc,9:F0}° | {s * 2.23694,9:F1} | {100 * (1 - s / bare),8:F1} % | {lift,6:F0} N = {100 * lift / wFloat,3:F0} % of the exchange weight");
        }
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>2-33 spoilers are modest upper-surface panels: full brakes roughly triple the drag at approach
/// speed (L/D ~23 → ~7), they are NOT terminal-velocity dive brakes. Owner 2026-09-08: the old model used the
/// whole wing as a flat plate and the glider fell vertically at 27 kt.</summary>
public class SpoilerTests
{
    [Fact]
    public void FullSpoilersAddAModestDragIncrement()
    {
        AircraftConfig c = TestAircraftConfig.Load();
        var tables = Aircraft.BuildAirfoilTables(c);
        double rho = Atmosphere.SeaLevelDensityKgM3, V = 22.0, a = 0.07;
        var vel = new Vec3(V * Math.Cos(a), 0, V * Math.Sin(a));
        (Vec3 f0, _) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, 0));
        (Vec3 f1, _) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, 1.0));
        double S = 0; foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) foreach (var st in sf.Strips) S += st.Area;
        double q = 0.5 * rho * V * V;
        double dCd = -Vec3.Dot(f1 - f0, vel / V) / (q * S);
        Assert.InRange(dCd, 0.02, 0.06);
    }

    [Fact]
    public void VerticalDiveWithBrakesIsNotSlow()
    {
        // Straight-down terminal speed with full spoilers must be well above approach speed (real 2-33: past Vne).
        AircraftConfig c = TestAircraftConfig.Load();
        var tables = Aircraft.BuildAirfoilTables(c);
        double rho = Atmosphere.SeaLevelDensityKgM3, W = c.Mass.MassKg * 9.81;
        double vT = 0;
        for (double V = 20; V < 120; V += 1)
        {
            (Vec3 f, _) = AeroModel.Compute(c, tables, new Vec3(V, 0, 0), Vec3.Zero, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, 1.0));
            if (-f.X >= W) { vT = V; break; }
        }
        Assert.True(vT > 40, $"terminal dive speed with brakes only {vT:F0} m/s — brakes far too strong");
    }
}

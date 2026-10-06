using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Stick-fixed pitch stability and elevator power, measured from the aero model (owner 2026-10-06: the hinged-surface
/// research). Static margin = −Cmα/CLα in mean chords; elevator power Cmδ. Printed for the current tail and for a tail
/// whose stab and elevator share one section with the finite-span lift slope (Helmbold).</summary>
public class PitchStabilityTests
{
    private readonly ITestOutputHelper _o;
    public PitchStabilityTests(ITestOutputHelper o) { _o = o; }

    internal static (double sm, double clA, double cmD, double mac) Measure(AircraftConfig c)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double v = 40, rho = 1.225, S = 0, cS = 0;
        foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) foreach (var st in sf.Strips) { S += st.Area; cS += st.Area * st.Chord; }
        double mac = cS / S, q = 0.5 * rho * v * v;
        (double cl, double cm) At(double aDeg, double eDeg)
        {
            double a = aDeg * Math.PI / 180;
            var (f, m) = AeroModel.Compute(c, tables, new Vec3(v * Math.Cos(a), 0, v * Math.Sin(a)), Vec3.Zero, Vec3.Zero, rho, new ControlDeflections(0, eDeg * Math.PI / 180, 0, 0));
            double lift = -(f.Z * Math.Cos(a) - f.X * Math.Sin(a));
            return (lift / (q * S), m.Y / (q * S * mac));
        }
        var p0 = At(1, 0); var p1 = At(5, 0); var e0 = At(3, -2); var e1 = At(3, 2);
        double clA = (p1.cl - p0.cl) / (4 * Math.PI / 180), cmA = (p1.cm - p0.cm) / (4 * Math.PI / 180);
        double cmD = (e1.cm - e0.cm) / (4 * Math.PI / 180);
        return (-cmA / clA, clA, cmD, mac);
    }

    internal static double Helmbold(double ar, double a2d = 2 * Math.PI) => a2d * ar / (2 + Math.Sqrt(ar * ar * Math.Pow(a2d / (2 * Math.PI), -2) + 4)) ;

    [Fact]
    public void EveryTypeIsStaticallyStableInPitch()
    {
        // Stick-fixed static margin with the hinged-section tails (finite-span slope). Light aircraft fly at roughly
        // 5–20 % MAC; every type must at least be stable.
        string dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var bad = new List<string>();
        foreach (string f in Directory.GetFiles(dir, "*-like.json").OrderBy(x => x))
        {
            if (f.Contains(" 2")) continue;
            var c = AircraftConfigLoader.LoadFromFile(f);
            var m = Measure(c);
            _o.WriteLine($"{Path.GetFileNameWithoutExtension(f),-24} SM {m.sm * 100,5:F1} % MAC   CLα {m.clA:F2}/rad   Cmδ {m.cmD:F2}/rad");
            if (m.sm <= 0.02) bad.Add($"{c.Id} {m.sm * 100:F1} %");
        }
        Assert.True(bad.Count == 0, "unstable or neutral: " + string.Join(", ", bad));
    }

    private static double Sample(AirfoilTableData t, double a)
    {
        for (int i = 0; i < t.AlphaRad.Length - 1; i++)
            if (t.AlphaRad[i] <= a && a <= t.AlphaRad[i + 1]) { double f = (a - t.AlphaRad[i]) / (t.AlphaRad[i + 1] - t.AlphaRad[i]); return t.Cl[i] + f * (t.Cl[i + 1] - t.Cl[i]); }
        return 0;
    }
}

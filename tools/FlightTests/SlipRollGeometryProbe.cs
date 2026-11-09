using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// PHASE 2 MEASUREMENT (owner 2026-11-09: "make each of these real science not lump sums"). The separated wing's
/// rolling moment due to sideslip is a TABLE — NACA TR-379 read off the published curves, scaled by the wing's own
/// separated fraction so it does not double-count the strip wing's attached-flow roll. This probe asks the question
/// that decides whether the table can go: at each angle of attack, how much rolling moment does the STRIP WING alone
/// make at 20 deg of sideslip, and how does that compare with the table it is gated against?
///
/// Both are reported as C_l at beta 20, which is the table's own unit, so they are directly comparable.
/// </summary>
public class SlipRollGeometryProbe
{
    private readonly ITestOutputHelper _out;
    public SlipRollGeometryProbe(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // The table as it stands, for the comparison.
    private static readonly double[] TabAlpha = { 0, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60 };
    private static readonly double[] TabMono = { 0, 0.003, 0.015, 0.045, 0.078, 0.056, 0.038, 0.034, 0.031, 0.028, 0.026, 0.024, 0.022, 0.020, 0.018 };
    private static readonly double[] TabBi = { 0, 0.003, 0.010, 0.023, 0.038, 0.050, 0.050, 0.043, 0.036, 0.031, 0.027, 0.025, 0.023, 0.021, 0.019 };

    [Fact]
    public void Probe()
    {
        foreach (string id in new[] { "pitts-s2b-like", "great-lakes-like", "j3-cub-like", "glider-2-33-like", "extra-300-like", "c172-like" })
        {
            var c = Load(id);
            string kind = c.SpinAero?.RollDueToSideslip ?? "";
            var tables = Aircraft.BuildAirfoilTables(c);
            double S = 0; foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing", System.StringComparison.OrdinalIgnoreCase)) foreach (var st in sf.Strips) S += st.Area;
            double b = AeroModel.WingSpan(c);
            // The table OFF: everything the geometry itself produces.
            var off = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            if (off.SpinAero != null) off.SpinAero.RollDueToSideslip = "";

            _out.WriteLine($"=== {id}  (table: {(kind.Length == 0 ? "none" : kind)})  S {S:F2} m2  b {b:F2} m");
            _out.WriteLine("   alpha |   wings only  |  whole a/c  | table C_l | wings/table");
            for (int i = 0; i < TabAlpha.Length; i++)
            {
                double aDeg = TabAlpha[i];
                double a = aDeg * System.Math.PI / 180.0, beta = 20.0 * System.Math.PI / 180.0;
                double V = 30.0;
                Vec3 v = new Vec3(V * System.Math.Cos(a) * System.Math.Cos(beta), V * System.Math.Sin(beta), V * System.Math.Sin(a) * System.Math.Cos(beta));
                double q = 0.5 * 1.225 * V * V;
                // Wings only, to compare like with like: the table is the separated WING's moment, while the fin above
                // the CG and the fuselage make their own roll from sideslip and belong to neither side of this question.
                var wingsOnly = new bool[off.Surfaces.Count];
                for (int k = 0; k < wingsOnly.Length; k++) wingsOnly[k] = off.Surfaces[k].Id.Contains("wing", System.StringComparison.OrdinalIgnoreCase);
                Vec3 m = AeroModel.Compute(off, tables, v, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral, surfaceMask: wingsOnly).Moment;
                Vec3 mAll = AeroModel.Compute(off, tables, v, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral).Moment;
                double clGeom = m.X / (q * S * b), clAll = mAll.X / (q * S * b);
                double clTab = kind == "biplane" ? TabBi[i] : kind.Length == 0 ? 0.0 : TabMono[i];
                string ratio = clTab > 1e-9 ? $"{clGeom / -clTab:+0.00;-0.00}" : "   --";
                _out.WriteLine($"   {aDeg,5:F0} | {clGeom,13:+0.0000;-0.0000} | {clAll,11:+0.0000;-0.0000} | {-clTab,9:F4} | {ratio,8}");
            }
        }
    }
}

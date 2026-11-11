using System.Text.Json;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// FLAT_PLATE_SEARCH=&lt;grid.json&gt;: the MAP of the flat-plate tail's parameter space, against the two requirements
/// Ty set on 2026-11-10 — "Put an agent on trying to adjust k factors and slopes as needed to get the flat plate
/// tail to meet our requirements".
///
///   1. ELEVATOR AUTHORITY WITH NO HOLE from α 20 to 60. His chosen 12°/1.05 collapses to 0.02 of its α 0 value
///      at α 30, which is where an ordinary (non-flat) spin sits. The NACA 0012 that ships gives 0.33 at α 30 but
///      only 0.10 at α 60.
///   2. NO ANTI-DAMPING ANYWHERE from α 10 to 60, and α 20 above all, because that is where he flies aerobatics.
///      The 9°/0.80 candidate was rejected for dM/dq +448 N·m per rad/s at α 20.
///
/// BOTH ARE SWEPT AT 2°, NOT AT FIVE POINTS. The two candidates he was shown were compared at α 10/20/30/45/60,
/// and that grid is coarser than the features being decided on: swept at 2°, the 0012 that ships today has
/// dM/dq +610 at α 28 on the Pitts, and 12°/1.05 has +2031 at α 24 — neither is visible at five points, and
/// "no anti-damping anywhere" was true of 12°/1.05 only in the sense that its band falls between two samples.
/// A requirement is only met if it is met between the samples too, so this probe reports the WORST value found
/// anywhere in the band and the angle it is at.
///
/// It injects the candidate as the horizontal tail's SECTION TABLE and changes nothing else: the fin, the rudder,
/// the wing and `AeroModel.ClampNormalForcePastStall` (off) are left exactly as the config has them. So it
/// measures a type without converting it — which is how the Pitts is measured here while its shipping config
/// keeps `naca0012-like` (Ty 2026-11-10: "Put the 0012 Pitts tail back on the app until the flat plate tail
/// work is done").
///
///   FLAT_PLATE_SEARCH=/path/grid.json   the candidate tables, from `flat_plate.py --grid`
///   FLAT_PLATE_SEARCH_TYPES=a,b,c       which types (default: the Pitts alone, which is the one he judges by)
///   FLAT_PLATE_SEARCH_BASE=1            also measure each type's CURRENT section, as the row to beat
///   FLAT_PLATE_SEARCH_ONSET=30,20,12    sweep SpinAeroConfig.ControlSpoilerOnsetDeg too (default: leave the config's)
/// </summary>
[Collection("WorldTerrainActive")]
public class FlatPlateSearchProbe
{
    private readonly ITestOutputHelper _out;
    public FlatPlateSearchProbe(ITestOutputHelper o) { _out = o; }

    /// <summary>The alphas authority is required at. 2° through the hole and the aerobatic band, 5° out to 60.</summary>
    private static readonly int[] AuthAlphas =
    {
        20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 45, 50, 55, 60,
    };
    /// <summary>The alphas damping is required at: every 2° from 10 to 60, the band Ty named.</summary>
    private static readonly int[] DampAlphas =
    {
        10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52, 54, 56, 58, 60,
    };

    private sealed class Row
    {
        public string Key = "";
        public double A0;                 // stop-to-stop pitching moment at α 0, N·m — the absolute scale
        public double[] Auth = Array.Empty<double>();
        public double MinAuth; public int MinAuthAt;
        public double WorstDq; public int WorstDqAt;
    }

    [Fact]
    public void Probe()
    {
        string? grid = Environment.GetEnvironmentVariable("FLAT_PLATE_SEARCH");
        if (grid == null) return;
        bool clampWas = AeroModel.ClampNormalForcePastStall;
        AeroModel.ClampNormalForcePastStall = false;
        try
        {
            DirectoryInfo? d = new(AppContext.BaseDirectory);
            while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
            string root = Path.Combine(d.FullName, "configs", "aircraft");

            var cand = JsonSerializer.Deserialize<Dictionary<string, Tbl>>(File.ReadAllText(grid))!;
            string[] types = (Environment.GetEnvironmentVariable("FLAT_PLATE_SEARCH_TYPES") ?? "pitts-s2b-like")
                             .Split(',', StringSplitOptions.RemoveEmptyEntries);
            _out.WriteLine($"{cand.Count} candidates x {types.Length} type(s); clamp OFF; authority at "
                         + string.Join("/", AuthAlphas) + "; dM/dq every 2 deg 10-60");

            foreach (string id in types)
            {
                string path = Path.Combine(root, id + ".json");
                _out.WriteLine($"\n======== {id} ========");
                if (Environment.GetEnvironmentVariable("FLAT_PLATE_SEARCH_BASE") != null)
                {
                    Row b = Measure(path, null, null);
                    _out.WriteLine("BASE (the section this config ships now)\n" + Format(b));
                }
                var rows = new List<Row>();
                double[] onsets = (Environment.GetEnvironmentVariable("FLAT_PLATE_SEARCH_ONSET") ?? "")
                                  .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(double.Parse).ToArray();
                if (onsets.Length == 0) onsets = new[] { double.NaN };     // NaN = leave the config alone
                foreach (double on in onsets)
                    foreach (var (key, tbl) in cand)
                        rows.Add(Measure(path, double.IsNaN(on) ? key : $"{key}_on{on:g}", tbl, on));

                // Everything that meets BOTH requirements, best authority-floor first. The floor is the number
                // that matters: a candidate is only as good as its worst alpha in the band.
                var ok = rows.Where(r => r.WorstDq < 0).OrderByDescending(r => r.MinAuth).ToList();
                _out.WriteLine($"-- {ok.Count} of {rows.Count} candidates have NO anti-damping anywhere in 10-60 deg --");
                foreach (Row r in ok.Take(25)) _out.WriteLine(Format(r));
                _out.WriteLine("-- best authority floor regardless of damping, for the map's sake --");
                foreach (Row r in rows.OrderByDescending(r => r.MinAuth).Take(10)) _out.WriteLine(Format(r));
                // The whole map, one line per candidate, for offline analysis.
                string dump = Path.Combine(Path.GetTempPath(), $"flatplate-map-{id}.tsv");
                using (var w = new StreamWriter(dump))
                {
                    w.WriteLine("key\ta0\tminAuth\tminAuthAt\tworstDq\tworstDqAt\t" + string.Join("\t", AuthAlphas));
                    foreach (Row r in rows)
                        w.WriteLine($"{r.Key}\t{r.A0:F1}\t{r.MinAuth:F4}\t{r.MinAuthAt}\t{r.WorstDq:F0}\t{r.WorstDqAt}\t"
                                  + string.Join("\t", r.Auth.Select(x => x.ToString("F4"))));
                }
                _out.WriteLine($"full map: {dump}");
            }
        }
        finally { AeroModel.ClampNormalForcePastStall = clampWas; }
    }

    private static string Format(Row r) =>
        $"  {r.Key,-22} floor {r.MinAuth:F3} at a{r.MinAuthAt,-3} worst dM/dq {r.WorstDq,+6:+0;-0} at a{r.WorstDqAt,-3} "
      + $"a0 {r.A0,7:F0} N.m | " + string.Join(" ", AuthAlphas.Zip(r.Auth, (a, v) => $"{a}:{v:F2}"));

    private sealed class Tbl
    {
        public double[] alphaRad { get; set; } = Array.Empty<double>();
        public double[] cl { get; set; } = Array.Empty<double>();
        public double[] cd { get; set; } = Array.Empty<double>();
        public double[] cm { get; set; } = Array.Empty<double>();
    }

    /// <summary>
    /// One candidate on one type. The config is RELOADED for every candidate on purpose: every per-config table in
    /// AeroModel / TailWake / BiplaneCellule is a ConditionalWeakTable keyed on the AircraftConfig OBJECT, so a
    /// mutated config would keep serving the first candidate's derived tail table under the next one's name.
    /// </summary>
    private static Row Measure(string path, string? key, Tbl? tbl, double onsetDeg = double.NaN)
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(path);
        if (!double.IsNaN(onsetDeg) && cfg.SpinAero is not null) cfg.SpinAero.ControlSpoilerOnsetDeg = onsetDeg;
        int hi = cfg.Surfaces.FindIndex(s => s.Id == "hStab");
        string baseAf = cfg.Surfaces[hi].Strips[0].Airfoil;
        if (tbl != null)
            cfg.AirfoilTables[baseAf] = new AirfoilTableData
            { AlphaRad = tbl.alphaRad, Cl = tbl.cl, Cd = tbl.cd, Cm = tbl.cm };
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double e = cfg.Controls.Elevator.MaxDeflRad;

        double DCm(double deg)
        {
            double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
            double m(double de) => AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.0,
                                                     new ControlDeflections(0, de, 0, 0)).Moment.Y;
            return Math.Abs(m(-e) - m(e));
        }
        double dMdq(double deg)
        {
            double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
            double m(double q) => AeroModel.Compute(cfg, tables, v, new Vec3(0, q, 0), Vec3.Zero, 1.0,
                                                    new ControlDeflections(0, 0, 0, 0)).Moment.Y;
            return (m(0.1) - m(-0.1)) / 0.2;
        }

        var r = new Row { Key = key ?? $"BASE:{baseAf}", A0 = DCm(0) };
        r.Auth = AuthAlphas.Select(x => r.A0 > 0 ? DCm(x) / r.A0 : 0).ToArray();
        r.MinAuth = double.MaxValue;
        for (int i = 0; i < AuthAlphas.Length; i++)
            if (r.Auth[i] < r.MinAuth) { r.MinAuth = r.Auth[i]; r.MinAuthAt = AuthAlphas[i]; }
        r.WorstDq = double.MinValue;
        foreach (int x in DampAlphas)
        {
            double q = dMdq(x);
            if (q > r.WorstDq) { r.WorstDq = q; r.WorstDqAt = x; }
        }
        return r;
    }
}

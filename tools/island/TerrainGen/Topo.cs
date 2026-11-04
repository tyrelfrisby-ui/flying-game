using System.Text.Json;
using FlyingGame.Core;
namespace IslandTerrainGen;

/// <summary>
/// Real topography as the shape of six areas (tools/island/topo/dem.py makes the templates from public DEMs):
/// Sitka, Mount Fairweather, the Grand Canyon, Dillingham/Mokuleia, the Knik Glacier and Johnson Creek.
/// Each template is the real place scaled by ONE factor c horizontally and vertically, so its slopes,
/// valley width/depth ratios and hypsometry are the real ones. Here it replaces the generator's own macro
/// surface inside its window (feathered over FeatherKm), BELOW every authored constraint: the pads, lakes,
/// clearance surfaces and the coast still bound it exactly as before.
///
/// An authored summit or crest that the area does not take over keeps its own massif: the template fades out
/// within ProtectKm of it (Frank Church beside Johnson Creek, Chugach beside the Knik, the Mokuleia crest
/// above Dillingham, the Chugach-Denali crest along Sitka's back).
/// </summary>
public static class Topo
{
    public const double FeatherKm = .6, ProtectInnerKm = .25, ProtectKm = 1.0;
    /// <summary>Authored summits an area takes over: the template's own summit replaces the authored one.</summary>
    public static readonly Dictionary<string, string> Owns = new() { ["Fairweather"] = "fairweather" };
    /// <summary>Authored crests kept as they are. The Chugach-Denali crest is not among them: it lay under the A2
    /// airway (no ground above 2,975 ft) and its line is now the backs of the Sitka, Knik and Fairweather areas.</summary>
    public static readonly HashSet<string> KeepCrests = new() { "Mokuleia", "Sierra", "Summit" };
    public static string Repo = "";

    public sealed class Area
    {
        public string Id = "";
        public double E0, N0, Cell, C; public int W, H;
        public float[] Z = Array.Empty<float>(); // NaN = real sea
        public (double e0, double e1, double n0, double n1) Window;
        public double Sample(IslandPoint p, out bool inside)
        {
            double x = (p.E - E0) / Cell, y = (p.N - N0) / Cell; inside = x >= 0 && y >= 0 && x <= W - 1 && y <= H - 1;
            if (!inside) return double.NaN;
            int i = Math.Min(W - 2, (int)x), j = Math.Min(H - 2, (int)y); double tx = x - i, ty = y - j;
            float a = Z[j * W + i], b = Z[j * W + i + 1], c = Z[(j + 1) * W + i], d = Z[(j + 1) * W + i + 1];
            // Real sea counts as ground just above the sea (a fjord or a lagoon inside the island's own land).
            static double L(float v) => float.IsNaN(v) ? 3 : v;
            return (L(a) * (1 - tx) + L(b) * tx) * (1 - ty) + (L(c) * (1 - tx) + L(d) * tx) * ty;
        }
        /// <summary>1 inside the window, smoothstep to 0 over the feather at its edge.</summary>
        public double Weight(IslandPoint p)
        {
            double d = Math.Min(Math.Min(p.E - Window.e0, Window.e1 - p.E), Math.Min(p.N - Window.n0, Window.n1 - p.N));
            return d <= 0 ? 0 : IslandTerrain.Smooth(Math.Min(1, d / FeatherKm));
        }
    }

    static List<Area>? _areas;
    public static List<Area> Areas(string repo)
    {
        if (_areas != null) return _areas;
        string dir = Path.Combine(repo, "tools/island/topo");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "templates.json")));
        var raw = File.ReadAllBytes(Path.Combine(dir, "templates.bin"));
        var list = new List<Area>();
        foreach (var a in doc.RootElement.GetProperty("areas").EnumerateArray())
        {
            var win = a.GetProperty("window"); int w = a.GetProperty("w").GetInt32(), h = a.GetProperty("h").GetInt32(), off = a.GetProperty("offset").GetInt32();
            var area = new Area
            {
                Id = a.GetProperty("id").GetString()!, W = w, H = h, Cell = a.GetProperty("cell_km").GetDouble(), C = a.GetProperty("c").GetDouble(),
                Window = (win[0].GetDouble(), win[1].GetDouble(), win[2].GetDouble(), win[3].GetDouble()), Z = new float[w * h]
            };
            area.E0 = area.Window.e0; area.N0 = area.Window.n0;
            for (int k = 0; k < w * h; k++)
            {
                short v = BitConverter.ToInt16(raw, 2 * (off + k));
                area.Z[k] = v == short.MinValue ? float.NaN : v / 10f;
            }
            list.Add(area);
        }
        return _areas = list;
    }

    /// <summary>Template height and its weight (0..1) at every node; NaN where no template applies.</summary>
    public static (double[] z, double[] w) Field(Grid g, string repo)
    {
        var areas = Areas(repo);
        var summits = IslandTerrain.World["height_field"]["peaks"].Items.Select(q => (id: q.S("id"), p: q["centre"].Point)).ToArray();
        var crests = IslandTerrain.World["height_field"]["crests"].Items.Select(q => (id: q.S("id"), path: q["path"])).ToArray();
        var z = new double[g.N]; var w = new double[g.N];
        Parallel.For(0, g.N, k =>
        {
            var p = g.P(k); double sz = 0, sw = 0;
            foreach (var a in areas)
            {
                double wt = a.Weight(p); if (wt <= 0) continue;
                double v = a.Sample(p, out bool inside); if (!inside) continue;
                foreach (var (id, sp) in summits)
                {
                    if (Owns.TryGetValue(id, out var owner) && owner == a.Id) continue;
                    wt *= IslandTerrain.Smooth(Math.Clamp(((p - sp).Length - ProtectInnerKm) / ProtectKm, 0, 1));
                }
                foreach (var (id, path) in crests)
                    if (KeepCrests.Contains(id)) wt *= IslandTerrain.Smooth(Math.Clamp((IslandTerrain.Path(p, path).d - ProtectInnerKm) / ProtectKm, 0, 1));
                sz += v * wt; sw += wt;
            }
            w[k] = Math.Min(1, sw); z[k] = sw > 0 ? sz / sw : double.NaN;
        });
        return (z, w);
    }
}

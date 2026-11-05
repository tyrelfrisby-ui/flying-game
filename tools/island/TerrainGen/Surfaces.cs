using System.Text.Json;
using FlyingGame.Core;
namespace IslandTerrainGen;

/// <summary>
/// What the gear meets on the glaciers and the high ground (Ty's spec, 2026-11-02,
/// docs/pending/ISLAND-TERRAIN-SURFACES-TY.md). Runs after Glaciers.Apply on the same lattice and writes four
/// channels the runtime reads (IslandRelief) plus a few landforms baked into the heights:
///
///  * SNOW: glacier surface above the early-summer snowline (the shader's rule, same constants), and every
///    designated snow patch. Smooth: nothing is added to it at runtime.
///  * ICE (the Rough channel): bare glacier ice below the snowline. The runtime adds its undulation
///    (IslandSurfaces) in proportion to this weight.
///  * CREVASSE: where the ice is pulled apart hard enough to break. Shallow-ice velocities from the routed
///    flux, the strain-rate tensor of that flow, Glen's law (A = 2.4e-24 Pa^-3 s^-1, n = 3, temperate ice) for
///    the deviatoric stress, and the largest principal (tensile) stress against a fracture threshold of
///    100 kPa — the low end of the 90-320 kPa Vaughan (1993) found across real glaciers, appropriate to
///    temperate ice. The runtime cuts the crevasses and raises the seracs in proportion to this intensity.
///  * LEDGE: a designated rock ledge on a dry mountain.
///
/// Moraines: rock that falls onto the ice from every wall above it rides down the flow lines (single-flow on
/// the ice surface). Where two branches join, their margin stripes run on side by side: that is a medial
/// moraine, made the way real ones are, not drawn. Debris thicker than a few cm insulates the ice (Ostrem
/// 1959: ~0.4 of bare-ice melt under ~10 cm), so a stripe stands up as an ice-cored ridge by 0.6 of the
/// ablation it has seen since the snowline, capped where a ridge of rock rubble 50 m wide would be at the
/// angle of repose (35 deg: 17.5 m).
///
/// Nothing here may sit in an approach, a departure, a strip, a ski zone, a spawn, a lake, a cave or a nav aid:
/// all of them are under the PROTECT mask, which every channel is multiplied by.
/// </summary>
public static class Surfaces
{
    const double Ft = IslandTerrain.Feet;
    /// <summary>Glen's flow-law rate factor for temperate ice (Cuffey and Paterson 2010), Pa^-3 s^-1.</summary>
    public const double GlenA = 2.4e-24;
    /// <summary>Tensile stress at which glacier ice crevasses: 100 kPa (Vaughan 1993, temperate end).</summary>
    public const double FractureStressPa = 100e3;
    /// <summary>Ablation under a debris stripe relative to bare ice (Ostrem 1959, ~10 cm of debris).</summary>
    public const double DebrisMeltRatio = .4;
    /// <summary>Highest ice-cored moraine ridge: a 50 m wide rubble ridge at the 35 deg angle of repose.</summary>
    public const double MaxRidgeM = 17.5;
    /// <summary>The early-summer snowline the shader paints: local ELA - 220 m, 160 m lower on a steep north
    /// face (IslandTerrain.shader, the same constants).</summary>
    public const double SnowlineBelowElaM = 220, SnowAspectM = 160;

    public sealed record Site(string Id, string Kind, string Shape, IslandPoint Centre, double HeadingDeg, double LengthM, double WidthM,
                              double SlopeDeg, double CrossDeg, double CentreM, double LowM, double HighM, double CutFillM, double SupportCutFillM);
    public static readonly List<Site> Sites = new();
    /// <summary>The site mask (Ty's list only) of the mass being processed.</summary>
    static double[] SiteProtect = Array.Empty<double>();

    public static void Empty(Mass m)
    {
        int n = m.G.N; m.Snow = new double[n]; m.Rough = new double[n]; m.Crevasse = new double[n]; m.Ledge = new double[n]; m.Protect = new double[n];
    }

    public static void Apply(Mass m, Action<string> say)
    {
        var g = m.G; int n = g.N; Empty(m);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Protection(m);
        say($"{m.Name}: protection mask {sw.Elapsed.TotalSeconds:F1}s");

        var sm = g.Blur(m.H, 2);
        // ---- SNOW / ICE classes on the glacier -----------------------------------------------------
        var ela = new double[n];
        for (int k = 0; k < n; k++) ela[k] = Climate.Ela(m.Rain[k]);
        for (int j = 1; j < g.H - 1; j++) for (int i = 1; i < g.W - 1; i++)
        {
            int k = j * g.W + i; if (m.Ice[k] <= 0) continue;
            double dx = (sm[k + 1] - sm[k - 1]) / (2 * g.CellM), dy = (sm[k + g.W] - sm[k - g.W]) / (2 * g.CellM);
            double s = Math.Sqrt(dx * dx + dy * dy), north = s > 1e-6 ? -dy / s : 0, steep = Math.Min(1, s / .35);
            double line = ela[k] - SnowAspectM * north * steep - SnowlineBelowElaM;
            // Glacier snow only on the glacier: the feathered margin outside it is rock, not landing ground.
            m.Snow[k] = IslandTerrain.Smooth((m.H[k] - (line - 40)) / 130) * IslandTerrain.Smooth((m.Ice[k] - .15) / .45);
        }

        // ---- crevasses: tensile stress of the shallow-ice flow ---------------------------------------
        var u = new double[n];
        for (int k = 0; k < n; k++)
            if (m.Ice[k] > .5 && m.IceCore[k] > 0 && m.Thick[k] > 0) u[k] = m.Flux[k] / g.CellM / Math.Max(10, m.Thick[k]); // m/yr
        u = g.Blur(u, 2);
        var vx = new double[n]; var vy = new double[n];
        for (int j = 1; j < g.H - 1; j++) for (int i = 1; i < g.W - 1; i++)
        {
            int k = j * g.W + i; if (u[k] <= 0) continue;
            double dx = (sm[k + 1] - sm[k - 1]) / 2, dy = (sm[k + g.W] - sm[k - g.W]) / 2, s = Math.Sqrt(dx * dx + dy * dy);
            if (s < 1e-9) continue; vx[k] = -u[k] * dx / s; vy[k] = -u[k] * dy / s;
        }
        var c0 = new double[n]; const double year = 3.156e7;
        for (int j = 1; j < g.H - 1; j++) for (int i = 1; i < g.W - 1; i++)
        {
            int k = j * g.W + i; if (m.Ice[k] < .6 || m.Fixed[k]) continue;
            double h2 = 2 * g.CellM;
            double exx = (vx[k + 1] - vx[k - 1]) / h2 / year, eyy = (vy[k + g.W] - vy[k - g.W]) / h2 / year;
            double exy = .5 * ((vx[k + g.W] - vx[k - g.W]) / h2 + (vy[k + 1] - vy[k - 1]) / h2) / year, ezz = -(exx + eyy);
            double ee = Math.Sqrt(.5 * (exx * exx + eyy * eyy + ezz * ezz) + exy * exy); if (ee < 1e-14) continue;
            double e1 = .5 * (exx + eyy) + Math.Sqrt(.25 * (exx - eyy) * (exx - eyy) + exy * exy);
            double sigma1 = Math.Pow(GlenA, -1.0 / 3) * Math.Pow(ee, -2.0 / 3) * e1;
            c0[k] = IslandTerrain.Smooth((sigma1 - FractureStressPa) / (.5 * FractureStressPa));
        }
        var cb = g.Blur(c0, 1, 2);
        for (int k = 0; k < n; k++)
        {
            // The blurred field's faint fringe (< 0.1) is not a crevasse field; remap so intensity rises
            // continuously from 0 there (a hard cut-off would leave a step in the crevasse depth).
            double c = Math.Max(0, (cb[k] - .1) / .9) * IslandTerrain.Smooth((m.Ice[k] - .5) / .3) * (1 - m.Protect[k]);
            m.Crevasse[k] = c < .004 ? 0 : c;
            // An icefall is bare: snow bridges fall in and seracs shed their snow. A crevasse field is ICE.
            m.Snow[k] *= 1 - IslandTerrain.Smooth(m.Crevasse[k] / .3);
        }

        // ---- moraines: debris from every wall above the ice, carried down the flow lines --------------
        Moraines(m, sm);

        // ---- the authored ski zones are snow, whatever the climate does ------------------------------
        // Not a stamped disc. Ty (2026-11-03): "glacier landings are just on snow" — a landing area on a real
        // glacier is the glacier's own snow, told apart only by being smoother and free of crevasses. So each zone
        // is a snowfield lying ALONG its glacier (the authored path's last segment through the zone), sized from
        // the zone's own length and width and grown by the safety ground round it, with an edge that wanders
        // (value noise on 90 m and 35 m) and fades over ~100 m instead of a 25 m step. The landing core — the
        // authored 150 m flat pad that SurfaceAt calls Snow — is always fully inside it.
        foreach (var q in IslandTerrain.World["glaciers"].Items)
        {
            if (!q.Has("ski_zone")) continue;   // Knik: no ski zone since 2026-11-04 (Knik River bar strip instead)
            var z = q["ski_zone"]; var c = z["centre"].Point; var path = q["path"].Items;
            var axis = path[^1].Point - path[^2].Point; axis = axis * (1 / Math.Max(1e-9, axis.Length));
            double half = z.N("length_m") / 2000 + .15, wide = z.N("width_m") / 2000 + .18;   // km: zone + safety ground
            for (int k = 0; k < n; k++)
            {
                var d = g.P(k) - c; if (d.Length > half + .3) continue;
                double along = d.Dot(axis), across = d.E * axis.N - d.N * axis.E;
                double q2 = Math.Sqrt(along * along / (half * half) + across * across / (wide * wide));
                double wander = .22 * (ValueNoise(g.P(k), .09, 11) - .5) * 2 + .10 * (ValueNoise(g.P(k), .035, 23) - .5) * 2;
                double w = 1 - IslandTerrain.Smooth((q2 + wander - .80) / .45);
                // Not over a river bar's braids (Knik's F13 lies at the glacier's foot): the snow ends at the outwash.
                foreach (var id in new[] { "F13", "F14", "F15" })
                {
                    var b = g.P(k) - IslandTerrain.Feature(id)["centre"].Point;
                    double outside = Math.Max(Math.Abs(b.E) - .35, Math.Abs(b.N) - .11);
                    w *= IslandTerrain.Smooth(outside / .06);
                }
                w = Math.Max(w, 1 - IslandTerrain.Smooth((d.Length - .17) / .10));   // the flat landing core, always snow
                if (w <= m.Snow[k]) continue;
                m.Snow[k] = w; m.Crevasse[k] *= 1 - w;
            }
        }
        for (int k = 0; k < n; k++) m.Rough[k] = IslandTerrain.Smooth((m.Ice[k] - .15) / .45) * (1 - m.Snow[k]) * (1 - m.Protect[k]);

        // ---- snow patches and ledges --------------------------------------------------------------------
        if (m.Name == "mainland") Sites_(m, ela, say);

        double cell = g.CellM * g.CellM / 1e6; double aIce = 0, aSnow = 0, aBare = 0, aCrev = 0, aDebris = 0;
        for (int k = 0; k < n; k++)
        {
            if (m.Ice[k] > .5) { aIce += cell; if (m.Snow[k] > .5) aSnow += cell; else aBare += cell; if (m.Crevasse[k] > .5) aCrev += cell; if (m.Debris[k] > .35) aDebris += cell; }
        }
        m.Log.Add($"surfaces: ice {aIce:F2} km2 = SNOW {aSnow:F2} + ICE {aBare:F2} km2 (crevassed {aCrev:F2} km2, debris-covered {aDebris:F2} km2)");
        say($"{m.Name}: surfaces {sw.Elapsed.TotalSeconds:F1}s; ice {aIce:F2} km2, snow {aSnow:F2}, bare {aBare:F2}, crevassed {aCrev:F2}, debris {aDebris:F2}");
    }

    // ---------------------------------------------------------------------------------------------------
    /// <summary>1 inside every place that must stay clear, falling to 0 over 150 m outside it.</summary>
    static void Protection(Mass m)
    {
        var g = m.G; var W = IslandTerrain.World;
        // km outside each zone (<= 0 inside); `site` marks Ty's list for the landing places (airports, strips,
        // lakes, caves, nav aids, spawns, approach fans, plus the ski zones). The rest keep only ice detail away.
        // Each zone: distance (km) outside the item itself, the margin the ice detail keeps, and the margin the
        // landing sites keep (NaN: sites may be there).
        var zones = new List<(Func<IslandPoint, double> f, double all, double site)>();
        string cat = ""; var cats = new List<string>();
        void Z(Func<IslandPoint, double> f, double all, double site) { zones.Add((f, all, site)); cats.Add(cat); }
        cat = "fields"; foreach (var f in IslandTerrain.Fields)
        {
            var ff = f;
            Z(p => { var d = p - ff.Centre; return Math.Max(Math.Abs(d.Dot(ff.U)) - ff.Length / 2000, Math.Abs(d.Dot(ff.V)) - ff.Width / 2000); }, .40, .15);
        }
        // Every approach and departure surface (the published instrument finals and the visual runway ends), its
        // own footprint and guard, plus 100 m.
        cat = "fans"; foreach (var e in W["clearance_budgets"].Items)
        {
            if (e.S("kind") == "airway") continue;
            var u = e["u"].Point; var a = e["a"].Point; double guard = e.N("guard_km", .25), length = e.N("length"), half = e.N("half_width_km", .15), widen = e.N("widen", .05);
            Z(p =>
            {
                var dp = p - a; double along = -dp.Dot(u), cross = Math.Abs(dp.E * u.N - dp.N * u.E);
                return Math.Max(Math.Max(-along - guard, along - length - guard), cross - (half + widen * Math.Max(0, along)) - guard);
            }, .10, 0);
        }
        // Ski zones: no heading is published, so every direction of approach out to 2 km.
        cat = "ski"; foreach (var q in W["glaciers"].Items) { if (!q.Has("ski_zone")) continue; var c = q["ski_zone"]["centre"].Point; Z(p => (p - c).Length, 2.0, 1.5); }
        cat = "lakes"; foreach (var q in W["water"].Items)
        {
            var qq = q; double r = Math.Min(q["radii_km"][0].Number(), q["radii_km"][1].Number());
            Z(p => (IslandTerrain.EllipseRadius(p, qq) - 1) * r, .6 * r, .3 * r);
        }
        cat = "mesa"; foreach (var q in W["mesa_pads"].Items) { var c = q["centre"].Point; Z(p => (p - c).Length, .6, .3); }
        cat = "starts"; foreach (var q in W["starts"].Items) { var c = q["centre"].Point; Z(p => (p - c).Length, .8, .5); }
        cat = "features"; foreach (var q in W["features"].Items)
        {
            if (!q.Has("centre")) continue; var c = q["centre"].Point;
            bool cave = q.S("id") == "F1" || q.S("id") == "F2";
            Z(p => (p - c).Length, cave ? 1.0 : .5, cave ? 1.0 : double.NaN);
            if (q.Has("interior") && q["interior"].Has("chamber_centre")) { var cc = q["interior"]["chamber_centre"].Point; Z(p => (p - cc).Length, 1.0, 1.0); }
        }
        cat = "lessons"; foreach (var q in W["lesson_terrain"].Items)
        {
            var box = q["box_path"]; var safe = q["safe_path"];
            Z(p => IslandTerrain.Path(p, box).d, .4, double.NaN); Z(p => IslandTerrain.Path(p, safe).d, .4, double.NaN);
        }
        cat = "beaches"; foreach (var key in new[] { "landable_beaches", "backcountry_strips" })
            if (W.Has(key)) foreach (var q in W[key].Items) if (q.Has("centre")) { var c = q["centre"].Point; Z(p => (p - c).Length, .8, .5); }
        cat = "navaids"; foreach (var c in Navaids()) Z(p => (p - c).Length, .6, .3);

        if (Environment.GetEnvironmentVariable("SITE_DEBUG") != null)
        {
            var land = Enumerable.Range(0, g.N).Where(k => !m.C.Ocean[k] && k % 7 == 0).ToList();
            foreach (var cname in cats.Distinct())
            {
                int inside = land.Count(k => { var p = g.P(k); for (int z = 0; z < zones.Count; z++) if (cats[z] == cname && zones[z].f(p) <= (double.IsNaN(zones[z].site) ? -1 : zones[z].site)) return true; return false; });
                Console.WriteLine($"  protection {cname}: {100.0 * inside / land.Count:F1}% of land");
            }
        }
        var prot = m.Protect; var siteProt = SiteProtect = new double[g.N];
        Parallel.For(0, g.N, k =>
        {
            if (m.C.Ocean[k]) { prot[k] = siteProt[k] = 1; return; }
            var p = g.P(k); double best = double.PositiveInfinity, bestSite = double.PositiveInfinity;
            foreach (var (z, all, site) in zones) { double d = z(p); best = Math.Min(best, d - all); if (!double.IsNaN(site)) bestSite = Math.Min(bestSite, d - site); if (bestSite <= 0 && best <= 0) break; }
            prot[k] = 1 - IslandTerrain.Smooth(best / .15); siteProt[k] = 1 - IslandTerrain.Smooth(bestSite / .15);
        });
    }

    /// <summary>Every VOR and NDB in the IFR data and every outer nav-aid site (island-spec.json; the runtime world
    /// does not carry the IFR block).</summary>
    public static List<IslandPoint> Navaids()
    {
        var result = new List<IslandPoint>();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Topo.Repo, "docs/pending/island-spec.json")));
        var navaids = doc.RootElement.GetProperty("ifr").GetProperty("navaids");
        foreach (var kind in navaids.EnumerateObject())
        {
            if (kind.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var s in kind.Value.EnumerateArray())
                if (s.ValueKind == JsonValueKind.Object && s.TryGetProperty("centre", out var c)) result.Add(new IslandPoint(c[0].GetDouble(), c[1].GetDouble()));
        }
        foreach (var q in IslandTerrain.World["outer_navaid_sites"].Items) result.Add(q["centre"].Point);
        return result;
    }

    // ---------------------------------------------------------------------------------------------------
    static void Moraines(Mass m, double[] sm)
    {
        var g = m.G; int n = g.N;
        bool IceAt(int k) => m.Ice[k] > .5;
        var load = new double[n]; var exposure = new double[n];
        for (int j = 1; j < g.H - 1; j++) for (int i = 1; i < g.W - 1; i++)
        {
            int k = j * g.W + i; if (!IceAt(k)) continue;
            for (int d = 0; d < 8; d++)
            {
                int nk = (j + Grid.DJ[d]) * g.W + i + Grid.DI[d]; if (m.Ice[nk] > .2) continue;
                // A rock wall above the ice (steeper than 30 deg toward this node) sheds onto it.
                double rise = (m.H[nk] - m.H[k]) / (Grid.DL[d] * g.CellM);
                if (rise > .58) { load[k] = 1; break; }
            }
        }
        // Descending surface order: everything upstream of a node is final when it is reached.
        var order = Enumerable.Range(0, n).Where(IceAt).ToArray(); var keys = order.Select(k => -sm[k]).ToArray();
        Array.Sort(keys, order);
        var u = new double[n];
        for (int k = 0; k < n; k++) if (m.Thick[k] > 0) u[k] = Math.Max(5, m.Flux[k] / g.CellM / Math.Max(10, m.Thick[k]));
        foreach (int k in order)
        {
            if (load[k] <= 0) continue; int i = k % g.W, j = k / g.W; if (i == 0 || j == 0 || i == g.W - 1 || j == g.H - 1) continue;
            int best = -1; double drop = 0;
            for (int d = 0; d < 8; d++)
            {
                int nk = (j + Grid.DJ[d]) * g.W + i + Grid.DI[d]; if (!IceAt(nk)) continue;
                double s = (sm[k] - sm[nk]) / Grid.DL[d]; if (s > drop) { drop = s; best = d; }
            }
            if (best < 0) continue;
            int next = (j + Grid.DJ[best]) * g.W + i + Grid.DI[best];
            // Melt seen below the snowline while riding this cell (m of ice), toward the ridge height.
            // (u is 0 on an ice node with no routed thickness; 0/0 there was NaN, which the blur below spread over
            // whole rows of the island the first time the relief put such a node on a rock-shed path, 2026-11-03.)
            double melt = m.Snow[k] > .5 || u[k] <= 0 ? 0 : Math.Max(0, -m.Balance[k]) / .9 * Grid.DL[best] * g.CellM / u[k];
            load[next] = Math.Max(load[next], load[k]);
            exposure[next] = Math.Max(exposure[next], exposure[k] + melt);
        }
        // Debris cover on the ice itself is the stripes plus the snout: englacial debris melts out where the
        // ice has lost the most (beyond 2 m w.e./yr of melt, ~290 m below the ELA at 0.7 m per 100 m), and the
        // slowing ice piles it there. (Glaciers.Apply had put cover over the whole ablation zone; with the ice
        // now a surface of its own, that hid the bare ice under a uniform grey.)
        for (int k = 0; k < n; k++)
            if (m.Ice[k] > .5) m.Debris[k] = Math.Clamp((-m.Balance[k] - 2) / 4, 0, 1) * m.Ice[k];
        var stripe = new double[n];
        for (int k = 0; k < n; k++) stripe[k] = load[k] * (1 - m.Snow[k]) * (1 - m.Protect[k]);
        var wide = g.Blur(stripe, 1, 2);
        var ridge = new double[n];
        for (int k = 0; k < n; k++) ridge[k] = stripe[k] * Math.Min(MaxRidgeM, (1 - DebrisMeltRatio) * exposure[k]);
        ridge = g.Blur(ridge, 1, 2);
        double area = 0, top = 0;
        for (int k = 0; k < n; k++)
        {
            if (m.Ice[k] <= .2) continue;
            m.Debris[k] = Math.Max(m.Debris[k], Math.Min(1, wide[k] * 1.6));
            if (m.Fixed[k] || ridge[k] < .05) continue;
            double up = Math.Min(m.Upper[k] - m.H[k], ridge[k]); if (up <= 0) continue;
            m.H[k] += up; top = Math.Max(top, up); if (wide[k] > .3) area += g.CellM * g.CellM / 1e6;
        }
        m.Log.Add($"moraines on the ice: {area:F2} km2 of supraglacial stripes (lateral and medial), ice-cored ridges up to {top:F1} m");
    }

    /// <summary>Deterministic value noise in [0, 1] on a lattice of <paramref name="cellKm"/>, smooth between nodes.</summary>
    static double ValueNoise(IslandPoint p, double cellKm, uint seed)
    {
        double x = p.E / cellKm, y = p.N / cellKm; int i = (int)Math.Floor(x), j = (int)Math.Floor(y);
        double tx = IslandTerrain.Smooth(x - i), ty = IslandTerrain.Smooth(y - j);
        static double Hash(int a, int b, uint s)
        {
            uint h = unchecked((uint)a * 0x8da6b343u ^ (uint)b * 0xd8163841u ^ s * 0xcb1ab31fu);
            h ^= h >> 13; h = unchecked(h * 0x5bd1e995u); h ^= h >> 15; return (h & 0xffffff) / (double)0xffffff;
        }
        double a00 = Hash(i, j, seed), a10 = Hash(i + 1, j, seed), a01 = Hash(i, j + 1, seed), a11 = Hash(i + 1, j + 1, seed);
        return (a00 * (1 - tx) + a10 * tx) * (1 - ty) + (a01 * (1 - tx) + a11 * tx) * ty;
    }

    // ---------------------------------------------------------------------------------------------------
    sealed record Target(string Kind, string Shape, double SlopeDeg, double L, double W, int Order = 0)
    {
        /// <summary>A snow patch lies the way snow lies (Ty, 2026-11-03: not perfectly flat): banana, crest or rolling,
        /// on top of its slope. Ledges are rock and keep their authored flat / sloped / rounded shapes.</summary>
        public NaturalShapeParams Natural => Kind == "patch" ? NaturalShapeParams.For($"patch:{SlopeDeg}:{L}", L, W, Order) : NaturalShapeParams.Flat;
    }
    /// <summary>Most cut or fill a site may need: on its shelf (landable area + one cell), and over the whole
    /// Catmull-Rom support. Design limits — a shelf, not a quarry.</summary>
    public const double ShelfCutFillM = 25, SupportCutFillM = 50;

    /// <summary>
    /// Designated landing places, found by a deterministic search over the generated ground: for each target
    /// (kind, shape, slope, size) every node on a 50 m stride and every heading at 15 deg is tried; the ground
    /// under a footprint (the landable rectangle plus the Catmull-Rom support, so the surface is the exact plane
    /// or dome over the whole landable area: see Support) is fitted with the target surface, and the site that needs the least
    /// cut and fill (at most 25 m: a shelf, not a quarry) wins, at least 0.45 km from every other site (patches are
    /// scattered over the same mountains) or 1.2 km (ledges are 'here and there'), and 100 m
    /// beyond every protected place.
    ///   SNOW PATCH: on a glaciated mountain (within 2.5 km of ice, off the ice or on its last feathered edge, at least 700 m up and no more
    ///   than 400 m below the local ELA), level across, rising uphill at its slope.
    ///   LEDGE: on a dry mountain — one that holds no glacier: more than 1 km from any ice and at least 300 m
    ///   below its local ELA — with 100 m of relief within 300 m; flat, sloped, or rounded
    ///   (a crown 2.5 m above its ends).
    /// </summary>
    static void Sites_(Mass m, double[] ela, Action<string> say)
    {
        var g = m.G; int n = g.N;
        // Distance to ice (km), 8-connected chamfer, out to 2.6 km.
        var dIce = new double[n]; Array.Fill(dIce, double.PositiveInfinity);
        var q = new PriorityQueue<int, double>();
        for (int k = 0; k < n; k++) if (m.Ice[k] > .5) { dIce[k] = 0; q.Enqueue(k, 0); }
        while (q.TryDequeue(out int k, out double dk))
        {
            if (dk > dIce[k] || dk > 2.6) continue; int i = k % g.W, j = k / g.W;
            for (int d = 0; d < 8; d++) { int ni = i + Grid.DI[d], nj = j + Grid.DJ[d]; if (!g.Inside(ni, nj)) continue; int nk = nj * g.W + ni; double nd = dk + Grid.DL[d] * g.Cell; if (nd < dIce[nk]) { dIce[nk] = nd; q.Enqueue(nk, nd); } }
        }
        // Protection grown by 100 m: a site must be clear, not merely outside.
        var near = g.Blur(SiteProtect.Select(v => v > .01 ? 1.0 : 0).ToArray(), 2, 2);
        // And out of every authored height layer: inside one the runtime reshapes the ground after the relief, and the
        // site would no longer be its plane (v1's LD4 met lesson 2's box-canyon layer at its uphill end: 10 m off).
        Parallel.For(0, n, k => { if (!m.C.Ocean[k] && IslandTerrain.AuthoredLayerAt(g.P(k))) near[k] = 1; });
        var relief = new double[n];
        Parallel.For(0, g.H, j =>
        {
            for (int i = 0; i < g.W; i++)
            {
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                for (int b = -12; b <= 12; b += 3) for (int a = -12; a <= 12; a += 3) { if (!g.Inside(i + a, j + b)) continue; double z = m.H[(j + b) * g.W + i + a]; lo = Math.Min(lo, z); hi = Math.Max(hi, z); }
                relief[j * g.W + i] = hi - lo;
            }
        });
        var targets = new List<Target>
        {
            // Order: which natural shape each patch takes (banana, crest, rolling in turn over the ones that are placed).
            new("patch", "plane", 1, 160, 40, 1), new("patch", "plane", 3, 130, 35, 0), new("patch", "plane", 4, 150, 35, 2), new("patch", "plane", 5, 120, 30, 1),
            new("patch", "plane", 7, 110, 30, 1), new("patch", "plane", 8, 110, 30, 1), new("patch", "plane", 10, 100, 25, 0), new("patch", "plane", 12, 100, 25, 2),
            new("ledge", "flat", 0, 150, 45), new("ledge", "flat", 1, 130, 40), new("ledge", "sloped", 4, 170, 45), new("ledge", "sloped", 7, 150, 40),
            new("ledge", "rounded", 2, 160, 50), new("ledge", "rounded", 0, 140, 45),
        };
        if (Environment.GetEnvironmentVariable("SITE_MAP") != null)
            for (int j = g.H - 1; j >= 0; j -= 16)
            {
                var line = new System.Text.StringBuilder();
                for (int i = 0; i < g.W; i += 8)
                {
                    int k = j * g.W + i; char ch = m.C.Ocean[k] ? '~' : m.Ice[k] > .5 ? '#' : near[k] > 0 ? '.' : dIce[k] <= 2.5 && m.H[k] >= 700 ? 'P' : dIce[k] > 1 && m.H[k] >= 350 ? 'L' : ' ';
                    line.Append(ch);
                }
                say(line.ToString());
            }
        var chosen = new List<IslandPoint>();
        int index = 0;
        foreach (var t in targets)
        {
            index++;
            bool patch = t.Kind == "patch";
            var cands = new List<int>(); var why = new int[6];
            for (int j = 4; j < g.H - 4; j += 2) for (int i = 4; i < g.W - 4; i += 2)
            {
                int k = j * g.W + i; if (m.C.Ocean[k]) continue;
                if (m.Fixed[k]) { why[0]++; continue; }
                if (near[k] > 0) { why[1]++; continue; }
                if (m.Ice[k] > (patch ? .3 : .05)) { why[2]++; continue; }
                double z = m.H[k];
                if (patch ? !(dIce[k] <= 2.5 && dIce[k] >= .05) : !(dIce[k] > 1.0)) { why[3]++; continue; }
                if (patch ? !(z >= 700 && z >= ela[k] - 400) : !(z >= 350 && z < ela[k] - 300)) { why[4]++; continue; }
                if (relief[k] < 100) { why[5]++; continue; }
                cands.Add(k);
            }
            if (Environment.GetEnvironmentVariable("SITE_DEBUG") != null) say($"  rejected: fixed {why[0]} protected {why[1]} ice {why[2]} glaciation {why[3]} height {why[4]} relief {why[5]}");
            var results = new (double score, int k, int hd, double a, double cut, double cs)[cands.Count]; var fail = new int[5];
            double maxCross = Math.Tan((patch ? 3 : 2) * Math.PI / 180);
            Parallel.For(0, cands.Count, c =>
            {
                int k = cands[c]; var p0 = g.P(k); var best = (double.PositiveInfinity, k, 0, 0.0, 0.0, 0.0);
                for (int hd = 0; hd < 360; hd += 15)
                {
                    var uu = IslandPoint.Heading(hd); var vv = new IslandPoint(uu.N, -uu.E);
                    double s = Math.Tan(t.SlopeDeg * Math.PI / 180);
                    var (hl, hw) = Support(g, t, uu, vv);
                    double sum = 0, sb = 0, sbb = 0, srb = 0; int cnt = 0; bool ok = true;
                    var nodes = Footprint(g, p0, uu, vv, hl, hw);
                    double il0 = t.L / 2 + g.CellM, iw0 = t.W / 2 + g.CellM;
                    foreach (var (nk, a, b) in nodes)
                    {
                        // A snow patch's shelf must be off the ice; the support band beyond it may lie on the glacier's
                        // edge, which the patch turns to snow (Place). A ledge is on a dry mountain throughout.
                        bool shelf = Math.Abs(a) <= il0 && Math.Abs(b) <= iw0;
                        bool onIce = m.Ice[nk] > (patch ? .3 : .05) && (shelf || !patch);
                        if (m.C.Ocean[nk] || m.Fixed[nk] || onIce || near[nk] > 0)
                        { ok = false; System.Threading.Interlocked.Increment(ref fail[m.Fixed[nk] ? 0 : onIce ? 1 : 2]); break; }
                        double rr = m.H[nk] - Shape(t, s, 0, a, b); sum += rr; sb += b; sbb += b * b; srb += rr * b; cnt++;
                    }
                    if (!ok || cnt == 0) continue;
                    // Least-squares cross slope, held to what a landing tolerates (3 deg on snow, 2 on rock; none on a dome).
                    double vb = sbb - sb * sb / cnt, cs = t.Shape == "rounded" || vb <= 0 ? 0 : Math.Clamp((srb - sum * sb / cnt) / vb, -maxCross, maxCross);
                    // The SHELF is the landable area plus one cell: that is what is held to 25 m of cut and fill and
                    // scored. The rest of the support is fixed to the same plane — it has to be, for the landable area
                    // to be exact — and may cut deeper into the slope above (reported, not limited).
                    double a0 = (sum - cs * sb) / cnt, worst = 0, ss = 0, worstAll = 0; int inner = 0;
                    double il = t.L / 2 + g.CellM, iw = t.W / 2 + g.CellM;
                    foreach (var (nk, a, b) in nodes)
                    {
                        double z = a0 + Shape(t, s, cs, a, b), r = m.H[nk] - z;
                        worstAll = Math.Max(worstAll, Math.Abs(r));
                        if (Math.Abs(a) <= il && Math.Abs(b) <= iw) { ss += r * r; worst = Math.Max(worst, Math.Abs(r)); inner++; }
                        // Between the authored ceiling and floor: a runtime layer would otherwise pull the shelf down to
                        // a clearance cap or lift it back up to a floor, and the landable area would no longer be the plane.
                        if (z > m.Upper[nk] - 3 || z < m.C.Lo[nk] + .5) { ok = false; System.Threading.Interlocked.Increment(ref fail[3]); break; }
                    }
                    if (inner == 0) continue;
                    if (!ok) continue;
                    // Design limits, not physics: 25 m on the shelf, and twice that where the support cuts back into
                    // the slope above it — beyond that the site is a quarry with a cliff behind it.
                    if (worst > ShelfCutFillM || worstAll > SupportCutFillM) System.Threading.Interlocked.Increment(ref fail[4]);
                    double score = Math.Sqrt(ss / inner) + .25 * worst;
                    if (worst <= ShelfCutFillM && worstAll <= SupportCutFillM && score < best.Item1) best = (score, k, hd, a0, worst, cs);
                }
                results[c] = best;
            });
            var pick = results.Where(r => double.IsFinite(r.score)).OrderBy(r => r.score).ThenBy(r => r.k)
                              .FirstOrDefault(r => chosen.All(c => (c - g.P(r.k)).Length >= (patch ? .45 : 1.2)), (double.PositiveInfinity, -1, 0, 0, 0, 0));
            if (Environment.GetEnvironmentVariable("SITE_DEBUG") != null)
            {
                var fin = results.Where(r => double.IsFinite(r.score)).OrderBy(r => r.score).Take(5).ToList();
                say($"  footprint fails: fixed {fail[0]} ice {fail[1]} protected {fail[2]} ceiling {fail[3]} cut/fill {fail[4]}");
                say($"  {t.Kind} {t.Shape} {t.SlopeDeg}: {cands.Count} candidates, {results.Count(r => double.IsFinite(r.score))} fit; best {string.Join(", ", fin.Select(r => $"({g.P(r.k).E:F2},{g.P(r.k).N:F2}) s{r.score:F1} w{r.cut:F1}"))}");
            }
            if (pick.k < 0) { m.Log.Add($"site {t.Kind} {t.Shape} {t.SlopeDeg} deg: NO candidate"); say($"  no site for {t.Kind} {t.SlopeDeg}"); continue; }
            var pc = g.P(pick.k); chosen.Add(pc);
            Place(m, t, pc, pick.hd, pick.a, pick.cut, pick.cs, $"{(patch ? "SP" : "LD")}{Sites.Count(x => x.Kind == t.Kind) + 1}");
            say($"  site {Sites[^1]}");
        }
    }

    /// <summary>The site's surface (m, about its centre height): its plane or dome, and — when placing, not when
    /// choosing — its natural lie. Sites are chosen on the plane alone, so laying the snow's shape on them does not
    /// move any site.</summary>
    static double Shape(Target t, double s, double cs, double a, double b, bool natural = false)
        => s * a + cs * b - (t.Shape == "rounded" ? 4 * 2.5 / (t.L * t.L) * (a * a + b * b) : 0)
         + (natural ? IslandTerrain.NaturalShape(t.Natural, a / t.L, b / (t.W / 2), a) : 0);

    /// <summary>
    /// Half-length and half-width of the nodes a site must fix. The runtime surface is Catmull-Rom (Keys' cubic
    /// convolution, a = -1/2), which reproduces a plane or a quadratic dome exactly only where all 16 nodes it reads
    /// lie on it — and a point reads nodes up to TWO cells away along each lattice axis. So the fixed nodes are the
    /// landable rectangle grown by a lattice-aligned square of two cells, which on the site's own axes is 2 cells x
    /// (|cos| + |sin|) of its heading: 50 m on a cardinal heading, 70.7 m on a diagonal. (v1 fixed one cell, and
    /// the edges of every patch were off its plane — SP1's cross slope measured 1.09 deg against the 0.78 it was cut to.)
    /// </summary>
    static (double hl, double hw) Support(Grid g, Target t, IslandPoint u, IslandPoint v)
        => (t.L / 2 + 2 * g.CellM * (Math.Abs(u.E) + Math.Abs(u.N)), t.W / 2 + 2 * g.CellM * (Math.Abs(v.E) + Math.Abs(v.N)));

    static List<(int k, double a, double b)> Footprint(Grid g, IslandPoint c, IslandPoint u, IslandPoint v, double hl, double hw)
    {
        var list = new List<(int, double, double)>(); double r = Math.Max(hl, hw) / 1000 + g.Cell;
        int i0 = (int)Math.Floor((c.E - r - g.E0) / g.Cell), i1 = (int)Math.Ceiling((c.E + r - g.E0) / g.Cell);
        int j0 = (int)Math.Floor((c.N - r - g.N0) / g.Cell), j1 = (int)Math.Ceiling((c.N + r - g.N0) / g.Cell);
        for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
        {
            if (!g.Inside(i, j)) continue; var d = g.P(i, j) - c; double a = d.Dot(u) * 1000, b = d.Dot(v) * 1000;
            if (Math.Abs(a) <= hl && Math.Abs(b) <= hw) list.Add((j * g.W + i, a, b));
        }
        return list;
    }

    static void Place(Mass m, Target t, IslandPoint c, int hd, double a0, double cut, double cs, string id)
    {
        var g = m.G; var u = IslandPoint.Heading(hd); var v = new IslandPoint(u.N, -u.E); double s = Math.Tan(t.SlopeDeg * Math.PI / 180);
        bool patch = t.Kind == "patch"; const double ring = 50;
        var (hl, hw) = Support(g, t, u, v); double support = 0;
        foreach (var (k, a, b) in Footprint(g, c, u, v, hl + ring, hw + ring))
        {
            double z = a0 + Shape(t, s, cs, Math.Clamp(a, -hl, hl), Math.Clamp(b, -hw, hw), natural: true);
            double out_ = Math.Sqrt(Math.Pow(Math.Max(0, Math.Abs(a) - hl), 2) + Math.Pow(Math.Max(0, Math.Abs(b) - hw), 2));
            if (out_ <= 0) { support = Math.Max(support, Math.Abs(m.H[k] - z)); m.H[k] = z; m.Fixed[k] = true; }
            else if (!m.Fixed[k]) m.H[k] += (z - m.H[k]) * (1 - IslandTerrain.Smooth(out_ / ring));
            double w = 1 - IslandTerrain.Smooth(out_ / (g.CellM * 1.2));
            if (patch) { m.Snow[k] = Math.Max(m.Snow[k], w); m.Ice[k] *= 1 - w; } else m.Ledge[k] = Math.Max(m.Ledge[k], w);
            m.Protect[k] = Math.Max(m.Protect[k], 1 - IslandTerrain.Smooth(out_ / ring));
            m.Rough[k] *= 1 - m.Protect[k]; m.Crevasse[k] *= 1 - m.Protect[k];
        }
        double lo = a0 + Shape(t, s, 0, -t.L / 2, 0), hi = a0 + Shape(t, s, 0, t.L / 2, 0), crossDeg = Math.Atan(cs) * 180 / Math.PI;
        var site = new Site(id, t.Kind, t.Kind == "patch" ? t.Natural.Name : t.Shape, c, hd, t.L, t.W, t.SlopeDeg, crossDeg, a0, lo, hi, cut, support);
        Sites.Add(site);
        m.Log.Add($"site {id} {t.Kind}/{t.Shape}: centre ({c.E:F3},{c.N:F3}) km = sim x {c.N * 1000:F0} y {c.E * 1000:F0} m, uphill heading {hd} deg true, landable {t.L:F0} x {t.W:F0} m, slope {t.SlopeDeg:F0} deg (cross {crossDeg:+0.0;-0.0} deg, + rising to the right of the uphill heading), centre {a0:F1} m ({a0 / Ft:F0} ft), ends {lo:F1}..{hi:F1} m, worst cut/fill {cut:F1} m on the shelf, {support:F1} m over the whole support; lies {t.Natural.Name} ({t.Natural.Amp:F2} m, sweep {t.Natural.Sweep:F2} m)");
    }
}

using FlyingGame.Core;
namespace IslandTerrainGen;

/// <summary>
/// The island's climate, chosen and then held to (docs/pending/ISLAND-TERRAIN-CLAUDE.md):
/// a temperate maritime island in the westerlies, the Olympic Mountains being the closest real analogue
/// (2,400 m summits, glaciers on the shaded faces, rainforest on the windward side, a semi-arid rain shadow).
///   * Moist flow from 310 deg true; background precipitation 2.2 m/yr at the windward coast. (v1 used 280 deg and
///     14 m/yr per unit slope. The real-topography pass, 2026-11-02, put a 1,580 m compacted Mount Fairweather
///     1.2 km from the north-west coast: turning the storm track 30 deg to the north-west puts that massif, Sitka's
///     coast and the Knik square in the onshore flow, as the Gulf of Alaska's storms are at the real places, and
///     keeps the red-rock desert in the shadow.)
///   * Orographic precipitation, 30 m/yr per unit upslope: condensate generated where the wind is forced up a slope, carried downwind
///     and falling out over FalloutKm (Smith 2003, linear upslope model reduced to its 1-D essentials);
///     descending lee air evaporates cloud and the background is depleted by what has already fallen.
///   * Summer (JJA) mean temperature 18 C at sea level, lapse 6.5 K/km.
///   * Equilibrium-line altitude from a degree-day balance: winter snow (60% of annual precipitation)
///     equals summer melt (4 mm w.e. per degree-day over 92 days), so T_JJA(ELA) = P / 613 mm.
///   * Why 30 m/yr per unit slope: windward maritime barriers carry 3-6 m/yr above their coastal background over
///     mean upwind slopes of 0.1-0.2 (Olympics: Forks 3 m -> Mt Olympus ~5.5 m w.e.; Fairweather coast 4 m ->
///     icefields 5-8 m w.e.; Westland 3 m -> 10+ m), i.e. 20-40 m/yr per unit slope after the fallout delay.
///     14 sat at the bottom of that range and left the compacted Fairweather (P 4 m/yr, ELA 1.6 km) bare.
/// </summary>
public static class Climate
{
    public const double WindFromDeg = 310, BackgroundM = 2.2, FalloutKm = 4.0, UpslopeM = 30.0, DepletionM = 2600;
    public const double SeaLevelJjaC = 18.0, LapseKPerKm = 6.5, MeltPerDegreeDayM = .004, SummerDays = 92, SnowFraction = .6;

    /// <summary>Annual precipitation (m/yr) on the grid, from a (smoothed) surface.</summary>
    public static double[] Precipitation(Grid g, double[] surface)
    {
        var hs = g.Blur(surface, 20);                       // ~0.5 km sigma: the wind sees massifs, not gullies
        var to = IslandPoint.Heading(WindFromDeg + 180);    // unit vector the wind blows toward
        // Upslope / downslope component along the wind, per node.
        var up = new double[g.N]; var down = new double[g.N];
        Parallel.For(1, g.H - 1, j =>
        {
            for (int i = 1; i < g.W - 1; i++)
            {
                int k = j * g.W + i; double dx = (hs[k + 1] - hs[k - 1]) / (2 * g.CellM), dy = (hs[k + g.W] - hs[k - g.W]) / (2 * g.CellM);
                double s = dx * to.E + dy * to.N; up[k] = Math.Max(0, s); down[k] = Math.Max(0, -s);
            }
        });
        var p = new double[g.N];
        double step = .1; int n = (int)(18 / step);
        Parallel.For(0, g.H, j =>
        {
            for (int i = 0; i < g.W; i++)
            {
                int k = j * g.W + i; var x = g.P(k);
                double oro = 0, evap = 0, lifted = 0;
                for (int s = 1; s <= n; s++)
                {
                    var q = x - to * (s * step); double w = Math.Exp(-s * step / FalloutKm) * step / FalloutKm;
                    double u = Bilinear(g, up, q), d = Bilinear(g, down, q);
                    oro += u * w; evap += d * w;
                    lifted += Bilinear(g, hs, q) > 0 ? u * step * 1000 : 0;
                }
                double background = BackgroundM * Math.Exp(-lifted / DepletionM);
                p[k] = Math.Max(.35, background + UpslopeM * (oro - .7 * evap));
            }
        });
        return p;
    }
    static double Bilinear(Grid g, double[] f, IslandPoint p)
    {
        double x = (p.E - g.E0) / g.Cell, y = (p.N - g.N0) / g.Cell;
        if (x < 0 || y < 0 || x >= g.W - 1 || y >= g.H - 1) return 0;
        int i = (int)x, j = (int)y; double tx = x - i, ty = y - j; int k = j * g.W + i;
        return (f[k] * (1 - tx) + f[k + 1] * tx) * (1 - ty) + (f[k + g.W] * (1 - tx) + f[k + g.W + 1] * tx) * ty;
    }
    /// <summary>Equilibrium-line altitude (m) for annual precipitation P (m/yr).</summary>
    public static double Ela(double p) => (SeaLevelJjaC - SnowFraction * p / (MeltPerDegreeDayM * SummerDays)) / LapseKPerKm * 1000;
}

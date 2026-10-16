namespace FlyingGame.Core;

/// <summary>
/// A small Alaska range with a Denali and glaciers you can land on (owner 2026-10-16), and at its foot the
/// country modelled on the Knik and Colony glaciers: ice ending in lakes with bergs in them, and a braided
/// river running away down an outwash plain — which is <see cref="BraidedRiver"/>.
///
/// THE MOUNTAIN. One that dominates the skyline the way the real one does: 5,900 m, nearly 19,400 ft, rising
/// off a 4,000 ft plateau, with a footprint to match — a mountain that size is 6 km across at the bottom, and
/// anything less reads as a spike rather than a massif.
///
/// THE GLACIERS are the point of it. Each is a valley full of ice: a smooth, slightly crowned surface running
/// from a basin high on the mountain down to a terminus in a lake, shallow enough to land on along its
/// length (3–6°) and getting steeper above the icefall. The ice is <see cref="WorldTerrain.Surface.Snow"/>,
/// which a wheel finds slippery and draggy at once — which is exactly why skis were invented, and they are
/// the next piece of work.
/// </summary>
public static class AlaskaRange
{
    /// <summary>
    /// The block, on the Mesa plateau. It has to clear the Cascades to the south: the first placement ran
    /// Denali's skirt over two of their lakes and put the outwash plain on top of the range, which the
    /// Cascades' own tests caught.
    /// </summary>
    public const double X0 = 2600, X1 = 8800, Y0 = -7450, Y1 = -4450;
    public static double BaseM => WorldTerrain.DatumM + 2 * WorldTerrain.StepHeightM;   // the Mesa plateau, 5,000 ft

    public readonly struct Peak
    {
        public readonly string Name;
        public readonly double X, Y, ReliefM, RadiusM;
        public Peak(string name, double x, double y, double reliefM, double radiusM)
        { Name = name; X = x; Y = y; ReliefM = reliefM; RadiusM = radiusM; }
        /// <summary>The summit above the sea: the plateau plus the peak's own relief.</summary>
        public double TopM => BaseM + ReliefM;
    }

    /// <summary>
    /// A glacier: a river of ice from <see cref="HeadX"/>,<see cref="HeadY"/> down to its terminus, with a
    /// lake at the bottom. The surface is landable the whole way below the icefall.
    /// </summary>
    public readonly struct Glacier
    {
        public readonly string Name;
        public readonly double HeadX, HeadY, HeadM;          // up in the accumulation basin (m above the sea)
        public readonly double TermX, TermY, TermM;          // where the ice ends, at the lake
        public readonly double HalfWidthM;
        public readonly double LakeRx, LakeRy;               // the proglacial lake at the terminus
        public readonly int Bergs;

        public Glacier(string name, double headX, double headY, double headReliefM,
                       double termX, double termY, double termReliefM, double halfWidthM,
                       double lakeRx, double lakeRy, int bergs)
        {
            Name = name; HeadX = headX; HeadY = headY; HeadM = BaseM + headReliefM;
            TermX = termX; TermY = termY; TermM = BaseM + termReliefM; HalfWidthM = halfWidthM;
            LakeRx = lakeRx; LakeRy = lakeRy; Bergs = bergs;
        }

        public double LengthM => System.Math.Sqrt((TermX - HeadX) * (TermX - HeadX) + (TermY - HeadY) * (TermY - HeadY));
        /// <summary>Average gradient down the ice, in degrees — what decides whether it can be landed on.</summary>
        public double SlopeDeg => System.Math.Atan2(HeadM - TermM, LengthM) * 180 / System.Math.PI;
        /// <summary>The lake's surface, just below the ice front.</summary>
        public double LakeSurfaceM => TermM - 12;
        public (double x, double y) LakeCentre =>
            (TermX + (TermX - HeadX) / LengthM * LakeRx * 0.9, TermY + (TermY - HeadY) / LengthM * LakeRx * 0.9);

        /// <summary>Where (x, y) falls on the glacier: 0 at the head, 1 at the terminus, and across in metres.</summary>
        public (double along, double across) Local(double x, double y)
        {
            double dx = TermX - HeadX, dy = TermY - HeadY, len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? ((x - HeadX) * dx + (y - HeadY) * dy) / len2 : 0;
            double px = HeadX + t * dx, py = HeadY + t * dy;
            double across = System.Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            return (t, across);
        }

        /// <summary>The ice surface at a point on the glacier: a long slope with a slight crown across it.</summary>
        public double IceHeightAt(double t, double across)
        {
            double s = System.Math.Clamp(t, 0, 1);
            // A real glacier is nearly all of its drop in the icefall at the top and then a long shallow
            // tongue — which is why people land on the tongue. (1−s)³ puts seven eighths of the fall in the
            // top half and leaves the lower half at a handful of degrees.
            double f = (1 - s) * (1 - s) * (1 - s);
            double h = TermM + (HeadM - TermM) * f;
            double u = System.Math.Clamp(across / HalfWidthM, 0, 1);
            return h + 14 * (1 - u * u);                    // crowned in the middle, like real ice
        }
    }

    /// Summits are RELIEF above the plateau, not metres above the sea: the range has to keep its shape when the
    /// plateau under it moves, and it moved 1,500 ft on 2026-10-16.
    public static readonly Peak[] Peaks =
    {
        //          name          x      y    relief radius
        new("The High One", 4200, -5900, 3881, 2000),   // 17,700 ft — the biggest thing that fits the plateau
        new("North Peak",   3200, -4900, 2681, 1100),
        new("Hunter",       3100, -6900, 2581, 1100),
        new("Foraker",      5400, -7200, 2781, 1300),
        new("Huntington",   5500, -4800, 2381, 1100),
    };

    /// <summary>
    /// Two glaciers off the massif's south side, modelled on the Knik and the Colony: both run down to a lake
    /// with bergs in it, and the outwash from those lakes is the braided river at the bottom.
    /// </summary>
    public static readonly Glacier[] Glaciers =
    {
        //                name      headX headY headRel termX termY termRel halfW lakeRx lakeRy bergs
        // The heads sit out on the flanks, not through the summit: run a glacier's trough past the top of a
        // mountain and it takes 1,300 ft off it, which is exactly what the first pair did. And they are two
        // kilometres long, because a glacier with 900 m to lose in less than that is a cliff, not a glacier —
        // the first pair averaged 47° and could not have been landed on anywhere.
        // The ice comes down TO THE PLATEAU and calves into a lake dug into it; the meltwater goes on over the
        // escarpment and feeds the braided plain on the flats below. Running the ice all the way down to that
        // plain would need a 2,100 m drop in two kilometres, which is a cliff.
        //
        // The ice fronts used to stop 80 m above the plateau 150 m short of the rim, which put each lake —
        // which sits beyond its ice front, as a proglacial lake does — OVER the rim and 620 m up in the air
        // above the plateau below. A render found it standing there as a drum of water; the test underneath
        // had only asked that the bed be below the surface, and a bed 620 m below is a flood, not a lake.
        // Both glaciers now end a kilometre further back, with their heads moved back to match so the
        // gradient is what it was, and their fronts at plateau level so the lakes have a plateau to sit in.
        // Heights here are RELIEF above that plateau, for the same reason as the peaks'.
        new Glacier("Knik",         5000, -7000,  555,  5300, -5700,    0,   420,   470,  330,  14),
        new Glacier("Colony",       6400, -6900,  536,  6600, -5600,    0,   340,   420,  300,  10),
    };

    private static bool InBlock(double x, double y)
        => x > X0 - 800 && x < X1 + 800 && y > Y0 - 900 && y < Y1 + 800;

    public static bool InRange(double x, double y) => InBlock(x, y);

    private static double Smooth(double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Ridge(double x, double y)
        => 60 * System.Math.Sin(x / 260.0) * System.Math.Sin(y / 310.0)
         + 26 * System.Math.Sin(x / 97.0 + 1.3) * System.Math.Sin(y / 113.0 + 0.4);

    /// <summary>The mountains, the glaciers cut into them, and the lakes at the ice fronts.</summary>
    public static double HeightAt(double x, double y, double baseH)
    {
        if (!InBlock(x, y)) return baseH;
        double h = baseH;

        foreach (Peak p in Peaks)
        {
            double d = System.Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            if (d >= p.RadiusM) continue;
            double t = 1 - d / p.RadiusM;
            // Smoothstep, not pow(t, 1.6): a power law has slope 1.6 at the summit, which puts a point on
            // every mountain — the range rendered as a row of witch's hats. Smoothstep's slope is zero at both
            // ends, so the summit rounds off and the foot blends into the plateau. (The same mistake, and the
            // same fix, as the high desert's peaks.)
            double cone = baseH + (p.TopM - baseH) * Smooth(t);
            cone += Ridge(x, y) * (0.3 + 0.7 * System.Math.Sin(System.Math.PI * System.Math.Clamp(t, 0, 1)));
            h = System.Math.Max(h, cone);
        }

        // The glaciers: a valley of ice cut through whatever the mountains just built.
        foreach (Glacier g in Glaciers)
        {
            var (t, across) = g.Local(x, y);
            if (t < -0.05 || t > 1.25 || across > g.HalfWidthM + 400) continue;
            double ice = g.IceHeightAt(t, across);
            if (across <= g.HalfWidthM)
            {
                h = System.Math.Min(h, ice);
                continue;
            }
            // The valley walls above the ice, so it reads as a glacier in a trough rather than a ribbon laid
            // on the mountainside.
            double w = Smooth((across - g.HalfWidthM) / 400.0);
            h = System.Math.Min(h, ice + (h - ice) * w);
        }

        // The lake at each ice front.
        foreach (Glacier g in Glaciers)
        {
            var (cx, cy) = g.LakeCentre;
            double dx = (x - cx) / g.LakeRx, dy = (y - cy) / g.LakeRy;
            double r = System.Math.Sqrt(dx * dx + dy * dy);
            if (r > 1.6) continue;
            double surf = g.LakeSurfaceM;
            if (r <= 1) { h = System.Math.Min(h, surf - 22 * System.Math.Sqrt(1 - r * r)); continue; }
            double w = Smooth((r - 1) / 0.6);
            h = System.Math.Min(h, surf + (h - surf) * w);
        }
        return h;
    }

    /// <summary>Is this point on glacier ice — the thing a ski wants and a wheel does not?</summary>
    public static bool OnIce(double x, double y)
    {
        if (!InBlock(x, y)) return false;
        foreach (Glacier g in Glaciers)
        {
            var (t, across) = g.Local(x, y);
            if (t >= 0 && t <= 1 && across <= g.HalfWidthM) return true;
        }
        return false;
    }

    /// <summary>The proglacial lakes, which have bergs in them.</summary>
    public static double? WaterSurfaceAt(double x, double y)
    {
        if (!InBlock(x, y)) return null;
        foreach (Glacier g in Glaciers)
        {
            var (cx, cy) = g.LakeCentre;
            double dx = (x - cx) / g.LakeRx, dy = (y - cy) / g.LakeRy;
            if (dx * dx + dy * dy < 1.0) return g.LakeSurfaceM;
        }
        return null;
    }

    /// <summary>
    /// The bergs: deterministic positions in each lake, so a pilot who threads them once can do it again.
    /// Returns the centre, the radius at the waterline and how far it stands proud of the water.
    /// </summary>
    public static System.Collections.Generic.IEnumerable<(double x, double y, double radiusM, double aboveM)>
        Icebergs(Glacier g)
    {
        var (cx, cy) = g.LakeCentre;
        for (int i = 0; i < g.Bergs; i++)
        {
            // A fixed spiral: no dice, and no two in the same place.
            double a = i * 2.39996323;                              // the golden angle
            double rr = System.Math.Sqrt((i + 0.5) / g.Bergs) * 0.86;
            double x = cx + System.Math.Cos(a) * rr * g.LakeRx;
            double y = cy + System.Math.Sin(a) * rr * g.LakeRy;
            double radius = 9 + 14 * ((i * 37) % 11) / 11.0;
            yield return (x, y, radius, radius * 0.22);             // a berg shows about a fifth of itself
        }
    }
}

namespace FlyingGame.Core;

/// <summary>
/// A small Cascades-style range with alpine lakes, on the Summit plateau (owner 2026-10-16: "a small
/// cascades styled mountain range with alpine lakes like the enchantments").
///
/// WHERE. East of the river on the highest plateau, which is dead flat at 5,500 ft for four kilometres in
/// every direction — nothing to disturb and nothing to disturb it. The peaks go to 8,200–9,500 ft, which puts
/// them above the terrain shader's snow line, and the lakes sit at 6,700–7,700 ft: the Enchantments'own
/// altitude, and the altitude at which a floatplane stops being able to do what it does at sea level.
///
/// THE LAKES are a stair of six, climbing south-east the way the Enchantments climb, 1,000 ft to 3,000 ft
/// long as asked. Three have an OPEN END — a notch in the rim where the ground falls away down-valley, so a
/// pilot can fly a straight-in approach and depart the same way. Three are FULL BOWLS: a cirque wall all the
/// way round, so the only way in is to spiral down inside the basin and the only way out is to spiral back
/// up. Those basins are sized for that to be possible rather than merely imaginable — see
/// <see cref="Lake.BasinRadiusM"/> and the test that measures the turning room at height.
///
/// A thing worth knowing before flying one: a 1,000 ft lake at 7,700 ft of elevation is a place you can land
/// and not leave. The arithmetic is in docs/ALPINE-LAKES.md, lake by lake.
/// </summary>
public static class AlpineRange
{
    /// <summary>The block the range occupies, east of the river on the Summit plateau.</summary>
    /// Six basins need room: a bowl 1.2–1.4 km across cannot sit a kilometre from the next one or each one
    /// carves the other's rim away — which is exactly what the first layout did.
    public const double X0 = 2400, X1 = 8600, Y0 = -14400, Y1 = -8200;
    /// <summary>The plateau it stands on.</summary>
    public static double BaseM => WorldTerrain.DatumM + 3 * WorldTerrain.StepHeightM;   // the Summit plateau, 7,000 ft

    /// <summary>A peak: a cone of rock, rounded, with its own ridges. Where two overlap they make a ridge,
    /// which is how a range gets its shape without a heightmap.</summary>
    public readonly struct Peak
    {
        public readonly string Name;
        public readonly double X, Y, ReliefM, RadiusM;
        public Peak(string name, double x, double y, double reliefM, double radiusM)
        { Name = name; X = x; Y = y; ReliefM = reliefM; RadiusM = radiusM; }
        /// <summary>The summit above the sea. Heights in this file are RELIEF above the plateau, so that the
        /// range keeps its shape when the plateau moves — which it did, by 1,500 ft, on 2026-10-16.</summary>
        public double TopM => BaseM + ReliefM;
    }

    /// <summary>
    /// An alpine lake in its basin. <see cref="OpenHeadingDeg"/> is the bearing of the notch in the rim —
    /// the way in and out — or null for a lake that is fully walled.
    /// </summary>
    public readonly struct Lake
    {
        public readonly string Name;
        public readonly double X, Y, LengthM, WidthM, HeadingDeg, SurfaceM, RimM, BasinRadiusM;
        public readonly double? OpenHeadingDeg;

        public Lake(string name, double x, double y, double lengthM, double widthM, double headingDeg,
                    double surfaceReliefM, double rimM, double basinRadiusM, double? openHeadingDeg)
        {
            Name = name; X = x; Y = y; LengthM = lengthM; WidthM = widthM; HeadingDeg = headingDeg;
            SurfaceM = BaseM + surfaceReliefM; RimM = rimM; BasinRadiusM = basinRadiusM; OpenHeadingDeg = openHeadingDeg;
        }

        public bool Bowled => OpenHeadingDeg is null;
        public double LengthFt => LengthM * 3.28084;

        /// <summary>The lake's own frame: along the long axis, and across it.</summary>
        public (double along, double across) Local(double x, double y)
        {
            double h = HeadingDeg * System.Math.PI / 180;
            double dx = x - X, dy = y - Y;
            return (dx * System.Math.Cos(h) + dy * System.Math.Sin(h),
                    -dx * System.Math.Sin(h) + dy * System.Math.Cos(h));
        }

        /// <summary>Elliptical radius: &lt;1 inside the water, 1 at the shoreline.</summary>
        public double Inside(double x, double y)
        {
            var (a, c) = Local(x, y);
            double u = a / (LengthM * 0.5), v = c / (WidthM * 0.5);
            return System.Math.Sqrt(u * u + v * v);
        }

        /// <summary>How far round the basin the point is from the open notch, in degrees (180 = opposite it).
        /// 180 for a bowl, which has no notch to be near.</summary>
        public double FromNotchDeg(double x, double y)
        {
            if (OpenHeadingDeg is null) return 180;
            double bearing = System.Math.Atan2(y - Y, x - X) * 180 / System.Math.PI;
            double d = bearing - OpenHeadingDeg.Value;
            while (d > 180) d -= 360;
            while (d < -180) d += 360;
            return System.Math.Abs(d);
        }
    }

    /// <summary>Half-width of the notch in a rim, in degrees — the gap you fly in through.</summary>
    public const double NotchHalfAngleDeg = 32.0;
    /// <summary>How far below the lake's surface the notch's floor falls, so the valley drains away and the
    /// approach is genuinely open rather than a lip to clear.</summary>
    public const double NotchDropM = 30.0;
    /// <summary>Depth of the water. Deep enough to float anything, shallow enough to see the bottom shelve.</summary>
    public const double DepthM = 14.0;

    /// Peaks stand BETWEEN the basins — at least 1.3 km from any lake centre, so a basin's carve cannot eat
    /// a summit — and where two cones overlap they make a ridge, which is where the range gets its shape.
    public static readonly Peak[] Peaks =
    {
        //        name          x       y   relief radius
        new("Thunder",     4500,  -9000,  944, 1150),
        new("Cathedral",   5900, -10300, 1224, 1250),   // the high one, in the middle of the range — 11,000 ft
        new("Sentinel",    3100, -10400,  884, 1100),
        new("Dragontail",  7400,  -9400, 1004, 1100),
        new("Colchuck",    5000, -12700, 1064, 1200),
        new("Prusik",      8000, -12700,  864, 1050),
        new("Argonaut",    2600, -11800,  904, 1000),
        new("Annapurna",   8100,  -9600,  884,  950),
    };

    /// <summary>
    /// The stair of lakes. Lengths run 1,000 ft to 3,000 ft as the owner asked; three open down-valley and
    /// three are complete bowls. The bowled basins are 1,150–1,500 m across, which is what makes a spiral in
    /// and out possible: a Beaver at this altitude turns in about 700 m at 20° of bank.
    /// </summary>
    public static readonly Lake[] Lakes =
    {
        //                    name          x      y      len  wid  hdg  relief rim basin  open   (relief = surface above the plateau)
        new Lake("Outlet",    3000,  -8900,  914, 300,  40,  384, 150, 1250, 40),      // 3,000 ft long, open north-east
        new Lake("Hidden",    6000,  -8900,  610, 240,  20,  564, 250, 1400, null),    // 2,000 ft long, a full bowl
        new Lake("Cirque",    4500, -11300,  460, 200, 340,  599, 250, 1250, null),    // 1,500 ft long, a full bowl
        new Lake("Crown",     7600, -11000,  305, 170,  10,  674, 230, 1150, null),    // 1,000 ft long, the top bowl
        new Lake("Shelf",     3200, -13500,  760, 260, 300,  454, 170, 1200, 300),     // 2,500 ft long, open north-west
        new Lake("Larch",     6600, -13600,  550, 230, 210,  444, 170, 1200, 210),     // 1,800 ft long, open south-west
    };

    private static bool InBlock(double x, double y) => x > X0 - 600 && x < X1 + 600 && y > Y0 - 600 && y < Y1 + 600;

    /// <summary>Is this point in the range's block at all? (Its ground is granite and scree.)</summary>
    public static bool InRange(double x, double y) => InBlock(x, y);

    private static double Smooth(double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Ridge roughness, so a cone reads as rock rather than a tent.</summary>
    private static double Ridge(double x, double y)
    {
        return 42 * System.Math.Sin(x / 190.0) * System.Math.Sin(y / 230.0)
             + 22 * System.Math.Sin(x / 83.0 + 1.7) * System.Math.Sin(y / 97.0 + 0.6)
             + 9 * System.Math.Sin(x / 37.0 + 2.9) * System.Math.Sin(y / 41.0 + 2.1);
    }

    /// <summary>The range's surface at (x, y), or the untouched <paramref name="baseH"/> outside it.</summary>
    public static double HeightAt(double x, double y, double baseH)
    {
        if (!InBlock(x, y)) return baseH;
        double h = baseH;

        // ---- the mountains: the highest cone wins, and overlapping cones make the ridges.
        foreach (Peak p in Peaks)
        {
            double d = System.Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            if (d >= p.RadiusM) continue;
            double t = 1 - d / p.RadiusM;
            double cone = baseH + (p.TopM - baseH) * System.Math.Pow(t, 1.35);
            // Rock, not a tent: the roughness fades out at the summit and at the foot.
            cone += Ridge(x, y) * (0.35 + 0.65 * System.Math.Sin(System.Math.PI * System.Math.Clamp(t, 0, 1)));
            h = System.Math.Max(h, cone);
        }

        // ---- the basin walls. A bowl's rim is built UP so it is there whatever the peaks do, and an open
        // lake's rim is left out across the notch so the valley can fall away.
        foreach (Lake l in Lakes)
        {
            double d = System.Math.Sqrt((x - l.X) * (x - l.X) + (y - l.Y) * (y - l.Y));
            double ringAt = l.BasinRadiusM;
            double band = 500.0;
            if (d > ringAt + band) continue;
            double near = 1 - System.Math.Abs(d - ringAt) / band;          // 1 on the ring, 0 a band away
            if (near <= 0) continue;
            double open = l.OpenHeadingDeg is null ? 0
                        : 1 - Smooth((l.FromNotchDeg(x, y) - NotchHalfAngleDeg * 0.55) / (NotchHalfAngleDeg * 0.9));
            double add = l.RimM * Smooth(near) * (1 - open);
            // Only ever ADD rock. Writing the suppressed ring as "surface + nothing" raised the ground across
            // the notch TO the lake's level, which walled off the one approach the lake was supposed to have.
            if (add > 0.5) h = System.Math.Max(h, l.SurfaceM + add);
        }

        // ---- the basins themselves, carved out of all of that: the water, the shore, and the walls above it.
        foreach (Lake l in Lakes)
        {
            double r = l.Inside(x, y);
            double d = System.Math.Sqrt((x - l.X) * (x - l.X) + (y - l.Y) * (y - l.Y));
            if (d > l.BasinRadiusM) continue;
            double shore = l.SurfaceM + 4.0;
            if (r <= 1)
            {
                // The bed: a dish, deepest in the middle.
                h = System.Math.Min(h, l.SurfaceM + 2.0 - DepthM * System.Math.Sqrt(System.Math.Max(0, 1 - r * r)));
                continue;
            }
            // The wall climbs from the SHORELINE to the basin's edge, both measured in metres along this
            // bearing — the first version measured it in elliptical units, which made the basin a long thin
            // ellipse instead of a bowl and left the notch unexcavated where it mattered.
            double dShore = d / System.Math.Max(r, 1e-9);
            double t = (d - dShore) / System.Math.Max(1.0, l.BasinRadiusM - dShore);
            double wall = Smooth(t);
            // Across the notch the ground keeps falling instead of rising: the outlet valley.
            double notch = l.OpenHeadingDeg is null ? 0
                         : 1 - Smooth((l.FromNotchDeg(x, y) - NotchHalfAngleDeg * 0.55) / (NotchHalfAngleDeg * 0.9));
            double top = l.SurfaceM + l.RimM;
            double target = shore + (top - shore) * wall;
            double drained = l.SurfaceM - NotchDropM - 120 * wall;
            h = System.Math.Min(h, target * (1 - notch) + drained * notch);
        }
        return h;
    }

    /// <summary>The lake under (x, y), if the point is over one.</summary>
    public static bool TryLakeAt(double x, double y, out Lake found)
    {
        if (InBlock(x, y))
            foreach (Lake l in Lakes)
                if (l.Inside(x, y) < 1.0) { found = l; return true; }
        found = default;
        return false;
    }

    /// <summary>Water surface over (x, y), or null — the hook <see cref="WorldTerrain.WaterSurfaceAt"/> uses.</summary>
    public static double? WaterSurfaceAt(double x, double y)
        => TryLakeAt(x, y, out Lake l) ? l.SurfaceM : null;

    /// <summary>Is this point on a lake a floatplane could be on? (Every lake here is landable water; this
    /// exists so callers do not have to know the difference between a lake and a puddle.)</summary>
    public static bool OnWater(double x, double y) => TryLakeAt(x, y, out _);
}

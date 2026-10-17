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
    /// An alpine lake in its basin.
    ///
    /// Owner 2026-10-16: "Make the terrain more natural with no round bowls. The lakes can have a valley on
    /// the exit — just some should have virtually no lip (obstacle) and others should have a lip of say
    /// 20–100 ft or tall trees, some kind of obstacle to make it more challenging." And, on sizes: some big
    /// enough to put a floatplane on, the rest small and committing.
    ///
    /// So a basin is no longer a circle of constant height. Its radius and its rim BOTH vary with bearing
    /// (<see cref="BasinAt"/>, <see cref="RimAt"/>): the wall runs high on one side and falls to a col on
    /// another, which is what a gouged cirque does and what stops six of them reading as six craters.
    ///
    /// And an outlet is no longer a gap in a ring. It is a VALLEY — a trough running away from the water for
    /// a kilometre and a half, falling as it goes, so the approach is a valley you fly up and the departure
    /// is a valley you fly down. Across its mouth lies a BAR: the rock a glacier leaves at the lip of its
    /// basin, <see cref="LipM"/> above the water, with <see cref="TimberM"/> of trees standing on it. That
    /// bar is the obstacle, and it is what you are committing to clearing when you put the aeroplane on the
    /// water. Outlet's is nothing at all; Hidden's is 80 ft of rock; Larch's is a stand of 85 ft timber.
    /// </summary>
    public readonly struct Lake
    {
        public readonly string Name;
        public readonly double X, Y, LengthM, WidthM, HeadingDeg, SurfaceM, RimM, BasinRadiusM;
        /// <summary>The bearing the outlet valley runs on, or null for a cirque walled all the way round.</summary>
        public readonly double? OutletHeadingDeg;
        /// <summary>How far that valley runs before it has given up all the height it is going to give up.</summary>
        public readonly double OutletRunM;
        /// <summary>The bar across the outlet, above the water: 0 for an open one, up to 30 m (100 ft).</summary>
        public readonly double LipM;
        /// <summary>And the timber standing on that bar — the other way to make a lake hard to leave.</summary>
        public readonly double TimberM;
        /// <summary>Long enough to put a floatplane on and get it off again at this altitude.</summary>
        public readonly bool Landable;
        /// <summary>Which irregular plan this basin has. Two basins with the same number are the same shape,
        /// which is why every one has its own.</summary>
        public readonly int Shape;

        public Lake(string name, double x, double y, double lengthM, double widthM, double headingDeg,
                    double surfaceReliefM, double rimM, double basinRadiusM, double? outletHeadingDeg,
                    double outletRunM, double lipM, double timberM, bool landable, int shape)
        {
            Name = name; X = x; Y = y; LengthM = lengthM; WidthM = widthM; HeadingDeg = headingDeg;
            SurfaceM = BaseM + surfaceReliefM; RimM = rimM; BasinRadiusM = basinRadiusM;
            OutletHeadingDeg = outletHeadingDeg; OutletRunM = outletRunM; LipM = lipM; TimberM = timberM;
            Landable = landable; Shape = shape;
        }

        public bool Bowled => OutletHeadingDeg is null;
        public double LengthFt => LengthM * 3.28084;
        /// <summary>Everything you have to clear on the way out, in feet — rock and timber together.</summary>
        public double ObstacleFt => (LipM + TimberM) * 3.28084;

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

        /// <summary>
        /// The basin's radius on this bearing. NOT a circle: a cirque is gouged out by ice that came from one
        /// direction, and the wall it leaves is deep on one side and shallow on another.
        /// </summary>
        public double BasinAt(double bearingRad)
        {
            double q = Shape * 2.4 + 0.3;
            return BasinRadiusM * (1 + 0.12 * System.Math.Sin(2 * bearingRad + q)
                                     + 0.07 * System.Math.Sin(3 * bearingRad - 1.7 * q)
                                     + 0.04 * System.Math.Sin(5 * bearingRad + 0.6 * q));
        }

        /// <summary>The furthest the basin can reach on any bearing — what the no-two-basins-overlap test has
        /// to budget with, now that the radius is not one number.</summary>
        public double MaxBasinM => BasinRadiusM * 1.23;

        /// <summary>And the rim's height on this bearing: shoulders and cols, not a ring of one height.</summary>
        public double RimAt(double bearingRad)
        {
            double q = Shape * 1.9 + 0.7;
            // A walled cirque's lowest col still has to be a wall — the first irregular rim dropped Cirque's
            // to 34 m above its own water on one bearing, which is a way in, and Cirque is meant not to have
            // one. An open lake's rim may fall as low as it likes: it has a valley for that.
            double floorF = Bowled ? 0.55 : 0.22;
            return RimM * System.Math.Clamp(1 + 0.50 * System.Math.Sin(2 * bearingRad + q)
                                              + 0.28 * System.Math.Sin(3 * bearingRad + 2.3 * q)
                                              + 0.13 * System.Math.Sin(5 * bearingRad - q), floorF, 1.7);
        }

        public double BearingAt(double x, double y) => System.Math.Atan2(y - Y, x - X);

        /// <summary>How far round the basin the point is from the outlet, in degrees (180 = opposite it).
        /// 180 for a bowl, which has no outlet to be near.</summary>
        public double FromNotchDeg(double x, double y)
        {
            if (OutletHeadingDeg is null) return 180;
            double d = BearingAt(x, y) * 180 / System.Math.PI - OutletHeadingDeg.Value;
            while (d > 180) d -= 360;
            while (d < -180) d += 360;
            return System.Math.Abs(d);
        }

        /// <summary>Where the shoreline is along the outlet's bearing — the valley starts there, not at the
        /// lake's centre, or the bar across its mouth would be built out in the middle of the water.</summary>
        public double ShoreOnOutlet()
        {
            if (OutletHeadingDeg is null) return 0;
            double d = (OutletHeadingDeg.Value - HeadingDeg) * System.Math.PI / 180;
            double a = System.Math.Cos(d) / (LengthM * 0.5), b = System.Math.Sin(d) / (WidthM * 0.5);
            return 1.0 / System.Math.Sqrt(a * a + b * b);
        }

        /// <summary>Along and across the outlet valley, measured from the SHORE (along &lt; 0 is still lake).</summary>
        public (double along, double across) Outlet(double x, double y)
        {
            double hd = (OutletHeadingDeg ?? 0) * System.Math.PI / 180;
            double ux = System.Math.Cos(hd), uy = System.Math.Sin(hd);
            double dx = x - X, dy = y - Y;
            return (dx * ux + dy * uy - ShoreOnOutlet(), -dx * uy + dy * ux);
        }

        /// <summary>Half-width of the valley this far down it: narrow at the lip, opening as it falls.</summary>
        public double OutletHalfWidthAt(double along)
            => 135 + 330 * System.Math.Clamp(along / System.Math.Max(1.0, OutletRunM), 0, 1);
    }

    /// <summary>Half-width of the notch in a rim, in degrees — the gap you fly in through.</summary>
    public const double NotchHalfAngleDeg = 32.0;
    /// <summary>How far below the lake's surface the notch's floor falls, so the valley drains away and the
    /// approach is genuinely open rather than a lip to clear.</summary>
    public const double NotchDropM = 30.0;
    /// <summary>Depth of the water. Deep enough to float anything, shallow enough to see the bottom shelve.</summary>
    public const double DepthM = 14.0;
    /// <summary>The fraction of a peak's radius inside which its cone is re-asserted after the valleys have
    /// cut: the summit pyramid, which is everything above about four fifths of its height.</summary>
    public const double SummitFraction = 0.30;

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
        //                    name          x      y     len  wid  hdg  relief rim  basin  outlet  run  lip timber land shape
        // The outlet bearings and runs are not free: this range is crowded, and a valley that crosses
        // another basin's rim cuts a 600 m slot through it. Outlet's first one ran 1,500 m on 315° and took
        // the wall out of Cirque, two lakes away — which the bowled-lakes test caught by measuring Cirque's
        // lowest rim at 34 m above its own water. Every bearing here clears every peak by 400 m and every
        // other basin by its own radius plus the width of the valley, and a test holds it.
        // The three landable ones have TIGHT basins — about one and a quarter times the length of their own
        // water. A render with the basins three times the lake showed each one as a puddle in the middle of a
        // white bowl, which is not what an alpine lake looks like; the water should fill its floor. The three
        // bowled ones keep wide basins, because a bowl you can only spiral out of needs the turning room.
        // THREE YOU CAN LAND ON. 2,500 to 3,000 ft of water, low on the stair, with a valley to leave by —
        // and an obstacle at the mouth of it that gets worse as the water gets shorter. At 8,000 ft a Beaver
        // on floats wants most of 3,000 ft on a warm afternoon, so Larch's 2,500 ft with 85 ft of timber at
        // the end of it is a decision and not a formality.
        new Lake("Outlet",    3000,  -8900,  915, 320,  40,  300, 150,  720,  250,  1100,   0,   0, true,  0),
        new Lake("Shelf",     3200, -13500,  800, 300, 300,  380, 170,  640,  320,   900,   8,   0, true,  1),
        new Lake("Larch",     6600, -13600,  760, 290, 210,  360, 170,  620,  325,  1100,   4,  26, true,  2),
        // AND THREE YOU ONLY FLY INTO. Short, high and committing: the water is there to look at and to
        // frighten you, and the way out is the way you came.
        new Lake("Hidden",    6000,  -8900,  550, 240,  20,  600, 250, 1150,   30,   900,  24,   0, false, 3),
        new Lake("Cirque",    4500, -11300,  460, 200, 340,  660, 250, 1040, null,     0,   0,   0, false, 4),
        new Lake("Crown",     7600, -11000,  305, 170,  10,  740, 230,  990, null,     0,   0,  22, false, 5),
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
        // Nothing shorter than about 160 m. The range's mesh is sampled every 30 m, and the 37 m and 83 m
        // components this used to carry are below what that can resolve: they do not read as rock, they
        // alias, and every render of the range came back with its walls combed into vertical stripes like
        // corduroy. What is left is the long swell, which the mesh can actually show.
        return 42 * System.Math.Sin(x / 190.0) * System.Math.Sin(y / 230.0)
             + 18 * System.Math.Sin(x / 163.0 + 1.7) * System.Math.Sin(y / 177.0 + 0.6);
    }

    /// <summary>
    /// A basin's rim at (x, y). The ring is not a circle and the rim is not one height — both wander with the
    /// bearing, which is the whole difference between a cirque and a crater; a render of the first version
    /// showed six perfectly round craters with a puddle at the bottom of each.
    ///
    /// The rim GROWS out of whatever is around it. It used to be written as "the lake's surface plus the rim,
    /// wherever that rim is more than half a metre", and that half-metre threshold was a cliff: on a plateau
    /// 300 m below the water, the ground jumped 300 m the moment it tripped, and the basins came out ringed
    /// with vertical slabs like extruded cardboard.
    /// </summary>
    private static double RimAt(in Lake l, double x, double y, double h)
    {
        double d = System.Math.Sqrt((x - l.X) * (x - l.X) + (y - l.Y) * (y - l.Y));
        double bearing = l.BearingAt(x, y);
        double ringAt = l.BasinAt(bearing);
        const double band = 500.0;
        if (d > ringAt + band) return h;
        double near = 1 - System.Math.Abs(d - ringAt) / band;          // 1 on the ring, 0 a band away
        if (near <= 0) return h;
        double open = l.OutletHeadingDeg is null ? 0
                    : 1 - Smooth((l.FromNotchDeg(x, y) - NotchHalfAngleDeg * 0.55) / (NotchHalfAngleDeg * 0.9));
        double w = Smooth(near) * (1 - open);
        double top = l.SurfaceM + l.RimAt(bearing);
        return top > h ? h + (top - h) * w : h;
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
            // Smoothstep, not a power law: pow(t, 1.35) has slope 1.35 at the summit and puts a point on
            // every peak. A render of the range showed eight witch's hats above the cirques.
            double cone = baseH + (p.TopM - baseH) * Smooth(t);
            // Rock, not a tent: the roughness fades out at the summit and at the foot.
            cone += Ridge(x, y) * (0.35 + 0.65 * System.Math.Sin(System.Math.PI * System.Math.Clamp(t, 0, 1)));
            // And the couloirs (owner 2026-10-16): channels down the flanks with ribs between them. Granite
            // peaks over cirques are the most eroded thing in the world — it is all the mountain IS.
            cone -= Couloirs(p, x, y, t, baseH);
            h = System.Math.Max(h, cone);
        }

        // ---- the basin walls. A bowl's rim is built UP so it is there whatever the peaks do, and an open
        // lake's rim is left out across the notch so the valley can fall away.
        foreach (Lake l in Lakes) h = RimAt(l, x, y, h);

        // ---- the basins themselves, carved out of all of that: the water, the shore, and the walls above it.
        foreach (Lake l in Lakes)
        {
            double r = l.Inside(x, y);
            double d = System.Math.Sqrt((x - l.X) * (x - l.X) + (y - l.Y) * (y - l.Y));
            double bearing = l.BearingAt(x, y);
            double basin = l.BasinAt(bearing);
            if (d > basin) continue;
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
            double t = (d - dShore) / System.Math.Max(1.0, basin - dShore);
            // The wall rises FAST out of the water. A plain smoothstep left a flat apron most of the way to
            // the rim and the lake sat in the middle of it like a puddle on a table, which a render showed
            // plainly: raise it to 0.6 and the wall is two thirds up by half the radius, which is what a
            // cirque looks like and what makes the water fill its floor.
            double wall = System.Math.Pow(Smooth(t), 0.6);
            // Across the notch the ground keeps falling instead of rising: the outlet valley.
            double notch = l.OutletHeadingDeg is null ? 0
                         : 1 - Smooth((l.FromNotchDeg(x, y) - NotchHalfAngleDeg * 0.55) / (NotchHalfAngleDeg * 0.9));
            double top = l.SurfaceM + l.RimAt(bearing);
            // Gullied, not smooth: the same rock roughness the peaks get, strongest on the wall and fading
            // out at the water and at the rim.
            double target = shore + (top - shore) * wall + Ridge(x, y) * 0.55 * System.Math.Sin(System.Math.PI * System.Math.Clamp(wall, 0, 1));
            double drained = l.SurfaceM - NotchDropM - 120 * wall;
            h = System.Math.Min(h, target * (1 - notch) + drained * notch);
        }

        // ---- the outlet valleys, and the bar across the mouth of each.
        foreach (Lake l in Lakes) h = OutletValleyAt(l, x, y, h);

        // ---- the basins win back their rims. A valley may run past another basin — this range is crowded
        // and there is nowhere else to put them — but it may not take the wall out of one. Outlet's first
        // valley cut a 640 m slot through Cirque's rim two lakes away.
        foreach (Lake l in Lakes)
        {
            if (!l.Bowled && l.Inside(x, y) < 1.2) continue;    // not over its own outlet's shoulder
            h = RimAt(l, x, y, h);
        }

        // ---- and then the mountains win back their summits. A valley may cut a trough through a flank —
        // that is exactly what a glacial trough does — but it may not take the top off anything, and in a
        // range this crowded a valley's shoulder reaches a summit: Outlet's took 780 m off Sentinel from a
        // centreline 667 m away. Everything above about four fifths of a peak's height is inviolate; below
        // that the valleys may cut as deep as they like.
        foreach (Peak p in Peaks)
        {
            double d = System.Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            if (d >= p.RadiusM * SummitFraction) continue;
            double t = 1 - d / p.RadiusM;
            // The SAME cut as the first pass, couloirs and all: re-asserting the plain cone here would fill
            // the channels back in inside this radius and leave a ring where they stopped.
            h = System.Math.Max(h, baseH + (p.TopM - baseH) * Smooth(t) - Couloirs(p, x, y, t, baseH));
        }
        return h;
    }

    /// <summary>The couloirs cut into one peak's flank. Both passes over the peaks have to use this, or the
    /// summit re-assertion draws a ring round the mountain at <see cref="SummitFraction"/>.</summary>
    private static double Couloirs(in Peak p, double x, double y, double t, double baseH)
    {
        double bearing = System.Math.Atan2(y - p.Y, x - p.X);
        int channels = 10 + (int)(7 * WorldTerrain.Hash(p.X * 0.011 + p.Y * 0.019, 6.7));
        double depth = System.Math.Clamp(0.05 * (p.TopM - baseH), 22, 120);
        return WorldTerrain.ConeErosion(bearing, t, p.X * 0.0013 + p.Y * 0.0009, channels, depth, depth * 0.25);
    }

    /// <summary>
    /// The valley a lake drains by, and the bar across its mouth. The valley is a trough running away from
    /// the shore on <see cref="Lake.OutletHeadingDeg"/>, opening out and falling as it goes, so there is a
    /// line to fly in on and a line to climb out along instead of a notch in a wall. The bar is the rock a
    /// glacier leaves at the lip of its basin — a riegel — and it is the obstacle: <see cref="Lake.LipM"/>
    /// of rock above the water, with <see cref="Lake.TimberM"/> of trees on top of that.
    ///
    /// Carve first, raise the bar second. The other order puts the valley's own floor through the bar.
    /// </summary>
    public static double OutletValleyAt(in Lake l, double x, double y, double h)
    {
        if (l.OutletHeadingDeg is null) return h;
        // Never over the water. The valley is wide, and a point well out to the side of it at a small `along`
        // can still be inside the lake — which is how the first version built an 8 m bar of rock through the
        // middle of Shelf and left it with its bed above its own surface.
        // 0.98, not 1.0: at exactly the shoreline the floating point falls either way, and where it fell
        // inside, the valley's floor was not laid and the probe found a single sample of bare plateau 380 m
        // below the water at the lake's own edge.
        if (l.Inside(x, y) < 0.98) return h;
        var (along, across) = l.Outlet(x, y);
        if (along < 0 || along > l.OutletRunM + 260) return h;
        double halfW = l.OutletHalfWidthAt(along);
        // How far the valley reaches sideways has to be budgeted against the peaks and the other basins, and
        // it is bigger than it looks: halfW + edge + fade. The first version reached 965 m and took 780 m off
        // Sentinel's summit from a valley whose centreline passed 667 m away.
        // 420 m of flank: at the lake's end the valley floor is 300 m above the plateau it is heading
        // for, and that has to be given somewhere to go or it is a cliff.
        double edge = halfW + 420, fade = 250;
        if (System.Math.Abs(across) > edge + fade) return h;

        // The floor, from the water's own level down to the plateau. How far it gets to fall is set by how
        // far it gets to run: a short valley given a long drop is a chute, not a valley.
        double t = Smooth(System.Math.Clamp(along / l.OutletRunM, 0, 1));
        double drop = System.Math.Min(280, 0.19 * l.OutletRunM);
        double floor = l.SurfaceM + (System.Math.Max(BaseM, l.SurfaceM - drop) - l.SurfaceM) * t;

        // The valley's WALLS. Leaving these out was the first version's mistake, and it was a bad one: the
        // basin's rim is suppressed across the outlet (that is what makes it an outlet), and a carve-only
        // valley therefore left nothing at all between the lake and the plateau — a 300 to 600 m vertical
        // drop at the shoreline, with the lake sitting on a plinth. The probe that found it printed the
        // ground falling from 2,734 m to 2,134 in a hundred metres. They stand as high as the basin's rim
        // where they leave it and die away to nothing by the far end, which is what a valley does.
        double side = Smooth(System.Math.Clamp((System.Math.Abs(across) - halfW) / 420.0, 0, 1));
        // The flanks run from the floor to WHATEVER IS THERE — up to a mountainside, down to the plateau.
        // Two earlier versions got this wrong in opposite directions: standing them at the basin's own rim
        // height put a 450 m slab of rock along both sides of every valley, and clamping them at the floor
        // left a flat shelf of ground at the lake's own level with a 300 m drop off the edge of it. A valley
        // has neither. It just joins what is around it.
        double prof = floor + (h - floor) * side;
        // Inside the valley the ground IS this profile — filled where the ground was lower, cut where it was
        // higher — and over the last 200 m it gives way to whatever was there before.
        double blend = Smooth(System.Math.Clamp((System.Math.Abs(across) - edge) / fade, 0, 1));
        h = prof + (h - prof) * blend;

        // The bar across the mouth: the rock a glacier leaves at the lip of its basin, and the obstacle you
        // are committing to clearing when you put the aeroplane on the water. Crested about 80 m out.
        if (l.LipM > 0 && along < 220 && System.Math.Abs(across) < halfW + 60)
        {
            double bump = System.Math.Sin(System.Math.PI * System.Math.Clamp(along / 220.0, 0, 1));
            double narrow = 1 - Smooth(System.Math.Clamp((System.Math.Abs(across) - halfW * 0.8) / (halfW * 0.4), 0, 1));
            h = System.Math.Max(h, l.SurfaceM + l.LipM * bump * narrow);
        }
        return h;
    }

    // ---- the timber ------------------------------------------------------------------------------
    //
    // Owner 2026-10-16: an obstacle at the exit, "a lip of say 20–100 ft or tall trees". Rock is one answer
    // and a stand of mountain hemlock is the other, and the second is the harder one to read from the air:
    // a bar of rock throws a shadow and a line of timber at the end of a lake looks like the shore until you
    // are committed to it. Larch has 85 ft of it across the mouth of its valley; Crown, which has no valley
    // at all, is ringed with it.

    /// <summary>A conifer. The trunk is a solid — this is not scenery you can mow.</summary>
    public readonly struct Tree
    {
        public readonly double X, Y, HeightM, RadiusM, LeanDeg;
        public Tree(double x, double y, double heightM, double radiusM, double leanDeg)
        { X = x; Y = y; HeightM = heightM; RadiusM = radiusM; LeanDeg = leanDeg; }
    }

    /// <summary>How far outside the trees' trunks an airframe is still in the branches.</summary>
    public const double TreeCrownRatio = 0.34;

    private static System.Collections.Generic.List<Tree>? _trees;
    public static System.Collections.Generic.List<Tree> Trees() => _trees ??= BuildTrees();

    private static System.Collections.Generic.List<Tree> BuildTrees()
    {
        var list = new System.Collections.Generic.List<Tree>();
        foreach (Lake l in Lakes)
        {
            if (l.TimberM <= 0) continue;
            int n = l.Bowled ? 150 : 110;
            for (int i = 0; i < n; i++)
            {
                double x, y;
                if (l.Bowled)
                {
                    // Round the shore, in the band between the water and the foot of the wall.
                    double a = (i + WorldTerrain.Hash(i, l.Shape + 1.3)) / n * 2 * System.Math.PI;
                    double basin = l.BasinAt(a);
                    double shore = l.LengthM * 0.5 * 0.9;
                    double d = shore + (basin * 0.55 - shore) * WorldTerrain.Hash(i, l.Shape + 4.1);
                    x = l.X + System.Math.Cos(a) * d; y = l.Y + System.Math.Sin(a) * d;
                }
                else
                {
                    // On the bar across the outlet and the first of the valley below it: a wall of timber
                    // exactly where the climb-out has to go.
                    double hd = l.OutletHeadingDeg!.Value * System.Math.PI / 180;
                    double along = 10 + 240 * WorldTerrain.Hash(i, l.Shape + 2.7);
                    double halfW = l.OutletHalfWidthAt(along);
                    double across = (WorldTerrain.Hash(i, l.Shape + 5.9) * 2 - 1) * (halfW + 40);
                    x = l.X + (l.ShoreOnOutlet() + along) * System.Math.Cos(hd) - across * System.Math.Sin(hd);
                    y = l.Y + (l.ShoreOnOutlet() + along) * System.Math.Sin(hd) + across * System.Math.Cos(hd);
                }
                if (l.Inside(x, y) < 1.02) continue;              // never standing in the water
                double tall = l.TimberM * (0.72 + 0.45 * WorldTerrain.Hash(i, l.Shape + 7.3));
                list.Add(new Tree(x, y, tall, 1.6 + 1.5 * WorldTerrain.Hash(i, l.Shape + 9.1),
                                  (WorldTerrain.Hash(i, l.Shape + 11.7) * 2 - 1) * 6));
            }
        }
        return list;
    }

    /// <summary>Register the trunks as solids (call after the landmarks reset the list).</summary>
    public static void RegisterSolids()
    {
        if (WorldTerrain.Active is null) return;
        foreach (Tree t in Trees())
        {
            double g = WorldTerrain.Active.HeightAt(t.X, t.Y);
            WorldSolids.Boxes.Add(new WorldSolids.Box(t.X, t.Y, t.RadiusM, t.RadiusM, g, g + t.HeightM));
        }
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

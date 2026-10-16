namespace FlyingGame.Core;

/// <summary>
/// The high desert (owner 2026-10-16: "a Reno-themed mountainous area with landable hilltops, hazards that
/// are random but the same every time, guaranteed clear places to land, and a few visible strips with tyre
/// tracks and rock piles marking the thresholds a hundred feet apart at each end").
///
/// WHY IT GOES ON THE MESA. The plateau's floor is 4,000 ft, and the country round Reno runs 4,400 ft in the
/// valleys to 8,300 ft on Peavine — so the plateau's own height IS the valley floor, and the hills on top of
/// it land in the right band without any fudging. The block sits west of the Alaska range, which the probe
/// says is empty plateau from x −2,000 all the way out.
///
/// WHAT MAKES IT DIFFERENT FROM THE BACKCOUNTRY. The Idaho strips (<see cref="FrankChurch"/>) are down in a
/// canyon: the hazard is the terrain around you, and the strips are cut benches you are committed to. Here
/// the hazard is the ground ITSELF — a mesa top looks flat from a thousand feet and is covered in sagebrush
/// and rock — and you are never committed, because the tops are big enough to go round again. What you have
/// to learn is to READ a surface from the air, which is the whole skill of landing somewhere nobody made.
///
/// THE THREE RULES, each measured by a test rather than eyeballed:
///
///  1. EVERY LANDABLE TOP HAS A CLEAN LANE. The scatter is deterministic — the same rock in the same place
///     every time, so a pilot can learn a hilltop — and a lane <see cref="CleanLaneHalfWidthM"/> either side
///     of each top's centreline is left utterly empty for its whole usable length.
///  2. A LANDABLE TOP IS ACTUALLY FLAT. Its surface is a plane, sloping no more than <see cref="MaxTopSlopeDeg"/>
///     along the lane and dead level across it. The rock texture that makes the hillsides look like hillsides
///     is faded out before it reaches the top.
///  3. THE MARKED STRIPS ARE UNMISTAKABLE. Four rock piles each, two at every threshold,
///     <see cref="ThresholdPileSpacingM"/> (100 ft) apart across the strip, with wheel ruts worn down the
///     middle between them — so from the air you see where it starts and which way it runs.
/// </summary>
public static class HighDesert
{
    /// <summary>
    /// The block, on the Mesa plateau west of the Alaska range (which starts at x 2,600). Its WEST edge is
    /// set by the world mesh, not by the height field: the terrain is only built out to x −7,000, so a block
    /// laid further west than that is a range of hills with no ground under it — the first layout ran out to
    /// x −13,000 and its playa and scrub hung in mid-air over the edge of the world. Growing the mesh west
    /// instead would have cost a fifth of the whole world's base terrain for one region, so the desert is
    /// compact: 4.9 km by 2.9 km. Which is also what the owner has asked for before — "tighten it up, it
    /// takes a long time to get from feature to feature" — and it puts the place 2 to 7 km from the Mesa
    /// airport rather than thirteen.
    /// </summary>
    /// Its EAST edge is set by the city. Every plateau carries a copy of the whole Valley landscape — airport,
    /// city, tower, arch, lake, race course — and the Mesa copy's city stands at x −2,800 to −250. The first
    /// layout ran east to x −2,000 and put a mesa under it: a render showed tower blocks standing up the side
    /// of Silver Knoll. A height probe will never find that, because a city does not move the ground.
    public const double X0 = -6900, X1 = -3600, Y0 = -7450, Y1 = -4550;
    public static double BaseM => WorldTerrain.DatumM + 2 * WorldTerrain.StepHeightM;   // 4,000 ft

    /// <summary>The steepest a landable top is allowed to be, along its lane. Four degrees is 7 %: steep
    /// enough that it matters which way you land, gentle enough that it is a choice and not a trap.</summary>
    public const double MaxTopSlopeDeg = 4.0;
    /// <summary>Half the width of the lane kept clear of hazards down every landable top.</summary>
    public const double CleanLaneHalfWidthM = 23;
    /// <summary>Rock piles at a threshold, 100 ft apart across the strip — the owner's figure.</summary>
    public const double ThresholdPileSpacingM = 30.48;
    /// <summary>Wheel ruts worn into a used strip: a light aeroplane's main gear track.</summary>
    public const double RutGaugeM = 2.4;
    /// <summary>How steeply a mesa's sides fall away from its top. 36° is a talus slope that has stood for a
    /// while — and it is what makes a hilltop committing to leave as well as to arrive at.</summary>
    public const double SkirtSlopeDeg = 36.0;

    public enum Scrub { Sagebrush, Rock, Juniper }

    public readonly struct Hazard
    {
        public readonly Scrub Kind;
        public readonly double X, Y, RadiusM, HeightM, HeadingDeg;
        public Hazard(Scrub kind, double x, double y, double radiusM, double heightM, double headingDeg)
        { Kind = kind; X = x; Y = y; RadiusM = radiusM; HeightM = heightM; HeadingDeg = headingDeg; }
        /// <summary>A sage bush you can mow; a rock or a juniper trunk you cannot.</summary>
        public bool Solid => Kind != Scrub.Sagebrush;
    }

    /// <summary>
    /// A hill. Either a peak — scenery, and a thing to fly round — or a MESA with a flat top you can land
    /// on: <see cref="TopLengthM"/> by <see cref="TopWidthM"/>, running along <see cref="TopHeadingDeg"/>
    /// and falling at <see cref="TopSlopeDeg"/> in that direction.
    /// </summary>
    public readonly struct Hill
    {
        public readonly string Name;
        public readonly double X, Y, TopM, RadiusM;
        public readonly bool Landable;
        public readonly double TopLengthM, TopWidthM, TopHeadingDeg, TopSlopeDeg;

        public Hill(string name, double x, double y, double topM, double radiusM, bool landable,
                    double topLengthM, double topWidthM, double topHeadingDeg, double topSlopeDeg)
        {
            Name = name; X = x; Y = y; TopM = topM; RadiusM = radiusM; Landable = landable;
            TopLengthM = topLengthM; TopWidthM = topWidthM; TopHeadingDeg = topHeadingDeg; TopSlopeDeg = topSlopeDeg;
        }

        public double TopFt => TopM * 3.28084;
        public double TopLengthFt => TopLengthM * 3.28084;
        /// <summary>How far the top stands above the plateau floor.</summary>
        public double ReliefM => TopM - BaseM;
        /// <summary>Uphill is the way to land: <see cref="TopHeadingDeg"/> runs DOWN the slope.</summary>
        public double LandingHeadingDeg => (TopHeadingDeg + 180) % 360;

        public (double along, double across) Local(double x, double y)
        {
            double h = TopHeadingDeg * System.Math.PI / 180;
            double dx = x - X, dy = y - Y;
            return (dx * System.Math.Cos(h) + dy * System.Math.Sin(h),
                    -dx * System.Math.Sin(h) + dy * System.Math.Cos(h));
        }

        /// <summary>Distance outside the top's rectangle (0 anywhere on the top itself).</summary>
        public double OutsideTopM(double x, double y)
        {
            if (!Landable) return double.PositiveInfinity;
            var (a, c) = Local(x, y);
            double oa = System.Math.Max(0, System.Math.Abs(a) - TopLengthM * 0.5);
            double oc = System.Math.Max(0, System.Math.Abs(c) - TopWidthM * 0.5);
            return System.Math.Sqrt(oa * oa + oc * oc);
        }

        public bool OnTop(double x, double y, double pad = 0) => OutsideTopM(x, y) <= pad;

        /// <summary>The lane down the middle of the top that is kept clear of everything.</summary>
        public bool InCleanLane(double x, double y)
        {
            if (!Landable) return false;
            var (a, c) = Local(x, y);
            return System.Math.Abs(a) <= TopLengthM * 0.5 && System.Math.Abs(c) <= CleanLaneHalfWidthM;
        }

        /// <summary>The top's surface: a plane, falling along the heading, dead level across.</summary>
        public double TopSurfaceAt(double x, double y)
        {
            var (a, _) = Local(x, y);
            return TopM - a * System.Math.Tan(TopSlopeDeg * System.Math.PI / 180);
        }
    }

    /// <summary>
    /// The hills. Named in the Nevada idiom, laid out along the block with their skirts clear of each other —
    /// the Cascades taught that two bowls a kilometre apart with 1.3 km radii carve each other's rims, and a
    /// test here checks every pair.
    /// </summary>
    public static readonly Hill[] Hills =
    {
        //           name             x       y     topM  radius land  topLen topWid  hdg  slope
        // A peak is a cone and IS cut off at its radius, so it has to sit wholly inside the block.
        new Hill("Peavine",        -6000, -6100, 2130,   880, false,     0,     0,    0,  0),
        // It sat east of the Knoll at first, and its cone stood 113 m above the ground 120 m off that mesa's
        // low end: a hill in the overrun of the strip you have just failed to stop on.
        new Hill("Spanish Butte",  -4300, -5600, 1890,   700, false,     0,     0,    0,  0),
        // The mesas. A top is long enough to go round again on and short enough to concentrate the mind:
        // 1,600 to 2,900 ft of usable length, which is a backcountry strip's length with nothing at the ends.
        // They run roughly along x, which is the way the block is long and the way the runways run. The
        // relief is 650 to 1,500 ft — enough that the top is committing, little enough that the 36° skirt
        // that falls off it still fits inside the block. There were four; the fourth had to go when the city
        // pushed the block's east edge back, and two mesas 360 m apart carve each other's tops anyway.
        new Hill("Red Rock Mesa",  -5600, -5280, 1646,     0, true,    700,   230,  178, 2.6),
        new Hill("Sand Pass Mesa", -4700, -6600, 1480,     0, true,    880,   300,    6, 1.1),
        // The third one is LOW on purpose. There is no room beside Red Rock for another full mesa — two of
        // them 600 m apart bury each other's tops in talus, whatever height they are given — but there IS
        // room on the north edge for a 400 ft bench, and a low one is a different kind of flying anyway:
        // gentle enough to be the one you try first, and the only top here you could go round again on
        // below the rim.
        new Hill("Hungry Ridge",   -4150, -4810, 1340,     0, true,    500,   180,    0, 1.8),
    };

    /// <summary>
    /// The playa: a dry lake on the valley floor. In Nevada this is the one surface you can trust — a mile of
    /// cracked clay, flat as a table, and the place everybody goes when they want somewhere to land that is
    /// not a decision. It is kept entirely clear of hazards.
    /// </summary>
    public const double PlayaX = -6100, PlayaY = -7250, PlayaRx = 480, PlayaRy = 150;
    public static double PlayaSurfaceM => BaseM + 3.0;

    public static bool OnPlaya(double x, double y, double pad = 0)
    {
        double dx = (x - PlayaX) / (PlayaRx + pad), dy = (y - PlayaY) / (PlayaRy + pad);
        return dx * dx + dy * dy <= 1.0;
    }

    /// <summary>
    /// A strip somebody made and keeps using: graded, with wheel ruts worn down it and a pile of rocks at
    /// each corner of each threshold. <see cref="OnHill"/> names the mesa it is on, or null for the flats.
    /// </summary>
    public readonly struct DesertStrip
    {
        public readonly string Name, OnHill;
        public readonly double X, Y, HeadingDeg, LengthM, WidthM;
        public DesertStrip(string name, string onHill, double x, double y, double headingDeg, double lengthM, double widthM)
        { Name = name; OnHill = onHill; X = x; Y = y; HeadingDeg = headingDeg; LengthM = lengthM; WidthM = widthM; }

        public double LengthFt => LengthM * 3.28084;
        public double WidthFt => WidthM * 3.28084;

        public (double along, double across) Local(double x, double y)
        {
            double h = HeadingDeg * System.Math.PI / 180;
            double dx = x - X, dy = y - Y;
            return (dx * System.Math.Cos(h) + dy * System.Math.Sin(h),
                    -dx * System.Math.Sin(h) + dy * System.Math.Cos(h));
        }

        public double OutsideM(double x, double y)
        {
            var (a, c) = Local(x, y);
            double oa = System.Math.Max(0, System.Math.Abs(a) - LengthM * 0.5);
            double oc = System.Math.Max(0, System.Math.Abs(c) - WidthM * 0.5);
            return System.Math.Sqrt(oa * oa + oc * oc);
        }

        public bool On(double x, double y, double pad = 0) => OutsideM(x, y) <= pad;

        /// <summary>A point on the strip, from a fraction of its length and metres across the centreline.</summary>
        public (double x, double y) At(double alongFrac, double acrossM)
        {
            double h = HeadingDeg * System.Math.PI / 180;
            double a = alongFrac * LengthM;
            return (X + System.Math.Cos(h) * a - System.Math.Sin(h) * acrossM,
                    Y + System.Math.Sin(h) * a + System.Math.Cos(h) * acrossM);
        }
    }

    /// <summary>
    /// The made strips. Two up on mesas and two on the flats, so the place teaches both: a hilltop you commit
    /// to the moment you are below the rim, and a flat strip you can walk away from at any point.
    /// </summary>
    public static readonly DesertStrip[] Strips =
    {
        //                  name             on hill            x      y    hdg   len   wid
        new DesertStrip("Red Rock Strip",   "Red Rock Mesa",  -5600, -5280, 178,  620,  30.5),
        new DesertStrip("Sand Pass Strip",  "Sand Pass Mesa", -4700, -6600,   6,  800,  30.5),
        new DesertStrip("Playa Strip",       null,            -6100, -7250,   0,  760,  30.5),
        new DesertStrip("Hungry Flat",      "Hungry Ridge",   -4150, -4810,   0,  440,  30.5),
    };

    /// <summary>
    /// The rock piles: two at each threshold, 100 ft apart across the strip, so the threshold reads as a GATE
    /// from the air and you can see which way the strip runs before you are committed to it.
    /// </summary>
    public static System.Collections.Generic.IEnumerable<(double x, double y, double radiusM, double heightM)>
        ThresholdPiles(DesertStrip s)
    {
        foreach (int end in new[] { -1, 1 })
            foreach (int side in new[] { -1, 1 })
            {
                var (px, py) = s.At(end * 0.5, side * ThresholdPileSpacingM * 0.5);
                yield return (px, py, 1.5, 0.9);
            }
    }

    private static bool InBlock(double x, double y)
        => x > X0 - 600 && x < X1 + 600 && y > Y0 - 600 && y < Y1 + 600;

    public static bool InArea(double x, double y) => InBlock(x, y);

    private static double Smooth(double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Rock and gully texture on the hillsides. Faded out before it reaches a landable top — a mesa
    /// top that ripples is not a mesa top.</summary>
    private static double Rough(double x, double y)
        => 17 * System.Math.Sin(x / 185.0) * System.Math.Sin(y / 214.0)
         + 7 * System.Math.Sin(x / 71.0 + 1.1) * System.Math.Sin(y / 83.0 + 0.6);

    /// <summary>How far outside the block the desert fades to nothing. Without this the floor's own rolling
    /// (a dozen metres of it) ran straight on across the plateau, and the block's edge test found it 900 m
    /// out — the same leak the braided plain had, in the other direction.</summary>
    public const double EdgeFadeM = 500;

    /// <summary>1 anywhere in the block, falling to 0 <see cref="EdgeFadeM"/> outside it.</summary>
    public static double EdgeFade(double x, double y)
    {
        double ex = System.Math.Min(x - (X0 - EdgeFadeM), (X1 + EdgeFadeM) - x);
        double ey = System.Math.Min(y - (Y0 - EdgeFadeM), (Y1 + EdgeFadeM) - y);
        return Smooth(System.Math.Min(ex, ey) / EdgeFadeM);
    }

    /// <summary>The desert floor: not a table — low rolling sage country with dry washes in it.</summary>
    public static double FloorHeightAt(double x, double y)
        => BaseM + EdgeFade(x, y) * (9 * System.Math.Sin(x / 620.0 + 0.4) * System.Math.Sin(y / 540.0)
                                   + 3 * System.Math.Sin(x / 210.0 + 2.1) * System.Math.Sin(y / 178.0 + 1.3));

    /// <summary>The hills, their flat tops, and the playa.</summary>
    public static double HeightAt(double x, double y, double baseH)
    {
        if (!InBlock(x, y)) return baseH;
        double h = FloorHeightAt(x, y);
        double skirtTan = System.Math.Tan(SkirtSlopeDeg * System.Math.PI / 180);

        // THE PLAYA FIRST, because a dry lake is part of the valley FLOOR and the hills stand on the floor.
        // Applied after them it flattened whatever it reached, and its blend margin took 371 m out of the
        // corner of Red Rock Mesa's top — a dry lake carving a hole in a hilltop 400 ft above it.
        double px = (x - PlayaX) / PlayaRx, py = (y - PlayaY) / PlayaRy;
        double pr = System.Math.Sqrt(px * px + py * py);
        if (pr < 1.5)
        {
            double surf = PlayaSurfaceM;
            h = pr <= 1 ? surf : surf + (h - surf) * Smooth((pr - 1) / 0.5);
        }

        foreach (Hill hl in Hills)
        {
            if (!hl.Landable)
            {
                // A peak: a cone with rock on it, roughest halfway up and smooth at the very top.
                double dx = x - hl.X, dy = y - hl.Y;
                double d = System.Math.Sqrt(dx * dx + dy * dy);
                if (d >= hl.RadiusM) continue;
                // ROUNDED, not conical. t^1.7 has a slope of 1.7 at the summit — a point — and the first
                // version of these read as witch's hats standing in the desert. Smoothstep is flat at both
                // ends: a rounded top and a foot that feathers into the floor, which is what a desert
                // mountain looks like, with the rock texture doing the rest.
                double t = 1 - d / hl.RadiusM;
                double cone = BaseM + (hl.TopM - BaseM) * Smooth(t);
                cone += Rough(x, y) * System.Math.Sin(System.Math.PI * t);
                h = System.Math.Max(h, cone);
                continue;
            }

            // A mesa: the flat top, and a talus skirt falling away from its edge at SkirtSlopeDeg. The skirt
            // LIMITS ITSELF — it simply runs out below the floor — so a mesa has no radius to be cut off at.
            // (It did at first, and the cut was a vertical cliff ringing every mesa at its radius, because the
            // skirt was still 200 m above the desert when the radius check stopped it.)
            double outside = hl.OutsideTopM(x, y);
            double top = hl.TopSurfaceAt(x, y);
            double mesa = top - outside * skirtTan;
            if (mesa <= h) continue;
            // The top is the LAST word on its own ground: the texture that makes a hillside look like a
            // hillside is faded in with distance outside the top, so the landable surface is a plane.
            if (outside > 0) mesa += Rough(x, y) * 0.55 * Smooth(outside / 160.0);
            h = System.Math.Max(h, mesa);
        }

        // The made strips are graded, and that grading is the last word of all — level across, following the
        // mesa's own slope along. (The braided plain taught this: apply it before the floor and the ground
        // texture ripples straight through a strip that is supposed to have been bladed.)
        foreach (DesertStrip s in Strips)
        {
            double outside = s.OutsideM(x, y);
            if (outside > 110) continue;
            double graded = StripSurfaceAt(s, x, y);
            h = outside <= 20 ? graded : graded + (h - graded) * Smooth((outside - 20) / 90.0);
        }
        return h;
    }

    /// <summary>The graded surface of a made strip — on a mesa it is that mesa's own plane, so the strip does
    /// not stand proud of the top it was bladed out of.</summary>
    public static double StripSurfaceAt(DesertStrip s, double x, double y)
    {
        foreach (Hill hl in Hills)
            if (hl.Landable && hl.Name == s.OnHill) return hl.TopSurfaceAt(x, y);
        if (OnPlaya(s.X, s.Y)) return PlayaSurfaceM;
        return FloorHeightAt(s.X, s.Y);
    }

    /// <summary>
    /// Gravel everywhere a wheel wants it — the made strips, the mesa tops and the playa — and rough sage
    /// country in between. Only the block ITSELF is claimed, never the margin the height field blends over:
    /// claiming the margin is how the braided plain turned ground three hundred metres outside it to gravel.
    /// </summary>
    public static WorldTerrain.Surface? SurfaceAt(double x, double y)
    {
        if (x < X0 || x > X1 || y < Y0 || y > Y1) return null;
        foreach (DesertStrip s in Strips) if (s.On(x, y, 8)) return WorldTerrain.Surface.Gravel;
        if (OnPlaya(x, y)) return WorldTerrain.Surface.Gravel;
        foreach (Hill hl in Hills) if (hl.OnTop(x, y)) return WorldTerrain.Surface.Gravel;
        return WorldTerrain.Surface.Rough;
    }

    // ---- the hazards ---------------------------------------------------------------------------------

    /// <summary>
    /// Sagebrush, rock and juniper, scattered over the whole block — the same scatter every time, because a
    /// hazard you cannot learn is a dice roll and this place is supposed to be learnable. Nothing is ever
    /// placed in a landable top's clean lane, on a made strip, or on the playa.
    /// </summary>
    /// <summary>
    /// A deterministic hash in [0, 1). It has to be a FLAT one: |sin(n)| on its own, which is what the
    /// braided plain uses, piles up near 1 (it is an arcsine distribution), and the first scatter here came
    /// out with more 10 ft junipers than sagebrush because of it.
    /// </summary>
    public static double Hash(int n, double salt)
    {
        double v = System.Math.Sin(n * 12.9898 + salt * 78.233) * 43758.5453;
        return v - System.Math.Floor(v);
    }

    public static System.Collections.Generic.List<Hazard> Hazards()
    {
        var list = new System.Collections.Generic.List<Hazard>();
        int n = 0;
        for (double x = X0 + 90; x < X1; x += 165)
        for (double y = Y0 + 90; y < Y1; y += 165)
        {
            n++;
            double jx = 124 * (Hash(n, 0.3) - 0.5);
            double jy = 124 * (Hash(n, 2.9) - 0.5);
            double hx = x + jx, hy = y + jy;

            bool blocked = OnPlaya(hx, hy, 40);
            if (!blocked) foreach (DesertStrip s in Strips) if (s.On(hx, hy, 45)) { blocked = true; break; }
            if (!blocked) foreach (Hill hl in Hills) if (hl.InCleanLane(hx, hy)) { blocked = true; break; }
            // Nothing clinging to a talus slope either: it would be scenery you could never reach anyway,
            // and a rock halfway down a 36° skirt is not a hazard, it is a cliff.
            if (!blocked && OnSkirt(hx, hy)) blocked = true;
            if (blocked) continue;

            double pick = Hash(n, 1.7);
            if (pick < 0.22) continue;                              // bare ground between the bushes
            // Sage is most of the high desert, rock is the thing that will have your gear, and a juniper is
            // a tree: rare, and the only one of the three you would see from any height.
            double which = Hash(n, 5.3);
            Scrub kind = which < 0.62 ? Scrub.Sagebrush : which < 0.90 ? Scrub.Rock : Scrub.Juniper;
            double size = Hash(n, 9.1);
            double radius = kind switch
            {
                Scrub.Sagebrush => 0.7 + 0.8 * size,
                Scrub.Rock => 0.8 + 1.7 * size,
                _ => 1.4 + 1.1 * size,
            };
            double height = kind switch
            {
                Scrub.Sagebrush => 0.8,
                Scrub.Rock => 0.9,
                _ => 3.2,
            };
            list.Add(new Hazard(kind, hx, hy, radius, height, (n * 37) % 180));
        }
        return list;
    }

    /// <summary>On the steep side of a mesa, where nothing is scattered and nothing can be landed.</summary>
    public static bool OnSkirt(double x, double y)
    {
        foreach (Hill hl in Hills)
        {
            if (!hl.Landable) continue;
            double dx = x - hl.X, dy = y - hl.Y;
            if (dx * dx + dy * dy >= hl.RadiusM * hl.RadiusM) continue;
            double outside = hl.OutsideTopM(x, y);
            if (outside > 0 && outside * System.Math.Tan(SkirtSlopeDeg * System.Math.PI / 180) < hl.ReliefM)
                return true;
        }
        return false;
    }

    /// <summary>The rocks and the junipers are solid; a sage bush is something you mow. The threshold piles
    /// are solid too — they are a pile of rocks, and that is rather the point of putting them there.</summary>
    public static void RegisterSolids()
    {
        foreach (Hazard hz in Hazards())
        {
            if (!hz.Solid) continue;
            double gh = GroundForSolidAt(hz.X, hz.Y);
            WorldSolids.Boxes.Add(new WorldSolids.Box(hz.X, hz.Y, hz.RadiusM, hz.RadiusM,
                                                      gh - 1.0, gh + hz.HeightM));
        }
        foreach (DesertStrip s in Strips)
            foreach (var (px, py, radius, height) in ThresholdPiles(s))
            {
                double gh = StripSurfaceAt(s, px, py);
                WorldSolids.Boxes.Add(new WorldSolids.Box(px, py, radius, radius, gh - 1.0, gh + height));
            }
    }

    /// <summary>The ground a scattered thing stands on — the mesa top it is up on, or the desert floor.</summary>
    public static double GroundForSolidAt(double x, double y)
    {
        foreach (Hill hl in Hills) if (hl.Landable && hl.OnTop(x, y)) return hl.TopSurfaceAt(x, y);
        if (OnPlaya(x, y)) return PlayaSurfaceM;
        return FloorHeightAt(x, y);
    }
}

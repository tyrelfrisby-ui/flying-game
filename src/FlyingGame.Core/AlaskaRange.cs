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

        /// <summary>
        /// The ice surface at a point on the glacier: an icefall at the top, a shallow tongue below it, and a
        /// slight crown across.
        ///
        /// This used to be (1−s)³ of the whole drop, with a comment saying it put "nearly all of the drop in
        /// the icefall at the top and then a long shallow tongue — which is why people land on the tongue".
        /// It did not. A cubic has no tongue: its gradient falls off smoothly the whole way down, so the Knik
        /// was still at 8.7° where the landing zone is and 27.5° at the top of it — 152 m of fall across the
        /// 800 m the owner asked to be graded to 3°. Grading THAT as a bench means a 110 m cut, because the
        /// LZ's lower end is 67 m from the ice front and the fill has nowhere to go but the lake.
        ///
        /// So the profile now says what the comment always claimed. Below <see cref="TongueStartT"/> the ice
        /// falls at exactly <see cref="TongueDeg"/>; above it the icefall carries everything that is left, at
        /// a constant gradient with a short fillet where the two meet. The head, the terminus, the length and
        /// the total drop are all unchanged, and the icefall comes out at 52° against the 51° the cubic
        /// already reached at the head — so nothing on this glacier is more than a degree steeper than it
        /// was, while the whole lower 70% is landable instead of just the last few hundred metres.
        /// </summary>
        public double IceHeightAt(double t, double across)
        {
            double s = System.Math.Clamp(t, 0, 1);
            double h = TermM + DropAboveTerminus(s);
            double u = System.Math.Clamp(across / HalfWidthM, 0, 1);
            return h + 14 * (1 - u * u);                    // crowned in the middle, like real ice
        }

        /// <summary>Where the tongue starts, as a fraction of the length from the head. It has to sit above
        /// the landing zone's own safety buffer (which reaches s ≈ 0.30 on both glaciers) or the LZ straddles
        /// the foot of the icefall, and no amount of earthwork saves it.</summary>
        public const double TongueStartT = 0.30;
        /// <summary>The tongue's gradient, in degrees. Real glacier landings are made on tongues of 3–6° and
        /// the owner asked for 3° or less along the LZ, so the tongue IS the tolerance less a little — 2.8°,
        /// so that the measured gradient has somewhere to be instead of sitting exactly on the limit. The
        /// landing zone then needs no earthwork of its own beyond taking the crown's curvature out across it
        /// and whatever the proglacial lake's shore blend had taken out of its lower end.</summary>
        public const double TongueDeg = 2.8;
        /// <summary>The span in s over which the icefall's gradient eases into the tongue's. The two differ by
        /// nearly fifty degrees, so without it there is a crease across the ice; with it there is the sharp
        /// convex break a real icefall has at its foot, 300 m above the top of the LZ buffer.</summary>
        public const double TongueFilletT = 0.04;

        /// <summary>The tongue's fall, in metres, from <see cref="TongueStartT"/> down to the terminus.</summary>
        public double TongueDropM => System.Math.Tan(TongueDeg * System.Math.PI / 180) * (1 - TongueStartT) * LengthM;

        /// <summary>How far the ice stands above its own terminus at s along the glacier.</summary>
        public double DropAboveTerminus(double s)
        {
            double total = HeadM - TermM;
            double tongue = TongueDropM;
            // Extending the tongue's straight line all the way to the head would use this much of the drop;
            // the icefall gets the rest. A glacier too shallow to have an icefall at all is all tongue.
            double tongueToHead = tongue / (1 - TongueStartT);
            double icefall = total - tongueToHead;
            if (icefall <= 0) return total * (1 - s);
            double w = System.Math.Clamp((TongueStartT - s) / TongueStartT, 0, 1);
            return tongue * (1 - s) / (1 - TongueStartT) + icefall * IcefallRamp(w);
        }

        /// <summary>
        /// The icefall's share of the drop, 0 at the tongue's top and 1 at the head: a straight gradient with
        /// a smooth start, so the gradient — not just the height — is continuous where it meets the tongue.
        /// </summary>
        private static double IcefallRamp(double w)
        {
            double b = TongueFilletT / TongueStartT;                     // the fillet, in w
            double p = w <= b ? w * w / (2 * b) : w - b / 2;
            return p / (1 - b / 2);
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

    // ---- ICE CORRIDORS AND THE LANDING ZONES ON THEM ---------------------------------------------
    //
    // The two glaciers above are cut into the mountain. The EAST FORK is not: it is the broad valley east of
    // the Colony, and the ground there is the bare Mesa plateau — flat, which is why it needs no earthwork
    // and why it is the easiest of the three to land on. The visual owner paints ice along all three the same
    // way, from one axis and one half-width each, so Core owns all three here rather than leaving the third
    // as a literal in the Unity bridge (it was `new Vector4(7500, -7000, 7700, -5500)` in AlaskaBuilder, and
    // a number the sim cannot see is a number the sim and the shader will disagree about).
    //
    // The frame is the visual owner's, exactly: for the axis unit vector D, S = dot(P − head, D) along the
    // ice and U = dot(P − head, (−D.y, D.x)) across it. Everything below — the landing zone, its safety
    // buffer, the crevasse no-go areas — is stated in S and U so that the shader masks and the height field
    // are the same geometry and not two descriptions of it.
    public readonly struct IceCorridor
    {
        public readonly string Name;
        public readonly double HeadX, HeadY, TermX, TermY, HalfWidthM;
        public IceCorridor(string name, double headX, double headY, double termX, double termY, double halfWidthM)
        { Name = name; HeadX = headX; HeadY = headY; TermX = termX; TermY = termY; HalfWidthM = halfWidthM; }

        public double LengthM => System.Math.Sqrt((TermX - HeadX) * (TermX - HeadX) + (TermY - HeadY) * (TermY - HeadY));
        /// <summary>Down-glacier, clockwise from +x toward +y — the strip-heading convention.</summary>
        public double HeadingDeg => System.Math.Atan2(TermY - HeadY, TermX - HeadX) * 180 / System.Math.PI;

        /// <summary>The visual owner's frame: S metres down the axis from the head, U metres across it.</summary>
        public (double s, double u) Frame(double x, double y)
        {
            double len = LengthM;
            double dx = (TermX - HeadX) / len, dy = (TermY - HeadY) / len;
            double px = x - HeadX, py = y - HeadY;
            return (px * dx + py * dy, px * -dy + py * dx);
        }

        /// <summary>The point at (S, U) — the inverse of <see cref="Frame"/>.</summary>
        public (double x, double y) At(double s, double u)
        {
            double len = LengthM;
            double dx = (TermX - HeadX) / len, dy = (TermY - HeadY) / len;
            return (HeadX + dx * s - dy * u, HeadY + dy * s + dx * u);
        }
    }

    /// <summary>The three corridors the visual owner draws ice along, in the shader's own order.</summary>
    public static readonly IceCorridor[] Corridors =
    {
        new IceCorridor(Glaciers[0].Name, Glaciers[0].HeadX, Glaciers[0].HeadY, Glaciers[0].TermX, Glaciers[0].TermY, Glaciers[0].HalfWidthM),
        new IceCorridor(Glaciers[1].Name, Glaciers[1].HeadX, Glaciers[1].HeadY, Glaciers[1].TermX, Glaciers[1].TermY, Glaciers[1].HalfWidthM),
        // The East Fork: a snowfield on the plateau, not a glacier cut into the massif. No head high on the
        // mountain, no icefall and no proglacial lake — so it is a corridor and not a Glacier, and the tests
        // that ask every Glacier for its lake and its gradient still mean what they say.
        new IceCorridor("East Fork", 7500, -7000, 7700, -5500, 280),
    };

    /// <summary>The landing zone the owner asked for, in the visual owner's numbers: 800 m along the ice by
    /// 80 m across, centred at S = 0.65 × length, aligned down-glacier.</summary>
    public const double LzCentreT = 0.65, LzLengthM = 800, LzWidthM = 80;
    /// <summary>And the safety buffer around it: 920 × 200 m, crevasse-free, blending the graded surface back
    /// into the ice over the 60 m along and 60 m across between the two rectangles — which is the same
    /// 60 m / 60 m feather the terrain shader uses to exclude its cracks and moraines from the LZ.</summary>
    public const double LzBufferLengthM = 920, LzBufferWidthM = 200;
    public const double LzHalfLengthM = LzLengthM / 2, LzHalfWidthM = LzWidthM / 2;
    public const double LzBufferHalfLengthM = LzBufferLengthM / 2, LzBufferHalfWidthM = LzBufferWidthM / 2;
    public static double LzApronAlongM => LzBufferHalfLengthM - LzHalfLengthM;      // 60 m
    public static double LzApronAcrossM => LzBufferHalfWidthM - LzHalfWidthM;       // 60 m

    /// <summary>The tolerances the owner set for a graded zone, and what the tests hold it to.</summary>
    public const double LzMaxAlongDeg = 3.0, LzMaxAlongCeilingDeg = 5.0, LzMaxAcrossDeg = 2.0, LzMaxPlaneErrM = 0.5;

    /// <summary>What the graded surface is made of.</summary>
    public enum LzGrade
    {
        /// <summary>The ice's own tongue: the zone is the centreline of the glacier surface, so the earthwork
        /// is only the crown's curvature across and whatever the proglacial lake's shore blend took out of
        /// the lower end. This is the Knik, once its tongue is a tongue.</summary>
        Tongue,
        /// <summary>A pad cut flat at plateau level. The Colony and the East Fork have no ice ON the ground in
        /// their lower halves — the ice surface there is ABOVE the bare plateau, so the height field keeps
        /// the plateau and the shader paints it white. The zone is therefore the plateau, flattened: level,
        /// with the walls and steps that clipped its upper end cut back to it.</summary>
        Pad,
    }

    public readonly struct LandingZone
    {
        public readonly int Corridor;
        public readonly LzGrade Grade;
        public LandingZone(int corridor, LzGrade grade) { Corridor = corridor; Grade = grade; }
        public IceCorridor Ice => Corridors[Corridor];
        public string Name => Ice.Name;
        /// <summary>S at the middle of the zone.</summary>
        public double CentreS => LzCentreT * Ice.LengthM;
        public (double x, double y) Centre => Ice.At(CentreS, 0);
        public double HeadingDeg => Ice.HeadingDeg;

        /// <summary>The graded surface, as a plane: this is what the height field returns inside the clear
        /// rectangle, and the thing the tolerances are measured against.</summary>
        public double SurfaceAt(double s)
        {
            if (Grade == LzGrade.Pad) return BaseM;         // dead level on the plateau
            Glacier g = Glaciers[Corridor];
            // The tongue's own centreline: its height at the middle of the zone, carried along it at the
            // tongue's own gradient. Zero cut and zero fill at the centre, by construction.
            double centre = g.TermM + g.DropAboveTerminus(LzCentreT) + 14;
            return centre - System.Math.Tan(Glacier.TongueDeg * System.Math.PI / 180) * (s - CentreS);
        }

        /// <summary>The gradient down the zone, in degrees — + falls down-glacier.</summary>
        public double AlongDeg => Grade == LzGrade.Pad ? 0 : Glacier.TongueDeg;
    }

    /// <summary>One graded zone per corridor, in the same order.</summary>
    public static readonly LandingZone[] LandingZones =
    {
        new LandingZone(0, LzGrade.Tongue),     // Knik: graded along its own tongue
        new LandingZone(1, LzGrade.Pad),        // Colony: a pad flattened out of the plateau
        new LandingZone(2, LzGrade.Pad),        // East Fork: already flat; declared so it stays that way
    };

    /// <summary>1 on the clear rectangle, falling to 0 at the edge of the safety buffer. The graded surface
    /// owns the ground where this is 1 and hands it back to the ice where it is 0.</summary>
    public static double LzClaim(LandingZone z, double s, double u)
    {
        double ds = System.Math.Abs(s - z.CentreS), du = System.Math.Abs(u);
        if (ds > LzBufferHalfLengthM || du > LzBufferHalfWidthM) return 0;
        double along = ds <= LzHalfLengthM ? 1 : 1 - Smooth((ds - LzHalfLengthM) / LzApronAlongM);
        double across = du <= LzHalfWidthM ? 1 : 1 - Smooth((du - LzHalfWidthM) / LzApronAcrossM);
        return along * across;
    }

    /// <summary>Inside the 800 × 80 m rectangle that has to meet the tolerances.</summary>
    public static bool InLandingZone(double x, double y)
    {
        foreach (LandingZone z in LandingZones)
        {
            var (s, u) = z.Ice.Frame(x, y);
            if (System.Math.Abs(s - z.CentreS) <= LzHalfLengthM && System.Math.Abs(u) <= LzHalfWidthM) return true;
        }
        return false;
    }

    /// <summary>Inside the 920 × 200 m safety buffer: graded or blending, and crevasse-free by construction.</summary>
    public static bool InLandingBuffer(double x, double y) => InLandingBuffer(x, y, 0);

    /// <summary>The buffer, grown by <paramref name="marginM"/> on every side.</summary>
    public static bool InLandingBuffer(double x, double y, double marginM)
    {
        foreach (LandingZone z in LandingZones)
        {
            var (s, u) = z.Ice.Frame(x, y);
            if (System.Math.Abs(s - z.CentreS) <= LzBufferHalfLengthM + marginM
                && System.Math.Abs(u) <= LzBufferHalfWidthM + marginM) return true;
        }
        return false;
    }

    /// <summary>How far outside the safety buffer the crevasse no-go areas start.</summary>
    public const double LzClearMarginM = 2.0;

    /// <summary>
    /// The crevasse no-go areas, in the visual owner's terms: outside the safety buffer, the upper 55% of the
    /// length is the icefall, the outer 55% of the half-width (|U| > 0.45 × half-width) is the margin where
    /// the ice shears against the valley wall, and anywhere the ice is locally steep is pulling apart. None
    /// of it is inside the buffer — that is what the buffer is for.
    ///
    /// NOTE for the visual owner: this is WIDER than the cracks the terrain shader currently draws, which
    /// gate on the outer 35% of the half-width and the upper 35% of the length. The sim's no-go is deliberately
    /// the more conservative of the two, but the drawn cracks should grow to match it or a pilot will see
    /// clean ice where the ground is broken.
    /// </summary>
    public const double CrevasseIcefallT = 0.55, CrevasseMarginFrac = 0.45, CrevasseSteepDeg = 12.0;

    public static bool Crevassed(double x, double y)
    {
        if (!InBlock(x, y)) return false;
        for (int i = 0; i < Corridors.Length; i++)
        {
            IceCorridor c = Corridors[i];
            var (s, u) = c.Frame(x, y);
            double len = c.LengthM;
            if (s < 0 || s > len || System.Math.Abs(u) > c.HalfWidthM) continue;
            // The buffer is clear, always — and clear with a couple of metres in hand, so that no crack
            // reaches its very edge and so that a point built at exactly S = centre − 460 and read back
            // through the frame does not fall out of the buffer on a rounding error, which is how a no-go
            // area first appeared inside the Knik's buffer at (S −460, U −10).
            if (InLandingBuffer(x, y, LzClearMarginM)) return false;
            if (s < CrevasseIcefallT * len) return true;                   // the icefall
            if (System.Math.Abs(u) > CrevasseMarginFrac * c.HalfWidthM) return true;   // the margins
            if (i < Glaciers.Length)
            {
                // And wherever the ice itself is steepening: it is being stretched, so it is cracking.
                Glacier g = Glaciers[i];
                double t = s / len, d = 0.004;
                double fall = (g.DropAboveTerminus(System.Math.Max(0, t - d)) - g.DropAboveTerminus(System.Math.Min(1, t + d)))
                            / (2 * d * len);
                if (System.Math.Atan(System.Math.Abs(fall)) * 180 / System.Math.PI > CrevasseSteepDeg) return true;
            }
        }
        return false;
    }

    /// <summary>Is (x, y) on one of the three ice corridors at all?</summary>
    public static bool OnCorridor(double x, double y)
    {
        if (!InBlock(x, y)) return false;
        foreach (IceCorridor c in Corridors)
        {
            var (s, u) = c.Frame(x, y);
            if (s >= 0 && s <= c.LengthM && System.Math.Abs(u) <= c.HalfWidthM) return true;
        }
        return false;
    }

    /// <summary>
    /// How far outside the block the range's ground still reaches, per side. Each has to clear the widest
    /// peak's own skirt or a cone is cut off with relief still on it, and the south side did not: Foraker's
    /// radius is 1,300 m from a centre 250 m inside Y0, so its skirt reaches 1,050 m past it and the pad was
    /// 900. That left a 60 m step along the line y = Y0 − 900 (found 2026-10-17 by the world sweep, at
    /// (5,460, −8,350), which is Foraker's flank). The other three sides clear their peaks: Hunter reaches
    /// 600 m past X0, Huntington 750 m past Y1, and nothing comes near X1.
    /// </summary>
    public const double PadWestM = 800, PadEastM = 800, PadSouthM = 1200, PadNorthM = 800;

    private static bool InBlock(double x, double y)
        => x > X0 - PadWestM && x < X1 + PadEastM && y > Y0 - PadSouthM && y < Y1 + PadNorthM;

    public static bool InRange(double x, double y) => InBlock(x, y);

    private static double Smooth(double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Ridge(double x, double y)
        => 60 * System.Math.Sin(x / 260.0) * System.Math.Sin(y / 310.0)
         + 26 * System.Math.Sin(x / 97.0 + 1.3) * System.Math.Sin(y / 113.0 + 0.4);

    /// <summary>How far in a CIRQUE reaches, as a fraction of the peak's nominal radius. The ridges reach the
    /// full radius and the bowls between them stop short of it, so the footprint only ever shrinks.</summary>
    private const double ReachInCirque = 0.74;
    /// <summary>How proud a buttress stands, as a fraction of the peak's relief.</summary>
    private const double ButtressFrac = 0.030;

    /// <summary>
    /// The peak's plan: 1 along a main ridge, 0 in the middle of a cirque between two of them. Periodic in
    /// the bearing — the wander is made of sines for the same reason <see cref="WorldTerrain.ConeErosion"/>'s
    /// is, so that a bearing of +π and one of −π are the same ground and there is no seam down one flank.
    /// </summary>
    private static double RidgePlan(double bearing, double seed, int arms)
    {
        double u = arms * (bearing / (2 * System.Math.PI)
                           + 0.085 * System.Math.Sin(2 * bearing + seed)
                           + 0.040 * System.Math.Sin(3 * bearing - 2 * seed));
        double v = u - System.Math.Floor(u);
        double crest = 1 - System.Math.Abs(2 * v - 1);       // triangular: a ridge, a bowl, a ridge
        return System.Math.Pow(crest, 0.70);                 // broad ridges, tight cirque floors
    }

    /// <summary>How far past its head, as a fraction of its length, a glacier's claim on the ground fades to
    /// nothing. Short: above the head is the icefall and the cirque wall over it, which is near-vertical rock
    /// in the real thing.</summary>
    public const double HeadFadeT = 0.08;
    /// <summary>And past its toe, where the trough opens out into the valley below the terminus.</summary>
    public const double ToeFadeT = 0.25;

    /// <summary>The mountains, the glaciers cut into them, and the lakes at the ice fronts.</summary>
    public static double HeightAt(double x, double y, double baseH) => HeightAt(x, y, baseH, null);

    /// <summary>The same ground, recording the height after each stage — see the note on
    /// <see cref="AlpineRange.HeightAt(double, double, double, System.Collections.Generic.List{System.ValueTuple{string, double}}?)"/>.</summary>
    public static double HeightAt(double x, double y, double baseH, System.Collections.Generic.List<(string stage, double h)>? trace)
    {
        if (!InBlock(x, y)) { trace?.Add(("outside the block", baseH)); return baseH; }
        double h = baseH;

        foreach (Peak p in Peaks)
        {
            double d = System.Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            if (d >= p.RadiusM) continue;
            double bearing0 = System.Math.Atan2(y - p.Y, x - p.X);
            double seed0 = p.X * 0.001 + p.Y * 0.0007;
            // RIDGES AND BUTTRESSES (owner 2026-10-29: the peaks are "cone-shaped"). They were. A smoothstep
            // of the distance from one point is a CIRCLE in plan and a dome in section, and the renders came
            // back as a row of draped plaster hats however much erosion was cut into the flanks — because
            // erosion takes material away from a cone and leaves a cone.
            //
            // What makes a massif is its PLAN: three to five main ridges running off the summit with cirques
            // bitten in between them, so the foot is a star and not a circle. So the reach varies with the
            // bearing. It only ever varies DOWNWARD, never past p.RadiusM: the pads outside the block are
            // sized to the widest peak's own skirt, and Foraker's already reaches 1,050 m of the 1,200 m
            // south pad — let a ridge reach further than its nominal radius and the old 60 m step along
            // y = Y0 − 900 comes straight back.
            int arms = 3 + (int)(3 * WorldTerrain.Hash(p.X * 0.0117 + p.Y * 0.0231, 8.7));
            double plan = RidgePlan(bearing0, seed0, arms);
            double reach = p.RadiusM * (ReachInCirque + (1 - ReachInCirque) * plan);
            if (d >= reach) continue;
            double t = 1 - d / reach;
            // Smoothstep, not pow(t, 1.6): a power law has slope 1.6 at the summit, which puts a point on
            // every mountain — the range rendered as a row of witch's hats. Smoothstep's slope is zero at both
            // ends, so the summit rounds off and the foot blends into the plateau. (The same mistake, and the
            // same fix, as the high desert's peaks.)
            // The cone stands on the PLATEAU the range is declared to stand on, not on whatever the base
            // field happens to be doing underneath it. It used to be `baseH + (p.TopM - baseH) * Smooth(t)`,
            // which multiplies every wrinkle in the base field by (1 - Smooth(t)) and carries it up the
            // mountain: there is an 18 m escarpment in the base field at (2522, -6820), under Hunter's west
            // flank, and the cone was reproducing a scaled copy of it as an 11 m wall in mid-mountainside.
            // The range's own doc has said from the start that its heights are RELIEF above the plateau, for
            // exactly this reason; this makes the cone mean it. (Max() still lets higher ground win, and the
            // rim fade below still hands the foot back to whatever is there.)
            double cone = BaseM + p.ReliefM * Smooth(t);
            cone += Ridge(x, y) * (0.3 + 0.7 * System.Math.Sin(System.Math.PI * System.Math.Clamp(t, 0, 1)));
            // And the BUTTRESSES: where a ridge comes down onto the glaciers it stands out from the flank
            // between it and the next one as a broad rib. Highest on the lower flank, gone before the summit
            // (up there the ridge is an arête, which is what the plan above and the spurs below make of it),
            // and zero at the foot so the skirt still lies down onto the plateau.
            cone += ButtressFrac * (p.TopM - baseH) * plan * plan
                  * Smooth(System.Math.Clamp(t / 0.14, 0, 1)) * Smooth(System.Math.Clamp((0.72 - t) / 0.30, 0, 1));
            // Water and ice take a mountain apart from the top down (owner 2026-10-16): couloirs down the
            // flanks with ribs standing between them, deepest at mid-height, closing as they reach the summit.
            int channels = 11 + (int)(6 * WorldTerrain.Hash(p.X * 0.013 + p.Y * 0.017, 4.1));
            double depth = System.Math.Clamp(0.045 * (p.TopM - baseH), 30, 170);
            cone -= WorldTerrain.ConeErosion(bearing0, t, seed0, channels, depth, depth * 0.22);
            // AND THE RIM. `cone` is only taken where it beats what is under it, so a cone that drops below
            // the plateau at its own foot costs nothing — but the Ridge term above is multiplied by
            // (0.3 + 0.7 sin(pi t)), which is 0.3 and NOT zero at t = 0, so the cone could stand up to 26 m
            // proud of the ground at the very edge of its footprint and the ground stepped there. The old
            // fixed-radius footprint hid it (the edge fell where Ridge happened to be negative); varying the
            // reach with the bearing moved the edge onto ground where Ridge is positive, and the world sweep
            // found a 15 m step in 0.1 m at (2520, -6820), on Hunter's west flank. Fading the whole
            // contribution over the last 6% of the reach makes the rim continuous whatever is inside it.
            cone = baseH + (cone - baseH) * Smooth(System.Math.Clamp(t / 0.06, 0, 1));
            h = System.Math.Max(h, cone);
        }

        trace?.Add(("peaks", h));
        // The glaciers: a valley of ice cut through whatever the mountains just built.
        //
        // The ice's claim on the ground fades at the SIDES and at the ENDS (the ends, 2026-10-17). Across the
        // trough it always did — that is what makes it read as ice in a valley rather than a ribbon laid on
        // the mountainside — but along its length it simply stopped at t = −0.05 and t = 1.25, and where the
        // mountain above the head was 2.2 km higher than the ice, that is exactly what you got: a 2,235 m
        // vertical wall across the head of the glacier on the 19,000 ft peak, at (5,400, −7,160), with no
        // more geology behind it than an inequality.
        foreach (Glacier g in Glaciers)
        {
            var (t, across) = g.Local(x, y);
            if (t < -HeadFadeT || t > 1 + ToeFadeT || across > g.HalfWidthM + 400) continue;
            double along = t < 0 ? Smooth((t + HeadFadeT) / HeadFadeT)
                         : t > 1 ? Smooth((1 + ToeFadeT - t) / ToeFadeT)
                         : 1;
            double side = across <= g.HalfWidthM ? 1 : 1 - Smooth((across - g.HalfWidthM) / 400.0);
            double claim = along * side;
            if (claim <= 0) continue;
            double ice = g.IceHeightAt(t, across);
            h = System.Math.Min(h, ice + (h - ice) * (1 - claim));
        }

        trace?.Add(("glaciers", h));
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
        trace?.Add(("ice-front lakes", h));

        // THE GRADED LANDING ZONES, last — after the lakes on purpose. The Knik's proglacial lake reaches
        // r = 1.47 of its own radius at the lower end of its landing zone, and its shore blend was pulling
        // the last 40 m of the rectangle down 3 m, which on its own is a 4° break across the touchdown end.
        // Nothing may be laid over a graded surface afterwards, or it is not graded.
        foreach (LandingZone z in LandingZones)
        {
            var (ls, lu) = z.Ice.Frame(x, y);
            double claim = LzClaim(z, ls, lu);
            if (claim <= 0) continue;
            double pad = z.SurfaceAt(ls);
            h = pad + (h - pad) * (1 - claim);
        }
        trace?.Add(("graded landing zones", h));
        return h;
    }

    /// <summary>
    /// Is this point on glacier ice — the thing a ski wants and a wheel does not? All three corridors,
    /// including the East Fork, which had no sim ice designation at all until now: the visual owner has been
    /// painting ice along it since the first visuals pass while the sim called it gravel, so a pilot could
    /// see a glacier and land on a rock field.
    /// </summary>
    public static bool OnIce(double x, double y) => OnCorridor(x, y);

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

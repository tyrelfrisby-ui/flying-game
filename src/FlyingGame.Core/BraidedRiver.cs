namespace FlyingGame.Core;

/// <summary>
/// The outwash plain below the glaciers (owner 2026-10-16): "ribboned rivers with gravel bars galore",
/// moderately difficult, with slight elevation changes — sometimes sharp but visible — driftwood logs and
/// the occasional boulder. Modelled on the flats below the Knik and the Colony, which is where people really
/// do learn this.
///
/// WHAT MAKES IT FLYABLE. A braided river is not one river: it is a dozen shallow channels wandering across a
/// flat of gravel, and the bars between them are what you land on. Three things decide whether that is a
/// pleasure or a lottery, and all three are measured by tests rather than eyeballed:
///
///  1. EVERY BAR HAS A CLEAN LANE. The obstacles are scattered deterministically — the same every time, so a
///     pilot can learn a bar — but a lane <see cref="CleanLaneHalfWidthM"/> wide and
///     <see cref="CleanLaneLengthM"/> long down the middle of each bar is left empty, which is enough for
///     anything up to a Beaver.
///  2. THE STEPS ARE VISIBLE. Bars sit a little above the channels, and some have a sharp lip at one end
///     rather than a ramp — but a lip is never more than <see cref="MaxStepM"/>, and it always faces across
///     the usual landing direction so it is seen side-on against the water.
///  3. THE WIND IS ALWAYS THERE. A glacier breathes downhill all day; see <see cref="WindAt"/>. Land upstream
///     and it is a headwind, which is the whole reason to land upstream.
/// </summary>
public static class BraidedRiver
{
    /// <summary>
    /// The plain. It runs NORTH from the glacier lakes, down off the Mesa rim onto the Bench plateau below —
    /// which is where the room is, and which is also what meltwater does: the ice ends at the rim and the
    /// outwash spreads over the flats underneath. <see cref="Y0"/> is the high end, by the ice.
    ///
    /// (It was first laid out south of the glaciers, directly on top of the Cascades range and two of its
    /// lakes. Their own tests caught it, which is the entire reason those tests sample the ground around
    /// them rather than trusting the layout.)
    /// </summary>
    /// X0 clears the tunnel maze, which runs under this plateau out to x ≈ 3,400 — and where the maze runs,
    /// the GROUND is its bore floor 610 m down, so a channel laid across it fell off a cliff.
    public const double X0 = 3600, X1 = 8000, Y0 = -4150, Y1 = -1450;
    /// <summary>How far outside that rectangle the outwash still lays its own gravel down — the footprint it
    /// actually FLATTENS, which is what a mountain front has to keep out of.</summary>
    public const double FootprintPadM = 300;

    /// <summary>The outwash plain's head and foot — a real outwash gradient, about 1 %. Tied to the BENCH
    /// plateau (the braids run out across it) rather than written as metres above the sea: the foot IS the
    /// plateau, and when the step went to 2,000 ft an absolute 762 m would have put the river 150 m under it.</summary>
    public static double FootM => WorldTerrain.DatumM + WorldTerrain.StepHeightM;
    public static double HeadM => FootM + 58;

    public const double ChannelHalfWidthM = 34;      // one braid
    public const double ChannelDepthM = 0.8;         // shallow: a wheel can skim it, which is the point
    public const double BarRaiseM = 1.6;             // how far a bar stands above the water
    /// <summary>The sharpest lip on the plain. A bar standing 2 m proud with a channel scoured at its foot
    /// is a 3 m cut bank, which is what the Knik flats really look like — and it is always side-on to the
    /// landing direction, so it is seen against the water rather than met head-on.</summary>
    public const double MaxStepM = 3.0;
    public const double CleanLaneHalfWidthM = 26;
    public const double CleanLaneLengthM = 520;

    public enum Obstacle { Driftwood, Boulder, Bush }

    public readonly struct Hazard
    {
        public readonly Obstacle Kind;
        public readonly double X, Y, RadiusM, HeightM, HeadingDeg;
        public Hazard(Obstacle kind, double x, double y, double radiusM, double heightM, double headingDeg)
        { Kind = kind; X = x; Y = y; RadiusM = radiusM; HeightM = heightM; HeadingDeg = headingDeg; }
    }

    /// <summary>
    /// A gravel bar: somewhere to land. The <see cref="LaneHeadingDeg"/> is the way the lane runs — up the
    /// valley, into the glacier wind.
    /// </summary>
    public readonly struct Bar
    {
        public readonly string Name;
        public readonly double X, Y, LengthM, WidthM, HeadingDeg, RaiseM;
        public readonly bool SharpLip;              // a cut bank at the downstream end rather than a ramp
        public Bar(string name, double x, double y, double lengthM, double widthM, double headingDeg,
                   double raiseM, bool sharpLip)
        { Name = name; X = x; Y = y; LengthM = lengthM; WidthM = widthM; HeadingDeg = headingDeg; RaiseM = raiseM; SharpLip = sharpLip; }

        public double LaneHeadingDeg => HeadingDeg;

        public (double along, double across) Local(double x, double y)
        {
            double h = HeadingDeg * System.Math.PI / 180;
            double dx = x - X, dy = y - Y;
            return (dx * System.Math.Cos(h) + dy * System.Math.Sin(h),
                    -dx * System.Math.Sin(h) + dy * System.Math.Cos(h));
        }

        public bool On(double x, double y, double pad = 0)
        {
            var (a, c) = Local(x, y);
            return System.Math.Abs(a) <= LengthM * 0.5 + pad && System.Math.Abs(c) <= WidthM * 0.5 + pad;
        }

        /// <summary>Is this point in the lane that is kept clear of obstacles?</summary>
        public bool InCleanLane(double x, double y)
        {
            var (a, c) = Local(x, y);
            return System.Math.Abs(a) <= System.Math.Min(CleanLaneLengthM, LengthM) * 0.5
                && System.Math.Abs(c) <= CleanLaneHalfWidthM;
        }
    }

    /// <summary>Bed height before any bar or channel: the plain's own fall down the valley.</summary>
    public static double PlainHeightAt(double x, double y)
    {
        // 0 at the ice end, 1 at the foot: the valley runs toward +y.
        double t = System.Math.Clamp((y - Y0) / (Y1 - Y0), 0, 1);
        double h = HeadM + (FootM - HeadM) * t;
        // A little texture so it is gravel rather than a table.
        return h + 0.5 * System.Math.Sin(x / 61.0) * System.Math.Sin(y / 47.0);
    }

    /// <summary>
    /// The braids. Each channel wanders down the plain; where one runs, the bed is cut
    /// <see cref="ChannelDepthM"/> below the plain and there is water in it.
    /// </summary>
    public static double ChannelCentreX(int braid, double y)
    {
        double k = braid * 1.37;
        double spread = 2000 + 260 * braid;
        return (X0 + X1) * 0.5 + (braid - 3.5) * 560
               + 180 * System.Math.Sin(y / 430.0 + k) + 70 * System.Math.Sin(y / 190.0 + 2 * k)
               + 0.04 * spread * System.Math.Sin(y / 1100.0 + k);
    }

    public const int Braids = 8;

    /// <summary>How deep into a channel (0 outside, 1 in the middle of one).</summary>
    private static double ChannelDepthFraction(double x, double y)
    {
        double deepest = 0;
        for (int b = 0; b < Braids; b++)
        {
            double d = System.Math.Abs(x - ChannelCentreX(b, y));
            if (d >= ChannelHalfWidthM) continue;
            double u = 1 - d / ChannelHalfWidthM;
            deepest = System.Math.Max(deepest, u * u * (3 - 2 * u));
        }
        return deepest;
    }

    /// <summary>
    /// The bars, lying BETWEEN the braids and running UP THE VALLEY — which is what makes landing on them a
    /// landing into the glacier wind. (The first layout had them across the valley, and the wind that is the
    /// whole point of the place was a crosswind on every one of them.)
    /// </summary>
    public static readonly Bar[] Bars =
    {
        //            name            x       y     len  wid  hdg  raise  sharpLip
        new Bar("Long Bar",        4100,  -2600, 900, 150, 270, 1.6, false),
        new Bar("Lower Long Bar",  4520,  -1900, 760, 150, 272, 1.4, true),
        new Bar("Middle Bar",      5140,  -3100, 820, 160, 268, 1.8, false),
        new Bar("Cut Bank Bar",    5760,  -2200, 640, 140, 271, 2.0, true),
        new Bar("Island Bar",      6380,  -3300, 700, 150, 269, 1.5, false),
        new Bar("Top Bar",         7000,  -2500, 560, 130, 273, 1.7, true),
    };

    // ---- the two named strips ------------------------------------------------------------------------

    /// <summary>
    /// A strip somebody has made, as opposed to a bar somebody lands on: graded, marked, and with things
    /// round it. The owner asked for two — a practice strip with orange cones, chairs and a chalk line
    /// across it, and a picnic strip by a glacier with benches.
    /// </summary>
    public readonly struct NamedStrip
    {
        public readonly string Name;
        public readonly double X, Y, HeadingDeg, LengthM, WidthM;
        public readonly bool Cones, ChalkLine, Chairs, PicnicBenches;

        public NamedStrip(string name, double x, double y, double headingDeg, double lengthM, double widthM,
                          bool cones, bool chalkLine, bool chairs, bool picnicBenches)
        {
            Name = name; X = x; Y = y; HeadingDeg = headingDeg; LengthM = lengthM; WidthM = widthM;
            Cones = cones; ChalkLine = chalkLine; Chairs = chairs; PicnicBenches = picnicBenches;
        }

        public double LengthFt => LengthM * 3.28084;
        public double WidthFt => WidthM * 3.28084;

        public (double along, double across) Local(double x, double y)
        {
            double h = HeadingDeg * System.Math.PI / 180;
            double dx = x - X, dy = y - Y;
            return (dx * System.Math.Cos(h) + dy * System.Math.Sin(h),
                    -dx * System.Math.Sin(h) + dy * System.Math.Cos(h));
        }

        public bool On(double x, double y, double pad = 0)
        {
            var (a, c) = Local(x, y);
            return System.Math.Abs(a) <= LengthM * 0.5 + pad && System.Math.Abs(c) <= WidthM * 0.5 + pad;
        }

        /// <summary>A point on the strip, from fractions along and across it.</summary>
        public (double x, double y) At(double alongFrac, double acrossM)
        {
            double h = HeadingDeg * System.Math.PI / 180;
            double a = alongFrac * LengthM;
            return (X + System.Math.Cos(h) * a - System.Math.Sin(h) * acrossM,
                    Y + System.Math.Sin(h) * a + System.Math.Cos(h) * acrossM);
        }
    }

    /// <summary>Where the chalk line is drawn across the practice strip: a third of the way in, which is
    /// what you aim at when somebody is scoring your spot landings.</summary>
    public const double ChalkLineFrac = -0.17;
    /// <summary>Cones down both sides at this spacing.</summary>
    public const double ConeSpacingM = 60;

    public static readonly NamedStrip[] NamedStrips =
    {
        // 2,500 ft by 100 ft of graded gravel, up the valley into the wind like everything else here.
        new NamedStrip("Practice Strip", 5450, -1750, 270, 762.0, 30.5, true, true, true, false),
        // By the Knik's lake, where you would stop for lunch and watch the ice.
        new NamedStrip("Picnic Strip",   7050,  -4000, 276, 430.0, 24.0, false, false, false, true),
    };

    /// <summary>The orange cones: both edges, evenly spaced, and one pair on the chalk line.</summary>
    public static System.Collections.Generic.IEnumerable<(double x, double y)> Cones(NamedStrip s)
    {
        if (!s.Cones) yield break;
        int n = (int)(s.LengthM / ConeSpacingM);
        for (int i = 0; i <= n; i++)
        {
            double f = -0.5 + (double)i / n;
            foreach (int side in new[] { -1, 1 })
            {
                var (cx, cy) = s.At(f, side * (s.WidthM * 0.5 + 2));
                yield return (cx, cy);
            }
        }
    }

    /// <summary>The chairs: a row beside the chalk line, facing the strip, for people watching landings.</summary>
    public static System.Collections.Generic.IEnumerable<(double x, double y, double headingDeg)> Chairs(NamedStrip s)
    {
        if (!s.Chairs) yield break;
        for (int i = 0; i < 6; i++)
        {
            var (cx, cy) = s.At(ChalkLineFrac + (i - 2.5) * 0.012, s.WidthM * 0.5 + 9);
            yield return (cx, cy, s.HeadingDeg - 90);          // facing across the strip
        }
    }

    /// <summary>The picnic benches, in the grass off the side of the picnic strip.</summary>
    public static System.Collections.Generic.IEnumerable<(double x, double y, double headingDeg)> PicnicBenches(NamedStrip s)
    {
        if (!s.PicnicBenches) yield break;
        for (int i = 0; i < 4; i++)
        {
            var (bx, by) = s.At(-0.3 + i * 0.2, s.WidthM * 0.5 + 12);
            yield return (bx, by, s.HeadingDeg + (i % 2 == 0 ? 20 : -20));
        }
    }

    private static bool InPlain(double x, double y)
        => x > X0 - FootprintPadM && x < X1 + FootprintPadM && y > Y0 - FootprintPadM && y < Y1 + FootprintPadM;

    public static bool InArea(double x, double y) => InPlain(x, y);

    private static double Smooth(double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The plain, its braids and its bars.</summary>
    public static double HeightAt(double x, double y, double baseH)
    {
        if (!InPlain(x, y)) return baseH;
        double h = PlainHeightAt(x, y);

        // The channels, cut into it.
        double ch = ChannelDepthFraction(x, y);
        if (ch > 0) h -= ChannelDepthM * ch;

        // The bars, standing above it. A sharp lip at one end is a cut bank: the step is a step, not a ramp,
        // and it is capped so it can always be survived by somebody who sees it.
        foreach (Bar b in Bars)
        {
            var (along, across) = b.Local(x, y);
            double halfL = b.LengthM * 0.5, halfW = b.WidthM * 0.5;
            double outAlong = System.Math.Max(0, System.Math.Abs(along) - halfL);
            double outAcross = System.Math.Max(0, System.Math.Abs(across) - halfW);
            double outside = System.Math.Sqrt(outAlong * outAlong + outAcross * outAcross);
            double blend = b.SharpLip && along < 0 ? 6.0 : 60.0;     // a cut bank at the downstream end
            if (outside > blend) continue;
            double top = PlainHeightAt(x, y) + System.Math.Min(b.RaiseM, MaxStepM);
            h = System.Math.Max(h, top - (top - h) * Smooth(outside / blend));
        }
        h = System.Math.Max(h, PlainHeightAt(x, y) - ChannelDepthM - 0.2);

        // Hand back to the ground underneath across the margin outside the plain. The plain is laid at its
        // own level for 300 m beyond its bounds and then stops, which on the south side is where the first
        // front climbs — so the gravel was being laid across the face and ending in a wall. (The same fault
        // the high desert had on the third front: 2026-10-16.) Before the strips, not after: their grading
        // is the last word, and a strip near the edge came out tilted when this was applied over the top.
        h = baseH + (h - baseH) * WorldTerrain.BlockFade(x, y, X0, Y0, X1, Y1, FootprintPadM);

        // The made strips are graded, and that grading is the LAST word: dead flat across, following the
        // valley's own fall along. Applying it before the plain's floor clamp let the gravel texture ripple
        // through and put four centimetres of wave across a strip that is supposed to have been bladed.
        foreach (NamedStrip ns in NamedStrips)
        {
            var (along, across) = ns.Local(x, y);
            double outA = System.Math.Max(0, System.Math.Abs(along) - ns.LengthM * 0.5);
            double outC = System.Math.Max(0, System.Math.Abs(across) - ns.WidthM * 0.5);
            double outside = System.Math.Sqrt(outA * outA + outC * outC);
            if (outside > 90) continue;
            double grade = (HeadM - FootM) / (Y1 - Y0);                     // the plain's own gradient
            double graded = PlainHeightAt(ns.X, ns.Y) + along * grade;
            h = outside <= 25 ? graded : graded + (h - graded) * Smooth((outside - 25) / 65.0);
        }
        return h;
    }

    /// <summary>Water in the braids — shallow, which is what makes a wheel skim rather than sink.</summary>
    public static double? WaterSurfaceAt(double x, double y)
    {
        // The plain ITSELF, not the margin the height field blends over — the same rule the surface follows.
        // The bed in the margin is faded toward the ground underneath while the water surface is not, so
        // water out there sat two and a half metres deep over a bed that had been handed back to the Bench.
        if (x < X0 || x > X1 || y < Y0 || y > Y1) return null;
        foreach (Bar b in Bars) if (b.On(x, y, 70)) return null;     // a bar and its shoulder are dry
        foreach (NamedStrip ns in NamedStrips) if (ns.On(x, y, 60)) return null;
        double ch = ChannelDepthFraction(x, y);
        if (ch < 0.25) return null;
        return PlainHeightAt(x, y) - ChannelDepthM * ch + 0.25;      // a hand's depth over the bed
    }

    /// <summary>Gravel, all of it — but only the plain ITSELF, not the margin the height field blends over.
    /// Claiming the margin turned ground three hundred metres outside the plain into gravel, which an
    /// airport test two plateaus away noticed before anybody else would have.</summary>
    public static WorldTerrain.Surface? SurfaceAt(double x, double y)
        => x >= X0 && x <= X1 && y >= Y0 && y <= Y1 ? WorldTerrain.Surface.Gravel : null;

    // ---- the wind ------------------------------------------------------------------------------------

    /// <summary>Strength of the glacier wind at the head of the plain, in m/s. 18 kt, and it never stops.</summary>
    public const double GlacierWindMs = 9.3;

    /// <summary>
    /// A glacier breathes downhill: cold dense air slides off the ice all day and runs away down the valley.
    /// So the wind here blows from the glaciers toward the foot of the plain, strongest near the ice and
    /// fading as the valley opens out — which means landing UPSTREAM, the way a bar is meant to be landed on,
    /// is always into a headwind. Returned in world axes (x north, y east, z down), zero outside the plain.
    /// </summary>
    public static MathTypes.Vec3 WindAt(double x, double y, double up)
    {
        if (!InPlain(x, y)) return MathTypes.Vec3.Zero;
        // Down-valley is +y: away from the ice at Y0 and toward the foot at Y1.
        double along = System.Math.Clamp((y - Y0) / (Y1 - Y0), 0, 1);
        double strength = GlacierWindMs * (1.0 - 0.45 * along);
        // It hugs the ground: the ice wind is a shallow layer, gone a few hundred metres up.
        double hAgl = up - PlainHeightAt(x, y);
        double layer = System.Math.Clamp(1.0 - (hAgl - 60) / 420.0, 0, 1);
        return new MathTypes.Vec3(0, strength * layer, 0);
    }

    // ---- the obstacles -------------------------------------------------------------------------------

    /// <summary>
    /// Driftwood, boulders and bushes, scattered across the plain — the same scatter every time, because a
    /// hazard you cannot learn is a dice roll. Nothing is ever placed in a bar's clean lane.
    /// </summary>
    public static System.Collections.Generic.List<Hazard> Hazards()
    {
        var list = new System.Collections.Generic.List<Hazard>();
        // A fixed lattice with a deterministic jitter: no random number generator anywhere near it.
        int n = 0;
        for (double x = X0 + 120; x < X1; x += 190)
        for (double y = Y0 + 120; y < Y1; y += 190)
        {
            n++;
            double jx = 70 * System.Math.Sin(n * 12.9898) * System.Math.Cos(n * 4.1414);
            double jy = 70 * System.Math.Sin(n * 78.233);
            double px = x + jx, py = y + jy;

            // Never in a channel (it would wash away), never on a bar's clean lane.
            if (ChannelDepthFraction(px, py) > 0.3) continue;
            bool blocked = false;
            foreach (Bar b in Bars) if (b.InCleanLane(px, py)) { blocked = true; break; }
            // And nothing at all on a made strip or its shoulders: these are graded.
            foreach (NamedStrip ns in NamedStrips) if (ns.On(px, py, 45)) { blocked = true; break; }
            if (blocked) continue;

            double pick = System.Math.Abs(System.Math.Sin(n * 3.717));
            if (pick < 0.30) continue;                               // most of the plain is bare gravel
            Obstacle kind = pick < 0.60 ? Obstacle.Driftwood : pick < 0.85 ? Obstacle.Bush : Obstacle.Boulder;
            double radius = kind switch
            {
                Obstacle.Driftwood => 3.5 + 4.0 * pick,
                Obstacle.Bush => 1.6 + 1.4 * pick,
                _ => 1.2 + 1.6 * pick,
            };
            double height = kind switch
            {
                Obstacle.Driftwood => 0.7,
                Obstacle.Bush => 1.4,
                _ => 1.1,
            };
            list.Add(new Hazard(kind, px, py, radius, height, (n * 37) % 180));
        }
        return list;
    }

    /// <summary>Register the boulders and the driftwood as solids — a bush you can mow, a rock you cannot.</summary>
    public static void RegisterSolids()
    {
        foreach (Hazard hz in Hazards())
        {
            if (hz.Kind == Obstacle.Bush) continue;                  // a bush slows you; it does not stop you
            double gh = PlainHeightAt(hz.X, hz.Y);
            WorldSolids.Boxes.Add(new WorldSolids.Box(hz.X, hz.Y, hz.RadiusM, hz.RadiusM * 0.5,
                                                      gh - 0.5, gh + hz.HeightM));
        }
    }
}

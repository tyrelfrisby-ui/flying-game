using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

// The seaside half of the world (owner 2026-10-03): "compress the map… make the combat zone go over the gorge and put a
// bridge that looks like the Golden Gate over it… more twists and turns… a city with tall buildings… an aircraft carrier
// just off shore… an island that looks like Catalina with a runway… an arch big enough to fly through… Avalon… a cave
// you can water taxi inside of with a sandy beach, an opening that lets light in at the top and a wrecked pirate ship."
//
// Frame: sim x = north, y = east, heights UP (m above sea level). The Valley tableland (WorldTerrain.DatumM, 500 ft)
// ends in sea cliffs ~5 km east of the Valley airport; the river leaves its gorge through a cleft in those cliffs (the
// Golden Gate); the island lies ~3.5 km offshore, long axis north–south like Catalina's NW–SE.

/// <summary>The mainland coast: tableland → sea cliff → a narrow sand beach → the continental shelf.</summary>
public static class Coast
{
    public const double SeaLevelM = 0.0;
    public const double MeanShoreY = 5200.0;
    public const double CliffRunM = 90.0, BeachM = 28.0, BeachTopM = 2.2;

    /// <summary>The waterline's y (east) at x: a wandering coast of headlands and coves.</summary>
    public static double ShoreY(double x) => MeanShoreY + 240.0 * System.Math.Sin(x / 1400.0 + 0.7) + 90.0 * System.Math.Sin(x / 530.0 + 2.0);

    /// <summary>Seaward of the cliff foot: the sea's surface applies where the ground is below it.</summary>
    public static bool IsSea(double x, double y) => y > ShoreY(x) - BeachM;

    /// <summary>The coast's cap on terrain height (+∞ inland of the cliff top). <paramref name="plainH"/> = the tableland
    /// height there. Cliff: rounded top, near-vertical foot (75°); beach: sand from the cliff foot to the waterline;
    /// shelf: 5.5 % down to 65 m.</summary>
    public static double ProfileAt(double x, double y, double plainH)
    {
        double d = y - ShoreY(x);
        double cliffTop = -(CliffRunM + BeachM);
        if (d <= cliffTop) return double.PositiveInfinity;
        if (d < -BeachM)
        {
            double t = (d - cliffTop) / CliffRunM;
            return BeachTopM + (plainH - BeachTopM) * (1 - System.Math.Pow(t, 2.2));
        }
        if (d < 0) return -0.3 + (BeachTopM + 0.3) * (-d / BeachM);
        return -0.3 - System.Math.Min(65.0, 0.055 * d);
    }

    public static bool IsBeach(double x, double y) { double d = y - ShoreY(x); return d >= -BeachM && d < 3; }
}

/// <summary>
/// A Santa Catalina–like island: a long, steep, mountainous ridge rising straight out of the sea (cliffs all round), in
/// two parts — the main body, and the "West End" beyond where Catalina's isthmus is. Owner: instead of the isthmus, a
/// SEA ARCH big enough to fly through joins them. On the main ridge, "Airport in the Sky" (the real one: 1,602 ft,
/// 3,000 ft runway on a ridge top with drop-offs at both ends); at the south end on the mainland-facing side, AVALON in
/// its crescent bay with the round Casino on the bay's north point; south of Avalon, a sea cave.
/// </summary>
public static class Island
{
    public readonly struct Lobe
    {
        public readonly double Cx, Cy, Hx, Hy, PeakM, Exponent;
        public Lobe(double cx, double cy, double hx, double hy, double peak, double exp) { Cx = cx; Cy = cy; Hx = hx; Hy = hy; PeakM = peak; Exponent = exp; }
    }

    /// <summary>Main body (south) and the West End (north) — the gap between their tips is the arch's channel.</summary>
    public static readonly Lobe Main = new(600, 9450, 2700, 850, 600, 2.6), WestEnd = new(4520, 9520, 960, 520, 380, 2.4);
    public const double SeaFloorM = -30.0;
    public static double MinX => Main.Cx - Main.Hx - 300;
    public static double MaxX => WestEnd.Cx + WestEnd.Hx + 300;
    public static double MinY => System.Math.Min(Main.Cy - Main.Hy, WestEnd.Cy - WestEnd.Hy) - 300;
    public static double MaxY => System.Math.Max(Main.Cy + Main.Hy, WestEnd.Cy + WestEnd.Hy) + 300;
    public static bool InBounds(double x, double y) => x > MinX && x < MaxX && y > MinY && y < MaxY;

    // ---- Airport in the Sky (real: 1,602 ft, runway 4/22 3,000 ft x 100 ft on a ridge) ----
    public const double RunwayX = 1350, RunwayY = 9470, RunwayElevM = 488.3, RunwayLengthM = 914.4, RunwayWidthM = 30.5;
    public const double RunwayPadHalfX = RunwayLengthM / 2 + 60, RunwayPadHalfY = 75, RunwayBlendM = 140;
    public static bool OnRunway(double x, double y) => System.Math.Abs(x - RunwayX) <= RunwayLengthM / 2 && System.Math.Abs(y - RunwayY) <= RunwayWidthM / 2;

    // ---- Avalon bay (a crescent cove on the mainland side near the south end) ----
    public const double AvalonX = -1450, AvalonCoveR = 330;
    public static double AvalonY => _avalonY ??= WestShoreY(AvalonX) - 40;
    private static double? _avalonY;
    /// <summary>The round Casino on the bay's north point: real one ~55 m across, ~42 m tall (white, red-tile roof).</summary>
    public static (double x, double y) CasinoXY => (AvalonX + AvalonCoveR * 0.98, AvalonY + AvalonCoveR * 0.22);
    public const double CasinoRadiusM = 27, CasinoHeightM = 42;

    /// <summary>The island's waterline on its west (mainland) side at x, from the main lobe's shape (solved once per x).</summary>
    public static double WestShoreYPlain(double x) => WestShoreY(x);
    public static double WestShoreY(double x)
    {
        double lo = Main.Cy - Main.Hy - 50, hi = Main.Cy;
        for (int i = 0; i < 40; i++) { double m = 0.5 * (lo + hi); if (LobeHeight(Main, x, m) < 0) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    private static double LobeHeight(in Lobe l, double x, double y)
    {
        double u = System.Math.Abs(x - l.Cx) / l.Hx;
        double wander = 70 * System.Math.Sin(x / 900.0 + 0.4);
        double v = (y - l.Cy - wander) / l.Hy;
        double r2 = System.Math.Pow(u, l.Exponent) + v * v;
        if (r2 >= 1.3) return SeaFloorM - 10;
        double k = System.Math.Max(0, 1 - r2);
        double h = (l.PeakM - SeaFloorM) * System.Math.Pow(k, 0.75) + SeaFloorM;
        // Ridges and gullies on the flanks (Catalina's canyons run down to the sea).
        h += k * (34 * System.Math.Sin(x / 210.0) * System.Math.Sin(y / 170.0) + 18 * System.Math.Sin(x / 97.0 + y / 61.0));
        return h;
    }

    /// <summary>The headland the sea cave is cut into: a rounded bluff rising ~55 m above the slope.</summary>
    private static double HeadlandBump(double x, double y)
    {
        double hx = SeaCave.HeadlandX, hy = SeaCave.HeadlandY;
        double d2 = ((x - hx) * (x - hx) + (y - hy) * (y - hy)) / (140.0 * 140.0);
        return d2 > 9 ? 0 : 55 * System.Math.Exp(-d2);
    }

    /// <summary>Island ground height, or null outside the island's footprint (open sea). <paramref name="withCave"/> =
    /// false gives the hillside OVER the sea cave (its rock roof's outer surface).</summary>
    public static double? HeightAt(double x, double y, bool withCave = true)
    {
        if (!InBounds(x, y)) return null;
        double h = System.Math.Max(LobeHeight(Main, x, y), LobeHeight(WestEnd, x, y));
        if (h > -20) h += HeadlandBump(x, y) * System.Math.Clamp((h + 20) / 30, 0, 1);

        // Airport in the Sky: the ridge top cut / filled flat to field elevation (fills fall away as embankments —
        // the real runway ends at drop-offs).
        {
            double dx = System.Math.Max(0, System.Math.Abs(x - RunwayX) - RunwayPadHalfX);
            double dy = System.Math.Max(0, System.Math.Abs(y - RunwayY) - RunwayPadHalfY);
            double d = System.Math.Sqrt(dx * dx + dy * dy);
            if (d < RunwayBlendM) { double w = 1 - d / RunwayBlendM; w = w * w * (3 - 2 * w); h += (RunwayElevM - h) * w; }
        }

        // Avalon's cove: a bowl of sheltered water, with the town's hillside rising gently behind it.
        {
            double cy = AvalonY;
            double d = System.Math.Sqrt((x - AvalonX) * (x - AvalonX) + (y - cy) * (y - cy));
            if (d < AvalonCoveR) h = System.Math.Min(h, -9 + 9 * System.Math.Pow(d / AvalonCoveR, 3) + 0.0);
            else if (d < AvalonCoveR + 420 && x > AvalonX - AvalonCoveR * 0.6)
                h = System.Math.Min(h, 1.5 + (d - AvalonCoveR) * 0.28);   // ~16° town slope behind the bay (not the headland south of it)
        }

        if (withCave && SeaCave.InFootprint(x, y)) h = SeaCave.FloorAt(x, y);
        return h;
    }

    /// <summary>Paved runway on the island (wheels).</summary>
    public static bool IsPaved(double x, double y) => OnRunway(x, y);
}

/// <summary>
/// The sea cave south of Avalon (owner): water-taxi in through its mouth in the cliff, a sandy beach at the back, a
/// SKYLIGHT hole in the roof letting a shaft of sun in, a wrecked pirate ship on the sand. A domed chamber (half-
/// ellipsoid) cut into the cliff: its seaward edge pokes out past the cliff face, which opens the MOUTH — an arch of
/// rock ~25 m high and ~70 m wide at the waterline (a Beaver spans 15 m). The rock roof between the dome and the hillside
/// is a solid (<see cref="WorldSolids"/>); the floor is the terrain (water, then sand).
/// </summary>
public static class SeaCave
{
    public const double RadiusM = 75, DomeM = 40, FloorWaterM = -5.0, BeachTopM = 1.4;
    /// <summary>The mouth: an arched opening through the seaward wall, 40 m wide, 24 m high at the crown.</summary>
    public const double MouthHalfWidthM = 20, MouthHeightM = 24;
    public const double SkylightRadiusM = 10, SkylightOffsetM = 22;   // hole in the roof, a little inland of centre
    public const double MinRoofM = 3.0;

    /// <summary>Chamber centre: inland of the island's west shore at the cave's x so the dome's edge breaks the cliff.
    /// (Computed from the shore WITHOUT the headland bump, which is itself placed off this centre.)</summary>
    public static double Cx => Island.AvalonX - 420;
    public static double Cy => Island.WestShoreYPlain(Cx) + RadiusM * 0.82;
    public static double HeadlandX => Cx;
    public static double HeadlandY => Cy + 45;
    private static double _cx = double.NaN, _cy;
    private static void Ensure() { if (double.IsNaN(_cx)) { _cx = Cx; _cy = Cy; } }

    /// <summary>Inland direction = +y. The beach is the back (landward) third of the floor.</summary>
    public static bool InFootprint(double x, double y)
    {
        Ensure();
        double dx = x - _cx, dy = y - _cy;
        return dx * dx + dy * dy < RadiusM * RadiusM;
    }

    public static double FloorAt(double x, double y)
    {
        Ensure();
        double back = (y - _cy) / RadiusM;              // -1 sea side .. +1 back wall
        double sand = System.Math.Clamp((back - 0.15) / 0.35, 0, 1);
        sand = sand * sand * (3 - 2 * sand);
        return FloorWaterM + (BeachTopM - FloorWaterM) * sand + 1.2 * System.Math.Max(0, back - 0.5);
    }

    public static bool IsSand(double x, double y) { Ensure(); return InFootprint(x, y) && (y - _cy) / RadiusM > 0.25; }

    /// <summary>Dome ceiling height above sea level at (x, y) (floor-level at the rim) — raised to the mouth's arch in
    /// the seaward wall so the opening runs right through to the sea.</summary>
    public static double CeilingAt(double x, double y)
    {
        Ensure();
        double r2 = ((x - _cx) * (x - _cx) + (y - _cy) * (y - _cy)) / (RadiusM * RadiusM);
        double dome = FloorWaterM + (DomeM - FloorWaterM) * System.Math.Sqrt(System.Math.Max(0, 1 - r2));
        return System.Math.Max(dome, MouthArchAt(x, y));
    }

    /// <summary>Crown height of the mouth's arch at (x, y) (−∞ outside the mouth passage).</summary>
    public static double MouthArchAt(double x, double y)
    {
        Ensure();
        if (y - _cy > -RadiusM * 0.25) return double.NegativeInfinity;
        double u = (x - _cx) / MouthHalfWidthM;
        if (System.Math.Abs(u) >= 1) return double.NegativeInfinity;
        return MouthHeightM * System.Math.Sqrt(1 - u * u);
    }

    public static (double x, double y) Skylight { get { Ensure(); return (_cx, _cy + SkylightOffsetM); } }
    public static bool InSkylight(double x, double y) { var s = Skylight; return (x - s.x) * (x - s.x) + (y - s.y) * (y - s.y) < SkylightRadiusM * SkylightRadiusM; }

    /// <summary>Rock exists between the dome and the hillside above it (except through the skylight, and where the
    /// hillside is lower than the dome — that gap is the mouth).</summary>
    public static bool RoofAt(double x, double y, out double ceiling, out double outer)
    {
        ceiling = CeilingAt(x, y);
        outer = Island.HeightAt(x, y, withCave: false) ?? -100;
        return InFootprint(x, y) && !InSkylight(x, y) && outer > ceiling + MinRoofM;
    }

    /// <summary>The pirate wreck: on the sand, keeled over, bow toward the water.</summary>
    public static (double x, double y, double headingDeg) Wreck { get { Ensure(); return (_cx - 18, _cy + RadiusM * 0.55, -100); } }

    /// <summary>The roof as a solid for airframe contact.</summary>
    public sealed class RoofSolid : WorldSolids.IShape
    {
        public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
        {
            normalNed = Vec3.Zero; depth = 0;
            if (!InFootprint(x, y) || !RoofAt(x, y, out double c, out double o) || up < c || up > o) return false;
            double down = up - c, upward = o - up;
            if (down <= upward) { normalNed = new Vec3(0, 0, 1); depth = down; }   // pushed DOWN out of the roof
            else { normalNed = new Vec3(0, 0, -1); depth = upward; }
            return true;
        }
    }
}

/// <summary>The sea arch where Catalina's isthmus would be: rock spanning the channel between the main body and the
/// West End. Opening ~150 m high at the centre over ~280 m of water — big enough for anything in the hangar.</summary>
public static class SeaArch
{
    public static double X0 => Island.Main.Cx + Island.Main.Hx - 40;     // south abutment
    public static double X1 => Island.WestEnd.Cx - Island.WestEnd.Hx + 40;  // north abutment
    public static double Cx => 0.5 * (X0 + X1);
    public static double Cy => 0.5 * (Island.Main.Cy + Island.WestEnd.Cy) + 30;
    public const double OpeningM = 150, ThickM = 34, HalfWidthYM = 32;
    public static double HalfSpan => 0.5 * (X1 - X0);
    /// <summary>Underside of the arch at x (m above the sea).</summary>
    public static double InnerAt(double x) { double u = (x - Cx) / HalfSpan; return OpeningM * (1 - u * u); }

    public static void RegisterSolids()
    {
        const double seg = 12;
        for (double x = X0; x < X1; x += seg)
        {
            double xm = x + seg / 2;
            double inner = System.Math.Max(0, InnerAt(xm));
            WorldSolids.Boxes.Add(new WorldSolids.Box(xm, Cy, seg / 2, HalfWidthYM, inner, inner + ThickM + 20 * System.Math.Pow((xm - Cx) / HalfSpan, 2)));
        }
    }
}

/// <summary>The Golden Gate–style suspension bridge across the gorge's mouth (owner): two Art-Deco towers standing in the
/// water near the walls, the deck at the cliff tops (~150 m over the water — fly under it), main cables sagging between
/// the tower tops and anchored on the tableland beyond, International Orange.</summary>
public static class GoldenGate
{
    /// <summary>Bridge line (y): 300 m inland of where the river meets the coast.</summary>
    public static double Y { get { Init(); return _y; } }
    public static double CentreX { get { Init(); return _cx; } }
    public static double HalfMainSpanM { get { Init(); return _half; } }
    public const double DeckHalfWidthM = 14, DeckThickM = 8, TowerAboveDeckM = 150, TowerLegM = 10, AnchorBackM = 340;
    public static double DeckTopM => WorldTerrain.DatumM - 2;
    public static double TowerTopM => DeckTopM + TowerAboveDeckM;
    private static double _y = double.NaN, _cx, _half;

    private static void Init()
    {
        if (!double.IsNaN(_y)) return;
        // Where the river crosses the coastline: iterate y = ShoreY(RiverCentreX(y)).
        double y = Coast.MeanShoreY;
        for (int i = 0; i < 40; i++) y = Coast.ShoreY(WorldTerrain.RiverCentreX(y));
        _y = y - 300;
        _cx = WorldTerrain.RiverCentreX(_y);
        _half = WorldTerrain.GorgeHalfWidthAt(_y) - 70;   // towers in the water just off each wall
    }

    /// <summary>Main-cable height at x (parabola from tower tops to 6 m over the deck at midspan; straight backstays).</summary>
    public static double CableAt(double x)
    {
        double dx = System.Math.Abs(x - CentreX);
        if (dx <= HalfMainSpanM) { double u = dx / HalfMainSpanM; return DeckTopM + 6 + (TowerTopM - DeckTopM - 6) * u * u; }
        double t = System.Math.Clamp((dx - HalfMainSpanM) / AnchorBackM, 0, 1);
        return TowerTopM + (DeckTopM + 2 - TowerTopM) * t;
    }

    public static void RegisterSolids()
    {
        double halfLen = HalfMainSpanM + AnchorBackM;
        // Deck (in 40 m slabs) from anchorage to anchorage.
        for (double x = CentreX - halfLen; x < CentreX + halfLen; x += 40)
            WorldSolids.Boxes.Add(new WorldSolids.Box(x + 20, Y, 20, DeckHalfWidthM, DeckTopM - DeckThickM, DeckTopM));
        // Towers: two legs each (either side of the deck) from the water to the top, portal struts between.
        foreach (double s in new[] { -1.0, 1.0 })
        {
            double tx = CentreX + s * HalfMainSpanM;
            foreach (double side in new[] { -1.0, 1.0 })
                WorldSolids.Boxes.Add(new WorldSolids.Box(tx, Y + side * (DeckHalfWidthM + TowerLegM / 2), TowerLegM / 2, TowerLegM / 2, -5, TowerTopM));
            foreach (double z in new[] { DeckTopM + 50, DeckTopM + 100, TowerTopM - 8 })
                WorldSolids.Boxes.Add(new WorldSolids.Box(tx, Y, TowerLegM / 2, DeckHalfWidthM + TowerLegM, z, z + 7));
        }
        // Main cables (one each side): 1.5 m thick boxes every 15 m along the sag.
        foreach (double side in new[] { -1.0, 1.0 })
            for (double x = CentreX - halfLen; x < CentreX + halfLen; x += 15)
            {
                double c = CableAt(x + 7.5);
                WorldSolids.Boxes.Add(new WorldSolids.Box(x + 7.5, Y + side * (DeckHalfWidthM + TowerLegM / 2), 7.5, 1.0, c - 1.0, c + 1.0));
            }
    }
}

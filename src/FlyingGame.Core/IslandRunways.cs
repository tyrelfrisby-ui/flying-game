namespace FlyingGame.Core;

/// <summary>
/// The island's own runways, served as the <see cref="WorldTerrain.RunwayEnd"/> that the spawn, tow,
/// approach and practice paths already speak. The world authors each field's real centre, true heading,
/// length, width, surface and slope (<see cref="IslandField"/>); this reads them and chooses an end, so
/// nothing downstream has to know which world it is flying in.
///
/// It exists because the legacy template — one 2,400 m runway along +x at every field, with a crosswind
/// runway along +y — was still being applied to the island's airport centres. At PHDH that put the start
/// 1,570 m south of the field at pad elevation, 785 m inside a hillside; at KMYL it put it 1,137 m in the
/// air. An aeroplane spawned inside terrain takes LandingGear.Compute to NaN on its first step, so the sim
/// throws every frame: the controls do nothing, the airframe never moves, and the chase camera — which may
/// not go below ground — climbs to the surface above and looks down at an aeroplane it can no longer reach.
/// That is the whole of "wrong view, dead controls".
///
/// The geometry is Codex's and is only ever read here. Which end to use is the part I own.
/// </summary>
public static class IslandRunways
{
    /// <summary>A water lane is not a runway: the floatplane paths pick a lake or a seaplane lane themselves.</summary>
    public static bool IsRunway(IslandField f) => f.Surface != "water" && f.Length > 0 && f.Width > 0;

    /// <summary>The island's surface names in the vocabulary the rest of the game already uses, so
    /// <see cref="WorldTerrain.IsPaved"/> keeps answering correctly. The runway's own numbers ride along so
    /// two runways at one field are distinguishable (KMEV 17/35 and 13/31 are different runways; KSNA 2L and
    /// 2R are parallel and are not).</summary>
    private static string KindOf(IslandField f) => f.Surface switch
    {
        "asphalt" or "concrete" => "paved",
        "gravel" or "dirt" or "hard sand" => "gravel",
        _ => "grass",
    };

    /// <summary>Every landable runway at this airport, both ends of each.</summary>
    public static List<WorldTerrain.RunwayEnd> Ends(WorldTerrain.Airport a)
    {
        var ends = new List<WorldTerrain.RunwayEnd>();
        foreach (IslandField f in IslandSites.Fields)
        {
            if (f.Field != a.Name || !IsRunway(f)) continue;
            var strip = new WorldTerrain.Strip(KindOf(f) + ":" + f.Numbers,
                f.Centre.X - a.X, f.Centre.Y - a.Y, f.Length, f.Width, f.Heading);
            double h = f.Heading * Math.PI / 180;
            ends.Add(new WorldTerrain.RunwayEnd(strip, h, f.Centre.X, f.Centre.Y));
            ends.Add(new WorldTerrain.RunwayEnd(strip, h + Math.PI, f.Centre.X, f.Centre.Y));
        }
        return ends;
    }

    /// <summary>The runway axis in degrees, 0–180: two ends of one runway share it, and it is what tells a
    /// genuinely crosswind runway from the other end of the same one.</summary>
    private static double Axis(WorldTerrain.RunwayEnd e)
    {
        double d = e.HeadingRad * 180 / Math.PI % 180;
        return d < 0 ? d + 180 : d;
    }

    private static bool SameRunway(WorldTerrain.RunwayEnd x, WorldTerrain.RunwayEnd y)
    {
        double d = Math.Abs(Axis(x) - Axis(y));
        return Math.Min(d, 180 - d) < 30;   // parallel runways are the same choice to a pilot picking into wind
    }

    /// <summary>The end the world itself starts aeroplanes from, when it has authored one: in calm air that is
    /// the answer, rather than a convention of mine about which way is "into the wind".</summary>
    private static int AuthoredEnd(WorldTerrain.Airport a, List<WorldTerrain.RunwayEnd> ends)
    {
        double best = double.MaxValue;
        int pick = -1;
        foreach (IslandSites.Start s in IslandSites.Starts)
        {
            if (s.Airport != a.Name || s.Mode != "OnTheRunway") continue;
            for (int i = 0; i < ends.Count; i++)
            {
                (double sx, double sy) = ends[i].Start;
                double d = (sx - s.X) * (sx - s.X) + (sy - s.Y) * (sy - s.Y);
                if (d < best) { best = d; pick = i; }
            }
        }
        return best <= 100 * 100 ? pick : -1;   // within 100 m: the same end, not a different runway
    }

    /// <summary>The runway end to use at an island airport. Headwind: the end of any runway best aligned into
    /// the wind, the longer runway breaking a tie between parallels. Crosswind: the best end of a runway on a
    /// different axis, or — at a field with only one runway — its other end, which is what the four-airport
    /// world meant by the crosswind choice.</summary>
    public static WorldTerrain.RunwayEnd Choose(WorldTerrain.Airport a, double windFromRad, bool headwind, double windSpeedMs = 1.0)
    {
        List<WorldTerrain.RunwayEnd> ends = Ends(a);
        if (ends.Count == 0) return WorldTerrain.ChooseTemplateRunway(a, windFromRad, headwind, windSpeedMs);

        if (windSpeedMs < 0.3)
        {
            int authored = AuthoredEnd(a, ends);
            if (authored >= 0)
            {
                if (headwind) return ends[authored];
                foreach (WorldTerrain.RunwayEnd e in ends) if (!SameRunway(e, ends[authored])) return e;
                foreach (WorldTerrain.RunwayEnd e in ends)
                    if (e.Strip.Kind == ends[authored].Strip.Kind && Math.Abs(e.HeadingRad - ends[authored].HeadingRad) > 0.1)
                        return e;   // one runway only: the crosswind choice is its other end
                return ends[authored];
            }
        }

        WorldTerrain.RunwayEnd best = ends[0];
        double bestF = double.MinValue, bestLen = 0;
        foreach (WorldTerrain.RunwayEnd e in ends)
        {
            double f = e.HeadwindFactor(windFromRad);
            if (f > bestF + 1e-9 || (Math.Abs(f - bestF) <= 1e-9 && e.LengthM > bestLen))
            { bestF = f; bestLen = e.LengthM; best = e; }
        }
        if (headwind) return best;

        WorldTerrain.RunwayEnd pick = best;
        double pickF = double.MinValue;
        bool found = false;
        foreach (WorldTerrain.RunwayEnd e in ends)
        {
            if (SameRunway(e, best)) continue;
            double f = e.HeadwindFactor(windFromRad);
            if (!found || f > pickF + 1e-9) { pickF = f; pick = e; found = true; }
        }
        if (found) return pick;
        foreach (WorldTerrain.RunwayEnd e in ends)
            if (SameRunway(e, best) && Math.Abs(e.HeadingRad - best.HeadingRad) > 0.1) return e;
        return best;
    }

    /// <summary>The water a seaplane starts from: the island's own float lanes (fields with surface "water") and its
    /// authored lakes, whichever is nearest the field and long enough for the hull; failing those, the open sea.
    ///
    /// `WorldTerrain.Lakes` was the four-airport world's water the way `AirportStrips` was its land — one lake
    /// 3 km south-west of every field at the field's elevation less 2 m. On the island that is a hillside: at PHDH
    /// the lake sits at 2.3 m where the ground is 450.1 m and there is no water at all, so a floatplane was
    /// spawned 448 m inside a hill, and the flying boats' harbour stands 1,529 m up a mountain.</summary>
    /// <summary>How far out of its way a seaplane will go for every metre of climb. The field's own water is the
    /// water at the field's own height: nearest-by-distance alone sent PHDH's Cub, which lives at 4 m by the sea,
    /// to an alpine tarn 2,030 m up a mountain 5 km inland because it was 3 km closer than the beach.</summary>
    private const double MetresPerMetreOfClimb = 10.0;

    public static WorldTerrain.Lake? Water(WorldTerrain.Airport a, double minLengthM)
    {
        WorldTerrain.Lake? best = null;
        double bestScore = double.MaxValue;

        void Consider(double cx, double cy, double rx, double ry, double surfaceM)
        {
            if (2 * Math.Max(rx, ry) < minLengthM) return;
            double? w = WorldTerrain.Active?.WaterSurfaceAt(cx, cy, surfaceM);
            if (!w.HasValue) return;
            double score = Math.Sqrt((cx - a.X) * (cx - a.X) + (cy - a.Y) * (cy - a.Y))
                         + MetresPerMetreOfClimb * Math.Abs(w.Value - a.ElevationM);
            if (score < bestScore) { bestScore = score; best = new WorldTerrain.Lake(cx, cy, rx, ry, w.Value); }
        }

        foreach (IslandField f in IslandSites.Fields)
        {
            if (f.Surface != "water") continue;
            double h = f.Heading * Math.PI / 180, c = Math.Abs(Math.Cos(h)), sn = Math.Abs(Math.Sin(h));
            Consider(f.Centre.X, f.Centre.Y, (f.Length * c + f.Width * sn) / 2, (f.Length * sn + f.Width * c) / 2, f.Elevation);
        }
        foreach (IslandValue q in IslandTerrain.World["water"].Items)
        {
            IslandPoint p = q["centre"].Point;
            double h = q.N("long_axis_true_deg", 90) * Math.PI / 180, c = Math.Abs(Math.Cos(h)), sn = Math.Abs(Math.Sin(h));
            double la = q["radii_km"][0].Number() * 1000, sa = q["radii_km"][1].Number() * 1000;
            Consider(p.X, p.Y, la * c + sa * sn, la * sn + sa * c, q.N("elevation_ft") * IslandTerrain.Feet);
        }
        // The sea is a candidate, not only a fallback: for a field on the coast it is the right water even when a
        // lake lies closer, and for a flying boat it is the only water long enough anywhere on the island.
        WorldTerrain.Lake? sea = Sea(a, minLengthM);
        if (sea.HasValue) Consider(sea.Value.Cx, sea.Value.Cy, sea.Value.Rx, sea.Value.Ry, sea.Value.SurfaceM);
        return best;
    }

    /// <summary>Open sea at sea level with the bed below it — what a flying boat needs when no authored lake is
    /// long enough for it. Found by walking outward from the field, not authored here: the coast is Codex's.</summary>
    private static bool IsOpenSea(double x, double y)
    {
        double? w = WorldTerrain.Active?.WaterSurfaceAt(x, y, 0);
        return w.HasValue && Math.Abs(w.Value) < 0.5 && WorldTerrain.GroundHeightAt(x, y) < -1;
    }

    private static WorldTerrain.Lake? Sea(WorldTerrain.Airport a, double minLengthM)
    {
        for (double r = 2000; r <= 26000; r += 500)
            for (int i = 0; i < 24; i++)
            {
                double th = i * Math.PI / 12, x = a.X + r * Math.Cos(th), y = a.Y + r * Math.Sin(th);
                if (!IsOpenSea(x, y)) continue;
                bool clear = true;
                for (double d = -minLengthM / 2; d <= minLengthM / 2 && clear; d += 100)
                    for (double c = -250; c <= 250 && clear; c += 125)
                        clear = IsOpenSea(x + d * Math.Cos(th) - c * Math.Sin(th), y + d * Math.Sin(th) + c * Math.Cos(th));
                if (clear) return new WorldTerrain.Lake(x, y, Math.Max(minLengthM / 2, 400), 250, 0.0);
            }
        return null;
    }

    /// <summary>The start the world authored for this airport and mode ("OnTheRunway", "OnFinal", "InTheAir"),
    /// or null. Consuming these rather than deriving a start keeps the eight starts exactly where the world
    /// put them, slope, displaced threshold and all.</summary>
    public static IslandSites.Start? Authored(string airport, string mode)
    {
        foreach (IslandSites.Start s in IslandSites.Starts)
            if (s.Airport == airport && s.Mode == mode) return s;
        return null;
    }

    /// <summary>The authored start for a mode that belongs to no airport ("InAeroBox", "InCombatZone",
    /// "InThermal"), nearest to <paramref name="nearX"/>,<paramref name="nearY"/>; or null.</summary>
    public static IslandSites.Start? AuthoredNearest(string mode, double nearX, double nearY)
    {
        IslandSites.Start? pick = null;
        double best = double.MaxValue;
        foreach (IslandSites.Start s in IslandSites.Starts)
        {
            if (s.Mode != mode) continue;
            double d = (s.X - nearX) * (s.X - nearX) + (s.Y - nearY) * (s.Y - nearY);
            if (d < best) { best = d; pick = s; }
        }
        return pick;
    }
}

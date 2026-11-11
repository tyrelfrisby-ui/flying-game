using System;
using System.Collections.Generic;

namespace FlyingGame.Core;

/// <summary>
/// Where the low weather goes (Ty 2026-11-05 08:39: "some of the whispy layers and puffy clouds at lower altitudes too
/// just patches here and there ... some thin fog over some grass runways and lakes on occasion"). Pure placement, no
/// rendering: the Bridge's CloudPlayground and GroundFog draw what this picks. The fog sites are only where fog CAN lie
/// (every grass strip, every lake, the valley floors that pool cold air); whether it does, and how thick, is the island
/// weather's call (Weather.FogModel, Ty 08:41: "fog tends to sit in valleys"). Seeded and deterministic, so every
/// device (iOS, Mac, Quest) and every session sees the same sky. Positions are Unity world metres (x east, z north,
/// y up = altitude MSL); headings are degrees true.
/// </summary>
public static class CloudSites
{
    public const int Seed = 1105;

    /// <summary>One fog volume: an oriented box sitting on a grass strip or a lake.</summary>
    public readonly struct FogSite
    {
        public readonly string Id;
        public readonly bool Lake;
        /// <summary>Unity x (east) and z (north) of the centre, m.</summary>
        public readonly double X, Z;
        /// <summary>Half the extent along the long axis and across it, m (an ellipse for a lake, a box for a strip).</summary>
        public readonly double HalfLength, HalfWidth;
        /// <summary>Long axis, degrees true.</summary>
        public readonly double HeadingDeg;
        /// <summary>The surface it lies on (ground or water), m MSL, and how deep the fog is above it, m.</summary>
        public readonly double SurfaceM, DepthM;
        public FogSite(string id, bool lake, double x, double z, double halfLength, double halfWidth, double headingDeg, double surfaceM, double depthM)
        { Id = id; Lake = lake; X = x; Z = z; HalfLength = halfLength; HalfWidth = halfWidth; HeadingDeg = headingDeg; SurfaceM = surfaceM; DepthM = depthM; }
    }

    /// <summary>One low patch: a small puffy cumulus cluster or a thin wisp sheet, 150-600 m above the ground under it.</summary>
    public readonly struct LowPatch
    {
        public readonly bool Puffy;
        public readonly double X, Z;
        /// <summary>Cloud base (puffy) or sheet height (wisp), m MSL, and its height above the highest ground under it.</summary>
        public readonly double BaseM, AglM;
        /// <summary>Radius of the patch, m.</summary>
        public readonly double RadiusM;
        /// <summary>Per-patch seed for its lobes / its noise offset.</summary>
        public readonly int PatchSeed;
        public LowPatch(bool puffy, double x, double z, double baseM, double aglM, double radiusM, int patchSeed)
        { Puffy = puffy; X = x; Z = z; BaseM = baseM; AglM = aglM; RadiusM = radiusM; PatchSeed = patchSeed; }
    }

    /// <summary>Valley pools: the deepest valley floors (≥ <see cref="ValleyMinDepthM"/> below their surroundings), at
    /// most <see cref="ValleyMax"/>, <see cref="ValleySpacingM"/> apart and clear of the strips and lakes.</summary>
    public const double ValleyMinDepthM = 80, ValleySpacingM = 3000, ValleyRadiusM = 700, ValleyGridM = 1000;
    public const int ValleyMax = 8;
    public const double MinAglM = 150, MaxAglM = 600;

    /// <summary>Stable hash in [0, 1) of a name and the seed (FNV-1a; string.GetHashCode is randomised per process).</summary>
    public static double Hash01(string s, int salt)
    {
        uint h = 2166136261u ^ (uint)(Seed * 31 + salt);
        foreach (char c in s) { h ^= c; h *= 16777619u; }
        h ^= h >> 16; h *= 0x85ebca6bu; h ^= h >> 13; h *= 0xc2b2ae35u; h ^= h >> 16;   // murmur3 fmix32
        return (h & 0xFFFFFF) / (double)0x1000000;
    }

    private static List<FogSite>? _fog;
    private static List<LowPatch>? _low;

    /// <summary>The fog volumes for the island world (built once).</summary>
    public static IReadOnlyList<FogSite> Fog => _fog ??= PickFog();
    /// <summary>The low patches for the island world (built once; needs the island's ground).</summary>
    public static IReadOnlyList<LowPatch> Low => _low ??= PickLow(GroundTop);

    private static List<FogSite> PickFog()
    {
        var list = new List<FogSite>();
        var strips = new List<IslandField>();
        foreach (IslandField f in IslandTerrain.Fields) if (StripFinish.LiesNaturally(f.Surface) && f.Field != "DRAKE") strips.Add(f);   // grass and turf; not the cave strip
        foreach (IslandField f in strips)
        {
            // The strip and a margin round it, so the fog spills off the edges onto the field.
            double surface = WorldTerrain.Active != null ? WorldTerrain.GroundHeightAt(f.Centre.X, f.Centre.Y) : f.SurfaceHeight(f.Centre);
            double depth = 5 + 4 * Hash01(f.Id, 2);     // thin: a vortex shed a wingspan up reaches its top
            list.Add(new FogSite(f.Id, false, f.Centre.E * 1000, f.Centre.N * 1000, f.Length / 2 + 90, f.Width / 2 + 70,
                f.Heading, surface, depth));
        }
        var lakes = new List<IslandValue>();
        foreach (IslandValue q in IslandTerrain.World["water"].Items) if (q.Has("radii_km")) lakes.Add(q);
        foreach (IslandValue q in lakes)
        {
            string id = q.S("id");
            double rx = q["radii_km"][0].Number() * 1000, ry = q["radii_km"][1].Number() * 1000;
            double depth = 6 + 5 * Hash01(id, 4);
            IslandPoint c = q["centre"].Point;
            list.Add(new FogSite(id, true, c.E * 1000, c.N * 1000, rx, ry, q.N("long_axis_true_deg", 90),
                q.N("elevation_ft") * IslandTerrain.Feet, depth));
        }
        if (WorldTerrain.Active != null) list.AddRange(PickValleys(WorldTerrain.GroundHeightAt, list));
        return list;
    }

    /// <summary>The valley floors where a calm, clear night pools its cold air (<see cref="Weather.FogModel.ValleyDepth"/>
    /// on a 1 km grid over the main island, dry land only), deepest first. <paramref name="ground"/> is sim (north, east).
    /// The rings are read off a 500 m grid of the ground sampled once (6,561 lookups, not the 40,000 the exact rings at
    /// every node would cost: a build-time stall on the Quest); 500 m is well inside the rings' 1.5 and 3 km.</summary>
    public static List<FogSite> PickValleys(Func<double, double, double> ground, List<FogSite> taken)
    {
        const double half = 17000, step = 500, reach = half + Weather.FogModel.RingOuterM;
        int n0 = (int)(2 * reach / step) + 1;
        var g = new double[n0, n0];
        for (int i = 0; i < n0; i++)
        for (int j = 0; j < n0; j++)
            g[i, j] = ground(-reach + i * step, -reach + j * step);
        double Grid(double north, double east)
        {
            double fi = (north + reach) / step, fj = (east + reach) / step;
            int i = Math.Clamp((int)Math.Floor(fi), 0, n0 - 2), j = Math.Clamp((int)Math.Floor(fj), 0, n0 - 2);
            double ti = fi - i, tj = fj - j;
            return (g[i, j] * (1 - tj) + g[i, j + 1] * tj) * (1 - ti) + (g[i + 1, j] * (1 - tj) + g[i + 1, j + 1] * tj) * ti;
        }
        var cand = new List<(double depth, double x, double z)>();
        for (double n = -half; n <= half; n += ValleyGridM)
        for (double e = -half; e <= half; e += ValleyGridM)
        {
            if (Grid(n, e) < 5 || IslandTerrain.WaterAt(n, e) != null) continue;
            double d = Weather.FogModel.ValleyDepth(Grid, n, e);
            if (d >= ValleyMinDepthM) cand.Add((d, e, n));
        }
        cand.Sort((a, b) => b.depth.CompareTo(a.depth));
        var o = new List<FogSite>();
        foreach (var c in cand)
        {
            if (o.Count >= ValleyMax) break;
            bool clear = true;
            foreach (FogSite f in taken) if (Math.Sqrt((f.X - c.x) * (f.X - c.x) + (f.Z - c.z) * (f.Z - c.z)) < ValleySpacingM / 2 + Math.Max(f.HalfLength, f.HalfWidth)) clear = false;
            foreach (FogSite f in o) if (Math.Sqrt((f.X - c.x) * (f.X - c.x) + (f.Z - c.z) * (f.Z - c.z)) < ValleySpacingM) clear = false;
            if (!clear) continue;
            // The pool's surface is the floor's lowest ground: the fog fills it from the bottom, its top level.
            double floor = double.MaxValue;
            for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
                floor = Math.Min(floor, ground(c.z + i * ValleyRadiusM / 2, c.x + j * ValleyRadiusM / 2));
            if (floor < 2) continue;   // the disc runs into the sea: a coast, not a pool
            string id = $"Valley {o.Count + 1}";
            o.Add(new FogSite(id, true, c.x, c.z, ValleyRadiusM, ValleyRadiusM, 0, floor, 8 + 4 * Hash01(id, 5)));
        }
        return o;
    }

    /// <summary>Highest ground (m) under a disc, from the active terrain, on a 5 × 5 grid.</summary>
    public static double GroundTop(double x, double z, double radius)
    {
        double top = 0;
        for (int i = -2; i <= 2; i++)
        for (int j = -2; j <= 2; j++)
            top = Math.Max(top, WorldTerrain.GroundHeightAt(z + j * radius * .5, x + i * radius * .5));   // sim (north, east)
        return top;
    }

    /// <summary>A jittered grid over the island and its sea (±21 km), one candidate per 3.5 km cell, about one cell in
    /// five used: "patches here and there". Each is lifted to 150-600 m above the HIGHEST ground under it, so a patch
    /// over a ridge still clears the ridge. <paramref name="groundTop"/>(x, z, radius) gives that ground.</summary>
    public static List<LowPatch> PickLow(Func<double, double, double, double> groundTop)
    {
        var list = new List<LowPatch>();
        var rng = new Random(Seed + 600);
        const double cell = 3500, half = 21000;
        for (double cz = -half; cz < half; cz += cell)
        for (double cx = -half; cx < half; cx += cell)
        {
            double use = rng.NextDouble(), jx = rng.NextDouble(), jz = rng.NextDouble(), kind = rng.NextDouble();
            double agl = MinAglM + rng.NextDouble() * (MaxAglM - MinAglM), size = rng.NextDouble();
            int seed = rng.Next();
            if (use > 0.2) continue;
            double x = cx + (0.15 + 0.7 * jx) * cell, z = cz + (0.15 + 0.7 * jz) * cell;
            bool puffy = kind < 0.55;
            double radius = puffy ? 140 + 160 * size : 260 + 340 * size;
            double ground = groundTop(x, z, radius);
            list.Add(new LowPatch(puffy, x, z, ground + agl, agl, radius, seed));
        }
        return list;
    }

    /// <summary>Test hook: forget the cached picks (the terrain under them changed).</summary>
    public static void Reset() { _fog = null; _low = null; }
}

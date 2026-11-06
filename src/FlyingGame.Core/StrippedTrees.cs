namespace FlyingGame.Core;

using FlyingGame.Core.MathTypes;

/// <summary>
/// The trees an airframe has gone INTO, and what of each is still drawn (Ty 2026-11-05 13:41, iPhone 2026.1105.1140: "Pitts
/// tree impact lost propeller, retained wings, pitched nearly vertical; aircraft vanished while crash sounds continued").
///
/// What vanished it, measured (PittsTreeVisProbe / StrippedTreesTests): square on a 25 m fir 12 m up at 35 m/s the nose
/// breaks off against the stem, the airframe pitches to −85° and slides down the stem, and it comes to rest with its CG
/// 0.4 m from the stem's axis. The sim's stem there is 0.42 m thick (H/D 60, <see cref="TreeStructure"/>). The DRAWN tree
/// is a 1.5 m square trunk (1.2 × <see cref="Forest.Tree.RadiusM"/>) under three opaque four-sided skirts, the lowest
/// reaching 4.3 m out from a fifth of the height up. So the occupied airframe was inside the drawn tree from the moment
/// it entered the crown until the pilot left it: nothing culled it, nothing disabled it, the tree was simply drawn
/// over it — solid wood and needles where the sim has a thin stem and branches it has just gone through.
///
/// A tree the airframe's CG has entered (inside a skirt, or inside the drawn trunk) is drawn as the sim has it: its stem
/// at the stem's own diameter, and without every skirt whose height overlaps where the airframe has been in it (± its
/// half-size) — the branches it went through are broken out. Skirts above that stay. A wing brushing a crown leaves the
/// CG outside it and changes nothing; that pass is the tree-branch lane's physics, not this drawing.
///
/// Process-wide, like <see cref="Forest"/>'s snapped tops, and cleared with them for a new flight.
/// </summary>
public static class StrippedTrees
{
    /// <summary>One skirt of the drawn conifer: its base at <see cref="BaseFrac"/> of the height, rising
    /// <see cref="RiseFrac"/> of it, its base half-width <see cref="RadiusFrac"/> × the crown radius (the side midpoint;
    /// the corners reach √2 of that).</summary>
    public readonly struct Skirt
    {
        public readonly double BaseFrac, RiseFrac, RadiusFrac, TwistDeg;
        public Skirt(double baseFrac, double riseFrac, double radiusFrac, double twistDeg)
        { BaseFrac = baseFrac; RiseFrac = riseFrac; RadiusFrac = radiusFrac; TwistDeg = twistDeg; }
    }

    /// <summary>The close-up tree's three skirts (ForestBuilder's detail tree, which these numbers draw).</summary>
    public static readonly Skirt[] DetailSkirts = { new(0.22, 0.42, 1.00, 0), new(0.52, 0.34, 0.72, 23), new(0.78, 0.24, 0.42, 47) };
    /// <summary>The far tree's two skirts (silhouette only).</summary>
    public static readonly Skirt[] FarSkirts = { new(0.20, 0.52, 1.00, 0), new(0.62, 0.40, 0.60, 31) };
    /// <summary>The drawn trunk: a box this many × <see cref="Forest.Tree.RadiusM"/> across, centred at this fraction of the
    /// height and this fraction of it tall (so up to 0.33 of the height).</summary>
    public const double TrunkWidthOfRadius = 1.2, TrunkCentreFrac = 0.16, TrunkHeightFrac = 0.34;
    /// <summary>The farthest a four-sided skirt reaches from its axis, × its half-width: the corners.</summary>
    public const double CornerReach = 1.414214;

    /// <summary>The band of height (m AMSL) the airframe has been in a tree.</summary>
    public readonly struct Band
    {
        public readonly double LoM, HiM;
        public Band(double lo, double hi) { LoM = lo; HiM = hi; }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int, int), Band> _bands = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int, int), int> _drawn = new();
    private static int _version;

    /// <summary>Changes whenever what is DRAWN of any stripped tree changes (a skirt goes, a tree is first stripped,
    /// <see cref="Clear"/>): the renderer rebuilds then, and only then.</summary>
    public static int Version => System.Threading.Volatile.Read(ref _version);
    public static int Count => _bands.Count;

    private static (int, int) Key(in Forest.Tree t) => (Forest.CellOf(t.X), Forest.CellOf(t.Y));

    /// <summary>Forget every stripped tree (a new flight, a new world, tests).</summary>
    public static void Clear()
    {
        if (_bands.IsEmpty) return;
        _bands.Clear(); _drawn.Clear();
        System.Threading.Interlocked.Increment(ref _version);
    }

    public static bool TryGet(in Forest.Tree t, out Band band) => _bands.TryGetValue(Key(t), out band);

    /// <summary>Every stripped tree's cell (the renderer's chunks to rebuild).</summary>
    public static IEnumerable<(int i, int j)> Cells => _bands.Keys;

    /// <summary>Is skirt <paramref name="s"/> of <paramref name="t"/> still drawn? Not when its height overlaps the band.</summary>
    public static bool SkirtDrawn(in Forest.Tree t, in Skirt s)
    {
        if (!TryGet(t, out Band b)) return true;
        double lo = t.GroundM + s.BaseFrac * t.HeightM, hi = lo + s.RiseFrac * t.HeightM;
        return hi < b.LoM || lo > b.HiM;
    }

    /// <summary>Bit k set = detail skirt k is gone; bit 3 = the tree is stripped at all (its stem drawn as the sim's).</summary>
    public static int DrawnMask(in Forest.Tree t)
    {
        if (!TryGet(t, out _)) return 0;
        int m = 8;
        for (int k = 0; k < DetailSkirts.Length; k++) if (!SkirtDrawn(t, DetailSkirts[k])) m |= 1 << k;
        for (int k = 0; k < FarSkirts.Length; k++) if (!SkirtDrawn(t, FarSkirts[k])) m |= 1 << (4 + k);
        return m;
    }

    /// <summary>Is the point (x, y, up m AMSL), grown by <paramref name="marginM"/>, inside the drawn close-up tree: in one
    /// of its skirts (to their corners) or in its trunk box?</summary>
    public static bool InsideDrawn(in Forest.Tree t, double x, double y, double up, double marginM)
    {
        double dx = x - t.X, dy = y - t.Y, rho = System.Math.Sqrt(dx * dx + dy * dy), h = up - t.GroundM;
        if (h < -marginM || h > t.HeightM * 1.05 + marginM) return false;
        double trunkHalf = 0.5 * TrunkWidthOfRadius * t.RadiusM * CornerReach;
        if (h <= (TrunkCentreFrac + 0.5 * TrunkHeightFrac) * t.HeightM + marginM && rho <= trunkHalf + marginM) return true;
        foreach (Skirt s in DetailSkirts)
        {
            double b = s.BaseFrac * t.HeightM, rise = s.RiseFrac * t.HeightM;
            if (h < b - marginM || h > b + rise + marginM) continue;
            double f = System.Math.Clamp((h - b) / rise, 0, 1);
            if (rho <= s.RadiusFrac * t.CrownRadiusM * CornerReach * (1 - f) + marginM) return true;
        }
        return false;
    }

    private static readonly List<Forest.Tree> _near = new();

    /// <summary>
    /// The airframe's CG at <paramref name="cgNed"/> (world NED), its body <paramref name="bodyRadiusM"/> round it and its
    /// half-size <paramref name="halfSizeM"/> (half the larger of span and length): every tree it is inside has the band
    /// [up − half-size, up + half-size] added to its stripped band. True when what is drawn changed. Main thread only
    /// (the scratch list), as the renderer that asks is.
    /// </summary>
    public static bool Observe(ITreeSource src, Vec3 cgNed, double bodyRadiusM, double halfSizeM)
    {
        double x = cgNed.X, y = cgNed.Y, up = -cgNed.Z;
        if (src == null || !(System.Math.Abs(x) < 1e7 && System.Math.Abs(y) < 1e7 && System.Math.Abs(up) < 1e5)) return false;
        double reach = Forest.CrownRatio * (0.22 + Forest.MaxHeightM / 24.0) * CornerReach + bodyRadiusM;
        _near.Clear();
        src.Near(x - reach, y - reach, x + reach, y + reach, _near);
        bool changed = false;
        foreach (Forest.Tree t in _near)
        {
            if (!InsideDrawn(t, x, y, up, bodyRadiusM)) continue;
            var k = Key(t);
            Band nb = _bands.TryGetValue(k, out Band ob)
                ? new Band(System.Math.Min(ob.LoM, up - halfSizeM), System.Math.Max(ob.HiM, up + halfSizeM))
                : new Band(up - halfSizeM, up + halfSizeM);
            _bands[k] = nb;
            int mask = DrawnMask(t);
            if (!_drawn.TryGetValue(k, out int old) || old != mask) { _drawn[k] = mask; changed = true; }
        }
        if (changed) System.Threading.Interlocked.Increment(ref _version);
        return changed;
    }
}

namespace FlyingGame.Core;

/// <summary>
/// A list that counts its own changes, so an index built over it knows when it is stale. <see cref="WorldSolids.Shapes"/>
/// and <see cref="WorldDecks.All"/> are filled and cleared by a dozen builders; none of them has to remember to tell
/// the index.
/// </summary>
public sealed class TrackedList<T> : System.Collections.ObjectModel.Collection<T>
{
    /// <summary>Bumped by every Add, Insert, Remove, set and Clear.</summary>
    public int Version { get; private set; }
    protected override void InsertItem(int index, T item) { base.InsertItem(index, item); Version++; }
    protected override void RemoveItem(int index) { base.RemoveItem(index); Version++; }
    protected override void SetItem(int index, T item) { base.SetItem(index, item); Version++; }
    protected override void ClearItems() { base.ClearItems(); Version++; }
}

/// <summary>
/// Which of a list's items can possibly answer at a point (x, y), in the list's own order.
///
/// Quest 3S, build 8e7862eb, 2026-11-02: the 2-33 sitting on the ground at (5854, 3825) ran at 27–30 fps for minutes,
/// with "contactPoints 31.7 ms/frame". Every hard point asked every one of the island's 1,475 solids (buildings,
/// cables, arches, rings, slopes, the cavity roofs, the carrier, the forest) and 128 decks whether it was inside
/// them, at each of RK4's four stages: about 82,000 <see cref="WorldSolids.IShape.Penetrate"/> calls per 5 ms step,
/// nearly all of them a few kilometres from the aeroplane. The solids are tested at every height near the ground
/// (only the 60 m clear-of-the-ground test skips them), so it was the same in a low pass, a takeoff run and a wreck.
///
/// An item that implements <see cref="WorldSolids.IFootprint"/> promises it answers "not here" everywhere outside its
/// rectangle; it is filed in the grid cells that rectangle overlaps. Anything else (the forest, the carrier, which
/// moves, the cavity roofs) is in every cell. A cell's candidates are kept in ascending list order, so a caller that
/// stops at the first hit stops at the same item it did when it walked the whole list: the answers are the same,
/// bit for bit, by construction.
/// </summary>
public sealed class FootprintIndex<T> where T : class
{
    /// <summary>Grid pitch. A building is a cell or four; a kilometre of cable is a few dozen.</summary>
    public const double CellM = 128.0;
    /// <summary>An item covering more cells than this is simply put in the "everywhere" list.</summary>
    private const long MaxCellsPerItem = 4096;

    private sealed class Snapshot
    {
        public int Version;
        public T[] Items = System.Array.Empty<T>();
        public int[] Everywhere = System.Array.Empty<int>();
        public System.Collections.Generic.Dictionary<long, int[]> Cells = new();
    }

    private readonly TrackedList<T> _list;
    private volatile Snapshot? _snap;
    private readonly object _gate = new();

    public FootprintIndex(TrackedList<T> list) { _list = list; }

    private static long Key(long i, long j) => (i << 32) ^ (j & 0xffffffffL);
    private static long CellOf(double v) => (long)System.Math.Floor(v / CellM);

    /// <summary>The items, and the indices into them that can answer at (x, y), ascending. Allocates nothing once built.</summary>
    public T[] Candidates(double x, double y, out int[] indices)
    {
        Snapshot s = Current();
        indices = s.Cells.TryGetValue(Key(CellOf(x), CellOf(y)), out int[]? c) ? c : s.Everywhere;
        return s.Items;
    }

    /// <summary>How many of the list's items are filed by footprint (the rest are tested everywhere).</summary>
    public int FootprintCount { get { Snapshot s = Current(); return s.Items.Length - s.Everywhere.Length; } }

    private Snapshot Current()
    {
        Snapshot? s = _snap;
        if (s != null && s.Version == _list.Version) return s;
        lock (_gate)
        {
            s = _snap;
            if (s != null && s.Version == _list.Version) return s;
            s = Build();
            _snap = s;
            return s;
        }
    }

    private Snapshot Build()
    {
        var s = new Snapshot { Version = _list.Version, Items = new T[_list.Count] };
        _list.CopyTo(s.Items, 0);
        var everywhere = new System.Collections.Generic.List<int>();
        var cells = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int>>();
        for (int k = 0; k < s.Items.Length; k++)
        {
            if (s.Items[k] is not WorldSolids.IFootprint f) { everywhere.Add(k); continue; }
            f.Footprint(out double x0, out double y0, out double x1, out double y1);
            if (!(x0 <= x1 && y0 <= y1)) { everywhere.Add(k); continue; }   // NaN or inverted: never trust it
            long i0 = CellOf(x0), i1 = CellOf(x1), j0 = CellOf(y0), j1 = CellOf(y1);
            if ((i1 - i0 + 1) * (j1 - j0 + 1) > MaxCellsPerItem) { everywhere.Add(k); continue; }
            for (long i = i0; i <= i1; i++)
                for (long j = j0; j <= j1; j++)
                {
                    long key = Key(i, j);
                    if (!cells.TryGetValue(key, out var l)) cells[key] = l = new System.Collections.Generic.List<int>();
                    l.Add(k);
                }
        }
        s.Everywhere = everywhere.ToArray();
        foreach (var kv in cells)
        {
            // Merge with the everywhere items, keeping list order (both runs are already ascending).
            var merged = new int[kv.Value.Count + s.Everywhere.Length];
            int a = 0, b = 0, m = 0;
            while (a < kv.Value.Count || b < s.Everywhere.Length)
                merged[m++] = b >= s.Everywhere.Length || (a < kv.Value.Count && kv.Value[a] < s.Everywhere[b]) ? kv.Value[a++] : s.Everywhere[b++];
            s.Cells[kv.Key] = merged;
        }
        return s;
    }
}

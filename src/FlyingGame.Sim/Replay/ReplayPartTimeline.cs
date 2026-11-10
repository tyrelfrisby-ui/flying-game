using System.Collections.Generic;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// WHEN A PART OF THE AIRFRAME IS ON THE AEROPLANE, in the replay (Ty pt1001, 2026-11-07: "after a crash the replay begins
/// with the wings already missing, before the crash"). A break cuts the part out of the aeroplane's own meshes and hands it
/// to a debris object posed at the aeroplane at that instant; the replay re-poses the aeroplane but the debris lay wherever
/// it fell, so every replay frame showed the aeroplane as it ended. This is the part's history: the replay-clock time it
/// broke (<see cref="BreakT"/>), and its recorded pose samples from then on. Before the break the replay carries it on the
/// replayed aeroplane at the pose it had relative to the aeroplane when it broke (which puts the cut-out piece back exactly
/// where it was cut from); from the break on, its recorded pose. A part that broke before the recording began (or before
/// the window it still holds) is broken from the first frame: a recording that starts damaged stays damaged.
/// Engine-agnostic: the Bridge applies the poses (FlightReplay); this decides which, so the suite can test it.
/// </summary>
public sealed class ReplayPartTimeline
{
    /// <summary>The replay-clock time the part came off (or was cut onto its own pivot).</summary>
    public readonly double BreakT;
    private readonly List<double> _t = new();

    public ReplayPartTimeline(double breakT) { BreakT = breakT; }

    public int Count => _t.Count;
    public double TimeAt(int i) => _t[i];

    /// <summary>Record a sample at <paramref name="t"/> (ascending). Returns its index (the caller keeps the pose).</summary>
    public int Add(double t)
    {
        if (_t.Count > 0 && t < _t[^1]) throw new System.ArgumentException($"samples must ascend: {t} after {_t[^1]}");
        _t.Add(t);
        return _t.Count - 1;
    }

    /// <summary>Drop samples older than <paramref name="startT"/>, keeping the last one before it so a pose there can still
    /// be interpolated. Returns how many the caller must drop from its pose lists (from the front). The break time is kept:
    /// trimming never moves when the part came off.</summary>
    public int TrimBefore(double startT)
    {
        int n = 0;
        while (n < _t.Count - 1 && _t[n + 1] <= startT) n++;
        if (n > 0) _t.RemoveRange(0, n);
        return n;
    }

    /// <summary>What the replay shows at <paramref name="t"/>: on the aeroplane (<paramref name="attached"/>), or the recorded
    /// pose between samples <paramref name="i"/> and i + 1 at fraction <paramref name="u"/> (u = 0 and i = the only sample when
    /// there is one; clamped to the first and last samples).</summary>
    public void At(double t, out bool attached, out int i, out double u)
    {
        attached = t < BreakT || _t.Count == 0;
        i = 0; u = 0;
        if (attached) return;
        if (t <= _t[0]) return;
        if (t >= _t[^1]) { i = _t.Count - 1; return; }
        int lo = 0, hi = _t.Count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (_t[mid] <= t) lo = mid; else hi = mid; }
        i = lo;
        double span = _t[hi] - _t[lo];
        u = span > 1e-12 ? (t - _t[lo]) / span : 0;
    }
}

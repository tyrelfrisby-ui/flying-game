using System.Collections.Generic;
using System.Linq;
using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE WHOLE WORLD, swept for ground that stops dead.
///
/// Every invisible wall found so far has been the same mistake wearing a different hat: a feature's
/// contribution is computed inside a boolean — a footprint rectangle, a radius, a range of a parameter — and
/// outside it the feature simply is not applied. Where the feature still had relief on it at that line, the
/// ground steps by however much relief that was, vertically, with nothing to see from the air until you are
/// in it. Found on 2026-10-17, in one evening (see docs/INVISIBLE-WALLS.md):
///
///   639 m   the east mouth of the Frank Church canyon   the cut was not applied past the footprint
///   759 m   the west mouth of the same canyon           the same line at the other end
///   234 m   260 m off the end of Dewey Moore            the nearest strip owned the ground outright
///    98 m   600 m west of the alpine range's block      a peak's skirt cut off by the footprint pad
///   613 m   a circle of radius 330 m round Sentinel     the summit's claim stopped instead of fading
/// 2,235 m   the head of the glacier on the 19,000 ft    the ice's claim stopped at t = −0.05
///   853 m   the end of Hidden's outlet valley           the valley's claim stopped at its run + 260 m
///
/// THE MEASUREMENT. Telling a wall from a steep face is the whole difficulty, and comparing two probes 5 m
/// apart cannot do it: this world is allowed cliffs, and an escarpment crossed at a grazing angle shows a
/// 570 m difference in x while being perfectly continuous ground. So the sweep is two passes. The first
/// walks a grid and notes anywhere the ground moves more than <see cref="Candidate"/> between probes. The
/// second goes back to each of those and closes the probes down to <see cref="FineM"/>: a steep face's
/// height difference falls away in proportion, and a discontinuity's does not. What survives is a step the
/// ground actually takes — 10 cm of horizontal distance for tens or hundreds of metres of height.
/// </summary>
[Collection("WorldTerrainActive")]
public class WorldContinuityTests
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t = new();
    public WorldContinuityTests(ITestOutputHelper o) { _o = o; }

    // The world, generously: beyond this there is sea or nothing, and nobody flies there.
    private const double XMin = -7200, XMax = 9200, YMin = -15600, YMax = 3600;
    private const double Line = 60;          // spacing between the lines walked
    private const double Step = 5;           // and how far apart the probes are along one
    private const double Candidate = 40;     // worth going back to with a finer probe
    private const double FineM = 0.1;        // how close the second pass puts them
    private const double Wall = 12;          // a step this big in 10 cm is not a slope

    /// <summary>
    /// The ground is allowed to stand vertically in these places, because it is MEANT to and the shape of it
    /// is somebody's decision rather than an accident. Each is a circle, in metres, with its reason.
    /// </summary>
    private static readonly (string why, double x, double y, double r)[] Allowed =
    {
        // The three plunge falls. A plunge fall IS a vertical face, and behind each is an amphitheater with a
        // vertical back wall and a rock shelf over it (WorldTerrain.FallRecessAt, FallShelfBottomM).
        ("the first fall and its amphitheater", 1423, -1276, 1500),
        ("the second fall and its amphitheater", 1423, -4650, 1500),
        ("the third fall and its amphitheater", 1423, -8020, 1500),
        // The dam, which is a wall on purpose, and the gorge it stands in.
        ("the dam", 1600, 1500, 900),
    };

    /// <summary>
    /// Walls that are known about and NOT fixed, so that the threshold below can stay tight enough to catch
    /// a new one. Every entry is a bug with an owner's decision behind it, not an exemption on the merits.
    ///
    /// All six of these are the same thing: the alpine lakes are not held up by anything where no peak
    /// happens to sit under them. An open lake's rim is deliberately left out across its outlet notch so the
    /// valley can fall away, the outlet valley carries the ground at the lake's level but refuses to act
    /// inside the shoreline, and the basin's walls are applied with a Min — so they cannot build ground UP
    /// out of a plateau that lies below the water. Hidden's eastern end has its water 602 m above its own
    /// bed. Making the bed the dish outright was tried on 2026-10-17 and is worse: it moves the step to the
    /// shoreline and every lake in the range grows a 600–740 m cliff at its own edge. The fix is a decision
    /// about where the lakes are, or about the basins building their own mountain, and it is Ty's.
    /// </summary>
    private static readonly (string what, double x, double y, double r)[] Known =
    {
        ("Hidden has no bed under its eastern end", 6000, -8900, 700),
        ("Outlet's shoreline across its notch", 3000, -8900, 700),
        ("Shelf's shoreline across its notch", 3200, -13500, 700),
        ("Larch's shoreline across its notch", 6600, -13600, 700),
        ("Cirque's shoreline", 4500, -11300, 700),
        ("Crown's shoreline", 7600, -11000, 700),
        // And four that were found by this sweep and NOT diagnosed. They are an order of magnitude smaller
        // than anything above and none of them is in BaseHeightAt — walking the base terrain through the
        // first one at a metre at a time finds nothing, so each is in a later stage of HeightAt. The first
        // three are the same feature on each of the three fronts in turn, 3,370 m apart in y, which is the
        // step spacing. Diagnosing them is the next job on this.
        ("undiagnosed, on the first front", -2400, -720, 400),
        ("undiagnosed, on the second front", -2880, -4130, 400),
        ("undiagnosed, on the third front", -2340, -7440, 400),
        ("undiagnosed, north of the alpine range", 5460, -8345, 400),
    };

    private static bool In((string, double x, double y, double r)[] list, double x, double y, out string why)
    {
        foreach (var a in list)
            if ((x - a.x) * (x - a.x) + (y - a.y) * (y - a.y) < a.r * a.r) { why = a.Item1; return true; }
        why = "";
        return false;
    }

    /// <summary>
    /// The height the ground really steps at this crossing: the two probes are closed to
    /// <see cref="FineM"/> apart around it, so what comes back is a step and not a slope.
    /// </summary>
    private double StepAt(double x, double y, bool alongX)
    {
        double lo = alongX ? x - Step : y - Step, hi = lo + Step;
        for (int i = 0; i < 24 && hi - lo > FineM; i++)
        {
            double mid = (lo + hi) * 0.5;
            double a = alongX ? _t.HeightAt(lo, y, false, false) : _t.HeightAt(x, lo, false, false);
            double m = alongX ? _t.HeightAt(mid, y, false, false) : _t.HeightAt(x, mid, false, false);
            double b = alongX ? _t.HeightAt(hi, y, false, false) : _t.HeightAt(x, hi, false, false);
            // Follow whichever half still holds most of the drop.
            if (System.Math.Abs(m - a) >= System.Math.Abs(b - m)) hi = mid; else lo = mid;
        }
        double h0 = alongX ? _t.HeightAt(lo, y, false, false) : _t.HeightAt(x, lo, false, false);
        double h1 = alongX ? _t.HeightAt(hi, y, false, false) : _t.HeightAt(x, hi, false, false);
        return System.Math.Abs(h1 - h0);
    }

    [Fact]
    public void NowhereDoesTheGroundStopDead()
    {
        List<(double x, double y, bool alongX)> candidates = new();
        // The ground as if the runway tunnel had never been bored: the far end of it is 570 m of rock,
        // because that is what the end of a tunnel is, and nobody can fly into it.
        for (double y = YMin; y <= YMax; y += Line)
        {
            double prev = _t.HeightAt(XMin, y, false, false);
            for (double x = XMin + Step; x <= XMax; x += Step)
            {
                double h = _t.HeightAt(x, y, false, false);
                if (System.Math.Abs(h - prev) > Candidate) candidates.Add((x, y, true));
                prev = h;
            }
        }
        for (double x = XMin; x <= XMax; x += Line)
        {
            double prev = _t.HeightAt(x, YMin, false, false);
            for (double y = YMin + Step; y <= YMax; y += Step)
            {
                double h = _t.HeightAt(x, y, false, false);
                if (System.Math.Abs(h - prev) > Candidate) candidates.Add((x, y, false));
                prev = h;
            }
        }

        // Worst real step per square kilometre, so one long wall cannot crowd the report and a bad square is
        // easy to go and look at.
        Dictionary<(int, int), (double step, double x, double y)> cells = new();
        foreach (var c in candidates)
        {
            double step = StepAt(c.x, c.y, c.alongX);
            if (step < Wall) continue;
            var key = ((int)System.Math.Floor(c.x / 1000), (int)System.Math.Floor(c.y / 1000));
            if (!cells.TryGetValue(key, out var cur) || step > cur.step) cells[key] = (step, c.x, c.y);
        }

        var worst = cells.Values.OrderByDescending(v => v.step).ToList();
        _o.WriteLine($"{candidates.Count} crossings worth a closer look; {cells.Count} square kilometres hold a real step, all of them:");
        foreach (var w in worst)
        {
            string tag = In(Allowed, w.x, w.y, out string why) ? "   allowed: " + why
                       : In(Known, w.x, w.y, out string known) ? "   KNOWN: " + known
                       : "";
            _o.WriteLine($"  {w.step,7:F1} m over {FineM:F1} m at ({w.x,7:F0}, {w.y,8:F0}){tag}");
        }

        var offenders = worst.Where(w => !In(Allowed, w.x, w.y, out _) && !In(Known, w.x, w.y, out _)).ToList();
        _o.WriteLine(offenders.Count == 0
            ? "nothing outside the allowed and known places steps at all"
            : $"worst step that is neither allowed nor known: {offenders[0].step:F1} m at ({offenders[0].x:F0}, {offenders[0].y:F0})");
        Assert.True(offenders.Count == 0,
                    offenders.Count == 0 ? "" :
                    $"the ground stops dead at ({offenders[0].x:F0}, {offenders[0].y:F0}): {offenders[0].step:F0} m in {FineM:F1} m"
                    + $" (and in {offenders.Count} square kilometre(s) in all)");
    }
}

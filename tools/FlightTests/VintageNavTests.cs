using FlyingGame.Core;
using FlyingGame.Core.Nav;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The rungs below the VOR: the fire pots, the four-course range, and the coloured low-frequency airways.
///
/// Owner, 2026-10-18: *"The game will have navigation go from pilotage to ded reconing to fire pots to an
/// ranged to ndb to vor to ils but never to gps."* Each rung has to work on its own terms, and each has to
/// be WORSE than the one above it in a way you can feel — otherwise the ladder is decoration. These tests
/// assert the differences rather than only the mechanisms.
/// </summary>
[Collection("WorldTerrainActive")]
public class VintageNavTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t = new();
    public VintageNavTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = _t; }

    /// <summary>
    /// Hand the world back. A class that sets <see cref="WorldTerrain.Active"/> and walks away leaves every
    /// later test flying over this world's mountains instead of the flat ground at sea level they were
    /// written for — which is exactly what happened: WindTests' crosswind touchdown started failing, and only
    /// when the whole suite ran, because on its own nobody had set the terrain before it.
    /// </summary>
    public void Dispose() => WorldTerrain.Active = null;

    private double Ground(double x, double y) => _t.HeightAt(x, y, false, false);

    // ---------------------------------------------------------------- the fire pots

    /// <summary>
    /// The route exists, it runs from one airport to another, every light stands on ground you could build
    /// on, and — the part that makes it a lesson rather than a decoration — IT GOES DARK AT EVERY WALL.
    /// </summary>
    [Fact]
    public void TheMailRouteIsLitExceptOnTheEscarpments()
    {
        var pots = FirePots.All(_t);
        foreach (FirePot p in pots)
            _o.WriteLine($"  #{p.Number,2} ({p.X:F0},{p.Y,7:F0})  {p.GroundFt,6:F0} ft  plateau {p.Plateau}");
        Assert.True(pots.Count >= 8, $"only {pots.Count} pots on the route");

        // Every pot is on buildable ground and out of the river.
        foreach (FirePot p in pots)
        {
            Assert.Null(_t.WaterSurfaceAt(p.X, p.Y));
            Assert.Equal(Ground(p.X, p.Y), p.GroundM, 1);
            double worst = 0;
            for (int k = 0; k < 8; k++)
            {
                double a = k * System.Math.PI / 4;
                worst = System.Math.Max(worst, System.Math.Abs(
                    Ground(p.X + 120 * System.Math.Cos(a), p.Y + 120 * System.Math.Sin(a)) - p.GroundM));
            }
            Assert.True(System.Math.Atan2(worst, 120) * 180 / System.Math.PI <= FirePots.MaxSlopeDeg + 0.01,
                        $"pot #{p.Number} is on a wall");
        }

        // It starts at one airport and finishes at another.
        Assert.Equal(WorldTerrain.Airports[0].Y, pots[0].Y, 0);
        Assert.True(System.Math.Abs(pots[^1].Y - WorldTerrain.Airports[^1].Y) < FirePots.SpacingM * 1.5);
        // And every plateau carries some.
        for (int p = 0; p < WorldTerrain.PlateauCount; p++)
            Assert.Contains(p, pots.Select(x => x.Plateau));

        // Three dark stretches, one per escarpment, each about a mile.
        var dark = FirePots.DarkStretches(_t);
        foreach (var g in dark)
            _o.WriteLine($"  dark #{g.from.Number} to #{g.to.Number}: {g.gapM / Terps.MetresPerNm:F2} NM on heading alone");
        Assert.Equal(WorldTerrain.StepCount, dark.Count);
        foreach (var g in dark) Assert.InRange(g.gapM / Terps.MetresPerNm, 0.5, 2.0);
        // A dark stretch always steps UP a plateau — that is what the gap IS.
        foreach (var g in dark) Assert.True(g.to.Plateau == g.from.Plateau + 1,
            $"the gap from #{g.from.Number} to #{g.to.Number} is not an escarpment");
    }

    // ---------------------------------------------------------------- the four-course range

    [Fact]
    public void TheFourCoursesGiveASteadyNoteAndTheQuadrantsGiveLetters()
    {
        AnRange r = Navaids.Ranges[0];
        Assert.Equal(new[] { 360.0, 90.0, 180.0, 270.0 }, r.Courses());

        // Down each course, a steady note.
        foreach (double c in r.Courses())
        {
            double rad = c * System.Math.PI / 180;
            double x = r.X + System.Math.Cos(rad) * 4000, y = r.Y + System.Math.Sin(rad) * 4000;
            var tone = AnRangeSignal.Read(_t, r, x, y, Ground(x, y) + 3000 * Terps.MPerFt,
                                          out double off, out double near, out double sig);
            _o.WriteLine($"  4 km out on the {c:000} course: {AnRangeSignal.Describe(tone)} ({off:+0.0;-0.0;0.0} off {near:000})");
            Assert.Equal(RangeTone.OnCourse, tone);
            Assert.Equal(0, off, 1);
            Assert.Equal(c, near, 1);
        }

        // Either side of the 090 course, opposite letters — which is the whole instrument.
        var north = AnRangeSignal.Read(_t, r, r.X + 1000, r.Y + 3000, 1200, out double offN, out _, out _);
        var south = AnRangeSignal.Read(_t, r, r.X - 1000, r.Y + 3000, 1200, out double offS, out _, out _);
        _o.WriteLine($"  north of the 090 course: {AnRangeSignal.Describe(north)}; south: {AnRangeSignal.Describe(south)}");
        Assert.True(north == RangeTone.A || north == RangeTone.BiSignalA);
        Assert.True(south == RangeTone.N || south == RangeTone.BiSignalN);
        // And the off-course sign reads like a needle, measured along the COURSE's own direction. The 090
        // course runs east; flying east, your right hand points south. So being NORTH of it puts the course
        // on your right and the sign positive — which is the opposite of what I first wrote down, and the
        // third time in this file's history that a sign convention had to be settled with a compass rather
        // than with an intuition.
        _o.WriteLine($"  north of the course reads {offN:+0.0;-0.0}, south reads {offS:+0.0;-0.0}");
        Assert.True(offN > 0, $"north of an eastbound course, the course is to the right: got {offN:F1}");
        Assert.True(offS < 0, $"south of an eastbound course, the course is to the left: got {offS:F1}");
    }

    [Fact]
    public void CloseInToACourseYouHearBothLettersBeforeYouHearOne()
    {
        AnRange r = Navaids.Ranges[0];
        // Walking out across the 090 course at 3 km: steady note, then bi-signal, then a clean letter.
        var seen = new List<string>();
        for (double across = 0; across <= 1600; across += 100)
        {
            var tone = AnRangeSignal.Read(_t, r, r.X + across, r.Y + 3000, 1200, out double off, out _, out _);
            seen.Add($"{across:F0} m ({off:+0.0;-0.0;0.0} deg): {tone}");
        }
        foreach (string s in seen) _o.WriteLine("  " + s);
        Assert.Contains("OnCourse", string.Join("|", seen));
        Assert.Contains("BiSignalA", string.Join("|", seen));
        Assert.Contains("|" + "A", string.Join("|", seen.Select(s => s.Substring(s.LastIndexOf(' ') + 1))));
    }

    [Fact]
    public void TheConeOfSilenceIsTheOnlyPositiveFixTheSystemGives()
    {
        AnRange r = Navaids.Ranges[0];
        // Overhead at 3,000 ft above the station: silence.
        double alt = r.AntennaM + 3000 * Terps.MPerFt;
        Assert.Equal(RangeTone.Silent, AnRangeSignal.Read(_t, r, r.X + 50, r.Y, alt, out _, out _, out _));
        // A mile to one side at the same height: not silent.
        var beside = AnRangeSignal.Read(_t, r, r.X, r.Y + 2000, alt, out _, out _, out _);
        Assert.NotEqual(RangeTone.Silent, beside);
        // On the ground beside the mast there is no cone at all.
        Assert.NotEqual(RangeTone.Silent, AnRangeSignal.Read(_t, r, r.X + 300, r.Y, r.GroundM + 30, out _, out _, out _));
        // And it is the approach's missed approach point, which is the point of all this.
        Approach range = Approaches.All.First(a => a.Kind == ApproachKind.Range);
        ProcedureFix map = range.Fix(FixRole.MissedApproachPoint);
        Assert.Equal(r.X, map.X, 1);
        Assert.Equal(r.Y, map.Y, 1);
        Assert.Contains("cone of silence", map.Identified);
    }

    [Fact]
    public void ARangeIsHeardThroughTheGroundLikeABeacon()
    {
        AnRange r = Navaids.Ranges[0];
        Vor vly = Navaids.ById("VLY");
        // Behind the first escarpment, on the deck: the co-sited VOR is gone and the range is not.
        double x = 400, y = -2500, alt = Ground(x, y) + 100 * Terps.MPerFt;
        double vor = NavReception.PathSignal(_t, vly.X, vly.Y, vly.AntennaM, x, y, alt, vly.FreqMhz);
        var tone = AnRangeSignal.Read(_t, r, x, y, alt, out _, out _, out double sig);
        _o.WriteLine($"at (400,-2500) on the deck: the VOR reads {vor:F2}, the range on the same mast reads {sig:F2} ({tone})");
        Assert.True(vor < Airways.UsableSignal);
        Assert.True(sig > 0.9);
        Assert.NotEqual(RangeTone.Silent, tone);
    }

    // ---------------------------------------------------------------- the coloured airways

    /// <summary>
    /// THE OLD AIRWAY IS LOWER THAN THE NEW ONE, over the same ground, and by 1,900 ft. That is not nostalgia
    /// — a Victor airway's minimum has to satisfy reception as well as obstacles, and up this valley the VHF
    /// signal is what holds you up. The coloured airway's signal follows the earth, so only the rocks matter.
    /// This is the measurable reason the ladder is worth climbing down as well as up.
    /// </summary>
    [Fact]
    public void TheRangeCourseIsLowerThanTheVictorAirwayOverTheSameGround()
    {
        Airway green = Airways.All.First(a => a.Designator == "GREEN 1");
        var asColoured = Airways.Leg(_t, green.Points[0], green.Points[1], AirwayKind.Coloured);
        var asVictor = Airways.Leg(_t, green.Points[0], green.Points[1], AirwayKind.Victor);
        _o.WriteLine($"GREEN 1 {green.Points[0]}-{green.Points[1]}: coloured MEA {asColoured.MeaFt:F0} ft "
                     + $"({asColoured.MeaDrivenBy}), the same route as a Victor airway {asVictor.MeaFt:F0} ft ({asVictor.MeaDrivenBy})");
        Assert.Equal("terrain", asColoured.MeaDrivenBy);
        Assert.Equal("signal", asVictor.MeaDrivenBy);
        Assert.True(asVictor.MeaFt - asColoured.MeaFt >= 1000,
                    $"the low-frequency airway should be well below the VHF one: {asColoured.MeaFt:F0} vs {asVictor.MeaFt:F0}");
    }

    [Fact]
    public void EveryColouredAirwayRunsOnItsStationsOwnCourse()
    {
        // A coloured airway WAS a range course or a beacon track, not a line drawn near a station. So each
        // leg's course has to actually be one of the station's courses, within a degree.
        foreach (Airway a in Airways.All.Where(x => x.Kind == AirwayKind.Coloured))
            for (int i = 0; i + 1 < a.Points.Length; i++)
            {
                var leg = Airways.Leg(_t, a.Points[i], a.Points[i + 1], a.Kind);
                _o.WriteLine($"  {a.Designator} {leg.FromId}-{leg.ToId} course {leg.CourseDeg:000}");
                bool onACourse = false;
                foreach (string id in new[] { leg.FromId, leg.ToId })
                {
                    if (!Navaids.TryRangeById(id, out AnRange r)) continue;
                    foreach (double c in r.Courses())
                        if (System.Math.Abs(NavReception.AngleDiff(c, leg.CourseDeg)) < 1.0
                            || System.Math.Abs(NavReception.AngleDiff(c + 180, leg.CourseDeg)) < 1.0) onACourse = true;
                }
                // A beacon airway has no courses of its own, so it is exempt — you track it on a needle.
                bool beaconLeg = Navaids.TryNdbById(leg.FromId, out _) || Navaids.TryNdbById(leg.ToId, out _);
                Assert.True(onACourse || beaconLeg,
                            $"{a.Designator} {leg.FromId}-{leg.ToId} at {leg.CourseDeg:000} is not on any course of its stations");
            }
    }

    [Fact]
    public void TheParallelBeaconAirwayCostsTwoThousandFeetForFourHundredMetres()
    {
        // RED 1 runs 450 m east of GREEN 1 up the same valley, and its MEA is far higher — because 450 m of
        // offset brings the Alaska range's edge inside the airway's protected area. Worth a test because it
        // is the kind of thing that looks like a mistake on the chart and is not.
        var green = Airways.Leg(_t, "VR", "SUM", AirwayKind.Coloured);
        var red = Airways.Leg(_t, "BN", "SM", AirwayKind.Coloured);
        _o.WriteLine($"GREEN 1 (x=400) MEA {green.MeaFt:F0} ft;  RED 1 (x=850) MEA {red.MeaFt:F0} ft");
        Assert.True(red.MeaFt > green.MeaFt + 1000,
                    $"RED 1 should be well above GREEN 1: {red.MeaFt:F0} vs {green.MeaFt:F0}");
    }

    [Fact]
    public void TheLadderHasARungForEveryKindOfApproach()
    {
        // pilotage -> dead reckoning -> fire pots -> range -> NDB -> VOR -> ILS, and never GPS.
        var kinds = Approaches.All.Select(a => a.Kind).Distinct().OrderBy(k => (int)k).ToList();
        foreach (Approach a in Approaches.All)
            _o.WriteLine($"  {a.Airport,-8} {a.Title,-14} {a.Kind}");
        Assert.Contains(ApproachKind.Range, kinds);
        Assert.Contains(ApproachKind.Ndb, kinds);
        Assert.Contains(ApproachKind.Vor, kinds);
        Assert.Contains(ApproachKind.Ils, kinds);
        // And the thing that must never appear.
        Assert.DoesNotContain("GPS", string.Join(" ", Approaches.All.Select(a => a.Title)).ToUpperInvariant());
        Assert.DoesNotContain("RNAV", string.Join(" ", Approaches.All.Select(a => a.Title)).ToUpperInvariant());
    }

    /// <summary>
    /// THE INTERLOCK: with both letters equally strong the headphones carry an unbroken note, and each
    /// letter on its own is still a readable A or N. If this ever stops being true the instrument stops
    /// existing, because the note breaking up is the only thing it tells you.
    /// </summary>
    [Fact]
    public void TheAAndTheNDovetailIntoOneUnbrokenNote()
    {
        // Across a whole cycle, exactly one of the two is keying at every instant.
        for (int u = 0; u < 8; u++)
        {
            double t = (u + 0.5) * AnRangeSignal.UnitSeconds;
            bool a = AnRangeSignal.AKeying(t), n = AnRangeSignal.NKeying(t);
            _o.WriteLine($"  unit {u}: {(a ? "A" : " ")}{(n ? "N" : " ")}");
            Assert.True(a ^ n, $"unit {u} has {(a && n ? "both" : "neither")} letter keying");
        }

        // On the course the envelope never dips: a steady note.
        double min = 1, max = 0;
        for (double t = 0; t < 2.4; t += 0.005)
        {
            double k = AnRangeSignal.Keying(t, 0, true);
            min = System.Math.Min(min, k); max = System.Math.Max(max, k);
        }
        _o.WriteLine($"  on course the envelope runs {min:F2} to {max:F2}");
        Assert.Equal(min, max, 6);
        Assert.Equal(0.5, min, 6);

        // Deep in the A quadrant it is a clean A: full on during A's units, silent during N's.
        Assert.Equal(1.0, AnRangeSignal.Keying(3.5 * AnRangeSignal.UnitSeconds, 20, true), 6);
        Assert.Equal(0.0, AnRangeSignal.Keying(0.5 * AnRangeSignal.UnitSeconds, 20, true), 6);
        // And in the N quadrant the other way round.
        Assert.Equal(1.0, AnRangeSignal.Keying(0.5 * AnRangeSignal.UnitSeconds, 20, false), 6);
        Assert.Equal(0.0, AnRangeSignal.Keying(3.5 * AnRangeSignal.UnitSeconds, 20, false), 6);

        // Just off the course you hear both, one louder — which is the cue to turn back before the note
        // breaks up completely.
        double loud = AnRangeSignal.Keying(3.5 * AnRangeSignal.UnitSeconds, 6, true);
        double quiet = AnRangeSignal.Keying(0.5 * AnRangeSignal.UnitSeconds, 6, true);
        _o.WriteLine($"  6 degrees off: the A reads {loud:F2} over an N of {quiet:F2}");
        Assert.True(loud > quiet && quiet > 0.05, $"the bi-signal should be both letters: {loud:F2} / {quiet:F2}");
    }

    [Fact]
    public void TheLettersAreTheRightLettersInMorse()
    {
        // N is dash-dot and A is dot-dash. Measured off the keying rather than trusted.
        var runs = new List<(char on, int units)>();
        char last = '?'; int count = 0;
        for (int u = 0; u < 8; u++)
        {
            char c = AnRangeSignal.NKeying((u + 0.5) * AnRangeSignal.UnitSeconds) ? 'x' : '.';
            if (c == last) count++; else { if (last != '?') runs.Add((last, count)); last = c; count = 1; }
        }
        runs.Add((last, count));
        _o.WriteLine("  N keys: " + string.Join(" ", runs.Select(r => $"{r.on}{r.units}")));
        Assert.Equal(3, runs[0].units);     // dash
        Assert.Equal('x', runs[0].on);
        Assert.Equal(1, runs[2].units);     // dot
        Assert.Equal('x', runs[2].on);
    }
}

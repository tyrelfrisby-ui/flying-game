using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Frank Church strips on the island (island-spec.json "backcountry_strips"), measured on the island ground
/// that is actually flown.
///
/// 2026-11-03 (lane/claude-frank-church-valleys). Ty flew them and found every one on a mesa top: "always deep in
/// the valleys". They were anchors at plateau elevations with a 20:1 Part 77 fan planing 1.5 km off each end, so
/// each stood on its own table. They now lie on the floors of the "C2-*" river canyons, and these tests hold that:
/// walls hundreds of metres high on both sides, the river beside the strip and a few metres below it, never across
/// it, the floors falling downstream, the one-way strips landing uphill into a wall, and the drawn surface being the
/// surface the wheels roll on.
///
/// The tests this file used to hold measured the LEGACY canyon (FrankChurch.HeightAt, CanyonCentreY, X0..X1): a
/// world that is no longer built. WorldTerrain.HeightAt is the island's height and has been since the island
/// landed, so those asserted things about ground nobody flies (and asked for ten strips; the island has nine).
/// </summary>
[Collection("WorldTerrainActive")]
public class FrankChurchTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t;

    public FrankChurchTests(ITestOutputHelper o)
    {
        _o = o;
        _t = new WorldTerrain();
        WorldTerrain.Active = _t;
    }

    public void Dispose() => WorldTerrain.Active = null;

    private double H(double x, double y) => _t.HeightAt(x, y);
    // Every strip is measured before anything fails, so one run names every strip that is wrong, not the first.
    private readonly List<string> _bad = new();
    private void Check(bool ok, string what) { if (!ok) _bad.Add(what); }
    private void Verdict() => Assert.True(_bad.Count == 0, string.Join("\n", _bad));
    private static IslandField Field(FrankChurch.Strip s) => IslandTerrain.Fields.First(f => f.Id == s.Name);
    private static (double x, double y) At(FrankChurch.Strip s, double along, double across = 0)
    {
        double h = s.HeadingDeg * Math.PI / 180;
        return (s.X + Math.Cos(h) * along - Math.Sin(h) * across, s.Y + Math.Sin(h) * along + Math.Cos(h) * across);
    }

    // Copied by hand from the design (island-spec.json, 2026-11-03): which river each strip is on, and which are
    // one-way. Moving a strip to another river, or making one one-way, has to be an edit here too.
    private static readonly Dictionary<string, string> Rivers = new()
    {
        ["Thomas Creek"] = "Middle Fork", ["Mackay Bar"] = "Middle Fork", ["Indian Creek"] = "Middle Fork",
        ["Sulphur Creek"] = "Sulphur Creek", ["Cabin Creek"] = "Big Creek", ["Soldier Bar"] = "Chamberlain Creek",
        ["Chamberlain"] = "Chamberlain Creek", ["Dewey Moore"] = "Chamberlain Creek", ["Big Creek"] = "Chamberlain Creek",
    };
    private static readonly string[] OneWays = { "Cabin Creek", "Soldier Bar", "Dewey Moore" };

    [Fact]
    public void EveryStripIsWithinTenMinutesOfEveryOther()
    {
        // The owner's compactness rule, in the unit that matters: minutes in a Cub.
        double worst = FrankChurch.WorstCrossingMinutes();
        _o.WriteLine($"farthest pair: {worst:0.0} minutes at 90 kt");
        Assert.True(worst < 10, $"the farthest pair is {worst:0.0} minutes apart");
        Assert.True(worst > 1.5, "they should not all be on top of each other either");
        Assert.Equal(9, FrankChurch.Strips.Length);
    }

    [Fact]
    public void TheStripsAreTheLengthsTheRealOnesAre()
    {
        // The famous ones run from about 1,200 ft (Dewey Moore) to about 4,000 ft (Thomas Creek, Indian Creek).
        double shortest = double.MaxValue, longest = 0;
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            Assert.InRange(s.LengthFt, 1150, 4100);
            shortest = Math.Min(shortest, s.LengthFt);
            longest = Math.Max(longest, s.LengthFt);
            _o.WriteLine($"{s.Name,-14} {s.LengthFt,5:0} ft  {s.ElevationM * 3.28084,5:0} ft MSL  {s.SlopePercent,4:0.0}%  {(s.OneWay ? "ONE WAY" : "two-way")}  {s.River}");
        }
        Assert.True(shortest < 1300, "one of them has to be properly short");
        Assert.True(longest > 3800, "and one of them long");
    }

    [Fact]
    public void EveryStripIsFlatAcrossAndAtItsOwnSlopeAlong()
    {
        // A strip you cannot roll straight down is not a strip. Flat across to the edges, and the slope along it is
        // the slope it says it has, every 5 m from end to end — a sloped strip that was flat and then stepped would
        // pass a test that only looked at a few stations. The turf strip, Sulphur Creek, lies the way a field lies
        // (Ty 2026-11-03, NaturalSurfaceTests): a 1.92 m banana with a 0.38 m cross-sweep, held to those bounds.
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            double sag = s.Name == "Sulphur Creek" ? 1.92 + .05 : 0, sweep = s.Name == "Sulphur Creek" ? 2 * .38 + .05 : 0;
            for (double along = -s.LengthM / 2; along <= s.LengthM / 2; along += 5)
            {
                var (cx, cy) = At(s, along);
                double mid = H(cx, cy), plane = s.ElevationM + along * s.SlopePercent / 100.0;
                if (sag > 0) Assert.InRange(mid - plane, -sag, .05);
                else Assert.True(Math.Abs(mid - plane) < .01, $"{s.Name}: {mid - plane:+0.000;-0.000} m off its plane {along:0} m along");
                foreach (int k in new[] { -1, 1 })
                {
                    var (ex, ey) = At(s, along, k * s.WidthM * 0.45);
                    if (sweep > 0) Assert.InRange(H(ex, ey) - mid, -sweep, sweep);
                    else Assert.True(Math.Abs(H(ex, ey) - mid) < .01, $"{s.Name}: not flat across {along:0} m along");
                }
            }
            Assert.Equal(s.HighEndM - s.LowEndM, s.LengthM * s.SlopePercent / 100.0, 3);
        }
    }

    [Fact]
    public void TheStripsAreSurfacesTheWheelsUnderstand()
    {
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            WorldTerrain.Surface surf = WorldTerrain.SurfaceAt(s.X, s.Y);
            Assert.True(surf is WorldTerrain.Surface.Grass or WorldTerrain.Surface.Gravel, $"{s.Name} is {surf} — nothing lands on that");
            var (ox, oy) = At(s, 0, s.WidthM * 0.5 + 60);
            Assert.NotEqual(WorldTerrain.Surface.Grass, WorldTerrain.SurfaceAt(ox, oy));
        }
    }

    /// <summary>
    /// The point of the job. Beside every strip — within 1.5 km either side, not off its ends — the ground climbs at
    /// least 200 m above it on BOTH sides, and at least 300 m on one: a strip in a canyon, not on a table. (Measured
    /// 2026-11-03: 239 m on the low side of Cabin Creek, where KPGA's approach surfaces hold the ridge down, to
    /// 552 m under Frank Church's south face beside Chamberlain. On the plateau they had been 4 to 220 m.)
    /// </summary>
    [Fact]
    public void EveryStripIsDeepInAValley()
    {
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            double left = double.MinValue, right = double.MinValue;
            for (double r = 100; r <= 1500; r += 25)
                for (double a = 0; a < 360; a += 3)
                {
                    double along = r * Math.Cos(a * Math.PI / 180), across = r * Math.Sin(a * Math.PI / 180);
                    if (Math.Abs(along) > s.LengthM / 2 + 300) continue;
                    var (x, y) = At(s, along, across);
                    double h = H(x, y) - s.HighEndM;
                    if (across > 0) right = Math.Max(right, h); else left = Math.Max(left, h);
                }
            _o.WriteLine($"{s.Name,-14} walls {left,4:0} m / {right,4:0} m above its high end");
            Check(Math.Min(left, right) >= 200, $"{s.Name}: one side rises only {Math.Min(left, right):0} m — that is a rim, not a valley");
            Check(Math.Max(left, right) >= 300, $"{s.Name}: neither wall reaches 300 m ({Math.Max(left, right):0} m)");
        }
        Verdict();
    }

    /// <summary>The river is beside every strip and a little below it — 3 to 60 m, the bar or the low bench a real
    /// one sits on — and never under it.</summary>
    [Fact]
    public void EveryStripLiesBesideItsRiverAndNotInIt()
    {
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            Assert.Equal(Rivers[s.Name], s.River);
            foreach (int end in new[] { -1, 1 })
            {
                var (ex, ey) = At(s, end * s.LengthM / 2);
                double endZ = end > 0 ? s.HighEndM : s.LowEndM, best = double.MaxValue, water = double.NaN;
                for (double dx = -400; dx <= 400; dx += 5)
                    for (double dy = -400; dy <= 400; dy += 5)
                    {
                        double d = Math.Sqrt(dx * dx + dy * dy); if (d >= best) continue;
                        double? w = _t.WaterSurfaceAt(ex + dx, ey + dy, H(ex + dx, ey + dy) + 1);
                        if (w.HasValue) { best = d; water = w.Value; }
                    }
                _o.WriteLine($"{s.Name,-14} {(end > 0 ? "high" : "low ")} end: river {best,3:0} m away, {endZ - water,5:0.0} m below");
                Check(best < 320, $"{s.Name}: no river within 320 m of its {(end > 0 ? "high" : "low")} end");
                Check(endZ - water >= 3 && endZ - water <= 60, $"{s.Name}: {(end > 0 ? "high" : "low")} end {endZ - water:0.0} m above the river");
            }
            for (double along = -s.LengthM / 2; along <= s.LengthM / 2; along += 10)
                for (double c = -s.WidthM / 2; c <= s.WidthM / 2; c += s.WidthM / 4)
                {
                    var (x, y) = At(s, along, c);
                    Check(_t.WaterSurfaceAt(x, y, H(x, y) + 1) == null, $"{s.Name}: water on the strip {along:0} m along");
                }
        }
        Verdict();
    }

    /// <summary>
    /// The rivers run downhill. Walking each river from its head (the C2 canyons in order: Middle Fork, Sulphur Creek,
    /// Big Creek, Chamberlain Creek), the ground on the thalweg is never above the floor the canyon authors (6 m where a strip's bank blend reaches
    /// the water: measured up to 5.1 m beside Big Creek), never
    /// climbs more than 5 m above the lowest point already passed — a riffle where a strip's bank blend reaches the
    /// water (4.3 m under Thomas Creek's, measured), not a dam (Soldier Bar's once stood 23 m across the Big Creek mouth), and carries water wherever a strip's bank is not over it. Below its
    /// strips the Middle Fork leaves its canyon for the Dillingham lowland, which is lower than the gorge floor: there
    /// the river runs on the lowland's own ground. And the strips on the Middle Fork fall with it, Thomas Creek above
    /// Mackay Bar above Indian Creek.
    /// </summary>
    [Fact]
    public void TheRiversFallDownstreamAndTheStripsWithThem()
    {
        int looked = 0, wet = 0;
        var canyons = IslandTerrain.World["canyons"].Items.Where(q => q.S("id").StartsWith("C2-")).ToArray();
        foreach (string river in new[] { "MF", "SC", "BC", "CC" })
        {
            double lowest = double.MaxValue;
            foreach (var c in canyons.Where(q => q.S("id").StartsWith("C2-" + river)))
            {
                double z0 = c["floor_ft"][0].Number() * IslandTerrain.Feet, z1 = c["floor_ft"][1].Number() * IslandTerrain.Feet;
                Check(z1 < z0, $"{c.S("id")}'s floor runs uphill");
                var a = c["path"][0].Point; var b = c["path"][1].Point;
                for (double t = 0; t < 1; t += .01)
                {
                    var p = a + (b - a) * t; double floor = z0 + (z1 - z0) * t, h = H(p.X, p.Y);
                    bool bank = IslandTerrain.Fields.Any(f => f.Contains(p, 330));   // within reach of a strip's bank blend
                    Check(h <= floor + (bank ? 6 : 1), $"{c.S("id")} at t {t:0.00}: ground {h - floor:+0.0} m above its floor");
                    Check(h <= lowest + 5, $"{c.S("id")} at t {t:0.00}: the river climbs {h - lowest:0.0} m");
                    lowest = Math.Min(lowest, h);
                    if (IslandTerrain.Fields.Any(f => f.Contains(p, 160))) continue;   // under a strip's bank the river is not drawn
                    looked++;
                    if (_t.WaterSurfaceAt(p.X, p.Y, h + 1).HasValue) wet++;
                }
            }
        }
        _o.WriteLine($"{looked} open thalweg stations, {wet} with water");
        Check(looked > 400 && wet == looked, $"{wet} of {looked} open thalweg stations carry the river");
        var mf = FrankChurch.Strips.Where(s => s.River == "Middle Fork").ToDictionary(s => s.Name);
        Check(mf["Thomas Creek"].ElevationM > mf["Mackay Bar"].ElevationM && mf["Mackay Bar"].ElevationM > mf["Indian Creek"].ElevationM,
              "the Middle Fork strips do not fall downstream");
        Verdict();
    }

    /// <summary>
    /// One-way in, one-way out (Ty 2026-11-03): you land UPHILL toward ground that closes past the far end, so once
    /// committed there is no going around; you take off the other way, downhill and down-canyon. The approach is
    /// open down the canyon, and the briefing data says all of it.
    /// </summary>
    [Fact]
    public void AOneWayStripLandsUphillIntoAWallAndLeavesDownCanyon()
    {
        Assert.Equal(OneWays.OrderBy(n => n), FrankChurch.Strips.Where(s => s.OneWay).Select(s => s.Name).OrderBy(n => n));
        foreach (FrankChurch.Strip s in FrankChurch.Strips.Where(s => s.OneWay))
        {
            double land = s.OneWayInHeadingDeg!.Value;
            Assert.Equal(s.HeadingDeg, land, 6);                          // the strip rises along its landing heading
            Assert.InRange(s.SlopePercent, 3, 8);
            Assert.True(s.NoGoAround);
            Assert.Equal(new[] { land }, s.LandingHeadingsDeg);
            Assert.Equal(new[] { (land + 180) % 360 }, s.TakeoffHeadingsDeg);
            Assert.Equal((land + 180) % 360, s.TakeoffHeadingDeg!.Value, 6);
            // Past the high end: within 700 m the ground stands at least 100 m above it, and nowhere in that run does
            // an aeroplane climbing out at 20 % (11 degrees, far beyond a loaded Cub at 4,000 ft) clear it.
            double top = double.MinValue, needed = 0;
            for (double d = 20; d <= 700; d += 10)
            {
                var (x, y) = At(s, s.LengthM / 2 + d);
                double rise = H(x, y) - s.HighEndM;
                top = Math.Max(top, rise); needed = Math.Max(needed, rise / (d + 300));   // 300 m of float and flare first
            }
            // Behind the low end, down the canyon: open. The ground stays under a 5.7-degree (10 %) approach from 10 m
            // over the threshold for 450 m, and falls away from the strip — the takeoff direction is downhill.
            double worstApproach = double.MinValue;
            for (double d = 60; d <= 450; d += 10)
            {
                var (x, y) = At(s, -s.LengthM / 2 - d);
                worstApproach = Math.Max(worstApproach, H(x, y) - (s.LowEndM + 10 + .10 * d));
            }
            _o.WriteLine($"{s.Name,-14} lands {land:000} uphill {s.SlopePercent}%, wall +{top:0} m, needs {needed * 100:0}% to out-climb; approach margin {-worstApproach:0} m; takes off {(land + 180) % 360:000}");
            Check(top >= 100, $"{s.Name}: the far end is open ({top:0} m) — that is not a one-way strip");
            Check(needed > .20, $"{s.Name}: a {needed * 100:0}% climb clears the far end, so a go-around exists");
            Check(worstApproach < 0, $"{s.Name}: ground stands {worstApproach:0} m into the approach");
            Assert.True(s.LowEndM < s.HighEndM);
        }
        Verdict();
    }

    /// <summary>A two-way strip is gentle, and neither end runs into a wall: off both ends the ground stays under a
    /// 10 % plane from 10 m over the end for 400 m, so the canyon goes on either way and you can land in either
    /// direction or go around down the canyon.</summary>
    [Fact]
    public void TheTwoWayStripsCanBeUsedInEitherDirection()
    {
        foreach (FrankChurch.Strip s in FrankChurch.Strips.Where(s => !s.OneWay))
        {
            Assert.InRange(s.SlopePercent, 0, 2.5);
            Assert.False(s.NoGoAround);
            Assert.Equal(2, s.LandingHeadingsDeg.Length);
            foreach (int k in new[] { -1, 1 })
            {
                double endZ = k > 0 ? s.HighEndM : s.LowEndM, worst = double.MinValue;
                for (double d = 60; d <= 400; d += 10)
                {
                    var (x, y) = At(s, k * (s.LengthM / 2 + d));
                    worst = Math.Max(worst, H(x, y) - (endZ + 10 + .10 * d));
                }
                _o.WriteLine($"{s.Name,-14} {(k > 0 ? "high" : "low ")} end: {-worst:0} m under the 10 % plane");
                Check(worst < 0, $"{s.Name}: ground stands {worst:0} m into the {(k > 0 ? "high" : "low")} end's approach");
            }
        }
        Verdict();
    }

    [Fact]
    public void TheStripsDoNotSitOnTopOfEachOther()
    {
        var st = FrankChurch.Strips;
        for (int i = 0; i < st.Length; i++)
            for (int j = i + 1; j < st.Length; j++)
                for (double f = -0.5; f <= 0.5; f += 0.02)
                    for (double c = -.5; c <= .5; c += .5)
                    {
                        var (x, y) = At(st[i], f * st[i].LengthM, c * st[i].WidthM);
                        Assert.False(Field(st[j]).Contains(new IslandPoint(y / 1000, x / 1000), 60), $"{st[i].Name} runs onto {st[j].Name}'s shoulders");
                    }
    }

    /// <summary>
    /// What is drawn is what the wheels roll on. The terrain mesh is a 25 m lattice (IslandTerrainMesh, origin -20 km,
    /// split SW-NE); over a planar strip a triangle of a plane IS the plane, so the mesh and HeightAt agree to the
    /// centimetre, and the strip's paint is drawn on the same plane (IslandWorldBuilder.SurfaceQuad). A strip that was
    /// flat and then stepped, or a pad that was not a plane, would show here as a gap between the two — and did, at the
    /// thresholds, until the canyon strips' slope was carried through their end shoulders (pad "sloped_end_shoulders").
    /// The turf strip is the exception: its paint follows the ground (every 10 m), but Sulphur Creek's 1.9 m banana and
    /// its cross-sweep are finer than a 25 m triangle, and the mesh misses them by up to 0.49 m (measured 2026-11-03).
    /// That is true of every natural-shaped field on the island, not something these valleys made; held at 0.5 m.
    /// </summary>
    [Fact]
    public void TheDrawnSurfaceIsTheGroundTheWheelsRollOn()
    {
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            var f = Field(s); bool shaped = f.Shape.Kind > -.5; double worstMesh = 0, worstPaint = 0;
            for (double along = -s.LengthM / 2; along <= s.LengthM / 2; along += 7)
                for (double c = -s.WidthM / 2; c <= s.WidthM / 2; c += s.WidthM / 6)
                {
                    var (x, y) = At(s, along, c);
                    double x0 = Math.Floor((x + 20000) / 25) * 25 - 20000, y0 = Math.Floor((y + 20000) / 25) * 25 - 20000;
                    double tx = (y - y0) / 25, ty = (x - x0) / 25;
                    double a = H(x0, y0), b = H(x0, y0 + 25), d = H(x0 + 25, y0), e = H(x0 + 25, y0 + 25);
                    double mesh = tx >= ty ? a + (b - a) * tx + (e - b) * ty : a + (e - d) * tx + (d - a) * ty;
                    double truth = H(x, y);
                    worstMesh = Math.Max(worstMesh, Math.Abs(mesh - truth));
                    if (!shaped) worstPaint = Math.Max(worstPaint, Math.Abs(s.ElevationM + along * s.SlopePercent / 100 - truth));
                }
            _o.WriteLine($"{s.Name,-14} mesh vs ground {worstMesh:0.000} m, paint vs ground {worstPaint:0.000} m");
            Check(worstMesh < (shaped ? .5 : .01), $"{s.Name}: the drawn mesh is {worstMesh:0.00} m off the ground the wheels use");
            Check(worstPaint < .01, $"{s.Name}: the strip's paint is {worstPaint:0.00} m off its ground");
        }
        Verdict();
    }

    /// <summary>No cliff cut into a strip: over the ground a strip has shaped — the pad, its shoulders and its blend,
    /// — the ground never steps more than 10 m in 5 m (63 degrees). The pad blends into its banks and
    /// its walls; it is not set into them. Past a one-way strip's high end is the closing wall and its flanks, which
    /// is meant to be steep (asserted by the one-way test), and is left out. (Further out the canyon walls have the relief's own cliff bands, 20-25 m in
    /// 5 m in places, as canyon walls do; those are not this test's business.)</summary>
    [Fact]
    public void NoStripIsCutIntoACliff()
    {
        foreach (FrankChurch.Strip s in FrankChurch.Strips)
        {
            double worst = 0; string where = "";
            var f = Field(s); double ra = s.LengthM / 2 + f.EndShoulder + f.Blend, rc = s.WidthM / 2 + f.Shoulder + f.Blend;
            for (double along = -ra; along <= ra; along += 5)
            {
                var (px, py) = At(s, along, -rc); double p = H(px, py);
                for (double c = -rc + 5; c <= rc; c += 5)
                {
                    var (x, y) = At(s, along, c); double h = H(x, y);
                    bool headwall = s.OneWay && along > s.LengthM / 2;   // the wall it lands into, and its flanks
                    if (!headwall && Math.Abs(h - p) > worst) { worst = Math.Abs(h - p); where = $"{along:0} m along, {c:0} m across"; }
                    p = h;
                }
            }
            _o.WriteLine($"{s.Name,-14} worst step {worst:0.0} m in 5 m at {where}");
            Check(worst < 10, $"{s.Name}: {worst:0.0} m in 5 m at {where}");
        }
        Verdict();
    }
}

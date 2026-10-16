using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Alaska range, its glaciers, and the braided river below them (owner 2026-10-16). The things that have
/// to be true for any of it to be flyable: ice you can land along, lakes with bergs that are in the water and
/// not on the shore, bars with a clean lane down the middle, steps you can see rather than steps that eat
/// you, and a wind that is always blowing down the valley so an upstream landing is into it.
/// </summary>
public class AlaskaTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t;

    public AlaskaTests(ITestOutputHelper o)
    {
        _o = o;
        _t = new WorldTerrain();
        WorldTerrain.Active = _t;
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
    }

    public void Dispose()
    {
        WorldTerrain.Active = null;
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    // ---- the mountain ----

    [Fact]
    public void ThereIsADenaliAndItIsHuge()
    {
        AlaskaRange.Peak denali = AlaskaRange.Peaks[0];
        double top = 0;
        for (double dx = -120; dx <= 120; dx += 40)
        for (double dy = -120; dy <= 120; dy += 40)
            top = Math.Max(top, _t.HeightAt(denali.X + dx, denali.Y + dy));
        _o.WriteLine($"{denali.Name}: {top:0} m ({top * 3.28084:0} ft), {denali.RadiusM * 2 / 1000:0.0} km across the foot");
        // 16,700 ft, not 20,000: the plateau it stands on is only 3 km deep between its walls, and a mountain
        // with a footprint bigger than that swallows the escarpments either side of it. It is still 5,600 ft
        // taller than anything else in this world.
        Assert.True(top > 4800, $"{denali.Name} is only {top * 3.28084:0} ft");
        Assert.True(denali.RadiusM > 1800, "a mountain that tall needs a footprint, or it reads as a spike");
        double tallestElsewhere = 0;
        foreach (AlpineRange.Peak p in AlpineRange.Peaks) tallestElsewhere = Math.Max(tallestElsewhere, p.TopM);
        Assert.True(top > tallestElsewhere + 1500, "it should tower over the Cascades");
    }

    // ---- the glaciers ----

    [Fact]
    public void EveryGlacierCanBeLandedAlong()
    {
        // A glacier landing is made UP the ice. Below the icefall the slope has to be shallow enough to fly
        // onto — real ones run 3–6° where people land.
        foreach (AlaskaRange.Glacier g in AlaskaRange.Glaciers)
        {
            _o.WriteLine($"{g.Name,-8} {g.LengthM / 1000:0.0} km long, average {g.SlopeDeg:0.0}°, " +
                         $"{g.HeadM:0} m → {g.TermM:0} m, {g.HalfWidthM * 2:0} m wide");
            // A mountain glacier averages steeply — it starts in an icefall. What matters is the tongue.
            Assert.InRange(g.SlopeDeg, 2.0, 30.0);
            // The lower half, where a landing would actually be made, is shallower than the average.
            double lower = (g.IceHeightAt(0.55, 0) - g.IceHeightAt(1.0, 0)) / (g.LengthM * 0.45);
            double lowerDeg = Math.Atan(lower) * 180 / Math.PI;
            _o.WriteLine($"         lower half: {lowerDeg:0.0}°");
            Assert.InRange(lowerDeg, 1.0, 9.0);
            // Wide enough to put an aeroplane on with room either side.
            Assert.True(g.HalfWidthM * 2 > 300, $"{g.Name} is only {g.HalfWidthM * 2:0} m wide");
        }
    }

    [Fact]
    public void TheIceIsSnowUnderTheWheelsAndTheRockIsNot()
    {
        foreach (AlaskaRange.Glacier g in AlaskaRange.Glaciers)
        {
            double mx = (g.HeadX + g.TermX) * 0.5, my = (g.HeadY + g.TermY) * 0.5;
            Assert.True(AlaskaRange.OnIce(mx, my), $"the middle of the {g.Name} is not ice");
            Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(mx, my));
            // Off the side of the glacier it is mountain again.
            double dx = (g.TermX - g.HeadX) / g.LengthM, dy = (g.TermY - g.HeadY) / g.LengthM;
            double ox = mx - dy * (g.HalfWidthM + 150), oy = my + dx * (g.HalfWidthM + 150);
            Assert.False(AlaskaRange.OnIce(ox, oy));
            Assert.NotEqual(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(ox, oy));
        }
    }

    [Fact]
    public void EachGlacierEndsInALakeWithBergsFloatingInIt()
    {
        foreach (AlaskaRange.Glacier g in AlaskaRange.Glaciers)
        {
            var (cx, cy) = g.LakeCentre;
            double? w = _t.WaterSurfaceAt(cx, cy, g.LakeSurfaceM);
            Assert.True(w.HasValue, $"{g.Name} has no lake at its foot");
            Assert.Equal(g.LakeSurfaceM, w!.Value, 3);
            Assert.True(_t.HeightAt(cx, cy) < g.LakeSurfaceM - 2, "the lake needs a bed under it");
            // The ice front stands above the water it calves into.
            Assert.True(g.TermM > g.LakeSurfaceM, $"{g.Name}'s ice front is below its own lake");

            int bergs = 0;
            foreach (var (bx, by, radius, above) in AlaskaRange.Icebergs(g))
            {
                bergs++;
                Assert.True(AlaskaRange.WaterSurfaceAt(bx, by).HasValue,
                            $"a berg in {g.Name}'s lake is aground at ({bx:0},{by:0})");
                Assert.InRange(radius, 5, 30);
                Assert.True(above > 1 && above < radius, "a berg shows a fraction of itself above water");
            }
            _o.WriteLine($"{g.Name,-8} lake at {g.LakeSurfaceM:0} m, {bergs} bergs");
            Assert.True(bergs >= 8, "a proglacial lake should be full of them");
        }
    }

    // ---- the braided river ----

    [Fact]
    public void EveryBarHasACleanLaneToLandOn()
    {
        // The rule that makes the hazards fair: scattered everywhere, but never in the lane.
        var hazards = BraidedRiver.Hazards();
        _o.WriteLine($"{hazards.Count} obstacles on the plain");
        Assert.True(hazards.Count > 80, "the owner asked for gravel bars with obstacles, not a billiard table");
        foreach (BraidedRiver.Bar b in BraidedRiver.Bars)
        {
            int inLane = 0;
            foreach (BraidedRiver.Hazard hz in hazards) if (b.InCleanLane(hz.X, hz.Y)) inLane++;
            Assert.Equal(0, inLane);
        }
        // And the lane is big enough for the aeroplane it is meant for: a Beaver spans 14.6 m.
        Assert.True(BraidedRiver.CleanLaneHalfWidthM * 2 > 20);
        Assert.True(BraidedRiver.CleanLaneLengthM > 450);
    }

    [Fact]
    public void TheLanesAreActuallyFlatEnoughToLandOn()
    {
        foreach (BraidedRiver.Bar b in BraidedRiver.Bars)
        {
            double h = b.HeadingDeg * Math.PI / 180;
            double lo = double.MaxValue, hi = double.MinValue, worstStep = 0, prev = double.NaN;
            double half = Math.Min(BraidedRiver.CleanLaneLengthM, b.LengthM) * 0.5;
            for (double a = -half; a <= half; a += 10)
            {
                double gx = b.X + Math.Cos(h) * a, gy = b.Y + Math.Sin(h) * a;
                double gh = _t.HeightAt(gx, gy);
                lo = Math.Min(lo, gh); hi = Math.Max(hi, gh);
                if (!double.IsNaN(prev)) worstStep = Math.Max(worstStep, Math.Abs(gh - prev));
                prev = gh;
            }
            double slopePct = (hi - lo) / (2 * half) * 100;
            _o.WriteLine($"{b.Name,-16} lane: {hi - lo:0.00} m of rise ({slopePct:0.00} %), worst step {worstStep:0.00} m in 10 m");
            // An outwash bar slopes at the valley's own gradient, about 1–2 %, and landing UP it into the
            // glacier wind is the whole technique. What would be wrong is a step, not a slope.
            Assert.True(slopePct < 2.5, $"{b.Name}'s lane falls at {slopePct:0.0} %");
            Assert.True(worstStep < 0.7, $"{b.Name} has a {worstStep:0.0} m step in the landing lane");
        }
    }

    [Fact]
    public void TheStepsOffTheBarsAreCappedSoTheyCanBeSurvived()
    {
        // Sharp is fine — the owner asked for sharp but visible — and nothing is sharper than the cap.
        foreach (BraidedRiver.Bar b in BraidedRiver.Bars)
        {
            Assert.True(b.RaiseM <= BraidedRiver.MaxStepM, $"{b.Name} stands {b.RaiseM:0.0} m proud");
            // The STEP is the drop at the edge — measured from the bar's surface right at its end, not from
            // its middle, which on a 900 m bar on a 1.7 % plain is seven metres higher for honest reasons.
            double h = b.HeadingDeg * Math.PI / 180;
            double halfL = b.LengthM * 0.5;
            double onBar = _t.HeightAt(b.X - Math.Cos(h) * (halfL - 4), b.Y - Math.Sin(h) * (halfL - 4));
            double offBar = _t.HeightAt(b.X - Math.Cos(h) * (halfL + 8), b.Y - Math.Sin(h) * (halfL + 8));
            double step = onBar - offBar;
            _o.WriteLine($"{b.Name,-16} {(b.SharpLip ? "cut bank" : "ramp")}: {step:0.00} m step at the downstream end");
            Assert.True(step <= BraidedRiver.MaxStepM + 0.3, $"{b.Name}'s lip is {step:0.0} m");
            if (b.SharpLip) Assert.True(step > 0.8, $"{b.Name} is supposed to be a cut bank, but its step is {step:0.0} m");
        }
    }

    [Fact]
    public void TheChannelsAreShallowEnoughToSkimAndTheBarsAreDry()
    {
        int wet = 0, dry = 0;
        for (double y = BraidedRiver.Y0 + 200; y < BraidedRiver.Y1 - 200; y += 150)
        for (int braid = 0; braid < BraidedRiver.Braids; braid++)
        {
            double cx = BraidedRiver.ChannelCentreX(braid, y);
            double? w = BraidedRiver.WaterSurfaceAt(cx, y);
            if (!w.HasValue) continue;
            wet++;
            double bed = _t.HeightAt(cx, y);
            double depth = w.Value - bed;
            Assert.InRange(depth, 0.05, 1.3);          // a hand's depth: a wheel skims it, a float cannot use it
        }
        foreach (BraidedRiver.Bar b in BraidedRiver.Bars)
        {
            Assert.Null(BraidedRiver.WaterSurfaceAt(b.X, b.Y));
            dry++;
        }
        _o.WriteLine($"{wet} wet samples in the braids, {dry} bars dry");
        Assert.True(wet > 40, "there should be water in the braids");
    }

    [Fact]
    public void TheGlacierWindAlwaysBlowsDownTheValley()
    {
        // Owner: an ever-present wind off the ice, so landing upstream on a bar is into a headwind.
        Atmosphere.SteadyWind = Vec3.Zero;
        foreach (BraidedRiver.Bar b in BraidedRiver.Bars)
        {
            double up = _t.HeightAt(b.X, b.Y) + 15;
            Vec3 w = Atmosphere.WindAtPosition(new Vec3(b.X, b.Y, -up));
            double speed = w.Length;
            // The lane points up-valley; the wind must oppose it.
            double h = b.LaneHeadingDeg * Math.PI / 180;
            var laneDir = new Vec3(Math.Cos(h), Math.Sin(h), 0);
            double head = -(w.X * laneDir.X + w.Y * laneDir.Y);
            _o.WriteLine($"{b.Name,-16} wind {speed * 1.943844:0} kt, headwind component landing up the lane {head * 1.943844:0} kt");
            Assert.True(speed > 3, $"{b.Name} has almost no glacier wind ({speed:0.0} m/s)");
            Assert.True(head > 0.5 * speed, $"{b.Name}: landing up the lane is not into the wind");
        }
        // And it fades with height: it is a shallow layer off the ice, not a gale aloft.
        Vec3 low = Atmosphere.WindAtPosition(new Vec3(BraidedRiver.Bars[0].X, BraidedRiver.Bars[0].Y, -(BraidedRiver.HeadM + 20)));
        Vec3 high = Atmosphere.WindAtPosition(new Vec3(BraidedRiver.Bars[0].X, BraidedRiver.Bars[0].Y, -(BraidedRiver.HeadM + 900)));
        Assert.True(high.Length < low.Length * 0.4, "the glacier wind should die away with height");
        // Outside the plain there is none of it.
        Assert.Equal(0, Atmosphere.WindAtPosition(new Vec3(0, 0, -500)).Length, 6);
    }

    [Fact]
    public void ThePracticeStripIsTheSizeTheOwnerAskedForAndIsMarked()
    {
        BraidedRiver.NamedStrip s = Array.Find(BraidedRiver.NamedStrips, n => n.Name == "Practice Strip");
        _o.WriteLine($"{s.Name}: {s.LengthFt:0} ft × {s.WidthFt:0} ft");
        Assert.Equal(2500, s.LengthFt, 0);
        Assert.Equal(100, s.WidthFt, 0);
        Assert.Equal(WorldTerrain.Surface.Gravel, WorldTerrain.SurfaceAt(s.X, s.Y));

        // Cones down both sides, and enough of them to see the edges from the air.
        int cones = 0;
        foreach (var (cx, cy) in BraidedRiver.Cones(s))
        {
            cones++;
            var (_, across) = s.Local(cx, cy);
            Assert.True(Math.Abs(across) > s.WidthM * 0.5, "a cone on the strip is a cone somebody hits");
            Assert.True(Math.Abs(across) < s.WidthM * 0.5 + 6, "the cones mark the edges, not a field");
        }
        _o.WriteLine($"  {cones} cones");
        Assert.True(cones >= 24);

        // Chairs beside the chalk line, off the strip, facing it.
        int chairs = 0;
        foreach (var (cx, cy, hdg) in BraidedRiver.Chairs(s))
        {
            chairs++;
            Assert.False(s.On(cx, cy), "a chair on the runway");
            var (along, across) = s.Local(cx, cy);
            Assert.True(Math.Abs(across) > s.WidthM * 0.5 + 4, "the chairs sit well back");
            Assert.True(Math.Abs(along - BraidedRiver.ChalkLineFrac * s.LengthM) < 60, "they watch the chalk line");
            _ = hdg;
        }
        _o.WriteLine($"  {chairs} chairs by the line");
        Assert.Equal(6, chairs);
    }

    [Fact]
    public void ThePicnicStripIsByTheIceAndHasItsBenches()
    {
        BraidedRiver.NamedStrip s = Array.Find(BraidedRiver.NamedStrips, n => n.Name == "Picnic Strip");
        // Near the Knik's lake — you land here to look at the glacier.
        AlaskaRange.Glacier knik = Array.Find(AlaskaRange.Glaciers, g => g.Name == "Knik");
        var (lx, ly) = knik.LakeCentre;
        double d = Math.Sqrt((s.X - lx) * (s.X - lx) + (s.Y - ly) * (s.Y - ly));
        _o.WriteLine($"{s.Name}: {s.LengthFt:0} ft, {d / 1000:0.0} km from the Knik's lake");
        Assert.True(d < 2500, $"the picnic strip is {d / 1000:0.0} km from the ice");

        int benches = 0;
        foreach (var (bx, by, _) in BraidedRiver.PicnicBenches(s))
        {
            benches++;
            Assert.False(s.On(bx, by), "a bench on the runway");
        }
        Assert.Equal(4, benches);
    }

    [Fact]
    public void BothMadeStripsAreGradedFlatAndClearOfObstacles()
    {
        var hazards = BraidedRiver.Hazards();
        foreach (BraidedRiver.NamedStrip s in BraidedRiver.NamedStrips)
        {
            foreach (BraidedRiver.Hazard hz in hazards)
                Assert.False(s.On(hz.X, hz.Y, 30), $"{s.Name} has a {hz.Kind} on it");
            // Flat across, and no steps along.
            // A graded strip is a STRAIGHT PLANE, not a level one: it follows the valley's 1.7 %. So what
            // matters is how far the surface departs from a straight line between its ends, not the drop
            // between two samples thirty metres apart, which is just the gradient doing its job.
            var (ax, ay) = s.At(-0.48, 0);
            var (bx2, by2) = s.At(0.48, 0);
            double hA = _t.HeightAt(ax, ay), hB = _t.HeightAt(bx2, by2);
            double worst = 0;
            for (double f = -0.48; f <= 0.48; f += 0.04)
            {
                var (cx, cy) = s.At(f, 0);
                double mid = _t.HeightAt(cx, cy);
                double ideal = hA + (hB - hA) * ((f + 0.48) / 0.96);
                worst = Math.Max(worst, Math.Abs(mid - ideal));
                for (int k = -1; k <= 1; k += 2)
                {
                    var (ex, ey) = s.At(f, k * s.WidthM * 0.45);
                    Assert.Equal(mid, _t.HeightAt(ex, ey), 1);
                }
            }
            double slopePct = (hB - hA) / (0.96 * s.LengthM) * 100;
            _o.WriteLine($"{s.Name,-16} {slopePct:+0.00;-0.00} % along it, {worst * 100:0.0} cm out of true");
            Assert.True(worst < 0.10, $"{s.Name} is {worst * 100:0} cm out of true");
            Assert.Null(BraidedRiver.WaterSurfaceAt(s.X, s.Y));
        }
    }

    [Fact]
    public void ThePlainOwnsItsOwnGround()
    {
        // The invariant that two separate mistakes broke: the plain was laid first on top of the Cascades and
        // then across the tunnel maze, where the "ground" is a bore floor 440 m under the plateau. Everywhere
        // on the plain, the world's height has to be the plain's own height.
        int checked_ = 0;
        for (double x = BraidedRiver.X0 + 100; x < BraidedRiver.X1 - 100; x += 150)
        for (double y = BraidedRiver.Y0 + 100; y < BraidedRiver.Y1 - 100; y += 150)
        {
            double mine = BraidedRiver.HeightAt(x, y, _t.BaseHeightAt(x, y));
            double world = _t.HeightAt(x, y);
            Assert.True(Math.Abs(world - mine) < 3.0,
                        $"something else owns the ground at ({x:0},{y:0}): the plain says {mine:0.0}, the world says {world:0.0}");
            checked_++;
        }
        _o.WriteLine($"{checked_} points on the plain, all its own");
        Assert.True(checked_ > 400);
    }

    [Fact]
    public void TheBouldersAreSolidAndTheBushesAreNot()
    {
        BraidedRiver.RegisterSolids();
        var hazards = BraidedRiver.Hazards();
        int rocks = 0, bushes = 0;
        foreach (BraidedRiver.Hazard hz in hazards)
        {
            if (hz.Kind == BraidedRiver.Obstacle.Bush) { bushes++; continue; }
            rocks++;
            double gh = BraidedRiver.PlainHeightAt(hz.X, hz.Y);
            Assert.NotNull(WorldSolids.Penetration(hz.X, hz.Y, gh + hz.HeightM * 0.5));
        }
        _o.WriteLine($"{rocks} solid obstacles, {bushes} bushes you can mow");
        Assert.True(rocks > 20 && bushes > 10, "there should be both kinds");
        Assert.Equal(rocks, WorldSolids.Boxes.Count);
    }
}

using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Cascades-style range and its alpine lakes (owner 2026-10-16). The facts that decide whether the lakes
/// can be used rather than only looked at: the lengths he asked for, water you can float on, a rim that
/// really does enclose the bowled ones, a notch you can actually fly in through on the open ones, and enough
/// room inside a bowl to turn an aeroplane round in — because "circling down and up are the only ways in"
/// is only true if the circle fits.
/// </summary>
public class AlpineRangeTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t;

    public AlpineRangeTests(ITestOutputHelper o)
    {
        _o = o;
        _t = new WorldTerrain();
        WorldTerrain.Active = _t;
    }

    public void Dispose() => WorldTerrain.Active = null;

    [Fact]
    public void TheLakesAreTheLengthsTheOwnerAskedFor()
    {
        // 1,000 ft to 3,000 ft, and the set has to actually span that.
        double shortest = double.MaxValue, longest = 0;
        foreach (AlpineRange.Lake l in AlpineRange.Lakes)
        {
            Assert.InRange(l.LengthFt, 990, 3010);
            shortest = Math.Min(shortest, l.LengthFt);
            longest = Math.Max(longest, l.LengthFt);
        }
        Assert.InRange(shortest, 990, 1100);
        Assert.InRange(longest, 2900, 3010);
        Assert.Equal(6, AlpineRange.Lakes.Length);
    }

    [Fact]
    public void SomeAreOpenAtOneEndAndSomeAreFullBowls()
    {
        int open = 0, bowls = 0;
        foreach (AlpineRange.Lake l in AlpineRange.Lakes) if (l.Bowled) bowls++; else open++;
        Assert.True(open >= 2, "some lakes have to have a way in at one end");
        Assert.True(bowls >= 2, "and some have to be fully in a bowl");
    }

    [Fact]
    public void NoTwoBasinsOverlap()
    {
        // The invariant the first layout broke: two basins a kilometre apart carved each other's rims away,
        // and a bowl with a hole in it is not a bowl. Each basin needs its own ground.
        AlpineRange.Lake[] lk = AlpineRange.Lakes;
        for (int i = 0; i < lk.Length; i++)
        for (int j = i + 1; j < lk.Length; j++)
        {
            double d = Math.Sqrt((lk[i].X - lk[j].X) * (lk[i].X - lk[j].X) + (lk[i].Y - lk[j].Y) * (lk[i].Y - lk[j].Y));
            double need = lk[i].BasinRadiusM + lk[j].BasinRadiusM;
            Assert.True(d > need, $"{lk[i].Name} and {lk[j].Name} are {d:0} m apart but their basins want {need:0} m");
        }
        // And a basin must not swallow a summit either.
        foreach (AlpineRange.Lake l in lk)
        foreach (AlpineRange.Peak p in AlpineRange.Peaks)
        {
            double d = Math.Sqrt((l.X - p.X) * (l.X - p.X) + (l.Y - p.Y) * (l.Y - p.Y));
            Assert.True(d > 1300, $"{p.Name} is only {d:0} m from {l.Name} — the basin would cut the summit off");
        }
    }

    [Fact]
    public void EveryLakeIsWaterYouCanFloatOn()
    {
        foreach (AlpineRange.Lake l in AlpineRange.Lakes)
        {
            // Flat water at the surface height, across the whole lake.
            foreach (double f in new[] { 0.0, 0.4, 0.8 })
            {
                double h = l.HeadingDeg * Math.PI / 180;
                double x = l.X + Math.Cos(h) * f * l.LengthM * 0.45;
                double y = l.Y + Math.Sin(h) * f * l.LengthM * 0.45;
                double? w = _t.WaterSurfaceAt(x, y, l.SurfaceM);
                Assert.True(w.HasValue, $"{l.Name}: no water at {f:0.0} along it");
                Assert.Equal(l.SurfaceM, w!.Value, 3);
                // And a bed below it, so a float has something to displace.
                Assert.True(_t.HeightAt(x, y) < l.SurfaceM - 2, $"{l.Name}: the bed is not under the water");
            }
            // Dry ground a little outside the shoreline.
            double hh = l.HeadingDeg * Math.PI / 180;
            double ox = l.X + Math.Cos(hh) * (l.LengthM * 0.5 + 120);
            double oy = l.Y + Math.Sin(hh) * (l.LengthM * 0.5 + 120);
            Assert.Null(AlpineRange.WaterSurfaceAt(ox, oy));
        }
    }

    [Fact]
    public void TheBowledLakesAreReallyEnclosed()
    {
        // The whole way round, the rim stands well above the water: no straight-in approach exists, so the
        // only way in is down inside the basin.
        foreach (AlpineRange.Lake l in AlpineRange.Lakes)
        {
            if (!l.Bowled) continue;
            double lowest = double.MaxValue, lowestAt = 0;
            for (double deg = 0; deg < 360; deg += 4)
            {
                double a = deg * Math.PI / 180;
                double peak = 0;
                // The highest ground anywhere on this bearing between the shore and the basin's edge — that is
                // what an aeroplane coming in from that direction has to clear.
                for (double d = l.LengthM * 0.5 + 60; d < l.BasinRadiusM + 260; d += 25)
                    peak = Math.Max(peak, _t.HeightAt(l.X + Math.Cos(a) * d, l.Y + Math.Sin(a) * d));
                if (peak < lowest) { lowest = peak; lowestAt = deg; }
            }
            _o.WriteLine($"{l.Name,-8} bowl: lowest point on the rim {lowest - l.SurfaceM,5:0} m above the water (bearing {lowestAt:0}°)");
            Assert.True(lowest > l.SurfaceM + 110,
                        $"{l.Name}: the rim is only {lowest - l.SurfaceM:0} m above the water at {lowestAt:0}° — that is a way in");
        }
    }

    [Fact]
    public void ThereIsRoomToTurnInsideEveryBowl()
    {
        // A Beaver on floats at 7,000 ft turns in about 350 m of radius at 20° of bank, so a bowl needs some
        // 700 m of clear diameter at the height a pilot would be circling at. Without this, "spiral in and
        // out" is a sentence rather than a manoeuvre.
        foreach (AlpineRange.Lake l in AlpineRange.Lakes)
        {
            if (!l.Bowled) continue;
            double at = l.SurfaceM + 150;          // circling height inside the basin
            double worst = double.MaxValue;
            for (double deg = 0; deg < 360; deg += 10)
            {
                double a = deg * Math.PI / 180;
                double d = 0;
                while (d < l.BasinRadiusM + 400 && _t.HeightAt(l.X + Math.Cos(a) * d, l.Y + Math.Sin(a) * d) < at) d += 20;
                worst = Math.Min(worst, d);
            }
            _o.WriteLine($"{l.Name,-8} bowl: {worst * 2,5:0} m of clear diameter at 150 m above the water");
            Assert.True(worst * 2 > 700, $"{l.Name}: only {worst * 2:0} m across at circling height — no room to turn");
        }
    }

    [Fact]
    public void TheOpenLakesHaveOneWayInAndItIsFlyable()
    {
        foreach (AlpineRange.Lake l in AlpineRange.Lakes)
        {
            if (l.Bowled) continue;
            double a = l.OpenHeadingDeg!.Value * Math.PI / 180;
            // Down the notch: the ground stays below the lake's own surface for a good distance out, which is
            // what makes a straight-in approach possible.
            for (double d = l.LengthM * 0.5 + 80; d < l.BasinRadiusM; d += 40)
            {
                double h = _t.HeightAt(l.X + Math.Cos(a) * d, l.Y + Math.Sin(a) * d);
                Assert.True(h < l.SurfaceM, $"{l.Name}: the notch is blocked {d:0} m out ({h - l.SurfaceM:+0;-0} m)");
            }
            // And it is wide enough to fly down: at least 300 m across, measured at the lake's own level.
            double wide = 0;
            for (double off = -400; off <= 400; off += 20)
            {
                double px = l.X + Math.Cos(a) * (l.BasinRadiusM * 0.6) - Math.Sin(a) * off;
                double py = l.Y + Math.Sin(a) * (l.BasinRadiusM * 0.6) + Math.Cos(a) * off;
                if (_t.HeightAt(px, py) < l.SurfaceM) wide += 20;
            }
            _o.WriteLine($"{l.Name,-8} notch: {wide:0} m wide at the lake's level");
            Assert.True(wide >= 300, $"{l.Name}: the notch is only {wide:0} m wide");
            // The opposite side is still a wall — one way in, not a through-valley.
            double back = 0;
            for (double d = l.LengthM * 0.5 + 60; d < l.BasinRadiusM + 200; d += 25)
                back = Math.Max(back, _t.HeightAt(l.X - Math.Cos(a) * d, l.Y - Math.Sin(a) * d));
            Assert.True(back > l.SurfaceM + 80, $"{l.Name}: the far end is open too — it is a valley, not a lake in a basin");
        }
    }

    [Fact]
    public void ThePeaksAreMountainsAndTheyCarrySnow()
    {
        // Above 2,500 m the terrain shader puts snow on them, which is what makes it read as the Cascades.
        foreach (AlpineRange.Peak p in AlpineRange.Peaks)
        {
            double top = 0;
            for (double dx = -60; dx <= 60; dx += 20)
            for (double dy = -60; dy <= 60; dy += 20)
                top = Math.Max(top, _t.HeightAt(p.X + dx, p.Y + dy));
            _o.WriteLine($"{p.Name,-16} {top,6:0} m ({top * 3.28084,6:0} ft)");
            Assert.InRange(top, p.TopM - 60, p.TopM + 120);
            Assert.True(top > 2500, $"{p.Name} is below the snow line");
        }
    }

    [Fact]
    public void TheRangeStandsOnTheSummitPlateauAndLeavesEverythingElseAlone()
    {
        // It sits east of the river on flat ground, and nothing outside its block moves by a millimetre.
        Assert.True(AlpineRange.X0 > 2000, "the range must stay clear of the river's meander");
        // Between the feet of the mountains the plateau is still the plateau: sample everywhere in the block
        // that no cone, no rim and no basin reaches, and it must be untouched to the millimetre.
        int sampled = 0;
        for (double x = AlpineRange.X0; x <= AlpineRange.X1; x += 200)
        for (double y = AlpineRange.Y0; y <= AlpineRange.Y1; y += 200)
        {
            bool clear = true;
            foreach (AlpineRange.Peak p in AlpineRange.Peaks)
                if ((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y) < (p.RadiusM + 50) * (p.RadiusM + 50)) clear = false;
            foreach (AlpineRange.Lake l in AlpineRange.Lakes)
                if ((x - l.X) * (x - l.X) + (y - l.Y) * (y - l.Y) < (l.BasinRadiusM + 550) * (l.BasinRadiusM + 550)) clear = false;
            if (!clear) continue;
            Assert.Equal(AlpineRange.BaseM, _t.HeightAt(x, y), 0);
            sampled++;
        }
        _o.WriteLine($"plateau between the mountains: {sampled} points untouched");
        Assert.True(sampled > 15, "there should still be some plateau between the feet of the mountains");
        // And outside the range entirely, nothing has moved at all.
        foreach ((double x, double y) in new[]
                 { (AlpineRange.X1 + 700, -9000.0), (AlpineRange.X1 + 1200, -11000.0),
                   (4000.0, AlpineRange.Y0 - 800), (6000.0, AlpineRange.Y0 - 800) })
            Assert.Equal(AlpineRange.BaseM, _t.HeightAt(x, y), 0);
        // And the Summit airport is untouched.
        WorldTerrain.Airport summit = WorldTerrain.Airports[3];
        Assert.Equal(summit.ElevationM, _t.HeightAt(summit.X, summit.Y), 0);
    }
}

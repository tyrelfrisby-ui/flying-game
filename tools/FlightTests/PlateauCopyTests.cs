using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlightTests;

/// <summary>Owner 2026-09-09: 1,500 ft plateau steps, and the SAME landscape repeated on every step.</summary>
public class PlateauCopyTests
{
    [Fact]
    public void StepsAre1500Feet()
    {
        for (int i = 0; i < WorldTerrain.Airports.Length; i++)
            Assert.InRange((WorldTerrain.Airports[i].ElevationM - WorldTerrain.DatumM) * 3.28084, 1500 * i - 1, 1500 * i + 1);
    }

    [Fact]
    public void EveryPlateauCarriesTheValleyLandscapeOnFlatGround()
    {
        var t = new WorldTerrain();
        for (int p = 0; p < WorldTerrain.PlateauCount; p++)
        {
            double elev = WorldTerrain.Airports[p].ElevationM, dy = WorldTerrain.PlateauDy(p);
            // Race course, aerobatic box, crop field, town and tower all sit on this plateau's level.
            foreach (RaceElement e in RaceCourse.ElementsFor(p)) Assert.InRange(t.HeightAt(e.X, e.Y), elev - 1, elev + 30);
            Assert.InRange(t.HeightAt(AeroBox.CenterX, AeroBox.CenterYAt(p)), elev - 1, elev + 1);
            CropField f = CropField.For(p);
            Assert.Equal(elev, f.ElevationM);
            Assert.InRange(t.HeightAt(f.WireX, f.PoleY0), elev - 1, elev + 1);
            var (ccx, ccy) = ValleyCity.At(p).Centre;
            Assert.InRange(t.HeightAt(ccx, ccy), elev - 1, elev + 1);
            Assert.InRange(t.HeightAt(Landmarks.TowerX, Landmarks.TowerYAt(p)), elev - 1, elev + 1);
            // Exact copies: the same x, y shifted by the plateau offset.
            Assert.Equal(RaceCourse.Elements[3].Y + dy, RaceCourse.ElementsFor(p)[3].Y, 6);
            Assert.Equal(ValleyCity.At(0).Towers()[17].Cy + dy, ValleyCity.At(p).Towers()[17].Cy, 6);
            Assert.Equal(ValleyCity.At(0).Towers()[17].HeightM, ValleyCity.At(p).Towers()[17].HeightM, 6);
            // The arch spans the river in its gorge on every plateau: legs on the rims, well below the deck level.
            Assert.True(t.WaterSurfaceAt(Landmarks.ArchCentreXAt(p), Landmarks.ArchYAt(p)).HasValue, $"river under the arch on plateau {p}");
        }
    }

    [Fact]
    public void SolidsAndAeroBoxAreRegisteredOnEveryPlateau()
    {
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        try
        {
            WorldSolids.Boxes.Clear();
            Landmarks.RegisterSeasideSolids(t);
            int seaside = WorldSolids.Boxes.Count;
            WorldSolids.Boxes.Clear();
            Landmarks.RegisterSolids(t, 0);
            int valley = WorldSolids.Boxes.Count;
            Landmarks.RegisterSolids(t);
            Assert.Equal(valley * WorldTerrain.PlateauCount + seaside, WorldSolids.Boxes.Count);   // every plateau's copy + the one seaside
            for (int p = 0; p < WorldTerrain.PlateauCount; p++)
            {
                double elev = WorldTerrain.Airports[p].ElevationM;
                // The city's tallest tower is solid at 100 m AGL on each plateau.
                FlyCity.Tower b = ValleyCity.At(p).Towers().OrderByDescending(x => x.HeightM).First();
                Assert.NotNull(WorldSolids.Penetration(b.Cx, b.Cy, elev + 100));
                Assert.True(AeroBox.Inside(new Vec3(AeroBox.CenterX, AeroBox.CenterYAt(p), -(elev + 300))));
                var race = new AirRace(p);
                RaceElement g1 = RaceCourse.ElementsFor(p)[0];
                race.Update(new Vec3(g1.X - 50, g1.Y, -(elev + 20)), 0, 0.1);
                race.Update(new Vec3(g1.X + 50, g1.Y, -(elev + 20)), 0, 0.1);
                Assert.True(race.Running, $"race on plateau {p} should start through its own gate 1");
            }
        }
        finally { WorldTerrain.Active = null; WorldSolids.Boxes.Clear(); }
    }

    [Fact]
    public void TheValleyCityIsTheCombatCityOnFlatDryGroundClearOfTheOtherFeatures()
    {
        // Owner 2026-10-05: "make the city near the initial airport identical to the one you built in the combat zone".
        var t = new WorldTerrain();
        CityGrid combat = FlyCity.Grid;
        for (int p = 0; p < WorldTerrain.PlateauCount; p++)
        {
            CityGrid city = ValleyCity.At(p);
            // Identical: the same towers (heights, sizes — turned 90°), the same sky-bridges.
            var byCell = city.Towers().ToDictionary(x => (x.I, x.J));
            int same = 0;
            foreach (FlyCity.Tower c in combat.Towers())
                if (byCell.TryGetValue((c.I, c.J), out FlyCity.Tower v) && Math.Abs(v.HeightM - c.HeightM) < 1e-9 && Math.Abs(v.Hx - c.Hy) < 1e-9 && Math.Abs(v.Hy - c.Hx) < 1e-9) same++;
            Assert.True(same >= combat.Towers().Count - 3, $"plateau {p}: {same} of {combat.Towers().Count} towers identical");
            Assert.True(city.SkyBridges().Count >= combat.SkyBridges().Count - 2);
            // On flat, dry ground at the field elevation, clear of the runway corridor and the plateau's other features.
            double elev = WorldTerrain.Airports[p].ElevationM, ay = WorldTerrain.Airports[p].Y;
            var (x0, y0, x1, y1) = city.Extent(40);
            for (double x = x0; x <= x1; x += 40)
                for (double y = y0; y <= y1; y += 40)
                {
                    Assert.InRange(t.HeightAt(x, y), elev - 1, elev + 1);
                    Assert.False(FloatHydro.WaterSurfaceAt(x, y).HasValue, $"water under the city at {x:F0},{y:F0} (plateau {p})");
                }
            Assert.True(y0 > ay + 450, $"plateau {p}: the city reaches within {y0 - ay:F0} m of the runway centreline");
            bool Near(double x, double y, double m) => x > x0 - m && x < x1 + m && y > y0 - m && y < y1 + m;
            foreach (RaceElement e in RaceCourse.ElementsFor(p)) Assert.False(Near(e.X, e.Y, 60), $"race element at {e.X:F0},{e.Y:F0} in the city (plateau {p})");
            Assert.False(Near(AeroBox.CenterX, AeroBox.CenterYAt(p), AeroBox.SizeM / 2), "aerobatic box over the city");
            CropField f = CropField.For(p);
            Assert.False(Near(f.X0, f.Y0, 60) || Near(f.X1, f.Y1, 60), "crop field in the city");
            Assert.False(Near(Landmarks.TowerX, Landmarks.TowerYAt(p), 100), "the Eiffel-style tower in the city");
        }
    }
}

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
            Assert.InRange(t.HeightAt(Landmarks.TownCentreX, Landmarks.TownCentreYAt(p)), elev - 1, elev + 1);
            Assert.InRange(t.HeightAt(Landmarks.TowerX, Landmarks.TowerYAt(p)), elev - 1, elev + 1);
            // Exact copies: the same x, y shifted by the plateau offset.
            Assert.Equal(RaceCourse.Elements[3].Y + dy, RaceCourse.ElementsFor(p)[3].Y, 6);
            Assert.Equal(Landmarks.Buildings()[17].Cy + dy, Landmarks.Buildings(p)[17].Cy, 6);
            Assert.Equal(Landmarks.Buildings()[17].HeightM, Landmarks.Buildings(p)[17].HeightM, 6);
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
                // A tall downtown tower is solid at 100 m AGL on each plateau; the box is inside at 300 m AGL over its centre.
                var b = Landmarks.Buildings(p)[8 * Landmarks.TownBlocksY + 5];
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
}

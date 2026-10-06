using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Things to fly under and through: a Gateway-style arch spanning the river in its gorge, an Eiffel-style
/// tower with a road running under it, and a grid town whose buildings rise from bungalows at the edge to
/// skyscrapers downtown — with a sky bridge between twin towers and a tower with a hole through it. Every
/// building (and every leg) is a solid the airframe collides with (<see cref="WorldSolids"/>).
/// The constants describe the Valley copy; the <c>…At(p)</c> accessors give the copy on plateau p (owner: the
/// same landscape on every step).
/// </summary>
public static class Landmarks
{
    // ---- Gateway arch over the river (legs stand on the lower gorge walls, the arch rises past the rim) ----
    public const double ArchY = -600.0, ArchHalfSpanM = 75.0, ArchHeightM = 175.0, ArchLegWidthM = 14.0, ArchTopWidthM = 5.0;
    public static double ArchCentreX => WorldTerrain.RiverCentreX(ArchY);
    /// <summary>Centreline of the arch (x offset from centre, height above the leg base) — a catenary-like parabola.</summary>
    public static double ArchHeightAt(double dx) { double u = dx / ArchHalfSpanM; return ArchHeightM * (1 - u * u); }
    public static double ArchBaseUp(WorldTerrain t) => ArchBaseUp(t, 0);
    public static double ArchYAt(int p) => ArchY + WorldTerrain.PlateauDy(p);
    public static double ArchCentreXAt(int p) => WorldTerrain.RiverCentreX(ArchYAt(p));
    public static double ArchBaseUp(WorldTerrain t, int p)
    {
        double y = ArchYAt(p), cx = ArchCentreXAt(p);
        return System.Math.Min(t.HeightAt(cx - ArchHalfSpanM, y), t.HeightAt(cx + ArchHalfSpanM, y));
    }

    // ---- Eiffel-style tower on the valley floor, road running under it along x --------------------------
    public const double TowerX = -900.0, TowerY = -500.0, TowerHeightM = 300.0, TowerBaseHalfM = 62.0, TowerFirstFloorM = 57.0, TowerSecondFloorM = 115.0, TowerTopFloorM = 276.0;
    public const double RoadHalfLengthM = 900.0, RoadWidthM = 12.0;
    /// <summary>Half-width of the tower at height h (legs curve inward: quadratic taper to a slim top).</summary>
    public static double TowerHalfAt(double h) { double u = System.Math.Clamp(h / TowerHeightM, 0, 1); return TowerBaseHalfM * (1 - u) * (1 - u) + 4.0 * u; }
    public static double TowerYAt(int p) => TowerY + WorldTerrain.PlateauDy(p);

    // ---- The Valley's city: the combat city's grid on the old town site (ValleyCity, Playground.cs) ----

    /// <summary>Register every solid (the city's towers and sky-bridges, arch legs, tower legs) on EVERY plateau for airframe contact.</summary>
    public static void RegisterSolids(WorldTerrain t)
    {
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
        for (int p = 0; p < WorldTerrain.PlateauCount; p++) RegisterSolids(t, p);
        RegisterSeasideSolids(t);
    }

    /// <summary>The one-off features (not copied per plateau): the city (+ the Mall, rings, pad deck), the canyon lake's spires,
    /// arches and dam, the Golden Gate, the sea arch, the sea cave's roof.</summary>
    public static void RegisterSeasideSolids(WorldTerrain t)
    {
        WorldDecks.All.Clear();
        FlyCity.RegisterSolids(t);
        CanyonLake.RegisterSolids();
        GoldenGate.RegisterSolids();
        SeaArch.RegisterSolids();
        WorldSolids.Shapes.Add(new SeaCave.RoofSolid());
    }

    /// <summary>Solids of the copy on plateau <paramref name="p"/> (appends; does not clear).</summary>
    public static void RegisterSolids(WorldTerrain t, int p)
    {
        ValleyCity.At(p).RegisterSolids(t);   // the city built for flying (towers + sky-bridges), owner 2026-10-05
        // Arch legs and tower legs.
        double archBase = ArchBaseUp(t, p), archY = ArchYAt(p), archCx = ArchCentreXAt(p);
        foreach (double sgn in new[] { -1.0, 1.0 })
        {
            WorldSolids.Boxes.Add(new WorldSolids.Box(archCx + sgn * ArchHalfSpanM, archY, ArchLegWidthM / 2, ArchLegWidthM / 2, archBase - 30, archBase + 40));
        }
        double towerY = TowerYAt(p);
        double tg = t.HeightAt(TowerX, towerY);
        foreach ((double sx, double sy) in new[] { (-1.0, -1.0), (-1.0, 1.0), (1.0, -1.0), (1.0, 1.0) })
        {
            WorldSolids.Boxes.Add(new WorldSolids.Box(TowerX + sx * (TowerBaseHalfM - 10), towerY + sy * (TowerBaseHalfM - 10), 10, 10, tg, tg + TowerFirstFloorM));
        }
        WorldSolids.Boxes.Add(new WorldSolids.Box(TowerX, towerY, TowerHalfAt(TowerSecondFloorM), TowerHalfAt(TowerSecondFloorM), tg + TowerFirstFloorM, tg + TowerHeightM));
    }
}

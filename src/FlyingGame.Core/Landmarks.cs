using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Things to fly under and through: a Gateway-style arch spanning the river in its gorge, an Eiffel-style
/// tower with a road running under it, and a grid town whose buildings rise from bungalows at the edge to
/// skyscrapers downtown — with a sky bridge between twin towers and a tower with a hole through it. Every
/// building (and every leg) is a solid the airframe collides with (<see cref="WorldSolids"/>).
/// </summary>
public static class Landmarks
{
    // ---- Gateway arch over the river (legs stand on the lower gorge walls, the arch rises past the rim) ----
    public const double ArchY = -900.0, ArchHalfSpanM = 75.0, ArchHeightM = 175.0, ArchLegWidthM = 14.0, ArchTopWidthM = 5.0;
    public static double ArchCentreX => WorldTerrain.RiverCentreX(ArchY);
    /// <summary>Centreline of the arch (x offset from centre, height above the leg base) — a catenary-like parabola.</summary>
    public static double ArchHeightAt(double dx) { double u = dx / ArchHalfSpanM; return ArchHeightM * (1 - u * u); }
    public static double ArchBaseUp(WorldTerrain t) => System.Math.Min(t.HeightAt(ArchCentreX - ArchHalfSpanM, ArchY), t.HeightAt(ArchCentreX + ArchHalfSpanM, ArchY));

    // ---- Eiffel-style tower on the valley floor, road running under it along x --------------------------
    public const double TowerX = -1750.0, TowerY = 1300.0, TowerHeightM = 300.0, TowerBaseHalfM = 62.0, TowerFirstFloorM = 57.0, TowerSecondFloorM = 115.0, TowerTopFloorM = 276.0;
    public const double RoadHalfLengthM = 1750.0, RoadWidthM = 12.0;
    /// <summary>Half-width of the tower at height h (legs curve inward: quadratic taper to a slim top).</summary>
    public static double TowerHalfAt(double h) { double u = System.Math.Clamp(h / TowerHeightM, 0, 1); return TowerBaseHalfM * (1 - u) * (1 - u) + 4.0 * u; }

    // ---- Town grid --------------------------------------------------------------------------------------
    public const double TownX0 = -2650.0, TownY0 = -660.0, BlockM = 100.0, StreetM = 20.0;   // right off the south end of the pad
    public const int TownBlocksX = 15, TownBlocksY = 11;
    public static double TownCentreX => TownX0 + TownBlocksX * (BlockM + StreetM) / 2;
    public static double TownCentreY => TownY0 + TownBlocksY * (BlockM + StreetM) / 2;
    public const double SkyBridgeHeightM = 120.0, SkyBridgeDepthM = 8.0, GateHoleBottomM = 80.0, GateHoleTopM = 120.0;

    public readonly struct Building
    {
        public readonly double Cx, Cy, Hx, Hy, HeightM; public readonly int Style;
        public Building(double cx, double cy, double hx, double hy, double h, int style) { Cx = cx; Cy = cy; Hx = hx; Hy = hy; HeightM = h; Style = style; }
    }

    private static uint Hash(int i, int j) { uint h = (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ 0x9E3779B9u; h ^= h >> 13; h *= 0x85EBCA6Bu; h ^= h >> 16; return h; }
    private static double Rnd(uint h, int k) => ((h >> (k * 5)) & 31) / 31.0;

    /// <summary>Deterministic building list: heights by distance from downtown; block (7,5) is the gate tower and
    /// blocks (8,5)/(9,5) the twin towers joined by the sky bridge.</summary>
    public static List<Building> Buildings()
    {
        var list = new List<Building>();
        for (int i = 0; i < TownBlocksX; i++)
        for (int j = 0; j < TownBlocksY; j++)
        {
            double cx = TownX0 + StreetM / 2 + i * (BlockM + StreetM) + BlockM / 2;
            double cy = TownY0 + StreetM / 2 + j * (BlockM + StreetM) + BlockM / 2;
            uint h = Hash(i, j);
            double r = System.Math.Sqrt((cx - TownCentreX) * (cx - TownCentreX) + (cy - TownCentreY) * (cy - TownCentreY));
            double height = r < 300 ? 120 + 140 * Rnd(h, 0) : r < 600 ? 30 + 60 * Rnd(h, 0) : 8 + 14 * Rnd(h, 0);
            double hx = r < 300 ? 22 + 12 * Rnd(h, 1) : 28 + 14 * Rnd(h, 1), hy = r < 300 ? 22 + 12 * Rnd(h, 2) : 28 + 14 * Rnd(h, 2);
            int style = (int)(Rnd(h, 3) * 4);
            if (i == 7 && j == 5) { height = 160; hx = 34; hy = 34; style = 9; }             // gate tower (hole through it)
            if ((i == 8 || i == 9) && j == 5) { height = 200; hx = 26; hy = 26; style = 8; }  // twin towers + sky bridge
            list.Add(new Building(cx, cy, hx, hy, height, style));
        }
        return list;
    }

    /// <summary>Register every solid (buildings, arch legs, tower legs, sky bridge) for airframe contact.</summary>
    public static void RegisterSolids(WorldTerrain t)
    {
        WorldSolids.Boxes.Clear();
        double ground = t.HeightAt(TownCentreX, TownCentreY);
        foreach (Building b in Buildings())
        {
            if (b.Style == 9)
            {
                // Gate tower: solid below the hole, two side walls beside it, solid above.
                WorldSolids.Boxes.Add(new WorldSolids.Box(b.Cx, b.Cy, b.Hx, b.Hy, ground, ground + GateHoleBottomM));
                WorldSolids.Boxes.Add(new WorldSolids.Box(b.Cx, b.Cy, b.Hx, b.Hy, ground + GateHoleTopM, ground + b.HeightM));
                WorldSolids.Boxes.Add(new WorldSolids.Box(b.Cx - b.Hx + 5, b.Cy, 5, b.Hy, ground + GateHoleBottomM, ground + GateHoleTopM));
                WorldSolids.Boxes.Add(new WorldSolids.Box(b.Cx + b.Hx - 5, b.Cy, 5, b.Hy, ground + GateHoleBottomM, ground + GateHoleTopM));
                continue;
            }
            WorldSolids.Boxes.Add(new WorldSolids.Box(b.Cx, b.Cy, b.Hx, b.Hy, ground, ground + b.HeightM));
        }
        // Sky bridge between the twin towers (blocks 8 and 9, row 5).
        double bx = TownX0 + StreetM / 2 + 8 * (BlockM + StreetM) + BlockM / 2 + (BlockM + StreetM) / 2;
        double by = TownY0 + StreetM / 2 + 5 * (BlockM + StreetM) + BlockM / 2;
        WorldSolids.Boxes.Add(new WorldSolids.Box(bx, by, (BlockM + StreetM) / 2, 6, ground + SkyBridgeHeightM, ground + SkyBridgeHeightM + SkyBridgeDepthM));
        // Arch legs and tower legs.
        double archBase = ArchBaseUp(t);
        foreach (double sgn in new[] { -1.0, 1.0 })
        {
            WorldSolids.Boxes.Add(new WorldSolids.Box(ArchCentreX + sgn * ArchHalfSpanM, ArchY, ArchLegWidthM / 2, ArchLegWidthM / 2, archBase - 30, archBase + 40));
        }
        double tg = t.HeightAt(TowerX, TowerY);
        foreach ((double sx, double sy) in new[] { (-1.0, -1.0), (-1.0, 1.0), (1.0, -1.0), (1.0, 1.0) })
        {
            WorldSolids.Boxes.Add(new WorldSolids.Box(TowerX + sx * (TowerBaseHalfM - 10), TowerY + sy * (TowerBaseHalfM - 10), 10, 10, tg, tg + TowerFirstFloorM));
        }
        WorldSolids.Boxes.Add(new WorldSolids.Box(TowerX, TowerY, TowerHalfAt(TowerSecondFloorM), TowerHalfAt(TowerSecondFloorM), tg + TowerFirstFloorM, tg + TowerHeightM));
    }
}

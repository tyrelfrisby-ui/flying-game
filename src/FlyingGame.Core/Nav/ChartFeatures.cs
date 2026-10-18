namespace FlyingGame.Core.Nav;

/// <summary>What kind of thing a charted landmark is. The symbol follows from this.</summary>
public enum ChartFeatureKind { Town, Obstruction, Bridge, Arch, Dam, Waterfall, FirePot }

/// <summary>
/// One landmark worth printing. A point feature carries its height above the ground where that is the
/// interesting thing (an obstruction); a town carries its rectangle.
/// </summary>
public readonly struct ChartFeature
{
    public readonly ChartFeatureKind Kind;
    public readonly string Name;
    public readonly double X, Y;
    /// <summary>Height above the ground, metres. 0 where height is not the point.</summary>
    public readonly double AglM;
    /// <summary>Top elevation above sea level, metres — what a sectional prints in bold.</summary>
    public readonly double MslM;
    /// <summary>For a town, the rectangle it covers; otherwise zero.</summary>
    public readonly double X0, Y0, X1, Y1;

    public ChartFeature(ChartFeatureKind kind, string name, double x, double y, double aglM, double mslM,
                        double x0 = 0, double y0 = 0, double x1 = 0, double y1 = 0)
    { Kind = kind; Name = name; X = x; Y = y; AglM = aglM; MslM = mslM; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }

    public double AglFt => AglM * Terps.FtPerM;
    public double MslFt => MslM * Terps.FtPerM;
}

/// <summary>
/// The landmarks a VFR chart prints, gathered from the world itself.
///
/// This exists because dead reckoning on its own is not how anybody actually navigates: you fly a heading
/// and a time, and then you *look out of the window* and find the thing you expected to see. Pilotage needs
/// landmarks on the chart, and without them the owner's "no position on the map" would be a good deal
/// harsher than he meant it. So the towns, the bridges, the arch, the dam, the three waterfalls and the two
/// tall towers are all on the sectional, with their heights, exactly as a real one prints obstructions.
///
/// Every position here is read from the world's own constants rather than copied, so moving a landmark moves
/// its chart symbol.
/// </summary>
public static class ChartFeatures
{
    public static List<ChartFeature> All(WorldTerrain t)
    {
        var l = new List<ChartFeature>();

        // The cities: a rectangle for the built-up area and the tallest building as the obstruction, which
        // is how a sectional shows a town with something tall in it.
        void Town(string name, CityGrid g)
        {
            var (x0, y0, x1, y1) = g.Extent(90);
            var (cx, cy) = g.Centre;
            l.Add(new ChartFeature(ChartFeatureKind.Town, name, cx, cy, 0, 0, x0, y0, x1, y1));
            FlyCity.Tower tall = g.Tallest;
            double ground = t.HeightAt(tall.Cx, tall.Cy, false, false);
            l.Add(new ChartFeature(ChartFeatureKind.Obstruction, name + " tower",
                                   tall.Cx, tall.Cy, tall.HeightM, ground + tall.HeightM));
        }

        Town("DOWNTOWN", FlyCity.Grid);
        for (int p = 0; p < WorldTerrain.PlateauCount; p++)
        {
            if (WorldTerrain.Has(p, WorldTerrain.Feature.Town) && p > 0)
                Town(WorldTerrain.Airports[p].Name.ToUpperInvariant() + " TOWN", ValleyCity.At(p));
            else if (WorldTerrain.Has(p, WorldTerrain.Feature.Town))
                Town("VALLEY TOWN", ValleyCity.At(p));

            if (WorldTerrain.Has(p, WorldTerrain.Feature.Tower))
            {
                double ty = Landmarks.TowerYAt(p);
                double ground = t.HeightAt(Landmarks.TowerX, ty, false, false);
                l.Add(new ChartFeature(ChartFeatureKind.Obstruction, "TOWER", Landmarks.TowerX, ty,
                                       Landmarks.TowerHeightM, ground + Landmarks.TowerHeightM));
            }

            if (WorldTerrain.Has(p, WorldTerrain.Feature.Arch))
            {
                double ay = Landmarks.ArchYAt(p), ax = Landmarks.ArchCentreXAt(p);
                double baseUp = Landmarks.ArchBaseUp(t, p);
                l.Add(new ChartFeature(ChartFeatureKind.Arch, "ARCH", ax, ay,
                                       Landmarks.ArchHeightM, baseUp + Landmarks.ArchHeightM));
            }

            if (WorldTerrain.Has(p, WorldTerrain.Feature.Bridge))
            {
                double by = WorldTerrain.BridgeY + WorldTerrain.PlateauDy(p);
                double bx = WorldTerrain.RiverCentreX(by);
                l.Add(new ChartFeature(ChartFeatureKind.Bridge, "BRIDGE", bx, by, 0,
                                       t.BaseHeightAt(bx, by, true)));
            }
        }

        // The dam, and the three falls — the best landmarks in the world, because a 2,000 ft wall with a
        // waterfall in it is visible from a long way off and tells you exactly which step you are on.
        double damY = CanyonLake.DamY, damX = WorldTerrain.RiverCentreX(damY);
        l.Add(new ChartFeature(ChartFeatureKind.Dam, "DAM", damX, damY, 0, CanyonLake.DamCrestM));
        foreach (WorldTerrain.Waterfall w in t.Waterfalls)
            l.Add(new ChartFeature(ChartFeatureKind.Waterfall, $"FALL {w.Step + 1}", w.X, w.LipY,
                                   w.UpperM - w.LowerM, w.UpperM));

        // The lighted airway. On a sectional a beacon is a small star, and the chain of them IS the route —
        // the oldest thing on this chart and the only one you navigate by looking out of the window at night.
        foreach (FirePot p in FirePots.All(t))
            l.Add(new ChartFeature(ChartFeatureKind.FirePot, $"{p.Number}", p.X, p.Y, 0, p.GroundM));

        return l;
    }
}

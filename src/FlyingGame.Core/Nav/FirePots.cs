namespace FlyingGame.Core.Nav;

/// <summary>One lighted beacon on the mail route, with the ground it stands on measured.</summary>
public readonly struct FirePot
{
    public readonly int Number;
    public readonly double X, Y, GroundM;
    /// <summary>Which plateau it stands on, 0 at the coast.</summary>
    public readonly int Plateau;
    public FirePot(int number, double x, double y, double groundM, int plateau)
    { Number = number; X = x; Y = y; GroundM = groundM; Plateau = plateau; }
    public double GroundFt => GroundM * Terps.FtPerM;
}

/// <summary>
/// THE FIRE POTS. Owner, 2026-10-18: *"create a row of fire pots between two airports like the old mail
/// pilots followed."*
///
/// Before there was a radio to follow there was a line of fires. The transcontinental airmail route of 1923
/// was lit with flashing beacons every ten miles or so, with emergency fields between them, and a pilot flew
/// the mail at night by going from one light to the next — the first navigation system in the world that let
/// you fly when you could not see the ground. It is the rung between pilotage and dead reckoning: you are
/// still looking out of the window, but at something somebody built for you.
///
/// This route runs from the Valley field up the staircase to Summit, which is every airport in the world and
/// the whole 5.4 NM of it, along x = 400 — the same line the range's 270 course and the airway follow, and
/// for the same reason: it is where the valley goes.
///
/// **The escarpments have no pots on them**, and that is the interesting part rather than an omission. A
/// beacon has to stand on ground somebody can drive a truck to and climb a ladder on, so any site on a wall
/// steeper than <see cref="MaxSlopeDeg"/> is skipped. The route therefore arrives at the foot of each
/// 2,000 ft wall, goes dark for the climb, and picks up again on the plateau above — which is exactly the
/// decision a pilot following it has to make, and exactly what the real route did at a ridge.
/// </summary>
public static class FirePots
{
    /// <summary>The line the route runs along: the airport corridor.</summary>
    public const double RouteX = 400.0;

    /// <summary>From the Valley field to Summit.</summary>
    public const double StartY = 0.0, EndY = -10050.0;

    /// <summary>How far apart. The real route used ten miles; this world is 15 miles wide, so half a mile.</summary>
    public const double SpacingM = 900.0;

    /// <summary>A beacon needs buildable ground. Anything steeper than this is a wall, and walls get no pots.</summary>
    public const double MaxSlopeDeg = 12.0;

    /// <summary>How far either side the slope is judged.</summary>
    public const double SlopeProbeM = 120.0;

    public static List<FirePot> All(WorldTerrain t)
    {
        var l = new List<FirePot>();
        int n = 0;
        for (double y = StartY; y >= EndY - 1; y -= SpacingM)
        {
            double h = t.HeightAt(RouteX, y, false, false);
            double worst = 0;
            for (int k = 0; k < 8; k++)
            {
                double a = k * System.Math.PI / 4;
                worst = System.Math.Max(worst, System.Math.Abs(
                    t.HeightAt(RouteX + SlopeProbeM * System.Math.Cos(a),
                               y + SlopeProbeM * System.Math.Sin(a), false, false) - h));
            }
            double slopeDeg = System.Math.Atan2(worst, SlopeProbeM) * 180.0 / System.Math.PI;
            if (slopeDeg > MaxSlopeDeg) continue;                       // on a wall: no pot
            if (t.WaterSurfaceAt(RouteX, y) is double) continue;        // in the river: no pot
            // Which plateau: the nearest airport in y.
            int plateau = 0;
            double bestD = double.MaxValue;
            for (int p = 0; p < WorldTerrain.PlateauCount; p++)
            {
                double d = System.Math.Abs(WorldTerrain.Airports[p].Y - y);
                if (d < bestD) { bestD = d; plateau = p; }
            }
            l.Add(new FirePot(++n, RouteX, y, h, plateau));
        }
        return l;
    }

    /// <summary>
    /// The gaps — where the route goes dark, and how far you fly on heading alone between one light and the
    /// next. This is the number a lesson about the fire pots would be built on.
    /// </summary>
    public static List<(FirePot from, FirePot to, double gapM)> DarkStretches(WorldTerrain t, double thresholdM = SpacingM * 1.5)
    {
        var pots = All(t);
        var gaps = new List<(FirePot, FirePot, double)>();
        for (int i = 0; i + 1 < pots.Count; i++)
        {
            double gap = System.Math.Abs(pots[i + 1].Y - pots[i].Y);
            if (gap > thresholdM) gaps.Add((pots[i], pots[i + 1], gap));
        }
        return gaps;
    }
}

using System.Collections.Generic;

namespace FlyingGame.Core;

/// <summary>Where one windsock stands, in airport-local metres.</summary>
public readonly struct SockSpot
{
    public readonly double X, Y;
    /// <summary>Which strip it serves, and which of its three it is (0 = first threshold, 1 = far threshold, 2 = midfield).</summary>
    public readonly string Strip;
    public readonly int Index;
    public SockSpot(double x, double y, string strip, int index) { X = x; Y = y; Strip = strip; Index = index; }
}

/// <summary>
/// Three windsocks per runway (Ty 2026-11-08): "one should be 1000 from the end or one fifth of the runway length
/// whichever is shorter and the third should be mid field unless there is a crossing runway that would prevent it
/// without being on another ru[n]way then just near the intersection but never on a runway."
///
/// So: one in from each threshold, one at midfield, each on a side chosen at random but never — any of them — on any
/// paved or landable surface. Midfield is the one that has to think: at this world's airports the crosswind runway's
/// own midfield IS its crossing with the main, so its third sock walks along its centreline until it is clear of
/// everything and stands there, as close to the intersection as it can honestly get.
/// </summary>
public static class WindsockPlacement
{
    /// <summary>1000 ft from the end, or a fifth of the runway, whichever is shorter.</summary>
    public const double EndOffsetM = 304.8;
    /// <summary>Clear of the runway edge by this much: outside the strip, inside a glance.</summary>
    public const double SideClearanceM = 22.0;
    /// <summary>How far a blocked midfield sock may walk along its runway looking for room.</summary>
    public const double MidfieldSearchM = 400.0;

    public static double EndOffsetFor(double lengthM) => System.Math.Min(EndOffsetM, lengthM / 5.0);

    public static List<SockSpot> For(IReadOnlyList<WorldTerrain.Strip> strips, int seed)
    {
        var spots = new List<SockSpot>();
        if (strips == null) return spots;
        var rng = new System.Random(seed == 0 ? 12345 : seed);
        for (int i = 0; i < strips.Count; i++)
        {
            WorldTerrain.Strip s = strips[i];
            double hdg = s.HeadingDeg * System.Math.PI / 180.0;
            // Along the strip, and across it. Heading 0 runs along +x, so along = (cos, sin) with x and y swapped to
            // match the table's convention that a 0° strip lies on the x axis.
            double ax = System.Math.Cos(hdg), ay = System.Math.Sin(hdg);
            double nx = -ay, ny = ax;
            double half = s.Length / 2.0, off = EndOffsetFor(s.Length);
            double side = s.Width / 2.0 + SideClearanceM;

            for (int k = 0; k < 3; k++)
            {
                double along = k switch { 0 => -(half - off), 1 => half - off, _ => 0.0 };
                double sign = rng.Next(2) == 0 ? -1.0 : 1.0;          // which side: random, as he asked
                if (!TryPlace(strips, s, ax, ay, nx, ny, along, side, sign, out double x, out double y) && k == 2)
                {
                    // Midfield is blocked by a crossing runway: walk along the centreline, both ways, for the nearest
                    // honest spot. Never a compromise that puts it on a surface.
                    bool found = false;
                    for (double step = 25; step <= MidfieldSearchM && !found; step += 25)
                        foreach (double d in new[] { step, -step })
                        {
                            if (!TryPlace(strips, s, ax, ay, nx, ny, d, side, sign, out x, out y)) continue;
                            found = true; break;
                        }
                    if (!found) continue;   // nowhere clear at all: better no sock than one on a runway
                }
                else if (!TryPlace(strips, s, ax, ay, nx, ny, along, side, sign, out x, out y))
                {
                    // A threshold sock blocked on its random side: take the other side rather than lose it.
                    if (!TryPlace(strips, s, ax, ay, nx, ny, along, side, -sign, out x, out y)) continue;
                }
                spots.Add(new SockSpot(x, y, s.Kind, k));
            }
        }
        return spots;
    }

    private static bool TryPlace(IReadOnlyList<WorldTerrain.Strip> strips, in WorldTerrain.Strip own,
        double ax, double ay, double nx, double ny, double along, double side, double sign, out double x, out double y)
    {
        x = own.Dx + ax * along + nx * side * sign;
        y = own.Dy + ay * along + ny * side * sign;
        return !OnAnyStrip(strips, x, y);
    }

    /// <summary>Is this point on a landable surface? Every strip, not just the one being served.</summary>
    public static bool OnAnyStrip(IReadOnlyList<WorldTerrain.Strip> strips, double x, double y, double marginM = 6.0)
    {
        for (int i = 0; i < strips.Count; i++)
        {
            WorldTerrain.Strip s = strips[i];
            double hdg = s.HeadingDeg * System.Math.PI / 180.0;
            double ax = System.Math.Cos(hdg), ay = System.Math.Sin(hdg);
            double dx = x - s.Dx, dy = y - s.Dy;
            double along = dx * ax + dy * ay, across = -dx * ay + dy * ax;
            if (System.Math.Abs(along) <= s.Length / 2.0 + marginM && System.Math.Abs(across) <= s.Width / 2.0 + marginM) return true;
        }
        return false;
    }
}

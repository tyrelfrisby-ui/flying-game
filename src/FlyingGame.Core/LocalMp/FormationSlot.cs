namespace FlyingGame.Core.LocalMp;

/// <summary>
/// LOCAL 2-PLAYER: where the second aeroplane starts relative to the first (Ty 2026-11-05: "mostly combat and formation
/// flying"). Offsets are in the start's HEADING frame, metres: +forward along the heading, +right to its right.
///
/// In the air: abreast on the right, the same heading, height and speed, with <see cref="WingtipGapM"/> between the
/// wingtips (centre to centre = span + gap). On a runway or on the water: in trail on the same centreline,
/// <see cref="TrailGapM"/> plus a length behind — a runway is 20–45 m wide, so abreast would put one of them on the grass,
/// and a lake's edge is closer than a span in places.
/// </summary>
public static class FormationSlot
{
    public const double WingtipGapM = 30.0;
    public const double TrailGapM = 60.0;

    public readonly struct Offset
    {
        public readonly double ForwardM, RightM;
        public Offset(double f, double r) { ForwardM = f; RightM = r; }
    }

    public static Offset For(int playerIndex, double spanM, double lengthM, bool onSurface)
    {
        if (playerIndex <= 0) return new Offset(0, 0);
        spanM = Math.Max(1.0, spanM); lengthM = Math.Max(1.0, lengthM);
        return onSurface ? new Offset(-(TrailGapM + lengthM) * playerIndex, 0)
                         : new Offset(0, (spanM + WingtipGapM) * playerIndex);
    }

    /// <summary>The offset in the sim's world frame (x north, y east) for a start heading <paramref name="headingRad"/>
    /// (0 = north, + clockwise).</summary>
    public static (double Dx, double Dy) World(Offset o, double headingRad)
    {
        double c = Math.Cos(headingRad), s = Math.Sin(headingRad);
        return (o.ForwardM * c - o.RightM * s, o.ForwardM * s + o.RightM * c);
    }
}

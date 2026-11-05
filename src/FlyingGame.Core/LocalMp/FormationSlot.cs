namespace FlyingGame.Core.LocalMp;

/// <summary>
/// LOCAL 2-PLAYER: where the second aeroplane starts relative to the first (Ty 2026-11-05: "mostly combat and formation
/// flying"). Offsets are in the start's HEADING frame, metres: +forward along the heading, +right to its right.
///
/// In the air: abreast on the right, the same heading, height and speed, with <see cref="WingtipGapM"/> between the
/// wingtips (centre to centre = span + gap). On a runway or on the water: in line on the same centreline,
/// <see cref="TrailGapM"/> plus a length AHEAD — a runway is 20–45 m wide, so abreast would put one of them on the grass,
/// and a lake's edge is closer than a span in places; and the first player starts at the threshold, so behind him is
/// off the end of the runway. Every start the game makes on a surface faces down a runway or across open water.
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
        return onSurface ? new Offset((TrailGapM + lengthM) * playerIndex, 0)
                         : new Offset(0, (spanM + WingtipGapM) * playerIndex);
    }

    /// <summary>Span and length of an airframe from its strips (span: the outermost strip edge, both sides; length: the
    /// strips' chord extent fore and aft, the way the chase camera is fitted). Floats extend the length.</summary>
    public static (double SpanM, double LengthM) SizeOf(DataContracts.AircraftConfig cfg)
    {
        double ymax = 0, xmin = double.MaxValue, xmax = double.MinValue;
        foreach (var sf in cfg.Surfaces)
            foreach (var st in sf.Strips)
            {
                double halfWidth = st.Chord > 1e-6 ? 0.5 * st.Area / st.Chord : 0.0;   // strip width = area / chord
                ymax = Math.Max(ymax, Math.Abs(st.Pos[1]) + halfWidth);
                xmin = Math.Min(xmin, st.Pos[0] - st.Chord * 0.75); xmax = Math.Max(xmax, st.Pos[0] + st.Chord * 0.25);
            }
        if (cfg.Floats != null) { xmax = Math.Max(xmax, cfg.Floats.BowX); xmin = Math.Min(xmin, cfg.Floats.BowX - cfg.Floats.LengthM); }
        double span = 2 * ymax;
        return (span, xmax > xmin ? xmax - xmin : span * 0.8);
    }

    /// <summary>The offset in the sim's world frame (x north, y east) for a start heading <paramref name="headingRad"/>
    /// (0 = north, + clockwise).</summary>
    public static (double Dx, double Dy) World(Offset o, double headingRad)
    {
        double c = Math.Cos(headingRad), s = Math.Sin(headingRad);
        return (o.ForwardM * c - o.RightM * s, o.ForwardM * s + o.RightM * c);
    }
}

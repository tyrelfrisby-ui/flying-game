namespace FlyingGame.Core.LocalMp;

/// <summary>
/// LOCAL 2-PLAYER: where each player's view sits on one screen. Returned as a normalised viewport rect
/// (x, y, w, h in 0..1, origin BOTTOM-left: Unity's <c>Camera.rect</c>).
///
/// Landscape (every phone and iPad held for two pads): side by side, P1 on the LEFT, P2 on the right, each the full
/// height — the two players sit side by side on the couch, so each looks at the half on their side, and an aeroplane's
/// view is wider than tall anyway. Portrait: P1 on TOP, P2 below. One player: the whole screen
/// (the single-player rect is whatever the caller already had).
///
/// A thin divider (<see cref="GapFraction"/> of the split edge) is left between the halves so the two pictures do not
/// read as one.
/// </summary>
public static class SplitLayout
{
    /// <summary>The gap between the two views, as a fraction of the split dimension (each side gives up half).</summary>
    public const double GapFraction = 0.004;

    public readonly struct Rect01
    {
        public readonly double X, Y, W, H;
        public Rect01(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
        public override string ToString() => $"({X:F3}, {Y:F3}, {W:F3}, {H:F3})";
    }

    /// <summary>True when the screen is split left/right (wider than tall), false when top/bottom.</summary>
    public static bool SideBySide(double screenW, double screenH) => screenW >= screenH;

    /// <summary>The viewport of player <paramref name="index"/> (0 = P1) when <paramref name="players"/> share a
    /// <paramref name="screenW"/> × <paramref name="screenH"/> screen.</summary>
    public static Rect01 Viewport(int index, int players, double screenW, double screenH)
    {
        if (players <= 1) return new Rect01(0, 0, 1, 1);
        int i = Math.Clamp(index, 0, 1);
        double half = 0.5 - GapFraction * 0.5;
        if (SideBySide(screenW, screenH))
            return i == 0 ? new Rect01(0, 0, half, 1) : new Rect01(1 - half, 0, half, 1);
        return i == 0 ? new Rect01(0, 1 - half, 1, half) : new Rect01(0, 0, 1, half);
    }
}

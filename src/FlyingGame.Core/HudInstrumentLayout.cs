using System;
namespace FlyingGame.Core;

/// <summary>Exterior HUD strip geometry, native pixels. Never scales dial text down to fit.</summary>
public static class HudInstrumentLayout
{
    public static double DialRadius(double shortEdge) => shortEdge * 0.087;
    public static double RowWidth(double shortEdge, double available) => Math.Min(available, shortEdge * 0.84);
    public static double EngineTop(double toolbarBottom, double gap, double shortEdge) =>
        toolbarBottom + gap + 2 * DialRadius(shortEdge) + Math.Round(shortEdge * 0.040) * 1.9;
}

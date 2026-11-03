using System;
namespace FlyingGame.Core;

/// <summary>Native-pixel exterior gauge geometry. One value/unit line sits below each face.</summary>
public static class HudInstrumentLayout
{
    public static double ToolbarGap(double s) => s * 12 / 1206;
    public static double DialRadius(double s, bool gamepad = false) => s * (gamepad ? 92.5 : 75) / 1206;
    public static double RowWidth(double s, double available, bool gamepad = false) => Math.Min(available, s * (gamepad ? 820.0 / 1206 : 700.0 / 1206));
    public static double ValueHeight(double s) => Math.Max(22, s * 33 / 1206) * 1.05;
    public static double EngineRadius(double s, bool gamepad = false) => s * (gamepad ? 67.5 : 55) / 1206;
    public static double NumeralText(double s, bool gamepad = false) => s * (gamepad ? 24 : 20) / 1206;
    public static double RowGap(double s) => s * 6 / 1206;
    public static double RowHeight(double s, bool gamepad = false) => 2 * DialRadius(s, gamepad) + ValueHeight(s);
    public static double EngineTop(double toolbarBottom, double gap, double s, bool gamepad = false) => toolbarBottom + ToolbarGap(s) + RowHeight(s, gamepad) + RowGap(s);
    public static double Bottom(double toolbarBottom, double gap, double s, bool gamepad, bool engine) =>
        toolbarBottom + ToolbarGap(s) + RowHeight(s, gamepad) + (engine ? RowGap(s) + 2 * EngineRadius(s, gamepad) + ValueHeight(s) : 0);
    public static double FuelGallons(double kg) => kg / (0.72 * 3.785);
    /// <summary>Height of the engine row: dial face plus its one value/unit line.</summary>
    public static double EngineBlockHeight(double s, bool gamepad = false, int valueLines = 1) => 2 * EngineRadius(s, gamepad) + valueLines * ValueHeight(s);
    /// <summary>Lines the engine values need: the widest ("1822 RPM", "29.3 GAL") is 8 bold glyphs at ~0.6 em each, so
    /// a cell narrower than 4.8 em puts the unit on a second line rather than shrinking text below MinText or
    /// letting neighbours overlap (the narrow touch band between the iPad's pads).</summary>
    public static int EngineValueLines(double cellWidth, double minText) => cellWidth >= minText * 4.8 ? 1 : 2;
    /// <summary>Left edge of a row of <paramref name="width"/> centred on <paramref name="centreX"/>, pushed inside [minX, maxX]
    /// (owner 2026-11-02: the engine row sits centred below the airplane but never on a button column or a pad).</summary>
    public static double CentredRowLeft(double centreX, double width, double minX, double maxX) =>
        Math.Max(minX, Math.Min(maxX - width, centreX - width / 2));
}

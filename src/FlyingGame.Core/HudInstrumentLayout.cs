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
}

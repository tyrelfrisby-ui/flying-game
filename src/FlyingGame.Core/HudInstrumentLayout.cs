using System;
namespace FlyingGame.Core;

/// <summary>Native-pixel exterior gauge geometry. All text lives outside the faces.</summary>
public static class HudInstrumentLayout
{
    public static double ToolbarGap(double s) => s * 12 / 1206;
    public static double DialRadius(double s, bool gamepad = false) => s * (gamepad ? 60 : 54) / 1206;
    public static double RowWidth(double s, double available, bool gamepad = false) => Math.Min(available, s * (gamepad ? 1.65 : 0.84));
    public static double ValueHeight(double s) => Math.Max(22, s * 33 / 1206) * 1.2;
    public static double CaptionHeight(double s, bool gamepad = false) => Math.Max(20, s * 30 / 1206) * (gamepad ? 1.2 : 2.4);
    // Captions have two lines, preserving the full unit label even in the touch band.
    public static double RowHeight(double s, bool gamepad = false) => 2 * DialRadius(s, gamepad) + ValueHeight(s) + CaptionHeight(s, gamepad);
    public static double EngineTop(double toolbarBottom, double gap, double s, bool gamepad = false) => toolbarBottom + ToolbarGap(s) + RowHeight(s, gamepad) + gap;
    public static double Bottom(double toolbarBottom, double gap, double s, bool gamepad, bool engine) =>
        toolbarBottom + ToolbarGap(s) + RowHeight(s, gamepad) + (engine ? gap + 1.4 * DialRadius(s, gamepad) + ValueHeight(s) + CaptionHeight(s, gamepad) : 0);
    public static double FuelGallons(double kg) => kg / (0.72 * 3.785);
}

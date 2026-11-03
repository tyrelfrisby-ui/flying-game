namespace FlyingGame.Core;

/// <summary>
/// The outside (chase) camera's framing numbers, pure so they can be tested without Unity.
///
///  • FOLLOW DISTANCE: sized by the larger of span and 1.15 × length, × 1.3, kept between 11 m and 200 m (owner
///    2026-09-10 / 2026-10-07), then × <see cref="ZoomOut"/> — Ty 2026-11-02 (iPhone): "zoom out a bit on all
///    airplanes, they are cropped in a bit too tight. Use 50% more distance." Every aircraft gets exactly 1.5× the
///    distance it had, the 11 m floor and 200 m cap included; the camera's height stays 0.22 × the distance, so the
///    angle it looks down at the aircraft — and so where the aircraft sits on the screen — does not change.
///  • VIEWPORT: in portrait the bottom control tray takes its height off the 3D view. With a gamepad connected the
///    touch sticks are hidden (2026-11-02) and the view gets the whole screen (Ty: "make the whole screen show the
///    outside, not just a giant black area where the track pads were, and move the airplane to the center").
/// </summary>
public static class ChaseFraming
{
    /// <summary>Ty 2026-11-02: 50 % more distance on every aircraft.</summary>
    public const double ZoomOut = 1.5;

    /// <summary>The distance before <see cref="ZoomOut"/> (what FitTo set until 2026-11-02).</summary>
    public static double BaseDistanceM(double spanM, double lengthM)
    {
        double size = System.Math.Max(spanM, lengthM * 1.15);
        return System.Math.Min(200.0, System.Math.Max(11.0, size * 1.3));
    }

    /// <summary>The chase camera's distance behind the aircraft (m).</summary>
    public static double FollowDistanceM(double spanM, double lengthM) => ZoomOut * BaseDistanceM(spanM, lengthM);

    /// <summary>The chase camera's height above the flight path line (m): the same fraction of the distance as before.</summary>
    public static double FollowHeightM(double followDistanceM) => followDistanceM * 0.22;

    /// <summary>
    /// The camera's normalized viewport (x, y, width, height; bottom-left origin). <paramref name="trayPx"/> is the
    /// portrait tray's height (0 in landscape); <paramref name="fullScreen"/> = a gamepad is connected and the touch
    /// sticks are hidden: the whole screen, so the aircraft (aimed at the viewport's centre) is in the screen's centre.
    /// </summary>
    public static (double X, double Y, double W, double H) CameraViewport(double trayPx, double screenHeightPx, bool fullScreen)
    {
        if (fullScreen || screenHeightPx <= 0 || trayPx <= 0) return (0, 0, 1, 1);
        double f = System.Math.Min(1.0, trayPx / screenHeightPx);
        return (0, f, 1, 1 - f);
    }
}

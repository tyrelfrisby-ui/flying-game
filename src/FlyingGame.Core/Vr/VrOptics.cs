using System;

namespace FlyingGame.Core.Vr;

/// <summary>
/// The lens geometry: barrel distortion and back, where each lens sits on the screen, what field of view the
/// screen actually subtends through it, and how big the per-eye render target should be.
///
/// Engine-free on purpose. The distortion is the half Codex's shader inverts, and every number the Unity rig
/// feeds a camera comes out of here, so both can be tested without a phone.
/// </summary>
public static class VrOptics
{
    /// <summary>Which way a frustum edge points, in the eye's own frame.</summary>
    public enum Edge { Left, Right, Bottom, Top }

    /// <summary>Barrel distortion the LENS applies: a point r from the lens axis on the screen appears at
    /// r' = r(1 + k1·r² + k2·r⁴). r is in TANGENT units (distance on the screen ÷ screen-to-lens), which is the
    /// convention Cardboard's own profile coefficients are quoted in.</summary>
    public static double Distort(double r, double k1, double k2)
    {
        double r2 = r * r;
        return r * (1.0 + k1 * r2 + k2 * r2 * r2);
    }

    /// <summary>The inverse: where to SAMPLE so the lens puts it where it belongs. There is no closed form, so
    /// this is Newton on f(r) = Distort(r) − target, from a decent first guess. Converges in a handful of
    /// iterations over the range a viewer can actually see; the loop is bounded so a pathological coefficient
    /// cannot hang a frame.</summary>
    public static double Undistort(double distorted, double k1, double k2, double tolerance = 1e-9)
    {
        if (!double.IsFinite(distorted) || Math.Abs(distorted) < 1e-12) return 0.0;
        double sign = Math.Sign(distorted), target = Math.Abs(distorted);
        double r = target;                                  // the lens magnifies, so the pre-image is smaller
        for (int i = 0; i < 24; i++)
        {
            double r2 = r * r;
            double f = r * (1.0 + k1 * r2 + k2 * r2 * r2) - target;
            if (Math.Abs(f) < tolerance) break;
            double df = 1.0 + 3.0 * k1 * r2 + 5.0 * k2 * r2 * r2;
            if (Math.Abs(df) < 1e-12) break;
            double next = r - f / df;
            if (!double.IsFinite(next)) break;
            r = next < 0.0 ? r * 0.5 : next;                // never cross the axis
        }
        return sign * r;
    }

    /// <summary>
    /// How far each frustum edge is from the lens axis, in tangent units, for ONE eye — the thing that becomes
    /// an asymmetric projection.
    ///
    /// Two limits, and the smaller wins for each edge:
    ///  • the LENS can only show so much (the profile's 60° each way);
    ///  • the SCREEN only reaches so far, and the lens stretches that reach, so the screen's own edge is at
    ///    Undistort(distance from the lens axis ÷ screen-to-lens).
    ///
    /// <paramref name="halfScreenWidthM"/> is half the whole screen in landscape (one eye gets half of that).
    /// </summary>
    public static double EdgeTangent(VrLensConfig lens, Edge edge, double screenWidthM, double screenHeightM)
    {
        double d = lens.ScreenToLensDistanceM;
        // The two lens axes straddle the screen centre, inter-lens apart, so each sits this far inboard of its
        // own half's outer edge.
        double halfScreen = screenWidthM * 0.5;
        double outward = halfScreen - lens.InterLensDistanceM * 0.5;   // lens axis to the OUTER screen edge
        double inward = lens.InterLensDistanceM * 0.5;                  // lens axis to the screen CENTRE line
        double below = VerticalOffsetM(lens, screenHeightM);            // lens axis to the BOTTOM screen edge
        double above = screenHeightM - below;

        (double reachM, double lensDeg) = edge switch
        {
            Edge.Left => (outward, lens.FovLeftDeg),      // the left eye's outboard side is the screen's left
            Edge.Right => (inward, lens.FovRightDeg),
            Edge.Bottom => (below, lens.FovBottomDeg),
            _ => (above, lens.FovTopDeg),
        };
        double screenLimited = Undistort(Math.Max(0.0, reachM) / d, lens.K1, lens.K2);
        double lensLimited = Math.Tan(lensDeg * Math.PI / 180.0);
        return Math.Min(screenLimited, lensLimited);
    }

    /// <summary>Height of the lens axis above the BOTTOM edge of the screen, metres. With the v2 viewer's BOTTOM
    /// alignment the lens sits a fixed distance above the tray, and the screen's bottom edge is taken to be at
    /// the tray — the profile carries no bezel figure, so that simplification is stated rather than hidden.</summary>
    public static double VerticalOffsetM(VrLensConfig lens, double screenHeightM) =>
        (lens.VerticalAlignment ?? "BOTTOM").ToUpperInvariant() switch
        {
            "TOP" => screenHeightM - lens.TrayToLensDistanceM,
            "CENTER" or "CENTRE" => screenHeightM * 0.5,
            _ => Math.Min(lens.TrayToLensDistanceM, screenHeightM),
        };

    /// <summary>The eye's field of view as four half-angles in degrees (left, right, bottom, top), ready for an
    /// asymmetric projection. The RIGHT eye is this mirrored left-for-right.</summary>
    public static (double Left, double Right, double Bottom, double Top) EyeFovDeg(
        VrLensConfig lens, double screenWidthM, double screenHeightM)
    {
        double ToDeg(Edge e) => Math.Atan(EdgeTangent(lens, e, screenWidthM, screenHeightM)) * 180.0 / Math.PI;
        return (ToDeg(Edge.Left), ToDeg(Edge.Right), ToDeg(Edge.Bottom), ToDeg(Edge.Top));
    }

    /// <summary>Pixels for one eye's render target: half the screen across, full height, times the render
    /// scale, never zero and never bigger than the screen half itself.</summary>
    public static (int Width, int Height) EyeTargetPixels(VrLensConfig lens, int screenWidthPx, int screenHeightPx)
    {
        double s = Math.Clamp(lens.RenderScale, 0.25, 1.0);
        // AwayFromZero, not the default banker's rounding: a half-pixel eye target is arbitrary either way, but
        // 566 out of 2266 x 0.5 x 0.5 = 566.5 reads as a bug to anyone checking the arithmetic by hand.
        int w = (int)Math.Round(screenWidthPx * 0.5 * s, MidpointRounding.AwayFromZero);
        int h = (int)Math.Round(screenHeightPx * s, MidpointRounding.AwayFromZero);
        return (Math.Max(16, w), Math.Max(16, h));
    }

    /// <summary>Where one eye's image goes on the screen, in pixels: left eye the left half, right the right.</summary>
    public static (int X, int Y, int Width, int Height) EyeViewportPixels(bool rightEye, int screenWidthPx, int screenHeightPx)
    {
        int half = screenWidthPx / 2;
        return (rightEye ? half : 0, 0, half, screenHeightPx);
    }
}

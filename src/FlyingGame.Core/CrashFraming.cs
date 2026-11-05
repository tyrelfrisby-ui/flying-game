using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// The chase camera's crash view: when it opens, and where it frames the wreck. Pure, so a seeded crash can test it
/// without Unity; ChaseCamera smooths towards what this returns.
///
/// Ty 2026-11-05 06:01 (iPhone): "I crashed in flight path view on the phone and the shot went straight up into the
/// sky?" The crash view leaves the flight path and sits 30° above the wreck at the range that fits it, and that range
/// only grew. Two things fed it that are not the wreck:
///  • the smoothed focus's LAG behind the moving wreck (tens of metres while it slides) was latched into the range for
///    good, so a 6 m Pitts ended 100-240 m away and 50-120 m up;
///  • the aircraft's "radius" was every enabled renderer under it, and on water that includes the fuel sheen
///    (FuelSlickVisual, scaled to the slick: 0.5 m/s of spreading, ~150 m in five minutes) and the crash spray, so the
///    camera climbed for as long as the slick spread.
/// The lag now eases the range out and back (<see cref="RangeM"/> with it, latched without it), a part is never
/// bigger than its aeroplane (<see cref="ClampRadiusM"/>), and the range never exceeds <see cref="MaxRangeChase"/>
/// chase distances: a cartwheel strews parts 40 m and more, and fitting every one into a portrait phone's 15° half-width
/// still parked the camera 90 m up. Past that the far parts leave the frame and the heavy wreck stays in the middle. The view also opens for a piece arriving on LAND now — the
/// flight path of a cartwheeling wreck is no direction to look along (a crash is something breaking: parts break
/// only by load, so the airframe merely touching something still opens nothing).
/// </summary>
public static class CrashFraming
{
    /// <summary>Does this impact switch the chase camera to the crash view? A broken-off piece (PieceId ≥ 0) arriving on
    /// land or water. The intact airframe's impacts (PieceId −1: a crunch, a skim, a bent panel) do not.</summary>
    public static bool OpensCrashView(in CrashImpact e) => e.PieceId >= 0;

    /// <summary>The camera looks down at the wreck from this far above the horizon: it exposes the field the pieces lie
    /// in and keeps the camera off the ground.</summary>
    public const double ElevationDeg = 30.0;

    /// <summary>Ty's robust-cluster rule: a point further from the mass centre than this many median distances is a
    /// far-flung piece, not part of the wreck to frame.</summary>
    public const double ClusterCutoffMedians = 2.5;

    /// <summary>Breathing room round the fitted wreck.</summary>
    public const double FitMargin = 1.15;

    /// <summary>The wreck to frame: the mass-weighted centre of the robust cluster and the radius that holds every
    /// clustered point with its own radius. <paramref name="distances"/> is scratch (cleared here).</summary>
    public static (Vec3 Focus, double RadiusM) Cluster(IReadOnlyList<Vec3> points, IReadOnlyList<double> massesKg,
        IReadOnlyList<double> radiiM, List<double> distances)
    {
        distances.Clear();
        if (points.Count == 0) return (Vec3.Zero, 0);
        Vec3 centre = Vec3.Zero; double mass = 0;
        for (int i = 0; i < points.Count; i++) { double m = System.Math.Max(.01, massesKg[i]); centre += points[i] * m; mass += m; }
        centre /= mass;
        for (int i = 0; i < points.Count; i++) distances.Add((centre - points[i]).Length);
        distances.Sort();
        int middle = distances.Count / 2;
        double median = distances.Count % 2 == 0 ? (distances[middle - 1] + distances[middle]) * .5 : distances[middle];
        double cutoff = ClusterCutoffMedians * median;
        Vec3 focus = Vec3.Zero; mass = 0;
        for (int i = 0; i < points.Count; i++) if ((centre - points[i]).Length <= cutoff)
        { double m = System.Math.Max(.01, massesKg[i]); focus += points[i] * m; mass += m; }
        focus = mass > 0 ? focus / mass : centre;
        double radius = 0;
        for (int i = 0; i < points.Count; i++) if ((centre - points[i]).Length <= cutoff)
            radius = System.Math.Max(radius, (focus - points[i]).Length + radiiM[i]);
        return (focus, radius);
    }

    /// <summary>A drawn part of an aeroplane, measured round its centre, is never more than this many times the larger
    /// of the aeroplane's span and length: whatever measures bigger is not airframe (a fuel sheen, spray, a trail).</summary>
    public const double MaxPartRadiusOfSize = 0.75;

    /// <summary>The radius to frame for a part that measured <paramref name="measuredM"/> (renderer bounds about its centre).</summary>
    public static double ClampRadiusM(double measuredM, double spanM, double lengthM)
        => System.Math.Min(measuredM, MaxPartRadiusOfSize * System.Math.Max(spanM, lengthM));

    /// <summary>The crash view is never further than this many chase distances (49.5 m on the Pitts' 16.5 m).</summary>
    public const double MaxRangeChase = 3.0;

    /// <summary>The range at which a sphere of <paramref name="radiusM"/> (plus the smoothed focus's lag behind the true
    /// one) fits a view of half-angle <paramref name="halfFovRad"/> (the narrower of the two axes), and never closer than
    /// 1.5 chase distances (an intact ditching opens into an overview too) nor further than <see cref="MaxRangeChase"/>. Keep the largest of these with
    /// <paramref name="focusLagM"/> = 0 (no pumping in when a piece sinks), and add the lag only to this frame's want: it
    /// is the smoothing catching up, not the wreck.</summary>
    public static double RangeM(double radiusM, double focusLagM, double halfFovRad, double chaseDistanceM)
        => System.Math.Clamp((radiusM + focusLagM) * FitMargin / System.Math.Sin(halfFovRad), chaseDistanceM * 1.5, chaseDistanceM * MaxRangeChase);

    /// <summary>Where the camera sits relative to the focus: <paramref name="rangeM"/> out along the horizontal
    /// <paramref name="bearing"/> and raised <see cref="ElevationDeg"/> towards <paramref name="up"/>.</summary>
    public static Vec3 CameraOffset(Vec3 bearing, Vec3 up, double rangeM)
    {
        double e = ElevationDeg * System.Math.PI / 180;
        return (bearing * System.Math.Cos(e) + up * System.Math.Sin(e)) * rangeM;
    }
}

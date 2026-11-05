using System;

namespace FlyingGame.Core.Vr;

/// <summary>Destination (eye-local, bottom-up) to source UV, in the rig's tangent convention.</summary>
public static class VrDistortion
{
    public static (double U, double V) Sample(VrLensConfig lens, bool rightEye,
        double widthM, double heightM, double u, double v, double colourScale = 1)
    {
        double half = widthM * 0.5;
        double centre = rightEye ? lens.InterLensDistanceM * 0.5 : half - lens.InterLensDistanceM * 0.5;
        double x = (u * half - centre) / lens.ScreenToLensDistanceM;
        double y = (v * heightM - VrOptics.VerticalOffsetM(lens, heightM)) / lens.ScreenToLensDistanceM;
        double radius = Math.Sqrt(x * x + y * y);
        double scale = radius < 1e-12 ? 1 : VrOptics.Distort(radius, lens.K1, lens.K2) / radius;
        x *= scale * colourScale; y *= scale * colourScale;
        double l = VrOptics.EdgeTangent(lens, VrOptics.Edge.Left, widthM, heightM);
        double r = VrOptics.EdgeTangent(lens, VrOptics.Edge.Right, widthM, heightM);
        double b = VrOptics.EdgeTangent(lens, VrOptics.Edge.Bottom, widthM, heightM);
        double t = VrOptics.EdgeTangent(lens, VrOptics.Edge.Top, widthM, heightM);
        if (rightEye) (l, r) = (r, l);
        return ((x + l) / (l + r), (y + b) / (b + t));
    }

    /// <summary>HUD vocabulary: off and minimal. Unknown values fail closed (no HUD).</summary>
    public static bool MinimalHud(string mode) => string.Equals(mode, "minimal", StringComparison.OrdinalIgnoreCase);
}

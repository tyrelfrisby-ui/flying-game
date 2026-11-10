using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr;

/// <summary>
/// Where the two eyes are. Engine-free so the arithmetic is tested without a scene.
///
/// The two cameras straddle the ACTIVE SEAT'S authored eye point by half the pilot's IPD, along the HEAD's own
/// right axis — so when he tilts his head the baseline tilts with it, which is what keeps the stereo fused.
/// They are NOT the front and rear seat eyes: those are two different pilots' heads, a metre apart, and using
/// them as a stereo pair would be a wall-eyed view from nowhere.
///
/// Everything is in the aeroplane's own local frame, the same frame <c>CockpitView.EyeLocal</c> returns, so the
/// Unity side only has to transform the result by the airframe.
/// </summary>
public static class VrStereoRig
{
    /// <summary>Half the separation, along the head's right. <paramref name="headRight"/> is the head's right
    /// axis in the aeroplane's local frame — tilted by roll, which is the point.</summary>
    public static (Vec3 Left, Vec3 Right) EyePositions(Vec3 seatEye, Vec3 headRight, double ipdM)
    {
        double len = headRight.Length;
        Vec3 r = len > 1e-9 ? headRight / len : new Vec3(0.0, 1.0, 0.0);
        Vec3 half = r * (System.Math.Max(0.0, ipdM) * 0.5);
        return (seatEye - half, seatEye + half);
    }

    /// <summary>What the two eyes are actually separated by — the test that says the IPD is the IPD.</summary>
    public static double Separation(Vec3 left, Vec3 right) => (right - left).Length;

    /// <summary>The midpoint, which must stay exactly on the seat's eye however the head is turned.</summary>
    public static Vec3 Midpoint(Vec3 left, Vec3 right) => (left + right) * 0.5;
}

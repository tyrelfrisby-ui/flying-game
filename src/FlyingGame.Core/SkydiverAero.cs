namespace FlyingGame.Core;

/// <summary>A human body as a wing, which is what a skydiver's is.
///
/// A falling body is not one drag area but a whole range of them, chosen by what the pilot does with his
/// arms and legs, and the spread is enormous: flat and stable he falls at about 120 mph, and standing on
/// his head he falls at about 210 mph. Everything in between is a trade of drag for speed, and leaning
/// into it converts some of that fall into FORWARD travel -- tracking -- at a glide ratio of roughly 0.7
/// for a body with no suit to help it.
///
/// The drag areas here are not quoted from anywhere, because the sources quote terminal VELOCITIES and
/// disagree wildly about area and Cd. So they are back-solved from the speeds, which are the agreed part:
///
///     CdA = 2 m g / (rho V^2)
///
/// at 105 kg (a 90 kg pilot and 15 kg of kit) in sea-level air. That makes the speeds exact by
/// construction and the areas a consequence -- the honest direction for this derivation, and the same way
/// the Great Lakes' CLmax and the two soft wings were done. <see cref="SkydiverAeroTests"/> checks the
/// round trip.
///
/// Body position runs 0 (flat and stable, belly to earth) through 0.5 (the steepest useful track) to
/// 1 (head down). It is one number because a pilot only has one axis of real choice here: how much he
/// trades drag for speed.</summary>
public static class SkydiverAero
{
    /// <summary>Mass the drag areas were solved at: a 90 kg pilot and 15 kg of rig and kit.</summary>
    public const double ReferenceMassKg = 105.0;
    public const double ReferenceDensity = 1.225;

    /// <summary>120 mph: the figure every source agrees on for a stable belly-to-earth fall.</summary>
    public const double BellyTerminalMs = 53.6;
    /// <summary>About 210 mph, the middle of the quoted range for a head-down freefly position.</summary>
    public const double HeadDownTerminalMs = 93.9;
    /// <summary>A steep track falls slower than head down but much faster than flat.</summary>
    public const double TrackTerminalMs = 61.0;

    /// <summary>Best glide a tracking body reaches with no wingsuit: about 0.7 forward for 1 down.</summary>
    public const double TrackBestGlide = 0.70;

    private static double AreaFor(double terminalMs) =>
        2.0 * ReferenceMassKg * 9.80665 / (ReferenceDensity * terminalMs * terminalMs);

    public static readonly double CdABelly = AreaFor(BellyTerminalMs);
    public static readonly double CdATrack = AreaFor(TrackTerminalMs);
    public static readonly double CdAHeadDown = AreaFor(HeadDownTerminalMs);

    /// <summary>Drag area at a body position, interpolated through the steep-track point so that all three
    /// published speeds are hit rather than two of them and a straight line between.</summary>
    public static double CdA(double bodyPos)
    {
        double p = Math.Clamp(bodyPos, 0.0, 1.0);
        return p <= 0.5
            ? CdABelly + (CdATrack - CdABelly) * (p / 0.5)
            : CdATrack + (CdAHeadDown - CdATrack) * ((p - 0.5) / 0.5);
    }

    /// <summary>How much of the fall this body position turns into forward travel. Flat and stable, almost
    /// none -- a belly-to-earth skydiver goes very nearly straight down. Steepest track, the full 0.7. Head
    /// down, almost none again: he is fast, not going anywhere.</summary>
    public static double GlideRatio(double bodyPos)
    {
        double p = Math.Clamp(bodyPos, 0.0, 1.0);
        // A smooth hump peaking at the track position, so there is a best body angle to hold and a cost to
        // being either side of it -- which is what makes tracking a skill.
        double x = (p - 0.5) / 0.5;
        return TrackBestGlide * Math.Max(0.0, 1.0 - x * x) + 0.04;
    }

    /// <summary>Terminal speed at this body position, mass and air density.</summary>
    public static double TerminalSpeed(double bodyPos, double massKg, double density) =>
        Math.Sqrt(2.0 * massKg * 9.80665 / (Math.Max(1e-6, density) * CdA(bodyPos)));
}

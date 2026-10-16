namespace FlyingGame.Core.DataContracts;

/// <summary>
/// The whole-aircraft parachute's own data (owner 2026-10-16). See <see cref="FlyingGame.Core.BallisticRecovery"/>
/// for where these numbers come from and how they check against each other.
/// </summary>
public sealed class BrsConfig
{
    /// <summary>Inflated PROJECTED area, m². The Cirrus canopy is 2,400 ft².</summary>
    public double CanopyProjectedAreaM2 { get; set; } = 223.0;
    /// <summary>Drag coefficient referred to that PROJECTED area — 1.3–1.5 for a round canopy. (Referred to
    /// the flat nominal area it would be ~0.75; the two are not interchangeable.)</summary>
    public double Cd { get; set; } = 1.4;
    /// <summary>Vpd, the maximum DEMONSTRATED deployment speed, m/s. 140 KIAS on the SR22.</summary>
    public double MaxDeployIasMs { get; set; } = 72.0;
    /// <summary>Least height it is certified from in level flight, m. 500 ft; a spin needs 920.</summary>
    public double MinSafeAglM { get; set; } = 152.4;
    /// <summary>
    /// Where the harness pulls on the airframe, in body axes (x fore, y right, z DOWN). It must sit ABOVE
    /// the centre of gravity — negative z — or the aeroplane will not hang the right way up, because nothing
    /// in the model makes it do so except this offset.
    /// </summary>
    public double[] BridlePos { get; set; } = { -0.3, 0.0, -1.0 };

    public MathTypes.Vec3 BridleVec() => new(BridlePos[0], BridlePos[1], BridlePos[2]);
}

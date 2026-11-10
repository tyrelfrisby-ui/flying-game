using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// An air-driven gyro rotor: a brass wheel with buckets cut in its rim, spun by a jet of the air the vacuum
/// system pulls through the instrument.
///
///   I·dω/dt = ṁ·r·(v_jet − ω·r) − c·ω² − T_bearing
///
/// The drive is the impulse-turbine law for a flat bucket: the jet gives up the momentum by which it is
/// faster than the rim, so the torque falls to nothing as the rim approaches the jet. The drag is windage
/// (rotor churning the air in its housing, ∝ ω²) plus Coulomb friction in the two ball races.
///
/// What is measured and what is derived: the rotor's mass, radius and rated speed are the instrument's own
/// (E — a 3-1/8 in air gyro has a brass wheel of about this size and runs at about this speed). The
/// windage coefficient c is DERIVED so that at the rated suction the rotor settles at its rated speed. Spin-up
/// and run-down times are therefore outputs of the physics, not inputs — and they come out at a minute or
/// two each way, which is what a pilot sees: "give the gyros five minutes" before take-off, and an attitude
/// indicator that keeps a convincing horizon for a minute or more after the pump quits.
/// </summary>
public sealed class GyroRotor
{
    public readonly double InertiaKgM2, RadiusM, RatedRadS, WindageNmPerRadS2, BearingNm;
    /// <summary>Rated-condition jet momentum torque ṁ·r·v, N·m: the yardstick erection torques scale from.</summary>
    public readonly double RatedJetTorqueNm;

    public double OmegaRadS;

    /// <param name="massKg">Wheel mass (treated as a solid disc).</param>
    /// <param name="radiusM">Wheel (and bucket) radius.</param>
    /// <param name="ratedRpm">Speed at the rated suction.</param>
    /// <param name="ratedFlowKgS">Air through the instrument at the rated suction.</param>
    /// <param name="ratedJetMs">Jet velocity at the rated suction.</param>
    /// <param name="bearingNm">Coulomb torque of the two races.</param>
    public GyroRotor(double massKg, double radiusM, double ratedRpm, double ratedFlowKgS, double ratedJetMs, double bearingNm)
    {
        InertiaKgM2 = 0.5 * massKg * radiusM * radiusM;
        RadiusM = radiusM;
        RatedRadS = ratedRpm * 2.0 * Math.PI / 60.0;
        BearingNm = bearingNm;
        double drive = ratedFlowKgS * radiusM * (ratedJetMs - RatedRadS * radiusM);
        if (drive <= bearingNm)
            throw new ArgumentException("rotor: the rated jet cannot hold the rated speed (rim faster than the jet).");
        WindageNmPerRadS2 = (drive - bearingNm) / (RatedRadS * RatedRadS);
        RatedJetTorqueNm = ratedFlowKgS * radiusM * ratedJetMs;
    }

    /// <summary>Angular momentum, kg·m²/s.</summary>
    public double H => InertiaKgM2 * OmegaRadS;
    public double RatedH => InertiaKgM2 * RatedRadS;
    public double SpeedFraction => OmegaRadS / RatedRadS;

    public void Step(double dt, double flowKgS, double jetMs)
    {
        double w = OmegaRadS;
        double drive = flowKgS * RadiusM * (jetMs - w * RadiusM);
        double net = drive - WindageNmPerRadS2 * w * Math.Abs(w);
        // Coulomb friction: opposes motion, and holds a stopped wheel until the drive beats it.
        if (Math.Abs(w) < 1e-6 && Math.Abs(net) <= BearingNm) { OmegaRadS = 0.0; return; }
        net -= BearingNm * Math.Sign(w != 0 ? w : net);
        double next = w + net / InertiaKgM2 * dt;
        // Friction cannot reverse a wheel: it stops at zero.
        if (w > 0 && next < 0 && drive <= 0) next = 0.0;
        OmegaRadS = next;
    }

    /// <summary>
    /// Torque a jet of momentum flux ṁ·v delivers at <paramref name="leverM"/> as a fraction of the rated jet's —
    /// the scale of anything else in the instrument that is driven by the same air (the erection vanes).
    /// </summary>
    public double JetFraction(double flowKgS, double jetMs) => RatedJetTorqueNm > 0 ? flowKgS * RadiusM * jetMs / RatedJetTorqueNm : 0.0;
}

/// <summary>
/// The parts of a two-gimbal gyro that do not care which way its spin axis points: precession under a torque,
/// gimbal-bearing friction, and what happens at a gimbal stop.
/// </summary>
internal static class GimbalPhysics
{
    /// <summary>Below this relative rate, rad/s, the Coulomb friction is smoothed (a real race is not a perfect sign function).</summary>
    public const double FrictionSmoothRadS = 0.005;

    /// <summary>
    /// Precession: dH/dt = τ, so with the rotor speed held the spin axis moves along the part of the torque
    /// perpendicular to it, at |τ⊥|/H. Returns the new unit axis.
    /// </summary>
    public static Vec3 Precess(Vec3 s, Vec3 torque, double h, double dt)
    {
        if (h <= 1e-12) return s;
        Vec3 perp = torque - s * torque.Dot(s);
        return (s + perp * (dt / h)).Normalized();
    }

    /// <summary>
    /// Friction in a gimbal bearing whose axis is <paramref name="axis"/> (world) while the case turns about it at
    /// <paramref name="relRate"/> rad/s relative to the gimbal. The race drags the gimbal WITH the case, i.e. a torque
    /// T_f along the relative rotation, which a spinning rotor answers by precessing at right angles at T_f/H.
    ///
    /// If T_f/H is more than the relative rate itself the rotor is too slow to resist: the friction simply
    /// carries the gimbal round with the case, and the axis turns with it. That is how a run-down gyro ends up
    /// turning with the aeroplane rather than precessing away from it.
    /// </summary>
    public static Vec3 Friction(Vec3 s, Vec3 axis, double relRate, double frictionNm, double h, double dt)
    {
        if (frictionNm <= 0) return s;
        double sgn = Math.Tanh(relRate / FrictionSmoothRadS);
        if (h <= 1e-12 || frictionNm / h >= Math.Abs(relRate))
            return V.Rotate(s, axis, relRate * dt).Normalized();
        return Precess(s, axis * (frictionNm * sgn), h, dt);
    }
}

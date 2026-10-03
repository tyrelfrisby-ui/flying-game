using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Propeller effects, all of them, with correct signs for a right-hand prop (RotationSign=+1,
/// clockwise seen from behind):
///  - Thrust along body x (power/velocity with a static cap), offset ThrustLineZ from the CG.
///  - Torque reaction: rolls LEFT (-x moment) for a RH prop.
///  - P-factor: at positive alpha the down-going (right) blade sees higher AoA -> thrust center
///    shifts right -> yaws LEFT. Mirrored into pitch for sideslip.
///  - Spiral slipstream: the swirling tube strikes the left side of the fin -> yaws LEFT,
///    strongest at high power/low speed.
///  - Gyroscopic precession: prop angular momentum h = Ip*Omega*x; the -omega x h term means
///    pitching down yaws LEFT and yawing right pitches DOWN (classic taildragger behavior).
/// </summary>
public static class PropModel
{
    public static (Vec3 Force, Vec3 Moment) Compute(
        PropulsionConfig prop, double throttle01, Vec3 bodyVelocity, Vec3 bodyRates, double airDensity)
    {
        double thr = Math.Clamp(throttle01, 0.0, 1.0);

        // JET (propDiameterM <= 0): pure axial thrust, flat with speed to a cap, NO torque/P-factor/
        // slipstream/gyro (those are propeller-specific). MaxPowerW is reinterpreted as max thrust (N).
        if (prop.PropDiameterM <= 0.0)
        {
            double jetThrust = prop.MaxPowerW * thr;
            return (new Vec3(jetThrust, 0, jetThrust * prop.ThrustLineZ * 0), new Vec3(0, jetThrust * prop.ThrustLineZ, 0));
        }

        double rpm = prop.IdleRpm + (prop.MaxRpm - prop.IdleRpm) * thr;
        double omegaProp = rpm * 2.0 * Math.PI / 60.0;
        double powerW = prop.MaxPowerW * thr;

        double v = bodyVelocity.Length;
        double radius = prop.PropDiameterM * 0.5;

        // Thrust: eta*P/V, capped by momentum-theory static thrust.
        double discArea = Math.PI * radius * radius;
        // Ideal (momentum-theory) static thrust × a static figure of merit: real fixed-pitch/CS props make
        // ~55-65 % of the ideal at zero airspeed (a 180 hp 172 ≈ 2.7 kN, not the 5 kN momentum theory gives).
        const double staticFigureOfMerit = 0.6;
        double staticThrust = staticFigureOfMerit * Math.Pow(powerW * powerW * 2.0 * airDensity * discArea, 1.0 / 3.0);
        // Propeller efficiency falls off below the design speed (owner 2026-09-15: the sim climbed at 1.6× the book with a
        // flat 0.78). A fixed-pitch cruise prop makes ~85 % of its peak efficiency at Vy and ~60 % near the stall; a
        // constant-speed prop holds its efficiency down to about 60 kt.
        double effRatio = prop.ConstantSpeed
            ? Math.Clamp(0.6 + 0.4 * (v / 30.0), 0.6, 1.0)
            : Math.Clamp(0.35 + 0.65 * (v / 45.0), 0.4, 1.0);
        double eta = prop.Efficiency * effRatio;
        if (prop.ConstantSpeed && powerW > 0 && v > 5.0)
        {
            // Momentum (actuator-disc) limit (owner 2026-10-02, SR22 Vy read 82 kt vs book ~104): a constant-speed prop keeps
            // its blades at a good angle, but it still has to accelerate the air through its disc — at low speed and high
            // power per disc area that ideal efficiency 2/(1+√(1+T/(qA))) falls well below the blade's own, which is why
            // a 310 hp / 78 in SR22 climbs best near 100 kt, not at the minimum-power speed. × 0.85 for profile/swirl loss.
            double e = eta;
            for (int i = 0; i < 6; i++)
            {
                double t = e * powerW / v;
                double ideal = 2.0 / (1.0 + Math.Sqrt(1.0 + t / (0.5 * airDensity * v * v * discArea)));
                e = Math.Min(eta, 0.85 * ideal);
            }
            eta = e;
        }
        double thrust = powerW <= 0 ? 0 : Math.Min(eta * powerW / Math.Max(v, 5.0), staticThrust);

        Vec3 force = new(thrust, 0, 0);
        Vec3 moment = Vec3.Zero;

        // Thrust-line pitch moment (ThrustLineZ + is below CG -> nose-up with power).
        moment += new Vec3(0, thrust * prop.ThrustLineZ, 0);

        // Torque reaction (RH prop rolls the aircraft LEFT).
        double torque = omegaProp > 1 ? powerW / omegaProp : 0;
        moment += new Vec3(-prop.RotationSign * torque, 0, 0);

        // P-factor: thrust center offset toward the down-going blade.
        if (v > 1)
        {
            double alpha = Math.Atan2(bodyVelocity.Z, Math.Max(bodyVelocity.X, 1.0));
            double beta = Math.Asin(Math.Clamp(bodyVelocity.Y / v, -1, 1));
            double yOffset = prop.RotationSign * prop.PFactorK * radius * Math.Sin(alpha);   // + = right
            double zOffset = -prop.RotationSign * prop.PFactorK * radius * Math.Sin(beta);
            moment += new Vec3(0, thrust * zOffset, -thrust * yOffset);                       // r x F, r=(0,y,z)... N = -y*T? sign: offset right (+y) with thrust +x: Mz = -y*Fx -> yaw LEFT (negative) — correct for RH at +alpha
        }

        // Spiral slipstream: swirl strikes the fin -> yaw LEFT for RH prop; ~ P / V_slip where V_slip is the
        // speed of the air IN the slipstream (momentum theory: V_slip² = V² + 2T/(ρA)). Using freestream V
        // clamped at 8 m/s over-predicted the static yaw ~4× (a 172 at full power on the brakes needed
        // more rudder than it has) — at rest the fin sits in ~35-40 m/s of slipstream, not 8.
        double vSlip = Math.Sqrt(v * v + 2.0 * thrust / Math.Max(airDensity * discArea, 1e-3));
        moment += new Vec3(0, 0, -prop.RotationSign * prop.SlipstreamK * powerW / Math.Max(vSlip, 8.0));

        // Gyroscopic: M = -omega x h, h = Ip*OmegaProp along +x*sign.
        double h = prop.RotationSign * prop.PropInertia * omegaProp;
        moment += new Vec3(0, -bodyRates.Z * h, bodyRates.Y * h);

        return (force, moment);
    }
}

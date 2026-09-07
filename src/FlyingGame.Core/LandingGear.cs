using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Landing-gear ground reactions (NASA TM-1999-209143 / shimmy-analysis style, at low order): each
/// wheel is an oleo strut (spring + oil damping, vertical) plus a tire that makes longitudinal
/// (rolling/braking) and LATERAL (cornering) force. Cornering force is proportional to the tire's
/// slip angle up to the friction-circle limit μ·N — so a skidding tire saturates. Forces are applied
/// at each wheel's contact point, so the emergent behaviours the owner asked for FALL OUT of the
/// rigid-body dynamics with no special cases:
///  - a side load rolls the aircraft, compressing the outer strut more → weight shifts to that wheel;
///  - a TAILDRAGGER (CG behind the mains) is directionally UNSTABLE — a yaw disturbance puts a side
///    load at the CG behind the main pivot, which grows the yaw (ground-loop tendency), controllable
///    with rudder + steerable tailwheel; a tricycle (CG ahead of the mains) is self-stable.
/// Ground is the plane world z = groundZ (z is DOWN). Runway at sea level → groundZ = 0.
/// </summary>
public static class LandingGear
{
    public static (Vec3 Force, Vec3 Moment) Compute(
        AircraftConfig config, RigidBodyState s, double rudderCmd, double brakeCmd, double groundZ = 0.0)
    {
        Vec3 totalForce = Vec3.Zero, totalMoment = Vec3.Zero;
        if (config.Gear.Count == 0)
        {
            return (Vec3.Zero, Vec3.Zero);
        }

        Vec3 cg = config.Mass.CgVec();
        Vec3 worldDown = new(0, 0, 1); // NED: +z is down

        foreach (GearConfig g in config.Gear)
        {
            Vec3 rBody = g.PosVec() - cg;
            Vec3 wheelWorld = s.Position + s.Attitude.Rotate(rBody);
            double penetration = wheelWorld.Z - groundZ; // >0 = wheel below ground surface (compressed)
            if (penetration <= 0.0)
            {
                continue; // wheel in the air
            }

            // Contact-point velocity (world) = body vel + ω×r, rotated to world.
            Vec3 contactVelWorld = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
            double compressionRate = contactVelWorld.Z; // vertical closing rate (+ = compressing)

            // Oleo strut reacts along the WORLD VERTICAL (the ground pushes straight up regardless of
            // aircraft pitch — using the body axis here gives a spurious fore/aft force). Never pulls.
            double normalN = g.SpringN * penetration + g.DampNs * System.Math.Max(0, compressionRate);
            normalN = System.Math.Max(0.0, normalN);
            Vec3 normalForce = worldDown * (-normalN); // straight up

            // Ground-plane tire frame: forward = aircraft heading projected on ground, right = ×down.
            Vec3 fwdWorld = s.Attitude.Rotate(new Vec3(1, 0, 0));
            Vec3 groundUp = new(0, 0, -1);
            Vec3 fwdGround = (fwdWorld - groundUp * Vec3.Dot(fwdWorld, groundUp));
            double fwdLen = fwdGround.Length;
            if (fwdLen < 1e-6)
            {
                totalForce += normalForce;
                totalMoment += Vec3.Cross(s.Attitude.Rotate(rBody), normalForce);
                continue;
            }

            fwdGround /= fwdLen;
            Vec3 rightGround = Vec3.Cross(groundUp, fwdGround); // right-hand: up × fwd = right

            // Steered wheels (nose / steerable tailwheel) point the tire by the rudder command.
            double steer = g.IsSteerable ? rudderCmd * g.MaxSteerRad : 0.0;
            Vec3 tireFwd = fwdGround * System.Math.Cos(steer) + rightGround * System.Math.Sin(steer);
            Vec3 tireRight = Vec3.Cross(groundUp, tireFwd);

            // Contact velocity in the ground plane, decomposed on the tire frame.
            Vec3 velGround = contactVelWorld - groundUp * Vec3.Dot(contactVelWorld, groundUp);
            double vFwd = Vec3.Dot(velGround, tireFwd);
            double vSide = Vec3.Dot(velGround, tireRight);
            double speed = velGround.Length;

            // Lateral: cornering force opposes slip, ∝ slip angle, capped by the friction circle.
            double slip = System.Math.Atan2(vSide, System.Math.Abs(vFwd) + 0.5);
            double lateralN = -g.CorneringStiffnessN * slip;
            double muLimit = g.TireMu * normalN;
            lateralN = System.Math.Clamp(lateralN, -muLimit, muLimit);

            // Longitudinal: rolling resistance + braking, opposing forward motion, sharing the μ budget.
            double brakeN = g.Brake ? brakeCmd * muLimit * 0.9 : 0.0;
            double rollN = g.RollResistN * (normalN / System.Math.Max(1.0, g.SpringN * 0.3));
            double longN = -(rollN + brakeN) * System.Math.Sign(vFwd == 0 ? 1 : vFwd);
            double longBudget = System.Math.Sqrt(System.Math.Max(0.0, muLimit * muLimit - lateralN * lateralN));
            longN = System.Math.Clamp(longN, -longBudget, longBudget);

            Vec3 tireForce = tireRight * lateralN + tireFwd * longN;
            Vec3 wheelForce = normalForce + tireForce;

            totalForce += wheelForce;
            totalMoment += Vec3.Cross(s.Attitude.Rotate(rBody), wheelForce);
        }

        return (totalForce, totalMoment);
    }

    /// <summary>True if any wheel is in contact with the ground (for on-ground state / challenge logic).</summary>
    public static bool OnGround(AircraftConfig config, RigidBodyState s, double groundZ = 0.0)
    {
        Vec3 cg = config.Mass.CgVec();
        foreach (GearConfig g in config.Gear)
        {
            Vec3 wheelWorld = s.Position + s.Attitude.Rotate(g.PosVec() - cg);
            if (wheelWorld.Z - groundZ > 0.0)
            {
                return true;
            }
        }

        return false;
    }
}

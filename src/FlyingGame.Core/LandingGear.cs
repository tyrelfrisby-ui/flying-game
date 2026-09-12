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
    /// <summary>True if any main (non-tail) wheel is at or below the local ground.</summary>
    public static bool AnyMainWheelOnGround(AircraftConfig config, RigidBodyState s)
    {
        Vec3 cg = config.Mass.CgVec();
        foreach (GearConfig g in config.Gear)
        {
            if (g.IsTailwheel) continue;
            Vec3 w = s.Position + s.Attitude.Rotate(g.PosVec() - cg);
            double? waterH = FloatHydro.WaterSurfaceAt(w.X, w.Y);
            if (waterH.HasValue && waterH.Value >= WorldTerrain.GroundHeightAt(w.X, w.Y) - 0.01) continue;
            if (w.Z >= -WorldTerrain.WheelGroundHeightAt(w.X, w.Y) - 0.02) return true;
        }
        return false;
    }

    /// <summary>
    /// The aircraft at rest on ALL its wheels (owner 2026-09-09: a taildragger spawned level dropped its tail from a
    /// metre up and broke the tail boom): the three-point pitch that puts the mains and the tail/nose wheel on the
    /// ground together, CG height so the lowest wheel sits 2 cm into the surface (struts settle), heading given.
    /// </summary>
    public static RigidBodyState RestingState(AircraftConfig c, double x, double y, double groundElevM, double headingRad = 0.0)
    {
        Vec3 cg = c.Mass.CgVec();
        var mains = c.Gear.FindAll(g => !g.IsTailwheel && g.GearType != "nose-skid" && g.GearType != "float-keel" && System.Math.Abs(g.Pos[1]) < 2.5 && g.Pos[2] > 0);
        double pitch = 0.0;
        if (mains.Count > 0)
        {
            // Main wheel reference: the lowest-hanging main. Third wheel: the tailwheel (aft) or the nosewheel (ahead).
            GearConfig m = mains[0]; foreach (GearConfig g in mains) if (g.Pos[2] > m.Pos[2]) m = g;
            GearConfig? third = null;
            foreach (GearConfig g in c.Gear)
            {
                if (g == m || g.Pos[2] <= 0 || g.GearType == "nose-skid" || g.GearType == "float-keel") continue;
                if (g.IsTailwheel || (System.Math.Abs(g.Pos[1]) < 0.3 && System.Math.Abs(g.Pos[0] - m.Pos[0]) > 1.0))
                    if (third == null || System.Math.Abs(g.Pos[0] - m.Pos[0]) > System.Math.Abs(third.Pos[0] - m.Pos[0])) third = g;
            }
            if (third != null && System.Math.Abs(m.Pos[0] - third.Pos[0]) > 0.5)
                pitch = System.Math.Atan((m.Pos[2] - third.Pos[2]) / (m.Pos[0] - third.Pos[0]));   // nose up when the tailwheel hangs higher
        }
        var qPitch = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
        var qYaw = new Quat(0, 0, System.Math.Sin(headingRad / 2), System.Math.Cos(headingRad / 2));
        Quat att = Quat.Multiply(qYaw, qPitch);
        double lowest = double.MinValue;   // NED z (down) of the lowest wheel relative to the CG
        foreach (GearConfig g in c.Gear)
        {
            if (g.GearType == "float-keel" || g.Pos[2] <= 0) continue;
            lowest = System.Math.Max(lowest, att.Rotate(g.PosVec() - cg).Z);
        }
        if (lowest == double.MinValue) lowest = 0.0;
        return new RigidBodyState(new Vec3(x, y, -(groundElevM + lowest - 0.02)), att, Vec3.Zero, Vec3.Zero);
    }

    /// <summary>First-contact sink rate (m/s) that tears a leg off when the config gives none: FAR 23 designs the
    /// gear for 10 ft/s (3 m/s); ~4 m/s is past its ultimate. A tailwheel takes the tail dropping through onto it at
    /// the end of every wheel landing (5–6 m/s at the wheel from the pitch rate), so it is only lost to a real slam.</summary>
    public const double DefaultBreakSinkMs = 4.0, DefaultTailwheelBreakSinkMs = 8.0;
    /// <summary>Tyre cornering stiffness per newton of wheel load (N per rad per N): ~12 for an aircraft tyre.</summary>
    public const double CorneringPerLoad = 12.0;
    /// <summary>A tailwheel's small contact patch: about half the cornering per newton, and a lower friction cap.</summary>
    public const double TailwheelCorneringPerLoad = 6.0, TailwheelMuScale = 0.85;
    public const double DefaultSteerSpringNmPerRad = 25.0, DefaultSteerDampNms = 3.0, DefaultCastorTrailM = 0.06, DefaultBreakoutRad = 0.61;   // 35°; soft springs (owner: "a lot of give") — tune in testing

    /// <summary>
    /// The castoring tailwheel's own yaw (owner 2026-09-12): the wheel is not bolted to the rudder — steering springs
    /// pull it toward the rudder command, the ground force on its contact patch (a trail behind the pivot) swings it
    /// toward the direction it is actually rolling, and past the breakout angle it swivels free. Its load is whatever
    /// the tail spring carries — tail weight less the aerodynamic tail load, so a tail-up wheel landing carries none.
    /// </summary>
    public sealed class TailwheelState
    {
        public double AngleRad;        // wheel yaw relative to the fuselage, + = wheel turned right
        public double LastLateralN;    // side force on the contact patch last step (diagnostics)
        public double LastLoadN;       // vertical load last step
        public bool FreeSwivel;        // past the breakout: springs disengaged
    }

    /// <summary>Advance the tailwheel castor angle by dt from the current state and rudder command.</summary>
    public static void UpdateTailwheel(AircraftConfig config, RigidBodyState s, TailwheelState tw, double rudderCmd, double dt)
    {
        GearConfig? g = null;
        foreach (GearConfig c in config.Gear) if (c.IsTailwheel && c.IsSteerable) { g = c; break; }
        if (g == null || tw == null) return;
        double k = g.SteerSpringNmPerRad > 0 ? g.SteerSpringNmPerRad : DefaultSteerSpringNmPerRad;
        double cD = g.SteerDampNms > 0 ? g.SteerDampNms : DefaultSteerDampNms;
        double trail = g.CastorTrailM > 0 ? g.CastorTrailM : DefaultCastorTrailM;
        double breakout = g.BreakoutRad > 0 ? g.BreakoutRad : DefaultBreakoutRad;
        double cmd = rudderCmd * g.MaxSteerRad;

        // Ground force on the patch: from the wheel's own slip at its CURRENT angle (the same tyre model Compute uses).
        Vec3 cg = config.Mass.CgVec();
        Vec3 rBody = g.PosVec() - cg;
        Vec3 wheelWorld = s.Position + s.Attitude.Rotate(rBody);
        double groundH = WorldTerrain.WheelGroundHeightAt(wheelWorld.X, wheelWorld.Y);
        double penetration = wheelWorld.Z + groundH;
        double lateralN = 0, loadN = 0;
        if (penetration > 0)
        {
            Vec3 vel = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
            loadN = System.Math.Max(0, g.SpringN * penetration + g.DampNs * System.Math.Max(0, vel.Z));
            Vec3 fwdWorld = s.Attitude.Rotate(new Vec3(1, 0, 0));
            Vec3 fwdGround = new Vec3(fwdWorld.X, fwdWorld.Y, 0);
            if (fwdGround.Length > 1e-6)
            {
                fwdGround = fwdGround / fwdGround.Length;
                Vec3 rightGround = new Vec3(-fwdGround.Y, fwdGround.X, 0);
                Vec3 tireFwd = fwdGround * System.Math.Cos(tw.AngleRad) + rightGround * System.Math.Sin(tw.AngleRad);
                Vec3 tireRight = new Vec3(-tireFwd.Y, tireFwd.X, 0);
                double vFwd = Vec3.Dot(vel, tireFwd), vSide = Vec3.Dot(vel, tireRight);
                double slip = System.Math.Atan2(vSide, System.Math.Abs(vFwd) + 0.5);
                double cornering = System.Math.Max(g.CorneringStiffnessN, TailwheelCorneringPerLoad * loadN);
                lateralN = System.Math.Clamp(-cornering * slip, -g.TireMu * TailwheelMuScale * loadN, g.TireMu * TailwheelMuScale * loadN);
            }
        }
        tw.LastLateralN = lateralN; tw.LastLoadN = loadN;

        // Castor: a side force on a patch behind the pivot turns the wheel toward its velocity. Springs: toward the
        // rudder command unless broken out. Overdamped first-order swing (the wheel's own inertia is negligible).
        double err = tw.AngleRad - cmd;
        tw.FreeSwivel = System.Math.Abs(err) > breakout;
        double torque = -lateralN * trail - (tw.FreeSwivel ? 0.0 : k * err);
        int sub = 4; double h = dt / sub;
        for (int i = 0; i < sub; i++)
        {
            double rate = torque / cD;
            rate = System.Math.Clamp(rate, -12.0, 12.0);
            tw.AngleRad += rate * h;
            err = tw.AngleRad - cmd;
            tw.FreeSwivel = System.Math.Abs(err) > breakout;
            torque = -lateralN * trail - (tw.FreeSwivel ? 0.0 : k * err);
        }
        // Full swivel: keep the angle wrapped so a 180° spin reads as a wheel rolling backwards, not 540°.
        while (tw.AngleRad > System.Math.PI) tw.AngleRad -= 2 * System.Math.PI;
        while (tw.AngleRad < -System.Math.PI) tw.AngleRad += 2 * System.Math.PI;
    }

    /// <param name="lostLegs">legs already torn off (skipped)</param>
    /// <param name="broken">receives the leg that just hit harder than its break sink rate</param>
    /// <param name="touching">per-leg contact state (first-contact detection), config.Gear order</param>
    /// <param name="impacts">records the hardest first contact (crash sound)</param>
    public static (Vec3 Force, Vec3 Moment) Compute(
        AircraftConfig config, RigidBodyState s, double rudderCmd, double brakeCmd, double groundZ = 0.0, double brakeBias = 0.0,
        IReadOnlyCollection<AirframeComponent>? lostLegs = null, List<AirframeComponent>? broken = null, bool[]? touching = null, ImpactRecorder? impacts = null,
        TailwheelState? tailwheel = null)
    {
        Vec3 totalForce = Vec3.Zero, totalMoment = Vec3.Zero;
        if (config.Gear.Count == 0)
        {
            return (Vec3.Zero, Vec3.Zero);
        }

        Vec3 cg = config.Mass.CgVec();
        Vec3 worldDown = new(0, 0, 1); // NED: +z is down

        for (int gi = 0; gi < config.Gear.Count; gi++)
        {
            GearConfig g = config.Gear[gi];
            AirframeComponent leg = AirframeContact.GearComponent(g);
            if (lostLegs != null && lostLegs.Contains(leg)) continue;   // torn off: the stub/belly hard points carry the load
            // Differential braking: wheels left of centre get wheelBrake·(1+bias·-1)... i.e. bias<0 favours LEFT.
            double wheelBrake = g.Pos[1] < -0.05 ? brakeCmd * System.Math.Clamp(1.0 - brakeBias, 0.0, 1.0)
                              : g.Pos[1] > 0.05 ? brakeCmd * System.Math.Clamp(1.0 + brakeBias, 0.0, 1.0)
                              : brakeCmd;
            Vec3 rBody = g.PosVec() - cg;
            Vec3 wheelWorld = s.Position + s.Attitude.Rotate(rBody);
            // Ground under THIS wheel: the world height field (plateau airports) — NED z = -height.
            double groundH = WorldTerrain.WheelGroundHeightAt(wheelWorld.X, wheelWorld.Y);
            double? waterH = FloatHydro.WaterSurfaceAt(wheelWorld.X, wheelWorld.Y);
            if (waterH.HasValue && waterH.Value >= groundH - 0.01) continue; // over water: the hull floats, wheels don't touch the bed
            WorldTerrain.Surface surface = WorldTerrain.Active != null ? WorldTerrain.SurfaceAt(wheelWorld.X, wheelWorld.Y) : WorldTerrain.Surface.Paved;
            double localGroundZ = groundZ - groundH;
            double penetration = wheelWorld.Z - localGroundZ; // >0 = wheel below ground surface (compressed)
            if (penetration <= 0.0)
            {
                if (touching != null && gi < touching.Length) touching[gi] = false;
                continue; // wheel in the air
            }

            // Contact-point velocity (world) = body vel + ω×r, rotated to world.
            Vec3 contactVelWorld = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
            double compressionRate = contactVelWorld.Z; // vertical closing rate (+ = compressing)
            if (touching != null && gi < touching.Length && !touching[gi])
            {
                // Touchdown on this leg: a hard enough one tears it off (individually — the other legs stay).
                touching[gi] = true;
                impacts?.Record(compressionRate, leg.ToString());
                double breakSink = (g.BreakSinkMs > 0 ? g.BreakSinkMs : g.IsTailwheel ? DefaultTailwheelBreakSinkMs : DefaultBreakSinkMs) * config.ImpactStrength;
                if (broken != null && compressionRate > breakSink && !broken.Contains(leg)) broken.Add(leg);
            }

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

            // Steered wheels: a nosewheel points where the rudder says; a steerable TAILWHEEL points where its castor
            // state says (springs toward the rudder, ground force toward its velocity — UpdateTailwheel).
            bool castoring = g.IsTailwheel && g.IsSteerable && tailwheel != null;
            double steer = castoring ? tailwheel!.AngleRad : g.IsSteerable ? rudderCmd * g.MaxSteerRad : 0.0;
            Vec3 tireFwd = fwdGround * System.Math.Cos(steer) + rightGround * System.Math.Sin(steer);
            Vec3 tireRight = Vec3.Cross(groundUp, tireFwd);

            // Contact velocity in the ground plane, decomposed on the tire frame.
            Vec3 velGround = contactVelWorld - groundUp * Vec3.Dot(contactVelWorld, groundUp);
            double vFwd = Vec3.Dot(velGround, tireFwd);
            double vSide = Vec3.Dot(velGround, tireRight);
            double speed = velGround.Length;

            // Lateral: cornering force opposes slip, ∝ slip angle, capped by the friction circle. Cornering stiffness
            // grows with the wheel load (a tyre's C ≈ 10–15 × its vertical load per radian): a fixed per-type constant
            // left the heavier types with a fifth of their friction budget and they skated sideways (owner).
            double slip = System.Math.Atan2(vSide, System.Math.Abs(vFwd) + 0.5);
            double cornering = System.Math.Max(g.CorneringStiffnessN, (g.IsTailwheel ? TailwheelCorneringPerLoad : CorneringPerLoad) * normalN);
            double lateralN = -cornering * slip;
            double muLimit = g.TireMu * (g.IsTailwheel ? TailwheelMuScale : 1.0) * normalN;
            lateralN = System.Math.Clamp(lateralN, -muLimit, muLimit);

            // Longitudinal: rolling resistance + braking, opposing forward motion, sharing the μ budget.
            double brakeN = g.Brake ? wheelBrake * muLimit * 0.9 : 0.0;
            // Rolling resistance = μ_r · wheel load, μ_r by surface (paved 0.025 … rough ground 0.15).
            double rollN = WorldTerrain.RollingCoefficient(surface) * normalN;
            double longN = -(rollN + brakeN) * System.Math.Sign(vFwd == 0 ? 1 : vFwd);
            double longBudget = System.Math.Sqrt(System.Math.Max(0.0, muLimit * muLimit - lateralN * lateralN));
            longN = System.Math.Clamp(longN, -longBudget, longBudget);

            Vec3 tireForce = tireRight * lateralN + tireFwd * longN;
            Vec3 wheelForce = normalForce + tireForce;
            Aero.ForceDebug.Add(g.PosVec(), s.Attitude.Conjugate().Rotate(wheelForce), Vec3.Zero, "gear");

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

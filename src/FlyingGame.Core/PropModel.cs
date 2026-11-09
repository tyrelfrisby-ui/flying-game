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
    /// <summary>The prop's effects one by one (Ty 2026-10-13: "since the propeller is the dominant driving force for the
    /// rotation show the vectors of the propeller's forces and label them"): the thrust and where it acts (the P-factor's
    /// offset of its centre from the hub, m), the torque reaction (roll, N·m), the spiral slipstream's yaw on the fin (N·m)
    /// and the gyroscopic moment. <see cref="Compute(PropulsionConfig, double, Vec3, Vec3, double)"/> is their sum.</summary>
    public struct Parts { public double Thrust, PFactorY, PFactorZ, TorqueRoll, SlipYaw, SwirlC; public Vec3 Gyro, Normal, InPlane; }

    /// <summary>
    /// THE PROPELLER'S IN-PLANE (NORMAL) FORCE (2026-11-08). Every propeller effect in this model was until now either
    /// axial force or a pure moment: thrust along +x, the thrust-line and P-factor moments, the spiral slipstream's yaw
    /// and the gyro. A disc meeting the air at an angle also makes a force IN its own plane, and nothing produced one.
    /// SPIN-ROTARY-DATA named it as the next piece of work: "the prop's in-plane (H) force is not modelled".
    ///
    /// Momentum form. The disc ingests mass flow mdot = rho*A*w, where w is the through-disc velocity: the axial
    /// component of the relative wind plus the induced velocity v_i = sqrt(T / (2*rho*A)). It turns what it ingests
    /// toward its own axis, destroying a share k of that flow's CROSS-stream momentum, so the reaction on the airframe
    /// is k*mdot*(the air's in-plane velocity). The air approaches at -bodyVelocity, so the force opposes the in-plane
    /// components of the aircraft's own velocity. (Glauert, ARC R&amp;M 1111; Ribner, NACA TR 820, "Formulas for
    /// propellers in yaw", 1945; Perkins &amp; Hage ch. 5 / USAF DATCOM 4.6.1 for the normal-force derivative.)
    ///
    /// Sign check: at positive alpha (w &gt; 0) the air comes up at the disc from below, the disc pushes it down and the
    /// reaction is UP (body -z) at a station AHEAD of the CG — the classic nose-up, statically destabilising propeller
    /// normal force of a tractor layout. It flattens BOTH spins: at alpha +85 the body nose-up raises alpha further,
    /// and inverted at alpha -90 the body nose-DOWN is nose-up with respect to the earth. That is the symmetric
    /// power-flattening term the gyro cannot be, because the gyro mirrors exactly between upright L and inverted R.
    ///
    /// LIMITATION, stated plainly: the cross-momentum form is a small-angle result being used at the 50-90 deg inflow
    /// angles of a spin, where it grows linearly in the crossflow with nothing to saturate it. mdot is honest at any
    /// angle (edge-on, the through-flow is the induced velocity alone, which this gives), but k is one number standing
    /// in for Ribner's blade-geometry integral, and <see cref="NormalForceK"/> is NOT measured. The term's SHAPE is
    /// published physics; its magnitude is a coefficient inside a plausible band, like the prop inertia beside it.
    /// It is per-type and OFF by default (<see cref="PropulsionConfig.NormalForceK"/>): the term acts in attached flight
    /// too, where every stored rig and trim was solved without it, so switching it on fleet-wide is a re-rig.
    /// </summary>
    public static Vec3 NormalForce(PropulsionConfig prop, double thrustN, Vec3 bodyVelocity, double airDensity, double fraction)
    {
        double radius = 0.5 * prop.PropDiameterM;
        if (radius <= 0.0 || fraction <= 0.0 || airDensity <= 0.0) return Vec3.Zero;
        double area = Math.PI * radius * radius;
        double vInduced = Math.Sqrt(Math.Max(0.0, thrustN) / (2.0 * airDensity * area));
        double through = Math.Max(0.0, bodyVelocity.X) + vInduced;
        double mdot = airDensity * area * through;
        double k = prop.NormalForceK * Math.Clamp(fraction, 0.0, 1.0) * mdot;
        return new Vec3(0.0, -k * bodyVelocity.Y, -k * bodyVelocity.Z);
    }

    /// <summary>The static thrust <see cref="Compute(PropulsionConfig, double, Vec3, Vec3, double, PropDrive, out Parts)"/>
    /// caps the thrust at (momentum theory × the static figure of merit, sea level), N.</summary>
    public static double StaticThrustN(PropulsionConfig prop, double throttle01, double airDensity = 1.225)
    {
        if (prop.PropDiameterM <= 0) return prop.MaxPowerW * Math.Clamp(throttle01, 0, 1);
        double powerW = prop.MaxPowerW * Math.Clamp(throttle01, 0, 1), r = 0.5 * prop.PropDiameterM;
        return StaticFigureOfMerit * Math.Pow(powerW * powerW * 2.0 * airDensity * Math.PI * r * r, 1.0 / 3.0);
    }

    /// <summary>Real fixed-pitch/CS props make ~55-65 % of the ideal (momentum-theory) thrust at zero airspeed.</summary>
    public const double StaticFigureOfMerit = 0.6;

    public static (Vec3 Force, Vec3 Moment) Compute(
        PropulsionConfig prop, double throttle01, Vec3 bodyVelocity, Vec3 bodyRates, double airDensity)
        => Compute(prop, throttle01, bodyVelocity, bodyRates, airDensity, out _);

    public static (Vec3 Force, Vec3 Moment) Compute(
        PropulsionConfig prop, double throttle01, Vec3 bodyVelocity, Vec3 bodyRates, double airDensity, out Parts parts)
        => Compute(prop, throttle01, bodyVelocity, bodyRates, airDensity, PropDrive.Running, out parts);

    public static (Vec3 Force, Vec3 Moment) Compute(
        PropulsionConfig prop, double throttle01, Vec3 bodyVelocity, Vec3 bodyRates, double airDensity, PropDrive drive)
        => Compute(prop, throttle01, bodyVelocity, bodyRates, airDensity, drive, out _);

    /// <summary>
    /// What is really turning the propeller (Ty 2026-11-04: after a prop strike in the Extra "the prop was completely
    /// stopped but if i had the throttle up it would still yaw with rudder"). Every term below used to come from the
    /// THROTTLE alone — power = MaxPowerW × throttle, rpm from throttle — so a dead engine with the lever forward still
    /// made torque, P-factor, spiral slipstream, gyroscopic moment and a wash over the tail. Now: a running engine
    /// makes <see cref="PowerScale"/> × the throttle's power (mixture and governor; 1 = exactly as before); a dead one
    /// makes none, its prop windmilling at <see cref="Rpm"/> (drag and gyro only), or stopped (Rpm 0).
    /// </summary>
    public readonly struct PropDrive
    {
        public readonly bool EngineRunning;
        /// <summary>Running: shaft power relative to the throttle's (mixture × governor). Ignored when dead.</summary>
        public readonly double PowerScale;
        /// <summary>Dead: how fast the air turns the prop (0 = stopped). Ignored when running.</summary>
        public readonly double Rpm;
        /// <summary>Running, constant-speed: the governor (prop) lever, 0..1 (1 = fine, full rpm). Ignored otherwise.</summary>
        public readonly double PropLever;
        /// <summary>
        /// THE RPM THE SHAFT IS ACTUALLY TURNING, when the caller is integrating it as a state (Aircraft does). 0
        /// means "nobody is tracking it", and the model falls back to the old throttle law — which is how a type
        /// whose blade has not been fitted yet keeps flying exactly as it did.
        ///
        /// It has to come from outside because it is not a function of the throttle: a propeller's rpm is where the
        /// engine's torque and the blade's balance, and it ACCELERATES against the propeller's inertia. That is what
        /// makes a fixed-pitch aeroplane turn 2150 standing still and red-line in a dive on the same throttle.
        /// </summary>
        public readonly double ShaftRpm;
        /// <summary>A governed propeller's blade angle at 0.75 R this instant, degrees; 0 = use the config's fitted
        /// pitch (a fixed-pitch blade, or before the governor has been solved).</summary>
        public readonly double BladePitchDeg;
        private PropDrive(bool running, double scale, double rpm, double propLever = 1.0, double shaftRpm = 0.0, double bladePitchDeg = 0.0)
        { EngineRunning = running; PowerScale = scale; Rpm = rpm; PropLever = propLever; ShaftRpm = shaftRpm; BladePitchDeg = bladePitchDeg; }
        public static readonly PropDrive Running = new(true, 1.0, 0.0);
        public static PropDrive RunningAt(double powerScale) => new(true, Math.Max(0.0, powerScale), 0.0);
        public static PropDrive RunningAt(double powerScale, double propLever) => new(true, Math.Max(0.0, powerScale), 0.0, Math.Clamp(propLever, 0.0, 1.0));
        public static PropDrive Dead(double windmillRpm) => new(false, 0.0, Math.Max(0.0, windmillRpm));
        /// <summary>The same drive, told what the shaft is doing and (for a governor) what the blade is set to.</summary>
        public PropDrive WithShaft(double shaftRpm, double bladePitchDeg = 0.0)
            => new(EngineRunning, PowerScale, Rpm, PropLever, Math.Max(0.0, shaftRpm), Math.Max(0.0, bladePitchDeg));
    }

    /// <summary>Lowest governed rpm, as a fraction of <c>MaxRpm</c> (a 2,700 rpm Lycoming's governor bottoms near 1,900).</summary>
    public const double LowGovernorFraction = 0.7;

    /// <summary>
    /// The rpm a RUNNING engine's propeller turns at, for its gyroscopic couple. Fixed pitch: the throttle law
    /// (idle → MaxRpm). Constant speed: the governor (the engine's own rpm, <c>Aircraft.EngineRpm</c>): on the fine-pitch
    /// stop below 30 % throttle, then <see cref="PropulsionConfig.GovernedRpm"/> as the prop lever sets it, rising to MaxRpm
    /// only firewalled (take-off). Q007, owner 2026-11-05: the gyro used the throttle law for every prop, so a governed
    /// prop's angular momentum grew 1.8× from 40 % power (1,500 rpm) to full (2,700) while the engine itself turned
    /// 2,500 → 2,700, and that extra gyro couple pitched the Pitts' full-power flat spin past flat and rolled its
    /// inverted flat out.
    /// </summary>
    public static double RunningRpm(PropulsionConfig prop, double throttle01, double propLever = 1.0)
    {
        double idle = prop.IdleRpm, max = prop.MaxRpm, thr = Math.Clamp(throttle01, 0.0, 1.0);
        if (!prop.ConstantSpeed) return idle + (max - idle) * thr;
        double lever = Math.Clamp(propLever, 0.0, 1.0);
        double low = Math.Max(idle, LowGovernorFraction * max);
        double govTop = Math.Clamp(prop.GovernedRpm, idle, max);
        double gov = low + (govTop - low) * lever;
        double takeoff = low + (max - low) * lever;   // full forward and firewalled: up to the red line
        double finePitch = idle + (Math.Min(gov, 1800.0) - idle) * Math.Min(1.0, thr / 0.3);
        if (thr < 0.3) return finePitch;
        double t = Math.Clamp((thr - 0.9) / 0.1, 0.0, 1.0);
        return gov + (takeoff - gov) * t * t * (3 - 2 * t);
    }

    /// <summary>A stopped (not windmilling) prop's drag as a fraction of the windmilling one: a fixed blade blocks
    /// less of the disc than one the air is driving.</summary>
    public const double StoppedDragFraction = 0.5;

    public static (Vec3 Force, Vec3 Moment) Compute(
        PropulsionConfig prop, double throttle01, Vec3 bodyVelocity, Vec3 bodyRates, double airDensity, PropDrive drive, out Parts parts)
        => Compute(prop, throttle01, bodyVelocity, bodyRates, airDensity, drive, out parts, null);

    /// <summary>The same, also reporting the slipstream's radial swirl distribution into a caller-owned buffer.</summary>
    public static (Vec3 Force, Vec3 Moment) Compute(
        PropulsionConfig prop, double throttle01, Vec3 bodyVelocity, Vec3 bodyRates, double airDensity, PropDrive drive,
        out Parts parts, BladeElement.SwirlProfile? profile)
    {
        if (profile is not null) profile.Any = false;
        parts = default;
        // A dead engine: no power whatever the lever says. The windmill drag below then sees a closed throttle.
        double thr = drive.EngineRunning ? Math.Clamp(throttle01, 0.0, 1.0) : 0.0;

        // JET (propDiameterM <= 0): pure axial thrust, flat with speed to a cap, NO torque/P-factor/
        // slipstream/gyro (those are propeller-specific). MaxPowerW is reinterpreted as max thrust (N).
        if (prop.PropDiameterM <= 0.0)
        {
            double jetThrust = prop.MaxPowerW * thr;
            parts.Thrust = jetThrust;
            return (new Vec3(jetThrust, 0, jetThrust * prop.ThrustLineZ * 0), new Vec3(0, jetThrust * prop.ThrustLineZ, 0));
        }

        // THE BLADE-ELEMENT PATH (Phase 1, Ty 2026-11-09). When a type's blade has been fitted
        // (propulsion.bladePitch75Deg, from its static rpm) and the caller supplies the rpm the shaft is ACTUALLY
        // turning, the propeller is solved instead of estimated: thrust and torque from the blade, P-factor and the
        // in-plane force from the azimuthal integral, and the swirl from the torque. That retires eta*P/V, the static
        // figure of merit, PFactorK, NormalForceK and SlipstreamK in one path.
        //
        // The rpm has to come from OUTSIDE because it is a STATE: a propeller's rpm is where the engine's torque and
        // the blade's balance, and it accelerates against the propeller's inertia rather than jumping. Aircraft
        // integrates it. When nothing supplies one, or the type has no fitted blade, everything below is unchanged —
        // which is how the fleet stays flyable while the blades are fitted one at a time.
        if (IsSolved(prop, drive))
            return BladeElementForces(prop, bodyVelocity, bodyRates, airDensity, drive, ref parts, profile);

        double rpm = drive.EngineRunning ? RunningRpm(prop, thr, drive.PropLever) : drive.Rpm;   // the gyro's
        // The torque reaction keeps the throttle law: every type's stored rig (RigTests) was solved against it, so moving it
        // to the governed rpm is a re-rig of the constant-speed fleet, not part of Q007 (AGENT-LOG 2026-11-05, open).
        double omegaProp = (drive.EngineRunning ? prop.IdleRpm + (prop.MaxRpm - prop.IdleRpm) * thr : drive.Rpm) * 2.0 * Math.PI / 60.0;
        double powerW = drive.EngineRunning ? prop.MaxPowerW * thr * drive.PowerScale : 0.0;

        double v = bodyVelocity.Length;
        double radius = prop.PropDiameterM * 0.5;

        // Thrust: eta*P/V, capped by momentum-theory static thrust.
        double discArea = Math.PI * radius * radius;
        // Ideal (momentum-theory) static thrust × a static figure of merit: real fixed-pitch/CS props make
        // ~55-65 % of the ideal at zero airspeed (a 180 hp 172 ≈ 2.7 kN, not the 5 kN momentum theory gives).
        double staticThrust = StaticFigureOfMerit * Math.Pow(powerW * powerW * 2.0 * airDensity * discArea, 1.0 / 3.0);
        // Propeller efficiency falls off below the design speed (owner 2026-09-15: the sim climbed at 1.6× the book with a
        // flat 0.78). A fixed-pitch cruise prop makes ~85 % of its peak efficiency at Vy and ~60 % near the stall; a
        // constant-speed prop holds its efficiency down to about 60 kt.
        double effRatio = prop.ConstantSpeed
            ? Math.Clamp(0.6 + 0.4 * (v / 30.0), 0.6, 1.0)
            : Math.Clamp(0.35 + 0.65 * (v / Math.Max(10.0, prop.DesignSpeedMs)), 0.4, 1.0);
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
        // The idling prop windmills: drag on the disc, fading out by 20 % throttle (above that it is pulling).
        if (thr < 0.2 && v > 5.0 && prop.IdleDragCd > 0)
        {
            double cd = prop.IdleDragCd * (1 - thr / 0.2) * (drive.EngineRunning || drive.Rpm > 0.0 ? 1.0 : StoppedDragFraction);
            thrust -= cd * 0.5 * airDensity * Math.Max(0, bodyVelocity.X) * Math.Max(0, bodyVelocity.X) * discArea;
        }

        Vec3 force = new(thrust, 0, 0);
        Vec3 moment = Vec3.Zero;

        // Thrust-line pitch moment (ThrustLineZ + is below CG -> nose-up with power).
        moment += new Vec3(0, thrust * prop.ThrustLineZ, 0);

        // Torque reaction (RH prop rolls the aircraft LEFT).
        double torque = omegaProp > 1 ? powerW / omegaProp : 0;
        moment += new Vec3(-prop.RotationSign * torque, 0, 0);
        parts.Thrust = thrust; parts.TorqueRoll = -prop.RotationSign * torque;

        // P-factor: thrust center offset toward the down-going blade. A powered disc's asymmetry: a dead engine's prop
        // (windmilling or stopped) has none to speak of.
        if (v > 1 && drive.EngineRunning)
        {
            double alpha = Math.Atan2(bodyVelocity.Z, Math.Max(bodyVelocity.X, 1.0));
            double beta = Math.Asin(Math.Clamp(bodyVelocity.Y / v, -1, 1));
            double yOffset = prop.RotationSign * prop.PFactorK * radius * Math.Sin(alpha);   // + = right
            double zOffset = -prop.RotationSign * prop.PFactorK * radius * Math.Sin(beta);
            moment += new Vec3(0, thrust * zOffset, -thrust * yOffset);
            parts.PFactorY = yOffset; parts.PFactorZ = zOffset;                       // r x F, r=(0,y,z)... N = -y*T? sign: offset right (+y) with thrust +x: Mz = -y*Fx -> yaw LEFT (negative) — correct for RH at +alpha
        }

        // Spiral slipstream: swirl strikes the fin -> yaw LEFT for RH prop; ~ P / V_slip where V_slip is the
        // speed of the air IN the slipstream (momentum theory: V_slip² = V² + 2T/(ρA)). Using freestream V
        // clamped at 8 m/s over-predicted the static yaw ~4× (a 172 at full power on the brakes needed
        // more rudder than it has) — at rest the fin sits in ~35-40 m/s of slipstream, not 8.
        double vSlip = Math.Sqrt(v * v + 2.0 * thrust / Math.Max(airDensity * discArea, 1e-3));
        parts.SlipYaw = -prop.RotationSign * prop.SlipstreamK * powerW / Math.Max(vSlip, 8.0);
        moment += new Vec3(0, 0, parts.SlipYaw);

        parts.Gyro = GyroMomentAt(prop, rpm, bodyRates);
        moment += parts.Gyro;

        return (force, moment);
    }

    /// <summary>Gyroscopic: M = -omega x h, h = Ip*OmegaProp along +x*sign (part of Compute's moment; on its own for the
    /// widget, which shows the prop's share of a spin's nose-up moment — Ty 2026-10-13).</summary>
    public static Vec3 GyroMoment(PropulsionConfig prop, double throttle01, Vec3 bodyRates) =>
        GyroMomentAt(prop, RunningRpm(prop, throttle01), bodyRates);

    /// <summary>The same at the prop's real speed: a stopped prop has no angular momentum, a windmilling one some.</summary>
    public static Vec3 GyroMoment(PropulsionConfig prop, double throttle01, Vec3 bodyRates, PropDrive drive) =>
        drive.EngineRunning ? GyroMomentAt(prop, RunningRpm(prop, throttle01, drive.PropLever), bodyRates) : GyroMomentAt(prop, drive.Rpm, bodyRates);

    /// <summary>
    /// The propeller as a propeller: every force and moment from one blade-element solution at the rpm the shaft is
    /// really turning. See <see cref="FlyingGame.Core.BladeElement"/> for the model and the bench it was checked on.
    /// </summary>
    /// <summary>
    /// THE DRIVE AT A STEADY CONDITION: the rpm and blade angle the engine and the propeller agree on at this speed,
    /// throttle and density, with no transient. In flight the rpm is a state that Aircraft integrates against the
    /// propeller's inertia; a rig, a trim or a probe asks instead for where that state would settle, and every one of
    /// them was deriving it again from the same five lines. Derived twice it drifts, and that drift is invisible: the
    /// rig quietly solves one aeroplane and the sim flies another.
    /// </summary>
    public static PropDrive SteadyDrive(PropulsionConfig? prop, double throttle01, double vAxialMs, double rho)
    {
        PropDrive drive = PropDrive.Running;
        if (prop is not { PropDiameterM: > 0 }) return drive;
        double pressureRatio = rho / Atmosphere.SeaLevelDensityKgM3;
        if (prop.ConstantSpeed && prop.FineStopDeg > 0.0)
        {
            var stops = new BladeElement.Stops(prop.FineStopDeg, prop.CoarseStopDeg);
            var gov = PistonEngine.Govern(prop.PropBladeCount, 0.5 * prop.PropDiameterM, prop.ActivityFactor, 0.15, stops,
                RunningRpm(prop, throttle01, 1.0), prop.MaxPowerW, prop.MaxRpm, prop.IdleRpm, throttle01, pressureRatio,
                vAxialMs, rho);
            return drive.WithShaft(gov.Rpm, gov.PitchDeg);
        }
        if (prop.BladePitch75Deg > 0.0)
        {
            var g = new BladeElement.Geometry(prop.PropBladeCount, 0.5 * prop.PropDiameterM, prop.BladePitch75Deg, prop.ActivityFactor);
            return drive.WithShaft(PistonEngine.BalancedRpm(g, prop.MaxPowerW, prop.MaxRpm, prop.IdleRpm, throttle01,
                                                            pressureRatio, vAxialMs, rho, pressureRatio));
        }
        return drive;
    }

    /// <summary>
    /// IS THE PROPELLER SOLVED, blade by blade, rather than estimated from coefficients? One predicate, because more
    /// than one place has to agree with it and they disagreed once already: AeroModel decides on it whether the FIN
    /// stands in the slipstream's swirl (a solved propeller makes the spiral yaw out of geometry) or stays out of it
    /// (an estimated one makes the same yaw as a bulk moment, <see cref="Parts.SlipYaw"/>, so the fin in the swirl
    /// would count it twice). Asked of the CONFIG alone it gave the wrong answer for all three constant-speed types,
    /// whose blade angle is not in the config at all — it is a STATE between the stops, governed, and it arrives here
    /// on the drive. Asking it of both is what makes the two places agree.
    /// </summary>
    public static bool IsSolved(PropulsionConfig prop, PropDrive drive)
        => prop.PropDiameterM > 0.0 && drive.EngineRunning && drive.ShaftRpm > 0.0
           && (prop.BladePitch75Deg > 0.0 || drive.BladePitchDeg > 0.0);

    private static (Vec3 Force, Vec3 Moment) BladeElementForces(PropulsionConfig prop, Vec3 bodyVelocity, Vec3 bodyRates,
                                                                double airDensity, PropDrive drive, ref Parts parts,
                                                                BladeElement.SwirlProfile? profile = null)
    {
        double rpm = drive.ShaftRpm;
        double radius = 0.5 * prop.PropDiameterM;
        double vAxial = bodyVelocity.X;
        double vCross = Math.Sqrt(bodyVelocity.Y * bodyVelocity.Y + bodyVelocity.Z * bodyVelocity.Z);
        double pitch = drive.BladePitchDeg > 0.0 ? drive.BladePitchDeg : prop.BladePitch75Deg;
        var g = new BladeElement.Geometry(prop.PropBladeCount, radius, pitch, prop.ActivityFactor);
        var r = BladeElement.Solve(g, vAxial, vCross, rpm, airDensity, profile?.WakeRadiusM, profile?.SwirlMs);
        if (profile is not null)
        {
            profile.Count = BladeElement.ProfileCount;
            profile.Any = r.ThrustN > 0.0;
        }

        Vec3 force = new(r.ThrustN, 0, 0);
        Vec3 moment = new(0, r.ThrustN * prop.ThrustLineZ, 0);

        // Torque reaction: a right-hand prop rolls the aeroplane left, and the torque is now the blade's own.
        moment += new Vec3(-prop.RotationSign * r.TorqueNm, 0, 0);
        parts.Thrust = r.ThrustN; parts.TorqueRoll = -prop.RotationSign * r.TorqueNm;

        // THE IN-PLANE FORCE AND P-FACTOR, which are the same azimuthal integral and no longer two coefficients. The
        // disc's thrust centre moves toward the down-going blade, and the disc makes a force in its own plane; both
        // act on the cross-stream direction the aeroplane is actually meeting the air from.
        if (vCross > 1e-6 && (r.NormalN != 0.0 || r.ThrustMomentNm != 0.0))
        {
            double uy = bodyVelocity.Y / vCross, uz = bodyVelocity.Z / vCross;

            // THE IN-PLANE FORCE acts at the DISC, and the disc is a long way in front of the aeroplane. Its moment
            // was taken here as (force) x (one propeller RADIUS), which is not an arm that exists: the real arm is the
            // hub's distance from the CG, about 1.7 m on a J-3 against a 0.915 m radius, so the moment it makes was
            // little more than half of it. Worse, nothing downstream made up the difference — the caller adds
            // Cross(propArm, ...) only for the legacy coefficient force, which is off everywhere. So the force is
            // handed out in parts.InPlane and Aircraft.PropulsionForces places it on the real arm, exactly as it
            // already does for PropModel.NormalForce.
            Vec3 inPlane = new(0, prop.RotationSign * r.NormalN * uz, -prop.RotationSign * r.NormalN * uy);
            force += inPlane;
            parts.InPlane = inPlane;

            // P-FACTOR'S MOMENT, now measured. The thrust centre's offset toward the down-going blade is the disc's
            // first moment of thrust over its thrust, out of the same azimuthal integral — not (in-plane force x
            // radius), which was a stand-in with no derivation behind it.
            double offset = r.ThrustN > 1e-6 ? r.ThrustMomentNm / r.ThrustN : 0.0;
            double yOffset = prop.RotationSign * offset * uz;     // + = right
            double zOffset = -prop.RotationSign * offset * uy;
            moment += new Vec3(0, r.ThrustN * zOffset, -r.ThrustN * yOffset);
            parts.PFactorY = yOffset; parts.PFactorZ = zOffset;
        }

        parts.Gyro = GyroMomentAt(prop, rpm, bodyRates);
        moment += parts.Gyro;
        parts.SwirlC = r.SwirlC;
        return (force, moment);
    }

    private static Vec3 GyroMomentAt(PropulsionConfig prop, double rpm, Vec3 bodyRates)
    {
        if (prop.PropDiameterM <= 0.0) return Vec3.Zero;
        double h = prop.RotationSign * prop.PropInertia * rpm * 2.0 * Math.PI / 60.0;
        return new Vec3(0, -bodyRates.Z * h, bodyRates.Y * h);
    }
}

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

    /// <summary>
    /// WHERE A CONSTANT-SPEED GOVERNOR BOTTOMS with the blue lever full back, rpm. 1,200 across the fleet, from Ty
    /// on 2026-11-10: "Governor low stops 1600 across the board", then "Actually 1200 rpm across the board".
    ///
    /// It replaces a FRACTION of max rpm (0.7, so 1,890 on a 2,700 Lycoming), which was wrong in a way worth
    /// recording: a governor's low stop is set by the unit and its spring, not by how fast the engine it happens to
    /// be bolted to can turn. Scaling it with MaxRpm made a big engine's governor bottom higher than a small one's
    /// for no reason anybody could point at.
    /// </summary>
    public const double FleetLowGovernedRpm = 1200.0;

    /// <summary>Where this type's governor bottoms: its own authored stop if it has one, else the fleet's 1,200 —
    /// never below idle, and never above what the governor can hold.</summary>
    public static double LowGovernedRpm(PropulsionConfig prop) =>
        System.Math.Clamp(prop.GovernorLowRpm > 0.0 ? prop.GovernorLowRpm : FleetLowGovernedRpm,
                          prop.IdleRpm, System.Math.Clamp(prop.GovernedRpm, prop.IdleRpm, prop.MaxRpm));

    /// <summary>
    /// WHERE THE BLADES GO WHEN THE OIL PRESSURE GOES, degrees at 0.75R, or 0 for a type this cannot happen to (fixed
    /// pitch, or a constant-speed type whose stops are not fitted yet). Ty 2026-11-10: "aerobatic aircraft with a
    /// constant speed propeller default to high pitch with loss of oil pressure like twins do (Seminole beech 18 etc)
    /// and standard single engines default to low pitch with no oil pressure".
    ///
    /// The governor's oil works against a spring, and WHICH WAY it is plumbed is the whole difference. A standard
    /// single lets the oil hold the blades coarse, so losing it springs them FINE: you keep the power you need at the
    /// low end. An aerobatic single and every twin are plumbed the other way — the oil holds them fine, so losing it
    /// drives them COARSE, toward feather on a twin and away from an overspeed on an aerobatic single pointed down.
    /// </summary>
    public static double OilLossPitchDeg(PropulsionConfig prop)
    {
        if (!prop.ConstantSpeed || prop.FineStopDeg <= 0.0) return 0.0;
        double fine = prop.FineStopDeg;
        double coarse = prop.CoarseStopDeg > fine ? prop.CoarseStopDeg : fine;
        return prop.CoarseOnOilLoss ? coarse : fine;
    }

    /// <summary>
    /// A DEAD ENGINE'S PROPELLER, SOLVED: the rpm the air turns it at against the engine's cranking resistance, and the
    /// drag (N, positive aft) it makes doing it. NaN for a type this does not apply to — fixed pitch, or a
    /// constant-speed type whose stops are not fitted yet — which keeps the authored
    /// <see cref="PropulsionConfig.IdleDragCd"/> and <see cref="FlyingGame.Core.Engine.MixtureModel.WindmillRpm"/>
    /// exactly as they were.
    ///
    /// THIS IS THE PHYSICS, AND IT ONLY BECAME TRUSTWORTHY WHEN TY CORRECTED IT. Solved against a free-turning engine
    /// it gave the Pitts 18 N coarse at 60 m/s against an authored 509 — a factor of 28 — and I would not put that
    /// against a coefficient fitted to published glide ratios. Shown the dead propeller running away to 4,414 rpm he
    /// said: "You are not accounting for the cranking resistance of the engine". With
    /// <see cref="PistonEngine.MotoringTorqueNm"/> in the balance the same solve lands where the calibration lives:
    ///
    ///     Pitts, FINE stop (11 deg)          Pitts, COARSE stop (33 deg)
    ///       30 m/s    STOPPED                  30 m/s    STOPPED
    ///       38.4    1,671 rpm  Cd 0.198        38.4      STOPPED
    ///       45      2,167      Cd 0.142        45        466 rpm  Cd 0.085
    ///       60      3,113      Cd 0.089        60        934      Cd 0.041
    ///       80      4,280      Cd 0.063        80      1,374      Cd 0.019
    ///
    /// The fine stop at APPROACH speed — 38.4 m/s, 1.3 Vso — is Cd 0.198 on disc area, and the authored coefficient's
    /// own note says a fine-pitch prop driven by the air is "C_D ~ 0.15-0.25 on disc area". The model and the
    /// calibration agree, from opposite directions, which is the only evidence worth having, and all five types this
    /// applies to land in the band at their own approach speeds (0.153 to 0.223 — OilLossPitchTests measures it).
    ///
    /// AND AT 30 M/S BOTH STOPS ARE STOPPED, which was not true before 2026-11-10's second pass and is the whole point
    /// of it: 30 m/s is the Pitts' stall speed, and Ty's anchor is that a dead propeller stops right there. The dry
    /// boundary friction is solved from that (PistonEngine.BoundaryExcessFor), so this table is now the SAME
    /// calibration read at a different speed rather than a second one. The coarse stop gives up first, by about 40 m/s,
    /// because it turns a third as fast and sits deeper in the boundary regime — the end of the road Ty pointed down
    /// when told the two stops differed by only a quarter: "Much more - nearer feather".
    ///
    /// The drag falls with speed as a coefficient because the engine's cranking torque is nearly constant, so the power
    /// the propeller must extract is nearly constant and the FORCE therefore grows much more slowly than q. That is the
    /// right shape for a windmilling propeller and no authored coefficient on q could have it. What a coefficient also
    /// could not have is the step at the breakaway: a propeller drags HARDEST just before it gives up and a stopped
    /// disc drags much less, so the drag jumps down as the speed falls through the stop's own breakaway — on the Pitts'
    /// coarse stop, 326 N windmilling at 45 m/s against 208 N stopped at 38.
    ///
    /// Cached: the solve is up to thirty disc solutions and a glide asks every step. The buckets (0.25 deg, 1 m/s, 2 %
    /// of density) are finer than anything downstream resolves.
    /// </summary>
    public static (double Rpm, double DragN) Windmilling(PropulsionConfig prop, double vAxialMs, double rhoKgM3)
    {
        double pitch = OilLossPitchDeg(prop);
        if (pitch <= 0.0 || prop.PropDiameterM <= 0.0 || vAxialMs <= 1.0) return (double.NaN, double.NaN);

        // THE ENGINE IS PART OF THE KEY, not just the blade. It always should have been — the balance is against the
        // engine's cranking torque — and it matters now that the dry boundary friction is solved per engine
        // (PistonEngine.BoundaryExcessFor): two types with the same propeller and different engines are different
        // answers, and without the rated power and the excess in the key the first one asked would answer for both.
        var key = ((long)Math.Round(pitch * 4.0), (long)Math.Round(vAxialMs), (long)Math.Round(rhoKgM3 * 50.0),
                   prop.PropBladeCount, (long)Math.Round(prop.PropDiameterM * 100.0), (long)Math.Round(prop.ActivityFactor),
                   (long)Math.Round(prop.MaxPowerW), (long)Math.Round(prop.MaxRpm),
                   (long)Math.Round(PistonEngine.BoundaryExcessFor(prop) * 1000.0));
        lock (_windmillCache)
        {
            if (_windmillCache.TryGetValue(key, out (double Rpm, double DragN) cached)) return cached;
            var g = new BladeElement.Geometry(prop.PropBladeCount, 0.5 * prop.PropDiameterM, pitch, prop.ActivityFactor);
            double sigma = rhoKgM3 / Atmosphere.SeaLevelDensityKgM3;
            double EngineQ(double r) => PistonEngine.MotoringTorqueNm(prop, r, 0.0, sigma, sigma);
            BladeElement.Windmilling w = BladeElement.Windmill(g, vAxialMs, rhoKgM3, prop.MaxRpm, EngineQ);
            // A blade too coarse to turn the engine over at all is STOPPED, and a stopped disc is not a blade-element
            // problem: NaN hands it back to the authored coefficient, halved, which is what the sim has always done.
            (double, double) r = w.Turning ? (w.Rpm, Math.Max(0.0, w.DragN)) : (double.NaN, double.NaN);
            if (_windmillCache.Count < 4096) _windmillCache[key] = r;
            return r;
        }
    }

    /// <summary>The rpm the air turns a dead propeller at; NaN when that is not this type's story. See
    /// <see cref="Windmilling"/>, which this reads.</summary>
    public static double WindmillRpm(PropulsionConfig prop, double vAxialMs, double rhoKgM3)
        => Windmilling(prop, vAxialMs, rhoKgM3).Rpm;

    private static readonly Dictionary<(long, long, long, int, long, long, long, long, long), (double Rpm, double DragN)> _windmillCache = new();

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
        double low = LowGovernedRpm(prop);
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
        // SimProfile only, so the propeller's share of a step is a measured number and not an argument (Ty 2026-11-09:
        // "the j3 is noticibly slower"). Off, Begin() is one thread-static read; the body moved to ComputeInner rather
        // than wrapping every return in a try/finally, which would have cost an EH frame in the hot path for nothing.
        long tProp = SimProfile.Begin();
        (Vec3 Force, Vec3 Moment) r = ComputeInner(prop, throttle01, bodyVelocity, bodyRates, airDensity, drive, out parts, profile);
        SimProfile.End(SimProfile.Scope.Prop, tProp);
        return r;
    }

    private static (Vec3 Force, Vec3 Moment) ComputeInner(
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
            // A DEAD CONSTANT-SPEED PROP IS SOLVED, not estimated: its blades are on the stop the oil loss left them
            // on and the blade element knows what turning the engine with them costs. Only this case — a running
            // engine, a fixed-pitch propeller and a type whose stops are not fitted all keep the authored coefficient
            // untouched, including the idling prop the coefficient was calibrated on.
            double solved = drive.EngineRunning ? double.NaN : Windmilling(prop, bodyVelocity.X, airDensity).DragN;
            if (!double.IsNaN(solved)) thrust -= solved;
            else
            {
                double cd = prop.IdleDragCd * (1 - thr / 0.2) * (drive.EngineRunning || drive.Rpm > 0.0 ? 1.0 : StoppedDragFraction);
                thrust -= cd * 0.5 * airDensity * Math.Max(0, bodyVelocity.X) * Math.Max(0, bodyVelocity.X) * discArea;
            }
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

    /// <summary>
    /// Ablation switches for the SOLVED disc's two cross-flow terms, which <see cref="PropulsionConfig.PFactorK"/>
    /// does NOT reach: on a blade-element prop the in-plane force and the P-factor offset come out of the azimuthal
    /// integral, so zeroing PFactorK (the legacy coefficient) ablates nothing at all on the Pitts, the Extra or any
    /// other solved type. PROP_NOINPLANE=1 / PROP_NOPFACTOR=1. Diagnostics only.
    /// </summary>
    public static bool NoDiscInPlaneForDiagnostics = Environment.GetEnvironmentVariable("PROP_NOINPLANE") != null;
    public static bool NoDiscPFactorForDiagnostics = Environment.GetEnvironmentVariable("PROP_NOPFACTOR") != null;

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
            // THE DISC'S NORMAL FORCE ACTS ALONG THE CROSS-FLOW, not across it. Written as (uz, -uy) it was rotated a
            // quarter turn, so a pure SIDESLIP produced a vertical force and a pure angle of attack produced a lateral
            // one: at 40 deg of sideslip the Extra's disc pushed 496 N straight DOWN and nothing sideways.
            //
            // The blade integral says the same thing. With azimuth 0 at the right-hand blade going down, an element's
            // tangential reaction is (0, sin psi, -cos psi) * ct * q, and summing that over the azimuth leaves only
            // (0, 0, -sum(ct cos psi)) for a cross-flow in +z - along the cross-flow axis, which is where NormalN's
            // own integral already points. It also has to agree with the coefficient model this path replaced
            // (PropModel.NormalForce returns -k * (0, v.Y, v.Z)): at positive alpha the disc's force is UP, and a disc
            // 1.7 m ahead of the CG makes that destabilising. That is the term the gyro derivation says is ten times
            // the gyroscopic couple (docs/aircraft/prop-gyro-DERIVATION.md section 5) - and on the solved types it was
            // pointing the wrong way entirely, which is what Ty felt as the Extra "coming up way short on side slip
            // angle (beta) when yawed left and pushed forward... like the gyroscopics are too light".
            Vec3 inPlane = NoDiscInPlaneForDiagnostics ? Vec3.Zero : new(0, -r.NormalN * uy, -r.NormalN * uz);
            force += inPlane;
            parts.InPlane = inPlane;

            // P-FACTOR'S MOMENT, now measured. The thrust centre's offset toward the down-going blade is the disc's
            // first moment of thrust over its thrust, out of the same azimuthal integral — not (in-plane force x
            // radius), which was a stand-in with no derivation behind it.
            double offset = r.ThrustN > 1e-6 ? r.ThrustMomentNm / r.ThrustN : 0.0;
            double yOffset = prop.RotationSign * offset * uz;     // + = right
            double zOffset = -prop.RotationSign * offset * uy;
            if (!NoDiscPFactorForDiagnostics) moment += new Vec3(0, r.ThrustN * zOffset, -r.ThrustN * yOffset);
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

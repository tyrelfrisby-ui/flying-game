using System;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// THE ENGINE, SO THE RPM CAN BE SOLVED INSTEAD OF ASSUMED (Phase 1b). The blade-element propeller
/// (<see cref="BladeElement"/>) answers "what does this blade do at this rpm and airspeed", and the answer includes
/// the torque it demands. Something has to decide what the rpm actually IS, and on a real aeroplane that is a torque
/// balance: the engine makes a torque at a given rpm, throttle and altitude, the propeller absorbs one, and the rpm
/// settles where they are equal. The old model ran the other way — power in, thrust out — which is exactly why it
/// needed an efficiency constant to close the loop.
///
/// This matters for how an aeroplane FEELS, not just for its numbers:
///   - a fixed-pitch aeroplane cannot make rated rpm standing still. Static rpm is well below redline and climbs as
///     it accelerates, which is why a run-up is a check and not a formality;
///   - a constant-speed aeroplane holds its rpm until the governor runs out of blade, and then does not: fine stop
///     in the climb and the rpm sags, coarse stop in the dive and it overspeeds. That is what the pitch stops are
///     FOR (see BladeElement.GovernorStops).
///
/// POWER AGAINST MANIFOLD PRESSURE AND RPM. A naturally aspirated piston engine's indicated power is very nearly
/// proportional to the air it swallows, which is manifold pressure times rpm, less the friction it spends on itself:
///
///     P(MP, rpm) = P_rated * (MP - MP_friction)/(MP_sea - MP_friction) * (rpm / rpm_rated)
///
/// with MP_friction near 10 inHg. That gives the shape every piston engine has — roughly constant torque, power
/// rising with rpm — and it is the same relation BladeElement.GovernorStops uses for the 26 inHg rule, so the two
/// cannot drift apart. Manifold pressure itself is ambient pressure times the throttle, so altitude costs power
/// through the air's own density with no separate lapse constant.
/// </summary>
public static class PistonEngine
{
    public const double FrictionManifoldInHg = BladeElement.FrictionManifoldInHg;
    public const double SeaLevelManifoldInHg = BladeElement.SeaLevelManifoldInHg;

    /// <summary>Manifold pressure a naturally aspirated engine can draw: ambient, throttled. <paramref name="pressureRatio"/>
    /// is the ambient pressure over sea level (0.69 at 10 000 ft), and idle is never a fully closed throttle.</summary>
    public static double ManifoldInHg(double throttle01, double pressureRatio, double idleFraction = 0.28)
    {
        double t = Math.Clamp(throttle01, 0.0, 1.0);
        double open = idleFraction + (1.0 - idleFraction) * t;
        return SeaLevelManifoldInHg * Math.Clamp(pressureRatio, 0.05, 1.3) * open;
    }

    /// <summary>
    /// ALTITUDE LAPSE, GAGG-FERRAR. Ty 2026-11-09: "high altitude actually has the opposite effect. while it is true
    /// that the engine makes less power the air is thinner and creates less drag for the propeller blade too. the two
    /// roughly cancel eachother out but the pilot infers higher rpm per airspeed at higher altitude because the prop
    /// cares about true airspeed not indicated airspeed."
    ///
    /// He is right and the first version of this was wrong. Running the throttle's manifold-pressure line straight
    /// through its friction intercept AND using it for altitude made height far too expensive — 53 % of rated at
    /// 10 000 ft, where the standard figure is near 70. Propeller torque falls as DENSITY (sigma = 0.738 there), so
    /// an engine losing 47 % against a blade losing 26 % drags the rpm down 10 % at constant true airspeed, when the
    /// two should very nearly cancel.
    ///
    /// The classical relation for a naturally aspirated engine is Gagg and Ferrar's, and it is in density ratio:
    ///
    ///     P / P_rated = (sigma - 0.117) / 0.883
    ///
    /// which gives 0.703 at 10 000 ft against the blade's 0.738 — the propeller unloads slightly MORE than the engine
    /// loses, so rpm at a given TRUE airspeed is near enough unchanged, and at a given INDICATED airspeed (where the
    /// true airspeed is higher) it RISES. That is the effect a pilot actually sees.
    ///
    /// Throttling is a separate thing and keeps the manifold-pressure line: at a given altitude the power is
    /// proportional to what the engine can draw, from the friction intercept up to whatever full throttle gives
    /// THERE. So the 26 inHg run-up rule (BladeElement.GovernorStops) is untouched — it is a sea-level, part-throttle
    /// condition — while the altitude behaviour is now the measured one.
    /// </summary>
    public static double AltitudePowerFraction(double densityRatio)
        => Math.Clamp((Math.Clamp(densityRatio, 0.05, 1.3) - 0.117) / 0.883, 0.0, 1.2);

    /// <summary>Shaft power, W. <paramref name="densityRatio"/> defaults to the pressure ratio when not supplied,
    /// which is close enough in ISA and keeps older callers working.</summary>
    public static double PowerW(double ratedPowerW, double ratedRpm, double rpm, double throttle01, double pressureRatio,
                                double densityRatio = 0.0)
    {
        if (ratedRpm <= 0 || rpm <= 0) return 0.0;
        double sigma = densityRatio > 0 ? densityRatio : pressureRatio;
        double mpFull = ManifoldInHg(1.0, pressureRatio);
        double mp = ManifoldInHg(throttle01, pressureRatio);
        // How far the throttle is open, from the friction intercept to what full throttle gives at THIS altitude.
        double span = Math.Max(1e-6, mpFull - FrictionManifoldInHg);
        double openFraction = Math.Clamp((mp - FrictionManifoldInHg) / span, 0.0, 1.0);
        return ratedPowerW * AltitudePowerFraction(sigma) * openFraction * (rpm / ratedRpm);
    }

    /// <summary>
    /// CRANKING RESISTANCE: the torque it takes to TURN a piston engine that is not running, N·m, returned negative
    /// because it opposes whatever is turning it. Ty 2026-11-10, shown a dead propeller windmilling to 4,414 rpm on a
    /// 2,700 rpm engine: "You are not accounting for the cranking resistance of the engine". He is right, and the model
    /// already knew the number — it was being thrown away.
    ///
    /// <see cref="PowerW"/> clamps its open fraction to [0, 1]. But the fraction is
    /// (MP − MP_friction) / (MP_sea − MP_friction), and a closed throttle draws 0.28 of ambient, which at sea level is
    /// 8.4 inHg — BELOW the 10 inHg friction intercept. So the engine's own law says it absorbs about 8 % of rated
    /// power at rated rpm with the throttle shut, and the lower clamp was discarding exactly that. This is the same
    /// expression with the clamp lifted below zero, so there is no new constant and nothing to tune: the pumping and
    /// friction come from the friction intercept that is already in the power curve and in BladeElement.GovernorStops'
    /// 26 inHg rule.
    ///
    /// For the Pitts' 260 hp at 2,700 rpm that is about −56 N·m, which is the order a 540 cubic inch engine really
    /// takes to turn over, and it is what keeps a dead propeller's rpm down to something an aeroplane survives.
    ///
    /// DELIBERATELY NOT FOLDED INTO <see cref="TorqueNm"/> or <see cref="PowerW"/>, though the comment on TorqueNm has
    /// always promised it. Those two also feed the THRUST path, and the drag of an idling or windmilling propeller is
    /// already carried there by PropulsionConfig.IdleDragCd — a coefficient calibrated against published glide ratios
    /// (172 8.9:1 on a book 9:1, Cub 8:1, SR22 10:1 on 8.8:1). Letting the power go negative there would count the same
    /// drag twice and break a calibration that is right. So this belongs only where the RPM is solved from a torque
    /// balance: <see cref="BladeElement.Windmill"/> and the dead-engine branch of the shaft integration.
    /// </summary>
    public static double MotoringTorqueNm(double ratedPowerW, double ratedRpm, double rpm, double throttle01,
                                          double pressureRatio, double densityRatio = 0.0, double boundaryExcess = -1.0)
    {
        if (ratedRpm <= 0.0 || rpm <= 1.0) return 0.0;
        double sigma = densityRatio > 0 ? densityRatio : pressureRatio;
        double mpFull = ManifoldInHg(1.0, pressureRatio);
        double mp = ManifoldInHg(throttle01, pressureRatio);
        double span = Math.Max(1e-6, mpFull - FrictionManifoldInHg);
        double openFraction = (mp - FrictionManifoldInHg) / span;      // the SAME expression, lower clamp lifted
        if (openFraction >= 0.0) return 0.0;                           // open enough to make power: not this function's business
        double powerW = ratedPowerW * AltitudePowerFraction(sigma) * openFraction * (rpm / ratedRpm);
        return powerW / (rpm * 2.0 * Math.PI / 60.0) * BoundaryFrictionFactor(rpm, boundaryExcess);
    }

    /// <summary>The same thing for a configured engine, which is the form every caller in the sim wants: it carries its
    /// own <see cref="PropulsionConfig.DryBoundaryExcess"/> where one has been solved for it.</summary>
    public static double MotoringTorqueNm(PropulsionConfig prop, double rpm, double throttle01,
                                          double pressureRatio, double densityRatio = 0.0)
        => MotoringTorqueNm(prop.MaxPowerW, prop.MaxRpm, rpm, throttle01, pressureRatio, densityRatio,
                            BoundaryExcessFor(prop));

    /// <summary>
    /// BOUNDARY FRICTION, which is why a dead propeller stops so much earlier than the running friction alone says.
    /// The friction intercept in the power curve is the friction of an engine that is RUNNING — oil pump turning, a
    /// hydrodynamic film in every bearing, friction roughly proportional to speed so the torque is near constant. An
    /// engine with no oil pressure has no film: the bearings and the rings are in boundary contact, and boundary
    /// friction is several times hydrodynamic. That is a coincidence worth noticing — the one case this whole
    /// mechanism is about, oil pressure lost, is exactly the case where it bites hardest.
    ///
    /// THE SHAPE WAS THE BUG, NOT THE AMPLITUDE (2026-11-10, second pass). The first version of this decayed as
    /// exp(−rpm/300), so the whole effect lived below 300 rpm — and the measurement says that is nowhere near where a
    /// propeller actually gives up. Seeded from a windmill and slowed down, these types quit between about 650 and
    /// 1,500 rpm, where an exp(−rpm/300) tail has already shrunk the excess by a factor of 9 to 140. That is why Ty's
    /// anchor — "Cranking resistance should be calibrated such that at fine pitch the prop stops right at level flight
    /// stall speed" — asked for amplitudes of 7 to 40 times the running friction, far outside anything tribology
    /// allows, and STILL could not be met with one number: the exponent, not the amplitude, was carrying a 25-fold
    /// spread, and it was carrying it in exactly the rpm band the windmilling drag is calibrated in.
    ///
    /// A STRIBECK ROLL-OFF IS THE RIGHT SHAPE, AND IT IS NOT A CURVE I PICKED. Boundary friction is not a spike at
    /// zero speed; it is a PLATEAU that holds while the surfaces are in contact and falls away once sliding speed
    /// builds a film:
    ///
    ///     factor(rpm) = 1 + excess / (1 + (rpm / BoundaryRpmScale)^2)
    ///
    /// flat at low rpm, half gone at the scale, and down to a sixteenth of itself by rated rpm.
    ///
    /// <see cref="BoundaryRpmScale"/> = 700 rpm IS A RUBBING SPEED, which is the variable a Stribeck transition
    /// actually lives in. The boundary-to-mixed transition for a piston ring sits near 2.5 m/s of mean piston speed,
    /// and mean piston speed is 2·S·N/60, so on this fleet's strokes — a Continental C-85 is 3.875 in = 0.098 m, a
    /// Lycoming 540 is 4.375 in = 0.111 m — 2.5 m/s is 680 to 765 rpm. ONE number for the fleet because the fleet's
    /// strokes are all within 12 % of each other; made per type it would move the answer a few per cent, and the
    /// displacement it would need is authored on six of the twenty-seven propeller types.
    ///
    /// <see cref="DryBoundaryExcess"/> = 3.0 is the FLEET amplitude: a dry engine resisting 4.0x its running friction
    /// at rest. It is the geometric mean of what the five types this mechanism is live on each ask for (2.19 to 4.71,
    /// each solved on its own against Ty's stall-speed anchor; the mean is 3.07 and 3.0 is it to the precision the
    /// measurement has), and it sits inside what the tribology allows — a
    /// boundary friction coefficient is one to two orders of magnitude above a hydrodynamic one at the contact, but
    /// only the film-dependent part of an engine's loss changes, so a cold unlubricated engine's motoring torque comes
    /// out a few times the hot running figure and not a hundred. Types that have been solved carry their own number in
    /// <see cref="PropulsionConfig.DryBoundaryExcess"/>; this is what every other type uses.
    ///
    /// At 700 rpm the excess is half gone and by 4,300 rpm a fortieth, so a fine blade still runs away past the red
    /// line in a dive — which is what Ty saw, and the other half of why the aerobatic installation is plumbed coarse.
    /// What it does NOT leave alone is the windmilling drag at approach speed, and it must not: see
    /// <see cref="PropModel.Windmilling"/> for the measured coefficients and the band they sit in.
    /// </summary>
    public const double DryBoundaryExcess = 3.0, BoundaryRpmScale = 700.0;

    /// <summary>The multiplier on the running friction at this rpm for an engine whose dry excess at rest is
    /// <paramref name="excess"/>. Negative = the fleet <see cref="DryBoundaryExcess"/>.</summary>
    public static double BoundaryFrictionFactor(double rpm, double excess = -1.0)
    {
        double e = excess < 0.0 ? DryBoundaryExcess : excess;
        double x = Math.Max(0.0, rpm) / BoundaryRpmScale;
        return 1.0 + e / (1.0 + x * x);
    }

    /// <summary>
    /// THIS ENGINE'S dry boundary excess: its own solved value where there is one, the fleet number otherwise. Ty
    /// 2026-11-10, shown that no single constant could put every type's propeller down at its own stall speed: "fix it
    /// per engine".
    ///
    /// AND IT IS PER ENGINE THE WAY <see cref="PistonCompression"/>'s peak is — SOLVED from one of his own anchors,
    /// one equation in one unknown — rather than predicted from a property of the engine, because no property of the
    /// engine predicts it, and I looked. Motoring friction and rated torque BOTH scale with displacement (this fleet's
    /// BMEP runs 139 to 163 psi, a 17 % spread), so every engine-internal measure — displacement, FMEP, bore, ring
    /// circumference, main-bearing count — hands back very nearly the same excess for all of them, and the two that do
    /// vary point the wrong way: per unit of displacement a SMALL cylinder has more ring circumference, and a SHORT
    /// stroke moves the Stribeck transition UP. Cylinder count looked like it split the fleet four-against-six until I
    /// ran the same aeroplane at 4, 6 and 9 cylinders with nothing else touched and the stopping speed moved 2-5 %: the
    /// split was the two 540s being aerobatic, not their cylinder count. What the spread actually tracks is the
    /// AIRFRAME — its stall speed against its Vne, and its propeller's inertia (the two Cubs differ in nothing but the
    /// propeller, and a 6 kg wood blade asks 2.20 where a 20 kg metal one asks 2.85) — and a friction law has no
    /// business reading either of those. So it is solved per type from the type's own stall speed instead of predicted,
    /// and <see cref="PropulsionConfig.DryBoundaryExcessSource"/> records what each one was solved against.
    /// </summary>
    public static double BoundaryExcessFor(PropulsionConfig? prop)
        => prop is { DryBoundaryExcess: > 0.0 } ? prop.DryBoundaryExcess : DryBoundaryExcess;

    /// <summary>Shaft torque, N·m. Negative when the engine is being driven: below its friction manifold pressure it
    /// absorbs power rather than making it, which is what lets a windmilling propeller drag.</summary>
    public static double TorqueNm(double ratedPowerW, double ratedRpm, double rpm, double throttle01, double pressureRatio,
                                  double densityRatio = 0.0)
    {
        if (rpm <= 1.0) return 0.0;
        return PowerW(ratedPowerW, ratedRpm, rpm, throttle01, pressureRatio, densityRatio) / (rpm * 2.0 * Math.PI / 60.0);
    }

    /// <summary>
    /// WHERE A FIXED-PITCH PROPELLER SETTLES: the rpm at which the engine's torque and the blade's are equal. The
    /// propeller's demand climbs steeply with rpm (roughly as rpm squared) while the engine's torque is nearly flat,
    /// so the crossing is single and a bisection finds it.
    /// </summary>
    public static double BalancedRpm(BladeElement.Geometry g, double ratedPowerW, double ratedRpm, double idleRpm,
                                     double throttle01, double pressureRatio, double vAxialMs, double rhoKgM3,
                                     double densityRatio = 0.0)
    {
        BladeElement.Geometry blade = g;   // a lambda cannot close over an 'in' parameter
        double Excess(double rpm) =>
            TorqueNm(ratedPowerW, ratedRpm, rpm, throttle01, pressureRatio, densityRatio)
            - BladeElement.Solve(blade, vAxialMs, 0.0, rpm, rhoKgM3).TorqueNm;

        double lo = Math.Max(200.0, idleRpm * 0.5), hi = ratedRpm * 1.35;
        if (Excess(hi) > 0) return hi;          // the blade cannot hold it back: an overspeed
        if (Excess(lo) < 0) return lo;          // it cannot even turn it
        for (int i = 0; i < 36; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Excess(mid) > 0) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// FIT A FIXED-PITCH BLADE TO ITS AEROPLANE, from the one number every pilot reads on every run-up: the rpm at
    /// full throttle standing still. Ty 2026-11-09: "Use full power static rpm (zero airspeed) as the anchor for
    /// fixed pitch."
    ///
    /// It is the right anchor because it is sharply sensitive to pitch — on a J-3 two degrees of blade is 200 rpm —
    /// it needs no assumption about drag or cruise, and it is a number he has seen rather than one from a book. A
    /// finer blade lets the engine run up; a coarser one holds it down, so static rpm falls monotonically with pitch
    /// over the range any real propeller occupies, and a bisection finds it.
    ///
    /// Past about 18 deg on a light aeroplane that monotonicity fails, because the blade is then stalled standing
    /// still and a stalled blade absorbs LESS torque — the same inboard stall that puts the dip in the rpm-against-
    /// speed curve. The search is therefore bounded to the unstalled range rather than allowed to wander into it.
    /// </summary>
    public static double FitPitchForStaticRpm(int blades, double radiusM, double activityFactor, double hubFraction,
                                              double targetStaticRpm, double ratedPowerW, double ratedRpm, double idleRpm,
                                              double rhoKgM3 = 1.225, double loDeg = 8.0, double hiDeg = 18.0)
    {
        double Static(double pitch) => BalancedRpm(new BladeElement.Geometry(blades, radiusM, pitch, activityFactor, hubFraction),
                                                   ratedPowerW, ratedRpm, idleRpm, 1.0, 1.0, 0.0, rhoKgM3);
        if (Static(loDeg) < targetStaticRpm) return loDeg;      // even the finest blade will not turn that fast
        if (Static(hiDeg) > targetStaticRpm) return hiDeg;      // even the coarsest will not hold it down
        for (int i = 0; i < 30; i++)
        {
            double mid = 0.5 * (loDeg + hiDeg);
            if (Static(mid) > targetStaticRpm) loDeg = mid; else hiDeg = mid;
        }
        return 0.5 * (loDeg + hiDeg);
    }

    /// <summary>What a governed propeller is doing right now.</summary>
    public readonly struct Governed
    {
        public readonly double Rpm, PitchDeg;
        public readonly bool OnFineStop, OnCoarseStop;
        public Governed(double rpm, double pitch, bool fine, bool coarse) { Rpm = rpm; PitchDeg = pitch; OnFineStop = fine; OnCoarseStop = coarse; }
    }

    /// <summary>
    /// A CONSTANT-SPEED PROPELLER. The governor coarsens or fines the blade to hold the commanded rpm, and holds it
    /// exactly — until it runs out of blade. On the FINE stop the propeller cannot absorb the power and the rpm runs
    /// up past the command; on the COARSE stop it cannot absorb it either way round and the rpm falls below. Both
    /// are solved as the fixed-pitch balance at the stop, so hitting a stop is not a special case, it is the same
    /// equation with the pitch no longer free.
    /// </summary>
    public static Governed Govern(int blades, double radiusM, double activityFactor, double hubFraction,
                                  BladeElement.Stops stops, double commandedRpm,
                                  double ratedPowerW, double ratedRpm, double idleRpm,
                                  double throttle01, double pressureRatio, double vAxialMs, double rhoKgM3)
    {
        double qEngine = TorqueNm(ratedPowerW, ratedRpm, commandedRpm, throttle01, pressureRatio);
        double pitch = BladeElement.PitchForTorqueDeg(blades, radiusM, activityFactor, hubFraction, qEngine,
                                                      commandedRpm, vAxialMs, rhoKgM3, stops.FineDeg, stops.CoarseDeg);
        double qAtFine = BladeElement.Solve(new BladeElement.Geometry(blades, radiusM, stops.FineDeg, activityFactor, hubFraction), vAxialMs, 0.0, commandedRpm, rhoKgM3).TorqueNm;
        double qAtCoarse = BladeElement.Solve(new BladeElement.Geometry(blades, radiusM, stops.CoarseDeg, activityFactor, hubFraction), vAxialMs, 0.0, commandedRpm, rhoKgM3).TorqueNm;
        if (qEngine > qAtCoarse)     // even the coarsest blade cannot absorb it: the rpm runs up from the coarse stop
        {
            var g = new BladeElement.Geometry(blades, radiusM, stops.CoarseDeg, activityFactor, hubFraction);
            return new Governed(BalancedRpm(g, ratedPowerW, ratedRpm, idleRpm, throttle01, pressureRatio, vAxialMs, rhoKgM3), stops.CoarseDeg, false, true);
        }
        if (qEngine < qAtFine)       // the finest blade still absorbs more than the engine makes: the rpm sags
        {
            var g = new BladeElement.Geometry(blades, radiusM, stops.FineDeg, activityFactor, hubFraction);
            return new Governed(BalancedRpm(g, ratedPowerW, ratedRpm, idleRpm, throttle01, pressureRatio, vAxialMs, rhoKgM3), stops.FineDeg, true, false);
        }
        return new Governed(commandedRpm, pitch, false, false);
    }
}

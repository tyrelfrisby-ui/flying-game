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

    /// <summary>
    /// THE ENGINE, as the torque law needs to know it. It is a struct and not four loose doubles because the law now
    /// needs the DISPLACEMENT and the CYLINDERS and the COMPRESSION RATIO as well as the rating — friction and pumping
    /// are mean effective PRESSURES, and a pressure only becomes a torque through a swept volume — and because the rig
    /// and the sim solving the same propeller against two different engines is a failure this repository has already
    /// had once (see <see cref="PropModel.SteadyFlownDrive"/>). One struct, built one way, from the config.
    /// </summary>
    public readonly struct Spec
    {
        public readonly double RatedPowerW, RatedRpm, DisplacementM3, CompressionRatio, StrokeM, DryBoundaryExcess;
        public readonly int Cylinders;

        public Spec(double ratedPowerW, double ratedRpm, double displacementM3, int cylinders, double compressionRatio,
                    double dryBoundaryExcess)
        {
            RatedPowerW = ratedPowerW; RatedRpm = ratedRpm;
            Cylinders = Math.Max(1, cylinders);
            CompressionRatio = compressionRatio > 1.0 ? compressionRatio : DefaultCompressionRatio;
            double qRated = ratedRpm > 0.0 ? ratedPowerW / (ratedRpm * 2.0 * Math.PI / 60.0) : 0.0;
            // 0 = not authored: back the swept volume out of rated torque at the fleet's BMEP, which is a definition
            // (BMEP = 4 pi Q / V_d) and not a fit. The same fallback PistonCompression.DisplacementM3 uses.
            DisplacementM3 = displacementM3 > 0.0 ? displacementM3 : 4.0 * Math.PI * qRated / PistonCompression.FleetBmepPa;
            StrokeM = PistonMep.StrokeM(DisplacementM3, Cylinders);
            DryBoundaryExcess = dryBoundaryExcess;
        }

        /// <summary>Four cylinders and 8.7:1 when nothing says otherwise — the same defaults
        /// <see cref="DataContracts.AircraftConfig.EngineCylinders"/> carries, so a loose-parameter caller and a
        /// configured one describe the same engine.</summary>
        public const int DefaultCylinders = 4;
        public const double DefaultCompressionRatio = 8.7;

        /// <summary>A rating with no engine data behind it: the fleet BMEP, four cylinders, 8.7:1. Every caller that
        /// has a config should use <see cref="For"/> instead.</summary>
        public static Spec Rated(double ratedPowerW, double ratedRpm)
            => new Spec(ratedPowerW, ratedRpm, 0.0, DefaultCylinders, DefaultCompressionRatio, -1.0);

        /// <summary>THIS type's engine, which is the form every caller in the sim and the rig should use.</summary>
        public static Spec For(PropulsionConfig p)
            => new Spec(p.MaxPowerW, p.MaxRpm, p.DisplacementCuIn * PistonCompression.CubicInchM3,
                        p.Cylinders > 0 ? p.Cylinders : DefaultCylinders,
                        p.CompressionRatio > 1.0 ? p.CompressionRatio : DefaultCompressionRatio,
                        BoundaryExcessFor(p));

        public double RatedTorqueNm => RatedRpm > 0.0 ? RatedPowerW / (RatedRpm * 2.0 * Math.PI / 60.0) : 0.0;
        /// <summary>Brake mean effective pressure at the rating, Pa: BMEP = 4 pi Q / V_d. This fleet runs 9.6 to
        /// 11.3 bar.</summary>
        public double RatedBmepPa => DisplacementM3 > 0.0 ? 4.0 * Math.PI * RatedTorqueNm / DisplacementM3 : 0.0;
        /// <summary>Gross indicated MEP at the rating, sea level, Pa. Gagg and Ferrar say friction is 11.7 % of it,
        /// so BMEP_rated = IMEP_rated * 0.883 — see <see cref="PistonMep"/>.</summary>
        public double RatedIndicatedMepPa => RatedBmepPa / (1.0 - PistonMep.GaggFerrarFrictionFraction);
        /// <summary>A pressure difference as a shaft torque: Q = MEP * V_d / (4 pi), the four-stroke definition.</summary>
        public double TorquePerPa => DisplacementM3 / (4.0 * Math.PI);
        /// <summary>What the manifold reads at full throttle at rated rpm at sea level, Pa — the reference every
        /// normalisation in the torque law is taken against.</summary>
        public double RatedWideOpenManifoldPa
            => PistonMep.ManifoldPa(1.0, RatedRpm, RatedRpm, Atmosphere.SeaLevelPressurePa);
        /// <summary>FMEP at the rating, Pa. NOT A NEW CONSTANT: Gagg and Ferrar's 0.117 of gross indicated power is a
        /// statement about friction, and this is that statement, less the pumping the induction system already
        /// accounts for at wide-open throttle.</summary>
        public double RatedFrictionMepPa
            => PistonMep.GaggFerrarFrictionFraction * RatedIndicatedMepPa
             - PistonMep.PumpingMepPa(RatedWideOpenManifoldPa, Atmosphere.SeaLevelPressurePa);
    }

    /// <summary>
    /// MANIFOLD PRESSURE, inHg — AND IT DEPENDS ON RPM NOW, which is the term that was missing. A throttle plate is a
    /// restriction and an engine is a pump whose demand goes as its speed, so the same lever reads a healthy manifold
    /// at 700 rpm and a few inches of vacuum at 2,500. See <see cref="PistonMep.ManifoldPa"/> for the orifice balance
    /// and for why friction alone could never have produced engine braking.
    /// </summary>
    public static double ManifoldInHg(double throttle01, double pressureRatio, double rpm, double ratedRpm)
    {
        double pAmb = Atmosphere.SeaLevelPressurePa * Math.Clamp(pressureRatio, 0.05, 1.3);
        return PistonMep.ManifoldPa(throttle01, rpm, ratedRpm, pAmb) / Atmosphere.SeaLevelPressurePa
             * SeaLevelManifoldInHg;
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
    /// SINCE THE FMEP/PMEP MODEL LANDED THIS IS NO LONGER APPLIED AS A FACTOR — it is DERIVED. The 0.117 in it is a
    /// friction statement (friction is 11.7 % of sea-level gross indicated power), and <see cref="Spec"/> reads it
    /// that way to set FMEP at the rating, so the wide-open-throttle lapse falls out of the decomposition instead of
    /// multiplying it. The two agree to 1.4 % at 10,000 ft — the model is slightly the LESS lossy of the two, because
    /// Gagg and Ferrar hold friction and pumping constant with height where the decomposition lets the gas load and
    /// the back pressure fall with the air. This remains here because it is still the right closed form for anything
    /// that wants the lapse without an engine to hand, and <see cref="BladeElement.GovernorStops"/> is one.
    /// </summary>
    public static double AltitudePowerFraction(double densityRatio)
        => Math.Clamp((Math.Clamp(densityRatio, 0.05, 1.3) - 0.117) / 0.883, 0.0, 1.2);

    /// <summary>
    /// SHAFT TORQUE, N.m: gross indicated work, less friction, less pumping, through the swept volume. This is the
    /// whole of the engine model and <see cref="PistonMep"/> is its derivation; what follows is bookkeeping.
    ///
    /// It is NEGATIVE whenever the engine is being driven faster than its throttle can feed it, which is engine
    /// braking and is what the old law — a torque with no rpm in it — could not produce at all.
    /// </summary>
    public static double TorqueNm(in Spec e, double rpm, double throttle01, double pressureRatio, double densityRatio = 0.0)
    {
        if (e.RatedRpm <= 1.0 || rpm <= 1.0 || e.DisplacementM3 <= 0.0) return 0.0;
        double delta = Math.Clamp(pressureRatio, 0.05, 1.3);
        double sigma = densityRatio > 0.0 ? Math.Clamp(densityRatio, 0.05, 1.3) : delta;
        double pAmb = Atmosphere.SeaLevelPressurePa * delta;              // ambient IS the exhaust back pressure
        double mp = PistonMep.ManifoldPa(throttle01, rpm, e.RatedRpm, pAmb);

        double mpWot = e.RatedWideOpenManifoldPa;                         // the sea-level, rated-rpm reference
        double kIndicated = e.RatedIndicatedMepPa / Math.Max(1.0, mpWot); // gross indicated MEP per Pa of manifold

        // The charge is fresh air less the engine's own residuals, and it is denser than its pressure says when the
        // air is cold: sigma/delta is T0/T, which is how altitude reaches the charge rather than through a factor.
        double charge = PistonMep.FreshChargeFactor(mp, pAmb, mpWot, Atmosphere.SeaLevelPressurePa, e.CompressionRatio);
        // And at idle an engine does not get indicated work in proportion to what it swallows at all: see
        // PistonMep.IdleIndicatedLossB, solved from Continental's published C-85 idle.
        double imep = kIndicated * mp * charge * (sigma / delta) * PistonMep.IndicatedEfficiencyFactor(rpm, e.RatedRpm);
        double pmep = PistonMep.PumpingMepPa(mp, pAmb);
        double fmep = e.RatedFrictionMepPa * PistonMep.FrictionShape(e.StrokeM, rpm, e.RatedRpm, mp / Math.Max(1.0, mpWot));
        return (imep - fmep - pmep) * e.TorquePerPa;
    }

    /// <summary>The same for a configured engine, which is the form every caller in the sim and the rig wants.</summary>
    public static double TorqueNm(PropulsionConfig p, double rpm, double throttle01, double pressureRatio,
                                  double densityRatio = 0.0)
        => TorqueNm(Spec.For(p), rpm, throttle01, pressureRatio, densityRatio);

    /// <summary>Shaft power, W — the torque times the speed, and nothing else. It was the other way round before the
    /// FMEP/PMEP model: a power law with the torque divided out of it, which is how the torque came to have no rpm
    /// in it.</summary>
    public static double PowerW(in Spec e, double rpm, double throttle01, double pressureRatio, double densityRatio = 0.0)
        => TorqueNm(e, rpm, throttle01, pressureRatio, densityRatio) * (rpm * 2.0 * Math.PI / 60.0);

    public static double PowerW(PropulsionConfig p, double rpm, double throttle01, double pressureRatio,
                                double densityRatio = 0.0)
        => PowerW(Spec.For(p), rpm, throttle01, pressureRatio, densityRatio);

    /// <summary>
    /// CRANKING RESISTANCE: the torque it takes to TURN a piston engine that is not running, N.m, returned negative
    /// because it opposes whatever is turning it. Ty 2026-11-10, shown a dead propeller windmilling to 4,414 rpm on a
    /// 2,700 rpm engine: "You are not accounting for the cranking resistance of the engine".
    ///
    /// IT IS THE SAME DECOMPOSITION WITH THE COMBUSTION TAKEN OUT, which is what a motored engine is: BMEP = 0 - FMEP
    /// - PMEP. No clamp lifted, no expression reused out of its range — the previous version was
    /// <see cref="PowerW"/>'s open fraction allowed to go negative, which worked only because that fraction's zero
    /// happened to sit near where an engine stops making power, and it carried the 10 inHg intercept's whole
    /// mis-apportioned friction with it.
    ///
    /// THE GAS LOAD IS A MOTORED ONE. Chen-Flynn's load term is the force the rings and bearings carry, and a cylinder
    /// that is only being compressed peaks at about a third of a fired one (<see cref="PistonMep.MotoredPeakFraction"/>).
    ///
    /// AND THE DRY BOUNDARY EXCESS NOW MULTIPLIES THE FRICTION ONLY, not the pumping, which it did before because the
    /// two were one undivided number. Boundary friction is friction; a cold engine's pumping loop is the same pumping
    /// loop. This is one of the reasons the five solved <see cref="PropulsionConfig.DryBoundaryExcess"/> had to be
    /// re-solved when this landed.
    ///
    /// A STOPPED ENGINE PUMPS NOTHING, and that falls out rather than being special-cased: as rpm goes to zero the
    /// manifold fills to ambient, so PMEP goes to zero and what is left at rest is friction alone — which is exactly
    /// what a breakaway torque should be made of (<see cref="PistonCompression"/>, AircraftEngine.BreakawayTorqueNm).
    /// </summary>
    public static double MotoringTorqueNm(in Spec e, double rpm, double throttle01, double pressureRatio,
                                          double densityRatio = 0.0, double boundaryExcess = -1.0)
    {
        if (e.RatedRpm <= 1.0 || rpm <= 0.0 || e.DisplacementM3 <= 0.0) return 0.0;
        double delta = Math.Clamp(pressureRatio, 0.05, 1.3);
        double pAmb = Atmosphere.SeaLevelPressurePa * delta;
        double mp = PistonMep.ManifoldPa(throttle01, rpm, e.RatedRpm, pAmb);
        double mpWot = e.RatedWideOpenManifoldPa;
        double excess = boundaryExcess >= 0.0 ? boundaryExcess
                      : e.DryBoundaryExcess > 0.0 ? e.DryBoundaryExcess : DryBoundaryExcess;
        double peak = mp / Math.Max(1.0, mpWot) * PistonMep.MotoredPeakFraction;
        double fmep = e.RatedFrictionMepPa * PistonMep.FrictionShape(e.StrokeM, rpm, e.RatedRpm, peak)
                    * BoundaryFrictionFactor(rpm, excess);
        double pmep = PistonMep.PumpingMepPa(mp, pAmb);
        return -(fmep + pmep) * e.TorquePerPa;
    }

    /// <summary>The same thing for a configured engine, which is the form every caller in the sim wants: it carries its
    /// own <see cref="PropulsionConfig.DryBoundaryExcess"/> where one has been solved for it.</summary>
    public static double MotoringTorqueNm(PropulsionConfig prop, double rpm, double throttle01,
                                          double pressureRatio, double densityRatio = 0.0)
        => MotoringTorqueNm(Spec.For(prop), rpm, throttle01, pressureRatio, densityRatio, BoundaryExcessFor(prop));

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
    /// <see cref="DryBoundaryExcess"/> = 1.45 is the FLEET amplitude: a dry engine resisting 2.45x its running friction
    /// at rest. It is the geometric mean of what the five types this mechanism is live on each ask for, re-solved
    /// 2026-11-11 against the FMEP/PMEP engine (<see cref="PistonMep"/>) that replaced the 10 inHg friction intercept.
    ///
    /// IT WAS 3.0, AND THE SPREAD BEHIND IT HAS BLOWN OUT. The five used to run 2.19 to 4.71 — a factor of 2.2, which
    /// is what made one fleet number defensible for the types that have not been solved. They now run 0.21 to 4.41, a
    /// factor of 21, and the fleet is split: the two 540s and the 360 sit at 2.1 to 4.4 while both J-3s fall to
    /// almost nothing. That is not noise. The dry excess multiplies FRICTION now and no longer the pumping, and the
    /// pumping is measured (NACA 262) rather than ideal, so what is left for the excess to do is smaller and the
    /// airframe's own stall speed — the thing this is solved against — matters more. The C-85 on a 36 mph aeroplane
    /// is at the end of that: see the two J-3 configs, which record that they no longer reach the anchor at all.
    ///
    /// So this fleet default is now a much weaker claim than it was, and a type that starts running this mechanism
    /// should be solved rather than allowed to inherit it. It still sits inside what the tribology allows — a
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
    public const double DryBoundaryExcess = 1.45, BoundaryRpmScale = 700.0;

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

    /// <summary>
    /// WHERE A FIXED-PITCH PROPELLER SETTLES: the rpm at which the engine's torque and the blade's are equal. The
    /// propeller's demand climbs steeply with rpm (roughly as rpm squared) while the engine's torque is nearly flat,
    /// so the crossing is single and a bisection finds it.
    /// </summary>
    public static double BalancedRpm(BladeElement.Geometry g, in Spec e, double idleRpm,
                                     double throttle01, double pressureRatio, double vAxialMs, double rhoKgM3,
                                     double densityRatio = 0.0)
    {
        BladeElement.Geometry blade = g;   // a lambda cannot close over an 'in' parameter
        Spec spec = e;
        double Excess(double rpm) =>
            TorqueNm(spec, rpm, throttle01, pressureRatio, densityRatio)
            - BladeElement.Solve(blade, vAxialMs, 0.0, rpm, rhoKgM3).TorqueNm;

        throttle01 = Math.Max(throttle01, IdleBleedThrottle(g, e, idleRpm, pressureRatio, rhoKgM3, densityRatio));
        double lo = Math.Max(100.0, idleRpm * 0.5), hi = e.RatedRpm * 1.35;
        if (Excess(hi) > 0) return hi;          // the blade cannot hold it back: an overspeed

        // THE CROSSING IS NO LONGER UNIQUE, AND TAKING THE WRONG ONE IS CATASTROPHIC. This used to bisect straight
        // from lo to hi on the strength of "the propeller's demand climbs steeply while the engine's torque is nearly
        // flat, so the crossing is single". That was true of a torque with no rpm in it. A real torque curve has a
        // PEAK, so the excess can cross zero TWICE: once on the way up, where the engine is only just beating the
        // blade and the equilibrium is UNSTABLE, and again at the top, which is where the propeller actually runs.
        //
        // Caught by the blade fitting, which went from fitting the J-3 to reporting that its finest possible blade
        // could only be turned at 350 rpm at full throttle: the old code's "if (Excess(lo) < 0) return lo" — "it
        // cannot even turn it" — fired on the LOW side of a curve that was positive a few hundred rpm higher up.
        //
        // So: walk down from the top and bisect the FIRST bracket found, which is the highest crossing. A real
        // propeller that is somehow below the low crossing is being dragged down by its own blade and belongs at the
        // bottom, which is what falling through to lo still says.
        // 96 steps, not a handful: the bracket can be NARROW. With the idle bleed floor applied the engine's curve
        // is pushed up until it just meets the blade's at idle rpm, so near idle the two are close to tangent and a
        // coarse scan steps straight over the crossing and falls through to lo — which reads as a propeller stuck at
        // half idle rpm and is how this was caught (the Pitts returning exactly 350.0, its own lower bound, at
        // 10 m/s). This is not a hot path: AircraftEngine integrates the shaft instead of solving it.
        const int steps = 96;
        double upper = hi, upperE = Excess(hi);          // < 0 here, or we returned above
        for (int i = 1; i <= steps; i++)
        {
            double rpm = hi + (lo - hi) * i / steps;
            double e2 = Excess(rpm);
            if (e2 > 0.0)                                 // bracketed between rpm (positive) and upper (negative)
            {
                double a = rpm, b = upper;
                for (int k = 0; k < 30; k++)
                {
                    double mid = 0.5 * (a + b);
                    if (Excess(mid) > 0) a = mid; else b = mid;
                }
                return 0.5 * (a + b);
            }
            upper = rpm; upperE = e2;
        }
        return lo;                                        // it cannot turn it anywhere in the range
    }

    /// <summary>This type's propeller, balanced against this type's engine — the form the sim and the rig both use,
    /// so that neither can accidentally solve against a differently specified engine.</summary>
    public static double BalancedRpm(BladeElement.Geometry g, PropulsionConfig p, double throttle01,
                                     double pressureRatio, double vAxialMs, double rhoKgM3, double densityRatio = 0.0)
        => BalancedRpm(g, Spec.For(p), p.IdleRpm, throttle01, pressureRatio, vAxialMs, rhoKgM3, densityRatio);

    /// <summary>
    /// THE IDLE STOP IS NOT A CLOSED THROTTLE. A throttle lever at its idle stop leaves the plate cracked — plus,
    /// on most installations, a drilled idle air bleed — and what it is set to is exactly what holds the published
    /// idle rpm. This returns that throttle fraction: the one that balances THIS blade at <paramref name="idleRpm"/>
    /// standing still, found by bisection on the same torque excess <see cref="BalancedRpm"/> uses.
    ///
    /// WITHOUT IT A SOLVED PROPELLER HAS NO IDLE. The old throttle law read idle rpm straight off
    /// <c>PropulsionConfig.IdleRpm</c>, so a closed throttle making literally zero power never mattered; a blade-
    /// element propeller asks the engine for torque instead, gets none, and the shaft decays until only friction is
    /// left. Measured on the ground at the idle stop, 2026-11-11: the J-3, solved since the blade fitting landed,
    /// idles at <b>89.8 rpm</b> against its authored 700 — and the three constant-speed types joined it the moment
    /// they began solving their governed shafts (Pitts 249.5, Extra 165.8, Great Lakes 216.6). The two types still on
    /// the old law, the PA-18 and the 172, read exactly 700, which is the tell: it was never the engine, it was the
    /// lever's bottom end.
    ///
    /// IdleRpm is published for every type in the fleet and was being bypassed; this makes it mean something again.
    /// </summary>
    public static double IdleBleedThrottle(BladeElement.Geometry g, in Spec e, double idleRpm,
                                           double pressureRatio, double rhoKgM3, double densityRatio = 0.0)
    {
        if (idleRpm <= 1.0 || e.RatedRpm <= 1.0) return 0.0;
        BladeElement.Geometry blade = g;
        Spec spec = e;
        double demand = BladeElement.Solve(blade, 0.0, 0.0, idleRpm, rhoKgM3).TorqueNm;
        if (demand <= 0.0) return 0.0;
        double Excess(double t) => TorqueNm(spec, idleRpm, t, pressureRatio, densityRatio) - demand;
        if (Excess(0.0) >= 0.0) return 0.0;            // a closed throttle already holds it
        double lo = 0.0, hi = 1.0;
        if (Excess(hi) < 0.0) return hi;               // it cannot hold idle at all: say so by asking for everything
        for (int i = 0; i < 28; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Excess(mid) < 0.0) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>This type's idle stop, from its own engine and its own published idle rpm.</summary>
    public static double IdleBleedThrottle(BladeElement.Geometry g, PropulsionConfig p, double pressureRatio,
                                           double rhoKgM3, double densityRatio = 0.0)
        => IdleBleedThrottle(g, Spec.For(p), p.IdleRpm, pressureRatio, rhoKgM3, densityRatio);

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
                                              double targetStaticRpm, Spec e, double idleRpm,
                                              double rhoKgM3 = 1.225, double loDeg = 8.0, double hiDeg = 18.0)
    {
        Spec spec = e;
        double Static(double pitch) => BalancedRpm(new BladeElement.Geometry(blades, radiusM, pitch, activityFactor, hubFraction),
                                                   spec, idleRpm, 1.0, 1.0, 0.0, rhoKgM3);
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
                                  Spec e, double idleRpm,
                                  double throttle01, double pressureRatio, double vAxialMs, double rhoKgM3)
    {
        double qEngine = TorqueNm(e, commandedRpm, throttle01, pressureRatio);
        double pitch = BladeElement.PitchForTorqueDeg(blades, radiusM, activityFactor, hubFraction, qEngine,
                                                      commandedRpm, vAxialMs, rhoKgM3, stops.FineDeg, stops.CoarseDeg);
        double qAtFine = BladeElement.Solve(new BladeElement.Geometry(blades, radiusM, stops.FineDeg, activityFactor, hubFraction), vAxialMs, 0.0, commandedRpm, rhoKgM3).TorqueNm;
        double qAtCoarse = BladeElement.Solve(new BladeElement.Geometry(blades, radiusM, stops.CoarseDeg, activityFactor, hubFraction), vAxialMs, 0.0, commandedRpm, rhoKgM3).TorqueNm;
        if (qEngine > qAtCoarse)     // even the coarsest blade cannot absorb it: the rpm runs up from the coarse stop
        {
            var g = new BladeElement.Geometry(blades, radiusM, stops.CoarseDeg, activityFactor, hubFraction);
            return new Governed(BalancedRpm(g, e, idleRpm, throttle01, pressureRatio, vAxialMs, rhoKgM3), stops.CoarseDeg, false, true);
        }
        if (qEngine < qAtFine)       // the finest blade still absorbs more than the engine makes: the rpm sags
        {
            var g = new BladeElement.Geometry(blades, radiusM, stops.FineDeg, activityFactor, hubFraction);
            return new Governed(BalancedRpm(g, e, idleRpm, throttle01, pressureRatio, vAxialMs, rhoKgM3), stops.FineDeg, true, false);
        }
        return new Governed(commandedRpm, pitch, false, false);
    }
}

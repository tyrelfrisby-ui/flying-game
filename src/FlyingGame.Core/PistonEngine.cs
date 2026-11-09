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

    /// <summary>Shaft power, W.</summary>
    public static double PowerW(double ratedPowerW, double ratedRpm, double rpm, double throttle01, double pressureRatio)
    {
        if (ratedRpm <= 0 || rpm <= 0) return 0.0;
        double mp = ManifoldInHg(throttle01, pressureRatio);
        double load = (mp - FrictionManifoldInHg) / (SeaLevelManifoldInHg - FrictionManifoldInHg);
        return ratedPowerW * load * (rpm / ratedRpm);
    }

    /// <summary>Shaft torque, N·m. Negative when the engine is being driven: below its friction manifold pressure it
    /// absorbs power rather than making it, which is what lets a windmilling propeller drag.</summary>
    public static double TorqueNm(double ratedPowerW, double ratedRpm, double rpm, double throttle01, double pressureRatio)
    {
        if (rpm <= 1.0) return 0.0;
        return PowerW(ratedPowerW, ratedRpm, rpm, throttle01, pressureRatio) / (rpm * 2.0 * Math.PI / 60.0);
    }

    /// <summary>
    /// WHERE A FIXED-PITCH PROPELLER SETTLES: the rpm at which the engine's torque and the blade's are equal. The
    /// propeller's demand climbs steeply with rpm (roughly as rpm squared) while the engine's torque is nearly flat,
    /// so the crossing is single and a bisection finds it.
    /// </summary>
    public static double BalancedRpm(BladeElement.Geometry g, double ratedPowerW, double ratedRpm, double idleRpm,
                                     double throttle01, double pressureRatio, double vAxialMs, double rhoKgM3)
    {
        BladeElement.Geometry blade = g;   // a lambda cannot close over an 'in' parameter
        double Excess(double rpm) =>
            TorqueNm(ratedPowerW, ratedRpm, rpm, throttle01, pressureRatio)
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

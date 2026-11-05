using System;

namespace FlyingGame.Core.Engine;

/// <summary>
/// The mixture lever of a normally aspirated piston engine, as numbers (Ty 2026-11-04, Quest: "the mixture is stuck at
/// full rich"). The lever meters fuel; the air the engine breathes falls with density, so the same lever position is
/// richer the higher you fly. What the engine makes is a function of the fuel/air EQUIVALENCE RATIO φ (1 = chemically
/// correct, &gt; 1 rich):
///
///  - best power about 100–125 °F rich of peak EGT, φ ≈ <see cref="PhiBestPower"/>; the rich side is flat (full rich at
///    sea level is within 1 % of it), the lean side falls quickly (≈ 3 % down at peak EGT, ≈ 14 % at φ 0.85);
///  - EGT peaks at φ = 1 and falls on both sides;
///  - below φ ≈ 0.75 the engine runs rough, below <see cref="PhiLeanLimit"/> it quits (lean misfire);
///  - the bottom of the lever's travel is IDLE CUT-OFF: no fuel at all, the engine stops.
///
/// <see cref="PowerFactor"/> is normalised to FULL RICH, so an aeroplane flown with the mixture where it has always been
/// makes exactly the power it always made at every height: this adds the lever, it does not re-tune the fleet. Leaning
/// from full rich then gains a little (≈ 1 % at sea level, ≈ 10 % at 8,000 ft) before it loses. Pure maths; the state
/// (running / stopped, the relight) lives in the aircraft.
/// </summary>
public static class MixtureModel
{
    /// <summary>Lever positions at or below this are idle cut-off: the fuel is shut off.</summary>
    public const double CutoffLever = 0.05;
    /// <summary>Equivalence ratio at full rich at sea-level density (a typical carburettor / injector rich setting).</summary>
    public const double PhiFullRichSeaLevel = 1.25;
    /// <summary>Best power mixture.</summary>
    public const double PhiBestPower = 1.13;
    /// <summary>Peak EGT.</summary>
    public const double PhiPeakEgt = 1.0;
    /// <summary>Leaner than this the engine runs rough.</summary>
    public const double PhiRough = 0.75;
    /// <summary>Leaner than this it will not fire: the engine quits.</summary>
    public const double PhiLeanLimit = 0.6;
    /// <summary>... and it fires again only once richer than this (a little hysteresis, so it does not chatter).</summary>
    public const double PhiRelight = 0.63;

    private const double RichCurve = 0.5, LeanCurve = 1.78;

    /// <summary>Fraction of full-rich fuel flow the lever meters (0 at cut-off, 1 at full rich).</summary>
    public static double FuelFraction(double lever) =>
        Math.Clamp((Math.Clamp(lever, 0.0, 1.0) - CutoffLever) / (1.0 - CutoffLever), 0.0, 1.0);

    /// <summary>The equivalence ratio for a lever position at density ratio σ = ρ/ρ₀ (fixed fuel, less air aloft).</summary>
    public static double Phi(double lever, double sigma) =>
        PhiFullRichSeaLevel / Math.Clamp(sigma, 0.2, 1.5) * FuelFraction(lever);

    /// <summary>Power at φ as a fraction of best power (1 at <see cref="PhiBestPower"/>), 0 where it will not fire.</summary>
    public static double PowerAtPhi(double phi)
    {
        if (phi < PhiLeanLimit) return 0.0;
        double d = phi - PhiBestPower;
        return Math.Max(0.0, d >= 0.0 ? 1.0 - RichCurve * d * d : 1.0 - LeanCurve * d * d);
    }

    /// <summary>Shaft power relative to the same throttle at FULL RICH (1.0 there, at any density). 0 when it will not fire.</summary>
    public static double PowerFactor(double lever, double sigma)
    {
        double rich = PowerAtPhi(Phi(1.0, sigma));
        return rich > 1e-9 ? PowerAtPhi(Phi(lever, sigma)) / rich : 0.0;
    }

    /// <summary>0 smooth … 1 on the point of quitting: how rough a lean engine runs.</summary>
    public static double Roughness(double phi) =>
        phi >= PhiRough ? 0.0 : Math.Clamp((PhiRough - phi) / (PhiRough - PhiLeanLimit), 0.0, 1.0);

    /// <summary>True when the lever (at this density) gives a mixture that will fire.</summary>
    public static bool Fires(double lever, double sigma) => lever > CutoffLever && Phi(lever, sigma) >= PhiLeanLimit;

    /// <summary>Steady exhaust-gas temperature, °F, for a running engine at φ and shaft power fraction p (0..1):
    /// peak ≈ 1,450 °F at full power, ≈ 1,290 °F full rich at sea level, cooler at low power.</summary>
    public static double EgtF(double phi, double power01)
    {
        double peak = 1000.0 + 450.0 * Math.Sqrt(Math.Clamp(power01, 0.0, 1.0));
        double off = phi >= PhiPeakEgt ? 650.0 * (phi - PhiPeakEgt) : 900.0 * (PhiPeakEgt - phi);
        return peak - off;
    }

    /// <summary>
    /// A propeller driven by the air (the engine dead, the aeroplane moving): it turns at roughly the advance ratio of
    /// zero thrust, J = V/(nD) ≈ 1 for a cruise prop, a constant-speed one on its fine-pitch stop about the same.
    /// Below <see cref="MinWindmillRpm"/> compression and friction hold it: it stops.
    /// </summary>
    public static double WindmillRpm(double airspeedMs, double propDiameterM, double maxRpm)
    {
        if (propDiameterM <= 0.0) return 0.0;
        double rpm = 60.0 * Math.Max(0.0, airspeedMs) / (WindmillAdvanceRatio * propDiameterM);
        return rpm < MinWindmillRpm ? 0.0 : Math.Min(rpm, maxRpm);
    }

    public const double WindmillAdvanceRatio = 1.0;
    public const double MinWindmillRpm = 400.0;
}

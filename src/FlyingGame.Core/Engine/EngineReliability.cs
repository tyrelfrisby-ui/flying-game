using System;
using System.Collections.Generic;
using System.Linq;

namespace FlyingGame.Core.Engine;

/// <summary>What went wrong. Every member of this enum is a RANDOM failure drawn from the engine's hazard rate.</summary>
public enum FailureCause
{
    None = 0,

    // Fuel — the largest bucket. NOT fuel exhaustion or tank mismanagement: those are the pilot's doing and are
    // driven by the fuel system's own state, never drawn from this pool (see the class comment on why that matters).
    WaterInFuel,
    FuelContamination,          // debris progressively plugging a fuel screen or line
    FuelLineRestriction,        // a partial restriction: surges at high power, runs clean at low
    EngineFuelPumpFailure,

    // Ignition
    MagnetoFailure,             // one mag: rough, a few per cent down, hotter cylinders. Both is a dead engine.
    SparkPlugFouling,           // lead fouling at low power/rich; clears with a lean burn. Recoverable.

    // Cylinder
    ExhaustValveFailure,        // progressive loss of one cylinder's compression, then the cylinder
    CylinderStructuralFailure,  // compression gone outright; on a radial the jug can leave the engine

    // Oil and bearings
    BearingFailure,             // oil temp up, pressure down, vibration up, then seizure
    OilLineLoss,                // oil overboard: pressure falls, and it goes on the windscreen
    OilPumpFailure,

    // Accessory
    PropGovernorFailure,        // constant-speed prop runs away fine or sticks coarse

    /// <summary>The crankshaft lets go and the propeller departs with it. NOT drawn from this pool: it has its own
    /// hazard, from the gyroscopic load the pilot has actually put through it — see <see cref="CrankshaftFatigue"/>.
    /// Listed here so the effects layer has one vocabulary for what broke.</summary>
    CrankshaftFailure,
}

/// <summary>How a failure expresses itself, which is what the engine model and the effects layer act on.</summary>
public enum FailureKind
{
    /// <summary>A hard ceiling on power. The engine runs, but will not make full rated power.</summary>
    PartialPower,
    /// <summary>Power cycles: starved at high demand, recovers as rpm and demand fall, then starves again.</summary>
    Surging,
    /// <summary>Rough running with a modest power loss. Nothing is about to let go.</summary>
    Roughness,
    /// <summary>Degrades over a time constant, then stops. The gauges tell you first, if you are reading them.</summary>
    Progressive,
    /// <summary>Immediate and total.</summary>
    Sudden,
}

/// <summary>Carburettor or fuel injection — it decides which conditional failures an engine can even have.</summary>
public enum InductionType { None = 0, Carburettor, FuelInjection }

/// <summary>
/// One row of the failure table.
///
/// `set`, not `init`, even though nothing mutates these after construction: Unity's compiler has no
/// System.Runtime.CompilerServices.IsExternalInit, so an `init` accessor compiles clean under .NET 8 and takes the
/// iOS player build down with CS0518. That cost a day's build on 2026-10-28; see BiplaneCellule.cs and
/// docs/AGENT-LOG.md. A green test suite is not a build.
/// </summary>
public sealed class FailureMode
{
    public FailureCause Cause { get; set; }
    public FailureKind Kind { get; set; }
    /// <summary>Share of the engine's total hazard rate. The weights over a given engine sum to 1.</summary>
    public double Weight { get; set; }
    /// <summary>Seconds from onset to the end state. 0 for a failure with no warning.</summary>
    public double OnsetSeconds { get; set; }
    /// <summary>Power still available at the end state, as a fraction of rated. 0 is a stopped engine.</summary>
    public double EndPowerFraction { get; set; }
    /// <summary>Does it put oil on the windscreen?</summary>
    public bool ThrowsOil { get; set; }
    /// <summary>Does it smoke?</summary>
    public bool Smokes { get; set; }
    public string Describe { get; set; } = "";
}

/// <summary>
/// The randomizer behind engine failure.
///
/// THE RATE IS THE INPUT, AND IT IS PRESERVED EXACTLY. A piston engine fails once per
/// <see cref="StandardMtbfHours"/> hours at the standard setting. That is a hazard rate, lambda = 1/3700 per hour,
/// and the chance of a failure in a step of dt hours is 1 - exp(-lambda*dt). Each cause owns a SHARE of that one
/// rate (the weights sum to 1), so the engine fails for many different reasons at exactly the rate asked for —
/// adding, removing or reweighting causes cannot change how often it breaks, only what breaks. That is the property
/// the owner was reaching for, and it falls out of the superposition of competing exponential hazards: drawing once
/// at the total rate and then picking a cause by weight is mathematically identical to racing one draw per cause,
/// and it cannot drift.
///
/// WHAT IS DELIBERATELY NOT IN THIS POOL. Carburettor ice, vapour lock, fuel exhaustion and detonation are not
/// random: they happen whenever the conditions are right, every time. Drawing them from this pool would be wrong
/// twice over — they would stop happening when the conditions warranted them, and they would steal probability from
/// the mechanical causes, making the engine quietly MORE reliable than 1 in 3700 in exactly the ways that matter.
/// They belong in their own condition-driven models.
///
/// RELIABILITY ONLY GETS WORSE. <see cref="ClampUnreliability"/> holds the multiplier in [1, 1000]: an engine is
/// never better than the fleet figure, and the worst setting breaks one per 3.7 hours.
/// </summary>
public static class EngineReliability
{
    /// <summary>Mean time between random failures of a piston aero engine, hours (owner 2026-10-30).
    /// Published fleet estimates cluster around 1 in 3200–4000 hours, so this sits inside the range.</summary>
    public const double StandardMtbfHours = 3700.0;

    /// <summary>Mean time between in-cockpit electrical fires/smoke, hours (owner 2026-10-30). Its own hazard,
    /// independent of the engine: the wiring can smoke while the engine is perfect.</summary>
    public const double StandardElectricalMtbfHours = 10000.0;

    /// <summary>The worst the owner can set it: 1000x the standard rate, one failure per 3.7 hours.</summary>
    public const double MaxUnreliability = 1000.0;

    /// <summary>Never below 1: the setting makes an engine worse, never better than the fleet.</summary>
    public static double ClampUnreliability(double m) =>
        double.IsNaN(m) ? 1.0 : Math.Clamp(m, 1.0, MaxUnreliability);

    /// <summary>Hazard rate per hour for a given unreliability multiplier.</summary>
    public static double RatePerHour(double unreliability, double mtbfHours = StandardMtbfHours) =>
        ClampUnreliability(unreliability) / mtbfHours;

    /// <summary>
    /// The chance of at least one failure in a step. Exact, not the small-lambda approximation, so neither a long
    /// step nor a very unreliable engine can produce a probability above 1 or a rate that quietly sags.
    /// </summary>
    public static double StepProbability(double ratePerHour, double dtSeconds) =>
        ratePerHour <= 0.0 || dtSeconds <= 0.0 ? 0.0 : OneMinusExpMinus(ratePerHour * dtSeconds / 3600.0);

    /// <summary>
    /// 1 - exp(-x), accurately. .NET has no expm1, and at the values this is actually called with — a 20 ms step at
    /// 1 in 3700 hours is x = 1.5e-9 — writing it as 1.0 - Math.Exp(-x) subtracts two numbers either side of 1 and
    /// throws away most of the significant digits. The series is exact in that range and costs nothing.
    /// </summary>
    private static double OneMinusExpMinus(double x) =>
        x < 1e-5 ? x * (1.0 - 0.5 * x * (1.0 - x / 3.0)) : 1.0 - Math.Exp(-x);

    /// <summary>
    /// The failure table for an engine. Fuel is the largest bucket at 38 %, as the owner asked and as the accident
    /// record supports; a radial trades some valve failures for the cylinder departures it is actually known for.
    ///
    /// THE WEIGHTS ARE AN ALLOCATION, NOT A MEASUREMENT. They are informed by the NTSB/AOPA record — fuel-related
    /// causes are the largest single bucket there (27–43 % across studies), catastrophic mechanical failure about a
    /// quarter, and one fatal-accident study found 34 % of failed components were crankshaft, rod or piston — but
    /// that record counts ACCIDENTS, and most of its fuel share is exhaustion and mismanagement, which this pool
    /// deliberately excludes. So no published table was copied, and none could be. What would refine these is a
    /// service-difficulty or warranty-claim distribution, which counts failures rather than crashes.
    /// </summary>
    public static IReadOnlyList<FailureMode> Table(bool radial = false)
    {
        double valve = radial ? 0.08 : 0.14;
        double jug = radial ? 0.12 : 0.06;     // a radial sheds a cylinder; a flat four almost never does
        return new FailureMode[]
        {
            // ---- fuel: 0.38 ----
            new() { Cause = FailureCause.WaterInFuel, Kind = FailureKind.Surging, Weight = 0.18,
                    OnsetSeconds = 4.0, EndPowerFraction = 0.0,
                    Describe = "water through the carburettor or the servo: coughs, surges, quits" },
            new() { Cause = FailureCause.FuelContamination, Kind = FailureKind.Progressive, Weight = 0.12,
                    OnsetSeconds = 90.0, EndPowerFraction = 0.0,
                    Describe = "debris closing a fuel screen: power falls away over a minute or two" },
            new() { Cause = FailureCause.FuelLineRestriction, Kind = FailureKind.Surging, Weight = 0.04,
                    OnsetSeconds = 2.0, EndPowerFraction = 0.55,
                    Describe = "a partial restriction: surges at high power, runs clean at low" },
            new() { Cause = FailureCause.EngineFuelPumpFailure, Kind = FailureKind.Sudden, Weight = 0.04,
                    OnsetSeconds = 0.0, EndPowerFraction = 0.0,
                    Describe = "the engine-driven fuel pump: fuel pressure gone, and the engine with it" },
            // ---- ignition: 0.20 ----
            new() { Cause = FailureCause.MagnetoFailure, Kind = FailureKind.Roughness, Weight = 0.12,
                    OnsetSeconds = 0.0, EndPowerFraction = 0.93,
                    Describe = "one magneto out: rough, a few per cent down, hotter cylinders" },
            new() { Cause = FailureCause.SparkPlugFouling, Kind = FailureKind.Roughness, Weight = 0.08,
                    OnsetSeconds = 20.0, EndPowerFraction = 0.85,
                    Describe = "a lead-fouled plug: rough and down on power, clears with a lean burn" },
            // ---- cylinder: 0.20 ----
            new() { Cause = FailureCause.ExhaustValveFailure, Kind = FailureKind.Progressive, Weight = valve,
                    OnsetSeconds = 120.0, EndPowerFraction = 0.70, Smokes = true,
                    Describe = "a burning exhaust valve: one cylinder goes away over a couple of minutes" },
            new() { Cause = FailureCause.CylinderStructuralFailure, Kind = FailureKind.Sudden, Weight = jug,
                    OnsetSeconds = 0.0, EndPowerFraction = 0.0, ThrowsOil = true, Smokes = true,
                    Describe = radial ? "a cylinder leaves the engine: oil everywhere, violent vibration"
                                      : "a cracked cylinder: compression gone, oil overboard" },
            // ---- oil and bearings: 0.20 ----
            new() { Cause = FailureCause.BearingFailure, Kind = FailureKind.Progressive, Weight = 0.10,
                    OnsetSeconds = 240.0, EndPowerFraction = 0.0, ThrowsOil = true, Smokes = true,
                    Describe = "a bearing on its way out: oil temperature up, pressure down, vibration, then seizure" },
            new() { Cause = FailureCause.OilLineLoss, Kind = FailureKind.Progressive, Weight = 0.07,
                    OnsetSeconds = 300.0, EndPowerFraction = 0.0, ThrowsOil = true,
                    Describe = "oil going overboard: pressure falls, and it arrives on the windscreen" },
            new() { Cause = FailureCause.OilPumpFailure, Kind = FailureKind.Progressive, Weight = 0.03,
                    OnsetSeconds = 150.0, EndPowerFraction = 0.0,
                    Describe = "oil pump: pressure straight to zero, then the engine follows" },
            // ---- accessory: 0.02 ----
            new() { Cause = FailureCause.PropGovernorFailure, Kind = FailureKind.PartialPower, Weight = 0.02,
                    OnsetSeconds = 1.0, EndPowerFraction = 0.75,
                    Describe = "governor: the prop runs away fine or sticks coarse" },
        };
    }

    /// <summary>The weights of a table, which must sum to 1 for the aggregate rate to be the rate asked for.</summary>
    public static double WeightSum(IReadOnlyList<FailureMode> table) => table.Sum(m => m.Weight);

    /// <summary>
    /// Draw for a failure over one step. Returns null when nothing broke, which is almost always.
    /// <paramref name="rng"/> is passed in rather than owned, so a test can replay a seed and a session can be
    /// reproduced from one.
    /// </summary>
    public static FailureMode? Draw(Random rng, double dtSeconds, double unreliability, bool radial = false)
    {
        if (rng.NextDouble() >= StepProbability(RatePerHour(unreliability), dtSeconds)) return null;
        return PickByWeight(rng.NextDouble(), Table(radial));
    }

    /// <summary>
    /// Which cause, given a uniform draw in [0,1). Split out so the distribution can be tested without burning a
    /// hazard draw per sample: one draw at the total rate then a weighted pick is identical to racing a draw per
    /// cause, and it cannot drift away from the rate.
    /// </summary>
    public static FailureMode PickByWeight(double u, IReadOnlyList<FailureMode> table)
    {
        double pick = u * WeightSum(table), run = 0.0;
        foreach (FailureMode m in table)
        {
            run += m.Weight;
            if (pick < run) return m;
        }
        return table[^1];
    }

    /// <summary>
    /// Draw for cockpit electrical smoke, on its own clock (<see cref="StandardElectricalMtbfHours"/>) and its own
    /// multiplier. True means it has just started.
    /// </summary>
    public static bool DrawElectricalSmoke(Random rng, double dtSeconds, double unreliability) =>
        rng.NextDouble() < StepProbability(RatePerHour(unreliability, StandardElectricalMtbfHours), dtSeconds);
}

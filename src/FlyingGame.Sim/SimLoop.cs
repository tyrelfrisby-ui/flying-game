using FlyingGame.Core;
using FlyingGame.Core.Aero;

namespace FlyingGame.Sim;

/// <summary>
/// Fixed-timestep sim loop around an <see cref="Aircraft"/>. Default step targets 200 Hz per
/// ARCHITECTURE.md ("200 Hz sim substeps inside Unity's 50 Hz FixedUpdate").
/// </summary>
public sealed class SimLoop
{
    public const double DefaultFixedDtSec = 1.0 / 200.0;

    public Aircraft Aircraft { get; }
    public double FixedDtSec { get; }

    public SimLoop(Aircraft aircraft, double fixedDtSec = DefaultFixedDtSec)
    {
        Aircraft = aircraft;
        FixedDtSec = fixedDtSec;
    }

    /// <summary>Runs a whole number of fixed steps covering (at least) the given duration, holding one control input throughout. Returns the resulting state.</summary>
    public RigidBodyState RunFor(double durationSec, ControlInputs inputs)
    {
        int steps = (int)Math.Round(durationSec / FixedDtSec);
        for (int i = 0; i < steps; i++)
        {
            Aircraft.Step(inputs, FixedDtSec);
        }

        return Aircraft.State;
    }

    /// <summary>Same as <see cref="RunFor(double,ControlInputs)"/> but holding explicit deflection targets (e.g. a solved trim) rather than shaped stick input.</summary>
    public RigidBodyState RunFor(double durationSec, ControlDeflections targetDeflections)
    {
        int steps = (int)Math.Round(durationSec / FixedDtSec);
        for (int i = 0; i < steps; i++)
        {
            Aircraft.StepWithDeflectionTargets(targetDeflections, FixedDtSec);
        }

        return Aircraft.State;
    }

    /// <summary>Fixed-step accumulator for a variable-rate host (e.g. Unity's Update/FixedUpdate) — consumes as many fixed steps as `realDtSec` allows, carrying remainder in `accumulatorSec`.</summary>
    public void Advance(double realDtSec, ControlInputs inputs, ref double accumulatorSec)
    {
        accumulatorSec += realDtSec;
        while (accumulatorSec >= FixedDtSec)
        {
            Aircraft.Step(inputs, FixedDtSec);
            accumulatorSec -= FixedDtSec;
        }
    }
}

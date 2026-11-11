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
    /// <summary>Step for the AI traffic (combat drones, formations, opponents): 50 Hz. The player's aircraft keeps 200 Hz.
    /// Fifteen AI aircraft at 200 Hz cost more CPU than an iPad mini has (owner 2026-10-04: 3 fps for 20 s at a time);
    /// their flight at 50 Hz matches 200 Hz (ZzDroneRate → FormationTests/DronePilotTests run at this rate).</summary>
    public const double AiFixedDtSec = 1.0 / 50.0;

    public Aircraft Aircraft { get; }
    public double FixedDtSec { get; }

    /// <summary>
    /// CALLED ONCE PER FIXED PHYSICS STEP, after <see cref="Aircraft.Step"/> has fully returned — so the state,
    /// the forces, the damage and the contacts are all the settled end-of-step values rather than something
    /// half-integrated. The control input for that step is handed over with it, because the sim does not keep it.
    ///
    /// It exists for the flight-test recorder, which has to sample at the sim's own rate: Aircraft's
    /// SurfaceContactsStepped fires mid-step and leaves the damage channels one step stale, and the Bridge's
    /// PostStep fires once a FRAME, which cannot give a fixed 50 or 200 Hz at all. Null by default, one null test
    /// per step when nothing is listening.
    /// </summary>
    public Action<SimLoop, ControlInputs>? Stepped;

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
            Stepped?.Invoke(this, inputs);
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
    /// <summary>At most this much simulated time per rendered frame. Without a cap, one slow frame asks for more steps the
    /// next frame, which is slower still — on the iPad mini the game sat at 3 fps for 20 s at a time (owner 2026-10-04:
    /// "choppy … smooth for about 30 s … then choppy again"). Past the cap the sim runs a little slower than real time for
    /// that frame instead.</summary>
    public const double MaxCatchUpSec = 1.0 / 20.0;

    public void Advance(double realDtSec, ControlInputs inputs, ref double accumulatorSec) =>
        Advance(realDtSec, inputs, ref accumulatorSec, MaxCatchUpSec);

    /// <summary>The catch-up cap for a headset running at <paramref name="displayHz"/>: two display periods (27.8 ms,
    /// five or six steps, at 72 Hz). A dropped frame is caught up in full; anything longer is not chased. On a phone the
    /// 1/20 s cap is a frame and a half at 30 fps; in a headset it is 3.6 frames, so one 50 ms garbage collection asked
    /// the next frame for ten steps — a third again of the frame's CPU — which made THAT frame late too: the
    /// "slow, catches up, slows again" Ty saw on the Quest (2026-11-02). Never more than <see cref="MaxCatchUpSec"/>.</summary>
    public static double HeadsetCatchUpSec(double displayHz) =>
        displayHz > 1.0 ? Math.Min(MaxCatchUpSec, 2.0 / displayHz) : MaxCatchUpSec;

    /// <summary><see cref="Advance(double, ControlInputs, ref double)"/> with the caller's own cap. Returns true when the
    /// cap threw time away: the sim ran slower than real time this frame rather than make the next frame late.</summary>
    public bool Advance(double realDtSec, ControlInputs inputs, ref double accumulatorSec, double maxCatchUpSec)
    {
        double wanted = accumulatorSec + realDtSec;
        accumulatorSec = Math.Min(wanted, maxCatchUpSec);
        bool capped = wanted > maxCatchUpSec;
        bool capture = Aircraft.CaptureForces;
        while (accumulatorSec >= FixedDtSec)
        {
            // Force vectors are drawn once a frame: capture them on the frame's LAST step only (capturing every strip on
            // every step was ~4× the allocation for nothing — the side-view lessons had it on).
            Aircraft.CaptureForces = capture && accumulatorSec < 2 * FixedDtSec;
            Aircraft.Step(inputs, FixedDtSec);
            accumulatorSec -= FixedDtSec;
            Stepped?.Invoke(this, inputs);
        }
        Aircraft.CaptureForces = capture;
        return capped;
    }
}

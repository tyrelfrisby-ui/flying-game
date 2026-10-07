using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Aero;

namespace FlyingGame.Sim.Challenge;

/// <summary>
/// Runs and grades a ChallengeDefinition against a live aircraft. The host (Unity or a headless test)
/// spawns the aircraft from Spawn, then calls Tick each sim step; the runner advances phases, samples
/// each tolerance band, accumulates in-band time, and reports a 0–100 score and pass/fail at the end.
/// Angle bands wrap correctly (heading ±π). All engine-agnostic — no Unity here.
/// </summary>
public sealed class ChallengeRunner
{
    public ChallengeDefinition Def { get; }
    public int PhaseIndex { get; private set; }
    public double PhaseElapsed { get; private set; }
    public bool Complete { get; private set; }
    public double Score { get; private set; }
    public bool Passed { get; private set; }
    public string? LastCalloutKey { get; private set; }
    /// <summary>Live per-tolerance in-band flags for the current phase (HUD). Rebuilt each Tick.</summary>
    public readonly List<(string Signal, bool InBand, double Error)> LiveBands = new();
    public double LiveScore => _totalWeightedTime > 0 ? 100.0 * _inBandWeightedTime / _totalWeightedTime : 0.0;
    public double PhaseTimeLeft => PhaseIndex < Def.Phases.Count ? System.Math.Max(0, Def.Phases[PhaseIndex].DurationSec - PhaseElapsed) : 0;

    private readonly Dictionary<string, double> _phaseStartValues = new();
    private double _inBandWeightedTime;
    private double _totalWeightedTime;
    private int _lastCalloutIdx = -1;

    public ChallengeRunner(ChallengeDefinition def)
    {
        Def = def;
    }

    /// <summary>Spawns the aircraft into the challenge's start state. Returns the ready-to-fly Aircraft.</summary>
    /// <summary>Spawn at the challenge's start. The JSON positions were written for a flat world at sea level with the
    /// runway threshold at the origin; the world now has plateaus (the Valley sits at 1,000 ft) and a moved runway
    /// (2026-10-07: the landing challenges started 120 m underground). <paramref name="originX"/>/<paramref name="originY"/>
    /// = the main runway's south threshold, <paramref name="groundElevM"/> = the field: the start is measured from them
    /// (height above the field). The grading signals don't use position, so nothing else moves.</summary>
    public Aircraft Spawn(AircraftConfig config, double originX = 0, double originY = 0, double groundElevM = 0)
    {
        SpawnState sp = Def.Spawn;
        var pos0 = new Vec3(originX + sp.Pos[0], originY + sp.Pos[1], sp.Pos[2] - groundElevM);
        double alpha = 0, elevatorTrim = 0;
        if (sp.AttitudeTrim)
        {
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, sp.IasMs, -pos0.Z);
            alpha = trim.AlphaRad;
            elevatorTrim = trim.ElevatorRad;
            double thetaHalf = trim.ThetaRad / 2.0;
            // Compose heading (about world z / body via yaw) with the trimmed pitch.
            var pitchQ = new Quat(0, System.Math.Sin(thetaHalf), 0, System.Math.Cos(thetaHalf));
            var yawQ = new Quat(0, 0, System.Math.Sin(sp.HeadingRad / 2), System.Math.Cos(sp.HeadingRad / 2));
            var att = Quat.Multiply(yawQ, pitchQ).Normalized();
            var velBody = new Vec3(sp.IasMs * System.Math.Cos(alpha), 0, sp.IasMs * System.Math.Sin(alpha));
            var state = new RigidBodyState(pos0, att, velBody, Vec3.Zero);
            return new Aircraft(config, state, new ControlDeflections(0, elevatorTrim, 0, 0));
        }

        var q = new Quat(0, 0, System.Math.Sin(sp.HeadingRad / 2), System.Math.Cos(sp.HeadingRad / 2));
        var v = new Vec3(sp.IasMs, 0, 0);
        var st = new RigidBodyState(pos0, q, v, Vec3.Zero);
        return new Aircraft(config, st, ControlDeflections.Neutral);
    }

    /// <summary>Advance the challenge one step. Call after the aircraft has been stepped by dt.</summary>
    public void Tick(Aircraft aircraft, double dt)
    {
        if (Complete || PhaseIndex >= Def.Phases.Count)
        {
            return;
        }

        ChallengePhase phase = Def.Phases[PhaseIndex];

        if (PhaseElapsed == 0.0)
        {
            _phaseStartValues.Clear();
            foreach (Tolerance t in phase.Tolerances)
            {
                _phaseStartValues[t.Signal] = FlightSignals.Read(t.Signal, aircraft);
            }
            _lastCalloutIdx = -1;
        }

        // Fire callouts whose time has arrived.
        for (int i = 0; i < phase.Callouts.Count; i++)
        {
            if (i > _lastCalloutIdx && PhaseElapsed >= phase.Callouts[i].AtSec)
            {
                LastCalloutKey = phase.Callouts[i].TextKey;
                _lastCalloutIdx = i;
            }
        }

        // Grade every tolerance this step.
        LiveBands.Clear();
        foreach (Tolerance t in phase.Tolerances)
        {
            double value = FlightSignals.Read(t.Signal, aircraft);
            double target = t.TargetMode switch
            {
                "relative" => _phaseStartValues[t.Signal] + t.Target,
                "current" => _phaseStartValues[t.Signal],
                _ => t.Target,
            };
            double err = IsAngle(t.Signal) ? WrapPi(value - target) : value - target;
            bool inBand = System.Math.Abs(err) <= t.Band;
            _totalWeightedTime += t.Weight * dt;
            if (inBand)
            {
                _inBandWeightedTime += t.Weight * dt;
            }
            LiveBands.Add((t.Signal, inBand, err));
        }

        PhaseElapsed += dt;
        if (PhaseElapsed >= phase.DurationSec)
        {
            PhaseIndex++;
            PhaseElapsed = 0.0;
            if (PhaseIndex >= Def.Phases.Count)
            {
                Finish();
            }
        }
    }

    private void Finish()
    {
        Complete = true;
        Score = _totalWeightedTime > 0 ? 100.0 * _inBandWeightedTime / _totalWeightedTime : 0.0;
        Passed = Score >= Def.Scoring.PassThreshold;
    }

    private static bool IsAngle(string signal) =>
        signal is "headingRad" or "bankRad" or "pitchRad" or "alphaRad" or "betaRad";

    private static double WrapPi(double a)
    {
        while (a > System.Math.PI) a -= 2 * System.Math.PI;
        while (a < -System.Math.PI) a += 2 * System.Math.PI;
        return a;
    }
}

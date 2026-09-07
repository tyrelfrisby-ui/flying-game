namespace FlyingGame.Sim.Challenge;

/// <summary>
/// Data-driven challenge (see docs/DATA-CONTRACTS.md). New challenges are new JSON, not new C#.
/// A challenge spawns the aircraft in a state, then grades one or more phases against tolerance bands;
/// the score is % time in band (accuracy) or accumulated points. The ChallengeRunner does the grading.
/// </summary>
public sealed class ChallengeDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Arena { get; set; } = "";
    public string AircraftId { get; set; } = "";
    public string TitleKey { get; set; } = "";

    public SpawnState Spawn { get; set; } = new();
    public List<ChallengePhase> Phases { get; set; } = new();
    public ScoringConfig Scoring { get; set; } = new();
    public FailConfig Fail { get; set; } = new();
}

public sealed class SpawnState
{
    public double[] Pos { get; set; } = { 0, 0, -600 };  // NED; z<0 = altitude
    public double HeadingRad { get; set; }
    public double IasMs { get; set; } = 22;
    public bool AttitudeTrim { get; set; } = true;        // spawn at solved trim for IasMs
}

public sealed class ChallengePhase
{
    public string Id { get; set; } = "";
    public double DurationSec { get; set; } = 30;
    public List<Tolerance> Tolerances { get; set; } = new();
    public List<Callout> Callouts { get; set; } = new();
}

/// <summary>A live-graded band on one flight signal: |signal - target| within band = in-tolerance.
/// TargetMode "relative" reads the target as an offset from the value at phase start (e.g. a turn to
/// a new heading). "current" freezes the phase-start value as the target (hold what you have).</summary>
public sealed class Tolerance
{
    public string Signal { get; set; } = "";          // ChallengeSignal name
    public double Target { get; set; }
    public double Band { get; set; }
    public string TargetMode { get; set; } = "absolute"; // absolute | relative | current
    public double Weight { get; set; } = 1.0;
}

public sealed class Callout
{
    public double AtSec { get; set; }
    public string TextKey { get; set; } = "";
}

public sealed class ScoringConfig
{
    public string Mode { get; set; } = "accuracyPercent"; // accuracyPercent | points
    public double PassThreshold { get; set; } = 80;
}

public sealed class FailConfig
{
    public string OnGroundContact { get; set; } = "crash"; // crash | fail | ignore
    public string OnOverstress { get; set; } = "crash";
    public bool BailAllowed { get; set; } = true;
}

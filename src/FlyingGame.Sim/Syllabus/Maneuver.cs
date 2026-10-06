using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Practice;

namespace FlyingGame.Sim.Syllabus;

/// <summary>The training courses (owner 2026-10-03: "glider, private, multi, ATP … basic aerobatics").</summary>
public enum Course { Glider, Private, Commercial, Instrument, Multi, Atp, Aerobatics }

/// <summary>Everything a manoeuvre needs to know about the aircraft this instant (angles in RADIANS, speeds m/s, heights m).</summary>
public sealed class FlightNow
{
    public double T, Dt;                      // seconds since the hand-over (the live part), step length
    public double Roll, Pitch, Heading;       // Euler (rad); heading 0 = north, + = clockwise
    public double P, Q, R;                    // body rates (rad/s): roll right, nose up, nose right
    public double Ias, Agl, Hdot, Beta, Alpha, G, StalledFraction;
    public double Vso, Vs1, Cruise, Vy, Vx, BestGlide, Vyse, Vmc, Va;
    public double TurnedDeg;                  // heading change since the manoeuvre began (unwrapped, + = right)
    public double TurnRateDegS;               // rate of heading change (smoothed, + = right)
    public double RolledDeg, LoopedDeg;       // ∫p dt and ∫q dt since the manoeuvre began (rolls, loops — Euler fails near vertical)
    public double WingTiltDeg;                // angle of the wing axis from where it pointed at the start (the plane of a loop)
    public double VneMs, GLimit;
    public double StartAgl, StartHeadingDeg;
    public bool Glider, Multi;
    public AircraftConfig Config = null!;
    public bool Stalled => StalledFraction > 0.35;
    public double BankDeg => Roll * R2D;
    public double PitchDeg => Pitch * R2D;
    public double HeadingDeg => (Heading * R2D + 360) % 360;
    public double Kt(double ms) => ms * 1.943844;
    public double IasKt => Ias * 1.943844;
    public double AltFt => Agl * 3.28084;
    public const double R2D = 180 / Math.PI, D2R = Math.PI / 180;
}

/// <summary>What the lesson starts from: height, speed, configuration, and (unusual attitudes) a starting attitude.</summary>
public sealed class ManeuverSetup
{
    public double AglM = 914.4;               // 3,000 ft over the field
    public Func<FlightNow, double> Speed = f => f.Cruise;
    public double Flaps;
    public double BankDeg, PitchDeg;          // starting attitude (0/0 = trimmed straight and level, or the glide for a glider)
    public double? Power;                     // 0..1 throttle at the start (null = what level flight takes)
    public int FailEngine = -1;               // multi: this engine is dead from the start (0 = left)
}

/// <summary>
/// The instructor's hands: control laws the demonstration pilot (and the game's half of a part-task drill) flies with.
/// Sim conventions: +aileron rolls right, +elevator = nose DOWN (stick forward), +rudder = nose right, lever −1 = full
/// power … +1 = idle. The pitch gains are scaled by <see cref="ElevatorPower"/> (a tail with twice the power gets half).
/// </summary>
public sealed class Laws
{
    public double ElevTrim, ElevatorPower = 1, Thr0 = 0.5;
    private double _hInt, _vInt, _gInt, _thrInt, _ailCmd;
    public void Reset() { _hInt = _vInt = _gInt = _thrInt = 0; }

    /// <summary>Aileron to reach a bank (deg) at no more than <paramref name="maxRateDegS"/>, like a hand would.</summary>
    public double Bank(FlightNow f, double bankDeg, double maxRateDegS = 30, double handRate = 2.5)
    {
        double rateCmd = Math.Clamp(2.0 * (bankDeg * FlightNow.D2R - f.Roll), -maxRateDegS * FlightNow.D2R, maxRateDegS * FlightNow.D2R);
        double want = Math.Clamp(2.0 * (rateCmd - f.P), -1, 1);
        _ailCmd += Math.Clamp(want - _ailCmd, -handRate * f.Dt, handRate * f.Dt);
        return _ailCmd;
    }

    /// <summary>A steady roll rate (deg/s) — rolls, rolling reversals.</summary>
    public double RollRate(FlightNow f, double rateDegS)
    {
        double want = Math.Clamp(Math.Sign(rateDegS) * 0.6 + 2.0 * (rateDegS * FlightNow.D2R - f.P), -1, 1);
        _ailCmd += Math.Clamp(want - _ailCmd, -4 * f.Dt, 4 * f.Dt);
        return _ailCmd;
    }

    /// <summary>Rudder: ball centred — sideslip and the yaw rate the turn doesn't need, with the aileron against adverse yaw.</summary>
    public double Ball(FlightNow f, double ail)
    {
        double rTurn = f.Ias > 5 ? 9.81 * Math.Sin(f.Roll) * Math.Cos(f.Pitch) / f.Ias : 0;
        return Math.Clamp(-1.5 * f.Beta - 0.5 * (f.R - rTurn) + 0.35 * ail, -1, 1);
    }

    private double Kva(FlightNow f) => Math.Clamp(Math.Pow(22.0 / Math.Max(f.Ias, 8.0), 1.5), 0.25, 1.0);   // elevator power grows with V²

    /// <summary>Elevator: hold a height (m AGL) through bank — the load-factor feed-forward of a turn included.</summary>
    public double Altitude(FlightNow f, double aglM, double lo = -0.9, double hi = 0.6)
    {
        double hErr = f.Agl - aglM;
        double bankFf = -0.3 * (1.0 / Math.Max(0.4, Math.Cos(f.Roll)) - 1.0);
        _hInt = Math.Clamp(_hInt + 0.03 * hErr * f.Dt, -0.4, 0.4);
        return Math.Clamp(ElevTrim + ElevatorPower * (Kva(f) * (bankFf + 0.08 * hErr + 0.25 * f.Hdot) + _hInt) + 0.5 * f.Q, lo, hi);
    }

    /// <summary>Elevator: hold a vertical speed (m/s, + = up).</summary>
    public double ClimbRate(FlightNow f, double hdot)
    {
        double bankFf = -0.3 * (1.0 / Math.Max(0.4, Math.Cos(f.Roll)) - 1.0);
        _hInt = Math.Clamp(_hInt + 0.02 * (f.Hdot - hdot) * f.Dt, -0.4, 0.4);
        return Math.Clamp(ElevTrim + ElevatorPower * (Kva(f) * (bankFf + 0.25 * (f.Hdot - hdot)) + _hInt) + 0.5 * f.Q, -0.9, 0.6);
    }

    /// <summary>Elevator: pitch for a speed (the glider's and the climb's law).</summary>
    public double Speed(FlightNow f, double vMs)
    {
        double bankFf = -0.3 * (1.0 / Math.Max(0.4, Math.Cos(f.Roll)) - 1.0);
        _vInt = Math.Clamp(_vInt - 0.004 * (vMs - f.Ias) * f.Dt, -0.3, 0.3);
        return Math.Clamp(ElevTrim + ElevatorPower * Kva(f) * bankFf - 0.035 * (vMs - f.Ias) * ElevatorPower + _vInt + 0.6 * f.Q, -0.9, 0.6);
    }

    /// <summary>Elevator: hold a pitch attitude (rad).</summary>
    public double PitchTo(FlightNow f, double thetaRad, double gain = 2.0)
        => Math.Clamp(ElevTrim + ElevatorPower * gain * (f.Pitch - thetaRad) + 0.6 * f.Q, -1, 1);

    /// <summary>Elevator: pull a load factor (g) — loops, pull-outs.</summary>
    public double LoadFactor(FlightNow f, double g)
    {
        _gInt = Math.Clamp(_gInt + 0.08 * (f.G - g) * f.Dt, -1.2, 0.6);
        return Math.Clamp(ElevTrim + ElevatorPower * (_gInt + 0.15 * (f.G - g)) + 0.3 * f.Q, -1, 1);
    }

    /// <summary>Throttle lever for a speed (powered); the glider's lever is its spoilers (kept closed).</summary>
    public double PowerForSpeed(FlightNow f, double vMs, bool bankFeedForward = true)
    {
        if (f.Glider) return 0;
        double vErr = vMs - f.Ias;
        double ff = bankFeedForward ? 0.6 * Thr0 * (1.0 / Math.Max(0.4, Math.Cos(f.Roll)) - 1.0) : 0;   // a 45° bank needs ~40 % more power
        _thrInt = Math.Clamp(_thrInt + 0.04 * vErr * f.Dt, -0.5, 0.5);
        return Lever(Math.Clamp(Thr0 + ff + 0.15 * vErr + _thrInt, 0, 1));
    }

    /// <summary>Throttle fraction (0 idle … 1 full) → the sim's lever (−1 full … +1 idle). A glider: spoilers closed.</summary>
    public static double Lever(double power01) => 1 - 2 * Math.Clamp(power01, 0, 1);
    /// <summary>Idle / full power — for a glider the lever is the spoilers, so both are "closed" (0).</summary>
    public static double Idle(FlightNow f) => f.Glider ? 0 : 1;
    public static double Full(FlightNow f) => f.Glider ? 0 : -1;
    public static double Power(FlightNow f, double power01) => f.Glider ? 0 : Lever(power01);
    /// <summary>Throttle lever for a height (slow flight: pitch for the speed, power for the altitude).</summary>
    public double PowerForAltitude(FlightNow f, double aglM)
    {
        if (f.Glider) return 0;
        double hErr = f.Agl - aglM;
        _thrInt = Math.Clamp(_thrInt - 0.01 * hErr * f.Dt, -0.5, 0.5);
        return Lever(Math.Clamp(Thr0 - 0.03 * hErr - 0.12 * f.Hdot + _thrInt, 0, 1));
    }
}

/// <summary>
/// One syllabus manoeuvre (owner 2026-10-03: "break down the components as needed … a lesson at the beginning to thoroughly
/// explain the objective, the standards measured against, a demonstration, a score and feedback"). Each step the scenario
/// calls <see cref="Advance"/> (the manoeuvre's phases move on the AIRCRAFT's state, whoever is flying), then
/// <see cref="Fly"/> (the instructor's inputs on every axis: the demonstration, and the game's half of a part-task drill),
/// then <see cref="Judge"/> while live. The user's axes replace the instructor's.
/// </summary>
public abstract class Maneuver
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract Course Course { get; }
    /// <summary>One sentence: what you are doing and how you earn points (top of the briefing).</summary>
    public abstract string Goal { get; }
    /// <summary>The spoken card: the procedure, step by step.</summary>
    public abstract string Instructions { get; }
    /// <summary>Illustrated explanation pages (title, text) before the card.</summary>
    public virtual (string title, string text)[] Pages => Array.Empty<(string, string)>();
    /// <summary>The standards: live criteria (the orb takes the worst) and judged moments — built once per run, for this
    /// aircraft (a glider is judged on speed where a powered aircraft is judged on altitude).</summary>
    protected abstract LessonRules BuildRules(AircraftConfig c);
    public LessonRules Rules(AircraftConfig c) => _rules ??= BuildRules(c);
    private LessonRules? _rules;
    /// <summary>bit 0 aileron, 1 elevator, 2 rudder, 3 throttle — what the student flies by default.</summary>
    public virtual int DefaultUserAxes => 0b1111;
    /// <summary>The aircraft the course flies this in (the menu offers it; any type can try).</summary>
    public virtual string? Aircraft => null;
    public virtual ManeuverSetup Setup(FlightNow f) => new();
    /// <summary>A run longer than this ends (s, live).</summary>
    public virtual double TimeLimitSec => 240;

    /// <summary>What to do right now, as the instructor would say it ("Roll into 45° left").</summary>
    public string Cue { get; protected set; } = "";
    /// <summary>A new cue this step (the host speaks it in the demonstration).</summary>
    public bool CueChanged { get; private set; }
    private string _lastCue = "";
    public bool Done { get; protected set; }
    /// <summary>Multi: the engine that is failed right now (−1 none), and whether it has been feathered.</summary>
    public int EngineOut { get; protected set; } = -1;
    public bool Feathered { get; protected set; }
    public string Verdict { get; protected set; } = "";
    /// <summary>Lesson-specific notes for the debrief (numbers the judge doesn't carry).</summary>
    public readonly List<string> Notes = new();

    public void BeginStep() { CueChanged = Cue != _lastCue; _lastCue = Cue; }
    public void Say(string cue) { Cue = cue; }

    /// <summary>Move the manoeuvre's phase on from the aircraft's state.</summary>
    public abstract void Advance(FlightNow f);
    /// <summary>The instructor's inputs on every axis this step.</summary>
    public abstract ControlInputs Fly(FlightNow f, Laws L);
    /// <summary>Grade the live criteria through <paramref name="W"/>; post moments to the judge.</summary>
    public abstract void Judge(FlightNow f, LessonJudge j, Action<Criterion, double> W);

    /// <summary>Called once when the run ends: the score (0–100) from the judge's time-in-grade and moments.</summary>
    public virtual double Score(LessonJudge j)
    {
        double total = j.Seconds.Sum();
        double live = total > 0 ? (j.Seconds[0] + 0.6 * j.Seconds[1] + 0.2 * j.Seconds[2]) / total : 1;
        double mom = 1;
        if (j.Moments.Count > 0) mom = j.Moments.Average(m => m.grade switch { Grade.Green => 1.0, Grade.Yellow => 0.7, Grade.Orange => 0.35, _ => 0.0 });
        double s = j.Rules.Moments.Count > 0 ? 100 * (0.5 * live + 0.5 * mom) : 100 * live;
        if (string.IsNullOrEmpty(Verdict)) Verdict = s >= 90 ? "To the standard." : s >= 70 ? "Close to the standard — see the debrief." : "Below the standard — watch the demonstration again.";
        return Math.Clamp(s, 0, 100);
    }

    // ---- helpers for the manoeuvres ----
    protected static double Clamp(double v, double lo, double hi) => Math.Clamp(v, lo, hi);
    protected static double Wrap180(double d) { while (d > 180) d -= 360; while (d < -180) d += 360; return d; }
}

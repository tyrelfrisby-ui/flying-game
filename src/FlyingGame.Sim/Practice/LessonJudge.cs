using FlyingGame.Core;

namespace FlyingGame.Sim.Practice;

/// <summary>Owner 2026-10-03: every lesson is a points run. One ORB shows how you are doing right now — no hint which way
/// you are off: GREEN = on the ideal, earning at the top rate; YELLOW = close, earning less; ORANGE = not good, earning
/// nothing; RED = bad, losing points. Landings and stalls also score their moments (touchdown sink, speed, point, …).</summary>
public enum Grade { Green, Yellow, Orange, Red }

/// <summary>One judged quantity: the ideal, and the error that still counts as green / yellow / orange (beyond = red).</summary>
public sealed class Criterion
{
    public string Name = "", Ideal = "", Unit = "";
    public double Green, Yellow, Orange;
    public string Why = "";
    public Criterion(string name, string ideal, string unit, double g, double y, double o, string why = "") { Name = name; Ideal = ideal; Unit = unit; Green = g; Yellow = y; Orange = o; Why = why; }
    /// <summary>Grade an error magnitude (in this criterion's unit).</summary>
    public Grade Rate(double err) { err = System.Math.Abs(err); return err <= Green ? Grade.Green : err <= Yellow ? Grade.Yellow : err <= Orange ? Grade.Orange : Grade.Red; }
    public string Bands => $"≤{Green:0.#} · ≤{Yellow:0.#} · ≤{Orange:0.#} · more {Unit}";
}

/// <summary>What a lesson is judged on: its goal, the live criteria (the orb takes the WORST of them) and the moments.</summary>
public sealed class LessonRules
{
    public string Goal = "";
    public readonly List<Criterion> Live = new();
    public readonly List<Criterion> Moments = new();
}

public sealed class LessonJudge
{
    /// <summary>Points per second by grade, and points for a judged moment.</summary>
    public static readonly double[] RatePerSec = { 10, 4, 0, -6 };
    public static readonly double[] MomentPoints = { 100, 50, 0, -50 };
    public const double CrashPoints = -300;

    public LessonRules Rules { get; }
    public Grade Current { get; private set; } = Grade.Green;
    public double Points { get; private set; }
    public readonly double[] Seconds = new double[4];
    public readonly List<(string name, Grade grade, string detail, double points)> Moments = new();

    // ---- the debrief's raw material (owner 2026-10-03: no metrics live — a thorough review afterwards) ----
    /// <summary>One 10 Hz sample: time, the live grade, each live criterion's error (its own unit), and the flight state.</summary>
    public readonly struct Sample
    {
        public readonly double T; public readonly Grade G; public readonly double[] Err;
        public readonly double Along, Cross, Height, Ias, Sink, Bank, Pitch;
        public Sample(double t, Grade g, double[] err, double along, double cross, double height, double ias, double sink, double bank, double pitch)
        { T = t; G = g; Err = err; Along = along; Cross = cross; Height = height; Ias = ias; Sink = sink; Bank = bank; Pitch = pitch; }
    }
    public readonly List<Sample> Timeline = new();
    /// <summary>Everything worth a comment, in time order: judged moments AND the first slide into red (not scored).</summary>
    public readonly List<(double t, string title, Grade grade, string comment)> Events = new();
    public double Now { get; set; }
    private readonly Dictionary<string, double> _lastRedNote = new();

    public void AddSample(Grade g, double[] err, double along, double cross, double height, double ias, double sink, double bank, double pitch)
    {
        if (Timeline.Count > 0 && Now - Timeline[^1].T < 0.1) return;
        Timeline.Add(new Sample(Now, g, err, along, cross, height, ias, sink, bank, pitch));
    }

    /// <summary>Note a slide into RED on a live criterion (once per criterion per 6 s) — for the replay's commentary.</summary>
    public void NoteRed(Criterion c, double err)
    {
        if (_lastRedNote.TryGetValue(c.Name, out double last) && Now - last < 6) return;
        _lastRedNote[c.Name] = Now;
        Events.Add((Now, $"{c.Name}: red", Grade.Red, Comment(c, Grade.Red, $"{System.Math.Abs(err):0.#} {c.Unit}")));
    }

    /// <summary>The instructor's line for a criterion at a grade.</summary>
    public static string Comment(Criterion c, Grade g, string detail)
    {
        string n = c.Name;
        if (g == Grade.Green) return $"{n}: {detail} — that's the standard. Nicely done.";
        string fix =
            n.StartsWith("Nose straight") || n.StartsWith("Aligned") ? "Use the rudder to keep the nose pointed straight down the runway — small, early inputs, and keep them in as the wind pushes." :
            n.StartsWith("Over the centreline") || n.StartsWith("On the centreline") ? "Lower the upwind wing just enough to stop the drift, and hold it — more bank as the speed bleeds off." :
            n.StartsWith("Wings level") ? "Small aileron, early: catch the bank before it builds, then neutralise." :
            n.StartsWith("Coordination") ? "Lead the roll with rudder in the same direction — the adverse yaw shows up the moment the aileron goes in." :
            n.StartsWith("Stop at 45") ? "Start taking the aileron out a few degrees before 45° so it stops right on it." :
            n.StartsWith("Wing drop") ? "Opposite rudder, promptly — and keep the ailerons out of it until the wing is flying again." :
            n.StartsWith("Time stalled") || n.StartsWith("Altitude lost") ? "At the break, lower the nose to unload the wing at once — a smaller, quicker push costs less height." :
            n.StartsWith("Airspeed") ? "Pitch for the speed: set the attitude, wait for it to settle, trim, then adjust — chase attitude, not the needle." :
            n.StartsWith("Altitude on") ? "Level off with pitch, then power: lead the level-off by about 10 % of your climb rate." :
            n.StartsWith("On the glide path") ? "High: less power and nose down a touch. Low: add power — never just pull." :
            n.StartsWith("Flare") ? "Ease back progressively as it settles: the sink should shrink with the height — don't let it balloon or drop in." :
            n.StartsWith("Touchdown sink") ? "Hold it off a little longer: keep easing back so it settles the last foot gently." :
            n.StartsWith("Touchdown speed") ? "Too fast at touchdown — hold it off until the speed bleeds toward the stall; it will settle by itself." :
            n.StartsWith("Touchdown point") ? "Fly the aim point on the approach, then round out and hold off — floating far comes from arriving fast." :
            n.StartsWith("Three-point") ? "Keep raising the nose in the hold-off until it sits in the three-point attitude as it touches." :
            n.StartsWith("Nose-high") ? "Mains first: keep the nose coming up through the hold-off — never let the nosewheel touch first." :
            n.StartsWith("Bounces") ? "It bounced: the wheels arrived with sink or extra speed. Hold it off just above the runway until it settles by itself." :
            n.StartsWith("Round-out") ? "Begin the round-out about 10 to 20 feet up — a smooth change from the descent to the hold-off." :
            "Work toward the ideal shown on the briefing.";
        string how = g == Grade.Yellow ? "close" : g == Grade.Orange ? "not good" : "well off";
        return $"{n}: {detail} — {how}. {fix}";
    }
    /// <summary>Which live criterion set the orb this instant (for the results breakdown, not shown live).</summary>
    public string Limiting { get; private set; } = "";

    public LessonJudge(LessonRules rules) { Rules = rules; }

    public void Tick(Grade g, double dt, string limiting = "")
    {
        Current = g; Limiting = limiting;
        Seconds[(int)g] += dt;
        Points += RatePerSec[(int)g] * dt;
    }

    public Grade Moment(Criterion c, double err, string detail)
    {
        Grade g = c.Rate(err);
        double p = MomentPoints[(int)g];
        Points += p;
        Moments.Add((c.Name, g, detail, p));
        Events.Add((Now, c.Name, g, Comment(c, g, detail)));
        return g;
    }

    public void Crash(string what)
    {
        Points += CrashPoints; Moments.Add((what, Grade.Red, "", CrashPoints)); Current = Grade.Red;
        Events.Add((Now, what, Grade.Red, $"{what}. The run ends here — next time, more height and less sink before the ground arrives."));
    }

    // ---- the judging matrix --------------------------------------------------------------------------------------
    // Standards: FAA Airman Certification Standards (Private / Commercial) where one exists — altitude ±100 ft, airspeed
    // ±10 kt (Vx/Vy +10/−5), heading ±10°, touchdown within 400 ft (private) / 200 ft (commercial) beyond a point, no drift,
    // axis aligned with the runway, coordinated (ball centred), stall recovery with minimum altitude loss; the rest from
    // the Airplane Flying Handbook (round-out 10–20 ft, touchdown at minimum controllable speed, mains first).
    public static class Std
    {
        public static Criterion Alignment => new("Nose straight down the runway", "0°", "°", 2, 4, 7, "In a crosswind the rudder keeps the fuselage parallel to the runway (no crab at touchdown: ACS 'no drift, longitudinal axis aligned').");
        public static Criterion Centreline => new("Over the centreline", "0 ft", "ft", 5, 10, 20, "Aileron into the wind holds you over the centreline (a sideslip): ACS 'no drift'.");
        public static Criterion WingsLevel => new("Wings level", "0° bank", "°", 2, 5, 10, "Bank makes the lift lean; the slip that follows weathervanes the nose into a turn.");
        public static Criterion Ball => new("Coordination (ball centred)", "0° slip", "°", 2, 4, 7, "Rudder with aileron: no slip or skid in the roll (ACS: coordinated flight).");
        public static Criterion BankStop => new("Stop at 45° of bank", "45°", "° over", 3, 6, 10, "Roll briskly and stop exactly on 45° — no overshoot.");
        public static Criterion StallBank => new("Wing drop held with rudder", "0° bank", "°", 5, 10, 20, "Ailerons at the stall make it worse; opposite rudder stops the wing drop (ACS ±10° straight-ahead stalls).");
        public static Criterion StallTime => new("Time stalled before the break", "≤ 1 s", "s", 1.0, 2.0, 3.0, "At the break, lower the angle of attack at once.");
        public static Criterion Speed(string what, double g = 3, double y = 5, double o = 10) => new($"Airspeed ({what})", what, "kt", g, y, o, "ACS: airspeed ±10 kt; inside 3 kt is green.");
        public static Criterion Altitude => new("Altitude on the level leg", "assigned", "ft", 30, 60, 100, "ACS: ±100 ft; inside 30 ft is green.");
        public static Criterion Glideslope => new("On the glide path", "on the slope", "° off", 0.25, 0.5, 1.0, "Pitch for the path, power for the speed (a dot on a PAPI is ~0.2°).");
        public static Criterion FlareSink => new("Flare: sink easing off with height", "sink ≈ height ÷ 5 s", "fpm off", 60, 120, 200, "Round out 10–20 ft up, then hold it off: the sink should shrink with the height, never balloon.");
        public static Criterion TdSink => new("Touchdown sink rate", "≤ 150 fpm", "fpm", 150, 250, 400, "A smooth arrival is under ~150 fpm; ~400 fpm and over is a hard landing.");
        public static Criterion TdSpeed => new("Touchdown speed", "≈ Vso (stall)", "% over Vso", 10, 20, 30, "AFH: touch down at minimum controllable airspeed — at or just above the stall.");
        public static Criterion TdPoint => new("Touchdown point", "330 ft past the aim point", "ft past", 200, 400, 600, "ACS: within 200 ft (commercial) / 400 ft (private) beyond the chosen point — 330 ft past where the glide path meets the runway (the numbers for the flare lesson); short is orange.");
        public static Criterion TdAlign => new("Aligned at touchdown", "0°", "°", 2, 4, 7, "No crab: the wheels must be rolling the way the airplane is going.");
        public static Criterion TdCentre => new("On the centreline at touchdown", "0 ft", "ft", 5, 10, 20, "ACS: on the centreline, no drift.");
        public static Criterion TdAttitude(bool taildragger) => taildragger
            ? new("Three-point attitude at touchdown", "tail on the stance", "°", 2, 4, 7, "Mains and tailwheel together at minimum speed.")
            : new("Nose-high at touchdown", "mains first", "° low", 0, 2, 4, "Mains first, nose wheel held off (pitch at least a few degrees up).");
        public static Criterion Bounce => new("Bounces", "none", "ft", 0.5, 2, 5, "One arrival: a bounce means it touched with too much sink or too fast — hold it off longer.");
        public static Criterion NoseWheelFirst => new("Nose wheel first", "mains first", "", 0, 0, 0.5, "The nose wheel took the first touch: flat and fast, it bounces the nose and porpoises. Hold the nose up — mains first.");
        public static Criterion RoundOut => new("Round-out height", "10–20 ft", "ft off", 0, 5, 12, "AFH: begin the round-out about 10–20 ft up (no round-out = red).");
        public static Criterion StallLoss => new("Altitude lost in the recovery", "≤ 100 ft", "ft", 100, 150, 250, "ACS: recover with the minimum loss of altitude.");
        public static Criterion Result => new("Result against the best possible", "100 %", "% short", 5, 12, 25, "How close your speed control came to the book number.");
    }

    public static LessonRules For(PracticeKind kind, bool taildragger)
    {
        var r = new LessonRules();
        switch (kind)
        {
            case PracticeKind.CrosswindRudder:
                r.Goal = "Five feet over the runway in a crosswind. The game flies stick and power; YOU keep the fuselage parallel to the runway with RUDDER.";
                r.Live.Add(Std.Alignment); break;
            case PracticeKind.CrosswindAileron:
                r.Goal = "Five feet over the runway in a crosswind. The game flies rudder, elevator and power; YOU hold the centreline with AILERON (a sideslip).";
                r.Live.Add(Std.Centreline); break;
            case PracticeKind.LandingRudder:
                r.Goal = "A crosswind landing: you have the RUDDER from the approach to the end of the roll-out — no crab when the wheels touch.";
                r.Live.Add(Std.Alignment); r.Moments.Add(Std.TdAlign); r.Moments.Add(Std.Bounce); break;
            case PracticeKind.LandingAileron:
                r.Goal = "A crosswind landing: you have the AILERON — hold the centreline down to the touchdown, wing low into the wind.";
                r.Live.Add(Std.Centreline); r.Moments.Add(Std.TdCentre); r.Moments.Add(Std.Bounce); break;
            case PracticeKind.Flare:
            case PracticeKind.FlareSideView:
                r.Goal = "At idle on the power-off glide, aimed at the runway numbers from 100 ft. You have the ELEVATOR: round out 10–20 ft up, then hold it off until it settles at minimum speed.";
                r.Live.Add(Std.FlareSink);
                r.Moments.AddRange(new[] { Std.RoundOut, Std.TdSink, Std.Bounce, Std.TdSpeed, Std.TdPoint, Std.TdAttitude(taildragger) }); break;
            case PracticeKind.ApproachSideView:
                r.Goal = "Fly the glide path to the aim point: PITCH for the path, POWER for the speed (1.3 Vso), then round out and land.";
                r.Live.Add(Std.Glideslope); r.Live.Add(Std.Speed("1.3 Vso"));
                r.Moments.AddRange(new[] { Std.RoundOut, Std.TdSink, Std.Bounce, Std.TdSpeed, Std.TdPoint, Std.TdAttitude(taildragger) }); break;
            case PracticeKind.STurns:
                r.Goal = "The game rolls 45° to 45° and back. YOU have the RUDDER: keep the ball centred through every roll (adverse yaw!).";
                r.Live.Add(Std.Ball); break;
            case PracticeKind.STurnsTest:
                r.Goal = "One speed cycle of 45°-to-45° rolls with aileron AND rudder: crisp rolls, stop on 45°, ball centred.";
                r.Live.Add(Std.Ball); r.Live.Add(Std.BankStop); break;
            case PracticeKind.Straight:
                r.Goal = "Keep the wings level with AILERON. The rudder is locked neutral, so any bank slips and the nose wanders.";
                r.Live.Add(Std.WingsLevel); break;
            case PracticeKind.StallSideView:
            case PracticeKind.StallElevator:
                r.Goal = "Power off: ease the nose up until it stalls, then break the stall at once — angle of attack first — and recover with as little height lost as you can.";
                r.Live.Add(Std.StallTime); r.Moments.Add(Std.StallLoss); break;
            case PracticeKind.StallRudder:
                r.Goal = "The game stalls it again and again. YOU have the RUDDER: stop every wing drop (no aileron near the stall).";
                r.Live.Add(Std.StallBank); break;
            case PracticeKind.ClimbLevelDescend:
                r.Goal = "Climb at Vy (full power), level off on the assigned altitude (75 %), descend at cruise speed (50 %): pitch for speed, power for the leg.";
                r.Live.Add(Std.Speed("the leg's target")); r.Live.Add(Std.Altitude); break;
            case PracticeKind.GlideRear:
            case PracticeKind.GlideSide:
                r.Goal = "Power off: hold the speed to fly for this wind — the most distance over the ground for the height you lose.";
                r.Live.Add(Std.Speed("speed to fly", 2, 4, 8)); r.Moments.Add(Std.Result); break;
            case PracticeKind.ClimbVyRear:
                r.Goal = "Full power: hold Vy, the best RATE of climb — 1,000 ft in the least time.";
                r.Live.Add(Std.Speed("Vy", 2, 4, 8)); r.Moments.Add(Std.Result); break;
            case PracticeKind.ClimbVxSide:
                r.Goal = "Full power: hold Vx, the best ANGLE of climb — 1,000 ft over the least ground.";
                r.Live.Add(Std.Speed("Vx", 2, 4, 8)); r.Moments.Add(Std.Result); break;
        }
        return r;
    }

    public static Grade Worst(Grade a, Grade b) => (Grade)System.Math.Max((int)a, (int)b);
}

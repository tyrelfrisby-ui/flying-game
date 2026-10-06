using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Practice;

namespace FlyingGame.Sim.Syllabus;

/// <summary>How the aircraft is flown on a leg: level (altitude with elevator, speed with power), a full-power climb at
/// Vy, a reduced-power descent, or a glide (speed with elevator, power off — every leg is a glide in a glider).</summary>
public enum LegMode { Level, Climb, Descend, Glide }

/// <summary>One leg of a turn sequence: straight for a time, or a heading change at a bank.</summary>
public sealed class Leg
{
    public readonly string Cue; public readonly double TurnDeg, BankDeg, Seconds; public readonly LegMode Mode;
    public Leg(string cue, double turnDeg, double bankDeg, double seconds, LegMode mode = LegMode.Level) { Cue = cue; TurnDeg = turnDeg; BankDeg = bankDeg; Seconds = seconds; Mode = mode; }
}

/// <summary>
/// A sequence of straight legs and turns — turns to headings, climbing and descending turns, gliding turns, steep turns,
/// the steep spiral, standard-rate turns. Roll-out begins half the bank in degrees before the heading (the rule of thumb);
/// the roll-out heading is judged the moment the wings come level.
/// </summary>
public abstract class TurnSequence : Maneuver
{
    protected abstract IReadOnlyList<Leg> Legs { get; }
    protected virtual double SpeedFor(FlightNow f, LegMode m) => m switch
    {
        LegMode.Climb => f.Vy,
        LegMode.Descend => 1.15 * f.Vy,
        LegMode.Glide => f.BestGlide,
        _ => _vLevel,
    };
    protected virtual double MaxRollRate => 20;
    /// <summary>Glider: every leg is a glide.</summary>
    protected LegMode ModeOf(FlightNow f, Leg l) => f.Glider ? LegMode.Glide : l.Mode;

    protected Criterion? CBank, CAlt, CSpeed, CRollout, CBall;
    private int _i = -1;
    private double _legT0, _legTurned0, _hRef, _vLevel, _pendingRollout = double.NaN, _bankReachedT = -1;
    protected int LegIndex => _i;
    protected Leg? Current => _i >= 0 && _i < Legs.Count ? Legs[_i] : null;

    protected override LessonRules BuildRules(AircraftConfig c)
    {
        var r = new LessonRules { Goal = Goal };
        bool glider = c.Propulsion is null;
        CBank = BankCriterion(); r.Live.Add(CBank);
        if (!glider && JudgeAltitude) { CAlt = SylStd.Altitude(AltitudeAcsFt); r.Live.Add(CAlt); }
        if (glider || JudgeSpeed) { CSpeed = SylStd.Airspeed(SpeedName(glider), SpeedAcsKt); r.Live.Add(CSpeed); }
        if (JudgeBall) { CBall = SylStd.Ball; r.Live.Add(CBall); }
        CRollout = SylStd.Rollout(RolloutAcs); r.Moments.Add(CRollout);
        ExtraRules(r, c);
        return r;
    }
    protected virtual Criterion BankCriterion() => SylStd.BankHeld(5);
    protected virtual bool JudgeAltitude => Legs.All(l => l.Mode == LegMode.Level);
    protected virtual bool JudgeSpeed => !JudgeAltitude;
    protected virtual bool JudgeBall => true;
    protected virtual double AltitudeAcsFt => 100;
    protected virtual double SpeedAcsKt => 10;
    protected virtual double RolloutAcs => 10;
    protected virtual string SpeedName(bool glider) => glider ? "the glide speed" : "the leg's speed";
    protected virtual void ExtraRules(LessonRules r, AircraftConfig c) { }

    public override void Advance(FlightNow f)
    {
        if (Done) return;
        if (_i < 0) { _vLevel = f.Ias; _hRef = f.StartAgl; Next(f, f.TurnedDeg); }
        Leg l = Legs[_i];
        if (l.TurnDeg == 0) { if (f.T - _legT0 >= l.Seconds) Next(f, _legTurned0); return; }
        double target = _legTurned0 + l.TurnDeg;
        double remaining = (target - f.TurnedDeg) * Math.Sign(l.TurnDeg);
        if (remaining <= Math.Abs(l.BankDeg) * 0.5) { _pendingRollout = target; Next(f, target); }
    }

    private void Next(FlightNow f, double turned0)
    {
        LegMode prev = _i >= 0 ? ModeOf(f, Legs[_i]) : LegMode.Level;
        _i++; _legT0 = f.T; _legTurned0 = turned0; _bankReachedT = -1;
        if (_i >= Legs.Count) { Done = true; Say("Done."); return; }
        if (ModeOf(f, Legs[_i]) == LegMode.Level && prev != LegMode.Level) _hRef = f.Agl;   // level off where we are
        Say(Legs[_i].Cue);
        OnLeg(f, _i);
    }
    protected virtual void OnLeg(FlightNow f, int i) { }

    protected double BankTarget(FlightNow f)
    {
        Leg? l = Current;
        if (l == null) return 0;
        if (l.TurnDeg == 0) return Clamp(2.0 * (_legTurned0 - f.TurnedDeg), -10, 10);   // straight: hold the heading
        return Math.Sign(l.TurnDeg) * Math.Abs(l.BankDeg);
    }

    public override ControlInputs Fly(FlightNow f, Laws L)
    {
        Leg l = Current ?? Legs[^1];
        double ail = L.Bank(f, BankTarget(f), MaxRollRate);
        double rud = L.Ball(f, ail);
        LegMode m = ModeOf(f, l);
        double v = SpeedFor(f, m), ele, lever;
        switch (m)
        {
            case LegMode.Level: ele = L.Altitude(f, _hRef); lever = L.PowerForSpeed(f, v); break;
            case LegMode.Climb: ele = L.Speed(f, v); lever = Laws.Full(f); break;
            case LegMode.Descend: ele = L.Speed(f, v); lever = Laws.Power(f, 0.25 * L.Thr0 + 0.05); break;
            default: ele = L.Speed(f, v); lever = Laws.Idle(f); break;
        }
        return new ControlInputs(ail, ele, rud, lever);
    }

    public override void Judge(FlightNow f, LessonJudge j, Action<Criterion, double> W)
    {
        Leg? l = Current;
        if (l != null && l.TurnDeg != 0 && CBank != null)
        {
            if (_bankReachedT < 0 && Math.Abs(f.BankDeg) >= Math.Abs(l.BankDeg) - 3) _bankReachedT = f.T;
            double remaining = (_legTurned0 + l.TurnDeg - f.TurnedDeg) * Math.Sign(l.TurnDeg);
            if (_bankReachedT >= 0 && remaining > Math.Abs(l.BankDeg) * 0.5 + 5) W(CBank, Math.Abs(f.BankDeg) - Math.Abs(l.BankDeg));
        }
        if (CAlt != null) W(CAlt, (f.Agl - _hRef) * 3.28084);
        if (CSpeed != null && l != null) W(CSpeed, f.Kt(f.Ias - SpeedFor(f, ModeOf(f, l))));
        if (CBall != null) W(CBall, f.Beta * FlightNow.R2D);
        if (!double.IsNaN(_pendingRollout) && Math.Abs(f.BankDeg) < 5 && CRollout != null)
        {
            double e = f.TurnedDeg - _pendingRollout;
            j.Moment(CRollout, e, $"{Math.Abs(e):F0}° {(Math.Abs(e) < 0.5 ? "" : (e > 0 ? "past" : "short of"))} the heading".Replace("  ", " "));
            _pendingRollout = double.NaN;
        }
        JudgeExtra(f, j, W);
    }
    protected virtual void JudgeExtra(FlightNow f, LessonJudge j, Action<Criterion, double> W) { }
}

// ======================================================================================================================
// THE BASICS — the glider and private courses both start here (owner: "they are learning in the glider first").
// ======================================================================================================================

public sealed class TurnsToHeadings : TurnSequence
{
    private readonly Course _course;
    public TurnsToHeadings(Course c) { _course = c; }
    public override string Id => _course == Course.Glider ? "g-turns" : "p-turns";
    public override string Title => "Turns to a heading";
    public override Course Course => _course;
    public override string Goal => "Medium turns (30° of bank) onto headings: hold the bank, keep the ball centred, roll out on the heading — and in a powered aircraft hold the altitude.";
    public override string Instructions => "From straight and level, a turn of ninety degrees to the left, then one hundred and eighty to the right, both at thirty degrees of bank. Roll in with aileron and rudder together; once the bank is set, neutralise the aileron and hold a little back pressure: the lift is tilted, so it takes more of it to hold the altitude. Begin the roll-out half the bank angle before the heading — fifteen degrees early — with aileron and rudder together, and release the back pressure as the wings come level.";
    public override (string, string)[] Pages => new[]
    {
        ("Why a turn takes back pressure", "In a bank the lift tilts with the wings. Only its vertical part holds the aircraft up, so to hold altitude the wing must make more lift — a little more angle of attack, a little back pressure. At thirty degrees it is fifteen percent more; at sixty it is double."),
        ("Adverse yaw and the ball", "Rolling in, the rising wing's aileron goes down: more lift, but more drag too, and that drag pulls the nose the wrong way. Rudder in the direction of the roll cancels it. The ball shows the sideslip: step on the ball — rudder on the side it has rolled to."),
    };
    protected override IReadOnlyList<Leg> Legs { get; } = new[]
    {
        new Leg("Straight and level — clear the area", 0, 0, 4),
        new Leg("Turn LEFT 90° at 30° of bank", -90, 30, 0),
        new Leg("Roll out — straight and level", 0, 0, 5),
        new Leg("Turn RIGHT 180° at 30° of bank", 180, 30, 0),
        new Leg("Roll out — straight and level", 0, 0, 5),
    };
}

public sealed class ClimbingDescendingTurns : TurnSequence
{
    public override string Id => "p-climb-descend-turns";
    public override string Title => "Climbing and descending turns";
    public override Course Course => Course.Private;
    public override string Goal => "Climbing turn at Vy with full power, descending turn on reduced power, at 20° of bank: hold the bank and the speed, keep the ball centred, roll out on the heading.";
    public override string Instructions => "Full power and a climbing turn to the left, one hundred and eighty degrees at twenty degrees of bank, pitching for V Y. Then reduce the power and descend in a turn to the right, one hundred and eighty degrees at twenty degrees of bank, pitching for the descent speed. Level off at the end. In the climb, the slipstream and P factor want left rudder in a left turn less, and right rudder more: watch the ball.";
    protected override IReadOnlyList<Leg> Legs { get; } = new[]
    {
        new Leg("Straight and level", 0, 0, 3),
        new Leg("Full power — CLIMBING turn LEFT 180° at 20°, pitch for Vy", -180, 20, 0, LegMode.Climb),
        new Leg("Roll out, keep climbing", 0, 0, 3, LegMode.Climb),
        new Leg("Power back — DESCENDING turn RIGHT 180° at 20°", 180, 20, 0, LegMode.Descend),
        new Leg("Roll out and level off", 0, 0, 6),
    };
    protected override bool JudgeAltitude => false;
    protected override string SpeedName(bool glider) => "Vy climbing, descent speed descending";
}

public sealed class GlidingTurns : TurnSequence
{
    public override string Id => "g-gliding-turns";
    public override string Title => "Shallow, medium and steep gliding turns";
    public override Course Course => Course.Glider;
    public override string Goal => "Turns at 15°, 30° and 45° of bank at a constant airspeed: the nose must come DOWN as the bank steepens, or the speed bleeds away toward the stall.";
    public override string Instructions => "Hold the glide speed through three turns: shallow, fifteen degrees, ninety to the left; medium, thirty degrees, one hundred and eighty to the right; steep, forty five degrees, one hundred and eighty to the left. The steeper the bank, the faster the stall speed rises and the more the nose must be lowered to keep the speed: the yaw string stays straight with rudder.";
    public override (string, string)[] Pages => new[]
    {
        ("Stall speed rises in the turn", "The wing carries the load factor: at 45° of bank it carries 1.41 times the weight and stalls at 1.19 times the wings-level speed; at 60°, twice the weight and 1.41 times the speed. A glider flown at a wings-level speed in a steep turn is close to the stall — fly faster in steep turns."),
    };
    protected override IReadOnlyList<Leg> Legs { get; } = new[]
    {
        new Leg("Straight glide", 0, 0, 4, LegMode.Glide),
        new Leg("SHALLOW turn LEFT 90° at 15°", -90, 15, 0, LegMode.Glide),
        new Leg("Roll out", 0, 0, 3, LegMode.Glide),
        new Leg("MEDIUM turn RIGHT 180° at 30°", 180, 30, 0, LegMode.Glide),
        new Leg("Roll out", 0, 0, 3, LegMode.Glide),
        new Leg("STEEP turn LEFT 180° at 45° — nose down for the speed", -180, 45, 0, LegMode.Glide),
        new Leg("Roll out", 0, 0, 5, LegMode.Glide),
    };
    protected override double SpeedFor(FlightNow f, LegMode m) => 1.1 * f.BestGlide;
    protected override string SpeedName(bool glider) => "the glide speed";
}

public sealed class SteepTurns : TurnSequence
{
    private readonly Course _course; private readonly double _bank;
    public SteepTurns(Course course, double bank) { _course = course; _bank = bank; }
    public override string Id => _course switch { Course.Commercial => "c-steep-turns", Course.Atp => "a-steep-turns", Course.Glider => "g-steep-turns", _ => "p-steep-turns" };
    public override string Title => $"Steep turns ({_bank:0}°)";
    public override Course Course => _course;
    public override string? Aircraft => _course == Course.Atp ? "boeing-737-like" : null;
    public override string Goal => $"A 360° turn at {_bank:0}° of bank each way, back to back: hold the bank (±5°), the altitude (±100 ft) and the speed (±10 kt), and roll out on the entry heading (±10°).";
    public override string Instructions => $"At or below manoeuvring speed. Roll into {_bank:0} degrees of bank to the left, adding back pressure and a little power as the bank passes thirty degrees: the load factor at {_bank:0} degrees is {1 / Math.Cos(_bank * Math.PI / 180):0.0} g. Hold the bank and the altitude all the way around; the nose sits a little above the horizon. Begin the roll-out {_bank / 2:0} degrees before the entry heading and roll straight into the turn to the right, then roll out on the entry heading. If the nose drops, shallow the bank first, then raise the nose.";
    public override (string, string)[] Pages => new[]
    {
        ("Load factor", $"In a level turn the load factor is one over the cosine of the bank: {1 / Math.Cos(_bank * Math.PI / 180):0.00} g at {_bank:0}°. The stall speed rises with its square root — {Math.Sqrt(1 / Math.Cos(_bank * Math.PI / 180)):0.00} times the wings-level stall. That is why steep turns are flown below manoeuvring speed, with power added to hold the speed against the extra induced drag."),
        ("Overbanking tendency", "Past about thirty degrees the outer wing travels faster than the inner one and makes more lift: the bank wants to steepen. Hold a little opposite aileron once established. If the nose drops, raising it with elevator alone just tightens the spiral — shallow the bank first."),
    };
    protected override IReadOnlyList<Leg> Legs => _legs ??= new[]
    {
        new Leg("Straight and level — at or below Va", 0, 0, 4),
        new Leg($"Roll LEFT into {_bank:0}° — back pressure, add power", -360, _bank, 0),
        new Leg($"Roll straight into {_bank:0}° RIGHT", 360, _bank, 0),
        new Leg("Roll out on the entry heading", 0, 0, 5),
    };
    private Leg[]? _legs;
    protected override Criterion BankCriterion() => SylStd.Bank(_bank, 5);
    protected override double MaxRollRate => 25;
}

public sealed class SteepSpiral : TurnSequence
{
    public override string Id => "c-steep-spiral";
    public override string Title => "Steep spiral";
    public override Course Course => Course.Commercial;
    public override string Goal => "Three gliding 360° turns, power off, at a constant airspeed (±10 kt) and a steep bank — then roll out on the entry heading (±10°).";
    public override string Instructions => "Power to idle. Pitch for the glide speed, then roll into a steep spiral, about forty five degrees, and hold the airspeed through three full turns. Every so often clear the engine with a burst of power. Roll out on the entry heading.";
    protected override IReadOnlyList<Leg> Legs { get; } = new[]
    {
        new Leg("Power idle — establish the glide", 0, 0, 6, LegMode.Glide),
        new Leg("Roll into the spiral, three turns — hold the speed", -1080, 45, 0, LegMode.Glide),
        new Leg("Roll out on the entry heading", 0, 0, 5, LegMode.Glide),
    };
    public override ManeuverSetup Setup(FlightNow f) => new() { AglM = 1219, Speed = x => 1.15 * x.BestGlide, Power = 0 };
    protected override double SpeedFor(FlightNow f, LegMode m) => 1.15 * f.BestGlide * 1.1;
    protected override Criterion BankCriterion() => new("Bank (steep spiral)", "≈ 45°, ≤ 60°", "°", 5, 10, 15, "Constant radius in calm air: constant bank and constant speed.");
    protected override bool JudgeAltitude => false;
    protected override string SpeedName(bool glider) => "the spiral speed";
    public override double TimeLimitSec => 300;
}

public sealed class StandardRateTurn : TurnSequence
{
    public override string Id => "i-standard-rate";
    public override string Title => "Standard-rate turns";
    public override Course Course => Course.Instrument;
    public override string Goal => "A 360° turn each way at standard rate (3°/s — two minutes for the circle), holding the altitude (±100 ft) and airspeed (±10 kt), rolling out on the heading (±10°).";
    public override string Instructions => "Standard rate is three degrees a second. The bank for it is about the true airspeed in knots divided by ten, plus seven. Turn left through three hundred and sixty degrees, then right, holding the altitude on the instruments: the attitude indicator for the bank and pitch, the turn coordinator for the rate, the altimeter and the airspeed to confirm.";
    protected override IReadOnlyList<Leg> Legs => _legs ??= new[]
    {
        new Leg("Straight and level", 0, 0, 3),
        new Leg("Standard-rate turn LEFT 360°", -360, _bank, 0),
        new Leg("Roll out — straight and level", 0, 0, 4),
        new Leg("Standard-rate turn RIGHT 360°", 360, _bank, 0),
        new Leg("Roll out", 0, 0, 4),
    };
    private Leg[]? _legs; private double _bank = 15;
    protected override void OnLeg(FlightNow f, int i) { }
    public override void Advance(FlightNow f)
    {
        if (LegIndex < 0) { _bank = Math.Clamp(Math.Atan(3.0 * FlightNow.D2R * f.Ias / 9.81) * FlightNow.R2D, 5, 30); _legs = null; }
        base.Advance(f);
    }
    protected override Criterion BankCriterion() => SylStd.TurnRate;
    private Criterion? _rate;
    protected override void ExtraRules(LessonRules r, AircraftConfig c) { }
    public override double TimeLimitSec => 360;
    public override void Judge(FlightNow f, LessonJudge j, Action<Criterion, double> W)
    {
        // The bank criterion is replaced by the turn RATE in the turns.
        Leg? l = Current;
        if (l != null && l.TurnDeg != 0 && CBank != null && f.T > 0)
        {
            double remaining = (TurnTarget(l) - f.TurnedDeg) * Math.Sign(l.TurnDeg);
            if (Math.Abs(f.TurnRateDegS) > 2.0 && remaining > 15) W(CBank, Math.Abs(f.TurnRateDegS) - 3.0);
        }
        var bank = CBank; CBank = null;
        base.Judge(f, j, W);
        CBank = bank;
    }
    private double _turn0 = double.NaN; private int _lastLeg = -1;
    private double TurnTarget(Leg l) => l.TurnDeg;   // judged by rate only; the target is used for the 'remaining' gate
}

// ======================================================================================================================
// SLOW FLIGHT AND STALLS
// ======================================================================================================================

public sealed class SlowFlight : Maneuver
{
    private readonly Course _course;
    public SlowFlight(Course c) { _course = c; }
    public override string Id => _course == Course.Glider ? "g-slow-flight" : "p-slow-flight";
    public override string Title => "Slow flight";
    public override Course Course => _course;
    public override string Goal => "Fly at the edge of the stall — just above the stall warning — straight and in gentle turns: airspeed +10/−0 kt, altitude ±100 ft, heading ±10°, and no stall.";
    public override string Instructions => "Reduce the power and hold the altitude: the nose comes up as the speed bleeds off. At the slow-flight speed, add power to hold the altitude — slow flight is behind the power curve: pitch controls the speed, power controls the altitude. Hold it straight, then turn fifteen degrees left through ninety degrees and back to the right. The controls are mushy and need bigger movements; the left-turning tendencies need right rudder. Recover with full power, nose down to accelerate, and hold the altitude.";
    public override (string, string)[] Pages => new[]
    {
        ("Behind the power curve", "Slower than the speed for minimum drag, the induced drag grows faster than the parasite drag falls: flying slower takes MORE power. Pitch for the speed, power for the altitude — raise the nose to climb and you only slow down."),
    };
    private Criterion _speed = null!, _alt = null!, _hdg = null!, _bank = null!, _stall = null!;
    protected override LessonRules BuildRules(AircraftConfig c)
    {
        var r = new LessonRules { Goal = Goal };
        _speed = SylStd.SlowFlightSpeed; r.Live.Add(_speed);
        if (c.Propulsion is not null) { _alt = SylStd.Altitude(100); r.Live.Add(_alt); }
        _hdg = SylStd.Heading(10); r.Live.Add(_hdg);
        _bank = SylStd.Bank(15, 10); r.Live.Add(_bank);
        _stall = SylStd.NoStall; r.Moments.Add(_stall);
        return r;
    }
    private int _ph = -1; private double _t0, _hRef, _turn0, _vT, _stalledSec; private bool _judgedStall;
    private double Vt(FlightNow f) => _vT = 1.2 * f.Vs1;
    public override void Advance(FlightNow f)
    {
        if (Done) return;
        if (_ph < 0) { _ph = 0; _hRef = f.StartAgl; _turn0 = f.TurnedDeg; Say("Power back — hold the altitude, the nose comes up as it slows"); }
        Vt(f);
        switch (_ph)
        {
            case 0: if (f.Ias <= _vT + 1.0 || (f.Glider && f.T > 6)) Go(f, 1, "Slow flight — pitch for the speed, power for the altitude"); break;
            case 1: if (f.T - _t0 > 12) Go(f, 2, "Turn LEFT 90° at 15° of bank — right rudder, bigger inputs"); break;
            case 2: if (f.TurnedDeg - _turn0 <= -90 + 7) { _turn0 -= 90; Go(f, 3, "Turn RIGHT 90° at 15°"); } break;
            case 3: if (f.TurnedDeg - _turn0 >= 90 - 7) { _turn0 += 90; Go(f, 4, "Roll out — straight, slow"); } break;
            case 4: if (f.T - _t0 > 6) Go(f, 5, "Recover: full power, nose down to accelerate, hold the altitude"); break;
            case 5: if (f.Ias >= Math.Min(0.9 * f.Cruise, 1.6 * f.Vs1) || f.T - _t0 > 30) { Done = true; Say("Done."); } break;
        }
    }
    private void Go(FlightNow f, int ph, string cue) { _ph = ph; _t0 = f.T; Say(cue); }
    public override ControlInputs Fly(FlightNow f, Laws L)
    {
        double bank = _ph == 2 ? -15 : _ph == 3 ? 15 : Clamp(2.0 * (_turn0 - f.TurnedDeg), -8, 8);
        double ail = L.Bank(f, bank, 15);
        double rud = L.Ball(f, ail);
        double ele, lever;
        if (f.Glider) { ele = L.Speed(f, _ph == 5 ? 1.0 * f.BestGlide : _vT); lever = 0; }
        else if (_ph == 0) { ele = L.Altitude(f, _hRef); lever = Laws.Power(f, 0.08); }
        else if (_ph == 5) { ele = L.Altitude(f, _hRef); lever = Laws.Full(f); }
        else { if (_ph == 1 && f.T - _t0 < f.Dt * 1.5) L.Thr0 = 0.45; ele = L.Speed(f, _vT); lever = L.PowerForAltitude(f, _hRef); }
        return new ControlInputs(ail, ele, rud, lever);
    }
    public override void Judge(FlightNow f, LessonJudge j, Action<Criterion, double> W)
    {
        if (f.Stalled) _stalledSec += f.Dt;
        if (_ph >= 1 && _ph <= 4)
        {
            double kt = f.Kt(f.Ias - _vT);
            W(_speed, kt < 0 ? -3 * kt : Math.Max(0, kt - 0) );
            if (_ph is 1 or 4) W(_hdg, f.TurnedDeg - _turn0);
            if (_ph is 2 or 3 && Math.Abs(f.BankDeg) > 10) W(_bank, Math.Abs(f.BankDeg) - 15);
        }
        if (_alt != null && _ph >= 1) W(_alt, (f.Agl - _hRef) * 3.28084);
        if (Done && !_judgedStall) { _judgedStall = true; j.Moment(_stall, _stalledSec, _stalledSec < 0.05 ? "never stalled" : $"stalled for {_stalledSec:F1} s"); }
    }
}

/// <summary>Power-off (approach-to-landing) and power-on (departure) stalls; a glider's stall is power-off.</summary>
public sealed class StallRecovery : Maneuver
{
    private readonly Course _course; private readonly bool _powerOn;
    public StallRecovery(Course c, bool powerOn) { _course = c; _powerOn = powerOn; }
    public override string Id => (_course switch { Course.Glider => "g", Course.Atp => "a", _ => "p" }) + (_powerOn ? "-stall-power-on" : "-stall-power-off");
    public override string Title => _course == Course.Atp ? "Approach to stall — recovery" : _powerOn ? "Power-on stall (departure)" : _course == Course.Glider ? "Stall and recovery" : "Power-off stall (approach to landing)";
    public override Course Course => _course;
    public override string? Aircraft => _course == Course.Atp ? "boeing-737-like" : null;
    private bool Atp => _course == Course.Atp;
    public override string Goal => Atp
        ? "Slowing in level flight at idle: at the FIRST indication (buffet) recover — nose down to unload, wings level, thrust — with the least altitude lost."
        : _powerOn ? "Full power, nose up until it stalls; recover — angle of attack first — with the wings level and the least altitude lost, then climb."
        : "In the approach configuration at idle, raise the nose to the landing attitude and hold it until it stalls; recover — angle of attack first — wings level, least altitude lost, no secondary stall.";
    public override string Instructions => Atp
        ? "Idle thrust, hold the altitude and let the speed decay. At the first buffet: push to unload the wing until the buffet stops, roll the wings level, then advance the thrust and return to the altitude. Recovery is angle of attack first, always."
        : _powerOn ? "Full power. Raise the nose smoothly to about twenty degrees and hold it: the speed bleeds away and the left-turning tendencies grow — keep the ball centred with right rudder. At the break, lower the nose to reduce the angle of attack, level the wings with coordinated aileron and rudder, and climb away at V Y."
        : "Set the approach configuration and glide at idle. Level off and keep raising the nose toward the landing attitude, as in a flare that goes on too long. Keep the wings level with coordinated rudder and aileron. At the break, lower the nose to reduce the angle of attack, add full power, level the wings, and climb away — easing out of the descent so it doesn't stall again.";
    public override (string, string)[] Pages => new[]
    {
        ("A stall is angle of attack", "The wing stalls at its critical angle of attack, at any speed, any attitude, any power. The recovery is always the same first step: reduce the angle of attack. Power, wings level and the climb come after."),
        ("The wing drop", "Near the stall, one wing usually stalls first and drops. Aileron on that wing increases its angle of attack and deepens the stall. First unload the wing; then level it with coordinated controls."),
    };
    public override ManeuverSetup Setup(FlightNow f) => Atp
        ? new() { AglM = 1219, Speed = x => 1.45 * x.Vs1 }
        : _powerOn ? new() { AglM = 1066, Speed = x => 1.3 * x.Vs1 }
        : new() { AglM = 1066, Speed = x => 1.3 * x.Vso, Flaps = 1.0, Power = 0 };
    public override double TimeLimitSec => 120;

    private Criterion _wings = null!, _time = null!, _loss = null!, _second = null!, _ball = null!, _rec = null!;
    protected override LessonRules BuildRules(AircraftConfig c)
    {
        var r = new LessonRules { Goal = Goal };
        _wings = SylStd.WingsLevelAtStall; r.Live.Add(_wings);
        _ball = SylStd.Ball; r.Live.Add(_ball);
        _time = SylStd.StallBreakTime; r.Live.Add(_time);
        if (Atp) { _rec = SylStd.Recognition; r.Moments.Add(_rec); }
        _loss = SylStd.StallAltitudeLoss; r.Moments.Add(_loss);
        _second = SylStd.SecondaryStall; r.Moments.Add(_second);
        return r;
    }
    private int _ph = -1; private double _t0, _hRef, _onsetAgl = double.NaN, _minAgl, _stalledT, _thetaCmd, _warnT = -1;
    private bool _secondary, _lossJudged;
    private bool Indication(FlightNow f) => Atp ? f.StalledFraction > 0.04 : f.Stalled;
    public override void Advance(FlightNow f)
    {
        if (Done) return;
        if (_ph < 0) { _ph = 0; _hRef = f.StartAgl; _thetaCmd = f.Pitch; Say(Atp ? "Idle — hold the altitude, let it slow" : _powerOn ? "Full power — raise the nose to about 20°" : "Approach glide at idle"); _t0 = f.T; }
        switch (_ph)
        {
            case 0:
                if (!_powerOn && !Atp && f.T - _t0 > 5) Go(f, 1, "Level off — keep raising the nose to the landing attitude");
                else if (_powerOn || Atp) Go(f, 1, Cue);
                break;
            case 1:
                if (Indication(f)) { _onsetAgl = f.Agl; _minAgl = f.Agl; _warnT = f.T; Go(f, 2, Atp ? "Buffet — push to unload, wings level, thrust" : "STALL — nose down, full power, wings level"); }
                break;
            case 2:
                if (!f.Stalled && f.T - _t0 > 0.8 && f.Ias > 1.2 * (Atp ? f.Vs1 : _powerOn ? f.Vs1 : f.Vso)) Go(f, 3, Atp ? "Return to the altitude" : "Climb away at Vy — ease out, no secondary stall");
                break;
            case 3:
                if (f.T - _t0 > 4 && f.Hdot > 0.5) { Done = true; Say("Done."); }
                else if (f.T - _t0 > 25) { Done = true; Say("Done."); }
                break;
        }
        if (_ph >= 2) _minAgl = Math.Min(_minAgl, f.Agl);
    }
    private void Go(FlightNow f, int ph, string cue) { _ph = ph; _t0 = f.T; Say(cue); }
    public override ControlInputs Fly(FlightNow f, Laws L)
    {
        double ail = L.Bank(f, 0, 20), rud = L.Ball(f, ail), ele, lever;
        switch (_ph)
        {
            case 0:
                ele = Atp ? L.Altitude(f, _hRef) : L.Speed(f, 1.3 * f.Vso); lever = Laws.Idle(f); break;
            case 1:
                if (Atp) { ele = L.Altitude(f, _hRef); lever = Laws.Idle(f); }
                else if (_powerOn)
                {
                    _thetaCmd = Math.Min(_thetaCmd + 3 * FlightNow.D2R * f.Dt, (f.T - _t0 > 20 ? 30 : 20) * FlightNow.D2R);
                    ele = L.PitchTo(f, _thetaCmd); lever = Laws.Full(f);
                }
                else
                {
                    // Keep the nose coming up: a steady aft movement toward full back stick.
                    ele = Clamp(L.ElevTrim - 0.1 * (f.T - _t0), -1, 1) + 0.3 * f.Q; lever = Laws.Idle(f);
                }
                break;
            case 2:
                ele = L.PitchTo(f, (Atp ? -3 : -6) * FlightNow.D2R, 2.5); lever = Atp && f.T - _t0 < 1.0 ? Laws.Idle(f) : Laws.Full(f); break;
            default:
                if (Atp) { ele = L.Altitude(f, _hRef); lever = L.PowerForSpeed(f, 1.6 * f.Vs1); }
                else { ele = L.Speed(f, f.Glider ? f.BestGlide : Math.Max(f.Vy, 1.3 * f.Vso)); lever = Laws.Full(f); }
                break;
        }
        // In the stall the instructor's feet pick up a dropped wing with rudder before the aileron is any use.
        if (_ph == 2 && Math.Abs(f.BankDeg) > 10) rud = Clamp(-0.08 * f.BankDeg, -1, 1);
        return new ControlInputs(ail, ele, rud, lever);
    }
    public override void Judge(FlightNow f, LessonJudge j, Action<Criterion, double> W)
    {
        if (_ph == 1 || (_ph == 2 && f.T - _t0 < 1.5)) W(_wings, f.BankDeg);
        if (_ph == 1) W(_ball, f.Beta * FlightNow.R2D);
        if (_ph == 2) { if (f.Stalled) _stalledT += f.Dt; W(_time, _stalledT); }
        if (_ph == 2 && Atp && _rec != null && j.Moments.All(m => m.name != _rec.Name))
        {
            // Recognition: from the first indication to the stick moving forward (nose-down pitch rate).
            if (f.Q < -2 * FlightNow.D2R) j.Moment(_rec, f.T - _warnT, $"{f.T - _warnT:F1} s");
        }
        if (_ph == 3 && f.Stalled) _secondary = true;
        if (Done && !_lossJudged && !double.IsNaN(_onsetAgl))
        {
            _lossJudged = true;
            double loss = (_onsetAgl - _minAgl) * 3.28084;
            j.Moment(_loss, Math.Max(0, loss), $"{Math.Max(0, loss):F0} ft");
            j.Moment(_second, _secondary ? 1 : 0, _secondary ? "it stalled again in the pull-out" : "none");
        }
    }
}

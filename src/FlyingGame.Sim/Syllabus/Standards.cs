using FlyingGame.Sim.Practice;

namespace FlyingGame.Sim.Syllabus;

/// <summary>
/// The syllabus's judged quantities. Tolerances follow the FAA Airman Certification Standards for the certificate the
/// course leads to (Private FAA-S-ACS-6, Commercial FAA-S-ACS-7, Instrument FAA-S-ACS-8, Multi-engine class rating,
/// ATP FAA-S-ACS-11): the ACS limit is the edge of ORANGE; inside a third of it is GREEN.
/// </summary>
public static class SylStd
{
    public static Criterion Bank(double deg, double acs = 5) => new($"Bank {deg:0}°", $"{deg:0}°", "°", acs * 0.5, acs * 0.8, acs, $"ACS: hold the bank within ±{acs:0}°.");
    public static Criterion BankHeld(double acs = 5) => new("Bank held", "the assigned bank", "°", acs * 0.5, acs * 0.8, acs, $"Hold the bank within ±{acs:0}° once established.");
    public static Criterion Altitude(double acsFt = 100) => new("Altitude held", "entry altitude", "ft", acsFt * 0.3, acsFt * 0.6, acsFt, $"ACS: ±{acsFt:0} ft.");
    public static Criterion Airspeed(string what, double acsKt = 10) => new($"Airspeed ({what})", what, "kt", acsKt * 0.3, acsKt * 0.6, acsKt, $"ACS: ±{acsKt:0} kt.");
    public static Criterion SlowFlightSpeed => new("Airspeed (slow flight)", "just above the stall warning", "kt", 3, 6, 10, "ACS: +10/−0 kt — any slower is a stall; faster isn't slow flight.");
    public static Criterion Heading(double acs = 10) => new("Heading held", "entry heading", "°", acs * 0.5, acs, acs * 2, $"ACS: ±{acs:0}°.");
    public static Criterion Rollout(double acs = 10) => new("Roll-out heading", "the assigned heading", "°", acs * 0.5, acs, acs * 1.5, $"ACS: roll out within ±{acs:0}° of the heading.");
    public static Criterion Ball => LessonJudge.Std.Ball;
    public static Criterion NoStall => new("No stall", "never stalled", "s stalled", 0, 0.5, 1.5, "Slow flight and turns are flown without a stall (ACS: no stall warning is a fail in slow flight).");
    public static Criterion WingsLevelAtStall => LessonJudge.Std.StallBank;
    public static Criterion StallBreakTime => LessonJudge.Std.StallTime;
    public static Criterion StallAltitudeLoss => LessonJudge.Std.StallLoss;
    public static Criterion SecondaryStall => new("No secondary stall", "none", "", 0, 0, 0.5, "Pulling out too hard after the break stalls it again — ease out of the dive.");
    public static Criterion TurnRate => new("Standard rate (3°/s)", "3°/s", "°/s", 0.3, 0.6, 1.0, "Instrument ACS: a standard-rate turn is 3°/s — 360° in two minutes.");
    public static Criterion RolloutSpeed(double vs1Kt) => new("Roll-out speed", $"just above the stall ({vs1Kt + 5:0} kt)", "kt", 3, 6, 10, "Commercial ACS: the chandelle ends at +10 kt of the power-on stall speed, the bank coming out as the speed runs out.");
    public static Criterion RecoveryTime => new("Recovered to level flight", "promptly", "s", 6, 9, 12, "Unusual attitudes: recognise, then the correct order, without hesitation.");
    public static Criterion AltitudeExcursion(double g, double y, double o) => new("Altitude excursion", "as little as possible", "ft", g, y, o, "The less height gained or lost, the better the recovery.");
    public static Criterion SpeedLimits => new("Within the speed limits", "no stall, no overspeed", "kt beyond", 0, 5, 10, "Nose-high: don't stall it; nose-low: don't pass Vne.");
    public static Criterion GLimit(double limit) => new("Load factor within limits", $"≤ {limit:0.#} g", "g over", 0, 0.3, 0.8, "The limit load factor is the structure's — past it, it bends.");
    public static Criterion MinG => new("Positive g throughout", "≥ +1 g", "g below +1", 0.5, 0.75, 1.0, "Barrel rolls and loops are positive-g manoeuvres: the pull never stops.");
    public static Criterion PlaneOfLoop => new("Wings level through the loop", "a straight line in the sky", "°", 5, 10, 20, "Any bank in the pull turns the loop into a corkscrew — keep the wings square to the horizon behind you.");
    public static Criterion ExitAltitude => new("Exit altitude", "the entry altitude", "ft", 50, 100, 200, "A round loop ends where it began.");
    public static Criterion ExitHeading(double g = 5, double y = 10, double o = 20) => new("Exit heading", "the entry heading", "°", g, y, o, "Rolls and loops end on the line they started on.");
    public static Criterion RecoveryTurns => new("Turns to recover", "≤ ½ turn", "turns", 0.5, 1.0, 1.5, "PARE: power idle, ailerons neutral, rudder opposite, elevator forward through the break — rotation stops within about half a turn.");
    public static Criterion SpinPullOut(double limit) => new("Pull-out load factor", $"≤ {Math.Min(3.5, limit):0.#} g", "g over", 0, 0.5, 1.0, "Ease out of the dive: hard enough to keep the speed down, gently enough to stay inside the limit and not stall it again.");
    public static Criterion HeadingDrift => new("Heading held with rudder", "entry heading", "°", 5, 10, 20, "Engine out: rudder stops the yaw — 'dead foot, dead engine'.");
    public static Criterion BankIntoGood => new("Bank into the good engine", "2–3°", "° off", 2, 4, 7, "Two to three degrees toward the live engine with the ball half out: the least sideslip, the least drag, the lowest Vmc.");
    public static Criterion VmcRecoveryHeading => new("Recovered at the first loss of control", "≤ 20° off heading", "°", 10, 20, 30, "At the first sign that full rudder no longer holds the heading (or a stall warning), power back on the good engine and lower the nose.");
    public static Criterion Recognition => new("Recovery started at the first indication", "≤ 1 s", "s", 1, 2, 3, "ATP: at the stick shaker or buffet, recover — don't wait for the stall.");
}

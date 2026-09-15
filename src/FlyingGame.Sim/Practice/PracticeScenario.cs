using System;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Practice;

public enum PracticeKind
{
    CrosswindRudder,     // level along the runway at 5 ft: the game flies stick + power, the user keeps the fuselage parallel with rudder
    CrosswindAileron,    // level along the runway at 5 ft: the game flies rudder + elevator + power, the user stays over the centreline with aileron
    LandingRudder,       // the same, descending to a touchdown and roll-out — user has the rudder throughout
    LandingAileron,      // the same, descending to a touchdown and roll-out — user has the aileron; rudder takes over on the ground
    Flare,               // 50 ft, power off, 1.3 Vso: the game keeps it straight and centred, the user rounds out and flares
    FlareSideView,       // the flare exercise seen from the side, aircraft fixed in frame, weight-on-wheels vectors
    ApproachSideView,    // side view with a glideslope: the user pitches for the path and powers for the speed, then lands
    STurns,              // rear view: 45° bank to 45° bank at 45°/s (or full aileron) while the game cycles the speed cruise → 1.15 Vso → cruise, endlessly
    STurnsTest,          // the same, scored, over exactly one speed cycle
    StallSideView,       // side view, pitch only: the user stalls it (power idle) and watches the tail force bring the nose down
    StallRudder,         // rear view: the game stalls and unstalls it repeatedly (≤10 % aileron); the user holds the wing drop with rudder
    StallElevator,       // rear view: the user stalls and recovers with elevator; the game holds it with ≤10 % rudder and aileron
    Straight,            // lesson: keep the wings level with aileron; the game flies elevator and power, rudder held NEUTRAL so a bank slips and weathervanes into a turn
}

/// <summary>Crosswind exercises: Steady / Gusty / Shifting (crosswind from the right, swapping sides every 1,000 ft).
/// Approach and flare exercises: Calm / Headwind / HeadwindGusty / Tailwind / TailwindGusty (8 kt along the runway).</summary>
public enum PracticeWind { Steady, Gusty, Shifting, Calm, Headwind, HeadwindGusty, Tailwind, TailwindGusty }

public enum PracticePhase { Briefing, Live, Finished }

/// <summary>
/// Crosswind / flare practice (owner 2026-09-15): the "game" flies every axis except the one being practised, so the
/// user can concentrate on one thing. Runs in the runway frame of the chosen runway end; each Step merges the user's
/// axis into the autopilot's inputs, drives the practice wind (steady / gusty / shifting) and keeps the score.
/// </summary>
public sealed class PracticeScenario
{
    public const double FiveFtM = 1.524, FiftyFtM = 15.24, HoldOffM = 0.15, ShiftEveryM = 304.8;
    public const double BriefingSec = 8.0;

    public PracticeKind Kind { get; }
    public PracticeWind WindPattern { get; }
    public AircraftConfig Config { get; }
    public WorldTerrain.RunwayEnd Runway { get; }
    public double SurfaceM { get; }
    public double CrosswindMs { get; }
    public double VsoMs { get; }
    public double StanceRad { get; }          // three-point pitch for a taildragger, 0 for a nosewheel type
    public bool Taildragger { get; }
    /// <summary>Main-wheel drop below the CG in level attitude: exercise heights are WHEEL heights.</summary>
    public double GearDropM { get; }
    /// <summary>Wheel brake the game applies on the roll-out (0..1); the host copies it to Aircraft.BrakeInput.</summary>
    public double GameBrake { get; private set; }
    public double GameBrakeBias { get; private set; }

    public bool UserRudder => Kind is PracticeKind.CrosswindRudder or PracticeKind.LandingRudder or PracticeKind.STurns or PracticeKind.STurnsTest or PracticeKind.StallRudder;
    public bool UserAileron => Kind is PracticeKind.CrosswindAileron or PracticeKind.LandingAileron or PracticeKind.STurns or PracticeKind.STurnsTest or PracticeKind.Straight;
    public bool UserElevator => Kind is PracticeKind.Flare or PracticeKind.FlareSideView or PracticeKind.ApproachSideView or PracticeKind.StallSideView or PracticeKind.StallElevator;
    public bool UserThrottle => Kind == PracticeKind.ApproachSideView;
    public bool SideView => Kind is PracticeKind.FlareSideView or PracticeKind.ApproachSideView or PracticeKind.StallSideView;
    public bool Approach => Kind == PracticeKind.ApproachSideView;
    public bool STurn => Kind is PracticeKind.STurns or PracticeKind.STurnsTest;
    public bool Stall => Kind is PracticeKind.StallSideView or PracticeKind.StallRudder or PracticeKind.StallElevator;
    public bool Straight => Kind == PracticeKind.Straight;
    public bool Airwork => STurn || Stall || Straight;
    /// <summary>Side views are longitudinal only: roll, yaw, sideslip and cross-track are held at zero after every step.</summary>
    public bool LongitudinalOnly => SideView;
    public bool Endless => Kind is PracticeKind.STurns or PracticeKind.StallSideView or PracticeKind.StallRudder or PracticeKind.StallElevator or PracticeKind.Straight;
    public bool Descending => !Airwork && Kind is not (PracticeKind.CrosswindRudder or PracticeKind.CrosswindAileron);
    public bool FlareExercise => UserElevator && !Approach;

    public PracticePhase Phase { get; private set; } = PracticePhase.Briefing;
    public double Time { get; private set; }
    public double BriefingLeft => Math.Max(0, BriefingSec - Time);
    public void SkipBriefing() { if (Phase == PracticePhase.Briefing) Phase = PracticePhase.Live; }
    /// <summary>Keep the briefing countdown parked (illustrated pages are being read).</summary>
    public void HoldBriefing() { if (Phase == PracticePhase.Briefing) Time = Math.Min(Time, 0.5); }

    // Live readouts (runway frame): + = right of the centreline / nose right of the runway heading.
    public double AlignmentDeg { get; private set; }
    public double OffCentreM { get; private set; }
    public double AlongM { get; private set; }
    public double AglM { get; private set; }
    public double MainsAglM { get; private set; }
    public double AirspeedMs { get; private set; }
    public double SinkMs { get; private set; }
    public bool OnGround { get; private set; }
    public bool TouchedDown { get; private set; }
    public double TouchdownSinkMs { get; private set; }
    public double TouchdownAlignDeg { get; private set; }
    public double TouchdownOffCentreM { get; private set; }
    public Vec3 WindNow { get; private set; }
    public bool UserHasControl => Phase == PracticePhase.Live;

    // Score: the fraction of live time inside the band (±3° / ±3 m), plus touchdown quality for the landings.
    private double _liveSec, _inBandSec, _rmsAccum;
    public double InBandFraction => _liveSec > 0 ? _inBandSec / _liveSec : 0;
    public double RmsError => _liveSec > 0 ? Math.Sqrt(_rmsAccum / _liveSec) : 0;
    public double Score { get; private set; }
    public string Verdict { get; private set; } = "";

    // Autopilot state
    private double _elevTrim, _elevInt, _thrInt, _thr0 = 0.45, _rudInt, _ailInt;
    public string EndReason { get; private set; } = "";
    private double _gustLevel, _gustTarget, _gustNextT, _gustRamp = 1;
    private readonly Random _rng;
    private double _lastAlongForShift, _stillSec, _crab0, _hT = double.NaN, _theta0, _betaF;
    private bool _heightLawOn;
    /// <summary>Elevator-power scale for the game's pitch laws: 1.8°/° of trim slope = 1.0; a tail with twice the power gets half the gains.</summary>
    public double ElevatorPower { get; private set; } = 1.0;

    public ControlInputs LastInputs { get; private set; } = ControlInputs.Neutral;
    /// <summary>What the game would do on every axis this step (the user's axis included) — the briefing autopilot and the test yardstick.</summary>
    public ControlInputs Autopilot { get; private set; } = ControlInputs.Neutral;
    public bool GameAileron { get; private set; } = true; public bool GameElevator { get; private set; } = true;
    public bool GameRudder { get; private set; } = true; public bool GameThrottle { get; private set; } = true;

    public PracticeScenario(PracticeKind kind, PracticeWind wind, AircraftConfig config, WorldTerrain.RunwayEnd runway, double surfaceM, double crosswindMs = 4.0, int seed = 1)
    // crosswindMs is the CAP; the actual crosswind scales with the type's speed
    {
        Kind = kind; WindPattern = wind; Config = config; Runway = runway; SurfaceM = surfaceM;
        _rng = new Random(seed);
        VsoMs = EstimateVso(config, surfaceM);
        // Crosswind scaled to the type: ~20 % of the exercise speed (a Cub at 28 kt gets 5 kt, a 172 at 50 kt gets 8 kt) —
        // a fixed 8 kt asked a Cub for 16° of slip, beyond its rudder.
        CrosswindMs = crosswindMs > 0 ? Math.Clamp(0.16 * 1.15 * VsoMs, 2.0, crosswindMs) : 0;   // ≈0.18 Vso steady, 0.28 Vso in the gusts
        var mains = config.Gear.FindAll(g => !g.IsTailwheel && g.Pos[2] > 0 && Math.Abs(g.Pos[1]) < 3.5);
        var tws = config.Gear.FindAll(g => g.IsTailwheel);
        Taildragger = mains.Count > 0 && tws.Count > 0;
        StanceRad = Taildragger ? Math.Atan((mains[0].Pos[2] - tws[0].Pos[2]) / (mains[0].Pos[0] - tws[0].Pos[0])) : 0.0;
        double drop = 0; foreach (GearConfig g in config.Gear) if (!g.IsTailwheel) drop = Math.Max(drop, g.Pos[2] - config.Mass.CgVec().Z);
        GearDropM = drop;
        _gustNextT = 2 + 3 * _rng.NextDouble();
        try
        {
            TrimSolver.Result ta = TrimSolver.SolveGliderTrim(config, 1.5 * VsoMs, surfaceM + 300), tb = TrimSolver.SolveGliderTrim(config, 1.8 * VsoMs, surfaceM + 300);
            if (ta.Converged && tb.Converged && Math.Abs(ta.AlphaRad - tb.AlphaRad) > 1e-3)
                ElevatorPower = Math.Clamp(Math.Abs((ta.ElevatorRad - tb.ElevatorRad) / (ta.AlphaRad - tb.AlphaRad)) / 1.8, 0.3, 1.0);
        }
        catch (Exception) { }
        if (Approach)
        {
            // Glideslope: a little shallower than the idle glide at 1.3 Vso (so it takes a touch of power to stay on it);
            // for a glider, the glide with half spoiler at 1.3 Vso (room to play either side).
            double v = 1.3 * VsoMs;
            bool glider = config.Propulsion is null;
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, v, surfaceM + 50, spoilerFraction: glider ? 0.5 : 0.0);
            double ratio = t.Converged && t.GlideRatio > 0 ? t.GlideRatio : ApproachSpawn.FindBestGlide(config, surfaceM + 50).GlideRatio;
            double gammaIdle = Math.Atan(1.0 / Math.Max(3.0, ratio));
            GlideslopeRad = glider ? gammaIdle : gammaIdle * 0.8;
            GlideslopeRad = Math.Clamp(GlideslopeRad, 2.5 * Math.PI / 180, 8.0 * Math.PI / 180);
        }
    }

    /// <summary>Approach: glideslope angle (positive = down) and where it meets the runway (150 m past the threshold).</summary>
    public double GlideslopeRad { get; }
    public const double AimPastThresholdM = 150.0;
    public double GlideslopeHeightAt(double along) => Math.Max(0, (AimPastThresholdM - along) * Math.Tan(GlideslopeRad));
    /// <summary>+ = above the glideslope (m).</summary>
    public double GlideslopeDeviationM { get; private set; }
    public double GlideslopeDeviationDeg { get; private set; }

    /// <summary>The SIM's stall speed for this type: the slowest speed the trim solver can hold below 12° of angle of attack
    /// (a CLmax guess from the wing area put a Cub's "1.15 Vso" below its real stall, and the height law pulled it into a
    /// stall in the first gust).</summary>
    public static double EstimateVso(AircraftConfig config, double altitudeM)
    {
        // Scan DOWN from a comfortable speed and stop at the first speed the trim solver can no longer hold below 14° of
        // angle of attack (scanning up found spurious low-speed solutions on some types).
        double vRef = config.SpawnIasMs > 1 ? config.SpawnIasMs : 25.0;
        double last = vRef;
        for (double v = vRef * 1.6; v >= vRef * 0.35; v -= 0.5)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, v, altitudeM);
            bool ok = t.Converged && !double.IsNaN(t.AlphaRad) && t.AlphaRad < 14.0 * Math.PI / 180 && t.GlideRatio > 0;
            if (!ok) return last;
            last = v;
        }
        return last;
    }

    /// <summary>Level-flight speed at 75 % power: the slowest speed whose drag power (weight ÷ glide ratio × V ÷ η) reaches
    /// 75 % of the engine's; a glider uses 1.5 × best glide.</summary>
    public static double EstimateCruise75(AircraftConfig config, double altitudeM, double vso)
    {
        if (config.Propulsion is null || config.Propulsion.MaxPowerW <= 0) return 1.5 * ApproachSpawn.FindBestGlide(config, altitudeM).SpeedMs;
        double eff = config.Propulsion.Efficiency > 0 ? config.Propulsion.Efficiency : 0.75;
        double weight = config.Mass.MassKg * 9.81, target = 0.75 * config.Propulsion.MaxPowerW;
        double last = 1.3 * vso;
        for (double v = 1.3 * vso; v <= 6 * vso; v += 1.0)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, v, altitudeM);
            if (!t.Converged || t.GlideRatio <= 0) continue;
            double pReq = weight / t.GlideRatio * v / eff;
            last = v;
            if (pReq >= target) return v;
        }
        return last;
    }

    public double CruiseMs { get; private set; }
    public const double AirworkAglM = 800.0;          // the airwork exercises fly at ~2,600 ft over the field
    public const double SpeedCycleSec = 80.0;         // cruise → 1.15 Vso → cruise
    /// <summary>S-turns: the speed the game is steering to right now, and where in the cycle it is (0..1).</summary>
    public double SpeedTargetMs { get; private set; }
    public double CycleFraction { get; private set; }
    public int Cycles { get; private set; }
    public double BankDeg { get; private set; }
    public double RollRateDegS { get; private set; }
    public int TargetBankSign { get; private set; } = 1;   // +1 = roll right to 45°, −1 = roll left to 45°
    public int Reversals { get; private set; }
    public double AlphaDeg { get; private set; }
    public double StalledFraction { get; private set; }
    public bool Stalled => StalledFraction > 0.35;
    public int StallCount { get; private set; }
    public double MaxSinkMs { get; private set; }
    public double MaxWingDropDeg { get; private set; }
    private bool _wasStalled;
    private double _stallPhaseT; private int _stallPhase;   // game-flown stalls: 0 = pull, 1 = hold, 2 = break, 3 = settle
    private double _rollRateAccum, _rollRateSamples, _bankErrAccum;

    public string Title => Kind switch
    {
        PracticeKind.CrosswindRudder => "Crosswind: rudder",
        PracticeKind.CrosswindAileron => "Crosswind: aileron",
        PracticeKind.LandingRudder => "Crosswind landing: rudder",
        PracticeKind.LandingAileron => "Crosswind landing: aileron",
        PracticeKind.Flare => "Round-out and flare",
        PracticeKind.FlareSideView => "Flare, side view",
        PracticeKind.ApproachSideView => "Approach, side view",
        PracticeKind.STurns => "S-turns: 45° to 45°",
        PracticeKind.STurnsTest => "S-turns test: one speed cycle",
        PracticeKind.StallSideView => "Stall, side view",
        PracticeKind.StallRudder => "Stall: wing drop with rudder",
        PracticeKind.StallElevator => "Stall: recover with elevator",
        _ => "Straight: wings level",
    } + WindPattern switch { PracticeWind.Gusty => " · gusty", PracticeWind.Shifting => " · shifting wind", PracticeWind.Headwind => " · headwind", PracticeWind.HeadwindGusty => " · gusty headwind", PracticeWind.Tailwind => " · tailwind", PracticeWind.TailwindGusty => " · gusty tailwind", _ => "" };

    public string Instructions => Kind switch
    {
        PracticeKind.CrosswindRudder => "The game flies the stick and the power: bank to hold the centreline, elevator for five feet, power for one point one five V S O. You have the rudder. Keep the fuselage parallel to the runway. Parallel is the goal, not centred.",
        PracticeKind.CrosswindAileron => "The game flies the rudder, the elevator and the power. You have the aileron. Stay over the centreline. Bank into the wind as much as it takes.",
        PracticeKind.LandingRudder => "The game flies the stick and the power down to a touchdown. You have the rudder all the way. Keep the fuselage parallel to the runway, through the touchdown and the roll-out.",
        PracticeKind.LandingAileron => "The game flies the rudder, the elevator and the power down to a touchdown. You have the aileron. Stay over the centreline; on the ground the rudder takes the direction, keep the aileron into the wind.",
        PracticeKind.Flare => "Fifty feet, power off, one point three V S O. The game keeps it straight and on the centreline. You have the elevator. Round out, hold it off, and let it settle.",
        PracticeKind.FlareSideView => "Side view. Fifty feet, power off. You have the elevator and the brakes. Round out, flare, and watch the weight on the wheels: elevator and braking shift it between the wheels.",
        PracticeKind.ApproachSideView => "Side view, on the glideslope at one point three V S O. You have the elevator and the power. Pitch for the glide path, power for the airspeed. The slope is a little shallower than the idle glide, so it takes a touch of power. Fly it down to the runway and land.",
        PracticeKind.STurns => "The game holds the altitude and cycles the speed from cruise down to one point one five V S O and back, over and over. You have the aileron and rudder. Roll into a forty five degree bank at forty five degrees a second, or full aileron, then reverse immediately all the way to forty five the other way. Keep it going.",
        PracticeKind.STurnsTest => "Scored: one speed cycle, cruise to one point one five V S O and back. Forty five degrees of bank to forty five the other way, at forty five degrees a second or full aileron, continuously. Roll rate and bank accuracy count.",
        PracticeKind.StallSideView => "Side view, power off, pitch only. You have the elevator. Bring the nose up and hold it until the wing stalls. Watch the bubbles: as it sinks, the relative wind at the tail comes from below, the tail force changes and the nose drops by itself. Hold it in the stall if you like and watch the sink rate build.",
        PracticeKind.StallRudder => "Power off. The game stalls the aircraft and breaks the stall, again and again, using no more than ten percent aileron. You have the rudder. When a wing drops, pick it up with rudder, not aileron.",
        PracticeKind.StallElevator => "Power off. You have the elevator. Stall it, then break the stall by lowering the angle of attack. The game holds it with no more than ten percent rudder and aileron. Recovery is angle of attack first, and the wing drop is a rudder job.",
        _ => "The game holds the altitude and the speed, and keeps the rudder neutral. You have the aileron. Keep the wings level and the nose will stay put. Let a bank develop and watch: the aircraft slips toward the low wing, then weathervanes into a turn.",
    } + WindPattern switch
    {
        PracticeWind.Gusty => " The crosswind gusts to half again its strength, at random.",
        PracticeWind.Shifting => " The crosswind swaps sides every thousand feet.",
        PracticeWind.Headwind => " Eight knots of headwind.",
        PracticeWind.HeadwindGusty => " Eight knots of headwind, gusting to twelve at random.",
        PracticeWind.Tailwind => " Eight knots of tailwind: it will float.",
        PracticeWind.TailwindGusty => " Eight knots of tailwind, gusting to twelve at random.",
        _ => "",
    };
    public bool IsGusty => WindPattern is PracticeWind.Gusty or PracticeWind.HeadwindGusty or PracticeWind.TailwindGusty;

    /// <summary>Illustrated briefing pages (title, text) shown before the standard card; the host renders the pictures.</summary>
    public (string title, string text)[] LessonPages => Kind == PracticeKind.Straight ? new[]
    {
        ("Bank tilts the lift", "Seen from behind: the wing's lift stands straight up when the wings are level and balances the weight. Bank the wings and the lift tilts with them. Part of it now pulls sideways toward the low wing, and the aircraft starts to slide that way. That sideways motion is a slip."),
        ("The slip becomes a turn", "Seen from above: once the aircraft slides toward the low wing, the air comes at it from that side. The vertical tail is an arrow's feathers: it pushes the tail away from the wind, the nose swings toward it, and the aircraft weathervanes into a turn. Hold the wings level with aileron and none of that happens."),
    } : System.Array.Empty<(string, string)>();

    public double HeadingDriftDeg { get; private set; }
    public double BetaDeg { get; private set; }
    private double _heading0 = double.NaN;

    public string HandoverLine => Straight ? "You have the aileron. Keep the wings level." : STurn ? "You have the aileron and rudder." : UserRudder ? "You have the rudder." : UserAileron ? "You have the aileron." : Approach ? "You have the elevator and the power." : "You have the elevator.";

    // ---- runway frame helpers ------------------------------------------------------------------------------
    private (double along, double cross) Localize(double x, double y)
    {
        (double tx, double ty) = Runway.Threshold;
        double dx = x - tx, dy = y - ty;
        return (dx * Runway.AlongX + dy * Runway.AlongY, -dx * Runway.AlongY + dy * Runway.AlongX);
    }
    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }
    private static (double roll, double pitch, double yaw) Euler(Quat q)
    {
        double roll = Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
        double pitch = Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
        double yaw = Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        return (roll, pitch, yaw);
    }

    /// <summary>Starting aircraft: over the threshold at 5 ft and 1.15 Vso (crosswind exercises) or 50 ft, 1.3 Vso on a
    /// 4° path to the threshold (flare exercises), heading along the runway, crabbed for the steady crosswind.</summary>
    public Aircraft Spawn()
    {
        Atmosphere.ActiveTurbulence = null;
        Atmosphere.SteadyWind = WindVector(0, 0);
        if (Airwork) return SpawnAirwork();
        double wheels = Approach ? 91.44 : FlareExercise ? FiftyFtM : FiveFtM;   // WHEEL height (owner: "5 ft")
        double agl = wheels + GearDropM;
        double v = (FlareExercise || Approach) ? 1.3 * VsoMs : 1.15 * VsoMs;
        double back = Approach ? wheels / Math.Tan(GlideslopeRad) - AimPastThresholdM : FlareExercise ? wheels / Math.Tan(4.0 * Math.PI / 180) - 150 : 0;   // aimed 150 m past the threshold
        (double tx, double ty) = Runway.Threshold;
        var pos = new Vec3(tx - Runway.AlongX * back, ty - Runway.AlongY * back, -(SurfaceM + agl));
        // Crab into the wind so the ground track runs along the runway from the first frame.
        Vec3 w = Atmosphere.SteadyWind;
        double wCross = -w.X * Runway.AlongY + w.Y * Runway.AlongX;
        double crab = Math.Asin(Math.Clamp(-wCross / v, -0.5, 0.5));
        double heading = Runway.HeadingRad + crab;
        _crab0 = crab;
        // Trim for THIS speed (the best-glide trim is faster and flatter: spawning with it sank the aircraft off 5 ft, the
        // law hauled back and the wing stalled and dropped). The glide trim at v gives the angle of attack and elevator;
        // the power to hold level flight follows from the glide ratio.
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(Config, v, SurfaceM + agl, spoilerFraction: Config.Propulsion is null && Approach ? 0.5 : 0.0);
        _theta0 = trim.Converged ? trim.ThetaRad : 0.0;
        double gamma = Approach ? -GlideslopeRad : FlareExercise ? -4.0 * Math.PI / 180 : 0;
        double alpha = trim.Converged ? trim.AlphaRad : 0.08;
        double pitch = alpha + gamma;
        double hh = heading / 2, hp = pitch / 2;
        var att = Quat.Multiply(new Quat(0, 0, Math.Sin(hh), Math.Cos(hh)), new Quat(0, Math.Sin(hp), 0, Math.Cos(hp)));
        // Ground velocity along the runway: air velocity along the heading plus the wind.
        double vAlong = Math.Sqrt(Math.Max(0, v * v - wCross * wCross)) + (w.X * Runway.AlongX + w.Y * Runway.AlongY);
        var vWorld = new Vec3(Runway.AlongX * vAlong * Math.Cos(gamma), Runway.AlongY * vAlong * Math.Cos(gamma), -vAlong * Math.Sin(gamma));
        var state = new RigidBodyState(pos, att, att.Conjugate().Rotate(vWorld), Vec3.Zero);
        double elevRad = trim.Converged ? trim.ElevatorRad : ApproachSpawn.ApproachGlide(Config, SurfaceM + agl).ElevatorRad;
        _elevTrim = Aircraft.StickForDeflection(elevRad, Config.Controls.Elevator);
        // Power for the exercise: thrust = drag (weight / glide ratio) less the gravity component along the path.
        double ratio = trim.Converged && trim.GlideRatio > 0 ? trim.GlideRatio : 8.0;
        double weight = Config.Mass.MassKg * 9.81;
        double thrustNeeded = weight / ratio + weight * Math.Sin(gamma);   // gamma negative on a descent: less thrust
        double eff = Config.Propulsion?.Efficiency > 0 ? Config.Propulsion.Efficiency : 0.75;
        double maxP = Config.Propulsion?.MaxPowerW > 0 ? Config.Propulsion.MaxPowerW : 100000;
        _thr0 = FlareExercise || Config.Propulsion is null ? 0.0 : Math.Clamp(thrustNeeded * v / (eff * maxP), 0.0, 0.9);
        var ac = new Aircraft(Config, state, new ControlDeflections(0, elevRad, 0, Config.Propulsion is null && Approach ? 0.5 : 0));
        ac.FlapFraction = 0.0;   // the user picks the flap setting (owner: practise with and without)
        return ac;
    }

    /// <summary>Airwork: 2,600 ft over the runway centre, heading along it, trimmed at cruise (S-turns) or 1.3 Vso (stalls).</summary>
    private Aircraft SpawnAirwork()
    {
        CruiseMs = EstimateCruise75(Config, SurfaceM + AirworkAglM, VsoMs);
        double v = STurn || (Straight && Config.Propulsion is not null) ? CruiseMs : Straight ? ApproachSpawn.FindBestGlide(Config, SurfaceM + AirworkAglM).SpeedMs : 1.3 * VsoMs;
        SpeedTargetMs = v;
        double alt = SurfaceM + AirworkAglM;
        var pos = new Vec3(Runway.CentreX, Runway.CentreY, -alt);
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(Config, v, alt);
        _theta0 = trim.Converged ? trim.ThetaRad : 0.05;
        double alpha = trim.Converged ? trim.AlphaRad : 0.05;
        bool glider = Config.Propulsion is null;
        double gamma = glider ? -Math.Atan(1.0 / Math.Max(3.0, trim.GlideRatio)) : 0.0;
        double heading = Runway.HeadingRad, pitch = alpha + gamma;
        double hh = heading / 2, hp = pitch / 2;
        var att = Quat.Multiply(new Quat(0, 0, Math.Sin(hh), Math.Cos(hh)), new Quat(0, Math.Sin(hp), 0, Math.Cos(hp)));
        var vWorld = new Vec3(Runway.AlongX * v * Math.Cos(gamma), Runway.AlongY * v * Math.Cos(gamma), -v * Math.Sin(gamma));
        var state = new RigidBodyState(pos, att, att.Conjugate().Rotate(vWorld), Vec3.Zero);
        double elevRad = trim.Converged ? trim.ElevatorRad : 0;
        _elevTrim = Aircraft.StickForDeflection(elevRad, Config.Controls.Elevator);
        double ratio = trim.Converged && trim.GlideRatio > 0 ? trim.GlideRatio : 8.0;
        double weight = Config.Mass.MassKg * 9.81;
        double eff = Config.Propulsion?.Efficiency > 0 ? Config.Propulsion.Efficiency : 0.75;
        double maxP = Config.Propulsion?.MaxPowerW > 0 ? Config.Propulsion.MaxPowerW : 100000;
        _thr0 = glider || Stall ? 0.0 : Math.Clamp(weight / ratio * v / (eff * maxP), 0.0, 1.0);
        _hT = AirworkAglM; _heightLawOn = true;
        return new Aircraft(Config, state, new ControlDeflections(0, elevRad, 0, 0));
    }

    /// <summary>Longitudinal-only exercises: after each sim step, zero the roll, yaw off the runway heading, sideslip, roll
    /// and yaw rates and cross-track (owner: "ignore everything lateral — a simplified side-only pitch and power experience").</summary>
    public void ConstrainLongitudinal(Aircraft ac)
    {
        if (!LongitudinalOnly) return;
        RigidBodyState s = ac.State;
        (_, double pitch, _) = Euler(s.Attitude);
        double hh = Runway.HeadingRad / 2, hp = pitch / 2;
        var att = Quat.Multiply(new Quat(0, 0, Math.Sin(hh), Math.Cos(hh)), new Quat(0, Math.Sin(hp), 0, Math.Cos(hp)));
        Vec3 vWorld = s.Attitude.Rotate(s.Velocity);
        double vAlong = vWorld.X * Runway.AlongX + vWorld.Y * Runway.AlongY;
        var vW = new Vec3(Runway.AlongX * vAlong, Runway.AlongY * vAlong, vWorld.Z);
        (double along, _) = Localize(s.Position.X, s.Position.Y);
        (double tx, double ty) = Runway.Threshold;
        var pos = new Vec3(tx + Runway.AlongX * along, ty + Runway.AlongY * along, s.Position.Z);
        ac.State = new RigidBodyState(pos, att, att.Conjugate().Rotate(vW), new Vec3(0, s.Rates.Y, 0));
    }

    // ---- wind ------------------------------------------------------------------------------------------------
    /// <summary>Crosswind from the RIGHT of the runway (blowing toward the left), modulated by the pattern.</summary>
    public const double AlongWindMs = 4.1;   // 8 kt
    public Vec3 WindVector(double t, double along)
    {
        if (WindPattern is PracticeWind.Calm) return Vec3.Zero;
        if (WindPattern is PracticeWind.Headwind or PracticeWind.HeadwindGusty or PracticeWind.Tailwind or PracticeWind.TailwindGusty)
        {
            double sp = AlongWindMs * (1.0 + 0.5 * _gustLevel);
            double dir = WindPattern is PracticeWind.Headwind or PracticeWind.HeadwindGusty ? -1.0 : 1.0;   // headwind blows toward the approach end
            return new Vec3(Runway.AlongX * sp * dir, Runway.AlongY * sp * dir, 0);
        }
        double side = 1.0;
        if (WindPattern == PracticeWind.Shifting)
        {
            // Swap sides every 1,000 ft along the runway, blended over 40 m so the change is a shift, not a step.
            double cell = along / ShiftEveryM;
            int k = (int)Math.Floor(cell);
            double frac = cell - k;
            double blend = frac < 40.0 / ShiftEveryM ? frac / (40.0 / ShiftEveryM) : 1.0;
            double s0 = (k % 2 == 0) ? 1.0 : -1.0;
            side = k == 0 ? 1.0 : (-s0) + (s0 - (-s0)) * (0.5 - 0.5 * Math.Cos(Math.PI * blend));
            if (along < 0) side = 1.0;
        }
        double speed = CrosswindMs * (1.0 + 0.5 * _gustLevel) * side;
        // blowing toward the LEFT of the runway = −cross direction; cross = (−AlongY, AlongX)
        return new Vec3(speed * Runway.AlongY, -speed * Runway.AlongX, 0);
    }

    private void StepGusts(double dt)
    {
        if (!IsGusty) { _gustLevel = 0; return; }
        if (Time >= _gustNextT)
        {
            // A gust arrives: ramp up over ~0.6 s, hold 1–3 s, decay; the next comes 2–6 s later.
            _gustTarget = _gustTarget > 0.5 ? 0.0 : 1.0;
            _gustNextT = Time + (_gustTarget > 0.5 ? 1.0 + 2.0 * _rng.NextDouble() : 2.0 + 4.0 * _rng.NextDouble());
        }
        double rate = _gustTarget > _gustLevel ? 1.7 : 0.9;
        _gustLevel += Math.Clamp(_gustTarget - _gustLevel, -rate * dt, rate * dt);
    }

    // ---- the step ----------------------------------------------------------------------------------------------
    /// <summary>Merge the user's axis into the autopilot's inputs for this step; drive the wind and the score.</summary>
    public ControlInputs Step(Aircraft ac, ControlInputs user, double dt)
    {
        Time += dt;
        RigidBodyState s = ac.State;
        (double along, double cross) = Localize(s.Position.X, s.Position.Y);
        (double roll, double pitch, double yaw) = Euler(s.Attitude);
        double psiErr = Wrap(yaw - Runway.HeadingRad);
        Vec3 vWorld = s.Attitude.Rotate(s.Velocity);
        double vAlong = vWorld.X * Runway.AlongX + vWorld.Y * Runway.AlongY;
        double vCross = -vWorld.X * Runway.AlongY + vWorld.Y * Runway.AlongX;
        double agl = -s.Position.Z - SurfaceM;
        double hdot = -vWorld.Z;
        Vec3 wind = Atmosphere.WindAtPosition(s.Position);
        Vec3 vAirBody = s.Velocity - s.Attitude.Conjugate().Rotate(wind);
        double ias = vAirBody.Length;
        double mainsAgl = agl; 
        foreach (GearConfig g in Config.Gear)
        {
            if (g.IsTailwheel) continue;
            Vec3 w = s.Attitude.Rotate(g.PosVec() - Config.Mass.CgVec());
            mainsAgl = Math.Min(mainsAgl, agl - w.Z);
        }
        bool ground = LandingGear.AnyMainWheelOnGround(Config, s);

        AlignmentDeg = psiErr * 180 / Math.PI; OffCentreM = cross; AlongM = along; AglM = agl; MainsAglM = mainsAgl;
        AirspeedMs = ias; SinkMs = -hdot; OnGround = ground;
        if (ground && !TouchedDown) { TouchedDown = true; TouchdownSinkMs = -hdot; TouchdownAlignDeg = AlignmentDeg; TouchdownOffCentreM = cross; }

        StepGusts(dt);
        WindNow = WindVector(Time, along);
        Atmosphere.SteadyWind = WindNow;

        if (Phase == PracticePhase.Briefing && Time >= BriefingSec) Phase = PracticePhase.Live;
        AlphaDeg = ias > 2 ? Math.Atan2(vAirBody.Z, Math.Max(vAirBody.X, 0.5)) * 180 / Math.PI : 0;
        StalledFraction = ac.StalledFraction;
        if (Airwork) return StepAirwork(ac, user, dt, roll, pitch, yaw, agl, hdot, ias, vAirBody);

        // ---- autopilot laws (sim sign conventions: +aileron rolls right, +elevator = nose DOWN (stick forward), +rudder = nose right,
        // lever −1 = full power … +1 = idle) ----
        double p = s.Rates.X, q = s.Rates.Y, r = s.Rates.Z;

        // De-crab gently: the runway heading is the target, reached from the spawn crab over the first 4 s (a step
        // there set off a rudder/dihedral limit cycle that put a wing on the runway).
        double psiTarget = _crab0 * Math.Max(0, 1 - Time / 4.0);
        double psiE = Wrap(psiErr - psiTarget);

        // Aileron: bank toward the centreline (position + cross-track rate + a slow integral for the steady wing-low slip),
        // wings level on the ground. Modest gains: the loop couples with the rudder through dihedral.
        _ailInt = Math.Clamp(_ailInt + (ground ? 0 : 0.006 * cross * dt), -0.25, 0.25);   // the steady wing-low slip needs up to ~14° of bank
        double phiCmd = ground ? 0 : Math.Clamp(-0.025 * cross - 0.08 * vCross - _ailInt, -0.28, 0.28);
        double ail = Math.Clamp(1.2 * (phiCmd - roll) - 0.5 * p, -1, 1);
        if (ground && !UserAileron) ail = Math.Clamp(-0.8 * roll, -1, 1);

        // Rudder: fuselage parallel to the runway (P + slow I + rate); on the ground the takeoff-roll law (heading, rate, centreline).
        // Feed-forward from the sideslip: holding the fuselage parallel in a slip takes rudder in proportion to the sideslip
        // angle (air from the right → the nose wants to weathervane right → left rudder); the integral only trims it.
        double beta = ias > 3 ? Math.Asin(Math.Clamp(vAirBody.Y / ias, -1, 1)) : 0;
        _betaF += (beta - _betaF) * Math.Min(1, dt / 1.0);   // 1 s low-pass: gusts must not slam the rudder
        _rudInt = Math.Clamp(_rudInt + (ground ? 0 : 0.3 * psiE * dt), -0.5, 0.5);
        double rud = Math.Clamp(-2.0 * psiE - 1.0 * r - 1.5 * _betaF - _rudInt, -1, 1);
        if (ground) rud = Math.Clamp(-3.0 * psiErr - 0.8 * r - 0.08 * cross, -1, 1);
        GameBrake = ground && Descending ? 0.35 : 0.0;   // roll-out braking; the host biases it with the rudder (differential braking steers)
        GameBrakeBias = GameBrake > 0 ? Math.Clamp(rud * 0.8, -1, 1) : 0;

        // Height target for the MAINS along the runway (a function, so its slope gives the target descent rate).
        double L = Runway.LengthM;
        double HTargetAt(double a)
        {
            if (!Descending) return FiveFtM;
            if (FlareExercise || Approach)
            {
                double aimAlong = FlareExercise ? 150.0 : AimPastThresholdM;
                double pathH = FlareExercise ? Math.Max(0, (aimAlong - a) * Math.Tan(4.0 * Math.PI / 180)) : GlideslopeHeightAt(a);
                // Round-out: below 3 m the target eases in as the square of the path height, so the sink bleeds off progressively.
                return pathH > 3.0 ? pathH : HoldOffM + (3.0 - HoldOffM) * (pathH / 3.0) * (pathH / 3.0);
            }
            // Crosswind landing: level for the first quarter, then down to the hold-off over the next third.
            double f = Math.Clamp((a - 0.25 * L) / (0.30 * L), 0, 1);
            double ease = 0.5 - 0.5 * Math.Cos(Math.PI * f);
            return FiveFtM + (HoldOffM - FiveFtM) * ease;
        }
        double hProfile = HTargetAt(along);
        // The target the elevator law chases follows the profile, but may only come DOWN as fast as a real round-out:
        // 0.3 m/s at the hold-off rising to ~1.35 m/s at 3 m (an exponential flare, not a chase).
        if (double.IsNaN(_hT)) _hT = mainsAgl;
        double maxSink = 0.25 + 0.5 * Math.Max(0, mainsAgl);   // 0.3 m/s at the hold-off, ~1.75 m/s at 3 m
        double hPrev = _hT;
        if ((FlareExercise || Approach) && _heightLawOn) hProfile = Math.Min(hProfile, _hT);   // a flare never climbs back to the path
        _hT += Math.Clamp(hProfile - _hT, -maxSink * dt, 2.0 * dt);
        double hTarget = _hT;
        double hdotTarget = (hTarget - hPrev) / Math.Max(dt, 1e-3);
        double vTarget = 1.15 * VsoMs;
        if (FlareExercise || Approach) vTarget = hTarget < 0.4 ? 0.85 * VsoMs : 1.3 * VsoMs;   // bleed the speed only once in the hold-off
        else if (Descending)
        {
            double f = Math.Clamp((along - 0.25 * L) / (0.30 * L), 0, 1);
            double ease = 0.5 - 0.5 * Math.Cos(Math.PI * f);
            vTarget = 1.15 * VsoMs - 0.05 * VsoMs * ease;
            if (hTarget < 0.4) vTarget = 0.85 * VsoMs;   // in the hold-off: below stall, the game can no longer hold it off — it settles on
        }

        double ele, lever;
        GlideslopeDeviationM = Approach ? mainsAgl - GlideslopeHeightAt(along) : 0;
        GlideslopeDeviationDeg = Approach && along < AimPastThresholdM - 20 ? Math.Atan2(GlideslopeDeviationM, AimPastThresholdM - along) * 180 / Math.PI : 0;
        bool glider = Config.Propulsion is null;
        // Height hold on the mains: trim + height error + descent-rate error (against the profile's own slope) + pitch-rate
        // damping; a slow integral finds the trim for this speed and power.
        {
            bool gliderPathLaw = glider && Approach && mainsAgl > 3.0;
            if (!gliderPathLaw && !_heightLawOn) { _heightLawOn = true; _hT = mainsAgl; hTarget = _hT; hdotTarget = hdot; }   // take over from where it is
            double hErr = mainsAgl - hTarget;
            if (!gliderPathLaw) _elevInt = Math.Clamp(_elevInt + 0.08 * hErr * dt, -0.5, 0.5);
            // Gains scaled down with speed (elevator power grows with V²): the same law that flares a 2-33 at 20 m/s hunted
            // in pitch with a Cub at 34 m/s.
            double kv = Math.Clamp(Math.Pow(22.0 / Math.Max(ias, 8.0), 1.5), 0.35, 1.2);
            // Pitch-rate damping stays at full strength (scaling it away let the Cub porpoise in the hold-off).
            ele = Math.Clamp(_elevTrim + ElevatorPower * (kv * (0.45 * hErr + 0.55 * (hdot - hdotTarget)) + _elevInt) + 0.6 * q, -0.9, 0.6);
            // Stall guard: slow and still above the hold-off, the answer is power, not more back stick (a gust at 5 ft
            // had the law pull a Cub into a stall and drop a wing).
            if (!ground && mainsAgl > 0.5 && ias < 1.06 * VsoMs) ele = Math.Max(ele, _elevTrim - 0.15);
            double vErr = vTarget - ias;
            if (gliderPathLaw)
            {
                // A glider cannot add speed with its spoiler: pitch attitude holds the SPEED (slow → nose down), the spoiler
                // holds the PATH (high → more), half spoiler on the slope. The round-out below 3 m uses the height law.
                double thetaCmd = _theta0 - 0.02 * vErr;
                ele = Math.Clamp(_elevTrim + 2.0 * (pitch - thetaCmd) + 0.8 * q, -0.9, 0.6);
                lever = Math.Clamp(0.5 + 0.12 * GlideslopeDeviationM + 0.20 * (hdot - hdotTarget), 0.05, 1.0);
            }
            else
            {
                if (Descending && Taildragger && mainsAgl < 0.6)
                {
                    // Three-point: bring the attitude to the stance as it settles (never let the nose drop through it).
                    double attHold = Math.Clamp(-(StanceRad - pitch) * 3.0 + q * 0.6, -0.8, 0.3);
                    ele = Math.Min(ele, attHold);
                }
                if (ground) ele = Taildragger ? -0.6 : 0.0;   // tail down on the roll-out; neutral for a nosewheel type
                // Power: hold the target airspeed (PI); idle on the ground and in the power-off exercises. A glider's lever is
                // its spoiler: half out in the flare, full once it is down.
                _thrInt = Math.Clamp(_thrInt + 0.05 * vErr * dt, -0.4, 0.4);
                double thr01 = ground || FlareExercise ? 0 : Math.Clamp(_thr0 + 0.20 * vErr + _thrInt, 0, 1);
                // A glider's lever is its spoiler: half on the slope, more when fast, full once it is down.
                lever = glider ? (ground ? 1.0 : 0.4) : 1 - 2 * thr01;   // glider round-out: spoiler eased to 40 %, full once down
            }
        }
        Autopilot = new ControlInputs(ail, ele, rud, lever);

        // Merge: the user's axis replaces the game's once the briefing is over (until then the game flies everything).
        bool live = Phase == PracticePhase.Live;
        GameAileron = !(live && UserAileron); GameRudder = !(live && UserRudder); GameElevator = !(live && UserElevator); GameThrottle = !(live && UserThrottle);
        double outAil = GameAileron ? ail : user.Aileron;
        double outRud = GameRudder ? rud : user.Rudder;
        double outEle = GameElevator ? ele : user.Elevator;
        if (!GameThrottle) lever = user.ThrottleLever;

        // Score while live.
        if (live && Phase != PracticePhase.Finished)
        {
            double err = UserAileron ? Math.Abs(cross) : Approach ? Math.Abs(GlideslopeDeviationDeg) * 3 : UserElevator ? 0 : Math.Abs(AlignmentDeg);
            double band = WindPattern == PracticeWind.Shifting ? 5.0 : 3.0;   // ±3° / ±3 m; ±5 while the wind swaps sides
            _liveSec += dt; if (err <= band) _inBandSec += dt; _rmsAccum += err * err * dt;
        }

        // End: the departure end (level exercises), or stopped / off the far end (landings and flares).
        bool pastEnd = along > L - 40;
        if (Time > BriefingSec + 3 && s.Velocity.Length < 1.0 && agl < 3) _stillSec += dt; else _stillSec = 0;
        bool stopped = _stillSec > 2.0;
        bool offSide = Math.Abs(cross) > 60;
        if (Phase != PracticePhase.Finished && (pastEnd || stopped || offSide || ac.Structure.WingsFailed))
        {
            EndReason = pastEnd ? "departure end" : stopped ? "stopped" : offSide ? "off the side" : "airframe failed";
            Phase = PracticePhase.Finished;
            Score = ComputeScore();
        }

        LastInputs = new ControlInputs(outAil, outEle, outRud, lever);
        return LastInputs;
    }

    private ControlInputs StepAirwork(Aircraft ac, ControlInputs user, double dt, double roll, double pitch, double yaw, double agl, double hdot, double ias, Vec3 vAirBody)
    {
        RigidBodyState s = ac.State;
        double p = s.Rates.X, q = s.Rates.Y, r = s.Rates.Z;
        bool glider = Config.Propulsion is null;
        bool live = Phase == PracticePhase.Live;
        BankDeg = roll * 180 / Math.PI; RollRateDegS = p * 180 / Math.PI;
        if (Stalled && !_wasStalled) StallCount++;
        _wasStalled = Stalled;
        if (Stalled) { MaxSinkMs = Math.Max(MaxSinkMs, -hdot); MaxWingDropDeg = Math.Max(MaxWingDropDeg, Math.Abs(BankDeg)); }

        double ail = 0, ele, rud = 0, lever;
        double beta = ias > 3 ? Math.Asin(Math.Clamp(vAirBody.Y / ias, -1, 1)) : 0;
        BetaDeg = beta * 180 / Math.PI;
        if (double.IsNaN(_heading0)) _heading0 = yaw;
        HeadingDriftDeg = Wrap(yaw - _heading0) * 180 / Math.PI;
        if (Straight)
        {
            // Wings level is the user's job; the game holds altitude (or best glide) and speed, and the rudder stays neutral
            // so a bank is free to slip and weathervane — the lesson.
            double hErr = agl - AirworkAglM;
            if (glider)
            {
                double thetaCmd = _theta0 - 0.02 * (SpeedTargetMs - ias);
                ele = Math.Clamp(_elevTrim + 2.0 * (pitch - thetaCmd) + 0.8 * q, -0.9, 0.6);
                lever = 0.0;
            }
            else
            {
                double kva = Math.Clamp(Math.Pow(22.0 / Math.Max(ias, 8.0), 1.5), 0.25, 1.0);
                _elevInt = Math.Clamp(_elevInt + 0.03 * hErr * dt, -0.3, 0.3);
                ele = Math.Clamp(_elevTrim + ElevatorPower * (kva * (0.08 * hErr + 0.25 * hdot) + _elevInt) + 0.5 * q, -0.6, 0.45);
                double vErr = SpeedTargetMs - ias;
                _thrInt = Math.Clamp(_thrInt + 0.04 * vErr * dt, -0.5, 0.5);
                lever = 1 - 2 * Math.Clamp(_thr0 + 0.15 * vErr + _thrInt, 0, 1);
            }
            ail = Math.Clamp(-1.2 * roll - 0.5 * p, -1, 1);   // the game's own wings-level (briefing only)
            rud = 0.0;
            Autopilot = new ControlInputs(ail, ele, rud, lever);
            GameAileron = !live; GameRudder = true; GameElevator = true; GameThrottle = true;
            double outA = GameAileron ? ail : user.Aileron;
            if (live) { _liveSec += dt; if (Math.Abs(BankDeg) < 3) _inBandSec += dt; _rmsAccum += BankDeg * BankDeg * dt; }
            bool crashedS = agl < 2 || ac.Structure.WingsFailed;
            if (Phase != PracticePhase.Finished && crashedS) { EndReason = "hit the ground"; Phase = PracticePhase.Finished; Score = 0; Verdict = "That ended on the ground."; }
            LastInputs = new ControlInputs(outA, ele, 0.0, lever);
            return LastInputs;
        }
        if (STurn)
        {
            // Speed cycle: cruise → 1.15 Vso → cruise, a triangle over SpeedCycleSec; the test is exactly one cycle.
            if (live)
            {
                double f = (Time - BriefingSec) / SpeedCycleSec;
                Cycles = (int)Math.Floor(f);
                CycleFraction = f - Cycles;
            }
            double tri = 1.0 - Math.Abs(2.0 * CycleFraction - 1.0);   // 0 → 1 → 0
            SpeedTargetMs = CruiseMs + (1.15 * VsoMs - CruiseMs) * tri;
            // Bank cue: reverse the target once 45° is reached.
            if (TargetBankSign > 0 && BankDeg >= 44) { TargetBankSign = -1; Reversals++; }
            else if (TargetBankSign < 0 && BankDeg <= -44) { TargetBankSign = 1; Reversals++; }
            if (live) { _rollRateAccum += Math.Min(1.0, Math.Abs(RollRateDegS) / 45.0) * dt; _rollRateSamples += dt; _bankErrAccum += Math.Max(0, Math.Abs(BankDeg) - 47) * dt; }
            // Elevator: hold the altitude (powered) — the load factor in a 45° bank takes back stick; the height law does it.
            double hErr = agl - AirworkAglM;
            if (glider)
            {
                // Pitch for speed in the glide.
                double thetaCmd = _theta0 - 0.02 * (SpeedTargetMs - ias);
                ele = Math.Clamp(_elevTrim + 2.0 * (pitch - thetaCmd) + 0.8 * q, -0.9, 0.6);
                lever = 0.0;
            }
            else
            {
                // Altitude hold through the S-turns: bank feed-forward (a 45° bank needs 1.41 g of back stick), height and
                // climb-rate errors, pitch-rate damping, slow integral.
                double kva = Math.Clamp(Math.Pow(22.0 / Math.Max(ias, 8.0), 1.5), 0.25, 1.0);   // elevator power grows with V²
                double bankFf = -0.3 * (1.0 / Math.Max(0.5, Math.Cos(roll)) - 1.0);
                _elevInt = Math.Clamp(_elevInt + 0.03 * hErr * dt, -0.3, 0.3);
                ele = Math.Clamp(_elevTrim + ElevatorPower * (kva * (bankFf + 0.08 * hErr + 0.25 * hdot) + _elevInt) + 0.5 * q, -0.6, 0.45);
                double vErr = SpeedTargetMs - ias;
                _thrInt = Math.Clamp(_thrInt + 0.04 * vErr * dt, -0.5, 0.5);
                // Altitude has priority over the speed schedule: sinking away from the block, add power (a 45° bank at
                // 1.15 Vso is past the stall in the turn, so the bottom of the cycle mushes without it).
                double altBoost = Math.Clamp(-0.008 * hErr, 0, 0.5);
                lever = 1 - 2 * Math.Clamp(_thr0 + 0.15 * vErr + _thrInt + altBoost, 0, 1);
            }
            // Game aileron/rudder only during the briefing: wings level, coordinated.
            ail = Math.Clamp(-1.2 * roll - 0.5 * p, -1, 1);
            rud = Math.Clamp(-1.5 * beta - 0.5 * r, -1, 1);
        }
        else
        {
            lever = 1.0;   // power off in every stall exercise
            // Game-flown stalls (StallRudder): pull until stalled, hold 2 s, push to break, settle 4 s, repeat.
            _stallPhaseT += dt;
            if (_stallPhase == 0 && Stalled) { _stallPhase = 1; _stallPhaseT = 0; }
            else if (_stallPhase == 1 && _stallPhaseT > 2.0) { _stallPhase = 2; _stallPhaseT = 0; }
            else if (_stallPhase == 2 && (!Stalled && _stallPhaseT > 1.5)) { _stallPhase = 3; _stallPhaseT = 0; }
            else if (_stallPhase == 3 && _stallPhaseT > 4.0) { _stallPhase = 0; _stallPhaseT = 0; }
            double gameEle = _stallPhase switch
            {
                0 => Math.Clamp(_elevTrim - 0.06 * _stallPhaseT, -0.55, 0.5),      // ease the nose up
                1 => Math.Clamp(_elevTrim - 0.45, -0.6, 0.3),                       // hold it just past the stall
                2 => Math.Clamp(_elevTrim + 0.25, -0.9, 0.6),                       // break: nose down
                _ => Math.Clamp(_elevTrim + 0.15 * (1.3 * VsoMs - ias) + 0.6 * q, -0.9, 0.6),   // settle back to 1.3 Vso
            };
            ele = Kind == PracticeKind.StallRudder ? gameEle : user.Elevator;
            // The game's lateral help is capped at 10 % (owner): the wing drop is the user's to control.
            double cap = 0.10;
            ail = Math.Clamp(-1.2 * roll - 0.5 * p, -cap, cap);
            rud = Math.Clamp(-1.5 * beta - 0.6 * r - 0.5 * roll, -cap, cap);
            if (!live) { ail = Math.Clamp(-1.2 * roll - 0.5 * p, -1, 1); rud = Math.Clamp(-1.5 * beta - 0.5 * r, -1, 1); }
        }
        Autopilot = new ControlInputs(ail, ele, rud, lever);

        GameAileron = !(live && UserAileron); GameRudder = !(live && UserRudder); GameElevator = !(live && UserElevator); GameThrottle = true;
        if (Kind == PracticeKind.StallSideView) { GameAileron = true; GameRudder = true; }
        double outAil = GameAileron ? ail : user.Aileron;
        double outRud = GameRudder ? rud : user.Rudder;
        double outEle = GameElevator ? ele : user.Elevator;
        if (Kind is PracticeKind.StallRudder or PracticeKind.StallElevator && live)
        {
            // Rear-view stalls: the game's cap holds even when the axis is the game's; the user's rudder is unrestricted.
            if (Kind == PracticeKind.StallElevator) { outAil = ail; outRud = rud; }
        }

        if (live && Phase != PracticePhase.Finished)
        {
            _liveSec += dt;
            double err = Stall ? Math.Abs(BankDeg) : Math.Max(0, Math.Abs(BankDeg) - 47);
            if (Stall ? Math.Abs(BankDeg) < 15 : true) _inBandSec += dt;
            _rmsAccum += err * err * dt;
        }

        bool crashed = agl < 2 || ac.Structure.WingsFailed;
        bool testDone = Kind == PracticeKind.STurnsTest && Cycles >= 1;
        if (Phase != PracticePhase.Finished && (crashed || testDone))
        {
            EndReason = crashed ? (ac.Structure.WingsFailed ? "airframe failed" : "hit the ground") : "one speed cycle flown";
            Phase = PracticePhase.Finished;
            Score = ComputeAirworkScore(crashed);
        }
        LastInputs = new ControlInputs(outAil, outEle, outRud, lever);
        return LastInputs;
    }

    private double ComputeAirworkScore(bool crashed)
    {
        if (crashed) { Verdict = "That ended on the ground."; return 0; }
        if (STurn)
        {
            double rate = _rollRateSamples > 0 ? _rollRateAccum / _rollRateSamples : 0;      // 1 = 45°/s all the time
            double over = _liveSec > 0 ? _bankErrAccum / _liveSec : 0;                        // mean overshoot beyond 47°
            double sc = Math.Clamp(60 * rate + 40 * Math.Max(0, 1 - over / 10) , 0, 100);
            Verdict = sc >= 80 ? "Crisp." : sc >= 50 ? "Keep the rate up and stop at forty-five." : "Roll harder, reverse sooner.";
            return sc;
        }
        Verdict = ""; return InBandFraction * 100;
    }

    private double ComputeScore()
    {
        double band = InBandFraction * 100;
        if (!Descending) { Verdict = band >= 80 ? "Parallel — nicely held." : band >= 50 ? "Getting there." : "Keep the rudder working."; return band; }
        double td = 100;
        if (TouchedDown)
        {
            td -= Math.Max(0, TouchdownSinkMs - 0.6) * 25;                     // firm arrivals cost
            td -= Math.Max(0, Math.Abs(TouchdownAlignDeg) - 2) * 6;           // crabbed touchdowns cost
            td -= Math.Max(0, Math.Abs(TouchdownOffCentreM) - 3) * 3;
        }
        else td = 0;
        double s = Math.Clamp(FlareExercise ? td : 0.5 * band + 0.5 * td, 0, 100);   // approach: half path-keeping, half touchdown
        Verdict = !TouchedDown ? "No touchdown." : TouchdownSinkMs < 0.8 ? "Greaser." : TouchdownSinkMs < 1.6 ? "Firm but fine." : "That one hurt.";
        return s;
    }
}

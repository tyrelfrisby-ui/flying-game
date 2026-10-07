using System.Linq;
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
    ClimbLevelDescend,   // lesson: climb at Vy (full power), level at 75 % power, descend at cruise speed on 50 % power; the user picks the axes
    GlideRear,           // lesson: max L/D over the ground adjusted for head/tail wind — rear view
    GlideSide,           // the same, side view (pitch only)
    ClimbVyRear,         // lesson: best RATE of climb (Vy) — rear view, timed
    ClimbVxSide,         // lesson: best ANGLE of climb (Vx) — side view, measured over the ground
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

    public bool UserRudder => Selectable ? UserRudOn : Kind is PracticeKind.CrosswindRudder or PracticeKind.LandingRudder or PracticeKind.STurns or PracticeKind.STurnsTest or PracticeKind.StallRudder;
    public bool Selectable => CLD || Glide || ClimbLesson || STurn;
    public bool UserAileron => Selectable ? UserAilOn : Kind is PracticeKind.CrosswindAileron or PracticeKind.LandingAileron or PracticeKind.STurns or PracticeKind.STurnsTest or PracticeKind.Straight;
    public bool UserElevator => Selectable ? UserEleOn : Kind is PracticeKind.Flare or PracticeKind.FlareSideView or PracticeKind.ApproachSideView or PracticeKind.StallSideView or PracticeKind.StallElevator;
    public bool UserThrottle => Selectable ? UserThrOn : Kind == PracticeKind.ApproachSideView;
    public bool SideView => Kind is PracticeKind.FlareSideView or PracticeKind.ApproachSideView or PracticeKind.StallSideView or PracticeKind.GlideSide or PracticeKind.ClimbVxSide;
    public bool Approach => Kind == PracticeKind.ApproachSideView;
    public bool STurn => Kind is PracticeKind.STurns or PracticeKind.STurnsTest;
    public bool Stall => Kind is PracticeKind.StallSideView or PracticeKind.StallRudder or PracticeKind.StallElevator;
    public bool Straight => Kind == PracticeKind.Straight;
    public bool CLD => Kind == PracticeKind.ClimbLevelDescend;
    public bool Glide => Kind is PracticeKind.GlideRear or PracticeKind.GlideSide;
    public bool ClimbLesson => Kind is PracticeKind.ClimbVyRear or PracticeKind.ClimbVxSide;
    public bool Airwork => STurn || Stall || Straight || CLD || Glide || ClimbLesson;
    /// <summary>Which axes the USER flies (the game takes the rest). Fixed by the exercise, or chosen in the setup for the lessons.</summary>
    public bool UserAilOn, UserEleOn, UserRudOn, UserThrOn;
    /// <summary>Side views are longitudinal only: roll, yaw, sideslip and cross-track are held at zero after every step.</summary>
    public bool LongitudinalOnly => SideView;
    public bool Endless => Kind is PracticeKind.STurns or PracticeKind.StallSideView or PracticeKind.StallRudder or PracticeKind.StallElevator or PracticeKind.Straight;
    public bool Descending => !Airwork && Kind is not (PracticeKind.CrosswindRudder or PracticeKind.CrosswindAileron);
    public bool FlareExercise => UserElevator && !Approach;
    /// <summary>A jet (no propeller): the flare lessons fly it the way a jet lands (owner 2026-10-07) — on a 3° path at the
    /// approach speed with the power set for it, the game easing the thrust to idle from 50 ft to 5 ft.</summary>
    public bool Jet => Config.Propulsion is { PropDiameterM: <= 0.0 };
    public bool JetFlare => Jet && FlareExercise;
    public const double JetPathDeg = 3.0, JetStartFt = 200.0, ThrustOffStartFt = 50.0, ThrustOffEndFt = 5.0;
    /// <summary>Approach speed as a multiple of the stall speed in this configuration: the 737's Vref = 1.23 Vs; the
    /// F-86 a little more (tuned in the regimen for a stable, flareable final).</summary>
    public double JetApproachFactor => Config.Id.StartsWith("boeing-737") ? 1.23 : Config.Id.StartsWith("f86") ? F86ApproachFactor : 1.25;
    private const double F86ApproachFactor = 1.25;
    private double _thrHeld = -1;
    private const double LawGain = 1.0, LawDamp = 1.0;
    private double _jetPitchInt, _trimIdleStick, _thrLast;
    /// <summary>The jet landing law's tuning, per type (regimen 2026-10-07, swept against the game's own models): the
    /// 737 flares best on the path law about the idle-glide attitude (its approach attitude leaves no room before a tail
    /// strike: 4° of flare struck it), the F-86 about its approach attitude with a 4° flare from 40 ft.</summary>
    private (bool glideTheta, double ki, double flareDeg, double flareFt) JetTune => Config.Id.StartsWith("boeing-737") ? (true, 0.0, 3.0, 30.0) : (false, 0.5, 4.0, 40.0);
    private const double JetKTheta = 4.0, JetKq = 4.0, JetIntCap = 0.05;

    public PracticePhase Phase { get; private set; } = PracticePhase.Briefing;
    public double Time { get; private set; }
    public double BriefingLeft => Math.Max(0, BriefingSec - Time);
    public void SkipBriefing() { if (Phase == PracticePhase.Briefing) Phase = PracticePhase.Live; }
    /// <summary>Keep the briefing countdown parked (illustrated pages are being read).</summary>
    public void HoldBriefing() { if (Phase == PracticePhase.Briefing) Time = Math.Min(Time, 0.5); }

    // Live readouts (runway frame): + = right of the centreline / nose right of the runway heading.
    public double AlignmentDeg { get; private set; }
    /// <summary>Largest heading swing off the runway on the rollout (deg).</summary>
    public double MaxRolloutSwingDeg { get; private set; }
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
    private double _elevTrim, _elevInt, _thrInt, _thr0 = 0.45, _rudInt, _ailInt, _gndRudInt;
    private double _curBounceM, _gndSec;
    private readonly List<double> _bounceHeightsM = new(), _touchPitchDeg = new();
    /// <summary>A nose-wheel type touching with the nose this low (deg) arrived nose wheel first.</summary>
    private const double NoseFirstPitchDeg = 0.5;
    /// <summary>Touches that arrived nose wheel first (tricycle types; a taildragger can't).</summary>
    public int NoseFirstTouches => Taildragger ? 0 : _touchPitchDeg.Count(p => p < NoseFirstPitchDeg);
    public IReadOnlyList<double> BounceHeightsM => _bounceHeightsM;
    /// <summary>The idle-power landing lessons hand the user the WHEEL BRAKES (a glider: the spoiler handle, its last travel
    /// the wheel brake) — owner 2026-10-06 — and judge the braking on the roll-out.</summary>
    public bool UserBrakes => FlareExercise && !WaterLane;
    /// <summary>A seaplane lesson on a water lane (owner 2026-10-07): heights from the float keels / hull step, "touchdown" =
    /// the keel in the water, no wheel brakes, and the run ends once it is off the step (taxi speed).</summary>
    public bool WaterLane => Runway.Strip.Kind == "water";
    /// <summary>Lessons flown onto a runway (or a water lane) rather than airwork over the field.</summary>
    public static bool IsRunwayLesson(PracticeKind k) => k is PracticeKind.CrosswindRudder or PracticeKind.CrosswindAileron or PracticeKind.LandingRudder or PracticeKind.LandingAileron or PracticeKind.Flare or PracticeKind.FlareSideView or PracticeKind.ApproachSideView;
    private double _rollV0 = -1, _rollX0, _rollPrevGs = -1, _rollDecelF, _peakDecelG, _maxBrake; private bool _tailLifted, _tailWasDown, _brakingJudged;
    /// <summary>Roll-out: average deceleration from touchdown to stop (g), peak (g), and whether a taildragger's tail came up.</summary>
    public double RolloutAvgDecelG { get; private set; }
    public double RolloutPeakDecelG => _peakDecelG;
    public bool RolloutTailLifted => _tailLifted;
    public string EndReason { get; private set; } = "";
    private double _gustLevel, _gustTarget, _gustNextT, _gustRamp = 1;
    private readonly Random _rng;
    private double _lastAlongForShift, _stillSec, _crab0, _hT = double.NaN, _theta0, _betaF, _ailCmd;
    private bool _heightLawOn;
    /// <summary>Elevator-power scale for the game's pitch laws: 1.8°/° of trim slope = 1.0; a tail with twice the power gets half the gains.</summary>
    public double ElevatorPower { get; private set; } = 1.0;

    public ControlInputs LastInputs { get; private set; } = ControlInputs.Neutral;
    /// <summary>What the game would do on every axis this step (the user's axis included) — the briefing autopilot and the test yardstick.</summary>
    public ControlInputs Autopilot { get; private set; } = ControlInputs.Neutral;
    public bool GameAileron { get; private set; } = true; public bool GameElevator { get; private set; } = true;
    public bool GameRudder { get; private set; } = true; public bool GameThrottle { get; private set; } = true;

    public PracticeScenario(PracticeKind kind, PracticeWind wind, AircraftConfig config, WorldTerrain.RunwayEnd runway, double surfaceM, double crosswindMs = 4.0, int seed = 1, int userAxes = -1, double flapFraction = 0.0)
    // crosswindMs is the CAP; the actual crosswind scales with the type's speed. userAxes: bit 0 aileron, 1 elevator, 2 rudder, 3 throttle (−1 = the lesson's default)
    {
        if (userAxes < 0) userAxes = kind switch { PracticeKind.STurns => 0b0100, PracticeKind.STurnsTest => 0b0101, PracticeKind.ClimbLevelDescend => 0b1010, PracticeKind.GlideRear or PracticeKind.GlideSide => 0b0010, PracticeKind.ClimbVyRear => 0b0110, PracticeKind.ClimbVxSide => 0b0010, _ => 0 };
        UserAilOn = (userAxes & 1) != 0; UserEleOn = (userAxes & 2) != 0; UserRudOn = (userAxes & 4) != 0; UserThrOn = (userAxes & 8) != 0;
        Kind = kind; WindPattern = wind; Config = config; Runway = runway; SurfaceM = surfaceM;
        _rng = new Random(seed);
        // Flaps (owner 2026-10-03: pick the setting for the landing lessons): every speed and angle below is for THIS configuration.
        Flaps = Descending ? Math.Clamp(flapFraction, 0, 1) : 0.0;
        // The jets' flare lessons fly the landing flap the model can trim (owner 2026-10-07: 737 / F-86 on a 3° path): the
        // 737 full flaps; the F-86 clean — with its flaps down the nose-up pitch outruns the elevator (the real one trims
        // with an all-moving tail, not modelled yet).
        if ((kind is PracticeKind.Flare or PracticeKind.FlareSideView) && config.Propulsion is { PropDiameterM: <= 0.0 })
            Flaps = config.Id.StartsWith("f86") ? 0.0 : 1.0;
        // The idle-glide TABLE (owner: start every landing lesson from it) at the Valley; computed live elsewhere.
        Table = Math.Abs(surfaceM - WorldTerrain.DatumM) < 60 ? GlideTable.Lookup(config.Id, Flaps) : null;
        VsoMs = Table != null ? Table.VsoKt / 1.943844 : EstimateVso(config, surfaceM, Flaps);
        // Crosswind scaled to the type: ~20 % of the exercise speed (a Cub at 28 kt gets 5 kt, a 172 at 50 kt gets 8 kt) —
        // a fixed 8 kt asked a Cub for 16° of slip, beyond its rudder.
        CrosswindMs = crosswindMs > 0 ? Math.Clamp(0.16 * 1.15 * VsoMs, 2.0, crosswindMs) : 0;   // ≈0.18 Vso steady, 0.28 Vso in the gusts
        var mains = config.Gear.FindAll(g => !g.IsTailwheel && g.Pos[2] > 0 && Math.Abs(g.Pos[1]) < 3.5);
        var tws = config.Gear.FindAll(g => g.IsTailwheel);
        Taildragger = mains.Count > 0 && tws.Count > 0;
        StanceRad = Taildragger ? Math.Atan((mains[0].Pos[2] - tws[0].Pos[2]) / (mains[0].Pos[0] - tws[0].Pos[0])) : 0.0;
        double drop = 0; foreach (GearConfig g in config.Gear) if (!g.IsTailwheel) drop = Math.Max(drop, g.Pos[2] - config.Mass.CgVec().Z);
        if (config.Floats is FloatsConfig fk) drop = Math.Max(drop, fk.KeelZ);   // floats / hull: the keel at the step (the H-4 has no gear)
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
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, v, surfaceM + 50, flapFraction: Flaps, spoilerFraction: glider ? 0.5 : 0.0);
            double ratio = t.Converged && t.GlideRatio > 0 ? t.GlideRatio : ApproachSpawn.FindBestGlide(config, surfaceM + 50).GlideRatio;
            double gammaIdle = Math.Atan(1.0 / Math.Max(3.0, ratio));
            GlideslopeRad = glider ? gammaIdle : gammaIdle * 0.8;
            GlideslopeRad = Math.Clamp(GlideslopeRad, 2.5 * Math.PI / 180, 8.0 * Math.PI / 180);
        }
    }

    /// <summary>Flap setting for the landing lessons (0 up … 1 full).</summary>
    public double Flaps { get; }

    /// <summary>The flare lesson's starting path: the POWER-OFF glide at 1.3 Vso in this configuration (idle prop drag
    /// included) — the airplane starts on it, on speed and in trim, so all that is left is the round-out and flare.</summary>
    public double FlareGlideRad => _flareGlide ??= ComputeFlareGlide();
    private double? _flareGlide;
    /// <summary>This lesson's row of the idle-glide table (null: not in the table / not at the Valley — computed live).</summary>
    public GlideEntry? Table { get; }

    private double ComputeFlareGlide()
    {
        if (JetFlare) return JetPathDeg * Math.PI / 180;
        if (Table != null) return Table.GlideDeg * Math.PI / 180;
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(Config, 1.3 * VsoMs, SurfaceM + 15, flapFraction: Flaps, spoilerFraction: Config.Propulsion is null ? 0.5 : 0.0, idleProp: true);
        double ratio = t.Converged && t.GlideRatio > 1 ? t.GlideRatio : 8.0;
        return Math.Clamp(Math.Atan(1.0 / ratio), 2.0 * Math.PI / 180, 14.0 * Math.PI / 180);
    }

    /// <summary>Stick position that holds the spawn trim — preset on the pilot's pitch trim so hands-off flies the path.</summary>
    public double TrimStick => _elevTrim;
    public double ElevIntDbg, HErrDbg, HdotTDbg;
    private const double HErrCap = 1.0;   // m: 0.5 left the Archer too little pull, 1.5+ ballooned the Cub and the SR22 (regimen sweep)

    /// <summary>Approach: glideslope angle (positive = down) and where it meets the runway (150 m past the threshold).</summary>
    public double GlideslopeRad { get; }
    public const double AimPastThresholdM = 150.0;
    /// <summary>The flare lesson's aim point: the runway NUMBERS (their middle, ~220 ft past the threshold — owner 2026-10-03:
    /// "start it before the runway … the flight path right on the numbers at 100 ft").</summary>
    public const double NumbersPastThresholdM = 67.0, FlareStartFt = 100.0;
    public double FlareAimM => NumbersPastThresholdM;
    public double GlideslopeHeightAt(double along) => Math.Max(0, (AimPastThresholdM - along) * Math.Tan(GlideslopeRad));
    /// <summary>+ = above the glideslope (m).</summary>
    public double GlideslopeDeviationM { get; private set; }
    public double GlideslopeDeviationDeg { get; private set; }

    /// <summary>The SIM's stall speed for this type: the slowest speed the trim solver can hold below 12° of angle of attack
    /// (a CLmax guess from the wing area put a Cub's "1.15 Vso" below its real stall, and the height law pulled it into a
    /// stall in the first gust).</summary>
    public static double EstimateVso(AircraftConfig config, double altitudeM, double flapFraction = 0.0)
    {
        // Scan DOWN from a comfortable speed and stop at the first speed the trim solver can no longer hold below 14° of
        // angle of attack (scanning up found spurious low-speed solutions on some types).
        double vRef = config.SpawnIasMs > 1 ? config.SpawnIasMs : 25.0;
        // (With flaps the fast end may not trim at all — the elevator runs out nose-down — so failures before the first
        // trimmable speed are skipped; the stall is the first failure AFTER one.)
        double last = vRef; bool found = false;
        for (double v = vRef * 1.6; v >= vRef * 0.35; v -= 0.5)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, v, altitudeM, flapFraction: flapFraction);
            bool ok = t.Converged && !double.IsNaN(t.AlphaRad) && t.AlphaRad < 14.0 * Math.PI / 180 && t.GlideRatio > 0;
            if (!ok) { if (found) return last; continue; }
            found = true; last = v;
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
    public double VyMs { get; private set; }
    public double VxMs { get; private set; }
    public double RocAtVyMs { get; private set; }
    public double RocAtVxMs { get; private set; }
    public double BestLdMs { get; private set; }
    public double MinSinkMs { get; private set; }
    public double SpeedToFlyMs { get; private set; }
    public double BestGlideRatio { get; private set; }
    public double SpeedToFlyGroundRatio { get; private set; }   // ground distance per height at the speed to fly (in this wind)
    public int Leg { get; private set; }                        // climb/level/descend: 0/1/2
    public double LegT { get; private set; }
    public double StartAglM { get; private set; }
    public double TargetSpeedMs { get; private set; }
    public double AchievedRatio { get; private set; }           // glide: ground distance / height lost so far
    public double ClimbTimeSec { get; private set; }
    public double ClimbDistanceM { get; private set; }
    private double _climbStartAlong, _glideStartAlong;

    /// <summary>Power-off polar and full-power climb curve from the trim solver: best L/D, min sink, wind-corrected speed to fly,
    /// and Vy / Vx (rate = (thrust − drag)·V / W with thrust from power and propeller efficiency).</summary>
    private void ComputePerformance(double altM, double windAlong)
    {
        double vRef = Config.SpawnIasMs > 1 ? Config.SpawnIasMs : 25.0;
        double bestLd = 0, minSink = 1e9, bestGround = -1e9, bestRoc = -1e9, bestAngle = -1e9;
        double weight = Config.Mass.MassKg * 9.81;
        bool glider = Config.Propulsion is null;
        for (double v = Math.Max(8, VsoMs * 1.02); v <= vRef * 2.0; v += 0.5)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(Config, v, altM);
            if (!t.Converged || t.GlideRatio <= 0 || double.IsNaN(t.GlideRatio)) continue;
            // The power-OFF numbers (best glide, min sink, speed to fly) are with the prop idling; the climb numbers below
            // use the airframe's own drag (t) against full-power thrust.
            TrimSolver.Result ti = glider ? t : TrimSolver.SolveGliderTrim(Config, v, altM, idleProp: true);
            if (!ti.Converged || ti.GlideRatio <= 0 || double.IsNaN(ti.GlideRatio)) continue;
            double sink = v / ti.GlideRatio;
            if (ti.GlideRatio > bestLd) { bestLd = ti.GlideRatio; BestLdMs = v; }
            if (sink < minSink) { minSink = sink; MinSinkMs = v; }
            double ground = (v + windAlong) / sink;                    // distance over the ground per unit height
            if (ground > bestGround) { bestGround = ground; SpeedToFlyMs = v; }
            if (!glider)
            {
                (Vec3 tf, _) = PropModel.Compute(Config.Propulsion!, 1.0, new Vec3(v, 0, 0), Vec3.Zero, 1.2);   // the sim's own propeller (efficiency falls off at low speed)
                // Every engine (2026-10-06: the twins — Seminole, DC-3, AirCam — were climbing on one).
                double thrust = tf.X * Math.Max(1, Config.Engines?.Count ?? 1);
                double roc = (thrust - weight / t.GlideRatio) * v / weight;
                if (roc > bestRoc) { bestRoc = roc; VyMs = v; RocAtVyMs = roc; }
                double angle = roc / (v + windAlong);
                if (angle > bestAngle && roc > 0) { bestAngle = angle; VxMs = v; RocAtVxMs = roc; }
            }
        }
        BestGlideRatio = bestLd; SpeedToFlyGroundRatio = bestGround;
        if (VxMs <= 0) VxMs = 0.9 * VyMs;
    }
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
        PracticeKind.Straight => "Straight: wings level",
        PracticeKind.ClimbLevelDescend => "Climb, level, descend",
        PracticeKind.GlideRear => "Glide: speed to fly",
        PracticeKind.GlideSide => "Glide: speed to fly, side view",
        PracticeKind.ClimbVyRear => "Climb: best rate (Vy)",
        _ => "Climb: best angle (Vx), side view",
    } + WindPattern switch { PracticeWind.Gusty => " · gusty", PracticeWind.Shifting => " · shifting wind", PracticeWind.Headwind => " · headwind", PracticeWind.HeadwindGusty => " · gusty headwind", PracticeWind.Tailwind => " · tailwind", PracticeWind.TailwindGusty => " · gusty tailwind", _ => "" };

    public string Instructions => Kind switch
    {
        PracticeKind.CrosswindRudder => "The game flies the stick and the power: bank to hold the centreline, elevator for five feet, power for one point one five V S O. You have the rudder. Keep the fuselage parallel to the runway. Parallel is the goal, not centred.",
        PracticeKind.CrosswindAileron => "The game flies the rudder, the elevator and the power. You have the aileron. Stay over the centreline. Bank into the wind as much as it takes.",
        PracticeKind.LandingRudder => "The game flies the stick and the power down to a touchdown. You have the rudder all the way. Keep the fuselage parallel to the runway, through the touchdown and the roll-out.",
        PracticeKind.LandingAileron => "The game flies the rudder, the elevator and the power down to a touchdown. You have the aileron. Stay over the centreline; on the ground the rudder takes the direction, keep the aileron into the wind.",
        PracticeKind.Flare when JetFlare => "Two hundred feet on a three degree path at the approach speed, power set. From fifty feet the game eases the thrust to idle by five feet. The game keeps it straight and on the centreline. You have the elevator. Round out, hold it off, and let it settle.",
        PracticeKind.FlareSideView when JetFlare => "Side view. Two hundred feet on a three degree path, power set; the thrust comes back to idle between fifty and five feet. You have the elevator and the brakes. Round out, flare, and watch the weight on the wheels.",
        PracticeKind.Flare => "Fifty feet, power off, one point three V S O. The game keeps it straight and on the centreline. You have the elevator. Round out, hold it off, and let it settle.",
        PracticeKind.FlareSideView => "Side view. Fifty feet, power off. You have the elevator and the brakes. Round out, flare, and watch the weight on the wheels: elevator and braking shift it between the wheels.",
        PracticeKind.ApproachSideView => "Side view, on the glideslope at one point three V S O. You have the elevator and the power. Pitch for the glide path, power for the airspeed. The slope is a little shallower than the idle glide, so it takes a touch of power. Fly it down to the runway and land.",
        PracticeKind.STurns => "The game holds the altitude, cycles the speed from cruise down to one point one five V S O and back, and rolls: full aileron until the roll rate reaches forty five degrees a second, into a forty five degree bank, then straight back the other way, over and over. You have the rudder. Keep it coordinated through every reversal: rudder with the aileron, against the adverse yaw, more of it as the speed comes down.",
        PracticeKind.STurnsTest => "Scored: one speed cycle, cruise to one point one five V S O and back. Forty five degrees of bank to forty five the other way, at forty five degrees a second or full aileron, continuously. Roll rate and bank accuracy count.",
        PracticeKind.StallSideView => "Side view, power off, pitch only. You have the elevator. Bring the nose up and hold it until the wing stalls. Watch the bubbles: as it sinks, the relative wind at the tail comes from below, the tail force changes and the nose drops by itself. Hold it in the stall if you like and watch the sink rate build.",
        PracticeKind.StallRudder => "Power off. The game stalls the aircraft and breaks the stall, again and again, using no more than ten percent aileron. You have the rudder. When a wing drops, pick it up with rudder, not aileron.",
        PracticeKind.StallElevator => "Power off. You have the elevator. Stall it, then break the stall by lowering the angle of attack. The game holds it with no more than ten percent rudder and aileron. Recovery is angle of attack first, and the wing drop is a rudder job.",
        PracticeKind.Straight => "The game holds the altitude and the speed, and keeps the rudder neutral. You have the aileron. Keep the wings level and the nose will stay put. Let a bank develop and watch: the aircraft slips toward the low wing, then weathervanes into a turn.",
        PracticeKind.ClimbLevelDescend => "Three legs. Climb five hundred feet at V Y with full power. Level off and cruise at seventy five percent power. Then descend back down at cruise speed on fifty percent power. Pitch for the speed, power for the leg. Watch the prop yaw the nose left as the power goes in, and trim off the stick force each time the speed settles.",
        PracticeKind.GlideRear or PracticeKind.GlideSide => "Power off. The goal is the most ground for the height, so the speed to fly is best L over D corrected for the wind: faster into a headwind to penetrate, slower with a tailwind. The readout shows the speed to fly and the glide you are getting over the ground. The run ends after a thousand feet.",
        PracticeKind.ClimbVyRear => "Full power. V Y is the speed for the best RATE of climb, the most feet per minute. Hold it and the clock stops when you have gained a thousand feet. Faster or slower both cost time.",
        _ => "Full power, side view. V X is the speed for the best ANGLE of climb, the most height per foot of ground, the speed for clearing the trees at the end of the runway. Hold it and the run ends after a thousand feet; the score is how little ground it took.",
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
    public string UserAxesText
    {
        get
        {
            var l = new System.Collections.Generic.List<string>();
            if (UserAileron) l.Add("the aileron"); if (UserElevator) l.Add("the elevator"); if (UserRudder) l.Add("the rudder"); if (UserThrottle) l.Add(Config.Propulsion is null ? "the spoilers" : "the power");
            return l.Count == 0 ? "nothing — the game flies it" : string.Join(", ", l);
        }
    }

    public (string title, string text)[] LessonPages => CLD ? new[]
    {
        ("Prop yaw", "Seen from above at full power: the propeller's slipstream spirals around the fuselage and strikes the fin from the side, and the descending blade makes more thrust than the rising one. Both push the nose LEFT as the power goes in, most of all when slow and nose-high in the climb. It takes right rudder; the more power and the slower you fly, the more."),
        ("Trim", "Seen from behind: every speed has its own stick position. After you set the pitch for the climb, the cruise or the descent, the speed settles and the stick is still loaded. Trim moves that load to zero so the aircraft holds the speed hands-off. Set the power, pitch for the speed, wait for it to settle, then trim."),
    } : Glide ? new[]
    {
        ("Best glide and minimum sink", "The polar: sink rate against airspeed, power off. The lowest point is MINIMUM SINK, the speed that keeps you up longest. The line from the origin that just touches the curve marks BEST GLIDE, the speed that goes the farthest through the air, always a little faster than minimum sink."),
        ("Speed to fly in wind", "Seen from the side: the same aircraft from the same height, best glide against minimum sink. The wind moves the origin of that tangent line: into a headwind you must fly FASTER than best glide to penetrate; with a tailwind you fly SLOWER and let the wind carry you. That corrected speed is the speed to fly."),
    } : ClimbLesson ? new[]
    {
        ("Vx and Vy", "Rate of climb against airspeed at full power: the peak is V Y, the best RATE — the most feet per minute. The line from the origin that touches the curve marks V X, the best ANGLE — the most height per foot of ground, always slower than V Y."),
        ("Angle against rate", "Seen from the side from the same point: the V X climb is steeper and clears the obstacle in less distance; the V Y climb is shallower but reaches the altitude sooner. Obstacle ahead: V X. Nothing ahead: V Y."),
    } : Kind == PracticeKind.Straight ? new[]
    {
        ("Bank tilts the lift", "Seen from behind: the wing's lift stands straight up when the wings are level and balances the weight. Bank the wings and the lift tilts with them. Part of it now pulls sideways toward the low wing, and the aircraft starts to slide that way. That sideways motion is a slip."),
        ("The slip becomes a turn", "Seen from above: once the aircraft slides toward the low wing, the air comes at it from that side. The vertical tail is an arrow's feathers: it pushes the tail away from the wind, the nose swings toward it, and the aircraft weathervanes into a turn. Hold the wings level with aileron and none of that happens."),
    } : System.Array.Empty<(string, string)>();

    public double HeadingDriftDeg { get; private set; }
    public double BetaDeg { get; private set; }
    private double _heading0 = double.NaN;

    public string HandoverLine => Selectable ? "You have " + UserAxesText + "." : Straight ? "You have the aileron. Keep the wings level." : STurn ? "You have the aileron and rudder." : UserRudder ? "You have the rudder." : UserAileron ? "You have the aileron." : Approach ? "You have the elevator and the power." : "You have the elevator.";

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
        double wheels = Approach ? 91.44 : JetFlare ? JetStartFt * 0.3048 : FlareExercise ? FlareStartFt * 0.3048 : FiveFtM;   // WHEEL height (owner: "5 ft"; the flare: 100 ft on the idle glide)
        double agl = wheels + GearDropM;
        double v = JetFlare ? JetApproachFactor * VsoMs : (FlareExercise || Approach) ? 1.3 * VsoMs : 1.15 * VsoMs;
        double back = Approach ? wheels / Math.Tan(GlideslopeRad) - AimPastThresholdM : FlareExercise ? wheels / Math.Tan(FlareGlideRad) - NumbersPastThresholdM : 0;   // the idle glide path runs onto the NUMBERS — the start distance varies with the glide angle
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
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(Config, v, SurfaceM + agl, flapFraction: Flaps, spoilerFraction: Config.Propulsion is null && (Approach || FlareExercise) ? 0.5 : 0.0, idleProp: FlareExercise);   // the flare starts power-off
        _theta0 = trim.Converged ? trim.ThetaRad : 0.0;
        double gamma = Approach ? -GlideslopeRad : FlareExercise ? -FlareGlideRad : 0;
        double alpha = trim.Converged ? trim.AlphaRad : 0.08;
        bool fromTable = FlareExercise && Table != null && !JetFlare;   // the idle-glide table is not a jet's 3° powered path     // start exactly on the table's row: idle, on speed, trimmed
        if (fromTable) alpha = Table!.AlphaDeg * Math.PI / 180;
        double pitch = alpha + gamma;
        if (JetFlare && !JetTune.glideTheta) _theta0 = pitch;   // the approach attitude on the 3° powered path (the glide trim's pitch is the idle glide's)
        double hh = heading / 2, hp = pitch / 2;
        var att = Quat.Multiply(new Quat(0, 0, Math.Sin(hh), Math.Cos(hh)), new Quat(0, Math.Sin(hp), 0, Math.Cos(hp)));
        // Ground velocity along the runway: air velocity along the heading plus the wind.
        double vAlong = Math.Sqrt(Math.Max(0, v * v - wCross * wCross)) + (w.X * Runway.AlongX + w.Y * Runway.AlongY);
        var vWorld = new Vec3(Runway.AlongX * vAlong * Math.Cos(gamma), Runway.AlongY * vAlong * Math.Cos(gamma), -vAlong * Math.Sin(gamma));
        var state = new RigidBodyState(pos, att, att.Conjugate().Rotate(vWorld), Vec3.Zero);
        double elevRad = fromTable ? Table!.ElevatorDeg * Math.PI / 180 : trim.Converged ? trim.ElevatorRad : ApproachSpawn.ApproachGlide(Config, SurfaceM + agl).ElevatorRad;
        _elevTrim = Aircraft.StickForDeflection(elevRad, Config.Controls.Elevator);
        // Power for the exercise: thrust = drag (weight / glide ratio) less the gravity component along the path.
        double ratio = trim.Converged && trim.GlideRatio > 0 ? trim.GlideRatio : 8.0;
        double weight = Config.Mass.MassKg * 9.81;
        double thrustNeeded = weight / ratio + weight * Math.Sin(gamma);   // gamma negative on a descent: less thrust
        double eff = Config.Propulsion?.Efficiency > 0 ? Config.Propulsion.Efficiency : 0.75;
        double maxP = Config.Propulsion?.MaxPowerW > 0 ? Config.Propulsion.MaxPowerW : 100000;
        _thr0 = FlareExercise || Config.Propulsion is null ? 0.0 : Math.Clamp(thrustNeeded * v / (eff * maxP), 0.0, 0.9);
        if (JetFlare) _thr0 = Math.Clamp(thrustNeeded / (maxP * Math.Max(1, Config.Engines.Count)), 0.0, 0.9);   // jet: thrust = max × throttle per engine
        _thrHeld = -1; _thrLast = _thr0;
        if (JetFlare)
        {
            // Powered trim: the glide trim ignores the thrust's pitching moment (the 737's engines hang 1.9 m below the CG
            // — set for the 3° path it pitched up 11° in two seconds and stalled). Find the elevator that holds the
            // attitude with this thrust: a few short trial steps, secant on the pitch rate they build.
            double QAfter(double e)
            {
                var t = new Aircraft(Config, state, new ControlDeflections(0, e, 0, 0, Flaps)) { FlapFraction = Flaps };
                new SimLoop(t).RunFor(0.2, new ControlInputs(0, Aircraft.StickForDeflection(e, Config.Controls.Elevator), 0, 1 - 2 * _thr0));
                return t.State.Rates.Y;
            }
            double e0 = elevRad, e1 = elevRad - 0.02, q0 = QAfter(e0), q1 = QAfter(e1);
            for (int i = 0; i < 12 && Math.Abs(q1) > 1e-4 && Math.Abs(q1 - q0) > 1e-9; i++)
            {
                double e2 = Math.Clamp(e1 - q1 * (e1 - e0) / (q1 - q0), -Config.Controls.Elevator.MaxDeflRad, Config.Controls.Elevator.MaxDeflRad);
                e0 = e1; q0 = q1; e1 = e2; q1 = QAfter(e1);
            }
            _trimIdleStick = _elevTrim;   // the power-off glide trim at this speed
            if (double.IsFinite(e1)) { elevRad = e1; _elevTrim = Aircraft.StickForDeflection(elevRad, Config.Controls.Elevator); }
        }
        var ac = new Aircraft(Config, state, new ControlDeflections(0, elevRad, 0, Config.Propulsion is null && (Approach || FlareExercise) ? 0.5 : 0, Flaps));
        ac.FlapFraction = Flaps;   // the setting chosen on the lesson page
        return ac;
    }

    /// <summary>Airwork: 2,600 ft over the runway centre, heading along it, trimmed at cruise (S-turns) or 1.3 Vso (stalls).</summary>
    private Aircraft SpawnAirwork()
    {
        CruiseMs = EstimateCruise75(Config, SurfaceM + AirworkAglM, VsoMs);
        Vec3 w0 = WindVector(0, 0);
        ComputePerformance(SurfaceM + AirworkAglM, w0.X * Runway.AlongX + w0.Y * Runway.AlongY);
        double v = STurn || (Straight && Config.Propulsion is not null) ? CruiseMs
                 : Straight ? ApproachSpawn.FindBestGlide(Config, SurfaceM + AirworkAglM).SpeedMs
                 : CLD ? (Config.Propulsion is null ? BestLdMs : VyMs)
                 : Glide ? SpeedToFlyMs
                 : Kind == PracticeKind.ClimbVyRear ? VyMs : Kind == PracticeKind.ClimbVxSide ? VxMs : 1.3 * VsoMs;
        if (!double.IsFinite(v) || v <= 0) v = double.IsFinite(BestLdMs) && BestLdMs > 0 ? BestLdMs : 1.5 * VsoMs;   // a glider has no Vy/Vx (NaN spawn)
        SpeedTargetMs = v; TargetSpeedMs = v; StartAglM = AirworkAglM;
        double alt = SurfaceM + AirworkAglM;
        var pos = new Vec3(Runway.CentreX, Runway.CentreY, -alt);
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(Config, v, alt, idleProp: Glide);   // the glide lessons are power-off
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
        _thr0 = glider || Stall || Glide ? 0.0 : (CLD || ClimbLesson) ? 1.0 : Math.Clamp(weight / ratio * v / (eff * maxP), 0.0, 1.0);
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
    /// <summary>Merge the user's axis into the autopilot's inputs for this step; drive the wind and the score; judge it.</summary>
    public ControlInputs Step(Aircraft ac, ControlInputs user, double dt)
    {
        ControlInputs c = StepCore(ac, user, dt);
        JudgeStep(ac, dt);
        // Never hand the sim a NaN (it throws): a lesson law that can't compute (a glider in a climb lesson) flies neutral.
        static double Ok(double v) => double.IsFinite(v) ? v : 0.0;
        if (!double.IsFinite(c.Aileron) || !double.IsFinite(c.Elevator) || !double.IsFinite(c.Rudder) || !double.IsFinite(c.ThrottleLever))
            c = new ControlInputs(Ok(c.Aileron), Ok(c.Elevator), Ok(c.Rudder), double.IsFinite(c.ThrottleLever) ? c.ThrottleLever : 1.0, c.AileronFree, c.ElevatorFree, c.RudderFree);
        return c;
    }

    // ---- points (owner 2026-10-03): the orb + the judged moments ------------------------------------------------------
    /// <summary>The lesson's judge: live grade (the orb), points, judged moments.</summary>
    public LessonJudge Judge => _judge ??= new LessonJudge(LessonJudge.For(Kind, Taildragger));
    private LessonJudge? _judge;
    private bool _judgedTouchdown, _judgedEnd, _flareStarted, _onGroundPrev, _damageNoted;
    private int _touches; private double _hardestSinkMs, _maxBounceM, _groundedT, _touchSinkWindow;
    private double _flarePitchRef = double.NaN, _flareSinkRef, _roundOutFt = -1, _stalledT, _stallOnsetAgl = double.NaN, _stallMinAgl, _sinceUnstallT;
    public double RoundOutFt => _roundOutFt;
    public const double TouchdownBeyondAimM = 100.0;

    private void JudgeStep(Aircraft ac, double dt)
    {
        LessonJudge j = Judge;
        (_, double pitch, _) = Euler(ac.State.Attitude);
        double pitchDeg = pitch * 180 / Math.PI;
        var live = j.Rules.Live;

        // The end of a run: the result moment, or the crash.
        if (Phase == PracticePhase.Finished)
        {
            if (_judgedEnd) return;
            j.Now = Time;
            _judgedEnd = true;
            if (EndReason is "hit the ground" or "airframe failed") { j.Crash(EndReason == "airframe failed" ? "Airframe failed" : "Hit the ground"); return; }
            foreach (Criterion m in j.Rules.Moments)
                if (m.Name == LessonJudge.Std.Result.Name) j.Moment(m, Math.Max(0, 100 - Score), $"{Score:F0} % of the best");
            return;
        }
        if (Phase != PracticePhase.Live) return;

        j.Now = Time;
        Grade g = Grade.Green; string lim = "";
        double[] errs = new double[live.Count];
        void W(Criterion c, double err)
        {
            Grade gg = c.Rate(err);
            int i = live.IndexOf(c); if (i >= 0) errs[i] = err;
            if (gg == Grade.Red && j.Current != Grade.Red) j.NoteRed(c, err);
            if (gg > g) { g = gg; lim = c.Name; }
        }
        double kt = 1.943844, ft = 3.28084;
        switch (Kind)
        {
            case PracticeKind.CrosswindRudder: case PracticeKind.LandingRudder: W(live[0], AlignmentDeg); break;
            case PracticeKind.CrosswindAileron: case PracticeKind.LandingAileron: W(live[0], OffCentreM * ft); break;
            case PracticeKind.Flare: case PracticeKind.FlareSideView: case PracticeKind.ApproachSideView:
                if (!TouchedDown)
                {
                    // Round-out: the first nose-up of more than 2° below 60 ft, measured against the attitude at 60 ft.
                    // (or the sink falling below 70 % of the approach sink measured there — the path flattening).
                    if (MainsAglM < 18.3 && double.IsNaN(_flarePitchRef)) { _flarePitchRef = pitchDeg; _flareSinkRef = Math.Max(1.0, SinkMs); }
                    if (!_flareStarted && !double.IsNaN(_flarePitchRef) && (pitchDeg - _flarePitchRef > 2.0 || SinkMs < 0.7 * _flareSinkRef)) { _flareStarted = true; _roundOutFt = MainsAglM * ft; }
                    if (MainsAglM > 6.1)
                    {
                        if (Approach) { W(live[0], GlideslopeDeviationDeg); W(live[1], (AirspeedMs - 1.3 * VsoMs) * kt); }
                    }
                    else
                    {
                        Criterion fs = live[live.Count - 1].Name == LessonJudge.Std.FlareSink.Name ? live[live.Count - 1] : LessonJudge.Std.FlareSink;
                        double ideal = MainsAglM / 5.0 + 0.1;                         // m/s: sink shrinking with the height
                        double err = SinkMs < -0.3 ? 999 : Math.Abs(SinkMs - ideal) * 196.85;   // ballooning = red
                        W(fs, err);
                    }
                }
                break;
            case PracticeKind.STurns: W(live[0], BetaDeg); break;
            case PracticeKind.STurnsTest: W(live[0], BetaDeg); if (Math.Abs(BankDeg) > 40) W(live[1], Math.Max(0, Math.Abs(BankDeg) - 45)); break;
            case PracticeKind.Straight: W(live[0], BankDeg); break;
            case PracticeKind.StallRudder: W(live[0], BankDeg); break;
            case PracticeKind.StallSideView: case PracticeKind.StallElevator:
                if (Stalled) { _stalledT += dt; W(live[0], _stalledT); } else _stalledT = 0;
                break;
            case PracticeKind.ClimbLevelDescend:
                if (Leg == 1 || Leg == 3) W(live[1], (AglM - (StartAglM + 152.4)) * ft); else W(live[0], (AirspeedMs - TargetSpeedMs) * kt);
                break;
            case PracticeKind.GlideRear: case PracticeKind.GlideSide: case PracticeKind.ClimbVyRear: case PracticeKind.ClimbVxSide:
                W(live[0], (AirspeedMs - TargetSpeedMs) * kt); break;
        }
        j.Tick(g, dt, lim);
        j.AddSample(g, errs, AlongM, OffCentreM, Airwork ? AglM : MainsAglM, AirspeedMs, SinkMs, BankDeg, pitchDeg);

        // The whole ARRIVAL, not the first touch (owner 2026-10-03: "I bounced, then hit so hard it broke apart, and it said
        // firm but fine"): every touch is followed; the hardest one counts, the highest bounce is graded, and the landing is
        // judged once it has stayed down 1.5 s (or the run ends). Damage on the way is a crash.
        if (TouchedDown)
        {
            if (OnGround && !_onGroundPrev)
            {
                _touches++; _hardestSinkMs = Math.Max(_hardestSinkMs, Math.Max(SinkMs, TouchdownSinkMs));
                // Every arrival is kept (owner 2026-10-06: "it porpoised down the runway with every contact nose wheel first …
                // a score of 423 with one simple comment 'bounced'"): the height of the bounce that led to it, and its pitch.
                if (_touches > 1) _bounceHeightsM.Add(_curBounceM);
                _touchPitchDeg.Add(pitchDeg);
                _curBounceM = 0;
            }
            if (!OnGround && _touches > 0) _curBounceM = Math.Max(_curBounceM, MainsAglM);
            if (UserBrakes && TouchedDown) _maxBrake = Math.Max(_maxBrake, ac.BrakeInput);
            if (UserBrakes && OnGround && _groundedT >= 1.0)   // from the moment it has settled (not the touchdown's own bump)
            {
                Vec3 vg = ac.State.Attitude.Rotate(ac.State.Velocity);
                double gs = Math.Sqrt(vg.X * vg.X + vg.Y * vg.Y);
                if (_rollV0 < 0) { _rollV0 = gs; _rollX0 = AlongM; }
                if (_rollPrevGs >= 0 && dt > 0)
                {
                    double d = (_rollPrevGs - gs) / dt / 9.81;
                    _rollDecelF += (d - _rollDecelF) * Math.Min(1.0, dt / 0.3);   // 0.3 s smoothing: the tyre's bite, not the bumps
                    if (gs > 2 && ac.BrakeInput > 0.05) _peakDecelG = Math.Max(_peakDecelG, _rollDecelF);   // the BRAKING's peak
                }
                _rollPrevGs = gs;
                _maxBrake = Math.Max(_maxBrake, ac.BrakeInput);
                if (Taildragger && pitchDeg > StanceRad * 180 / Math.PI - 1.5) _tailWasDown = true;
                // Braked so hard the tail came UP (it had been down): a wheel landing still rolling tail-high when the brakes
                // came on is not a nose-over (regimen 2026-10-06: the Cub's light braking was judged red).
                if (Taildragger && _tailWasDown && gs > 3 && ac.BrakeInput > 0.05 && pitchDeg < StanceRad * 180 / Math.PI - 4) _tailLifted = true;
            }
            if (OnGround && _touches > 0) _groundedT += dt; else _groundedT = 0;
            if (!OnGround && _touches > 0) _maxBounceM = Math.Max(_maxBounceM, MainsAglM);
            if (TouchedDown && OnGround && ac.State.Velocity.Length > 2) MaxRolloutSwingDeg = Math.Max(MaxRolloutSwingDeg, Math.Abs(AlignmentDeg));   // the rollout counts too (a ground loop was scoring "Greaser.")
            if (OnGround) _hardestSinkMs = Math.Max(_hardestSinkMs, _touchSinkWindow > 0 ? SinkMs : 0);
            _touchSinkWindow = OnGround && !_onGroundPrev ? 0.15 : Math.Max(0, _touchSinkWindow - dt);
            _onGroundPrev = OnGround;
            if (!_damageNoted && ac.LostComponents.Count > 0) { _damageNoted = true; j.Crash("Airframe damaged on landing"); }
        }
        if (TouchedDown && !_judgedTouchdown && (_groundedT >= 1.5 || Phase == PracticePhase.Finished))
        {
            _judgedTouchdown = true;
            // The judged touchdown point: 100 m (330 ft) past where the path meets the runway — the flare carries you that
            // far (the "1,000 ft markers" for a path aimed ~500 ft in).
            double aim = (FlareExercise ? NumbersPastThresholdM : AimPastThresholdM) + TouchdownBeyondAimM;
            foreach (Criterion m in j.Rules.Moments)
            {
                string n = m.Name;
                if (n == LessonJudge.Std.TdSink.Name) j.Moment(m, _hardestSinkMs * 196.85, _touches > 1 ? $"{_hardestSinkMs * 196.85:F0} fpm (hardest of {_touches} touches)" : $"{_hardestSinkMs * 196.85:F0} fpm");
                else if (n == LessonJudge.Std.Bounce.Name)
                {
                    // EACH bounce is judged (and costs points), not just the highest; a nose-wheel-first arrival costs again.
                    if (_bounceHeightsM.Count == 0) j.Moment(m, _maxBounceM * 3.28084, "none");
                    for (int b = 0; b < _bounceHeightsM.Count; b++)
                    {
                        bool nose = !Taildragger && b + 1 < _touchPitchDeg.Count && _touchPitchDeg[b + 1] < NoseFirstPitchDeg;
                        j.Moment(m, _bounceHeightsM[b] * 3.28084, $"bounce {b + 1}: {_bounceHeightsM[b] * 3.28084:F1} ft{(nose ? ", back down nose wheel first" : "")}");
                        if (nose) j.Moment(LessonJudge.Std.NoseWheelFirst, 1, $"touch {b + 2}: nose wheel first");
                    }
                    if (!Taildragger && _touchPitchDeg.Count > 0 && _touchPitchDeg[0] < NoseFirstPitchDeg) j.Moment(LessonJudge.Std.NoseWheelFirst, 1, "first touch: nose wheel first");
                }
                else if (n == LessonJudge.Std.Braking.Name) { }   // judged when the roll-out ends
                else if (n == LessonJudge.Std.TdSpeed.Name) { double r = AirspeedMs / Math.Max(1, VsoMs); j.Moment(m, Math.Max(0, (r - 1) * 100), $"{AirspeedMs * kt:F0} kt ({r:F2} Vso)"); }
                else if (n == LessonJudge.Std.TdPoint.Name) { double d = (AlongM - aim) * ft; j.Moment(m, d < -100 ? 500 : Math.Max(0, d), d < 0 ? $"{-d:F0} ft short" : $"{d:F0} ft past"); }
                else if (n == LessonJudge.Std.TdAlign.Name) j.Moment(m, TouchdownAlignDeg, $"{Math.Abs(TouchdownAlignDeg):F1}°");
                else if (n == LessonJudge.Std.TdCentre.Name) j.Moment(m, TouchdownOffCentreM * ft, $"{Math.Abs(TouchdownOffCentreM) * ft:F0} ft off");
                else if (n.StartsWith("Three-point")) { double p0 = _touchPitchDeg.Count > 0 ? _touchPitchDeg[0] : pitchDeg; j.Moment(m, p0 - StanceRad * 180 / Math.PI, $"{p0:F1}° at the first touch (stance {StanceRad * 180 / Math.PI:F1}°)"); }
                else if (n.StartsWith("Nose-high")) { double p0 = _touchPitchDeg.Count > 0 ? _touchPitchDeg[0] : pitchDeg; j.Moment(m, Math.Max(0, 3 - p0), $"{p0:F1}° nose up at the first touch"); }   // (was the pitch 1.5 s later, settled)
                else if (n == LessonJudge.Std.RoundOut.Name)
                {
                    double ro = _roundOutFt;
                    double err = ro < 0 ? 99 : ro < 10 ? 10 - ro : ro > 20 ? ro - 20 : 0;
                    j.Moment(m, err, ro < 0 ? "no round-out" : $"{ro:F0} ft");
                }
            }
        }

        // Stall recoveries: height lost from the break to the bottom of the recovery.
        if (Kind is PracticeKind.StallSideView or PracticeKind.StallElevator)
        {
            if (Stalled && double.IsNaN(_stallOnsetAgl)) { _stallOnsetAgl = AglM; _stallMinAgl = AglM; _sinceUnstallT = 0; }
            if (!double.IsNaN(_stallOnsetAgl))
            {
                _stallMinAgl = Math.Min(_stallMinAgl, AglM);
                if (!Stalled) _sinceUnstallT += dt; else _sinceUnstallT = 0;
                if (_sinceUnstallT > 0.5 && (SinkMs <= 0 || _sinceUnstallT > 10))
                {
                    double loss = (_stallOnsetAgl - _stallMinAgl) * ft;
                    foreach (Criterion m in j.Rules.Moments) if (m.Name == LessonJudge.Std.StallLoss.Name) j.Moment(m, loss, $"{loss:F0} ft");
                    _stallOnsetAgl = double.NaN;
                }
            }
        }
    }

    private ControlInputs StepCore(Aircraft ac, ControlInputs user, double dt)
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
        if (Config.Floats is FloatsConfig fl)
        {
            // The step of the float keel (or the hull) — the H-4 has no gear entries at all.
            double stepX = fl.BowX - fl.StepFraction * fl.LengthM;
            foreach (double side in fl.SpreadM > 0.1 ? new[] { -fl.SpreadM / 2, fl.SpreadM / 2 } : new[] { 0.0 })
            {
                Vec3 w = s.Attitude.Rotate(new Vec3(stepX, side, fl.KeelZ));
                mainsAgl = Math.Min(mainsAgl, agl - w.Z);
            }
        }
        bool ground = LandingGear.AnyMainWheelOnGround(Config, s) || (WaterLane && mainsAgl <= 0.03);

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
        // On the ground: a tailwheel pilot answers the YAW RATE, fast and hard, before the heading error builds (owner
        // 2026-10-06: "the pilot moves the rudder rather slowly on the stearman landing — it ground looped … move the rudder
        // very quickly so as not to get behind the divergence"). Rate gain 0.8 → 3.0: the Stearman went round 180° in gusts.
        // Once down it STAYS on the ground law (a skipping main flipped it back to the flying law every half second and slammed
        // the rudder −1/+0.85 — that started the Stearman's swing). The heading and centreline terms are capped so the RATE
        // term always wins: uncapped, a 70° heading error held full rudder while the swing reversed at 60°/s and it looped
        // the other way.
        if (ground || TouchedDown)
        {
            // Cascade, the way a tailwheel pilot flies the rollout: the heading (and centreline) ask for a yaw RATE back
            // toward the runway, ≤ ~20°/s; the rudder chases that rate hard and fast (the reflex that catches a swing before
            // it diverges); a slow integral holds the steady crosswind rudder (a cap on the outer terms alone left too little
            // rudder to stop the weathervane).
            // Gains (owner 2026-10-06, AirCam: "the rudder moves way too slowly, it almost ground looped — 10x faster"): the
            // old rate gain (4 per rad/s) put in a tenth of the rudder for a 5°/s swing and let a slow oscillation grow on the
            // roll-out. Now a few °/s of swing or of heading error is a big boot of rudder, as a tailwheel pilot's feet do.
            const double kR = 30.0, kPsi = 2.5;   // 4 and 1.2 before; 60 chattered on the Pitts at 30 fps
            double rCmd = Math.Clamp(-kPsi * psiErr - 0.03 * cross, -0.35, 0.35);
            _gndRudInt = Math.Clamp(_gndRudInt - 0.8 * psiErr * dt, -0.6, 0.6);
            rud = Math.Clamp(-kR * (r - rCmd) + _gndRudInt, -1, 1);
        }
        GameBrake = !UserBrakes && !WaterLane && (ground || TouchedDown) && Descending ? 0.35 : 0.0;   // (the brakes lessons: the user's)   // roll-out braking; the host biases it with the rudder (differential braking steers)
        GameBrakeBias = GameBrake > 0 ? Math.Clamp(rud * 0.8, -1, 1) : 0;
        // Rudder out of authority (full, slowing down, the swing still growing): stand on the inside brake, hard — a tailwheel
        // pilot's last tool before the loop (the Stearman went round in the gusty crosswind with full rudder and 0.35 brake).
        if (GameBrake > 0 && Math.Abs(rud) > 0.95) { GameBrake = 0.6; GameBrakeBias = Math.Sign(rud); }

        // Height target for the MAINS along the runway (a function, so its slope gives the target descent rate).
        double L = Runway.LengthM;
        double HTargetAt(double a)
        {
            if (!Descending) return FiveFtM;
            if (FlareExercise || Approach)
            {
                double aimAlong = FlareExercise ? NumbersPastThresholdM : AimPastThresholdM;
                double pathH = FlareExercise ? Math.Max(0, (aimAlong - a) * Math.Tan(FlareGlideRad)) : GlideslopeHeightAt(a);
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
        // The hold-off: in the last 2 ft it only settles as the speed goes — still fast (≥ 1.15 Vso) it holds it off, near
        // the stall it lets it down. Settling at 0.3 m/s regardless put the Pitts on at 64 kt and 8° and it skipped down
        // the runway (regimen 2026-10-06).
        if ((FlareExercise || Approach) && mainsAgl < 0.6) maxSink *= Math.Clamp((1.15 * VsoMs - ias) / (0.15 * VsoMs), 0.15, 1.0);
        double hPrev = _hT;
        if ((FlareExercise || Approach) && _heightLawOn) hProfile = Math.Min(hProfile, _hT);   // a flare never climbs back to the path
        _hT += Math.Clamp(hProfile - _hT, -maxSink * dt, 2.0 * dt);
        double hTarget = _hT;
        double hdotTarget = (hTarget - hPrev) / Math.Max(dt, 1e-3);
        if ((FlareExercise || Approach) && _heightLawOn) hdotTarget = Math.Min(hdotTarget, 0.0);   // a flare never asks for a climb
        double vTarget = 1.15 * VsoMs;
        if (FlareExercise || Approach) vTarget = hTarget < 0.4 ? 0.85 * VsoMs : (JetFlare ? JetApproachFactor : 1.3) * VsoMs;   // bleed the speed only once in the hold-off
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
            // Through the round-out the height error is capped at a metre either way (regimen 2026-10-06): a fast type
            // sinking 5 m/s fell 2.6 m behind the rate-limited target, the error (and its integral) wound in back stick, it
            // ballooned 3 m and the law then pushed it into the ground — every faster type broke its gear in the flare
            // lesson. The descent-rate term asks for the round-out; the height term only fine-tunes it.
            if ((FlareExercise || Approach) && mainsAgl < 15 && !ground) hErr = Math.Clamp(hErr, -HErrCap, HErrCap);
            bool roundOut = (FlareExercise || Approach) && mainsAgl < 10 && !ground;   // no integration through the round-out (windup → balloon)
            if (!gliderPathLaw && !roundOut) _elevInt = Math.Clamp(_elevInt + 0.08 * hErr * dt, -0.5, 0.5);
            ElevIntDbg = _elevInt; HErrDbg = hErr; HdotTDbg = hdotTarget;
            // Gains scaled down with speed (elevator power grows with V²): the same law that flares a 2-33 at 20 m/s hunted
            // in pitch with a Cub at 34 m/s.
            double kv = Math.Clamp(Math.Pow(22.0 / Math.Max(ias, 8.0), 1.5), 0.35, 1.2);
            // Pitch-rate damping stays at full strength (scaling it away let the Cub porpoise in the hold-off).
            ele = Math.Clamp(_elevTrim + ElevatorPower * (LawGain * kv * (0.45 * hErr + 0.55 * (hdot - hdotTarget)) + _elevInt) + LawDamp * 0.6 * q, -0.9, 0.6);
            // Near the ground a pilot never shoves the nose down: after a bounce the DC-3's law pushed full forward to chase
            // the target, pitched −10° and drove it in (regimen 2026-10-06). Through the round-out the stick goes no further
            // forward than just past trim, and below 5 m the nose is held at or above level.
            if (roundOut)
            {
                ele = Math.Min(ele, _elevTrim + 0.1);
                if (mainsAgl < 5) ele = Math.Min(ele, Math.Clamp(-(0.0 - pitch) * 2.0 + 0.6 * q, -0.6, 0.6));
            }
            if (JetFlare && !ground)
            {
                // A jet lands by ATTITUDE (owner 2026-10-07: 737 / F-86 on a 3° path): fly the path with small pitch changes
                // about the approach attitude, then from ~30 ft raise the nose a few degrees and hold it — the height-chasing
                // law built for the light types over-flared the 737 to 9.6° at 138 kt and ballooned it 20 ft.
                double pathH = Math.Max(0, (NumbersPastThresholdM - along) * Math.Tan(FlareGlideRad));
                double flareFt = JetTune.flareFt, h = mainsAgl / 0.3048;
                double thetaCmd = _theta0 - 0.03 * Math.Clamp(mainsAgl - pathH, -10, 10) - 0.04 * (hdot + ias * Math.Sin(FlareGlideRad));
                if (h < flareFt) thetaCmd = Math.Max(thetaCmd, _theta0 + JetTune.flareDeg * Math.PI / 180 * Math.Clamp((flareFt - h) / (flareFt * 0.66), 0, 1));
                _jetPitchInt = Math.Clamp(_jetPitchInt + JetTune.ki * (thetaCmd - pitch) * dt, -JetIntCap, JetIntCap);   // small and capped (±3°): an uncapped one wound to 17° and ballooned it
                // Trim follows the thrust (the engines hang below the CG: thrust pitches it up) — interpolated between the
                // power-off trim and the powered trim found at the spawn, so the power coming off doesn't drop the nose.
                double trimNow = _thr0 > 0.01 ? _trimIdleStick + (_elevTrim - _trimIdleStick) * Math.Clamp(_thrLast / _thr0, 0, 1.5) : _elevTrim;
                ele = Math.Clamp(trimNow - JetKTheta * ((thetaCmd - pitch) + _jetPitchInt) + JetKq * q, -0.9, 0.6);
            }
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
                if (Descending && Taildragger && mainsAgl < 1.5)
                {
                    // Three-point: bring the attitude to the stance as it settles (never let the nose drop through it) —
                    // blended in from 5 ft (stance − 6° there, the stance itself by 1 ft). Switched on at 2 ft it snapped the
                    // Cub's nose up to 14° at the touch and it bounced (regimen 2026-10-06).
                    double thetaCmd = StanceRad - Math.Clamp((mainsAgl - 0.3) / 1.2, 0, 1) * 6.0 * Math.PI / 180;
                    double attHold = Math.Clamp(-(thetaCmd - pitch) * 2.0 + q * 0.6, -0.8, 0.3);
                    ele = Math.Min(ele, attHold);
                }
                if (ground) ele = Taildragger ? -0.6 : 0.0;   // tail down on the roll-out; neutral for a nosewheel type
                // …but a taildragger still above the stall holds the three-point attitude, stick coming back as it slows:
                // full back stick the instant the mains touched at 49 kt flew the Cub off again — bounced, bounced (regimen
                // 2026-10-06).
                _gndSec = ground ? _gndSec + dt : 0.0;
                if (ground && Taildragger && Descending && ias > 0.95 * VsoMs && _gndSec < 1.0)   // then full back: the brakes come next
                    ele = Math.Clamp(-(StanceRad - pitch) * 2.0 + q * 0.6, -0.6, 0.2);
                // Power: hold the target airspeed (PI); idle on the ground and in the power-off exercises. A glider's lever is
                // its spoiler: half out in the flare, full once it is down.
                _thrInt = Math.Clamp(_thrInt + 0.05 * vErr * dt, -0.4, 0.4);
                double thr01 = ground || FlareExercise ? 0 : Math.Clamp(_thr0 + 0.20 * vErr + _thrInt, 0, 1);
                if (JetFlare && !ground && !TouchedDown)
                {
                    // Above 50 ft: hold the approach speed on the 3° path (PI on the thrust). From 50 ft to 5 ft: ease from
                    // that setting to idle, linearly with the height (owner 2026-10-07). Below 5 ft: idle.
                    double h = mainsAgl, h50 = ThrustOffStartFt * 0.3048, h5 = ThrustOffEndFt * 0.3048;
                    if (h > h50)
                    {
                        double ve = JetApproachFactor * VsoMs - ias;
                        _thrInt = Math.Clamp(_thrInt + 0.01 * ve * dt, -0.3, 0.3);
                        thr01 = Math.Clamp(_thr0 + 0.05 * ve + _thrInt, 0, 1);
                        _thrHeld = thr01;
                    }
                    else
                    {
                        if (_thrHeld < 0) _thrHeld = _thr0;
                        thr01 = _thrHeld * Math.Clamp((h - h5) / (h50 - h5), 0, 1);
                    }
                }
                // A glider's lever is its spoiler: half on the slope, more when fast, full once it is down.
                _thrLast = thr01;
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
        if (live && UserBrakes && glider) lever = user.ThrottleLever;   // a glider's spoiler handle is the user's (its last travel the wheel brake)

        // Score while live.
        if (live && Phase != PracticePhase.Finished)
        {
            double err = UserAileron ? Math.Abs(cross) : Approach ? Math.Abs(GlideslopeDeviationDeg) * 3 : UserElevator ? 0 : Math.Abs(AlignmentDeg);
            double band = WindPattern == PracticeWind.Shifting ? 5.0 : 3.0;   // ±3° / ±3 m; ±5 while the wind swaps sides
            _liveSec += dt; if (err <= band) _inBandSec += dt; _rmsAccum += err * err * dt;
        }

        // End: the departure end (level exercises), or stopped / off the far end (landings and flares).
        bool pastEnd = along > L - 40;
        if (Time > BriefingSec + 3 && s.Velocity.Length < (WaterLane ? 4.0 : 1.0) && mainsAgl < 1.0) _stillSec += dt; else _stillSec = 0;   // on water: off the step, taxiing   // wheel height: the 737 sits with its CG 3 m up
        bool stopped = _stillSec > 2.0;
        bool offSide = Math.Abs(cross) > 60;
        if (Phase != PracticePhase.Finished && (pastEnd || stopped || offSide || ac.Structure.WingsFailed) && UserBrakes && TouchedDown && !_brakingJudged && (_rollV0 > 0 || _maxBrake > 0.05 || ac.LostComponents.Count > 0))
        {
            // BRAKING (owner 2026-10-06: "after landing the user is judged on wheel brake usage — too much, too little"):
            // the average from touchdown to the stop, the peak, a tail that came up, an overrun.
            _brakingJudged = true;
            if (_rollV0 < 0) { _rollV0 = 0; _rollX0 = AlongM; }   // went over (or stopped) before it had even settled
            double dist = Math.Max(1, AlongM - _rollX0);
            double gsEnd = Math.Max(0, _rollPrevGs);
            RolloutAvgDecelG = (_rollV0 * _rollV0 - gsEnd * gsEnd) / (2 * dist * 9.81);
            string detail; double err;
            if (pastEnd) { err = 1; detail = $"off the far end at {gsEnd * 1.944:F0} kt — not enough brake"; }
            else if (_tailLifted || ac.LostComponents.Count > 0) { err = 0.2; detail = $"{(_peakDecelG > 0.05 ? $"peak {_peakDecelG:F2} g — " : "")}too much: {(ac.LostComponents.Count > 0 ? "it went over on the brakes" : "the tail came up")}"; }
            else if (_peakDecelG > 0.55) { err = _peakDecelG - 0.55; detail = $"peak {_peakDecelG:F2} g — too much, ease the brakes on"; }
            else if (RolloutAvgDecelG < 0.12) { err = 0.12 - RolloutAvgDecelG; detail = $"average {RolloutAvgDecelG:F2} g over {dist * 3.28084:F0} ft — {(_maxBrake < 0.05 ? "no brakes used" : "too little")}"; }
            else { err = 0; detail = $"average {RolloutAvgDecelG:F2} g, peak {_peakDecelG:F2} g, stopped in {dist * 3.28084:F0} ft — firm and controlled"; }
            Judge.Moment(LessonJudge.Std.Braking, err, detail);
        }
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
        if (CLD || Glide || ClimbLesson) return StepLesson(ac, user, dt, roll, pitch, yaw, agl, hdot, ias, vAirBody, beta, glider, live);
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
            // The game's roll law (owner 2026-09-16): full aileron until the roll rate reaches 45°/s, toward the target bank,
            // reversing the instant 45° is reached — rapid rolling reversals. Before the hand-over: wings level.
            double rateT = TargetBankSign * 45.0 * Math.PI / 180;
            double ailWant = live ? Math.Clamp(3.0 * (rateT - p), -1, 1) : Math.Clamp(-1.2 * roll - 0.5 * p, -1, 1);
            // The game's aileron moves no faster than a hand would: one second stop to stop (owner 2026-09-16), so the
            // rudder has a chance to go in with it.
            _ailCmd += Math.Clamp(ailWant - _ailCmd, -2.0 * dt, 2.0 * dt);
            ail = _ailCmd;
            rud = Math.Clamp(-1.5 * beta - 0.5 * r + (live ? 0.35 * ail : 0), -1, 1);   // coordinated: rudder with the aileron against adverse yaw
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

    /// <summary>Climb/level/descend, glide (speed to fly) and Vx/Vy climb lessons. Game laws: wings level + coordinated, pitch
    /// for the target speed (or altitude on the level leg), power per leg. Any axis the user has chosen is theirs.</summary>
    private ControlInputs StepLesson(Aircraft ac, ControlInputs user, double dt, double roll, double pitch, double yaw, double agl, double hdot, double ias, Vec3 vAirBody, double beta, bool glider, bool live)
    {
        RigidBodyState s = ac.State;
        double p = s.Rates.X, q = s.Rates.Y, r = s.Rates.Z;
        (double along, _) = Localize(s.Position.X, s.Position.Y);
        const double LegHeightM = 152.4;   // 500 ft
        const double RunHeightM = 304.8;   // 1,000 ft
        double thr01 = 0; bool levelLeg = false;
        if (CLD)
        {
            if (live) LegT += dt;
            if (Leg == 0 && agl >= StartAglM + LegHeightM) { Leg = 1; LegT = 0; }
            else if (Leg == 1 && LegT > 45) { Leg = 2; LegT = 0; }
            else if (Leg == 2 && (agl <= StartAglM + 5 || LegT > 150)) { Leg = 3; }   // 150 s cap: a type that will not descend on 50 % power at cruise speed
            thr01 = glider ? 0 : Leg == 0 ? 1.0 : Leg == 1 ? 0.75 : Leg == 2 ? 0.5 : 0.75;
            TargetSpeedMs = Leg == 0 ? VyMs : CruiseMs;
            levelLeg = Leg == 1 || Leg == 3;
            if (glider) { TargetSpeedMs = BestLdMs; levelLeg = false; }
        }
        else if (Glide) { thr01 = 0; TargetSpeedMs = SpeedToFlyMs; }
        else { thr01 = 1.0; TargetSpeedMs = Kind == PracticeKind.ClimbVyRear ? VyMs : VxMs; }
        SpeedTargetMs = TargetSpeedMs;

        // Game laws: hold the start heading with bank, coordinated with rudder (which also cancels the prop yaw).
        if (double.IsNaN(_heading0)) _heading0 = yaw;
        double hdgErr = Wrap(yaw - _heading0);
        double phiCmd = Math.Clamp(-1.5 * hdgErr, -0.35, 0.35);
        double ail = Math.Clamp(1.2 * (phiCmd - roll) - 0.5 * p, -1, 1);
        double rud = Math.Clamp(-1.5 * beta - 0.6 * r, -1, 1);
        double ele;
        if (levelLeg)
        {
            double hErr = agl - (StartAglM + LegHeightM);
            double kva = Math.Clamp(Math.Pow(22.0 / Math.Max(ias, 8.0), 1.5), 0.25, 1.0);
            _elevInt = Math.Clamp(_elevInt + 0.03 * hErr * dt, -0.3, 0.3);
            ele = Math.Clamp(_elevTrim + ElevatorPower * (kva * (0.08 * hErr + 0.25 * hdot) + _elevInt) + 0.5 * q, -0.6, 0.45);
        }
        else
        {
            // Pitch for speed: attitude command from the speed error (P + slow I: the trim attitude differs with power), pitch-rate damping.
            double vErr = TargetSpeedMs - ias;
            _thrInt = Math.Clamp(_thrInt + 0.006 * vErr * dt, -0.3, 0.3);   // (re-used as the attitude integral in the lessons)
            double thetaCmd = Math.Clamp(_theta0 - 0.015 * vErr - _thrInt + (thr01 > 0.9 ? 0.05 : thr01 < 0.1 && !glider ? -0.04 : 0), -0.35, 0.45);
            ele = Math.Clamp(_elevTrim + ElevatorPower * 4.0 * (pitch - thetaCmd) + 0.8 * q, -0.9, 0.6);
        }
        double lever = glider ? (Glide ? 0.0 : 0.0) : 1 - 2 * thr01;
        Autopilot = new ControlInputs(ail, ele, rud, lever);
        GameAileron = !(live && UserAileron); GameRudder = !(live && UserRudder); GameElevator = !(live && UserElevator); GameThrottle = !(live && UserThrottle);
        double outAil = GameAileron ? ail : user.Aileron, outRud = GameRudder ? rud : user.Rudder, outEle = GameElevator ? ele : user.Elevator;
        if (!GameThrottle) lever = user.ThrottleLever;

        // Measurements and scoring.
        if (Glide)
        {
            if (live)
            {
                if (_glideStartAlong == 0 && LegT == 0) { _glideStartAlong = along; }
                LegT += dt;
                double lost = StartAglM - agl;
                AchievedRatio = lost > 5 ? (along - _glideStartAlong) / lost : 0;
                _liveSec += dt; double err = Math.Abs(ias - SpeedToFlyMs); if (err < 0.06 * SpeedToFlyMs) _inBandSec += dt; _rmsAccum += err * err * dt;
                if (lost >= RunHeightM && Phase != PracticePhase.Finished)
                {
                    EndReason = "1,000 ft lost";
                    Score = Math.Clamp(100 * AchievedRatio / Math.Max(1, SpeedToFlyGroundRatio), 0, 100);
                    Verdict = Score >= 92 ? "Right on the speed to fly." : Score >= 75 ? "Close — check the readout against your speed." : "Well off the speed to fly.";
                    Phase = PracticePhase.Finished;
                }
            }
        }
        else if (ClimbLesson)
        {
            if (live)
            {
                if (LegT == 0) _climbStartAlong = along;
                LegT += dt; ClimbTimeSec = LegT; ClimbDistanceM = along - _climbStartAlong;
                _liveSec += dt; double err = Math.Abs(ias - TargetSpeedMs); if (err < 0.06 * TargetSpeedMs) _inBandSec += dt; _rmsAccum += err * err * dt;
                if (agl - StartAglM >= RunHeightM && Phase != PracticePhase.Finished)
                {
                    EndReason = "1,000 ft gained";
                    double best = Kind == PracticeKind.ClimbVyRear ? RunHeightM / Math.Max(0.1, RocAtVyMs) : RunHeightM * (VxMs + 0) / Math.Max(0.1, RocAtVxMs);
                    double got = Kind == PracticeKind.ClimbVyRear ? ClimbTimeSec : Math.Max(1, ClimbDistanceM);
                    Score = Math.Clamp(100 * best / got, 0, 100);
                    Verdict = Score >= 92 ? (Kind == PracticeKind.ClimbVyRear ? "That is Vy." : "That is Vx.") : Score >= 75 ? "Close." : "Well off the speed.";
                    Phase = PracticePhase.Finished;
                }
            }
        }
        else if (CLD)
        {
            if (live)
            {
                _liveSec += dt;
                double err = levelLeg ? Math.Abs(agl - (StartAglM + LegHeightM)) : Math.Abs(ias - TargetSpeedMs);
                double band = levelLeg ? 15.0 : 0.06 * TargetSpeedMs;
                if (err < band) _inBandSec += dt; _rmsAccum += err * err * dt;
                if (Leg == 3 && Phase != PracticePhase.Finished) { EndReason = "back at the start altitude"; Score = InBandFraction * 100; Verdict = Score >= 85 ? "On speed, on altitude." : Score >= 60 ? "Getting there." : "Pitch for the speed, power for the leg."; Phase = PracticePhase.Finished; }
            }
        }
        bool crashed = agl < 2 || ac.Structure.WingsFailed;
        if (Phase != PracticePhase.Finished && crashed) { EndReason = "hit the ground"; Phase = PracticePhase.Finished; Score = 0; Verdict = "That ended on the ground."; }
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
            td -= Math.Max(0, Math.Max(TouchdownSinkMs, _hardestSinkMs) - 0.6) * 25;   // firm arrivals cost (the HARDEST touch)
            if (_touches > 1) td -= Math.Min(40, _maxBounceM * 3.28084 * 6);       // bounces cost
            td -= 15 * Math.Max(0, _bounceHeightsM.Count(h => h > 0.15) - 1);       // … and every bounce after the first
            td -= 10 * NoseFirstTouches;                                            // nose wheel first: a porpoise in the making
            if (_damageNoted) td = 0;
            td -= Math.Max(0, Math.Abs(TouchdownAlignDeg) - 2) * 6;           // crabbed touchdowns cost
            td -= Math.Max(0, Math.Abs(TouchdownOffCentreM) - 3) * 3;
            td -= Math.Max(0, MaxRolloutSwingDeg - 10) * 1.5;                 // the rollout: swings cost, a ground loop fails
            if (MaxRolloutSwingDeg > 45) td = Math.Min(td, 10);
        }
        else td = 0;
        double s = Math.Clamp(FlareExercise ? td : 0.5 * band + 0.5 * td, 0, 100);   // approach: half path-keeping, half touchdown
        double hard = Math.Max(TouchdownSinkMs, _hardestSinkMs);
        int bounces = _bounceHeightsM.Count(h => h > 0.15);
        string bounced = bounces <= 0 ? "" : string.Join(" ", System.Linq.Enumerable.Repeat("Bounced.", Math.Min(bounces, 8))) + (bounces > 8 ? $" ({bounces} bounces)" : "") + (NoseFirstTouches >= 2 ? " Porpoised, nose wheel first." : "");
        Verdict = !TouchedDown ? "No touchdown." : _damageNoted ? "It broke on landing." : MaxRolloutSwingDeg > 45 ? "Ground loop." : bounces > 0 && (_maxBounceM > 0.6 || bounces > 1) ? bounced : hard < 0.8 ? "Greaser." : hard < 1.6 ? "Firm but fine." : "That one hurt.";
        return s;
    }
}

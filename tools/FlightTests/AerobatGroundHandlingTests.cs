using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ground handling of the two aerobatic taildraggers whose gear was re-measured from their own models (2026-10-28):
/// the Pitts S-2B (both liveries share one airframe) and the Extra 300. A scripted pilot taxis, turns, brakes, takes
/// off, and lands both three-point and on the wheels, and the numbers are held to what those aeroplanes do:
///  - taxi: the tailwheel steers, the brakes stop it, and neither a full-rudder turn nor normal braking puts it on its nose
///    (stamping on full brakes at 12 kt still can — that is how taildraggers get nosed over);
///  - takeoff: the tail comes up, the roll swings LEFT without right rudder (US engine, clockwise from the cockpit), and
///    it flies off at the book speed in a book-length roll;
///  - landing: three-point and wheel landings both stay on the runway and slow down sensibly;
///  - ground loop: hands off, a swerve grows (the Pitts faster), and a pilot on the rudder and brakes stops it.
/// The gear geometry itself is checked against the GLB measurements in <see cref="GearMatchesTheModels"/>.
/// </summary>
[Collection("WorldTerrainActive")]
public class AerobatGroundHandlingTests
{
    private readonly ITestOutputHelper _out;
    public AerobatGroundHandlingTests(ITestOutputHelper o) { _out = o; }

    private const string Pitts = "pitts-s2b-like.json", Extra = "extra-300-like.json";
    private static AircraftConfig Load(string f)
    {
        // GROUND_CFG_DIR: run the same pilot against another copy of the configs (before/after comparisons).
        string dir = System.Environment.GetEnvironmentVariable("GROUND_CFG_DIR") ?? Path.Combine(AppContext.BaseDirectory, "TestData");
        return AircraftConfigLoader.LoadFromFile(Path.Combine(dir, f));
    }

    private static bool Trace => System.Environment.GetEnvironmentVariable("GROUND_TRACE") != null;

    private static void TraceLine(ITestOutputHelper? log, double t, Aircraft ac, string extra = "")
    {
        if (!Trace || log == null || Math.Abs(t / 0.5 - Math.Round(t / 0.5)) > 1e-6) return;
        log.WriteLine($"   t {t,4:F1} V {Speed(ac),5:F1} pitch {Pitch(ac) * 57.3,5:F1}° roll {Roll(ac) * 57.3,5:F1}° hdg {Heading(ac) * 57.3,6:F1} r {ac.State.Rates.Z * 57.3,6:F1}°/s y {ac.State.Position.Y,5:F1} mains {(MainsDown(ac) ? 1 : 0)} tail {(TailDown(ac) ? 1 : 0)} tw {(ac.Tailwheel?.AngleRad ?? 0) * 57.3,5:F0}° free {(ac.Tailwheel?.FreeSwivel == true ? 1 : 0)} h {-ac.State.Position.Z,5:F2} {extra}");
    }

    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
        WorldDecks.All.Clear(); WorldSolids.Shapes.Clear(); WorldSolids.Boxes.Clear();
    }

    private static double Pitch(Aircraft ac) { var q = ac.State.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)); }
    private static double Heading(Aircraft ac) { var q = ac.State.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)); }
    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }
    private static double Roll(Aircraft ac) { var q = ac.State.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)); }
    private static double Speed(Aircraft ac) { var v = ac.State.Velocity; return Math.Sqrt(v.X * v.X + v.Y * v.Y); }
    private static bool MainsDown(Aircraft ac) => LandingGear.AnyMainWheelOnGround(ac.Config, ac.State);
    private static bool TailDown(Aircraft ac) => (ac.Tailwheel?.LastLoadN ?? 0) > 1.0;
    private static bool Struck(Aircraft ac) => ac.LostComponents.Count > 0;
    private static double ThreePointDeg(AircraftConfig c) { var r = LandingGear.RestingState(c, 0, 0, 0).Attitude; return Math.Asin(2 * (r.W * r.Y - r.Z * r.X)) * 180 / Math.PI; }
    /// <summary>Throttle lever for a throttle fraction (lever −1 = full, +1 = idle).</summary>
    private static double Lever(double throttle01) => 1.0 - 2.0 * Math.Clamp(throttle01, 0, 1);

    public const double QuickFeet = 6.0, RelaxedFeet = 2.5;

    private sealed class Pilot
    {
        public double Int;
        public double RateError;
        /// <summary>Rudder per rad/s of yaw-rate error. 2.5 = relaxed feet (enough for the Extra); 6 = the quick feet a Pitts
        /// asks for (full rudder at a 10°/s error).</summary>
        public double Kr = QuickFeet;
        public Pilot(double kr = QuickFeet) { Kr = kr; }
        /// <summary>Feet: hold heading 0 on the centreline (y = 0). Cascaded, as a pilot flies it: the heading error asks
        /// for a yaw rate back toward the runway (at most 15°/s), and the rudder works on the yaw-rate error, with an
        /// integral for the steady rudder the swing needs (right rudder at full power).</summary>
        public double Rudder(Aircraft ac, double dt, double gain = 1.0, double hdgRef = 0.0, double maxRate = 0.26, bool centreline = true)
        {
            double hdg = Heading(ac), y = ac.State.Position.Y, r = ac.State.Rates.Z;
            double hdgErr = Wrap(hdg - hdgRef - (centreline ? Math.Clamp(-0.015 * y, -0.04, 0.04) : 0.0));
            double rCmd = Math.Clamp(-1.2 * hdgErr, -maxRate, maxRate);
            RateError = r - rCmd;
            Int = Math.Clamp(Int - 0.8 * RateError * dt, -0.6, 0.6);
            return Math.Clamp(Int - gain * Kr * RateError, -1, 1);
        }
        /// <summary>Differential brake on the side the nose must go, in proportion to the yaw-rate error (0 when on track).</summary>
        public void Brakes(Aircraft ac, double max)
        {
            double b = Math.Clamp(1.5 * Math.Abs(RateError) - 0.05, 0, max);
            ac.BrakeInput = b; ac.BrakeBias = b > 0 ? -Math.Sign(RateError) : 0;
        }
        public double PInt;
        public double Stick(Aircraft ac, double targetRad, double dt, double lo = -1, double hi = 1, double kp = 8.0)
        {
            double e = Pitch(ac) - targetRad;
            PInt = Math.Clamp(PInt + 2.0 * e * dt, -0.6, 0.6);
            return Math.Clamp(PInt + kp * e + 1.2 * ac.State.Rates.Y, lo, hi);
        }
    }

    // ------------------------------------------------------------------------------------------------ taxi

    public record TaxiResult(double PitchDeg, double TailShare, double TurnRS, double TurnLS, double MaxTurnRate, double FullRudderRate,
                             double FullRudderGrowth, bool Unlocked, bool TailLifted, double MinPitchDeg,
                             double StopHalfM, double MinPitchHalfDeg, double MinPitchFullSlowDeg, double MinPitchFullFastDeg, bool Struck, bool StruckFullFast);

    /// <summary>
    /// Taxi as it is taught: settle on the brakes; taxi out at 3.5 m/s (7 kt) on the centreline; slow to 2.5 m/s (5 kt) and
    /// turn 90° right then back 90° left with rudder and a touch of brake (yaw rate asked for: at most 20°/s); a full-rudder turn at
    /// 2.5 m/s held for 8 s (it must settle into a steady turn, not wind itself up); then three brake stops, stick fully back:
    /// half brakes from 4 m/s, full brakes at a walking 2 m/s, and full brakes from a fast 6 m/s taxi (reported, not required to stay
    /// on its wheels: stamping on the brakes at 12 kt is how taildraggers get put on their noses).
    /// </summary>
    public static TaxiResult Taxi(AircraftConfig c, ITestOutputHelper? log = null)
    {
        Calm();
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, rest, ControlDeflections.Neutral);
        var sim = new SimLoop(ac); const double dt = 0.01;
        ac.BrakeInput = 1.0;
        for (double t = 0; t < 3; t += dt) sim.RunFor(dt, new ControlInputs(0, -1, 0, Lever(0)));   // settle on the brakes, stick back
        double pitch0 = Pitch(ac) * 180 / Math.PI, share = (ac.Tailwheel?.LastLoadN ?? 0) / (c.Mass.MassKg * 9.81);
        ac.BrakeInput = 0;
        var pilot = new Pilot(); bool unlocked = false, tailUp = false; double minPitch = 90, maxRate = 0, tt = 0;
        double Thr(double vT) => Math.Clamp(0.10 + 0.25 * (vT - Speed(ac)), 0, 0.45);
        void Step(double rud, double thr, string phase)
        {
            sim.RunFor(dt, new ControlInputs(0, -1, rud, Lever(thr)));
            unlocked |= ac.Tailwheel?.FreeSwivel == true; tailUp |= !TailDown(ac);
            minPitch = Math.Min(minPitch, Pitch(ac) * 180 / Math.PI); maxRate = Math.Max(maxRate, Math.Abs(ac.State.Rates.Z) * 180 / Math.PI);
            TraceLine(log, tt, ac, $"{phase} rud {rud:F2} thr {thr:F2} brake {ac.BrakeInput:F2}/{ac.BrakeBias:F0}"); tt += dt;
        }
        for (double t = 0; t < 8; t += dt) { ac.BrakeInput = 0; Step(pilot.Rudder(ac, dt), Thr(3.5), "out"); }
        double TurnTo(double hdgRef, string phase)
        {
            pilot.Int = 0;
            for (double t = 0; t < 25; t += dt)
            {
                double rud = pilot.Rudder(ac, dt, 1.0, hdgRef, 20 * Math.PI / 180, false);
                pilot.Brakes(ac, 0.4);
                Step(rud, Thr(2.5), phase);
                if (Math.Abs(Wrap(Heading(ac) - hdgRef)) < 3 * Math.PI / 180 && Math.Abs(ac.State.Rates.Z) < 3 * Math.PI / 180) return t;
            }
            return double.NaN;
        }
        double turnR = TurnTo(Math.PI / 2, "turnR"), turnL = TurnTo(0, "turnL");
        ac.BrakeInput = 0; ac.BrakeBias = 0;
        double r34 = 0, r78 = 0;
        for (double t = 0; t < 8; t += dt)
        {
            Step(1.0, Thr(2.5), "full");
            if (t >= 3 && t < 4) r34 += ac.State.Rates.Z * dt; if (t >= 7) r78 += ac.State.Rates.Z * dt;
        }
        bool struck = Struck(ac);
        // Brake stops, stick fully back.
        (double dist, double minP, bool strike) Stop(double v0, double brake)
        {
            var a = new Aircraft(c, rest, ControlDeflections.Neutral); var sm = new SimLoop(a); var p = new Pilot();
            for (double t = 0; t < 30 && Speed(a) < v0; t += dt) sm.RunFor(dt, new ControlInputs(0, -1, p.Rudder(a, dt), Lever(0.4)));
            double x0 = a.State.Position.X, mp = 90; a.BrakeInput = brake;
            for (double t = 0; t < 10 && Speed(a) > 0.05; t += dt) { sm.RunFor(dt, new ControlInputs(0, -1, p.Rudder(a, dt), Lever(0))); mp = Math.Min(mp, Pitch(a) * 180 / Math.PI); }
            return (a.State.Position.X - x0, mp, Struck(a));
        }
        var half = Stop(4.0, 0.5); var fullSlow = Stop(2.0, 1.0); var fullFast = Stop(6.0, 1.0);
        struck |= half.strike || fullSlow.strike;
        var r = new TaxiResult(pitch0, share, turnR, turnL, maxRate, r78 * 180 / Math.PI, Math.Abs(r34) > 1e-6 ? r78 / r34 : double.NaN, unlocked, tailUp, minPitch,
                               half.dist, half.minP, fullSlow.minP, fullFast.minP, struck, fullFast.strike);
        log?.WriteLine($"{c.Id}: three-point {r.PitchDeg:F1}°, tail carries {r.TailShare * 100:F1} %; 90° turns R {r.TurnRS:F1} s, L {r.TurnLS:F1} s (max yaw rate {r.MaxTurnRate:F0}°/s); " +
                       $"full rudder at 5 kt {r.FullRudderRate:F1}°/s (x{r.FullRudderGrowth:F2} from 3 s to 8 s); steering unlocked {r.Unlocked}, tail lifted {r.TailLifted}, min pitch {r.MinPitchDeg:F1}°; " +
                       $"half brakes from 8 kt stop in {r.StopHalfM:F1} m (min pitch {r.MinPitchHalfDeg:F1}°), full brakes at 4 kt min pitch {r.MinPitchFullSlowDeg:F1}°, full brakes at 12 kt min pitch {r.MinPitchFullFastDeg:F1}° (strike {r.StruckFullFast}); prop strike {r.Struck}");
        return r;
    }

    // ------------------------------------------------------------------------------------------------ takeoff

    public record TakeoffResult(double TailUpV, double TailUpM, double LiftoffV, double RollM, double RollS, double MeanRudderTailUp,
                                double MaxOffCentreM, double MaxHdgDeg, double HandsOffSwingDeg, bool Struck);

    /// <summary>Full power over 2 s; feet hold the centreline; stick neutral until <paramref name="tailUpV"/>, then forward to a
    /// level fuselage; at <paramref name="vr"/> the attitude is eased up 4°/s to <paramref name="climbPitchDeg"/> and it flies off.</summary>
    public static TakeoffResult Takeoff(AircraftConfig c, double tailUpV, double vr, double climbPitchDeg, ITestOutputHelper? log = null)
    {
        Calm();
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, rest, ControlDeflections.Neutral);
        var sim = new SimLoop(ac); var pilot = new Pilot(); const double dt = 0.01;
        double tTail = -1, vTail = 0, xTail = 0, lift = -1, vLift = 0, xLift = 0, rudSum = 0, rudN = 0, maxY = 0, maxH = 0, airborne = 0, target = 0; bool rotating = false;
        double t = 0;
        for (; t < 40; t += dt)
        {
            double lever = Math.Max(-1.0, 1.0 - t);   // idle → full over 2 s
            double v = Speed(ac);
            double ele = 0;
            if (v >= tailUpV || tTail >= 0)
            {
                if (v >= vr) rotating = true;
                target = rotating ? Math.Min(climbPitchDeg, target + 6.0 * dt) : 0.0;
                ele = pilot.Stick(ac, target * Math.PI / 180, dt, rotating ? -1.0 : -0.8, 0.8, rotating ? 20.0 : 8.0);
            }
            double rud = pilot.Rudder(ac, dt);
            sim.RunFor(dt, new ControlInputs(0, ele, rud, lever));
            if (tTail < 0 && !TailDown(ac) && v > 5) { tTail = t; vTail = Speed(ac); xTail = ac.State.Position.X; }
            if (tTail >= 0 && !rotating) { rudSum += rud; rudN++; }
            maxY = Math.Max(maxY, Math.Abs(ac.State.Position.Y)); maxH = Math.Max(maxH, Math.Abs(Heading(ac)));
            if (!MainsDown(ac) && !TailDown(ac)) { if (airborne == 0) { vLift = Speed(ac); xLift = ac.State.Position.X; } airborne += dt; } else airborne = 0;
            if (airborne > 0.5) { lift = t - 0.5; break; }
            if (Trace && Math.Abs(t / 0.5 - Math.Round(t / 0.5)) < 1e-6)
                log?.WriteLine($"   t {t,4:F1} V {Speed(ac),5:F1} pitch {Pitch(ac) * 57.3,5:F1}° tgt {target,4:F1} ele {ele,5:F2} rud {rud,5:F2} hdg {Heading(ac) * 57.3,5:F1} y {ac.State.Position.Y,5:F1} mains {MainsDown(ac)} tail {TailDown(ac)} h {-ac.State.Position.Z,5:F2} rpm {ac.EngineRpm:F0}");
        }
        bool struck = Struck(ac);
        // Hands off the rudder: full power, stick neutral, 5 s — which way does it swing?
        ac = new Aircraft(c, rest, ControlDeflections.Neutral); sim = new SimLoop(ac);
        for (double s = 0; s < 5; s += dt) sim.RunFor(dt, new ControlInputs(0, 0, 0, Math.Max(-1.0, 1.0 - s)));
        double swing = Heading(ac) * 180 / Math.PI;
        var r = new TakeoffResult(vTail, xTail, vLift, xLift, lift, rudN > 0 ? rudSum / rudN : 0, maxY, maxH * 180 / Math.PI, swing, struck);
        log?.WriteLine($"{c.Id}: tail up at {r.TailUpV:F1} m/s after {r.TailUpM:F0} m; lift-off {r.LiftoffV:F1} m/s ({r.LiftoffV * 2.237:F0} mph, {r.LiftoffV * 1.944:F0} kt) after {r.RollM:F0} m / {r.RollS:F1} s; " +
                       $"mean rudder tail-up {r.MeanRudderTailUp * 100:+0;-0} %; max off-centre {r.MaxOffCentreM:F1} m, heading {r.MaxHdgDeg:F1}°; hands-off swing {r.HandsOffSwingDeg:F0}° in 5 s; prop strike {r.Struck}");
        return r;
    }

    // ------------------------------------------------------------------------------------------------ landing

    public record LandingResult(double TouchdownV, double RollM, double RollBrakesM, double MaxBounceM, double MaxOffCentreM, double MaxHdgDeg,
                                double MinPitchDeg, bool TailDownAtEnd, bool Struck);

    /// <summary>Idle, from <paramref name="h"/> m with <paramref name="sink"/> m/s at <paramref name="v"/> m/s and a held attitude:
    /// three-point = the stance attitude, stick coming fully back after touchdown; wheel landing = a level fuselage held with
    /// forward stick until the tail comes down by itself, then stick back. <paramref name="brakeBelowV"/>: half brakes below that
    /// speed once the tail is down (0 = none).</summary>
    public static LandingResult Land(AircraftConfig c, bool wheelLanding, double v, double h, double sink, double brakeBelowV, double brake = 0.5, ITestOutputHelper? log = null, double feet = QuickFeet)
    {
        Calm();
        double three = ThreePointDeg(c) * Math.PI / 180;
        double approachPitch = wheelLanding ? 1.0 * Math.PI / 180 : three;
        double lowestMain = 0; foreach (GearConfig g in c.Gear) if (!g.IsTailwheel) lowestMain = Math.Max(lowestMain, g.Pos[2]);
        Vec3 cg = c.Mass.CgVec();
        var att = new Quat(0, Math.Sin(approachPitch / 2), 0, Math.Cos(approachPitch / 2));
        // CG height so the mains are h above the runway in this attitude.
        double mainsBelowCg = 0; foreach (GearConfig g in c.Gear) if (!g.IsTailwheel) mainsBelowCg = Math.Max(mainsBelowCg, att.Rotate(g.PosVec() - cg).Z);
        var s0 = new RigidBodyState(new Vec3(0, 0, -(h + mainsBelowCg)), att, att.Conjugate().Rotate(new Vec3(v, 0, sink)), Vec3.Zero);
        var ac = new Aircraft(c, s0, ControlDeflections.Neutral);
        var sim = new SimLoop(ac); var pilot = new Pilot(feet); const double dt = 0.01;
        double tdV = -1, xTd = 0, bounce = 0, maxY = 0, maxH = 0, minPitch = 90; bool touched = false, tailDownSeen = false;
        for (double t = 0; t < 60 && (Speed(ac) > 0.3 || !touched); t += dt)
        {
            double vg = Speed(ac), ele;
            if (!wheelLanding) ele = touched ? -1.0 : pilot.Stick(ac, three, dt);
            else if (!touched) ele = pilot.Stick(ac, approachPitch, dt);
            else if (!tailDownSeen && vg > 0.55 * v) ele = pilot.Stick(ac, 0.0, dt, -0.5, 0.6);   // pin it on the mains, tail up
            else ele = -1.0;                                                                     // tail coming down: stick back
            if (TailDown(ac) && touched) tailDownSeen = true;
            ac.BrakeInput = brakeBelowV > 0 && tailDownSeen && vg < brakeBelowV ? brake : 0.0;
            double rudL = touched ? pilot.Rudder(ac, dt) : 0.0;
            sim.RunFor(dt, new ControlInputs(0, ele, rudL, Lever(0)));
            TraceLine(log, t, ac, $"ele {ele:F2} rud {rudL:F2} brake {ac.BrakeInput:F2}");
            bool down = MainsDown(ac) || TailDown(ac);
            if (down && !touched) { touched = true; tdV = Speed(ac); xTd = ac.State.Position.X; }
            if (touched)
            {
                double gearH = -ac.State.Position.Z - mainsBelowCg;
                bounce = Math.Max(bounce, gearH);
                maxY = Math.Max(maxY, Math.Abs(ac.State.Position.Y)); maxH = Math.Max(maxH, Math.Abs(Heading(ac)));
                minPitch = Math.Min(minPitch, Pitch(ac) * 180 / Math.PI);
            }
        }
        double roll = ac.State.Position.X - xTd;
        return new LandingResult(tdV, brakeBelowV > 0 ? double.NaN : roll, brakeBelowV > 0 ? roll : double.NaN, bounce, maxY, maxH * 180 / Math.PI, minPitch, TailDown(ac), Struck(ac));
    }

    // ------------------------------------------------------------------------------------------------ ground loop

    /// <summary>Rolling at v with the nose yawed `yawDeg` off the track: after `delayS` a pilot on rudder and differential
    /// brake brings it back. Returns the worst heading excursion and whether it ended straight.</summary>
    public static (double maxDeg, double endDeg, double maxOffM, bool struck) Recover(AircraftConfig c, double v, double yawDeg, double delayS, ITestOutputHelper? log = null)
    {
        Calm();
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        double psi = yawDeg * Math.PI / 180;
        Quat att = Quat.Multiply(new Quat(0, 0, Math.Sin(psi / 2), Math.Cos(psi / 2)), rest.Attitude);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, att, att.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac); var pilot = new Pilot(); const double dt = 0.01;
        double maxDeg = 0, maxY = 0;
        for (double t = 0; t < 8; t += dt)
        {
            double hdg = Heading(ac);
            double rud = t < delayS ? 0.0 : pilot.Rudder(ac, dt);
            if (t >= delayS) pilot.Brakes(ac, 0.6);
            sim.RunFor(dt, new ControlInputs(0, -1, rud, Lever(0)));
            TraceLine(log, t, ac, $"rud {rud:F2} brake {ac.BrakeInput:F1} bias {ac.BrakeBias:F0} twLoad {ac.Tailwheel?.LastLoadN ?? 0:F0} twSide {ac.Tailwheel?.LastLateralN ?? 0:F0}");
            maxDeg = Math.Max(maxDeg, Math.Abs(Heading(ac)) * 180 / Math.PI); maxY = Math.Max(maxY, Math.Abs(ac.State.Position.Y));
        }
        return (maxDeg, Heading(ac) * 180 / Math.PI, maxY, Struck(ac));
    }

    /// <summary>Per type: tail-up speed, rotation speed (Vr), three-point and wheel-landing approach speeds (m/s at the fence
    /// height used below). Pitts S-2B: stall ~58 mph clean, rotate ~56 mph, wheel landings ~75 mph. Extra 300: stall 55–60 kt,
    /// rotate ~54 kt, wheel landings ~68 kt.</summary>
    public static readonly (string file, double tailUp, double vr, double v3pt, double vWheel)[] Types =
        { (Pitts, 13.0, 25.0, 29.0, 33.0), (Extra, 14.0, 28.0, 31.0, 35.0) };
    private static (string file, double tailUp, double vr, double v3pt, double vWheel) TypeOf(string f) => Types.First(t => t.file == f);

    // ------------------------------------------------------------------------------------------------ assertions

    /// <summary>
    /// The config's gear is where the drawn wheels are (2026-10-28). The GLB is placed the way AirframeModels.Place places it
    /// (scaled to the config span with the OwnSpecs trim, its outer-wing chord centred on the config's), and lined up vertically
    /// on the WINGS (strip-area-weighted chord-line height); the main and tail tyre contacts must then be the config's
    /// gear points within 2.5 cm, and the three-point attitude the model's within 0.3°. Before: Pitts mains 0.19 m short and
    /// 8 cm aft, tailwheel 0.36 m forward; Extra mains 0.44 m short and 15 cm aft, tailwheel 1.60 m forward.
    /// </summary>
    [Theory]
    [InlineData(Pitts, "pitts-s2b-like/pitts-s2b-like-own.glb", 6.094 / 5.996)]
    [InlineData(Pitts, "pitts-s2b-like/pitts-s2b-like-own-n1191.glb", 6.094 / 5.996)]
    [InlineData(Pitts, "pitts-s2b-like/pitts-s2b-like-own-checker-unmarked.glb", 6.094 / 5.996)]
    [InlineData(Pitts, "pitts-s2b-like/pitts-s2b-like-own-factory-unmarked.glb", 6.094 / 5.996)]
    [InlineData(Extra, "extra-300-like/extra-300-like-own.glb", 1.0)]
    [InlineData(Extra, "extra-300-like/extra-300-like-own-n300uy.glb", 1.0)]
    [InlineData(Extra, "extra-300-like/extra-300-like-own-unmarked.glb", 1.0)]
    public void GearMatchesTheModels(string file, string glb, double trim)
    {
        var c = Load(file);
        var parts = OwnPittsModelTests.Vertices(Path.Combine(OwnPittsModelTests.Models, glb));
        // glTF: nose −x, up +y, span z → (fwd, up, span)
        var all = parts.Values.SelectMany(v => v).ToList();
        double spanExt = all.Max(p => p.z) - all.Min(p => p.z), cz = 0.5 * (all.Max(p => p.z) + all.Min(p => p.z));
        var wingStrips = c.Surfaces.Where(sf => sf.Id.ToLowerInvariant().Contains("wing")).SelectMany(sf => sf.Strips.Select(st => (sf, st))).ToList();
        double half = wingStrips.Max(w => Math.Abs(w.st.Pos[1]));
        double k = 2 * half / spanExt * trim;
        (double f, double u, double s) S((float x, float y, float z) p) => (-p.x * k, p.y * k, (p.z - cz) * k);
        var outer = all.Select(S).Where(q => Math.Abs(q.s) >= 0.65 * half && Math.Abs(q.s) <= 0.92 * half).ToList();
        double modelMid = 0.5 * (outer.Max(q => q.f) + outer.Min(q => q.f));
        var cfgOuter = wingStrips.Where(w => Math.Abs(w.st.Pos[1]) >= 0.65 * half && Math.Abs(w.st.Pos[1]) <= 0.92 * half).ToList();
        double cfgMid = 0.5 * (cfgOuter.Max(w => w.st.Pos[0] + 0.25 * w.st.Chord) + cfgOuter.Min(w => w.st.Pos[0] - 0.75 * w.st.Chord));
        double sx = cfgMid - modelMid;
        // Wing heights: each main-wing strip against the drawn wing's mid-thickness at that span and chord.
        var wing = parts.Where(kv => kv.Key.StartsWith("Wing") || kv.Key.StartsWith("Aileron")).SelectMany(kv => kv.Value).Select(S).ToList();
        double num = 0, den = 0;
        foreach (var (sf, st) in wingStrips.Where(w => !w.sf.Id.Contains("aileron")))
        {
            double xs = st.Pos[0] - sx;
            var near = wing.Where(q => Math.Abs(Math.Abs(q.s) - Math.Abs(st.Pos[1])) < 0.12 && Math.Abs(q.f - xs) < 0.25 * st.Chord + 0.2).ToList();
            if (sf.Id.StartsWith("wing-upper")) near = near.Where(q => q.u > 0.2).ToList();
            if (sf.Id.StartsWith("wing-lower")) near = near.Where(q => q.u < 0.0).ToList();
            if (near.Count == 0) continue;
            num += st.Area * (0.5 * (near.Max(q => q.u) + near.Min(q => q.u)) + st.Pos[2]); den += st.Area;
        }
        double dz = num / den;   // model up − config up at the wings
        var wheels = parts["Wheels"].Select(S).ToList();
        (double x, double z) Contact(List<(double f, double u, double s)> w)
        {
            double b = w.Min(q => q.u); var bot = w.Where(q => q.u < b + 0.004).ToList();
            return (bot.Average(q => q.f) + sx, -(b - dz));
        }
        var main = Contact(wheels.Where(q => q.f > -1.5).ToList()); var tail = Contact(wheels.Where(q => q.f < -2.0).ToList());
        GearConfig gm = c.Gear.First(g => !g.IsTailwheel), gt = c.Gear.First(g => g.IsTailwheel);
        double modelThree = Math.Atan((main.z - tail.z) / (main.x - tail.x)) * 180 / Math.PI;
        double cfgThree = Math.Atan((gm.Pos[2] - gt.Pos[2]) / (gm.Pos[0] - gt.Pos[0])) * 180 / Math.PI;
        _out.WriteLine($"{glb}: scale {k:F4}, x shift {sx:+0.000;-0.000}, wings {dz:+0.000;-0.000} m | model mains ({main.x:F3}, {main.z:F3}) tail ({tail.x:F3}, {tail.z:F3}) {modelThree:F2}° | " +
                       $"config mains ({gm.Pos[0]:F3}, {gm.Pos[2]:F3}) tail ({gt.Pos[0]:F3}, {gt.Pos[2]:F3}) {cfgThree:F2}°");
        Assert.InRange(gm.Pos[0] - main.x, -0.025, 0.025); Assert.InRange(gm.Pos[2] - main.z, -0.025, 0.025);
        Assert.InRange(gt.Pos[0] - tail.x, -0.025, 0.025); Assert.InRange(gt.Pos[2] - tail.z, -0.025, 0.025);
        Assert.InRange(cfgThree - modelThree, -0.3, 0.3);
        Assert.InRange(ThreePointDeg(c) - modelThree, -0.6, 0.6);   // the physics' own stance, settled 2 cm into the struts
    }

    /// <summary>Taxi: steers on the tailwheel, finishes 90° turns, settles into a steady full-rudder turn, stops on half
    /// brakes with the tail down, and full brakes at a walk lift the tail but do not put the prop in the ground.</summary>
    [Theory]
    [InlineData(Pitts)]
    [InlineData(Extra)]
    public void TaxiTurnsAndBrakesWithoutNosingOver(string file)
    {
        var c = Load(file); var r = Taxi(c, _out);
        Assert.InRange(r.TailShare, 0.05, 0.15);
        Assert.InRange(r.TurnRS, 3, 12); Assert.InRange(r.TurnLS, 3, 12);
        Assert.InRange(r.FullRudderRate, 7, 25);                 // a full-rudder turn at 5 kt: ~10 m radius
        Assert.InRange(r.FullRudderGrowth, 0.8, 1.25);           // steady — it does not wind itself up into a loop
        Assert.False(r.Unlocked, "the steering broke out during normal taxiing");
        Assert.False(r.TailLifted, "the tail came up while taxiing");
        Assert.True(r.MinPitchHalfDeg > r.PitchDeg - 3, $"half brakes from 8 kt raised the tail ({r.MinPitchHalfDeg:F1}°)");
        Assert.True(r.MinPitchFullSlowDeg > -10, $"full brakes at a walk tipped it to {r.MinPitchFullSlowDeg:F1}°");
        Assert.InRange(r.StopHalfM, 1.5, 8);
        Assert.False(r.Struck, "prop strike while taxiing");
    }

    /// <summary>Takeoff: tail up early, swings LEFT without right rudder and needs right rudder held, flies off at about
    /// 1.2 Vs in a book-length roll (Pitts S-2B: 557 ft = 170 m ground roll; Extra 300L: 115–200 m published).</summary>
    [Theory]
    [InlineData(Pitts, 29.0, 36.0, 110.0, 220.0)]
    [InlineData(Extra, 32.0, 40.0, 110.0, 230.0)]
    public void TakeoffRollIsTheBooks(string file, double vLoMin, double vLoMax, double rollMin, double rollMax)
    {
        var c = Load(file); var t = TypeOf(file);
        var r = Takeoff(c, t.tailUp, t.vr, ThreePointDeg(c) - 0.5, _out);
        Assert.InRange(r.TailUpV, 10, 20);
        Assert.InRange(r.LiftoffV, vLoMin, vLoMax);
        Assert.InRange(r.RollM, rollMin, rollMax);
        Assert.True(r.MeanRudderTailUp > 0.03, $"right rudder needed on the roll ({r.MeanRudderTailUp * 100:F0} %)");
        Assert.True(r.HandsOffSwingDeg < -20, $"hands off the rudder it should swing left ({r.HandsOffSwingDeg:F0}°)");
        Assert.True(r.MaxOffCentreM < 3 && r.MaxHdgDeg < 5, "held straight");
        Assert.False(r.Struck);
    }

    /// <summary>Landing: three-point and wheel landings both stay on the runway, settle on all three wheels and slow sensibly
    /// with quick feet; half brakes below 20 m/s bring the roll near the book (Pitts S-2B landing roll 750 ft = 229 m).</summary>
    [Theory]
    [InlineData(Pitts, false)]
    [InlineData(Pitts, true)]
    [InlineData(Extra, false)]
    [InlineData(Extra, true)]
    public void ThreePointAndWheelLandingsAreWorkable(string file, bool wheel)
    {
        var c = Load(file); var t = TypeOf(file);
        var free = Land(c, wheel, wheel ? t.vWheel : t.v3pt, wheel ? 0.3 : 0.5, wheel ? 0.5 : 0.3, 0.0);
        var braked = Land(c, wheel, wheel ? t.vWheel : t.v3pt, wheel ? 0.3 : 0.5, wheel ? 0.5 : 0.3, 20.0);
        foreach (var (l, name) in new[] { (free, "rolling"), (braked, "half brakes") })
        {
            _out.WriteLine($"{c.Id} {(wheel ? "wheel" : "3-pt")} {name}: touchdown {l.TouchdownV:F1} m/s, roll {(name == "rolling" ? l.RollM : l.RollBrakesM):F0} m, bounce {l.MaxBounceM:F2} m, off-centre {l.MaxOffCentreM:F1} m, heading {l.MaxHdgDeg:F1}°, min pitch {l.MinPitchDeg:F1}°");
            Assert.True(l.MaxBounceM < 0.5, "bounced");
            Assert.True(l.MaxOffCentreM < 5 && l.MaxHdgDeg < 10, $"{name}: off the runway or swung ({l.MaxOffCentreM:F1} m, {l.MaxHdgDeg:F1}°)");
            Assert.True(l.MinPitchDeg > -2, "nosed over");
            Assert.True(l.TailDownAtEnd && !l.Struck);
        }
        // A wheel landing touches down ~4 m/s faster and keeps the tail up (no braking) until it comes down by itself near
        // 18 m/s, so its braked roll is the longer one.
        Assert.InRange(braked.RollBrakesM, 150, wheel ? 550 : 400);
        Assert.True(free.RollM > braked.RollBrakesM * 1.3, "the brakes shorten the roll");
    }

    /// <summary>The ground-loop tendency: the Pitts is notoriously squirrelly, the Extra less so. Relaxed feet that keep
    /// the Extra straight on a three-point rollout let the Pitts get away; quick feet hold it (above). A swerve at 12 and
    /// 18 m/s is caught by rudder and differential brake on both.</summary>
    [Fact]
    public void PittsNeedsQuickFeetTheExtraForgivesRelaxedOnes()
    {
        var p = Load(Pitts); var e = Load(Extra);
        var pl = Land(p, false, TypeOf(Pitts).v3pt, 0.5, 0.3, 0.0, feet: RelaxedFeet);
        var el = Land(e, false, TypeOf(Extra).v3pt, 0.5, 0.3, 0.0, feet: RelaxedFeet);
        _out.WriteLine($"relaxed feet, 3-pt rollout: Pitts swung {pl.MaxHdgDeg:F0}°, Extra {el.MaxHdgDeg:F1}°");
        Assert.True(pl.MaxHdgDeg > 20, "the Pitts should get away from relaxed feet");
        Assert.True(el.MaxHdgDeg < 10, "the Extra should stay straight with relaxed feet");
        foreach (var c in new[] { p, e })
            foreach (double v in new[] { 12.0, 18.0 })
            {
                var r = Recover(c, v, 8, 0.4);
                _out.WriteLine($"{c.Id}: 8° swerve at {v} m/s, pilot after 0.4 s: max {r.maxDeg:F1}°, ends {r.endDeg:F1}°, off-centre {r.maxOffM:F1} m");
                Assert.True(r.maxDeg < 35 && Math.Abs(r.endDeg) < 8 && r.maxOffM < 12 && !r.struck, $"{c.Id} not recovered at {v} m/s");
            }
    }

    /// <summary>Opt-in table (GROUND_PROBE=1): every scenario for both types, for the log and before/after comparisons.</summary>
    [Fact]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("GROUND_PROBE") == null) return;
        foreach (var (f, tail, vr, vApp3, vAppW) in Types)
        {
            var c = Load(f);
            Taxi(c, _out);
            Takeoff(c, tail, vr, ThreePointDeg(c) - 0.5, _out);
            foreach (bool wheel in new[] { false, true })
                foreach (double brakeV in new[] { 0.0, 20.0 })
                {
                    var l = Land(c, wheel, wheel ? vAppW : vApp3, wheel ? 0.3 : 0.5, wheel ? 0.5 : 0.3, brakeV, log: brakeV == 0 ? _out : null);
                    _out.WriteLine($"{c.Id} {(wheel ? "wheel" : "3-pt")} landing{(brakeV > 0 ? " +brakes" : "")}: touchdown {l.TouchdownV:F1} m/s, roll {(brakeV > 0 ? l.RollBrakesM : l.RollM):F0} m, bounce {l.MaxBounceM:F2} m, off-centre {l.MaxOffCentreM:F1} m, hdg {l.MaxHdgDeg:F1}°, min pitch {l.MinPitchDeg:F1}°, tail down {l.TailDownAtEnd}, strike {l.Struck}");
                }
            foreach (double v in new[] { 8.0, 14.0 })
            {
                var g = GroundStabilityProbe.YawRateGrowth(c, v, 5, 3);
                var rec = Recover(c, v + 4, 8, 0.4);
                _out.WriteLine($"{c.Id} v={v}: hands-off yaw rate x{g.ratio:F2}, heading {g.heading:F1}° in 3 s | 8° swerve at {v + 4} m/s, pilot after 0.4 s: max {rec.maxDeg:F1}°, end {rec.endDeg:F1}°, off-centre {rec.maxOffM:F1} m, strike {rec.struck}");
            }
        }
    }
}

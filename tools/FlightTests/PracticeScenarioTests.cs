using System.Linq;
using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Crosswind / flare / approach practice (owner 2026-09-15): the game flies every axis but the user's.</summary>
[Collection("WorldTerrainActive")]
public class PracticeScenarioTests
{
    private readonly ITestOutputHelper _out;
    private bool Trace;
    public PracticeScenarioTests(ITestOutputHelper o) { _out = o; }

    private static double Roll(Aircraft ac) { var q = ac.State.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.3; }
    private static double Pitch(Aircraft ac) { var q = ac.State.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3; }
    private static AircraftConfig Load(string f) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));
    internal static WorldTerrain.RunwayEnd TestRunway() => Runway();
    private static WorldTerrain.RunwayEnd Runway()
    {
        var main = Array.Find(WorldTerrain.AirportStrips, st => st.Kind == "paved");
        return new WorldTerrain.RunwayEnd(main, 0, 0, 0);
    }

    /// <summary>Run with a "perfect" user who applies the game's own law on the user's axis (one step late).</summary>
    private PracticeScenario Run(string file, PracticeKind kind, PracticeWind wind, Func<PracticeScenario, ControlInputs, ControlInputs>? userLaw = null, double maxSec = 150)
    {
        var c = Load(file); WorldTerrain.Active = null;
        var sc = new PracticeScenario(kind, wind, c, Runway(), 0.0);
        var ac = sc.Spawn();
        var sim = new SimLoop(ac);
        ControlInputs user = ControlInputs.Neutral;
        for (double t = 0; t < maxSec && sc.Phase != PracticePhase.Finished; t += 0.02)
        {
            var inputs = sc.Step(ac, user, 0.02);
            ac.BrakeInput = sc.GameBrake; ac.BrakeBias = sc.GameBrakeBias;
            sim.RunFor(0.02, inputs);
            if (Environment.GetEnvironmentVariable("NOCONSTRAIN") == null) sc.ConstrainLongitudinal(ac);
            user = userLaw != null ? userLaw(sc, sc.Autopilot) : sc.Autopilot;
            if (Trace && Math.Abs(t / 1.0 - Math.Round(t / 1.0)) < 1e-6) _out.WriteLine($"   t={t,4:F0} leg {sc.Leg} agl {sc.AglM,6:F1} ias {sc.AirspeedMs,5:F1} vT {sc.TargetSpeedMs,5:F1} pitch {Pitch(ac),5:F1}° roc {-sc.SinkMs * 196.85,6:F0} mains {sc.MainsAglM,5:F2} ias {sc.AirspeedMs,5:F1} align {sc.AlignmentDeg,6:F1}° off {sc.OffCentreM,6:F1} ail {inputs.Aileron,5:F2} ele {inputs.Elevator,5:F2} rud {inputs.Rudder,5:F2} lev {inputs.ThrottleLever,5:F2} gnd {sc.OnGround}");
        }
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null;   // the scenario drives the wind: never leak it into other tests
        _out.WriteLine($"{file} {kind} {wind} (Vso {sc.VsoMs:F1} m/s): {sc.Phase} t={sc.Time:F0}s along {sc.AlongM:F0} m  in-band {sc.InBandFraction * 100:F0}%  rms {sc.RmsError:F1}  touchdown {(sc.TouchedDown ? $"sink {sc.TouchdownSinkMs:F1} m/s align {sc.TouchdownAlignDeg:F1}° off {sc.TouchdownOffCentreM:F1} m" : "none")}  score {sc.Score:F0} {sc.Verdict} [{sc.EndReason}]");
        return sc;
    }

    [Theory]
    [InlineData("pa18-cub-like.json", PracticeKind.CrosswindRudder, PracticeWind.Steady)]
    [InlineData("pa18-cub-like.json", PracticeKind.CrosswindAileron, PracticeWind.Gusty)]
    [InlineData("c172-like.json", PracticeKind.CrosswindRudder, PracticeWind.Shifting)]
    [InlineData("decathlon-8kcab-like.json", PracticeKind.CrosswindAileron, PracticeWind.Steady)]
    public void LevelCrosswindRunReachesTheDepartureEndHeldStraightAndCentred(string file, PracticeKind kind, PracticeWind wind)
    {
        var sc = Run(file, kind, wind);
        Assert.Equal(PracticePhase.Finished, sc.Phase);
        Assert.False(sc.TouchedDown, "the level exercise never touches down");
        Assert.True(sc.AlongM > sc.Runway.LengthM - 60, $"ended at {sc.AlongM:F0} m, not the departure end");
        Assert.True(sc.InBandFraction > (wind == PracticeWind.Shifting ? 0.15 : wind == PracticeWind.Gusty ? 0.2 : 0.5), $"the game's own law only held the band {sc.InBandFraction * 100:F0} % of the time");
    }

    [Theory]
    [InlineData("pa18-cub-like.json", PracticeKind.LandingRudder, PracticeWind.Steady)]
    [InlineData("c172-like.json", PracticeKind.LandingAileron, PracticeWind.Gusty)]
    [InlineData("decathlon-8kcab-like.json", PracticeKind.LandingRudder, PracticeWind.Shifting)]
    public void CrosswindLandingTouchesDownGentlyAndStraight(string file, PracticeKind kind, PracticeWind wind)
    {
        var sc = Run(file, kind, wind);
        Assert.True(sc.TouchedDown, "no touchdown");
        Assert.True(sc.TouchdownSinkMs < 3.0, $"touchdown sink {sc.TouchdownSinkMs:F1} m/s");
        Assert.True(Math.Abs(sc.TouchdownAlignDeg) < 12, $"touched down {sc.TouchdownAlignDeg:F1}° crabbed");
        Assert.True(Math.Abs(sc.TouchdownOffCentreM) < 12, $"touched down {sc.TouchdownOffCentreM:F1} m off the centreline");
    }

    [Theory]
    [InlineData("pa18-cub-like.json", PracticeKind.Flare, PracticeWind.Calm)]
    [InlineData("c172-like.json", PracticeKind.FlareSideView, PracticeWind.HeadwindGusty)]
    [InlineData("c172-like.json", PracticeKind.ApproachSideView, PracticeWind.Calm)]
    [InlineData("pa18-cub-like.json", PracticeKind.ApproachSideView, PracticeWind.Tailwind)]
    [InlineData("glider-2-33-like.json", PracticeKind.ApproachSideView, PracticeWind.Headwind)]
    public void FlareAndApproachExercisesLandWithTheGamesOwnLaw(string file, PracticeKind kind, PracticeWind wind)
    {
        var sc = Run(file, kind, wind, maxSec: 200);
        if (kind == PracticeKind.ApproachSideView) _out.WriteLine($"  glideslope {sc.GlideslopeRad * 57.3:F1}°");
        Assert.True(sc.TouchedDown, "no touchdown");
        Assert.True(sc.TouchdownSinkMs < 3.0, $"touchdown sink {sc.TouchdownSinkMs:F1} m/s");
        Assert.True(Math.Abs(sc.TouchdownOffCentreM) < 12, $"touched down {sc.TouchdownOffCentreM:F1} m off the centreline");
    }

    [Fact]
    public void TraceSTurns()
    {
        Trace = true;
        Run("c172-like.json", PracticeKind.ClimbLevelDescend, PracticeWind.Calm, maxSec: 130);
        Trace = false;
    }

    [Fact]
    public void GlideslopeIsShallowerThanTheIdleGlideForPoweredTypesAndHalfSpoilerForGliders()
    {
        var c172 = new PracticeScenario(PracticeKind.ApproachSideView, PracticeWind.Steady, Load("c172-like.json"), Runway(), 0);
        var glider = new PracticeScenario(PracticeKind.ApproachSideView, PracticeWind.Steady, Load("glider-2-33-like.json"), Runway(), 0);
        _out.WriteLine($"172 glideslope {c172.GlideslopeRad * 57.3:F1}°, 2-33 {glider.GlideslopeRad * 57.3:F1}°");
        Assert.InRange(c172.GlideslopeRad * 57.3, 2.5, 8.0);
        Assert.InRange(glider.GlideslopeRad * 57.3, 2.5, 8.0);
        Assert.True(glider.GlideslopeRad > c172.GlideslopeRad * 0.5);
    }

    // ---- points (owner 2026-10-03): the orb's grades, the points, the judged moments ----

    [Fact]
    public void EveryLessonHasAGoalAndJudgedCriteria()
    {
        foreach (PracticeKind k in Enum.GetValues<PracticeKind>())
        {
            var r = LessonJudge.For(k, false);
            Assert.False(string.IsNullOrEmpty(r.Goal), $"{k} has no goal");
            Assert.NotEmpty(r.Live);
            foreach (var c in r.Live) Assert.True(c.Green <= c.Yellow && c.Yellow <= c.Orange, $"{k}: {c.Name} bands out of order");
        }
    }

    [Fact]
    public void HeldCrosswindEarnsPointsHandsOffLosesThem()
    {
        var good = Run("pa18-cub-like.json", PracticeKind.CrosswindRudder, PracticeWind.Steady);
        var bad = Run("pa18-cub-like.json", PracticeKind.CrosswindRudder, PracticeWind.Steady, (s, auto) => new ControlInputs(auto.Aileron, auto.Elevator, 0, auto.ThrottleLever));
        _out.WriteLine($"held: {good.Judge.Points:F0} pts (green {good.Judge.Seconds[0]:F0}s yellow {good.Judge.Seconds[1]:F0}s orange {good.Judge.Seconds[2]:F0}s red {good.Judge.Seconds[3]:F0}s); hands-off: {bad.Judge.Points:F0} pts (red {bad.Judge.Seconds[3]:F0}s)");
        Assert.True(good.Judge.Seconds[0] > 0.7 * (good.Judge.Seconds.Sum()), "the game's own rudder is mostly green");
        Assert.True(good.Judge.Points > bad.Judge.Points + 100);
        Assert.True(bad.Judge.Seconds[2] + bad.Judge.Seconds[3] > 3, "hands off spends time orange/red");
    }

    [Theory]
    [InlineData(PracticeKind.Flare)]
    [InlineData(PracticeKind.ApproachSideView)]
    public void LandingsJudgeTheirMoments(PracticeKind kind)
    {
        var sc = Run("c172-like.json", kind, PracticeWind.Calm);
        foreach (var m in sc.Judge.Moments) _out.WriteLine($"  {m.name}: {m.grade} {m.detail} ({m.points:+0;-0} pts)");
        _out.WriteLine($"points {sc.Judge.Points:F0}; round-out {sc.RoundOutFt:F0} ft");
        Assert.True(sc.TouchedDown);
        Assert.Equal(sc.Judge.Rules.Moments.Count, sc.Judge.Moments.Count);

        Assert.True(sc.Judge.Points > 0);
    }

    [Theory]
    [InlineData("c172-like.json", 0.0)]
    [InlineData("c172-like.json", 0.5)]
    [InlineData("c172-like.json", 1.0)]
    [InlineData("pa28-archer-like.json", 1.0)]
    [InlineData("cirrus-sr22-like.json", 0.0)]
    [InlineData("cirrus-sr22-like.json", 1.0)]
    public void FlareLessonStartsOnSpeedInTrimOnThePowerOffGlide(string file, double flaps)
    {
        // Owner 2026-10-03: idle, on speed, in trim — hands off (stick on the preset trim) it stays on its glide.
        var c = Load(file); WorldTerrain.Active = null;
        var sc = new PracticeScenario(PracticeKind.Flare, PracticeWind.Calm, c, Runway(), 0.0, flapFraction: flaps);
        var ac = sc.Spawn(); var sim = new SimLoop(ac); sc.SkipBriefing();
        double v0 = -1, maxDv = 0, sink0 = 0;
        var hands = new ControlInputs(0, sc.TrimStick, 0, 1.0, true, true, true);   // no finger on the pads: controls RELEASED (they float on their trim)
        for (double t = 0; t < 3.0; t += 0.02)
        {
            var inp = sc.Step(ac, hands, 0.02);
            sim.RunFor(0.02, inp); sc.ConstrainLongitudinal(ac);
            if (v0 < 0) v0 = sc.AirspeedMs;
            if (sc.MainsAglM > 4.6) maxDv = Math.Max(maxDv, Math.Abs(sc.AirspeedMs - v0));   // until the round-out height
            if (t < 0.03) sink0 = sc.SinkMs;
        }
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null;
        _out.WriteLine($"{file} flaps {flaps}: Vso {sc.VsoMs * 1.944:F0} kt, start {v0 * 1.944:F0} kt, glide {sc.FlareGlideRad * 57.3:F1}° ({1 / Math.Tan(sc.FlareGlideRad):F1}:1), sink {sink0 * 196.85:F0} fpm, speed wander {maxDv * 1.944:F1} kt in 3 s, throttle lever {sc.LastInputs.ThrottleLever:F1}");
        Assert.Equal(flaps, ac.FlapFraction, 3);
        Assert.Equal(1.0, sc.LastInputs.ThrottleLever, 3);                  // idle
        Assert.True(maxDv * 1.944 < 3.0, "starts on speed and in trim");
    }

    [Theory]
    [InlineData("c172-like.json", 0.0)]
    [InlineData("c172-like.json", 1.0)]
    public void RoundOutBleedsTheSpeedTheSameInBothViews(string file, double flaps)
    {
        // Owner 2026-10-05: "the physics feel different between the rear and side views … they don't slow down". The same
        // pilot (hold the height from 2 s on, like a long hold-off at idle) in the rear-view and side-view flare lessons:
        // the speed must bleed (≈1.5–3 kt/s, the 172's idle L/D) and identically in both.
        double[] Fly(PracticeKind kind)
        {
            var c = Load(file); WorldTerrain.Active = null;
            var sc = new PracticeScenario(kind, PracticeWind.Calm, c, Runway(), 0.0, flapFraction: flaps);
            var ac = sc.Spawn(); var sim = new SimLoop(ac); sc.SkipBriefing();
            var kts = new List<double>();
            double stick = sc.TrimStick, h0 = double.NaN, s0 = 0, hInt = 0;
            int n = 0;
            for (double t = 0; t < 10.0; t += 0.02, n++)
            {
                if (t > 2.0)
                {
                    // The pilot holds the height: back stick (−) against a sink or a loss of height, damped by the pitch rate.
                    if (double.IsNaN(h0)) { h0 = sc.AglM; s0 = stick; }
                    double q = ac.State.Rates.Y;
                    // (gentler on the sink, more pitch damping, 2026-10-06: with the real pitch stability the old gains zoomed in the
                    // round-out and the "bleed" measured the zoom, not the drag)
                    // A progressive flare: the old gains slammed the stick full aft in 0.25 s and pulled 23° of α (the airplane is no
                    // longer twice as stable as a real 172, 2026-10-06) — the "bleed" then measured a zoom and a stall.
                    hInt = Math.Clamp(hInt + 0.01 * (sc.AglM - h0) * 0.02, -0.5, 0.5);
                    stick = Math.Clamp(s0 + hInt - 0.06 * sc.SinkMs + 0.03 * (sc.AglM - h0) + 0.6 * q, -1, 1);
                }
                var inp = sc.Step(ac, new ControlInputs(0, stick, 0, 1.0), 0.02);
                sim.RunFor(0.02, inp); sc.ConstrainLongitudinal(ac);
                if (n % 50 == 0) kts.Add(sc.AirspeedMs * 1.943844);
                if (n % 50 == 0 && Environment.GetEnvironmentVariable("BLEEDTRACE") != null) _out.WriteLine($"   {kind} t={t:F0} agl {sc.AglM:F1} sink {sc.SinkMs:F2} α {Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3:F1} stick {stick:F2} kt {sc.AirspeedMs * 1.943844:F1}");
            }
            Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null;
            return kts.ToArray();
        }
        double[] rear = Fly(PracticeKind.Flare), side = Fly(PracticeKind.FlareSideView);
        _out.WriteLine($"{file} flaps {flaps}: rear {string.Join(" ", rear.Select(k => k.ToString("F1")))}");
        _out.WriteLine($"{file} flaps {flaps}: side {string.Join(" ", side.Select(k => k.ToString("F1")))}");
        double bleed = (rear[3] - rear[5]) / 2.0;   // the first two seconds of the hold, before it nears the stall
        _out.WriteLine($"bleed in the hold-off {bleed:F2} kt/s (rear), {(side[3] - side[5]) / 2.0:F2} kt/s (side)");
        for (int i = 0; i < Math.Min(6, Math.Min(rear.Length, side.Length)); i++) Assert.True(Math.Abs(rear[i] - side[i]) < 0.5, $"t={i}s rear {rear[i]:F1} side {side[i]:F1}");   // (past the stall the side view's lateral lock differs)
        Assert.InRange(bleed, 1.0, flaps > 0.5 ? 5.0 : 4.0);   // full flap: the flap's lift carries its induced drag (2026-10-06), L/D ~4.6 → ~4 kt/s
    }

    [Theory]
    [InlineData("c172-like.json", 1.0)]
    [InlineData("c172-like.json", 0.0)]
    [InlineData("cirrus-sr22-like.json", 1.0)]
    public void FlareFromFiftyFeetMeasuredAgainstThePoh(string file, double flaps)
    {
        // Owner 2026-10-05: "the airplanes just don't slow down, it is like the power is above idle". Benchmark: C172S POH
        // landing, flaps 30, power off, 61 KIAS at 50 ft: 1,335 ft over 50 ft − 575 ft ground roll = ~760 ft (232 m) in the air.
        var c = Load(file); WorldTerrain.Active = null;
        var sc = new PracticeScenario(PracticeKind.Flare, PracticeWind.Calm, c, Runway(), 0.0, flapFraction: flaps);
        var ac = sc.Spawn(); var sim = new SimLoop(ac); sc.SkipBriefing();
        ControlInputs user = ControlInputs.Neutral;
        double x50 = double.NaN, v50 = 0, xTd = double.NaN, vTd = 0, maxLever = -9, holdV0 = double.NaN, holdT0 = 0, holdV1 = 0, holdT1 = 0;
        var log = new System.Text.StringBuilder();
        for (double t = 0; t < 90 && !sc.TouchedDown; t += 0.02)
        {
            var inputs = sc.Step(ac, user, 0.02);
            sim.RunFor(0.02, inputs); sc.ConstrainLongitudinal(ac);
            user = sc.Autopilot;
            maxLever = Math.Max(maxLever, -inputs.ThrottleLever);
            if (double.IsNaN(x50) && sc.MainsAglM <= 15.24) { x50 = sc.AlongM; v50 = sc.AirspeedMs; }
            if (!double.IsNaN(x50) && sc.MainsAglM < 0.6) { if (double.IsNaN(holdV0)) { holdV0 = sc.AirspeedMs; holdT0 = t; } holdV1 = sc.AirspeedMs; holdT1 = t; }
            if (!double.IsNaN(x50) && Math.Abs(t * 2 - Math.Round(t * 2)) < 1e-6) log.Append($" [{t:F1}s {sc.MainsAglM:F1}m {sc.AirspeedMs * 1.943844:F0}kt]");
            if (sc.TouchedDown) { xTd = sc.AlongM; vTd = sc.AirspeedMs; }
        }
        _out.WriteLine(log.ToString());
        double air = xTd - x50, kts = 1.943844;
        double holdBleed = holdT1 > holdT0 ? (holdV0 - holdV1) * kts / (holdT1 - holdT0) : 0;
        _out.WriteLine($"{file} flaps {flaps}: 50 ft at {v50 * kts:F0} kt → touchdown at {vTd * kts:F0} kt after {air:F0} m ({air / 0.3048:F0} ft) in the air; hold-off (< 2 ft) {holdT1 - holdT0:F1} s bleeding {holdBleed:F2} kt/s; max lever forward {maxLever:F2} (−1 = idle)");
        Assert.True(sc.TouchedDown);
        Assert.Equal(-1.0, maxLever, 6);   // power off throughout
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    public void HandsOffNoFlarePorpoisesAndIsJudgedForEveryBounce(double flaps)
    {
        // Owner 2026-10-06: "i didn't touch a thing and let the 172 land and it porpoised down the runway with every contact
        // nose wheel first then it got a score of 423 with one simple comment 'bounced'". Hands off at the trim: no flare.
        var c = Load("c172-like.json"); WorldTerrain.Active = null;
        var sc = new PracticeScenario(PracticeKind.Flare, PracticeWind.Calm, c, Runway(), 0.0, flapFraction: flaps);
        var ac = sc.Spawn(); var sim = new SimLoop(ac); sc.SkipBriefing();
        var hands = new ControlInputs(0, sc.TrimStick, 0, 1.0, true, true, true);   // no finger on the pads: controls RELEASED (they float on their trim)
        for (double t = 0; t < 120 && sc.Phase != PracticePhase.Finished; t += 0.02)
        {
            var inputs = sc.Step(ac, hands, 0.02);
            ac.BrakeInput = sc.GameBrake; ac.BrakeBias = sc.GameBrakeBias;
            sim.RunFor(0.02, inputs);
        }
        foreach (var m in sc.Judge.Moments) _out.WriteLine($"  {m.name}: {m.grade} {m.detail} ({m.points:+0;-0})");
        _out.WriteLine($"flaps {flaps}: {sc.BounceHeightsM.Count} bounces [{string.Join(", ", sc.BounceHeightsM.Select(h => $"{h * 3.28:F1} ft"))}], nose-first touches {sc.NoseFirstTouches}; verdict '{sc.Verdict}', score {sc.Score:F0}, points {sc.Judge.Points:F0}");
        Assert.True(sc.TouchedDown);
        if (sc.BounceHeightsM.Count(h => h > 0.15) >= 2)
        {
            Assert.StartsWith("Bounced. Bounced.", sc.Verdict);                         // every bounce named
            Assert.True(sc.Judge.Moments.Count(m => m.name == LessonJudge.Std.Bounce.Name) >= 2);
            Assert.True(sc.Score < 50, $"a porpoise scored {sc.Score:F0}");
        }
    }

    [Fact]
    public void ABouncedOrBrokenLandingIsNotFirmButFine()
    {
        // Owner 2026-10-03: "I bounced my first landing then hit so hard the plane broke apart and it gave me 82 % firm but fine".
        // Shove the nose down from 10 ft: the whole arrival must be judged, not the first touch.
        var sc = Run("c172-like.json", PracticeKind.Flare, PracticeWind.Calm, (s, auto) => s.MainsAglM < 3.0 || s.TouchedDown ? new ControlInputs(auto.Aileron, 0.6, auto.Rudder, auto.ThrottleLever) : auto);
        foreach (var m in sc.Judge.Moments) _out.WriteLine($"  {m.name}: {m.grade} {m.detail} ({m.points:+0;-0})");
        _out.WriteLine($"verdict '{sc.Verdict}', score {sc.Score:F0}, points {sc.Judge.Points:F0}");
        Assert.NotEqual("Firm but fine.", sc.Verdict);
        Assert.NotEqual("Greaser.", sc.Verdict);
        Assert.Contains(sc.Judge.Moments, m => m.grade == Grade.Red);
    }

    [Fact]
    public void HandsOffRudderLetsTheNoseCrabAwayFromTheRunwayHeading()
    {
        double worst = 0;
        var sc = Run("pa18-cub-like.json", PracticeKind.CrosswindRudder, PracticeWind.Steady,
            (s, auto) => { worst = Math.Max(worst, Math.Abs(s.AlignmentDeg)); return new ControlInputs(auto.Aileron, auto.Elevator, 0, auto.ThrottleLever); });
        _out.WriteLine($"hands-off rudder: worst misalignment {worst:F1}°");
        Assert.True(worst > 4, "with no rudder the crosswind should leave the fuselage crabbed off the runway heading");
    }

    [Fact]
    public void ShiftingWindSwapsSidesEveryThousandFeetAndGustsRiseToHalfAgain()
    {
        var c = Load("pa18-cub-like.json");
        var shifting = new PracticeScenario(PracticeKind.CrosswindRudder, PracticeWind.Shifting, c, Runway(), 0);
        Vec3 w0 = shifting.WindVector(0, 100), w1 = shifting.WindVector(0, 400), w2 = shifting.WindVector(0, 700);
        Assert.True(w0.Y < 0 && w1.Y > 0 && w2.Y < 0, $"wind y: {w0.Y:F1} {w1.Y:F1} {w2.Y:F1}");
        Assert.InRange(Math.Abs(w1.Y), 3.9, 4.1);
        var gusty = new PracticeScenario(PracticeKind.CrosswindRudder, PracticeWind.Gusty, c, Runway(), 0);
        var ac = gusty.Spawn(); var sim = new SimLoop(ac);
        double max = 0, min = 99;
        for (double t = 0; t < 30; t += 0.02) { gusty.Step(ac, gusty.Autopilot, 0.02); sim.RunFor(0.02, gusty.LastInputs); double sp = gusty.WindNow.Length; max = Math.Max(max, sp); min = Math.Min(min, sp); }
        Atmosphere.SteadyWind = Vec3.Zero;
        _out.WriteLine($"gusty: {min:F1}–{max:F1} m/s");
        Assert.InRange(max, 5.8, 6.2); Assert.InRange(min, 3.9, 4.1);
    }

    // ---- airwork: S-turns and stalls (owner 2026-09-15) --------------------------------------------------------------

    [Theory]
    [InlineData("pa18-cub-like.json")]
    [InlineData("c172-like.json")]
    [InlineData("glider-2-33-like.json")]
    public void STurnsTestFliesOneSpeedCycleWithFullAileronReversals(string file)
    {
        var sc = Run(file, PracticeKind.STurnsTest, PracticeWind.Calm,
            (s, auto) => new ControlInputs(s.UserHasControl ? s.TargetBankSign : auto.Aileron, auto.Elevator, s.UserHasControl ? 0.3 * s.TargetBankSign : auto.Rudder, auto.ThrottleLever), maxSec: 140);
        _out.WriteLine($"  cruise {sc.CruiseMs * 1.944:F0} kt, 1.15 Vso {1.15 * sc.VsoMs * 1.944:F0} kt, reversals {sc.Reversals}, agl {sc.AglM:F0} m");
        Assert.Equal(PracticePhase.Finished, sc.Phase);
        Assert.Equal("one speed cycle flown", sc.EndReason);
        Assert.True(sc.Reversals >= 8, $"only {sc.Reversals} reversals");
        Assert.True(sc.Score > 40, $"score {sc.Score:F0}");
        Assert.True(sc.CruiseMs > 1.3 * sc.VsoMs, "cruise above 1.3 Vso");
    }

    [Fact]
    public void StallRudderExerciseStallsRepeatedlyAndTheRudderHoldsTheWing()
    {
        var sc = Run("c172-like.json", PracticeKind.StallRudder, PracticeWind.Calm,
            (s, auto) => new ControlInputs(auto.Aileron, auto.Elevator, Math.Clamp(-0.8 * s.BankDeg / 57.3 - 0.3 * s.RollRateDegS / 57.3, -1, 1), auto.ThrottleLever), maxSec: 60);   // a student's rudder: against the wing drop
        _out.WriteLine($"  stalls {sc.StallCount}, max wing drop {sc.MaxWingDropDeg:F0}°, max sink {sc.MaxSinkMs:F1} m/s, agl {sc.AglM:F0}");
        Assert.True(sc.StallCount >= 2, $"only {sc.StallCount} stalls in 60 s");
        Assert.NotEqual(PracticePhase.Finished, sc.Phase);   // endless: still flying
    }

    [Theory]
    [InlineData("c172-like.json", PracticeKind.StallElevator)]
    [InlineData("pa18-cub-like.json", PracticeKind.StallSideView)]
    public void UserStallsAndRecoversWithElevator(string file, PracticeKind kind)
    {
        double maxRoll = 0, maxCross = 0; bool noseDropped = false; double pitchAtStall = double.NaN;
        var sc = Run(file, kind, PracticeWind.Calm, (s, auto) =>
        {
            double t = s.Time - PracticeScenario.BriefingSec;
            double ele = !s.UserHasControl ? auto.Elevator : (t % 12.0) < 8.0 ? -0.55 : 0.3;   // pull past the stall 8 s, push 4 s
            maxRoll = Math.Max(maxRoll, Math.Abs(s.BankDeg)); maxCross = Math.Max(maxCross, Math.Abs(s.OffCentreM));
            if (s.Stalled && double.IsNaN(pitchAtStall)) pitchAtStall = s.AlphaDeg;
            return new ControlInputs(auto.Aileron, ele, auto.Rudder, auto.ThrottleLever);
        }, maxSec: 50);
        _out.WriteLine($"  stalls {sc.StallCount}, max sink {sc.MaxSinkMs:F1} m/s, max roll {maxRoll:F0}°, max cross {maxCross:F1} m");
        Assert.True(sc.StallCount >= 2, $"only {sc.StallCount} stalls");
        Assert.True(sc.MaxSinkMs > 3, $"held in the stall the sink only reached {sc.MaxSinkMs:F1} m/s");
        if (kind == PracticeKind.StallSideView) { Assert.True(maxRoll < 0.5 && maxCross < 0.5, "side view must stay longitudinal"); }
        Assert.NotEqual("hit the ground", sc.EndReason);
    }

    [Theory]
    [InlineData("pa18-cub-like.json")]
    [InlineData("c172-like.json")]
    public void TrimProbeElevatorNeededVsSpeed(string file)
    {
        var c = Load(file);
        double max = c.Controls.Elevator.MaxDeflRad;
        for (double v = 14; v <= 34; v += 2)
        {
            var t = TrimSolver.SolveGliderTrim(c, v, 500);
            _out.WriteLine($"{file} v {v,4:F0} m/s ({v * 1.944,3:F0} kt): converged {t.Converged}  alpha {t.AlphaRad * 57.3,5:F1}°  elevator {t.ElevatorRad * 57.3,6:F1}° of ±{max * 57.3:F0}°  L/D {t.GlideRatio,5:F1}");
        }
    }

    /// <summary>Pitch balance: wing, tail and fuselage pitching moments about the CG versus angle of attack, elevator neutral and full aft.</summary>
    [Theory]
    [InlineData("pa18-cub-like.json", 22.0)]
    [InlineData("c172-like.json", 26.0)]
    public void PitchMomentBreakdown(string file, double v)
    {
        var c = Load(file);
        var tables = Aircraft.BuildAirfoilTables(c);
        var cg = c.Mass.CgVec();
        double stabX = double.NaN, wingX = double.NaN, wingChord = 1;
        foreach (var sf in c.Surfaces) { if (sf.Id == "elevator") stabX = sf.Strips[0].Pos[0]; if (sf.Id == "wing") { wingX = sf.Strips[0].Pos[0]; wingChord = sf.Strips[0].Chord; } }
        _out.WriteLine($"{file}: cg x {cg.X:F2}, wing LE x {wingX:F2} chord {wingChord:F2} (quarter chord x {wingX - 0.25 * wingChord:F2}), stab x {stabX:F2}");
        foreach (double eDeg in new[] { 0.0, -30.0 })
        {
            foreach (double aDeg in new[] { 4.0, 8.0, 12.0, 14.0, 16.0, 18.0, 22.0 })
            {
                double a = aDeg * Math.PI / 180;
                var vel = new Vec3(v * Math.Cos(a), 0, v * Math.Sin(a));
                var flow = new FlyingGame.Core.Aero.StripFlowState(); flow.EnsureSize(64);
                FlyingGame.Core.Aero.ForceDebug.Samples = new System.Collections.Generic.List<FlyingGame.Core.Aero.ForceSample>();
                var (F, M) = FlyingGame.Core.Aero.AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, 1.225, new FlyingGame.Core.Aero.ControlDeflections(0, eDeg * Math.PI / 180, 0, 0), -1.0, flow);
                double mWing = 0, mTail = 0, mOther = 0, lift = 0;
                foreach (var s in FlyingGame.Core.Aero.ForceDebug.Samples)
                {
                    Vec3 r = s.PosBody - cg; double m = r.Z * s.ForceBody.X - r.X * s.ForceBody.Z + s.MomentBody.Y;
                    if (s.Kind is "lift" or "drag" or "moment") { if (s.PosBody.X < stabX + 1.0) mTail += m; else mWing += m; } else mOther += m;
                    if (s.Kind == "lift") lift += -s.ForceBody.Z;
                }
                FlyingGame.Core.Aero.ForceDebug.Samples = null;
                double weight = c.Mass.MassKg * 9.81;
                _out.WriteLine($"  elev {eDeg,4:F0}°  alpha {aDeg,3:F0}°  lift/W {lift / weight:F2}  My total {M.Y,7:F0} N·m  = wing {mWing,7:F0} + tail {mTail,7:F0} + other {mOther,6:F0}");
            }
        }
    }

    [Theory]
    [InlineData("c172-like.json")]
    [InlineData("glider-2-33-like.json")]
    public void StraightLessonABankSlipsAndWeathervanesIntoATurnWithTheRudderNeutral(string file)
    {
        // Hands off the aileron after a 2 s roll input: the game never touches the rudder, so the bank slips and the nose swings.
        double maxBeta = 0;
        var sc = Run(file, PracticeKind.Straight, PracticeWind.Calm, (s, auto) =>
        {
            double t = s.Time - PracticeScenario.BriefingSec;
            double ail = !s.UserHasControl ? auto.Aileron : t < 1.5 ? 0.3 : 0.0;   // a modest roll input, then hands off
            maxBeta = Math.Max(maxBeta, Math.Abs(s.BetaDeg));
            return new ControlInputs(ail, auto.Elevator, 0, auto.ThrottleLever);
        }, maxSec: PracticeScenario.BriefingSec + 25);
        _out.WriteLine($"  bank {sc.BankDeg:F0}°, heading drift {sc.HeadingDriftDeg:F0}°, max slip {maxBeta:F1}°, rudder {sc.LastInputs.Rudder:F2}");
        Assert.Equal(0.0, sc.LastInputs.Rudder, 3);
        Assert.True(Math.Abs(sc.HeadingDriftDeg) > 15, $"the bank should have turned it: heading drift {sc.HeadingDriftDeg:F0}°");
        Assert.True(maxBeta > 0.5, $"a slip should show before the turn: max {maxBeta:F1}°");
        // Wings held level by the game's own law instead: it goes straight.
        var sc2 = Run(file, PracticeKind.Straight, PracticeWind.Calm, maxSec: PracticeScenario.BriefingSec + 25);
        _out.WriteLine($"  wings level: heading drift {sc2.HeadingDriftDeg:F1}°");
        // Powered types drift a little left with the rudder neutral at cruise (P-factor / slipstream); the lesson shows that too.
        Assert.True(Math.Abs(sc2.HeadingDriftDeg) < (file.Contains("glider") ? 5 : 20), $"wings level should go (nearly) straight: drift {sc2.HeadingDriftDeg:F1}°");
    }

    // ---- climb / level / descend, glide speed-to-fly, Vx / Vy (owner 2026-09-15) ----------------------------------

    [Fact]
    public void PerformanceSpeedsAreOrderedSensibly()
    {
        foreach (string f in new[] { "c172-like.json", "pa18-cub-like.json" })
        {
            var sc = new PracticeScenario(PracticeKind.ClimbLevelDescend, PracticeWind.Calm, Load(f), Runway(), 0);
            sc.Spawn();
            _out.WriteLine($"{f}: Vso {sc.VsoMs * 1.944:F0} kt  min sink {sc.MinSinkMs * 1.944:F0}  best L/D {sc.BestLdMs * 1.944:F0} ({sc.BestGlideRatio:F1}:1)  Vx {sc.VxMs * 1.944:F0}  Vy {sc.VyMs * 1.944:F0} (ROC {sc.RocAtVyMs * 196.85:F0} fpm)  cruise75 {sc.CruiseMs * 1.944:F0}");
            Assert.True(sc.MinSinkMs < sc.BestLdMs, "min sink slower than best L/D");
            Assert.True(sc.VxMs < sc.VyMs, "Vx slower than Vy");
            Assert.True(sc.VyMs < sc.CruiseMs, "Vy slower than cruise");
            Assert.InRange(sc.RocAtVyMs * 196.85, 300, 1700);   // the sim climbs ~1.6× the book (propeller efficiency + drag polar) — noted for the owner
        }
        var g = new PracticeScenario(PracticeKind.GlideRear, PracticeWind.Headwind, Load("glider-2-33-like.json"), Runway(), 0); g.Spawn();
        var g2 = new PracticeScenario(PracticeKind.GlideRear, PracticeWind.Tailwind, Load("glider-2-33-like.json"), Runway(), 0); g2.Spawn();
        _out.WriteLine($"2-33: best L/D {g.BestLdMs * 1.944:F0} kt, speed to fly headwind {g.SpeedToFlyMs * 1.944:F0} kt, tailwind {g2.SpeedToFlyMs * 1.944:F0} kt");
        Assert.True(g.SpeedToFlyMs > g.BestLdMs, "headwind: fly faster than best L/D");
        Assert.True(g2.SpeedToFlyMs <= g.BestLdMs, "tailwind: fly no faster than best L/D");
    }

    [Theory]
    [InlineData("c172-like.json")]
    [InlineData("glider-2-33-like.json")]
    public void GlideLessonFlownAtTheSpeedToFlyScoresNearFull(string file)
    {
        var sc = Run(file, PracticeKind.GlideSide, PracticeWind.Headwind, maxSec: 400);
        _out.WriteLine($"  achieved {sc.AchievedRatio:F1}:1 over the ground vs optimum {sc.SpeedToFlyGroundRatio:F1}:1, speed to fly {sc.SpeedToFlyMs * 1.944:F0} kt");
        Assert.Equal(PracticePhase.Finished, sc.Phase);
        Assert.True(sc.Score > 80, $"score {sc.Score:F0}");
    }

    [Fact]
    public void ClimbLessonsFinishAndScoreWithTheGamesOwnLaw()
    {
        var vy = Run("c172-like.json", PracticeKind.ClimbVyRear, PracticeWind.Calm, maxSec: 400);
        _out.WriteLine($"  Vy: {vy.ClimbTimeSec:F0} s for 1,000 ft, score {vy.Score:F0}");
        Assert.Equal(PracticePhase.Finished, vy.Phase); Assert.True(vy.Score > 70, $"Vy score {vy.Score:F0}");
        var vx = Run("c172-like.json", PracticeKind.ClimbVxSide, PracticeWind.Calm, maxSec: 400);
        _out.WriteLine($"  Vx: {vx.ClimbDistanceM:F0} m of ground for 1,000 ft, score {vx.Score:F0}");
        Assert.Equal(PracticePhase.Finished, vx.Phase); Assert.True(vx.Score > 70, $"Vx score {vx.Score:F0}");
    }

    [Fact]
    public void ClimbLevelDescendRunsAllThreeLegs()
    {
        var sc = Run("c172-like.json", PracticeKind.ClimbLevelDescend, PracticeWind.Calm, maxSec: 500);
        _out.WriteLine($"  legs done {sc.Leg}, score {sc.Score:F0}, in-band {sc.InBandFraction * 100:F0} %");
        Assert.Equal(PracticePhase.Finished, sc.Phase);
        Assert.Equal("back at the start altitude", sc.EndReason);
        Assert.True(sc.InBandFraction > 0.4, $"in band {sc.InBandFraction * 100:F0} %");
    }

    /// <summary>Where the climb performance comes from: thrust vs drag at Vy, with the drag split by source.</summary>
    [Theory]
    [InlineData("c172-like.json")]
    [InlineData("pa18-cub-like.json")]
    public void ClimbPerformanceBreakdown(string file)
    {
        var c = Load(file);
        var sc = new PracticeScenario(PracticeKind.ClimbVyRear, PracticeWind.Calm, c, Runway(), 0); sc.Spawn();
        double weight = c.Mass.MassKg * 9.81;
        var tables = Aircraft.BuildAirfoilTables(c);
        foreach (double v in new[] { sc.VyMs, 1.3 * sc.VsoMs, sc.CruiseMs })
        {
            var tr = TrimSolver.SolveGliderTrim(c, v, 500);
            var (F, _) = FlyingGame.Core.PropModel.Compute(c.Propulsion!, 1.0, new Vec3(v, 0, 0), Vec3.Zero, 1.2);
            double thrust = F.X;
            // drag split at the trimmed alpha
            var vel = new Vec3(v * Math.Cos(tr.AlphaRad), 0, v * Math.Sin(tr.AlphaRad));
            var flow = new FlyingGame.Core.Aero.StripFlowState(); flow.EnsureSize(64);
            FlyingGame.Core.Aero.ForceDebug.Samples = new System.Collections.Generic.List<FlyingGame.Core.Aero.ForceSample>();
            FlyingGame.Core.Aero.AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, 1.2, new FlyingGame.Core.Aero.ControlDeflections(0, tr.ElevatorRad, 0, 0), -1.0, flow);
            double dWing = 0, dTail = 0, dFus = 0, lift = 0;
            Vec3 flowDir = vel / vel.Length;
            foreach (var s in FlyingGame.Core.Aero.ForceDebug.Samples)
            {
                double along = -Vec3.Dot(s.ForceBody, flowDir);   // component against the flow = drag
                if (s.Kind == "drag") { if (s.PosBody.X < -2.5) dTail += along; else dWing += along; }
                else if (s.Kind == "lift") { lift += -s.ForceBody.Z; }
                else if (s.Kind == "fuselage") dFus += along;
            }
            FlyingGame.Core.Aero.ForceDebug.Samples = null;
            double dTot = weight / tr.GlideRatio;
            double roc = (thrust - dTot) * v / weight;
            _out.WriteLine($"{file} v {v * 1.944,4:F0} kt: L/D {tr.GlideRatio:F1}  drag {dTot:F0} N (wing {dWing:F0} + tail {dTail:F0} + fuselage {dFus:F0})  thrust {thrust:F0} N (eff·P/V = {c.Propulsion!.Efficiency * c.Propulsion.MaxPowerW / v:F0})  ROC {roc * 196.85:F0} fpm");
        }
    }

    [Fact]
    public void STurnsPracticeRollsItselfAtFortyFiveDegreesASecondReversingAtFortyFive()
    {
        double maxRate = 0, maxBank = 0;
        var sc = Run("c172-like.json", PracticeKind.STurns, PracticeWind.Calm, (s, auto) => { maxRate = Math.Max(maxRate, Math.Abs(s.RollRateDegS)); maxBank = Math.Max(maxBank, Math.Abs(s.BankDeg)); return new ControlInputs(0, 0, auto.Rudder, 0); }, maxSec: PracticeScenario.BriefingSec + 30);
        _out.WriteLine($"  reversals {sc.Reversals} in 30 s, peak roll rate {maxRate:F0}°/s, peak bank {maxBank:F0}°");
        Assert.True(sc.Reversals >= 6, $"only {sc.Reversals} reversals");
        Assert.InRange(maxRate, 35, 90);   // the game's aileron is rate-limited (1 s stop to stop)
        Assert.InRange(maxBank, 44, 62);
    }
}

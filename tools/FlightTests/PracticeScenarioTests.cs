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
public class PracticeScenarioTests
{
    private readonly ITestOutputHelper _out;
    private bool Trace;
    public PracticeScenarioTests(ITestOutputHelper o) { _out = o; }

    private static double Roll(Aircraft ac) { var q = ac.State.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.3; }
    private static double Pitch(Aircraft ac) { var q = ac.State.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3; }
    private static AircraftConfig Load(string f) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));
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
            user = userLaw != null ? userLaw(sc, sc.Autopilot) : sc.Autopilot;
            if (Trace && Math.Abs(t / 1.0 - Math.Round(t / 1.0)) < 1e-6) _out.WriteLine($"   t={t,4:F0} along {sc.AlongM,6:F0} agl {sc.AglM,5:F2} roll {Roll(ac),5:F1}° pitch {Pitch(ac),5:F1}° mains {sc.MainsAglM,5:F2} ias {sc.AirspeedMs,5:F1} align {sc.AlignmentDeg,6:F1}° off {sc.OffCentreM,6:F1} ail {inputs.Aileron,5:F2} ele {inputs.Elevator,5:F2} rud {inputs.Rudder,5:F2} lev {inputs.ThrottleLever,5:F2} gnd {sc.OnGround}");
        }
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
        Assert.True(sc.InBandFraction > (wind == PracticeWind.Shifting ? 0.15 : 0.5), $"the game's own law only held the band {sc.InBandFraction * 100:F0} % of the time");
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
    public void TraceDecathlonAileronAndC172Approach()
    {
        Trace = true;
        Run("pa18-cub-like.json", PracticeKind.CrosswindAileron, PracticeWind.Gusty, maxSec: 18);
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
        _out.WriteLine($"gusty: {min:F1}–{max:F1} m/s");
        Assert.InRange(max, 5.8, 6.2); Assert.InRange(min, 3.9, 4.1);
    }
}

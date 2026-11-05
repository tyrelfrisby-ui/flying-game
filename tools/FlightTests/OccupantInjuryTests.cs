using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 06:35: "Bailing out after a crash has the pilot surviving because the comment is only looking at his
/// impact and it is low." The crash's own verdict (Core OccupantInjury, fed by Sim Aircraft.Occupant) and how it combines
/// with the parachute landing's. Flat ground at 0 (WorldTerrain.Active = null). Expected numbers are literals worked by hand.
/// </summary>
[Collection("WorldTerrainActive")]
public class OccupantInjuryTests
{
    private const double G = 9.80665;
    private readonly ITestOutputHelper _o;
    public OccupantInjuryTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    /// <summary>Fly <paramref name="id"/> from <paramref name="h"/> m at <paramref name="v"/> m/s forward and
    /// <paramref name="sink"/> m/s down, pitched <paramref name="pitchDeg"/>, hands off, for 8 s over flat ground.</summary>
    private Aircraft Fly(string id, double v, double sink, double pitchDeg, double h, double seconds = 8)
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests();
        try
        {
            double th = pitchDeg * Math.PI / 180;
            var q = new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2));
            var ac = new Aircraft(Load(id), new RigidBodyState(new Vec3(0, 0, -h), q, q.Conjugate().Rotate(new Vec3(v, 0, sink)), Vec3.Zero), ControlDeflections.Neutral);
            for (double t = 0; t < seconds; t += 0.005) ac.Step(new ControlInputs(0, 0, 0, 1), 0.005);
            _o.WriteLine($"{id} {v} m/s, sink {sink}, pitch {pitchDeg}: {ac.Occupant.Describe()}; lost [{string.Join(",", ac.LostComponents)}]");
            return ac;
        }
        finally { WorldTerrain.Active = null; CrashEvents.ClearForTests(); }
    }

    // ---- the model itself ------------------------------------------------------------------------------------------

    [Fact]
    public void BrinkleyConstantsAreThePublishedOnes()
    {
        // Copied by hand: Brinkley's restrained limits (NASA-STD-3001) and the per-axis spring constants.
        Assert.Equal((62.8, 0.2), (OccupantInjury.OmegaX, OccupantInjury.ZetaX));
        Assert.Equal((58.0, 0.09), (OccupantInjury.OmegaY, OccupantInjury.ZetaY));
        Assert.Equal((52.9, 0.224), (OccupantInjury.OmegaZ, OccupantInjury.ZetaZ));
        var l = OccupantInjury.LowRisk; Assert.Equal((35.0, 28.0, 14.0, 14.0, 13.1, 13.4), (l.XPos, l.XNeg, l.YPos, l.YNeg, l.ZPos, l.ZNeg));
        var m = OccupantInjury.ModerateRisk; Assert.Equal((40.0, 35.0, 17.0, 17.0, 15.2, 16.5), (m.XPos, m.XNeg, m.YPos, m.YNeg, m.ZPos, m.ZNeg));
        var hi = OccupantInjury.HighRisk; Assert.Equal((46.0, 46.0, 22.0, 22.0, 18.0, 20.4), (hi.XPos, hi.XNeg, hi.YPos, hi.YNeg, hi.ZPos, hi.ZNeg));
        Assert.Equal(1.5, OccupantInjury.FatalBetaHigh);
    }

    /// <summary>Sitting at 1 g, then a 10 g step up the spine: a damped spring overshoots a step by e^(−ζπ/√(1−ζ²)).
    /// ζ 0.224: √(1 − 0.050176) = 0.974589, ζπ = 0.703717, ratio 0.722065, e^−0.722065 = 0.48574, so the peak is
    /// DRz = 1 + 10 × 1.48574 = 15.857.</summary>
    [Fact]
    public void AStepUpTheSpineOvershootsByTheBodysOwnDamping()
    {
        var o = new OccupantInjury();
        for (int i = 0; i < 200; i++) o.Step(new Vec3(0, 0, -G), 0.005);
        Assert.InRange(o.PeakDrZ, 0.99, 1.01);                         // settled: 1 g is DRz 1, not a blow
        for (int i = 0; i < 200; i++) o.Step(new Vec3(0, 0, -11 * G), 0.005);
        _o.WriteLine(o.Describe());
        Assert.InRange(o.PeakDrZ, 15.857 - 0.12, 15.857 + 0.12);
        // β high 15.857 / 18 = 0.881; moderate 15.857 / 15.2 = 1.043 → Injured, not dead.
        Assert.InRange(o.PeakBetaHigh, 0.874, 0.888);
        Assert.Equal(LandingOutcome.Injured, o.Level);
    }

    [Fact]
    public void GradesFromLoadsAndFromSpace()
    {
        Assert.Equal(LandingOutcome.Unhurt, OccupantInjury.FromLoads(0.99, 0.8, 0.7));
        Assert.Equal(LandingOutcome.Bruised, OccupantInjury.FromLoads(1.0, 0.99, 0.8));
        Assert.Equal(LandingOutcome.Injured, OccupantInjury.FromLoads(1.4, 1.0, 1.49));
        Assert.Equal(LandingOutcome.Dead, OccupantInjury.FromLoads(2.0, 1.8, 1.5));
        // Cabin crush: any → bruised, one stroke (0.15 m) → injured, the full geometric fold (0.30 m) → dead.
        Assert.Equal(LandingOutcome.Unhurt, OccupantInjury.FromCrush(0.0));
        Assert.Equal(LandingOutcome.Bruised, OccupantInjury.FromCrush(0.01));
        Assert.Equal(LandingOutcome.Injured, OccupantInjury.FromCrush(0.15));
        Assert.Equal(LandingOutcome.Dead, OccupantInjury.FromCrush(0.30));
    }

    /// <summary>A teleport, a Restart's pose or a replay written between steps is not something the seat did.</summary>
    [Fact]
    public void AStateWrittenBetweenStepsIsNotAnAcceleration()
    {
        WorldTerrain.Active = null; CrashEvents.ClearForTests();
        try
        {
            var ac = new Aircraft(Load("c172-like"), new RigidBodyState(new Vec3(0, 0, -500), Quat.Identity, new Vec3(55, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            for (int i = 0; i < 100; i++) ac.Step(new ControlInputs(0, 0, 0, 1), 0.005);
            ac.State = new RigidBodyState(new Vec3(0, 0, -800), Quat.Identity, Vec3.Zero, Vec3.Zero);   // 55 m/s gone in no time
            for (int i = 0; i < 10; i++) ac.Step(new ControlInputs(0, 0, 0, 1), 0.005);
            _o.WriteLine(ac.Occupant.Describe());
            Assert.Equal(LandingOutcome.Unhurt, ac.Occupant.Level);
        }
        finally { WorldTerrain.Active = null; CrashEvents.ClearForTests(); }
    }

    [Fact]
    public void FlyingAlongHurtsNobody()
    {
        Aircraft ac = Fly("c172-like", 60, 0, 0, 500, seconds: 10);
        Assert.Equal(LandingOutcome.Unhurt, ac.Occupant.Level);
        Assert.InRange(ac.Occupant.PeakSeatG, 0.5, 2.0);
    }

    // ---- crash, then bail out (the three cases in the brief) ------------------------------------------------------

    /// <summary>High-g crash then bail out: fatal, and he leaves as a ghost that rises, however gently he then "lands".</summary>
    [Theory]
    [InlineData("c172-like", 40, 15, -20)]       // 85 mph, 15 m/s down, nose 20° low
    [InlineData("pitts-s2b-like", 50, 35, -45)]  // a 45° dive into the ground
    public void AHighGCrashThenBailOutIsFatalAndTheGhostAscends(string id, double v, double sink, double pitch)
    {
        Aircraft ac = Fly(id, v, sink, pitch, 3);
        LandingOutcome crash = ac.Occupant.Level;
        Assert.Equal(LandingOutcome.Dead, crash);
        Assert.True(ac.Occupant.PeakBetaHigh >= OccupantInjury.FatalBetaHigh || ac.Occupant.CockpitCrushM >= FuselageStructure.MaxGeometricCrushM);
        LandingOutcome stepOff = ParachuteLanding.Outcome(1.0, water: false);   // stepping off a stopped wreck
        Assert.Equal(LandingOutcome.Unhurt, stepOff);
        Assert.Equal(LandingOutcome.Dead, PilotOutRules.Combined(crash, stepOff));
        Assert.True(PilotOutRules.GhostAscends(crash));
    }

    /// <summary>Gentle crash then bail out: the wreck hurt him (or not), he survives, no ghost.</summary>
    [Theory]
    [InlineData("c172-like", 25, 4, 0)]     // nose-gear first at 4 m/s: the gear and the prop go
    [InlineData("j3-cub-like", 20, 2, 0)]   // a wheel landing gone wrong: the prop strikes
    public void AGentleCrashThenBailOutSurvives(string id, double v, double sink, double pitch)
    {
        Aircraft ac = Fly(id, v, sink, pitch, 3);
        Assert.NotEmpty(ac.LostComponents);              // it WAS a crash
        LandingOutcome crash = ac.Occupant.Level;
        Assert.NotEqual(LandingOutcome.Dead, crash);
        Assert.False(PilotOutRules.GhostAscends(crash));
        LandingOutcome combined = PilotOutRules.Combined(crash, ParachuteLanding.Outcome(1.0, false));
        Assert.NotEqual(LandingOutcome.Dead, combined);
        Assert.Equal(crash, combined);                   // the crash's bruises are not forgotten either
    }

    /// <summary>A normal bail-out from a flying aeroplane: the crash verdict is Unhurt, so the outcome is the parachute
    /// landing's alone, on the same thresholds as before (6 / 9 / 14 m/s, water ÷ 1.6).</summary>
    [Fact]
    public void ANormalInFlightBailOutIsUnchanged()
    {
        Aircraft ac = Fly("c172-like", 60, 0, 0, 500, seconds: 5);
        LandingOutcome crash = ac.Occupant.Level;
        Assert.Equal(LandingOutcome.Unhurt, crash);
        Assert.False(PilotOutRules.GhostAscends(crash));
        (double ms, bool water, LandingOutcome want)[] cases =
        {
            (4.9, false, LandingOutcome.Unhurt), (6.0, false, LandingOutcome.Bruised), (8.9, false, LandingOutcome.Bruised),
            (9.0, false, LandingOutcome.Injured), (13.9, false, LandingOutcome.Injured), (14.0, false, LandingOutcome.Dead),
            (9.5, true, LandingOutcome.Unhurt), (14.4, true, LandingOutcome.Injured), (22.5, true, LandingOutcome.Dead),
        };
        foreach (var (ms, water, want) in cases)
            Assert.Equal(want, PilotOutRules.Combined(crash, ParachuteLanding.Outcome(ms, water)));
    }

    // ---- what the screen does with it ------------------------------------------------------------------------------

    [Fact]
    public void TheGhostsLineShowsAsHeRisesAndTheHudStaysOff()
    {
        Assert.False(PilotOutRules.ShowHud(PilotOutRules.Stage.Ascending));
        string line = PilotOutRules.HeavenLines[0];
        Assert.True(PilotOutRules.ShowRemark(PilotOutRules.Stage.Ascending, spoken: true, line));
        Assert.False(PilotOutRules.ShowRemark(PilotOutRules.Stage.Ascending, spoken: false, line));
        Assert.Equal("GONE WEST", PilotOutRules.GhostLabel);
        Assert.InRange(PilotOutRules.HeavenLines.Length, 2, 3);
        Assert.Contains(PilotOutRules.HeavenLines, l => l.Contains("Good Ship Lollipop"));
        Assert.All(PilotOutRules.HeavenLines, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.InRange(PilotOutRules.GhostLiftG, 1.01, 2.0);   // net upward: he rises
    }
}

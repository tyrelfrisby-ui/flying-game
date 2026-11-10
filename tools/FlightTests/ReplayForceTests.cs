using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Forces during a replay (Ty 2026-11-08: "Force vectors do not show even though they were on for the live flying").
/// A replay poses the aeroplane; it does not run the physics. <see cref="Aircraft.LastForces"/> therefore holds
/// whatever the last LIVE step worked out, so drawing it would hang one instant's forces on another instant's
/// attitude — right only on the frame the replay was entered on, and a convincing lie everywhere else. So the forces
/// are worked out again from the pose the recording gives, and these tests pin the two things that has to mean: the
/// answer follows the pose, and asking the question costs the live sim nothing.
/// </summary>
public class ReplayForceTests
{
    private static Aircraft Build(string id, Vec3 velBody, double altitudeM = 600.0)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -altitudeM), Quat.Identity, velBody, Vec3.Zero), ControlDeflections.Neutral);
    }

    /// <summary>The only public way to set the lever directly: the live path runs it through the shaped axis.</summary>
    private static void Throttle(Aircraft a, double throttle01) =>
        a.SetReplayPose(a.State, ControlDeflections.Neutral, throttle01, a.FlapFraction, a.GearExtension, a.LoadFactorZ);

    private static Vec3 Total(IEnumerable<ForceSample> samples) =>
        samples.Where(s => s.Kind != "gyro" && s.Kind != "moment" && s.Kind != "flow")
               .Aggregate(Vec3.Zero, (a, s) => a + s.ForceBody);

    private static double LiftMagnitude(IEnumerable<ForceSample> samples) =>
        samples.Where(s => s.Kind == "lift").Sum(s => s.ForceBody.Length);

    // ---- the answer follows the pose -------------------------------------------------------------

    [Fact]
    public void AWingIsLiftingWheneverItIsFlying_EvenThoughNothingHasStepped()
    {
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        List<ForceSample> f = a.ForcesAtCurrentState();
        Assert.NotEmpty(f);
        Assert.True(LiftMagnitude(f) > 0.0, "a wing at cruise should report lift");
        Assert.Contains(f, s => s.Kind == "weight");
    }

    [Fact]
    public void TheSameAEROPLANEInTwoDIFFERENTPosesGivesTwoDifferentAnswers()
    {
        // This is the defect itself: with the forces taken from the last live step, a replay scrubbed from cruise to a
        // dive showed the SAME arrows throughout. Asking at each pose must not.
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        double cruise = LiftMagnitude(a.ForcesAtCurrentState());

        a.SetReplayPose(new RigidBodyState(new Vec3(0, 0, -600), Quat.Identity, new Vec3(110, 0, 0), Vec3.Zero),
            ControlDeflections.Neutral, 0, 0, 1, 1);
        double fast = LiftMagnitude(a.ForcesAtCurrentState());

        Assert.True(fast > cruise * 1.5,
            $"twice the airspeed is ~4x the dynamic pressure, so the lift must grow: cruise {cruise:F0} N, fast {fast:F0} N");
    }

    [Fact]
    public void LiftGrowsWithTheSquareOfTheAirspeed()
    {
        Aircraft a = Build("c172-like", new Vec3(40, 0, 0));
        double slow = LiftMagnitude(a.ForcesAtCurrentState());
        a.SetReplayPose(new RigidBodyState(new Vec3(0, 0, -600), Quat.Identity, new Vec3(80, 0, 0), Vec3.Zero),
            ControlDeflections.Neutral, 0, 0, 1, 1);
        double fast = LiftMagnitude(a.ForcesAtCurrentState());
        double ratio = fast / slow;
        Assert.InRange(ratio, 3.0, 5.0);   // 4x at exactly constant alpha; the strips' own geometry moves it a little
    }

    [Fact]
    public void TheWeightIsTheAEROPLANESOwnWeightAndPointsDOWNInTheWorld()
    {
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        ForceSample w = a.ForcesAtCurrentState().Single(s => s.Kind == "weight");
        double expected = a.MassProperties.MassKg * Atmosphere.GravityMs2;
        Assert.Equal(expected, w.ForceBody.Length, expected * 1e-6);
        Assert.True(w.ForceBody.Z > 0, "level and upright, weight is +Z in body axes, which is down in NED");
    }

    [Fact]
    public void AskingTwiceAtTheSamePoseGivesTheSameAnswer()
    {
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        Vec3 first = Total(a.ForcesAtCurrentState());
        Vec3 second = Total(a.ForcesAtCurrentState());
        Assert.Equal(first.X, second.X, 1e-9);
        Assert.Equal(first.Y, second.Y, 1e-9);
        Assert.Equal(first.Z, second.Z, 1e-9);
    }

    [Fact]
    public void TheForcesFollowThePoseAndNotTheLastLIVEStep()
    {
        // The defect, written down. Fly the aeroplane somewhere with its own forces, keep what the last live step
        // computed, then pose it somewhere quite different — as a replay does — and ask again. The old overlay drew the
        // kept list, so the answer could not move; it must now.
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        a.CaptureForces = true;
        for (int i = 0; i < 60; i++) a.StepWithDeflectionTargets(ControlDeflections.Neutral, 1.0 / 120.0);
        double lastLiveLift = LiftMagnitude(a.LastForces);
        Assert.True(lastLiveLift > 0.0, "the live step should have captured some lift to begin with");

        // Hauled over into a near-vertical dive at twice the speed: nothing about the old answer could still be right.
        Quat noseDown = new(0, System.Math.Sin(-1.3 / 2), 0, System.Math.Cos(-1.3 / 2));
        a.SetReplayPose(new RigidBodyState(new Vec3(0, 0, -600), noseDown, new Vec3(115, 0, 0), Vec3.Zero),
            ControlDeflections.Neutral, 0, 0, 1, 1);
        double posed = LiftMagnitude(a.ForcesAtCurrentState());

        Assert.True(posed > lastLiveLift * 1.5,
            $"the recomputed lift must answer to the new pose, not the kept one: kept {lastLiveLift:F0} N, recomputed {posed:F0} N");
        Assert.Equal(lastLiveLift, LiftMagnitude(a.LastForces), 1e-9);   // and the kept list is still untouched
    }

    // ---- and it costs the live sim nothing -------------------------------------------------------

    [Fact]
    public void AskingDoesNotDisturbTheLiveSim()
    {
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        a.CaptureForces = true;
        for (int i = 0; i < 20; i++) a.StepWithDeflectionTargets(ControlDeflections.Neutral, 1.0 / 120.0);

        List<ForceSample> liveForces = a.LastForces;
        int liveCount = liveForces.Count;
        double loadFactor = a.LoadFactorZ, aeroLoadFactor = a.AeroLoadFactorZ;
        Vec3 specific = a.SpecificForceBody;
        double[] separation = (double[])a.FlowStateForTests.Separation.Clone();
        double[] lagCl = (double[])a.FlowStateForTests.LagCl.Clone();
        double eps = a.FlowStateForTests.DownwashEpsLagged;

        a.ForcesAtCurrentState();

        Assert.Same(liveForces, a.LastForces);
        Assert.Equal(liveCount, a.LastForces.Count);
        Assert.Equal(loadFactor, a.LoadFactorZ);
        Assert.Equal(aeroLoadFactor, a.AeroLoadFactorZ);
        Assert.Equal(specific.Z, a.SpecificForceBody.Z);
        Assert.Equal(separation, a.FlowStateForTests.Separation);
        Assert.Equal(lagCl, a.FlowStateForTests.LagCl);
        Assert.Equal(eps, a.FlowStateForTests.DownwashEpsLagged);
    }

    [Fact]
    public void AskingDoesNotChangeWhereTheAEROPLANEGoesNext()
    {
        // The strongest form of the promise above: two aeroplanes flown identically, one of them interrogated at every
        // step, must end up in the same place to the last bit.
        Aircraft clean = Build("c172-like", new Vec3(55, 0, 2));
        Aircraft asked = Build("c172-like", new Vec3(55, 0, 2));
        for (int i = 0; i < 240; i++)
        {
            clean.StepWithDeflectionTargets(ControlDeflections.Neutral, 1.0 / 120.0);
            asked.StepWithDeflectionTargets(ControlDeflections.Neutral, 1.0 / 120.0);
            asked.ForcesAtCurrentState();
        }
        Assert.Equal(clean.State.Position.X, asked.State.Position.X, 1e-9);
        Assert.Equal(clean.State.Position.Z, asked.State.Position.Z, 1e-9);
        Assert.Equal(clean.State.Velocity.X, asked.State.Velocity.X, 1e-9);
    }

    // ---- the engine, as flown rather than as it ended up -----------------------------------------

    [Fact]
    public void TheRecordingRemembersWhetherTheEngineWasMakingPower()
    {
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        Throttle(a, 0.8);
        ReplayFrame f = ReplayFrame.Capture(1.0, a, Vec3.Zero);
        Assert.True(f.EngineRunning);
        Assert.True(f.EngineRpm > 1.0);
    }

    [Fact]
    public void AReplayedFrameOfAFlyingAEROPLANEReportsARunningEngine()
    {
        // Ty 2026-11-08: "No engine sound on replay". The aeroplane had crashed, so the LIVE engine was stopped for the
        // whole playback — and the sound, the shaft power and the thrust all asked the live engine.
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        Throttle(a, 1.0);
        a.ReplayEngineRpm = 2400.0;
        a.ReplayEngineRunning = true;
        Assert.True(a.EngineRunning);
        Assert.True(a.ShaftPower01 > 0.9, "a replayed frame at full throttle is making power");
        Assert.True(Total(a.ForcesAtCurrentState().Where(s => s.Kind == "thrust")).Length > 0.0);
    }

    [Fact]
    public void AReplayedFrameWithAStoppedEngineMakesNoPower()
    {
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        Throttle(a, 1.0);
        a.ReplayEngineRpm = 0.0;
        a.ReplayEngineRunning = false;
        Assert.False(a.EngineRunning);
        Assert.Equal(0.0, a.ShaftPower01);
    }

    [Fact]
    public void LeavingTheReplayHandsTheEngineBackToThePhysics()
    {
        Aircraft a = Build("c172-like", new Vec3(55, 0, 0));
        Throttle(a, 1.0);
        a.ReplayEngineRunning = false;
        Assert.False(a.EngineRunning);
        a.ReplayEngineRunning = null;
        Assert.True(a.EngineRunning, "with the recording's answer withdrawn, the live engine speaks for itself again");
    }

    [Fact]
    public void AGliderHasNoEngineWhateverTheRecordingSays()
    {
        Aircraft a = Build("glider-2-33-like", new Vec3(30, 0, 1.5));
        a.ReplayEngineRunning = true;
        Assert.False(a.EngineRunning);
    }
}

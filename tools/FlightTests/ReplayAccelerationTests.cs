using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The acceleration a replay hands to the display (Ty 2026-11-08: "i am going down and the flight path vector is
/// going up"). The flight path vector predicts where the aeroplane will be in three seconds by carrying the velocity
/// forward under the acceleration, and it used to work that acceleration out by differencing what it saw frame to
/// frame. Inside a replay the world is frozen, so that difference is divided by a zero dt and never taken: the
/// prediction kept curving under whatever acceleration was last measured in live flight — usually the crash — and
/// pointed up out of a descent, or sprayed rays across the sky. The recording knows the answer, so it is asked.
/// </summary>
public class ReplayAccelerationTests
{
    private static RigidBodyState Flying(Vec3 velBody, Quat attitude) =>
        new(new Vec3(0, 0, -300), attitude, velBody, Vec3.Zero);

    private static ReplayFrame Frame(double t, Vec3 velBody, Quat attitude) =>
        new(t, Flying(velBody, attitude), default, 0, 0, 0, 1, Vec3.Zero, default, 2000.0);

    /// <summary>Rotation about the world's down axis (NED), i.e. a change of heading.</summary>
    private static Quat Yaw(double rad) => new(0, 0, System.Math.Sin(rad / 2), System.Math.Cos(rad / 2));

    private static FlightRecorder Recorded(params ReplayFrame[] frames)
    {
        var rec = new FlightRecorder(30.0, 600.0);
        foreach (ReplayFrame f in frames) rec.Record(f);
        return rec;
    }

    [Fact]
    public void TheAccelerationIsTheRecordingsOwnVelocityChangeOverItsOwnTime()
    {
        // Straight ahead, 10 m/s faster every second.
        FlightRecorder rec = Recorded(Frame(0.0, new Vec3(30, 0, 0), Quat.Identity),
                                      Frame(2.0, new Vec3(50, 0, 0), Quat.Identity));
        Vec3 a = rec.AccelerationAt(1.0);
        Assert.Equal(10.0, a.X, 6);
        Assert.Equal(0.0, a.Y, 6);
        Assert.Equal(0.0, a.Z, 6);
    }

    [Fact]
    public void AnAeroplanePullingOutOfADiveIsAcceleratingUPWARDS()
    {
        // NED: +Z is down. Going down at 20 m/s, then at 4 — the pull-out. The acceleration must come out NEGATIVE
        // in Z (upward); the frozen live value is what used to send the vector the other way.
        FlightRecorder rec = Recorded(Frame(0.0, new Vec3(50, 0, 20), Quat.Identity),
                                      Frame(1.0, new Vec3(50, 0, 4), Quat.Identity));
        Vec3 a = rec.AccelerationAt(0.5);
        Assert.True(a.Z < 0.0, $"expected an upward (negative-Z) acceleration, got {a.Z}");
        Assert.Equal(-16.0, a.Z, 6);
    }

    [Fact]
    public void AnAeroplaneHoldingASteadyTurnIsAcceleratingEvenThoughItsSpeedNeverChanges()
    {
        // The body velocity is identical in all three frames; only the heading moves. In world axes that IS an
        // acceleration — the centripetal one — and it is what bends the predicted path into an arc.
        var v = new Vec3(40, 0, 0);
        FlightRecorder rec = Recorded(Frame(0.0, v, Yaw(0.0)), Frame(1.0, v, Yaw(0.2)), Frame(2.0, v, Yaw(0.4)));
        Vec3 a = rec.AccelerationAt(1.0);
        Assert.True(a.Length > 1.0, $"a steady turn should show a centripetal acceleration, got {a.Length}");
    }

    [Fact]
    public void AskingAtTheVeryStartOrEndStillAnswersInsteadOfRunningOffTheRecording()
    {
        FlightRecorder rec = Recorded(Frame(0.0, new Vec3(30, 0, 0), Quat.Identity),
                                      Frame(1.0, new Vec3(40, 0, 0), Quat.Identity));
        Vec3 atStart = rec.AccelerationAt(rec.StartTime);
        Vec3 atEnd = rec.AccelerationAt(rec.EndTime);
        Assert.Equal(10.0, atStart.X, 6);   // one-sided, but the rate is the same
        Assert.Equal(10.0, atEnd.X, 6);
        Assert.Equal(10.0, rec.AccelerationAt(-5.0).X, 6);     // and before/after the recording it clamps
        Assert.Equal(10.0, rec.AccelerationAt(99.0).X, 6);
    }

    [Fact]
    public void ARecordingWithNothingToCompareReportsNoAccelerationRatherThanThrowing()
    {
        Assert.Equal(0.0, new FlightRecorder(30.0, 600.0).AccelerationAt(0.0).Length, 9);
        FlightRecorder one = Recorded(Frame(0.0, new Vec3(30, 0, 0), Quat.Identity));
        Assert.Equal(0.0, one.AccelerationAt(0.0).Length, 9);
    }

    [Fact]
    public void TheAnswerDependsOnTheMomentAskedAboutAndNotOnWhenOrHowOftenItIsAsked()
    {
        // Scrubbing is not a clock: asking twice, or asking out of order, must not change what the recording says.
        FlightRecorder rec = Recorded(Frame(0.0, new Vec3(30, 0, 0), Quat.Identity),
                                      Frame(1.0, new Vec3(30, 0, 10), Quat.Identity),
                                      Frame(2.0, new Vec3(30, 0, 0), Quat.Identity));
        Vec3 first = rec.AccelerationAt(0.5);
        Vec3 late = rec.AccelerationAt(1.5);
        Vec3 again = rec.AccelerationAt(0.5);
        Assert.Equal(first.X, again.X, 9);
        Assert.Equal(first.Y, again.Y, 9);
        Assert.Equal(first.Z, again.Z, 9);
        Assert.True(first.Z > 0 && late.Z < 0, "down then up: the two halves must differ in sign");
    }
}

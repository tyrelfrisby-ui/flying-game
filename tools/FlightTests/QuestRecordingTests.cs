using System;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Quest's "what I see" recorder (owner Ty, 2026-11-02: a controller shortcut to record without the Meta
/// menu): the A + B chord, the I420 packing the GPU writes and Java reads, the frame pacing, the names.
/// Expected numbers are literals worked by hand, not read back from QuestRecording.
/// </summary>
public sealed class QuestRecordingTests
{
    private const double Dt = 1.0 / 72.0;

    private static QuestFlightCommand Step(QuestControlMapper m, bool a, bool b, bool rightClick = false) =>
        m.Step(new QuestControllerFrame { A = a, B = b, RightStickClick = rightClick }, Dt, 0.5);

    [Fact]
    public void HoldingABTogetherForASecondFiresOnce()
    {
        var m = new QuestControlMapper();
        int fired = 0, firstAt = -1;
        for (int i = 0; i < 72 * 3; i++)
            if (Step(m, true, true).Record) { fired++; if (firstAt < 0) firstAt = i; }
        Assert.Equal(1, fired);
        Assert.InRange(firstAt, 70, 72);   // 1 s at 72 Hz, give or take the frame it lands on
    }

    [Fact]
    public void ACutShortChordDoesNothingAndLettingGoRearmsIt()
    {
        var m = new QuestControlMapper();
        for (int i = 0; i < 50; i++) Assert.False(Step(m, true, true).Record);   // 0.7 s
        Assert.False(Step(m, true, false).Record);                                 // B let go: the count restarts
        for (int i = 0; i < 50; i++) Assert.False(Step(m, true, true).Record);
        int fired = 0;
        for (int i = 0; i < 30; i++) if (Step(m, true, true).Record) fired++;
        Assert.Equal(1, fired);
        Step(m, false, false);
        fired = 0;
        for (int i = 0; i < 80; i++) if (Step(m, true, true).Record) fired++;
        Assert.Equal(1, fired);                                                    // and again after a release
    }

    [Fact]
    public void AOrBAloneIsTrimAndNeverRecords()
    {
        var m = new QuestControlMapper();
        for (int i = 0; i < 200; i++)
        {
            var c = Step(m, true, false);
            Assert.False(c.Record);
            Assert.Equal(-0.5, c.PitchTrimRate, 9);   // A = nose up
        }
        for (int i = 0; i < 200; i++) Assert.False(Step(m, false, true).Record);
    }

    [Fact]
    public void WhileTheChordIsHeldTheTrimDoesNotMove()
    {
        var m = new QuestControlMapper();
        for (int i = 0; i < 100; i++) Assert.Equal(0.0, Step(m, true, true).PitchTrimRate, 9);
    }

    [Fact]
    public void TheChordTouchesNoOtherButton()
    {
        var m = new QuestControlMapper();
        for (int i = 0; i < 100; i++)
        {
            var c = Step(m, true, true);
            Assert.False(c.Gear || c.Recentre || c.Menu || c.FlapsDown || c.FlapsUp || c.Smoke || c.Fire);
        }
    }

    [Fact]
    public void HandsDoNotRecordAndDoNotLeaveAHalfHeldChord()
    {
        var m = new QuestControlMapper();
        for (int i = 0; i < 60; i++) Step(m, true, true);   // 0.83 s
        m.Step(new QuestControllerFrame { HandsOnly = true }, Dt, 0.5);
        for (int i = 0; i < 20; i++) Assert.False(Step(m, true, true).Record);   // would have fired at 1 s
    }

    [Fact]
    public void TheI420FrameIsLumaThenQuarterSizeUThenV()
    {
        var l = QuestRecording.I420Layout(1280, 720);
        Assert.Equal(921_600, l.yBytes);
        Assert.Equal(921_600, l.uOffset);
        Assert.Equal(1_152_000, l.vOffset);
        Assert.Equal(1_382_400, l.total);
        Assert.Equal(1080, QuestRecording.I420Rows(720));
        Assert.Equal(1280 * 1080, l.total);   // the packed texture is exactly the frame
        Assert.Equal(1280, QuestRecording.Width);
        Assert.Equal(720, QuestRecording.Height);
    }

    [Theory]
    [InlineData(1280, 722)]
    [InlineData(1278, 720)]
    [InlineData(0, 720)]
    public void SidesThatCannotPackAreRefused(int w, int h) =>
        Assert.Throws<ArgumentException>(() => QuestRecording.I420Layout(w, h));

    [Fact]
    public void ThirtyFramesASecondFromA72HzLoop()
    {
        double next = 0;
        int n = 0;
        for (int i = 0; i < 72 * 10; i++) if (QuestRecording.Due(i / 72.0, ref next)) n++;
        Assert.InRange(n, 299, 301);
    }

    [Fact]
    public void AfterAStallThePacingRestartsInsteadOfBursting()
    {
        double next = 0;
        Assert.True(QuestRecording.Due(0.0, ref next));
        Assert.True(QuestRecording.Due(5.0, ref next));    // five seconds paused
        Assert.False(QuestRecording.Due(5.0 + 1.0 / 72, ref next));
        Assert.Equal(5.0 + 1.0 / 30, next, 9);
    }

    [Fact]
    public void NamesAndClock()
    {
        Assert.Equal("AeroQuest-20261102-093005.mp4", QuestRecording.FileName(new DateTime(2026, 11, 2, 9, 30, 5)));
        Assert.Equal("0:00", QuestRecording.Clock(-1));
        Assert.Equal("0:07", QuestRecording.Clock(7.9));
        Assert.Equal("12:34", QuestRecording.Clock(754));
        Assert.Equal("1:02:03", QuestRecording.Clock(3723));
        Assert.Equal(1800.0, QuestRecording.MaxSeconds);
    }
}

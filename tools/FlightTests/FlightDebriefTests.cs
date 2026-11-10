using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE DEBRIEF (Ty 2026-11-10). He was asked how far the instructor idea goes and chose three things — an instructor
/// in the back seat, scripted lessons with failures built in, and a debrief after the flight — and then chose the
/// debrief to build first, against this reasoning, which he accepted: "The replay already exists and is good.
/// Turning it into a debrief — what failed, when, what you did, how long you took to notice — is the smallest step
/// that is useful alone, and it is useful even solo."
///
/// So these tests are about four things, in order of how much they matter:
///  • the LATENCY: how long after the failure the pilot moved something, and when the reading is not a reaction time;
///  • the NARRATIVE: the right lines, in time order, with the right numbers in them;
///  • the DETECTION: that the event log sees what the aeroplane did;
///  • the no-position rule — a debrief must not become a ground track.
/// Every recording here is hand-built at 30 Hz, so none of this needs the aero model and none of it is slow.
/// </summary>
public class FlightDebriefTests
{
    private const double Hz = 30.0, Dt = 1.0 / Hz;

    /// <summary>
    /// What a frame looks like at one instant: level, heading north, at a height and an INDICATED airspeed. Indicated,
    /// because every number the debrief prints and every limit it compares against is indicated — so a test that
    /// wrote true airspeed would be asking for one speed and asserting another.
    /// </summary>
    private static ReplayFrame Frame(double t, double altM, double iasMs, double throttle, double elevatorRad = 0,
        double rudderRad = 0, double yawRate = 0, double g = 1.0, double rpm = 2300, bool running = true)
    {
        double tasMs = iasMs / Math.Sqrt(Atmosphere.DensityAtAltitude(altM) / Atmosphere.SeaLevelDensityKgM3);
        var s = new RigidBodyState(new Vec3(0, 0, -altM), new Quat(0, 0, 0, 1), new Vec3(tasMs, 0, 0),
            new Vec3(0, 0, yawRate));
        var d = new ControlDeflections(0, elevatorRad, rudderRad, 0);
        return new ReplayFrame(t, s, d, throttle, 0, 1, g, Vec3.Zero, default, rpm, running);
    }

    /// <summary>
    /// TY'S OWN EXAMPLE OF THE FEATURE, flown as a recording: "The engine quit at 4:12; the throttle came back at
    /// 4:19; best-glide speed was reached at 4:31. Seven seconds to notice."
    /// The hand stays still through the failure, so the seven seconds is a clean reaction time.
    /// </summary>
    private static (FlightRecorder rec, FlightEventLog log) TysEngineFailure()
    {
        var rec = new FlightRecorder(Hz, 600.0);
        var log = new FlightEventLog();
        const double quit = 252.0;          // 4:12
        const double hand = 259.0;          // 4:19 — seven seconds later
        const double onGlide = 271.0;       // 4:31
        for (double t = 240.0; t <= 290.0; t += Dt)
        {
            bool dead = t >= quit;
            double thr = t < hand ? 0.75 : 0.0;
            // Cruising at 44 kt-equivalent (m/s here); from the moment he lowers the nose the speed decays towards
            // best glide (26 m/s) the way an aeroplane's does — first order, not a ramp. The time constant is chosen
            // so the needle is inside the debrief's ±3 kt band at 4:31, which is the moment in his example.
            const double Tau = 12.0 / 2.456;
            double ias = t < hand ? 44.0 : 26.0 + 18.0 * Math.Exp(-(t - hand) / Tau);
            double alt = dead ? 900.0 - 3.0 * (t - quit) : 900.0;
            rec.Record(Frame(t, alt, ias, thr, rpm: dead ? 600 : 2300, running: !dead));
        }
        log.Note(new FlightEvent(quit, FlightEventKind.EngineStopped, 2300, 0, 0, "fuel starvation"));
        return (rec, log);
    }

    private static DebriefLimits Limits(double bestGlideMs = 0.0) => new DebriefLimits
    {
        GLimitPos = 6.0, GLimitNeg = -3.0, RedlineRpm = 2700, TypeName = "Pitts S-1S", BestGlideIasMs = bestGlideMs,
    };

    // ---- the latency: the heart of the thing ---------------------------------------------------------------------

    [Fact]
    public void SevenSecondsToTheThrottleIsTheReactionTime()
    {
        (FlightRecorder rec, FlightEventLog _) = TysEngineFailure();
        Assert.True(FlightResponse.TryReaction(rec, 252.0, out PilotReaction r));
        Assert.Equal(PilotControl.Throttle, r.Control);
        Assert.Equal(7.0, r.LatencySec, 1);
        Assert.True(r.Clean);                                      // his hand was still when it quit
        Assert.True(r.AltitudeLostM > 15.0);                       // 7 s at 3 m/s: the wait cost height
    }

    [Fact]
    public void TheMomentReportedIsWhenTheControlMovedNotWhenTheMovementWasConfirmed()
    {
        // The sustain window exists to throw out a wobble, but the time it reports must be the movement's own: a
        // reaction time that was a third of a second late every time would be a third of a second of fiction.
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 20.0; t += Dt) rec.Record(Frame(t, 500, 40, t < 10.0 ? 0.8 : 0.1));
        Assert.True(FlightResponse.TryReaction(rec, 5.0, out PilotReaction r));
        Assert.Equal(10.0, r.T, 1);
        Assert.Equal(5.0, r.LatencySec, 1);
    }

    [Fact]
    public void OneFrameOfWobbleIsNotADecision()
    {
        // A single sample past the dead band — a touch-screen slip, a noisy frame — followed by the real movement
        // four seconds later. The real one is the answer.
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 20.0; t += Dt)
        {
            bool blip = t >= 6.0 && t < 6.0 + Dt * 1.5;
            double thr = blip ? 0.2 : t < 10.0 ? 0.8 : 0.1;
            rec.Record(Frame(t, 500, 40, thr));
        }
        Assert.True(FlightResponse.TryReaction(rec, 5.0, out PilotReaction r));
        Assert.Equal(10.0, r.T, 1);
    }

    [Fact]
    public void AHandAlreadyOnTheStickIsNotACleanReactionTime()
    {
        // He was rolling when it quit. The stick crosses the dead band almost at once, so the naive answer would be
        // "0.2 seconds" — the flattering, useless number this is built to refuse.
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 20.0; t += Dt)
            rec.Record(Frame(t, 500, 40, 0.75, elevatorRad: 0.25 * Math.Sin(t * 2.0)));
        Assert.True(FlightResponse.TryReaction(rec, 10.0, out PilotReaction r));
        Assert.False(r.Clean);

        Debrief d = FlightDebrief.Build(rec, Log(new FlightEvent(10.0, FlightEventKind.EngineStopped, 2300)), Limits());
        DebriefLine did = Find(d, "WHAT YOU DID");
        Assert.Contains("not a reaction time", did.Text);
    }

    [Fact]
    public void ANeverSettledGlideSaysWhatHeHeldInstead()
    {
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 60.0; t += Dt) rec.Record(Frame(t, 900, t < 5 ? 44 : 34, t < 5 ? 0.8 : 0.0));
        // Best glide 26 m/s; he sat at 34 m/s the whole way down.
        Assert.False(FlightResponse.TrySettledOnSpeed(rec, 5.0, 26.0, FlightDebrief.GlideTolMs,
            FlightDebrief.GlideDwellSec, out _, out double closest));
        Assert.Equal(34.0, closest, 2);

        Debrief d = FlightDebrief.Build(rec, Log(new FlightEvent(5.0, FlightEventKind.EngineStopped, 2300)),
            Limits(bestGlideMs: 26.0));
        Assert.Contains("never settled on best glide", Find(d, "WHAT YOU DID").Text);
    }

    [Fact]
    public void NothingMovedAtAllIsItsOwnFinding()
    {
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 60.0; t += Dt) rec.Record(Frame(t, 900, 40, 0.75));
        Assert.False(FlightResponse.TryReaction(rec, 5.0, out _));
        Debrief d = FlightDebrief.Build(rec, Log(new FlightEvent(5.0, FlightEventKind.EngineStopped, 2300)), Limits());
        Assert.Contains("moved nothing", Find(d, "WHAT YOU DID").Text);
        Assert.Equal(DebriefTone.Serious, Find(d, "WHAT YOU DID").Tone);
    }

    // ---- the narrative ------------------------------------------------------------------------------------------

    [Fact]
    public void TheEngineFailureReadsAsAnInstructorWouldSayIt()
    {
        (FlightRecorder rec, FlightEventLog log) = TysEngineFailure();
        Debrief d = FlightDebrief.Build(rec, log, Limits(bestGlideMs: 26.0));

        Assert.Equal("Pitts S-1S · 4:50", d.Headline);
        Assert.Contains("one engine failure", d.Contents);

        DebriefLine quit = Find(d, "ENGINE STOPPED");
        Assert.StartsWith("4:12 — the engine stopped", quit.Text);
        Assert.Contains("Fuel starvation.", quit.Text);          // it could say why, so it does

        DebriefLine did = Find(d, "WHAT YOU DID");
        Assert.Contains("the throttle, 7 s after it quit", did.Text);
        Assert.Contains("of height went by while you waited", did.Text);
        Assert.Contains("you were on it at 4:31", did.Text);
        Assert.DoesNotContain("not a reaction time", did.Text);

        // And the one thing to talk about is the seven seconds, pointing at the line that explains it.
        Assert.Contains("7 s from the engine quitting to your hand on the throttle", d.Talk);
        Assert.Equal("WHAT YOU DID", d.Lines[d.TalkLine].Title);

        // Every line is in time order, and the summary comes last.
        for (int i = 1; i < d.Lines.Count; i++)
            if (d.Lines[i].Timed && d.Lines[i - 1].Timed) Assert.True(d.Lines[i].T >= d.Lines[i - 1].T);
        Assert.Equal("THE NUMBERS", d.Lines[^1].Title);
        Assert.False(d.Lines[^1].Timed);
    }

    [Fact]
    public void ACauseItCannotSeeIsNotInvented()
    {
        (FlightRecorder rec, FlightEventLog _) = TysEngineFailure();
        Debrief d = FlightDebrief.Build(rec, Log(new FlightEvent(252.0, FlightEventKind.EngineStopped, 2300)),
            Limits());
        Assert.Contains("Nothing in the recording says why.", Find(d, "ENGINE STOPPED").Text);
    }

    [Fact]
    public void EveryLineCanBeWatched_AndOneOlderThanTheRecordingSaysSo()
    {
        // The ring holds a minute here; the bent wingtip happened at 0:10 and has fallen out of it. The debrief must
        // still TELL him, and must not offer to show him something it no longer has.
        var rec = new FlightRecorder(Hz, 60.0);
        for (double t = 0; t <= 200.0; t += Dt) rec.Record(Frame(t, 500, 40, 0.6));
        var log = new FlightEventLog();
        log.Note(new FlightEvent(10.0, FlightEventKind.PanelBent, AirframeComponent.WingTipLowerRight, 9.0));
        log.Note(new FlightEvent(180.0, FlightEventKind.EngineStopped, 2300));
        Debrief d = FlightDebrief.Build(rec, log, Limits());

        DebriefLine bent = Find(d, "BENT: RIGHT LOWER WINGTIP PANEL");
        Assert.False(bent.CanCue);
        DebriefLine quit = Find(d, "ENGINE STOPPED");
        Assert.True(quit.CanCue);
        Assert.Equal(180.0 - FlightDebrief.CueLeadSec, quit.CueT, 3);
        Assert.True(d.Partial);
        Assert.Contains("the recorder keeps no more", d.Lines[^1].Text);
    }

    [Fact]
    public void AFlightWhereNothingHappenedSaysSoAndStops()
    {
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 300.0; t += Dt) rec.Record(Frame(t, 700, 40, 0.6));
        Debrief d = FlightDebrief.Build(rec, new FlightEventLog(), Limits());
        Assert.True(d.Quiet);
        Assert.Equal("", d.Talk);
        Assert.Equal("", d.Contents);
        Assert.Equal(2, d.Lines.Count);                            // the "nothing to debrief" line and the numbers
        Assert.Contains("nothing failed, nothing bent", d.Lines[0].Text);
    }

    [Fact]
    public void AStallIsMeasuredInHeightNotInReactionTime()
    {
        // A pilot at the stall is already holding the stick back: that is how he got there. So the debrief reports
        // what the recovery cost in height, which is also the ACS's measure (LessonJudge.Std.StallLoss).
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 40.0; t += Dt)
        {
            double alt = t < 10 ? 1000 : t < 20 ? 1000 - 9.0 * (t - 10) : 910;
            rec.Record(Frame(t, alt, 25, 0.2, elevatorRad: -0.2));
        }
        var log = new FlightEventLog();
        log.Note(new FlightEvent(10.0, FlightEventKind.StallBegan, 0.5));
        log.Note(new FlightEvent(20.0, FlightEventKind.StallEnded, 0.1));
        Debrief d = FlightDebrief.Build(rec, log, Limits());
        Assert.Contains("it stalled", Find(d, "STALL").Text);
        DebriefLine rec2 = Find(d, "RECOVERY");
        Assert.Contains("300 ft", rec2.Text);                      // 90 m of it, to the nearest ten feet
        Assert.Contains("the standard is 100 ft", rec2.Text);
        // and no latency anywhere in the stall's own lines
        Assert.DoesNotContain("reaction", rec2.Text);
    }

    [Fact]
    public void ProSpinRudderIsNamedForWhatItIs()
    {
        // Yawing right; the pilot feeds RIGHT rudder. The sim's convention is + rudder = nose right, so that is
        // rudder with the rotation, which holds the spin in — the classic error, and one the recording can see
        // without guessing at anything.
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 40.0; t += Dt)
            rec.Record(Frame(t, 1200 - 20.0 * Math.Max(0, t - 10), 30, 0.1,
                rudderRad: t < 14.0 ? 0.0 : 0.35, yawRate: t >= 10.0 ? 1.2 : 0.0));
        var log = new FlightEventLog();
        log.Note(new FlightEvent(10.0, FlightEventKind.SpinBegan, 1.2 * FlightReading.R2D));
        Debrief d = FlightDebrief.Build(rec, log, Limits());
        Assert.Equal("SPIN TO THE RIGHT", Find(d, "SPIN TO THE RIGHT").Title);
        DebriefLine rud = Find(d, "RUDDER");
        Assert.Contains("WITH the rotation", rud.Text);
        Assert.Contains("pro-spin rudder", rud.Text);
        Assert.Contains("pro-spin rudder", d.Talk);
    }

    [Fact]
    public void TouchdownIsGradedAgainstTheSameStandardTheLessonsUse()
    {
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 20.0; t += Dt) rec.Record(Frame(t, 100, 25, 0.1));
        // 2.0 m/s of sink is 394 fpm: Std.TdSink calls anything over 250 fpm "orange" and ~400 a hard landing.
        var log = new FlightEventLog();
        log.Note(new FlightEvent(10.0, FlightEventKind.Touchdown, 2.0, 3.0, 6.0));
        Debrief d = FlightDebrief.Build(rec, log, Limits());
        DebriefLine td = Find(d, "TOUCHDOWN");
        Assert.Contains("390 fpm", td.Text);
        Assert.Contains("3 ° of crab", td.Text);
        Assert.Contains("Well outside the standard", td.Text);
        Assert.Contains("≤ 150 fpm", td.Text);                     // LessonJudge's own ideal, not a second copy
    }

    // ---- the no-position rule ------------------------------------------------------------------------------------

    [Fact]
    public void NoPositionInDebrief()
    {
        // TY'S STANDING RULE (2026-10-18): a chart never shows your own position, and a debrief that drew your ground
        // track would be exactly that with a clock on it. Rather than hunting for coordinates in the strings, this
        // proves the stronger thing: the debrief CANNOT depend on where the flight was. The same flight is recorded
        // twice, once at the origin and once eleven kilometres away, and the two debriefs must be the same text.
        string Build(double x, double y)
        {
            var rec = new FlightRecorder(Hz, 600.0);
            for (double t = 0; t <= 60.0; t += Dt)
            {
                ReplayFrame f = Frame(t, 800, t < 20 ? 44 : 30, t < 20 ? 0.8 : 0.0);
                var s = new RigidBodyState(new Vec3(x + 40.0 * t, y, f.State.Position.Z), f.State.Attitude,
                    f.State.Velocity, f.State.Rates);
                rec.Record(new ReplayFrame(f.Time, s, f.Deflections, f.Throttle01, f.FlapFraction, f.GearExtension,
                    f.LoadFactorZ, f.WindWorld, f.Smoke, f.EngineRpm, f.EngineRunning));
            }
            var log = new FlightEventLog();
            log.Note(new FlightEvent(20.0, FlightEventKind.EngineStopped, 2300, 0, 0, "fuel starvation"));
            log.Note(new FlightEvent(35.0, FlightEventKind.PanelBent, AirframeComponent.WingTipRight, 7.0));
            log.Note(new FlightEvent(50.0, FlightEventKind.Touchdown, 1.0, 2.0, 5.0));
            Debrief d = FlightDebrief.Build(rec, log, Limits(26.0));
            var sb = new System.Text.StringBuilder(d.Headline).Append('|').Append(d.Contents).Append('|').Append(d.Talk);
            foreach (DebriefLine l in d.Lines) sb.Append('|').Append(l.Title).Append('|').Append(l.Text);
            return sb.ToString();
        }
        Assert.Equal(Build(0, 0), Build(11000, -7250));
    }

    // ---- detection: the log sees what the aeroplane did -----------------------------------------------------------

    private static AircraftConfig CubConfig() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    private static Aircraft Parked()
    {
        AircraftConfig cfg = CubConfig();
        var state = new RigidBodyState(new Vec3(0, 0, -600), new Quat(0, 0, 0, 1),
            new Vec3(cfg.SpawnIasMs, 0, 0), Vec3.Zero);
        return new Aircraft(cfg, state, ControlDeflections.Neutral);
    }

    [Fact]
    public void TheEngineStoppingIsLoggedWithWhateverCauseTheSimCanSee()
    {
        Aircraft a = Parked();
        var log = new FlightEventLog();
        log.Record(0.0, a);

        // Both magnetos off — which is exactly what an instructor's switchboard will do, and the engine stops on the
        // same frame because nothing has to run dry first.
        double turning = a.EngineRpm;
        Assert.True(turning > 100);
        a.MagLeft = a.MagRight = false;
        log.Record(1.0, a);
        Assert.Equal(FlightEventKind.EngineStopped, log.At(0).Kind);
        Assert.Equal(1.0, log.At(0).T, 6);
        Assert.Equal("both magnetos are off", log.At(0).Detail);
        // The rpm of a stop is the one it was TURNING AT, not the dead one it is reading on the frame the stop is
        // seen — otherwise the debrief says "the engine stopped, turning 410 rpm".
        Assert.Equal(turning, log.At(0).A, 3);
        Assert.NotEqual(a.EngineRpm, log.At(0).A);

        a.MagLeft = true;
        log.Record(2.0, a);
        Assert.Equal(FlightEventKind.EngineStarted, log.At(1).Kind);

        // AND THE CASE THAT MATTERS MOST: an engine that stops for a reason the sim cannot name leaves the cause
        // EMPTY, so the debrief says "nothing in the recording says why" instead of picking the likeliest one.
        a.ReplayEngineRunning = false;
        log.Record(3.0, a);
        Assert.Equal(FlightEventKind.EngineStopped, log.At(2).Kind);
        Assert.Equal("", log.At(2).Detail);
    }

    [Fact]
    public void AnAeroplaneThatSpawnsDamagedDoesNotReportItAsSomethingThatJustHappened()
    {
        Aircraft a = Parked();
        a.LoseComponent(AirframeComponent.WingTipRight);
        var log = new FlightEventLog();
        log.Record(0.0, a);                                        // the first call is the baseline
        log.Record(1.0, a);
        Assert.Equal(0, log.Count);
        a.LoseComponent(AirframeComponent.AileronLeft);
        log.Record(2.0, a);
        Assert.Equal(1, log.Count);
        Assert.Equal(AirframeComponent.AileronLeft, log.At(0).Component);
    }

    [Fact]
    public void AnOverGBooksThisPullsOwnPeakAndWhatItCostTheWing()
    {
        // Drive one wing's overstress member straight (its own public Step), so this costs nothing and measures
        // exactly what the structure booked. Up to 1.8 × limit and back inside: it yields, keeps a set, and loses
        // strength.
        Aircraft a = Parked();
        OverstressMember m = a.Structure.RightWing.Member;
        var log = new FlightEventLog();
        double t = 0;
        void Hold(double ratio, double secs)
        {
            for (double k = 0; k < secs; k += Dt) { m.Step(ratio, Dt); t += Dt; log.Record(t, a); }
        }
        Hold(1.0, 0.5);
        Hold(1.8, 1.0);
        Hold(0.8, 1.0);

        FlightEvent e = Assert.Single(log.Events);
        Assert.Equal(FlightEventKind.Overstress, e.Kind);
        Assert.Equal("right wing", e.Detail);
        Assert.True(e.A > 1.7 && e.A <= 1.81, $"the pull's own peak, got {e.A}");
        Assert.Equal(OverstressMember.WeakeningFor(1.8), e.B, 2);  // 6 % at 1.8
        Assert.Equal(m.SetRad * FlightReading.R2D, e.C, 3);
        Assert.False(e.Flag);                                      // a first bend is not a straightening
    }

    [Fact]
    public void StraighteningTheWingIsCalledOutAndCostsFourTimes()
    {
        // TY, 2026-11-10: "a bend that reduces bend angle like a hard push after bending wings up releases the travel
        // limit but is accounted for in fatigue and has a quadruple reduction in fatigue life so you can straighten
        // your wings out once but it will cost you significantly for the next stress."
        Aircraft a = Parked();
        OverstressMember m = a.Structure.RightWing.Member;
        var log = new FlightEventLog();
        double t = 0;
        void Hold(double ratio, double secs)
        {
            for (double k = 0; k < secs; k += Dt) { m.Step(ratio, Dt); t += Dt; log.Record(t, a); }
        }
        Hold(0.8, 0.5);
        Hold(1.8, 1.0); Hold(0.8, 0.5);          // bend it up
        Hold(-1.8, 1.0); Hold(0.8, 0.5);         // and push it back the other way

        Assert.Equal(2, log.Count);
        FlightEvent up = log.At(0), back = log.At(1);
        Assert.False(up.Flag);
        Assert.True(back.Flag, "the second excursion took the set out");
        Assert.True(back.A < 0, "the push is signed the other way");
        // Four times — but four times ITS OWN weakening, which is larger than the first pull's: the same 1.8 ratio
        // is a bigger demand on a wing that has already lost strength, which is the compounding the model is for.
        Assert.Equal(OverstressMember.WeakeningFor(Math.Abs(back.A)) * OverstressMember.ReversalFatigueFactor,
            back.B, 2);
        Assert.True(back.B > up.B * OverstressMember.ReversalFatigueFactor);
        Assert.True(Math.Abs(back.C) < Math.Abs(up.C), "the set came out");

        var rec = new FlightRecorder(Hz, 600.0);
        for (double k = 0; k <= t; k += Dt) rec.Record(Frame(k, 900, 50, 0.9, g: k < 2.0 ? 7.6 : -4.2));
        Debrief d = FlightDebrief.Build(rec, log, Limits());
        DebriefLine line = d.Lines[1];
        Assert.Equal("STRAIGHTENED THE RIGHT WING", line.Title);
        Assert.Contains("four times what bending it further would", line.Text);
        Assert.Contains("you can straighten your wings out once", line.Text);
        Assert.Contains("straightened the right wing out", d.Talk);
    }

    [Fact]
    public void PastVneIsSeenAndReportedWithThePeakItReached()
    {
        Aircraft a = Parked();
        var log = new FlightEventLog();
        log.Record(0.0, a);
        double vne = a.Config.Limits.VneAt(600.0);
        // Vne is an INDICATED speed, so the true speed that breaks it at 600 m is higher.
        double tasOver = (vne + 6.0) / Math.Sqrt(Atmosphere.DensityAtAltitude(600.0) / Atmosphere.SeaLevelDensityKgM3);
        a.State = new RigidBodyState(a.State.Position, a.State.Attitude, new Vec3(tasOver, 0, 0), Vec3.Zero);
        log.Record(1.0, a);
        a.State = new RigidBodyState(a.State.Position, a.State.Attitude, new Vec3(a.Config.SpawnIasMs, 0, 0), Vec3.Zero);
        log.Record(2.0, a);

        Assert.Equal(FlightEventKind.OverspeedBegan, log.At(0).Kind);
        Assert.Equal(vne, log.At(0).A, 3);
        Assert.Equal(FlightEventKind.OverspeedEnded, log.At(1).Kind);
    }

    [Fact]
    public void ConditionsThatNeverClosedAreSaidToBeStillRunning()
    {
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 30.0; t += Dt) rec.Record(Frame(t, 900 - 5.0 * t, 28, 0.1, yawRate: 1.0));
        var log = new FlightEventLog();
        log.Note(new FlightEvent(10.0, FlightEventKind.SpinBegan, 57.0));
        Debrief d = FlightDebrief.Build(rec, log, Limits());
        Assert.Contains("still turning when the recording ends", Find(d, "SPIN TO THE RIGHT").Text);
    }

    // ---- the future the debrief is meant to carry ----------------------------------------------------------------

    [Fact]
    public void AnInjectedFailureIsTimedExactlyAsARealOneIs()
    {
        // Ty picked the debrief first of the three because the other two build on it. This is that claim, tested: a
        // failure an instructor or a lesson script puts in is one Note() call, and every measurement works unchanged.
        var rec = new FlightRecorder(Hz, 600.0);
        for (double t = 0; t <= 40.0; t += Dt) rec.Record(Frame(t, 900, 40, t < 14.5 ? 0.8 : 0.1));
        var log = new FlightEventLog();
        log.Note(new FlightEvent(10.0, FlightEventKind.InjectedFailure, 0, 0, 0,
            "your instructor pulled the throttle on you"));
        Debrief d = FlightDebrief.Build(rec, log, Limits());
        Assert.Contains("your instructor pulled the throttle on you", Find(d, "FAILURE").Text);
        Assert.Contains("the throttle, 4.5 s after it happened", Find(d, "WHAT YOU DID").Text);
    }

    [Fact]
    public void NoteNeverLetsTheTimelineRunBackwards()
    {
        var log = new FlightEventLog();
        log.Note(new FlightEvent(10.0, FlightEventKind.EngineStopped));
        log.Note(new FlightEvent(4.0, FlightEventKind.Crash, 0, 0, 0, "the water"));
        Assert.Equal(10.0, log.At(1).T, 6);
        Assert.Equal("the water", log.At(1).Detail);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private static FlightEventLog Log(params FlightEvent[] events)
    {
        var log = new FlightEventLog();
        foreach (FlightEvent e in events) log.Note(e);
        return log;
    }

    private static DebriefLine Find(Debrief d, string title)
    {
        foreach (DebriefLine l in d.Lines) if (l.Title == title) return l;
        throw new Xunit.Sdk.XunitException($"no \"{title}\" line; got: "
            + string.Join(" / ", d.Lines.ConvertAll(l => l.Title)));
    }
}

using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Practice;

namespace FlyingGame.Sim.Replay;

/// <summary>How hard a debrief line leans on the pilot. The renderer colours by it; nothing is scored.</summary>
public enum DebriefTone { Note, Caution, Serious }

/// <summary>
/// One thing the instructor says, in order. A line is a short <see cref="Title"/> for the list and a
/// <see cref="Text"/> that is a finished English sentence — the Unity side prints it and does not compose anything,
/// so the Quest, the Mac and the phone all say the same words and the suite tests the words that ship.
/// </summary>
public readonly struct DebriefLine
{
    /// <summary>Recorder-clock time, s.</summary>
    public readonly double T;
    /// <summary>This line is about a MOMENT (it opens with a clock). False for the summary lines.</summary>
    public readonly bool Timed;
    public readonly DebriefTone Tone;
    /// <summary>Four or five words for the list ("ENGINE STOPPED", "OVER-G: RIGHT WING").</summary>
    public readonly string Title;
    /// <summary>What the instructor says. Already a sentence; already has its times and units in it.</summary>
    public readonly string Text;
    /// <summary>Where to put the replay's play head to watch it — a few seconds before, so the pilot sees it
    /// happen.</summary>
    public readonly double CueT;
    /// <summary>The recording still holds that moment. False for something that happened before the rolling
    /// ten minutes the recorder keeps: the debrief still TELLS him, it just cannot show him.</summary>
    public readonly bool CanCue;

    public DebriefLine(double t, bool timed, DebriefTone tone, string title, string text, double cueT, bool canCue)
    {
        T = t; Timed = timed; Tone = tone; Title = title; Text = text; CueT = cueT; CanCue = canCue;
    }
}

/// <summary>
/// The numbers the debrief compares against, handed in rather than derived, so it never guesses and never pays for a
/// trim sweep it did not need. ANY FIELD LEFT AT ZERO IS UNKNOWN, and the debrief then says nothing about it at all
/// (Ty 2026-11-10: "where you cannot [say why], do not guess — a debrief that invents a cause is worse than one that
/// lists facts").
/// </summary>
public sealed class DebriefLimits
{
    /// <summary>The structure's rated limit load factors (StructuralState.LimitPosG / LimitNegG): what it carries
    /// without taking a set.</summary>
    public double GLimitPos, GLimitNeg;
    /// <summary>The propeller's red line, rpm.</summary>
    public double RedlineRpm;
    /// <summary>Best-glide indicated airspeed, m/s — the speed the debrief times an engine failure against. 0 = not
    /// known here, and then the debrief says only when the pilot moved something.</summary>
    public double BestGlideIasMs;
    /// <summary>The type, as the headline names it.</summary>
    public string TypeName = "";
    /// <summary>Ground height at a world point, m. Null = the debrief quotes heights above SEA LEVEL instead of
    /// above the ground. It is a delegate and not a world so the suite can test both without installing terrain.</summary>
    public System.Func<double, double, double>? Ground;

    /// <summary>
    /// The limits of a live aeroplane. <paramref name="bestGlideIasMs"/> is the caller's because finding it costs a
    /// sweep of trim solutions (ApproachSpawn.FindBestGlide, cached per type and per 100 m): worth paying once when
    /// the pilot opens a debrief after an engine failure, not worth paying on a flight where the engine never
    /// stopped.
    /// </summary>
    public static DebriefLimits For(Aircraft a, double bestGlideIasMs = 0.0,
        System.Func<double, double, double>? ground = null) => new DebriefLimits
        {
            GLimitPos = a.Structure.LimitPosG,
            GLimitNeg = a.Structure.LimitNegG,
            RedlineRpm = a.Config.Propulsion?.MaxRpm ?? 0.0,
            BestGlideIasMs = bestGlideIasMs,
            TypeName = string.IsNullOrEmpty(a.Config.DisplayName) ? a.Config.Id : a.Config.DisplayName,
            Ground = ground,
        };
}

/// <summary>
/// THE DEBRIEF: what happened, what you did about it, and the bit worth talking about.
///
/// Ty, 2026-11-10, choosing this of the three pieces of the instructor idea (a back-seat instructor, scripted lessons
/// with failures built in, and a debrief): "The replay already exists and is good. Turning it into a debrief — what
/// failed, when, what you did, how long you took to notice — is the smallest step that is useful alone, and it is
/// useful even solo."
///
/// WHAT IT IS NOT. Not a score: nothing here adds up to a number and nothing is a personal best
/// (<see cref="LessonJudge"/> scores a LESSON, where the pilot asked to be graded against one criterion; a flight is
/// not a lesson). Not a replay with extra buttons either: the replay shows you the flight, and this tells you about
/// it, in order, in the words an instructor would use. A pilot of Ty's standard throws out anything that reads as a
/// video game, so there are no points, no stars, no "nice!" and no exclamation marks.
///
/// WHERE EVERY NUMBER COMES FROM. Three places and no fourth:
///  • <see cref="FlightEventLog"/> — what happened and when.
///  • <see cref="FlightRecorder"/> — the pose stream, for every height, speed, attitude and rate, read back through
///    <see cref="FlightReading"/> and <see cref="FlightResponse"/>.
///  • <see cref="DebriefLimits"/> — the limits to compare against, handed in, with zero meaning "not known", which
///    deletes the line rather than filling it with a guess.
/// Nothing is remembered from the flight that the recording does not already hold, so a debrief built twice from one
/// recording says the same thing twice, which is what makes it testable without a build.
///
/// NO GROUND TRACK, EVER. The obvious thing to draw on a debrief is where you flew, and that is exactly the thing
/// this game does not do: Ty's standing rule (2026-10-18) is that a chart never shows your own position, enforced
/// structurally on the chart API (ChartRasterTests). A ground track IS an own-position display with a clock on it, so
/// the debrief carries no positions at all — heights, speeds, angles, rates and times only. <c>NoPositionInDebrief</c>
/// in the suite flies a flight far from the origin and fails if any world coordinate reaches the text.
/// </summary>
public sealed class Debrief
{
    /// <summary>The type and the length of the flight: "Pitts S-1S · 8:12".</summary>
    public string Headline = "";
    /// <summary>One line of what the flight contained: "one engine failure · two over-g · one bent wingtip".
    /// Empty when nothing happened.</summary>
    public string Contents = "";
    /// <summary>THE BIT WORTH TALKING ABOUT — one sentence, or empty when there honestly isn't one.</summary>
    public string Talk = "";
    /// <summary>Which line <see cref="Talk"/> is about (−1 = none), so the renderer can take the pilot there.</summary>
    public int TalkLine = -1;
    public readonly List<DebriefLine> Lines = new();
    /// <summary>How long the flight has been going, s (the recorder's clock starts at 0 with the flight).</summary>
    public double FlightSec;
    /// <summary>Nothing failed, nothing bent, nothing was exceeded. The renderer says so in one line and stops.</summary>
    public bool Quiet;
    /// <summary>The recording no longer holds the whole flight, so the summary extremes are of its last stretch
    /// only — which the summary says out loud.</summary>
    public bool Partial;

    /// <summary>Build scaffolding: the weight of the offer that currently holds <see cref="Talk"/>. Internal because
    /// nothing outside <see cref="FlightDebrief"/> means anything by the number — it exists so the heaviest offer can
    /// win without a side table keyed by this object, which would outlive it.</summary>
    internal double TalkWeight = double.MinValue;
}

/// <summary>Builds a <see cref="Debrief"/>. See <see cref="Debrief"/> for what it is and where its numbers come from.</summary>
public static class FlightDebrief
{
    /// <summary>How far before a moment the play head is put when the pilot taps a line: far enough to see it
    /// coming. The lesson debrief uses the same four seconds (LessonDebrief.WatchEvent).</summary>
    public const double CueLeadSec = 4.0;

    /// <summary>How far back from a booked overstress the peak g is looked for. An excursion is booked when the load
    /// is back inside the limit, so the pull itself is BEFORE the entry; ten seconds covers any pull a pilot can
    /// hold.</summary>
    public const double OverGLookbackSec = 10.0;

    /// <summary>A reaction slower than this is the thing worth talking about. Three seconds is not a standard — there
    /// is no published one for "notice the engine has stopped" — it is the point past which an instructor would
    /// mention it.</summary>
    public const double SlowReactionSec = 3.0;

    /// <summary>Tolerance and dwell for "settled on best glide": ±3 kt held for 2 s.</summary>
    public const double GlideTolMs = 3.0 / FlightReading.KtPerMs, GlideDwellSec = 2.0;

    public static Debrief Build(FlightRecorder rec, FlightEventLog log, DebriefLimits limits)
    {
        var d = new Debrief { FlightSec = rec.Count > 0 ? rec.EndTime : 0.0 };
        d.Partial = rec.Count > 0 && rec.StartTime > 0.5;
        d.Headline = (string.IsNullOrEmpty(limits.TypeName) ? "Flight" : limits.TypeName)
                     + " · " + FlightReading.Clock(d.FlightSec);
        if (rec.Count < 2) { d.Quiet = true; d.Lines.Add(Plain("NO RECORDING", "There is not enough of this flight recorded to debrief.")); return d; }

        var counts = new Dictionary<string, int>();
        int touchdowns = 0;

        for (int i = 0; i < log.Count; i++)
        {
            FlightEvent e = log.At(i);
            switch (e.Kind)
            {
                case FlightEventKind.EngineStopped: Engine(d, rec, limits, e, counts); break;
                case FlightEventKind.InjectedFailure: Injected(d, rec, limits, e, counts); break;
                case FlightEventKind.EngineStarted:
                    Add(d, rec, e.T, DebriefTone.Note, "ENGINE RUNNING",
                        $"{FlightReading.Clock(e.T)} — it was making power again at {e.A:0} rpm."); break;
                case FlightEventKind.Overstress: OverG(d, rec, limits, e, counts); break;
                case FlightEventKind.StructureBroke: Broke(d, rec, e, counts); break;
                case FlightEventKind.PanelBent: Bent(d, rec, limits, e, counts); break;
                case FlightEventKind.PanelBroken:
                    Bump(counts, "broken panel");
                    Add(d, rec, e.T, DebriefTone.Serious, "PANEL BROKE",
                        $"{FlightReading.Clock(e.T)} — the {Words.Of(e.Component)} broke off at its station."); break;
                case FlightEventKind.WingFolded:
                    Bump(counts, "folded wing");
                    Add(d, rec, e.T, DebriefTone.Serious, "WING FOLDED",
                        $"{FlightReading.Clock(e.T)} — the {Words.Of(e.Component)} folded {System.Math.Abs(e.A):0} ° about its root."); break;
                case FlightEventKind.ComponentLost:
                    Bump(counts, "lost part");
                    Add(d, rec, e.T, DebriefTone.Serious, "PART GONE",
                        $"{FlightReading.Clock(e.T)} — the {Words.Of(e.Component)} left the aeroplane."); break;
                case FlightEventKind.StallBegan: Stall(d, rec, log, e, i, counts); break;
                case FlightEventKind.SpinBegan: Spin(d, rec, log, limits, e, i, counts); break;
                case FlightEventKind.OverspeedBegan: Overspeed(d, rec, log, e, i, counts); break;
                case FlightEventKind.PropOverspeedBegan: PropOverspeed(d, rec, log, limits, e, i, counts); break;
                case FlightEventKind.FireBegan: Fire(d, rec, log, e, i, counts); break;
                case FlightEventKind.FuelLeakBegan:
                    Bump(counts, "holed tank");
                    Add(d, rec, e.T, DebriefTone.Serious, "FUEL LEAK",
                        $"{FlightReading.Clock(e.T)} — the {Words.Of(e.Component)} is holed and streaming fuel."); break;
                case FlightEventKind.Touchdown: touchdowns++; Touchdown(d, rec, e, touchdowns, counts); break;
                case FlightEventKind.PilotOut:
                    Bump(counts, "bail-out");
                    Add(d, rec, e.T, DebriefTone.Serious, "YOU LEFT IT",
                        $"{FlightReading.Clock(e.T)} — you got out, {Height(rec, limits, e.T)} and {FlightReading.Kt(FlightReading.IasMs(rec.Sample(System.Math.Min(e.T, rec.EndTime))))}."); break;
                case FlightEventKind.Crash:
                    Bump(counts, "crash");
                    Add(d, rec, e.T, DebriefTone.Serious, "CRASH",
                        $"{FlightReading.Clock(e.T)} — it hit{(e.Detail.Length > 0 ? " " + e.Detail : "")}, {FlightReading.Kt(FlightReading.IasMs(rec.Sample(System.Math.Min(e.T, rec.EndTime))))} and {FlightReading.Fpm(FlightReading.SinkMs(rec.Sample(System.Math.Min(e.T, rec.EndTime))))} of sink."); break;
                // The closing half of a bracketed condition, and the lift-offs: read by the opening half, no line of
                // their own (see FlightEventKind: "conditions come in pairs").
                case FlightEventKind.StallEnded:
                case FlightEventKind.SpinEnded:
                case FlightEventKind.OverspeedEnded:
                case FlightEventKind.PropOverspeedEnded:
                case FlightEventKind.FireEnded:
                case FlightEventKind.Liftoff:
                    break;
            }
        }

        d.Contents = Contents(counts);
        if (d.Lines.Count == 0)
        {
            d.Quiet = true;
            d.Lines.Add(Plain("NOTHING TO DEBRIEF",
                $"{FlightReading.Clock(d.FlightSec)} of flying: nothing failed, nothing bent, and you stayed inside the limits."));
        }
        d.Lines.Add(Summary(rec, limits, d.Partial));
        return d;
    }

    // ---- the engine --------------------------------------------------------------------------------------------
    private static void Engine(Debrief d, FlightRecorder rec, DebriefLimits lim, in FlightEvent e,
        Dictionary<string, int> counts)
    {
        Bump(counts, "engine failure");
        string why = e.Detail.Length > 0
            ? $" {Capital(e.Detail)}."
            : " Nothing in the recording says why.";
        Add(d, rec, e.T, DebriefTone.Serious, "ENGINE STOPPED",
            $"{FlightReading.Clock(e.T)} — the engine stopped, turning {e.A:0} rpm, {Height(rec, lim, e.T)} at {FlightReading.Kt(Ias(rec, e.T))}.{why}");
        Response(d, rec, lim, e.T, "it quit", "the engine quitting", counts);
    }

    private static void Injected(Debrief d, FlightRecorder rec, DebriefLimits lim, in FlightEvent e,
        Dictionary<string, int> counts)
    {
        Bump(counts, "failure");
        string what = e.Detail.Length > 0 ? e.Detail : "something was failed on you";
        Add(d, rec, e.T, DebriefTone.Serious, "FAILURE",
            $"{FlightReading.Clock(e.T)} — {what}, {Height(rec, lim, e.T)} at {FlightReading.Kt(Ias(rec, e.T))}.");
        Response(d, rec, lim, e.T, "it happened", "the failure", counts);
    }

    /// <summary>
    /// WHAT YOU DID ABOUT IT — the heart of the thing, and the only line in the debrief with an opinion in it.
    /// The latency, which control it was, what the wait cost in height, and (when the caller knows the number) when
    /// the aeroplane actually arrived at best glide. Where the reading is not a reaction time it says so.
    /// </summary>
    private static void Response(Debrief d, FlightRecorder rec, DebriefLimits lim, double t0, string since,
        string gerund, Dictionary<string, int> counts)
    {
        string text;
        DebriefTone tone = DebriefTone.Note;
        if (!FlightResponse.TryReaction(rec, t0, out PilotReaction r))
        {
            bool ranOut = rec.EndTime - t0 < FlightResponse.SearchWindowSec;
            text = ranOut
                ? $"You had not moved a control by the time the recording ends, {rec.EndTime - t0:0.#} s later."
                : $"You moved nothing at all in the {FlightResponse.SearchWindowSec:0} s after {since}.";
            tone = DebriefTone.Serious;
        }
        else
        {
            string cost = r.AltitudeLostM > 3.0 ? $", and {FlightReading.Ft(r.AltitudeLostM)} of height went by while you waited" : "";
            text = $"The first thing you moved was {r.ControlWords}, {r.LatencySec:0.#} s after {since}{cost}.";
            if (!r.Clean)
                text += $" You were already working {r.ControlWords} when {since}, so that is when it changed — not a reaction time.";
            else if (r.LatencySec >= SlowReactionSec) tone = DebriefTone.Caution;
        }

        if (lim.BestGlideIasMs > 0)
        {
            if (FlightResponse.TrySettledOnSpeed(rec, t0, lim.BestGlideIasMs, GlideTolMs, GlideDwellSec,
                    out double tGlide, out double _))
                text += $" Best glide is {FlightReading.Kt(lim.BestGlideIasMs)}; you were on it at {FlightReading.Clock(tGlide)}, {tGlide - t0:0.#} s after {since}.";
            else
            {
                FlightResponse.TrySettledOnSpeed(rec, t0, lim.BestGlideIasMs, GlideTolMs, GlideDwellSec, out _,
                    out double closest);
                text += double.IsNaN(closest)
                    ? $" Best glide is {FlightReading.Kt(lim.BestGlideIasMs)}."
                    : $" You never settled on best glide ({FlightReading.Kt(lim.BestGlideIasMs)}); the nearest you held was {FlightReading.Kt(closest)}.";
            }
        }

        int line = d.Lines.Count;
        Add(d, rec, t0, tone, "WHAT YOU DID", text);
        // The weight: an absent or slow response to a failure is the first thing an instructor would raise.
        double weight = 100 + (r.Found ? (r.Clean ? r.LatencySec : 0.0) : 30.0);
        Consider(d, line, weight, r.Found
            ? (r.Clean && r.LatencySec >= SlowReactionSec
                ? $"{r.LatencySec:0.#} s from {gerund} to your hand on {r.ControlWords}."
                : $"Your first move after {since} was {r.ControlWords}, {r.LatencySec:0.#} s later.")
            : $"You moved nothing at all after {since}.");
    }

    // ---- g ----------------------------------------------------------------------------------------------------
    private static void OverG(Debrief d, FlightRecorder rec, DebriefLimits lim, in FlightEvent e,
        Dictionary<string, int> counts)
    {
        bool up = e.A >= 0;                                // the sign of the pull (see FlightEventKind.Overstress)
        double ratio = System.Math.Abs(e.A);
        double from = e.T - OverGLookbackSec;
        double g = up ? FlightResponse.PeakG(rec, from, e.T) : FlightResponse.MinG(rec, from, e.T);
        double limit = up ? lim.GLimitPos : lim.GLimitNeg;
        string member = e.Detail.Length > 0 ? e.Detail : "the structure";
        string pull = up ? $"you pulled {g:0.0} g" : $"you pushed to {g:0.0} g";
        string against = limit != 0.0 ? $" against a {limit:0.0} g limit" : "";
        double setDeg = System.Math.Abs(e.C);
        double lost = e.B * 100.0;

        if (e.Flag)
        {
            // STRAIGHTENING: Ty's own rule, 2026-11-10, and the reason this case gets its own paragraph. His words,
            // verbatim from OverstressMember.ReversalFatigueFactor's comment.
            Bump(counts, "straightened wing");
            double wouldHave = lost / OverstressMember.ReversalFatigueFactor;
            Add(d, rec, e.T, DebriefTone.Serious, $"STRAIGHTENED THE {member.ToUpperInvariant()}",
                $"{FlightReading.Clock(e.T)} — you bent the {member} back the other way: {pull}{against}, {ratio:0.00} times what is left of its limit. "
                + $"The set came out, and so did the aileron travel it was costing you — but straightening a bent spar costs four times what bending it further would: "
                + $"{lost:0.0} % of its strength instead of {wouldHave:0.0} %. His own rule: “you can straighten your wings out once but it will cost you significantly for the next stress.”");
            Consider(d, d.Lines.Count - 1, 90, $"You straightened the {member} out — {lost:0.0} % of its strength for it.");
            return;
        }

        Bump(counts, "over-g");
        if (setDeg >= 0.5 || lost > 0.05)
            Add(d, rec, e.T, DebriefTone.Serious, $"OVER-G: {member.ToUpperInvariant()}",
                $"{FlightReading.Clock(e.T)} — {pull}{against}: {ratio:0.00} times the {member}'s limit, past the 1.5 where it yields. "
                + $"It has kept {setDeg:0.0} ° of permanent set and lost {lost:0.0} % of its strength, so it will bend at less next time.");
        else
            Add(d, rec, e.T, DebriefTone.Caution, $"OVER-G: {member.ToUpperInvariant()}",
                $"{FlightReading.Clock(e.T)} — {pull}{against}: {ratio:0.00} times the {member}'s limit. Nothing is bent, but that is past the rating.");
        Consider(d, d.Lines.Count - 1, setDeg >= 0.5 ? 80 : 45,
            $"{Capital(pull)} at {FlightReading.Clock(e.T)} — {ratio:0.00} times the {member}'s limit.");
    }

    private static void Broke(Debrief d, FlightRecorder rec, in FlightEvent e, Dictionary<string, int> counts)
    {
        Bump(counts, "structural failure");
        string member = e.Detail.Length > 0 ? e.Detail : "the structure";
        Add(d, rec, e.T, DebriefTone.Serious, $"{member.ToUpperInvariant()} FAILED",
            $"{FlightReading.Clock(e.T)} — the {member} came apart at {System.Math.Abs(e.A):0.00} times its limit. "
            + "Two times limit is where it breaks, and weakening from earlier pulls moves that down.");
        Consider(d, d.Lines.Count - 1, 85, $"The {member} came apart at {FlightReading.Clock(e.T)}.");
    }

    // ---- what the ground did ----------------------------------------------------------------------------------
    private static void Bent(Debrief d, FlightRecorder rec, DebriefLimits lim, in FlightEvent e,
        Dictionary<string, int> counts)
    {
        Bump(counts, "bent panel");
        ReplayFrame f = rec.Sample(System.Math.Clamp(e.T, rec.StartTime, rec.EndTime));
        // The facts of the moment, and no cause: nothing in the recording says what it touched.
        Add(d, rec, e.T, DebriefTone.Serious, "BENT: " + Words.Of(e.Component).ToUpperInvariant(),
            $"{FlightReading.Clock(e.T)} — the {Words.Of(e.Component)} took a {System.Math.Abs(e.A):0} ° set. "
            + $"At that moment you were {Height(rec, lim, e.T)} at {FlightReading.Kt(FlightReading.IasMs(f))}, {Bank(f)}, "
            + $"{FlightReading.Fpm(FlightReading.SinkMs(f))} of sink.");
        Consider(d, d.Lines.Count - 1, 60, $"You bent the {Words.Of(e.Component)} at {FlightReading.Clock(e.T)}.");
    }

    // ---- the stall and the spin -------------------------------------------------------------------------------
    private static void Stall(Debrief d, FlightRecorder rec, FlightEventLog log, in FlightEvent e, int i,
        Dictionary<string, int> counts)
    {
        Bump(counts, "stall");
        double end = Closing(log, i, FlightEventKind.StallEnded);
        bool open = double.IsNaN(end);
        double to = open ? rec.EndTime : end;
        double lost = FlightResponse.AltitudeLostM(rec, e.T, to);
        string tail = open
            ? "It was still stalled when the recording ends."
            : $"It was flying again at {FlightReading.Clock(end)} — {end - e.T:0.#} s and {FlightReading.Ft(lost)}.";
        Add(d, rec, e.T, DebriefTone.Caution, "STALL",
            $"{FlightReading.Clock(e.T)} — it stalled, {FlightReading.Kt(Ias(rec, e.T))} and {Bank(rec.Sample(System.Math.Clamp(e.T, rec.StartTime, rec.EndTime)))}. {tail}");
        // No reaction time on a stall. The pilot is already holding the stick back — that is how he got here — so a
        // latency measured off a control that was already moving would be exactly the number
        // PilotReaction.Clean exists to refuse. Height lost is the honest measure, and it is the one the ACS uses
        // (LessonJudge.Std.StallLoss: 100 ft green).
        if (!open && lost > 0)
        {
            Grade g = LessonJudge.Std.StallLoss.Rate(lost * FlightReading.FtPerM);
            Add(d, rec, e.T, g >= Grade.Orange ? DebriefTone.Caution : DebriefTone.Note, "RECOVERY",
                $"{FlightReading.Ft(lost)} to get it flying again. {GradeWords(g)} — the standard is 100 ft, and it is bought by lowering the angle of attack at once rather than by pulling.");
            if (g >= Grade.Orange)
                Consider(d, d.Lines.Count - 1, 55, $"The stall at {FlightReading.Clock(e.T)} cost you {FlightReading.Ft(lost)}.");
        }
    }

    private static void Spin(Debrief d, FlightRecorder rec, FlightEventLog log, DebriefLimits lim,
        in FlightEvent e, int i, Dictionary<string, int> counts)
    {
        Bump(counts, "spin");
        double end = Closing(log, i, FlightEventKind.SpinEnded);
        bool open = double.IsNaN(end);
        double to = open ? rec.EndTime : end;
        bool right = e.A >= 0;
        double turns = FlightResponse.YawTurns(rec, e.T, to);
        double lost = FlightResponse.AltitudeLostM(rec, e.T, to);
        string tail = open
            ? $"It was still turning when the recording ends — about {turns:0.#} turns and {FlightReading.Ft(lost)} so far."
            : $"It stopped at {FlightReading.Clock(end)}: {end - e.T:0.#} s, about {turns:0.#} turns, {FlightReading.Ft(lost)}.";
        int spinLine = d.Lines.Count;
        Add(d, rec, e.T, DebriefTone.Serious, right ? "SPIN TO THE RIGHT" : "SPIN TO THE LEFT",
            $"{FlightReading.Clock(e.T)} — it went round to the {(right ? "right" : "left")}, {System.Math.Abs(e.A):0} °/s of yaw at the entry, {Height(rec, lim, e.T)}. {tail}");

        // The rudder, and WHICH WAY. Pro-spin rudder is the classic error and the recording can see it without
        // guessing: + rudder is nose right (the sim's convention), so rudder of the same sign as the yaw rate holds
        // the rotation in.
        if (FlightResponse.TryReaction(rec, e.T, out PilotReaction r, PilotControl.Rudder))
        {
            double before = rec.Sample(System.Math.Clamp(e.T, rec.StartTime, rec.EndTime)).Deflections.RudderRad;
            double after = rec.Sample(System.Math.Clamp(r.T, rec.StartTime, rec.EndTime)).Deflections.RudderRad;
            bool pro = (after - before) * (right ? 1 : -1) > 0;
            Add(d, rec, r.T, pro ? DebriefTone.Serious : DebriefTone.Note, "RUDDER",
                $"You moved the rudder {r.LatencySec:0.#} s after it started, {r.MovedBy:0} °, "
                + (pro
                    ? "WITH the rotation. That is pro-spin rudder: it holds the spin in. Full rudder against the yaw is what stops it."
                    : "against the rotation — which is what stops it."));
            if (pro) Consider(d, d.Lines.Count - 1, 78, $"You fed pro-spin rudder into the spin at {FlightReading.Clock(r.T)}.");
        }
        else
            Add(d, rec, e.T, DebriefTone.Serious, "RUDDER",
                "The rudder did not move while it was turning. A spin stops with rudder against the yaw; nothing else does it.");
        Consider(d, spinLine, 75, $"A spin at {FlightReading.Clock(e.T)}: about {turns:0.#} turns and {FlightReading.Ft(lost)}.");
    }

    // ---- the limits -------------------------------------------------------------------------------------------
    private static void Overspeed(Debrief d, FlightRecorder rec, FlightEventLog log, in FlightEvent e, int i,
        Dictionary<string, int> counts)
    {
        Bump(counts, "overspeed");
        double end = Closing(log, i, FlightEventKind.OverspeedEnded);
        double to = double.IsNaN(end) ? rec.EndTime : end;
        double peak = FlightResponse.PeakIasMs(rec, e.T, to);
        Add(d, rec, e.T, DebriefTone.Serious, "PAST VNE",
            $"{FlightReading.Clock(e.T)} — past Vne. Vne at that height is {FlightReading.Kt(e.A)}; you reached {FlightReading.Kt(peak)} and were over it for {to - e.T:0.#} s.");
        Consider(d, d.Lines.Count - 1, 50, $"You went {FlightReading.Kt(peak - e.A)} past Vne at {FlightReading.Clock(e.T)}.");
    }

    private static void PropOverspeed(Debrief d, FlightRecorder rec, FlightEventLog log, DebriefLimits lim,
        in FlightEvent e, int i, Dictionary<string, int> counts)
    {
        Bump(counts, "prop overspeed");
        double end = Closing(log, i, FlightEventKind.PropOverspeedEnded);
        double to = double.IsNaN(end) ? rec.EndTime : end;
        double peak = FlightResponse.PeakRpm(rec, e.T, to);
        string how = e.Detail.Length > 0 ? ", " + e.Detail : "";
        Add(d, rec, e.T, DebriefTone.Serious, "PROP PAST RED LINE",
            $"{FlightReading.Clock(e.T)} — the propeller went past its red line{how}: {peak:0} rpm against {e.A:0}, for {to - e.T:0.#} s, "
            + $"at {FlightReading.Kt(FlightResponse.PeakIasMs(rec, e.T, to))}. Raise the nose: the speed is what is turning it.");
        Consider(d, d.Lines.Count - 1, 50, $"The propeller ran to {peak:0} rpm past a {e.A:0} red line at {FlightReading.Clock(e.T)}.");
    }

    private static void Fire(Debrief d, FlightRecorder rec, FlightEventLog log, in FlightEvent e, int i,
        Dictionary<string, int> counts)
    {
        Bump(counts, "fire");
        double end = Closing(log, i, FlightEventKind.FireEnded);
        string tail = double.IsNaN(end)
            ? "It was still burning when the recording ends."
            : $"It stopped burning at {FlightReading.Clock(end)}, {end - e.T:0.#} s later.";
        Add(d, rec, e.T, DebriefTone.Serious, "FIRE",
            $"{FlightReading.Clock(e.T)} — the {Words.Of(e.Component)} caught fire. {tail}");
        Consider(d, d.Lines.Count - 1, 65, $"A fire at {FlightReading.Clock(e.T)}.");
    }

    // ---- the arrival ------------------------------------------------------------------------------------------
    private static void Touchdown(Debrief d, FlightRecorder rec, in FlightEvent e, int n,
        Dictionary<string, int> counts)
    {
        Bump(counts, n > 1 ? "bounce" : "landing");
        double fpm = e.A * FlightReading.FtPerM * 60.0;
        Grade g = LessonJudge.Std.TdSink.Rate(fpm);
        string which = n > 1 ? $"touchdown {n}" : "touchdown";
        Add(d, rec, e.T, g >= Grade.Orange ? DebriefTone.Caution : DebriefTone.Note,
            n > 1 ? "BOUNCE" : "TOUCHDOWN",
            $"{FlightReading.Clock(e.T)} — {which}: {FlightReading.Fpm(e.A)}, {System.Math.Abs(e.B):0.#} ° of crab, {e.C:0.#} ° of pitch. "
            + $"{GradeWords(g)} ({LessonJudge.Std.TdSink.Ideal}), and a softer one is bought by holding it off a moment longer.");
        if (g >= Grade.Orange)
            Consider(d, d.Lines.Count - 1, 40, $"A {FlightReading.Fpm(e.A)} arrival at {FlightReading.Clock(e.T)}.");
    }

    // ---- the summary ------------------------------------------------------------------------------------------
    private static DebriefLine Summary(FlightRecorder rec, DebriefLimits lim, bool partial)
    {
        double gHi = FlightResponse.PeakG(rec, rec.StartTime, rec.EndTime);
        double gLo = FlightResponse.MinG(rec, rec.StartTime, rec.EndTime);
        double vHi = FlightResponse.PeakIasMs(rec, rec.StartTime, rec.EndTime);
        string window = partial
            ? $" (the last {FlightReading.Clock(rec.EndTime - rec.StartTime)} — the recorder keeps no more)"
            : "";
        string limits = lim.GLimitPos != 0.0
            ? $" against limits of {lim.GLimitPos:+0.0;-0.0} and {lim.GLimitNeg:+0.0;-0.0}"
            : "";
        return Plain("THE NUMBERS",
            $"Highest g {gHi:0.0}, lowest {gLo:0.0}{limits}. Fastest {FlightReading.Kt(vHi)}.{window}");
    }

    // ---- plumbing ---------------------------------------------------------------------------------------------
    private static void Add(Debrief d, FlightRecorder rec, double t, DebriefTone tone, string title, string text)
    {
        bool canCue = rec.Count > 0 && t >= rec.StartTime - 1e-9 && t <= rec.EndTime + 1e-9;
        double cue = canCue ? System.Math.Max(rec.StartTime, t - CueLeadSec) : t;
        d.Lines.Add(new DebriefLine(t, true, tone, title, text, cue, canCue));
    }

    private static DebriefLine Plain(string title, string text) =>
        new DebriefLine(0, false, DebriefTone.Note, title, text, 0, false);

    /// <summary>Offer a line as THE bit worth talking about; the heaviest offer wins.</summary>
    private static void Consider(Debrief d, int line, double weight, string talk)
    {
        if (line < 0 || line >= d.Lines.Count || weight <= d.TalkWeight) return;
        d.TalkWeight = weight;
        d.Talk = talk; d.TalkLine = line;
    }

    /// <summary>The closing half of a bracketed condition at or after index <paramref name="i"/>, or NaN when the
    /// condition never closed (see <see cref="FlightEventKind"/>: "conditions come in pairs").</summary>
    private static double Closing(FlightEventLog log, int i, FlightEventKind closing)
    {
        for (int k = i + 1; k < log.Count; k++)
        {
            FlightEvent e = log.At(k);
            if (e.Kind == closing) return e.T;
            if (e.Kind == log.At(i).Kind) break;            // the condition began again without closing: unmatched
        }
        return double.NaN;
    }

    /// <summary>Bank as a pilot says it, which is "wings level" and not "0 ° of bank right".</summary>
    private static string Bank(in ReplayFrame f)
    {
        double b = FlightReading.BankDeg(f);
        return System.Math.Abs(b) < 1.0 ? "wings level" : $"{System.Math.Abs(b):0} ° of bank {(b > 0 ? "right" : "left")}";
    }

    private static double Ias(FlightRecorder rec, double t) =>
        FlightReading.IasMs(rec.Sample(System.Math.Clamp(t, rec.StartTime, rec.EndTime)));

    /// <summary>
    /// How high, in the words a pilot uses: above the GROUND when the caller can tell us where the ground is, above
    /// sea level when it cannot. NEVER where — see <see cref="Debrief"/> on the no-position rule. The ground lookup
    /// takes a world point and gives back a height; the point does not survive the call.
    /// </summary>
    private static string Height(FlightRecorder rec, DebriefLimits lim, double t)
    {
        ReplayFrame f = rec.Sample(System.Math.Clamp(t, rec.StartTime, rec.EndTime));
        double msl = FlightReading.AltitudeM(f);
        if (lim == null || lim.Ground == null) return FlightReading.Ft(msl) + " up";
        double agl = msl - lim.Ground(f.State.Position.X, f.State.Position.Y);
        return FlightReading.Ft(System.Math.Max(0.0, agl)) + " over the ground";
    }

    private static void Bump(Dictionary<string, int> counts, string what) =>
        counts[what] = (counts.TryGetValue(what, out int n) ? n : 0) + 1;

    private static string Contents(Dictionary<string, int> counts)
    {
        if (counts.Count == 0) return "";
        var parts = new List<string>();
        foreach (KeyValuePair<string, int> kv in counts)
            parts.Add(kv.Value == 1 ? "one " + kv.Key : $"{Number(kv.Value)} {Plural(kv.Key)}");
        parts.Sort(System.StringComparer.Ordinal);
        return string.Join(" · ", parts);
    }

    private static string Number(int n) => n switch
    {
        2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight",
        9 => "nine", _ => n.ToString(),
    };

    private static string Plural(string s) =>
        s.EndsWith("failure") || s.EndsWith("stall") || s.EndsWith("spin") || s.EndsWith("panel")
        || s.EndsWith("part") || s.EndsWith("wing") || s.EndsWith("crash") || s.EndsWith("bounce")
        || s.EndsWith("landing") || s.EndsWith("fire") || s.EndsWith("tank") || s.EndsWith("overspeed")
        || s.EndsWith("out")
            ? s + (s.EndsWith("crash") ? "es" : "s")
            : s;

    private static string GradeWords(Grade g) => g switch
    {
        Grade.Green => "That is the standard",
        Grade.Yellow => "Close to the standard",
        Grade.Orange => "Well outside the standard",
        _ => "Nowhere near the standard",
    };

    private static string Capital(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    /// <summary>
    /// Part names as PROSE ("the right lower wingtip panel"), which is not the same thing as the HUD's shout
    /// ("BENT: RIGHT LOWER WINGTIP PANEL"). The HUD's names live in the bridge (StructuralDamage.PanelName) because
    /// that is where the HUD is; these live here because the debrief's sentences are built here, where the suite can
    /// read them. Deliberately a second list and not a refactor of the first: the HUD's wording was flight-tested and
    /// is not up for change by this lane.
    /// </summary>
    public static class Words
    {
        public static string Of(AirframeComponent c) => c switch
        {
            AirframeComponent.WingLeft => "left wing",
            AirframeComponent.WingRight => "right wing",
            AirframeComponent.WingLeftOuter => "left outer wing",
            AirframeComponent.WingRightOuter => "right outer wing",
            AirframeComponent.WingLeftInner => "left inner wing",
            AirframeComponent.WingRightInner => "right inner wing",
            AirframeComponent.WingTipLeft => "left wingtip panel",
            AirframeComponent.WingTipRight => "right wingtip panel",
            AirframeComponent.WingTipLowerLeft => "left lower wingtip panel",
            AirframeComponent.WingTipLowerRight => "right lower wingtip panel",
            AirframeComponent.TailHorizontal => "stabiliser",
            AirframeComponent.TailVertical => "fin",
            AirframeComponent.TailBoom => "tail",
            AirframeComponent.AileronLeft => "left aileron",
            AirframeComponent.AileronRight => "right aileron",
            AirframeComponent.ElevatorLeft => "left elevator",
            AirframeComponent.ElevatorRight => "right elevator",
            AirframeComponent.Rudder => "rudder",
            AirframeComponent.GearLeft => "left gear leg",
            AirframeComponent.GearRight => "right gear leg",
            AirframeComponent.GearNose => "nose gear",
            AirframeComponent.GearTail => "tailwheel",
            AirframeComponent.NacelleLeft => "left nacelle",
            AirframeComponent.NacelleRight => "right nacelle",
            AirframeComponent.Propeller => "propeller",
            AirframeComponent.Nose => "nose",
            AirframeComponent.Cabin => "cabin",
            _ => "fuselage",
        };
    }
}

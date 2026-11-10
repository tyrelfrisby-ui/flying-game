namespace FlyingGame.Sim.Replay;

/// <summary>Which control the pilot moved.</summary>
public enum PilotControl { None, Throttle, Elevator, Aileron, Rudder, Flap, Gear, Spoiler }

/// <summary>
/// WHAT THE PILOT DID ABOUT IT, AND HOW LONG HE TOOK. One reading, measured from the recording.
///
/// Ty's own example of the feature, 2026-11-10: "The engine quit at 4:12; the throttle came back at 4:19; best-glide
/// speed was reached at 4:31. Seven seconds to notice. That one number is worth more than a page of statistics."
/// </summary>
public readonly struct PilotReaction
{
    /// <summary>The recording holds a movement that counts.</summary>
    public readonly bool Found;
    /// <summary>When the control MOVED (recorder clock) — the first frame past the dead band, not the end of the
    /// window that confirmed it.</summary>
    public readonly double T;
    /// <summary>T minus the event, s. This is the number the debrief leads with.</summary>
    public readonly double LatencySec;
    public readonly PilotControl Control;
    /// <summary>How far it moved from where it was held: degrees for a surface, fraction of travel for the
    /// throttle, the flap, the gear and the spoiler.</summary>
    public readonly double MovedBy;
    /// <summary>
    /// THE READING IS A REACTION TIME. False when the pilot was ALREADY working that control in the second before the
    /// event (<see cref="FlightResponse.PriorWindowSec"/>): a hand that is already moving cannot be timed, and a
    /// debrief that reported 0.2 s because the pilot happened to be rolling when the engine quit would be a lie the
    /// pilot could see through at once. The debrief still gives the number — it is when he moved — but says plainly
    /// that it is not a reaction time.
    /// </summary>
    public readonly bool Clean;
    /// <summary>Height between the event and the movement, m: positive = lost. What the delay cost.</summary>
    public readonly double AltitudeLostM;

    public PilotReaction(double t, double latency, PilotControl control, double movedBy, bool clean, double altLost)
    {
        Found = true; T = t; LatencySec = latency; Control = control; MovedBy = movedBy; Clean = clean;
        AltitudeLostM = altLost;
    }

    /// <summary>The control's name as a debrief sentence uses it ("the throttle", "the stick", "the rudder").</summary>
    public string ControlWords => Words(Control);

    public static string Words(PilotControl c) => c switch
    {
        PilotControl.Throttle => "the throttle",
        PilotControl.Elevator => "the stick",
        PilotControl.Aileron => "the stick",
        PilotControl.Rudder => "the rudder",
        PilotControl.Flap => "the flap lever",
        PilotControl.Gear => "the gear",
        PilotControl.Spoiler => "the brakes",
        _ => "nothing",
    };
}

/// <summary>
/// THE INSTRUCTOR'S QUESTION, measured off the recording: how long after something happened did the pilot do
/// anything about it, and when did the aeroplane end up where it should be.
///
/// WHAT "NOTICED" MEANS HERE, because the whole feature rests on it. Not "did he know" — the recording cannot see
/// inside the cockpit — but the one thing an instructor can actually time: THE FIRST DELIBERATE CONTROL MOVEMENT
/// AFTER THE EVENT. A movement counts when a control has left where it was held at the event by more than a dead
/// band (<see cref="ThrottleDeadBand"/>, <see cref="SurfaceDeadBandRad"/>) and has STAYED past it for
/// <see cref="SustainSec"/>.
///
/// The two halves of that rule each buy something:
///  • The DEAD BAND keeps the needle still. A pilot flying with a finger on a touch pad is never exactly steady, and
///    a gust moves the stick through the control loop; 15 % of throttle travel and 3° of surface are larger than
///    either and smaller than any decision.
///  • The SUSTAIN keeps one sample from being read as a decision. A touch-screen slip, a single noisy frame or the
///    stick passing through on its way back would otherwise all time as a response. It also means the reaction time
///    reported is the moment the control moved, CONFIRMED a third of a second later — so the number is early-honest,
///    not late.
///
/// And where the rule cannot answer, it says so rather than answering anyway: see <see cref="PilotReaction.Clean"/>.
///
/// Nothing here knows what the event WAS. That is deliberate: the same measurement serves an engine failure, a
/// structural bang, a fire, and — when they arrive — an instructor's injected failure and a lesson's scripted one,
/// because all four ask the same question.
/// </summary>
public static class FlightResponse
{
    /// <summary>Fraction of travel a lever must move to count as moved.</summary>
    public const double ThrottleDeadBand = 0.15, FlapDeadBand = 0.15, GearDeadBand = 0.20, SpoilerDeadBand = 0.15;
    /// <summary>Surface movement that counts as a control input: 3°.</summary>
    public const double SurfaceDeadBandRad = 3.0 * System.Math.PI / 180.0;
    /// <summary>How long the movement must hold to be a decision rather than a wobble.</summary>
    public const double SustainSec = 0.3;
    /// <summary>How far back a control is checked for being already in motion when the event happened.</summary>
    public const double PriorWindowSec = 1.0;
    /// <summary>How long after the event a response is still looked for. Past this the pilot was not responding to
    /// it; he was flying something else.</summary>
    public const double SearchWindowSec = 30.0;

    private static double Value(in ReplayFrame f, PilotControl c) => c switch
    {
        PilotControl.Throttle => f.Throttle01,
        PilotControl.Elevator => f.Deflections.ElevatorRad,
        PilotControl.Aileron => f.Deflections.AileronRad,
        PilotControl.Rudder => f.Deflections.RudderRad,
        PilotControl.Flap => f.FlapFraction,
        PilotControl.Gear => f.GearExtension,
        PilotControl.Spoiler => f.Deflections.SpoilerFraction,
        _ => 0.0,
    };

    private static double DeadBand(PilotControl c) => c switch
    {
        PilotControl.Throttle => ThrottleDeadBand,
        PilotControl.Flap => FlapDeadBand,
        PilotControl.Gear => GearDeadBand,
        PilotControl.Spoiler => SpoilerDeadBand,
        _ => SurfaceDeadBandRad,
    };

    /// <summary>Degrees for a surface, fraction of travel for a lever — the unit the reaction reports.</summary>
    private static double Display(PilotControl c, double delta) =>
        c is PilotControl.Elevator or PilotControl.Aileron or PilotControl.Rudder
            ? System.Math.Abs(delta) * FlightReading.R2D : System.Math.Abs(delta);

    private static readonly PilotControl[] All =
    {
        PilotControl.Throttle, PilotControl.Elevator, PilotControl.Aileron, PilotControl.Rudder,
        PilotControl.Flap, PilotControl.Gear, PilotControl.Spoiler,
    };

    /// <summary>The first recorded index strictly after <paramref name="t"/>, or Count when there is none.</summary>
    public static int IndexAfter(FlightRecorder rec, double t)
    {
        if (rec.Count == 0 || rec.At(rec.Count - 1).Time <= t) return rec.Count;
        if (rec.At(0).Time > t) return 0;
        int lo = 0, hi = rec.Count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (rec.At(mid).Time <= t) lo = mid; else hi = mid; }
        return hi;
    }

    /// <summary>
    /// The pilot's first deliberate control movement after <paramref name="eventT"/>. False when he moved nothing
    /// inside <see cref="SearchWindowSec"/>, or when the recording does not reach that far — which is itself worth
    /// saying, and the debrief does say it.
    ///
    /// <paramref name="only"/> narrows it to ONE control, for a case where the answer is a particular control and no
    /// other: a spin is stopped with the rudder, so "when did the rudder move" is the question there and a stick
    /// movement two seconds earlier is not an answer to it. <see cref="PilotControl.None"/> (the default) watches
    /// them all and reports whichever moved first — the order below breaks a tie, throttle first, which is the one a
    /// pilot reaches for when something goes quiet.
    /// </summary>
    public static bool TryReaction(FlightRecorder rec, double eventT, out PilotReaction reaction,
        PilotControl only = PilotControl.None)
    {
        reaction = default;
        if (rec.Count < 2 || eventT > rec.EndTime) return false;
        ReplayFrame at = rec.Sample(System.Math.Clamp(eventT, rec.StartTime, rec.EndTime));
        double alt0 = FlightReading.AltitudeM(at);

        // Per control: when it first went past the dead band and has stayed past it since (NaN = it has not).
        var since = new double[All.Length];
        var moved = new double[All.Length];
        for (int k = 0; k < since.Length; k++) since[k] = double.NaN;

        for (int i = IndexAfter(rec, eventT); i < rec.Count; i++)
        {
            ReplayFrame f = rec.At(i);
            if (f.Time - eventT > SearchWindowSec) break;
            for (int k = 0; k < All.Length; k++)
            {
                PilotControl c = All[k];
                if (only != PilotControl.None && c != only) continue;
                double d = Value(f, c) - Value(at, c);
                if (System.Math.Abs(d) < DeadBand(c)) { since[k] = double.NaN; continue; }
                if (double.IsNaN(since[k])) { since[k] = f.Time; moved[k] = d; }
                else if (System.Math.Abs(d) > System.Math.Abs(moved[k])) moved[k] = d;
                if (f.Time - since[k] < SustainSec) continue;
                double t0 = since[k];
                reaction = new PilotReaction(t0, t0 - eventT, c, Display(c, moved[k]), IsClean(rec, eventT, c),
                    alt0 - FlightReading.AltitudeM(rec.Sample(t0)));
                return true;
            }
        }
        return false;
    }

    /// <summary>Was this control still in the second before the event? (See <see cref="PilotReaction.Clean"/>.)</summary>
    public static bool IsClean(FlightRecorder rec, double eventT, PilotControl c)
    {
        double from = System.Math.Max(rec.StartTime, eventT - PriorWindowSec);
        if (eventT - from < 0.2) return false;              // nothing to judge from: do not claim a clean reading
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = IndexAfter(rec, from - 1e-9); i < rec.Count; i++)
        {
            ReplayFrame f = rec.At(i);
            if (f.Time > eventT) break;
            double v = Value(f, c);
            lo = System.Math.Min(lo, v); hi = System.Math.Max(hi, v);
        }
        return hi >= lo && hi - lo < DeadBand(c);
    }

    /// <summary>
    /// When the aeroplane first ARRIVED at a target airspeed after <paramref name="fromT"/> and stayed there — the
    /// second half of Ty's example ("best-glide speed was reached at 4:31"). Inside <paramref name="tolMs"/> for
    /// <paramref name="dwellSec"/>; <paramref name="t"/> is when it got there, not when the dwell completed.
    /// <paramref name="closestMs"/> is the nearest it ever came, so a debrief can say what he held instead.
    /// </summary>
    public static bool TrySettledOnSpeed(FlightRecorder rec, double fromT, double targetIasMs, double tolMs,
        double dwellSec, out double t, out double closestMs)
    {
        t = 0; closestMs = double.NaN;
        if (rec.Count < 2 || targetIasMs <= 0) return false;
        double since = double.NaN;
        double best = double.MaxValue, bestIas = double.NaN;
        for (int i = IndexAfter(rec, fromT); i < rec.Count; i++)
        {
            ReplayFrame f = rec.At(i);
            double ias = FlightReading.IasMs(f);
            double err = System.Math.Abs(ias - targetIasMs);
            if (err < best) { best = err; bestIas = ias; }
            if (err > tolMs) { since = double.NaN; continue; }
            if (double.IsNaN(since)) since = f.Time;
            if (f.Time - since >= dwellSec) { t = since; closestMs = bestIas; return true; }
        }
        closestMs = bestIas;
        return false;
    }

    /// <summary>Height lost between <paramref name="t0"/> and <paramref name="t1"/>, m: the height at t0 less the
    /// LOWEST it got to, which is what "altitude lost in the manoeuvre" means to a pilot (he cares about the hole,
    /// not where he ended up).</summary>
    public static double AltitudeLostM(FlightRecorder rec, double t0, double t1)
    {
        if (rec.Count == 0) return 0;
        double a0 = FlightReading.AltitudeM(rec.Sample(System.Math.Clamp(t0, rec.StartTime, rec.EndTime)));
        double low = a0;
        for (int i = IndexAfter(rec, t0); i < rec.Count; i++)
        {
            ReplayFrame f = rec.At(i);
            if (f.Time > t1) break;
            low = System.Math.Min(low, FlightReading.AltitudeM(f));
        }
        if (t1 <= rec.EndTime) low = System.Math.Min(low, FlightReading.AltitudeM(rec.Sample(System.Math.Clamp(t1, rec.StartTime, rec.EndTime))));
        return a0 - low;
    }

    /// <summary>The largest indicated airspeed in a window, m/s (0 when the window holds no frame).</summary>
    public static double PeakIasMs(FlightRecorder rec, double t0, double t1) =>
        Extreme(rec, t0, t1, f => FlightReading.IasMs(f), true);

    /// <summary>The largest engine rpm in a window.</summary>
    public static double PeakRpm(FlightRecorder rec, double t0, double t1) =>
        Extreme(rec, t0, t1, f => f.EngineRpm, true);

    /// <summary>The largest and smallest load factor in a window.</summary>
    public static double PeakG(FlightRecorder rec, double t0, double t1) =>
        Extreme(rec, t0, t1, f => f.LoadFactorZ, true);
    public static double MinG(FlightRecorder rec, double t0, double t1) =>
        Extreme(rec, t0, t1, f => f.LoadFactorZ, false);

    private static double Extreme(FlightRecorder rec, double t0, double t1, System.Func<ReplayFrame, double> read,
        bool max)
    {
        double best = double.NaN;
        for (int i = IndexAfter(rec, t0 - 1e-9); i < rec.Count; i++)
        {
            ReplayFrame f = rec.At(i);
            if (f.Time > t1) break;
            double v = read(f);
            if (double.IsNaN(best) || (max ? v > best : v < best)) best = v;
        }
        return double.IsNaN(best) ? 0.0 : best;
    }

    /// <summary>
    /// Turns of autorotation between two times, from ∫r dt about the body's yaw axis. APPROXIMATE, and the debrief
    /// says "about": in a spin the aeroplane rotates about a vertical axis the body's z only partly lies along, so
    /// the yaw-rate integral is the standard estimate of turns and not the exact count. It is reported because an
    /// instructor counts turns, and because every other number here (duration, rate, height lost) is exact.
    /// </summary>
    public static double YawTurns(FlightRecorder rec, double t0, double t1)
    {
        double sum = 0, last = double.NaN, lastT = 0;
        for (int i = IndexAfter(rec, t0 - 1e-9); i < rec.Count; i++)
        {
            ReplayFrame f = rec.At(i);
            if (f.Time > t1) break;
            double r = f.State.Rates.Z;
            if (!double.IsNaN(last)) sum += 0.5 * (r + last) * (f.Time - lastT);
            last = r; lastT = f.Time;
        }
        return System.Math.Abs(sum) / (2.0 * System.Math.PI);
    }
}

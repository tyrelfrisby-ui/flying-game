using FlyingGame.Core;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// WHAT HAPPENED, and WHEN. One kind per thing a debrief would mention by name (Ty 2026-11-10, choosing the debrief
/// as the first step of the instructor work: "what failed, when, what you did, how long you took to notice").
///
/// A/B/C below are <see cref="FlightEvent"/>'s three measurements; what they mean is per kind and is documented on
/// each kind, exactly as <see cref="ContactEventKind"/> documents its Intensity and Value.
///
/// THE EVENT SAYS WHAT AND WHEN; THE RECORDING SAYS WHAT THE AEROPLANE WAS DOING. Nothing here carries a height, a
/// speed, a heading or an attitude that <see cref="FlightRecorder"/> already holds at the same instant, because two
/// copies of the same number drift apart and the recording is the one the replay draws from. So an event is the
/// smallest fact that the pose stream cannot answer — "the engine stopped", "the right wing kept 4 degrees of set" —
/// and every "how high, how fast, how much did you lose" in the debrief is read back out of the recording
/// (<see cref="FlightDebrief"/>).
///
/// CONDITIONS COME IN PAIRS. A thing that lasts (a stall, a spin, an overspeed, a fire) is logged as a BEGAN kind and
/// an ENDED kind, so the debrief can bracket it and measure what it cost from the recording between the two. A thing
/// that is instantaneous (a part departing, an overstress booked) is one entry.
/// </summary>
public enum FlightEventKind
{
    /// <summary>The engine stopped making power. Detail names the cause when the sim knows one — fuel starvation, a
    /// closed valve, a lost propeller, a lost nose — and is EMPTY when it does not. A: the rpm it was turning when it
    /// quit.</summary>
    EngineStopped,
    /// <summary>It made power again (a relight, a restart). A: the rpm at that moment.</summary>
    EngineStarted,

    /// <summary>
    /// A G EXCURSION PAST 1.5 × LIMIT THAT CAME BACK, booked by <see cref="OverstressMember"/> the moment the load is
    /// inside the limit again (that is when the member's own set and weakening are booked, so that is when it can be
    /// reported). Detail = the member ("left wing", "right wing", "horizontal tail", "engine mount").
    /// A: the peak load the member felt, as a multiple of what is LEFT of its limit (its effective ratio, so 1.5 is
    /// where it starts to yield and 2.0 is where it breaks), SIGNED the way the load was bending it: + = the way
    /// positive g does, − = the other way. B: the strength it lost, as a fraction of the strength it
    /// had going in. C: the permanent set it is left with, degrees, signed the way positive g bends it.
    /// <see cref="FlightEvent.Flag"/> = this excursion was taking an existing set OUT — Ty's quadruple-fatigue case.
    /// </summary>
    Overstress,
    /// <summary>A member broke under load and departed. Detail = the member. A: the peak effective ratio, signed as
    /// <see cref="Overstress"/>'s is.</summary>
    StructureBroke,
    /// <summary>A wing panel took a permanent set (a tip strike, or a fold). Component = the panel. A: its bend,
    /// degrees. B: its twist, degrees.</summary>
    PanelBent,
    /// <summary>A wing panel broke off at its station. Component = the panel.</summary>
    PanelBroken,
    /// <summary>A wing folded about its root. Component = the panel on that side. A: the fold, degrees.</summary>
    WingFolded,
    /// <summary>A part of the airframe is gone (any cause: a strike, a g failure, fire, gunfire). Component = the
    /// part.</summary>
    ComponentLost,

    /// <summary>The wing stalled: the stalled fraction passed <see cref="FlightEventLog.StalledEnter"/>. A: the
    /// stalled fraction at that moment.</summary>
    StallBegan,
    /// <summary>It was flying again (back under <see cref="FlightEventLog.StalledExit"/>).</summary>
    StallEnded,
    /// <summary>Stalled AND yawing, held long enough to be autorotation rather than a wing drop
    /// (<see cref="FlightEventLog.SpinYawRateRadS"/> for <see cref="FlightEventLog.SpinDwellSec"/>). DATED AT THE
    /// START of that dwell, not at the end of it: the spin began when the rotation did. A: the yaw rate, deg/s,
    /// signed (+ = to the right).</summary>
    SpinBegan,
    /// <summary>The autorotation stopped (unstalled, or the yaw came off, for <see cref="FlightEventLog.SpinExitSec"/>).</summary>
    SpinEnded,

    /// <summary>IAS passed Vne for this height. A: Vne there, m/s.</summary>
    OverspeedBegan,
    /// <summary>Back inside Vne.</summary>
    OverspeedEnded,
    /// <summary>The propeller passed its red line. A: the red line, rpm. Detail says whether the engine was making
    /// power or the airflow was driving it.</summary>
    PropOverspeedBegan,
    /// <summary>Back under the red line.</summary>
    PropOverspeedEnded,

    /// <summary>A wing caught fire. Component = the panel (WingLeftInner / WingRightInner).</summary>
    FireBegan,
    /// <summary>It stopped burning — put out, or the panel it was on left (DamageState.FireBurnSec).</summary>
    FireEnded,
    /// <summary>A tank is holed and streaming fuel. Component = the panel.</summary>
    FuelLeakBegan,

    /// <summary>The wheels (or floats) touched. A: the sink at that instant, m/s positive down. B: the sideslip the
    /// wheels arrived at, degrees — the crab, which is what an instructor looks at. C: pitch, degrees.</summary>
    Touchdown,
    /// <summary>They left the surface.</summary>
    Liftoff,

    // ---- NOTED, never detected: the bridge or a lesson calls Note() for these -------------------------------------
    /// <summary>The pilot left the aeroplane (the bridge's egress knows; the sim does not).</summary>
    PilotOut,
    /// <summary>The flight ended in a crash. Detail = what the crash relay called it.</summary>
    Crash,
    /// <summary>
    /// SOMETHING WAS FAILED ON PURPOSE — by a lesson script, or by an instructor at a switchboard. The whole point of
    /// putting this kind in now, with nothing that raises it: Ty picked the debrief FIRST of the three
    /// (back-seat instructor, scripted lessons with failures, debrief) because it "is the smallest step that is useful
    /// alone", and the other two are meant to build on it. When they arrive, they log here and every latency
    /// measurement below works on them unchanged, because the measurement never asked where the failure came from.
    /// Detail = what was failed, in the words the debrief should use.
    /// </summary>
    InjectedFailure,
}

/// <summary>One thing that happened, at one instant of the recorder's clock.</summary>
public readonly struct FlightEvent
{
    /// <summary>Recorder-clock time, s — the same clock <see cref="FlightRecorder"/> and the replay's play head use,
    /// so the debrief can cue the replay straight to it.</summary>
    public readonly double T;
    public readonly FlightEventKind Kind;
    /// <summary>The part this is about, when it is about a part (<see cref="HasComponent"/>).</summary>
    public readonly AirframeComponent Component;
    public readonly bool HasComponent;
    /// <summary>The three measurements; what they mean is per kind (see <see cref="FlightEventKind"/>).</summary>
    public readonly double A, B, C;
    /// <summary>A per-kind flag (see <see cref="FlightEventKind"/>; only Overstress uses it so far).</summary>
    public readonly bool Flag;
    /// <summary>Words the debrief may use as they stand. EMPTY when nothing is known — never a guess.</summary>
    public readonly string Detail;

    public FlightEvent(double t, FlightEventKind kind, double a = 0, double b = 0, double c = 0,
        string? detail = null, bool flag = false)
    {
        T = t; Kind = kind; A = a; B = b; C = c; Detail = detail ?? ""; Flag = flag;
        Component = default; HasComponent = false;
    }

    public FlightEvent(double t, FlightEventKind kind, AirframeComponent component, double a = 0, double b = 0,
        double c = 0, string? detail = null, bool flag = false)
    {
        T = t; Kind = kind; A = a; B = b; C = c; Detail = detail ?? ""; Flag = flag;
        Component = component; HasComponent = true;
    }
}

/// <summary>
/// The flight's events, in time order, kept alongside the recording (Ty 2026-11-10: the debrief first, because "the
/// replay already exists and is good … what failed, when, what you did, how long you took to notice").
///
/// POLLED, NOT SUBSCRIBED. <see cref="Record"/> is called once a display frame with the live aeroplane, from the same
/// place the replay already records the pose, and works out what has changed since the last call. Three reasons, and
/// the third is the one that decided it:
///  • <see cref="Aircraft"/> raises some of this and not the rest. ComponentLost and HardImpact are events; the engine
///    stopping, a stall, a spin, an overspeed, a propeller past its red line and an overstress being booked are state,
///    with no event to hang on.
///  • A subscription has a lifetime, and the aeroplane is swapped under the bridge whenever the pilot changes type
///    (FlightSimDriver.AircraftChanged). The replay already has to clear everything there; one more subscription pair
///    to attach and detach is one more thing to leak.
///  • The replay's recorder is polled, so a polled log takes its times from the same clock on the same frame. An
///    event raised inside a sim step would be stamped between two recorded frames and the play head could not land on
///    it exactly.
/// What polling CANNOT see is noted instead (<see cref="Note"/>): the pilot leaving, the crash the relay names, and a
/// failure an instructor or a lesson injects.
///
/// NOT TRIMMED WITH THE RING. <see cref="FlightRecorder"/> keeps the last ten minutes; this keeps the whole flight,
/// because an event the pilot can no longer WATCH is still one the debrief should TELL him about ("you bent that
/// wingtip twelve minutes ago"). The debrief marks which events the recording can still cue (<see cref="FlightDebrief"/>).
/// A cap of <see cref="MaxEvents"/> guards a flight that chatters; past it the oldest go, as the ring's do.
/// </summary>
public sealed class FlightEventLog
{
    /// <summary>The stalled fraction that counts as stalled, and the one that counts as flying again. 0.35 is the
    /// repo's existing line (PracticeScenario.Stalled, Syllabus FlightNow.Stalled: "StalledFraction > 0.35"), kept so
    /// the debrief calls a stall exactly what the lessons call one. The exit is lower so a wing hunting across the
    /// threshold is one stall and not twenty.</summary>
    public const double StalledEnter = 0.35, StalledExit = 0.20;

    /// <summary>What separates a spin from a wing drop: stalled AND yawing at least this fast (0.6 rad/s ≈ 34°/s)
    /// without a break for this long. A dropped wing at the stall yaws for a moment and stops; autorotation does not.
    /// The event is dated at the START of the dwell — the spin began when the rotation did, not two seconds later when
    /// it was certain.</summary>
    public const double SpinYawRateRadS = 0.6, SpinDwellSec = 2.0, SpinExitSec = 1.0;

    /// <summary>A touchdown and a lift-off must hold for this long to be logged, so one wheel chattering on a rough
    /// strip is not forty arrivals. A real bounce is far longer than this and is logged as the two touchdowns it is
    /// (an instructor counts bounces).</summary>
    public const double ContactDwellSec = 0.25;

    /// <summary>Hysteresis on the speed limits, m/s: back under the limit by this much before the condition ends.</summary>
    public const double SpeedHysteresisMs = 1.0;
    /// <summary>Hysteresis on the propeller's red line, rpm.</summary>
    public const double RpmHysteresis = 30.0;

    /// <summary>How many events one flight keeps.</summary>
    public const int MaxEvents = 1024;

    private readonly List<FlightEvent> _events = new();

    public int Count => _events.Count;
    public FlightEvent At(int i) => _events[i];
    public IReadOnlyList<FlightEvent> Events => _events;

    // ---- what the last Record() saw -------------------------------------------------------------------------------
    private bool _have;
    private bool _engineRunning, _touching, _fireL, _fireR, _leakL, _leakR, _stalled, _spinning, _overspeed, _propOver;
    private double _contactSince = double.NaN, _spinSince = double.NaN, _spinLostSince = double.NaN;
    /// <summary>The rpm at the last call: what the engine was turning at BEFORE it stopped (see <see cref="Engine"/>).</summary>
    private double _engineRpm;
    private readonly HashSet<AirframeComponent> _lost = new();
    private PanelState[] _panelState = System.Array.Empty<PanelState>();
    private double[] _panelFold = System.Array.Empty<double>();
    private readonly Dictionary<string, MemberWatch> _members = new();

    /// <summary>One overstress member's history between calls: the excursion it is inside, and what it was before.</summary>
    private sealed class MemberWatch
    {
        public int Overstresses;
        public double Strength = 1.0;
        public bool Broken;
        /// <summary>Inside an excursion: the largest effective ratio seen so far, the set it started from, and which
        /// way it is being bent. The member keeps these privately; polling them every frame reproduces them to within
        /// a frame, which is what lets the debrief quote the peak rather than the all-time peak.</summary>
        public bool InEvent;
        public double Peak, SetBefore;
        public int Sign;
    }

    public void Clear()
    {
        _events.Clear();
        _have = false;
        _lost.Clear();
        _members.Clear();
        _panelState = System.Array.Empty<PanelState>();
        _panelFold = System.Array.Empty<double>();
        _contactSince = _spinSince = _spinLostSince = double.NaN;
        _engineRunning = _touching = _fireL = _fireR = _leakL = _leakR = _stalled = _spinning = _overspeed = _propOver = false;
        _engineRpm = 0;
    }

    /// <summary>Add an event the polling cannot see (see <see cref="FlightEventKind.PilotOut"/>,
    /// <see cref="FlightEventKind.Crash"/>, <see cref="FlightEventKind.InjectedFailure"/>). Times never go backwards,
    /// whatever the caller's clock did — the debrief reads the list in order.</summary>
    public void Note(in FlightEvent e)
    {
        FlightEvent f = e;
        if (_events.Count > 0 && f.T < _events[^1].T)
            f = f.HasComponent
                ? new FlightEvent(_events[^1].T, f.Kind, f.Component, f.A, f.B, f.C, f.Detail, f.Flag)
                : new FlightEvent(_events[^1].T, f.Kind, f.A, f.B, f.C, f.Detail, f.Flag);
        _events.Add(f);
        if (_events.Count > MaxEvents) _events.RemoveAt(0);
    }

    private void Add(double t, FlightEventKind kind, double a = 0, double b = 0, double c = 0, string? detail = null,
        bool flag = false) => Note(new FlightEvent(t, kind, a, b, c, detail, flag));

    private void Add(double t, FlightEventKind kind, AirframeComponent comp, double a = 0, double b = 0, double c = 0,
        string? detail = null, bool flag = false) => Note(new FlightEvent(t, kind, comp, a, b, c, detail, flag));

    private const double R2D = 180.0 / System.Math.PI;

    /// <summary>
    /// Note everything that has changed about <paramref name="a"/> since the last call, at recorder-clock time
    /// <paramref name="t"/>. Call it once a display frame, from the same place the pose is recorded.
    /// </summary>
    public void Record(double t, Aircraft a)
    {
        if (a == null) return;
        bool first = !_have;
        _have = true;

        Engine(t, a, first);
        Overstresses(t, a, first);
        Panels(t, a, first);
        Losses(t, a, first);
        Stall(t, a, first);
        Fire(t, a, first);
        Limits(t, a, first);
        Contact(t, a, first);
    }

    // ---- the engine ----------------------------------------------------------------------------------------------
    private void Engine(double t, Aircraft a, bool first)
    {
        if (a.Config.Propulsion == null) return;            // a glider's engine never stops because it has none
        bool run = a.EngineRunning;
        if (!first && run != _engineRunning)
            // THE RPM OF A STOP IS THE LAST FRAME'S, not this one's. By the frame the stop is seen the engine is
            // already dead and EngineRpm is whatever the airflow is turning the propeller at — so reporting it
            // would have the debrief say "the engine stopped, turning 410 rpm", which is the number AFTER the thing
            // it is describing. A start is the other way round: the rpm now is the rpm it caught at.
            Add(t, run ? FlightEventKind.EngineStarted : FlightEventKind.EngineStopped,
                run ? a.EngineRpm : _engineRpm, 0, 0, run ? "" : StoppageCause(a));
        _engineRunning = run;
        _engineRpm = a.EngineRpm;
    }

    /// <summary>
    /// WHY the engine stopped, in the words the debrief may use — or EMPTY when the sim cannot say. Ty's bar for this
    /// feature was "where you can say WHY something happened, say it; where you cannot, do not guess", so this answers
    /// only from state that is UNAMBIGUOUS — the five conditions <see cref="Aircraft.EngineRunning"/> itself tests,
    /// read back in the order that makes the most specific one win (a lost nose has taken the fuel and the magnetos
    /// with it, so it must be answered before them).
    ///
    /// Anything else — a bullet through the cylinders, a prop strike's sudden stoppage, an instructor cutting the
    /// mixture by a route not modelled here — leaves this empty, and the debrief then says "nothing in the recording
    /// says why" rather than picking the most likely cause. A debrief that invents a cause is worse than one that
    /// lists facts.
    /// </summary>
    private static string StoppageCause(Aircraft a)
    {
        if (a.IsLost(AirframeComponent.Nose)) return "the engine is gone";
        if (a.IsLost(AirframeComponent.Propeller)) return "the propeller is gone";
        if (!a.FuelValveOpen) return "the fuel valve is shut";
        if (a.FuelStarved) return "fuel starvation";
        if (a.EngineStarved) return "the mixture is at idle cut-off";
        if (!FlyingGame.Core.Engine.Magnetos.Fires(a.MagLeft, a.MagRight)) return "both magnetos are off";
        return "";
    }

    // ---- g: what the structure booked ----------------------------------------------------------------------------
    private void Overstresses(double t, Aircraft a, bool first)
    {
        Watch(t, a.Structure.LeftWing.Member, first);
        Watch(t, a.Structure.RightWing.Member, first);
        if (a.Structure.HorizontalTail != null) Watch(t, a.Structure.HorizontalTail, first);
        if (a.Structure.EngineMount != null) Watch(t, a.Structure.EngineMount, first);
    }

    private void Watch(double t, OverstressMember m, bool first)
    {
        if (!_members.TryGetValue(m.Name, out MemberWatch? w))
        {
            w = new MemberWatch { Overstresses = m.Overstresses, Strength = m.Strength, Broken = m.Broken };
            _members[m.Name] = w;   // its baseline, not news: an aeroplane may spawn with a set already in it
        }

        // Follow the excursion while it runs, so the peak the debrief quotes is THIS pull's and not the flight's
        // largest. The member keeps the same number privately (_eventPeak) and books its set and its weakening from
        // it; polling every frame reproduces it to within a frame.
        if (m.Overstressed && !w.InEvent)
        {
            w.InEvent = true;
            w.Peak = m.EffectiveRatio;
            w.SetBefore = m.SetRad;
            w.Sign = m.LoadRatio >= 0 ? 1 : -1;
        }
        else if (w.InEvent) w.Peak = System.Math.Max(w.Peak, m.EffectiveRatio);

        if (m.Overstresses > w.Overstresses)
        {
            double before = w.Strength > 0 ? w.Strength : 1.0;
            double lost = System.Math.Max(0.0, 1.0 - m.Strength / before);
            // The excursion's own peak, not the flight's: an aeroplane that was pulled to 1.8 earlier must not have
            // this pull reported at 1.8. (w.Peak is this excursion's; m.PeakEffectiveRatio is the fallback for an
            // excursion that began and ended inside one frame, which only a very long frame can do.)
            double peak = System.Math.Max(w.InEvent ? w.Peak : m.PeakEffectiveRatio, OverstressMember.UltimateRatio);
            int sign = w.InEvent ? w.Sign : (m.SetRad >= 0.0 ? 1 : -1);
            Add(t, FlightEventKind.Overstress, peak * sign, lost,
                m.SetRad * R2D, m.Name, OverstressMember.Straightening(w.SetBefore, w.Sign));
            w.InEvent = false;
        }
        else if (!m.Overstressed) w.InEvent = false;

        if (m.Broken && !w.Broken)
            Add(t, FlightEventKind.StructureBroke,
                System.Math.Max(w.Peak, m.PeakEffectiveRatio) * (m.LoadRatio >= 0.0 ? 1 : -1), 0, 0, m.Name);

        w.Overstresses = m.Overstresses; w.Strength = m.Strength; w.Broken = m.Broken;
    }

    // ---- wing panels: the set they keep, and the fold -------------------------------------------------------------
    private void Panels(double t, Aircraft a, bool first)
    {
        IReadOnlyList<WingPanel> panels = a.Panels;
        if (_panelState.Length != panels.Count)
        {
            _panelState = new PanelState[panels.Count];
            _panelFold = new double[panels.Count];
            for (int i = 0; i < panels.Count; i++) { _panelState[i] = panels[i].State; _panelFold[i] = panels[i].FoldRad; }
            return;                                          // a new aeroplane: this is its baseline, not news
        }
        for (int i = 0; i < panels.Count; i++)
        {
            WingPanel p = panels[i];
            if (!first && p.State != _panelState[i])
            {
                if (p.State == PanelState.Bent)
                    Add(t, FlightEventKind.PanelBent, p.Component, p.BendRad * R2D, p.TwistRad * R2D);
                else if (p.State == PanelState.Broken)
                    Add(t, FlightEventKind.PanelBroken, p.Component, p.BendRad * R2D, p.TwistRad * R2D);
            }
            // The fold is reported once, at WingStructure's own visible threshold: it only ever grows, so a second
            // entry would be the same wing folding further, which the shape timeline already draws.
            if (!first && p.FoldRad >= WingStructure.VisibleSetRad && _panelFold[i] < WingStructure.VisibleSetRad)
                Add(t, FlightEventKind.WingFolded, p.Component, p.FoldRad * R2D);
            _panelState[i] = p.State; _panelFold[i] = p.FoldRad;
        }
    }

    // ---- parts that are gone -------------------------------------------------------------------------------------
    private void Losses(double t, Aircraft a, bool first)
    {
        // An aeroplane that SPAWNED damaged (a scenario, a recorded ship) has its losses taken as the baseline on the
        // first call: they are the state it started in, not things that happened during this flight.
        foreach (AirframeComponent c in a.LostComponents)
            if (_lost.Add(c) && !first) Add(t, FlightEventKind.ComponentLost, c);
    }

    // ---- the stall, and the spin ---------------------------------------------------------------------------------
    private void Stall(double t, Aircraft a, bool first)
    {
        double frac = a.StalledFraction;
        bool stalled = _stalled ? frac > StalledExit : frac >= StalledEnter;
        if (!first && stalled != _stalled)
            Add(t, stalled ? FlightEventKind.StallBegan : FlightEventKind.StallEnded, frac);
        _stalled = stalled;

        double r = a.State.Rates.Z;
        bool rotating = stalled && System.Math.Abs(r) >= SpinYawRateRadS;
        if (!_spinning)
        {
            if (!rotating) _spinSince = double.NaN;
            else
            {
                if (double.IsNaN(_spinSince)) _spinSince = t;
                if (t - _spinSince >= SpinDwellSec && !first)
                {
                    // Dated at the start of the rotation, with the rate it had THEN — which is the honest answer to
                    // "when did the spin begin".
                    _spinning = true; _spinLostSince = double.NaN;
                    Add(_spinSince, FlightEventKind.SpinBegan, r * R2D);
                }
            }
        }
        else
        {
            if (rotating) _spinLostSince = double.NaN;
            else
            {
                if (double.IsNaN(_spinLostSince)) _spinLostSince = t;
                if (t - _spinLostSince >= SpinExitSec)
                {
                    _spinning = false; _spinSince = double.NaN;
                    Add(_spinLostSince, FlightEventKind.SpinEnded, r * R2D);
                }
            }
        }
    }

    // ---- fire and fuel ------------------------------------------------------------------------------------------
    private void Fire(double t, Aircraft a, bool first)
    {
        FlyingGame.Core.Combat.DamageState d = a.Damage;
        if (d == null) return;
        Flag(t, first, d.FireLeft, ref _fireL, FlightEventKind.FireBegan, FlightEventKind.FireEnded, AirframeComponent.WingLeftInner);
        Flag(t, first, d.FireRight, ref _fireR, FlightEventKind.FireBegan, FlightEventKind.FireEnded, AirframeComponent.WingRightInner);
        // A leak has no "ended": the hole does not close. It stops being DRAWN when the fire takes over
        // (DamageFxState.LeakDrawnLeft), which is a view rule and not a thing that happened.
        if (!first && d.FuelLeakLeft && !_leakL) Add(t, FlightEventKind.FuelLeakBegan, AirframeComponent.WingLeftInner);
        if (!first && d.FuelLeakRight && !_leakR) Add(t, FlightEventKind.FuelLeakBegan, AirframeComponent.WingRightInner);
        _leakL = d.FuelLeakLeft; _leakR = d.FuelLeakRight;
    }

    private void Flag(double t, bool first, bool now, ref bool was, FlightEventKind began, FlightEventKind ended,
        AirframeComponent comp)
    {
        if (!first && now != was) Add(t, now ? began : ended, comp);
        was = now;
    }

    // ---- the speed limits ----------------------------------------------------------------------------------------
    private void Limits(double t, Aircraft a, bool first)
    {
        double alt = -a.State.Position.Z;
        double ias = FlightReading.IasFromTas(a.State.Velocity.Length, alt);
        double vne = a.Config.Limits.VneAt(alt);
        bool over = _overspeed ? ias > vne - SpeedHysteresisMs : ias > vne;
        if (!first && over != _overspeed)
            Add(t, over ? FlightEventKind.OverspeedBegan : FlightEventKind.OverspeedEnded, vne);
        _overspeed = over;

        PropulsionConfig? pr = a.Config.Propulsion;
        if (pr == null || pr.PropDiameterM <= 0 || pr.MaxRpm <= 0) { _propOver = false; return; }
        double red = pr.MaxRpm;
        double rpm = a.EngineRpm;
        bool pOver = _propOver ? rpm > red - RpmHysteresis : rpm > red;
        if (!first && pOver != _propOver)
            Add(t, pOver ? FlightEventKind.PropOverspeedBegan : FlightEventKind.PropOverspeedEnded, red, 0, 0,
                pOver ? (a.EngineRunning ? "under power" : "the airflow is driving it, with the engine dead") : "");
        _propOver = pOver;
    }

    // ---- the ground ---------------------------------------------------------------------------------------------
    private void Contact(double t, Aircraft a, bool first)
    {
        bool touching = a.AnyGearTouching;
        if (first) { _touching = touching; _contactSince = double.NaN; return; }
        if (touching == _touching) { _contactSince = double.NaN; return; }
        if (double.IsNaN(_contactSince)) { _contactSince = t; return; }
        if (t - _contactSince < ContactDwellSec) return;
        // Dated at the first frame of the change, not at the end of the dwell: the wheels touched when they touched.
        if (touching)
        {
            RigidBodyState s = a.State;
            double sink = s.Attitude.Rotate(s.Velocity).Z;               // world +z down
            Add(_contactSince, FlightEventKind.Touchdown, sink, FlightReading.SideslipDeg(s), FlightReading.PitchDeg(s));
        }
        else Add(_contactSince, FlightEventKind.Liftoff);
        _touching = touching;
        _contactSince = double.NaN;
    }
}

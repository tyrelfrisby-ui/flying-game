using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>Which kind of canopy latch a type has, for the grab system to offer.</summary>
    public enum CanopyLatchKind
    {
        /// <summary>No latch to grab: an open cockpit (Great Lakes), a door (Cub, 172), or not built yet.</summary>
        None = 0,
        /// <summary>The Pitts S-2B (v2, Ty 2026-11-05): a black lever at each cockpit's left canopy frame, joined by a cable;
        /// pushed FORWARD = latched.</summary>
        CableLever = 1,
        /// <summary>The Extra 300: a fixed rear handle and a spring-loaded front handle on the canopy frame; squeezing them
        /// together draws the latch pins (v2, Ty 2026-11-02).</summary>
        Squeeze = 2,
    }

    /// <summary>The state every latch shows the rest of the game: locked or not, and the rig's `canopy_latch`
    /// param (0 locked .. 1 fully open / squeezed, CONTRACT.md). Phase 2's canopy opening reads <see cref="Locked"/>.</summary>
    public interface ICanopyLatch
    {
        CanopyLatchKind Kind { get; }
        bool Locked { get; }
        double Latch01 { get; }
        /// <summary>A fresh aeroplane: closed and locked.</summary>
        void Reset();
    }

    /// <summary>The two Pitts latch levers, one per cockpit, joined by the cable.</summary>
    public enum PittsLatchLever { Front = 0, Rear = 1 }

    /// <summary>
    /// THE PITTS S-2B CANOPY LATCH, v2 (Ty 2026-11-05: "a BLACK canopy latch at the forward-left corner of the canopy,
    /// left of the front pilot's head. Push FORWARD = latched, pull BACK = unlatched. Lever ~2 in long, ~90 deg throw. A
    /// cable runs from it along the bottom of the left canopy sill, between the two curved tubes, to the black lever the
    /// rear pilot operates"). Both levers and the cable are on the CANOPY frame; the hook they work engages the keeper
    /// on the fuselage's left sill.
    ///
    ///   lever    θ, degrees back from full FORWARD: 0 latched .. <see cref="ThrowDeg"/> unlatched. The cable makes it ONE
    ///            angle: whichever lever a hand moves, the other follows (the rig's `canopy_latch` = θ / throw drives both).
    ///   held     θ follows the hand's angle about the lever's pivot (the Bridge's HingedLever, 1:1); two hands on the two
    ///            levers: the last one to close on its lever drives (the cable cannot be in two places).
    ///   detent   the latched end is OVER CENTRE: within <see cref="DetentCaptureDeg"/> of forward it snaps home (a click)
    ///            and stays until the hand pulls it <see cref="DetentBreakoutDeg"/> back. That breakout is the over-centre
    ///            spring in hand travel, since a controller has no force to measure.
    ///   released it stays where it was left (cable friction), except that inside the capture it snaps home.
    ///   Locked   = hook home AND canopy shut: a hook pushed forward with the canopy slid back or open swings in free air,
    ///            and closing the canopy onto it rides the keeper's ramp and locks (same rule as the Extra's sill).
    ///
    /// Numbers: arm 2 in = 0.0508 m and throw 90° (Ty); capture 6° and breakout 12° are estimates (E) of an over-centre
    /// latch's feel, about 5 and 11 mm at the knob. Tune on the headset.
    /// </summary>
    public sealed class PittsCanopyLatch : ICanopyLatch
    {
        public const double ThrowDeg = 90.0, ArmM = 0.0508, DetentCaptureDeg = 6.0, DetentBreakoutDeg = 12.0;

        public CanopyLatchKind Kind => CanopyLatchKind.CableLever;
        /// <summary>Both levers' angle, degrees back from full forward.</summary>
        public double LeverDeg { get; private set; }
        /// <summary>The hook is in its over-centre detent (lever home, forward).</summary>
        public bool HookHome { get; private set; } = true;
        public bool Locked { get; private set; } = true;
        public double Latch01 => LeverDeg / ThrowDeg;
        /// <summary>The lever a hand is driving, or null.</summary>
        public PittsLatchLever? HeldBy { get; private set; }

        private bool _shut = true;

        public void Reset() { LeverDeg = 0.0; HookHome = true; Locked = true; HeldBy = null; _shut = true; }

        /// <summary>A hand closes on one lever: from now on that lever (and through the cable the other) follows it.</summary>
        public void Grab(PittsLatchLever which) => HeldBy = which;

        /// <summary>That hand lets go (a hand letting go of the OTHER lever changes nothing).</summary>
        public void Release(PittsLatchLever which) { if (HeldBy == which) HeldBy = null; }

        /// <summary>One frame of a held lever. <paramref name="handDeg"/>: where the hand puts that lever, degrees back
        /// from full forward (the Bridge's HingedLever.Follow × throw, grab offset included). Ignored unless
        /// <paramref name="which"/> is the lever being driven. Returns true on the frame the hook snaps home (a click).</summary>
        public bool Follow(PittsLatchLever which, double handDeg)
        {
            if (HeldBy != which) return false;
            double want = Math.Clamp(handDeg, 0.0, ThrowDeg);
            bool click = false;
            if (HookHome)
            {
                if (want < DetentBreakoutDeg) { LeverDeg = 0.0; Refresh(); return false; }
                HookHome = false;
                LeverDeg = want;
            }
            else if (want <= DetentCaptureDeg) { LeverDeg = 0.0; HookHome = true; click = true; }
            else LeverDeg = want;
            Refresh();
            return click;
        }

        /// <summary>Every frame, with the canopy's state BEFORE this frame's canopy step (CanopyDynamics.Shut). Returns true
        /// when <see cref="Locked"/> changes.</summary>
        public bool Update(bool canopyShut)
        {
            bool was = Locked;
            _shut = canopyShut;
            Refresh();
            return was != Locked;
        }

        private void Refresh() => Locked = HookHome && _shut;
    }

    /// <summary>The hands on the Extra's latch this frame. Positions are along the canopy frame's AFT axis, metres, any
    /// origin (the frame runs fore-aft on the hinge axis, so it is the cockpit's −z whether the canopy is shut or open).
    /// A controller has no fingers to measure, so "one hand spanning both handles" is the PAIR grab, squeezed with the
    /// trigger; Phase 3's tracked hands can fill <see cref="PairSqueeze"/> from the pinch instead.</summary>
    public struct ExtraLatchHands
    {
        /// <summary>One hand on the pair (between the handles); <see cref="PairSqueeze"/> 0..1 is its trigger.</summary>
        public bool PairHeld;
        public double PairSqueeze;
        /// <summary>A hand closed on the FRONT handle (the one that moves), and where it is along the frame.</summary>
        public bool FwdHeld;
        public double FwdAftM;
        /// <summary>A hand closed on the REAR handle (fixed to the frame), and where it is along the frame.</summary>
        public bool AftHeld;
        public double AftAftM;
    }

    /// <summary>
    /// THE EXTRA 300 CANOPY LATCH, v2 (Ty, 2026-11-02 12:29: "The two handles are on the canopy frame, one in front of
    /// the other. The rear handle is fixed. The front handle is spring-loaded forward. Squeezing them together slides
    /// the front handle aft toward the rear one, and that motion pulls the latch pins out of their holes in the
    /// airframe"). Both handles and the pins are on the CANOPY (they open with it); the holes are in the sill.
    ///
    ///   s        the front handle's position, 0 forward on its spring stop (pins IN) .. 1 fully aft (pins OUT);
    ///            the rig's `canopy_latch` = s drives Codex's Fwd handle (35 mm) and the pins (18 mm up).
    ///   held     the hand is far stiffer than the spring, so a held handle is where the hand puts it:
    ///              pair + trigger      s = trigger
    ///              front AND rear      s = s₀ + (separation₀ − separation) / travel   (the hands close together)
    ///              front only          s = s₀ + (aft travel of that hand) / travel     (it is the moving part)
    ///              rear only           nothing: that handle is fixed to the frame
    ///   released a mass on a preloaded spring with Coulomb friction, against the forward stop (no bounce):
    ///              m ẍ = −(F₀ + k x) − F_f sign(ẋ)
    ///   pins     retract linearly with s (18 mm over the 35 mm stroke). They are OUT of their holes once they have
    ///            been drawn the engagement depth plus a clearance: s ≥ <see cref="PinsClearAt"/>. Below that the
    ///            canopy is still pinned, however hard the handle is squeezed.
    ///   sill     with the canopy NOT shut the pins cannot engage: lifted off the sill they hang in free air, and in the
    ///            last <see cref="NearSillDeg"/> before it shuts their bevelled tips ride on the sill, held up at the
    ///            clear position (so the handle is pushed aft too). Shut and aligned, the spring snaps them in: LOCKED.
    ///   Locked   = pins engaged = canopy shut AND s &lt; PinsClearAt. <see cref="CanopyDynamics"/> will not move a
    ///            canopy whose pins are engaged, by hand or by airload.
    ///
    /// Numbers. Travel 35 mm, pin stroke 18 mm: Codex's rig (E). Everything else is my estimate for Ty:
    ///   spring preload 20 N at rest, rate 600 N/m → 41 N at full squeeze (a firm one-hand squeeze, not a fight);
    ///   moving mass 0.08 kg (handle, push-rods, two pins) and 2 N of friction → it snaps home in about 13 ms;
    ///   pin engagement 14 mm + 0.5 mm clearance → the pins clear at 14.5 / 18 = 0.806 of the squeeze. NB Codex's drawn
    ///   sockets show about 6 mm of engagement, which would clear at a third of the squeeze — the contract asks for
    ///   "pins out only near full squeeze", so the physics uses 14 mm and the drawing is a question (docs).
    /// </summary>
    public sealed class ExtraCanopyLatch : ICanopyLatch
    {
        public const double TravelM = 0.035, PinStrokeM = 0.018, PinEngagementM = 0.014, PinClearanceM = 0.0005;
        public const double SpringPreloadN = 20.0, SpringRateNPerM = 600.0, MovingMassKg = 0.08, FrictionN = 2.0;
        /// <summary>Within this of shut the extended pins' tips are on the sill (E: 14 mm of pin at the 0.73 m free
        /// edge is 1.1°).</summary>
        public const double NearSillDeg = 1.5;
        /// <summary>Handle centres ±45 mm either side of the pair's grip (Codex).</summary>
        public const double HandleSpacingM = 0.090;
        /// <summary>s at which the pins are out of their holes: (14 + 0.5) / 18 = 0.80556.</summary>
        public const double PinsClearAt = (PinEngagementM + PinClearanceM) / PinStrokeM;

        public CanopyLatchKind Kind => CanopyLatchKind.Squeeze;
        /// <summary>The front handle, 0 forward (pins in) .. 1 aft (pins out).</summary>
        public double S { get; private set; }
        /// <summary>Its speed, m/s, + aft.</summary>
        public double SpeedMs { get; private set; }
        public bool Locked { get; private set; } = true;
        /// <summary>The pins' tips are on the sill (canopy all but shut, pins extended).</summary>
        public bool RidingSill { get; private set; }
        public double Latch01 => S;
        public double PinRetractM => S * PinStrokeM;
        public bool PinsClear => S >= PinsClearAt;
        /// <summary>Some hand is moving the front handle (pair, or the front handle itself).</summary>
        public bool Driven { get; private set; }

        private int _set;
        private double _s0, _fwd0, _sep0;

        public void Reset() { S = 0.0; SpeedMs = 0.0; Locked = true; RidingSill = false; Driven = false; _set = 0; }

        /// <summary>
        /// One frame. <paramref name="canopyShut"/> / <paramref name="canopyAngleDeg"/>: the canopy's state BEFORE this
        /// frame's canopy step (CanopyDynamics.Shut, .AngleDeg). Returns true on the frame the pins come out (unlock
        /// click) or snap in (lock click).
        /// </summary>
        public bool Step(double dt, in ExtraLatchHands h, bool canopyShut, double canopyAngleDeg)
        {
            bool was = Locked;
            // Re-take the references whenever the set of hands changes, so a new grip never jumps the handle.
            int set = (h.PairHeld ? 1 : 0) | (h.FwdHeld ? 2 : 0) | (h.AftHeld ? 4 : 0);
            if (set != _set) { _set = set; _s0 = S; _fwd0 = h.FwdAftM; _sep0 = h.AftAftM - h.FwdAftM; }

            double want = double.NegativeInfinity;
            if (h.PairHeld) want = Math.Clamp(h.PairSqueeze, 0.0, 1.0);
            if (h.FwdHeld && h.AftHeld) want = Math.Max(want, _s0 + (_sep0 - (h.AftAftM - h.FwdAftM)) / TravelM);
            else if (h.FwdHeld) want = Math.Max(want, _s0 + (h.FwdAftM - _fwd0) / TravelM);
            Driven = !double.IsNegativeInfinity(want);

            if (Driven) { S = Math.Clamp(want, 0.0, 1.0); SpeedMs = 0.0; }
            else if (dt > 0.0) Spring(dt);

            // The sill holds the extended pins up while the canopy comes down onto it.
            RidingSill = !canopyShut && canopyAngleDeg < NearSillDeg && S < PinsClearAt;
            if (RidingSill) { S = PinsClearAt; SpeedMs = 0.0; }

            Locked = canopyShut && S < PinsClearAt;
            return was != Locked;
        }

        /// <summary>The front handle on its spring, in 0.5 ms steps (it is a stiff little system: ω = √(k/m) = 87 rad/s).</summary>
        private void Spring(double dt)
        {
            double x = S * TravelM, v = SpeedMs;
            int n = Math.Max(1, (int)Math.Ceiling(dt / 0.0005));
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                double f = -(SpringPreloadN + SpringRateNPerM * x);
                if (Math.Abs(v) > 1e-9) f -= FrictionN * Math.Sign(v);
                else if (Math.Abs(f) <= FrictionN) f = 0.0;   // stuck: the spring cannot beat the friction
                else f += FrictionN;                          // breaking away forward
                v += f / MovingMassKg * h;
                x += v * h;
                if (x <= 0.0) { x = 0.0; v = 0.0; }          // the forward stop: no bounce
            }
            S = x / TravelM;
            SpeedMs = v;
        }
    }
}

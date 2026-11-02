using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// One frame of what a Meta Quest's two Touch controllers (or tracked hands) report, already read off the
    /// device by the Bridge. Thumbsticks are −1..1 with +x to the RIGHT and +y AWAY from the pilot (pushed
    /// forward); triggers and grips 0..1.
    /// </summary>
    public struct QuestControllerFrame
    {
        public double LeftX, LeftY, RightX, RightY;
        public double LeftTrigger, RightTrigger, LeftGrip, RightGrip;
        public bool A, B, X, Y, LeftStickClick, RightStickClick, Menu;

        /// <summary>The left controller's position along the SEAT's fore-aft axis, metres, forward positive —
        /// the throttle hand. Only differences matter, so any origin will do.</summary>
        public double LeftHandForwardM;

        /// <summary>Hand tracking (no controllers): pinch strength 0..1 per hand.</summary>
        public double LeftPinch, RightPinch;
        public bool HandsOnly;
    }

    /// <summary>What the frame asks the aeroplane to do. Axes in the game's own pad convention, so the Bridge can
    /// hand them to the same controls the touch pads and a joystick feed (TouchFlightControls): aileron right
    /// positive, elevator PUSH positive (nose down), rudder right positive.</summary>
    public struct QuestFlightCommand
    {
        public double Aileron, Elevator, Rudder;

        /// <summary>The throttle lever, 0 idle … 1 full (on a glider: 0 spoilers out … 1 stowed, exactly as the
        /// touch pad's upper band). Valid only when <see cref="ThrottleMoved"/>; otherwise the lever is wherever
        /// it was left, which is what makes it a lever.</summary>
        public double Throttle01;
        public bool ThrottleMoved;

        /// <summary>Wheel brakes 0..1, held.</summary>
        public double Brake;

        /// <summary>Pitch-trim rate in trim-slider units per second (TouchFlightControls._pitchTrim): positive is
        /// nose DOWN, the same sense as the slider and a joystick's TRIM DOWN.</summary>
        public double PitchTrimRate;

        /// <summary>One-shot presses (true on the frame the button goes down).</summary>
        public bool FlapsDown, FlapsUp, Gear, Smoke, Recentre, Menu;

        /// <summary>A + B held together for a second: start / stop the "what I see" recording (QuestRecordChord).</summary>
        public bool Record;

        /// <summary>Held: guns, while the trigger is pulled.</summary>
        public bool Fire;

        /// <summary>True when the right stick is out of its dead zone, i.e. the pilot's hand is on the stick. When
        /// false the stick is "let go" — on a reversible aeroplane the surfaces then trail, as with a lifted thumb.</summary>
        public bool StickHeld, RudderHeld;
    }

    /// <summary>
    /// THE QUEST CONTROL MAPPING, engine-free so it is tested without a headset (QuestControlsTests).
    ///
    /// Right hand flies, left hand runs the engine — the way a real pilot's hands are split:
    ///   right stick       aileron (X) and elevator (Y; forward = push = nose down, flipped by InvertElevator)
    ///   right trigger     guns (only where the guns are hot; the Bridge decides)
    ///   right grip        smoke on / off
    ///   A / B (held)      pitch trim nose UP / nose DOWN (B sits further from the thumb: "forward" = nose down)
    ///   right stick click gear up / down (glider: aerotow / release)
    ///   left stick X      rudder
    ///   left stick Y      THROTTLE LEVER, as a rate: push to open, pull to close, let go and it STAYS
    ///   left grip + move  grab the lever: squeeze, move the hand forward / back, let go — 20 cm is full travel
    ///   left trigger      wheel brakes (rudder biases them, as on the touch pad)
    ///   X / Y             flaps one step down / up
    ///   left stick click  recentre the head at the seat's eye point; HELD 1 s it also opens / closes the menu
    ///   menu (☰)          the VR menu, which pauses
    ///   A + B held 1 s    start / stop recording what the headset shows (QuestRecordChord: the two trims cancel)
    ///
    /// The held stick click is the fallback for the menu button (owner Ty, 2026-11-02: "i couldn't figure out how
    /// to get to options"). Every other button already flies something, and the click's own job (recentre) is
    /// harmless to repeat, so holding it costs nothing in the air.
    ///
    /// The throttle is NEVER sprung. A thumbstick returns to centre, so its Y is a RATE: the lever moves while the
    /// stick is held and stays where it was left. One stick carries both rudder and throttle, so whichever axis
    /// is clearly dominant wins — a bootful of rudder with the thumb wandering 0.3 forward does not open the
    /// throttle, and an easing of power does not kick the rudder.
    ///
    /// DEAD ZONES HERE ARE THE HARDWARE'S ONLY: <see cref="StickDeadZone"/> is for thumbstick drift, rescaled so
    /// full deflection is still 1. The aeroplane's own dead zone and expo (AircraftConfig, applied once by
    /// Aircraft.ShapeAxis) come on top, exactly as for the touch pads, which pass the raw stick — stacking a
    /// second curve here is the bug TouchFlightControls already fixed once.
    /// </summary>
    public sealed class QuestControlMapper
    {
        public const double StickDeadZone = 0.10;
        /// <summary>The throttle needs a wider zone than the stick: it is a rate, and drift would creep it.</summary>
        public const double ThrottleDeadZone = 0.25;
        /// <summary>Lever travel per second at full stick: idle to full in 2 s.</summary>
        public const double ThrottleRatePerSec = 0.5;
        /// <summary>Hand travel for the full lever when it is grabbed with the grip.</summary>
        public const double LeverTravelM = 0.20;
        /// <summary>Pitch trim, slider units per second — the same as a joystick's TRIM buttons.</summary>
        public const double TrimRatePerSec = 0.5;
        /// <summary>Below this the OTHER axis of a shared stick may not move its control.</summary>
        public const double DominanceFloor = 0.5;
        /// <summary>Squeeze thresholds, with hysteresis so a grip held at the edge does not chatter.</summary>
        public const double GripOn = 0.6, GripOff = 0.4;
        public const double TriggerFire = 0.5;
        /// <summary>Hands only: a pinch held this long opens the menu (left) or recentres (right), so the ordinary
        /// pinches of a hand resting on a knee do not.</summary>
        public const double PinchHoldSec = 1.0;
        public const double PinchOn = 0.8, PinchOff = 0.5;
        /// <summary>The left stick click held this long opens (or closes) the menu, as well as ☰.</summary>
        public const double MenuHoldSec = 1.0;

        public bool InvertElevator;

        private QuestControllerFrame _prev;
        private bool _havePrev;
        private bool _leverGrabbed, _rightGrip;
        private double _grabHandM, _grabThrottle01;
        private bool _leftPinch, _rightPinch;
        private double _leftPinchHeld, _rightPinchHeld;
        private bool _leftPinchFired, _rightPinchFired;
        private double _leftClickHeld;
        private bool _leftClickFired;
        private readonly QuestRecordChord _record = new();

        /// <summary>Thumbstick drift removed, rescaled so the edge is still 1.</summary>
        public static double DeadZone(double v, double zone)
        {
            double a = Math.Abs(v);
            if (a <= zone) return 0.0;
            return Math.Sign(v) * Math.Min(1.0, (a - zone) / (1.0 - zone));
        }

        /// <summary>
        /// One frame. <paramref name="throttle01"/> is where the lever is NOW (the game owns it: a reset or an
        /// aeroplane change moves it); the command says where it should go, if anywhere.
        /// </summary>
        public QuestFlightCommand Step(in QuestControllerFrame f, double dt, double throttle01)
        {
            var c = new QuestFlightCommand();
            QuestControllerFrame p = _havePrev ? _prev : default;
            bool Down(bool now, bool was) => now && !was;

            if (f.HandsOnly)
            {
                // Tracked hands cannot hold a stick or a lever with any precision, so they do not fly: they open
                // the menu (left pinch, held) and recentre (right pinch, held). The aeroplane keeps whatever the
                // controls were left at.
                c.Menu = PinchHold(f.LeftPinch, dt, ref _leftPinch, ref _leftPinchHeld, ref _leftPinchFired);
                c.Recentre = PinchHold(f.RightPinch, dt, ref _rightPinch, ref _rightPinchHeld, ref _rightPinchFired);
                c.Throttle01 = throttle01;
                _leverGrabbed = false;
                _record.Step(false, false, dt);
                _prev = f; _havePrev = true;
                return c;
            }
            _leftPinch = _rightPinch = false; _leftPinchHeld = _rightPinchHeld = 0;

            // ---- right hand: the stick ----
            double ax = DeadZone(f.RightX, StickDeadZone), ay = DeadZone(f.RightY, StickDeadZone);
            c.Aileron = ax;
            c.Elevator = InvertElevator ? -ay : ay;
            c.StickHeld = ax != 0.0 || ay != 0.0;

            // ---- left hand: rudder and the throttle lever share one stick ----
            double rx = DeadZone(f.LeftX, StickDeadZone);
            double ty = DeadZone(f.LeftY, ThrottleDeadZone);
            bool rudderDominant = Math.Abs(f.LeftX) >= Math.Abs(f.LeftY);
            if (!rudderDominant && Math.Abs(f.LeftX) < DominanceFloor) rx = 0.0;
            if (rudderDominant && Math.Abs(f.LeftY) < DominanceFloor) ty = 0.0;
            c.Rudder = rx;
            c.RudderHeld = rx != 0.0;

            double lever = Clamp01(throttle01);
            bool moved = false;

            // Grab the lever with the grip: from the moment of the squeeze, hand travel is lever travel.
            bool grip = _leverGrabbed ? f.LeftGrip > GripOff : f.LeftGrip >= GripOn;
            if (grip && !_leverGrabbed) { _grabHandM = f.LeftHandForwardM; _grabThrottle01 = lever; }
            _leverGrabbed = grip;
            if (grip)
            {
                double want = Clamp01(_grabThrottle01 + (f.LeftHandForwardM - _grabHandM) / LeverTravelM);
                if (Math.Abs(want - lever) > 1e-9) { lever = want; moved = true; }
            }
            else if (ty != 0.0)
            {
                lever = Clamp01(lever + ty * ThrottleRatePerSec * dt);
                moved = true;
            }
            c.Throttle01 = lever;
            c.ThrottleMoved = moved;

            c.Brake = Clamp01(f.LeftTrigger);

            // ---- buttons ----
            c.PitchTrimRate = (f.B ? TrimRatePerSec : 0.0) - (f.A ? TrimRatePerSec : 0.0);
            c.FlapsDown = Down(f.X, p.X);
            c.FlapsUp = Down(f.Y, p.Y);
            c.Gear = Down(f.RightStickClick, p.RightStickClick);
            c.Recentre = Down(f.LeftStickClick, p.LeftStickClick);
            c.Menu = Down(f.Menu, p.Menu) | ClickHold(f.LeftStickClick, dt);
            c.Record = _record.Step(f.A, f.B, dt);
            bool rg = _rightGrip ? f.RightGrip > GripOff : f.RightGrip >= GripOn;
            c.Smoke = rg && !_rightGrip;
            _rightGrip = rg;
            c.Fire = f.RightTrigger >= TriggerFire;

            _prev = f; _havePrev = true;
            return c;
        }

        /// <summary>A pinch that fires once after being held <see cref="PinchHoldSec"/>, and not again until it is
        /// let go.</summary>
        private static bool PinchHold(double strength, double dt, ref bool on, ref double held, ref bool fired)
        {
            on = on ? strength > PinchOff : strength >= PinchOn;
            if (!on) { held = 0; fired = false; return false; }
            held += dt;
            if (fired || held < PinchHoldSec) return false;
            fired = true;
            return true;
        }

        /// <summary>The left stick click, held <see cref="MenuHoldSec"/>: fires once, then not again until let go.</summary>
        private bool ClickHold(bool down, double dt)
        {
            if (!down) { _leftClickHeld = 0; _leftClickFired = false; return false; }
            _leftClickHeld += dt;
            if (_leftClickFired || _leftClickHeld < MenuHoldSec) return false;
            _leftClickFired = true;
            return true;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}

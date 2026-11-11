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

        /// <summary>Wheel brakes 0..1, held (both wheels). Nothing on the Touch sets it since 2026-11-04; kept for callers.</summary>
        public double Brake;
        /// <summary>LEFT / RIGHT wheel (toe) brakes, 0..1: the left / right trigger, proportional (Ty 2026-11-04).</summary>
        public double BrakeLeft, BrakeRight;

        /// <summary>Pitch-trim rate in trim-slider units per second (TouchFlightControls._pitchTrim): positive is
        /// nose DOWN, the same sense as the slider and a joystick's TRIM DOWN.</summary>
        public double PitchTrimRate;

        /// <summary>One-shot presses (true on the frame the button goes down).</summary>
        public bool FlapsDown, FlapsUp, Gear, Smoke, Recentre, Menu;

        /// <summary>Which of the menu's controller paths fired <see cref="Menu"/> this frame, for the [QuestInput] log.</summary>
        public QuestMenuTrigger MenuBy;

        /// <summary>A + B held together for a second: start / stop the "what I see" recording (QuestRecordChord).</summary>
        public bool Record;

        /// <summary>Held: guns, while the trigger is pulled.</summary>
        public bool Fire;

        /// <summary>True when the right stick is out of its dead zone, i.e. the pilot's hand is on the stick. When
        /// false the stick is "let go" — on a reversible aeroplane the surfaces then trail, as with a lifted thumb.</summary>
        public bool StickHeld, RudderHeld;
    }

    /// <summary>What opened (or closed) the VR menu. The first three come from <see cref="QuestControlMapper"/>; the
    /// MENU tab's two are <see cref="QuestMenuTab"/>'s.</summary>
    public enum QuestMenuTrigger { None, MenuButton, StickClickHeld, LeftPinchHeld, TabPoked, TabPointedTrigger }

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
        /// <summary>Thumbstick drift, removed and rescaled. 0.10 until 2026-11-02, when Ty found the stick "far too
        /// sensitive, especially around neutral": the cure for that is the curve (<see cref="QuestStickFeel"/>), and a
        /// wide dead band in front of a gentle curve only moves the jump outwards. 0.05 is the small dead zone of the
        /// requested feel; the Touch controllers' and a gamepad's rest drift sit inside it.</summary>
        public const double StickDeadZone = 0.05;
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
                if (c.Menu) c.MenuBy = QuestMenuTrigger.LeftPinchHeld;
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

            // Triggers = toe brakes, left wheel / right wheel (Ty 2026-11-04). They no longer select in the menu or
            // fire the guns: the menu is the ☰ button + sticks + A/B, the guns are the right grip.
            var (bl, br) = GamepadShaping.TriggerBrakes(f.LeftTrigger, f.RightTrigger);
            c.BrakeLeft = bl; c.BrakeRight = br;

            // ---- buttons ----
            c.PitchTrimRate = (f.B ? TrimRatePerSec : 0.0) - (f.A ? TrimRatePerSec : 0.0);
            c.FlapsDown = Down(f.X, p.X);
            c.FlapsUp = Down(f.Y, p.Y);
            c.Gear = Down(f.RightStickClick, p.RightStickClick);
            c.Recentre = Down(f.LeftStickClick, p.LeftStickClick);
            bool button = Down(f.Menu, p.Menu), held = ClickHold(f.LeftStickClick, dt);
            c.Menu = button | held;
            c.MenuBy = button ? QuestMenuTrigger.MenuButton : held ? QuestMenuTrigger.StickClickHeld : QuestMenuTrigger.None;
            c.Record = _record.Step(f.A, f.B, dt);
            bool rg = _rightGrip ? f.RightGrip > GripOff : f.RightGrip >= GripOn;
            c.Smoke = rg && !_rightGrip;
            _rightGrip = rg;
            c.Fire = rg;   // held: guns where they are hot (the Bridge uses the grip for smoke when they are not)

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

    /// <summary>
    /// THE STICK FEEL IN A HEADSET (Ty on the Quest, 2026-11-02: "far too sensitive, especially around neutral").
    ///
    /// The curve is the one the game already has, applied once by Aircraft.ShapeAxis to whatever the stick says:
    ///   out = sign(x) · ((1 − k)·u + k·u³),  u = (|x| − dz) / (1 − dz) past the dead zone dz, 0 inside it
    /// so 0 → 0, it rises monotonically, and full stick is still full deflection (|x| = 1 → 1). With k = 0.65 a
    /// small movement does a third of what it did linearly (the slope at neutral is 1 − k = 0.35) and half stick is
    /// a quarter of the travel. The dead zone is taken by the stick reader (QuestControlMapper.StickDeadZone, 0.05,
    /// rescaled so there is no step at its edge); on top of it a headset adds none of its own.
    ///
    /// The setting is the one Options → Controls already has (SessionSettings.FeelExpo / FeelDeadZone, per axis,
    /// "unset" meaning the aircraft's own value). In a headset "unset" means THESE values instead, for aileron,
    /// elevator and rudder alike; phones and the Mac keep each aircraft's own (0.1–0.3). A value the pilot has set
    /// wins everywhere. The VR menu's "Stick expo" row sets it in the headset.
    /// </summary>
    public static class QuestStickFeel
    {
        public const double DefaultExpo = 0.65;
        /// <summary>On top of the stick reader's own 0.05: none.</summary>
        public const double DefaultExtraDeadZone = 0.0;

        /// <summary>The shaping curve (the same arithmetic as Aircraft.ShapeAxis, before the surface's travel).</summary>
        public static double Curve(double x, double expo, double deadZone)
        {
            double a = Math.Min(1.0, Math.Abs(x));
            if (a <= deadZone) return 0.0;
            double u = Math.Min(1.0, (a - deadZone) / (1.0 - deadZone));
            return Math.Sign(x) * (expo * u * u * u + (1.0 - expo) * u);
        }

        /// <summary>What a control-feel setting resolves to: the pilot's own value if set (&gt;= 0), otherwise the
        /// headset default in a headset, otherwise the aircraft's own.</summary>
        public static double Resolve(double pilotSetting, double aircraftValue, bool headset, double headsetDefault) =>
            pilotSetting >= 0 ? pilotSetting : headset ? headsetDefault : aircraftValue;

        /// <summary>The whole path a thumbstick or gamepad stick takes in a headset with the defaults: the reader's
        /// dead zone, then the curve.</summary>
        public static double StickToCommand(double raw) =>
            Curve(QuestControlMapper.DeadZone(raw, QuestControlMapper.StickDeadZone), DefaultExpo, DefaultExtraDeadZone);
    }

    /// <summary>A gamepad as the Input System reports it (the standard Gamepad layout: Xbox, PlayStation, 8BitDo …),
    /// read off the device by the Bridge. Sticks −1..1, +x right, +y forward; triggers 0..1. Buttons by POSITION
    /// (south = Xbox A / 8BitDo B), which is what the Input System's layout means by them.</summary>
    public struct GamepadState
    {
        public double LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger;
        public bool South, East, West, North, LeftShoulder, RightShoulder, LeftStickPress, RightStickPress, Start, Select;
    }

    /// <summary>
    /// A BLUETOOTH GAMEPAD IN THE HEADSET (Ty has an 8BitDo, 2026-11-02). It becomes one more
    /// <see cref="QuestControllerFrame"/>, merged with the Touch controllers', so every rule the controllers fly by —
    /// dead zones, the throttle as a lever, trim, flaps, the menu — is the same code and the same tests.
    ///
    ///   right stick       aileron (X) and elevator (Y; forward = push) — the Touch's right stick
    ///   left stick X      rudder;  left stick Y  the throttle lever as a rate (push to open, let go and it stays)
    ///   left / right trigger  LEFT / RIGHT wheel brake (proportional);  right shoulder  guns where hot, else smoke
    ///   south / east      pitch trim nose UP / nose DOWN (the Touch A / B)
    ///   west / north      flaps one step down / up (the Touch X / Y)
    ///   left stick press  gear (glider: tow / release)
    ///   right stick press recentre; held 1 s, the menu
    ///   Start             the VR menu;  Select  recentre
    ///
    /// The sticks are Ty's mapping for every platform (2026-11-02: right stick flies, left stick X rudder, left stick
    /// Y throttle — the iPhone's GamepadShaping has the same, with the throttle a rate there too), which is the Touch
    /// controllers' own split, so the pad's sticks go straight through. The two stick PRESSES stay where eeb12758 put
    /// them: neither collides with anything, and the 1 s menu hold sits on the flying stick, where a thumb pressing
    /// down nudges only the expo'd aileron and elevator rather than winding the throttle rate up or down.
    /// </summary>
    public static class QuestGamepadMap
    {
        public static QuestControllerFrame ToFrame(in GamepadState g) => new QuestControllerFrame
        {
            RightX = g.RightX, RightY = g.RightY,       // the flying stick
            LeftX = g.LeftX, LeftY = g.LeftY,           // rudder and the throttle lever
            LeftTrigger = g.LeftTrigger, RightTrigger = g.RightTrigger,
            RightGrip = g.RightShoulder ? 1.0 : 0.0,
            A = g.South, B = g.East, X = g.West, Y = g.North,
            RightStickClick = g.LeftStickPress,
            LeftStickClick = g.RightStickPress || g.Select,
            Menu = g.Start,
        };

        /// <summary>Two frames as one: each stick axis from whichever source is further from centre (a resting thumb
        /// on one does not cancel the other), triggers and grips the larger, buttons either. The hand position for the
        /// grabbed throttle and the hand-tracking fields come from <paramref name="controllers"/>; with a gamepad in
        /// use the pilot is not flying by hand gestures, so it is not "hands only".</summary>
        public static QuestControllerFrame Merge(in QuestControllerFrame controllers, in QuestControllerFrame pad, bool padPresent)
        {
            if (!padPresent) return controllers;
            QuestControllerFrame f = controllers;
            f.LeftX = Bigger(controllers.LeftX, pad.LeftX); f.LeftY = Bigger(controllers.LeftY, pad.LeftY);
            f.RightX = Bigger(controllers.RightX, pad.RightX); f.RightY = Bigger(controllers.RightY, pad.RightY);
            f.LeftTrigger = Math.Max(controllers.LeftTrigger, pad.LeftTrigger); f.RightTrigger = Math.Max(controllers.RightTrigger, pad.RightTrigger);
            f.LeftGrip = Math.Max(controllers.LeftGrip, pad.LeftGrip); f.RightGrip = Math.Max(controllers.RightGrip, pad.RightGrip);
            f.A |= pad.A; f.B |= pad.B; f.X |= pad.X; f.Y |= pad.Y;
            f.LeftStickClick |= pad.LeftStickClick; f.RightStickClick |= pad.RightStickClick; f.Menu |= pad.Menu;
            f.HandsOnly = false;
            return f;
        }

        private static double Bigger(double a, double b) => Math.Abs(b) > Math.Abs(a) ? b : a;
    }
}

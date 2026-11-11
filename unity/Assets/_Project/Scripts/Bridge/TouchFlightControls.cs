using System.Collections.Generic;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// RC-transmitter dual-touchpad controls (build-order step 5), the primary on-device input. All
    /// game controls sit along the BOTTOM edge of the screen (owner request); the HUD owns the top.
    /// Portrait: the controls become a bottom tray and the view/HUD sit above it (<see cref="ScreenLayout"/>).
    ///
    ///   LEFT pad  : square. X = rudder (spring back to centre),  Y = THROTTLE + WHEEL BRAKES on one
    ///               sticky axis (owner spec, standard for every type): top 75 % of travel is throttle
    ///               (full at the top, idle at 25 %), 20–25 % is a dead band so idle is easy to find
    ///               without touching the brakes, and 20 % → 0 is progressively stronger braking.
    ///               Rudder biases the brakes left/right while they are applied (toe-brake feel).
    ///               On the glider the upper band is the speed-brake lever (top = stowed, 25 % = full
    ///               out) and the lower band is the wheel brake, same geometry.
    ///   RIGHT pad : square. X = aileron (spring),  Y = elevator (spring). Up = push = nose DOWN
    ///               (realistic stick sense); flip <see cref="InvertElevator"/> for arcade sense.
    ///   TRIM      : a vertical pitch-trim slider beside the right pad, like the trim lever on an RC
    ///               transmitter. Sticky; biases the elevator by up to <see cref="TrimAuthority"/>.
    ///   Each pad draws X/Y position lines through the knob so the current stick position relative to
    ///   centre is readable at a glance.
    ///
    /// True multitouch: both thumbs at once are read from <see cref="Input.touches"/> (a touch that
    /// begins on/near a pad owns that pad). In the editor a single mouse pointer plus the arrow/A-D/S-W
    /// keys stand in so the pads and the whole loop can be exercised without a device. Rendered with
    /// OnGUI to match FlightHud/ChallengeHud; input is read from raw touches in Update (OnGUI is display
    /// only), so multitouch is unaffected by IMGUI's single-event model.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class TouchFlightControls : MonoBehaviour
    {
        [Header("Feel")]  // dead zone + expo are per-aircraft in AircraftConfig, not here
        public float PadHalfFraction = 0.30f;  // pad half-size as a fraction of min(screen w,h) (owner: MUCH bigger)
        public bool InvertElevator = false;     // false = realistic (up = nose down)
        /// <summary>Mac app (owner 2026-10-03: "should not have the same touch screen controls — keyboard or joystick"):
        /// no touch pads / trim slider; keyboard (ramped) + joystick drive the controls and a small read-only indicator
        /// shows where they are. The on-screen buttons stay (mouse).</summary>
        public static bool DeskMode => !Application.isMobilePlatform && !UiLayout.ForceTouchLayoutForTest;
        public float TrimAuthority = 0.6f;      // full trim slider = this much elevator (stick units) — enough to trim flaps-down approaches (owner 2026-10-03)
        public float IdleFraction = 0.25f;      // knob height (0 bottom..1 top) where throttle reaches idle
        public float BrakeStartFraction = 0.20f;// braking begins below this (20–25 % is the dead band)
        public float BrakeRudderBias = 0.8f;    // full rudder shifts this much braking to one side
        public float GrabSlop = 0.35f;          // a touch may begin this far outside a pad (x half-size) and still grab it

        /// <summary>
        /// OUTBOARD clearance of the pads from the screen edge, in INCHES (owner 2026-11-08: "increase the margin
        /// between screen edge and control/trim touch box edge slightly to make full deflection easier to attain").
        /// <see cref="ClampToPad"/> means full travel needs the THUMB at the pad's own edge, and at the shared 0.035
        /// margin that edge sat ~14 pt in on a phone — inside iOS's edge-gesture strip, so full aileron and full rudder
        /// were a fight. The strip is a fixed PHYSICAL width, not a proportion, so this is a floor in inches rather than
        /// a bigger fraction: a phone gains ~8 pt and an iPad, already clear at ~29 pt, is left exactly as it was (a
        /// fraction big enough to fix the phone shrank the iPad's pads 9 % to buy clearance it did not need). It is the
        /// pads' own number, so the button cluster, the trim bars and the portrait height reservation are untouched.
        /// </summary>
        public float PadEdgeInches = 0.14f;
        public float EjectHoldSec = 0.4f;       // EJECT must be HELD this long (a thumb slip can't eject)

        /// <summary>Raw aileron stick (-1..1) regardless of egress — after bail out / eject it steers the canopy.</summary>
        public float StickAileron { get; private set; }

        /// <summary>Raw pitch stick (-1..1) regardless of egress — in free fall it sets the pilot's BODY
        /// POSITION (PilotEgress.ReadBodyPos): neutral tracks, back is flat and slow, forward is head down
        /// and fast. The canopy ignores it.</summary>
        public float StickElevator { get; private set; }
        /// <summary>Elevator bias from the trim slider alone (stick units) — what the abandoned aircraft keeps.</summary>
        public float TrimElevator => (InvertElevator ? -_pitchTrim : _pitchTrim) * TrimAuthority;

        // Fleet the on-screen "Aircraft" button cycles through (matches KeyboardTestControls 1-0/F1-F2). The retired
        // 737-800 is not on it (owner 2026-11-01).
        private static readonly string[] Fleet =
        {
            "glider-2-33-like", "glider-eb29r-like", "glider-swift-s1-like", "glider-grob103-acro-like", "c172-like", "pa28-archer-like", "cirrus-sr22-like", "pitts-s2b-like", "stearman-pt17-like", "jn4-jenny-like",
            "cassutt-f1-like", "geebee-r2-like", "glasair3-like",
            "extra-300-like", "p51d-like", "f86-sabre-like", "seminole-like",
            "dc3-like", "hughes-h4-like", "pa18-cub-like", "dhc2-beaver-like", "dhc2-beaver-floats-like", "decathlon-8kcab-like",
        };
        // What the button and the number keys actually cycle: Fleet, except in the TestFlight slim package (FG_TF_SLIM,
        // FlyingGame.Core.TestFlightFleet), which ships only its own six types.
#if FG_TF_SLIM
        private static readonly string[] Cycle = System.Linq.Enumerable.ToArray(FlyingGame.Core.TestFlightFleet.Listed);
#else
        private static readonly string[] Cycle = Fleet;
#endif

        private FlightSimDriver _driver;

        // Live control state (already shaped, -1..1). Throttle and trim are sticky so they live across frames.
        private float _rudder, _throttle, _aileron, _elevator, _pitchTrim; // _throttle = left-pad Y knob, -1 (bottom)..1 (top)
        private bool _brakeHeld;                                          // B key (editor) — the pad bottom band is the real brake

        // Pad geometry recomputed each frame from screen size (screen px, origin bottom-left).
        private Vector2 _leftCenter, _rightCenter;
        private Vector2 _lookPad;   // the right pad's deflection when it is the view's pad (TvLookPad), -1..+1
        private float _half;                    // pad half-size (square)
        private Rect _trimRect;                 // vertical trim slider track

        // Which finger (or mouse, id = -1) currently owns each control, and its current position.
        private int _leftFinger = int.MinValue, _rightFinger = int.MinValue, _trimFinger = int.MinValue;
        private Vector2 _leftKnob, _rightKnob; // screen px; knob = pad centre when idle
        /// <summary>Lesson strip mode (owner 2026-10-03): only the controls the lesson gives you — a tall ELEVATOR strip on the
        /// right (+ the pitch trim beside it) and, when the lesson gives you power, a THROTTLE strip on the left. No pads.</summary>
        public bool StripElevator, StripThrottle;
        /// <summary>Idle-power landing lessons (owner 2026-10-06): the left strip is the WHEEL BRAKES (powered: pull down = more,
        /// spring off when released) or a glider's SPOILER handle (stays put; its last 15 % is the wheel brake).</summary>
        public bool StripBrakes;
        private float _stripBrake01;
        /// <summary>The brake/spoiler strip's starting point (a glider's spoiler opens half way on the approach).</summary>
        public void PresetStripBrake(float v) => _stripBrake01 = Mathf.Clamp01(v);
        public bool StripMode => StripElevator && !DeskMode;

        /// <summary>
        /// A gamepad is connected: the touch pads / strips and trim slider / bars are hidden and take no touches
        /// (owner 2026-11-02). Re-read every frame from <see cref="GamepadInput.Now"/>, so hot-plug flips it.
        /// The buttons (Reset, Aircraft, Flaps, Gear, Smoke, Tow, Bail, Fire, Eject, Look) stay.
        /// TV MODE: while the instruments are drawn into the TV's picture (UiSurface.IsTv) this also reads true: there are
        /// no sticks on the TV, so its dials take the same full-width row they take with a pad.
        /// </summary>
        public static bool TouchSticksHidden => UiSurface.IsTv || (!DeskMode && !FlyingGame.Core.GamepadShaping.TouchSticksShown(DeskMode, GamepadInput.Now.Connected));

        /// <summary>
        /// THIS PHONE IS A CONTROLLER FOR A TV: the game is on the TV and there is no game controller, so the touch
        /// pads are the only way to fly and they get the whole screen (Ty 2026-11-10). The complement of
        /// <see cref="TvButtonPad.Showing"/>, which takes the other case — TV plus a game controller, where the phone
        /// becomes a grid of buttons instead because the flying is done on the pad.
        /// Never true on the TV's own pass (<see cref="UiSurface.IsTv"/>): the TV draws no touch controls at all.
        /// </summary>
        public static bool TvControllerLayout => TvOutput.Active && !UiSurface.IsTv && !GamepadInput.Now.Connected;

        /// <summary>
        /// THE RIGHT PAD IS THE VIEW, not the stick. The owner's second controller layout (Ty 2026-11-10): "one with
        /// gyro control with a touchpad for look around" — the gyro flies roll and pitch, so the pad that used to be
        /// aileron and elevator is free, and the look goes on it. The LEFT pad is untouched in both layouts, because
        /// tilt gives three axes and throttle is not one of them and it still has to live somewhere ("Yes left pad
        /// stays throttle").
        ///
        /// Rudder is the owner's own choice and already has a setting: TiltSteering.RudderFromYaw off keeps it on the
        /// left pad sideways, so "throttle rudder touch and roll pitch movement based" is that switch, not new code.
        /// </summary>
        public static bool TvLookPad => TvControllerLayout && TvGyroLayout;

        /// <summary>
        /// HAS THE OWNER CHOSEN THE GYRO LAYOUT? Its own setting, and OFF until picked — NOT inferred from
        /// TiltSteering.Enabled, which is what the first cut did and which cost him a flight: tilt flying had been
        /// switched on at some earlier point, so the moment the TV layout appeared the right pad stopped flying and
        /// started looking around, with no way to tell from the screen. Ty 2026-11-10: "i can no longer hear it when
        /// i'm moving the controls". A layout that takes the stick away has to be asked for.
        /// </summary>
        public static bool TvGyroLayout
        {
            get { if (_tvGyro < 0) _tvGyro = PlayerPrefs.GetInt("tv.gyroLayout", 0); return _tvGyro == 1; }
            set { _tvGyro = value ? 1 : 0; PlayerPrefs.SetInt("tv.gyroLayout", _tvGyro); PlayerPrefs.Save(); }
        }
        private static int _tvGyro = -1;

        /// <summary>LOCAL 2-PLAYER: the tray is drawn AND hit-tested, or neither (<see cref="FlyingGame.Core.LocalMp.SplitControls"/>).
        /// Off while the compact split HUD is up and P1 has a pad: the single-player tray spans both halves, and an
        /// invisible RESET / BAIL / FIRE on P2's half would be P1's buttons under P2's thumb. P1 then flies the pad (A fires,
        /// Menu opens OPTIONS) and RESET / the handle are on the toolbar's FLIGHT panel (ViewPanel), one column per player.
        /// Always true outside split screen.</summary>
        public static bool TrayLive => FlyingGame.Core.LocalMp.SplitControls.TouchTrayLive(LocalPlayerHud.CompactActive, GamepadInput.Now.Connected);
        private bool _touchSticksWereHidden;
        private Rect _eleStrip, _thrStrip; private float _stripY;

        // On-screen button rects (screen px, bottom-left origin) — computed in Update, drawn in OnGUI.
        private Rect _brakeRect, _resetRect, _acftRect, _towRect;
        private Rect _bailRect, _ejectRect;                 // BAIL OUT (tap) / EJECT (hold EjectHoldSec)
        private Rect _ailTrimRect, _rudTrimRect;            // horizontal trim bars under the pads (RC transmitter style)
        private Rect _fireRect; private int _fireFinger = int.MinValue;   // FIRE (hold) — only shown with the guns hot
        private CombatController _combat;
        private int _ailTrimFinger = int.MinValue, _rudTrimFinger = int.MinValue;
        private float _aileronTrim, _rudderTrim;            // -1..1 × TrimAuthority
        public float TrimAileron => _aileronTrim * TrimAuthority;
        public float TrimRudder => _rudderTrim * TrimAuthority;
        // Buttons fire on the finger that touches them (not IMGUI), so they work while the pads are held (owner).
        private enum Btn { Reset, Aircraft, Flaps, Gear, Tow, Bail, Smoke, LookCentre, CamRef, CtlMode }
        private readonly HashSet<Btn> _tapped = new();
        private Rect _gearRect;                             // GEAR UP / DOWN (retractable types)
        private Rect _smokeRect;                            // SMOKE ON / OFF (owner 2026-10-31: a full-time button, not a menu)
        private Rect _ctlModeRect;                          // TOUCH / GYRO, only while the phone is the TV's controller
        private Rect _camRefRect;                           // CAM: FLT PATH / TRACK (Ty 2026-11-03: full-time, beside SMOKE)
        private CockpitView _cockpitCache;
        private CockpitView _cockpit => _cockpitCache ??= GetComponent<CockpitView>();
        /// <summary>Looking around is only a thing when you are sitting in the aeroplane.</summary>
        private bool LookAvailable => _cockpit != null && ChaseCamera.InCockpit;
        private Rect _lookLeftRect, _lookCentreRect, _lookRightRect;
        /// <summary>The finger on a look button and which way it points. Held is a pan and a tap is a snap, but
        /// neither is decided here: the direction goes to CockpitView.OnScreenHat and ITS tap window decides,
        /// exactly as it does for a stick's hat, so glass and hat cannot drift apart.</summary>
        private int _lookFinger = int.MinValue;
        private int _lookDir;

        private SmokeController _smokeCache;
        private SmokeController _smoke => _smokeCache ??= GetComponent<SmokeController>();
        /// <summary>Whether to give SMOKE a slot at all: anything that can carry the oil, which is every type with an
        /// engine's exhaust and every glider with wingtip canisters. Laid out per frame, so it follows an aircraft change.</summary>
        private bool SmokeAvailable => _smoke != null && _smoke.Available;
        private int _ejectFinger = int.MinValue;            // pointer holding EJECT
        private float _ejectHold;                           // seconds held so far
        private PilotEgress _egress;

        private Texture2D _knobTex, _solidTex, _btnBg;
        private GUIStyle _btnStyle, _labelStyle, _valueStyle, _ejectStyle;
        private int _styleFs;

        private void Awake()
        {
            _driver = GetComponent<FlightSimDriver>();
            Input.multiTouchEnabled = true;
        }

        private void Start()
        {
            _driver.AircraftChanged += PresetTrim;
            PresetTrim();
        }

        private void OnDestroy()
        {
            if (_driver != null) _driver.AircraftChanged -= PresetTrim;
        }

        /// <summary>Preset the pitch-trim slider to the spawn trim so a neutral stick holds level flight.</summary>
        /// <summary>Set the pilot's pitch trim to a stick position (lessons start in trim).</summary>
        public void PresetPitchTrim(double stick)
        {
            float t = (float)stick / Mathf.Max(0.01f, TrimAuthority);
            _pitchTrim = Mathf.Clamp(InvertElevator ? -t : t, -1f, 1f);
        }

        private void PresetTrim()
        {
            // Air start: the RIG power, 75 % (owner 2026-11-02) -- every aeroplane is rigged to fly straight and level hands off
            // there, and FlightSimDriver starts it trimmed for exactly that. Ground start: IDLE (owner: "power should start at
            // idle on the ground").
            _throttle = AxisForFraction(_driver.GroundStart || _driver.IdleStart ? IdleFraction
                : IdleFraction + (1f - IdleFraction) * (float)FlyingGame.Sim.RigSolver.RigThrottle);
            if (_driver.Sim?.Aircraft?.Config?.Propulsion == null)
            {
                // Glider: spoilers open on the ground, stowed in the air; on final the knob starts where the spawn's
                // spoiler setting is (half) — SpoilerFraction = (0.5 − k) / (0.5 − IdleFraction).
                float k = _driver.GroundStart ? IdleFraction : _driver.IdleStart ? 0.5f - (float)_driver.StartSpoilerFraction * (0.5f - IdleFraction) : 1f;
                _throttle = AxisForFraction(k);
            }
            _leftKnob = IdleLeftKnob();
            _vrHandle = -1f;
            float t = (float)_driver.TrimStick / Mathf.Max(0.01f, TrimAuthority);
            _pitchTrim = Mathf.Clamp(InvertElevator ? -t : t, -1f, 1f);
            // Aileron and rudder trim start centred on every new flight, like the pitch trim starts on the spawn's: the
            // airframe's own rig is what flies it straight (2026-11-02). Left over from the last flight -- or another
            // aeroplane -- they were a hidden bias, and with a gamepad connected their bars are not even drawn.
            _aileronTrim = _rudderTrim = 0f;
        }

        // The last group carries one more bool than the mainline wrote: TvOutput.Active, because the layout differs
        // when the game is on the TV. The VALUE and this TYPE have to be changed together or it does not compile.
        private ((int, int, bool, bool, bool, bool, bool), (bool, float, float, float, float, float, float), (bool, bool, bool, bool, bool, bool, bool, bool)) _layoutKey;
        private bool _layoutKeySet;

        private void Update()
        {
            // The look pad's deflection goes to the view every frame, zero included — zero is what lets the view's
            // own spring-centre work, exactly as the hat's 0,0 does. Only while this pad IS the view's: otherwise
            // it would fight the hat buttons for the same head.
            if (_cockpit != null)
            {
                _cockpit.OnScreenPan = TvLookPad ? _lookPad : Vector2.zero;
                _cockpit.PanHeld = TvLookPad && _rightFinger != int.MinValue;
            }
            // Re-lay out only when something the layout depends on changes (screen, aircraft type, keep-out step) —
            // a per-frame layout from the live aircraft rect made the buttons creep (owner: "jitter").
            _egress ??= GetComponent<PilotEgress>();
            Rect ko = ScreenLayout.AircraftKeepOut;
            // A value tuple, not a string: this runs every frame, and an interpolated key was ~10 boxed values and a
            // string of garbage a frame for the collector (docs/quest-performance.md).
            var key = ((Screen.width, Screen.height, StripElevator, StripThrottle, StripBrakes, FireAvailable, HasEjectionSeat),
                       (_driver.Sim?.Aircraft?.Config?.Propulsion == null, ko.yMin, ko.xMin, ko.xMax, UiLayout.SafeBottom, UiLayout.SafeLeft, UiLayout.SafeRight),
                       (BailAvailable, EjectAvailable, TouchSticksHidden, EngineBelowWanted, SmokeAvailable, _driver.HasFlaps, _driver.Sim?.Aircraft?.Config?.RetractableGear == true, TvOutput.Active));
            if (!_layoutKeySet || !key.Equals(_layoutKey)) { _layoutKey = key; _layoutKeySet = true; LayOut(); }
            JoyLink.Tick();   // (the Mac keeps sending its joystick to a linked iPad / iPhone, menus open or not)
            RudderPedals.Tick();   // (hot-plug and the calibration's live readout work with the menus open, too)
            GamepadMenuButtons();   // (Menu has to be able to CLOSE the options panel, so this runs with it open)
            if (SessionSettings.MenuOpen || SessionSettings.ReplayActive)
            {
                _leftFinger = _rightFinger = _trimFinger = _ejectFinger = int.MinValue; _ejectHold = 0f;
                _lookFinger = int.MinValue; _lookDir = 0;
                if (_cockpit != null) _cockpit.OnScreenHat = Vector2Int.zero;
                return;   // landing page owns the screen
            }
            ApplyTouchSticksHidden();
            // Cardboard: nothing here is drawn (OnGUI stands down), and the only touch a pilot in the viewer can make
            // is its trigger, which VrRig reads as a recentre. An invisible RESET or BAIL must not take it.
            if (VrMode.On)
            {
                _brakeHeld = false;
                _leftFinger = _rightFinger = _trimFinger = _ailTrimFinger = _rudTrimFinger = _fireFinger = _ejectFinger = int.MinValue;
                _ejectHold = 0f; _lookFinger = int.MinValue; _lookDir = 0;
            }
            else if (TrayLive) ReadPointers();
            else PadOnly();
            if (DeskMode) DeskInput(); else { MergeKeyboardFallback(); MobileJoystick(); }
            GamepadFlight();
            QuestInput();   // last: in the headset the Touch controllers and the hands own the controls they move
            PublishToDriver();
        }

        /// <summary>
        /// Landscape: [left pad] [Aircraft/Reset | Flaps] [Trim] [right pad] at mid-height.
        /// Portrait (<see cref="ScreenLayout"/>): a bottom TRAY — pads in the two bottom corners with the trim
        /// slider between them, and one button row (Reset · Aircraft · Flaps · Tow) above the pad labels.
        /// The 3D view + HUD live above the tray.
        /// </summary>
        private void LayOut()
        {
            float w = Screen.width, h = Screen.height, s = Mathf.Min(w, h);
            if (ScreenLayout.Portrait)
            {
                _engineRowRect = Rect.zero;
                LayOutPortrait(w);
                if (TouchSticksHidden) LayOutPortraitBottomRow(UiLayout.SafeBottom + UiLayout.Margin);
                PublishBand();
                return;
            }
            float margin = s * 0.035f;
            float gap = s * 0.02f;
            float padMargin = Mathf.Max(margin, UiLayout.Dpi * PadEdgeInches);
            if (TvControllerLayout)
            {
                // THE PHONE IS THE CONTROLLER AND NOTHING ELSE (Ty 2026-11-10: "I need the phone to turn into a
                // controller unless an external controller is connected", "two giant touch pads and the control
                // buttons between"). The dials and the aeroplane are on the TV, so the centre band is protecting
                // nothing here and the pads stop being a fraction of the screen and start being the screen.
                //
                // They are square and centred at mid-height, so two of them on a landscape phone eat the width fast:
                // at full height they would leave about 250 px between on a 2622 x 1206 screen, which is not a middle
                // anyone can put buttons in. So the pads grow until EITHER the height runs out OR the middle is down
                // to what the button cluster and the trim slider need — about 0.45 of the short edge, which leaves
                // them 81 % of the screen height rather than 100 % of it and keeps the layout he asked for.
                float middle = s * 0.45f;
                _half = Mathf.Min((h - 2f * margin) * 0.5f, (w - 2f * padMargin - middle) * 0.25f);
            }
            else
            {
                // No-overlap rule: on squarer screens (iPad, 4:3) full-size pads would squeeze the centre band until the dials
                // pile on each other — the pads shrink so the band keeps at least MinBandFrac of the short edge.
                const float MinBandFrac = 0.85f;
                _half = Mathf.Min(s * PadHalfFraction, (w - 2f * padMargin - 3f * gap - MinBandFrac * s) / 2.22f);
            }
            // Pads sit at MID-HEIGHT (owner request), not along the bottom.
            _leftCenter = new Vector2(padMargin + _half, h * 0.5f);
            _rightCenter = new Vector2(w - padMargin - _half, h * 0.5f);

            // Pitch-trim slider: full pad height, just inboard of the right (elevator) pad.
            float trimW = _half * 0.22f;
            _trimRect = new Rect(_rightCenter.x - _half - gap - trimW, _rightCenter.y - _half, trimW, 2f * _half);

            // Centre cluster between the left pad and the trim slider: Aircraft over Reset; Flaps/Tow beside.
            // (The Brakes button is gone — braking lives on the bottom of the throttle pad.)
            float clusterLeft = _leftCenter.x + _half + gap;
            float clusterRight = _trimRect.x - gap;
            float avail = clusterRight - clusterLeft;
            float bh = _half * 0.26f;
            float bw = (avail - gap) * 0.5f;
            float x0 = (clusterLeft + clusterRight) * 0.5f - (2f * bw + gap) * 0.5f;
            // Three rows of two, from the bottom: [Reset | Flaps] [Aircraft | Gear] [Tow | Bail] (or [Bail | Eject]).
            // RULE: nothing covers the aircraft — if the top row would reach the aircraft's screen rect, the rows shrink.
            float rowGap = gap * 0.6f;
            // Four rows now, not three: SMOKE is a full-time button of its own (owner 2026-10-31) rather than a
            // control squeezed in beside BAIL. The keep-out shrink has to divide by four or the top row climbs onto
            // the aeroplane, which the rule below forbids.
            // The CAM button (Flight Path / Track, Ty 2026-11-03) is full-time, so the fourth row always exists.
            int rows = 4;
            if (ScreenLayout.HasAircraftKeepOut)
            {
                float maxTop = ScreenLayout.AircraftKeepOut.yMin - gap;
                float fit = (maxTop - margin - (rows - 1) * rowGap) / rows;
                if (fit < bh) bh = Mathf.Max(_half * 0.16f, fit);
            }
            float r0 = margin, r1 = margin + bh + rowGap, r2 = margin + 2f * (bh + rowGap);
            float r3 = margin + 3f * (bh + rowGap);
            _resetRect = new Rect(x0, r0, bw, bh);
            _brakeRect = new Rect(x0 + bw + gap, r0, bw, bh);     // the FLAPS slot
            _acftRect = new Rect(x0, r1, bw, Mathf.Max(bh, UiLayout.CaptionText * 2.4f));
            _gearRect = new Rect(x0 + bw + gap, r1, bw, bh);
            if (HasEjectionSeat)
            {
                _towRect = new Rect(x0, r2, bw, bh);               // (powered: never shown)
                _bailRect = new Rect(x0, r2, bw, bh);
                _ejectRect = new Rect(x0 + bw + gap, r2, bw, bh);
            }
            else
            {
                bool towSlot = _driver.Sim?.Aircraft?.Config?.Propulsion == null;
                _towRect = new Rect(x0, r2, bw, bh);
                _bailRect = towSlot ? new Rect(x0 + bw + gap, r2, bw, bh) : new Rect(x0, r2, 2f * bw + gap, bh);
                _ejectRect = new Rect(x0 + bw + gap, r2, bw, bh);
            }
            _smokeRect = SmokeAvailable ? new Rect(x0, r3, bw, bh) : new Rect(0, 0, 0, 0);
            _camRefRect = SmokeAvailable ? new Rect(x0 + bw + gap, r3, bw, bh) : new Rect(x0, r3, 2f * bw + gap, bh);
            // TOUCH / GYRO, a fifth row, and ONLY when the phone is the TV's controller (Ty 2026-11-10: "we need a
            // shortcut button for touch vs gyro on the controller so I don't have to several menus deep to switch").
            // It exists nowhere else, so no ordinary layout grows a row: in TV mode the cluster has the height, the
            // pads having taken the width.
            _ctlModeRect = TvControllerLayout ? new Rect(x0, r3 + bh + rowGap, 2f * bw + gap, bh) : Rect.zero;
            _engineRowRect = Rect.zero;
            if (EngineBelowWanted && !TouchSticksHidden) LayOutEngineBelowCluster(s, h, margin, gap, rowGap, clusterLeft, clusterRight);
            // Trim bars: rudder under the left pad, aileron under the right pad (RC transmitter style).
            float barH = s * 0.045f;
            _rudTrimRect = new Rect(_leftCenter.x - _half, _leftCenter.y - _half - gap - barH, 2f * _half, barH);
            _ailTrimRect = new Rect(_rightCenter.x - _half, _rightCenter.y - _half - gap - barH, 2f * _half, barH);
            // LOOK: three buttons at the TOP centre, under the toolbar, only while in the cockpit. They go up
            // there because the bottom cluster is already four rows deep and has a keep-out rule, and because
            // looking around belongs beside VIEW rather than among the aeroplane's switches.
            float lookW = Mathf.Min(_half * 0.62f, w * 0.14f), lookH = _half * 0.24f, lookGap = gap * 0.8f;
            float lookY = UiLayout.ToolbarBottom + gap;
            float lookX = w * 0.5f - (3f * lookW + 2f * lookGap) * 0.5f;
            _lookLeftRect = new Rect(lookX, lookY, lookW, lookH);
            _lookCentreRect = new Rect(lookX + lookW + lookGap, lookY, lookW, lookH);
            _lookRightRect = new Rect(lookX + 2f * (lookW + lookGap), lookY, lookW, lookH);
            _fireRect = FireRectTopLeft();
            if (StripMode) LayOutStrips(w);
            if (TouchSticksHidden) { LayOutControllerButtons(); LayOutControllerEngineRow(); }
            PublishBand();
        }

        private Rect _engineRowRect;   // GUI space (top-left origin); width 0 = engine row under the flight row
        /// <summary>Owner 2026-11-02: the analog ENGINE row (tach, throttle, flow, fuel) goes BELOW the airplane, centred,
        /// in touch and controller layouts. Not in the cockpit (panel), lessons' strips or the Mac.</summary>
        private bool EngineBelowWanted => !DeskMode && !StripMode && !ChaseCamera.InCockpit && !TvOutput.Active &&   // TV: the engine row is on the TV
            (SessionSettings.Instruments == SessionSettings.InstrumentMode.Analog || SessionSettings.Instruments == SessionSettings.InstrumentMode.Hud) && !SessionSettings.FlightTestData &&
            _driver != null && _driver.Sim?.Aircraft?.Config?.Propulsion != null;

        /// <summary>Touch landscape with the engine row below the airplane. The four-row centre cluster leaves no room
        /// between the airplane and the buttons, so the cluster is re-laid and the engine row sits on top of it:
        /// - WIDE (iPad): the pads end well above the bottom edge, so every button goes in ONE row along the bottom,
        ///   under the pads, and the engine row has the band between the pads to itself;
        /// - NARROW (iPhone): the pads reach the bottom, so the buttons fold into TWO rows in the band —
        ///   [SMOKE | BAIL | (EJECT) | (GEAR)] over [Reset | Aircraft | FLAPS].
        /// Same keep-out rule as before: if the engine row would reach the airplane, the buttons shrink first.</summary>
        private void LayOutEngineBelowCluster(float s, float h, float margin, float gap, float rowGap, float clusterLeft, float clusterRight)
        {
            float w = Screen.width, avail = clusterRight - clusterLeft;
            float rowW = (float)FlyingGame.Core.HudInstrumentLayout.RowWidth(s, avail, false);
            int lines = FlyingGame.Core.HudInstrumentLayout.EngineValueLines(rowW / 4f, UiLayout.MinText);
            float engineH = (float)FlyingGame.Core.HudInstrumentLayout.EngineBlockHeight(s, false, lines);
            bool flaps = _driver.HasFlaps && !Practice.PracticeController.AnyActive;
            bool gear = GearSlot;
            float capH = UiLayout.CaptionText * 2.4f;   // the aircraft name may need two lines
            // Bottom of the trim bars' captions (DrawTrimBar: label lh = 1.5 fs below a bar of s*0.045 under each pad).
            float fs = Mathf.Round(s * ScreenLayout.FontFrac);
            float padsFloor = h * 0.5f - _half - gap - s * 0.045f - fs * 1.5f - 2f;
            float bh = _half * 0.26f;
            float wideH = Mathf.Max(bh, UiLayout.CaptionText * 1.3f);   // one-line aircraft name: the row is wide
            bool wide = margin + wideH + gap <= padsFloor;
            float clusterTop;   // screen px, bottom-left origin
            _towRect = Rect.zero;   // powered only: never a tow button
            if (wide)
            {
                if (ScreenLayout.HasAircraftKeepOut)
                {
                    float fit = ScreenLayout.AircraftKeepOut.yMin - gap - engineH - gap - margin;
                    if (fit < wideH) wideH = Mathf.Max(_half * 0.16f, fit);
                }
                // Units: Reset 1, Aircraft 1.5, Flaps 1.5 (three segments), the switches 1.2 each.
                float units = 1f + 1.5f + (flaps ? 1.5f : 0f) + (SmokeAvailable ? 1.2f : 0f) + 1.2f + 1.2f + (HasEjectionSeat ? 1.2f : 0f) + (gear ? 1.2f : 0f);
                int n = 4 + (flaps ? 1 : 0) + (SmokeAvailable ? 1 : 0) + (HasEjectionSeat ? 1 : 0) + (gear ? 1 : 0);   // Reset, Aircraft, CAM, Bail + options
                float span = w - UiLayout.SafeLeft - UiLayout.SafeRight - 2f * margin;
                float u = Mathf.Min(s * 0.16f, (span - (n - 1) * gap) / units);
                float x = w * 0.5f - (u * units + (n - 1) * gap) * 0.5f;
                Rect Take(float k) { var r = new Rect(x, margin, u * k, wideH); x += u * k + gap; return r; }
                _resetRect = Take(1f);
                _acftRect = Take(1.5f);
                _brakeRect = flaps ? Take(1.5f) : Rect.zero;
                _smokeRect = SmokeAvailable ? Take(1.2f) : Rect.zero;
                _camRefRect = Take(1.2f);
                _bailRect = Take(1.2f);
                _ejectRect = HasEjectionSeat ? Take(1.2f) : Rect.zero;
                _gearRect = gear ? Take(1.2f) : Rect.zero;
                clusterTop = margin + wideH;
            }
            else
            {
                if (ScreenLayout.HasAircraftKeepOut)
                {
                    float maxTop = ScreenLayout.AircraftKeepOut.yMin - gap - engineH - gap;
                    float fit = (maxTop - margin - rowGap) / 2f;
                    if (fit < bh) bh = Mathf.Max(_half * 0.16f, fit);
                }
                float hLow = Mathf.Max(bh, capH);
                float rowLow = margin, rowHigh = margin + hLow + rowGap;
                float unit = (avail - 2f * gap) / 4f;
                float lowW = flaps ? 4f * unit + 2f * gap : 2.5f * unit + gap;
                float lx = (clusterLeft + clusterRight - lowW) * 0.5f;
                _resetRect = new Rect(lx, rowLow, unit, hLow);
                _acftRect = new Rect(lx + unit + gap, rowLow, 1.5f * unit, hLow);
                _brakeRect = flaps ? new Rect(lx + 2.5f * unit + 2f * gap, rowLow, 1.5f * unit, hLow) : Rect.zero;
                int n = (SmokeAvailable ? 1 : 0) + 2 + (HasEjectionSeat ? 1 : 0) + (gear ? 1 : 0);
                float tw = Mathf.Min(1.5f * unit, (avail - (n - 1) * gap) / n);
                float tx = (clusterLeft + clusterRight - (n * tw + (n - 1) * gap)) * 0.5f;
                int k = 0;
                Rect Next() => new Rect(tx + k++ * (tw + gap), rowHigh, tw, bh);
                _smokeRect = SmokeAvailable ? Next() : Rect.zero;
                _camRefRect = Next();
                _bailRect = Next();
                _ejectRect = HasEjectionSeat ? Next() : Rect.zero;
                _gearRect = gear ? Next() : Rect.zero;
                clusterTop = rowHigh + bh;
            }
            float left = (float)FlyingGame.Core.HudInstrumentLayout.CentredRowLeft(w * 0.5f, rowW, clusterLeft, clusterRight);
            float bottomGui = h - clusterTop - gap;
            _engineRowRect = new Rect(left, bottomGui - engineH, rowW, engineH);
        }

        /// <summary>Controller (no pads, full screen): the engine row sits at the bottom of the screen, centred, beside the
        /// bottom-left button column (pushed right only when the column would be underneath it).</summary>
        private void LayOutControllerEngineRow()
        {
            _engineRowRect = Rect.zero;
            if (!EngineBelowWanted) return;
            float w = Screen.width, h = Screen.height, s = UiLayout.S;
            float minX = UiLayout.SafeLeft + UiLayout.Margin, maxX = w - UiLayout.SafeRight - UiLayout.Margin;
            float rowW = (float)FlyingGame.Core.HudInstrumentLayout.RowWidth(s, maxX - minX, true);
            float engineH = (float)FlyingGame.Core.HudInstrumentLayout.EngineBlockHeight(s, true);
            float columnRight = Mathf.Max(_resetRect.xMax, Mathf.Max(_acftRect.xMax, _smokeRect.xMax)) + UiLayout.Gap;
            float left = (float)FlyingGame.Core.HudInstrumentLayout.CentredRowLeft(w * 0.5f, rowW, Mathf.Max(minX, columnRight), maxX);
            float bottomGui = h - UiLayout.SafeBottom - UiLayout.Margin;
            _engineRowRect = new Rect(left, bottomGui - engineH, rowW, engineH);
        }

        private void LayOutControllerButtons()
        {
            float s = UiLayout.S, gap = s * 6f / 1206f;
            float width = s * .48f, height = s * .055f;
            float x = UiLayout.SafeLeft + UiLayout.Margin, y = UiLayout.SafeBottom + UiLayout.Margin;
            int row = 0;
            Rect Slot(bool shown = true) => shown ? new Rect(x, y + row++ * (height + gap), width, height) : Rect.zero;
            _resetRect = Slot(); _acftRect = Slot();
            _brakeRect = Slot(_driver.HasFlaps && !Practice.PracticeController.AnyActive);
            _gearRect = Slot(GearSlot);
            var tow = GetComponent<TowController>();
            _towRect = Slot(tow != null && _driver.Sim?.Aircraft?.Config?.Propulsion == null &&
                (tow.Towing || (_driver.GroundStart && tow.Tow == null)));
            _bailRect = Slot(BailAvailable); _ejectRect = Slot(EjectAvailable); _smokeRect = Slot(SmokeAvailable); _camRefRect = Slot();
        }

        /// <summary>Tell the shared layout (UiLayout) where the controls are, so text and dials keep clear of them.</summary>
        private void PublishBand()
        {
            float w = Screen.width, h = Screen.height;
            if (!DeskMode) UiLayout.LeftBottomLimit = 99999f;
            UiLayout.EngineRowRect = DeskMode || TvOutput.Active ? Rect.zero : _engineRowRect;
            if (TouchSticksHidden)
            {
                UiLayout.BandMin = UiLayout.SafeLeft; UiLayout.BandMax = w - UiLayout.SafeRight;
                UiLayout.BottomLimit = h * .4f;
                return;
            }
            if (DeskMode)
            {
                // Mac: no pads; only the small control indicator bottom-left and the centre buttons at the bottom.
                UiLayout.BandMin = 0f; UiLayout.BandMax = w;
                UiLayout.BottomLimit = h - (_bailRect.yMax + Mathf.Min(w, h) * 0.02f);
                return;
            }
            if (ScreenLayout.Portrait)
            {
                UiLayout.BandMin = 0f; UiLayout.BandMax = w;
                UiLayout.BottomLimit = TouchSticksHidden ? h - (Mathf.Max(_bailRect.yMax, _resetRect.yMax) + Mathf.Min(w, h) * 0.02f)
                    : h - ScreenLayout.TrayHeightPx;
                if (_engineRowRect.width > 0f) UiLayout.BottomLimit = Mathf.Min(UiLayout.BottomLimit, _engineRowRect.y - UiLayout.Gap);
                return;
            }
            float gap = Mathf.Min(w, h) * 0.02f;
            if (StripMode)
            {
                UiLayout.BandMin = (StripThrottle || StripBrakes ? _thrStrip.xMax : 0f) + gap;
                UiLayout.BandMax = _trimRect.x - gap;
                float top = Mathf.Max(_bailRect.yMax, Mathf.Max(_acftRect.yMax, _towRect.yMax));
                UiLayout.BottomLimit = h - top - gap;
                return;
            }
            UiLayout.BandMin = _leftCenter.x + _half + gap;
            UiLayout.BandMax = Mathf.Min(_trimRect.x, _rightCenter.x - _half) - gap;
            float clusterTop = Mathf.Max(_bailRect.yMax, Mathf.Max(_acftRect.yMax, _towRect.yMax));   // screen px, bottom-left origin
            UiLayout.BottomLimit = h - clusterTop - gap;
            if (_engineRowRect.width > 0f) UiLayout.BottomLimit = _engineRowRect.y - gap;   // text keeps clear of the engine row
        }

        private void LayOutPortrait(float w)
        {
            _half = w * ScreenLayout.PortraitPadHalf;
            float margin = w * ScreenLayout.Margin;
            float gap = w * ScreenLayout.Gap;

            // Pads in the bottom corners.
            float padMargin = Mathf.Max(margin, UiLayout.Dpi * PadEdgeInches);
            _leftCenter = new Vector2(padMargin + _half, UiLayout.SafeBottom + margin + _half);
            _rightCenter = new Vector2(w - padMargin - _half, UiLayout.SafeBottom + margin + _half);

            // Trim slider centred between the pads, full pad height.
            float trimW = _half * 0.22f;
            _trimRect = new Rect(w * 0.5f - trimW * 0.5f, UiLayout.SafeBottom + margin, trimW, 2f * _half);

            // Trim bars right above each pad (rudder left, aileron right), then the labels, then the buttons.
            float barH = w * ScreenLayout.TrimBarHeight;
            float barY = UiLayout.SafeBottom + margin + 2f * _half + gap * 0.5f;
            _rudTrimRect = new Rect(_leftCenter.x - _half, barY, 2f * _half, barH);
            _ailTrimRect = new Rect(_rightCenter.x - _half, barY, 2f * _half, barH);
            // A single row clears the complete pad/trim label block.
            float rowY = barY + barH + ScreenLayout.LabelBlockPx + gap;
            LayOutPortraitBottomRow(rowY);
            _fireRect = FireRectTopLeft();
            if (StripMode) LayOutStrips(w);
            PublishBand();
        }

        // Button rects use bottom-left screen coordinates; the gauge rect uses top-left GUI coordinates.
        private void LayOutPortraitBottomRow(float bottom)
        {
            float s = UiLayout.S, gap = UiLayout.Gap;
            bool flaps = _driver.HasFlaps && !Practice.PracticeController.AnyActive;
            bool gear = GearSlot;
            var tow = GetComponent<TowController>();
            bool towing = tow != null && _driver.Sim?.Aircraft?.Config?.Propulsion == null &&
                (tow.Towing || (_driver.GroundStart && tow.Tow == null));
            int count = 3 + (SmokeAvailable ? 1 : 0) + (BailAvailable ? 1 : 0) +
                (EjectAvailable ? 1 : 0) + (flaps ? 1 : 0) + (gear ? 1 : 0) + (towing ? 1 : 0);
            float left = UiLayout.SafeLeft + UiLayout.Margin;
            float span = Screen.width - UiLayout.SafeRight - UiLayout.Margin - left;
            // Aircraft and segmented flaps get extra width; every button shares the same height.
            float units = count - .2f + .9f + (SmokeAvailable ? .1f : 0f) + .1f + (BailAvailable ? .1f : 0f) + (flaps ? 1.1f : 0f);
            float unit = (span - (count - 1) * gap) / units;
            float height = s * ScreenLayout.ButtonHeight;
            Rect Take(bool shown, float weight = 1f)
            {
                if (!shown) return Rect.zero;
                var r = new Rect(left, bottom, unit * weight, height);
                left = r.xMax + gap;
                return r;
            }
            _smokeRect = Take(SmokeAvailable, 1.1f); _camRefRect = Take(true, 1.1f); _bailRect = Take(BailAvailable, 1.1f);
            _ejectRect = Take(EjectAvailable); _resetRect = Take(true, .8f);
            _acftRect = Take(true, 1.9f); _brakeRect = Take(flaps, 2.1f);
            _gearRect = Take(gear); _towRect = Take(towing);
            _engineRowRect = Rect.zero;
            if (!EngineBelowWanted) return;
            bool controller = TouchSticksHidden;
            float width = (float)FlyingGame.Core.HudInstrumentLayout.RowWidth(s, span, true);
            float engineHeight = (float)FlyingGame.Core.HudInstrumentLayout.EngineBlockHeight(s, controller);
            // The opaque touch tray ends above the buttons; keep the complete readout above it.
            float engineGap = controller ? gap : s * ScreenLayout.Gap + gap;
            _engineRowRect = new Rect((Screen.width - width) * .5f,
                Screen.height - bottom - height - engineGap - engineHeight, width, engineHeight);
        }

        /// <summary>Strip layout. Landscape: full height under the toolbar, right edge (elevator) / left edge (throttle).
        /// Portrait: in the tray, ~1.3 pad heights tall.</summary>
        private void LayOutStrips(float w)
        {
            float h = Screen.height, s = Mathf.Min(w, h), margin = s * 0.035f, gap = s * 0.02f;
            float sw = s * 0.2f;
            float bottom, top;
            if (ScreenLayout.Portrait) { bottom = margin; top = margin + 2f * _half * 1.3f; }
            else { bottom = margin; top = h - UiLayout.ToolbarBottom - margin - (FireAvailable ? s * 0.15f : 0f); }
            _eleStrip = new Rect(w - margin - sw, bottom, sw, top - bottom);
            _thrStrip = new Rect(margin, bottom, sw, top - bottom);
            float trimW = sw * 0.3f;
            _trimRect = new Rect(_eleStrip.x - gap - trimW, bottom, trimW, top - bottom);
        }

        /// <summary>A tall control strip: track, centre detent line, quarter ticks, the knob at its position.</summary>
        private void DrawStrip(Rect r, float pos, string label, string value)
        {
            float t = Mathf.Max(2f, r.width * 0.02f);
            GUI.color = new Color(1f, 1f, 1f, 0.16f); GUI.DrawTexture(ToGui(r), _solidTex);
            GUI.color = new Color(1f, 1f, 1f, 0.55f); DrawFrame(r, t);
            for (int i = -4; i <= 4; i++)
            {
                float y = r.center.y + i * 0.25f * r.height * 0.5f;
                GUI.color = new Color(1f, 1f, 1f, i == 0 ? 0.7f : 0.3f);
                float tw = i == 0 ? r.width : r.width * 0.4f;
                GUI.DrawTexture(ToGui(new Rect(r.center.x - tw * 0.5f, y - t * 0.5f, tw, t)), _solidTex);
            }
            float ky = r.center.y + Mathf.Clamp(pos, -1f, 1f) * r.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(GuiRectCentered(new Vector2(r.center.x, ky), r.width * 0.7f), _knobTex);
            GUI.color = Color.white;
            float lh = _styleFs * 1.5f;
            UiLayout.Label(new Rect(r.x - r.width * 0.25f, Screen.height - (r.yMax + lh), r.width * 1.5f, lh), label, _labelStyle);
        }

        /// <summary>FIRE (owner 2026-10-01): top left under MENU, a big target for a third finger while both thumbs stay on
        /// the pads (every touch is tracked on its own, so holding FIRE never steals a pad). Screen px, bottom-left origin.</summary>
        private static Rect FireRectTopLeft()
        {
            float s = Mathf.Min(Screen.width, Screen.height);
            float top = s * 0.02f + s * 0.055f + s * 0.015f;   // below the MENU button
            float w = s * 0.24f, h = s * 0.13f;
            return new Rect(s * 0.02f, Screen.height - top - h, w, h);
        }

        private bool HasEjectionSeat => _driver.Sim?.Aircraft?.Config?.EjectionSeat == true;
        private bool FireAvailable { get { _combat ??= GetComponent<CombatController>(); return _combat != null && _combat.GunsHot && !(_egress?.PilotOut ?? false); } }
        /// <summary>
        /// A whole-aircraft parachute (owner 2026-10-16: "if an airplane has a brs like a cirrus display
        /// deploy brs instead of bail out"). Where one is fitted nobody bails out at all, so the button says
        /// what it actually does — and once it is pulled there is nothing left to press.
        /// </summary>
        private bool HasBrs => _driver.Sim?.Aircraft?.Config?.Brs != null;
        private bool BrsPulled => _driver.Sim?.Aircraft?.Brs.Out == true;
        private bool BailAvailable => _egress != null && _egress.Current == PilotEgress.Phase.InCockpit && !(HasBrs && BrsPulled);
        /// <summary>EJECT exists only on types with an ejection seat (the F-86); everyone else bails out.</summary>
        private bool EjectAvailable => _egress != null && !_egress.PilotOut && HasEjectionSeat;

        /// <summary>Read touches (device) or the mouse (editor) and update each control's owning pointer.</summary>
        private void ReadPointers()
        {
            _brakeHeld = false;

            var pointers = new List<(int id, Vector2 pos, bool began, bool ended)>();
            if (TvButtonPad.Showing)
            {
                // TV + pad: the phone is TvButtonPad's grid, and these buttons are not drawn. Let go of anything held.
                _fireFinger = _lookFinger = _ejectFinger = int.MinValue; _lookDir = 0; _ejectHold = 0f;
            }
            else if (Input.touchCount > 0)
            {
                foreach (Touch t in Input.touches)
                {
                    bool began = t.phase == TouchPhase.Began;
                    bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                    pointers.Add((t.fingerId, t.position, began, ended));
                }
            }
            else
            {
                // Editor stand-in: one mouse pointer, id -1.
                bool down = Input.GetMouseButton(0);
                bool began = Input.GetMouseButtonDown(0);
                bool ended = Input.GetMouseButtonUp(0);
                if (down || ended)
                {
                    pointers.Add((-1, Input.mousePosition, began, ended));
                }
            }

            foreach (var p in pointers)
            {
                if (p.ended)
                {
                    if (p.id == _leftFinger) ReleaseLeft();
                    if (p.id == _rightFinger) ReleaseRight();
                    if (p.id == _lookFinger) { _lookFinger = int.MinValue; _lookDir = 0; }
                    if (p.id == _trimFinger) _trimFinger = int.MinValue; // trim holds where it was left
                    if (p.id == _ailTrimFinger) _ailTrimFinger = int.MinValue;
                    if (p.id == _rudTrimFinger) _rudTrimFinger = int.MinValue;
                    if (p.id == _fireFinger) _fireFinger = int.MinValue;
                    if (p.id == _ejectFinger) { _ejectFinger = int.MinValue; _ejectHold = 0f; } // released early: no eject
                    continue;
                }

                if (p.began)
                {
                    if (ViewPanel.BlocksTouch(p.pos)) continue;   // a tap on the open view menu is for the menu, not the stick
                    // EJECT is a HOLD (not an IMGUI tap): this pointer now owns the hold timer.
                    if (EjectAvailable && _ejectRect.Contains(p.pos) && _ejectFinger == int.MinValue) { _ejectFinger = p.id; _ejectHold = 0f; }
                    // Buttons fire on the touch itself (any finger, pads still held) — never through IMGUI's single pointer.
                    if (EjectAvailable && _ejectRect.Contains(p.pos)) continue;
                    if (FireAvailable && _fireRect.Contains(p.pos) && _fireFinger == int.MinValue) { _fireFinger = p.id; continue; }
                    if (_bailRect.Contains(p.pos) && BailAvailable) { _tapped.Add(Btn.Bail); continue; }
                    if (_resetRect.Contains(p.pos)) { _tapped.Add(Btn.Reset); continue; }
                    if (_acftRect.Contains(p.pos)) { _tapped.Add(Btn.Aircraft); continue; }
                    if (_brakeRect.Contains(p.pos)) { _flapTapThird = Mathf.Clamp((p.pos.x < _brakeRect.x + _brakeRect.width * .5f ? 0 : p.pos.x < _brakeRect.x + _brakeRect.width * .75f ? 1 : 2), 0, 2); _tapped.Add(Btn.Flaps); continue; }
                    if (_gearRect.Contains(p.pos)) { _tapped.Add(Btn.Gear); continue; }
                    if (SmokeAvailable && _smokeRect.Contains(p.pos)) { _tapped.Add(Btn.Smoke); continue; }
                    if (_camRefRect.Contains(p.pos)) { _tapped.Add(Btn.CamRef); continue; }
                    if (_ctlModeRect.width > 0f && _ctlModeRect.Contains(p.pos)) { _tapped.Add(Btn.CtlMode); continue; }
                    if (LookAvailable && _lookCentreRect.Contains(p.pos)) { _tapped.Add(Btn.LookCentre); continue; }
                    if (LookAvailable && _lookLeftRect.Contains(p.pos) && _lookFinger == int.MinValue)
                    { _lookFinger = p.id; _lookDir = -1; continue; }
                    if (LookAvailable && _lookRightRect.Contains(p.pos) && _lookFinger == int.MinValue)
                    { _lookFinger = p.id; _lookDir = 1; continue; }
                    if (_towRect.Contains(p.pos)) { _tapped.Add(Btn.Tow); continue; }

                    if (DeskMode) continue;   // Mac: no touch pads / trim sliders to grab
                    if (TouchSticksHidden) continue;   // a gamepad is connected: the touch sticks are hidden
                    if (_trimRect.Contains(p.pos) && _trimFinger == int.MinValue) _trimFinger = p.id;
                    else if (_ailTrimRect.Contains(p.pos) && _ailTrimFinger == int.MinValue) _ailTrimFinger = p.id;
                    else if (_rudTrimRect.Contains(p.pos) && _rudTrimFinger == int.MinValue) _rudTrimFinger = p.id;
                    else if (StripMode)
                    {
                        if (_eleStrip.Contains(p.pos) && _rightFinger == int.MinValue) _rightFinger = p.id;
                        else if ((StripThrottle || StripBrakes) && _thrStrip.Contains(p.pos) && _leftFinger == int.MinValue) _leftFinger = p.id;
                    }
                    else if (NearPad(p.pos, _leftCenter) && _leftFinger == int.MinValue) _leftFinger = p.id;
                    else if (NearPad(p.pos, _rightCenter) && _rightFinger == int.MinValue) _rightFinger = p.id;
                }

                if (p.id == _leftFinger) DriveLeft(p.pos);
                else if (p.id == _rightFinger) DriveRight(p.pos);
                else if (p.id == _trimFinger) DriveTrim(p.pos);
                else if (p.id == _ailTrimFinger) _aileronTrim = Mathf.Clamp((p.pos.x - _ailTrimRect.center.x) / (_ailTrimRect.width * 0.5f), -1f, 1f);
                else if (p.id == _rudTrimFinger) _rudderTrim = Mathf.Clamp((p.pos.x - _rudTrimRect.center.x) / (_rudTrimRect.width * 0.5f), -1f, 1f);
                else if (p.id == _ejectFinger)
                {
                    if (_ejectRect.Contains(p.pos)) _ejectHold += Time.unscaledDeltaTime;
                    else { _ejectFinger = int.MinValue; _ejectHold = 0f; }   // slid off the button: cancel
                }
                else if (p.id == _lookFinger
                         && !(_lookDir < 0 ? _lookLeftRect : _lookRightRect).Contains(p.pos))
                { _lookFinger = int.MinValue; _lookDir = 0; }                                             // slid off: stop panning
                else if (p.id == _fireFinger && !_fireRect.Contains(p.pos)) _fireFinger = int.MinValue;   // slid off: cease fire
            }
            // Hand the held direction to CockpitView, which runs the same tap-vs-pan window a stick's hat runs
            // through. Zero every frame no finger is down, or the head would keep turning after the lift.
            if (_cockpit != null) _cockpit.OnScreenHat = new Vector2Int(_lookFinger != int.MinValue ? _lookDir : 0, 0);
            FireEjectAndTaps();
        }

        /// <summary>Split screen with the tray off (<see cref="TrayLive"/>): no touch reaches a tray control — every finger is
        /// let go, as when a menu opens — but the pad's FIRE, a pending eject hold's cancel and the queued taps (keyboard,
        /// joystick, gamepad flaps / gear) still run.</summary>
        private void PadOnly()
        {
            _brakeHeld = false;
            if (_leftFinger != int.MinValue) ReleaseLeft();
            if (_rightFinger != int.MinValue) ReleaseRight();
            _trimFinger = _ailTrimFinger = _rudTrimFinger = _fireFinger = _ejectFinger = int.MinValue;
            _ejectHold = 0f; _lookFinger = int.MinValue; _lookDir = 0;
            if (_cockpit != null) _cockpit.OnScreenHat = Vector2Int.zero;
            FireEjectAndTaps();
        }

        private void FireEjectAndTaps()
        {
            _combat ??= GetComponent<CombatController>();
            // Fire: the on-screen button, the space bar, or a game controller's A / right shoulder (MFi / PlayStation /
            // Xbox over Bluetooth — the phone's own volume, Action and Camera Control buttons are reserved by iOS for
            // camera apps and cannot be read here).
            // A pad the Input System can see fires from A alone (GamepadInput): its right shoulder is the throttle,
            // its Menu is the menu and its d-pad the trim and flaps, and the old Input Manager button numbers below
            // land on exactly those buttons — so they only count when no such pad is connected.
            if (_combat != null)
            {
                var gp = GamepadInput.Now;
                _combat.Firing = _fireFinger != int.MinValue || Input.GetKey(KeyCode.Space) || gp.Fire
                    || (!gp.Connected && (Input.GetKey(KeyCode.JoystickButton14) || Input.GetKey(KeyCode.JoystickButton9) || Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.JoystickButton5)))
                    || JoystickInput.Held("guns");   // a calibrated joystick's GUNS button (or an RC radio's button channel)
            }

            if (_ejectFinger != int.MinValue && _ejectHold >= EjectHoldSec)
            {
                _ejectFinger = int.MinValue;
                _ejectHold = 0f;
                if (EjectAvailable) _egress.Eject();
            }
            ActOnTaps();
        }

        /// <summary>The GEAR slot is shown for a retractable undercarriage, and for the WHEELS / SKIS selector on fixed gear.</summary>
        private bool GearSlot => _driver.Sim?.Aircraft is { } a && (a.Config.RetractableGear || a.Skis != null);

        private void ActOnTaps()
        {
            if (_tapped.Count == 0) return;
            var tow = GetComponent<TowController>();
            var ac = _driver.Sim?.Aircraft;
            foreach (Btn b in _tapped)
            {
                switch (b)
                {
                    case Btn.Reset: DoReset(); break;
                    case Btn.Aircraft: CycleAircraft(); break;
                    case Btn.Bail: if (BailAvailable) PullTheHandle(); break;
                    case Btn.Flaps:
                        // Three positions (owner 2026-09-15): the tap's third of the slot picks 0 / 50 / 100 directly, so 50 → 0
                        // never has to pass through 100.
                        if (_driver.HasFlaps && ac != null && !Practice.PracticeController.AnyActive) ac.FlapFraction = _flapTapThird switch { 0 => 0.0, 1 => 0.5, _ => 1.0 };
                        break;
                    case Btn.Gear:
                        if (ac?.Config?.RetractableGear == true) ac.SetGear(!ac.GearDown);
                        // Wheel-skis on fixed gear (Ty 2026-11-05): the slot is the WHEELS / SKIS selector, the same lever the
                        // cockpit's shared part (SkiSelectorLever) shows and a click or poke on it moves.
                        else if (ac?.Skis != null) SkiSelectorLever.Move(ac, "button");
                        // Floatplanes with a water-rudder retract (the J-3 floats, Ty 2026-11-02 t278u): the same slot
                        // hangs the red handle on the front-seat hook (rudder UP) or lets it go (spring: DOWN). In VR the
                        // hand does it on the handle itself (CockpitWaterRudder); this is the phone / controller way.
                        else if (ac != null && ac.HasWaterRudderRetract && ac.WaterRudderHeldUp01 == null) ac.ToggleWaterRudder();
                        break;
                    case Btn.Smoke:
                        if (SmokeAvailable) _smoke.Toggle();
                        break;
                    case Btn.CamRef:
                        // A lesson that forces Track still shows it; the tap sets the choice it goes back to afterwards.
                        SessionSettings.CameraReferenceMode = FlyingGame.Core.CameraReference.Toggle(SessionSettings.CameraReferenceMode);
                        break;
                    case Btn.CtlMode:
                        // TOUCH <-> GYRO without going into the menus (Ty 2026-11-10). Picking the layout is what
                        // turns the gyro on, exactly as the OPTIONS row does — the two must not disagree.
                        TvGyroLayout = !TvGyroLayout;
                        TiltSteering.Enabled = TvGyroLayout;
                        if (TvGyroLayout) TiltSteering.Centre();   // zero it where the phone is being held right now
                        break;
                    case Btn.LookCentre:
                        if (LookAvailable) _cockpit.Centre();
                        break;
                    case Btn.Tow:
                        if (tow != null && ac?.Config?.Propulsion == null)
                        {
                            if (tow.Towing) tow.PullRelease();
                            else if (_driver.GroundStart && tow.Tow == null) tow.StartTow();
                        }
                        break;
                }
            }
            _tapped.Clear();
        }

        /// <summary>Draw a button (the tap is read in ReadPointers, so it works with the pads held).</summary>
        private void Button(Rect r, string label) { Rect g = ToGui(r); UiLayout.ButtonLabel(g, label, _btnStyle); }   // shrinks to fit (no-overlap rule)

        private bool NearPad(Vector2 pos, Vector2 center)
        {
            float r = _half * (1f + GrabSlop);
            return Mathf.Abs(pos.x - center.x) <= r && Mathf.Abs(pos.y - center.y) <= r;
        }

        private void DriveLeft(Vector2 pos)
        {
            if (StripMode)
            {
                // Throttle strip: bottom = idle, top = full (lever travel the whole strip height).
                float t01 = Mathf.Clamp01((pos.y - _thrStrip.y) / Mathf.Max(1f, _thrStrip.height));
                if (StripBrakes) { _stripBrake01 = 1f - t01; _rudder = 0f; _vrHandle = -1f; return; }   // brakes / spoiler: top = off, pull DOWN = more
                _throttle = AxisForFraction(IdleFraction + (1f - IdleFraction) * t01);
                _rudder = 0f;
                return;
            }
            _leftKnob = ClampToPad(pos, _leftCenter);
            Vector2 n = (_leftKnob - _leftCenter) / _half;
            _rudder = Shape(n.x);
            _throttle = Shape(n.y); // sticky: last value persists after release (see ReleaseLeft)
        }

        private void DriveRight(Vector2 pos)
        {
            if (StripMode)
            {
                // Elevator strip: the full strip height is the full elevator travel (far less twitchy than the pad).
                float ny = Mathf.Clamp((pos.y - _eleStrip.center.y) / (_eleStrip.height * 0.5f), -1f, 1f);
                _stripY = ny;
                _aileron = 0f;
                _elevator = Shape(InvertElevator ? -ny : ny);
                return;
            }
            _rightKnob = ClampToPad(pos, _rightCenter);
            Vector2 n = (_rightKnob - _rightCenter) / _half;
            if (TvLookPad)
            {
                // The gyro is flying: this pad turns the head. Raw deflection, not Shape() — the stick's expo is
                // there to make small control inputs fine, and a view wants the opposite, an even sweep.
                _lookPad = n;
                _aileron = 0f;
                _elevator = 0f;
                return;
            }
            _lookPad = Vector2.zero;
            _aileron = Shape(n.x); // pad/stick RIGHT (n.x>0) = positive aileron = roll RIGHT (sim convention)
            _elevator = Shape(InvertElevator ? -n.y : n.y);
        }

        /// <summary>Trim slider follows the finger; same sense as the stick (up = nose down unless inverted).</summary>
        private void DriveTrim(Vector2 pos)
        {
            float n = (pos.y - _trimRect.center.y) / (_trimRect.height * 0.5f);
            _pitchTrim = Mathf.Clamp(n, -1f, 1f);
        }

        /// <summary>A pad connected with a thumb on a touch stick: let go of it, so the hidden stick can't hold a deflection.</summary>
        private void ApplyTouchSticksHidden()
        {
            bool hidden = TouchSticksHidden;
            if (hidden && !_touchSticksWereHidden)
            {
                if (_leftFinger != int.MinValue) ReleaseLeft();
                if (_rightFinger != int.MinValue) ReleaseRight();
                _trimFinger = _ailTrimFinger = _rudTrimFinger = int.MinValue;   // trims hold where they were left
            }
            if (hidden != _touchSticksWereHidden)
                Debug.Log(hidden ? "[Touch] gamepad connected: touch sticks hidden" : "[Touch] no gamepad: touch sticks shown");
            _touchSticksWereHidden = hidden;
            bool live = TrayLive;
            if (live != _trayWasLive)
                Debug.Log(live ? "[Touch] tray live (drawn and hit-tested)" : "[Touch] split screen, P1 on a pad: tray off, drawing and hit areas together; RESET / handle on the FLIGHT panel");
            _trayWasLive = live;
        }
        private bool _trayWasLive = true;

        private void ReleaseLeft()
        {
            _leftFinger = int.MinValue;
            if (StripBrakes && _driver?.Sim?.Aircraft?.Config?.Propulsion != null) _stripBrake01 = 0f;   // toe brakes spring off (a glider's spoiler stays)
            _rudder = 0f;                 // rudder springs back
            // throttle holds — knob stays at its vertical height, recentre horizontally.
            _leftKnob = IdleLeftKnob();
        }

        private void ReleaseRight()
        {
            _rightFinger = int.MinValue;
            _stripY = 0f;
            _lookPad = Vector2.zero;   // the view stops where it was: SpringCentre decides whether it walks back
            _aileron = 0f;
            _elevator = 0f;
            _rightKnob = _rightCenter;   // both spring back
        }

        /// <summary>Editor keyboard fallback (only when a control isn't under a live pointer).</summary>
        private void MergeKeyboardFallback()
        {
            if (_rightFinger == int.MinValue)
            {
                float kx = Key(KeyCode.RightArrow) - Key(KeyCode.LeftArrow); // Right arrow = positive = roll right
                float ky = Key(KeyCode.UpArrow) - Key(KeyCode.DownArrow); // up = nose down
                if (kx != 0f) _aileron = kx;
                if (ky != 0f) _elevator = InvertElevator ? -ky : ky;
            }

            if (_leftFinger == int.MinValue)
            {
                float rx = Key(KeyCode.D) - Key(KeyCode.A);
                if (rx != 0f) _rudder = rx;
                // S/W nudge the sticky throttle (S = aft/idle-brake, W = forward/power).
                float tv = Key(KeyCode.W) - Key(KeyCode.S);
                if (tv != 0f)
                {
                    _throttle = Mathf.Clamp(_throttle + tv * Time.unscaledDeltaTime, -1f, 1f);   // (real time, in slow motion too)
                    _leftKnob = IdleLeftKnob();
                }
            }

            if (_trimFinger == int.MinValue)
            {
                // = / - nudge pitch trim (same sense as the slider: = is up).
                float tt = Key(KeyCode.Equals) - Key(KeyCode.Minus);
                if (tt != 0f) _pitchTrim = Mathf.Clamp(_pitchTrim + tt * 0.5f * Time.unscaledDeltaTime, -1f, 1f);
            }

            if (Input.GetKey(KeyCode.B)) _brakeHeld = true;
            if (Input.GetKeyDown(KeyCode.R)) DoReset();
            if (Input.GetKeyDown(KeyCode.E) && MenuHandleAvailable) MenuPullHandle();   // the handle: bail out (eject / BRS where fitted) — one press (Ty 2026-11-05)
            for (int i = 0; i < Cycle.Length && i < 10; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _driver.SwitchAircraft(Cycle[i]);
            }
        }

        /// <summary>Left-pad Y knob height, 0 (bottom) .. 1 (top). The tow controller reads it for the tug's power.</summary>
        public float LeftPadFraction => (_throttle + 1f) * 0.5f;

        /// <summary>Glider speed brakes: stowed above 50 % of the pad, fully out at 25 % (owner spec).</summary>
        public float SpoilerFraction => Mathf.Clamp01((0.5f - LeftPadFraction) / (0.5f - IdleFraction));

        /// <summary>Left-pad Y knob (-1..1) → throttle lever (0..1 power) and pad braking (0..1).</summary>
        private (float throttle01, float brake) SplitLeftAxis()
        {
            float k = LeftPadFraction;                         // 0 bottom .. 1 top
            float throttle01 = Mathf.Clamp01((k - IdleFraction) / (1f - IdleFraction));
            float brake = k < BrakeStartFraction ? Mathf.Clamp01((BrakeStartFraction - k) / BrakeStartFraction) : 0f;
            return (throttle01, brake);
        }

        private void PublishToDriver()
        {
            StickAileron = _aileron;
            StickElevator = _elevator;   // (_elevator already has InvertElevator applied where it is read)
            // After bail out / eject the stick steers the CANOPY and sets the free-fall body position
            // (PilotEgress reads StickAileron and StickElevator); the
            // abandoned aircraft's inputs are frozen by PilotEgress, so nothing is published here.
            if (_egress != null && _egress.PilotOut) return;

            (float throttle01, float padBrake) = SplitLeftAxis();
            float brake = Mathf.Max(Mathf.Max(padBrake, _brakeHeld ? 1f : 0f), _questBrake);
            bool poweredNow = _driver.Sim?.Aircraft?.Config?.Propulsion != null;
            if (StripMode && StripBrakes) brake = poweredNow ? _stripBrake01 : Mathf.Clamp01((_stripBrake01 - 0.85f) / 0.15f);
            // A glider's dive-brake handle moved by a hand in the headset (VrCockpitHands): its last 15 % is the wheel
            // brake, the same band as the strip's. The spoiler itself is already on the knob (SetSpoilerHandle).
            if (!poweredNow && _vrHandle >= 0f) brake = Mathf.Max(brake, (float)FlyingGame.Core.Vr.SpoilerHandle.WheelBrake(_vrHandle));
            // RUDDER PEDALS (docs/RUDDER-PEDALS.md) join here, so there is still one input path: each wheel gets the
            // harder of its toe brake and the hand's brake (biased by the hand's rudder, as before), carried to the
            // sim as its brake + bias; the rudder below adds the pedals to the hand's. No pedals = exactly as before.
            FlyingGame.Core.PedalFrame pedals = RudderPedals.Frame;
            float handBias = brake > 0f ? Mathf.Clamp(_rudder * BrakeRudderBias, -1f, 1f) : 0f;
            (double simBrake, double simBias) = FlyingGame.Core.PedalBlend.Combine(brake, handBias, pedals);
            _driver.Sim.Aircraft.BrakeInput = simBrake;
            _driver.Sim.Aircraft.BrakeBias = simBias;
            // Trim biases the elevator like a real trim tab: hands-off stick still holds the trimmed attitude.
            float trim = (InvertElevator ? -_pitchTrim : _pitchTrim) * TrimAuthority;
            // ThrottleLever: -1 = full forward (full power), +1 = full aft (idle). Powered: lever = 1 - 2·throttle01.
            // Glider: the sim reads max(0, lever) as the speed-brake fraction → lever = SpoilerFraction
            // (stowed above 50 % of the pad, full out at 25 %; the brake band below is the wheel brake).
            bool powered = _driver.Sim?.Aircraft?.Config?.Propulsion != null;
            float lever = powered ? 1f - 2f * throttle01 : SpoilerFraction;
            if (StripMode && StripBrakes) lever = powered ? 1f : _stripBrake01;   // powered: idle (the game holds it); glider: the spoiler handle
            // Aileron / rudder trim (RC-transmitter style): a sticky bias under the stick, like the pitch trim.
            // Reversible controls (owner 2026-10-02): a pad with no finger (and no key) lets go of its controls — on a
            // cable-controlled aircraft those surfaces then trail with the air loads (the axis values are just the trims).
            // With a gamepad connected a centred thumb stick holds the surfaces unless the player chose stick-free
            // (OPTIONS › Controls, off by default with a pad; GamepadShaping.ControlsFree). Read every frame: hot-plug flips it.
            // FlyingGame.Sim.PilotControls is the composition (engine-free, so the launch tests fly the same one).
            // A hand in the headset on the cockpit's stick is never "let go"; feet on the rudder pedals hold the rudder,
            // and the pedals' rudder adds to the hand's (PedalBlend).
            double rudderIn = FlyingGame.Core.PedalBlend.Rudder(_rudder, pedals);
            // Tilt flies the three flight axes when it is on, and nothing else: the throttle, flaps, gear, spoiler and
            // water rudder below are untouched (Ty 2026-11-08). The pads keep their AUTHORITY — a finger on a pad still
            // adds to the tilt — so a pilot can hold the phone level and fly the rudder with his thumb.
            double aileronIn = _aileron, elevatorIn = _elevator;
            bool tilting = TiltSteering.TryAxes(out double tAil, out double tEle, out double tRud);
            if (tilting)
            {
                aileronIn = System.Math.Clamp(aileronIn + tAil, -1.0, 1.0);
                elevatorIn = System.Math.Clamp(elevatorIn + tEle, -1.0, 1.0);
                if (TiltSteering.RudderFromYaw) rudderIn = System.Math.Clamp(rudderIn + tRud, -1.0, 1.0);
            }
            // Hands are ON while tilting, whatever the pads say. Otherwise a reversible aeroplane would let its
            // surfaces trail (PilotControls' rightFree/leftFree) the moment the pilot lifted his thumbs — which with
            // tilt is the normal way to fly it.
            _driver.Inputs = PilotControls.Compose(aileronIn, elevatorIn, rudderIn, _aileronTrim * TrimAuthority, trim, _rudderTrim * TrimAuthority,
                lever, tilting || _rightFinger != int.MinValue || _vrStickHeld,
                tilting || _leftFinger != int.MinValue || pedals.Active,
                GamepadInput.Now.Connected, SessionSettings.GamepadStickFree);
        }

        // ---- Meta Quest (native OpenXR headset) -------------------------------
        // The Touch controllers feed the SAME state the pads and a joystick feed (aileron, elevator, rudder, the
        // sticky throttle knob, trim, brakes, the tap buttons), so the flight model has one input path. The mapping
        // itself is QuestControlMapper in Core (tested in QuestControlsTests); the table is docs/quest-controls.md.

        private readonly FlyingGame.Core.Vr.QuestControlMapper _quest = new();
        private float _questBrake;
        private bool _questFlying, _questWaitRelease;
        /// <summary>A headset hand is holding the cockpit's own stick (VrCockpitHands): the stick is held even at neutral.</summary>
        private bool _vrStickHeld;
        /// <summary>Where a hand in the headset last left a glider's dive-brake handle, 0..1, or −1 when the knob (a
        /// thumbstick, a pad, a reset) has moved it since. Only its wheel-brake band needs keeping: the spoiler part is
        /// written into the knob itself, so everything else carries on from there.</summary>
        private float _vrHandle = -1f;

        /// <summary>The glider's dive-brake handle, 0 stowed .. 1 full aft (the last 15 % the wheel brake): the hand's
        /// own value while it has one, else where the knob or the strip puts it (<see cref="FlyingGame.Core.Vr.SpoilerHandle"/>).</summary>
        public float SpoilerHandle01
        {
            get
            {
                if (_vrHandle >= 0f) return _vrHandle;
                if (StripMode && StripBrakes) return _stripBrake01;
                (_, float padBrake) = SplitLeftAxis();
                return (float)FlyingGame.Core.Vr.SpoilerHandle.FromKnob(SpoilerFraction, padBrake);
            }
        }

        /// <summary>A hand moved the dive-brake handle: the knob (and the strip) go to its spoiler, and its wheel-brake
        /// band is kept until something else moves the knob. Released, it stays: nothing springs it back.</summary>
        public void SetSpoilerHandle(float handle01)
        {
            handle01 = Mathf.Clamp01(handle01);
            _vrHandle = handle01;
            _throttle = AxisForFraction((float)FlyingGame.Core.Vr.SpoilerHandle.KnobFor(handle01, IdleFraction));
            _leftKnob = IdleLeftKnob();
            _stripBrake01 = handle01;
        }

        /// <summary>The pitch-trim LEVER, −1 full forward (nose down) .. +1 full aft (nose up): the rig's `trim`, i.e. the
        /// negative of the elevator bias (AirframeVisual.RigInputsNow). Setting it moves the same trim the slider, the
        /// keys and the A / B buttons move.</summary>
        public float PitchTrimLever
        {
            get => InvertElevator ? _pitchTrim : -_pitchTrim;
            set => _pitchTrim = Mathf.Clamp(InvertElevator ? value : -value, -1f, 1f);
        }

        /// <summary>The hands on the cockpit's own controls, into the same state the thumbsticks feed. Runs with
        /// controllers or with tracked hands alone.</summary>
        private void ApplyCockpitHands(VrCockpitHands hands)
        {
            if (hands == null) return;
            VrCockpitHands.GateRightFinger = _rightFinger; VrCockpitHands.GateLeftFinger = _leftFinger;   // (the [VrHands] line)
            // A hand on the stick owns pitch and roll unless a thumbstick is pushed past its dead zone
            // (HandPadArbiter); rudder and throttle stay with the pad unless a hand holds the throttle lever.
            if (hands.StickHeld && !hands.PadOwnsStick && _rightFinger == int.MinValue)
            {
                // The held stick, in the sim's own sense (already linear in stick angle), less the trim this class adds
                // back in PublishToDriver: the surface goes where the hand put the stick, trimmed or not.
                _aileron = Mathf.Clamp(hands.AileronInput - TrimAileron, -1f, 1f);
                _elevator = Mathf.Clamp(hands.ElevatorInput - TrimElevator, -1f, 1f);
                _vrStickHeld = true;
            }
            if (_leftFinger == int.MinValue)
            {
                if (hands.ThrottleHeld) { _throttle = AxisForFraction(IdleFraction + (1f - IdleFraction) * hands.Throttle01); _leftKnob = IdleLeftKnob(); }
                if (hands.SpoilerHandleHeld) SetSpoilerHandle(hands.SpoilerHandle01);
            }
            if (hands.TrimLeverHeld && _trimFinger == int.MinValue) PitchTrimLever = hands.TrimLever;
        }

        private void QuestInput()
        {
            _questBrake = 0f;
            _vrStickHeld = false;
            if (!XrHeadset.Active) { _questFlying = false; return; }
            // The press that closed the VR menu (A, B, X, Y, a trigger) must not carry on into trim, flaps or guns:
            // after the menu, nothing is read until every button is up.
            if (XrHeadset.MenuOpen) { _questWaitRelease = true; return; }
            var f = XrControllers.Read(out bool any);
            if (!any || f.HandsOnly)
            {
                // Controllers put down or asleep: LET GO of the stick and rudder rather than holding the last
                // deflection (a stick latched full right would keep rolling). The throttle is a lever and stays.
                if (_questFlying && _rightFinger == int.MinValue) { _aileron = 0f; _elevator = 0f; }
                if (_questFlying && _leftFinger == int.MinValue) _rudder = 0f;
                _questFlying = false;
                // Tracked hands still reach the cockpit's own controls (docs/vr-cockpit.md, "Hands").
                VrCockpitHands th = VrCockpitHands.Current;
                if (th != null) { th.PadOwnsStick = false; th.Tick(); ApplyCockpitHands(th); }
                return;
            }
            if (_questWaitRelease)
            {
                if (f.A || f.B || f.X || f.Y || f.LeftTrigger > 0.2 || f.RightTrigger > 0.2 || f.LeftStickClick || f.RightStickClick) return;
                _questWaitRelease = false;
            }
            _questFlying = true;
            float dt = Time.unscaledDeltaTime;
            _quest.InvertElevator = InvertElevator;
            (float thr01, _) = SplitLeftAxis();
            // In the cockpit the grips belong to the hands on the aeroplane's own controls (VrCockpitHands): the
            // mapper's grab-anywhere throttle and the smoke toggle only see a grip that isn't closing on a control.
            VrCockpitHands hands = VrCockpitHands.Current;
            if (hands != null) { hands.PadOwnsStick = FlyingGame.Core.Vr.HandPadArbiter.PadStickActive(f.RightX, f.RightY); hands.Tick(); hands.MaskGrips(ref f); }
            var c = _quest.Step(f, dt, thr01);
            if (_rightFinger == int.MinValue) { _aileron = (float)c.Aileron; _elevator = (float)c.Elevator; }
            if (_leftFinger == int.MinValue)
            {
                _rudder = (float)c.Rudder;
                if (c.ThrottleMoved) { _throttle = AxisForFraction(IdleFraction + (1f - IdleFraction) * (float)c.Throttle01); _leftKnob = IdleLeftKnob(); _vrHandle = -1f; }
            }
            _questBrake = (float)c.Brake;
            if (c.PitchTrimRate != 0.0) _pitchTrim = Mathf.Clamp(_pitchTrim + (float)c.PitchTrimRate * dt, -1f, 1f);
            ApplyCockpitHands(hands);
            var ac = _driver.Sim?.Aircraft;
            if ((c.FlapsDown || c.FlapsUp) && ac != null)
            {
                // From where the flaps ARE (a lesson or a reset may have set them), one notch at a time.
                int step = Mathf.Clamp(Mathf.RoundToInt((float)ac.FlapFraction * 2f) + (c.FlapsDown ? 1 : -1), 0, 2);
                _deskFlapStep = step; _flapTapThird = step; _tapped.Add(Btn.Flaps);
            }
            if (c.Gear) _tapped.Add(ac?.Config?.Propulsion == null ? Btn.Tow : Btn.Gear);   // a glider has no gear: the stick click is TOW / RELEASE
            if (c.Smoke) _tapped.Add(Btn.Smoke);
            _combat ??= GetComponent<CombatController>();
            if (c.Fire && FireAvailable && _combat != null) _combat.Firing = true;
            ActOnTaps();
        }

        /// <summary>True when a touch that begins at <paramref name="p"/> (screen pixels, y up) belongs to one of the glass
        /// controls — a button, a trim slider, a stick pad or strip — so the cockpit's own tap targets (CockpitTaps: the
        /// card, the altimeter, the g-meter) leave it alone. The same tests, in the same order, as the touch loop.</summary>
        public bool IsControlAt(Vector2 p)
        {
            if (ViewPanel.BlocksTouch(p)) return true;
            if (!TrayLive) return false;   // split screen, tray off: none of its rects is a control
            if (EjectAvailable && _ejectRect.Contains(p)) return true;
            if (FireAvailable && _fireRect.Contains(p)) return true;
            if (BailAvailable && _bailRect.Contains(p)) return true;
            if (_resetRect.Contains(p) || _acftRect.Contains(p) || _brakeRect.Contains(p) || _gearRect.Contains(p) || _camRefRect.Contains(p) || _towRect.Contains(p)) return true;
            if (SmokeAvailable && _smokeRect.Contains(p)) return true;
            if (LookAvailable && (_lookCentreRect.Contains(p) || _lookLeftRect.Contains(p) || _lookRightRect.Contains(p))) return true;
            if (DeskMode || TouchSticksHidden) return false;
            if (_trimRect.Contains(p) || _ailTrimRect.Contains(p) || _rudTrimRect.Contains(p)) return true;
            if (StripMode) return _eleStrip.Contains(p) || ((StripThrottle || StripBrakes) && _thrStrip.Contains(p));
            return NearPad(p, _leftCenter) || NearPad(p, _rightCenter);
        }

        // The VR menu's handles on the buttons a pilot would otherwise tap on the glass.
        public void MenuReset() => DoReset();
        public bool MenuHandleAvailable => EjectAvailable || BailAvailable;
        public string MenuHandleLabel => EjectAvailable ? "Eject" : HasBrs ? "Deploy BRS" : "Bail out";
        public void MenuPullHandle()
        {
            if (EjectAvailable) _egress.Eject();
            else if (BailAvailable) PullTheHandle();
        }

        // ---- shaping helpers -------------------------------------------------

        // Pads pass the RAW stick position. Dead zone / expo live in AircraftConfig (applied once by
        // Aircraft.ShapeAxis); shaping here too was stacking two dead zones and two expo curves, so a
        // half-stick rudder input only delivered ~22 % deflection.
        private static float Shape(float v) => Mathf.Clamp(v, -1f, 1f);

        /// <summary>Square pad: clamp each axis independently.</summary>
        private Vector2 ClampToPad(Vector2 pos, Vector2 center) =>
            new(Mathf.Clamp(pos.x, center.x - _half, center.x + _half),
                Mathf.Clamp(pos.y, center.y - _half, center.y + _half));

        private static float Key(KeyCode k) => Input.GetKey(k) ? 1f : 0f;

        /// <summary>
        /// The handle. On a type with a ballistic parachute it fires that and everybody stays aboard; on
        /// everything else the pilot goes over the side as before. The engine is shut down either way —
        /// the Cirrus checklist has mixture to idle cut-off before the handle, and a running engine under a
        /// canopy is only a way of swinging the aeroplane about.
        /// </summary>
        private void PullTheHandle()
        {
            var ac = _driver.Sim?.Aircraft;
            if (HasBrs && ac != null)
            {
                double ground = FlyingGame.Core.WorldTerrain.GroundHeightAt(ac.State.Position.X, ac.State.Position.Y);
                double agl = -ac.State.Position.Z - ground;
                if (ac.DeployBrs(agl))
                {
                    _throttle = 1f;        // lever +1 is CLOSED ((1 - lever) / 2)
                    GetComponent<FlightAudio>()?.EjectionSeat();   // the rocket: the loudest thing the aeroplane has
                }
                return;
            }
            _egress.BailOut();
        }

        /// <summary>
        /// TV + GAME CONTROLLER (TvButtonPad): a big button on the phone's grid presses the SAME button this class owns —
        /// the same queue the on-screen buttons and the pad's d-pad use, so the two can never drift apart. Flaps step
        /// from where the flaps ARE, as the d-pad does.
        /// </summary>
        public void PressFromPad(FlyingGame.Core.PadButton b)
        {
            switch (b)
            {
                case FlyingGame.Core.PadButton.Reset: _tapped.Add(Btn.Reset); break;
                case FlyingGame.Core.PadButton.Aircraft: _tapped.Add(Btn.Aircraft); break;
                case FlyingGame.Core.PadButton.Gear: _tapped.Add(Btn.Gear); break;
                case FlyingGame.Core.PadButton.Smoke: _tapped.Add(Btn.Smoke); break;
                case FlyingGame.Core.PadButton.CamRef: _tapped.Add(Btn.CamRef); break;
                case FlyingGame.Core.PadButton.LookCentre: _tapped.Add(Btn.LookCentre); break;
                case FlyingGame.Core.PadButton.FlapsUp:
                case FlyingGame.Core.PadButton.FlapsDown:
                    int at = Mathf.RoundToInt((float)(_driver.Sim?.Aircraft?.FlapFraction ?? 0.0) * 2f);
                    _deskFlapStep = Mathf.Clamp(at + (b == FlyingGame.Core.PadButton.FlapsDown ? 1 : -1), 0, 2);
                    _flapTapThird = _deskFlapStep; _tapped.Add(Btn.Flaps);
                    break;
            }
        }

        private void DoReset()
        {
            _rudder = _aileron = _elevator = 0f;
            _leftFinger = _rightFinger = _trimFinger = int.MinValue;
            _rightKnob = _rightCenter;
            if (LocalSplit.Active) LocalCoop.MarkP1OwnReset();   // P1's RESET is P1's flight only: the second player flies on
            _driver.ResetFlight(); // fires AircraftChanged → PresetTrim
        }

        private void CycleAircraft()
        {
            int idx = System.Array.IndexOf(Cycle, _driver.AircraftId);
            _driver.SwitchAircraft(Cycle[(idx + 1 + Cycle.Length) % Cycle.Length]);
        }

        // ---- rendering (display only) ----------------------------------------

        private void OnGUI()
        {
            // In a headset a screen-space overlay smears across both eyes at the wrong depth. Codex's
            // VR HUD replaces these; the MENUS deliberately keep drawing, or there would be no way to
            // switch VR back off.
            if (VrMode.SuppressFlatOverlays) return;
            if (SessionSettings.MenuOpen || SessionSettings.ReplayActive) return;   // the replay bar owns the tray
            if (TvButtonPad.Showing) return;   // TV + pad: the phone is the button grid
            if (!TrayLive) return;   // split screen: the tray is off as a whole, drawing and hit areas together
            EnsureStyles();

            // Portrait: opaque tray under the controls. The camera only clears its own (upper) viewport,
            // so this also guarantees the strip below it never shows stale frame contents.
            if (ScreenLayout.Portrait && !ScreenLayout.FullScreenView)   // (gamepad: no tray, the view fills the screen)
            {
                GUI.color = new Color(0.07f, 0.09f, 0.12f, 1f);
                GUI.DrawTexture(ToGui(ScreenLayout.TrayRect), _solidTex);
                GUI.color = Color.white;
            }
            // A lesson card (briefing / debrief) is up: the pads and buttons step aside where they would sit under it — except a
            // lesson's tall STRIPS (owner 2026-10-06: "the elevator control needs to appear before the game launches so the user
            // knows where to put their thumb"): they stay, drawn over the card's backdrop, with a pointer.
            bool sticksHidden = TouchSticksHidden;   // a gamepad is connected (hot-plug: re-read every frame)
            bool stripBriefing = UiLayout.Modal && StripMode && !sticksHidden && Practice.PracticeController.AnyActive;
            if (UiLayout.Modal && (!ScreenLayout.Portrait || DeskMode) && !stripBriefing) return;
            if (stripBriefing)
            {
                GUI.depth = -50;   // over the lesson card's backdrop
                DrawStrip(_eleStrip, _stripY, "ELEVATOR", "your thumb here");
                if (StripThrottle) { (float th01, _) = SplitLeftAxis(); DrawStrip(_thrStrip, (float)(2 * th01 - 1), "THROTTLE", "your thumb here"); }
                if (StripBrakes) DrawStrip(_thrStrip, 1f - 2f * _stripBrake01, _driver.Sim?.Aircraft?.Config?.Propulsion != null ? "BRAKES" : "SPOILERS", "pull down = more");
                return;
            }

            bool powered = _driver.Sim?.Aircraft?.Config?.Propulsion != null;
            (float thr01, float padBrake) = SplitLeftAxis();
            string leftValue = padBrake > 0f
                ? $"BRAKES ON  {Mathf.RoundToInt(padBrake * 100f)}%"
                : powered ? $"THR {Mathf.RoundToInt(thr01 * 100f)}%" : $"SPOILER {Mathf.RoundToInt(SpoilerFraction * 100f)}%";
            if (DisplayOverride != null && GameThrottle && padBrake <= 0f) leftValue = "GAME  " + leftValue;
            bool pilotOut = _egress != null && _egress.PilotOut;
            (Vector2 freeLeft, Vector2 freeRight) = FreeKnobs(pilotOut);
            if (DeskMode) DrawDeskIndicator(GameLeftKnob(freeLeft), GameRightKnob(freeRight), leftValue);
            else if (sticksHidden) { }   // gamepad connected: no touch pads, strips or trim slider
            else if (StripMode)
            {
                DrawStrip(_eleStrip, _stripY, "ELEVATOR", "push = nose down");
                if (StripThrottle) DrawStrip(_thrStrip, (float)(2 * thr01 - 1), "THROTTLE", leftValue);
                if (StripBrakes) DrawStrip(_thrStrip, 1f - 2f * _stripBrake01, powered ? "BRAKES" : "SPOILERS",
                    powered ? $"BRAKE {Mathf.RoundToInt(_stripBrake01 * 100f)}%" : _stripBrake01 > 0.85f ? $"WHEEL BRAKE {Mathf.RoundToInt((_stripBrake01 - 0.85f) / 0.15f * 100f)}%" : $"SPOILER {Mathf.RoundToInt(_stripBrake01 * 100f)}%");
                DrawTrim();
            }
            else
            {
                DrawPad(_leftCenter, GameLeftKnob(_leftFinger == int.MinValue ? freeLeft : _leftKnob), "RUD / THR", leftValue);
                DrawPad(_rightCenter, GameRightKnob(_rightFinger == int.MinValue ? freeRight : _rightKnob), pilotOut ? "CHUTE  L / R" : "AIL / ELE", null);
                DrawTrim();
            }

            // BAIL OUT / DEPLOY BRS (tap) — cockpit only. EJECT — hold; the fill bar shows the hold progress.
            if (BailAvailable) Button(_bailRect, HasBrs ? "DEPLOY BRS" : "BAIL OUT");
            if (FireAvailable && !DeskMode)   // Mac: Space fires — no on-screen button over the HUD lines
            {
                Rect g = ToGui(_fireRect);
                GUI.color = _fireFinger != int.MinValue ? new Color(1f, 0.55f, 0.1f, 0.95f) : new Color(0.55f, 0.12f, 0.1f, 0.85f);
                GUI.DrawTexture(g, _solidTex);
                GUI.color = Color.white;
                UiLayout.ButtonLabel(g, _fireFinger != int.MinValue ? "FIRING" : "FIRE", _ejectStyle);
            }
            if (EjectAvailable)
            {
                Rect g = ToGui(_ejectRect);
                GUI.color = new Color(0.72f, 0.14f, 0.12f, 0.9f);
                GUI.DrawTexture(g, _solidTex);
                if (_ejectFinger != int.MinValue)
                {
                    GUI.color = new Color(1f, 0.85f, 0.2f, 0.95f);
                    GUI.DrawTexture(new Rect(g.x, g.y, g.width * Mathf.Clamp01(_ejectHold / EjectHoldSec), g.height), _solidTex);
                }
                GUI.color = Color.white;
                UiLayout.ButtonLabel(g, _ejectFinger != int.MinValue ? "EJECT" : "HOLD: EJECT", _ejectStyle);
            }

            Button(_resetRect, "Reset");
            string an = _driver.AircraftName ?? "";
            int paren = an.IndexOf(" (");
            Button(_acftRect, paren > 0 ? an.Substring(0, paren) : an);   // "Bush Taildragger (PA-18)" → fits the button
            if (!DeskMode && !StripMode && !sticksHidden)   // Mac / gamepad: no touch trim bars — the desk indicator shows trim (=/- keys); strips: elevator / throttle only
            {
                DrawTrimBar(_rudTrimRect, _rudderTrim, "RUD TRIM");
                DrawTrimBar(_ailTrimRect, _aileronTrim, "AIL TRIM");
            }
            // Landing gear (retractable types): one button, labelled with what it will do.
            if (_driver.Sim?.Aircraft?.Config?.RetractableGear == true)
            {
                var acg = _driver.Sim.Aircraft;
                string lbl = acg.GearDown ? (acg.GearExtension < 0.99 ? "GEAR ↓ …" : "GEAR UP") : (acg.GearExtension > 0.01 ? "GEAR ↑ …" : "GEAR DOWN");
                Button(_gearRect, lbl);
            }
            else if (_driver.Sim?.Aircraft is { Skis: { } skis })
            {
                // Says what a tap will do, like GEAR; the dots while the hydraulics are still running.
                bool down = skis.Selector.Commanded == FlyingGame.Core.SkiSelector.Position.Skis;
                bool moving = skis.Selector.Annunciation == FlyingGame.Core.SkiSelector.Light.Transit;
                Button(_gearRect, down ? (moving ? "SKIS ↓ …" : "SKIS UP") : (moving ? "SKIS ↑ …" : "SKIS DOWN"));
            }
            else if (_driver.Sim?.Aircraft is { HasWaterRudderRetract: true } acw)
            {
                // Says what a tap will do, like GEAR; the dots while the cable is still travelling.
                double u = acw.WaterRudderUp01;
                string lbl = acw.WaterRudderHooked ? (u < 0.99 ? "W.RUDDER ↑ …" : "W.RUDDER DOWN") : (u > 0.01 ? "W.RUDDER ↓ …" : "W.RUDDER UP");
                Button(_gearRect, lbl);
            }
            // SMOKE (owner 2026-10-31): a full-time button like GEAR, lit while the oil is on. Shown for anything that
            // can carry it — an engine's exhaust, or a glider's wingtip canisters.
            if (SmokeAvailable)
            {
                bool on = _smoke.On;
                GUI.color = on ? new Color(1f, 0.85f, 0.3f, 1f) : Color.white;
                Button(_smokeRect, on ? "SMOKE ON" : "SMOKE");
                GUI.color = Color.white;
            }
            // CAM (Ty 2026-11-03): what the chase camera looks along — the flight path (relative wind) or the ground
            // track. Full-time, like SMOKE; a runway lesson that forces Track says so on the button.
            {
                bool forced = _driver.GroundReferenceForced;
                var mode = FlyingGame.Core.CameraReference.Effective(SessionSettings.CameraReferenceMode, forced);
                Button(_camRefRect, FlyingGame.Core.CameraReference.Label(mode, forced));
            }
            // TOUCH / GYRO: the shortcut, lit while the gyro is flying. Only drawn where it is laid out, which is
            // only while the phone is the TV's controller.
            if (_ctlModeRect.width > 0f) Button(_ctlModeRect, TvGyroLayout ? "GYRO FLIES" : "TOUCH FLIES");
            // LOOK ◀ / centre / ▶ (owner 2026-10-31): the aerobatic sights sit on the eye's own lateral line, about
            // 90 degrees off the nose, so without a way to turn your head they can never be seen. Shown only in the
            // cockpit, lit while the head is off centre.
            if (LookAvailable)
            {
                double yaw = _cockpit.Look.YawDeg;
                GUI.color = yaw < -1.0 ? new Color(1f, 0.85f, 0.3f, 1f) : Color.white;
                Button(_lookLeftRect, "◀ LOOK");
                GUI.color = System.Math.Abs(yaw) <= 1.0 ? new Color(1f, 0.85f, 0.3f, 1f) : Color.white;
                Button(_lookCentreRect, "AHEAD");
                GUI.color = yaw > 1.0 ? new Color(1f, 0.85f, 0.3f, 1f) : Color.white;
                Button(_lookRightRect, "LOOK ▶");
                GUI.color = Color.white;
            }
            // Flaps (types that have them): cycle 0 / ½ / full, in the slot beside Reset. In a lesson the flaps are part of
            // the lesson's SETUP (owner 2026-10-05: changing them in flight "balloons wildly") — no buttons there.
            if (_driver.HasFlaps && !Practice.PracticeController.AnyActive)
            {
                // FLAPS 0 | 50 | 100: three buttons in the slot, the current setting lit.
                var fr = _brakeRect;
                double f = _driver.Sim.Aircraft.FlapFraction;
                float third = fr.width / 4f;
                for (int i = 0; i < 3; i++)
                {
                    bool on = i == 0 ? f < 0.25 : i == 1 ? (f >= 0.25 && f < 0.75) : f >= 0.75;
                    var r = new Rect(fr.x + (i == 0 ? 0 : i + 1) * third, fr.y, (i == 0 ? 2 : 1) * third - 2f, fr.height);
                    Rect g = ToGui(r);
                    GUI.color = on ? new Color(0.2f, 0.62f, 0.35f, 0.95f) : new Color(0.18f, 0.24f, 0.32f, 0.95f);
                    GUI.DrawTexture(g, _solidTex);
                    GUI.color = Color.white;
                    UiLayout.ButtonLabel(g, i == 0 ? "FLAP 0" : i == 1 ? "50" : "100", _ejectStyle);
                }
            }
            // Glider on the ground: TOW button (aerotow from the runway) above the Aircraft button.
            var tow = GetComponent<TowController>();
            if (tow != null && _driver.Sim?.Aircraft?.Config?.Propulsion == null)
            {
                var r = _towRect;
                if (tow.Towing)
                {
                    GUI.color = new Color(1f, 0.85f, 0.3f, 1f);
                    Button(r, "RELEASE");
                    GUI.color = Color.white;
                }
                else if (_driver.GroundStart && tow.Tow == null)
                {
                    Button(r, "TOW");
                }
            }
        }

        // ---- Mac desk controls ------------------------------------------------------------------------------------
        private const float DeskRamp = 2.2f;   // keyboard stick travel per second (keys aren't square waves)
        private int _deskFlapStep;
        private bool _joySeen;
        private ChaseCamera _joyChase;

        /// <summary>Keyboard: arrows = stick (Up = nose down, realistic), A/D = rudder, W/S = throttle, =/- pitch trim,
        /// F flaps, L gear, B brakes, E bail out (the handle), Space fire. Joystick (any connected): X/Y = stick, twist/4th axis = rudder, 3rd axis
        /// = throttle. Released keys ramp the stick home — and on a reversible aircraft the controls then go free.</summary>
        private void DeskInput()
        {
            float dt = Time.unscaledDeltaTime;
            float Ramp(float cur, float target) => Mathf.MoveTowards(cur, target, DeskRamp * dt * (Mathf.Approximately(target, 0f) ? 1.6f : 1f));
            float kx = Key(KeyCode.RightArrow) - Key(KeyCode.LeftArrow);
            float ky = Key(KeyCode.UpArrow) - Key(KeyCode.DownArrow);
            float kr = Key(KeyCode.D) - Key(KeyCode.A);
            _aileron = Ramp(_aileron, kx);
            _elevator = Ramp(_elevator, InvertElevator ? -ky : ky);
            _rudder = Ramp(_rudder, kr);
            float tv = Key(KeyCode.W) - Key(KeyCode.S);
            if (tv != 0f) _throttle = Mathf.Clamp(_throttle + tv * 0.6f * dt, -1f, 1f);

            // Joystick: once one is seen moving it takes over the stick and rudder (a sprung stick centred = held neutral).
            // A CALIBRATED joystick (OPTIONS › JOYSTICK SETUP, owner 2026-10-11) uses its profile: the axes it learnt, their
            // centres, endpoints and dead zones, and its button assignments. Nothing flies from it while the setup runs.
            var jp = JoystickInput.Profile;
            if (JoystickInput.SetupActive) { }
            else if (jp != null)
            {
                var r = jp.Read();
                if (Mathf.Abs(r.ail) > 0.1f || Mathf.Abs(r.ele) > 0.1f || Mathf.Abs(r.rud) > 0.1f) _joySeen = true;
                if (_joySeen && kx == 0f && ky == 0f)
                {
                    _aileron = r.ail; _elevator = r.ele;                                     // (+ = push: nose down, as the keys)
                    if (kr == 0f && jp.Rudder.Set) _rudder = r.rud;
                }
                if (r.hasThr && tv == 0f) _throttle = AxisForFraction(IdleFraction + (1f - IdleFraction) * r.thr01);
                JoyActions(jp, dt);
            }
            else
            {
                float jx = SafeAxis("AeroJoyX"), jy = SafeAxis("AeroJoyY"), jr = SafeAxis("AeroJoyTwist"), jt = SafeAxis("AeroJoyThrottle");
                if (Mathf.Abs(jx) > 0.15f || Mathf.Abs(jy) > 0.15f) _joySeen = true;
                if (_joySeen && kx == 0f && ky == 0f)
                {
                    _aileron = Mathf.Abs(jx) < 0.03f ? 0f : jx;
                    _elevator = Mathf.Abs(jy) < 0.03f ? 0f : (InvertElevator ? jy : -jy);   // stick back (axis +) = pull
                    if (kr == 0f) _rudder = Mathf.Abs(jr) < 0.05f ? 0f : jr;
                    if (Mathf.Abs(jt) > 0.02f && tv == 0f) _throttle = -jt;                   // lever forward (axis -) = power
                }
            }

            float tt = Key(KeyCode.Equals) - Key(KeyCode.Minus);
            if (tt != 0f) _pitchTrim = Mathf.Clamp(_pitchTrim + tt * 0.5f * dt, -1f, 1f);
            if (Input.GetKey(KeyCode.B)) _brakeHeld = true;
            if (Input.GetKeyDown(KeyCode.F)) { _deskFlapStep = (_deskFlapStep + 1) % 3; _flapTapThird = _deskFlapStep; _tapped.Add(Btn.Flaps); }
            if (Input.GetKeyDown(KeyCode.L)) _tapped.Add(Btn.Gear);
            if (Input.GetKeyDown(KeyCode.R)) DoReset();
            if (Input.GetKeyDown(KeyCode.E) && MenuHandleAvailable) MenuPullHandle();   // the handle, one press
            _leftKnob = IdleLeftKnob();
        }

        private int _joyFlapSw = -1;

        /// <summary>A calibrated joystick's buttons and switches (brakes, flaps, gear, trim, reset, view, AP, pause).</summary>
        private void JoyActions(JoystickProfile jp, float dt)
        {
            if (JoystickInput.Held("brakes")) _brakeHeld = true;
            if (JoystickInput.Pressed("flapsDown")) { _deskFlapStep = Mathf.Min(2, _deskFlapStep + 1); _flapTapThird = _deskFlapStep; _tapped.Add(Btn.Flaps); }
            if (JoystickInput.Pressed("flapsUp")) { _deskFlapStep = Mathf.Max(0, _deskFlapStep - 1); _flapTapThird = _deskFlapStep; _tapped.Add(Btn.Flaps); }
            if (JoystickInput.Pressed("flapsHalf")) { _deskFlapStep = 1; _flapTapThird = 1; _tapped.Add(Btn.Flaps); }   // the middle position
            // FLAP SWITCH: the switch's position is the flap handle (up · half · full), set when it moves.
            int fs = jp.FlapSwitchPos();
            if (fs >= 0 && fs != _joyFlapSw) { if (_joyFlapSw >= 0) { _deskFlapStep = fs; _flapTapThird = fs; _tapped.Add(Btn.Flaps); } _joyFlapSw = fs; }   // (only when it MOVES: a scenario's flaps stand until then)
            if (JoystickInput.Pressed("gear")) _tapped.Add(Btn.Gear);
            if (JoystickInput.Held("trimUp")) _pitchTrim = Mathf.Clamp(_pitchTrim - 0.5f * dt, -1f, 1f);       // nose up = less push
            if (JoystickInput.Held("trimDown")) _pitchTrim = Mathf.Clamp(_pitchTrim + 0.5f * dt, -1f, 1f);
            if (jp.RudderTrim.Set) { float rt = jp.RudderTrim.Value(); _rudderTrim = Mathf.Clamp(_rudderTrim + rt * Mathf.Abs(rt) * 0.083f * dt, -1f, 1f); }   // the rocker runs the rudder trim, progressive and slow (∝ deflection², centre → full ~12 s); it stays
            if (JoystickInput.Pressed("reset")) DoReset();
            if (JoystickInput.Pressed("view")) (LocalSplit.Active ? LocalPlayerView.ChaseFor(_driver) : (_joyChase ??= Object.FindFirstObjectByType<ChaseCamera>()))?.NextView();
            if (JoystickInput.Pressed("smoke") && SmokeAvailable) _smoke.Toggle();
            if (JoystickInput.Pressed("apDisconnect")) Object.FindFirstObjectByType<Widget.GameAeroHost>()?.Autopilot?.Off(disconnect: true);
            if (JoystickInput.Pressed("pause")) Object.FindFirstObjectByType<FlightReplay>()?.Enter();
        }

        /// <summary>iPad / iPhone: a calibrated joystick — the Mac's over the JOYSTICK LINK (2026-10-12) — flies the airplane
        /// when it moves, as on the Mac; a finger on a pad still wins for that pad's controls.</summary>
        private void MobileJoystick()
        {
            var jp = JoystickInput.Profile;
            if (jp == null || JoystickInput.SetupActive) return;
            float dt = Time.unscaledDeltaTime;
            var r = jp.Read();
            if (Mathf.Abs(r.ail) > 0.1f || Mathf.Abs(r.ele) > 0.1f || Mathf.Abs(r.rud) > 0.1f) _joySeen = true;
            if (_joySeen && _rightFinger == int.MinValue) { _aileron = r.ail; _elevator = r.ele; }
            if (_joySeen && _leftFinger == int.MinValue && jp.Rudder.Set) _rudder = r.rud;
            if (r.hasThr && _leftFinger == int.MinValue) { _throttle = AxisForFraction(IdleFraction + (1f - IdleFraction) * r.thr01); _leftKnob = IdleLeftKnob(); }
            JoyActions(jp, dt);
        }

        // ---- gamepad (Input System: MFi / Xbox / PlayStation / 8BitDo) -------------------------------------------
        //
        //   RIGHT stick       roll and pitch: stick back (toward you) = nose UP, pushed away = nose down, the same
        //                     sense as the touch pad and a real stick (InvertElevator flips it, as it does for touch)
        //   LEFT stick X      rudder; RT / LT are a second rudder (RT − LT), summed and clamped
        //   LEFT stick Y      throttle as a RATE: held up = power comes up, held down = comes off, let go = stays.
        //                     (A self-centring stick read as a POSITION would snap to half power every time the
        //                     thumb lifted.) Linear, no expo. Stops at idle: brakes are X, not the bottom of the lever.
        //   RB / LB (held)    throttle up / down too, at a fixed rate
        //   d-pad up / down   pitch trim nose down / nose up (held)
        //   d-pad left/right  flaps up / down one step
        //   A (held)          guns          B   smoke on / off       X (held)   wheel brakes       Y   change view
        //   Menu (Start)      open / close OPTIONS                  View (Select)   REC: start / stop recording
        //
        // The pad takes over a group of controls only while it is moving them, and for the one frame after, so a
        // stick let go returns its surfaces to centre; while it rests, the touch pads work exactly as before, and a
        // finger on a pad always wins that pad's controls.
        // Left stick: idle to full in 2 s at full stick, past a 0.25 dead zone — the Quest's (Ty 2026-11-02), see GamepadShaping.
        private const float GamepadButtonThrottleRate = 0.8f;   // the shoulders: idle to full in just under 2 s
        private bool _gpStickWas, _gpRudderWas;
        private OptionsPanel _options;

        /// <summary>Menu and View: run before the early return for menus, so Menu can close what it opened.</summary>
        private void GamepadMenuButtons()
        {
            var gp = GamepadInput.Now;
            if (!gp.Connected || SessionSettings.MenuOpen) return;   // (the landing page has its own buttons)
            if (gp.MenuPressed && (_options != null || (_options = Object.FindFirstObjectByType<OptionsPanel>()) != null)) _options.Toggle();
            if (gp.RecordPressed && !SessionSettings.ReplayActive) ClipRecorder.ToggleRecording();
        }

        /// <summary>The sticks, triggers and flying buttons, after touch / keyboard / a calibrated joystick have run.</summary>
        private void GamepadFlight()
        {
            var gp = GamepadInput.Now;
            if (JoystickInput.SetupActive) return;   // (the setup owns every stick while it runs)
            var controls = _driver.Sim?.Aircraft?.Config?.Controls;
            float Lift(float v, ControlAxisConfig axis) => axis == null ? v : (float)FlyingGame.Core.GamepadShaping.PreCompensateDeadZone(v, axis.DeadZone);

            // Sticks. Disconnected reads as all-zero, so a pad that drops out mid-roll lets go on the next frame.
            bool stick = gp.Roll != 0f || gp.Pitch != 0f;
            if ((stick || _gpStickWas) && _rightFinger == int.MinValue)
            {
                _aileron = Lift(gp.Roll, controls?.Aileron);
                _elevator = Lift(InvertElevator ? -gp.Pitch : gp.Pitch, controls?.Elevator);   // pad y + = pushed away = nose down
            }
            _gpStickWas = stick;
            bool rudder = gp.Rudder != 0f;
            if ((rudder || _gpRudderWas) && _leftFinger == int.MinValue) _rudder = Lift(gp.Rudder, controls?.Rudder);
            _gpRudderWas = rudder;
            if (!gp.Connected) return;

            float dt = Time.unscaledDeltaTime;   // (real time, as the keyboard's W / S, so it works in slow motion too)
            if (_leftFinger == int.MinValue && (gp.Throttle != 0f || gp.ThrottleUp || gp.ThrottleDown))
            {
                _throttle = (float)FlyingGame.Core.GamepadShaping.Lever(_throttle, gp.Throttle, gp.ThrottleUp, gp.ThrottleDown,
                    (float)FlyingGame.Core.GamepadShaping.StickLeverRatePerSec(AxisForFraction(IdleFraction), 1f), GamepadButtonThrottleRate, dt, AxisForFraction(IdleFraction), 1f);
                _leftKnob = IdleLeftKnob();
            }
            if (_trimFinger == int.MinValue)
            {
                if (gp.TrimNoseDown) _pitchTrim = Mathf.Clamp(_pitchTrim + 0.5f * dt, -1f, 1f);   // the slider's sense: up = nose down
                if (gp.TrimNoseUp) _pitchTrim = Mathf.Clamp(_pitchTrim - 0.5f * dt, -1f, 1f);
            }
            if (gp.Brakes) _brakeHeld = true;
            if (gp.FlapsDownPressed || gp.FlapsUpPressed)
            {
                // A step from where the flaps ARE (0 / 50 / 100), not from a count of presses: the touch slot may have moved them.
                int at = Mathf.RoundToInt((float)(_driver.Sim?.Aircraft?.FlapFraction ?? 0.0) * 2f);
                _deskFlapStep = Mathf.Clamp(at + (gp.FlapsDownPressed ? 1 : -1), 0, 2); _flapTapThird = _deskFlapStep; _tapped.Add(Btn.Flaps);
            }
            if (gp.SmokePressed && SmokeAvailable) _smoke.Toggle();
            if (gp.ViewPressed) (LocalSplit.Active ? LocalPlayerView.ChaseFor(_driver) : (_joyChase ??= Object.FindFirstObjectByType<ChaseCamera>()))?.NextView();
        }

        private static float SafeAxis(string name)
        {
            try { return Input.GetAxisRaw(name); } catch (System.ArgumentException) { return 0f; }   // axis not defined
        }

        /// <summary>Mac: a compact read-only display of the controls (bottom-left) — stick dot, rudder and throttle bars,
        /// pitch trim — sized off the short screen edge and kept clear of the gauges.</summary>
        private void DrawDeskIndicator(Vector2 leftKnob, Vector2 rightKnob, string leftValue)
        {
            float s = Mathf.Min(Screen.width, Screen.height), box = s * 0.16f, pad = s * 0.02f;
            var r = new Rect(pad, Screen.height - pad - box - s * 0.05f, box, box);
            GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(r, _solidTex);
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(r.center.x - 1f, r.y, 2f, r.height), _solidTex);
            GUI.DrawTexture(new Rect(r.x, r.center.y - 1f, r.width, 2f), _solidTex);
            // Stick dot: the right pad's knob mapped from pad space into the box (GUI y down).
            Vector2 n = (rightKnob - _rightCenter) / Mathf.Max(1f, _half);
            Vector2 dot = new Vector2(r.center.x + n.x * box * 0.5f, r.center.y - n.y * box * 0.5f);
            GUI.color = new Color(0.5f, 0.95f, 1f, 0.95f); GUI.DrawTexture(new Rect(dot.x - s * 0.012f, dot.y - s * 0.012f, s * 0.024f, s * 0.024f), _solidTex);
            // Rudder bar under the box, throttle bar to its right.
            float rn = (leftKnob.x - _leftCenter.x) / Mathf.Max(1f, _half);
            var rb = new Rect(r.x, r.yMax + s * 0.012f, box, s * 0.018f);
            GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(rb, _solidTex);
            GUI.color = new Color(0.5f, 0.95f, 1f, 0.95f); GUI.DrawTexture(new Rect(rb.center.x + rn * box * 0.5f - 2f, rb.y, 4f, rb.height), _solidTex);
            var tb = new Rect(r.xMax + s * 0.012f, r.y, s * 0.022f, box);
            GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(tb, _solidTex);
            float tf = LeftPadFraction;
            GUI.color = new Color(0.4f, 0.9f, 0.5f, 0.9f); GUI.DrawTexture(new Rect(tb.x, tb.yMax - tb.height * tf, tb.width, tb.height * tf), _solidTex);
            GUI.color = Color.white;
            _deskLabel ??= new GUIStyle { font = UiFont.Get(), alignment = TextAnchor.UpperLeft, normal = { textColor = new Color(1f, 1f, 1f, 0.85f) } };
            _deskLabel.fontSize = Mathf.RoundToInt(s * 0.022f);
            GUI.Label(new Rect(r.x, r.y - s * 0.034f, box * 2f, s * 0.03f), leftValue + $"   trim {(_pitchTrim >= 0 ? "+" : "")}{_pitchTrim * 100f:F0}", _deskLabel);
            UiLayout.LeftBottomLimit = r.y - s * 0.045f;   // the left dial column stays above the indicator's label
        }
        private GUIStyle _deskLabel;

        /// <summary>Released pads on a reversible aircraft show where the stick / pedals really are: the surface trails with
        /// the air (and its centering spring), and the control in the cockpit moves with it.</summary>
        private (Vector2 left, Vector2 right) FreeKnobs(bool pilotOut)
        {
            Vector2 left = IdleLeftKnob(), right = _rightCenter;
            Aircraft ac = _driver.Sim?.Aircraft;
            if (ac == null || pilotOut) return (left, right);
            ControlsConfig c = ac.Config.Controls;
            ControlDeflections d = ac.CurrentDeflections;
            float Stick(double defl, ControlAxisConfig axis, float trim) =>
                Mathf.Clamp((float)Aircraft.StickForDeflection(defl, axis) - trim, -1f, 1f);
            if (c.Rudder.Reversible) left.x = _leftCenter.x + Stick(d.RudderRad, c.Rudder, _rudderTrim * TrimAuthority) * _half;
            if (c.Aileron.Reversible) right.x = _rightCenter.x + Stick(d.AileronRad, c.Aileron, _aileronTrim * TrimAuthority) * _half;
            if (c.Elevator.Reversible)
            {
                float e = Stick(d.ElevatorRad, c.Elevator, (InvertElevator ? -_pitchTrim : _pitchTrim) * TrimAuthority);
                right.y = _rightCenter.y + (InvertElevator ? -e : e) * _half;
            }
            return (left, right);
        }

        private Vector2 IdleLeftKnob() =>
            new Vector2(_leftCenter.x, _leftCenter.y + _throttle * _half); // throttle height persists

        /// <summary>Knob axis value (-1..1) for a given knob height fraction (0 bottom..1 top).</summary>
        private static float AxisForFraction(float f) => f * 2f - 1f;

        /// <summary>Put the throttle knob where a lever value is (lever −1 full power … +1 idle) — the lesson hands the power over
        /// at the setting the game was flying (owner 2026-10-06: hands-off, the approach lesson's knob sat at the half-power air
        /// start, floated the whole way down and porpoised).</summary>
        public void SetThrottleFromLever(double lever)
        {
            if (_driver?.Sim?.Aircraft?.Config?.Propulsion == null) return;   // glider: the lever is the spoiler
            float thr01 = Mathf.Clamp01((float)(1.0 - lever) * 0.5f);
            _throttle = AxisForFraction(IdleFraction + (1f - IdleFraction) * thr01);
        }

        /// <summary>Square pad with centre cross, X/Y position lines through the knob, and the knob.</summary>
        private GUIStyle _brakeValueStyle;
        private int _flapTapThird;
        /// <summary>Practice exercises: the merged inputs the sim actually received, so the pads can show what the GAME is doing
        /// on the axes it flies (owner: "make sure the control positions are shown on the control pads for all controls").</summary>
        public ControlInputs? DisplayOverride;
        public bool GameAileron, GameElevator, GameRudder, GameThrottle;
        private Vector2 GameLeftKnob(Vector2 fallback)
        {
            if (DisplayOverride == null) return fallback;
            ControlInputs o = DisplayOverride.Value;
            float x = GameRudder ? _leftCenter.x + (float)o.Rudder * _half : fallback.x;
            float y = fallback.y;
            if (GameThrottle)
            {
                bool powered = _driver.Sim?.Aircraft?.Config?.Propulsion != null;
                float f = powered ? IdleFraction + (1f - (float)o.ThrottleLever) * 0.5f * (1f - IdleFraction)   // lever −1 = full power
                                  : 0.5f - Mathf.Clamp01((float)o.ThrottleLever) * (0.5f - IdleFraction);        // glider: spoiler fraction
                y = _leftCenter.y + AxisForFraction(f) * _half;
            }
            return new Vector2(x, y);
        }
        private Vector2 GameRightKnob(Vector2 fallback)
        {
            if (DisplayOverride == null) return fallback;
            ControlInputs o = DisplayOverride.Value;
            float x = GameAileron ? _rightCenter.x + (float)o.Aileron * _half : fallback.x;
            float ny = InvertElevator ? -(float)o.Elevator : (float)o.Elevator;
            float y = GameElevator ? _rightCenter.y + ny * _half : fallback.y;
            return new Vector2(x, y);
        }
        private void DrawPad(Vector2 c, Vector2 knob, string label, string value)
        {
            float d = _half * 2f;
            float t = Mathf.Max(2f, _half * 0.012f);   // line thickness
            var pad = new Rect(c.x - _half, c.y - _half, d, d);

            // Background + border.
            GUI.color = new Color(1f, 1f, 1f, 0.16f);
            GUI.DrawTexture(ToGui(pad), _solidTex);
            if (c == _leftCenter)
            {
                bool gliderPad = _driver.Sim?.Aircraft?.Config?.Propulsion == null;
                if (gliderPad)
                {
                    // Spoiler band 25–50 %: tinted so the stowed line at 50 % is obvious.
                    GUI.color = new Color(0.4f, 0.7f, 1f, 0.14f);
                    GUI.DrawTexture(ToGui(new Rect(pad.x, pad.y + d * IdleFraction, d, d * (0.5f - IdleFraction))), _solidTex);
                    GUI.color = new Color(1f, 1f, 1f, 0.45f);
                    GUI.DrawTexture(ToGui(new Rect(pad.x, pad.y + d * 0.5f - t * 0.5f, d, t)), _solidTex);
                }
                // Brake band (bottom 20 %) tinted, dead band 20–25 % darker, idle line at 25 %.
                GUI.color = new Color(1f, 0.45f, 0.3f, 0.18f);
                GUI.DrawTexture(ToGui(new Rect(pad.x, pad.y, d, d * BrakeStartFraction)), _solidTex);
                GUI.color = new Color(0f, 0f, 0f, 0.18f);
                GUI.DrawTexture(ToGui(new Rect(pad.x, pad.y + d * BrakeStartFraction, d, d * (IdleFraction - BrakeStartFraction))), _solidTex);
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                GUI.DrawTexture(ToGui(new Rect(pad.x, pad.y + d * IdleFraction - t * 0.5f, d, t)), _solidTex);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            DrawFrame(pad, t);

            // Faint centre cross.
            GUI.color = new Color(1f, 1f, 1f, 0.28f);
            GUI.DrawTexture(ToGui(new Rect(pad.x, c.y - t * 0.5f, d, t)), _solidTex);
            GUI.DrawTexture(ToGui(new Rect(c.x - t * 0.5f, pad.y, t, d)), _solidTex);

            // Position lines: where the stick is now, relative to centre.
            GUI.color = new Color(0.55f, 0.95f, 1f, 0.8f);
            GUI.DrawTexture(ToGui(new Rect(pad.x, knob.y - t * 0.5f, d, t)), _solidTex);
            GUI.DrawTexture(ToGui(new Rect(knob.x - t * 0.5f, pad.y, t, d)), _solidTex);

            // Knob.
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            // The knob disc stays INSIDE the pad (no-overlap rule: at full travel it used to cover the pad's label and the
            // trim bar); the position lines above still mark the exact stick position.
            float kr = _half * 0.225f;
            var kd = new Vector2(Mathf.Clamp(knob.x, pad.x + kr, pad.xMax - kr), Mathf.Clamp(knob.y, pad.y + kr, pad.yMax - kr));
            GUI.DrawTexture(GuiRectCentered(kd, _half * 0.45f), _knobTex);

            // Label (and value) just above the pad.
            GUI.color = Color.white;
            float lh = _styleFs * 1.5f;
            // Portrait: the trim bars sit right above the pads — the labels go above the bars (no-overlap rule).
            float lift = ScreenLayout.Portrait ? _rudTrimRect.height + Mathf.Min(Screen.width, Screen.height) * 0.012f + _styleFs * 1.5f : 0f;
            UiLayout.Label(new Rect(pad.x, Screen.height - (pad.yMax + lift + lh), d, lh), label, _labelStyle);
            if (value != null)
            {
                // Brake awareness (owner): the readout goes RED while any brake is applied.
                bool braking = value.StartsWith("BRAKES");
                if (braking && _brakeValueStyle == null) _brakeValueStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(1f, 0.25f, 0.2f) } };
                UiLayout.Label(new Rect(pad.x, Screen.height - (pad.yMax + lift + 2f * lh), d, lh), value, braking ? _brakeValueStyle : _valueStyle);
            }
        }

        /// <summary>Vertical pitch-trim slider: track, centre + quarter ticks, and a bar knob.</summary>
        /// <summary>Horizontal trim bar: frame, centre + quarter ticks, knob at the trim position, small label.</summary>
        private void DrawTrimBar(Rect r, float value, string label)
        {
            float t = Mathf.Max(2f, _half * 0.012f);
            GUI.color = new Color(1f, 1f, 1f, 0.16f);
            GUI.DrawTexture(ToGui(r), _solidTex);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            DrawFrame(r, t);
            for (int i = -4; i <= 4; i++)
            {
                float x = r.center.x + i * 0.25f * r.width * 0.5f;
                bool centre = i == 0;
                GUI.color = new Color(1f, 1f, 1f, centre ? 0.7f : 0.3f);
                float th = centre ? r.height : r.height * 0.5f;
                GUI.DrawTexture(ToGui(new Rect(x - t * 0.5f, r.center.y - th * 0.5f, t, th)), _solidTex);
            }
            float kx = r.center.x + value * r.width * 0.5f;
            float kw = Mathf.Max(6f, r.width * 0.035f);
            GUI.color = new Color(0.45f, 0.9f, 1f, 0.95f);
            GUI.DrawTexture(ToGui(new Rect(kx - kw * 0.5f, r.y, kw, r.height)), _solidTex);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            // The label sits OUTSIDE the bar (no-overlap rule: the marker used to slide over it) — above the bar in
            // portrait (the pad is right below it; the tray reserves the line), below it in landscape.
            if (_labelStyle != null)
            {
                float lh = _styleFs * 1.5f;
                float ly = ScreenLayout.Portrait ? r.yMax + 2f : r.y - lh - 2f;
                UiLayout.Label(ToGui(new Rect(r.x, ly, r.width, lh)), label, _labelStyle);
            }
            GUI.color = Color.white;
        }

        private void DrawTrim()
        {
            float t = Mathf.Max(2f, _half * 0.012f);
            Rect r = _trimRect;

            GUI.color = new Color(1f, 1f, 1f, 0.16f);
            GUI.DrawTexture(ToGui(r), _solidTex);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            DrawFrame(r, t);

            // Ticks: centre strong, quarters faint.
            for (int i = -4; i <= 4; i++)
            {
                float y = r.center.y + i * 0.25f * r.height * 0.5f;
                bool centre = i == 0;
                GUI.color = new Color(1f, 1f, 1f, centre ? 0.7f : 0.3f);
                float tw = centre ? r.width : r.width * 0.5f;
                GUI.DrawTexture(ToGui(new Rect(r.center.x - tw * 0.5f, y - t * 0.5f, tw, t)), _solidTex);
            }

            // Knob bar at the trim position.
            float ky = r.center.y + _pitchTrim * r.height * 0.5f;
            float kh = _half * 0.10f;
            GUI.color = new Color(0.55f, 0.95f, 1f, 0.95f);
            GUI.DrawTexture(ToGui(new Rect(r.x + t, ky - kh * 0.5f, r.width - 2f * t, kh)), _solidTex);

            GUI.color = Color.white;
            float lh = _styleFs * 1.5f;
            GUI.Label(new Rect(r.x - r.width, Screen.height - (r.yMax + lh), r.width * 3f, lh), "TRIM", _labelStyle);
        }

        private void DrawFrame(Rect r, float t)
        {
            GUI.DrawTexture(ToGui(new Rect(r.x, r.y, r.width, t)), _solidTex);
            GUI.DrawTexture(ToGui(new Rect(r.x, r.yMax - t, r.width, t)), _solidTex);
            GUI.DrawTexture(ToGui(new Rect(r.x, r.y, t, r.height)), _solidTex);
            GUI.DrawTexture(ToGui(new Rect(r.xMax - t, r.y, t, r.height)), _solidTex);
        }

        /// <summary>Screen rect (bottom-left origin) → GUI rect (top-left origin).</summary>
        private Rect ToGui(Rect screenRect) =>
            new Rect(screenRect.x, Screen.height - screenRect.y - screenRect.height, screenRect.width, screenRect.height);

        private Rect GuiRectCentered(Vector2 centerScreen, float size) =>
            new Rect(centerScreen.x - size * 0.5f, Screen.height - centerScreen.y - size * 0.5f, size, size);

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(UiLayout.Unit * ScreenLayout.FontFrac);
            if (_solidTex != null && fs == _styleFs) return;
            _styleFs = fs;

            _knobTex ??= CircleTex(64, 0.0f, 1.0f);   // solid knob
            _solidTex ??= SolidTex(Color.white);
            _btnBg ??= SolidTex(new Color(0.20f, 0.26f, 0.34f, 0.85f));

            // Build styles FROM SCRATCH (no GUI.skin.* base): the built-in default skin is stripped on
            // iOS, so `new GUIStyle(GUI.skin.button)` NREs. These own their font + background.
            _btnStyle = new GUIStyle
            {
                fontSize = fs,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                font = UiFont.Get(),
                normal = { textColor = Color.white, background = _btnBg },
                active = { textColor = Color.white, background = _btnBg },
                padding = new RectOffset(8, 8, 8, 8),
            };
            _ejectStyle = new GUIStyle(_btnStyle) { normal = { textColor = Color.white, background = null } };
            _labelStyle = new GUIStyle
            {
                fontSize = Mathf.Max(UiLayout.MinText, Mathf.RoundToInt(fs * 0.8f)),
                alignment = TextAnchor.MiddleCenter,
                font = UiFont.Get(),
                normal = { textColor = new Color(1f, 1f, 1f, 0.6f) },
            };
            _valueStyle = new GUIStyle
            {
                fontSize = fs,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                font = UiFont.Get(),
                normal = { textColor = new Color(0.55f, 0.95f, 1f, 0.95f) },
            };
        }

        private static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(4, 4, TextureFormat.ARGB32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>A soft filled/ring circle in a transparent square (alpha only; tinted via GUI.color).</summary>
        private static Texture2D CircleTex(int size, float innerFrac, float outerFrac)
        {
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
            float c = (size - 1) * 0.5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a;
                if (innerFrac <= 0f)
                {
                    a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(outerFrac - 0.12f, outerFrac, r));
                }
                else
                {
                    // ring: fade in at innerFrac, out at outerFrac
                    float inA = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerFrac - 0.12f, innerFrac, r));
                    float outA = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(outerFrac - 0.12f, outerFrac, r));
                    a = Mathf.Min(inA, outA);
                }
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}

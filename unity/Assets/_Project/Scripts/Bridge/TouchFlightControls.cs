using System.Collections.Generic;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// RC-transmitter dual-touchpad controls (build-order step 5), the primary on-device input. All
    /// game controls sit along the BOTTOM edge of the screen (owner request); the HUD owns the top.
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
        public float TrimAuthority = 0.4f;      // full trim slider = this much elevator (stick units)
        public float IdleFraction = 0.25f;      // knob height (0 bottom..1 top) where throttle reaches idle
        public float BrakeStartFraction = 0.20f;// braking begins below this (20–25 % is the dead band)
        public float BrakeRudderBias = 0.8f;    // full rudder shifts this much braking to one side
        public float GrabSlop = 0.35f;          // a touch may begin this far outside a pad (x half-size) and still grab it

        // Fleet the on-screen "Aircraft" button cycles through (matches KeyboardTestControls 1-0/F1-F2).
        private static readonly string[] Fleet =
        {
            "glider-2-33-like", "c172-like", "pitts-s2b-like", "stearman-pt17-like",
            "extra-300-like", "p51d-like", "f86-sabre-like", "seminole-like",
            "dc3-like", "boeing-737-like", "pa18-cub-like", "decathlon-8kcab-like",
        };

        private FlightSimDriver _driver;

        // Live control state (already shaped, -1..1). Throttle and trim are sticky so they live across frames.
        private float _rudder, _throttle, _aileron, _elevator, _pitchTrim; // _throttle = left-pad Y knob, -1 (bottom)..1 (top)
        private bool _brakeHeld;                                          // B key (editor) — the pad bottom band is the real brake

        // Pad geometry recomputed each frame from screen size (screen px, origin bottom-left).
        private Vector2 _leftCenter, _rightCenter;
        private float _half;                    // pad half-size (square)
        private Rect _trimRect;                 // vertical trim slider track

        // Which finger (or mouse, id = -1) currently owns each control, and its current position.
        private int _leftFinger = int.MinValue, _rightFinger = int.MinValue, _trimFinger = int.MinValue;
        private Vector2 _leftKnob, _rightKnob; // screen px; knob = pad centre when idle

        // On-screen button rects (screen px, bottom-left origin) — computed in Update, drawn in OnGUI.
        private Rect _brakeRect, _resetRect, _acftRect;

        private Texture2D _knobTex, _solidTex, _btnBg;
        private GUIStyle _btnStyle, _labelStyle, _valueStyle;
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
        private void PresetTrim()
        {
            // Air start: half power. Ground start: IDLE (owner: "power should start at idle on the ground").
            _throttle = AxisForFraction(_driver.GroundStart ? IdleFraction : IdleFraction + (1f - IdleFraction) * 0.5f);
            if (_driver.Sim?.Aircraft?.Config?.Propulsion == null) _throttle = AxisForFraction(_driver.GroundStart ? IdleFraction : 1f); // glider: spoilers open on the ground, stowed in the air
            _leftKnob = IdleLeftKnob();
            float t = (float)_driver.TrimStick / Mathf.Max(0.01f, TrimAuthority);
            _pitchTrim = Mathf.Clamp(InvertElevator ? -t : t, -1f, 1f);
        }

        private void Update()
        {
            LayOut();
            if (SessionSettings.MenuOpen) { _leftFinger = _rightFinger = _trimFinger = int.MinValue; return; } // landing page owns the screen
            ReadPointers();
            MergeKeyboardFallback();
            PublishToDriver();
        }

        /// <summary>Everything along the bottom edge: [left pad] [Aircraft/Reset | Flaps] [Trim] [right pad].</summary>
        private void LayOut()
        {
            float w = Screen.width, h = Screen.height, s = Mathf.Min(w, h);
            _half = s * PadHalfFraction;
            float margin = s * 0.035f;
            float gap = s * 0.02f;
            // Pads sit at MID-HEIGHT (owner request), not along the bottom.
            _leftCenter = new Vector2(margin + _half, h * 0.5f);
            _rightCenter = new Vector2(w - margin - _half, h * 0.5f);

            // Pitch-trim slider: full pad height, just inboard of the right (elevator) pad.
            float trimW = _half * 0.22f;
            _trimRect = new Rect(_rightCenter.x - _half - gap - trimW, _rightCenter.y - _half, trimW, 2f * _half);

            // Centre cluster between the left pad and the trim slider: Aircraft over Reset; Flaps/Tow beside.
            // (The Brakes button is gone — braking lives on the bottom of the throttle pad.)
            float clusterLeft = _leftCenter.x + _half + gap;
            float clusterRight = _trimRect.x - gap;
            float avail = clusterRight - clusterLeft;
            float bh = _half * 0.26f;
            float bw = Mathf.Min(_half * 0.9f, (avail - gap) * 0.5f);
            float x0 = (clusterLeft + clusterRight) * 0.5f - (2f * bw + gap) * 0.5f;
            _acftRect = new Rect(x0, margin + bh + gap * 0.6f, bw, bh);
            _resetRect = new Rect(x0, margin, bw, bh);
            _brakeRect = new Rect(x0 + bw + gap, margin, bw, bh); // now the FLAPS slot (kept as the layout anchor)
        }

        /// <summary>Read touches (device) or the mouse (editor) and update each control's owning pointer.</summary>
        private void ReadPointers()
        {
            _brakeHeld = false;

            var pointers = new List<(int id, Vector2 pos, bool began, bool ended)>();
            if (Input.touchCount > 0)
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
                    if (p.id == _trimFinger) _trimFinger = int.MinValue; // trim holds where it was left
                    continue;
                }

                if (p.began)
                {
                    // Buttons are handled by IMGUI; don't let a button tap also grab a pad.
                    if (_resetRect.Contains(p.pos) || _acftRect.Contains(p.pos)) continue;

                    if (_trimRect.Contains(p.pos) && _trimFinger == int.MinValue) _trimFinger = p.id;
                    else if (NearPad(p.pos, _leftCenter) && _leftFinger == int.MinValue) _leftFinger = p.id;
                    else if (NearPad(p.pos, _rightCenter) && _rightFinger == int.MinValue) _rightFinger = p.id;
                }

                if (p.id == _leftFinger) DriveLeft(p.pos);
                else if (p.id == _rightFinger) DriveRight(p.pos);
                else if (p.id == _trimFinger) DriveTrim(p.pos);
            }
        }

        private bool NearPad(Vector2 pos, Vector2 center)
        {
            float r = _half * (1f + GrabSlop);
            return Mathf.Abs(pos.x - center.x) <= r && Mathf.Abs(pos.y - center.y) <= r;
        }

        private void DriveLeft(Vector2 pos)
        {
            _leftKnob = ClampToPad(pos, _leftCenter);
            Vector2 n = (_leftKnob - _leftCenter) / _half;
            _rudder = Shape(n.x);
            _throttle = Shape(n.y); // sticky: last value persists after release (see ReleaseLeft)
        }

        private void DriveRight(Vector2 pos)
        {
            _rightKnob = ClampToPad(pos, _rightCenter);
            Vector2 n = (_rightKnob - _rightCenter) / _half;
            _aileron = Shape(n.x); // pad/stick RIGHT (n.x>0) = positive aileron = roll RIGHT (sim convention)
            _elevator = Shape(InvertElevator ? -n.y : n.y);
        }

        /// <summary>Trim slider follows the finger; same sense as the stick (up = nose down unless inverted).</summary>
        private void DriveTrim(Vector2 pos)
        {
            float n = (pos.y - _trimRect.center.y) / (_trimRect.height * 0.5f);
            _pitchTrim = Mathf.Clamp(n, -1f, 1f);
        }

        private void ReleaseLeft()
        {
            _leftFinger = int.MinValue;
            _rudder = 0f;                 // rudder springs back
            // throttle holds — knob stays at its vertical height, recentre horizontally.
            _leftKnob = IdleLeftKnob();
        }

        private void ReleaseRight()
        {
            _rightFinger = int.MinValue;
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
                    _throttle = Mathf.Clamp(_throttle + tv * Time.deltaTime, -1f, 1f);
                    _leftKnob = IdleLeftKnob();
                }
            }

            if (_trimFinger == int.MinValue)
            {
                // = / - nudge pitch trim (same sense as the slider: = is up).
                float tt = Key(KeyCode.Equals) - Key(KeyCode.Minus);
                if (tt != 0f) _pitchTrim = Mathf.Clamp(_pitchTrim + tt * 0.5f * Time.deltaTime, -1f, 1f);
            }

            if (Input.GetKey(KeyCode.B)) _brakeHeld = true;
            if (Input.GetKeyDown(KeyCode.R)) DoReset();
            for (int i = 0; i < Fleet.Length && i < 10; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _driver.SwitchAircraft(Fleet[i]);
            }
        }

        /// <summary>Left-pad Y knob (-1..1) → throttle lever (0..1 power) and pad braking (0..1).</summary>
        private (float throttle01, float brake) SplitLeftAxis()
        {
            float k = (_throttle + 1f) * 0.5f;                 // 0 bottom .. 1 top
            float throttle01 = Mathf.Clamp01((k - IdleFraction) / (1f - IdleFraction));
            float brake = k < BrakeStartFraction ? Mathf.Clamp01((BrakeStartFraction - k) / BrakeStartFraction) : 0f;
            return (throttle01, brake);
        }

        private void PublishToDriver()
        {
            (float throttle01, float padBrake) = SplitLeftAxis();
            float brake = Mathf.Max(padBrake, _brakeHeld ? 1f : 0f);
            _driver.Sim.Aircraft.BrakeInput = brake;
            _driver.Sim.Aircraft.BrakeBias = brake > 0f ? Mathf.Clamp(_rudder * BrakeRudderBias, -1f, 1f) : 0f;
            // Trim biases the elevator like a real trim tab: hands-off stick still holds the trimmed attitude.
            float trim = (InvertElevator ? -_pitchTrim : _pitchTrim) * TrimAuthority;
            float elevator = Mathf.Clamp(_elevator + trim, -1f, 1f);
            // ThrottleLever: -1 = full forward (full power / brake stowed), +1 = full aft (idle / brake out).
            // Powered: lever = 1 - 2·throttle01. Glider: the same upper band is the speed-brake lever.
            float lever = 1f - 2f * throttle01;
            _driver.Inputs = new ControlInputs(_aileron, elevator, _rudder, lever);
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

        private void DoReset()
        {
            _rudder = _aileron = _elevator = 0f;
            _leftFinger = _rightFinger = _trimFinger = int.MinValue;
            _rightKnob = _rightCenter;
            _driver.ResetFlight(); // fires AircraftChanged → PresetTrim
        }

        private void CycleAircraft()
        {
            int idx = System.Array.IndexOf(Fleet, _driver.AircraftId);
            _driver.SwitchAircraft(Fleet[(idx + 1 + Fleet.Length) % Fleet.Length]);
        }

        // ---- rendering (display only) ----------------------------------------

        private void OnGUI()
        {
            if (SessionSettings.MenuOpen) return;
            EnsureStyles();

            bool powered = _driver.Sim?.Aircraft?.Config?.Propulsion != null;
            (float thr01, float padBrake) = SplitLeftAxis();
            string leftValue = padBrake > 0f
                ? $"BRAKE {Mathf.RoundToInt(padBrake * 100f)}%"
                : powered ? $"THR {Mathf.RoundToInt(thr01 * 100f)}%" : $"SPOILER {Mathf.RoundToInt((1f - thr01) * 100f)}%";
            DrawPad(_leftCenter, _leftFinger == int.MinValue ? IdleLeftKnob() : _leftKnob, "RUD / THR", leftValue);
            DrawPad(_rightCenter, _rightFinger == int.MinValue ? _rightCenter : _rightKnob, "AIL / ELE", null);
            DrawTrim();

            if (GUI.Button(ToGui(_resetRect), "Reset", _btnStyle)) DoReset();
            if (GUI.Button(ToGui(_acftRect), _driver.AircraftName, _btnStyle)) CycleAircraft();
            // Flaps (types that have them): cycle 0 / ½ / full, in the slot beside Reset.
            if (_driver.HasFlaps)
            {
                var fr = _brakeRect;
                double f = _driver.Sim.Aircraft.FlapFraction;
                if (GUI.Button(ToGui(fr), $"FLAPS {f * 100:F0}%", _btnStyle))
                {
                    _driver.Sim.Aircraft.FlapFraction = f < 0.25 ? 0.5 : f < 0.75 ? 1.0 : 0.0;
                }
            }
            // Glider on the ground: TOW button (aerotow from the runway) above the Aircraft button.
            var tow = GetComponent<TowController>();
            if (tow != null && _driver.Sim?.Aircraft?.Config?.Propulsion == null && !tow.Towing && _driver.GroundStart)
            {
                var r = new Rect(_acftRect.x, _acftRect.yMax + _half * 0.12f, _acftRect.width, _acftRect.height);
                if (GUI.Button(ToGui(r), "TOW", _btnStyle)) tow.StartTow();
            }
        }

        private Vector2 IdleLeftKnob() =>
            new Vector2(_leftCenter.x, _leftCenter.y + _throttle * _half); // throttle height persists

        /// <summary>Knob axis value (-1..1) for a given knob height fraction (0 bottom..1 top).</summary>
        private static float AxisForFraction(float f) => f * 2f - 1f;

        /// <summary>Square pad with centre cross, X/Y position lines through the knob, and the knob.</summary>
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
            GUI.DrawTexture(GuiRectCentered(knob, _half * 0.45f), _knobTex);

            // Label (and value) just above the pad.
            GUI.color = Color.white;
            float lh = _styleFs * 1.5f;
            GUI.Label(new Rect(pad.x, Screen.height - (pad.yMax + lh), d, lh), label, _labelStyle);
            if (value != null)
            {
                GUI.Label(new Rect(pad.x, Screen.height - (pad.yMax + 2f * lh), d, lh), value, _valueStyle);
            }
        }

        /// <summary>Vertical pitch-trim slider: track, centre + quarter ticks, and a bar knob.</summary>
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
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.028f);
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
            _labelStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(fs * 0.8f),
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

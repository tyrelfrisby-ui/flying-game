using System.Collections.Generic;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// RC-transmitter dual-touchpad controls (build-order step 5), the primary on-device input.
    ///
    ///   LEFT pad  : X = rudder (spring back to centre),  Y = THROTTLE (sticky — holds where you
    ///               leave it, like a real throttle / RC left stick). On the glider this same axis is
    ///               the speed-brake lever (down = deployed) per DATA-CONTRACTS.md.
    ///   RIGHT pad : X = aileron (spring),  Y = elevator (spring). Up = push = nose DOWN (realistic
    ///               stick sense); flip <see cref="InvertElevator"/> for arcade sense.
    ///
    /// True multitouch: both thumbs at once are read from <see cref="Input.touches"/> (a touch on the
    /// left half drives the left pad, right half the right pad). In the editor a single mouse pointer
    /// plus the arrow/A-D/S-W keys stand in so the pads and the whole loop can be exercised without a
    /// device. Rendered with OnGUI to match FlightHud/ChallengeHud; input is read from raw touches in
    /// Update (OnGUI is display only), so multitouch is unaffected by IMGUI's single-event model.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class TouchFlightControls : MonoBehaviour
    {
        [Header("Feel")]
        public float PadRadiusFraction = 0.14f; // of min(screen w,h)
        public float DeadZone = 0.06f;
        public float Expo = 0.35f;              // 0 = linear, 1 = full cubic (fine near centre)
        public bool InvertElevator = false;     // false = realistic (up = nose down)

        // Fleet the on-screen "Aircraft" button cycles through (matches KeyboardTestControls 1-0/F1-F2).
        private static readonly string[] Fleet =
        {
            "glider-2-33-like", "c172-like", "pitts-s2b-like", "stearman-pt17-like",
            "extra-300-like", "p51d-like", "f86-sabre-like", "seminole-like",
            "dc3-like", "boeing-737-like", "pa18-cub-like", "decathlon-8kcab-like",
        };

        private FlightSimDriver _driver;

        // Live control state (already shaped, -1..1). Throttle is sticky so it lives across frames.
        private float _rudder, _throttle, _aileron, _elevator;
        private bool _brakeHeld;

        // Pad geometry recomputed each frame from screen size (screen px, origin bottom-left).
        private Vector2 _leftCenter, _rightCenter;
        private float _radius;

        // Which finger (or mouse, id = -1) currently owns each pad, and its current position.
        private int _leftFinger = int.MinValue, _rightFinger = int.MinValue;
        private Vector2 _leftKnob, _rightKnob; // screen px; knob = pad centre when idle

        // On-screen button rects (screen px, bottom-left origin) — computed in Update, drawn in OnGUI.
        private Rect _brakeRect, _resetRect, _acftRect;

        private Texture2D _baseTex, _knobTex, _btnTex;
        private GUIStyle _btnStyle;

        private void Awake()
        {
            _driver = GetComponent<FlightSimDriver>();
            Input.multiTouchEnabled = true;
        }

        private void Update()
        {
            LayOutPads();
            ReadPointers();
            MergeKeyboardFallback();
            PublishToDriver();
        }

        private void LayOutPads()
        {
            float w = Screen.width, h = Screen.height;
            _radius = Mathf.Min(w, h) * PadRadiusFraction;
            float margin = _radius * 1.25f;
            _leftCenter = new Vector2(margin + _radius, margin + _radius);
            _rightCenter = new Vector2(w - margin - _radius, margin + _radius);

            // Buttons across the top edge (below the HUD text). Sized off the pad radius.
            float bw = _radius * 1.6f, bh = _radius * 0.55f, gap = bh * 0.4f, top = h - bh - gap;
            _resetRect = new Rect(w * 0.5f - bw - gap * 0.5f, top, bw, bh);
            _acftRect = new Rect(w * 0.5f + gap * 0.5f, top, bw, bh);
            // Momentary wheel-brake button sits just inboard of the right (elevator) pad.
            _brakeRect = new Rect(_rightCenter.x - _radius - bw - gap, _radius * 0.5f, bw, bh);
        }

        /// <summary>Read touches (device) or the mouse (editor) and update each pad's owning pointer.</summary>
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
                    continue;
                }

                // Momentary brake: any active pointer inside the brake button holds the brakes.
                if (_brakeRect.Contains(p.pos))
                {
                    _brakeHeld = true;
                    continue;
                }

                bool leftHalf = p.pos.x < Screen.width * 0.5f;
                if (p.began)
                {
                    if (leftHalf && _leftFinger == int.MinValue) _leftFinger = p.id;
                    else if (!leftHalf && _rightFinger == int.MinValue) _rightFinger = p.id;
                }

                if (p.id == _leftFinger) DriveLeft(p.pos);
                else if (p.id == _rightFinger) DriveRight(p.pos);
            }
        }

        private void DriveLeft(Vector2 pos)
        {
            _leftKnob = ClampToPad(pos, _leftCenter);
            Vector2 n = (_leftKnob - _leftCenter) / _radius;
            _rudder = Shape(n.x);
            _throttle = Shape(n.y); // sticky: last value persists after release (see ReleaseLeft)
        }

        private void DriveRight(Vector2 pos)
        {
            _rightKnob = ClampToPad(pos, _rightCenter);
            Vector2 n = (_rightKnob - _rightCenter) / _radius;
            _aileron = Shape(-n.x); // pad/stick RIGHT = roll RIGHT
            _elevator = Shape(InvertElevator ? -n.y : n.y);
        }

        private void ReleaseLeft()
        {
            _leftFinger = int.MinValue;
            _rudder = 0f;                 // rudder springs back
            // throttle holds — knob stays at its vertical height, recentre horizontally.
            _leftKnob = new Vector2(_leftCenter.x, _leftCenter.y + _throttle * _radius);
        }

        private void ReleaseRight()
        {
            _rightFinger = int.MinValue;
            _aileron = 0f;
            _elevator = 0f;
            _rightKnob = _rightCenter;   // both spring back
        }

        /// <summary>Editor keyboard fallback (only when a pad isn't under a live pointer).</summary>
        private void MergeKeyboardFallback()
        {
            if (_rightFinger == int.MinValue)
            {
                float kx = Key(KeyCode.LeftArrow) - Key(KeyCode.RightArrow); // Right arrow = roll right
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
                    _leftKnob = new Vector2(_leftCenter.x, _leftCenter.y + _throttle * _radius);
                }
            }

            if (Input.GetKey(KeyCode.B)) _brakeHeld = true;
            if (Input.GetKeyDown(KeyCode.R)) DoReset();
            for (int i = 0; i < Fleet.Length && i < 10; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _driver.SwitchAircraft(Fleet[i]);
            }
        }

        private void PublishToDriver()
        {
            _driver.Sim.Aircraft.BrakeInput = _brakeHeld ? 1f : 0f;
            // ThrottleLever: -1 = full forward (power / stowed brake), +1 = full aft (idle / brake out).
            // Pad up (throttle +1) = full power → lever -1, so lever = -throttle.
            _driver.Inputs = new ControlInputs(_aileron, _elevator, _rudder, -_throttle);
        }

        // ---- shaping helpers -------------------------------------------------

        private float Shape(float v)
        {
            v = Mathf.Clamp(v, -1f, 1f);
            float s = Mathf.Sign(v);
            float a = Mathf.Abs(v);
            if (a < DeadZone) return 0f;
            a = (a - DeadZone) / (1f - DeadZone);              // rescale past dead zone
            a = Mathf.Lerp(a, a * a * a, Mathf.Clamp01(Expo)); // expo blend
            return s * a;
        }

        private Vector2 ClampToPad(Vector2 pos, Vector2 center)
        {
            Vector2 d = pos - center;
            if (d.sqrMagnitude > _radius * _radius) d = d.normalized * _radius;
            return center + d;
        }

        private static float Key(KeyCode k) => Input.GetKey(k) ? 1f : 0f;

        private void DoReset()
        {
            _rudder = _throttle = _aileron = _elevator = 0f;
            _leftFinger = _rightFinger = int.MinValue;
            _leftKnob = _leftCenter;
            _rightKnob = _rightCenter;
            _driver.ResetFlight();
        }

        private void CycleAircraft()
        {
            int idx = System.Array.IndexOf(Fleet, _driver.AircraftId);
            _driver.SwitchAircraft(Fleet[(idx + 1 + Fleet.Length) % Fleet.Length]);
        }

        // ---- rendering (display only) ----------------------------------------

        private void OnGUI()
        {
            EnsureTextures();
            DrawPad(_leftCenter, _leftFinger == int.MinValue ? IdleLeftKnob() : _leftKnob, "THR / RUD");
            DrawPad(_rightCenter, _rightFinger == int.MinValue ? _rightCenter : _rightKnob, "ELE / AIL");

            if (GUI.Button(ToGui(_resetRect), "Reset", _btnStyle)) DoReset();
            if (GUI.Button(ToGui(_acftRect), _driver.AircraftName, _btnStyle)) CycleAircraft();

            // Brake is momentary (held via touch/key), so just render its lit/idle state.
            GUI.color = _brakeHeld ? new Color(1f, 0.5f, 0.3f, 0.95f) : new Color(1f, 1f, 1f, 0.55f);
            GUI.Box(ToGui(_brakeRect), "Brakes", _btnStyle);
            GUI.color = Color.white;
        }

        private Vector2 IdleLeftKnob() =>
            new Vector2(_leftCenter.x, _leftCenter.y + _throttle * _radius); // throttle height persists

        private void DrawPad(Vector2 centerScreen, Vector2 knobScreen, string label)
        {
            float d = _radius * 2f;
            GUI.color = new Color(1f, 1f, 1f, 0.28f);
            GUI.DrawTexture(GuiRectCentered(centerScreen, d), _baseTex);
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(GuiRectCentered(knobScreen, _radius * 0.9f), _knobTex);
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            var lr = GuiRectCentered(new Vector2(centerScreen.x, centerScreen.y - _radius - 14f), 0);
            GUI.Label(new Rect(centerScreen.x - _radius, Screen.height - centerScreen.y + _radius + 2f, d, 18f),
                label, _btnStyle);
            GUI.color = Color.white;
            _ = lr;
        }

        /// <summary>Screen rect (bottom-left origin) → GUI rect (top-left origin).</summary>
        private Rect ToGui(Rect screenRect) =>
            new Rect(screenRect.x, Screen.height - screenRect.y - screenRect.height, screenRect.width, screenRect.height);

        private Rect GuiRectCentered(Vector2 centerScreen, float size) =>
            new Rect(centerScreen.x - size * 0.5f, Screen.height - centerScreen.y - size * 0.5f, size, size);

        private void EnsureTextures()
        {
            if (_baseTex != null) return;
            _baseTex = CircleTex(96, 0.62f, 0.72f); // soft ring
            _knobTex = CircleTex(64, 0.0f, 1.0f);   // solid knob
            _btnTex = CircleTex(4, 0f, 1f);
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.028f),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
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

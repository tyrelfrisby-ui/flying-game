using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The GAME's host for the teaching layers built for the Aero Widget (owner rule 2026-10-09: "keep game and widget in
    /// parity"): the same vectors and labels (protocol 7: wing/tail winds, inertial, total aero, tail force, moment arcs, yaw
    /// moment, fin force, body axes, CG/NP), the same insets (CL–α, L/D, power required, ball, AoA, W&amp;B), the same
    /// autopilot (ALT · VS · FLC · HDG · YD · A/T; LNAV/LOC/APP shown, not active) and the same weight &amp; balance — one
    /// implementation, drawn on the game's screen from the chase camera. The AERO panel (<see cref="GameAeroPanel"/>) has the
    /// switches. The autopilot flies through <see cref="FlightSimDriver.AutoFilter"/>: it replaces only the channels it owns,
    /// and the pilot's stick disconnects it (rudder: the yaw damper), like the widget.
    /// </summary>
    public sealed class GameAeroHost : AeroHost
    {
        public FlightSimDriver Driver;
        private readonly Controls _controls = new();
        public override IAeroControls AeroControls => _controls;
        public override string AircraftName => string.IsNullOrEmpty(Driver?.AircraftName) ? base.AircraftName : Driver.AircraftName.ToUpperInvariant();
        private Aircraft _seen;
        private bool _loadingChanged;
        private ControlInputs _pilotAtEngage; private bool _wasOn;
        private const string Prefs = "aero.parity.";

        /// <summary>The adapter the autopilot writes: the channels it doesn't fly read back the pilot's.</summary>
        private sealed class Controls : IAeroControls
        {
            public double Aileron { get; set; }
            public double Elevator { get; set; }
            public double Rudder { get; set; }
            public double Throttle01 { get; set; }
            public bool ElevatorFree { get; set; }
        }

        private void Awake()
        {
            Loading = new WidgetLoading(this); Autopilot = new WidgetAutopilot(this) { AutoThrottle = false }; Curves = new WidgetCurves();
            Load();
        }

        private void Start()
        {
            Cam = GetComponent<Camera>();
            _vectors = gameObject.AddComponent<WidgetVectors>(); _vectors.Widget = this;
            _display = gameObject.AddComponent<WidgetControlsDisplay>(); _display.Host = this;
            if (Driver != null) Driver.AutoFilter = Filter;
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AERO_PARITY"))) StartCoroutine(ParitySelfTest());
        }

        /// <summary>AERO_PARITY=1: fly, switch on every vector family and the insets, engage the AP, and save screenshots
        /// (persistentDataPath/parity-*.png) — for the no-overlap check on each screen.</summary>
        private System.Collections.IEnumerator ParitySelfTest()
        {
            Application.runInBackground = true;
            yield return new WaitForSecondsRealtime(3f);
            string id = System.Environment.GetEnvironmentVariable("AERO_PARITY");
            if (id != null && id.Contains("-")) SessionSettings.AircraftId = id;   // e.g. AERO_PARITY=c172-like
            Object.FindFirstObjectByType<StartMenu>()?.Fly();
            yield return new WaitForSecondsRealtime(4f);
            Show["vectors"] = true; foreach (var k in new[] { "yawMoment", "finForce", "cgnp" }) Show[k] = true;
            foreach (var n in new[] { "clAlpha", "aoa", "ball", "wb" }) Insets.Add(n);
            Autopilot.Engage(Read().HeightFt, null, "alt", null, "hdg", (Read().HeadingDeg + 40) % 360);
            string dir = Application.persistentDataPath;
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSecondsRealtime(5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"parity-{i}.png"));
                Debug.Log($"[Parity] free {_display.GameInsetArea} keepout {_display.GameKeepOut} scale {_display.TextScale} dpi {Screen.dpi} screen {Screen.width}x{Screen.height} insetsDrawn {_display.LabelKeepOut.Count}");
                Debug.Log($"[Parity] shot {i}: AP {Autopilot.Annunciation} status {Autopilot.Status} alt {Read().HeightFt:0} hdg {Read().HeadingDeg:0} W&B {Loading.CgMac:0.0}%");
            }
            Loading.Set(Loading.DefaultKg * 1.2, Loading.DefaultCgMac + 6, false); LoadingTouched();
            yield return new WaitForSecondsRealtime(6f);
            Debug.Log($"[Parity] loaded: {Loading.Kg * 2.2046:0} lb CG {Loading.CgMac:0.0}% AP {Autopilot.Status} alt {Read().HeightFt:0}");
            GameAeroPanel.Toggle();
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "parity-panel.png"));
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[Parity] DONE " + dir);
        }

        // ---- the switches persist between launches ----
        public void Save()
        {
            foreach (var kv in Show) PlayerPrefs.SetInt(Prefs + "show." + kv.Key, kv.Value ? 1 : 0);
            foreach (var n in WidgetControlsDisplay.InsetNames) PlayerPrefs.SetInt(Prefs + "inset." + n, Insets.Contains(n) ? 1 : 0);
            PlayerPrefs.Save();
        }
        private void Load()
        {
            var keys = new System.Collections.Generic.List<string>(Show.Keys);
            foreach (var k in keys) if (PlayerPrefs.HasKey(Prefs + "show." + k)) Show[k] = PlayerPrefs.GetInt(Prefs + "show." + k) == 1;
            foreach (var n in WidgetControlsDisplay.InsetNames) if (PlayerPrefs.GetInt(Prefs + "inset." + n, 0) == 1) Insets.Add(n);
        }

        /// <summary>Anything to draw (or to capture the forces for)?</summary>
        public bool Active => Show["vectors"] || Insets.Count > 0;

        private void Update()
        {
            if (Driver == null || Driver.Sim == null) return;
            var ac = Driver.Sim.Aircraft;
            if (ac != _seen)
            {
                // A new flight (respawn, a new type, a challenge start): the same type keeps the loading the pilot set.
                bool sameType = Config != null && Driver.AircraftId == AircraftId;
                double kg = Loading.TargetKg, cg = Loading.TargetCgMac;
                AircraftId = Driver.AircraftId; Config = ac.Config; Ac = ac; _seen = ac;
                Measure();
                Loading.Configure(Config, sameType && _loadingChanged, kg, cg);
                Autopilot.Off();
            }
            Driver.ForceCapture = Active;
            UiLayout.AeroInsetsShown = Insets.Count > 0 && !SessionSettings.MenuOpen && !Driver.Replaying;
            if (!Driver.Replaying) Loading.Tick(Time.deltaTime);
        }

        /// <summary>Called by the AERO panel whenever the pilot moves a loading slider.</summary>
        public void LoadingTouched() => _loadingChanged = true;

        private void Measure()
        {
            double ymax = 0, xmin = double.MaxValue, xmax = double.MinValue;
            foreach (var sf in Config.Surfaces) foreach (var st in sf.Strips)
            {
                if (sf.Id.ToLowerInvariant().Contains("wing")) ymax = System.Math.Max(ymax, System.Math.Abs(st.Pos[1]) + System.Math.Sqrt(st.Area) * 0.5);
                xmin = System.Math.Min(xmin, st.Pos[0] - st.Chord * 0.75); xmax = System.Math.Max(xmax, st.Pos[0] + st.Chord * 0.25);
            }
            SpanM = ymax > 0 ? (float)(2 * ymax) : 11f;
            LengthM = xmax > xmin ? (float)(xmax - xmin) : SpanM * 0.8f;
        }

        /// <summary>The autopilot between the pilot's controls and the sim step (FlightSimDriver.AutoFilter).</summary>
        private ControlInputs Filter(ControlInputs pilot, float dt)
        {
            var ap = Autopilot;
            double thr = (1.0 - pilot.ThrottleLever) * 0.5;
            if (!ap.On) { _controls.Elevator = pilot.Elevator; _controls.Aileron = pilot.Aileron; _controls.Rudder = pilot.Rudder; _controls.Throttle01 = thr; }
            if (Ac == null || (!ap.On && !ap.YawDamper)) { _wasOn = false; return pilot; }
            if (ap.On && !_wasOn) _pilotAtEngage = pilot;
            _wasOn = ap.On;
            // The pilot's hand on the stick disconnects the AP (as in the widget); a push on a pedal turns the YD off.
            if (ap.On && (System.Math.Abs(pilot.Aileron - _pilotAtEngage.Aileron) > 0.2 || System.Math.Abs(pilot.Elevator - _pilotAtEngage.Elevator) > 0.2))
            { ap.Off(disconnect: true); _wasOn = false; }
            if (ap.YawDamper && System.Math.Abs(pilot.Rudder) > 0.25) ap.YawDamper = false;
            // The channels the AP doesn't fly read the pilot's; then it flies one step.
            _controls.Aileron = pilot.Aileron; _controls.Rudder = pilot.Rudder; _controls.Throttle01 = thr; _controls.ElevatorFree = pilot.ElevatorFree;
            if (!ap.On) _controls.Elevator = pilot.Elevator;
            ap.Tick(dt);
            bool roll = ap.On && !ap.PitchOnly, power = ap.On && (ap.AutoThrottle || ap.PitchMode == "flc"), yaw = ap.YawDamper;
            return new ControlInputs(roll ? _controls.Aileron : pilot.Aileron, ap.On ? _controls.Elevator : pilot.Elevator, yaw ? _controls.Rudder : pilot.Rudder,
                power ? 1.0 - 2.0 * _controls.Throttle01 : pilot.ThrottleLever, pilot.AileronFree, ap.On ? false : pilot.ElevatorFree, pilot.RudderFree);
        }

        // ---- drawing: after the camera has rendered (its final pose), straight onto the screen ----
        private void OnPostRender()
        {
            if (_display == null || Ac == null || !Active || SessionSettings.MenuOpen || Driver == null || Driver.Replaying) return;   // (a replay shows a recorded pose, not the live forces)
            if (ChaseCamera.InCockpit) return;   // vectors on the airframe need the outside view
            // Text: about 11 pt on every screen (iPhone 3×, iPad 2×, Mac); plates and arrows scale with it.
            float dpi = Screen.dpi > 1f ? Screen.dpi : 160f;
            _display.TextScale = Mathf.Clamp(0.58f * dpi / 163f, 1.2f, 2.2f);
            // The free picture (owner rule: no overlap): between the dial columns (or the pads), under the toolbar and the text
            // lines, above the bottom buttons. Vectors are clipped to it, labels and insets stay inside it.
            var view = ScreenLayout.ViewRect;   // origin bottom-left (the portrait tray is at the bottom)
            float H = Screen.height;
            bool dials = UiLayout.DialsShown;   // the analog dials' two columns: the middle between them
            float left = dials ? Mathf.Max(UiLayout.BandLeft, UiLayout.DialsLeft) : UiLayout.BandLeft, right = dials ? Mathf.Min(UiLayout.BandRight, UiLayout.DialsRight) : UiLayout.BandRight;
            float top = Mathf.Min(view.yMax, H - Mathf.Max(UiLayout.StackBottomLastFrame, UiLayout.ToolbarBottom) - UiLayout.Gap);
            float bottom = Mathf.Max(view.yMin, UiLayout.BottomLimit < 99000f ? H - UiLayout.BottomLimit : view.yMin);
            var free = new Rect(left, bottom, Mathf.Max(0, right - left), Mathf.Max(0, top - bottom));
            _display.GameView = free;
            _display.GameInsetArea = free;
            _display.GameKeepOut = ScreenLayout.HasAircraftKeepOut ? ScreenLayout.AircraftKeepOut : default;
            _display.DrawScreen();
        }
    }
}

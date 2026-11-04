using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// TV + GAME CONTROLLER (Ty, 2026-11-04: "the same TV mode with a Bluetooth controller: the phone screen becomes a
    /// touchpad of extra buttons and controls — quick access to views, switches, menus"). With the picture on the TV and
    /// a pad flying the aeroplane, the phone has nothing else to do, so it becomes a grid of big buttons: views,
    /// switches, actions (Core TvPadLayout has the list and the layout). The toolbar stays above it for MENU and OPTIONS.
    ///
    /// Every button presses a button that already exists: the touch controls' own (TouchFlightControls.PressFromPad, the
    /// same queue its on-screen buttons and the pad's d-pad feed), the view menu's ChaseCamera.SetView, the toolbar's
    /// REPLAY / CLIP / REC / AERO. The pad keeps every binding it has. Touches are read raw in Update, so a tap lands
    /// while the thumbs are on the pad; OnGUI only draws. Lives on TvOutput's runner, so it exists only where TV mode does.
    /// </summary>
    public sealed class TvButtonPad : MonoBehaviour
    {
        /// <summary>The phone is the grid: TV mode, a pad connected, and no menu or replay over it.</summary>
        public static bool Showing => TvOutput.Active && GamepadInput.Now.Connected && !SessionSettings.MenuOpen && !SessionSettings.ReplayActive;

        private TouchFlightControls _controls;
        private FlightSimDriver _driver;
        private SmokeController _smoke;
        private ChaseCamera _chase;
        private FlightReplay _replay;
        private readonly List<(PadButton Button, Rect Gui)> _cells = new();
        private string _key;
        private GUIStyle _btn, _btnOn, _text, _caption;
        private Texture2D _bg, _bgOn;
        private int _fs;

        private void Update()
        {
            if (!Showing) return;
            if (_controls == null)
            {
                _controls = FindFirstObjectByType<TouchFlightControls>();
                if (_controls == null) return;
                _driver = _controls.GetComponent<FlightSimDriver>();
                _smoke = _controls.GetComponent<SmokeController>();
            }
            if (_chase == null) _chase = FindFirstObjectByType<ChaseCamera>();
            if (_replay == null) _replay = FindFirstObjectByType<FlightReplay>();
            LayOutIfChanged();

            if (Input.touchCount > 0)
            {
                foreach (Touch t in Input.touches)
                    if (t.phase == TouchPhase.Began) Tap(t.position);
            }
            else if (Input.GetMouseButtonDown(0)) Tap(Input.mousePosition);   // Editor stand-in
        }

        private bool Flaps => _driver != null && _driver.HasFlaps && !Practice.PracticeController.AnyActive;
        private bool Gear => _driver?.Sim?.Aircraft?.Config?.RetractableGear == true;
        private bool SmokeHere => _smoke != null && _smoke.Available;

        private void LayOutIfChanged()
        {
            bool cockpit = ChaseCamera.InCockpit;
            string key = $"{Screen.width}x{Screen.height}|{Flaps}{Gear}{SmokeHere}{cockpit}|{UiLayout.SafeTop}|{UiLayout.SafeBottom}|{UiLayout.SafeLeft}|{UiLayout.SafeRight}";
            if (key == _key) return;
            _key = key;
            // Under the toolbar and three of the text stack's lines (events, challenge, "Showing on the TV"), inside the
            // safe area. GUI coordinates, top-left origin.
            float lines = UiLayout.MinText * 1.5f * 3f;
            float x = UiLayout.SafeLeft + UiLayout.Margin, y = UiLayout.ToolbarBottom + UiLayout.Gap + lines;
            float w = Screen.width - UiLayout.SafeLeft - UiLayout.SafeRight - 2f * UiLayout.Margin;
            float h = Screen.height - y - UiLayout.SafeBottom - UiLayout.Margin;
            _cells.Clear();
            var groups = TvPadLayout.Groups(Flaps, Gear, SmokeHere, cockpit);
            foreach (var c in TvPadLayout.Layout(groups, ScreenLayout.Portrait, x, y, w, h, UiLayout.Gap * 1.4f))
                _cells.Add((c.Button, new Rect((float)c.X, (float)c.Y, (float)c.W, (float)c.H)));
        }

        private void Tap(Vector2 screen)
        {
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            foreach (var c in _cells)
                if (c.Gui.Contains(gui)) { Press(c.Button); return; }
        }

        private void Press(PadButton b)
        {
            switch (b)
            {
                case PadButton.ViewChase: _chase?.SetView(ChaseCamera.View.RelativeWind); break;
                case PadButton.ViewCockpit: _chase?.SetView(ChaseCamera.View.Cockpit); break;
                case PadButton.ViewTail: _chase?.SetView(ChaseCamera.View.Tail); break;
                case PadButton.ViewSide: _chase?.SetView(ChaseCamera.View.SideLeft); break;
                case PadButton.ViewFlyby: _chase?.SetView(ChaseCamera.View.Flyby); break;
                case PadButton.ViewLock: _chase?.ToggleLock(); break;
                case PadButton.Pause: _replay?.Enter(); break;
                case PadButton.Clip: if (!ClipRecorder.Busy) ClipRecorder.SaveClip(); break;
                case PadButton.Rec: ClipRecorder.ToggleRecording(); break;
                case PadButton.Aero: Widget.GameAeroPanel.Toggle(); break;
                default: _controls?.PressFromPad(b); break;
            }
            Debug.Log($"[TV] pad button {b}");
        }

        private void OnGUI()
        {
            if (!Showing || _cells.Count == 0) return;
            EnsureStyles();
            foreach (var c in _cells)
            {
                bool on = IsOn(c.Button);
                GUI.Box(c.Gui, "", on ? _btnOn : _btn);
                string caption = Caption(c.Button);
                float capH = caption != null ? _caption.fontSize * 1.4f : 0f;
                var label = new Rect(c.Gui.x, c.Gui.y, c.Gui.width, c.Gui.height - capH);
                UiLayout.ButtonLabel(label, Label(c.Button), _text);
                if (caption != null) GUI.Label(new Rect(c.Gui.x, c.Gui.yMax - capH * 1.3f, c.Gui.width, capH), caption, _caption);
            }
        }

        /// <summary>Lit when that state is what the button shows: the current view, smoke on, recording.</summary>
        private bool IsOn(PadButton b)
        {
            var v = _chase != null ? _chase.CurrentView : ChaseCamera.View.RelativeWind;
            switch (b)
            {
                case PadButton.ViewChase: return v == ChaseCamera.View.RelativeWind;
                case PadButton.ViewCockpit: return v == ChaseCamera.View.Cockpit;
                case PadButton.ViewTail: return v == ChaseCamera.View.Tail;
                case PadButton.ViewSide: return v == ChaseCamera.View.SideLeft;
                case PadButton.ViewFlyby: return v == ChaseCamera.View.Flyby;
                case PadButton.ViewLock: return v == ChaseCamera.View.Locked;
                case PadButton.Smoke: return _smoke != null && _smoke.On;
                case PadButton.Rec: return ClipRecorder.Recording;
                case PadButton.Aero: return Widget.GameAeroPanel.IsOpen;
                default: return false;
            }
        }

        private string Label(PadButton b)
        {
            if (b == PadButton.Gear) return _driver?.Sim?.Aircraft?.GearDown == true ? "GEAR UP" : "GEAR DOWN";   // what a tap does
            if (b == PadButton.Rec && ClipRecorder.Recording) return "STOP REC";
            if (b == PadButton.CamRef)   // the touch controls' own CAM label (FLT PATH / TRACK, and a lesson's lock)
            {
                bool forced = _driver != null && _driver.GroundReferenceForced;
                return CameraReference.Label(CameraReference.Effective(SessionSettings.CameraReferenceMode, forced), forced);
            }
            return TvPadLayout.Label(b);
        }

        /// <summary>A small second line where the state is not a simple on / off.</summary>
        private string Caption(PadButton b)
        {
            var ac = _driver?.Sim?.Aircraft;
            switch (b)
            {
                case PadButton.FlapsUp:
                case PadButton.FlapsDown: return ac != null ? $"{ac.FlapFraction * 100:F0} %" : null;
                case PadButton.Gear: return ac != null ? (ac.GearDown ? "now down" : "now up") : null;
                default: return null;
            }
        }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.03f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.9f));     // the toolbar's own button colours (ViewPanel)
            _bgOn ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = Mathf.Max(UiLayout.CaptionText, fs), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Color.white, background = _bg } };
            _btnOn = new GUIStyle(_btn) { normal = { textColor = Color.white, background = _bgOn } };
            _text = new GUIStyle(_btn) { normal = { textColor = Color.white, background = null } };
            _caption = new GUIStyle { font = f, fontSize = UiLayout.CaptionText, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.9f, 0.95f) } };
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}

using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The game's AERO panel (toolbar AERO button; owner 2026-10-09: "add all of the new features … from widget work to the
    /// game as well — keep game and widget in parity"). The same switches as the widget's Display panel and remote:
    ///   FORCES   — every vector, moment and reference of protocol 7, each on/off, plus the master switch;
    ///   INSETS   — CL–α lift curve, L/D, power required, slip/skid ball, AoA gauge, W&amp;B envelope;
    ///   AUTOPILOT — AP · YD · A/T · ALT · VS ▲▼ · FLC · SPD ± · ALT SEL ± · HDG ± · SYNC · ROL; LNAV / LOC / APP present but
    ///              not active yet (a later build), as in the widget;
    ///   WEIGHT &amp; BALANCE — weight and CG sliders (slewed, live in any condition), RESET, the limits.
    /// The sim keeps running underneath. While the panel is shut, the autopilot's mode line sits in the text stack.
    /// </summary>
    public sealed class GameAeroPanel : MonoBehaviour
    {
        public GameAeroHost Host;
        public static bool IsOpen { get; private set; }
        public static void Toggle() => IsOpen = !IsOpen;
        public static void Close() => IsOpen = false;
        private static Rect _panel; private static int _panelFrame = -10;
        /// <summary>A touch on the open panel is for its switches, never the stick (screen px, origin bottom-left).</summary>
        public static bool Blocks(Vector2 screenPos) => IsOpen && Time.frameCount - _panelFrame <= 2 && _panel.Contains(new Vector2(screenPos.x, Screen.height - screenPos.y));
        private GUIStyle _btn, _btnOn, _btnDim, _head, _label, _amber;
        private Texture2D _bg, _btnBg, _btnOnBg, _btnDimBg;
        private int _fs;
        private string _note; private float _noteUntil;
        private string _initFor;

        private static readonly (string key, string name)[] Forces =
        {
            ("wingWind", "Wing relative wind"), ("tailWind", "Tail wind (downwash, prop)"), ("inertial", "Inertial m(g−a)"),
            ("total", "Total aero"), ("tail", "Tail force"), ("moments", "Pitch moments"),
            ("yawMoment", "Yaw moment"), ("finForce", "Fin side force"), ("bodyAxes", "Body axes"),
            ("cgnp", "CG & neutral point"), ("lift", "Lift"), ("drag", "Drag"),
            ("weight", "Weight"), ("thrust", "Thrust"), ("wind", "Relative wind"), ("wheels", "Wheel loads"),
        };
        private static readonly (string key, string name)[] InsetChips =
        {
            ("clAlpha", "Lift curve CL–α"), ("liftDrag", "Lift / drag"), ("powerRequired", "Power required"),
            ("ball", "Slip / skid ball"), ("aoa", "AoA gauge"), ("wb", "Weight & balance"),
        };

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.92f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            _btnDimBg ??= Solid(new Color(0.12f, 0.14f, 0.17f, 0.9f));
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(4, 4, 3, 3) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _btnDim = new GUIStyle(_btn) { normal = { textColor = new Color(1, 1, 1, 0.35f), background = _btnDimBg }, active = { textColor = new Color(1, 1, 1, 0.35f), background = _btnDimBg } };
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.05f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.55f, 0.95f, 1f) } };
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.88f, 0.92f) } };
            _amber = new GUIStyle(_label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.75f, 0.2f) } };
        }

        private static double R100(double v) => System.Math.Round(v / 100.0) * 100.0;

        private void OnGUI()
        {
            if (SessionSettings.MenuOpen) { IsOpen = false; return; }
            if (Host == null || Host.Ac == null) return;
            EnsureStyles();
            var ap = Host.Autopilot;
            var rd = Host.Read();
            // The selectors start at the present values (a new aircraft: again).
            if (_initFor != Host.AircraftId + Host.Ac.GetHashCode()) { _initFor = Host.AircraftId + Host.Ac.GetHashCode(); if (!ap.On) { ap.AltFt = R100(rd.HeightFt); ap.Kias = System.Math.Round(rd.Kias); ap.HdgDeg = System.Math.Round(rd.HeadingDeg); ap.VsFpm = 0; } }
            if (!IsOpen)
            {
                // The flight-mode line (text stack: never on a dial or a button).
                string fma = ap.Disc ? "AP DISCONNECT" : ap.Annunciation;
                int dropped = Host.Display != null ? Host.Display.InsetsDropped : 0;
                if (dropped > 0 && Host.Insets.Count > 0) { Rect l2 = UiLayout.NextLine(_fs * 1.4f); UiLayout.Label(l2, $"{dropped} inset{(dropped > 1 ? "s" : "")} hidden: no room beside the airplane (fewer insets, a wider window or portrait)", _amber); }
                if (fma != null) { Rect line = UiLayout.NextLine(_fs * 1.6f); UiLayout.Label(line, fma, ap.Disc || ap.Status != "holding" && ap.On ? _amber : _label); }
                return;
            }
            float s = Mathf.Min(Screen.width, Screen.height);
            bool twoCol = Screen.width > Screen.height * 1.3f;
            float lh = _fs * 1.8f, gap = _fs * 0.45f;
            float w = twoCol ? Mathf.Min(Screen.width * 0.94f, s * 1.9f) : Mathf.Min(Screen.width * 0.94f, s * 0.94f);
            float rows = twoCol ? 13.6f : 25.5f;
            float h = Mathf.Min(rows * (lh + gap * 0.5f) + gap * 2, Screen.height - UiLayout.ToolbarBottom - gap * 2);
            var panel = new Rect((Screen.width - w) * 0.5f, UiLayout.ToolbarBottom + gap, w, h);
            GUI.DrawTexture(panel, _bg);
            _panel = panel; _panelFrame = Time.frameCount; UiLayout.ModalShown();
            float cw = twoCol ? (panel.width - 3 * gap) * 0.5f : panel.width - 2 * gap;
            float x = panel.x + gap, y = panel.y + gap, top = y;

            void Head(string t) { GUI.Label(new Rect(x, y, cw, lh), t, _head); y += lh; }
            void Chips((string key, string name)[] list, int per, System.Func<string, bool> get, System.Action<string> flip)
            {
                float bw = (cw - (per - 1) * gap * 0.5f) / per;
                for (int i = 0; i < list.Length; i++)
                {
                    var r = new Rect(x + (i % per) * (bw + gap * 0.5f), y, bw, lh);
                    bool on = get(list[i].key);
                    if (UiLayout.Button(r, list[i].name, on ? _btnOn : _btn)) flip(list[i].key);
                    if (i % per == per - 1 || i == list.Length - 1) y += lh + gap * 0.5f;
                }
            }
            bool Btn(Rect r, string t, bool on) => UiLayout.Button(r, t, on ? _btnOn : _btn);
            Rect Cell(int i, int n) { float bw = (cw - (n - 1) * gap * 0.5f) / n; return new Rect(x + i * (bw + gap * 0.5f), y, bw, lh); }

            // ---- FORCES ----
            Head("FORCES ON THE AIRPLANE");
            if (Btn(Cell(0, 2), Host.Show["vectors"] ? "VECTORS ON" : "VECTORS OFF", Host.Show["vectors"])) { Host.Show["vectors"] = !Host.Show["vectors"]; Host.Save(); }
            GUI.Label(Cell(1, 2), ChaseCamera.InCockpit ? "  (outside views only)" : "  from the sim's own forces", _label);
            y += lh + gap * 0.5f;
            Chips(Forces, 3, k => Host.Show[k], k => { Host.Show[k] = !Host.Show[k]; if (Host.Show[k]) Host.Show["vectors"] = true; Host.Save(); });
            y += gap * 0.5f;
            Head("INSETS");
            Chips(InsetChips, 3, k => Host.Insets.Contains(k), k => { if (!Host.Insets.Remove(k)) Host.Insets.Add(k); Host.Save(); });

            if (twoCol) { x += cw + gap; y = top; } else y += gap * 0.5f;

            // ---- AUTOPILOT ----
            Head("AUTOPILOT");
            {
                string fma = ap.Disc ? "AP DISCONNECT" : ap.Annunciation ?? "AP OFF";
                GUI.Label(new Rect(x, y, cw, lh), fma, ap.Disc || ap.On && ap.Status != "holding" ? _amber : _label); y += lh;
            }
            if (Btn(Cell(0, 4), "AP", ap.On)) { if (ap.On) ap.Off(); else ap.Engage(R100(rd.HeightFt), null, "alt"); }
            if (Btn(Cell(1, 4), "YD", ap.YawDamper)) ap.YawDamper = !ap.YawDamper;
            if (Btn(Cell(2, 4), "A/T", ap.AutoThrottle)) ap.AutoThrottle = !ap.AutoThrottle;
            if (Btn(Cell(3, 4), "ROL", ap.On && ap.RollMode == "rol")) ap.Engage(null, null, null, null, "rol");
            y += lh + gap * 0.5f;
            if (Btn(Cell(0, 4), "ALT", ap.On && (ap.PitchMode == "alt" || ap.PitchMode == "alt*"))) ap.Engage(R100(rd.HeightFt), null, "alt");
            if (Btn(Cell(1, 4), "VS", ap.On && ap.PitchMode == "vs"))
            {
                double vs = R100(-rd.SinkFpm); if (System.Math.Abs(vs) < 100) vs = ap.AltFt >= rd.HeightFt ? 500 : -500;
                ap.Engage(ap.AltFt, null, "vs", vs);
            }
            if (Btn(Cell(2, 4), "VS ▲", false)) { ap.VsFpm += 100; if (ap.On && ap.PitchMode == "alt*") ap.Engage(ap.AltFt, null, "vs", ap.VsFpm); }
            if (Btn(Cell(3, 4), "VS ▼", false)) { ap.VsFpm -= 100; if (ap.On && ap.PitchMode == "alt*") ap.Engage(ap.AltFt, null, "vs", ap.VsFpm); }
            y += lh + gap * 0.5f;
            if (Btn(Cell(0, 4), "FLC", ap.On && ap.PitchMode == "flc")) ap.Engage(ap.AltFt, ap.Kias, "flc");
            if (Btn(Cell(1, 4), "SPD −", false)) ap.Kias = System.Math.Max(30, ap.Kias - 5);
            GUI.Label(Cell(2, 4), $"  {ap.Kias:0} KT", _label);
            if (Btn(Cell(3, 4), "SPD +", false)) ap.Kias += 5;
            y += lh + gap * 0.5f;
            if (Btn(Cell(0, 4), "ALT −", false)) { ap.AltFt = System.Math.Max(0, ap.AltFt - 100); ap.HasAltTarget = true; }
            GUI.Label(Cell(1, 4), $"  SEL {ap.AltFt:0} FT", _label);
            if (Btn(Cell(2, 4), "ALT +", false)) { ap.AltFt += 100; ap.HasAltTarget = true; }
            if (Btn(Cell(3, 4), "HDG", ap.On && ap.RollMode == "hdg")) ap.Engage(null, null, null, null, "hdg", ap.HdgDeg);
            y += lh + gap * 0.5f;
            if (Btn(Cell(0, 4), "HDG −10", false)) ap.Heading(null, -10, false);
            GUI.Label(Cell(1, 4), $"  BUG {ap.HdgDeg:000}°", _label);
            if (Btn(Cell(2, 4), "HDG +10", false)) ap.Heading(null, 10, false);
            if (Btn(Cell(3, 4), "SYNC", false)) ap.Heading(null, null, true);
            y += lh + gap * 0.5f;
            for (int i = 0; i < 3; i++)
                if (UiLayout.Button(Cell(i, 4), WidgetAutopilot.FutureModes[i].ToUpperInvariant(), _btnDim)) { _note = $"{WidgetAutopilot.FutureModes[i].ToUpperInvariant()} is not available yet (a later build)"; _noteUntil = Time.unscaledTime + 3f; }
            if (Time.unscaledTime < _noteUntil) GUI.Label(Cell(3, 4), "  not yet", _amber);
            y += lh + gap * 0.5f;
            if (Time.unscaledTime < _noteUntil) { GUI.Label(new Rect(x, y, cw, lh), _note, _amber); }
            y += lh * 0.6f;

            // ---- WEIGHT & BALANCE ----
            var L = Host.Loading;
            Head("WEIGHT & BALANCE");
            {
                double lb = L.TargetKg * 2.20462;
                GUI.Label(new Rect(x, y, cw * 0.36f, lh), $"Weight {lb:#,0} lb", _label);
                float v = GUI.HorizontalSlider(new Rect(x + cw * 0.38f, y + lh * 0.35f, cw * 0.62f, lh * 0.4f), (float)(L.TargetKg / L.DefaultKg), 0.6f, 1.4f);
                if (Mathf.Abs(v - (float)(L.TargetKg / L.DefaultKg)) > 0.004f) { L.Set(System.Math.Round(v, 2) * L.DefaultKg, null, false); Host.LoadingTouched(); }
                y += lh + gap * 0.4f;
                GUI.Label(new Rect(x, y, cw * 0.36f, lh), $"CG {L.TargetCgMac:0.0} % MAC", _label);
                float c = GUI.HorizontalSlider(new Rect(x + cw * 0.38f, y + lh * 0.35f, cw * 0.62f, lh * 0.4f), (float)(L.TargetCgMac - L.DefaultCgMac), -15f, 25f);
                if (Mathf.Abs(c - (float)(L.TargetCgMac - L.DefaultCgMac)) > 0.2f) { L.Set(null, L.DefaultCgMac + Mathf.Round(c * 2f) / 2f, false); Host.LoadingTouched(); }
                y += lh + gap * 0.4f;
                string lim = L.WithinLimits ? $"WITHIN LIMITS · SM {L.StaticMarginMac:0.0} % · NP {L.NpMac:0} %" : $"OUT OF LIMITS{(L.Overweight ? " · OVERWEIGHT" : "")}{(L.CgOut != null ? $" · CG {L.CgOut.ToUpperInvariant()}" : "")}";
                GUI.Label(new Rect(x, y, cw * 0.72f, lh), lim, L.WithinLimits ? _label : _amber);
                if (Btn(new Rect(x + cw * 0.74f, y, cw * 0.26f, lh), "RESET", false)) L.Set(null, null, true);
                y += lh + gap * 0.4f;
            }
            if (GUI.Button(new Rect(panel.xMax - gap - cw * 0.3f, panel.yMax - gap - lh, cw * 0.3f, lh), "Close", _btnOn)) IsOpen = false;
        }
    }
}

using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// In-flight OPTIONS button (top right) opening a small panel of settings that are not immediate flight
    /// actions: lift/sink markers (Auto = gliders only / On / Off), the air bubbles, and the instrument style.
    /// The sim keeps running underneath; the touch controls stay live outside the panel.
    /// </summary>
    public sealed class OptionsPanel : MonoBehaviour
    {
        public FlightSimDriver Driver;
        private bool _open;
        private GUIStyle _btn, _btnOn, _head, _label;
        private Texture2D _bg, _btnBg, _btnOnBg;
        private int _fs;

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.92f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(6, 6, 4, 4) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.05f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.55f, 0.95f, 1f) } };
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.88f, 0.92f) } };
        }

        private void OnGUI()
        {
            if (SessionSettings.MenuOpen) { _open = false; return; }
            EnsureStyles();
            float s = Mathf.Min(Screen.width, Screen.height);
            float mbw = s * 0.13f, mbh = s * 0.055f;
            // OPTIONS button top right, mirroring the MENU button top left.
            if (!_open)
            {
                if (GUI.Button(new Rect(Screen.width - s * 0.02f - mbw, s * 0.02f, mbw, mbh), "OPTIONS", _btn)) _open = true;
                return;
            }
            float w = Mathf.Min(Screen.width * 0.9f, s * 0.9f), lh = _fs * 1.8f, gap = _fs * 0.5f;
            float h = lh * 9.5f;
            var panel = new Rect((Screen.width - w) * 0.5f, s * 0.02f + mbh + gap, w, h);
            GUI.DrawTexture(panel, _bg);
            float x = panel.x + gap, y = panel.y + gap, cw = panel.width - 2 * gap;
            GUI.Label(new Rect(x, y, cw, lh), "OPTIONS", _head); y += lh;

            void TriRow(string name, ref SessionSettings.Tri v)
            {
                GUI.Label(new Rect(x, y, cw * 0.38f, lh), name, _label);
                float bw = (cw * 0.6f - 2 * gap * 0.4f) / 3f, bx = x + cw * 0.4f;
                var opts = new[] { (SessionSettings.Tri.Auto, "Auto"), (SessionSettings.Tri.On, "On"), (SessionSettings.Tri.Off, "Off") };
                for (int i = 0; i < 3; i++)
                    if (GUI.Button(new Rect(bx + i * (bw + gap * 0.4f), y, bw, lh), opts[i].Item2, v == opts[i].Item1 ? _btnOn : _btn)) v = opts[i].Item1;
                y += lh + gap * 0.4f;
            }
            void BoolRow(string name, ref bool v)
            {
                GUI.Label(new Rect(x, y, cw * 0.38f, lh), name, _label);
                float bw = (cw * 0.6f - gap * 0.4f) / 2f, bx = x + cw * 0.4f;
                if (GUI.Button(new Rect(bx, y, bw, lh), "On", v ? _btnOn : _btn)) v = true;
                if (GUI.Button(new Rect(bx + bw + gap * 0.4f, y, bw, lh), "Off", !v ? _btnOn : _btn)) v = false;
                y += lh + gap * 0.4f;
            }
            TriRow("Lift / sink markers", ref SessionSettings.LiftMarkers);
            GUI.Label(new Rect(x + cw * 0.4f, y, cw * 0.6f, lh * 0.8f), "Auto = gliders only", _label); y += lh * 0.8f;
            BoolRow("Air bubbles", ref SessionSettings.BubblesOn);
            {
                GUI.Label(new Rect(x, y, cw * 0.38f, lh), "Instruments", _label);
                float bw = (cw * 0.6f - 2 * gap * 0.4f) / 3f, bx = x + cw * 0.4f;
                var modes = new[] { (SessionSettings.InstrumentMode.Analog, "Dials"), (SessionSettings.InstrumentMode.Hud, "HUD"), (SessionSettings.InstrumentMode.None, "None") };
                for (int i = 0; i < 3; i++)
                    if (GUI.Button(new Rect(bx + i * (bw + gap * 0.4f), y, bw, lh), modes[i].Item2, SessionSettings.Instruments == modes[i].Item1 ? _btnOn : _btn)) SessionSettings.Instruments = modes[i].Item1;
                y += lh + gap * 0.4f;
            }
            {
                GUI.Label(new Rect(x, y, cw * 0.38f, lh), "Sound volume", _label);
                float vol = GUI.HorizontalSlider(new Rect(x + cw * 0.4f, y + lh * 0.35f, cw * 0.6f, lh * 0.4f), FlightAudio.MasterVolume, 0f, 1f);
                FlightAudio.MasterVolume = vol;
                y += lh + gap * 0.4f;
            }
            y += gap;
            if (GUI.Button(new Rect(panel.xMax - gap - cw * 0.3f, y, cw * 0.3f, lh), "Close", _btnOn)) _open = false;
        }
    }
}

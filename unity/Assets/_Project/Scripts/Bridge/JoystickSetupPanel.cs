using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The game's JOYSTICK SETUP window (Mac; OPTIONS › Joystick setup): the guided calibration step by step — the
    /// instruction, what it's detecting, a progress bar, the raw axes (the one it's looking at lit), BACK / SKIP / CANCEL,
    /// FINISH at the end. The world is paused while it runs; Escape cancels.
    /// </summary>
    public sealed class JoystickSetupPanel : MonoBehaviour
    {
        private static JoystickSetupPanel _inst;
        private readonly JoystickWizard _wiz = new();
        private GUIStyle _head, _text, _small, _btn;
        private Texture2D _bg, _bar, _barOn, _barBack;
        private float _savedScale = 1f;

        public static void Open()
        {
            if (_inst == null) _inst = new GameObject("JoystickSetupPanel").AddComponent<JoystickSetupPanel>();
            _inst.Begin();
        }
        private void Begin()
        {
            if (!_wiz.Start()) return;
            _savedScale = Time.timeScale > 0f ? Time.timeScale : SessionSettings.SimSpeed;
            Time.timeScale = 0f;   // nothing flies while the stick is being learnt
        }
        private void End()
        {
            if (!SessionSettings.MenuOpen && !SessionSettings.ReplayActive) Time.timeScale = _savedScale > 0f ? _savedScale : SessionSettings.SimSpeed;
        }

        private void Update()
        {
            if (!_wiz.Active) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { _wiz.Cancel(); End(); return; }
            _wiz.Tick(Time.unscaledDeltaTime);
        }

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
        private void Styles(int fs)
        {
            if (_head != null && _head.fontSize == Mathf.RoundToInt(fs * 1.3f)) return;
            _bg = Solid(new Color(0.05f, 0.07f, 0.1f, 0.94f)); _bar = Solid(new Color(1, 1, 1, 0.35f)); _barOn = Solid(new Color(0.3f, 0.95f, 0.45f, 1f)); _barBack = Solid(new Color(1, 1, 1, 0.1f));
            _head = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 1.3f), fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            _text = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 1.15f), fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = Color.white } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = fs, normal = { textColor = new Color(1, 1, 1, 0.75f) }, wordWrap = true };
            _btn = new GUIStyle(GUI.skin.button) { fontSize = fs, fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            if (!_wiz.Active) return;
            float s = Mathf.Min(Screen.width, Screen.height);
            int fs = Mathf.RoundToInt(Mathf.Clamp(s * 0.022f, 12f, 30f));
            Styles(fs);
            GUI.depth = -100;
            float w = Mathf.Min(Screen.width * 0.9f, s * 1.1f), h = s * 0.62f;
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.DrawTexture(r, _bg);
            float pad = fs, x = r.x + pad, y = r.y + pad, cw = w - 2 * pad, lh = fs * 1.6f;
            GUI.Label(new Rect(x, y, cw, lh), $"JOYSTICK SETUP — {_wiz.DeviceName}   ({_wiz.StepNumber} / {_wiz.StepCount})", _head); y += lh * 1.2f;
            GUI.Label(new Rect(x, y, cw, lh * 2.2f), _wiz.Instruction, _text); y += lh * 2.3f;
            GUI.Label(new Rect(x, y, cw, lh), _wiz.Status, _small); y += lh;
            GUI.DrawTexture(new Rect(x, y, cw, fs * 0.5f), _barBack);
            GUI.DrawTexture(new Rect(x, y, cw * _wiz.Progress, fs * 0.5f), _barOn); y += fs * 1.2f;
            // The raw axes: a bar per axis, the one being looked at lit.
            float mw = (cw - 7 * fs * 0.4f) / 8f, mh = s * 0.12f;
            for (int a = 1; a <= JoystickInput.AxisCount; a++)
            {
                float mx = x + (a - 1) * (mw + fs * 0.4f), v = JoystickInput.Raw(a);
                GUI.DrawTexture(new Rect(mx, y, mw, mh), _barBack);
                float fh = mh * 0.5f * Mathf.Abs(v);
                GUI.DrawTexture(new Rect(mx, v >= 0 ? y + mh * 0.5f - fh : y + mh * 0.5f, mw, fh), a == _wiz.Focus ? _barOn : _bar);
                GUI.Label(new Rect(mx, y + mh, mw, lh), $"AXIS {a}", _small);
            }
            y += mh + lh * 1.1f;
            GUI.Label(new Rect(x, y, cw, lh * 1.5f), _wiz.Summary(), _small);
            // BACK · SKIP · CANCEL (FINISH at the end).
            float bw = cw * 0.22f, bh = lh * 1.3f, by = r.yMax - pad - bh;
            if (GUI.Button(new Rect(x, by, bw, bh), "BACK", _btn)) _wiz.Back();
            bool done = _wiz.StepNumber == _wiz.StepCount;
            if (GUI.Button(new Rect(x + bw + pad, by, bw, bh), done ? "FINISH" : "SKIP", _btn)) { if (done) { _wiz.Finish(); End(); } else _wiz.Skip(); }
            if (GUI.Button(new Rect(r.xMax - pad - bw, by, bw, bh), "CANCEL", _btn)) { _wiz.Cancel(); End(); }
        }
    }
}

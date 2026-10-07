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
        public bool IsOpen => _open;
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

        private void ApplyFeelLive()
        {
            var cfg = Driver?.Sim?.Aircraft?.Config;
            if (cfg == null) return;
            // Re-load the type's own values first so switching a setting back to "aircraft" restores them.
            try
            {
                var fresh = UnityAircraftConfigLoader.LoadFromStreamingAssets(cfg.Id);
                cfg.Controls.Aileron.Expo = fresh.Controls.Aileron.Expo; cfg.Controls.Aileron.DeadZone = fresh.Controls.Aileron.DeadZone;
                cfg.Controls.Elevator.Expo = fresh.Controls.Elevator.Expo; cfg.Controls.Elevator.DeadZone = fresh.Controls.Elevator.DeadZone;
                cfg.Controls.Rudder.Expo = fresh.Controls.Rudder.Expo; cfg.Controls.Rudder.DeadZone = fresh.Controls.Rudder.DeadZone;
            }
            catch (System.Exception) { }
            SessionSettings.ApplyFeel(cfg);
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
                if (UiLayout.Button(UiLayout.OptionsRect, "OPTIONS", _btn)) _open = true;   // the shared toolbar row
                return;
            }
            // Landscape has the width but not the height for one long list: two columns (settings | control feel).
            bool twoCol = Screen.width > Screen.height * 1.3f;
            float w = twoCol ? Mathf.Min(Screen.width * 0.94f, s * 1.9f) : Mathf.Min(Screen.width * 0.9f, s * 0.9f), lh = _fs * 1.8f, gap = _fs * 0.5f;
            float h = lh * (twoCol ? 17.5f : 27.5f);
            var panel = new Rect((Screen.width - w) * 0.5f, s * 0.02f + mbh + gap, w, h);
            GUI.DrawTexture(panel, _bg);
            float x = panel.x + gap, y = panel.y + gap, cw = twoCol ? (panel.width - 3 * gap) * 0.5f : panel.width - 2 * gap;
            float colTop = y + lh;
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
            TriRow("Show lift", ref SessionSettings.LiftMarkers);
            GUI.Label(new Rect(x + cw * 0.4f, y, cw * 0.6f, lh * 0.8f), "Auto = gliders only", _label); y += lh * 0.8f;
            BoolRow("Air bubbles", ref SessionSettings.BubblesOn);
            BoolRow("Dense bubbles (GPU instancing)", ref SessionSettings.BubbleInstancing);
            BoolRow("Bubble streaks (longer with speed)", ref SessionSettings.BubbleStreaks);
            BoolRow("Flight path vector (3 s, air-relative)", ref SessionSettings.ShowFlightPath);
            BoolRow("Real airframe models (rebuild: switch aircraft)", ref SessionSettings.UseAirframeModels);
            BoolRow("Force vectors", ref SessionSettings.ShowForceVectors);
            BoolRow("Flight test data", ref SessionSettings.FlightTestData);
            GUI.Label(new Rect(x + cw * 0.4f, y, cw * 0.6f, lh * 0.8f), "lift green · drag red · moment yellow · thrust magenta · wheels cyan", _label); y += lh * 0.8f;
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
            // Quick clips (owner 2026-10-01): how much the CLIP button saves, and whether the instruments are in the picture.
            y += gap * 0.5f;
            GUI.Label(new Rect(x, y, cw, lh), "QUICK CLIPS", _head); y += lh;
            {
                int sec = ClipRecorder.ClipSeconds;
                GUI.Label(new Rect(x, y, cw * 0.38f, lh), $"Clip length  {ClipRecorder.Length(sec)}", _label);
                float v = GUI.HorizontalSlider(new Rect(x + cw * 0.4f, y + lh * 0.35f, cw * 0.6f, lh * 0.4f), sec, ClipRecorder.MinSeconds, ClipRecorder.MaxSeconds);
                if (Mathf.Abs(v - sec) >= ClipRecorder.StepSeconds * 0.5f) ClipRecorder.ClipSeconds = Mathf.RoundToInt(v);
                y += lh + gap * 0.4f;
            }
            {
                bool inc = ClipRecorder.IncludeInstruments;
                var box = new Rect(x, y, lh, lh);
                if (GUI.Button(box, inc ? "X" : "", inc ? _btnOn : _btn)) ClipRecorder.IncludeInstruments = !inc;
                if (GUI.Button(new Rect(x + lh + gap, y, cw - lh - gap, lh), "Include instruments in clips (dials or HUD) - applies from when you tick it", _label)) ClipRecorder.IncludeInstruments = !inc;
                y += lh + gap * 0.4f;
            }
            if (twoCol) { x += cw + gap; y = colTop; }
            // Control feel (owner 2026-09-15): per-axis expo and dead zone, tuned by the user. Applied live to the current
            // aircraft and to every aircraft loaded from now on; "aircraft" = that type's own value.
            y += gap * 0.5f;
            SessionSettings.LoadFeel();
            GUI.Label(new Rect(x, y, cw, lh), "CONTROL FEEL", _head);
            if (GUI.Button(new Rect(panel.xMax - gap - cw * 0.3f, y, cw * 0.3f, lh * 0.9f), "Aircraft defaults", _btn))
            {
                for (int i = 0; i < 3; i++) { SessionSettings.FeelExpo[i] = -1f; SessionSettings.FeelDeadZone[i] = -1f; }
                SessionSettings.SaveFeel(); ApplyFeelLive();
            }
            y += lh;
            for (int i = 0; i < 3; i++)
            {
                var axis = Driver?.Sim?.Aircraft?.Config?.Controls == null ? null : new[] { Driver.Sim.Aircraft.Config.Controls.Aileron, Driver.Sim.Aircraft.Config.Controls.Elevator, Driver.Sim.Aircraft.Config.Controls.Rudder }[i];
                float expoNow = SessionSettings.FeelExpo[i] >= 0f ? SessionSettings.FeelExpo[i] : (float)(axis?.Expo ?? 0.3);
                float dzNow = SessionSettings.FeelDeadZone[i] >= 0f ? SessionSettings.FeelDeadZone[i] : (float)(axis?.DeadZone ?? 0.05);
                GUI.Label(new Rect(x, y, cw * 0.38f, lh), $"{SessionSettings.FeelAxisNames[i]} expo  {expoNow * 100f:F0} %{(SessionSettings.FeelExpo[i] < 0f ? " (aircraft)" : "")}", _label);
                float e = GUI.HorizontalSlider(new Rect(x + cw * 0.4f, y + lh * 0.35f, cw * 0.6f, lh * 0.4f), expoNow, 0f, 1f);
                if (Mathf.Abs(e - expoNow) > 0.004f) { SessionSettings.FeelExpo[i] = Mathf.Round(e * 20f) / 20f; SessionSettings.SaveFeel(); ApplyFeelLive(); }
                y += lh * 0.95f;
                GUI.Label(new Rect(x, y, cw * 0.38f, lh), $"{SessionSettings.FeelAxisNames[i]} dead zone  {dzNow * 100f:F0} %{(SessionSettings.FeelDeadZone[i] < 0f ? " (aircraft)" : "")}", _label);
                float d = GUI.HorizontalSlider(new Rect(x + cw * 0.4f, y + lh * 0.35f, cw * 0.6f, lh * 0.4f), dzNow, 0f, 0.3f);
                if (Mathf.Abs(d - dzNow) > 0.002f) { SessionSettings.FeelDeadZone[i] = Mathf.Round(d * 100f) / 100f; SessionSettings.SaveFeel(); ApplyFeelLive(); }
                y += lh * 0.95f;
            }
            // Intercom + radio (owner 2026-10-01): intercom = voice-activated, heard in headphones; radio = hold TALK.
            y += gap * 0.5f;
            GUI.Label(new Rect(x, y, cw, lh), "INTERCOM & RADIO", _head); y += lh;
            {
                bool ic = VoiceComms.IntercomOn;
                if (GUI.Button(new Rect(x, y, lh, lh), ic ? "X" : "", ic ? _btnOn : _btn)) VoiceComms.IntercomOn = !ic;
                if (GUI.Button(new Rect(x + lh + gap, y, cw - lh - gap, lh), "Intercom: talk and hear yourself (headphones)", _label)) VoiceComms.IntercomOn = !ic;
                y += lh + gap * 0.4f;
                GUI.Label(new Rect(x, y, cw * 0.38f, lh), "Radio frequency", _label);
                int nf = VoiceComms.Frequencies.Length;
                float bw = (cw * 0.6f - (nf - 1) * gap * 0.4f) / nf, bx = x + cw * 0.4f;
                for (int i = 0; i < nf; i++)
                {
                    string f = VoiceComms.Frequencies[i];
                    if (GUI.Button(new Rect(bx + i * (bw + gap * 0.4f), y, bw, lh), f, VoiceComms.Frequency == f ? _btnOn : _btn)) VoiceComms.Frequency = f;
                }
                y += lh + gap * 0.4f;
            }
            y += gap;
            if (GUI.Button(new Rect(panel.xMax - gap - cw * 0.3f, y, cw * 0.3f, lh), "Close", _btnOn)) _open = false;
        }
    }
}

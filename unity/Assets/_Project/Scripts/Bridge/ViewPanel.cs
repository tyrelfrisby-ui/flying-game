using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Camera / replay / clips controls (owner 2026-10-01).
    ///   In flight: a column under OPTIONS (top right) — VIEW (cycles the camera views), REPLAY, CLIP (the last
    ///   15 s – 3 min to Photos, length in OPTIONS), REC (everything until the next tap). Clips never show these buttons
    ///   (<see cref="ClipRecorder"/> films a separate camera). It steps sideways out of the aircraft's on-screen box.
    ///   In a replay: a bar in the control tray (portrait) or along the bottom (landscape) — scrub track, −10 s,
    ///   play/pause, +10 s, speed, VIEW, CLIP, REC, EXIT (back to the live flight where it was paused).
    /// </summary>
    public sealed class ViewPanel : MonoBehaviour
    {
        public ChaseCamera Chase;
        public FlightReplay Replay;
        public OptionsPanel Options;

        private GUIStyle _btn, _btnOn, _btnRec, _label, _small;
        private Texture2D _bg, _btnBg, _btnOnBg, _recBg, _track, _fill, _knob;
        private int _fs;
        private bool _scrubbing;
        private string _toast;
        private float _toastUntil;

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.92f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.9f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            _recBg ??= Solid(new Color(0.8f, 0.12f, 0.12f, 0.95f));
            _track ??= Solid(new Color(0.3f, 0.34f, 0.4f, 1f));
            _fill ??= Solid(new Color(0.25f, 0.75f, 1f, 1f));
            _knob ??= Solid(Color.white);
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(4, 4, 2, 2) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _btnRec = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _recBg }, active = { textColor = Color.white, background = _recBg } };
            _label = new GUIStyle { font = f, fontSize = fs, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _small = new GUIStyle(_label) { fontStyle = FontStyle.Normal, fontSize = Mathf.RoundToInt(fs * 0.85f), normal = { textColor = new Color(0.85f, 0.9f, 0.95f) } };
        }

        private void Update()
        {
            string m = ClipRecorder.PollMessage();
            if (!string.IsNullOrEmpty(m)) { _toast = m; _toastUntil = Time.realtimeSinceStartup + 3.5f; Debug.Log("[Clips] " + m); }
        }

        private static string Clock(double sec)
        {
            sec = System.Math.Max(0, sec);
            int m = (int)(sec / 60); double s = sec - m * 60;
            return $"{m}:{s:00.0}";
        }

        private void OnGUI()
        {
            if (SessionSettings.MenuOpen || Chase == null) return;
            EnsureStyles();
            if (Replay != null && Replay.Active) DrawReplayBar();
            else if (Options == null || !Options.IsOpen) DrawLiveColumn();
            DrawToast();
        }

        private void DrawToast()
        {
            if (string.IsNullOrEmpty(_toast) || Time.realtimeSinceStartup > _toastUntil) return;
            float s = Mathf.Min(Screen.width, Screen.height);
            var r = new Rect(Screen.width * 0.5f - s * 0.35f, s * 0.11f, s * 0.7f, _fs * 1.9f);
            GUI.DrawTexture(r, _bg);
            GUI.Label(r, _toast, _label);
        }

        private void DrawLiveColumn()
        {
            float s = Mathf.Min(Screen.width, Screen.height);
            float gap = s * 0.012f, bw = s * 0.2f, bh = s * 0.06f, vh = s * 0.085f;
            float x = Screen.width - s * 0.02f - bw;
            float y = s * 0.02f + s * 0.055f + gap;   // under the OPTIONS button
            float colH = vh + 3 * (bh + gap);
            // Owner's rule: nothing covers the aircraft — step the column sideways out of its screen box.
            if (ScreenLayout.HasAircraftKeepOut)
            {
                Rect ko = ScreenLayout.AircraftKeepOut;   // bottom-left origin
                Rect koGui = Rect.MinMaxRect(ko.xMin, Screen.height - ko.yMax, ko.xMax, Screen.height - ko.yMin);
                if (new Rect(x, y, bw, colH).Overlaps(koGui))
                    x = koGui.xMax + gap + bw <= Screen.width ? koGui.xMax + gap : Mathf.Max(0f, koGui.xMin - gap - bw);
            }
            bool other = Chase.CurrentView != ChaseCamera.View.RelativeWind;
            if (GUI.Button(new Rect(x, y, bw, vh), "VIEW\n" + ChaseCamera.ViewNames[(int)Chase.CurrentView], other ? _btnOn : _btn)) Chase.NextView();
            y += vh + gap;
            if (Replay != null && GUI.Button(new Rect(x, y, bw, bh), "REPLAY", _btn)) Replay.Enter();
            y += bh + gap;
            if (GUI.Button(new Rect(x, y, bw, bh), ClipRecorder.Busy ? "SAVING..." : "CLIP " + ClipRecorder.Length(ClipRecorder.ClipSeconds), _btn)) ClipRecorder.SaveClip();
            y += bh + gap;
            bool rec = ClipRecorder.Recording;
            if (GUI.Button(new Rect(x, y, bw, bh), rec ? "STOP REC" : "REC", rec ? _btnRec : _btn)) ClipRecorder.ToggleRecording();
        }

        private void DrawReplayBar()
        {
            float s = Mathf.Min(Screen.width, Screen.height);
            bool portrait = ScreenLayout.Portrait;
            float gap = s * 0.015f;
            int perRow = portrait ? 4 : 8;
            int rows = portrait ? 2 : 1;
            float bh = s * (portrait ? 0.085f : 0.07f);
            float trackH = s * 0.05f, labelH = _fs * 1.6f;
            float barH = portrait ? ScreenLayout.TrayHeightPx : labelH + trackH + rows * (bh + gap) + 3 * gap;
            var bar = new Rect(0f, Screen.height - barH, Screen.width, barH);
            GUI.DrawTexture(bar, _bg);   // portrait: also covers the tray the pads normally paint

            var rec = Replay.Recorder;
            double start = rec.StartTime, end = rec.EndTime;
            float x0 = gap * 2f, w = Screen.width - gap * 4f;
            float y = bar.y + gap;
            GUI.Label(new Rect(x0, y, w, labelH),
                $"REPLAY   {Clock(Replay.Head - start)} / {Clock(end - start)}    ({Clock(end - Replay.Head)} before you paused)   ×{Replay.Speed:0.##}", _small);
            y += labelH + gap * 0.5f;

            // Scrub track: tap or drag anywhere on it.
            var track = new Rect(x0, y + trackH * 0.35f, w, trackH * 0.3f);
            var hit = new Rect(x0 - gap, y - gap, w + 2 * gap, trackH + 2 * gap);
            float frac = end > start ? (float)((Replay.Head - start) / (end - start)) : 1f;
            GUI.DrawTexture(track, _track);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * frac, track.height), _fill);
            GUI.DrawTexture(new Rect(track.x + track.width * frac - trackH * 0.2f, y, trackH * 0.4f, trackH), _knob);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && hit.Contains(e.mousePosition)) _scrubbing = true;
            if (_scrubbing && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                float u = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                Replay.Seek(start + u * (end - start));
                e.Use();
            }
            if (e.type == EventType.MouseUp) _scrubbing = false;
            y += trackH + gap;

            float bw = (w - (perRow - 1) * gap) / perRow;
            int i = 0;
            Rect Next() { int r = i / perRow, c = i % perRow; i++; return new Rect(x0 + c * (bw + gap), y + r * (bh + gap), bw, bh); }
            if (GUI.Button(Next(), "-10 s", _btn)) Replay.Seek(Replay.Head - 10.0);
            if (GUI.Button(Next(), Replay.Playing ? "PAUSE" : "PLAY", _btnOn)) Replay.TogglePlay();
            if (GUI.Button(Next(), "+10 s", _btn)) Replay.Seek(Replay.Head + 10.0);
            if (GUI.Button(Next(), $"SPEED ×{Replay.Speed:0.##}", _btn)) Replay.CycleSpeed();
            if (GUI.Button(Next(), "VIEW\n" + ChaseCamera.ViewNames[(int)Chase.CurrentView], _btn)) Chase.NextView();
            if (GUI.Button(Next(), ClipRecorder.Busy ? "SAVING..." : "CLIP " + ClipRecorder.Length(ClipRecorder.ClipSeconds), _btn)) ClipRecorder.SaveClip();
            bool recOn = ClipRecorder.Recording;
            if (GUI.Button(Next(), recOn ? "STOP REC" : "REC", recOn ? _btnRec : _btn)) ClipRecorder.ToggleRecording();
            if (GUI.Button(Next(), "EXIT\nREPLAY", _btnOn)) Replay.Exit();
        }
    }
}

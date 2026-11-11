using UnityEngine;
using FlyingGame.Sim.Replay;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE DEBRIEF, on screen (Ty 2026-11-10). He chose the debrief as the first of the three pieces of the
    /// instructor idea, against this reasoning, which he accepted: "The replay already exists and is good. Turning it
    /// into a debrief — what failed, when, what you did, how long you took to notice — is the smallest step that is
    /// useful alone, and it is useful even solo."
    ///
    /// THIS CLASS COMPOSES NO SENTENCES AND COMPUTES NO NUMBERS. Every word on the screen is a string
    /// <see cref="FlightDebrief"/> built in the shared assembly, where the suite can read it; this draws the list,
    /// handles the taps and cues the replay. That split is the parity requirement, not a preference: the Quest's
    /// menu and the Mac build can render the same <see cref="Debrief"/> without any of this.
    ///
    /// WHERE IT LIVES, AND HOW MANY TAPS. His standing complaint is menus — "we need a shortcut button so I don't
    /// have to [go] several menus deep" — and the live toolbar is already seven buttons wide, so a DEBRIEF button up
    /// there would cost a slot and a quarter of nothing. It lives on the REPLAY BAR instead, where the flight has
    /// just ended up anyway:
    ///   REPLAY (1 tap, live toolbar) → DEBRIEF (1 tap) = reading it;
    ///   → tap any line (1 tap) = watching that moment, from four seconds before it.
    /// Tapping a line closes this panel on purpose: the point of the tap is to WATCH, and a sheet of text over the
    /// aeroplane is not watching.
    ///
    /// PHONE, PORTRAIT, FRUGAL. A four-dial instrument row was deleted on 2026-11-10 for stealing a quarter of the
    /// cockpit view, so nothing here is ever on screen during flight: it is open or it is gone. While it is open it
    /// takes the whole screen, because it is text to read and the world is frozen behind it.
    /// </summary>
    public sealed class DebriefPanel : MonoBehaviour
    {
        public FlightReplay Replay;
        public FlightSimDriver Driver;

        public static bool IsOpen { get; private set; }
        public static void Close() { IsOpen = false; }

        /// <summary>The DEBRIEF button on the replay bar. Opening it builds the debrief once, there and then.</summary>
        public void Toggle()
        {
            IsOpen = !IsOpen;
            if (IsOpen) _built = null; else _scroll = Vector2.zero;
        }

        private Debrief _built;
        private Vector2 _scroll;
        private GUIStyle _btn, _head, _sub, _talk, _title, _body, _time;
        private Texture2D _bg, _card, _tone0, _tone1, _tone2, _talkBg;
        private float _fs = -1f;
        private readonly System.Collections.Generic.List<Rect> _rows = new();

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void EnsureStyles()
        {
            float fs = Mathf.Round(UiLayout.PanelUnit * 0.028f);
            if (_body != null && Mathf.Approximately(fs, _fs)) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.02f, 0.03f, 0.05f, 0.97f));
            _card ??= Solid(new Color(0.07f, 0.10f, 0.14f, 0.95f));
            _talkBg ??= Solid(new Color(0.10f, 0.16f, 0.12f, 0.95f));
            // The same three steps the lesson debrief's caption uses, one short of its four: a debrief has no "red",
            // because nothing here is a score.
            _tone0 ??= Solid(new Color(0.55f, 0.62f, 0.70f));
            _tone1 ??= Solid(new Color(1f, 0.78f, 0.25f));
            _tone2 ??= Solid(new Color(0.95f, 0.35f, 0.22f));
            Font f = UiFont.Get();
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.5f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _sub = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.9f), alignment = TextAnchor.MiddleLeft, wordWrap = true, normal = { textColor = new Color(0.72f, 0.78f, 0.86f) } };
            _talk = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.05f), fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = new Color(0.85f, 0.95f, 0.88f) } };
            _title = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.95f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _time = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.85f), alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.60f, 0.70f, 0.80f) } };
            _body = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.92f), alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = new Color(0.88f, 0.91f, 0.95f) } };
            _btn = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _card }, active = { textColor = Color.white, background = _card } };
        }

        /// <summary>
        /// Build it once, when the panel opens. The replay freezes the world, so the recording cannot grow while the
        /// pilot is reading, and rebuilding every frame would pay for the best-glide sweep sixty times a second.
        /// </summary>
        private Debrief Current()
        {
            if (_built != null) return _built;
            if (Replay == null) return _built = new Debrief { Headline = "DEBRIEF" };
            FlyingGame.Sim.Aircraft a = Driver != null && Driver.Sim != null ? Driver.Sim.Aircraft : null;
            // No aeroplane to read limits off (it is being swapped): build it anyway from the recording. A limits
            // object left at its defaults simply deletes the lines that would have compared against one, which is the
            // same rule DebriefLimits states — a zero is "not known", never a guess.
            if (a == null) return _built = FlightDebrief.Build(Replay.Recorder, Replay.Events, new DebriefLimits());
            // The best-glide speed is only worth a trim sweep when the flight actually lost its engine
            // (ApproachSpawn.FindBestGlide caches per type and per 100 m, so a second debrief is free).
            double bestGlideIas = 0.0;
            if (NeedsGlideSpeed(Replay.Events))
            {
                double alt = -a.State.Position.Z;
                double tas = FlyingGame.Sim.ApproachSpawn.FindBestGlide(a.Config, alt).SpeedMs;
                bestGlideIas = FlightReading.IasFromTas(tas, alt);
            }
            // Heights above the GROUND when there is a world to ask, above sea level when there is not. Written as a
            // statement and not a ternary on purpose: a method group in a conditional has no natural type in the
            // language version the Bridge is compiled at, and a line that the headless suite cannot compile is
            // exactly how a green suite turns into a broken iOS build.
            System.Func<double, double, double> ground = null;
            if (FlyingGame.Core.WorldTerrain.Active != null) ground = FlyingGame.Core.WorldTerrain.GroundHeightAt;
            return _built = FlightDebrief.Build(Replay.Recorder, Replay.Events,
                DebriefLimits.For(a, bestGlideIas, ground));
        }

        private static bool NeedsGlideSpeed(FlightEventLog log)
        {
            for (int i = 0; i < log.Count; i++)
            {
                FlightEventKind k = log.At(i).Kind;
                if (k == FlightEventKind.EngineStopped || k == FlightEventKind.InjectedFailure) return true;
            }
            return false;
        }

        /// <summary>Watch the moment a line is about: the play head four seconds before it, playing, panel gone.</summary>
        private void Watch(in DebriefLine line)
        {
            if (Replay == null || !line.CanCue) return;
            if (!Replay.Active) Replay.Enter();
            if (!Replay.Active) return;
            Replay.Seek(line.CueT);
            Replay.Playing = true;
            Close();
        }

        private void OnGUI()
        {
            if (!IsOpen || SessionSettings.MenuOpen) return;
            if (Replay == null || !Replay.Active) { Close(); return; }   // it belongs to the replay; it goes with it
            EnsureStyles();
            Debrief d = Current();

            float s = Mathf.Min(Screen.width, Screen.height);
            float m = s * 0.03f, gap = s * 0.012f;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bg);

            float x = UiLayout.SafeLeft + m, w = Screen.width - UiLayout.SafeLeft - UiLayout.SafeRight - 2 * m;
            float y = UiLayout.SafeTop + m;
            float bw = Mathf.Min(s * 0.26f, w * 0.4f), bh = _fs * 2.1f;
            if (UiLayout.Button(new Rect(x + w - bw, y, bw, bh), "CLOSE", _btn)) { Close(); return; }

            GUI.Label(new Rect(x, y, w - bw - gap, bh), "DEBRIEF", _head);
            y += bh + gap * 0.5f;
            GUI.Label(new Rect(x, y, w, _fs * 1.4f), d.Headline + (d.Contents.Length > 0 ? "   ·   " + d.Contents : ""), _sub);
            y += _fs * 1.4f + gap;

            if (d.Talk.Length > 0)
            {
                string talk = "WORTH TALKING ABOUT\n" + d.Talk;
                float th = _talk.CalcHeight(new GUIContent(talk), w - 2 * gap) + gap;
                var tr = new Rect(x, y, w, th);
                GUI.DrawTexture(tr, _talkBg);
                GUI.DrawTexture(new Rect(tr.x, tr.y, s * 0.008f, tr.height), _tone1);
                if (GUI.Button(tr, GUIContent.none, GUIStyle.none) && d.TalkLine >= 0 && d.TalkLine < d.Lines.Count)
                { Watch(d.Lines[d.TalkLine]); return; }
                GUI.Label(new Rect(tr.x + gap * 1.5f, tr.y + gap * 0.4f, tr.width - gap * 2.5f, tr.height), talk, _talk);
                y += th + gap;
            }

            // The list. Measured first so the scroll view knows how tall its content is, then drawn: a phone in
            // portrait fits three or four of these, and a flight that broke things needs all of them reachable.
            float viewTop = y, viewH = Screen.height - UiLayout.SafeBottom - m - viewTop;
            float contentW = w - s * 0.05f;
            _rows.Clear();
            float ch = 0f;
            foreach (DebriefLine l in d.Lines)
            {
                float h = _fs * 1.5f + _body.CalcHeight(new GUIContent(l.Text), contentW - gap * 3f) + gap;
                _rows.Add(new Rect(0, ch, contentW, h));
                ch += h + gap * 0.7f;
            }

            _scroll = GUI.BeginScrollView(new Rect(x, viewTop, w, viewH), _scroll, new Rect(0, 0, contentW, ch));
            for (int i = 0; i < d.Lines.Count; i++)
            {
                DebriefLine l = d.Lines[i];
                Rect r = _rows[i];
                GUI.DrawTexture(r, _card);
                GUI.DrawTexture(new Rect(r.x, r.y, s * 0.008f, r.height),
                    l.Tone == DebriefTone.Serious ? _tone2 : l.Tone == DebriefTone.Caution ? _tone1 : _tone0);
                // Only a line that can still be watched is a button: offering to show a moment the ring has dropped
                // and then doing nothing is worse than not offering (FlightDebrief: CanCue).
                if (l.CanCue && GUI.Button(r, GUIContent.none, GUIStyle.none)) { GUI.EndScrollView(); Watch(l); return; }
                float tx = r.x + gap * 1.5f, tw = r.width - gap * 3f;
                GUI.Label(new Rect(tx, r.y + gap * 0.3f, tw * 0.72f, _fs * 1.4f), l.Title, _title);
                GUI.Label(new Rect(tx + tw * 0.72f, r.y + gap * 0.3f, tw * 0.28f, _fs * 1.4f),
                    l.Timed ? (l.CanCue ? "WATCH  " + FlightReading.Clock(l.T) : FlightReading.Clock(l.T)) : "", _time);
                GUI.Label(new Rect(tx, r.y + _fs * 1.5f, tw, r.height - _fs * 1.5f), l.Text, _body);
            }
            GUI.EndScrollView();
        }
    }
}

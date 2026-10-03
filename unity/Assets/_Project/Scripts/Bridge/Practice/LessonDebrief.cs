using System.Collections;
using System.Collections.Generic;
using FlyingGame.Sim.Practice;
using UnityEngine;

namespace FlyingGame.Bridge.Practice
{
    /// <summary>
    /// The lesson debrief's recorder (owner 2026-10-03: "an analytics review after the lesson … charts, diagrams,
    /// screenshots and video playback with commentary"). While a lesson runs it notes, for every judged moment and every
    /// slide into red, the replay clock (so the replay can be cued to it) and grabs a screenshot of the real screen. After
    /// the run, <see cref="WatchReplay"/> plays the flight back from the start with each comment captioned and spoken as
    /// the replay reaches it.
    /// </summary>
    public sealed class LessonDebrief : MonoBehaviour
    {
        public PracticeController Controller;
        public FlightReplay Replay;

        public sealed class Shot { public Texture2D Tex; public string Caption; public Grade Grade; public double LessonT; }
        public readonly List<Shot> Shots = new();
        /// <summary>Replay-clock time of each judge event (same index as Judge.Events).</summary>
        public readonly List<double> EventClock = new();
        public double StartClock { get; private set; } = -1;
        public double StartLessonT { get; private set; }
        public bool Commentary { get; private set; }
        public string Caption { get; private set; } = "";
        public Grade CaptionGrade { get; private set; }

        private PracticeScenario _sc;
        private int _seenEvents, _spokenUpTo;
        private const int MaxShots = 8;

        private void Update()
        {
            Replay ??= FindFirstObjectByType<FlightReplay>();
            PracticeScenario sc = Controller != null && Controller.Active ? Controller.Scenario : null;
            if (sc != _sc) { _sc = sc; Reset(); }
            if (sc == null || Replay == null) { Commentary = false; return; }

            if (!Replay.Active)
            {
                if (Commentary) { Commentary = false; Caption = ""; }
                if (sc.Phase == PracticePhase.Live && StartClock < 0) { StartClock = Replay.Clock; StartLessonT = sc.Time; }
                var ev = sc.Judge.Events;
                while (_seenEvents < ev.Count)
                {
                    EventClock.Add(Replay.Clock - (sc.Time - ev[_seenEvents].t));   // back-date to the moment itself
                    if (Shots.Count < MaxShots) StartCoroutine(Grab(ev[_seenEvents].title, ev[_seenEvents].grade, ev[_seenEvents].t));
                    _seenEvents++;
                }
                return;
            }

            // Replay with commentary: caption the latest event at or before the replay head (for 5 s), say each once.
            if (!Commentary) return;
            var events = sc.Judge.Events;
            Caption = "";
            for (int i = 0; i < EventClock.Count && i < events.Count; i++)
            {
                double dt = Replay.Head - EventClock[i];
                if (dt >= 0 && dt < 5) { Caption = events[i].comment; CaptionGrade = events[i].grade; }
                if (dt >= 0 && i >= _spokenUpTo && Replay.Playing) { _spokenUpTo = i + 1; PilotVoice.Say(events[i].comment, 0.5f, 1.0f); }
            }
        }

        private void Reset()
        {
            foreach (Shot s in Shots) if (s.Tex != null) Destroy(s.Tex);
            Shots.Clear(); EventClock.Clear(); StartClock = -1; _seenEvents = 0; _spokenUpTo = 0; Commentary = false; Caption = "";
        }

        private IEnumerator Grab(string caption, Grade g, double t)
        {
            yield return new WaitForEndOfFrame();
            Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();
            if (full == null) yield break;
            // Keep a 640-wide copy (memory: eight of these are ~6 MB).
            int w = Mathf.Min(640, full.width), h = Mathf.RoundToInt(full.height * (w / (float)full.width));
            var rt = RenderTexture.GetTemporary(w, h);
            Graphics.Blit(full, rt);
            var small = new Texture2D(w, h, TextureFormat.RGB24, false);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0); small.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            Destroy(full);
            Shots.Add(new Shot { Tex = small, Caption = caption, Grade = g, LessonT = t });
        }

        /// <summary>Play the run back from its start with the instructor's commentary.</summary>
        public void WatchReplay()
        {
            if (Replay == null || StartClock < 0) return;
            if (!Replay.Active) Replay.Enter();
            if (!Replay.Active) return;
            Replay.Seek(StartClock);
            Commentary = true; _spokenUpTo = 0;
        }

        /// <summary>Jump the replay to a given event (from the debrief's moment list).</summary>
        public void WatchEvent(int i)
        {
            if (Replay == null || i < 0 || i >= EventClock.Count) return;
            if (!Replay.Active) Replay.Enter();
            if (!Replay.Active) return;
            Replay.Seek(System.Math.Max(StartClock, EventClock[i] - 4));
            Commentary = true; _spokenUpTo = i;
        }

        private void OnGUI()
        {
            if (!Commentary || string.IsNullOrEmpty(Caption) || Replay == null || !Replay.Active) return;
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            var st = new GUIStyle { font = UiFont.Get(), fontSize = fs, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            Rect r = UiLayout.NextBlock(fs * 4.2f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            Color[] gc = { new(0.25f, 0.92f, 0.35f), new(1f, 0.88f, 0.2f), new(1f, 0.55f, 0.12f), new(0.95f, 0.16f, 0.12f) };
            GUI.color = gc[(int)CaptionGrade]; GUI.DrawTexture(new Rect(r.x, r.y, 6f, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 12f, r.y, r.width - 24f, r.height), "INSTRUCTOR:  " + Caption, st);
        }
    }
}

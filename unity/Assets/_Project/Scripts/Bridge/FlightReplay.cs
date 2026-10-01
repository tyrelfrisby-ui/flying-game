using FlyingGame.Core;
using FlyingGame.Sim.Replay;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Flight replay (owner 2026-10-01): records the last 10 minutes of flight (30 Hz pose, surfaces, power, gear, g, wind)
    /// and plays it back on the real airframe, so every camera view, gauge and engine sound works in the replay too.
    /// Entering a replay freezes the world (Time.timeScale 0) and parks the live flight; leaving it puts the aircraft back
    /// exactly where it was and the flight carries on.
    /// </summary>
    public sealed class FlightReplay : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public readonly FlightRecorder Recorder = new FlightRecorder(30.0, 600.0);
        public const double StartBackSec = 30.0;   // a replay opens this far before "now"

        public bool Active { get; private set; }
        public bool Playing;
        public float Speed = 1f;
        public static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };
        /// <summary>Play head (recorder clock seconds).</summary>
        public double Head { get; private set; }

        private double _clock;
        private ReplayFrame _live;
        private float _savedTimeScale = 1f;
        private Vector3 _lastPos;
        private bool _haveLast;

        public bool CanReplay => !Active && Recorder.Duration > 2.0 && !SessionSettings.MenuOpen && Time.timeScale > 0f;

        private void Awake()
        {
            Driver ??= GetComponent<FlightSimDriver>();
            Driver.AircraftChanged += OnAircraftChanged;
        }

        private void OnDestroy() { if (Driver != null) Driver.AircraftChanged -= OnAircraftChanged; }

        private void OnAircraftChanged()
        {
            if (Active) Exit();
            Recorder.Clear();
            _haveLast = false;
        }

        private void LateUpdate()
        {
            if (Active || Driver?.Sim == null || SessionSettings.MenuOpen || Time.deltaTime <= 0f) return;
            var a = Driver.Sim.Aircraft;
            Vector3 pos = transform.position;
            if (_haveLast && (pos - _lastPos).magnitude > 500f) Recorder.Clear();   // teleported (challenge / event start): a new flight
            _lastPos = pos; _haveLast = true;
            _clock += Time.deltaTime;
            Recorder.Record(ReplayFrame.Capture(_clock, a, Atmosphere.WindAtPosition(a.State.Position)));
        }

        public void Enter()
        {
            if (!CanReplay) return;
            var a = Driver.Sim.Aircraft;
            _live = ReplayFrame.Capture(_clock, a, Atmosphere.WindAtPosition(a.State.Position));
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;   // tug, drones, bubbles, pilot … all hold still while we watch
            Active = true;
            Driver.Replaying = true;
            SessionSettings.ReplayActive = true;
            Head = System.Math.Max(Recorder.StartTime, Recorder.EndTime - StartBackSec);
            Playing = true;
            Speed = 1f;
            Show();
        }

        public void Exit()
        {
            if (!Active) return;
            _live.ApplyTo(Driver.Sim.Aircraft);
            Driver.ShowReplayPose(_live.WindWorld);
            Active = false;
            Driver.Replaying = false;
            SessionSettings.ReplayActive = false;
            Time.timeScale = SessionSettings.MenuOpen ? 0f : (_savedTimeScale > 0f ? _savedTimeScale : 1f);
        }

        public void Seek(double t)
        {
            Head = System.Math.Clamp(t, Recorder.StartTime, Recorder.EndTime);
            Show();
        }

        public void TogglePlay()
        {
            if (!Playing && Head >= Recorder.EndTime - 1e-3) Head = Recorder.StartTime;   // at the end: play from the top
            Playing = !Playing;
        }

        public void CycleSpeed()
        {
            int i = System.Array.IndexOf(Speeds, Speed);
            Speed = Speeds[(i + 1) % Speeds.Length];
        }

        private void Update()
        {
            if (!Active) return;
            if (Playing)
            {
                Head += Time.unscaledDeltaTime * Speed;
                if (Head >= Recorder.EndTime) { Head = Recorder.EndTime; Playing = false; }
            }
            Show();
        }

        private void Show()
        {
            if (Recorder.Count == 0) return;
            ReplayFrame f = Recorder.Sample(Head);
            f.ApplyTo(Driver.Sim.Aircraft);
            Driver.ShowReplayPose(f.WindWorld);
        }
    }
}

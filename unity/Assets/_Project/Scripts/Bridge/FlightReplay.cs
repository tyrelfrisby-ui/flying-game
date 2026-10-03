using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Sim.Replay;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Things outside the aircraft that the replay must show: the pilot, his parachute, the jettisoned canopy and
    /// seat (registered when they are created) and aircraft parts whose visibility changes (the canopy).</summary>
    public static class ReplayActors
    {
        internal static readonly List<(GameObject go, bool hideBefore)> Pending = new();

        /// <summary>Record this object and everything under it from now on. hideBefore: invisible in the replay before it
        /// was registered (created during the flight); false = shown as it was at registration (an existing part).</summary>
        public static void Register(GameObject go, bool hideBefore = true) { if (go != null) Pending.Add((go, hideBefore)); }
    }

    /// <summary>
    /// Flight replay (owner 2026-10-01): records the last 10 minutes of flight (30 Hz pose, surfaces, power, gear, g, wind)
    /// and plays it back on the real airframe, so every camera view, gauge and engine sound works in the replay too.
    /// The bail-out is part of it: every pilot / parachute / canopy / seat object is recorded part by part (pose, scale,
    /// visibility) and the camera follows the replayed pilot after the moment he left, as it does live.
    /// Entering a replay freezes the world (Time.timeScale 0) and parks the live flight; leaving it puts the aircraft and
    /// the egress objects back exactly where they were and the flight carries on.
    /// </summary>
    [DefaultExecutionOrder(900)]
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
        /// <summary>The recorder's clock now (lesson debrief: where each judged moment sits in the replay).</summary>
        public double Clock => _clock;

        private double _clock;
        private ReplayFrame _live;
        private float _savedTimeScale = 1f;
        private Vector3 _lastPos;
        private bool _haveLast;

        // ---- egress actors ----
        private sealed class Track
        {
            public Transform T; public Renderer R; public int Depth;
            public bool BeforeActive, BeforeRendered;
            public readonly List<double> Time = new();
            public readonly List<Vector3> Pos = new(), Scale = new();
            public readonly List<Quaternion> Rot = new();
            public readonly List<byte> Flags = new();   // bit0 active, bit1 renderer enabled
            // live state saved on Enter
            public Vector3 LPos, LScale; public Quaternion LRot; public bool LActive, LRendered;
        }
        private readonly List<(GameObject go, bool hideBefore)> _roots = new();
        private readonly Dictionary<Transform, Track> _tracks = new();
        private readonly List<Track> _ordered = new();
        private PilotEgress _egress;
        private double _pilotLeftT = double.NaN;
        private Transform _pilotRoot;
        private Transform _camOvrTarget, _camOvrBackdrop; private Vector3 _camOvrVel;
        private ChaseCamera _cam;

        /// <summary>At the play head the pilot has already left the aircraft (the audio listens from him then).</summary>
        public static bool ReplayPilotOut { get; private set; }
        /// <summary>The replayed pilot's speed (m/s): the wind in his ears.</summary>
        public static float ReplayPilotSpeedMs { get; private set; }

        public bool CanReplay => !Active && Recorder.Duration > 2.0 && !SessionSettings.MenuOpen && Time.timeScale > 0f;

        private void Awake()
        {
            Driver ??= GetComponent<FlightSimDriver>();
            Driver.AircraftChanged += OnAircraftChanged;
        }

        private void Start()
        {
            _egress = GetComponent<PilotEgress>();
            if (_egress != null) _egress.PilotLeft += OnPilotLeft;
        }

        private void OnDestroy()
        {
            if (Driver != null) Driver.AircraftChanged -= OnAircraftChanged;
            if (_egress != null) _egress.PilotLeft -= OnPilotLeft;
        }

        private void OnPilotLeft()
        {
            _pilotLeftT = _clock;
            _pilotRoot = _egress.PilotTransform;
        }

        private void OnAircraftChanged()
        {
            if (Active) Exit();
            ClearAll();
        }

        private void ClearAll()
        {
            Recorder.Clear();
            _haveLast = false;
            _roots.Clear(); _tracks.Clear(); _ordered.Clear(); ReplayActors.Pending.Clear();
            _pilotLeftT = double.NaN; _pilotRoot = null;
        }

        private void LateUpdate()
        {
            if (Active) { ShowActors(); return; }
            if (Driver?.Sim == null || SessionSettings.MenuOpen || Time.deltaTime <= 0f) return;
            var a = Driver.Sim.Aircraft;
            Vector3 pos = transform.position;
            if (_haveLast && (pos - _lastPos).magnitude > 500f) ClearAll();   // teleported (challenge / event start): a new flight
            _lastPos = pos; _haveLast = true;
            _clock += Time.deltaTime;
            if (Recorder.Record(ReplayFrame.Capture(_clock, a, Atmosphere.WindAtPosition(a.State.Position)))) RecordActors();
        }

        private void RecordActors()
        {
            foreach (var p in ReplayActors.Pending) _roots.Add(p);
            ReplayActors.Pending.Clear();
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                var (go, hide) = _roots[i];
                if (go == null) { _roots.RemoveAt(i); continue; }
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (!_tracks.TryGetValue(t, out Track tr))
                    {
                        tr = new Track { T = t, R = t.GetComponent<Renderer>() };
                        for (Transform q = t.parent; q != null; q = q.parent) tr.Depth++;
                        tr.BeforeActive = !hide && t.gameObject.activeSelf;
                        tr.BeforeRendered = !hide && (tr.R == null || tr.R.enabled);
                        _tracks[t] = tr;
                        _ordered.Add(tr);
                        _ordered.Sort((x, y) => x.Depth.CompareTo(y.Depth));
                    }
                    tr.Time.Add(_clock); tr.Pos.Add(t.position); tr.Rot.Add(t.rotation); tr.Scale.Add(t.localScale);
                    tr.Flags.Add((byte)((t.gameObject.activeSelf ? 1 : 0) | (tr.R == null || tr.R.enabled ? 2 : 0)));
                }
            }
            // Drop samples older than the recording window (in chunks, now and then).
            double start = Recorder.StartTime;
            foreach (Track tr in _ordered)
            {
                int n = 0;
                while (n < tr.Time.Count - 1 && tr.Time[n] < start) n++;
                if (n > 300)
                {
                    tr.Time.RemoveRange(0, n); tr.Pos.RemoveRange(0, n); tr.Rot.RemoveRange(0, n); tr.Scale.RemoveRange(0, n); tr.Flags.RemoveRange(0, n);
                }
            }
        }

        private static void SetVisible(Track tr, bool active, bool rendered)
        {
            if (tr.T.gameObject.activeSelf != active) tr.T.gameObject.SetActive(active);
            if (tr.R != null && tr.R.enabled != rendered) tr.R.enabled = rendered;
        }

        private static int IndexAt(List<double> times, double t)
        {
            int lo = 0, hi = times.Count - 1;
            if (t >= times[hi]) return hi;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (times[mid] <= t) lo = mid; else hi = mid; }
            return lo;
        }

        private static Vector3 PosAt(Track tr, double t)
        {
            int i = IndexAt(tr.Time, t);
            if (i + 1 >= tr.Time.Count || t <= tr.Time[i]) return tr.Pos[i];
            float u = (float)((t - tr.Time[i]) / (tr.Time[i + 1] - tr.Time[i]));
            return Vector3.Lerp(tr.Pos[i], tr.Pos[i + 1], u);
        }

        /// <summary>Pose every recorded egress object as it was at the play head, and aim the camera like the live game did.</summary>
        private void ShowActors()
        {
            double t = Head;
            foreach (Track tr in _ordered)
            {
                if (tr.T == null || tr.Time.Count == 0) continue;
                if (t < tr.Time[0]) { SetVisible(tr, tr.BeforeActive, tr.BeforeRendered); continue; }
                int i = IndexAt(tr.Time, t);
                Vector3 pos = tr.Pos[i]; Quaternion rot = tr.Rot[i];
                if (i + 1 < tr.Time.Count && t > tr.Time[i])
                {
                    float u = (float)((t - tr.Time[i]) / (tr.Time[i + 1] - tr.Time[i]));
                    pos = Vector3.Lerp(pos, tr.Pos[i + 1], u); rot = Quaternion.Slerp(rot, tr.Rot[i + 1], u);
                }
                byte f = tr.Flags[i];
                SetVisible(tr, (f & 1) != 0, (f & 2) != 0);
                tr.T.SetPositionAndRotation(pos, rot);
                tr.T.localScale = tr.Scale[i];
            }
            if (_cam == null) return;
            bool pilotOut = !double.IsNaN(_pilotLeftT) && t >= _pilotLeftT && _pilotRoot != null && _tracks.TryGetValue(_pilotRoot, out Track pt) && pt.Time.Count > 1;
            if (pilotOut)
            {
                Track tr = _tracks[_pilotRoot];
                const double dtV = 0.2;
                _cam.OverrideTarget = _pilotRoot;
                _cam.OverrideBackdrop = transform;   // the abandoned aircraft behind him, as live
                _cam.OverrideVelocity = (PosAt(tr, t) - PosAt(tr, t - dtV)) / (float)dtV;
                ReplayPilotSpeedMs = _cam.OverrideVelocity.magnitude;
            }
            else { _cam.OverrideTarget = null; _cam.OverrideBackdrop = null; ReplayPilotSpeedMs = 0f; }
            ReplayPilotOut = pilotOut;
        }

        public void Enter()
        {
            if (!CanReplay) return;
            var a = Driver.Sim.Aircraft;
            _live = ReplayFrame.Capture(_clock, a, Atmosphere.WindAtPosition(a.State.Position));
            foreach (Track tr in _ordered)
            {
                if (tr.T == null) continue;
                tr.LPos = tr.T.position; tr.LRot = tr.T.rotation; tr.LScale = tr.T.localScale;
                tr.LActive = tr.T.gameObject.activeSelf; tr.LRendered = tr.R == null || tr.R.enabled;
            }
            _cam = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            if (_cam != null) { _camOvrTarget = _cam.OverrideTarget; _camOvrBackdrop = _cam.OverrideBackdrop; _camOvrVel = _cam.OverrideVelocity; }
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
            foreach (Track tr in _ordered)
            {
                if (tr.T == null) continue;
                SetVisible(tr, tr.LActive, tr.LRendered);
                tr.T.SetPositionAndRotation(tr.LPos, tr.LRot);
                tr.T.localScale = tr.LScale;
            }
            if (_cam != null) { _cam.OverrideTarget = _camOvrTarget; _cam.OverrideBackdrop = _camOvrBackdrop; _cam.OverrideVelocity = _camOvrVel; }
            Active = false;
            ReplayPilotOut = false;
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

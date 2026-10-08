using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The widget's history (protocol 3): a ring buffer of the last 60 s of SIMULATION time at 30 frames a second — every
    /// snapshot is enough to redraw that moment exactly (pose, velocities and rates, control deflections and inputs, every
    /// force sample the vectors are drawn from, the camera), so the readout, the α / STALLED labels and the arrows of a
    /// past frame come back exactly as they were. Bounded: 1,800 frames; the oldest is dropped.
    /// </summary>
    public sealed class WidgetHistory
    {
        public const int Hz = 30, Seconds = 60, Capacity = Hz * Seconds;
        public const double FrameMs = 1000.0 / Hz;

        public sealed class Snap
        {
            public RigidBodyState State; public ControlDeflections Defl; public double Flaps, Nz, AcThrottle, GearExt;
            public ForceSample[] Forces;
            public double Ail, Ele, Rud, Thr, Brake; public bool HandsOff;
            public Vector3 CamPos; public Quaternion CamRot; public bool Ortho; public float OrthoSize, Fov;
            public double SimTime;
            // Protocol 4 inputs (filled when present).
            public double FlapsCmd, FlapsActual, Spoilers, BrakeL, BrakeR; public bool SpoilersArmed; public string Gear = "fixed", GearLights = "down";
            public double[] EngineThr;
            public string PilotName, PilotColor; public bool HandsOffTag;
        }

        private readonly Snap[] _ring = new Snap[Capacity];
        private int _head = -1, _count;
        public int Count => _count;

        public void Clear() { _head = -1; _count = 0; System.Array.Clear(_ring, 0, _ring.Length); }

        public void Add(Snap s)
        {
            _head = (_head + 1) % Capacity;
            _ring[_head] = s;
            if (_count < Capacity) _count++;
        }

        /// <summary>Frame <paramref name="back"/> frames before the newest (0 = the newest).</summary>
        public Snap Get(int back)
        {
            if (_count == 0) return null;
            back = Mathf.Clamp(back, 0, _count - 1);
            return _ring[((_head - back) % Capacity + Capacity) % Capacity];
        }

        /// <summary>Drop the newest <paramref name="n"/> frames (resuming from a past frame branches the history).</summary>
        public void DropNewest(int n)
        {
            n = Mathf.Clamp(n, 0, _count);
            for (int i = 0; i < n; i++) { _ring[_head] = null; _head = (_head - 1 + Capacity) % Capacity; _count--; }
            if (_count == 0) _head = -1;
        }
    }

    /// <summary>REVIEW MODE (owner 2026-10-08, protocol 3): pause, then step / rewind / seek / play through the history; the
    /// picture (Syphon + NDI) shows the reviewed frame; controls don't move the aircraft until resume.</summary>
    public sealed class WidgetReview
    {
        private readonly AeroWidget _w;
        public readonly WidgetHistory History = new();
        /// <summary>Frames back from the newest (0 = the live edge where it was paused).</summary>
        public int Index { get; private set; }
        public bool Playing { get; private set; }
        public float Rate { get; private set; } = 0.25f;
        public string Direction { get; private set; } = "reverse";
        private float _recAcc, _playAcc;
        private double _simTime;

        public WidgetReview(AeroWidget w) { _w = w; }

        public bool Active => _w.Paused;
        public double OffsetMs => -Index * WidgetHistory.FrameMs;
        public double HistoryMs => History.Count * WidgetHistory.FrameMs;
        public WidgetHistory.Snap Shown => History.Get(Index);

        public void Clear() { History.Clear(); Index = 0; Playing = false; _recAcc = 0; }

        /// <summary>Called every live frame with the sim dt: a snapshot each 1/30 s of simulation time.</summary>
        public void Record(float simDt)
        {
            _simTime += simDt;
            _recAcc += simDt;
            if (History.Count > 0 && _recAcc < 1f / WidgetHistory.Hz) return;
            _recAcc = Mathf.Repeat(_recAcc, 1f / WidgetHistory.Hz);
            History.Add(Capture());
        }

        public WidgetHistory.Snap Capture()
        {
            var ac = _w.Ac; var c = _w.Controls; var cam = _w.Cam;
            var s = new WidgetHistory.Snap
            {
                State = ac.State, Defl = ac.CurrentDeflections, Flaps = ac.FlapFraction, Nz = ac.LoadFactorZ, AcThrottle = ac.Throttle01, GearExt = ac.GearExtension,
                Forces = ac.LastForces != null ? ac.LastForces.ToArray() : new ForceSample[0],
                Ail = c.Aileron, Ele = c.Elevator, Rud = c.Rudder, Thr = c.Throttle01, Brake = c.Brake01, HandsOff = c.ElevatorFree,
                CamPos = cam.transform.position, CamRot = cam.transform.rotation, Ortho = cam.orthographic, OrthoSize = cam.orthographicSize, Fov = cam.fieldOfView,
                SimTime = _simTime,
            };
            c.FillSnap(s);
            return s;
        }

        // ---- commands ----
        public void Pause()
        {
            if (!_w.Paused && _recAcc > 1e-4f) { History.Add(Capture()); _recAcc = 0; }   // the exact paused moment, if it fell between frames
            _w.Paused = true; Index = 0; Playing = false;
        }

        /// <summary>Move N frames (− back, + forward). Forward past the live edge advances the live sim N frames.</summary>
        public void Step(int frames)
        {
            if (!_w.Paused) Pause();
            Playing = false;
            int target = Index - frames;
            if (target >= 0) { Index = Mathf.Min(target, History.Count - 1); return; }
            // Past "now": fly the sim on, a frame at a time, recording as it goes, and stay paused on the newest.
            int more = -target;
            Index = 0;
            float dt = 1f / WidgetHistory.Hz;
            for (int i = 0; i < more; i++)
            {
                _w.Presets.Tick(dt);
                _w.StepSim(dt);
                _w.ShowLive(dt);
                _simTime += dt;
                History.Add(Capture());
            }
        }

        public void Rewind(double seconds) { if (!_w.Paused) Pause(); Playing = false; Index = Mathf.Clamp(Index + Mathf.RoundToInt((float)(seconds * WidgetHistory.Hz)), 0, Mathf.Max(0, History.Count - 1)); }

        public void Seek(double offsetMs) { if (!_w.Paused) Pause(); Playing = false; Index = Mathf.Clamp(Mathf.RoundToInt((float)(-offsetMs / WidgetHistory.FrameMs)), 0, Mathf.Max(0, History.Count - 1)); }

        public void Play(float rate, string direction)
        {
            if (!_w.Paused) Pause();
            Rate = Mathf.Clamp(rate, 0.05f, 4f); Direction = direction == "forward" ? "forward" : "reverse";
            Playing = true; _playAcc = 0;
        }

        /// <summary>Resume flying: from the frame shown (the history after it is discarded), or "live" — back where it paused.</summary>
        public void Resume(bool fromLive)
        {
            if (!_w.Paused) return;
            if (fromLive || Index == 0) _w.ResumeFrom(fromLive ? null : Shown);
            else { var f = Shown; History.DropNewest(Index); _w.ResumeFrom(f); }
            Index = 0; Playing = false; _recAcc = 0;
        }

        /// <summary>Review playback, while paused.</summary>
        public void Tick(float realDt)
        {
            if (!Playing) return;
            _playAcc += realDt * Rate * WidgetHistory.Hz;
            while (_playAcc >= 1f)
            {
                _playAcc -= 1f;
                if (Direction == "reverse") { if (Index >= History.Count - 1) { Playing = false; break; } Index++; }
                else { if (Index <= 0) { Playing = false; break; } Index--; }
            }
        }

        /// <summary>The reply's "frame": what the shown frame says (index back from the live edge, offset, the key readouts).</summary>
        public Newtonsoft.Json.Linq.JObject FrameJson()
        {
            var f = Shown; if (f == null) return new Newtonsoft.Json.Linq.JObject();
            var r = _w.Read(f.State, f.Nz);
            return new Newtonsoft.Json.Linq.JObject
            {
                ["index"] = -Index, ["offsetMs"] = System.Math.Round(OffsetMs), ["simTime"] = System.Math.Round(f.SimTime, 3),
                ["kias"] = System.Math.Round(r.Kias, 1), ["alpha"] = System.Math.Round(r.AlphaDeg, 2), ["pitch"] = System.Math.Round(r.PitchDeg, 1),
                ["roll"] = System.Math.Round(r.RollDeg, 1), ["yawRate"] = System.Math.Round(r.YawRateDps, 1),
                ["leftWingAlpha"] = System.Math.Round(r.LeftAlphaDeg, 1), ["rightWingAlpha"] = System.Math.Round(r.RightAlphaDeg, 1),
                ["leftStalled"] = r.LeftStalled, ["rightStalled"] = r.RightStalled,
            };
        }
    }
}

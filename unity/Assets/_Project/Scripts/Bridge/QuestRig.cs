using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using UnityEngine;
using UnityEngine.XR;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE XR CAMERA for a native headset (Meta Quest through OpenXR). Lives on the main camera beside
    /// <see cref="ChaseCamera"/> and <see cref="VrRig"/>, and does nothing unless <see cref="XrHeadset.Active"/>.
    ///
    /// The main camera IS the XR camera: OpenXR renders it to both eyes (single-pass instanced / multiview) at the
    /// runtime's own projection. Unity's implicit camera tracking is switched off for it
    /// (<see cref="XRDevice.DisableAutoXRCameraTracking"/>) because the head here is not "wherever the headset is in
    /// the room" — it is the head RELATIVE TO THE ACTIVE SEAT'S AUTHORED EYE, in the aeroplane:
    ///
    ///   camera = origin · (head relative to the recentre pose, heading removed, lean clamped)
    ///   origin = the aeroplane's transform at <see cref="CockpitView.EyeLocal"/> (the selected seat), facing the nose
    ///
    /// so the pilot sits where Codex put the seat's eye, the aeroplane rolls him with it, and his own head moves on
    /// top one-for-one. No look gain, ever, and the hat / on-screen look pad do not apply: in a headset the head is
    /// the look.
    ///
    /// SEATED, EYE-LEVEL TRACKING ("device" origin, not floor). The authored eye IS the reference height, so a
    /// floor origin would only add the pilot's real seated height on top of an eye point that already includes
    /// it. Recentre (<see cref="XrHeadset.Recentre"/>) puts wherever the head is now at the eye, facing the nose:
    /// on entering the cockpit, on changing seat, on the left stick click / a held right-hand pinch, from the VR
    /// menu, and when the runtime itself recentres (holding the Meta button).
    ///
    /// Outside views keep working: the origin is then wherever <see cref="ChaseCamera"/> put the camera, levelled
    /// to the horizon (a chase camera that rolls with the aeroplane is the quickest way to make someone ill in a
    /// headset), with the head on top.
    ///
    /// The pose is applied twice a frame: in LateUpdate (after ChaseCamera, so this wins) and again just before
    /// rendering with the freshest head pose (<see cref="Application.onBeforeRender"/>), which is what a
    /// TrackedPoseDriver does and what keeps the world from swimming.
    ///
    /// FOR CODEX: the knobs are <see cref="XrHeadset"/> (render scale, foveation, refresh rate); the HUD-in-VR hook
    /// is <see cref="HudAnchor"/>, a transform at the seat's eye that rides the aeroplane (NOT the head), with the
    /// flat overlays already standing down (<see cref="VrMode.SuppressFlatOverlays"/>). See AGENT-LOG HANDOFF.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [RequireComponent(typeof(Camera))]
    public sealed class QuestRig : MonoBehaviour
    {
        private Camera _cam;
        private ChaseCamera _chase;
        private readonly XrHeadPose _head = new();
        private readonly QuestControlMapper _system = new();
        private QuestMenu _menu;
        private readonly QuestMenuTabView _tab = new();
        private float _menuErrorAt = -100f;
        private bool _running, _wasCockpit;
        private int _lastSeat = -1;
        private Vector3 _originPos;
        private Quaternion _originRot = Quaternion.identity;
        private Transform _hudAnchor;
        /// <summary>The incremental GC's slice in a headset, ns (see StartRig).</summary>
        public const ulong GcSliceNs = 1_000_000;
        private ulong _gcSliceWas;
        private float _perfT, _perfFrames, _perfCpuMs, _perfCpuMax, _perfFrameMax;
        private int _perfDropped0 = -1, _perfLong, _perfGc0 = -1, _perfCapped0;

        /// <summary>
        /// THE HUD-IN-VR HOOK. A transform at the active seat's eye, oriented with the aeroplane (forward = the nose),
        /// re-placed every frame. Parent world-space HUD geometry here and it sits in the cockpit, rolls with the
        /// aeroplane, and stays put when the pilot turns his head. Null until the headset starts.
        /// </summary>
        public Transform HudAnchor => _hudAnchor;

        /// <summary>True while the head is being held at the edge of the cockpit box (leaning too far). Codex's fade
        /// hook: <see cref="XrHeadPose.Clamped"/>.</summary>
        public bool HeadClamped { get; private set; }

        private void OnEnable()
        {
            XrHeadset.RecentreRequested += RecentreNow;
            XrHeadset.SettingsChanged += ApplySettings;
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDisable()
        {
            XrHeadset.RecentreRequested -= RecentreNow;
            XrHeadset.SettingsChanged -= ApplySettings;
            Application.onBeforeRender -= OnBeforeRender;
            StopRig();
        }

        private void LateUpdate()
        {
            bool want = XrHeadset.Active;
            if (want && !_running) StartRig();
            else if (!want && _running) StopRig();
            if (!_running) return;

            // Whatever ChaseCamera did this frame becomes the origin for an outside view; it ran before us.
            if (_cam.rect != FullRect) _cam.rect = FullRect;   // ChaseCamera shrinks it for a portrait tray
            CaptureOrigin();

            // Entering the cockpit or changing seat: the head goes to the (new) eye.
            bool cockpit = ChaseCamera.InCockpit;
            int seat = _chase != null && _chase.Cockpit != null ? _chase.Cockpit.SeatIndex : 0;
            if (cockpit && (!_wasCockpit || seat != _lastSeat)) RecentreNow();
            _wasCockpit = cockpit; _lastSeat = seat;

            SystemInput();
            Place();
            // The menu must never take the rig down with it, and an error in it must never be silent (the a222bda
            // menu threw on every press and nobody could see why): log it, at most every few seconds.
            try { _menu?.Tick(this); }
            catch (System.Exception e) { MenuError("tick", e); }
            FlyReport();
            PerfLog();
        }

        private int _reportAtFrame = -1;

        /// <summary>Log where FLY put everything once the head has been placed for it (two frames on: the
        /// ChaseCamera has been into the cockpit and this rig has recentred on the eye).</summary>
        public void ReportAfterFly() => _reportAtFrame = Time.frameCount + 2;

        /// <summary>One line logcat can prove a start with: the aeroplane, the ground under it, the head. Positions
        /// are Unity world metres (x east, y up, z north).</summary>
        private void FlyReport()
        {
            if (_reportAtFrame < 0 || Time.frameCount < _reportAtFrame) return;
            _reportAtFrame = -1;
            Transform air = _chase != null ? _chase.Target : null;
            FlightSimDriver d = _chase != null ? _chase.Driver : null;
            if (air == null) { Debug.LogWarning("[Quest] FLY: no aeroplane to report"); return; }
            Vector3 a = air.position;
            double ground = FlyingGame.Core.WorldTerrain.GroundHeightAt(a.z, a.x);   // Unity (east, up, north) → sim (north, east)
            Vector3 eye = _chase.Cockpit != null ? air.TransformPoint(_chase.Cockpit.EyeLocal) : a;
            Debug.Log($"[Quest] FLY: {(d != null ? d.AircraftId : "?")} start {SessionSettings.StartMode} | aircraft ({a.x:F1}, {a.y:F1}, {a.z:F1}) " +
                      $"ground {ground:F1} m AGL {a.y - ground:F1} m | sim {(d != null && d.Sim != null ? "running" : "NONE")} | " +
                      $"camera ({transform.position.x:F1}, {transform.position.y:F1}, {transform.position.z:F1}) " +
                      $"{Vector3.Distance(transform.position, eye):F2} m from the seat eye | InCockpit {ChaseCamera.InCockpit}");
        }

        private void OnBeforeRender()
        {
            if (_running) Place();
        }

        private static readonly Rect FullRect = new(0f, 0f, 1f, 1f);

        // ---- going in and coming out ----

        private void StartRig()
        {
            _cam = GetComponent<Camera>();
            _chase = GetComponent<ChaseCamera>();
            if (_cam == null) return;
            _running = true;
            XRDevice.DisableAutoXRCameraTracking(_cam, true);
            _cam.stereoTargetEye = StereoTargetEyeMask.Both;
            // Seated: the eye-level ("device") origin. Floor would add the pilot's real seated height to an eye
            // point that already has one.
            var inputs = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var s in inputs)
            {
                s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
                s.trackingOriginUpdated -= OnRuntimeRecentre;
                s.trackingOriginUpdated += OnRuntimeRecentre;
            }
            if (_hudAnchor == null) _hudAnchor = new GameObject("VR HUD Anchor").transform;
            _menu ??= new QuestMenu();
            // The "what I see" recorder (A + B held, or the menu): Android only, on this camera because the
            // AudioListener is here and it records the head's view.
            if (QuestRecorder.Supported && GetComponent<QuestRecorder>() == null) gameObject.AddComponent<QuestRecorder>();
            ApplySettings();
            // A flight sim at 200 Hz with a world to the horizon: ask for sustained-high CPU and GPU levels rather
            // than letting the runtime save power (QuestPerformanceHints, XR_EXT_performance_settings).
            XrHeadset.PerformanceHinter?.Invoke();
            _perfGc0 = -1;
            // The incremental collector's slice: Unity's default (3 ms) is a fifth of a 72 Hz frame, on top of a frame the
            // sim already fills; 1 ms slices take longer to finish a cycle and never cost a frame. Restored in StopRig.
            if (UnityEngine.Scripting.GarbageCollector.isIncremental)
            {
                if (_gcSliceWas == 0) _gcSliceWas = UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds;
                UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = GcSliceNs;
            }
            _head.Forget();          // the first head pose recentres
            XrControllers.LogDevices("headset started");
            Debug.Log($"[Quest] XR on — device '{XRSettings.loadedDeviceName}', eye texture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, " +
                      $"render scale {XRSettings.eyeTextureResolutionScale:F2}, stereo {XRSettings.stereoRenderingMode}, " +
                      $"refresh {XrHeadset.CurrentRefreshHz:F0} Hz. Cardboard VR and the device look stand down.");
        }

        private void StopRig()
        {
            if (!_running) return;
            _running = false;
            var inputs = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var s in inputs) s.trackingOriginUpdated -= OnRuntimeRecentre;
            if (_cam != null) XRDevice.DisableAutoXRCameraTracking(_cam, false);
            if (_gcSliceWas != 0) { UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = _gcSliceWas; _gcSliceWas = 0; }
            QuestRecorder.Instance?.StopRecording(QuestRecorder.StopWaitMs);
            _menu?.Close(force: true);
            Debug.Log("[Quest] XR off.");
        }

        private void OnRuntimeRecentre(XRInputSubsystem _) => RecentreNow();

        /// <summary>Apply the knobs in <see cref="XrHeadset"/>: refresh rate, render scale, fixed foveation.</summary>
        private void ApplySettings()
        {
            if (!_running) return;
            XRSettings.eyeTextureResolutionScale = XrHeadset.RenderScale;
            XRDisplaySubsystem d = XrHeadset.Display;
            if (d != null) d.foveatedRenderingLevel = XrHeadset.FoveationLevel;
            float want = XrHeadset.WantedRefreshHz;
            bool asked = XrHeadset.RefreshRateRequester != null && XrHeadset.RefreshRateRequester(want);
            Debug.Log($"[Quest] refresh rate: asked {want:F0} Hz ({(asked ? "accepted" : XrHeadset.RefreshRateRequester == null ? "no requester" : "refused")}), " +
                      $"now {XrHeadset.CurrentRefreshHz:F0} Hz; render scale {XrHeadset.RenderScale:F2}, foveation {XrHeadset.FoveationLevel:F2}");
        }

        // ---- the head ----

        private void RecentreNow()
        {
            if (XrControllers.TryHead(out Vector3 p, out Quaternion q))
            {
                _head.Recentre(ToCore(p), ToCore(q));
                XrControllers.SeatYawDeg = (float)_head.ReferenceYawDeg;
                Debug.Log($"[Quest] recentred (heading {_head.ReferenceYawDeg:F0}° taken out)");
            }
            else _head.Forget();
        }

        private void CaptureOrigin()
        {
            Transform air = _chase != null ? _chase.Target : null;
            CockpitView cockpit = _chase != null ? _chase.Cockpit : null;
            if (ChaseCamera.InCockpit && air != null && cockpit != null)
            {
                _originPos = air.TransformPoint(cockpit.EyeLocal);
                _originRot = air.rotation;
            }
            else
            {
                // Outside: where ChaseCamera put the camera, heading only — the horizon stays level.
                Vector3 f = transform.forward;
                Vector3 flat = new Vector3(f.x, 0f, f.z);
                _originPos = transform.position;
                _originRot = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : _originRot;
            }
            if (_hudAnchor != null && air != null && cockpit != null)
                _hudAnchor.SetPositionAndRotation(air.TransformPoint(cockpit.EyeLocal), air.rotation);
        }

        private void Place()
        {
            if (!XrControllers.TryHead(out Vector3 p, out Quaternion q)) return;
            if (!_head.IsCentred) RecentreNow();
            (Vec3 rp, Quat rq) = _head.Relative(ToCore(p), ToCore(q));
            HeadClamped = _head.Clamped(ToCore(p));
            var local = new Vector3((float)rp.X, (float)rp.Y, (float)rp.Z);
            var rot = new Quaternion((float)rq.X, (float)rq.Y, (float)rq.Z, (float)rq.W);
            transform.SetPositionAndRotation(_originPos + _originRot * local, _originRot * rot);
        }

        /// <summary>Tracking space → world, through the same origin and recentre as the head. The menu's pointer.</summary>
        public bool TrackingToWorld(Vector3 p, Quaternion q, out Vector3 wp, out Quaternion wq)
        {
            wp = default; wq = Quaternion.identity;
            if (!_head.IsCentred) return false;
            // The pointer is NOT clamped to the head's box: only the heading and the recentre position come out.
            Vec3 lp = _head.ToSeat(ToCore(p));
            Quat lq = _head.ToSeat(ToCore(q));
            wp = _originPos + _originRot * new Vector3((float)lp.X, (float)lp.Y, (float)lp.Z);
            wq = _originRot * new Quaternion((float)lq.X, (float)lq.Y, (float)lq.Z, (float)lq.W);
            return true;
        }

        // ---- buttons that work everywhere: menu, recentre and record ----

        private void SystemInput()
        {
            QuestControllerFrame f = XrControllers.Read(out bool any);
            if (!any) return;
            // Its own mapper instance, stepped once a frame: the flight one (TouchFlightControls) does not run while
            // a menu is open, and these two buttons must.
            QuestFlightCommand c = _system.Step(f, Time.unscaledDeltaTime, 0.0);
            if (c.Recentre) RecentreNow();
            // The MENU tab on the panel: cockpit view, in flight, menu closed (QuestMenuTabView, QuestMenuTab).
            bool tabShown = !XrHeadset.MenuOpen && !SessionSettings.MenuOpen && ChaseCamera.InCockpit;
            QuestMenuTrigger tab = _tab.Tick(this, f, tabShown, Time.unscaledDeltaTime);
            QuestMenuTrigger by = c.Menu ? c.MenuBy : tab;
            if (by != QuestMenuTrigger.None) ToggleMenu(by);
            if (c.Record && !XrHeadset.MenuOpen) QuestRecorder.Toggle();   // in the menu A selects and B closes: use its row
        }

        private void ToggleMenu(QuestMenuTrigger by)
        {
            string via = by switch
            {
                QuestMenuTrigger.MenuButton => $"the left MENU button ({XrControllers.MenuVia})",
                QuestMenuTrigger.StickClickHeld => "the left stick click held 1 s",
                QuestMenuTrigger.LeftPinchHeld => "a left pinch held 1 s",
                QuestMenuTrigger.TabPoked => "a poke on the MENU tab",
                QuestMenuTrigger.TabPointedTrigger => "the MENU tab, pointed at + trigger",
                _ => by.ToString(),
            };
            Debug.Log($"[QuestInput] menu trigger: {via}");
            try { _menu?.Toggle(this, via); }
            catch (System.Exception e) { MenuError("open/close", e); }
        }

        private void MenuError(string what, System.Exception e)
        {
            if (Time.unscaledTime - _menuErrorAt < 5f) return;
            _menuErrorAt = Time.unscaledTime;
            Debug.LogError($"[QuestInput] VR menu {what} failed: {e}");
        }

        /// <summary>How often the [QuestPerf] line is written, seconds.</summary>
        public const float PerfEverySec = 5f;

        /// <summary>Log a frame-time line every <see cref="PerfEverySec"/> s so a logcat capture carries real numbers
        /// (docs/quest-performance.md): the frame rate, the worst frame and how many frames ran long, the CPU / GPU
        /// times, garbage collections and the managed heap, how often the sim had to give up catching up, and the
        /// runtime's own performance notices. Timings only — never where the aeroplane is.</summary>
        private void PerfLog()
        {
            float dt = Time.unscaledDeltaTime;
            _perfT += dt; _perfFrames++;
            if (dt > _perfFrameMax) _perfFrameMax = dt;
            float hz = XrHeadset.CurrentRefreshHz;
            if (hz > 1f && dt > 1.5f / hz) _perfLong++;
            if (FrameTimingManager.IsFeatureEnabled())
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                {
                    float cpu = (float)_timing[0].cpuMainThreadFrameTime;
                    _perfCpuMs += cpu; _perfCpuMax = Mathf.Max(_perfCpuMax, cpu);
                }
            }
            if (_perfGc0 < 0) { _perfGc0 = System.GC.CollectionCount(0); _perfCapped0 = FlightSimDriver.CatchUpCappedFrames; }
            if (_perfT < PerfEverySec) return;
            XRDisplaySubsystem d = XrHeadset.Display;
            string gpu = d != null && d.TryGetAppGPUTimeLastFrame(out float g) ? $"{g * 1000f:F1}" : "n/a";
            string comp = d != null && d.TryGetCompositorGPUTimeLastFrame(out float cg) ? $"{cg * 1000f:F1}" : "n/a";
            string dropped = "n/a";
            if (d != null && d.TryGetDroppedFrameCount(out int df))
            {
                dropped = _perfDropped0 < 0 ? "0" : (df - _perfDropped0).ToString();
                _perfDropped0 = df;
            }
            string cpuMain = _perfCpuMs > 0f ? $"{_perfCpuMs / _perfFrames:F1} (max {_perfCpuMax:F1})" : "n/a";
            int gcNow = System.GC.CollectionCount(0), capped = FlightSimDriver.CatchUpCappedFrames;
            long heapMb = System.GC.GetTotalMemory(false) / (1024 * 1024);
            string notice = XrHeadset.PerformanceNotice.Length > 0 ? XrHeadset.PerformanceNotice : "none";
            Debug.Log($"[QuestPerf] {_perfFrames / _perfT:F1} fps at {hz:F0} Hz | frame max {_perfFrameMax * 1000f:F1} ms, {_perfLong} long | " +
                      $"cpu main {cpuMain} ms | gpu app {gpu} ms | compositor {comp} ms | dropped {dropped} in {_perfT:F0}s | " +
                      $"gc +{gcNow - _perfGc0} heap {heapMb} MB | sim capped {capped - _perfCapped0} | " +
                      $"scale {XRSettings.eyeTextureResolutionScale:F2} fov-level {XrHeadset.FoveationLevel:F2} | runtime {notice} | {QuestRecorder.PerfTag}");
            _perfT = 0f; _perfFrames = 0; _perfCpuMs = 0f; _perfCpuMax = 0f; _perfFrameMax = 0f; _perfLong = 0;
            _perfGc0 = gcNow; _perfCapped0 = capped;
        }

        private readonly FrameTiming[] _timing = new FrameTiming[1];

        private static Vec3 ToCore(Vector3 v) => new(v.x, v.y, v.z);
        private static Quat ToCore(Quaternion q) => new(q.x, q.y, q.z, q.w);
    }
}

using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The stereo rig: two cameras half an IPD either side of the ACTIVE SEAT'S authored eye, pointed by the
    /// phone's own attitude, each rendering to its own target, composited side by side.
    ///
    /// Lives beside <see cref="ChaseCamera"/> on the main camera and takes over from it while VR is on: the
    /// main camera stops drawing to the screen and becomes the head, carrying the two eyes.
    ///
    /// WHAT CODEX OWNS. Everything here renders UNDISTORTED. The seam is <see cref="IVrCompositor"/>: this
    /// hands it the two eye textures and the lens profile, and whatever it puts on the screen is what the pilot
    /// sees. <see cref="VrCompositorSideBySide"/> is the plain version shipped here so the rig works end to end;
    /// Codex replaces or subclasses it to add the barrel-distortion correction and the chromatic-aberration
    /// pass without touching the rig.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VrRig : MonoBehaviour
    {
        /// <summary>Degrees the cockpit view already looks down; the head is measured on top of it, exactly as
        /// the mono cockpit camera does. Taken from the ChaseCamera on the same object at Awake, so the two
        /// cannot drift apart — it is a per-instance field there, not a constant.</summary>
        public float LookDownDeg;

        private Camera _main;
        private ChaseCamera _chase;
        private Camera _left, _right;
        private RenderTexture _leftRt, _rightRt;
        private IVrCompositor _compositor;
        private readonly VrHead _head = new();
        private bool _running;
        private ScreenOrientation _savedOrientation;
        private bool _savedAutoLandscapeLeft, _savedAutoLandscapeRight, _savedAutoPortrait, _savedAutoPortraitDown;
        private int _savedSleep;

        private void Awake() => EnsureRefs();

        private void OnEnable()
        {
            VrMode.Changed += Sync;
            VrMode.RecentreRequested += RecentreNow;
            Sync();
        }

        private void OnDisable()
        {
            VrMode.Changed -= Sync;
            VrMode.RecentreRequested -= RecentreNow;
            Stop();
        }

        private void Sync()
        {
            if (VrMode.On && !_running) Start_();
            else if (!VrMode.On && _running) Stop();
        }

        // ---- going in and coming out ----

        /// <summary>Awake does not run in edit mode, so anything that needs these resolves them itself.</summary>
        private void EnsureRefs()
        {
            if (_main == null) _main = GetComponent<Camera>();
            if (_chase == null) _chase = GetComponent<ChaseCamera>();
            if (_chase != null && LookDownDeg == 0f) LookDownDeg = _chase.CockpitLookDownDeg;
        }

        private void Start_()
        {
            EnsureRefs();
            if (_main == null) { Debug.LogWarning("[VR] no camera on this object; the rig cannot start."); return; }
            _running = true;
            EnableGyro(true);
            RecentreNow();

            // A phone in a headset is landscape-left, charge port to the pilot's right, and must not rotate or
            // sleep. Everything is put back the way it was on the way out.
            _savedOrientation = Screen.orientation;
            _savedAutoLandscapeLeft = Screen.autorotateToLandscapeLeft;
            _savedAutoLandscapeRight = Screen.autorotateToLandscapeRight;
            _savedAutoPortrait = Screen.autorotateToPortrait;
            _savedAutoPortraitDown = Screen.autorotateToPortraitUpsideDown;
            _savedSleep = Screen.sleepTimeout;
            Screen.autorotateToPortrait = Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            BuildEyes();
            _main.enabled = false;        // the main camera becomes the head; the eyes do the drawing
            Debug.Log($"[VR] on — IPD {VrMode.IpdM * 1000f:F0} mm, lens inter-lens {VrMode.Lens.InterLensDistanceM * 1000f:F1} mm, " +
                      $"render scale {VrMode.Lens.RenderScale:F2}, eye target {_leftRt.width}x{_leftRt.height}");
        }

        private void Stop()
        {
            if (!_running) return;
            _running = false;
            EnableGyro(false);
            _head.Forget();
            if (_main != null) _main.enabled = true;
            Screen.autorotateToLandscapeLeft = _savedAutoLandscapeLeft;
            Screen.autorotateToLandscapeRight = _savedAutoLandscapeRight;
            Screen.autorotateToPortrait = _savedAutoPortrait;
            Screen.autorotateToPortraitUpsideDown = _savedAutoPortraitDown;
            Screen.orientation = _savedOrientation;
            Screen.sleepTimeout = _savedSleep;
            DestroyEyes();
            Debug.Log("[VR] off — the mono view is back exactly as it was.");
        }

        /// <summary>The screen the rig sizes itself to. Normally the real one; the shot tool overrides it so a
        /// 2266 x 1488 picture is RENDERED at 2266 x 1488 with that device's own physical screen, instead of
        /// being upscaled from whatever window a batch run happens to have (640 x 480, which is how the first
        /// shots came out soft and far too narrow).</summary>
        public void SetScreenForTesting(int widthPx, int heightPx, float widthM, float heightM)
        {
            _screenPx = new Vector2Int(widthPx, heightPx);
            _screenM = new Vector2(widthM, heightM);
            _screenOverridden = true;
        }

        private Vector2Int _screenPx;
        private Vector2 _screenM;
        private bool _screenOverridden;

        private int ScreenW => _screenOverridden ? _screenPx.x : Screen.width;
        private int ScreenH => _screenOverridden ? _screenPx.y : Screen.height;

        /// <summary>The screen in METRES, which is what the lens geometry needs. Unity gives dpi; where it is
        /// not reported, 326 dpi (a Retina phone) is the stand-in.</summary>
        private Vector2 ScreenMetres
        {
            get
            {
                if (_screenOverridden) return _screenM;
                float dpi = Screen.dpi > 1f ? Screen.dpi : 326f;
                return new Vector2(Screen.width / dpi * 0.0254f, Screen.height / dpi * 0.0254f);
            }
        }

        private void BuildEyes()
        {
            VrLensConfig lens = VrMode.Lens;
            (int w, int h) = VrOptics.EyeTargetPixels(lens, ScreenW, ScreenH);
            _leftRt = new RenderTexture(w, h, 24) { name = "VR Left", antiAliasing = 1 };
            _rightRt = new RenderTexture(w, h, 24) { name = "VR Right", antiAliasing = 1 };
            _left = MakeEye("VR Eye Left", _leftRt, false, lens);
            _right = MakeEye("VR Eye Right", _rightRt, true, lens);
            _compositor = GetComponent<IVrCompositor>();
            if (_compositor == null)
            {
                _compositor = gameObject.AddComponent<VrCompositorCardboard>();
                _compositorIsOurs = true;      // only tear down the stand-in; Codex's own stays put
            }
            _compositor.Configure(lens, _leftRt, _rightRt);
        }

        private Camera MakeEye(string name, RenderTexture target, bool rightEye, VrLensConfig lens)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            Camera c = go.AddComponent<Camera>();
            c.CopyFrom(_main);
            c.targetTexture = target;
            c.rect = new Rect(0, 0, 1, 1);
            c.depth = _main.depth - 1;
            Vector2 m = ScreenMetres;
            var (l, r, b, t) = VrOptics.EyeFovDeg(lens, m.x, m.y);
            if (rightEye) (l, r) = (r, l);                      // the right eye is the left one mirrored
            c.projectionMatrix = Frustum(l, r, b, t, c.nearClipPlane, c.farClipPlane);
            return c;
        }

        /// <summary>An asymmetric frustum from four half-angles in degrees.</summary>
        private static Matrix4x4 Frustum(double leftDeg, double rightDeg, double bottomDeg, double topDeg, float near, float far)
        {
            float l = -Mathf.Tan((float)leftDeg * Mathf.Deg2Rad) * near;
            float r = Mathf.Tan((float)rightDeg * Mathf.Deg2Rad) * near;
            float b = -Mathf.Tan((float)bottomDeg * Mathf.Deg2Rad) * near;
            float t = Mathf.Tan((float)topDeg * Mathf.Deg2Rad) * near;
            return Matrix4x4.Frustum(l, r, b, t, near, far);
        }

        private bool _compositorIsOurs;

        private void DestroyEyes()
        {
            foreach (Camera c in new[] { _left, _right }) if (c != null) Gone(c.gameObject);
            foreach (RenderTexture rt in new[] { _leftRt, _rightRt }) if (rt != null) rt.Release();
            _left = _right = null; _leftRt = _rightRt = null;
            if (_compositorIsOurs && _compositor is Component comp && comp != null) Gone(comp);
            _compositorIsOurs = false;
            _compositor = null;
        }

        /// <summary>Destroy that also works in edit mode — the shot tool tears the rig down from an editor
        /// batch, where plain Destroy is an error rather than a delete.</summary>
        private static void Gone(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ---- every frame ----

        private void LateUpdate()
        {
            if (!_running || _left == null) return;
            if (TryAttitude(out Quaternion q)) _head.FromDevice(ToCore(q));
            RecentreGesture();

            PlaceEyes((float)_head.YawDeg, (float)_head.PitchDeg, (float)_head.RollDeg);
        }

        /// <summary>Put the head and the two eyes where the given look says. The ONE placement routine: the
        /// live gyro path and the shot tool's fixed poses both come through here, so a screenshot cannot show a
        /// rig the game never builds.</summary>
        public void PlaceEyes(float yawDeg, float pitchDeg, float rollDeg)
        {
            EnsureRefs();
            if (_left == null || _right == null) return;
            // The airframe's own frame, the seat's own eye: exactly what the mono cockpit camera uses.
            CockpitView cockpit = _chase != null ? _chase.Cockpit : null;
            Transform air = _chase != null ? _chase.Target : null;
            if (air == null || cockpit == null) return;

            Vector3 seatEye = cockpit.EyeLocal;
            // Head on top of the cockpit's own look-down, with ROLL — the axis the mono view does not have.
            Quaternion head = Quaternion.Euler(LookDownDeg - pitchDeg, yawDeg, -rollDeg);
            transform.SetPositionAndRotation(air.TransformPoint(seatEye), air.rotation * head);

            // The baseline rides the head, so rolling tilts it (VrStereoTests.HeadRollTiltsTheBaseline).
            Vector3 right = air.InverseTransformDirection(transform.right);
            (Vec3 l, Vec3 r) = VrStereoRig.EyePositions(
                new Vec3(seatEye.x, seatEye.y, seatEye.z), new Vec3(right.x, right.y, right.z), VrMode.IpdM);
            _left.transform.position = air.TransformPoint(new Vector3((float)l.X, (float)l.Y, (float)l.Z));
            _right.transform.position = air.TransformPoint(new Vector3((float)r.X, (float)r.Y, (float)r.Z));
            _left.transform.rotation = _right.transform.rotation = transform.rotation;
        }

        /// <summary>
        /// RECENTRE IN A HEADSET. The phone is behind a lens and the only control the Cardboard v2 has is its
        /// trigger, which is a capacitive lever that lands as an ordinary screen touch — so any touch would
        /// recentre, and a pilot resting a hand on the case would re-zero his world. A LONG PRESS (0.6 s) is
        /// used instead: deliberate, reachable blind through the trigger, and nothing else in VR reads touches.
        /// Options has a menu action for the same thing.
        /// </summary>
        private void RecentreGesture()
        {
            bool held = Input.GetMouseButton(0) || Input.touchCount > 0;
            if (!held) { _touchHeld = 0f; return; }
            _touchHeld += Time.unscaledDeltaTime;
            if (_touchHeld >= RecentreHoldSeconds) { _touchHeld = -999f; RecentreNow(); }
        }

        public const float RecentreHoldSeconds = 0.6f;
        private float _touchHeld;

        private void RecentreNow()
        {
            if (TryAttitude(out Quaternion q)) _head.Recentre(ToCore(q));
            else _head.Forget();
        }

        // ---- the gyro, exactly as CockpitView reads it ----

        private static void EnableGyro(bool on)
        {
            if (SystemInfo.supportsGyroscope) Input.gyro.enabled = on;
        }

        private static bool TryAttitude(out Quaternion q)
        {
            q = Quaternion.identity;
            if (!SystemInfo.supportsGyroscope || !Input.gyro.enabled) return false;
            q = Input.gyro.attitude;
            return true;
        }

        /// <summary>Unity's quaternion into the sim's, component for component — CoreMotion's own right-handed
        /// numbers, which is the frame <see cref="VrHead"/> works in. See CockpitView.ToCore for why this is
        /// deliberately NOT converted to Unity's left-handed frame.</summary>
        private static Quat ToCore(Quaternion q) => new Quat(q.x, q.y, q.z, q.w);

        /// <summary>
        /// Build or tear down the rig WITHOUT Unity's messages. An editor batch runs in edit mode, where Awake
        /// and OnEnable never fire on an ordinary MonoBehaviour, so a tool that adds this component and flips
        /// VrMode.On gets nothing at all — which is exactly what the first run of the shot tool reported.
        /// </summary>
        public void SetActiveForTesting(bool on)
        {
            if (on) { if (!_running) Start_(); }
            else Stop();
        }

        /// <summary>For the editor shot tool: put the head at an exact pose, with no gyro in the room.</summary>
        public void SetHeadForTesting(float yawDeg, float pitchDeg, float rollDeg) => PlaceEyes(yawDeg, pitchDeg, rollDeg);

        /// <summary>The live compositor, so the shot tool renders the SAME composite the pilot sees — and so
        /// Codex's distortion pass appears in the screenshots without the tool being touched.</summary>
        public IVrCompositor Compositor => _compositor;

        /// <summary>The two eye targets, for the shot tool and for Codex's compositor.</summary>
        public RenderTexture LeftTarget => _leftRt;
        public RenderTexture RightTarget => _rightRt;
        public Camera LeftEye => _left;
        public Camera RightEye => _right;
    }
}

using FlyingGame.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// TV MODE (Ty, 2026-11-04: "when a second screen (AirPlay or HDMI) connects, the game renders full size on the TV and
    /// the phone becomes a full-screen touch controller"). docs/TV-COMPANION-PLAN.md has the whole design; in short:
    ///
    ///   • The TV shows what a passenger sees: the main camera's picture (the world, the HUD's GL lines, the flight-path
    ///     vector, tracers) and the instruments. The phone shows everything you touch: TouchFlightControls, the toolbar,
    ///     the menus and the text lines, laid out for the phone exactly as in handheld play.
    ///   • The main camera renders a TV-sized texture. In the Repaint pass the instruments are drawn into that texture the
    ///     way ClipRecorder already draws them into a clip (RenderTexture.active + GL.LoadPixelMatrix), inside a
    ///     <see cref="UiSurface"/> bracket so they lay themselves out for the TV's shape.
    ///   • OnGUI runs after every camera, so the texture is only complete at the end of the frame: two textures
    ///     ping-pong, and a presenter camera on display 1 shows the one finished last frame (one frame, 16 ms).
    ///   • A backdrop camera clears the phone, which no world camera draws on any more.
    ///
    /// MIRROR (Options) leaves the TV to iOS, which mirrors the phone letterboxed: nothing here runs. Unity has no
    /// Display.Deactivate, so going from GAME ON TV to MIRROR while connected asks Plugins/iOS/AeroTv.mm to hide Unity's
    /// TV window. iOS only; on the Mac, the Quest and in the Editor <see cref="Supported"/> is false and nothing is ever
    /// created, except under <see cref="SimulateForTest"/>, which runs the same pipeline into a capture texture.
    /// </summary>
    public static class TvOutput
    {
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void AeroTv_SetExternalShown(int shown);
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int AeroTv_ExternalSceneCount();
        public static readonly bool Supported = true;
#else
        private static void AeroTv_SetExternalShown(int shown) { }
        private static int AeroTv_ExternalSceneCount() => 0;
        public static readonly bool Supported = false;
#endif
        private const string SettingKey = "tv.setting";

        /// <summary>Options › TV SCREEN. Saved; GAME ON TV is 0, so a device that never chose gets it (plan Q1).</summary>
        public static TvSetting Setting
        {
            get => PlayerPrefs.GetInt(SettingKey, (int)TvSetting.GameOnTv) == (int)TvSetting.Mirror ? TvSetting.Mirror : TvSetting.GameOnTv;
            set { PlayerPrefs.SetInt(SettingKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>What the screens are doing this frame.</summary>
        public static TvMode Mode { get; private set; } = TvMode.Handheld;

        /// <summary>The world and the instruments are on the TV, and the phone is the controller.</summary>
        public static bool Active => Mode == TvMode.GameOnTv && _runner != null && _runner.Ready;

        /// <summary>A TV is connected, in either mode: Options shows the TV SCREEN choice only then.</summary>
        public static bool TvConnected => Mode != TvMode.Handheld;

        /// <summary>The TV picture's size in px (valid while <see cref="Active"/>).</summary>
        public static int Width { get; private set; }
        public static int Height { get; private set; }

        /// <summary>For the phone's status line and Options.</summary>
        public static string StatusText => Mode switch
        {
            TvMode.GameOnTv => Active ? $"Showing on the TV · {Width} × {Height}" : "Starting the TV…",
            TvMode.Mirror => "Mirroring to the TV",
            _ => "",
        };

        // ---- test hook ----------------------------------------------------------------------------------------------

        /// <summary>PlayMode tests: behave as though a TV of <see cref="SimWidth"/> × <see cref="SimHeight"/> were connected
        /// and present into <see cref="SimDisplay"/> instead of display 1 (the Editor has one display).</summary>
        public static bool SimulateForTest;
        public static int SimWidth = 1920, SimHeight = 1080;
        public static RenderTexture SimDisplay { get; private set; }
        /// <summary>PlayMode tests: how many Repaint passes have drawn into the TV picture.</summary>
        public static int TvPassesForTest { get; private set; }
        /// <summary>PlayMode tests: drawn into the TV picture in the TV pass, after the instruments (in TV pixels).</summary>
        public static System.Action DrawInTvPassForTest;
        /// <summary>PlayMode tests: render the TV's cameras now. A -batchmode Editor has no Game view to drive them.</summary>
        public static void RenderFrameForTest() => _runner?.RenderNow();
        /// <summary>PlayMode tests: start the runner (on a device it starts itself after the first scene).</summary>
        public static void EnsureRunnerForTest() { if (_runner == null) CreateRunner(); }
        /// <summary>PlayMode tests: tear everything down.</summary>
        public static void ShutdownForTest()
        {
            SimulateForTest = false;
            if (_runner != null) { _runner.Leave(); Object.DestroyImmediate(_runner.gameObject); }
            _runner = null; Mode = TvMode.Handheld;
        }

        // ---- runner ---------------------------------------------------------------------------------------------------

        private static Runner _runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Supported) return;
            CreateRunner();
            foreach (var d in Display.displays) Debug.Log($"[TV] display {d.systemWidth}×{d.systemHeight} active {d.active}");
            Display.onDisplaysUpdated += () => Debug.Log($"[TV] displays updated: {Display.displays.Length} · scenes {AeroTv_ExternalSceneCount()}");
        }

        private static void CreateRunner()
        {
            var go = new GameObject("TvOutput") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
            go.AddComponent<TvButtonPad>();   // TV + game controller: the phone becomes a button grid
        }

        // After ChaseCamera has placed the main camera and set its viewport, and after ClipRecorder has copied it.
        [DefaultExecutionOrder(3000)]
        private sealed class Runner : MonoBehaviour
        {
            private Camera _main, _presenter, _backdrop;
            private RenderTexture _front, _back;
            private CommandBuffer _blit;
            private AnalogGauges _gauges;
            private HudOverlay _hud;
            private bool _shownHidden;   // we hid Unity's TV window for MIRROR and must show it again for GAME ON TV
            private float _retryAt;

            public bool Ready => _back != null && _presenter != null;

            private void LateUpdate()
            {
                int displays = SimulateForTest ? 2 : Display.displays.Length;
                TvMode want = TvModeLogic.Decide(Supported || SimulateForTest, displays, Setting, Widget.AeroWidget.IsWidget);
                if (want == TvMode.GameOnTv && Time.unscaledTime < _retryAt) want = TvMode.Mirror;   // the last start failed
                if (want != Mode)
                {
                    Debug.Log($"[TV] {Mode} → {want} · displays {displays} · setting {Setting}");
                    if (Mode == TvMode.GameOnTv) Leave();
                    if (want == TvMode.Mirror && !SimulateForTest && displays > 1 && Display.displays[1].active) { AeroTv_SetExternalShown(0); _shownHidden = true; }
                    Mode = want;
                    if (want == TvMode.GameOnTv && !Enter())
                    {
                        Leave();
                        Mode = TvMode.Mirror;   // couldn't start: leave the TV mirroring and try again in a moment
                        _retryAt = Time.unscaledTime + 2f;
                    }
                }
                if (Mode == TvMode.GameOnTv) Frame();
            }

            private bool Enter()
            {
                int w, h;
                if (SimulateForTest) { w = SimWidth; h = SimHeight; }
                else
                {
                    Display d = Display.displays[1];
                    if (!d.active) d.Activate();
                    else if (_shownHidden) AeroTv_SetExternalShown(1);
                    _shownHidden = false;
                    (w, h) = TvResolution.Choose(d.systemWidth, d.systemHeight);
                    if (w <= 0) return false;
                    d.SetRenderingResolution(w, h);
                    Debug.Log($"[TV] game on the TV: native {d.systemWidth}×{d.systemHeight}, rendering {w}×{h}");
                }
                Width = w; Height = h;
                _front = MakeTexture("TvFront", w, h);
                _back = MakeTexture("TvBack", w, h);
                if (SimulateForTest) SimDisplay = MakeTexture("TvSimDisplay", w, h, 1);   // (read back by tests: no MSAA)

                _presenter = new GameObject("TvPresenter").AddComponent<Camera>();
                _presenter.transform.SetParent(transform, false);
                _presenter.cullingMask = 0;
                _presenter.clearFlags = CameraClearFlags.SolidColor;
                _presenter.backgroundColor = Color.black;
                _presenter.depth = 100f;
                _presenter.useOcclusionCulling = false;
                if (SimulateForTest) _presenter.targetTexture = SimDisplay; else _presenter.targetDisplay = 1;
                _blit = new CommandBuffer { name = "TV present" };
                _presenter.AddCommandBuffer(CameraEvent.AfterEverything, _blit);

                // The phone: no world camera draws there now, so something has to clear it under the controls.
                _backdrop = new GameObject("TvPhoneBackdrop").AddComponent<Camera>();
                _backdrop.transform.SetParent(transform, false);
                _backdrop.cullingMask = 0;
                _backdrop.clearFlags = CameraClearFlags.SolidColor;
                _backdrop.backgroundColor = PhoneBackdrop;
                _backdrop.depth = -100f;
                _backdrop.useOcclusionCulling = false;
                _backdrop.targetDisplay = 0;
                return true;
            }

            /// <summary>Back to handheld: the main camera draws the phone again, and nothing of ours is left.</summary>
            public void Leave()
            {
                if (_main != null) _main.targetTexture = null;
                _main = null;
                if (_presenter != null) Destroy(_presenter.gameObject);
                if (_backdrop != null) Destroy(_backdrop.gameObject);
                _presenter = _backdrop = null;
                _blit?.Release(); _blit = null;
                Free(ref _front); Free(ref _back);
                var sim = SimDisplay; Free(ref sim); SimDisplay = null;
                Width = Height = 0;
            }

            private void Frame()
            {
                if (!Ready) return;
                Camera main = Camera.main;
                if (main != _main)
                {
                    if (_main != null) _main.targetTexture = null;   // a scene change left the old one behind
                    _main = main;
                    _gauges = main != null ? main.GetComponent<AnalogGauges>() : null;
                    _hud = main != null ? main.GetComponent<HudOverlay>() : null;
                }
                // Last frame's picture is finished (world + instruments): show it, and let the camera draw the next.
                (_front, _back) = (_back, _front);
                _blit.Clear();
                _blit.Blit(_front, BuiltinRenderTextureType.CameraTarget);
                if (_main != null)
                {
                    _main.targetTexture = _back;
                    _main.rect = new Rect(0f, 0f, 1f, 1f);
                }
            }

            public void RenderNow()
            {
                if (!Ready) return;
                if (_main != null) _main.Render();
                _presenter.Render();
            }

            private void OnGUI()
            {
                if (!Active) return;
                if (Event.current.type == EventType.Repaint) DrawTvPass();
                DrawPhoneStatus();   // every event, so the text stack hands out the same lines in Layout and Repaint
            }

            /// <summary>
            /// The instruments into the TV picture: the same drawing code the phone runs, laid out for the TV
            /// (<see cref="UiSurface"/>). IMGUI clips to the phone's screen, which in portrait is narrower than a TV, so the
            /// GUI is scaled by k ≤ 1 to fit the phone's rect and the pixel matrix scales it back up to the TV's pixels; in
            /// landscape k is 1 and this is exactly ClipRecorder's pass.
            /// </summary>
            private void DrawTvPass()
            {
                float k = Mathf.Min(1f, Mathf.Min(Screen.width / (float)Width, Screen.height / (float)Height));
                RenderTexture prev = RenderTexture.active;
                Matrix4x4 keepMatrix = GUI.matrix;
                Color keepColor = GUI.color;
                RenderTexture.active = _back;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, Width * k, Height * k, 0f);
                UiSurface.Begin(Width, Height);
                try
                {
                    GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
                    if (_gauges != null) _gauges.DrawForTv();
                    GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
                    if (_hud != null) _hud.DrawForTv(Width, Height);
                    DrawInTvPassForTest?.Invoke();
                    TvPassesForTest++;
                }
                finally
                {
                    UiSurface.End();
                    GUI.matrix = keepMatrix;
                    GUI.color = keepColor;
                    GL.PopMatrix();
                    RenderTexture.active = prev;
                }
            }

            private GUIStyle _status;

            /// <summary>One quiet line on the phone so it is obvious where the picture went.</summary>
            private void DrawPhoneStatus()
            {
                if (SessionSettings.MenuOpen) return;
                _status ??= new GUIStyle { font = UiFont.Get(), alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.55f) } };
                _status.fontSize = UiLayout.CaptionText;
                UiLayout.Label(UiLayout.NextLine(UiLayout.CaptionText * 1.5f), StatusText, _status);
            }

            private void OnDestroy() => Leave();

            private static RenderTexture MakeTexture(string name, int w, int h, int msaa = 0)
            {
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                { name = name, antiAliasing = msaa > 0 ? msaa : Mathf.Max(1, QualitySettings.antiAliasing) };
                rt.Create();
                return rt;
            }

            private static void Free(ref RenderTexture rt)
            {
                if (rt == null) return;
                rt.Release();
                Destroy(rt);
                rt = null;
            }
        }

        /// <summary>The phone's background in TV mode: the controls' own dark, not black (a black phone reads as "off").</summary>
        public static readonly Color PhoneBackdrop = new Color(0.07f, 0.08f, 0.10f, 1f);
    }
}

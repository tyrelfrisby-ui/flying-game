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
    /// TV window. iPhone and iPad alike (Ty 2026-11-04, Q2: "GAME ON TV applies to the iPad too when AirPlayed").
    ///
    /// SECOND MONITOR (Mac, Ty 2026-11-04, Q8: "instruments on a second monitor"): the same runner, with the world left in
    /// the game's window and only the instruments drawn into the monitor's picture, laid out on a square reference
    /// surface and scaled to fill it (Core MonitorPanel). Off unless Options › SECOND MONITOR asks for it.
    ///
    /// On the Quest and in the Editor nothing is ever created, except under <see cref="SimulateForTest"/>, which runs
    /// the same pipeline into a capture texture.
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
        /// <summary>The Mac app: SECOND MONITOR is offered and the runner starts.</summary>
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        public static readonly bool MonitorSupported = true;
#else
        public static readonly bool MonitorSupported = false;
#endif
        /// <summary>What this device does with a second screen (Core TvModeLogic). An iPad is told apart from an iPhone
        /// only for the log and Options' wording: the mode table treats them the same (Q2).</summary>
        public static TvDevice Device => _device ??=
            Supported ? (SystemInfo.deviceModel.StartsWith("iPad", System.StringComparison.Ordinal) ? TvDevice.IPad : TvDevice.IPhone)
            : MonitorSupported ? TvDevice.Mac
            : Application.platform == RuntimePlatform.Android ? TvDevice.Quest : TvDevice.Other;
        private static TvDevice? _device;

        private const string SettingKey = "tv.setting";
        private const string MonitorKey = "tv.monitor";

        /// <summary>Options › SECOND MONITOR (Mac). Saved; OFF is 0, so a Mac that never chose keeps its monitor.</summary>
        public static MonitorSetting Monitor
        {
            get => PlayerPrefs.GetInt(MonitorKey, (int)MonitorSetting.Off) == (int)MonitorSetting.Instruments ? MonitorSetting.Instruments : MonitorSetting.Off;
            set { PlayerPrefs.SetInt(MonitorKey, (int)value); PlayerPrefs.Save(); }
        }

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

        /// <summary>The Mac's second monitor is showing the instruments; the game's window keeps the world.</summary>
        public static bool MonitorActive => Mode == TvMode.InstrumentsOnMonitor && _runner != null && _runner.Ready;

        /// <summary>The dials are drawn somewhere else (the TV or the second monitor), so not on this screen.</summary>
        public static bool InstrumentsElsewhere => Active || MonitorActive;

        /// <summary>A TV is connected, in either mode.</summary>
        public static bool TvConnected => Mode == TvMode.GameOnTv || Mode == TvMode.Mirror;

        /// <summary>A second display is there at all (any mode, any device): the radio keeps off it (VoiceComms, Q7).</summary>
        public static bool ExternalDisplay => SimulateForTest || ((Supported || MonitorSupported) && Display.displays.Length > 1);

        /// <summary>The TV picture's size in px (valid while <see cref="Active"/>).</summary>
        public static int Width { get; private set; }
        public static int Height { get; private set; }

        /// <summary>For the phone's status line and Options.</summary>
        public static string StatusText => Mode switch
        {
            TvMode.GameOnTv => Active ? $"Showing on the TV · {Width} × {Height}" : "Starting the TV…",
            TvMode.Mirror => "Mirroring to the TV",
            TvMode.InstrumentsOnMonitor => MonitorActive ? $"Instruments on the second monitor · {Width} × {Height}" : "Starting the second monitor…",
            _ => "",
        };

        // ---- test hook ----------------------------------------------------------------------------------------------

        /// <summary>PlayMode tests: behave as though a TV of <see cref="SimWidth"/> × <see cref="SimHeight"/> were connected
        /// and present into <see cref="SimDisplay"/> instead of display 1 (the Editor has one display).</summary>
        public static bool SimulateForTest;
        public static int SimWidth = 1920, SimHeight = 1080;
        /// <summary>PlayMode tests: the device being simulated (Mac → the second-monitor path).</summary>
        public static TvDevice SimDevice = TvDevice.IPhone;
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
            SimDevice = TvDevice.IPhone;
            if (_runner != null) { _runner.Leave(); Object.DestroyImmediate(_runner.gameObject); }
            _runner = null; Mode = TvMode.Handheld;
        }

        // ---- runner ---------------------------------------------------------------------------------------------------

        private static Runner _runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Supported && !MonitorSupported) return;
            CreateRunner();
            Debug.Log($"[TV] {Device} ({SystemInfo.deviceModel}) · TV screen {Setting} · second monitor {Monitor}");
            foreach (var d in Display.displays) Debug.Log($"[TV] display {d.systemWidth}×{d.systemHeight} active {d.active}");
            Display.onDisplaysUpdated += () => Debug.Log($"[TV] displays updated: {Display.displays.Length} · scenes {AeroTv_ExternalSceneCount()}");
        }

        private static void CreateRunner()
        {
            var go = new GameObject("TvOutput") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
            if (!MonitorSupported || SimulateForTest) go.AddComponent<TvButtonPad>();   // TV + game controller: the phone becomes a button grid
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
            private bool _retargeted;    // the main camera draws into our texture (GAME ON TV)
            private Camera _blank;       // Mac: a monitor Unity activated keeps its window until the app quits; keep it black
            private bool _monitorActivated;

            public bool Ready => _back != null && _presenter != null;

            private void LateUpdate()
            {
                int displays = SimulateForTest ? 2 : Display.displays.Length;
                TvDevice device = SimulateForTest ? SimDevice : Device;
                TvMode want = TvModeLogic.Decide(device, displays, Setting, Monitor, Widget.AeroWidget.IsWidget);
                if (Time.unscaledTime < _retryAt)   // the last start failed
                {
                    if (want == TvMode.GameOnTv) want = TvMode.Mirror;
                    else if (want == TvMode.InstrumentsOnMonitor) want = TvMode.Handheld;
                }
                if (want != Mode)
                {
                    Debug.Log($"[TV] {Mode} → {want} · displays {displays} · setting {Setting}");
                    if (Mode == TvMode.GameOnTv || Mode == TvMode.InstrumentsOnMonitor) Leave();
                    if (want == TvMode.Mirror && !SimulateForTest && displays > 1 && Display.displays[1].active) { AeroTv_SetExternalShown(0); _shownHidden = true; }
                    Mode = want;
                    bool ok = want == TvMode.GameOnTv ? Enter() : want == TvMode.InstrumentsOnMonitor ? EnterMonitor() : true;
                    if (!ok)
                    {
                        Leave();
                        // Couldn't start: leave the TV mirroring (the Mac's monitor alone) and try again in a moment.
                        Mode = want == TvMode.GameOnTv ? TvMode.Mirror : TvMode.Handheld;
                        _retryAt = Time.unscaledTime + 2f;
                    }
                }
                if (Mode == TvMode.GameOnTv || Mode == TvMode.InstrumentsOnMonitor) Frame();
                BlankUnusedMonitor(displays);
            }

            /// <summary>
            /// Mac, SECOND MONITOR: activate the monitor (a full-screen window of Unity's on it) and present the
            /// instrument picture there. No world camera is moved: the game's window keeps the flight.
            /// </summary>
            private bool EnterMonitor()
            {
                int w, h;
                if (SimulateForTest) { w = SimWidth; h = SimHeight; }
                else
                {
                    Display d = Display.displays[1];
                    if (!d.active) d.Activate();
                    _monitorActivated = true;
                    // Dials are cheap to draw, so a Mac monitor gets up to 4K rather than the phone's 1080p cap.
                    (w, h) = TvResolution.Choose(d.systemWidth, d.systemHeight, 3840);
                    if (w <= 0) return false;
                    Debug.Log($"[TV] instruments on the second monitor: native {d.systemWidth}×{d.systemHeight}, drawing {w}×{h}");
                }
                if (_blank != null) { Destroy(_blank.gameObject); _blank = null; }
                Width = w; Height = h;
                _front = MakeTexture("MonitorFront", w, h, 1);
                _back = MakeTexture("MonitorBack", w, h, 1);
                if (SimulateForTest) SimDisplay = MakeTexture("TvSimDisplay", w, h, 1);
                _presenter = MakePresenter();
                return true;
            }

            /// <summary>
            /// Unity has no Display.Deactivate: a Mac monitor it activated keeps Unity's full-screen window until the app
            /// quits. With SECOND MONITOR turned off that window shows plain black (not a frozen last frame), and Options
            /// says it is released at quit.
            /// </summary>
            private void BlankUnusedMonitor(int displays)
            {
                bool want = _monitorActivated && Mode != TvMode.InstrumentsOnMonitor && displays > 1 && !SimulateForTest;
                if (want && _blank == null)
                {
                    _blank = new GameObject("MonitorBlank").AddComponent<Camera>();
                    _blank.transform.SetParent(transform, false);
                    _blank.cullingMask = 0;
                    _blank.clearFlags = CameraClearFlags.SolidColor;
                    _blank.backgroundColor = Color.black;
                    _blank.useOcclusionCulling = false;
                    _blank.targetDisplay = 1;
                }
                else if (!want && _blank != null) { Destroy(_blank.gameObject); _blank = null; }
                if (displays < 2) _monitorActivated = false;   // unplugged: Unity dropped its window with it
            }

            /// <summary>Whether a monitor Unity activated is being kept black (Options' note).</summary>
            public bool MonitorHeld => _monitorActivated && Mode != TvMode.InstrumentsOnMonitor;

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

                _presenter = MakePresenter();

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

            /// <summary>A camera on display 1 (or the capture texture) that shows the finished picture.</summary>
            private Camera MakePresenter()
            {
                var cam = new GameObject("TvPresenter").AddComponent<Camera>();
                cam.transform.SetParent(transform, false);
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.depth = 100f;
                cam.useOcclusionCulling = false;
                if (SimulateForTest) cam.targetTexture = SimDisplay; else cam.targetDisplay = 1;
                _blit = new CommandBuffer { name = "TV present" };
                cam.AddCommandBuffer(CameraEvent.AfterEverything, _blit);
                return cam;
            }

            /// <summary>Back to handheld: the main camera draws the phone again, and nothing of ours is left.</summary>
            public void Leave()
            {
                if (_main != null && _retargeted) _main.targetTexture = null;   // (the Mac's monitor never moved it)
                _main = null; _retargeted = false;
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
                    if (_main != null && _retargeted) _main.targetTexture = null;   // a scene change left the old one behind
                    _main = main;
                    _gauges = main != null ? main.GetComponent<AnalogGauges>() : null;
                    _hud = main != null ? main.GetComponent<HudOverlay>() : null;
                }
                // Last frame's picture is finished (world + instruments): show it, and let the camera draw the next.
                (_front, _back) = (_back, _front);
                _blit.Clear();
                _blit.Blit(_front, BuiltinRenderTextureType.CameraTarget);
                if (Mode == TvMode.InstrumentsOnMonitor) return;   // the Mac's window keeps its camera
                if (_main != null)
                {
                    _retargeted = true;
                    _main.targetTexture = _back;
                    _main.rect = new Rect(0f, 0f, 1f, 1f);
                }
            }

            public void RenderNow()
            {
                if (!Ready) return;
                if (_main != null && Mode == TvMode.GameOnTv) _main.Render();
                _presenter.Render();
            }

            private void OnGUI()
            {
                if (MonitorActive) { if (Event.current.type == EventType.Repaint) DrawMonitorPass(); return; }
                if (!Active) return;
                if (Event.current.type == EventType.Repaint) DrawTvPass();
                DrawPhoneStatus();   // every event, so the text stack hands out the same lines in Layout and Repaint
            }

            /// <summary>
            /// Mac, SECOND MONITOR: the instruments laid out on a square surface (as the TV pass lays them out, with the
            /// engine row stacked under the flight row) and that block scaled uniformly to fill the monitor
            /// (Core MonitorPanel.Fit); the square's side is chosen so that scale is about 1 (MonitorPanel.SideFor). The GUI matrix shrinks the surface by g so it lies inside the game window's rect
            /// (IMGUI clips to the screen), and the pixel matrix maps the block onto the monitor's picture.
            /// </summary>
            private void DrawMonitorPass()
            {
                bool engine = _gauges != null && _gauges.HasEngine;
                // Lay out at the side that makes the final scale about 1 (crisp faces and text), from the reference fit.
                UiSurface.Begin((int)MonitorPanel.ReferenceSide, (int)MonitorPanel.ReferenceSide, panel: true);
                var reference = MonitorPanel.Block(MonitorPanel.ReferenceSide, UiLayout.ToolbarBottom, UiLayout.Gap, engine);
                UiSurface.End();
                int side = MonitorPanel.SideFor(MonitorPanel.Fit(reference, Width, Height).Scale);
                RenderTexture prev = RenderTexture.active;
                Matrix4x4 keepMatrix = GUI.matrix;
                Color keepColor = GUI.color;
                RenderTexture.active = _back;
                GL.Clear(true, true, PanelColor);
                GL.PushMatrix();
                UiSurface.Begin(side, side, panel: true);
                try
                {
                    var block = MonitorPanel.Block(side, UiLayout.ToolbarBottom, UiLayout.Gap, engine);
                    var (f, ox, oy) = MonitorPanel.Fit(block, Width, Height);
                    if (f > 0)
                    {
                        float g = Mathf.Min(1f, Mathf.Min(Screen.width, Screen.height) / (float)side);
                        float left = g * (float)(block.X - ox / f), right = g * (float)(block.X + (Width - ox) / f);
                        float top = g * (float)(block.Y - oy / f), bottom = g * (float)(block.Y + (Height - oy) / f);
                        GL.LoadPixelMatrix(left, right, bottom, top);
                        GUI.matrix = Matrix4x4.Scale(new Vector3(g, g, 1f));
                        if (_gauges != null) _gauges.DrawForPanel();
                        DrawInTvPassForTest?.Invoke();
                        TvPassesForTest++;
                    }
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

        /// <summary>Options: a Mac monitor is held black until quit (SECOND MONITOR turned off after it was on).</summary>
        public static bool MonitorHeld => _runner != null && _runner.MonitorHeld;

        /// <summary>The second monitor's background behind the dials: the cockpit panel's dark.</summary>
        public static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.12f, 1f);

        /// <summary>The phone's background in TV mode: the controls' own dark, not black (a black phone reads as "off").</summary>
        public static readonly Color PhoneBackdrop = new Color(0.07f, 0.08f, 0.10f, 1f);
    }
}

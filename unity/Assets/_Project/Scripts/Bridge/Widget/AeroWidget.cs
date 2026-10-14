using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// AERO WIDGET (owner 2026-10-07): a stripped-down Aero Playground for Glass Overlay to call on during a podcast — two
    /// fixed scenarios on a TRANSPARENT background, the aircraft and its physics exactly as the game has them:
    ///   FLARE — side view only: the aircraft side-on, a white line for the surface, lift / drag / weight / thrust,
    ///           the relative wind and α, wheel loads at the touch;
    ///   SPIN  — the aircraft alone, the camera following it: each wing's relative wind and α (one stalled, one not), each
    ///           wing's lift and drag, weight, the total aerodynamic force, the rotation axis.
    /// Output: a 1080 × 1080 frame with alpha, published as Syphon AND NDI ("Aero Widget"). Control: a TCP JSON port
    /// (Glass Overlay / its MCP layer), a phone/iPad web remote, a gamepad, the keyboard, and scripted presets. See
    /// docs/WIDGET.md for the protocol.
    /// </summary>
    public sealed class AeroWidget : AeroHost
    {
        public const int FrameSize = 1080;   // the frame's HEIGHT (and the width when square)
        /// <summary>The frame (2026-10-11, Glass: a 16:9 option so the widget fills the program): 1080 × 1080 ("square") or
        /// 1920 × 1080 ("16:9"), with alpha either way.</summary>
        public static int FrameW = FrameSize, FrameH = FrameSize;
        public string FrameMode => FrameW == FrameH ? "square" : "16:9";
        public void SetFrame(string mode)
        {
            int fw = mode == "16:9" ? 1920 : FrameSize;
            PlayerPrefs.SetString("frame.mode", fw == FrameSize ? "square" : "16:9"); PlayerPrefs.Save();
            if (fw == FrameW && Frame != null) return;
            FrameW = fw; FrameH = FrameSize;
            var old = Frame;
            Frame = new RenderTexture(FrameW, FrameH, 24, RenderTextureFormat.ARGB32) { name = "AeroWidgetFrame", antiAliasing = 4 };
            Frame.Create();
            if (Cam != null && (Cam.targetTexture == old || Cam.targetTexture == null)) Cam.targetTexture = Frame;
            if (old != null) { old.Release(); Destroy(old); }
            if (PanelW > 0) PlacePanel(PanelX, PanelY, PanelW, WidgetControlsDisplay.PanelAspectN);
            if (_outputs != null) _outputs.Rebind();
        }
        public const string StreamName = "Aero Widget";
        /// <summary>CAPTURE instance (AERO_WIDGET_CAPTURE=1; 2026-10-11, the Talk Flying shot renders): a second widget that
        /// leaves the presenter's alone — its own ports (AERO_WIDGET_PORT, the HTTP port; control = port − 1), no Syphon/NDI, no
        /// Bonjour, no joystick. Its own prefs come from its own bundle id (a copy of the app).</summary>
        /// <summary>A phase tag drawn big in the classic display ({"cmd":"caption","text":…}); null = none.</summary>
        public string Caption;
        /// <summary>A word above the airplane (owner 2026-10-12: "STALL" in red once the wing stalls), following it on screen.</summary>
        public string Tag; public Color TagColor = new(1f, 0.2f, 0.15f);
        public static readonly bool Capture = System.Environment.GetEnvironmentVariable("AERO_WIDGET_CAPTURE") == "1";

        /// <summary>The widget build (bundle id …aerowidget), or AERO_WIDGET=1 / -aerowidget for testing the game binary.</summary>
        public static bool IsWidget =>
            Application.identifier.EndsWith(".aerowidget") || System.Environment.GetEnvironmentVariable("AERO_WIDGET") == "1"
            || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aerowidget") >= 0;

        public static AeroWidget Create()
        {
            var go = new GameObject("AeroWidget");
            DontDestroyOnLoad(go);
            return go.AddComponent<AeroWidget>();
        }

        // ---- state ----
        public enum Scenario { Flare, Spin, Cruise }
        public Scenario Current { get; private set; } = Scenario.Flare;
        // (AircraftId lives in AeroHost)
        public double Flaps { get; private set; } = 1.0;
        public float TimeScale = 1f;
        public string View = "side";          // spin camera: side | behind | front | top | chase | locked
        /// <summary>Direction lock: what it was locked from (current | side | behind | front | top).</summary>
        public string ViewFrom = "";
        private Vector3 _lockDir = new(1f, 0.18f, -0.25f);
        private Vector3 _crowd;   // the crowd view's spectator (Unity world)
        private Vec3? _crowdNed;   // a spectator placed at a world point (true path frame), zoom = lens: the FLY-BY (Ty 2026-10-13)
        /// <summary>FLY-BY (Ty 2026-10-13: "the camera is fixed in space below and slightly off to the side of the airplane's
        /// trajectory in the spin … looking up at the aircraft then … horizontally … then pans down"): the crowd view's spectator
        /// at a given world point (north, east, down; the true path, the altitude lock's shift added when drawn), turning to
        /// follow the airplane; the zoom narrows the lens. view {"name":"crowd","crowdNed":[n,e,d]}.</summary>
        private double _propT = double.NaN;
        public void SetCrowd(Vec3 ned) { _crowdNed = ned; View = "crowd"; }
        public static readonly string[] Views = { "side", "behind", "front", "top", "chase", "locked", "body", "crowd", "orbit" };
        /// <summary>The start condition (protocol 5): "cruise" | "final" | "spin", or null for an old-style scenario start.</summary>
        public string Condition { get; private set; }
        /// <summary>FINAL's view is the flare's side view and can't be changed.</summary>
        public bool ViewFixed => false;   // (final had only the side view until 2026-10-11; now its 3D views fly over a real runway)
        public static readonly string[] FinalViews = { "side", "chase", "behind", "front", "top", "crowd" };
        public static readonly string[] Conditions = { "cruise", "final", "spin" };
        private double? _finalStartFt;
        public WorldTerrain.RunwayEnd Runway { get; private set; }
        private bool _snapCam;
        private float _lead;
        public static readonly string[] LockFrom = { "current", "side", "behind", "front", "top" };

        /// <summary>World-fixed direction (from the aircraft to the camera) of each named view.</summary>
        private static Vector3 Dir(string name) => name switch
        {
            "behind" => new Vector3(0f, 0.25f, -1f), "front" => new Vector3(0f, 0.2f, 1f), "top" => new Vector3(0.001f, 1f, 0f),
            _ => new Vector3(1f, 0.18f, -0.25f),   // side, a touch behind
        };

        /// <summary>Set the view (owner 2026-10-08: "locked" = the camera keeps the aircraft centred but its DIRECTION is fixed in
        /// the world — from the current camera direction, or a named one). Unknown names fall back to "side".</summary>
        public void SetView(string name, string from = null)
        {
            name = System.Array.IndexOf(Views, name) >= 0 ? name : "side";
            if (name == "locked")
            {
                from = System.Array.IndexOf(LockFrom, from ?? "current") >= 0 ? (from ?? "current") : "current";
                Vector3 d = Cam != null && Ac != null ? Cam.transform.position - CoordinateMap.ToUnity(Ac.State.Position) : Dir("side");
                _lockDir = from == "current" ? (d.sqrMagnitude > 1e-4f ? d.normalized : Dir("side").normalized) : Dir(from).normalized;
                ViewFrom = from;
            }
            else if (name == "body") ViewFrom = "left";   // the camera rigidly on the airframe, looking at its left side
            else if (name == "crowd" && Ac != null)
            {
                _crowdNed = null;
                // CROWD (owner 2026-10-11, the outro's smoke shots): a spectator on the ground's side of the box — fixed in the
                // world, abeam the airplane where it is now (the frame shown, in review), 450 m to its left and 250 m below,
                // turning to follow it. The whole smoke figure fits.
                var st = Shown?.State ?? Ac.State;
                Vector3 ac = CoordinateMap.ToUnity(st.Position), fwd = CoordinateMap.ToUnity(st.Attitude.Rotate(st.Velocity));
                fwd.y = 0; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
                Vector3 left = Vector3.Cross(fwd, Vector3.up).normalized;
                _crowd = ac + left * (450f / Mathf.Max(0.3f, Zoom)) + Vector3.down * 250f;
                ViewFrom = "";
            }
            else ViewFrom = "";
            View = name;
        }
        /// <summary>The airplane drawn larger than life when the side view is so wide it would be a few pixels (FINAL's
        /// framing): labelled "AIRPLANE ×N"; 1 = true scale. Physics, vectors and the camera are unchanged.</summary>
        private void ApplyVisualScale()
        {
            float k = 1f;
            if (Cam.orthographic && Cam.orthographicSize > 0)
            {
                float lenPx = LengthM * Cam.pixelHeight / (2f * Cam.orthographicSize);
                if (lenPx < 110f) k = Mathf.Max(1f, Mathf.Round(110f / Mathf.Max(1f, lenPx)));
            }
            VisualScale = k;
            _visualRoot.localScale = Vector3.one * k;
        }

        /// <summary>Control display (protocol 4): where, and how big (fraction of the picture).</summary>
        public string ControlsPlace = "bottom";
        public float ControlsSize = 0.28f;
        private SimLoop _sim;
        private PracticeScenario _flare;   // the flare lesson's own start, constraint and (for the demo) law
        private Transform _visualRoot;
        private AirframeBuilder _builder;

        // ---- output ----
        public RenderTexture Frame { get; private set; }
        private GameObject _surface;
        private WidgetOutputs _outputs;
        private WidgetServer _server;
        public WidgetControls Controls { get; private set; }
        public WidgetPresets Presets { get; private set; }

        /// <summary>JOYSTICK SETUP (owner 2026-10-11): the guided calibration, run from the panel's module / the "joystick" command.</summary>
        public readonly FlyingGame.Bridge.JoystickWizard JoyWizard = new();

        /// <summary>Camera zoom (owner 2026-10-11: "the airplane is too small"): 1 = the old framing, default 1.6× closer.</summary>
        public float Zoom = 1.6f;
        public void SetZoom(float z) { Zoom = Mathf.Clamp(z, 0.6f, 3f); PlayerPrefs.SetFloat("view.zoom", Zoom); PlayerPrefs.Save(); }

        /// <summary>Keep the presenter's panel box across launches.</summary>
        public void SavePanel()
        {
            PlayerPrefs.SetInt("panel.v", 2); PlayerPrefs.SetFloat("panel.x", PanelX); PlayerPrefs.SetFloat("panel.y", PanelY); PlayerPrefs.SetFloat("panel.w", PanelW); PlayerPrefs.Save();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
            Screen.SetResolution(720, 720, FullScreenMode.Windowed);
            WorldTerrain.Active = null;   // a flat world at 0 — no scenery, no thermals
            Zoom = Mathf.Clamp(PlayerPrefs.GetFloat("view.zoom", 1.6f), 0.6f, 3f);
            if (PlayerPrefs.GetInt("panel.v", 0) == 2 && PlayerPrefs.GetFloat("panel.w", -1f) > 0)   // (v2 = the one-row panel; older boxes reset) PlacePanel(PlayerPrefs.GetFloat("panel.x"), PlayerPrefs.GetFloat("panel.y"), PlayerPrefs.GetFloat("panel.w"), WidgetControlsDisplay.PanelAspectN);
            Atmosphere.ThermalStrengthScale = 0; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.SlopeLiftEnabled = false;

            if (PlayerPrefs.GetString("frame.mode", "square") == "16:9") FrameW = 1920;
            Frame = new RenderTexture(FrameW, FrameH, 24, RenderTextureFormat.ARGB32) { name = "AeroWidgetFrame", antiAliasing = 4 };
            Frame.Create();
            var camGo = new GameObject("WidgetCamera");
            camGo.transform.SetParent(transform, false);
            Cam = camGo.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0, 0, 0, 0);   // TRANSPARENT: only the aircraft, the line, the vectors show
            Cam.targetTexture = Frame;
            // LDR all the way (2026-10-13, Ty: the smoke "rendering as black"): with HDR on, the camera drew into a float buffer where
            // the glossy model's ALPHA came out above 1; the smoke's fill pass (weight 1 − destination alpha) then went negative and
            // subtracted the airframe under the smoke to exactly (0,0,0). An 8-bit target keeps alpha in 0…1.
            Cam.allowHDR = false;
            Cam.nearClipPlane = 0.1f; Cam.farClipPlane = 20000f;
            var sun = new GameObject("WidgetSun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.15f; sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sun.transform.SetParent(transform, false);
            // MORE LIGHT, from every side (Ty 2026-10-13: "the airplane was very dark while it was spinning down toward the camera
            // … more light overall actually from all directions"): a fill from below (the ground's bounce), a fill from behind
            // and the far side, and a brighter, even ambient — the airplane reads from any angle a spin turns it to.
            _origAmbMode = RenderSettings.ambientMode; _origAmbProbe = RenderSettings.ambientProbe; _origReflMode = RenderSettings.defaultReflectionMode; _origReflInt = RenderSettings.reflectionIntensity;
            foreach (var (rot, inten) in new[] { (Quaternion.Euler(-55f, 150f, 0f), 1.0f), (Quaternion.Euler(15f, 140f, 0f), 0.7f), (Quaternion.Euler(10f, -150f, 0f), 0.6f), (Quaternion.Euler(-30f, 30f, 0f), 0.6f) })
            {
                var fill = new GameObject("WidgetFill").AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = inten; fill.shadows = LightShadows.None;
                fill.transform.rotation = rot; fill.transform.SetParent(transform, false);
                _fills.Add((fill, inten));
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.85f, 0.87f, 0.9f);
            // A light on the camera (a photographer's on-axis fill): whatever face of the airplane the camera sees is lit — the
            // dark navy paint read black wherever the sun missed it.
            var camFill = new GameObject("WidgetCameraFill").AddComponent<Light>();
            camFill.type = LightType.Directional; camFill.intensity = 0.8f; camFill.shadows = LightShadows.None;
            camFill.transform.SetParent(Cam.transform, false);
            _fills.Add((camFill, 0.8f));
            // Something to REFLECT: the model's glossy paint (roughness 0) mirrors the environment, and a transparent frame has
            // none — those panels went black. A soft sky above, a grey-green ground below (a 16-px cube).
            {
                var cube = new Cubemap(16, TextureFormat.RGBA32, false);
                foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                {
                    var px = new Color[16 * 16];
                    for (int yy = 0; yy < 16; yy++)
                        for (int xx = 0; xx < 16; xx++)
                        {
                            // the texel's direction's height (−1 below … +1 above); cube faces' y runs top → bottom
                            float u = (xx + 0.5f) / 8f - 1f, v = (yy + 0.5f) / 8f - 1f;
                            Vector3 d = face switch { CubemapFace.PositiveX => new Vector3(1, -v, -u), CubemapFace.NegativeX => new Vector3(-1, -v, u),
                                CubemapFace.PositiveY => new Vector3(u, 1, v), CubemapFace.NegativeY => new Vector3(u, -1, -v),
                                CubemapFace.PositiveZ => new Vector3(u, -v, 1), _ => new Vector3(-u, -v, -1) };
                            float h = d.normalized.y;
                            px[yy * 16 + xx] = h >= 0 ? Color.Lerp(new Color(0.75f, 0.82f, 0.9f), new Color(0.45f, 0.62f, 0.9f), h) : Color.Lerp(new Color(0.6f, 0.62f, 0.58f), new Color(0.35f, 0.4f, 0.32f), -h);
                        }
                    cube.SetPixels(px, face);
                }
                cube.Apply();
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 1f;
            }
            { var sh = new UnityEngine.Rendering.SphericalHarmonicsL2(); sh.AddAmbientLight(RenderSettings.ambientLight); _brightProbe = sh; RenderSettings.ambientProbe = sh; }   // (the shaders read the probe, not the colour)

            _vectors = camGo.AddComponent<WidgetVectors>(); _vectors.Widget = this;
            _display = gameObject.AddComponent<WidgetControlsDisplay>(); _display.Widget = this;
            Controls = gameObject.AddComponent<WidgetControls>(); Controls.Widget = this;
            Presets = gameObject.AddComponent<WidgetPresets>(); Presets.Widget = this;
            _review = new WidgetReview(this);
            Loading = new WidgetLoading(this); Autopilot = new WidgetAutopilot(this); Curves = new WidgetCurves(); _lessons = new WidgetLessons(this);
            if (!Capture) { _outputs = gameObject.AddComponent<WidgetOutputs>(); _outputs.Widget = this; }
            _server = gameObject.AddComponent<WidgetServer>(); _server.Widget = this;
            Load(Scenario.Flare, AircraftId, Flaps);
        }

        /// <summary>Start (or restart) a scenario with an aircraft from the fleet.</summary>
        public void Load(Scenario sc, string aircraftId, double flaps)
        {
            // Altitude lock: ON in the spin at 5,000 ft AGL, OFF elsewhere (each start sets its default).
            AltLock = sc == Scenario.Spin; if (sc == Scenario.Spin) AltLockFt = SpinLockFt;
            Atmosphere.Thermals.Clear(); Atmosphere.ThermalStrengthScale = 0;
            Flight++; AltShiftM = 0;
            Tow?.Stop();
            Current = sc; Condition = null; _jetFinalThr = -1; bool approachFlaps = flaps < 0; Flaps = System.Math.Clamp(flaps, 0, 1);
            bool newConfig = aircraftId != AircraftId || Config == null;
            Autopilot?.Off();
            // A start is clean: the yaw damper (independent of the AP) goes off too — left on, it fought the spin's held rudder.
            if (Autopilot != null) { Autopilot.YawDamper = false; Autopilot.AutoThrottle = false; }   // (and the A/T, independent too)
            if (newConfig)
            {
                AircraftId = aircraftId;
                Config = UnityAircraftConfigLoader.LoadFromStreamingAssets(aircraftId);
                BuildVisual();
            }
            Presets.Stop();
            Controls.ResetToTrim();
            // The type's levers (protocol 4); the flare's flap setting goes on a detent of its own handle.
            Controls.Configure(Config, AircraftId, sc == Scenario.Flare ? Flaps : 0, 0);
            if (approachFlaps && sc == Scenario.Flare) { Flaps = Controls.Spec.ApproachFlaps; Controls.Configure(Config, AircraftId, Flaps, 0); }
            if (sc == Scenario.Flare && Controls.Spec.Flaps.Length > 1) Flaps = Controls.FlapsHandle;
            SetWind(WindFromDeg, WindKt);   // the air mass stays as set (a lesson or the wind command changes it)
            if (sc == Scenario.Flare)
            {
                var main = System.Array.Find(WorldTerrain.AirportStrips, st => st.Kind == "paved");
                var rw = new WorldTerrain.RunwayEnd(main, 0, 0, 0);
                Runway = rw;
                _flare = new PracticeScenario(PracticeKind.FlareSideView, PracticeWind.Calm, Config, rw, 0.0, flapFraction: Flaps) { StartWheelsFt = _finalStartFt };
                _finalStartFt = null;
                Ac = _flare.Spawn(); _flare.SkipBriefing();
                Controls.SetTrimStick(_flare.TrimStick);
            }
            else if (sc == Scenario.Cruise)
            {
                _flare = null;
                SpawnCruise();
            }
            else
            {
                _flare = null;
                // High and slow, wings level, power off, trimmed just above the stall (clean): the spin is the pilot's to start.
                double spinM = SpinLockFt * 0.3048;   // 5,000 ft AGL (owner via Glass 2026-10-09)
                double vs = PracticeScenario.EstimateVso(Config, spinM, 0.0), v = 1.15 * vs;
                TrimSolver.Result t = TrimSolver.SolveGliderTrim(Config, v, spinM);
                double pitch = t.Converged ? t.ThetaRad : 0.1, alpha = t.Converged ? t.AlphaRad : 0.1;
                var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
                var vb = new Vec3(v * System.Math.Cos(alpha), 0, v * System.Math.Sin(alpha));
                Ac = new Aircraft(Config, new RigidBodyState(new Vec3(0, 0, -spinM), att, vb, Vec3.Zero), new FlyingGame.Core.Aero.ControlDeflections(0, t.Converged ? t.ElevatorRad : 0, 0, 0, 0));
                Controls.SetTrimStick(t.Converged ? Aircraft.StickForDeflection(t.ElevatorRad, Config.Controls.Elevator) : 0);
                Controls.Throttle01 = 0;
            }
            Ac.FlapFraction = sc == Scenario.Flare ? Flaps : 0;
            Ac.CaptureForces = true;
            if (Controls.Spec.Engines == 0) Controls.Spoilers = Ac.CurrentDeflections.SpoilerFraction;   // a glider's approach spoilers
            bool gearUp = sc == Scenario.Cruise && Controls.Spec.Retractable;   // cruise: gear up
            Ac.SetGear(!gearUp, immediate: true); Controls.GearDown = !gearUp;
            _snapCam = true;
            if (newConfig || Loading.MacM <= 0) Loading.Configure(Config, false, 0, 0);   // a new type: its own default loading (the same type keeps the current one)
            _sim = NewSim();
            Review?.Clear(); _shown = null;
            if (_surface != null) _surface.SetActive(sc == Scenario.Flare);
            else if (sc == Scenario.Flare) BuildSurface();
            if (sc == Scenario.Flare) BuildRunway3D(); else if (_runway3D != null) _runway3D.SetActive(false);
            Paused = false;
        }

        /// <summary>PROTOCOL 5 (owner 2026-10-08): start a lesson from one of three known states with one tap —
        ///   CRUISE: straight and level at 75 % power, trimmed (free flight, any view; default chase);
        ///   FINAL: 300 ft on final at idle with the approach flaps, lined up, flying on into the flare (side view, fixed);
        ///   SPIN: a developed spin with full pro-spin elevator and rudder HELD (every input source holds; views: locked from
        ///         the side, or body from the left).
        /// Returns null, or the error for a view the condition doesn't allow.</summary>
        public string StartCondition(string cond, string aircraftId, string view, string from)
        {
            aircraftId ??= AircraftId;
            switch (cond)
            {
                case "cruise":
                    Load(Scenario.Cruise, aircraftId, 0);
                    Condition = "cruise";
                    string v = System.Array.IndexOf(Views, view ?? "") >= 0 ? view : "chase";
                    SetView(v, from);
                    return view != null && v != view ? "unknown view" : null;
                case "final":
                    _finalStartFt = 300;
                    Load(Scenario.Flare, aircraftId, -1);   // the type's approach flaps
                    Condition = "final"; View = "side"; ViewFrom = "";
                    if (_flare.JetFlare) TrimFinalJet();
                    if (_surface != null) _surface.SetActive(false);   // the runway is drawn instead (WidgetVectors.DrawWorld)
                    if (view != null && System.Array.IndexOf(FinalViews, view) >= 0) SetView(view);
                    return view != null && System.Array.IndexOf(FinalViews, view) < 0 ? "final views: " + string.Join(", ", FinalViews) : null;
                case "spin":
                {
                    string err = view == null || view == "locked" || view == "body" ? null : "spin views: locked (from side) or body (from left)";
                    Load(Scenario.Spin, aircraftId, 0);
                    Presets.Run("spin-developed");          // the entry, fast-forwarded offscreen
                    for (int i = 0; i < 300; i++) { Presets.Tick(0.02f); StepOffscreen(0.02f); }   // + 6 s: settled
                    Presets.Stop();
                    Condition = "spin";
                    // Full pro-spin controls, held: stick full aft, rudder full WITH the rotation, ailerons neutral, idle.
                    double r = Ac.State.Rates.Z;
                    Controls.Aileron = 0; Controls.Elevator = -1; Controls.Rudder = r < 0 ? -1 : 1; Controls.Throttle01 = 0; Controls.ElevatorFree = false;
                    Controls.Hold = true;
                    if (view == "body") SetView("body", "left"); else SetView("locked", "side");
                    Review.Clear(); _snapCam = true;
                    return err;
                }
            }
            return "unknown condition";
        }

        /// <summary>The views a condition allows: null = any.</summary>
        public string CheckView(string name, string from)
        {
            if (Condition == "final" && System.Array.IndexOf(FinalViews, name) < 0) return "final views: " + string.Join(", ", FinalViews);
            if (Condition == "spin" && !((name == "locked" && (from == null || from == "side")) || (name == "body" && (from == null || from == "left"))))
                return "spin views: locked (from side) or body (from left)";
            return null;
        }

        /// <summary>FINAL for a jet: the lesson's 3° powered approach, but TRIMMED — flown offscreen with a pitch-for-path,
        /// power-for-speed hold until it is steady, then put back at the 300 ft start. Hands-off it then stays on the 3° path
        /// at the approach speed; the power comes off to idle from 50 to 5 ft (the lesson's schedule).</summary>
        private void TrimFinalJet()
        {
            var s0 = Ac.State;
            double V0 = s0.Velocity.Length, gT = -PracticeScenario.JetPathDeg * System.Math.PI / 180, flaps = Ac.FlapFraction;
            var q0 = s0.Attitude;
            double heading = System.Math.Atan2(2 * (q0.W * q0.Z + q0.X * q0.Y), 1 - 2 * (q0.Y * q0.Y + q0.Z * q0.Z));
            RigidBodyState Make(double aa)
            {
                double pitch = aa + gT, hh = heading / 2, hp = pitch / 2;
                var att = Quat.Multiply(new Quat(0, 0, System.Math.Sin(hh), System.Math.Cos(hh)), new Quat(0, System.Math.Sin(hp), 0, System.Math.Cos(hp)));
                return new RigidBodyState(s0.Position, att, new Vec3(V0 * System.Math.Cos(aa), 0, V0 * System.Math.Sin(aa)), Vec3.Zero);
            }
            const double T = 0.4;
            double[] Res(double[] x)
            {
                var t = new Aircraft(Config, Make(x[0]), new FlyingGame.Core.Aero.ControlDeflections(0, x[1], 0, 0, flaps)) { FlapFraction = flaps };
                new SimLoop(t).RunFor(T, new ControlInputs(0, Aircraft.StickForDeflection(x[1], Config.Controls.Elevator), 0, 1 - 2 * x[2]));
                var st = t.State; Vec3 vw = st.Attitude.Rotate(st.Velocity); double V = vw.Length;
                return new[] { (V - V0) / T, (System.Math.Asin(System.Math.Clamp(-vw.Z / V, -1, 1)) - gT) / T, st.Rates.Y / T };
            }
            var q = s0.Attitude; double pitch0 = System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
            double[] X = { pitch0 - gT, Ac.CurrentDeflections.ElevatorRad, 0.3 }, H = { 0.003, 0.003, 0.01 }, Lim = { 0.0175, 0.0175, 0.1 };
            for (int it = 0; it < 40; it++)
            {
                double[] r0 = Res(X);
                if (System.Math.Abs(r0[0]) < 0.01 && System.Math.Abs(r0[1]) < 0.0005 && System.Math.Abs(r0[2]) < 0.0005) break;
                var J = new double[3, 3];
                for (int k = 0; k < 3; k++) { var xk = (double[])X.Clone(); xk[k] += H[k]; double[] rk = Res(xk); for (int i = 0; i < 3; i++) J[i, k] = (rk[i] - r0[i]) / H[k]; }
                double[] dx = SolveN(J, new[] { -r0[0], -r0[1], -r0[2] });
                if (dx == null) break;
                double lim = 1; for (int i = 0; i < 3; i++) lim = System.Math.Min(lim, Lim[i] / System.Math.Max(1e-9, System.Math.Abs(dx[i])));
                for (int i = 0; i < 3; i++) X[i] += dx[i] * lim;
                X[2] = System.Math.Clamp(X[2], 0, 1);
            }
            { var rr = Res(X); Debug.Log($"[Widget] final jet trim {AircraftId}: α {X[0] * 57.3:F2}° elev {X[1] * 57.3:F2}° stick {Aircraft.StickForDeflection(X[1], Config.Controls.Elevator):F3} thr {X[2]:F3} res {rr[0]:F4} {rr[1]:F5} {rr[2]:F5}"); }
            Ac = new Aircraft(Config, Make(X[0]), new FlyingGame.Core.Aero.ControlDeflections(0, X[1], 0, 0, flaps)) { FlapFraction = flaps, CaptureForces = true };
            Ac.SetGear(true, immediate: true);
            _sim = NewSim();
            Controls.SetTrimStick(Aircraft.StickForDeflection(X[1], Config.Controls.Elevator));
            _jetFinalThr = X[2];
        }
        private double _jetFinalThr = -1;

        public const double CruiseAltM = 762;   // 2,500 ft AGL over the flat world (owner via Glass 2026-10-09: the default start height)

        /// <summary>Straight and level at 75 % power: the speed, angle of attack and elevator at which, with that power, the
        /// aircraft neither speeds up, climbs nor pitches — solved on the sim itself (Newton on short trial runs), so
        /// hands-off it stays level. A glider has no power: it glides at best L/D, trimmed.</summary>
        private void SpawnCruise()
        {
            double alt = CruiseAltM, vso = PracticeScenario.EstimateVso(Config, alt, 0.0);
            bool glider = Config.Propulsion == null;
            bool gearUp = Config.RetractableGear;
            double thr = glider ? 0 : 0.75;
            double v = glider ? ApproachSpawn.FindBestGlide(Config, alt).SpeedMs : PracticeScenario.EstimateCruise75(Config, alt, vso);
            TrimSolver.Result g = TrimSolver.SolveGliderTrim(Config, v, alt);
            double a = g.Converged ? g.AlphaRad : 0.05, e = g.Converged ? g.ElevatorRad : 0;
            double gamma = glider && g.Converged ? -System.Math.Atan(1.0 / System.Math.Max(3.0, g.GlideRatio)) : 0;
            RigidBodyState Make(double vv, double aa)
            {
                double pitch = aa + gamma;
                var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
                return new RigidBodyState(new Vec3(0, 0, -alt), att, new Vec3(vv * System.Math.Cos(aa), 0, vv * System.Math.Sin(aa)), Vec3.Zero);
            }
            Aircraft Trial(double vv, double aa, double ee)
            {
                var t = new Aircraft(Config, Make(vv, aa), new FlyingGame.Core.Aero.ControlDeflections(0, ee, 0, 0, 0));
                if (gearUp) t.SetGear(false, immediate: true);
                return t;
            }
            double ail = 0, rud = 0;
            if (!glider)
            {
                // Five unknowns (speed, α, elevator, aileron, rudder), five residuals over a short run: no speed change, no
                // climb, no pitch, roll or yaw rate. Aileron and rudder are the cruise trim a pilot holds or tabs in (the
                // single's torque and P-factor at 75 % power).
                const double T = 0.25;
                double[] Res(double[] x)
                {
                    var t = Trial(x[0], x[1], x[2]);
                    new SimLoop(t).RunFor(T, new ControlInputs(x[3], Aircraft.StickForDeflection(x[2], Config.Controls.Elevator), x[4], 1 - 2 * thr));
                    var st = t.State; Vec3 vw = st.Attitude.Rotate(st.Velocity); double V = vw.Length;
                    return new[] { (V - x[0]) / T, System.Math.Asin(System.Math.Clamp(-vw.Z / V, -1, 1)) / T, st.Rates.Y / T, st.Rates.X / T, st.Rates.Z / T };
                }
                double[] X = { v, a, e, 0, 0 }, H = { 0.5, 0.003, 0.003, 0.02, 0.02 }, Lim = { 3, 0.0175, 0.0175, 0.1, 0.1 };
                for (int it = 0; it < 16; it++)
                {
                    double[] r0 = Res(X);
                    if (System.Math.Abs(r0[0]) < 0.01 && System.Math.Abs(r0[1]) < 0.0005 && System.Math.Abs(r0[2]) < 0.0005 && System.Math.Abs(r0[3]) < 0.0005 && System.Math.Abs(r0[4]) < 0.0005) break;
                    var J = new double[5, 5];
                    for (int k = 0; k < 5; k++)
                    {
                        var xk = (double[])X.Clone(); xk[k] += H[k];
                        double[] rk = Res(xk);
                        for (int i = 0; i < 5; i++) J[i, k] = (rk[i] - r0[i]) / H[k];
                    }
                    double[] dx = SolveN(J, new[] { -r0[0], -r0[1], -r0[2], -r0[3], -r0[4] });
                    if (dx == null) break;
                    double lim = 1;
                    for (int i = 0; i < 5; i++) lim = System.Math.Min(lim, Lim[i] / System.Math.Max(1e-9, System.Math.Abs(dx[i])));
                    for (int i = 0; i < 5; i++) X[i] += dx[i] * lim;
                    X[0] = System.Math.Clamp(X[0], 1.2 * vso, 8 * vso); X[3] = System.Math.Clamp(X[3], -0.5, 0.5); X[4] = System.Math.Clamp(X[4], -0.6, 0.6);
                }
                v = X[0]; a = X[1]; e = X[2]; ail = X[3]; rud = X[4];
            }
            Ac = Trial(v, a, e);
            double eleStick = Aircraft.StickForDeflection(e, Config.Controls.Elevator);
            if (!glider)
            {
                // Then trim it as a pilot does: fly it (offscreen) with a gentle wings-level / ball-centred / zero-VSI hold
                // until the hands are only holding the trim — the engine, slipstream and torque settle too. Those control
                // positions are the trim; the settled airplane is the start, put back at the start point.
                var loop = new SimLoop(Ac);
                double eI = eleStick, aI = ail, rI = rud;
                const double dt = 0.02;
                for (int i = 0; i < 3000; i++)
                {
                    var st = Ac.State; var q = st.Attitude;
                    double roll = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
                    Vec3 vw = q.Rotate(st.Velocity); double V = st.Velocity.Length;
                    double climb = -vw.Z, beta = System.Math.Asin(System.Math.Clamp(st.Velocity.Y / System.Math.Max(1, V), -1, 1));
                    aI = System.Math.Clamp(aI - 0.25 * roll * dt, -0.5, 0.5);
                    rI = System.Math.Clamp(rI + 0.8 * beta * dt, -0.6, 0.6);   // wind from the right (β > 0) → right rudder
                    eI = System.Math.Clamp(eI + 0.004 * climb * dt, -1, 1);   // climbing → a touch of push (elevator + = nose down)
                    double aCmd = aI - 0.6 * roll - 0.15 * st.Rates.X, rCmd = rI + 2.0 * beta - 0.2 * st.Rates.Z, eCmd = eI + 0.01 * climb + 0.15 * st.Rates.Y;
                    loop.RunFor(dt, new ControlInputs(aCmd, eCmd, rCmd, 1 - 2 * thr));
                }
                eleStick = eI; ail = aI; rud = rI;
                var s1 = Ac.State;
                Ac.State = new RigidBodyState(new Vec3(0, 0, -alt), s1.Attitude, s1.Velocity, s1.Rates);
                v = s1.Velocity.Length;
            }
            Controls.SetTrimStick(eleStick);
            Controls.SetLateralTrim(ail, rud);
            Controls.Throttle01 = thr;
            CruiseSpeedMs = v;
        }
        public double CruiseSpeedMs { get; private set; }

        /// <summary>Gaussian elimination with partial pivoting (small dense systems).</summary>
        private static double[] SolveN(double[,] A, double[] b)
        {
            int n = b.Length; var m = (double[,])A.Clone(); var r = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int piv = c; for (int i = c + 1; i < n; i++) if (System.Math.Abs(m[i, c]) > System.Math.Abs(m[piv, c])) piv = i;
                if (System.Math.Abs(m[piv, c]) < 1e-12) return null;
                if (piv != c) { for (int k = 0; k < n; k++) (m[c, k], m[piv, k]) = (m[piv, k], m[c, k]); (r[c], r[piv]) = (r[piv], r[c]); }
                for (int i = c + 1; i < n; i++)
                {
                    double f = m[i, c] / m[c, c];
                    for (int k = c; k < n; k++) m[i, k] -= f * m[c, k];
                    r[i] -= f * r[c];
                }
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--) { double sum = r[i]; for (int k = i + 1; k < n; k++) sum -= m[i, k] * x[k]; x[i] = sum / m[i, i]; }
            return x;
        }

        private void BuildVisual()
        {
            if (_visualRoot != null) Destroy(_visualRoot.gameObject);
            _visualRoot = new GameObject("WidgetAircraft").transform;
            _visualRoot.SetParent(transform, false);
            _builder = new AirframeBuilder();
            float half = _builder.Build(_visualRoot, Config);
            SpanM = half * 2f;
            double xmin = double.MaxValue, xmax = double.MinValue;
            foreach (var sf in Config.Surfaces) foreach (var st in sf.Strips) { xmin = System.Math.Min(xmin, st.Pos[0] - st.Chord * 0.75); xmax = System.Math.Max(xmax, st.Pos[0] + st.Chord * 0.25); }
            LengthM = xmax > xmin ? (float)(xmax - xmin) : SpanM * 0.8f;
            _smoke ??= new SmokeTrail(transform, _visualRoot.gameObject.layer);
            var ex = SmokeTrail.ExhaustsFor(Config, AircraftId, _builder.PropHubsBody(), LengthM, (float)(Config.Fuselage?.Crossflow?.BodyRadiusM ?? 0.5), out var jet);
            // A config'd exhaust is measured on the MODEL itself (owner 2026-10-12: "4 ft after the spinner … out of the belly
            // just ahead of the landing gear"): so far aft of the model's spinner tip, on its belly surface at the centreline.
            ModelNoseFromCg = ModelNoseAndBelly(0, out var noseAt) ? noseAt.X : double.NaN;
            if (!ModelTopAndBelly(out ModelTopFromCg, out ModelBellyFromCg)) { ModelTopFromCg = double.NaN; ModelBellyFromCg = double.NaN; }
            if (double.IsNaN(ModelNoseFromCg)) { var hubs = _builder.PropHubsBody(); if (hubs.Count == 1) ModelNoseFromCg = hubs[0].x + 0.25 + VisualOffset.X; }   // (a model we can't read: the spinner ≈ the prop hub + 0.25 m)   // (the model's spinner tip: the four-forces thrust starts there)
            if (Config.Smoke != null && Config.Smoke.AftOfNoseM > 0 && ex.Length == 1 && ModelNoseAndBelly(Config.Smoke.AftOfNoseM, out var exM)) ex[0] = exM;
            else for (int i = 0; i < ex.Length; i++) ex[i] += VisualOffset;
            _smoke.SetExhausts(ex, jet);
            _smoke.Flow = BuildAirframeFlow();
            _smoke.PipeDiameterM = (Config.Smoke?.PipeDiameterIn ?? 4) * 0.0254;
            SmokeExhausts = ex;
            _smoke.Stream = Config.Propulsion == null;   // a glider's wingtip canisters
        }

        /// <summary>The air round this airframe for the smoke: the surfaces from the config, the fuselage measured on the model
        /// (its depth on the centreline, station by station; the fin's root caps it aft), else a rounded analytic body.</summary>
        private FlyingGame.Core.Aero.AirframeFlow BuildAirframeFlow()
        {
            double nose = double.IsNaN(ModelNoseFromCg) ? 0.4 * LengthM : ModelNoseFromCg;
            var flow = new FlyingGame.Core.Aero.AirframeFlow(Config, nose, Config.Fuselage?.Crossflow?.BodyRadiusM is double br && br > 0 ? br : 0.5);
            var pts = new List<Vector3>();
            foreach (var mf in _visualRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (m == null || !m.isReadable || mf.GetComponent<MeshRenderer>() == null || !mf.GetComponent<MeshRenderer>().enabled) continue;
                var vs = m.vertices; var tf = mf.transform;
                for (int i = 0; i < vs.Length; i += System.Math.Max(1, vs.Length / 40000))
                {
                    Vector3 u = _visualRoot.InverseTransformPoint(tf.TransformPoint(vs[i]));
                    pts.Add(new Vector3(u.z + (float)VisualOffset.X, u.x + (float)VisualOffset.Y, -u.y + (float)VisualOffset.Z));
                }
            }
            if (pts.Count < 100) return flow;
            // The fin's root (body z), and its chord range, so the fin isn't taken for fuselage.
            double finRoot = double.NaN, finLe = double.MinValue, finTe = double.MaxValue; var cg = Config.Mass.CgVec();
            foreach (var sf in Config.Surfaces)
            {
                bool vert = false; foreach (var st in sf.Strips) if (System.Math.Abs(st.Pos[2] - sf.Strips[0].Pos[2]) > 0.2) vert = true;
                if (!vert) continue;
                foreach (var st in sf.Strips) { double z = st.Pos[2] - cg.Z; if (double.IsNaN(finRoot) || z > finRoot) finRoot = z; finLe = System.Math.Max(finLe, st.Pos[0] - cg.X + 0.25 * st.Chord); finTe = System.Math.Min(finTe, st.Pos[0] - cg.X - 0.75 * st.Chord); }
            }
            var xs = new List<double>(); var rs = new List<double>(); var zs = new List<double>();
            float noseM = float.MinValue, tailM = float.MaxValue; foreach (var p in pts) { noseM = Mathf.Max(noseM, p.x); tailM = Mathf.Min(tailM, p.x); }
            for (float x = noseM - 0.05f; x > tailM; x -= 0.1f)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in pts) if (Mathf.Abs(p.x - x) < 0.05f && Mathf.Abs(p.y) < 0.1f) { lo = Mathf.Min(lo, p.z); hi = Mathf.Max(hi, p.z); }
                if (hi < lo) continue;
                if (!double.IsNaN(finRoot) && x <= finLe + 0.1 && x >= finTe - 0.1) lo = Mathf.Max(lo, (float)finRoot - 0.05f);
                if (hi - lo < 0.02f) continue;
                xs.Add(x); rs.Add(0.5 * (hi - lo)); zs.Add(0.5 * (hi + lo));
            }
            if (xs.Count >= 4) { flow.SetFuselage(xs.ToArray(), rs.ToArray(), zs.ToArray()); Debug.Log($"[Smoke] airframe flow: fuselage measured, {xs.Count} stations, max radius {System.Linq.Enumerable.Max(rs):0.00} m"); }
            return flow;
        }

        /// <summary>The smoke exhausts as placed (body axes from the CG, m) — for the debug dot and /state.</summary>
        public Vec3[] SmokeExhausts = System.Array.Empty<Vec3>();
        /// <summary>The visual model's most forward point (the spinner tip), body x from the CG (m); NaN = unknown.</summary>
        public double ModelNoseFromCg = double.NaN;
        /// <summary>The visual model's highest and lowest points, body z from the CG (m, + = down; so Top is the more negative):
        /// the four forces' LIFT starts above the airframe and WEIGHT below it (Ty 2026-10-14), as thrust and drag start clear of
        /// the spinner and the tail. NaN = unknown (a model we can't read): the vectors then start at the CG as before.</summary>
        public double ModelTopFromCg = double.NaN, ModelBellyFromCg = double.NaN;
        /// <summary>The middle of the airframe, spinner to tail, body x from the CG (m).</summary>
        public double AirframeMidFromCg
        {
            get
            {
                if (double.IsNaN(ModelNoseFromCg) || Config == null) return 0;
                double tail = double.MaxValue; foreach (var sf in Config.Surfaces) foreach (var st in sf.Strips) tail = System.Math.Min(tail, st.Pos[0] - 0.75 * st.Chord);
                return 0.5 * (ModelNoseFromCg + (tail - Config.Mass.Cg[0]));
            }
        }

        /// <summary>The visual model's spinner tip (its most forward point) and, <paramref name="aft"/> metres behind it, the belly
        /// surface on the centreline — in body axes from the CG (the visual root sits at CG + VisualOffset).</summary>
        private bool ModelNoseAndBelly(double aft, out Vec3 at)
        {
            at = Vec3.Zero;
            var pts = new List<Vector3>();
            foreach (var mf in _visualRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (m == null || !m.isReadable || mf.GetComponent<MeshRenderer>() == null || !mf.GetComponent<MeshRenderer>().enabled) continue;
                var vs = m.vertices; var tf = mf.transform;
                for (int i = 0; i < vs.Length; i += System.Math.Max(1, vs.Length / 20000))
                {
                    Vector3 u = _visualRoot.InverseTransformPoint(tf.TransformPoint(vs[i]));
                    pts.Add(new Vector3(u.z, u.x, -u.y));   // Unity local (y, −z, x) → body (x fwd, y right, z down)
                }
            }
            if (pts.Count < 100) return false;
            float nose = float.MinValue; foreach (var p in pts) nose = Mathf.Max(nose, p.x);
            float x = nose - (float)aft, belly = float.MinValue;
            foreach (var p in pts) if (Mathf.Abs(p.x - x) < 0.12f && Mathf.Abs(p.y) < 0.12f) belly = Mathf.Max(belly, p.z);
            if (belly == float.MinValue) return false;
            at = new Vec3(x, 0, belly) + VisualOffset;
            Debug.Log($"[Smoke] model nose x {nose:0.00} m · exhaust {aft:0.00} m aft at x {x:0.00}, belly z {belly:0.00} (body, from the visual origin)");
            return true;
        }

        /// <summary>The visual model's highest and lowest points over the whole airframe (fin top to tyre bottom), body z from the
        /// CG (m, + = down). The four forces' lift and weight start clear of these, so neither arrow crosses the airplane in any
        /// view. Falls back to the config's own surfaces when no mesh can be read.</summary>
        private bool ModelTopAndBelly(out double top, out double belly)
        {
            top = belly = double.NaN;
            float hi = float.MaxValue, lo = float.MinValue;   // body z: − is up
            foreach (var mf in _visualRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (m == null || !m.isReadable || mf.GetComponent<MeshRenderer>() == null || !mf.GetComponent<MeshRenderer>().enabled) continue;
                var vs = m.vertices; var tf = mf.transform;
                for (int i = 0; i < vs.Length; i += System.Math.Max(1, vs.Length / 20000))
                {
                    float z = -_visualRoot.InverseTransformPoint(tf.TransformPoint(vs[i])).y;   // Unity local → body z (down +)
                    hi = Mathf.Min(hi, z); lo = Mathf.Max(lo, z);
                }
            }
            if (hi < lo) { top = hi + VisualOffset.Z; belly = lo + VisualOffset.Z; }
            else if (Config != null)
            {
                var cg = Config.Mass.CgVec();
                foreach (var sf in Config.Surfaces) foreach (var st in sf.Strips) { double z = st.Pos[2] - cg.Z; top = double.IsNaN(top) ? z : System.Math.Min(top, z); belly = double.IsNaN(belly) ? z : System.Math.Max(belly, z); }
                foreach (var g in Config.Gear) { double z = g.Pos[2]; belly = double.IsNaN(belly) ? z : System.Math.Max(belly, z); }   // (gear Pos is already vs the CG)
            }
            if (double.IsNaN(top) || double.IsNaN(belly) || belly - top < 0.2) return false;
            Debug.Log($"[Widget] airframe top z {top:0.00} m, belly z {belly:0.00} (body, from the CG): the four forces' lift and weight start clear of them");
            return true;
        }

        /// <summary>The aerotow (WidgetTow): the game's rope and tug pilot, hooked up in the air.</summary>
        private WidgetTow _tow;
        public WidgetTow Tow => _tow ??= new WidgetTow(this);
        public int VisualLayer => _visualRoot != null ? _visualRoot.gameObject.layer : 0;

        // ---- SMOKE (owner 2026-10-11, the Talk Flying outro) ----
        private SmokeTrail _smoke;
        // ---- the light rig's strength (2026-10-13: the bright spin rig washed the white C-172 out) ----
        private readonly List<(Light l, float full)> _fills = new();
        private UnityEngine.Rendering.AmbientMode _origAmbMode; private UnityEngine.Rendering.SphericalHarmonicsL2 _origAmbProbe, _brightProbe;
        private UnityEngine.Rendering.DefaultReflectionMode _origReflMode; private float _origReflInt;
        /// <summary>The fill rig, 0 … 1 (Ty 2026-10-13: the white C-172 "really washed out" under the rig the dark Extra needed):
        /// 0 = the sun and the scene's own ambient (the original look); 1 = all the fills, the bright ambient and the sky
        /// reflection. vectorStyle {"lightFill"}.</summary>
        public float LightFill { get; private set; } = 1f;
        public void SetLightFill(float f)
        {
            LightFill = Mathf.Clamp01(f);
            foreach (var (l, full) in _fills) l.intensity = full * LightFill;
            RenderSettings.ambientMode = LightFill > 0 ? UnityEngine.Rendering.AmbientMode.Flat : _origAmbMode;
            RenderSettings.ambientProbe = _origAmbProbe * (1 - LightFill) + _brightProbe * LightFill;
            RenderSettings.defaultReflectionMode = LightFill > 0 ? UnityEngine.Rendering.DefaultReflectionMode.Custom : _origReflMode;
            RenderSettings.reflectionIntensity = LightFill > 0 ? LightFill : _origReflInt;
        }
        /// <summary>The exhaust's exit velocity (m/s) at this rpm and throttle (Ty 2026-10-13: "a 540 ci engine turning 2700 rpm
        /// and the exhaust pipe is a 4 inch diameter — calculate the velocity"): <see cref="FlyingGame.Core.Aero.ExhaustJet"/> from
        /// the config's displacement (else ≈ 0.56 hp/in³ of the rated power) and pipe. A turbine's exhaust isn't modelled
        /// this way: 25 m/s (the old figure) at its tailpipe.</summary>
        private double JetU0(double rpm, double throttle, double altM)
        {
            var pr = Config?.Propulsion; if (pr == null) return 0;
            if (pr.PropDiameterM <= 0) return 25.0 * System.Math.Clamp(throttle, 0, 1);
            var sm = Config.Smoke;
            double cuIn = sm != null && sm.DisplacementCuIn > 0 ? sm.DisplacementCuIn : pr.MaxPowerW / 745.7 / 0.556;
            return FlyingGame.Core.Aero.ExhaustJet.ExitVelocity(cuIn * 16.387e-6, rpm, throttle, Atmosphere.DensityAtAltitude(altM),
                (sm?.PipeDiameterIn ?? 4) * 0.0254, sm?.ExhaustTempK ?? 900);
        }
        /// <summary>Which start this is (the smoke never joins one flight's path to the next).</summary>
        public int Flight;
        /// <summary>The smoke oil's flow now: none with no engine turning; more with power (the exhaust carries it).</summary>
        public float SmokeFlow => Ac == null ? 0f : Config.Propulsion == null ? 1f   // a glider's wingtip canisters: always full
            : Controls.Spec.Engines <= 0 || Ac.EngineRpm < 100 ? 0f : (float)(0.25 + 0.75 * System.Math.Clamp(Ac.Throttle01, 0, 1));
        private SmokeTrail.Sample? SmokeSample(WidgetHistory.Snap f) => f == null ? null : new SmokeTrail.Sample
            { T = f.SimTime, Pos = f.State.Position - new Vec3(0, 0, f.AltShift), Att = f.State.Attitude, VelBody = f.State.Velocity, Rates = f.State.Rates, Flow = f.SmokeFlow,
              JetU0 = JetU0(f.Rpm, f.AcThrottle, -(f.State.Position.Z - f.AltShift)), AirDensity = Atmosphere.DensityAtAltitude(-(f.State.Position.Z - f.AltShift)) };
        /// <summary>The smoke as it was at the frame shown (index frames back; -1 = live, the aircraft as it is now).</summary>
        private void ShowSmoke(int index)
        {
            if (_smoke == null) return;
            if (!Show.TryGetValue("smoke", out bool on) || !on) { _smoke.Hide(); return; }
            var H = Review.History;
            WidgetHistory.Snap at = index < 0 ? null : H.Get(index);
            int flight = at?.Flight ?? Flight;
            double shift = at?.AltShift ?? AltShiftM, now = at?.SimTime ?? Review.SimTimeNow;
            var live = index < 0 ? new SmokeTrail.Sample { T = now, Pos = Ac.State.Position - new Vec3(0, 0, AltShiftM), Att = Ac.State.Attitude, VelBody = Ac.State.Velocity, Rates = Ac.State.Rates, Flow = SmokeFlow, JetU0 = JetU0(Ac.EngineRpm, Ac.Throttle01, -(Ac.State.Position.Z - AltShiftM)), AirDensity = Atmosphere.DensityAtAltitude(-(Ac.State.Position.Z - AltShiftM)) } : (SmokeTrail.Sample?)null;
            int first = index < 0 ? 0 : index;
            _smoke.Build(k =>
            {
                if (live.HasValue) { if (k == 0) return live; k--; }
                int b = first + k;
                if (b >= H.Count) return null;
                var f = H.Get(b);
                return f == null || f.Flight != flight ? null : SmokeSample(f);
            }, now, p => CoordinateMap.ToUnity(p + new Vec3(0, 0, shift)), Cam);
        }

        /// <summary>FINAL in 3D (owner 2026-10-11: a chase-view landing): the runway as a slab of asphalt under the 3D views;
        /// its markings and the ground grid are drawn with the vectors (WidgetVectors.DrawWorld). The side view keeps its line.</summary>
        private GameObject _runway3D;
        private void BuildRunway3D()
        {
            if (_runway3D != null || Runway.Strip.Length <= 0) return;
            _runway3D = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_runway3D.GetComponent<Collider>());
            _runway3D.name = "WidgetRunway"; _runway3D.transform.SetParent(transform, false);
            _runway3D.transform.localScale = new Vector3((float)Runway.Strip.Width, 0.05f, (float)Runway.LengthM);   // Unity z = sim north = the runway
            _runway3D.transform.position = new Vector3(0f, -0.03f, 0f);
            var mr = _runway3D.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = new Color(0.32f, 0.33f, 0.35f, 1f) };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _runway3D.layer = _visualRoot != null ? _visualRoot.gameObject.layer : 0;
            _runway3D.SetActive(false);
        }

        private void BuildSurface()
        {
            // The surface: one white line, edge-on to the side camera (a long thin bar along the runway).
            _surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_surface.GetComponent<Collider>());
            _surface.name = "WidgetSurface"; _surface.transform.SetParent(transform, false);
            _surface.transform.localScale = new Vector3(0.6f, 0.08f, 20000f);   // Unity z = sim north = the runway
            _surface.transform.position = new Vector3(0f, -0.04f, 0f);
            var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = Color.white };
            _surface.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void Update()
        {
            if (_sim == null) return;
            float rdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Controls.Poll();
            if (Paused)
            {
                // REVIEW (protocol 3): the picture is a frame from the history; nothing flies (controls don't move it).
                Review.Tick(rdt);
                ShowFrame(Review.Shown);
                return;
            }
            float dt = rdt * TimeScale;
            if (JoyWizard.Active) JoyWizard.Tick(Time.unscaledDeltaTime);
            Presets.Tick(dt);
            Loading.Tick(dt);
            Lessons.Tick(dt);
            Autopilot.Tick(dt);
            float stepped = dt > 0f ? StepSim(dt) : 0f;
            ForceTrend.Tick(this, dt);
            ShowLive(dt);
            Review.Record(stepped);   // (the time actually simulated, so the history's clock is the airplane's)
        }

        /// <summary>One step of the physics with the current controls (the flare merges in the lesson's lateral law).</summary>
        /// <summary>The physics clock (2026-10-13). The widget used to ask for `RunFor(dt)`, which ROUNDS each frame's time to
        /// whole fixed steps: at 60 fps 16.7 ms → 3 × 5 ms, so the airplane ran ~10 % slow against the history's clock (every
        /// render's motion, a spin's rotation too); at a 0.1 timescale nearly every frame rounded to none. Now the remainder
        /// carries over (the game's accumulator) and the step divides the history's frame exactly — 1/210 s at 30 snapshots a
        /// second, 1/240 at 120, 1/600 at 600 — so every snapshot is a real physics state, evenly spaced.</summary>
        private double _simAcc;
        private SimLoop NewSim()
        {
            int perFrame = Mathf.Max(1, Mathf.CeilToInt((float)(1.0 / SimLoop.DefaultFixedDtSec / (WidgetHistory.Hz * Review.RecordRate))));
            _simAcc = 0;
            return new SimLoop(Ac, 1.0 / (WidgetHistory.Hz * Review.RecordRate * perFrame));
        }
        /// <summary>The capture's snapshots per 1/30 s (WidgetReview.RecordRate), with the physics step to match.</summary>
        public void SetRecordRate(int rate) { Review.RecordRate = rate; if (Ac != null) _sim = NewSim(); }

        /// <summary>Step the physics by <paramref name="dt"/> (carrying the remainder); returns the time actually simulated.</summary>
        public float StepSim(float dt)
        {
            ControlInputs inputs = Controls.Inputs;
            if (_flare != null)
            {
                // The flare: the pilot's elevator and power (the lesson keeps it straight and on the centreline — the
                // side view is longitudinal only); the demo preset flies the lesson's own law.
                ControlInputs merged = _flare.Step(Ac, inputs, dt);
                var auto = _flare.Autopilot;
                // FINAL: the lesson's power (idle; a jet's 3° approach power, eased to idle from 50 to 5 ft) until the pilot moves it.
                bool lessonThr = Condition == "final" && !Controls.ThrottleTouched && Controls.Spec.Engines > 0;
                if (lessonThr)
                {
                    if (_flare.JetFlare && _jetFinalThr >= 0)
                    {
                        // The trimmed approach power, off to idle from 50 to 5 ft.
                        double ft = -Ac.State.Position.Z / 0.3048;
                        Controls.Throttle01 = _jetFinalThr * System.Math.Clamp((ft - PracticeScenario.ThrustOffEndFt) / (PracticeScenario.ThrustOffStartFt - PracticeScenario.ThrustOffEndFt), 0, 1);
                    }
                    else Controls.Throttle01 = System.Math.Clamp((1.0 - merged.ThrottleLever) * 0.5, 0, 1);
                }
                double lever = lessonThr ? 1.0 - 2.0 * Controls.Throttle01 : inputs.ThrottleLever;
                inputs = Presets.FlareDemo ? auto : new ControlInputs(merged.Aileron, inputs.Elevator, merged.Rudder, lever, false, inputs.ElevatorFree, false);
                if (Presets.FlareDemo)
                {
                    // The lesson's law is flying: the control display (and the history) show ITS inputs.
                    Controls.Aileron = auto.Aileron; Controls.Elevator = auto.Elevator; Controls.Rudder = auto.Rudder;
                    if (Controls.Spec.Engines > 0) Controls.Throttle01 = System.Math.Clamp((1.0 - auto.ThrottleLever) * 0.5, 0, 1);
                }
            }
            Controls.ApplyLevers(Ac, dt);
            Tow.Step(dt);   // the rope's pull and the tug, before the glider's step
            double before = System.Math.Min(_simAcc + dt, SimLoop.MaxCatchUpSec);
            _sim.Advance(dt, inputs, ref _simAcc);
            float stepped = (float)(before - _simAcc);
            if (_flare != null) _flare.ConstrainLongitudinal(Ac);
            else if (AltLock) ApplyAltitudeLock();
            else if (Current == Scenario.Spin) WrapAltitude();
            return stepped;
        }
        public override bool AltLockApplies => _flare == null;

        /// <summary>Pose the live aircraft (surfaces follow the sim's deflections, props spin) and place the camera.</summary>
        public void ShowLive(float dt)
        {
            _shown = null;
            var s = Ac.State;
            _visualRoot.position = CoordinateMap.ToUnity(s.Position + s.Attitude.Rotate(VisualOffset));
            _visualRoot.rotation = CoordinateMap.ToUnity(s.Attitude);
            var d = Ac.CurrentDeflections;
            _builder.SetDeflections((float)d.AileronRad, (float)d.ElevatorRad, (float)d.RudderRad, (float)d.SpoilerFraction);
            _builder.SpinProps(_ => (float)Ac.EngineRpm, dt);
            ShowLostParts();
            PlaceCamera(Ac.State);
            ApplyVisualScale();
            ShowSmoke(-1);
            Tow.Show(Tow.Active ? Tow.Tug.State : (RigidBodyState?)null, Tow.Connected, Ac.State, Vec3.Zero);
        }

        /// <summary>Parts the physics has broken off (a hard landing folds a gear leg, a ground strike takes a wing): the game's
        /// debris, the same as the game (StructuralDamage) — the drawing never keeps a wheel the sim has lost (owner
        /// 2026-10-11: the wheels drew below the ground line after a hard landing, the airplane on its belly). A new
        /// start (a new Aircraft) rebuilds the airframe whole.</summary>
        private void ShowLostParts()
        {
            if (!ReferenceEquals(_lostFor, Ac))
            {
                if (_shownLost.Count > 0) BuildVisual();
                _shownLost.Clear(); _lostFor = Ac;
            }
            foreach (var c in Ac.LostComponents)
            {
                if (!_shownLost.Add(c)) continue;
                var s = Ac.State;
                AirframeVisual.DetachStatic(_builder, _visualRoot, Config, c, CoordinateMap.ToUnity(s.Attitude.Rotate(s.Velocity)));
            }
        }
        private readonly HashSet<FlyingGame.Core.AirframeComponent> _shownLost = new();
        private Aircraft _lostFor;

        /// <summary>The frame being reviewed (null = live).</summary>
        private WidgetHistory.Snap _shown;
        public override WidgetHistory.Snap Shown => _shown;

        /// <summary>Draw a past frame exactly as it was: pose, surfaces, camera — the vectors and labels read it too.</summary>
        public void ShowFrame(WidgetHistory.Snap f)
        {
            if (f == null) { ShowLive(0f); return; }
            _shown = f;
            _visualRoot.position = CoordinateMap.ToUnity(f.State.Position + f.State.Attitude.Rotate(VisualOffset));
            _visualRoot.rotation = CoordinateMap.ToUnity(f.State.Attitude);
            _builder.SetDeflections((float)f.Defl.AileronRad, (float)f.Defl.ElevatorRad, (float)f.Defl.RudderRad, (float)f.Defl.SpoilerFraction);
            // The recorded flight drawn with the CURRENT display (owner 2026-10-11: fly it clean, then play it back with other
            // views, forces and graphs): the camera follows the current view and zoom, not the one recorded.
            // The prop turns in review too (Ty 2026-10-13: "make sure the prop is spinning not fixed"), by the SIM time between
            // the frames shown — a slow-motion render turns it in proportion; a jump (seek) doesn't spin it on.
            double pdt = double.IsNaN(_propT) ? 0 : f.SimTime - _propT; _propT = f.SimTime;
            _builder.SpinProps(_ => f.Rpm, (float)System.Math.Clamp(System.Math.Abs(pdt), 0, 0.1));
            PlaceCamera(f.State, Review.Index);
            ApplyVisualScale();
            ShowSmoke(Review.Index);
            Tow.Show(f.TugState, f.RopeOn, f.State, Vec3.Zero);
        }


        /// <summary>Resume from the frame being shown (protocol 3): the aircraft is put back in that state with the controls it
        /// had, the history after it is discarded (a branch).</summary>
        public void ResumeFrom(WidgetHistory.Snap f)
        {
            if (f != null)
            {
                Ac.SetReplayPose(f.State, f.Defl, f.AcThrottle, f.Flaps, f.GearExt, f.Nz);   // pose + actuator positions exactly as shown
                Presets.Stop();
                Controls.FromSnap(f);
            }
            Paused = false; _shown = null;
        }

        private WidgetReview _review;
        public override WidgetReview Review => _review;
        private WidgetLessons _lessons;
        public override WidgetLessons Lessons => _lessons;
        public override IAeroControls AeroControls => Controls;
        public override string AircraftName { get { foreach (var f in SessionSettings.Fleet) if (f.id == AircraftId) return f.name.ToUpperInvariant(); return AircraftId.Replace("-like", "").ToUpperInvariant(); } }
        public override bool IsWidgetHost => true;
        public double WindFromDeg, WindKt;
        /// <summary>Steady wind (from, kt) — the air mass moves; the lessons' "air-mass" scene uses it.</summary>
        public void SetWind(double fromDeg, double kt)
        {
            WindFromDeg = fromDeg; WindKt = kt;
            double toRad = (fromDeg + 180) * System.Math.PI / 180, ms = kt / 1.943844;
            Atmosphere.SteadyWind = new Vec3(System.Math.Cos(toRad) * ms, System.Math.Sin(toRad) * ms, 0);
        }
        /// <summary>A THERMAL (owner 2026-10-11, the soaring shots): the sim core's own thermal — a Gaussian core with its ring of
        /// compensating sink, the model the game's soaring uses — centred on the circle the aircraft is turning now (straight
        /// flight: under the aircraft). Off on every start.</summary>
        public bool ThermalOn => Atmosphere.Thermals.Count > 0;
        public void SetThermal(bool on, double coreMs = 3.0, double radiusM = 150)
        {
            Atmosphere.Thermals.Clear(); Atmosphere.ThermalStrengthScale = 0;
            if (!on || Ac == null) return;
            var r = Read(); var p = Ac.State.Position;
            double bank = r.RollDeg * System.Math.PI / 180, v = r.Ktas / 1.943844, hdg = r.HeadingDeg * System.Math.PI / 180;
            double turnR = System.Math.Abs(bank) > 0.09 ? v * v / (9.80665 * System.Math.Tan(System.Math.Abs(bank))) : 0;
            double side = hdg + System.Math.Sign(bank) * System.Math.PI / 2;   // the turn's centre: right of the track in a right bank
            var c = new Vec3(p.X + System.Math.Cos(side) * turnR, p.Y + System.Math.Sin(side) * turnR, 0);
            Atmosphere.Thermals.Add(new Thermal(c, radiusM, coreMs, -p.Z + 1500));
            Atmosphere.ThermalStrengthScale = 1;
        }
        /// <summary>The drawn airframe is built about the type's original CG; the physics now turns about the loaded CG.</summary>
        private Vec3 VisualOffset => Loading != null && Loading.MacM > 0 ? new Vec3(Loading.Cg0X - Config.Mass.Cg[0], 0, 0) : Vec3.Zero;

        /// <summary>Step the sim without drawing (the developed-spin preset fast-forwards the entry).</summary>
        public void StepOffscreen(float dt) => StepSim(dt);

        /// <summary>The spin never runs out of sky: below 600 m it is lifted 1,800 m, nothing else changes.</summary>
        private void WrapAltitude()
        {
            var s = Ac.State;
            if (-s.Position.Z < 600) { AltShiftM -= 1800; Ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 1800), s.Attitude, s.Velocity, s.Rates); }
        }

        /// <summary>The orbit view's camera bearing from the nose (clockwise from above) and height above the horizontal.</summary>
        public float OrbitAzDeg = 270f, OrbitElevDeg = 12f;
        /// <summary>World-fixed orbit (owner 2026-10-12, the autorotation intro: "fixed camera orientation with the airplane
        /// spinning"): OrbitAzDeg from north instead of from the nose, aimed at the CG — the camera follows the airplane's
        /// position but not its rotation. view {"orbitWorld": true}.</summary>
        public bool OrbitWorld;
        /// <summary>SPIN-AXIS orbit (Ty 2026-10-13: "centering the camera on the axis at the center of the corkscrew rather than
        /// the center of gravity … then the corkscrew nature of the smoke would also be revealed"): with OrbitWorld, the camera
        /// aims at the spin's axis instead of the airframe — the airplane circles the middle of the picture. The axis is the
        /// motion's own screw axis, p = CG + (Ω × v) / |Ω|² (world Ω and v: the points whose velocity lies along Ω),
        /// averaged over the last turn so it holds still; at the airplane's height. view {"orbitSpinAxis": true}.</summary>
        public bool OrbitSpinAxis;
        private bool SpinAxisAt(int index, RigidBodyState s, out Vec3 axis)
        {
            axis = s.Position;
            double sx = 0, sy = 0, wsum = 0, t0 = double.NaN, turn = 3.0;
            for (int k = System.Math.Max(0, index); k < Review.History.Count; k++)
            {
                var f = Review.History.Get(k); if (f == null) break;
                if (double.IsNaN(t0)) t0 = f.SimTime;
                if (t0 - f.SimTime > turn) break;
                Vec3 om = f.State.Attitude.Rotate(f.State.Rates), v = f.State.Attitude.Rotate(f.State.Velocity);
                double o2 = om.LengthSquared; if (o2 < 0.25) return false;   // (no spin: < ≈ 30°/s)
                turn = System.Math.Min(3.0, 2 * System.Math.PI / System.Math.Sqrt(o2));
                Vec3 p = f.State.Position + Vec3.Cross(om, v) * (1.0 / o2);
                sx += p.X; sy += p.Y; wsum++;
            }
            if (wsum < 3) return false;
            axis = new Vec3(sx / wsum, sy / wsum, s.Position.Z);
            return true;
        }
        private void PlaceCamera(RigidBodyState s, int histIndex = 0)
        {
            Vector3 ac = CoordinateMap.ToUnity(s.Position);
            if (_runway3D != null) _runway3D.SetActive(Current == Scenario.Flare && View != "side");
            if (Current == Scenario.Flare && View == "side")
            {
                // Side-on, orthographic, the camera on the aircraft's right (+east): runway left → right on screen = south → north.
                Cam.orthographic = true;
                // CONSTANT ZOOM (owner 2026-10-11: "I do not like the changing zoom"): one scale per airplane for the whole final
                // and flare — the camera follows the aircraft instead. Low, the ground sits a quarter of the way up; higher, the
                // aircraft stays centred and the ground drops out of the bottom. The aircraft is centred left-right (owner 2026-10-11: "the
                // airplane on the widget is not centered" — it used to sit at ~35 % from the left, the runway ahead).
                float h = Mathf.Max(0f, ac.y);
                float half = Mathf.Max(12f, 1.4f * LengthM) * 1.6f / Mathf.Max(0.3f, Zoom);   // the ground in view from ~40 ft down (C172)
                Cam.orthographicSize = half;
                _snapCam = false;
                _lead = 0f;                                                // centred left-right
                float focusY = Mathf.Max(h, 0.5f * half);
                Cam.transform.position = new Vector3(ac.x + 400f, focusY, ac.z + _lead);
                Cam.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            }
            else
            {
                Cam.orthographic = false; Cam.fieldOfView = 34f;
                float dist = (Mathf.Max(SpanM, LengthM) * 2.6f + 6f) / Zoom;
                _snapCam = false;
                if (View == "crowd")
                {
                    Cam.fieldOfView = 50f;
                    if (_crowdNed.HasValue) { Cam.fieldOfView = Mathf.Clamp(50f / Mathf.Max(0.2f, Zoom), 4f, 70f); _crowd = CoordinateMap.ToUnity(_crowdNed.Value + new Vec3(0, 0, Shown?.AltShift ?? AltShiftM)); }
                    Cam.transform.position = _crowd;
                    Cam.transform.LookAt(ac, Vector3.up);
                    return;
                }
                if (View == "orbit")
                {
                    // ORBIT (owner 2026-10-12, W12): circling the airplane, level, at OrbitElevDeg above it. OrbitAzDeg is measured
                    // from the nose, clockwise seen from above (0 ahead, 90 right, 180 behind, 270 left), relative to the
                    // airplane's heading — the capture script turns it frame by frame.
                    Vector3 nose = OrbitWorld ? CoordinateMap.ToUnity(new Vec3(1, 0, 0)) : CoordinateMap.ToUnity(s.Attitude.Rotate(new Vec3(1, 0, 0))); nose.y = 0;
                    if (nose.sqrMagnitude < 1e-4f) nose = Vector3.forward; nose.Normalize();
                    Vector3 rightH = Vector3.Cross(Vector3.up, nose).normalized;
                    float az = OrbitAzDeg * Mathf.Deg2Rad, el = OrbitElevDeg * Mathf.Deg2Rad;
                    Vector3 od = Mathf.Cos(el) * (Mathf.Cos(az) * nose + Mathf.Sin(az) * rightH) + Mathf.Sin(el) * Vector3.up;
                    // Aimed at the middle of the picture — the airplane plus the arrow pair out ahead of it (WidgetVectors: each
                    // arrow 0.55 of the airplane's length) — 0.4 of a length ahead of the CG along the flight path.
                    Vector3 vel = CoordinateMap.ToUnity(s.Attitude.Rotate(s.Velocity));
                    // World-fixed: aimed at the middle of the airframe (spinner to tail), not the CG, so the airplane is centred.
                    Vector3 aim = OrbitWorld ? ac + CoordinateMap.ToUnity(s.Attitude.Rotate(new Vec3(AirframeMidFromCg, 0, 0))) : ac + (vel.sqrMagnitude > 1f ? vel.normalized : nose) * (0.4f * LengthM);
                    if (OrbitWorld && OrbitSpinAxis && SpinAxisAt(histIndex, s, out var axis)) aim = CoordinateMap.ToUnity(axis);
                    Cam.transform.position = aim + od * dist;
                    Cam.transform.LookAt(aim, Vector3.up);
                    return;
                }
                if (View == "body")
                {
                    // AIRPLANE-FIXED (protocol 5): rigidly on the airframe, looking at its LEFT side (a touch above). The aircraft
                    // holds still in the frame; the horizon, the ground, the relative wind and the weight turn around it.
                    var off = new Vec3(0, -1, -0.18); off = off * (1.0 / off.Length);
                    Vector3 offU = CoordinateMap.ToUnity(s.Attitude.Rotate(off)) * dist;
                    Vector3 upU = CoordinateMap.ToUnity(s.Attitude.Rotate(new Vec3(0, 0, -1)));
                    Cam.transform.SetPositionAndRotation(ac + offU, Quaternion.LookRotation(-offU, upU));
                    return;
                }
                // Every view but "chase" is fixed in the world (the camera moves WITH the aircraft — the altitude wrap too —
                // but never turns with it); "locked" is the direction captured when it was engaged.
                Vector3 dir = View switch
                {
                    "chase" => -CoordinateMap.ToUnity(s.Attitude.Rotate(new Vec3(1, 0, 0))) + Vector3.up * 0.3f,
                    "locked" => _lockDir,
                    _ => Dir(View),
                };
                Cam.transform.position = ac + dir.normalized * dist;
                bool vertical = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.98f;
                Cam.transform.LookAt(ac, vertical ? Vector3.forward : Vector3.up);
            }
        }

        private void OnGUI()
        {
            // The window is a preview: the frame over a checkerboard (the transparent part shows as checks), plus who's connected.
            if (Frame == null) return;
            float side = Mathf.Min(Screen.width, Screen.height);
            var rect = new Rect((Screen.width - side) / 2, (Screen.height - side) / 2, side, side);
            Checker(rect);
            GUI.DrawTexture(rect, Frame, ScaleMode.ScaleToFit, true);
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(side * 0.018f) };
            st.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(rect.x + 8, rect.yMax - side * 0.09f, side - 16, side * 0.09f),
                $"{Current} · {AircraftId} · {(Paused ? "PAUSED (review)" : TimeScale < 0.99f ? $"×{TimeScale:0.##}" : "live")} · {(Controls.Managed ? $"{Controls.PilotName} is flying — keyboard / gamepad view-only" : Controls.SourceLabel)}\n" +
                $"Syphon/NDI \"{StreamName}\" · control tcp {WidgetServer.TcpPort} · remote http://{WidgetServer.LocalIp}:{WidgetServer.HttpPort}/   [1] flare [2] spin  arrows/A-D/W-S  Space pause  R reset  P preset", st);
        }

        private Texture2D _checker;
        private void Checker(Rect r)
        {
            if (_checker == null)
            {
                _checker = new Texture2D(16, 16) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                for (int i = 0; i < 16; i++) for (int j = 0; j < 16; j++) _checker.SetPixel(i, j, ((i / 8 + j / 8) % 2 == 0) ? new Color(0.22f, 0.22f, 0.24f) : new Color(0.3f, 0.3f, 0.32f));
                _checker.Apply();
            }
            GUI.DrawTextureWithTexCoords(r, _checker, new Rect(0, 0, r.width / 32f, r.height / 32f));
        }
    }
}

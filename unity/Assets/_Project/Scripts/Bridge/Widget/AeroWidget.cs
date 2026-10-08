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
    public sealed class AeroWidget : MonoBehaviour
    {
        public const int FrameSize = 1080;
        public const string StreamName = "Aero Widget";

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
        public enum Scenario { Flare, Spin }
        public Scenario Current { get; private set; } = Scenario.Flare;
        public string AircraftId { get; private set; } = "c172-like";
        public double Flaps { get; private set; } = 1.0;
        public bool Paused;
        public float TimeScale = 1f;
        public string View = "side";          // spin camera: side | behind | front | top | chase | locked
        /// <summary>Direction lock: what it was locked from (current | side | behind | front | top).</summary>
        public string ViewFrom = "";
        private Vector3 _lockDir = new(1f, 0.18f, -0.25f);
        public static readonly string[] Views = { "side", "behind", "front", "top", "chase", "locked" };
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
            else ViewFrom = "";
            View = name;
        }
        public readonly Dictionary<string, bool> Show = new()
        {
            ["lift"] = true, ["drag"] = true, ["weight"] = true, ["thrust"] = true, ["wind"] = true, ["total"] = true,
            ["axis"] = true, ["wheels"] = true, ["labels"] = true, ["readout"] = true, ["strips"] = false,
        };

        public AircraftConfig Config { get; private set; }
        public Aircraft Ac { get; private set; }
        private SimLoop _sim;
        private PracticeScenario _flare;   // the flare lesson's own start, constraint and (for the demo) law
        private Transform _visualRoot;
        private AirframeBuilder _builder;
        public float SpanM { get; private set; } = 11f;
        public float LengthM { get; private set; } = 8f;

        // ---- output ----
        public Camera Cam { get; private set; }
        public RenderTexture Frame { get; private set; }
        private GameObject _surface;
        private WidgetVectors _vectors;
        private WidgetOutputs _outputs;
        private WidgetServer _server;
        public WidgetControls Controls { get; private set; }
        public WidgetPresets Presets { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
            Screen.SetResolution(720, 720, FullScreenMode.Windowed);
            WorldTerrain.Active = null;   // a flat world at 0 — no scenery, no thermals
            Atmosphere.ThermalStrengthScale = 0; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.SlopeLiftEnabled = false;

            Frame = new RenderTexture(FrameSize, FrameSize, 24, RenderTextureFormat.ARGB32) { name = "AeroWidgetFrame", antiAliasing = 4 };
            Frame.Create();
            var camGo = new GameObject("WidgetCamera");
            camGo.transform.SetParent(transform, false);
            Cam = camGo.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0, 0, 0, 0);   // TRANSPARENT: only the aircraft, the line, the vectors show
            Cam.targetTexture = Frame;
            Cam.nearClipPlane = 0.1f; Cam.farClipPlane = 20000f;
            var sun = new GameObject("WidgetSun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.15f; sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sun.transform.SetParent(transform, false);

            _vectors = camGo.AddComponent<WidgetVectors>(); _vectors.Widget = this;
            Controls = gameObject.AddComponent<WidgetControls>(); Controls.Widget = this;
            Presets = gameObject.AddComponent<WidgetPresets>(); Presets.Widget = this;
            _outputs = gameObject.AddComponent<WidgetOutputs>(); _outputs.Widget = this;
            _server = gameObject.AddComponent<WidgetServer>(); _server.Widget = this;
            Load(Scenario.Flare, AircraftId, Flaps);
        }

        /// <summary>Start (or restart) a scenario with an aircraft from the fleet.</summary>
        public void Load(Scenario sc, string aircraftId, double flaps)
        {
            Current = sc; Flaps = System.Math.Clamp(flaps, 0, 1);
            if (aircraftId != AircraftId || Config == null)
            {
                AircraftId = aircraftId;
                Config = UnityAircraftConfigLoader.LoadFromStreamingAssets(aircraftId);
                BuildVisual();
            }
            Presets.Stop();
            Controls.ResetToTrim();
            Atmosphere.SteadyWind = Vec3.Zero;
            if (sc == Scenario.Flare)
            {
                var main = System.Array.Find(WorldTerrain.AirportStrips, st => st.Kind == "paved");
                var rw = new WorldTerrain.RunwayEnd(main, 0, 0, 0);
                _flare = new PracticeScenario(PracticeKind.FlareSideView, PracticeWind.Calm, Config, rw, 0.0, flapFraction: Flaps);
                Ac = _flare.Spawn(); _flare.SkipBriefing();
                Controls.SetTrimStick(_flare.TrimStick);
            }
            else
            {
                _flare = null;
                // High and slow, wings level, power off, trimmed just above the stall (clean): the spin is the pilot's to start.
                double vs = PracticeScenario.EstimateVso(Config, 2500, 0.0), v = 1.15 * vs;
                TrimSolver.Result t = TrimSolver.SolveGliderTrim(Config, v, 2500);
                double pitch = t.Converged ? t.ThetaRad : 0.1, alpha = t.Converged ? t.AlphaRad : 0.1;
                var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
                var vb = new Vec3(v * System.Math.Cos(alpha), 0, v * System.Math.Sin(alpha));
                Ac = new Aircraft(Config, new RigidBodyState(new Vec3(0, 0, -2500), att, vb, Vec3.Zero), new FlyingGame.Core.Aero.ControlDeflections(0, t.Converged ? t.ElevatorRad : 0, 0, 0, 0));
                Controls.SetTrimStick(t.Converged ? Aircraft.StickForDeflection(t.ElevatorRad, Config.Controls.Elevator) : 0);
                Controls.Throttle01 = 0;
            }
            Ac.FlapFraction = sc == Scenario.Flare ? Flaps : 0;
            Ac.CaptureForces = true;
            _sim = new SimLoop(Ac);
            if (_surface != null) _surface.SetActive(sc == Scenario.Flare);
            else if (sc == Scenario.Flare) BuildSurface();
            Paused = false;
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
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f) * TimeScale;
            Controls.Poll();
            Presets.Tick(dt);
            if (!Paused && dt > 0f)
            {
                ControlInputs inputs = Controls.Inputs;
                if (_flare != null)
                {
                    // The flare: the pilot's elevator and power (the lesson keeps it straight and on the centreline — the
                    // side view is longitudinal only); the demo preset flies the lesson's own law.
                    ControlInputs merged = _flare.Step(Ac, inputs, dt);
                    var auto = _flare.Autopilot;
                    inputs = Presets.FlareDemo ? auto : new ControlInputs(merged.Aileron, inputs.Elevator, merged.Rudder, inputs.ThrottleLever, false, inputs.ElevatorFree, false);
                    Ac.BrakeInput = Controls.Brake01;
                }
                _sim.RunFor(dt, inputs);
                if (_flare != null) _flare.ConstrainLongitudinal(Ac);
                else WrapAltitude();
            }
            // Pose the aircraft; surfaces follow the sim's deflections; props spin.
            var s = Ac.State;
            _visualRoot.position = CoordinateMap.ToUnity(s.Position);
            _visualRoot.rotation = CoordinateMap.ToUnity(s.Attitude);
            var d = Ac.CurrentDeflections;
            _builder.SetDeflections((float)d.AileronRad, (float)d.ElevatorRad, (float)d.RudderRad, (float)d.SpoilerFraction);
            _builder.SpinProps(_ => (float)Ac.EngineRpm, Paused ? 0f : dt);
            PlaceCamera();
        }

        /// <summary>Step the sim without drawing (the developed-spin preset fast-forwards the entry).</summary>
        public void StepOffscreen(float dt) { _sim.RunFor(dt, Controls.Inputs); if (_flare == null) WrapAltitude(); }

        /// <summary>The spin never runs out of sky: below 600 m it is lifted 1,800 m, nothing else changes.</summary>
        private void WrapAltitude()
        {
            var s = Ac.State;
            if (-s.Position.Z < 600) Ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 1800), s.Attitude, s.Velocity, s.Rates);
        }

        private void PlaceCamera()
        {
            var s = Ac.State;
            Vector3 ac = CoordinateMap.ToUnity(s.Position);
            if (Current == Scenario.Flare)
            {
                // Side-on, orthographic, the camera on the aircraft's right (+east): runway left → right on screen = south → north.
                Cam.orthographic = true;
                float h = Mathf.Max(0f, ac.y);
                float half = Mathf.Max(Mathf.Max(5.5f, 0.6f * LengthM), 0.65f * h + 4f);
                Cam.orthographicSize = Mathf.Lerp(Cam.orthographicSize <= 0 ? half : Cam.orthographicSize, half, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
                float focusY = Mathf.Min(ac.y, 0.55f * h + 0.35f * Cam.orthographicSize);
                Cam.transform.position = new Vector3(ac.x + 400f, focusY, ac.z);
                Cam.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            }
            else
            {
                Cam.orthographic = false; Cam.fieldOfView = 34f;
                float dist = Mathf.Max(SpanM, LengthM) * 2.6f + 6f;
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

        // ---- readouts for the labels and the state stream ----
        public struct Readout
        {
            public double Kias, Ktas, AlphaDeg, BetaDeg, PitchDeg, RollDeg, HeadingDeg, SinkFpm, HeightFt, YawRateDps, RollRateDps, PitchRateDps, LoadFactor, Q;
            public double LeftAlphaDeg, RightAlphaDeg; public bool LeftStalled, RightStalled, OnGround;
        }

        public Readout Read()
        {
            var s = Ac.State; var r = new Readout();
            Vec3 vAir = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
            double tas = vAir.Length, rho = Atmosphere.DensityAtAltitude(-s.Position.Z);
            r.Ktas = tas * 1.943844; r.Kias = tas * System.Math.Sqrt(rho / 1.225) * 1.943844; r.Q = 0.5 * rho * tas * tas;
            r.AlphaDeg = tas > 1 ? System.Math.Atan2(vAir.Z, vAir.X) * 57.2958 : 0;
            r.BetaDeg = tas > 1 ? System.Math.Asin(System.Math.Clamp(vAir.Y / tas, -1, 1)) * 57.2958 : 0;
            var q = s.Attitude;
            r.RollDeg = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.2958;
            r.PitchDeg = System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.2958;
            r.HeadingDeg = (System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 57.2958 + 360) % 360;
            Vec3 vW = q.Rotate(s.Velocity);
            r.SinkFpm = vW.Z * 196.85; r.HeightFt = -s.Position.Z * 3.28084;
            r.RollRateDps = s.Rates.X * 57.2958; r.PitchRateDps = s.Rates.Y * 57.2958; r.YawRateDps = s.Rates.Z * 57.2958;
            r.LoadFactor = Ac.LoadFactorZ;
            // Each wing's local α at mid-semispan: the airflow there includes the rotation (ω × r) — the heart of the spin.
            double half = SpanM * 0.5;
            Vec3 Local(double y) { Vec3 rr = new(0, y, 0); return vAir + Vec3.Cross(s.Rates, rr); }
            Vec3 vl = Local(-half * 0.5), vr = Local(half * 0.5);
            r.LeftAlphaDeg = System.Math.Atan2(vl.Z, System.Math.Max(0.1, vl.X)) * 57.2958;
            r.RightAlphaDeg = System.Math.Atan2(vr.Z, System.Math.Max(0.1, vr.X)) * 57.2958;
            double stallDeg = 15.0;
            r.LeftStalled = r.LeftAlphaDeg > stallDeg; r.RightStalled = r.RightAlphaDeg > stallDeg;
            r.OnGround = LandingGear.AnyMainWheelOnGround(Config, s);
            return r;
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
                $"{Current} · {AircraftId} · {(Paused ? "PAUSED" : TimeScale < 0.99f ? $"×{TimeScale:0.##}" : "live")} · {Controls.SourceLabel}\n" +
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

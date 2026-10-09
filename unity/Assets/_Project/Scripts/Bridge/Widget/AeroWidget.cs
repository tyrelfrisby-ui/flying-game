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
        public enum Scenario { Flare, Spin, Cruise }
        public Scenario Current { get; private set; } = Scenario.Flare;
        // (AircraftId lives in AeroHost)
        public double Flaps { get; private set; } = 1.0;
        public float TimeScale = 1f;
        public string View = "side";          // spin camera: side | behind | front | top | chase | locked
        /// <summary>Direction lock: what it was locked from (current | side | behind | front | top).</summary>
        public string ViewFrom = "";
        private Vector3 _lockDir = new(1f, 0.18f, -0.25f);
        public static readonly string[] Views = { "side", "behind", "front", "top", "chase", "locked", "body" };
        /// <summary>The start condition (protocol 5): "cruise" | "final" | "spin", or null for an old-style scenario start.</summary>
        public string Condition { get; private set; }
        /// <summary>FINAL's view is the flare's side view and can't be changed.</summary>
        public bool ViewFixed => Condition == "final";
        public static readonly string[] Conditions = { "cruise", "final", "spin" };
        private double? _finalStartFt;
        public WorldTerrain.RunwayEnd Runway { get; private set; }
        private bool _snapCam;
        private float _lead, _wide;
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
            _display = gameObject.AddComponent<WidgetControlsDisplay>(); _display.Widget = this;
            Controls = gameObject.AddComponent<WidgetControls>(); Controls.Widget = this;
            Presets = gameObject.AddComponent<WidgetPresets>(); Presets.Widget = this;
            _review = new WidgetReview(this);
            Loading = new WidgetLoading(this); Autopilot = new WidgetAutopilot(this); Curves = new WidgetCurves(); _lessons = new WidgetLessons(this);
            _outputs = gameObject.AddComponent<WidgetOutputs>(); _outputs.Widget = this;
            _server = gameObject.AddComponent<WidgetServer>(); _server.Widget = this;
            Load(Scenario.Flare, AircraftId, Flaps);
        }

        /// <summary>Start (or restart) a scenario with an aircraft from the fleet.</summary>
        public void Load(Scenario sc, string aircraftId, double flaps)
        {
            Current = sc; Condition = null; _jetFinalThr = -1; bool approachFlaps = flaps < 0; Flaps = System.Math.Clamp(flaps, 0, 1);
            bool newConfig = aircraftId != AircraftId || Config == null;
            Autopilot?.Off();
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
            if (Controls.Spec.Engines == 0) Controls.Spoilers = Ac.CurrentDeflections.SpoilerFraction;   // a glider's approach spoilers
            bool gearUp = sc == Scenario.Cruise && Controls.Spec.Retractable;   // cruise: gear up
            Ac.SetGear(!gearUp, immediate: true); Controls.GearDown = !gearUp;
            _snapCam = true;
            if (newConfig || Loading.MacM <= 0) Loading.Configure(Config, false, 0, 0);   // a new type: its own default loading (the same type keeps the current one)
            _sim = new SimLoop(Ac);
            Review?.Clear(); _shown = null;
            if (_surface != null) _surface.SetActive(sc == Scenario.Flare);
            else if (sc == Scenario.Flare) BuildSurface();
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
                    return view != null && view != "side" ? "view is fixed in final" : null;
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
            if (Condition == "final") return "view is fixed in final";
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
            _sim = new SimLoop(Ac);
            Controls.SetTrimStick(Aircraft.StickForDeflection(X[1], Config.Controls.Elevator));
            _jetFinalThr = X[2];
        }
        private double _jetFinalThr = -1;

        public const double CruiseAltM = 900;   // ~3,000 ft over the flat world

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
            Presets.Tick(dt);
            Loading.Tick(dt);
            Lessons.Tick(dt);
            Autopilot.Tick(dt);
            if (dt > 0f) StepSim(dt);
            ShowLive(dt);
            Review.Record(dt);
        }

        /// <summary>One step of the physics with the current controls (the flare merges in the lesson's lateral law).</summary>
        public void StepSim(float dt)
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
            _sim.RunFor(dt, inputs);
            if (_flare != null) _flare.ConstrainLongitudinal(Ac);
            else if (Current == Scenario.Spin) WrapAltitude();
        }

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
            PlaceCamera();
            ApplyVisualScale();
        }

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
            Cam.orthographic = f.Ortho; Cam.orthographicSize = f.OrthoSize; Cam.fieldOfView = f.Fov;
            Cam.transform.SetPositionAndRotation(f.CamPos, f.CamRot);
            ApplyVisualScale();
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
        /// <summary>The drawn airframe is built about the type's original CG; the physics now turns about the loaded CG.</summary>
        private Vec3 VisualOffset => Loading != null && Loading.MacM > 0 ? new Vec3(Loading.Cg0X - Config.Mass.Cg[0], 0, 0) : Vec3.Zero;

        /// <summary>Step the sim without drawing (the developed-spin preset fast-forwards the entry).</summary>
        public void StepOffscreen(float dt) => StepSim(dt);

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
                // FINAL: keep the runway ahead in frame — the threshold and the aim point plus 150 m beyond — with the
                // aircraft near the left edge; as it closes on the aim point this narrows into the flare's own framing.
                float lead = 0f, wide = 0f;
                if (Condition == "final")
                {
                    float aimZ = (float)(-Runway.LengthM / 2 + PracticeScenario.NumbersPastThresholdM);   // the runway runs north (Unity +z) from its threshold
                    float span = aimZ + 150f - ac.z;
                    float aspect = Mathf.Max(0.3f, Cam.aspect);
                    float spanHalf = span / 0.64f / (2f * aspect);   // frame width = span / 0.64: aircraft at 28 % (room for its labels), the far point at 92 %
                    if (spanHalf > half)
                    {
                        float blend = Mathf.Clamp01((spanHalf - half) / 40f);   // eases into the flare framing over the last ~80 m
                        lead = 0.22f * (span / 0.64f) * blend; wide = blend;
                        half = spanHalf;
                    }
                }
                float k = _snapCam || Cam.orthographicSize <= 0 ? 1f : 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
                Cam.orthographicSize = Mathf.Lerp(Cam.orthographicSize, half, k);
                _lead = Mathf.Lerp(_lead, lead, k); _wide = Mathf.Lerp(_wide, wide, k);
                _snapCam = false;
                float focusY = Mathf.Min(ac.y, 0.55f * h + 0.35f * Cam.orthographicSize);
                focusY = Mathf.Lerp(focusY, 0.3f * Cam.orthographicSize, _wide);   // wide final framing: the ground 35 % up (room below for the weight)
                Cam.transform.position = new Vector3(ac.x + 400f, focusY, ac.z + _lead);
                Cam.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            }
            else
            {
                Cam.orthographic = false; Cam.fieldOfView = 34f;
                float dist = Mathf.Max(SpanM, LengthM) * 2.6f + 6f;
                _snapCam = false;
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

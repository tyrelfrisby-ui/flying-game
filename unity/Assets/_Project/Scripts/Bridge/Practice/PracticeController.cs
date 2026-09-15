using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using UnityEngine;

namespace FlyingGame.Bridge.Practice
{
    /// <summary>
    /// Runs a practice exercise (owner 2026-09-15): spawns the scenario, replaces every axis but the user's before each
    /// sim step (FlightSimDriver.InputFilter), shows the game's control positions on the pads, speaks the briefing and the
    /// hand-over, drives the side-view camera and the glideslope line, and restores everything on End.
    /// </summary>
    public sealed class PracticeController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public PracticeScenario Scenario { get; private set; }
        public bool Active { get; private set; }
        public PracticeKind Kind { get; private set; }
        public PracticeWind Wind { get; private set; }

        private TouchFlightControls _touch;
        private ChaseCamera _chase;
        private LineRenderer _slope;
        private bool _handoverSpoken, _finishSpoken, _beginning;
        private float _sideBlend;   // 0 = frame centred on the glideslope, 1 = on the runway

        private void Awake()
        {
            Driver ??= GetComponent<FlightSimDriver>();
            _touch = GetComponent<TouchFlightControls>();
            Driver.AircraftChanged += OnAircraftChanged;
        }
        private void OnDestroy() { if (Driver != null) Driver.AircraftChanged -= OnAircraftChanged; }

        /// <summary>RESET (or an aircraft switch) while an exercise is running restarts it.</summary>
        private void OnAircraftChanged() { if (Active && !_beginning) Begin(Kind, Wind); }

        public void Begin(PracticeKind kind, PracticeWind wind)
        {
            _beginning = true;
            try
            {
                Kind = kind; Wind = wind;
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(Driver.AircraftId);
                SessionSettings.ApplyFeel(cfg);
                WorldTerrain.RunwayEnd rw = SessionSettings.ChosenRunway();
                double surface = SessionSettings.Airport.ElevationM;
                Scenario = new PracticeScenario(kind, wind, cfg, rw, surface, seed: Random.Range(1, 9999));
                Aircraft ac = Scenario.Spawn();
                Driver.AdoptSim(new SimLoop(ac));
                Driver.InputFilter = Filter;
                Driver.ForceCapture = Scenario.SideView;
                Active = true; _handoverSpoken = false; _finishSpoken = false; _sideBlend = 0f;
                SetupCamera();
                SetupSlopeLine();
                PilotVoice.Say(Scenario.Title + ". " + Scenario.Instructions, 0.52f, 1.0f);
            }
            finally { _beginning = false; }
        }

        public void Restart() { if (Scenario != null) Begin(Kind, Wind); }

        public void End()
        {
            if (!Active) return;
            Active = false;
            Driver.InputFilter = null;
            Driver.ForceCapture = false;
            if (_touch != null) { _touch.DisplayOverride = null; _touch.GameAileron = _touch.GameElevator = _touch.GameRudder = _touch.GameThrottle = false; }
            if (_chase != null) _chase.SideView = false;
            if (_slope != null) Destroy(_slope.gameObject);
            SessionSettings.ApplyWeather();   // back to the session's own wind
        }

        private ControlInputs Filter(ControlInputs user, float dt)
        {
            if (!Active || Scenario == null || Driver.Sim == null) return user;
            Aircraft ac = Driver.Sim.Aircraft;
            ControlInputs merged = Scenario.Step(ac, user, dt);
            if (Scenario.GameBrake > 0)
            {
                ac.BrakeInput = System.Math.Max(ac.BrakeInput, Scenario.GameBrake);
                ac.BrakeBias = Scenario.GameBrakeBias;
            }
            if (_touch != null)
            {
                _touch.DisplayOverride = merged;
                _touch.GameAileron = Scenario.GameAileron; _touch.GameElevator = Scenario.GameElevator;
                _touch.GameRudder = Scenario.GameRudder; _touch.GameThrottle = Scenario.GameThrottle;
            }
            if (Scenario.Phase == PracticePhase.Live && !_handoverSpoken) { _handoverSpoken = true; PilotVoice.Say(Scenario.HandoverLine, 0.55f, 1.0f); }
            if (Scenario.Phase == PracticePhase.Finished && !_finishSpoken)
            {
                _finishSpoken = true;
                PilotVoice.Say($"{Scenario.Verdict} Score {Scenario.Score:F0} percent.", 0.52f, 1.0f);
                if (_touch != null) { _touch.GameAileron = _touch.GameElevator = _touch.GameRudder = _touch.GameThrottle = false; }
            }
            return merged;
        }

        // ---- side view camera + glideslope line ------------------------------------------------------------------
        private void SetupCamera()
        {
            if (Camera.main == null) return;
            _chase = Camera.main.GetComponent<ChaseCamera>();
            if (_chase == null) return;
            if (!Scenario.SideView) { _chase.SideView = false; return; }
            var rw = Scenario.Runway;
            // Sim (x north, y east) → Unity (x = east, z = north): along = (AlongY, 0, AlongX); right of the runway = (AlongX, 0, -AlongY).
            _chase.SideRight = new Vector3((float)rw.AlongX, 0f, -(float)rw.AlongY).normalized;
            _chase.SideDistance = Mathf.Clamp(_chase.Distance * 1.9f, 16f, 90f);
            _chase.SideFocusY = FocusHeight();
            _chase.SideView = true;
        }

        private float FocusHeight()
        {
            float ground = (float)Scenario.SurfaceM;
            if (!Scenario.Approach) return ground + 1.5f;
            float slope = ground + (float)(Scenario.GlideslopeHeightAt(Scenario.AlongM) + Scenario.GearDropM);
            float runway = ground + 1.5f;
            return Mathf.Lerp(slope, runway, _sideBlend);
        }

        private void LateUpdate()
        {
            if (!Active || Scenario == null || _chase == null || !Scenario.SideView) return;
            // Approach: centred on the glideslope until the runway, then (over ~3 s) on the runway like the flare exercise.
            if (Scenario.Approach && Scenario.AlongM > -60) _sideBlend = Mathf.MoveTowards(_sideBlend, 1f, Time.deltaTime / 3f);
            _chase.SideFocusY = FocusHeight();
        }

        private void SetupSlopeLine()
        {
            if (_slope != null) Destroy(_slope.gameObject);
            if (!Scenario.Approach) return;
            var go = new GameObject("Glideslope");
            _slope = go.AddComponent<LineRenderer>();
            _slope.material = new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = new Color(0.3f, 1f, 0.4f, 0.8f) };
            _slope.startColor = _slope.endColor = new Color(0.3f, 1f, 0.4f, 0.8f);
            _slope.startWidth = _slope.endWidth = 0.5f;
            _slope.useWorldSpace = true;
            var rw = Scenario.Runway;
            (double tx, double ty) = rw.Threshold;
            double aimX = tx + rw.AlongX * PracticeScenario.AimPastThresholdM, aimY = ty + rw.AlongY * PracticeScenario.AimPastThresholdM;
            const int n = 40; const double reach = 2600;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                double back = reach * i / (n - 1);
                double h = Scenario.SurfaceM + back * System.Math.Tan(Scenario.GlideslopeRad);
                pts[i] = CoordinateMap.ToUnity(new Vec3(aimX - rw.AlongX * back, aimY - rw.AlongY * back, -h));
            }
            _slope.positionCount = n; _slope.SetPositions(pts);
        }
    }
}

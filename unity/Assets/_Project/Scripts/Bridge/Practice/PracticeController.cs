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
        private ChaseCamera.View? _viewBefore;
        private LineRenderer _slope;
        private bool _handoverSpoken, _finishSpoken, _beginning;
        private float _sideBlend;   // 0 = frame centred on the glideslope, 1 = on the runway
        private bool _bubblesWere; private float _stallFocusY;
        /// <summary>Lesson pictures rendered from the game at Begin (index = LessonPages index).</summary>
        public Texture2D[] LessonPictures { get; private set; } = System.Array.Empty<Texture2D>();
        public int LessonPage { get; set; }
        /// <summary>Briefing pacing (owner 2026-09-16): the sim is FROZEN while the pages and the card are up; the dismiss button
        /// starts a 3-2-1 countdown (real time), and only at zero does the sim run, with the user's axes live at once.</summary>
        public bool Counting { get; private set; }
        public static bool SelfTestStudent;
        public float CountdownLeft => Counting ? Mathf.Max(0f, _countEnd - Time.realtimeSinceStartup) : 0f;
        private float _countEnd;
        public void StartCountdown() { if (!Active || Counting || Scenario == null || Scenario.Phase != PracticePhase.Briefing) return; Counting = true; _countEnd = Time.realtimeSinceStartup + 3.2f; PilotVoice.Say("Three. Two. One.", 0.5f, 1.0f); }

        private void Update()
        {
            if (!Active || Scenario == null) return;
            if (Scenario.Phase == PracticePhase.Briefing)
            {
                Time.timeScale = 0f;   // no live play behind the briefing
                if (Counting && Time.realtimeSinceStartup >= _countEnd)
                {
                    Counting = false;
                    Scenario.SkipBriefing();
                    Time.timeScale = 1f;
                }
            }
            else if (!SessionSettings.MenuOpen && !SessionSettings.ReplayActive && Time.timeScale == 0f) Time.timeScale = 1f;
        }

        private void Awake()
        {
            Driver ??= GetComponent<FlightSimDriver>();
            _touch = GetComponent<TouchFlightControls>();
            Driver.AircraftChanged += OnAircraftChanged;
            Driver.PostStep += AfterStep;
        }

        /// <summary>Side views are longitudinal only: roll, yaw, sideslip and cross-track are zeroed after every step.</summary>
        private void AfterStep(Aircraft ac) { if (Active && Scenario != null) Scenario.ConstrainLongitudinal(ac); }
        private void OnDestroy() { if (Driver != null) { Driver.AircraftChanged -= OnAircraftChanged; Driver.PostStep -= AfterStep; } }

        /// <summary>RESET (or an aircraft switch) while an exercise is running restarts it.</summary>
        private void OnAircraftChanged() { if (Active && !_beginning) Begin(Kind, Wind, _userAxes); }

        private int _userAxes = -1;
        public void Begin(PracticeKind kind, PracticeWind wind, int userAxes = -1)
        {
            _beginning = true;
            try
            {
                Kind = kind; Wind = wind; _userAxes = userAxes;
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(Driver.AircraftId);
                SessionSettings.ApplyFeel(cfg);
                WorldTerrain.RunwayEnd rw = SessionSettings.ChosenRunway();
                double surface = SessionSettings.Airport.ElevationM;
                Scenario = new PracticeScenario(kind, wind, cfg, rw, surface, seed: Random.Range(1, 9999), userAxes: _userAxes, flapFraction: SessionSettings.LessonFlaps);
                Aircraft ac = Scenario.Spawn();
                Driver.AdoptSim(new SimLoop(ac));
                _touch?.PresetPitchTrim(Scenario.TrimStick);   // on speed AND in trim: hands-off flies the starting path
                Driver.InputFilter = Filter;
                // Only the controls this lesson gives you (owner 2026-10-03): elevator (and throttle) lessons get tall strips.
                if (_touch != null)
                {
                    _touch.StripElevator = Scenario.UserElevator && !Scenario.UserAileron && !Scenario.UserRudder;
                    _touch.StripThrottle = _touch.StripElevator && Scenario.UserThrottle;
                }
                Driver.ForceCapture = Scenario.SideView;
                Driver.GroundReferenceForced = !Scenario.Airwork;   // runway lessons: camera + path vector relative to the runway
                Active = true; _handoverSpoken = false; _finishSpoken = false; _sideBlend = 0f; Counting = false;
                if (kind == PracticeKind.StallSideView) { _bubblesWere = SessionSettings.BubblesOn; SessionSettings.BubblesOn = true; }   // the air must be visible
                _stallFocusY = CoordinateMap.ToUnity(ac.State.Position).y;
                SetupCamera();
                SetupSlopeLine();
                RenderLessonPictures();
                LessonPage = 0;
                if (Scenario.LessonPages.Length > 0) PilotVoice.Say(Scenario.LessonPages[0].title + ". " + Scenario.LessonPages[0].text, 0.52f, 1.0f);
                else PilotVoice.Say(Scenario.Title + ". " + Scenario.Instructions, 0.52f, 1.0f);
            }
            finally { _beginning = false; }
        }

        public void Restart() { if (Scenario != null) Begin(Kind, Wind, _userAxes); }

        /// <summary>Next briefing page: speaks it; past the last picture the standard card (title + instructions) is read.</summary>
        public void NextLessonPage()
        {
            LessonPage++;
            if (LessonPage < Scenario.LessonPages.Length) PilotVoice.Say(Scenario.LessonPages[LessonPage].title + ". " + Scenario.LessonPages[LessonPage].text, 0.52f, 1.0f);
            else PilotVoice.Say(Scenario.Title + ". " + Scenario.Instructions, 0.52f, 1.0f);
        }

        private void RenderLessonPictures()
        {
            foreach (var t in LessonPictures) if (t != null) Destroy(t);
            LessonPictures = System.Array.Empty<Texture2D>();
            if (Scenario.LessonPages.Length == 0) return;
            try
            {
                Driver.transform.SetPositionAndRotation(CoordinateMap.ToUnity(Driver.Sim.Aircraft.State.Position), CoordinateMap.ToUnity(Driver.Sim.Aircraft.State.Attitude));
                var sc = Scenario;
                LessonPictures = sc.Kind switch
                {
                    PracticeKind.Straight => new[] { LessonIllustrator.RearViewBank(Driver.transform), LessonIllustrator.TopViewWeathervane(Driver.transform) },
                    PracticeKind.ClimbLevelDescend => new[] { LessonIllustrator.TopViewPropYaw(Driver.transform), LessonIllustrator.RearViewTrim(Driver.transform) },
                    PracticeKind.GlideRear or PracticeKind.GlideSide => new[] { LessonIllustrator.PolarChart(sc), LessonIllustrator.SideViewGlides(Driver.transform, sc) },
                    _ => new[] { LessonIllustrator.ClimbChart(sc), LessonIllustrator.SideViewClimbs(Driver.transform, sc) },
                };
            }
            catch (System.Exception e) { Debug.LogWarning("lesson pictures: " + e.Message); }
        }

        public void End()
        {
            if (!Active) return;
            Active = false; Counting = false;
            if (_touch != null) { _touch.StripElevator = false; _touch.StripThrottle = false; }
            if (_chase != null && _viewBefore != null) { _chase.SetView(_viewBefore.Value); _viewBefore = null; }
            if (!SessionSettings.MenuOpen) Time.timeScale = 1f;
            Driver.InputFilter = null;
            Driver.ForceCapture = false;
            Driver.GroundReferenceForced = false;
            if (_touch != null) { _touch.DisplayOverride = null; _touch.GameAileron = _touch.GameElevator = _touch.GameRudder = _touch.GameThrottle = false; }
            if (_chase != null) _chase.SideView = false;
            Side2DView.Exit(Camera.main);
            if (_slope != null) Destroy(_slope.gameObject);
            if (Kind == PracticeKind.StallSideView) SessionSettings.BubblesOn = _bubblesWere;
            SessionSettings.ApplyWeather();   // back to the session's own wind
        }

        private ControlInputs Filter(ControlInputs user, float dt)
        {
            if (!Active || Scenario == null || Driver.Sim == null) return user;
            Aircraft ac = Driver.Sim.Aircraft;
            if (Scenario.LessonPages.Length > 0 && LessonPage < Scenario.LessonPages.Length) Scenario.HoldBriefing();
            if (SelfTestStudent && Scenario.Phase == PracticePhase.Live) user = Scenario.Autopilot;   // AERO_SELFTEST=lesson: the game flies as the student
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
            if (!Scenario.SideView)
            {
                _chase.SideView = false; Side2DView.Exit(Camera.main);
                // Runway lessons from behind (owner 2026-10-03: "major lag … can't flare"): the relative-wind view follows the
                // flight PATH, which bends ~1 s after the nose — the flare cue (the nose against the horizon) was hidden. These
                // lessons open in the attitude-locked TAIL view; VIEW still switches.
                if (Scenario.Descending)
                {
                    if (_viewBefore == null) _viewBefore = _chase.CurrentView;
                    _chase.SetView(ChaseCamera.View.Tail);
                }
                return;
            }
            var rw = Scenario.Runway;
            // Sim (x north, y east) → Unity (x = east, z = north): along = (AlongY, 0, AlongX); right of the runway = (AlongX, 0, -AlongY).
            _chase.SideRight = new Vector3((float)rw.AlongX, 0f, -(float)rw.AlongY).normalized;
            _chase.SideDistance = Mathf.Clamp(_chase.Distance * 1.9f, 16f, 90f);
            _chase.SideFocusY = FocusHeight();
            _chase.SideView = true;
            // Owner 2026-10-03: side-view lessons are 2-D — flat side-on picture, no 3-D world.
            (double tx, double ty) = rw.Threshold;
            Vector3 thr = CoordinateMap.ToUnity(new Vec3(tx, ty, -Scenario.SurfaceM));
            Side2DView.Enter(Camera.main, _chase.SideRight, new Vector3((float)rw.AlongY, 0f, (float)rw.AlongX), (float)Scenario.SurfaceM, thr, (float)rw.LengthM);
            Side2DView.Frame(Camera.main, _chase.SideDistance);
        }

        private float FocusHeight()
        {
            float ground = (float)Scenario.SurfaceM;
            if (Scenario.Airwork) return _stallFocusY;   // airwork side views: the aircraft stays centred, the world moves past
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
            if (Scenario.Airwork) _stallFocusY = Mathf.Lerp(_stallFocusY, transform.position.y, 1f - Mathf.Exp(-3f * Time.deltaTime));
            _chase.SideFocusY = FocusHeight();
            Side2DView.Frame(Camera.main, _chase.SideDistance);
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

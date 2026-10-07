using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Air Racing: feeds the aircraft position to the Core scorer and shows the race line in the HUD.</summary>
    public sealed class RaceController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public AirRace Race { get; private set; }
        public bool Active { get; private set; }

        /// <summary>Plateau whose course is raced: the one the session started from.</summary>
        public int Plateau { get; private set; }

        public void Begin()
        {
            Plateau = SessionSettings.AirportIndex;
            Race = new AirRace(Plateau); Race.Reset(); Active = true;
            foreach (PylonTopBurst top in WorldBuilder.PylonTops.Values) if (top != null) top.ResetTop();
        }
        public void End() { Active = false; }

        private AirRace[] _strikeRaces;   // pylon strikes are live even outside a race (free flight through any plateau's course)

        private void Update()
        {
            if (Driver?.Sim == null) return;
            var ac = Driver.Sim.Aircraft; var s = ac.State; var q = s.Attitude;
            if (Active && Race != null)
            {
                double bank = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
                Race.Update(s.Position, bank, Time.deltaTime);
            }
            // Wing tips against the pylons of every plateau's course (the active race scores its own plateau).
            if (_strikeRaces == null || _strikeRaces.Length != WorldTerrain.PlateauCount)
            {
                _strikeRaces = new AirRace[WorldTerrain.PlateauCount];
                for (int p = 0; p < _strikeRaces.Length; p++) _strikeRaces[p] = new AirRace(p);
            }
            double half = 0; foreach (var sf in ac.Config.Surfaces) foreach (var st in sf.Strips) half = System.Math.Max(half, System.Math.Abs(st.Pos[1]));
            if (ac.IsLost(FlyingGame.Core.AirframeComponent.WingLeft) || ac.IsLost(FlyingGame.Core.AirframeComponent.WingRight)) half *= 0.3;
            else if (ac.IsLost(FlyingGame.Core.AirframeComponent.WingLeftOuter) || ac.IsLost(FlyingGame.Core.AirframeComponent.WingRightOuter)) half *= (float)FlyingGame.Core.WingPanels.OuterFraction;
            Vec3 left = s.Position + q.Rotate(new Vec3(0, -half, 0)), right = s.Position + q.Rotate(new Vec3(0, half, 0));
            for (int p = 0; p < _strikeRaces.Length; p++)
            {
                AirRace strikes = Active && Race != null && p == Plateau ? Race : _strikeRaces[p];
                var hit = strikes.CheckPylonStrike(left, right, Time.deltaTime);
                if (!hit.HasValue) continue;
                if (WorldBuilder.PylonTops.TryGetValue((p, hit.Value.element, hit.Value.side), out PylonTopBurst top) && top != null) top.Launch();
                Driver.GetComponent<FlightAudio>()?.PylonBurst();
            }
        }

        public string Line => !Active || Race == null ? null
            : Race.Finished ? $"AIR RACE  {Race.LastEvent}   (penalties {Race.PenaltySec:F0} s)"
            : $"AIR RACE  next {Race.Next + 1}/{RaceCourse.Elements.Length}   {Race.TotalSec:F1} s   {Race.LastEvent}";
    }

    /// <summary>
    /// STOL contest (owner 2026-10-07): three landings and three takeoffs on the grass strip from the white line — flags either
    /// side, judges at the line, thin white lines every 10 ft after it. Landing: on final at 1.1 Vs, full flaps; takeoff: at
    /// rest with the mains on the line. When an attempt ends a flagger walks out to the spot (the stop / lift-off point) and
    /// holds up a flag (red for a landing, blue for a takeoff) — they stay out there, so the replay shows them. Score =
    /// average takeoff + average landing.
    /// </summary>
    public sealed class StolController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public StolContest Contest { get; private set; }
        public bool Active { get; private set; }
        private GameObject _field;
        private float _doneT = -1f;
        private double _elev;

        public void Begin(WorldTerrain.Airport a)
        {
            End();
            Contest = StolContest.ForAirport(a); Active = true; _elev = a.ElevationM; _doneT = -1f;
            BuildField();
            PlaceForAttempt();
        }

        public void End()
        {
            Active = false;
            if (_field != null) Destroy(_field);
            _field = null;
        }

        private void PlaceForAttempt()
        {
            if (Driver?.Sim == null || Contest == null) return;
            var cfg = Driver.Sim.Aircraft.Config;
            var touch = Object.FindFirstObjectByType<TouchFlightControls>();
            if (Contest.Kind == StolContest.Kinds.Landing)
            {
                var (st, elev, thr, _) = FlyingGame.Sim.StolSpawn.Final(cfg, Contest, _elev);
                var ac = new FlyingGame.Sim.Aircraft(cfg, st, new FlyingGame.Core.Aero.ControlDeflections(0, elev, 0, 0, 1.0)) { FlapFraction = 1.0 };
                Driver.AdoptSim(new FlyingGame.Sim.SimLoop(ac));
                double stick = FlyingGame.Sim.Aircraft.StickForDeflection(elev, cfg.Controls.Elevator);
                Driver.TrimStick = stick; touch?.PresetPitchTrim(stick); touch?.SetThrottleFromLever(1 - 2 * thr);
            }
            else
            {
                var ac = new FlyingGame.Sim.Aircraft(cfg, FlyingGame.Sim.StolSpawn.OnTheLine(cfg, Contest, _elev)) { FlapFraction = Driver.Sim.Aircraft.FlapFraction };
                Driver.AdoptSim(new FlyingGame.Sim.SimLoop(ac));
                Driver.TrimStick = 0; touch?.PresetPitchTrim(0); touch?.SetThrottleFromLever(1.0);   // idle: the pilot opens it up
            }
        }

        private void Update()
        {
            if (!Active || Contest == null || Driver?.Sim == null || Contest.Phase == StolContest.Phases.Finished) return;
            var ac = Driver.Sim.Aircraft; var s = ac.State;
            bool onGround = LandingGear.AnyMainWheelOnGround(ac.Config, s);
            var vW = s.Attitude.Rotate(s.Velocity);
            double gs = System.Math.Sqrt(vW.X * vW.X + vW.Y * vW.Y);
            Contest.Update(FlyingGame.Sim.StolSpawn.Mains(ac.Config, s), onGround, gs, Time.deltaTime);
            if (Contest.AttemptDone && _doneT < 0f)
            {
                _doneT = Time.time;
                if (Contest.Phase != StolContest.Phases.Foul && Contest.FlagPastLineM.HasValue)
                    Flagger(Contest.FlagPastLineM.Value, Contest.Kind == StolContest.Kinds.Landing ? new Color(0.9f, 0.12f, 0.1f) : new Color(0.12f, 0.35f, 0.95f));
            }
            // A few seconds to see the flag, then the next attempt.
            if (_doneT >= 0f && Time.time - _doneT > 4f)
            {
                _doneT = -1f;
                if (Contest.Next()) PlaceForAttempt();
            }
        }

        // ---- the field: line, flags, judges, 10 ft marks, flaggers ----------------------------------------------------

        private Vector3 At(double pastLineM, double sideM, double up = 0)
        {
            double x = Contest.XAt(pastLineM), y = Contest.StripY + sideM;
            double g = WorldTerrain.GroundHeightAt(x, y);
            return CoordinateMap.ToUnity(new Vec3(x, y, -(g + up)));
        }

        private void BuildField()
        {
            _field = new GameObject("StolContestField");
            var white = new Color(0.97f, 0.97f, 0.97f);
            float halfW = (float)(Contest.StripHalfW - 2 + 1.5);
            // The line: a broad white stripe across the strip (on the ground's swell), with a flag at each end.
            Stripe(0, halfW * 2 + 2f, 0.6f, white);
            for (double d = StolContest.MarkSpacingM; d <= StolContest.MarkedLengthM + 0.01; d += StolContest.MarkSpacingM)
            {
                bool fifty = System.Math.Abs((d / StolContest.MarkSpacingM) % 5) < 0.01;
                Stripe(d, halfW * 2, fifty ? 0.22f : 0.1f, white);
            }
            foreach (int side in new[] { -1, 1 }) Flag(At(0, side * (halfW + 2.5)), new Color(0.95f, 0.85f, 0.1f), 3.2f, false);
            // Judges at the line, both sides, plus a few spectators.
            Person(At(0, -(halfW + 5), 0), new Color(0.98f, 0.55f, 0.1f), false, null);
            Person(At(-1.2, -(halfW + 6.2), 0), new Color(0.98f, 0.55f, 0.1f), false, null);
            Person(At(0, halfW + 5, 0), new Color(0.98f, 0.55f, 0.1f), false, null);
            for (int i = 0; i < 6; i++) Person(At(-8 - i * 1.4, -(halfW + 9 + (i % 2) * 1.5), 0), Color.HSVToRGB(i / 6f, 0.5f, 0.8f), false, null);
            foreach (Transform t in _field.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = WaterReflection.PropsLayer;
        }

        private void Stripe(double pastLineM, float lengthAcross, float widthAlong, Color c)
        {
            // A strip of quads across the runway following the ground (the grass swells).
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(go);
            go.name = "StolMark"; go.transform.SetParent(_field.transform, false);
            go.transform.position = At(pastLineM, 0, 0.05);
            go.transform.rotation = Quaternion.Euler(0f, (float)(StolContest.HeadingRad * Mathf.Rad2Deg), 0f);
            go.transform.localScale = new Vector3(lengthAcross, 0.06f, widthAlong);
            go.GetComponent<MeshRenderer>().sharedMaterial = WorldBuilder.Mat("FlyingGame/Lit", c);
        }

        private void Flag(Vector3 foot, Color c, float pole, bool raised)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Kill(p);
            p.name = "FlagPole"; p.transform.SetParent(_field.transform, false);
            p.transform.position = foot + Vector3.up * pole * 0.5f; p.transform.localScale = new Vector3(0.05f, pole * 0.5f, 0.05f);
            p.GetComponent<MeshRenderer>().sharedMaterial = WorldBuilder.Mat("FlyingGame/Lit", new Color(0.3f, 0.3f, 0.3f));
            var f = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(f);
            f.name = "Flag"; f.transform.SetParent(_field.transform, false);
            f.transform.position = foot + Vector3.up * (pole - 0.35f) + Vector3.right * 0.45f; f.transform.localScale = new Vector3(0.9f, 0.6f, 0.02f);
            f.GetComponent<MeshRenderer>().sharedMaterial = WorldBuilder.Mat("FlyingGame/Lit", c);
        }

        /// <summary>A simple standing figure; <paramref name="flag"/> non-null = holding a flag straight up.</summary>
        private void Person(Vector3 foot, Color vest, bool _, Color? flag)
        {
            var root = new GameObject("Person"); root.transform.SetParent(_field.transform, false); root.transform.position = foot;
            void Part(PrimitiveType t, Vector3 at, Vector3 size, Color c)
            {
                var g = GameObject.CreatePrimitive(t); Kill(g); g.transform.SetParent(root.transform, false);
                g.transform.localPosition = at; g.transform.localScale = size;
                g.GetComponent<MeshRenderer>().sharedMaterial = WorldBuilder.Mat("FlyingGame/Lit", c);
            }
            Color jeans = new Color(0.2f, 0.25f, 0.4f), skin = new Color(0.85f, 0.68f, 0.55f);
            Part(PrimitiveType.Cube, new Vector3(-0.1f, 0.42f, 0), new Vector3(0.14f, 0.84f, 0.16f), jeans);
            Part(PrimitiveType.Cube, new Vector3(0.1f, 0.42f, 0), new Vector3(0.14f, 0.84f, 0.16f), jeans);
            Part(PrimitiveType.Cube, new Vector3(0, 1.17f, 0), new Vector3(0.44f, 0.66f, 0.24f), vest);
            Part(PrimitiveType.Sphere, new Vector3(0, 1.66f, 0), Vector3.one * 0.24f, skin);
            if (flag.HasValue)
            {
                Part(PrimitiveType.Cube, new Vector3(0.28f, 1.75f, 0), new Vector3(0.1f, 0.6f, 0.1f), vest);          // arm up
                Part(PrimitiveType.Cylinder, new Vector3(0.3f, 2.4f, 0), new Vector3(0.03f, 0.55f, 0.03f), new Color(0.3f, 0.3f, 0.3f));
                Part(PrimitiveType.Cube, new Vector3(0.62f, 2.7f, 0), new Vector3(0.62f, 0.42f, 0.02f), flag.Value);
                Part(PrimitiveType.Cube, new Vector3(-0.28f, 0.95f, 0), new Vector3(0.1f, 0.6f, 0.1f), vest);
            }
            else foreach (float sx in new[] { -0.28f, 0.28f }) Part(PrimitiveType.Cube, new Vector3(sx, 0.95f, 0), new Vector3(0.1f, 0.6f, 0.1f), vest);
        }

        /// <summary>The judge's flagger at the spot, at the strip's edge, flag held high.</summary>
        private void Flagger(double pastLineM, Color flag)
        {
            float halfW = (float)(Contest.StripHalfW - 2 + 1.5);
            Person(At(pastLineM, -(halfW + 1.5)), new Color(0.98f, 0.55f, 0.1f), true, flag);
            Stripe(pastLineM, 1.2f, 0.25f, flag);   // a marker on the ground at the strip edge too
            foreach (Transform t in _field.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = WaterReflection.PropsLayer;
        }

        private static void Kill(GameObject g) { var c = g.GetComponent<Collider>(); if (c != null) Destroy(c); }

        public string Line
        {
            get
            {
                if (!Active || Contest == null) return null;
                string scores = $"L {string.Join(" / ", Contest.Landings.ConvertAll(m => (m * 3.28084).ToString("F0")))}   T {string.Join(" / ", Contest.Takeoffs.ConvertAll(m => (m * 3.28084).ToString("F0")))}";
                return $"STOL  {Contest.Message}   ·  {scores}";
            }
        }
    }

    /// <summary>
    /// Crop dusting (owner 2026-10-07): the field is part-sprayed; five racetrack passes are left. The spray comes on by
    /// itself low over the field; a stretch flown on its line inside the speed / height window paints neon orange (a miss
    /// stays bare — come back for it). A ground arrow marks where the next pass begins; an ag-nav light bar across the top
    /// steers you onto the line; a radio altimeter reads height above the crop. Score = time + penalties (outside the window).
    /// </summary>
    public sealed class CropDustController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public CropDust Run { get; private set; }
        public bool Active { get; private set; }
        private ParticleSystem _spray;
        private Material _mat;
        private bool _struck;
        private GameObject _arrow;
        private GUIStyle _st, _big;
        public static readonly Color Neon = new(1f, 0.42f, 0.02f);

        public CropField Field { get; private set; }
        private Mesh FieldMesh => Field != null && Field.Plateau < WorldBuilder.CropFieldMeshes.Length ? WorldBuilder.CropFieldMeshes[Field.Plateau] : null;

        public void Begin()
        {
            Field = CropField.For(SessionSettings.AirportIndex);
            Run = new CropDust(Field); Active = true; _struck = false;
            Mesh m = FieldMesh;
            if (m != null)
            {
                var cols = m.colors; int ny = CropField.CellsY;
                for (int v = 0; v < cols.Length; v++)
                {
                    int cell = v / 4, i = cell / ny, j = cell % ny;
                    Color c = Run.Credited(i, j) ? Neon : (j % 2 == 0 ? WorldBuilder.Ploughed : WorldBuilder.Ploughed * 0.85f); c.a = 1f; cols[v] = c;
                }
                m.colors = cols;
            }
            BuildArrow();
        }

        public void End()
        {
            Active = false;
            if (_spray != null) { var em = _spray.emission; em.rateOverTime = 0f; }
            if (_arrow != null) Destroy(_arrow);
        }

        private void Update()
        {
            if (!Active || Run == null || Driver?.Sim == null) return;
            var ac = Driver.Sim.Aircraft; var s = ac.State;
            double agl = -s.Position.Z - WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
            var vW = s.Attitude.Rotate(s.Velocity);
            double gs = System.Math.Sqrt(vW.X * vW.X + vW.Y * vW.Y);
            Run.Update(s.Position, agl, gs, Time.deltaTime);

            if (Run.NewlyCovered.Count > 0 && FieldMesh != null)
            {
                Mesh m = FieldMesh; var cols = m.colors; int ny = CropField.CellsY;
                foreach ((int i, int j) in Run.NewlyCovered) { int b = (i * ny + j) * 4; for (int q = 0; q < 4; q++) cols[b + q] = Neon; }
                m.colors = cols;
            }
            PlaceArrow();

            if (_spray == null) BuildSpray();
            var em = _spray.emission; em.rateOverTime = Run.Spraying ? 350f : 0f;   // automatic: on whenever low over the field
            Vector3 back = -Driver.WorldVelocityUnity.normalized;
            _spray.transform.position = Driver.transform.position + back * 2.5f + Vector3.down * 0.8f;
            var vel = _spray.velocityOverLifetime; vel.enabled = true;
            Vector3 drift = Driver.WorldVelocityUnity * 0.15f;
            vel.x = drift.x; vel.y = -1.2f; vel.z = drift.z;
            var main = _spray.main; main.startColor = new Color(1f, 0.6f, 0.25f, 0.5f);

            if (Run.WireStrike && !_struck)
            {
                _struck = true;
                ac.State = new RigidBodyState(s.Position, s.Attitude, s.Velocity * 0.35, new Vec3(s.Rates.X, s.Rates.Y - 0.6, s.Rates.Z));
                em.rateOverTime = 0f;
            }
        }

        // ---- the arrow: where the next pass begins, pointing along it ----
        private void BuildArrow()
        {
            if (_arrow != null) Destroy(_arrow);
            _arrow = new GameObject("NextPassArrow");
            var mat = WorldBuilder.Mat("FlyingGame/Lit", new Color(1f, 0.95f, 0.1f));
            void Box(Vector3 at, Vector3 size, float yaw)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(_arrow.transform, false); b.transform.localPosition = at; b.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); b.transform.localScale = size;
                b.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            // Local +z = the pass direction: a 30 m shaft and a head, lying on the ground, plus a tall mast so it reads from the air.
            Box(new Vector3(0, 0.3f, -12f), new Vector3(3f, 0.4f, 24f), 0f);
            Box(new Vector3(-3.2f, 0.3f, 2f), new Vector3(2.6f, 0.4f, 12f), -35f);
            Box(new Vector3(3.2f, 0.3f, 2f), new Vector3(2.6f, 0.4f, 12f), 35f);
            Box(new Vector3(0, 6f, -24f), new Vector3(0.4f, 12f, 0.4f), 0f);
        }

        private void PlaceArrow()
        {
            if (_arrow == null) return;
            var (x, y, h) = Run.NextPassStart();
            _arrow.SetActive(!Run.Finished && !Run.WireStrike);
            // Just outside the field edge the pass enters at, so it isn't sprayed over.
            double back = 25; double ax = x - System.Math.Cos(h) * back, ay = y - System.Math.Sin(h) * back;
            _arrow.transform.position = CoordinateMap.ToUnity(new Vec3(ax, ay, -WorldTerrain.GroundHeightAt(ax, ay)));
            _arrow.transform.rotation = Quaternion.Euler(0f, (float)(h * Mathf.Rad2Deg), 0f);
        }

        private void BuildSpray()
        {
            var go = new GameObject("CropSpray");
            _spray = go.AddComponent<ParticleSystem>();
            var main = _spray.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = 2.2f; main.startSize = 1.6f;
            main.startSpeed = 0f; main.gravityModifier = 0.02f; main.maxParticles = 1500; main.loop = true; main.playOnAwake = true;
            var em = _spray.emission; em.rateOverTime = 0f;
            var sh = _spray.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(15f, 0.5f, 1.5f);   // the 50 ft swath across the wing
            var col = _spray.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.7f, 0.4f), 1f) },
                      new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = _spray.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.5f)));
            _mat = new Material(Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = new Color(1f, 0.75f, 0.5f, 0.6f) };
            var r = _spray.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = _mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        }

        // ---- the ag-nav light bar and the radio altimeter ----
        private void OnGUI()
        {
            if (!Active || Run == null || Driver?.Sim == null || SessionSettings.MenuOpen) return;
            float S = UiLayout.S;
            _st ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            Rect band = UiLayout.NextBlock(S * 0.075f);
            // Steer-to convention: the lights light on the side to steer toward; 1 m per light, ±10.
            const int n = 21; float led = Mathf.Min(band.width * 0.6f / n, S * 0.03f), gap = led * 0.18f;
            float total = n * led + (n - 1) * gap, x0 = band.center.x - total / 2, y0 = band.y + band.height * 0.08f;
            var hdg = Driver.Sim.Aircraft.State; var fwd = hdg.Attitude.Rotate(new Vec3(1, 0, 0));
            bool south = fwd.X < 0;
            double right = (south ? -1 : 1) * Run.CrossTrackM;            // + = right of the line (pilot's frame)
            int lit = Mathf.Clamp(Mathf.RoundToInt((float)right), -10, 10);   // lights toward the line: steer left when right of it
            var old = GUI.color;
            for (int i = 0; i < n; i++)
            {
                int pos = i - n / 2;   // −10 … +10 (left … right)
                bool on = Run.TargetSwath >= 0 && (pos == 0 ? System.Math.Abs(right) < 1.0 : (lit > 0 ? pos < 0 && -pos <= lit : lit < 0 && pos > 0 && pos <= -lit));
                Color c = pos == 0 ? new Color(0.1f, 0.95f, 0.2f) : System.Math.Abs(pos) <= 3 ? new Color(1f, 0.75f, 0.1f) : new Color(1f, 0.18f, 0.12f);
                GUI.color = on ? c : new Color(c.r * 0.22f, c.g * 0.22f, c.b * 0.22f, 0.9f);
                GUI.DrawTexture(new Rect(x0 + i * (led + gap), y0, led, led * 0.75f), Texture2D.whiteTexture);
            }
            GUI.color = old;
            float ty = y0 + led * 0.8f, th = band.yMax - ty;
            string sw = Run.TargetSwath >= 0 ? $"SWATH {Run.TargetSwath + 1}" : "FIELD DONE";
            UiLayout.Label(new Rect(x0, ty, total * 0.3f, th), sw, _st);
            UiLayout.Label(new Rect(x0 + total * 0.3f, ty, total * 0.4f, th), $"XTK {System.Math.Abs(right):F1} m {(right > 0.5 ? "R" : right < -0.5 ? "L" : "")}", _st);
            double agl = -hdg.Position.Z - WorldTerrain.GroundHeightAt(hdg.Position.X, hdg.Position.Y);
            GUI.color = Run.Spraying && !Run.InWindow ? new Color(1f, 0.4f, 0.3f) : Color.white;
            UiLayout.Label(new Rect(x0 + total * 0.7f, ty, total * 0.3f, th), agl < 760 ? $"RA {agl * 3.28084:F0} ft" : "RA ----", _st);   // radio altimeter: 2,500 ft range
            GUI.color = old;
        }

        public string Line => !Active || Run == null ? null
            : Run.WireStrike ? "CROP DUST  HIT THE WIRES — run over.  R to reset"
            : Run.Finished ? $"CROP DUST  FIELD DONE  {Run.ScoreSec:F0} s  (flying {Run.ElapsedSec:F0} + penalties {Run.PenaltySec:F0})"
            : $"CROP DUST  {Run.PassesLeft} passes left   {Run.ElapsedSec:F0} s + {Run.PenaltySec:F0} s   {Run.LastEvent}";
    }
}

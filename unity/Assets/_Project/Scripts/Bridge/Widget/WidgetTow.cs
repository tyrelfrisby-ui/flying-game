using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// AEROTOW in the widget (owner 2026-10-11, the Talk Flying outro): the game's own tow — AeroTow's elastic rope
    /// with its weak links, and TugPilot flying a fully simulated Pawnee — hooked up in the air, the tug a rope's length
    /// ahead at tow speed, climbing. The glider is whoever flies the widget (or the tow-release preset). The tug's state
    /// and the rope go into the history, so review shows them exactly.
    /// </summary>
    public sealed class WidgetTow
    {
        public const string TugId = "pa25-pawnee-like";
        public const double RopeM = 61.0;
        public Aircraft Tug { get; private set; }
        public AeroTow Rope { get; private set; }
        public TugPilot Pilot { get; private set; }
        public bool Active => Tug != null;
        public bool Connected => Rope != null && Rope.Connected;

        private readonly AeroWidget _w;
        private GameObject _tugGo;
        private AirframeBuilder _tugBuilder;
        private LineRenderer _line;

        public WidgetTow(AeroWidget w) { _w = w; }

        /// <summary>Hook up in the air: the tug ahead on the glider's heading at 60 kt, the rope just taut.</summary>
        public void Start()
        {
            Stop();
            var g = _w.Ac.State;
            var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(TugId);
            Vec3 fwd = g.Attitude.Rotate(new Vec3(1, 0, 0)); fwd = new Vec3(fwd.X, fwd.Y, 0); fwd = fwd * (1.0 / System.Math.Max(1e-6, fwd.Length));
            double heading = System.Math.Atan2(fwd.Y, fwd.X);
            Vec3 pos = g.Position + fwd * (RopeM + 2.0 + 3.4 - 0.3) + new Vec3(0, 0, 3.0);   // a hair of slack; the glider a little high (high tow)
            double hh = heading / 2;
            var att = new Quat(0, 0, System.Math.Sin(hh), System.Math.Cos(hh));
            Vec3 air = att.Conjugate().Rotate(Atmosphere.WindAtPosition(pos));
            Tug = new Aircraft(cfg, new RigidBodyState(pos, att, new Vec3(TugPilotSpeed, 0, 0) + air, Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
            // Hooked up in the air means already flying the tow together: the glider at tow speed too, trimmed for it, wings
            // level on the heading, climbing with the tug. (Left at its own 40 kt cruise the tug ran away and the rope snatched
            // the weak link; at tow speed but trimmed for 40 kt it ballooned 10 m above the tug, the rope pulled the tug's
            // tail up, the tug dived to 68 kt and the rope went slack and snatched.)
            double gAlt = -g.Position.Z, climb = 2.5 / TugPilotSpeed;
            TrimSolver.Result gt = TrimSolver.SolveGliderTrim(_w.Config, TugPilotSpeed, gAlt);
            double gAlpha = gt.Converged ? gt.AlphaRad : 0.08, gTheta = gAlpha + climb;
            Quat gAtt = Quat.Multiply(new Quat(0, 0, System.Math.Sin(heading / 2), System.Math.Cos(heading / 2)), new Quat(0, System.Math.Sin(gTheta / 2), 0, System.Math.Cos(gTheta / 2)));
            Vec3 gAir = gAtt.Conjugate().Rotate(Atmosphere.WindAtPosition(g.Position));
            _w.Ac.State = new RigidBodyState(g.Position, gAtt, new Vec3(TugPilotSpeed * System.Math.Cos(gAlpha), 0, TugPilotSpeed * System.Math.Sin(gAlpha)) + gAir, Vec3.Zero);
            if (gt.Converged) _w.Controls.SetTrimStick(Aircraft.StickForDeflection(gt.ElevatorRad, _w.Config.Controls.Elevator));
            // The steady pull the tug will feel: the glider's drag (its glide angle × weight) plus its weight up the climb.
            double gW = _w.Config.Mass.MassKg * 9.81;
            double towLoadN = gW * (System.Math.Sin(gt.Converged ? System.Math.Max(0.02, gAlpha - gt.ThetaRad) : 0.04) + climb);
            double alt = -pos.Z;
            Pilot = new TugPilot
            {
                OriginX = pos.X, OriginY = pos.Y, RunwayHeadingRad = heading, ThresholdX = 60000,   // its first pattern leg 63 km straight ahead: it never turns on the clip
                RunwayY = 0, RunwayElevM = 0,
                TowSpeedMs = TugPilotSpeed, PatternAglM = alt + 900,   // keeps climbing straight out for the whole clip
            };
            // The tow pilot has been flying a while: 30 s alone first, so he is trimmed and steady in the climb (started cold,
            // his trim integrator at zero, the Pawnee dived, ran to 77 kt and porpoised the glider off the rope). Then back
            // into place ahead of the glider, with his attitude, speed and trim as they are.
            for (int i = 0; i < 30 * 60; i++)
            {
                Vec3 tv = Tug.State.Attitude.Rotate(Tug.State.Velocity);
                Tug.ExternalForceWorld = tv * (-towLoadN / System.Math.Max(1.0, tv.Length)); Tug.ExternalForcePointBody = new Vec3(-3.4, 0, 0);   // trimmed for the glider on the rope
                new SimLoop(Tug).RunFor(1.0 / 60, Pilot.Update(Tug, 1.0 / 60, 0, null));
            }
            var ts = Tug.State;
            Tug.State = new RigidBodyState(pos, ts.Attitude, ts.Velocity, ts.Rates);
            Rope = new AeroTow(Tug, _w.Ac, RopeM);
            if (_tugGo == null)
            {
                _tugGo = new GameObject("WidgetTug");
                _tugGo.transform.SetParent(_w.transform, false);
                _tugBuilder = new AirframeBuilder();
                _tugBuilder.Build(_tugGo.transform, cfg);
                _line = new GameObject("WidgetTowRope").AddComponent<LineRenderer>();
                _line.transform.SetParent(_w.transform, false);
                _line.material = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = new Color(0.95f, 0.95f, 0.9f) };
                _line.widthMultiplier = 0.12f; _line.positionCount = 2;
                SetLayer(_tugGo, _w.VisualLayer); _line.gameObject.layer = _w.VisualLayer;
            }
            _tugGo.SetActive(true);
        }
        private const double TugPilotSpeed = 30.0;   // 58 kt: tow speed for a trainer glider

        public void Stop()
        {
            if (Rope != null) Rope.Release("stopped");
            if (_w.Ac != null) _w.Ac.ExternalForceWorld = Vec3.Zero;
            Tug = null; Rope = null; Pilot = null;
            if (_tugGo != null) _tugGo.SetActive(false);
            if (_line != null) _line.enabled = false;
        }

        /// <summary>The glider pulls the release (the yellow knob).</summary>
        public void Release() { if (Connected) { Rope.Release("glider"); Pilot.GliderReleased = true; } }

        /// <summary>One sim step, before the glider's: the rope's pull on both, then the tug pilot and the tug.</summary>
        public void Step(float dt)
        {
            if (!Active) return;
            if (Rope.Connected) Rope.Apply(dt);
            Vec3? dir = null;
            if (Rope.Connected) { Vec3 r = Rope.GliderHookWorld - Rope.TugHookWorld; if (r.Length > 1e-6) dir = Tug.State.Attitude.Conjugate().Rotate(r * (1.0 / r.Length)); }
            ControlInputs ci = Pilot.Update(Tug, dt, Rope.Tension, dir);
            if (Pilot.WantsRelease && Rope.Connected) { Rope.Release("tug"); Pilot.GliderReleased = true; }
            new SimLoop(Tug).RunFor(dt, ci);
        }

        /// <summary>Draw the tug and the rope as recorded (null tug = no tow in that frame).</summary>
        public void Show(RigidBodyState? tug, bool rope, RigidBodyState glider, Vec3 shift)
        {
            if (_tugGo == null) return;
            if (tug == null) { _tugGo.SetActive(false); _line.enabled = false; return; }
            var t = tug.Value;
            _tugGo.SetActive(true);
            _tugGo.transform.SetPositionAndRotation(CoordinateMap.ToUnity(t.Position + shift), CoordinateMap.ToUnity(t.Attitude));
            _tugBuilder.SpinProps(_ => 2400f, Time.deltaTime);
            _line.enabled = rope;
            if (rope)
            {
                _line.SetPosition(0, CoordinateMap.ToUnity(t.Position + t.Attitude.Rotate(new Vec3(-3.4, 0, 0)) + shift));
                _line.SetPosition(1, CoordinateMap.ToUnity(glider.Position + glider.Attitude.Rotate(new Vec3(2.0, 0, 0)) + shift));
            }
        }

        private static void SetLayer(GameObject go, int layer) { go.layer = layer; foreach (Transform c in go.transform) SetLayer(c.gameObject, layer); }
    }
}

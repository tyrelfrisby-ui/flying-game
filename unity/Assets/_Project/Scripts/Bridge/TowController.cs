using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Aero;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Aerotow gameplay: spawns a tug (PA-18) ahead of the glider on a 61 m rope, runs the tug on an
    /// AI speed/altitude hold while the PLAYER flies the glider (holding formation), applies the rope
    /// physics each fixed step, and draws the rope. Press G to release (the yellow-knob pull) — the
    /// glider drops free and the tug climbs away. Press Y to (re)hook a tow from a stable state.
    /// </summary>
    public sealed class TowController : MonoBehaviour
    {
        public const double TowSpeedMs = 30.0;
        public float RopeLengthM = 61f;

        public AeroTow Tow { get; private set; }
        public bool Towing => Tow is { Connected: true };

        private FlightSimDriver _gliderDriver;
        private GameObject _tugGo;
        private FlightSimDriver _tugDriver;
        private LineRenderer _rope;
        private double _accumulator;

        private void Awake() => _gliderDriver = GetComponent<FlightSimDriver>();

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Y) && !Towing)
            {
                StartTow();
            }
            if (Input.GetKeyDown(KeyCode.G) && Towing)
            {
                Tow.Release();
            }

            if (Tow != null)
            {
                _accumulator += Time.deltaTime;
                while (_accumulator >= SimLoop.DefaultFixedDtSec)
                {
                    if (Tow.Connected) Tow.Apply(SimLoop.DefaultFixedDtSec);
                    if (_tugAircraft != null) FlyTugAutopilot(SimLoop.DefaultFixedDtSec);   // keeps flying after release/break
                    _accumulator -= SimLoop.DefaultFixedDtSec;
                }
                UpdateTugTransform();
                DrawRope();
            }
        }

        private bool _groundTow;
        private bool _powerLatched;      // ground tow: once the glider pilot has brought the tug to full power it stays there
        private double _tugThrottle01;   // 0..1 commanded by the glider's left pad until latched

        /// <summary>Hook up and go. In the air the tug appears ahead at tow speed; on the ground (runway start)
        /// the tug sits ahead on the runway at rest and takes off with full power, climbing after liftoff.</summary>
        public void StartTow()
        {
            var gliderState = _gliderDriver.Sim.Aircraft.State;
            var tugConfig = UnityAircraftConfigLoader.LoadFromStreamingAssets("pa18-cub-like");
            var fwd = gliderState.Attitude.Rotate(new Vec3(1, 0, 0));
            double groundHere = FlyingGame.Core.WorldTerrain.GroundHeightAt(gliderState.Position.X, gliderState.Position.Y);
            _groundTow = (-gliderState.Position.Z - groundHere) < 3.0 && gliderState.Velocity.Length < 3.0;
            Vec3 tugPos;
            RigidBodyState tugState;
            if (_groundTow)
            {
                double tugGearZ = 0; foreach (var g in tugConfig.Gear) if (!g.IsTailwheel) tugGearZ = System.Math.Max(tugGearZ, g.Pos[2]);
                var fwdFlat = new Vec3(fwd.X, fwd.Y, 0); fwdFlat = fwdFlat / fwdFlat.Length;
                // Rope just taut at hookup (no snatch when the tug moves off): hooks are ~2 m ahead of the
                // glider CG and ~4.5 m behind the tug CG, so tug CG = glider CG + rope + 2.5 m.
                double ahead = RopeLengthM + 2.0 + 3.4 - 0.2;   // glider hook +2.0, tug hook -3.4: rope just taut (0.2 m slack)
                tugPos = new Vec3(gliderState.Position.X + fwdFlat.X * ahead, gliderState.Position.Y + fwdFlat.Y * ahead, -(groundHere + tugGearZ - 0.02));
                tugState = new RigidBodyState(tugPos, gliderState.Attitude, Vec3.Zero, Vec3.Zero);
            }
            else
            {
                tugPos = gliderState.Position + fwd * (RopeLengthM + 5.0);
                tugState = new RigidBodyState(tugPos, gliderState.Attitude, new Vec3(TowSpeedMs, 0, 0), Vec3.Zero);
            }
            var tug = new Aircraft(tugConfig, tugState, new ControlDeflections(0, 0, 0, 0));
            _tugDriver = null;

            if (_tugGo == null)
            {
                _tugGo = BuildTugVisual();
                _rope = BuildRope();
            }
            _tugGo.SetActive(true);
            _rope.enabled = true;

            _tugAircraft = tug;
            _powerLatched = !_groundTow; _tugThrottle01 = _groundTow ? 0.0 : 1.0;
            { var q = gliderState.Attitude; _towHeading = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)); }
            Tow = new AeroTow(tug, _gliderDriver.Sim.Aircraft, RopeLengthM);
            if (_groundTow)
            {
                var runner = GetComponent<WingRunner>() ?? gameObject.AddComponent<WingRunner>();
                runner.Driver = _gliderDriver;
                runner.Begin(1f);   // right wingtip
            }
            _accumulator = 0;
        }

        private Aircraft _tugAircraft;

        // Simple AI tug: hold tow speed with throttle, hold altitude (or a climb after a ground launch) with
        // elevator, wings level, runway heading with rudder while rolling.
        private double _towHeading;
        private void FlyTugAutopilot(double dt)
        {
            var s = _tugAircraft.State;
            double speedErr = s.Velocity.Length - TowSpeedMs;
            double lever = System.Math.Clamp(speedErr * 0.3, -1.0, 1.0);         // throttle (speed hold, airborne)
            double agl = -s.Position.Z - FlyingGame.Core.WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
            double targetSink = _groundTow && agl > 4.0 && s.Velocity.Length > 24.0 ? -2.5 : 0.0; // climb 500 fpm on a ground launch
            if (_groundTow)
            {
                // The GLIDER pilot advances the tug's power with the left pad (owner spec). Once full power is
                // reached it latches: the tug stays at full power and ignores the glider's lever from then on.
                if (!_powerLatched)
                {
                    // Tug power = the glider's left pad above its 50 % line (below it the pad is the spoiler lever).
                    var pad = GetComponent<TouchFlightControls>();
                    double padThrottle01 = pad != null ? System.Math.Clamp((pad.LeftPadFraction - 0.5) / 0.5, 0.0, 1.0)
                                                       : System.Math.Clamp((1.0 - _gliderDriver.Inputs.ThrottleLever) * 0.5, 0.0, 1.0);
                    _tugThrottle01 = padThrottle01;
                    if (_tugThrottle01 >= 0.98) _powerLatched = true;
                }
                if (agl < 4.0 || !_powerLatched) lever = 1.0 - 2.0 * (_powerLatched ? 1.0 : _tugThrottle01); // ground roll: pad-commanded / full
            }
            double sink = s.Attitude.Rotate(s.Velocity).Z;                        // +down
            double elev = System.Math.Clamp(-(sink - targetSink) * 0.15 - s.Rates.Y * 0.5, -1, 1); // hold, damp pitch
            if (_groundTow && agl < 4.0)
            {
                // Taildragger ground roll with a glider in tow: stick neutral until rolling, a touch forward to
                // lift the tail (never enough to nose over in the prop wash), rotate gently past 24 m/s.
                double v = s.Velocity.Length;
                elev = v < 10 ? 0.0 : v < 24 ? 0.08 : -0.25;
                elev += -s.Rates.Y * 0.4;
                double pitchDeg = System.Math.Asin(System.Math.Clamp(2 * (s.Attitude.W * s.Attitude.Y - s.Attitude.Z * s.Attitude.X), -1, 1)) * 57.3;
                if (pitchDeg < -4) elev = -0.3; // tail too high: back stick
            }
            double rud = 0;
            if (_groundTow)
            {
                double q0 = s.Attitude.W, q1 = s.Attitude.X, q2 = s.Attitude.Y, q3 = s.Attitude.Z;
                double psi = System.Math.Atan2(2 * (q0 * q3 + q1 * q2), 1 - 2 * (q2 * q2 + q3 * q3));
                double err = _towHeading - psi; while (err > System.Math.PI) err -= 2 * System.Math.PI; while (err < -System.Math.PI) err += 2 * System.Math.PI;
                rud = System.Math.Clamp(err * 2.0 - s.Rates.Z * 0.5, -1, 1);
            }
            double bank = System.Math.Atan2(2 * (s.Attitude.W * s.Attitude.X + s.Attitude.Y * s.Attitude.Z),
                1 - 2 * (s.Attitude.X * s.Attitude.X + s.Attitude.Y * s.Attitude.Y));
            double ail = System.Math.Clamp(-bank * 1.5 - s.Rates.X * 0.5, -1, 1); // wings level
            var sim = new SimLoop(_tugAircraft);
            sim.RunFor(dt, new ControlInputs(ail, elev, rud, lever));
        }

        private void UpdateTugTransform()
        {
            if (_tugGo == null || _tugAircraft == null)
            {
                return;
            }

            _tugGo.transform.SetPositionAndRotation(
                CoordinateMap.ToUnity(_tugAircraft.State.Position),
                CoordinateMap.ToUnity(_tugAircraft.State.Attitude));
        }

        private void DrawRope()
        {
            if (_rope == null)
            {
                return;
            }

            if (!Towing)
            {
                _rope.enabled = false;
                return;
            }

            // Catenary-ish sag between the two hooks (a light rope droops under gravity when slack-ish).
            Vector3 a = CoordinateMap.ToUnity(Tow.GliderHookWorld);
            Vector3 b = CoordinateMap.ToUnity(Tow.TugHookWorld);
            float slack = Mathf.Max(0, RopeLengthM - Vector3.Distance(a, b));
            float sag = Mathf.Clamp(slack * 0.4f + 0.5f, 0.5f, 8f);
            const int seg = 12;
            _rope.positionCount = seg + 1;
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * (4 * t * (1 - t)); // parabolic droop
                _rope.SetPosition(i, p);
            }
        }

        private static GameObject BuildTugVisual()
        {
            // The real Super Cub airframe (same builder as the flyable one) — the old primitive placeholder
            // used the stripped Standard shader and rendered magenta on device.
            var root = new GameObject("Tug-PA18");
            var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("pa18-cub-like");
            new AirframeBuilder().Build(root.transform, cfg);
            return root;
        }

        private static LineRenderer BuildRope()
        {
            var go = new GameObject("TowRope");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.9f, 0.9f, 0.85f) };
            lr.widthMultiplier = 0.12f;
            lr.numCornerVertices = 2;
            return lr;
        }

    }
}

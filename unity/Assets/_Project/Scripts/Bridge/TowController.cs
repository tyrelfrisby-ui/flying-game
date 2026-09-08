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
                ReleaseFromGlider();
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
            var tugConfig = UnityAircraftConfigLoader.LoadFromStreamingAssets(SessionSettings.TugId);
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

            if (_tugGo != null && _tugVisualId != SessionSettings.TugId) { Destroy(_tugGo); _tugGo = null; }
            if (_tugGo == null)
            {
                _tugGo = BuildTugVisual();
                _tugVisualId = SessionSettings.TugId;
                if (_rope == null) _rope = BuildRope();
            }
            _tugGo.SetActive(true);
            _rope.enabled = true;

            _tugAircraft = tug;
            _pilot = null;
            _powerLatched = !_groundTow; _tugThrottle01 = _groundTow ? 0.0 : 1.0;
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
        private string _tugVisualId;

        private TugPilot _pilot;

        /// <summary>Tow status for the HUD.</summary>
        public string StatusLine => _tugAircraft == null ? null
            : Towing ? $"TOW  {_pilot?.Status}   rope {Tow.Tension:F0} N   RELEASE when ready"
            : _pilot != null ? $"TOW  released ({Tow?.SeverReason})  tug {_pilot.Status}" : null;

        /// <summary>Glider-side release (button / G key).</summary>
        public void ReleaseFromGlider()
        {
            if (Towing) { Tow.Release("glider"); if (_pilot != null) _pilot.GliderReleased = true; }
        }

        private void FlyTugAutopilot(double dt)
        {
            if (_pilot == null)
            {
                var ap = SessionSettings.Airport;
                _pilot = new TugPilot
                {
                    ThresholdX = ap.X - FlyingGame.Core.WorldTerrain.RunwayLengthM * 0.5 + 80.0, RunwayY = ap.Y, RunwayElevM = ap.ElevationM,
                    TowSpeedMs = TowSpeedMs,
                };
                if (!_groundTow) _pilot.GliderReleased = false;
            }
            _pilot.PowerLimit01 = _groundTow ? (_powerLatched ? 1.0 : _tugThrottle01) : 1.0;
            if (_groundTow && !_powerLatched)
            {
                var pad = GetComponent<TouchFlightControls>();
                _tugThrottle01 = pad != null ? System.Math.Clamp((pad.LeftPadFraction - 0.5) / 0.5, 0.0, 1.0) : 1.0;
                if (_tugThrottle01 >= 0.98) _powerLatched = true;
            }
            Vec3? ropeDir = null;
            if (Tow != null && Tow.Connected)
            {
                Vec3 r = Tow.GliderHookWorld - Tow.TugHookWorld;
                if (r.Length > 1e-6) ropeDir = _tugAircraft.State.Attitude.Conjugate().Rotate(r / r.Length);
            }
            ControlInputs ci = _pilot.Update(_tugAircraft, dt, Tow?.Tension ?? 0, ropeDir);
            if (_pilot.WantsRelease && Tow != null && Tow.Connected) { Tow.Release("tug"); _pilot.GliderReleased = true; }
            new SimLoop(_tugAircraft).RunFor(dt, ci);
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
                if (Tow != null && Tow.SeverReason == "tug")
                {
                    // Tug let go: the rope stays on the glider's hook and trails behind, drooping under its own weight.
                    Vector3 hook = CoordinateMap.ToUnity(Tow.GliderHookWorld);
                    var vs = _gliderDriver.Sim.Aircraft.State;
                    Vector3 vel = CoordinateMap.ToUnity(vs.Attitude.Rotate(vs.Velocity));
                    Vector3 back = vel.magnitude > 1f ? -vel.normalized : -_gliderDriver.transform.forward;
                    float droop = Mathf.Clamp(12f - vel.magnitude * 0.3f, 2f, 12f);
                    const int n = 12;
                    _rope.enabled = true;
                    _rope.positionCount = n + 1;
                    for (int i = 0; i <= n; i++)
                    {
                        float t = i / (float)n;
                        Vector3 p = hook + back * (RopeLengthM * 0.85f * t) + Vector3.down * (droop * t * t);
                        var ps = CoordinateMap.ToSim(p);
                        float g = (float)FlyingGame.Core.WorldTerrain.GroundHeightAt(ps.X, ps.Y);
                        if (p.y < g + 0.05f) p.y = g + 0.05f;   // drags on the ground
                        _rope.SetPosition(i, p);
                    }
                    return;
                }
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
            var root = new GameObject("Tug-" + SessionSettings.TugId);
            var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(SessionSettings.TugId);
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

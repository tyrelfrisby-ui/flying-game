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
                    if (Tow.Connected)
                    {
                        Tow.Apply(SimLoop.DefaultFixedDtSec);
                        FlyTugAutopilot(SimLoop.DefaultFixedDtSec);
                    }
                    _accumulator -= SimLoop.DefaultFixedDtSec;
                }
                UpdateTugTransform();
                DrawRope();
            }
        }

        private void StartTow()
        {
            // Build the tug just ahead of the glider, both at tow speed, rope taut.
            var gliderState = _gliderDriver.Sim.Aircraft.State;
            var tugConfig = UnityAircraftConfigLoader.LoadFromStreamingAssets("pa18-cub-like");
            var fwd = gliderState.Attitude.Rotate(new Vec3(1, 0, 0));
            var tugPos = gliderState.Position + fwd * (RopeLengthM + 5.0);
            var tugState = new RigidBodyState(tugPos, gliderState.Attitude, new Vec3(TowSpeedMs, 0, 0), Vec3.Zero);
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
            Tow = new AeroTow(tug, _gliderDriver.Sim.Aircraft, RopeLengthM);
            _accumulator = 0;
        }

        private Aircraft _tugAircraft;

        // Simple AI tug: hold tow speed with throttle, hold altitude with elevator, wings level.
        private void FlyTugAutopilot(double dt)
        {
            var s = _tugAircraft.State;
            double speedErr = s.Velocity.Length - TowSpeedMs;
            double lever = System.Math.Clamp(speedErr * 0.3, -1.0, 1.0);         // throttle
            double sink = s.Attitude.Rotate(s.Velocity).Z;                        // +down
            double elev = System.Math.Clamp(-sink * 0.15 - s.Rates.Y * 0.5, -1, 1); // hold altitude, damp pitch
            double bank = System.Math.Atan2(2 * (s.Attitude.W * s.Attitude.X + s.Attitude.Y * s.Attitude.Z),
                1 - 2 * (s.Attitude.X * s.Attitude.X + s.Attitude.Y * s.Attitude.Y));
            double ail = System.Math.Clamp(-bank * 1.5 - s.Rates.X * 0.5, -1, 1); // wings level
            var sim = new SimLoop(_tugAircraft);
            sim.RunFor(dt, new ControlInputs(ail, elev, 0, lever));
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
            var root = new GameObject("Tug-PA18");
            AddPart(root, "Fuselage", PrimitiveType.Capsule, new Vector3(0, 0, 0.4f),
                Quaternion.Euler(90, 0, 0), new Vector3(0.9f, 3.4f, 0.9f), new Color(0.95f, 0.85f, 0.2f));
            AddPart(root, "Wing", PrimitiveType.Cube, new Vector3(0, 0.7f, 0.6f),
                Quaternion.identity, new Vector3(10.7f, 0.12f, 1.5f), new Color(0.95f, 0.85f, 0.2f));
            AddPart(root, "HStab", PrimitiveType.Cube, new Vector3(0, 0.2f, -3.0f),
                Quaternion.identity, new Vector3(3.2f, 0.08f, 0.9f), new Color(0.95f, 0.85f, 0.2f));
            AddPart(root, "VStab", PrimitiveType.Cube, new Vector3(0, 0.7f, -3.1f),
                Quaternion.identity, new Vector3(0.08f, 1.2f, 0.9f), new Color(0.9f, 0.2f, 0.2f));
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

        private static void AddPart(GameObject parent, string name, PrimitiveType type,
            Vector3 pos, Quaternion rot, Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent.transform, false);
            part.transform.SetLocalPositionAndRotation(pos, rot);
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().material.color = color;
        }
    }
}

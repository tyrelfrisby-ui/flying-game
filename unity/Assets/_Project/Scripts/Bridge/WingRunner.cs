using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Wing runner for the glider's ground launch (owner request): a person holds the wingtip level and runs
    /// with it until a human's top running speed, keeping the wings level regardless of aileron; then lets
    /// go and slows to a walk. While holding, the sim's roll is constrained level after each step (the
    /// runner's grip is a kinematic constraint on the tip). Simple articulated figure built from primitives.
    /// </summary>
    public sealed class WingRunner : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public float ReleaseSpeedMs = 7.0f;     // top human running speed
        public float WalkSpeedMs = 1.4f;
        public float Side = 1f;                 // +1 = right wingtip

        public bool Holding { get; private set; }
        private GameObject _figure;
        private Transform _legL, _legR, _armUp;
        private Vector3 _pos, _vel;
        private float _phase;
        private bool _active;

        public void Begin(float side)
        {
            Side = side;
            if (_figure == null) _figure = BuildFigure();
            _figure.SetActive(true);
            Holding = true; _active = true;
            Driver.PostStep = ConstrainLevel;
            _pos = TipWorld() + Vector3.up * 0.0f;
        }

        public void End()
        {
            Holding = false; _active = false;
            if (Driver != null && Driver.PostStep == ConstrainLevel) Driver.PostStep = null;
            if (_figure != null) _figure.SetActive(false);
        }

        /// <summary>Called by the driver after each sim step while holding: wings level, no roll rate.</summary>
        private void ConstrainLevel(FlyingGame.Sim.Aircraft ac)
        {
            var s = ac.State;
            var q = s.Attitude;
            double yaw = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
            double pitch = System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
            var yawQ = new FlyingGame.Core.MathTypes.Quat(0, 0, System.Math.Sin(yaw / 2), System.Math.Cos(yaw / 2));
            var pitchQ = new FlyingGame.Core.MathTypes.Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
            var level = FlyingGame.Core.MathTypes.Quat.Multiply(yawQ, pitchQ);
            ac.State = new FlyingGame.Core.RigidBodyState(s.Position, level, s.Velocity, new FlyingGame.Core.MathTypes.Vec3(0, s.Rates.Y, s.Rates.Z));
        }

        private Vector3 TipWorld()
        {
            // Wingtip (sim y = ±half-span, at the wing's z) on the ground beside the aircraft.
            var cfg = Driver.Sim.Aircraft.Config;
            float half = 0f; foreach (var sf in cfg.Surfaces) foreach (var st in sf.Strips) half = Mathf.Max(half, Mathf.Abs((float)st.Pos[1]));
            Vector3 tip = Driver.transform.TransformPoint(new Vector3(Side * (half - 0.3f), 0f, 0f));
            var sim = CoordinateMap.ToSim(tip);
            float ground = (float)FlyingGame.Core.WorldTerrain.GroundHeightAt(sim.X, sim.Y);
            return new Vector3(tip.x, ground, tip.z);
        }

        private void LateUpdate()
        {
            if (!_active || Driver?.Sim == null) return;
            float dt = Time.deltaTime;
            Vector3 fwd = Driver.transform.forward; fwd.y = 0; fwd.Normalize();
            if (Holding)
            {
                float v = (float)Driver.Sim.Aircraft.State.Velocity.Length;
                Vector3 tip = TipWorld();
                _vel = (tip - _pos) / Mathf.Max(dt, 1e-3f);
                _pos = tip;
                if (v > ReleaseSpeedMs)
                {
                    Holding = false;
                    if (Driver.PostStep == ConstrainLevel) Driver.PostStep = null;
                    _vel = fwd * ReleaseSpeedMs;
                }
            }
            else
            {
                // Let go: decelerate to a walk, keep walking down the runway.
                float speed = _vel.magnitude;
                speed = Mathf.MoveTowards(speed, WalkSpeedMs, 2.5f * dt);
                _vel = (_vel.sqrMagnitude > 1e-4f ? _vel.normalized : fwd) * speed;
                _pos += _vel * dt;
                var sim = CoordinateMap.ToSim(_pos);
                _pos.y = (float)FlyingGame.Core.WorldTerrain.GroundHeightAt(sim.X, sim.Y);
            }
            // Pose: figure at _pos facing its motion; legs swing with speed; the near arm up on the wing while holding.
            float speedNow = _vel.magnitude;
            _figure.transform.position = _pos;
            Vector3 face = speedNow > 0.2f ? _vel.normalized : fwd;
            _figure.transform.rotation = Quaternion.LookRotation(face, Vector3.up);
            _phase += speedNow * 2.2f * dt;
            float swing = Mathf.Clamp(speedNow / ReleaseSpeedMs, 0.15f, 1f) * 35f;
            _legL.localRotation = Quaternion.Euler(Mathf.Sin(_phase) * swing, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-Mathf.Sin(_phase) * swing, 0f, 0f);
            _armUp.localRotation = Holding ? Quaternion.Euler(0f, 0f, -Side * 80f) : Quaternion.Euler(Mathf.Sin(_phase) * 20f, 0f, -10f * Side);
        }

        private GameObject BuildFigure()
        {
            var root = new GameObject("WingRunner");
            var skin = new Color(0.85f, 0.65f, 0.5f); var shirt = new Color(0.9f, 0.3f, 0.2f); var pants = new Color(0.2f, 0.25f, 0.5f);
            Part(root, "Torso", PrimitiveType.Capsule, new Vector3(0, 1.2f, 0), new Vector3(0.36f, 0.3f, 0.22f), shirt);
            Part(root, "Head", PrimitiveType.Sphere, new Vector3(0, 1.72f, 0), new Vector3(0.24f, 0.24f, 0.24f), skin);
            _legL = Part(root, "LegL", PrimitiveType.Capsule, new Vector3(-0.1f, 0.85f, 0), new Vector3(0.14f, 0.42f, 0.14f), pants).transform;
            _legR = Part(root, "LegR", PrimitiveType.Capsule, new Vector3(0.1f, 0.85f, 0), new Vector3(0.14f, 0.42f, 0.14f), pants).transform;
            // Legs pivot at the hip: put the capsule under an empty at hip height.
            _legL = Pivot(root, _legL, new Vector3(-0.1f, 0.9f, 0)); _legR = Pivot(root, _legR, new Vector3(0.1f, 0.9f, 0));
            var arm = Part(root, "ArmUp", PrimitiveType.Capsule, new Vector3(Side * 0.28f, 1.35f, 0), new Vector3(0.1f, 0.3f, 0.1f), skin);
            _armUp = Pivot(root, arm.transform, new Vector3(Side * 0.22f, 1.5f, 0));
            Part(root, "ArmOther", PrimitiveType.Capsule, new Vector3(-Side * 0.26f, 1.2f, 0), new Vector3(0.1f, 0.3f, 0.1f), skin);
            return root;
        }

        private static Transform Pivot(GameObject root, Transform part, Vector3 hip)
        {
            var p = new GameObject(part.name + "Pivot");
            p.transform.SetParent(root.transform, false);
            p.transform.localPosition = hip;
            Vector3 local = part.localPosition - hip;
            part.SetParent(p.transform, false);
            part.localPosition = local;
            return p.transform;
        }

        private static GameObject Part(GameObject parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("FlyingGame/Lit") ?? Shader.Find("Unlit/Color")) { color = c };
            return go;
        }
    }
}

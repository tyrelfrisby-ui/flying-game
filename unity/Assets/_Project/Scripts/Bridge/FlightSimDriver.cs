using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Hosts the engine-agnostic sim inside Unity: owns the Aircraft + SimLoop, advances them with a
    /// fixed-step accumulator in Update (render-rate smooth, physics-rate exact), and copies the sim
    /// state onto this GameObject's Transform. Unity never computes flight physics — it only displays it.
    /// </summary>
    public sealed class FlightSimDriver : MonoBehaviour
    {
        public const string AircraftId = "glider-2-33-like";
        public const double SpawnAltitudeM = 600.0;
        public const double SpawnIasMs = 22.0;

        public SimLoop Sim { get; private set; }
        public ControlInputs Inputs { get; set; } = ControlInputs.Neutral;

        public double IasMs { get; private set; }
        public double AltitudeM { get; private set; }
        public double AlphaDeg { get; private set; }
        public double BetaDeg { get; private set; }

        private double _accumulator;

        private void Awake()
        {
            var config = UnityAircraftConfigLoader.LoadFromStreamingAssets(AircraftId);
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, SpawnIasMs, SpawnAltitudeM);
            if (!trim.Converged)
            {
                Debug.LogError("Spawn trim failed to converge; starting from level attitude.");
            }

            double half = trim.ThetaRad / 2.0;
            var attitude = new Quat(0, System.Math.Sin(half), 0, System.Math.Cos(half));
            var velocityBody = new Vec3(
                SpawnIasMs * System.Math.Cos(trim.AlphaRad), 0, SpawnIasMs * System.Math.Sin(trim.AlphaRad));
            var state = new RigidBodyState(new Vec3(0, 0, -SpawnAltitudeM), attitude, velocityBody, Vec3.Zero);

            var deflections = new ControlDeflections(0, trim.ElevatorRad, 0, 0);
            Sim = new SimLoop(new Aircraft(config, state, deflections));
            ApplyStateToTransform();
        }

        private void Update()
        {
            Sim.Advance(Time.deltaTime, Inputs, ref _accumulator);
            ApplyStateToTransform();
        }

        public void ResetFlight()
        {
            _accumulator = 0;
            Awake();
        }

        private void ApplyStateToTransform()
        {
            RigidBodyState s = Sim.Aircraft.State;
            transform.SetPositionAndRotation(CoordinateMap.ToUnity(s.Position), CoordinateMap.ToUnity(s.Attitude));

            Vec3 v = s.Velocity;
            IasMs = v.Length;
            AltitudeM = -s.Position.Z;
            AlphaDeg = System.Math.Atan2(v.Z, v.X) * 180.0 / System.Math.PI;
            BetaDeg = IasMs > 1e-3 ? System.Math.Asin(System.Math.Clamp(v.Y / IasMs, -1, 1)) * 180.0 / System.Math.PI : 0;
        }
    }
}

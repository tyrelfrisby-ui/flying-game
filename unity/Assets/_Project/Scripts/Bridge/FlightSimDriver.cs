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
        public string AircraftId { get; private set; } = "glider-2-33-like";
        public string AircraftName { get; private set; } = "";
        public const double SpawnAltitudeM = 600.0;
        public double SpawnIasMs { get; private set; } = 22.0;   // per type, from AircraftConfig.SpawnIasMs

        /// <summary>Raw elevator stick fraction (-1..1) that reproduces the spawn trim through the config's
        /// stick shaping — the pitch-trim slider is preset to this so the aircraft holds level hands-off.</summary>
        public double TrimStick { get; private set; }

        public SimLoop Sim { get; private set; }

        /// <summary>Raised after the sim is rebuilt for a (possibly different) aircraft — visuals rebuild on it.</summary>
        public event System.Action AircraftChanged;
        public ControlInputs Inputs { get; set; } = ControlInputs.Neutral;

        public double IasMs { get; private set; }
        public double AltitudeM { get; private set; }
        public double AlphaDeg { get; private set; }
        public double BetaDeg { get; private set; }

        /// <summary>Ground-frame velocity in Unity world axes (the flight path the chase camera follows).</summary>
        public Vector3 WorldVelocityUnity { get; private set; }

        // Spin grading (owner benchmarks: ~100 ft/s descent, ~300 ft and ~3 s per turn in a 2-33).
        public double DescentFtPerSec { get; private set; }
        public double SecPerTurn { get; private set; }      // 0 when not rotating
        public double FtPerTurn { get; private set; }

        private double _accumulator;
        private double _prevHeadingRad;
        private double _headingRateFilt;
        private double _descentFilt;

        private void Awake()
        {
            var config = UnityAircraftConfigLoader.LoadFromStreamingAssets(AircraftId);
            AircraftName = string.IsNullOrEmpty(config.DisplayName) ? AircraftId : config.DisplayName;
            SpawnIasMs = config.SpawnIasMs > 0 ? config.SpawnIasMs : 22.0;
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, SpawnIasMs, SpawnAltitudeM);
            TrimStick = trim.Converged ? Aircraft.StickForDeflection(trim.ElevatorRad, config.Controls.Elevator) : 0.0;
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
            UpdateSpinMetrics(Time.deltaTime);
        }

        private void UpdateSpinMetrics(double dt)
        {
            if (dt <= 0)
            {
                return;
            }

            RigidBodyState s = Sim.Aircraft.State;
            Quat q = s.Attitude;
            double heading = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
            double dPsi = heading - _prevHeadingRad;
            if (dPsi > System.Math.PI) dPsi -= 2 * System.Math.PI;
            if (dPsi < -System.Math.PI) dPsi += 2 * System.Math.PI;
            _prevHeadingRad = heading;

            double k = 1 - System.Math.Exp(-dt / 0.8); // ~0.8 s low-pass
            _headingRateFilt += (dPsi / dt - _headingRateFilt) * k;
            double sinkMs = s.Attitude.Rotate(s.Velocity).Z; // world +z is down
            _descentFilt += (sinkMs - _descentFilt) * k;

            DescentFtPerSec = _descentFilt * 3.28084;
            double omega = System.Math.Abs(_headingRateFilt);
            SecPerTurn = omega > 0.15 ? 2 * System.Math.PI / omega : 0;
            FtPerTurn = SecPerTurn > 0 ? DescentFtPerSec * SecPerTurn : 0;
        }

        public void ResetFlight()
        {
            _accumulator = 0;
            Awake();
            AircraftChanged?.Invoke();
        }

        public void SwitchAircraft(string id)
        {
            AircraftId = id;
            ResetFlight();
        }

        /// <summary>Adopt an externally-built sim (challenge spawns the aircraft at its start state).</summary>
        public void AdoptSim(SimLoop sim)
        {
            Sim = sim;
            _accumulator = 0;
            ApplyStateToTransform();
        }

        private void ApplyStateToTransform()
        {
            RigidBodyState s = Sim.Aircraft.State;
            transform.SetPositionAndRotation(CoordinateMap.ToUnity(s.Position), CoordinateMap.ToUnity(s.Attitude));

            Vec3 v = s.Velocity;
            WorldVelocityUnity = CoordinateMap.ToUnity(s.Attitude.Rotate(v));
            IasMs = v.Length;
            AltitudeM = -s.Position.Z;
            AlphaDeg = System.Math.Atan2(v.Z, v.X) * 180.0 / System.Math.PI;
            BetaDeg = IasMs > 1e-3 ? System.Math.Asin(System.Math.Clamp(v.Y / IasMs, -1, 1)) * 180.0 / System.Math.PI : 0;
        }
    }
}

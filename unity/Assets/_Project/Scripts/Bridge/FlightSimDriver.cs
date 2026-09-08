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

        /// <summary>Optional constraint applied after each frame's sim steps (wing runner).</summary>
        public System.Action<Aircraft> PostStep;

        /// <summary>Raised after the sim is rebuilt for a (possibly different) aircraft — visuals rebuild on it.</summary>
        public event System.Action AircraftChanged;
        public ControlInputs Inputs { get; set; } = ControlInputs.Neutral;

        public double IasMs { get; private set; }
        public double AltitudeM { get; private set; }
        public double AlphaDeg { get; private set; }
        public double BetaDeg { get; private set; }

        /// <summary>Ground-frame velocity in Unity world axes (the flight path the chase camera follows).</summary>
        public Vector3 WorldVelocityUnity { get; private set; }

        // Shared telemetry (audio / structure / HUD / net). Safe before Spawn: null Sim → neutral values.
        public double LoadFactorG => Sim?.Aircraft?.LoadFactorZ ?? 1.0;
        public double Throttle01 => Sim?.Aircraft?.Throttle01 ?? 0.0;
        public double EngineRpm => Sim?.Aircraft?.EngineRpm ?? 0.0;
        public bool Powered => Sim?.Aircraft?.Config?.Propulsion != null;
        public bool IsJet => Powered && Sim.Aircraft.Config.Propulsion.PropDiameterM <= 0.0;
        public bool OnGround => Sim != null && AltitudeM - FlyingGame.Core.WorldTerrain.GroundHeightAt(Sim.Aircraft.State.Position.X, Sim.Aircraft.State.Position.Y) < 3.0;

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
            Spawn();
        }

        /// <summary>Apply the landing-page choices: aircraft + start position, then rebuild visuals.</summary>
        public void ApplySession()
        {
            AircraftId = SessionSettings.AircraftId;
            ResetFlight();
        }

        /// <summary>True when spawned on the ground (runway start) — until the first liftoff.</summary>
        public bool GroundStart { get; private set; }

        private void Spawn()
        {
            var config = UnityAircraftConfigLoader.LoadFromStreamingAssets(AircraftId);
            AircraftName = string.IsNullOrEmpty(config.DisplayName) ? AircraftId : config.DisplayName;
            SpawnIasMs = config.SpawnIasMs > 0 ? config.SpawnIasMs : 22.0;
            FlyingGame.Core.WorldTerrain.Airport ap = SessionSettings.Airport;
            bool ground = SessionSettings.StartMode == SessionSettings.Start.OnTheRunway;
            GroundStart = ground;

            if (ground && config.Floats != null)
            {
                // Floatplane: "on the runway" means on the water — at rest on the field's lake, heading north.
                FlyingGame.Core.WorldTerrain.Lake lake = FlyingGame.Core.WorldTerrain.Lakes[Mathf.Clamp(SessionSettings.AirportIndex, 0, FlyingGame.Core.WorldTerrain.Lakes.Length - 1)];
                var wpos = new Vec3(lake.Cx - lake.Rx * 0.5, lake.Cy, -(lake.SurfaceM + 1.3));
                var wstate = new RigidBodyState(wpos, new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
                TrimStick = 0.0;
                Sim = new SimLoop(new Aircraft(config, wstate, ControlDeflections.Neutral));
                ApplyStateToTransform();
                return;
            }
            if (ground)
            {
                // At rest at the south threshold of the main paved runway, heading north (+x), sitting on
                // the wheels: lowest main-gear contact 2 cm into the surface so the struts settle.
                double gearZ = 0.0;
                foreach (var g in config.Gear) if (!g.IsTailwheel) gearZ = System.Math.Max(gearZ, g.Pos[2]);
                double x = ap.X - FlyingGame.Core.WorldTerrain.RunwayLengthM * 0.5 + 80.0;
                var pos = new Vec3(x, ap.Y, -(ap.ElevationM + gearZ - 0.02));
                var state = new RigidBodyState(pos, new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
                TrimStick = 0.0;
                Sim = new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
                ApplyFixedSlats(config);
                ApplyStateToTransform();
                return;
            }

            // Event starts: the race begins 800 m short of gate 1 at 60 m AGL heading through it; the STOL
            // contest begins on a 1.2 km final for the gravel strip at 90 m AGL.
            double spawnAlt = ap.ElevationM + SpawnAltitudeM, spawnX = ap.X - 600, spawnY = ap.Y, spawnHdg = 0.0;
            string ch = SessionSettings.ChallengeId;
            if (ch == "event:race")
            {
                var g = RaceCourse.Elements[0];
                spawnX = g.X - g.Forward.X * 800; spawnY = g.Y - g.Forward.Y * 800; spawnHdg = g.HeadingDeg * System.Math.PI / 180;
                spawnAlt = FlyingGame.Core.WorldTerrain.GroundHeightAt(spawnX, spawnY) + 60;
            }
            else if (ch == "event:dust")
            {
                // Crop dusting: 1 km south of the field at 40 m AGL heading north — the wires are 100 yards in.
                spawnX = CropField.X0 - 1000; spawnY = (CropField.Y0 + CropField.Y1) / 2;
                spawnAlt = CropField.ElevationM + 40;
            }
            else if (ch == "event:stol")
            {
                var dirt = System.Array.Find(FlyingGame.Core.WorldTerrain.AirportStrips, st => st.Kind == "gravel");
                spawnX = ap.X + dirt.Dx - dirt.Length / 2 - 1200; spawnY = ap.Y + dirt.Dy;
                spawnAlt = ap.ElevationM + 90;
            }
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, SpawnIasMs, spawnAlt);
            TrimStick = trim.Converged ? Aircraft.StickForDeflection(trim.ElevatorRad, config.Controls.Elevator) : 0.0;
            if (!trim.Converged)
            {
                Debug.LogError("Spawn trim failed to converge; starting from level attitude.");
            }

            double half = trim.ThetaRad / 2.0;
            var pitchQ = new Quat(0, System.Math.Sin(half), 0, System.Math.Cos(half));
            var yawQ = new Quat(0, 0, System.Math.Sin(spawnHdg / 2), System.Math.Cos(spawnHdg / 2));
            var attitude = Quat.Multiply(yawQ, pitchQ);
            var velocityBody = new Vec3(
                SpawnIasMs * System.Math.Cos(trim.AlphaRad), 0, SpawnIasMs * System.Math.Sin(trim.AlphaRad));
            var airState = new RigidBodyState(new Vec3(spawnX, spawnY, -spawnAlt), attitude, velocityBody, Vec3.Zero);

            var deflections = new ControlDeflections(0, trim.ElevatorRad, 0, 0);
            Sim = new SimLoop(new Aircraft(config, airState, deflections));
            ApplyFixedSlats(config);
            ApplyStateToTransform();
        }

        private void Update()
        {
            Sim.Advance(Time.deltaTime, Inputs, ref _accumulator);
            PostStep?.Invoke(Sim.Aircraft);   // e.g. the wing runner holding the wings level on the ground roll
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

        /// <summary>Fixed leading-edge slats (bushwheel Cub): any strip with a slat config flies with it fully out.</summary>
        private void ApplyFixedSlats(FlyingGame.Core.DataContracts.AircraftConfig config)
        {
            foreach (var sf in config.Surfaces) foreach (var st in sf.Strips) if (st.Slat != null) { Sim.Aircraft.SlatFraction = 1.0; return; }
        }

        /// <summary>True when the type has flaps (any strip carries a flap config).</summary>
        public bool HasFlaps
        {
            get { if (Sim == null) return false; foreach (var sf in Sim.Aircraft.Config.Surfaces) foreach (var st in sf.Strips) if (st.Flap != null) return true; return false; }
        }

        public void ResetFlight()
        {
            _accumulator = 0;
            Spawn();
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

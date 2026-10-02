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
        /// <summary>Practice exercises: the game replaces every axis but the user's before each step (PracticeController).</summary>
        public System.Func<ControlInputs, float, ControlInputs> InputFilter;
        /// <summary>Extra reason to record the per-step force samples (the wheel-force overlay in the side-view exercises).</summary>
        public bool ForceCapture;
        /// <summary>Ground-reference mode (owner 2026-09-15): on a runway lesson, or near the ground in free flight, the chase
        /// camera and the flight path vector follow the GROUND track (wind included), not the air mass — the runway is
        /// the reference, so a crosswind crab shows as the nose pointing off the track.</summary>
        public bool GroundReferenceForced;
        public bool GroundReference
        {
            get
            {
                if (GroundReferenceForced) return true;
                if (Sim?.Aircraft == null) return false;
                var p = Sim.Aircraft.State.Position;
                double agl = -p.Z - FlyingGame.Core.WorldTerrain.GroundHeightAt(p.X, p.Y);
                return agl < 100.0;   // the approach / landing / take-off regime
            }
        }

        public double IasMs { get; private set; }
        public double AltitudeM { get; private set; }
        public double AlphaDeg { get; private set; }
        public double BetaDeg { get; private set; }

        /// <summary>Ground-frame velocity in Unity world axes (the inertial flight path: HUD flight-path marker).</summary>
        public Vector3 WorldVelocityUnity { get; private set; }

        /// <summary>AIR-relative velocity in Unity world axes (ground velocity minus the local wind). The chase
        /// camera follows THIS, so a steady crosswind crab does not read as a yaw — only real sideslip does.</summary>
        public Vector3 AirVelocityUnity { get; private set; }

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
        /// <summary>Started on final with the power at idle (the left pad knob begins at idle, not mid-throttle).</summary>
        public bool IdleStart { get; private set; }
        /// <summary>Spoiler setting the on-final start was trimmed with (gliders: half); the left pad knob begins there.</summary>
        public double StartSpoilerFraction { get; private set; }

        private void Spawn()
        {
            var config = UnityAircraftConfigLoader.LoadFromStreamingAssets(AircraftId);
            SessionSettings.ApplyFeel(config);   // the user's expo / dead-zone tuning (OPTIONS)
            AircraftName = string.IsNullOrEmpty(config.DisplayName) ? AircraftId : config.DisplayName;
            SpawnIasMs = config.SpawnIasMs > 0 ? config.SpawnIasMs : 22.0;
            FlyingGame.Core.WorldTerrain.Airport ap = SessionSettings.Airport;
            bool ground = SessionSettings.StartMode == SessionSettings.Start.OnTheRunway;
            GroundStart = ground;
            IdleStart = SessionSettings.StartMode == SessionSettings.Start.OnFinal || SessionSettings.StartMode == SessionSettings.Start.InThermal;
            StartSpoilerFraction = 0;

            if (SessionSettings.StartMode == SessionSettings.Start.InThermal && FlyingGame.Core.Atmosphere.Thermals.Count > 0)
            {
                // Already circling in the field's best thermal: banked at the turning min-sink speed, trimmed, idle.
                FlyingGame.Core.Thermal th = PickThermal(ap);
                double baseAlt = -th.SurfaceCenter.Z;
                double alt = System.Math.Max(baseAlt + 300, baseAlt + 0.3 * (th.TopAltitudeM - baseAlt));
                ThermalSpawn.Plan plan = ThermalSpawn.Compute(config, th, alt);
                TrimStick = Aircraft.StickForDeflection(plan.ElevatorRad, config.Controls.Elevator);
                Sim = new SimLoop(new Aircraft(config, plan.State, new ControlDeflections(0, plan.ElevatorRad, 0, 0)));
                if (config.RetractableGear) Sim.Aircraft.SetGear(false, immediate: true);
                ApplyFixedSlats(config);
                ApplyStateToTransform();
                return;
            }

            if (IdleStart && config.Floats == null && SessionSettings.StartMode == SessionSettings.Start.OnFinal)
            {
                // On final: 300 ft AGL on the centreline, idle, trimmed at best glide on the best-glide angle.
                var (fState, glide, _) = ApproachSpawn.Compute(config, ap, SessionSettings.ChosenRunway());
                TrimStick = Aircraft.StickForDeflection(glide.ElevatorRad, config.Controls.Elevator);
                Sim = new SimLoop(new Aircraft(config, fState, new ControlDeflections(0, glide.ElevatorRad, 0, glide.SpoilerFraction)));
                StartSpoilerFraction = glide.SpoilerFraction;
                ApplyFixedSlats(config);
                ApplyStateToTransform();
                return;
            }

            if (IdleStart && config.Floats != null && SessionSettings.StartMode == SessionSettings.Start.OnFinal)
            {
                // Floatplane on final: to the field's lake, 300 ft over the water, idle, best glide (owner 2026-09-14).
                FlyingGame.Core.WorldTerrain.Lake lake = FlyingGame.Core.WorldTerrain.Lakes[Mathf.Clamp(SessionSettings.AirportIndex, 0, FlyingGame.Core.WorldTerrain.Lakes.Length - 1)];
                var (fState, glide, _) = ApproachSpawn.ComputeToLake(config, lake);
                TrimStick = Aircraft.StickForDeflection(glide.ElevatorRad, config.Controls.Elevator);
                Sim = new SimLoop(new Aircraft(config, fState, new ControlDeflections(0, glide.ElevatorRad, 0, glide.SpoilerFraction)));
                StartSpoilerFraction = glide.SpoilerFraction;
                ApplyStateToTransform();
                return;
            }
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
                // At rest at the south threshold of the main paved runway, heading north (+x), sitting on ALL its
                // wheels (three-point stance for a taildragger — spawned level, the tail dropped and broke off).
                var rw = SessionSettings.ChosenRunway();   // headwind or crosswind runway, lined up on its heading
                (double sx, double sy) = rw.Start;
                RigidBodyState state = FlyingGame.Core.LandingGear.RestingState(config, sx, sy, ap.ElevationM, rw.HeadingRad);
                TrimStick = 0.0;
                Sim = new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
                ApplyFixedSlats(config);
                ApplyStateToTransform();
                return;
            }

            // Event starts: the race begins 800 m short of gate 1 at 60 m AGL heading through it; the STOL
            // contest begins on a 1.2 km final for the gravel strip at 90 m AGL.
            double spawnAlt = ap.ElevationM + SpawnAltitudeM, spawnX = ap.X - 600, spawnY = ap.Y, spawnHdg = 0.0;
            string ch = SessionSettings.StartMode == SessionSettings.Start.InCombatZone ? "event:combat" : SessionSettings.ChallengeId;   // same start as the combat event
            if (SessionSettings.StartMode == SessionSettings.Start.InAeroBox)
            {
                // Running in to the aerobatic box: 300 m short of its south edge, heading north through the middle, 700 m
                // above the ground (box floor 100 m, ceiling 1,067 m).
                spawnX = AeroBox.CenterX - AeroBox.SizeM / 2 - 300; spawnY = AeroBox.CenterYAt(SessionSettings.AirportIndex); spawnHdg = 0.0;
                spawnAlt = FlyingGame.Core.WorldTerrain.GroundHeightAt(spawnX, spawnY) + 700;
            }
            else if (ch == "event:race")
            {
                var g = RaceCourse.ElementsFor(SessionSettings.AirportIndex)[0];   // this plateau's course
                spawnX = g.X - g.Forward.X * 800; spawnY = g.Y - g.Forward.Y * 800; spawnHdg = g.HeadingDeg * System.Math.PI / 180;
                spawnAlt = FlyingGame.Core.WorldTerrain.GroundHeightAt(spawnX, spawnY) + 60;
            }
            else if (ch == "event:dust")
            {
                // Crop dusting: 1 km south of the field at 40 m AGL heading north — the wires are 100 yards in.
                CropField f = CropField.For(SessionSettings.AirportIndex);   // this plateau's field
                spawnX = f.X0 - 1000; spawnY = (f.Y0 + f.Y1) / 2;
                spawnAlt = f.ElevationM + 40;
            }
            else if (ch == "event:combat")
            {
                // Combat zone: 800 m up at its west edge, heading in — the drones are already orbiting.
                spawnX = FlyingGame.Core.Combat.CombatZone.X0 + 300; spawnY = FlyingGame.Core.Combat.CombatZone.CentreY;
                spawnAlt = FlyingGame.Core.WorldTerrain.GroundHeightAt(spawnX, spawnY) + 800; spawnHdg = 0.0;
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
            // Spawn with the trimmed speed relative to the AIR: ground velocity = air velocity + local wind, so in
            // a crosswind the aircraft starts in a coordinated crab (zero sideslip, camera on the tail) instead of
            // a 12° sideslip that takes seconds to weathervane out.
            var spawnPos = new Vec3(spawnX, spawnY, -spawnAlt);
            var velocityBody = new Vec3(
                SpawnIasMs * System.Math.Cos(trim.AlphaRad), 0, SpawnIasMs * System.Math.Sin(trim.AlphaRad))
                + attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(spawnPos));
            var airState = new RigidBodyState(spawnPos, attitude, velocityBody, Vec3.Zero);

            var deflections = new ControlDeflections(0, trim.ElevatorRad, 0, 0);
            Sim = new SimLoop(new Aircraft(config, airState, deflections));
            if (config.RetractableGear) Sim.Aircraft.SetGear(false, immediate: true);   // airborne start: wheels up
            ApplyFixedSlats(config);
            ApplyStateToTransform();
        }

        /// <summary>Flight replay owns the aircraft pose (<see cref="FlightReplay"/>): the sim is not stepped.</summary>
        public bool Replaying;

        /// <summary>Replay: redraw the aircraft from the pose the replay just put into it, with the wind recorded then.</summary>
        public void ShowReplayPose(Vec3 windWorld) => ApplyStateToTransform(windWorld);

        /// <summary>The strongest thermal within 1.5 km of the airport (else the nearest one).</summary>
        private static FlyingGame.Core.Thermal PickThermal(FlyingGame.Core.WorldTerrain.Airport ap)
        {
            FlyingGame.Core.Thermal best = null, nearest = null; double bestW = -1, nearD = double.MaxValue;
            foreach (FlyingGame.Core.Thermal t in FlyingGame.Core.Atmosphere.Thermals)
            {
                double d = System.Math.Sqrt(System.Math.Pow(t.SurfaceCenter.X - ap.X, 2) + System.Math.Pow(t.SurfaceCenter.Y - ap.Y, 2));
                if (d < nearD) { nearD = d; nearest = t; }
                if (d < 1500 && t.CoreUpdraftMs > bestW) { bestW = t.CoreUpdraftMs; best = t; }
            }
            return best ?? nearest;
        }

        private void Update()
        {
            if (Replaying) return;
            Sim.Aircraft.CaptureForces = SessionSettings.ShowForceVectors || ForceCapture;
            ControlInputs inputs = InputFilter != null ? InputFilter(Inputs, Time.deltaTime) : Inputs;
            Sim.Advance(Time.deltaTime, inputs, ref _accumulator);
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

        private void ApplyStateToTransform() => ApplyStateToTransform(null);

        private void ApplyStateToTransform(Vec3? windOverride)
        {
            RigidBodyState s = Sim.Aircraft.State;
            transform.SetPositionAndRotation(CoordinateMap.ToUnity(s.Position), CoordinateMap.ToUnity(s.Attitude));

            // State.Velocity is INERTIAL (ground) velocity in body axes. Everything the pilot reads — airspeed,
            // angle of attack, sideslip — and the chase camera's follow vector are AIR-relative: subtract the
            // local wind (steady wind + slope lift + thermals + gusts), exactly as the aero model does. With the
            // ground vector, a crosswind read as a permanent sideslip and put the camera off the tail.
            Vec3 groundVelWorld = s.Attitude.Rotate(s.Velocity);
            Vec3 windWorld = windOverride ?? Atmosphere.WindAtPosition(s.Position);
            Vec3 v = s.Velocity - s.Attitude.Conjugate().Rotate(windWorld);   // air-relative, body axes
            WorldVelocityUnity = CoordinateMap.ToUnity(groundVelWorld);
            AirVelocityUnity = CoordinateMap.ToUnity(groundVelWorld - windWorld);
            IasMs = v.Length;
            AltitudeM = -s.Position.Z;
            AlphaDeg = System.Math.Atan2(v.Z, v.X) * 180.0 / System.Math.PI;
            BetaDeg = IasMs > 1e-3 ? System.Math.Asin(System.Math.Clamp(v.Y / IasMs, -1, 1)) * 180.0 / System.Math.PI : 0;
        }
    }
}

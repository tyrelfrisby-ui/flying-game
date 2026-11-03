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
        public double TrimStick { get; set; }

        private SimLoop _sim;

        /// <summary>The flying aeroplane. Assigning it applies the pilot's saved loadout, so every path that builds or
        /// adopts an aeroplane — Spawn's six branches, ResetFlight, a challenge's AdoptSim — gets the weight and
        /// balance he set on the launch page without each of them having to remember.</summary>
        public SimLoop Sim
        {
            get => _sim;
            private set
            {
                _sim = value;
                if (_sim?.Aircraft != null) SessionSettings.ApplyLoadTo(AircraftId, _sim.Aircraft);
            }
        }

        /// <summary>Optional constraint applied after each frame's sim steps (wing runner).</summary>
        public System.Action<Aircraft> PostStep;

        /// <summary>Raised after the sim is rebuilt for a (possibly different) aircraft — visuals rebuild on it.</summary>
        public event System.Action AircraftChanged;
        public ControlInputs Inputs { get; set; } = ControlInputs.Neutral;
        /// <summary>Practice exercises: the game replaces every axis but the user's before each step (PracticeController).</summary>
        public System.Func<ControlInputs, float, ControlInputs> InputFilter;
        /// <summary>The autopilot / yaw damper (AERO panel), applied after <see cref="InputFilter"/>: it replaces only the channels it flies.</summary>
        public System.Func<ControlInputs, float, ControlInputs> AutoFilter;
        /// <summary>Extra reason to record the per-step force samples (the wheel-force overlay in the side-view exercises).</summary>
        public bool ForceCapture;
        /// <summary>Ground-reference mode (owner 2026-09-15): on a runway lesson, or near the ground in free flight, the chase
        /// camera and the flight path vector follow the GROUND track (wind included), not the air mass — the runway is
        /// the reference, so a crosswind crab shows as the nose pointing off the track.</summary>
        public bool GroundReferenceForced;
        /// <summary>Below this height above the surface the flight path vector switches to the GROUND track (and turns magenta,
        /// with the point-of-impact target) — owner 2026-10-05: 500 ft.</summary>
        public const double GroundReferenceAglM = 152.4;
        public bool GroundReference
        {
            get
            {
                if (GroundReferenceForced) return true;
                if (Sim?.Aircraft == null) return false;
                var p = Sim.Aircraft.State.Position;
                double agl = -p.Z - FlyingGame.Core.WorldTerrain.GroundHeightAt(p.X, p.Y);
                return agl < GroundReferenceAglM;   // the approach / landing / take-off regime
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

        public const double DogfightHalfSeparationM = 1200, DogfightLateralOffsetM = 60, DogfightAglM = 1500;

        /// <summary>
        /// Build the aeroplane for the session's start, then put the inputs the sim will fly on the HANDS-OFF trim of that
        /// start (stick and rudder centred, elevator on the trim, the start's power). Until TouchFlightControls publishes
        /// its first frame, <see cref="Inputs"/> would otherwise still be the PREVIOUS flight's last frame -- whatever the
        /// stick, the pad and the power were doing when the menu opened (the controls stop publishing while it is open) --
        /// and nothing orders this Update before the controls' (owner 2026-11-02: the Pitts went left on one launch, right
        /// on another). The first frame after a respawn is a long one, so that was up to MaxCatchUpSec = 0.05 s, ten sim
        /// steps, of the last flight's stick on the new one: enough on its own to pick the spiral's direction.
        /// </summary>
        private void Spawn()
        {
            SpawnAircraft();
            double lever = Powered ? (GroundStart || IdleStart ? 1.0 : 1.0 - 2.0 * RigSolver.RigThrottle)   // idle, or the air start's 75 %
                : GroundStart ? 1.0 : IdleStart ? StartSpoilerFraction : 0.0;                              // glider: spoilers as TouchFlightControls.PresetTrim sets them
            Inputs = new ControlInputs(0, TrimStick, 0, lever);
        }

        private void SpawnAircraft()
        {
            var config = UnityAircraftConfigLoader.LoadFromStreamingAssets(AircraftId);
            SessionSettings.ApplyFeel(config);   // the user's expo / dead-zone tuning (OPTIONS)
            // (The 2026-10-30 SessionLoading.Apply stood here. The 2026-10-31 loading model applies the load
            // through Sim's setter instead - SessionSettings.ApplyLoadTo - which also covers AdoptSim.)
            AircraftName = string.IsNullOrEmpty(config.DisplayName) ? AircraftId : config.DisplayName;
            SpawnIasMs = config.SpawnIasMs > 0 ? config.SpawnIasMs : 22.0;
            FlyingGame.Core.WorldTerrain.Airport ap = SessionSettings.Airport;
            // A dogfight always starts head-on in the middle of the combat zone, whatever START says.
            bool dogfight = SessionSettings.ChallengeId == "event:dogfight";
            bool ground = SessionSettings.StartMode == SessionSettings.Start.OnTheRunway && !dogfight;
            GroundStart = ground;
            IdleStart = !dogfight && (SessionSettings.StartMode == SessionSettings.Start.OnFinal || SessionSettings.StartMode == SessionSettings.Start.InThermal);
            StartSpoilerFraction = 0;

            if (!dogfight && SessionSettings.StartMode == SessionSettings.Start.InThermal && FlyingGame.Core.Atmosphere.Thermals.Count > 0)
            {
                // Already circling in the field's best thermal: banked at the turning min-sink speed, trimmed, idle.
                FlyingGame.Core.Thermal th = PickThermal(ap);
                // Above the ground that is under the COLUMN, not the ground at the airport. SoaringScenery still
                // places its columns on the four-plateau pattern, so on the island a thermal can stand over a
                // mesa 1,000 m above its field; starting 300 m over the field would then be 700 m inside it, and
                // an aeroplane inside terrain takes LandingGear.Compute to NaN on its first step. Re-authoring the
                // columns onto the world's own lift_terrain (S1-S7) is the weather pass; this makes the START safe
                // whatever the layout.
                double baseAlt = System.Math.Max(-th.SurfaceCenter.Z,
                    FlyingGame.Core.WorldTerrain.GroundHeightAt(th.SurfaceCenter.X, th.SurfaceCenter.Y));
                double alt = System.Math.Max(baseAlt + 300, baseAlt + 0.3 * (th.TopAltitudeM - baseAlt));
                ThermalSpawn.Plan plan = ThermalSpawn.Compute(config, th, alt);
                TrimStick = Aircraft.StickForDeflection(plan.ElevatorRad, config.Controls.Elevator);
                Sim = new SimLoop(new Aircraft(config, plan.State, new ControlDeflections(0, plan.ElevatorRad, 0, 0)));
                if (config.RetractableGear) Sim.Aircraft.SetGear(false, immediate: true);
                ApplyFixedSlats(config);
                ApplyStateToTransform();
                return;
            }

            // An AMPHIBIAN (floats + retractable wheels, the AirCam Amphib) starts like a landplane, wheels down: on the runway
            // or on final to it — the water is somewhere it flies to (2026-10-06).
            bool amphib = config.Floats != null && config.RetractableGear;
            if (IdleStart && (config.Floats == null || amphib) && SessionSettings.StartMode == SessionSettings.Start.OnFinal)
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

            if (IdleStart && config.Floats != null && !amphib && SessionSettings.StartMode == SessionSettings.Start.OnFinal)
            {
                // Floatplane on final: to the field's lake, 300 ft over the water, idle, best glide (owner 2026-09-14).
                // Flying boats (a hull: floats count 1) use the big harbor; floatplanes the field's own lake.
                FlyingGame.Core.WorldTerrain.Lake lake = StartWater(config, ap);
                var (fState, glide, _) = ApproachSpawn.ComputeToLake(config, lake);
                TrimStick = Aircraft.StickForDeflection(glide.ElevatorRad, config.Controls.Elevator);
                Sim = new SimLoop(new Aircraft(config, fState, new ControlDeflections(0, glide.ElevatorRad, 0, glide.SpoilerFraction)));
                StartSpoilerFraction = glide.SpoilerFraction;
                ApplyStateToTransform();
                return;
            }
            if (ground && config.Floats != null && !amphib)
            {
                // Floatplane: "on the runway" means on the water — at rest on the field's lake, heading north.
                // Flying boats (a hull: floats count 1) use the big harbor; floatplanes the field's own lake.
                FlyingGame.Core.WorldTerrain.Lake lake = StartWater(config, ap);
                // CG above the water: keel depth below the CG less a resting draft (Cub floats 1.55 → 1.3 m; the H-4 hull ~5.8 m).
                var wpos = new Vec3(lake.Cx - lake.Rx * (config.Floats.Count == 1 ? 0.85 : 0.5), lake.Cy, -(lake.SurfaceM + config.Floats.KeelZ - 0.25));
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
                // Sit on the ground that is actually there. A field-wide elevation is a metre or two out on a
                // sloping strip (3U2 is 0.5 %, Cabin Creek 3 %) and was hundreds of metres out while the runway
                // itself came from a template instead of the world: an aeroplane spawned inside terrain takes
                // LandingGear.Compute to NaN on its first step, which is a dead aeroplane and a lost camera.
                double padElev = FlyingGame.Core.WorldTerrain.Active != null
                    ? FlyingGame.Core.WorldTerrain.GroundHeightAt(sx, sy) : ap.ElevationM;
                RigidBodyState state = FlyingGame.Core.LandingGear.RestingState(config, sx, sy, padElev, rw.HeadingRad);
                TrimStick = 0.0;
                Sim = new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
                ApplyFixedSlats(config);
                ApplyStateToTransform();
                return;
            }

            // Event starts: the race begins 800 m short of gate 1 at 60 m AGL heading through it; the STOL
            // contest begins on a 1.2 km final for the gravel strip at 90 m AGL.
            double spawnAlt = ap.ElevationM + SpawnAltitudeM, spawnX = ap.X - 600, spawnY = ap.Y, spawnHdg = 0.0;
            // The world authors a start for each mode (IslandSites.Starts): position, true heading and the
            // altitude it means, over the ground it means. Consume it rather than deriving one — a derived
            // start has no way to know that the ground 600 m off the end of KMEV's runway is 1,100 m lower.
            FlyingGame.Core.IslandSites.Start? authored = FlyingGame.Core.WorldTerrain.Active == null ? null
                : SessionSettings.StartMode == SessionSettings.Start.InTheAir
                    ? FlyingGame.Core.IslandRunways.Authored(ap.Name, "InTheAir")
                    : FlyingGame.Core.IslandRunways.AuthoredNearest(SessionSettings.StartMode.ToString(), ap.X, ap.Y);
            string ch = SessionSettings.StartMode == SessionSettings.Start.InCombatZone ? "event:combat" : SessionSettings.ChallengeId;   // same start as the combat event
            if (dogfight)
            {
                // Head-on: 1,200 m south of the zone's centre heading north, offset 60 m west so the merge is a pass, not a
                // collision (the opponent starts the mirror image). 1,500 m above the ground.
                spawnX = FlyingGame.Core.Combat.CombatZone.CentreX - DogfightHalfSeparationM; spawnY = FlyingGame.Core.Combat.CombatZone.CentreY - DogfightLateralOffsetM;
                spawnHdg = 0.0; spawnAlt = FlyingGame.Core.WorldTerrain.GroundHeightAt(FlyingGame.Core.Combat.CombatZone.CentreX, FlyingGame.Core.Combat.CombatZone.CentreY) + DogfightAglM;
            }
            else if (SessionSettings.StartMode == SessionSettings.Start.InAeroBox)
            {
                // Running in to the aerobatic box: 300 m short of its south edge, heading north through the middle, 700 m
                // above the ground (box floor 100 m, ceiling 1,067 m).
                spawnX = AeroBox.CenterX - AeroBox.SizeM / 2 - 300; spawnY = AeroBox.CenterYAt(WorldTerrain.NearestPlateauWith(WorldTerrain.Feature.AeroBox, SessionSettings.AirportIndex)); spawnHdg = 0.0;
                spawnAlt = FlyingGame.Core.WorldTerrain.GroundHeightAt(spawnX, spawnY) + 700;
            }
            else if (ch == "event:race")
            {
                // The nearest plateau that still HAS a course (2026-10-16: only the Valley's and the Mesa's).
                var g = RaceCourse.ElementsFor(WorldTerrain.NearestPlateauWith(WorldTerrain.Feature.RaceCourse, SessionSettings.AirportIndex))[0];
                spawnX = g.X - g.Forward.X * 800; spawnY = g.Y - g.Forward.Y * 800; spawnHdg = g.HeadingDeg * System.Math.PI / 180;
                spawnAlt = FlyingGame.Core.WorldTerrain.GroundHeightAt(spawnX, spawnY) + 60;
            }
            else if (ch == "event:dust")
            {
                // Crop dusting (2026-10-07: the south approach now runs through the city): 500 m NORTH of the field (short of the gorge rim) at 30 m AGL,
                // heading south, lined up on the first pass left (swath 18) — the wires are 100 yards in from this end.
                CropField f = CropField.For(WorldTerrain.NearestPlateauWith(WorldTerrain.Feature.CropField, SessionSettings.AirportIndex));
                spawnX = f.X1 + 500; spawnY = f.Y0 + (17 + 0.5) * CropDust.SwathM; spawnHdg = System.Math.PI;   // 500 m: short of the gorge rim
                spawnAlt = f.ElevationM + 30;
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
            // Free flight: the world's authored start wins. An EVENT (race, crop dust, STOL, dogfight) keeps its
            // own start, because the event defines where it begins.
            if (!dogfight && authored.HasValue && string.IsNullOrEmpty(SessionSettings.ChallengeId))
            {
                spawnX = authored.Value.X; spawnY = authored.Value.Y; spawnAlt = authored.Value.AltitudeM;
                spawnHdg = authored.Value.HeadingTrueDeg * System.Math.PI / 180;
            }
            // Start HANDS OFF at the type's rig condition (owner 2026-11-02): level at 75 % power on the rig, trimmed, or a glider
            // at best glide. FlyingGame.Sim.AirStart is the whole computation, engine-free, so LaunchReplicationTests fly
            // exactly this launch.
            AirStart.Plan start = AirStart.Compute(config, spawnX, spawnY, spawnAlt, spawnHdg, SpawnIasMs);
            if (start.Warning != null) Debug.LogWarning(start.Warning);
            TrimStick = start.TrimStick;
            Sim = new SimLoop(AirStart.Build(config, start));
            ApplyFixedSlats(config);
            ApplyStateToTransform();
        }

        /// <summary>Flight replay owns the aircraft pose (<see cref="FlightReplay"/>): the sim is not stepped.</summary>
        public bool Replaying;

        /// <summary>Replay: redraw the aircraft from the pose the replay just put into it, with the wind recorded then.</summary>
        public void ShowReplayPose(Vec3 windWorld) => ApplyStateToTransform(windWorld);

        /// <summary>The strongest thermal within 1.5 km of the airport (else the nearest one).</summary>
        /// <summary>The water a seaplane starts from. `WorldTerrain.Lakes` is the four-airport world's water the way
        /// `AirportStrips` was its land — one lake 3 km south-west of every field at the field's elevation less 2 m —
        /// and on the island that is a hillside: at PHDH it sits at 2.3 m where the ground is 450 m and there is no
        /// water at all, so the Cub on floats was spawned 448 m inside a hill, went to NaN on its first step and
        /// froze with its controls dead. The island authors its own water; this asks it for the nearest piece at
        /// roughly the field's own height that is long enough for the hull, and keeps the old table for the widget
        /// and anywhere no world is installed.</summary>
        private static FlyingGame.Core.WorldTerrain.Lake StartWater(FlyingGame.Core.DataContracts.AircraftConfig config,
                                                                   FlyingGame.Core.WorldTerrain.Airport ap)
        {
            bool hull = config.Floats.Count == 1;   // a flying boat: one hull, and it needs about 2.2 km of water
            if (FlyingGame.Core.WorldTerrain.Active != null)
            {
                FlyingGame.Core.WorldTerrain.Lake? w = FlyingGame.Core.IslandRunways.Water(ap, hull ? 2200 : 600);
                if (w.HasValue) return w.Value;
            }
            return hull ? FlyingGame.Core.WorldTerrain.Harbor
                : FlyingGame.Core.WorldTerrain.Lakes[Mathf.Clamp(SessionSettings.AirportIndex, 0, FlyingGame.Core.WorldTerrain.Lakes.Length - 2)];
        }

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

        /// <summary>Stopwatch ticks spent inside SimLoop.Advance by every driver, summed (the AERO_SELFTEST=perf line reports
        /// it per frame beside the frame time, with the player's height above the ground).</summary>
        public static long SimAdvanceTicks;

        private void Update()
        {
            if (Replaying) return;
            Sim.Aircraft.CaptureForces = SessionSettings.ShowForceVectors || ForceCapture;
            ControlInputs inputs = InputFilter != null ? InputFilter(Inputs, Time.deltaTime) : Inputs;
            if (AutoFilter != null) inputs = AutoFilter(inputs, Time.deltaTime);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Sim.Advance(Time.deltaTime, inputs, ref _accumulator);
            SimAdvanceTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
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

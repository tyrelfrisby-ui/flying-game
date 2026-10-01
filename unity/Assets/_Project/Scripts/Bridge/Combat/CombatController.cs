using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Air-to-air and air-to-ground gunnery in the COMBAT ZONE (owner 2026-09-10): the player's guns fire while the
    /// FIRE button is held and the aircraft is inside the zone; bullets fly through the sim's Gunnery and hit the
    /// orange target drones (three of them orbiting the zone, each a full sim Aircraft with the same damage model as
    /// the player: panels, fuel, fire), the bullseyes on the ground, or the ground. Killed drones tumble in, burn out
    /// and respawn. Other players will join the target list when the network carries hits.
    /// </summary>
    public sealed class CombatController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public readonly Gunnery Gunnery = new();
        public readonly List<GroundTarget> GroundTargets = CombatZone.BuildGroundTargets();
        public bool Firing;                 // set by TouchFlightControls (FIRE held)
        public bool InZone { get; private set; }
        public bool GunsHot => InZone && Driver?.Sim?.Aircraft?.Guns != null && Driver.Sim.Aircraft.Guns.RoundsLeft > 0;
        public int DronesDown { get; private set; }
        public int GroundHits { get; private set; }
        public int HitsOnDrones { get; private set; }
        /// <summary>Singles (owner 2026-09-12, more 2026-10-01): fast aerobatic Cassutts flying random manoeuvres and DC-3s
        /// lumbering round. Plus FORMATIONS (owner 2026-10-01): a 4-ship of P-51s (finger four) and a 3-ship of DC-3s (vic).
        /// Hit a P-51 and its flight fights back — ONE at a time, medium skill (AttackPilot); the next takes over when
        /// that one goes down. Drones only fly while the player is within 3 km of the zone (CPU).</summary>
        public const int CassuttCount = 6, Dc3Count = 2;
        public const float ActiveRangeM = 3000f;
        public static readonly Vec3[] P51Slots = { new(-35, -35, 3), new(-35, 35, 3), new(-70, 70, 6) };
        public static readonly Vec3[] Dc3Slots = { new(-45, -45, 4), new(-45, 45, 4) };
        public int HitsTaken { get; private set; }
        public const float RespawnSec = 8f;
        public static readonly Color DroneOrange = new(1f, 0.45f, 0.05f);

        private sealed class Drone
        {
            public int Id; public Aircraft Aircraft; public DronePilot Pilot; public GameObject Go; public AirframeBuilder Builder; public DamageFx Fx;
            public GunTarget Target; public bool Dead; public float DeadT; public AircraftConfig Config; public bool Aerobatic; public string Kind;
            public Flight Flight; public FormationPilot Fp; public AttackPilot Ap;
        }

        private sealed class Flight
        {
            public string Kind, ConfigId; public double Cruise; public Vec3[] Slots; public bool CanFightBack;
            public readonly List<Drone> Members = new();
            public Drone Lead, Attacker; public bool Provoked; public int Seed;
        }
        private readonly List<Flight> _flights = new();
        private readonly List<GunTarget> _stepTargets = new();
        private GunTarget _playerTarget; private Aircraft _playerTargetFor;
        private readonly List<Drone> _drones = new();
        private readonly List<GunTarget> _targets = new();
        private AircraftConfig _cassuttCfg, _dc3Cfg;
        private double _accum;
        private FlightAudio _audio;
        private string _flash; private float _flashT;
        private ParticleSystem _puffGround, _puffHit;
        private int _seedBase;

        /// <summary>Live drone positions (Unity world) for the HUD markers.</summary>
        public IEnumerable<(Vector3 pos, string kind, bool dead)> DroneMarkers()
        {
            foreach (Drone d in _drones) if (d.Go != null) yield return (d.Go.transform.position, d.Kind, d.Dead);
        }

        private void Start()
        {
            _audio = GetComponent<FlightAudio>();
            _cassuttCfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("target-drone-like");
            _dc3Cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("dc3-like");
            _seedBase = System.Environment.TickCount;   // a different set of patterns every session
            BuildPuffs();
            for (int i = 0; i < CassuttCount + Dc3Count; i++) SpawnDrone(i);
            _flights.Add(new Flight { Kind = "P-51", ConfigId = "p51d-like", Cruise = 85, Slots = P51Slots, CanFightBack = true });
            _flights.Add(new Flight { Kind = "DC-3", ConfigId = "dc3-like", Cruise = 62, Slots = Dc3Slots });
            foreach (Flight f in _flights) SpawnFlight(f);
        }

        /// <summary>Put a whole formation in the air together: the leader at one of its route's waypoints, the wingmen in
        /// their slots, all at cruise. Every member carries the same route so any of them can take over the lead.</summary>
        private void SpawnFlight(Flight f)
        {
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(f.ConfigId);
            f.Seed = _seedBase + 104729 * (_flights.IndexOf(f) + 1) + Random.Range(0, 1000);
            var route = new DronePilot(f.Seed, false, f.Cruise);
            Vec3 wp = route.Waypoints[(route.Next + 3) % route.Waypoints.Count];
            double ground = WorldTerrain.GroundHeightAt(wp.X, wp.Y);
            var start = new Vec3(wp.X, wp.Y, -(ground + System.Math.Max(450, wp.Z)));
            double hdg = Random.Range(0f, 6.28f);
            var att = new Quat(0, 0, System.Math.Sin(hdg / 2), System.Math.Cos(hdg / 2));
            var leadState = new RigidBodyState(start, att, new Vec3(f.Cruise, 0, 0), Vec3.Zero);
            for (int k = 0; k <= f.Slots.Length; k++)
            {
                Drone d;
                if (k < f.Members.Count) d = f.Members[k];
                else { d = new Drone { Id = 100 + _drones.Count, Flight = f }; _drones.Add(d); f.Members.Add(d); }
                var pilot = new DronePilot(f.Seed, false, f.Cruise) { MaxBankDeg = 20, CaptureRadiusM = 1200 };
                d.Fp = new FormationPilot(k == 0 ? Vec3.Zero : f.Slots[k - 1]);
                d.Ap = null;
                Vec3 pos = k == 0 ? start : d.Fp.SlotPosition(leadState);
                CreateDrone(d, cfg, f.Kind, pilot, pos, att, f.Cruise, aerobatic: false);
            }
            f.Lead = f.Members[0]; f.Attacker = null; f.Provoked = false;
            _targets.Clear(); foreach (Drone x in _drones) _targets.Add(x.Target);
        }

        private void SpawnDrone(int i)
        {
            bool aerobatic = i < CassuttCount;
            AircraftConfig cfg = aerobatic ? _cassuttCfg : _dc3Cfg;
            var d = _drones.Count > i ? _drones[i] : null;
            if (d == null) { d = new Drone { Id = 100 + i }; _drones.Add(d); }
            var pilot = new DronePilot(_seedBase + i * 7919 + Random.Range(0, 1000), aerobatic, aerobatic ? 65.0 : 70.0);
            Vec3 wp = pilot.Waypoints[(pilot.Next + 3) % pilot.Waypoints.Count];
            double ground = WorldTerrain.GroundHeightAt(wp.X, wp.Y);
            var start = new Vec3(wp.X, wp.Y, -(ground + wp.Z));
            double hdg = Random.Range(0f, 6.28f);
            var att = new Quat(0, 0, System.Math.Sin(hdg / 2), System.Math.Cos(hdg / 2));
            CreateDrone(d, cfg, aerobatic ? "CASSUTT" : "DC-3", pilot, start, att, pilot.CruiseMs, aerobatic);
            _targets.Clear(); foreach (Drone x in _drones) _targets.Add(x.Target);
        }

        private void CreateDrone(Drone d, AircraftConfig cfg, string kind, DronePilot pilot, Vec3 start, Quat att, double speed, bool aerobatic)
        {
            d.Config = cfg; d.Aerobatic = aerobatic; d.Kind = kind; d.Pilot = pilot;
            if (d.Go != null) Destroy(d.Go);
            d.Aircraft = new Aircraft(cfg, new RigidBodyState(start, att, new Vec3(speed, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            if (cfg.RetractableGear) d.Aircraft.SetGear(false, immediate: true);   // DC-3s and P-51s cruise with the gear up
            int i = d.Id - 100;
            d.Go = new GameObject($"Drone{i}-{d.Kind}");
            d.Builder = new AirframeBuilder { Paint = DroneOrange };
            d.Builder.Build(d.Go.transform, cfg);
            d.Fx = new DamageFx(d.Go.transform, cfg);
            d.Target = new GunTarget { Id = d.Id, Config = cfg, Volumes = HitVolumes.Build(cfg), State = () => d.Aircraft.State, Lost = d.Aircraft.IsLost, Damage = d.Aircraft.Damage };
            d.Dead = false; d.DeadT = 0f;
            Drone dd = d;
            d.Aircraft.ComponentLost += c => { if (dd.Go != null) AirframeVisual.DetachStatic(dd.Builder, dd.Go.transform, dd.Config, c, WorldVel(dd.Aircraft)); };
        }

        /// <summary>The player as a gun target (the fighting-back P-51 shoots at him): same hit volumes and damage as a drone.
        /// Id 0 = the player's own shooter id, so his own rounds never hit himself.</summary>
        private GunTarget PlayerTarget()
        {
            Aircraft a = Driver?.Sim?.Aircraft;
            if (a == null) return null;
            if (!ReferenceEquals(a, _playerTargetFor))
            {
                _playerTargetFor = a;
                _playerTarget = new GunTarget { Id = 0, Config = a.Config, Volumes = HitVolumes.Build(a.Config), State = () => a.State, Lost = a.IsLost, Damage = a.Damage };
            }
            return _playerTarget;
        }

        /// <summary>Once per frame: who leads each formation, who flies which slot, and whether a P-51 is attacking.</summary>
        private void AssignFlights(bool playerFair)
        {
            foreach (Flight f in _flights)
            {
                if (f.Attacker != null && (f.Attacker.Dead || !playerFair))
                {
                    if (!f.Attacker.Dead) f.Attacker.Kind = f.Kind;   // back to the formation
                    f.Attacker = null;
                }
                if (f.CanFightBack && f.Provoked && f.Attacker == null && playerFair)
                {
                    foreach (Drone m in f.Members)
                    {
                        if (m.Dead) continue;
                        f.Attacker = m;
                        m.Ap = new AttackPilot(f.Seed + m.Id);
                        m.Kind = f.Kind + " ATTACKING";
                        Flash($"{f.Kind} BREAKING TOWARD YOU");
                        break;
                    }
                }
                int k = 0; f.Lead = null;
                foreach (Drone m in f.Members)
                {
                    if (m.Dead || m == f.Attacker) continue;
                    if (k == 0) f.Lead = m;
                    else if (k - 1 < f.Slots.Length) m.Fp.Slot = f.Slots[k - 1];
                    k++;
                }
            }
        }

        private static double DistanceToZone(Vec3 p)
        {
            double dx = System.Math.Max(0, System.Math.Max(CombatZone.X0 - p.X, p.X - CombatZone.X1));
            double dy = System.Math.Max(0, System.Math.Max(CombatZone.Y0 - p.Y, p.Y - CombatZone.Y1));
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        private static Vector3 WorldVel(Aircraft a) => CoordinateMap.ToUnity(a.State.Attitude.Rotate(a.State.Velocity));

        private void Update()
        {
            if (Driver?.Sim == null) return;
            var ac = Driver.Sim.Aircraft;
            InZone = CombatZone.Inside(ac.State.Position);
            float dt = Time.deltaTime;
            _accum += dt;
            bool firing = Firing && GunsHot && !SessionSettings.MenuOpen && !(GetComponent<PilotEgress>()?.PilotOut ?? false);
            _audio?.SetGuns(firing, ac.Guns != null ? (float)ac.Guns.RateHz : 0f);
            bool pilotOut = GetComponent<PilotEgress>()?.PilotOut ?? false;
            bool active = DistanceToZone(ac.State.Position) < ActiveRangeM;   // far away: the drones wait (CPU)
            AssignFlights(InZone && !pilotOut);
            while (_accum >= SimLoop.DefaultFixedDtSec)
            {
                double h = SimLoop.DefaultFixedDtSec;
                if (firing) Gunnery.Fire(0, ac.Guns, ac.Config, ac.State, h);
                foreach (Drone d in _drones)
                {
                    if (d.Aircraft == null || !active) continue;
                    ControlInputs ci;
                    if (d.Dead) ci = new ControlInputs(0, 0, 0, 1.0);
                    else if (d.Flight != null && d.Flight.Attacker == d)
                    {
                        ci = d.Ap.Update(d.Aircraft, ac.State, h);
                        if (d.Ap.Firing && d.Aircraft.Guns != null && d.Aircraft.Guns.RoundsLeft > 0) Gunnery.Fire(d.Id, d.Aircraft.Guns, d.Config, d.Aircraft.State, h);
                    }
                    else if (d.Flight != null && d.Flight.Lead != null && d.Flight.Lead != d) ci = d.Fp.Update(d.Aircraft, d.Flight.Lead.Aircraft, h);
                    else ci = d.Pilot.Update(d.Aircraft, h);
                    new SimLoop(d.Aircraft).RunFor(h, ci);
                }
                _stepTargets.Clear(); _stepTargets.AddRange(_targets);
                GunTarget me = PlayerTarget(); if (me != null) _stepTargets.Add(me);
                Gunnery.Step(h, _stepTargets, GroundTargets);
                foreach (HitEvent hit in Gunnery.Hits)
                {
                    Vector3 at = CoordinateMap.ToUnity(hit.Pos);
                    if (hit.Target == 0) { HitsTaken++; _audio?.BulletHit(1f); Flash("YOU'RE HIT"); Puff(_puffHit, at, 8); }
                    else if (hit.Target >= 100)
                    {
                        HitsOnDrones++; _audio?.BulletHit(0.6f); Flash($"HIT  {hit.Part}"); Puff(_puffHit, at, 6);
                        if (hit.Shooter == 0) foreach (Drone x in _drones) if (x.Id == hit.Target && x.Flight != null) x.Flight.Provoked = true;   // you shot at them
                    }
                    else if (hit.GroundTarget >= 0) { GroundHits++; _audio?.BulletHit(0.4f); Flash("TARGET HIT"); Puff(_puffGround, at, 10); }
                    else Puff(_puffGround, at, 7);   // plain ground: a spurt of dust where the round landed
                }
                _accum -= h;
            }
            foreach (Drone d in _drones)
            {
                if (d.Aircraft == null || d.Go == null) continue;
                d.Go.transform.SetPositionAndRotation(CoordinateMap.ToUnity(d.Aircraft.State.Position), CoordinateMap.ToUnity(d.Aircraft.State.Attitude));
                d.Fx.Update(d.Aircraft.Damage, WorldVel(d.Aircraft));
                var st = d.Aircraft.State;
                double agl = -st.Position.Z - WorldTerrain.GroundHeightAt(st.Position.X, st.Position.Y);
                bool crippled = d.Aircraft.IsLost(AirframeComponent.WingLeftInner) || d.Aircraft.IsLost(AirframeComponent.WingRightInner) || d.Aircraft.IsLost(AirframeComponent.TailBoom);
                if (!d.Dead && (crippled || (agl < 2 && st.Velocity.Length > 5)))
                {
                    d.Dead = true; DronesDown++; _audio?.Explosion(); Flash("DRONE DOWN");
                }
                if (d.Dead) { d.DeadT += dt; if (d.Flight == null && d.DeadT > RespawnSec) SpawnDrone(_drones.IndexOf(d)); }
            }
            // A formation comes back as a whole once every member has been down for a while.
            foreach (Flight f in _flights)
            {
                bool allDown = true;
                foreach (Drone m in f.Members) if (!m.Dead || m.DeadT <= RespawnSec) { allDown = false; break; }
                if (allDown) SpawnFlight(f);
            }
            if (_flashT > 0f) _flashT -= dt;
        }

        private void Flash(string s) { _flash = s; _flashT = 1.5f; }

        // ---- impact puffs (owner): dust where a round hits the ground, a grey-orange puff where it hits an aircraft
        private void BuildPuffs()
        {
            _puffGround = MakePuff("ImpactDust", new Color(0.62f, 0.52f, 0.36f, 0.85f), 1.2f, 1.4f);
            _puffHit = MakePuff("ImpactSpark", new Color(0.85f, 0.75f, 0.6f, 0.9f), 0.7f, 0.6f);
        }

        private static ParticleSystem MakePuff(string name, Color c, float size, float life)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = life; main.startSize = size;
            main.startSpeed = 1.5f; main.gravityModifier = 0.05f; main.maxParticles = 800; main.loop = false; main.playOnAwake = false; main.startColor = c;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.2f)));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = new Material(Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = c };
            r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        private static void Puff(ParticleSystem ps, Vector3 at, int count)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { position = at + Vector3.up * 0.2f, applyShapeToPosition = true };
            ps.Emit(ep, count);
        }

        /// <summary>HUD line: state of the guns and the score.</summary>
        public string Line
        {
            get
            {
                var ac = Driver?.Sim?.Aircraft;
                if (ac?.Guns == null) return null;
                if (!InZone) return "GUNS COLD  fly to the COMBAT ZONE (north-east, past the lakes)";
                string flash = _flashT > 0f ? $"   {_flash}" : "";
                // Nearest live drone: range and clock position so it can be found.
                string nearest = "";
                double best = double.MaxValue; Drone bd = null;
                foreach (Drone d in _drones) { if (d.Dead || d.Aircraft == null) continue; double r = (d.Aircraft.State.Position - ac.State.Position).Length; if (r < best) { best = r; bd = d; } }
                if (bd != null)
                {
                    Vec3 rel = bd.Aircraft.State.Position - ac.State.Position;
                    Vec3 relBody = ac.State.Attitude.Conjugate().Rotate(rel);
                    double bearing = System.Math.Atan2(relBody.Y, relBody.X) * 180 / System.Math.PI;
                    int clock = ((int)System.Math.Round(bearing / 30.0) + 12) % 12; if (clock == 0) clock = 12;
                    string hiLo = relBody.Z < -150 ? " high" : relBody.Z > 150 ? " low" : "";
                    nearest = $"   {bd.Kind} {best / 1000:F1} km {clock} o'clock{hiLo}";
                }
                string taken = HitsTaken > 0 ? $"   hits taken {HitsTaken}" : "";
                return $"COMBAT ZONE  guns HOT  rounds {ac.Guns.RoundsLeft}   drones down {DronesDown}   hits {HitsOnDrones}{taken}{nearest}{flash}";
            }
        }

        /// <summary>Self-test: act as if the player had just hit the P-51 formation.</summary>
        public void DebugProvoke() { foreach (Flight f in _flights) if (f.CanFightBack) f.Provoked = true; }

        /// <summary>Self-test: one line per formation — members alive, who leads, who attacks.</summary>
        public string DebugFlights()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Flight f in _flights)
            {
                int alive = 0; foreach (Drone m in f.Members) if (!m.Dead) alive++;
                string range = f.Attacker != null && Driver?.Sim != null ? $" attacker at {(f.Attacker.Aircraft.State.Position - Driver.Sim.Aircraft.State.Position).Length:F0} m" : "";
                sb.Append($"{f.Kind} flight {alive}/{f.Members.Count} alive, provoked {f.Provoked}{range}; ");
            }
            return sb.ToString();
        }

        public void ResetScore()
        {
            DronesDown = 0; GroundHits = 0; HitsOnDrones = 0; HitsTaken = 0;
            Driver?.Sim?.Aircraft?.Guns?.Reload();
            Gunnery.Clear();
        }
    }
}

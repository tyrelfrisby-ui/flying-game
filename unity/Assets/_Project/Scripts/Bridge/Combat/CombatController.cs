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
        /// <summary>Owner 2026-09-12: four fast aerobatic Cassutts flying random manoeuvres and two DC-3s lumbering round.</summary>
        public const int CassuttCount = 4, Dc3Count = 2;
        public const float RespawnSec = 8f;
        public static readonly Color DroneOrange = new(1f, 0.45f, 0.05f);

        private sealed class Drone
        {
            public int Id; public Aircraft Aircraft; public DronePilot Pilot; public GameObject Go; public AirframeBuilder Builder; public DamageFx Fx;
            public GunTarget Target; public bool Dead; public float DeadT; public AircraftConfig Config; public bool Aerobatic; public string Kind;
        }
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
        }

        private void SpawnDrone(int i)
        {
            bool aerobatic = i < CassuttCount;
            AircraftConfig cfg = aerobatic ? _cassuttCfg : _dc3Cfg;
            var d = _drones.Count > i ? _drones[i] : null;
            if (d == null) { d = new Drone { Id = 100 + i }; _drones.Add(d); }
            d.Config = cfg; d.Aerobatic = aerobatic; d.Kind = aerobatic ? "CASSUTT" : "DC-3";
            if (d.Go != null) Destroy(d.Go);
            d.Pilot = new DronePilot(_seedBase + i * 7919 + Random.Range(0, 1000), aerobatic, aerobatic ? 65.0 : 70.0);
            Vec3 wp = d.Pilot.Waypoints[(d.Pilot.Next + 3) % d.Pilot.Waypoints.Count];
            double ground = WorldTerrain.GroundHeightAt(wp.X, wp.Y);
            var start = new Vec3(wp.X, wp.Y, -(ground + wp.Z));
            double hdg = Random.Range(0f, 6.28f);
            var att = new Quat(0, 0, System.Math.Sin(hdg / 2), System.Math.Cos(hdg / 2));
            d.Aircraft = new Aircraft(cfg, new RigidBodyState(start, att, new Vec3(d.Pilot.CruiseMs, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            if (!aerobatic) d.Aircraft.SetGear(false, immediate: true);   // the DC-3 cruises with its gear up
            d.Go = new GameObject($"Drone{i}-{d.Kind}");
            d.Builder = new AirframeBuilder { Paint = DroneOrange };
            d.Builder.Build(d.Go.transform, cfg);
            d.Fx = new DamageFx(d.Go.transform, cfg);
            d.Target = new GunTarget { Id = d.Id, Config = cfg, Volumes = HitVolumes.Build(cfg), State = () => d.Aircraft.State, Lost = d.Aircraft.IsLost, Damage = d.Aircraft.Damage };
            d.Dead = false; d.DeadT = 0f;
            Drone dd = d;
            d.Aircraft.ComponentLost += c => { if (dd.Go != null) AirframeVisual.DetachStatic(dd.Builder, dd.Go.transform, dd.Config, c, WorldVel(dd.Aircraft)); };
            _targets.Clear(); foreach (Drone x in _drones) _targets.Add(x.Target);
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
            while (_accum >= SimLoop.DefaultFixedDtSec)
            {
                double h = SimLoop.DefaultFixedDtSec;
                if (firing) Gunnery.Fire(0, ac.Guns, ac.Config, ac.State, h);
                foreach (Drone d in _drones)
                {
                    if (d.Aircraft == null) continue;
                    ControlInputs ci = d.Dead ? new ControlInputs(0, 0, 0, 1.0) : d.Pilot.Update(d.Aircraft, h);
                    new SimLoop(d.Aircraft).RunFor(h, ci);
                }
                Gunnery.Step(h, _targets, GroundTargets);
                foreach (HitEvent hit in Gunnery.Hits)
                {
                    Vector3 at = CoordinateMap.ToUnity(hit.Pos);
                    if (hit.Target >= 100) { HitsOnDrones++; _audio?.BulletHit(0.6f); Flash($"HIT  {hit.Part}"); Puff(_puffHit, at, 6); }
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
                if (d.Dead) { d.DeadT += dt; if (d.DeadT > RespawnSec) SpawnDrone(_drones.IndexOf(d)); }
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
                return $"COMBAT ZONE  guns HOT  rounds {ac.Guns.RoundsLeft}   drones down {DronesDown}   hits {HitsOnDrones}{nearest}{flash}";
            }
        }

        public void ResetScore()
        {
            DronesDown = 0; GroundHits = 0; HitsOnDrones = 0;
            Driver?.Sim?.Aircraft?.Guns?.Reload();
            Gunnery.Clear();
        }
    }
}

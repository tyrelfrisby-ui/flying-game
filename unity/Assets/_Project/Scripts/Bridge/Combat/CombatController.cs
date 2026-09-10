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
        public const int DroneCount = 3;
        public const float RespawnSec = 8f;

        private sealed class Drone
        {
            public int Id; public Aircraft Aircraft; public DronePilot Pilot; public GameObject Go; public AirframeBuilder Builder; public DamageFx Fx;
            public GunTarget Target; public bool Dead; public float DeadT; public AircraftConfig Config;
        }
        private readonly List<Drone> _drones = new();
        private readonly List<GunTarget> _targets = new();
        private AircraftConfig _droneCfg;
        private double _accum;
        private FlightAudio _audio;
        private string _flash; private float _flashT;

        private void Start()
        {
            _audio = GetComponent<FlightAudio>();
            _droneCfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("target-drone-like");
            for (int i = 0; i < DroneCount; i++) SpawnDrone(i);
        }

        private void SpawnDrone(int i)
        {
            var d = _drones.Count > i ? _drones[i] : null;
            if (d == null) { d = new Drone { Id = 100 + i, Config = _droneCfg }; _drones.Add(d); }
            if (d.Go != null) Destroy(d.Go);
            d.Pilot = new DronePilot(17 + i * 31);
            Vec3 wp = d.Pilot.Waypoints[(d.Pilot.Next + 3) % d.Pilot.Waypoints.Count];
            double ground = WorldTerrain.GroundHeightAt(wp.X, wp.Y);
            var start = new Vec3(wp.X, wp.Y, -(ground + wp.Z));
            double hdg = Random.Range(0f, 6.28f);
            var att = new Quat(0, 0, System.Math.Sin(hdg / 2), System.Math.Cos(hdg / 2));
            d.Aircraft = new Aircraft(_droneCfg, new RigidBodyState(start, att, new Vec3(d.Pilot.CruiseMs, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            d.Go = new GameObject($"Drone{i}");
            d.Builder = new AirframeBuilder { Paint = new Color(1f, 0.45f, 0.05f) };
            d.Builder.Build(d.Go.transform, _droneCfg);
            d.Fx = new DamageFx(d.Go.transform, _droneCfg);
            d.Target = new GunTarget { Id = d.Id, Config = _droneCfg, Volumes = HitVolumes.Build(_droneCfg), State = () => d.Aircraft.State, Lost = d.Aircraft.IsLost, Damage = d.Aircraft.Damage };
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
                    if (hit.Target >= 100) { HitsOnDrones++; _audio?.BulletHit(0.6f); Flash($"HIT  {hit.Part}"); }
                    else if (hit.GroundTarget >= 0) { GroundHits++; _audio?.BulletHit(0.4f); Flash("TARGET HIT"); }
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

        /// <summary>HUD line: state of the guns and the score.</summary>
        public string Line
        {
            get
            {
                var ac = Driver?.Sim?.Aircraft;
                if (ac?.Guns == null) return null;
                if (!InZone) return "GUNS COLD  fly to the COMBAT ZONE (north-east, past the lakes)";
                string flash = _flashT > 0f ? $"   {_flash}" : "";
                return $"COMBAT ZONE  guns HOT  rounds {ac.Guns.RoundsLeft}   drones down {DronesDown}   hits {HitsOnDrones}   targets {GroundHits}{flash}";
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

using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// SMOKE in the game (owner 2026-10-31: "I want smoke and I want it as a full time button like gear or water
    /// rudders or flaps not in a menu").
    ///
    /// All the hard parts already existed in <see cref="SmokeTrail"/> — the exhaust positions per type, the airframe
    /// flow that carries each puff AROUND the aeroplane rather than through it, the jet that throws it clear of the
    /// stack for the first few metres, a puff's own 10-30 s life — but only the Aero Widget ever drove it. Nothing in
    /// the flight scene did. This is the missing half: the history the trail is rebuilt from each frame, and a switch.
    ///
    /// The trail is rebuilt every frame from a ring of samples, newest first, which is the contract
    /// <see cref="SmokeTrail.Build"/> wants. The switch only gates the FLOW recorded into each sample, so turning
    /// smoke off leaves what is already in the air to drift and fade for its own lifetime instead of vanishing —
    /// which is what happens when a real pilot shuts the oil off.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class SmokeController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public AirframeVisual Visual;

        /// <summary>The smoke oil is on. Off at every spawn: a flight does not begin trailing smoke.</summary>
        public bool On { get; private set; }

        /// <summary>Whether this aeroplane can make smoke at all — an engine to carry the oil, or a glider's wingtip
        /// canisters. Everything in the fleet qualifies; the check is here so the button can hide if one ever does not.</summary
        public bool Available => Driver?.Sim?.Aircraft != null && _exhausts != null && _exhausts.Length > 0;

        public void Toggle() => On = !On;
        public void SetOn(bool on) => On = on;

        /// <summary>The oil's flow now: none with the engine stopped, more with power, because the exhaust carries it.
        /// A glider's canisters are always full. Same law the widget uses.</summary>
        public float Flow
        {
            get
            {
                FlyingGame.Sim.Aircraft ac = Driver?.Sim?.Aircraft;
                if (!On || ac == null) return 0f;
                if (ac.Config.Propulsion == null) return 1f;                  // wingtip canisters
                if (!ac.EngineRunning || ac.EngineRpm < 100) return 0f;       // no exhaust (a windmilling dead engine has none)
                return (float)(0.25 + 0.75 * Mathf.Clamp01((float)ac.ShaftPower01));
            }
        }

        private SmokeTrail _smoke;
        private GameObject _root;          // scene root, identity transform: the trail is world-space
        private Vec3[] _exhausts, _jet;
        private FlyingGame.Sim.Aircraft _builtFor;
        private readonly List<SmokeTrail.Sample> _hist = new();
        private Camera _cam;

        private void Start()
        {
            if (Driver == null) Driver = GetComponent<FlightSimDriver>();
            if (Visual == null) Visual = GetComponent<AirframeVisual>();
            Driver.AircraftChanged += OnAircraftChanged;
        }

        private void OnDestroy()
        {
            if (Driver != null) Driver.AircraftChanged -= OnAircraftChanged;
            if (_root != null) Destroy(_root);
        }

        /// <summary>A new aeroplane, or a reset: different exhausts, and the smoke must never join one flight's path to
        /// the next. The switch goes off too, so FLY never starts smoking.</summary>
        private void OnAircraftChanged()
        {
            On = false;
            _hist.Clear();
            _builtFor = null;
            _smoke?.Hide();
        }

        private void Rebuild(FlyingGame.Sim.Aircraft ac)
        {
            _builtFor = ac;
            _hist.Clear();
            AircraftConfig cfg = ac.Config;
            // The trail's mesh is built in WORLD coordinates (shown = CoordinateMap.ToUnity below), so its GameObject
            // must hang off something with an identity transform. Parenting it to THIS transform — the aeroplane —
            // transformed every vertex a second time by the aeroplane's own position and attitude, which put the
            // smoke far off screen and rotating with the airframe: the trail was being drawn, just nowhere a camera
            // was looking. (The Aero Widget passes its own transform and gets away with it because the widget host
            // sits at the origin and its sample mapping is widget-local, not world.)
            if (_root == null) _root = new GameObject("SmokeRoot");
            _smoke ??= new SmokeTrail(_root.transform, gameObject.layer);

            float lengthM = 7f, bodyRadiusM = 0.5f;
            if (cfg.Fuselage?.Crossflow != null)
            {
                if (cfg.Fuselage.Crossflow.LengthM > 0) lengthM = (float)cfg.Fuselage.Crossflow.LengthM;
                if (cfg.Fuselage.Crossflow.BodyRadiusM > 0) bodyRadiusM = (float)cfg.Fuselage.Crossflow.BodyRadiusM;
            }
            _exhausts = SmokeTrail.ExhaustsFor(cfg, Driver.AircraftId, PropHubs(), lengthM, bodyRadiusM, out _jet);
            _smoke.SetExhausts(_exhausts, _jet);
            _smoke.Flow = new FlyingGame.Core.Aero.AirframeFlow(cfg, 0.4 * lengthM, bodyRadiusM);
            _smoke.PipeDiameterM = (cfg.Smoke?.PipeDiameterIn ?? 4) * 0.0254;
            _smoke.Stream = cfg.Propulsion == null;                           // a glider's canisters stream, they do not puff
        }

        /// <summary>The propeller hubs, for the exhausts that sit ahead of a tractor's disc. The visual knows them;
        /// without it the config's thrust line is close enough for the smoke.</summary>
        private IReadOnlyList<Vector3> PropHubs()
        {
            if (Visual != null)
            {
                IReadOnlyList<Vector3> hubs = Visual.PropHubsBody();
                if (hubs != null && hubs.Count > 0) return hubs;
            }
            return System.Array.Empty<Vector3>();
        }

        private double JetU0(FlyingGame.Sim.Aircraft ac, double altM)
        {
            PropulsionConfig pr = ac.Config.Propulsion;
            if (pr == null) return 0;
            if (pr.PropDiameterM <= 0) return 25.0 * Mathf.Clamp01((float)ac.Throttle01);
            SmokeConfig sm = ac.Config.Smoke;
            double cuIn = sm != null && sm.DisplacementCuIn > 0 ? sm.DisplacementCuIn : pr.MaxPowerW / 745.7 / 0.556;
            return FlyingGame.Core.Aero.ExhaustJet.ExitVelocity(cuIn * 16.387e-6, ac.EngineRunning ? ac.EngineRpm : 0.0, ac.ShaftPower01,
                Atmosphere.DensityAtAltitude(altM), (sm?.PipeDiameterIn ?? 4) * 0.0254, sm?.ExhaustTempK ?? 900);
        }

        // LateUpdate, so the sample is the pose the camera is about to see rather than one frame behind it.
        private void LateUpdate()
        {
            FlyingGame.Sim.Aircraft ac = Driver?.Sim?.Aircraft;
            if (ac == null) { _smoke?.Hide(); return; }
            if (!ReferenceEquals(ac, _builtFor)) Rebuild(ac);
            if (Driver.Replaying) return;                                     // the replay owns the pose; FlightReplay redraws

            double now = Atmosphere.SimTimeSec;
            double altM = -ac.State.Position.Z;
            _hist.Add(new SmokeTrail.Sample
            {
                T = now, Pos = ac.State.Position, Att = ac.State.Attitude,
                VelBody = ac.State.Velocity, Rates = ac.State.Rates, Flow = Flow,
                JetU0 = JetU0(ac, altM), AirDensity = Atmosphere.DensityAtAltitude(altM),
            });

            // Drop what has aged out, oldest first. The ring is about 30 s at the frame rate - a couple of thousand
            // samples - so this stays bounded however long the flight is.
            int drop = 0;
            while (drop < _hist.Count && now - _hist[drop].T > SmokeTrail.LifeMaxS) drop++;
            if (drop > 0) _hist.RemoveRange(0, drop);

            // Nothing in the air and the switch off: do not walk the ring every frame for puffs that cannot exist.
            if (!On && !AnyFlow()) { _smoke.Hide(); return; }

            if (_cam == null) _cam = Camera.main;
            _smoke.Build(k =>
            {
                int i = _hist.Count - 1 - k;                                  // newest first, which is Build's contract
                return i < 0 ? (SmokeTrail.Sample?)null : _hist[i];
            }, now, p => CoordinateMap.ToUnity(p), _cam);
        }

        private bool AnyFlow()
        {
            for (int i = _hist.Count - 1; i >= 0; i--) if (_hist[i].Flow > 0.001f) return true;
            return false;
        }
    }
}

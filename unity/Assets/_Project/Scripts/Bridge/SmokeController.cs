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
        /// A glider's canisters are always full. Same law the widget uses. The oil pump is electric: with the master
        /// off or a flat battery (Aircraft.Electrics) there is no smoke, whatever the switch says.
        /// (<see cref="FlyingGame.Sim.Replay.ReplaySmoke.FlowIfOnNow"/>: the recorder keeps the same number.)</summary>
        public float Flow => On ? FlyingGame.Sim.Replay.ReplaySmoke.FlowIfOnNow(Driver?.Sim?.Aircraft) : 0f;

        /// <summary>This moment's smoke for the replay recorder (Ty t912u): the switch, the flow it makes or would make,
        /// the jet and the crank, so the replay draws the trail as it was flown and can add or mute it after the fact.</summary>
        public FlyingGame.Sim.Replay.ReplaySmoke ReplayChannel()
        {
            FlyingGame.Sim.Aircraft ac = Driver?.Sim?.Aircraft;
            return ac == null ? default : FlyingGame.Sim.Replay.ReplaySmoke.Capture(ac, On, _crank);
        }

        private SmokeTrail _smoke;
        private GameObject _root;          // scene root, identity transform: the trail is world-space
        private Vec3[] _exhausts, _jet;
        private FlyingGame.Sim.Aircraft _builtFor;
        private readonly List<SmokeTrail.Sample> _hist = new();
        private Camera _cam;
        private double _thinnedT = double.NegativeInfinity;   // when the ring was last thinned (SmokeTrail.ThinRing)
        private double _crank, _crankT = double.NaN;   // the crank's revolutions, integrated from the live rpm: times the exhaust pulses

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
            _smoke.Cylinders = cfg.Smoke?.Cylinders ?? 6;
            _crank = 0; _crankT = double.NaN;
            _measuredModel = _triedModel = null; _measureTries = 0;
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

        private static double JetU0(FlyingGame.Sim.Aircraft ac, double altM) => FlyingGame.Sim.Replay.ReplaySmoke.JetU0Now(ac, Atmosphere.DensityAtAltitude(altM));

        // LateUpdate, so the sample is the pose the camera is about to see rather than one frame behind it.
        private void LateUpdate()
        {
            FlyingGame.Sim.Aircraft ac = Driver?.Sim?.Aircraft;
            if (ac == null) { _smoke?.Hide(); return; }
            if (!ReferenceEquals(ac, _builtFor)) Rebuild(ac);
            ac.Electrics.SmokePump = On && ac.Config.Propulsion != null;     // the pump's load on the bus
            MeasureAirframe();
            if (Driver.Replaying) { BuildReplay(); return; }                 // the replay owns the pose and the trail's samples

            double now = Atmosphere.SimTimeSec;
            double altM = -ac.State.Position.Z;
            if (!double.IsNaN(_crankT) && now > _crankT) _crank += ac.EngineRpm / 60.0 * (now - _crankT);
            _crankT = now;
            _hist.Add(new SmokeTrail.Sample
            {
                T = now, Pos = ac.State.Position, Att = ac.State.Attitude,
                VelBody = ac.State.Velocity, Rates = ac.State.Rates, Flow = Flow,
                JetU0 = JetU0(ac, altM), AirDensity = Atmosphere.DensityAtAltitude(altM),
                Crank = _crank, Rpm = ac.Config.Propulsion != null && ac.Config.Propulsion.PropDiameterM > 0 ? (float)ac.EngineRpm : 0f,   // (a jet has no pulses)
            });

            // Drop what has aged out, oldest first. The ring is 90 s (SmokeTrail.LifeMaxS), so this stays bounded however
            // long the flight is; and once a second the samples the trail would skip are dropped (SmokeTrail.ThinRing:
            // past 2 s it draws one per 1/30 s), which keeps it to ~2,700 instead of 8,100 at the Quest's 90 Hz.
            int drop = 0;
            while (drop < _hist.Count && now - _hist[drop].T > SmokeTrail.LifeMaxS) drop++;
            if (drop > 0) _hist.RemoveRange(0, drop);
            if (System.Math.Abs(now - _thinnedT) > 1.0) { _thinnedT = now; SmokeTrail.ThinRing(_hist, now); }

            // Nothing in the air and the switch off: do not walk the ring every frame for puffs that cannot exist.
            if (!On && !AnyFlow()) { _smoke.Hide(); return; }

            if (_cam == null) _cam = Camera.main;
            _smoke.Build(k =>
            {
                int i = _hist.Count - 1 - k;                                  // newest first, which is Build's contract
                return i < 0 ? (SmokeTrail.Sample?)null : _hist[i];
            }, now, p => CoordinateMap.ToUnity(p), _cam);
        }

        // ---- the replay's trail (Ty t912u) ----
        private FlightReplay _replay;
        private readonly List<FlyingGame.Sim.Replay.ReplaySmokePoint> _replayPts = new();
        private readonly List<SmokeTrail.Sample> _replaySamples = new();

        /// <summary>In a replay, the trail is rebuilt at the play head from the RECORDING (FlightReplay.Recorder), in the mode
        /// the replay bar has chosen (as flown / on / off). The live ring (_hist) is left exactly as it was, so the flight's
        /// own trail carries on when the replay ends. The clip recorders copy this same camera's view, so a clip made from
        /// the replay shows the same smoke.</summary>
        private void BuildReplay()
        {
            if (_replay == null) _replay = GetComponent<FlightReplay>();
            FlyingGame.Sim.Aircraft ac = Driver.Sim.Aircraft;
            if (_replay == null || !_replay.Active
                || !FlyingGame.Sim.Replay.ReplaySmokeView.Window(_replay.Recorder, _replay.Head, _replay.SmokeMode, ac.Config.Propulsion == null, SmokeTrail.LifeMaxS, _replayPts))
            { _smoke.Hide(); return; }
            _replaySamples.Clear();
            foreach (var p in _replayPts)
                _replaySamples.Add(new SmokeTrail.Sample { T = p.T, Pos = p.Pos, Att = p.Att, VelBody = p.VelBody, Rates = p.Rates, Flow = p.Flow,
                    JetU0 = p.JetU0, AirDensity = p.AirDensity > 0 ? p.AirDensity : Atmosphere.DensityAtAltitude(-p.Pos.Z), Crank = p.Crank, Rpm = p.Rpm });
            if (_cam == null) _cam = Camera.main;
            double now = _replayPts[0].T;
            _smoke.Build(k => k < _replaySamples.Count ? _replaySamples[k] : (SmokeTrail.Sample?)null, now, p => CoordinateMap.ToUnity(p), _cam);
        }

        // ---- the airframe the smoke keeps out of (Ty t914u: every aeroplane, every part) ----
        private Transform _measuredModel, _triedModel;
        private int _measureTries;

        /// <summary>Once the model is placed, measure its fuselage (AirframeFlow.MeasureFuselage) so the smoke goes round the
        /// body that is DRAWN, not an analytic tube of the config's radius. The model is placed after the aircraft is built,
        /// so this waits for it; a model whose meshes cannot be read keeps the analytic shape (and says so once).</summary>
        private void MeasureAirframe()
        {
            if (_smoke?.Flow == null || Visual == null) return;
            Transform model = Visual.ModelRoot != null ? Visual.ModelRoot : Visual.transform;
            if (ReferenceEquals(model, _measuredModel)) return;
            if (!ReferenceEquals(model, _triedModel)) { _triedModel = model; _measureTries = 0; }
            if (_measureTries > 3) return;
            _measureTries++;
            var hubs = new List<Transform>();
            for (int i = 0; ; i++) { Transform h = Visual.PropHubTransform(i); if (h == null) break; hubs.Add(h); }
            List<Vec3> pts = ModelPoints(transform, model, hubs);
            if (FlyingGame.Core.Aero.AirframeFlow.MeasureFuselage(pts, Driver.Sim.Aircraft.Config, out double[] xs, out double[] rs, out double[] zs))
            {
                _smoke.Flow.SetFuselage(xs, rs, zs);
                _measuredModel = model;
                double rmax = 0; foreach (double r in rs) rmax = System.Math.Max(rmax, r);
                Debug.Log($"[Smoke] {Driver.AircraftId}: fuselage measured on the drawn model, {xs.Length} stations x {xs[0]:0.00}..{xs[^1]:0.00} m, max radius {rmax:0.00} m, reach {_smoke.Flow.Reach:0.0} m");
            }
            else if (_measureTries > 3) Debug.Log($"[Smoke] {Driver.AircraftId}: model not readable ({pts.Count} points): smoke keeps out of the analytic body");
        }

        /// <summary>The drawn model's vertices in body axes from the CG (<paramref name="aircraft"/> is the transform at the
        /// CG, rotated to the body by CoordinateMap), up to ~40,000 of them: every enabled, readable mesh under
        /// <paramref name="model"/> except the propellers (under a hub, or named prop / blade / disc) and anything named smoke.
        /// Names are read here, once per model, never per frame.</summary>
        public static List<Vec3> ModelPoints(Transform aircraft, Transform model, IReadOnlyList<Transform> propHubs)
        {
            var pts = new List<Vec3>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(false))
            {
                Mesh m = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>();
                if (m == null || !m.isReadable || mr == null || !mr.enabled) continue;
                bool prop = false;
                for (Transform t = mf.transform; t != null && t != aircraft && !prop; t = t.parent)
                {
                    if (propHubs != null) foreach (var h in propHubs) if (t == h) prop = true;
                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("prop") || n.Contains("blade") || n.Contains("disc") || n.Contains("smoke")) prop = true;
                }
                if (prop) continue;
                var vs = m.vertices; var tf = mf.transform;
                int stride = System.Math.Max(1, vs.Length / 40000);
                for (int i = 0; i < vs.Length; i += stride)
                {
                    Vector3 u = aircraft.InverseTransformPoint(tf.TransformPoint(vs[i]));
                    pts.Add(new Vec3(u.z, u.x, -u.y));   // Unity local → body (x fwd, y right, z down)
                }
            }
            return pts;
        }

        private bool AnyFlow()
        {
            for (int i = _hist.Count - 1; i >= 0; i--) if (_hist[i].Flow > 0.001f) return true;
            return false;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Procedural flight sound (no sample assets). WIND from airspeed (filtered noise, rises with IAS,
    /// gusts with turbulence, whistle with AoA/beta, stall buffet, spoiler roar); ENGINE per type from
    /// FlightSimDriver.EngineRpm / Throttle01 — flat-4/6 Lycoming, 7/14-cyl radials, the P-51's MERLIN V-12
    /// (6 pulses/rev, two-stage supercharger whine, 4-blade prop at 0.479 reduction), jet (F-86 axial,
    /// 737 turbofan); gliders are wind only. One-shots below are triggered by the other systems.
    ///
    /// Threading: Update copies driver values into the volatile <see cref="AudioTelemetry"/> mailbox; the
    /// audio thread (FlightAudioOutput.OnAudioFilterRead → <see cref="Render"/>) reads them and renders
    /// with zero allocation. Aircraft switches build a new EngineSynth on the main thread and swap the
    /// reference. SessionSettings.MenuOpen ducks everything to silence.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class FlightAudio : MonoBehaviour
    {
        /// <summary>Master volume 0..1 (menu slider, persisted).</summary>
        public static float MasterVolume = 1f;
        /// <summary>LOCAL 2-PLAYER: this aeroplane's share of the one mix (both engines are heard through the one
        /// listener; LocalCoop lowers each a little so the two together are not louder than one was). 1 = as always.</summary>
        public float MixGain = 1f;
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void FlyingGame_SetPlaybackAudioSession();
#endif

        private const string PrefKey = "audio.masterVolume";
        private const float EngineMix = 0.55f, WindMix = 1f, FxMix = 0.8f;
        /// <summary>Blade strikes are ground contact: they ride the ground bus (GroundSoundMix, Ty 2026-11-05 −4 dB).</summary>
        private static float PropStrikeMix => FlyingGame.Core.GroundSoundMix.OutputGain;
        private const float GroanHoldS = 0.2f;   // StructuralGroan is per-frame; hold bridges frame gaps

        private FlightSimDriver _driver;
        private AudioSource _source;
        private AudioClip _clip;
        private readonly AudioTelemetry _tele = new AudioTelemetry();
        private WindSynth _wind;
        private SurfaceSynth _surface;
        private TyreBrakeSynth _tyres;
        private FlyingGame.Core.GroundSoundMix _groundMix;   // main thread
        private readonly int[] _chirpSeenUs = new int[2];      // audio thread
        private readonly float[] _chirpNewS = new float[2];    // audio thread
        private readonly double[] _chirpTotalS = new double[2]; // main thread
        private readonly int[] _scrubSeenUs = new int[2];      // audio thread
        private readonly float[] _scrubNewS = new float[2];    // audio thread
        private readonly double[] _scrubTotalS = new double[2]; // main thread
        private readonly double[] _thumpTotalJ = new double[2]; // main thread
        private readonly int[] _popTotal = new int[2];          // main thread
        private readonly int[] _thumpSeenMj = new int[2], _popSeen = new int[2];   // audio thread
        private readonly float[] _thumpNewJ = new float[2];
        private readonly int[] _popsNew = new int[2];
        private OneShotSynth _fx;
        private volatile CrashSoundMixer _crashes;
        private volatile PropStrikeMixer _propStrikes;
        private FlyingGame.Sim.Aircraft _strikeWatched;
        private double _strikeBatchSim = double.NaN;   // sim time of the first blade strike delivered this frame
        private CrashWaterVisuals _crashVisuals;
        private volatile EngineSynth _engine;
        private volatile bool _ready;
        private float _fs;
        private float _gain;                 // audio-thread smoothed master gain
        private float _shakePh;              // audio thread: the bent prop's once-a-revolution throb
        private float _gainA;
        private float _groanSeverity, _groanHold;
        private float _savedMaster;
        private string _builtForId;

        /// <summary>Name of the active engine recipe (diagnostics / HUD).</summary>
        public string EngineProfileName => _engine?.ProfileName ?? "glider (wind only)";

        // ---- one-shot API (called by StructuralDamage / PilotEgress) --------------------------------

        /// <summary>Stress hum while over the limit load; severity 0 (limit) .. 1 (ultimate). Called every frame it applies;
        /// follows the load up and down (the hold only bridges frames without a call).</summary>
        public void StructuralGroan(float severity)
        {
            _groanSeverity = Mathf.Clamp01(severity);
            _groanHold = GroanHoldS;
        }

        /// <summary>One rivet or fitting letting go: a metallic pop over the groan. The sim decides when (so a test
        /// can count them); the severity only shapes the timbre and level here.</summary>
        public void StructuralPop(float severity) { AtAircraft(FxKind.StructurePop, Mathf.Clamp01(severity)); }

        /// <summary>The wings let go: the hum stops dead and one snap.</summary>
        public void WingFailure() { _groanSeverity = 0f; _groanHold = 0f; AtAircraft(FxKind.WingFailure, 1f); }
        public void CanopyJettison() { _fx?.Trigger(FxKind.CanopyJettison); }
        public void EjectionSeat() { _fx?.Trigger(FxKind.EjectionSeat); }
        public void ChuteDeploy() { _fx?.Trigger(FxKind.ChuteDeploy); }
        public void ChuteInflate() { _fx?.Trigger(FxKind.ChuteInflate); }
        public void PylonBurst() { AtAircraft(FxKind.PylonBurst, 1f); }
        /// <summary>The airframe hitting something: crunching / tearing metal, weight 0..1 (a firm arrival .. a break-up).</summary>
        public void Crash(float severity) { AtAircraft(FxKind.Crash, severity); }
        /// <summary>Tyres meeting the runway: a short chirp, weight 0..1 from the sink rate.</summary>
        public void TireChirp(float severity) { AtAircraft(FxKind.TireChirp, severity); }

        // ---- sounds made at the aircraft: once the pilot is out they arrive late (speed of sound) and quiet ----
        public const float SpeedOfSoundMs = 343f, ReferenceDistanceM = 25f;
        private readonly List<(FxKind kind, float level, float due)> _delayed = new();
        private PilotEgress _egress;
        private AirframeVisual _visual;

        /// <summary>Distance from the listener (the pilot once out, else 0) to the aircraft.</summary>
        public float ListenerDistanceM { get; private set; }

        private void AtAircraft(FxKind kind, float level)
        {
            float d = ListenerDistanceM;
            if (d < 5f) { _fx?.Trigger(kind, level); return; }
            _delayed.Add((kind, level, Time.unscaledTime + d / SpeedOfSoundMs));
        }

        private float DistanceTo(Vector3 point)
        {
            _egress ??= GetComponent<PilotEgress>();
            bool pilotOut = _egress != null && _egress.PilotTransform != null &&
                (SessionSettings.ReplayActive ? FlightReplay.ReplayPilotOut : _egress.PilotOut);
            return Vector3.Distance(pilotOut ? _egress.PilotTransform.position : transform.position, point);
        }

        private readonly Dictionary<int, float> _waterImpactTimes = new();
        private void OnCrashImpact(FlyingGame.Core.CrashImpact impact)
        {
            if (impact.Surface == FlyingGame.Core.ImpactSurface.Water && !_waterImpactTimes.ContainsKey(impact.PieceId))
                _waterImpactTimes.Add(impact.PieceId, Time.unscaledTime);
            Vector3 point = CoordinateMap.ToUnity(impact.Position);
            float distance = DistanceTo(point);
            _crashes?.Impact(impact.Surface == FlyingGame.Core.ImpactSurface.Water, (float)impact.Severity01,
                (float)impact.MassKg, (float)impact.NormalSpeedMs, DistanceGain(distance), distance / SpeedOfSoundMs);
        }

        private void OnFlood(FlyingGame.Core.FloodState state)
        {
            // No impact means no audible flooding; later updates cannot re-arm an expired piece.
            if (!_waterImpactTimes.TryGetValue(state.PieceId, out float first)) return;
            float age = Time.unscaledTime - first;
            Vector3 point = CoordinateMap.ToUnity(state.Position);
            _crashes?.Flood(state.PieceId, (float)state.AirEscapeM3s, (float)state.TrappedAirM3,
                (float)state.DepthM, DistanceGain(DistanceTo(point)), age);
        }

        private static float DistanceGain(float d) => d <= 0f ? 1f : ReferenceDistanceM / (ReferenceDistanceM + d);
        /// <summary>A bullet landing on something (a drone, a ground target): metallic ping, weight 0..1.</summary>
        public void BulletHit(float severity) { _fx?.Trigger(FxKind.BulletHit, severity); }
        /// <summary>A drone going up.</summary>
        public void Explosion() { _fx?.Trigger(FxKind.Explosion, 1f); }
        /// <summary>Guns firing (held): a pulse train at the combined rate, rendered continuously.</summary>
        public void SetGuns(bool firing, float rateHz) { _gunsFiring = firing; _gunRateHz = rateHz; }
        private volatile bool _gunsFiring; private volatile float _gunRateHz;
        private float _gunPhase, _gunEnv, _gunSubPhase;
        private readonly Noise _gunNoise = new Noise(0x6A11u);
        private Biquad _gunBp;
        /// <summary>The pilot hitting the ground (severity 0..1 from the impact speed).</summary>
        public void PilotThud(float severity) { _fx?.Trigger(FxKind.BodyThud, severity); }
        public void PilotGrunt(float severity) { _fx?.Trigger(FxKind.Grunt, severity); }
        public void PilotWince(float severity) { _fx?.Trigger(FxKind.Wince, severity); }

        // ---- lifecycle ------------------------------------------------------------------------------

        private void Awake()
        {
            _driver = GetComponent<FlightSimDriver>();
#if UNITY_IOS && !UNITY_EDITOR
            // Ignore the ring/silent switch: a flight game with the engine muted by the mute switch reads as "no sound".
            try { FlyingGame_SetPlaybackAudioSession(); } catch (System.Exception e) { Debug.LogWarning("audio session: " + e.Message); }
#endif
            if (PlayerPrefs.HasKey(PrefKey)) MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKey, 1f));
            _savedMaster = MasterVolume;

            _fs = AudioSettings.outputSampleRate;
            if (_fs < 8000f) _fs = 48000f;
            _gainA = Dsp.Tau(0.08f, _fs);
            _wind = new WindSynth(_fs);
            _surface = new SurfaceSynth(_fs);
            _tyres = new TyreBrakeSynth(_fs);
            _fx = new OneShotSynth(_fs);
            _crashes = new CrashSoundMixer(_fs);
            _propStrikes = new PropStrikeMixer(_fs);
            _crashVisuals = gameObject.AddComponent<CrashWaterVisuals>();
            RebuildEngine();
            PushTelemetry();

            // A silent looping clip keeps the source "playing" so Unity runs OnAudioFilterRead every block;
            // the filter overwrites the (zero) buffer with the synthesized mix. Runtime-created, so nothing
            // depends on Resources or asset stripping.
            var go = new GameObject("FlightAudioOut");
            go.transform.SetParent(transform, false);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.dopplerLevel = 0f;
            _source.bypassEffects = true;
            _source.bypassListenerEffects = false;   // the clip recorder's listener tap must hear it (it only reads)
            _source.bypassReverbZones = true;
            _source.priority = 0;
            _source.volume = 1f;
            _source.loop = true;
            _clip = AudioClip.Create("FlightAudioSilence", 8192, 2, (int)_fs, false);
            _source.clip = _clip;
            go.AddComponent<FlightAudioOutput>().Owner = this;
            _ready = true;
            _source.Play();
        }

        private void OnEnable()
        {
            FlyingGame.Core.CrashEvents.Impact += OnCrashImpact;
            FlyingGame.Core.CrashEvents.Flood += OnFlood;
            if (_crashes != null) _crashes = new CrashSoundMixer(_fs);
            _ready = _crashes != null;
            if (_crashVisuals != null) _crashVisuals.enabled = true;
            if (_driver != null) { _driver.AircraftChanged += OnAircraftChanged; _driver.AirframeReplaced += OnAirframeReplaced; }
        }

        private void OnDisable()
        {
            FlyingGame.Core.CrashEvents.Impact -= OnCrashImpact;
            FlyingGame.Core.CrashEvents.Flood -= OnFlood;
            WatchStrikes(null);
            _delayed.Clear();
            _ready = false;
            if (_crashVisuals != null) _crashVisuals.enabled = false;
            if (_driver != null) { _driver.AircraftChanged -= OnAircraftChanged; _driver.AirframeReplaced -= OnAirframeReplaced; }
        }

        private void OnDestroy()
        {
            FlyingGame.Core.CrashEvents.Impact -= OnCrashImpact;
            FlyingGame.Core.CrashEvents.Flood -= OnFlood;
            _ready = false;
            SaveMaster();
            if (_clip != null) Destroy(_clip);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveMaster();
        }

        private void SaveMaster()
        {
            if (Mathf.Approximately(_savedMaster, MasterVolume)) return;
            _savedMaster = MasterVolume;
            PlayerPrefs.SetFloat(PrefKey, Mathf.Clamp01(MasterVolume));
        }

        private void OnAircraftChanged() { _waterImpactTimes.Clear(); RebuildEngine(); }

        /// <summary>A new aeroplane (FlightSimDriver.AirframeReplaced): the last one's crash is over — sounds still on their
        /// way at the speed of sound, crunches and flooding gurgles still ringing, spray and foam on the water.</summary>
        private void OnAirframeReplaced()
        {
            _delayed.Clear();
            _waterImpactTimes.Clear();
            if (_crashes != null) _crashes = new CrashSoundMixer(_fs);
            if (_propStrikes != null) _propStrikes = new PropStrikeMixer(_fs);
            if (_crashVisuals != null) _crashVisuals.ClearAll();
        }

        private void RebuildEngine()
        {
            var sim = _driver != null ? _driver.Sim : null;
            var config = sim?.Aircraft?.Config;
            string id = _driver != null ? _driver.AircraftId : null;
            _engine = EngineSynth.Build(id, config, _fs, (float)(_driver?.EngineRpm ?? 0.0), (float)(_driver?.Throttle01 ?? 0.0));
            _builtForId = id;
        }

        private void Update()
        {
            // Aircraft swapped without the event (AdoptSim / external Sim replacement): rebuild lazily.
            if (_driver.AircraftId != _builtForId) RebuildEngine();

            _groanHold -= Time.unscaledDeltaTime;
            if (_groanHold <= 0f) { _groanHold = 0f; _groanSeverity = 0f; }

            // Listener: the pilot once he has left the aircraft.
            _egress ??= GetComponent<PilotEgress>();
            // In a replay the listener follows the REPLAYED moment (in the cockpit until the recorded jump), not the live pilot.
            bool pilotOut = _egress != null && _egress.PilotTransform != null && (SessionSettings.ReplayActive ? FlightReplay.ReplayPilotOut : _egress.PilotOut);
            ListenerDistanceM = pilotOut ? Vector3.Distance(_egress.PilotTransform.position, transform.position) : 0f;
            for (int i = _delayed.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _delayed[i].due) continue;
                _fx?.Trigger(_delayed[i].kind, _delayed[i].level);
                _delayed.RemoveAt(i);
            }

            PushTelemetry();
            if (!Mathf.Approximately(_savedMaster, MasterVolume) && Time.frameCount % 120 == 0) SaveMaster();
        }

        private void LateUpdate()
        {
            // Whatever aeroplane is flying now; the next frame's blade strikes are timed from the first of them.
            WatchStrikes(_driver != null && _driver.Sim != null ? _driver.Sim.Aircraft : null);
            _strikeBatchSim = double.NaN;
        }

        private void WatchStrikes(FlyingGame.Sim.Aircraft ac)
        {
            if (ReferenceEquals(ac, _strikeWatched)) return;
            if (_strikeWatched != null) _strikeWatched.Contact -= OnBladeContact;
            _strikeWatched = ac;
            if (ac != null) ac.Contact += OnBladeContact;
        }

        /// <summary>One blade through the ground or the water (Aircraft.PropStrike, Ty 2026-11-05 9:04 "accurate sounds on both
        /// the ground and water too per blade strike"), or a blade breaking up. The sim steps several times a frame and a
        /// prop chewing the ground meets it blades × rpm times a second (60-90 a second), so each strike keeps its own
        /// instant: it is delayed from the first strike of this frame by the sim time between them, in whole samples
        /// (PropStrikeMixer), and by the speed of sound once the pilot is out.</summary>
        private void OnBladeContact(FlyingGame.Core.ContactEvent e)
        {
            if (_propStrikes == null) return;
            PropStrikeKind kind;
            bool water = e.SurfaceCode == FlyingGame.Core.ContactPoint.SurfaceWater;
            if (e.Kind == FlyingGame.Core.ContactEventKind.PropBladeStrike)
                kind = water ? PropStrikeKind.Water
                    : e.Material == FlyingGame.Core.PropMaterial.Wood ? PropStrikeKind.WoodGround
                    : e.Material == FlyingGame.Core.PropMaterial.Composite ? PropStrikeKind.CompositeGround : PropStrikeKind.MetalGround;
            else if (e.Kind == FlyingGame.Core.ContactEventKind.PropBladeBroken)
                kind = e.Material == FlyingGame.Core.PropMaterial.Wood ? PropStrikeKind.WoodSplinter : PropStrikeKind.CompositeFray;
            else return;
            if (double.IsNaN(_strikeBatchSim)) _strikeBatchSim = e.TimeSec;
            float delay = Mathf.Max(0f, (float)(e.TimeSec - _strikeBatchSim));
            float d = ListenerDistanceM;
            float tip = e.Kind == FlyingGame.Core.ContactEventKind.PropBladeStrike ? (float)e.Intensity : 150f;
            float depth = e.Kind == FlyingGame.Core.ContactEventKind.PropBladeStrike ? (float)e.Value : 0.02f;
            _propStrikes.Strike(kind, tip, depth, DistanceGain(d), delay + d / SpeedOfSoundMs);
        }

        private void PushTelemetry()
        {
            FlightSimDriver d = _driver;
            var aircraft = d.Sim?.Aircraft;
            _tele.IasMs = (float)d.IasMs;
            _tele.AlphaDeg = (float)d.AlphaDeg;
            _tele.BetaDeg = (float)d.BetaDeg;
            // What the engine is really doing, not the lever: a dead engine (prop strike, idle cut-off, too lean) makes no
            // engine note, however far forward the throttle is (Ty 2026-11-04).
            bool running = aircraft == null || aircraft.EngineRunning;
            _tele.Throttle01 = aircraft != null ? (float)aircraft.ShaftPower01 : (float)d.Throttle01;
            _tele.EngineRpm = running ? (float)d.EngineRpm : 0f;
            _tele.PropShakeG = aircraft != null ? (float)aircraft.PropVibrationG() : 0f;
            _tele.PropShakeHz = aircraft != null ? (float)aircraft.PropVibrationHz : 0f;
            _tele.Spoiler01 = aircraft != null ? (float)aircraft.CurrentDeflections.SpoilerFraction : 0f;
            var spoilerCfg = aircraft?.Config?.Controls?.Spoiler;
            _tele.SpoilerRoar = spoilerCfg != null ? (float)spoilerCfg.SoundLevel : 1f;
            _tele.SpoilerRumble = spoilerCfg != null ? (float)spoilerCfg.SoundRumble : 0f;
            _tele.OnGround = d.OnGround;
            _tele.TurbulenceLevel = SessionSettings.TurbulenceLevel;
            _tele.WindSpeedMs = SessionSettings.WindSpeedMs;
            if (aircraft != null && FlyingGame.Core.Atmosphere.Field != null)
            {
                // Regional air: the wind noise follows the air the aircraft is actually in (dead quiet at the home field).
                var here = FlyingGame.Core.Atmosphere.Field.Sample(aircraft.State.Position, FlyingGame.Core.Atmosphere.SimTimeSec);
                _tele.TurbulenceLevel = Mathf.Clamp((float)here.SigmaHorizMs / 1.5f, 0f, 3f);   // light ≈ 1.5 m/s, moderate ≈ 4
                _tele.WindSpeedMs = (float)System.Math.Sqrt(here.MeanWind.X * here.MeanWind.X + here.MeanWind.Y * here.MeanWind.Y);
            }
            _tele.GroanSeverity = _groanSeverity;
            PushGroundSound(aircraft);
            _tele.MasterGain = SessionSettings.MenuOpen ? 0f : Mathf.Clamp01(MasterVolume) * MixGain;
            // Pilot out: his own airspeed is the wind in his ears; the aircraft's engine, tyres and crashes fade with distance.
            bool pilotOut = _egress != null && _egress.PilotTransform != null && (SessionSettings.ReplayActive ? FlightReplay.ReplayPilotOut : _egress.PilotOut);
            _tele.PilotOut = pilotOut;
            _tele.AircraftGain = pilotOut ? DistanceGain(ListenerDistanceM) : 1f;
            if (pilotOut) { _tele.IasMs = SessionSettings.ReplayActive ? FlightReplay.ReplayPilotSpeedMs : _egress.PilotAirspeedMs; _tele.AlphaDeg = 0f; _tele.BetaDeg = 0f; _tele.Spoiler01 = 0f; }
            // An open or departed canopy lets the slipstream into the cockpit: the noise grows with q and with how open it
            // is (Core Vr.CockpitWind). What a SHUT canopy lets through is already in the wind above; only the excess is added.
            _visual ??= GetComponent<AirframeVisual>();
            float air = 0f;
            if (!pilotOut && _visual != null && LocalPlayerView.InCockpitFor(d))
            {
                double q = 0.5 * 1.225 * d.IasMs * d.IasMs, e = _visual.Cockpit.CockpitExposure01;
                air = (float)(FlyingGame.Core.Vr.CockpitWind.Amplitude(q, e) - FlyingGame.Core.Vr.CockpitWind.Amplitude(q, 0.0));
            }
            _tele.CockpitAir = air;
        }

        /// <summary>The wheels, brakes, skis and floats: Core's GroundSoundMix reads each wheel's contact (the ground
        /// under THAT wheel, its spin, its slip, its brake) and this copies the result into the mailbox.</summary>
        private void PushGroundSound(FlyingGame.Sim.Aircraft aircraft)
        {
            var wheels = aircraft?.Wheels;
            if (wheels == null || wheels.Count == 0)
            {
                for (int i = 0; i < _tele.RollVoiceLR.Length; i++) _tele.RollVoiceLR[i] = 0f;
                for (int s = 0; s < 2; s++) { _tele.WhirGain[s] = 0f; _tele.ScrapeGain[s] = 0f; _tele.SquealGain[s] = 0f; _tele.FlapGain[s] = 0f; }
                _tele.WaterSplash = _tele.WaterHiss = _tele.Spray = _tele.SkiHiss = _tele.KeelScrape = 0f;
                return;
            }
            if (_groundMix == null || _groundMixFor != aircraft) { _groundMix = new FlyingGame.Core.GroundSoundMix(wheels.Count); _groundMixFor = aircraft; }
            var m = _groundMix;
            double weight = aircraft.MassProperties.MassKg * 9.81;
            m.Update(wheels, weight, Time.deltaTime, aircraft.Config.Floats != null ? FlyingGame.Core.FloatHydro.Floats : null, 2);
            const int keys = FlyingGame.Core.GroundSoundMix.Keys, voices = 10;
            for (int i = 0; i < _voiceLR.Length; i++) _voiceLR[i] = 0f;
            for (int v = 0; v < voices; v++) { _voicePitch[v] = 0f; _voicePitchW[v] = 0f; }
            for (int k = 0; k < keys; k++)
            {
                int v = (int)FlyingGame.Core.GroundMaterials.Row((FlyingGame.Core.GroundMaterial)k).Voice;
                float gl = m.RollingGain[k], gr = m.RollingGain[keys + k];
                _voiceLR[v] += gl; _voiceLR[voices + v] += gr;
                float wsum = gl + gr;
                _voicePitch[v] += m.RollingPitch[k] * wsum; _voicePitchW[v] += wsum;
            }
            for (int i = 0; i < _voiceLR.Length; i++) _tele.RollVoiceLR[i] = _voiceLR[i];
            for (int v = 0; v < voices; v++) _tele.RollVoicePitch[v] = _voicePitchW[v] > 1e-6f ? _voicePitch[v] / _voicePitchW[v] : 1f;
            var vW = aircraft.State.Attitude.Rotate(aircraft.State.Velocity);
            _tele.RollSpeedMs = (float)System.Math.Sqrt(vW.X * vW.X + vW.Y * vW.Y);
            for (int s = 0; s < 2; s++)
            {
                _tele.WhirHz[s] = m.WhirHz[s]; _tele.WhirGain[s] = m.WhirGain[s];
                _chirpTotalS[s] += m.ChirpSeconds[s];
                System.Threading.Volatile.Write(ref _tele.ChirpTotalUs[s], (int)(_chirpTotalS[s] * 1e6 % int.MaxValue));
                if (m.ChirpSeconds[s] > 0f) { _tele.ChirpGain[s] = m.ChirpGain[s]; _tele.ChirpHz[s] = m.ChirpHz[s]; }
                _scrubTotalS[s] += m.ScrubSeconds[s];
                System.Threading.Volatile.Write(ref _tele.ScrubTotalUs[s], (int)(_scrubTotalS[s] * 1e6 % int.MaxValue));
                if (m.ScrubSeconds[s] > 0f) { _tele.ScrubGain[s] = m.ScrubLevel[s]; _tele.ScrubHz[s] = m.ScrubCentreHz[s]; }
                _tele.ScrapeGain[s] = m.ScrapeGain[s]; _tele.ScrapeHz[s] = m.ScrapeHz[s];
                _tele.SquealGain[s] = m.SquealGain[s]; _tele.SquealHz[s] = m.SquealHz[s];
                _thumpTotalJ[s] += m.ThumpJ[s];
                // Felt as well as heard (2026-11-05, flat spots): in a headset each thump is a short knock in the
                // controller on that side, as loud as the sound's (√ of the work against the reference). The phone
                // has no haptics path; there the bump is the aeroplane's own pitch and bounce.
                if (m.ThumpJ[s] > 0f && XrHeadset.Active)
                    XrHandInput.Tick(s == 0, Mathf.Clamp01(0.5f * Mathf.Sqrt(m.ThumpJ[s] / (float)FlyingGame.Core.GroundSoundMix.ThumpRefJ)), 0.03f);
                System.Threading.Volatile.Write(ref _tele.ThumpTotalMj[s], (int)(_thumpTotalJ[s] * 1e3 % int.MaxValue));
                _popTotal[s] += m.Pops[s];
                System.Threading.Volatile.Write(ref _tele.PopTotal[s], _popTotal[s]);
                _tele.FlapHz[s] = m.FlapHz[s]; _tele.FlapGain[s] = m.FlapGain[s];
            }
            _tele.WaterSplash = m.WaterSplash; _tele.WaterHiss = m.WaterHiss; _tele.Spray = m.Spray;
            _tele.SkiHiss = m.SkiHiss; _tele.KeelScrape = m.KeelScrape;
        }
        private FlyingGame.Sim.Aircraft _groundMixFor;
        private readonly float[] _voiceLR = new float[20], _voicePitch = new float[10], _voicePitchW = new float[10];

        // ---- audio thread ---------------------------------------------------------------------------

        /// <summary>Fill an interleaved output buffer with the synthesized mix. Audio thread; no Unity API,
        /// no allocation.</summary>
        internal void Render(float[] data, int channels)
        {
            if (!_ready || channels < 1) return;
            int frames = data.Length / channels;
            AudioTelemetry t = _tele;
            float ias = t.IasMs, thr = t.Throttle01, rpm = t.EngineRpm;
            float shakeHz = t.PropShakeHz, shakeDepth = shakeHz > 1f ? (float)FlyingGame.Core.PropBladeDamage.Felt01(t.PropShakeG) * 0.6f : 0f;
            float masterTarget = t.MasterGain;
            EngineSynth engine = _engine;
            bool powered = engine != null;

            float acGain = t.AircraftGain;
            _wind.SetSpoilerVoice(t.SpoilerRoar, t.SpoilerRumble);
            _wind.Prepare(ias, t.AlphaDeg, t.BetaDeg, t.TurbulenceLevel, t.WindSpeedMs, t.Spoiler01, powered && !t.PilotOut, t.CockpitAir);
            _fx.Prepare(ias, t.GroanSeverity, acGain);
            _surface.Prepare(t.RollVoiceLR, t.RollVoicePitch, t.RollSpeedMs);
            for (int s = 0; s < 2; s++)
            {
                int total = System.Threading.Volatile.Read(ref t.ChirpTotalUs[s]);
                int d = total - _chirpSeenUs[s];
                _chirpSeenUs[s] = total;
                _chirpNewS[s] = d > 0 && d < 1000000 ? d * 1e-6f : 0f;
                int sc = System.Threading.Volatile.Read(ref t.ScrubTotalUs[s]);
                int dsc = sc - _scrubSeenUs[s];
                _scrubSeenUs[s] = sc;
                _scrubNewS[s] = dsc > 0 && dsc < 1000000 ? dsc * 1e-6f : 0f;
                int th = System.Threading.Volatile.Read(ref t.ThumpTotalMj[s]);
                int dth = th - _thumpSeenMj[s];
                _thumpSeenMj[s] = th;
                _thumpNewJ[s] = dth > 0 && dth < 100000000 ? dth * 1e-3f : 0f;
                int pops = System.Threading.Volatile.Read(ref t.PopTotal[s]);
                _popsNew[s] = pops - _popSeen[s] > 0 ? pops - _popSeen[s] : 0;
                _popSeen[s] = pops;
            }
            _tyres.Prepare(t.WhirHz, t.WhirGain, _chirpNewS, t.ChirpGain, t.ChirpHz, _scrubNewS, t.ScrubGain, t.ScrubHz, t.ScrapeGain, t.ScrapeHz, t.SquealGain, t.SquealHz,
                           t.WaterSplash, t.WaterHiss, t.Spray, t.SkiHiss, t.KeelScrape,
                           _thumpNewJ, _popsNew, t.FlapHz, t.FlapGain);
            CrashSoundMixer crashes = _crashes;
            crashes.Prepare();
            PropStrikeMixer strikes = _propStrikes;
            strikes?.Prepare();
            engine?.Prepare(rpm, thr);

            // Fully ducked and settled: emit silence without rendering.
            if (masterTarget <= 0f && _gain < 1e-4f)
            {
                _gain = 0f;
                // Keep flood expiry and impact delays advancing while the menu mutes the output.
                for (int f = 0; f < frames; f++) { crashes.Next(); strikes?.Next(); }
                for (int i = 0; i < data.Length; i++) data[i] = 0f;
                return;
            }

            // Guns: each round is a 6 ms noise crack through a 900 Hz body with a sub thump, at the combined rate.
            bool guns = _gunsFiring; float gunRate = _gunRateHz;
            if (_gunBp == null) { _gunBp = new Biquad(); _gunBp.BandPass(_fs, 900f, 1.2f); }
            int idx = 0;
            for (int f = 0; f < frames; f++)
            {
                _gain += _gainA * (masterTarget - _gain);
                float gun = 0f;
                if (guns && gunRate > 0f)
                {
                    _gunPhase += gunRate / _fs;
                    if (_gunPhase >= 1f) { _gunPhase -= 1f; _gunEnv = 1f; _gunSubPhase = 0f; }
                    _gunEnv *= 1f - 1f / (0.006f * _fs);
                    _gunSubPhase += 60f / _fs;
                    gun = (_gunBp.Process(_gunNoise.Next() * _gunEnv * 5f) + 0.35f * Dsp.Sin01(_gunSubPhase) * _gunEnv) * 0.7f;
                }
                else _gunEnv = 0f;

                _wind.Next(out float wl, out float wr);
                _surface.Next(out float sl, out float sr);
                _tyres.Next(out float tl, out float tr, out float ql, out float qr);
                sl = (sl + tl) * FlyingGame.Core.GroundSoundMix.OutputGain + ql * FlyingGame.Core.GroundSoundMix.ScreechGain;   // ground bus −4 dB, screech unchanged
                sr = (sr + tr) * FlyingGame.Core.GroundSoundMix.OutputGain + qr * FlyingGame.Core.GroundSoundMix.ScreechGain;
                float el = 0f, er = 0f;
                engine?.Next(out el, out er);
                if (shakeDepth > 0f)
                {
                    // A bent prop (Aircraft.PropVibrationG): the engine note throbs once a revolution, with the airframe's
                    // own low drone at that rate under it. Depth from how strongly the shake is felt (PropBladeDamage.Felt01).
                    _shakePh = Dsp.Wrap01(_shakePh + shakeHz / _fs);
                    float sw = Dsp.Sin01(_shakePh);
                    el = el * (1f + shakeDepth * sw) + 0.12f * shakeDepth * sw;
                    er = er * (1f + shakeDepth * sw) + 0.12f * shakeDepth * sw;
                }
                float fx = (_fx.Next() + crashes.Next() + (strikes != null ? strikes.Next() * PropStrikeMix : 0f)) * FxMix;

                float l = Dsp.SoftClip((wl * WindMix + (el * EngineMix + sl) * acGain + fx + gun) * _gain);
                float r = Dsp.SoftClip((wr * WindMix + (er * EngineMix + sr) * acGain + fx + gun) * _gain);

                data[idx] = l;
                if (channels > 1) data[idx + 1] = r;
                for (int c = 2; c < channels; c++) data[idx + c] = 0.5f * (l + r);
                idx += channels;
            }
            VoiceComms.RenderRx(data, frames, channels);   // radio from other pilots (in the game mix, so clips hear it)
        }
    }
}

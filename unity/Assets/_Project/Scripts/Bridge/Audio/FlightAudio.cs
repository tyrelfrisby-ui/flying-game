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
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void FlyingGame_SetPlaybackAudioSession();
#endif

        private const string PrefKey = "audio.masterVolume";
        private const float EngineMix = 0.55f, WindMix = 1f, FxMix = 0.8f;
        private const float GroanHoldS = 0.2f;   // StructuralGroan is per-frame; hold bridges frame gaps

        private FlightSimDriver _driver;
        private AudioSource _source;
        private AudioClip _clip;
        private readonly AudioTelemetry _tele = new AudioTelemetry();
        private WindSynth _wind;
        private SurfaceSynth _surface;
        private OneShotSynth _fx;
        private volatile EngineSynth _engine;
        private volatile bool _ready;
        private float _fs;
        private float _gain;                 // audio-thread smoothed master gain
        private float _gainA;
        private float _groanSeverity, _groanHold;
        private float _savedMaster;
        private string _builtForId;

        /// <summary>Name of the active engine recipe (diagnostics / HUD).</summary>
        public string EngineProfileName => _engine?.ProfileName ?? "glider (wind only)";

        // ---- one-shot API (called by StructuralDamage / PilotEgress) --------------------------------

        /// <summary>Airframe groan/creak while over the limit load; severity 0..1. Called every frame it applies.</summary>
        public void StructuralGroan(float severity)
        {
            if (severity > _groanSeverity || _groanHold <= 0f) _groanSeverity = Mathf.Clamp01(severity);
            _groanHold = GroanHoldS;
        }

        public void WingFailure() { AtAircraft(FxKind.WingFailure, 1f); }
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

        /// <summary>Distance from the listener (the pilot once out, else 0) to the aircraft.</summary>
        public float ListenerDistanceM { get; private set; }

        private void AtAircraft(FxKind kind, float level)
        {
            float d = ListenerDistanceM;
            if (d < 5f) { _fx?.Trigger(kind, level); return; }
            _delayed.Add((kind, level, Time.unscaledTime + d / SpeedOfSoundMs));
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
            _fx = new OneShotSynth(_fs);
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
            if (_driver != null) _driver.AircraftChanged += OnAircraftChanged;
        }

        private void OnDisable()
        {
            if (_driver != null) _driver.AircraftChanged -= OnAircraftChanged;
        }

        private void OnDestroy()
        {
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

        private void OnAircraftChanged() => RebuildEngine();

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
            bool pilotOut = _egress != null && _egress.PilotOut && _egress.PilotTransform != null;
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

        private void PushTelemetry()
        {
            FlightSimDriver d = _driver;
            var aircraft = d.Sim?.Aircraft;
            _tele.IasMs = (float)d.IasMs;
            _tele.AlphaDeg = (float)d.AlphaDeg;
            _tele.BetaDeg = (float)d.BetaDeg;
            _tele.Throttle01 = (float)d.Throttle01;
            _tele.EngineRpm = (float)d.EngineRpm;
            _tele.Spoiler01 = aircraft != null ? (float)aircraft.CurrentDeflections.SpoilerFraction : 0f;
            _tele.OnGround = d.OnGround;
            _tele.TurbulenceLevel = SessionSettings.TurbulenceLevel;
            _tele.WindSpeedMs = SessionSettings.WindSpeedMs;
            _tele.GroanSeverity = _groanSeverity;
            float gravel = 0f, rough = 0f;
            if (aircraft != null && d.OnGround && FlyingGame.Core.LandingGear.AnyMainWheelOnGround(aircraft.Config, aircraft.State))
            {
                var st = aircraft.State; var vW = st.Attitude.Rotate(st.Velocity);
                float gs = (float)System.Math.Sqrt(vW.X * vW.X + vW.Y * vW.Y);
                var surf = FlyingGame.Core.WorldTerrain.SurfaceAt(st.Position.X, st.Position.Y);
                if (surf == FlyingGame.Core.WorldTerrain.Surface.Gravel) gravel = gs;
                else if (surf == FlyingGame.Core.WorldTerrain.Surface.Rough) rough = gs;
            }
            _tele.GravelSpeed = gravel; _tele.RoughSpeed = rough;
            _tele.MasterGain = SessionSettings.MenuOpen ? 0f : Mathf.Clamp01(MasterVolume);
            // Pilot out: his own airspeed is the wind in his ears; the aircraft's engine, tyres and crashes fade with distance.
            bool pilotOut = _egress != null && _egress.PilotOut && _egress.PilotTransform != null;
            _tele.PilotOut = pilotOut;
            _tele.AircraftGain = pilotOut ? DistanceGain(ListenerDistanceM) : 1f;
            if (pilotOut) { _tele.IasMs = _egress.PilotAirspeedMs; _tele.AlphaDeg = 0f; _tele.BetaDeg = 0f; _tele.Spoiler01 = 0f; }
        }

        // ---- audio thread ---------------------------------------------------------------------------

        /// <summary>Fill an interleaved output buffer with the synthesized mix. Audio thread; no Unity API,
        /// no allocation.</summary>
        internal void Render(float[] data, int channels)
        {
            if (!_ready || channels < 1) return;
            int frames = data.Length / channels;
            AudioTelemetry t = _tele;
            float ias = t.IasMs, thr = t.Throttle01, rpm = t.EngineRpm;
            float masterTarget = t.MasterGain;
            EngineSynth engine = _engine;
            bool powered = engine != null;

            float acGain = t.AircraftGain;
            _wind.Prepare(ias, t.AlphaDeg, t.BetaDeg, t.TurbulenceLevel, t.WindSpeedMs, t.Spoiler01, powered && !t.PilotOut);
            _fx.Prepare(ias, t.GroanSeverity, acGain);
            _surface.Prepare(t.GravelSpeed, t.RoughSpeed);
            engine?.Prepare(rpm, thr);

            // Fully ducked and settled: emit silence without rendering.
            if (masterTarget <= 0f && _gain < 1e-4f)
            {
                _gain = 0f;
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
                float el = 0f, er = 0f;
                engine?.Next(out el, out er);
                float fx = _fx.Next() * FxMix;

                float l = Dsp.SoftClip((wl * WindMix + (el * EngineMix + sl) * acGain + fx + gun) * _gain);
                float r = Dsp.SoftClip((wr * WindMix + (er * EngineMix + sr) * acGain + fx + gun) * _gain);

                data[idx] = l;
                if (channels > 1) data[idx + 1] = r;
                for (int c = 2; c < channels; c++) data[idx + c] = 0.5f * (l + r);
                idx += channels;
            }
        }
    }
}

using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE PILOT'S WATER SOUNDS (Ty, 2026-11-02 14:31: "NO gurgling or bubbling … only a LIGHT splash on entry and gentle
    /// water-lapping ambience at the surface; while the eye is under water, the same ambience low-passed / muted").
    /// Core PilotWaterAudio decides; this synthesizes, on a child of the camera (the listener):
    ///   splash   a short burst of band-limited noise (5 ms attack, 0.25 s decay, 300 Hz – 4 kHz; E), its gain Core's
    ///   lapping  low-passed noise (600 Hz) under two slow swells (0.35 and 0.22 Hz; E) — the surface slapping softly
    ///   under    an AudioLowPassFilter on the listener at Core's corner (500 Hz under water, open above, log-blended with
    ///            the view's 0.25 s blend), so the lapping AND everything else he hears are muffled together
    /// Nothing here makes a bubble or a gurgle, and <see cref="PilotWaterAudioGate"/> tells anything else to stay silent near
    /// him while he is in the water. Same silent-clip pattern as FlightAudio: the filter writes the mix.
    /// </summary>
    public sealed class PilotWaterSound : MonoBehaviour
    {
        private AudioSource _source;
        private AudioLowPassFilter _lpf;
        private float _fs = 48000f;
        // main thread → audio thread
        private volatile float _lapGain, _splashGain;
        private volatile int _splashSerial;
        // audio thread state
        private int _splashSeen;
        private float _splashEnv, _splashPeak, _t, _lpLap, _hpS, _lpS, _prevS;
        private uint _rng = 0x9E3779B9u;
        private float _lapSmoothed;

        private void Awake()
        {
            _fs = AudioSettings.outputSampleRate;
            if (_fs < 8000f) _fs = 48000f;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.dopplerLevel = 0f;
            _source.bypassEffects = true;
            _source.bypassListenerEffects = false;   // the listener's low-pass must muffle it too
            _source.bypassReverbZones = true;
            _source.loop = true;
            _source.volume = 1f;
            _source.clip = AudioClip.Create("PilotWaterSilence", 8192, 2, (int)_fs, false);
            _source.Play();
            Transform listener = transform.parent != null ? transform.parent : transform;
            _lpf = listener.GetComponent<AudioLowPassFilter>();
            if (_lpf == null) _lpf = listener.gameObject.AddComponent<AudioLowPassFilter>();
            _lpf.cutoffFrequency = (float)PilotWaterAudio.OpenLowPassHz;
            _lpf.lowpassResonanceQ = 1f;
            _lpf.enabled = false;
        }

        private void Update()
        {
            VrCockpitHands hands = VrCockpitHands.Current;
            bool inWater = hands != null && hands.PilotInWater;
            double speed = 0.0;
            bool entered = hands != null && hands.TakeWaterEntry(out speed);
            // The eye's state comes from the ONE underwater renderer, PilotUnderwater (Ty 2026-11-10: one source of
            // truth). This used to read lane/claude-vr-cockpit's own UnderwaterRender, which was dropped on merge
            // because the shipping PilotUnderwater (t922u, 11-06) already does the view and two layers would have
            // double-murked a flooded cockpit.
            PilotWaterAudioState st = PilotWaterAudio.Select(inWater || entered, PilotUnderwater.EyeUnder, entered, speed, PilotUnderwater.Blend01);
            _lapGain = (float)st.LappingGain;
            if ((st.Cues & WaterCue.Splash) != 0) { _splashGain = (float)st.SplashGain; _splashSerial++; }
            if (_lpf != null)
            {
                bool muffle = st.LowPassHz < 21000.0;
                if (_lpf.enabled != muffle) _lpf.enabled = muffle;
                if (muffle) _lpf.cutoffFrequency = (float)st.LowPassHz;
            }
        }

        private float Noise()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 8388608f - 1f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float dt = 1f / _fs;
            float aLap = 1f - Mathf.Exp(-2f * Mathf.PI * 600f * dt);
            float aHp = Mathf.Exp(-2f * Mathf.PI * 300f * dt), aLp = 1f - Mathf.Exp(-2f * Mathf.PI * 4000f * dt);
            float decay = Mathf.Exp(-dt / 0.25f), attack = 1f - Mathf.Exp(-dt / 0.005f);
            float lapTarget = _lapGain;
            if (_splashSerial != _splashSeen) { _splashSeen = _splashSerial; _splashPeak = _splashGain; _splashEnv = 0f; }
            for (int i = 0; i < data.Length; i += channels)
            {
                _t += dt;
                _lapSmoothed += (lapTarget - _lapSmoothed) * 0.0005f;
                // lapping: soft low noise under two slow swells
                _lpLap += aLap * (Noise() - _lpLap);
                float swell = 0.55f + 0.30f * Mathf.Sin(2f * Mathf.PI * 0.35f * _t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.22f * _t + 1.3f);
                float lap = _lpLap * swell * _lapSmoothed * 2.2f;
                // splash: band-limited noise burst
                float s = 0f;
                if (_splashPeak > 1e-4f)
                {
                    _splashEnv += attack * (_splashPeak - _splashEnv);
                    _splashPeak *= decay;
                    float n = Noise();
                    _hpS = aHp * (_hpS + n - _prevS); _prevS = n;
                    _lpS += aLp * (_hpS - _lpS);
                    s = _lpS * _splashEnv;
                }
                float v = lap + s;
                for (int c = 0; c < channels; c++) data[i + c] = v;
            }
        }

        private void OnDisable() { if (_lpf != null) _lpf.enabled = false; }
    }
}

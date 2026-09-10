using FlyingGame.Core.DataContracts;

namespace FlyingGame.Bridge
{
    /// <summary>One engine's sound. Prepare() runs once per audio block (coefficients from the smoothed
    /// state), Next() once per sample. Pan gains place twins left/right.</summary>
    internal abstract class EngineVoice
    {
        public float PanL = 0.707f, PanR = 0.707f;
        public abstract void Prepare(float rpmTarget, float throttleTarget);
        public abstract float Next();
        /// <summary>Jump the smoothed state to the targets (new aircraft: no spool sweep).</summary>
        public abstract void Snap(float rpm, float throttle);
    }

    /// <summary>
    /// Piston engine + prop: exhaust pulse train (decaying, crackle-noised pulses at the firing rate,
    /// per-cylinder unevenness, idle lope) + additive harmonic series of the firing fundamental (the
    /// tone), shaped by two exhaust formants and a load-dependent low-pass; prop blade-pass thrum with
    /// tip-speed noise; optional supercharger whine; optional radial crank thump.
    /// </summary>
    internal sealed class PistonVoice : EngineVoice
    {
        private readonly PistonProfile P;
        private readonly float _fs, _nyq;
        private readonly Noise _noise;
        private readonly float _detune;

        // smoothed rpm / throttle (0.15 s audio-side lag)
        private float _rpm, _thr, _rpmTarget, _thrTarget;
        private readonly float _lagA;

        // exhaust pulse train
        private readonly float[] _cylAmp;
        private float _firePhase, _env;
        private int _fireIdx;
        private readonly float _decay;
        private float _jitter;

        // additive tone
        private readonly float[] _harm;
        private float _tonePhase, _tilt;

        // idle lope
        private float _camPhase, _wobble, _lopeDepth;

        // prop
        private float _propPhase, _propGain, _tipGain, _crankPhase, _thumpGain;
        private readonly OnePole _tipLp = new OnePole();

        // supercharger
        private float _scPhase, _scVibPhase, _scGain;
        private readonly Biquad _scBp = new Biquad();

        // exhaust chain
        private readonly Biquad _hp = new Biquad(), _f1 = new Biquad(), _f2 = new Biquad(), _lp = new Biquad();
        private float _exhaustGain, _toneGain, _mechGain;
        private readonly float _pulsesPerRev;

        public PistonVoice(PistonProfile profile, float fs, uint seed, float detune)
        {
            P = profile;
            _fs = fs;
            _nyq = fs * 0.45f;
            _noise = new Noise(seed);
            _detune = detune;
            _lagA = Dsp.Tau(0.15f, fs);
            _decay = Dsp.DecayCoef(P.PulseDecayS, fs);
            _pulsesPerRev = P.Cylinders * 0.5f;
            _harm = (float[])P.Harmonics.Clone();
            float sum = 0f;
            foreach (float a in _harm) sum += a;
            if (sum > 0f) for (int i = 0; i < _harm.Length; i++) _harm[i] /= sum;   // tone peak ≤ 1
            _cylAmp = new float[P.Cylinders];
            for (int i = 0; i < P.Cylinders; i++) _cylAmp[i] = 1f + P.CylinderSpread * _noise.Next();
            _hp.HighPass(fs, 55f, 0.7f);
            _f1.BandPass(fs, P.Formant1Hz, P.Formant1Q);
            if (P.Formant2Gain > 0f) _f2.BandPass(fs, P.Formant2Hz, P.Formant2Q); else _f2.Bypass();
            _lp.LowPass(fs, P.LpIdleHz, 0.6f);
            _scBp.BandPass(fs, 1000f, 12f);
            _tipLp.SetCutoff(1200f, fs);
            _rpm = P.IdleRpm;
        }

        public override void Snap(float rpm, float throttle)
        {
            _rpm = _rpmTarget = rpm * _detune;
            _thr = _thrTarget = throttle;
        }

        public override void Prepare(float rpmTarget, float throttleTarget)
        {
            _rpmTarget = rpmTarget * _detune;
            _thrTarget = throttleTarget;

            float thr = Dsp.Clamp01(_thr);
            float rpmN = Dsp.Clamp01((_rpm - P.IdleRpm) / (P.MaxRpm - P.IdleRpm));

            // load-dependent brightness and mix
            _lp.LowPass(_fs, Dsp.Lerp(P.LpIdleHz, P.LpFullHz, Dsp.Pow(thr, 0.7f)), 0.6f);
            // Throttle = manifold pressure = how hard each pulse hits: loudness, not pitch (a governed prop holds
            // rpm, so this is the whole difference between cruise and full power on a constant-speed type).
            _exhaustGain = P.ExhaustGain * (0.18f + 0.82f * Dsp.Pow(thr, 0.8f));
            _toneGain = P.ToneGain * (0.25f + 0.75f * thr) * (0.5f + 0.5f * rpmN);
            _tilt = 0.55f * (1f - thr);
            _jitter = 0.04f + 0.14f * (1f - thr);
            _mechGain = P.Mechanical * (1f - 0.65f * thr);
            _thumpGain = P.CrankThump * (0.5f + 0.5f * thr);

            // idle lope: only near idle throttle / idle rpm; random-walk wobble refreshed per block
            _lopeDepth = P.Lope * (1f - Dsp.SmoothStep(0.03f, 0.35f, thr)) * (1f - Dsp.SmoothStep(0.15f, 0.5f, rpmN));
            _wobble += (_noise.Next() * 0.6f - _wobble) * 0.35f;

            // prop: blade-pass grows with rpm, tip noise with tip Mach
            float n = _rpm * P.PropReduction / 60f;
            float tipMach = 3.14159f * P.PropDiameterM * n / 340f;
            _propGain = P.PropGain * rpmN * rpmN;
            _tipGain = P.PropTipNoise * tipMach * tipMach * tipMach;
            _tipLp.SetCutoff(900f + 2500f * rpmN, _fs);

            // supercharger: whine rises with boost (throttle) and impeller speed
            if (P.SuperchargerRatio > 0f)
            {
                float whineHz = _rpm * P.SuperchargerRatio / 60f * P.SuperchargerBlades;
                if (whineHz > _nyq * 0.8f) whineHz = _nyq * 0.8f;
                _scBp.BandPass(_fs, whineHz, 12f);
                _scGain = P.SuperchargerGain * (0.25f + 0.75f * Dsp.Pow(thr, 1.5f)) * (0.2f + 0.8f * Dsp.Pow(rpmN, 1.2f));
            }
        }

        public override float Next()
        {
            _rpm += _lagA * (_rpmTarget - _rpm);
            _thr += _lagA * (_thrTarget - _thr);

            // lope modulates the instantaneous rpm the whole voice runs on
            _camPhase += _rpm / 120f / _fs;
            if (_camPhase >= 1f) _camPhase -= 1f;
            float lope = _lopeDepth * (0.6f * Dsp.Sin01(_camPhase) + 0.4f * _wobble);
            float rpmA = _rpm * (1f + lope);
            float f0 = rpmA / 60f * _pulsesPerRev;
            float inc = f0 / _fs;

            // exhaust pulse train
            _firePhase += inc;
            if (_firePhase >= 1f)
            {
                _firePhase -= 1f;
                if (++_fireIdx >= P.Cylinders) _fireIdx = 0;
                _env = _cylAmp[_fireIdx] * (1f + _jitter * _noise.Next()) * (1f + 3f * lope);
            }
            _env *= _decay;
            float n = _noise.Next();
            float exhaust = _env * (1f + P.Crackle * n);

            // additive tone: harmonic series of the firing fundamental (band-limited)
            _tonePhase += inc;
            if (_tonePhase >= 1f) _tonePhase -= 1f;
            float tone = 0f;
            int H = _harm.Length;
            for (int h = 0; h < H; h++)
            {
                float hz = f0 * (h + 1);
                if (hz > _nyq) break;
                tone += _harm[h] * (1f - _tilt * h / H) * Dsp.Sin01(_tonePhase * (h + 1));
            }

            // valve-train clatter: noise gated by the exhaust pulses, loudest at idle
            float mech = n * _mechGain * (0.3f + _env);

            float eng = exhaust * _exhaustGain + tone * _toneGain + mech;
            eng = _hp.Process(eng);
            eng += P.Formant1Gain * _f1.Process(eng) + P.Formant2Gain * _f2.Process(eng);
            eng = _lp.Process(eng);

            // radial: once-per-rev thump (+ half-order) from the master-rod geometry
            if (_thumpGain > 0f)
            {
                _crankPhase += rpmA / 60f / _fs;
                if (_crankPhase >= 1f) _crankPhase -= 1f;
                eng += _thumpGain * (Dsp.Sin01(_crankPhase) + 0.5f * Dsp.Sin01(_crankPhase * 0.5f));
            }

            // prop: blade-pass thrum + chop AM + tip noise
            _propPhase += rpmA * P.PropReduction * P.PropBlades / 60f / _fs;
            if (_propPhase >= 1f) _propPhase -= 1f;
            float prop = Dsp.Sin01(_propPhase) + 0.35f * Dsp.Sin01(_propPhase * 2f);
            float tip = _tipLp.Process(_noise.Next()) * _tipGain;
            eng = eng * (1f + 0.15f * prop) + prop * _propGain + tip;

            // supercharger whine: pure impeller tone + breathy band of noise around it, slight vibrato
            if (_scGain > 0f)
            {
                _scVibPhase += 5.3f / _fs;
                if (_scVibPhase >= 1f) _scVibPhase -= 1f;
                float whineHz = rpmA * P.SuperchargerRatio / 60f * P.SuperchargerBlades * (1f + 0.004f * Dsp.Sin01(_scVibPhase));
                if (whineHz < _nyq * 0.8f)
                {
                    _scPhase += whineHz / _fs;
                    if (_scPhase >= 1f) _scPhase -= 1f;
                    float wh = 0.7f * Dsp.Sin01(_scPhase);
                    if (whineHz * 2f < _nyq) wh += 0.3f * Dsp.Sin01(_scPhase * 2f);
                    float breath = _scBp.Process(_noise.Next()) * 3f;
                    eng += (wh * 0.55f + breath * 0.8f) * _scGain;
                }
            }

            return eng * P.Level;
        }
    }

    /// <summary>
    /// Turbojet / turbofan: fan (or first-stage compressor) blade-pass whine with a breathy band, shaft-
    /// order hum, broadband roar whose low-pass opens with N1, low rumble for high-bypass fans and a
    /// buzz-saw harmonic stack that appears near full power. N1 spools with the profile's lag.
    /// </summary>
    internal sealed class JetVoice : EngineVoice
    {
        private readonly JetProfile P;
        private readonly float _fs, _nyq, _detune, _spoolA;
        private readonly Noise _noise;
        private float _n1, _n1Target;
        private float _whinePhase, _shaftPhase;
        private float _whineGain, _humGain, _roarGain, _rumbleGain, _buzzGain;
        private readonly Biquad _whineBp = new Biquad(), _roarLp = new Biquad(), _roarBp = new Biquad(), _rumbleLp = new Biquad();

        public JetVoice(JetProfile profile, float fs, uint seed, float detune)
        {
            P = profile;
            _fs = fs;
            _nyq = fs * 0.45f;
            _detune = detune;
            _noise = new Noise(seed);
            _spoolA = Dsp.Tau(P.SpoolS, fs);
            _n1 = P.IdleN1;
            _whineBp.BandPass(fs, 1000f, 10f);
            _roarLp.LowPass(fs, P.RoarLpIdleHz, 0.7f);
            _roarBp.BandPass(fs, 600f, 0.8f);
            _rumbleLp.LowPass(fs, 150f, 0.7f);
        }

        private float N1For(float throttle) => (P.IdleN1 + (1f - P.IdleN1) * Dsp.Clamp01(throttle)) * _detune;

        public override void Snap(float rpm, float throttle) { _n1 = _n1Target = N1For(throttle); }

        public override void Prepare(float rpmTarget, float throttleTarget)
        {
            // The sim reports jet rpm as 0 (no MaxRpm in the config); N1 follows throttle with a spool lag.
            _n1Target = N1For(throttleTarget);
            float n1 = Dsp.Clamp01(_n1);
            float shaftHz = n1 * P.MaxShaftRpm / 60f;
            float whineHz = shaftHz * P.FanBlades;
            if (whineHz > _nyq * 0.8f) whineHz = _nyq * 0.8f;
            _whineBp.BandPass(_fs, whineHz, 10f);
            _roarLp.LowPass(_fs, Dsp.Lerp(P.RoarLpIdleHz, P.RoarLpMaxHz, Dsp.Pow(n1, 1.2f)), 0.7f);

            float n1sq = n1 * n1;
            _whineGain = P.WhineGain * n1sq;
            _humGain = P.HumGain * (0.3f + 0.7f * n1);
            _roarGain = P.RoarGain * (0.08f + 0.92f * Dsp.Pow(n1, 2.5f));
            _rumbleGain = P.RumbleGain * n1sq * 6f;
            _buzzGain = P.BuzzGain * Dsp.SmoothStep(0.72f, 0.95f, n1);
        }

        public override float Next()
        {
            _n1 += _spoolA * (_n1Target - _n1);
            float n1 = _n1;
            float shaftHz = n1 * P.MaxShaftRpm / 60f;

            // whine
            float whineHz = shaftHz * P.FanBlades;
            float whine = 0f;
            if (whineHz < _nyq * 0.8f)
            {
                _whinePhase += whineHz / _fs;
                if (_whinePhase >= 1f) _whinePhase -= 1f;
                whine = 0.6f * Dsp.Sin01(_whinePhase);
                if (whineHz * 2f < _nyq) whine += 0.25f * Dsp.Sin01(_whinePhase * 2f);
            }
            whine += _whineBp.Process(_noise.Next()) * 2.5f;

            // shaft-order hum + buzz-saw stack (same phase accumulator)
            _shaftPhase += shaftHz / _fs;
            if (_shaftPhase >= 1f) _shaftPhase -= 1f;
            float hum = 0.4f * Dsp.Sin01(_shaftPhase) + 0.25f * Dsp.Sin01(_shaftPhase * 2f)
                      + 0.18f * Dsp.Sin01(_shaftPhase * 3f) + 0.12f * Dsp.Sin01(_shaftPhase * 4f);
            float buzz = 0f;
            if (_buzzGain > 0f)
            {
                for (int h = 5; h <= 14; h++)
                {
                    if (shaftHz * h > _nyq) break;
                    buzz += Dsp.Sin01(_shaftPhase * h) / h;
                }
            }

            // roar + rumble
            float n = _noise.Next();
            float roar = _roarLp.Process(n) * 0.7f + _roarBp.Process(n) * 0.6f;
            float rumble = _rumbleLp.Process(_noise.Next());

            return (whine * _whineGain + hum * _humGain + buzz * _buzzGain + roar * _roarGain + rumble * _rumbleGain) * P.Level;
        }
    }

    /// <summary>
    /// All engines of the current aircraft (1 or 2 voices, twins detuned + panned). Built on the main
    /// thread per aircraft and swapped in by reference; the render loop only reads it.
    /// </summary>
    internal sealed class EngineSynth
    {
        private readonly EngineVoice[] _voices;
        private readonly float _mix;
        private float _fade;              // 0..1 fade-in after a swap (hides the cut)
        private readonly float _fadeInc;
        public readonly string ProfileName;

        private EngineSynth(EngineVoice[] voices, string name, float fs)
        {
            _voices = voices;
            ProfileName = name;
            _mix = voices.Length > 1 ? 0.75f : 1f;
            _fadeInc = 1f / (0.12f * fs);
        }

        public static EngineSynth Build(string aircraftId, AircraftConfig config, float fs, float rpm, float throttle)
        {
            string id = aircraftId ?? "";
            int count = (config?.Engines != null && config.Engines.Count >= 2) ? 2 : 1;
            EngineVoice[] voices;
            string name;
            PistonProfile piston = EngineProfiles.PistonFor(id, config);
            // Recorded engines: a real Merlin for the P-51 (Resources/Audio/merlin_<rpm>.wav loops); synth fallback.
            SampleEngineVoice recorded = id.StartsWith("p51") ? SampleEngineVoice.TryBuild("merlin_", fs) : null;
            if (recorded != null)
            {
                voices = new EngineVoice[] { recorded };
                name = "Merlin V-12 (recorded)";
            }
            else if (piston != null)
            {
                voices = new EngineVoice[count];
                for (int i = 0; i < count; i++)
                    voices[i] = new PistonVoice(piston, fs, 0x1234567u + (uint)i * 0x9E3779B9u, i == 0 ? 1f : piston.TwinDetune);
                name = piston.Name;
            }
            else
            {
                JetProfile jet = EngineProfiles.JetFor(id, config);
                if (jet == null) return null;   // glider: no engine voice
                voices = new EngineVoice[count];
                for (int i = 0; i < count; i++)
                    voices[i] = new JetVoice(jet, fs, 0x7654321u + (uint)i * 0x9E3779B9u, i == 0 ? 1f : jet.TwinDetune);
                name = jet.Name;
            }
            if (count == 2)
            {
                voices[0].PanL = 0.8f; voices[0].PanR = 0.45f;
                voices[1].PanL = 0.45f; voices[1].PanR = 0.8f;
            }
            foreach (EngineVoice v in voices) v.Snap(rpm, throttle);
            return new EngineSynth(voices, name, fs);
        }

        public void Prepare(float rpm, float throttle)
        {
            for (int i = 0; i < _voices.Length; i++) _voices[i].Prepare(rpm, throttle);
        }

        public void Next(out float l, out float r)
        {
            l = 0f; r = 0f;
            for (int i = 0; i < _voices.Length; i++)
            {
                EngineVoice v = _voices[i];
                float s = v.Next();
                l += s * v.PanL;
                r += s * v.PanR;
            }
            if (_fade < 1f) _fade += _fadeInc;
            float g = _mix * (_fade < 1f ? _fade : 1f);
            l *= g;
            r *= g;
        }
    }
}

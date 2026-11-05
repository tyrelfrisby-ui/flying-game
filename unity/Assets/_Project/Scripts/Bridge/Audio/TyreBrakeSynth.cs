namespace FlyingGame.Bridge
{
    /// <summary>
    /// The wheels themselves, per side (Core GroundSoundMix decides every number; this only makes the sound):
    ///  - BEARING WHIR: a tone at ~9× the wheel's rev/s with its 2nd and 3rd partials and a little filtered noise, very
    ///    quiet, from the wheel's own spin, so it winds down over half a minute after take-off;
    ///  - SQUEAL (spin-up chirp, a fast skid): a jittery tone with two partials and a narrow band of noise at the pitch
    ///    Core gives (the tyre's size and the patch's slip speed; it glides, so a spin-up falls as the wheel catches up),
    ///    played for exactly the time the tyre was audibly slipping (a 10 ms arrival is a 10 ms chirp);
    ///  - SCRUB (a crabbed touchdown, a tight turn, the tail of a spin-up): broad low noise with a rough, rubbery
    ///    flutter, at Core's centre frequency, for the time the patch slid slowly;
    ///  - BRAKE SCRAPE (pads on the disc, ∝ pressure × wheel speed) and SQUEAL (a slightly wobbling tone on hard braking
    ///    and near lock-up, fading as the wheel stops);
    ///  - a worn or FLAT tyre (Core TyreWear): a low THUMP for the work the axle does on the tread's lumps (it arrives
    ///    when the bad strip passes under the axle, so it is in time with the wheel), a flat casing's SLAP once a
    ///    revolution at the wheel's own rev/s, and the POP of a tyre letting go;
    /// and, centred: water splash / planing hiss / spray (wheels and floats), ski hiss on snow, a float keel's scrape.
    /// Audio thread only, no allocation.
    /// </summary>
    internal sealed class TyreBrakeSynth
    {
        private readonly float _fs;
        private readonly float _smooth, _slowSmooth, _chirpAttack, _chirpRelease;
        private readonly Noise[] _n = { new Noise(0xC41A9u), new Noise(0x7E11Au), new Noise(0xB0A7u), new Noise(0x5EAu) };
        private readonly Biquad[] _whirNoise = { new Biquad(), new Biquad() };
        private readonly Biquad[] _chirpBp = { new Biquad(), new Biquad() };
        private readonly Biquad[] _scrapeBp = { new Biquad(), new Biquad() };
        private readonly Biquad _splashLp = new Biquad(), _hissHp = new Biquad(), _sprayBp = new Biquad(), _skiBp = new Biquad(), _keelBp = new Biquad();
        private readonly float[] _whirPh = new float[2], _whirHz = new float[2], _whirG = new float[2], _whirGT = new float[2];
        private readonly float[] _chirpLeft = new float[2], _chirpEnv = new float[2], _chirpG = new float[2];
        private readonly float[] _chirpHz = new float[2], _chirpHzT = new float[2], _chirpPh = new float[2];
        private readonly OnePole[] _chirpJit = { new OnePole(), new OnePole() };
        private readonly float[] _scrubLeft = new float[2], _scrubEnv = new float[2], _scrubG = new float[2];
        private readonly Biquad[] _scrubBp = { new Biquad(), new Biquad() }, _scrubLp = { new Biquad(), new Biquad() };
        private readonly OnePole[] _scrubFlutter = { new OnePole(), new OnePole() };
        private readonly float _glide, _scrubAttack, _scrubRelease;
        private readonly float[] _scrapeG = new float[2], _scrapeGT = new float[2];
        private readonly float[] _squealPh = new float[2], _squealHz = new float[2], _squealG = new float[2], _squealGT = new float[2], _vibPh = new float[2];
        private float _splashG, _splashGT, _hissG, _hissGT, _sprayG, _sprayGT, _skiG, _skiGT, _keelG, _keelGT;
        private readonly OnePole _lap = new OnePole();   // the splash's slow, irregular swell
        private readonly Biquad[] _thumpBp = { new Biquad(), new Biquad() }, _slapBp = { new Biquad(), new Biquad() }, _popLp = { new Biquad(), new Biquad() };
        private readonly float[] _thumpEnv = new float[2], _thumpPh = new float[2], _flapPh = new float[2], _flapHz = new float[2], _flapG = new float[2];
        private readonly float[] _slapEnv = new float[2], _popEnv = new float[2], _boomPh = new float[2];
        private readonly float _thumpDecay, _slapDecay, _popDecay;

        public TyreBrakeSynth(float fs)
        {
            _fs = fs;
            _smooth = Dsp.Tau(0.03f, fs);
            _slowSmooth = Dsp.Tau(0.12f, fs);
            _chirpAttack = Dsp.Tau(0.002f, fs);
            _chirpRelease = Dsp.Tau(0.012f, fs);
            _glide = Dsp.Tau(0.004f, fs);
            _scrubAttack = Dsp.Tau(0.004f, fs);
            _scrubRelease = Dsp.Tau(0.025f, fs);
            for (int s = 0; s < 2; s++)
            {
                _chirpJit[s].SetCutoff(90f, fs);        // the squeal's pitch wanders ±2 % at stick-slip rates
                _scrubFlutter[s].SetCutoff(35f, fs);    // the scrub's rough, rubbery flutter
                _scrubBp[s].BandPass(fs, 600f, 1.1f);
                _scrubLp[s].LowPass(fs, 260f, 0.7f);
                _chirpHz[s] = _chirpHzT[s] = 1500f;
            }
            for (int s = 0; s < 2; s++) { _whirNoise[s].BandPass(fs, 2000f, 1.2f); _chirpBp[s].BandPass(fs, 2000f, 7f); _scrapeBp[s].BandPass(fs, 500f, 1.5f); }
            _splashLp.LowPass(fs, 450f, 0.7f);
            _lap.SetCutoff(2f, fs);
            _hissHp.HighPass(fs, 3000f, 0.7f);
            _sprayBp.BandPass(fs, 6000f, 0.8f);
            _skiBp.BandPass(fs, 2500f, 0.7f);
            _keelBp.BandPass(fs, 700f, 2f);
            for (int s = 0; s < 2; s++) { _thumpBp[s].LowPass(fs, 220f, 0.8f); _slapBp[s].BandPass(fs, 450f, 1.2f); _popLp[s].LowPass(fs, 2500f, 0.7f); }
            _thumpDecay = Dsp.DecayCoef(0.045f, fs);
            _slapDecay = Dsp.DecayCoef(0.025f, fs);
            _popDecay = Dsp.DecayCoef(0.12f, fs);
        }

        /// <summary>Per block, from the telemetry. <paramref name="chirpNewS"/> is the slip time that arrived since the
        /// last block, per side: the chirp plays it.</summary>
        public void Prepare(float[] whirHz, float[] whirGain, float[] chirpNewS, float[] chirpGain, float[] chirpHz,
                            float[] scrubNewS, float[] scrubGain, float[] scrubHz,
                            float[] scrapeGain, float[] scrapeHz, float[] squealGain, float[] squealHz,
                            float splash, float hiss, float spray, float ski, float keel,
                            float[] thumpNewJ, int[] popsNew, float[] flapHz, float[] flapGain)
        {
            for (int s = 0; s < 2; s++)
            {
                _whirHz[s] = whirHz[s];
                _whirGT[s] = whirGain[s];
                if (chirpNewS[s] > 0f) { _chirpLeft[s] += chirpNewS[s]; _chirpG[s] = chirpGain[s]; }
                if (_chirpLeft[s] > 0.4f) _chirpLeft[s] = 0.4f;   // never queue more than a long skid's worth
                if (chirpHz[s] > 50f)
                {
                    _chirpHzT[s] = chirpHz[s];
                    if (_chirpEnv[s] < 1e-4f) _chirpHz[s] = chirpHz[s];   // a new chirp starts on its own pitch
                    _chirpBp[s].BandPass(_fs, chirpHz[s], 9f);
                }
                if (scrubNewS[s] > 0f) { _scrubLeft[s] += scrubNewS[s]; _scrubG[s] = scrubGain[s]; }
                if (_scrubLeft[s] > 0.6f) _scrubLeft[s] = 0.6f;
                if (scrubHz[s] > 30f) { _scrubBp[s].BandPass(_fs, scrubHz[s], 1.1f); _scrubLp[s].LowPass(_fs, 0.45f * scrubHz[s], 0.7f); }
                _scrapeGT[s] = scrapeGain[s];
                if (scrapeHz[s] > 0f) _scrapeBp[s].BandPass(_fs, scrapeHz[s], 1.5f);
                _squealGT[s] = squealGain[s];
                if (squealHz[s] > 0f) _squealHz[s] = squealHz[s];
                // Thump: the block's work on the lumps kicks a short low knock (level ~ √work against the reference).
                if (thumpNewJ[s] > 0f)
                {
                    float k = (float)System.Math.Sqrt(thumpNewJ[s] / (float)FlyingGame.Core.GroundSoundMix.ThumpRefJ);
                    _thumpEnv[s] = System.Math.Min(1.5f, System.Math.Max(_thumpEnv[s], 0.6f * k));
                }
                if (popsNew[s] > 0) _popEnv[s] = 1f;
                _flapHz[s] = flapHz[s]; _flapG[s] = flapGain[s];
            }
            _splashGT = splash; _hissGT = hiss; _sprayGT = spray; _skiGT = ski; _keelGT = keel;
        }

        public void Next(out float l, out float r)
        {
            l = 0f; r = 0f;
            float dtS = 1f / _fs;
            for (int s = 0; s < 2; s++)
            {
                float y = 0f;
                _whirG[s] += _slowSmooth * (_whirGT[s] - _whirG[s]);
                if (_whirG[s] > 1e-6f && _whirHz[s] > 5f)
                {
                    _whirPh[s] = Dsp.Wrap01(_whirPh[s] + _whirHz[s] / _fs);
                    float p = _whirPh[s];
                    float tone = Dsp.Sin01(p) + 0.5f * Dsp.Sin01(2f * p) + 0.25f * Dsp.Sin01(3f * p);
                    y += _whirG[s] * (tone + 0.3f * _whirNoise[s].Process(_n[s].Next()));
                }
                // Squeal: on while there is slip time left, released over ~12 ms; the pitch glides to Core's over 4 ms.
                float target = _chirpLeft[s] > 0f ? _chirpG[s] : 0f;
                _chirpEnv[s] += (target > _chirpEnv[s] ? _chirpAttack : _chirpRelease) * (target - _chirpEnv[s]);
                if (_chirpLeft[s] > 0f) _chirpLeft[s] -= dtS;
                if (_chirpEnv[s] > 1e-5f)
                {
                    _chirpHz[s] += _glide * (_chirpHzT[s] - _chirpHz[s]);
                    float nz = _n[2].Next();
                    float hz = _chirpHz[s] * (1f + 0.6f * _chirpJit[s].Process(nz));
                    _chirpPh[s] = Dsp.Wrap01(_chirpPh[s] + hz / _fs);
                    float p = _chirpPh[s];
                    float tone = Dsp.Sin01(p) + 0.35f * Dsp.Sin01(2f * p) + 0.15f * Dsp.Sin01(3f * p);
                    y += _chirpEnv[s] * (0.55f * tone + 1.6f * _chirpBp[s].Process(nz));
                }
                // Scrub: the same bookkeeping, a slower release; band noise and its low body under a rough flutter.
                float st = _scrubLeft[s] > 0f ? _scrubG[s] : 0f;
                _scrubEnv[s] += (st > _scrubEnv[s] ? _scrubAttack : _scrubRelease) * (st - _scrubEnv[s]);
                if (_scrubLeft[s] > 0f) _scrubLeft[s] -= dtS;
                if (_scrubEnv[s] > 1e-5f)
                {
                    float nz = _n[1].Next();
                    float flutter = 0.4f + 18f * System.Math.Abs(_scrubFlutter[s].Process(_n[0].Next()));
                    y += _scrubEnv[s] * flutter * (2.6f * _scrubBp[s].Process(nz) + 1.4f * _scrubLp[s].Process(nz));
                }
                _scrapeG[s] += _smooth * (_scrapeGT[s] - _scrapeG[s]);
                if (_scrapeG[s] > 1e-5f) y += _scrapeG[s] * 2.2f * _scrapeBp[s].Process(_n[3].Next());
                _squealG[s] += _smooth * (_squealGT[s] - _squealG[s]);
                if (_squealG[s] > 1e-5f)
                {
                    _vibPh[s] = Dsp.Wrap01(_vibPh[s] + 6.3f / _fs);
                    float hz = _squealHz[s] * (1f + 0.015f * Dsp.Sin01(_vibPh[s]));
                    _squealPh[s] = Dsp.Wrap01(_squealPh[s] + hz / _fs);
                    y += _squealG[s] * (Dsp.Sin01(_squealPh[s]) + 0.15f * Dsp.Sin01(2f * _squealPh[s]));
                }
                // Thump: a decaying ~70 Hz knock with a little low-passed grit.
                if (_thumpEnv[s] > 1e-4f)
                {
                    _thumpPh[s] = Dsp.Wrap01(_thumpPh[s] + 70f / _fs);
                    y += _thumpEnv[s] * (0.8f * Dsp.Sin01(_thumpPh[s]) + 0.6f * _thumpBp[s].Process(_n[0].Next()));
                    _thumpEnv[s] *= _thumpDecay;
                }
                // Flat casing: one slap per revolution of the wheel.
                if (_flapG[s] > 1e-4f && _flapHz[s] > 0.05f)
                {
                    float ph = _flapPh[s] + _flapHz[s] / _fs;
                    if (ph >= 1f) { ph -= 1f; _slapEnv[s] = _flapG[s]; }
                    _flapPh[s] = ph;
                }
                if (_slapEnv[s] > 1e-4f)
                {
                    y += _slapEnv[s] * 1.6f * _slapBp[s].Process(_n[1].Next());
                    _slapEnv[s] *= _slapDecay;
                }
                // Pop: a broadband crack over a falling boom.
                if (_popEnv[s] > 1e-4f)
                {
                    _boomPh[s] = Dsp.Wrap01(_boomPh[s] + (40f + 60f * _popEnv[s]) / _fs);
                    y += _popEnv[s] * (1.2f * _popLp[s].Process(_n[3].Next()) + 0.9f * Dsp.Sin01(_boomPh[s]));
                    _popEnv[s] *= _popDecay;
                }
                if (s == 0) l += y; else r += y;
            }
            _splashG += _smooth * (_splashGT - _splashG);
            _hissG += _smooth * (_hissGT - _hissG);
            _sprayG += _smooth * (_sprayGT - _sprayG);
            _skiG += _smooth * (_skiGT - _skiG);
            _keelG += _smooth * (_keelGT - _keelG);
            float c = 0f;
            if (_splashG > 1e-5f)
            {
                // Water lapping at a hull swells and falls irregularly. This voice used to be gated by a 4.5 Hz sine
                // (66 % depth), a steady thump-thump four and a half times a second through every taxi and the start of
                // every water take-off: Ty's "hammering noise" (2026-11-05). Now the swell is low-passed noise, ~15 %
                // depth, with no period (tools/FloatAudio measures it).
                float lap = _lap.Process(_n[3].Next());
                c += _splashG * 2.5f * _splashLp.Process(_n[0].Next()) * (0.66f + 12.5f * lap);   // rms as the old pulsed voice (0.66 = its rms gain)
            }
            if (_hissG > 1e-5f) c += _hissG * 1.2f * _hissHp.Process(_n[1].Next());
            if (_sprayG > 1e-5f) c += _sprayG * 0.9f * _sprayBp.Process(_n[2].Next());
            if (_skiG > 1e-5f) c += _skiG * 1.4f * _skiBp.Process(_n[3].Next());
            if (_keelG > 1e-5f) c += _keelG * 2.0f * _keelBp.Process(_n[0].Next());
            l += c * 0.7071f; r += c * 0.7071f;
        }
    }
}

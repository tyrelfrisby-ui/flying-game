namespace FlyingGame.Bridge
{
    /// <summary>
    /// The wheels themselves, per side (Core GroundSoundMix decides every number; this only makes the sound):
    ///  - BEARING WHIR: a tone at ~9× the wheel's rev/s with its 2nd and 3rd partials and a little filtered noise, very
    ///    quiet, from the wheel's own spin, so it winds down over half a minute after take-off;
    ///  - SPIN-UP CHIRP / SKID: resonant noise whose pitch falls as the slip closes, played for exactly the time the tyre
    ///    was audibly slipping (a 10 ms greaser-free arrival is a 10 ms chirp);
    ///  - BRAKE SCRAPE (pads on the disc, ∝ pressure × wheel speed) and SQUEAL (a slightly wobbling tone on hard braking
    ///    and near lock-up, fading as the wheel stops);
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
        private readonly float[] _scrapeG = new float[2], _scrapeGT = new float[2];
        private readonly float[] _squealPh = new float[2], _squealHz = new float[2], _squealG = new float[2], _squealGT = new float[2], _vibPh = new float[2];
        private float _splashG, _splashGT, _hissG, _hissGT, _sprayG, _sprayGT, _skiG, _skiGT, _keelG, _keelGT, _sloshPh;

        public TyreBrakeSynth(float fs)
        {
            _fs = fs;
            _smooth = Dsp.Tau(0.03f, fs);
            _slowSmooth = Dsp.Tau(0.12f, fs);
            _chirpAttack = Dsp.Tau(0.002f, fs);
            _chirpRelease = Dsp.Tau(0.012f, fs);
            for (int s = 0; s < 2; s++) { _whirNoise[s].BandPass(fs, 2000f, 1.2f); _chirpBp[s].BandPass(fs, 2000f, 7f); _scrapeBp[s].BandPass(fs, 500f, 1.5f); }
            _splashLp.LowPass(fs, 450f, 0.7f);
            _hissHp.HighPass(fs, 3000f, 0.7f);
            _sprayBp.BandPass(fs, 6000f, 0.8f);
            _skiBp.BandPass(fs, 2500f, 0.7f);
            _keelBp.BandPass(fs, 700f, 2f);
        }

        /// <summary>Per block, from the telemetry. <paramref name="chirpNewS"/> is the slip time that arrived since the
        /// last block, per side: the chirp plays it.</summary>
        public void Prepare(float[] whirHz, float[] whirGain, float[] chirpNewS, float[] chirpGain, float[] chirpSlip,
                            float[] scrapeGain, float[] scrapeHz, float[] squealGain, float[] squealHz,
                            float splash, float hiss, float spray, float ski, float keel)
        {
            for (int s = 0; s < 2; s++)
            {
                _whirHz[s] = whirHz[s];
                _whirGT[s] = whirGain[s];
                if (chirpNewS[s] > 0f) { _chirpLeft[s] += chirpNewS[s]; _chirpG[s] = chirpGain[s]; }
                if (_chirpLeft[s] > 0.4f) _chirpLeft[s] = 0.4f;   // never queue more than a long skid's worth
                _chirpBp[s].BandPass(_fs, 1500f + 2200f * Dsp.Clamp01(chirpSlip[s] * 3f), 7f);
                _scrapeGT[s] = scrapeGain[s];
                if (scrapeHz[s] > 0f) _scrapeBp[s].BandPass(_fs, scrapeHz[s], 1.5f);
                _squealGT[s] = squealGain[s];
                if (squealHz[s] > 0f) _squealHz[s] = squealHz[s];
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
                    y += _whirG[s] * (tone + 0.6f * _whirNoise[s].Process(_n[s].Next()));
                }
                // Chirp: on while there is slip time left, released over ~12 ms.
                float target = _chirpLeft[s] > 0f ? _chirpG[s] : 0f;
                _chirpEnv[s] += (target > _chirpEnv[s] ? _chirpAttack : _chirpRelease) * (target - _chirpEnv[s]);
                if (_chirpLeft[s] > 0f) _chirpLeft[s] -= dtS;
                if (_chirpEnv[s] > 1e-5f) y += _chirpEnv[s] * 3.5f * _chirpBp[s].Process(_n[2].Next());
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
                _sloshPh = Dsp.Wrap01(_sloshPh + 4.5f / _fs);
                c += _splashG * 2.5f * _splashLp.Process(_n[0].Next()) * (0.6f + 0.4f * Dsp.Sin01(_sloshPh));
            }
            if (_hissG > 1e-5f) c += _hissG * 1.2f * _hissHp.Process(_n[1].Next());
            if (_sprayG > 1e-5f) c += _sprayG * 0.9f * _sprayBp.Process(_n[2].Next());
            if (_skiG > 1e-5f) c += _skiG * 1.4f * _skiBp.Process(_n[3].Next());
            if (_keelG > 1e-5f) c += _keelG * 2.0f * _keelBp.Process(_n[0].Next());
            l += c * 0.7071f; r += c * 0.7071f;
        }
    }
}

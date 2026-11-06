namespace FlyingGame.Bridge
{
    /// <summary>
    /// Airflow over the airframe: two decorrelated broadband noise streams low-passed with a cutoff and
    /// level that rise with airspeed (silent below ~10 m/s, dominant above 60), gust modulation from the
    /// turbulence setting, a fluttering whistle that grows with |AoA| / |beta| (panned to the slip side),
    /// a shuddering stall-buffet rumble above ~14° AoA and a spoiler roar while spoilers are out. A type's spoiler
    /// voice can be quieter and carry a low wake rumble under the roar (SpoilerConfig.SoundLevel / SoundRumble; the
    /// 2-33's, Ty 2026-11-05 16:06: "a little too loud and it should have a bit more rumble to it").
    /// </summary>
    internal sealed class WindSynth
    {
        private readonly float _fs;
        private readonly Noise _nL = new Noise(0xA51CE5u), _nR = new Noise(0xB0BCA7u), _nS = new Noise(0xC0FFEEu);
        // The spoiler rumble's own noise and its wake's random walk: separate streams, so a type without a rumble
        // hears every other voice sample for sample as before.
        private readonly Noise _nRumble = new Noise(0xD1BE5Au), _nWake = new Noise(0x5EAB0Au);
        private readonly Biquad _lpL = new Biquad(), _lpR = new Biquad();
        private readonly Biquad _whistle = new Biquad(), _buffet = new Biquad(), _spoiler = new Biquad(), _rumble = new Biquad();
        private readonly float _smoothA;

        // block targets (T) and per-sample smoothed values
        private float _level, _levelT;
        private float _gust, _gustT, _gustDepth;
        private float _whGain, _whGainT, _whPanL = 0.7f, _whPanR = 0.7f;
        private float _bfGain, _bfGainT;
        private float _spGain, _spGainT, _spRoar = 1f, _spRumble, _spRumbleT;
        private float _wake, _wakeT;
        private float _flutPhase, _flutHz = 7f;
        private int _shudCount; private float _shudEnv, _shudDecay, _drumEnv, _drumDecay, _drumPhase, _bfDepth, _drumTarget, _drumAttack;

        public WindSynth(float fs)
        {
            _fs = fs;
            _smoothA = Dsp.Tau(0.03f, fs);
            _shudDecay = 1f - 1f / (0.045f * fs);
            _drumDecay = 1f - 1f / (0.28f * fs);    // long ~280 ms roll so successive thuds overlap
            _drumAttack = 1f / (0.025f * fs);
            _lpL.LowPass(fs, 400f, 0.7f);
            _lpR.LowPass(fs, 400f, 0.7f);
            _whistle.BandPass(fs, 900f, 5f);
            _buffet.LowPass(fs, 45f, 0.9f);        // pre-stall buffet body: sub-bass rumble under the roll
            _spoiler.BandPass(fs, 650f, 0.8f);
            _rumble.BandPass(fs, RumbleHz, 0.7f);
        }

        /// <summary>The rumble's band: low enough to read as a rumble under the 650 Hz roar, high enough that a phone's
        /// speaker (which gives little below ~150 Hz) still plays it.</summary>
        public const float RumbleHz = 140f;
        /// <summary>Rumble amplitude per unit of SoundRumble, against the roar's 2.5: at SoundRumble 1 its RMS is about
        /// half the stock roar's (measured by tools/SpoilerAudio, which prints both).</summary>
        public const float RumbleGain = 2.4f;

        /// <summary>The type's spoiler voice (main thread → audio thread, each float written whole): the roar's level
        /// (1 = the stock voice) and how much wake rumble is under it (0 = none, the stock voice).</summary>
        public void SetSpoilerVoice(float roarLevel, float rumble)
        {
            _spRoar = Dsp.Clamp(roarLevel, 0f, 2f);
            _spRumbleT = Dsp.Clamp(rumble, 0f, 2f);
        }

        /// <summary>Per-block parameter update (audio thread).</summary>
        /// <param name="cockpitAir">The open cockpit's extra wind (Core Vr.CockpitWind amplitude above a shut canopy's; 1 =
        /// an open cockpit at 100 KIAS): added to the broadband level, its gusts deepened. 0 with the canopy shut.</param>
        public void Prepare(float ias, float alphaDeg, float betaDeg, float turbulence, float windMs, float spoiler, bool powered, float cockpitAir = 0f)
        {
            float v = ias < 0f ? 0f : ias;

            // broadband: audible from ~10 m/s, glider-cruise presence by 25, full roar past 100
            float lvl = 0.12f * Dsp.SmoothStep(8f, 30f, v) + 0.5f * Dsp.Pow(Dsp.Clamp01((v - 20f) / 100f), 1.3f);
            if (powered) lvl *= 0.65f;   // leave room for the engine
            // The slipstream in an open cockpit: amplitude ∝ q (CockpitWind), a third of the full-scale roar per unit.
            float air = Dsp.Clamp(cockpitAir, 0f, 3f);
            lvl += 0.3f * air;
            _levelT = lvl;
            float cutoff = 150f + 32f * v;
            if (cutoff > 6000f) cutoff = 6000f;
            _lpL.LowPass(_fs, cutoff, 0.7f);
            _lpR.LowPass(_fs, cutoff * 1.04f, 0.7f);

            // gusts: slow random walk, depth from the turbulence setting + steady wind
            _gustDepth = Dsp.Clamp(0.06f + 0.12f * turbulence + 0.003f * windMs + 0.15f * air, 0f, 0.6f);
            _gustT += _nS.Next() * 0.25f - _gustT * 0.06f;
            _gustT = Dsp.Clamp(_gustT, -1f, 1f);

            // whistle / flutter from angle of attack and sideslip
            float aoaTerm = Dsp.SmoothStep(4f, 14f, alphaDeg < 0f ? -alphaDeg : alphaDeg);
            float slipTerm = Dsp.SmoothStep(3f, 15f, betaDeg < 0f ? -betaDeg : betaDeg);
            _whGainT = (0.6f * aoaTerm + slipTerm) * Dsp.SmoothStep(12f, 45f, v) * 0.18f;
            float whHz = 500f + 18f * v;
            if (whHz > 4500f) whHz = 4500f;
            _whistle.BandPass(_fs, whHz, 5f);
            float pan = Dsp.Clamp(betaDeg / 20f, -1f, 1f);   // +beta: relative wind from the right
            _whPanL = 0.7f - 0.3f * pan;
            _whPanR = 0.7f + 0.3f * pan;
            _flutHz = 5f + 9f * aoaTerm + 4f * slipTerm;

            // Stall buffet: a drumbeat that starts slow and soft as the wing approaches the stall and BUILDS in both
            // beat rate and intensity up to the stall (owner). Onset ~11° AoA, full at ~16.5° (negative: -7°..-12°).
            float buffet = Dsp.SmoothStep(11f, 16.5f, alphaDeg);
            float negBuffet = Dsp.SmoothStep(7f, 12f, -alphaDeg);
            if (negBuffet > buffet) buffet = negBuffet;
            _bfDepth = buffet;
            _bfGainT = Dsp.Pow(buffet, 1.6f) * Dsp.SmoothStep(8f, 25f, v) * 0.85f;

            // spoiler / airbrake roar
            _spGainT = Dsp.Clamp01(spoiler) * Dsp.SmoothStep(10f, 35f, v) * 0.35f;
            // The wake behind the plates is turbulent: the rumble swells and sags as a random walk (no LFO, which a
            // listener hears as a metronome; CLAUDE.md "a periodic modulation on a sound the physics drives").
            if (_spRumbleT > 0f)
            {
                _wakeT += _nWake.Next() * 0.35f - _wakeT * 0.15f;
                _wakeT = Dsp.Clamp(_wakeT, -1f, 1f);
            }
        }

        public void Next(out float l, out float r)
        {
            _level += _smoothA * (_levelT - _level);
            _gust += _smoothA * (_gustT - _gust);
            _whGain += _smoothA * (_whGainT - _whGain);
            _bfGain += _smoothA * (_bfGainT - _bfGain);
            _spGain += _smoothA * (_spGainT - _spGain);
            _spRumble += _smoothA * (_spRumbleT - _spRumble);
            _wake += _smoothA * (_wakeT - _wake);

            // Buffet shudder: irregular thuds 8–14 times a second (the wake is turbulent, not a metronome), each a
            // short decaying burst; no modulation of the airflow hiss itself (that read as a weird warble).
            if (--_shudCount <= 0)
            {
                float rate = (6f + 10f * _bfDepth) * (0.8f + 0.4f * (_nS.Next() * 0.5f + 0.5f));  // 6/s at onset → 16/s at the stall: the thuds overlap into a roll
                _shudCount = (int)(_fs / rate * (0.7f + 0.6f * (_nS.Next() * 0.5f + 0.5f)));
                _shudEnv = 0.6f + 0.4f * (_nS.Next() * 0.5f + 0.5f);
                _drumTarget = _shudEnv;   // soft attack: the drum swells to this over ~25 ms instead of hitting
            }
            _drumEnv += (_drumTarget - _drumEnv) * _drumAttack;
            _drumTarget *= _drumDecay;
            _shudEnv *= _shudDecay;
            // Drum head: each thud rings a damped low sine whose pitch drops as it decays (a bass drum, not a rattle).
            float drumHz = (24f + 8f * _bfDepth) + 12f * _drumEnv;    // distant thunder: 24–44 Hz, rolling, no sharp edge
            _drumPhase += drumHz / _fs;
            if (_drumPhase >= 1f) _drumPhase -= 1f;

            float g = _level * (1f + _gustDepth * _gust);
            float bl = _lpL.Process(_nL.Next());
            float br = _lpR.Process(_nR.Next());

            float ns = _nS.Next();
            _flutPhase += _flutHz / _fs;
            if (_flutPhase >= 1f) _flutPhase -= 1f;
            float flut = 0.6f + 0.4f * Dsp.Sin01(_flutPhase);
            float wh = _whistle.Process(ns) * 3f * _whGain * flut;
            float bf = (Dsp.Sin01(_drumPhase) * _drumEnv * 2.2f + _buffet.Process(ns) * 4f * _shudEnv) * _bfGain;   // thunder roll + sub rumble
            float sp = _spoiler.Process(ns) * 2.5f * _spRoar * _spGain;
            if (_spRumble > 1e-4f) sp += _rumble.Process(_nRumble.Next()) * RumbleGain * _spRumble * (1f + 0.5f * _wake) * _spGain;

            l = bl * g + wh * _whPanL + bf + sp;
            r = br * g + wh * _whPanR + bf + sp;
        }
    }
}

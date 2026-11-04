using FlyingGame.Core;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// What the tyres sound like rolling over the ground, one voice per <see cref="RollingVoice"/> per side, driven by
    /// Core's <see cref="GroundSoundMix"/> (which ground is under which wheel, crossfaded, at what speed). Each voice is
    /// the same small recipe with its own numbers (<see cref="Recipes"/>): a band of noise whose centre follows the
    /// speed, a shower of grains (stones, crust, joints) at a rate that rises with speed, and low thuds at the lump
    /// spacing. Audio thread only, no allocation.
    /// </summary>
    internal sealed class SurfaceSynth
    {
        private struct Recipe
        {
            public float BandHz, BandQ, BandMix;      // the continuous body of the sound
            public float GrainPerM, GrainHz, GrainQ, GrainDecayS, GrainMix;   // grains per metre rolled
            public float ThudPerM, ThudMix;           // lumps per metre rolled (low thud)
        }

        // Indexed by RollingVoice. None is silent.
        private static readonly Recipe[] Recipes =
        {
            default,
            // Tarmac: a low tyre hum and the expansion joints / crack sealant ticking past every ~6 m.
            new Recipe { BandHz = 320, BandQ = 0.8f, BandMix = 1.0f, GrainPerM = 0.16f, GrainHz = 900, GrainQ = 1.5f, GrainDecayS = 0.012f, GrainMix = 0.6f },
            // Grass: a soft swish of blades, and the field's bumps.
            new Recipe { BandHz = 3200, BandQ = 0.6f, BandMix = 0.7f, ThudPerM = 0.5f, ThudMix = 0.6f },
            // Gravel: the stones grinding under the tyre, and their crackle (the old SurfaceSynth's gravel).
            new Recipe { BandHz = 1800, BandQ = 0.9f, BandMix = 0.9f, GrainPerM = 4.0f, GrainHz = 3800, GrainQ = 2.0f, GrainDecayS = 0.004f, GrainMix = 1.6f },
            // Dirt: a soft rumble with a few crackles.
            new Recipe { BandHz = 220, BandQ = 0.7f, BandMix = 1.0f, GrainPerM = 0.6f, GrainHz = 2200, GrainQ = 1.5f, GrainDecayS = 0.006f, GrainMix = 0.8f, ThudPerM = 0.3f, ThudMix = 0.4f },
            // Hard sand: a hiss / shush.
            new Recipe { BandHz = 2600, BandQ = 0.5f, BandMix = 1.0f },
            // Rough pasture: the gear hammering over lumps every ~3 m, and a rattle (the old SurfaceSynth's rough).
            new Recipe { BandHz = 160, BandQ = 0.7f, BandMix = 0.4f, ThudPerM = 0.35f, ThudMix = 1.6f, GrainPerM = 0.4f, GrainHz = 1200, GrainQ = 1.0f, GrainDecayS = 0.01f, GrainMix = 0.5f },
            // Snow (wheels): a crunch of crust breaking under the tyre.
            new Recipe { BandHz = 1100, BandQ = 0.8f, BandMix = 0.5f, GrainPerM = 9.0f, GrainHz = 1500, GrainQ = 1.2f, GrainDecayS = 0.006f, GrainMix = 1.4f },
            // Ice: a thin hiss and the odd crack of the weathered crust.
            new Recipe { BandHz = 4200, BandQ = 0.9f, BandMix = 0.7f, GrainPerM = 0.3f, GrainHz = 5000, GrainQ = 3.0f, GrainDecayS = 0.008f, GrainMix = 0.7f },
            // A ship's deck: hollow, a drum under the tyres, and the plate seams.
            new Recipe { BandHz = 110, BandQ = 2.5f, BandMix = 1.4f, GrainPerM = 0.25f, GrainHz = 600, GrainQ = 2.5f, GrainDecayS = 0.03f, GrainMix = 0.7f },
        };

        private const int Voices = 10;   // RollingVoice members
        private readonly float _fs, _smoothA;
        private readonly Noise[] _n = new Noise[2 * Voices], _ng = new Noise[2 * Voices];
        private readonly Biquad[] _band = new Biquad[2 * Voices], _grain = new Biquad[2 * Voices], _thud = new Biquad[2 * Voices];
        private readonly float[] _gain = new float[2 * Voices], _gainT = new float[2 * Voices];
        private readonly float[] _grainRate = new float[Voices], _thudRate = new float[Voices];
        private readonly float[] _grainEnv = new float[2 * Voices], _thudEnv = new float[2 * Voices], _thudPhase = new float[2 * Voices];
        private readonly float[] _grainDecay = new float[Voices];
        private readonly float _thudDecay;

        public SurfaceSynth(float fs)
        {
            _fs = fs;
            _smoothA = Dsp.Tau(0.05f, fs);
            _thudDecay = 1f - 1f / (0.06f * fs);
            for (int i = 0; i < 2 * Voices; i++)
            {
                _n[i] = new Noise(0x6A7E15u + (uint)i * 7919u);
                _ng[i] = new Noise(0x51C0DEu + (uint)i * 104729u);
                _band[i] = new Biquad(); _grain[i] = new Biquad(); _thud[i] = new Biquad();
                Recipe r = Recipes[i % Voices];
                _band[i].BandPass(fs, r.BandHz > 0 ? r.BandHz : 1000f, r.BandQ > 0 ? r.BandQ : 1f);
                _grain[i].BandPass(fs, r.GrainHz > 0 ? r.GrainHz : 1000f, r.GrainQ > 0 ? r.GrainQ : 1f);
                _thud[i].LowPass(fs, 90f, 0.8f);
            }
            for (int v = 0; v < Voices; v++) _grainDecay[v] = Recipes[v].GrainDecayS > 0 ? 1f - 1f / (Recipes[v].GrainDecayS * fs) : 0f;
        }

        /// <summary>Per block: each voice's gain per side and pitch (from GroundSoundMix, summed over the keys that use
        /// the voice), and the ground speed that sets the grain and lump rates.</summary>
        public void Prepare(float[] voiceGainLR, float[] voicePitch, float speedMs)
        {
            float v = speedMs < 0f ? 0f : speedMs;
            for (int k = 1; k < Voices; k++)
            {
                Recipe r = Recipes[k];
                float pitch = voicePitch[k] > 0.05f ? voicePitch[k] : 1f;
                _grainRate[k] = r.GrainPerM * v;
                _thudRate[k] = r.ThudPerM * v;
                for (int s = 0; s < 2; s++)
                {
                    int i = s * Voices + k;
                    _gainT[i] = voiceGainLR[i];
                    if (_gainT[i] > 1e-4f || _gain[i] > 1e-4f) _band[i].BandPass(_fs, r.BandHz * pitch, r.BandQ);
                }
            }
        }

        public void Next(out float l, out float r)
        {
            l = 0f; r = 0f;
            for (int i = 0; i < 2 * Voices; i++)
            {
                int k = i % Voices;
                if (k == 0) continue;
                _gain[i] += _smoothA * (_gainT[i] - _gain[i]);
                if (_gain[i] < 1e-5f) continue;
                Recipe rc = Recipes[k];
                float n = _n[i].Next();
                float y = _band[i].Process(n) * 1.6f * rc.BandMix;
                if (rc.GrainMix > 0f)
                {
                    if (_ng[i].Next01() < _grainRate[k] / _fs) _grainEnv[i] = 0.6f + 0.4f * _ng[i].Next01();
                    _grainEnv[i] *= _grainDecay[k];
                    y += _grain[i].Process(n * _grainEnv[i]) * 4f * rc.GrainMix;
                }
                if (rc.ThudMix > 0f)
                {
                    _thudPhase[i] += _thudRate[k] / _fs;
                    if (_thudPhase[i] >= 1f) { _thudPhase[i] -= 1f; _thudEnv[i] = 0.5f + 0.5f * _ng[i].Next01(); }
                    _thudEnv[i] *= _thudDecay;
                    y += _thud[i].Process(_ng[i].Next() * _thudEnv[i]) * 6f * rc.ThudMix;
                }
                y *= _gain[i];
                if (i < Voices) l += y; else r += y;
            }
        }
    }
}

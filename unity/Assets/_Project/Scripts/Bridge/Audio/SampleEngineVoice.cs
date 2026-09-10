using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A RECORDED engine (owner 2026-09-09: "an authentic Merlin"): seamless loops of a real Rolls-Royce Merlin cut from
    /// a CC0 static-run recording (Freesound #276597 by squashy555, public domain) at several rpm. The two loops nearest
    /// the live rpm play back resampled to it (a four-stroke's firing rate scales with rpm, so the harmonics land
    /// right) and crossfade by rpm; throttle sets the exhaust loudness and opens the tone (the P-51's governor holds
    /// rpm, so power is heard as loudness). Under it, what the static run lacks: the four-blade prop's blade-pass thrum
    /// (0.479 reduction) and the two-stage supercharger's whine (5.8:1 step-up, ten-vane impeller), both by boost.
    /// Loops live in Resources/Audio/merlin_&lt;rpm&gt;.wav and are decoded once on the main thread.
    /// </summary>
    internal sealed class SampleEngineVoice : EngineVoice
    {
        private sealed class Loop { public float[] Data; public float Rpm; public double Pos; }

        private readonly Loop[] _loops;                 // ascending rpm
        private readonly float _fs, _lagA;
        private float _rpm, _thr, _rpmT, _thrT;
        private float _propPhase, _scPhase;
        private readonly Noise _noise = new Noise(0x3E71A9u);
        private readonly OnePole _tone = new OnePole();
        private readonly Biquad _scBp = new Biquad();
        private readonly float _clipFs;

        public const float PropReduction = 0.479f, PropBlades = 4f, SuperchargerStepUp = 5.8f, ImpellerVanes = 10f;

        private SampleEngineVoice(Loop[] loops, float clipFs, float fs)
        {
            _loops = loops; _clipFs = clipFs; _fs = fs;
            _lagA = Dsp.Tau(0.25f, fs);
            _tone.SetCutoff(2500f, fs);
            _scBp.BandPass(fs, 2500f, 8f);
        }

        /// <summary>Main thread only (loads clips). Null when no loops are found — the caller falls back to the synth.</summary>
        public static SampleEngineVoice TryBuild(string resourcePrefix, float fs)
        {
            var loops = new List<Loop>();
            float clipFs = 0f;
            foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Audio"))
            {
                if (clip == null || !clip.name.StartsWith(resourcePrefix)) continue;
                string tail = clip.name.Substring(resourcePrefix.Length);
                if (!float.TryParse(tail, out float rpm) || rpm <= 0f) continue;
                var data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0)) { Debug.LogWarning($"SampleEngineVoice: {clip.name} not readable (set Load Type = Decompress On Load)."); continue; }
                float[] mono = data;
                if (clip.channels > 1)
                {
                    mono = new float[clip.samples];
                    for (int i = 0; i < clip.samples; i++) { float s = 0f; for (int c = 0; c < clip.channels; c++) s += data[i * clip.channels + c]; mono[i] = s / clip.channels; }
                }
                loops.Add(new Loop { Data = mono, Rpm = rpm });
                clipFs = clip.frequency;
            }
            if (loops.Count == 0) return null;
            loops.Sort((a, b) => a.Rpm.CompareTo(b.Rpm));
            Debug.Log($"SampleEngineVoice: {loops.Count} loops for '{resourcePrefix}' ({string.Join(", ", loops.ConvertAll(l => l.Rpm.ToString("F0")))} rpm)");
            return new SampleEngineVoice(loops.ToArray(), clipFs, fs);
        }

        public override void Snap(float rpm, float throttle) { _rpm = _rpmT = rpm; _thr = _thrT = throttle; }

        public override void Prepare(float rpmTarget, float throttleTarget)
        {
            _rpmT = rpmTarget; _thrT = Mathf.Clamp01(throttleTarget);
            // Tone opens with power: a Merlin at boost is all bark; at idle it burbles.
            _tone.SetCutoff(900f + 5000f * _thr, _fs);
            float scHz = Mathf.Max(200f, _rpm / 60f * SuperchargerStepUp * ImpellerVanes);   // ~2560 Hz at 2650 rpm
            _scBp.BandPass(_fs, Mathf.Min(scHz, _fs * 0.45f), 9f);
        }

        public override float Next()
        {
            _rpm += _lagA * (_rpmT - _rpm);
            _thr += _lagA * (_thrT - _thr);
            if (_rpm < 50f) return 0f;   // engine stopped

            // Pick the two loops bracketing the rpm; crossfade between them, each resampled to the live rpm.
            int hi = 0;
            while (hi < _loops.Length - 1 && _loops[hi].Rpm < _rpm) hi++;
            int lo = hi > 0 ? hi - 1 : 0;
            float mix = hi == lo ? 0f : Mathf.Clamp01((_rpm - _loops[lo].Rpm) / (_loops[hi].Rpm - _loops[lo].Rpm));
            float s = Read(_loops[lo]) * (1f - mix) + (hi != lo ? Read(_loops[hi]) * mix : 0f);
            // Keep every loop's read head moving so the crossfade never lands on a stale position.
            for (int i = 0; i < _loops.Length; i++) if (i != lo && i != hi) Advance(_loops[i]);

            // Loudness by throttle (the governor holds rpm; power is heard, not seen): idle quiet, boost loud.
            float g = 0.28f + 0.72f * _thr;
            float exhaust = _tone.Process(s) * g;

            // Prop blade-pass thrum and tip noise (4 blades through the 0.479 reduction): ~85 Hz at 2650 rpm.
            float bladeHz = _rpm / 60f * PropReduction * PropBlades;
            _propPhase += bladeHz / _fs; if (_propPhase >= 1f) _propPhase -= 1f;
            float prop = Dsp.Sin01(_propPhase) * (0.05f + 0.10f * _thr);

            // Supercharger whine: a thin whistle that rises with boost.
            float whine = _scBp.Process(_noise.Next()) * (0.02f + 0.10f * _thr * _thr);

            return exhaust + prop + whine;
        }

        private float Read(Loop l)
        {
            float[] d = l.Data;
            int n = d.Length;
            int i0 = (int)l.Pos; float frac = (float)(l.Pos - i0);
            int i1 = i0 + 1; if (i1 >= n) i1 -= n;
            float s = d[i0] + (d[i1] - d[i0]) * frac;
            Advance(l);
            return s;
        }

        private void Advance(Loop l)
        {
            // Playback rate: rpm ratio × sample-rate ratio (the clip's rate vs the output rate).
            l.Pos += (_rpm / l.Rpm) * (_clipFs / _fs);
            if (l.Pos >= l.Data.Length) l.Pos -= l.Data.Length;
        }
    }
}

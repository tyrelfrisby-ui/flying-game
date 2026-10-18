using FlyingGame.Core.Nav;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The sound of a four-course radio range in your headphones: a 1,020 Hz tone keyed by the interlocking
    /// A and N of <see cref="AnRangeSignal.Keying"/>.
    ///
    /// This matters more than it looks. A range has no needle — the instrument IS the sound, and the pilot's
    /// job is to keep the note from breaking up. Drawing "you hear an A" on the screen would be describing
    /// the instrument instead of giving it to you, so the tone is generated for real: on the course it is one
    /// unbroken note, a few degrees off you hear a letter rising over it, and well into a quadrant it is a
    /// clean dot-dash or dash-dot with silence between.
    ///
    /// Built the way the rest of the project's procedural audio is built (see FlightAudio): an AudioSource
    /// playing a silent looping clip, and <see cref="OnAudioFilterRead"/> overwriting the buffer on the audio
    /// thread. Nothing here touches the flight synth, so a bug in it cannot take the engine note with it.
    /// </summary>
    public sealed class RangeAudio : MonoBehaviour
    {
        /// <summary>Set from the main thread each frame: the envelope's inputs. Volatile-ish by being plain floats.</summary>
        private volatile float _offCourseDeg;
        private volatile bool _aQuadrant;
        private volatile bool _live;
        private volatile float _gain;

        private double _phase;        // tone phase, audio thread only
        private double _t;            // keying clock, audio thread only
        private float _smoothed;      // amplitude smoothing, audio thread only
        private int _fs = 48000;

        private AudioSource _source;
        private AudioClip _silence;

        /// <summary>Main thread: what the receiver is reading right now. <paramref name="live"/> false = silent.</summary>
        public void Set(bool live, double offCourseDeg, bool aQuadrant, float gain)
        {
            _live = live;
            _offCourseDeg = (float)offCourseDeg;
            _aQuadrant = aQuadrant;
            _gain = Mathf.Clamp01(gain);
        }

        private void Awake()
        {
            _fs = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            _source = gameObject.AddComponent<AudioSource>();
            _silence = AudioClip.Create("RangeSilence", 4096, 1, _fs, false);
            _source.clip = _silence;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;        // in the headphones, not out in the world
            _source.volume = 1f;
            _source.Play();
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            bool live = _live;
            float gain = _gain * FlightAudio.MasterVolume * 0.18f;   // a radio in headphones, not an engine
            float off = _offCourseDeg;
            bool aq = _aQuadrant;
            double dt = 1.0 / _fs;
            double w = 2.0 * System.Math.PI * AnRangeSignal.ToneHz / _fs;

            for (int i = 0; i < data.Length; i += channels)
            {
                _t += dt;
                double want = live ? AnRangeSignal.Keying(_t, off, aq) * gain : 0.0;
                // Five-millisecond smoothing on the key, or every dot edge clicks.
                _smoothed += (float)(want - _smoothed) * 0.004f * _fs / 1000f * 0.25f;
                _phase += w;
                if (_phase > 2 * System.Math.PI) _phase -= 2 * System.Math.PI;
                float v = _smoothed * (float)System.Math.Sin(_phase);
                for (int c = 0; c < channels; c++) data[i + c] = v;
            }
        }
    }
}

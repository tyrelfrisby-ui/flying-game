using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Sits on the child GameObject that carries the AudioSource, so the DSP callback is isolated from
    /// whatever else lives on the aircraft root. Unity calls OnAudioFilterRead on the audio thread while
    /// the source plays its silent looping clip; we overwrite the buffer with the synthesized mix.
    /// </summary>
    internal sealed class FlightAudioOutput : MonoBehaviour
    {
        internal FlightAudio Owner;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            FlightAudio owner = Owner;
            if (ReferenceEquals(owner, null)) return;   // no UnityEngine.Object == on the audio thread
            owner.Render(data, channels);
        }
    }
}

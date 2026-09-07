using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Sets the active turbulence level — the ONE field both the aircraft (via Atmosphere) and the
    /// bubble field read, so what you see the bubbles doing is exactly what the wings feel. Press T to
    /// cycle Calm → Light → Moderate → Severe. The bubble field is handed the same Turbulence instance
    /// so its visual gusts and the aircraft's buffeting are the identical air.
    /// </summary>
    public sealed class WeatherController : MonoBehaviour
    {
        public BubbleField Bubbles;

        private static readonly string[] Levels = { "Calm", "Light", "Moderate", "Severe" };
        private int _level;

        public string CurrentLevel => Levels[_level];

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                _level = (_level + 1) % Levels.Length;
                Apply();
            }
        }

        private void Apply()
        {
            Turbulence t = _level switch
            {
                1 => Turbulence.Light(),
                2 => Turbulence.Moderate(),
                3 => Turbulence.Severe(),
                _ => null,
            };
            Atmosphere.ActiveTurbulence = t;
            if (Bubbles != null)
            {
                Bubbles.Turbulence = t;
            }
        }
    }
}

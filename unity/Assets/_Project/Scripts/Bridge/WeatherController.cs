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

        // Steady wind: headwind/tailwind/crosswind. K cycles direction relative to the runway
        // (calm / headwind / tailwind / left-cross / right-cross); [ and ] trim its strength.
        private static readonly (string Name, float DirDeg)[] WindDirs =
        {
            ("Calm", 0), ("Headwind", 180), ("Tailwind", 0), ("Left X-wind", 90), ("Right X-wind", 270),
        };
        private int _windDir;
        private float _windSpeed = 8f;
        public string WindLabel => _windDir == 0 ? "Calm" : $"{WindDirs[_windDir].Name} {_windSpeed:F0} m/s";

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                _level = (_level + 1) % Levels.Length;
                Apply();
            }
            if (Input.GetKeyDown(KeyCode.K))
            {
                _windDir = (_windDir + 1) % WindDirs.Length;
                ApplyWind();
            }
            if (Input.GetKeyDown(KeyCode.LeftBracket)) { _windSpeed = Mathf.Max(0, _windSpeed - 2); ApplyWind(); }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { _windSpeed = Mathf.Min(20, _windSpeed + 2); ApplyWind(); }
        }

        private void ApplyWind()
        {
            if (_windDir == 0) { Atmosphere.SteadyWind = FlyingGame.Core.MathTypes.Vec3.Zero; return; }
            // Runway is along +x (sim north). Wind blows FROM DirDeg toward the aircraft.
            float rad = WindDirs[_windDir].DirDeg * Mathf.Deg2Rad;
            // sim frame: x fwd, y right. Wind vector (world) = speed in the blowing-toward direction.
            var wind = new FlyingGame.Core.MathTypes.Vec3(
                -_windSpeed * Mathf.Cos(rad), -_windSpeed * Mathf.Sin(rad), 0);
            Atmosphere.SteadyWind = wind;
            if (Bubbles != null) Bubbles.ApplyWind(CoordinateMap.ToUnity(wind) * Time.deltaTime * 0f); // origin drift handled per-frame below
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

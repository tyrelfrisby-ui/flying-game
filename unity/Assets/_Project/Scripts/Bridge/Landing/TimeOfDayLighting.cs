using FlyingGame.Core.Landing;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Draws the landing page's time of day (Ty 2026-11-05: "time of day ... light dark feedback"). Lighting only.
    ///
    /// MIDDAY IS THE SCENE AS IT WAS BUILT (SceneBootstrap.BuildSun: 55° up, from the south-east, 1.1): nothing changes
    /// for anybody who never touches the setting. Morning and afternoon swing that sun round to the east and west and
    /// lower it, with a warmer, dimmer light; sunset is the existing preset (SunsetLighting.Apply). Switching back
    /// restores what the scene had, read once the first time this runs.
    /// </summary>
    public static class TimeOfDayLighting
    {
        private static bool _saved, _applied;
        private static Quaternion _rot; private static Color _col; private static float _int;
        private static AmbientMode _amb; private static Color _sky, _eq, _gnd; private static Material _skybox;

        public static TimeOfDay Current { get; private set; } = TimeOfDay.Midday;

        /// <summary>(sun elevation °, azimuth the sun is IN °, light colour, intensity) for a preset other than sunset.</summary>
        public static (float elev, float az, Color color, float intensity) SunFor(TimeOfDay t) => t switch
        {
            TimeOfDay.Morning => (22f, 100f, new Color(1f, 0.90f, 0.78f), 0.95f),
            TimeOfDay.Afternoon => (30f, 245f, new Color(1f, 0.93f, 0.82f), 1.0f),
            _ => (55f, 145f, Color.white, 1.1f),
        };

        public static Light Sun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) return l;
            return null;
        }

        public static void Apply(TimeOfDay t)
        {
            Light sun = Sun();
            if (sun == null) return;
            if (System.Environment.GetEnvironmentVariable("AERO_SUNSET") == "1") return;   // a capture asked for sunset: leave it
            if (!_saved)
            {
                _saved = true; _rot = sun.transform.rotation; _col = sun.color; _int = sun.intensity;
                _amb = RenderSettings.ambientMode; _sky = RenderSettings.ambientSkyColor; _eq = RenderSettings.ambientEquatorColor;
                _gnd = RenderSettings.ambientGroundColor; _skybox = RenderSettings.skybox;
            }
            if (_applied && t == Current) return;
            _applied = true; Current = t;
            // Back to the scene's own first, so each preset starts from the same place.
            sun.transform.rotation = _rot; sun.color = _col; sun.intensity = _int;
            RenderSettings.ambientMode = _amb; RenderSettings.ambientSkyColor = _sky; RenderSettings.ambientEquatorColor = _eq;
            RenderSettings.ambientGroundColor = _gnd;
            Shader.SetGlobalFloat("_SunsetOn", 0);
            if (_skybox != null) RenderSettings.skybox = _skybox;
            if (t == TimeOfDay.Midday) return;
            if (t == TimeOfDay.Sunset) { SunsetLighting.Apply(sun, false); return; }
            var (elev, az, color, intensity) = SunFor(t);
            // A directional light shines along its forward: point it FROM the sun's place in the sky, down.
            sun.transform.rotation = Quaternion.Euler(elev, az + 180f, 0f);
            sun.color = color; sun.intensity = intensity;
            Shader.SetGlobalVector("_BubbleSunDir", -sun.transform.forward);
        }
    }
}

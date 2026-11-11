using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    // Opt-in built-in-pipeline preset. Does not modify weather or simulation signals.
    public static class SunsetLighting
    {
        // Reuses the included sky shader; no cubemap, cloud mesh or extra render pass.
        // Public for editor fixtures (EditorTools is a separate assembly).
        // Same atmosphere on every tier; Low skips the finest cloud noise octave.
        public static void ConfigureDayAtmosphere(string tier = null, float density = .000052f, float clearRange = 850f)
        {
            string quality = tier ?? QualitySettings.names[QualitySettings.GetQualityLevel()];
            bool low = quality.IndexOf("low", System.StringComparison.OrdinalIgnoreCase) >= 0
                || quality.IndexOf("fast", System.StringComparison.OrdinalIgnoreCase) >= 0;
            Shader.SetGlobalVector("_DayAtmosphere", new Vector4(Mathf.Max(0, density), Mathf.Max(0, clearRange), low ? 0 : 1, 0));
        }

        public static void ApplyDay()
        {
            Shader.SetGlobalFloat("_CityNight", 0);
            Shader.SetGlobalFloat("_SunsetOn", 0);
            ConfigureDayAtmosphere();
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(.4f, .8f, -.4f).normalized);
            if (RenderSettings.skybox == null || RenderSettings.skybox.shader.name != "FlyingGame/SunsetSky")
                RenderSettings.skybox = new Material(Shader.Find("FlyingGame/SunsetSky"));
        }

        public static void Apply(Light sun, bool calm)
        {
            var direction = new Vector3(.18f, .045f, .983f).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-direction);
            sun.color = new Color(1f, .66f, .38f); sun.intensity = .85f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.38f, .32f, .46f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .34f, .30f);
            RenderSettings.ambientGroundColor = new Color(.30f, .29f, .34f);
            Shader.SetGlobalFloat("_SunsetOn", 1);
            // THE WATER IS NOT THE LIGHTING'S TO DECIDE any more. _WaterCalm used to be set here from this flag,
            // which is how a lake came to look the same in a dead calm and in 25 knots: the only thing that could
            // change it was which lighting preset you called. The sea state now comes from the wind, through
            // SeaSurfaceDriver -> SeaState (Ty, 2026-11-11: "water texture should change with wind ... use real
            // physics"). `calm` therefore only means what it says: put the water in a dead calm, a mirror. A
            // non-calm sunset leaves whatever the wind has made of the water alone.
            if (calm) SeaSurface.Calm();
            Shader.SetGlobalVector("_SunsetSunDir", direction);
            Shader.SetGlobalVector("_BubbleSunDir", direction);
            RenderSettings.skybox = new Material(Shader.Find("FlyingGame/SunsetSky"));
        }
    }
}

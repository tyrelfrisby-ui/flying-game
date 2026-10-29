using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    // Opt-in built-in-pipeline preset. Does not modify weather or simulation signals.
    public static class SunsetLighting
    {
        // Reuses the included sky shader; no cubemap, cloud mesh or extra render pass.
        public static void ApplyDay()
        {
            Shader.SetGlobalFloat("_SunsetOn", 0);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(.4f, .8f, -.4f).normalized);
            if (RenderSettings.skybox == null || RenderSettings.skybox.shader.name != "FlyingGame/SunsetSky")
                RenderSettings.skybox = new Material(Shader.Find("FlyingGame/SunsetSky"));
        }

        public static void Apply(Light sun, bool calm)
        {
            var direction = new Vector3(-.24f, .045f, .97f).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-direction);
            sun.color = new Color(1f, .66f, .38f); sun.intensity = .85f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.38f, .32f, .46f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .34f, .30f);
            RenderSettings.ambientGroundColor = new Color(.30f, .29f, .34f);
            Shader.SetGlobalFloat("_SunsetOn", 1);
            Shader.SetGlobalFloat("_WaterCalm", calm ? 1 : 0);
            Shader.SetGlobalVector("_SunsetSunDir", direction);
            Shader.SetGlobalVector("_BubbleSunDir", direction);
            RenderSettings.skybox = new Material(Shader.Find("FlyingGame/SunsetSky"));
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    // Opt-in built-in-pipeline preset. Does not modify weather or simulation signals.
    public static class SunsetLighting
    {
        public static void Apply(Light sun, bool calm)
        {
            var direction = new Vector3(-.24f, .045f, .97f).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-direction);
            sun.color = new Color(1f, .66f, .38f); sun.intensity = .85f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.38f, .32f, .46f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .34f, .30f);
            RenderSettings.ambientGroundColor = new Color(.10f, .13f, .17f);
            Shader.SetGlobalFloat("_SunsetOn", 1);
            Shader.SetGlobalFloat("_WaterCalm", calm ? 1 : 0);
            Shader.SetGlobalVector("_SunsetSunDir", direction);
            Shader.SetGlobalVector("_BubbleSunDir", direction);
            RenderSettings.skybox = new Material(Shader.Find("FlyingGame/SunsetSky"));
        }
    }
}

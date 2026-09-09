using System.IO;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>Headless aerial renders of the generated world (terrain steps, water, airports) to build/world/*.png.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.WorldRender.Render  (no -nographics)</summary>
    public static class WorldRender
    {
        [MenuItem("FlyingGame/Render World")]
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/world"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            FlyingGame.Core.WorldTerrain.Active = new FlyingGame.Core.WorldTerrain();
            WorldBuilder.BuildAll();

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 50f; cam.nearClipPlane = 1f; cam.farClipPlane = 60000f;
            var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt;

            (string name, Vector3 pos, Vector3 look)[] views =
            {
                ("overview", new Vector3(9000f, 9000f, -9000f), new Vector3(-3500f, 800f, 1500f)),
                ("valley-airport", new Vector3(1200f, 700f, -1600f), new Vector3(0f, 0f, 400f)),
                ("first-wall", new Vector3(2500f, 1500f, -2500f), new Vector3(-1800f, 450f, 1000f)),
                ("summit-airport", new Vector3(-7000f, 3500f, -1800f), new Vector3(-8900f, 2700f, 400f)),
                ("stol-strip", new Vector3(600f, 120f, -50f), new Vector3(420f, 0f, 250f)),
                ("river-lake", new Vector3(3500f, 1400f, -3200f), new Vector3(2400f, 850f, 1500f)),
                ("race-course", new Vector3(1950f, 520f, 250f), new Vector3(1950f, 40f, 1200f)),
                ("race-gate", new Vector3(1800f, 45f, 650f), new Vector3(1800f, 40f, 860f)),
                ("aero-box", new Vector3(1000f, 700f, -900f), new Vector3(1000f, 300f, 400f)),
                ("bridge", new Vector3(-150f, 120f, 2200f), new Vector3(-150f, 0f, 1900f)),
                ("crop-field", new Vector3(2050f, 90f, -300f), new Vector3(1750f, 25f, 400f)),
                ("crop-wires", new Vector3(1450f, 25f, 100f), new Vector3(1750f, 30f, 191f)),
                ("grass-strip", new Vector3(-435f, 5f, -180f), new Vector3(-420f, 1f, 350f)),
                ("arch", new Vector3(-1250f, 120f, 1300f), new Vector3(-900f, 120f, 1740f)),
                ("eiffel", new Vector3(1700f, 120f, -2500f), new Vector3(1300f, 120f, -1750f)),
                ("town", new Vector3(0f, 400f, -3700f), new Vector3(0f, 60f, -1750f)),
                ("town-street", new Vector3(10f, 40f, -3000f), new Vector3(10f, 60f, -1500f)),
                // Plunge waterfall at the first wall (sim x 1849, lip y −2002): front, from inside the slot, and the lip.
                ("waterfall-front", new Vector3(-1350f, 520f, 2350f), new Vector3(-1985f, 380f, 1849f)),
                ("waterfall-slot", new Vector3(-2030f, 350f, 1480f), new Vector3(-2030f, 330f, 2200f)),
                ("waterfall-lip", new Vector3(-1900f, 880f, 1700f), new Vector3(-2010f, 780f, 1849f)),
            };
            foreach ((string name, Vector3 pos, Vector3 look) in views)
            {
                cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            RenderTexture.active = null;
            Debug.Log($"WorldRender: wrote {views.Length} images to {outDir}");
        }
    }
}

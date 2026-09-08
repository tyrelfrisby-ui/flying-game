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
                ("race-course", new Vector3(2800f, 900f, -600f), new Vector3(2800f, 30f, 1500f)),
                ("race-gate", new Vector3(2000f, 60f, -120f), new Vector3(2000f, 15f, 0f)),
                ("aero-box", new Vector3(1200f, 900f, -3400f), new Vector3(1200f, 0f, -2200f)),
                ("bridge", new Vector3(-150f, 120f, 2450f), new Vector3(-150f, 0f, 2150f)),
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

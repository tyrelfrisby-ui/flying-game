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
            FlyingGame.Core.WorldClock.TimeS = 20;
            WorldBuilder.BuildAll();

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 50f; cam.nearClipPlane = 1f; cam.farClipPlane = 60000f;
            var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt;

            (string name, Vector3 pos, Vector3 look)[] views =
            {
                ("island-overview", new Vector3(18000, 18000, -21000), new Vector3(0, 500, 0)),
                ("island-topdown", new Vector3(0, 44000, 0), new Vector3(0, 0, 1)),
                ("mokuleia", new Vector3(7000, 2200, 9000), new Vector3(5000, 200, 5500)),
                ("mccall-crags", new Vector3(-3500, 4200, 6000), new Vector3(-600, 1700, 2400)),
                ("alaska-ruth", new Vector3(-8500, 3500, 8000), new Vector3(-3500, 900, 4700)),
                ("carson-drake", new Vector3(-5500, 1900, -3000), new Vector3(-4900, 1450, -3900)),
                ("drake-interior", new Vector3(-4900, 1450, -3900), new Vector3(-4900, 1460, -4350)),
                ("powell", new Vector3(6500, 2300, -6500), new Vector3(4475, 1150, -4525)),
                ("oc-city", new Vector3(6200, 1500, -9200), new Vector3(4800, 300, -6600)),
                ("catalina", new Vector3(4500, 1700, -17500), new Vector3(3500, 300, -14000)),
                ("pirate-mouth", new Vector3(2500, 35, -14720), new Vector3(2750, 15, -14570)),
                ("pirate-interior", new Vector3(2790, 18, -14550), new Vector3(2850, 10, -14510)),
                ("sitka-volcano", new Vector3(-17000, 1900, -500), new Vector3(-14000, 400, 2800)),
                ("airpark-payette", new Vector3(-2200, 1800, 3900), new Vector3(-1600, 1500, 4600)),
                ("ab1-rafts", new Vector3(6800, 1000, 6600), new Vector3(6900, 0, 7600)),
                ("ab2-rafts", new Vector3(-13700, 1500, -7500), new Vector3(-12200, 0, -6300)),
                ("ab3-mccall", new Vector3(-3100, 2400, 1700), new Vector3(-1800, 1524, 2800)),
                ("cz5-land", new Vector3(16900, 500, 2000), new Vector3(17300, 30, 2400)),
            };
            // Moving scenery doesn't Update in an editor render: pose the carrier for its views.
            var carrier = Object.FindFirstObjectByType<CarrierRuntime>();
            if (carrier != null)
            {
                var (cx, cy, ch, _) = FlyingGame.Core.Carrier.PoseAt(20);
                carrier.transform.SetPositionAndRotation(WorldBuilder.U(cx, cy, 0), Quaternion.Euler(0f, (float)(ch * Mathf.Rad2Deg), 0f));
            }
            string onlyViews = System.Environment.GetEnvironmentVariable("WORLD_VIEWS");
            foreach ((string name, Vector3 pos, Vector3 look) in views)
            {
                if (!string.IsNullOrEmpty(onlyViews) && !onlyViews.Contains(name)) continue;
                cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
                // The forest is streamed round the camera in the game, and nothing streams in a batch render,
                // so build the timber round whatever this view is LOOKING AT and throw it away afterwards.
                // (Unity x is the sim's y and Unity z is the sim's x.)
                var forest = new GameObject("ForestView");
                ForestBuilder.BuildAround(FlyingGame.Core.WorldTerrain.Active, forest.transform, look.z, look.x, 2200, 700);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(forest);
            }
            RenderTexture.active = null;
            Debug.Log($"WorldRender: wrote {views.Length} images to {outDir}");
        }
    }
}

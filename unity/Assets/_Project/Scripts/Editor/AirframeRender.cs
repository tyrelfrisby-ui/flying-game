using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Headless preview of every aircraft's generated airframe: builds each type with AirframeBuilder
    /// in an empty scene and renders 3/4, top and side views to build/airframes/*.png. Lets the shapes
    /// be checked against 3-view drawings without a device.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.AirframeRender.Render
    /// (needs graphics — do NOT pass -nographics).
    /// </summary>
    public static class AirframeRender
    {
        private static readonly string[] Ids =
        {
            "glider-2-33-like", "c172-like", "pitts-s2b-like", "stearman-pt17-like",
            "extra-300-like", "p51d-like", "f86-sabre-like", "seminole-like",
            "dc3-like", "boeing-737-like", "pa18-cub-like", "decathlon-8kcab-like", "pa18-bush-like", "pa18-floats-like", "pa25-pawnee-like", "glider-eb29r-like", "glider-swift-s1-like", "cassutt-f1-like", "geebee-r2-like", "glasair3-like", "pa28-archer-like", "cirrus-sr22-like", "hughes-h4-like", "dhc2-beaver-floats-like",
        };

        [MenuItem("FlyingGame/Render Airframes")]
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/airframes"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            var rt = new RenderTexture(1280, 900, 24);
            cam.targetTexture = rt;

            // Ground disc so the gear/shadow relationship reads.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.42f, 0.55f, 0.28f) };

            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            var builder = new AirframeBuilder();
            foreach (string id in Ids)
            {
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                var root = new GameObject(id);
                float half = builder.Build(root.transform, cfg);
                float size = Mathf.Max(half * 2f, 8f);
                // Sit the aircraft on its wheels: lowest gear point (sim +z down) → ground.
                float lowest = 0f;
                foreach (GearConfig g in cfg.Gear) lowest = Mathf.Max(lowest, (float)g.Pos[2]);
                root.transform.position = new Vector3(0f, lowest + 0.02f, 0f);
                ground.transform.localScale = Vector3.one * size;

                (string name, Vector3 dir, bool ortho)[] views =
                {
                    ("iso", new Vector3(1.2f, 0.7f, 1.0f), false),
                    ("top", new Vector3(0.001f, 1f, 0f), true),
                    ("side", new Vector3(1f, 0.05f, 0f), true),
                    ("front", new Vector3(0f, 0.08f, 1f), true),
                };
                // Neutral views, then (for the glider) all controls deflected positive + spoilers out, to
                // check hinge directions: +aileron = roll right (left TE down, right TE up), +elevator =
                // TE down, +rudder = TE right, spoilers up.
                for (int pass = 0; pass < (id == "glider-2-33-like" ? 2 : 1); pass++)
                {
                builder.SetDeflections(pass == 1 ? 0.35f : 0f, pass == 1 ? 0.35f : 0f, pass == 1 ? 0.35f : 0f, pass == 1 ? 1f : 0f);
                foreach ((string name, Vector3 dir, bool ortho) in views)
                {
                    cam.orthographic = ortho;
                    cam.orthographicSize = size * 0.6f;
                    Vector3 target = root.transform.position + Vector3.up * 0.5f;
                    cam.transform.position = target + dir.normalized * size * 1.8f;
                    cam.transform.LookAt(target, Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"{id}-{(pass == 1 ? "defl-" : "")}{name}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                }
                if (id == "glider-2-33-like")
                {
                    // Numeric hinge-direction check: +elevator must move the elevator's TE DOWN (Unity -y),
                    // +rudder must move the rudder's TE RIGHT (Unity +x), spoilers must rise (+y).
                    Vector3 Centre(string n)
                    {
                        Vector3 c = Vector3.zero; int k = 0;
                        foreach (Transform t in root.transform)
                            if (t.name.StartsWith(n)) foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>()) { c += mf.GetComponent<Renderer>().bounds.center; k++; }
                        return k > 0 ? c / k : c;
                    }
                    builder.SetDeflections(0f, 0f, 0f, 0f);
                    Vector3 e0 = Centre("elevator"), r0 = Centre("rudder"), s0 = Centre("Spoiler");
                    builder.SetDeflections(0f, 0.5f, 0.5f, 1f);
                    Vector3 e1 = Centre("elevator"), r1 = Centre("rudder"), s1 = Centre("Spoiler");
                    Debug.Log($"HINGECHECK elevator Δy={e1.y - e0.y:F3} (want <0)  rudder Δx={r1.x - r0.x:F3} (want >0)  spoiler Δy={s1.y - s0.y:F3} (want >0)");
                    builder.SetDeflections(0f, 0f, 0f, 0f);
                }
                builder.Clear();
                Object.DestroyImmediate(root);
            }
            RenderTexture.active = null;
            Debug.Log($"AirframeRender: wrote {Ids.Length * 4} images to {outDir}");
        }
    }
}

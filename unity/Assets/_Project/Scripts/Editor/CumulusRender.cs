using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Clouds;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// The thermal cumulus (Bridge/Cumulus.cs + Shaders/Cumulus.shader), rendered in the world it actually ships in —
    /// the island, with the CloudPlayground banks and the painted sky deck in the same frames, so the three cloud
    /// implementations can be judged against each other in ONE picture rather than three (Ty 2026-11-11: "there are
    /// clouds that look good realistic and there are clouds that look fake like a bunch of circles glued together").
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.CumulusRender.Render -outDir DIR
    /// (no -nographics: it renders.)
    /// </summary>
    public static class CumulusRender
    {
        static string _out;
        static Camera _cam;
        static RenderTexture _rt;
        static CloudPlayground _cp;

        [MenuItem("FlyingGame/Render Cumulus")]
        public static void Render()
        {
            var args = System.Environment.GetCommandLineArgs();
            int oi = System.Array.IndexOf(args, "-outDir");
            _out = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/cumulus"));
            Directory.CreateDirectory(_out);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            WorldTerrain.Active = new WorldTerrain();
            WorldClock.TimeS = 20;
            Atmosphere.SteadyWind = Vec3.Zero;
            Atmosphere.Thermals.Clear();
            WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            SunsetLighting.ConfigureDayAtmosphere("Balanced", .000052f, 850f);

            // The same sun SceneBootstrap.BuildSun hangs up, so the shading in these frames is the shading in the game.
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f;
            sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            // The thermals and their cumulus, exactly as SoaringScenery lays them at play start. Timed, because the
            // meshes are built on the CPU in the startup frame and the Quest's first frame is already 3.5-7.4 s
            // (docs/quest-performance.md); a cloud that looks right but costs a second of launch is not a win.
            var build = System.Diagnostics.Stopwatch.StartNew();
            SoaringScenery.BuildThermals();
            double buildMs = build.Elapsed.TotalMilliseconds;

            var camGo = new GameObject("RenderCam");
            camGo.tag = "MainCamera";                      // Cumulus.LateUpdate reads Camera.main for the whiteout
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            _cam.depthTextureMode = DepthTextureMode.Depth;
            _cam.nearClipPlane = 1f; _cam.farClipPlane = 60000f;
            _rt = new RenderTexture(1600, 1000, 24);
            _cam.targetTexture = _rt;
            _cp = Object.FindFirstObjectByType<CloudPlayground>();

            var report = new System.Text.StringBuilder();
            report.AppendLine($"thermals {Atmosphere.Thermals.Count}; every cloud's mesh built in {buildMs:F0} ms "
                            + $"({buildMs / System.Math.Max(1, Atmosphere.Thermals.Count):F1} ms each) on this Mac");

            // The first thermal over the default airport: the one a glider flying from the home field works first.
            Thermal th = Atmosphere.Thermals[0];
            float baseY = (float)th.TopAltitudeM;
            float radius = (float)(th.CoreRadiusM * 1.8 * 1.05);
            float tall = Mathf.Lerp(0.6f, 1.05f, Mathf.InverseLerp(4f, 14f, (float)th.CoreUpdraftMs));
            float height = 2f * radius * tall;
            Vector3 c = CoordinateMap.ToUnity(th.CoreAt(th.TopAltitudeM * 0.98));
            c.y = baseY;
            report.AppendLine($"cumulus 0 at {c}, base {baseY:F0} m, radius {radius:F0} m, height {height:F0} m, core {th.CoreUpdraftMs:F1} m/s");

            // Looking NORTH-EAST from the south-west so the sun (azimuth −35°, elevation 55°) is behind the camera's
            // shoulder: the lit side faces us, which is how a cumulus is usually seen and the hardest test of its shape.
            Vector3 lit = new Vector3(-0.72f, 0f, -0.69f);
            Shot("cumulus_1500m_lit", c + lit * 1500f + Vector3.up * (height * 0.35f), c + Vector3.up * (height * 0.45f), 42);
            Shot("cumulus_3km_lit", c + lit * 3000f + Vector3.up * (height * 0.30f), c + Vector3.up * (height * 0.45f), 34);
            // Backlit from the other side: the silhouette alone, nothing but the outline to look at.
            Shot("cumulus_1500m_backlit", c - lit * 1500f + Vector3.up * (height * 0.35f), c + Vector3.up * (height * 0.45f), 42);
            // From below the condensation level, the pilot's usual view of one.
            Shot("cumulus_under_base", c + lit * 700f - Vector3.up * 500f, c + Vector3.up * (height * 0.2f), 60);
            // Above it, looking down on the crown.
            Shot("cumulus_above_crown", c + lit * 1100f + Vector3.up * (height + 700f), c + Vector3.up * (height * 0.7f), 50);

            // The comparison shot: the thermal cumulus in the near field with a CloudPlayground bank behind it. The
            // banks stand on rings 6 / 11 / 16.5 km from the island centre, so sight along the line from the cloud
            // AWAY from the centre and a bank lands in the background.
            Vector3 outward = new Vector3(c.x, 0, c.z).normalized;
            if (outward.sqrMagnitude < 0.5f) outward = Vector3.right;
            Shot("compare_cumulus_vs_banks", c - outward * 2200f + Vector3.up * (height * 0.35f), c + Vector3.up * (height * 0.45f), 55);

            report.AppendLine(Cost(c, height));
            File.WriteAllText(Path.Combine(_out, "cumulus-render-report.txt"), report.ToString());
            Debug.Log("[CumulusRender] done\n" + report);
        }

        /// <summary>What the thermal cumulus cost the phone: the geometry every one of them submits (this is static
        /// mesh, built once and never touched again, so vertices and triangles ARE the budget), and the GPU time for
        /// one frame with them and one without — measured by rendering the same camera 60 times either way.</summary>
        static string Cost(Vector3 cloud, float height)
        {
            var sb = new System.Text.StringBuilder();
            var clouds = new System.Collections.Generic.List<MeshRenderer>();
            GameObject root = GameObject.Find("Clouds");
            if (root != null)
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) clouds.Add(r);
            int verts = 0, tris = 0;
            foreach (var r in clouds)
            {
                Mesh m = r.GetComponent<MeshFilter>().sharedMesh;
                if (m == null) continue;
                verts += m.vertexCount; tris += m.triangles.Length / 3;
            }
            sb.AppendLine($"cumulus geometry: {clouds.Count} clouds, {verts} vertices, {tris} triangles "
                        + $"({(clouds.Count > 0 ? verts / clouds.Count : 0)} v / {(clouds.Count > 0 ? tris / clouds.Count : 0)} t each), "
                        + $"{verts * 24.0 / 1024 / 1024:F1} MiB of vertex buffer (pos + normal + colour)");

            // A cloud filling most of the frame is the overdraw worst case: stand off one by half its height.
            _cam.fieldOfView = 60;
            _cam.transform.position = cloud + new Vector3(0, height * 0.4f, -height * 0.5f);
            _cam.transform.rotation = Quaternion.LookRotation(cloud + Vector3.up * height * 0.45f - _cam.transform.position, Vector3.up);
            Cumulus.StepForTesting();
            var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
            double Frame(int n)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < n; i++)
                {
                    _cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = _rt;
                    px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = prev;
                }
                return sw.Elapsed.TotalMilliseconds / n;
            }
            // Interleaved, because a Mac GPU's clock drifts over a run and a straight A-then-B reads the drift as
            // the clouds' cost. Three passes each way, best of three: the cheapest pass is the one with the least
            // other work in it.
            Frame(20);
            double with = 1e9, without = 1e9;
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var r in clouds) r.enabled = true;
                with = System.Math.Min(with, Frame(40));
                foreach (var r in clouds) r.enabled = false;
                without = System.Math.Min(without, Frame(40));
            }
            foreach (var r in clouds) r.enabled = true;
            Object.DestroyImmediate(px);
            sb.AppendLine($"frame at 1600x1000 on this Mac: {with:F2} ms with the cumulus, {without:F2} ms without "
                        + $"(delta {with - without:+0.00;-0.00} ms) — a desktop GPU, so read the DELTA, not the absolute");
            return sb.ToString();
        }

        static void Shot(string name, Vector3 eye, Vector3 look, float fov)
        {
            _cam.fieldOfView = fov;
            _cam.transform.position = eye;
            _cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
            Cumulus.StepForTesting();
            if (_cp != null) for (int i = 0; i < 3; i++) _cp.StepForTesting(_cam, 10f + i * 0.1f, 0.1f);
            Save(name);
        }

        static void Save(string name)
        {
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_out, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[CumulusRender] {name}");
        }
    }
}

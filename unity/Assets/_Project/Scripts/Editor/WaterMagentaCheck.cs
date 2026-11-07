using System;
using System.IO;
using System.Reflection;
using System.Text;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-05: magenta blotches over the water. Builds the real island world, flies a camera low over the
    /// island's first float lane looking down it (the view in his clip), runs the planar reflection the game runs, and writes
    /// the frame and the reflection target. Fails on magenta pixels in either, and on any renderer the reflection camera
    /// would draw with an unsupported or missing shader, and on any water a builder left off the Water layer (the cause:
    /// the mirror camera drew the water into its own target, and its shader read that target while writing it).
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.WaterMagentaCheck.Run   (no -nographics)
    /// Output: build/water-magenta/
    /// </summary>
    public static class WaterMagentaCheck
    {
        private static object Call(object o, string name, params object[] args) =>
            o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(o, args);

        public static void Run()
        {
            int fails = 0;
            var log = new StringBuilder();
            void Fail(string m) { fails++; log.AppendLine("FAIL " + m); Debug.LogError("[WaterMagenta] FAIL " + m); }
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/water-magenta"));
            try
            {
                Directory.CreateDirectory(output);
                if (SystemInfo.graphicsDeviceType.ToString().Contains("Null")) throw new InvalidOperationException("needs a GPU: run without -nographics");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                WorldTerrain.Active = new WorldTerrain();
                Shader.SetGlobalFloat("_SunsetOn", 0); Shader.SetGlobalFloat("_WaterCalm", 0);
                WorldBuilder.BuildAll();
                SunsetLighting.ConfigureDayAtmosphere("Balanced", 0.000052f, 850f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(45, -35, 0);
                Shader.SetGlobalVector("_BubbleSunDir", new Vector3(.4f, .8f, -.4f));

                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("pitts-s2b-like");
                var root = new GameObject("Pitts");
                var driver = root.AddComponent<FlightSimDriver>(); driver.enabled = false;
                // The -crashtest water start: the island's first float lane, the water in Ty's clip.
                var state = CrashScenario.PittsWaterStart(cfg);
                driver.AdoptSim(new SimLoop(new Aircraft(cfg, state, ControlDeflections.Neutral)));
                root.transform.position = CoordinateMap.ToUnity(state.Position);
                var builder = new AirframeBuilder(); builder.Build(root.transform, cfg);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = WaterReflection.AircraftLayer;

                var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
                cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
                cam.nearClipPlane = .3f; cam.farClipPlane = 20000; cam.fieldOfView = 60;
                var reflection = cam.gameObject.AddComponent<WaterReflection>();
                reflection.enabled = false; reflection.Driver = driver; Call(reflection, "Awake");
                const int W = 1600, H = 1050;
                var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt; cam.aspect = (float)W / H;

                // The cause on 2026-11-05: a builder that leaves water off the Water layer, so the mirror draws the water into
                // its own target. Checked before WaterReflection runs, because its guard would quietly fix it.
                int waters = 0;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.shader != null && m.shader.name == "FlyingGame/Water")
                        {
                            waters++;
                            if (r.gameObject.layer != WaterReflection.WaterLayer) Fail($"water '{r.name}' built on layer {r.gameObject.layer}, not {WaterReflection.WaterLayer}");
                            break;
                        }
                log.AppendLine($"{waters} water renderers checked for the Water layer");
                if (waters == 0) Fail("the world built no water");
                // Renderers the mirrored camera draws, by shader.
                int mask = (int)typeof(WaterReflection).GetProperty("Mask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reflection);
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.enabled || ((1 << r.gameObject.layer) & mask) == 0) continue;
                    string why = WaterReflection.Suspect(r);
                    if (why != null) Fail($"{r.name}: {why}");
                }

                double water = WorldTerrain.Active.WaterSurfaceAt(state.Position.X, state.Position.Y, -state.Position.Z) ?? -state.Position.Z;
                Vec3 along = state.Attitude.Rotate(new Vec3(1, 0, 0)); along = new Vec3(along.X, along.Y, 0) / Math.Sqrt(along.X * along.X + along.Y * along.Y);
                Vector3 surface = CoordinateMap.ToUnity(new Vec3(state.Position.X - 60 * along.X, state.Position.Y - 60 * along.Y, -water));
                Vector3 far = CoordinateMap.ToUnity(new Vec3(state.Position.X + 600 * along.X, state.Position.Y + 600 * along.Y, -water));
                // After a water break-up the wreck also lays a fuel sheen (FuelSlickVisual) on the water: shot without it,
                // then with it at 60 s and 240 s.
                var load = new AircraftLoad(cfg);
                foreach (var slot in load.Slots) if (slot.Kind == AircraftLoad.StationKind.Fuel) load.Set(slot.Id, slot.MaxKg);
                var slick = FlyingGame.Core.Wreck.FuelSlick.TryCreate(cfg, load, true, true, new Vec3(state.Position.X, state.Position.Y, -water), Vec3.Zero);
                var sheen = root.AddComponent<FuelSlickVisual>();
                foreach (var (up, age) in new[] { (25f, -1), (8f, -1), (60f, -1), (25f, 60), (25f, 240), (8f, 240) })
                {
                    if (age < 0 || slick == null) sheen.Show(null, 0); else sheen.Show(slick, age);
                    string tag = age < 0 ? $"{up:00}m" : $"{up:00}m-slick{age}s";
                    cam.transform.position = surface + Vector3.up * up;
                    cam.transform.LookAt(far);
                    Call(reflection, "OnPreCull");
                    cam.Render();
                    int main = CountMagenta(rt, Path.Combine(output, $"view-{tag}.png"));
                    var refl = (RenderTexture)typeof(WaterReflection).GetField("_rt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reflection);
                    int mirror = CountMagenta(refl, Path.Combine(output, $"reflection-{tag}.png"));
                    log.AppendLine($"{tag}: _ReflOn {Shader.GetGlobalFloat("_ReflOn")} plane {Shader.GetGlobalFloat("_ReflPlaneY"):F2}; magenta px: view {main}, reflection {mirror}");
                    if (main > 0) Fail($"{tag}: {main} magenta pixels in the view");
                    if (mirror > 0) Fail($"{tag}: {mirror} magenta pixels in the reflection");
                }
                builder.Clear();
            }
            catch (Exception e) { fails++; log.AppendLine("EXCEPTION " + e); Debug.LogException(e); }
            finally
            {
                log.AppendLine(fails == 0 ? "PASS" : $"FAIL ({fails})");
                File.WriteAllText(Path.Combine(output, "report.txt"), log.ToString());
                Debug.Log("[WaterMagenta]\n" + log);
                EditorApplication.Exit(fails == 0 ? 0 : 1);
            }
        }

        /// <summary>Magenta: red and blue both well above green (the error shader's (1, 0, 1), or anything near it).</summary>
        private static int CountMagenta(RenderTexture rt, string png)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            int n = 0;
            foreach (Color32 c in tex.GetPixels32())
                if (WaterReflection.IsErrorPink(c)) n++;
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return n;
        }
    }
}

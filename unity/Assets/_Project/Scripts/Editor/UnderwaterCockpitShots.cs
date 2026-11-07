using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// Ty t922u (2026-11-06): the J-3 on floats upside down in the lake, seen from the seat. The game's world
    /// (WorldBuilder.BuildAll), the airframe the game builds, at the island's first float lane and its real surface, in
    /// prescribed poses (no sim run, nothing about buoyancy is asserted), each drawn from the seat eye with
    /// <see cref="PilotUnderwater"/> off (before) and on (after), plus the aerial and the upright-afloat seat views, which
    /// must come out PIXEL-IDENTICAL either way (the approved water look is not touched).
    ///   Unity -batchmode -projectPath unity -executeMethod FlyingGame.EditorTools.UnderwaterCockpitShots.Render
    /// PNGs to UNDERWATER_RENDER_DIR (default build/underwater-cockpit/): name-before.png, name-after.png, name-board.png.
    /// </summary>
    public static class UnderwaterCockpitShots
    {
        private const string Id = "j3-cub-floats-like";
        private const int W = 900, H = 1800;   // portrait, as Ty's phone screenshot

        private static object Call(object o, string name) =>
            o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(o, null);

        private sealed class Pose
        {
            public string Name; public double RollDeg, PitchDeg; public double EyeBelowWaterM;   // + = eye under the surface
            public bool Cockpit = true; public bool MustMatch; public float LookPitch, LookYaw;
        }

        public static void Render()
        {
            string outDir = Environment.GetEnvironmentVariable("UNDERWATER_RENDER_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/underwater-cockpit"));
            Directory.CreateDirectory(outDir);
            int failures = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                WorldTerrain.Active = new WorldTerrain();
                Shader.SetGlobalFloat("_SunsetOn", 0); Shader.SetGlobalFloat("_WaterCalm", 0);
                WorldBuilder.BuildAll();
                SunsetLighting.ConfigureDayAtmosphere("Balanced");
                RenderSettings.fog = false;
                RenderSettings.ambientLight = new Color(.65f, .7f, .78f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(45, -35, 0);
                Shader.SetGlobalVector("_BubbleSunDir", new Vector3(.4f, .8f, -.4f));

                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(Id);
                var lane = IslandSites.Fields.First(f => f.Surface == "water");
                double surface = WorldTerrain.Active.WaterSurfaceAt(lane.Centre.X, lane.Centre.Y) ?? lane.Elevation;
                double hdg = lane.Heading * Math.PI / 180;
                var root = new GameObject("Underwater " + Id);
                var driver = root.AddComponent<FlightSimDriver>(); driver.enabled = false;
                var builder = new AirframeBuilder(); builder.Build(root.transform, cfg);
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = WaterReflection.AircraftLayer;
                Debug.Log($"[UnderwaterShots] {Id}: model {(builder.ModelRoot != null ? builder.ModelRoot.name : "(procedural)")}; lane surface {surface:F2} m");
                Vec3 eyeBody = cfg.Loading.Seats.First(s => s.HasEye).EyeVec();

                var cam = new GameObject("Seat camera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox; cam.backgroundColor = new Color(.45f, .66f, .95f);
                cam.nearClipPlane = .05f; cam.farClipPlane = 12000; cam.fieldOfView = 100; cam.aspect = (float)W / H;
                cam.depthTextureMode = DepthTextureMode.Depth;
                var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
                var reflection = cam.gameObject.AddComponent<WaterReflection>();
                reflection.enabled = false; reflection.Driver = driver; Call(reflection, "Awake");
                var under = cam.gameObject.AddComponent<PilotUnderwater>();
                under.enabled = false; under.SetActiveForTesting();

                var poses = new[]
                {
                    // Ty's case: on its back, floats up; the cabin hangs under them, the head 1.5 m down.
                    new Pose { Name = "inverted-deep", RollDeg = 180, PitchDeg = 0, EyeBelowWaterM = 1.5 },
                    new Pose { Name = "inverted-deep-look-down", RollDeg = 180, PitchDeg = 0, EyeBelowWaterM = 1.5, LookPitch = -35 },
                    // On its back with the waterline through the cabin: the head just under, the floor/top of the panel out.
                    new Pose { Name = "inverted-partial", RollDeg = 180, PitchDeg = 8, EyeBelowWaterM = 0.15 },
                    // Nosed in, upright: eye 0.4 m above the water, the panel and the cowl under it.
                    new Pose { Name = "nose-in-partial", RollDeg = 10, PitchDeg = -35, EyeBelowWaterM = -0.4 },
                    // Sunk: upright, everything 3 m under.
                    new Pose { Name = "full-submersion", RollDeg = 0, PitchDeg = 0, EyeBelowWaterM = 3.0 },
                    // Must not change: the seat afloat upright, and the outside views of the wreck.
                    new Pose { Name = "afloat-upright-seat", RollDeg = 0, PitchDeg = 0, EyeBelowWaterM = -1.43, MustMatch = true },
                    new Pose { Name = "aerial-inverted", RollDeg = 180, PitchDeg = 0, EyeBelowWaterM = 1.5, Cockpit = false, MustMatch = true },
                    new Pose { Name = "aerial-afloat", RollDeg = 0, PitchDeg = 0, EyeBelowWaterM = -1.43, Cockpit = false, MustMatch = true },
                };
                foreach (Pose p in poses)
                {
                    try { failures += Shoot(p, cfg, root, driver, builder, eyeBody, lane, surface, hdg, cam, rt, reflection, under, outDir); }
                    catch (Exception e) { failures++; Debug.LogException(e); }
                }
                foreach (string name in new[] { "Hidden/FlyingGame/PilotUnderwater", "FlyingGame/Water" })
                {
                    var shader = Shader.Find(name);
                    if (shader == null || !shader.isSupported) { Debug.LogError("[UnderwaterShots] missing/unsupported shader " + name); failures++; continue; }
                    foreach (var m in ShaderUtil.GetShaderMessages(shader))
                        if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) { Debug.LogError($"[UnderwaterShots] {name}: {m.message}"); failures++; }
                }
                cam.targetTexture = null; rt.Release();
            }
            catch (Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                PilotUnderwater.DisabledForTesting = false; PilotUnderwater.InCockpitForTesting = null;
                RenderTexture.active = null;
                Debug.Log($"[UnderwaterShots] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures); images in {outDir}");
                if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int Shoot(Pose p, FlyingGame.Core.DataContracts.AircraftConfig cfg, GameObject root, FlightSimDriver driver,
            AirframeBuilder builder, Vec3 eyeBody, IslandField lane, double surface, double hdg, Camera cam, RenderTexture rt,
            WaterReflection reflection, PilotUnderwater under, string outDir)
        {
            double r = p.RollDeg * Math.PI / 180, th = p.PitchDeg * Math.PI / 180;
            Quat q = Quat.Multiply(new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2)),
                     Quat.Multiply(new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2)), new Quat(Math.Sin(r / 2), 0, 0, Math.Cos(r / 2))));
            // Sim z is DOWN: the eye's height is -(cg.z + rotated eye z); put it EyeBelowWaterM under the surface.
            double eyeZ = q.Rotate(eyeBody).Z;
            double cgZ = -(surface - p.EyeBelowWaterM) - eyeZ;
            var state = new RigidBodyState(new Vec3(lane.Centre.X, lane.Centre.Y, cgZ), q, Vec3.Zero, Vec3.Zero);
            var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
            ac.SetReplayPose(state, ControlDeflections.Neutral, 0, 0, 1, 1);
            driver.AdoptSim(new SimLoop(ac));
            Transform t = root.transform;
            Vector3 eye = t.TransformPoint(CoordinateMap.ToUnity(eyeBody));
            if (p.Cockpit)
            {
                cam.fieldOfView = 100;
                cam.transform.SetPositionAndRotation(eye, t.rotation * Quaternion.Euler(4f - p.LookPitch, p.LookYaw, 0f));
            }
            else
            {
                cam.fieldOfView = 50;
                Vector3 at = t.position;
                Vector3 back = Quaternion.Euler(0, (float)lane.Heading, 0) * new Vector3(-9, 9, -16);
                cam.transform.position = at + back;
                cam.transform.LookAt(at, Vector3.up);
            }
            PilotUnderwater.InCockpitForTesting = p.Cockpit;
            Texture2D Snap(bool on)
            {
                PilotUnderwater.DisabledForTesting = !on;
                Call(reflection, "OnPreCull");
                under.PreCullForTesting();
                cam.Render();
                under.PostRenderForTesting();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                return tex;
            }
            Texture2D before = Snap(false);
            Texture2D after = Snap(true);
            var mode = under.CurrentMode;
            File.WriteAllBytes(Path.Combine(outDir, p.Name + "-before.png"), before.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(outDir, p.Name + "-after.png"), after.EncodeToPNG());
            Color32[] a = before.GetPixels32(), b = after.GetPixels32();
            long diff = 0; int changed = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
                diff += d; if (d > 6) changed++;
            }
            var board = new Texture2D(W * 2 + 12, H, TextureFormat.RGB24, false);
            board.SetPixels32(0, 0, W, H, a); board.SetPixels32(W + 12, 0, W, H, b);
            for (int y = 0; y < H; y++) for (int x = W; x < W + 12; x++) board.SetPixel(x, y, Color.white);
            board.Apply(); File.WriteAllBytes(Path.Combine(outDir, p.Name + "-board.png"), board.EncodeToPNG());
            double eyeAbove = eye.y - surface;
            string line = $"[UnderwaterShots] {p.Name}: mode {mode}, eye {eyeAbove:+0.00;-0.00} m vs the water, camera {(p.Cockpit ? "seat" : "aerial")}; " +
                          $"{changed} of {a.Length} px changed ({100.0 * changed / a.Length:F1} %), mean |d| {(double)diff / a.Length:F2}";
            int fails = 0;
            if (p.MustMatch && diff != 0) { Debug.LogError(line + " — MUST be identical"); fails++; }
            else if (!p.MustMatch && changed < a.Length / 50) { Debug.LogError(line + " — the treatment barely changed the view"); fails++; }
            else Debug.Log(line);
            Object.DestroyImmediate(before); Object.DestroyImmediate(after); Object.DestroyImmediate(board);
            return fails;
        }
    }
}

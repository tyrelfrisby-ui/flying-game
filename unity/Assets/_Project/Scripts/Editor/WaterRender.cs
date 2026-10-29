using System;
using System.IO;
using System.Reflection;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    // Deterministic harbour tow fixture: prescribed pose, real FloatHydro samples and wave solver.
    // Does not claim to be a free-running takeoff or write any flight configuration.
    public static class WaterRender
    {
        static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args);
        public static void RenderFromArgs()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                string Read(string flag, string fallback)
                {
                    int i = Array.IndexOf(args, flag);
                    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
                }
                string output = Path.GetFullPath(Read("-out", "build/renders/water"));
                string id = Read("-aircraft", "j3-cub-floats-like");
                float throttle = float.Parse(Read("-throttle", "1"), System.Globalization.CultureInfo.InvariantCulture);
                float lift = float.Parse(Read("-lift", "0"), System.Globalization.CultureInfo.InvariantCulture);
                Directory.CreateDirectory(output);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                WorldTerrain.Active = new WorldTerrain();
                WorldBuilder.BuildAll();
                RenderSettings.fog = false;
                RenderSettings.ambientLight = new Color(.65f, .7f, .78f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(45, -35, 0);
                Shader.SetGlobalVector("_BubbleSunDir", new Vector3(.4f, .8f, -.4f));
                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                if (cfg.Floats == null) throw new ArgumentException("Water render requires a floatplane.");
                var lake = WorldTerrain.Harbor;
                var root = new GameObject("Water render " + id);
                var driver = root.AddComponent<FlightSimDriver>(); driver.enabled = false;
                var builder = new AirframeBuilder(); builder.Build(root.transform, cfg);
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = WaterReflection.AircraftLayer;
                var spray = root.AddComponent<FloatSplash>(); spray.Driver = driver;
                var wake = root.AddComponent<WakeWaves>(); wake.Driver = driver; Call(wake, "Start");
                Call(wake, "Allocate", WorldTerrain.Lakes.Length - 1);
                for (int frame = 0; frame < 960; frame++)
                {
                    double north = lake.Cx + (frame - 959) * 18.0 / 60;
                    var attitude = new Quat(0, Math.Sin(3 * Math.PI / 360), 0, Math.Cos(3 * Math.PI / 360));
                    var state = new RigidBodyState(new Vec3(north, lake.Cy, -lake.SurfaceM - cfg.Floats.KeelZ + .13 - lift), attitude, new Vec3(18, 0, 0), Vec3.Zero);
                    var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
                    ac.SetReplayPose(state, ControlDeflections.Neutral, throttle, 0, 1, 1);
                    driver.AdoptSim(new SimLoop(ac));
                    FloatHydro.Compute(cfg, state, 0);
                    Call(spray, "LateUpdate");
                    var particles = new System.Collections.Generic.List<ParticleSystem>();
                    foreach (var field in typeof(FloatSplash).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                        if (field.FieldType == typeof(ParticleSystem[])) particles.AddRange((ParticleSystem[])field.GetValue(spray));
                    for (int i = 0; i < particles.Count; i++)
                    {
                        var ps = particles[i];
                        if (frame == 0) { ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(1234 + i); ps.Play(); }
                        ps.Simulate(1f / 60, false, false, false);
                    }
                    if (frame % 3 == 0) Call(wake, "Step", (int)Call(wake, "Sources", ac));
                }
                Debug.Log($"WaterRender: wet={FloatHydro.Floats[0].Wet}, lift={FloatHydro.Floats[0].PlaningLiftN}, buoyancy={FloatHydro.Floats[0].BuoyancyN}, drag={FloatHydro.Floats[0].DragN}");
                // Publish the final ping-pong texture, as Update does in play mode.
                Shader.SetGlobalTexture("_WakeTex", (RenderTexture)typeof(WakeWaves).GetField("_curr", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(wake));
                var foamField = typeof(WakeWaves).GetField("_foam", BindingFlags.NonPublic | BindingFlags.Instance);
                Shader.SetGlobalTexture("_WakeFoamTex", foamField != null ? (Texture)foamField.GetValue(wake) : Texture2D.blackTexture);
                builder.SpinProps(_ => (float)driver.Sim.Aircraft.EngineRpm, .02f);
                var camera = new GameObject("Water camera").AddComponent<Camera>();
                var reflection = camera.gameObject.AddComponent<WaterReflection>();
                reflection.enabled = false; reflection.Driver = driver; Call(reflection, "Awake");
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.45f, .66f, .95f);
                camera.nearClipPlane = .15f; camera.farClipPlane = 12000; camera.fieldOfView = 48;
                var rt = new RenderTexture(1600, 1000, 24); camera.targetTexture = rt;
                Vector3 at = root.transform.position;
                var offsets = new[] { new Vector3(15, 5, 15), new Vector3(30, 40, -45), new Vector3(7, 2, 6) };
                var names = new[] { "spray", "wake", "bow" };
                for (int i = 0; i < offsets.Length; i++)
                {
                    camera.transform.position = at + offsets[i]; camera.transform.LookAt(at + new Vector3(0, -1, i == 1 ? -24 : 0));
                    Call(reflection, "OnPreCull");
                    camera.Render(); RenderTexture.active = rt;
                    var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(output, names[i] + ".png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                RenderTexture.active = null;
                foreach (string name in new[] { "FlyingGame/Water", "FlyingGame/Spray", "Hidden/FlyingGame/WaveSim", "Hidden/FlyingGame/WakeFoam" })
                {
                    if (name == "Hidden/FlyingGame/WakeFoam" && foamField == null) continue;
                    var shader = Shader.Find(name);
                    if (shader == null || !shader.isSupported) throw new Exception("Missing/unsupported water shader: " + name);
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) throw new Exception(name + ": " + message.message);
                }
                Debug.Log("WaterRender: wrote " + output);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}

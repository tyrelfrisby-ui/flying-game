using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-05 9:04: metal blades curl forward under power and aft at idle; wood and composite break off only where
    /// they hit, jagged. Each case is a REAL strike in the sim (the same harness as tools/FlightTests/PropBladeDamageTests:
    /// the aeroplane held with its lowest tip a set depth into the ground or the water), and the airframe the game builds
    /// is then drawn with the blades the sim left (AirframeBuilder.DamageBlades), stopped, from the left side (fore and
    /// aft read left and right) and from ahead.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.PropDamageRender.Render
    /// Images to PROP_RENDER_DIR (default build/prop-damage/).
    /// </summary>
    public static class PropDamageRender
    {
        private sealed class Case
        {
            public string Name, Id; public bool Water; public double Depth, Hold, Throttle, Speed;
        }

        public static void Render()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PROP_RENDER_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/prop-damage"));
            Directory.CreateDirectory(outDir);
            int failures = 0;
            var cases = new[]
            {
                new Case { Name = "1-metal-forward-curl", Id = "pitts-s2b-like", Water = false, Depth = 0.06, Hold = 0.2, Throttle = 1.0, Speed = 0 },
                new Case { Name = "2-metal-aft-curl", Id = "pitts-s2b-like", Water = false, Depth = 0.06, Hold = 0.06, Throttle = 0.0, Speed = 30 },
                new Case { Name = "3-wood-jagged-break", Id = "j3-cub-like", Water = false, Depth = 0.10, Hold = 0.03, Throttle = 1.0, Speed = 0 },
                new Case { Name = "4-composite-break", Id = "extra-300-like", Water = true, Depth = 0.12, Hold = 0.03, Throttle = 1.0, Speed = 0 },
            };
            try
            {
                foreach (Case c in cases) failures += RenderCase(c, outDir);
            }
            catch (System.Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                RenderTexture.active = null;
                Debug.Log($"[PropDamage] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures); images in {outDir}");
                if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int RenderCase(Case c, string outDir)
        {
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(c.Id);
            Aircraft ac = Strike(cfg, c);
            int n = ac.PropBladeCount;
            var look = new AirframeBuilder.PropBladeLook[n];
            string line = $"[PropDamage] {c.Name} ({c.Id}, {ac.PropBladeMaterial}, {(c.Water ? "water" : "ground")} {c.Depth * 100:F0} cm, throttle {c.Throttle:F1}, {c.Speed:F0} m/s): engine {(ac.EngineStopped ? "stopped" : "running")};";
            for (int k = 0; k < n; k++)
            {
                look[k] = new AirframeBuilder.PropBladeLook
                {
                    Aero = (float)ac.PropBladeRadiusM(0, k), Affected = (float)ac.PropBladeAffectedM(0, k),
                    Curl = ac.PropBladeCurl(0, k), Broken = ac.PropBladeBroken(0, k),
                };
                line += $" blade {k}: straight {look[k].Aero:F3} m, affected {look[k].Affected * 100:F1} cm, {(look[k].Broken ? "broken" : look[k].Curl > 0 ? "curled FORWARD" : look[k].Curl < 0 ? "curled AFT" : "straight")};";
            }
            Debug.Log(line + $" thrust x{ac.PropThrustScale():F3}, shake {ac.PropVibrationG():F4} g");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionSettings.UseAirframeModels = true;
            SessionSettings.UseOwnModels = true;
            var cam = new GameObject("RenderCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.7f, 0.92f);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 200f;
            var rt = new RenderTexture(1400, 1000, 24); cam.targetTexture = rt;
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(40, 60, 0);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var root = new GameObject(c.Id);
            var builder = new AirframeBuilder();
            builder.Build(root.transform, cfg);
            Transform hub = builder.PropHubTransform(0);
            if (hub == null) { Debug.LogError($"[PropDamage] {c.Name}: no propeller hub"); return 1; }
            int fails = 0;
            // Stopped, blade 0 pointing down then across (a quarter turn: 60 rpm for 0.25 s).
            builder.SpinProps(_ => 0f, 0f);
            builder.DamageBlades(_ => ((float)(0.5 * cfg.Propulsion.PropDiameterM), ac.PropBladeMaterial, look));
            int meshes = builder.PropDamageMeshCount(0);
            float r = (float)(0.5 * cfg.Propulsion.PropDiameterM);
            Debug.Log($"[PropDamage] {c.Name}: reshaped {meshes} blade meshes; hub children: " +
                      string.Join(", ", hub.GetComponentsInChildren<MeshFilter>(true).Select(m => m.name)));
            foreach (MeshFilter m in hub.GetComponentsInChildren<MeshFilter>(true))
            {
                if (m.sharedMesh == null) continue;
                float far = 0f;
                foreach (Vector3 v in m.sharedMesh.vertices)
                {
                    Vector3 w = m.transform.TransformPoint(v) - hub.position;
                    far = Mathf.Max(far, Vector3.ProjectOnPlane(w, hub.forward).magnitude);
                }
                var mr = m.GetComponent<MeshRenderer>();
                Debug.Log($"[PropDamage] {c.Name}:   mesh '{m.name}' under '{m.transform.parent?.name}' enabled {(mr != null && mr.enabled)} " +
                          $"lossyScale {m.transform.lossyScale} reaches {far:F3} m from the hub axis (sim R {r:F3})");
            }
            if (meshes == 0) { Debug.LogError($"[PropDamage] {c.Name}: no blade mesh could be reshaped (skinned or unreadable)"); fails++; }
            Vector3 at = hub.position, fwd = root.transform.forward, right = root.transform.right, up = root.transform.up;
            for (int turn = 0; turn < 2; turn++)
            {
                if (turn == 1) builder.SpinProps(_ => 60f, 0.25f);
                Snap(cam, rt, at, at - right * (4.2f * r) + up * (0.25f * r), up, outDir, $"{c.Name}-side{turn}");
                Snap(cam, rt, at, at + fwd * (3.6f * r) - right * (1.6f * r) + up * (0.6f * r), up, outDir, $"{c.Name}-front{turn}");
            }
            // Close on the upper and lower tips after the quarter turn: from the left (fore-aft shows as left-right) and from
            // ahead-left.
            foreach ((string tag, float sgn) in new[] { ("up", 1f), ("down", -1f) })
            {
                Vector3 tip = at + up * (sgn * 0.85f * r);
                Snap(cam, rt, tip, tip - right * (1.1f * r), up, outDir, $"{c.Name}-tip-{tag}-side");
                Snap(cam, rt, tip, tip + fwd * (0.9f * r) - right * (0.5f * r) + up * (sgn * 0.2f * r), up, outDir, $"{c.Name}-tip-{tag}-front");
            }
            Object.DestroyImmediate(root);
            return fails;
        }

        /// <summary>The strike, exactly as PropBladeDamageTests.Strike does it.</summary>
        private static Aircraft Strike(AircraftConfig cfg, Case c)
        {
            WorldTerrain.Active = null;
            double low = AirframeContact.BuildPoints(cfg).Where(p => p.PropDisc).Max(p => p.Body.Z - cfg.Mass.CgVec().Z);
            var att = new Quat(0, 0, 0, 1);
            double surface = c.Water ? 3.0 : 0.0;
            var pos = new Vec3(0, 0, -(surface + low - c.Depth));
            var vel = new Vec3(c.Speed, 0, 0);
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -100), att, vel, Vec3.Zero), ControlDeflections.Neutral);
            var sim = new SimLoop(ac);
            var lever = new ControlInputs(0, 0, 0, 1 - 2 * c.Throttle);
            if (c.Water) FloatHydro.FlatWaterOverride = surface;
            try
            {
                sim.RunFor(0.5, lever);
                Atmosphere.SimTimeSec = 0.5;
                for (double t = 0; t < c.Hold - 1e-9; t += 0.005)
                {
                    ac.State = new RigidBodyState(pos, att, vel, Vec3.Zero);
                    sim.RunFor(0.005, lever);
                }
                ac.State = new RigidBodyState(new Vec3(0, 0, -100), att, vel, Vec3.Zero);
                sim.RunFor(0.1, lever);
            }
            finally { FloatHydro.FlatWaterOverride = null; Atmosphere.SimTimeSec = 0; }
            return ac;
        }

        private static void Snap(Camera cam, RenderTexture rt, Vector3 target, Vector3 from, Vector3 up, string outDir, string name)
        {
            cam.transform.position = from;
            cam.transform.LookAt(target, up);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}

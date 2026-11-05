using System;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// EVERY VIEW A HEADSET OFFERS, driven through the real rig (Ty 2026-11-04: "no views are available except 'outside'
    /// which is actually cockpit"): <see cref="ChaseCamera"/> places itself, <see cref="QuestRig"/> takes the head from
    /// <see cref="XrControllers.HeadForTesting"/> and makes the origin, exactly as a frame on the Quest does
    /// (XrHeadset.ForceActiveForTesting), then the camera is drawn.
    ///
    /// ASSERTED: Cockpit puts the camera at the seat's eye (within 2 cm, the head at rest); every outside view puts it
    /// at least 5 m from the eye, level (no roll, no pitch from the rig), and a second frame with the aeroplane banked
    /// 30° and turned 20° leaves the origin's heading where it was (Chase eases and snap-turns, it never rolls you).
    ///
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.QuestViewShots.Run   (no -nographics: PNGs)
    /// PNGs: logs/quest-views/&lt;aeroplane&gt;-&lt;view&gt;.png
    /// </summary>
    public static class QuestViewShots
    {
        private const int W = 1440, H = 1080;
        private static readonly string[] Fleet = { "great-lakes-like", "extra-300-like", "glider-2-33-like" };
        private static int _fail;
        private static void Fail(string m) { _fail++; Debug.LogError("[QuestViews] FAIL " + m); }

        public static void Run()
        {
            _fail = 0;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "..", "logs", "quest-views");
            bool render = !SystemInfo.graphicsDeviceType.ToString().Contains("Null");
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            try
            {
                Directory.CreateDirectory(dir);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                XrHeadset.ForceActiveForTesting = true;
                XrControllers.HeadForTesting = (Vector3.zero, Quaternion.identity);
                string only = Environment.GetEnvironmentVariable("VIEWS_ONLY");
                foreach (string id in Fleet)
                    if (string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), id) >= 0) One(id, dir, render);
            }
            catch (Exception e) { _fail++; Debug.LogException(e); }
            finally
            {
                XrHeadset.ForceActiveForTesting = false;
                XrControllers.HeadForTesting = null;
                ChaseCamera.CompactFlyby = false;
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[QuestViews] {(_fail == 0 ? "PASS" : "FAIL")} ({_fail} failures)");
                EditorApplication.Exit(_fail == 0 ? 0 : 1);
            }
        }

        private static void One(string id, string dir, bool render)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(400f, 1f, 400f);
            var gm = new Material(Shader.Find("FlyingGame/Lit") ?? Shader.Find("Standard")) { color = new Color(0.33f, 0.45f, 0.25f) };
            ground.GetComponent<Renderer>().sharedMaterial = gm;
            // A row of posts down the runway line, so a still frame shows where the ground is and which way is north.
            for (int i = -6; i <= 6; i++)
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.transform.position = new Vector3(i % 2 == 0 ? 30f : -30f, 4f, i * 60f);
                post.transform.localScale = new Vector3(2f, 8f, 2f);
            }
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.55f);

            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            var root = new GameObject("Aircraft");
            FlightSimDriver driver = root.AddComponent<FlightSimDriver>();
            CockpitView view = root.AddComponent<CockpitView>();
            view.Driver = driver;
            // 40 m up, 45 m/s north: Unity (east, up, north) ← sim (north, east, down).
            var state = new FlyingGame.Core.RigidBodyState(new Vec3(0, 0, -40), Quat.Identity, new Vec3(45, 0, 0), Vec3.Zero);
            driver.AdoptSim(new FlyingGame.Sim.SimLoop(new FlyingGame.Sim.Aircraft(cfg, state)));
            root.transform.SetPositionAndRotation(new Vector3(0f, 40f, 0f), Quaternion.identity);
            var builder = new AirframeBuilder();
            builder.Build(root.transform, cfg);
            view.SelectSeat(0, remember: false);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.70f, 0.90f);
            cam.fieldOfView = 90f; cam.farClipPlane = 5000f;
            ChaseCamera chase = camGo.AddComponent<ChaseCamera>();
            chase.Driver = driver; chase.Target = root.transform; chase.Cockpit = view;
            chase.FitTo(10f, 7f);   // the game fits it from the built model (AirframeVisual); a light aerobat is about this
            QuestRig rig = camGo.AddComponent<QuestRig>();

            foreach (ChaseCamera.View v in QuestViews.Offered)
            {
                root.transform.SetPositionAndRotation(new Vector3(0f, 40f, 0f), Quaternion.identity);
                chase.SetView(v);
                for (int f = 0; f < 3; f++) { chase.PlaceForTesting(); rig.StepForTesting(); }
                Vector3 eye = root.transform.TransformPoint(view.EyeLocal);
                float fromEye = Vector3.Distance(camGo.transform.position, eye);
                Vector3 e = camGo.transform.rotation.eulerAngles;
                string label = QuestViews.Label(v);
                Debug.Log($"[QuestViews] {id} {label}: InCockpit {ChaseCamera.InCockpit}, camera {fromEye:F2} m from the seat eye, " +
                          $"pitch {Mathf.DeltaAngle(0f, e.x):F1}° roll {Mathf.DeltaAngle(0f, e.z):F1}° heading {e.y:F1}°");
                if (v == ChaseCamera.View.Cockpit)
                {
                    if (!ChaseCamera.InCockpit) Fail($"{id} Cockpit: InCockpit is false");
                    if (fromEye > 0.02f) Fail($"{id} Cockpit: camera {fromEye:F3} m from the eye");
                }
                else
                {
                    if (ChaseCamera.InCockpit) Fail($"{id} {label}: InCockpit is true");
                    if (fromEye < 5f) Fail($"{id} {label}: camera only {fromEye:F2} m from the seat eye — that is the cockpit, not outside");
                    if (Mathf.Abs(Mathf.DeltaAngle(0f, e.x)) > 0.01f || Mathf.Abs(Mathf.DeltaAngle(0f, e.z)) > 0.01f)
                        Fail($"{id} {label}: the rig pitched/rolled the view ({e})");
                }
                if (render) Shoot(cam, Path.Combine(dir, $"{id}-{Slug(label)}.png"));
                if (v != ChaseCamera.View.Cockpit)
                {
                    // The aeroplane banks 30° and turns 20°: the origin must not roll, and must not turn (under the 45° snap).
                    float yaw0 = camGo.transform.rotation.eulerAngles.y;
                    root.transform.rotation = Quaternion.Euler(0f, 20f, -30f);
                    chase.PlaceForTesting(); rig.StepForTesting();
                    Vector3 e2 = camGo.transform.rotation.eulerAngles;
                    Debug.Log($"[QuestViews] {id} {label} after a 30° bank / 20° turn: roll {Mathf.DeltaAngle(0f, e2.z):F2}°, heading {Mathf.DeltaAngle(yaw0, e2.y):F2}° from before");
                    if (Mathf.Abs(Mathf.DeltaAngle(0f, e2.z)) > 0.01f) Fail($"{id} {label}: the view rolled with the aeroplane");
                    if (v != ChaseCamera.View.Flyby && Mathf.Abs(Mathf.DeltaAngle(yaw0, e2.y)) > 0.01f) Fail($"{id} {label}: the view turned with the aeroplane");
                    if (render && v == ChaseCamera.View.RelativeWind) Shoot(cam, Path.Combine(dir, $"{id}-{Slug(label)}-banked.png"));
                }
            }
            Object.DestroyImmediate(rig);
            builder.Clear();
        }

        private static string Slug(string s) => s.ToLowerInvariant().Replace(' ', '-').Replace(",", "");

        private static void Shoot(Camera cam, string path)
        {
            RenderTexture rt = RenderTexture.GetTemporary(W, H, 24);
            RenderTexture active = RenderTexture.active;
            try
            {
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(W, H, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, W, H), 0, 0); shot.Apply();
                File.WriteAllBytes(path, shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                Debug.Log("[QuestViews] wrote " + Path.GetFullPath(path));
            }
            finally { cam.targetTexture = null; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}

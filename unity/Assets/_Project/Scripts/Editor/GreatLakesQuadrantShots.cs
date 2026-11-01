using System.IO;
using System.Reflection;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// The Great Lakes throttle quadrant, photographed at idle and at full throttle in both cockpits (owner Ty,
    /// 2026-11-01: "the throttle quadrant lever does not move when the throttle changes").
    ///
    /// A still cannot show movement, so this takes the SAME viewpoint twice with only RigInputs.Throttle changed,
    /// and prints how far the lever's tip actually travelled between them. The number is the evidence; the two
    /// pictures are so the colours and the layout can be checked against Ty's photographs.
    ///
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.GreatLakesQuadrantShots.Run
    /// </summary>
    public static class GreatLakesQuadrantShots
    {
        private const int W = 1280, H = 720;

        public static void Run()
        {
            string outDir = System.Environment.GetEnvironmentVariable("QUADRANT_OUT")
                            ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "logs", "quadrant");
            int failures = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            GameObject root = null, camGo = null;
            try
            {
                Directory.CreateDirectory(outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;

                camGo = new GameObject("Shot Camera");
                Camera cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.02f; cam.fieldOfView = 40f;
                root = new GameObject("Aircraft");
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var builder = new AirframeBuilder();
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("great-lakes-like");
                if (cfg == null) { Debug.LogError("[Quadrant] no great-lakes-like config"); EditorApplication.Exit(1); return; }
                builder.Build(root.transform, cfg);

                foreach ((string seat, string suffix) in new[] { ("rear", "_R"), ("front", "_F") })
                {
                    Transform lever = Find(root.transform, "Throttle" + suffix);
                    if (lever == null) { Debug.LogError("[Quadrant] no Throttle" + suffix); failures++; continue; }
                    // Look at the quadrant from inboard and a little above, the way the pilot glances down-left.
                    Bounds b = Box(lever);
                    camGo.transform.position = b.center + new Vector3(0f, 0.22f, 0.30f);
                    camGo.transform.LookAt(b.center);

                    Vector3 idleTip = Pose(builder, lever, 0f);
                    Shoot(cam, Path.Combine(outDir, (seat == "rear" ? "" : "front-") + "quadrant-idle.png"));
                    Vector3 fullTip = Pose(builder, lever, 1f);
                    Shoot(cam, Path.Combine(outDir, (seat == "rear" ? "" : "front-") + "quadrant-full.png"));

                    float travelMm = Vector3.Distance(idleTip, fullTip) * 1000f;
                    if (travelMm < 20f) { Debug.LogError($"[Quadrant] {seat}: the lever moved {travelMm:F1} mm from idle to full — it is still stuck"); failures++; }
                    else Debug.Log($"[Quadrant] {seat} OK: the throttle lever's tip moved {travelMm:F0} mm from idle to full.");
                }
                builder.Clear();
            }
            catch (System.Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                foreach (GameObject go in new[] { root, camGo }) if (go != null) Object.DestroyImmediate(go);
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[Quadrant] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures)");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        /// <summary>Drive the rig at this throttle and report where the lever's far end ended up.</summary>
        private static Vector3 Pose(AirframeBuilder builder, Transform lever, float throttle)
        {
            var r = new RigInputs { Throttle = throttle, PropLever = 1.0, Mixture = 1.0, Magneto = 3.0, Master = 1.0 };
            builder.SetRig(r, 0.02f);
            Bounds b = Box(lever);
            return b.center + b.extents;          // a corner, so a pure rotation still shows as travel
        }

        private static Bounds Box(Transform t)
        {
            Bounds b = default; bool any = false;
            foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var world = new Bounds(mf.transform.TransformPoint(mf.sharedMesh.bounds.center), Vector3.zero);
                Vector3 e = mf.transform.TransformVector(mf.sharedMesh.bounds.extents);
                world.Expand(2f * new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)));
                if (!any) { b = world; any = true; } else b.Encapsulate(world);
            }
            return b;
        }

        private static Transform Find(Transform root, string node)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.StartsWith("Model:") ? t.name.Substring(6) : t.name;
                if (n == node) return t;
            }
            return null;
        }

        private static void Shoot(Camera cam, string path)
        {
            RenderTexture rt = RenderTexture.GetTemporary(W, H, 24);
            RenderTexture old = RenderTexture.active;
            try
            {
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[Quadrant] wrote " + path);
            }
            finally { cam.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}

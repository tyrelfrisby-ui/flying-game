using System.IO;
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
    /// The Great Lakes FRONT panel, photographed from the front seat's own eye, to be held against Ty's
    /// photograph of the real one (art/GL-FRONT-PANEL-AUDIT.md).
    ///
    /// It also settles the wet compass's SIGN, which cannot be reasoned about safely: on a real whiskey compass
    /// the card is reverse-sensing, so flying 000 puts E to the LEFT of the lubber line. The run shoots the
    /// compass at 000 and at 090 so the two can be compared directly — at 090 the card must have moved so that E
    /// is under the line.
    ///
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.GreatLakesFrontPanelShot.Run
    /// </summary>
    public static class GreatLakesFrontPanelShot
    {
        private const int W = 1600, H = 900;

        public static void Run()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PANEL_OUT")
                            ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "logs", "panel");
            int failures = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            GameObject root = null, camGo = null;
            try
            {
                Directory.CreateDirectory(outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;

                camGo = new GameObject("Panel Camera");
                Camera cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.02f;
                root = new GameObject("Aircraft");
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var builder = new AirframeBuilder();
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("great-lakes-like");
                if (cfg == null) { Debug.LogError("[FrontPanel] no config"); EditorApplication.Exit(1); return; }
                builder.Build(root.transform, cfg);

                // The FRONT seat's authored eye. Seats are primary-first, so the front is index 1.
                StationConfig front = cfg.Loading.Seats[1];
                Vector3 eye = CoordinateMap.ToUnity(front.EyeVec());
                Transform centre = Find(root.transform, "TC_Symbol_F");
                Transform compass = Find(root.transform, "Compass_Card_F");
                if (centre == null) { Debug.LogError("[FrontPanel] TC_Symbol_F missing"); failures++; }

                if (centre != null)
                {
                    camGo.transform.position = eye;
                    camGo.transform.LookAt(centre.position);
                    cam.fieldOfView = 48f;
                    Pose(builder, 0f);
                    Shoot(cam, Path.Combine(outDir, "front-panel-seat-eye.png"));
                    Debug.Log($"[FrontPanel] panel shot from the front eye {eye}, {Vector3.Distance(eye, centre.position):F2} m away.");
                }

                // The compass, close, at two headings. North first, then east.
                if (compass != null)
                {
                    // Far enough back to be OUTSIDE the case: 18% of the way to the eye put the camera inside
                    // it and rendered a black frame.
                    camGo.transform.position = Vector3.Lerp(compass.position, eye, 0.62f);
                    camGo.transform.LookAt(compass.position);
                    cam.fieldOfView = 18f;
                    foreach ((string tag, float hdg) in new[] { ("000", 0f), ("090", 90f) })
                    {
                        Pose(builder, hdg);
                        Shoot(cam, Path.Combine(outDir, $"front-compass-{tag}.png"));
                    }
                    Debug.Log("[FrontPanel] compass shot at 000 and 090 — a reverse-sensing card shows E LEFT of the line at 000.");
                }
                else { Debug.LogError("[FrontPanel] Compass_Card_F missing"); failures++; }
                builder.Clear();
            }
            catch (System.Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                foreach (GameObject go in new[] { root, camGo }) if (go != null) Object.DestroyImmediate(go);
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[FrontPanel] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures)");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        /// <summary>A running engine on a given heading, so the needles and the card are somewhere to look at.</summary>
        private static void Pose(AirframeBuilder builder, float headingDeg)
        {
            var r = new RigInputs
            {
                HeadingDeg = headingDeg, Throttle = 0.6, PropLever = 1.0, Mixture = 1.0, Magneto = 3.0, Master = 1.0,
                Rpm = 2100, OilPressPsi = 72, OilTempF = 185, AirspeedMph = 95, AltitudeFt = 2500, GLoad = 1.0,
            };
            builder.SetRig(r, 0.02f);
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
                Debug.Log("[FrontPanel] wrote " + path);
            }
            finally { cam.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}

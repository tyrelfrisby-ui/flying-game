using System;
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
    /// A cockpit as the HEADSET sees it: the aeroplane built the way the game builds it (AirframeBuilder, own models,
    /// rig posed), a camera at the seat's authored eye (the same <c>seats[].eye</c> CockpitView and the Quest rig
    /// anchor on), level flight attitude, one eye of a Quest 3S: 90 deg vertical by ~96 deg horizontal, 1666 x 1500.
    /// A horizon plane 1.5 m below the CG stands in for the ground so "can I see over the nose" has an answer.
    ///
    /// Shots come from COCKPIT_SHOTS, ';'-separated "name|aircraft-id|seat-id|yawDeg|pitchDeg[|vfovDeg]" (yaw + is
    /// to the pilot's RIGHT, pitch + is UP); PNGs go to COCKPIT_OUT (default ../logs/cockpit-eye).
    ///
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.CockpitEyeShots.Run   (no -nographics: PNGs)
    /// </summary>
    public static class CockpitEyeShots
    {
        private const int W = 1666, H = 1500;

        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("COCKPIT_OUT")
                            ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "logs", "cockpit-eye");
            string spec = Environment.GetEnvironmentVariable("COCKPIT_SHOTS") ?? "";
            int failures = 0, shots = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            try
            {
                Directory.CreateDirectory(outDir);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                foreach (string raw in spec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = raw.Trim().Split('|');
                    if (f.Length < 5) { Debug.LogError("[CockpitEye] bad shot spec: " + raw); failures++; continue; }
                    float vfov = f.Length > 5 ? float.Parse(f[5], System.Globalization.CultureInfo.InvariantCulture) : 90f;
                    if (One(f[0], f[1], f[2], F(f[3]), F(f[4]), vfov, outDir)) shots++; else failures++;
                }
            }
            catch (Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[CockpitEye] {(failures == 0 ? "PASS" : "FAIL")}: {shots} shots, {failures} failures");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        private static bool One(string name, string id, string seatId, float yaw, float pitch, float vfov, string outDir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            if (cfg == null) { Debug.LogError($"[CockpitEye] {id}: no config"); return false; }
            StationConfig seat = cfg.Loading.Seats.Find(s => s.Id == seatId);
            if (seat == null) { Debug.LogError($"[CockpitEye] {id}: no seat '{seatId}'"); return false; }

            var root = new GameObject("Aircraft");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var builder = new AirframeBuilder();
            builder.Build(root.transform, cfg);
            builder.SetRig(new RigInputs
            {
                Throttle = 0.6, PropLever = 1.0, Mixture = 1.0, Magneto = 3.0, Master = 1.0, Rpm = 2300,
                OilPressPsi = 70, OilTempF = 185, AirspeedMph = 120, AltitudeFt = 2500, GLoad = 1.0,
            }, 0.02f);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 160f, 0f);   // high, from ahead-left, as at midday heading north
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.51f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.35f, 0.32f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(0f, -1.5f, 0f); ground.transform.localScale = new Vector3(400f, 1f, 400f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.36f, 0.45f, 0.30f) };
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            Vector3 eye = CoordinateMap.ToUnity(seat.EyeVec());
            var camGo = new GameObject("Eye");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.70f, 0.88f);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 2000f; cam.fieldOfView = vfov;
            camGo.transform.SetPositionAndRotation(eye, Quaternion.Euler(-pitch, yaw, 0f));
            string path = Path.Combine(outDir, name + ".png");
            Shoot(cam, path);
            Debug.Log($"[CockpitEye] {name}: {id} seat {seatId} eye {eye.x:F3},{eye.y:F3},{eye.z:F3} (Unity, CG frame) yaw {yaw} pitch {pitch} vfov {vfov} -> {path}");
            builder.Clear();
            Object.DestroyImmediate(root); Object.DestroyImmediate(camGo); Object.DestroyImmediate(gm);
            return true;
        }

        private static void Shoot(Camera cam, string path)
        {
            RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;
            RenderTexture old = RenderTexture.active;
            try
            {
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { cam.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}

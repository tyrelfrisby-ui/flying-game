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
    /// Shots come from COCKPIT_SHOTS, ';'-separated "name|aircraft-id|seat-id|yawDeg|pitchDeg[|vfovDeg[|light[|hand[|WxH]]]]"
    /// (yaw + is to the pilot's RIGHT, pitch + is UP); PNGs go to COCKPIT_OUT (default ../logs/cockpit-eye).
    ///   light: sun (default: high, from ahead-left), shadow (the same sun from BEHIND the panel — the panel lit by the
    ///          sky alone, as in any attitude that puts the sun aft), dusk (sun 4 deg up, orange, dim sky), night (sky only, very dim).
    ///   hand:  "hand" puts a gloved right fist on the seat's stick grip (Grip_Stick_Rear / _Front): an ellipsoid 95 mm across
    ///          the knuckles, 105 mm tall and 85 mm deep (adult male hand breadth ~89 mm, fist ~100 mm, glove included),
    ///          its top 25 mm above the grip's top — a stand-in for the Quest pilot body on a build without it.
    ///   WxH:   image size (default 1666x1500, one Quest 3S eye; the iPhone cockpit camera is vfov 72 at about 2160x1000).
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
                    string light = f.Length > 6 ? f[6] : "sun";
                    bool hand = f.Length > 7 && f[7] == "hand";
                    int w = W, h = H;
                    if (f.Length > 8) { string[] wh = f[8].Split('x'); w = int.Parse(wh[0]); h = int.Parse(wh[1]); }
                    if (One(f[0], f[1], f[2], F(f[3]), F(f[4]), vfov, outDir, light, hand, w, h)) shots++; else failures++;
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

        private static bool One(string name, string id, string seatId, float yaw, float pitch, float vfov, string outDir,
                                string light, bool hand, int w, int h)
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

            var displays = new CockpitDisplays();
            displays.Bind(builder.FindInModel);
            displays.Update(new RigInputs { ComFreqMhz = 122.750, ComStandbyMhz = 121.500, ComVolume = 0.6, XpdrSquawk = 1200, XpdrMode = 3,
                                            Master = 1.0, FuelFlowGph = 17.8, FuelUsedGal = 3.4 });

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 160f, 0f);   // high, from ahead-left, as at midday heading north
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.51f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.35f, 0.32f);
            Color sky = new Color(0.55f, 0.70f, 0.88f);
            switch (light)
            {
                case "shadow":                                              // same sun, from behind the panel
                    sun.transform.rotation = Quaternion.Euler(50f, -20f, 0f);
                    break;
                case "dusk":
                    sun.transform.rotation = Quaternion.Euler(4f, 100f, 0f); sun.intensity = 0.45f; sun.color = new Color(1f, .62f, .36f);
                    RenderSettings.ambientSkyColor = new Color(.26f, .22f, .30f); RenderSettings.ambientEquatorColor = new Color(.30f, .22f, .20f);
                    RenderSettings.ambientGroundColor = new Color(.10f, .09f, .10f); sky = new Color(.42f, .30f, .32f);
                    break;
                case "night":
                    sun.intensity = 0f;
                    RenderSettings.ambientSkyColor = new Color(.05f, .06f, .09f); RenderSettings.ambientEquatorColor = new Color(.04f, .04f, .06f);
                    RenderSettings.ambientGroundColor = new Color(.02f, .02f, .02f); sky = new Color(.04f, .05f, .09f);
                    break;
            }
            GameObject fist = hand ? Fist(builder, seatId) : null;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(0f, -1.5f, 0f); ground.transform.localScale = new Vector3(400f, 1f, 400f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.36f, 0.45f, 0.30f) };
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            Vector3 eye = CoordinateMap.ToUnity(seat.EyeVec());
            var camGo = new GameObject("Eye");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = sky;
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 2000f; cam.fieldOfView = vfov;
            camGo.transform.SetPositionAndRotation(eye, Quaternion.Euler(-pitch, yaw, 0f));
            string path = Path.Combine(outDir, name + ".png");
            Shoot(cam, path, w, h);
            Debug.Log($"[CockpitEye] {name}: {id} seat {seatId} eye {eye.x:F3},{eye.y:F3},{eye.z:F3} (Unity, CG frame) yaw {yaw} pitch {pitch} vfov {vfov} light {light}{(fist != null ? " hand" : "")} -> {path}");
            displays.Forget();
            if (fist != null) Object.DestroyImmediate(fist);
            builder.Clear();
            Object.DestroyImmediate(root); Object.DestroyImmediate(camGo); Object.DestroyImmediate(gm);
            return true;
        }

        /// <summary>The gloved fist on the stick (see the class note). Null where the seat's grip node is missing.</summary>
        private static GameObject Fist(AirframeBuilder builder, string seatId)
        {
            Transform grip = builder.FindInModel(seatId == "front" ? "Grip_Stick_Front" : "Grip_Stick_Rear");
            Transform stick = builder.FindInModel(seatId == "front" ? "Stick_F" : "Stick_R");
            if (grip == null) { Debug.LogWarning("[CockpitEye] no stick grip node: no hand"); return null; }
            float top = grip.position.y;
            if (stick != null)
                foreach (Renderer r in stick.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "HandProxy";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            const float tall = 0.105f;
            go.transform.position = new Vector3(grip.position.x, top + 0.025f - tall * 0.5f, grip.position.z);
            go.transform.localScale = new Vector3(0.095f, tall, 0.085f);
            var m = new Material(Shader.Find("Standard")) { color = new Color(0.16f, 0.13f, 0.11f) };
            go.GetComponent<Renderer>().sharedMaterial = m;
            Debug.Log($"[CockpitEye] hand on {grip.name}: grip top {top:F3} (Unity y), hand top {top + 0.025f:F3}");
            return go;
        }

        private static void Shoot(Camera cam, string path, int W, int H)
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

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
    /// Great Lakes panels v2 (Ty 2026-11-05, gl_panel_v2.py), photographed in Unity through the game's own airframe
    /// builder and rig: from each seat's authored eye, and close up on every item of the spec — the centred card space,
    /// the front card, the radios under the panel, the switch console with its MAG switches OFF and ON, and the fuel
    /// sight gauge full, half and low (its ball driven by fuel_top_wing_frac through the rig, as in flight).
    ///
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.GreatLakesPanelV2Shots.Run   (PANEL_OUT=dir)
    /// </summary>
    public static class GreatLakesPanelV2Shots
    {
        private const int W = 1600, H = 1000;

        public static void Run()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PANEL_OUT")
                            ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "logs", "gl-panel-v2");
            int failures = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            GameObject root = null, camGo = null, sun = null;
            try
            {
                Directory.CreateDirectory(outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.62f);
                sun = new GameObject("Sun");
                Light l = sun.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.1f;
                sun.transform.rotation = Quaternion.Euler(50f, 200f, 0f);
                camGo = new GameObject("Shot Camera");
                Camera cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.01f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.70f, 0.80f);
                root = new GameObject("Aircraft");
                var builder = new AirframeBuilder();
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("great-lakes-like");
                if (cfg == null) { Debug.LogError("[GLPanelV2] no config"); EditorApplication.Exit(1); return; }
                builder.Build(root.transform, cfg);
                Vector3 eyeR = CoordinateMap.ToUnity(cfg.Loading.Seats[0].EyeVec());
                Vector3 eyeF = CoordinateMap.ToUnity(cfg.Loading.Seats[1].EyeVec());

                Transform T(string n)
                {
                    Transform t = Find(root.transform, n);
                    if (t == null) { Debug.LogError("[GLPanelV2] missing node " + n); failures++; }
                    return t;
                }
                void Shot(string name, Vector3 from, Vector3 at, float fov, RigInputs r)
                {
                    builder.SetRig(r, 0.02f);
                    camGo.transform.position = from; camGo.transform.LookAt(at); cam.fieldOfView = fov;
                    Shoot(cam, Path.Combine(outDir, name + ".png"));
                }
                Vector3 Back(Transform t, Vector3 eye, float d) => t.position + (eye - t.position).normalized * d;

                Transform card = T("SequenceCard_Face"), cardF = T("FrontPanel_SequenceCard_Face"), tcF = T("TC_Symbol_F");
                Transform master = T("Ctl_Toggle_Master"), magL = T("Ctl_Toggle_MagL"), magLF = T("Ctl_Toggle_MagL_Front");
                Transform sight = T("GL_FuelSight_Float"), radio = T("Radio_Display_R"), alt = T("Needle_Alt100_R");
                if (failures == 0)
                {
                    RigInputs off = Flying(1.0, false), on = Flying(1.0, true);
                    Shot("rear-seat-eye", eyeR, card.position, 70f, on);
                    Shot("rear-seat-eye-forward", eyeR, eyeR + new Vector3(0f, -0.25f, 1f), 80f, on);
                    Shot("front-seat-eye", eyeF, Vector3.Lerp(tcF.position, cardF.position, 0.35f), 70f, on);
                    Shot("front-seat-eye-forward", eyeF, eyeF + new Vector3(0f, -0.25f, 1f), 80f, on);
                    Shot("rear-card-space-close", Back(card, eyeR, 0.30f), card.position, 45f, on);
                    Shot("rear-altimeter-knob-kollsman", Back(alt, eyeR, 0.20f), alt.position, 35f, on);
                    Shot("front-card-close", Back(cardF, eyeF, 0.30f), cardF.position, 45f, on);
                    Shot("radios-under-panel", Back(radio, eyeR, 0.30f), radio.position + new Vector3(0f, 0f, 0f), 45f, on);
                    Shot("switch-console-rear-off", Back(master, eyeR, 0.30f), master.position, 40f, off);
                    Shot("switch-console-rear-on", Back(master, eyeR, 0.30f), master.position, 40f, on);
                    Shot("mag-switches-rear-off", Back(magL, eyeR, 0.16f), magL.position, 35f, off);
                    Shot("mag-switches-rear-on", Back(magL, eyeR, 0.16f), magL.position, 35f, on);
                    Shot("switch-console-front-on", Back(magLF, eyeF, 0.30f), magLF.position + (master.position - magL.position) * 0.5f, 40f, on);
                    foreach ((string tag, double f) in new[] { ("full", 1.0), ("half", 0.5), ("low", 0.15) })
                    {
                        Shot("sight-tube-" + tag, Back(sight, eyeF, 0.35f), sight.position + Vector3.down * 0.05f, 25f, Flying(f, true));
                        Debug.Log($"[GLPanelV2] sight ball at fuel {f:0.00}: y {sight.position.y:F4} (local {sight.localPosition.y:F4})");
                    }
                    Shot("sight-tube-from-front-seat", eyeF, sight.position, 45f, Flying(0.5, true));
                }
                builder.Clear();
            }
            catch (System.Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                foreach (GameObject go in new[] { root, camGo, sun }) if (go != null) Object.DestroyImmediate(go);
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[GLPanelV2] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures)");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        /// <summary>A running engine in cruise; switches all OFF or all ON; the top-wing tank at a fraction.</summary>
        private static RigInputs Flying(double fuel, bool switchesOn)
        {
            double s = switchesOn ? 1.0 : 0.0;
            return new RigInputs
            {
                HeadingDeg = 30, Throttle = 0.6, PropLever = 1.0, Mixture = 1.0, Magneto = switchesOn ? 3.0 : 0.0,
                MagLeft = s, MagRight = s, Master = s, Alternator = s, FuelPump = s, FuelValve = 1.0,
                Rpm = 2300, ManifoldInHg = 24, OilPressPsi = 72, OilTempF = 185, ChtF = 380, AirspeedMph = 110, AltitudeFt = 2500,
                GLoad = 1.0, GMaxSeen = 4.5, GMinSeen = -1.5, FuelTopWingFrac = fuel, FuelCentreFrac = fuel,
            };
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
                Debug.Log("[GLPanelV2] wrote " + path);
            }
            finally { cam.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}

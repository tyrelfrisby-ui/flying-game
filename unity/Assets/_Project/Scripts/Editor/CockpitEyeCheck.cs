using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// The cockpit camera must sit EXACTLY at the seated pilot's eye (owner Ty, approved 2026-10-31), so this
    /// drives the real <see cref="ChaseCamera"/> cockpit path for every aeroplane and every seat and asserts the
    /// camera lands on the authored eye within 1 mm, with nothing between the eye and the view.
    ///
    /// Why a batch check and not a unit test: the eye is authored in Core (configs) but the camera is Unity's,
    /// and tools/FlightTests compiles src/ only — a green .NET suite would say nothing about where the camera
    /// actually ends up. <see cref="FlyingGame.FlightTests.CockpitEyeTests"/> pins the data; this pins the camera.
    ///
    /// Run headless for the assertions:
    ///   Unity -batchmode -quit -nographics -executeMethod FlyingGame.EditorTools.CockpitEyeCheck.Run
    /// Drop -nographics and it also writes a cockpit view per seat to logs/cockpit/ for review.
    /// </summary>
    public static class CockpitEyeCheck
    {
        private const float ToleranceM = 0.001f;
        private const int ShotW = 1280, ShotH = 720;

        private static readonly string[] Fleet =
        {
            "glider-2-33-like", "c172-like", "j3-cub-like", "j3-cub-floats-like",
            "great-lakes-like", "pitts-s2b-like", "extra-300-like",
        };

        private static int _failures;

        private static void Require(bool ok, string message)
        {
            if (ok) return;
            _failures++;
            Debug.LogError("[CockpitEye] " + message);
        }

        public static void Run()
        {
            _failures = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            GameObject root = null, camGo = null;
            string shots = Path.Combine(Directory.GetCurrentDirectory(), "..", "logs", "cockpit");
            bool canRender = !SystemInfo.graphicsDeviceType.ToString().Contains("Null");
            try
            {
                Directory.CreateDirectory(shots);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;

                camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                Camera cam = camGo.AddComponent<Camera>();
                ChaseCamera chase = camGo.AddComponent<ChaseCamera>();
                // Identity, so the aeroplane's local frame IS world and a 1 mm comparison is a 1 mm comparison.
                root = new GameObject("Aircraft");
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                FlightSimDriver driver = root.AddComponent<FlightSimDriver>();
                CockpitView view = root.AddComponent<CockpitView>();
                view.Driver = driver;
                chase.Driver = driver; chase.Target = root.transform; chase.Cockpit = view;
                chase.CurrentView = ChaseCamera.View.Cockpit;

                MethodInfo lateUpdate = typeof(ChaseCamera)
                    .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(lateUpdate != null, "ChaseCamera.LateUpdate not found; the camera path cannot be driven");

                var builder = new AirframeBuilder();
                foreach (string id in Fleet)
                {
                    AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                    Require(cfg != null, id + ": no config in StreamingAssets");
                    if (cfg == null) continue;
                    // CockpitView reads Driver.Sim.Aircraft.Config, so without a sim it sees NO seats and the
                    // camera silently falls back to its canopy estimate -- which is exactly the bug being fixed,
                    // and would make this check pass against itself if the harness skipped it.
                    driver.AdoptSim(new FlyingGame.Sim.SimLoop(
                        new FlyingGame.Sim.Aircraft(cfg, new FlyingGame.Core.RigidBodyState())));
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    builder.Build(root.transform, cfg);

                    List<StationConfig> seats = cfg.Loading?.Seats ?? new List<StationConfig>();
                    Require(seats.Count > 0, id + ": no authored seats");
                    for (int i = 0; i < seats.Count; i++)
                    {
                        StationConfig seat = seats[i];
                        if (!seat.HasEye)
                        {
                            Debug.Log($"[CockpitEye] {id} seat {seat.Id}: no authored eye, camera falls back (expected for a rear bench)");
                            continue;
                        }
                        int before = _failures;
                        view.SelectSeat(i, remember: false);
                        lateUpdate?.Invoke(chase, null);

                        Vector3 want = CoordinateMap.ToUnity(seat.EyeVec());
                        Vector3 got = camGo.transform.position;
                        float off = Vector3.Distance(want, got);
                        Require(off <= ToleranceM,
                            $"{id}/{seat.Id}: camera is {off * 1000f:F2} mm from the eye " +
                            $"(want {want:F4}, got {got:F4}) — a fallback estimate, not the authored eye");

                        Require(cam.nearClipPlane <= 0.05f + 1e-4f,
                            $"{id}/{seat.Id}: near clip {cam.nearClipPlane:F4} m would clip the panel or canopy frame");

                        // REPORTED, not asserted: whether the eye clears the fuselage top line at its own
                        // station. The drawn fuselage is a closed body of revolution with no cockpit cutout, so
                        // any ray from a seated pilot's head hits it on the way out and no ray test here can
                        // tell "buried in structure" from "inside a shell, looking out of a hole the mesh does
                        // not model". For the three aeroplanes that carry sights, check_sights.py settles it
                        // against the real GLB: 808/808 true-eye rays clear. This number is for Ty's eye.
                        float top = FuselageTopAt(root, want);
                        string line = float.IsNaN(top) ? "no fuselage mesh at this station"
                            : $"fuselage top {top:F3} m, eye {want.y:F3} m, eye is {(want.y - top) * 100f:+0.0;-0.0} cm "
                              + (want.y >= top ? "ABOVE it" : "BELOW it");

                        if (_failures != before) continue;
                        Debug.Log($"[CockpitEye] {id} seat {seat.Id}{(i == 0 ? " (primary)" : "")} " +
                                  $"OK: camera {off * 1000f:F3} mm from the eye, near clip {cam.nearClipPlane:F3} m, " +
                                  $"body ({seat.EyeVec().X:F3}, {seat.EyeVec().Y:F3}, {seat.EyeVec().Z:F3}); {line}");

                        if (canRender)
                        {
                            // The sights are the reason the look-around exists, so the review shot has to show
                            // them. CockpitSights stays off until a LOCAL driver is bound -- AirframeVisual.Rebuild
                            // does that in the game; this harness drives AirframeBuilder directly, so bind it here.
                            foreach (CockpitSights g in root.GetComponentsInChildren<CockpitSights>(true))
                                g.BindLocal(driver);
                            // Ahead, then both 90-degree snaps. The sight bars sit on the eye's own lateral line
                            // -- 1.85 to 3.94 m out, depending on type -- so looking AHEAD never shows them and a
                            // forward-only review shot would say nothing about whether they line up.
                            foreach ((string tag, CockpitLook.Direction dir) in new[]
                                     { ("ahead", CockpitLook.Direction.Centre),
                                       ("look-left", CockpitLook.Direction.Left),
                                       ("look-right", CockpitLook.Direction.Right) })
                            {
                                view.Look.Snap(dir);
                                lateUpdate?.Invoke(chase, null);
                                Shoot(cam, Path.Combine(shots, $"{id}-{seat.Id}-{tag}.png"));
                            }
                            view.Look.Snap(CockpitLook.Direction.Centre);
                            lateUpdate?.Invoke(chase, null);
                        }
                    }
                }
                builder.Clear();
                if (!canRender)
                    Debug.Log("[CockpitEye] no graphics device (-nographics): assertions ran, no cockpit views written. " +
                              "Re-run without -nographics to write logs/cockpit/.");
            }
            catch (Exception e) { _failures++; Debug.LogException(e); }
            finally
            {
                foreach (GameObject go in new[] { root, camGo }) if (go != null) Object.DestroyImmediate(go);
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[CockpitEye] {(_failures == 0 ? "PASS" : "FAIL")} ({_failures} failures)");
                EditorApplication.Exit(_failures == 0 ? 0 : 1);
            }
        }

        /// <summary>The highest point of the fuselage skin within 15 cm of the eye's own station, in world Y,
        /// or NaN when no fuselage mesh reaches that station. Reported, never asserted — see the caller.</summary>
        private static float FuselageTopAt(GameObject model, Vector3 eye)
        {
            float top = float.NaN;
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                string n = mf.gameObject.name;
                if (!n.Contains("Fuselage") && !n.Contains("Cowl") && !n.Contains("Turtle")) continue;
                foreach (Vector3 local in mesh.vertices)
                {
                    Vector3 w = mf.transform.TransformPoint(local);
                    if (Mathf.Abs(w.z - eye.z) > 0.15f) continue;      // Unity +z is body +x: the same station
                    if (float.IsNaN(top) || w.y > top) top = w.y;
                }
            }
            return top;
        }

        private static void Shoot(Camera cam, string path)
        {
            RenderTexture rt = RenderTexture.GetTemporary(ShotW, ShotH, 24);
            RenderTexture old = cam.targetTexture, active = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
                shot.Apply();
                File.WriteAllBytes(path, shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                Debug.Log("[CockpitEye] wrote " + path);
            }
            finally
            {
                cam.targetTexture = old;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}

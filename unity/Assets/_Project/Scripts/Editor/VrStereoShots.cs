using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Vr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// The full side-by-side composite, at phone and tablet resolutions, so the stereo can be checked without a
    /// headset: the two halves must show the SAME cockpit from points an IPD apart, and the parallax must be
    /// visible on near objects (the panel) and vanish on far ones.
    ///
    /// It renders through <see cref="IVrCompositor"/>, so when Codex's distortion and chromatic-aberration pass
    /// replaces the plain compositor, these shots show it with no change to this tool.
    ///
    ///   VR_SHOTS_OUT=/somewhere Unity -batchmode -quit \
    ///     -executeMethod FlyingGame.EditorTools.VrStereoShots.Render
    /// </summary>
    public static class VrStereoShots
    {
        /// <summary>iPad mini landscape, then iPhone 17 Pro landscape: name, pixels, and the DIAGONAL in
        /// inches, because the lens geometry is in metres and a pixel count on its own says nothing about how
        /// big the screen behind the lens actually is.</summary>
        private static readonly (string Name, int W, int H, double DiagIn)[] Screens =
        {
            ("ipad-mini-2266x1488", 2266, 1488, 8.3),
            ("iphone-2532x1170", 2532, 1170, 6.1),
        };

        /// <summary>A type with an authored eye in both seats; the front seat is the one with the panel close.</summary>
        private const string Aircraft = "great-lakes-like";
        private const int FrontSeat = 1;

        public static void Render()
        {
            string outDir = System.Environment.GetEnvironmentVariable("VR_SHOTS_OUT")
                            ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "build", "vr-shots");
            int failures = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            GameObject root = null, camGo = null;
            try
            {
                Directory.CreateDirectory(outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;

                camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                Camera cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.05f;
                ChaseCamera chase = camGo.AddComponent<ChaseCamera>();
                chase.CurrentView = ChaseCamera.View.Cockpit;

                root = new GameObject("Aircraft");
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                FlightSimDriver driver = root.AddComponent<FlightSimDriver>();
                CockpitView cockpit = root.AddComponent<CockpitView>();
                cockpit.Driver = driver;
                chase.Driver = driver; chase.Target = root.transform; chase.Cockpit = cockpit;

                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(Aircraft);
                if (cfg == null) { Debug.LogError("[VRShots] no config for " + Aircraft); EditorApplication.Exit(1); return; }
                driver.AdoptSim(new FlyingGame.Sim.SimLoop(
                    new FlyingGame.Sim.Aircraft(cfg, new FlyingGame.Core.RigidBodyState())));
                new AirframeBuilder().Build(root.transform, cfg);
                cockpit.SelectSeat(FrontSeat, remember: false);

                VrLensConfig lens = VrMode.Lens;
                Debug.Log($"[VRShots] lens: inter-lens {lens.InterLensDistanceM * 1000f:F1} mm, " +
                          $"screen-to-lens {lens.ScreenToLensDistanceM * 1000f:F1} mm, k1 {lens.K1:F6}, k2 {lens.K2:F6}, " +
                          $"IPD {VrMode.IpdM * 1000f:F0} mm, render scale {lens.RenderScale:F2}");

                VrMode.ForceAvailableForTesting = true;   // a Mac reports no gyroscope; the poses are fixed here anyway
                var rig = camGo.AddComponent<VrRig>();
                bool wasOn = VrMode.On;
                VrMode.On = true;
                rig.SetActiveForTesting(true);          // edit mode: no Awake/OnEnable, so build the rig by hand
                foreach ((string name, int w, int h, double diagIn) in Screens)
                    foreach ((string pose, float yaw, float pitch, float roll) in
                             new[] { ("ahead", 0f, 0f, 0f), ("yaw30-roll15", 30f, 0f, 15f) })
                    {
                        // Size the rig to THIS device before posing it, so the picture is rendered at its own
                        // resolution with its own physical screen rather than upscaled from the batch window.
                        double aspect = System.Math.Sqrt((double)w * w + (double)h * h);
                        float wM = (float)(diagIn * 0.0254 * w / aspect), hM = (float)(diagIn * 0.0254 * h / aspect);
                        rig.SetActiveForTesting(false);
                        rig.SetScreenForTesting(w, h, wM, hM);
                        rig.SetActiveForTesting(true);
                        rig.SetHeadForTesting(yaw, pitch, roll);
                        if (rig.LeftEye == null || rig.RightEye == null || rig.Compositor == null)
                        {
                            Debug.LogError("[VRShots] the rig built no eyes — is a gyro reported in batch mode?");
                            failures++; break;
                        }
                        float sep = Vector3.Distance(rig.LeftEye.transform.position, rig.RightEye.transform.position);
                        if (Mathf.Abs(sep - VrMode.IpdM) > 0.0005f)
                        { Debug.LogError($"[VRShots] the eyes are {sep * 1000f:F1} mm apart, not {VrMode.IpdM * 1000f:F0}"); failures++; }
                        Shoot(rig, w, h, Path.Combine(outDir, $"vr-{name}-{pose}.png"));
                    }
                rig.SetActiveForTesting(false);
                VrMode.On = wasOn;
                VrMode.ForceAvailableForTesting = false;
            }
            catch (System.Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                foreach (GameObject go in new[] { root, camGo }) if (go != null) Object.DestroyImmediate(go);
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[VRShots] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures)");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        /// <summary>Render both eyes and put the composite on disk. Batch mode has no backbuffer, so the
        /// compositor is pointed at a RenderTexture of the device's size instead — which is why
        /// <see cref="IVrCompositor.Composite"/> takes its dimensions from the destination. It goes through
        /// the live compositor on purpose: once Codex's distortion and chromatic-aberration pass is the one
        /// installed, these shots show it and this tool does not change.</summary>
        private static void Shoot(VrRig rig, int w, int h, string path)
        {
            rig.LeftEye.Render();
            rig.RightEye.Render();
            RenderTexture outRt = RenderTexture.GetTemporary(w, h, 24);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = outRt;
                GL.Clear(true, true, Color.black);
                rig.Compositor.Composite(outRt);
                RenderTexture.active = outRt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[VRShots] wrote " + path);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(outRt); }
        }
    }
}

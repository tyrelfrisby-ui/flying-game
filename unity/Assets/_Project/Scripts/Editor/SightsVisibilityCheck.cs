using System;
using System.Linq;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Headless EditMode check of imported sight renderers and actual camera callback handlers.
    /// No graphics device or NUnit package needed. Run with -executeMethod
    /// FlyingGame.EditorTools.SightsVisibilityCheck.Run. Throws/exits 1 on any visibility leak.
    /// </summary>
    public static class SightsVisibilityCheck
    {
        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("[SightsVisibility] " + message);
        }

        private static Renderer[] Bars(GameObject root) => root.GetComponentsInChildren<Renderer>(true)
            .Where(r => CockpitSights.IsSight(r.transform)).ToArray();

        private static void Check(Renderer[] bars, bool visible, string context)
        {
            Require(bars.Length == 8, context + ": expected eight imported bars, found " + bars.Length);
            foreach (var bar in bars) Require(bar.enabled == visible, context + ": " + bar.name + " enabled=" + bar.enabled);
        }

        private static void Frame(Camera camera, Renderer[] local, Renderer[] remote, bool visible, string context)
        {
            Camera.onPreCull?.Invoke(camera);
            try
            {
                Check(local, visible, context);
                Check(remote, false, context + " remote");
            }
            finally { Camera.onPostRender?.Invoke(camera); }
            Check(local, false, context + " after render");
            Check(remote, false, context + " remote after render");
        }

        public static void Run()
        {
            int failures = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            bool oldReplay = SessionSettings.ReplayActive;
            string oldLivery = SessionSettings.LiveryOverride;
            GameObject root = null, remote = null, mainGo = null, otherGo = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                SessionSettings.ReplayActive = false;
                mainGo = new GameObject("Main Camera") { tag = "MainCamera" };
                var main = mainGo.AddComponent<Camera>();
                var chase = mainGo.AddComponent<ChaseCamera>();
                otherGo = new GameObject("OtherCamera");
                var other = otherGo.AddComponent<Camera>();
                root = new GameObject("LocalAircraft");
                var driver = root.AddComponent<FlightSimDriver>();
                chase.Driver = driver; chase.Target = root.transform;
                var builder = new AirframeBuilder();
                foreach (string id in new[] { "extra-300-like", "pitts-s2b-like", "great-lakes-like" })
                {
                    var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                    var liveries = id == "extra-300-like" ? new[] { "unmarked", "talk-flying", "n300uy" }
                        : id == "pitts-s2b-like" ? new[] { "checker-unmarked", "factory-unmarked", "n31dg", "n1191" } : new[] { "" };
                    foreach (string livery in liveries.Concat(liveries)) // rebuild/switch back too
                    {
                        SessionSettings.LiveryOverride = livery;
                        builder.Build(root.transform, cfg);
                        var local = Bars(root);
                        Check(local, false, id + " spawn");
                        var guard = root.GetComponentInChildren<CockpitSights>(true);
                        Require(guard != null, id + ": missing guard");
                        guard.BindLocal(driver); // the same explicit ownership granted by AirframeVisual.Rebuild
                        remote = new GameObject("UnboundRemoteOrWidget");
                        Require(AirframeModels.Place(remote.transform, id, cfg, out _) != null, id + ": missing remote model");
                        var remoteBars = Bars(remote);
                        Check(remoteBars, false, id + " remote spawn");
                        foreach (ChaseCamera.View view in Enum.GetValues(typeof(ChaseCamera.View)))
                        {
                            chase.CurrentView = view;
                            Frame(main, local, remoteBars, view == ChaseCamera.View.Cockpit, id + " " + view);
                        }
                        chase.CurrentView = ChaseCamera.View.Cockpit;
                        chase.SideView = true; Frame(main, local, remoteBars, false, "side lesson"); chase.SideView = false;
                        SessionSettings.ReplayActive = true; Frame(main, local, remoteBars, false, "replay"); SessionSettings.ReplayActive = false;
                        driver.Replaying = true; Frame(main, local, remoteBars, false, "driver replay"); driver.Replaying = false;
                        chase.OverrideTarget = remote.transform; Frame(main, local, remoteBars, false, "egress"); chase.OverrideTarget = null;
                        chase.Target = null; Frame(main, local, remoteBars, false, "null target"); chase.Target = root.transform;
                        chase.enabled = false; Frame(main, local, remoteBars, false, "disabled chase"); chase.enabled = true;
                        // CopyFrom reproduces ClipRecorder and WaterReflection. All other cameras are denied,
                        // including LessonIllustrator, widget, scene/editor previews, and future cameras.
                        other.CopyFrom(main);
                        Frame(other, local, remoteBars, false, "CopyFrom clip/reflection/lesson/unknown camera");
                        Camera.onPreCull?.Invoke(main);
                        Check(local, true, "main before nested reflection");
                        Camera.onPreCull?.Invoke(other);
                        Check(local, false, "nested reflection");
                        Camera.onPostRender?.Invoke(other);
                        Check(local, true, "main restored after reflection");
                        Check(remoteBars, false, "remote nested");
                        Camera.onPostRender?.Invoke(main);
                        Check(local, false, "main render finished");
                        guard.BindLocal(null);
                        Frame(main, local, remoteBars, false, "unbound local");
                        guard.BindLocal(driver);
                        guard.enabled = false;
                        Frame(main, local, remoteBars, false, "disabled guard");
                        guard.enabled = true;
                        Frame(main, local, remoteBars, true, "re-enabled guard");
                        Vector3 eye = PilotEgress.CockpitLocal(driver) + Vector3.up * 0.1f;
                        Debug.Log($"[SightsVisibility] {id} {livery} PASS; cockpit body m=({eye.z:F6},{eye.x:F6},{-eye.y:F6})");
                        Object.DestroyImmediate(remote); remote = null;
                    }
                }
                builder.Clear();
            }
            catch (Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                foreach (var go in new[] { remote, root, mainGo, otherGo }) if (go != null) Object.DestroyImmediate(go);
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                SessionSettings.ReplayActive = oldReplay; SessionSettings.LiveryOverride = oldLivery;
                Debug.Log($"[SightsVisibility] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures)");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }
    }
}

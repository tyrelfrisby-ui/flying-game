using System.Linq;
using System.Reflection;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-04: "I crashed and every time I restart it is still crashed, even at different locations or in different
    /// airplanes." Drives the REAL AirframeVisual + StructuralDamage on a FlightSimDriver (edit mode: Start is invoked by
    /// hand, CLAUDE.md "An editor batch tool has to drive the component itself"): bend a wing panel and strike the prop on
    /// the drawn model, then install a new aeroplane the way Restart / Relocate / a new type do (the driver's Sim setter,
    /// here through AdoptSim) and require the model to be undamaged before the new aeroplane has taken a step.
    /// <c>-executeMethod FlyingGame.EditorTools.CrashResetCheck.Run</c>; exit 0 = pass.
    /// </summary>
    public static class CrashResetCheck
    {
        private static int _failures;
        private static void Require(bool ok, string message) { if (ok) return; _failures++; Debug.LogError("[CrashReset] " + message); }

        private static bool ShowsDamage(AirframeVisual v) =>
            (bool)typeof(AirframeVisual).GetProperty("ShowsDamage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(v);

        private static void Invoke(object o, string method) =>
            o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(o, null);

        private static void SetId(FlightSimDriver d, string id) => typeof(FlightSimDriver).GetProperty("AircraftId").GetSetMethod(true).Invoke(d, new object[] { id });

        private static FlyingGame.Sim.SimLoop NewSim(AircraftConfig cfg) =>
            new(new FlyingGame.Sim.Aircraft(cfg, new RigidBodyState(new FlyingGame.Core.MathTypes.Vec3(0, 0, -500), FlyingGame.Core.MathTypes.Quat.Identity,
                new FlyingGame.Core.MathTypes.Vec3(40, 0, 0), FlyingGame.Core.MathTypes.Vec3.Zero)));

        public static void Run()
        {
            _failures = 0;
            GameObject root = null;
            try
            {
                foreach ((string crashed, string next) in new[] { ("c172-like", "c172-like"), ("pitts-s2b-like", "pitts-s2b-like"), ("c172-like", "pitts-s2b-like") })
                {
                    root = new GameObject("Aircraft");
                    FlightSimDriver driver = root.AddComponent<FlightSimDriver>();
                    AircraftConfig a = UnityAircraftConfigLoader.LoadFromStreamingAssets(crashed);
                    SetId(driver, crashed);
                    driver.AdoptSim(NewSim(a));
                    AirframeVisual visual = root.AddComponent<AirframeVisual>();
                    StructuralDamage damage = root.AddComponent<StructuralDamage>();
                    Invoke(visual, "Awake"); Invoke(visual, "Start");
                    Invoke(damage, "Awake"); Invoke(damage, "Start");
                    int intactRenderers = root.GetComponentsInChildren<Renderer>(false).Length;

                    // The crash: the tip panel bends up on its pivot, the prop is struck.
                    var wreck = driver.Sim.Aircraft;
                    WingPanel panel = wreck.Panels.Count > 0 ? wreck.Panels[0] : null;
                    if (panel != null) visual.ShowPanelBend(panel, a);
                    visual.DetachComponent(AirframeComponent.Propeller, Vector3.zero);
                    Require(ShowsDamage(visual), $"{crashed}: the model does not report the crash it was given");
                    bool hadBent = root.transform.Find("Bent-" + panel?.Component) != null;
                    Require(panel == null || hadBent, $"{crashed}: no bent panel pivot after ShowPanelBend");

                    // Restart / relocate / new type: a new sim Aircraft.
                    AircraftConfig b = next == crashed ? a : UnityAircraftConfigLoader.LoadFromStreamingAssets(next);
                    SetId(driver, next);
                    driver.AdoptSim(NewSim(b));
                    Require(!ShowsDamage(visual), $"{crashed} -> {next}: the model still shows the last aeroplane's damage");
                    Require(panel == null || root.transform.Find("Bent-" + panel.Component) == null, $"{crashed} -> {next}: the bent panel survived");
                    Require(root.GetComponentsInChildren<Transform>(true).All(t => t.name != "BentBladeTip"), $"{crashed} -> {next}: the struck prop survived");
                    int renderers = root.GetComponentsInChildren<Renderer>(false).Length;
                    if (next == crashed) Require(renderers == intactRenderers, $"{crashed}: {renderers} renderers after the reset, {intactRenderers} when new");
                    Debug.Log($"[CrashReset] {crashed} -> {next}: damage cleared; renderers {intactRenderers} new, {renderers} after reset");
                    Object.DestroyImmediate(root); root = null;
                }
            }
            catch (System.Exception e) { _failures++; Debug.LogError("[CrashReset] threw: " + e); }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                Debug.Log(_failures == 0 ? "[CrashReset] PASS" : $"[CrashReset] FAIL ({_failures})");
                EditorApplication.Exit(_failures == 0 ? 0 : 1);
            }
        }
    }
}

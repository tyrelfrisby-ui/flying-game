using System;
using System.Collections;
using System.Reflection;
using FlyingGame.Bridge;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Every aeroplane with an engine must have a propeller that TURNS (owner 2026-11-01, via Aero: "the extras
    /// propeller is static").
    ///
    /// Why a batch check and not a unit test: the binding is Unity's — hub, blade bone and blur disc are
    /// GameObjects built by <see cref="AirframeBuilder"/> — and tools/FlightTests compiles src/ only, so a green
    /// .NET suite says nothing at all about it.
    ///
    /// There are two ways a model's propeller gets attached, and the check has to know the difference:
    ///  - a DOWNLOADED model is carved: its propeller vertices are skinned to the hub's bones, so the blades turn
    ///    while the node's TRANSFORM never moves. Only <c>PropHub.ModelBound</c> shows it worked.
    ///  - a model with its OWN rig skips the carve, so <see cref="AirframeBuilder.BindModelProp"/> re-parents its
    ///    Prop and Spinner nodes under the hub, and the transform does turn.
    /// Measuring only the transform marks every carved aeroplane static, which is how the first version of this
    /// check reported four false failures.
    ///
    ///   Unity -batchmode -quit -nographics -executeMethod FlyingGame.EditorTools.PropSpinCheck.Run
    /// </summary>
    public static class PropSpinCheck
    {
        private static readonly string[] Fleet =
        {
            "extra-300-like", "great-lakes-like", "pitts-s2b-like", "j3-cub-like", "c172-like", "glider-2-33-like",
        };

        /// <summary>The types drawn from a GLB we made ourselves, where the geometry is ours to fix and a static
        /// propeller is a defect rather than a limitation of someone else's mesh.</summary>
        private static readonly string[] Drawn = { "extra-300-like", "great-lakes-like", "pitts-s2b-like" };

        private static int _failures;

        private static void Require(bool ok, string message)
        {
            if (ok) return;
            _failures++;
            Debug.LogError("[PropSpin] " + message);
        }

        public static void Run()
        {
            _failures = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            GameObject root = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                root = new GameObject("Aircraft");
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var builder = new AirframeBuilder();
                FieldInfo propsField = typeof(AirframeBuilder)
                    .GetField("_props", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(propsField != null, "AirframeBuilder._props not found; the propellers cannot be inspected");

                foreach (string id in Fleet)
                {
                    AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                    Require(cfg != null, id + ": no config in StreamingAssets");
                    if (cfg == null) continue;
                    int before = _failures;
                    builder.Build(root.transform, cfg);

                    bool hasEngine = cfg.Propulsion != null && cfg.Propulsion.MaxPowerW > 0.0;
                    var hubs = (IEnumerable)propsField?.GetValue(builder);
                    int count = 0; object first = null;
                    if (hubs != null) foreach (object h in hubs) { if (count++ == 0) first = h; }

                    if (!hasEngine)
                    {
                        Require(count == 0, $"{id}: a glider with {count} propeller hubs?");
                        Debug.Log($"[PropSpin] {id}: no engine, nothing to turn (correct).");
                        builder.Clear();
                        continue;
                    }
                    Require(count > 0, id + ": an engine but no propeller hub at all");
                    if (first == null) { builder.Clear(); continue; }

                    bool modelBound = (bool)first.GetType().GetField("ModelBound").GetValue(first);
                    if (System.Array.IndexOf(Drawn, id) >= 0)
                    {
                        Require(modelBound,
                            $"{id}: the model's propeller is not attached to the turning hub — it will stand still "
                            + "whatever the engine does");
                    }
                    else if (!modelBound)
                    {
                        // A DOWNLOADED model whose propeller the carve could not separate keeps its own static
                        // blades and gets no blur disc. AirframeVisual already handles that deliberately, so it
                        // is reported rather than failed: fixing it means re-cutting someone else's mesh.
                        Debug.LogWarning($"[PropSpin] {id}: the carve could not separate this model's propeller, "
                                         + "so it keeps its own static blades (known, see AGENT-LOG NEEDS YOU).");
                    }

                    // The rigged path re-parents the node, so there the transform itself must turn. 600 rpm over
                    // 0.05 s is 180 degrees: far enough to measure, and below the rpm at which the blades give
                    // way to the blur disc, so the blades themselves have to move.
                    Transform blades = Find(root.transform, "Prop");
                    bool reparented = blades != null && InsideHub(blades);
                    if (reparented)
                    {
                        Vector3 up = blades.up, at = blades.position;
                        builder.SpinProps(_ => 600f, 0.05f);
                        float turned = Vector3.Angle(up, blades.up);
                        Require(turned > 1f, $"{id}: SpinProps turned the model's blades {turned:F3} deg");
                        Require(Vector3.Distance(at, blades.position) < 0.02f,
                            $"{id}: the blades moved {Vector3.Distance(at, blades.position) * 100f:F1} cm off their "
                            + "own axis as they turned — the hub is not centred on the propeller");
                        if (_failures == before)
                            Debug.Log($"[PropSpin] {id} OK (own rig): the model's blades turned {turned:F1} deg at "
                                      + "600 rpm over 0.05 s, about their own axis.");
                    }
                    else if (_failures == before && modelBound)
                    {
                        Debug.Log($"[PropSpin] {id} OK (carved): the model's propeller is skinned to the hub's "
                                  + "bones, so the blades turn with it and the node stays put.");
                    }
                    builder.Clear();
                }
            }
            catch (Exception e) { _failures++; Debug.LogException(e); }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[PropSpin] {(_failures == 0 ? "PASS" : "FAIL")} ({_failures} failures)");
                EditorApplication.Exit(_failures == 0 ? 0 : 1);
            }
        }

        /// <summary>The model's node, whatever TryPlaceModel renamed it to. EXACT name only: the Extra's rig has a
        /// node called Prop_R and it is the blue rpm vernier on the rear panel, not a blade.</summary>
        private static Transform Find(Transform root, string node)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name.StartsWith("Model:") ? t.name.Substring(6) : t.name;
                if (name == node) return t;
            }
            return null;
        }

        private static bool InsideHub(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name == "PropHub") return true;
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-04 19:16: the Extra's 30 % aileron hinge (nose standing proud of the skin TE-down) and the Great Lakes'
    /// slave struts following both ailerons, rendered through the game's own build path (AirframeBuilder + AirframeRig,
    /// the same code iOS and the Quest run) at full and neutral aileron, and MEASURED there: each strut's two ends
    /// against its two pins, in the engine, after the rig has moved everything.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.HingeSlaveStrutShots.Render -out &lt;dir&gt;
    /// Needs graphics (no -nographics). Prints STRUT lines; exits 1 if a strut end is more than 3 mm off its pin.
    /// </summary>
    public static class HingeSlaveStrutShots
    {
        public static void Render()
        {
            int exit = 1;
            string[] args = Environment.GetCommandLineArgs();
            int oi = Array.IndexOf(args, "-out");
            string outDir = Path.GetFullPath(oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : Path.Combine(Application.dataPath, "../../renders/hinge-slavestruts"));
            bool prevModels = SessionSettings.UseAirframeModels;
            var builder = new AirframeBuilder();
            GameObject root = null, camGo = null, sunGo = null;
            RenderTexture rt = null;
            try
            {
                Directory.CreateDirectory(outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = true;
                sunGo = new GameObject("Sun");
                var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.60f, 0.70f);
                RenderSettings.fog = false;
                camGo = new GameObject("Cam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.74f, 0.94f);
                cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 200f;
                rt = new RenderTexture(1400, 1000, 24) { antiAliasing = 8 };
                cam.targetTexture = rt; cam.aspect = 1.4f;

                bool ok = true;
                // ---- the Extra: nose of the TE-down aileron rises above the wing ----
                {
                    AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("extra-300-like");
                    root = new GameObject("extra-300-like");
                    builder.Build(root.transform, cfg);
                    float max = (float)cfg.Controls.Aileron.MaxDeflRad;
                    Transform al = Find(root, "Aileron_L"), ar = Find(root, "Aileron_R");
                    if (al == null || ar == null) throw new InvalidOperationException("Extra: no Aileron_L / Aileron_R in the placed model");
                    Debug.Log($"HINGE extra: rig drives {builder.Rig?.Nodes.Count ?? 0} nodes; Aileron_L pivot (model local) {al.localPosition.ToString("F4")}");
                    foreach ((string tag, float stick) in new[] { ("neutral", 0f), ("right-roll", 1f), ("left-roll", -1f) })
                    {
                        Prep(root);
                        builder.SetDeflections(stick * max, 0f, 0f, 0f);
                        // The TE-down aileron is the left one on right roll, the right one on left roll.
                        foreach ((string side, Transform a) in new[] { ("L", al), ("R", ar) })
                        {
                            if (tag == "right-roll" && side == "R" || tag == "left-roll" && side == "L") continue;
                            Bounds b = BoundsOf(a);
                            float sx = side == "L" ? -1f : 1f;
                            // Along the span from outboard, a little aft and above: the aileron's profile, nose and cove.
                            Vector3 at = Vector3.Lerp(b.center, new Vector3(b.center.x + sx * b.extents.x, b.center.y, b.center.z), 0.55f);
                            Shot(cam, rt, at, new Vector3(sx * 1f, 0.35f, -0.25f), 1.1f, Path.Combine(outDir, $"extra_{tag}_aileron{side}_span.png"));
                            Shot(cam, rt, b.center, new Vector3(sx * 0.35f, 0.55f, -1f), 2.6f, Path.Combine(outDir, $"extra_{tag}_aileron{side}_aft.png"));
                        }
                    }
                    builder.Clear(); Object.DestroyImmediate(root); root = null;
                }
                // ---- the Great Lakes: slave struts on both horns at both full deflections ----
                {
                    AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("great-lakes-like");
                    root = new GameObject("great-lakes-like");
                    builder.Build(root.transform, cfg);
                    float max = (float)cfg.Controls.Aileron.MaxDeflRad;
                    var ends = new Dictionary<string, (Transform strut, Transform pu, Transform pl, Vector3 lowerLocal, Vector3 restPu)>();
                    builder.SetDeflections(0f, 0f, 0f, 0f);
                    foreach (string s in new[] { "L", "R" })
                    {
                        Transform st = Find(root, "Strut_Slave_" + s), pu = Find(root, "Strut_Slave_" + s + "_PinUpper"), pl = Find(root, "Strut_Slave_" + s + "_PinLower");
                        if (st == null || pu == null || pl == null) throw new InvalidOperationException($"Great Lakes: Strut_Slave_{s} or a pin is missing from the placed model");
                        // The strut's lower end in its own frame, at rest: where the lower pin is then.
                        ends[s] = (st, pu, pl, st.InverseTransformPoint(pl.position), pu.position);
                    }
                    foreach ((string tag, float stick) in new[] { ("neutral", 0f), ("right-roll", 1f), ("left-roll", -1f) })
                    {
                        Prep(root);
                        builder.SetDeflections(stick * max, 0f, 0f, 0f);
                        foreach (string s in new[] { "L", "R" })
                        {
                            var e = ends[s];
                            float upOff = (e.strut.position - e.pu.position).magnitude * 1000f;
                            float loOff = (e.strut.TransformPoint(e.lowerLocal) - e.pl.position).magnitude * 1000f;
                            float moved = (e.pu.position - e.restPu).magnitude * 1000f;
                            Debug.Log($"STRUT {s} {tag} (stick {stick:+0;-0;0}): upper end {upOff:F2} mm and lower end {loOff:F2} mm off their pins; upper pin moved {moved:F0} mm");
                            if (upOff > 3f || loOff > 3f) ok = false;
                            Bounds b = BoundsOf(e.strut);
                            float sx = s == "L" ? -1f : 1f;
                            Shot(cam, rt, b.center, new Vector3(sx * 0.9f, 0.15f, -1f), 1.6f, Path.Combine(outDir, $"greatlakes_{tag}_strut{s}_aft.png"));
                            Shot(cam, rt, b.center, new Vector3(sx * 1f, 0.05f, 0.05f), 1.4f, Path.Combine(outDir, $"greatlakes_{tag}_strut{s}_side.png"));
                        }
                    }
                    builder.Clear(); Object.DestroyImmediate(root); root = null;
                }
                Debug.Log(ok ? $"HingeSlaveStrutShots: PASS -> {outDir}" : $"HingeSlaveStrutShots: FAIL (a strut end is off its pin) -> {outDir}");
                exit = ok ? 0 : 1;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                SessionSettings.UseAirframeModels = prevModels;
                RenderTexture.active = null;
                builder.Clear();
                if (root != null) Object.DestroyImmediate(root);
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (sunGo != null) Object.DestroyImmediate(sunGo);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                EditorApplication.Exit(exit);
            }
        }

        private static void Prep(GameObject root)
        {
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
        }

        private static Transform Find(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name == "Model:" + name) return t;
            return null;
        }

        private static Bounds BoundsOf(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.one * 0.3f);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static void Shot(Camera cam, RenderTexture rt, Vector3 at, Vector3 dir, float dist, string path)
        {
            cam.transform.position = at + dir.normalized * dist;
            cam.transform.LookAt(at, Vector3.up);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("HingeSlaveStrutShots: wrote " + path);
        }
    }
}

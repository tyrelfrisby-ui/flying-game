using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Do the DRAWN control surfaces move? (Ty t917u 2026-11-06: "The elevator on the pitts is not visibly moving.")
    /// Builds each type exactly as the game does (AirframeBuilder.Build, own models where the game uses them), poses it
    /// through the one call every caller uses (SetDeflections: the live aeroplane, a replay, a remote player, a render),
    /// bakes every visible renderer, and measures which vertices moved, in the airframe root's frame:
    ///   ELEVATOR  vertices in the aft 30 % of the airframe, left and right of the centreline separately, and the
    ///             vertical travel of the aft-most moved vertex on each side (+elevator = TE DOWN, Unity -y).
    ///   AILERON   vertices outboard of 40 % semispan, each side: + aileron = left TE down, right TE up.
    ///   RUDDER    aft 30 %, within 25 cm of the centreline: + rudder = TE to the right (Unity +x).
    /// It also checks the motion is PROPORTIONAL (half the deflection moves the trailing edge about half as far) and
    /// that neutral returns every vertex to where it started.
    ///
    /// The cockpit (stick, pedals) moves with the same inputs and is not a surface, so only the aft / outboard regions
    /// are counted. A part counts as "moving" when it travels more than 1 cm at the full deflection used here.
    ///
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.ControlSurfaceMotionCheck.Run
    ///   SURFACE_TYPES=pitts-s2b-like,extra-300-like  (default: every type in StreamingAssets/aircraft)
    ///   SURFACE_REPORT=&lt;file&gt;  (default build/surface-motion/report.txt)
    /// Exit code 1 when any listed type FAILs (SURFACE_STRICT=0 to always exit 0).
    /// </summary>
    public static class ControlSurfaceMotionCheck
    {
        private const float Defl = 0.35f;     // rad, at or under every type's travel
        private const float MovedM = 0.01f;

        public static void Run()
        {
            int exit = 1;
            var lines = new List<string>();
            bool anyFail = false;
            bool prevModels = SessionSettings.UseAirframeModels, prevOwn = SessionSettings.UseOwnModels;
            string prevLivery = SessionSettings.LiveryOverride;
            try
            {
                string list = Environment.GetEnvironmentVariable("SURFACE_TYPES");
                IEnumerable<string> ids = !string.IsNullOrWhiteSpace(list)
                    ? list.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                    : Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "aircraft"), "*.json")
                        .Select(Path.GetFileNameWithoutExtension).OrderBy(s => s);
                SessionSettings.UseAirframeModels = true;
                SessionSettings.LiveryOverride = null;
                foreach (string id in ids)
                {
                    try { anyFail |= !Check(id, lines); }
                    catch (Exception e) { lines.Add($"{id}: ERROR {e.GetType().Name}: {e.Message}"); anyFail = true; }
                }
                string report = Environment.GetEnvironmentVariable("SURFACE_REPORT");
                if (string.IsNullOrWhiteSpace(report)) report = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/surface-motion/report.txt"));
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllLines(report, lines);
                foreach (string l in lines) Debug.Log("[SurfaceMotion] " + l);
                Debug.Log($"[SurfaceMotion] {(anyFail ? "FAIL" : "PASS")} — report {report}");
                exit = anyFail && Environment.GetEnvironmentVariable("SURFACE_STRICT") != "0" ? 1 : 0;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                SessionSettings.UseAirframeModels = prevModels; SessionSettings.UseOwnModels = prevOwn; SessionSettings.LiveryOverride = prevLivery;
                EditorApplication.Exit(exit);
            }
        }

        /// <summary>
        /// Before/after evidence: the Pitts (or SURFACE_TYPES' first type) at the sim's own elevator deflections, as
        /// tools/FlightTests/SurfaceHingeTests.PittsStickGivesAFullProportionalElevator measured them (stick neutral 0,
        /// full aft -27.0 deg, half aft -12.1 deg, full forward +27.0 deg), each view a 2 x 2 grid in that order:
        /// "chase" (behind and above, the external camera's view), "tail34" (rear three-quarter) and "tailside";
        /// plus "roll" (full right aileron and right rudder, from behind).
        ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.ControlSurfaceMotionCheck.Shots
        ///   SURFACE_SHOTS=&lt;dir&gt; [SURFACE_TAG=before|after] [AERO_LIVERY=...]   (needs graphics: no -nographics)
        /// </summary>
        public static void Shots()
        {
            int exit = 1;
            GameObject root = null, camGo = null, sunGo = null;
            var builder = new AirframeBuilder();
            RenderTexture rt = null;
            bool prevModels = SessionSettings.UseAirframeModels;
            try
            {
                string list = Environment.GetEnvironmentVariable("SURFACE_TYPES");
                string id = string.IsNullOrWhiteSpace(list) ? "pitts-s2b-like" : list.Split(',')[0].Trim();
                string dir = Environment.GetEnvironmentVariable("SURFACE_SHOTS");
                if (string.IsNullOrWhiteSpace(dir)) dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/surface-motion/shots"));
                string tag = Environment.GetEnvironmentVariable("SURFACE_TAG") ?? "shot";
                Directory.CreateDirectory(dir);
                SessionSettings.UseAirframeModels = true;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                sunGo = new GameObject("Sun"); var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f); sun.intensity = 1.2f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.60f, 0.70f);
                RenderSettings.fog = false;
                root = new GameObject(id);
                builder.Build(root.transform, UnityAircraftConfigLoader.LoadFromStreamingAssets(id));
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.forceMatrixRecalculationPerRender = true;
                builder.SetDeflections(0f, 0f, 0f, 0f);
                Snap n0 = Bake(root.transform);
                float zMin = float.MaxValue, zMax = float.MinValue;
                foreach (Vector3[] v in n0.V) if (v != null) foreach (Vector3 p in v) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); }
                Vector3 tail = new Vector3(0f, 0.15f, zMin + 0.45f), mid = new Vector3(0f, 0.2f, 0.5f * (zMin + zMax));

                camGo = new GameObject("Cam"); Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.74f, 0.94f);
                cam.allowHDR = false; cam.allowMSAA = true; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
                const int tw = 800, th = 500;
                rt = new RenderTexture(tw, th, 24) { antiAliasing = 8 }; rt.Create();
                cam.targetTexture = rt; cam.aspect = (float)tw / th;
                float d = Mathf.Deg2Rad;
                var poses = new (string name, float ail, float ele, float rud)[]
                    { ("neutral", 0f, 0f, 0f), ("full aft -27.0", 0f, -27.0f * d, 0f), ("half aft -12.1", 0f, -12.1f * d, 0f), ("full fwd +27.0", 0f, 27.0f * d, 0f) };
                var views = new (string name, Vector3 eye, Vector3 at, float fov)[]
                {
                    ("chase", mid + new Vector3(0f, 2.6f, -9.5f), mid, 40f),
                    ("tail34", tail + new Vector3(-1.9f, 1.1f, -2.2f), tail, 40f),
                    ("tailside", tail + new Vector3(-3.4f, 0.15f, 0.1f), tail, 30f),
                };
                void Tile(Texture2D sheet, int tx, int ty)
                {
                    cam.Render();
                    RenderTexture.active = rt;
                    var t = new Texture2D(tw, th, TextureFormat.RGB24, false);
                    t.ReadPixels(new Rect(0, 0, tw, th), 0, 0); t.Apply();
                    sheet.SetPixels(tx * tw, ty * th, tw, th, t.GetPixels());
                    Object.DestroyImmediate(t);
                }
                foreach (var v in views)
                {
                    cam.fieldOfView = v.fov;
                    cam.transform.position = root.transform.TransformPoint(v.eye);
                    cam.transform.rotation = Quaternion.LookRotation(root.transform.TransformPoint(v.at) - cam.transform.position, Vector3.up);
                    var sheet = new Texture2D(2 * tw, 2 * th, TextureFormat.RGB24, false);
                    for (int i = 0; i < poses.Length; i++)
                    {
                        builder.SetDeflections(poses[i].ail, poses[i].ele, poses[i].rud, 0f);
                        Tile(sheet, i % 2, 1 - i / 2);   // neutral top-left, full aft top-right, half aft bottom-left, full fwd bottom-right
                    }
                    sheet.Apply();
                    string path = Path.Combine(dir, $"{id}-{tag}-elevator-{v.name}.png");
                    File.WriteAllBytes(path, sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                    Debug.Log($"[SurfaceMotion] shot {path} (tiles: {string.Join(" | ", poses.Select(p => p.name))})");
                }
                {
                    cam.fieldOfView = 40f;
                    cam.transform.position = root.transform.TransformPoint(mid + new Vector3(0f, 1.4f, -9.5f));
                    cam.transform.rotation = Quaternion.LookRotation(root.transform.TransformPoint(mid) - cam.transform.position, Vector3.up);
                    var sheet = new Texture2D(2 * tw, th, TextureFormat.RGB24, false);
                    builder.SetDeflections(0f, 0f, 0f, 0f); Tile(sheet, 0, 0);
                    builder.SetDeflections(0.35f, 0f, 0.35f, 0f); Tile(sheet, 1, 0);
                    sheet.Apply();
                    string path = Path.Combine(dir, $"{id}-{tag}-roll.png");
                    File.WriteAllBytes(path, sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                    Debug.Log($"[SurfaceMotion] shot {path} (tiles: neutral | aileron +0.35 rad, rudder +0.35 rad)");
                }
                // The left slave strut between its ailerons' horns, neutral and full aileron each way (it must stay on both pins).
                Renderer strut = root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.EndsWith("Strut_Slave_L"));
                if (strut != null)
                {
                    Vector3 c = root.transform.InverseTransformPoint(strut.bounds.center);
                    cam.fieldOfView = 30f;
                    cam.transform.position = root.transform.TransformPoint(c + new Vector3(-0.9f, 0.25f, -1.6f));
                    cam.transform.rotation = Quaternion.LookRotation(root.transform.TransformPoint(c) - cam.transform.position, Vector3.up);
                    var sheet = new Texture2D(3 * tw, th, TextureFormat.RGB24, false);
                    builder.SetDeflections(-0.4f, 0f, 0f, 0f); Tile(sheet, 0, 0);
                    builder.SetDeflections(0f, 0f, 0f, 0f); Tile(sheet, 1, 0);
                    builder.SetDeflections(0.4f, 0f, 0f, 0f); Tile(sheet, 2, 0);
                    sheet.Apply();
                    string path = Path.Combine(dir, $"{id}-{tag}-strut.png");
                    File.WriteAllBytes(path, sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                    Debug.Log($"[SurfaceMotion] shot {path} (tiles: aileron -0.4 | neutral | +0.4 rad, left slave strut)");
                }
                exit = 0;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                RenderTexture.active = null;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                builder.Clear();
                if (root != null) Object.DestroyImmediate(root);
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (sunGo != null) Object.DestroyImmediate(sunGo);
                SessionSettings.UseAirframeModels = prevModels;
                EditorApplication.Exit(exit);
            }
        }

        private sealed class Snap { public readonly List<Vector3[]> V = new(); }

        private static Snap Bake(Transform root)
        {
            var s = new Snap();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) { s.V.Add(null); continue; }
                Mesh m = null; Matrix4x4 tm;
                if (r is SkinnedMeshRenderer skin)
                {
                    m = new Mesh(); skin.BakeMesh(m, true);
                    tm = root.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) m = mf.sharedMesh;
                    tm = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                }
                if (m == null || !m.isReadable) { s.V.Add(null); continue; }
                Vector3[] v = m.vertices;
                for (int i = 0; i < v.Length; i++) v[i] = tm.MultiplyPoint3x4(v[i]);
                s.V.Add(v);
                if (r is SkinnedMeshRenderer) Object.DestroyImmediate(m);
            }
            return s;
        }

        /// <summary>The moved vertices of one region between two snapshots: count, and the signed travel (along
        /// <paramref name="dir"/>) of the moved vertex farthest along <paramref name="aft"/> (the trailing edge).</summary>
        private static (int n, float te) Moved(Snap a, Snap b, Func<Vector3, bool> region, Vector3 dir)
        {
            int n = 0; float bestZ = float.MaxValue, te = 0f;
            for (int k = 0; k < a.V.Count && k < b.V.Count; k++)
            {
                Vector3[] p = a.V[k], q = b.V[k];
                if (p == null || q == null || p.Length != q.Length) continue;
                for (int i = 0; i < p.Length; i++)
                {
                    if (!region(p[i])) continue;
                    Vector3 d = q[i] - p[i];
                    if (d.magnitude < MovedM) continue;
                    n++;
                    if (p[i].z < bestZ) { bestZ = p[i].z; te = Vector3.Dot(d, dir); }   // aft-most = trailing edge (nose is +z)
                }
            }
            return (n, te);
        }

        private static float MaxDiff(Snap a, Snap b)
        {
            float m = 0f;
            for (int k = 0; k < a.V.Count && k < b.V.Count; k++)
            {
                Vector3[] p = a.V[k], q = b.V[k];
                if (p == null || q == null || p.Length != q.Length) continue;
                for (int i = 0; i < p.Length; i++) m = Mathf.Max(m, (q[i] - p[i]).magnitude);
            }
            return m;
        }

        private static bool Check(string id, List<string> lines)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionSettings.UseOwnModels = id == "glider-2-33-like";
            var root = new GameObject(id);
            var builder = new AirframeBuilder();
            try
            {
                builder.Build(root.transform, UnityAircraftConfigLoader.LoadFromStreamingAssets(id));
                builder.SetDeflections(0f, 0f, 0f, 0f);
                Snap n0 = Bake(root.transform);
                // Extent of what is drawn, root frame.
                float zMin = float.MaxValue, zMax = float.MinValue, xMax = 0f;
                foreach (Vector3[] v in n0.V) if (v != null) foreach (Vector3 p in v) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); xMax = Mathf.Max(xMax, Mathf.Abs(p.x)); }
                if (zMin > zMax) { lines.Add($"{id}: no readable drawn geometry"); return true; }
                float tailZ = zMin + 0.30f * (zMax - zMin);
                bool Tail(Vector3 p) => p.z < tailZ;
                bool TailL(Vector3 p) => Tail(p) && p.x < -0.12f;
                bool TailR(Vector3 p) => Tail(p) && p.x > 0.12f;
                bool Fin(Vector3 p) => Tail(p) && Mathf.Abs(p.x) < 0.25f && p.y > 0.05f;
                bool WingL(Vector3 p) => p.x < -0.4f * xMax && p.z > tailZ;
                bool WingR(Vector3 p) => p.x > 0.4f * xMax && p.z > tailZ;

                builder.SetDeflections(0f, Defl, 0f, 0f); Snap eUp = Bake(root.transform);
                builder.SetDeflections(0f, 0.5f * Defl, 0f, 0f); Snap eHalf = Bake(root.transform);
                builder.SetDeflections(0f, -Defl, 0f, 0f); Snap eDn = Bake(root.transform);
                builder.SetDeflections(Defl, 0f, 0f, 0f); Snap aP = Bake(root.transform);
                builder.SetDeflections(0f, 0f, Defl, 0f); Snap rP = Bake(root.transform);
                builder.SetDeflections(0f, 0f, 0f, 0f); Snap n1 = Bake(root.transform);

                var eL = Moved(n0, eUp, TailL, Vector3.up); var eR = Moved(n0, eUp, TailR, Vector3.up);
                var eLh = Moved(n0, eHalf, TailL, Vector3.up); var eRh = Moved(n0, eHalf, TailR, Vector3.up);
                var eLn = Moved(n0, eDn, TailL, Vector3.up); var eRn = Moved(n0, eDn, TailR, Vector3.up);
                var aL = Moved(n0, aP, WingL, Vector3.up); var aR = Moved(n0, aP, WingR, Vector3.up);
                var rd = Moved(n0, rP, Fin, Vector3.right);
                float back = MaxDiff(n0, n1);

                var fails = new List<string>();
                // ELEVATOR: both halves move, TE DOWN for +, the other way for -, half deflection about half the travel.
                if (eL.n == 0 || eR.n == 0) fails.Add($"elevator static ({(eL.n == 0 ? "L" : "")}{(eR.n == 0 ? "R" : "")})");
                else
                {
                    if (eL.te >= 0f || eR.te >= 0f) fails.Add("+elevator does not put both TEs down");
                    if (eLn.te <= 0f || eRn.te <= 0f) fails.Add("-elevator does not put both TEs up");
                    if (Mathf.Abs(eL.te - eR.te) > 0.25f * Mathf.Max(Mathf.Abs(eL.te), Mathf.Abs(eR.te))) fails.Add("elevator halves travel differently");
                    float rl = eLh.te / eL.te, rr = eRh.te / eR.te;
                    if (rl < 0.4f || rl > 0.6f || rr < 0.4f || rr > 0.6f) fails.Add($"elevator not proportional (half/full {rl:F2} / {rr:F2})");
                }
                // AILERON: + = roll right: left TE down, right TE up.
                if (aL.n == 0 || aR.n == 0) fails.Add($"aileron static ({(aL.n == 0 ? "L" : "")}{(aR.n == 0 ? "R" : "")})");
                else if (aL.te >= 0f || aR.te <= 0f) fails.Add("+aileron is not left TE down / right TE up");
                // RUDDER: + = TE to the right.
                if (rd.n == 0) fails.Add("rudder static");
                else if (rd.te <= 0f) fails.Add("+rudder does not put the TE right");
                if (back > 0.002f) fails.Add($"neutral does not return ({back * 1000f:F1} mm)");

                string model = AirframeModels.PlacedOwnName(id) ?? "(downloaded / procedural)";
                lines.Add($"{id} [{model}] {(fails.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", fails))}");
                lines.Add($"    elevator +{Defl:F2} rad: L {eL.n} vtx TE {eL.te * 100f:+0.0;-0.0} cm, R {eR.n} vtx TE {eR.te * 100f:+0.0;-0.0} cm" +
                          $" | half: L {eLh.te * 100f:+0.0;-0.0} R {eRh.te * 100f:+0.0;-0.0} | -{Defl:F2}: L {eLn.te * 100f:+0.0;-0.0} R {eRn.te * 100f:+0.0;-0.0}");
                lines.Add($"    aileron +{Defl:F2}: L {aL.n} vtx TE {aL.te * 100f:+0.0;-0.0} cm, R {aR.n} vtx TE {aR.te * 100f:+0.0;-0.0} cm | rudder +{Defl:F2}: {rd.n} vtx TE {rd.te * 100f:+0.0;-0.0} cm right | neutral back {back * 1000f:F2} mm");
                return fails.Count == 0;
            }
            finally
            {
                builder.Clear();
                Object.DestroyImmediate(root);
            }
        }
    }
}

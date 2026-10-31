using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Does the model's own rig actually reach the model? Builds the aeroplane exactly as the game does, binds
    /// the rig, and then MEASURES it, because none of this is visible in a still:
    ///   - every node in the table found in the GLB, and nothing in the GLB's rig left unbound;
    ///   - each node's origin where the table says its pivot is (which proves the glTF → Unity frame map);
    ///   - each node's parent the one the table names;
    ///   - and, at each control's extremes, which way the thing actually moved, reported in the aeroplane's own
    ///     frame (forward / up / starboard) so a reversed sign reads as plain English.
    ///
    ///   Unity -batchmode -quit -nographics -projectPath unity \
    ///     -executeMethod FlyingGame.EditorTools.SgsRigCheck.Run
    ///
    /// With RIG_SHOTS=&lt;dir&gt; (and no -nographics) it also captures the cockpit and a 3/4 exterior at a few
    /// control positions, for the things only an eye can judge: which number a needle is on, which way the yaw
    /// string lies.
    /// </summary>
    public static class SgsRigCheck
    {
        private const string Type = "glider-2-33-like";

        public static void Run()
        {
            int exit = 1;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.LiveryId = "factory";
                SessionSettings.UseAirframeModels = true;
                SessionSettings.UseOwnModels = true;

                var root = new GameObject(Type);
                var builder = new AirframeBuilder();
                builder.Build(root.transform, UnityAircraftConfigLoader.LoadFromStreamingAssets(Type));
                if (!builder.HasOwnRig) throw new InvalidOperationException("the 2-33's own rig did not bind — nothing to check.");

                Transform model = root.transform.Find("Model");
                if (model == null) throw new InvalidOperationException("the placed model is not under the airframe root.");
                ModelRig rig = AirframeRig.Load(AirframeModels.PlacedOwnName(Type));
                Debug.Log($"RIG {rig.Model}: {rig.Nodes.Count} nodes in the table, params {string.Join(", ", rig.Params())}");

                // --- every node bound, on its pivot, under its parent -------------------------------------
                var bound = new Dictionary<string, Transform>();
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.StartsWith("Model:") ? t.name.Substring(6) : t.name;
                    if (!bound.ContainsKey(n)) bound[n] = t;
                }
                int missing = 0, offPivot = 0, wrongParent = 0;
                double worstPivot = 0; string worstNode = "";
                foreach (RigNode n in rig.Nodes)
                {
                    if (!bound.TryGetValue(n.Node, out Transform t)) { Debug.LogError($"RIG '{n.Node}' is in the table and not in the model"); missing++; continue; }
                    // The node's origin in the MODEL's own space, against the pivot the table gives in glTF
                    // space (where x is negated on import). Metres: the instance carries a uniform scale, and
                    // InverseTransformPoint takes it off again.
                    Vector3 p = model.InverseTransformPoint(t.position);
                    var want = new Vector3((float)-n.Px, (float)n.Py, (float)n.Pz);
                    float off = (p - want).magnitude;
                    if (off > worstPivot) { worstPivot = off; worstNode = n.Node; }
                    if (off > 0.005f)
                    {
                        Debug.LogError($"RIG '{n.Node}' sits at {p} and the table's pivot says {want} ({off * 1000f:F0} mm out)");
                        offPivot++;
                    }
                    string parent = t.parent == null ? "" : t.parent.name.StartsWith("Model:") ? t.parent.name.Substring(6) : t.parent.name;
                    if (parent == "Model") parent = "";
                    if (parent != n.Parent) { Debug.LogError($"RIG '{n.Node}' hangs off '{parent}' and the table says '{n.Parent}'"); wrongParent++; }
                }
                Debug.Log($"RIG bound {rig.Nodes.Count - missing}/{rig.Nodes.Count}; {offPivot} off their pivot "
                          + $"(worst {worstNode} {worstPivot * 1000:F1} mm); {wrongParent} under the wrong parent");

                // A CHAIN's links turn across themselves, never about themselves: a link's hinge axis has to be
                // perpendicular to the offset from its parent's pivot to its own, or the part twists about its
                // own length and barely goes anywhere. (A single surface is the opposite case — an aileron's
                // hinge runs ALONG its span — which is why this only applies where one rig node hangs off
                // another: the sticks on the torque tube, the yaw string's four links.)
                int chainBad = 0;
                foreach (RigNode n in rig.Nodes)
                {
                    if (n.Move || rig.Find(n.Parent) == null) continue;
                    if (!bound.TryGetValue(n.Node, out Transform t)) continue;
                    Vector3 off = t.localPosition;
                    if (off.sqrMagnitude < 1e-8f) continue;
                    var a = n.TurnAxisUnity;
                    var axis = new Vector3((float)a.X, (float)a.Y, (float)a.Z).normalized;
                    float cos = Vector3.Dot(axis, off.normalized);
                    float outOfSquare = 90f - Vector3.Angle(axis, off.normalized);
                    if (Mathf.Abs(cos) > 0.2f)
                    {
                        chainBad++;
                        Debug.LogError($"RIG '{n.Node}' turns about an axis {Mathf.Abs(outOfSquare):F0} deg out of square with the "
                                       + $"{off.magnitude * 1000f:F0} mm link it hangs on — it will twist about itself instead of swinging");
                    }
                    else
                        Debug.Log($"RIG   chain link {n.Node,-14} axis {Mathf.Abs(outOfSquare):F1} deg out of square with its {off.magnitude * 1000f:F0} mm link");
                }

                // Anything in the GLB that marks itself as rigged and ISN'T in the table would silently never
                // move. The hinge markers name the nine flight surfaces, so check those at least.
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.StartsWith("Model:") ? t.name.Substring(6) : t.name;
                    if (!n.EndsWith("_HingeAxis")) continue;
                    string surface = n.Substring(0, n.Length - "_HingeAxis".Length);
                    if (rig.Find(surface) == null) Debug.LogError($"RIG the model marks '{surface}' as hinged and the table does not rig it");
                }

                // --- which way does each thing go? ---------------------------------------------------------
                // Reported in the aeroplane's own frame: the models are placed nose along Unity +z, so
                // forward = +z, up = +y, starboard = +x (AirframeModels).
                string Where(Vector3 d) => string.Format(CultureInfo.InvariantCulture,
                    "{0,6:F0} mm {1}, {2,6:F0} mm {3}, {4,6:F0} mm {5}",
                    Mathf.Abs(d.z) * 1000, d.z >= 0 ? "fwd " : "aft ",
                    Mathf.Abs(d.y) * 1000, d.y >= 0 ? "up  " : "down",
                    Mathf.Abs(d.x) * 1000, d.x >= 0 ? "stbd" : "port");

                var probes = new (string label, RigInputs inputs)[]
                {
                    ("stick RIGHT (aileron +1)", new RigInputs { Aileron = 1 }),
                    ("stick AFT (elevator +1)", new RigInputs { Elevator = 1 }),
                    ("RIGHT pedal (rudder +1)", new RigInputs { Rudder = 1 }),
                    ("air brake OUT (spoiler 1)", new RigInputs { Spoiler = 1 }),
                    ("trim AFT (trim +1)", new RigInputs { Trim = 1 }),
                    ("release PULLED (tow 1)", new RigInputs { TowRelease = 1 }),
                    ("60 mph, 2500 ft, +4 kt, hdg 090, beta +10",
                        new RigInputs { AirspeedMph = 60, AltitudeFt = 2500, VarioKt = 4, HeadingDeg = 90, SideslipDeg = 10 }),
                };
                // Follow the PART, not just its origin: the point of its own geometry farthest from the
                // pivot — a surface's trailing edge, a stick's grip, a needle's tip. Held in the node's own
                // local coordinates, which never change, so the same material point is measured every time.
                var probe = new Dictionary<string, Vector3>();
                var home = new Dictionary<string, Vector3>();
                var neutral = new RigInputs();
                builder.SetRig(neutral, 0f);
                foreach (RigNode n in rig.Nodes)
                {
                    if (!bound.TryGetValue(n.Node, out Transform t)) continue;
                    var ax = n.Move ? n.SlideAxisUnity : n.TurnAxisUnity;
                    Vector3 local = t.InverseTransformPoint(FarthestVertex(t, new Vector3((float)ax.X, (float)ax.Y, (float)ax.Z), n.Move));
                    probe[n.Node] = local;
                    home[n.Node] = root.transform.InverseTransformPoint(t.TransformPoint(local));
                }

                foreach ((string label, RigInputs inputs) in probes)
                {
                    builder.SetRig(inputs, 0f);
                    Debug.Log($"RIG --- {label} ---");
                    foreach (RigNode n in rig.Nodes)
                    {
                        if (!inputs.TryGet(n.Param, out double param)) continue;
                        if (Math.Abs(n.Value(param) - n.Value(n.ModelAt)) < 1e-6) continue;
                        if (!bound.TryGetValue(n.Node, out Transform t)) continue;
                        Vector3 now = root.transform.InverseTransformPoint(t.TransformPoint(probe[n.Node]));
                        Debug.Log($"RIG   {n.Node,-18} {n.Value(param),7:F2} {(n.Move ? "m  " : "deg")}   far point moved {Where(now - home[n.Node])}");
                    }
                }
                builder.SetRig(neutral, 0f);

                if (Environment.GetEnvironmentVariable("RIG_SHOTS") is string shots && !string.IsNullOrWhiteSpace(shots))
                    Shots(root.transform, model, builder, shots);

                exit = missing == 0 && offPivot == 0 && wrongParent == 0 && chainBad == 0 ? 0 : 1;
                Debug.Log(exit == 0 ? "RIG OK" : "RIG PROBLEMS — see the errors above");
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(exit);
        }

        /// <summary>The world point of this node's own geometry that MOVES most: for a hinge, the vertex
        /// farthest from the hinge LINE (an aileron's outboard trailing edge lies on the line and hardly moves
        /// at all, so measuring the farthest point from the pivot reports 3 mm for a 38-degree deflection); for
        /// a slide, simply the farthest point, since the whole part travels together.</summary>
        private static Vector3 FarthestVertex(Transform t, Vector3 axisLocal, bool move)
        {
            Vector3 axis = axisLocal.sqrMagnitude > 1e-9f ? t.TransformDirection(axisLocal).normalized : Vector3.up;
            Vector3 best = t.position; float far = -1f;
            foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                foreach (Vector3 v in m.vertices)
                {
                    Vector3 w = mf.transform.TransformPoint(v), r = w - t.position;
                    float d = move ? r.sqrMagnitude : (r - axis * Vector3.Dot(r, axis)).sqrMagnitude;
                    if (d > far) { far = d; best = w; }
                }
            }
            return best;
        }

        // ---- optional shots ---------------------------------------------------------------------------------

        private static void Shots(Transform root, Transform model, AirframeBuilder builder, string dir)
        {
            Directory.CreateDirectory(dir);
            var sunGo = new GameObject("Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.intensity = 1.2f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.60f, 0.70f);
            RenderSettings.fog = false;

            var camGo = new GameObject("Cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.52f, 0.68f, 0.86f);
            var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };

            // The front seat's eye, in the MODEL's own space (x nose, y up, z port) — just behind the panel,
            // which the table puts at x 1.744.
            Vector3 eye = model.TransformPoint(new Vector3(0.95f, 0.06f, 0f));
            Vector3 look = model.TransformPoint(new Vector3(2.4f, -0.05f, 0f));

            var states = new (string name, RigInputs r)[]
            {
                ("neutral", new RigInputs { AirspeedMph = 0, AltitudeFt = 0, HeadingDeg = 0 }),
                ("cruise-45mph-2500ft-hdg090", new RigInputs { AirspeedMph = 45, AltitudeFt = 2500, VarioKt = 0, HeadingDeg = 90 }),
                ("thermal-40mph-4700ft-up4kt-hdg270-slip-right10",
                    new RigInputs { Aileron = -0.4, Elevator = 0.3, Rudder = -0.3, AirspeedMph = 40, AltitudeFt = 4700, VarioKt = 4, HeadingDeg = 270, SideslipDeg = 10 }),
                ("full-controls-brakes-out-release-pulled",
                    new RigInputs { Aileron = 1, Elevator = 1, Rudder = 1, Spoiler = 1, Trim = 1, TowRelease = 1, AirspeedMph = 55, AltitudeFt = 800, VarioKt = -6, HeadingDeg = 180, SideslipDeg = -12 }),
            };
            foreach ((string name, RigInputs r) in states)
            {
                builder.SetRig(r, 0f);
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation(look - eye, model.TransformDirection(Vector3.up));
                cam.fieldOfView = 65f;
                Capture(cam, rt, Path.Combine(dir, $"cockpit-{name}.png"));

                Bounds b = Visible(root);
                float far = Mathf.Max(b.size.magnitude, 1f);
                cam.transform.position = b.center + new Vector3(0.55f, 0.35f, -0.75f).normalized * far * 0.62f;
                cam.transform.rotation = Quaternion.LookRotation(b.center - cam.transform.position, Vector3.up);
                cam.fieldOfView = 35f;
                Capture(cam, rt, Path.Combine(dir, $"exterior-{name}.png"));
            }
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(sunGo);
        }

        private static Bounds Visible(Transform root)
        {
            bool any = false; Bounds b = default;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private static void Capture(Camera cam, RenderTexture rt, string path)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            RenderTexture.active = null;
            Debug.Log($"RIG shot {path} ({new FileInfo(path).Length / 1024} KB)");
        }
    }
}

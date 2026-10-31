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
    ///   - each node's parent the one the table names, and every child listed after its parent;
    ///   - the camera's eye points, the two avionics displays and the sequence card all present;
    ///   - and, at each control's extremes, which way the thing actually moved, reported in the aeroplane's own
    ///     frame (forward / up / starboard) so a reversed sign reads as plain English.
    ///
    ///   Unity -batchmode -quit -nographics -projectPath unity \
    ///     -executeMethod FlyingGame.EditorTools.OwnRigCheck.Run          # the Extra
    ///   RIG_TYPE=glider-2-33-like … same thing for another type
    ///
    /// With RIG_SHOTS=&lt;dir&gt; (and no -nographics) it also captures each seat and a 3/4 exterior at a few
    /// control positions, for the things only an eye can judge: which number a needle is on, whether the canopy
    /// swings the right way.
    ///
    /// GENERIC on purpose. The 2-33 had a check of its own and it found a real fault (a yaw-string axis 21-29°
    /// out), so the Pitts and the Great Lakes should not have to wait for someone to write a third one.
    /// </summary>
    public static class OwnRigCheck
    {
        public static void Run()
        {
            int exit = 1;
            try
            {
                string type = Environment.GetEnvironmentVariable("RIG_TYPE");
                if (string.IsNullOrWhiteSpace(type)) type = "extra-300-like";

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = true;
                SessionSettings.UseOwnModels = true;

                var root = new GameObject(type);
                var builder = new AirframeBuilder();
                builder.Build(root.transform, UnityAircraftConfigLoader.LoadFromStreamingAssets(type));
                if (!builder.HasOwnRig) throw new InvalidOperationException($"{type}: its own rig did not bind — nothing to check.");

                Transform model = builder.ModelRoot;
                if (model == null) throw new InvalidOperationException("the placed model is not under the airframe root.");
                ModelRig rig = AirframeRig.Load(AirframeModels.PlacedOwnName(type));
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
                    if (parent == model.name || parent == "Model") parent = "";
                    if (parent != n.Parent) { Debug.LogError($"RIG '{n.Node}' hangs off '{parent}' and the table says '{n.Parent}'"); wrongParent++; }
                }
                Debug.Log($"RIG bound {rig.Nodes.Count - missing}/{rig.Nodes.Count}; {offPivot} off their pivot "
                          + $"(worst {worstNode} {worstPivot * 1000:F1} mm); {wrongParent} under the wrong parent");

                // A CHAIN's links turn across themselves, never about themselves: a link's hinge axis has to be
                // perpendicular to the offset from its parent's pivot to its own, or the part twists about its
                // own length and barely goes anywhere. (A single surface is the opposite case — an aileron's
                // hinge runs ALONG its span — which is why this only applies where one rig node hangs off
                // another: a stick on its roll gimbal, a toe brake on its pedal.)
                int chainBad = 0;
                foreach (RigNode n in rig.Nodes)
                {
                    RigNode parentNode = rig.Find(n.Parent);
                    if (n.Move || parentNode == null) continue;
                    // ...and only a TRUE chain, meaning parent and child move on the SAME param: the yaw
                    // string's four links, a torque tube's arms. A part that merely RIDES on a moving parent
                    // and has a hinge of its own — a toe brake on its pedal, a lock lever on the canopy — is
                    // free to sit anywhere, and holding it to this rule reports a fault that is not there (the
                    // Extra's two canopy locks, 2026-10-30).
                    bool sameParam = parentNode.Param == n.Param;
                    if (!bound.TryGetValue(n.Node, out Transform t)) continue;
                    Vector3 off = t.localPosition;
                    if (off.sqrMagnitude < 1e-8f) continue;
                    var a = n.TurnAxisUnity;
                    var axis = new Vector3((float)a.X, (float)a.Y, (float)a.Z).normalized;
                    float cos = Vector3.Dot(axis, off.normalized);
                    float outOfSquare = 90f - Vector3.Angle(axis, off.normalized);
                    if (Mathf.Abs(cos) > 0.2f && sameParam)
                    {
                        chainBad++;
                        Debug.LogError($"RIG '{n.Node}' turns about an axis {Mathf.Abs(outOfSquare):F0} deg out of square with the "
                                       + $"{off.magnitude * 1000f:F0} mm link it hangs on — it will twist about itself instead of swinging");
                    }
                    else
                        Debug.Log($"RIG   {(sameParam ? "chain link" : "rides on  ")} {n.Node,-14} axis {Mathf.Abs(outOfSquare):F1} deg "
                                  + $"out of square with its {off.magnitude * 1000f:F0} mm link");
                }

                // Anything in the GLB that marks itself as rigged and ISN'T in the table would silently never move.
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.StartsWith("Model:") ? t.name.Substring(6) : t.name;
                    if (!n.EndsWith("_HingeAxis")) continue;
                    string surface = n.Substring(0, n.Length - "_HingeAxis".Length);
                    if (rig.Find(surface) == null) Debug.LogError($"RIG the model marks '{surface}' as hinged and the table does not rig it");
                }

                // --- the nodes the GAME wants that are not rig nodes -------------------------------------
                // The eye points (the camera), the two avionics displays and the sequence card. These carry no
                // param, so the rig cannot report them missing; a renamed one would simply stop working.
                int fixtures = 0;
                void Want(string node, string what, bool required)
                {
                    bool have = bound.ContainsKey(node);
                    if (have) Debug.Log($"RIG   {what,-22} {node}");
                    else if (required) { Debug.LogError($"RIG the model has no '{node}' — {what} will not work"); fixtures++; }
                    else Debug.Log($"RIG   {what,-22} (none on this model)");
                }
                Want("Eye_Rear", "cockpit eye, default", true);
                Want("Eye_Front", "cockpit eye, other seat", false);
                Want("Radio_Display_R", "COM display", false);
                Want("Xpdr_Display_R", "transponder display", false);
                Want(SequenceCard.FaceNode, "sequence card", false);

                // The v15 rebuild renamed the old `Cockpit` MESH to Interior/Cockpit_Shell and left `Cockpit` as
                // a group. Anything still binding the old name would be posing an empty.
                if (bound.TryGetValue("Cockpit", out Transform cockpit) && cockpit.GetComponent<MeshFilter>() != null)
                    Debug.LogError("RIG 'Cockpit' still carries a mesh — the v15 model expects it to be a group with Cockpit_Shell under Interior");

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
                    ("stick RIGHT (aileron +1)", With((ref RigInputs r) => r.Aileron = 1)),
                    ("stick AFT (elevator +1)", With((ref RigInputs r) => r.Elevator = 1)),
                    ("RIGHT pedal (rudder +1)", With((ref RigInputs r) => r.Rudder = 1)),
                    ("LEFT toe brake", With((ref RigInputs r) => r.BrakeLeft = 1)),
                    ("throttle OPEN", With((ref RigInputs r) => r.Throttle = 1)),
                    ("prop + mixture PULLED OUT", With((ref RigInputs r) => { r.PropLever = 0; r.Mixture = 0; })),
                    ("trim AFT (nose up)", With((ref RigInputs r) => r.Trim = 1)),
                    ("fuel selector to 2, key to START, master OFF",
                        With((ref RigInputs r) => { r.FuelSelector = 2; r.Magneto = 4; r.Master = 0; })),
                    ("canopy UNLOCKED and OPEN", With((ref RigInputs r) => { r.CanopyLock = 0; r.CanopyOpen = 1; })),
                    ("release handle PULLED", With((ref RigInputs r) => r.CanopyJettison = 1)),
                    ("150 kt, 3500 ft, +1200 fpm, 2400 rpm, 24 inHg, hdg 090, +4 g, ball right",
                        With((ref RigInputs r) =>
                        {
                            r.AirspeedKias = 150; r.AltitudeFt = 3500; r.VsiFpm = 1200; r.Rpm = 2400;
                            r.ManifoldInHg = 24; r.OilPressPsi = 75; r.OilTempF = 190; r.ChtF = 380;
                            r.FuelCentreFrac = 0.6; r.HeadingDeg = 90; r.TurnRateDps = 3; r.SlipBall = 0.5;
                            r.GLoad = 4; r.GMaxSeen = 6; r.GMinSeen = -3;
                        })),
                };

                // Follow the PART, not just its origin: the point of its own geometry farthest from the
                // pivot — a surface's trailing edge, a stick's grip, a needle's tip. Held in the node's own
                // local coordinates, which never change, so the same material point is measured every time.
                var probe = new Dictionary<string, Vector3>();
                var home = new Dictionary<string, Vector3>();
                RigInputs neutral = RigInputs.Parked();
                builder.SetRig(neutral, 0f);
                foreach (RigNode n in rig.Nodes)
                {
                    if (!bound.TryGetValue(n.Node, out Transform t)) continue;
                    var ax = n.Move ? n.SlideAxisUnity : n.TurnAxisUnity;
                    Vector3 local = t.InverseTransformPoint(FarthestVertex(t, new Vector3((float)ax.X, (float)ax.Y, (float)ax.Z), n.Move));
                    probe[n.Node] = local;
                    home[n.Node] = root.transform.InverseTransformPoint(t.TransformPoint(local));
                    // WHERE the measured point sits on the part, in the MODEL's own frame (x nose, y up,
                    // z port) relative to the pivot. Without this the displacement below is ambiguous: a
                    // rotation of +30 about an axis moves a point exactly as -30 moves its mirror, so
                    // "160 mm up" says nothing until you know whether the point is fore or aft of the hinge.
                    Vector3 rel = model.InverseTransformPoint(t.TransformPoint(local)) - model.InverseTransformPoint(t.position);
                    Debug.Log($"RIG   probe {n.Node,-18} {rel.magnitude * 1000,6:F0} mm from the pivot: "
                              + $"{Mathf.Abs(rel.x) * 1000,5:F0} {(rel.x >= 0 ? "nose" : "aft ")}, "
                              + $"{Mathf.Abs(rel.y) * 1000,5:F0} {(rel.y >= 0 ? "up  " : "down")}, "
                              + $"{Mathf.Abs(rel.z) * 1000,5:F0} {(rel.z >= 0 ? "port" : "stbd")}");
                }

                foreach ((string label, RigInputs inputs) in probes)
                {
                    builder.SetRig(inputs, 0f);
                    Debug.Log($"RIG --- {label} ---");
                    foreach (RigNode n in rig.Nodes)
                    {
                        if (!inputs.TryGet(n.Param, out double param)) continue;
                        if (!neutral.TryGet(n.Param, out double was)) was = n.ModelAt;
                        if (Math.Abs(n.Value(param) - n.Value(was)) < 1e-6) continue;
                        if (!bound.TryGetValue(n.Node, out Transform t)) continue;
                        Vector3 now = root.transform.InverseTransformPoint(t.TransformPoint(probe[n.Node]));
                        Debug.Log($"RIG   {n.Node,-18} {n.Value(param),7:F2} {(n.Move ? "m  " : "deg")}   far point moved {Where(now - home[n.Node])}");
                    }
                }
                builder.SetRig(neutral, 0f);

                if (Environment.GetEnvironmentVariable("RIG_SHOTS") is string shots && !string.IsNullOrWhiteSpace(shots))
                    Shots(root.transform, model, builder, bound, shots);

                exit = missing == 0 && offPivot == 0 && wrongParent == 0 && chainBad == 0 && fixtures == 0 ? 0 : 1;
                Debug.Log(exit == 0 ? "RIG OK" : "RIG PROBLEMS — see the errors above");
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(exit);
        }

        /// <summary>A parked cockpit with one thing moved, so every probe starts from the same switch positions
        /// and only the thing under test differs.
        ///
        /// The delegate takes its RigInputs BY REFERENCE, and that is not a style choice: RigInputs is a struct,
        /// so an Action&lt;RigInputs&gt; is handed a copy and every "r.Aileron = 1" is thrown away. The first run
        /// of this check measured the parked cockpit against itself and printed eleven empty probe blocks.</summary>
        private delegate void Set(ref RigInputs r);

        private static RigInputs With(Set set)
        {
            RigInputs r = RigInputs.Parked();
            set(ref r);
            return r;
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

        private static void Shots(Transform root, Transform model, AirframeBuilder builder,
                                  Dictionary<string, Transform> bound, string dir)
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
            cam.nearClipPlane = 0.03f;
            var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };

            var displays = new CockpitDisplays();
            displays.Bind(n => bound.TryGetValue(n, out Transform t) ? t : null);

            var states = new (string name, RigInputs r)[]
            {
                ("parked", RigInputs.Parked()),
                ("cruise", With((ref RigInputs r) =>
                {
                    r.AirspeedKias = 150; r.AltitudeFt = 3500; r.VsiFpm = 0; r.Rpm = 2400; r.ManifoldInHg = 24;
                    r.OilPressPsi = 75; r.OilTempF = 190; r.ChtF = 380; r.FuelCentreFrac = 0.6;
                    r.HeadingDeg = 90; r.GLoad = 1; r.GMaxSeen = 1; r.ComFreqMhz = 122.8; r.ComStandbyMhz = 118.325;
                    r.XpdrMode = 3; r.XpdrSquawk = 4321;
                })),
                ("pulling-4g", With((ref RigInputs r) =>
                {
                    r.Elevator = 0.8; r.Aileron = 0.3; r.AirspeedKias = 180; r.AltitudeFt = 2800; r.VsiFpm = 2500;
                    r.Rpm = 2650; r.ManifoldInHg = 28; r.GLoad = 4.2; r.GMaxSeen = 6.1; r.GMinSeen = -3.4;
                    r.TurnRateDps = 6; r.SlipBall = 0.3; r.HeadingDeg = 215; r.OilTempF = 205; r.ChtF = 420;
                    r.XpdrMode = 3; r.XpdrSquawk = 1200;
                })),
                ("full-controls-canopy-open", With((ref RigInputs r) =>
                {
                    r.Aileron = 1; r.Elevator = 1; r.Rudder = 1; r.Trim = 1; r.Throttle = 1;
                    r.BrakeLeft = 1; r.BrakeRight = 1; r.CanopyLock = 0; r.CanopyOpen = 1; r.CanopyJettison = 1;
                    r.AirspeedKias = 60; r.AltitudeFt = 0; r.Rpm = 800; r.HeadingDeg = 0;
                })),
            };
            foreach ((string name, RigInputs r) in states)
            {
                builder.SetRig(r, 0f);
                displays.Update(r);
                foreach (string seat in new[] { "Eye_Rear", "Eye_Front" })
                {
                    if (!bound.TryGetValue(seat, out Transform eye) || eye == null) continue;
                    cam.transform.position = eye.position;
                    cam.transform.rotation = root.rotation;
                    cam.fieldOfView = 72f;
                    Capture(cam, rt, Path.Combine(dir, $"{seat.ToLowerInvariant()}-{name}.png"));
                }

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

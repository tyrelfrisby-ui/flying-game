using System.Collections.Generic;
using System.IO;
using FlyingGame.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A model's OWN moving parts, driven from the sim (the SGS 2-33's 30 nodes: both sticks, four pedals, the
    /// torque tube, both air-brake handles, the trim lever, both tow-release knobs, four dive-brake doors, the
    /// ailerons, elevator halves and rudder, the ASI / altimeter / vario needles, the compass card and the
    /// four-link yaw string). The table is the model's own: docs/models/&lt;model&gt;-rig.json, written by the
    /// Blender build script and mirrored into StreamingAssets/rigs by ConfigSync, so a rebuilt model brings its
    /// new rig with it and nothing here has to be re-typed.
    ///
    /// Where this replaces <see cref="AirframeBuilder"/>'s carve: a model with a rig already HAS separate,
    /// correctly pivoted surfaces, so cutting hinge regions out of its meshes would both tear the geometry and
    /// deflect it twice.
    ///
    /// FRAMES. The rig's axes are directions in the MODEL's own glTF frame, and the importer's conversion to
    /// Unity's is on <see cref="RigNode.TurnAxisUnity"/> / <see cref="RigNode.SlideAxisUnity"/> — turning and
    /// sliding do NOT convert the same way, because the conversion is a reflection. The editor check
    /// SgsRigCheck measures both against the model, since neither shows in a still.
    /// </summary>
    public sealed class AirframeRig
    {
        /// <summary>One node of the rig, bound to the transform the importer made for it.</summary>
        public readonly struct Bound
        {
            public readonly RigNode Node;
            public readonly Transform T;
            /// <summary>The rig's axis in the imported model's frame (rotation axis, or slide direction).</summary>
            public readonly Vector3 Axis;
            public readonly Vector3 RestPos;
            public readonly Quaternion RestRot;
            /// <summary>Degrees of flutter on top of the deflection (the yaw string only; 0 for everything else).</summary>
            public readonly float Flutter;
            public readonly float Phase;
            public Bound(RigNode node, Transform t, Vector3 axis, float flutter, float phase)
            {
                Node = node; T = t; Axis = axis;
                RestPos = t.localPosition; RestRot = t.localRotation;
                Flutter = flutter; Phase = phase;
            }
        }

        private readonly List<Bound> _bound = new();
        /// <summary>Nodes the rig names that the model didn't bring (an old GLB against a new rig, or the other
        /// way round) — reported once, at bind.</summary>
        private readonly List<string> _missing = new();

        private float _t;
        private readonly HashSet<string> _warned = new();

        // The two frame conversions live on RigNode (engine-free, so they are tested): see the class note.
        private static Vector3 Axis(RigNode n)
        {
            var a = n.Move ? n.SlideAxisUnity : n.TurnAxisUnity;
            return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
        }

        /// <summary>Bind a placed model to its rig, or null where the model has none. Call it on the instance
        /// BEFORE anything moves its nodes: each node's imported pose is its rest pose.</summary>
        public static AirframeRig Bind(GameObject inst, string modelId)
        {
            ModelRig rig = Load(modelId);
            if (rig == null || rig.Nodes.Count == 0) return null;

            // Only inside the placed model. A name scan over the scene finds the procedural shell's own
            // "Rudder" and puts the rudder on the config's hinge instead of the model's (it threw the Extra's
            // rudder axis 48 degrees off vertical before this rule was learnt).
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
            {
                // TryPlaceModel prefixes every MESH object with "Model:" once it has taken the model over, and
                // glTFast gives the node's own GameObject the node's name (a second primitive becomes a CHILD
                // named <mesh>_1, which rides along). A trailing _<digits> is NOT stripped: the yaw string's
                // links are themselves YawString_1..YawString_4.
                string n = t.name.StartsWith("Model:") ? t.name.Substring(6) : t.name;
                if (!byName.ContainsKey(n)) byName[n] = t;
            }

            var bound = new AirframeRig();
            int link = 0;
            foreach (RigNode n in rig.Nodes)
            {
                if (!byName.TryGetValue(n.Node, out Transform t) || t == null) { bound._missing.Add(n.Node); continue; }
                // A yaw string is a length of wool: it lives in the airflow and never hangs dead still. The
                // flutter is per link and out of phase, so the chain ripples instead of swinging as one bar.
                bool wool = n.Param == "sideslip_deg";
                bound._bound.Add(new Bound(n, t, Axis(n), wool ? 1.1f : 0f, wool ? 0.37f * link++ : 0f));
            }
            if (bound._bound.Count == 0) return null;
            Debug.Log($"[Airframe] {modelId}: the model's own rig drives {bound._bound.Count} of {rig.Nodes.Count} nodes"
                      + (bound._missing.Count > 0 ? $"; MISSING from the model: {string.Join(", ", bound._missing)}" : "")
                      + $" (params {string.Join(", ", rig.Params())})");
            return bound;
        }

        /// <summary>Every node that bound, for an editor check to measure against the model.</summary>
        public IReadOnlyList<Bound> Nodes => _bound;
        /// <summary>Nodes the rig named that the model did not bring.</summary>
        public IReadOnlyList<string> Missing => _missing;

        /// <summary>The transform the model gave this rig node, or null where it has none.</summary>
        public Transform Find(string node)
        {
            foreach (Bound b in _bound) if (b.Node.Node == node) return b.T;
            return null;
        }

        /// <summary>The node the rig says comes OFF, and the param that throws it (the Extra's canopy) — or a
        /// null transform where this model has nothing to lose.</summary>
        public (Transform T, string Param) Jettisonable()
        {
            foreach (Bound b in _bound) if (b.Node.JettisonParam.Length > 0) return (b.T, b.Node.JettisonParam);
            return (null, "");
        }

        /// <summary>Stop driving one node, because it is no longer part of the aeroplane (it was jettisoned, and
        /// something else owns its transform now). Anything parented under it goes with it.</summary>
        public void Release(Transform t)
        {
            if (t == null) return;
            for (int i = _bound.Count - 1; i >= 0; i--)
                if (_bound[i].T == t || (_bound[i].T != null && _bound[i].T.IsChildOf(t))) _bound.RemoveAt(i);
        }

        private RigInputs _state;

        /// <summary>Move every node to the sim's current state. <paramref name="dt"/> only drives the yaw string's flutter.</summary>
        public void Apply(in RigInputs inputs, float dt)
        {
            _state = inputs;
            ApplyState(dt);
        }

        /// <summary>Move the SURFACES and handles only, leaving the instruments reading what they read. This is
        /// the path every drawn aeroplane takes — a remote player's, the widget's — where there is a stick
        /// position but no panel anyone can see.</summary>
        public void SetControls(in RigInputs controls)
        {
            _state.TakeControlsFrom(controls);
            ApplyState(0f);
        }

        private void ApplyState(float dt)
        {
            RigInputs inputs = _state;
            _t += dt;
            foreach (Bound b in _bound)
            {
                if (b.T == null) continue;
                if (!inputs.TryGet(b.Node.Param, out double param))
                {
                    if (_warned.Add(b.Node.Param))
                        Debug.LogWarning($"[Airframe] the rig asks for '{b.Node.Param}' ({b.Node.Node}) and the sim has no such value — that node will not move.");
                    continue;
                }
                double v = b.Node.Value(param);
                if (b.Node.Move)
                {
                    // Metres in the MODEL's units. The instance carries one uniform scale (the model is fitted to
                    // the config's span), so a 50 mm pull is drawn 50 mm at the scale the rest of the model is drawn.
                    b.T.localPosition = b.RestPos + b.Axis * (float)v;
                }
                else
                {
                    float deg = (float)v;
                    if (b.Flutter > 0f)
                    {
                        // Faster air, livelier wool: nothing below a walking pace, full life by about 60 mph.
                        float life = Mathf.Clamp01(((float)inputs.AirspeedMph - 15f) / 45f);
                        deg += b.Flutter * life * (Mathf.Sin((3.1f * _t + b.Phase) * 2f * Mathf.PI)
                                                 + 0.5f * Mathf.Sin((7.3f * _t + 2f * b.Phase) * 2f * Mathf.PI));
                    }
                    b.T.localRotation = b.RestRot * Quaternion.AngleAxis(deg, b.Axis);
                }
            }
        }

        // ---- the table ---------------------------------------------------------------------------------------

        private static readonly Dictionary<string, ModelRig> Cache = new();

        /// <summary>The rig for this model, or null where there is no file. Parsed once.</summary>
        public static ModelRig Load(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return null;
            if (Cache.TryGetValue(modelId, out ModelRig cached)) return cached;
            ModelRig rig = null;
            string path = Path.Combine(Application.streamingAssetsPath, "rigs", modelId + ".json");
            try
            {
                if (File.Exists(path)) rig = Parse(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Airframe] rig {path} could not be read: {e.Message}");
            }
            Cache[modelId] = rig;
            return rig;
        }

        /// <summary>Forget the parsed rigs (ConfigSync re-mirrors them in the editor).</summary>
        public static void Forget() => Cache.Clear();

        /// <summary>docs/models/&lt;model&gt;-rig.json as the build script writes it.</summary>
        public static ModelRig Parse(string json)
        {
            JObject root = JObject.Parse(json);
            var nodes = new List<RigNode>();
            var rows = root["rig"] as JArray;
            if (rows != null)
            {
                foreach (JToken row in rows)
                {
                    var map = row["map"] as JArray;
                    if (map == null || map.Count == 0) continue;
                    var mp = new double[map.Count];
                    var mv = new double[map.Count];
                    for (int i = 0; i < map.Count; i++)
                    {
                        mp[i] = map[i][0].Value<double>();
                        mv[i] = map[i][1].Value<double>();
                    }
                    JToken axis = row["axis_gltf"], pivot = row["pivot_gltf"], extra = row["extra"];
                    double[] detents = null;
                    if (extra?["rig_detents"] is JArray det)
                    {
                        detents = new double[det.Count];
                        for (int i = 0; i < det.Count; i++) detents[i] = det[i].Value<double>();
                    }
                    nodes.Add(new RigNode(
                        node: row["node"]?.Value<string>() ?? "",
                        parent: row["parent"]?.Type == JTokenType.Null ? "" : row["parent"]?.Value<string>() ?? "",
                        param: row["param"]?.Value<string>() ?? "",
                        move: (row["kind"]?.Value<string>() ?? "rot") == "move",
                        ax: axis?[0]?.Value<double>() ?? 0, ay: axis?[1]?.Value<double>() ?? 0, az: axis?[2]?.Value<double>() ?? 0,
                        px: pivot?[0]?.Value<double>() ?? 0, py: pivot?[1]?.Value<double>() ?? 0, pz: pivot?[2]?.Value<double>() ?? 0,
                        mapParam: mp, mapValue: mv,
                        wrapPeriod: extra?["rig_map_wrap"]?.Value<double>() ?? 0.0,
                        extrapolate: (extra?["rig_map_extrapolate"]?.Value<int>() ?? 0) != 0,
                        detents: detents,
                        modelAt: row["model"]?.Value<double>() ?? 0.0,
                        desc: row["desc"]?.Value<string>() ?? "",
                        jettisonParam: extra?["rig_jettison_param"]?.Value<string>() ?? ""));
                }
            }
            return new ModelRig(root["model"]?.Value<string>() ?? "", nodes);
        }
    }
}

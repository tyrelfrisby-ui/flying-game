using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A RIG LEVER OR KNOB AS A HAND SEES IT: the bound node, its rest pose, the point a hand takes it by (in the node's
    /// own frame), and the param and rig value at each end of its travel. Where the knob is at any rig value comes from
    /// the rig's OWN transform — <c>RestRot · AngleAxis(v, axis)</c> for a lever, <c>RestPos + axis · v</c> for a slide,
    /// exactly as <see cref="AirframeRig"/> draws it — so nobody reasons out a glTF or a Unity sign here.
    ///
    /// Shared by the headset's hands (VrCockpitHands.Hands.cs) and the bench check that measures them
    /// (EditorTools.VrHandleCheck), so the check measures the code that ships.
    /// </summary>
    public sealed class RigHandle
    {
        public Transform T;
        public Vector3 RestPos, Axis, KnobLocal;
        public Quaternion RestRot;
        public bool Slide;
        /// <summary>The param and the rig's value (degrees, or metres for a slide) at the first and last map point.</summary>
        public double P0, P1, V0, V1;
        public string Name;
        public RigNode Node;

        public double TFor(double param) => System.Math.Abs(P1 - P0) < 1e-9 ? 0.0 : System.Math.Clamp((param - P0) / (P1 - P0), 0.0, 1.0);
        public double ParamFor(double t) => P0 + (P1 - P0) * System.Math.Clamp(t, 0.0, 1.0);
        /// <summary>The rig value for a param, linear between the ends and NOT snapped to a detent (the hand's lever).</summary>
        public double ValueFor(double param) => V0 + (V1 - V0) * TFor(param);

        /// <summary>The pivot (a lever's hinge point, a slide's home origin), world.</summary>
        public Vector3 PivotWorld => T.parent.TransformPoint(RestPos);

        /// <summary>A lever's hinge direction, world (<c>RestRot · axis</c> in the parent: the axis the rig turns about).</summary>
        public Vector3 HingeWorld => T.parent.TransformDirection(RestRot * Axis);

        /// <summary>Put the node where the rig would draw it at rig value <paramref name="v"/> (degrees or metres).</summary>
        public void Pose(double v)
        {
            if (Slide) T.localPosition = RestPos + Axis * (float)v;
            else T.localRotation = RestRot * Quaternion.AngleAxis((float)v, Axis);
        }

        /// <summary>The knob, world, with the node at rig value <paramref name="v"/>.</summary>
        public Vector3 KnobWorld(double v)
        {
            Vector3 k = Vector3.Scale(T.localScale, KnobLocal);
            Vector3 local = Slide ? RestPos + Axis * (float)v + RestRot * k
                                  : RestPos + RestRot * (Quaternion.AngleAxis((float)v, Axis) * k);
            return T.parent.TransformPoint(local);
        }

        /// <summary>The bound node by name, or null.</summary>
        public static RigHandle Find(AirframeRig rig, string node) => Find(rig, node, null);

        /// <summary>The bound node by name, with the hand's point given by an empty under it (Codex's <c>Grip_*</c>) when
        /// there is one, rather than worked out from the meshes.</summary>
        public static RigHandle Find(AirframeRig rig, string node, Transform gripEmpty)
        {
            if (rig == null || string.IsNullOrEmpty(node)) return null;
            foreach (AirframeRig.Bound b in rig.Nodes)
            {
                if (b.Node.Node != node || b.T == null || b.T.parent == null) continue;
                int n = b.Node.MapPoints;
                return new RigHandle
                {
                    T = b.T, RestPos = b.RestPos, RestRot = b.RestRot, Axis = b.Axis, Name = node, Node = b.Node, Slide = b.Node.Move,
                    P0 = b.Node.MapPoint(0).Param, V0 = b.Node.MapPoint(0).Value,
                    P1 = b.Node.MapPoint(n - 1).Param, V1 = b.Node.MapPoint(n - 1).Value,
                    KnobLocal = gripEmpty != null ? b.T.InverseTransformPoint(gripEmpty.position) : KnobOf(b.T, b.Node.Move),
                };
            }
            return null;
        }

        /// <summary>Where a hand takes the part, in its own frame: a lever's knob is the farthest vertex of its meshes from
        /// the pivot (its origin); a slide knob's is the middle of its meshes. Read once per binding, from every mesh under
        /// the node (glTFast puts a mesh's further primitives on children: CLAUDE.md, "A glTF mesh is not a Unity mesh").</summary>
        public static Vector3 KnobOf(Transform node, bool slide)
        {
            Vector3 far = Vector3.zero, sum = Vector3.zero;
            float best = -1f; int count = 0;
            foreach (MeshFilter mf in node.GetComponentsInChildren<MeshFilter>())
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                Vector3[] v = m.vertices;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = node.InverseTransformPoint(mf.transform.TransformPoint(v[i]));
                    sum += p; count++;
                    float d = p.sqrMagnitude;
                    if (d > best) { best = d; far = p; }
                }
            }
            if (count == 0) return Vector3.zero;
            return slide ? sum / count : far;
        }

        /// <summary>How many readable mesh vertices the knob was found from (0 = it fell back to the origin).</summary>
        public static int VertexCount(Transform node)
        {
            int n = 0;
            foreach (MeshFilter mf in node.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.sharedMesh.isReadable) n += mf.sharedMesh.vertexCount;
            return n;
        }
    }
}

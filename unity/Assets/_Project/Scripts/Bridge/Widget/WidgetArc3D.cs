using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// A SOLID curved arrow in the scene (owner 2026-10-12, the autorotation shot: "the airplane should obscure an arrow that
    /// is behind it"): a round tube along a world-space path ending in a cone head, unlit, depth-tested like the airframe.
    /// Rebuilt each frame from the path.
    /// </summary>
    public sealed class WidgetArc3D
    {
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly List<Vector3> _v = new(); private readonly List<int> _t = new();
        private const int Sides = 12;

        public WidgetArc3D(string name, Transform parent, int layer, Color c)
        {
            _go = new GameObject(name); _go.layer = layer;   // (no parent: the path is in world space)
            if (parent != null) Object.DontDestroyOnLoad(_go);
            _mesh = new Mesh { name = name }; _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>(); r.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = c };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            _go.SetActive(false);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }

        /// <summary>The path (world, in order — the head at the last point), tube radius r, head length and radius.</summary>
        public void Place(IReadOnlyList<Vector3> path, float r, float headLen, float headR)
        {
            if (path == null || path.Count < 2) { Hide(); return; }
            // Stop the tube where the head begins.
            var pts = new List<Vector3>(path);
            Vector3 tip = pts[^1];
            float left = headLen;
            while (pts.Count > 2 && Vector3.Distance(pts[^2], tip) < left) pts.RemoveAt(pts.Count - 1);
            Vector3 dirH = (tip - pts[^2]).normalized;
            Vector3 headBase = tip - dirH * headLen;
            pts[^1] = headBase;
            _v.Clear(); _t.Clear();
            Vector3 prevN = Vector3.zero;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 tan = (i == 0 ? pts[1] - pts[0] : i == pts.Count - 1 ? pts[i] - pts[i - 1] : pts[i + 1] - pts[i - 1]).normalized;
                Vector3 n = i == 0 ? Vector3.Cross(tan, Mathf.Abs(Vector3.Dot(tan, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized
                                   : Vector3.ProjectOnPlane(prevN, tan).normalized;
                prevN = n; Vector3 b = Vector3.Cross(tan, n);
                for (int k = 0; k < Sides; k++) { float a = k * Mathf.PI * 2f / Sides; _v.Add(pts[i] + (n * Mathf.Cos(a) + b * Mathf.Sin(a)) * r); }
            }
            for (int i = 0; i + 1 < pts.Count; i++)
                for (int k = 0; k < Sides; k++)
                {
                    int a0 = i * Sides + k, a1 = i * Sides + (k + 1) % Sides, b0 = a0 + Sides, b1 = a1 + Sides;
                    Both(a0, b0, a1); Both(a1, b0, b1);
                }
            // The head: a cone from the head base to the tip.
            int hb = _v.Count;
            Vector3 hn = Vector3.ProjectOnPlane(prevN, dirH).normalized, hbv = Vector3.Cross(dirH, hn);
            for (int k = 0; k < Sides; k++) { float a = k * Mathf.PI * 2f / Sides; _v.Add(headBase + (hn * Mathf.Cos(a) + hbv * Mathf.Sin(a)) * headR); }
            int ti = _v.Count; _v.Add(tip); int ci = _v.Count; _v.Add(headBase);
            for (int k = 0; k < Sides; k++) { int a0 = hb + k, a1 = hb + (k + 1) % Sides; Both(a0, a1, ti); Both(a1, a0, ci); }
            _go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); _go.transform.localScale = Vector3.one;
            _mesh.Clear(); _mesh.SetVertices(_v); _mesh.SetTriangles(_t, 0); _mesh.RecalculateBounds();
            if (!_go.activeSelf) _go.SetActive(true);
        }

        // Both windings: either side may face the camera.
        private void Both(int a, int b, int c) { _t.Add(a); _t.Add(b); _t.Add(c); _t.Add(a); _t.Add(c); _t.Add(b); }
    }
}

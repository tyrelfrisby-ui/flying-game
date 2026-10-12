using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// A SOLID arrow in the scene (owner 2026-10-12, W12 orbit shot): a round shaft and a cone head, unlit in one colour, so
    /// it reads from every side as the camera circles — a screen-drawn line collapses to a dot end-on. Placed in world space
    /// each frame from tail to tip.
    /// </summary>
    public sealed class WidgetArrow3D
    {
        private readonly GameObject _go;
        private readonly Transform _shaft, _head;

        public WidgetArrow3D(string name, Transform parent, int layer, Color c)
        {
            _go = new GameObject(name); _go.transform.SetParent(parent, false); _go.layer = layer;
            var mat = new Material(Shader.Find("Unlit/Color")) { color = c };
            _shaft = Part("Shaft", Cylinder(16), mat, layer);
            _head = Part("Head", Cone(20), mat, layer);
            _go.SetActive(false);
        }

        private Transform Part(string n, Mesh m, Material mat, int layer)
        {
            var g = new GameObject(n); g.transform.SetParent(_go.transform, false); g.layer = layer;
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g.transform;
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }

        /// <summary>Tail to tip (world), shaft radius r; the head is 3r wide and 8r long.</summary>
        public void Place(Vector3 tail, Vector3 tip, float r)
        {
            Vector3 d = tip - tail; float len = d.magnitude;
            if (len < 1e-3f) { Hide(); return; }
            if (!_go.activeSelf) _go.SetActive(true);
            Vector3 u = d / len; float headLen = Mathf.Min(8f * r, 0.45f * len);
            var rot = Quaternion.FromToRotation(Vector3.up, u);
            // Meshes are unit height along +y from their base.
            _shaft.SetPositionAndRotation(tail, rot); _shaft.localScale = new Vector3(r, len - headLen, r);
            _head.SetPositionAndRotation(tip - u * headLen, rot); _head.localScale = new Vector3(3f * r, headLen, 3f * r);
        }

        private static Mesh Cylinder(int n)
        {
            var v = new Vector3[n * 2 + 2]; var t = new int[n * 12];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n; var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i] = p; v[n + i] = p + Vector3.up;
            }
            v[2 * n] = Vector3.zero; v[2 * n + 1] = Vector3.up;
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                t[k++] = i; t[k++] = n + i; t[k++] = j;  t[k++] = j; t[k++] = n + i; t[k++] = n + j;   // side
                t[k++] = 2 * n; t[k++] = i; t[k++] = j;                                               // base cap
                t[k++] = 2 * n + 1; t[k++] = n + j; t[k++] = n + i;                                   // top cap
            }
            return TwoSided(v, t);
        }

        // Both windings: the unlit shader culls back faces, and either side may face the circling camera.
        private static Mesh TwoSided(Vector3[] v, int[] t)
        {
            var all = new int[t.Length * 2]; t.CopyTo(all, 0);
            for (int i = 0; i < t.Length; i += 3) { all[t.Length + i] = t[i]; all[t.Length + i + 1] = t[i + 2]; all[t.Length + i + 2] = t[i + 1]; }
            var m = new Mesh { vertices = v, triangles = all }; m.RecalculateBounds(); return m;
        }

        private static Mesh Cone(int n)
        {
            var v = new Vector3[n + 2]; var t = new int[n * 6];
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; v[i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            v[n] = Vector3.zero; v[n + 1] = Vector3.up;
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                t[k++] = n + 1; t[k++] = j; t[k++] = i;   // side
                t[k++] = n; t[k++] = i; t[k++] = j;       // base
            }
            return TwoSided(v, t);
        }
    }
}

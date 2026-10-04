using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Slices a mesh's triangles along planes, keeping every vertex attribute (position, normal, tangent, UVs, colour) by
    /// interpolation and sharing the new vertices between neighbours (one per cut edge). Used to give a coarse downloaded
    /// model real panel edges at a control surface's hinge line and span ends before it is rigged (owner 2026-10-04:
    /// "make the control surfaces move again with these new skins").
    /// </summary>
    public sealed class MeshSlicer
    {
        public readonly List<Vector3> Pos = new(), Nrm = new(), Root = new();
        public readonly List<Vector4> Tan = new();
        public readonly List<Vector2> Uv0 = new(), Uv1 = new();
        public readonly List<Color> Col = new();
        public readonly List<List<int>> Tris = new();
        private readonly bool _n, _t, _u0, _u1, _c;

        /// <param name="toRoot">Mesh space → the frame the planes are given in.</param>
        public MeshSlicer(Mesh m, Matrix4x4 toRoot)
        {
            m.GetVertices(Pos); m.GetNormals(Nrm); m.GetTangents(Tan); m.GetUVs(0, Uv0); m.GetUVs(1, Uv1); m.GetColors(Col);
            _n = Nrm.Count == Pos.Count; _t = Tan.Count == Pos.Count; _u0 = Uv0.Count == Pos.Count; _u1 = Uv1.Count == Pos.Count; _c = Col.Count == Pos.Count;
            foreach (Vector3 p in Pos) Root.Add(toRoot.MultiplyPoint3x4(p));
            for (int s = 0; s < m.subMeshCount; s++) Tris.Add(new List<int>(m.GetTriangles(s)));
        }

        private int Lerp(int a, int b, float t)
        {
            Pos.Add(Vector3.Lerp(Pos[a], Pos[b], t)); Root.Add(Vector3.Lerp(Root[a], Root[b], t));
            if (_n) Nrm.Add(Vector3.Lerp(Nrm[a], Nrm[b], t).normalized);
            if (_t) Tan.Add(Vector4.Lerp(Tan[a], Tan[b], t));
            if (_u0) Uv0.Add(Vector2.Lerp(Uv0[a], Uv0[b], t));
            if (_u1) Uv1.Add(Vector2.Lerp(Uv1[a], Uv1[b], t));
            if (_c) Col.Add(Color.Lerp(Col[a], Col[b], t));
            return Pos.Count - 1;
        }

        /// <summary>Cut every triangle that <paramref name="inScope"/> accepts (all three root-space corners) by the plane
        /// dot(p − point, normal) = 0. Returns the number of triangles cut.</summary>
        public int Slice(Vector3 point, Vector3 normal, System.Func<Vector3, bool> inScope)
        {
            const float eps = 1e-4f;
            var cut = new Dictionary<long, int>();
            int Edge(int a, int b, float fa, float fb)
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cut.TryGetValue(k, out int v)) return v;
                v = Lerp(a, b, fa / (fa - fb)); cut[k] = v; return v;
            }
            int count = 0;
            for (int s = 0; s < Tris.Count; s++)
            {
                List<int> src = Tris[s]; var dst = new List<int>(src.Count + 32);
                for (int k = 0; k + 2 < src.Count; k += 3)
                {
                    int[] v = { src[k], src[k + 1], src[k + 2] };
                    if (!inScope(Root[v[0]]) || !inScope(Root[v[1]]) || !inScope(Root[v[2]])) { dst.Add(v[0]); dst.Add(v[1]); dst.Add(v[2]); continue; }
                    float[] f = { Vector3.Dot(Root[v[0]] - point, normal), Vector3.Dot(Root[v[1]] - point, normal), Vector3.Dot(Root[v[2]] - point, normal) };
                    bool pos = f[0] > eps || f[1] > eps || f[2] > eps, neg = f[0] < -eps || f[1] < -eps || f[2] < -eps;
                    if (!(pos && neg)) { dst.Add(v[0]); dst.Add(v[1]); dst.Add(v[2]); continue; }
                    count++;
                    // Rotate so corner 0 is the one alone on its side (keeps the winding).
                    int lone = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        int j = (i + 1) % 3, l = (i + 2) % 3;
                        bool si = f[i] > 0, sj = f[j] > 0, sl = f[l] > 0;
                        if (si != sj && si != sl && Mathf.Abs(f[i]) > eps) { lone = i; break; }
                    }
                    int a = v[lone], b = v[(lone + 1) % 3], c = v[(lone + 2) % 3];
                    float fa = f[lone], fb = f[(lone + 1) % 3], fc = f[(lone + 2) % 3];
                    if (Mathf.Abs(fb) <= eps || Mathf.Abs(fc) <= eps)
                    {
                        // One corner on the plane: two triangles.
                        if (Mathf.Abs(fb) <= eps) { int x = Edge(a, c, fa, fc); dst.Add(a); dst.Add(b); dst.Add(x); dst.Add(x); dst.Add(b); dst.Add(c); }
                        else { int x = Edge(a, b, fa, fb); dst.Add(a); dst.Add(x); dst.Add(c); dst.Add(x); dst.Add(b); dst.Add(c); }
                        continue;
                    }
                    int ab = Edge(a, b, fa, fb), ac = Edge(a, c, fa, fc);
                    dst.Add(a); dst.Add(ab); dst.Add(ac);
                    dst.Add(ab); dst.Add(b); dst.Add(c);
                    dst.Add(ab); dst.Add(c); dst.Add(ac);
                }
                Tris[s] = dst;
            }
            return count;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = Pos.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(Pos);
            if (_n) m.SetNormals(Nrm);
            if (_t) m.SetTangents(Tan);
            if (_u0) m.SetUVs(0, Uv0);
            if (_u1) m.SetUVs(1, Uv1);
            if (_c) m.SetColors(Col);
            m.subMeshCount = Tris.Count;
            for (int s = 0; s < Tris.Count; s++) m.SetTriangles(Tris[s], s, false);
            m.RecalculateBounds();
            return m;
        }
    }
}

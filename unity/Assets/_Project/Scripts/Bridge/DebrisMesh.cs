using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The break-up's mesh cutting, and the one place it is done (owner 2026-11-03: "when the pitts crashes and breaks
    /// apart it turns into the old pitts render ... new broken pieces ... that do not change or lose their paint").
    ///
    /// The old cut (AirframeVisual.CloneMesh) copied positions and triangle indices and nothing else: every submesh was
    /// merged into submesh 0, the UVs were dropped and the normals recomputed. A model whose paint is a texture (all of
    /// ours) came apart as flat, untextured slabs in its FIRST material's colour — and so did the half that stayed on
    /// the aeroplane, because the kept side went through the same copy. A cut here carries EVERY vertex attribute the
    /// source has (positions, normals, tangents, colours, all eight UV channels) and keeps each triangle in its own
    /// submesh, so a piece wears the same material on the same faces as the intact renderer did.
    ///
    /// TODO(debris): the cut face is left open (no cap). The prebuilt debris GLBs (lane/codex-pitts-debris) carry
    /// capped breaks; the runtime split is the fallback when a type has none.
    /// </summary>
    public static class DebrisMesh
    {
        /// <summary>Split <paramref name="src"/> by triangle: <paramref name="isLost"/> (given the triangle's three vertex
        /// indices) says which go. Both halves keep every vertex attribute and the source's submesh slots — empty slots
        /// included, so the KEPT half can go straight back on the renderer it came from without touching its materials.
        /// Nothing lost: lost is null and kept is <paramref name="src"/> itself. Everything lost: kept is null. An
        /// unreadable mesh cannot be cut and comes back as (null, src).</summary>
        public static void Split(Mesh src, System.Func<int, int, int, bool> isLost, out Mesh lost, out Mesh kept)
        {
            lost = null; kept = src;
            if (src == null || !src.isReadable) return;
            int subs = src.subMeshCount;
            var lt = new List<int>[subs]; var kt = new List<int>[subs];
            bool anyLost = false, anyKept = false;
            for (int s = 0; s < subs; s++)
            {
                lt[s] = new List<int>(); kt[s] = new List<int>();
                if (src.GetTopology(s) != MeshTopology.Triangles)
                {
                    // Lines and points (a bracing wire drawn as lines) are not cut: they stay on the aeroplane.
                    int[] idx = src.GetIndices(s);
                    if (idx.Length > 0) { kt[s].AddRange(idx); anyKept = true; }
                    continue;
                }
                int[] t = src.GetTriangles(s);
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    bool l = isLost(t[k], t[k + 1], t[k + 2]);
                    List<int> dst = l ? lt[s] : kt[s];
                    dst.Add(t[k]); dst.Add(t[k + 1]); dst.Add(t[k + 2]);
                    if (l) anyLost = true; else anyKept = true;
                }
            }
            if (!anyLost) return;   // nothing goes: the source stays exactly as it is
            lost = Build(src, lt, src.name + "-lost");
            kept = anyKept ? Build(src, kt, src.name + "-kept") : null;
        }

        /// <summary>A new mesh from the source's vertices that <paramref name="indices"/> uses (per submesh), compacted,
        /// with every attribute the source carries.</summary>
        private static Mesh Build(Mesh src, List<int>[] indices, string name)
        {
            int n = src.vertexCount;
            var map = new int[n];
            for (int i = 0; i < n; i++) map[i] = -1;
            var order = new List<int>(n);
            foreach (List<int> l in indices) foreach (int i in l) if (map[i] < 0) { map[i] = order.Count; order.Add(i); }

            var m = new Mesh { name = name, indexFormat = order.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(Pick(src.vertices, order));
            bool normals = src.HasVertexAttribute(VertexAttribute.Normal);
            if (normals) m.SetNormals(Pick(src.normals, order));
            if (src.HasVertexAttribute(VertexAttribute.Tangent)) m.SetTangents(Pick(src.tangents, order));
            if (src.HasVertexAttribute(VertexAttribute.Color)) m.SetColors(Pick(src.colors, order));
            for (int ch = 0; ch < 8; ch++)
            {
                var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
                if (!src.HasVertexAttribute(attr)) continue;
                switch (src.GetVertexAttributeDimension(attr))
                {
                    case 2: { var l = new List<Vector2>(); src.GetUVs(ch, l); m.SetUVs(ch, Pick(l, order)); break; }
                    case 3: { var l = new List<Vector3>(); src.GetUVs(ch, l); m.SetUVs(ch, Pick(l, order)); break; }
                    default: { var l = new List<Vector4>(); src.GetUVs(ch, l); m.SetUVs(ch, Pick(l, order)); break; }
                }
            }
            m.subMeshCount = indices.Length;
            for (int s = 0; s < indices.Length; s++)
            {
                var t = new int[indices[s].Count];
                for (int k = 0; k < t.Length; k++) t[k] = map[indices[s][k]];
                m.SetIndices(t, src.GetTopology(s), s, false);
            }
            if (!normals) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static List<T> Pick<T>(IList<T> a, List<int> order)
        {
            var o = new List<T>(order.Count);
            if (a == null || a.Count == 0) return o;
            foreach (int i in order) o.Add(a[i]);
            return o;
        }

        /// <summary>A debris piece's mesh and the materials it draws with, aligned the way a renderer reads them:
        /// material i on submesh i. Submeshes with no triangles go, and so do their materials — in step, so every face
        /// keeps its own material. A submesh the source drew with no material (more submeshes than materials) was never
        /// on screen and does not appear on the piece either. Extra materials past the last submesh (Unity draws the
        /// last submesh again with each) are kept with it. Never mutates <paramref name="mesh"/>: it may be the model's
        /// own shared asset.</summary>
        public static (Mesh mesh, Material[] mats) ForPiece(Mesh mesh, Material[] mats)
        {
            mats ??= System.Array.Empty<Material>();
            if (mesh == null) return (null, mats);
            int subs = mesh.subMeshCount;
            var keep = new List<int>(subs);
            for (int s = 0; s < subs; s++) if (s < mats.Length && mesh.GetSubMesh(s).indexCount > 0) keep.Add(s);
            bool lastKept = subs > 0 && keep.Count > 0 && keep[^1] == subs - 1;
            var outMats = new List<Material>(keep.Count + 2);
            foreach (int s in keep) outMats.Add(mats[s]);
            if (lastKept) for (int i = subs; i < mats.Length; i++) outMats.Add(mats[i]);
            if (keep.Count == subs) return (mesh, outMats.ToArray());
            if (keep.Count == 0) return (null, System.Array.Empty<Material>());
            Mesh m = Object.Instantiate(mesh);
            m.name = mesh.name + "-piece";
            var desc = new List<(int[] idx, MeshTopology topo)>(keep.Count);
            foreach (int s in keep) desc.Add((mesh.GetIndices(s), mesh.GetTopology(s)));
            m.subMeshCount = keep.Count;
            for (int i = 0; i < desc.Count; i++) m.SetIndices(desc[i].idx, desc[i].topo, i, false);
            m.RecalculateBounds();
            return (m, outMats.ToArray());
        }
    }
}

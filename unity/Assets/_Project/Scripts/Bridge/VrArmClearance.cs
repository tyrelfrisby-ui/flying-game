using System.Collections.Generic;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// HOW FAR THE PILOT'S ARM IS FROM THE COCKPIT, answered from the aeroplane's own meshes (Ty 2026-11-04, Quest: "the
    /// arms go into the structure on the great lakes like the switch box on the left"). Core's <see cref="ArmPose"/> asks
    /// it while choosing where the elbow swings.
    ///
    /// Built once per seat (<see cref="Build"/>): every triangle of the airframe's meshes inside a box round the seat is
    /// voxelised into a 2.5 cm grid in the aeroplane's frame, each surface cell keeping its averaged face normal, and a
    /// two-pass sweep gives every cell its nearest surface cell (a feature transform). A query is then one lookup and a
    /// subtraction: distance to that surface cell, negative when the point is BEHIND its face (inside the switch box,
    /// through the sidewall). The controls the hands hold (stick, throttle) and anything named in
    /// <paramref name="exclude"/> are left out: the hand is meant to be on them.
    ///
    /// Cost: ~150 k cells (two arrays, ~1.2 MB), built in tens of milliseconds at bind; a query is a handful of
    /// multiplies, no allocation. Points outside the box are in the clear.
    /// </summary>
    public sealed class VrArmClearance : IArmClearance
    {
        public const float Cell = 0.025f;
        /// <summary>The box round the eye the grid covers, aeroplane frame (+x right, +y up, +z nose).</summary>
        public static readonly Vector3 BoxMin = new(-0.65f, -1.05f, -0.55f), BoxMax = new(0.65f, 0.25f, 0.95f);

        private Transform _air;
        private Vector3 _origin;          // aeroplane-local corner of cell (0,0,0)
        private int _nx, _ny, _nz;
        private int[] _nearest;           // per cell: index of its nearest surface cell, -1 none
        private Vector3[] _normal;        // per surface cell: summed face normal (aeroplane-local)
        public int SurfaceCells { get; private set; }
        public int Triangles { get; private set; }
        public bool Ready => _nearest != null;

        /// <summary>Voxelise the airframe under <paramref name="air"/> round the eye at <paramref name="eyeLocal"/>
        /// (aeroplane frame). Renderers under any of <paramref name="exclude"/> are skipped.</summary>
        public void Build(Transform air, Vector3 eyeLocal, IList<Transform> exclude, string what = "")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _air = air;
            _origin = eyeLocal + BoxMin;
            Vector3 size = BoxMax - BoxMin;
            _nx = Mathf.CeilToInt(size.x / Cell); _ny = Mathf.CeilToInt(size.y / Cell); _nz = Mathf.CeilToInt(size.z / Cell);
            int n = _nx * _ny * _nz;
            _nearest = new int[n];
            _normal = new Vector3[n];
            var surface = new bool[n];
            for (int i = 0; i < n; i++) _nearest[i] = -1;
            Triangles = 0;
            int unreadable = 0;
            Quaternion toLocal = Quaternion.Inverse(air.rotation);
            var verts = new List<Vector3>(4096);
            var tris = new List<int>(8192);
            foreach (MeshFilter mf in air.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || Excluded(mf.transform, exclude)) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                Mesh m = mf.sharedMesh;
                if (!m.isReadable) { unreadable++; continue; }
                AddMesh(m, mf.transform, toLocal, verts, tris, surface);
            }
            foreach (SkinnedMeshRenderer smr in air.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (smr.sharedMesh == null || !smr.enabled || Excluded(smr.transform, exclude)) continue;
                if (!smr.sharedMesh.isReadable) { unreadable++; continue; }
                AddMesh(smr.sharedMesh, smr.transform, toLocal, verts, tris, surface);   // bind pose: good enough for structure
            }
            SurfaceCells = 0;
            for (int i = 0; i < n; i++) if (surface[i]) { _nearest[i] = i; SurfaceCells++; }
            Sweep();
            Debug.Log($"[VrCockpit] arm clearance {what}: {Triangles} triangles → {SurfaceCells} surface cells of {n} ({_nx}x{_ny}x{_nz} at {Cell * 100:F1} cm) in {sw.ElapsedMilliseconds} ms" +
                      (unreadable > 0 ? $"; {unreadable} meshes not readable (left out)" : ""));
        }

        private static bool Excluded(Transform t, IList<Transform> exclude)
        {
            if (exclude == null) return false;
            for (Transform p = t; p != null; p = p.parent)
                for (int i = 0; i < exclude.Count; i++) if (exclude[i] != null && p == exclude[i]) return true;
            return false;
        }

        private void AddMesh(Mesh m, Transform t, Quaternion toLocal, List<Vector3> verts, List<int> tris, bool[] surface)
        {
            verts.Clear(); m.GetVertices(verts);
            // Into the aeroplane's frame, metres (its own scale ignored, as everywhere in the cockpit code).
            Matrix4x4 toWorld = t.localToWorldMatrix;
            Vector3 airPos = _air.position;
            for (int i = 0; i < verts.Count; i++) verts[i] = toLocal * (toWorld.MultiplyPoint3x4(verts[i]) - airPos);
            Vector3 lo = _origin, hi = _origin + new Vector3(_nx, _ny, _nz) * Cell;
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                if (m.GetTopology(sub) != MeshTopology.Triangles) continue;
                tris.Clear(); m.GetTriangles(tris, sub);
                for (int k = 0; k + 2 < tris.Count; k += 3)
                {
                    Vector3 a = verts[tris[k]], b = verts[tris[k + 1]], c = verts[tris[k + 2]];
                    Vector3 mn = Vector3.Min(a, Vector3.Min(b, c)), mx = Vector3.Max(a, Vector3.Max(b, c));
                    if (mx.x < lo.x || mx.y < lo.y || mx.z < lo.z || mn.x > hi.x || mn.y > hi.y || mn.z > hi.z) continue;
                    Triangles++;
                    Vector3 nrm = Vector3.Cross(b - a, c - a);
                    float area2 = nrm.magnitude;
                    if (area2 < 1e-10f) continue;
                    nrm /= area2;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((mn.x - lo.x) / Cell)), x1 = Mathf.Min(_nx - 1, Mathf.FloorToInt((mx.x - lo.x) / Cell));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt((mn.y - lo.y) / Cell)), y1 = Mathf.Min(_ny - 1, Mathf.FloorToInt((mx.y - lo.y) / Cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((mn.z - lo.z) / Cell)), z1 = Mathf.Min(_nz - 1, Mathf.FloorToInt((mx.z - lo.z) / Cell));
                    const float half = Cell * 0.8660254f;   // half the cell's diagonal
                    for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                            for (int z = z0; z <= z1; z++)
                            {
                                Vector3 p = lo + new Vector3((x + 0.5f) * Cell, (y + 0.5f) * Cell, (z + 0.5f) * Cell);
                                if (Mathf.Abs(Vector3.Dot(p - a, nrm)) > half) continue;
                                if ((ClosestOnTriangle(p, a, b, c) - p).sqrMagnitude > half * half) continue;
                                int i = Index(x, y, z);
                                surface[i] = true;
                                _normal[i] += nrm * area2;   // area-weighted: a big panel wins over a sliver
                            }
                }
            }
        }

        /// <summary>Two passes (forward, backward) over the 13 half-neighbourhoods: every cell takes the nearest of its
        /// neighbours' nearest surface cells. Exact enough at this resolution for "is the arm within 5 cm".</summary>
        private void Sweep()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int s = pass == 0 ? 1 : -1;
                int xa = pass == 0 ? 0 : _nx - 1, xb = pass == 0 ? _nx : -1;
                int ya = pass == 0 ? 0 : _ny - 1, yb = pass == 0 ? _ny : -1;
                int za = pass == 0 ? 0 : _nz - 1, zb = pass == 0 ? _nz : -1;
                for (int x = xa; x != xb; x += s)
                    for (int y = ya; y != yb; y += s)
                        for (int z = za; z != zb; z += s)
                        {
                            int i = Index(x, y, z);
                            int best = _nearest[i];
                            float bd = best >= 0 ? D2(i, best) : float.MaxValue;
                            for (int dx = -1; dx <= 0; dx++)
                                for (int dy = -1; dy <= 1; dy++)
                                    for (int dz = -1; dz <= 1; dz++)
                                    {
                                        // The half of the 26-neighbourhood already visited in this pass's order.
                                        if (dx == 0 && (dy > 0 || (dy == 0 && dz >= 0))) continue;
                                        int nx = x + dx * s, ny = y + dy * s, nz = z + dz * s;
                                        if (nx < 0 || ny < 0 || nz < 0 || nx >= _nx || ny >= _ny || nz >= _nz) continue;
                                        int f = _nearest[Index(nx, ny, nz)];
                                        if (f < 0) continue;
                                        float d = D2(i, f);
                                        if (d < bd) { bd = d; best = f; }
                                    }
                            _nearest[i] = best;
                        }
            }
        }

        private int Index(int x, int y, int z) => (x * _ny + y) * _nz + z;
        private Vector3 Centre(int i)
        {
            int z = i % _nz, y = (i / _nz) % _ny, x = i / (_nz * _ny);
            return _origin + new Vector3((x + 0.5f) * Cell, (y + 0.5f) * Cell, (z + 0.5f) * Cell);
        }
        private float D2(int a, int b) => (Centre(a) - Centre(b)).sqrMagnitude;

        /// <summary>WORLD point → signed distance to the structure, metres (Core's convention).</summary>
        public double Distance(Vec3 world)
        {
            if (_nearest == null || _air == null) return 1.0;
            Vector3 p = Quaternion.Inverse(_air.rotation) * (new Vector3((float)world.X, (float)world.Y, (float)world.Z) - _air.position);
            return DistanceLocal(p);
        }

        /// <summary>The same for an aeroplane-local point.</summary>
        public float DistanceLocal(Vector3 p)
        {
            Vector3 q = (p - _origin) / Cell;
            int x = Mathf.FloorToInt(q.x), y = Mathf.FloorToInt(q.y), z = Mathf.FloorToInt(q.z);
            if (x < 0 || y < 0 || z < 0 || x >= _nx || y >= _ny || z >= _nz) return 1f;
            int f = _nearest[Index(x, y, z)];
            if (f < 0) return 1f;
            Vector3 d = p - Centre(f);
            // The surface runs through its cell, not along its centre: half a cell of slack.
            float dist = Mathf.Max(0f, d.magnitude - Cell * 0.5f);
            Vector3 nrm = _normal[f];
            return nrm.sqrMagnitude > 1e-12f && Vector3.Dot(d, nrm) < -Cell * 0.5f * nrm.magnitude ? -dist : dist;
        }

        /// <summary>Ericson, Real-Time Collision Detection 5.1.5.</summary>
        public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            float v = vb * denom, w = vc * denom;
            return a + ab * v + ac * w;
        }
    }
}

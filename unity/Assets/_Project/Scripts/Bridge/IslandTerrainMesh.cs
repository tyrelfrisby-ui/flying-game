using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FlyingGame.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The island's ground mesh (2026-11-02, lane/claude-island-terrain). Every vertex is a sample of the ONE
    /// height function the sim uses (IslandTerrain via WorldTerrain), on the 25 m lattice the natural relief
    /// was generated on, so the mesh and the wheels agree to within the lattice's own curvature.
    ///
    ///  * 2 km chunks, each a LODGroup of 25 / 50 / 100 / 200 m grids. A chunk switches level where that level's
    ///    MEASURED error (95th percentile of the gap between it and the 25 m surface, sampled per chunk) falls
    ///    under PixelTolerance pixels on the iPad mini's 1,488 px landscape height (LODGroup then applies the
    ///    quality level's lodBias: 0.7 on iOS Medium, the device default).
    ///  * Shared vertices with the ANALYTIC normal (IslandTerrain.HeightAndGradientAt): smooth shading, and the
    ///    same normal at every LOD, so a level change does not change the lighting.
    ///  * A skirt hangs from every chunk edge, deeper than that level's error, so neighbours at different
    ///    levels never show a crack.
    ///  * Per-vertex surface channels for FlyingGame/IslandTerrain: colour = (glacier ice, debris, wetness,
    ///    forest cover), uv0 = (local equilibrium-line altitude m, red-rock country + / tropical coast -,
    ///    gravel + / managed turf -, glacier or patch SNOW + / CREVASSE field -).
    ///  * 5 m cells where a cave mouth needs a hole in the ground (the old builder's PortalHole rule), and where
    ///    the ice is crevassed or carries a boulder (IslandRelief.NeedsFineMesh): a 25 m triangle cannot draw a
    ///    10 m crevasse, and the crevasse is in the height the wheels and the airframe hit. Those sub-cells are
    ///    sampled in the parallel pass. The coarser levels draw the same ground WITHOUT that sub-lattice detail
    ///    (IslandRelief.FineDetail): at their distances a crevasse is a few pixels, which the shader paints.
    /// </summary>
    public static class IslandTerrainMesh
    {
        public const double ChunkM = 2000, CellM = 25, WorldMinM = -20000, WorldMaxM = 20000;
        const int N = (int)(ChunkM / CellM);           // 80 cells a side at LOD0
        static readonly int[] Steps = { 1, 2, 4, 8 };  // 25, 50, 100, 200 m
        public const float PixelTolerance = 2.5f, ScreenHeightPx = 1488, FovDeg = 60;
        const int Detail = 5;                          // 5 m sub-cells in a 25 m cell near a cave mouth

        public struct Stats { public int Chunks, Renderers, FineCells; public long Lod0Triangles, AllTriangles, Vertices; public double BuildS, SampleS; public long GpuBytes; }
        public static Stats Last;

        sealed class ChunkData
        {
            public int Ci, Cj; public double X0, Y0; // sim north / east of the chunk's SW corner (m)
            public float[] H, Dx, Dy; public Color32[] C; public Vector2[] Uv0, Uv1;
            /// <summary>Heights and gradients for the coarser levels: H less the sub-lattice detail (same arrays
            /// as H where a chunk has none).</summary>
            public float[] Hc, Dxc, Dyc;
            public bool Any; public float[] Error = new float[4];
            public bool[] Detail; // per LOD0 cell
            /// <summary>Exact samples for every fine cell ((Detail+1)^2, row-major from its SW corner) and every
            /// stitch cell (centre, then the ring as StitchCell walks it), taken in the parallel pass.</summary>
            public Dictionary<int, Vert[]> Fine = new(), Stitch = new();
        }
        struct Vert { public Vector3 P, N; public Color32 C; public Vector2 A, B; }

        public static GameObject Build(Transform parent, Material material)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var root = new GameObject("Terrain-island");
            root.transform.SetParent(parent, false);
            int count = (int)((WorldMaxM - WorldMinM) / ChunkM);
            var chunks = new ChunkData[count * count];
            // Sampling is pure Core arithmetic (thread-safe, no Unity API): spread it over the cores.
            Parallel.For(0, chunks.Length, idx => chunks[idx] = Sample(idx % count, idx / count));
            double sampleS = clock.Elapsed.TotalSeconds;
            var stats = new Stats { SampleS = sampleS };
            foreach (var c in chunks)
            {
                if (c == null || !c.Any) continue;
                MakeChunk(c, root.transform, material, ref stats);
            }
            stats.BuildS = clock.Elapsed.TotalSeconds;
            Last = stats;
            Debug.Log($"Island terrain: {stats.Chunks} chunks, {stats.Renderers} renderers, {stats.FineCells} fine 25 m cells, LOD0 {stats.Lod0Triangles} triangles, all levels {stats.AllTriangles} triangles / {stats.Vertices} vertices, ~{stats.GpuBytes / 1048576.0:F1} MiB GPU; sampled in {sampleS:F2} s, built in {stats.BuildS:F2} s");
            return root;
        }

        /// <summary>East/north km boxes of every land polygon plus its 0.35 km shore fade: outside all of them the
        /// height function is the ocean bed and there is nothing to build.</summary>
        static readonly (double e0, double e1, double n0, double n1)[] LandBoxes = IslandTerrain.World["land_polygons"].Items
            .Select(v => v.Points).Select(pts => (pts.Min(p => p.E) - .4, pts.Max(p => p.E) + .4, pts.Min(p => p.N) - .4, pts.Max(p => p.N) + .4)).ToArray();

        static ChunkData Sample(int ci, int cj)
        {
            // Unity x = sim east (y), Unity z = sim north (x). Chunk (ci, cj) spans east ci, north cj.
            var c = new ChunkData { Ci = ci, Cj = cj, Y0 = WorldMinM + ci * ChunkM, X0 = WorldMinM + cj * ChunkM };
            double ce0 = c.Y0 / 1000, ce1 = (c.Y0 + ChunkM) / 1000, cn0 = c.X0 / 1000, cn1 = (c.X0 + ChunkM) / 1000;
            bool touches = false;
            foreach (var b in LandBoxes) if (ce1 >= b.e0 && ce0 <= b.e1 && cn1 >= b.n0 && cn0 <= b.n1) { touches = true; break; }
            if (!touches) return c;
            int n = N + 1; c.H = new float[n * n]; c.Dx = new float[n * n]; c.Dy = new float[n * n];
            bool any = false;
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
            {
                double x = c.X0 + j * CellM, y = c.Y0 + i * CellM;
                double h = IslandTerrain.HeightAndGradientAt(x, y, out double dx, out double dy);
                int k = j * n + i; c.H[k] = (float)h; c.Dx[k] = (float)dx; c.Dy[k] = (float)dy;
                if (h > -25) any = true;
            }
            c.Any = any;
            if (!any) return c;
            c.C = new Color32[n * n]; c.Uv0 = new Vector2[n * n]; c.Uv1 = new Vector2[n * n];
            // Forest cover (the expensive term: ~4-7 us) on the 50 m sub-lattice, bilinear between; every
            // other channel exactly per vertex.
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
            {
                int k = j * n + i; double x = c.X0 + j * CellM, y = c.Y0 + i * CellM;
                Attributes(x, y, c.H[k], c.Dx[k], c.Dy[k], (i | j) % 2 == 0, out c.C[k], out c.Uv0[k], out c.Uv1[k]);
            }
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
            {
                if ((i | j) % 2 == 0) continue;
                int i0 = i & ~1, j0 = j & ~1, i1 = Math.Min(N, i0 + 2), j1 = Math.Min(N, j0 + 2);
                float tx = (i - i0) / 2f, ty = (j - j0) / 2f;
                float a = c.C[j0 * n + i0].a, b = c.C[j0 * n + i1].a, d = c.C[j1 * n + i0].a, e = c.C[j1 * n + i1].a;
                var col = c.C[j * n + i]; col.a = (byte)Mathf.RoundToInt((a * (1 - tx) + b * tx) * (1 - ty) + (d * (1 - tx) + e * tx) * ty); c.C[j * n + i] = col;
            }
            c.Detail = new bool[N * N]; bool fine = false;
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                double x0 = c.X0 + j * CellM, y0 = c.Y0 + i * CellM;
                bool ice = IslandRelief.NeedsFineMesh(y0 / 1000, x0 / 1000, (y0 + CellM) / 1000, (x0 + CellM) / 1000);
                fine |= ice;
                c.Detail[j * N + i] = ice || NearCave(x0 + .5 * CellM, y0 + .5 * CellM) || OnShapedField(x0 + .5 * CellM, y0 + .5 * CellM);
            }
            // The coarse levels' ground: without the crevasses, seracs and boulders.
            c.Hc = c.H; c.Dxc = c.Dx; c.Dyc = c.Dy;
            if (fine)
            {
                c.Hc = (float[])c.H.Clone(); c.Dxc = (float[])c.Dx.Clone(); c.Dyc = (float[])c.Dy.Clone();
                for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                {
                    int k = j * n + i; double f = IslandRelief.FineDetailAndGradientAt(c.X0 + j * CellM, c.Y0 + i * CellM, out double fx, out double fy);
                    if (f == 0 && fx == 0 && fy == 0) continue;
                    c.Hc[k] -= (float)f; c.Dxc[k] -= (float)fx; c.Dyc[k] -= (float)fy;
                }
            }
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                if (c.Detail[j * N + i]) c.Fine[j * N + i] = FineSamples(c, i, j);
                else if (DetailNeighbour(c, i, j)) c.Stitch[j * N + i] = StitchSamples(c, i, j);
            }
            // Measured error of each coarser level against the 25 m surface (bilinear inside a coarse cell).
            for (int l = 1; l < Steps.Length; l++)
            {
                int s = Steps[l]; var gaps = new List<float>(n * n);
                for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                {
                    int i0 = Math.Min(N - s, i / s * s), j0 = Math.Min(N - s, j / s * s); float tx = (i - i0) / (float)s, ty = (j - j0) / (float)s;
                    float a = c.Hc[j0 * n + i0], b = c.Hc[j0 * n + i0 + s], d = c.Hc[(j0 + s) * n + i0], e = c.Hc[(j0 + s) * n + i0 + s];
                    // Same diagonal split as the triangles below.
                    float z = tx >= ty ? a + (b - a) * tx + (e - b) * ty : a + (e - d) * tx + (d - a) * ty;
                    if (c.Hc[j * n + i] > -25) gaps.Add(Math.Abs(z - c.Hc[j * n + i]));
                }
                gaps.Sort(); c.Error[l] = gaps.Count == 0 ? 0 : gaps[(int)(gaps.Count * .95)];
            }
            return c;
        }

        static void Attributes(double x, double y, double h, double dx, double dy, bool forestToo, out Color32 colour, out Vector2 uv0, out Vector2 uv1)
        {
            var p = new IslandPoint(y / 1000, x / 1000);
            var (ice, debris, wet, ela) = IslandRelief.Channels(p);
            double slope = Math.Sqrt(dx * dx + dy * dy);
            float forest = forestToo && h > Forest.ShoreClearM ? (float)Forest.CoverAt(x, y, h, slope, dx) : 0;
            var surface = h > -1 ? IslandTerrain.SurfaceAt(x, y) : WorldTerrain.Surface.Rough;
            var (snow, _, crevasse, _) = IslandRelief.Classes(p);
            colour = new Color32(B(ice), B(debris), B(wet), B(forest));
            uv0 = new Vector2(ela, (float)(Region(p, 2, 99, -7, -1) - Region(p, 2.8, 99, 2.4, 99)));
            float gravel = surface == WorldTerrain.Surface.Gravel ? 1 : 0, turf = surface == WorldTerrain.Surface.Grass || surface == WorldTerrain.Surface.Paved ? 1 : 0;
            // The crevasse FIELD strength the height uses (none below the onset, full from 0.5): the generator
            // clears the snow off a crevasse field, so the two never overlap and share one channel.
            double field = Math.Clamp((crevasse - IslandRelief.CrevasseOnset) / (.5 - IslandRelief.CrevasseOnset), 0, 1);
            uv1 = new Vector2(gravel - turf, (float)(snow - field * field * (3 - 2 * field)));
        }
        static byte B(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        /// <summary>A material region (km box), feathered over 0.6 km inside its edge so no colour line follows a
        /// grid line: red-rock country (the Grand Canyon area, + in uv0.y) and the leeward tropical coast (the
        /// Dillingham/Mokuleia area, - in uv0.y). docs/pending/ISLAND-TERRAIN-CLAUDE.md, real-topo section.</summary>
        static double Region(IslandPoint p, double e0, double e1, double n0, double n1)
        {
            double d = Math.Min(Math.Min(p.E - e0, e1 - p.E), Math.Min(p.N - n0, n1 - p.N));
            return d <= 0 ? 0 : IslandTerrain.Smooth(Math.Min(1, d / .6));
        }

        /// <summary>On a natural-shaped (grass/turf) strip or its painted shoulders, cell centre (x, y) sim metres. Their
        /// banana, crest or roll is finer than a 25 m triangle: Sulphur Creek's 1.9 m banana and cross-sweep missed the
        /// mesh by 0.49 m, and the ground came up through the strip's paint in dark zig-zags (2026-11-03, the Frank
        /// Church renders). 5 m cells there follow the shape the paint is drawn on.</summary>
        static bool OnShapedField(double x, double y)
        {
            var p = new IslandPoint(y / 1000, x / 1000);
            foreach (var f in IslandTerrain.Fields) if (f.Shape.Kind > -.5 && f.Contains(p, f.Shoulder + 20)) return true;
            return false;
        }
        static bool NearCave(double x, double y)
        {
            var p = new IslandPoint(y / 1000, x / 1000);
            return (p - IslandCavities.Drake["chamber_centre"].Point).Length < .65 || (p - IslandCavities.Pirate["chamber_centre"].Point).Length < .35;
        }
        /// <summary>A mesh aperture for the pirate cave throat and the Drake skylight; the roof/side shells are
        /// built separately (IslandWorldBuilder.Cavities). Same rule as the shipping builder.</summary>
        static bool PortalHole(IslandPoint p)
        {
            var f = IslandTerrain.Feature("F2"); var dp = p - f["centre"].Point; var u = IslandPoint.Heading(f.N("mouth_true_deg") + 180);
            return dp.Dot(u) >= -.015 && dp.Dot(u) < .20 && Math.Abs(dp.E * u.N - dp.N * u.E) < .030
                && (p - f["interior"]["chamber_centre"].Point).Length > .090 || IslandCavities.Skylight(p);
        }

        /// <summary>28 bytes a vertex: position float3, normal snorm8x4, colour unorm8x4, uv0 half4 (ELA, red rock /
        /// tropical, gravel / turf, snow / crevasse field).</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct Packed { public Vector3 P; public sbyte Nx, Ny, Nz, Nw; public Color32 C; public ushort U0, U1, U2, U3; }
        static readonly VertexAttributeDescriptor[] Layout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3), new(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4), new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 4),
        };
        sealed class Builder
        {
            public readonly List<Packed> V = new(); public readonly List<int> T = new();
            public int Add(Vector3 v, Vector3 n, Color32 c, Vector2 a, Vector2 b)
            {
                static sbyte Sn(float f) => (sbyte)Mathf.Clamp(Mathf.RoundToInt(f * 127), -127, 127);
                V.Add(new Packed { P = v, Nx = Sn(n.x), Ny = Sn(n.y), Nz = Sn(n.z), Nw = 0, C = c, U0 = Mathf.FloatToHalf(a.x), U1 = Mathf.FloatToHalf(a.y), U2 = Mathf.FloatToHalf(b.x), U3 = Mathf.FloatToHalf(b.y) });
                return V.Count - 1;
            }
            public int Copy(int from, Vector3 offset) { var q = V[from]; q.P += offset; V.Add(q); return V.Count - 1; }
        }

        static Vector3 Normal(float dx, float dy)
        {
            // dx = dh/d(sim north) = d/dUnityZ, dy = dh/d(sim east) = d/dUnityX.
            return new Vector3(-dy, 1, -dx).normalized;
        }

        static void MakeChunk(ChunkData c, Transform parent, Material material, ref Stats stats)
        {
            var go = new GameObject($"Terrain-{c.Ci:00}-{c.Cj:00}");
            go.transform.SetParent(parent, false);
            var group = go.AddComponent<LODGroup>();
            var lods = new LOD[Steps.Length];
            int n = N + 1;
            for (int l = 0; l < Steps.Length; l++)
            {
                int s = Steps[l], m = N / s + 1; var b = new Builder();
                var index = new int[m * m]; Array.Fill(index, -1);
                // Vertices are created on first use: sea-floor cells add none.
                // Level 0 is the exact ground; the coarser levels the ground less its sub-lattice detail (the two
                // are the same at every node a level-0 regular cell uses: a node with detail has only fine cells round it).
                var hh = l == 0 ? c.H : c.Hc; var hx = l == 0 ? c.Dx : c.Dxc; var hy = l == 0 ? c.Dy : c.Dyc;
                int At(int i, int j)
                {
                    int q = j * m + i; if (index[q] >= 0) return index[q];
                    int k = j * s * n + i * s;
                    return index[q] = b.Add(Pos(c, i * s, j * s, hh[k]), Normal(hx[k], hy[k]), c.C[k], c.Uv0[k], c.Uv1[k]);
                }
                for (int j = 0; j < m - 1; j++) for (int i = 0; i < m - 1; i++)
                {
                    int ka = j * s * n + i * s, kb = ka + s, kd = ka + s * n, ke = kd + s;
                    if (c.H[ka] < -25 && c.H[kb] < -25 && c.H[kd] < -25 && c.H[ke] < -25) continue; // deep sea floor: under the ocean plane
                    if (l == 0 && (c.Detail[j * N + i] || DetailNeighbour(c, i, j)))
                    {
                        if (c.Detail[j * N + i]) { DetailCell(c, b, i, j); stats.FineCells++; } else StitchCell(c, b, i, j, At(i, j), At(i + 1, j), At(i, j + 1), At(i + 1, j + 1));
                        continue;
                    }
                    int a0 = At(i, j), a1 = At(i + 1, j), a2 = At(i, j + 1), a3 = At(i + 1, j + 1);
                    // Clockwise seen from above (Unity's front face); diagonal SW-NE.
                    b.T.Add(a0); b.T.Add(a2); b.T.Add(a3); b.T.Add(a0); b.T.Add(a3); b.T.Add(a1);
                }
                // Deep enough to close any gap a neighbour can open: the level's own error, and in a chunk with fine
                // cells the deepest fine detail (a crevasse plus a serac top), which a coarse neighbour does not draw.
                float detailDepth = c.Fine.Count + c.Stitch.Count > 0 ? (float)(IslandRelief.CrevasseDepthM + IslandRelief.SeracM + 2) : 0f;
                Skirt(c, b, index, m, s, Math.Max(Math.Max(4f, 2 * c.Error[l] + 2), detailDepth));
                float relative = l == Steps.Length - 1 ? .002f : Relative(c.Error[l + 1]);
                if (b.T.Count == 0) { lods[l] = new LOD(relative, new Renderer[0]); continue; }
                var mesh = new Mesh { name = go.name + "-LOD" + l };
                var format = b.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertexBufferParams(b.V.Count, Layout);
                mesh.SetVertexBufferData(b.V, 0, 0, b.V.Count, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                mesh.SetIndexBufferParams(b.T.Count, format);
                if (format == IndexFormat.UInt16) { var ix = new ushort[b.T.Count]; for (int t = 0; t < ix.Length; t++) ix[t] = (ushort)b.T[t]; mesh.SetIndexBufferData(ix, 0, 0, ix.Length, MeshUpdateFlags.DontRecalculateBounds); }
                else mesh.SetIndexBufferData(b.T, 0, 0, b.T.Count, MeshUpdateFlags.DontRecalculateBounds);
                mesh.subMeshCount = 1; mesh.SetSubMesh(0, new SubMeshDescriptor(0, b.T.Count), MeshUpdateFlags.DontRecalculateBounds);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true); // GPU copy only: the sim never reads the mesh
                var lgo = new GameObject(go.name + "-LOD" + l); lgo.transform.SetParent(go.transform, false);
                lgo.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = lgo.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                lods[l] = new LOD(relative, new Renderer[] { r });
                int tris = b.T.Count / 3; stats.AllTriangles += tris; stats.Vertices += b.V.Count; if (l == 0) stats.Lod0Triangles += tris;
                stats.GpuBytes += b.V.Count * 28 + b.T.Count * (format == IndexFormat.UInt32 ? 4 : 2);
                stats.Renderers++;
            }
            // Strictly decreasing transition heights, as LODGroup requires.
            for (int l = 1; l < lods.Length; l++) lods[l].screenRelativeTransitionHeight = Mathf.Min(lods[l].screenRelativeTransitionHeight, lods[l - 1].screenRelativeTransitionHeight * .98f);
            group.SetLODs(lods); group.RecalculateBounds(); group.size = (float)ChunkM;
            stats.Chunks++;
        }

        /// <summary>Screen height fraction at which a level whose error is <paramref name="errorM"/> is still
        /// under PixelTolerance pixels: size * tolerance / (error * screen pixels).</summary>
        static float Relative(float errorM) => Mathf.Clamp((float)ChunkM * PixelTolerance / (Mathf.Max(.05f, errorM) * ScreenHeightPx), .003f, 1f);

        static Vector3 Pos(ChunkData c, double i, double j, double h) => WorldBuilder.U(c.X0 + j * CellM, c.Y0 + i * CellM, h);

        static bool DetailNeighbour(ChunkData c, int i, int j)
        {
            if (c.Detail[j * N + i]) return true;
            return Detailed(c, i - 1, j) || Detailed(c, i + 1, j) || Detailed(c, i, j - 1) || Detailed(c, i, j + 1);
        }

        /// <summary>Whether cell (i, j) has fine sub-cells — asked of the NEIGHBOURING chunk's ground too when (i, j) is
        /// outside this one. Inside-only, a fine cell on a chunk's edge left the next chunk's cell unstitched: a
        /// T-junction along the boundary, and through it the sky, where a crevasse cut 22 m under the straight edge.</summary>
        static bool Detailed(ChunkData c, int i, int j)
        {
            if (i >= 0 && i < N && j >= 0 && j < N) return c.Detail[j * N + i];
            double x0 = c.X0 + j * CellM, y0 = c.Y0 + i * CellM;
            return IslandRelief.NeedsFineMesh(y0 / 1000, x0 / 1000, (y0 + CellM) / 1000, (x0 + CellM) / 1000) || NearCave(x0 + .5 * CellM, y0 + .5 * CellM) || OnShapedField(x0 + .5 * CellM, y0 + .5 * CellM);
        }

        /// <summary>One exact sample of the ground (thread-safe Core arithmetic only; Unity types are plain structs).</summary>
        static Vert Exact(double x, double y)
        {
            double h = IslandTerrain.HeightAndGradientAt(x, y, out double dx, out double dy);
            Attributes(x, y, h, dx, dy, true, out var col, out var a, out var u);
            return new Vert { P = WorldBuilder.U(x, y, h), N = Normal((float)dx, (float)dy), C = col, A = a, B = u };
        }
        static int Add(Builder b, in Vert v) => b.Add(v.P, v.N, v.C, v.A, v.B);

        static Vert[] FineSamples(ChunkData c, int i, int j)
        {
            double x0 = c.X0 + j * CellM, y0 = c.Y0 + i * CellM, d = CellM / Detail;
            var v = new Vert[(Detail + 1) * (Detail + 1)];
            for (int q = 0; q <= Detail; q++) for (int r = 0; r <= Detail; r++) v[q * (Detail + 1) + r] = Exact(x0 + q * d, y0 + r * d);
            return v;
        }

        /// <summary>Centre, then the perimeter clockwise from the SW corner without the four corners (those are
        /// the lattice nodes, shared with the regular cells).</summary>
        static Vert[] StitchSamples(ChunkData c, int i, int j)
        {
            double x0 = c.X0 + j * CellM, y0 = c.Y0 + i * CellM, d = CellM / Detail;
            var v = new List<Vert> { Exact(x0 + CellM / 2, y0 + CellM / 2) };
            for (int r = 1; r < Detail; r++) v.Add(Exact(x0, y0 + r * d));
            for (int q = 1; q < Detail; q++) v.Add(Exact(x0 + q * d, y0 + CellM));
            for (int r = Detail - 1; r > 0; r--) v.Add(Exact(x0 + CellM, y0 + r * d));
            for (int q = Detail - 1; q > 0; q--) v.Add(Exact(x0 + q * d, y0));
            return v.ToArray();
        }

        static void DetailCell(ChunkData c, Builder b, int i, int j)
        {
            double x0 = c.X0 + j * CellM, y0 = c.Y0 + i * CellM, d = CellM / Detail;
            var idx = new int[(Detail + 1) * (Detail + 1)]; var fine = c.Fine[j * N + i];
            for (int q = 0; q < idx.Length; q++) idx[q] = Add(b, fine[q]);
            for (int q = 0; q < Detail; q++) for (int r = 0; r < Detail; r++)
            {
                var centre = new IslandPoint((y0 + (r + .5) * d) / 1000, (x0 + (q + .5) * d) / 1000);
                if (PortalHole(centre)) continue;
                int a0 = idx[q * (Detail + 1) + r], a1 = a0 + 1, a2 = a0 + Detail + 1, a3 = a2 + 1;
                b.T.Add(a0); b.T.Add(a2); b.T.Add(a3); b.T.Add(a0); b.T.Add(a3); b.T.Add(a1);
            }
        }

        /// <summary>A 25 m cell beside a detailed one: a fan from its centre to every 5 m point on its edges, so
        /// it shares each vertex the detailed neighbour has (no T-junction, no crack).</summary>
        static void StitchCell(ChunkData c, Builder b, int i, int j, int sw, int se, int nw, int ne)
        {
            var v = c.Stitch[j * N + i]; int at = 1;
            int centre = Add(b, v[0]);
            // Perimeter clockwise seen from above: south edge (W->E), east edge (S->N), north (E->W), west (N->S).
            var ring = new List<int> { sw };
            for (int r = 1; r < Detail; r++) ring.Add(Add(b, v[at++])); ring.Add(se);
            for (int q = 1; q < Detail; q++) ring.Add(Add(b, v[at++])); ring.Add(ne);
            for (int r = Detail - 1; r > 0; r--) ring.Add(Add(b, v[at++])); ring.Add(nw);
            for (int q = Detail - 1; q > 0; q--) ring.Add(Add(b, v[at++]));
            for (int t = 0; t < ring.Count; t++) { b.T.Add(centre); b.T.Add(ring[(t + 1) % ring.Count]); b.T.Add(ring[t]); }
        }

        static void Skirt(ChunkData c, Builder b, int[] index, int m, int s, float depth)
        {
            void Edge(Func<int, int> at, bool _)
            {
                for (int t = 0; t < m - 1; t++)
                {
                    int top0 = at(t), top1 = at(t + 1);
                    if (top0 < 0 || top1 < 0) continue;
                    int bot0 = b.Copy(top0, Vector3.down * depth), bot1 = b.Copy(top1, Vector3.down * depth);
                    // BOTH faces. A T-junction gap between a 5 m fine cell and a coarser neighbour can be seen from
                    // either side of the seam; one-sided, the skirt was culled from the far side and the gap showed as
                    // a dark dash along the chunk edge near every crevasse field. The copied vertices carry the
                    // surface's own colour and normal, so either face shades like the ground it hangs from.
                    b.T.Add(top0); b.T.Add(bot1); b.T.Add(bot0); b.T.Add(top0); b.T.Add(top1); b.T.Add(bot1);
                    b.T.Add(top0); b.T.Add(bot0); b.T.Add(bot1); b.T.Add(top0); b.T.Add(bot1); b.T.Add(top1);
                }
            }
            Edge(t => index[t], false);                    // south edge
            Edge(t => index[(m - 1) * m + t], true);       // north edge
            Edge(t => index[t * m], true);                 // west edge
            Edge(t => index[t * m + m - 1], false);        // east edge
        }
    }
}

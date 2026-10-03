using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Combat;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Builds the seaside (FlyingGame.Core.Seaside): the ocean, the Golden Gate across the gorge's mouth, the waterfront
    /// city, the combat zone's puffy clouds, and the Catalina-like island — Airport in the Sky, the sea arch, Avalon (the
    /// Casino, the town climbing the hill, the green pier, boats on their moorings), and the sea cave with its rock roof,
    /// skylight shaft, sandy beach and pirate wreck. Static scenery is batched (vertex colour, FlyingGame/LitVC) so each
    /// landmark is a handful of draw calls.
    /// </summary>
    public static class SeasideBuilder
    {
        private static Vector3 U(double x, double y, double up) => WorldBuilder.U(x, y, up);

        /// <summary>Vertex-coloured triangle batch → one or more meshes.</summary>
        private sealed class Batch
        {
            private readonly List<Vector3> _v = new(); private readonly List<Vector3> _n = new(); private readonly List<Color> _c = new(); private readonly List<int> _t = new();
            public int Count => _v.Count;

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
            {
                Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                int o = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int i = 0; i < 4; i++) { _n.Add(n); _c.Add(col); }
                _t.Add(o); _t.Add(o + 1); _t.Add(o + 2); _t.Add(o); _t.Add(o + 2); _t.Add(o + 3);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int o = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c);
                for (int i = 0; i < 3; i++) { _n.Add(n); _c.Add(col); }
                _t.Add(o); _t.Add(o + 1); _t.Add(o + 2);
            }

            /// <summary>A box: centre, size (Unity axes before rotation), rotation.</summary>
            public void Box(Vector3 centre, Vector3 size, Quaternion rot, Color col, Color? top = null)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float sx, float sy, float sz) => centre + rot * new Vector3(sx * h.x, sy * h.y, sz * h.z);
                Vector3 p000 = P(-1, -1, -1), p100 = P(1, -1, -1), p010 = P(-1, 1, -1), p110 = P(1, 1, -1);
                Vector3 p001 = P(-1, -1, 1), p101 = P(1, -1, 1), p011 = P(-1, 1, 1), p111 = P(1, 1, 1);
                Quad(p010, p011, p111, p110, top ?? col);   // top
                Quad(p000, p100, p101, p001, col * 0.7f);   // bottom
                Quad(p000, p010, p110, p100, col);          // -z
                Quad(p101, p111, p011, p001, col);          // +z
                Quad(p001, p011, p010, p000, col);          // -x
                Quad(p100, p110, p111, p101, col);          // +x
            }

            public void Box(Vector3 centre, Vector3 size, Color col, Color? top = null) => Box(centre, size, Quaternion.identity, col, top);

            /// <summary>A beam between two points with a square-ish cross-section.</summary>
            public void Beam(Vector3 a, Vector3 b, float w, float h, Color col)
            {
                Vector3 d = b - a; float len = d.magnitude; if (len < 1e-3f) return;
                Quaternion rot = Quaternion.LookRotation(d / len, Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up);
                Box((a + b) * 0.5f, new Vector3(w, h, len), rot, col);
            }

            /// <summary>Upright cylinder (n sides) from <paramref name="baseC"/>, optional cone roof.</summary>
            public void Cylinder(Vector3 baseC, float r, float hgt, int n, Color wall, Color? roof = null, float roofH = 0)
            {
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                    Vector3 d0 = new(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), d1 = new(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                    Quad(baseC + d0, baseC + d0 + Vector3.up * hgt, baseC + d1 + Vector3.up * hgt, baseC + d1, wall);
                    Vector3 top = baseC + Vector3.up * hgt;
                    if (roof.HasValue) Tri(top + d0 * 1.04f, top + Vector3.up * roofH, top + d1 * 1.04f, roof.Value);
                    else Tri(top + d0, top, top + d1, wall);
                }
            }

            public GameObject Build(string name, Transform parent, Material m)
            {
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetColors(_c); mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go;
            }
        }

        private static Material _vc;
        private static Material VC => _vc ??= WorldBuilder.Mat("FlyingGame/LitVC", Color.white);

        public static readonly Color Orange = new(0.75f, 0.22f, 0.16f);   // International Orange
        private static readonly Color Concrete = new(0.68f, 0.67f, 0.64f), Asphalt = new(0.2f, 0.2f, 0.22f), Rock = new(0.52f, 0.45f, 0.37f),
                                      CaveRock = new(0.2f, 0.18f, 0.16f), Stucco = new(0.94f, 0.92f, 0.86f), Tile = new(0.72f, 0.27f, 0.18f),
                                      Wood = new(0.3f, 0.19f, 0.11f), PierGreen = new(0.2f, 0.45f, 0.3f);

        public static void Build(WorldTerrain t, Transform parent)
        {
            var root = new GameObject("Seaside"); root.transform.SetParent(parent, false);
            BuildOcean(root.transform);
            BuildGoldenGate(t, root.transform);
            BuildCity(t, root.transform);
            BuildIslandRunway(t, root.transform);
            BuildArch(root.transform);
            BuildAvalon(t, root.transform);
            BuildCave(t, root.transform);
            foreach (var c in CombatZone.Clouds)
                Cumulus.Puffy(U(c.x, c.y, c.baseM), (float)c.radiusM, (float)c.heightM, (int)(c.x * 7 + c.y));
        }

        // ---- the ocean ----------------------------------------------------------------------------------------
        private static void BuildOcean(Transform parent)
        {
            // One big plane from just under the cliffs out to the horizon; land above it hides it.
            double y0 = Coast.MeanShoreY - 700, y1 = Coast.MeanShoreY + 45000, x0 = -45000, x1 = 45000;
            var v = new[] { U(x0, y0, Coast.SeaLevelM), U(x1, y0, Coast.SeaLevelM), U(x1, y1, Coast.SeaLevelM), U(x0, y1, Coast.SeaLevelM) };
            var mesh = new Mesh { name = "Ocean" };
            mesh.vertices = v; mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals(); mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200000f);
            var go = new GameObject("Ocean"); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = WorldBuilder.Mat("FlyingGame/Water", new Color(0.05f, 0.22f, 0.4f, 0.95f));
        }

        // ---- the Golden Gate ----------------------------------------------------------------------------------
        private static void BuildGoldenGate(WorldTerrain t, Transform parent)
        {
            var b = new Batch();
            double y = GoldenGate.Y, cx = GoldenGate.CentreX, half = GoldenGate.HalfMainSpanM, back = GoldenGate.AnchorBackM;
            double deck = GoldenGate.DeckTopM, top = GoldenGate.TowerTopM;
            float dw = (float)GoldenGate.DeckHalfWidthM, leg = (float)GoldenGate.TowerLegM;
            // Deck: orange stiffening truss under a grey roadway, anchorage to anchorage.
            double x0 = cx - half - back, x1 = cx + half + back;
            b.Box(U((x0 + x1) / 2, y, deck - 4.5), new Vector3(2 * dw, 7f, (float)(x1 - x0)), Orange);
            b.Box(U((x0 + x1) / 2, y, deck - 0.4), new Vector3(2 * dw - 2f, 0.8f, (float)(x1 - x0)), Asphalt);
            // Truss diagonals on both faces (reads at a distance as the deep Warren truss).
            for (double x = x0; x < x1 - 15; x += 15)
                foreach (float side in new[] { -1f, 1f })
                    b.Beam(U(x, y + side * dw, deck - 8), U(x + 15, y + side * dw, deck - 1), 0.7f, 0.7f, Orange * 0.85f);
            // Towers: two tapering legs with stepped tiers, portal struts, Art-Deco cap.
            foreach (double s in new[] { -1.0, 1.0 })
            {
                double tx = cx + s * half;
                foreach (float side in new[] { -1f, 1f })
                {
                    double ly = y + side * (dw + leg / 2);
                    double[] tiers = { -6, deck + 20, deck + 70, deck + 115, top };
                    for (int k = 0; k < tiers.Length - 1; k++)
                    {
                        float w = leg * (1.3f - 0.1f * k);
                        b.Box(U(tx, ly, (tiers[k] + tiers[k + 1]) / 2), new Vector3(w, (float)(tiers[k + 1] - tiers[k]), w * 1.4f), Orange);
                    }
                }
                foreach (double z in new[] { deck - 25, deck + 50, deck + 100, top - 8 })
                    b.Box(U(tx, y, z + 3.5), new Vector3(2 * dw + 2 * leg, 7f, leg), Orange);
                b.Box(U(tx, y, -3), new Vector3(2 * dw + 3 * leg, 10f, leg * 3f), Concrete);   // pier footing
            }
            // Main cables + suspenders.
            foreach (float side in new[] { -1f, 1f })
            {
                double cy = y + side * (dw + leg / 2);
                for (double x = x0; x < x1; x += 10)
                    b.Beam(U(x, cy, GoldenGate.CableAt(x)), U(x + 10, cy, GoldenGate.CableAt(x + 10)), 1.4f, 1.4f, Orange);
                for (double x = cx - half + 15; x < cx + half; x += 15)
                    b.Box(U(x, cy, (GoldenGate.CableAt(x) + deck) / 2), new Vector3(0.35f, (float)(GoldenGate.CableAt(x) - deck), 0.35f), Orange);
            }
            // Anchorage blocks on the tableland.
            foreach (double s in new[] { -1.0, 1.0 })
            {
                double ax = cx + s * (half + back);
                b.Box(U(ax, y, t.HeightAt(ax, y) + 10), new Vector3(2 * dw + 14, 22f, 40f), Concrete);
            }
            b.Build("GoldenGate", parent, VC);
        }

        // ---- the waterfront city --------------------------------------------------------------------------------
        private static void BuildCity(WorldTerrain t, Transform parent)
        {
            var b = new Batch();
            double g = t.HeightAt(SeaCity.CentreX, SeaCity.CentreY);
            // Paved ground and the street grid.
            b.Box(U(0.5 * (SeaCity.X0 + SeaCity.X1), 0.5 * (SeaCity.Y0 + SeaCity.Y1), g + 0.05), new Vector3((float)(SeaCity.Y1 - SeaCity.Y0), 0.1f, (float)(SeaCity.X1 - SeaCity.X0)), Concrete * 0.85f);
            Color[] walls = { new(0.78f, 0.75f, 0.7f), new(0.55f, 0.6f, 0.68f), new(0.7f, 0.52f, 0.44f), new(0.86f, 0.86f, 0.88f) };
            var glass = new Color(0.42f, 0.6f, 0.78f);
            foreach (Landmarks.Building bd in SeaCity.Buildings())
            {
                double gg = t.HeightAt(bd.Cx, bd.Cy);
                bool tower = bd.HeightM > 140;
                Color c = tower ? (bd.Style % 2 == 0 ? glass : walls[bd.Style]) : walls[System.Math.Min(bd.Style, 3)];
                b.Box(U(bd.Cx, bd.Cy, gg + bd.HeightM / 2), new Vector3((float)(bd.Hy * 2), (float)bd.HeightM, (float)(bd.Hx * 2)), c, c * 0.8f);
                if (tower)
                {
                    // Setback crown + a mast: the skyline reads as a skyline.
                    b.Box(U(bd.Cx, bd.Cy, gg + bd.HeightM + 8), new Vector3((float)bd.Hy * 1.2f, 16f, (float)bd.Hx * 1.2f), c * 0.85f);
                    if (bd.Style == 0) b.Box(U(bd.Cx, bd.Cy, gg + bd.HeightM + 30), new Vector3(1.2f, 28f, 1.2f), new Color(0.8f, 0.8f, 0.82f));
                }
            }
            b.Build("SeaCity", parent, VC);
        }

        // ---- island: Airport in the Sky --------------------------------------------------------------------------
        private static void BuildIslandRunway(WorldTerrain t, Transform parent)
        {
            var b = new Batch();
            double x = Island.RunwayX, y = Island.RunwayY, e = Island.RunwayElevM;
            b.Box(U(x, y, e + 0.06), new Vector3((float)Island.RunwayWidthM, 0.12f, (float)Island.RunwayLengthM), Asphalt);
            for (double d = -Island.RunwayLengthM / 2 + 60; d < Island.RunwayLengthM / 2 - 60; d += 40)
                b.Box(U(x + d, y, e + 0.13), new Vector3(0.9f, 0.03f, 18f), Color.white);
            foreach (double s in new[] { -1.0, 1.0 })
                for (int k = -3; k <= 3; k++)
                    b.Box(U(x + s * (Island.RunwayLengthM / 2 - 12), y + k * 3.8, e + 0.13), new Vector3(1.8f, 0.03f, 20f), Color.white);
            // The little Spanish-style terminal + a hangar beside the runway.
            double ty = y + Island.RunwayWidthM / 2 + 40, tx = x - 120;
            double tg = t.HeightAt(tx, ty);
            b.Box(U(tx, ty, tg + 4), new Vector3(16f, 8f, 30f), Stucco, Tile);
            b.Box(U(tx, ty, tg + 9), new Vector3(17f, 2f, 31f), Tile);
            b.Box(U(tx + 70, ty + 5, t.HeightAt(tx + 70, ty + 5) + 6), new Vector3(24f, 12f, 30f), new Color(0.75f, 0.76f, 0.78f));
            b.Build("IslandAirport", parent, VC);
        }

        // ---- island: the sea arch ---------------------------------------------------------------------------------
        private static void BuildArch(Transform parent)
        {
            var b = new Batch();
            double step = 6, cy = SeaArch.Cy, hw = SeaArch.HalfWidthYM;
            for (double x = SeaArch.X0 - 20; x < SeaArch.X1 + 20; x += step)
            {
                double xa = x, xb = x + step;
                double ia = System.Math.Max(-8, SeaArch.InnerAt(xa)), ib = System.Math.Max(-8, SeaArch.InnerAt(xb));
                double ua = (xa - SeaArch.Cx) / SeaArch.HalfSpan, ub = (xb - SeaArch.Cx) / SeaArch.HalfSpan;
                double oa = ia + SeaArch.ThickM + 20 * ua * ua + 4 * System.Math.Sin(xa / 13), ob = ib + SeaArch.ThickM + 20 * ub * ub + 4 * System.Math.Sin(xb / 13);
                double wa = hw * (1 + 0.5 * ua * ua) + 3 * System.Math.Sin(xa / 9), wb = hw * (1 + 0.5 * ub * ub) + 3 * System.Math.Sin(xb / 9);
                Color c = Rock * (0.92f + 0.08f * Mathf.Sin((float)x / 17f));
                b.Quad(U(xa, cy - wa, ia), U(xb, cy - wb, ib), U(xb, cy + wb, ib), U(xa, cy + wa, ia), c * 0.75f);   // underside
                b.Quad(U(xa, cy - wa, oa), U(xa, cy + wa, oa), U(xb, cy + wb, ob), U(xb, cy - wb, ob), c * 1.05f);   // top
                b.Quad(U(xa, cy - wa, ia), U(xa, cy - wa, oa), U(xb, cy - wb, ob), U(xb, cy - wb, ib), c);          // west face
                b.Quad(U(xa, cy + wa, ia), U(xb, cy + wb, ib), U(xb, cy + wb, ob), U(xa, cy + wa, oa), c);          // east face
            }
            b.Build("SeaArch", parent, VC);
        }

        // ---- island: Avalon -------------------------------------------------------------------------------------------
        private static void BuildAvalon(WorldTerrain t, Transform parent)
        {
            var b = new Batch();
            double ax = Island.AvalonX, ay = Island.AvalonY, R = Island.AvalonCoveR;
            // The Casino: round, white, red-tile roof, on the bay's north point.
            var (kx, ky) = Island.CasinoXY;
            double kg = System.Math.Max(1.0, t.HeightAt(kx, ky));
            b.Cylinder(U(kx, ky, kg - 2), (float)Island.CasinoRadiusM, (float)(Island.CasinoHeightM - 10), 28, Stucco, Tile, 14f);
            b.Cylinder(U(kx, ky, kg - 2 + Island.CasinoHeightM - 10), (float)Island.CasinoRadiusM * 0.98f, 1.5f, 28, Tile);
            // Town: whitewashed houses with red-tile roofs climbing the slope behind the bay, facing the water.
            var rng = new System.Random(1931);
            for (int i = 0; i < 260; i++)
            {
                double a = (rng.NextDouble() - 0.5) * 2.2;                     // fan landward (+y side of the cove)
                double r = R + 30 + rng.NextDouble() * 360;
                double hx = ax + System.Math.Sin(a) * r, hy = ay + System.Math.Cos(a) * r;
                if (hx < ax - R * 0.6) continue;
                double hg = t.HeightAt(hx, hy);
                if (hg < 2 || hg > 110) continue;
                float w = 7 + (float)rng.NextDouble() * 7, l = 8 + (float)rng.NextDouble() * 8, hh = 5 + (float)rng.NextDouble() * 5;
                float yaw = Mathf.Atan2((float)(ax - hx), (float)(ay - hy)) * Mathf.Rad2Deg;
                Color wall = rng.NextDouble() < 0.7 ? Stucco : new Color(0.95f, 0.84f, 0.7f);
                Quaternion q = Quaternion.Euler(0, yaw, 0);
                b.Box(U(hx, hy, hg + hh / 2 - 1), new Vector3(w, hh + 2, l), q, wall, Tile);
                b.Box(U(hx, hy, hg + hh + 1), new Vector3(w * 1.08f, 1.6f, l * 1.08f), q, Tile);
            }
            // The green Pleasure Pier, from mid-beach out into the bay.
            double px = ax, py0 = ay + R * 0.97, py1 = ay + R * 0.55;
            b.Box(U(px, (py0 + py1) / 2, 3), new Vector3((float)(py0 - py1), 1f, 9f), Wood);
            for (double py = py1; py < py0; py += 12) b.Box(U(px, py, -2), new Vector3(0.8f, 9f, 0.8f), Wood);
            b.Box(U(px, py1 + 8, 6), new Vector3(10f, 6f, 12f), PierGreen, PierGreen * 0.8f);
            // Boats on their moorings, in rows, all head to wind.
            for (int i = 0; i < 70; i++)
            {
                double bx = ax + (rng.NextDouble() - 0.5) * R * 1.5, by = ay + (rng.NextDouble() - 0.6) * R * 1.3;
                if ((bx - ax) * (bx - ax) + (by - ay) * (by - ay) > R * R * 0.75) continue;
                float len = 8 + (float)rng.NextDouble() * 8;
                Quaternion q = Quaternion.Euler(0, 75 + (float)rng.NextDouble() * 10, 0);
                b.Box(U(bx, by, 0.6), new Vector3(len * 0.32f, 1.6f, len), q, Color.white, new Color(0.85f, 0.8f, 0.7f));
                if (rng.NextDouble() < 0.5) b.Box(U(bx, by, 6), new Vector3(0.2f, 10f, 0.2f), q, new Color(0.85f, 0.85f, 0.85f));
            }
            b.Build("Avalon", parent, VC);
        }

        // ---- island: the sea cave ---------------------------------------------------------------------------------
        private static void BuildCave(WorldTerrain t, Transform parent)
        {
            var b = new Batch();
            double cx = SeaCave.Cx, cy = SeaCave.Cy, R = SeaCave.RadiusM;
            const double step = 3;
            int n = (int)(2 * R / step) + 1;
            var roof = new bool[n, n]; var ceil = new double[n, n]; var outer = new double[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
            {
                double x = cx - R + i * step, y = cy - R + j * step;
                roof[i, j] = SeaCave.RoofAt(x, y, out ceil[i, j], out outer[i, j]);
            }
            Color grass = new(0.5f, 0.52f, 0.33f);
            Vector3 P(int i, int j, double h) => U(cx - R + i * step, cy - R + j * step, h);
            for (int i = 0; i < n - 1; i++) for (int j = 0; j < n - 1; j++)
            {
                if (!(roof[i, j] && roof[i + 1, j] && roof[i, j + 1] && roof[i + 1, j + 1])) continue;
                // Outer hillside (facing up) and the dome's inside (facing down, dark rock).
                b.Quad(P(i, j, outer[i, j]), P(i, j + 1, outer[i, j + 1]), P(i + 1, j + 1, outer[i + 1, j + 1]), P(i + 1, j, outer[i + 1, j]), grass);
                Color rk = CaveRock * (0.85f + 0.3f * Mathf.PerlinNoise(i * 0.31f, j * 0.27f));
                b.Quad(P(i, j, ceil[i, j]), P(i + 1, j, ceil[i + 1, j]), P(i + 1, j + 1, ceil[i + 1, j + 1]), P(i, j + 1, ceil[i, j + 1]), rk);
                // Close the shell where it ends (the mouth, the skylight, the rim): rock walls from ceiling to hillside.
                void Wall(int ia, int ja, int ib, int jb) => b.Quad(P(ia, ja, ceil[ia, ja]), P(ia, ja, outer[ia, ja]), P(ib, jb, outer[ib, jb]), P(ib, jb, ceil[ib, jb]), Rock * 0.8f);
                if (i == 0 || !roof[i - 1, j] || !roof[i - 1, j + 1]) Wall(i, j, i, j + 1);
                if (i + 2 >= n || !roof[i + 2, j] || !roof[i + 2, j + 1]) Wall(i + 1, j, i + 1, j + 1);
                if (j == 0 || !roof[i, j - 1] || !roof[i + 1, j - 1]) Wall(i, j, i + 1, j);
                if (j + 2 >= n || !roof[i, j + 2] || !roof[i + 1, j + 2]) Wall(i, j + 1, i + 1, j + 1);
            }
            // The pirate wreck: keeled over on the sand, bow to the water, a broken mast, a rag of a sail, a chest.
            var (wx, wy, wh) = SeaCave.Wreck;
            double wg = t.HeightAt(wx, wy);
            Quaternion hull = Quaternion.Euler(0, (float)wh, 22f);
            Vector3 c0 = U(wx, wy, wg + 2.2);
            b.Box(c0, new Vector3(6.5f, 3.6f, 22f), hull, Wood, Wood * 0.8f);                                 // hull
            b.Box(c0 + hull * new Vector3(0, 0.4f, 11.5f), new Vector3(3.5f, 3f, 4f), hull * Quaternion.Euler(0, 0, 0), Wood * 0.9f);   // bow
            b.Box(c0 + hull * new Vector3(0, 2.8f, -8.5f), new Vector3(6f, 2.6f, 5f), hull, Wood * 1.1f);       // stern castle
            b.Box(c0 + hull * new Vector3(0, 1.9f, 0), new Vector3(5.6f, 0.3f, 18f), hull, Wood * 1.25f);       // deck
            for (int k = -3; k <= 3; k++) b.Box(c0 + hull * new Vector3(3.3f, 0.2f, k * 2.6f), new Vector3(0.4f, 3.4f, 0.5f), hull, Wood * 0.6f);   // stove-in ribs
            Quaternion mast = hull * Quaternion.Euler(-12f, 0, 8f);
            b.Box(c0 + hull * new Vector3(0, 7f, 2f), new Vector3(0.6f, 11f, 0.6f), mast, Wood * 0.9f);        // broken mainmast
            b.Box(c0 + hull * new Vector3(0, 5f, -4f), new Vector3(0.5f, 6f, 0.5f), hull, Wood * 0.9f);        // mizzen stump
            b.Quad(c0 + hull * new Vector3(0, 11f, 2.2f), c0 + hull * new Vector3(0, 11f, 6f), c0 + hull * new Vector3(0.5f, 6.5f, 5f), c0 + hull * new Vector3(0, 7f, 2.2f), new Color(0.82f, 0.77f, 0.62f));   // torn sail
            b.Box(c0 + hull * new Vector3(0, 12.2f, 2f), new Vector3(0.05f, 1.2f, 1.8f), mast, new Color(0.08f, 0.08f, 0.08f));   // the black flag
            b.Box(U(wx + 6, wy - 4, wg + 0.5), new Vector3(1.2f, 0.8f, 0.8f), Quaternion.Euler(0, 30, 0), new Color(0.55f, 0.35f, 0.15f), new Color(0.85f, 0.66f, 0.2f));   // the chest
            b.Build("SeaCave", parent, VC);

            // The skylight's shaft of sun down onto the sand.
            var (sx, sy) = SeaCave.Skylight;
            double topH = Island.HeightAt(sx, sy, false) ?? 60, botH = t.HeightAt(sx, sy);
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(shaft.GetComponent<Collider>());
            shaft.name = "SkylightShaft"; shaft.transform.SetParent(parent, false);
            shaft.transform.position = U(sx, sy, (topH + botH) / 2);
            shaft.transform.localScale = new Vector3((float)SeaCave.SkylightRadiusM * 1.6f, (float)(topH - botH) / 2f, (float)SeaCave.SkylightRadiusM * 1.6f);
            var sm = new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Transparent")) { color = new Color(1f, 0.95f, 0.75f, 0.16f) };
            shaft.GetComponent<MeshRenderer>().sharedMaterial = sm;
        }
    }
}

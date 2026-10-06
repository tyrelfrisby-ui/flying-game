using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Builds the "skate park for airplanes" (FlyingGame.Core.Playground): the canyon lake (water, banded sandstone
    /// spires and buttes, natural arches, the concrete arch dam) and the city built for flying (towers on wide avenues,
    /// sky-bridges, the fountain plaza's crossing water cannons, the 500 ft cantilevered pad, the numbered rooftop rings,
    /// the giant spinning ring) and the Mall (reflecting pond, mown grass, the Capitol, the Washington Monument).
    /// </summary>
    public static class PlaygroundBuilder
    {
        private static Vector3 U(double x, double y, double up) => WorldBuilder.U(x, y, up);
        private static readonly Color Sand1 = new(0.78f, 0.43f, 0.27f), Sand2 = new(0.86f, 0.58f, 0.38f), Sand3 = new(0.66f, 0.34f, 0.22f),
                                      Concrete = new(0.72f, 0.71f, 0.68f), Marble = new(0.94f, 0.93f, 0.9f), Glass = new(0.42f, 0.6f, 0.78f);

        public static void Build(WorldTerrain t, Transform parent)
        {
            var root = new GameObject("Playground"); root.transform.SetParent(parent, false);
            BuildLake(t, root.transform);
            BuildCity(t, root.transform);
            BuildMall(t, root.transform);
            root.AddComponent<PlaygroundRuntime>();
        }

        // ---- the canyon lake ------------------------------------------------------------------------------------------
        private static void BuildLake(WorldTerrain t, Transform parent)
        {
            // Water: a strip down the canyon at the lake level, stopping at the dam's upstream face.
            var v = new List<Vector3>(); var tr = new List<int>();
            for (double y = CanyonLake.Y0; y <= CanyonLake.DamY; y += 15)
            {
                double cx = CanyonLake.CentreX(y), W = CanyonLake.HalfWidthAt(y) - 1;
                double yl = System.Math.Min(y, CanyonLake.DamCentreY(cx - W) - 4), yr = System.Math.Min(y, CanyonLake.DamCentreY(cx + W) - 4);
                v.Add(U(cx - W, yl, CanyonLake.SurfaceM)); v.Add(U(cx + W, yr, CanyonLake.SurfaceM));
                if (v.Count >= 4) { int b0 = v.Count - 4; tr.AddRange(new[] { b0, b0 + 2, b0 + 1, b0 + 1, b0 + 2, b0 + 3, b0, b0 + 1, b0 + 2, b0 + 1, b0 + 3, b0 + 2 }); }
            }
            var wm = new Mesh { name = "CanyonLake", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            wm.SetVertices(v); wm.SetTriangles(tr, 0); wm.RecalculateNormals(); wm.RecalculateBounds();
            var wgo = new GameObject("CanyonLakeWater"); wgo.transform.SetParent(parent, false);
            wgo.AddComponent<MeshFilter>().sharedMesh = wm;
            wgo.AddComponent<MeshRenderer>().sharedMaterial = WorldBuilder.Mat("FlyingGame/Water", new Color(0.06f, 0.3f, 0.42f, 0.93f));

            var b = new SeasideBuilder.Batch();
            int seed = 0;
            foreach (Spire s in CanyonLake.Spires) Spire(b, s, seed++);
            foreach (Spire s in CanyonLake.ArchPillars()) Spire(b, s, seed++);
            foreach (NaturalArch a in CanyonLake.Arches) Arch(b, a);
            Dam(b);
            b.Build("CanyonLakeRock", parent, SeasideBuilder.VC);
        }

        /// <summary>A sandstone spire: stacked rough frustums in Navajo-sandstone bands, a flat cap.</summary>
        private static void Spire(SeasideBuilder.Batch b, Spire s, int seed)
        {
            const int sides = 14;
            var rng = new System.Random(4000 + seed);
            double z0 = s.BaseM, step = 7;
            float[] prev = null; double prevZ = z0;
            for (double z = z0; z <= s.TopM + 0.01; z += step)
            {
                double zz = System.Math.Min(z, s.TopM);
                var ring = new float[sides];
                double R = s.RadiusAt(zz);
                for (int k = 0; k < sides; k++) ring[k] = (float)(R * (0.88 + 0.24 * rng.NextDouble()));
                if (prev != null)
                {
                    int band = (int)(zz / 9) % 3;
                    Color c = band == 0 ? Sand1 : band == 1 ? Sand2 : Sand3;
                    for (int k = 0; k < sides; k++)
                    {
                        double a0 = k * System.Math.PI * 2 / sides, a1 = (k + 1) * System.Math.PI * 2 / sides;
                        Vector3 P(float r, double a, double h) => U(s.X + r * System.Math.Cos(a), s.Y + r * System.Math.Sin(a), h);
                        b.Quad(P(prev[k], a0, prevZ), P(ring[k], a0, zz), P(ring[(k + 1) % sides], a1, zz), P(prev[(k + 1) % sides], a1, prevZ), c);
                    }
                }
                prev = ring; prevZ = zz;
            }
            // Cap.
            for (int k = 0; k < sides; k++)
            {
                double a0 = k * System.Math.PI * 2 / sides, a1 = (k + 1) * System.Math.PI * 2 / sides;
                b.Tri(U(s.X + prev[k] * System.Math.Cos(a0), s.Y + prev[k] * System.Math.Sin(a0), s.TopM), U(s.X, s.Y, s.TopM + 1.5),
                      U(s.X + prev[(k + 1) % sides] * System.Math.Cos(a1), s.Y + prev[(k + 1) % sides] * System.Math.Sin(a1), s.TopM), Sand2 * 1.05f);
            }
        }

        private static void Arch(SeasideBuilder.Batch b, NaturalArch a)
        {
            double L = 2 * a.HalfSpan, ux = (a.Bx - a.Ax) / L, uy = (a.By - a.Ay) / L, vx = -uy, vy = ux;
            double mx = 0.5 * (a.Ax + a.Bx), my = 0.5 * (a.Ay + a.By), hw = a.WidthM / 2;
            Vector3 P(double s, double c, double up) => U(mx + s * ux + c * vx, my + s * uy + c * vy, up);
            const double step = 4;
            for (double s = -a.HalfSpan; s < a.HalfSpan; s += step)
            {
                double s1 = System.Math.Min(a.HalfSpan, s + step);
                double i0 = a.InnerAt(s), i1 = a.InnerAt(s1), o0 = a.OuterAt(s), o1 = a.OuterAt(s1);
                Color c = ((int)((s + 1000) / 12)) % 2 == 0 ? Sand1 : Sand2;
                b.Quad(P(s, -hw, i0), P(s1, -hw, i1), P(s1, hw, i1), P(s, hw, i0), c * 0.8f);
                b.Quad(P(s, -hw, o0), P(s, hw, o0), P(s1, hw, o1), P(s1, -hw, o1), c * 1.05f);
                b.Quad(P(s, -hw, i0), P(s, -hw, o0), P(s1, -hw, o1), P(s1, -hw, i1), c);
                b.Quad(P(s, hw, i0), P(s1, hw, i1), P(s1, hw, o1), P(s, hw, o0), c);
            }
        }

        private static void Dam(SeasideBuilder.Batch b)
        {
            double cx = WorldTerrain.RiverCentreX(CanyonLake.DamY), L = CanyonLake.DamHalfLength + 12;
            const double step = 6, baseM = -2;
            for (double x = cx - L; x < cx + L; x += step)
            {
                double x1 = x + step, y0 = CanyonLake.DamCentreY(x), y1 = CanyonLake.DamCentreY(x1);
                double tc = CanyonLake.DamCrestThickM / 2, tb = CanyonLake.DamBaseThickM / 2, top = CanyonLake.DamCrestM;
                b.Quad(U(x, y0 - tc, top), U(x1, y1 - tc, top), U(x1, y1 + tc, top), U(x, y0 + tc, top), Concrete * 1.05f);            // crest road
                b.Quad(U(x, y0 - tc, top), U(x, y0 - tb, baseM), U(x1, y1 - tb, baseM), U(x1, y1 - tc, top), Concrete * 0.9f);        // upstream face
                b.Quad(U(x, y0 + tc, top), U(x1, y1 + tc, top), U(x1, y1 + tb, baseM), U(x, y0 + tb, baseM), Concrete);              // downstream face
            }
            // Crest railings and a spillway gate house.
            for (double x = cx - L; x < cx + L; x += 3)
                foreach (double side in new[] { -1.0, 1.0 })
                    b.Box(U(x, CanyonLake.DamCentreY(x) + side * (CanyonLake.DamCrestThickM / 2 - 0.3), CanyonLake.DamCrestM + 0.6), new Vector3(0.3f, 1.2f, 0.3f), Concrete * 0.8f);
            b.Box(U(cx - L * 0.6, CanyonLake.DamCentreY(cx - L * 0.6), CanyonLake.DamCrestM + 5), new Vector3(10f, 10f, 14f), Concrete * 0.95f);
        }

        // ---- the city built for flying ---------------------------------------------------------------------------------
        private static void BuildCity(WorldTerrain t, Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            BuildCityGrid(FlyCity.Grid, t, b, parent);
            BuildCityExtras(FlyCity.Grid, t, b, parent);
            b.Build("FlyCity", parent, SeasideBuilder.VC);


        }

        /// <summary>A city's extras (owner 2026-10-05: the Valley city gets "the same race course and spinning circle and
        /// landable platform on the buildings as the combat city has"): the cantilevered pad, the rooftop rings 1–5, the spinning
        /// ring's post into the batch; the spinning ring itself as its own object (turned every frame by PlaygroundRuntime).</summary>
        internal static void BuildCityExtras(CityGrid grid, WorldTerrain t, SeasideBuilder.Batch b, Transform parent)
        {
            if (grid.PadLot.HasValue)
            // The cantilevered pad: disc, red-and-white edge, white ring + centreline, the steel brackets to the tower.
            {
                var tw = grid.PadTower; double g = t.HeightAt(tw.Cx, tw.Cy), top = g + FlyCity.PadDeckM;
                var (px, py) = grid.PadCentre; float R = (float)FlyCity.PadRadiusM;
                const int n = 64;
                for (int k = 0; k < n; k++)
                {
                    double a0 = k * System.Math.PI * 2 / n, a1 = (k + 1) * System.Math.PI * 2 / n;
                    Vector3 e0 = U(px + R * System.Math.Cos(a0), py + R * System.Math.Sin(a0), top), e1 = U(px + R * System.Math.Cos(a1), py + R * System.Math.Sin(a1), top);
                    b.Tri(U(px, py, top), e0, e1, new Color(0.24f, 0.24f, 0.26f));
                    Color edge = k % 2 == 0 ? new Color(0.85f, 0.15f, 0.12f) : Color.white;
                    b.Quad(e0, e0 + Vector3.down * 4f, e1 + Vector3.down * 4f, e1, edge);
                    b.Tri(U(px, py, top - 4), e1 + Vector3.down * 4f, e0 + Vector3.down * 4f, new Color(0.35f, 0.35f, 0.37f));
                    // Painted ring 6 m in from the edge.
                    Vector3 r0 = U(px + (R - 6) * System.Math.Cos(a0), py + (R - 6) * System.Math.Sin(a0), top + 0.05), r1 = U(px + (R - 6) * System.Math.Cos(a1), py + (R - 6) * System.Math.Sin(a1), top + 0.05);
                    Vector3 q0 = U(px + (R - 8) * System.Math.Cos(a0), py + (R - 8) * System.Math.Sin(a0), top + 0.05), q1 = U(px + (R - 8) * System.Math.Cos(a1), py + (R - 8) * System.Math.Sin(a1), top + 0.05);
                    b.Quad(q0, r0, r1, q1, Color.white);
                }
                for (double d = -R + 14; d < R - 14; d += 18) b.Box(U(px + d, py, top + 0.06), new Vector3(1.2f, 0.04f, 9f), Color.white);
                // Brackets: raking struts from the tower face down to the pad's underside.
                // (along whichever way the pad hangs: d out from the tower, q across)
                double dx = px - tw.Cx, dy = py - tw.Cy, dl = System.Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
                double qx = -dy, qy = dx, face = System.Math.Abs(dx) > 0.5 ? tw.Hx : tw.Hy;
                foreach (double ox in new[] { -40.0, -14.0, 14.0, 40.0 })
                    b.Beam(U(tw.Cx + dx * face + qx * ox * 0.5, tw.Cy + dy * face + qy * ox * 0.5, top - 70),
                           U(px + dx * 30 + qx * ox, py + dy * 30 + qy * ox, top - 4), 2.5f, 2.5f, new Color(0.5f, 0.52f, 0.55f));
            }
            // Rooftop rings 1–5: red/white tori on two posts, big numerals.
            var rings = grid.RooftopRings();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int k = 0; k < rings.Count; k++)
            {
                var (tw, hdg) = rings[k]; double g = t.HeightAt(tw.Cx, tw.Cy), cz = g + tw.HeightM + FlyCity.RingAboveRoofM;
                double hr = hdg * System.Math.PI / 180;
                Vector3 normal = new((float)System.Math.Sin(hr), 0, (float)System.Math.Cos(hr));
                b.Torus(U(tw.Cx, tw.Cy, cz), normal, (float)FlyCity.RingRadiusM, (float)FlyCity.RingTubeM, 32, 8, i => i % 4 < 2 ? new Color(0.9f, 0.15f, 0.12f) : Color.white);
                Vector3 side = Vector3.Cross(normal, Vector3.up).normalized * (float)(FlyCity.RingRadiusM * 0.6);
                foreach (float sg in new[] { -1f, 1f })
                {
                    Vector3 top = U(tw.Cx, tw.Cy, cz) + side * sg + Vector3.down * (float)(FlyCity.RingRadiusM * 0.8);
                    b.Beam(new Vector3(top.x, (float)(g + tw.HeightM), top.z), top, 1.4f, 1.4f, new Color(0.4f, 0.4f, 0.42f));
                }
                // An opaque plate between the two faces (owner 2026-10-06: "the numbers are double sided … a 4 becomes a weird
                // looking house on two posts … a non transparent layer between them"): each side then reads correctly.
                b.Box(U(tw.Cx, tw.Cy, cz + FlyCity.RingRadiusM + 16), new Vector3((k + 1 >= 10 ? 11f : 7f), 7f, 2.0f), Quaternion.LookRotation(normal, Vector3.up), new Color(0.12f, 0.12f, 0.15f));
                foreach (float face in new[] { 1f, -1f })
                {
                    var lbl = new GameObject($"RoofRing{k + 1}"); lbl.transform.SetParent(parent, false);
                    lbl.transform.position = U(tw.Cx, tw.Cy, cz + FlyCity.RingRadiusM + 16) - normal * face * 1.5f;
                    lbl.transform.rotation = Quaternion.LookRotation(normal * face, Vector3.up);
                    var tm = lbl.AddComponent<TextMesh>(); tm.text = (k + 1).ToString(); tm.font = font; tm.fontSize = 96; tm.characterSize = 0.5f; tm.anchor = TextAnchor.MiddleCenter; tm.color = new Color(1f, 0.85f, 0.2f);
                    var mr = lbl.GetComponent<MeshRenderer>(); if (font != null) mr.sharedMaterial = WorldBuilder.DepthTestedText(font);
                }
            }
            // The spinning ring's bearing post (the ring itself spins: PlaygroundRuntime).
            {
                var (sx, sy, _) = grid.SpinRingCentre; double g = t.HeightAt(sx, sy);
                b.Box(U(sx, sy, g + grid.Tallest.HeightM + FlyCity.SpinPostM / 2), new Vector3(4f, (float)FlyCity.SpinPostM, 4f), new Color(0.3f, 0.3f, 0.32f));
            }
            // Spinning ring (its own object; rotated every frame).
            {
                var (sx, sy, cz) = grid.SpinRingCentre; double g = t.HeightAt(sx, sy);
                var rb = new SeasideBuilder.Batch();
                rb.Torus(Vector3.zero, Vector3.forward, (float)FlyCity.SpinRingRadiusM, (float)FlyCity.SpinRingTubeM, 48, 10, i => i % 2 == 0 ? new Color(1f, 0.8f, 0.1f) : new Color(0.1f, 0.1f, 0.1f));
                var ring = rb.Build("SpinRing", parent, SeasideBuilder.VC);
                ring.transform.position = U(sx, sy, g + cz);
                PlaygroundRuntime.SpinRings.Add(ring.transform);
            }
        }

        /// <summary>
        /// The city built for flying on any grid (owner 2026-10-05: the Valley's city "identical to the one in the combat zone"):
        /// avenue paving, the towers with floor bands, the sky-bridges, the fountain plaza with its two basins and the
        /// crossing water cannons. The pad, the rooftop rings and the spinning ring are the combat city's own.
        /// </summary>
        internal static void BuildCityGrid(CityGrid grid, WorldTerrain t, SeasideBuilder.Batch b, Transform parent)
        {
            Color[] walls = { new(0.78f, 0.75f, 0.7f), new(0.55f, 0.6f, 0.68f), new(0.7f, 0.52f, 0.44f), new(0.86f, 0.86f, 0.88f) };
            var (ex0, ey0, ex1, ey1) = grid.Extent(100);
            var (cgx, cgy) = grid.Centre;
            double g0 = t.HeightAt(cgx, cgy);
            // Plaza-paved city ground (the avenues).
            b.Box(U(0.5 * (ex0 + ex1), 0.5 * (ey0 + ey1), g0 + 0.04), new Vector3((float)(ey1 - ey0), 0.08f, (float)(ex1 - ex0)), new Color(0.6f, 0.6f, 0.58f));
            foreach (FlyCity.Tower tw in grid.Towers())
            {
                double g = t.HeightAt(tw.Cx, tw.Cy);
                Color c = tw.HeightM > 170 && tw.Style % 2 == 0 ? Glass : walls[System.Math.Min(tw.Style, 3)];
                b.Box(U(tw.Cx, tw.Cy, g + tw.HeightM / 2), new Vector3((float)(tw.Hy * 2), (float)tw.HeightM, (float)(tw.Hx * 2)), c, c * 0.8f);
                // Floor bands every 4 floors so height reads.
                for (double z = 14; z < tw.HeightM - 4; z += 16)
                    b.Box(U(tw.Cx, tw.Cy, g + z), new Vector3((float)(tw.Hy * 2) + 0.6f, 0.8f, (float)(tw.Hx * 2) + 0.6f), c * 0.7f);
            }
            foreach (var (a, n, h) in grid.SkyBridges())
            {
                double g = t.HeightAt(a.Cx, a.Cy);
                var (bx, by, hx, hy) = CityGrid.BridgeBox(a, n);
                b.Box(U(bx, by, g + h + 4), new Vector3((float)(2 * hy), 8f, (float)(2 * hx)), Glass * 0.9f, new Color(0.8f, 0.8f, 0.82f));
            }
            // The fountain plaza: paving, two basins.
            var (px0, py0, px1, py1) = grid.PlazaRect;
            b.Box(U(0.5 * (px0 + px1), 0.5 * (py0 + py1), g0 + 0.1), new Vector3((float)(py1 - py0 + 80), 0.12f, (float)(px1 - px0 + 80)), new Color(0.82f, 0.78f, 0.7f));
            foreach (var (from, to) in grid.Jets)
                foreach (var p in new[] { from, to })
                    b.Cylinder(U(p.x, p.y, g0), 16f, 1.2f, 24, Marble);
            // Fountain jets: flowing-water tubes along parabolas whose apexes cross ~110 m up, and mist where they land.
            var water = WorldBuilder.Mat("FlyingGame/Waterfall", new Color(0.82f, 0.92f, 1f, 0.8f));
            var mist = WorldBuilder.Mat("FlyingGame/Waterfall", new Color(1f, 1f, 1f, 0.45f));
            foreach (var (from, to) in grid.Jets)
            {
                var pts = new List<Vector3>();
                for (int i = 0; i <= 40; i++)
                {
                    double u = i / 40.0;
                    double h = g0 + 3 + 4 * CityGrid.FountainCrossM * u * (1 - u);
                    pts.Add(U(from.x + (to.x - from.x) * u, from.y + (to.y - from.y) * u, h));
                }
                var jb = new SeasideBuilder.Batch();
                jb.Tube(pts, 2.6f, 8, Color.white);
                var go = jb.Build("FountainJet", parent, water);
                SetJetUV(go);
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(m.GetComponent<Collider>());
                m.name = "FountainMist"; m.transform.SetParent(parent, false);
                m.transform.position = U(to.x, to.y, g0 + 6); m.transform.localScale = new Vector3(28f, 16f, 28f);
                m.GetComponent<MeshRenderer>().sharedMaterial = mist;
            }
        }

        /// <summary>The waterfall shader streams along uv.y: give the jet tube a uv running nozzle → basin.</summary>
        private static void SetJetUV(GameObject go)
        {
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var v = mesh.vertices; var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i += 4) { float s = i / (float)v.Length * 12f; uv[i] = new Vector2(0, s); uv[i + 1] = new Vector2(0, s + 0.3f); uv[i + 2] = new Vector2(1, s + 0.3f); uv[i + 3] = new Vector2(1, s); }
            mesh.uv = uv;
        }

        // ---- the Mall --------------------------------------------------------------------------------------------------
        private static void BuildMall(WorldTerrain t, Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            double g = WorldTerrain.DatumM, cx = Mall.CentreX, y0 = Mall.PondY0, y1 = Mall.PondY1, hw = Mall.PondHalfWidthM;
            // Mown grass in stripes either side, gravel walks, the pond's granite coping.
            foreach (double sg in new[] { -1.0, 1.0 })
            {
                for (int k = 0; k < 10; k++)
                {
                    double x = cx + sg * (hw + Mall.GrassWidthM * (k + 0.5) / 10);
                    b.Box(U(x, 0.5 * (y0 + y1), g + 0.03), new Vector3((float)(y1 - y0 + 80), 0.06f, (float)(Mall.GrassWidthM / 10)), k % 2 == 0 ? new Color(0.36f, 0.6f, 0.26f) : new Color(0.42f, 0.68f, 0.3f));
                }
                b.Box(U(cx + sg * (hw + Mall.GrassWidthM + 8), 0.5 * (Mall.MonumentY + Mall.CapitolY), g + 0.04), new Vector3((float)(Mall.CapitolY - Mall.MonumentY), 0.08f, 12f), new Color(0.85f, 0.8f, 0.68f));
                b.Box(U(cx + sg * (hw + 1), 0.5 * (y0 + y1), g + 0.3), new Vector3((float)(y1 - y0), 0.6f, 2f), new Color(0.7f, 0.7f, 0.68f));
                // Elm rows beyond the walks (visual).
                for (double y = Mall.MonumentY + 60; y < Mall.CapitolY - 60; y += 28)
                {
                    double tx = cx + sg * (hw + Mall.GrassWidthM + 22);
                    b.Box(U(tx, y, g + 3), new Vector3(1f, 6f, 1f), new Color(0.35f, 0.25f, 0.15f));
                    b.Cylinder(U(tx, y, g + 5), 6f, 6f, 8, new Color(0.2f, 0.42f, 0.18f), new Color(0.24f, 0.48f, 0.2f), 5f);
                }
            }
            foreach (double yy in new[] { y0, y1 }) b.Box(U(cx, yy, g + 0.3), new Vector3(2f, 0.6f, (float)(2 * hw + 2)), new Color(0.7f, 0.7f, 0.68f));

            // The Washington Monument: 555 ft marble obelisk, aluminium-tipped pyramidion; a ring of flags.
            {
                double my = Mall.MonumentY, sh = Mall.MonumentShaftM; float bh = (float)Mall.MonumentBaseHalfM, th = (float)Mall.MonumentShaftTopHalfM;
                Vector3 P(float sx, float sy, double up, float half) => U(cx + sx * half, my + sy * half, g + up);
                (float, float)[] c = { (-1, -1), (1, -1), (1, 1), (-1, 1) };
                for (int k = 0; k < 4; k++)
                {
                    var (ax, ay) = c[k]; var (bx, by) = c[(k + 1) % 4];
                    b.Quad(P(ax, ay, 0, bh), P(ax, ay, sh, th), P(bx, by, sh, th), P(bx, by, 0, bh), k % 2 == 0 ? Marble : Marble * 0.95f);
                    b.Tri(P(ax, ay, sh, th), U(cx, my, g + Mall.MonumentHeightM), P(bx, by, sh, th), Marble * 0.97f);
                }
                for (int k = 0; k < 50; k++)
                {
                    double a = k * System.Math.PI * 2 / 50; double fx = cx + 40 * System.Math.Cos(a), fy = my + 40 * System.Math.Sin(a);
                    b.Box(U(fx, fy, g + 7), new Vector3(0.3f, 14f, 0.3f), new Color(0.85f, 0.85f, 0.85f));
                    b.Box(U(fx, fy + 1.5, g + 12.5), new Vector3(3f, 2f, 0.1f), k % 2 == 0 ? new Color(0.75f, 0.12f, 0.15f) : Color.white);
                }
            }

            // The Capitol: white wings with a colonnaded west front, the drum ringed with columns, the cast-iron dome,
            // the lantern and Freedom on top.
            {
                double cy = Mall.CapitolY;
                b.Box(U(cx, cy, g + Mall.CapitolWingM / 2), new Vector3((float)(2 * Mall.CapitolHalfDepthM), (float)Mall.CapitolWingM, (float)(2 * Mall.CapitolHalfLengthM)), Marble, Marble * 0.92f);
                b.Box(U(cx, cy - Mall.CapitolHalfDepthM - 6, g + 4), new Vector3(12f, 8f, 90f), Marble * 0.97f);   // west terrace steps
                for (double x = -40; x <= 40; x += 8) b.Box(U(cx + x, cy - Mall.CapitolHalfDepthM - 2, g + 8 + Mall.CapitolWingM / 2 - 4), new Vector3(1.8f, (float)Mall.CapitolWingM - 8, 1.8f), Marble);
                b.Box(U(cx, cy - Mall.CapitolHalfDepthM - 2, g + Mall.CapitolWingM + 3), new Vector3(6f, 6f, 90f), Marble * 0.95f);   // pediment
                float R = (float)Mall.CapitolDomeR;
                b.Cylinder(U(cx, cy, g + Mall.CapitolWingM), R, 26f, 32, Marble);                                     // drum
                for (int k = 0; k < 24; k++)
                {
                    double a = k * System.Math.PI * 2 / 24;
                    b.Box(U(cx + (R + 2.5) * System.Math.Cos(a), cy + (R + 2.5) * System.Math.Sin(a), g + Mall.CapitolWingM + 12), new Vector3(1.6f, 22f, 1.6f), Marble);
                }
                // Dome: stacked rings closing over.
                const int rings = 10; double dz0 = g + Mall.CapitolWingM + 26, dh = 26;
                for (int i = 0; i < rings; i++)
                {
                    double a0 = i * System.Math.PI / 2 / rings, a1 = (i + 1) * System.Math.PI / 2 / rings;
                    float r0 = R * Mathf.Cos((float)a0) * 0.92f, r1 = R * Mathf.Cos((float)a1) * 0.92f;
                    double z0 = dz0 + dh * System.Math.Sin(a0), z1 = dz0 + dh * System.Math.Sin(a1);
                    for (int k = 0; k < 32; k++)
                    {
                        double p0 = k * System.Math.PI * 2 / 32, p1 = (k + 1) * System.Math.PI * 2 / 32;
                        b.Quad(U(cx + r0 * System.Math.Cos(p0), cy + r0 * System.Math.Sin(p0), z0), U(cx + r1 * System.Math.Cos(p0), cy + r1 * System.Math.Sin(p0), z1),
                               U(cx + r1 * System.Math.Cos(p1), cy + r1 * System.Math.Sin(p1), z1), U(cx + r0 * System.Math.Cos(p1), cy + r0 * System.Math.Sin(p1), z0), Marble * 0.98f);
                    }
                }
                b.Cylinder(U(cx, cy, dz0 + dh), 4f, (float)(Mall.CapitolDomeTopM - Mall.CapitolWingM - 26 - dh - 6), 12, Marble);   // lantern
                b.Box(U(cx, cy, g + Mall.CapitolDomeTopM - 3), new Vector3(1.2f, 6f, 1.2f), new Color(0.3f, 0.32f, 0.3f));        // Freedom
            }
            b.Build("Mall", parent, SeasideBuilder.VC);

            // The reflecting pond's water.
            var v = new[] { U(cx - hw, y0, Mall.PondSurfaceM), U(cx + hw, y0, Mall.PondSurfaceM), U(cx + hw, y1, Mall.PondSurfaceM), U(cx - hw, y1, Mall.PondSurfaceM) };
            var mesh = new Mesh { name = "ReflectingPond" }; mesh.vertices = v; mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }; mesh.RecalculateNormals();
            var go = new GameObject("ReflectingPond"); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = WorldBuilder.Mat("FlyingGame/Water", new Color(0.12f, 0.3f, 0.36f, 0.95f));
        }
    }

    /// <summary>Runtime for the playground: the world clock (moving scenery shares it with the physics), the spinning
    /// ring's rotation, and the rooftop ring race (HUD line in the shared text stack).</summary>
    public sealed class PlaygroundRuntime : MonoBehaviour
    {
        /// <summary>Every city's spinning ring (the combat city's and each Valley city's).</summary>
        public static readonly List<Transform> SpinRings = new();
        private readonly List<RooftopRace> _races = new();
        private RooftopRace _race;   // the one the HUD shows: the race being flown (or the last to report)
        private FlightSimDriver _drv;
        private GUIStyle _style; private int _fs;
        private float _eventUntil; private string _lastEvent = "";

        private void Update()
        {
            if (!SessionSettings.ReplayActive) WorldClock.TimeS = Time.timeSinceLevelLoad;
            var yaw = Quaternion.Euler(0f, (float)(FlyCity.SpinYawRad(WorldClock.TimeS) * Mathf.Rad2Deg), 0f);
            SpinRings.RemoveAll(r => r == null);
            foreach (Transform r in SpinRings) r.rotation = yaw;
            _drv ??= FindFirstObjectByType<FlightSimDriver>();
            if (_drv?.Sim?.Aircraft == null || WorldTerrain.Active == null) return;
            if (_races.Count == 0)
            {
                _races.Add(new RooftopRace(WorldTerrain.Active, FlyCity.Grid));
                for (int p = 0; p < WorldTerrain.PlateauCount; p++) _races.Add(new RooftopRace(WorldTerrain.Active, ValleyCity.At(p)));
            }
            foreach (RooftopRace race in _races)
            {
                string before = race.LastEvent;
                race.Update(_drv.Sim.Aircraft.State.Position, WorldClock.TimeS);
                if (race.LastEvent != before || (race.StartS >= 0 && (_race == null || _race.StartS < 0))) _race = race;
            }
            if (_race != null && _race.LastEvent != _lastEvent) { _lastEvent = _race.LastEvent; _eventUntil = Time.time + 6f; }
        }

        private void OnGUI()
        {
            if (_race == null || SessionSettings.MenuOpen || UiLayout.Modal) return;
            bool running = _race.StartS >= 0, recent = Time.time < _eventUntil;
            if (!running && !recent) return;
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_style == null || fs != _fs) { _fs = fs; _style = new GUIStyle { font = UiFont.Get(), fontSize = fs, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.2f) } }; }
            string txt = running ? $"ROOFTOP RACE   ring {_race.Next + 1}/5   {WorldClock.TimeS - _race.StartS:F1} s" + (recent ? $"   ·   {_race.LastEvent}" : "") : _race.LastEvent;
            UiLayout.Label(UiLayout.NextLine(fs * 1.5f), txt, _style);
        }
    }
}

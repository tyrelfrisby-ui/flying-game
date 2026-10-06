using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Builds the visible world from <see cref="WorldTerrain"/> (the same height field the wheels use):
    /// the terrain mesh (valley + three canyon-wall steps + plateaus), lakes and the stepping river, and
    /// the four identical airports — three parallel strips (paved, gravel STOL strip with its landing line and
    /// distance marks, undulating grass), an apron and an open hangar — plus the ploughed field and its power line. Edit-mode safe (used by the editor's
    /// world render tool as well as at play start).
    /// </summary>
    public static class WorldBuilder
    {
        // Sim (x north, y east, up) → Unity (east, up, north).
        public static Vector3 U(double x, double y, double up) => new((float)y, (float)up, (float)x);

        private static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        public static Material Mat(string shader, Color c)
        {
            Shader sh = Shader.Find(shader) ?? Shader.Find("Unlit/Color");
            return new Material(sh) { color = c };
        }

        private static readonly Dictionary<Color, Material> LitMats = new();
        private static Material Lit(Color c)
        {
            if (!LitMats.TryGetValue(c, out Material m)) { m = Mat("FlyingGame/Lit", c); LitMats[c] = m; }
            return m;
        }

        /// <summary>Build everything under one root; returns it.</summary>
        public static GameObject BuildAll()
        {
            WorldTerrain.Active ??= new WorldTerrain();
            var root = new GameObject("World");
            BuildTerrain(WorldTerrain.Active, root.transform);
            BuildWater(WorldTerrain.Active, root.transform);
            BuildWaterfalls(WorldTerrain.Active, root.transform);
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports) BuildAirport(a, root.transform);
            // The same landscape on every plateau (owner 2026-09-09): bridge, crop field, landmarks, aerobatic
            // box and race course are built once per step, shifted by WorldTerrain.PlateauDy.
            CropFieldMeshes = new Mesh[WorldTerrain.PlateauCount];
            PylonTops.Clear();
            for (int p = 0; p < WorldTerrain.PlateauCount; p++)
            {
                BuildBridge(WorldTerrain.Active, root.transform, p);
                BuildCropField(WorldTerrain.Active, root.transform, p);
                BuildLandmarks(WorldTerrain.Active, root.transform, p);
                BuildAeroBox(WorldTerrain.Active, root.transform, p);
                BuildRaceCourse(WorldTerrain.Active, root.transform, p);
            }
            Landmarks.RegisterSolids(WorldTerrain.Active);
            WorldTerrain.Active.RegisterWaterfallSolids();   // the rock shelves over the plunge falls
            BuildCombatZone(WorldTerrain.Active, root.transform);
            SeasideBuilder.Build(WorldTerrain.Active, root.transform);   // ocean, Golden Gate, the island
            PlaygroundBuilder.Build(WorldTerrain.Active, root.transform); // canyon lake, the city built for flying, the Mall
            // Slope soaring: terrain-following flow over every wall (air rises up a windward face, sinks on the
            // lee) — replaces the old single Gaussian lift band.
            Atmosphere.ActiveRidge = null;
            Atmosphere.SlopeLiftEnabled = true;
            return root;
        }

        // ---- terrain mesh ------------------------------------------------------------------------

        private const double MinX = -7000, MaxX = 9000, MinY = -15000, MaxY = 12500;   // MinY: past the Summit plateau's tower road; MaxY: past the island

        private const double CoarseStep = 60.0, FineStep = 6.0;

        /// <summary>Terrain vertex colour by slope and height (shared by the coarse mesh and the fine waterfall patches).</summary>
        private static Color TerrainColor(WorldTerrain t, double x, double y, double h)
        {
            var grass = new Color(0.42f, 0.55f, 0.28f);
            var dry = new Color(0.62f, 0.56f, 0.36f);
            var rock = new Color(0.47f, 0.42f, 0.37f);
            var snow = new Color(0.9f, 0.9f, 0.92f);
            double dhx = (t.HeightAt(x + 8, y) - t.HeightAt(x - 8, y)) / 16.0;
            double dhy = (t.HeightAt(x, y + 8) - t.HeightAt(x, y - 8)) / 16.0;
            float slope = (float)System.Math.Sqrt(dhx * dhx + dhy * dhy);
            float hf = Mathf.Clamp01((float)h / 2800f);
            Color c = Color.Lerp(grass, dry, hf * 0.9f);
            if (h > 2500) c = Color.Lerp(c, snow, Mathf.Clamp01((float)(h - 2500) / 300f));
            float rockiness = Mathf.Clamp01((slope - 0.35f) / 0.6f);
            c = Color.Lerp(c, rock, rockiness);
            // Sand: the beaches under the sea cliffs, Avalon's cove, the cave's beach; the sea floor below.
            if (h < 3.0 && Coast.IsSea(x, y))
            {
                var sand = new Color(0.86f, 0.79f, 0.6f);
                c = h > -0.6 ? Color.Lerp(c, sand, 0.9f) : Color.Lerp(sand, new Color(0.35f, 0.4f, 0.42f), Mathf.Clamp01((float)(-h) / 20f));
                c.a = 0f;
                return c;
            }
            if (SeaCave.IsSand(x, y)) { c = new Color(0.86f, 0.79f, 0.6f, 0f); return c; }
            // The canyon lake's walls: banded Navajo sandstone.
            if (y > CanyonLake.Y0 - 60 && y < CanyonLake.DamY + 80 && slope > 0.5f && System.Math.Abs(x - WorldTerrain.RiverCentreX(y)) < CanyonLake.HalfWidthAt(y) + 40)
            {
                int band = (int)(h / 9) % 3;
                Color sandstone = band == 0 ? new Color(0.78f, 0.43f, 0.27f) : band == 1 ? new Color(0.86f, 0.58f, 0.38f) : new Color(0.66f, 0.34f, 0.22f);
                c = Color.Lerp(c, sandstone, Mathf.Clamp01((slope - 0.35f) / 0.4f));
            }
            c.a = 1f - Mathf.Clamp01(slope / 0.08f); // flatness → field grid lines
            return c;
        }

        /// <summary>Coarse-cell index rectangle [i0,i1) × [j0,j1) that a waterfall's fine patch replaces.</summary>
        private static (int i0, int i1, int j0, int j1, double step) FinePatchCells(WorldTerrain.Waterfall f)
        {
            double hx = WorldTerrain.FallNotchHalfSpanM + 240;
            return Cells(f.X - hx, f.X + hx, f.LipY - 240, f.LipY + 460, FineStep);
        }

        private static (int i0, int i1, int j0, int j1, double step) Cells(double x0, double x1, double y0, double y1, double step)
        {
            int i0 = (int)System.Math.Floor((x0 - MinX) / CoarseStep), i1 = (int)System.Math.Ceiling((x1 - MinX) / CoarseStep);
            int j0 = (int)System.Math.Floor((y0 - MinY) / CoarseStep), j1 = (int)System.Math.Ceiling((y1 - MinY) / CoarseStep);
            return (i0, i1, j0, j1, step);
        }

        /// <summary>Seaside detail (step must divide the 60 m coarse grid): Avalon + the sea cave, the sea arch, Airport
        /// in the Sky, the Golden Gate's strait.</summary>
        private static IEnumerable<(int i0, int i1, int j0, int j1, double step)> SeasidePatches()
        {
            double R = Island.AvalonCoveR;
            yield return Cells(System.Math.Min(Island.AvalonX - R - 450, SeaCave.Cx - SeaCave.RadiusM - 90), Island.AvalonX + R + 450,
                               System.Math.Min(Island.AvalonY - R - 120, SeaCave.Cy - SeaCave.RadiusM - 90), System.Math.Max(Island.AvalonY + R + 450, SeaCave.Cy + SeaCave.RadiusM + 90), 5);
            yield return Cells(SeaArch.X0 - 200, SeaArch.X1 + 200, SeaArch.Cy - 350, SeaArch.Cy + 350, 10);
            yield return Cells(Island.RunwayX - Island.RunwayPadHalfX - 200, Island.RunwayX + Island.RunwayPadHalfX + 200, Island.RunwayY - 350, Island.RunwayY + 350, 10);
            // The canyon lake (sandstone walls, buttes): from just past the Valley waterfall's own patch to the dam.
            double fallsEnd = 0; foreach (WorldTerrain.Waterfall f in WorldTerrain.Active.Waterfalls) if (f.Step == 0) fallsEnd = f.LipY + 460;
            double lx0 = double.MaxValue, lx1 = double.MinValue;
            for (double y = CanyonLake.Y0; y < CanyonLake.DamY + 100; y += 50) { lx0 = System.Math.Min(lx0, CanyonLake.CentreX(y) - 420); lx1 = System.Math.Max(lx1, CanyonLake.CentreX(y) + 420); }
            yield return Cells(lx0, lx1, fallsEnd + 61, CanyonLake.DamY + 120, 10);
            yield return Cells(GoldenGate.CentreX - GoldenGate.HalfMainSpanM - 500, GoldenGate.CentreX + GoldenGate.HalfMainSpanM + 500, GoldenGate.Y - 900, GoldenGate.Y + 400, 10);
        }

        private static void BuildTerrain(WorldTerrain t, Transform parent)
        {
            double step = CoarseStep;
            int nx = (int)((MaxX - MinX) / step) + 1, ny = (int)((MaxY - MinY) / step) + 1;
            var verts = new Vector3[nx * ny];
            var cols = new Color[nx * ny];
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                double x = MinX + i * step, y = MinY + j * step;
                double h = t.HeightAt(x, y);
                verts[j * nx + i] = U(x, y, h);
                cols[j * nx + i] = TerrainColor(t, x, y, h);
            }
            // Cells replaced by a waterfall's fine patch (a 60 m grid cannot show a 60 m vertical recess).
            var patches = new List<(int i0, int i1, int j0, int j1, double step)>();
            foreach (WorldTerrain.Waterfall f in t.Waterfalls) patches.Add(FinePatchCells(f));
            patches.AddRange(SeasidePatches());
            var tris = new List<int>((nx - 1) * (ny - 1) * 6);
            for (int j = 0; j < ny - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                bool skip = false;
                foreach (var p in patches) if (i >= p.i0 && i < p.i1 && j >= p.j0 && j < p.j1) { skip = true; break; }
                if (skip) continue;
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                // Unity is left-handed: (east, up, north). Winding for an upward normal.
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "Terrain" };
            mesh.vertices = verts; mesh.colors = cols; mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Terrain");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat("FlyingGame/Terrain", Color.white);

            // Fine patches: same height field at 6 m so the lip, the sheer concave back wall and the notch read.
            // Their edges land exactly on the coarse grid lines (60 / 6 = 10), so there is no seam.
            foreach (var p in patches)
            {
                double x0 = MinX + p.i0 * step, x1 = MinX + p.i1 * step, y0 = MinY + p.j0 * step, y1 = MinY + p.j1 * step;
                double fs = p.step;
                int fx = (int)System.Math.Round((x1 - x0) / fs) + 1, fy = (int)System.Math.Round((y1 - y0) / fs) + 1;
                var fv = new Vector3[fx * fy]; var fc = new Color[fx * fy];
                for (int j = 0; j < fy; j++)
                for (int i = 0; i < fx; i++)
                {
                    double x = x0 + i * fs, y = y0 + j * fs;
                    double h = t.HeightAt(x, y);
                    fv[j * fx + i] = U(x, y, h);
                    fc[j * fx + i] = TerrainColor(t, x, y, h);
                }
                var ft = new int[(fx - 1) * (fy - 1) * 6];
                int k = 0;
                for (int j = 0; j < fy - 1; j++)
                for (int i = 0; i < fx - 1; i++)
                {
                    int a = j * fx + i, b = a + 1, c = a + fx, d = c + 1;
                    ft[k++] = a; ft[k++] = b; ft[k++] = c;
                    ft[k++] = b; ft[k++] = d; ft[k++] = c;
                }
                var fm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "TerrainFine" };
                fm.vertices = fv; fm.colors = fc; fm.triangles = ft;
                fm.RecalculateNormals(); fm.RecalculateBounds();
                var fgo = new GameObject("TerrainFine");
                fgo.transform.SetParent(parent, false);
                fgo.AddComponent<MeshFilter>().sharedMesh = fm;
                fgo.AddComponent<MeshRenderer>().sharedMaterial = Mat("FlyingGame/Terrain", Color.white);
            }
        }

        // ---- water -------------------------------------------------------------------------------

        private static void BuildWater(WorldTerrain t, Transform parent)
        {
            var water = Mat("FlyingGame/Water", new Color(0.08f, 0.28f, 0.5f, 0.92f));
            // Lakes: ellipse fans.
            foreach (WorldTerrain.Lake l in WorldTerrain.Lakes)
            {
                const int seg = 48;
                var v = new Vector3[seg + 1]; var tr = new int[seg * 3];
                v[0] = U(l.Cx, l.Cy, l.SurfaceM);
                for (int i = 0; i < seg; i++)
                {
                    double a = i * System.Math.PI * 2 / seg;
                    v[i + 1] = U(l.Cx + l.Rx * System.Math.Cos(a), l.Cy + l.Ry * System.Math.Sin(a), l.SurfaceM);
                }
                for (int i = 0; i < seg; i++) { tr[i * 3] = 0; tr[i * 3 + 1] = i + 1; tr[i * 3 + 2] = (i + 1) % seg + 1; }
                Spawn("Lake", v, tr, water, parent, true);
            }
            // River: ribbon along the meander; the surface steps down at each wall.
            var rv = new List<Vector3>(); var rt = new List<int>();
            double halfW = WorldTerrain.RiverHalfWidthM + 4;
            int n = 0;
            double prevSurf = 0;
            for (double y = MinY + 200; y <= MaxY - 200; y += 25)
            {
                if (WorldTerrain.PastRiverMouth(y)) break;   // the river ends in the sea
                double cx = WorldTerrain.RiverCentreX(y);
                double surf = t.WaterSurfaceAt(cx, y) ?? (t.BaseHeightAt(cx, y) - 2.0);
                rv.Add(U(cx - halfW, y, surf)); rv.Add(U(cx + halfW, y, surf));
                // Break the ribbon at a plunge fall (drop ≫ a staircase step): the curtain mesh takes over.
                if (n > 0 && prevSurf - surf < 100)
                {
                    int b = rv.Count - 4;
                    rt.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
                prevSurf = surf;
                n++;
            }
            Spawn("River", rv.ToArray(), rt.ToArray(), water, parent, true);
        }

        // ---- plunge waterfalls -------------------------------------------------------------------

        /// <summary>
        /// For each canyon-wall crossing (<see cref="WorldTerrain.Waterfalls"/>): the rock SHELF that carries
        /// the river to the lip (top = the un-recessed terrain, flat underside; the airframe collides with the
        /// matching WorldSolids box), the free-falling CURTAIN (a parabolic sheet leaving the lip at
        /// FallLipSpeedMs, widening and whitening as it falls; flowing-water shader), and the MIST at the
        /// plunge pool. The concave back wall and the notch floor are in the terrain mesh already.
        /// </summary>
        private static void BuildWaterfalls(WorldTerrain t, Transform parent)
        {
            var rock = Mat("FlyingGame/Lit", new Color(0.45f, 0.40f, 0.35f));
            var curtainMat = Mat("FlyingGame/Waterfall", new Color(0.80f, 0.90f, 1.0f, 0.82f));
            var mistMat = Mat("FlyingGame/Waterfall", new Color(1f, 1f, 1f, 0.55f));
            mistMat.SetFloat("_Mist", 1f);

            foreach (WorldTerrain.Waterfall f in t.Waterfalls)
            {
                var root = new GameObject($"Waterfall-{f.Step}");
                root.transform.SetParent(parent, false);

                // -- shelf: grid of the shelf-top surface over the recess footprint, skirted down to a flat underside.
                {
                    const double step = 10.0;
                    double x0 = f.X - WorldTerrain.FallNotchHalfSpanM, x1 = f.X + WorldTerrain.FallNotchHalfSpanM;
                    double y0 = f.LipY - WorldTerrain.FallRecessM - 2, y1 = f.LipY + 0.5;
                    double bottom = t.FallShelfBottomM(f);
                    int nx = (int)((x1 - x0) / step) + 1, ny = (int)((y1 - y0) / step) + 1;
                    var v = new List<Vector3>(); var tr = new List<int>();
                    for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        double x = x0 + i * step, y = y0 + j * step;
                        // Only the part of the footprint that is actually undercut (the recess is concave in plan).
                        double back = f.LipY - WorldTerrain.FallRecessAt(f, x);
                        double yy = System.Math.Max(y, back - 0.5);
                        v.Add(U(x, yy, t.HeightAt(x, yy, shelfTop: true)));
                    }
                    for (int j = 0; j < ny - 1; j++)
                    for (int i = 0; i < nx - 1; i++)
                    {
                        int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                        tr.AddRange(new[] { a, b, c, b, d, c });
                    }
                    // Underside (flat) + front face along the lip + two ends: a closed-looking slab.
                    int baseIdx = v.Count;
                    v.Add(U(x0, y0, bottom)); v.Add(U(x1, y0, bottom)); v.Add(U(x0, y1, bottom)); v.Add(U(x1, y1, bottom));
                    tr.AddRange(new[] { baseIdx, baseIdx + 2, baseIdx + 1, baseIdx + 1, baseIdx + 2, baseIdx + 3 });
                    // Lip face: from the top row down to the underside.
                    int topRow = (ny - 1) * nx;
                    for (int i = 0; i < nx - 1; i++)
                    {
                        int a = topRow + i, b = a + 1;
                        int p0 = v.Count; v.Add(new Vector3(v[a].x, (float)bottom, v[a].z));
                        int p1 = v.Count; v.Add(new Vector3(v[b].x, (float)bottom, v[b].z));
                        tr.AddRange(new[] { a, b, p0, b, p1, p0 });
                    }
                    // End faces (x0 and x1 columns).
                    foreach (int col in new[] { 0, nx - 1 })
                    {
                        for (int j = 0; j < ny - 1; j++)
                        {
                            int a = j * nx + col, b = a + nx;
                            int p0 = v.Count; v.Add(new Vector3(v[a].x, (float)bottom, v[a].z));
                            int p1 = v.Count; v.Add(new Vector3(v[b].x, (float)bottom, v[b].z));
                            tr.AddRange(new[] { a, p0, b, b, p0, p1 });
                        }
                    }
                    Spawn("Shelf", v.ToArray(), tr.ToArray(), rock, root.transform, true);
                }

                // -- curtain: parabola from the lip; two sheets (front/back) for depth.
                {
                    double zTop = t.RiverSurfaceAt(f.LipY - 1.0);
                    double zPool = t.RiverSurfaceAt(f.LipY + WorldTerrain.RiverTableStepPublic + 2.0);
                    double drop = System.Math.Max(10.0, zTop - zPool);
                    double tFall = System.Math.Sqrt(2.0 * drop / 9.81);
                    const int rows = 48;
                    foreach (double sheet in new[] { 0.0, -3.0 })
                    {
                        var v = new Vector3[(rows + 1) * 2]; var uv = new Vector2[(rows + 1) * 2];
                        var tr = new int[rows * 6];
                        for (int k = 0; k <= rows; k++)
                        {
                            double s01 = (double)k / rows;
                            double tk = tFall * s01;
                            double y = f.LipY + WorldTerrain.FallLipSpeedMs * tk + sheet;
                            double z = zTop - 0.5 * 9.81 * tk * tk;
                            double half = (WorldTerrain.RiverHalfWidthM + 2) * (1.0 + 0.25 * s01);   // the sheet spreads as it falls
                            v[k * 2] = U(f.X - half, y, z); v[k * 2 + 1] = U(f.X + half, y, z);
                            uv[k * 2] = new Vector2(0, (float)s01); uv[k * 2 + 1] = new Vector2(1, (float)s01);
                            if (k > 0)
                            {
                                int b = (k - 1) * 2, o = (k - 1) * 6;
                                tr[o] = b; tr[o + 1] = b + 2; tr[o + 2] = b + 1; tr[o + 3] = b + 1; tr[o + 4] = b + 2; tr[o + 5] = b + 3;
                            }
                        }
                        SpawnUv(sheet == 0.0 ? "Curtain" : "CurtainBack", v, uv, tr, curtainMat, root.transform);
                    }

                    // -- mist at the plunge pool: three crossed vertical quads, 160 m wide, 90 m tall, boiling.
                    double yHit = f.LipY + WorldTerrain.FallLipSpeedMs * tFall;
                    for (int q = 0; q < 3; q++)
                    {
                        double ang = q * System.Math.PI / 3.0;
                        double dx = System.Math.Cos(ang) * 80, dy = System.Math.Sin(ang) * 80;
                        var v = new[] { U(f.X - dx, yHit - dy, zPool - 2), U(f.X + dx, yHit + dy, zPool - 2), U(f.X - dx, yHit - dy, zPool + 90), U(f.X + dx, yHit + dy, zPool + 90) };
                        var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                        SpawnUv("Mist", v, uv, new[] { 0, 2, 1, 1, 2, 3 }, mistMat, root.transform);
                    }
                }
            }
        }

        private static GameObject SpawnUv(string name, Vector3[] v, Vector2[] uv, int[] tris, Material m, Transform parent)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = name };
            mesh.vertices = v; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        private static GameObject Spawn(string name, Vector3[] v, int[] tris, Material m, Transform parent, bool doubleSided)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = v;
            if (doubleSided)
            {
                var t2 = new int[tris.Length * 2];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    t2[i] = tris[i]; t2[i + 1] = tris[i + 1]; t2[i + 2] = tris[i + 2];
                    t2[tris.Length + i] = tris[i]; t2[tris.Length + i + 1] = tris[i + 2]; t2[tris.Length + i + 2] = tris[i + 1];
                }
                tris = t2;
            }
            mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        // ---- bridge over the gorge (a road crossing abeam the Valley airport) ---------------------

        private static void BuildBridge(WorldTerrain t, Transform parent, int p)
        {
            double y = WorldTerrain.BridgeY + WorldTerrain.PlateauDy(p);
            double cx = WorldTerrain.RiverCentreX(y);
            double halfSpan = System.Math.Max(WorldTerrain.GorgeHalfWidthAt(y) + 60, CanyonLake.HalfWidthAt(y) + 45);   // the Valley copy spans the canyon lake
            double deck = t.BaseHeightAt(cx - halfSpan - 40, y) + 1.5;   // rim level
            var root = new GameObject($"Bridge{p}");
            root.transform.SetParent(parent, false);
            var steel = new Color(0.55f, 0.2f, 0.18f);
            var concrete = new Color(0.7f, 0.7f, 0.68f);
            // Deck across the gorge (along x), 12 m wide, plus approach ramps.
            WBox(root, "Deck", U(cx, y, deck), new Vector3(12f, 1.2f, (float)(2 * halfSpan)), concrete);
            WBox(root, "Approach", U(cx - halfSpan - 100, y, deck - 0.3), new Vector3(12f, 0.6f, 200f), concrete);
            WBox(root, "Approach", U(cx + halfSpan + 100, y, deck - 0.3), new Vector3(12f, 0.6f, 200f), concrete);
            // Railings.
            foreach (float side in new[] { -6.5f, 6.5f })
                WBox(root, "Rail", U(cx, y + side, deck + 1.5), new Vector3(0.3f, 1.2f, (float)(2 * halfSpan)), steel);
            // Two towers on the rims with a shallow arch of truss members under the deck.
            double waterSurf = t.WaterSurfaceAt(cx, y) ?? (deck - 60);
            foreach (double side in new[] { -1.0, 1.0 })
            {
                double tx = cx + side * (halfSpan - 25);
                double ground = t.HeightAt(tx, y);
                WBox(root, "Tower", U(tx, y, (ground + deck + 30) * 0.5), new Vector3(8f, (float)(deck + 30 - ground), 8f), steel);
                WBox(root, "Tower", U(tx, y, (ground + deck + 30) * 0.5), new Vector3(8f, (float)(deck + 30 - ground), 8f), steel);
            }
            // Arch: chord of boxes from rim to rim dipping toward the water.
            int n = 14;
            for (int i = 0; i < n; i++)
            {
                double f0 = (double)i / n, f1 = (double)(i + 1) / n;
                double x0 = cx - halfSpan + 2 * halfSpan * f0, x1 = cx - halfSpan + 2 * halfSpan * f1;
                double sag = deck - 0.75 * (deck - waterSurf);
                double z0 = deck - (deck - sag) * System.Math.Sin(f0 * System.Math.PI), z1 = deck - (deck - sag) * System.Math.Sin(f1 * System.Math.PI);
                Vector3 a = U(x0, y, z0), b = U(x1, y, z1);
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Kill(seg.GetComponent<Collider>());
                seg.name = "Arch"; seg.transform.SetParent(root.transform, false);
                seg.transform.position = (a + b) * 0.5f; seg.transform.rotation = Quaternion.LookRotation(b - a);
                seg.transform.localScale = new Vector3(3f, 3f, (b - a).magnitude + 1f);
                seg.GetComponent<MeshRenderer>().sharedMaterial = Lit(steel);
                // hanger from arch to deck
                WBox(root, "Hanger", U((x0 + x1) * 0.5, y, (deck + z0) * 0.5), new Vector3(0.6f, (float)System.Math.Max(0.5, deck - z0), 0.6f), steel);
            }
        }

        private static void WBox(GameObject parent, string name, Vector3 worldPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.position = worldPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit(color);
        }

        // ---- IAC aerobatic box: white ground markers (corner Ls, mid-side bars, centre cross) --------

        private static void BuildAeroBox(WorldTerrain t, Transform parent, int p)
        {
            var root = new GameObject($"AeroBox{p}");
            root.transform.SetParent(parent, false);
            double cx = AeroBox.CenterX, cy = AeroBox.CenterYAt(p), h = AeroBox.SizeM / 2;
            double z = t.HeightAt(cx, cy) + 0.08;
            foreach (double sx in new[] { -1.0, 1.0 })
            foreach (double sy in new[] { -1.0, 1.0 })
            {
                // Corner L: two 40 m arms pointing into the box.
                WBox(root, "CornerL", U(cx + sx * (h - 20), cy + sy * h, z), new Vector3(5f, 0.1f, 40f), Paint);
                WBox(root, "CornerL", U(cx + sx * h, cy + sy * (h - 20), z), new Vector3(40f, 0.1f, 5f), Paint);
            }
            // Mid-side bars and centre cross.
            WBox(root, "Mid", U(cx + h, cy, z), new Vector3(30f, 0.1f, 5f), Paint);
            WBox(root, "Mid", U(cx - h, cy, z), new Vector3(30f, 0.1f, 5f), Paint);
            WBox(root, "Mid", U(cx, cy + h, z), new Vector3(5f, 0.1f, 30f), Paint);
            WBox(root, "Mid", U(cx, cy - h, z), new Vector3(5f, 0.1f, 30f), Paint);
            WBox(root, "Centre", U(cx, cy, z), new Vector3(5f, 0.1f, 60f), Paint);
            WBox(root, "Centre", U(cx, cy, z), new Vector3(60f, 0.1f, 5f), Paint);
            var lbl = new GameObject("Label"); lbl.transform.SetParent(root.transform, false);
            lbl.transform.position = U(cx, cy - h - 60, z + 0.1); lbl.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = lbl.AddComponent<TextMesh>(); tm.text = "AEROBATIC BOX"; tm.fontSize = 48; tm.characterSize = 1.2f; tm.anchor = TextAnchor.MiddleCenter; tm.color = Paint;

            // The box itself: a glass box in the sky from the 328 ft floor to the 3 500 ft ceiling — six faint
            // panes with a Fresnel edge and a little sky reflection. No collision: fly through it.
            var glass = Mat("FlyingGame/Glass", new Color(0.6f, 0.8f, 1.0f, 0.10f));
            float floor = (float)(t.HeightAt(cx, cy) + AeroBox.FloorAglM), ceil = (float)(t.HeightAt(cx, cy) + AeroBox.CeilingAglM);
            float mid = (floor + ceil) * 0.5f, hgt = ceil - floor, size = (float)AeroBox.SizeM;
            void Pane(string name, Vector3 centre, Quaternion rot, Vector2 dims)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Kill(q.GetComponent<Collider>()); q.name = name; q.transform.SetParent(root.transform, false);
                q.transform.position = centre; q.transform.rotation = rot; q.transform.localScale = new Vector3(dims.x, dims.y, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial = glass;
            }
            Vector3 c0 = U(cx, cy, 0);
            Pane("Floor", new Vector3(c0.x, floor, c0.z), Quaternion.Euler(90f, 0f, 0f), new Vector2(size, size));
            Pane("Ceiling", new Vector3(c0.x, ceil, c0.z), Quaternion.Euler(90f, 0f, 0f), new Vector2(size, size));
            Pane("North", new Vector3(c0.x, mid, c0.z + size / 2), Quaternion.identity, new Vector2(size, hgt));
            Pane("South", new Vector3(c0.x, mid, c0.z - size / 2), Quaternion.identity, new Vector2(size, hgt));
            Pane("East", new Vector3(c0.x + size / 2, mid, c0.z), Quaternion.Euler(0f, 90f, 0f), new Vector2(size, hgt));
            Pane("West", new Vector3(c0.x - size / 2, mid, c0.z), Quaternion.Euler(0f, 90f, 0f), new Vector2(size, hgt));
            // Edge frame lines so the box reads even edge-on.
            var edge = new Color(0.8f, 0.92f, 1f);
            foreach (float y in new[] { floor, ceil })
            {
                WBox(root, "Edge", new Vector3(c0.x, y, c0.z + size / 2), new Vector3(size, 1.2f, 1.2f), edge);
                WBox(root, "Edge", new Vector3(c0.x, y, c0.z - size / 2), new Vector3(size, 1.2f, 1.2f), edge);
                WBox(root, "Edge", new Vector3(c0.x + size / 2, y, c0.z), new Vector3(1.2f, 1.2f, size), edge);
                WBox(root, "Edge", new Vector3(c0.x - size / 2, y, c0.z), new Vector3(1.2f, 1.2f, size), edge);
            }
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
                WBox(root, "Edge", new Vector3(c0.x + sx * size / 2, mid, c0.z + sz * size / 2), new Vector3(1.2f, hgt, 1.2f), edge);
        }

        // ---- COMBAT ZONE (owner 2026-09-10): a 4 × 4 km glass box from the surface to 3 km, skull-and-crossbones and
        // dogfight art on the outside, bullseyes on the floor, target drones inside (CombatController) ----

        private static Texture2D _skullTex;

        /// <summary>Skull and crossbones drawn into a texture (no art assets): white on transparent.</summary>
        private static Texture2D SkullTexture()
        {
            if (_skullTex != null) return _skullTex;
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            var clear = new Color32(0, 0, 0, 0); var white = new Color32(255, 255, 255, 255); var black = new Color32(0, 0, 0, 255);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            void Disc(float cx, float cy, float r, Color32 c) { for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { float dx = x - cx, dy = y - cy; if (dx * dx + dy * dy <= r * r) px[y * N + x] = c; } }
            void Bar(float x0, float y0, float x1, float y1, float w, Color32 c)
            {
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float vx = x1 - x0, vy = y1 - y0, len2 = vx * vx + vy * vy;
                    float u = Mathf.Clamp01(((x - x0) * vx + (y - y0) * vy) / len2);
                    float px2 = x0 + u * vx - x, py2 = y0 + u * vy - y;
                    if (px2 * px2 + py2 * py2 <= w * w) px[y * N + x] = c;
                }
            }
            // Crossed bones behind the skull.
            Bar(40, 40, 216, 216, 9, white); Bar(40, 216, 216, 40, 9, white);
            foreach ((float x, float y) in new[] { (40f, 40f), (216f, 216f), (40f, 216f), (216f, 40f) }) { Disc(x - 8, y + 8, 12, white); Disc(x + 8, y - 8, 12, white); }
            // Skull: cranium, jaw, eyes, nose, teeth.
            Disc(128, 150, 62, white);
            for (int y = 60; y < 110; y++) for (int x = 86; x < 170; x++) px[y * N + x] = white;
            Disc(104, 152, 17, black); Disc(152, 152, 17, black);
            Bar(128, 132, 122, 112, 6, black); Bar(128, 132, 134, 112, 6, black);
            for (int k = 0; k < 6; k++) Bar(94 + k * 13, 62, 94 + k * 13, 95, 2.2f, black);
            t.SetPixels32(px); t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            _skullTex = t;
            return t;
        }

        private static void BuildCombatZone(WorldTerrain t, Transform parent)
        {
            var root = new GameObject("CombatZone"); root.transform.SetParent(parent, false);
            double x0 = FlyingGame.Core.Combat.CombatZone.X0, x1 = FlyingGame.Core.Combat.CombatZone.X1, y0 = FlyingGame.Core.Combat.CombatZone.Y0, y1 = FlyingGame.Core.Combat.CombatZone.Y1;
            double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            float floor = (float)t.HeightAt(cx, cy), hgt = (float)FlyingGame.Core.Combat.CombatZone.HeightM;
            float sx = (float)(y1 - y0), sz = (float)(x1 - x0);   // Unity x = sim y, Unity z = sim x
            Vector3 c0 = U(cx, cy, floor);
            var glass = Mat("FlyingGame/Glass", new Color(1f, 0.25f, 0.2f, 0.08f));
            void Pane(string name, Vector3 centre, Quaternion rot, Vector2 dims)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Kill(q.GetComponent<Collider>()); q.name = name; q.transform.SetParent(root.transform, false);
                q.transform.position = centre; q.transform.rotation = rot; q.transform.localScale = new Vector3(dims.x, dims.y, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial = glass;
            }
            float mid = floor + hgt / 2;
            Pane("Ceiling", new Vector3(c0.x, floor + hgt, c0.z), Quaternion.Euler(90f, 0f, 0f), new Vector2(sx, sz));
            Pane("North", new Vector3(c0.x, mid, c0.z + sz / 2), Quaternion.identity, new Vector2(sx, hgt));
            Pane("South", new Vector3(c0.x, mid, c0.z - sz / 2), Quaternion.identity, new Vector2(sx, hgt));
            Pane("East", new Vector3(c0.x + sx / 2, mid, c0.z), Quaternion.Euler(0f, 90f, 0f), new Vector2(sz, hgt));
            Pane("West", new Vector3(c0.x - sx / 2, mid, c0.z), Quaternion.Euler(0f, 90f, 0f), new Vector2(sz, hgt));
            var edge = new Color(1f, 0.3f, 0.2f);
            foreach (float y in new[] { floor + 1f, floor + hgt })
            {
                WBox(root, "Edge", new Vector3(c0.x, y, c0.z + sz / 2), new Vector3(sx, 2f, 2f), edge);
                WBox(root, "Edge", new Vector3(c0.x, y, c0.z - sz / 2), new Vector3(sx, 2f, 2f), edge);
                WBox(root, "Edge", new Vector3(c0.x + sx / 2, y, c0.z), new Vector3(2f, 2f, sz), edge);
                WBox(root, "Edge", new Vector3(c0.x - sx / 2, y, c0.z), new Vector3(2f, 2f, sz), edge);
            }
            foreach (float ex in new[] { -1f, 1f }) foreach (float ez in new[] { -1f, 1f })
                WBox(root, "Edge", new Vector3(c0.x + ex * sx / 2, mid, c0.z + ez * sz / 2), new Vector3(2f, hgt, 2f), edge);
            // Skull-and-crossbones and the name on every outside face, big enough to read from the airfield.
            var skullMat = new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Transparent")) { mainTexture = SkullTexture(), color = Color.white };
            void Sign(Vector3 centre, Quaternion facing, float w)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Kill(q.GetComponent<Collider>()); q.name = "Skull"; q.transform.SetParent(root.transform, false);
                q.transform.position = centre; q.transform.rotation = facing; q.transform.localScale = new Vector3(w, w, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial = skullMat;
                var lbl = new GameObject("ZoneLabel"); lbl.transform.SetParent(root.transform, false);
                lbl.transform.position = centre + facing * new Vector3(0f, -w * 0.75f, 0f); lbl.transform.rotation = facing;
                var tm = lbl.AddComponent<TextMesh>(); tm.text = "COMBAT ZONE\nGUNS HOT · AIR TO AIR · AIR TO GROUND"; tm.fontSize = 64; tm.characterSize = w * 0.012f; tm.anchor = TextAnchor.MiddleCenter; tm.color = new Color(1f, 0.35f, 0.25f);
                var mr = lbl.GetComponent<MeshRenderer>(); if (mr != null && tm.font != null) mr.sharedMaterial = DepthTestedText(tm.font);
            }
            float signW = 600f, signY = floor + 900f, off = 6f;
            Sign(new Vector3(c0.x, signY, c0.z - sz / 2 - off), Quaternion.Euler(0f, 180f, 0f), signW);   // south face, seen from the airfield side
            Sign(new Vector3(c0.x, signY, c0.z + sz / 2 + off), Quaternion.identity, signW);
            Sign(new Vector3(c0.x - sx / 2 - off, signY, c0.z), Quaternion.Euler(0f, -90f, 0f), signW);
            Sign(new Vector3(c0.x + sx / 2 + off, signY, c0.z), Quaternion.Euler(0f, 90f, 0f), signW);
            // Ground targets: bullseyes (white / red / white / red discs) flat on the ground.
            foreach (FlyingGame.Core.Combat.GroundTarget gt in FlyingGame.Core.Combat.CombatZone.BuildGroundTargets())
            {
                float g = (float)t.HeightAt(gt.X, gt.Y) + 0.15f;
                float r = (float)gt.RadiusM;
                int ring = 0;
                foreach (float f in new[] { 1f, 0.75f, 0.5f, 0.25f })
                {
                    var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Kill(d.GetComponent<Collider>());
                    d.name = "Bullseye"; d.transform.SetParent(root.transform, false);
                    d.transform.position = U(gt.X, gt.Y, g + ring * 0.05f); d.transform.localScale = new Vector3(2f * r * f, 0.05f, 2f * r * f);
                    d.GetComponent<MeshRenderer>().sharedMaterial = Lit(ring % 2 == 0 ? Color.white : new Color(0.85f, 0.1f, 0.1f));
                    ring++;
                }
            }
        }

        // ---- Air Racing course: pylons (base + burstable inflated top), rotating numbers 300 ft up, flags ----

        /// <summary>Burstable pylon tops by (plateau, element, side) — the race controller launches one on a wing strike.</summary>
        public static readonly Dictionary<(int, int, int), PylonTopBurst> PylonTops = new();

        private static void BuildRaceCourse(WorldTerrain t, Transform parent, int p)
        {
            var root = new GameObject($"RaceCourse{p}");
            root.transform.SetParent(parent, false);
            RaceElement[] els = RaceCourse.ElementsFor(p);
            var blue = new Color(0.15f, 0.35f, 0.85f); var red = new Color(0.85f, 0.15f, 0.15f); var white = Color.white;
            foreach ((int el, int side, double px, double py) in RaceCourse.Pylons(p))
            {
                double g = t.HeightAt(px, py);
                bool gate = els[el].Kind == RaceElement.Kinds.Gate;
                PylonTopBurst top = Pylon(root, U(px, py, g), (float)RaceElement.GateHeightM, gate ? blue : red, white);
                top.SetGround((float)g);
                PylonTops[(p, el, side)] = top;
            }
            int n = els.Length;
            for (int i = 0; i < n; i++)
            {
                RaceElement e = els[i];
                double g = t.HeightAt(e.X, e.Y);
                // Big number 300 ft over the element, turning once every 5 s, readable from both sides.
                var num = new GameObject($"Number{i + 1}"); num.transform.SetParent(root.transform, false);
                num.transform.position = U(e.X, e.Y, g + RaceElement.NumberAglM);
                num.AddComponent<Spin>().DegPerSec = 72f;
                // Two faces either side of an opaque plate, so each side reads correctly and never through the other.
                Color numCol = i == 0 ? new Color(0.3f, 1f, 0.4f) : i == n - 1 ? white : new Color(1f, 0.9f, 0.3f);
                WBox(num, "Plate", num.transform.position, new Vector3((i + 1 >= 10 ? 22f : 12f), 16f, 0.6f), new Color(0.12f, 0.12f, 0.15f));
                foreach (float face in new[] { 0f, 180f })
                {
                    var lbl = new GameObject("Digits"); lbl.transform.SetParent(num.transform, false);
                    lbl.transform.localRotation = Quaternion.Euler(0f, face, 0f);
                    lbl.transform.localPosition = Quaternion.Euler(0f, face, 0f) * new Vector3(0f, 0f, -0.5f);   // just in front of its side of the plate
                    var tm = lbl.AddComponent<TextMesh>(); tm.text = (i + 1).ToString(); tm.fontSize = 64; tm.characterSize = 6f; tm.anchor = TextAnchor.MiddleCenter; tm.color = numCol;
                    // Depth-tested text: the default font material draws through everything (ZTest Always), so the far
                    // face showed through the plate and doubled the digit (owner). The 3D text shader respects the plate.
                    var mr = lbl.GetComponent<MeshRenderer>();
                    if (mr != null && tm.font != null) mr.sharedMaterial = DepthTestedText(tm.font);
                }
                if (i == 0) Flag(root, U(e.X, e.Y, g + RaceElement.NumberAglM + 22), false);      // start: green
                if (i == n - 1) Flag(root, U(e.X, e.Y, g + RaceElement.NumberAglM + 22), true);   // finish: checkered
            }
        }

        private static readonly Dictionary<Font, Material> _textMats = new();

        /// <summary>Font material that writes/tests depth (GUI/3D Text Shader) so signs occlude their own back face.</summary>
        internal static Material DepthTestedText(Font font)
        {
            if (_textMats.TryGetValue(font, out Material m) && m != null) return m;
            Shader sh = Shader.Find("GUI/3D Text Shader");
            if (sh == null || font.material == null) return font.material;
            m = new Material(sh) { mainTexture = font.material.mainTexture };
            _textMats[font] = m;
            return m;
        }

        /// <summary>A racing pylon: fixed lower 60 % and an inflated top 40 % that can be launched and collapsed.</summary>
        private static PylonTopBurst Pylon(GameObject parent, Vector3 basePos, float height, Color c, Color band)
        {
            float split = height * 0.6f, rMid = Mathf.Lerp(4f, 1.2f, 0.6f);
            var go = new GameObject("PylonBase");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = basePos;
            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh(4f, rMid, split, 16);
            go.AddComponent<MeshRenderer>().sharedMaterial = Lit(c);
            var top = new GameObject("PylonTop");
            top.transform.SetParent(parent.transform, false);
            top.transform.position = basePos + Vector3.up * split;
            top.AddComponent<MeshFilter>().sharedMesh = ConeMesh(rMid, 1.2f, height - split, 16);
            top.AddComponent<MeshRenderer>().sharedMaterial = Lit(c);
            // White bands: two on the base, one on the top.
            for (int i = 1; i <= 3; i++)
            {
                float y = height * i / 4f, r = Mathf.Lerp(4f, 1.2f, i / 4f) + 0.1f;
                var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Kill(b.GetComponent<Collider>());
                bool onTop = y > split;
                b.transform.SetParent(onTop ? top.transform : go.transform, false);
                b.transform.localPosition = new Vector3(0f, onTop ? y - split : y, 0f); b.transform.localScale = new Vector3(r * 2f, height * 0.03f, r * 2f);
                b.GetComponent<MeshRenderer>().sharedMaterial = Lit(band);
            }
            return top.AddComponent<PylonTopBurst>();
        }

        /// <summary>A flag hanging above a number: a pole and a 20 × 12 m panel of 2.5 m cells — checkered (finish)
        /// or solid green (start). Built from cubes so no texture is needed.</summary>
        private static void Flag(GameObject parent, Vector3 pos, bool checkered)
        {
            var root = new GameObject(checkered ? "FinishFlag" : "StartFlag"); root.transform.SetParent(parent.transform, false);
            root.transform.position = pos;
            root.AddComponent<Spin>().DegPerSec = 72f;
            WBox(root, "Pole", pos + Vector3.up * 8f, new Vector3(0.6f, 20f, 0.6f), new Color(0.85f, 0.85f, 0.85f));
            var black = new Color(0.05f, 0.05f, 0.05f); var white = Color.white; var green = new Color(0.15f, 0.8f, 0.25f);
            for (int i = 0; i < 8; i++)
            for (int j = 0; j < 5; j++)
            {
                Color c = checkered ? ((i + j) % 2 == 0 ? white : black) : green;
                WBox(root, "Cell", pos + new Vector3(0.6f + 1.25f + i * 2.5f, 16f - j * 2.5f, 0f), new Vector3(2.5f, 2.5f, 0.15f), c);
            }
        }

        private static void Cone(GameObject parent, Vector3 basePos, float height, Color c, Color band)
        {
            var go = new GameObject("Pylon");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = basePos;
            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh(4f, 1.2f, height, 16);
            go.AddComponent<MeshRenderer>().sharedMaterial = Lit(c);
            // White bands.
            for (int i = 1; i <= 3; i++)
            {
                float y = height * i / 4f, r = Mathf.Lerp(4f, 1.2f, i / 4f) + 0.1f;
                var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Kill(b.GetComponent<Collider>()); b.transform.SetParent(go.transform, false);
                b.transform.localPosition = new Vector3(0f, y, 0f); b.transform.localScale = new Vector3(r * 2f, height * 0.03f, r * 2f);
                b.GetComponent<MeshRenderer>().sharedMaterial = Lit(band);
            }
        }

        private static Mesh ConeMesh(float rBase, float rTop, float h, int seg)
        {
            var v = new Vector3[seg * 2 + 2]; var tr = new System.Collections.Generic.List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v[i] = new Vector3(Mathf.Cos(a) * rBase, 0f, Mathf.Sin(a) * rBase);
                v[seg + i] = new Vector3(Mathf.Cos(a) * rTop, h, Mathf.Sin(a) * rTop);
            }
            v[2 * seg] = new Vector3(0f, h, 0f); v[2 * seg + 1] = Vector3.zero;
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                tr.AddRange(new[] { i, seg + i, j, j, seg + i, seg + j });      // side
                tr.AddRange(new[] { seg + i, 2 * seg, seg + j });               // cap
            }
            var m = new Mesh(); m.vertices = v; m.triangles = tr.ToArray(); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        public static Mesh Torus(float R, float r, int segs, int rings)
        {
            var v = new Vector3[segs * rings]; var tr = new int[segs * rings * 6];
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                var c = new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, 0f); // ring in the local x-y plane; local z = travel direction
                for (int j = 0; j < rings; j++)
                {
                    float b = j * Mathf.PI * 2f / rings;
                    Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    v[i * rings + j] = c + radial * (Mathf.Cos(b) * r) + new Vector3(0f, 0f, Mathf.Sin(b) * r);
                }
            }
            int k = 0;
            for (int i = 0; i < segs; i++)
            for (int j = 0; j < rings; j++)
            {
                int a = i * rings + j, b = ((i + 1) % segs) * rings + j, c = i * rings + (j + 1) % rings, d = ((i + 1) % segs) * rings + (j + 1) % rings;
                tr[k++] = a; tr[k++] = b; tr[k++] = c; tr[k++] = b; tr[k++] = d; tr[k++] = c;
            }
            var m = new Mesh(); m.vertices = v; m.triangles = tr; m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        // ---- airports ----------------------------------------------------------------------------

        private static readonly Color Asphalt = new(0.24f, 0.24f, 0.26f), Gravel = new(0.58f, 0.55f, 0.48f), Earth = new(0.36f, 0.25f, 0.15f),
            Grass = new(0.36f, 0.52f, 0.22f), Paint = Color.white, Apron = new(0.62f, 0.62f, 0.60f);

        private static void BuildAirport(WorldTerrain.Airport a, Transform parent)
        {
            var root = new GameObject($"Airport-{a.Name}");
            root.transform.SetParent(parent, false);
            // Paved apron around the hangar (the only other hard surface on the pad — the infield is rough ground).
            Slab(root.transform, "Apron", a.X + WorldTerrain.ApronDx, a.Y + WorldTerrain.ApronDy, a.ElevationM + 0.03, WorldTerrain.ApronLengthM, WorldTerrain.ApronWidthM, 0.05, 0, Apron);
            foreach (WorldTerrain.Strip s in WorldTerrain.AirportStrips)
            {
                bool paved = WorldTerrain.IsPaved(s.Kind);
                Color c = paved ? Asphalt : s.Kind == "gravel" ? Gravel : Grass;
                GameObject strip = s.Kind == "grass"
                    ? GrassStrip(root.transform, a, s)   // follows the Snoopy swoops in the height field
                    : Slab(root.transform, $"Strip-{s.Kind}", a.X + s.Dx, a.Y + s.Dy, a.ElevationM + (s.Kind == "paved-xwind" ? 0.045 : 0.04), s.Length, s.Width, 0.06, s.HeadingDeg, c);
                if (paved) RunwayMarkings(strip, s);
                if (s.Kind == "gravel")
                {
                    // STOL contest: landing line and distance marks (every 10 m, bold every 50 m, numbered).
                    double line = -s.Length * 0.5 + WorldTerrain.StolLineFromThresholdM;
                    Child(strip, "StolLine", line, 0, 0.05, 1.2, s.Width + 6, 0.04, Paint);
                    for (double d = 10; d <= WorldTerrain.StolMarkedLengthM; d += 10)
                    {
                        bool bold = d % 50 == 0;
                        Child(strip, "Mark", line + d, -(s.Width * 0.5 + 1.5), 0.05, bold ? 1.0 : 0.4, bold ? 4 : 2, 0.04, Paint);
                        Child(strip, "Mark", line + d, s.Width * 0.5 + 1.5, 0.05, bold ? 1.0 : 0.4, bold ? 4 : 2, 0.04, Paint);
                        if (bold) Label(strip, $"{d * 3.28084:F0}", line + d, s.Width * 0.5 + 6, 0.1, 2.2f);
                    }
                    Label(strip, "STOL", line - 12, 0, 0.1, 3f);
                }
            }
            // Apron + open hangar.
            BuildHangar(root.transform, a.X + WorldTerrain.HangarDx, a.Y + WorldTerrain.HangarDy, a.ElevationM);
            BuildWindsocks(root.transform, a);
            // Field name on the apron.
            Label(root, a.Name.ToUpperInvariant(), a.X + WorldTerrain.HangarDx + 60, a.Y + WorldTerrain.HangarDy - 70, a.ElevationM + 0.1, 8f);
        }

        /// <summary>
        /// Standard (FAA AC 150/5340-1) runway markings, owner 2026-10-03: threshold "piano keys" (8 stripes, 150 ft), the
        /// runway NUMBERS (60 ft tall, magnetic heading / 10, read upright from each approach end), touchdown-zone bars at
        /// 500 / 1,500 / 2,000 ft, aiming-point blocks at 1,000 ft, the 120 ft centreline stripes with 80 ft gaps, and
        /// edge stripes. Main runway along x (north) = 36 / 18; the crosswind runway along y = 09 / 27.
        /// </summary>
        private static void RunwayMarkings(GameObject strip, WorldTerrain.Strip s)
        {
            double L = s.Length, W = s.Width, ft = 0.3048;
            int hdg = (int)System.Math.Round(s.HeadingDeg / 10.0) % 36; if (hdg == 0) hdg = 36;   // flying along +along
            int recip = (hdg + 18 - 1) % 36 + 1;
            foreach (int end in new[] { -1, 1 })                       // -1: the +along approach end; +1: the far end
            {
                // "along" measured from THIS threshold, inward; across mirrored so each end reads correctly.
                double A(double d) => end < 0 ? -L / 2 + d : L / 2 - d;
                double X(double c) => end < 0 ? c : -c;
                // Threshold stripes: 8, 150 ft long, starting 20 ft in.
                double sw = 5.75 * ft, gap = (W - 2 * 3 * ft - 8 * sw) / 7.0;
                for (int k = 0; k < 8; k++)
                {
                    double c = -W / 2 + 3 * ft + sw / 2 + k * (sw + gap);
                    if (k >= 4) c += 0; // (a centreline gap is implicit in the even spacing)
                    Child(strip, "Thresh", A(20 * ft + 75 * ft), X(c), 0.05, 150 * ft, sw, 0.03, Paint);
                }
                // Numbers, 60 ft tall, 20 ft beyond the stripes.
                Numerals(strip, (end < 0 ? hdg : recip).ToString("00"), A(190 * ft + 30 * ft), end > 0);
                // Touchdown zone (3 bars at 500 ft, 2 at 1,500, 2 at 2,000), aiming point at 1,000 ft.
                foreach ((double d, int n) in new[] { (500.0, 3), (1500.0, 2), (2000.0, 2) })
                    for (int side = -1; side <= 1; side += 2)
                        for (int k = 0; k < n; k++)
                            Child(strip, "TDZ", A(d * ft + 37.5 * ft), X(side * (W / 2 - 4 * ft - k * 5 * ft - 1.5 * ft)), 0.05, 75 * ft, 3 * ft, 0.03, Paint);
                for (int side = -1; side <= 1; side += 2)
                    Child(strip, "Aim", A(1020 * ft + 75 * ft), X(side * (W / 2 - 4 * ft - 7.5 * ft)), 0.05, 150 * ft, 15 * ft, 0.03, Paint);
            }
            // Centreline: 120 ft stripes, 80 ft gaps, between the numbers.
            for (double d = -L / 2 + 280 * ft; d < L / 2 - 280 * ft - 120 * ft; d += 200 * ft)
                Child(strip, "Dash", d + 60 * ft, 0, 0.05, 120 * ft, 3 * ft, 0.03, Paint);
            // Edge stripes.
            foreach (int side in new[] { -1, 1 })
                Child(strip, "Edge", 0, side * (W / 2 - 1.5 * ft), 0.05, L - 6 * ft, 3 * ft, 0.03, Paint);
        }

        /// <summary>Runway designation numerals as paint blocks (seven-segment construction, 60 ft x 20 ft per digit).
        /// <paramref name="flip"/>: rotated 180° (the far end's numbers read from that end).</summary>
        private static void Numerals(GameObject strip, string text, double centreAlong, bool flip)
        {
            double ft = 0.3048, H = 60 * ft, Wd = 20 * ft, t = 5 * ft, pitch = Wd + 15 * ft;
            // segment: (centre across, centre along, across size, along size) in digit units; along + = "up" the digit.
            (double cx, double ca, double sx, double sa)[] Seg(char ch)
            {
                double hx = Wd / 2 - t / 2, ha = H / 2 - t / 2;
                var a = (0.0, ha, Wd, t); var d = (0.0, -ha, Wd, t); var gm = (0.0, 0.0, Wd, t);
                var f = (-hx, H / 4, t, H / 2); var b = (hx, H / 4, t, H / 2); var e = (-hx, -H / 4, t, H / 2); var c = (hx, -H / 4, t, H / 2);
                return ch switch
                {
                    '0' => new[] { a, b, c, d, e, f }, '1' => new[] { b, c }, '2' => new[] { a, b, gm, e, d }, '3' => new[] { a, b, gm, c, d },
                    '4' => new[] { f, gm, b, c }, '5' => new[] { a, f, gm, c, d }, '6' => new[] { a, f, gm, e, c, d }, '7' => new[] { a, b, c },
                    '8' => new[] { a, b, c, d, e, f, gm }, _ => new[] { a, b, c, d, f, gm },
                };
            }
            for (int i = 0; i < text.Length; i++)
            {
                double dx = (i - (text.Length - 1) / 2.0) * pitch;
                foreach (var (cx, ca, sx, sa) in Seg(text[i]))
                {
                    double across = dx + cx, along = ca;
                    if (flip) { across = -across; along = -along; }
                    Child(strip, "Num", centreAlong + along, across, 0.05, sa, sx, 0.03, Paint);
                }
            }
        }

        /// <summary>The grass strip as a fine mesh riding the height field, so its smooth undulations show.</summary>
        private static GameObject GrassStrip(Transform parent, WorldTerrain.Airport a, WorldTerrain.Strip s)
        {
            WorldTerrain t = WorldTerrain.Active;
            const double dx = 3.0; int nAlong = (int)(s.Length / dx) + 1, nAcross = 5;
            var verts = new Vector3[nAlong * nAcross]; var cols = new Color[verts.Length];
            for (int i = 0; i < nAlong; i++)
            for (int j = 0; j < nAcross; j++)
            {
                double x = a.X + s.Dx - s.Length / 2 + i * dx, y = a.Y + s.Dy - s.Width / 2 + j * s.Width / (nAcross - 1);
                verts[i * nAcross + j] = U(x, y, t.HeightAt(x, y) + 0.04);
                cols[i * nAcross + j] = Grass;
            }
            var tris = new int[(nAlong - 1) * (nAcross - 1) * 6]; int k = 0;
            for (int i = 0; i < nAlong - 1; i++)
            for (int j = 0; j < nAcross - 1; j++)
            {
                int p0 = i * nAcross + j, p1 = p0 + 1, p2 = p0 + nAcross, p3 = p2 + 1;
                tris[k++] = p0; tris[k++] = p2; tris[k++] = p1; tris[k++] = p1; tris[k++] = p2; tris[k++] = p3;
            }
            var mesh = new Mesh { name = "GrassStrip" }; mesh.vertices = verts; mesh.colors = cols; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Strip-grass"); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Lit(Grass);
            // Winding check: flip if the normal came out downward.
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0) { System.Array.Reverse(tris); mesh.triangles = tris; mesh.RecalculateNormals(); }
            return go;
        }

        /// <summary>The ploughed field's cell mesh (recoloured as the crop duster covers it) — null until built.</summary>
        /// <summary>Crop-field meshes by plateau (their vertex colours green up as the field is sprayed).</summary>
        public static Mesh[] CropFieldMeshes { get; private set; } = new Mesh[0];
        public static Mesh CropFieldMesh => CropFieldMeshes.Length > 0 ? CropFieldMeshes[0] : null;
        public static readonly Color Ploughed = new(0.36f, 0.25f, 0.15f), Sprayed = new(0.30f, 0.42f, 0.18f);

        /// <summary>Farmer's field north of the Valley runway: ploughed furrows along the runway heading, and the
        /// power line crossing it 100 yards from the south end (two poles outside the field, three conductors
        /// sagging to 100 ft AGL at mid-span).</summary>
        private static void BuildCropField(WorldTerrain t, Transform parent, int p)
        {
            CropField f = CropField.For(p);
            var root = new GameObject($"CropField{p}"); root.transform.SetParent(parent, false);
            int nx = CropField.CellsX, ny = CropField.CellsY;
            var verts = new Vector3[nx * ny * 4]; var cols = new Color[verts.Length]; var tris = new int[nx * ny * 6];
            int v = 0, k = 0; double elev = f.ElevationM + 0.05;
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double x0 = f.X0 + i * CropField.CellM, y0 = f.Y0 + j * CropField.CellM;
                // Furrows run along x: alternate cell rows darker.
                Color c = j % 2 == 0 ? Ploughed : Ploughed * 0.85f; c.a = 1f;
                int b = v;
                verts[v] = U(x0, y0, elev); verts[v + 1] = U(x0 + CropField.CellM, y0, elev);
                verts[v + 2] = U(x0, y0 + CropField.CellM, elev); verts[v + 3] = U(x0 + CropField.CellM, y0 + CropField.CellM, elev);
                for (int q = 0; q < 4; q++) cols[v + q] = c;
                v += 4;
                tris[k++] = b; tris[k++] = b + 2; tris[k++] = b + 1; tris[k++] = b + 1; tris[k++] = b + 2; tris[k++] = b + 3;
            }
            var mesh = new Mesh { name = "CropField", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.colors = cols; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (mesh.normals[0].y < 0) { System.Array.Reverse(tris); mesh.triangles = tris; mesh.RecalculateNormals(); }
            var go = new GameObject("Field"); go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat("FlyingGame/Terrain", Color.white);
            CropFieldMeshes[p] = mesh;

            // Power line: poles + crossarms, three conductors.
            var pole = new Color(0.45f, 0.4f, 0.35f); var wire = new Color(0.1f, 0.1f, 0.1f);
            double ground = t.HeightAt(f.WireX, f.PoleY0);
            foreach (double py in new[] { f.PoleY0, f.PoleY1 })
            {
                double g = t.HeightAt(f.WireX, py);
                var poleGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Kill(poleGo.GetComponent<Collider>());
                poleGo.name = "Pole"; poleGo.transform.SetParent(root.transform, false);
                poleGo.transform.position = U(f.WireX, py, g + CropField.PoleHeightM / 2);
                poleGo.transform.localScale = new Vector3(1.2f, (float)CropField.PoleHeightM / 2, 1.2f);
                poleGo.GetComponent<MeshRenderer>().sharedMaterial = Lit(pole);
                WBox(root, "Crossarm", U(f.WireX, py, g + CropField.PoleHeightM), new Vector3(0.4f, 0.4f, (float)(CropField.WireSpacingM * 2 + 1.0)), pole);
            }
            for (int w = -1; w <= 1; w++)
            {
                double wx = f.WireX + w * CropField.WireSpacingM;
                var lr = new GameObject($"Wire{w}").AddComponent<LineRenderer>();
                lr.transform.SetParent(root.transform, false);
                const int n = 33; var pts = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    double y = f.PoleY0 + (f.PoleY1 - f.PoleY0) * i / (n - 1);
                    pts[i] = U(wx, y, ground + f.WireAglAt(y));
                }
                lr.positionCount = n; lr.SetPositions(pts);
                lr.startWidth = lr.endWidth = 0.35f; lr.useWorldSpace = true; lr.alignment = LineAlignment.View;
                lr.sharedMaterial = Mat("FlyingGame/Lit", wire); lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ---- landmarks: arch over the river, tower with a road under it, the town ----------------------

        private static void BuildLandmarks(WorldTerrain t, Transform parent, int p)
        {
            var root = new GameObject($"Landmarks{p}"); root.transform.SetParent(parent, false);
            var steel = new Color(0.72f, 0.74f, 0.78f);
            // Plateau copy: the Valley layout shifted in y (Landmarks.*At(p)).
            double archY = Landmarks.ArchYAt(p), towerY = Landmarks.TowerYAt(p);
            // Gateway arch: tapered segments along the parabola, spanning the river in its gorge.
            double cx = Landmarks.ArchCentreXAt(p), baseUp = Landmarks.ArchBaseUp(t, p);
            const int n = 40;
            for (int i = 0; i < n; i++)
            {
                double u0 = -1 + 2.0 * i / n, u1 = -1 + 2.0 * (i + 1) / n;
                double x0 = u0 * Landmarks.ArchHalfSpanM, x1 = u1 * Landmarks.ArchHalfSpanM;
                double h0 = Landmarks.ArchHeightAt(x0), h1 = Landmarks.ArchHeightAt(x1);
                double um = (u0 + u1) / 2, w = Landmarks.ArchLegWidthM + (Landmarks.ArchTopWidthM - Landmarks.ArchLegWidthM) * (1 - um * um);
                Vector3 a = U(cx + x0, archY, baseUp + h0), b = U(cx + x1, archY, baseUp + h1);
                Beam(root, "ArchSeg", a, b, (float)w, (float)w, steel);
            }
            // Legs reach down to the wall they stand on.
            foreach (double sgn in new[] { -1.0, 1.0 })
            {
                double lx = cx + sgn * Landmarks.ArchHalfSpanM;
                Beam(root, "ArchLeg", U(lx, archY, t.HeightAt(lx, archY) - 25), U(lx, archY, baseUp + 2), (float)Landmarks.ArchLegWidthM, (float)Landmarks.ArchLegWidthM, steel);
            }

            // Eiffel-style tower: four curving legs meeting in a slim top, three floors; a road runs under it.
            var iron = new Color(0.45f, 0.33f, 0.25f);
            double tg = t.HeightAt(Landmarks.TowerX, towerY);
            foreach ((double sx, double sy) in new[] { (-1.0, -1.0), (-1.0, 1.0), (1.0, -1.0), (1.0, 1.0) })
            {
                const int segs = 14; double prevH = 0;
                for (int i = 1; i <= segs; i++)
                {
                    double h = Landmarks.TowerHeightM * i / segs;
                    double r0 = Landmarks.TowerHalfAt(prevH), r1 = Landmarks.TowerHalfAt(h);
                    float thick = (float)(6.0 * (1 - h / Landmarks.TowerHeightM) + 1.5);
                    Beam(root, "TowerLeg", U(Landmarks.TowerX + sx * r0, towerY + sy * r0, tg + prevH), U(Landmarks.TowerX + sx * r1, towerY + sy * r1, tg + h), thick, thick, iron);
                    // Lattice cross-braces between neighbouring legs at each segment.
                    if (sx < 0 && i <= segs)
                    {
                        Beam(root, "Brace", U(Landmarks.TowerX - r1, towerY + sy * r1, tg + h), U(Landmarks.TowerX + r1, towerY + sy * r1, tg + h), thick * 0.5f, thick * 0.5f, iron);
                        Beam(root, "Brace", U(Landmarks.TowerX + sy * r1, towerY - r1, tg + h), U(Landmarks.TowerX + sy * r1, towerY + r1, tg + h), thick * 0.5f, thick * 0.5f, iron);
                    }
                    prevH = h;
                }
            }
            foreach (double fh in new[] { Landmarks.TowerFirstFloorM, Landmarks.TowerSecondFloorM, Landmarks.TowerTopFloorM })
            {
                float half = (float)Landmarks.TowerHalfAt(fh) + 4f;
                WBox(root, "TowerFloor", U(Landmarks.TowerX, towerY, tg + fh), new Vector3(half * 2, fh > 200 ? 6f : 4f, half * 2), iron);
            }
            // First-floor arches (the road passes under them): a low curved beam between the legs on each side.
            foreach (double sy in new[] { -1.0, 1.0 })
            {
                double r = Landmarks.TowerHalfAt(0) * 0.9;
                for (int i = 0; i < 12; i++)
                {
                    double a0 = System.Math.PI * i / 12, a1 = System.Math.PI * (i + 1) / 12;
                    Beam(root, "TowerArch", U(Landmarks.TowerX - r * System.Math.Cos(a0), towerY + sy * Landmarks.TowerHalfAt(0) * 0.85, tg + 20 + 30 * System.Math.Sin(a0)),
                                            U(Landmarks.TowerX - r * System.Math.Cos(a1), towerY + sy * Landmarks.TowerHalfAt(0) * 0.85, tg + 20 + 30 * System.Math.Sin(a1)), 2.5f, 2.5f, iron);
                }
            }
            Slab(root.transform, "TowerRoad", Landmarks.TowerX, towerY, tg + 0.05, Landmarks.RoadHalfLengthM * 2, Landmarks.RoadWidthM, 0.08, 0, Asphalt);
            for (double d = -Landmarks.RoadHalfLengthM + 20; d < Landmarks.RoadHalfLengthM; d += 40)
                Slab(root.transform, "RoadDash", Landmarks.TowerX + d, towerY, tg + 0.11, 12, 0.4, 0.03, 0, Paint);

            // The city (owner 2026-10-05: "identical to the one you built in the combat zone"): the combat city's grid —
            // towers, sky-bridges, the fountain plaza with its crossing water cannons — on the old town site, on every plateau.
            var cityBatch = new SeasideBuilder.Batch();
            PlaygroundBuilder.BuildCityGrid(ValleyCity.At(p), t, cityBatch, root.transform);
            PlaygroundBuilder.BuildCityExtras(ValleyCity.At(p), t, cityBatch, root.transform);   // pad, rooftop rings, spinning ring
            cityBatch.Build("ValleyCity", root.transform, SeasideBuilder.VC);
        }

        /// <summary>A box beam from a to b (world Unity points) with the given cross-section.</summary>
        private static void Beam(GameObject parent, string name, Vector3 a, Vector3 b, float w, float h, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(go.GetComponent<Collider>());
            go.name = name; go.transform.SetParent(parent.transform, false);
            Vector3 d = b - a; float len = d.magnitude;
            go.transform.position = (a + b) * 0.5f;
            go.transform.rotation = len > 1e-4f ? Quaternion.LookRotation(d / len, Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up) : Quaternion.identity;
            go.transform.localScale = new Vector3(w, h, len + w * 0.5f);
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit(c);
        }

        /// <summary>A flat slab: sim centre (x,y), top at `up`, size along its heading (length) × across (width).</summary>
        private static GameObject Slab(Transform parent, string name, double x, double y, double up, double length, double width, double thick, double headingDeg, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = U(x, y, up - thick * 0.5);
            go.transform.rotation = Quaternion.Euler(0f, (float)headingDeg, 0f);
            go.transform.localScale = new Vector3((float)width, (float)thick, (float)length);
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit(c);
            return go;
        }

        /// <summary>Marking on a strip: local (along, across, up-offset) relative to the strip's centre/heading.</summary>
        private static void Child(GameObject strip, string name, double along, double across, double up, double length, double width, double thick, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(strip.transform, false);
            Vector3 s = strip.transform.localScale;
            go.transform.localPosition = new Vector3((float)(across / s.x), (float)(0.5 + up / s.y), (float)(along / s.z));
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3((float)(width / s.x), (float)(thick / s.y), (float)(length / s.z));
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit(c);
        }

        private static void Label(GameObject strip, string text, double along, double across, double up, float size)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(strip.transform, false);
            Vector3 s = strip.transform.localScale;
            bool onStrip = s.x > 0.99f && s.x < 1.01f ? false : true;
            if (onStrip)
                go.transform.localPosition = new Vector3((float)(across / s.x), (float)(0.5 + up / s.y), (float)(along / s.z));
            else
                go.transform.position = U(along, across, up);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat on the ground, readable heading north
            go.transform.localScale = onStrip ? new Vector3(1f / s.x, 1f / s.y, 1f / s.z) : Vector3.one;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text; tm.fontSize = 48; tm.characterSize = size * 0.1f; tm.anchor = TextAnchor.MiddleCenter; tm.color = Paint;
        }

        /// <summary>Windsocks (owner 2026-09-10): several along each side of every runway/strip, plus one by the apron and
        /// one at each far corner of the pad, all reading the live wind where they stand.</summary>
        private static void BuildWindsocks(Transform parent, WorldTerrain.Airport a)
        {
            var spots = new List<(double x, double y)>();
            foreach (WorldTerrain.Strip s in WorldTerrain.AirportStrips)
            {
                double h = s.HeadingDeg * System.Math.PI / 180, c = System.Math.Cos(h), sn = System.Math.Sin(h);
                double off = s.Width / 2 + 35;                       // clear of the edge lights, on both sides
                int n = s.Length > 1000 ? 3 : 2;                     // three along the long runways, two on the strips
                for (int i = 0; i < n; i++)
                {
                    double along = n == 1 ? 0 : -s.Length * 0.38 + s.Length * 0.76 * i / (n - 1);
                    foreach (double side in new[] { -1.0, 1.0 })
                    {
                        double across = side * off;
                        spots.Add((a.X + s.Dx + along * c - across * sn, a.Y + s.Dy + along * sn + across * c));
                    }
                }
            }
            spots.Add((a.X + WorldTerrain.ApronDx, a.Y + WorldTerrain.ApronDy + WorldTerrain.ApronWidthM / 2 + 25));
            spots.Add((a.X + WorldTerrain.PadHalfX - 60, a.Y + WorldTerrain.PadHalfY - 60));
            spots.Add((a.X - WorldTerrain.PadHalfX + 60, a.Y - WorldTerrain.PadHalfY + 60));
            foreach ((double x, double y) in spots) Windsock(parent, x, y, WorldTerrain.Active?.HeightAt(x, y) ?? a.ElevationM);
        }

        private static void Windsock(Transform parent, double x, double y, double ground)
        {
            var root = new GameObject("Windsock"); root.transform.SetParent(parent, false);
            root.transform.position = U(x, y, ground);
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Kill(pole.GetComponent<Collider>());
            pole.name = "Pole"; pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 3.5f, 0f); pole.transform.localScale = new Vector3(0.12f, 3.5f, 0.12f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = Lit(new Color(0.75f, 0.75f, 0.78f));
            // Ring at the top; the cone pivots there and points downwind (Windsock turns it).
            var pivot = new GameObject("Cone"); pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 7.0f, 0f);
            var orange = new Color(1f, 0.45f, 0.05f);
            // Five bands: orange, white, orange, white, orange — each a tapering cone segment along +z.
            float len = 3.6f, r0 = 0.45f, r1 = 0.18f;
            for (int i = 0; i < 5; i++)
            {
                float za = len * i / 5f, zb = len * (i + 1) / 5f;
                float ra = Mathf.Lerp(r0, r1, i / 5f), rb = Mathf.Lerp(r0, r1, (i + 1) / 5f);
                var seg = new GameObject($"Band{i}"); seg.transform.SetParent(pivot.transform, false);
                seg.transform.localPosition = new Vector3(0f, 0f, za);
                seg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // ConeMesh grows along local +y → +z
                seg.AddComponent<MeshFilter>().sharedMesh = ConeMesh(ra, rb, zb - za, 12);
                seg.AddComponent<MeshRenderer>().sharedMaterial = Lit(i % 2 == 0 ? orange : Color.white);
            }
            root.AddComponent<Windsock>().Cone = pivot.transform;
        }

        public static void BuildHangar(Transform parent, double cx, double cy, double elev)
        {
            const float halfW = 22f, height = 16f, halfL = 25f, wall = 0.6f;
            var wallCol = new Color(0.80f, 0.78f, 0.72f);
            var roofCol = new Color(0.42f, 0.44f, 0.48f);
            var trimCol = new Color(0.55f, 0.57f, 0.60f);
            var doorCol = new Color(0.22f, 0.34f, 0.56f);

            var root = new GameObject("Hangar");
            root.transform.SetParent(parent, false);
            root.transform.position = U(cx, cy, elev);

            Box(root, "Apron", new Vector3(0f, 0.02f, 0f), new Vector3(2f * halfW + 30f, 0.04f, 2f * halfL + 40f), Apron);
            Box(root, "WallWest", new Vector3(-halfW, height * 0.5f, 0f), new Vector3(wall, height, 2f * halfL), wallCol);
            Box(root, "WallEast", new Vector3(halfW, height * 0.5f, 0f), new Vector3(wall, height, 2f * halfL), wallCol);
            Box(root, "Roof", new Vector3(0f, height + wall * 0.5f, 0f), new Vector3(2f * halfW + 2f, wall, 2f * halfL + 2f), roofCol);
            Box(root, "FasciaN", new Vector3(0f, height - 0.8f, halfL), new Vector3(2f * halfW + 2f, 1.6f, wall), trimCol);
            Box(root, "FasciaS", new Vector3(0f, height - 0.8f, -halfL), new Vector3(2f * halfW + 2f, 1.6f, wall), trimCol);
            const float doorW = 13f, doorH = height - 1.2f;
            foreach (float zEnd in new[] { halfL, -halfL })
            {
                float zDoor = zEnd + Mathf.Sign(zEnd) * (wall + 0.35f);
                foreach (float side in new[] { -1f, 1f })
                {
                    float xDoor = side * (halfW + wall + doorW * 0.5f + 0.4f);
                    Box(root, "Door", new Vector3(xDoor, doorH * 0.5f, zDoor), new Vector3(doorW, doorH, 0.3f), doorCol);
                    Box(root, "DoorStripe", new Vector3(xDoor, doorH * 0.5f, zDoor + Mathf.Sign(zEnd) * 0.2f), new Vector3(doorW * 0.12f, doorH * 0.9f, 0.1f), new Color(0.85f, 0.88f, 0.92f));
                }
                Box(root, "DoorRail", new Vector3(0f, doorH + 0.5f, zDoor), new Vector3(2f * (halfW + wall + doorW + 0.8f), 0.6f, 0.5f), trimCol);
            }
        }

        private static void Box(GameObject parent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit(color);
        }
    }
}

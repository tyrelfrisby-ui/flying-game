using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Builds the visible world from <see cref="WorldTerrain"/> (the same height field the wheels use):
    /// the terrain mesh (valley + three canyon-wall steps + plateaus), lakes and the stepping river, and
    /// the four identical airports — two crossing paved runways, an open hangar, a dirt STOL strip with
    /// its landing line and distance marks, and a grass strip. Edit-mode safe (used by the editor's
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
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports) BuildAirport(a, root.transform);
            BuildBridge(WorldTerrain.Active, root.transform);
            BuildAeroBox(WorldTerrain.Active, root.transform);
            BuildRaceCourse(WorldTerrain.Active, root.transform);
            // Slope soaring on the first canyon wall (replaces the old stand-alone hill).
            double crestY = WorldTerrain.EdgeMeanY(0) - WorldTerrain.EscarpmentWidthM;
            Atmosphere.ActiveRidge = new Ridge(new FlyingGame.Core.MathTypes.Vec3(0, crestY, -WorldTerrain.StepHeightM),
                new FlyingGame.Core.MathTypes.Vec3(1, 0, 0), WorldTerrain.StepHeightM, 700);
            return root;
        }

        // ---- terrain mesh ------------------------------------------------------------------------

        private const double MinX = -7000, MaxX = 9000, MinY = -11500, MaxY = 6500;

        private static void BuildTerrain(WorldTerrain t, Transform parent)
        {
            const double step = 60.0;
            int nx = (int)((MaxX - MinX) / step) + 1, ny = (int)((MaxY - MinY) / step) + 1;
            var verts = new Vector3[nx * ny];
            var cols = new Color[nx * ny];
            var grass = new Color(0.42f, 0.55f, 0.28f);
            var dry = new Color(0.62f, 0.56f, 0.36f);
            var rock = new Color(0.47f, 0.42f, 0.37f);
            var snow = new Color(0.9f, 0.9f, 0.92f);
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                double x = MinX + i * step, y = MinY + j * step;
                double h = t.HeightAt(x, y);
                verts[j * nx + i] = U(x, y, h);
                // Colour by slope and height.
                double dhx = (t.HeightAt(x + 8, y) - t.HeightAt(x - 8, y)) / 16.0;
                double dhy = (t.HeightAt(x, y + 8) - t.HeightAt(x, y - 8)) / 16.0;
                float slope = (float)System.Math.Sqrt(dhx * dhx + dhy * dhy);
                float hf = Mathf.Clamp01((float)h / 2800f);
                Color c = Color.Lerp(grass, dry, hf * 0.9f);
                if (h > 2500) c = Color.Lerp(c, snow, Mathf.Clamp01((float)(h - 2500) / 300f));
                float rockiness = Mathf.Clamp01((slope - 0.35f) / 0.6f);
                c = Color.Lerp(c, rock, rockiness);
                c.a = 1f - Mathf.Clamp01(slope / 0.08f); // flatness → field grid lines
                cols[j * nx + i] = c;
            }
            var tris = new int[(nx - 1) * (ny - 1) * 6];
            int k = 0;
            for (int j = 0; j < ny - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                // Unity is left-handed: (east, up, north). Winding for an upward normal.
                tris[k++] = a; tris[k++] = b; tris[k++] = c;
                tris[k++] = b; tris[k++] = d; tris[k++] = c;
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "Terrain" };
            mesh.vertices = verts; mesh.colors = cols; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Terrain");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat("FlyingGame/Terrain", Color.white);
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
            for (double y = MinY + 200; y <= MaxY - 200; y += 25)
            {
                double cx = WorldTerrain.RiverCentreX(y);
                double surf = t.WaterSurfaceAt(cx, y) ?? (t.BaseHeightAt(cx, y) - 2.0);
                rv.Add(U(cx - halfW, y, surf)); rv.Add(U(cx + halfW, y, surf));
                if (n > 0)
                {
                    int b = rv.Count - 4;
                    rt.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
                n++;
            }
            Spawn("River", rv.ToArray(), rt.ToArray(), water, parent, true);
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

        private static void BuildBridge(WorldTerrain t, Transform parent)
        {
            double y = WorldTerrain.BridgeY;
            double cx = WorldTerrain.RiverCentreX(y);
            double halfSpan = WorldTerrain.GorgeHalfWidthAt(y) + 60;
            double deck = t.BaseHeightAt(cx - halfSpan - 40, y) + 1.5;   // rim level
            var root = new GameObject("Bridge");
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

        private static void BuildAeroBox(WorldTerrain t, Transform parent)
        {
            var root = new GameObject("AeroBox");
            root.transform.SetParent(parent, false);
            double cx = AeroBox.CenterX, cy = AeroBox.CenterY, h = AeroBox.SizeM / 2;
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
        }

        // ---- Air Racing course: gate cones, turning pylons, numbered cloud hoops --------------------

        private static void BuildRaceCourse(WorldTerrain t, Transform parent)
        {
            var root = new GameObject("RaceCourse");
            root.transform.SetParent(parent, false);
            var blue = new Color(0.15f, 0.35f, 0.85f); var red = new Color(0.85f, 0.15f, 0.15f); var white = Color.white;
            for (int i = 0; i < RaceCourse.Elements.Length; i++)
            {
                RaceElement e = RaceCourse.Elements[i];
                double g = t.HeightAt(e.X, e.Y);
                if (e.Kind == RaceElement.Kinds.Gate)
                {
                    var r = e.Right;
                    Cone(root, U(e.X + r.X * RaceElement.GateHalfWidthM, e.Y + r.Y * RaceElement.GateHalfWidthM, g), (float)RaceElement.GateHeightM, blue, white);
                    Cone(root, U(e.X - r.X * RaceElement.GateHalfWidthM, e.Y - r.Y * RaceElement.GateHalfWidthM, g), (float)RaceElement.GateHeightM, blue, white);
                }
                else
                {
                    Cone(root, U(e.X, e.Y, g), (float)RaceElement.GateHeightM, red, white);
                }
                // Element number on the ground.
                var lbl = new GameObject("Num"); lbl.transform.SetParent(root.transform, false);
                lbl.transform.position = U(e.X - e.Forward.X * 25, e.Y - e.Forward.Y * 25, g + 0.15);
                lbl.transform.rotation = Quaternion.Euler(90f, (float)e.HeadingDeg, 0f);
                var tm = lbl.AddComponent<TextMesh>(); tm.text = (i + 1).ToString(); tm.fontSize = 48; tm.characterSize = 1.0f; tm.anchor = TextAnchor.MiddleCenter; tm.color = white;
            }
            // Cloud hoops (optional guides), numbered.
            var cloud = Mat("FlyingGame/UnlitTransparent", new Color(1f, 1f, 1f, 0.55f));
            Mesh ring = Torus((float)RaceCourse.HoopRadiusM, 3.5f, 40, 10);
            foreach ((FlyingGame.Core.MathTypes.Vec3 pos, double hdg, int number) in RaceCourse.Hoops())
            {
                var go = new GameObject($"Hoop{number}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = U(pos.X, pos.Y, -pos.Z);
                go.transform.rotation = Quaternion.Euler(0f, (float)hdg, 0f); // ring plane faces the leg direction
                go.AddComponent<MeshFilter>().sharedMesh = ring;
                go.AddComponent<MeshRenderer>().sharedMaterial = cloud;
                var lbl = new GameObject("Num"); lbl.transform.SetParent(go.transform, false);
                lbl.transform.localPosition = new Vector3(0f, (float)RaceCourse.HoopRadiusM + 8f, 0f);
                lbl.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // readable from the approach side
                var tm = lbl.AddComponent<TextMesh>(); tm.text = number.ToString(); tm.fontSize = 64; tm.characterSize = 2.2f; tm.anchor = TextAnchor.MiddleCenter; tm.color = white;
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

        private static Mesh Torus(float R, float r, int segs, int rings)
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

        private static readonly Color Asphalt = new(0.24f, 0.24f, 0.26f), Dirt = new(0.55f, 0.42f, 0.28f),
            Grass = new(0.36f, 0.52f, 0.22f), Paint = Color.white, Apron = new(0.62f, 0.62f, 0.60f);

        private static void BuildAirport(WorldTerrain.Airport a, Transform parent)
        {
            var root = new GameObject($"Airport-{a.Name}");
            root.transform.SetParent(parent, false);
            foreach (WorldTerrain.Strip s in WorldTerrain.AirportStrips)
            {
                Color c = s.Kind == "paved" ? Asphalt : s.Kind == "dirt" ? Dirt : Grass;
                GameObject strip = Slab(root.transform, $"Strip-{s.Kind}", a.X + s.Dx, a.Y + s.Dy, a.ElevationM + 0.04, s.Length, s.Width, 0.06, s.HeadingDeg, c);
                if (s.Kind == "paved")
                {
                    // Centreline dashes + threshold bars.
                    for (double d = -s.Length * 0.5 + 60; d < s.Length * 0.5 - 60; d += 36)
                    {
                        Child(strip, "Dash", d, 0, 0.05, 16, 0.6, 0.03, Paint);
                    }
                    Child(strip, "Thresh", -s.Length * 0.5 + 20, 0, 0.05, 12, s.Width * 0.9, 0.03, Paint);
                    Child(strip, "Thresh", s.Length * 0.5 - 20, 0, 0.05, 12, s.Width * 0.9, 0.03, Paint);
                }
                if (s.Kind == "dirt")
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
            // Field name on the apron.
            Label(root, a.Name.ToUpperInvariant(), a.X + WorldTerrain.HangarDx + 60, a.Y + WorldTerrain.HangarDy - 70, a.ElevationM + 0.1, 8f);
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

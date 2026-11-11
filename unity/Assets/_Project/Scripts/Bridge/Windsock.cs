using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;
using B = FlyingGame.Bridge.SeasideBuilder.Batch;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Draws and moves every windsock in the world: the ONE place the shared part (<see cref="WindsockPart"/>) becomes
    /// geometry. Masts and spindles are static, batched by 2 km region like the rest of the scenery; each sock is one
    /// small mesh (five bands, inside and out, and its hoop) re-shaped every frame from its own
    /// <see cref="WindsockDynamics"/>, stepped in the air at its hoop (<see cref="WindsockPart.AirAt"/>, the query the
    /// aeroplanes fly in). Socks beyond <see cref="UpdateRangeM"/> of the camera hold still (a 3.7 m sock is a few
    /// pixels there) and beyond <see cref="DrawRangeM"/> are not drawn.
    /// </summary>
    public sealed class Windsock : MonoBehaviour
    {
        public const float UpdateRangeM = 2500f, DrawRangeM = 4000f;
        private const int Sides = 12, HoopSegs = 16, HoopSides = 4;
        private const float HoopTubeM = .025f, SpindleRadiusM = .03f;
        private static readonly Color Orange = new(1f, .40f, .05f), White = new(.95f, .95f, .92f), Steel = new(.70f, .71f, .73f), Concrete = new(.62f, .62f, .60f);

        private sealed class Sock
        {
            public WindsockPart.Site Site; public WindsockDynamics Motion; public Vector3 Pos;
            public Mesh Mesh; public MeshRenderer Renderer;
        }
        private readonly List<Sock> _socks = new();
        // Scratch, shared by every sock (one is re-shaped at a time): no garbage per frame.
        private static readonly int FabricVerts = WindsockPart.Bands * 2 * Sides * 2, HoopVerts = HoopSegs * HoopSides;
        private readonly Vector3[] _v = new Vector3[FabricVerts + HoopVerts], _n = new Vector3[FabricVerts + HoopVerts];
        private readonly Vector3[] _joint = new Vector3[WindsockPart.Bands + 1], _dir = new Vector3[WindsockPart.Bands];

        public int Count => _socks.Count;

        /// <summary>Build every sock in <paramref name="sites"/> under <paramref name="parent"/>.</summary>
        public static Windsock Build(Transform parent, IReadOnlyList<WindsockPart.Site> sites, Material lit)
        {
            var root = new GameObject("Windsocks"); root.transform.SetParent(parent, false);
            var w = root.AddComponent<Windsock>();
            var batches = new Dictionary<string, B>();
            float r0 = (float)WindsockPart.ThroatRadiusM, pivotUp = (float)WindsockPart.PivotHeightM;
            foreach (var s in sites)
            {
                string key = Mathf.Floor((float)(s.X / 2000)) + ":" + Mathf.Floor((float)(s.Y / 2000));
                if (!batches.TryGetValue(key, out var b)) { b = new B(); batches.Add(key, b); }
                Vector3 foot = WorldBuilder.U(s.X, s.Y, s.GroundM);
                b.Box(foot + Vector3.up * .05f, new Vector3(.9f, .3f, .9f), Concrete);                       // footing, sunk 10 cm
                b.Cylinder(foot, (float)WindsockPart.MastRadiusM, pivotUp - r0, 8, Steel);                   // mast to the hoop's foot
                b.Cylinder(foot + Vector3.up * (pivotUp - r0), SpindleRadiusM, 2 * r0 + .08f, 6, Steel);     // swivel spindle up the hoop
                w.Add(s, root.transform, lit);
            }
            foreach (var kv in batches) kv.Value.Build("Windsock-masts-" + kv.Key, root.transform, lit);
            return w;
        }

        private void Add(WindsockPart.Site s, Transform parent, Material lit)
        {
            var go = new GameObject(s.Id); go.transform.SetParent(parent, false);
            go.transform.position = WorldBuilder.U(s.X, s.Y, s.GroundM + WindsockPart.PivotHeightM);
            var mesh = new Mesh { name = s.Id };
            mesh.MarkDynamic();
            mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetColors(Colours()); mesh.SetTriangles(Triangles(), 0);
            float reach = (float)(WindsockPart.SockLengthM + WindsockPart.ThroatRadiusM + .1);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (2 * reach));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = lit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Start where the air at build time leaves it: downwind and filled, not swinging in from north.
            Vec3 air = WindsockPart.AirAt(s.Pivot);
            var motion = new WindsockDynamics(System.Math.Atan2(air.Y, air.X) * 180 / System.Math.PI);
            motion.Step(air, Atmosphere.DensityAt(s.Pivot), 5);
            var sock = new Sock { Site = s, Motion = motion, Pos = go.transform.position, Mesh = mesh, Renderer = r };
            _socks.Add(sock);
            Shape(sock);
        }

        private void Update()
        {
            var cam = Camera.main;
            Step(Mathf.Min(Time.deltaTime, .1f), cam != null ? cam.transform.position : Vector3.zero);
        }

        /// <summary>Advance every sock near <paramref name="eye"/> by dt (s). Editor shot tools call this directly
        /// (Update does not run in edit mode).</summary>
        public void Step(float dt, Vector3 eye)
        {
            float update2 = UpdateRangeM * UpdateRangeM, draw2 = DrawRangeM * DrawRangeM;
            for (int i = 0; i < _socks.Count; i++)
            {
                var s = _socks[i];
                float d2 = (s.Pos - eye).sqrMagnitude;
                bool draw = d2 < draw2;
                if (s.Renderer.enabled != draw) s.Renderer.enabled = draw;
                if (d2 > update2 || dt <= 0) continue;
                Vec3 pivot = s.Site.Pivot;
                s.Motion.Step(WindsockPart.AirAt(pivot), Atmosphere.DensityAt(pivot), dt);
                Shape(s);
            }
        }

        /// <summary>Settle every sock in the air as it is now (for shot tools that change the wind).</summary>
        public void SettleForTesting(double seconds)
        {
            foreach (var s in _socks) { s.Motion.Step(WindsockPart.AirAt(s.Site.Pivot), Atmosphere.DensityAt(s.Site.Pivot), seconds); Shape(s); }
        }

        /// <summary>The centreline: from the hoop, band by band, each band at its own pitch (WindsockDynamics), in the
        /// vertical plane of the azimuth.</summary>
        private void Shape(Sock s)
        {
            var m = s.Motion;
            float psi = (float)(m.AzimuthDeg * System.Math.PI / 180);
            Vector3 h = new(Mathf.Sin(psi), 0, Mathf.Cos(psi)), side = new(Mathf.Cos(psi), 0, -Mathf.Sin(psi));
            float band = (float)(WindsockPart.SockLengthM / WindsockPart.Bands);
            _joint[0] = Vector3.zero;
            for (int i = 0; i < WindsockPart.Bands; i++)
            {
                float p = (float)(WindsockDynamics.BandPitchDeg(i, m.Fill, m.FlowPitchDeg) * System.Math.PI / 180);
                _dir[i] = h * Mathf.Cos(p) - Vector3.up * Mathf.Sin(p);
                _joint[i + 1] = _joint[i] + _dir[i] * band;
            }
            int o = 0;
            for (int i = 0; i < WindsockPart.Bands; i++)
            for (int end = 0; end < 2; end++)
            {
                int k = i + end;   // joint index
                Vector3 t = k == 0 ? _dir[0] : k == WindsockPart.Bands ? _dir[k - 1] : (_dir[k - 1] + _dir[k]).normalized;
                Vector3 nrm = Vector3.Cross(t, side);
                // An empty band's cloth hangs in folds, narrower than a filled one; the hoop holds the throat open.
                double fillAfter = k == 0 ? 1 : System.Math.Clamp(m.Fill * WindsockPart.Bands - System.Math.Min(k, WindsockPart.Bands - 1), 0, 1);
                float r = (float)((WindsockPart.ThroatRadiusM + (WindsockPart.TailRadiusM - WindsockPart.ThroatRadiusM) * k / WindsockPart.Bands)
                                  * (k == 0 ? 1 : .55 + .45 * fillAfter));
                for (int j = 0; j < Sides; j++)
                {
                    float a = j * Mathf.PI * 2 / Sides;
                    Vector3 out1 = side * Mathf.Cos(a) + nrm * Mathf.Sin(a);
                    _v[o] = _joint[k] + out1 * r; _n[o] = out1;                               // outside
                    _v[o + Sides] = _joint[k] + out1 * (r - .004f); _n[o + Sides] = -out1;     // inside
                    o++;
                }
                o += Sides;
            }
            // The hoop: a steel ring round the throat, square to the first band.
            Vector3 axis = _dir[0], hn = Vector3.Cross(axis, side);
            float R = (float)WindsockPart.ThroatRadiusM;
            for (int i = 0; i < HoopSegs; i++)
            {
                float a = i * Mathf.PI * 2 / HoopSegs; Vector3 c = side * Mathf.Cos(a) + hn * Mathf.Sin(a);
                for (int k = 0; k < HoopSides; k++)
                {
                    float b = k * Mathf.PI * 2 / HoopSides; Vector3 n = c * Mathf.Cos(b) + axis * Mathf.Sin(b);
                    _v[o] = c * R + n * HoopTubeM; _n[o] = n; o++;
                }
            }
            s.Mesh.vertices = _v; s.Mesh.normals = _n;
        }

        private static Color[] Colours()
        {
            var c = new Color[FabricVerts + HoopVerts]; int o = 0;
            for (int i = 0; i < WindsockPart.Bands; i++)
            for (int k = 0; k < 2 * Sides * 2; k++) c[o++] = i % 2 == 0 ? Orange : White;
            while (o < c.Length) c[o++] = Steel;
            return c;
        }

        private static int[] Triangles()
        {
            var t = new List<int>();
            for (int i = 0; i < WindsockPart.Bands; i++)
            {
                int a = i * 4 * Sides, b = a + 2 * Sides;   // ring at the band's start / end (outside; inside at +Sides)
                for (int j = 0; j < Sides; j++)
                {
                    int j1 = (j + 1) % Sides;
                    t.Add(a + j); t.Add(b + j); t.Add(b + j1); t.Add(a + j); t.Add(b + j1); t.Add(a + j1);                     // outside
                    t.Add(a + Sides + j); t.Add(b + Sides + j1); t.Add(b + Sides + j); t.Add(a + Sides + j); t.Add(a + Sides + j1); t.Add(b + Sides + j1); // inside
                }
            }
            int h = FabricVerts;
            for (int i = 0; i < HoopSegs; i++)
            for (int k = 0; k < HoopSides; k++)
            {
                int i1 = (i + 1) % HoopSegs, k1 = (k + 1) % HoopSides;
                int p00 = h + i * HoopSides + k, p10 = h + i1 * HoopSides + k, p11 = h + i1 * HoopSides + k1, p01 = h + i * HoopSides + k1;
                t.Add(p00); t.Add(p01); t.Add(p11); t.Add(p00); t.Add(p11); t.Add(p10);
            }
            return t.ToArray();
        }
    }
}

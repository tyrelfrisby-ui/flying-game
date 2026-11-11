using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;
using B = FlyingGame.Bridge.SeasideBuilder.Batch;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Draws every airport light in the world: the ONE place the shared parts in <see cref="AirportLighting"/> (the PAPI,
    /// the 2-bar VASI, the runway edge light) become geometry. Housings, piers and lamp stalks are static, batched by 2 km
    /// region. Each PAPI / VASI row is one small mesh of lens faces whose colour is worked out every frame from the angle
    /// between each lens and the camera (<see cref="GlidePathUnit.WhiteFraction"/>, <see cref="GlidePathUnit.Intensity"/>):
    /// nothing is painted red or white. Edge and threshold lights shine only at dusk or night, through the
    /// <c>FlyingGame/AirportGlow</c> halos, each face masked to its own side of the lamp by the shader.
    /// </summary>
    public sealed class AirportLights : MonoBehaviour
    {
        /// <summary>Rows further than this from the camera are not re-coloured (a PAPI is a few pixels at 15 km).</summary>
        public const float UpdateRangeM = 15000f;
        private static readonly Color Red = new(1f, .03f, .015f), White = new(1f, .97f, .90f), Dark = new(.06f, .06f, .06f);
        private static readonly Color Steel = new(.55f, .56f, .57f), Housing = new(.78f, .74f, .62f), Concrete = new(.62f, .62f, .60f);
        private static Material _lens, _glow;

        private sealed class Row
        {
            public GlidePathUnit[] Units; public Vector3 Centre;
            public Mesh Lens, Halo; public Color[] LensColours, HaloColours; public int LensVertsPerUnit;
        }
        private readonly List<Row> _rows = new();
        private readonly List<MeshRenderer> _halos = new();

        public int RowCount => _rows.Count;

        public static AirportLights Build(Transform parent, Material lit)
        {
            var root = new GameObject("AirportLights"); root.transform.SetParent(parent, false);
            var w = root.AddComponent<AirportLights>();
            if (_lens == null) { _lens = WorldBuilder.Mat("FlyingGame/LitVC", Color.white); _lens.SetFloat("_Emission", 1); }
            if (_glow == null)
            {
                _glow = new Material(Shader.Find("FlyingGame/AirportGlow"));
                string tier = QualitySettings.names[QualitySettings.GetQualityLevel()];
                bool low = tier.IndexOf("low", System.StringComparison.OrdinalIgnoreCase) >= 0 || tier.IndexOf("fast", System.StringComparison.OrdinalIgnoreCase) >= 0;
                _glow.SetFloat("_HaloPixels", low ? 1.5f : Application.isMobilePlatform ? 2.0f : 3.0f);
            }
            var batches = new Dictionary<string, B>();
            var halos = new Dictionary<string, HaloBatch>();
            B BatchAt(double x, double y)
            {
                string key = Mathf.Floor((float)(x / 2000)) + ":" + Mathf.Floor((float)(y / 2000));
                if (!batches.TryGetValue(key, out var b)) { b = new B(); batches.Add(key, b); }
                return b;
            }
            HaloBatch HalosAt(double x, double y)
            {
                string key = Mathf.Floor((float)(x / 2000)) + ":" + Mathf.Floor((float)(y / 2000));
                if (!halos.TryGetValue(key, out var h)) { h = new HaloBatch(); halos.Add(key, h); }
                return h;
            }

            // Runway edge, threshold and end lights.
            foreach (var l in AirportLighting.EdgeLamps)
            {
                Vector3 foot = WorldBuilder.U(l.Point.X, l.Point.Y, l.GroundM);
                float up = (float)EdgeLightPart.LensHeightM;
                var b = BatchAt(l.Point.X, l.Point.Y);
                b.Cylinder(foot, .035f, up - .06f, 6, Steel);
                b.Cylinder(foot + Vector3.up * (up - .06f), .07f, .14f, 8, LensTint(l.TowardPlusU), LensTint(l.Omni ? l.TowardPlusU : l.TowardMinusU));
                Vector3 lens = foot + Vector3.up * up, along = Along(l.HeadingDeg);
                var h = HalosAt(l.Point.X, l.Point.Y);
                Vector3 rowStep = l.Threshold ? Vector3.zero : along * (float)EdgeLightPart.MaxSpacingM;
                if (l.Omni) h.Add(lens, Glow(l.TowardPlusU), Vector3.zero, rowStep);
                else
                {
                    h.Add(lens, Glow(l.TowardPlusU), along, rowStep);
                    h.Add(lens, Glow(l.TowardMinusU), -along, rowStep);
                }
            }

            // PAPI and VASI rows: static housings and piers, one dynamic lens mesh per row.
            var rows = new Dictionary<string, List<GlidePathUnit>>();
            foreach (var u in AirportLighting.GlidePathUnits)
            {
                string key = u.FieldId + ":" + u.Runway;
                if (!rows.TryGetValue(key, out var list)) { list = new List<GlidePathUnit>(); rows.Add(key, list); }
                list.Add(u);
                Vector3 size = ToSize(u.Kind == GlidePathAid.Papi ? PapiPart.BoxSizeM : VasiPart.BoxSizeM);
                Vector3 foot = WorldBuilder.U(u.Point.X, u.Point.Y, u.GroundM);
                Vector3 centre = WorldBuilder.U(u.Point.X, u.Point.Y, u.LensM);
                Quaternion rot = Quaternion.LookRotation(Along(u.FacingDeg), Vector3.up);
                var b = BatchAt(u.Point.X, u.Point.Y);
                b.Box(foot + Vector3.up * .05f, new Vector3(size.x + .4f, .2f, size.z + .4f), rot, Concrete);
                foreach (int side in new[] { -1, 1 })   // two legs from the pad up to the housing
                {
                    Vector3 leg = foot + rot * new Vector3(side * size.x * .35f, 0, 0);
                    b.Cylinder(leg, .05f, (centre.y - size.y / 2) - foot.y, 6, Steel);
                }
                b.Box(centre, size, rot, Housing);
            }
            foreach (var kv in rows) w.AddRow(kv.Key, kv.Value.ToArray(), root.transform);
            foreach (var kv in batches) kv.Value.Build("AirportLights-fixtures-" + kv.Key, root.transform, lit);
            foreach (var kv in halos) w._halos.Add(kv.Value.Build("AirportLights-halos-" + kv.Key, root.transform, _glow));
            Debug.Log($"[AirportLights] {AirportLighting.EdgeLamps.Count} runway lights, {rows.Count} glide path aids ({AirportLighting.GlidePathUnits.Count} boxes)");
            return w;
        }

        private void AddRow(string id, GlidePathUnit[] units, Transform parent)
        {
            var row = new Row { Units = units, LensVertsPerUnit = 4 };
            var go = new GameObject("GlidePath-" + id); go.transform.SetParent(parent, false);
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            var hv = new List<Vector3>(); var hn = new List<Vector3>(); var huv = new List<Vector2>(); var ht = new List<int>();
            Vector3 sum = Vector3.zero;
            foreach (var u in units)
            {
                Vector3 size = ToSize(u.Kind == GlidePathAid.Papi ? PapiPart.BoxSizeM : VasiPart.BoxSizeM);
                Vector3 c = WorldBuilder.U(u.Point.X, u.Point.Y, u.LensM); sum += c;
                Vector3 f = Along(u.FacingDeg), r = Vector3.Cross(Vector3.up, f);
                Vector3 face = c + f * (size.z / 2 + .01f);
                float hw = size.x * .42f, hh = size.y * .28f;
                int v = verts.Count;
                verts.Add(face - r * hw - Vector3.up * hh); verts.Add(face - r * hw + Vector3.up * hh);
                verts.Add(face + r * hw + Vector3.up * hh); verts.Add(face + r * hw - Vector3.up * hh);
                for (int k = 0; k < 4; k++) norms.Add(f);
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 1); tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
                int h = hv.Count;
                foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) })
                { hv.Add(face + f * .05f); hn.Add(f); huv.Add(corner); }
                ht.Add(h); ht.Add(h + 1); ht.Add(h + 2); ht.Add(h); ht.Add(h + 2); ht.Add(h + 3);
            }
            row.Centre = sum / units.Length;
            row.LensColours = new Color[verts.Count]; row.HaloColours = new Color[hv.Count];
            row.Lens = new Mesh { name = "GlidePath-lens-" + id }; row.Lens.MarkDynamic();
            row.Lens.SetVertices(verts); row.Lens.SetNormals(norms); row.Lens.colors = row.LensColours; row.Lens.SetTriangles(tris, 0); row.Lens.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = row.Lens;
            var lr = go.AddComponent<MeshRenderer>(); lr.sharedMaterial = _lens; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var hgo = new GameObject("GlidePath-halo-" + id); hgo.transform.SetParent(parent, false);
            row.Halo = new Mesh { name = "GlidePath-halo-" + id }; row.Halo.MarkDynamic();
            row.Halo.SetVertices(hv); row.Halo.SetNormals(hn); row.Halo.SetUVs(0, huv); row.Halo.SetUVs(1, new List<Vector3>(new Vector3[hv.Count]));
            row.Halo.colors = row.HaloColours; row.Halo.SetTriangles(ht, 0);
            row.Halo.RecalculateBounds(); var bounds = row.Halo.bounds; bounds.Expand(52); row.Halo.bounds = bounds;
            hgo.AddComponent<MeshFilter>().sharedMesh = row.Halo;
            var hr = hgo.AddComponent<MeshRenderer>(); hr.sharedMaterial = _glow;
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; hr.receiveShadows = false;
            _halos.Add(hr);
            _rows.Add(row);
            Colour(row, new Vec3(0, 0, -1e5));
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) Step(cam.transform.position);
        }

        /// <summary>Re-colour every row near <paramref name="eye"/> (Unity world position) from that eye. Editor shot
        /// tools call this directly (LateUpdate does not run in edit mode).</summary>
        public void Step(Vector3 eye)
        {
            float glow = Mathf.Max(Shader.GetGlobalFloat("_AirportDusk"), Mathf.Max(Shader.GetGlobalFloat("_SunsetOn") * .78f, Shader.GetGlobalFloat("_CityNight")));
            for (int i = 0; i < _halos.Count; i++) if (_halos[i].enabled != glow > 0) _halos[i].enabled = glow > 0;
            Vec3 ned = new(eye.z, eye.x, -eye.y);
            float range2 = UpdateRangeM * UpdateRangeM;
            for (int i = 0; i < _rows.Count; i++)
                if ((_rows[i].Centre - eye).sqrMagnitude < range2) Colour(_rows[i], ned);
        }

        private static void Colour(Row row, Vec3 eye)
        {
            for (int i = 0; i < row.Units.Length; i++)
            {
                var u = row.Units[i];
                float white = (float)u.WhiteFraction(eye), seen = (float)u.Intensity(eye);
                Color lamp = Color.Lerp(Red, White, white);
                Color lens = Color.Lerp(Dark, lamp, seen);
                Color halo = new(lamp.r * seen, lamp.g * seen, lamp.b * seen, .8f);   // .8: the shader's PAPI-sized core
                for (int k = 0; k < row.LensVertsPerUnit; k++) { row.LensColours[i * 4 + k] = lens; row.HaloColours[i * 4 + k] = halo; }
            }
            row.Lens.colors = row.LensColours; row.Halo.colors = row.HaloColours;
        }

        /// <summary>Unity direction of a true heading (sim x north = Unity z, sim y east = Unity x).</summary>
        private static Vector3 Along(double headingDeg)
        {
            float h = (float)(headingDeg * System.Math.PI / 180);
            return new Vector3(Mathf.Sin(h), 0, Mathf.Cos(h));
        }
        private static Vector3 ToSize(Vec3 s) => new((float)s.X, (float)s.Y, (float)s.Z);   // across, up, along

        private static Color LensTint(LampColour c) => c switch
        {
            LampColour.Yellow => new Color(.85f, .70f, .25f),
            LampColour.Green => new Color(.30f, .62f, .40f),
            LampColour.Red => new Color(.70f, .25f, .22f),
            _ => new Color(.86f, .86f, .82f),
        };
        // AirportGlow reads alpha as a fixture code: 1 a plain lamp, .95 a threshold green (brighter core).
        private static Color Glow(LampColour c) => c switch
        {
            LampColour.Yellow => new Color(1f, .74f, .12f, 1f),
            LampColour.Green => new Color(.03f, 1f, .23f, .95f),
            LampColour.Red => new Color(1f, .035f, .015f, 1f),
            _ => new Color(1f, .96f, .86f, 1f),
        };

        /// <summary>Halo cards for AirportGlow: four vertices at the lamp, a facing normal (zero = all round), and the
        /// row's spacing vector in UV1 so the shader can keep a foreshortened row from merging into a rail.</summary>
        private sealed class HaloBatch
        {
            private readonly List<Vector3> _v = new(), _n = new(), _step = new();
            private readonly List<Vector2> _uv = new(); private readonly List<Color> _c = new(); private readonly List<int> _t = new();
            private static readonly Vector2[] Corners = { new(-1, -1), new(-1, 1), new(1, 1), new(1, -1) };
            public void Add(Vector3 p, Color c, Vector3 facing, Vector3 rowStep)
            {
                int v = _v.Count;
                for (int k = 0; k < 4; k++) { _v.Add(p); _n.Add(facing); _uv.Add(Corners[k]); _c.Add(c); _step.Add(rowStep); }
                _t.Add(v); _t.Add(v + 1); _t.Add(v + 2); _t.Add(v); _t.Add(v + 2); _t.Add(v + 3);
            }
            public MeshRenderer Build(string name, Transform parent, Material m)
            {
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetUVs(0, _uv); mesh.SetUVs(1, _step); mesh.SetColors(_c); mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds(); var b = mesh.bounds; b.Expand(52); mesh.bounds = b;
                var go = new GameObject(name); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                return r;
            }
        }
    }
}

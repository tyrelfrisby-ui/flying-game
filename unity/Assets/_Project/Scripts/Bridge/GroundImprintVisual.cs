using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The marks the ground keeps, drawn (docs/GROUND-IMPRINTS.md). Ty 2026-11-10: *"if it is penetrating it should
    /// leave imprints behind as it slides and rolls displacing the dirt or snow etc"*.
    ///
    /// **Almost nothing happens here**, and that is the design. The record, the soil mechanics and the geometry are all
    /// engine-free in Core (<see cref="GroundImprints"/>, <see cref="GroundImprintDrawing"/>,
    /// <see cref="GroundImprintMesh"/>), so iOS, the Mac, the Quest and the Aero widget draw the same marks from the
    /// same code. This component is the three things only an engine can do: convert sim NED to Unity's frame, hand the
    /// vertices to a <see cref="Mesh"/>, and choose how often.
    ///
    /// One <see cref="MeshRenderer"/>, one material, one draw call for every mark in the world — 18,432 triangles at the
    /// ring's full 1,024 stretches, which is a fraction of one of ForestBuilder's detail trees. **No particles**: a
    /// standing prohibition of Ty's, and a mark is geometry lying on the ground.
    ///
    /// The field is process-wide, so this draws EVERY aeroplane's marks, player and AI — you can see where the other
    /// one landed. <see cref="StructuralDamage.Repair"/> clears it for a new flight, beside Forest and StrippedTrees.
    /// </summary>
    public sealed class GroundImprintVisual : MonoBehaviour
    {
        /// <summary>
        /// How often the geometry is brought up to date, s. 20 Hz.
        ///
        /// The RECORD is exact every physics step; this is only how often the picture catches up. A wheel rolling at
        /// 14 m/s leaves the growing end of its rut up to 0.7 m behind itself between refreshes — under the aeroplane,
        /// where it cannot be seen — and the vertex traffic drops by the ratio of the physics rate to this one.
        /// Without it a landing roll re-uploads 16,384 vertices on every step of the step loop.
        /// </summary>
        public const float RefreshIntervalS = 1f / 20f;

        private GroundImprintMesh _geometry;
        private Mesh _mesh;
        private Material _material;
        private GameObject _go;
        private MeshRenderer _renderer;
        private List<Vector3> _verts;
        private List<Vector3> _normals;
        private List<Color32> _colours;
        private float _nextRefresh;

        /// <summary>Marks with geometry in the mesh, for a test or a render fixture to assert on.</summary>
        public int DrawnMarks => _geometry != null ? _geometry.Count : 0;
        /// <summary>Triangles the last refresh left in the mesh.</summary>
        public int DrawnTriangles => _geometry != null ? _geometry.Triangles : 0;
        /// <summary>Is there a material to draw the marks WITH? False means the shader was missing, the ground is
        /// still keeping its marks and nothing is drawing them — which on screen is indistinguishable from a flight
        /// that left no marks, so a render fixture must fail on this rather than shoot a plausible empty field.</summary>
        public bool Drawable => _material != null;

        /// <summary>Put one on the world root (IslandWorldBuilder.Build). Idempotent.</summary>
        public static GroundImprintVisual Attach(Transform parent)
        {
            if (parent == null) return null;
            GroundImprintVisual had = parent.GetComponentInChildren<GroundImprintVisual>();
            if (had != null) return had;
            var go = new GameObject("GroundImprints");
            go.transform.SetParent(parent, false);
            return go.AddComponent<GroundImprintVisual>();
        }

        private void Awake() => Build();

        /// <summary>Everything this component owns, built once. Called from Awake, and lazily from
        /// <see cref="Refresh"/> so an editor render fixture that adds the component and refreshes it in the same
        /// frame cannot find it half-built.</summary>
        private void Build()
        {
            if (_geometry != null) return;
            _geometry = new GroundImprintMesh(GroundImprints.World);
            int verts = _geometry.Capacity * GroundImprintDrawing.VerticesPerMark;
            _verts = new List<Vector3>(verts);
            _normals = new List<Vector3>(verts);
            _colours = new List<Color32>(verts);
            for (int i = 0; i < verts; i++) { _verts.Add(Vector3.zero); _normals.Add(Vector3.up); _colours.Add(new Color32(0, 0, 0, 0)); }

            // The whole ring's triangles, once: a mark's slot owns the same sixteen vertices for ever, so only the
            // vertices are ever rewritten. 16,384 vertices stays inside a 16-bit index buffer, which is what the phone
            // and the Quest want.
            var tris = new List<int>(_geometry.Indices.Length);
            for (int i = 0; i < _geometry.Indices.Length; i++) tris.Add(_geometry.Indices[i]);

            _mesh = new Mesh { name = "Ground imprints", indexFormat = IndexFormat.UInt16 };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.SetColors(_colours);
            _mesh.SetTriangles(tris, 0);
            // The marks are in WORLD space and spread over the island, so the bounds are the world's: recalculating
            // them over 16,384 vertices on every refresh would cost more than the drawing does, and a mark is a few
            // centimetres of a tiny screen area either way.
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(80000, 20000, 80000));

            _go = new GameObject("Ground imprint mesh");
            _go.transform.SetParent(transform, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            // Not a throw, unlike FuelSlickVisual's: this is built from IslandWorldBuilder.Build, and a missing
            // shader taking the whole world build down with it would be far worse than a flight with no marks in it.
            Shader shader = Shader.Find("FlyingGame/GroundImprint") ?? Shader.Find("FlyingGame/UnlitTransparent");
            if (shader == null) { Debug.LogWarning("GroundImprintVisual: FlyingGame/GroundImprint is missing from the build; the ground keeps its marks but nothing draws them."); return; }
            _material = new Material(shader);
            _renderer.sharedMaterial = _material;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshIntervalS;
            Refresh();
        }

        /// <summary>Bring the mesh up to date now. Also the entry point for the editor render fixture, which has no
        /// Update.</summary>
        public void Refresh()
        {
            Build();
            if (_geometry == null || _mesh == null) return;
            if (!_geometry.Refresh()) return;
            bool[] changed = _geometry.Changed;
            Vec3[] p = _geometry.Positions;
            Vec3[] n = _geometry.Normals;
            float[] rgba = _geometry.Rgba;
            for (int m = 0; m < changed.Length; m++)
            {
                if (!changed[m]) continue;
                int v0 = m * GroundImprintDrawing.VerticesPerMark;
                for (int i = v0; i < v0 + GroundImprintDrawing.VerticesPerMark; i++)
                {
                    _verts[i] = CoordinateMap.ToUnity(p[i]);
                    _normals[i] = CoordinateMap.ToUnity(n[i]);
                    int c = i * 4;
                    _colours[i] = new Color32(Byte(rgba[c]), Byte(rgba[c + 1]), Byte(rgba[c + 2]), Byte(rgba[c + 3]));
                }
            }
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.SetColors(_colours);
        }

        private static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        private static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            Release(_go); Release(_material); Release(_mesh);
        }
    }
}

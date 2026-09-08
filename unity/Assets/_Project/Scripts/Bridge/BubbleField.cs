using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The air made visible (ARCHITECTURE.md #1 visual + owner request): an evenly-spaced 3D grid of
    /// bubbles FIXED IN THE AIR MASS around the aircraft. Only a local block exists; as the aircraft
    /// moves a cell, bubbles wrap modulo the spacing so the field feels infinite. GPU-instanced,
    /// hard count budget, opaque size-faded spheres (NO transparent overdraw — the #1 mobile perf
    /// risk). Bubbles translate with wind relative to terrain, so relative streaming past the airframe
    /// IS the visible AoA/sideslip cue.
    /// </summary>
    public sealed class BubbleField : MonoBehaviour
    {
        public Transform Follow;                 // the aircraft
        public float Spacing = 12f;              // metres between bubbles
        public int HalfCount = 7;                // (2*half+1)^3 bubbles — 15^3 = 3375, within budget
        public float BubbleSize = 0.35f;
        public float FadeStartFraction = 0.6f;   // begin shrinking beyond this fraction of the block radius

        public FlyingGame.Core.Turbulence Turbulence;   // set by the scene/weather; null = calm
        public float GustDisplayScale = 0.6f;            // seconds of gust velocity shown as bubble offset

        private Mesh _mesh;
        private Material _material;
        private readonly List<Matrix4x4> _matrices = new();
        private Vector3 _airMassOrigin;          // world point the grid is anchored to (moves with wind)

        private void Start()
        {
            _mesh = BuildSphere();
            _material = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.85f, 0.92f, 1f, 1f) };
            _material.enableInstancing = true;
            _airMassOrigin = Vector3.zero;
        }

        private void LateUpdate()
        {
            if (Follow == null || _mesh == null)
            {
                return;
            }

            // Steady wind drifts the whole air mass over the ground, so bubbles slide relative to the
            // runway — you SEE the headwind/crosswind, not just feel it.
            var steady = FlyingGame.Core.Atmosphere.SteadyWind;
            if (steady.LengthSquared > 1e-9)
            {
                _airMassOrigin += CoordinateMap.ToUnity(steady) * Time.deltaTime;
            }

            Vector3 center = Follow.position;
            float blockRadius = HalfCount * Spacing;
            _matrices.Clear();

            // Snap the grid phase to the air-mass origin so bubbles hold station in the air, not on
            // the aircraft: each bubble's world position is the nearest lattice point (in air-mass
            // frame) to the aircraft, then wrapped — the classic infinite-grid modulo trick.
            for (int ix = -HalfCount; ix <= HalfCount; ix++)
            for (int iy = -HalfCount; iy <= HalfCount; iy++)
            for (int iz = -HalfCount; iz <= HalfCount; iz++)
            {
                Vector3 latticeFromAircraft = new(
                    Mathf.Round((center.x - _airMassOrigin.x) / Spacing) + ix,
                    Mathf.Round((center.y - _airMassOrigin.y) / Spacing) + iy,
                    Mathf.Round((center.z - _airMassOrigin.z) / Spacing) + iz);
                Vector3 pos = _airMassOrigin + latticeFromAircraft * Spacing;

                // Turbulence made visible: each bubble is displaced by the LOCAL gust — eddies show as
                // clusters of bubbles swirling together, and the aircraft visibly flies through moving
                // air. Same field the wings feel (via Atmosphere), sampled at the bubble's sim position.
                if (Turbulence != null)
                {
                    var simPos = CoordinateMap.ToSim(pos);
                    FlyingGame.Core.MathTypes.Vec3 gust = Turbulence.WindAt(simPos, FlyingGame.Core.Atmosphere.SimTimeSec);
                    pos += CoordinateMap.ToUnity(gust) * GustDisplayScale;
                }

                float dist = Vector3.Distance(pos, center);
                if (dist > blockRadius)
                {
                    continue; // spherical block, not cubic — fewer bubbles, rounder falloff
                }

                // Size fade with distance (opaque — no alpha), so the field dissolves at its edge
                // instead of popping. Bubbles nearest the airframe are full size.
                float f = Mathf.Clamp01((dist / blockRadius - FadeStartFraction) / (1f - FadeStartFraction));
                float size = BubbleSize * (1f - f);
                if (size < 0.02f)
                {
                    continue;
                }

                _matrices.Add(Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size));

                if (_matrices.Count >= 1023)
                {
                    Graphics.DrawMeshInstanced(_mesh, 0, _material, _matrices);
                    _matrices.Clear();
                }
            }

            if (_matrices.Count > 0)
            {
                Graphics.DrawMeshInstanced(_mesh, 0, _material, _matrices);
            }
        }

        /// <summary>Advance the air mass by a wind displacement (m) so bubbles drift vs terrain.</summary>
        public void ApplyWind(Vector3 windMetres) => _airMassOrigin += windMetres;

        private static Mesh BuildSphere()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return m;
        }
    }
}

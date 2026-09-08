using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The aircraft's shadow on the ground (owner request: judge height on landing the way a real pilot
    /// does — the shadow closes on the aircraft as it descends). Every mesh under the airframe is
    /// re-drawn each frame through a planar projection matrix that flattens it onto the ground plane
    /// along the sun's direction, so it is a true silhouette: correct size, correct shape, and offset
    /// from the aircraft by altitude / tan(sun elevation) — the height cue. No Unity shadow mapping (the
    /// ground is unlit and shadow maps are a mobile fill-rate cost); one cheap extra draw per part.
    /// </summary>
    public sealed class GroundShadow : MonoBehaviour
    {
        public float GroundY = 0.0f;              // flat ground plane height (world)
        public Color Shade = new(0f, 0f, 0f, 0.45f);
        public float MaxAltitudeM = 1500f;        // beyond this the shadow is far off-screen anyway; skip the draws

        private Material _material;
        private Light _sun;
        private readonly List<MeshFilter> _parts = new();

        private void Start()
        {
            Shader sh = Shader.Find("FlyingGame/PlanarShadow");
            if (sh == null)
            {
                Debug.LogWarning("GroundShadow: FlyingGame/PlanarShadow shader missing — no shadow.");
                enabled = false;
                return;
            }
            _material = new Material(sh) { color = Shade };
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { _sun = l; break; }
            }
            Refresh();
        }

        /// <summary>Re-collect the airframe meshes (AirframeVisual calls this after a rebuild).</summary>
        public void Refresh() => GetComponentsInChildren(true, _parts);

        private void LateUpdate()
        {
            if (_material == null || _parts.Count == 0)
            {
                return;
            }

            // Local ground under the aircraft (plateau airports): the world height field — or the WATER surface
            // when over a lake/river (owner: shadow on water, but subtler).
            var simPos = CoordinateMap.ToSim(transform.position);
            float ground = (float)FlyingGame.Core.WorldTerrain.GroundHeightAt(simPos.X, simPos.Y);
            double? water = FlyingGame.Core.FloatHydro.WaterSurfaceAt(simPos.X, simPos.Y);
            bool onWater = water.HasValue && water.Value > ground;
            GroundY = onWater ? (float)water.Value : ground;
            _material.color = onWater ? new Color(Shade.r, Shade.g, Shade.b, Shade.a * 0.45f) : Shade;
            float altitude = transform.position.y - GroundY;
            if (altitude < -1f || altitude > MaxAltitudeM)
            {
                return;
            }

            // Light direction (where the sunlight travels). Must have a downward component.
            Vector3 l = _sun != null ? _sun.transform.forward : new Vector3(-0.4f, -0.8f, 0.4f).normalized;
            if (l.y > -0.05f)
            {
                return; // sun on/below the horizon: no ground shadow
            }

            // Planar projection onto y = GroundY along l:  S = X - l * (X.y - GroundY) / l.y
            float kx = l.x / l.y, kz = l.z / l.y;
            var proj = new Matrix4x4();
            proj.SetRow(0, new Vector4(1f, -kx, 0f, kx * GroundY));
            proj.SetRow(1, new Vector4(0f, 0f, 0f, GroundY + (onWater ? 0.08f : 0.03f)));
            proj.SetRow(2, new Vector4(0f, -kz, 1f, kz * GroundY));
            proj.SetRow(3, new Vector4(0f, 0f, 0f, 1f));

            foreach (MeshFilter mf in _parts)
            {
                if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Matrix4x4 m = proj * mf.transform.localToWorldMatrix;
                Graphics.DrawMesh(mf.sharedMesh, m, _material, 0, null, 0, null, false, false, false);
            }
        }
    }
}

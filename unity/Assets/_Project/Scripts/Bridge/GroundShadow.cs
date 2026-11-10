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

        /// <summary>A part that may cast, with everything a NAME decides about it resolved once at
        /// <see cref="Refresh"/>. Reading Transform.name allocates a new string every time, and this loop used to read
        /// two per ancestor of every airframe mesh every frame (IsSight, HasSkin): over a thousand strings a frame
        /// below 1,500 m, which on the Quest was a garbage collection every few seconds (docs/quest-performance.md).</summary>
        private struct Caster { public MeshFilter Mf; public Renderer R; public SkinnedMeshRenderer[] Skins; }
        private readonly List<Caster> _cast = new();

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

        private static bool HasSkin(SkinnedMeshRenderer[] skins)
        {
            foreach (SkinnedMeshRenderer smr in skins) if (smr != null && smr.enabled) return true;
            return false;
        }

        /// <summary>The skinned copies a carve hung under a part ("Skin:" children), found once.</summary>
        private static SkinnedMeshRenderer[] SkinsOf(Transform t)
        {
            List<SkinnedMeshRenderer> l = null;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (!c.name.StartsWith("Skin:", System.StringComparison.Ordinal)) continue;
                var smr = c.GetComponent<SkinnedMeshRenderer>();
                if (smr != null) (l ??= new List<SkinnedMeshRenderer>()).Add(smr);
            }
            return l != null ? l.ToArray() : System.Array.Empty<SkinnedMeshRenderer>();
        }

        /// <summary>Re-collect the airframe meshes (AirframeVisual calls this after a rebuild).</summary>
        public void Refresh()
        {
            GetComponentsInChildren(true, _parts);
            _cast.Clear();
            foreach (MeshFilter mf in _parts)
            {
                if (mf == null || CockpitSights.IsSight(mf.transform)) continue;
                _cast.Add(new Caster { Mf = mf, R = mf.GetComponent<Renderer>(), Skins = SkinsOf(mf.transform) });
            }
        }

        private void LateUpdate()
        {
            if (_material == null || _cast.Count == 0)
            {
                return;
            }

            // Local ground under the aircraft (plateau airports): the world height field.
            var simPos = CoordinateMap.ToSim(transform.position);
            float ground = (float)FlyingGame.Core.WorldTerrain.GroundHeightAt(simPos.X, simPos.Y);
            double? water = FlyingGame.Core.FloatHydro.WaterSurfaceAt(simPos.X, simPos.Y);
            bool onWater = water.HasValue && water.Value > ground;
            // A dark cutout painted ON the surface is a second airplane beside the mirror
            // (Ty's J-3 floats Tail screenshot). A water shadow belongs on the submerged bed,
            // attenuated through the volume, not on the reflective interface. Keep land shadows.
            if (onWater) return;
            GroundY = ground;
            _material.color = Shade;
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
            // Shadow plane 16 cm above the ground height: the runway slabs, aprons, roads and their painted markings
            // are stacked up to ~13 cm above the terrain, and a 3 cm plane sat UNDER them (no shadow on the runway).
            proj.SetRow(1, new Vector4(0f, 0f, 0f, GroundY + 0.16f));
            proj.SetRow(2, new Vector4(0f, -kz, 1f, kz * GroundY));
            proj.SetRow(3, new Vector4(0f, 0f, 0f, 1f));

            foreach (Caster c in _cast)
            {
                MeshFilter mf = c.Mf;
                if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy)
                {
                    continue;
                }
                var rr = c.R;
                // The hidden procedural shell under a real model casts nothing — but a RIGGED model mesh (its renderer handed
                // over to a skinned copy, owner 2026-10-05: "the shadow was wrong") still casts, from its rigid mesh.
                if (rr != null && !rr.enabled && !HasSkin(c.Skins)) continue;
                Matrix4x4 m = proj * mf.transform.localToWorldMatrix;
                // Every submesh: the real models (USDZ imports) are one mesh with a submesh per material, and drawing
                // only submesh 0 cast the shadow of a single small piece (owner: "the shadow is tiny").
                Mesh mesh = mf.sharedMesh;
                for (int si = 0; si < mesh.subMeshCount; si++)
                    Graphics.DrawMesh(mesh, m, _material, 0, null, si, null, false, false, false);
            }
        }
    }
}

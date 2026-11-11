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
    ///
    /// HEIGHT (open visual report 2026-11-09, Ty: "fix it now — it's cheap"): the flattening above is a PARALLEL
    /// projection, and a parallel projection of a solid is exactly its own cross-section however far it travels —
    /// which is why the shadow was the same size at 50 ft and at 5,000 ft. The sun is a disc, not a point, so a real
    /// shadow grows and fades with the range it is cast over. <see cref="FlyingGame.Core.SunShadow"/> is that
    /// geometry and carries its derivation; all this does with it is scale the projection about the shadow's own
    /// centre by <c>Grow</c> and fade the material by <c>Opacity01</c>. Measured against the Pitts' 6.0 m span with
    /// the sun at 45° (SunShadowTests holds these): ×1.03 at 94 % darkness at 50 ft, ×1.33 at 56 % at 500 ft, ×1.67
    /// at 36 % at 1,000 ft, ×2.34 at 18 % at 2,000 ft, ×4.34 at 5 % at 5,000 ft — a shadow you cannot find at
    /// height, which is what a real one does.
    ///
    /// STILL WRONG, and not this change's to fix: the shadow plane is HORIZONTAL, at the ground height under the
    /// aeroplane's own position. On a hillside the surface up-sun is higher than that plane, so the silhouette lands
    /// somewhere the ground is not — in Ty's IMG_3289 frames the wreck is on a grass slope and its shadow is drawn
    /// above and behind it. Projecting onto the terrain's own normal (WorldTerrain.ContactNormalAt, which the hard
    /// points already use) is the fix, and it is a different change: a general-plane shadow matrix, which nothing
    /// here can verify without a Unity build.
    /// </summary>
    public sealed class GroundShadow : MonoBehaviour
    {
        public float GroundY = 0.0f;              // flat ground plane height (world)
        public Color Shade = new(0f, 0f, 0f, 0.45f);
        public float MaxAltitudeM = 1500f;        // beyond this the shadow is far off-screen anyway; skip the draws
        /// <summary>Below this share of <see cref="Shade"/>'s alpha nothing is drawn: a 1 % shadow on unlit ground is
        /// under one value of 8-bit colour, so the draws buy a pixel nobody can see. 1 % is Grow = 10, which for the
        /// Pitts' 3 m half-span is 5.8 km of slant range — past <see cref="MaxAltitudeM"/> unless the sun is low, so
        /// this bites for a small aeroplane under a low sun, which is exactly when the smear is not worth drawing.</summary>
        public float MinOpacity01 = 0.01f;

        private Material _material;
        private Light _sun;
        private readonly List<MeshFilter> _parts = new();
        /// <summary>Half the airframe's own horizontal size, m (measured once per <see cref="Refresh"/> from the
        /// meshes). This is what the sun's penumbra is compared against: a band that is a tenth of the aeroplane
        /// barely softens the shadow, one that is the whole aeroplane leaves no dark core at all. The OVERALL
        /// half-size is used — span, usually — so this is the generous reading: the chord-scale detail inside the
        /// silhouette (a wing 1 m deep, a strut) washes out far lower than the outline does, and a single flat
        /// silhouette has no way to show that happening anyway.</summary>
        private float _halfSizeM = 3f;

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
            MeasureHalfSize();
        }

        /// <summary>
        /// The airframe's horizontal half-size in its OWN frame, from the casters' mesh bounds: the aeroplane is
        /// measured where it cannot be turned by its attitude, so a knife-edge pass does not suddenly report a
        /// 1 m aeroplane and harden its shadow. Mesh.bounds is the local AABB and costs nothing to read (no
        /// isReadable needed), and its eight corners are carried into this transform's frame because a part sits
        /// wherever the rig put it. Falls back to the previous value if there is nothing to measure.
        /// </summary>
        private void MeasureHalfSize()
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            Matrix4x4 toMe = transform.worldToLocalMatrix;
            foreach (Caster c in _cast)
            {
                MeshFilter mf = c.Mf;
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = toMe * mf.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f)));
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                }
            }
            if (minX <= maxX) _halfSizeM = Mathf.Max(0.5f, 0.5f * Mathf.Max(maxX - minX, maxZ - minZ));
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

            // THE SUN IS A DISC. The band each edge casts is half a degree wide per unit of SLANT range — the distance
            // the shadow actually travels along the ray, altitude / sin(elevation), not the altitude: that is why an
            // evening shadow is both longer and softer than a midday one from the same height, and it falls out of the
            // geometry instead of being put in. l is a unit vector, so -l.y IS the sine of the sun's elevation.
            float slant = altitude / -l.y;
            (double growD, double opacityD) = FlyingGame.Core.SunShadow.Soften(slant, _halfSizeM);
            float grow = (float)growD, opacity = (float)opacityD;
            if (opacity < MinOpacity01) return;
            Color shade = Shade;
            shade.a *= opacity;
            _material.color = shade;
            // Grown about the shadow's OWN centre — where the aeroplane's own position lands on the plane the rows
            // above project onto, y = GroundY — so the patch spreads evenly round the silhouette instead of sliding
            // off down-sun. Only x and z: the projection has already flattened every vertex in y, so that row is
            // left alone and the shadow stays on the plane.
            Matrix4x4 soften = Matrix4x4.identity;
            if (grow > 1f)
            {
                Vector3 p = transform.position;
                Vector3 centre = p - l * ((p.y - GroundY) / l.y);
                soften.m00 = grow; soften.m22 = grow;
                soften.m03 = centre.x * (1f - grow);
                soften.m23 = centre.z * (1f - grow);
            }
            proj = soften * proj;

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

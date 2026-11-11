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
            // THE WATER GETS A SHADOW TOO, and it is not this projection. A dark cutout painted ON the surface is a
            // second airplane beside the mirror (Ty's J-3 floats Tail screenshot), because a silhouette drawn over
            // the water darkens the REFLECTION as much as the water, and a shadow has nothing to do with the
            // reflection: the sky still reflects off a shaded patch exactly as brightly as off a sunlit one. What
            // the shadow removes is the sunlight going INTO the water. Ty, 2026-11-11: "both a shadow and a
            // reflection. The reflection should show color etc where the shadow is just darkness."
            // So over water the same silhouette is rendered into a mask (<see cref="CastOntoWater"/>) and
            // Water.shader applies it to the submerged term and the sun glint only. Keep land shadows as they are.
            if (onWater) { CastOntoWater((float)water.Value); return; }
            SetWaterShadow(false);
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

        // ---- the shadow on the water -------------------------------------------------------------------------
        //
        // Water.shader had no AutoLight.cginc, no SHADOW_COORDS and no multi_compile_fwdbase, so it never sampled a
        // shadow map and the water could not receive anything; and nothing in this project renders a shadow map at
        // all (the ground is unlit, and a shadow map is a fill-rate cost a phone and a headset cannot pay for one
        // aeroplane). So the water is handed the SAME silhouette the ground gets, in the only form a transparent
        // surface can use: a mask.
        //
        // A tiny orthographic camera is put at the aeroplane looking ALONG the sun's ray and renders the airframe
        // white on black through WaterShadowCaster.shader. That camera's view-projection IS the parallel projection
        // this class has always used for the ground — the same matrix, read from the sun's end instead of the
        // water's — so there is no second shadow geometry to keep in step with the first. Water.shader transforms
        // each water fragment by it and darkens the submerged term and the sun glint; the reflection is untouched.
        //
        // THE PENUMBRA IS THE MASK'S OWN RESOLUTION, which is the part worth reading twice. SunShadow says an edge
        // casts a band 2p wide at slant range d, with the umbra ending p/2 inside the sharp edge and the penumbra
        // reaching p/2 outside it. A bilinear texture fetch across a one-texel black/white edge is a linear ramp
        // straddling that edge by exactly ±half a texel. So sizing the mask so that ONE TEXEL IS p gives the
        // physically correct penumbra for free, in one tap, with no blur kernel on the water's fragment shader at
        // all — and it gets cheaper the higher the aeroplane flies, which is the opposite of how a blur behaves.
        // It also means this does NOT need SunShadow.Soften's flux-conservation factor: Soften exists because a
        // single flat silhouette cannot draw a gradient, and this one can. The one thing taken from Soften is
        // MinOpacity01's reasoning — past the point where the patch is below one value of 8-bit colour, stop.
        //
        // COST, measured in draws rather than guessed: one camera render of the airframe meshes per frame at
        // between 8x8 and 128x128 pixels, and only while the aeroplane is over water and below MaxAltitudeM. The
        // fill is 16 k pixels at worst, a thousandth of a phone's screen; the real cost is the ~40 airframe draw
        // calls, which is what the LAND shadow already pays every frame and the water used to pay nothing for.
        // On the water's own fragment shader it is one matrix multiply and one tex2D, behind a uniform branch.
        private const int MaskMin = 8, MaskMax = 128;
        /// <summary>How much of the water's daylight is the DIRECT beam, the part a shadow can take away. The rest
        /// is skylight, which the aeroplane does not block: an aeroplane's shadow on a lake is dark, not black,
        /// and a real one has about a sixth of the light still in it under a clear sky.</summary>
        public float DirectBeamFraction = 0.82f;
        private Camera _maskCamera;
        private RenderTexture _mask;
        private Shader _casterShader;
        private static readonly int MaskTexId = Shader.PropertyToID("_WaterShadowTex");
        private static readonly int MaskMatrixId = Shader.PropertyToID("_WaterShadowMatrix");
        private static readonly int MaskParamsId = Shader.PropertyToID("_WaterShadow");

        private static void SetWaterShadow(bool on) => Shader.SetGlobalVector(MaskParamsId, new Vector4(on ? 1 : 0, 0, 0, 0));

        private void CastOntoWater(float waterY)
        {
            Vector3 l = _sun != null ? _sun.transform.forward : new Vector3(-0.4f, -0.8f, 0.4f).normalized;
            float altitude = transform.position.y - waterY;
            // Sun on or below the horizon, or the aeroplane under the surface: nothing to cast.
            if (l.y > -0.05f || altitude < -1f || altitude > MaxAltitudeM) { SetWaterShadow(false); return; }

            // Slant range along the ray, exactly as the land path measures it: altitude / sin(elevation), and l is
            // a unit vector so -l.y IS that sine.
            float slant = altitude / -l.y;
            float penumbra = (float)FlyingGame.Core.SunShadow.PenumbraHalfWidthM(slant);
            // Past the point where the patch is below one value of 8-bit colour there is nothing to draw. The patch
            // spreads the blocked light over (1 + p/w)^2 of its sharp area, which is Soften's own opacity.
            (double _, double opacity) = FlyingGame.Core.SunShadow.Soften(slant, _halfSizeM);
            if (opacity < MinOpacity01) { SetWaterShadow(false); return; }

            // The patch the mask has to cover: the silhouette, plus the penumbra it spreads into. 1.45 is the
            // diagonal of a square half-size plus a little — the measured half-size is the aeroplane's LARGEST
            // horizontal dimension, usually the span, and at an angle the silhouette's diagonal is longer.
            float half = _halfSizeM * 1.45f + penumbra;
            // One texel = one penumbra width, so bilinear filtering draws the penumbra and nothing else has to.
            int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(2f * half / Mathf.Max(penumbra, 1e-3f))), MaskMin, MaskMax);
            EnsureMask(size);
            if (_maskCamera == null) { SetWaterShadow(false); return; }

            // Looking ALONG the sun's ray from above the aeroplane. The near plane starts above everything that
            // could cast and the far plane is just past the water, so the mask holds the airframe and nothing else.
            // l is the direction the LIGHT travels, so the shadow of a point P lands at P + l·slant; the mask is
            // centred midway along that ray, which puts both the aeroplane and its shadow on the camera's axis.
            Vector3 centre = transform.position + l * (slant * 0.5f);
            float reach = slant * 0.5f + _halfSizeM * 2f + 5f;
            _maskCamera.transform.position = centre - l * reach;
            _maskCamera.transform.rotation = Quaternion.LookRotation(l, Mathf.Abs(l.y) > 0.99f ? Vector3.forward : Vector3.up);
            _maskCamera.orthographicSize = half;
            _maskCamera.nearClipPlane = 0.01f;
            _maskCamera.farClipPlane = reach * 2f + _halfSizeM * 2f;
            _maskCamera.RenderWithShader(_casterShader, "");

            // The water reads this matrix: world position -> the mask camera's clip space -> uv.
            //
            // renderIntoTexture: FALSE, and this is the whole of the "shadow is off by 90 degrees" bug (Ty,
            // 2026-11-11, on the glassy-water hero frame). It was true. GL.GetGPUProjectionMatrix(p, true) returns
            // the projection a camera USES when it draws into a texture, and on D3D and Metal that matrix has its
            // Y negated, because those APIs address a render target from the TOP row down while a shader's tex2D
            // addresses a texture from the BOTTOM row up. The flip is there so the image comes out of the
            // rasteriser the right way up IN MEMORY; by the time anything samples the result with
            // uv = clip·0.5 + 0.5, the image is already upright and the flip must not be applied a second time.
            // Applying it twice mirrors the mask about the sun-perpendicular axis.
            //
            // HOW I KNOW IT IS A MIRROR AND NOT A ROTATION, because those are not the same bug and only one of
            // them is this: in the diagnostic frame the aeroplane's heading put its fuselage at -135 deg in mask
            // coordinates and its span at +135 deg — symmetric about the mask's x axis — so a mirror in y maps the
            // fuselage exactly onto the span and is INDISTINGUISHABLE from a 90 degree yaw at that one heading.
            // It is distinguishable at any other, which is why the proof shots are taken at several (-viewYaw).
            // A constant 90 degrees would have been wrong at every heading but that one.
            //
            // GL and GLES do not flip at all, so passing false is also what they were already doing: this changes
            // Metal and D3D to agree with them rather than changing anything on Android.
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(_maskCamera.projectionMatrix, false) * _maskCamera.worldToCameraMatrix;
            Shader.SetGlobalTexture(MaskTexId, _mask);
            Shader.SetGlobalMatrix(MaskMatrixId, vp);
            Shader.SetGlobalVector(MaskParamsId, new Vector4(1f, Mathf.Clamp01(DirectBeamFraction), waterY, 0f));
        }

        private void EnsureMask(int size)
        {
            if (_casterShader == null)
            {
                _casterShader = Shader.Find("FlyingGame/WaterShadowCaster");
                if (_casterShader == null)
                {
                    Debug.LogWarning("GroundShadow: FlyingGame/WaterShadowCaster missing — no shadow on the water.");
                    return;
                }
            }
            if (_mask == null || _mask.width != size)
            {
                if (_mask != null) { _mask.Release(); Dispose(_mask); }
                // R8: a shadow has one channel. Clamped, so the water outside the patch reads the border, and the
                // border is black because the camera clears to black.
                _mask = new RenderTexture(size, size, 16, RenderTextureFormat.R8)
                { name = "Water shadow mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _mask.Create();
                if (_maskCamera != null) _maskCamera.targetTexture = _mask;
            }
            if (_maskCamera == null)
            {
                var go = new GameObject("WaterShadowCamera") { hideFlags = HideFlags.HideAndDontSave };
                _maskCamera = go.AddComponent<Camera>();
                _maskCamera.enabled = false;                 // rendered by hand, once a frame, only over water
                _maskCamera.orthographic = true;
                _maskCamera.clearFlags = CameraClearFlags.SolidColor;
                _maskCamera.backgroundColor = Color.black;   // "no aeroplane here" = full sun
                _maskCamera.cullingMask = 1 << WaterReflection.AircraftLayer;
                _maskCamera.depthTextureMode = DepthTextureMode.None;
                _maskCamera.allowMSAA = false;
                _maskCamera.allowHDR = false;
                _maskCamera.useOcclusionCulling = false;
                _maskCamera.targetTexture = _mask;
            }
        }

        private static void Dispose(Object obj)
        {
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        private void OnDestroy()
        {
            SetWaterShadow(false);
            if (_maskCamera != null) Dispose(_maskCamera.gameObject);
            if (_mask != null) { _mask.Release(); Dispose(_mask); }
        }
    }
}

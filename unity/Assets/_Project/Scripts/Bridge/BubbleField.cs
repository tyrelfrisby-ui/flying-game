using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The air made visible (ARCHITECTURE.md #1 visual + owner request): an evenly-spaced 3D grid of
    /// bubbles FIXED IN THE AIR MASS around the aircraft. Only a local block exists; as the aircraft
    /// moves a cell, bubbles wrap modulo the spacing so the field feels infinite. Bubbles translate
    /// with wind relative to terrain, so relative streaming past the airframe IS the visible
    /// AoA/sideslip cue.
    ///
    /// Look: soap bubbles (FlyingGame/Bubble shader — clear body, glossy fresnel rim, sun glint).
    /// Each bubble is TINTED by the local air temperature (blue = cold … white = 59 °F standard …
    /// red = hot) and SIZED by the local air density (denser = bigger), so a thermal's warm, thin
    /// core reads as a column of smaller, reddish bubbles and a cold high layer as bigger blue ones.
    ///
    /// Bubbles that have flowed PAST the aircraft toward the camera fade out — but only inside a
    /// circle at the centre of the frame; outside it they stay visible, so the periphery keeps
    /// streaming while nothing pops straight into the lens.
    /// </summary>
    public sealed class BubbleField : MonoBehaviour
    {
        public Transform Follow;                 // the aircraft
        public float Spacing = 12f;              // metres between bubbles
        public int HalfCount = 4;                // (2*half+1)^3 = 9^3; sphere-culled to a few hundred DrawMesh calls
        public float BubbleSize = 0.9f;          // metres at standard sea-level density
        public float FadeStartFraction = 0.6f;   // begin shrinking beyond this fraction of the block radius

        [Header("Temperature tint / density size")]
        public float HotF = 120f;                // fully red at/above this air temperature
        public float StandardF = 59f;            // ISA sea level: no tint
        public float ColdF = -50f;               // fully blue at/below this
        public Color HotTint = new(1f, 0.32f, 0.18f);
        public Color ColdTint = new(0.35f, 0.6f, 1f);
        public float DensitySizeGain = 4f;       // size = 1 + (rho/rho0 - 1) * gain (exaggerates the few-% real change)
        public float MinSizeScale = 0.4f, MaxSizeScale = 1.8f;

        [Header("Centre-of-frame fade (bubbles that passed the aircraft)")]
        public float CenterCircleFraction = 0.24f; // radius as a fraction of screen height
        public float CenterCircleFeather = 0.06f;  // soft edge width (same units)
        public float PastFadeDepthM = 6f;          // metres past the aircraft (toward the camera) to fully fade

        public FlyingGame.Core.Turbulence Turbulence;   // set by the scene/weather; null = calm
        public float GustDisplayScale = 0.6f;            // seconds of gust velocity shown as bubble offset
        public float StreamPeriodS = 4f;                 // seconds a bubble rides the local air (thermal / slope lift) before re-seeding

        private Mesh _mesh;
        private Material _material;
        private MaterialPropertyBlock _props;
        private Vector3 _airMassOrigin;          // world point the grid is anchored to (moves with wind)
        private Light _sun;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int SunDirId = Shader.PropertyToID("_BubbleSunDir");

        private void Start()
        {
            _mesh = BuildSphere();
            // Per-bubble Graphics.DrawMesh + MaterialPropertyBlock (tint/alpha per bubble). DrawMeshInstanced
            // with an instancing shader reported drawing bubbles but nothing appeared on Metal (per-instance
            // transforms weren't applying), so each matrix is submitted directly; the count is kept modest
            // so the draw calls stay mobile-friendly.
            Shader sh = Shader.Find("FlyingGame/Bubble") ?? Shader.Find("Unlit/Color");
            _material = new Material(sh);
            _props = new MaterialPropertyBlock();
            _airMassOrigin = Vector3.zero;
            _sun = FindSun();
        }

        private void LateUpdate()
        {
            if (Follow == null || _mesh == null)
            {
                return;
            }

            Camera cam = Camera.main;
            Shader.SetGlobalVector(SunDirId, _sun != null ? -_sun.transform.forward : new Vector3(0.4f, 0.8f, -0.4f).normalized);

            // Steady wind drifts the whole air mass over the ground, so bubbles slide relative to the
            // runway — you SEE the headwind/crosswind, not just feel it.
            var steady = FlyingGame.Core.Atmosphere.SteadyWind;
            if (steady.LengthSquared > 1e-9)
            {
                _airMassOrigin += CoordinateMap.ToUnity(steady) * Time.deltaTime;
            }

            Vector3 center = Follow.position;
            float blockRadius = HalfCount * Spacing;

            // Camera-space setup for the centre-circle fade.
            Matrix4x4 worldToCam = cam != null ? cam.worldToCameraMatrix : Matrix4x4.identity;
            float tanHalfV = cam != null ? Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) : 1f;
            float aircraftDepth = cam != null ? -worldToCam.MultiplyPoint3x4(center).z : 0f;

            float rho0 = (float)FlyingGame.Core.Atmosphere.SeaLevelDensityKgM3;

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
                var simPos = CoordinateMap.ToSim(pos);
                if (Turbulence != null)
                {
                    FlyingGame.Core.MathTypes.Vec3 gust = Turbulence.WindAt(simPos, FlyingGame.Core.Atmosphere.SimTimeSec);
                    pos += CoordinateMap.ToUnity(gust) * GustDisplayScale;
                }
                // Thermals and slope lift made visible: the LOCAL air motion (everything but the steady wind,
                // which already carries the whole lattice) moves each bubble along its streamline for a few
                // seconds, then it re-seeds at its lattice point — with per-bubble phases so the column reads
                // as a continuous stream: bubbles climbing the windward face and up the thermal core, others
                // sinking in the ring of sink around it and down the lee slope.
                float streamTau = 0f;
                FlyingGame.Core.MathTypes.Vec3 local = FlyingGame.Core.Atmosphere.WindAtPosition(simPos) - steady;
                if (Turbulence != null) local -= Turbulence.WindAt(simPos, FlyingGame.Core.Atmosphere.SimTimeSec);
                float localSpeed = (float)local.Length;
                if (localSpeed > 0.15f)
                {
                    int hx = (int)latticeFromAircraft.x, hy = (int)latticeFromAircraft.y, hz = (int)latticeFromAircraft.z;
                    uint h = (uint)(hx * 73856093) ^ (uint)(hy * 19349663) ^ (uint)(hz * 83492791);
                    h ^= h >> 13; h *= 0x85EBCA6Bu; h ^= h >> 16;
                    float phase = (h & 0xFFFF) / 65535f;
                    streamTau = Mathf.Repeat(Time.time / StreamPeriodS + phase, 1f);
                    pos += CoordinateMap.ToUnity(local) * (streamTau * StreamPeriodS);
                }

                float dist = Vector3.Distance(pos, center);
                if (dist > blockRadius)
                {
                    continue; // spherical block, not cubic — fewer bubbles, rounder falloff
                }

                // Size fade with distance so the field dissolves at its edge instead of popping.
                float f = Mathf.Clamp01((dist / blockRadius - FadeStartFraction) / (1f - FadeStartFraction));
                float edgeScale = 1f - f;

                // Density → size, temperature → tint, from the same atmosphere the wings fly in.
                float rho = (float)FlyingGame.Core.Atmosphere.DensityAtPosition(simPos);
                float sizeScale = Mathf.Clamp(1f + (rho / rho0 - 1f) * DensitySizeGain, MinSizeScale, MaxSizeScale);
                float size = BubbleSize * sizeScale * edgeScale;
                if (size < 0.02f)
                {
                    continue;
                }

                float tempF = KelvinToF((float)FlyingGame.Core.Atmosphere.TemperatureAtPosition(simPos));
                Color tint = TintFor(tempF);

                // Centre-of-frame fade: bubbles nearer the camera than the aircraft (they've flowed past
                // it) fade with how far past they are — but only inside the centre circle.
                float alpha = 1f;
                if (cam != null)
                {
                    Vector3 cs = worldToCam.MultiplyPoint3x4(pos);
                    float depth = -cs.z;
                    if (depth <= 0.05f)
                    {
                        continue; // behind the lens
                    }

                    float past = aircraftDepth - depth;
                    if (past > 0f)
                    {
                        // Screen offset from centre in units of screen HEIGHT (so the circle is round on any aspect).
                        float ny = cs.y / (depth * tanHalfV) * 0.5f;
                        float nx = cs.x / (depth * tanHalfV) * 0.5f;
                        float r = Mathf.Sqrt(nx * nx + ny * ny);
                        float inCircle = 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(CenterCircleFraction - CenterCircleFeather, CenterCircleFraction + CenterCircleFeather, r));
                        float pastFade = Mathf.Clamp01(past / Mathf.Max(0.01f, PastFadeDepthM));
                        alpha = 1f - inCircle * pastFade;
                        if (alpha < 0.02f)
                        {
                            continue;
                        }
                    }
                }

                // Fade the streaming bubble out just before it re-seeds so the jump back is invisible.
                if (streamTau > 0.75f) alpha *= 1f - (streamTau - 0.75f) / 0.25f;
                if (alpha < 0.02f) continue;
                _props.SetColor(ColorId, tint);
                _props.SetFloat(AlphaId, alpha);
                Graphics.DrawMesh(_mesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size), _material, 0,
                    cam, 0, _props, false, false, false);
            }
        }

        /// <summary>59 °F = no tint (white); warmer blends toward HotTint by 120 °F, colder toward ColdTint by -50 °F.</summary>
        private Color TintFor(float tempF)
        {
            if (tempF >= StandardF)
            {
                float t = Mathf.Clamp01((tempF - StandardF) / Mathf.Max(1f, HotF - StandardF));
                return Color.Lerp(Color.white, HotTint, t);
            }

            float c = Mathf.Clamp01((StandardF - tempF) / Mathf.Max(1f, StandardF - ColdF));
            return Color.Lerp(Color.white, ColdTint, c);
        }

        private static float KelvinToF(float k) => (k - 273.15f) * 1.8f + 32f;

        private static Light FindSun()
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) return l;
            }
            return null;
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

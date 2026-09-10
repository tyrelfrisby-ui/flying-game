using System.Collections.Generic;
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
        // Owner 2026-09-10: "so the user truly sees the air" — four times the density (spacing 12 → 7.5 m), bubbles a
        // quarter the size, full strength to 250 ft, fading to nothing by 500 ft. Lift/sink-tinted bubbles keep their
        // tint at any distance (the wide LiftField carries them beyond 500 ft).
        public float Spacing = 7.5f;             // metres between bubbles
        public int HalfCount = 21;               // lattice reach ≈ 157 m: sphere-culled and thinned beyond FullRangeM
        public float BubbleSize = 0.225f;        // metres at standard sea-level density
        public float FullRangeM = 76.2f;         // 250 ft: full intensity inside
        public float FadeRangeM = 152.4f;        // 500 ft: invisible beyond
        public float FadeStartFraction = 0.6f;   // (sparse fallback field) begin shrinking beyond this fraction of the block radius
        public float MinPixels = 9f;             // a bubble never draws smaller than this on a ~2500 px phone screen (else it vanishes)
        public float JitterFraction = 0.38f;     // per-cell offset of the lattice point (± this × spacing) so the field is not a grid
        public float PlainBodyAlpha = 0.16f;     // dense field: plain-air body alpha (the soap-bubble 0.06 is invisible at this size)
        public float SampleRefreshS = 0.6f;      // how often a lattice cell re-samples the atmosphere
        public int SamplesPerFrame = 1500;       // atmosphere samples per frame (the rest come from the cell cache)

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
        public float LiftShowMs = 0.4f;                  // vertical air speed (m/s) from which lift/sink colouring starts
        public static readonly Color LiftTint = new(0.4f, 1f, 0.5f), SinkTint = new(1f, 0.5f, 0.3f);   // match the variometer: green up, orange down

        private Mesh _mesh;
        private Material _material;
        private MaterialPropertyBlock _props;
        private Vector3 _airMassOrigin;          // world point the grid is anchored to (moves with wind)
        private Light _sun;

        // Per-cell atmosphere cache (lattice index in the air-mass frame → local air motion, density, temperature).
        private struct Cell { public Vector3 Local; public float Rho, TempF, Time; }
        private readonly Dictionary<Vector3Int, Cell> _cells = new();
        private readonly List<Vector3Int> _stale = new();
        // Instanced batches (1023 per DrawMeshInstanced call).
        private const int Batch = 1023;
        private readonly Matrix4x4[] _mats = new Matrix4x4[Batch];
        private readonly Vector4[] _cols = new Vector4[Batch];
        private readonly float[] _alphas = new float[Batch], _bodies = new float[Batch];
        private MaterialPropertyBlock _batchProps;
        private int _batchCount;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int BodyAlphaId = Shader.PropertyToID("_BodyAlpha");
        private static readonly int SunDirId = Shader.PropertyToID("_BubbleSunDir");

        private static Mesh _shared;
        /// <summary>The bubble sphere mesh, shared with the wide lift field.</summary>
        public static Mesh SharedSphere() => _shared ??= BuildSphere();

        private void Start()
        {
            _mesh = SharedSphere();
            // Per-bubble Graphics.DrawMesh + MaterialPropertyBlock (tint/alpha per bubble). DrawMeshInstanced
            // with an instancing shader reported drawing bubbles but nothing appeared on Metal (per-instance
            // transforms weren't applying), so each matrix is submitted directly; the count is kept modest
            // so the draw calls stay mobile-friendly.
            Shader sh = Shader.Find("FlyingGame/Bubble") ?? Shader.Find("Unlit/Color");
            _material = new Material(sh) { enableInstancing = true };
            _props = new MaterialPropertyBlock();
            _batchProps = new MaterialPropertyBlock();
            _airMassOrigin = Vector3.zero;
            _sun = FindSun();
        }

        private void LateUpdate()
        {
            if (!SessionSettings.BubblesOn) return;
            Draw(Camera.main);
        }

        /// <summary>Drawn bubbles last call (diagnostics).</summary>
        public int DrawnCount { get; private set; }

        /// <summary>Submit this frame's bubbles for <paramref name="cam"/> (also callable from an editor render check).</summary>
        public void Draw(Camera cam)
        {
            if (_mesh == null) { _mesh = SharedSphere(); }
            if (_material == null)
            {
                Shader sh = Shader.Find("FlyingGame/Bubble") ?? Shader.Find("Unlit/Color");
                _material = new Material(sh) { enableInstancing = true };
                _props = new MaterialPropertyBlock();
                _batchProps = new MaterialPropertyBlock();
            }
            if (Follow == null) return;
            DrawnCount = 0;
            Shader.SetGlobalVector(SunDirId, _sun != null ? -_sun.transform.forward : new Vector3(0.4f, 0.8f, -0.4f).normalized);

            // Steady wind drifts the whole air mass over the ground, so bubbles slide relative to the
            // runway — you SEE the headwind/crosswind, not just feel it.
            var steady = FlyingGame.Core.Atmosphere.SteadyWind * FlyingGame.Core.Atmosphere.WindGradientFactor(CoordinateMap.ToSim(Follow.position));
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
            bool dense = SessionSettings.BubbleInstancing;
            int half = dense ? HalfCount : 4;
            float spacing = dense ? Spacing : 12f;
            float bubbleSize = dense ? BubbleSize : 0.9f;
            float reach = dense ? FadeRangeM : half * spacing;
            int samplesLeft = SamplesPerFrame;
            float now = Time.time;
            _batchCount = 0;
            for (int ix = -half; ix <= half; ix++)
            for (int iy = -half; iy <= half; iy++)
            for (int iz = -half; iz <= half; iz++)
            {
                var lattice = new Vector3Int(
                    Mathf.RoundToInt((center.x - _airMassOrigin.x) / spacing) + ix,
                    Mathf.RoundToInt((center.y - _airMassOrigin.y) / spacing) + iy,
                    Mathf.RoundToInt((center.z - _airMassOrigin.z) / spacing) + iz);
                // Per-cell hash: jitter, phases, and the thinning draw beyond the full-strength range.
                uint h2 = (uint)(lattice.x * 73856093) ^ (uint)(lattice.y * 19349663) ^ (uint)(lattice.z * 83492791);
                h2 ^= h2 >> 13; h2 *= 0x85EBCA6Bu; h2 ^= h2 >> 16;
                Vector3 pos = _airMassOrigin + new Vector3(lattice.x, lattice.y, lattice.z) * spacing;
                if (dense)
                {
                    // Jitter each bubble off its lattice point (fixed per cell) so the air reads as air, not a grid.
                    uint j = h2 * 2654435761u;
                    pos += new Vector3(((j & 0x3FF) / 1023f - 0.5f), (((j >> 10) & 0x3FF) / 1023f - 0.5f), (((j >> 20) & 0x3FF) / 1023f - 0.5f)) * (2f * JitterFraction * spacing);
                }
                float dist0 = Vector3.Distance(pos, center);
                if (dist0 > reach) continue;   // spherical block, not cubic — fewer bubbles, rounder falloff
                float distFade = dense ? Mathf.Clamp01((dist0 - FullRangeM) / Mathf.Max(1f, FadeRangeM - FullRangeM)) : 0f;
                if (distFade > 0f && ((h2 >> 8) & 0xFF) / 255f < distFade) continue;   // thin out with the fade so the count stays sane

                // Atmosphere at this cell, from the cache (refreshed a slice per frame) — the same field the wings feel.
                var simPos = CoordinateMap.ToSim(pos);
                if (!_cells.TryGetValue(lattice, out Cell cell) || (now - cell.Time > SampleRefreshS && samplesLeft > 0))
                {
                    if (samplesLeft <= 0 && !_cells.ContainsKey(lattice)) { cell = new Cell { Local = Vector3.zero, Rho = rho0, TempF = StandardF, Time = now - SampleRefreshS }; }
                    else
                    {
                        samplesLeft--;
                        var local = FlyingGame.Core.Atmosphere.MeanWindAtPosition(simPos) - steady;
                        cell = new Cell
                        {
                            Local = new Vector3((float)local.X, (float)local.Y, (float)local.Z),
                            Rho = (float)FlyingGame.Core.Atmosphere.DensityAtPosition(simPos),
                            TempF = KelvinToF((float)FlyingGame.Core.Atmosphere.TemperatureAtPosition(simPos)),
                            Time = now,
                        };
                    }
                    _cells[lattice] = cell;
                }

                // Turbulence made visible: each bubble is displaced by the LOCAL gust — eddies show as
                // clusters of bubbles swirling together, and the aircraft visibly flies through moving air.
                if (Turbulence != null && dist0 < FullRangeM)
                {
                    FlyingGame.Core.MathTypes.Vec3 gust = Turbulence.WindAt(simPos, FlyingGame.Core.Atmosphere.SimTimeSec);
                    pos += CoordinateMap.ToUnity(gust) * GustDisplayScale;
                }
                // Thermals and slope lift made visible: the LOCAL air motion (everything but the steady wind, which
                // already carries the whole lattice) moves each bubble along its streamline for a few seconds, then it
                // re-seeds at its lattice point — per-bubble phases make the column read as a continuous stream.
                float streamTau = 0f;
                Vector3 localSim = cell.Local;
                float localSpeed = localSim.magnitude;
                if (localSpeed > 0.15f)
                {
                    float phase = (h2 & 0xFFFF) / 65535f;
                    streamTau = Mathf.Repeat(now / StreamPeriodS + phase, 1f);
                    pos += CoordinateMap.ToUnity(new FlyingGame.Core.MathTypes.Vec3(localSim.x, localSim.y, localSim.z)) * (streamTau * StreamPeriodS);
                }

                float dist = Vector3.Distance(pos, center);
                float edgeScale = dense ? 1f : 1f - Mathf.Clamp01((dist / reach - FadeStartFraction) / (1f - FadeStartFraction));

                // Density → size, temperature → tint, from the same atmosphere the wings fly in.
                float sizeScale = Mathf.Clamp(1f + (cell.Rho / rho0 - 1f) * DensitySizeGain, MinSizeScale, MaxSizeScale);
                float size = bubbleSize * sizeScale * edgeScale;
                if (size < 0.02f) continue;
                if (dense && cam != null)
                {
                    // Never below MinPixels on screen: a 0.2 m bubble 100 m out is sub-pixel and simply vanishes.
                    float camDist = Vector3.Distance(pos, cam.transform.position);
                    float minSize = camDist * (2f * tanHalfV) * (MinPixels / Mathf.Max(200f, cam.pixelHeight));
                    if (size < minSize) size = minSize;
                }

                Color tint = TintFor(cell.TempF);
                // Lift / sink made obvious (owner): rising air blinks GREEN, sinking air blinks ORANGE (the variometer
                // colours) — faster and brighter the stronger it is. Tinted bubbles stay visible at any distance.
                float w = -localSim.z;   // up positive (sim z is down)
                float liftBlink = 1f;
                bool lifting = w > LiftShowMs || w < -LiftShowMs;
                if (lifting)
                {
                    float strength = Mathf.Clamp01((Mathf.Abs(w) - LiftShowMs) / 4f);
                    float hz = 1.5f + 6.5f * strength;
                    float ph = (h2 & 0xFFFF) / 65535f;
                    liftBlink = 0.5f + 0.5f * (0.5f + 0.5f * Mathf.Sin((now * hz + ph) * 2f * Mathf.PI));   // never below half
                    Color c = w > 0 ? LiftTint : SinkTint;
                    tint = Color.Lerp(tint, c, 0.5f + 0.5f * strength) * (1f + 1.2f * strength);
                }

                // Distance fade (plain air only): full to 250 ft, gone by 500 ft.
                float alpha = lifting ? 1f : 1f - distFade;

                // Centre-of-frame fade: bubbles nearer the camera than the aircraft (they've flowed past
                // it) fade with how far past they are — but only inside the centre circle.
                if (cam != null)
                {
                    Vector3 cs = worldToCam.MultiplyPoint3x4(pos);
                    float depth = -cs.z;
                    if (depth <= 0.05f) continue; // behind the lens
                    float past = aircraftDepth - depth;
                    if (past > 0f)
                    {
                        float ny = cs.y / (depth * tanHalfV) * 0.5f;
                        float nx = cs.x / (depth * tanHalfV) * 0.5f;
                        float r = Mathf.Sqrt(nx * nx + ny * ny);
                        float inCircle = 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(CenterCircleFraction - CenterCircleFeather, CenterCircleFraction + CenterCircleFeather, r));
                        float pastFade = Mathf.Clamp01(past / Mathf.Max(0.01f, PastFadeDepthM));
                        alpha *= 1f - inCircle * pastFade;
                    }
                }

                // Fade the streaming bubble out just before it re-seeds so the jump back is invisible.
                if (streamTau > 0.75f) alpha *= 1f - (streamTau - 0.75f) / 0.25f;
                alpha *= liftBlink;
                if (alpha < 0.02f) continue;
                float body = lifting ? 0.10f : (dense ? PlainBodyAlpha : 0.06f);   // lift/sink bubbles 10 % (owner); plain air a soft dot in the dense field

                DrawnCount++;
                if (dense)
                {
                    _mats[_batchCount] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size);
                    _cols[_batchCount] = tint;
                    _alphas[_batchCount] = alpha;
                    _bodies[_batchCount] = body;
                    if (++_batchCount == Batch) FlushBatch(cam);
                }
                else
                {
                    _props.SetColor(ColorId, tint);
                    _props.SetFloat(AlphaId, alpha);
                    _props.SetFloat(BodyAlphaId, body);
                    Graphics.DrawMesh(_mesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size), _material, 0,
                        cam, 0, _props, false, false, false);
                }
            }
            if (_batchCount > 0) FlushBatch(cam);

            // Drop cells nobody has touched for a while (the aircraft moved on).
            if (Time.frameCount % 120 == 0)
            {
                _stale.Clear();
                foreach (var kv in _cells) if (now - kv.Value.Time > 6f) _stale.Add(kv.Key);
                foreach (var k in _stale) _cells.Remove(k);
            }
        }

        private void FlushBatch(Camera cam)
        {
            _batchProps.SetVectorArray(ColorId, _cols);
            _batchProps.SetFloatArray(AlphaId, _alphas);
            _batchProps.SetFloatArray(BodyAlphaId, _bodies);
            Graphics.DrawMeshInstanced(_mesh, 0, _material, _mats, _batchCount, _batchProps,
                UnityEngine.Rendering.ShadowCastingMode.Off, false, 0, cam);
            _batchCount = 0;
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

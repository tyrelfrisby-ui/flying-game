using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The air made visible (ARCHITECTURE.md #1 visual + owner request): bubbles FIXED IN THE AIR MASS around the
    /// aircraft. Dense field (owner 2026-10-02: "no planes, no gaps"): a BLUE-NOISE point set — even but irregular,
    /// like dust in air — in a 64 m cube that tiles seamlessly, tilted at odd angles so no heading or climb looks down a
    /// row; only the tiles near the aircraft are visited, so the field feels infinite. Bubbles translate
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
        public float TileSizeM = 64f;            // dense field: the repeating blue-noise cube (one bubble per Spacing³ on average)
        public int TileCandidates = 15;          // best-candidate sampling: tries per point (more = more even)
        public int TileSeed = 20261002;
        public bool VaryTiles = true;            // each tile gets its own rotation/mirror + shift of the cube (no repeats to line up)
        [Header("Speed streaks (owner 2026-10-02: like snow past a jet)")]
        public float StreakShutterS = 0.05f;     // streak length = airspeed × this (≈1 m at 20 m/s, 5 m at 100 m/s)
        public float StreakDim = 0.6f;           // how much a long streak dims (its light is spread along its length)
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
        public float PastMinAlpha = 0.18f;         // ... to this fraction, never to nothing (flying-through-snow)

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
        // Blue-noise tile (dense field) and this frame's candidate bubbles.
        private Vector3[] _tile; private float _tileSpacing = -1f;
        private Quaternion _tileRot = Quaternion.identity, _tileRotInv = Quaternion.identity;
        private readonly List<(Vector3 pos, Vector3Int key, uint hash)> _cand = new();
        private Vector3 _streak;                 // this frame: world vector from a bubble back along its motion past the aircraft
        /// <summary>Render check / tools: the airspeed vector (Unity world) to streak with when there is no aircraft driver.</summary>
        public Vector3? StreakVelocityOverride;
        private FlightSimDriver _driver;

        /// <summary>A bubble drawn as a streak: the sphere stretched back along the way it is moving past the aircraft
        /// (snow past a jet) — length ∝ airspeed. Short streaks stay round.</summary>
        private Matrix4x4 BubbleMatrix(Vector3 pos, float size, ref float alpha)
        {
            float L = _streak.magnitude;
            if (L < size * 0.5f) return Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size);
            alpha *= Mathf.Lerp(1f, size / (size + L), StreakDim);
            return Matrix4x4.TRS(pos + _streak * 0.5f, Quaternion.LookRotation(_streak / L), new Vector3(size, size, size + L));
        }

        /// <summary>Mitchell's best-candidate sampling in a periodic cube: each new point is the farthest (toroidally) of a
        /// handful of random tries from every point so far — even spacing without any lattice. Deterministic.</summary>
        private void EnsureTile(float spacing)
        {
            if (_tile != null && Mathf.Approximately(_tileSpacing, spacing)) return;
            _tileSpacing = spacing;
            float T = TileSizeM;
            int n = Mathf.Max(8, Mathf.RoundToInt(T * T * T / (spacing * spacing * spacing)));
            var rng = new System.Random(TileSeed);
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 best = default; float bestD = -1f;
                int k = i == 0 ? 1 : TileCandidates;
                for (int c = 0; c < k; c++)
                {
                    var p = new Vector3((float)rng.NextDouble() * T, (float)rng.NextDouble() * T, (float)rng.NextDouble() * T);
                    float dmin = float.MaxValue;
                    for (int j = 0; j < i; j++)
                    {
                        float dx = Mathf.Abs(p.x - pts[j].x), dy = Mathf.Abs(p.y - pts[j].y), dz = Mathf.Abs(p.z - pts[j].z);
                        dx = Mathf.Min(dx, T - dx); dy = Mathf.Min(dy, T - dy); dz = Mathf.Min(dz, T - dz);
                        float d = dx * dx + dy * dy + dz * dz;
                        if (d < dmin) { dmin = d; if (dmin < bestD) break; }
                    }
                    if (dmin > bestD) { bestD = dmin; best = p; }
                }
                pts[i] = best;
            }
            _tile = pts;
            _tileRot = Quaternion.Euler(23.7f, 41.3f, 17.9f);   // odd angles: no row lines up with a runway heading or the vertical
            _tileRotInv = Quaternion.Inverse(_tileRot);
        }

        /// <summary>This frame's bubbles: positions (world), the atmosphere-cache cell they belong to, and a per-bubble hash.</summary>
        private void BuildCandidates(bool dense, Vector3 center, float spacing, int half, float reach)
        {
            _cand.Clear();
            if (!dense)
            {
                // Sparse fallback: the plain lattice.
                for (int ix = -half; ix <= half; ix++)
                for (int iy = -half; iy <= half; iy++)
                for (int iz = -half; iz <= half; iz++)
                {
                    var lattice = new Vector3Int(
                        Mathf.RoundToInt((center.x - _airMassOrigin.x) / spacing) + ix,
                        Mathf.RoundToInt((center.y - _airMassOrigin.y) / spacing) + iy,
                        Mathf.RoundToInt((center.z - _airMassOrigin.z) / spacing) + iz);
                    _cand.Add((_airMassOrigin + new Vector3(lattice.x, lattice.y, lattice.z) * spacing, lattice, Hash(lattice.x, lattice.y, lattice.z, 0)));
                }
                return;
            }
            EnsureTile(spacing);
            float T = TileSizeM, r2 = reach * reach;
            Vector3 rel = _tileRotInv * (center - _airMassOrigin);   // the aircraft in the tile frame
            int x0 = Mathf.FloorToInt((rel.x - reach) / T), x1 = Mathf.FloorToInt((rel.x + reach) / T);
            int y0 = Mathf.FloorToInt((rel.y - reach) / T), y1 = Mathf.FloorToInt((rel.y + reach) / T);
            int z0 = Mathf.FloorToInt((rel.z - reach) / T), z1 = Mathf.FloorToInt((rel.z + reach) / T);
            for (int tx = x0; tx <= x1; tx++)
            for (int ty = y0; ty <= y1; ty++)
            for (int tz = z0; tz <= z1; tz++)
            {
                Vector3 o = new Vector3(tx, ty, tz) * T;
                // Skip tiles wholly outside the sphere.
                float ddx = Mathf.Max(0f, Mathf.Max(o.x - rel.x, rel.x - (o.x + T)));
                float ddy = Mathf.Max(0f, Mathf.Max(o.y - rel.y, rel.y - (o.y + T)));
                float ddz = Mathf.Max(0f, Mathf.Max(o.z - rel.z, rel.z - (o.z + T)));
                if (ddx * ddx + ddy * ddy + ddz * ddz > r2) continue;
                // Owner 2026-10-02 (screenshot): one cube repeated every 64 m lines each bubble up with its own copies, and
                // over the ~150 m in view those copies form dotted rays converging on the cube-edge directions. So every
                // tile uses its own variant of the cube — one of its 48 rotations/mirrors plus a wrap-around shift (both
                // keep the blue-noise spacing inside the tile) — and no bubble has a copy 64 m away any more.
                uint th = Hash(tx, ty, tz, -7);
                int perm = (int)(th % 6u), flips = (int)((th >> 3) & 7u);
                Vector3 shift = VaryTiles
                    ? new Vector3(((th >> 6) & 1023u) / 1024f, ((th >> 16) & 1023u) / 1024f, (Hash(tz, tx, ty, 11) & 1023u) / 1024f) * T
                    : Vector3.zero;
                for (int i = 0; i < _tile.Length; i++)
                {
                    Vector3 q = _tile[i];
                    if (VaryTiles)
                    {
                        q = perm switch
                        {
                            0 => q, 1 => new Vector3(q.y, q.z, q.x), 2 => new Vector3(q.z, q.x, q.y),
                            3 => new Vector3(q.y, q.x, q.z), 4 => new Vector3(q.x, q.z, q.y), _ => new Vector3(q.z, q.y, q.x),
                        };
                        if ((flips & 1) != 0) q.x = T - q.x;
                        if ((flips & 2) != 0) q.y = T - q.y;
                        if ((flips & 4) != 0) q.z = T - q.z;
                        q += shift;
                        q.x = Mathf.Repeat(q.x, T); q.y = Mathf.Repeat(q.y, T); q.z = Mathf.Repeat(q.z, T);
                    }
                    Vector3 local = o + q;
                    if ((local - rel).sqrMagnitude > r2) continue;
                    var key = new Vector3Int(Mathf.FloorToInt(local.x / spacing), Mathf.FloorToInt(local.y / spacing), Mathf.FloorToInt(local.z / spacing));
                    _cand.Add((_airMassOrigin + _tileRot * local, key, Hash(tx, ty, tz, i + 1)));
                }
            }
        }

        private static uint Hash(int a, int b, int c, int d)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791) ^ (uint)(d * 2654435761u);
            h ^= h >> 13; h *= 0x85EBCA6Bu; h ^= h >> 16;
            return h;
        }
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

        private PilotEgress _egress;
        private void LateUpdate()
        {
            if (!SessionSettings.BubblesOn) return;
            Draw(Camera.main);
        }

        /// <summary>Drawn bubbles last call (diagnostics).</summary>
        public int DrawnCount { get; private set; }

        /// <summary>Diagnostics: how many of this frame's bubbles have an exact copy one tile edge away (the repeats that
        /// line up into rays). Should be ~0 with <see cref="VaryTiles"/>.</summary>
        public int CountTileRepeats()
        {
            var set = new HashSet<Vector3Int>();
            foreach (var c in _cand) set.Add(Q(_tileRotInv * (c.pos - _airMassOrigin)));
            int n = 0;
            foreach (var c in _cand)
            {
                Vector3 r = _tileRotInv * (c.pos - _airMassOrigin);
                if (set.Contains(Q(r + new Vector3(TileSizeM, 0, 0))) || set.Contains(Q(r + new Vector3(0, TileSizeM, 0)))
                    || set.Contains(Q(r + new Vector3(0, 0, TileSizeM)))) n++;
            }
            return n;
            static Vector3Int Q(Vector3 v) => new(Mathf.RoundToInt(v.x * 20f), Mathf.RoundToInt(v.y * 20f), Mathf.RoundToInt(v.z * 20f));
        }

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
            // Bailed out: no bubble cloud around the abandoned aircraft (owner 2026-09-15: it was enshrined in it).
            _egress ??= Follow.GetComponent<PilotEgress>();
            if (_egress != null && _egress.PilotOut) return;
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
            // Streaks: relative to the aircraft the air (and every bubble in it) moves at minus its airspeed, so a bubble's
            // streak trails back toward +airspeed from where it is now.
            _driver ??= Follow.GetComponent<FlightSimDriver>();
            Vector3 airVel = StreakVelocityOverride ?? (_driver != null ? _driver.AirVelocityUnity : Vector3.zero);
            _streak = SessionSettings.BubbleStreaks ? airVel * StreakShutterS : Vector3.zero;
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
            BuildCandidates(dense, center, spacing, half, reach);
            for (int ci = 0; ci < _cand.Count; ci++)
            {
                // Per-bubble: position, the atmosphere-cache cell, and a hash for phases and the thinning draw.
                (Vector3 pos, Vector3Int lattice, uint h2) = _cand[ci];
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
                        // Owner 2026-09-11: bubbles that have flowed past the aircraft dim hard but stay — the snow-through-
                        // the-headlights feel — instead of vanishing inside the centre circle.
                        alpha *= 1f - inCircle * pastFade * (1f - PastMinAlpha);
                    }
                }

                // Fade the streaming bubble out just before it re-seeds so the jump back is invisible.
                if (streamTau > 0.75f) alpha *= 1f - (streamTau - 0.75f) / 0.25f;
                alpha *= liftBlink;
                if (alpha < 0.02f) continue;
                // Owner 2026-10-03: lift/sink air looks like the rest of the snow (same soft dot), just tinted and blinking.
                float body = dense ? PlainBodyAlpha : 0.06f;

                DrawnCount++;
                if (dense)
                {
                    _mats[_batchCount] = BubbleMatrix(pos, size, ref alpha);
                    _cols[_batchCount] = tint;
                    _alphas[_batchCount] = alpha;
                    _bodies[_batchCount] = body;
                    if (++_batchCount == Batch) FlushBatch(cam);
                }
                else
                {
                    Matrix4x4 m = BubbleMatrix(pos, size, ref alpha);
                    _props.SetColor(ColorId, tint);
                    _props.SetFloat(AlphaId, alpha);
                    _props.SetFloat(BodyAlphaId, body);
                    Graphics.DrawMesh(_mesh, m, _material, 0,
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

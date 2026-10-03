using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// "Show lift" (owner 2026-10-03): rising air is drawn as soap bubbles that RISE at 10x the air's real vertical speed,
    /// bigger the stronger the lift — a thermal reads as small, gently rising bubbles round its edge and big, fast ones up
    /// a core that looks like a column of very fast air. Sinking air shows nothing. Independent of the stationary snow
    /// (BubbleField): snow on = both; snow off = a clear sky with just the lift bubbles.
    ///
    /// Columns on a jittered 18 m grid within <see cref="RadiusM"/> of the aircraft each sample the air's vertical speed
    /// (cached, refreshed round-robin); a lifting column carries <see cref="PerColumn"/> bubbles cycling up through a
    /// <see cref="BandM"/> band around the aircraft's height, fading in at the bottom and out at the top.
    /// </summary>
    public sealed class LiftBubbles : MonoBehaviour
    {
        public Transform Follow;
        public float RadiusM = 420f;
        public float GridM = 18f;
        public float BandM = 260f;              // the band the bubbles rise through, centred on the aircraft's height
        public int PerColumn = 4;
        public float SpeedFactor = 10f;         // owner: 10x the actual vertical speed
        public float ThresholdMs = 0.25f;       // rising air slower than this isn't shown
        public float RefreshS = 2f;
        public int SamplesPerFrame = 250;

        private struct Col { public Vector2 xz; public float w; public float t; public uint h; }
        private readonly Dictionary<Vector2Int, Col> _cols = new();
        private readonly List<Vector2Int> _keys = new();
        private int _cursor;
        private Mesh _mesh; private Material _mat;
        private readonly Matrix4x4[] _m = new Matrix4x4[1023];
        private readonly float[] _a = new float[1023];
        private MaterialPropertyBlock _mpb;
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha"), BodyId = Shader.PropertyToID("_BodyAlpha");
        private FlightSimDriver _driver;

        private void Start()
        {
            _mesh = BubbleField.SharedSphere();
            _mat = new Material(Shader.Find("FlyingGame/Bubble") ?? Shader.Find("Unlit/Color")) { enableInstancing = true };
            if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", new Color(0.92f, 0.97f, 1f));
            _mpb = new MaterialPropertyBlock();
        }

        private static uint Hash(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13; h *= 0x85EBCA6Bu; h ^= h >> 16;
            return h;
        }

        private void LateUpdate()
        {
            if (Follow == null || SessionSettings.MenuOpen) return;
            _driver ??= Follow.GetComponent<FlightSimDriver>();
            if (!SessionSettings.LiftMarkersVisible(_driver != null ? _driver.AircraftId : SessionSettings.AircraftId)) return;
            Vector3 p = Follow.position;
            float now = Time.time;

            // Column set around the aircraft (world grid, so columns stay put as it flies).
            int r = Mathf.CeilToInt(RadiusM / GridM);
            int cx = Mathf.RoundToInt(p.x / GridM), cz = Mathf.RoundToInt(p.z / GridM);
            _keys.Clear();
            for (int i = -r; i <= r; i++)
                for (int j = -r; j <= r; j++)
                {
                    if (i * i + j * j > r * r) continue;
                    var k = new Vector2Int(cx + i, cz + j);
                    _keys.Add(k);
                    if (!_cols.ContainsKey(k))
                    {
                        uint h = Hash(k.x, k.y);
                        float jx = ((h & 1023u) / 1023f - 0.5f) * GridM, jz = (((h >> 10) & 1023u) / 1023f - 0.5f) * GridM;
                        _cols[k] = new Col { xz = new Vector2(k.x * GridM + jx, k.y * GridM + jz), w = 0f, t = -999f, h = h };
                    }
                }
            if (_cols.Count > _keys.Count * 3) { var keep = new HashSet<Vector2Int>(_keys); foreach (var k in new List<Vector2Int>(_cols.Keys)) if (!keep.Contains(k)) _cols.Remove(k); }

            // Refresh a slice of the columns' vertical air speed (at the aircraft's height).
            for (int n = 0; n < SamplesPerFrame && _keys.Count > 0; n++)
            {
                _cursor = (_cursor + 1) % _keys.Count;
                Vector2Int k = _keys[_cursor];
                Col c = _cols[k];
                if (now - c.t < RefreshS) continue;
                var sim = CoordinateMap.ToSim(new Vector3(c.xz.x, p.y, c.xz.y));
                var local = FlyingGame.Core.Atmosphere.MeanWindAtPosition(sim) - FlyingGame.Core.Atmosphere.SteadyWind * FlyingGame.Core.Atmosphere.WindGradientFactor(sim);
                c.w = -(float)local.Z; c.t = now;
                _cols[k] = c;
            }

            // Draw: each lifting column carries PerColumn bubbles cycling up through the band at 10x w.
            Camera cam = Camera.main;
            float pxH = cam != null ? 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(200f, cam.pixelHeight) : 0.001f;
            float bandBase = Mathf.Floor((p.y - BandM * 0.5f) / 20f) * 20f;   // snapped so the band doesn't slide with tiny height changes
            int count = 0;
            foreach (Vector2Int k in _keys)
            {
                Col c = _cols[k];
                if (c.w < ThresholdMs) continue;
                float rise = SpeedFactor * c.w;                                   // m/s on screen
                float size = Mathf.Clamp(0.5f + 0.55f * c.w, 0.5f, 4f);         // bigger with stronger lift
                for (int b = 0; b < PerColumn; b++)
                {
                    float phase = ((c.h >> (b * 5)) & 1023u) / 1023f;
                    float u = Mathf.Repeat(now * rise / BandM + phase + b / (float)PerColumn, 1f);   // 0 bottom .. 1 top
                    var pos = new Vector3(c.xz.x, bandBase + u * BandM, c.xz.y);
                    float alpha = Mathf.Clamp01(u / 0.12f) * Mathf.Clamp01((1f - u) / 0.12f);       // fade in/out at the band ends
                    float d = Vector3.Distance(pos, p);
                    alpha *= 1f - Mathf.Clamp01((d - RadiusM * 0.75f) / (RadiusM * 0.25f));
                    if (alpha < 0.03f) continue;
                    float sz = size;
                    if (cam != null) sz = Mathf.Max(sz, Vector3.Distance(pos, cam.transform.position) * pxH * 6f);   // never sub-pixel
                    _m[count] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * sz);
                    _a[count] = alpha;
                    if (++count == _m.Length) { Flush(cam, count); count = 0; }
                }
            }
            if (count > 0) Flush(cam, count);
        }

        private void Flush(Camera cam, int n)
        {
            _mpb.SetFloatArray(AlphaId, _a);
            _mpb.SetFloat(BodyId, 0.08f);
            Graphics.DrawMeshInstanced(_mesh, 0, _mat, _m, n, _mpb, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0, cam);
        }
    }
}

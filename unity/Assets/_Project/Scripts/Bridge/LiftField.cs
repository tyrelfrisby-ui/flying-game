using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The air's lift and sink made visible from a distance (owner): a coarse lattice (35 m) out to ~700 m
    /// around the aircraft; every cell whose MEAN air motion rises or sinks faster than a threshold shows a
    /// blinking bubble — GREEN for lift, ORANGE for sink (the variometer colours), blinking faster and glowing brighter the stronger the
    /// air. Thermal columns read as green towers ringed with orange; the windward wall glows green, the lee orange.
    /// The lattice is sampled round-robin (a slice per frame) so the phone never evaluates it all at once.
    /// </summary>
    public sealed class LiftField : MonoBehaviour
    {
        public Transform Follow;
        public float Spacing = 35f;
        public int HalfCount = 20;             // (2·20+1)³ lattice, sphere-culled
        public float ThresholdMs = 0.6f;       // show cells rising/sinking faster than this
        public float BubbleSize = 4f;
        public int SamplesPerFrame = 900;

        private Mesh _mesh; private Material _material; private MaterialPropertyBlock _props;
        private Vector3Int _anchor;            // lattice cell the aircraft is in (world lattice, so cells are stable)
        private float[] _w;                    // sampled vertical air speed per cell (NaN = not yet)
        private int _n, _cursor;
        private static readonly int ColorId = Shader.PropertyToID("_Color"), AlphaId = Shader.PropertyToID("_Alpha");

        private void Start()
        {
            _mesh = BubbleField.SharedSphere();
            _material = new Material(Shader.Find("FlyingGame/Bubble") ?? Shader.Find("Unlit/Color"));
            _props = new MaterialPropertyBlock();
            int side = 2 * HalfCount + 1; _n = side * side * side;
            _w = new float[_n];
            for (int i = 0; i < _n; i++) _w[i] = float.NaN;
        }

        private Vector3 CellWorld(int idx)
        {
            int side = 2 * HalfCount + 1;
            int iz = idx / (side * side), rem = idx - iz * side * side, iy = rem / side, ix = rem - iy * side;
            return new Vector3((_anchor.x + ix - HalfCount) * Spacing, (_anchor.y + iy - HalfCount) * Spacing, (_anchor.z + iz - HalfCount) * Spacing);
        }

        private void LateUpdate()
        {
            if (Follow == null || _w == null || SessionSettings.MenuOpen) return;
            Vector3 p = Follow.position;
            var anchor = new Vector3Int(Mathf.RoundToInt(p.x / Spacing), Mathf.RoundToInt(p.y / Spacing), Mathf.RoundToInt(p.z / Spacing));
            if (anchor != _anchor)
            {
                // Re-anchor: shift the cache so cells keep their samples where they still fall inside the window.
                var shifted = new float[_n]; int side = 2 * HalfCount + 1; Vector3Int d = anchor - _anchor;
                for (int i = 0; i < _n; i++)
                {
                    int iz = i / (side * side), rem = i - iz * side * side, iy = rem / side, ix = rem - iy * side;
                    int ox = ix + d.x, oy = iy + d.y, oz = iz + d.z;
                    shifted[i] = ox >= 0 && ox < side && oy >= 0 && oy < side && oz >= 0 && oz < side ? _w[(oz * side + oy) * side + ox] : float.NaN;
                }
                _w = shifted; _anchor = anchor;
            }
            // Sample a slice.
            var steady = FlyingGame.Core.Atmosphere.SteadyWind;
            for (int k = 0; k < SamplesPerFrame; k++)
            {
                int i = _cursor; _cursor = (_cursor + 1) % _n;
                Vector3 c = CellWorld(i);
                if (c.y < 0f) { _w[i] = 0f; continue; }
                var sim = CoordinateMap.ToSim(c);
                var local = FlyingGame.Core.Atmosphere.MeanWindAtPosition(sim) - steady;
                float ground = (float)FlyingGame.Core.WorldTerrain.GroundHeightAt(sim.X, sim.Y);
                _w[i] = c.y < ground ? 0f : -(float)local.Z;
            }
            // Draw the live cells.
            float radius = HalfCount * Spacing, t = Time.time;
            for (int i = 0; i < _n; i++)
            {
                float w = _w[i];
                if (float.IsNaN(w) || (w < ThresholdMs && w > -ThresholdMs)) continue;
                Vector3 c = CellWorld(i);
                float dist = Vector3.Distance(c, p);
                if (dist > radius || dist < 60f) continue;   // the near field's own bubbles take over close in
                float strength = Mathf.Clamp01((Mathf.Abs(w) - ThresholdMs) / 4f);
                float hz = 1.5f + 6.5f * strength;
                float ph = ((i * 2654435761u) & 0xFFFF) / 65535f;
                float blink = 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Sin((t * hz + ph) * 2f * Mathf.PI));
                Color col = (w > 0 ? BubbleField.LiftTint : BubbleField.SinkTint) * (0.8f + 1.4f * strength);
                float edge = 1f - Mathf.Clamp01((dist / radius - 0.7f) / 0.3f);
                _props.SetColor(ColorId, col);
                _props.SetFloat(AlphaId, 0.55f * blink * edge);
                Graphics.DrawMesh(_mesh, Matrix4x4.TRS(c, Quaternion.identity, Vector3.one * (BubbleSize * (0.7f + 0.6f * strength))), _material, 0, null, 0, _props, false, false);
            }
        }
    }
}

using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The floatplanes' / flying boats' wake on the water (owner 2026-10-07): a wave-equation height field on the GPU over the
    /// lake the aircraft is on (the harbor or a lake), forced at each float's step and bow (or the hull's) while it is in the
    /// water. A source moving faster than the wave speed trails a V (half-angle asin(c / V); c = 6 m/s puts a planing float's V
    /// near the Kelvin 19.5°); slower, the rings spread ahead and round it. The shore holds the water still, so the waves
    /// reflect off it; they decay over ~100 s and the field keeps running for 5 minutes after the last disturbance. The water
    /// shader (FlyingGame/Water) bends its normal and reflection by the field's slope, and froths the steepest crests.
    /// </summary>
    public sealed class WakeWaves : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public const float WaveSpeedMs = 6f, StepSec = 0.05f, DecaySec = 100f, LastsSec = 300f;

        private Material _sim;
        private RenderTexture _prev, _curr, _next;
        private int _lake = -1;
        private float _cell, _width, _depth, _acc, _sinceForcing = 1e9f;
        private Vector2 _min;   // Unity xz of the grid's corner
        private readonly Vector4[] _src = new Vector4[8];
        private static readonly int WakeTexId = Shader.PropertyToID("_WakeTex"), WakeRectId = Shader.PropertyToID("_WakeRect"),
            WakeOnId = Shader.PropertyToID("_WakeOn"), WakeYId = Shader.PropertyToID("_WakeY"), WakeTexelId = Shader.PropertyToID("_WakeTexel");

        private void Start()
        {
            Shader sh = Shader.Find("Hidden/FlyingGame/WaveSim");
            if (sh == null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)) { enabled = false; Shader.SetGlobalFloat(WakeOnId, 0f); return; }
            _sim = new Material(sh);
            Shader.SetGlobalFloat(WakeOnId, 0f);
        }

        private void Update()
        {
            var ac = Driver != null && Driver.Sim != null ? Driver.Sim.Aircraft : null;
            if (ac == null) return;
            // The lake under / nearest the aircraft (within 1.3 radii): the wake lives on that one.
            var p = ac.State.Position;
            int best = -1; double bestR = 1.3;
            for (int i = 0; i < WorldTerrain.Lakes.Length; i++) { double r = WorldTerrain.Lakes[i].Inside(p.X, p.Y); if (r < bestR) { bestR = r; best = i; } }
            bool forcing = ac.Config.Floats != null && (FloatHydro.Floats[0].Wet || FloatHydro.Floats[1].Wet);
            if (best >= 0 && best != _lake && (forcing || _lake < 0)) Allocate(best);
            if (_lake < 0) return;
            if (forcing && best == _lake) _sinceForcing = 0f; else _sinceForcing += Time.deltaTime;
            if (_sinceForcing > LastsSec) { Release(); return; }

            _acc += Mathf.Min(Time.deltaTime, 0.25f);
            int n = 0;
            if (forcing && best == _lake) n = Sources(ac);
            while (_acc >= StepSec)
            {
                _acc -= StepSec;
                Step(n);
            }
            Shader.SetGlobalTexture(WakeTexId, _curr);
        }

        private void Allocate(int lake)
        {
            Release();
            var l = WorldTerrain.Lakes[lake];
            // ~2 m cells on a lake, coarser on the 3.2 km harbor (≤ 1024 across).
            _cell = Mathf.Max(2f, (float)(2 * l.Rx) / 1024f);
            // Sim x (north) → Unity z, sim y (east) → Unity x: the grid's u runs along Unity x (sim y), v along Unity z (sim x).
            Vector3 c = CoordinateMap.ToUnity(new FlyingGame.Core.MathTypes.Vec3(l.Cx, l.Cy, -l.SurfaceM));
            // Texture u spans the lake's sim-y extent (2·Ry), v its sim-x extent (2·Rx).
            int tw = Mathf.CeilToInt((float)(2 * l.Ry) / _cell), th = Mathf.CeilToInt((float)(2 * l.Rx) / _cell);
            _width = tw * _cell; _depth = th * _cell;
            _min = new Vector2(c.x - _width / 2, c.z - _depth / 2);
            _prev = Make(tw, th); _curr = Make(tw, th); _next = Make(tw, th);
            _lake = lake; _acc = 0f;
            Shader.SetGlobalVector(WakeRectId, new Vector4(_min.x, _min.y, 1f / _width, 1f / _depth));
            Shader.SetGlobalVector(WakeTexelId, new Vector4(1f / tw, 1f / th, _cell, 0));
            Shader.SetGlobalFloat(WakeYId, c.y);
            Shader.SetGlobalFloat(WakeOnId, 1f);
            _sim.SetFloat("_C2", Mathf.Pow(WaveSpeedMs * StepSec / _cell, 2f));
            _sim.SetFloat("_Damp", Mathf.Exp(-StepSec / DecaySec));
            _sim.SetFloat("_Aspect", (float)th / tw);
        }

        private static RenderTexture Make(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.RHalf) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            var was = RenderTexture.active; RenderTexture.active = rt; GL.Clear(false, true, Color.clear); RenderTexture.active = was;
            return rt;
        }

        private void Release()
        {
            foreach (var rt in new[] { _prev, _curr, _next }) if (rt != null) { rt.Release(); Destroy(rt); }
            _prev = _curr = _next = null; _lake = -1;
            Shader.SetGlobalFloat(WakeOnId, 0f);
        }

        /// <summary>Forcing points: each float's step and bow (or the hull's), sized by its beam, pushed by its speed and draft.</summary>
        private int Sources(FlyingGame.Sim.Aircraft ac)
        {
            var fl = ac.Config.Floats;
            int n = 0, floats = fl.SpreadM > 0.1 ? 2 : 1;
            float beam = (float)fl.BeamM;
            for (int i = 0; i < floats && n < 7; i++)
            {
                var f = FloatHydro.Floats[i];
                if (!f.Wet) continue;
                float speed = (float)f.SpeedMs;
                bool planing = f.PlaningLiftN > f.BuoyancyN && speed > 4f;
                // Height pushed per step: a displacement hull heaps water at the bow; planing, the step throws the bigger
                // wave. Scaled by beam (a 7.6 m H-4 hull makes a far bigger wake than a 0.66 m Cub float).
                float k = Mathf.Clamp01(speed / 12f) * Mathf.Clamp(beam, 0.5f, 8f);
                float step = (planing ? 0.020f : 0.010f) * k, bow = (planing ? 0.004f : 0.012f) * k;
                n = Add(n, f.StepWorld, beam * 0.6f, step);
                n = Add(n, f.BowWorld, beam * 0.5f, bow);
            }
            return n;
        }

        private int Add(int n, FlyingGame.Core.MathTypes.Vec3 sim, float radiusM, float amp)
        {
            Vector3 u = CoordinateMap.ToUnity(sim);
            float uu = (u.x - _min.x) / _width, vv = (u.z - _min.y) / _depth;
            if (uu < 0 || uu > 1 || vv < 0 || vv > 1) return n;
            _src[n] = new Vector4(uu, vv, Mathf.Max(radiusM, _cell * 0.8f) / _width, amp);
            return n + 1;
        }

        private void Step(int sources)
        {
            _sim.SetTexture("_Prev", _prev);
            _sim.SetInt("_SrcCount", sources);
            _sim.SetVectorArray("_Src", _src);
            Graphics.Blit(_curr, _next, _sim);
            var t = _prev; _prev = _curr; _curr = _next; _next = t;
        }

        private void OnDestroy() => Release();
    }
}

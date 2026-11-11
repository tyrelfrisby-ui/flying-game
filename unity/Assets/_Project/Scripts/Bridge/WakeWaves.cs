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

        private Material _sim, _foamSim;
        private RenderTexture _foam, _foamNext;
        private RenderTexture _prev, _curr, _next;
        private int _lake = -1;
        private float _cell, _width, _depth, _acc, _sinceForcing = 1e9f;
        private Vector2 _min;   // Unity xz of the grid's corner
        private readonly Vector4[] _foamOrigins = new Vector4[2], _foamMotion = new Vector4[2], _foamWake = new Vector4[2], _foamTrack = new Vector4[2];
        private readonly Vector4[] _src = new Vector4[8];
        private static readonly int WakeTexId = Shader.PropertyToID("_WakeTex"), WakeRectId = Shader.PropertyToID("_WakeRect"),
            WakeOnId = Shader.PropertyToID("_WakeOn"), WakeYId = Shader.PropertyToID("_WakeY"), WakeTexelId = Shader.PropertyToID("_WakeTexel");

        private void Start()
        {
            Shader sh = Shader.Find("Hidden/FlyingGame/WaveSim");
            if (sh == null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)) { enabled = false; Shader.SetGlobalFloat(WakeOnId, 0f); return; }
            _sim = new Material(sh);
            var foamShader = Shader.Find("Hidden/FlyingGame/WakeFoam");
            if (foamShader != null && foamShader.isSupported) _foamSim = new Material(foamShader);
            Shader.SetGlobalFloat(WakeOnId, 0f);
        }

        private void Update()
        {
            var ac = Driver != null && Driver.Sim != null ? Driver.Sim.Aircraft : null;
            if (ac == null) return;
            // The water under / nearest the aircraft: the wake lives on that body. WorldTerrain.WakeBodyAt knows the
            // ISLAND's water lanes as well as the legacy lake table — this used to scan WorldTerrain.Lakes alone,
            // which is why the wake had never allocated on either lane the player can actually land on (Ty
            // 2026-11-11, "the water spray and wake don't seem to have been done"; WakeFieldCoverageProbe measures it).
            var p = ac.State.Position;
            WorldTerrain.Lake? body = WorldTerrain.WakeBodyAt(p.X, p.Y);
            bool here = body.HasValue && Same(body.Value, _body);
            bool forcing = ac.Config.Floats != null && (FloatHydro.Floats[0].Wet || FloatHydro.Floats[1].Wet);
            if (body.HasValue && !here && (forcing || _lake < 0)) { Allocate(body.Value); here = true; }
            if (_lake < 0) return;
            if (forcing && here) _sinceForcing = 0f; else _sinceForcing += Time.deltaTime;
            if (_sinceForcing > LastsSec) { Release(); return; }

            _acc += Mathf.Min(Time.deltaTime, 0.25f);
            int n = 0;
            if (here) n = Sources(ac);
            while (_acc >= StepSec)
            {
                _acc -= StepSec;
                Step(n);
            }
            Shader.SetGlobalTexture(WakeTexId, _curr);
            Shader.SetGlobalTexture("_WakeFoamTex", _foam != null ? (Texture)_foam : Texture2D.blackTexture);
        }

        /// <summary>The body a field is currently allocated over, and whether another reading is the same one. Compared
        /// by geometry rather than by index because a water lane is not in the lake table at all.</summary>
        private WorldTerrain.Lake _body;
        private static bool Same(in WorldTerrain.Lake a, in WorldTerrain.Lake b) =>
            Mathf.Approximately((float)a.Cx, (float)b.Cx) && Mathf.Approximately((float)a.Cy, (float)b.Cy)
            && Mathf.Approximately((float)a.Rx, (float)b.Rx) && Mathf.Approximately((float)a.Ry, (float)b.Ry);

        private void Allocate(WorldTerrain.Lake lake)
        {
            AllocateLake(lake);
            _body = lake;
            _lake = 0;   // a field is allocated; which body it is lives in _body
        }

        // Editor fixtures can use the current island's water bounds without changing legacy lake tables.
        private void AllocateLake(WorldTerrain.Lake l)
        {
            Release();
            // ~2 m cells on a lake, coarser on the 3.2 km harbor (≤ 1024 across).
            _cell = Mathf.Max(2f, (float)(2 * l.Rx) / 1024f);
            // Sim x (north) → Unity z, sim y (east) → Unity x: the grid's u runs along Unity x (sim y), v along Unity z (sim x).
            Vector3 c = CoordinateMap.ToUnity(new FlyingGame.Core.MathTypes.Vec3(l.Cx, l.Cy, -l.SurfaceM));
            // Texture u spans the lake's sim-y extent (2·Ry), v its sim-x extent (2·Rx).
            int tw = Mathf.CeilToInt((float)(2 * l.Ry) / _cell), th = Mathf.CeilToInt((float)(2 * l.Rx) / _cell);
            _width = tw * _cell; _depth = th * _cell;
            _min = new Vector2(c.x - _width / 2, c.z - _depth / 2);
            _prev = Make(tw, th); _curr = Make(tw, th); _next = Make(tw, th);
            if (_foamSim != null) { _foam = Make(tw, th); _foamNext = Make(tw, th); _foamSim.SetFloat("_Cell", _cell); }
            _acc = 0f;
            QuestRig.Note($"wake field {tw}x{th} allocated");
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
            foreach (var rt in new[] { _prev, _curr, _next, _foam, _foamNext }) if (rt != null) { rt.Release(); Destroy(rt); }
            _prev = _curr = _next = _foam = _foamNext = null;
            Shader.SetGlobalTexture("_WakeFoamTex", Texture2D.blackTexture);
            Shader.SetGlobalVectorArray("_FoamOrigins", new Vector4[2]);
            _lake = -1;
            Shader.SetGlobalFloat(WakeOnId, 0f);
        }

        /// <summary>Forcing points: each float's step and bow (or the hull's), sized by its beam, pushed by its speed and draft.</summary>
        private int Sources(FlyingGame.Sim.Aircraft ac)
        {
            var fl = ac.Config.Floats;
            int n = 0, floats = fl.SpreadM > 0.1 ? 2 : 1;
            float beam = (float)fl.BeamM;
            Vector3 forward = CoordinateMap.ToUnity(ac.State.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(1, 0, 0)));
            forward.y = 0; forward.Normalize();
            for (int i = 0; i < 2; i++) { _foamOrigins[i] = Vector4.zero; _foamMotion[i] = Vector4.zero; _foamWake[i] = Vector4.zero; _foamTrack[i] = Vector4.zero; }
            for (int i = 0; i < floats && n < 7; i++)
            {
                var f = FloatHydro.Floats[i];
                if (!f.Wet) continue;
                float speed = (float)f.SpeedMs;
                Vector3 origin = CoordinateMap.ToUnity(f.StepWorld);
                float load = Mathf.Clamp01((float)(f.PlaningLiftN + f.BuoyancyN) / 1500f);
                // WHITE WATER IS A FROUDE NUMBER, not a speed. Ty 2026-11-11: "There needs to be more white
                // spray/foam behind the floats at any speed above 10 mph ramping up with speed" — and on this
                // float's own beam his 10 mph IS Savitsky's planing onset, Cv = 2, to within 3 % (PlaningWake,
                // PlaningWakeTests). InverseLerp(4, 10, speed) was a metres-per-second curve with no beam in it,
                // so it said the same thing about a Cub float and a flying boat's hull. Displacement taxi still
                // keeps the solver's ripples and no cosmetic whitewater, because Cv < 2 gives zero.
                var wake = FlyingGame.Core.PlaningWake.Of(f.SpeedMs, fl.BeamM, f.StepDraftM, f.Lambda, f.TrimDeg, fl.DeadriseDeg);
                _foamOrigins[i] = new Vector4(origin.x, origin.z, beam, load * (float)wake.WhiteWater01);
                _foamMotion[i] = new Vector4(forward.x, forward.z, speed, floats == 1 ? 0 : i == 0 ? -1 : 1);
                // The wake surface behind the step: Savitsky and Morabito (2010) read as a free-surface contour.
                // x: how deep the cavity is at the step (= the keel's immersion); y: how far astern it closes;
                // z: where the rooster crest stands; w: how far the crest rises above the undisturbed water.
                _foamWake[i] = new Vector4((float)wake.CavityDepthM, (float)wake.CavityLengthM,
                                           (float)wake.CrestDistanceM, (float)wake.CrestHeightM);
                // The AERATED KEEL TRACK, which is the white water: where the keel drags air under, how fast the
                // bubble track widens, and how far astern the bubbles last before they have risen back out. Ty
                // 2026-11-11: "directly behind and in line with the floats keels and widens at a narrower rate
                // than the wake does ... the mixing of air and water together as the keels rub along the water
                // surface forming bubbles (white)". One per float, from that float's own draft — not a band
                // behind the aeroplane.
                _foamTrack[i] = new Vector4((float)wake.TrackHalfWidthM, (float)wake.TrackSpreadPerM,
                                            (float)wake.AeratedLengthM, 0f);
                bool planing = f.PlaningLiftN > f.BuoyancyN && speed > 4f;
                // Height pushed per step: a displacement hull heaps water at the bow; planing, the step throws the bigger
                // wave. Scaled by beam (a 7.6 m H-4 hull makes a far bigger wake than a 0.66 m Cub float).
                float k = Mathf.Clamp01(speed / 12f) * Mathf.Clamp(beam, 0.5f, 8f);
                float step = (planing ? 0.020f : 0.010f) * k, bow = (planing ? 0.004f : 0.012f) * k;
                n = Add(n, f.StepWorld, beam * 0.6f, step);
                n = Add(n, f.BowWorld, beam * 0.5f, bow);
            }
            Shader.SetGlobalVectorArray("_FoamOrigins", _foamOrigins);
            Shader.SetGlobalVectorArray("_FoamMotion", _foamMotion);
            Shader.SetGlobalVectorArray("_FoamWake", _foamWake);
            Shader.SetGlobalVectorArray("_FoamTrack", _foamTrack);
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
            if (_foamSim != null && _foam != null)
            {
                _foamSim.SetTexture("_Height", _curr);
                Graphics.Blit(_foam, _foamNext, _foamSim);
                t = _foam; _foam = _foamNext; _foamNext = t;
            }
        }

        private void OnDestroy()
        {
            Release();
            if (_sim != null) Destroy(_sim);
            if (_foamSim != null) Destroy(_foamSim);
        }
    }
}

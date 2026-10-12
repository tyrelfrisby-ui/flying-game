using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// AIRSHOW SMOKE (owner 2026-10-11, the Talk Flying outro): oil in the exhaust, a puff every 1/30 s from each exhaust.
    /// Each puff stays where the air put it — fixed in the air mass, drifting with the wind — and grows and fades over 10–30 s.
    /// Slow, it piles up into big round puffs; fast, the same oil is spread over more air (smaller, stretched along the path).
    /// More power, more exhaust, denser smoke; engine off, none. Gliders and the target drone have none.
    ///
    /// It is drawn FROM THE RECORDED PATH, not kept as live particles: give it the samples (newest first) and the time being
    /// shown, and it builds every puff still alive at that moment — so review, stepping, seeking and replay show exactly the
    /// smoke that was there (a torque roll falls back into its own cloud).
    /// </summary>
    public sealed class SmokeTrail
    {
        /// <summary>One recorded moment: when, where (the TRUE path — altitude lock / wrap shifts already removed), the pose,
        /// the body velocity (m/s) and the smoke flow (0 = none … 1 = full power).</summary>
        public struct Sample { public double T; public Vec3 Pos; public Quat Att; public Vec3 VelBody; public float Flow; }

        public const double LifeMaxS = 30.0;
        private const float Hz = 30f;
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly List<Vector3> _v = new(); private readonly List<Vector2> _uv = new(); private readonly List<Color> _c = new(); private readonly List<int> _tri = new();
        private readonly List<(float d2, Vector3 p, Vector3 axis, float w, float l, float a)> _puffs = new();
        private Vec3[] _exhaust = System.Array.Empty<Vec3>();

        public SmokeTrail(Transform parent, int layer)
        {
            _go = new GameObject("Smoke"); _go.transform.SetParent(parent, false); _go.layer = layer;
            _mesh = new Mesh { name = "Smoke" }; _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("FlyingGame/Smoke") ?? Shader.Find("FlyingGame/Spray")) { color = new Color(0.95f, 0.95f, 0.97f, 1f) };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        /// <summary>The exhausts (body axes, m from the CG) for a type — none for a glider or the drone. A tractor's stacks
        /// sit under the cowling, about a sixth of the fuselage aft of the spinner and below the thrust line; a pusher's just
        /// ahead of its propeller; a jet's at the tailpipe.</summary>
        public static Vec3[] ExhaustsFor(AircraftConfig cfg, string id, IReadOnlyList<Vector3> propHubsBody, float lengthM, float bodyRadiusM)
        {
            if (cfg?.Propulsion == null || (id ?? "").Contains("drone")) return System.Array.Empty<Vec3>();
            var list = new List<Vec3>();
            if (propHubsBody != null && propHubsBody.Count > 0)
            {
                foreach (var h in propHubsBody)
                {
                    bool pusher = h.x < -0.5f, wing = Mathf.Abs(h.y) > 1f;
                    float aft = Mathf.Clamp(0.16f * lengthM, 0.5f, wing ? 1.6f : 1.2f);
                    float x = pusher ? h.x + 0.35f : h.x - aft;
                    float z = h.z + (wing ? 0.35f : Mathf.Max(0.25f, 0.8f * bodyRadiusM));   // body z is DOWN
                    list.Add(new Vec3(x, h.y, z));
                }
            }
            else if (cfg.Engines != null && cfg.Engines.Count > 0)
                foreach (var m in cfg.Engines) list.Add(m.PosVec() + new Vec3(-1.5, 0, 0));   // podded jets: behind each nozzle plane
            else list.Add(new Vec3(-0.5 * lengthM, 0, 0));   // a jet in the fuselage: the tailpipe
            return list.ToArray();
        }

        public void SetExhausts(Vec3[] body) => _exhaust = body ?? System.Array.Empty<Vec3>();
        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }

        /// <summary>Build the smoke as it was at <paramref name="nowT"/>: <paramref name="sample"/>(k) is the k-th sample back
        /// (k = 0 the one shown), null past the end. <paramref name="shown"/> maps a true-path position to the drawing (the
        /// altitude lock's shift at the moment shown).</summary>
        public void Build(System.Func<int, Sample?> sample, double nowT, System.Func<Vec3, Vector3> shown, Camera cam)
        {
            if (_exhaust.Length == 0 || cam == null) { Hide(); return; }
            if (!_go.activeSelf) _go.SetActive(true);
            _puffs.Clear();
            Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;
            Vec3 wind = Atmosphere.SteadyWind;
            for (int k = 0; ; k++)
            {
                var sq = sample(k); if (sq == null) break;
                var s = sq.Value; double age = nowT - s.T;
                if (age > LifeMaxS) break;
                if (age < 0 || s.Flow <= 0.001f) continue;
                // Each puff's own life (10–30 s) and a little spread, from a hash of its time — the same puff every redraw.
                uint hsh = (uint)(s.T * 30.0 + 0.5) * 2654435761u;
                float rnd = (hsh >> 8 & 0xFFFF) / 65535f, rnd2 = (hsh >> 4 & 0xFF) / 255f;
                float life = 10f + 20f * rnd;
                if (age > life) continue;
                Vec3 velW = s.Att.Rotate(s.VelBody);
                float v = Mathf.Max(6f, (float)velW.Length);
                // Smoke per metre of path ∝ flow / speed: the puff's starting diameter (≈1.6 m at 60 kt, ≈1 m at 150 kt, full power).
                float d0 = Mathf.Clamp(9f * Mathf.Sqrt(s.Flow / v), 0.5f, 3.2f);
                float grow = d0 + 2.2f * Mathf.Sqrt((float)age) * (0.8f + 0.4f * rnd2);
                float stretch = Mathf.Max(grow, v / Hz * 1.3f + (grow - d0));   // one frame's travel, so a fast trail stays joined
                float fade = Mathf.Pow(1f - (float)age / life, 1.3f) * Mathf.Clamp01(d0 / grow * 1.6f);
                float a = 0.3f * Mathf.Lerp(0.45f, 1f, s.Flow) * fade;
                if (a < 0.004f) continue;
                Vec3 drift = wind * (Atmosphere.WindGradientFactor(s.Pos) * age) + new Vec3(0, 0, -System.Math.Min(2.0, 0.12 * age));   // + a little warm rise
                Vector3 axis = CoordinateMap.ToUnity(velW * (1.0 / v));
                foreach (var e in _exhaust)
                {
                    Vector3 p = shown(s.Pos + s.Att.Rotate(e) + drift);
                    float d2 = Vector3.Dot(p - camPos, camFwd);
                    if (d2 < 0.3f) continue;
                    // A puff the camera is inside (a chase camera flies through the up-line's smoke) would wash the whole
                    // picture white: thin it out within a few diameters of the eye.
                    float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f * grow, 4f * grow, (p - camPos).magnitude));
                    if (a * near < 0.004f) continue;
                    _puffs.Add((d2, p, axis, grow, stretch, a * near));
                }
            }
            // Back to front, then a camera-facing quad per puff, long along its (screen-projected) path.
            _puffs.Sort((x, y) => y.d2.CompareTo(x.d2));
            _v.Clear(); _uv.Clear(); _c.Clear(); _tri.Clear();
            foreach (var pf in _puffs)
            {
                Vector3 toCam = (camPos - pf.p).normalized;
                if (cam.orthographic) toCam = -camFwd;
                Vector3 along = Vector3.ProjectOnPlane(pf.axis, toCam);
                float alongLen = along.magnitude;
                if (alongLen < 1e-3f) { along = Vector3.Cross(toCam, Vector3.up); if (along.sqrMagnitude < 1e-4f) along = Vector3.right; along.Normalize(); alongLen = 0f; }
                else along /= alongLen;
                Vector3 side = Vector3.Cross(toCam, along).normalized;
                float hl = 0.5f * Mathf.Lerp(pf.w, pf.l, alongLen), hw = 0.5f * pf.w;
                int i0 = _v.Count;
                _v.Add(pf.p - along * hl - side * hw); _v.Add(pf.p + along * hl - side * hw); _v.Add(pf.p + along * hl + side * hw); _v.Add(pf.p - along * hl + side * hw);
                _uv.Add(new Vector2(0, 0)); _uv.Add(new Vector2(1, 0)); _uv.Add(new Vector2(1, 1)); _uv.Add(new Vector2(0, 1));
                var col = new Color(1, 1, 1, pf.a); _c.Add(col); _c.Add(col); _c.Add(col); _c.Add(col);
                _tri.Add(i0); _tri.Add(i0 + 1); _tri.Add(i0 + 2); _tri.Add(i0); _tri.Add(i0 + 2); _tri.Add(i0 + 3);
            }
            _mesh.Clear();
            _mesh.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_v); _mesh.SetUVs(0, _uv); _mesh.SetColors(_c); _mesh.SetTriangles(_tri, 0);
            _mesh.bounds = new Bounds(camPos, Vector3.one * 1e5f);
        }
    }
}

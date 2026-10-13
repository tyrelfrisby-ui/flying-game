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
    /// More power, more exhaust, denser smoke; engine off, none. The target drone has none.
    /// GLIDERS (owner 2026-10-12: "add wingtip smoke to the gliders … instead of puffs it is a steady stream"): a canister on
    /// each wingtip, a thin steady stream (<see cref="Stream"/>) that doesn't depend on power.
    ///
    /// It is drawn FROM THE RECORDED PATH, not kept as live particles: give it the samples (newest first) and the time being
    /// shown, and it builds every puff still alive at that moment — so review, stepping, seeking and replay show exactly the
    /// smoke that was there (a torque roll falls back into its own cloud).
    /// </summary>
    public sealed class SmokeTrail
    {
        /// <summary>One recorded moment: when, where (the TRUE path — altitude lock / wrap shifts already removed), the pose,
        /// the body velocity (m/s) and the smoke flow (0 = none … 1 = full power).</summary>
        public struct Sample { public double T; public Vec3 Pos; public Quat Att; public Vec3 VelBody; public Vec3 Rates; public float Flow; }

        /// <summary>The air round the airframe (Ty 2026-10-13: the smoke "needs to go around it"): each puff is carried round the
        /// fuselage and the surfaces as it leaves (<see cref="FlyingGame.Core.Aero.AirframeFlow"/>), then sits in the air with
        /// that displacement. null = the old straight-through smoke.</summary>
        public FlyingGame.Core.Aero.AirframeFlow Flow;
        private const double FlowDt = 0.004;
        private readonly Dictionary<(long, int), (Vec3 pos, List<Vec3> d)> _defl = new();
        /// <summary>A puff's push by the airframe, world axes, <paramref name="age"/> s after release.</summary>
        private Vec3 Pushed(in Sample s, int ei, Vec3 e, Vec3 jd, double age)
        {
            if (Flow == null) return Vec3.Zero;
            var key = ((long)System.Math.Round(s.T * 1e4), ei);
            if (!_defl.TryGetValue(key, out var c) || (c.pos - s.Pos).Length > 1e-6)
            {
                c = (s.Pos, Flow.Deflection(e, s.VelBody, s.Rates, jd, FlowDt, seed: (uint)key.Item1 * 2654435761u + (uint)ei * 40503u + 1u));
                _defl[key] = c;
            }
            int i = System.Math.Min(c.d.Count - 1, (int)(age / FlowDt));
            double t = i * FlowDt, w = s.Rates.Length;
            // The body axes at that moment: the release attitude turned on by ω·t (steady for the pass).
            Quat turn = w * t < 1e-9 ? Quat.Identity : new Quat(s.Rates.X / w * System.Math.Sin(0.5 * w * t), s.Rates.Y / w * System.Math.Sin(0.5 * w * t), s.Rates.Z / w * System.Math.Sin(0.5 * w * t), System.Math.Cos(0.5 * w * t));
            return Quat.Multiply(s.Att, turn).Rotate(c.d[i]);
        }
        private double _deflPruned;

        public const double LifeMaxS = 30.0;
        private const float Hz = 30f;
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly List<Vector3> _v = new(); private readonly List<Vector2> _uv = new(); private readonly List<Color> _c = new(); private readonly List<int> _tri = new();
        private readonly List<(float d2, Vector3 p, Vector3 axis, float w, float l, float a)> _puffs = new();
        private Vec3[] _exhaust = System.Array.Empty<Vec3>(), _jet = System.Array.Empty<Vec3>();

        public SmokeTrail(Transform parent, int layer)
        {
            _go = new GameObject("Smoke"); _go.transform.SetParent(parent, false); _go.layer = layer;
            _mesh = new Mesh { name = "Smoke" }; _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("FlyingGame/Smoke") ?? Shader.Find("FlyingGame/Spray")) { color = new Color(0.96f, 0.96f, 0.97f, 1f) };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        /// <summary>The exhausts (body axes, m from the CG) for a type — none for a glider or the drone. A tractor's stacks
        /// sit under the cowling, about a sixth of the fuselage aft of the spinner and below the thrust line; a pusher's just
        /// ahead of its propeller; a jet's at the tailpipe.</summary>
        public static Vec3[] ExhaustsFor(AircraftConfig cfg, string id, IReadOnlyList<Vector3> propHubsBody, float lengthM, float bodyRadiusM)
            => ExhaustsFor(cfg, id, propHubsBody, lengthM, bodyRadiusM, out _);

        /// <summary>The exhausts and the way each one blows (body axes, unit; zero = no jet — a wingtip canister). A tractor's
        /// stacks blow aft and DOWN (owner 2026-10-12, the Extra: 30° below the longitudinal axis); a type's config can place
        /// its exhaust exactly (<see cref="SmokeConfig"/>: so far aft of the nose, on the belly). Pushers and jets blow aft.</summary>
        public static Vec3[] ExhaustsFor(AircraftConfig cfg, string id, IReadOnlyList<Vector3> propHubsBody, float lengthM, float bodyRadiusM, out Vec3[] jet)
        {
            jet = System.Array.Empty<Vec3>();
            if (cfg == null || (id ?? "").Contains("drone")) return System.Array.Empty<Vec3>();
            if (cfg.Propulsion == null) { var tips = WingtipsFor(cfg); jet = new Vec3[tips.Length]; return tips; }
            var list = new List<Vec3>(); var dirs = new List<Vec3>();
            double down = (cfg.Smoke?.DownDeg ?? 30) * System.Math.PI / 180;
            var aftDown = new Vec3(-System.Math.Cos(down), 0, System.Math.Sin(down));   // body z is DOWN
            if (propHubsBody != null && propHubsBody.Count > 0)
            {
                foreach (var h in propHubsBody)
                {
                    bool pusher = h.x < -0.5f, wing = Mathf.Abs(h.y) > 1f;
                    float aft = Mathf.Clamp(0.16f * lengthM, 0.5f, wing ? 1.6f : 1.2f);
                    // A config'd exhaust: so far aft of the nose (the spinner tip, ~0.25 m ahead of the prop plane), on the belly.
                    if (!pusher && !wing && cfg.Smoke != null && cfg.Smoke.AftOfNoseM > 0) aft = (float)cfg.Smoke.AftOfNoseM - 0.25f;
                    float x = pusher ? h.x + 0.35f : h.x - aft;
                    float z = h.z + (wing ? 0.35f : !pusher && cfg.Smoke != null ? Mathf.Max(0.25f, bodyRadiusM) : Mathf.Max(0.25f, 0.8f * bodyRadiusM));   // body z is DOWN
                    list.Add(new Vec3(x, h.y, z)); dirs.Add(pusher ? new Vec3(-1, 0, 0) : aftDown);
                }
            }
            else if (cfg.Engines != null && cfg.Engines.Count > 0)
                foreach (var m in cfg.Engines) { list.Add(m.PosVec() + new Vec3(-1.5, 0, 0)); dirs.Add(new Vec3(-1, 0, 0)); }   // podded jets: behind each nozzle plane
            else { list.Add(new Vec3(-0.5 * lengthM, 0, 0)); dirs.Add(new Vec3(-1, 0, 0)); }   // a jet in the fuselage: the tailpipe
            jet = dirs.ToArray();
            return list.ToArray();
        }

        /// <summary>A glider's smoke: both wingtips, at the outermost wing strip's trailing edge.</summary>
        public static Vec3[] WingtipsFor(AircraftConfig cfg)
        {
            FlyingGame.Core.DataContracts.StripConfig tip = null;
            foreach (var sf in cfg.Surfaces) foreach (var st in sf.Strips) if (System.Math.Abs(st.Pos[2]) < 1.0 && (tip == null || System.Math.Abs(st.Pos[1]) > System.Math.Abs(tip.Pos[1]))) tip = st;
            if (tip == null || System.Math.Abs(tip.Pos[1]) < 1.0) return System.Array.Empty<Vec3>();
            double y = System.Math.Abs(tip.Pos[1]) * 1.03, x = tip.Pos[0] - 0.75 * tip.Chord;
            return new[] { new Vec3(x, y, tip.Pos[2]), new Vec3(x, -y, tip.Pos[2]) };
        }

        /// <summary>A steady stream (wingtip canisters) instead of exhaust puffs: thin, even, joined.</summary>
        public bool Stream;

        public void SetExhausts(Vec3[] body, Vec3[] jet = null) { _exhaust = body ?? System.Array.Empty<Vec3>(); _jet = jet ?? new Vec3[_exhaust.Length]; _defl.Clear(); }
        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }

        /// <summary>Build the smoke as it was at <paramref name="nowT"/>: <paramref name="sample"/>(k) is the k-th sample back
        /// (k = 0 the one shown), null past the end. <paramref name="shown"/> maps a true-path position to the drawing (the
        /// altitude lock's shift at the moment shown).</summary>
        public void Build(System.Func<int, Sample?> sample, double nowT, System.Func<Vec3, Vector3> shown, Camera cam)
        {
            if (_exhaust.Length == 0 || cam == null) { Hide(); return; }
            if (!_go.activeSelf) _go.SetActive(true);
            _puffs.Clear();
            if (System.Math.Abs(nowT - _deflPruned) > 5) { _deflPruned = nowT; var old = new List<(long, int)>(); foreach (var kv in _defl) if (System.Math.Abs(nowT - kv.Key.Item1 * 1e-4) > LifeMaxS + 5) old.Add(kv.Key); foreach (var o in old) _defl.Remove(o); }
            Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;
            Vec3 wind = Atmosphere.SteadyWind;
            double newerT = double.NaN;
            for (int k = 0; ; k++)
            {
                var sq = sample(k); if (sq == null) break;
                var s = sq.Value; double age = nowT - s.T;
                // This puff's share of the oil: the time to the next newer sample (the history records 30–600 a second).
                double span = double.IsNaN(newerT) ? 1.0 / Hz : System.Math.Clamp(newerT - s.T, 1e-4, 1.0 / Hz); newerT = s.T;
                float share = (float)(span * Hz);
                if (age > LifeMaxS) break;
                if (age < 0 || s.Flow <= 0.001f) continue;
                // Each puff's own life (10–30 s) and a little spread, from a hash of its time — the same puff every redraw.
                uint hsh = (uint)(s.T * 600.0 + 0.5) * 2654435761u;   // (per sample at up to 600 a second)
                float rnd = (hsh >> 8 & 0xFFFF) / 65535f, rnd2 = (hsh >> 4 & 0xFF) / 255f;
                float life = Stream ? 14f + 4f * rnd : 10f + 20f * rnd;
                if (age > life) continue;
                Vec3 velW = s.Att.Rotate(s.VelBody);
                float v = Mathf.Max(6f, (float)velW.Length);
                // Smoke per metre of path ∝ flow / speed: the puff's starting diameter (≈1.6 m at 60 kt, ≈1 m at 150 kt, full power).
                float d0 = Stream ? 0.5f : Mathf.Clamp(9f * Mathf.Sqrt(s.Flow / v), 0.5f, 3.2f);
                float grow = Stream ? d0 + 0.9f * Mathf.Sqrt((float)age) : d0 + 2.2f * Mathf.Sqrt((float)age) * (0.8f + 0.4f * rnd2);
                // Leaving the stack it's a narrow plume that spreads with the distance the air has carried it (≈ 0.2 per metre, a
                // turbulent jet), not yet the trail's full puff — so next to the airframe it's small, as it is round a real one.
                if (!Stream) grow = Mathf.Min(grow, 0.3f + 0.2f * v * (float)age);
                float stretch = Mathf.Max(grow, v * (float)span * (Stream ? 1.8f : 1.3f) + (grow - d0));   // one frame's travel, so a fast trail stays joined
                float fade = Mathf.Pow(1f - (float)age / life, 1.3f) * Mathf.Clamp01(d0 / grow * 1.6f);
                float a = 0.65f * Mathf.Lerp(0.45f, 1f, s.Flow) * fade * share;   // denser (owner 2026-10-12: the smoke must show in the transparent captures)
                if (a < 0.004f) continue;
                Vec3 drift = wind * (Atmosphere.WindGradientFactor(s.Pos) * age) + new Vec3(0, 0, -System.Math.Min(2.0, 0.12 * age));   // + a little warm rise
                Vector3 axis = CoordinateMap.ToUnity(velW * (1.0 / v));
                // The exhaust jet carries the smoke out along its line for the first moments (≈25 m/s, decaying in ~0.12 s:
                // about 3 m), then it sits in the air.
                double jetM = FlyingGame.Core.Aero.AirframeFlow.JetSpeed * FlyingGame.Core.Aero.AirframeFlow.JetTau * (1 - System.Math.Exp(-age / FlyingGame.Core.Aero.AirframeFlow.JetTau));
                // A puff covers the path from its release back to one frame earlier — it is drawn BEHIND the release point,
                // never ahead of the airplane (owner 2026-10-12: "the smoke appears to come from in front of the engine").
                Vec3 back = velW * (-0.5 * (stretch - grow) / v);
                for (int ei = 0; ei < _exhaust.Length; ei++)
                {
                    Vec3 e = _exhaust[ei], jd = ei < _jet.Length ? _jet[ei] : Vec3.Zero;
                    Vector3 p = shown(s.Pos + s.Att.Rotate(e + jd * jetM) + back + drift + Pushed(s, ei, e, jd, age));
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
                // Lit from above, shaded underneath (owner 2026-10-12: the smoke must read over a light page as well as a dark one):
                // each corner's brightness from its height in the puff — white on top, a soft grey below.
                float span = Mathf.Max(1e-3f, hl + hw);
                for (int q = i0; q < i0 + 4; q++)
                {
                    float up = Mathf.Clamp((_v[q].y - pf.p.y) / span, -1f, 1f);
                    float b = Mathf.Lerp(0.42f, 1f, 0.5f + 0.5f * up);
                    _c.Add(new Color(b, b, b * 1.02f, pf.a));
                }
                _tri.Add(i0); _tri.Add(i0 + 1); _tri.Add(i0 + 2); _tri.Add(i0); _tri.Add(i0 + 2); _tri.Add(i0 + 3);
            }
            _mesh.Clear();
            _mesh.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_v); _mesh.SetUVs(0, _uv); _mesh.SetColors(_c); _mesh.SetTriangles(_tri, 0);
            _mesh.bounds = new Bounds(camPos, Vector3.one * 1e5f);
        }
    }
}

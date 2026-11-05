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
        public struct Sample { public double T; public Vec3 Pos; public Quat Att; public Vec3 VelBody; public Vec3 Rates; public float Flow; public double JetU0, AirDensity;
            /// <summary>The crank's position (revolutions, integrated from the live rpm) and the rpm: they time the exhaust pulses
            /// (<see cref="FlyingGame.Core.Aero.ExhaustPulse"/>). Rpm 0 (the widget's history) = no pulse tubes, puffs from the stack.</summary>
            public double Crank; public float Rpm; }

        /// <summary>The air round the airframe (Ty 2026-10-13: the smoke "needs to go around it"): each puff is carried round the
        /// fuselage and the surfaces as it leaves (<see cref="FlyingGame.Core.Aero.AirframeFlow"/>), then sits in the air with
        /// that displacement. null = the old straight-through smoke.</summary>
        public FlyingGame.Core.Aero.AirframeFlow Flow;
        /// <summary>The exhaust pipe's diameter (m): the jet's starting width (<see cref="FlyingGame.Core.Aero.ExhaustJet"/>).</summary>
        public double PipeDiameterM = 0.1016;
        private const double FlowDt = 0.004;
        private readonly Dictionary<(long, int), (Vec3 pos, List<Vec3> d)> _defl = new();
        /// <summary>A puff's push by the airframe, world axes, <paramref name="age"/> s after release.</summary>
        private Vec3 Pushed(in Sample s, int ei, Vec3 e, Vec3 jd, double age)
        {
            if (Flow == null) return Vec3.Zero;
            var key = ((long)System.Math.Round(s.T * 1e4), ei);
            if (!_defl.TryGetValue(key, out var c) || (c.pos - s.Pos).Length > 1e-6)
            {
                c = (s.Pos, Flow.Deflection(e, s.VelBody, s.Rates, jd, s.JetU0, PipeDiameterM, s.AirDensity > 0 ? s.AirDensity : 1.225, FlowDt, seed: (uint)key.Item1 * 2654435761u + (uint)ei * 40503u + 1u));
                _defl[key] = c;
            }
            int i = System.Math.Min(c.d.Count - 1, (int)(age / FlowDt));
            double t = i * FlowDt, w = s.Rates.Length;
            // The body axes at that moment: the release attitude turned on by ω·t (steady for the pass).
            Quat turn = w * t < 1e-9 ? Quat.Identity : new Quat(s.Rates.X / w * System.Math.Sin(0.5 * w * t), s.Rates.Y / w * System.Math.Sin(0.5 * w * t), s.Rates.Z / w * System.Math.Sin(0.5 * w * t), System.Math.Cos(0.5 * w * t));
            return Quat.Multiply(s.Att, turn).Rotate(c.d[i]);
        }
        private double _deflPruned;
        private int _nanLogged;
        private long _lastSlot;

        public const double LifeMaxS = 30.0;
        private const float Hz = 30f;

        // VOLUME AND DENSITY (Ty 2026-11-04: the trail must be "more voluminous" and "clearly LESS see-through": wider,
        // puffier, billowing as it ages, a dense core with only its fringe feathered). Was 9 / 0.5–3.2 m / 2.2 / 0.8–1.2 /
        // 1.6 / 0.65, with each 1/30 s slot drawn for the whole 30 s.
        /// <summary>Starting diameter (m) = this × √(flow / speed), so 1.5 m at 120 kt full power.</summary>
        private const float PuffD0 = 12f, PuffD0Min = 0.7f, PuffD0Max = 4.2f;
        /// <summary>Growth with age (m per √s) and how much one puff differs from its neighbours (×0.7…1.3): the spread
        /// is what makes the column lumpy rather than a tube.</summary>
        private const float PuffGrowth = 3.4f, PuffGrowthSpread = 0.6f;
        /// <summary>The oil thins as the puff grows, ∝ d0 / diameter; this is how much of that it keeps (was 1.6). The
        /// bigger puffs keep their density instead of turning to gauze.</summary>
        private const float PuffThinning = 2.0f;
        /// <summary>A puff's coverage at the core, full power, fresh (1.5 × the old 0.65, clamped to opaque).</summary>
        private const float PuffAlpha = 0.98f;
        /// <summary>A puff wanders off the path by up to this × its diameter (turbulent diffusion): billows, at no cost.</summary>
        private const float PuffWander = 0.16f;
        /// <summary>FEWER PUFFS AS THEY GROW (Quest at 90 Hz, the iPhone): neighbours of a grown puff overlap many times
        /// over, so past these ages every other puff fades out and its neighbour takes over its coverage and its length
        /// of path — 30 → 15 a second over 1.5–3 s. Crossfaded, so nothing pops: about half the puffs. (A second stage,
        /// 15 → 7.5 past 6 s, was tried and rendered a loop as a string of beads: one puff standing for four slots shows
        /// its own size and life too plainly.)</summary>
        private const float ThinFrom = 1.5f, ThinTo = 3f;
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly List<Vector3> _v = new(); private readonly List<Vector2> _uv = new(); private readonly List<Color> _c = new(); private readonly List<int> _tri = new();
        private readonly List<(float d2, Vector3 p, Vector3 axis, float w, float l, float a)> _puffs = new();
        private Vec3[] _exhaust = System.Array.Empty<Vec3>(), _jet = System.Array.Empty<Vec3>();

        /// <param name="alphaCapture">The widget's straight-alpha frame (Syphon / NDI / ProRes 4444) needs the shader's two
        /// extra passes that fill empty sky and write coverage into alpha. The game draws to an opaque target, where
        /// those passes change nothing on screen and cost two thirds of the smoke's fill — so off unless asked for.</param>
        public SmokeTrail(Transform parent, int layer, bool alphaCapture = false)
        {
            _go = new GameObject("Smoke"); _go.transform.SetParent(parent, false); _go.layer = layer;
            _mesh = new Mesh { name = "Smoke" }; _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("FlyingGame/Smoke") ?? Shader.Find("FlyingGame/Spray")) { color = new Color(0.96f, 0.96f, 0.97f, 1f) };
            r.sharedMaterial.SetFloat("_CaptureAlpha", alphaCapture ? 1f : 0f);
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
            // Measured on the placed model, relative to its CG. In particular, do not move this point
            // to a procedural hub or back onto the centreline of an asymmetric/paired exhaust.
            if (cfg.Smoke != null && cfg.Smoke.TryOutlet(out Vec3 measured))
            {
                jet = new[] { aftDown };
                return new[] { measured };
            }
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

        /// <summary>Cylinders feeding the smoke pipe (<see cref="SmokeConfig.Cylinders"/>): cylinders / 2 pulses a revolution.</summary>
        public int Cylinders = 6;

        // EXHAUST PULSE TUBES (Ty 2026-11-04: "each exhaust pulse leaves the stack as a TUBE … fat/dense mid-pulse and tapers at
        // both ends … then it decays into the voluminous, near-opaque trail over about 1 s"). Right behind the aeroplane the
        // smoke is drawn per valve event, not per frame: a 540 at 2,700 rpm lets out 135 tubes a second. Each tube is the gas of
        // one event, laid down by three parcels — the 2 %, 50 % and 98 % points of its mass (ExhaustPulse.MassQuantile) — each
        // leaving the pipe at its own instant and its own exit speed off the pulse's bell. Gas faster than the air going its
        // way carries itself further (ExhaustPulse.Penetration, on top of the mean jet the airframe flow already carries), so a
        // strong pulse stretches its tube, and gas no faster than the stream is simply laid along the path flown. Between its
        // 2 % and 98 % points an event lets gas out for 119° of crank — a six's events are 120° apart, and the gas between them
        // never stops (the trough is 0.28 of the mean), so its tubes join into a rippled column; a four's are 180° apart and
        // its trough is 0.007, so at low speed its tubes part.
        // The trail's puffs take over from TubeHandFrom to TubeAgeS s (crossfaded), so the tubes cost nothing past 1 s.
        private const float TubeAgeS = 1.0f, TubeHandFrom = 0.25f;
        /// <summary>The tube's turbulent widening behind the airframe (m per √s), on top of the pipe and its jet's spread.</summary>
        private const float TubeDiffusion = 0.6f;
        private static readonly double Q10 = FlyingGame.Core.Aero.ExhaustPulse.MassQuantile(0.02),
                                       Q50 = FlyingGame.Core.Aero.ExhaustPulse.MassQuantile(0.5),
                                       Q90 = FlyingGame.Core.Aero.ExhaustPulse.MassQuantile(0.98);
        /// <summary>Tubes drawn in the last Build (the cost, for the review plates).</summary>
        public int TubeCount { get; private set; }

        public void SetExhausts(Vec3[] body, Vec3[] jet = null) { _exhaust = body ?? System.Array.Empty<Vec3>(); _jet = jet ?? new Vec3[_exhaust.Length]; _defl.Clear(); }
        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }

        /// <summary>The pulse tubes of the last <see cref="TubeAgeS"/> s: one per exhaust-valve event between each pair of
        /// samples (the crank's travel between them says which events opened, and when).</summary>
        private void BuildTubes(System.Func<int, Sample?> sample, double nowT, System.Func<Vec3, Vector3> shown, Vector3 camPos, Vector3 camFwd)
        {
            double spacing = 1.0 / FlyingGame.Core.Aero.ExhaustPulse.EventsPerRev(Cylinders);
            Sample? newer = sample(0);
            for (int k = 1; newer != null; k++)
            {
                Sample? olderQ = sample(k); if (olderQ == null) break;
                Sample sn = newer.Value, so = olderQ.Value; newer = olderQ;
                if (nowT - sn.T > TubeAgeS) break;
                double dc = sn.Crank - so.Crank, dt = sn.T - so.T;
                if (dc <= 0 || dt <= 1e-6 || (so.Flow <= 0.001f && sn.Flow <= 0.001f)) continue;
                double revPerS = dc / dt, window = FlyingGame.Core.Aero.ExhaustPulse.WindowRev / revPerS;   // the valve-open time, s
                Vec3 velW = sn.Att.Rotate(sn.VelBody);
                float v = Mathf.Max(6f, (float)velW.Length);
                float pathPerPulse = (float)(v * spacing / revPerS);                                      // the air between tubes, m
                for (long j = (long)System.Math.Ceiling(so.Crank / spacing); j * spacing < sn.Crank; j++)
                {
                    double evo = j * spacing, f = (evo - so.Crank) / dc, tEvo = so.T + f * dt;
                    double t10 = tEvo + Q10 * window;
                    if (t10 > nowT) continue;                                                              // not out yet
                    double t50 = System.Math.Min(nowT, tEvo + Q50 * window), t90 = System.Math.Min(nowT, tEvo + Q90 * window);
                    float flow = Mathf.Lerp(so.Flow, sn.Flow, (float)f);
                    if (flow <= 0.001f) continue;
                    double meanU = so.JetU0 + f * (sn.JetU0 - so.JetU0);
                    float age50 = (float)(nowT - t50);
                    float hand = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TubeHandFrom, TubeAgeS, age50));
                    if (hand <= 0.001f) continue;
                    for (int ei = 0; ei < _exhaust.Length; ei++)
                    {
                        Vec3 e = _exhaust[ei], jd = ei < _jet.Length ? _jet[ei] : Vec3.Zero;
                        double jl = jd.Length; if (jl < 1e-6) continue;                                   // a canister has no pulses
                        Vec3 dir = jd * (1.0 / jl);
                        double fs = Vec3.Dot(-1.0 * sn.VelBody, dir);                                     // the stream's speed along the pipe
                        Vector3 p10 = Parcel(so, sn, ei, e, dir, t10, evo + Q10 * FlyingGame.Core.Aero.ExhaustPulse.WindowRev, meanU, fs, nowT, shown, out _);
                        Vector3 p50 = Parcel(so, sn, ei, e, dir, t50, evo + Q50 * FlyingGame.Core.Aero.ExhaustPulse.WindowRev, meanU, fs, nowT, shown, out double pen50);
                        Vector3 p90 = Parcel(so, sn, ei, e, dir, t90, evo + Q90 * FlyingGame.Core.Aero.ExhaustPulse.WindowRev, meanU, fs, nowT, shown, out _);
                        if (!(float.IsFinite(p10.x) && float.IsFinite(p50.y) && float.IsFinite(p90.z) && float.IsFinite(p10.y) && float.IsFinite(p90.x))) continue;
                        Vector3 span = p90 - p10, mid = 0.5f * (p10 + p90);
                        float len = span.magnitude;
                        Vector3 axis = len > 1e-4f ? span / len : CoordinateMap.ToUnity(velW * (1.0 / v));
                        float width = (float)PipeDiameterM + 0.22f * (float)pen50 + TubeDiffusion * Mathf.Sqrt(age50);
                        // ... widening toward the trail's own puff at that age (the same law as below), so the handover is one
                        // column getting fatter, not a thin one fading inside a fat one.
                        float d0 = Mathf.Clamp(PuffD0 * Mathf.Sqrt(flow / v), PuffD0Min, PuffD0Max);
                        float trailW = Mathf.Min(d0 + PuffGrowth * Mathf.Sqrt(age50), 0.3f + 0.2f * v * age50);
                        width = Mathf.Lerp(width, Mathf.Max(width, trailW), Mathf.Clamp01(age50 / TubeAgeS));
                        // The 2 % and 98 % parcels sit where the drawn puff is down to a quarter (r 0.82 in FlyingGame/Smoke),
                        // so a six's tubes overlap to a ~0.4 neck, near the pulse train's own 0.28 trough.
                        float length = Mathf.Max(width, len / 0.82f + 0.5f * width);
                        // Opacity per tube, so the column of overlapping tubes is as dense as the trail it becomes.
                        float target = PuffAlpha * Mathf.Lerp(0.45f, 1f, flow);
                        float overlap = Mathf.Max(1f, length / Mathf.Max(0.05f, pathPerPulse));
                        float a = (1f - Mathf.Pow(1f - Mathf.Min(target, 0.999f), 1f / overlap)) * hand;
                        float d2 = Vector3.Dot(mid - camPos, camFwd);
                        if (d2 < 0.3f) continue;
                        float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f * width, 4f * width, (mid - camPos).magnitude));
                        if (a * near < 0.004f) continue;
                        _puffs.Add((d2, mid, axis, width, length, a * near));
                        TubeCount++;
                    }
                }
            }
        }

        /// <summary>One parcel of a pulse's gas, <paramref name="nowT"/> − <paramref name="tq"/> s after it left the pipe at the
        /// pulse's speed for that crank angle: the pipe where it was then, the airframe's flow and the mean jet (Pushed), and how
        /// much further (or less far) than the mean jet its own speed carried it.</summary>
        private Vector3 Parcel(in Sample so, in Sample sn, int ei, Vec3 e, Vec3 dir, double tq, double crank, double meanU, double fs,
                               double nowT, System.Func<Vec3, Vector3> shown, out double penetration)
        {
            double g = (tq - so.T) / (sn.T - so.T), age = System.Math.Max(0, nowT - tq);
            Vec3 pos = so.Pos + (sn.Pos - so.Pos) * g;
            Quat att = g < 0.5 ? so.Att : sn.Att;
            double u = FlyingGame.Core.Aero.ExhaustPulse.Velocity(crank, meanU, Cylinders);
            penetration = FlyingGame.Core.Aero.ExhaustPulse.Penetration(u - fs, age, PipeDiameterM);
            // The mean jet is already in the airframe flow's push; without that model the parcel carries all of its own.
            double extra = Flow != null ? penetration - FlyingGame.Core.Aero.ExhaustPulse.Penetration(meanU - fs, age, PipeDiameterM) : penetration;
            Vec3 drift = Atmosphere.SteadyWindAt(pos) * age + new Vec3(0, 0, -System.Math.Min(2.0, 0.12 * age));
            return shown(pos + att.Rotate(e + dir * extra) + drift + Pushed(so, ei, e, dir, age));
        }

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
            bool tubes = !Stream && sample(0) is Sample s0 && s0.Rpm > 1f;
            TubeCount = 0;
            if (tubes) BuildTubes(sample, nowT, shown, camPos, camFwd);
            double newerT = double.NaN;
            _lastSlot = long.MinValue;
            for (int k = 0; ; k++)
            {
                var sq = sample(k); if (sq == null) break;
                var s = sq.Value; double age = nowT - s.T;
                // This puff's share of the oil: the time to the next newer sample (the history records 30–600 a second). Past
                // 0.2 s old, one puff per 1/30 s carries its whole slot (2026-10-13: a 600 Hz recording drew ~20× the puffs at
                // 1/20 the opacity, and 8-bit blending of thousands of near-clear layers ground the airframe behind them BLACK).
                double span = double.IsNaN(newerT) ? 1.0 / Hz : System.Math.Clamp(newerT - s.T, 1e-4, 1.0 / Hz); newerT = s.T;
                float share = (float)(span * Hz);
                // How many slots' worth this puff stands for once its neighbours have been thinned out (0 = gone … 2).
                float carry = 1f;
                if (age > 0.2)
                {
                    long slot = (long)System.Math.Floor(s.T * Hz);
                    if (slot == _lastSlot) continue;
                    _lastSlot = slot; share = 1f; span = 1.0 / Hz;   // (and its length: a slot's worth of path, so the column stays joined)
                    if (!Stream)
                    {
                        float t = Mathf.InverseLerp(ThinFrom, ThinTo, (float)age);
                        carry = (slot & 1) != 0 ? 1f - t : 1f + t;           // odd slots fade out, even ones take over their share
                        if (carry <= 0.001f) continue;
                        span *= Mathf.Max(1f, carry);                        // and cover that much path, back from the release
                    }
                }
                if (age > LifeMaxS) break;
                if (age < 0 || s.Flow <= 0.001f) continue;
                // Each puff's own life (10–30 s) and a little spread, from a hash of its time — the same puff every redraw.
                uint hsh = (uint)(s.T * 600.0 + 0.5) * 2654435761u;   // (per sample at up to 600 a second)
                float rnd = (hsh >> 8 & 0xFFFF) / 65535f, rnd2 = (hsh >> 4 & 0xFF) / 255f;
                float life = Stream ? 14f + 4f * rnd : 10f + 20f * rnd;
                if (age > life) continue;
                Vec3 velW = s.Att.Rotate(s.VelBody);
                float v = Mathf.Max(6f, (float)velW.Length);
                // Smoke per metre of path ∝ flow / speed: the puff's starting diameter (≈2.2 m at 60 kt, ≈1.4 m at 150 kt, full power; was 1.6 and 1).
                float d0 = Stream ? 0.5f : Mathf.Clamp(PuffD0 * Mathf.Sqrt(s.Flow / v), PuffD0Min, PuffD0Max);
                float grow = Stream ? d0 + 0.9f * Mathf.Sqrt((float)age) : d0 + PuffGrowth * Mathf.Sqrt((float)age) * (1f - 0.5f * PuffGrowthSpread + PuffGrowthSpread * rnd2);
                // Leaving the stack it's a narrow plume that spreads with the distance the air has carried it (≈ 0.2 per metre, a
                // turbulent jet), not yet the trail's full puff — so next to the airframe it's small, as it is round a real one.
                if (!Stream) grow = Mathf.Min(grow, 0.3f + 0.2f * v * (float)age);
                float stretch = Mathf.Max(grow, v * (float)span * (Stream ? 1.8f : 1.3f) + (grow - d0));   // one frame's travel, so a fast trail stays joined
                float fade = Mathf.Pow(1f - (float)age / life, 1.3f) * Mathf.Clamp01(d0 / grow * (Stream ? 1.6f : PuffThinning));
                float a = (Stream ? 0.65f : PuffAlpha) * Mathf.Lerp(0.45f, 1f, s.Flow) * fade * share;   // denser (owner 2026-10-12: the smoke must show in the transparent captures)
                // A thinned puff: the one fading out loses its share, the one taking over composites the slots it now stands
                // for (1 − (1 − a)^n), so the column's coverage holds through the crossfade.
                a = carry < 1f ? a * carry : 1f - Mathf.Pow(1f - Mathf.Min(a, 0.999f), carry);
                a = Mathf.Min(a, 1f);
                // The pulse tubes own the first second; the trail's puff fades in over it as they fade out.
                if (tubes) a *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TubeHandFrom, TubeAgeS, (float)age));
                if (a < 0.004f) continue;
                Vec3 drift = Atmosphere.SteadyWindAt(s.Pos) * age + new Vec3(0, 0, -System.Math.Min(2.0, 0.12 * age));   // + a little warm rise
                Vector3 axis = CoordinateMap.ToUnity(velW * (1.0 / v));
                // Billowing: each puff wanders off the path, across it, by a share of its own size (fixed per puff, from its hash).
                Vector3 wander = Vector3.zero;
                if (!Stream)
                {
                    uint h2 = hsh * 2246822519u ^ (hsh >> 13);
                    float ang = (h2 & 0xFFFF) / 65535f * 2f * Mathf.PI, amt = PuffWander * grow * ((h2 >> 16 & 0xFF) / 255f);
                    Vector3 u1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, u2 = Vector3.Cross(axis, u1);
                    wander = (u1 * Mathf.Cos(ang) + u2 * Mathf.Sin(ang)) * amt;
                }
                // The exhaust jet carries the smoke out along its line for the first moments (≈25 m/s, decaying in ~0.12 s:
                // about 3 m), then it sits in the air.
                double jetM = Flow != null ? 0.0 : 25.0 * 0.02 * (1 - System.Math.Exp(-age / 0.02));   // (with the airflow model, the jet is in Pushed: ExhaustJet)
                // A puff covers the path from its release back to one frame earlier — it is drawn BEHIND the release point,
                // never ahead of the airplane (owner 2026-10-12: "the smoke appears to come from in front of the engine").
                Vec3 back = velW * (-0.5 * (stretch - grow) / v);
                for (int ei = 0; ei < _exhaust.Length; ei++)
                {
                    Vec3 e = _exhaust[ei], jd = ei < _jet.Length ? _jet[ei] : Vec3.Zero;
                    Vector3 p = shown(s.Pos + s.Att.Rotate(e + jd * jetM) + back + drift + Pushed(s, ei, e, jd, age)) + wander;
                    // A non-finite puff (a NaN anywhere in its push) would poison every blend it touches: its colour came out
                    // NaN and the airframe under the smoke column rendered (0,0,0) (Ty 2026-10-13: "rendering as black").
                    if (!(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) && float.IsFinite(a) && float.IsFinite(grow) && float.IsFinite(stretch)) || a < 0 || grow <= 0)
                    { if (_nanLogged++ < 5) Debug.LogWarning($"[Smoke] non-finite puff dropped: age {age:0.000} p {p} a {a} grow {grow} stretch {stretch} jet {s.JetU0:0.0} ρ {s.AirDensity:0.000}"); continue; }
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

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
        /// <summary>Each puff's push by the airframe, step by step (up to 4 s of it), and how many steps it had: once a puff
        /// is past its last step only that step is kept (a 90 s trail would otherwise hold ~2,700 full paths).</summary>
        private readonly Dictionary<(long, int), (Vec3 pos, List<Vec3> d, int n)> _defl = new();
        /// <summary>A puff's push by the airframe, world axes, <paramref name="age"/> s after release.</summary>
        private Vec3 Pushed(in Sample s, int ei, Vec3 e, Vec3 jd, double age)
        {
            if (Flow == null) return Vec3.Zero;
            var key = ((long)System.Math.Round(s.T * 1e4), ei);
            if (!_defl.TryGetValue(key, out var c) || (c.pos - s.Pos).Length > 1e-6)
            {
                var d = Flow.Deflection(e, s.VelBody, s.Rates, jd, s.JetU0, PipeDiameterM, s.AirDensity > 0 ? s.AirDensity : 1.225, FlowDt, seed: (uint)key.Item1 * 2654435761u + (uint)ei * 40503u + 1u);
                c = (s.Pos, d, d.Count);
                _defl[key] = c;
            }
            int i = System.Math.Min(c.n - 1, (int)(age / FlowDt));
            if (i < c.n - 1 && c.d.Count == 1)
            {
                // Collapsed to its last step, and now shown YOUNGER (a replay seeked or rewound back over it): the young
                // path again, or the puff would sit where its pass ended — a puff stuck in the wrong place.
                var d = Flow.Deflection(e, s.VelBody, s.Rates, jd, s.JetU0, PipeDiameterM, s.AirDensity > 0 ? s.AirDensity : 1.225, FlowDt, seed: (uint)key.Item1 * 2654435761u + (uint)ei * 40503u + 1u);
                c = (s.Pos, d, d.Count); _defl[key] = c;
                i = System.Math.Min(c.n - 1, (int)(age / FlowDt));
            }
            if (i == c.n - 1 && c.d.Count > 1) { c = (c.pos, new List<Vec3>(1) { c.d[^1] }, c.n); _defl[key] = c; }
            double t = i * FlowDt, w = s.Rates.Length;
            // The body axes at that moment: the release attitude turned on by ω·t (steady for the pass).
            Quat turn = w * t < 1e-9 ? Quat.Identity : new Quat(s.Rates.X / w * System.Math.Sin(0.5 * w * t), s.Rates.Y / w * System.Math.Sin(0.5 * w * t), s.Rates.Z / w * System.Math.Sin(0.5 * w * t), System.Math.Cos(0.5 * w * t));
            return Quat.Multiply(s.Att, turn).Rotate(c.d[c.d.Count == 1 ? 0 : i]);
        }
        private double _deflPruned;
        private int _nanLogged;
        private long _lastSlot;

        /// <summary>The oldest smoke drawn (s), and so the ring <see cref="SmokeController"/> keeps (Ty 2026-11-04 18:27: "the
        /// smoke seems to disappear way too quickly … should stick around longer": an airshow trail hangs for a minute or
        /// more in still air). Was 30.</summary>
        public const double LifeMaxS = 90.0;
        private const float Hz = 30f;
        /// <summary>A puff's own life (s): this plus up to <see cref="PuffLifeSpread"/>, from its hash, so the end of a trail
        /// breaks up into patches instead of ending on a line. Was 10–30 s, which put half the trail gone by 20 s.</summary>
        private const float PuffLife = 55f, PuffLifeSpread = 30f;
        /// <summary>The last share of its life over which a puff fades out (before that it only thins as it spreads).</summary>
        private const float PuffFadeShare = 0.35f;

        // VOLUME AND DENSITY (Ty 2026-11-04: the trail must be "more voluminous" and "clearly LESS see-through": wider,
        // puffier, billowing as it ages, a dense core with only its fringe feathered; then 18:27: "thicker, more white, less
        // transparent"). Was 9 / 0.5–3.2 m / 2.2 / 0.8–1.2 / 1.6 / 0.65, with each 1/30 s slot drawn for the whole 30 s.
        /// <summary>Starting diameter (m) = this × √(flow / speed), so 1.5 m at 120 kt full power.</summary>
        private const float PuffD0 = 12f, PuffD0Min = 0.7f, PuffD0Max = 4.2f;
        /// <summary>Growth with age (m per √s) and how much one puff differs from its neighbours (×0.7…1.3): the spread
        /// is what makes the column lumpy rather than a tube. 2.6 m/√s: ~7 m across at 5 s, ~13 m at 20 s, ~19 m at 45 s
        /// (899ada84's 3.4 spread the same smoke over a 60 % wider column, which is part of why it read thinner).</summary>
        private const float PuffGrowth = 2.6f, PuffGrowthSpread = 0.6f;
        /// <summary>THE COLUMN'S OPTICAL DEPTH, straight across its middle: <c>τ = TauCore × (0.45…1 by flow) × d0 / d</c>.
        /// The oil in a metre of trail is fixed when it leaves the pipe and the column only spreads it over its own width,
        /// so the depth across falls as 1/d — slowly, which is why a real trail stays white long after it has stopped
        /// growing. 17 at the stack: ~0.97 coverage at 5 s, ~0.85 at 20 s, ~0.7 at 45 s (full power, 120 kt).
        /// Each puff is then given whatever share of that depth its overlap with its neighbours leaves it
        /// (<see cref="PuffAlphaFor"/>), so the trail's coverage no longer depends on how many puffs happen to stand
        /// along it. (899ada84 set each PUFF's alpha instead — 0.98 × (1 − age/life)^1.3 × 2 d0/d — which with the old
        /// 10–30 s lives works out at ~0.2 per puff by 5 s and ~0.02 by 20 s: measured across the column, 0.65 at 5 s,
        /// 0.11 at 20 s and nothing at 45 s — renders/smoke-thicker, SmokeAgeRender.)</summary>
        private const float TauCore = 17f;
        /// <summary>The mean of FlyingGame/Smoke's profile along a line through the puff's middle (flat to r 0.45, then a
        /// smoothstep to the edge): how much of a puff's depth a ray straight through the column collects.</summary>
        private const float ProfileMean = 0.725f;
        /// <summary>A puff wanders off the path by up to this × its diameter (turbulent diffusion): billows, at no cost.</summary>
        private const float PuffWander = 0.16f;
        /// <summary>FEWER PUFFS AS THEY GROW (Quest at 72/90 Hz, the iPhone): neighbours of a grown puff overlap many times
        /// over, so past these ages every other puff fades out and its neighbour takes over its share of the column's depth
        /// and its length of path — 30 → 15 a second over 1.5–3 s, and → 7.5 over 18–26 s, by when a puff is ~12 m across
        /// and four slots (~8 m at 120 kt) still overlap it 2–3 times over. Crossfaded, so nothing pops. (A second stage
        /// at 6 s, with 7 m puffs, rendered a loop as a string of beads; at 18 s the puffs are big enough to hide it.)</summary>
        private const float ThinFrom = 1.5f, ThinTo = 3f, Thin2From = 18f, Thin2To = 26f;

        /// <summary>The 1/30 s slot a sample falls in. Past 0.2 s old, Build draws one puff per slot, from the NEWEST
        /// sample in it.</summary>
        public static long SlotOf(double t) => (long)System.Math.Floor(t * Hz);
        /// <summary>Samples younger than this are all kept (the pulse tubes and the first 0.2 s read every one).</summary>
        public const double FullRateS = 2.0;
        /// <summary>Drop from <paramref name="ring"/> (oldest first) every sample older than <see cref="FullRateS"/> that is not
        /// the newest in its slot: Build would skip it anyway, so the drawing is unchanged, but a 90 s ring at 60–90 Hz is
        /// 5,400–8,100 samples to walk every frame and this leaves ~2,700. Call it now and then (it is O(n)).</summary>
        public static void ThinRing(List<Sample> ring, double nowT)
        {
            int w = 0, n = ring.Count;
            for (int r = 0; r < n; r++)
            {
                Sample s = ring[r];
                if (nowT - s.T > FullRateS && r + 1 < n && SlotOf(ring[r + 1].T) == SlotOf(s.T)) continue;
                ring[w++] = s;
            }
            if (w < n) ring.RemoveRange(w, n - w);
        }
        private static float ColumnTau(float flow, float d0, float d) => TauCore * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(flow)) * Mathf.Clamp01(d0 / Mathf.Max(1e-3f, d));
        /// <summary>One puff's alpha, so that the <paramref name="overlap"/> puffs a ray through the column crosses add up to
        /// optical depth <paramref name="tau"/> (each standing for <paramref name="carry"/> slots' worth of it).</summary>
        public static float PuffAlphaFor(float tau, float overlap, float carry) => 1f - Mathf.Exp(-tau * carry / (ProfileMean * Mathf.Max(1f, overlap)));
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly List<Vector3> _v = new(); private readonly List<Vector2> _uv = new(); private readonly List<Color> _c = new(); private readonly List<int> _tri = new();
        private readonly List<(float d2, Vector3 p, Vector3 axis, float w, float l, float a)> _puffs = new();
        /// <summary>What the last Build drew: each piece's centre, axis, width, length and alpha (review tools only).</summary>
        public IReadOnlyList<(float d2, Vector3 p, Vector3 axis, float w, float l, float a)> Pieces => _puffs;
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
            r.sharedMaterial = new Material(Shader.Find("FlyingGame/Smoke") ?? Shader.Find("FlyingGame/Spray")) { color = new Color(0.99f, 0.99f, 1f, 1f) };
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

        /// <summary>A glider's smoke: both wingtips, at the trailing edge of the actual tip.</summary>
        public static Vec3[] WingtipsFor(AircraftConfig cfg)
        {
            FlyingGame.Core.DataContracts.StripConfig tip = null;
            foreach (var sf in cfg.Surfaces) foreach (var st in sf.Strips) if (System.Math.Abs(st.Pos[2]) < 1.0 && (tip == null || System.Math.Abs(st.Pos[1]) > System.Math.Abs(tip.Pos[1]))) tip = st;
            if (tip == null || System.Math.Abs(tip.Pos[1]) < 1.0) return System.Array.Empty<Vec3>();
            // THE ACTUAL WINGTIP, not 3 % outboard of the outermost strip's CENTRE. Ty 2026-11-10: "Make the 2-33
            // smoke come from the trailing edge of the actual wingtip." A strip's Pos is its centre and its span is
            // its own Area/Chord, so the tip is half a strip further out. That derivation lands on every glider's
            // published span to the centimetre -- 2-33 15.54 m, Grob 103 18.0, Swift S-1 12.66, EB29R 29.3 -- where
            // the old 1.03 factor was a guess that scaled with span and so erred in both directions: 18 cm OUTBOARD
            // of the 2-33's tip, leaving the canister hanging in clear air, and 17 cm INBOARD of the Swift's.
            double halfStrip = tip.Chord > 0.0 ? 0.5 * tip.Area / tip.Chord : 0.0;
            double y = System.Math.Abs(tip.Pos[1]) + halfStrip, x = tip.Pos[0] - 0.75 * tip.Chord;
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
                        Vec3 p10 = Parcel(so, sn, ei, e, dir, t10, evo + Q10 * FlyingGame.Core.Aero.ExhaustPulse.WindowRev, meanU, fs, nowT, out _);
                        Vec3 p50 = Parcel(so, sn, ei, e, dir, t50, evo + Q50 * FlyingGame.Core.Aero.ExhaustPulse.WindowRev, meanU, fs, nowT, out double pen50);
                        Vec3 p90 = Parcel(so, sn, ei, e, dir, t90, evo + Q90 * FlyingGame.Core.Aero.ExhaustPulse.WindowRev, meanU, fs, nowT, out _);
                        if (!(double.IsFinite(p10.X) && double.IsFinite(p50.Y) && double.IsFinite(p90.Z) && double.IsFinite(p10.Y) && double.IsFinite(p90.X))) continue;
                        Vec3 spanW = p90 - p10, midW = 0.5 * (p10 + p90);
                        float len = (float)spanW.Length;
                        Vec3 axisW = len > 1e-4f ? spanW * (1.0 / len) : velW * (1.0 / v);
                        Vector3 axis = CoordinateMap.ToUnity(axisW);
                        float width = (float)PipeDiameterM + 0.22f * (float)pen50 + TubeDiffusion * Mathf.Sqrt(age50);
                        // ... widening toward the trail's own puff at that age (the same law as below), so the handover is one
                        // column getting fatter, not a thin one fading inside a fat one.
                        float d0 = Mathf.Clamp(PuffD0 * Mathf.Sqrt(flow / v), PuffD0Min, PuffD0Max);
                        float trailW = Mathf.Min(d0 + PuffGrowth * Mathf.Sqrt(age50), 0.3f + 0.2f * v * age50);
                        width = Mathf.Lerp(width, Mathf.Max(width, trailW), Mathf.Clamp01(age50 / TubeAgeS));
                        // The 2 % and 98 % parcels sit where the drawn puff is down to a quarter (r 0.82 in FlyingGame/Smoke),
                        // so a six's tubes overlap to a ~0.4 neck, near the pulse train's own 0.28 trough.
                        float length = Mathf.Max(width, len / 0.82f + 0.5f * width);
                        Clear(ref midW, axisW, ref length, width, age50);
                        Vector3 mid = shown(midW);
                        if (!(float.IsFinite(mid.x) && float.IsFinite(mid.y) && float.IsFinite(mid.z))) continue;
                        // Opacity per tube, so the column of overlapping tubes is as dense as the trail it becomes; and the
                        // handover is in optical DEPTH (tube × hand, puff × (1 − hand)), so the two together are exactly as
                        // dense as either alone. (899ada84 crossfaded the alphas, which at the midpoint left 1 − (1 − a/2)² ≈ 0.75
                        // of an opaque column: a see-through band half a second behind the aeroplane.)
                        float overlap = Mathf.Max(1f, length / Mathf.Max(0.05f, pathPerPulse));
                        float a = PuffAlphaFor(ColumnTau(flow, d0, width), overlap, hand);
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
        private Vec3 Parcel(in Sample so, in Sample sn, int ei, Vec3 e, Vec3 dir, double tq, double crank, double meanU, double fs,
                            double nowT, out double penetration)
        {
            double g = (tq - so.T) / (sn.T - so.T), age = System.Math.Max(0, nowT - tq);
            Vec3 pos = so.Pos + (sn.Pos - so.Pos) * g;
            Quat att = g < 0.5 ? so.Att : sn.Att;
            double u = FlyingGame.Core.Aero.ExhaustPulse.Velocity(crank, meanU, Cylinders);
            penetration = FlyingGame.Core.Aero.ExhaustPulse.Penetration(u - fs, age, PipeDiameterM);
            // The mean jet is already in the airframe flow's push; without that model the parcel carries all of its own.
            double extra = Flow != null ? penetration - FlyingGame.Core.Aero.ExhaustPulse.Penetration(meanU - fs, age, PipeDiameterM) : penetration;
            Vec3 drift = Atmosphere.SteadyWindAt(pos) * age + new Vec3(0, 0, -System.Math.Min(2.0, 0.12 * age));
            return pos + att.Rotate(e + dir * extra) + drift + Pushed(so, ei, e, dir, age);
        }

        // AIRFRAME CLEARANCE (Ty t914u: "no smoke may pass through any portion of any airplane", the Pitts' upright flat spin
        // the case). Each puff and pulse tube is a soft blob stretched along a segment. The markers' own paths go round the
        // airframe (Pushed), but the DRAWING did not: a young puff is stretched back along the flight path, which in a flat
        // spin is straight down, so it ran from the belly exhaust up through the cowling; a tube is drawn straight between
        // parcels that went round opposite sides. So every piece younger than ClearAgeS and within reach of the airframe is
        // taken into the body frame of the pose being SHOWN (the aeroplane on screen, live and in a replay alike), its centre
        // held outside and its segment cut where it enters a body (AirframeFlow.ClearSegment). Older smoke is a cloud the
        // aeroplane may fly back into; that is left to the depth buffer, as a real aeroplane parts a cloud.
        private const double ClearAgeS = 3.0;
        private Vec3 _bodyPos; private Quat _bodyAtt, _bodyInv; private bool _bodyOn; private double _reach;
        /// <summary>Pieces the last Build tested against the airframe, and how many it had to move or cut (review plates).</summary>
        public int ClearTested { get; private set; }
        public int ClearTrimmed { get; private set; }
        /// <summary>Off only for the before/after plates.</summary>
        public bool ClearAirframe = true;

        /// <summary>A piece centred at <paramref name="p"/> (true path, world NED), long <paramref name="len"/> along
        /// <paramref name="axisW"/> (world, unit), <paramref name="age"/> s old: held out of the airframe being shown.</summary>
        private void Clear(ref Vec3 p, Vec3 axisW, ref float len, float width, double age)
        {
            if (!_bodyOn || age > ClearAgeS) return;
            Vec3 b = _bodyInv.Rotate(p - _bodyPos);
            double half = 0.5 * System.Math.Max(0, len - width);   // the spine: the blob's round ends add half a width each
            if (b.Length > _reach + half + width) return;
            ClearTested++;
            var (c, h) = Flow.ClearSegment(b, _bodyInv.Rotate(axisW), half, out bool trimmed);
            if (!trimmed) return;
            ClearTrimmed++;
            p = _bodyPos + _bodyAtt.Rotate(c);
            len = (float)(2 * h) + width;
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
            ClearTested = ClearTrimmed = 0;
            _bodyOn = ClearAirframe && Flow != null && sample(0) is Sample sb;
            if (_bodyOn) { Sample b0 = sample(0).Value; _bodyPos = b0.Pos; _bodyAtt = b0.Att; _bodyInv = b0.Att.Conjugate(); _reach = Flow.Reach; }
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
                    long slot = SlotOf(s.T);
                    if (slot == _lastSlot) continue;
                    _lastSlot = slot; share = 1f; span = 1.0 / Hz;   // (and its length: a slot's worth of path, so the column stays joined)
                    if (!Stream)
                    {
                        float t = Mathf.InverseLerp(ThinFrom, ThinTo, (float)age), t2 = Mathf.InverseLerp(Thin2From, Thin2To, (float)age);
                        carry = (slot & 1) != 0 ? 1f - t : 1f + t;           // odd slots fade out, even ones take over their share
                        if ((slot & 1) == 0) carry *= (slot & 2) != 0 ? 1f - t2 : 1f + t2;   // and later every other even one
                        if (carry <= 0.001f) continue;
                        span *= Mathf.Max(1f, carry);                        // and cover that much path, back from the release
                    }
                }
                if (age > LifeMaxS) break;
                if (age < 0 || s.Flow <= 0.001f) continue;
                // Each puff's own life (10–30 s) and a little spread, from a hash of its time — the same puff every redraw.
                uint hsh = (uint)(s.T * 600.0 + 0.5) * 2654435761u;   // (per sample at up to 600 a second)
                float rnd = (hsh >> 8 & 0xFFFF) / 65535f, rnd2 = (hsh >> 4 & 0xFF) / 255f;
                float life = Stream ? 30f + 15f * rnd : PuffLife + PuffLifeSpread * rnd;
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
                float a;
                if (Stream)
                {
                    float fade = Mathf.Pow(1f - (float)age / life, 1.3f) * Mathf.Clamp01(d0 / grow * 1.6f);
                    a = Mathf.Min(1f, 0.65f * Mathf.Lerp(0.45f, 1f, s.Flow) * fade * share);
                }
                else
                {
                    // The column's depth at this age (see TauCore), shared among the puffs a ray across it meets: this one
                    // stands for `carry` slots of path (0 … 4 as its neighbours are thinned out), its overlap counted per
                    // slot. Near the end of its own life it fades out.
                    float overlap = stretch / Mathf.Max(0.05f, v * (float)span / Mathf.Max(1f, carry));
                    float end = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp((1f - PuffFadeShare) * life, life, (float)age));
                    // The pulse tubes own the first second; the trail's puff takes over the depth as they give it up.
                    float hand = tubes ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TubeHandFrom, TubeAgeS, (float)age)) : 1f;
                    a = PuffAlphaFor(ColumnTau(s.Flow, d0, grow) * end, overlap, carry * hand);   // (overlap already counts the young samples' closer spacing: no `share`)
                }
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
                Vec3 axisW = velW * (1.0 / v), wanderW = CoordinateMap.ToSim(wander);
                for (int ei = 0; ei < _exhaust.Length; ei++)
                {
                    Vec3 e = _exhaust[ei], jd = ei < _jet.Length ? _jet[ei] : Vec3.Zero;
                    Vec3 pt = s.Pos + s.Att.Rotate(e + jd * jetM) + back + drift + Pushed(s, ei, e, jd, age) + wanderW;
                    float stretchE = stretch;
                    Clear(ref pt, axisW, ref stretchE, grow, age);
                    Vector3 p = shown(pt);
                    // A non-finite puff (a NaN anywhere in its push) would poison every blend it touches: its colour came out
                    // NaN and the airframe under the smoke column rendered (0,0,0) (Ty 2026-10-13: "rendering as black").
                    if (!(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) && float.IsFinite(a) && float.IsFinite(grow) && float.IsFinite(stretchE)) || a < 0 || grow <= 0)
                    { if (_nanLogged++ < 5) Debug.LogWarning($"[Smoke] non-finite puff dropped: age {age:0.000} p {p} a {a} grow {grow} stretch {stretch} jet {s.JetU0:0.0} ρ {s.AirDensity:0.000}"); continue; }
                    float d2 = Vector3.Dot(p - camPos, camFwd);
                    if (d2 < 0.3f) continue;
                    // A puff the camera is inside (a chase camera flies through the up-line's smoke) would wash the whole
                    // picture white: thin it out within a few diameters of the eye.
                    // (Only once the eye is nearly inside it: 899ada84's 0.8–4 diameters thinned every grown puff within
                    // ~35–70 m of the camera, which is most of what you see turning back round a loop into your own smoke.)
                    float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f * grow, 1.5f * grow, (p - camPos).magnitude));
                    if (a * near < 0.004f) continue;
                    _puffs.Add((d2, p, axis, grow, stretchE, a * near));
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
                    // (0.80 underneath, was 0.42: Ty 2026-11-04, "more white" — the grey half read as dirty, thin smoke)
                    float b = Mathf.Lerp(0.80f, 1f, 0.5f + 0.5f * up);
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

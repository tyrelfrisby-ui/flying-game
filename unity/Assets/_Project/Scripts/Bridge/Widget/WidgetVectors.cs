using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The widget's forces, flows and moments (protocol 7, owner 2026-10-09: "forces and moments that teach"):
    ///   WING WIND ×2 — the local relative wind at mid-span of each wing (the rotation's ω × r included, so in a spin the two
    ///     wings differ), labelled with that wing's α, red + STALLED past the type's critical α;
    ///   TAIL WIND ×2 — the flow at mid-span of each stab half, as the aero model sees it: the wing's downwash ε (collapsing
    ///     when the wing stalls), the prop's slipstream and swirl, the rotation; LENGTH = the local speed, so power visibly
    ///     lengthens them (η_t > 1) and idle shortens them (blanketing);
    ///   INERTIAL — m(g − a) from the CG = −(every non-gravity force): weight and the acceleration together;
    ///   TOTAL AERO — the sum of all aerodynamic forces, from the neutral point (equal and opposite to INERTIAL in any steady,
    ///     power-off state; with power, aero + thrust is);
    ///   TAIL — the horizontal tail's up/down force at the tail;
    ///   MOMENTS — two arcs in the pitch plane: the AERODYNAMIC pitching moment about the CG (ahead of the nose) and the
    ///     INERTIA-COUPLING moment −(ω × Iω) (behind the tail) on one scale. Their sum is I·q̇: equal and opposite in a steady
    ///     spin, and the aero one wins when forward stick recovers it.
    /// Drawing (protocol 7): bold (≈ 6 px) with a dark outline, every vector low-pass filtered in the AIRPLANE's axes (a steady
    /// spin is steady there, so nothing lags the airframe), labels anchored in screen space, filtered, pixel-snapped, numbers
    /// at most 4×/s with hysteresis, fixed slots with an alternate slot and a fade. Review replays the filtered picture.
    /// The flat world references (runway, horizon, ground grid) stay thin GL lines drawn with the camera.
    /// </summary>
    public sealed class WidgetVectors : MonoBehaviour
    {
        public AeroWidget Widget;
        private Material _mat;
        public float WidthScale = 1f;
        public float SmoothingMs = 130f;

        public delegate bool ScrFn(Vector3 world, out Vector2 px);
        public static readonly Color Lift = new(0.25f, 1f, 0.35f), Drag = new(1f, 0.3f, 0.25f), Weight = new(1f, 0.9f, 0.2f),
            Thrust = new(0.35f, 0.6f, 1f), Wind = new(0.3f, 0.95f, 1f), Total = new(1f, 1f, 1f), Axis = new(1f, 0.3f, 1f), Wheel = new(1f, 0.6f, 0.15f),
            Inertial = new(1f, 0.82f, 0.2f), TailC = new(0.75f, 0.55f, 1f), TailWindC = new(0.55f, 0.85f, 1f);

        private void Start() => _mat = new Material(Shader.Find("FlyingGame/HudLine")) { hideFlags = HideFlags.HideAndDontSave, color = Color.white };

        // ================= the physics picture of one frame (body axes) =================
        public sealed class Picture
        {
            public readonly Dictionary<string, (Vec3 from, Vec3 vec)> V = new();   // body-frame origin (config coords) and vector
            public double LiftN, DragN, BallDeg;
            public double AeroG, InertialG, TailN, MomAero, MomInertia, WingAlphaL, WingAlphaR, TailAlphaL, TailAlphaR, Eps, EtaL, EtaR, TailSpdL, TailSpdR, VInf;
            public bool StallL, StallR, HasTail;
        }

        /// <summary>Everything the frame's arrows say, from a state + its force samples (live or a history frame).</summary>
        public Picture Compute(RigidBodyState s, IReadOnlyList<ForceSample> forces, double critAlphaDeg)
        {
            var w = Widget; var cfg = w.Config; var mp = w.Ac.MassProperties;
            Vec3 cg = cfg.Mass.CgVec();
            double W = mp.MassKg * 9.81;
            var pic = new Picture();
            Vec3 windB = s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
            Vec3 vAir = s.Velocity - windB; pic.VInf = vAir.Length;

            Vec3 aero = Vec3.Zero, nonGrav = Vec3.Zero, thrust = Vec3.Zero, lift = Vec3.Zero, drag = Vec3.Zero, tailF = Vec3.Zero, tailPos = Vec3.Zero, thrustPos = Vec3.Zero;
            double mAero = 0, tailW = 0;
            var tailL = new List<ForceSample>(); var tailR = new List<ForceSample>();
            if (forces != null)
                foreach (var f in forces)
                {
                    switch (f.Kind)
                    {
                        case "tailflow": (f.PosBody.Y < 0 ? tailL : tailR).Add(f); continue;
                        case "weight": continue;
                        case "lift": case "drag": case "fuselage": case "spoiler": case "moment":
                        {
                            aero += f.ForceBody;
                            Vec3 r = f.PosBody - cg;
                            mAero += (r.Z * f.ForceBody.X - r.X * f.ForceBody.Z) + f.MomentBody.Y;
                            if (f.Kind == "lift") lift += f.ForceBody; else if (f.Kind == "drag") drag += f.ForceBody;
                            // The horizontal tail: aft, and its force mostly vertical (the fin's is sideways).
                            if ((f.Kind == "lift" || f.Kind == "drag") && f.PosBody.X < cg.X - 0.35 * w.LengthM && System.Math.Abs(f.ForceBody.Z) >= System.Math.Abs(f.ForceBody.Y))
                            { tailF += f.ForceBody; double a = System.Math.Abs(f.ForceBody.Z) + 1e-6; tailPos += f.PosBody * a; tailW += a; }
                            break;
                        }
                        case "thrust": thrust += f.ForceBody; thrustPos = f.PosBody; break;
                    }
                    if (f.Kind != "moment") nonGrav += f.ForceBody;
                }
            // INERTIAL = m(g − a) = −(every non-gravity force); TOTAL AERO from the neutral point.
            Vec3 inertial = -1.0 * nonGrav;
            // Lift and drag along the airflow; the slip ball (it sits opposite the lateral specific force).
            if (pic.VInf > 1)
            {
                Vec3 vh = vAir * (1.0 / pic.VInf), liftDir = -1.0 * Vec3.Cross(vh, new Vec3(0, 1, 0));
                double ld = liftDir.Length; if (ld > 1e-6) liftDir = liftDir * (1.0 / ld);
                pic.DragN = -(aero.X * vh.X + aero.Y * vh.Y + aero.Z * vh.Z); pic.LiftN = aero.X * liftDir.X + aero.Y * liftDir.Y + aero.Z * liftDir.Z;
            }
            Vec3 sf = nonGrav * (1.0 / mp.MassKg);
            pic.BallDeg = -System.Math.Atan2(sf.Y, System.Math.Max(0.5, -sf.Z)) * 57.2958;
            pic.InertialG = inertial.Length / W; pic.AeroG = aero.Length / W;
            pic.V["inertial"] = (cg, inertial * (1.0 / W));
            pic.V["total"] = (w.NeutralPointBody(), aero * (1.0 / W));
            pic.V["weight"] = (cg, s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1)));
            pic.V["wind"] = (cg, vAir * (-1.0 / System.Math.Max(1, pic.VInf)));   // RELATIVE wind: the air coming at the airplane (−velocity)
            pic.V["lift"] = (cg, lift * (1.0 / W)); pic.V["drag"] = (cg, drag * (3.0 / W));
            if (thrust.Length > 1) pic.V["thrust"] = (thrustPos, thrust * (3.0 / W));
            if (tailW > 0) { pic.HasTail = true; pic.TailN = tailF.Z; pic.V["tail"] = (tailPos * (1.0 / tailW), new Vec3(0, 0, tailF.Z * 4.0 / W)); }
            // Moments about the CG (pitch, nose-up +): aero, and the inertia coupling −(ω × Iω)_y.
            Vec3 om = s.Rates;
            double hx = mp.Ixx * om.X - mp.Ixz * om.Z, hz = mp.Izz * om.Z - mp.Ixz * om.X;
            pic.MomAero = mAero; pic.MomInertia = -(om.Z * hx - om.X * hz);
            // Wing winds at mid-semispan (ω × r).
            foreach (int side in new[] { -1, 1 })
            {
                Vec3 p = w.WingMidSpan(side);
                Vec3 v = vAir + Vec3.Cross(s.Rates, p - cg);
                double a = System.Math.Atan2(v.Z, System.Math.Max(0.1, v.X)) * 57.2958;
                pic.V[side < 0 ? "wingL" : "wingR"] = (p, v * (-1.0 / System.Math.Max(1, pic.VInf)));   // the local relative wind: −(the station's velocity through the air)
                if (side < 0) { pic.WingAlphaL = a; pic.StallL = a > critAlphaDeg; } else { pic.WingAlphaR = a; pic.StallR = a > critAlphaDeg; }
            }
            // Tail winds: the stab strip nearest each half's mid-span.
            foreach (var (list, side) in new[] { (tailL, -1), (tailR, 1) })
            {
                if (list.Count == 0) continue;
                double maxY = 0; foreach (var f in list) maxY = System.Math.Max(maxY, System.Math.Abs(f.PosBody.Y));
                ForceSample best = list[0];
                foreach (var f in list) if (System.Math.Abs(System.Math.Abs(f.PosBody.Y) - maxY * 0.5) < System.Math.Abs(System.Math.Abs(best.PosBody.Y) - maxY * 0.5)) best = f;
                // Effective local speed √η·V: the slipstream lengthens it, blanketing / the wake shortens it.
                double spd = System.Math.Sqrt(System.Math.Max(0, best.MomentBody.Z)) * pic.VInf, at = best.MomentBody.X * 57.2958;
                Vec3 dirB = best.ForceBody * (1.0 / System.Math.Max(1e-6, best.ForceBody.Length));
                pic.V[side < 0 ? "tailWindL" : "tailWindR"] = (best.PosBody, dirB * (-spd / System.Math.Max(1, pic.VInf)));   // the air arriving at the stab (−its flow velocity)
                if (side < 0) { pic.TailAlphaL = at; pic.EtaL = best.MomentBody.Z; pic.TailSpdL = spd; } else { pic.TailAlphaR = at; pic.EtaR = best.MomentBody.Z; pic.TailSpdR = spd; }
                pic.Eps = best.MomentBody.Y * 57.2958;
            }
            return pic;
        }

        // ================= filtering =================
        private readonly Dictionary<string, (Vec3 from, Vec3 vec)> _filt = new();
        private int _reviewIdx = -1;
        private static Vec3 Lerp(Vec3 a, Vec3 b, double k) => a + (b - a) * k;
        private void Filter(Picture raw, double dt, Dictionary<string, (Vec3 from, Vec3 vec)> into)
        {
            double k = SmoothingMs <= 1 ? 1 : 1 - System.Math.Exp(-dt * 1000.0 / SmoothingMs);
            foreach (var kv in raw.V)
                into[kv.Key] = into.TryGetValue(kv.Key, out var o) ? (Lerp(o.from, kv.Value.from, k), Lerp(o.vec, kv.Value.vec, k)) : kv.Value;
        }

        /// <summary>The picture being shown, filtered (live: one filter step; review: the filter replayed over the frames
        /// leading up to the reviewed one, so it draws what was on screen then).</summary>
        public Picture Current { get; private set; }
        public Dictionary<string, (Vec3 from, Vec3 vec)> Filtered => _filt;

        private void Advance()
        {
            var w = Widget; if (w == null || w.Ac == null) return;
            double crit = w.CriticalAlphaDeg(w.Shown != null ? w.Shown.FlapsActual : w.Ac.FlapFraction);
            if (w.Shown == null)
            {
                _reviewIdx = -1;
                Current = Compute(w.Ac.State, w.Ac.LastForces, crit);
                Filter(Current, Mathf.Min(Time.unscaledDeltaTime, 0.1f), _filt);
                return;
            }
            int idx = w.Review.Index;
            if (idx == _reviewIdx && Current != null) return;
            _reviewIdx = idx;
            var hist = w.Review.History;
            int start = Mathf.Min(idx + 12, hist.Count - 1);
            _filt.Clear();
            for (int i = start; i >= idx; i--)
            {
                var f = hist.Get(i);
                var p = Compute(f.State, f.Forces, crit);
                Filter(p, 1.0 / WidgetHistory.Hz, _filt);
                if (i == idx) Current = p;
            }
        }

        // ================= drawing (into the compositor, frame pixels) =================
        private bool On(string k) => Widget.Show.TryGetValue(k, out bool b) && b;

        /// <summary>Called by the compositor each frame after the camera is placed: arrows, arcs and labels for the frame shown.</summary>
        public void Build(WidgetControlsDisplay ui, Rect scene)
        {
            Advance();
            var w = Widget; if (Current == null) return;
            bool vec = w.DisplayMode == "classic" || On("vectors");
            var s = w.ShownState; var cam = w.Cam; Vec3 cg = w.Config.Mass.CgVec();
            Vector3 World(Vec3 bodyPos) => CoordinateMap.ToUnity(s.Position + s.Attitude.Rotate(bodyPos - cg));
            Vector3 Dir(Vec3 body) => CoordinateMap.ToUnity(s.Attitude.Rotate(body));
            bool Scr(Vector3 wp, out Vector2 px) { var p = cam.WorldToScreenPoint(wp); px = new Vector2(scene.x + p.x, scene.y + p.y); return p.z > 0.05f; }
            Vector3 cgU = World(cg);
            float pxM = PixelM(cgU);
            Scr(cgU, out var cgScr);
            float gPx = 190f;   // one g (one weight) on the screen
            float lw = 6f * WidthScale * (scene.height / AeroWidget.FrameSize + 0.25f) / 1.25f;

            // Readout (classic) and the airplane-scale tag.
            var rd = w.Read();
            if (w.DisplayMode == "classic" && On("readout"))
            {
                string extra = w.Current == AeroWidget.Scenario.Cruise ? $"ALT {rd.HeightFt:F0} ft  VS {-rd.SinkFpm:+0;-0} fpm  PWR {w.Controls.Throttle01 * 100:F0}%"
                    : w.Current == AeroWidget.Scenario.Spin ? $"YAW {rd.YawRateDps:+0;-0}°/s  ROLL {rd.RollRateDps:+0;-0}°/s"
                    : $"SINK {rd.SinkFpm:F0} fpm  HT {rd.HeightFt:F0} ft{(rd.OnGround ? "  ON THE WHEELS" : "")}";
                string rev = w.Paused && On("review") ? $"REVIEW {w.Review.OffsetMs / 1000.0:+0.0;-0.0;0.0} s\n" : "";
                float ty = scene.yMax - 24;
                foreach (var line in (rev + $"{w.AircraftName}   {rd.Kias:F0} KIAS   α {rd.AlphaDeg:F1}°   PITCH {rd.PitchDeg:+0;-0}°\n" + extra).Split('\n'))
                { ui.PxText(new Vector2(scene.x + 22, ty), line, 19, Color.white, TextAnchor.MiddleLeft); ty -= 24; }
            }
            if (w.VisualScale > 1.01f && Scr(cgU - cam.transform.up * pxM * 40f, out var sp)) _labels.Request("scale", sp, Vector2.zero, $"AIRPLANE ×{w.VisualScale:0}", new Color(1, 1, 1, 0.85f), 0);
            if (!vec) { _labels.Place(ui, scene, cgScr); return; }

            var F = _filt; var P = Current;
            void Arrow(string key, Color c, float scalePx, bool arriving = false)
            {
                if (!F.TryGetValue(key, out var v)) return;
                Vector3 o = World(v.from), d = Dir(v.vec) * (scalePx * pxM);
                if (d.sqrMagnitude < 1e-10f) return;
                Vector3 a3 = arriving ? o - d : o, b3 = arriving ? o : o + d;
                if (Scr(a3, out var a) && Scr(b3, out var b) && ClipToScene(ref a, ref b, scene)) ui.PxArrow(a, b, lw, c);
            }
            Vector2 Tip(string key, float scalePx, bool arriving = false)
            {
                var v = F[key]; Vector3 o = World(v.from), d = Dir(v.vec) * (scalePx * pxM);
                Scr(arriving ? o - d : o + d, out var t); return t;
            }
            Vector2 Origin(string key) { Scr(World(F[key].from), out var o); return o; }

            // Wing winds (arriving at each mid-span point).
            float windPx = 150f;
            foreach (var (k, a, st, nm) in new[] { ("wingL", P.WingAlphaL, P.StallL, "L WING"), ("wingR", P.WingAlphaR, P.StallR, "R WING") })
            {
                if (!On("wingWind") || !F.ContainsKey(k)) continue;
                Color c = st ? Drag : Wind;
                Arrow(k, c, windPx, true);
                _labels.Request(k, Tip(k, windPx, true), Away(Origin(k), Tip(k, windPx, true)), new[] { a }, v => $"{nm} α {v[0]:0}°{(st ? " STALLED" : "")}", c, 2);
            }
            if (On("wind") && F.ContainsKey("wind")) { Arrow("wind", Wind, windPx, true); _labels.Request("wind", Tip("wind", windPx, true), Away(Origin("wind"), Tip("wind", windPx, true)), new[] { rd.AlphaDeg }, v => $"RELATIVE WIND α {v[0]:0.0}°", Wind, 3, 0.1); }
            // Tail winds (length = the local speed: prop blast lengthens, blanketing shortens).
            foreach (var (k, a, eta, nm) in new[] { ("tailWindL", P.TailAlphaL, P.EtaL, "L STAB"), ("tailWindR", P.TailAlphaR, P.EtaR, "R STAB") })
            {
                if (!On("tailWind") || !F.ContainsKey(k)) continue;
                Arrow(k, TailWindC, windPx * 0.8f, true);
                _labels.Request(k, Tip(k, windPx * 0.8f, true), Away(Origin(k), Tip(k, windPx * 0.8f, true)), new[] { a, P.Eps }, v => $"{nm} α {v[0]:0}° · ε {v[1]:0}°", TailWindC, 4);
            }
            if (On("inertial") && F.ContainsKey("inertial")) { Arrow("inertial", Inertial, gPx); _labels.Request("inertial", Tip("inertial", gPx), Away(Origin("inertial"), Tip("inertial", gPx)), new[] { P.InertialG }, v => $"INERTIAL {v[0]:0.0} g", Inertial, 1, 0.1); }
            if (On("total") && F.ContainsKey("total")) { Arrow("total", Total, gPx); _labels.Request("total", Tip("total", gPx), Away(Origin("total"), Tip("total", gPx)), new[] { P.AeroG }, v => $"TOTAL AERO {v[0]:0.0} g", Total, 1, 0.1); }
            if (On("weight")) { Arrow("weight", Weight, gPx); _labels.Request("weight", Tip("weight", gPx), Away(Origin("weight"), Tip("weight", gPx)), new double[0], v => "WEIGHT", Weight, 3); }
            if (On("lift")) { Arrow("lift", Lift, gPx); _labels.Request("lift", Tip("lift", gPx), Away(Origin("lift"), Tip("lift", gPx)), new double[0], v => "LIFT", Lift, 5); }
            if (On("drag")) { Arrow("drag", Drag, gPx); _labels.Request("drag", Tip("drag", gPx), Away(Origin("drag"), Tip("drag", gPx)), new double[0], v => "DRAG ×3", Drag, 5); }
            if (On("thrust") && F.ContainsKey("thrust")) Arrow("thrust", Thrust, gPx);
            if (On("tail") && P.HasTail && F.ContainsKey("tail"))
            {
                Arrow("tail", TailC, gPx);
                double lb = P.TailN * 0.2248;   // body z down: + = DOWN force
                _labels.Request("tail", Tip("tail", gPx), Away(Origin("tail"), Tip("tail", gPx)), new[] { System.Math.Abs(lb) }, v => $"TAIL {(lb >= 0 ? "↓" : "↑")} {v[0]:0} lb", TailC, 3, 5);
            }
            if (On("wheels") && w.Ac.LastForces != null && w.Shown == null)
                foreach (var f in w.Ac.LastForces) if (f.Kind == "gear" && f.ForceBody.Length > w.Ac.MassProperties.MassKg * 9.81 * 0.02)
                    { Vector3 o = World(f.PosBody), d = Dir(f.ForceBody * (1.0 / (w.Ac.MassProperties.MassKg * 9.81))) * (gPx * pxM); if (Scr(o, out var a) && Scr(o + d, out var b) && ClipToScene(ref a, ref b, scene)) ui.PxArrow(a, b, lw * 0.7f, Wheel); }

            // CG and neutral-point marks (weight & balance; lessons): the CG a black-and-yellow disc, the NP a red ring.
            if (On("cgnp"))
            {
                if (Scr(World(cg), out var cgp)) { ui.PxDisc(cgp, 9f, Color.black); ui.PxDisc(cgp, 6f, Inertial); _labels.Request("cgMark", cgp, Vector2.down, $"CG {w.Loading.CgMac:0} % MAC", Inertial, 6); }
                if (Scr(World(w.NeutralPointBody()), out var npp)) { ui.PxRing(npp, 8f, 3f, new Color(1f, 0.25f, 0.2f)); _labels.Request("npMark", npp, Vector2.up, $"NP {w.Loading.NpMac:0} % · SM {w.Loading.StaticMarginMac:0.0} %", new Color(1f, 0.35f, 0.3f), 6); }
            }
            if (w.Lesson != null) w.Lessons.DrawExtras(ui, this, World, Dir, (Vector3 p, out Vector2 o) => Scr(p, out o), pxM, gPx, lw);
            // Moments: AERO ahead of the nose, INERTIA behind the tail, one shared scale.
            if (On("moments"))
            {
                double mRef = w.Ac.MassProperties.MassKg * 9.81 * 0.06 * System.Math.Max(2, w.LengthM);
                // Both arcs are drawn ABOUT THE CG, in the pitch plane, on a radius that clears the airframe: AERO's arc in front of
                // the nose, INERTIA's behind the tail (owner 2026-10-09).
                float R = (float)w.LengthM * 0.62f + 1.5f;
                Arc(ui, Dir, cgU, R, 0f, P.MomAero, mRef, Total, "aeroM", "AERO", scene, lw);
                Arc(ui, Dir, cgU, R, Mathf.PI, P.MomInertia, mRef, Inertial, "inertiaM", "INERTIA", scene, lw);
            }
            _labels.Place(ui, scene, cgScr);
        }

        /// <summary>Keep a vector inside the picture (it must never run into the instrument panel).</summary>
        public static bool ClipToScene(ref Vector2 a, ref Vector2 b, Rect r)
        {
            r = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            float t0 = 0, t1 = 1; Vector2 d = b - a;
            bool Edge(float p, float q) { if (Mathf.Abs(p) < 1e-6f) return q >= 0; float t = q / p; if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; } else { if (t < t0) return false; if (t < t1) t1 = t; } return true; }
            if (!(Edge(-d.x, a.x - r.xMin) && Edge(d.x, r.xMax - a.x) && Edge(-d.y, a.y - r.yMin) && Edge(d.y, r.yMax - a.y))) return false;
            Vector2 a0 = a; a = a0 + d * t0; b = a0 + d * t1; return true;
        }

        private static Vector2 Away(Vector2 from, Vector2 tip) { Vector2 d = tip - from; return d.sqrMagnitude < 1 ? Vector2.up : d.normalized; }

        /// <summary>A pitching-moment arc about the CG (centre = the CG, in the airplane's pitch plane), centred on the direction
        /// <paramref name="centreAngle"/> (0 = ahead along the body x axis, π = behind), radius <paramref name="R"/> (m). Sweep
        /// and thickness ∝ |M| on one shared scale; the arrowhead shows the sense — nose-up turns body x toward body "up".</summary>
        private void Arc(WidgetControlsDisplay ui, System.Func<Vec3, Vector3> dir, Vector3 cgU, float R, float centreAngle, double m, double mRef,
                         Color c, string id, string name, Rect scene, float lw)
        {
            double k = SmoothingMs <= 1 ? 1 : 1 - System.Math.Exp(-Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 1000.0 / SmoothingMs);
            if (!_mFilt.TryGetValue(id, out double mf) || Widget.Shown != null) mf = m;
            mf += (m - mf) * k; _mFilt[id] = mf;
            float sweep = Mathf.Clamp((float)(System.Math.Abs(mf) / mRef) * 70f, 25f, 160f) * Mathf.Deg2Rad;
            float sgn = mf >= 0 ? 1f : -1f;   // + = nose-up
            Vector3 X = dir(new Vec3(1, 0, 0)), Up = -dir(new Vec3(0, 0, 1));   // body x and body "up" (−z)
            var cam = Widget.Cam; var pts = new List<Vector2>();
            float th0 = centreAngle - sgn * sweep * 0.5f;
            for (int i = 0; i <= 28; i++)
            {
                float th = th0 + sgn * sweep * i / 28f;   // increasing θ = nose-up rotation at every point of the circle
                Vector3 p = cgU + (X * Mathf.Cos(th) + Up * Mathf.Sin(th)) * R;
                var sp = cam.WorldToScreenPoint(p); if (sp.z <= 0) return;
                var q = new Vector2(scene.x + sp.x, scene.y + sp.y);
                if (!scene.Contains(q)) { if (pts.Count > 2) break; else { pts.Clear(); continue; } }
                pts.Add(q);
            }
            if (pts.Count < 3) return;
            float thick = lw * Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01((float)(System.Math.Abs(mf) / (2 * mRef))));
            ui.PxPolyArrow(pts, thick, c);
            double ftlb = System.Math.Abs(m) * 0.7376;
            Vector2 mid = pts[pts.Count / 2];
            _labels.Request(id, mid, Vector2.down, new[] { ftlb, m }, v => $"{name} {(v[1] >= 0 ? "nose-up" : "nose-down")} {v[0]:#,0} ft·lb", c, 2, 10);
        }
        private readonly Dictionary<string, double> _mFilt = new();

        private float PixelM(Vector3 at)
        {
            var cam = Widget.Cam;
            float ph = Mathf.Max(1f, cam.pixelHeight);
            if (cam.orthographic) return cam.orthographicSize * 2f / ph;
            float d = Vector3.Distance(cam.transform.position, at);
            return 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / ph;
        }

        // ================= stable labels =================
        private readonly LabelStabilizer _labels = new();
        /// <summary>Where each label was drawn last frame (frame pixels) and its slot — for the stability test.</summary>
        public IReadOnlyDictionary<string, (Vector2 pos, int slot, float alpha)> LabelDebug => _labels.Last;

        private sealed class LabelStabilizer
        {
            // Labels live in FIXED SLOTS on an ellipse around the airplane (8 directions × 2 rows), joined to their arrow by a
            // thin leader line: the text stays still while the arrow swings (a spin in the locked view turns the airplane in
            // the frame). A label keeps its slot until its arrow has swung > 35° past it (at most once a second), then fades
            // into the new one. Numbers: ≤ 4×/s with hysteresis. Pixel-snapped.
            private sealed class St { public int Slot = -1; public float Alpha; public double[] Shown; public float LastText, LastMove; public string Text; public Vector2 Pos; public bool Has; }
            private readonly Dictionary<string, St> _st = new();
            public readonly Dictionary<string, (Vector2 pos, int slot, float alpha)> Last = new();
            private readonly List<(string id, Vector2 anchor, Vector2 dir, double[] vals, System.Func<double[], string> fmt, Color c, int prio, double step)> _req = new();
            private Vector2 _centre; private bool _hasCentre;

            public void Request(string id, Vector2 anchor, Vector2 dir, string text, Color c, int prio) => _req.Add((id, anchor, dir, new double[0], _ => text, c, prio, 1));
            public void Request(string id, Vector2 anchor, Vector2 dir, double[] vals, System.Func<double[], string> fmt, Color c, int prio, double step = 1) => _req.Add((id, anchor, dir, vals, fmt, c, prio, step));

            private List<Rect> _placedRef;
            private bool Hits(Rect r) { if (_placedRef != null) foreach (var p in _placedRef) if (p.Overlaps(r)) return true; return false; }

            public void Place(WidgetControlsDisplay ui, Rect scene, Vector2 airplane)
            {
                float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                bool paused = ui.Paused;
                // The ring's centre follows the airplane on the screen, heavily filtered (≈ 600 ms).
                if (!_hasCentre || paused) { _centre = airplane; _hasCentre = true; } else _centre = Vector2.Lerp(_centre, airplane, 1f - Mathf.Exp(-dt / 0.6f));
                _req.Sort((a, b) => a.prio.CompareTo(b.prio));
                Last.Clear();
                // The ring fits the picture around the airplane (the outer row included), so slots don't pile up on an edge.
                float rx = Mathf.Min(scene.width * 0.36f, Mathf.Min(_centre.x - scene.xMin - 110, scene.xMax - 110 - _centre.x) / 1.17f);
                float ry = Mathf.Min(scene.height * 0.36f, Mathf.Min(_centre.y - scene.yMin - 20, scene.yMax - 46 - _centre.y) / 1.17f);
                rx = Mathf.Max(rx, scene.width * 0.18f); ry = Mathf.Max(ry, scene.height * 0.16f);
                Vector2 SlotPos(int slot)
                {
                    int dirIdx = slot % 8, row = slot / 8;
                    float ang = dirIdx * Mathf.PI / 4f;   // 0 = right, counter-clockwise
                    float k = 1f + row * 0.17f;
                    var p = _centre + new Vector2(Mathf.Cos(ang) * rx * k, Mathf.Sin(ang) * ry * k);
                    p.x = Mathf.Clamp(p.x, scene.xMin + 110, scene.xMax - 110); p.y = Mathf.Clamp(p.y, scene.yMin + 20, scene.yMax - 46);
                    return p;
                }
                var taken = new HashSet<int>();
                var placed = new List<Rect>(); _placedRef = placed;
                Rect RectAt(Vector2 p, float tw) => new Rect(p.x - tw * 0.5f - 4, p.y - 13, tw + 8, 26);
                foreach (var r in _req)
                {
                    if (!_st.TryGetValue(r.id, out var st)) { st = new St(); _st[r.id] = st; }
                    bool changed = st.Shown == null || st.Shown.Length != r.vals.Length;
                    if (!changed) for (int i = 0; i < r.vals.Length; i++) if (System.Math.Abs(r.vals[i] - st.Shown[i]) >= r.step * 0.75) changed = true;
                    if (changed && (paused || now - st.LastText >= 0.25f)) { st.Shown = (double[])r.vals.Clone(); st.LastText = now; st.Text = r.fmt(st.Shown); }
                    st.Text ??= r.fmt(r.vals);
                    float twid = st.Text.Length * 17f * 0.53f;
                    // The arrow's direction from the airplane picks the preferred slot.
                    Vector2 rel = r.anchor - _centre; float ang = Mathf.Atan2(rel.y / ry, rel.x / rx);
                    int pref = ((Mathf.RoundToInt(ang / (Mathf.PI / 4f)) % 8) + 8) % 8;
                    int slot = st.Slot;
                    if (slot >= 0)
                    {
                        float slotAng = (slot % 8) * Mathf.PI / 4f;
                        float off = Mathf.Abs(Mathf.DeltaAngle(slotAng * Mathf.Rad2Deg, ang * Mathf.Rad2Deg));
                        bool keep = (off < 22.5f + 45f || now - st.LastMove < 1.5f) && !taken.Contains(slot) && !Hits(RectAt(SlotPos(slot), twid));
                        if (!keep) slot = -1;
                    }
                    if (slot < 0)
                    {
                        // nearest free direction (row 0 first, then row 1)
                        for (int t = 0; t < 16 && slot < 0; t++)
                        {
                            int cand = ((pref + (t == 0 ? 0 : ((t + 1) / 2) * (t % 2 == 1 ? 1 : -1))) % 8 + 8) % 8;
                            if (!taken.Contains(cand) && !Hits(RectAt(SlotPos(cand), twid))) slot = cand;
                            else if (!taken.Contains(cand + 8) && !Hits(RectAt(SlotPos(cand + 8), twid))) slot = cand + 8;
                        }
                        if (slot < 0) { Last.Remove(r.id); continue; }   // no room: leave this (lower-priority) label out rather than overlap
                    }
                    taken.Add(slot); placed.Add(RectAt(SlotPos(slot), twid));
                    if (slot != st.Slot) { if (st.Slot >= 0) st.Alpha = 0f; st.Slot = slot; st.LastMove = now; st.Has = false; }   // fade in AT the new slot — no slide
                    Vector2 target = SlotPos(slot);
                    st.Pos = !st.Has || paused ? target : Vector2.Lerp(st.Pos, target, 1f - Mathf.Exp(-dt / 0.3f));
                    st.Has = true;
                    st.Alpha = paused ? 1f : Mathf.MoveTowards(st.Alpha, 1f, dt / 0.25f);
                    var c = r.c; c.a *= st.Alpha;
                    var drawn = new Vector2(Mathf.Round(st.Pos.x), Mathf.Round(st.Pos.y));
                    // Leader: from the label's near edge to the arrow's tip.
                    float tw = st.Text.Length * 17f * 0.53f;
                    Vector2 edge = drawn + new Vector2(Mathf.Clamp(r.anchor.x - drawn.x, -tw * 0.5f, tw * 0.5f), Mathf.Clamp(r.anchor.y - drawn.y, -11f, 11f));
                    if ((r.anchor - edge).magnitude > 14f) ui.PxLeader(edge, r.anchor, new Color(c.r, c.g, c.b, 0.5f * c.a));
                    ui.PxText(drawn, st.Text, 17f, c, TextAnchor.MiddleCenter);
                    Last[r.id] = (drawn, st.Slot, st.Alpha);
                }
                _req.Clear();
            }
        }

        // ================= world references (thin lines, with the camera) =================
        private void OnPostRender()
        {
            if (Widget == null || Widget.Ac == null || _mat == null) return;
            float px = PixelM(CoordinateMap.ToUnity(Widget.ShownState.Position));
            _mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            DrawWorld(px);
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>World references (protocol 5): FINAL — the ground line, the runway as a thick white line from its threshold,
        /// the threshold mark and the aim point; spin and cruise — the horizon and a ground grid, so the airplane-fixed view
        /// shows the world turning around the airplane. Pixel-thick at any zoom.</summary>
        private void DrawWorld(float px)
        {
            var w = Widget;
            if (w.Current == AeroWidget.Scenario.Flare)
            {
                if (w.Condition != "final") return;
                float L = (float)w.Runway.LengthM, z0 = -L / 2f, z1 = L / 2f, aim = z0 + (float)FlyingGame.Sim.Practice.PracticeScenario.NumbersPastThresholdM;
                Line(new Vector3(0, 0, -20000), new Vector3(0, 0, 20000), new Color(1, 1, 1, 0.45f));                  // the ground
                for (int k = 0; k <= 4; k++) Line(new Vector3(0, -k * px, z0), new Vector3(0, -k * px, z1), Color.white);   // the runway: 5 px thick
                for (int k = -1; k <= 1; k++) Line(new Vector3(0, 0, z0 + k * px), new Vector3(0, 14 * px, z0 + k * px), Color.white);   // the threshold
                for (int k = 0; k <= 8; k++) Line(new Vector3(0, k * px * 0.5f, aim), new Vector3(0, k * px * 0.5f, aim + 45f), Color.white);   // the aim point (a 45 m bar)
                return;
            }
            if (!w.Show["horizon"]) return;
            var cam = w.Cam.transform.position;
            const float R = 15000f; Color hz = new(0.85f, 0.92f, 1f, 0.75f);
            Vector3 prev = cam + new Vector3(R, 0, 0);
            for (int i = 1; i <= 96; i++) { float a = i * Mathf.PI * 2f / 96f; var p = cam + new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R); Line(prev, p, hz); prev = p; }
            Vector3 ac = CoordinateMap.ToUnity(w.ShownState.Position);
            const float G = 250f, E = 4000f; Color gc = new(0.95f, 0.8f, 0.55f, 0.4f);
            float cx = Mathf.Round(ac.x / G) * G, cz = Mathf.Round(ac.z / G) * G;
            for (float d = -E; d <= E; d += G)
            {
                Line(new Vector3(cx + d, 0, cz - E), new Vector3(cx + d, 0, cz + E), gc);
                Line(new Vector3(cx - E, 0, cz + d), new Vector3(cx + E, 0, cz + d), gc);
            }
        }
        private static void Line(Vector3 a, Vector3 b, Color c) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }
    }
}

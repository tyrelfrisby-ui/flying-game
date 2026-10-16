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
        public AeroHost Widget;
        private Material _mat;
        public float WidthScale = 1f;
        /// <summary>The drag arrow's scale against lift (default ×3 so a small drag shows; owner 2026-10-12, the tailslide
        /// renders: "make the drag vector x1 not x3"). vectorStyle {"dragScale": 1…3}.</summary>
        public static double DragScale = 3.0;
        /// <summary>The pitching-moment arcs' names (owner 2026-10-12, the autorotation shot: "STABILITY → nose to the earth",
        /// "INERTIA → nose to the horizon"). vectorStyle {"aeroMomentLabel", "inertiaMomentLabel"}; null = AERO / INERTIA.</summary>
        public static string AeroMomentLabel, InertiaMomentLabel;
        /// <summary>The autorotation shot (owner 2026-10-12): both arcs the SAME size ("should be equal in size"), bigger heads,
        /// and STABILITY at the tail, INERTIA at the nose ("stability should be at the back near the stabilizers").
        /// vectorStyle {"momentArcsEqual", "momentArcHead", "momentArcsSwap"}.</summary>
        public static bool MomentArcsEqual, MomentArcsSwap; public static float MomentArcHead = 1f;
        /// <summary>Owner 2026-10-12 (the autorotation shot): every arc's head shows where the NOSE goes — an arc drawn at the tail
        /// then points the opposite way to the tail's own motion (a depiction choice, not a rotation arrow) — and no ft·lb values.
        /// vectorStyle {"momentArcNoseSense", "momentArcValues"}.</summary>
        public static bool MomentArcNoseSense, MomentArcValues = true;
        /// <summary>INERTIA SPLIT (Ty 2026-10-13: "how much of that nose up moment comes from gyroscopics vs structure weight"): the
        /// inertia arc drawn as two arcs end to end on its circle — STRUCTURE (the airframe's own inertia coupling) and GYROSCOPIC
        /// (the prop) — each swept in proportion to its share, labelled with its share in %. vectorStyle {"inertiaSplit"}.</summary>
        public static bool InertiaSplit;
        /// <summary>LEGEND (Ty 2026-10-13: "the labels jitter … just use color coded stationary labels with no connecting lines
        /// since the colors match the vectors … add a header to the labels and readouts that says flat spin dynamics"): the
        /// 3-D view's labels are a fixed block in the lower-left (inside the lower third), each in its vector's colour, under
        /// a header and the flight readouts; their numbers change at most 4× per simulated second. vectorStyle {"legend",
        /// "legendTitle"}.</summary>
        public static bool Legend;
        /// <summary>WING FORCES (Ty 2026-10-13: "add wing lift and drag forces on the spin dynamics shots to show the asymmetric
        /// lift/drag forces that drive the autorotation"): each wing half's own lift (⟂ its local wind) and drag (along it) —
        /// the sum of its strips' forces from the model — drawn from that wing, on one shared scale, so the difference
        /// between the wings reads at a glance. vectorStyle {"wingForces"}.</summary>
        public static bool WingForces;
        public static string LegendTitle = "FLAT SPIN DYNAMICS";
        /// <summary>Where the legend's two blocks sit: "bottom" (the corners, default) or "top" (the top corners — a square frame
        /// whose bottom carries charts); LegendScale shrinks the text. vectorStyle {"legendAnchor", "legendScale"}.</summary>
        public static string LegendAnchor = "bottom";
        public static float LegendScale = 1f;
        private readonly Dictionary<string, (string text, double t)> _legendHold = new();
        /// <summary>Where along each semispan the wing winds are taken (0.5 = mid; the rotation's ω × r grows toward the tips,
        /// owner 2026-10-12: "two relative wind vectors for each wing that are clearly different") and their length (×).
        /// vectorStyle {"wingWindSpan", "wingWindScale"}.</summary>
        public static double WingWindSpan = 0.5; public static float WingWindScale = 1f;
        /// <summary>Equal arcs' sweep (°) and their radius (× the default, out past the nose and tail so they don't cross the
        /// airframe; owner 2026-10-12: "smaller … should not overlap the airplane"). vectorStyle {"momentArcSweep", "momentArcRadius"}.</summary>
        public static float MomentArcSweepDeg = 110f, MomentArcRadius = 1f;
        /// <summary>SOLID 3D (owner 2026-10-12: "the airplane should obscure an arrow that is behind it"): the wing winds and the
        /// moment arcs as depth-tested meshes in the scene instead of lines painted over it. vectorStyle {"solid3D": true}.</summary>
        public static bool Solid3D;
        /// <summary>Wing winds always in the relative-wind cyan (owner 2026-10-12), "STALLED" in words only. vectorStyle {"wingWindCyan"}.</summary>
        public static bool WingWindCyan;
        /// <summary>A dot on each smoke exhaust (checking where the smoke leaves). vectorStyle {"smokeDebug": true}.</summary>
        public static bool SmokeDebug;
        /// <summary>All force arrows' length (×, one shared scale so their proportions hold). vectorStyle {"forceScale"}.</summary>
        public static float ForceScale = 1f;
        /// <summary>Thrust and drag drawn from the airplane's ends, true length, on its axis line (owner 2026-10-12: "if the
        /// thrust/drag vectors are too small and obscured by the aircraft put them in front of and behind the aircraft"):
        /// THRUST from just ahead of the spinner, DRAG from just behind the tail. vectorStyle {"forceEnds": true}.</summary>
        public static bool ForceEnds;
        /// <summary>The four forces' labels with their pounds (default) or words only (Glass's panel shows the numbers).
        /// vectorStyle {"forceValues": false}.</summary>
        public static bool ForceValues = true;
        /// <summary>A label just past an arrow's head, along it (it can't lie along its own short arrow).</summary>
        private void HeadLabel(WidgetControlsDisplay ui, Vector2 o, Vector2 t, string text, Color c)
        {
            Vector2 d = t - o; if (d.sqrMagnitude < 1f) d = Vector2.right; d.Normalize();
            Vector2 at = t + d * (10f * ui.TextScale);
            float tw = text.Length * 17f * ui.TextScale * 0.55f; Rect sc = Widget is AeroWidget aw2 && aw2.Display != null ? aw2.Display.SceneRect : new Rect(0, 0, 1e5f, 1e5f);
            tw *= 1.25f;   // (capitals run wide)
            bool room = d.x < 0 ? at.x - tw > sc.xMin + 8 : at.x + tw < sc.xMax - 8;
            if (room) { ui.PxText(at, text, 17f, c, d.x < 0 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft); return; }
            // No room past the head: centred just above the arrow, kept inside the picture.
            Vector2 mid = 0.5f * (o + t) + new Vector2(0f, -24f * ui.TextScale);
            mid.x = Mathf.Clamp(mid.x, sc.xMin + 8 + tw * 0.5f, sc.xMax - 8 - tw * 0.5f);
            ui.PxText(mid, text, 17f, c, TextAnchor.MiddleCenter);
        }
        private readonly Dictionary<string, WidgetArrow3D> _solidArrows = new();
        private readonly Dictionary<string, WidgetArc3D> _solidArcs = new();
        // The solid-3D shot's own labels (owner 2026-10-12, the autorotation intro: "each label should avoid the other labels
        // AND every arrow/arc, staying near its own arrow"): laid out together each frame, clear of every drawn arrow.
        private readonly List<(Vector2 a, Vector2 b)> _soloSegs = new();
        private readonly List<(Vector2 anchor, Vector2 pref, string text, Color c)> _soloLabels = new();
        private readonly List<Vector2> _soloBody = new();   // the airplane's outline on screen (nose, wingtip, tail, wingtip): labels keep off it
        private void SoloLabels(WidgetControlsDisplay ui, Rect scene)
        {
            if (Legend) { DrawLegend(ui, scene); return; }
            var placed = new List<Rect>();
            const float ts = 17f;
            foreach (var L in _soloLabels)
            {
                float w = ui.PxTextWidth(L.text, ts) + 14f, h = ts * ui.TextScale * 1.35f + 6f;   // (the drawn width, outline included)
                Rect best = default; float bestScore = float.MaxValue;
                Vector2 pref = L.pref.sqrMagnitude > 1e-4f ? L.pref.normalized : Vector2.down;
                for (int ri = 0; ri < 16; ri++)
                {
                    float rad = 18f * ui.TextScale + ri * 22f * ui.TextScale;
                    for (int k = 0; k < 24; k++)
                    {
                        float ang = k * Mathf.PI * 2f / 24f; Vector2 d = new(Mathf.Cos(ang), Mathf.Sin(ang));
                        // The rect's near edge sits rad from the anchor along d.
                        Vector2 c = L.anchor + d * rad + new Vector2(d.x * w * 0.5f, d.y * h * 0.5f);
                        var r = new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h);
                        if (r.xMin < scene.xMin + 6 || r.xMax > scene.xMax - 6 || r.yMin < scene.yMin + 6 || r.yMax > scene.yMax - 6) continue;
                        float score = rad + 40f * (1f - Vector2.Dot(d, pref));
                        foreach (var p in placed) if (p.Overlaps(r)) score += 5000f;
                        foreach (var sg in _soloSegs) if (SegHitsRect(sg.a, sg.b, r)) score += 3000f;
                        if (HitsBody(r)) score += 6000f;   // (worst: a label on the airplane)
                        if (score < bestScore) { bestScore = score; best = r; }
                    }
                }
                if (bestScore == float.MaxValue) continue;
                placed.Add(best);
                ui.PxText(best.center, L.text, ts, L.c, TextAnchor.MiddleCenter);
            }
        }
        /// <summary>The legend block: header, readouts, then the labels in their colours; texts held ≥ 0.25 s of sim time.</summary>
        private void DrawLegend(WidgetControlsDisplay ui, Rect scene)
        {
            var w = Widget; if (w == null || w.Ac == null) return;
            double simT = w.Shown?.SimTime ?? w.Review?.SimTimeNow ?? Time.time;
            string Hold(string key, string text)
            {
                if (_legendHold.TryGetValue(key, out var h) && System.Math.Abs(simT - h.t) < 0.25) return h.text;
                _legendHold[key] = (text, simT); return text;
            }
            static string Key(string t) { int i = 0; while (i < t.Length && !char.IsDigit(t[i])) i++; return t.Substring(0, i).Trim(); }
            var r = w.Read(); double om = w.ShownState.Rates.Length * 57.2958;
            var lines = new List<(string text, Color c, float size)>
            {
                (LegendTitle, Color.white, 17f),
                (Hold("r1", $"α {r.AlphaDeg:0}°   NOSE {System.Math.Abs(r.PitchDeg):0}° {(r.PitchDeg < 0 ? "LOW" : "HIGH")}"), new Color(0.9f, 0.9f, 0.9f), 14f),
                (Hold("r2", $"ROTATION {om:0}°/s   {(om > 1 ? 360 / om : 0):0.0} s/TURN"), new Color(0.9f, 0.9f, 0.9f), 14f),
                (Hold("r3", $"{r.Kias:0} KIAS   SINK {r.SinkFpm:#,0} fpm"), new Color(0.9f, 0.9f, 0.9f), 14f),
            };
            // Column 2: the labels; column 3 (WingForces): the YAW BALANCE — every yaw moment in the sense of the rotation
            // (+ drives it, − resists it); they sum to ≈ 0 at the steady rotation rate.
            var col2 = new List<(string text, Color c, float size)>(); var col2b = new List<(string text, Color c, float size)>();
            foreach (var L in _soloLabels) (L.text.Contains("WING") ? col2 : col2b).Add((Hold("l:" + Key(L.text), L.text), L.c, 14f));
            var col3 = new List<(string text, Color c, float size)>();
            var P = Current;
            if (WingForces && P != null)
            {
                double rs = w.ShownState.Rates.Z >= 0 ? 1 : -1, k = 0.7376 * rs;
                double other = P.MomYaw - P.WingYaw - P.FinYaw - P.FusYaw;
                Color grey = new(0.9f, 0.9f, 0.9f);
                col3.Add(("YAW BALANCE (ft·lb)", Color.white, 15f));
                col3.Add(("+ drives the rotation · − resists it", grey, 12f));
                foreach (var (nm, v, c) in new[] { ("WINGS", P.WingYaw, Lift), ("FIN", P.FinYaw, TailC), ("FUSELAGE", P.FusYaw, grey), ("STAB + DAMPING", other, grey), ("PROP P-FACTOR", P.HasProp ? -P.Prop.Thrust * P.Prop.PFactorY : 0, Thrust), ("PROP SLIPSTREAM", P.HasProp ? P.Prop.SlipYaw : 0, Thrust), ("PROP GYROSCOPIC", P.HasProp ? P.Prop.Gyro.Z : 0, Thrust), ("INERTIA COUPLING", P.InertiaYaw, Inertial) })
                    col3.Add((Hold("y:" + nm, $"{nm} {v * k:+#,0;-#,0}"), c, 14f));
                double sum = P.MomYaw + P.PropYaw + P.InertiaYaw;
                col3.Add((Hold("y:sum", $"NET {sum * k:+#,0;-#,0}"), Color.white, 14f));
            }
            float Hgt(List<(string text, Color c, float size)> col) { float t = 0; foreach (var l in col) t += l.size * LegendScale * ui.TextScale * 1.25f + 5f * LegendScale * ui.TextScale; return t; }
            float Wid(List<(string text, Color c, float size)> col) { float m = 0; foreach (var l in col) m = Mathf.Max(m, ui.PxTextWidth(l.text, l.size * LegendScale)); return m; }
            float bottom = scene.yMin + scene.height * 0.04f, x0 = scene.xMin + scene.width * 0.025f, colGap = 28f * ui.TextScale;
            float hMax = Mathf.Max(Mathf.Max(Hgt(lines), Hgt(col2b)), Mathf.Max(Hgt(col2), Hgt(col3)));
            void Col(List<(string text, Color c, float size)> col, float x)
            {
                float y = bottom + hMax;   // (y up: all three columns share the top line, the block ends 4 % above the bottom)
                foreach (var l in col)
                {
                    float lh = l.size * ui.TextScale * 1.25f;
                    y -= lh * 0.5f; ui.PxText(new Vector2(x, y), l.text, l.size, l.c, TextAnchor.MiddleLeft); y -= lh * 0.5f + 5f * ui.TextScale;
                }
            }
            // Two bottom corners, the middle left to the airplane: LEFT the header, readouts and the wings; RIGHT the moments
            // and the yaw balance (right-aligned to the frame's edge).
            var left = new List<(string text, Color c, float size)>(lines); left.AddRange(col2);
            var right = new List<(string text, Color c, float size)>(col2b); right.AddRange(col3);
            void Stack(List<(string text, Color c, float size)> col, float x, bool alignRight)
            {
                float y = LegendAnchor == "top" ? scene.yMax - scene.height * 0.03f : bottom + Hgt(col);
                foreach (var l in col)
                {
                    float sz = l.size * LegendScale, lh = sz * ui.TextScale * 1.25f;
                    y -= lh * 0.5f; ui.PxText(new Vector2(x, y), l.text, sz, l.c, alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft); y -= lh * 0.5f + 5f * LegendScale * ui.TextScale;
                }
            }
            Stack(left, x0, false);
            Stack(right, scene.xMax - scene.width * 0.025f, true);
        }

        private bool InHull(Vector2 p)
        {
            // Inside the (convex, counter-clockwise) outline of the airplane on screen.
            int n = _soloBody.Count; if (n < 3) return false;
            for (int i = 0; i < n; i++) { Vector2 a = _soloBody[i], b = _soloBody[(i + 1) % n]; if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < 0) return false; }
            return true;
        }
        /// <summary>A label's box touches the airplane: any of its 9 sample points inside the outline, or an outline corner inside it.</summary>
        private bool HitsBody(Rect r)
        {
            if (_soloBody.Count < 3) return false;
            for (int i = 0; i <= 2; i++) for (int j = 0; j <= 2; j++) if (InHull(new Vector2(r.xMin + r.width * i * 0.5f, r.yMin + r.height * j * 0.5f))) return true;
            foreach (var q in _soloBody) if (r.Contains(q)) return true;
            return false;
        }
        /// <summary>The convex hull (Andrew's monotone chain), counter-clockwise.</summary>
        private static void Hull(List<Vector2> pts)
        {
            if (pts.Count < 3) return;
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            static float Cr(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            foreach (var p in pts) { while (h.Count >= 2 && Cr(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1); h.Add(p); }
            int lo = h.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--) { var p = pts[i]; while (h.Count >= lo && Cr(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1); h.Add(p); }
            h.RemoveAt(h.Count - 1);
            pts.Clear(); pts.AddRange(h);
        }
        private static bool SegHitsRect(Vector2 a, Vector2 b, Rect r)
        {
            r = new Rect(r.xMin - 4, r.yMin - 4, r.width + 8, r.height + 8);
            if (r.Contains(a) || r.Contains(b)) return true;
            for (int i = 0; i <= 8; i++) if (r.Contains(Vector2.Lerp(a, b, i / 8f))) return true;
            return false;
        }

        private WidgetArrow3D SolidArrow(string id, Color c) { if (!_solidArrows.TryGetValue(id, out var a)) _solidArrows[id] = a = new WidgetArrow3D(id + "3D", transform, (Widget as AeroWidget)?.VisualLayer ?? 0, c); return a; }
        private WidgetArc3D SolidArc(string id, Color c) { if (!_solidArcs.TryGetValue(id, out var a)) _solidArcs[id] = a = new WidgetArc3D(id + "3D", transform, (Widget as AeroWidget)?.VisualLayer ?? 0, c); return a; }
        public float SmoothingMs = 130f;

        public delegate bool ScrFn(Vector3 world, out Vector2 px);
        public static readonly Color Lift = new(0.25f, 1f, 0.35f), Drag = new(1f, 0.3f, 0.25f), Weight = new(1f, 0.9f, 0.2f),
            Thrust = new(0.35f, 0.6f, 1f), PathColor = new(1f, 0.85f, 0.2f), Wind = new(0.3f, 0.95f, 1f), Total = new(1f, 1f, 1f), Axis = new(1f, 0.3f, 1f), Wheel = new(1f, 0.6f, 0.15f),
            Inertial = new(1f, 0.82f, 0.2f), TailC = new(0.75f, 0.55f, 1f), TailWindC = new(0.55f, 0.85f, 1f);

        // World lines (grid, runway) are depth-tested (owner 2026-10-12: the grid showed over the airplane).
        private void Start()
        {
            _mat = new Material(Shader.Find("FlyingGame/HudLine")) { hideFlags = HideFlags.HideAndDontSave, color = Color.white };
            _mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        }

        // ================= the physics picture of one frame (body axes) =================
        public sealed class Picture
        {
            public readonly Dictionary<string, (Vec3 from, Vec3 vec)> V = new();   // body-frame origin (config coords) and vector
            public double LiftN, DragN, BallDeg;
            public double MomYaw, FinN, FinYaw; public bool HasFin;
            public Vec3 WLiftL, WLiftR, WDragL, WDragR, WPosL, WPosR, FinF, FinPos, ThrustAt; public double WingYaw, FusYaw, InertiaYaw, PropYaw; public FlyingGame.Core.PropModel.Parts Prop; public bool HasProp;
            public double AeroG, InertialG, TailN, MomAero, MomInertia, MomGyro, WingAlphaL, WingAlphaR, TailAlphaL, TailAlphaR, Eps, EtaL, EtaR, TailSpdL, TailSpdR, VInf;
            public bool StallL, StallR, HasTail;
            /// <summary>FOUR FORCES (2026-10-11): the balance along and across the flight path, from the same forces. Along: thrust's
            /// part along the path, drag, and weight's part along it (W·sinγ: + = pulling back, a climb). Across: lift (+ thrust's
            /// small part), weight's part opposing it (W·cosγ·cosφ). AccelAlong (m/s²) and PathRate (rad/s, + = curving toward
            /// the lift) are the EXACT net results (every force summed), not the bars' differences.</summary>
            public double ThrustAlongN, ThrustPerpN, WeightN, WSinGammaN, WPerpN, GammaDeg, AccelAlong, PathRate;
        }

        /// <summary>Everything the frame's arrows say, from a state + its force samples (live or a history frame).</summary>
        public Picture Compute(RigidBodyState s, IReadOnlyList<ForceSample> forces, double critAlphaDeg, double critAlphaInvDeg = -1)
        {
            var w = Widget; var cfg = w.Config; var mp = w.Ac.MassProperties;
            Vec3 cg = cfg.Mass.CgVec();
            double W = mp.MassKg * 9.81;
            var pic = new Picture();
            Vec3 windB = s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
            Vec3 vAir = s.Velocity - windB; pic.VInf = vAir.Length;

            Vec3 finF = Vec3.Zero, finPos = Vec3.Zero; double finW = 0, nAero = 0, nFin = 0;
            Vec3 aero = Vec3.Zero, nonGrav = Vec3.Zero, thrust = Vec3.Zero, lift = Vec3.Zero, drag = Vec3.Zero, tailF = Vec3.Zero, tailPos = Vec3.Zero, thrustPos = Vec3.Zero;
            double mAero = 0, tailW = 0, mGyro = 0, wwL = 0, wwR = 0;
            Vec3 wpL = Vec3.Zero, wpR = Vec3.Zero;
            var tailL = new List<ForceSample>(); var tailR = new List<ForceSample>();
            if (forces != null)
                foreach (var f in forces)
                {
                    switch (f.Kind)
                    {
                        case "tailflow": (f.PosBody.Y < 0 ? tailL : tailR).Add(f); continue;
                        case "weight": continue;
                        case "gyro": mGyro += f.MomentBody.Y; continue;   // the prop's gyroscopic pitch (already inside the thrust sample: a breakdown)
                        case "lift": case "drag": case "fuselage": case "spoiler": case "moment":
                        {
                            aero += f.ForceBody;
                            Vec3 r = f.PosBody - cg;
                            mAero += (r.Z * f.ForceBody.X - r.X * f.ForceBody.Z) + f.MomentBody.Y;
                            double nz = r.X * f.ForceBody.Y - r.Y * f.ForceBody.X + f.MomentBody.Z;   // yaw moment about the CG (nose-right +)
                            nAero += nz;
                            if (f.Kind == "fuselage") pic.FusYaw += nz;
                            // The vertical tail (fin + rudder): aft, force mostly sideways.
                            if ((f.Kind == "lift" || f.Kind == "drag") && f.PosBody.X < cg.X - 0.35 * w.LengthM && System.Math.Abs(f.ForceBody.Y) > System.Math.Abs(f.ForceBody.Z))
                            { finF += f.ForceBody; double a2 = System.Math.Abs(f.ForceBody.Y) + 1e-6; finPos += f.PosBody * a2; finW += a2; nFin += nz; }
                            if (f.Kind == "lift") lift += f.ForceBody; else if (f.Kind == "drag") drag += f.ForceBody;
                            // Each wing half (ahead of the tail, out past the fuselage): its own lift and drag.
                            if ((f.Kind == "lift" || f.Kind == "drag") && f.PosBody.X > cg.X - 0.35 * w.LengthM && System.Math.Abs(f.PosBody.Y) > 0.3 && System.Math.Abs(f.PosBody.Z - cg.Z) < 0.6)
                            {
                                bool left = f.PosBody.Y < 0; double fm = f.ForceBody.Length + 1e-6;
                                if (f.Kind == "lift") { if (left) pic.WLiftL += f.ForceBody; else pic.WLiftR += f.ForceBody; }
                                else { if (left) pic.WDragL += f.ForceBody; else pic.WDragR += f.ForceBody; }
                                if (left) { wpL += f.PosBody * fm; wwL += fm; } else { wpR += f.PosBody * fm; wwR += fm; }
                                pic.WingYaw += nz;
                            }
                            // The horizontal tail: aft, and its force mostly vertical (the fin's is sideways).
                            if ((f.Kind == "lift" || f.Kind == "drag") && f.PosBody.X < cg.X - 0.35 * w.LengthM && System.Math.Abs(f.ForceBody.Z) >= System.Math.Abs(f.ForceBody.Y))
                            { tailF += f.ForceBody; double a = System.Math.Abs(f.ForceBody.Z) + 1e-6; tailPos += f.PosBody * a; tailW += a; }
                            break;
                        }
                        case "thrust": thrust += f.ForceBody; thrustPos = f.PosBody; pic.PropYaw += (f.PosBody - cg).X * f.ForceBody.Y - (f.PosBody - cg).Y * f.ForceBody.X + f.MomentBody.Z; break;
                    }
                    if (f.Kind != "moment") nonGrav += f.ForceBody;
                }
            if (wwL > 0) pic.WPosL = wpL * (1.0 / wwL); if (wwR > 0) pic.WPosR = wpR * (1.0 / wwR);
            // INERTIAL = m(g − a) = −(every non-gravity force); TOTAL AERO from the neutral point.
            Vec3 inertial = -1.0 * nonGrav;
            // Lift and drag along the airflow; the slip ball (it sits opposite the lateral specific force).
            if (pic.VInf > 1)
            {
                Vec3 vh = vAir * (1.0 / pic.VInf), liftDir = -1.0 * Vec3.Cross(vh, new Vec3(0, 1, 0));
                double ld = liftDir.Length; if (ld > 1e-6) liftDir = liftDir * (1.0 / ld);
                pic.DragN = -(aero.X * vh.X + aero.Y * vh.Y + aero.Z * vh.Z); pic.LiftN = aero.X * liftDir.X + aero.Y * liftDir.Y + aero.Z * liftDir.Z;
                // The four forces: lift ⟂ and drag ∥ the relative wind (the TOTAL aero, tail and fuselage included), thrust, weight.
                Vec3 wB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, W));
                static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
                pic.WeightN = W; pic.ThrustAlongN = Dot(thrust, vh); pic.ThrustPerpN = Dot(thrust, liftDir);
                pic.WSinGammaN = -Dot(wB, vh); pic.WPerpN = -Dot(wB, liftDir);
                Vec3 vW = s.Attitude.Rotate(vAir);
                pic.GammaDeg = System.Math.Asin(System.Math.Clamp(-vW.Z / pic.VInf, -1, 1)) * 57.2958;
                Vec3 net = nonGrav + wB;
                pic.AccelAlong = Dot(net, vh) / mp.MassKg; pic.PathRate = Dot(net, liftDir) / (mp.MassKg * pic.VInf);
                pic.V["lift4"] = (cg, liftDir * (pic.LiftN / W)); pic.V["drag4"] = (cg, vh * (-DragScale * pic.DragN / W));
            }
            Vec3 sf = nonGrav * (1.0 / mp.MassKg);
            pic.BallDeg = -System.Math.Atan2(sf.Y, System.Math.Max(0.5, -sf.Z)) * 57.2958;
            pic.InertialG = inertial.Length / W; pic.AeroG = aero.Length / W;
            pic.V["inertial"] = (cg, inertial * (1.0 / W));
            pic.V["total"] = (w.NeutralPointBody(), aero * (1.0 / W));
            pic.V["weight"] = (cg, s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1)));
            pic.V["wind"] = (cg, vAir * (-1.0 / System.Math.Max(1, pic.VInf)));   // RELATIVE wind: the air coming at the airplane (−velocity)
            pic.V["lift"] = (cg, lift * (1.0 / W)); pic.V["drag"] = (cg, drag * (DragScale / W));
            if (thrust.Length > 1) pic.V["thrust"] = (thrustPos, thrust * (DragScale / W));   // (thrust and drag share a scale)
            if (ForceEnds)
            {
                double tailX = double.MaxValue; foreach (var sf2 in cfg.Surfaces) foreach (var st2 in sf2.Strips) tailX = System.Math.Min(tailX, st2.Pos[0] - 0.75 * st2.Chord);
                double noseX = (w is AeroWidget mw && !double.IsNaN(mw.ModelNoseFromCg) ? cg.X + mw.ModelNoseFromCg : thrustPos.X) + 0.3;   // (just ahead of the spinner)
                if (thrust.Length > 1) pic.V["thrust"] = (new Vec3(noseX, 0, cg.Z), thrust * (DragScale / W));
                if (pic.V.TryGetValue("drag4", out var d4)) pic.V["drag4"] = (new Vec3(tailX - 0.3, 0, cg.Z), d4.vec);
                pic.V["drag"] = (new Vec3(tailX - 0.3, 0, cg.Z), pic.V["drag"].vec);
                // LIFT from above the airframe, WEIGHT from below it (Ty 2026-10-14: "the lift and weight vectors starting above
                // and below the aircraft like the thrust and drag vectors do so no vector overlaps the airplane"). Both stay on
                // the CG's vertical line, so the pair still reads against each other.
                if (w is AeroWidget vw && !double.IsNaN(vw.ModelTopFromCg))
                {
                    Vec3 above = new(cg.X, 0, cg.Z + vw.ModelTopFromCg - 0.3), below = new(cg.X, 0, cg.Z + vw.ModelBellyFromCg + 0.3);
                    if (pic.V.TryGetValue("lift4", out var l4)) pic.V["lift4"] = (above, l4.vec);
                    pic.V["lift"] = (above, pic.V["lift"].vec);
                    pic.V["weight"] = (below, pic.V["weight"].vec);
                }
            }
            if (tailW > 0) { pic.HasTail = true; pic.TailN = tailF.Z; pic.V["tail"] = (tailPos * (1.0 / tailW), new Vec3(0, 0, tailF.Z * 4.0 / W)); }
            // Moments about the CG (pitch, nose-up +): aero, and the inertia coupling −(ω × Iω)_y.
            Vec3 om = s.Rates;
            double hx = mp.Ixx * om.X - mp.Ixz * om.Z, hz = mp.Izz * om.Z - mp.Ixz * om.X;
            pic.MomYaw = nAero; pic.FinYaw = nFin;
            if (finW > 0) { pic.FinF = finF; pic.FinPos = finPos * (1.0 / finW); }
            if (finW > 0) { pic.HasFin = true; pic.FinN = finF.Y; pic.V["fin"] = (finPos * (1.0 / finW), new Vec3(0, finF.Y * 12.0 / W, 0)); }   // ×12: a fin force is small against the weight
            pic.MomAero = mAero; pic.MomInertia = -(om.Z * hx - om.X * hz); pic.MomGyro = mGyro;
            { double hy = mp.Iyy * om.Y; pic.InertiaYaw = -(om.X * hy - om.Y * hx); }   // −(ω × Iω)_z: the inertia coupling in yaw
            // The prop's parts, from the same model the sim flew (PropModel), at this frame's state and throttle.
            if (cfg.Propulsion != null && cfg.Propulsion.PropDiameterM > 0 && cfg.Engines.Count == 0)
            {
                double thr01 = w.Shown?.AcThrottle ?? w.Ac.Throttle01, alt = -(s.Position.Z - (w.Shown?.AltShift ?? 0));
                FlyingGame.Core.PropModel.Compute(cfg.Propulsion, thr01, s.Velocity, s.Rates, Atmosphere.DensityAtAltitude(alt), out pic.Prop);
                pic.HasProp = true; pic.ThrustAt = thrustPos;
            }
            // Wing winds at mid-semispan (ω × r).
            foreach (int side in new[] { -1, 1 })
            {
                Vec3 p = w.WingMidSpan(side); p = new Vec3(p.X, p.Y * WingWindSpan / 0.5, p.Z);
                Vec3 v = vAir + Vec3.Cross(s.Rates, p - cg);
                double a = System.Math.Atan2(v.Z, System.Math.Max(0.1, v.X)) * 57.2958;
                pic.V[side < 0 ? "wingL" : "wingR"] = (p, v * (-1.0 / System.Math.Max(1, pic.VInf)));   // the local relative wind: −(the station's velocity through the air)
                // Either way: an inverted wing stalls at negative α — and on a cambered wing it gets there
                // sooner, so each branch is judged by its own critical angle. (2026-10-15)
                bool st = System.Math.Abs(a) > (a < 0 && critAlphaInvDeg > 0 ? critAlphaInvDeg : critAlphaDeg);
                if (side < 0) { pic.WingAlphaL = a; pic.StallL = st; } else { pic.WingAlphaR = a; pic.StallR = st; }
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
            double flapsNow = w.Shown != null ? w.Shown.FlapsActual : w.Ac.FlapFraction;
            double crit = w.CriticalAlphaDeg(flapsNow), critInv = w.CriticalAlphaDeg(flapsNow, true);
            if (w.Shown == null)
            {
                _reviewIdx = -1;
                Current = Compute(w.Ac.State, w.Ac.LastForces, crit, critInv);
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
                var p = Compute(f.State, f.Forces, crit, critInv);
                Filter(p, 1.0 / WidgetHistory.Hz, _filt);
                if (i == idx) Current = p;
            }
        }

        // ================= drawing (into the compositor, frame pixels) =================
        private bool On(string k) => Widget.Show.TryGetValue(k, out bool b) && b;

        /// <summary>Called by the compositor each frame after the camera is placed: arrows, arcs and labels for the frame shown.</summary>
        /// <summary>Screen → frame offset of the projection: the widget's scene texture starts at the picture rect; the game's
        /// camera already projects in screen pixels (zero).</summary>
        private Vector2 _off;
        private WidgetArrow3D _path3D, _wind3D;
        public float Ui = 1f;   // text / line scale (the game's screens are bigger than the 1080 frame)
        public void Build(WidgetControlsDisplay ui, Rect scene) => Build(ui, scene, scene.position);
        public void Build(WidgetControlsDisplay ui, Rect scene, Vector2 projectionOffset)
        {
            _off = projectionOffset;
            Advance();
            var w = Widget; if (Current == null) return;
            bool vec = w.DisplayMode == "classic" || On("vectors");
            var s = w.ShownState; var cam = w.Cam; Vec3 cg = w.Config.Mass.CgVec();
            Vector3 World(Vec3 bodyPos) => CoordinateMap.ToUnity(s.Position + s.Attitude.Rotate(bodyPos - cg));
            Vector3 Dir(Vec3 body) => CoordinateMap.ToUnity(s.Attitude.Rotate(body));
            bool Scr(Vector3 wp, out Vector2 px) { var p = cam.WorldToScreenPoint(wp); px = new Vector2(_off.x + p.x, _off.y + p.y); return p.z > 0.05f; }
            Vector3 cgU = World(cg);
            float pxM = PixelM(cgU);
            Scr(cgU, out var cgScr);
            float gPx = 190f * ForceScale * (w.IsWidgetHost ? 1f : Ui);   // one g (one weight) on the screen
            // FOUR FORCES: lift and weight must both fit, whole, above and below the CG (the game's free area is often short).
            if (On("fourForces") && ForceScale <= 1f) gPx = Mathf.Max(50f * Ui, Mathf.Min(gPx, 0.82f * Mathf.Min(scene.yMax - cgScr.y, cgScr.y - scene.yMin)));
            float lw = 6f * WidthScale * (scene.height / AeroWidget.FrameSize + 0.25f) / 1.25f * (Widget.IsWidgetHost ? 1f : Ui * 0.8f);

            // Readout (classic) and the airplane-scale tag.
            var rd = w.Read();
            if (w is AeroWidget aw && w.DisplayMode == "classic" && On("readout"))
            {
                string extra = aw.Current == AeroWidget.Scenario.Cruise ? $"ALT {rd.HeightFt:F0} ft  VS {-rd.SinkFpm:+0;-0} fpm  PWR {aw.Controls.Throttle01 * 100:F0}%"
                    : aw.Current == AeroWidget.Scenario.Spin ? $"YAW {rd.YawRateDps:+0;-0}°/s  ROLL {rd.RollRateDps:+0;-0}°/s"
                    : $"SINK {rd.SinkFpm:F0} fpm  HT {rd.HeightFt:F0} ft{(rd.OnGround ? "  ON THE WHEELS" : "")}";
                string rev = w.Paused && On("review") ? $"REVIEW {w.Review.OffsetMs / 1000.0:+0.0;-0.0;0.0} s\n" : "";
                float ty = scene.yMax - 24 * ui.TextScale;
                foreach (var line in (rev + $"{w.AircraftName}   {rd.Kias:F0} KIAS   α {rd.AlphaDeg:F1}°   PITCH {rd.PitchDeg:+0;-0}°\n" + extra).Split('\n'))
                { ui.PxText(new Vector2(scene.x + 22, ty), line, 19, Color.white, TextAnchor.MiddleLeft); ty -= 24 * ui.TextScale; }
            }
            // CAPTION (2026-10-11, the Talk Flying renders): a big phase tag ("CLIMB +700 FPM"), top centre — classic display only.
            if (w is AeroWidget aw2 && aw2.DisplayMode == "classic" && !string.IsNullOrEmpty(aw2.Caption))
                ui.PxText(new Vector2(scene.center.x, scene.yMin + 40 * ui.TextScale), aw2.Caption, 28, new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleCenter);
            // TAG (2026-10-12): a word above the airplane — a wingspan-and-a-bit above the CG, straight up on screen.
            if (w is AeroWidget aw3 && !string.IsNullOrEmpty(aw3.Tag) && Scr(cgU + Vector3.up * (0.25f * aw3.SpanM + 1.5f), out var tagAt))
            {
                float m = 40f * ui.TextScale;   // kept inside the picture
                tagAt = new Vector2(Mathf.Clamp(tagAt.x, scene.xMin + 3 * m, scene.xMax - 3 * m), Mathf.Clamp(tagAt.y, scene.yMin + m, scene.yMax - m));
                ui.PxText(tagAt, aw3.Tag, 34f, aw3.TagColor, TextAnchor.MiddleCenter);
            }
            if (w.VisualScale > 1.01f && Scr(cgU - cam.transform.up * pxM * 40f, out var sp)) _labels.Request("scale", sp, Vector2.zero, $"AIRPLANE ×{w.VisualScale:0}", new Color(1, 1, 1, 0.85f), 0);
            if (!vec) { _labels.Place(ui, LabelScene(w, scene, ui), cgScr); return; }

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
            float windPx = 150f * (w.IsWidgetHost ? 1f : Ui);
            float wingWindPx = windPx * WingWindScale;
            foreach (var (k, a, st, nm) in new[] { ("wingL", P.WingAlphaL, P.StallL, "L WING"), ("wingR", P.WingAlphaR, P.StallR, "R WING") })
            {
                if (!On("wingWind") || !F.ContainsKey(k)) { foreach (var key in new[] { k, k + "S" }) if (_solidArrows.TryGetValue(key, out var hh)) hh.Hide(); continue; }   // (both colours: a cyan wing arrow was left standing)
                Color c = st && !WingWindCyan ? Drag : Wind;
                if (Solid3D)
                {
                    var fv = F[k]; Vector3 o3 = World(fv.from), d3 = Dir(fv.vec) * (wingWindPx * pxM);
                    string sk = k + (c == Drag ? "S" : "");
                    SolidArrow(k + "S", Drag).Hide(); SolidArrow(k, Wind).Hide();
                    SolidArrow(sk, c).Place(o3 - d3, o3, Mathf.Max(0.04f, 0.012f * (float)w.LengthM));
                    // Its label beside it, out on its own wing's side (owner 2026-10-12: which arrow is which).
                    if (Scr(o3 - d3 * 0.5f, out var midS) && Scr(o3, out var rootS) && Scr(o3 + Dir(new Vec3(0, k == "wingL" ? -1 : 1, 0)) * 2f, out var outS))
                    {
                        _soloSegs.Add((rootS, Scr(o3 - d3, out var tailS) ? tailS : midS));
                        _soloLabels.Add((midS, (outS - rootS), $"{nm} α {a:0}°{(st ? " STALLED" : "")}", c));
                    }
                    continue;
                }
                else Arrow(k, c, wingWindPx, true);
                _labels.Request(k, Tip(k, wingWindPx, true), Away(Origin(k), Tip(k, wingWindPx, true)), new[] { a }, v => $"{nm} α {v[0]:0}°{(st ? " STALLED" : "")}", c, 2);
            }
            // Each wing's lift and drag (WingForces), solid, from the wing's centre of force; one g = 1.4 airplane lengths.
            foreach (var (side, lf, dg, pos, nm) in new[] { ("L", P.WLiftL, P.WDragL, P.WPosL, "L WING"), ("R", P.WLiftR, P.WDragR, P.WPosR, "R WING") })
            {
                foreach (var (kind, f, c) in new[] { ("LIFT", lf, Lift), ("DRAG", dg, Drag) })
                {
                    string id = "wf" + side + kind;
                    if (!WingForces || !Solid3D || f.Length < 1) { if (_solidArrows.TryGetValue(id, out var hh)) hh.Hide(); continue; }
                    double W0 = w.Ac.MassProperties.MassKg * 9.81;
                    Vector3 o3 = World(pos), d3 = Dir(f * (1.0 / f.Length)) * (float)(f.Length / W0 * 1.4 * w.LengthM);
                    SolidArrow(id, c).Place(o3, o3 + d3, Mathf.Max(0.05f, 0.014f * (float)w.LengthM));
                    if (Scr(o3 + d3, out var tipS) && Scr(o3, out var rootS))
                    {
                        _soloSegs.Add((rootS, tipS));
                        _soloLabels.Add((tipS, tipS - rootS, $"{nm} {kind} {f.Length * 0.2248:#,0} lb", c));
                    }
                }
            }
            // The fin's side force (Ty 2026-10-13: "the vertical tail damping force … the driving force and the resistive force
            // that lead to equilibrium in rotation rate"): its own arrow; the label says whether its yaw moment resists the
            // rotation or drives it (the sign of N_fin · r).
            {
                var ff = P.FinF;
                if (!WingForces || !Solid3D || ff.Length < 1 || !P.HasFin) { if (_solidArrows.TryGetValue("wfFin", out var hh)) hh.Hide(); }
                else
                {
                    double W0 = w.Ac.MassProperties.MassKg * 9.81;
                    Vector3 o3 = World(P.FinPos), d3 = Dir(ff * (1.0 / ff.Length)) * (float)(ff.Length / W0 * 1.4 * w.LengthM);
                    SolidArrow("wfFin", TailC).Place(o3, o3 + d3, Mathf.Max(0.05f, 0.014f * (float)w.LengthM));
                    bool resists = P.FinYaw * s.Rates.Z < 0;
                    if (Scr(o3 + d3, out var tipS) && Scr(o3, out var rootS))
                    {
                        _soloSegs.Add((rootS, tipS));
                        _soloLabels.Add((tipS, tipS - rootS, $"FIN {ff.Length * 0.2248:#,0} lb — {(resists ? "RESISTS" : "DRIVES")} ROTATION", TailC));
                    }
                }
            }
            // The PROP's forces (Ty 2026-10-13): the THRUST from where it acts — off the hub toward the descending blade (P-factor) —
            // and the spiral SLIPSTREAM's push on the fin (its yaw moment ÷ the fin's arm). Torque and gyroscopics are moments: the
            // yaw balance lists them.
            {
                bool showP = WingForces && Solid3D && P.HasProp && P.Prop.Thrust > 1;
                if (!showP) { foreach (var id in new[] { "pfThrust", "pfSlip" }) if (_solidArrows.TryGetValue(id, out var hh)) hh.Hide(); }
                else
                {
                    double W0 = w.Ac.MassProperties.MassKg * 9.81;
                    Vec3 at = P.ThrustAt + new Vec3(0, P.Prop.PFactorY, P.Prop.PFactorZ);
                    Vector3 o3 = World(at), d3 = Dir(new Vec3(1, 0, 0)) * (float)(P.Prop.Thrust / W0 * 1.4 * w.LengthM);
                    SolidArrow("pfThrust", Thrust).Place(o3, o3 + d3, Mathf.Max(0.05f, 0.014f * (float)w.LengthM));
                    double off = System.Math.Sqrt(P.Prop.PFactorY * P.Prop.PFactorY + P.Prop.PFactorZ * P.Prop.PFactorZ);
                    if (Scr(o3 + d3, out var tS) && Scr(o3, out var rS)) { _soloSegs.Add((rS, tS)); _soloLabels.Add((tS, tS - rS, $"PROP THRUST {P.Prop.Thrust * 0.2248:#,0} lb · {off * 100:0} cm {(P.Prop.PFactorY >= 0 ? "R" : "L")} OF HUB (P-FACTOR)", Thrust)); }
                    if (P.HasFin)
                    {
                        double arm = P.FinPos.X - cg.X; double fy = System.Math.Abs(arm) > 0.3 ? P.Prop.SlipYaw / arm : 0;
                        Vec3 f = new(0, fy, 0);
                        if (System.Math.Abs(fy) > 1)
                        {
                            Vector3 f3 = World(P.FinPos + new Vec3(0, 0, -0.25)), fd = Dir(new Vec3(0, System.Math.Sign(fy), 0)) * (float)(System.Math.Abs(fy) / W0 * 1.4 * w.LengthM);
                            SolidArrow("pfSlip", Thrust).Place(f3, f3 + fd, Mathf.Max(0.05f, 0.014f * (float)w.LengthM));
                            if (Scr(f3 + fd, out var sS) && Scr(f3, out var sR)) { _soloSegs.Add((sR, sS)); _soloLabels.Add((sS, sS - sR, $"PROP SLIPSTREAM ON THE FIN {System.Math.Abs(fy) * 0.2248:#,0} lb", Thrust)); }
                        }
                        else if (_solidArrows.TryGetValue("pfSlip", out var hh)) hh.Hide();
                    }
                }
            }
            // FLIGHT PATH (2026-10-11, Talk Flying W12) with the RELATIVE WIND, equal and opposite (owner 2026-10-12): one line through
            // the CG, the same length, head on — the flight path out from the CG along the velocity, the relative wind coming back
            // down the same line to meet it. Labels only. Without the flight path the relative wind arrives at the CG as before.
            bool pathOn = On("flightPath") && F.ContainsKey("wind");
            if (On("wind") && F.ContainsKey("wind") && !pathOn) { Arrow("wind", Wind, windPx, true); _labels.Request("wind", Tip("wind", windPx, true), Away(Origin("wind"), Tip("wind", windPx, true)), new[] { rd.AlphaDeg }, v => $"RELATIVE WIND α {v[0]:0.0}°", Wind, 3, 0.1); }
            if (pathOn)
            {
                // The RAW velocity through the air for this frame, not the display-smoothed one (owner 2026-10-12): through a
                // pull-up the angle between these arrows and the fuselage is the sim's true α at that instant.
                var fw = P.V.TryGetValue("wind", out var rawW) ? rawW : F["wind"]; float len = windPx * 1.3f;
                Vec3 fwd = fw.vec * -1.0;                                     // unit, body axes: the way the airplane is going
                // Still on the CG's line, but starting just ahead of the nose (the most forward wheel + 1.5 m), clear of the cowling.
                double noseX = 0; foreach (var g in w.Config.Gear) noseX = System.Math.Max(noseX, g.PosVec().X - cg.X);
                Vec3 start = fw.from + fwd * (noseX + 1.5);
                F["path"] = (start, fwd);
                F["windHead"] = (start + fwd * (len * pxM), fw.vec);          // arrives at the path's tip from a path-length beyond it
                if (w is AeroWidget ow && ow.View == "orbit")
                {
                    // ORBIT: solid arrows fixed to the airplane's flight path, labels facing the camera (beside each arrow on
                    // screen: the flight path's below, the relative wind's above — apart even nose-on).
                    float lenM = 0.55f * ow.LengthM, rM = 0.035f * lenM;   // sized to the airplane: the camera circles at a fixed distance
                    Vector3 pT = World(start), pH = World(start + fwd * lenM), wT = World(start + fwd * (2.0 * lenM));
                    _path3D ??= new WidgetArrow3D("FlightPath3D", transform, ow.VisualLayer, PathColor);
                    _wind3D ??= new WidgetArrow3D("RelWind3D", transform, ow.VisualLayer, Wind);
                    _path3D.Place(pT, pH, rM);
                    if (On("wind")) _wind3D.Place(wT, pH, rM); else _wind3D.Hide();
                    // Beside each arrow, at right angles to it on screen (on opposite sides), so neither label lies on an arrow
                    // whichever way the line points; nose-on (the line a dot) they sit below and above.
                    if (Scr(pT, out var sT) && Scr(wT, out var sW) && Scr((pT + pH) * 0.5f, out var pm) && Scr((wT + pH) * 0.5f, out var wm))
                    {
                        Vector2 ln = sW - sT; float lnL = ln.magnitude;
                        Vector2 perp = lnL > 20f ? new Vector2(-ln.y, ln.x) / lnL : Vector2.up;
                        if (perp.y < 0) perp = -perp;                                  // + = the upper side
                        float g3 = (lnL > 20f ? 22f : 30f) * ui.TextScale + Mathf.Abs(perp.x) * 60f * ui.TextScale;   // wider when the line is steep (the text is wide)
                        ui.PxText(pm - perp * g3, "FLIGHT PATH", 17f, PathColor, TextAnchor.MiddleCenter);
                        if (On("wind")) ui.PxText(wm + perp * g3, "RELATIVE WIND", 17f, Wind, TextAnchor.MiddleCenter);
                    }
                    goto pathDone;
                }
                _path3D?.Hide(); _wind3D?.Hide();
                Arrow("path", PathColor, len);
                if (On("wind")) Arrow("windHead", Wind, len, true);
                Vector2 c0 = Origin("path"), mid = (c0 + Tip("path", len)) * 0.5f, far = Origin("windHead");
                // Each label right beside its own arrow (the placer parked both high on long leaders, and swapped them in the
                // descent): the relative wind's above the line, the flight path's below. Screen y runs down.
                Vector2 along = Away(c0, Tip("path", len)), up = new Vector2(-along.y, along.x);
                if (up.y > 0) up = -up;
                float gap = 26f * ui.TextScale;
                ui.PxText(mid - up * gap, "FLIGHT PATH", 17f, PathColor, TextAnchor.MiddleCenter);
                if (On("wind")) ui.PxText((far + Tip("windHead", len, true)) * 0.5f + up * gap, "RELATIVE WIND", 17f, Wind, TextAnchor.MiddleCenter);
                pathDone:;
            }
            else { _path3D?.Hide(); _wind3D?.Hide(); }
            // Tail winds (length = the local speed: prop blast lengthens, blanketing shortens).
            foreach (var (k, a, eta, nm) in new[] { ("tailWindL", P.TailAlphaL, P.EtaL, "L STAB"), ("tailWindR", P.TailAlphaR, P.EtaR, "R STAB") })
            {
                if (!On("tailWind") || !F.ContainsKey(k)) continue;
                Arrow(k, TailWindC, windPx * 0.8f, true);
                _labels.Request(k, Tip(k, windPx * 0.8f, true), Away(Origin(k), Tip(k, windPx * 0.8f, true)), new[] { a, P.Eps }, v => $"{nm} α {v[0]:0}° · ε {v[1]:0}°", TailWindC, 4);
            }
            if (On("inertial") && F.ContainsKey("inertial")) { Arrow("inertial", Inertial, gPx); _labels.Request("inertial", Tip("inertial", gPx), Away(Origin("inertial"), Tip("inertial", gPx)), new[] { P.InertialG }, v => $"INERTIAL {v[0]:0.0} g", Inertial, 1, 0.1); }
            if (On("total") && F.ContainsKey("total")) { Arrow("total", Total, gPx); _labels.Request("total", Tip("total", gPx), Away(Origin("total"), Tip("total", gPx)), new[] { P.AeroG }, v => $"TOTAL AERO {v[0]:0.0} g", Total, 1, 0.1); }
            if (On("fourForces"))
            {
                // FOUR FORCES: lift ⟂ and drag ∥ the relative wind from the CG (total aero), weight, thrust on its line; each
                // labelled in pounds. Lift/weight at one scale, thrust/drag at another (×3) — the pairs compare directly.
                double lb = 0.2248;
                if (On("weight")) { Arrow("weight", Weight, gPx); _labels.Request("weight", Tip("weight", gPx), Away(Origin("weight"), Tip("weight", gPx)), new[] { P.WeightN * lb }, v => ForceValues ? $"WEIGHT {v[0]:#,0} lb" : "WEIGHT", Weight, 3, 10); }
                if (On("lift") && F.ContainsKey("lift4")) { Arrow("lift4", Lift, gPx); _labels.Request("lift4", Tip("lift4", gPx), Away(Origin("lift4"), Tip("lift4", gPx)), new[] { P.LiftN * lb }, v => ForceValues ? $"LIFT {v[0]:#,0} lb" : "LIFT", Lift, 5, 10); }
                if (On("drag") && F.ContainsKey("drag4") && ForceEnds) { Arrow("drag4", Drag, gPx); HeadLabel(ui, Origin("drag4"), Tip("drag4", gPx), ForceValues ? $"DRAG {P.DragN * lb:#,0} lb" : "DRAG", Drag); }
                else if (On("drag") && F.ContainsKey("drag4")) { Arrow("drag4", Drag, gPx); _labels.Request("drag4", Tip("drag4", gPx), Away(Origin("drag4"), Tip("drag4", gPx)), new[] { P.DragN * lb }, v => DragScale == 1.0 ? $"DRAG {v[0]:#,0} lb" : $"DRAG {v[0]:#,0} lb ×{DragScale:0.#}", Drag, 5, 3); }
                if (On("thrust") && F.ContainsKey("thrust") && ForceEnds) { Arrow("thrust", Thrust, gPx); HeadLabel(ui, Origin("thrust"), Tip("thrust", gPx), ForceValues ? $"THRUST {P.ThrustAlongN * lb:#,0} lb" : "THRUST", Thrust); }
                else if (On("thrust") && F.ContainsKey("thrust")) { Arrow("thrust", Thrust, gPx); _labels.Request("thrust", Tip("thrust", gPx), Away(Origin("thrust"), Tip("thrust", gPx)), new[] { P.ThrustAlongN * lb }, v => DragScale == 1.0 ? $"THRUST {v[0]:#,0} lb" : $"THRUST {v[0]:#,0} lb ×{DragScale:0.#}", Thrust, 5, 3); }
            }
            else
            {
            if (On("weight")) { Arrow("weight", Weight, gPx); _labels.Request("weight", Tip("weight", gPx), Away(Origin("weight"), Tip("weight", gPx)), new double[0], v => "WEIGHT", Weight, 3); }
            if (On("lift")) { Arrow("lift", Lift, gPx); _labels.Request("lift", Tip("lift", gPx), Away(Origin("lift"), Tip("lift", gPx)), new double[0], v => "LIFT", Lift, 5); }
            if (On("drag")) { Arrow("drag", Drag, gPx); _labels.Request("drag", Tip("drag", gPx), Away(Origin("drag"), Tip("drag", gPx)), new double[0], v => DragScale == 1.0 ? "DRAG" : $"DRAG ×{DragScale:0.#}", Drag, 5); }
            if (On("thrust") && F.ContainsKey("thrust")) Arrow("thrust", Thrust, gPx);
            }
            if (On("tail") && P.HasTail && F.ContainsKey("tail"))
            {
                Arrow("tail", TailC, gPx);
                double lb = P.TailN * 0.2248;   // body z down: + = DOWN force
                _labels.Request("tail", Tip("tail", gPx), Away(Origin("tail"), Tip("tail", gPx)), new[] { System.Math.Abs(lb) }, v => $"TAIL {(lb >= 0 ? "↓" : "↑")} {v[0]:0} lb", TailC, 3, 5);
            }
            if (On("wheels") && w.ShownForces != null)
                foreach (var f in w.ShownForces) if (f.Kind == "gear" && f.ForceBody.Length > w.Ac.MassProperties.MassKg * 9.81 * 0.02)
                    { Vector3 o = World(f.PosBody), d = Dir(f.ForceBody * (1.0 / (w.Ac.MassProperties.MassKg * 9.81))) * (gPx * pxM); if (Scr(o, out var a) && Scr(o + d, out var b) && ClipToScene(ref a, ref b, scene)) ui.PxArrow(a, b, lw * 0.7f, Wheel); }

            // The vertical tail's side force (directional stability: it pushes the tail back into line; or the rudder's yaw force).
            if (On("finForce") && P.HasFin && F.ContainsKey("fin"))
            {
                Arrow("fin", Wind * 0.9f + Color.white * 0.1f, gPx);
                double lb = P.FinN * 0.2248;   // body y: + = force to the right
                _labels.Request("fin", Tip("fin", gPx), Away(Origin("fin"), Tip("fin", gPx)), new[] { System.Math.Abs(lb), w.Read().BetaDeg },
                    v => $"FIN SIDE FORCE {(lb >= 0 ? "→ R" : "← L")} {v[0]:0} lb (×12) · β {v[1]:+0;-0}°", new Color(0.55f, 0.95f, 1f), 3, 3);
            }
            // The body axes through the CG: longitudinal (x), lateral (y), vertical (z).
            if (On("bodyAxes"))
            {
                float L = (float)w.LengthM * 0.75f + 1f;
                foreach (var (ax, col, nm) in new[] { (new Vec3(1, 0, 0), new Color(1f, 0.45f, 0.85f), "LONGITUDINAL"), (new Vec3(0, 1, 0), new Color(0.45f, 1f, 0.55f), "LATERAL"), (new Vec3(0, 0, 1), new Color(0.5f, 0.7f, 1f), "VERTICAL") })
                {
                    Vector3 d = Dir(ax) * (ax.Y != 0 ? (float)w.SpanM * 0.65f + 1f : L);
                    if (Scr(cgU - d, out var a) && Scr(cgU + d, out var b))
                    {
                        // dashed, so it reads as a reference line, not a force
                        for (int k = 0; k < 20; k += 2) { Vector2 p0 = Vector2.Lerp(a, b, k / 20f), p1 = Vector2.Lerp(a, b, (k + 1) / 20f); if (ClipToScene(ref p0, ref p1, scene)) ui.PxLine(p0, p1, 2.2f, col); }
                        var tipEnd = ax.Z != 0 ? a : b;   // label the vertical axis at its top (−z), the others at their + end
                        _labels.Request("axis" + nm, tipEnd, (tipEnd - (a + b) * 0.5f).normalized, nm + " AXIS", col, 7);
                    }
                }
            }
            // The YAW moment about the vertical axis: a tight circular arrow above AND below the airplane (seen from either side).
            if (On("yawMoment"))
            {
                double nRef = w.Ac.MassProperties.MassKg * 9.81 * 0.015 * System.Math.Max(2, w.LengthM);
                float h = (float)System.Math.Max(1.2, w.LengthM * 0.28), r0 = (float)System.Math.Max(0.8, w.LengthM * 0.22);
                Vector3 X = Dir(new Vec3(1, 0, 0)), Y = Dir(new Vec3(0, 1, 0)), Up = -Dir(new Vec3(0, 0, 1));
                double nf = P.MomYaw; float sweep = Mathf.Clamp((float)(System.Math.Abs(nf) / nRef) * 120f, 40f, 300f) * Mathf.Deg2Rad, sgn = nf >= 0 ? 1f : -1f;   // + = nose RIGHT
                foreach (float side in new[] { 1f, -1f })
                {
                    Vector3 C = cgU + Up * h * side; var pts = new List<Vector2>();
                    for (int i = 0; i <= 26; i++)
                    {
                        float th = -sgn * sweep * 0.5f + sgn * sweep * i / 26f;   // increasing θ: from +x toward +y = nose right
                        Vector3 pw = C + (X * Mathf.Cos(th) + Y * Mathf.Sin(th)) * r0;
                        if (!Scr(pw, out var q) || !scene.Contains(q)) { pts.Clear(); break; }
                        pts.Add(q);
                    }
                    if (pts.Count > 3) ui.PxPolyArrow(pts, lw * 0.85f, new Color(0.45f, 1f, 0.85f));
                    if (side > 0 && pts.Count > 3) _labels.Request("yawM", pts[pts.Count / 2], Vector2.up, new[] { System.Math.Abs(nf) * 0.7376, nf, System.Math.Abs(P.FinYaw) * 0.7376 },
                        v => $"YAW {(v[1] >= 0 ? "nose-right" : "nose-left")} {v[0]:#,0} ft·lb (fin {v[2]:#,0})", new Color(0.45f, 1f, 0.85f), 2, 10);
                }
            }
            if (SmokeDebug && w is AeroWidget sw)
                foreach (var e in sw.SmokeExhausts)
                    if (Scr(World(cg + e), out var ep)) { ui.PxDisc(ep, 9f, Color.black); ui.PxDisc(ep, 6f, new Color(1f, 0.2f, 0.9f)); }
            // CG and neutral-point marks (weight & balance; lessons): the CG a black-and-yellow disc, the NP a red ring.
            if (On("cgnp"))
            {
                if (Scr(World(cg), out var cgp)) { ui.PxDisc(cgp, 9f, Color.black); ui.PxDisc(cgp, 6f, Inertial); _labels.Request("cgMark", cgp, Vector2.down, $"CG {w.Loading.CgMac:0} % MAC", Inertial, 6); }
                if (Scr(World(w.NeutralPointBody()), out var npp)) { ui.PxRing(npp, 8f, 3f, new Color(1f, 0.25f, 0.2f)); _labels.Request("npMark", npp, Vector2.up, $"NP {w.Loading.NpMac:0} % · SM {w.Loading.StaticMarginMac:0.0} %", new Color(1f, 0.35f, 0.3f), 6); }
            }
            if (w.Lesson != null && w.Lessons != null) w.Lessons.DrawExtras(ui, this, World, Dir, (Vector3 p, out Vector2 o) => Scr(p, out o), pxM, gPx, lw);
            // Moments: AERO ahead of the nose, INERTIA behind the tail, one shared scale.
            if (On("moments"))
            {
                double mRef = w.Ac.MassProperties.MassKg * 9.81 * 0.06 * System.Math.Max(2, w.LengthM);
                // Both arcs are drawn ABOUT THE CG, in the pitch plane, on a radius that clears the airframe: AERO's arc in front of
                // the nose, INERTIA's behind the tail (owner 2026-10-09).
                float R = ((float)w.LengthM * 0.62f + 1.5f) * MomentArcRadius;
                Arc(ui, Dir, cgU, R, MomentArcsSwap ? Mathf.PI : 0f, P.MomAero, mRef, Total, "aeroM", AeroMomentLabel ?? "AERO", scene, lw);
                float ic = MomentArcsSwap ? 0f : Mathf.PI;
                if (!InertiaSplit) { Arc(ui, Dir, cgU, R, ic, P.MomInertia, mRef, Inertial, "inertiaM", InertiaMomentLabel ?? "INERTIA", scene, lw); HideArc("structM"); HideArc("gyroM"); }
                else
                {
                    // End to end along the nose-up sense, the two together as long as one whole arc (the stability arc balances
                    // their sum): structure first, then the prop's gyroscopic part. A part that is nose-DOWN points back.
                    HideArc("inertiaM");
                    double ms = SmoothM("structSh", P.MomInertia), mg = SmoothM("gyroSh", P.MomGyro), sum = ms + mg, mag = System.Math.Abs(ms) + System.Math.Abs(mg);
                    float fs = mag > 1e-6 ? (float)(System.Math.Abs(ms) / mag) : 0.5f, gap = 0.04f;
                    string Pct(double m) => System.Math.Abs(sum) > 1e-6 ? $"{System.Math.Round(100 * m / sum):0}%" : "";
                    string Sense(double m) => m >= 0 ? "nose up" : "nose down";
                    Arc(ui, Dir, cgU, R, ic, P.MomInertia, mRef, Inertial, "structM", $"STRUCTURE INERTIA → {Sense(ms)}  {Pct(ms)}", scene, lw, sum, fs * (1 - gap), -0.5f + fs * (1 - gap) * 0.5f);
                    Arc(ui, Dir, cgU, R, ic, P.MomGyro, mRef, Thrust, "gyroM", $"GYROSCOPIC (prop) → {Sense(mg)}  {Pct(mg)}", scene, lw, sum, (1 - fs) * (1 - gap), 0.5f - (1 - fs) * (1 - gap) * 0.5f);
                }
            }
            else foreach (var a in _solidArcs.Values) a.Hide();
            if (!Solid3D) { foreach (var a in _solidArcs.Values) a.Hide(); foreach (var a in _solidArrows.Values) a.Hide(); }
            if (Solid3D)
            {
                _soloBody.Clear();
                // The airplane's real outline (2026-10-13: a label sat on the fin outside the old nose–tips–tail quad): every
                // surface strip's leading and trailing edge, the spinner, and the canopy and gear above and below the wing root.
                float L = (float)w.LengthM;
                double noseX = w is AeroWidget nw && !double.IsNaN(nw.ModelNoseFromCg) ? nw.ModelNoseFromCg : 0.4 * L;
                void Add(Vec3 bp) { if (Scr(World(cg + bp), out var q)) _soloBody.Add(q); }
                foreach (var sf in w.Config.Surfaces) foreach (var st in sf.Strips)
                {
                    Vec3 p = st.PosVec() - cg;
                    Add(new Vec3(p.X + 0.25 * st.Chord, p.Y, p.Z)); Add(new Vec3(p.X - 0.75 * st.Chord, p.Y, p.Z));
                }
                Add(new Vec3(noseX, 0, 0)); Add(new Vec3(0.1 * noseX, 0, -0.75)); Add(new Vec3(0.3 * noseX, 0, 0.9));
                Hull(_soloBody);
                if (SmokeDebug) for (int i = 0; i < _soloBody.Count; i++) ui.PxLine(_soloBody[i], _soloBody[(i + 1) % _soloBody.Count], 3f, Color.red);   // (the labels' no-go outline)
                SoloLabels(ui, scene);
            }
            _soloSegs.Clear(); _soloLabels.Clear();
            _labels.Place(ui, LabelScene(w, scene, ui), cgScr);
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
                         Color c, string id, string name, Rect scene, float lw, double? sweepM = null, float sweepFrac = 1f, float offsetFrac = 0f)
        {
            double k = SmoothingMs <= 1 ? 1 : 1 - System.Math.Exp(-Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 1000.0 / SmoothingMs);
            if (!_mFilt.TryGetValue(id, out double mf) || Widget.Shown != null) mf = m;
            mf += (m - mf) * k; _mFilt[id] = mf;
            // (a part of a split arc: sized as its whole, sweepM, then the fraction sweepFrac of it, offsetFrac of the whole along it)
            double ms = sweepM.HasValue ? SmoothM(id + "Whole", sweepM.Value) : mf;
            float sweep = (MomentArcsEqual ? MomentArcSweepDeg : Mathf.Clamp((float)(System.Math.Abs(ms) / mRef) * 70f, 25f, 160f)) * Mathf.Deg2Rad;
            float sgn = mf >= 0 ? 1f : -1f;   // + = nose-up
            if (MomentArcNoseSense && Mathf.Abs(centreAngle - Mathf.PI) < 0.1f) sgn = -sgn;   // at the tail: the head shows where the NOSE goes
            Vector3 X = dir(new Vec3(1, 0, 0)), Up = -dir(new Vec3(0, 0, 1));   // body x and body "up" (−z)
            var cam = Widget.Cam;
            // Equal arcs READ equal (owner 2026-10-12): an arc further from the camera is drawn proportionally bigger, so the
            // nose's and the tail's look the same size on screen whichever end is nearer.
            float persp = 1f;
            if (MomentArcsEqual && cam != null)
            {
                Vector3 cen = cgU + (X * Mathf.Cos(centreAngle) + Up * Mathf.Sin(centreAngle)) * R;
                float dc = Vector3.Distance(cam.transform.position, cgU), da = Vector3.Distance(cam.transform.position, cen);
                if (dc > 0.1f) persp = Mathf.Clamp(da / dc, 0.6f, 1.6f);
                sweep *= persp;
            }
            centreAngle += offsetFrac * sweep; sweep *= sweepFrac;
            var pts = new List<Vector2>(); var wpts = new List<Vector3>();
            float th0 = centreAngle - sgn * sweep * 0.5f;
            for (int i = 0; i <= 28; i++)
            {
                float th = th0 + sgn * sweep * i / 28f;   // increasing θ = nose-up rotation at every point of the circle
                Vector3 p = cgU + (X * Mathf.Cos(th) + Up * Mathf.Sin(th)) * R;
                wpts.Add(p);
                var sp = cam.WorldToScreenPoint(p); if (sp.z <= 0) { if (Solid3D) continue; return; }
                var q = new Vector2(_off.x + sp.x, _off.y + sp.y);
                if (!scene.Contains(q)) { if (pts.Count > 2) break; else { pts.Clear(); continue; } }
                pts.Add(q);
            }
            if (Solid3D)
            {
                // In the scene, depth-tested: the airplane hides what's behind it. Sized from the arc's radius.
                float tr = 0.022f * R * persp;
                SolidArc(id, c).Place(wpts, tr, 0.16f * R * MomentArcHead * persp, tr * 2.8f * Mathf.Max(1f, MomentArcHead * 0.8f));
                for (int i = 0; i + 1 < pts.Count; i++) _soloSegs.Add((pts[i], pts[i + 1]));
                if (pts.Count >= 1)
                {
                    var cgS = cam.WorldToScreenPoint(cgU); Vector2 cg2 = new(_off.x + cgS.x, _off.y + cgS.y);
                    string txt = !MomentArcValues ? name : $"{name}  {System.Math.Abs(m) * 0.7376:#,0} ft·lb";
                    _soloLabels.Add((pts[pts.Count / 2], pts[pts.Count / 2] - cg2, txt, c));
                }
                return;
            }
            if (pts.Count < 3) return;
            float thick = MomentArcsEqual ? lw * 1.2f : lw * Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01((float)(System.Math.Abs(mf) / (2 * mRef))));
            ui.PxPolyArrow(pts, thick, c, MomentArcHead);
            double ftlb = System.Math.Abs(m) * 0.7376;
            Vector2 mid = pts[pts.Count / 2];
            _labels.Request(id, mid, Vector2.down, new[] { ftlb, m }, v => !MomentArcValues ? name : name.Contains("→") ? $"{name}  {v[0]:#,0} ft·lb" : $"{name} {(v[1] >= 0 ? "nose-up" : "nose-down")} {v[0]:#,0} ft·lb", c, 2, 10);   // (a named direction says it in words)
        }
        private readonly Dictionary<string, double> _mFilt = new();
        /// <summary>The arcs' smoothing (SmoothingMs) for a value that isn't itself an arc (a split's whole, its shares).</summary>
        private double SmoothM(string id, double m)
        {
            double k = SmoothingMs <= 1 ? 1 : 1 - System.Math.Exp(-Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 1000.0 / SmoothingMs);
            if (!_mFilt.TryGetValue(id, out double mf) || Widget.Shown != null) mf = m;
            mf += (m - mf) * k; _mFilt[id] = mf; return mf;
        }
        private void HideArc(string id) { if (_solidArcs.TryGetValue(id, out var a)) a.Hide(); }

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

        /// <summary>The labels' room: the scene less the caption's band at the bottom, so no label lands on the caption.</summary>
        private static Rect LabelScene(object w, Rect scene, WidgetControlsDisplay ui)
        {
            if (!(w is AeroWidget aw) || aw.DisplayMode != "classic" || string.IsNullOrEmpty(aw.Caption)) return scene;
            float band = 64f * ui.TextScale;
            return new Rect(scene.x, scene.y + band, scene.width, Mathf.Max(0f, scene.height - band));
        }

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
                var placed = new List<Rect>(ui.LabelKeepOut); _placedRef = placed;
                Rect RectAt(Vector2 p, float tw) => new Rect(p.x - tw * 0.5f - 4, p.y - 13 * ui.TextScale, tw + 8, 26 * ui.TextScale);
                foreach (var r in _req)
                {
                    if (!_st.TryGetValue(r.id, out var st)) { st = new St(); _st[r.id] = st; }
                    bool changed = st.Shown == null || st.Shown.Length != r.vals.Length;
                    if (!changed) for (int i = 0; i < r.vals.Length; i++) if (System.Math.Abs(r.vals[i] - st.Shown[i]) >= r.step * 0.75) changed = true;
                    if (changed && (paused || now - st.LastText >= 0.25f)) { st.Shown = (double[])r.vals.Clone(); st.LastText = now; st.Text = r.fmt(st.Shown); }
                    if (r.vals.Length == 0) st.Text = r.fmt(r.vals);   // (a label with no number can still change: "DRAG ×3" → "DRAG")
                    else st.Text ??= r.fmt(r.vals);
                    float twid = st.Text.Length * 17f * ui.TextScale * 0.53f;
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
                    // Leader: from the label's near edge to the arrow's tip.
                    float tw = st.Text.Length * 17f * ui.TextScale * 0.53f, th = 17f * ui.TextScale;
                    // Whole text inside the picture (a scaled label at an edge slot ran off the frame).
                    float px = Mathf.Clamp(st.Pos.x, scene.xMin + tw * 0.5f + 6f, Mathf.Max(scene.xMin + tw * 0.5f + 6f, scene.xMax - tw * 0.5f - 6f));
                    float py = Mathf.Clamp(st.Pos.y, scene.yMin + th * 0.6f + 4f, Mathf.Max(scene.yMin + th * 0.6f + 4f, scene.yMax - th * 0.6f - 4f));
                    var drawn = new Vector2(Mathf.Round(px), Mathf.Round(py));
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
        /// the threshold mark and the aim point; spin and cruise — a ground grid, so the airplane-fixed view
        /// shows the world turning around the airplane. Pixel-thick at any zoom.</summary>
        private void DrawWorld(float px)
        {
            var h = Widget;
            if (!(h is AeroWidget w))
            {
                // The game: the earth grid is an option over its real world (off by default) — outside views only.
                if (h.Show["earthGrid"] && !ChaseCamera.InCockpit && !SessionSettings.MenuOpen) EarthGrid(h, (float)h.GroundM(h.ShownState.Position.X, h.ShownState.Position.Y), px);
                return;
            }
            if (w.Current == AeroWidget.Scenario.Flare)
            {
                if (w.Condition != "final") return;
                if (w.View != "side") { Runway3D(w); if (w.Show["earthGrid"]) EarthGrid(w, 0f, px); return; }
                float L = (float)w.Runway.LengthM, z0 = -L / 2f, z1 = L / 2f, aim = z0 + (float)FlyingGame.Sim.Practice.PracticeScenario.NumbersPastThresholdM;
                Line(new Vector3(0, 0, -20000), new Vector3(0, 0, 20000), new Color(1, 1, 1, 0.45f));                  // the ground
                for (int k = 0; k <= 4; k++) Line(new Vector3(0, -k * px, z0), new Vector3(0, -k * px, z1), Color.white);   // the runway: 5 px thick
                for (int k = -1; k <= 1; k++) Line(new Vector3(0, 0, z0 + k * px), new Vector3(0, 14 * px, z0 + k * px), Color.white);   // the threshold
                for (int k = 0; k <= 8; k++) Line(new Vector3(0, k * px * 0.5f, aim), new Vector3(0, k * px * 0.5f, aim + 45f), Color.white);   // the aim point (a 45 m bar)
                return;
            }
            if (!w.Show["earthGrid"]) return;
            EarthGrid(w, 0f, px);
        }

        /// <summary>EARTH GRID: a white ground grid (250 m squares, 8 km across) at the ground's height under the airplane. Transparent between the lines.</summary>
        private void EarthGrid(AeroHost w, float groundY, float px)
        {
            // No horizon circle (owner 2026-10-12: drawn at eye level it floated apart from the grid and confused). The grid
            // bolder: more opaque, and doubled a pixel (at the airplane's distance) higher.
            Vector3 ac = CoordinateMap.ToUnity(w.ShownState.Position);
            const float G = 250f, E = 4000f; Color gc = new(1f, 1f, 1f, 0.6f);
            float cx = Mathf.Round(ac.x / G) * G, cz = Mathf.Round(ac.z / G) * G;
            // Faded with distance (owner 2026-10-12: far off, thin lines at a grazing angle broke up into dashes — moiré): each
            // line in grid-cell pieces, its opacity falling to nothing at the edge; the doubled line only near the airplane.
            for (int k = 0; k < 2; k++)
            {
                float y = groundY + k * px, reach = k == 0 ? E : 0.35f * E;
                for (float d = -E; d <= E; d += G)
                    for (float e = -E; e < E; e += G)
                    {
                        foreach (bool alongZ in new[] { true, false })
                        {
                            Vector3 a = alongZ ? new Vector3(cx + d, y, cz + e) : new Vector3(cx + e, y, cz + d);
                            Vector3 b = alongZ ? new Vector3(cx + d, y, cz + e + G) : new Vector3(cx + e + G, y, cz + d);
                            Vector3 m = 0.5f * (a + b); float dist = new Vector2(m.x - ac.x, m.z - ac.z).magnitude;
                            float f = Mathf.Clamp01(1f - dist / reach); if (f <= 0f) continue;
                            Line(a, b, new Color(gc.r, gc.g, gc.b, gc.a * f * f));
                        }
                    }
            }
        }
        private static void Line(Vector3 a, Vector3 b, Color c) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }

        /// <summary>FINAL's runway in the 3D views (the asphalt is AeroWidget's slab): white edges, the threshold bars, the
        /// centreline dashes and the aim-point bars, a hair above the surface.</summary>
        private void Runway3D(AeroWidget w)
        {
            float L = (float)w.Runway.LengthM, W = (float)w.Runway.Strip.Width, z0 = -L / 2f, z1 = L / 2f, h = 0.03f, hw = W / 2f;
            float aim = z0 + (float)FlyingGame.Sim.Practice.PracticeScenario.NumbersPastThresholdM;
            Color c = Color.white;
            Line(new Vector3(-hw, h, z0), new Vector3(-hw, h, z1), c); Line(new Vector3(hw, h, z0), new Vector3(hw, h, z1), c);
            Line(new Vector3(-hw, h, z0), new Vector3(hw, h, z0), c); Line(new Vector3(-hw, h, z1), new Vector3(hw, h, z1), c);
            for (int k = 0; k < 8; k++)   // the threshold "piano keys"
            {
                float x = -hw + W * (k + 0.5f) / 8f, bw = W / 24f;
                for (float dx = -bw; dx <= bw; dx += bw / 2f) Line(new Vector3(x + dx, h, z0 + 6f), new Vector3(x + dx, h, z0 + 36f), c);
            }
            for (float z = z0 + 60f; z < z1 - 30f; z += 60f) Line(new Vector3(0, h, z), new Vector3(0, h, z + 36f), c);   // the centreline
            foreach (float sx in new[] { -1f, 1f })   // the aim-point markings, each side of the centreline
                for (float dx = 0; dx <= 9f; dx += 1.5f) Line(new Vector3(sx * (W * 0.18f + dx), h, aim), new Vector3(sx * (W * 0.18f + dx), h, aim + 45f), c);
        }
    }
}

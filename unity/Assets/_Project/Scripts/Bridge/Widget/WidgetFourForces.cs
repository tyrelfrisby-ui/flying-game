using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// FOUR FORCES readouts (owner 2026-10-11: "a graphical readout of lift drag and thrust so i can put the airplane on autopilot
    /// and manipulate the throttle and see how an imbalanced thrust/drag force results in an acceleration/deceleration until the
    /// forces are back in balance … same for lift and weight"). Two insets, both from the sim's own forces (WidgetVectors.Picture):
    ///   forceBalance — ALONG THE PATH: thrust vs drag (+ weight's part along the path, W·sinγ, in a climb or descent) and the
    ///                  result (speeding up / slowing down / steady); ACROSS THE PATH: lift vs weight (the weight bar fixed) and
    ///                  the result (the path curving up / down / steady). The results are the exact sums of every force.
    ///   forceTrend   — the last 60 s of sim time in lanes: thrust · drag, lift · weight, α, the flight-path angle γ and the
    ///                  airspeed, so a throttle or pitch change can be followed to the new equilibrium.
    /// </summary>
    public sealed class WidgetForceTrend
    {
        public struct Sample { public float T; public double Thrust, Drag, WSin, Lift, WPerp, W, Alpha, Gamma, Kias, Accel, PathRate; }
        public const float Seconds = 60f, Hz = 5f;
        public readonly List<Sample> Samples = new();
        /// <summary>The balance bars, low-pass filtered (≈0.4 s) so they read steadily.</summary>
        public Sample Smooth;
        private bool _has; private float _clock, _next;
        private string _for;

        /// <summary>One sim step (live only; the reviewed frame doesn't record).</summary>
        public void Tick(AeroHost w, float dt)
        {
            if (dt <= 0 || w.Ac == null || w.Shown != null) return;
            var p = w.Vectors?.Current; if (p == null || p.VInf < 1) return;
            if (_for != w.AircraftId) { Samples.Clear(); _has = false; _for = w.AircraftId; }
            var rd = w.Read();
            var s = new Sample { Thrust = p.ThrustAlongN, Drag = p.DragN, WSin = p.WSinGammaN, Lift = p.LiftN, WPerp = p.WPerpN, W = p.WeightN,
                                 Alpha = rd.AlphaDeg, Gamma = p.GammaDeg, Kias = rd.Kias, Accel = p.AccelAlong, PathRate = p.PathRate };
            if (!_has) { Smooth = s; _has = true; }
            else
            {
                double k = 1 - System.Math.Exp(-dt / 0.4);
                Smooth.Thrust += (s.Thrust - Smooth.Thrust) * k; Smooth.Drag += (s.Drag - Smooth.Drag) * k; Smooth.WSin += (s.WSin - Smooth.WSin) * k;
                Smooth.Lift += (s.Lift - Smooth.Lift) * k; Smooth.WPerp += (s.WPerp - Smooth.WPerp) * k; Smooth.W = s.W;
                Smooth.Accel += (s.Accel - Smooth.Accel) * k; Smooth.PathRate += (s.PathRate - Smooth.PathRate) * k;
                Smooth.Gamma += (s.Gamma - Smooth.Gamma) * k; Smooth.Kias = s.Kias; Smooth.Alpha = s.Alpha;
            }
            _clock += dt;
            if (_clock < _next) return;
            _next = _clock + 1f / Hz;
            s.T = _clock; Samples.Add(s);
            while (Samples.Count > 0 && _clock - Samples[0].T > Seconds) Samples.RemoveAt(0);
        }
        public float Clock => _clock;
        public bool HasData => _has;
    }

    /// <summary>
    /// THE FOUR FORCES LESSON (owner 2026-10-11), interactive and the same in the game and the widget: FOUR FORCES on, the
    /// autopilot in CWS (PIT + BANK: it holds the pitch attitude and wings level, nothing else), A/T off — the pilot moves the
    /// THROTTLE and the PITCH slider and watches each imbalance (thrust ≠ drag → the speed changes; lift ≠ weight → the path
    /// bends) die away into a new equilibrium: a steady climb, level flight or a steady descent. Each settled state is noted.
    /// </summary>
    public sealed class FourForcesCoach
    {
        private readonly AeroHost _w;
        public FourForcesCoach(AeroHost w) { _w = w; }
        public bool Active { get; private set; }
        public int Step { get; private set; }
        public readonly List<string> Results = new();
        private double _thr0, _pit0; private float _steadyT, _t;
        public const string Name = "The four forces";
        public const string Concept = "Thrust, drag, lift and weight: an imbalance accelerates the airplane or bends its path until the forces balance again — a new steady climb, level flight or descent. The autopilot holds only the pitch attitude.";

        /// <summary>Set up on the present flight: four forces, the readouts, CWS holding the attitude (wings level), A/T off.</summary>
        public void Start()
        {
            _w.Show["fourForces"] = false; _w.SetFourForces(true);
            // The game: a side view, so thrust and drag lie across the picture (the widget's lesson sets its own view).
            if (!_w.IsWidgetHost && (_w.GetComponent<ChaseCamera>() ?? Object.FindFirstObjectByType<ChaseCamera>()) is ChaseCamera cc && cc.CurrentView != ChaseCamera.View.SideLeft && cc.CurrentView != ChaseCamera.View.SideRight) cc.CurrentView = ChaseCamera.View.SideLeft;
            var ap = _w.Autopilot; var rd = _w.Read();
            ap.Attitude(System.Math.Round(rd.PitchDeg * 2) / 2, 0);
            ap.AutoThrottle = false; ap.YawDamper = true; ap.AllowStall = false;
            Results.Clear(); Active = true; Enter(0);
        }
        public void Stop() { Active = false; }
        private void Enter(int step) { Step = step; _t = 0; _steadyT = 0; _thr0 = _w.AeroControls.Throttle01; _pit0 = _w.Autopilot.PitchDeg; }

        private bool Steady(float dt)
        {
            var s = _w.ForceTrend.Smooth;
            bool st = System.Math.Abs(s.Accel * 1.943844) < 0.15 && System.Math.Abs(s.PathRate * 57.2958) < 0.15;
            _steadyT = st ? _steadyT + dt : 0;
            return _steadyT > 3f && _t > 4f;
        }
        private void Note(string why)
        {
            var s = _w.ForceTrend.Smooth;
            string path = System.Math.Abs(s.Gamma) < 0.5 ? "LEVEL" : s.Gamma > 0 ? $"CLIMBING {s.Gamma:0.0}° ({-_w.Read().SinkFpm:+0} fpm)" : $"DESCENDING {-s.Gamma:0.0}° ({-_w.Read().SinkFpm:0} fpm)";
            Results.Insert(0, $"{why}: PITCH {_w.Autopilot.PitchDeg:+0.0;-0.0}° · POWER {_w.AeroControls.Throttle01 * 100:0} % → {s.Kias:0} KIAS, α {s.Alpha:0.0}°, {path}");
            while (Results.Count > 4) Results.RemoveAt(4);
        }

        public void Tick(float dt)
        {
            if (!Active || dt <= 0) return;
            var ap = _w.Autopilot;
            if (!ap.On || ap.PitchMode != "pit") { Active = false; return; }   // another mode (or AP off) ends the lesson; the picture stays
            _t += dt;
            bool powerMoved = System.Math.Abs(_w.AeroControls.Throttle01 - _thr0) > 0.05, pitchMoved = System.Math.Abs(ap.PitchDeg - _pit0) >= 0.9;
            switch (Step)
            {
                case 0: if (powerMoved) Enter(1); else if (pitchMoved) Enter(3); break;
                case 1: if (Steady(dt)) { Note("NEW POWER"); Enter(2); } break;
                case 2: if (pitchMoved) Enter(3); else if (powerMoved) Enter(1); break;
                case 3: if (Steady(dt)) { Note("NEW PITCH"); Enter(4); } break;
                default: if (powerMoved || pitchMoved) Enter(5); break;
                case 5: if (Steady(dt)) { Note("SETTLED"); Enter(4); } break;
            }
        }

        /// <summary>What to do now.</summary>
        public string Instruction => !Active ? null : Step switch
        {
            0 => "THE AUTOPILOT HOLDS ONLY THE PITCH ATTITUDE — MOVE THE THROTTLE",
            1 => "POWER CHANGED: THRUST ≠ DRAG CHANGES THE SPEED, THE SPEED CHANGES LIFT, LIFT BENDS THE PATH",
            2 => "NOW CHANGE THE PITCH ATTITUDE (CWS PITCH): A NEW ANGLE OF ATTACK",
            3 => "PITCH CHANGED: α CHANGED LIFT → THE PATH BENDS → THE SPEED FOLLOWS → LIFT RETURNS TO WEIGHT",
            5 => "SETTLING INTO THE NEW EQUILIBRIUM",
            _ => "EXPLORE: EVERY POWER AND PITCH SETTLES INTO ITS OWN STEADY CLIMB, LEVEL FLIGHT OR DESCENT",
        };
        /// <summary>What the forces are doing now.</summary>
        public string Live
        {
            get
            {
                if (!Active) return null;
                var s = _w.ForceTrend.Smooth; double kts = s.Accel * 1.943844, dps = s.PathRate * 57.2958;
                string along = System.Math.Abs(kts) < 0.15 ? (s.WSin > s.W * 0.005 ? "THRUST = DRAG + W·sinγ" : s.WSin < -s.W * 0.005 ? "THRUST + W·sinγ = DRAG" : "THRUST = DRAG")
                    : kts > 0 ? $"THRUST > DRAG → SPEEDING UP {kts:0.0} KT/S" : $"DRAG > THRUST → SLOWING {-kts:0.0} KT/S";
                string across = System.Math.Abs(dps) < 0.15 ? "LIFT = WEIGHT" : dps > 0 ? $"LIFT > WEIGHT → PATH BENDING UP {dps:0.0}°/S" : $"LIFT < WEIGHT → PATH BENDING DOWN {-dps:0.0}°/S";
                return $"{along} · {across}";
            }
        }
    }

    public sealed partial class WidgetControlsDisplay
    {
        private static readonly Color FfLift = new(0.25f, 1f, 0.35f), FfDrag = new(1f, 0.3f, 0.25f), FfWeight = new(1f, 0.9f, 0.2f), FfThrust = new(0.35f, 0.6f, 1f),
            FfDim = new(1, 1, 1, 0.6f), FfSteady = new(0.55f, 0.95f, 0.6f), FfChange = new(1f, 0.78f, 0.3f);
        private double _ffAlongScale;

        /// <summary>The balance plate: two pairs of bars on one scale each, and what the imbalance is doing.</summary>
        private void ForceBalanceInset(Rect r, AeroHost w)
        {
            float u = TextScale;
            PxText(new Vector2(r.xMin + 10 * u, r.yMax - 12 * u), "FOUR FORCES", 14f, Color.white, TextAnchor.MiddleLeft);
            var tr = w.ForceTrend;
            // Review: the frame being shown (its recorded forces); live: the smoothed trend.
            var p = w.Vectors?.Current;
            if (w.Shown == null && !tr.HasData) return;
            if (w.Shown != null && (p == null || p.WeightN <= 0)) return;
            var s = w.Shown != null ? new WidgetForceTrend.Sample { Thrust = p.ThrustAlongN, Drag = p.DragN, WSin = p.WSinGammaN, Lift = p.LiftN, WPerp = p.WPerpN, W = p.WeightN,
                                                                     Gamma = p.GammaDeg, Accel = p.AccelAlong, PathRate = p.PathRate } : tr.Smooth;
            const double lb = 0.2248;
            float lx = r.xMin + 10 * u, bx = r.xMin + 64 * u, bw = r.width - 64 * u - 70 * u, vx = r.xMax - 8 * u, bh = 9f * u;
            float y = r.yMax - 30 * u;
            void Bar(string name, Color c, double v, double scale, double extra = 0, Color? ec = null, string val = null)
            {
                PxText(new Vector2(lx, y), name, 12f, c, TextAnchor.MiddleLeft);
                float x0 = bx, x1 = bx + (float)System.Math.Clamp(v / scale, 0, 1.05) * bw;
                Quad(new Vector2(x0, y - bh * 0.5f), new Vector2(x1, y - bh * 0.5f), new Vector2(x1, y + bh * 0.5f), new Vector2(x0, y + bh * 0.5f), c);
                if (extra > 0)
                {
                    float x2 = bx + (float)System.Math.Clamp((v + extra) / scale, 0, 1.05) * bw;
                    Quad(new Vector2(x1, y - bh * 0.5f), new Vector2(x2, y - bh * 0.5f), new Vector2(x2, y + bh * 0.5f), new Vector2(x1, y + bh * 0.5f), ec ?? FfWeight);
                }
                PxText(new Vector2(vx, y), val ?? $"{v * lb:#,0} lb", 12f, c, TextAnchor.MiddleRight);
                y -= 15f * u;
            }
            void Result(string text, bool steady) { PxText(new Vector2(lx, y), text, Mathf.Min(12f, (r.width - 20 * u) / u / (text.Length * 0.6f)), steady ? FfSteady : FfChange, TextAnchor.MiddleLeft); y -= 18f * u; }

            // ALONG THE PATH — thrust vs drag (+ W·sinγ on the side it acts: against thrust climbing, with it descending).
            PxText(new Vector2(lx, y), "ALONG THE PATH", 11f, FfDim, TextAnchor.MiddleLeft);
            // The two-colour bar's legend (owner asked if it was induced vs parasite): red = ALL the drag; yellow = weight's
            // part along the path (W·sinγ), on the drag side climbing, on the thrust side descending.
            if (System.Math.Abs(s.WSin) > s.W * 0.005) PxText(new Vector2(vx, y), "YELLOW = W·sinγ (WEIGHT)", 10f, FfWeight, TextAnchor.MiddleRight);
            y -= 14f * u;
            double tSide = s.Thrust + System.Math.Max(0, -s.WSin), dSide = s.Drag + System.Math.Max(0, s.WSin);
            double want = System.Math.Max(tSide, dSide) / 0.8;
            // The scale moves slowly (a change in power shows as a change in LENGTH, not a re-scale); it grows at once if needed.
            _ffAlongScale = _ffAlongScale <= 0 || want > _ffAlongScale * 1.05 ? want : _ffAlongScale + (want - _ffAlongScale) * 0.01;
            double sc = System.Math.Max(_ffAlongScale, s.W * 0.02);
            bool climbing = s.WSin > s.W * 0.005, descending = s.WSin < -s.W * 0.005;
            Bar("THRUST", FfThrust, s.Thrust, sc, descending ? -s.WSin : 0, FfWeight, descending ? $"{s.Thrust * lb:#,0}+{-s.WSin * lb:#,0}" : null);
            Bar("DRAG", FfDrag, s.Drag, sc, climbing ? s.WSin : 0, FfWeight, climbing ? $"{s.Drag * lb:#,0}+{s.WSin * lb:#,0}" : null);
            double kts = s.Accel * 1.943844;
            bool steadyA = System.Math.Abs(kts) < 0.15;
            Result(steadyA ? (climbing ? "T = D + W·sinγ → STEADY SPEED" : descending ? "T + W·sinγ = D → STEADY SPEED" : "T = D → STEADY SPEED")
                           : kts > 0 ? $"NET FORWARD → SPEEDING UP {kts:0.0} KT/S" : $"NET BACK → SLOWING {-kts:0.0} KT/S", steadyA);

            // ACROSS THE PATH — lift vs weight; the weight bar is fixed (the scale is the weight).
            PxText(new Vector2(lx, y), "ACROSS THE PATH", 11f, FfDim, TextAnchor.MiddleLeft); y -= 14f * u;
            double lsc = s.W / 0.7;
            Bar("LIFT", FfLift, s.Lift, lsc);
            Bar("WEIGHT", FfWeight, s.W, lsc);
            double dps = s.PathRate * 57.2958;
            bool steadyP = System.Math.Abs(dps) < 0.15;
            string path = System.Math.Abs(s.Gamma) < 0.5 ? "LEVEL" : s.Gamma > 0 ? $"CLIMB {s.Gamma:0.0}°" : $"DESCENT {-s.Gamma:0.0}°";
            Result(steadyP ? $"L = W → STEADY PATH · {path}" : dps > 0 ? $"LIFT WINS → PATH CURVING UP {dps:0.0}°/S" : $"WEIGHT WINS → PATH CURVING DOWN {-dps:0.0}°/S", steadyP);
        }

        /// <summary>The trend plate: the last minute in lanes, newest at the right.</summary>
        private void ForceTrendInset(Rect r, AeroHost w)
        {
            float u = TextScale;
            PxText(new Vector2(r.xMin + 10 * u, r.yMax - 12 * u), "LAST 60 s → NOW", 13f, Color.white, TextAnchor.MiddleLeft);
            var tr = w.ForceTrend; var S = tr.Samples; if (S.Count < 2) return;
            float x0 = r.xMin + 74 * u, x1 = r.xMax - 46 * u, top = r.yMax - 24 * u, bottom = r.yMin + 6 * u;
            const int lanes = 5; float lh = (top - bottom) / lanes;
            float now = tr.Clock;
            float X(float t) => x1 - (now - t) / WidgetForceTrend.Seconds * (x1 - x0);
            double W = S[S.Count - 1].W;
            void Lane(int i, string name, Color nameC, (System.Func<WidgetForceTrend.Sample, double> f, Color c, float lw)[] series, double minSpan, string now0, double zero = double.NaN)
            {
                float yb = top - (i + 1) * lh + 3 * u, yt = top - i * lh - 3 * u;
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var se in series) foreach (var smp in S) { double v = se.f(smp); lo = System.Math.Min(lo, v); hi = System.Math.Max(hi, v); }
                if (!double.IsNaN(zero)) { lo = System.Math.Min(lo, zero); hi = System.Math.Max(hi, zero); }
                if (hi - lo < minSpan) { double m = (hi + lo) * 0.5; lo = m - minSpan * 0.5; hi = m + minSpan * 0.5; }
                float Y(double v) => yb + (float)((v - lo) / (hi - lo)) * (yt - yb);
                PxSeg(new Vector2(x0, yb - 2 * u), new Vector2(x1, yb - 2 * u), 1f, new Color(1, 1, 1, 0.15f));
                if (!double.IsNaN(zero)) PxSeg(new Vector2(x0, Y(zero)), new Vector2(x1, Y(zero)), 1f, new Color(1, 1, 1, 0.35f));
                PxText(new Vector2(r.xMin + 8 * u, (yb + yt) * 0.5f), name, 11f, nameC, TextAnchor.MiddleLeft);
                foreach (var se in series)
                    for (int k = 1; k < S.Count; k++)
                        PxSeg(new Vector2(X(S[k - 1].T), Y(se.f(S[k - 1]))), new Vector2(X(S[k].T), Y(se.f(S[k]))), se.lw, se.c);
                PxText(new Vector2(r.xMax - 6 * u, (yb + yt) * 0.5f), now0, 11f, nameC, TextAnchor.MiddleRight);
            }
            var last = S[S.Count - 1];
            bool anyGamma = false; foreach (var smp in S) if (System.Math.Abs(smp.WSin) > W * 0.005) { anyGamma = true; break; }
            var td = new List<(System.Func<WidgetForceTrend.Sample, double>, Color, float)> { (x => x.Thrust, FfThrust, 2f * u), (x => x.Drag, FfDrag, 2f * u) };
            if (anyGamma) td.Add((x => x.Drag + x.WSin, new Color(1f, 0.6f, 0.2f, 0.9f), 1.2f * u));   // drag + W·sinγ: what thrust balances on a slope
            Lane(0, "T · D", FfThrust, td.ToArray(), W * 0.02, $"{(last.Thrust - last.Drag - last.WSin) * 0.2248:+0;-0}");
            Lane(1, "L · W", FfLift, new (System.Func<WidgetForceTrend.Sample, double>, Color, float)[] { (x => x.W, FfWeight, 2f * u), (x => x.Lift, FfLift, 2f * u) }, W * 0.06, $"{last.Lift / System.Math.Max(1, W):0.00} W");
            Lane(2, "α", Color.white, new (System.Func<WidgetForceTrend.Sample, double>, Color, float)[] { (x => x.Alpha, Color.white, 2f * u) }, 2, $"{last.Alpha:0.0}°");
            Lane(3, "PATH γ", new Color(0.3f, 0.95f, 1f), new (System.Func<WidgetForceTrend.Sample, double>, Color, float)[] { (x => x.Gamma, new Color(0.3f, 0.95f, 1f), 2f * u) }, 2, $"{last.Gamma:+0.0;-0.0}°", 0);
            Lane(4, "KIAS", new Color(1f, 0.82f, 0.2f), new (System.Func<WidgetForceTrend.Sample, double>, Color, float)[] { (x => x.Kias, new Color(1f, 0.82f, 0.2f), 2f * u) }, 10, $"{last.Kias:0}");
        }
    }
}

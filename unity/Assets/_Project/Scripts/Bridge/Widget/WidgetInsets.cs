using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// INSETS (protocol 8) — small plates in the picture's top-right corner, each a curve from the sim's own aero model with a
    /// live dot: CL–α (the wing responds to angle of attack), L/D–α (best glide), power required vs airspeed (the back side),
    /// the slip/skid BALL, the AoA gauge, and the weight-and-balance envelope. {"cmd":"inset","name","show"}.
    /// </summary>
    public sealed partial class WidgetControlsDisplay
    {
        public static readonly string[] InsetNames = { "clAlpha", "liftDrag", "powerRequired", "ball", "aoa", "wb" };

        private void Insets(AeroWidget w, Rect scene)
        {
            var on = new List<string>();
            foreach (var n in InsetNames) if (w.Insets.Contains(n)) on.Add(n);
            if (on.Count == 0) return;
            float iw = 300f, ih = 176f, gap = 10f;
            float x = scene.xMax - iw - 14f, y = scene.yMax - 54f;   // top-right, below the frame's top edge (pixels, y up)
            foreach (var n in on)
            {
                if (y - ih < scene.yMin + 10) { y = scene.yMax - 54f; x -= iw + gap; }
                var r = new Rect(x, y - ih, iw, ih);
                PxPlate(r);
                switch (n)
                {
                    case "clAlpha": CurveInset(r, "LIFT COEFFICIENT vs α", w.Curves.ClAlpha(w), w.Curves.NowAlpha, w.Curves.NowCl, "α°", "CL", w.CriticalAlphaDeg(w.Ac.FlapFraction)); break;
                    case "liftDrag": CurveInset(r, "LIFT / DRAG vs α", w.Curves.LdAlpha(w), w.Curves.NowAlpha, w.Curves.NowLd, "α°", "L/D", double.NaN); break;
                    case "powerRequired": CurveInset(r, "POWER REQUIRED (level)", w.Curves.PowerReq(w), w.Read().Kias, w.Curves.NowPowerReqHp(w), "KIAS", "HP", double.NaN, w.Curves.PowerAvailHp(w)); break;
                    case "ball": Ball(r, w); break;
                    case "aoa": AoaInset(r, w); break;
                    case "wb": WbInset(r, w); break;
                }
                y -= ih + gap;
            }
        }

        private void PxPlate(Rect r)
        {
            var bg = new Color(0, 0, 0, 0.55f);
            Quad(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), bg);
            var rule = new Color(0.85f, 0.87f, 0.9f, 0.55f);
            PxSeg(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), 1.2f, rule); PxSeg(new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), 1.2f, rule);
            PxSeg(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), 1.2f, rule); PxSeg(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), 1.2f, rule);
        }

        private void CurveInset(Rect r, string title, List<(double x, double y)> pts, double nowX, double nowY, string xl, string yl, double redX, double hLine = double.NaN)
        {
            PxText(new Vector2(r.xMin + 10, r.yMax - 14), title, 15f, Color.white, TextAnchor.MiddleLeft);
            if (pts == null || pts.Count < 2) return;
            var plot = new Rect(r.xMin + 40, r.yMin + 26, r.width - 52, r.height - 54);
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            foreach (var p in pts) { x0 = System.Math.Min(x0, p.x); x1 = System.Math.Max(x1, p.x); y0 = System.Math.Min(y0, p.y); y1 = System.Math.Max(y1, p.y); }
            if (!double.IsNaN(hLine)) y1 = System.Math.Max(y1, hLine);
            y0 = System.Math.Min(0, y0); y1 += (y1 - y0) * 0.08;
            Vector2 M(double px, double py) => new(plot.xMin + (float)((px - x0) / System.Math.Max(1e-9, x1 - x0)) * plot.width, plot.yMin + (float)((py - y0) / System.Math.Max(1e-9, y1 - y0)) * plot.height);
            var axis = new Color(1, 1, 1, 0.35f);
            PxSeg(new Vector2(plot.xMin, plot.yMin), new Vector2(plot.xMax, plot.yMin), 1f, axis); PxSeg(new Vector2(plot.xMin, plot.yMin), new Vector2(plot.xMin, plot.yMax), 1f, axis);
            if (y0 < 0) PxSeg(M(x0, 0), M(x1, 0), 1f, axis);
            if (!double.IsNaN(redX) && redX > x0 && redX < x1) PxSeg(M(redX, y0), M(redX, y1), 1.5f, new Color(0.9f, 0.15f, 0.1f, 0.9f));
            if (!double.IsNaN(hLine)) { PxSeg(M(x0, hLine), M(x1, hLine), 1.5f, new Color(0.3f, 0.8f, 0.4f, 0.9f)); PxText(M(x1, hLine) + new Vector2(-4, 9), "AVAILABLE", 13f, new Color(0.3f, 0.8f, 0.4f), TextAnchor.MiddleRight); }
            for (int i = 1; i < pts.Count; i++) PxSeg(M(pts[i - 1].x, pts[i - 1].y), M(pts[i].x, pts[i].y), 2.2f, new Color(0.55f, 0.85f, 1f));
            PxText(new Vector2(plot.xMin + plot.width * 0.5f, r.yMin + 12), $"{xl}  {x0:0} … {x1:0}", 13f, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter);
            PxText(new Vector2(r.xMin + 6, plot.yMax - 6), $"{yl} {y1:0.#}", 13f, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleLeft);
            if (!double.IsNaN(nowX) && !double.IsNaN(nowY) && nowX >= x0 && nowX <= x1)
            {
                var p = M(nowX, System.Math.Clamp(nowY, y0, y1));
                var dot = new Color(1f, 0.82f, 0.2f);
                for (int i = 0; i < 12; i++) { float a0 = i * Mathf.PI / 6f, a1 = (i + 1) * Mathf.PI / 6f; Quad(p, p + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 6f, p + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 6f, p + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 6f, dot); }
                PxText(p + new Vector2(10, 12), $"{nowY:0.##}", 14f, dot, TextAnchor.MiddleLeft);
            }
        }

        private void Ball(Rect r, AeroWidget w)
        {
            PxText(new Vector2(r.xMin + 10, r.yMax - 14), "SLIP / SKID", 15f, Color.white, TextAnchor.MiddleLeft);
            Vector2 c = new(r.center.x, r.center.y - 4);
            float R = 120f, sweep = 22f * Mathf.Deg2Rad;
            var tube = new Color(1, 1, 1, 0.5f);
            Vector2 Arc(float a, float rr) => c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * rr + new Vector2(0, R - 30);
            for (int i = 0; i < 20; i++) { float a0 = -sweep + 2 * sweep * i / 20f, a1 = -sweep + 2 * sweep * (i + 1) / 20f; PxSeg(Arc(a0, R + 13), Arc(a1, R + 13), 1.5f, tube); PxSeg(Arc(a0, R - 13), Arc(a1, R - 13), 1.5f, tube); }
            PxSeg(Arc(-0.06f, R - 14), Arc(-0.06f, R + 14), 1.5f, tube); PxSeg(Arc(0.06f, R - 14), Arc(0.06f, R + 14), 1.5f, tube);
            float ballA = Mathf.Clamp((float)w.BallDeg * Mathf.Deg2Rad, -sweep, sweep);
            var b = Arc(ballA, R);
            for (int i = 0; i < 16; i++) { float a0 = i * Mathf.PI / 8f, a1 = (i + 1) * Mathf.PI / 8f; Quad(b, b + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 11f, b + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 11f, b + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 11f, Color.white); }
            var rd = w.Read();
            PxText(new Vector2(r.center.x, r.yMin + 16), $"β {rd.BetaDeg:+0.0;-0.0;0.0}°   BALL {(System.Math.Abs(w.BallDeg) < 0.5 ? "CENTRED" : w.BallDeg > 0 ? "RIGHT" : "LEFT")}", 14f, Color.white, TextAnchor.MiddleCenter);
        }

        private void AoaInset(Rect r, AeroWidget w)
        {
            var rd = w.Read(); double crit = w.CriticalAlphaDeg(w.Ac.FlapFraction), warn = crit - 3;
            PxText(new Vector2(r.xMin + 10, r.yMax - 14), "ANGLE OF ATTACK", 15f, Color.white, TextAnchor.MiddleLeft);
            var bar = new Rect(r.xMin + 20, r.center.y - 14, r.width - 40, 28);
            float X(double a) => bar.xMin + (float)((System.Math.Clamp(a, -5, 35) + 5) / 40.0) * bar.width;
            PxSeg(new Vector2(bar.xMin, bar.center.y), new Vector2(bar.xMax, bar.center.y), 2f, new Color(1, 1, 1, 0.5f));
            Quad(new Vector2(X(warn), bar.yMin), new Vector2(X(crit), bar.yMin), new Vector2(X(crit), bar.yMax), new Vector2(X(warn), bar.yMax), new Color(0.95f, 0.65f, 0.1f, 0.9f));
            Quad(new Vector2(X(crit), bar.yMin), new Vector2(X(35), bar.yMin), new Vector2(X(35), bar.yMax), new Vector2(X(crit), bar.yMax), new Color(0.85f, 0.1f, 0.08f, 0.9f));
            float px = X(rd.AlphaDeg);
            PxSeg(new Vector2(px, bar.yMin - 8), new Vector2(px, bar.yMax + 8), 4f, Color.white);
            PxText(new Vector2(r.center.x, r.yMin + 18), $"α {rd.AlphaDeg:0.0}°   CRITICAL {crit:0}°", 15f, rd.AlphaDeg >= crit ? new Color(1, 0.3f, 0.3f) : Color.white, TextAnchor.MiddleCenter);
        }

        private void WbInset(Rect r, AeroWidget w)
        {
            var L = w.Loading;
            PxText(new Vector2(r.xMin + 10, r.yMax - 14), "WEIGHT & BALANCE", 15f, Color.white, TextAnchor.MiddleLeft);
            var plot = new Rect(r.xMin + 44, r.yMin + 30, r.width - 60, r.height - 60);
            double c0 = L.FwdLimitMac - 12, c1 = L.NpMac + 6, w0 = L.EmptyKg * 0.9, w1 = L.MaxGrossKg * 1.35;
            Vector2 M(double cg, double kg) => new(plot.xMin + (float)((cg - c0) / (c1 - c0)) * plot.width, plot.yMin + (float)((kg - w0) / (w1 - w0)) * plot.height);
            var box = new Color(0.3f, 0.8f, 0.4f, 0.9f);
            Vector2 a = M(L.FwdLimitMac, L.EmptyKg), b = M(L.AftLimitMac, L.EmptyKg), c = M(L.AftLimitMac, L.MaxGrossKg), d = M(L.FwdLimitMac, L.MaxGrossKg);
            PxSeg(a, b, 1.6f, box); PxSeg(b, c, 1.6f, box); PxSeg(c, d, 1.6f, box); PxSeg(d, a, 1.6f, box);
            var np = new Color(0.9f, 0.15f, 0.1f, 0.9f);
            PxSeg(M(L.NpMac, w0), M(L.NpMac, w1), 1.5f, np); PxText(M(L.NpMac, w1) + new Vector2(0, -8), "NP", 13f, np, TextAnchor.MiddleCenter);
            var p = M(L.CgMac, L.Kg);
            Color dot = L.WithinLimits ? new Color(1f, 0.82f, 0.2f) : new Color(1f, 0.6f, 0.1f);
            for (int i = 0; i < 12; i++) { float a0 = i * Mathf.PI / 6f, a1 = (i + 1) * Mathf.PI / 6f; Quad(p, p + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 7f, p + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 7f, p + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 7f, dot); }
            PxText(new Vector2(r.center.x, r.yMin + 14), $"{L.Kg * 2.20462:0} LB · CG {L.CgMac:0.0} % MAC · SM {L.StaticMarginMac:0.0} %{(L.WithinLimits ? "" : " · OUT")}", 13f, L.WithinLimits ? Color.white : new Color(1f, 0.65f, 0.15f), TextAnchor.MiddleCenter);
        }
    }

    /// <summary>The insets' curves, from the sim's aero model (cached per type, flaps and weight) and the live point.</summary>
    public sealed class WidgetCurves
    {
        private readonly Dictionary<string, List<(double, double)>> _cache = new();
        public double NowAlpha, NowCl, NowLd = double.NaN;

        private static double WingArea(FlyingGame.Core.DataContracts.AircraftConfig cfg)
        {
            double s = 0; foreach (var sf in cfg.Surfaces) if (sf.Id.ToLowerInvariant().Contains("wing")) foreach (var st in sf.Strips) s += st.Area; return s;
        }

        private List<(double, double)> Sweep(AeroWidget w, bool ld)
        {
            string key = $"{(ld ? "ld" : "cl")}|{w.AircraftId}|{System.Math.Round(w.Ac.FlapFraction, 2)}";
            if (_cache.TryGetValue(key, out var v)) return v;
            var cfg = w.Config; var tables = Aircraft.BuildAirfoilTables(cfg); double S = WingArea(cfg), V = 40, q = 0.5 * 1.225 * V * V;
            v = new List<(double, double)>();
            for (double a = -5; a <= 30; a += 0.5)
            {
                double ar = a * System.Math.PI / 180;
                var (F, _) = FlyingGame.Core.Aero.AeroModel.Compute(cfg, tables, new Vec3(V * System.Math.Cos(ar), 0, V * System.Math.Sin(ar)), Vec3.Zero, Vec3.Zero, 1.225,
                    new FlyingGame.Core.Aero.ControlDeflections(0, 0, 0, 0, w.Ac.FlapFraction));
                double L = F.X * System.Math.Sin(ar) - F.Z * System.Math.Cos(ar), D = -(F.X * System.Math.Cos(ar) + F.Z * System.Math.Sin(ar));
                v.Add((a, ld ? (D > 1 ? L / D : 0) : L / (q * S)));
            }
            _cache[key] = v; return v;
        }
        public List<(double, double)> ClAlpha(AeroWidget w) => Sweep(w, false);
        public List<(double, double)> LdAlpha(AeroWidget w) => Sweep(w, true);

        /// <summary>Thrust power (hp) needed for level flight vs KIAS: D·V from the glide trim's L/D at each speed.</summary>
        public List<(double, double)> PowerReq(AeroWidget w)
        {
            string key = $"pr|{w.AircraftId}|{System.Math.Round(w.Ac.FlapFraction, 2)}|{w.Config.Mass.MassKg:0}";
            if (_cache.TryGetValue(key, out var v)) return v;
            var cfg = w.Config; double alt = 900, vso = PracticeScenario.EstimateVso(cfg, alt, w.Ac.FlapFraction), W = cfg.Mass.MassKg * 9.81;
            double rhoR = System.Math.Sqrt(Atmosphere.DensityAtAltitude(alt) / 1.225);
            v = new List<(double, double)>();
            for (double sp = vso * 1.02; sp <= vso * 3.2; sp += vso * 0.06)
            {
                var t = TrimSolver.SolveGliderTrim(cfg, sp, alt, flapFraction: w.Ac.FlapFraction);
                if (!t.Converged || t.GlideRatio <= 0) continue;
                v.Add((sp * rhoR * 1.943844, W / t.GlideRatio * sp / 745.7));
            }
            _cache[key] = v; return v;
        }
        public double NowPowerReqHp(AeroWidget w)
        {
            var pr = PowerReq(w); double k = w.Read().Kias;
            for (int i = 1; i < pr.Count; i++) if (pr[i].Item1 >= k) { double t = (k - pr[i - 1].Item1) / System.Math.Max(1e-6, pr[i].Item1 - pr[i - 1].Item1); return pr[i - 1].Item2 + (pr[i].Item2 - pr[i - 1].Item2) * t; }
            return double.NaN;
        }
        public double PowerAvailHp(AeroWidget w)
        {
            var p = w.Config.Propulsion; if (p == null || p.MaxPowerW <= 0) return double.NaN;
            double eff = p.Efficiency > 0 ? p.Efficiency : 0.75;
            return p.MaxPowerW * eff * System.Math.Max(1, w.Config.Engines.Count) / 745.7;
        }

        /// <summary>The live point: CL and L/D now, from the aero forces of the frame shown.</summary>
        public void Update(AeroWidget w, WidgetVectors.Picture pic)
        {
            var rd = w.Read(); NowAlpha = rd.AlphaDeg;
            if (pic == null || rd.Q < 1) { NowCl = double.NaN; NowLd = double.NaN; return; }
            double S = WingArea(w.Config);
            NowCl = pic.LiftN / (rd.Q * S); NowLd = pic.DragN > 1 ? pic.LiftN / pic.DragN : double.NaN;
        }
    }
}
